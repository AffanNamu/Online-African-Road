-- Bus / passenger system and vehicle shop. Same authority model as jobs:
--   * the client never says how many passengers boarded, what fare was paid or how far it drove;
--   * stops are served only when the server-verified position is at the stop and enough server time has passed;
--   * passenger counts are derived on the server (deterministic from run id + stop), capped by seat capacity.

-- ---------------------------------------------------------------- shop
create function buy_vehicle(p_definition text) returns uuid
language plpgsql security definer set search_path = public as $$
declare
  v_uid uuid := auth.uid(); v_def vehicle_definitions; v_id uuid;
begin
  if v_uid is null then raise exception 'not_authenticated'; end if;
  select * into v_def from vehicle_definitions where id = p_definition;
  if not found then raise exception 'unknown_vehicle'; end if;
  if v_def.price <= 0 then raise exception 'not_for_sale'; end if;
  -- Wallet CHECK (balance >= 0) turns an unaffordable purchase into check_violation.
  begin
    perform _apply_transaction(v_uid, 'VEHICLE_PURCHASE', -v_def.price, 'buy:' || gen_random_uuid(), 'vehicle_definition', null);
  exception when check_violation then
    raise exception 'insufficient_funds';
  end;
  insert into vehicle_ownership(player_id, definition_id, fuel_l) values (v_uid, v_def.id, v_def.fuel_capacity_l)
    returning id into v_id;
  return v_id;
end $$;

-- ---------------------------------------------------------------- routes
create table bus_routes (
  id       uuid primary key default gen_random_uuid(),
  code     text not null unique,
  name     text not null,
  city_id  uuid not null references cities(id),
  fare     integer not null check (fare between 1 and 500),
  xp_reward integer not null check (xp_reward >= 0)
);
create table bus_route_stops (
  route_id    uuid not null references bus_routes(id) on delete cascade,
  seq         integer not null check (seq >= 1),
  location_id uuid not null references locations(id),
  demand      integer not null check (demand between 0 and 80),
  alight_pct  integer not null check (alight_pct between 0 and 100),
  primary key (route_id, seq)
);

create type bus_run_status as enum ('active','completed','abandoned');
create table bus_runs (
  id          uuid primary key default gen_random_uuid(),
  route_id    uuid not null references bus_routes(id),
  player_id   uuid not null references profiles(id),
  vehicle_id  uuid not null references vehicle_ownership(id),
  status      bus_run_status not null default 'active',
  next_seq    integer not null default 1,
  aboard      integer not null default 0 check (aboard >= 0),
  revenue     bigint not null default 0 check (revenue >= 0),
  started_at  timestamptz not null default now(),
  last_stop_at timestamptz,
  last_x      double precision not null,
  last_z      double precision not null,
  last_at     timestamptz not null default now(),
  verified_km numeric not null default 0 check (verified_km >= 0),
  rejected    integer not null default 0,
  flagged     boolean not null default false,
  completed_at timestamptz
);
create unique index one_active_bus_run on bus_runs(player_id) where status = 'active';

create table bus_run_stops (
  run_id    uuid not null references bus_runs(id) on delete cascade,
  seq       integer not null,
  boarded   integer not null check (boarded >= 0),
  alighted  integer not null check (alighted >= 0),
  fare_paid bigint not null check (fare_paid >= 0),
  served_at timestamptz not null default now(),
  primary key (run_id, seq)
);

alter table bus_routes      enable row level security;
alter table bus_route_stops enable row level security;
alter table bus_runs        enable row level security;
alter table bus_run_stops   enable row level security;
create policy "read bus routes" on bus_routes for select to authenticated using (true);
create policy "read bus stops" on bus_route_stops for select to authenticated using (true);
create policy "own bus runs" on bus_runs for select to authenticated using (player_id = auth.uid());
create policy "own bus run stops" on bus_run_stops for select to authenticated
  using (exists (select 1 from bus_runs r where r.id = run_id and r.player_id = auth.uid()));

-- New tables inherit Supabase default privileges; strip and grant read-only explicitly.
revoke all on bus_routes, bus_route_stops, bus_runs, bus_run_stops from anon, authenticated;
grant select on bus_routes, bus_route_stops, bus_runs, bus_run_stops to authenticated;

-- ---------------------------------------------------------------- run lifecycle
create function start_bus_run(p_route uuid, p_vehicle uuid, p_x double precision, p_z double precision) returns uuid
language plpgsql security definer set search_path = public as $$
declare
  v_uid uuid := auth.uid(); v_veh vehicle_ownership; v_cat text; v_first locations; v_id uuid;
