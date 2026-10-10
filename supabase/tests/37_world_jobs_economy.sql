\set ON_ERROR_STOP on
-- Backend foundation tests, part 3: world state + route unlocks, vehicles, job board + lifecycle hooks, wallet ledger,
-- XP / progression / achievements, daily-challenge foundation.
create function pg_temp.j(p_uid uuid, p_sql text) returns jsonb language sql as $$ select testkit.as_user(p_uid, p_sql)::jsonb $$;
create temp table fx(k text primary key, v text);
insert into fx select k, testkit.make_user(n)::text from (values ('A','WjA'),('B','WjB'),('C','WjC'),('D','WjD')) t(k, n);
create function pg_temp.u(k text) returns uuid language sql as $$ select v::uuid from fx where fx.k = $1 $$;
create function pg_temp.veh(k text) returns uuid language sql as $$ select id from vehicle_ownership where player_id = pg_temp.u($1) order by acquired_at limit 1 $$;
create function pg_temp.notes(p_player uuid, p_type text) returns text language sql as $$
  select count(*)::text from notifications where player_id = p_player and type = p_type $$;
create function pg_temp.events(p_player uuid, p_name text) returns text language sql as $$
  select count(*)::text from analytics_events where player_id = p_player and name = p_name $$;
-- the job as the board shows it to one player
create function pg_temp.jobrow(p_uid uuid, p_code text) returns jsonb language sql as $$
  select e from jsonb_array_elements(testkit.as_user(p_uid, 'select list_available_jobs(100, 0)::text')::jsonb->'items') e where e->>'code' = p_code $$;
-- accept -> load -> drive Apapa -> Mile 12 legitimately -> deliver
create function pg_temp.deliver(p_uid uuid, p_veh uuid, p_code text, p_damage numeric default 0) returns uuid
language plpgsql as $$
declare v_asg uuid;
begin
  v_asg := testkit.as_user(p_uid, format($q$select accept_job((select id from jobs where code = %L), %L)$q$, p_code, p_veh))::uuid;
  perform testkit.as_user(p_uid, format('select start_job(%L, 0, 0)::text', v_asg));
  perform testkit.drive(p_uid, v_asg, 0, 0, 2600, -900);
  perform testkit.age(v_asg, 5);
  perform testkit.as_user(p_uid, format('select submit_telemetry(%L, 2600, -900)::text', v_asg));
  perform testkit.as_user(p_uid, format('select complete_job(%L, %s)::text', v_asg, p_damage));
  return v_asg;
end $$;

-- ===== WORLD STATE
insert into fx select 'world', testkit.as_user(pg_temp.u('A'), 'select get_world_state()::text');
select testkit.assert_eq('all 23 cities are served', jsonb_array_length((select v::jsonb->'cities' from fx where k = 'world'))::text, '23');
select testkit.assert_eq('all 12 routes are served', jsonb_array_length((select v::jsonb->'routes' from fx where k = 'world'))::text, '12');
select testkit.assert_eq('the corridor city is OPEN', (select e->>'effective_status' from jsonb_array_elements((select v::jsonb->'cities' from fx where k = 'world')) e where e->>'slug' = 'lagos'), 'OPEN');
select testkit.assert_eq('a planned city is PLANNED', (select e->>'effective_status' from jsonb_array_elements((select v::jsonb->'cities' from fx where k = 'world')) e where e->>'slug' = 'abuja'), 'PLANNED');
select testkit.assert_eq('the playable route is OPEN', (select e->>'effective_status' from jsonb_array_elements((select v::jsonb->'routes' from fx where k = 'world')) e where e->>'code' = 'lagos-ibadan'), 'OPEN');
select testkit.assert_eq('surveyed road length is labelled road', (select e->>'distance_source' from jsonb_array_elements((select v::jsonb->'routes' from fx where k = 'world')) e where e->>'code' = 'lagos-ibadan'), 'road');
select testkit.assert_eq('estimated time = km / 50 km/h', (select e->>'est_minutes' from jsonb_array_elements((select v::jsonb->'routes' from fx where k = 'world')) e where e->>'code' = 'lagos-ibadan'), '154');
select testkit.assert_eq('unsurveyed distances are labelled estimated', (select e->>'distance_source' from jsonb_array_elements((select v::jsonb->'routes' from fx where k = 'world')) e where e->>'code' = 'lagos-benin-city'), 'estimated');
select testkit.assert_eq('planned routes stay planned', (select e->>'effective_status' from jsonb_array_elements((select v::jsonb->'routes' from fx where k = 'world')) e where e->>'code' = 'lagos-benin-city'), 'PLANNED');
select testkit.assert_eq('the route spec link is carried', (select e->>'game_route_id' from jsonb_array_elements((select v::jsonb->'routes' from fx where k = 'world')) e where e->>'code' = 'lagos-ibadan'), 'ng-lagos-ibadan');
select testkit.assert_eq('every route endpoint is a known city', (select count(*)::text from world_routes r where not exists (select 1 from cities where slug = r.via[1]) or not exists (select 1 from cities where slug = r.via[array_length(r.via, 1)])), '0');
select testkit.assert_eq('route endpoints match via', (select count(*)::text from world_routes r join cities a on a.id = r.from_city_id join cities b on b.id = r.to_city_id where a.slug <> r.via[1] or b.slug <> r.via[array_length(r.via, 1)]), '0');
-- level gating is decided here, not in the client: an OPEN route above the player's level is LOCKED for them
select admin_set_route('abuja-kaduna', 'OPEN', 5);
select testkit.assert_eq('OPEN above my level is LOCKED', (select e->>'effective_status' from jsonb_array_elements(testkit.as_user(pg_temp.u('A'), 'select get_world_state()::text')::jsonb->'routes') e where e->>'code' = 'abuja-kaduna'), 'LOCKED');
select testkit.assert_eq('with the reason', (select e->>'locked_reason' from jsonb_array_elements(testkit.as_user(pg_temp.u('A'), 'select get_world_state()::text')::jsonb->'routes') e where e->>'code' = 'abuja-kaduna'), 'level');
update profiles set experience = 1600, level = 5 where id = pg_temp.u('B');
select testkit.assert_eq('at the required level it opens', (select e->>'effective_status' from jsonb_array_elements(testkit.as_user(pg_temp.u('B'), 'select get_world_state()::text')::jsonb->'routes') e where e->>'code' = 'abuja-kaduna'), 'OPEN');
select testkit.assert_eq('reaching the level announced the unlock', (select count(*)::text from notifications where player_id = pg_temp.u('B') and metadata->>'route_code' = 'abuja-kaduna'), '1');
select testkit.assert_eq('and tracked route_unlocked', pg_temp.events(pg_temp.u('B'), 'route_unlocked'), '1');
select testkit.assert_eq('and level_up', pg_temp.notes(pg_temp.u('B'), 'level_up'), '1');
select admin_set_route('abuja-kaduna', 'PLANNED', 7);
select testkit.expect_error('authenticated', pg_temp.u('A'), $$select admin_set_route('lagos-ibadan', 'LOCKED')$$, 'permission denied');
select testkit.expect_error('authenticated', pg_temp.u('A'), 'select * from world_routes', 'permission denied');
select testkit.expect_error('authenticated', pg_temp.u('A'), $$update cities set status = 'OPEN'$$, 'permission denied');
select testkit.expect_error('authenticated', pg_temp.u('A'), $$update world_routes set status = 'OPEN'$$, 'permission denied');
select testkit.expect_error('authenticated', null, 'select get_world_state()', 'not_authenticated');
select testkit.expect_error('anon', null, 'select get_world_state()', 'permission denied');

