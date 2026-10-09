\set ON_ERROR_STOP on
-- Security matrix (phases 1 and 4). Maps to docs/SECURITY.md.

create temp table fx(k text primary key, v text);
insert into fx values ('A', testkit.make_user('MatrixA')::text), ('B', testkit.make_user('MatrixB')::text);
create function pg_temp.u(k text) returns uuid language sql as $$ select v::uuid from fx where k = $1 $$;
create function pg_temp.veh(k text) returns uuid language sql as $$ select id from vehicle_ownership where player_id = (select v::uuid from fx where fx.k = $1) $$;
create function pg_temp.asg(k text) returns uuid language sql as $$ select v::uuid from fx where k = $1 $$;

-- ===== R0: signup provisioning
select testkit.assert_eq('starter grant', (select balance::text from player_wallets where player_id = pg_temp.u('A')), '5000');
select testkit.assert_eq('starter vehicle', (select count(*)::text from vehicle_ownership where player_id = pg_temp.u('A')), '1');
select testkit.assert_eq('starter ledger row', (select count(*)::text from transactions where player_id = pg_temp.u('A')), '1');

select testkit.mk_job('MX-1', 2);
select testkit.mk_job('MX-BUS', 1, 500, 'bus');
select testkit.mk_job('MX-OLD', 1, 500, 'truck', 3.0, true);

-- ===== R1: clients cannot modify wallets / ledger / XP / stats / vehicles / jobs / assignments / telemetry directly
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
-- telemetry: not readable, not writable
select testkit.expect_error('authenticated', pg_temp.u('A'), 'select * from job_telemetry', 'permission denied');
select testkit.expect_error('authenticated', pg_temp.u('A'), 'update job_telemetry set verified_km = 1000', 'permission denied');
select testkit.expect_error('authenticated', pg_temp.u('A'), 'insert into job_telemetry(assignment_id,last_x,last_z,last_at) select id,0,0,now() from job_assignments limit 1', 'permission denied');
select testkit.expect_error('anon', null, 'select * from job_telemetry', 'permission denied');

-- ===== R2: internal / privileged functions are not callable by clients; anon can call nothing
select testkit.expect_error('authenticated', pg_temp.u('A'), $$select _apply_transaction(auth.uid(),'BONUS',1000,'k1')$$, 'permission denied');
select testkit.expect_error('authenticated', pg_temp.u('A'), 'select generate_jobs(5)', 'permission denied');
select testkit.expect_error('authenticated', pg_temp.u('A'), 'select handle_new_user()', 'permission denied');
select testkit.expect_error('anon', null, 'select * from jobs', 'permission denied');
select testkit.expect_error('anon', null, 'select * from profiles', 'permission denied');
select testkit.expect_error('anon', null, 'select * from player_wallets', 'permission denied');
select testkit.expect_error('anon', null, 'select accept_job(gen_random_uuid(), gen_random_uuid())', 'permission denied');
select testkit.expect_error('anon', null, 'select create_convoy(''Anon Convoy'')', 'permission denied');
select testkit.expect_error('anon', null, 'select start_job(gen_random_uuid(), 0, 0)', 'permission denied');
select testkit.expect_error('anon', null, 'select submit_telemetry(gen_random_uuid(), 0, 0)', 'permission denied');
select testkit.expect_error('anon', null, 'select complete_job(gen_random_uuid(), 0)', 'permission denied');
-- exactly the intended client RPCs are executable by authenticated, nothing else in public
do $$ declare extra text; begin
  select string_agg(p.oid::regprocedure::text, ', ') into extra from pg_proc p join pg_namespace n on n.oid = p.pronamespace
   where n.nspname = 'public' and has_function_privilege('authenticated', p.oid, 'execute')
     and p.proname not in ('set_display_name','accept_job','start_job','submit_telemetry','complete_job','abandon_job',
                           'buy_fuel','repair_vehicle','create_convoy','join_convoy','leave_convoy');
  if extra is not null then raise exception 'unexpected client-executable functions: %', extra; end if;
end $$;
do $$ declare bad text; begin
  select string_agg(p.proname, ',') into bad from pg_proc p join pg_namespace n on n.oid = p.pronamespace
   where n.nspname = 'public' and p.prosecdef and not coalesce(p.proconfig::text like '%search_path=public%', false);
  if bad is not null then raise exception 'SECURITY DEFINER without pinned search_path: %', bad; end if;
