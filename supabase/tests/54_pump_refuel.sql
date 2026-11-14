\set ON_ERROR_STOP on
-- Refuel at a pump (migration 20261114000001_pump_refuel.sql): buy_fuel with the game's current fuel reading.
-- A lower reading is used, a higher one ignored, a negative one refused; mid-job and mid-bus-run refuels are never deducted twice;
-- an abandoned activity never leaves more than a full tank; price and ledger are those of the Garage refuel.
create temp table fx(k text primary key, v text);
insert into fx select k, testkit.make_user(n)::text from (values ('P','PumpP'),('Q','PumpQ')) t(k, n);
create function pg_temp.u(k text) returns uuid language sql as $$ select v::uuid from fx where fx.k = $1 $$;
create function pg_temp.veh(k text) returns uuid language sql as $$ select id from vehicle_ownership where player_id = pg_temp.u($1) and definition_id = 'truck_light_01' $$;
create function pg_temp.bus(k text) returns uuid language sql as $$ select id from vehicle_ownership where player_id = pg_temp.u($1) and definition_id = 'bus_city_01' $$;
create function pg_temp.asg(k text) returns uuid language sql as $$ select v::uuid from fx where fx.k = $1 $$;
create function pg_temp.bal(k text) returns bigint language sql as $$ select balance from player_wallets where player_id = pg_temp.u($1) $$;
create function pg_temp.fuel(v uuid) returns numeric language sql as $$ select fuel_l from vehicle_ownership where id = $1 $$;
create function pg_temp.set_fuel(v uuid, l numeric) returns void language sql as $$ update vehicle_ownership set fuel_l = $2 where id = $1 $$;
create function pg_temp.pump(k text, v uuid, liters numeric, reading numeric) returns jsonb language sql as $$
  select testkit.as_user(pg_temp.u($1), format('select buy_fuel(%L, %s, %s)::text', $2, $3, coalesce(quote_literal($4::text) || '::numeric', 'null')))::jsonb $$;
create function pg_temp.r6(x numeric) returns text language sql as $$ select round($1, 6)::text $$;

-- ===== the reading (the starter truck: 120 L, 0.22 L/km)
select testkit.assert_eq('starter truck capacity', (select fuel_capacity_l::text from vehicle_definitions where id = 'truck_light_01'), '120');
select pg_temp.set_fuel(pg_temp.veh('P'), 50);
do $$ declare b0 bigint := pg_temp.bal('P'); r jsonb; begin
  r := pg_temp.pump('P', pg_temp.veh('P'), 9999, 30);
  perform testkit.assert_eq('a LOWER reading is used: litres from 30 to full', r->>'liters', '90');
  perform testkit.assert_eq('priced like the Garage refuel', r->>'cost', '180');
  perform testkit.assert_eq('charged exactly the answer', pg_temp.bal('P')::text, (b0 - 180)::text);
  perform testkit.assert_eq('the answer gives the level after the pump', r->>'fuel_l', '120');
  perform testkit.assert_eq('stored: full', pg_temp.fuel(pg_temp.veh('P'))::text, '120');
  perform testkit.assert_eq('one ledger row of kind FUEL_PURCHASE', (select count(*)::text from transactions where player_id = pg_temp.u('P') and kind = 'FUEL_PURCHASE'), '1');
