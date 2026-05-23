#!/usr/bin/env bash
# One-time MySQL setup for EbookAI on Ubuntu VM. Run as root on the server:
#   bash /opt/EbookAI/deploy/mysql-setup.sh
set -euo pipefail

DB="${MYSQL_DATABASE:-ebookpublications}"
USER="${MYSQL_USER:-ebookapp}"
PASS="${MYSQL_PASSWORD:-Root@1234}"

echo "==> Ensuring MySQL is running"
systemctl start mysql 2>/dev/null || systemctl start mysqld 2>/dev/null || true

echo "==> Creating database and app user (${USER}@${DB})"
mysql <<EOF
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

ConnectionStrings__DefaultConnection=Server=localhost;Port=3306;Database=${DB};User=${USER};Password=${PASS};SslMode=None;AllowPublicKeyRetrieval=True;

Then redeploy/restart the app.
EOF
