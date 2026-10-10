-- Backend foundation, step 6: vehicle API, job board API and job-lifecycle hooks.
-- Existing authority is untouched: accept_job / start_job / submit_telemetry / complete_job keep deciding outcomes.
-- This migration ADDS rules by trigger (so every path obeys them) and read APIs shaped for the dashboard:
--   * job requirements: minimum level, route open + unlocked  (BEFORE INSERT on job_assignments)
--   * vehicle unlock level                                     (BEFORE INSERT on vehicle_ownership)
--   * analytics + notifications on accept / complete / abandon
create temp table _fn_before as select oid from pg_proc where pronamespace = 'public'::regnamespace;

-- ---------------------------------------------------------------- columns
alter table vehicle_definitions
  add column unlock_level integer not null default 1 check (unlock_level >= 1),
  add column description  text;

alter table vehicle_ownership
  add column nickname      text  check (nickname is null or char_length(nickname) between 1 and 24),
  add column customization jsonb not null default '{}'::jsonb check (jsonb_typeof(customization) = 'object' and pg_column_size(customization) <= 1024);

alter table profiles add column selected_vehicle_id uuid references vehicle_ownership(id) on delete set null;

alter table jobs add column min_level integer not null default 1 check (min_level >= 1);
alter table job_assignments add column session_id uuid references game_sessions(id) on delete set null;
create index job_assignments_player_status_idx on job_assignments(player_id, status);
create index job_assignments_completed_idx on job_assignments(completed_at) where status = 'completed';
create index jobs_created_idx on jobs(created_at desc) where status = 'open';

-- ---------------------------------------------------------------- vehicle rules
create function _vehicle_unlock_guard() returns trigger
language plpgsql security definer set search_path = public as $$
declare v_level integer; v_need integer;
begin
  select level into v_level from profiles where id = new.player_id;
  select unlock_level into v_need from vehicle_definitions where id = new.definition_id;
  if coalesce(v_level, 1) < coalesce(v_need, 1) then raise exception 'vehicle_locked'; end if;
  return new;
end $$;
create trigger vehicle_ownership_unlock before insert on vehicle_ownership
  for each row execute function _vehicle_unlock_guard();

create function _vehicle_acquired() returns trigger
language plpgsql security definer set search_path = public as $$
begin
  if (select price from vehicle_definitions where id = new.definition_id) > 0 then
    perform _track(new.player_id, 'vehicle_purchased', jsonb_build_object('definition_id', new.definition_id));
  end if;
  return new;
end $$;
create trigger vehicle_ownership_acquired after insert on vehicle_ownership
  for each row execute function _vehicle_acquired();

-- ---------------------------------------------------------------- job lifecycle rules
create function _assignment_guard() returns trigger
language plpgsql security definer set search_path = public as $$
declare j jobs; v_level integer; v_origin uuid; v_dest uuid; v_access text;
begin
  select * into j from jobs where id = new.job_id;
  select level into v_level from profiles where id = new.player_id;
  if v_level < j.min_level then raise exception 'level_too_low'; end if;
  select city_id into v_origin from locations where id = j.origin_id;
  select city_id into v_dest   from locations where id = j.destination_id;
  v_access := _route_access(v_origin, v_dest, v_level);
  if v_access <> 'ok' then raise exception '%', v_access; end if;
  new.session_id := (select s.id from game_sessions s where s.player_id = new.player_id and s.ended_at is null
                      order by s.last_seen_at desc limit 1);
  return new;
end $$;
create trigger job_assignments_guard before insert on job_assignments
  for each row execute function _assignment_guard();

create function _assignment_accepted() returns trigger
language plpgsql security definer set search_path = public as $$
begin
  perform _track(new.player_id, 'job_accepted', jsonb_build_object('job_id', new.job_id), new.session_id);
  return new;
end $$;
create trigger job_assignments_accepted after insert on job_assignments
  for each row execute function _assignment_accepted();

create function _assignment_status_changed() returns trigger
language plpgsql security definer set search_path = public as $$
declare v_code text;
begin
  if new.status = old.status then return new; end if;
  if new.status = 'completed' then
    perform _track(new.player_id, 'job_completed', jsonb_build_object('job_id', new.job_id, 'distance_km', round(coalesce(new.reported_distance_km, 0), 2)), new.session_id);
  elsif new.status = 'abandoned' then
    select code into v_code from jobs where id = new.job_id;
    perform _track(new.player_id, 'job_abandoned', jsonb_build_object('job_id', new.job_id), new.session_id);
    perform _notify(new.player_id, 'job_event', 'Job abandoned', 'You abandoned job ' || v_code || '.',
                    jsonb_build_object('job_id', new.job_id, 'assignment_id', new.id), 'jobabandon:' || new.id);
  end if;
  return new;
