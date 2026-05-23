#!/usr/bin/env bash
# Deploy EBookDashboard on Linux (e.g. /opt/EbookAI)
# Usage: cd /opt/EbookAI && bash scripts/deploy-linux.sh

set -euo pipefail

APP_DIR="${APP_DIR:-/opt/EbookAI}"
PORT="${PORT:-5000}"
CSPROJ="EBookDashboard.csproj"
MAIN_DLL="EBookDashboard.dll"
PUBLISH_DIR="publish"

cd "$APP_DIR"

echo "==> Backup appsettings (do not lose production DB secrets)"
if [[ -f appsettings.json ]]; then
  cp -a appsettings.json "appsettings.json.bak.$(date +%Y%m%d%H%M%S)"
fi
if [[ -f publish/appsettings.json ]]; then
  cp -a publish/appsettings.json "publish.appsettings.json.bak.$(date +%Y%m%d%H%M%S)" 2>/dev/null || true
fi

echo "==> Stop old process on port $PORT"
if command -v ss >/dev/null 2>&1; then
  OLD_PID=$(ss -tlnp "sport = :$PORT" 2>/dev/null | awk 'NR>1 {print $NF}' | sed -n 's/.*pid=\([0-9]*\).*/\1/p' | head -1)
elif command -v netstat >/dev/null 2>&1; then
  OLD_PID=$(netstat -tpln 2>/dev/null | awk -v p=":$PORT" '$4 ~ p {print $7}' | sed 's/.*\///' | cut -d, -f1 | head -1)
fi
if [[ -n "${OLD_PID:-}" && "$OLD_PID" =~ ^[0-9]+$ ]]; then
  echo "Killing PID $OLD_PID"
  kill -9 "$OLD_PID" || true
  sleep 2
fi

echo "==> Publish (project file, not solution)"
dotnet publish "$CSPROJ" -c Release -r linux-x64 --self-contained true -maxcpucount:1 -o "$PUBLISH_DIR"

if [[ ! -f "$PUBLISH_DIR/$MAIN_DLL" ]]; then
  echo "ERROR: $PUBLISH_DIR/$MAIN_DLL not found. Files in publish:"
  ls -la "$PUBLISH_DIR" | head -30
  exit 1
fi

# Restore production appsettings if pull overwrote placeholders
if [[ -f appsettings.json.bak.* ]]; then
  LATEST_BAK=$(ls -t appsettings.json.bak.* 2>/dev/null | head -1)
  if grep -q 'YOUR_DB_HOST' appsettings.json 2>/dev/null && [[ -n "$LATEST_BAK" ]]; then
    echo "==> Restoring appsettings from $LATEST_BAK (repo has placeholders)"
    cp -a "$LATEST_BAK" appsettings.json
  fi
fi
cp -a appsettings.json "$PUBLISH_DIR/appsettings.json"

export ASPNETCORE_ENVIRONMENT=Production
export ASPNETCORE_URLS="http://0.0.0.0:${PORT}"

echo "==> Smoke test (foreground, 8s) — errors show here"
cd "$PUBLISH_DIR"
timeout 8 dotnet "$MAIN_DLL" --urls "http://0.0.0.0:${PORT}" || true
cd "$APP_DIR"

echo "==> Start with nohup"
nohup dotnet "$PUBLISH_DIR/$MAIN_DLL" --urls "http://0.0.0.0:${PORT}" > nohup.out 2>&1 &
sleep 3

echo "==> Listen check"
ss -tlnp | grep ":$PORT" || netstat -tpln | grep ":$PORT" || { echo "Nothing listening on $PORT"; tail -50 nohup.out; exit 1; }

echo "==> Local HTTP check"
curl -s -o /dev/null -w "HTTP %{http_code}\n" "http://127.0.0.1:${PORT}/" || true

echo "Done. Tail logs: tail -f $APP_DIR/nohup.out"
echo "Open: http://$(curl -s ifconfig.me 2>/dev/null || hostname -I | awk '{print $1}'):${PORT}"