begin
  if v_uid is null then raise exception 'not_authenticated'; end if;
  perform 1 from bus_routes where id = p_route;
  if not found then raise exception 'route_not_found'; end if;
  select * into v_veh from vehicle_ownership where id = p_vehicle and player_id = v_uid for update;
  if not found then raise exception 'vehicle_not_owned'; end if;
  select category into v_cat from vehicle_definitions where id = v_veh.definition_id;
  if v_cat <> 'bus' then raise exception 'wrong_vehicle_category'; end if;
  if v_veh.damage_pct >= 100 then raise exception 'vehicle_destroyed'; end if;
  if v_veh.fuel_l <= 0 then raise exception 'out_of_fuel'; end if;
  if exists (select 1 from job_assignments where player_id = v_uid and status in ('accepted','in_progress')) then
    raise exception 'job_active';
  end if;
  select l.* into v_first from bus_route_stops s join locations l on l.id = s.location_id where s.route_id = p_route and s.seq = 1;
  if sqrt(power(p_x - v_first.world_x, 2) + power(p_z - v_first.world_z, 2)) > 120 then raise exception 'not_at_stop'; end if;
  insert into bus_runs(route_id, player_id, vehicle_id, last_x, last_z) values (p_route, v_uid, p_vehicle, p_x, p_z)
    returning id into v_id;
  return v_id;
exception when unique_violation then raise exception 'run_active';
end $$;

create function submit_bus_telemetry(p_run uuid, p_x double precision, p_z double precision) returns jsonb
language plpgsql security definer set search_path = public as $$
declare
  r bus_runs; v_max numeric; dt numeric; dist double precision; allowed double precision;
begin
  select * into r from bus_runs where id = p_run and player_id = auth.uid() for update;
  if not found then raise exception 'run_not_found'; end if;
  if r.status <> 'active' then raise exception 'run_not_active'; end if;
  if r.flagged then return jsonb_build_object('accepted', false, 'reason', 'flagged', 'verified_km', r.verified_km); end if;
  dt := extract(epoch from (now() - r.last_at));
  if dt < 0.5 then return jsonb_build_object('accepted', false, 'reason', 'rate_limited', 'verified_km', r.verified_km); end if;
  select d.max_speed_kmh into v_max from vehicle_ownership o join vehicle_definitions d on d.id = o.definition_id where o.id = r.vehicle_id;
  dist := sqrt(power(p_x - r.last_x, 2) + power(p_z - r.last_z, 2));
  allowed := (v_max * 1.3 / 3.6) * dt + 15;
  if dist > allowed then
    update bus_runs set rejected = rejected + 1, flagged = (rejected + 1 >= 10) where id = p_run;
    return jsonb_build_object('accepted', false, 'reason', 'too_fast', 'verified_km', r.verified_km);
  end if;
  update bus_runs set last_x = p_x, last_z = p_z, last_at = now(), verified_km = verified_km + dist / 1000.0 where id = p_run;
  return jsonb_build_object('accepted', true, 'verified_km', r.verified_km + dist / 1000.0);
end $$;

-- Serve the next stop. Passenger numbers are server-derived; the client supplies nothing but the run id.
create function serve_stop(p_run uuid) returns jsonb
language plpgsql security definer set search_path = public as $$
declare
  v_uid uuid := auth.uid();
  r bus_runs; v_route bus_routes; v_stop bus_route_stops; v_loc locations; v_prev locations; v_def vehicle_definitions;
  v_last boolean; v_alight int; v_wait int; v_board int; v_fare bigint; v_min numeric; v_since numeric;
  v_hash int; v_xp bigint; v_level int; v_burn numeric; v_veh vehicle_ownership; v_total int;
