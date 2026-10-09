\set ON_ERROR_STOP on
-- Phase-1 security matrix. Each block maps to a requirement in docs/SECURITY.md.

-- ===== fixtures
create temp table fx(k text primary key, v text);
insert into fx values
  ('A', testkit.make_user('MatrixA')::text), ('B', testkit.make_user('MatrixB')::text);
create function pg_temp.u(k text) returns uuid language sql as $$ select v::uuid from fx where k = $1 $$;

insert into jobs(code, origin_id, destination_id, cargo_type, cargo_weight_kg, distance_km, difficulty, reward, xp_reward, required_category, max_participants)
select 'MX-1', o.id, d.id, 'Test', 1000, 3.0, 2, 500, 40, 'truck', 2
from locations o, locations d where o.slug='lagos-apapa-port' and d.slug='lagos-mile12-market';
insert into jobs(code, origin_id, destination_id, cargo_type, cargo_weight_kg, distance_km, difficulty, reward, xp_reward, required_category, max_participants)
select 'MX-BUS', o.id, d.id, 'People', 100, 3.0, 2, 500, 40, 'bus', 1
from locations o, locations d where o.slug='lagos-apapa-port' and d.slug='lagos-mile12-market';
-- Expired job
insert into jobs(code, origin_id, destination_id, cargo_type, cargo_weight_kg, distance_km, difficulty, reward, xp_reward, required_category, expires_at)
select 'MX-OLD', o.id, d.id, 'Test', 1000, 3.0, 2, 500, 40, 'truck', now() - interval '1 hour'
from locations o, locations d where o.slug='lagos-apapa-port' and d.slug='lagos-mile12-market';

-- ===== R1: clients cannot modify wallets / ledger / XP / profile stats / vehicles / jobs / assignments directly
select testkit.expect_error('authenticated', pg_temp.u('A'), 'update player_wallets set balance = balance + 1000000 where player_id = auth.uid()', 'permission denied');
select testkit.expect_error('authenticated', pg_temp.u('A'), 'insert into player_wallets(player_id,balance) values (auth.uid(), 99)', 'permission denied');
select testkit.expect_error('authenticated', pg_temp.u('A'), 'delete from player_wallets', 'permission denied');
select testkit.expect_error('authenticated', pg_temp.u('A'), 'update profiles set experience = 999999, level = 50 where id = auth.uid()', 'permission denied');
select testkit.expect_error('authenticated', pg_temp.u('A'), 'update profiles set jobs_completed = 999 where id = auth.uid()', 'permission denied');
select testkit.expect_error('authenticated', pg_temp.u('A'), 'update vehicle_ownership set damage_pct = 0, fuel_l = 9999', 'permission denied');
select testkit.expect_error('authenticated', pg_temp.u('A'), $$insert into vehicle_ownership(player_id, definition_id, fuel_l) values (auth.uid(),'truck_medium_01',1)$$, 'permission denied');
select testkit.expect_error('authenticated', pg_temp.u('A'), 'update transactions set amount = 1', 'permission denied');
select testkit.expect_error('authenticated', pg_temp.u('A'), 'delete from transactions', 'permission denied');
select testkit.expect_error('authenticated', pg_temp.u('A'), 'update jobs set reward = 99999999', 'permission denied');
select testkit.expect_error('authenticated', pg_temp.u('A'), $$update job_assignments set status = 'completed'$$, 'permission denied');
select testkit.expect_error('authenticated', pg_temp.u('A'), $$insert into job_assignments(job_id, player_id, vehicle_id, status) select id, auth.uid(), (select id from vehicle_ownership limit 1), 'completed' from jobs limit 1$$, 'permission denied');
select testkit.expect_error('authenticated', pg_temp.u('A'), 'insert into convoy_members(convoy_id, player_id) select id, auth.uid() from convoys', 'permission denied');
select testkit.expect_error('authenticated', pg_temp.u('A'), $$insert into vehicle_definitions(id,category,name,price,fuel_capacity_l,max_speed_kmh) values ('x','truck','x',0,1,1)$$, 'permission denied');
select testkit.expect_error('authenticated', pg_temp.u('A'), 'truncate player_wallets', 'permission denied');

