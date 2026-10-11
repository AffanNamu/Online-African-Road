\set ON_ERROR_STOP on
-- Convoy lobby: route selection, ready check, start/stop driving, tags, chat, finding convoys and players, server status.
create function pg_temp.j(p_uid uuid, p_sql text) returns jsonb language sql as $$ select testkit.as_user(p_uid, p_sql)::jsonb $$;
create temp table fx(k text primary key, v text);
insert into fx select k, testkit.make_user(n)::text from (values ('L','CvL'),('M1','CvM1'),('M2','CvM2'),('S','CvS'),('X1','CvX1'),('X2','CvX2'),('F','CvF')) t(k, n);
create function pg_temp.u(k text) returns uuid language sql as $$ select v::uuid from fx where fx.k = $1 $$;
create function pg_temp.cv(p_uid uuid) returns jsonb language sql as $$ select testkit.as_user(p_uid, 'select get_my_convoy()::text')::jsonb->'convoy' $$;
create function pg_temp.member(p_uid uuid, p_name text) returns jsonb language sql as $$
  select m from jsonb_array_elements(pg_temp.cv(p_uid)->'members') m where m->>'display_name' = p_name $$;
create function pg_temp.msgs(p_uid uuid, p_after bigint default 0) returns jsonb language sql as $$
  select testkit.as_user(p_uid, format('select list_convoy_messages(%s, 100)::text', p_after))::jsonb->'items' $$;
create function pg_temp.has_msg(p_uid uuid, p_like text) returns text language sql as $$
  select (count(*) > 0)::text from jsonb_array_elements(pg_temp.msgs(p_uid)) m where m->>'body' like p_like $$;

-- a clean slate: earlier suites left convoys and live sessions behind
update convoys set disbanded_at = now() where disbanded_at is null;
update game_sessions set ended_at = now() where ended_at is null;
select admin_set_route('lagos-ibadan', 'OPEN', 1);
create temp table planned as select code from world_routes where status = 'PLANNED' order by code limit 1;

-- ===== create, join, initial lobby state
insert into fx select 'cv', testkit.as_user(pg_temp.u('L'), $$select create_convoy('Lobby Run')$$);
select testkit.as_user(pg_temp.u('M1'), format('select join_convoy(%L)', (select v from fx where k = 'cv')));
select testkit.as_user(pg_temp.u('M2'), format('select join_convoy(%L)', (select v from fx where k = 'cv')));
select testkit.as_user(pg_temp.u(k), 'select start_session()::text') from (values ('L'),('M1'),('M2')) t(k);
select testkit.assert_eq('a new convoy is a lobby', pg_temp.cv(pg_temp.u('L'))->>'phase', 'lobby');
select testkit.assert_eq('no route yet', (pg_temp.cv(pg_temp.u('L'))->'route')::text, 'null');
select testkit.assert_eq('no tags yet', (pg_temp.cv(pg_temp.u('L'))->'tags')::text, '[]');
select testkit.assert_eq('the leader counts as ready', pg_temp.member(pg_temp.u('L'), 'CvL')->>'ready', 'true');
select testkit.assert_eq('members start not ready', pg_temp.member(pg_temp.u('L'), 'CvM1')->>'ready', 'false');
select testkit.assert_eq('not all ready', pg_temp.cv(pg_temp.u('L'))->>'all_ready', 'false');
select testkit.assert_eq('members carry their vehicle name', ((pg_temp.member(pg_temp.u('L'), 'CvM1')->>'vehicle') is not null)::text, 'true');
select testkit.assert_eq('system line: creator', pg_temp.has_msg(pg_temp.u('L'), 'CvL created the convoy.'), 'true');
select testkit.assert_eq('system line: joiner', pg_temp.has_msg(pg_temp.u('L'), 'CvM1 joined the convoy.'), 'true');

