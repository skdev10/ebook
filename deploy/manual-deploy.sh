#!/usr/bin/env bash
# EbookAI manual deploy — matches /root/latest/EbookAI workflow.
# Usage: cd /root/latest/EbookAI && bash deploy/manual-deploy.sh
set -euo pipefail

APP_DIR="${APP_DIR:-/root/latest/EbookAI}"
BRANCH="${BRANCH:-Clean_Code}"
PORT="${PORT:-5000}"
ENV_FILE="${ENV_FILE:-/etc/default/ebookai}"

cd "$APP_DIR"

echo "==> [1/6] Git branch + pull"
git branch
git checkout "$BRANCH"
git pull origin "$BRANCH"
echo "    HEAD: $(git log -1 --oneline)"

echo "==> [2/5] Chromium for PDF export (interior PDF needs a headless browser)"
if ! command -v google-chrome-stable >/dev/null 2>&1 \
   && ! command -v google-chrome >/dev/null 2>&1 \
   && ! command -v chromium-browser >/dev/null 2>&1 \
   && ! command -v chromium >/dev/null 2>&1; then
  echo "    No system Chrome/Chromium found — installing chromium-browser (apt) if available…"
  if command -v apt-get >/dev/null 2>&1; then
    apt-get update -qq && apt-get install -y -qq chromium-browser || apt-get install -y -qq chromium || true
  fi
fi
if command -v google-chrome-stable >/dev/null 2>&1; then
  echo "    Using $(command -v google-chrome-stable)"
elif command -v chromium-browser >/dev/null 2>&1; then
  echo "    Using $(command -v chromium-browser)"
elif command -v chromium >/dev/null 2>&1; then
  echo "    Using $(command -v chromium)"
else
  echo "    WARNING: Install Chrome/Chromium or set Puppeteer__ExecutablePath in /etc/default/ebookai"
fi

echo "==> [3/5] Publish (linux-x64 self-contained)"
dotnet publish newEbook.csproj -c Release -r linux-x64 --self-contained true -maxcpucount:1 -o publish

echo "==> [3b/5] Link persistent uploads + session/auth keys"
chmod +x deploy/link-persistent.sh
bash deploy/link-persistent.sh "$APP_DIR" publish

echo "==> [4/5] Load environment"
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
if command -v netstat >/dev/null 2>&1; then
  OLD_PID=$(netstat -tpln 2>/dev/null | awk -v p=":$PORT" '$4 ~ p {print $7}' | sed 's/.*\///' | cut -d, -f1 | head -1)
elif command -v ss >/dev/null 2>&1; then
  OLD_PID=$(ss -tlnp "sport = :$PORT" 2>/dev/null | awk 'NR>1 {print $NF}' | sed -n 's/.*pid=\([0-9]*\).*/\1/p' | head -1)
fi
if [[ -n "${OLD_PID:-}" && "$OLD_PID" =~ ^[0-9]+$ ]]; then
  echo "    kill -9 $OLD_PID"
  kill -9 "$OLD_PID" || true
fi
fuser -k "${PORT}/tcp" 2>/dev/null || true
sleep 2

echo "==> [6/6] Start app (nohup)"
cd "$APP_DIR/publish"
: > "$APP_DIR/nohup.out"
if [[ -x ./EBookDashboard ]]; then
  nohup ./EBookDashboard --urls "http://0.0.0.0:${PORT}" >> "$APP_DIR/nohup.out" 2>&1 &
else
  nohup dotnet EBookDashboard.dll --urls "http://0.0.0.0:${PORT}" >> "$APP_DIR/nohup.out" 2>&1 &
fi
APP_PID=$!
echo "    Started PID $APP_PID"

echo "==> Health check (retry up to 60s — app can take 10–20s on cold start)"
HEALTH_OK=0
for i in $(seq 1 30); do
  if curl -sf "http://127.0.0.1:${PORT}/health" | head -c 500; then
    echo ""
    HEALTH_OK=1
    break
  fi
  if ! kill -0 "$APP_PID" 2>/dev/null; then
    echo "    Process $APP_PID exited before health check passed."
    break
  fi
  sleep 2
done

if [[ "$HEALTH_OK" -ne 1 ]]; then
  echo "Health check failed — diagnostics:"
  ps -p "$APP_PID" -o pid,cmd 2>/dev/null || echo "    Process not running."
  netstat -tpln 2>/dev/null | grep ":$PORT" || ss -tlnp 2>/dev/null | grep ":$PORT" || echo "    Nothing listening on :$PORT"
  echo "    Last log lines:"
  tail -n 60 "$APP_DIR/nohup.out" 2>/dev/null || true
  exit 1
fi
echo ""
echo "Deploy complete → http://138.197.76.70:${PORT}"
