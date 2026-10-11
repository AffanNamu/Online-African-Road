#!/usr/bin/env bash
# Migration test: upgrade a database that ALREADY holds players, money, jobs and a convoy (schema as of migration 6) to the
# current schema using the real supabase/apply.sh, and prove nothing was lost, nothing was rewritten, and the new APIs work for
# the existing players. Also proves apply.sh is re-runnable (second run applies nothing) and the seeds are idempotent.
# Usage: PGHOST=... PGUSER=... PGPASSWORD=... supabase/tests/40_upgrade.sh
set -euo pipefail
cd "$(dirname "$0")/.."
SRC="$PWD"
DB="aro_upgrade_$$"
TMP="$(mktemp -d)"
export PGHOST="${PGHOST:-localhost}" PGUSER="${PGUSER:-postgres}" PGPASSWORD="${PGPASSWORD:-postgres}"
URL="postgresql://$PGUSER:$PGPASSWORD@$PGHOST/$DB"
trap 'rm -rf "$TMP"; psql -q -d postgres -c "drop database if exists $DB" >/dev/null 2>&1' EXIT
q() { psql -X -q -At -v ON_ERROR_STOP=1 -d "$DB" "$@"; }
fail() { echo "UPGRADE TEST FAILED: $*"; exit 1; }
same() { [ "$2" = "$3" ] || fail "$1: before=[$2] after=[$3]"; }

psql -q -d postgres -c "create database $DB" >/dev/null
q -f tests/00_auth_stub.sql >/dev/null

# ---- 1. build the "existing production" state: only the first six migrations + the original seed, applied by apply.sh
mkdir -p "$TMP/old/migrations" "$TMP/old/tests"
cp seed.sql verify_live.sql api_surface.txt "$TMP/old/"
# the production tool as it was before the backend foundation: same script, minus the new seed files
sed -e 's/seed.sql, seed_world.sql, seed_content.sql/seed.sql/' -e '/^q -f seed_world.sql$/d' -e '/^q -f seed_content.sql$/d' apply.sh > "$TMP/old/apply.sh"
for f in migrations/20261009*.sql; do cp "$f" "$TMP/old/migrations/"; done
DATABASE_URL="$URL" bash "$TMP/old/apply.sh" >/dev/null
OLD=$(q -c "select count(*) from public._aro_migrations")
[ "$OLD" = "6" ] || fail "expected 6 migrations before the upgrade, found $OLD"
q -c "select 1 from information_schema.columns where table_name='profiles' and column_name='username'" | grep -q 1 && fail "old schema already has username"

# ---- 2. legacy data, made only through the legacy API (as a real game session would)
q -f tests/01_testkit.sql >/dev/null
q <<'SQL' >/dev/null
-- two players whose handles collide, one with characters a handle cannot hold, one ordinary
select testkit.make_user('Driver One');
select testkit.make_user('driver_one');
select testkit.make_user('Zoë Ünï');
select testkit.make_user('Legacy Larry');
select testkit.make_user('Legacy Lena');
SQL
LARRY=$(q -c "select id from profiles where display_name='Legacy Larry'")
LENA=$(q -c "select id from profiles where display_name='Legacy Lena'")
q -c "select testkit.mk_job('UP-1', 1, 700)" >/dev/null
ASG=$(q -c "select testkit.as_user('$LARRY', format(\$\$select accept_job((select id from jobs where code='UP-1'), %L)\$\$, (select id from vehicle_ownership where player_id='$LARRY')))")
q -c "select testkit.as_user('$LARRY', format('select start_job(%L,0,0)::text','$ASG')); select testkit.drive('$LARRY','$ASG',0,0,2600,-900);
      select testkit.age('$ASG', 5); select testkit.as_user('$LARRY', format('select submit_telemetry(%L,2600,-900)::text','$ASG'));
      select testkit.as_user('$LARRY', format('select complete_job(%L, 2)::text','$ASG'));" >/dev/null
q -c "select testkit.as_user('$LARRY', format('select buy_fuel(%L, 1)::text', (select id from vehicle_ownership where player_id='$LARRY')))" >/dev/null
CV=$(q -c "select testkit.as_user('$LARRY', \$\$select create_convoy('Legacy Convoy')\$\$)")
q -c "select testkit.as_user('$LENA', format('select join_convoy(%L)::text','$CV'))" >/dev/null
OPEN_JOBS=$(q -c "select count(*) from jobs where status='open' and expires_at > now()")

