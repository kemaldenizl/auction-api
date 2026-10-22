#!/bin/bash
# Postgres ilk kez (boş volume ile) ayağa kalkarken bir kez çalışır.
# Her mikroservis kendi database'ine sahip olsun diye ayrı ayrı yaratıyoruz.
# Volume silinmedikçe (docker compose down -v) tekrar çalışmaz.
set -euo pipefail

for db in catalog_db auction_db; do
  echo "init: creating database '$db'"
  psql -v ON_ERROR_STOP=1 --username "$POSTGRES_USER" --dbname "$POSTGRES_DB" <<-EOSQL
    SELECT 'CREATE DATABASE "$db"'
    WHERE NOT EXISTS (SELECT FROM pg_database WHERE datname = '$db')\gexec
EOSQL
done

echo "init: done"