-- ===== R2: internal / privileged functions are not callable by clients
select testkit.expect_error('authenticated', pg_temp.u('A'), $$select _apply_transaction(auth.uid(),'BONUS',1000,'k1')$$, 'permission denied');
select testkit.expect_error('authenticated', pg_temp.u('A'), 'select generate_jobs(5)', 'permission denied');
select testkit.expect_error('authenticated', pg_temp.u('A'), 'select handle_new_user()', 'permission denied');
-- anon: no table reads, no RPCs
select testkit.expect_error('anon', null, 'select * from jobs', 'permission denied');
select testkit.expect_error('anon', null, 'select * from profiles', 'permission denied');
select testkit.expect_error('anon', null, 'select * from player_wallets', 'permission denied');
select testkit.expect_error('anon', null, 'select accept_job(gen_random_uuid(), gen_random_uuid())', 'permission denied');
select testkit.expect_error('anon', null, 'select create_convoy(''Anon Convoy'')', 'permission denied');
-- every SECURITY DEFINER function pins search_path (hijack protection)
do $$ declare bad text; begin
  select string_agg(p.proname, ',') into bad from pg_proc p join pg_namespace n on n.oid = p.pronamespace
   where n.nspname = 'public' and p.prosecdef and not coalesce(p.proconfig::text like '%search_path=public%', false);
  if bad is not null then raise exception 'SECURITY DEFINER without pinned search_path: %', bad; end if;
end $$;
-- RLS enabled on every public table
do $$ declare bad text; begin
  select string_agg(c.relname, ',') into bad from pg_class c join pg_namespace n on n.oid = c.relnamespace
   where n.nspname = 'public' and c.relkind = 'r' and not c.relrowsecurity;
  if bad is not null then raise exception 'RLS disabled on: %', bad; end if;
end $$;

-- ===== R3: RLS isolates players
select testkit.assert_eq('A sees only own wallet', testkit.as_user(pg_temp.u('A'), 'select count(*) from player_wallets'), '1');
select testkit.assert_eq('A sees only own vehicles', testkit.as_user(pg_temp.u('A'), 'select count(*) from vehicle_ownership'), '1');
select testkit.assert_eq('A sees only own ledger', testkit.as_user(pg_temp.u('A'), 'select count(*) from transactions'), '1');
select testkit.assert_eq('A cannot read B wallet', testkit.as_user(pg_temp.u('A'), format('select count(*) from player_wallets where player_id = %L', pg_temp.u('B'))), '0');
select testkit.assert_eq('A cannot read B vehicles', testkit.as_user(pg_temp.u('A'), format('select count(*) from vehicle_ownership where player_id = %L', pg_temp.u('B'))), '0');

-- ===== R4: job acceptance rules
select testkit.expect_error('authenticated', pg_temp.u('A'), $$select accept_job((select id from jobs where code='MX-OLD'), (select id from vehicle_ownership where player_id = auth.uid()))$$, 'job_unavailable');
select testkit.expect_error('authenticated', pg_temp.u('A'), $$select accept_job((select id from jobs where code='MX-BUS'), (select id from vehicle_ownership where player_id = auth.uid()))$$, 'wrong_vehicle_category');
-- cannot use someone else's vehicle
select testkit.expect_error('authenticated', pg_temp.u('A'), format($$select accept_job((select id from jobs where code='MX-1'), %L)$$, (select id from vehicle_ownership where player_id = pg_temp.u('B'))), 'vehicle_not_owned');
select testkit.expect_error('authenticated', pg_temp.u('A'), 'select accept_job(gen_random_uuid(), gen_random_uuid())', 'job_not_found');