-- ===== route selection (leader only, server-validated)
select testkit.expect_error('authenticated', pg_temp.u('M1'), $$select set_convoy_route('lagos-ibadan')$$, 'not_leader');
select testkit.expect_error('authenticated', pg_temp.u('S'), $$select set_convoy_route('lagos-ibadan')$$, 'not_leader');
select testkit.expect_error('authenticated', pg_temp.u('L'), $$select set_convoy_route('nowhere-at-all')$$, 'route_not_found');
select testkit.expect_error('authenticated', pg_temp.u('L'), format('select set_convoy_route(%L)', (select code from planned)), 'route_unavailable');
select admin_set_route('lagos-ibadan', 'LOCKED');
select testkit.expect_error('authenticated', pg_temp.u('L'), $$select set_convoy_route('lagos-ibadan')$$, 'route_locked');
select admin_set_route('lagos-ibadan', 'OPEN', 4);
select testkit.expect_error('authenticated', pg_temp.u('L'), $$select set_convoy_route('lagos-ibadan')$$, 'route_locked');
select admin_set_route('lagos-ibadan', 'OPEN', 1);
select testkit.expect_error('anon', null, $$select set_convoy_route('lagos-ibadan')$$, 'permission denied');
select testkit.expect_error('authenticated', null, $$select set_convoy_route('lagos-ibadan')$$, 'not_authenticated');
select testkit.expect_error('authenticated', pg_temp.u('M1'), 'select set_my_ready(true)', 'route_required');
select testkit.expect_error('authenticated', pg_temp.u('L'), 'select start_convoy_drive()', 'route_required');
select testkit.assert_eq('leader sets the route', pg_temp.j(pg_temp.u('L'), $$select set_convoy_route('lagos-ibadan')::text$$)->'route'->>'code', 'lagos-ibadan');
select testkit.assert_eq('route json: from', pg_temp.cv(pg_temp.u('M2'))->'route'->>'from', 'Lagos');
select testkit.assert_eq('route json: to', pg_temp.cv(pg_temp.u('M2'))->'route'->>'to', 'Ibadan');
select testkit.assert_eq('route json: distance comes from the world state', pg_temp.cv(pg_temp.u('M2'))->'route'->>'distance_km', (select distance_km::text from world_routes where code = 'lagos-ibadan'));
select testkit.assert_eq('route json: estimated minutes', pg_temp.cv(pg_temp.u('M2'))->'route'->>'est_minutes', (select greatest(1, round(distance_km / 50 * 60))::int::text from world_routes where code = 'lagos-ibadan'));
select testkit.assert_eq('system line: route', pg_temp.has_msg(pg_temp.u('L'), 'Route set:%'), 'true');

-- ===== ready check
select testkit.expect_error('authenticated', pg_temp.u('S'), 'select set_my_ready(true)', 'not_in_convoy');
select pg_temp.j(pg_temp.u('M1'), 'select set_my_ready(true)::text');
select testkit.assert_eq('M1 is ready', pg_temp.member(pg_temp.u('M1'), 'CvM1')->>'ready', 'true');
select testkit.assert_eq('only the caller changed', pg_temp.member(pg_temp.u('L'), 'CvM2')->>'ready', 'false');
select testkit.assert_eq('still not all ready', pg_temp.cv(pg_temp.u('L'))->>'all_ready', 'false');
select testkit.expect_error('authenticated', pg_temp.u('L'), 'select start_convoy_drive()', 'members_not_ready');
select testkit.expect_error('authenticated', pg_temp.u('M1'), 'select start_convoy_drive()', 'not_leader');
select pg_temp.j(pg_temp.u('M2'), 'select set_my_ready(true)::text');
select testkit.assert_eq('now everyone is ready', pg_temp.cv(pg_temp.u('L'))->>'all_ready', 'true');
select testkit.assert_eq('system line: ready', pg_temp.has_msg(pg_temp.u('L'), 'CvM2 is ready.'), 'true');
select pg_temp.j(pg_temp.u('M2'), 'select set_my_ready(false)::text');
select testkit.assert_eq('un-readying is possible', pg_temp.cv(pg_temp.u('L'))->>'all_ready', 'false');
select testkit.assert_eq('system line: not ready', pg_temp.has_msg(pg_temp.u('L'), 'CvM2 is not ready.'), 'true');
select pg_temp.j(pg_temp.u('M2'), 'select set_my_ready(true)::text');
-- changing the route clears the ready check (it was for the old route)
select pg_temp.j(pg_temp.u('L'), 'select set_convoy_route(null)::text');
select testkit.assert_eq('clearing the route clears ready flags', pg_temp.member(pg_temp.u('L'), 'CvM1')->>'ready', 'false');
select testkit.assert_eq('route cleared', (pg_temp.cv(pg_temp.u('L'))->'route')::text, 'null');
select pg_temp.j(pg_temp.u('L'), $$select set_convoy_route('lagos-ibadan')::text$$);
select pg_temp.j(pg_temp.u('M1'), 'select set_my_ready(true)::text'); select pg_temp.j(pg_temp.u('M2'), 'select set_my_ready(true)::text');
select testkit.assert_eq('setting the same route again keeps the ready flags', pg_temp.j(pg_temp.u('L'), $$select set_convoy_route('lagos-ibadan')::text$$)->>'all_ready', 'true');
select testkit.expect_error('authenticated', pg_temp.u('M1'), $$update convoy_members set ready = true$$, 'permission denied');
select testkit.expect_error('authenticated', pg_temp.u('M1'), $$update convoys set phase = 'driving'$$, 'permission denied');

