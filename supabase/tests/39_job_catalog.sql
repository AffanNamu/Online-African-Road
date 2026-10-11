\set ON_ERROR_STOP on
-- Job board catalogue: cargo details, per-player estimates, server-side category filter and search.
create function pg_temp.j(p_uid uuid, p_sql text) returns jsonb language sql as $$ select testkit.as_user(p_uid, p_sql)::jsonb $$;
create temp table fx(k text primary key, v text);
insert into fx select k, testkit.make_user(n)::text from (values ('A','JcA'),('B','JcB')) t(k, n);
create function pg_temp.u(k text) returns uuid language sql as $$ select v::uuid from fx where fx.k = $1 $$;
create function pg_temp.board(p_uid uuid, p_args text default '') returns jsonb language sql as $$
  select testkit.as_user(p_uid, format('select list_available_jobs(%s)::text', p_args))::jsonb $$;
create function pg_temp.row(p_uid uuid, p_code text) returns jsonb language sql as $$
  select e from jsonb_array_elements(pg_temp.board(p_uid, '100, 0')->'items') e where e->>'code' = p_code $$;
create function pg_temp.codes(p_uid uuid, p_args text) returns text language sql as $$
  select coalesce(string_agg(e->>'code', ',' order by e->>'code'), 'none') from jsonb_array_elements(pg_temp.board(p_uid, p_args)->'items') e $$;

-- a clean board: only this suite's jobs are open
update jobs set status = 'closed' where status = 'open';
create function pg_temp.mk(p_code text, p_cargo text, p_dist numeric, p_reward bigint, p_to text default 'lagos-mile12-market', p_cat text default 'truck') returns void language sql as $$
  insert into jobs(code, origin_id, destination_id, cargo_type, cargo_weight_kg, distance_km, difficulty, reward, xp_reward, required_category)
  select p_code, o.id, d.id, p_cargo, 8000, p_dist, 3, p_reward, 50, p_cat from locations o, locations d where o.slug = 'lagos-apapa-port' and d.slug = p_to $$;
select pg_temp.mk('JC-CONT', 'Containers', 100, 4500);
select pg_temp.mk('JC-ELEC', 'Electronics', 40, 1200);
select pg_temp.mk('JC-DSL',  'Fuel (Diesel)', 60, 2000, 'lagos-ikeja-industrial');
select pg_temp.mk('JC-CEM',  'Cement Bags', 30, 900, 'lagos-ikeja-industrial');
select pg_temp.mk('JC-UNK',  'Cocoa', 20, 700);
select pg_temp.mk('JC-BUS',  'Containers', 10, 300, 'lagos-mile12-market', 'bus');

-- ===== catalogue is server-only
select testkit.expect_error('authenticated', pg_temp.u('A'), 'select * from cargo_types', 'permission denied');
select testkit.expect_error('authenticated', pg_temp.u('A'), $$update cargo_types set fragile = true$$, 'permission denied');
select testkit.expect_error('anon', null, 'select * from cargo_types', 'permission denied');
select testkit.assert_eq('one list_available_jobs signature (no ambiguous overload for PostgREST)', (select count(*)::text from pg_proc where proname = 'list_available_jobs' and pronamespace = 'public'::regnamespace), '1');
select testkit.assert_eq('catalogue covers every category', (select count(distinct category)::text from cargo_types), '6');

-- ===== cargo details on the board
select testkit.assert_eq('containers: category', pg_temp.row(pg_temp.u('A'), 'JC-CONT')->'cargo'->>'category', 'freight');
select testkit.assert_eq('containers: description', pg_temp.row(pg_temp.u('A'), 'JC-CONT')->'cargo'->>'description', 'Standard 20ft containers with general goods.');
select testkit.assert_eq('containers: picture key', pg_temp.row(pg_temp.u('A'), 'JC-CONT')->'cargo'->>'image_key', 'containers');
select testkit.assert_eq('containers: not fragile', pg_temp.row(pg_temp.u('A'), 'JC-CONT')->'cargo'->>'fragile', 'false');
select testkit.assert_eq('electronics are fragile', pg_temp.row(pg_temp.u('A'), 'JC-ELEC')->'cargo'->>'fragile', 'true');
select testkit.assert_eq('diesel needs special handling', pg_temp.row(pg_temp.u('A'), 'JC-DSL')->'cargo'->>'special_handling', 'true');
select testkit.assert_eq('diesel category', pg_temp.row(pg_temp.u('A'), 'JC-DSL')->'cargo'->>'category', 'fuel_chemicals');
select testkit.assert_eq('unknown cargo falls back to freight, never invents flags', (pg_temp.row(pg_temp.u('A'), 'JC-UNK')->'cargo')::text, '{"type": "Cocoa", "fragile": false, "category": "freight", "image_key": "general", "weight_kg": 8000, "description": "General cargo.", "special_handling": false}');

