\set ON_ERROR_STOP on
-- Backend foundation tests, part 1: profile authorization, presence (including server-side expiry), rate limiting.
create function pg_temp.j(p_uid uuid, p_sql text) returns jsonb language sql as $$ select testkit.as_user(p_uid, p_sql)::jsonb $$;
create temp table fx(k text primary key, v text);
insert into fx select k, testkit.make_user(n)::text from (values ('A','ProfA'),('B','ProfB'),('C','ProfC')) t(k, n);
create function pg_temp.u(k text) returns uuid language sql as $$ select v::uuid from fx where fx.k = $1 $$;

-- ===== PROFILE
select testkit.assert_eq('username derived from the display name', pg_temp.j(pg_temp.u('A'), 'select get_my_profile()::text')->>'username', 'profa');
select testkit.assert_eq('profile level', pg_temp.j(pg_temp.u('A'), 'select get_my_profile()::text')->>'level', '1');
select testkit.assert_eq('profile coins come from the wallet', pg_temp.j(pg_temp.u('A'), 'select get_my_profile()::text')->>'coins', '5000');
select testkit.assert_eq('profile xp window start', pg_temp.j(pg_temp.u('A'), 'select get_my_profile()::text')->>'xp_level_start', '0');
select testkit.assert_eq('profile xp window end (level 2 starts at 100)', pg_temp.j(pg_temp.u('A'), 'select get_my_profile()::text')->>'xp_next_level', '100');
select testkit.assert_eq('jobs_total counts assignments', pg_temp.j(pg_temp.u('A'), 'select get_my_profile()::text')->>'jobs_total', '0');

select testkit.assert_eq('update avatar + country',
  pg_temp.j(pg_temp.u('A'), $$select update_my_profile(null, null, 'sunset', 'NG')::text$$)->>'avatar_id', 'sunset');
select testkit.assert_eq('country persisted', (select country_code from profiles where id = pg_temp.u('A')), 'NG');
select testkit.assert_eq('unchanged fields stay (null = leave alone)', (select display_name from profiles where id = pg_temp.u('A')), 'ProfA');
select testkit.assert_eq('updated_at moves on edit', (select (updated_at > created_at)::text from profiles where id = pg_temp.u('A')), 'true');
select testkit.assert_eq('rename works', pg_temp.j(pg_temp.u('A'), $$select update_my_profile('Prof Alpha', 'profalpha')::text$$)->>'display_name', 'Prof Alpha');
select testkit.expect_error('authenticated', pg_temp.u('B'), $$select update_my_profile(null, 'profalpha')$$, 'username_taken');
select testkit.expect_error('authenticated', pg_temp.u('B'), $$select update_my_profile('prof ALPHA')$$, 'display_name_taken');
select testkit.expect_error('authenticated', pg_temp.u('B'), $$select update_my_profile(null, 'Bad Name!')$$, 'invalid_username');
select testkit.expect_error('authenticated', pg_temp.u('B'), $$select update_my_profile(null, null, 'UPPER CASE')$$, 'invalid_avatar');
select testkit.expect_error('authenticated', pg_temp.u('B'), $$select update_my_profile(null, null, null, 'ZZ')$$, 'unknown_country');
select testkit.expect_error('authenticated', pg_temp.u('B'), $$select update_my_profile('x')$$, 'invalid_display_name');
-- protected statistics have no client path at all
select testkit.expect_error('authenticated', pg_temp.u('B'), 'update profiles set avatar_id = ''x'' where id = auth.uid()', 'permission denied');
select testkit.expect_error('authenticated', pg_temp.u('B'), 'update profiles set lifetime_earnings = 999999999 where id = auth.uid()', 'permission denied');
select testkit.expect_error('authenticated', pg_temp.u('B'), 'update profiles set selected_vehicle_id = null', 'permission denied');
select testkit.expect_error('authenticated', pg_temp.u('B'), 'update profiles set username = ''hijack'' where id <> auth.uid()', 'permission denied');
-- private columns are not readable even though public ones are
select testkit.expect_error('authenticated', pg_temp.u('B'), 'select last_seen_at from profiles', 'permission denied');
select testkit.expect_error('authenticated', pg_temp.u('B'), 'select lifetime_earnings from profiles', 'permission denied');
select testkit.expect_error('authenticated', pg_temp.u('B'), 'select selected_vehicle_id from profiles', 'permission denied');
select testkit.assert_eq('public columns remain readable', testkit.as_user(pg_temp.u('B'), 'select count(*) from profiles where username is not null'), (select count(*)::text from profiles));
-- public profile of someone else: no private data
select testkit.assert_eq('public profile has no coins', (pg_temp.j(pg_temp.u('B'), format('select get_public_profile(%L)::text', pg_temp.u('A'))) ? 'coins')::text, 'false');
select testkit.assert_eq('public profile has no wallet', (pg_temp.j(pg_temp.u('B'), format('select get_public_profile(%L)::text', pg_temp.u('A'))) ? 'lifetime_earnings')::text, 'false');
select testkit.assert_eq('strangers are not friends', pg_temp.j(pg_temp.u('B'), format('select get_public_profile(%L)::text', pg_temp.u('A')))->>'friend_status', 'none');
select testkit.assert_eq('stranger presence is hidden', (pg_temp.j(pg_temp.u('B'), format('select get_public_profile(%L)::text', pg_temp.u('A')))->'presence')::text, 'null');
select testkit.expect_error('authenticated', pg_temp.u('B'), 'select get_public_profile(gen_random_uuid())', 'player_not_found');
-- authentication
select testkit.expect_error('authenticated', null, 'select get_my_profile()', 'not_authenticated');
select testkit.expect_error('authenticated', null, 'select update_my_profile(''abc'')', 'not_authenticated');
select testkit.expect_error('anon', null, 'select get_my_profile()', 'permission denied');
select testkit.expect_error('anon', null, 'select get_online_count()', 'permission denied');
select testkit.expect_error('anon', null, 'select start_session()', 'permission denied');

