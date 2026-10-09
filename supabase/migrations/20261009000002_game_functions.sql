-- Server-authoritative game logic. Every function runs SECURITY DEFINER with a pinned
-- search_path and derives the player from auth.uid() - never from a parameter.

-- ---------------------------------------------------------------- internals
create or replace function level_for_xp(xp bigint) returns integer
language sql immutable as $$ select 1 + floor(sqrt(xp / 100.0))::int $$;

-- Single choke point for wallet changes. Returns false if the idempotency key was already used
-- (replay) so callers can avoid double side effects. Negative balances raise via CHECK.
create or replace function _apply_transaction(
  p_player uuid, p_kind transaction_kind, p_amount bigint, p_key text,
  p_ref_type text default null, p_ref_id uuid default null
) returns boolean
language plpgsql security definer set search_path = public as $$
declare v_balance bigint;
begin
  if exists (select 1 from transactions where idempotency_key = p_key) then
    return false;
  end if;
  update player_wallets set balance = balance + p_amount, updated_at = now()
   where player_id = p_player returning balance into v_balance;  -- row lock serialises writers
  if not found then raise exception 'wallet_missing'; end if;
  insert into transactions(player_id, kind, amount, balance_after, ref_type, ref_id, idempotency_key)
  values (p_player, p_kind, p_amount, v_balance, p_ref_type, p_ref_id, p_key);
  return true;
exception when unique_violation then
  return false;  -- concurrent replay lost the race; whole subtransaction (incl. balance update) rolled back
end $$;

-- ---------------------------------------------------------------- signup
create or replace function handle_new_user() returns trigger
language plpgsql security definer set search_path = public as $$
declare v_name text;
begin
  v_name := coalesce(nullif(trim(new.raw_user_meta_data->>'display_name'), ''),
                     'driver_' || substr(replace(new.id::text, '-', ''), 1, 8));
  if char_length(v_name) not between 3 and 24
     or exists (select 1 from profiles where lower(display_name) = lower(v_name)) then
    v_name := 'driver_' || substr(replace(new.id::text, '-', ''), 1, 8);
  end if;
  insert into profiles(id, display_name) values (new.id, v_name);
  insert into player_wallets(player_id, balance) values (new.id, 0);
  perform _apply_transaction(new.id, 'STARTER_GRANT', 5000, 'starter:' || new.id, 'profile', new.id);
  insert into vehicle_ownership(player_id, definition_id, fuel_l)
    select new.id, id, fuel_capacity_l from vehicle_definitions where id = 'truck_light_01';
  return new;
end $$;

create trigger on_auth_user_created after insert on auth.users
  for each row execute function handle_new_user();

create or replace function set_display_name(p_name text) returns void
language plpgsql security definer set search_path = public as $$
begin
  if auth.uid() is null then raise exception 'not_authenticated'; end if;
  if p_name !~ '^[A-Za-z0-9_ -]{3,24}$' then raise exception 'invalid_display_name'; end if;
  update profiles set display_name = trim(p_name) where id = auth.uid();
exception when unique_violation then raise exception 'display_name_taken';
end $$;

-- ---------------------------------------------------------------- jobs
create or replace function accept_job(p_job uuid, p_vehicle uuid) returns uuid
language plpgsql security definer set search_path = public as $$
declare
  v_uid uuid := auth.uid(); v_job jobs; v_veh vehicle_ownership; v_cat text; v_id uuid;
begin
  if v_uid is null then raise exception 'not_authenticated'; end if;
  select * into v_job from jobs where id = p_job for update;
  if not found then raise exception 'job_not_found'; end if;
  if v_job.status <> 'open' or v_job.expires_at <= now() then raise exception 'job_unavailable'; end if;
  select * into v_veh from vehicle_ownership where id = p_vehicle and player_id = v_uid;
  if not found then raise exception 'vehicle_not_owned'; end if;
  select category into v_cat from vehicle_definitions where id = v_veh.definition_id;
  if v_cat <> v_job.required_category then raise exception 'wrong_vehicle_category'; end if;
  if v_veh.damage_pct >= 100 then raise exception 'vehicle_destroyed'; end if;
  if (select count(*) from job_assignments where job_id = p_job
        and status in ('accepted','in_progress','completed')) >= v_job.max_participants then
    raise exception 'job_full';
  end if;
  insert into job_assignments(job_id, player_id, vehicle_id) values (p_job, v_uid, p_vehicle)
    returning id into v_id;
  return v_id;