-- ===== estimates
-- reference: the starter truck's burn rate straight from its definition
create temp table ref as select coalesce((d.stats->>'fuelBurnLPerKm')::numeric, 0.25) as burn
  from vehicle_ownership vo join vehicle_definitions d on d.id = vo.definition_id where vo.player_id = pg_temp.u('A') and d.category = 'truck' limit 1;
select testkit.assert_eq('estimate: fuel litres = distance x burn', pg_temp.row(pg_temp.u('A'), 'JC-CONT')->'estimate'->>'fuel_l', (select round(100 * burn, 1)::text from ref));
select testkit.assert_eq('estimate: fuel cost = ceil(distance x burn x price)', pg_temp.row(pg_temp.u('A'), 'JC-CONT')->'estimate'->>'fuel_cost', (select ceil(100 * burn * cfg('fuel_price_per_l'))::text from ref));
select testkit.assert_eq('estimate: profit = reward - fuel cost', pg_temp.row(pg_temp.u('A'), 'JC-CONT')->'estimate'->>'profit', (select (4500 - ceil(100 * burn * cfg('fuel_price_per_l')))::text from ref));
-- the price in the estimate is the price buy_fuel really charges
update vehicle_ownership set fuel_l = 10 where player_id = pg_temp.u('A');
select testkit.assert_eq('buy_fuel charges the configured price', pg_temp.j(pg_temp.u('A'), format('select buy_fuel(%L, 7)::text', (select id from vehicle_ownership where player_id = pg_temp.u('A') limit 1)))->>'cost', (7 * cfg('fuel_price_per_l'))::text);
select testkit.assert_eq('requirement: matching vehicle owned', pg_temp.row(pg_temp.u('A'), 'JC-CONT')->'requirements'->>'has_vehicle', 'true');
select testkit.assert_eq('requirement: no bus owned', pg_temp.row(pg_temp.u('A'), 'JC-BUS')->'requirements'->>'has_vehicle', 'false');
update vehicle_ownership set damage_pct = 100 where player_id = pg_temp.u('B');
select testkit.assert_eq('requirement: a wrecked vehicle does not count', pg_temp.row(pg_temp.u('B'), 'JC-CONT')->'requirements'->>'has_vehicle', 'false');
update vehicle_ownership set damage_pct = 0 where player_id = pg_temp.u('B');
select testkit.assert_eq('requirement: repaired vehicle counts again', pg_temp.row(pg_temp.u('B'), 'JC-CONT')->'requirements'->>'has_vehicle', 'true');
select testkit.assert_eq('estimate follows the viewer: no matching vehicle uses the default burn', pg_temp.row(pg_temp.u('A'), 'JC-BUS')->'estimate'->>'fuel_l', '2.5');

