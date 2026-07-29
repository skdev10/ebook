#!/usr/bin/env bash
# EbookAI manual deploy — ALWAYS pulls from yitservices/EbookAI Clean_Code.
# Usage:
#   bash deploy/manual-deploy.sh
#   APP_DIR=/opt/EbookAI bash deploy/manual-deploy.sh
set -euo pipefail

# Prefer /root/latest/EbookAI; fall back to /opt/EbookAI if missing.
if [[ -z "${APP_DIR:-}" ]]; then
  if [[ -d /root/latest/EbookAI/.git ]]; then
    APP_DIR=/root/latest/EbookAI
  elif [[ -d /opt/EbookAI/.git ]]; then
    APP_DIR=/opt/EbookAI
  else
    APP_DIR=/root/latest/EbookAI
  fi
fi

BRANCH="${BRANCH:-Clean_Code}"
PORT="${PORT:-5000}"
ENV_FILE="${ENV_FILE:-/etc/default/ebookai}"
# Canonical remote with all UI fixes (NOT skdev10/yitEbook alone).
CANONICAL_URL="${CANONICAL_URL:-https://github.com/yitservices/EbookAI.git}"
EXPECTED_COMMIT="${EXPECTED_COMMIT:-7388182}"

cd "$APP_DIR"
echo "==> App dir: $APP_DIR"

echo "==> [1/6] Git: sync Clean_Code from yitservices/EbookAI"
git remote remove ebookai 2>/dev/null || true
git remote add ebookai "$CANONICAL_URL"
git fetch ebookai "$BRANCH"
git checkout -B "$BRANCH" "ebookai/$BRANCH"
git reset --hard "ebookai/$BRANCH"
HEAD_SHA=$(git rev-parse --short HEAD)
echo "    HEAD: $(git log -1 --oneline)"
if [[ "$HEAD_SHA" != "$EXPECTED_COMMIT"* ]] && [[ "$(git rev-parse HEAD)" != "$(git rev-parse ebookai/$BRANCH)" ]]; then
  echo "WARNING: HEAD does not match ebookai/$BRANCH — aborting"
  exit 1
fi
# Soft check: warn if older than expected tip (still OK if tip moved forward)
if ! git merge-base --is-ancestor "$EXPECTED_COMMIT" HEAD 2>/dev/null; then
  echo "WARNING: expected commit $EXPECTED_COMMIT not in history — confirm remotes"
  git log -3 --oneline
fi

echo "==> [2/6] Chromium for PDF export"
if ! command -v google-chrome-stable >/dev/null 2>&1 \
   && ! command -v google-chrome >/dev/null 2>&1 \
   && ! command -v chromium-browser >/dev/null 2>&1 \
   && ! command -v chromium >/dev/null 2>&1; then
  if command -v apt-get >/dev/null 2>&1; then
    apt-get update -qq && apt-get install -y -qq chromium-browser || apt-get install -y -qq chromium || true
  fi
fi

echo "==> [3/6] Publish (linux-x64 self-contained)"
dotnet publish newEbook.csproj -c Release -r linux-x64 --self-contained true -maxcpucount:1 -o publish

echo "==> [3b/6] Link persistent uploads"
if [[ -f deploy/link-persistent.sh ]]; then
  chmod +x deploy/link-persistent.sh
  bash deploy/link-persistent.sh "$APP_DIR" publish
fi

echo "==> [4/6] Load environment"
if [[ -f "$ENV_FILE" ]]; then
  set -a
  # shellcheck disable=SC1090
  source "$ENV_FILE"
  set +a
else
  echo "WARNING: $ENV_FILE not found — using appsettings only"
fi
export ASPNETCORE_ENVIRONMENT="${ASPNETCORE_ENVIRONMENT:-Production}"
export ASPNETCORE_URLS="${ASPNETCORE_URLS:-http://0.0.0.0:${PORT}}"
export EBOOKAI_PERSIST_DIR="${EBOOKAI_PERSIST_DIR:-$APP_DIR/persistent}"

echo "==> [5/6] Stop process on port $PORT"
fuser -k "${PORT}/tcp" 2>/dev/null || true
pkill -f 'EBookDashboard' 2>/dev/null || true
sleep 2

echo "==> [6/6] Start app"
cd "$APP_DIR/publish"
: > "$APP_DIR/nohup.out"
if [[ -x ./EBookDashboard ]]; then
  nohup ./EBookDashboard --urls "http://0.0.0.0:${PORT}" >> "$APP_DIR/nohup.out" 2>&1 &
elif [[ -x ./newEbook ]]; then
  nohup ./newEbook --urls "http://0.0.0.0:${PORT}" >> "$APP_DIR/nohup.out" 2>&1 &
else
  nohup /usr/bin/dotnet EBookDashboard.dll --urls "http://0.0.0.0:${PORT}" >> "$APP_DIR/nohup.out" 2>&1 &
fi
APP_PID=$!
echo "    Started PID $APP_PID"

echo "==> Health check"
HEALTH_OK=0
for i in $(seq 1 30); do
  if curl -sf "http://127.0.0.1:${PORT}/health" >/dev/null; then
    curl -sf "http://127.0.0.1:${PORT}/health" | head -c 400 || true
    echo ""
    HEALTH_OK=1
    break
  fi
  if ! kill -0 "$APP_PID" 2>/dev/null; then
    echo "    Process $APP_PID exited early."
    break
  fi
  sleep 2
done

echo "    Deployed commit: $(git -C "$APP_DIR" log -1 --oneline)"
if [[ "$HEALTH_OK" -ne 1 ]]; then
  echo "Health check failed — last log:"
  tail -n 60 "$APP_DIR/nohup.out" 2>/dev/null || true
  exit 1
fi
echo "Deploy complete → http://138.197.76.70:${PORT}"
echo "Browser: hard refresh (Ctrl+F5)"