-- ===== JOB BOARD: shape, requirements, route unlocks
select testkit.mk_job('WJ-1', 1, 500);
select testkit.mk_job('WJ-LVL', 1, 500);
update jobs set min_level = 3 where code = 'WJ-LVL';
select testkit.mk_job('WJ-BUS', 1, 500, 'bus');
-- Lagos -> Ibadan (a different city): needs an open route
insert into jobs(code, origin_id, destination_id, cargo_type, cargo_weight_kg, distance_km, difficulty, reward, xp_reward, required_category, max_participants)
  select 'WJ-IBA', o.id, d.id, 'Cocoa', 2000, 128, 3, 9000, 300, 'truck', 4 from locations o, locations d where o.slug = 'lagos-apapa-port' and d.slug = 'ibadan-ojoo-depot';
select testkit.assert_eq('board reports est minutes from the server', pg_temp.jobrow(pg_temp.u('A'), 'WJ-1')->>'est_minutes', '4');
select testkit.assert_eq('board carries origin city', pg_temp.jobrow(pg_temp.u('A'), 'WJ-1')->'origin'->>'city', 'Lagos');
select testkit.assert_eq('board carries destination name', pg_temp.jobrow(pg_temp.u('A'), 'WJ-1')->'destination'->>'name', 'Mile 12 Market');
select testkit.assert_eq('board carries cargo', pg_temp.jobrow(pg_temp.u('A'), 'WJ-1')->'cargo'->>'type', 'Test');
select testkit.assert_eq('board carries the authoritative reward', pg_temp.jobrow(pg_temp.u('A'), 'WJ-1')->>'reward', '500');
select testkit.assert_eq('board carries requirements', pg_temp.jobrow(pg_temp.u('A'), 'WJ-1')->'requirements'->>'category', 'truck');
select testkit.assert_eq('eligible job', pg_temp.jobrow(pg_temp.u('A'), 'WJ-1')->>'eligible', 'true');
select testkit.assert_eq('level requirement is reported', pg_temp.jobrow(pg_temp.u('A'), 'WJ-LVL')->>'blocked_reason', 'level_too_low');
select testkit.assert_eq('vehicle requirement is reported', pg_temp.jobrow(pg_temp.u('A'), 'WJ-BUS')->>'blocked_reason', 'no_matching_vehicle');
select testkit.assert_eq('open route is not a blocker', pg_temp.jobrow(pg_temp.u('A'), 'WJ-IBA')->>'eligible', 'true');
select testkit.expect_error('authenticated', pg_temp.u('A'), $$select accept_job((select id from jobs where code = 'WJ-LVL'), (select id from vehicle_ownership where player_id = auth.uid()))$$, 'level_too_low');
select testkit.assert_eq('a rejected accept leaves no assignment', (select count(*)::text from job_assignments where player_id = pg_temp.u('A')), '0');
select testkit.expect_error('authenticated', pg_temp.u('A'), 'select get_job_details(gen_random_uuid())', 'job_not_found');
select testkit.assert_eq('details match the board', testkit.as_user(pg_temp.u('A'), $$select (get_job_details((select id from jobs where code = 'WJ-1')))->>'code'$$), 'WJ-1');
-- route state is enforced on accept
select admin_set_route('lagos-ibadan', 'LOCKED');
select testkit.assert_eq('closed route blocks the job on the board', pg_temp.jobrow(pg_temp.u('A'), 'WJ-IBA')->>'blocked_reason', 'route_locked');
select testkit.expect_error('authenticated', pg_temp.u('A'), $$select accept_job((select id from jobs where code = 'WJ-IBA'), (select id from vehicle_ownership where player_id = auth.uid()))$$, 'route_locked');
select admin_set_route('lagos-ibadan', 'PLANNED');
select testkit.expect_error('authenticated', pg_temp.u('A'), $$select accept_job((select id from jobs where code = 'WJ-IBA'), (select id from vehicle_ownership where player_id = auth.uid()))$$, 'route_unavailable');
select admin_set_route('lagos-ibadan', 'OPEN', 4);
select testkit.expect_error('authenticated', pg_temp.u('A'), $$select accept_job((select id from jobs where code = 'WJ-IBA'), (select id from vehicle_ownership where player_id = auth.uid()))$$, 'route_locked');
select admin_set_route('lagos-ibadan', 'OPEN', 1);
insert into fx select 'iba', testkit.as_user(pg_temp.u('A'), $$select accept_job((select id from jobs where code = 'WJ-IBA'), (select id from vehicle_ownership where player_id = auth.uid()))$$);
select testkit.assert_eq('open route accepts', (select status::text from job_assignments where id = (select v::uuid from fx where k = 'iba')), 'accepted');
select testkit.assert_eq('analytics: job_accepted', pg_temp.events(pg_temp.u('A'), 'job_accepted'), '1');
select testkit.assert_eq('the assignment records the live session when there is one', (select (session_id is null)::text from job_assignments where id = (select v::uuid from fx where k = 'iba')), 'true');
-- active job API
select testkit.assert_eq('active job: assignment', pg_temp.j(pg_temp.u('A'), 'select get_my_active_job()::text')->'active'->>'status', 'accepted');
select testkit.assert_eq('active job: job code', pg_temp.j(pg_temp.u('A'), 'select get_my_active_job()::text')->'active'->'job'->>'code', 'WJ-IBA');
select testkit.assert_eq('active job: nothing verified yet', pg_temp.j(pg_temp.u('A'), 'select get_my_active_job()::text')->'active'->>'verified_km', '0');
select testkit.assert_eq('other players have no active job', (pg_temp.j(pg_temp.u('B'), 'select get_my_active_job()::text')->'active')::text, 'null');
select testkit.assert_eq('my active job is off my board', (select count(*)::text from jsonb_array_elements(testkit.as_user(pg_temp.u('A'), 'select list_available_jobs(100,0)::text')::jsonb->'items') e where e->>'code' = 'WJ-IBA'), '0');
select testkit.assert_eq('while busy, others are blocked', pg_temp.jobrow(pg_temp.u('A'), 'WJ-1')->>'blocked_reason', 'activity_in_progress');
select testkit.assert_eq('abandon', testkit.as_user(pg_temp.u('A'), format('select abandon_job(%L)::text', (select v from fx where k = 'iba'))), '');
select testkit.assert_eq('analytics: job_abandoned', pg_temp.events(pg_temp.u('A'), 'job_abandoned'), '1');
select testkit.assert_eq('notification: job event', pg_temp.notes(pg_temp.u('A'), 'job_event'), '1');
select testkit.expect_error('authenticated', pg_temp.u('B'), format('select abandon_job(%L)', (select v from fx where k = 'iba')), 'assignment_not_found');
select testkit.assert_eq('listing pagination: limit applies', jsonb_array_length(pg_temp.j(pg_temp.u('C'), 'select list_available_jobs(1, 0)::text')->'items')::text, '1');
select testkit.expect_error('anon', null, 'select list_available_jobs()', 'permission denied');
select testkit.expect_error('authenticated', null, 'select get_my_active_job()', 'not_authenticated');

