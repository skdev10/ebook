#!/usr/bin/env bash
# Ensure /etc/default/ebookai has all ExternalApi__* URLs and a real API key.
# Run on server: bash deploy/ensure-external-api-env.sh
set -euo pipefail

APP_DIR="${APP_DIR:-/opt/EbookAI}"
ENV_FILE="${ENV_FILE:-/etc/default/ebookai}"
EXAMPLE="${APP_DIR}/deploy/etc-default-ebookai.example"

cd "$APP_DIR"

if [[ ! -f "$ENV_FILE" ]]; then
  echo "Creating $ENV_FILE from example..."
  cp -f "$EXAMPLE" "$ENV_FILE"
  chmod 600 "$ENV_FILE"
  echo "EDIT $ENV_FILE — set ExternalApi__ApiKey and DB password, then re-run deploy."
  exit 1
fi

# Add missing ExternalApi / App URL / chapter timeout lines from example (never overwrite existing values).
while IFS= read -r line; do
  [[ "$line" =~ ^ExternalApi__ ]] || [[ "$line" =~ ^App__PublicBaseUrl= ]] || [[ "$line" =~ ^ChapterGeneration__ ]] || continue
  key="${line%%=*}"
  if ! grep -q "^${key}=" "$ENV_FILE" 2>/dev/null; then
    echo "$line" >> "$ENV_FILE"
    echo "    Added missing $key"
  fi
done < "$EXAMPLE"

set -a
# shellcheck disable=SC1090
source "$ENV_FILE"
set +a

API_KEY="$(echo "${ExternalApi__ApiKey:-}" | tr -d '"' | sed "s/^[[:space:]]*//;s/[[:space:]]*$//")"
if [[ -z "$API_KEY" || "$API_KEY" == "PASTE_YOUR_KEY_HERE" ]]; then
  echo ""
  echo "ERROR: ExternalApi__ApiKey is missing in $ENV_FILE"
  echo "  nano $ENV_FILE"
  echo '  ExternalApi__ApiKey="your-trimmed-key-no-spaces"'
  echo ""
  exit 1
fi

echo "OK — ExternalApi__ApiKey configured ($(echo -n "$API_KEY" | wc -c | tr -d ' ') chars)"
echo "    BaseUrl: ${ExternalApi__BaseUrl:-http://162.229.248.26:8001}"
