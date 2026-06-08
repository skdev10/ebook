#!/usr/bin/env bash
# Link persistent uploads + DataProtection keys into publish/app-out output.
# Without this, every redeploy invalidates login/session cookies and breaks API calls.
# Usage: bash deploy/link-persistent.sh /root/latest/EbookAI publish
set -euo pipefail

APP_DIR="${1:?APP_DIR required}"
OUT_DIR="${2:-publish}"
PERSIST_DIR="${PERSIST_DIR:-$APP_DIR/persistent}"
PERSIST_UPLOADS="$PERSIST_DIR/uploads"
PERSIST_KEYS="$PERSIST_DIR/DataProtection-Keys"
TARGET="$APP_DIR/$OUT_DIR"

mkdir -p "$PERSIST_UPLOADS" "$PERSIST_KEYS" "$TARGET/wwwroot"

if [[ -d "$APP_DIR/wwwroot/uploads" && -z "$(ls -A "$PERSIST_UPLOADS" 2>/dev/null)" ]]; then
  echo "[link-persistent] Seeding uploads from repo wwwroot/uploads"
  cp -a "$APP_DIR/wwwroot/uploads/." "$PERSIST_UPLOADS/" 2>/dev/null || true
elif [[ -d "$TARGET/wwwroot/uploads" && ! -L "$TARGET/wwwroot/uploads" && -z "$(ls -A "$PERSIST_UPLOADS" 2>/dev/null)" ]]; then
  echo "[link-persistent] Seeding uploads from previous $OUT_DIR"
  cp -a "$TARGET/wwwroot/uploads/." "$PERSIST_UPLOADS/" 2>/dev/null || true
fi

rm -rf "$TARGET/wwwroot/uploads"
ln -sfn "$PERSIST_UPLOADS" "$TARGET/wwwroot/uploads"
rm -rf "$TARGET/DataProtection-Keys"
ln -sfn "$PERSIST_KEYS" "$TARGET/DataProtection-Keys"

echo "[link-persistent] uploads -> $TARGET/wwwroot/uploads"
echo "[link-persistent] DataProtection-Keys -> $TARGET/DataProtection-Keys"
