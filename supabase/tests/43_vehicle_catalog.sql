\set ON_ERROR_STOP on
-- Vehicle catalogue (Trucks & Buses): one data-driven catalogue, purchase states decided by the server, one copy per model, job fit.
create function pg_temp.j(p_uid uuid, p_sql text) returns jsonb language sql as $$ select testkit.as_user(p_uid, p_sql)::jsonb $$;
create temp table fx(k text primary key, v text);
insert into fx select k, testkit.make_user(n)::text from (values ('A','VcA'),('B','VcB'),('C','VcC')) t(k, n);
create function pg_temp.u(k text) returns uuid language sql as $$ select v::uuid from fx where fx.k = $1 $$;
create function pg_temp.fund(k text, amt bigint) returns void language sql as $$ select _apply_transaction(pg_temp.u($1), 'BONUS', $2, 'fund:' || gen_random_uuid()) $$;
create function pg_temp.bal(k text) returns bigint language sql as $$ select balance from player_wallets where player_id = pg_temp.u($1) $$;
create function pg_temp.cat(k text) returns jsonb language sql as $$ select pg_temp.j(pg_temp.u($1), 'select list_vehicle_catalog()::text') $$;
create function pg_temp.item(k text, p_def text) returns jsonb language sql as $$
  select e from jsonb_array_elements(pg_temp.cat($1)->'items') e where e->>'definition_id' = $2 $$;
create function pg_temp.state(k text, p_def text) returns text language sql as $$ select pg_temp.item($1, $2)->>'purchase_state' $$;

-- ===== the catalogue is one list
select testkit.assert_eq('one list_vehicle_catalog signature', (select count(*)::text from pg_proc where proname = 'list_vehicle_catalog' and pronamespace = 'public'::regnamespace), '1');
select testkit.assert_eq('three drivable vehicles and the ARO line-up', jsonb_array_length(pg_temp.cat('A')->'items')::text, '14');
select testkit.assert_eq('trucks', (select count(*)::text from jsonb_array_elements(pg_temp.cat('A')->'items') e where e->>'category' = 'truck'), '5');
select testkit.assert_eq('buses', (select count(*)::text from jsonb_array_elements(pg_temp.cat('A')->'items') e where e->>'category' = 'bus'), '4');
select testkit.assert_eq('cars', (select count(*)::text from jsonb_array_elements(pg_temp.cat('A')->'items') e where e->>'category' = 'car'), '3');
select testkit.assert_eq('van and motorcycle', (select string_agg(e->>'category', ',' order by e->>'category') from jsonb_array_elements(pg_temp.cat('A')->'items') e where e->>'category' in ('van','motorcycle')), 'motorcycle,van');
select testkit.assert_eq('every model has a picture key', (select count(*)::text from jsonb_array_elements(pg_temp.cat('A')->'items') e where coalesce(e->>'picture_key', '') = ''), '0');
select testkit.assert_eq('sorted by the catalogue order, drivable first', (select e->>'definition_id' from jsonb_array_elements(pg_temp.cat('A')->'items') e limit 1), 'truck_light_01');
select testkit.assert_eq('the caller level and balance come with it', pg_temp.cat('A')->>'level', '1');
select testkit.expect_error('anon', null, 'select list_vehicle_catalog()', 'permission denied');
select testkit.expect_error('authenticated', pg_temp.u('A'), $$insert into vehicle_definitions(id, category, name, price, fuel_capacity_l, max_speed_kmh) values ('x','truck','x',1,1,1)$$, 'permission denied');
select testkit.expect_error('authenticated', pg_temp.u('A'), $$update vehicle_definitions set price = 0$$, 'permission denied');

-- ===== specification comes from the row
select testkit.assert_eq('titan: power', pg_temp.item('A', 'aro_titan_480')->>'engine_hp', '480');
select testkit.assert_eq('titan: torque', pg_temp.item('A', 'aro_titan_480')->>'torque_nm', '2300');
select testkit.assert_eq('titan: drive', pg_temp.item('A', 'aro_titan_480')->>'drive', '6x4');
select testkit.assert_eq('titan: transmission', pg_temp.item('A', 'aro_titan_480')->>'transmission', '12-Speed Automated Manual');
select testkit.assert_eq('titan: fuel capacity', pg_temp.item('A', 'aro_titan_480')->>'fuel_capacity_l', '1200');
select testkit.assert_eq('titan: weight is a gross weight', pg_temp.item('A', 'aro_titan_480')->>'weight_basis', 'gross');
select testkit.assert_eq('titan: manufacturer', pg_temp.item('A', 'aro_titan_480')->>'manufacturer', 'ARO Motors');
select testkit.assert_eq('cityliner: seats', pg_temp.item('A', 'aro_cityliner_45')->>'passenger_capacity', '55');
select testkit.assert_eq('nomad: seats', pg_temp.item('A', 'aro_nomad')->>'passenger_capacity', '7');
select testkit.assert_eq('a value the brand sheet does not give is null, never invented', pg_temp.item('A', 'aro_hauler_620')->>'torque_nm', null);
select testkit.assert_eq('the starter truck takes its torque from its physics definition', pg_temp.item('A', 'truck_light_01')->>'torque_nm', '900');
select testkit.assert_eq('features come as a list', jsonb_array_length(pg_temp.item('A', 'aro_titan_480')->'features')::text, '5');

