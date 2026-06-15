#!/usr/bin/env bash
# Apply one-time users table columns required for signup + onboarding tour.
# Safe to re-run (uses IF NOT EXISTS).
set -euo pipefail

APP_DIR="${APP_DIR:-/opt/EbookAI}"
SQL_FILE="${APP_DIR}/DatabaseScripts/add_has_completed_tour.sql"
ENV_FILE="${ENV_FILE:-/etc/default/ebookai}"

if [[ ! -f "$SQL_FILE" ]]; then
  echo "    [users-schema] SQL file missing: $SQL_FILE (skip)"
  exit 0
fi

if [[ -f "$ENV_FILE" ]]; then
  set -a
  # shellcheck disable=SC1090
  source "$ENV_FILE"
  set +a
fi

DB_NAME="${MYSQL_DATABASE:-ebookpublications}"
DB_USER="${MYSQL_USER:-}"
DB_PASS="${MYSQL_PASSWORD:-}"

mysql_run() {
  if [[ -n "$DB_USER" && -n "$DB_PASS" ]]; then
    mysql -u "$DB_USER" -p"$DB_PASS" "$@"
  elif mysql -e "SELECT 1" >/dev/null 2>&1; then
    mysql "$@"
  elif sudo mysql -e "SELECT 1" >/dev/null 2>&1; then
    sudo mysql "$@"
  else
    return 1
  fi
}

if ! mysql_run -e "USE \`${DB_NAME}\`; SELECT 1" >/dev/null 2>&1; then
  echo "    [users-schema] Cannot connect to MySQL database '${DB_NAME}' (skip — run SQL manually)"
  exit 0
fi

echo "    [users-schema] Applying add_has_completed_tour.sql to ${DB_NAME}..."
mysql_run "${DB_NAME}" < "$SQL_FILE" && echo "    [users-schema] OK" || echo "    [users-schema] WARN: SQL apply failed (check table name users)"