-- ===== JOB COMPLETION: server-side payout, ledger, XP, stats, mileage, notifications, analytics, achievements, replay
update jobs set min_level = 1, reward = 500 where code = 'WJ-LVL';
select admin_set_route('lagos-benin-city', 'OPEN', 2);                       -- unlocks when C reaches level 2
update profiles set experience = 95 where id = pg_temp.u('C');
select testkit.mk_job('WJ-PAY', 1, 700);
insert into fx select 'pay', pg_temp.deliver(pg_temp.u('C'), pg_temp.veh('C'), 'WJ-PAY', 1)::text;
select testkit.assert_eq('reward = the job row, not a client number', (select amount::text from transactions where player_id = pg_temp.u('C') and kind = 'JOB_REWARD'), '700');
select testkit.assert_eq('wallet = starter grant + reward', testkit.as_user(pg_temp.u('C'), 'select balance from player_wallets'), '5700');
select testkit.assert_eq('ledger row carries a reason', (select reason from transactions where player_id = pg_temp.u('C') and kind = 'JOB_REWARD'), 'job_reward');
select testkit.assert_eq('ledger row references the assignment', (select ref_id::text from transactions where player_id = pg_temp.u('C') and kind = 'JOB_REWARD'), (select v from fx where k = 'pay'));
select testkit.assert_eq('xp awarded by the server (95 + 40)', (select experience::text from profiles where id = pg_temp.u('C')), '135');
select testkit.assert_eq('level recomputed from xp', (select level::text from profiles where id = pg_temp.u('C')), '2');
select testkit.assert_eq('jobs_completed +1', (select jobs_completed::text from profiles where id = pg_temp.u('C')), '1');
select testkit.assert_eq('lifetime earnings follow the ledger', (select lifetime_earnings::text from profiles where id = pg_temp.u('C')), '700');
select testkit.assert_eq('vehicle mileage updated from verified distance', (select (odometer_km between 2.5 and 3.6)::text from vehicle_ownership where player_id = pg_temp.u('C')), 'true');
select testkit.assert_eq('payout notification', pg_temp.notes(pg_temp.u('C'), 'payout'), '1');
select testkit.assert_eq('payout carries the amount', (select (metadata->>'amount') from notifications where player_id = pg_temp.u('C') and type = 'payout'), '700');
select testkit.assert_eq('level-up notification', pg_temp.notes(pg_temp.u('C'), 'level_up'), '1');
select testkit.assert_eq('route unlock announced', (select count(*)::text from notifications where player_id = pg_temp.u('C') and metadata->>'route_code' = 'lagos-benin-city'), '1');
select testkit.assert_eq('first-delivery achievement', (select count(*)::text from player_achievements where player_id = pg_temp.u('C') and achievement_id = 'first_delivery'), '1');
select testkit.assert_eq('achievement notification', pg_temp.notes(pg_temp.u('C'), 'achievement'), '1');
select testkit.assert_eq('analytics: job_completed', pg_temp.events(pg_temp.u('C'), 'job_completed'), '1');
select testkit.assert_eq('analytics: level_up', pg_temp.events(pg_temp.u('C'), 'level_up'), '1');
select testkit.assert_eq('analytics: achievement_unlocked', pg_temp.events(pg_temp.u('C'), 'achievement_unlocked'), '1');
-- duplicate completion: nothing is paid or announced twice
select testkit.expect_error('authenticated', pg_temp.u('C'), format('select complete_job(%L, 1)', (select v from fx where k = 'pay')), 'already_completed');
select testkit.assert_eq('replay paid nothing', (select count(*)::text from transactions where player_id = pg_temp.u('C') and kind = 'JOB_REWARD'), '1');
select testkit.assert_eq('replay announced nothing', pg_temp.notes(pg_temp.u('C'), 'payout'), '1');
select testkit.assert_eq('replay did not re-award xp', (select experience::text from profiles where id = pg_temp.u('C')), '135');
select testkit.assert_eq('profile API: jobs_total counts it', pg_temp.j(pg_temp.u('C'), 'select get_my_profile()::text')->>'jobs_total', '1');
select testkit.assert_eq('profile API: coins', pg_temp.j(pg_temp.u('C'), 'select get_my_profile()::text')->>'coins', '5700');
select testkit.assert_eq('the finished job is gone from the board', (select count(*)::text from jsonb_array_elements(testkit.as_user(pg_temp.u('C'), 'select list_available_jobs(100,0)::text')::jsonb->'items') e where e->>'code' = 'WJ-PAY'), '0');
select admin_set_route('lagos-benin-city', 'PLANNED', 3);