-- ===== purchase states are decided by the server
select testkit.assert_eq('starter: owned', pg_temp.state('A', 'truck_light_01'), 'owned');
select testkit.assert_eq('line-up model: coming soon', pg_temp.state('A', 'aro_titan_480'), 'coming_soon');
select testkit.assert_eq('coming soon is never purchasable', pg_temp.item('A', 'aro_titan_480')->>'purchasable', 'false');
select testkit.assert_eq('poor player: insufficient funds', pg_temp.state('A', 'bus_city_01'), 'insufficient_funds');
select testkit.assert_eq('and how much is missing', pg_temp.item('A', 'bus_city_01')->>'need_more', (45000 - pg_temp.bal('A'))::text);
-- poor AND too low a level: the level is what the player must reach first
update vehicle_definitions set unlock_level = 4 where id = 'bus_city_01';
select testkit.assert_eq('poor and locked: the level is reported first', pg_temp.state('A', 'bus_city_01'), 'level_required');
select pg_temp.fund('A', 100000);
select testkit.assert_eq('level required beats the wallet', pg_temp.state('A', 'bus_city_01'), 'level_required');
update vehicle_definitions set unlock_level = 1 where id = 'bus_city_01';
select testkit.assert_eq('rich enough and unlocked: available', pg_temp.state('A', 'bus_city_01'), 'available');
select testkit.assert_eq('need_more is zero when affordable', pg_temp.item('A', 'bus_city_01')->>'need_more', '0');

-- a free model the player does not own is not for sale (the starter is owned by everyone, so use a temporary definition)
insert into vehicle_definitions(id, category, name, price, fuel_capacity_l, max_speed_kmh) values ('vc_free', 'truck', 'Free test truck', 0, 10, 10);
select testkit.assert_eq('free and not owned: not for sale', pg_temp.state('A', 'vc_free'), 'not_for_sale');
delete from vehicle_definitions where id = 'vc_free';

-- ===== buying
select testkit.expect_error('authenticated', pg_temp.u('A'), $$select buy_vehicle('aro_titan_480')$$, 'not_available');
select testkit.expect_error('authenticated', pg_temp.u('A'), $$select buy_vehicle('aro_ride_150')$$, 'not_available');
select testkit.expect_error('authenticated', pg_temp.u('A'), $$select buy_vehicle('truck_light_01')$$, 'not_for_sale');
select testkit.expect_error('authenticated', pg_temp.u('A'), $$select buy_vehicle('nope')$$, 'unknown_vehicle');
select testkit.assert_eq('refusals cost nothing', pg_temp.bal('A')::text, '105000');
do $$ declare before bigint := pg_temp.bal('A'); begin
  perform testkit.as_user(pg_temp.u('A'), $q$select buy_vehicle('bus_city_01')$q$);
  perform testkit.assert_eq('debited exactly the server price', pg_temp.bal('A')::text, (before - 45000)::text);
end $$;
select testkit.expect_error('authenticated', pg_temp.u('A'), $$select buy_vehicle('bus_city_01')$$, 'already_owned');
select testkit.assert_eq('a second click does not charge again', pg_temp.bal('A')::text, '60000');
select testkit.assert_eq('one ledger row', (select count(*)::text from transactions where player_id = pg_temp.u('A') and kind = 'VEHICLE_PURCHASE'), '1');
select testkit.assert_eq('one copy owned', (select count(*)::text from vehicle_ownership where player_id = pg_temp.u('A') and definition_id = 'bus_city_01'), '1');
select testkit.assert_eq('the catalogue now says owned', pg_temp.state('A', 'bus_city_01'), 'owned');
select testkit.assert_eq('the database refuses a second copy even without the function', (select (not exists (select 1 from pg_indexes where indexname = 'vehicle_ownership_one_per_model'))::text), 'false');
select testkit.expect_error('authenticated', pg_temp.u('B'), $$select buy_vehicle('bus_city_01')$$, 'insufficient_funds');
select testkit.assert_eq('failed purchase: no vehicle, no ledger row', (select count(*)::text from transactions where player_id = pg_temp.u('B') and kind = 'VEHICLE_PURCHASE'), '0');
update vehicle_definitions set unlock_level = 6 where id = 'truck_medium_01';
select pg_temp.fund('B', 100000);
select testkit.expect_error('authenticated', pg_temp.u('B'), $$select buy_vehicle('truck_medium_01')$$, 'vehicle_locked');
select testkit.assert_eq('locked purchase costs nothing', pg_temp.bal('B')::text, (select (5000 + 100000)::text));
update vehicle_definitions set unlock_level = 1 where id = 'truck_medium_01';
select testkit.expect_error('anon', null, $$select buy_vehicle('bus_city_01')$$, 'permission denied');