exception when unique_violation then raise exception 'already_assigned';
end $$;

create or replace function start_job(p_assignment uuid) returns void
language plpgsql security definer set search_path = public as $$
declare v_a job_assignments; v_fuel numeric;
begin
  select * into v_a from job_assignments where id = p_assignment and player_id = auth.uid() for update;
  if not found then raise exception 'assignment_not_found'; end if;
  if v_a.status <> 'accepted' then raise exception 'invalid_state'; end if;
  select fuel_l into v_fuel from vehicle_ownership where id = v_a.vehicle_id;
  if v_fuel <= 0 then raise exception 'out_of_fuel'; end if;
  update job_assignments set status = 'in_progress', started_at = now() where id = p_assignment;
end $$;

create or replace function abandon_job(p_assignment uuid) returns void
language plpgsql security definer set search_path = public as $$
begin
  update job_assignments set status = 'abandoned'
   where id = p_assignment and player_id = auth.uid() and status in ('accepted','in_progress');
  if not found then raise exception 'assignment_not_found'; end if;
end $$;

-- Delivery validation. The client reports what happened; the server decides whether it is
-- physically plausible and pays out the reward stored on the job (never a client value).
create or replace function complete_job(
  p_assignment uuid, p_distance_km numeric, p_final_x double precision, p_final_z double precision,
  p_fuel_remaining_l numeric, p_damage_pct numeric
) returns jsonb
language plpgsql security definer set search_path = public as $$
declare
  v_uid uuid := auth.uid();
  v_a job_assignments; v_job jobs; v_dest locations; v_veh vehicle_ownership; v_def vehicle_definitions;
  v_min_seconds numeric; v_elapsed numeric; v_party int; v_bonus bigint := 0; v_reward bigint; v_xp bigint;
  v_paid boolean; v_level int;
begin
  if v_uid is null then raise exception 'not_authenticated'; end if;
  select * into v_a from job_assignments where id = p_assignment and player_id = v_uid for update;
  if not found then raise exception 'assignment_not_found'; end if;
  if v_a.status = 'completed' then raise exception 'already_completed'; end if;
  if v_a.status <> 'in_progress' then raise exception 'invalid_state'; end if;

  select * into v_job from jobs where id = v_a.job_id;
  select * into v_dest from locations where id = v_job.destination_id;
  select * into v_veh from vehicle_ownership where id = v_a.vehicle_id for update;
  select * into v_def from vehicle_definitions where id = v_veh.definition_id;

  -- 1. Time: cannot beat vehicle top speed (+25% slack for downhill/physics noise).
  v_elapsed := extract(epoch from (now() - v_a.started_at));
  v_min_seconds := v_job.distance_km / (v_def.max_speed_kmh * 1.25) * 3600;
  if v_elapsed < v_min_seconds then raise exception 'delivery_too_fast'; end if;
  -- 2. Distance driven must be consistent with the route (not near zero, not absurd).
  if p_distance_km < v_job.distance_km * 0.8 or p_distance_km > v_job.distance_km * 3 then
    raise exception 'distance_implausible';
  end if;
  -- 3. Final position must be at the destination (150 m radius).
  if sqrt(power(p_final_x - v_dest.world_x, 2) + power(p_final_z - v_dest.world_z, 2)) > 150 then
    raise exception 'not_at_destination';
  end if;
  -- 4. Sanity of vehicle state.
  if p_fuel_remaining_l < 0 or p_fuel_remaining_l > v_def.fuel_capacity_l
     or p_damage_pct < v_veh.damage_pct or p_damage_pct > 100 then
    raise exception 'vehicle_state_invalid';
  end if;

  -- Convoy bonus: +10% per extra participant who accepted this job, capped at +30%.
  select count(*) into v_party from job_assignments
   where job_id = v_job.id and status in ('accepted','in_progress','completed');
  v_bonus := (v_job.reward * least(greatest(v_party - 1, 0) * 10, 30) / 100);
  v_reward := v_job.reward;
  v_xp := v_job.xp_reward;

  update job_assignments set status = 'completed', completed_at = now(), reported_distance_km = p_distance_km
   where id = p_assignment;

  v_paid := _apply_transaction(v_uid, 'JOB_REWARD', v_reward, 'job:' || p_assignment, 'job_assignment', p_assignment);
  if not v_paid then raise exception 'already_completed'; end if;
  if v_bonus > 0 then
    perform _apply_transaction(v_uid, 'BONUS', v_bonus, 'jobbonus:' || p_assignment, 'job_assignment', p_assignment);
  end if;

  update profiles set experience = experience + v_xp,
         level = level_for_xp(experience + v_xp),
         distance_km = distance_km + v_job.distance_km,
         jobs_completed = jobs_completed + 1
   where id = v_uid returning level into v_level;
  update vehicle_ownership set fuel_l = p_fuel_remaining_l, damage_pct = p_damage_pct,
         odometer_km = odometer_km + v_job.distance_km where id = v_veh.id;

  return jsonb_build_object('reward', v_reward, 'bonus', v_bonus, 'xp', v_xp, 'level', v_level,
    'balance', (select balance from player_wallets where player_id = v_uid));
