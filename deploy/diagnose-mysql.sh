#!/usr/bin/env bash
# Quick MySQL diagnostics on the ebook VM. Run as root:
#   bash /opt/EbookAI/deploy/diagnose-mysql.sh
set -euo pipefail

echo "==> MySQL service"
systemctl is-active mysql 2>/dev/null || systemctl is-active mysqld 2>/dev/null || echo "mysql service not found"

echo "==> Socket files"
ls -la /var/run/mysqld/mysqld.sock /run/mysqld/mysqld.sock 2>/dev/null || echo "no socket found"

echo "==> Root socket login (no password)"
mysql -e "SELECT VERSION() AS version, USER() AS mysql_user;" 2>/dev/null \
  || echo "root socket (no password) FAILED"

echo "==> Root with password"
mysql -u root -p'Root@1234' -e "SELECT VERSION() AS version, USER() AS mysql_user;" 2>/dev/null \
  || echo "root password login FAILED"
