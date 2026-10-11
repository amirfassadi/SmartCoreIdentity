#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."
if [[ -e .env ]]; then
  echo '.env already exists; existing keys preserved.'
  exit 0
fi
umask 077
{
  echo "POSTGRES_PASSWORD=$(openssl rand -hex 24)"
  echo "IDENTITY_MAC_KEY=$(openssl rand -base64 32)"
  echo "IDENTITY_MATERIAL_KEY=$(openssl rand -base64 32)"
} > .env
echo 'Created development configuration. Run: docker compose up --build'
