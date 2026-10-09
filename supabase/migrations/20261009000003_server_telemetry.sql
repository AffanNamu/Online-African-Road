-- Phase 4: server-verifiable telemetry. Replaces the client-reported distance/position/fuel of
-- start_job / complete_job. Rules:
--   * Only the SERVER clock is used. Client timestamps are never read.
--   * A position sample is accepted only if the speed implied by (distance moved / server time elapsed)
--     is physically possible for that vehicle. Accepted samples build `verified_km` and the last position.
--   * Completion uses verified_km, the last accepted position, and server-computed fuel burn.
--   * Damage can only rise, and only by a bounded amount per verified km.

create table job_telemetry (
  assignment_id uuid primary key references job_assignments(id) on delete cascade,
  last_x   double precision not null,
  last_z   double precision not null,
  last_at  timestamptz not null,
  verified_km numeric not null default 0 check (verified_km >= 0),
  samples  integer not null default 0,
  rejected integer not null default 0,
  max_kmh  numeric not null default 0,
  flagged  boolean not null default false
);
-- Clients never read or write telemetry directly; only the functions below do.
alter table job_telemetry enable row level security;
revoke all on job_telemetry from anon, authenticated;

drop function start_job(uuid);
drop function complete_job(uuid, numeric, double precision, double precision, numeric, numeric);

-- Loading at the pickup point starts the server clock and seeds telemetry at a position that must be at the origin.
create function start_job(p_assignment uuid, p_x double precision, p_z double precision) returns void
language plpgsql security definer set search_path = public as $$
declare v_a job_assignments; v_fuel numeric; v_o locations;
begin
  select * into v_a from job_assignments where id = p_assignment and player_id = auth.uid() for update;
  if not found then raise exception 'assignment_not_found'; end if;
  if v_a.status <> 'accepted' then raise exception 'invalid_state'; end if;
  select o.* into v_o from jobs j join locations o on o.id = j.origin_id where j.id = v_a.job_id;
  if sqrt(power(p_x - v_o.world_x, 2) + power(p_z - v_o.world_z, 2)) > 120 then raise exception 'not_at_pickup'; end if;
  select fuel_l into v_fuel from vehicle_ownership where id = v_a.vehicle_id;
  if v_fuel <= 0 then raise exception 'out_of_fuel'; end if;
  update job_assignments set status = 'in_progress', started_at = now() where id = p_assignment;
  insert into job_telemetry(assignment_id, last_x, last_z, last_at) values (p_assignment, p_x, p_z, now());
end $$;

-- One position sample. Returns {accepted, reason?, verified_km}. Never raises for cheating (so a client cannot
-- probe thresholds from error text); rejected samples simply do not advance verified state.
create function submit_telemetry(p_assignment uuid, p_x double precision, p_z double precision) returns jsonb
language plpgsql security definer set search_path = public as $$
declare
  t job_telemetry; a job_assignments; v_max numeric; dt numeric; dist double precision; allowed double precision; kmh numeric;
begin
  select * into a from job_assignments where id = p_assignment and player_id = auth.uid();
  if not found then raise exception 'assignment_not_found'; end if;
  if a.status <> 'in_progress' then raise exception 'invalid_state'; end if;
  select * into t from job_telemetry where assignment_id = p_assignment for update;
  if not found then raise exception 'invalid_state'; end if;
  if t.flagged then return jsonb_build_object('accepted', false, 'reason', 'flagged', 'verified_km', t.verified_km); end if;

  dt := extract(epoch from (now() - t.last_at));
  if dt < 0.5 then return jsonb_build_object('accepted', false, 'reason', 'rate_limited', 'verified_km', t.verified_km); end if;

  select d.max_speed_kmh into v_max from vehicle_ownership o join vehicle_definitions d on d.id = o.definition_id where o.id = a.vehicle_id;
  dist := sqrt(power(p_x - t.last_x, 2) + power(p_z - t.last_z, 2));
  allowed := (v_max * 1.3 / 3.6) * dt + 15;     -- +30% physics slack, +15 m GPS-style jitter
  if dist > allowed then
    update job_telemetry set rejected = rejected + 1, flagged = (rejected + 1 >= 10) where assignment_id = p_assignment;
    return jsonb_build_object('accepted', false, 'reason', 'too_fast', 'verified_km', t.verified_km);
  end if;

  kmh := (dist / dt) * 3.6;
  update job_telemetry set last_x = p_x, last_z = p_z, last_at = now(), samples = samples + 1,
         verified_km = verified_km + dist / 1000.0, max_kmh = greatest(max_kmh, kmh)
   where assignment_id = p_assignment;
  return jsonb_build_object('accepted', true, 'verified_km', t.verified_km + dist / 1000.0);
