#!/usr/bin/env bash
# Ensure /etc/default/ebookai has Email__* SMTP settings for forgot-password OTP.
# Run on server: bash deploy/ensure-email-env.sh
set -euo pipefail

APP_DIR="${APP_DIR:-/opt/EbookAI}"
ENV_FILE="${ENV_FILE:-/etc/default/ebookai}"
EXAMPLE="${APP_DIR}/deploy/etc-default-ebookai.example"

cd "$APP_DIR"

if [[ ! -f "$ENV_FILE" ]]; then
  echo "ERROR: $ENV_FILE not found. Run deploy/ensure-external-api-env.sh first."
  exit 1
fi

# Add missing Email__ lines from example (never overwrite existing values).
while IFS= read -r line; do
  [[ "$line" =~ ^Email__ ]] || continue
  key="${line%%=*}"
  if ! grep -q "^${key}=" "$ENV_FILE" 2>/dev/null; then
    echo "$line" >> "$ENV_FILE"
    echo "    Added missing $key"
  fi
done < "$EXAMPLE"

# shellcheck disable=SC1091
source "${APP_DIR}/deploy/lib-env.sh"
fix_env_file_syntax "$ENV_FILE"

SMTP_HOST="$(env_file_get "$ENV_FILE" "Email__SmtpServer" | tr -d '"' | sed 's/^[[:space:]]*//;s/[[:space:]]*$//')"
FROM_EMAIL="$(env_file_get "$ENV_FILE" "Email__FromEmail" | tr -d '"' | sed 's/^[[:space:]]*//;s/[[:space:]]*$//')"
SMTP_USER="$(env_file_get "$ENV_FILE" "Email__Username" | tr -d '"' | sed 's/^[[:space:]]*//;s/[[:space:]]*$//')"
SMTP_PASS="$(env_file_get "$ENV_FILE" "Email__Password" | tr -d '"' | sed 's/^[[:space:]]*//;s/[[:space:]]*$//')"

if [[ -z "$SMTP_HOST" || -z "$FROM_EMAIL" ]]; then
  echo "WARN — Email SMTP not fully configured in $ENV_FILE"
  echo "  Set credentials: bash deploy/set-smtp-credentials.sh 'you@gmail.com' 'gmail-app-password'"
  exit 0
fi

if [[ -z "$SMTP_PASS" || "$SMTP_PASS" == "PASTE_GMAIL_APP_PASSWORD_HERE" ]]; then
  echo "WARN — Email__Password missing in $ENV_FILE (forgot-password emails will not send)"
  echo "  bash deploy/set-smtp-credentials.sh 'you@gmail.com' 'gmail-app-password'"
  exit 0
fi

echo "OK — Email SMTP configured"
echo "    Host: ${SMTP_HOST}"
echo "    From: ${FROM_EMAIL}"
echo "    User: ${SMTP_USER:-$FROM_EMAIL}"