-- ===== VEHICLES
select testkit.assert_eq('one starter vehicle', jsonb_array_length(pg_temp.j(pg_temp.u('A'), 'select list_my_vehicles()::text')->'items')::text, '1');
select testkit.assert_eq('vehicle: status ready', pg_temp.j(pg_temp.u('A'), 'select list_my_vehicles()::text')->'items'->0->>'status', 'ready');
select testkit.assert_eq('vehicle: top speed from the definition', pg_temp.j(pg_temp.u('A'), 'select list_my_vehicles()::text')->'items'->0->>'top_speed_kmh', '110');
select testkit.assert_eq('vehicle: condition', pg_temp.j(pg_temp.u('A'), 'select list_my_vehicles()::text')->'items'->0->>'condition_pct', '100.0');
select testkit.assert_eq('vehicle: fuel percent', pg_temp.j(pg_temp.u('A'), 'select list_my_vehicles()::text')->'items'->0->>'fuel_pct', '100.0');
select testkit.assert_eq('vehicle: cargo capacity', pg_temp.j(pg_temp.u('A'), 'select list_my_vehicles()::text')->'items'->0->>'cargo_capacity_kg', '4000');
select testkit.assert_eq('vehicle: selected by default', pg_temp.j(pg_temp.u('A'), 'select list_my_vehicles()::text')->'items'->0->>'selected', 'true');
update vehicle_ownership set fuel_l = 6 where player_id = pg_temp.u('A');
select testkit.assert_eq('vehicle: low fuel (< 10 %)', pg_temp.j(pg_temp.u('A'), 'select list_my_vehicles()::text')->'items'->0->>'status', 'low_fuel');
update vehicle_ownership set damage_pct = 75 where player_id = pg_temp.u('A');
select testkit.assert_eq('vehicle: needs repair beats low fuel', pg_temp.j(pg_temp.u('A'), 'select list_my_vehicles()::text')->'items'->0->>'status', 'needs_repair');
update vehicle_ownership set damage_pct = 0, fuel_l = 120 where player_id = pg_temp.u('A');
-- ownership
select testkit.expect_error('authenticated', pg_temp.u('A'), format('select rename_vehicle(%L, ''Mine now'')', pg_temp.veh('B')), 'vehicle_not_owned');
select testkit.expect_error('authenticated', pg_temp.u('A'), format('select set_active_vehicle(%L)', pg_temp.veh('B')), 'vehicle_not_owned');
select testkit.expect_error('authenticated', pg_temp.u('A'), format($$select set_vehicle_customization(%L, '{"color":"#ff0000"}')$$, pg_temp.veh('B')), 'vehicle_not_owned');
select testkit.assert_eq('B untouched', (select coalesce(nickname, 'none') from vehicle_ownership where player_id = pg_temp.u('B')), 'none');
select testkit.assert_eq('rename own', testkit.as_user(pg_temp.u('A'), format('select rename_vehicle(%L, ''Old Faithful'')::text', pg_temp.veh('A'))), '');
select testkit.assert_eq('nickname shows in the API', pg_temp.j(pg_temp.u('A'), 'select list_my_vehicles()::text')->'items'->0->>'name', 'Old Faithful');
select testkit.expect_error('authenticated', pg_temp.u('A'), format('select rename_vehicle(%L, ''<script>'')', pg_temp.veh('A')), 'invalid_vehicle_name');
select testkit.assert_eq('customise own', testkit.as_user(pg_temp.u('A'), format($$select set_vehicle_customization(%L, '{"color":"#c8a24a","livery":"sunset","plate":"ARO 1"}')::text$$, pg_temp.veh('A'))), '');
select testkit.assert_eq('customisation persisted', pg_temp.j(pg_temp.u('A'), 'select list_my_vehicles()::text')->'items'->0->'customization'->>'color', '#c8a24a');
select testkit.expect_error('authenticated', pg_temp.u('A'), format($$select set_vehicle_customization(%L, '{"fuel_l":"9999"}')$$, pg_temp.veh('A')), 'invalid_customization');
select testkit.expect_error('authenticated', pg_temp.u('A'), format($$select set_vehicle_customization(%L, '{"fuel_l":9999}')$$, pg_temp.veh('A')), 'invalid_customization');
select testkit.expect_error('authenticated', pg_temp.u('A'), 'update vehicle_ownership set nickname = ''Hacked''', 'permission denied');
select testkit.expect_error('authenticated', pg_temp.u('A'), $$update vehicle_ownership set customization = '{"color":"#000000"}'$$, 'permission denied');
select testkit.expect_error('authenticated', pg_temp.u('A'), format($$select set_vehicle_customization(%L, '{"color":"red"}')$$, pg_temp.veh('A')), 'invalid_customization');
select testkit.expect_error('authenticated', pg_temp.u('A'), format($$select set_vehicle_customization(%L, '{"color":5}')$$, pg_temp.veh('A')), 'invalid_customization');
select testkit.expect_error('authenticated', pg_temp.u('A'), format($$select set_vehicle_customization(%L, '[]')$$, pg_temp.veh('A')), 'invalid_customization');
select testkit.assert_eq('stats are untouched by customisation', (select fuel_l::text from vehicle_ownership where player_id = pg_temp.u('A')), '120');
-- catalogue, unlock levels, purchase
select testkit.assert_eq('catalogue lists every definition', jsonb_array_length(pg_temp.j(pg_temp.u('D'), 'select list_vehicle_catalog()::text')->'items')::text, (select count(*)::text from vehicle_definitions));
select testkit.assert_eq('cannot afford a 60,000 coin truck', (select e->>'can_afford' from jsonb_array_elements(testkit.as_user(pg_temp.u('D'), 'select list_vehicle_catalog()::text')::jsonb->'items') e where e->>'definition_id' = 'truck_medium_01'), 'false');
select testkit.assert_eq('the starter truck is not for sale', (select e->>'purchasable' from jsonb_array_elements(testkit.as_user(pg_temp.u('D'), 'select list_vehicle_catalog()::text')::jsonb->'items') e where e->>'definition_id' = 'truck_light_01'), 'false');
select testkit.assert_eq('starter is owned', (select e->>'owned' from jsonb_array_elements(testkit.as_user(pg_temp.u('D'), 'select list_vehicle_catalog()::text')::jsonb->'items') e where e->>'definition_id' = 'truck_light_01'), '1');
select admin_adjust_wallet(pg_temp.u('D'), 100000, 'test funding', 'wj-fund-d');
update vehicle_definitions set unlock_level = 5 where id = 'truck_medium_01';
select testkit.assert_eq('locked on the catalogue', (select e->>'unlocked' from jsonb_array_elements(testkit.as_user(pg_temp.u('D'), 'select list_vehicle_catalog()::text')::jsonb->'items') e where e->>'definition_id' = 'truck_medium_01'), 'false');
select testkit.expect_error('authenticated', pg_temp.u('D'), $$select buy_vehicle('truck_medium_01')$$, 'vehicle_locked');
select testkit.assert_eq('a refused purchase costs nothing (atomic)', testkit.as_user(pg_temp.u('D'), 'select balance from player_wallets'), '105000');
select testkit.assert_eq('and grants nothing', (select count(*)::text from vehicle_ownership where player_id = pg_temp.u('D')), '1');
update vehicle_definitions set unlock_level = 1 where id = 'truck_medium_01';
select testkit.assert_eq('unlocked purchase works', (select (testkit.as_user(pg_temp.u('D'), $$select buy_vehicle('truck_medium_01')$$) is not null)::text), 'true');
select testkit.assert_eq('debited exactly the price', testkit.as_user(pg_temp.u('D'), 'select balance from player_wallets'), '45000');
select testkit.assert_eq('analytics: vehicle_purchased', pg_temp.events(pg_temp.u('D'), 'vehicle_purchased'), '1');
select testkit.assert_eq('two vehicles now', jsonb_array_length(pg_temp.j(pg_temp.u('D'), 'select list_my_vehicles()::text')->'items')::text, '2');
insert into fx select 'dmed', (select id::text from vehicle_ownership where player_id = pg_temp.u('D') and definition_id = 'truck_medium_01');
select testkit.assert_eq('select the new vehicle', testkit.as_user(pg_temp.u('D'), format('select set_active_vehicle(%L)::text', (select v from fx where k = 'dmed'))), '');
select testkit.assert_eq('selection is served', pg_temp.j(pg_temp.u('D'), 'select list_my_vehicles()::text')->>'selected_id', (select v from fx where k = 'dmed'));
select testkit.assert_eq('dashboard summary shows the selected vehicle', pg_temp.j(pg_temp.u('D'), 'select get_dashboard_summary()::text')->'vehicle'->>'definition_id', 'truck_medium_01');

