#!/usr/bin/env bash
# Pull latest Clean_Code, publish to app-out, restart app on DigitalOcean droplet.
# Run on server: cd /opt/EbookAI && bash deploy/do-deploy.sh
set -euo pipefail

APP_DIR="${APP_DIR:-/opt/EbookAI}"
BRANCH="${BRANCH:-Clean_Code}"
OUT_DIR="${OUT_DIR:-app-out}"
ENV_FILE="${ENV_FILE:-/etc/default/ebookai}"

cd "$APP_DIR"
# shellcheck disable=SC1091
source deploy/resolve-app-ports.sh
PORT="${KESTREL_PORT}"
PUBLIC_PORT="${PUBLIC_PORT:-5000}"

echo "==> Git pull ($BRANCH)"
git fetch origin "$BRANCH"
git checkout "$BRANCH"
# Server-side edits to deploy/*.sh often block pull — always take remote deploy/ before merge.
if [[ -n "$(git status --porcelain deploy/ 2>/dev/null || true)" ]]; then
  echo "    Resetting local deploy/ changes so pull can proceed..."
fi
git checkout "origin/${BRANCH}" -- deploy/ 2>/dev/null \
  || git checkout -- deploy/ 2>/dev/null \
  || git restore --source=HEAD --staged --worktree deploy/ 2>/dev/null \
  || true
if ! git pull --ff-only origin "$BRANCH" 2>/dev/null; then
  echo "    Pull still blocked — force-syncing deploy/ from origin/${BRANCH} and retrying..."
  git fetch origin "$BRANCH"
  git checkout "origin/${BRANCH}" -- deploy/
  git pull --ff-only origin "$BRANCH"
fi
echo "    HEAD: $(git log -1 --oneline)"

# Fix /etc/default/ebookai before any script sources it (unquoted spaces break bash).
if [[ -f "${APP_DIR}/deploy/lib-env.sh" ]]; then
  # shellcheck disable=SC1091
  source "${APP_DIR}/deploy/lib-env.sh"
  fix_env_file_syntax "${ENV_FILE}"
fi

echo "==> External API env"
chmod +x deploy/ensure-external-api-env.sh 2>/dev/null || true
if [[ -f deploy/ensure-external-api-env.sh ]]; then
  bash deploy/ensure-external-api-env.sh
fi

echo "==> Email SMTP env"
chmod +x deploy/ensure-email-env.sh deploy/set-smtp-credentials.sh 2>/dev/null || true
if [[ -n "${GMAIL_USER:-}" && -n "${GMAIL_APP_PASSWORD:-}" && -f deploy/set-smtp-credentials.sh ]]; then
  echo "    Applying GMAIL_USER from environment..."
  bash deploy/set-smtp-credentials.sh "$GMAIL_USER" "$GMAIL_APP_PASSWORD"
fi
if [[ -f deploy/ensure-email-env.sh ]]; then
  bash deploy/ensure-email-env.sh
fi

echo "==> Users DB schema (HasCompletedTour for signup)"
chmod +x deploy/ensure-users-schema.sh 2>/dev/null || true
if [[ -f deploy/ensure-users-schema.sh ]]; then
  bash deploy/ensure-users-schema.sh
fi

echo "==> Publish"
chmod +x deploy/vm-deploy.sh
DEPLOY_SKIP_GIT=1 BRANCH="$BRANCH" OUT_DIR="$OUT_DIR" ./deploy/vm-deploy.sh

if [[ -f "$ENV_FILE" ]]; then
  if [[ -f "${APP_DIR}/deploy/lib-env.sh" ]]; then
    # shellcheck disable=SC1091
    source "${APP_DIR}/deploy/lib-env.sh"
    safe_source_env "$ENV_FILE"
  else
    set -a
    # shellcheck disable=SC1090
    source "$ENV_FILE"
    set +a
  fi
fi
export ASPNETCORE_ENVIRONMENT="${ASPNETCORE_ENVIRONMENT:-Production}"
export ASPNETCORE_URLS="${ASPNETCORE_URLS:-http://0.0.0.0:${PORT}}"

echo "==> Restart"
if systemctl list-unit-files ebookai.service >/dev/null 2>&1 && systemctl is-enabled ebookai.service >/dev/null 2>&1; then
  systemctl daemon-reload
  systemctl restart ebookai
  sleep 2
  systemctl status ebookai --no-pager -l || true
else
  chmod +x deploy/start-app-out.sh 2>/dev/null || true
  bash deploy/start-app-out.sh
  sleep 2
fi

HEALTH_PORT="$PUBLIC_PORT"
if command -v nginx >/dev/null 2>&1; then
  echo "==> Nginx AI timeout patch (before health — public :${PUBLIC_PORT} → Kestrel :${PORT})"
  chmod +x deploy/apply-nginx-timeouts.sh 2>/dev/null || true
  if [[ -f deploy/apply-nginx-timeouts.sh ]]; then
    bash deploy/apply-nginx-timeouts.sh || echo "    nginx patch failed — run: sudo bash deploy/apply-nginx-timeouts.sh"
  fi
fi

echo "==> Health check (retry up to 60s) on :${HEALTH_PORT}"
HEALTH_OK=0
for _ in $(seq 1 30); do
  if curl -sf "http://127.0.0.1:${HEALTH_PORT}/health" | head -c 400; then
    echo ""
    HEALTH_OK=1
    break
  fi
  sleep 2
done
if [[ "$HEALTH_OK" -ne 1 ]]; then
  if [[ "$HEALTH_PORT" != "$PORT" ]]; then
    echo "    Retrying health on Kestrel :${PORT}..."
    if curl -sf "http://127.0.0.1:${PORT}/health" | head -c 400; then
      echo ""
      HEALTH_OK=1
    fi
  fi
fi
if [[ "$HEALTH_OK" -ne 1 ]]; then
  echo "Health check failed — last log lines:"
  tail -n 40 "$APP_DIR/nohup.out" 2>/dev/null || journalctl -u ebookai -n 40 --no-pager 2>/dev/null || true
  exit 1
fi

echo "==> API verify (fast endpoints)"
chmod +x deploy/verify-all-apis.sh 2>/dev/null || true
if [[ -f deploy/verify-all-apis.sh ]]; then
  bash deploy/verify-all-apis.sh || echo "    API verify had warnings — check ExternalApi__ApiKey and upstream 162.229.248.26:8001"
fi

echo "Deploy complete. App: http://138.197.76.70:${PUBLIC_PORT} (Kestrel :${PORT})"
