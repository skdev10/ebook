#!/usr/bin/env bash
# Apply nginx long timeouts for AI APIs (fixes 504 Gateway Time-out on chapter generate).
# Run on server: cd /opt/EbookAI && sudo bash deploy/apply-nginx-timeouts.sh
set -euo pipefail

APP_DIR="${APP_DIR:-/opt/EbookAI}"
SERVER_IP="${SERVER_IP:-138.197.76.70}"

if ! command -v nginx >/dev/null 2>&1; then
  echo "nginx not installed — skipping. App listens on :5000 directly (Kestrel)."
  echo "If you still see 504 HTML errors, something else is proxying port 5000 — check: ss -tlnp | grep 5000"
  exit 0
fi

SUDO=""
if [[ "$(id -u)" -ne 0 ]]; then
  if command -v sudo >/dev/null 2>&1; then
    SUDO="sudo"
  else
    echo "ERROR: nginx is installed but this script must run as root."
    echo "Run: sudo bash deploy/apply-nginx-timeouts.sh"
    exit 1
  fi
fi

cd "$APP_DIR"
SITE_SRC="$APP_DIR/deploy/nginx-ebookai.conf"
SITE_DST="/etc/nginx/sites-available/ebookai"
NGINX_CONF="/etc/nginx/nginx.conf"

echo "==> Install ebookai nginx site (public :5000/:80 → Kestrel :5050)"
$SUDO cp -f "$SITE_SRC" "$SITE_DST"
$SUDO ln -sf "$SITE_DST" /etc/nginx/sites-enabled/ebookai

if [[ -f /etc/nginx/sites-enabled/default ]]; then
  $SUDO rm -f /etc/nginx/sites-enabled/default
  echo "    Disabled default nginx site (frees port 80 for ebookai)"
fi

# Global http{} defaults — catches sites missing per-location timeouts.
if [[ -f "$NGINX_CONF" ]]; then
  if grep -q 'proxy_read_timeout' "$NGINX_CONF" 2>/dev/null; then
    $SUDO sed -i 's/proxy_read_timeout[^;]*/proxy_read_timeout 3600s/g' "$NGINX_CONF" || true
    $SUDO sed -i 's/proxy_send_timeout[^;]*/proxy_send_timeout 3600s/g' "$NGINX_CONF" || true
  else
    $SUDO sed -i '/^http {/a\    proxy_read_timeout 3600s;\n    proxy_send_timeout 3600s;' "$NGINX_CONF" || true
  fi
  echo "    Patched global proxy timeouts in nginx.conf"
fi

for f in /etc/nginx/sites-enabled/*; do
  [[ -f "$f" ]] || continue
  if grep -q 'proxy_pass' "$f" 2>/dev/null; then
    $SUDO sed -i 's/proxy_read_timeout[^;]*/proxy_read_timeout 3600s/g' "$f" || true
    $SUDO sed -i 's/proxy_send_timeout[^;]*/proxy_send_timeout 3600s/g' "$f" || true
    $SUDO sed -i 's/proxy_connect_timeout[^;]*/proxy_connect_timeout 60s/g' "$f" || true
    echo "    Patched timeouts in $(basename "$f")"
  fi
done

echo "==> Test and reload nginx"
$SUDO nginx -t
$SUDO systemctl reload nginx
echo "nginx OK — proxy_read_timeout 3600s for http://${SERVER_IP}:5000 and :80 → 127.0.0.1:5050"