-- ===== WALLET / LEDGER
select testkit.assert_eq('wallet endpoint', pg_temp.j(pg_temp.u('B'), 'select get_my_wallet()::text')->>'balance', '5000');
select testkit.assert_eq('ledger endpoint lists own rows only', (select count(*)::text from jsonb_array_elements(testkit.as_user(pg_temp.u('B'), 'select list_my_transactions()::text')::jsonb->'items') e), '1');
select testkit.assert_eq('ledger row reason (starter grant)', pg_temp.j(pg_temp.u('B'), 'select list_my_transactions()::text')->'items'->0->>'reason', 'starter_grant');
-- bonuses / adjustments: immutable, reasoned, idempotent
select testkit.assert_eq('adjustment applies', (select admin_adjust_wallet(pg_temp.u('B'), 250, 'goodwill bonus for outage', 'wj-b-1')::text), 'true');
select testkit.assert_eq('replay of the same key is a no-op', (select admin_adjust_wallet(pg_temp.u('B'), 250, 'goodwill bonus for outage', 'wj-b-1')::text), 'false');
select testkit.assert_eq('balance moved once', testkit.as_user(pg_temp.u('B'), 'select balance from player_wallets'), '5250');
select testkit.assert_eq('reason is auditable', (select reason from transactions where idempotency_key = 'adj:wj-b-1'), 'goodwill bonus for outage');
select testkit.assert_eq('kind is ADJUSTMENT', (select kind::text from transactions where idempotency_key = 'adj:wj-b-1'), 'ADJUSTMENT');
select testkit.assert_eq('reason does not leak into the next row', (select reason from (select reason from transactions where player_id = pg_temp.u('B') order by id desc offset 0 limit 1) q), 'goodwill bonus for outage');
select testkit.assert_eq('adjustments are not earnings', (select lifetime_earnings::text from profiles where id = pg_temp.u('B')), '0');
select testkit.assert_eq('balance_after is recorded', (select balance_after::text from transactions where idempotency_key = 'adj:wj-b-1'), '5250');
select testkit.expect_error('postgres', null, $$select admin_adjust_wallet((select id from profiles limit 1), 5, '', 'k-empty')$$, 'reason_required');
select testkit.expect_error('postgres', null, $$select admin_adjust_wallet((select id from profiles limit 1), 0, 'because', 'k-zero')$$, 'invalid_amount');
select testkit.expect_error('postgres', null, $$select admin_adjust_wallet((select id from profiles limit 1), 5, 'because', '')$$, 'key_required');
select testkit.expect_error('postgres', null, format('select admin_adjust_wallet(%L, -999999, ''clawback'', ''wj-b-neg'')', pg_temp.u('B')), 'insufficient_funds');
select testkit.assert_eq('failed debit changed nothing', testkit.as_user(pg_temp.u('B'), 'select balance from player_wallets'), '5250');
select testkit.assert_eq('a debit adjustment works', (select admin_adjust_wallet(pg_temp.u('B'), -250, 'revert test bonus', 'wj-b-2')::text), 'true');
-- refunds
update vehicle_ownership set fuel_l = 50 where player_id = pg_temp.u('B');
select testkit.as_user(pg_temp.u('B'), format('select buy_fuel(%L, 10)::text', pg_temp.veh('B')));
select testkit.assert_eq('fuel purchase debited 20', testkit.as_user(pg_temp.u('B'), 'select balance from player_wallets'), '4980');
insert into fx select 'fuel_tx', (select id::text from transactions where player_id = pg_temp.u('B') and kind = 'FUEL_PURCHASE');
select testkit.assert_eq('refund restores the coins', (select admin_refund_transaction((select v::bigint from fx where k = 'fuel_tx'), 'wrong pump')::text), 'true');
select testkit.assert_eq('refund is idempotent', (select admin_refund_transaction((select v::bigint from fx where k = 'fuel_tx'), 'wrong pump')::text), 'false');
select testkit.assert_eq('balance back', testkit.as_user(pg_temp.u('B'), 'select balance from player_wallets'), '5000');
select testkit.assert_eq('refund ledger row', (select kind::text from transactions where idempotency_key = 'refund:' || (select v from fx where k = 'fuel_tx')), 'REFUND');
select testkit.assert_eq('refund reason names the original', (select (reason like 'refund of transaction %: wrong pump')::text from transactions where idempotency_key = 'refund:' || (select v from fx where k = 'fuel_tx')), 'true');
select testkit.expect_error('postgres', null, format('select admin_refund_transaction(%L, ''x y z'')', (select id from transactions where idempotency_key = 'adj:wj-b-1')), 'not_refundable');
select testkit.expect_error('postgres', null, $$select admin_refund_transaction(-1, 'x y z')$$, 'transaction_not_found');
select testkit.assert_eq('the ledger is append-only: rows are never rewritten', (select count(*)::text from transactions where player_id = pg_temp.u('B')), '5');
-- pagination
select testkit.assert_eq('ledger pagination: limit', jsonb_array_length(pg_temp.j(pg_temp.u('B'), 'select list_my_transactions(2)::text')->'items')::text, '2');
insert into fx select 'oldest_in_page', (pg_temp.j(pg_temp.u('B'), 'select list_my_transactions(2)::text')->'items'->1->>'id');
select testkit.assert_eq('ledger pagination: before_id', jsonb_array_length(pg_temp.j(pg_temp.u('B'), format('select list_my_transactions(10, %s)::text', (select v from fx where k = 'oldest_in_page')))->'items')::text, '3');
-- clients cannot touch the ledger or wallet, nor call the admin tools
select testkit.expect_error('authenticated', pg_temp.u('B'), $$select admin_adjust_wallet(auth.uid(), 1000000, 'self service', 'x-self')$$, 'permission denied');
select testkit.expect_error('authenticated', pg_temp.u('B'), 'select admin_refund_transaction(1, ''abc'')', 'permission denied');
select testkit.expect_error('authenticated', pg_temp.u('B'), 'select admin_broadcast(''a'', ''b'', ''kkk'')', 'permission denied');
select testkit.expect_error('authenticated', pg_temp.u('B'), 'update transactions set reason = ''x''', 'permission denied');
select testkit.expect_error('authenticated', pg_temp.u('B'), 'update player_wallets set balance = 1', 'permission denied');
select testkit.assert_eq('every player has exactly one balance row', (select count(*)::text from (select player_id from player_wallets group by player_id having count(*) > 1) q), '0');
select testkit.assert_eq('wallet = sum of the ledger for every player in this suite', (select count(*)::text from player_wallets w where w.player_id::text in (select v from fx) and w.balance <> coalesce((select sum(amount) from transactions t where t.player_id = w.player_id), 0)), '0');