end $$;
select pg_temp.set_fuel(pg_temp.veh('P'), 50);
select testkit.assert_eq('a HIGHER reading is ignored: litres from the stored 50', pg_temp.pump('P', pg_temp.veh('P'), 9999, 80)->>'liters', '70');
select pg_temp.set_fuel(pg_temp.veh('P'), 50);
select testkit.assert_eq('no reading: the Garage behaviour', pg_temp.pump('P', pg_temp.veh('P'), 9999, null)->>'liters', '70');
select pg_temp.set_fuel(pg_temp.veh('P'), 50);
select testkit.assert_eq('a NaN reading is ignored', pg_temp.pump('P', pg_temp.veh('P'), 9999, 'NaN')->>'liters', '70');
select pg_temp.set_fuel(pg_temp.veh('P'), 50);
select testkit.assert_eq('the two-argument Garage call still works', testkit.as_user(pg_temp.u('P'), format($$select buy_fuel(%L, 9999)->>'liters'$$, pg_temp.veh('P'))), '70');
select pg_temp.set_fuel(pg_temp.veh('P'), 50);
select testkit.expect_error('authenticated', pg_temp.u('P'), format('select buy_fuel(%L, 10, -1)', pg_temp.veh('P')), 'invalid_reading');
select testkit.expect_error('authenticated', pg_temp.u('P'), format('select buy_fuel(%L, 0, 10)', pg_temp.veh('P')), 'invalid_amount');
select testkit.expect_error('authenticated', pg_temp.u('Q'), format('select buy_fuel(%L, 10, 10)', pg_temp.veh('P')), 'vehicle_not_owned');
select testkit.expect_error('anon', null, format('select buy_fuel(%L, 10, 10)', pg_temp.veh('P')), 'permission denied');
select testkit.assert_eq('refusals stored nothing', pg_temp.fuel(pg_temp.veh('P'))::text, '50');
select pg_temp.set_fuel(pg_temp.veh('P'), 120);
select testkit.expect_error('authenticated', pg_temp.u('P'), format('select buy_fuel(%L, 10, 120)', pg_temp.veh('P')), 'tank_full');
select testkit.assert_eq('a full tank by the server, but the game reads 100: 10 litres are sold', pg_temp.pump('P', pg_temp.veh('P'), 10, 100)->>'liters', '10');
select testkit.assert_eq('and stored as 110', pg_temp.fuel(pg_temp.veh('P'))::text, '110');
update player_wallets set balance = 5 where player_id = pg_temp.u('P');
select pg_temp.set_fuel(pg_temp.veh('P'), 50);
select testkit.expect_error('authenticated', pg_temp.u('P'), format('select buy_fuel(%L, 9999, 20)', pg_temp.veh('P')), 'insufficient_funds');
select testkit.assert_eq('a refused purchase changes no fuel', pg_temp.fuel(pg_temp.veh('P'))::text, '50');
select testkit.assert_eq('nor money', pg_temp.bal('P')::text, '5');
select _apply_transaction(pg_temp.u('P'), 'BONUS', 100000, 'fund:pump');

-- ===== mid-job: refuel halfway, complete, the burn before the pump is not deducted twice
select testkit.mk_job('PUMP-1');
select testkit.mk_job('PUMP-2');
select pg_temp.set_fuel(pg_temp.veh('P'), 60);
insert into fx select 'p1', testkit.as_user(pg_temp.u('P'), format($$select accept_job((select id from jobs where code='PUMP-1'), %L)$$, pg_temp.veh('P')));
select testkit.as_user(pg_temp.u('P'), format('select start_job(%L, 3, -2)::text', pg_temp.asg('p1')));
select testkit.drive(pg_temp.u('P'), pg_temp.asg('p1'), 3, -2, 1300, -450);
create temp table mid as select verified_km as km1 from job_telemetry where assignment_id = pg_temp.asg('p1');
do $$ declare km1 numeric := (select km1 from mid); used numeric := km1 * 0.22; reading numeric := 60 - used - 0.4; r jsonb; begin
  perform testkit.assert_eq('some distance verified before the pump', (km1 > 1)::text, 'true');
  r := pg_temp.pump('P', pg_temp.veh('P'), 9999, reading);
  perform testkit.assert_eq('mid-job: litres from the reading to full', pg_temp.r6((r->>'liters')::numeric), pg_temp.r6(120 - reading));
  perform testkit.assert_eq('mid-job: the game is told the tank is full', pg_temp.r6((r->>'fuel_l')::numeric), '120.000000');
  perform testkit.assert_eq('mid-job: stored on top of the pending deduction', pg_temp.r6(pg_temp.fuel(pg_temp.veh('P'))), pg_temp.r6(120 + used));
