#!/usr/bin/env bash
# Usage: PGHOST=... PGUSER=... PGPASSWORD=... supabase/tests/run.sh
# Optional: MIGRATIONS_DIR=<dir> to test a mutated copy (used by mutation.py).
set -euo pipefail
cd "$(dirname "$0")/.."
MIG="${MIGRATIONS_DIR:-migrations}"
export DB=aro_test_$$
psql -v ON_ERROR_STOP=1 -q -d postgres -c "create database $DB"
trap 'psql -q -d postgres -c "drop database if exists $DB" >/dev/null 2>&1' EXIT
run() { psql -v ON_ERROR_STOP=1 -q -d "$DB" -f "$1"; }
run tests/00_auth_stub.sql
for f in "$MIG"/*.sql; do run "$f"; done
run seed.sql
run seed_world.sql
run seed_content.sql
run tests/01_testkit.sql
run tests/20_security_matrix.sql
run tests/25_bus_and_shop.sql
run tests/35_profile_presence.sql
run tests/36_social.sql
run tests/37_world_jobs_economy.sql
run tests/38_content_boards_security.sql
if [ -z "${SKIP_CONCURRENCY:-}" ]; then bash tests/30_concurrency.sh; fi
