-- Job board catalogue: everything the Jobs screen shows about a job comes from the server.
--   * cargo_types      authoritative cargo catalogue (category, description, fragile / special handling, picture key)
--   * game_config      fuel_price_per_l (the price buy_fuel charges; the estimate must agree with it - tested)
--   * _job_json        + cargo details, + the viewer's fuel / profit estimate, + whether they own a matching vehicle
--   * list_available_jobs  + server-side category filter and search (the client never filters data it was not sent)
--   * generate_jobs    draws from the catalogue; rewards, distances and weights are computed exactly as before
-- Nothing here changes who can accept, complete or be paid for a job.
create temp table _fn_before as select oid from pg_proc where pronamespace = 'public'::regnamespace;

-- ---------------------------------------------------------------- catalogue
create table cargo_types (
  code             text primary key check (code ~ '^[a-z0-9_]{2,32}$'),
  name             text not null unique,
  category         text not null check (category in ('freight','construction','fuel_chemicals','agriculture','manufacturing','vehicles')),
  description      text not null,
  fragile          boolean not null default false,
  special_handling boolean not null default false,
  image_key        text not null check (image_key ~ '^[a-z0-9_]{2,32}$'),
  weight_min_kg    integer not null default 500  check (weight_min_kg > 0),
  weight_max_kg    integer not null default 4000 check (weight_max_kg >= weight_min_kg)
);
alter table cargo_types enable row level security;
revoke all on cargo_types from anon, authenticated;

-- The first seven are the names the generator has always used (existing jobs keep matching); the rest complete the board.
insert into cargo_types(code, name, category, description, fragile, special_handling, image_key) values
  ('electronics',   'Electronics',            'manufacturing', 'Packed consumer electronics. Keep dry, avoid hard braking.', true,  false, 'general'),
  ('food_produce',  'Food & Produce',         'agriculture',   'Fresh market produce and packed food for city traders.',     false, false, 'food'),
  ('building_mats', 'Building Materials',     'construction',  'Blocks, sand and fittings for building sites.',              false, false, 'general'),
  ('textiles',      'Textiles',               'manufacturing', 'Bales of cloth and finished garments.',                      false, false, 'general'),
  ('fuel_drums',    'Fuel Drums',             'fuel_chemicals','Drummed fuel and lubricants on pallets.',                    false, true,  'tanker'),
  ('furniture',     'Furniture',              'manufacturing', 'Wardrobes, tables and sofas from the workshops.',            true,  false, 'general'),
  ('agri_goods',    'Agricultural Goods',     'agriculture',   'Grain, yam and cash crops from farms to markets.',           false, false, 'food'),
  ('containers',    'Containers',             'freight',       'Standard 20ft containers with general goods.',               false, false, 'containers'),
  ('cement_bags',   'Cement Bags',            'construction',  'Palletised 50 kg cement bags for construction projects.',    false, false, 'cement'),
  ('diesel',        'Fuel (Diesel)',          'fuel_chemicals','Bulk diesel in a tanker. Flammable: drive carefully.',       false, true,  'tanker'),
  ('steel_pipes',   'Steel Pipes',            'construction',  'Long steel pipes for pipelines and structures.',             false, false, 'pipes'),
  ('food_supplies', 'Food Supplies',          'agriculture',   'Crates of food for supermarkets and markets.',               false, false, 'food'),
  ('machinery',     'Construction Machinery', 'construction',  'Excavators and loaders on a low-bed trailer. Oversize load.',false, true,  'machinery'),
  ('new_vehicles',  'New Vehicles',           'vehicles',      'Cars and light trucks delivered to dealerships.',            true,  false, 'vehicles')
on conflict (code) do nothing;

insert into game_config(key, value, note) values
  ('fuel_price_per_l', 2, 'coins per litre; buy_fuel charges ceil(litres x 2) and the job board estimate uses this value (a test fails if they drift apart)')