end $$;

-- ---------------------------------------------------------------- economy sinks
create or replace function buy_fuel(p_vehicle uuid, p_liters numeric) returns jsonb
language plpgsql security definer set search_path = public as $$
declare v_uid uuid := auth.uid(); v_veh vehicle_ownership; v_cap numeric; v_cost bigint;
begin
  if p_liters <= 0 then raise exception 'invalid_amount'; end if;
  select * into v_veh from vehicle_ownership where id = p_vehicle and player_id = v_uid for update;
  if not found then raise exception 'vehicle_not_owned'; end if;
  select fuel_capacity_l into v_cap from vehicle_definitions where id = v_veh.definition_id;
  p_liters := least(p_liters, v_cap - v_veh.fuel_l);
  if p_liters <= 0 then raise exception 'tank_full'; end if;
  v_cost := ceil(p_liters * 2);  -- 2 coins per litre; move to a config table when tuning begins
  -- unique key per call: gen_random_uuid() => each purchase is its own event
  if not _apply_transaction(v_uid, 'FUEL_PURCHASE', -v_cost, 'fuel:' || gen_random_uuid(), 'vehicle', p_vehicle) then
    raise exception 'duplicate';
  end if;
  update vehicle_ownership set fuel_l = fuel_l + p_liters where id = p_vehicle;
  return jsonb_build_object('liters', p_liters, 'cost', v_cost);
exception when check_violation then raise exception 'insufficient_funds';
end $$;

create or replace function repair_vehicle(p_vehicle uuid) returns jsonb
language plpgsql security definer set search_path = public as $$
declare v_uid uuid := auth.uid(); v_veh vehicle_ownership; v_cost bigint;
begin
  select * into v_veh from vehicle_ownership where id = p_vehicle and player_id = v_uid for update;
  if not found then raise exception 'vehicle_not_owned'; end if;
  if v_veh.damage_pct <= 0 then raise exception 'nothing_to_repair'; end if;
  v_cost := ceil(v_veh.damage_pct * 10);
  if not _apply_transaction(v_uid, 'REPAIR', -v_cost, 'repair:' || gen_random_uuid(), 'vehicle', p_vehicle) then
    raise exception 'duplicate';
  end if;
  update vehicle_ownership set damage_pct = 0 where id = p_vehicle;
  return jsonb_build_object('cost', v_cost);
exception when check_violation then raise exception 'insufficient_funds';
end $$;

