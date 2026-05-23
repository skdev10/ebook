#!/usr/bin/env bash
# Start EBookDashboard with persisted secrets (Fawad workflow + env file).
# Usage on server:
#   1. Create /etc/default/ebookai (see /etc/default/ebookai.example)
#   2. cd /opt/EbookAI && bash deploy/start-nohup.sh

set -euo pipefail

APP_DIR="${APP_DIR:-/opt/EbookAI}"
PORT="${PORT:-5000}"
ENV_FILE="${ENV_FILE:-/etc/default/ebookai}"

cd "$APP_DIR"

if [[ -f "$ENV_FILE" ]]; then
  set -a
  # shellcheck disable=SC1090
  source "$ENV_FILE"
  set +a
else
  echo "WARNING: $ENV_FILE not found. ExternalApi__ApiKey must be set or app will crash in Production."
fi

export ASPNETCORE_ENVIRONMENT="${ASPNETCORE_ENVIRONMENT:-Production}"
export ASPNETCORE_URLS="${ASPNETCORE_URLS:-http://0.0.0.0:${PORT}}"

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

if [[ ! -f publish/EBookDashboard.dll ]]; then
  echo "ERROR: publish/EBookDashboard.dll missing. Run: dotnet publish newEbook.csproj -c Release -r linux-x64 --self-contained true -maxcpucount:1 -o publish"
  exit 1
fi

nohup dotnet publish/EBookDashboard.dll --urls "http://0.0.0.0:${PORT}" > nohup.out 2>&1 &
echo "Started PID $! on port $PORT"
sleep 2
netstat -tpln 2>/dev/null | grep ":$PORT" || ss -tlnp | grep ":$PORT" || true
echo "Logs: tail -f $APP_DIR/nohup.out"
echo "Health: curl -s http://127.0.0.1:${PORT}/health"
