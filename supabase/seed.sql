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
  ('Ibadan', 'ibadan-challenge',     'Challenge Warehouse',     'warehouse',39500,  22800)
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