end $$;
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
select testkit.expect_error('authenticated', pg_temp.u('A'), format($$select accept_job((select id from jobs where code='MX-OLD'), %L)$$, pg_temp.veh('A')), 'job_unavailable');
select testkit.expect_error('authenticated', pg_temp.u('A'), format($$select accept_job((select id from jobs where code='MX-BUS'), %L)$$, pg_temp.veh('A')), 'wrong_vehicle_category');
select testkit.expect_error('authenticated', pg_temp.u('A'), format($$select accept_job((select id from jobs where code='MX-1'), %L)$$, pg_temp.veh('B')), 'vehicle_not_owned');
select testkit.expect_error('authenticated', pg_temp.u('A'), 'select accept_job(gen_random_uuid(), gen_random_uuid())', 'job_not_found');

-- ===== R5: telemetry-verified delivery (A)
insert into fx select 'a_asg', testkit.as_user(pg_temp.u('A'), format($$select accept_job((select id from jobs where code='MX-1'), %L)$$, pg_temp.veh('A')));
select testkit.expect_error('authenticated', pg_temp.u('A'), format('select complete_job(%L, 0)', pg_temp.u('A')), 'assignment_not_found');
select testkit.expect_error('authenticated', pg_temp.u('A'), format('select complete_job(%L, 0)', pg_temp.asg('a_asg')), 'invalid_state');                 -- not started
select testkit.expect_error('authenticated', pg_temp.u('A'), format('select submit_telemetry(%L, 0, 0)', pg_temp.asg('a_asg')), 'invalid_state');          -- cannot send samples before loading
select testkit.expect_error('authenticated', pg_temp.u('A'), format('select start_job(%L, 5000, 5000)', pg_temp.asg('a_asg')), 'not_at_pickup');           -- cannot "load" remotely
select testkit.expect_error('authenticated', pg_temp.u('B'), format('select start_job(%L, 0, 0)', pg_temp.asg('a_asg')), 'assignment_not_found');          -- B cannot touch A's assignment
select testkit.as_user(pg_temp.u('A'), format('select start_job(%L, 3, -2)::text', pg_temp.asg('a_asg')));
select testkit.expect_error('authenticated', pg_temp.u('A'), format('select start_job(%L, 0, 0)', pg_temp.asg('a_asg')), 'invalid_state');                 -- cannot restart the clock
select testkit.expect_error('authenticated', pg_temp.u('B'), format('select submit_telemetry(%L, 10, 10)', pg_temp.asg('a_asg')), 'assignment_not_found'); -- B cannot feed A's telemetry
-- immediate completion: impossible time
select testkit.expect_error('authenticated', pg_temp.u('A'), format('select complete_job(%L, 0)', pg_temp.asg('a_asg')), 'delivery_too_fast');
-- teleport straight to the destination: sample rejected, state unchanged
select testkit.age(pg_temp.asg('a_asg'), 2);
select testkit.assert_eq('teleport rejected', testkit.as_user(pg_temp.u('A'), format('select (submit_telemetry(%L, 2600, -900))->>''reason''', pg_temp.asg('a_asg'))), 'too_fast');
select testkit.assert_eq('teleport changed nothing', (select verified_km::text from job_telemetry where assignment_id = pg_temp.asg('a_asg')), '0');
-- sample flood is rate-limited (server clock, not client)
select testkit.age(pg_temp.asg('a_asg'), 2);
select testkit.as_user(pg_temp.u('A'), format('select submit_telemetry(%L, 10, -5)::text', pg_temp.asg('a_asg')));
select testkit.assert_eq('flood rate-limited', testkit.as_user(pg_temp.u('A'), format('select (submit_telemetry(%L, 12, -6))->>''reason''', pg_temp.asg('a_asg'))), 'rate_limited');
-- even after "waiting" the server time check still holds: a long wait does not allow teleport beyond top speed * time
select testkit.age(pg_temp.asg('a_asg'), 10);
select testkit.assert_eq('slow-wait teleport rejected', testkit.as_user(pg_temp.u('A'), format('select (submit_telemetry(%L, 2600, -900))->>''reason''', pg_temp.asg('a_asg'))), 'too_fast');
-- drive part of the way, then try to complete away from the destination
select testkit.drive(pg_temp.u('A'), pg_temp.asg('a_asg'), 10, -5, 2600, -1500);
select testkit.expect_error('authenticated', pg_temp.u('A'), format('select complete_job(%L, 0)', pg_temp.asg('a_asg')), 'not_at_destination');
-- go to the destination
select testkit.drive(pg_temp.u('A'), pg_temp.asg('a_asg'), 2600, -1500, 2600, -900);
-- damage rules: more than 3%/verified km is rejected, over 100 rejected
select testkit.expect_error('authenticated', pg_temp.u('A'), format('select complete_job(%L, 60)', pg_temp.asg('a_asg')), 'vehicle_state_invalid');
select testkit.expect_error('authenticated', pg_temp.u('A'), format('select complete_job(%L, 150)', pg_temp.asg('a_asg')), 'vehicle_state_invalid');
-- stale telemetry (client went silent) is rejected
select testkit.age(pg_temp.asg('a_asg'), 120);
select testkit.expect_error('authenticated', pg_temp.u('A'), format('select complete_job(%L, 0)', pg_temp.asg('a_asg')), 'telemetry_stale');
-- nothing above changed money or state
select testkit.assert_eq('wallet untouched by rejects', testkit.as_user(pg_temp.u('A'), 'select balance from player_wallets'), '5000');
select testkit.assert_eq('still in progress', (select status::text from job_assignments where id = pg_temp.asg('a_asg')), 'in_progress');
select testkit.assert_eq('no reward in ledger', (select count(*)::text from transactions where kind='JOB_REWARD' and player_id = pg_temp.u('A')), '0');
-- B cannot complete A's assignment
select testkit.expect_error('authenticated', pg_temp.u('B'), format('select complete_job(%L, 0)', pg_temp.asg('a_asg')), 'assignment_not_found');
-- a fresh final sample at the destination, then a valid completion
select testkit.age(pg_temp.asg('a_asg'), 5);
select testkit.as_user(pg_temp.u('A'), format('select submit_telemetry(%L, 2600, -900)::text', pg_temp.asg('a_asg')));
select testkit.as_user(pg_temp.u('A'), format('select complete_job(%L, 1)::text', pg_temp.asg('a_asg')));
select testkit.assert_eq('paid exactly the job reward', testkit.as_user(pg_temp.u('A'), 'select balance from player_wallets'), '5500');
select testkit.assert_eq('xp from job row', testkit.as_user(pg_temp.u('A'), 'select experience from profiles where id = auth.uid()'), '40');
select testkit.assert_eq('damage persisted', (select damage_pct::text from vehicle_ownership where player_id = pg_temp.u('A')), '1');
-- fuel is SERVER computed: 120 L - verified_km * 0.22 L/km (verified_km ~ 3.0 for this path)
do $$ declare f numeric; begin
  select fuel_l into f from vehicle_ownership where player_id = pg_temp.u('A');
  if f > 119.5 or f < 119.0 then raise exception 'server fuel burn wrong: %', f; end if;
