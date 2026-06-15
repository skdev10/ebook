#!/usr/bin/env bash
# Apply one-time books table columns required for whole-book HTML generation.
# Safe to re-run (ALTER uses IF NOT EXISTS pattern via column check).
set -euo pipefail

APP_DIR="${APP_DIR:-/opt/EbookAI}"
SQL_FILE="${APP_DIR}/DatabaseScripts/add_book_content_html.sql"
ENV_FILE="${ENV_FILE:-/etc/default/ebookai}"

if [[ ! -f "$SQL_FILE" ]]; then
  echo "    [books-schema] SQL file missing: $SQL_FILE (skip)"
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
ROOT_PASS="${MYSQL_ROOT_PASSWORD:-Root@1234}"

mysql_run() {
  if [[ -n "$DB_USER" && -n "$DB_PASS" ]]; then
    mysql -u "$DB_USER" -p"$DB_PASS" "$@"
  elif mysql -e "SELECT 1" >/dev/null 2>&1; then
    mysql "$@"
  elif mysql -u root -p"${ROOT_PASS}" -e "SELECT 1" >/dev/null 2>&1; then
    mysql -u root -p"${ROOT_PASS}" "$@"
  elif sudo mysql -e "SELECT 1" >/dev/null 2>&1; then
    sudo mysql "$@"
  else
    return 1
  fi
}

if ! mysql_run -e "USE \`${DB_NAME}\`; SELECT 1" >/dev/null 2>&1; then
  echo "    [books-schema] Cannot connect to MySQL database '${DB_NAME}' (skip — run SQL manually)"
  exit 0
fi

if mysql_run -N -e "USE \`${DB_NAME}\`; SELECT COUNT(*) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA='${DB_NAME}' AND TABLE_NAME='books' AND COLUMN_NAME='BookContentHtml';" 2>/dev/null | grep -q '^1$'; then
  echo "    [books-schema] BookContentHtml column already present (skip)"
  exit 0
fi

echo "    [books-schema] Applying add_book_content_html.sql to ${DB_NAME}..."
mysql_run "${DB_NAME}" < "$SQL_FILE" && echo "    [books-schema] OK" || echo "    [books-schema] WARN: SQL apply failed (check table name books)"