on conflict (key) do nothing;

-- ---------------------------------------------------------------- job json (replaces the 20261010000006 version, same signature)
create or replace function _job_json(j jobs, p_viewer uuid, p_level integer) returns jsonb
language plpgsql stable security definer set search_path = public as $$
declare o locations; d locations; oc cities; dc cities; v_reason text; c cargo_types; v_burn numeric; v_fuel numeric; v_cost bigint;
begin
  select * into o from locations where id = j.origin_id;      select * into oc from cities where id = o.city_id;
  select * into d from locations where id = j.destination_id; select * into dc from cities where id = d.city_id;
  select * into c from cargo_types where name = j.cargo_type;
  v_reason := _job_block_reason(j, p_viewer, p_level);
  -- the viewer's own burn rate: their selected vehicle if it fits the job, else any vehicle they own that does, else the server default
  select coalesce((vd.stats->>'fuelBurnLPerKm')::numeric, 0.25) into v_burn
    from vehicle_ownership vo join vehicle_definitions vd on vd.id = vo.definition_id
   where vo.player_id = p_viewer and vd.category = j.required_category
   order by (vo.id = (select selected_vehicle_id from profiles where id = p_viewer)) desc, vo.acquired_at limit 1;
  v_burn := coalesce(v_burn, 0.25);
  v_fuel := round(j.distance_km * v_burn, 1);
  v_cost := ceil(j.distance_km * v_burn * cfg('fuel_price_per_l'));
  return jsonb_build_object(
    'id', j.id, 'code', j.code, 'status', j.status, 'created_at', j.created_at, 'expires_at', j.expires_at,
    'origin', jsonb_build_object('slug', o.slug, 'name', o.name, 'city', oc.name, 'city_slug', oc.slug, 'world_x', o.world_x, 'world_z', o.world_z),
    'destination', jsonb_build_object('slug', d.slug, 'name', d.name, 'city', dc.name, 'city_slug', dc.slug, 'world_x', d.world_x, 'world_z', d.world_z),
    'distance_km', j.distance_km, 'est_minutes', greatest(1, round(j.distance_km / cfg('avg_speed_kmh') * 60))::int,
    'cargo', jsonb_build_object('type', j.cargo_type, 'weight_kg', j.cargo_weight_kg,
        'category', coalesce(c.category, 'freight'),
        'description', coalesce(c.description, 'General cargo.'),
        'fragile', coalesce(c.fragile, false), 'special_handling', coalesce(c.special_handling, false),
        'image_key', coalesce(c.image_key, 'general')),
    'reward', j.reward, 'xp_reward', j.xp_reward, 'difficulty', j.difficulty,
    'requirements', jsonb_build_object('category', j.required_category, 'min_level', j.min_level, 'max_participants', j.max_participants,
        'has_vehicle', exists (select 1 from vehicle_ownership vo join vehicle_definitions vd on vd.id = vo.definition_id
                                where vo.player_id = p_viewer and vd.category = j.required_category and vo.damage_pct < 100)),
    'estimate', jsonb_build_object('fuel_l', v_fuel, 'fuel_cost', v_cost, 'profit', j.reward - v_cost),
    'slots_taken', (select count(*) from job_assignments where job_id = j.id and status in ('accepted','in_progress','completed')),
    'eligible', v_reason is null, 'blocked_reason', v_reason);
end $$;