end $$;
select testkit.drive(pg_temp.u('P'), pg_temp.asg('p1'), 1300, -450, 2600, -900);
select testkit.age(pg_temp.asg('p1'), 5);
select testkit.as_user(pg_temp.u('P'), format('select submit_telemetry(%L, 2600, -900)::text', pg_temp.asg('p1')));
select testkit.as_user(pg_temp.u('P'), format('select complete_job(%L, 0)::text', pg_temp.asg('p1')));
select testkit.assert_eq('completed: full tank at the pump minus only the burn after it',
  pg_temp.r6(pg_temp.fuel(pg_temp.veh('P'))),
  pg_temp.r6(120 - ((select verified_km from job_telemetry where assignment_id = pg_temp.asg('p1')) - (select km1 from mid)) * 0.22));

-- ===== mid-job, then abandoned: never more than a full tank
select pg_temp.set_fuel(pg_temp.veh('P'), 60);
insert into fx select 'p2', testkit.as_user(pg_temp.u('P'), format($$select accept_job((select id from jobs where code='PUMP-2'), %L)$$, pg_temp.veh('P')));
select testkit.as_user(pg_temp.u('P'), format('select start_job(%L, 3, -2)::text', pg_temp.asg('p2')));
select testkit.drive(pg_temp.u('P'), pg_temp.asg('p2'), 3, -2, 1300, -450);
select pg_temp.pump('P', pg_temp.veh('P'), 9999, 40);
select testkit.assert_eq('mid-job the stored value carries the pending burn', (pg_temp.fuel(pg_temp.veh('P')) > 120)::text, 'true');
select testkit.as_user(pg_temp.u('P'), format('select abandon_job(%L)', pg_temp.asg('p2')));
select testkit.assert_eq('abandoned: capped at a full tank', pg_temp.fuel(pg_temp.veh('P'))::text, '120');

-- ===== mid-bus-run (the city bus: 180 L, 0.28 L/km)
select _apply_transaction(pg_temp.u('Q'), 'BONUS', 100000, 'fund:pumpq');
select testkit.as_user(pg_temp.u('Q'), $$select buy_vehicle('bus_city_01')::text$$);
select pg_temp.set_fuel(pg_temp.bus('Q'), 100);
insert into fx select 'q1', testkit.as_user(pg_temp.u('Q'), format('select start_bus_run((select id from bus_routes where code = %L), %L, 2600, -900)', 'LAG-R1', pg_temp.bus('Q')));
update bus_runs set verified_km = 2 where id = pg_temp.asg('q1');
do $$ declare r jsonb := pg_temp.pump('Q', pg_temp.bus('Q'), 9999, null); begin
  perform testkit.assert_eq('mid-run: the server level counts the run''s burn (100 - 2 x 0.28)', r->>'liters', '80.56');
  perform testkit.assert_eq('mid-run: priced on those litres', r->>'cost', '162');
  perform testkit.assert_eq('mid-run: stored on top of the pending deduction', pg_temp.fuel(pg_temp.bus('Q'))::text, '180.56');
end $$;
select testkit.as_user(pg_temp.u('Q'), format('select abandon_bus_run(%L)', pg_temp.asg('q1')));
select testkit.assert_eq('abandoned run: capped at a full tank', pg_temp.fuel(pg_temp.bus('Q'))::text, '180');

-- ===== surface
select testkit.assert_eq('one buy_fuel, with the reading as an optional third argument',
  (select string_agg(pg_get_function_identity_arguments(oid), ' | ') from pg_proc where proname = 'buy_fuel' and pronamespace = 'public'::regnamespace),
  'p_vehicle uuid, p_liters numeric, p_current_l numeric');
select testkit.expect_error('authenticated', pg_temp.u('P'), 'select _activity_ended_cap_fuel()', 'permission denied');
select 'PUMP REFUEL TESTS PASSED' as result;