-- ===== PRESENCE
select testkit.assert_eq('nobody is online before any session', testkit.as_user(pg_temp.u('B'), 'select get_online_count()'), '0');
insert into fx select 'sA', (pg_temp.j(pg_temp.u('A'), 'select start_session(''webgl'')::text')->>'session_id');
select testkit.assert_eq('one player online after start_session', testkit.as_user(pg_temp.u('B'), 'select get_online_count()'), '1');
select testkit.assert_eq('profile last_seen set by login', (select (last_seen_at is not null)::text from profiles where id = pg_temp.u('A')), 'true');
select testkit.assert_eq('login analytics event written', (select count(*)::text from analytics_events where name = 'login' and player_id = pg_temp.u('A')), '1');
select testkit.assert_eq('heartbeat returns server time + count',
  (pg_temp.j(pg_temp.u('A'), format('select heartbeat(%L, ''driving'', ''lagos-ibadan'', ''lagos'')::text', (select v from fx where k = 'sA'))) ? 'server_time')::text, 'true');
select testkit.assert_eq('my presence: online', pg_temp.j(pg_temp.u('A'), 'select get_my_presence()::text')->>'online', 'true');
select testkit.assert_eq('my presence: activity', pg_temp.j(pg_temp.u('A'), 'select get_my_presence()::text')->>'activity', 'driving');
select testkit.assert_eq('my presence: route', pg_temp.j(pg_temp.u('A'), 'select get_my_presence()::text')->>'route_code', 'lagos-ibadan');
select testkit.assert_eq('my presence: city', pg_temp.j(pg_temp.u('A'), 'select get_my_presence()::text')->>'city_slug', 'lagos');
-- a session id is not a bearer token
select testkit.expect_error('authenticated', pg_temp.u('B'), format('select heartbeat(%L)', (select v from fx where k = 'sA')), 'session_not_found');
select testkit.expect_error('authenticated', pg_temp.u('B'), format('select end_session(%L)', (select v from fx where k = 'sA')), 'session_not_found');
select testkit.expect_error('authenticated', pg_temp.u('A'), format('select heartbeat(%L, ''flying'')', (select v from fx where k = 'sA')), 'invalid_activity');
select testkit.expect_error('authenticated', pg_temp.u('A'), format('select heartbeat(%L, ''menu'', ''atlantis-road'')', (select v from fx where k = 'sA')), 'unknown_route');
select testkit.expect_error('authenticated', pg_temp.u('A'), format('select heartbeat(%L, ''menu'', null, ''gotham'')', (select v from fx where k = 'sA')), 'unknown_city');
select testkit.expect_error('authenticated', pg_temp.u('A'), 'select heartbeat(gen_random_uuid())', 'session_not_found');

