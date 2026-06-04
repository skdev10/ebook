#!/usr/bin/env bash
# One-time / repeat fix so live behaves like local: env file, persistent uploads, Settings LONGTEXT.
# Run on server: cd /opt/EbookAI && bash deploy/fix-live-parity.sh
set -euo pipefail

APP_DIR="${APP_DIR:-/opt/EbookAI}"
ENV_FILE="${ENV_FILE:-/etc/default/ebookai}"
PERSIST_DIR="${PERSIST_DIR:-$APP_DIR/persistent}"

cd "$APP_DIR"

echo "==> Ensure /etc/default/ebookai has required keys"
if [[ ! -f "$ENV_FILE" ]]; then
  cp -f deploy/etc-default-ebookai.example "$ENV_FILE"
  chmod 600 "$ENV_FILE"
  echo "    Created $ENV_FILE from example — EDIT passwords and API key before restart."
fi

grep -q '^App__PublicBaseUrl=' "$ENV_FILE" 2>/dev/null || echo "App__PublicBaseUrl=http://138.197.76.70:5000" >> "$ENV_FILE"
grep -q '^Authentication__CookieSecurePolicy=' "$ENV_FILE" 2>/dev/null || echo 'Authentication__CookieSecurePolicy=SameAsRequest' >> "$ENV_FILE"

if ! grep -q '^ConnectionStrings__DefaultConnection=' "$ENV_FILE" 2>/dev/null \
  && ! grep -q '^DATABASE_URL=' "$ENV_FILE" 2>/dev/null \
  && ! grep -q '^MYSQL_URL=' "$ENV_FILE" 2>/dev/null; then
  echo "WARNING: DB connection env missing in $ENV_FILE"
  echo "  Add one of these:"
  echo "  ConnectionStrings__DefaultConnection='Server=localhost;Port=3306;Database=ebookpublications;User=root;Password=YOUR_PASSWORD;SslMode=None;AllowPublicKeyRetrieval=True;'"
  echo "  DATABASE_URL='mysql://root:YOUR_PASSWORD@localhost:3306/ebookpublications?sslmode=None'"
fi

echo "==> Persistent uploads + session keys"
mkdir -p "$PERSIST_DIR/uploads" "$PERSIST_DIR/DataProtection-Keys"
if [[ -d "$APP_DIR/wwwroot/uploads" && "$APP_DIR/wwwroot/uploads" != "$PERSIST_DIR/uploads" ]]; then
  cp -an "$APP_DIR/wwwroot/uploads/." "$PERSIST_DIR/uploads/" 2>/dev/null || true
fi
if [[ -d "$APP_DIR/app-out/wwwroot/uploads" && ! -L "$APP_DIR/app-out/wwwroot/uploads" ]]; then
  cp -an "$APP_DIR/app-out/wwwroot/uploads/." "$PERSIST_DIR/uploads/" 2>/dev/null || true
fi

if [[ -d "$APP_DIR/app-out" ]]; then
  mkdir -p "$APP_DIR/app-out/wwwroot"
  rm -rf "$APP_DIR/app-out/wwwroot/uploads"
  ln -sfn "$PERSIST_DIR/uploads" "$APP_DIR/app-out/wwwroot/uploads"
  rm -rf "$APP_DIR/app-out/DataProtection-Keys"
  ln -sfn "$PERSIST_DIR/DataProtection-Keys" "$APP_DIR/app-out/DataProtection-Keys"
  echo "    Linked app-out -> $PERSIST_DIR"
fi

echo "==> Settings.Value LONGTEXT (cover paths + formatter draft)"
mysql ebookpublications -e "ALTER TABLE \`Settings\` MODIFY COLUMN \`Value\` LONGTEXT NULL;" 2>/dev/null \
  && echo "    Settings.Value is LONGTEXT" \
  || echo "    Skip or run manually: ALTER TABLE Settings MODIFY COLUMN Value LONGTEXT NULL;"

echo "==> Redeploy recommended"
echo "    cd $APP_DIR && bash deploy/do-deploy.sh"
echo "==> Then verify:"
echo "    curl -s http://127.0.0.1:5000/Books/DeploymentStatus | python3 -m json.tool"
