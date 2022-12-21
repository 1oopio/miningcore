#!/bin/bash
set -e

psql -v ON_ERROR_STOP=1 --username "$POSTGRES_USER" --dbname "$POSTGRES_DB" <<-EOSQL
	ALTER ROLE miningcore WITH PASSWORD '$POSTGRES_PASSWORD';
	ALTER DATABASE miningcore OWNER TO miningcore;
EOSQL

psql --username miningcore -d miningcore -f /miningcore/createdb.sql
psql --username miningcore -d miningcore -f /miningcore/createdb_postgresql_11_appendix.sql

# Create table shares for pools
psql --username miningcore -d miningcore -f /pools/createdb_shares.sql