end $$;
select testkit.assert_eq('profile distance is verified distance', (select (distance_km between 2.9 and 3.6)::text from profiles where id = pg_temp.u('A')), 'true');

-- ===== R5b: damage can never be reduced for free
select testkit.mk_job('MX-DMG');
insert into fx values ('D', testkit.make_user('MatrixD')::text);
update vehicle_ownership set damage_pct = 30 where player_id = pg_temp.u('D');
insert into fx select 'd_asg', testkit.as_user(pg_temp.u('D'), format($$select accept_job((select id from jobs where code='MX-DMG'), %L)$$, pg_temp.veh('D')));
select testkit.as_user(pg_temp.u('D'), format('select start_job(%L, 0, 0)::text', pg_temp.asg('d_asg')));
select testkit.drive(pg_temp.u('D'), pg_temp.asg('d_asg'), 0, 0, 2600, -900);
select testkit.expect_error('authenticated', pg_temp.u('D'), format('select complete_job(%L, 10)', pg_temp.asg('d_asg')), 'vehicle_state_invalid');
select testkit.as_user(pg_temp.u('D'), format('select complete_job(%L, 35)::text', pg_temp.asg('d_asg')));
select testkit.assert_eq('damage only increases', (select damage_pct::text from vehicle_ownership where player_id = pg_temp.u('D')), '35');

