#!/usr/bin/env bash
# Pull latest Clean_Code, publish to app-out, restart app on DigitalOcean droplet.
# Run on server: cd /opt/EbookAI && bash deploy/do-deploy.sh
set -euo pipefail

APP_DIR="${APP_DIR:-/opt/EbookAI}"
BRANCH="${BRANCH:-Clean_Code}"
OUT_DIR="${OUT_DIR:-app-out}"
PORT="${PORT:-5000}"
ENV_FILE="${ENV_FILE:-/etc/default/ebookai}"

cd "$APP_DIR"

echo "==> Git pull ($BRANCH)"
git fetch origin "$BRANCH"
git checkout "$BRANCH"
git pull origin "$BRANCH"
echo "    HEAD: $(git log -1 --oneline)"

echo "==> External API env"
chmod +x deploy/ensure-external-api-env.sh 2>/dev/null || true
if [[ -f deploy/ensure-external-api-env.sh ]]; then
  bash deploy/ensure-external-api-env.sh
fi

echo "==> Publish"
chmod +x deploy/vm-deploy.sh
DEPLOY_SKIP_GIT=1 BRANCH="$BRANCH" OUT_DIR="$OUT_DIR" ./deploy/vm-deploy.sh

if [[ -f "$ENV_FILE" ]]; then
  set -a
  # shellcheck disable=SC1090
  source "$ENV_FILE"
  set +a
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

echo "==> Health check (retry up to 60s)"
HEALTH_OK=0
for _ in $(seq 1 30); do
  if curl -sf "http://127.0.0.1:${PORT}/health" | head -c 400; then
    echo ""
    HEALTH_OK=1
    break
  fi
  sleep 2
done
if [[ "$HEALTH_OK" -ne 1 ]]; then
  echo "Health check failed — last log lines:"
  tail -n 40 "$APP_DIR/nohup.out" 2>/dev/null || journalctl -u ebookai -n 40 --no-pager 2>/dev/null || true
  exit 1
fi

if command -v nginx >/dev/null 2>&1; then
  echo "==> Nginx AI timeout patch (optional)"
  chmod +x deploy/apply-nginx-timeouts.sh 2>/dev/null || true
  if [[ -f deploy/apply-nginx-timeouts.sh ]]; then
    bash deploy/apply-nginx-timeouts.sh || echo "    nginx patch skipped (run: sudo bash deploy/apply-nginx-timeouts.sh)"
  fi
fi

echo "==> API verify (fast endpoints)"
chmod +x deploy/verify-all-apis.sh 2>/dev/null || true
if [[ -f deploy/verify-all-apis.sh ]]; then
  bash deploy/verify-all-apis.sh || echo "    API verify had warnings — check ExternalApi__ApiKey and upstream 162.229.248.26:8001"
fi

echo "Deploy complete. App: http://138.197.76.70:${PORT}"
