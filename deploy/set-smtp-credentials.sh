#!/usr/bin/env bash
# Set Gmail (or any SMTP) credentials on the production server.
# Run on server as root:
#   bash deploy/set-smtp-credentials.sh 'your@gmail.com' 'your-16-char-app-password'
#
# Gmail: Google Account → Security → 2-Step Verification → App passwords → Mail
set -euo pipefail

ENV_FILE="${ENV_FILE:-/etc/default/ebookai}"
SMTP_USER="${1:-}"
SMTP_PASS="${2:-}"
SMTP_HOST="${SMTP_HOST:-smtp.gmail.com}"
SMTP_PORT="${SMTP_PORT:-587}"
SENDER_NAME="${SENDER_NAME:-eBook Publisher}"

if [[ -z "$SMTP_USER" || -z "$SMTP_PASS" ]]; then
  echo "Usage: bash deploy/set-smtp-credentials.sh 'email@example.com' 'smtp-password-or-app-password'"
  echo ""
  echo "Gmail example:"
  echo "  bash deploy/set-smtp-credentials.sh 'you@gmail.com' 'abcd efgh ijkl mnop'"
  exit 1
fi

if [[ ! -f "$ENV_FILE" ]]; then
  echo "ERROR: $ENV_FILE not found."
  exit 1
fi

upsert() {
  local key="$1"
  local value="$2"
  local quoted
  quoted="$(printf '%q' "$value")"
  if grep -q "^${key}=" "$ENV_FILE" 2>/dev/null; then
    sed -i "s|^${key}=.*|${key}=${quoted}|" "$ENV_FILE"
  else
    echo "${key}=${quoted}" >> "$ENV_FILE"
  fi
}

upsert "Email__SmtpServer" "$SMTP_HOST"
upsert "Email__Port" "$SMTP_PORT"
upsert "Email__Username" "$SMTP_USER"
upsert "Email__Password" "$SMTP_PASS"
upsert "Email__FromEmail" "$SMTP_USER"
upsert "Email__SenderName" "$SENDER_NAME"
upsert "Email__EnableSSL" "true"

chmod 600 "$ENV_FILE"
echo "OK — SMTP credentials saved to $ENV_FILE"
echo "    From: $SMTP_USER"
echo "    Host: $SMTP_HOST:$SMTP_PORT"
echo ""
echo "Restart app:"
echo "  cd /opt/EbookAI && bash deploy/do-deploy.sh"
echo "Or quick restart:"
echo "  bash deploy/start-app-out.sh"