-- ===== R5c: inconsistent job (route length claims 20 km, destination only 3 km away): verified distance too short
select testkit.mk_job('MX-LONG', 1, 500, 'truck', 20.0);
insert into fx values ('E', testkit.make_user('MatrixE')::text);
insert into fx select 'e_asg', testkit.as_user(pg_temp.u('E'), format($$select accept_job((select id from jobs where code='MX-LONG'), %L)$$, pg_temp.veh('E')));
select testkit.as_user(pg_temp.u('E'), format('select start_job(%L, 0, 0)::text', pg_temp.asg('e_asg')));
select testkit.drive(pg_temp.u('E'), pg_temp.asg('e_asg'), 0, 0, 2600, -900, 100);
select testkit.age(pg_temp.asg('e_asg'), 900);   -- plenty of elapsed time, so only the distance rule can fail
select testkit.as_user(pg_temp.u('E'), format('select submit_telemetry(%L, 2600, -900)::text', pg_temp.asg('e_asg')));
select testkit.expect_error('authenticated', pg_temp.u('E'), format('select complete_job(%L, 0)', pg_temp.asg('e_asg')), 'distance_implausible');

-- ===== R5c2: verified distance far ABOVE the job's route length is also rejected (job claims 0.5 km, destination is 3 km away)
select testkit.mk_job('MX-SHORT', 1, 500, 'truck', 0.5);
insert into fx values ('G', testkit.make_user('MatrixG')::text);
insert into fx select 'g_asg', testkit.as_user(pg_temp.u('G'), format($$select accept_job((select id from jobs where code='MX-SHORT'), %L)$$, pg_temp.veh('G')));
select testkit.as_user(pg_temp.u('G'), format('select start_job(%L, 0, 0)::text', pg_temp.asg('g_asg')));
select testkit.drive(pg_temp.u('G'), pg_temp.asg('g_asg'), 0, 0, 2600, -900);
select testkit.expect_error('authenticated', pg_temp.u('G'), format('select complete_job(%L, 0)', pg_temp.asg('g_asg')), 'distance_implausible');

-- ===== R5d: repeated impossible samples flag the assignment; flagged jobs cannot be completed
select testkit.mk_job('MX-FLAG');
insert into fx values ('F', testkit.make_user('MatrixF')::text);
insert into fx select 'f_asg', testkit.as_user(pg_temp.u('F'), format($$select accept_job((select id from jobs where code='MX-FLAG'), %L)$$, pg_temp.veh('F')));
select testkit.as_user(pg_temp.u('F'), format('select start_job(%L, 0, 0)::text', pg_temp.asg('f_asg')));
do $$ declare i int; begin
  for i in 1..10 loop
    perform testkit.age((select v::uuid from fx where k='f_asg'), 1);
    perform testkit.as_user((select v::uuid from fx where k='F'), format('select submit_telemetry(%L, 9000, 9000)::text', (select v from fx where k='f_asg')));
  end loop;
end $$;
select testkit.assert_eq('flagged after 10 teleports', (select flagged::text from job_telemetry where assignment_id = pg_temp.asg('f_asg')), 'true');
select testkit.assert_eq('flagged assignment ignores further samples', testkit.as_user(pg_temp.u('F'), format('select (submit_telemetry(%L, 1, 1))->>''reason''', pg_temp.asg('f_asg'))), 'flagged');
select testkit.age(pg_temp.asg('f_asg'), 3600);
select testkit.expect_error('authenticated', pg_temp.u('F'), format('select complete_job(%L, 0)', pg_temp.asg('f_asg')), 'telemetry_flagged');

-- ===== R6: replay
select testkit.expect_error('authenticated', pg_temp.u('A'), format('select complete_job(%L, 1)', pg_temp.asg('a_asg')), 'already_completed');
select testkit.assert_eq('no double pay', testkit.as_user(pg_temp.u('A'), 'select balance from player_wallets'), '5500');
select testkit.assert_eq('one reward row', (select count(*)::text from transactions where kind='JOB_REWARD' and player_id = pg_temp.u('A')), '1');
select testkit.assert_eq('ledger replay returns false', _apply_transaction(pg_temp.u('A'), 'BONUS', 777, 'job:' || pg_temp.asg('a_asg'))::text, 'false');
select testkit.assert_eq('replay did not move money', (select balance::text from player_wallets where player_id = pg_temp.u('A')), '5500');
select testkit.expect_error('authenticated', pg_temp.u('A'), format($$select accept_job((select id from jobs where code='MX-1'), %L)$$, pg_temp.veh('A')), 'already_assigned');
-- samples cannot be added to a completed assignment
select testkit.expect_error('authenticated', pg_temp.u('A'), format('select submit_telemetry(%L, 2600, -900)', pg_temp.asg('a_asg')), 'invalid_state');