snapshot() {
  q -c "select 'profiles', md5(string_agg(concat_ws('|', id, display_name, level, experience, distance_km, jobs_completed, created_at), ';' order by id)) from profiles
        union all select 'wallets', md5(string_agg(concat_ws('|', player_id, balance), ';' order by player_id)) from player_wallets
        union all select 'ledger', md5(string_agg(concat_ws('|', id, player_id, kind, amount, balance_after, ref_type, ref_id, idempotency_key, created_at), ';' order by id)) from transactions
        union all select 'vehicles', md5(string_agg(concat_ws('|', id, player_id, definition_id, fuel_l, damage_pct, odometer_km), ';' order by id)) from vehicle_ownership
        union all select 'jobs', md5(string_agg(concat_ws('|', id, code, reward, xp_reward, status, distance_km), ';' order by id)) from jobs
        union all select 'assignments', md5(string_agg(concat_ws('|', id, job_id, player_id, status, reported_distance_km), ';' order by id)) from job_assignments
        union all select 'telemetry', md5(string_agg(concat_ws('|', assignment_id, verified_km, samples), ';' order by assignment_id)) from job_telemetry
        union all select 'convoys', md5(string_agg(concat_ws('|', id, leader_id, name, session_code), ';' order by id)) from convoys
        union all select 'members', md5(string_agg(concat_ws('|', convoy_id, player_id), ';' order by convoy_id, player_id)) from convoy_members
        union all select 'bus', md5(string_agg(concat_ws('|', code, fare), ';' order by code)) from bus_routes"
}
BEFORE=$(snapshot)
LARRY_BAL=$(q -c "select balance from player_wallets where player_id='$LARRY'")
LARRY_EARN=$(q -c "select coalesce(sum(amount),0) from transactions where player_id='$LARRY' and kind in ('JOB_REWARD','BONUS') and amount > 0")
[ "$LARRY_EARN" = "700" ] || fail "legacy delivery did not pay 700 (got $LARRY_EARN)"

