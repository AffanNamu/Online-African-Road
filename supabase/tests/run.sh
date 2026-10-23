#!/usr/bin/env bash
# Usage: PGHOST=... PGUSER=... PGPASSWORD=... supabase/tests/run.sh
# Optional: MIGRATIONS_DIR=<dir> to test a mutated copy (used by mutation.py).
set -euo pipefail
cd "$(dirname "$0")/.."
MIG="${MIGRATIONS_DIR:-migrations}"
export DB=aro_test_$$
psql -v ON_ERROR_STOP=1 -q -d postgres -c "create database $DB"
trap 'psql -q -d postgres -c "drop database if exists $DB" >/dev/null 2>&1' EXIT
run() { psql -v ON_ERROR_STOP=1 -v MIG="$MIG" -q -d "$DB" -f "$1"; }
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
run tests/39_job_catalog.sql
run tests/41_convoy_lobby.sql
run tests/42_convoy_spec.sql
run tests/43_vehicle_catalog.sql
run tests/44_garage.sql
run tests/45_person_vehicle.sql
run tests/46_leaderboard_social.sql
run tests/47_onboarding.sql
run tests/48_nearby_opt_in.sql
run tests/50_admin.sql
run tests/51_admin_content_moderation.sql
run tests/52_account_status.sql
if [ -z "${SKIP_CONCURRENCY:-}" ]; then bash tests/30_concurrency.sh; fi