-- ===== R5: delivery validation (A), exhaustively
insert into fx select 'a_asg', testkit.as_user(pg_temp.u('A'), $$select accept_job((select id from jobs where code='MX-1'), (select id from vehicle_ownership where player_id = auth.uid()))$$);
select testkit.expect_error('authenticated', pg_temp.u('A'), format('select complete_job(%L,3,2600,-900,100,0)', pg_temp.u('A')), 'assignment_not_found');  -- wrong id (uses user id)
select testkit.expect_error('authenticated', pg_temp.u('A'), format('select complete_job(%L,3,2600,-900,100,0)', (select v from fx where k='a_asg')), 'invalid_state');          -- not started
select testkit.expect_error('authenticated', pg_temp.u('B'), format('select start_job(%L)', (select v from fx where k='a_asg')), 'assignment_not_found');                      -- B cannot touch A's assignment
select testkit.as_user(pg_temp.u('A'), format('select start_job(%L)::text', (select v from fx where k='a_asg')));
select testkit.expect_error('authenticated', pg_temp.u('A'), format('select start_job(%L)', (select v from fx where k='a_asg')), 'invalid_state');                               -- cannot restart the clock
select testkit.expect_error('authenticated', pg_temp.u('A'), format('select complete_job(%L,3,2600,-900,100,0)', (select v from fx where k='a_asg')), 'delivery_too_fast');       -- impossible travel time
update job_assignments set started_at = now() - interval '10 minutes' where id = (select v from fx where k='a_asg')::uuid;
select testkit.expect_error('authenticated', pg_temp.u('A'), format('select complete_job(%L,3,0,0,100,0)', (select v from fx where k='a_asg')), 'not_at_destination');            -- wrong location
select testkit.expect_error('authenticated', pg_temp.u('A'), format('select complete_job(%L,3,2600,-1500,100,0)', (select v from fx where k='a_asg')), 'not_at_destination');     -- 600 m off
select testkit.expect_error('authenticated', pg_temp.u('A'), format('select complete_job(%L,0.2,2600,-900,100,0)', (select v from fx where k='a_asg')), 'distance_implausible');   -- teleport-length trip
select testkit.expect_error('authenticated', pg_temp.u('A'), format('select complete_job(%L,99,2600,-900,100,0)', (select v from fx where k='a_asg')), 'distance_implausible');    -- inflated distance
select testkit.expect_error('authenticated', pg_temp.u('A'), format('select complete_job(%L,3.2,2600,-900,100000,0)', (select v from fx where k='a_asg')), 'vehicle_state_invalid'); -- fuel > tank
select testkit.expect_error('authenticated', pg_temp.u('A'), format('select complete_job(%L,3.2,2600,-900,-5,0)', (select v from fx where k='a_asg')), 'vehicle_state_invalid');     -- negative fuel
select testkit.expect_error('authenticated', pg_temp.u('A'), format('select complete_job(%L,3.2,2600,-900,100,150)', (select v from fx where k='a_asg')), 'vehicle_state_invalid'); -- damage > 100
-- none of the rejected attempts changed anything
select testkit.assert_eq('wallet untouched by rejects', testkit.as_user(pg_temp.u('A'), 'select balance from player_wallets'), '5000');
select testkit.assert_eq('assignment still in progress', (select status::text from job_assignments where id=(select v from fx where k='a_asg')::uuid), 'in_progress');
select testkit.assert_eq('reward not in ledger', (select count(*) from transactions where kind='JOB_REWARD' and player_id=pg_temp.u('A'))::text, '0');

-- B cannot complete A's assignment
select testkit.expect_error('authenticated', pg_temp.u('B'), format('select complete_job(%L,3.2,2600,-900,100,0)', (select v from fx where k='a_asg')), 'assignment_not_found');

-- valid completion: pays the DB reward (500) + 0 bonus (A alone), exactly once
select testkit.as_user(pg_temp.u('A'), format('select complete_job(%L,3.2,2600,-900,100,4)::text', (select v from fx where k='a_asg')));
select testkit.assert_eq('paid 500', testkit.as_user(pg_temp.u('A'), 'select balance from player_wallets'), '5500');
select testkit.assert_eq('xp 40', testkit.as_user(pg_temp.u('A'), 'select experience from profiles where id = auth.uid()'), '40');
select testkit.assert_eq('vehicle damage persisted', (select damage_pct::text from vehicle_ownership where player_id = pg_temp.u('A')), '4');