-- ===== start: every member must meet the route's level
update profiles set level = 5 where id = pg_temp.u('L');
select admin_set_route('lagos-ibadan', 'OPEN', 3);
select testkit.expect_error('authenticated', pg_temp.u('L'), 'select start_convoy_drive()', 'member_level_too_low');
select testkit.assert_eq('a refused start changes nothing', pg_temp.cv(pg_temp.u('L'))->>'phase', 'lobby');
select admin_set_route('lagos-ibadan', 'PLANNED');
select testkit.expect_error('authenticated', pg_temp.u('L'), 'select start_convoy_drive()', 'route_unavailable');
select admin_set_route('lagos-ibadan', 'OPEN', 1);
update profiles set level = 1 where id = pg_temp.u('L');

-- ===== start driving
-- a member whose heartbeat stopped blocks the start
update game_sessions set last_seen_at = now() - interval '10 minutes' where player_id = pg_temp.u('M2');
select testkit.expect_error('authenticated', pg_temp.u('L'), 'select start_convoy_drive()', 'members_offline');
select testkit.assert_eq('a refused start changes nothing (offline)', pg_temp.cv(pg_temp.u('L'))->>'phase', 'lobby');
update game_sessions set last_seen_at = now() where player_id = pg_temp.u('M2');
select testkit.assert_eq('the leader starts the convoy', pg_temp.j(pg_temp.u('L'), 'select start_convoy_drive()::text')->>'phase', 'driving');
select testkit.assert_eq('status reads in_progress', pg_temp.cv(pg_temp.u('M1'))->>'status', 'in_progress');
select testkit.assert_eq('server stamped the start', ((pg_temp.cv(pg_temp.u('M1'))->>'started_at') is not null)::text, 'true');
select testkit.assert_eq('members are told', (select count(*)::text from notifications where type = 'convoy_event' and title = 'Convoy on the road' and player_id in (pg_temp.u('M1'), pg_temp.u('M2'))), '2');
select testkit.assert_eq('the leader is not notified of their own start', (select count(*)::text from notifications where title = 'Convoy on the road' and player_id = pg_temp.u('L')), '0');
select testkit.assert_eq('analytics: convoy_started', (select count(*)::text from analytics_events where name = 'convoy_started' and player_id = pg_temp.u('L')), '1');
select testkit.assert_eq('system line: on the road', pg_temp.has_msg(pg_temp.u('M2'), 'The convoy is on the road:%'), 'true');
select testkit.expect_error('authenticated', pg_temp.u('L'), 'select start_convoy_drive()', 'convoy_driving');
select testkit.expect_error('authenticated', pg_temp.u('L'), $$select set_convoy_route('lagos-ibadan')$$, 'convoy_driving');
select testkit.expect_error('authenticated', pg_temp.u('M1'), 'select set_my_ready(false)', 'convoy_driving');
select testkit.expect_error('authenticated', pg_temp.u('M1'), 'select end_convoy_drive()', 'not_leader');
-- the live-session code is released to members only
select testkit.as_user(pg_temp.u('L'), format($q$select set_convoy_session_code(%L, 'ABCD1234')$q$, (select v from fx where k = 'cv')));
select testkit.assert_eq('member can fetch the code', testkit.as_user(pg_temp.u('M1'), format('select get_convoy_session_code(%L)', (select v from fx where k = 'cv'))), 'ABCD1234');
select testkit.expect_error('authenticated', pg_temp.u('S'), format('select get_convoy_session_code(%L)', (select v from fx where k = 'cv')), 'not_a_member');
-- back to the lobby clears the code and the ready flags
select testkit.assert_eq('leader ends the drive', pg_temp.j(pg_temp.u('L'), 'select end_convoy_drive()::text')->>'phase', 'lobby');
select testkit.assert_eq('ready flags cleared', pg_temp.member(pg_temp.u('L'), 'CvM1')->>'ready', 'false');
select testkit.assert_eq('session code cleared', (select (session_code is null)::text from convoys where id = (select v::uuid from fx where k = 'cv')), 'true');
select testkit.assert_eq('ending a lobby is a no-op', pg_temp.j(pg_temp.u('L'), 'select end_convoy_drive()::text')->>'phase', 'lobby');

