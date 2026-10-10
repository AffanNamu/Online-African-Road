\set ON_ERROR_STOP on
-- Backend foundation tests, part 4: authored content, leaderboards, analytics hygiene, dashboard summary, and structural
-- API-security checks that hold for EVERY endpoint (so a future endpoint cannot silently break them).
\set api `grep -v '^#' api_surface.txt | tr '\n' ' '`
select set_config('aro.api', :'api', false);
create function pg_temp.j(p_uid uuid, p_sql text) returns jsonb language sql as $$ select testkit.as_user(p_uid, p_sql)::jsonb $$;
create temp table fx(k text primary key, v text);
insert into fx select k, testkit.make_user(n)::text from (values ('A','LbA'),('B','LbB'),('C','LbC'),('D','LbD'),('E','LbE')) t(k, n);
create function pg_temp.u(k text) returns uuid language sql as $$ select v::uuid from fx where fx.k = $1 $$;

-- ===== CONTENT
select testkit.assert_eq('the four authored news items are seeded', (select count(*)::text from content_items where source = 'authored' and kind = 'news'), '4');
select testkit.assert_eq('served newest/highest priority first', pg_temp.j(pg_temp.u('A'), 'select get_content()::text')->'items'->0->>'id', 'corridor-open');
select testkit.assert_eq('served list', jsonb_array_length(pg_temp.j(pg_temp.u('A'), 'select get_content()::text')->'items')::text, '4');
select testkit.assert_eq('content is labelled authored (not external news)', pg_temp.j(pg_temp.u('A'), 'select get_content()::text')->'items'->0->>'source', 'authored');
select testkit.assert_eq('image key carried', pg_temp.j(pg_temp.u('A'), 'select get_content()::text')->'items'->0->>'image_key', 'highway');
select testkit.assert_eq('limit applies', jsonb_array_length(pg_temp.j(pg_temp.u('A'), 'select get_content(''news'', 2)::text')->'items')::text, '2');
insert into content_items(id, title, body, published) values ('draft-item', 'Draft', 'Not published yet', false);
insert into content_items(id, title, body, published, publish_at) values ('future-item', 'Soon', 'Not yet', true, now() + interval '1 day');
insert into content_items(id, title, body, published, publish_at, expires_at) values ('expired-item', 'Old', 'Gone', true, now() - interval '2 days', now() - interval '1 day');
insert into content_items(id, title, body, published, priority) values ('pinned-item', 'Pinned', 'Highest priority', true, 99);
select testkit.assert_eq('drafts, future and expired items are not served', (select count(*)::text from jsonb_array_elements(testkit.as_user(pg_temp.u('A'), 'select get_content(''news'', 50)::text')::jsonb->'items') e where e->>'id' in ('draft-item','future-item','expired-item')), '0');
select testkit.assert_eq('priority orders the feed', pg_temp.j(pg_temp.u('A'), 'select get_content()::text')->'items'->0->>'id', 'pinned-item');
select testkit.assert_eq('hero kind is separate', jsonb_array_length(pg_temp.j(pg_temp.u('A'), 'select get_content(''hero'')::text')->'items')::text, '0');
select testkit.expect_error('postgres', null, $$insert into content_items(id, title, body, source) values ('x-ext', 't', 'b', 'external')$$, 'violates check constraint');
select testkit.expect_error('postgres', null, $$insert into content_items(id, title, body) values ('x-long', 't', repeat('x', 221))$$, 'violates check constraint');
select testkit.expect_error('authenticated', pg_temp.u('A'), 'select * from content_items', 'permission denied');
select testkit.expect_error('authenticated', pg_temp.u('A'), $$update content_items set title = 'pwned'$$, 'permission denied');
select testkit.expect_error('authenticated', pg_temp.u('A'), $$insert into content_items(id, title, body, published) values ('evil', 'a', 'b', true)$$, 'permission denied');
select testkit.expect_error('anon', null, 'select get_content()', 'permission denied');
select testkit.expect_error('authenticated', null, 'select get_content()', 'not_authenticated');
delete from content_items where id in ('draft-item','future-item','expired-item','pinned-item');

