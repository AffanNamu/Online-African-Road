\set ON_ERROR_STOP on
-- Bus system + vehicle shop (migration 5). Maps to docs/SECURITY.md section "Bus system and shop".

create temp table fx(k text primary key, v text);
insert into fx values ('A', testkit.make_user('BusA')::text), ('B', testkit.make_user('BusB')::text), ('D', testkit.make_user('BusD')::text);
create function pg_temp.u(k text) returns uuid language sql as $$ select v::uuid from fx where k = $1 $$;
create function pg_temp.bus(k text) returns uuid language sql as $$ select o.id from vehicle_ownership o where o.player_id = pg_temp.u($1) and o.definition_id = 'bus_city_01' order by acquired_at limit 1 $$;
create function pg_temp.truck(k text) returns uuid language sql as $$ select o.id from vehicle_ownership o where o.player_id = pg_temp.u($1) and o.definition_id = 'truck_light_01' limit 1 $$;
create function pg_temp.route(c text) returns uuid language sql as $$ select id from bus_routes where code = $1 $$;
create function pg_temp.bal(k text) returns bigint language sql as $$ select balance from player_wallets where player_id = pg_temp.u($1) $$;
create function pg_temp.fund(k text, amt bigint) returns void language sql as $$
  select _apply_transaction(pg_temp.u($1), 'BONUS', $2, 'fund:' || gen_random_uuid()) $$;
create function pg_temp.loc(slug text, out x double precision, out z double precision) language sql as $$
  select world_x, world_z from locations where slug = $1 $$;
-- Start a run for player k as that player at the first stop; returns run id.
create function pg_temp.start_run(k text, route text) returns uuid language sql as $$
  select testkit.as_user(pg_temp.u($1), format('select start_bus_run(%L,%L,2600,-900)', pg_temp.route($2), pg_temp.bus($1)))::uuid $$;
create function pg_temp.serve(k text, run uuid) returns jsonb language sql as $$
  select testkit.as_user(pg_temp.u($1), format('select serve_stop(%L)::text', $2))::jsonb $$;
create function pg_temp.at(run uuid, x double precision, z double precision, last_stop_ago numeric default null) returns void language sql as $$
  update bus_runs set last_x = $2, last_z = $3, last_at = now(),
         last_stop_at = case when $4 is null then last_stop_at else now() - make_interval(secs => $4) end where id = $1 $$;

-- ===== S1: seed sanity
select testkit.assert_eq('route seeded', (select count(*)::text from bus_route_stops where route_id = pg_temp.route('LAG-R1')), '3');

-- ===== S2: buy_vehicle
select pg_temp.fund('A', 100000);
select testkit.expect_error('authenticated', pg_temp.u('A'), $$select buy_vehicle('no_such_vehicle')$$, 'unknown_vehicle');
select testkit.expect_error('authenticated', pg_temp.u('A'), $$select buy_vehicle('truck_light_01')$$, 'not_for_sale');
select testkit.expect_error('anon', null, $$select buy_vehicle('bus_city_01')$$, 'permission denied');
do $$ declare before bigint := pg_temp.bal('A'); vid uuid; begin
  vid := testkit.as_user(pg_temp.u('A'), $q$select buy_vehicle('bus_city_01')$q$)::uuid;
  perform testkit.assert_eq('price debited', pg_temp.bal('A')::text, (before - 45000)::text);
  perform testkit.assert_eq('ledger row', (select count(*)::text from transactions where player_id = pg_temp.u('A') and kind = 'VEHICLE_PURCHASE' and amount = -45000), '1');
  perform testkit.assert_eq('owned', (select count(*)::text from vehicle_ownership where id = vid and player_id = pg_temp.u('A')), '1');
  perform testkit.assert_eq('full tank', (select fuel_l::text from vehicle_ownership where id = vid), '180');
  perform testkit.assert_eq('no damage', (select damage_pct::text from vehicle_ownership where id = vid), '0');
