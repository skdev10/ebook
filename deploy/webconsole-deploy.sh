#!/usr/bin/env bash
# Deploy on DigitalOcean web console — matches your manual steps exactly.
# Run as root on the droplet:
#   cd /root/latest/EbookAI && bash deploy/webconsole-deploy.sh
set -euo pipefail

APP_DIR="${APP_DIR:-/root/latest/EbookAI}"
BRANCH="${BRANCH:-Clean_Code}"
REPO_URL="${REPO_URL:-https://github.com/skdev10/ebook.git}"
PORT="${PORT:-5000}"
ENV_FILE="${ENV_FILE:-/etc/default/ebookai}"

echo "=========================================="
echo " EbookAI deploy — web console workflow"
echo " App dir : $APP_DIR"
echo " Branch  : $BRANCH"
echo " Port    : $PORT"
echo "=========================================="

# --- Step 1: clone or pull ---
if [[ ! -d "$APP_DIR/.git" ]]; then
  echo ""
  echo "[1/6] git clone -b $BRANCH $REPO_URL -> $APP_DIR"
  mkdir -p "$(dirname "$APP_DIR")"
  git clone -b "$BRANCH" "$REPO_URL" "$APP_DIR"
else
  echo ""
  echo "[1/6] git pull (branch $BRANCH)"
  cd "$APP_DIR"
  git fetch origin
  git checkout "$BRANCH"
  git pull origin "$BRANCH"
fi
cd "$APP_DIR"
echo "    HEAD: $(git log -1 --oneline)"

echo ""
echo "[1b/6] PDF export fonts (PDFsharp embedded TTF)"
chmod +x Scripts/download-export-fonts.sh 2>/dev/null || true
if [[ -x Scripts/download-export-fonts.sh ]]; then
  bash Scripts/download-export-fonts.sh "$APP_DIR/wwwroot/fonts/pdf" || echo "    Font download skipped (curl/network) — run manually if PDF fonts missing"
else
  echo "    Scripts/download-export-fonts.sh not found — skip"
fi

# --- Step 2: publish ---
echo ""
echo "[2/6] dotnet publish -> publish/"
dotnet publish newEbook.csproj -c Release -r linux-x64 --self-contained true -maxcpucount:1 -o publish

echo ""
echo "[2b/6] Link persistent uploads + session/auth keys (fixes APIs after redeploy)"
chmod +x deploy/link-persistent.sh
bash deploy/link-persistent.sh "$APP_DIR" publish

# --- Step 3: load env (secrets already on server) ---
echo ""
echo "[3/6] Load environment from $ENV_FILE"
if [[ -f "$ENV_FILE" ]]; then
  set -a
  # shellcheck disable=SC1090
  source "$ENV_FILE"
  set +a
  echo "    Loaded $ENV_FILE"
else
  echo "    WARNING: $ENV_FILE not found — using appsettings only"
fi
export ASPNETCORE_ENVIRONMENT="${ASPNETCORE_ENVIRONMENT:-Production}"
export ASPNETCORE_URLS="${ASPNETCORE_URLS:-http://0.0.0.0:${PORT}}"
export EBOOKAI_PERSIST_DIR="${EBOOKAI_PERSIST_DIR:-$APP_DIR/persistent}"

# --- Step 4: check port 5000 ---
echo ""
echo "[4/6] Check if something is listening on :$PORT"
if command -v netstat >/dev/null 2>&1; then
  netstat -tpln 2>/dev/null | grep ":$PORT" || echo "    (nothing on :$PORT yet)"
  OLD_PID=$(netstat -tpln 2>/dev/null | awk -v p=":$PORT" '$4 ~ p {print $7}' | sed 's/.*\///' | cut -d, -f1 | head -1)
elif command -v ss >/dev/null 2>&1; then
  ss -tlnp 2>/dev/null | grep ":$PORT" || echo "    (nothing on :$PORT yet)"
  OLD_PID=$(ss -tlnp "sport = :$PORT" 2>/dev/null | awk 'NR>1 {print $NF}' | sed -n 's/.*pid=\([0-9]*\).*/\1/p' | head -1)
else
  OLD_PID=""
fi

# --- Step 5: stop old process ---
echo ""
echo "[5/6] Stop old server on :$PORT"
if [[ -n "${OLD_PID:-}" && "$OLD_PID" =~ ^[0-9]+$ ]]; then
  echo "    kill -9 $OLD_PID"
  kill -9 "$OLD_PID" 2>/dev/null || true
fi
fuser -k "${PORT}/tcp" 2>/dev/null || true
sleep 2
netstat -tpln 2>/dev/null | grep ":$PORT" || ss -tlnp 2>/dev/null | grep ":$PORT" || echo "    Port $PORT is free."

# --- Step 6: start server ---
echo ""
echo "[6/6] Start server (nohup)"
cd "$APP_DIR/publish"
: > "$APP_DIR/nohup.out"
if [[ -x ./EBookDashboard ]]; then
  nohup ./EBookDashboard --urls "http://0.0.0.0:${PORT}" >> "$APP_DIR/nohup.out" 2>&1 &
else
  nohup dotnet EBookDashboard.dll --urls "http://0.0.0.0:${PORT}" >> "$APP_DIR/nohup.out" 2>&1 &
fi
APP_PID=$!
echo "    Started PID $APP_PID"
echo ""
echo "    Watch logs:  tail -f $APP_DIR/nohup.out"
echo "    Health:      curl -s http://127.0.0.1:${PORT}/health"
echo ""
sleep 5
if curl -sf "http://127.0.0.1:${PORT}/health" | head -c 300; then
  echo ""
  echo ""
  echo "Deploy OK → http://138.197.76.70:${PORT}"
else
  echo ""
  echo "Health check not ready yet — check logs:"
  tail -n 40 "$APP_DIR/nohup.out" 2>/dev/null || true
  echo ""
  echo "Run: tail -f $APP_DIR/nohup.out"
fi