-- ===== R7: shared job bonus is server-computed; full jobs and abandoned assignments
insert into fx select 'b_asg', testkit.as_user(pg_temp.u('B'), format($$select accept_job((select id from jobs where code='MX-1'), %L)$$, pg_temp.veh('B')));
select testkit.as_user(pg_temp.u('B'), format('select start_job(%L, 0, 0)::text', pg_temp.asg('b_asg')));
select testkit.drive(pg_temp.u('B'), pg_temp.asg('b_asg'), 0, 0, 2600, -900);
select testkit.as_user(pg_temp.u('B'), format('select complete_job(%L, 0)::text', pg_temp.asg('b_asg')));
select testkit.assert_eq('B got reward + 10% convoy bonus', testkit.as_user(pg_temp.u('B'), 'select balance from player_wallets'), '5550');
insert into fx values ('C', testkit.make_user('MatrixC')::text);
select testkit.expect_error('authenticated', pg_temp.u('C'), format($$select accept_job((select id from jobs where code='MX-1'), %L)$$, pg_temp.veh('C')), 'job_full');
select testkit.mk_job('MX-2');
insert into fx select 'c_asg', testkit.as_user(pg_temp.u('C'), format($$select accept_job((select id from jobs where code='MX-2'), %L)$$, pg_temp.veh('C')));
select testkit.as_user(pg_temp.u('C'), format('select start_job(%L, 0, 0)::text', pg_temp.asg('c_asg')));
select testkit.drive(pg_temp.u('C'), pg_temp.asg('c_asg'), 0, 0, 2600, -900);
select testkit.as_user(pg_temp.u('C'), format('select abandon_job(%L)::text', pg_temp.asg('c_asg')));
select testkit.expect_error('authenticated', pg_temp.u('C'), format('select complete_job(%L, 0)', pg_temp.asg('c_asg')), 'invalid_state');
select testkit.expect_error('authenticated', pg_temp.u('C'), format('select submit_telemetry(%L, 2600, -900)', pg_temp.asg('c_asg')), 'invalid_state');

-- ===== R8: fuel / repair cannot be duplicated or abused
update vehicle_ownership set fuel_l = 20, damage_pct = 10 where player_id = pg_temp.u('C');
select testkit.expect_error('authenticated', pg_temp.u('C'), format('select buy_fuel(%L, -5)', pg_temp.veh('C')), 'invalid_amount');
select testkit.expect_error('authenticated', pg_temp.u('C'), format('select buy_fuel(%L, 0)', pg_temp.veh('C')), 'invalid_amount');
select testkit.assert_eq('fuel bought', testkit.as_user(pg_temp.u('C'), format('select (buy_fuel(%L, 9999)->>''cost'')', pg_temp.veh('C'))), '200');
select testkit.assert_eq('tank full', (select fuel_l::text from vehicle_ownership where player_id = pg_temp.u('C')), '120');
select testkit.expect_error('authenticated', pg_temp.u('C'), format('select buy_fuel(%L, 9999)', pg_temp.veh('C')), 'tank_full');
select testkit.assert_eq('repair cost', testkit.as_user(pg_temp.u('C'), format('select (repair_vehicle(%L)->>''cost'')', pg_temp.veh('C'))), '100');
select testkit.expect_error('authenticated', pg_temp.u('C'), format('select repair_vehicle(%L)', pg_temp.veh('C')), 'nothing_to_repair');
select testkit.assert_eq('C wallet = 5000-200-100', testkit.as_user(pg_temp.u('C'), 'select balance from player_wallets'), '4700');
update player_wallets set balance = 10 where player_id = pg_temp.u('C');
update vehicle_ownership set fuel_l = 0, damage_pct = 50 where player_id = pg_temp.u('C');
select testkit.expect_error('authenticated', pg_temp.u('C'), format('select buy_fuel(%L, 100)', pg_temp.veh('C')), 'insufficient_funds');
select testkit.expect_error('authenticated', pg_temp.u('C'), format('select repair_vehicle(%L)', pg_temp.veh('C')), 'insufficient_funds');
select testkit.assert_eq('balance unchanged after failed spend', (select balance::text from player_wallets where player_id = pg_temp.u('C')), '10');
select testkit.assert_eq('fuel unchanged after failed spend', (select fuel_l::text from vehicle_ownership where player_id = pg_temp.u('C')), '0');
select testkit.expect_error('authenticated', pg_temp.u('A'), format('select buy_fuel(%L, 10)', pg_temp.veh('C')), 'vehicle_not_owned');
select testkit.expect_error('authenticated', pg_temp.u('A'), format('select repair_vehicle(%L)', pg_temp.veh('C')), 'vehicle_not_owned');
select testkit.mk_job('MX-3');
insert into fx select 'c2_asg', testkit.as_user(pg_temp.u('C'), format($$select accept_job((select id from jobs where code='MX-3'), %L)$$, pg_temp.veh('C')));
select testkit.expect_error('authenticated', pg_temp.u('C'), format('select start_job(%L, 0, 0)', pg_temp.asg('c2_asg')), 'out_of_fuel');
select testkit.as_user(pg_temp.u('C'), format('select abandon_job(%L)::text', pg_temp.asg('c2_asg')));

