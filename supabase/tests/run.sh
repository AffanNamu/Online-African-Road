#!/usr/bin/env bash
# Usage: PGHOST=... PGUSER=... PGPASSWORD=... supabase/tests/run.sh   (creates and drops a scratch database)
set -euo pipefail
cd "$(dirname "$0")/.."
DB=aro_test_$$
psql -v ON_ERROR_STOP=1 -d postgres -c "create database $DB"
trap 'psql -d postgres -c "drop database if exists $DB" >/dev/null' EXIT
run() { psql -v ON_ERROR_STOP=1 -q -d "$DB" -f "$1"; }
run tests/00_auth_stub.sql
for f in migrations/*.sql; do run "$f"; done
run seed.sql
run tests/10_game_rules.sql