-- ===== tags and language
select testkit.expect_error('authenticated', pg_temp.u('M1'), $$select set_convoy_tags(array['jobs'], 'en')$$, 'not_leader');
select testkit.assert_eq('leader sets tags', pg_temp.j(pg_temp.u('L'), $$select set_convoy_tags(array['jobs','chill_drive','jobs'], 'fr')::text$$)->>'tags', '["chill_drive", "jobs"]');
select testkit.assert_eq('language stored', pg_temp.cv(pg_temp.u('M1'))->>'language', 'fr');
select testkit.expect_error('authenticated', pg_temp.u('L'), $$select set_convoy_tags(array['jobs','freight','training','friendly'], 'en')$$, 'invalid_tags');
select testkit.expect_error('authenticated', pg_temp.u('L'), $$select set_convoy_tags(array['hacking'], 'en')$$, 'invalid_tags');
select testkit.expect_error('authenticated', pg_temp.u('L'), $$select set_convoy_tags(array['jobs'], 'english')$$, 'invalid_tags');
select testkit.assert_eq('a failed change leaves the tags', pg_temp.cv(pg_temp.u('M1'))->>'language', 'fr');
select pg_temp.j(pg_temp.u('L'), $$select set_convoy_tags(null, 'en')::text$$);
select testkit.assert_eq('null tags keep the old ones', pg_temp.cv(pg_temp.u('M1'))->>'tags', '["chill_drive", "jobs"]');

