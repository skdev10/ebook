#!/usr/bin/env bash
# EbookAI manual deploy — matches /root/latest/EbookAI workflow.
# Usage: cd /root/latest/EbookAI && bash deploy/manual-deploy.sh
set -euo pipefail

APP_DIR="${APP_DIR:-/root/latest/EbookAI}"
BRANCH="${BRANCH:-Clean_Code}"
PORT="${PORT:-5000}"
ENV_FILE="${ENV_FILE:-/etc/default/ebookai}"

cd "$APP_DIR"

echo "==> [1/5] Git branch + pull"
git branch
git checkout "$BRANCH"
git pull origin "$BRANCH"
echo "    HEAD: $(git log -1 --oneline)"

echo "==> [2/5] Publish (linux-x64 self-contained)"
dotnet publish newEbook.csproj -c Release -r linux-x64 --self-contained true -maxcpucount:1 -o publish

echo "==> [3/5] Load environment"
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

echo "==> [4/5] Stop process on port $PORT"
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

echo "==> [5/5] Start app (nohup)"
cd "$APP_DIR/publish"
: > "$APP_DIR/nohup.out"
nohup dotnet EBookDashboard.dll --urls "http://0.0.0.0:${PORT}" >> "$APP_DIR/nohup.out" 2>&1 &
echo "    Started PID $!"
sleep 3

echo "==> Health check"
curl -sf "http://127.0.0.1:${PORT}/health" | head -c 500 || {
  echo "Health check failed — last log lines:"
  tail -n 40 "$APP_DIR/nohup.out" 2>/dev/null || true
  exit 1
}
echo ""
echo "Deploy complete → http://138.197.76.70:${PORT}"