end $$;
-- B has 5000: 44999 total is one coin short, 45000 is exactly enough.
select pg_temp.fund('B', 39999);
select testkit.expect_error('authenticated', pg_temp.u('B'), $$select buy_vehicle('bus_city_01')$$, 'insufficient_funds');
select testkit.assert_eq('failed purchase: balance unchanged', pg_temp.bal('B')::text, '44999');
select testkit.assert_eq('failed purchase: no vehicle', (select count(*)::text from vehicle_ownership where player_id = pg_temp.u('B') and definition_id = 'bus_city_01'), '0');
select pg_temp.fund('B', 1);
select testkit.as_user(pg_temp.u('B'), $q$select buy_vehicle('bus_city_01')$q$);
select testkit.assert_eq('exact-funds purchase empties wallet', pg_temp.bal('B')::text, '0');
select testkit.expect_error('authenticated', pg_temp.u('B'), $$select buy_vehicle('bus_city_01')$$, 'insufficient_funds');
-- D gets a bus too (used for the job-conflict test)
select pg_temp.fund('D', 100000);
select testkit.as_user(pg_temp.u('D'), $q$select buy_vehicle('bus_city_01')$q$);

-- ===== S3: start_bus_run rejections
select testkit.expect_error('authenticated', pg_temp.u('A'), format('select start_bus_run(%L,%L,2600,-900)', pg_temp.route('LAG-R1'), pg_temp.truck('A')), 'wrong_vehicle_category');
select testkit.expect_error('authenticated', pg_temp.u('B'), format('select start_bus_run(%L,%L,2600,-900)', pg_temp.route('LAG-R1'), pg_temp.bus('A')), 'vehicle_not_owned');
select testkit.expect_error('authenticated', pg_temp.u('A'), format('select start_bus_run(%L,%L,0,0)', pg_temp.route('LAG-R1'), pg_temp.bus('A')), 'not_at_stop');
select testkit.expect_error('authenticated', pg_temp.u('A'), format('select start_bus_run(%L,%L,2600,-779)', pg_temp.route('LAG-R1'), pg_temp.bus('A')), 'not_at_stop');
select testkit.expect_error('authenticated', pg_temp.u('A'), format('select start_bus_run(%L,%L,2600,-900)', gen_random_uuid(), pg_temp.bus('A')), 'route_not_found');
select testkit.expect_error('anon', null, format('select start_bus_run(%L,%L,2600,-900)', pg_temp.route('LAG-R1'), pg_temp.bus('A')), 'permission denied');
update vehicle_ownership set fuel_l = 0 where id = pg_temp.bus('B');
select testkit.expect_error('authenticated', pg_temp.u('B'), format('select start_bus_run(%L,%L,2600,-900)', pg_temp.route('LAG-R1'), pg_temp.bus('B')), 'out_of_fuel');
update vehicle_ownership set fuel_l = 180, damage_pct = 100 where id = pg_temp.bus('B');
select testkit.expect_error('authenticated', pg_temp.u('B'), format('select start_bus_run(%L,%L,2600,-900)', pg_temp.route('LAG-R1'), pg_temp.bus('B')), 'vehicle_destroyed');
update vehicle_ownership set damage_pct = 0 where id = pg_temp.bus('B');
-- cannot run a bus while holding a freight job
select testkit.mk_job('BUS-CONFLICT', 1);
select testkit.as_user(pg_temp.u('D'), format('select accept_job(%L,%L)', (select id from jobs where code = 'BUS-CONFLICT'), pg_temp.truck('D')));
select testkit.expect_error('authenticated', pg_temp.u('D'), format('select start_bus_run(%L,%L,2600,-900)', pg_temp.route('LAG-R1'), pg_temp.bus('D')), 'job_active');

