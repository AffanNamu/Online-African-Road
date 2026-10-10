#!/usr/bin/env bash
# Applies supabase/migrations/*.sql (once each, tracked in public._aro_migrations) and supabase/seed.sql to the database in $DATABASE_URL.
# Refuses to touch a database that already has tables with our names but was not created by this script (i.e. another app's database).
set -euo pipefail
: "${DATABASE_URL:?set DATABASE_URL (Supabase connection string, never commit it)}"
cd "$(dirname "$0")"
q() { psql "$DATABASE_URL" -X -q -v ON_ERROR_STOP=1 "$@"; }

OURS="countries cities locations vehicle_definitions profiles player_wallets transactions vehicle_ownership jobs job_assignments convoys convoy_members job_telemetry bus_routes bus_route_stops bus_runs bus_run_stops"
marker=$(q -At -c "select to_regclass('public._aro_migrations') is not null")
if [ "$marker" != "t" ]; then
  clash=""
  for t in $OURS; do [ "$(q -At -c "select to_regclass('public.$t') is not null")" = "t" ] && clash="$clash $t"; done
  # A brand-new dedicated project has no tables in public and no players. Anything else means the project is in use by something else:
  # stop and report (names only, never data) so a human decides.
  others=$(q -At -c "select coalesce(string_agg(table_name, ' ' order by table_name), '') from information_schema.tables where table_schema = 'public' and table_type = 'BASE TABLE'")
  users=$(q -At -c "select count(*) from auth.users")
  if [ -n "$others" ] || [ "$users" != "0" ]; then
    echo "REFUSING TO RUN: this Supabase project is not empty (public tables: [${others:-none}], auth users: $users)."
    echo "African Roads Online installs a trigger on auth.users and tables in public; use a dedicated, brand-new project."
    exit 1
  fi
  if [ -n "$clash" ]; then
    echo "REFUSING TO RUN: these tables already exist and were not created by this project:$clash"
    echo "This looks like another application's database. Use a dedicated, empty Supabase project for African Roads Online."
    exit 1
  fi
  q -c "create table public._aro_migrations(name text primary key, applied_at timestamptz not null default now());
        alter table public._aro_migrations enable row level security;
        revoke all on public._aro_migrations from anon, authenticated;"
fi

for f in migrations/*.sql; do
  n=$(basename "$f")
  if [ "$(q -At -c "select exists (select 1 from public._aro_migrations where name = '$n')")" = "t" ]; then echo "skip   $n (already applied)"; continue; fi
  echo "apply  $n"
  q -1 -f "$f"
  q -c "insert into public._aro_migrations(name) values ('$n')"
done

echo "seed   seed.sql (idempotent)"
q -f seed.sql

open=$(q -At -c "select count(*) from jobs where status = 'open' and expires_at > now()")
if [ "$open" -lt 10 ]; then echo "jobs   generating (only $open open)"; q -c "select generate_jobs(20)" >/dev/null; fi

echo "verify live schema"
q -f verify_live.sql
echo "DONE"
