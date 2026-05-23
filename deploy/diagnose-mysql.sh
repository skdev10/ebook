#!/usr/bin/env bash
# Quick MySQL diagnostics on the ebook VM. Run as root:
#   bash /opt/EbookAI/deploy/diagnose-mysql.sh
set -euo pipefail

echo "==> MySQL service"
systemctl is-active mysql 2>/dev/null || systemctl is-active mysqld 2>/dev/null || echo "mysql service not found"

echo "==> Socket files"
ls -la /var/run/mysqld/mysqld.sock /run/mysqld/mysqld.sock 2>/dev/null || echo "no socket found"

echo "==> Root socket login (Ubuntu default)"
mysql -e "SELECT VERSION() AS version, USER() AS mysql_user;" || echo "root socket login FAILED"

echo "==> Test ebookapp user (if created)"
mysql -u ebookapp -p'Root@1234' -e "USE ebookpublications; SELECT COUNT(*) AS user_count FROM users;" 2>/dev/null \
  || echo "ebookapp login FAILED — run: bash deploy/mysql-setup.sh"

echo "==> Test root with password over TCP"
mysql -h 127.0.0.1 -u root -p'Root@1234' -e "SELECT 1;" 2>/dev/null \
  || echo "root@127.0.0.1 password login FAILED (normal on Ubuntu — use ebookapp user)"