-- server-side expiry: no heartbeat for longer than the TTL => offline, with no client action
update game_sessions set last_seen_at = now() - interval '120 seconds' where id = (select v::uuid from fx where k = 'sA');
select testkit.assert_eq('expired session no longer counts as online', testkit.as_user(pg_temp.u('B'), 'select get_online_count()'), '0');
select testkit.assert_eq('my presence: offline after expiry', pg_temp.j(pg_temp.u('A'), 'select get_my_presence()::text')->>'online', 'false');
select testkit.expect_error('authenticated', pg_temp.u('A'), format('select heartbeat(%L)', (select v from fx where k = 'sA')), 'session_expired');
select testkit.assert_eq('sweep closes it', (select sweep_stale_sessions()::text), '1');
select testkit.assert_eq('swept session is marked timeout', (select end_reason from game_sessions where id = (select v::uuid from fx where k = 'sA')), 'timeout');
select testkit.assert_eq('sweep is idempotent', (select sweep_stale_sessions()::text), '0');
-- a heartbeat just inside the window keeps the session alive
insert into fx select 'sA2', (pg_temp.j(pg_temp.u('A'), 'select start_session(''webgl'')::text')->>'session_id');
update game_sessions set last_seen_at = now() - interval '80 seconds' where id = (select v::uuid from fx where k = 'sA2');
select testkit.assert_eq('80 s of silence is still online', testkit.as_user(pg_temp.u('B'), 'select get_online_count()'), '1');
select testkit.assert_eq('heartbeat inside the window succeeds', (pg_temp.j(pg_temp.u('A'), format('select heartbeat(%L)::text', (select v from fx where k = 'sA2'))) ? 'server_time')::text, 'true');
-- ending
select testkit.assert_eq('end_session ok', testkit.as_user(pg_temp.u('A'), format('select end_session(%L)::text', (select v from fx where k = 'sA2'))), '');
select testkit.assert_eq('offline after logout', testkit.as_user(pg_temp.u('B'), 'select get_online_count()'), '0');
select testkit.assert_eq('end_session is idempotent', testkit.as_user(pg_temp.u('A'), format('select end_session(%L)::text', (select v from fx where k = 'sA2'))), '');
select testkit.assert_eq('logout analytics event written once', (select count(*)::text from analytics_events where name = 'logout' and player_id = pg_temp.u('A')), '1');
select testkit.expect_error('authenticated', pg_temp.u('A'), format('select heartbeat(%L)', (select v from fx where k = 'sA2')), 'session_expired');
-- the online count is an aggregate: two sessions of one player count once, two players count twice
insert into fx select 'sA3', (pg_temp.j(pg_temp.u('A'), 'select start_session()::text')->>'session_id');
insert into fx select 'sA4', (pg_temp.j(pg_temp.u('A'), 'select start_session()::text')->>'session_id');
insert into fx select 'sB1', (pg_temp.j(pg_temp.u('B'), 'select start_session()::text')->>'session_id');
select testkit.assert_eq('distinct players, not sessions', testkit.as_user(pg_temp.u('C'), 'select get_online_count()'), '2');
-- live-session cap: the oldest are replaced
do $$ declare i int; begin for i in 1..6 loop perform testkit.as_user((select v::uuid from fx where k = 'C'), 'select start_session()::text'); end loop; end $$;
select testkit.assert_eq('a player never holds more than 5 live sessions', (select count(*)::text from game_sessions where player_id = pg_temp.u('C') and ended_at is null), '5');
select testkit.assert_eq('replaced sessions are marked', (select (count(*) >= 1)::text from game_sessions where player_id = pg_temp.u('C') and end_reason = 'replaced'), 'true');
-- sessions / presence rows are not directly readable or writable
select testkit.expect_error('authenticated', pg_temp.u('A'), 'select * from game_sessions', 'permission denied');
select testkit.expect_error('authenticated', pg_temp.u('A'), 'update game_sessions set last_seen_at = now()', 'permission denied');
select testkit.expect_error('authenticated', pg_temp.u('A'), 'insert into game_sessions(player_id) values (auth.uid())', 'permission denied');
select testkit.expect_error('authenticated', pg_temp.u('A'), 'select sweep_stale_sessions()', 'permission denied');
insert into fx select 'sBx', (pg_temp.j(pg_temp.u('B'), $$select start_session('<script>')::text$$)->>'session_id');
select testkit.assert_eq('client label is sanitised', (select client from game_sessions where id = (select v::uuid from fx where k = 'sBx')), 'unknown');

-- ===== RATE LIMITING (fixed one-minute window; 45 calls always cross a 20/min limit even if the test straddles a window boundary)
do $$ declare ok int := 0; limited int := 0; i int; sid text; begin
  sid := testkit.as_user((select v::uuid from fx where k = 'B'), 'select start_session()::text')::jsonb->>'session_id';
  for i in 1..45 loop
    begin
      perform testkit.as_user((select v::uuid from fx where k = 'B'), format('select heartbeat(%L)::text', sid));
      ok := ok + 1;
    exception when others then
      if sqlerrm like '%rate_limited%' then limited := limited + 1; else raise; end if;
    end;
  end loop;
  if limited = 0 then raise exception 'RATE LIMIT NEVER TRIGGERED in 45 heartbeats (ok=%)', ok; end if;
  if ok > 40 then raise exception 'rate limit let % heartbeats through', ok; end if;
end $$;
select testkit.expect_error('authenticated', pg_temp.u('A'), 'select * from api_rate_limits', 'permission denied');
select testkit.expect_error('authenticated', pg_temp.u('A'), 'update api_rate_limits set hits = 0', 'permission denied');
select testkit.expect_error('authenticated', pg_temp.u('A'), $$select _rate_limit('x', 1, 1)$$, 'permission denied');
select testkit.expect_error('authenticated', pg_temp.u('A'), 'select cfg(''presence_ttl_seconds'')', 'permission denied');
select testkit.expect_error('authenticated', pg_temp.u('A'), 'select * from game_config', 'permission denied');

select 'PROFILE + PRESENCE TESTS PASSED' as result;