-- ===== LEADERBOARDS (server-side values only)
update profiles set experience = 500, level = 3, distance_km = 40, jobs_completed = 4, lifetime_earnings = 900 where id = pg_temp.u('A');
update profiles set experience = 300, level = 2, distance_km = 90, jobs_completed = 4, lifetime_earnings = 1500 where id = pg_temp.u('B');
update profiles set experience = 300, level = 2, distance_km = 10, jobs_completed = 9, lifetime_earnings = 200 where id = pg_temp.u('C');
create function pg_temp.board(p_uid uuid, p_args text) returns jsonb language sql as $$ select testkit.as_user(p_uid, 'select get_leaderboard(' || p_args || ')::text')::jsonb $$;
-- other suites created players with their own stats: assert relative order among OUR players only
create function pg_temp.rank_of(p_metric text, p_name text) returns text language sql as $$
  select (select e->>'rank' from jsonb_array_elements(pg_temp.board(pg_temp.u('E'), format('%L, ''all'', 100, 0', p_metric))->'items') e where e->>'display_name' = p_name) $$;
select testkit.assert_eq('xp: A above B', (pg_temp.rank_of('xp', 'LbA')::int < pg_temp.rank_of('xp', 'LbB')::int)::text, 'true');
select testkit.assert_eq('xp: ties share a rank', pg_temp.rank_of('xp', 'LbB'), pg_temp.rank_of('xp', 'LbC'));
select testkit.assert_eq('xp: a player with no xp is not ranked', coalesce(pg_temp.rank_of('xp', 'LbD'), 'unranked'), 'unranked');
select testkit.assert_eq('distance: B first', (pg_temp.rank_of('distance', 'LbB')::int < pg_temp.rank_of('distance', 'LbA')::int)::text, 'true');
select testkit.assert_eq('jobs: C first', (pg_temp.rank_of('jobs', 'LbC')::int < pg_temp.rank_of('jobs', 'LbA')::int)::text, 'true');
select testkit.assert_eq('jobs: A and B tie', pg_temp.rank_of('jobs', 'LbA'), pg_temp.rank_of('jobs', 'LbB'));
select testkit.assert_eq('earnings: B above A above C', (pg_temp.rank_of('earnings', 'LbB')::int < pg_temp.rank_of('earnings', 'LbA')::int and pg_temp.rank_of('earnings', 'LbA')::int < pg_temp.rank_of('earnings', 'LbC')::int)::text, 'true');
select testkit.assert_eq('rank = 1 + players with a strictly higher value', pg_temp.rank_of('xp', 'LbA'), (select (count(*) + 1)::text from profiles where experience > 500));
select testkit.assert_eq('total counts ranked players only', pg_temp.board(pg_temp.u('E'), $$'xp'$$)->>'total', (select count(*)::text from profiles where experience > 0));
select testkit.assert_eq('my rank is reported', pg_temp.board(pg_temp.u('A'), $$'xp'$$)->'me'->>'rank', pg_temp.rank_of('xp', 'LbA'));
select testkit.assert_eq('my value is reported', pg_temp.board(pg_temp.u('A'), $$'xp'$$)->'me'->>'value', '500');
select testkit.assert_eq('unranked me has a null rank', (pg_temp.board(pg_temp.u('D'), $$'xp'$$)->'me'->'rank')::text, 'null');
select testkit.assert_eq('pagination: limit', jsonb_array_length(pg_temp.board(pg_temp.u('E'), $$'xp', 'all', 2, 0$$)->'items')::text, '2');
insert into fx select 'pg1', pg_temp.board(pg_temp.u('E'), $$'xp', 'all', 1, 0$$)::text;
insert into fx select 'pg2', pg_temp.board(pg_temp.u('E'), $$'xp', 'all', 1, 1$$)::text;
select testkit.assert_eq('pagination pages are disjoint', ((select v::jsonb->'items'->0->>'player_id' from fx where k = 'pg1') <> (select v::jsonb->'items'->0->>'player_id' from fx where k = 'pg2'))::text, 'true');
select testkit.assert_eq('pagination pages are ordered', (((select v::jsonb->'items'->0->>'rank' from fx where k = 'pg1')::int) <= ((select v::jsonb->'items'->0->>'rank' from fx where k = 'pg2')::int))::text, 'true');
select testkit.assert_eq('pagination echoes the window', (select v::jsonb->>'offset' from fx where k = 'pg2'), '1');
select testkit.assert_eq('limit is clamped to 100', pg_temp.board(pg_temp.u('E'), $$'xp', 'all', 100000, 0$$)->>'limit', '100');
select testkit.assert_eq('items carry public fields only', (select count(*)::text from jsonb_array_elements(pg_temp.board(pg_temp.u('E'), $$'earnings', 'all', 100, 0$$)->'items') e
   where e ?| array['coins','balance','email','last_seen_at','wallet']), '0');
