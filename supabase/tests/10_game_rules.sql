-- Behavioural tests for economy/job security. Run via supabase/tests/run.sh. Any failure raises and exits non-zero.
\set ON_ERROR_STOP on

create or replace function pg_temp.expect_error(p_sql text, p_msg text) returns void language plpgsql as $$
begin
  begin execute p_sql; exception when others then
    if sqlerrm like '%' || p_msg || '%' then return; end if;
    raise exception 'expected error containing %, got: %', p_msg, sqlerrm;
  end;
  raise exception 'expected error containing %, but statement succeeded: %', p_msg, p_sql;
end $$;

create or replace function pg_temp.login(p_user uuid) returns void language sql as
  $$ select set_config('request.jwt.claim.sub', p_user::text, false) $$;

-- Two players
insert into auth.users(id, email, raw_user_meta_data) values
  ('11111111-1111-1111-1111-111111111111', 'a@test', '{"display_name":"PlayerA"}'),
  ('22222222-2222-2222-2222-222222222222', 'b@test', '{"display_name":"PlayerB"}');

do $$ begin
  assert (select balance from player_wallets where player_id = '11111111-1111-1111-1111-111111111111') = 5000, 'starter grant';
  assert (select count(*) from vehicle_ownership where player_id = '11111111-1111-1111-1111-111111111111') = 1, 'starter vehicle';
  assert (select count(*) from transactions where player_id = '11111111-1111-1111-1111-111111111111') = 1, 'starter ledger row';
end $$;

-- Jobs: server-generated, a fixed one for determinism
select generate_jobs(5);
do $$ declare o uuid; d uuid; begin
  select id into o from locations where slug = 'lagos-apapa-port';
  select id into d from locations where slug = 'lagos-mile12-market';
  insert into jobs(code, origin_id, destination_id, cargo_type, cargo_weight_kg, distance_km, difficulty, reward, xp_reward, required_category, max_participants)
  values ('TEST-1', o, d, 'Test', 1000, 3.0, 2, 500, 40, 'truck', 2);
end $$;

-- ---- Player A flow
select pg_temp.login('11111111-1111-1111-1111-111111111111');
set role authenticated;

-- direct writes must be denied
select pg_temp.expect_error($$update player_wallets set balance = 999999$$, 'permission denied');
select pg_temp.expect_error($$insert into transactions(player_id,kind,amount,balance_after,idempotency_key) values (auth.uid(),'BONUS',1,1,'x')$$, 'permission denied');
select pg_temp.expect_error($$update jobs set reward = 99999999$$, 'permission denied');
select pg_temp.expect_error($$select _apply_transaction(auth.uid(),'BONUS',1000,'hack')$$, 'permission denied');
select pg_temp.expect_error($$select generate_jobs(1)$$, 'permission denied');
-- others' wallets are invisible
do $$ begin assert (select count(*) from player_wallets) = 1, 'RLS: only own wallet visible'; end $$;

create temp table ctx(k text primary key, v text);
grant all on ctx to authenticated;
insert into ctx select 'a_assign', accept_job((select id from jobs where code='TEST-1'), (select id from vehicle_ownership where player_id = auth.uid()))::text;

-- cannot take two active jobs
select pg_temp.expect_error(format($$select accept_job((select id from jobs where code='TEST-1'), (select id from vehicle_ownership where player_id = auth.uid()))$$), 'already_assigned');
-- cannot complete before starting
select pg_temp.expect_error(format($$select complete_job(%L::uuid, 3.0, 2600, -900, 100, 0)$$, (select v from ctx where k='a_assign')), 'invalid_state');
select start_job((select v from ctx where k='a_assign')::uuid);
-- too fast (just started; 3 km needs >= ~78 s)
select pg_temp.expect_error(format($$select complete_job(%L::uuid, 3.0, 2600, -900, 100, 0)$$, (select v from ctx where k='a_assign')), 'delivery_too_fast');

reset role;
update job_assignments set started_at = now() - interval '10 minutes' where id = (select v from ctx where k='a_assign')::uuid;
set role authenticated;

-- wrong place / implausible distance / bad vehicle state
select pg_temp.expect_error(format($$select complete_job(%L::uuid, 3.0, 0, 0, 100, 0)$$, (select v from ctx where k='a_assign')), 'not_at_destination');
select pg_temp.expect_error(format($$select complete_job(%L::uuid, 0.1, 2600, -900, 100, 0)$$, (select v from ctx where k='a_assign')), 'distance_implausible');
select pg_temp.expect_error(format($$select complete_job(%L::uuid, 3.0, 2600, -900, 100000, 0)$$, (select v from ctx where k='a_assign')), 'vehicle_state_invalid');

-- valid completion pays exactly the server-side reward
select complete_job((select v from ctx where k='a_assign')::uuid, 3.2, 2610, -905, 110, 4);
do $$ begin
  assert (select balance from player_wallets where player_id = auth.uid()) = 5500, 'reward paid once (5000 + 500)';
  assert (select jobs_completed from profiles where id = auth.uid()) = 1, 'jobs_completed';
  assert (select experience from profiles where id = auth.uid()) = 40, 'xp';
end $$;

-- duplicate / replay is rejected and pays nothing
select pg_temp.expect_error(format($$select complete_job(%L::uuid, 3.2, 2610, -905, 110, 4)$$, (select v from ctx where k='a_assign')), 'already_completed');
do $$ begin assert (select balance from player_wallets where player_id = auth.uid()) = 5500, 'no double pay'; end $$;

-- ---- Player B on the same shared job gets a convoy bonus path; also cannot complete A's assignment
reset role;
select pg_temp.login('22222222-2222-2222-2222-222222222222');
set role authenticated;
insert into ctx select 'b_assign', accept_job((select id from jobs where code='TEST-1'), (select id from vehicle_ownership where player_id = auth.uid()))::text;
select pg_temp.expect_error(format($$select start_job(%L::uuid)$$, (select v from ctx where k='a_assign')), 'assignment_not_found');
select pg_temp.expect_error(format($$select abandon_job(%L::uuid)$$, (select v from ctx where k='a_assign')), 'assignment_not_found');

-- ---- Economy sinks
select pg_temp.expect_error($$select buy_fuel(gen_random_uuid(), 10)$$, 'vehicle_not_owned');
select pg_temp.expect_error($$select repair_vehicle((select id from vehicle_ownership where player_id = auth.uid()))$$, 'nothing_to_repair');

-- ---- Convoys
select create_convoy('Lagos Runners');
select pg_temp.expect_error($$select create_convoy('Second One')$$, 'already_in_convoy_or_code_taken');
do $$ begin assert (select count(*) from convoy_members) >= 1; end $$;
select leave_convoy();
do $$ begin assert (select count(*) from convoys where disbanded_at is not null) = 1, 'empty convoy disbands'; end $$;

reset role;
-- Ledger integrity: wallet balance equals ledger sum for every player
do $$ begin
  assert not exists (
    select 1 from player_wallets w
    where w.balance <> (select coalesce(sum(amount),0) from transactions t where t.player_id = w.player_id)
  ), 'ledger sum must equal wallet balance';
end $$;
\echo ALL SQL TESTS PASSED