end $$;

-- Delivery. The only client input is the damage figure, which is bounded; everything else is server-derived.
create function complete_job(p_assignment uuid, p_damage_pct numeric) returns jsonb
language plpgsql security definer set search_path = public as $$
declare
  v_uid uuid := auth.uid();
  v_a job_assignments; v_job jobs; v_dest locations; v_veh vehicle_ownership; v_def vehicle_definitions; v_t job_telemetry;
  v_min_seconds numeric; v_elapsed numeric; v_party int; v_bonus bigint := 0; v_reward bigint; v_xp bigint;
  v_paid boolean; v_level int; v_burn numeric; v_fuel numeric;
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
  select * into v_t from job_telemetry where assignment_id = p_assignment for update;
  if not found then raise exception 'invalid_state'; end if;

  if v_t.flagged then raise exception 'telemetry_flagged'; end if;
  -- 1. Server-measured trip time cannot beat the vehicle's top speed (+25%).
  v_elapsed := extract(epoch from (now() - v_a.started_at));
  v_min_seconds := v_job.distance_km / (v_def.max_speed_kmh * 1.25) * 3600;
  if v_elapsed < v_min_seconds then raise exception 'delivery_too_fast'; end if;
  -- 2. The last ACCEPTED position must be recent and at the destination (150 m).
  if extract(epoch from (now() - v_t.last_at)) > 60 then raise exception 'telemetry_stale'; end if;
  if sqrt(power(v_t.last_x - v_dest.world_x, 2) + power(v_t.last_z - v_dest.world_z, 2)) > 150 then
    raise exception 'not_at_destination';
  end if;
  -- 3. Server-verified distance must be consistent with the route.
  if v_t.verified_km < v_job.distance_km * 0.8 or v_t.verified_km > v_job.distance_km * 5 then
    raise exception 'distance_implausible';
  end if;
  -- 4. Damage can only rise, by at most 3% per verified km (the client cannot erase damage).
  if p_damage_pct < v_veh.damage_pct or p_damage_pct > 100
     or p_damage_pct > v_veh.damage_pct + v_t.verified_km * 3 then
    raise exception 'vehicle_state_invalid';
  end if;

  -- Fuel is computed here from verified distance and the vehicle definition, never reported.
  v_burn := coalesce((v_def.stats->>'fuelBurnLPerKm')::numeric, 0.25);
  v_fuel := greatest(0, v_veh.fuel_l - v_t.verified_km * v_burn);

  select count(*) into v_party from job_assignments
   where job_id = v_job.id and status in ('accepted','in_progress','completed');
  v_bonus := (v_job.reward * least(greatest(v_party - 1, 0) * 10, 30) / 100);
  v_reward := v_job.reward;
  v_xp := v_job.xp_reward;

  update job_assignments set status = 'completed', completed_at = now(), reported_distance_km = v_t.verified_km
   where id = p_assignment;

  v_paid := _apply_transaction(v_uid, 'JOB_REWARD', v_reward, 'job:' || p_assignment, 'job_assignment', p_assignment);
  if not v_paid then raise exception 'already_completed'; end if;
  if v_bonus > 0 then
    perform _apply_transaction(v_uid, 'BONUS', v_bonus, 'jobbonus:' || p_assignment, 'job_assignment', p_assignment);
  end if;

  update profiles set experience = experience + v_xp,
         level = level_for_xp(experience + v_xp),
         distance_km = distance_km + v_t.verified_km,
         jobs_completed = jobs_completed + 1
   where id = v_uid returning level into v_level;
  update vehicle_ownership set fuel_l = v_fuel, damage_pct = p_damage_pct,
         odometer_km = odometer_km + v_t.verified_km where id = v_veh.id;

  return jsonb_build_object('reward', v_reward, 'bonus', v_bonus, 'xp', v_xp, 'level', v_level,
    'balance', (select balance from player_wallets where player_id = v_uid));
end $$;

-- New functions get Supabase's default EXECUTE grants; strip and re-grant explicitly.
revoke execute on function
  start_job(uuid, double precision, double precision),
  submit_telemetry(uuid, double precision, double precision),
  complete_job(uuid, numeric)
  from public, anon, authenticated;
grant execute on function
  start_job(uuid, double precision, double precision),
  submit_telemetry(uuid, double precision, double precision),
  complete_job(uuid, numeric)
  to authenticated;