select testkit.expect_error('authenticated', pg_temp.u('E'), $$select get_leaderboard('coins')$$, 'invalid_metric');
select testkit.expect_error('authenticated', pg_temp.u('E'), $$select get_leaderboard('xp', 'monthly')$$, 'invalid_period');
select testkit.expect_error('authenticated', pg_temp.u('E'), $$select get_leaderboard('xp', 'weekly')$$, 'unsupported_period');
select testkit.expect_error('authenticated', pg_temp.u('E'), $$select get_leaderboard('xp; drop table profiles')$$, 'invalid_metric');
select testkit.expect_error('anon', null, $$select get_leaderboard('xp')$$, 'permission denied');
select testkit.expect_error('authenticated', null, $$select get_leaderboard('xp')$$, 'not_authenticated');
-- a client cannot move itself up the board
select testkit.expect_error('authenticated', pg_temp.u('D'), 'update profiles set experience = 99999999 where id = auth.uid()', 'permission denied');
select testkit.expect_error('authenticated', pg_temp.u('D'), 'update profiles set lifetime_earnings = 99999999', 'permission denied');
-- weekly boards come from this week's completed jobs / ledger
insert into jobs(code, origin_id, destination_id, cargo_type, cargo_weight_kg, distance_km, difficulty, reward, xp_reward, required_category)
  select 'LB-OLD', o.id, d.id, 'Test', 1000, 3.0, 1, 100, 10, 'truck' from locations o, locations d where o.slug = 'lagos-apapa-port' and d.slug = 'lagos-mile12-market';
insert into job_assignments(job_id, player_id, vehicle_id, status, accepted_at, started_at, completed_at, reported_distance_km)
  select (select id from jobs where code = 'LB-OLD'), pg_temp.u('A'), (select id from vehicle_ownership where player_id = pg_temp.u('A')), 'completed',
         now() - interval '40 days', now() - interval '40 days', now() - interval '40 days', 3;
select testkit.assert_eq('weekly jobs ignore last month', coalesce((select e->>'value' from jsonb_array_elements(pg_temp.board(pg_temp.u('A'), $$'jobs', 'weekly', 100, 0$$)->'items') e where e->>'display_name' = 'LbA'), 'none'), 'none');
update job_assignments set completed_at = now() where job_id = (select id from jobs where code = 'LB-OLD');
select testkit.assert_eq('weekly jobs count this week', (select e->>'value' from jsonb_array_elements(pg_temp.board(pg_temp.u('A'), $$'jobs', 'weekly', 100, 0$$)->'items') e where e->>'display_name' = 'LbA'), '1');
select testkit.assert_eq('weekly distance', (select e->>'value' from jsonb_array_elements(pg_temp.board(pg_temp.u('A'), $$'distance', 'weekly', 100, 0$$)->'items') e where e->>'display_name' = 'LbA'), '3');
select admin_adjust_wallet(pg_temp.u('B'), 100, 'test', 'lb-b-1');
select testkit.assert_eq('adjustments are not weekly earnings', coalesce((select e->>'value' from jsonb_array_elements(pg_temp.board(pg_temp.u('A'), $$'earnings', 'weekly', 100, 0$$)->'items') e where e->>'display_name' = 'LbB'), 'none'), 'none');
select testkit.assert_eq('weekly: me with no activity is unranked', (pg_temp.board(pg_temp.u('D'), $$'jobs', 'weekly', 100, 0$$)->'me'->'rank')::text, 'null');
delete from job_assignments where job_id = (select id from jobs where code = 'LB-OLD');