end $$;
create trigger job_assignments_status after update of status on job_assignments
  for each row execute function _assignment_status_changed();

-- ---------------------------------------------------------------- vehicle API
-- Status vocabulary matches the dashboard: needs_repair (damage >= 70) beats on_job / on_route; then low_fuel (< 10 %).
create function _vehicle_status(v vehicle_ownership, d vehicle_definitions) returns text
language plpgsql stable security definer set search_path = public as $$
declare v_fuel_pct numeric := case when d.fuel_capacity_l > 0 then v.fuel_l / d.fuel_capacity_l * 100 else 0 end;
begin
  if v.damage_pct >= 100 then return 'destroyed'; end if;
  if v.damage_pct >= 70 then return 'needs_repair'; end if;
  if exists (select 1 from job_assignments where vehicle_id = v.id and status in ('accepted','in_progress')) then return 'on_job'; end if;
  if exists (select 1 from bus_runs where vehicle_id = v.id and status = 'active') then return 'on_route'; end if;
  if v_fuel_pct < 10 then return 'low_fuel'; end if;
  return 'ready';
end $$;

create function _vehicle_json(v vehicle_ownership, d vehicle_definitions, p_selected uuid) returns jsonb
language plpgsql stable security definer set search_path = public as $$
begin
  return jsonb_build_object(
    'id', v.id, 'definition_id', d.id, 'name', coalesce(v.nickname, d.name), 'model', d.name, 'nickname', v.nickname,
    'category', d.category, 'status', _vehicle_status(v, d), 'selected', v.id = p_selected,
    'condition_pct', round(100 - v.damage_pct, 1), 'damage_pct', v.damage_pct,
    'fuel_l', v.fuel_l, 'fuel_capacity_l', d.fuel_capacity_l,
    'fuel_pct', round(case when d.fuel_capacity_l > 0 then v.fuel_l / d.fuel_capacity_l * 100 else 0 end, 1),
    'mileage_km', round(v.odometer_km, 1), 'top_speed_kmh', d.max_speed_kmh,
    'cargo_capacity_kg', d.cargo_capacity_kg, 'passenger_capacity', d.passenger_capacity,
    'customization', v.customization, 'acquired_at', v.acquired_at);
end $$;

-- The vehicle the dashboard shows: the player's selection, else the first one they acquired.
create function _selected_vehicle(p_player uuid) returns uuid
language sql stable security definer set search_path = public as $$
  select coalesce(
    (select selected_vehicle_id from profiles where id = p_player),
    (select id from vehicle_ownership where player_id = p_player order by acquired_at, id limit 1)) $$;

create function list_my_vehicles() returns jsonb
language plpgsql security definer set search_path = public as $$
declare v_uid uuid := auth.uid(); v_sel uuid;
begin
  if v_uid is null then raise exception 'not_authenticated'; end if;
  perform _rate_limit('vehicle_read', 120, 60);
  v_sel := _selected_vehicle(v_uid);
  return jsonb_build_object('items', coalesce((
      select jsonb_agg(_vehicle_json(v, d, v_sel) order by v.acquired_at, v.id)
        from vehicle_ownership v join vehicle_definitions d on d.id = v.definition_id where v.player_id = v_uid), '[]'::jsonb),
    'selected_id', v_sel, 'server_time', now());
end $$;

-- What can be bought: price, unlock level, whether the caller has unlocked / can afford it.
create function list_vehicle_catalog() returns jsonb
language plpgsql security definer set search_path = public as $$
declare v_uid uuid := auth.uid(); v_level integer; v_bal bigint;
begin
  if v_uid is null then raise exception 'not_authenticated'; end if;
  perform _rate_limit('vehicle_read', 120, 60);
  select level into v_level from profiles where id = v_uid;
  select balance into v_bal from player_wallets where player_id = v_uid;
  return jsonb_build_object('items', coalesce((
      select jsonb_agg(jsonb_build_object('definition_id', d.id, 'name', d.name, 'category', d.category, 'description', d.description,
               'price', d.price, 'unlock_level', d.unlock_level, 'top_speed_kmh', d.max_speed_kmh,
               'cargo_capacity_kg', d.cargo_capacity_kg, 'passenger_capacity', d.passenger_capacity, 'fuel_capacity_l', d.fuel_capacity_l,
               'owned', (select count(*) from vehicle_ownership where player_id = v_uid and definition_id = d.id),
               'unlocked', v_level >= d.unlock_level, 'purchasable', d.price > 0,
               'can_afford', coalesce(v_bal, 0) >= d.price) order by d.price, d.id)
        from vehicle_definitions d), '[]'::jsonb), 'server_time', now());