# ---- 3. upgrade with the real tool, in the two stages a published database can be in:
#         (a) the sixteen migrations published before the Players Nearby opt-in (a copy of the tool without 20261012000007): this is the state the privacy fix has to repair
#         (b) the real tool, which then applies exactly the opt-in migration on top of players who already have a country
mkdir -p "$TMP/mid/migrations"
cp seed.sql seed_world.sql seed_content.sql verify_live.sql api_surface.txt apply.sh "$TMP/mid/"
for f in migrations/*.sql; do case "$f" in *20261012000007_nearby_opt_in.sql) ;; *) cp "$f" "$TMP/mid/migrations/" ;; esac; done
DATABASE_URL="$URL" bash "$TMP/mid/apply.sh" > "$TMP/upgrade.log" 2>&1 || { cat "$TMP/upgrade.log"; fail "apply.sh failed on the existing database"; }
grep -q "skip   20261009000001_core_schema.sql" "$TMP/upgrade.log" || fail "old migrations were not skipped"
grep -q "apply  20261010000007_progression_content_economy.sql" "$TMP/upgrade.log" || fail "new migrations were not applied"
grep -q "apply  20261012000006_onboarding.sql" "$TMP/upgrade.log" || fail "the onboarding migration was not applied"
grep -q "verify live schema" "$TMP/upgrade.log" && grep -q "DONE" "$TMP/upgrade.log" || fail "verify_live.sql did not pass"
same "before the opt-in migration every existing player is visible (the old default)" "5/5" "$(q -c "select count(*) filter (where appear_nearby) || '/' || count(*) from profiles")"
# the players' countries (a player with none keeps none: nothing is inferred)
q -c "update profiles set country_code = 'NG' where id = '$LARRY'; update profiles set country_code = 'GH' where id = '$LENA'" >/dev/null
PROFILE_FP() { q -c "select md5(string_agg((to_jsonb(p) - 'appear_nearby')::text, ';' order by p.id)) from profiles p"; }
COUNTRIES() { q -c "select string_agg(display_name || ':' || coalesce(country_code, '-'), ',' order by display_name) from profiles"; }
GEO() { q -c "select md5((select string_agg(c::text, ';' order by code) from countries c) || (select string_agg(c::text, ';' order by id) from cities c) || (select string_agg(l::text, ';' order by id) from locations l))"; }
FP_BEFORE=$(PROFILE_FP); COUNTRIES_BEFORE=$(COUNTRIES); GEO_BEFORE=$(GEO)
LB_BEFORE=$(q -c "select testkit.as_user('$LENA', 'select get_leaderboard(''xp'')::text')::jsonb->'items'")
DATABASE_URL="$URL" bash apply.sh > "$TMP/upgrade2.log" 2>&1 || { cat "$TMP/upgrade2.log"; fail "apply.sh failed to add the opt-in migration"; }
[ "$(grep -c '^apply ' "$TMP/upgrade2.log")" = "1" ] && grep -q "apply  20261012000007_nearby_opt_in.sql" "$TMP/upgrade2.log" || { cat "$TMP/upgrade2.log"; fail "exactly the opt-in migration should have been applied"; }
grep -q "verify live schema" "$TMP/upgrade2.log" && grep -q "DONE" "$TMP/upgrade2.log" || fail "verify_live.sql did not pass after the opt-in migration"
same "after it nobody who played before is visible in Players Nearby" "0/5" "$(q -c "select count(*) filter (where appear_nearby) || '/' || count(*) from profiles")"
same "the default for a new account is false" "false" "$(q -c "select column_default from information_schema.columns where table_schema = 'public' and table_name = 'profiles' and column_name = 'appear_nearby'")"
same "every other profile column is byte-identical (country, name, level, XP, earnings, onboarding, language...)" "$FP_BEFORE" "$(PROFILE_FP)"
same "every existing player kept their country; the player with none still has none" "$COUNTRIES_BEFORE" "$(COUNTRIES)"
same "countries, cities and locations (the map) are identical" "$GEO_BEFORE" "$(GEO)"
same "the leaderboard still carries each player's country" "$LB_BEFORE" "$(q -c "select testkit.as_user('$LENA', 'select get_leaderboard(''xp'')::text')::jsonb->'items'")"
same "an existing player can still opt in on purpose" "true" "$(q -c "select testkit.as_user('$LARRY', 'select set_nearby_visibility(true)::text')")"
q -c "select testkit.as_user('$LARRY', 'select set_nearby_visibility(false)::text')" >/dev/null
NEW=$(q -c "select count(*) from public._aro_migrations")
EXPECT=$(ls migrations/*.sql | wc -l)
same "migrations recorded" "$EXPECT" "$NEW"

# ---- 4. nothing legacy changed
AFTER=$(snapshot)
same "legacy data (profiles, wallets, ledger, vehicles, jobs, assignments, telemetry, convoys, members, bus)" "$BEFORE" "$AFTER"
same "open jobs" "$OPEN_JOBS" "$(q -c "select count(*) from jobs where status='open' and expires_at > now()")"
same "balance" "$LARRY_BAL" "$(q -c "select balance from player_wallets where player_id='$LARRY'")"

# ---- 5. backfills are right
BAD=$(q -c "select count(*) from profiles where username !~ '^[a-z0-9_]{3,24}\$'"); same "invalid usernames" "0" "$BAD"
DUP=$(q -c "select count(*) - count(distinct username) from profiles"); same "duplicate usernames" "0" "$DUP"
same "collision resolved (driver_one + a suffixed twin)" "2" "$(q -c "select count(*) from profiles where username like 'driver_one%'")"
same "unicode name gets a safe handle" "1" "$(q -c "select count(*) from profiles where display_name = 'Zoë Ünï' and username ~ '^[a-z0-9_]{3,24}\$'")"
same "lifetime earnings backfilled from the ledger" "$LARRY_EARN" "$(q -c "select lifetime_earnings from profiles where id='$LARRY'")"
same "players without earnings stay at 0" "0" "$(q -c "select sum(lifetime_earnings) from profiles where id <> '$LARRY'")"
same "players from before onboarding are not asked to onboard again" "0" "$(q -c "select count(*) from profiles where onboarded_at is null")"
same "every city has a slug" "0" "$(q -c "select count(*) from cities where slug is null")"
same "legacy cities are OPEN" "2" "$(q -c "select count(*) from cities where slug in ('lagos','ibadan') and status = 'OPEN'")"
same "the playable route exists exactly once" "1" "$(q -c "select count(*) from world_routes where code = 'lagos-ibadan'")"
same "world seed: 23 cities, 12 routes" "23/12" "$(q -c "select (select count(*) from cities) || '/' || (select count(*) from world_routes)")"
same "content seeded" "7" "$(q -c "select count(*) from content_items")"
same "legacy delivery earned the first-delivery achievement silently" "1" "$(q -c "select count(*) from player_achievements where player_id='$LARRY' and achievement_id='first_delivery'")"
same "no retroactive notifications" "0" "$(q -c "select count(*) from notifications")"
same "no retroactive analytics" "0" "$(q -c "select count(*) from analytics_events")"
same "the catalogue: 3 drivable vehicles keep their ids, 11 announced models added" "3/11" "$(q -c "select count(*) filter (where availability = 'available') || '/' || count(*) filter (where availability = 'coming_soon') from vehicle_definitions")"
same "the drivable vehicles took their torque and kerb mass from their own physics stats" "900/4200/aro_light_180" "$(q -c "select torque_nm || '/' || weight_kg || '/' || picture_key from vehicle_definitions where id = 'truck_light_01'")"
same "legacy players still own exactly what they owned (one copy of a model each)" "0" "$(q -c "select count(*) from (select player_id, definition_id from vehicle_ownership group by 1, 2 having count(*) > 1) d")"
same "the garage: 8 paint skins, and no player owns a paid skin they never bought" "8/0" "$(q -c "select (select count(*) from vehicle_skins) || '/' || (select count(*) from player_skins)")"
same "legacy convoy gained defaults only" "8/false" "$(q -c "select capacity || '/' || is_private from convoys where id='$CV'")"
same "legacy convoy members intact" "2" "$(q -c "select count(*) from convoy_members where convoy_id='$CV'")"

# ---- 6. the new APIs work for existing players, and legacy flows keep working after the upgrade
same "get_my_profile for a legacy player" "Legacy Larry" "$(q -c "select (testkit.as_user('$LARRY', 'select get_my_profile()::text')::jsonb)->>'display_name'")"
same "legacy coins visible through the new API" "$LARRY_BAL" "$(q -c "select (testkit.as_user('$LARRY', 'select get_my_profile()::text')::jsonb)->>'coins'")"
same "legacy convoy through the new API" "2" "$(q -c "select (testkit.as_user('$LENA', 'select get_my_convoy()::text')::jsonb)->'convoy'->>'member_count'")"
same "legacy progression" "1" "$(q -c "select (testkit.as_user('$LARRY', 'select get_my_progression()::text')::jsonb)->'achievements'->>'unlocked'")"
q -c "select testkit.mk_job('UP-2', 1, 300)" >/dev/null
ASG2=$(q -c "select testkit.as_user('$LENA', format(\$\$select accept_job((select id from jobs where code='UP-2'), %L)\$\$, (select id from vehicle_ownership where player_id='$LENA')))")
q -c "select testkit.as_user('$LENA', format('select start_job(%L,0,0)::text','$ASG2')); select testkit.drive('$LENA','$ASG2',0,0,2600,-900);
      select testkit.age('$ASG2', 5); select testkit.as_user('$LENA', format('select submit_telemetry(%L,2600,-900)::text','$ASG2'));
      select testkit.as_user('$LENA', format('select complete_job(%L, 0)::text','$ASG2'));" >/dev/null
same "a legacy player completes a job on the upgraded schema" "5300" "$(q -c "select balance from player_wallets where player_id='$LENA'")"
same "and the new hooks fire for them" "1" "$(q -c "select count(*) from notifications where player_id='$LENA' and type='payout'")"

# ---- 7. re-running apply.sh applies nothing and the seeds do not duplicate
STATE1=$(q -c "select (select count(*) from cities) || '/' || (select count(*) from world_routes) || '/' || (select count(*) from content_items) || '/' || (select count(*) from countries) || '/' || (select count(*) from achievement_definitions)")
DATABASE_URL="$URL" bash apply.sh > "$TMP/rerun.log" 2>&1 || { cat "$TMP/rerun.log"; fail "second apply.sh run failed"; }
grep -q "^apply " "$TMP/rerun.log" && fail "second run re-applied a migration"
STATE2=$(q -c "select (select count(*) from cities) || '/' || (select count(*) from world_routes) || '/' || (select count(*) from content_items) || '/' || (select count(*) from countries) || '/' || (select count(*) from achievement_definitions)")
same "seed idempotence" "$STATE1" "$STATE2"
echo "UPGRADE TEST PASSED: $OLD -> $NEW migrations, legacy data byte-identical, backfills correct, APIs work, apply.sh re-runnable"