-- ---------------------------------------------------------------- board with filter + search
drop function list_available_jobs(integer, integer);
create function list_available_jobs(p_limit integer default 30, p_offset integer default 0, p_category text default null, p_search text default null)
returns jsonb
language plpgsql security definer set search_path = public as $$
declare v_uid uuid := auth.uid(); v_level integer; v_total integer; v_q text; v_items jsonb; v_off integer; v_lim integer;
begin
  if v_uid is null then raise exception 'not_authenticated'; end if;
  perform _rate_limit('jobs_read', 120, 60);
  if p_category is not null and p_category not in ('freight','construction','fuel_chemicals','agriculture','manufacturing','vehicles') then raise exception 'invalid_category'; end if;
  v_q := nullif(trim(coalesce(p_search, '')), '');
  if v_q is not null then
    if char_length(v_q) > 40 then raise exception 'invalid_search'; end if;
    v_q := '%' || replace(replace(replace(lower(v_q), '\', '\\'), '%', '\%'), '_', '\_') || '%';
  end if;
  v_off := greatest(coalesce(p_offset, 0), 0); v_lim := least(greatest(coalesce(p_limit, 30), 1), 100);
  select level into v_level from profiles where id = v_uid;
  with f as (
    select j.id, row_number() over (order by j.created_at desc, j.id) as seq
      from jobs j
      left join cargo_types c on c.name = j.cargo_type
      join locations o on o.id = j.origin_id join cities oc on oc.id = o.city_id
      join locations d on d.id = j.destination_id join cities dc on dc.id = d.city_id
     where j.status = 'open' and j.expires_at > now()
       and not exists (select 1 from job_assignments a where a.job_id = j.id and a.player_id = v_uid)
       and (p_category is null or coalesce(c.category, 'freight') = p_category)
       and (v_q is null or lower(j.cargo_type) like v_q or lower(o.name) like v_q or lower(d.name) like v_q or lower(oc.name) like v_q or lower(dc.name) like v_q))
  select (select count(*)::int from f),
         coalesce((select jsonb_agg(_job_json(j, v_uid, v_level) order by f.seq)
                     from f join jobs j on j.id = f.id where f.seq > v_off and f.seq <= v_off + v_lim), '[]'::jsonb)
    into v_total, v_items;
  return jsonb_build_object('total', v_total, 'items', v_items, 'server_time', now());
end $$;

-- ---------------------------------------------------------------- generator draws from the catalogue (same economy formulas as before)
create or replace function generate_jobs(p_count int default 10) returns int
language plpgsql security definer set search_path = public as $$
declare
  i int; o locations; d locations; v_dist numeric; v_diff smallint; c cargo_types; v_n int := 0;
begin
  for i in 1..p_count loop
    select * into o from locations order by random() limit 1;
    select * into d from locations where id <> o.id order by random() limit 1;
    exit when d.id is null;
    v_dist := round((sqrt(power(o.world_x - d.world_x, 2) + power(o.world_z - d.world_z, 2)) / 1000.0 * 1.1)::numeric, 1);
    continue when v_dist < 0.5;
    v_diff := 1 + floor(random() * 5)::int;
    select * into c from cargo_types order by random() limit 1;
    insert into jobs(code, origin_id, destination_id, cargo_type, cargo_weight_kg, distance_km, difficulty,
                     reward, xp_reward, required_category)
    values ('AFR-' || lpad(nextval('job_code_seq')::text, 6, '0'), o.id, d.id, c.name,
            c.weight_min_kg + floor(random() * (c.weight_max_kg - c.weight_min_kg + 1))::int, v_dist, v_diff,
            ceil(v_dist * (40 + v_diff * 15)), ceil(v_dist * (5 + v_diff * 2)), 'truck');
    v_n := v_n + 1;
  end loop;
  update jobs set status = 'expired' where status = 'open' and expires_at <= now();
  return v_n;
end $$;

-- ---------------------------------------------------------------- lock-down (same convention as every migration)
do $$ declare r record;
begin
  for r in select p.oid::regprocedure as sig, p.proname from pg_proc p
            where p.pronamespace = 'public'::regnamespace and p.oid not in (select oid from _fn_before) loop
    execute format('revoke execute on function %s from public, anon, authenticated', r.sig);
    if r.proname in ('list_available_jobs') then
      execute format('grant execute on function %s to authenticated', r.sig);
    end if;
  end loop;
end $$;