-- ===== R5b: reporting LESS damage than the server knows is rejected (free repair exploit)
insert into jobs(code, origin_id, destination_id, cargo_type, cargo_weight_kg, distance_km, difficulty, reward, xp_reward, required_category)
select 'MX-DMG', o.id, d.id, 'Test', 1000, 3.0, 2, 500, 40, 'truck' from locations o, locations d where o.slug='lagos-apapa-port' and d.slug='lagos-mile12-market';
insert into fx values ('D', testkit.make_user('MatrixD')::text);
update vehicle_ownership set damage_pct = 30 where player_id = pg_temp.u('D');
insert into fx select 'd_asg', testkit.as_user(pg_temp.u('D'), $$select accept_job((select id from jobs where code='MX-DMG'), (select id from vehicle_ownership where player_id = auth.uid()))$$);
select testkit.as_user(pg_temp.u('D'), format('select start_job(%L)::text', (select v from fx where k='d_asg')));
update job_assignments set started_at = now() - interval '10 minutes' where id = (select v from fx where k='d_asg')::uuid;
select testkit.expect_error('authenticated', pg_temp.u('D'), format('select complete_job(%L,3.2,2600,-900,100,10)', (select v from fx where k='d_asg')), 'vehicle_state_invalid');
select testkit.as_user(pg_temp.u('D'), format('select complete_job(%L,3.2,2600,-900,100,35)::text', (select v from fx where k='d_asg')));
select testkit.assert_eq('damage only ever increases', (select damage_pct::text from vehicle_ownership where player_id = pg_temp.u('D')), '35');

-- ===== R6: replay
select testkit.expect_error('authenticated', pg_temp.u('A'), format('select complete_job(%L,3.2,2600,-900,100,4)', (select v from fx where k='a_asg')), 'already_completed');
select testkit.assert_eq('no double pay', testkit.as_user(pg_temp.u('A'), 'select balance from player_wallets'), '5500');
select testkit.assert_eq('one reward row', (select count(*) from transactions where kind='JOB_REWARD' and player_id=pg_temp.u('A'))::text, '1');
-- ledger replay primitive: same idempotency key => no second effect (superuser calls the internal fn)
select testkit.assert_eq('ledger replay returns false', _apply_transaction(pg_temp.u('A'), 'BONUS', 777, 'job:' || (select v from fx where k='a_asg'))::text, 'false');
select testkit.assert_eq('replay did not move money', (select balance::text from player_wallets where player_id = pg_temp.u('A')), '5500');
-- cannot re-accept a completed job
select testkit.expect_error('authenticated', pg_temp.u('A'), $$select accept_job((select id from jobs where code='MX-1'), (select id from vehicle_ownership where player_id = auth.uid()))$$, 'already_assigned');

-- ===== R7: shared job bonus is server-computed; abandon blocks payout
insert into fx select 'b_asg', testkit.as_user(pg_temp.u('B'), $$select accept_job((select id from jobs where code='MX-1'), (select id from vehicle_ownership where player_id = auth.uid()))$$);
select testkit.as_user(pg_temp.u('B'), format('select start_job(%L)::text', (select v from fx where k='b_asg')));
update job_assignments set started_at = now() - interval '10 minutes' where id = (select v from fx where k='b_asg')::uuid;
select testkit.as_user(pg_temp.u('B'), format('select complete_job(%L,3.2,2600,-900,100,0)::text', (select v from fx where k='b_asg')));
select testkit.assert_eq('B got reward + 10% convoy bonus', testkit.as_user(pg_temp.u('B'), 'select balance from player_wallets'), '5550');
-- job full: a third player cannot join (max_participants=2)
insert into fx values ('C', testkit.make_user('MatrixC')::text);
select testkit.expect_error('authenticated', pg_temp.u('C'), $$select accept_job((select id from jobs where code='MX-1'), (select id from vehicle_ownership where player_id = auth.uid()))$$, 'job_full');
-- abandoned assignments cannot be completed
insert into jobs(code, origin_id, destination_id, cargo_type, cargo_weight_kg, distance_km, difficulty, reward, xp_reward, required_category)
select 'MX-2', o.id, d.id, 'Test', 1000, 3.0, 2, 500, 40, 'truck' from locations o, locations d where o.slug='lagos-apapa-port' and d.slug='lagos-mile12-market';
insert into fx select 'c_asg', testkit.as_user(pg_temp.u('C'), $$select accept_job((select id from jobs where code='MX-2'), (select id from vehicle_ownership where player_id = auth.uid()))$$);
select testkit.as_user(pg_temp.u('C'), format('select start_job(%L)::text', (select v from fx where k='c_asg')));
select testkit.as_user(pg_temp.u('C'), format('select abandon_job(%L)::text', (select v from fx where k='c_asg')));
update job_assignments set started_at = now() - interval '10 minutes' where id = (select v from fx where k='c_asg')::uuid;
select testkit.expect_error('authenticated', pg_temp.u('C'), format('select complete_job(%L,3.2,2600,-900,100,0)', (select v from fx where k='c_asg')), 'invalid_state');

