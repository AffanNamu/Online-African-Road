-- Seed: initial Lagos -> Ibadan corridor. Coordinates are Unity world metres (origin = Lagos Island depot).
-- Real distance Lagos-Ibadan ~ 120 km; the playable corridor is compressed (see docs/WORLD_DESIGN.md).
insert into countries(code, name) values ('NG', 'Nigeria') on conflict do nothing;

insert into cities(country_code, name, lat, lng) values
  ('NG', 'Lagos',  6.5244, 3.3792),
  ('NG', 'Ibadan', 7.3775, 3.9470)
on conflict do nothing;

insert into locations(city_id, slug, name, kind, world_x, world_z)
select c.id, v.slug, v.name, v.kind, v.x, v.z from (values
  ('Lagos',  'lagos-apapa-port',     'Apapa Port Depot',        'port',      0,      0),
  ('Lagos',  'lagos-ikeja-industrial','Ikeja Industrial Estate', 'factory',   4200,   2500),
  ('Lagos',  'lagos-mile12-market',  'Mile 12 Market',          'market',    2600,  -900),
  ('Ibadan', 'ibadan-bodija-market', 'Bodija Market',           'market',   38000,  21000),
  ('Ibadan', 'ibadan-ojoo-depot',    'Ojoo Freight Depot',      'depot',    36500,  19500),
  ('Ibadan', 'ibadan-challenge',     'Challenge Warehouse',     'warehouse',39500,  22800),
  ('Lagos',  'lagos-oshodi-terminal','Oshodi Terminal',         'station',   3400,    800),
  ('Lagos',  'lagos-ikeja-bus-park', 'Ikeja Bus Park',          'station',   4150,   2450)
) as v(city, slug, name, kind, x, z) join cities c on c.name = v.city
on conflict (slug) do nothing;

insert into vehicle_definitions(id, category, name, price, cargo_capacity_kg, passenger_capacity,
                                fuel_capacity_l, max_speed_kmh, stats) values
 ('truck_light_01',  'truck', 'Savanna 4x2 Light Truck', 0,      4000, 0, 120, 110,
   '{"massKg":4200,"torqueNm":900,"gears":[3.9,2.3,1.5,1.0,0.8],"reverseRatio":3.6,"finalDrive":4.1,"brakeTorqueNm":4500,"maxSteerDeg":32,"fuelBurnLPerKm":0.22}'),
 ('truck_medium_01', 'truck', 'Harmattan 6x2 Medium Truck', 60000, 9000, 0, 250, 100,
   '{"massKg":8500,"torqueNm":1400,"gears":[4.5,2.8,1.8,1.2,0.9,0.75],"reverseRatio":4.0,"finalDrive":4.6,"brakeTorqueNm":7500,"maxSteerDeg":28,"fuelBurnLPerKm":0.32}'),
 ('bus_city_01',     'bus',   'Danfo City Bus', 45000, 0, 40, 180, 90,
   '{"massKg":9500,"torqueNm":1100,"gears":[4.2,2.5,1.6,1.0,0.8],"reverseRatio":3.8,"finalDrive":4.4,"brakeTorqueNm":6500,"maxSteerDeg":30,"fuelBurnLPerKm":0.28}')
on conflict (id) do nothing;

-- Catalogue facts of the three drivable vehicles (migration 20261012000001 adds the columns; on a fresh database this seed runs after it, and the
-- upgrade test seeds an older schema first, so the update only runs when the columns exist).
do $$ begin
  if exists (select 1 from information_schema.columns where table_schema = 'public' and table_name = 'vehicle_definitions' and column_name = 'vehicle_class') then
    update vehicle_definitions set vehicle_class = 'Light Truck',  drive = '4x2', torque_nm = (stats->>'torqueNm')::int, weight_kg = (stats->>'massKg')::int, weight_basis = 'kerb',
           picture_key = 'aro_light_180',  sort_order = 10 where id = 'truck_light_01';
    update vehicle_definitions set vehicle_class = 'Medium Truck', drive = '6x2', torque_nm = (stats->>'torqueNm')::int, weight_kg = (stats->>'massKg')::int, weight_basis = 'kerb',
           picture_key = 'aro_cargo_260',  sort_order = 20 where id = 'truck_medium_01';
    update vehicle_definitions set vehicle_class = 'City Bus',     torque_nm = (stats->>'torqueNm')::int, weight_kg = (stats->>'massKg')::int, weight_basis = 'kerb',
           picture_key = 'aro_city_bus',   sort_order = 30 where id = 'bus_city_01';
  end if;
end $$;

-- Bus route LAG-R1 follows the same corridor nodes as the truck route (Mile 12 -> Oshodi -> Ikeja).
insert into bus_routes(code, name, city_id, fare, xp_reward)
select 'LAG-R1', 'Mile 12 - Oshodi - Ikeja', c.id, 15, 150 from cities c where c.name = 'Lagos'
on conflict (code) do nothing;

insert into bus_route_stops(route_id, seq, location_id, demand, alight_pct)
select r.id, v.seq, l.id, v.demand, v.alight from bus_routes r
join (values (1,'lagos-mile12-market',30,0), (2,'lagos-oshodi-terminal',40,50), (3,'lagos-ikeja-bus-park',0,100)) as v(seq, slug, demand, alight) on true
join locations l on l.slug = v.slug
where r.code = 'LAG-R1'
on conflict do nothing;