-- ===== XP / PROGRESSION / ACHIEVEMENTS
select testkit.assert_eq('level_for_xp(0)', (select level_for_xp(0)::text), '1');
select testkit.assert_eq('level_for_xp(99)', (select level_for_xp(99)::text), '1');
select testkit.assert_eq('level_for_xp(100)', (select level_for_xp(100)::text), '2');
select testkit.assert_eq('level_for_xp(399)', (select level_for_xp(399)::text), '2');
select testkit.assert_eq('level_for_xp(400)', (select level_for_xp(400)::text), '3');
select testkit.assert_eq('xp_for_level is the inverse', (select (level_for_xp(xp_for_level(7)::bigint) = 7 and level_for_xp(xp_for_level(7) - 1) = 6)::text), 'true');
select testkit.assert_eq('xp_for_level(3)', (select xp_for_level(3)::text), '400');
select testkit.assert_eq('progression: level', pg_temp.j(pg_temp.u('A'), 'select get_my_progression()::text')->>'level', '1');
select testkit.assert_eq('progression: xp into level', pg_temp.j(pg_temp.u('A'), 'select get_my_progression()::text')->>'xp_into_level', '0');
select testkit.assert_eq('progression: xp to next', pg_temp.j(pg_temp.u('A'), 'select get_my_progression()::text')->>'xp_to_next', '100');
select testkit.assert_eq('progression: first milestone is 50 km', pg_temp.j(pg_temp.u('A'), 'select get_my_progression()::text')->'next_milestone'->>'target', '50');
select testkit.assert_eq('progression: milestone title', pg_temp.j(pg_temp.u('A'), 'select get_my_progression()::text')->'next_milestone'->>'title', 'Drive 50 km');
select testkit.assert_eq('progression for C after the delivery: xp into level', pg_temp.j(pg_temp.u('C'), 'select get_my_progression()::text')->>'xp_into_level', '35');
select testkit.assert_eq('progression for C: next level at 400', pg_temp.j(pg_temp.u('C'), 'select get_my_progression()::text')->>'xp_next_level', '400');
-- distance drives the milestone ladder; unlocks are server-side
update profiles set distance_km = 120 where id = pg_temp.u('A');
select testkit.assert_eq('50 km and 100 km unlocked', (select count(*)::text from player_achievements where player_id = pg_temp.u('A') and achievement_id in ('distance_50','distance_100')), '2');
select testkit.assert_eq('250 km not yet', (select count(*)::text from player_achievements where player_id = pg_temp.u('A') and achievement_id = 'distance_250'), '0');
select testkit.assert_eq('next milestone moves to 250 km (from real distance)', pg_temp.j(pg_temp.u('A'), 'select get_my_progression()::text')->'next_milestone'->>'target', '250');
select testkit.assert_eq('milestone fraction', pg_temp.j(pg_temp.u('A'), 'select get_my_progression()::text')->'next_milestone'->>'fraction', '0.4800');
select testkit.assert_eq('achievement notifications', pg_temp.notes(pg_temp.u('A'), 'achievement'), '2');
update profiles set distance_km = 120.5 where id = pg_temp.u('A');
select testkit.assert_eq('unlocks are idempotent', pg_temp.notes(pg_temp.u('A'), 'achievement'), '2');
select testkit.assert_eq('achievement list: unlocked have a timestamp', (select count(*)::text from jsonb_array_elements(testkit.as_user(pg_temp.u('A'), 'select list_my_achievements()::text')::jsonb->'items') e where e->'unlocked_at' <> 'null'::jsonb), '2');
select testkit.assert_eq('achievement list: progress is capped at the threshold', (select e->>'current' from jsonb_array_elements(testkit.as_user(pg_temp.u('A'), 'select list_my_achievements()::text')::jsonb->'items') e where e->>'id' = 'distance_50'), '50');
select testkit.assert_eq('past the ladder the next goal is the next 25,000 km', (select _progression_json(pg_temp.u('A')) is not null)::text, 'true');
update profiles set distance_km = 26000 where id = pg_temp.u('D');
select testkit.assert_eq('beyond the last rung: 50,000', pg_temp.j(pg_temp.u('D'), 'select get_my_progression()::text')->'next_milestone'->>'target', '50000');
select testkit.expect_error('authenticated', pg_temp.u('A'), 'select * from player_achievements', 'permission denied');
select testkit.expect_error('authenticated', pg_temp.u('A'), $$insert into player_achievements(player_id, achievement_id) values (auth.uid(), 'level_25')$$, 'permission denied');
select testkit.expect_error('authenticated', pg_temp.u('A'), 'update achievement_definitions set threshold = 1', 'permission denied');