-- ===== chat
select testkit.expect_error('authenticated', pg_temp.u('S'), $$select post_convoy_message('hello')$$, 'not_in_convoy');
select testkit.expect_error('authenticated', pg_temp.u('S'), 'select list_convoy_messages()', 'not_in_convoy');
select testkit.expect_error('anon', null, $$select post_convoy_message('hello')$$, 'permission denied');
select testkit.expect_error('authenticated', pg_temp.u('M1'), $$select post_convoy_message('   ')$$, 'invalid_message');
select testkit.expect_error('authenticated', pg_temp.u('M1'), $$select post_convoy_message(null)$$, 'invalid_message');
select testkit.expect_error('authenticated', pg_temp.u('M1'), format('select post_convoy_message(%L)', repeat('x', 201)), 'invalid_message');
select pg_temp.j(pg_temp.u('M1'), $$select post_convoy_message('Ready to roll!')::text$$);
select testkit.assert_eq('another member reads it', pg_temp.has_msg(pg_temp.u('M2'), 'Ready to roll!'), 'true');
select testkit.assert_eq('sender is named by the server', (select m->'sender'->>'display_name' from jsonb_array_elements(pg_temp.msgs(pg_temp.u('M2'))) m where m->>'body' = 'Ready to roll!'), 'CvM1');
select testkit.assert_eq('chat lines are kind chat', (select m->>'kind' from jsonb_array_elements(pg_temp.msgs(pg_temp.u('M2'))) m where m->>'body' = 'Ready to roll!'), 'chat');
select testkit.assert_eq('system lines have no sender', (select (m->'sender')::text from jsonb_array_elements(pg_temp.msgs(pg_temp.u('M2'))) m where m->>'body' = 'CvM2 is ready.' limit 1), 'null');
select pg_temp.j(pg_temp.u('M1'), format($q$select post_convoy_message(%L)::text$q$, '<size=900>big</size>' || chr(7) || ' tail'));
select testkit.assert_eq('angle brackets and control characters are stripped', (select count(*)::text from jsonb_array_elements(pg_temp.msgs(pg_temp.u('L'))) m where m->>'body' ~ '[<>]' or m->>'body' ~ '[\x01-\x1f]'), '0');
select testkit.assert_eq('...the text itself survives', (select count(*)::text from jsonb_array_elements(pg_temp.msgs(pg_temp.u('L'))) m where m->>'body' like 'size=900big/size%tail'), '1');
select testkit.assert_eq('paging: after the last id nothing is returned', jsonb_array_length(pg_temp.msgs(pg_temp.u('L'), (select max(id) from convoy_messages)))::text, '0');
select testkit.assert_eq('paging: lines are in order', (select (array_agg((m->>'id')::bigint order by ord) = array_agg((m->>'id')::bigint order by (m->>'id')::bigint))::text from jsonb_array_elements(pg_temp.msgs(pg_temp.u('L'))) with ordinality t(m, ord)), 'true');
-- other convoys never see these lines
insert into fx select 'cv2', testkit.as_user(pg_temp.u('X1'), $$select create_convoy('Other Convoy')$$);
select testkit.assert_eq('another convoy does not see this convoy''s chat', pg_temp.has_msg(pg_temp.u('X1'), 'Ready to roll!'), 'false');
select testkit.expect_error('authenticated', pg_temp.u('L'), 'select * from convoy_messages', 'permission denied');
select testkit.expect_error('authenticated', pg_temp.u('L'), $$insert into convoy_messages(convoy_id, kind, body) values (gen_random_uuid(), 'system', 'x')$$, 'permission denied');
-- retention: the last 200 lines per convoy
insert into convoy_messages(convoy_id, sender_id, kind, body) select (select v::uuid from fx where k = 'cv'), pg_temp.u('M1'), 'chat', 'filler ' || g from generate_series(1, 250) g;
select pg_temp.j(pg_temp.u('M2'), $$select post_convoy_message('after the flood')::text$$);
select testkit.assert_eq('only the last 200 lines are kept', (select count(*)::text from convoy_messages where convoy_id = (select v::uuid from fx where k = 'cv')), '200');
select testkit.assert_eq('first load returns the newest lines', pg_temp.has_msg(pg_temp.u('L'), 'after the flood'), 'true');
select testkit.assert_eq('first load is capped at the page size', jsonb_array_length(pg_temp.msgs(pg_temp.u('L')))::text, '100');
select testkit.assert_eq('first load does not start at the oldest line', pg_temp.has_msg(pg_temp.u('L'), 'filler 51'), 'false');
select testkit.assert_eq('polling returns the lines after an id, oldest first', (select (m->>'body') from jsonb_array_elements(testkit.as_user(pg_temp.u('L'), format('select list_convoy_messages(%s, 1)::text', (select min(id) from convoy_messages where convoy_id = (select v::uuid from fx where k = 'cv'))))::jsonb->'items') m limit 1),
  (select body from convoy_messages where convoy_id = (select v::uuid from fx where k = 'cv') and id > (select min(id) from convoy_messages where convoy_id = (select v::uuid from fx where k = 'cv')) order by id limit 1));
-- rate limit: 20 lines per 30 s (own bucket)
do $$ declare i int; v_err text := null;
begin
  for i in 1..25 loop
    begin perform testkit.as_user((select v::uuid from fx where k = 'M2'), format($q$select post_convoy_message('spam %s')::text$q$, i));
    exception when others then v_err := sqlerrm; exit; end;
  end loop;
  if v_err is distinct from 'rate_limited' then raise exception 'ASSERT chat flood: expected rate_limited, got %', coalesce(v_err, 'no error'); end if;
end $$;

-- ===== finding convoys
update convoys set capacity = 2 where id = (select v::uuid from fx where k = 'cv2');
select testkit.as_user(pg_temp.u('X2'), format('select join_convoy(%L)', (select v from fx where k = 'cv2')));     -- cv2 is now full
insert into fx select 'cv3', testkit.as_user(pg_temp.u('S'), $$select create_convoy('Hidden Run')$$);
select pg_temp.j(pg_temp.u('S'), 'select set_convoy_options(null, true)::text');
create function pg_temp.open(p_uid uuid, p_args text default '') returns jsonb language sql as $$
  select testkit.as_user(p_uid, format('select list_open_convoys(%s)::text', p_args))::jsonb $$;
create function pg_temp.names(p_uid uuid, p_args text default '') returns text language sql as $$
  select coalesce(string_agg(e->>'name', ',' order by e->>'name'), 'none') from jsonb_array_elements(pg_temp.open(p_uid, p_args)->'items') e $$;
