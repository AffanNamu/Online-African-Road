#!/usr/bin/env bash
# Keeps the job board stocked. Run on a schedule (see .github/workflows/supabase-jobs.yml) against the database in $DATABASE_URL.
#
# Why: jobs expire one day after they are created and the only thing that creates them is generate_jobs(), which clients cannot call. Without a schedule the board is empty
# about 24 hours after the last schema apply. This script only calls generate_jobs() (the same operator function apply.sh uses); it changes no schema and touches no player data.
#
# Rule (counts are jobs that are open and not expired):
#   * fewer than COLD_BELOW open (default 10): the board is effectively empty, so make up to COLD_TO (default 20) at once, the same as apply.sh does;
#   * fewer than CAP open (default 40): add a small batch (default 4) so that jobs are created steadily and therefore also expire steadily (a one-off batch would all expire together and empty the board again a day later);
#   * otherwise do nothing. Never more than BATCH_MAX (default 20) in one run.
set -euo pipefail
: "${DATABASE_URL:?set DATABASE_URL (the Supabase connection string; never commit it, never print it)}"
COLD_BELOW="${COLD_BELOW:-10}"; COLD_TO="${COLD_TO:-20}"; CAP="${CAP:-40}"; BATCH="${BATCH:-4}"; BATCH_MAX="${BATCH_MAX:-20}"
for v in COLD_BELOW COLD_TO CAP BATCH BATCH_MAX; do case "${!v}" in ''|*[!0-9]*) echo "$v must be a whole number, got '${!v}'"; exit 2;; esac; done
q() { psql "$DATABASE_URL" -X -q -At -v ON_ERROR_STOP=1 "$@"; }

# Safety: only our own database. If the generator or the jobs table is missing this is another database (or the schema was never applied): do nothing.
[ "$(q -c "select to_regprocedure('public.generate_jobs(integer)') is not null and to_regclass('public.jobs') is not null")" = "t" ] \
  || { echo "REFUSING: generate_jobs() or the jobs table does not exist in this database; nothing was changed."; exit 1; }

open=$(q -c "select count(*) from jobs where status = 'open' and expires_at > now()")
if [ "$open" -lt "$COLD_BELOW" ]; then want=$((COLD_TO - open)); why="the board is almost empty"
elif [ "$open" -lt "$CAP" ]; then want=$((CAP - open)); [ "$want" -gt "$BATCH" ] && want=$BATCH; why="steady top-up"
else echo "open jobs: $open (cap $CAP): nothing to do"; exit 0; fi
[ "$want" -gt "$BATCH_MAX" ] && want=$BATCH_MAX
[ "$want" -ge 1 ] || { echo "open jobs: $open: nothing to do"; exit 0; }

# generate_jobs() may make fewer than asked (it skips pairs of places closer than half a kilometre): ask again for the rest, at most three times
made=0; left=$want
for attempt in 1 2 3; do
  got=$(q -c "select generate_jobs($left)"); made=$((made + got)); left=$((want - made))
  [ "$left" -le 0 ] && break
done
after=$(q -c "select count(*) from jobs where status = 'open' and expires_at > now()")
echo "open jobs: $open -> $after ($why; asked for $want, generate_jobs made $made)"
[ "$after" -ge 1 ] || { echo "PROBLEM: no open job after generating"; exit 1; }