-- ===== R9: convoy operations cannot be abused
insert into fx select 'conv', testkit.as_user(pg_temp.u('A'), $$select create_convoy('Alpha Convoy')$$);
select testkit.expect_error('authenticated', pg_temp.u('A'), $$select create_convoy('Second Convoy')$$, 'already_in_convoy_or_code_taken');
select testkit.expect_error('authenticated', pg_temp.u('A'), format('select join_convoy(%L)', pg_temp.asg('conv')), 'already_in_convoy');
select testkit.expect_error('authenticated', pg_temp.u('B'), 'select join_convoy(gen_random_uuid())', 'convoy_not_found');
select testkit.expect_error('authenticated', pg_temp.u('B'), $$select create_convoy('x')$$, 'violates check constraint');
select testkit.expect_error('authenticated', pg_temp.u('B'), $$update convoys set leader_id = auth.uid()$$, 'permission denied');
select testkit.expect_error('authenticated', pg_temp.u('B'), format($$delete from convoy_members where convoy_id = %L$$, pg_temp.asg('conv')), 'permission denied');
select testkit.as_user(pg_temp.u('B'), format('select join_convoy(%L)::text', pg_temp.asg('conv')));
select testkit.expect_error('authenticated', pg_temp.u('B'), $$select create_convoy('Third Convoy')$$, 'already_in_convoy_or_code_taken');
insert into fx select 'm'||g, testkit.make_user('Member'||g)::text from generate_series(1,6) g;
do $$ declare g int; begin
  for g in 1..6 loop
    perform testkit.as_user((select v::uuid from fx where k='m'||g), format('select join_convoy(%L)::text', (select v from fx where k='conv')));
  end loop;
end $$;
select testkit.assert_eq('8 members', (select count(*)::text from convoy_members where convoy_id = pg_temp.asg('conv')), '8');
insert into fx values ('late', testkit.make_user('LateJoiner')::text);
select testkit.expect_error('authenticated', pg_temp.u('late'), format('select join_convoy(%L)', pg_temp.asg('conv')), 'convoy_full');
select testkit.as_user(pg_temp.u('A'), 'select leave_convoy()::text');
select testkit.assert_eq('new leader is B', (select leader_id::text from convoys where id = pg_temp.asg('conv')), pg_temp.u('B')::text);
do $$ declare g int; begin
  perform testkit.as_user((select v::uuid from fx where k='B'), 'select leave_convoy()::text');
  for g in 1..6 loop perform testkit.as_user((select v::uuid from fx where k='m'||g), 'select leave_convoy()::text'); end loop;
end $$;
select testkit.assert_eq('empty convoy disbanded', (select (disbanded_at is not null)::text from convoys where id = pg_temp.asg('conv')), 'true');
select testkit.expect_error('authenticated', pg_temp.u('late'), format('select join_convoy(%L)', pg_temp.asg('conv')), 'convoy_not_found');

-- ===== R10: display names
select testkit.expect_error('authenticated', pg_temp.u('B'), $$select set_display_name('MatrixA')$$, 'display_name_taken');
select testkit.expect_error('authenticated', pg_temp.u('B'), $$select set_display_name('matrixa')$$, 'display_name_taken');
select testkit.expect_error('authenticated', pg_temp.u('B'), $$select set_display_name('<script>')$$, 'invalid_display_name');
select testkit.expect_error('authenticated', pg_temp.u('B'), $$select set_display_name('ab')$$, 'invalid_display_name');

-- ===== R11: global ledger invariants (player C's wallet was set directly by fixtures, so it is excluded)
do $$ begin
  if exists (select 1 from player_wallets w where w.player_id <> pg_temp.u('C') and w.balance <> (select coalesce(sum(amount),0) from transactions t where t.player_id = w.player_id))
  then raise exception 'ledger sum != wallet balance'; end if;
  if exists (select 1 from transactions where idempotency_key is null) then raise exception 'ledger row without idempotency key'; end if;
end $$;
\echo SECURITY MATRIX PASSED