select testkit.assert_eq('open list: private convoys are hidden', pg_temp.names(pg_temp.u('F')), 'Lobby Run,Other Convoy');
select testkit.assert_eq('open list: total', pg_temp.open(pg_temp.u('F'))->>'total', '2');
select pg_temp.j(pg_temp.u('L'), $$select set_convoy_route('lagos-ibadan')::text$$);
select testkit.assert_eq('open list: route name is included', (select e->'route'->>'name' from jsonb_array_elements(pg_temp.open(pg_temp.u('F'))->'items') e where e->>'name' = 'Lobby Run'), (select name from world_routes where code = 'lagos-ibadan'));
select testkit.assert_eq('open list: tags are included', (select e->>'tags' from jsonb_array_elements(pg_temp.open(pg_temp.u('F'))->'items') e where e->>'name' = 'Lobby Run'), '["chill_drive", "jobs"]');
select testkit.assert_eq('open list: joinable', (select e->>'joinable' from jsonb_array_elements(pg_temp.open(pg_temp.u('F'))->'items') e where e->>'name' = 'Lobby Run'), 'true');
select testkit.assert_eq('open list: a full convoy is not joinable', (select e->>'joinable' from jsonb_array_elements(pg_temp.open(pg_temp.u('F'))->'items') e where e->>'name' = 'Other Convoy'), 'false');
select testkit.assert_eq('search by convoy name', pg_temp.names(pg_temp.u('F'), $$20, 0, 'lobby'$$), 'Lobby Run');
select testkit.assert_eq('search by leader name', pg_temp.names(pg_temp.u('F'), $$20, 0, 'cvx1'$$), 'Other Convoy');
select testkit.assert_eq('search by route name', pg_temp.names(pg_temp.u('F'), format($q$20, 0, %L$q$, lower(split_part((select name from world_routes where code = 'lagos-ibadan'), ' ', 1)))), 'Lobby Run');
select testkit.assert_eq('% is a literal', pg_temp.names(pg_temp.u('F'), $$20, 0, '%'$$), 'none');
select testkit.assert_eq('tag filter', pg_temp.names(pg_temp.u('F'), $$20, 0, null, 'jobs'$$), 'Lobby Run');
select testkit.assert_eq('tag filter with no match', pg_temp.names(pg_temp.u('F'), $$20, 0, null, 'events'$$), 'none');
select testkit.expect_error('authenticated', pg_temp.u('F'), $$select list_open_convoys(20, 0, null, 'bogus')$$, 'invalid_tag');
select testkit.expect_error('authenticated', pg_temp.u('F'), format('select list_open_convoys(20, 0, %L)', repeat('y', 41)), 'invalid_search');
select testkit.assert_eq('pagination', jsonb_array_length(pg_temp.open(pg_temp.u('F'), '1, 1')->'items')::text, '1');
select testkit.assert_eq('pagination: total ignores the page', pg_temp.open(pg_temp.u('F'), '1, 1')->>'total', '2');
select testkit.expect_error('anon', null, 'select list_open_convoys()', 'permission denied');
-- lobbies sort before convoys already on the road
select pg_temp.j(pg_temp.u('M1'), 'select set_my_ready(true)::text'); select pg_temp.j(pg_temp.u('M2'), 'select set_my_ready(true)::text');
select pg_temp.j(pg_temp.u('L'), 'select start_convoy_drive()::text');
update convoys set created_at = now() + interval '1 hour' where id = (select v::uuid from fx where k = 'cv');      -- the driving convoy is the NEWER one, so only the phase can put it second
select testkit.assert_eq('driving convoys list as in progress, after the lobbies', (select string_agg(e->>'name' || ':' || (e->>'phase'), ',' order by ord) from jsonb_array_elements(pg_temp.open(pg_temp.u('F'))->'items') with ordinality t(e, ord)), 'Other Convoy:lobby,Lobby Run:driving');

-- ===== active players and server status
select testkit.as_user(pg_temp.u(k), 'select start_session()::text') from (values ('L'),('M1'),('S'),('F'),('X1')) t(k);
select testkit.as_user(pg_temp.u('M2'), 'select start_session()::text');
update game_sessions set last_seen_at = now() - interval '10 minutes' where player_id = pg_temp.u('M2');           -- expired: offline
create function pg_temp.players(p_uid uuid, p_args text default '') returns jsonb language sql as $$
  select testkit.as_user(p_uid, format('select list_active_players(%s)::text', p_args))::jsonb $$;
create function pg_temp.pnames(p_uid uuid, p_args text default '') returns text language sql as $$
  select coalesce(string_agg(e->>'display_name', ',' order by e->>'display_name'), 'none') from jsonb_array_elements(pg_temp.players(p_uid, p_args)->'items') e $$;
