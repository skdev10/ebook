#!/usr/bin/env bash
# Fix live API/session failures after redeploy (invalid auth cookies).
# Run on server: cd /root/latest/EbookAI && git pull origin Clean_Code && bash deploy/fix-live-apis.sh
set -euo pipefail

APP_DIR="${APP_DIR:-/root/latest/EbookAI}"
OUT_DIR="${OUT_DIR:-publish}"
PORT="${PORT:-5000}"
ENV_FILE="${ENV_FILE:-/etc/default/ebookai}"

cd "$APP_DIR"
chmod +x deploy/link-persistent.sh
bash deploy/link-persistent.sh "$APP_DIR" "$OUT_DIR"

if [[ -f "$ENV_FILE" ]]; then
  set -a
  # shellcheck disable=SC1090
  source "$ENV_FILE"
  set +a
fi
export EBOOKAI_PERSIST_DIR="${EBOOKAI_PERSIST_DIR:-$APP_DIR/persistent}"

if command -v ss >/dev/null 2>&1; then
  OLD_PID=$(ss -tlnp "sport = :$PORT" 2>/dev/null | awk 'NR>1 {print $NF}' | sed -n 's/.*pid=\([0-9]*\).*/\1/p' | head -1)
elif command -v netstat >/dev/null 2>&1; then
  OLD_PID=$(netstat -tpln 2>/dev/null | awk -v p=":$PORT" '$4 ~ p {print $7}' | sed 's/.*\///' | cut -d, -f1 | head -1)
fi
if [[ -n "${OLD_PID:-}" && "$OLD_PID" =~ ^[0-9]+$ ]]; then
  echo "Restarting PID $OLD_PID"
  kill -9 "$OLD_PID" || true
  sleep 2
fi

cd "$APP_DIR/$OUT_DIR"
export ASPNETCORE_ENVIRONMENT="${ASPNETCORE_ENVIRONMENT:-Production}"
export ASPNETCORE_URLS="${ASPNETCORE_URLS:-http://0.0.0.0:${PORT}}"
if [[ -x ./EBookDashboard ]]; then
  nohup ./EBookDashboard --urls "http://0.0.0.0:${PORT}" >> "$APP_DIR/nohup.out" 2>&1 &
else
  nohup dotnet EBookDashboard.dll --urls "http://0.0.0.0:${PORT}" >> "$APP_DIR/nohup.out" 2>&1 &
fi

sleep 5
curl -sf "http://127.0.0.1:${PORT}/health" | head -c 400
echo ""
echo "API fix applied. Users may need to log in once after this restart."
