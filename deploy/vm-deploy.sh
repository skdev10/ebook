#!/usr/bin/env bash
# EBookDashboard — build/publish on Linux VM (Ubuntu). Run from repo root after git clone:
#   chmod +x deploy/vm-deploy.sh && ./deploy/vm-deploy.sh
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"
cd "$REPO_ROOT"

BRANCH="${BRANCH:-Clean_Code}"
OUT_DIR="${OUT_DIR:-app-out}"
RUNTIM="${RUNTIM:-linux-x64}"
SELF_CONTAINED="${SELF_CONTAINED:-true}"

# Optional: path to a secrets JSON (same shape as appsettings). Copy into repo before publish.
# Example: export SECRETS_JSON="$HOME/ebook-secrets/appsettings.Local.json"
if [[ -n "${SECRETS_JSON:-}" && -f "$SECRETS_JSON" ]]; then
  echo "[deploy] Copying secrets from SECRETS_JSON -> appsettings.Local.json"
  cp -f "$SECRETS_JSON" "$REPO_ROOT/appsettings.Local.json"
fi

command -v git >/dev/null 2>&1 && if git -C "$REPO_ROOT" rev-parse --git-dir >/dev/null 2>&1; then
  git -C "$REPO_ROOT" fetch origin 2>/dev/null || true
  git -C "$REPO_ROOT" checkout "$BRANCH" 2>/dev/null || true
  git -C "$REPO_ROOT" pull origin "$BRANCH" 2>/dev/null || true
fi

if ! command -v dotnet >/dev/null 2>&1; then
  echo "dotnet SDK 8 not found. Install: https://learn.microsoft.com/dotnet/core/install/linux"
  exit 1
fi

PERSIST_DIR="${PERSIST_DIR:-$REPO_ROOT/persistent}"
PERSIST_UPLOADS="$PERSIST_DIR/uploads"
PERSIST_KEYS="$PERSIST_DIR/DataProtection-Keys"
mkdir -p "$PERSIST_UPLOADS" "$PERSIST_KEYS"

# Seed persistent uploads from repo or previous publish (covers survive redeploy).
if [[ -d "$REPO_ROOT/wwwroot/uploads" && -z "$(ls -A "$PERSIST_UPLOADS" 2>/dev/null)" ]]; then
  echo "[deploy] Seeding persistent uploads from repo wwwroot/uploads"
  cp -a "$REPO_ROOT/wwwroot/uploads/." "$PERSIST_UPLOADS/" 2>/dev/null || true
elif [[ -d "$REPO_ROOT/$OUT_DIR/wwwroot/uploads" && -z "$(ls -A "$PERSIST_UPLOADS" 2>/dev/null)" ]]; then
  echo "[deploy] Seeding persistent uploads from previous $OUT_DIR"
  cp -a "$REPO_ROOT/$OUT_DIR/wwwroot/uploads/." "$PERSIST_UPLOADS/" 2>/dev/null || true
fi

echo "[deploy] dotnet publish -> $OUT_DIR (runtime=$RUNTIM self-contained=$SELF_CONTAINED)"
dotnet publish "$REPO_ROOT/newEbook.csproj" -c Release -r "$RUNTIM" \
  --self-contained "$SELF_CONTAINED" -o "$REPO_ROOT/$OUT_DIR"

mkdir -p "$REPO_ROOT/$OUT_DIR/wwwroot"
rm -rf "$REPO_ROOT/$OUT_DIR/wwwroot/uploads"
ln -sfn "$PERSIST_UPLOADS" "$REPO_ROOT/$OUT_DIR/wwwroot/uploads"
rm -rf "$REPO_ROOT/$OUT_DIR/DataProtection-Keys"
ln -sfn "$PERSIST_KEYS" "$REPO_ROOT/$OUT_DIR/DataProtection-Keys"
echo "[deploy] Linked persistent uploads -> $REPO_ROOT/$OUT_DIR/wwwroot/uploads"
echo "[deploy] Linked DataProtection keys -> $REPO_ROOT/$OUT_DIR/DataProtection-Keys"

APP="$REPO_ROOT/$OUT_DIR/EBookDashboard"
if [[ ! -x "$APP" ]]; then
  echo "[deploy] Warning: EBookDashboard binary not found or not executable: $APP"
  echo "[deploy] For framework-dependent publish use: dotnet $OUT_DIR/EBookDashboard.dll"
else
  chmod +x "$APP" 2>/dev/null || true
fi

echo "[deploy] Done. Output: $REPO_ROOT/$OUT_DIR"
echo "[deploy] Run (foreground): cd $OUT_DIR && ASPNETCORE_ENVIRONMENT=Production ./EBookDashboard --urls http://0.0.0.0:5000"
echo "[deploy] Or use systemd: deploy/ebookai.service (set paths + /etc/default/ebookai)"
echo "[deploy] Required env vars: see DigitalOcean-EnvironmentVariables.txt"