select testkit.assert_eq('active players: live sessions only, never myself', pg_temp.pnames(pg_temp.u('F')), 'CvL,CvM1,CvS,CvX1');
select testkit.assert_eq('active players: an expired session is offline', (pg_temp.pnames(pg_temp.u('L')) like '%CvM2%')::text, 'false');
select testkit.assert_eq('active players: total', pg_temp.players(pg_temp.u('F'))->>'total', '4');
select testkit.assert_eq('active players: search', pg_temp.pnames(pg_temp.u('F'), $$30, 0, 'cvm'$$), 'CvM1');
select testkit.assert_eq('active players: % is a literal', pg_temp.pnames(pg_temp.u('F'), $$30, 0, '%'$$), 'none');
select testkit.expect_error('authenticated', pg_temp.u('F'), format('select list_active_players(30, 0, %L)', repeat('z', 41)), 'invalid_search');
select testkit.assert_eq('active players: level is the server''s', (select e->>'level' from jsonb_array_elements(pg_temp.players(pg_temp.u('F'))->'items') e where e->>'display_name' = 'CvL'), '1');
select testkit.assert_eq('active players: vehicle name is included', ((select e->>'vehicle' from jsonb_array_elements(pg_temp.players(pg_temp.u('F'))->'items') e where e->>'display_name' = 'CvL') is not null)::text, 'true');
select testkit.assert_eq('active players: convoy membership is flagged', (select e->>'in_convoy' from jsonb_array_elements(pg_temp.players(pg_temp.u('F'))->'items') e where e->>'display_name' = 'CvL'), 'true');
select testkit.assert_eq('active players: strangers do not see activity', (select (e->'activity')::text from jsonb_array_elements(pg_temp.players(pg_temp.u('F'))->'items') e where e->>'display_name' = 'CvL'), 'null');
insert into fx select 'fr', pg_temp.j(pg_temp.u('L'), format($q$select send_friend_request(%L)::text$q$, pg_temp.u('F')))->>'request_id';
select testkit.as_user(pg_temp.u('F'), format($q$select respond_friend_request(%L, true)$q$, (select v from fx where k = 'fr')));
select testkit.assert_eq('active players: friends see activity', (select e->>'friend_status' from jsonb_array_elements(pg_temp.players(pg_temp.u('F'))->'items') e where e->>'display_name' = 'CvL'), 'friends');
select testkit.assert_eq('active players: friend activity is shown', ((select e->>'activity' from jsonb_array_elements(pg_temp.players(pg_temp.u('F'))->'items') e where e->>'display_name' = 'CvL') is not null)::text, 'true');
select testkit.assert_eq('active players: page size is clamped', (select jsonb_array_length(pg_temp.players(pg_temp.u('F'), '100000, 0')->'items') <= 50)::text, 'true');
select testkit.expect_error('anon', null, 'select list_active_players()', 'permission denied');
select testkit.expect_error('authenticated', null, 'select list_active_players()', 'not_authenticated');
select testkit.assert_eq('server status: players online counts live sessions', pg_temp.j(pg_temp.u('F'), 'select get_server_status()::text')->>'players_online', (select count(distinct player_id)::text from game_sessions where ended_at is null and last_seen_at > now() - interval '90 seconds'));
select testkit.assert_eq('server status: active convoys', pg_temp.j(pg_temp.u('F'), 'select get_server_status()::text')->>'active_convoys', (select count(*)::text from convoys where disbanded_at is null));
select testkit.assert_eq('server status: convoys driving', pg_temp.j(pg_temp.u('F'), 'select get_server_status()::text')->>'convoys_driving', '1');
select testkit.expect_error('anon', null, 'select get_server_status()', 'permission denied');

-- ===== leaving
select testkit.as_user(pg_temp.u('L'), 'select leave_convoy()');
select testkit.assert_eq('leadership passes on', pg_temp.cv(pg_temp.u('M1'))->>'leader_id', (select leader_id::text from convoys where id = (select v::uuid from fx where k = 'cv')));
select testkit.assert_eq('system line: left', pg_temp.has_msg(pg_temp.u('M1'), 'CvL left the convoy.'), 'true');
select testkit.expect_error('authenticated', pg_temp.u('L'), 'select list_convoy_messages()', 'not_in_convoy');

select 'CONVOY LOBBY TESTS PASSED' as result;