-- ===== R8: fuel / repair cannot be duplicated or abused
-- drain the tank and damage the truck (superuser simulating a drive), then buy fuel
update vehicle_ownership set fuel_l = 20, damage_pct = 10 where player_id = pg_temp.u('C');
select testkit.expect_error('authenticated', pg_temp.u('C'), 'select buy_fuel((select id from vehicle_ownership where player_id = auth.uid()), -5)', 'invalid_amount');
select testkit.expect_error('authenticated', pg_temp.u('C'), 'select buy_fuel((select id from vehicle_ownership where player_id = auth.uid()), 0)', 'invalid_amount');
select testkit.assert_eq('fuel bought', testkit.as_user(pg_temp.u('C'), 'select (buy_fuel((select id from vehicle_ownership where player_id = auth.uid()), 9999)->>''cost'')'), '200');  -- 100 L * 2
select testkit.assert_eq('tank full', (select fuel_l::text from vehicle_ownership where player_id = pg_temp.u('C')), '120');
select testkit.expect_error('authenticated', pg_temp.u('C'), 'select buy_fuel((select id from vehicle_ownership where player_id = auth.uid()), 9999)', 'tank_full');  -- immediate duplicate
select testkit.assert_eq('repair cost', testkit.as_user(pg_temp.u('C'), 'select (repair_vehicle((select id from vehicle_ownership where player_id = auth.uid()))->>''cost'')'), '100');
select testkit.expect_error('authenticated', pg_temp.u('C'), 'select repair_vehicle((select id from vehicle_ownership where player_id = auth.uid()))', 'nothing_to_repair');
select testkit.assert_eq('C wallet = 5000-200-100', testkit.as_user(pg_temp.u('C'), 'select balance from player_wallets'), '4700');
-- cannot spend money you do not have, and failed purchases leave no trace
update player_wallets set balance = 10 where player_id = pg_temp.u('C');
update vehicle_ownership set fuel_l = 0, damage_pct = 50 where player_id = pg_temp.u('C');
select testkit.expect_error('authenticated', pg_temp.u('C'), 'select buy_fuel((select id from vehicle_ownership where player_id = auth.uid()), 100)', 'insufficient_funds');
select testkit.expect_error('authenticated', pg_temp.u('C'), 'select repair_vehicle((select id from vehicle_ownership where player_id = auth.uid()))', 'insufficient_funds');
select testkit.assert_eq('balance unchanged after failed spend', (select balance::text from player_wallets where player_id = pg_temp.u('C')), '10');
select testkit.assert_eq('fuel unchanged after failed spend', (select fuel_l::text from vehicle_ownership where player_id = pg_temp.u('C')), '0');
-- cannot buy fuel / repair someone else's vehicle
select testkit.expect_error('authenticated', pg_temp.u('A'), format('select buy_fuel(%L, 10)', (select id from vehicle_ownership where player_id = pg_temp.u('C'))), 'vehicle_not_owned');
select testkit.expect_error('authenticated', pg_temp.u('A'), format('select repair_vehicle(%L)', (select id from vehicle_ownership where player_id = pg_temp.u('C'))), 'vehicle_not_owned');
-- unfueled truck cannot start a job
insert into jobs(code, origin_id, destination_id, cargo_type, cargo_weight_kg, distance_km, difficulty, reward, xp_reward, required_category)
select 'MX-3', o.id, d.id, 'Test', 1000, 3.0, 2, 500, 40, 'truck' from locations o, locations d where o.slug='lagos-apapa-port' and d.slug='lagos-mile12-market';
insert into fx select 'c2_asg', testkit.as_user(pg_temp.u('C'), $$select accept_job((select id from jobs where code='MX-3'), (select id from vehicle_ownership where player_id = auth.uid()))$$);
select testkit.expect_error('authenticated', pg_temp.u('C'), format('select start_job(%L)', (select v from fx where k='c2_asg')), 'out_of_fuel');
select testkit.as_user(pg_temp.u('C'), format('select abandon_job(%L)::text', (select v from fx where k='c2_asg')));

