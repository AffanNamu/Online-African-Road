#!/usr/bin/env bash
# Test for supabase/top_up_jobs.sh against a throw-away database with the real migrations and seeds: cold start, steady top-up, the cap, expired jobs not counted, refusal on a foreign database,
# and that nothing but jobs is touched.
# Usage: PGHOST=... PGUSER=... PGPASSWORD=... supabase/tests/49_top_up_jobs.sh
set -euo pipefail
cd "$(dirname "$0")/.."
export PGHOST="${PGHOST:-localhost}" PGUSER="${PGUSER:-postgres}" PGPASSWORD="${PGPASSWORD:-postgres}"
DB="aro_topup_$$"; FOREIGN="aro_foreign_$$"
URL="postgresql://$PGUSER:$PGPASSWORD@$PGHOST/$DB"
trap 'psql -q -d postgres -c "drop database if exists $DB" >/dev/null 2>&1; psql -q -d postgres -c "drop database if exists $FOREIGN" >/dev/null 2>&1' EXIT
q() { psql -X -q -At -v ON_ERROR_STOP=1 -d "$DB" "$@"; }
fail() { echo "TOP-UP TEST FAILED: $*"; exit 1; }
expect() { [ "$2" = "$3" ] || fail "$1: expected [$2], got [$3]"; }
between() { [ "$3" -ge "$2" ] && [ "$3" -le "$4" ] || fail "$1: expected $2 to $4, got $3"; }   # generate_jobs may rarely make one job fewer than asked even after the retries
open() { q -c "select count(*) from jobs where status = 'open' and expires_at > now()"; }
run() { DATABASE_URL="$URL" "$@" bash top_up_jobs.sh 2>&1; }

psql -q -d postgres -c "create database $DB" >/dev/null
q -f tests/00_auth_stub.sql >/dev/null
for f in migrations/*.sql; do q -f "$f" >/dev/null; done
q -f seed.sql >/dev/null; q -f seed_world.sql >/dev/null; q -f seed_content.sql >/dev/null

# 1. cold start: an empty board is filled to COLD_TO in one run
q -c "delete from jobs" >/dev/null
out=$(run env); echo "$out"
between "cold start fills the board to about 20" 18 "$(open)" 20
echo "$out" | grep -q "almost empty" || fail "the cold start was not reported as such"

# 2. steady: below the cap it adds only a small batch; the board grows by 4, not to the cap
out=$(run env); echo "$out"
between "a steady top-up adds about the batch of 4" 22 "$(open)" 25
# generate_jobs may make fewer than asked (it skips pairs of places closer than half a kilometre), so the cap is reached within a few more runs, and never exceeded
for i in $(seq 1 20); do [ "$(open)" -ge 40 ] && break; run env >/dev/null; [ "$(open)" -le 40 ] || fail "the board went above the cap: $(open)"; done
between "the board reaches the cap and stops there" 38 "$(open)" 40

# 3. at the cap nothing happens
before=$(q -c "select count(*) from jobs")
out=$(run env); echo "$out"
echo "$out" | grep -q "nothing to do" || fail "at the cap the script should do nothing"
expect "no job was added at the cap" "$before" "$(q -c "select count(*) from jobs")"

# 4. expired jobs are not stock: expire most of the board, the script restocks it
q -c "update jobs set expires_at = now() - interval '1 minute' where id in (select id from jobs where status = 'open' order by created_at limit 35)" >/dev/null
expect "five jobs are left open" 5 "$(open)"
run env >/dev/null
between "the almost empty board is restocked to about 20" 18 "$(open)" 20

# 5. jobs that are no longer open (closed by the server) do not count, and the script touches nothing but jobs
q -c "update jobs set status = 'closed' where status = 'open'" >/dev/null
players=$(q -c "select count(*) from profiles"); tx=$(q -c "select count(*) from transactions")
run env >/dev/null
between "closed jobs are replaced by fresh ones" 18 "$(open)" 20
expect "players untouched" "$players" "$(q -c "select count(*) from profiles")"
expect "the ledger untouched" "$tx" "$(q -c "select count(*) from transactions")"

# 6. settings are validated and a foreign database is refused
if out=$(DATABASE_URL="$URL" CAP=abc bash top_up_jobs.sh 2>&1); then fail "a non-numeric setting must be refused"; fi
echo "$out" | grep -q "whole number" || fail "the setting error was not explained"
psql -q -d postgres -c "create database $FOREIGN" >/dev/null
psql -q -d "$FOREIGN" -c "create table something_else(id int)" >/dev/null
if out=$(DATABASE_URL="postgresql://$PGUSER:$PGPASSWORD@$PGHOST/$FOREIGN" bash top_up_jobs.sh 2>&1); then fail "a database without generate_jobs must be refused"; fi
echo "$out" | grep -q "REFUSING" || fail "the refusal was not explained"
expect "the foreign database was left alone" 1 "$(psql -X -q -At -d "$FOREIGN" -c "select count(*) from information_schema.tables where table_schema = 'public'")"
echo "TOP-UP TEST PASSED"