end $$;

create function set_active_vehicle(p_vehicle uuid) returns void
language plpgsql security definer set search_path = public as $$
begin
  if auth.uid() is null then raise exception 'not_authenticated'; end if;
  perform _rate_limit('vehicle_write', 30, 60);
  if not exists (select 1 from vehicle_ownership where id = p_vehicle and player_id = auth.uid()) then raise exception 'vehicle_not_owned'; end if;
  update profiles set selected_vehicle_id = p_vehicle where id = auth.uid();
end $$;

create function rename_vehicle(p_vehicle uuid, p_name text) returns void
language plpgsql security definer set search_path = public as $$
begin
  if auth.uid() is null then raise exception 'not_authenticated'; end if;
  perform _rate_limit('vehicle_write', 30, 60);
  if p_name is not null and p_name !~ '^[A-Za-z0-9_ -]{1,24}$' then raise exception 'invalid_vehicle_name'; end if;
  update vehicle_ownership set nickname = nullif(trim(p_name), '') where id = p_vehicle and player_id = auth.uid();
  if not found then raise exception 'vehicle_not_owned'; end if;
end $$;

-- Cosmetics only. Whitelisted keys and formats; nothing here can affect stats, fuel, damage or money.
create function set_vehicle_customization(p_vehicle uuid, p_data jsonb) returns void
language plpgsql security definer set search_path = public as $$
declare k text; v jsonb;
begin
  if auth.uid() is null then raise exception 'not_authenticated'; end if;
  perform _rate_limit('vehicle_write', 30, 60);
  if p_data is null or jsonb_typeof(p_data) <> 'object' then raise exception 'invalid_customization'; end if;
  for k, v in select * from jsonb_each(p_data) loop
    if jsonb_typeof(v) <> 'string' then raise exception 'invalid_customization'; end if;
    if k = 'color'  and (v #>> '{}') !~ '^#[0-9a-fA-F]{6}$' then raise exception 'invalid_customization'; end if;
    if k = 'livery' and (v #>> '{}') !~ '^[a-z0-9_]{1,32}$' then raise exception 'invalid_customization'; end if;
    if k = 'plate'  and (v #>> '{}') !~ '^[A-Z0-9 -]{2,10}$' then raise exception 'invalid_customization'; end if;
    if k not in ('color','livery','plate') then raise exception 'invalid_customization'; end if;
  end loop;
  update vehicle_ownership set customization = p_data where id = p_vehicle and player_id = auth.uid();
  if not found then raise exception 'vehicle_not_owned'; end if;
end $$;

-- ---------------------------------------------------------------- job board API
-- Why this player cannot take this job right now (null = they can). Mirrors the checks accept_job + the triggers will make.
create function _job_block_reason(j jobs, p_player uuid, p_level integer) returns text
language plpgsql stable security definer set search_path = public as $$
declare v_access text;
begin
  if exists (select 1 from job_assignments where player_id = p_player and status in ('accepted','in_progress'))
     or exists (select 1 from bus_runs where player_id = p_player and status = 'active') then return 'activity_in_progress'; end if;
  if p_level < j.min_level then return 'level_too_low'; end if;
  v_access := _route_access((select city_id from locations where id = j.origin_id), (select city_id from locations where id = j.destination_id), p_level);
  if v_access <> 'ok' then return v_access; end if;
  if (select count(*) from job_assignments where job_id = j.id and status in ('accepted','in_progress','completed')) >= j.max_participants then return 'job_full'; end if;
  if not exists (select 1 from vehicle_ownership v join vehicle_definitions d on d.id = v.definition_id
                  where v.player_id = p_player and d.category = j.required_category and v.damage_pct < 100) then return 'no_matching_vehicle'; end if;
  return null;
end $$;

create function _job_json(j jobs, p_viewer uuid, p_level integer) returns jsonb
language plpgsql stable security definer set search_path = public as $$
declare o locations; d locations; oc cities; dc cities; v_reason text;
begin
  select * into o from locations where id = j.origin_id;      select * into oc from cities where id = o.city_id;
  select * into d from locations where id = j.destination_id; select * into dc from cities where id = d.city_id;
  v_reason := _job_block_reason(j, p_viewer, p_level);
  return jsonb_build_object(
    'id', j.id, 'code', j.code, 'status', j.status, 'created_at', j.created_at, 'expires_at', j.expires_at,
    'origin', jsonb_build_object('slug', o.slug, 'name', o.name, 'city', oc.name, 'city_slug', oc.slug, 'world_x', o.world_x, 'world_z', o.world_z),
    'destination', jsonb_build_object('slug', d.slug, 'name', d.name, 'city', dc.name, 'city_slug', dc.slug, 'world_x', d.world_x, 'world_z', d.world_z),
    'distance_km', j.distance_km, 'est_minutes', greatest(1, round(j.distance_km / cfg('avg_speed_kmh') * 60))::int,
    'cargo', jsonb_build_object('type', j.cargo_type, 'weight_kg', j.cargo_weight_kg),
    'reward', j.reward, 'xp_reward', j.xp_reward, 'difficulty', j.difficulty,
    'requirements', jsonb_build_object('category', j.required_category, 'min_level', j.min_level, 'max_participants', j.max_participants),
    'slots_taken', (select count(*) from job_assignments where job_id = j.id and status in ('accepted','in_progress','completed')),
    'eligible', v_reason is null, 'blocked_reason', v_reason);
end $$;

create function list_available_jobs(p_limit integer default 30, p_offset integer default 0) returns jsonb
language plpgsql security definer set search_path = public as $$
declare v_uid uuid := auth.uid(); v_level integer; v_total integer;
begin
  if v_uid is null then raise exception 'not_authenticated'; end if;
  perform _rate_limit('jobs_read', 120, 60);
  select level into v_level from profiles where id = v_uid;
  select count(*)::int into v_total from jobs j
   where j.status = 'open' and j.expires_at > now() and not exists (select 1 from job_assignments a where a.job_id = j.id and a.player_id = v_uid);
  return jsonb_build_object('total', v_total, 'items', coalesce((
      select jsonb_agg(_job_json(j, v_uid, v_level) order by j.created_at desc, j.id)
        from (select * from jobs j where j.status = 'open' and j.expires_at > now()
                 and not exists (select 1 from job_assignments a where a.job_id = j.id and a.player_id = v_uid)
               order by j.created_at desc, j.id
               limit least(greatest(coalesce(p_limit, 30), 1), 100) offset greatest(coalesce(p_offset, 0), 0)) j), '[]'::jsonb),
    'server_time', now());
end $$;

create function get_job_details(p_job uuid) returns jsonb
language plpgsql security definer set search_path = public as $$
declare v_uid uuid := auth.uid(); j jobs;
begin
  if v_uid is null then raise exception 'not_authenticated'; end if;
  perform _rate_limit('jobs_read', 120, 60);
  select * into j from jobs where id = p_job;
  if not found then raise exception 'job_not_found'; end if;
  return _job_json(j, v_uid, (select level from profiles where id = v_uid));
end $$;

create function get_my_active_job() returns jsonb
language plpgsql security definer set search_path = public as $$
declare v_uid uuid := auth.uid(); a job_assignments; j jobs; t job_telemetry;
begin
  if v_uid is null then raise exception 'not_authenticated'; end if;
  perform _rate_limit('jobs_read', 120, 60);
  select * into a from job_assignments where player_id = v_uid and status in ('accepted','in_progress');
  if not found then return jsonb_build_object('active', null, 'server_time', now()); end if;
  select * into j from jobs where id = a.job_id;
  select * into t from job_telemetry where assignment_id = a.id;
  return jsonb_build_object('server_time', now(), 'active', jsonb_build_object(
    'assignment_id', a.id, 'status', a.status, 'vehicle_id', a.vehicle_id, 'accepted_at', a.accepted_at, 'started_at', a.started_at,
    'verified_km', coalesce(t.verified_km, 0),
    'progress_pct', round(least(100, coalesce(t.verified_km, 0) / j.distance_km * 100), 1),
    'job', _job_json(j, v_uid, (select level from profiles where id = v_uid))));
end $$;

-- ---------------------------------------------------------------- lock-down
do $$ declare r record;
begin
  for r in select p.oid::regprocedure as sig, p.proname from pg_proc p
            where p.pronamespace = 'public'::regnamespace and p.oid not in (select oid from _fn_before) loop
    execute format('revoke execute on function %s from public, anon, authenticated', r.sig);
    if r.proname in ('list_my_vehicles','list_vehicle_catalog','set_active_vehicle','rename_vehicle','set_vehicle_customization',
                     'list_available_jobs','get_job_details','get_my_active_job') then
      execute format('grant execute on function %s to authenticated', r.sig);
    end if;
  end loop;
end $$;