-- ===== DASHBOARD SUMMARY (one round trip, all server-derived)
select testkit.mk_job('LB-SUM', 1, 640);
insert into fx select 'sum', testkit.as_user(pg_temp.u('D'), 'select get_dashboard_summary()::text');
select testkit.assert_eq('summary: identity', (select v::jsonb->'profile'->>'display_name' from fx where k = 'sum'), 'LbD');
select testkit.assert_eq('summary: coins', (select v::jsonb->>'coins' from fx where k = 'sum'), '5000');
select testkit.assert_eq('summary: vehicle', (select v::jsonb->'vehicle'->>'definition_id' from fx where k = 'sum'), 'truck_light_01');
select testkit.assert_eq('summary: vehicle count', (select v::jsonb->>'vehicle_count' from fx where k = 'sum'), '1');
select testkit.assert_eq('summary: jobs available is the real board', (select v::jsonb->'jobs'->>'available' from fx where k = 'sum'),
  (select count(*)::text from jobs where status = 'open' and expires_at > now()));
select testkit.assert_eq('summary: best reward is a real job reward', (select v::jsonb->'jobs'->>'best_reward' from fx where k = 'sum'),
  (select max(reward)::text from jobs where status = 'open' and expires_at > now()));
select testkit.assert_eq('summary: world counts', (select v::jsonb->'world'->>'cities' from fx where k = 'sum'), '23');
select testkit.assert_eq('summary: one drivable route at level 1', (select v::jsonb->'world'->>'routes_open' from fx where k = 'sum'), '1');
select testkit.assert_eq('summary: no convoy', (select (v::jsonb->'convoy')::text from fx where k = 'sum'), 'null');
select testkit.assert_eq('summary: no active job', (select (v::jsonb->'active_job')::text from fx where k = 'sum'), 'null');
select testkit.assert_eq('summary: milestone present', (select v::jsonb->'progression'->'next_milestone'->>'target' from fx where k = 'sum'), '50');
create function pg_temp.live_players() returns text language sql as $$
  select count(distinct player_id)::text from game_sessions where ended_at is null and last_seen_at > now() - interval '90 seconds' $$;
select testkit.assert_eq('summary: online players is the live count', (select v::jsonb->>'online_players' from fx where k = 'sum'), pg_temp.live_players());
insert into fx select 'live0', pg_temp.live_players();
insert into fx select 'sD', (pg_temp.j(pg_temp.u('D'), 'select start_session()::text')->>'session_id');
select pg_temp.j(pg_temp.u('E'), 'select start_session()::text');
select testkit.assert_eq('summary: online players follows sessions', pg_temp.j(pg_temp.u('D'), 'select get_dashboard_summary()::text')->>'online_players', ((select v::int from fx where k = 'live0') + 2)::text);
insert into fx select 'cvd', testkit.as_user(pg_temp.u('D'), $$select create_convoy('Summary Run')$$);
select testkit.assert_eq('summary: convoy', pg_temp.j(pg_temp.u('D'), 'select get_dashboard_summary()::text')->'convoy'->>'name', 'Summary Run');
select testkit.assert_eq('summary: convoy leader flag', pg_temp.j(pg_temp.u('D'), 'select get_dashboard_summary()::text')->'convoy'->>'is_leader', 'true');
select _notify(pg_temp.u('D'), 'system', 'Hi', 'There');
select testkit.assert_eq('summary: unread notifications', pg_temp.j(pg_temp.u('D'), 'select get_dashboard_summary()::text')->>'unread_notifications', '1');
insert into fx select 'lreq', pg_temp.j(pg_temp.u('E'), format('select send_friend_request(%L)::text', pg_temp.u('D')))->>'request_id';
select testkit.assert_eq('summary: pending friend requests', pg_temp.j(pg_temp.u('D'), 'select get_dashboard_summary()::text')->>'pending_friend_requests', '1');
select testkit.assert_eq('summary: friends online starts at zero', pg_temp.j(pg_temp.u('D'), 'select get_dashboard_summary()::text')->>'friends_online', '0');
select testkit.assert_eq('summary: accepting a friend who is online counts', testkit.as_user(pg_temp.u('D'), format('select respond_friend_request(%L, true)', (select v from fx where k = 'lreq'))), 'accepted');
select testkit.assert_eq('summary: friends online', pg_temp.j(pg_temp.u('D'), 'select get_dashboard_summary()::text')->>'friends_online', '1');
insert into fx select 'dasg', testkit.as_user(pg_temp.u('D'), format($$select accept_job((select id from jobs where code = 'LB-SUM'), %L)$$, (select id from vehicle_ownership where player_id = pg_temp.u('D'))));
select testkit.assert_eq('summary: active job', pg_temp.j(pg_temp.u('D'), 'select get_dashboard_summary()::text')->'active_job'->>'job_code', 'LB-SUM');
select testkit.assert_eq('the live session is recorded on the assignment', (select (session_id = (select v::uuid from fx where k = 'sD'))::text from job_assignments where id = (select v::uuid from fx where k = 'dasg')), 'true');
select testkit.assert_eq('vehicle status follows the job', pg_temp.j(pg_temp.u('D'), 'select get_dashboard_summary()::text')->'vehicle'->>'status', 'on_job');
select testkit.expect_error('anon', null, 'select get_dashboard_summary()', 'permission denied');
select testkit.expect_error('authenticated', null, 'select get_dashboard_summary()', 'not_authenticated');