-- ===== filter + search (server side)
select testkit.assert_eq('no filter: every open job', pg_temp.codes(pg_temp.u('A'), '100, 0'), 'JC-BUS,JC-CEM,JC-CONT,JC-DSL,JC-ELEC,JC-UNK');
select testkit.assert_eq('filter freight (incl. unknown cargo)', pg_temp.codes(pg_temp.u('A'), $$100, 0, 'freight'$$), 'JC-BUS,JC-CONT,JC-UNK');
select testkit.assert_eq('filter construction', pg_temp.codes(pg_temp.u('A'), $$100, 0, 'construction'$$), 'JC-CEM');
select testkit.assert_eq('filter fuel_chemicals', pg_temp.codes(pg_temp.u('A'), $$100, 0, 'fuel_chemicals'$$), 'JC-DSL');
select testkit.assert_eq('filter manufacturing', pg_temp.codes(pg_temp.u('A'), $$100, 0, 'manufacturing'$$), 'JC-ELEC');
select testkit.assert_eq('filter with no matches is empty', pg_temp.codes(pg_temp.u('A'), $$100, 0, 'vehicles'$$), 'none');
select testkit.assert_eq('total reflects the filter', pg_temp.board(pg_temp.u('A'), $$100, 0, 'freight'$$)->>'total', '3');
select testkit.expect_error('authenticated', pg_temp.u('A'), $$select list_available_jobs(10, 0, 'bogus')$$, 'invalid_category');
select testkit.assert_eq('search by cargo (case-insensitive, partial)', pg_temp.codes(pg_temp.u('A'), $$100, 0, null, 'CEMENT'$$), 'JC-CEM');
select testkit.assert_eq('search by destination', pg_temp.codes(pg_temp.u('A'), $$100, 0, null, 'ikeja'$$), 'JC-CEM,JC-DSL');
select testkit.assert_eq('search by origin', pg_temp.codes(pg_temp.u('A'), $$100, 0, null, 'apapa'$$), 'JC-BUS,JC-CEM,JC-CONT,JC-DSL,JC-ELEC,JC-UNK');
select testkit.assert_eq('search by city', pg_temp.codes(pg_temp.u('A'), $$100, 0, null, 'lagos'$$), 'JC-BUS,JC-CEM,JC-CONT,JC-DSL,JC-ELEC,JC-UNK');
select testkit.assert_eq('search + filter combine', pg_temp.codes(pg_temp.u('A'), $$100, 0, 'freight', 'cocoa'$$), 'JC-UNK');
select testkit.assert_eq('blank search is no search', pg_temp.codes(pg_temp.u('A'), $$100, 0, null, '   '$$), 'JC-BUS,JC-CEM,JC-CONT,JC-DSL,JC-ELEC,JC-UNK');
select testkit.assert_eq('% is a literal, not a wildcard', pg_temp.codes(pg_temp.u('A'), $$100, 0, null, '%'$$), 'none');
select testkit.assert_eq('_ is a literal, not a wildcard', pg_temp.codes(pg_temp.u('A'), $$100, 0, null, 'c_ment'$$), 'none');
select testkit.assert_eq('search is data, not SQL', pg_temp.codes(pg_temp.u('A'), $$100, 0, null, '''; drop table jobs; --'$$), 'none');
select testkit.expect_error('authenticated', pg_temp.u('A'), format('select list_available_jobs(10, 0, null, %L)', repeat('x', 41)), 'invalid_search');
select testkit.assert_eq('pagination: second page', jsonb_array_length(pg_temp.board(pg_temp.u('A'), '4, 4')->'items')::text, '2');
select testkit.assert_eq('pagination: total ignores the page', pg_temp.board(pg_temp.u('A'), '4, 4')->>'total', '6');
select testkit.assert_eq('pagination: past the end is empty, not an error', jsonb_array_length(pg_temp.board(pg_temp.u('A'), '4, 40')->'items')::text, '0');
select testkit.assert_eq('pagination: limit is clamped to 100', pg_temp.board(pg_temp.u('A'), '100000, 0')->>'total', '6');
select testkit.assert_eq('accept for the held-job check', (testkit.as_user(pg_temp.u('A'), $$select accept_job((select id from jobs where code = 'JC-CEM'), (select id from vehicle_ownership where player_id = auth.uid() order by acquired_at limit 1))$$) is not null)::text, 'true');
select testkit.assert_eq('a job I hold is off my filtered board', pg_temp.codes(pg_temp.u('A'), $$100, 0, 'construction'$$), 'none');
select testkit.assert_eq('...and still on another player''s', pg_temp.codes(pg_temp.u('B'), $$100, 0, 'construction'$$), 'JC-CEM');
select testkit.expect_error('anon', null, 'select list_available_jobs(10, 0, null, null)', 'permission denied');
select testkit.expect_error('authenticated', null, 'select list_available_jobs(10, 0, null, null)', 'not_authenticated');

-- ===== the generator only uses catalogue cargo
update jobs set status = 'closed' where status = 'open';
select testkit.assert_eq('generator made jobs', (generate_jobs(40) > 0)::text, 'true');
select testkit.assert_eq('every generated cargo exists in the catalogue', (select count(*)::text from jobs where status = 'open' and cargo_type not in (select name from cargo_types)), '0');
select testkit.assert_eq('generated weights stay inside the catalogue range', (select count(*)::text from jobs j join cargo_types c on c.name = j.cargo_type where j.status = 'open' and j.cargo_weight_kg not between c.weight_min_kg and c.weight_max_kg), '0');

-- the page size is clamped by the server whatever the client asks for
select testkit.assert_eq('enough jobs to test the clamp', (generate_jobs(150) > 100)::text, 'true');
select testkit.assert_eq('page size is clamped to 100', jsonb_array_length(pg_temp.board(pg_temp.u('A'), '100000, 0')->'items')::text, '100');
select testkit.assert_eq('...while the total is the real count', ((pg_temp.board(pg_temp.u('A'), '100000, 0')->>'total')::int > 100)::text, 'true');

select 'JOB CATALOGUE TESTS PASSED' as result;
