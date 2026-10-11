-- Vehicle catalogue for the Trucks & Buses screen.
--   * trucks, buses, vans, cars and motorcycles live in ONE table (vehicle_definitions); the screen filters it, nothing is duplicated per tab
--   * specifications come from the definition row; nothing on screen is typed into the client
--   * availability: 'available' vehicles exist in the game (a drivable model + physics); 'coming_soon' vehicles are in the catalogue with their
--     real specification but cannot be bought until their model ships
--   * one copy of a model per player (unique index): a double click or a race can never charge twice or own two
--   * list_vehicle_catalog tells the caller WHY a vehicle can or cannot be bought (purchase_state) using the server's own wallet and level
--   * get_vehicle_job_fit explains which of the open jobs a model can take (same category rule accept_job enforces)
-- Brand-sheet values (hp, torque, engine, transmission, seats, GVW, features) are from the ARO Motors line-up sheets. Price, top speed, fuel and cargo
-- capacity of the coming-soon models are authored game-design values and may change before those vehicles ship.
create temp table _fn_before as select oid from pg_proc where pronamespace = 'public'::regnamespace;

alter table vehicle_definitions drop constraint vehicle_definitions_category_check;
alter table vehicle_definitions add constraint vehicle_definitions_category_check check (category in ('truck','bus','van','car','motorcycle'));

alter table vehicle_definitions
  add column manufacturer   text    not null default 'ARO Motors',
  add column vehicle_class  text,
  add column drive          text,
  add column engine         text,
  add column engine_hp      integer check (engine_hp is null or engine_hp > 0),
  add column torque_nm      integer check (torque_nm is null or torque_nm > 0),
  add column displacement_l numeric check (displacement_l is null or displacement_l > 0),
  add column transmission   text,
  add column fuel_type      text,
  add column weight_kg      integer check (weight_kg is null or weight_kg > 0),
  add column weight_basis   text    check (weight_basis is null or weight_basis in ('kerb','gross')),
  add column features       text[]  not null default '{}',
  add column availability   text    not null default 'available' check (availability in ('available','coming_soon')),
  add column picture_key    text    check (picture_key is null or picture_key ~ '^[a-z0-9_]{1,40}$'),
  add column sort_order     integer not null default 100;

-- The three vehicles that exist in the game today: facts that were already in their definition (physics stats) become columns.
update vehicle_definitions set vehicle_class = 'Light Truck',  drive = '4x2', torque_nm = (stats->>'torqueNm')::int, weight_kg = (stats->>'massKg')::int, weight_basis = 'kerb',
       picture_key = 'aro_light_180',  sort_order = 10 where id = 'truck_light_01';
update vehicle_definitions set vehicle_class = 'Medium Truck', drive = '6x2', torque_nm = (stats->>'torqueNm')::int, weight_kg = (stats->>'massKg')::int, weight_basis = 'kerb',
       picture_key = 'aro_cargo_260',  sort_order = 20 where id = 'truck_medium_01';
update vehicle_definitions set vehicle_class = 'City Bus',     torque_nm = (stats->>'torqueNm')::int, weight_kg = (stats->>'massKg')::int, weight_basis = 'kerb',
       picture_key = 'aro_city_bus',   sort_order = 30 where id = 'bus_city_01';