-- ===== ANALYTICS: allow-listed names, no PII, no direct access
select testkit.assert_eq('client may report a multiplayer session', testkit.as_user(pg_temp.u('D'), $$select track_event('multiplayer_session_started', '{"players": 3, "email": "x@y.z", "nested": {"a": 1}}')::text$$), '');
select testkit.assert_eq('only allow-listed scalar keys survive', (select props::text from analytics_events where name = 'multiplayer_session_started' and player_id = pg_temp.u('D')), '{"players": 3}');
select testkit.expect_error('authenticated', pg_temp.u('D'), $$select track_event('login')$$, 'event_not_allowed');
select testkit.expect_error('authenticated', pg_temp.u('D'), $$select track_event('level_up')$$, 'event_not_allowed');
select testkit.expect_error('authenticated', pg_temp.u('D'), $$select track_event('made_up_event')$$, 'event_not_allowed');
select testkit.expect_error('authenticated', pg_temp.u('D'), 'select * from analytics_events', 'permission denied');
select testkit.expect_error('authenticated', pg_temp.u('D'), 'delete from analytics_events', 'permission denied');
select testkit.expect_error('authenticated', pg_temp.u('D'), $$select _track(auth.uid(), 'login')$$, 'permission denied');
select testkit.assert_eq('events carry the session id when a session is live', (select (session_id = (select v::uuid from fx where k = 'sD'))::text from analytics_events where name = 'convoy_created' and player_id = pg_temp.u('D')), 'true');
select testkit.assert_eq('no event carries personal keys', (select count(*)::text from analytics_events where props ?| array['email','ip','password','phone','token','name','display_name']), '0');
do $$ begin begin insert into analytics_events(name, props) values ('login', jsonb_build_object('k', repeat('x', 3000))); raise exception 'oversized props accepted';
  exception when check_violation then null; end; end $$;
do $$ begin begin insert into analytics_events(name) values ('unlisted_event'); raise exception 'unlisted event accepted';
  exception when check_violation then null; end; end $$;
-- (accounts with a ledger cannot be deleted by design, so check the rule itself: analytics rows are de-linked, never cascaded away)
select testkit.assert_eq('analytics rows are de-linked (ON DELETE SET NULL), not cascaded', (select confdeltype::text from pg_constraint
  where conrelid = 'analytics_events'::regclass and contype = 'f' and confrelid = 'profiles'::regclass), 'n');
select testkit.assert_eq('purge keeps recent data', (select (purge_old_data()->>'analytics_events')), '0');