-- ===== S4: telemetry, flagging, staleness, ownership
create temp table runs(k text primary key, id uuid);
insert into runs values ('t1', pg_temp.start_run('A', 'LAG-R1'));
select testkit.expect_error('authenticated', pg_temp.u('A'), format('select start_bus_run(%L,%L,2600,-900)', pg_temp.route('LAG-R1'), pg_temp.bus('A')), 'run_active');
select testkit.expect_error('authenticated', pg_temp.u('B'), format('select submit_bus_telemetry(%L,2600,-890)', (select id from runs where k='t1')), 'run_not_found');
select testkit.expect_error('authenticated', pg_temp.u('B'), format('select serve_stop(%L)', (select id from runs where k='t1')), 'run_not_found');
select testkit.expect_error('authenticated', pg_temp.u('B'), format('select abandon_bus_run(%L)', (select id from runs where k='t1')), 'run_not_found');
-- rate limit: an immediate second sample is refused without error
select testkit.assert_eq('rate limited', testkit.as_user(pg_temp.u('A'), format('select submit_bus_telemetry(%L,2600,-899)::text', (select id from runs where k='t1')))::jsonb->>'reason', 'rate_limited');
-- a 1 km jump in 5 s is a teleport: rejected, verified distance unchanged
select testkit.age_bus((select id from runs where k='t1'), 5);
select testkit.assert_eq('teleport rejected', testkit.as_user(pg_temp.u('A'), format('select submit_bus_telemetry(%L,3600,-900)::text', (select id from runs where k='t1')))::jsonb->>'reason', 'too_fast');
select testkit.assert_eq('teleport added no km', (select verified_km::text from bus_runs where id = (select id from runs where k='t1')), '0');
select testkit.assert_eq('position not moved by teleport', (select last_x::text from bus_runs where id = (select id from runs where k='t1')), '2600');
-- 9 more teleports -> flagged; after that even a legit sample and serve_stop are refused
do $$ declare i int; rid uuid := (select id from runs where k='t1'); begin
  for i in 1..9 loop
    perform testkit.age_bus(rid, 5);
    perform testkit.as_user(pg_temp.u('A'), format('select submit_bus_telemetry(%L,%s,-900)', rid, 3600 + i * 100));
  end loop;
  perform testkit.assert_eq('flagged after 10', (select flagged::text from bus_runs where id = rid), 'true');
  perform testkit.age_bus(rid, 5);
  perform testkit.assert_eq('flagged run refuses legit sample', testkit.as_user(pg_temp.u('A'), format('select submit_bus_telemetry(%L,2601,-900)::text', rid))::jsonb->>'reason', 'flagged');
end $$;
select testkit.expect_error('authenticated', pg_temp.u('A'), format('select serve_stop(%L)', (select id from runs where k='t1')), 'telemetry_flagged');
select testkit.as_user(pg_temp.u('A'), format('select abandon_bus_run(%L)', (select id from runs where k='t1')));
select testkit.expect_error('authenticated', pg_temp.u('A'), format('select submit_bus_telemetry(%L,2600,-900)', (select id from runs where k='t1')), 'run_not_active');
select testkit.expect_error('authenticated', pg_temp.u('A'), format('select serve_stop(%L)', (select id from runs where k='t1')), 'run_not_active');
-- stale telemetry
insert into runs values ('t2', pg_temp.start_run('A', 'LAG-R1'));
select testkit.age_bus((select id from runs where k='t2'), 31);
select testkit.expect_error('authenticated', pg_temp.u('A'), format('select serve_stop(%L)', (select id from runs where k='t2')), 'telemetry_stale');
select testkit.as_user(pg_temp.u('A'), format('select abandon_bus_run(%L)', (select id from runs where k='t2')));

-- ===== S5: stop serving with shortcuts (positions set by the fixture, not by the client)
insert into runs values ('s1', pg_temp.start_run('A', 'LAG-R1'));
do $$ declare
  rid uuid := (select id from runs where k='s1'); r jsonb; w0 bigint := pg_temp.bal('A'); xp0 bigint; jc0 int; ox float; oz float; ix float; iz float; fare_sum bigint;