-- The ARO Motors line-up (brand sheets). Coming soon: no drivable model exists yet, so none of them can be bought.
insert into vehicle_definitions(id, category, name, price, cargo_capacity_kg, passenger_capacity, fuel_capacity_l, max_speed_kmh, stats, unlock_level, description,
    vehicle_class, drive, engine, engine_hp, torque_nm, displacement_l, transmission, fuel_type, weight_kg, weight_basis, features, availability, picture_key, sort_order) values
 ('aro_titan_480', 'truck', 'ARO TITAN 480', 95000, 40000, 0, 1200, 120, '{}', 10,
  'A powerful, reliable and rugged long-haul truck designed specifically for African roads. Engineered for strength, comfort and efficiency, it delivers exceptional performance across highways, urban routes and demanding terrain.',
  'Heavy Tractor Unit', '6x4', 'ARO D12 Turbo Diesel (EURO V)', 480, 2300, 12.0, '12-Speed Automated Manual', 'Diesel', 60000, 'gross',
  array['Long Haul','Heavy Cargo','African Roads','High Roof Sleeper Cabin','Heavy Duty Air Suspension'], 'coming_soon', 'aro_titan_480', 100),
 ('aro_hauler_620', 'truck', 'ARO HAULER 620', 120000, 32000, 0, 900, 100, '{}', 15,
  'Heavy cargo truck for construction and mining work.',
  'Heavy Cargo Truck', '8x4', 'Turbo Diesel', 620, null, null, null, 'Diesel', null, null,
  array['High Torque','Heavy Cargo','Construction','Mining'], 'coming_soon', 'aro_hauler_620', 110),
 ('aro_runner_300', 'truck', 'ARO RUNNER 300', 70000, 12000, 0, 300, 110, '{}', 5,
  'Medium duty truck for general cargo on urban and regional routes.',
  'Medium Duty Truck', '4x2 / 6x2', null, 300, null, null, null, null, null, null,
  array['General Cargo','Urban & Regional','Cost Efficient'], 'coming_soon', 'aro_runner_300', 120),
 ('aro_cityliner_45', 'bus', 'ARO CITYLINER 45', 90000, 0, 55, 400, 110, '{}', 8,
  'Intercity bus with a comfortable cabin and air conditioning for long distance African routes.',
  'Intercity Bus', null, null, null, null, null, null, null, null, null,
  array['Comfort Cabin','Air Conditioning','Long Distance','African Routes'], 'coming_soon', 'aro_cityliner_45', 200),
 ('aro_urban_32', 'bus', 'ARO URBAN 32', 65000, 0, 32, 250, 80, '{}', 3,
  'Low floor city bus built for durable urban transport in African cities.',
  'City Bus', null, null, null, null, null, null, null, null, null,
  array['Low Floor','Urban Transport','Durable Build','African Cities'], 'coming_soon', 'aro_urban_32', 210),
 ('aro_transit_16', 'bus', 'ARO TRANSIT 16', 45000, 0, 16, 90, 120, '{}', 2,
  'Comfortable and reliable passenger minibus for urban and intercity transport.',
  'Passenger Minibus', null, null, null, null, null, null, null, null, null,
  array['Comfortable','Urban & Intercity','Reliable','African Transport'], 'coming_soon', 'aro_transit_16', 220),
 ('aro_citygo', 'car', 'ARO CITYGO', 28000, 0, 5, 45, 170, '{}', 1,
  'Compact car for fuel efficient urban driving.',
  'Compact Car', null, null, null, null, null, null, null, null, null,
  array['Fuel Efficient','Urban Driving','Affordable','Modern Design'], 'coming_soon', 'aro_citygo', 300),
 ('aro_nomad', 'car', 'ARO NOMAD', 52000, 0, 7, 75, 160, '{}', 5,
  'Seven seat all terrain SUV for family and executive use.',
  'SUV', '4x4', null, null, null, null, null, null, null, null,
  array['Family & Executive','Comfortable','All Terrain'], 'coming_soon', 'aro_nomad', 310),
 ('aro_ancora', 'car', 'ARO ANCORA', 48000, 1000, 0, 80, 150, '{}', 4,
  'Pickup utility vehicle for rough terrain, work and personal use.',
  'Pickup Utility', '4x4 / 4x2', '2.8 Turbo Diesel', null, null, 2.8, null, 'Diesel', null, null,
  array['Utility & Support','Rough Terrain','Work & Personal'], 'coming_soon', 'aro_ancora', 320),
 ('aro_cargo_van', 'van', 'ARO CARGO VAN', 38000, 1200, 0, 80, 130, '{}', 3,
  'Delivery van with a large load space for urban delivery and business use.',
  'Delivery Van', null, null, null, null, null, null, null, null, null,
  array['Large Load Space','Urban Delivery','Business Use','Reliable','Low Maintenance'], 'coming_soon', 'aro_cargo_van', 330),
 ('aro_ride_150', 'motorcycle', 'ARO RIDE 150', 9000, 0, 0, 14, 120, '{}', 1,
  'Fuel efficient 150cc motorcycle for urban and rural everyday use.',
  'Motorcycle', null, '150cc', null, null, 0.15, null, null, null, null,
  array['Fuel Efficient','Urban & Rural','Reliable','Everyday Use'], 'coming_soon', 'aro_ride_150', 340)
on conflict (id) do nothing;

-- ---------------------------------------------------------------- one copy of a model per player
create unique index vehicle_ownership_one_per_model on vehicle_ownership(player_id, definition_id);

-- Purchase: every refusal happens BEFORE any money moves; the debit and the ownership row are one transaction (a failure rolls both back).
create or replace function buy_vehicle(p_definition text) returns uuid
language plpgsql security definer set search_path = public as $$
declare
  v_uid uuid := auth.uid(); v_def vehicle_definitions; v_id uuid; v_level integer;
begin
  if v_uid is null then raise exception 'not_authenticated'; end if;
  select * into v_def from vehicle_definitions where id = p_definition;
  if not found then raise exception 'unknown_vehicle'; end if;
  if v_def.availability <> 'available' then raise exception 'not_available'; end if;
  if v_def.price <= 0 then raise exception 'not_for_sale'; end if;
  perform pg_advisory_xact_lock(hashtextextended('buy_vehicle:' || v_uid::text || ':' || v_def.id, 0));   -- two clicks / two tabs: the second waits, then sees the first
  if exists (select 1 from vehicle_ownership where player_id = v_uid and definition_id = v_def.id) then raise exception 'already_owned'; end if;
  select level into v_level from profiles where id = v_uid;
  if coalesce(v_level, 1) < v_def.unlock_level then raise exception 'vehicle_locked'; end if;
  begin
    perform _apply_transaction(v_uid, 'VEHICLE_PURCHASE', -v_def.price, 'buy:' || gen_random_uuid(), 'vehicle_definition', null);
  exception when check_violation then
    raise exception 'insufficient_funds';
  end;
  insert into vehicle_ownership(player_id, definition_id, fuel_l) values (v_uid, v_def.id, v_def.fuel_capacity_l)
    returning id into v_id;
  return v_id;
