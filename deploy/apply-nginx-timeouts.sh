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

cd "$APP_DIR"
SITE_SRC="$APP_DIR/deploy/nginx-ebookai.conf"
SITE_DST="/etc/nginx/sites-available/ebookai"

echo "==> Install ebookai nginx site"
cp -f "$SITE_SRC" "$SITE_DST"
ln -sf "$SITE_DST" /etc/nginx/sites-enabled/ebookai

# Remove default site if it conflicts on port 80/5000 (optional — comment out if you need default)
if [[ -f /etc/nginx/sites-enabled/default ]]; then
  rm -f /etc/nginx/sites-enabled/default
  echo "    Disabled default nginx site (frees port 80 for ebookai)"
fi

# Patch any other enabled sites that proxy to the app
for f in /etc/nginx/sites-enabled/*; do
  [[ -f "$f" ]] || continue
  if grep -q 'proxy_pass' "$f" 2>/dev/null; then
    sed -i 's/proxy_read_timeout[^;]*/proxy_read_timeout 3600s/g' "$f" || true
    sed -i 's/proxy_send_timeout[^;]*/proxy_send_timeout 3600s/g' "$f" || true
    sed -i 's/proxy_connect_timeout[^;]*/proxy_connect_timeout 60s/g' "$f" || true
    echo "    Patched timeouts in $(basename "$f")"
  fi
done

echo "==> Test and reload nginx"
nginx -t
systemctl reload nginx
echo "nginx OK — proxy_read_timeout 3600s for http://$SERVER_IP:5000 and :80"