-- ---------------------------------------------------------------- convoys
create or replace function create_convoy(p_name text, p_session_code text default null) returns uuid
language plpgsql security definer set search_path = public as $$
declare v_id uuid;
begin
  if auth.uid() is null then raise exception 'not_authenticated'; end if;
  insert into convoys(leader_id, name, session_code) values (auth.uid(), p_name, p_session_code) returning id into v_id;
  insert into convoy_members(convoy_id, player_id) values (v_id, auth.uid());
  return v_id;
exception when unique_violation then raise exception 'already_in_convoy_or_code_taken';
end $$;

create or replace function join_convoy(p_convoy uuid) returns void
language plpgsql security definer set search_path = public as $$
begin
  if auth.uid() is null then raise exception 'not_authenticated'; end if;
  if not exists (select 1 from convoys where id = p_convoy and disbanded_at is null) then
    raise exception 'convoy_not_found'; end if;
  if (select count(*) from convoy_members where convoy_id = p_convoy) >= 8 then raise exception 'convoy_full'; end if;
  insert into convoy_members(convoy_id, player_id) values (p_convoy, auth.uid());
exception when unique_violation then raise exception 'already_in_convoy';
end $$;

create or replace function leave_convoy() returns void
language plpgsql security definer set search_path = public as $$
declare v_convoy uuid; v_next uuid;
begin
  delete from convoy_members where player_id = auth.uid() returning convoy_id into v_convoy;
  if v_convoy is null then return; end if;
  select player_id into v_next from convoy_members where convoy_id = v_convoy order by joined_at limit 1;
  if v_next is null then
    update convoys set disbanded_at = now() where id = v_convoy;
  else
    update convoys set leader_id = v_next where id = v_convoy and leader_id = auth.uid();
  end if;
end $$;

-- ---------------------------------------------------------------- job generation (server only)
-- Rewards derive from distance, difficulty and cargo - no hard-coded per-route values.
create or replace function generate_jobs(p_count int default 10) returns int
language plpgsql security definer set search_path = public as $$
declare
  i int; o locations; d locations; v_dist numeric; v_diff smallint; v_cargo text; v_n int := 0;
  cargos text[] := array['Electronics','Food & Produce','Building Materials','Textiles','Fuel Drums','Furniture','Agricultural Goods'];
begin
  for i in 1..p_count loop
    select * into o from locations order by random() limit 1;
    select * into d from locations where id <> o.id order by random() limit 1;
    exit when d.id is null;
    -- straight-line world distance in km, scaled by 1.1 for road winding
    v_dist := round((sqrt(power(o.world_x - d.world_x, 2) + power(o.world_z - d.world_z, 2)) / 1000.0 * 1.1)::numeric, 1);
    continue when v_dist < 0.5;
    v_diff := 1 + floor(random() * 5)::int;
    v_cargo := cargos[1 + floor(random() * array_length(cargos, 1))::int];
    insert into jobs(code, origin_id, destination_id, cargo_type, cargo_weight_kg, distance_km, difficulty,
                     reward, xp_reward, required_category)
    values ('AFR-' || lpad(nextval('job_code_seq')::text, 6, '0'), o.id, d.id, v_cargo,
            500 + floor(random() * 4000)::int, v_dist, v_diff,
            ceil(v_dist * (40 + v_diff * 15)), ceil(v_dist * (5 + v_diff * 2)), 'truck');
    v_n := v_n + 1;
  end loop;
  update jobs set status = 'expired' where status = 'open' and expires_at <= now();
  return v_n;
end $$;

-- ---------------------------------------------------------------- grants
revoke execute on all functions in schema public from public, anon, authenticated;
grant execute on function
  set_display_name(text), accept_job(uuid, uuid), start_job(uuid), abandon_job(uuid),
  complete_job(uuid, numeric, double precision, double precision, numeric, numeric),
  buy_fuel(uuid, numeric), repair_vehicle(uuid),
  create_convoy(text, text), join_convoy(uuid), leave_convoy()
  to authenticated;
-- _apply_transaction, generate_jobs, handle_new_user: not granted to clients (service role / trigger only).