-- ===== R9: convoy operations cannot be abused
insert into fx select 'conv', testkit.as_user(pg_temp.u('A'), $$select create_convoy('Alpha Convoy')$$);
select testkit.expect_error('authenticated', pg_temp.u('A'), $$select create_convoy('Second Convoy')$$, 'already_in_convoy_or_code_taken');
select testkit.expect_error('authenticated', pg_temp.u('A'), format('select join_convoy(%L)', (select v from fx where k='conv')), 'already_in_convoy');
select testkit.expect_error('authenticated', pg_temp.u('B'), 'select join_convoy(gen_random_uuid())', 'convoy_not_found');
select testkit.expect_error('authenticated', pg_temp.u('B'), $$select create_convoy('x')$$, 'violates check constraint');           -- name too short
select testkit.expect_error('authenticated', pg_temp.u('B'), $$update convoys set leader_id = auth.uid()$$, 'permission denied');   -- cannot hijack leadership
select testkit.expect_error('authenticated', pg_temp.u('B'), format($$delete from convoy_members where convoy_id = %L$$, (select v from fx where k='conv')), 'permission denied'); -- cannot kick
select testkit.as_user(pg_temp.u('B'), format('select join_convoy(%L)::text', (select v from fx where k='conv')));
select testkit.expect_error('authenticated', pg_temp.u('B'), $$select create_convoy('Third Convoy')$$, 'already_in_convoy_or_code_taken');
-- fill to 8, 9th is refused
insert into fx select 'm'||g, testkit.make_user('Member'||g)::text from generate_series(1,6) g;
do $$ declare g int; begin
  for g in 1..6 loop
    perform testkit.as_user((select v::uuid from fx where k='m'||g), format('select join_convoy(%L)::text', (select v from fx where k='conv')));
  end loop;
end $$;
select testkit.assert_eq('8 members', (select count(*) from convoy_members where convoy_id = (select v::uuid from fx where k='conv'))::text, '8');
insert into fx values ('late', testkit.make_user('LateJoiner')::text);
select testkit.expect_error('authenticated', pg_temp.u('late'), format('select join_convoy(%L)', (select v from fx where k='conv')), 'convoy_full');
-- leader leaves -> leadership transfers to the longest-standing member (not to an arbitrary caller)
select testkit.as_user(pg_temp.u('A'), 'select leave_convoy()::text');
select testkit.assert_eq('new leader is B', (select leader_id::text from convoys where id = (select v::uuid from fx where k='conv')), pg_temp.u('B')::text);
-- disbanded convoys cannot be joined; last member leaving disbands
do $$ declare g int; begin
  perform testkit.as_user(pg_temp.u('B'), 'select leave_convoy()::text');
  for g in 1..6 loop perform testkit.as_user((select v::uuid from fx where k='m'||g), 'select leave_convoy()::text'); end loop;
end $$;
select testkit.assert_eq('empty convoy disbanded', (select (disbanded_at is not null)::text from convoys where id = (select v::uuid from fx where k='conv')), 'true');
select testkit.expect_error('authenticated', pg_temp.u('late'), format('select join_convoy(%L)', (select v from fx where k='conv')), 'convoy_not_found');

-- ===== R10: display names
select testkit.expect_error('authenticated', pg_temp.u('B'), $$select set_display_name('MatrixA')$$, 'display_name_taken');
select testkit.expect_error('authenticated', pg_temp.u('B'), $$select set_display_name('matrixa')$$, 'display_name_taken');   -- case-insensitive
select testkit.expect_error('authenticated', pg_temp.u('B'), $$select set_display_name('<script>')$$, 'invalid_display_name');
select testkit.expect_error('authenticated', pg_temp.u('B'), $$select set_display_name('ab')$$, 'invalid_display_name');

-- ===== R11: global ledger invariants
do $$ begin
  -- Player C's wallet was set directly by this test's fixtures (simulating bankruptcy), so it is excluded.
  if exists (select 1 from player_wallets w where w.player_id <> pg_temp.u('C') and w.balance <> (select coalesce(sum(amount),0) from transactions t where t.player_id = w.player_id))
  then raise exception 'ledger sum != wallet balance'; end if;
  if exists (select 1 from transactions where idempotency_key is null) then raise exception 'ledger row without idempotency key'; end if;
end $$;
\echo SECURITY MATRIX PASSED
