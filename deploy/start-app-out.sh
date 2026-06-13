#!/usr/bin/env bash
# Start published app with /etc/default/ebookai loaded (API key + DB + timeouts).
# Usage: cd /opt/EbookAI && bash deploy/start-app-out.sh
set -euo pipefail

APP_DIR="${APP_DIR:-/opt/EbookAI}"
OUT_DIR="${OUT_DIR:-app-out}"
PORT="${PORT:-5000}"
ENV_FILE="${ENV_FILE:-/etc/default/ebookai}"

cd "$APP_DIR"

if [[ -f "$ENV_FILE" ]]; then
  set -a
  # shellcheck disable=SC1090
  source "$ENV_FILE"
  set +a
else
  echo "WARNING: $ENV_FILE not found — ExternalApi__ApiKey must be set or APIs will fail."
fi

export ASPNETCORE_ENVIRONMENT="${ASPNETCORE_ENVIRONMENT:-Production}"
export ASPNETCORE_URLS="${ASPNETCORE_URLS:-http://0.0.0.0:${PORT}}"

if [[ ! -f "$APP_DIR/$OUT_DIR/EBookDashboard" && ! -f "$APP_DIR/$OUT_DIR/EBookDashboard.dll" ]]; then
  echo "ERROR: $APP_DIR/$OUT_DIR missing. Run: bash deploy/do-deploy.sh"
  exit 1
fi

if command -v ss >/dev/null 2>&1; then
  OLD_PID=$(ss -tlnp "sport = :$PORT" 2>/dev/null | awk 'NR>1 {print $NF}' | sed -n 's/.*pid=\([0-9]*\).*/\1/p' | head -1)
elif command -v netstat >/dev/null 2>&1; then
  OLD_PID=$(netstat -tpln 2>/dev/null | awk -v p=":$PORT" '$4 ~ p {print $7}' | sed 's/.*\///' | cut -d, -f1 | head -1)
fi
if [[ -n "${OLD_PID:-}" && "$OLD_PID" =~ ^[0-9]+$ ]]; then
  echo "Stopping PID $OLD_PID on port $PORT"
  kill -9 "$OLD_PID" || true
  sleep 2
fi

cd "$APP_DIR/$OUT_DIR"
if [[ -x ./EBookDashboard ]]; then
  nohup ./EBookDashboard --urls "http://0.0.0.0:${PORT}" >> "$APP_DIR/nohup.out" 2>&1 &
else
  nohup dotnet EBookDashboard.dll --urls "http://0.0.0.0:${PORT}" >> "$APP_DIR/nohup.out" 2>&1 &
fi
echo "Started PID $! — env from $ENV_FILE, port $PORT"
sleep 3
curl -sf "http://127.0.0.1:${PORT}/Books/ExternalApiStatus" | head -c 300 || echo "WARN: ExternalApiStatus not ready yet"
