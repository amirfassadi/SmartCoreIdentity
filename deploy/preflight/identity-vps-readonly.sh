#!/usr/bin/env bash
# Inventory only: no install, restart, write, configuration dump or secret reads.
set -u
section() { printf '\n%s\n' "$1"; }
section 'OS'
if command -v lsb_release >/dev/null 2>&1; then lsb_release -ds; else uname -srm; fi
section 'Memory'
free -m
section 'Disk'
df -h / /var /home
section 'Listening ports (retain existing VPN ownership)'
if command -v ss >/dev/null 2>&1; then ss -lntp | awk 'NR==1 || $4 ~ /:(80|443|5432|6379|22110)$/'; fi
section 'Runtime versions'
for binary in node npm dotnet nginx psql; do
  if command -v "$binary" >/dev/null 2>&1; then
    case "$binary" in nginx) nginx -v 2>&1;; psql) psql --version;; *) "$binary" --version;; esac
  else printf '%s: unavailable\n' "$binary"; fi
done
section 'Service state (does not alter any service)'
if command -v systemctl >/dev/null 2>&1; then
  for service in nginx postgresql redis-server ocserv v2ray docker; do
    printf '%s: ' "$service"; systemctl is-active "$service" 2>/dev/null || true
  done
fi
section 'Database server version (local peer authentication only)'
# No password prompt; no database/table names, credentials or database contents printed.
if command -v sudo >/dev/null 2>&1 && command -v psql >/dev/null 2>&1; then
  sudo -n -u postgres psql -X -w -A -t -c 'SHOW server_version;' 2>/dev/null || printf 'Local peer check needs an operator session.\n'
fi
section 'End of read-only inventory'