begin
  if v_uid is null then raise exception 'not_authenticated'; end if;
  select * into r from bus_runs where id = p_run and player_id = v_uid for update;
  if not found then raise exception 'run_not_found'; end if;
  if r.status <> 'active' then raise exception 'run_not_active'; end if;
  if r.flagged then raise exception 'telemetry_flagged'; end if;
  if extract(epoch from (now() - r.last_at)) > 30 then raise exception 'telemetry_stale'; end if;

  select * into v_route from bus_routes where id = r.route_id;
  select * into v_stop from bus_route_stops where route_id = r.route_id and seq = r.next_seq;
  select * into v_loc from locations where id = v_stop.location_id;
  select * into v_veh from vehicle_ownership where id = r.vehicle_id for update;
  select * into v_def from vehicle_definitions where id = v_veh.definition_id;

  if sqrt(power(r.last_x - v_loc.world_x, 2) + power(r.last_z - v_loc.world_z, 2)) > 60 then raise exception 'not_at_stop'; end if;

  -- Cannot reach the next stop faster than the bus can physically travel from the previous one (+25%).
  if r.next_seq > 1 then
    select l.* into v_prev from bus_route_stops s join locations l on l.id = s.location_id
     where s.route_id = r.route_id and s.seq = r.next_seq - 1;
    v_min := sqrt(power(v_loc.world_x - v_prev.world_x, 2) + power(v_loc.world_z - v_prev.world_z, 2))
             / (v_def.max_speed_kmh * 1.25 / 3.6);
    v_since := extract(epoch from (now() - r.last_stop_at));
    if v_since < v_min then raise exception 'stop_too_soon'; end if;
  end if;

  select not exists (select 1 from bus_route_stops where route_id = r.route_id and seq = r.next_seq + 1) into v_last;

  v_alight := case when v_last then r.aboard else ceil(r.aboard * v_stop.alight_pct / 100.0)::int end;
  v_hash := abs(('x' || substr(md5(r.id::text || ':' || r.next_seq), 1, 7))::bit(28)::int);
  v_wait := case when v_stop.demand = 0 then 0 else (v_stop.demand * 6 / 10) + (v_hash % (v_stop.demand * 4 / 10 + 1)) end;
  v_board := case when v_last then 0 else least(v_wait, v_def.passenger_capacity - (r.aboard - v_alight)) end;
  v_fare := v_alight::bigint * v_route.fare;

  insert into bus_run_stops(run_id, seq, boarded, alighted, fare_paid) values (p_run, r.next_seq, v_board, v_alight, v_fare);
  if v_fare > 0 then
    perform _apply_transaction(v_uid, 'JOB_REWARD', v_fare, 'busstop:' || p_run || ':' || r.next_seq, 'bus_run', p_run);
  end if;

  if v_last then
    v_burn := coalesce((v_def.stats->>'fuelBurnLPerKm')::numeric, 0.28);
    select coalesce(sum(boarded), 0) into v_total from bus_run_stops where run_id = p_run;
    update bus_runs set status = 'completed', completed_at = now(), aboard = 0, revenue = revenue + v_fare, last_stop_at = now()
     where id = p_run;
    update profiles set experience = experience + v_route.xp_reward,
           level = level_for_xp(experience + v_route.xp_reward),
           distance_km = distance_km + r.verified_km, jobs_completed = jobs_completed + 1
     where id = v_uid returning level into v_level;
    update vehicle_ownership set fuel_l = greatest(0, fuel_l - r.verified_km * v_burn),
           odometer_km = odometer_km + r.verified_km where id = v_veh.id;
    return jsonb_build_object('completed', true, 'alighted', v_alight, 'fare', v_fare, 'revenue', r.revenue + v_fare,
      'passengers_carried', v_total, 'xp', v_route.xp_reward, 'level', v_level,
      'balance', (select balance from player_wallets where player_id = v_uid));
  end if;

  update bus_runs set next_seq = next_seq + 1, aboard = r.aboard - v_alight + v_board, revenue = revenue + v_fare, last_stop_at = now()
   where id = p_run;
  return jsonb_build_object('completed', false, 'alighted', v_alight, 'boarded', v_board, 'aboard', r.aboard - v_alight + v_board,
    'fare', v_fare, 'next_seq', r.next_seq + 1);
end $$;

create function abandon_bus_run(p_run uuid) returns void
language plpgsql security definer set search_path = public as $$
begin
  update bus_runs set status = 'abandoned', completed_at = now()
   where id = p_run and player_id = auth.uid() and status = 'active';
  if not found then raise exception 'run_not_found'; end if;
end $$;

revoke execute on function
  buy_vehicle(text),
  start_bus_run(uuid, uuid, double precision, double precision),
  submit_bus_telemetry(uuid, double precision, double precision),
  serve_stop(uuid),
  abandon_bus_run(uuid)
  from public, anon, authenticated;
grant execute on function
  buy_vehicle(text),
  start_bus_run(uuid, uuid, double precision, double precision),
  submit_bus_telemetry(uuid, double precision, double precision),
  serve_stop(uuid),
  abandon_bus_run(uuid)
  to authenticated;