begin
  select * into ox, oz from pg_temp.loc('lagos-oshodi-terminal'); select * into ix, iz from pg_temp.loc('lagos-ikeja-bus-park');
  select experience, jobs_completed into xp0, jc0 from profiles where id = pg_temp.u('A');
  -- stop 1: boards 18..30, nobody alights, no fare
  r := pg_temp.serve('A', rid);
  perform testkit.assert_eq('stop1 no fare', r->>'fare', '0');
  if (r->>'boarded')::int not between 18 and 30 then raise exception 'stop1 boarded out of demand range: %', r; end if;
  perform testkit.assert_eq('stop1 aboard = boarded', r->>'aboard', r->>'boarded');
  -- replay of the same stop: the bus is still at stop 1 but the next stop is Oshodi
  perform testkit.expect_error('authenticated', pg_temp.u('A'), format('select serve_stop(%L)', rid), 'not_at_stop');
  -- at Oshodi but only 10 s after the previous stop: physically impossible
  perform pg_temp.at(rid, ox, oz, 10);
  perform testkit.expect_error('authenticated', pg_temp.u('A'), format('select serve_stop(%L)', rid), 'stop_too_soon');
  -- 61 s is enough for 1.9 km at <=112 km/h
  perform pg_temp.at(rid, ox, oz, 61);
  r := pg_temp.serve('A', rid);
  perform testkit.assert_eq('stop2 half alight', r->>'alighted', ceil((select boarded from bus_run_stops where run_id = rid and seq = 1) / 2.0)::int::text);
  perform testkit.assert_eq('stop2 fare = alighted * 15', r->>'fare', ((r->>'alighted')::int * 15)::text);
  if (r->>'aboard')::int > 40 then raise exception 'over capacity: %', r; end if;
  -- 31 m away is inside the 60 m radius, 80 m is not
  perform pg_temp.at(rid, ix + 80, iz, 600);
  perform testkit.expect_error('authenticated', pg_temp.u('A'), format('select serve_stop(%L)', rid), 'not_at_stop');
  perform pg_temp.at(rid, ix + 31, iz, 600);
  r := pg_temp.serve('A', rid);
  perform testkit.assert_eq('completed', r->>'completed', 'true');
  perform testkit.assert_eq('all alight at terminus', (select aboard::text from bus_runs where id = rid), '0');
  select sum(fare_paid) into fare_sum from bus_run_stops where run_id = rid;
  perform testkit.assert_eq('revenue = sum of stop fares', (select revenue::text from bus_runs where id = rid), fare_sum::text);
  perform testkit.assert_eq('wallet got exactly the fares', pg_temp.bal('A')::text, (w0 + fare_sum)::text);
  perform testkit.assert_eq('every passenger paid once',
    fare_sum::text, ((select sum(alighted) from bus_run_stops where run_id = rid) * 15)::text);
  perform testkit.assert_eq('everyone who boarded alighted', (select sum(boarded)::text from bus_run_stops where run_id = rid), (select sum(alighted)::text from bus_run_stops where run_id = rid));
  perform testkit.assert_eq('xp awarded once', (select (experience - xp0)::text from profiles where id = pg_temp.u('A')), '150');
  perform testkit.assert_eq('jobs_completed +1', (select (jobs_completed - jc0)::text from profiles where id = pg_temp.u('A')), '1');
  perform testkit.assert_eq('status', (select status::text from bus_runs where id = rid), 'completed');
end $$;
select testkit.expect_error('authenticated', pg_temp.u('A'), format('select serve_stop(%L)', (select id from runs where k='s1')), 'run_not_active');
select testkit.expect_error('authenticated', pg_temp.u('A'), format('select submit_bus_telemetry(%L,0,0)', (select id from runs where k='s1')), 'run_not_active');
select testkit.assert_eq('no double payout rows', (select count(*)::text from transactions where idempotency_key like 'busstop:' || (select id from runs where k='s1') || '%' and amount <= 0), '0');

