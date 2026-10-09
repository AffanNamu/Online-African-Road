#!/usr/bin/env bash
# Real concurrent sessions racing on the same rows. $DB is the scratch database from run.sh.
set -euo pipefail
q() { psql -X -q -At -v ON_ERROR_STOP=1 -d "$DB" "$@"; }

q -c "
create temp table _x(n int);
do \$\$ declare a uuid := testkit.make_user('RaceA'); b uuid := testkit.make_user('RaceB'); asg uuid; j uuid; begin
  insert into jobs(code, origin_id, destination_id, cargo_type, cargo_weight_kg, distance_km, difficulty, reward, xp_reward, required_category, max_participants)
  select 'RACE-1', o.id, d.id, 'Test', 1000, 3.0, 2, 1000, 40, 'truck', 1 from locations o, locations d where o.slug='lagos-apapa-port' and d.slug='lagos-mile12-market' returning id into j;
  perform set_config('race.a', a::text, false); perform set_config('race.b', b::text, false);
  create table if not exists public._race(k text primary key, v text);
  insert into public._race values ('a', a::text), ('b', b::text), ('job', j::text);
end \$\$;
grant all on public._race to authenticated;"
A=$(q -c "select v from _race where k='a'"); B=$(q -c "select v from _race where k='b'"); J=$(q -c "select v from _race where k='job'")

# --- RACE 1: two players grab the single-slot job at the same moment: exactly one wins
out1=$(mktemp); out2=$(mktemp)
sess() { # uid outfile
  psql -X -q -At -d "$DB" -c "set role authenticated; select set_config('request.jwt.claim.sub','$1',false);
    begin; select pg_sleep(0.3); select 'ACCEPTED:' || accept_job('$J', (select id from vehicle_ownership where player_id='$1'))::text; commit;" >"$2" 2>&1 || true
}
sess "$A" "$out1" & sess "$B" "$out2" & wait
wins=$(cat "$out1" "$out2" | grep -c '^ACCEPTED:' || true)
[ "$wins" = "1" ] || { echo "RACE 1 FAILED: $wins winners"; cat "$out1" "$out2"; exit 1; }
grep -q "job_full" "$out1" "$out2" || { echo "RACE 1: loser did not get job_full"; cat "$out1" "$out2"; exit 1; }
echo "race 1 (single-slot job): exactly one acceptor"
WINNER=$( [ -s "$out1" ] && grep -q '^ACCEPTED:' "$out1" && echo "$A" || echo "$B" )

# --- RACE 2: the winner double-submits delivery concurrently: reward paid once
ASG=$(q -c "select id from job_assignments where job_id='$J' and player_id='$WINNER'")
q -c "select testkit.as_user('$WINNER', format('select start_job(%L,0,0)::text','$ASG')); select testkit.drive('$WINNER','$ASG',0,0,2600,-900);" >/dev/null
BEFORE=$(q -c "select balance from player_wallets where player_id='$WINNER'")
r1=$(mktemp); r2=$(mktemp)
deliver() {
  psql -X -q -At -d "$DB" -c "set role authenticated; select set_config('request.jwt.claim.sub','$WINNER',false);
    begin; select pg_sleep(0.2); select complete_job('$ASG',0)::text; select pg_sleep(0.5); commit;" >"$1" 2>&1 || true
}
deliver "$r1" & deliver "$r2" & wait
AFTER=$(q -c "select balance from player_wallets where player_id='$WINNER'")
ROWS=$(q -c "select count(*) from transactions where kind='JOB_REWARD' and ref_id='$ASG'")
[ "$ROWS" = "1" ] && [ $((AFTER-BEFORE)) -eq 1000 ] || { echo "RACE 2 FAILED rows=$ROWS delta=$((AFTER-BEFORE))"; cat "$r1" "$r2"; exit 1; }
echo "race 2 (double delivery): reward paid exactly once (+1000)"

# --- RACE 3: concurrent repairs of the same truck charge once
q -c "update vehicle_ownership set damage_pct=20 where player_id='$WINNER'; update player_wallets set balance=5000 where player_id='$WINNER';" >/dev/null
BEFORE=5000
f1=$(mktemp); f2=$(mktemp)
repair() {
  psql -X -q -At -d "$DB" -c "set role authenticated; select set_config('request.jwt.claim.sub','$WINNER',false);
    begin; select pg_sleep(0.2); select repair_vehicle((select id from vehicle_ownership where player_id='$WINNER'))::text; select pg_sleep(0.5); commit;" >"$1" 2>&1 || true
}
repair "$f1" & repair "$f2" & wait
AFTER=$(q -c "select balance from player_wallets where player_id='$WINNER'")
[ $((BEFORE-AFTER)) -eq 200 ] || { echo "RACE 3 FAILED charged $((BEFORE-AFTER)) (expected 200 once)"; cat "$f1" "$f2"; exit 1; }
echo "race 3 (double repair): charged exactly once (-200)"

# --- RACE 4: concurrent fuel purchases cannot overfill the tank or double charge
q -c "update vehicle_ownership set fuel_l=0 where player_id='$WINNER'; update player_wallets set balance=5000 where player_id='$WINNER';" >/dev/null
g1=$(mktemp); g2=$(mktemp)
fuel() {
  psql -X -q -At -d "$DB" -c "set role authenticated; select set_config('request.jwt.claim.sub','$WINNER',false);
    begin; select pg_sleep(0.2); select buy_fuel((select id from vehicle_ownership where player_id='$WINNER'), 9999)::text; select pg_sleep(0.5); commit;" >"$1" 2>&1 || true
}
fuel "$g1" & fuel "$g2" & wait
FUEL=$(q -c "select fuel_l from vehicle_ownership where player_id='$WINNER'")
SPENT=$((5000-$(q -c "select balance from player_wallets where player_id='$WINNER'")))
[ "${FUEL%.*}" = "120" ] && [ "$SPENT" -eq 240 ] || { echo "RACE 4 FAILED fuel=$FUEL spent=$SPENT"; cat "$g1" "$g2"; exit 1; }
echo "race 4 (double refuel): tank 120 L, charged exactly once (-240)"
q -c "drop table public._race" >/dev/null
echo "CONCURRENCY TESTS PASSED"