-- ===== selecting
select testkit.expect_error('authenticated', pg_temp.u('B'), format($$select set_active_vehicle(%L)$$, (select id from vehicle_ownership where player_id = pg_temp.u('A') and definition_id = 'bus_city_01')), 'vehicle_not_owned');
select testkit.assert_eq('selecting an owned vehicle persists', (select (testkit.as_user(pg_temp.u('A'), format($$select set_active_vehicle(%L)$$, (select id from vehicle_ownership where player_id = pg_temp.u('A') and definition_id = 'bus_city_01'))) is not distinct from '')::text), 'true');
select testkit.assert_eq('the selection is the one listed as selected', (select e->>'definition_id' from jsonb_array_elements(pg_temp.j(pg_temp.u('A'), 'select list_my_vehicles()::text')->'items') e where (e->>'selected')::boolean), 'bus_city_01');

-- ===== job fit: the rule accept_job enforces
update jobs set status = 'closed' where status = 'open';
insert into jobs(code, origin_id, destination_id, cargo_type, cargo_weight_kg, distance_km, difficulty, reward, xp_reward, required_category)
  select c, o.id, d.id, 'Containers', 8000, 50, 3, 1000, 50, cat from locations o, locations d, (values ('VC-T1','truck'),('VC-T2','truck'),('VC-B1','bus')) v(c, cat)
   where o.slug = 'lagos-apapa-port' and d.slug = 'lagos-mile12-market';
select testkit.assert_eq('truck: two open truck jobs fit', pg_temp.j(pg_temp.u('A'), $$select get_vehicle_job_fit('truck_light_01')::text$$)->>'fits', '2');
select testkit.assert_eq('truck: compatible', pg_temp.j(pg_temp.u('A'), $$select get_vehicle_job_fit('truck_light_01')::text$$)->>'compatible', 'true');
select testkit.assert_eq('the flagship truck fits the same jobs', pg_temp.j(pg_temp.u('A'), $$select get_vehicle_job_fit('aro_titan_480')::text$$)->>'fits', '2');
select testkit.assert_eq('but it says it is not drivable yet', pg_temp.j(pg_temp.u('A'), $$select get_vehicle_job_fit('aro_titan_480')::text$$)->>'drivable', 'false');
select testkit.assert_eq('bus: one open bus job fits', pg_temp.j(pg_temp.u('A'), $$select get_vehicle_job_fit('bus_city_01')::text$$)->>'fits', '1');
select testkit.assert_eq('a car fits no job', pg_temp.j(pg_temp.u('A'), $$select get_vehicle_job_fit('aro_citygo')::text$$)->>'compatible', 'false');
select testkit.assert_eq('and the reason is the category', pg_temp.j(pg_temp.u('A'), $$select get_vehicle_job_fit('aro_citygo')::text$$)->>'reason', 'category_mismatch');
select testkit.assert_eq('every category is present, also with zero', (pg_temp.j(pg_temp.u('A'), $$select get_vehicle_job_fit('aro_citygo')::text$$)->'open_by_category'->>'motorcycle'), '0');
select testkit.assert_eq('open jobs are counted per category', pg_temp.j(pg_temp.u('A'), $$select get_vehicle_job_fit('aro_citygo')::text$$)->'open_by_category'->>'truck', '2');
update jobs set status = 'closed' where status = 'open';
select testkit.assert_eq('no open jobs at all is its own reason', pg_temp.j(pg_temp.u('A'), $$select get_vehicle_job_fit('truck_light_01')::text$$)->>'reason', 'no_open_jobs');
select testkit.expect_error('authenticated', pg_temp.u('A'), $$select get_vehicle_job_fit('nope')$$, 'unknown_vehicle');
select testkit.expect_error('anon', null, $$select get_vehicle_job_fit('truck_light_01')$$, 'permission denied');

-- ===== schema guards
select testkit.expect_error('authenticated', pg_temp.u('A'), 'select _vehicle_purchase_state(null, false, 1, 1)', 'permission denied');
do $$ begin
  begin insert into vehicle_definitions(id, category, name, price, fuel_capacity_l, max_speed_kmh) values ('boat_1', 'boat', 'Boat', 1, 1, 1); raise exception 'category accepted';
  exception when check_violation then null; end;
  begin insert into vehicle_definitions(id, category, name, price, fuel_capacity_l, max_speed_kmh, availability) values ('x_1', 'car', 'X', 1, 1, 1, 'maybe'); raise exception 'availability accepted';
  exception when check_violation then null; end;
  begin insert into vehicle_definitions(id, category, name, price, fuel_capacity_l, max_speed_kmh, picture_key) values ('x_2', 'car', 'X', 1, 1, 1, '../etc/passwd'); raise exception 'picture key accepted';
  exception when check_violation then null; end;
  begin insert into vehicle_ownership(player_id, definition_id, fuel_l) values (pg_temp.u('A'), 'bus_city_01', 1); raise exception 'second copy accepted';
  exception when unique_violation then null; end;
end $$;
select 'VEHICLE CATALOG TESTS PASSED' as result;
