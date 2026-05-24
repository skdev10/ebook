#!/usr/bin/env bash
# One-time MySQL setup for EbookAI on Ubuntu VM. Run as root on the server:
#   bash /opt/EbookAI/deploy/mysql-setup.sh
set -euo pipefail

DB="${MYSQL_DATABASE:-ebookpublications}"
USER="${MYSQL_USER:-ebookapp}"
PASS="${MYSQL_PASSWORD:-Root@1234}"
ROOT_PASS="${MYSQL_ROOT_PASSWORD:-Root@1234}"

mysql_admin() {
  if mysql -e "SELECT 1" >/dev/null 2>&1; then
    mysql "$@"
  elif mysql -u root -p"${ROOT_PASS}" -e "SELECT 1" >/dev/null 2>&1; then
    mysql -u root -p"${ROOT_PASS}" "$@"
  elif sudo mysql -e "SELECT 1" >/dev/null 2>&1; then
    sudo mysql "$@"
  else
    echo "ERROR: Cannot connect to MySQL. Try: MYSQL_ROOT_PASSWORD='your-root-password' bash deploy/mysql-setup.sh"
    exit 1
  fi
}

echo "==> Ensuring MySQL is running"
systemctl start mysql 2>/dev/null || systemctl start mysqld 2>/dev/null || true

echo "==> Creating database and app user (${USER}@${DB})"
mysql_admin <<EOF
CREATE DATABASE IF NOT EXISTS \`${DB}\` CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;
CREATE USER IF NOT EXISTS '${USER}'@'localhost' IDENTIFIED BY '${PASS}';
CREATE USER IF NOT EXISTS '${USER}'@'127.0.0.1' IDENTIFIED BY '${PASS}';
GRANT ALL PRIVILEGES ON \`${DB}\`.* TO '${USER}'@'localhost';
GRANT ALL PRIVILEGES ON \`${DB}\`.* TO '${USER}'@'127.0.0.1';
FLUSH PRIVILEGES;
EOF

echo "==> Testing connection"
mysql -u "${USER}" -p"${PASS}" -e "USE \`${DB}\`; SELECT 'MySQL OK' AS status;"

cat <<EOF

Done. Update /etc/default/ebookai:

ConnectionStrings__DefaultConnection='Server=localhost;Port=3306;Database=${DB};User=${USER};Password=${PASS};SslMode=None;AllowPublicKeyRetrieval=True;'

Then restart: bash deploy/start-nohup.sh
EOF