-- ===== DAILY CHALLENGES (foundation; UI says "coming soon")
select testkit.assert_eq('no active definitions => none available (nothing is fabricated)', pg_temp.j(pg_temp.u('A'), 'select get_my_daily_challenges()::text')->>'available', 'false');
select testkit.assert_eq('and none assigned', jsonb_array_length(pg_temp.j(pg_temp.u('A'), 'select get_my_daily_challenges()::text')->'items')::text, '0');
insert into challenge_definitions(id, title, description, metric, target, reward_coins, reward_xp, active)
  values ('deliver_one', 'Make a delivery', 'Complete 1 job today.', 'jobs_completed', 1, 400, 30, true);
select testkit.assert_eq('an active definition is assigned for today', jsonb_array_length(pg_temp.j(pg_temp.u('A'), 'select get_my_daily_challenges()::text')->'items')::text, '1');
select testkit.assert_eq('assignment is idempotent per day', jsonb_array_length(pg_temp.j(pg_temp.u('A'), 'select get_my_daily_challenges()::text')->'items')::text, '1');
select testkit.assert_eq('expires at the next UTC midnight', (select (expires_at = (((now() at time zone 'utc')::date + 1)::timestamp at time zone 'utc'))::text from player_challenges where player_id = pg_temp.u('A')), 'true');
insert into fx select 'chal', (select id::text from player_challenges where player_id = pg_temp.u('A'));
select testkit.expect_error('authenticated', pg_temp.u('A'), format('select claim_challenge_reward(%L)', (select v from fx where k = 'chal')), 'challenge_not_complete');
select testkit.expect_error('authenticated', pg_temp.u('B'), format('select claim_challenge_reward(%L)', (select v from fx where k = 'chal')), 'challenge_not_found');
update profiles set jobs_completed = jobs_completed + 1 where id = pg_temp.u('A');       -- progress is driven by server-side stats only
select testkit.assert_eq('progress follows server stats', pg_temp.j(pg_temp.u('A'), 'select get_my_daily_challenges()::text')->'items'->0->>'completed', 'true');
select testkit.assert_eq('completion announced', (select count(*)::text from notifications where player_id = pg_temp.u('A') and title = 'Challenge complete'), '1');
select testkit.assert_eq('analytics: challenge_completed', pg_temp.events(pg_temp.u('A'), 'challenge_completed'), '1');
select testkit.expect_error('authenticated', pg_temp.u('B'), format('select claim_challenge_reward(%L)', (select v from fx where k = 'chal')), 'challenge_not_found');
select testkit.assert_eq('claim pays coins', pg_temp.j(pg_temp.u('A'), format('select claim_challenge_reward(%L)::text', (select v from fx where k = 'chal')))->>'coins', '400');
select testkit.assert_eq('ledger row', (select amount::text from transactions where player_id = pg_temp.u('A') and kind = 'CHALLENGE_REWARD'), '400');
select testkit.assert_eq('xp granted', (select experience::text from profiles where id = pg_temp.u('A')), '30');
select testkit.assert_eq('wallet', testkit.as_user(pg_temp.u('A'), 'select balance from player_wallets'), '5400');
select testkit.expect_error('authenticated', pg_temp.u('A'), format('select claim_challenge_reward(%L)', (select v from fx where k = 'chal')), 'already_claimed');
select testkit.assert_eq('no double payout', (select count(*)::text from transactions where player_id = pg_temp.u('A') and kind = 'CHALLENGE_REWARD'), '1');
insert into challenge_definitions(id, title, description, metric, target, reward_coins, reward_xp, active)
  values ('drive_far', 'Road trip', 'Drive 5 km today.', 'distance_km', 5, 100, 0, true);
