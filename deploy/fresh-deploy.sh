#!/usr/bin/env bash
# Fresh deploy from scratch on Ubuntu VM.
# Usage: cd /opt/EbookAI && bash deploy/fresh-deploy.sh
set -euo pipefail

APP_DIR="${APP_DIR:-/opt/EbookAI}"
BRANCH="${BRANCH:-Clean_Code}"
OUT_DIR="${OUT_DIR:-app-out}"
PORT="${PORT:-5000}"
ENV_FILE="${ENV_FILE:-/etc/default/ebookai}"

cd "$APP_DIR"

echo "==> [1/6] Reset local deploy script drift"
git checkout -- deploy/do-deploy.sh deploy/vm-deploy.sh deploy/fix-live-parity.sh deploy/fresh-deploy.sh 2>/dev/null || true

echo "==> [2/6] Git pull ($BRANCH)"
git fetch origin "$BRANCH"
git checkout "$BRANCH"
git pull origin "$BRANCH"
echo "    HEAD: $(git log -1 --oneline)"

echo "==> [3/6] Live parity (persistent uploads + session keys)"
chmod +x deploy/*.sh
bash deploy/fix-live-parity.sh

echo "==> [4/6] Publish (no extra git pull inside vm-deploy)"
export DEPLOY_SKIP_GIT=1
bash deploy/vm-deploy.sh

echo "==> [5/6] Load env + stop old app on port $PORT"
if [[ -f "$ENV_FILE" ]]; then
  set -a
  # shellcheck disable=SC1090
  source "$ENV_FILE"
  set +a
else
  echo "WARNING: $ENV_FILE missing — create from deploy/etc-default-ebookai.example"
fi
export ASPNETCORE_ENVIRONMENT="${ASPNETCORE_ENVIRONMENT:-Production}"
export ASPNETCORE_URLS="${ASPNETCORE_URLS:-http://0.0.0.0:${PORT}}"

if command -v ss >/dev/null 2>&1; then
  OLD_PID=$(ss -tlnp "sport = :$PORT" 2>/dev/null | awk 'NR>1 {print $NF}' | sed -n 's/.*pid=\([0-9]*\).*/\1/p' | head -1)
elif command -v netstat >/dev/null 2>&1; then
  OLD_PID=$(netstat -tpln 2>/dev/null | awk -v p=":$PORT" '$4 ~ p {print $7}' | sed 's/.*\///' | cut -d, -f1 | head -1)
fi
if [[ -n "${OLD_PID:-}" && "$OLD_PID" =~ ^[0-9]+$ ]]; then
  echo "    Stopping PID $OLD_PID"
  kill -9 "$OLD_PID" || true
  sleep 2
fi

echo "==> [6/6] Start app"
cd "$APP_DIR/$OUT_DIR"
nohup ./EBookDashboard --urls "http://0.0.0.0:${PORT}" > "$APP_DIR/nohup.out" 2>&1 &
echo "    Started PID $!"
sleep 4

echo ""
echo "==> Health"
curl -sf "http://127.0.0.1:${PORT}/health" && echo "" || { tail -40 "$APP_DIR/nohup.out"; exit 1; }

echo "==> DeploymentStatus"
curl -sf "http://127.0.0.1:${PORT}/Books/DeploymentStatus" | head -c 600 && echo "" || true

echo ""
echo "Deploy complete → http://138.197.76.70:${PORT}"