-- ===== S6: capacity is enforced server-side (fixture route with huge demand and nobody alighting midway)
insert into bus_routes(code, name, city_id, fare, xp_reward) select 'TEST-CAP', 'Capacity test', id, 15, 10 from cities where name = 'Lagos';
insert into bus_route_stops(route_id, seq, location_id, demand, alight_pct)
select pg_temp.route('TEST-CAP'), v.seq, l.id, v.d, v.a from (values (1,'lagos-mile12-market',80,0),(2,'lagos-oshodi-terminal',80,0),(3,'lagos-ikeja-bus-park',0,100)) v(seq,slug,d,a)
join locations l on l.slug = v.slug;
insert into runs values ('cap', pg_temp.start_run('A', 'TEST-CAP'));
do $$ declare rid uuid := (select id from runs where k='cap'); r jsonb; w0 bigint := pg_temp.bal('A'); ox float; oz float; ix float; iz float; begin
  select * into ox, oz from pg_temp.loc('lagos-oshodi-terminal'); select * into ix, iz from pg_temp.loc('lagos-ikeja-bus-park');
  r := pg_temp.serve('A', rid);
  perform testkit.assert_eq('bus filled to seat capacity, not demand', r->>'aboard', '40');
  perform pg_temp.at(rid, ox, oz, 61);
  r := pg_temp.serve('A', rid);
  perform testkit.assert_eq('full bus boards nobody', r->>'boarded', '0');
  perform testkit.assert_eq('still 40 aboard', r->>'aboard', '40');
  perform pg_temp.at(rid, ix, iz, 61);
  r := pg_temp.serve('A', rid);
  perform testkit.assert_eq('terminus pays all 40', r->>'fare', '600');
  perform testkit.assert_eq('wallet +600', pg_temp.bal('A')::text, (w0 + 600)::text);
end $$;

-- ===== S7: a legitimately driven run: verified distance, fuel burn
insert into runs values ('d1', pg_temp.start_run('A', 'LAG-R1'));
do $$ declare rid uuid := (select id from runs where k='d1'); r jsonb; ox float; oz float; ix float; iz float; f0 numeric; km numeric; f1 numeric; begin
  select * into ox, oz from pg_temp.loc('lagos-oshodi-terminal'); select * into ix, iz from pg_temp.loc('lagos-ikeja-bus-park');
  select fuel_l into f0 from vehicle_ownership where id = pg_temp.bus('A');
  perform pg_temp.serve('A', rid);
  perform testkit.drive_bus(pg_temp.u('A'), rid, 2600, -900, ox, oz, 60);
  perform pg_temp.serve('A', rid);
  perform testkit.drive_bus(pg_temp.u('A'), rid, ox, oz, ix, iz, 60);
  r := pg_temp.serve('A', rid);
  perform testkit.assert_eq('driven run completes', r->>'completed', 'true');
  select verified_km into km from bus_runs where id = rid;
  if km not between 3.6 and 3.8 then raise exception 'verified km implausible: %', km; end if;
  select fuel_l into f1 from vehicle_ownership where id = pg_temp.bus('A');
  if abs((f0 - f1) - km * 0.28) > 0.001 then raise exception 'fuel burn wrong: burned % for % km', f0 - f1, km; end if;
  if abs((select odometer_km from vehicle_ownership where id = pg_temp.bus('A')) - km) > 0.000001 then raise exception 'odometer not advanced by verified km'; end if;
end $$;