update challenge_definitions set active = false where id = 'deliver_one';
select testkit.assert_eq('new definition assigned', jsonb_array_length(pg_temp.j(pg_temp.u('A'), 'select get_my_daily_challenges()::text')->'items')::text, '2');
update player_challenges set expires_at = now() - interval '1 minute' where player_id = pg_temp.u('A') and challenge_id = 'drive_far';
update profiles set distance_km = distance_km + 6 where id = pg_temp.u('A');
select testkit.assert_eq('expired challenges do not progress', (select (completed_at is null)::text from player_challenges where player_id = pg_temp.u('A') and challenge_id = 'drive_far'), 'true');
-- a completed but expired challenge can no longer be claimed
insert into challenge_definitions(id, title, description, metric, target, reward_coins, reward_xp, active)
  values ('quick_one', 'Quick one', 'Complete 1 job today.', 'jobs_completed', 1, 50, 0, true);
select pg_temp.j(pg_temp.u('A'), 'select get_my_daily_challenges()::text');
update profiles set jobs_completed = jobs_completed + 1 where id = pg_temp.u('A');
select testkit.assert_eq('quick_one completed', (select (completed_at is not null)::text from player_challenges where player_id = pg_temp.u('A') and challenge_id = 'quick_one'), 'true');
update player_challenges set expires_at = now() - interval '1 minute' where player_id = pg_temp.u('A') and challenge_id = 'quick_one';
select testkit.expect_error('authenticated', pg_temp.u('A'), format('select claim_challenge_reward(%L)', (select id from player_challenges where player_id = pg_temp.u('A') and challenge_id = 'quick_one')), 'challenge_expired');
-- inactive definitions are never assigned
insert into challenge_definitions(id, title, description, metric, target, reward_coins, reward_xp, active)
  values ('sleeping_one', 'Sleeping', 'Not live yet.', 'jobs_completed', 1, 999, 0, false);
update game_config set value = 10 where key = 'daily_challenge_count';      -- room for every definition, so an inactive one would show up if it were assignable
select testkit.assert_eq('only active definitions are assigned', (select count(*)::text from jsonb_array_elements(testkit.as_user(pg_temp.u('B'), 'select get_my_daily_challenges()::text')::jsonb->'items') e where e->>'challenge_id' = 'sleeping_one'), '0');
select testkit.assert_eq('while active ones are', (select count(*)::text from jsonb_array_elements(testkit.as_user(pg_temp.u('B'), 'select get_my_daily_challenges()::text')::jsonb->'items') e where e->>'challenge_id' in ('drive_far','quick_one')), '2');
select testkit.expect_error('authenticated', pg_temp.u('A'), 'select * from player_challenges', 'permission denied');
select testkit.expect_error('authenticated', pg_temp.u('A'), $$update challenge_definitions set reward_coins = 99999999$$, 'permission denied');
select testkit.expect_error('authenticated', pg_temp.u('A'), $$insert into player_challenges(player_id, challenge_id, day, expires_at) values (auth.uid(), 'deliver_one', current_date, now())$$, 'permission denied');
delete from player_challenges; delete from challenge_definitions;
update game_config set value = 3 where key = 'daily_challenge_count';

select 'WORLD + JOBS + ECONOMY + PROGRESSION TESTS PASSED' as result;
