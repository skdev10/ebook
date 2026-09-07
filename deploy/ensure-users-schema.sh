#!/usr/bin/env bash
# Apply users-table columns required for signup, onboarding tour, and AI cover quota.
# Safe to re-run (uses IF NOT EXISTS / column checks).
set -euo pipefail

APP_DIR="${APP_DIR:-/opt/EbookAI}"
TOUR_SQL="${APP_DIR}/DatabaseScripts/add_has_completed_tour.sql"
QUOTA_SQL="${APP_DIR}/DatabaseScripts/add_ai_cover_quota.sql"
ENV_FILE="${ENV_FILE:-/etc/default/ebookai}"

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
  echo "    [users-schema] Cannot connect to MySQL database '${DB_NAME}' (skip — run SQL manually)"
  exit 0
fi

column_exists() {
  mysql_run -N -e "USE \`${DB_NAME}\`; SELECT COUNT(*) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA='${DB_NAME}' AND TABLE_NAME='users' AND COLUMN_NAME='$1';" 2>/dev/null | grep -q '^1$'
}

if [[ -f "$TOUR_SQL" ]]; then
  if column_exists "HasCompletedTour"; then
    echo "    [users-schema] HasCompletedTour already present"
  else
    echo "    [users-schema] Applying add_has_completed_tour.sql..."
    mysql_run "${DB_NAME}" < "$TOUR_SQL" && echo "    [users-schema] tour OK" || echo "    [users-schema] WARN: tour SQL failed"
  fi
else
  echo "    [users-schema] Missing $TOUR_SQL (skip tour column)"
fi

if [[ -f "$QUOTA_SQL" ]]; then
  if column_exists "AICoverGenerationsUsed" && column_exists "AICoverGenerationLimit"; then
    echo "    [users-schema] AI cover quota columns already present"
  else
    echo "    [users-schema] Applying add_ai_cover_quota.sql..."
    mysql_run "${DB_NAME}" < "$QUOTA_SQL" && echo "    [users-schema] quota OK" || echo "    [users-schema] WARN: quota SQL failed"
  fi
else
  echo "    [users-schema] Missing $QUOTA_SQL (skip cover quota columns)"
fi