-- ===== STRUCTURAL API SECURITY (every client-executable function)
create temp view api as
  select p.oid, p.proname, p.prosecdef, p.prosrc, p.proargnames, p.proconfig from pg_proc p
   where p.pronamespace = 'public'::regnamespace and has_function_privilege('authenticated', p.oid, 'execute');
select testkit.assert_eq('the API surface is exactly the reviewed list', (select count(*)::text from api where proname <> all (string_to_array(trim(:'api'), ' '))), '0');
select testkit.assert_eq('every listed endpoint exists', (select count(*)::text from unnest(string_to_array(trim(:'api'), ' ')) n where not exists (select 1 from api where proname = n)), '0');
select testkit.assert_eq('every endpoint is SECURITY DEFINER with a pinned search_path',
  (select count(*)::text from api where not prosecdef or not coalesce(proconfig::text like '%search_path=public%', false)), '0');
select testkit.assert_eq('every endpoint authenticates with auth.uid()', (select coalesce(string_agg(proname, ','), 'none') from api where prosrc not like '%auth.uid()%'), 'none');
select testkit.assert_eq('no endpoint accepts a caller identity (the player is always auth.uid())',
  (select coalesce(string_agg(proname || '(' || a || ')', ','), 'none') from api, unnest(proargnames) a
    where a in ('p_uid','p_user','p_user_id','p_player_id','p_owner','p_owner_id','p_me','p_self','p_caller')), 'none');
select testkit.assert_eq('no endpoint accepts a reward, xp, coins, balance or amount from the client',
  (select coalesce(string_agg(proname || '(' || a || ')', ','), 'none') from api, unnest(proargnames) a
    where a ~* '(reward|xp|coin|balance|amount|price|cost|level|distance|damage_total)' and proname not in ('complete_job')), 'none');
select testkit.assert_eq('complete_job accepts only the damage figure besides the assignment id', (select array_to_string(proargnames, ',') from api where proname = 'complete_job'), 'p_assignment,p_damage_pct');
select testkit.assert_eq('every write endpoint is rate limited or state-machine guarded',
  (select coalesce(string_agg(proname, ','), 'none') from api
    where prosrc not like '%_rate_limit%'
      and proname not in ('set_display_name','accept_job','start_job','submit_telemetry','complete_job','abandon_job','buy_fuel','repair_vehicle',
                          'create_convoy','join_convoy','leave_convoy','get_convoy_session_code','set_convoy_session_code','buy_vehicle',
                          'start_bus_run','submit_bus_telemetry','serve_stop','abandon_bus_run')), 'none');
select testkit.assert_eq('no table in public is readable by anon', (select count(*)::text from information_schema.role_table_grants where table_schema = 'public' and grantee = 'anon'), '0');
select testkit.assert_eq('clients hold no write privilege on any table', (select count(*)::text from information_schema.role_table_grants
   where table_schema = 'public' and grantee = 'authenticated' and privilege_type in ('INSERT','UPDATE','DELETE','TRUNCATE')), '0');
select testkit.assert_eq('RLS is enabled on every table', (select count(*)::text from pg_class c where c.relnamespace = 'public'::regnamespace and c.relkind = 'r' and not c.relrowsecurity), '0');
select testkit.assert_eq('no function is executable by anon', (select count(*)::text from pg_proc p where p.pronamespace = 'public'::regnamespace and has_function_privilege('anon', p.oid, 'execute')), '0');
select testkit.assert_eq('no internal helper is executable by clients', (select coalesce(string_agg(proname, ','), 'none') from api where proname like '\_%'), 'none');
select testkit.assert_eq('no admin tool is executable by clients', (select coalesce(string_agg(proname, ','), 'none') from api where proname like 'admin\_%' or proname in ('purge_old_data','sweep_stale_sessions','generate_jobs','cfg')), 'none');
select testkit.assert_eq('trigger functions are not callable by clients', (select coalesce(string_agg(proname, ','), 'none') from api where prosrc like '%TG_OP%' or proname in
  ('handle_new_user')), 'none');

select 'CONTENT + LEADERBOARD + SECURITY TESTS PASSED' as result;