end $$;

-- ---------------------------------------------------------------- catalogue API
-- Why this caller can or cannot buy the model right now. Order matters: what the player can still change comes last.
create function _vehicle_purchase_state(d vehicle_definitions, p_owned boolean, p_level integer, p_balance bigint) returns text
language sql stable as $$
  select case when p_owned then 'owned'
              when d.availability <> 'available' then 'coming_soon'
              when d.price <= 0 then 'not_for_sale'
              when p_level < d.unlock_level then 'level_required'
              when p_balance < d.price then 'insufficient_funds'
              else 'available' end $$;

create or replace function list_vehicle_catalog() returns jsonb
language plpgsql security definer set search_path = public as $$
declare v_uid uuid := auth.uid(); v_level integer; v_bal bigint;
begin
  if v_uid is null then raise exception 'not_authenticated'; end if;
  perform _rate_limit('vehicle_read', 120, 60);
  select level into v_level from profiles where id = v_uid;
  select coalesce(balance, 0) into v_bal from player_wallets where player_id = v_uid;
  v_bal := coalesce(v_bal, 0);
  return jsonb_build_object('level', v_level, 'balance', v_bal, 'items', coalesce((
      select jsonb_agg(jsonb_build_object(
               'definition_id', d.id, 'name', d.name, 'category', d.category, 'description', d.description, 'manufacturer', d.manufacturer,
               'vehicle_class', d.vehicle_class, 'drive', d.drive, 'engine', d.engine, 'engine_hp', d.engine_hp, 'torque_nm', d.torque_nm,
               'displacement_l', d.displacement_l, 'transmission', d.transmission, 'fuel_type', d.fuel_type, 'weight_kg', d.weight_kg, 'weight_basis', d.weight_basis,
               'features', to_jsonb(d.features), 'availability', d.availability, 'picture_key', d.picture_key,
               'price', d.price, 'unlock_level', d.unlock_level, 'top_speed_kmh', d.max_speed_kmh,
               'cargo_capacity_kg', d.cargo_capacity_kg, 'passenger_capacity', d.passenger_capacity, 'fuel_capacity_l', d.fuel_capacity_l,
               'owned', (select count(*) from vehicle_ownership where player_id = v_uid and definition_id = d.id),
               'unlocked', v_level >= d.unlock_level, 'purchasable', d.price > 0 and d.availability = 'available',
               'can_afford', v_bal >= d.price,
               'purchase_state', _vehicle_purchase_state(d, exists (select 1 from vehicle_ownership where player_id = v_uid and definition_id = d.id), v_level, v_bal),
               'need_more', greatest(d.price - v_bal, 0))
             order by d.sort_order, d.price, d.id)
        from vehicle_definitions d), '[]'::jsonb), 'server_time', now());
end $$;

-- Which of the open jobs can this model take? The rule is the one accept_job enforces: the job's required category equals the vehicle's category.
create function get_vehicle_job_fit(p_definition text) returns jsonb
language plpgsql security definer set search_path = public as $$
declare d vehicle_definitions; v_total integer; v_fit integer; v_by jsonb;
begin
  if auth.uid() is null then raise exception 'not_authenticated'; end if;
  perform _rate_limit('vehicle_read', 120, 60);
  select * into d from vehicle_definitions where id = p_definition;
  if not found then raise exception 'unknown_vehicle'; end if;
  select count(*)::int, count(*) filter (where required_category = d.category)::int into v_total, v_fit
    from jobs where status = 'open' and expires_at > now();
  select jsonb_object_agg(c, (select count(*)::int from jobs where status = 'open' and expires_at > now() and required_category = c)) into v_by
    from unnest(array['truck','bus','van','car','motorcycle']) c;
  return jsonb_build_object('definition_id', d.id, 'category', d.category, 'open_jobs', v_total, 'fits', v_fit, 'open_by_category', v_by,
    'compatible', v_fit > 0,
    'reason', case when v_fit > 0 then null when v_total = 0 then 'no_open_jobs' else 'category_mismatch' end,
    'drivable', d.availability = 'available');
end $$;

-- ---------------------------------------------------------------- lock-down
do $$ declare r record;
begin
  for r in select p.oid::regprocedure as sig, p.proname from pg_proc p
            where p.pronamespace = 'public'::regnamespace and p.oid not in (select oid from _fn_before) loop
    execute format('revoke execute on function %s from public, anon, authenticated', r.sig);
    if r.proname in ('get_vehicle_job_fit') then
      execute format('grant execute on function %s to authenticated', r.sig);
    end if;
  end loop;
end $$;