-- ===== S8: abandon frees the player to start again; abandon pays nothing
do $$ declare w0 bigint := pg_temp.bal('A'); rid uuid; begin
  rid := pg_temp.start_run('A', 'LAG-R1');
  perform testkit.as_user(pg_temp.u('A'), format('select abandon_bus_run(%L)', rid));
  perform testkit.assert_eq('abandon pays nothing', pg_temp.bal('A')::text, w0::text);
  perform testkit.assert_eq('abandoned', (select status::text from bus_runs where id = rid), 'abandoned');
  rid := pg_temp.start_run('A', 'LAG-R1');   -- allowed again
  perform testkit.as_user(pg_temp.u('A'), format('select abandon_bus_run(%L)', rid));
end $$;

-- ===== S9: direct access is closed, RLS isolates players
select testkit.expect_error('authenticated', pg_temp.u('A'), $$update bus_runs set revenue = 99999999$$, 'permission denied');
select testkit.expect_error('authenticated', pg_temp.u('A'), $$update bus_runs set status = 'completed'$$, 'permission denied');
select testkit.expect_error('authenticated', pg_temp.u('A'), $$insert into bus_runs(route_id, player_id, vehicle_id, last_x, last_z) select route_id, auth.uid(), vehicle_id, 0, 0 from bus_runs limit 1$$, 'permission denied');
select testkit.expect_error('authenticated', pg_temp.u('A'), $$insert into bus_run_stops(run_id, seq, boarded, alighted, fare_paid) select id, 9, 0, 0, 0 from bus_runs limit 1$$, 'permission denied');
select testkit.expect_error('authenticated', pg_temp.u('A'), $$update bus_routes set fare = 500$$, 'permission denied');
select testkit.expect_error('authenticated', pg_temp.u('A'), $$update bus_route_stops set demand = 80$$, 'permission denied');
select testkit.expect_error('authenticated', pg_temp.u('A'), $$insert into bus_routes(code, name, city_id, fare, xp_reward) select 'EVIL', 'x', id, 500, 99999 from cities limit 1$$, 'permission denied');
select testkit.expect_error('authenticated', pg_temp.u('A'), $$delete from bus_runs$$, 'permission denied');
select testkit.expect_error('anon', null, format('select serve_stop(%L)', gen_random_uuid()), 'permission denied');
select testkit.expect_error('anon', null, format('select submit_bus_telemetry(%L,0,0)', gen_random_uuid()), 'permission denied');
select testkit.expect_error('anon', null, format('select abandon_bus_run(%L)', gen_random_uuid()), 'permission denied');
select testkit.expect_error('anon', null, 'select * from bus_runs', 'permission denied');
select testkit.expect_error('anon', null, 'select * from bus_routes', 'permission denied');
select testkit.assert_eq('B sees none of A''s runs', testkit.as_user(pg_temp.u('B'), 'select count(*) from bus_runs'), '0');
select testkit.assert_eq('B sees none of A''s stop logs', testkit.as_user(pg_temp.u('B'), 'select count(*) from bus_run_stops'), '0');
select testkit.assert_eq('A sees own runs', testkit.as_user(pg_temp.u('A'), 'select count(*) from bus_runs'), (select count(*)::text from bus_runs where player_id = pg_temp.u('A')));
select testkit.assert_eq('A sees own stop logs', testkit.as_user(pg_temp.u('A'), 'select count(*) from bus_run_stops'), (select count(*)::text from bus_run_stops s join bus_runs r on r.id = s.run_id where r.player_id = pg_temp.u('A')));
select testkit.assert_eq('routes readable', testkit.as_user(pg_temp.u('B'), 'select count(*) from bus_routes where code = ''LAG-R1'''), '1');

-- ===== S10: ledger invariant still holds for every player touched here
do $$ begin
  if exists (select 1 from player_wallets w where w.player_id in (pg_temp.u('A'), pg_temp.u('B'), pg_temp.u('D'))
             and w.balance <> (select coalesce(sum(amount),0) from transactions t where t.player_id = w.player_id))
  then raise exception 'ledger sum != wallet balance (bus/shop)'; end if;
end $$;
\echo BUS AND SHOP TESTS PASSED
