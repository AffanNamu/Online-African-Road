\set ON_ERROR_STOP on
-- Convoy settings (driving mode, entry rank, password), atomic create, disband, member invites, chat reports and blocks, nearby players, server list.
create function pg_temp.j(p_uid uuid, p_sql text) returns jsonb language sql as $$ select testkit.as_user(p_uid, p_sql)::jsonb $$;
create temp table fx(k text primary key, v text);
insert into fx select k, testkit.make_user(n)::text from (values ('L','SpL'),('M1','SpM1'),('M2','SpM2'),('F','SpF'),('S','SpS'),('G','SpG'),('H','SpH'),('Z','SpZ')) t(k, n);
create function pg_temp.u(k text) returns uuid language sql as $$ select v::uuid from fx where fx.k = $1 $$;
create function pg_temp.cv(p_uid uuid) returns jsonb language sql as $$ select testkit.as_user(p_uid, 'select get_my_convoy()::text')::jsonb->'convoy' $$;
create function pg_temp.msgs(p_uid uuid) returns jsonb language sql as $$ select testkit.as_user(p_uid, 'select list_convoy_messages(0, 100)::text')::jsonb->'items' $$;
update convoys set disbanded_at = now() where disbanded_at is null;
update game_sessions set ended_at = now() where ended_at is null;
select admin_set_route('lagos-ibadan', 'OPEN', 1);
update profiles set level = 6 where id = pg_temp.u('L');
create temp table planned as select code from world_routes where status = 'PLANNED' order by code limit 1;

-- ===== atomic create with every setting
select testkit.expect_error('authenticated', pg_temp.u('L'), $$select create_convoy_lobby('ab')$$, 'invalid_name');
select testkit.expect_error('authenticated', pg_temp.u('L'), $$select create_convoy_lobby('Night Run', null, false, 1)$$, 'invalid_capacity');
select testkit.expect_error('authenticated', pg_temp.u('L'), $$select create_convoy_lobby('Night Run', null, false, 9)$$, 'invalid_capacity');
select testkit.expect_error('authenticated', pg_temp.u('L'), $$select create_convoy_lobby('Night Run', null, false, 8, 'turbo')$$, 'invalid_driving_mode');
select testkit.expect_error('authenticated', pg_temp.u('L'), $$select create_convoy_lobby('Night Run', null, false, 8, 'relaxed', 7)$$, 'invalid_min_level');
select testkit.expect_error('authenticated', pg_temp.u('L'), $$select create_convoy_lobby('Night Run', null, false, 8, 'relaxed', 0)$$, 'invalid_min_level');
select testkit.expect_error('authenticated', pg_temp.u('L'), $$select create_convoy_lobby('Night Run', null, false, 8, 'relaxed', 1, 'en', 'abc')$$, 'invalid_password');
select testkit.expect_error('authenticated', pg_temp.u('L'), $$select create_convoy_lobby('Night Run', null, false, 8, 'relaxed', 1, 'en', null, array['hacking'])$$, 'invalid_tags');
select testkit.expect_error('authenticated', pg_temp.u('L'), $$select create_convoy_lobby('Night Run', null, false, 8, 'relaxed', 1, 'en', null, array['jobs','freight','training','friendly'])$$, 'invalid_tags');
select testkit.expect_error('authenticated', pg_temp.u('L'), $$select create_convoy_lobby('Night Run', null, false, 8, 'relaxed', 1, 'english')$$, 'invalid_tags');
select testkit.expect_error('authenticated', pg_temp.u('L'), $$select create_convoy_lobby('Night Run', 'nowhere')$$, 'route_not_found');
select testkit.expect_error('authenticated', pg_temp.u('L'), format('select create_convoy_lobby(%L, %L)', 'Night Run', (select code from planned)), 'route_unavailable');
select testkit.assert_eq('a refused create leaves no convoy', (select count(*)::text from convoy_members where player_id = pg_temp.u('L')), '0');
select testkit.expect_error('anon', null, $$select create_convoy_lobby('Night Run')$$, 'permission denied');
select testkit.expect_error('authenticated', null, $$select create_convoy_lobby('Night Run')$$, 'not_authenticated');
insert into fx select 'cv', testkit.as_user(pg_temp.u('L'), $$select create_convoy_lobby('Lagos Ibadan Night Run', 'lagos-ibadan', false, 4, 'realistic', 3, 'fr', 'secret42', array['jobs','chill_drive'])$$);
select testkit.assert_eq('settings: route', pg_temp.cv(pg_temp.u('L'))->'route'->>'code', 'lagos-ibadan');
select testkit.assert_eq('settings: capacity', pg_temp.cv(pg_temp.u('L'))->>'capacity', '4');
select testkit.assert_eq('settings: driving mode', pg_temp.cv(pg_temp.u('L'))->>'driving_mode', 'realistic');
select testkit.assert_eq('settings: entry rank', pg_temp.cv(pg_temp.u('L'))->>'min_level', '3');
select testkit.assert_eq('settings: language', pg_temp.cv(pg_temp.u('L'))->>'language', 'fr');
select testkit.assert_eq('settings: tags', pg_temp.cv(pg_temp.u('L'))->>'tags', '["chill_drive", "jobs"]');
select testkit.assert_eq('settings: password is reported only as a flag', pg_temp.cv(pg_temp.u('L'))->>'has_password', 'true');
select testkit.assert_eq('the password never appears in any reply', (pg_temp.cv(pg_temp.u('L'))::text like '%secret42%')::text, 'false');
select testkit.assert_eq('the password is stored salted and hashed', (select (password_hash <> 'secret42' and password_salt is not null and char_length(password_hash) = 64)::text from convoys where id = (select v::uuid from fx where k = 'cv')), 'true');
select testkit.expect_error('authenticated', pg_temp.u('L'), $$select create_convoy_lobby('Second Convoy')$$, 'already_in_convoy_or_code_taken');
select testkit.assert_eq('the creator is the sole member and leader', pg_temp.cv(pg_temp.u('L'))->>'member_count', '1');

-- ===== joining: level, password
update profiles set level = 3 where id in (pg_temp.u('M1'), pg_temp.u('M2'), pg_temp.u('F'), pg_temp.u('G'), pg_temp.u('H'));
select testkit.expect_error('authenticated', pg_temp.u('S'), format('select join_convoy(%L)', (select v from fx where k = 'cv')), 'level_too_low');
select testkit.expect_error('authenticated', pg_temp.u('M1'), format('select join_convoy(%L)', (select v from fx where k = 'cv')), 'password_required');
select testkit.assert_eq('a wrong password is answered, not thrown (so the attempt is counted)', pg_temp.j(pg_temp.u('M1'), format('select join_convoy_with_password(%L, %L)::text', (select v from fx where k = 'cv'), 'wrong'))->>'error', 'wrong_password');
select testkit.assert_eq('no password given is a wrong password', pg_temp.j(pg_temp.u('M1'), format('select join_convoy_with_password(%L, null)::text', (select v from fx where k = 'cv')))->>'joined', 'false');
select testkit.expect_error('authenticated', pg_temp.u('S'), format('select join_convoy_with_password(%L, %L)', (select v from fx where k = 'cv'), 'secret42'), 'level_too_low');
select testkit.assert_eq('a failed attempt joins nothing', (select count(*)::text from convoy_members where player_id = pg_temp.u('M1')), '0');
select testkit.assert_eq('the right password joins', pg_temp.j(pg_temp.u('M1'), format('select join_convoy_with_password(%L, %L)::text', (select v from fx where k = 'cv'), 'secret42'))->>'joined', 'true');
select testkit.assert_eq('...and M1 is in', pg_temp.cv(pg_temp.u('M1'))->>'member_count', '2');
select testkit.expect_error('anon', null, format('select join_convoy_with_password(%L, %L)', (select v from fx where k = 'cv'), 'x'), 'permission denied');
select testkit.assert_eq('the password bypass does not outlive the call', coalesce(current_setting('aro.convoy_pw_ok', true), ''), '');
-- a password cannot be guessed endlessly
do $$ declare i int; v_err text := null;
begin
  for i in 1..12 loop
    begin perform testkit.as_user((select v::uuid from fx where k = 'M2'), format($q$select join_convoy_with_password(%L, %L)::text$q$, (select v from fx where k = 'cv'), 'guess' || i));
    exception when others then v_err := sqlerrm; if v_err = 'rate_limited' then exit; end if; end;
  end loop;
  if v_err is distinct from 'rate_limited' then raise exception 'ASSERT password guessing: expected rate_limited, got %', coalesce(v_err, 'no error'); end if;
end $$;
select testkit.assert_eq('even the right password is refused while rate limited', (select count(*)::text from convoy_members where player_id = pg_temp.u('M2')), '0');
-- an accepted invite is the leader's own invitation: no password needed
select testkit.as_user(pg_temp.u('L'), format('select invite_to_convoy(%L)', pg_temp.u('F')));
insert into fx select 'invF', (pg_temp.j(pg_temp.u('F'), 'select list_convoy_invites()::text')->'items'->0->>'invite_id');
select testkit.as_user(pg_temp.u('F'), format($q$select respond_convoy_invite(%L, true)$q$, (select v from fx where k = 'invF')));
select testkit.assert_eq('an invited player joins without the password', pg_temp.cv(pg_temp.u('F'))->>'member_count', '3');

-- ===== settings changes (leader only, lobby only)
select testkit.expect_error('authenticated', pg_temp.u('M1'), $$select set_convoy_settings('relaxed')$$, 'not_leader');
select testkit.expect_error('authenticated', pg_temp.u('L'), $$select set_convoy_settings('turbo')$$, 'invalid_driving_mode');
select testkit.expect_error('authenticated', pg_temp.u('L'), $$select set_convoy_settings(null, 7)$$, 'invalid_min_level');
select testkit.expect_error('authenticated', pg_temp.u('L'), $$select set_convoy_settings(null, 4)$$, 'member_below_level');
select testkit.expect_error('authenticated', pg_temp.u('L'), $$select set_convoy_settings(null, null, 'english')$$, 'invalid_language');
select testkit.expect_error('authenticated', pg_temp.u('L'), $$select set_convoy_settings(null, null, null, 'abc')$$, 'invalid_password');
select testkit.assert_eq('mode changes', pg_temp.j(pg_temp.u('L'), $$select set_convoy_settings('relaxed')::text$$)->>'driving_mode', 'relaxed');
select testkit.assert_eq('rank changes', pg_temp.j(pg_temp.u('L'), $$select set_convoy_settings(null, 2)::text$$)->>'min_level', '2');
select testkit.assert_eq('language changes', pg_temp.j(pg_temp.u('L'), $$select set_convoy_settings(null, null, 'ha')::text$$)->>'language', 'ha');
select testkit.assert_eq('password can be removed', pg_temp.j(pg_temp.u('L'), $$select set_convoy_settings(null, null, null, null, true)::text$$)->>'has_password', 'false');
select testkit.assert_eq('...and with it the join check', (select (password_hash is null and password_salt is null)::text from convoys where id = (select v::uuid from fx where k = 'cv')), 'true');
select testkit.assert_eq('a new password can be set', pg_temp.j(pg_temp.u('L'), $$select set_convoy_settings(null, null, null, 'newpass9')::text$$)->>'has_password', 'true');
select testkit.expect_error('authenticated', pg_temp.u('G'), format('select join_convoy(%L)', (select v from fx where k = 'cv')), 'password_required');
select testkit.assert_eq('null settings change nothing', pg_temp.j(pg_temp.u('L'), 'select set_convoy_settings()::text')->>'language', 'ha');
update profiles set level = 3 where id = pg_temp.u('L');
select pg_temp.j(pg_temp.u('L'), 'select set_convoy_settings(null, 1, null, null, true)::text');
select pg_temp.j(pg_temp.u('L'), $$select set_convoy_route('lagos-ibadan')::text$$);
select testkit.as_user(pg_temp.u(k), 'select start_session()::text') from (values ('L'),('M1'),('F')) t(k);
select pg_temp.j(pg_temp.u('M1'), 'select set_my_ready(true)::text'); select pg_temp.j(pg_temp.u('F'), 'select set_my_ready(true)::text');
select pg_temp.j(pg_temp.u('L'), 'select start_convoy_drive()::text');
select testkit.expect_error('authenticated', pg_temp.u('L'), $$select set_convoy_settings('realistic')$$, 'convoy_driving');
select testkit.assert_eq('language may change while driving', pg_temp.j(pg_temp.u('L'), $$select set_convoy_settings(null, null, 'en')::text$$)->>'language', 'en');
select pg_temp.j(pg_temp.u('L'), 'select end_convoy_drive()::text');
update profiles set level = 6 where id = pg_temp.u('L');

-- ===== connection state and the invited list in the convoy reply
select testkit.as_user(pg_temp.u('M1'), 'select start_session()::text'); update game_sessions set ended_at = now() where player_id = pg_temp.u('F');
select testkit.assert_eq('a member with a live session is connected', (select m->>'connected' from jsonb_array_elements(pg_temp.cv(pg_temp.u('L'))->'members') m where m->>'display_name' = 'SpM1'), 'true');
select testkit.assert_eq('a member without one is not', (select m->>'connected' from jsonb_array_elements(pg_temp.cv(pg_temp.u('L'))->'members') m where m->>'display_name' = 'SpF'), 'false');
update game_sessions set last_seen_at = now() - interval '10 minutes' where player_id = pg_temp.u('M1');
select testkit.assert_eq('an expired session is a disconnected member', (select m->>'connected' from jsonb_array_elements(pg_temp.cv(pg_temp.u('L'))->'members') m where m->>'display_name' = 'SpM1'), 'false');
update profiles set level = 3 where id in (pg_temp.u('G'), pg_temp.u('H'));
select testkit.as_user(pg_temp.u('L'), format('select invite_to_convoy(%L)', pg_temp.u('G')));
select testkit.assert_eq('pending invites are listed with the player', pg_temp.cv(pg_temp.u('L'))->'invited'->0->>'display_name', 'SpG');
select testkit.assert_eq('...and only one is pending', jsonb_array_length(pg_temp.cv(pg_temp.u('L'))->'invited')::text, '1');

-- ===== members inviting friends
update convoys set capacity = 8, is_private = false where id = (select v::uuid from fx where k = 'cv');
select testkit.expect_error('authenticated', pg_temp.u('S'), format('select invite_friend_to_convoy(%L)', pg_temp.u('H')), 'not_in_convoy');
select testkit.expect_error('authenticated', pg_temp.u('M1'), format('select invite_friend_to_convoy(%L)', pg_temp.u('M1')), 'cannot_invite_self');
select testkit.expect_error('authenticated', pg_temp.u('M1'), 'select invite_friend_to_convoy(gen_random_uuid())', 'player_not_found');
select testkit.expect_error('authenticated', pg_temp.u('M1'), format('select invite_friend_to_convoy(%L)', pg_temp.u('H')), 'not_friends');
insert into fx select 'fr', pg_temp.j(pg_temp.u('M1'), format($q$select send_friend_request(%L)::text$q$, pg_temp.u('H')))->>'request_id';
select testkit.as_user(pg_temp.u('H'), format($q$select respond_friend_request(%L, true)$q$, (select v from fx where k = 'fr')));
select testkit.expect_error('authenticated', pg_temp.u('M1'), format('select invite_friend_to_convoy(%L)', pg_temp.u('F')), 'not_friends');
select testkit.as_user(pg_temp.u('H'), format('select block_player(%L)', pg_temp.u('M1')));
select testkit.expect_error('authenticated', pg_temp.u('M1'), format('select invite_friend_to_convoy(%L)', pg_temp.u('H')), 'player_blocked');
select testkit.as_user(pg_temp.u('H'), format('select unblock_player(%L)', pg_temp.u('M1')));
update profiles set level = 1 where id = pg_temp.u('H'); update convoys set min_level = 3 where id = (select v::uuid from fx where k = 'cv');
select testkit.expect_error('authenticated', pg_temp.u('M1'), format('select invite_friend_to_convoy(%L)', pg_temp.u('H')), 'level_too_low');
update profiles set level = 3 where id = pg_temp.u('H'); update convoys set min_level = 1 where id = (select v::uuid from fx where k = 'cv');
select testkit.assert_eq('a member invites a friend', (testkit.as_user(pg_temp.u('M1'), format('select invite_friend_to_convoy(%L)', pg_temp.u('H'))) is not null)::text, 'true');
select testkit.assert_eq('the friend sees the invite from the member', pg_temp.j(pg_temp.u('H'), 'select list_convoy_invites()::text')->'items'->0->'from'->>'display_name', 'SpM1');
select testkit.assert_eq('the friend is notified', (select count(*)::text from notifications where player_id = pg_temp.u('H') and title = 'Convoy invite'), '1');
select testkit.expect_error('authenticated', pg_temp.u('M1'), format('select invite_friend_to_convoy(%L)', pg_temp.u('H')), 'already_invited');
select testkit.expect_error('authenticated', pg_temp.u('M1'), format('select invite_friend_to_convoy(%L)', pg_temp.u('L')), 'not_friends');
update convoys set is_private = true where id = (select v::uuid from fx where k = 'cv');
select testkit.expect_error('authenticated', pg_temp.u('M1'), format('select invite_friend_to_convoy(%L)', pg_temp.u('H')), 'invite_leader_only');
select testkit.expect_error('anon', null, format('select invite_friend_to_convoy(%L)', pg_temp.u('H')), 'permission denied');
update convoys set is_private = false where id = (select v::uuid from fx where k = 'cv');

-- ===== chat reports and blocks
select pg_temp.j(pg_temp.u('M1'), $$select post_convoy_message('rude words')::text$$);
select pg_temp.j(pg_temp.u('F'), $$select post_convoy_message('hello all')::text$$);
create function pg_temp.mid(p_body text) returns bigint language sql as $$ select id from convoy_messages where body = p_body order by id desc limit 1 $$;
select testkit.expect_error('authenticated', pg_temp.u('L'), format('select report_chat_message(%s, %L)', pg_temp.mid('rude words'), 'bogus'), 'invalid_reason');
select testkit.expect_error('authenticated', pg_temp.u('S'), format('select report_chat_message(%s, %L)', pg_temp.mid('rude words'), 'abuse'), 'not_in_convoy');
select testkit.expect_error('authenticated', pg_temp.u('L'), 'select report_chat_message(999999999, $$abuse$$)', 'message_not_found');
select testkit.expect_error('authenticated', pg_temp.u('M1'), format('select report_chat_message(%s, %L)', pg_temp.mid('rude words'), 'abuse'), 'cannot_report_self');
select testkit.expect_error('authenticated', pg_temp.u('L'), format('select report_chat_message(%s, %L)', (select min(id) from convoy_messages where kind = 'system' and convoy_id = (select v::uuid from fx where k = 'cv')), 'abuse'), 'message_not_found');
select testkit.assert_eq('a report is received', pg_temp.j(pg_temp.u('L'), format($q$select report_chat_message(%s, 'abuse')::text$q$, pg_temp.mid('rude words')))->>'status', 'received');
select testkit.expect_error('authenticated', pg_temp.u('L'), format('select report_chat_message(%s, %L)', pg_temp.mid('rude words'), 'spam'), 'already_reported');
select testkit.assert_eq('one report does not hide a line', (select moderation from convoy_messages where id = pg_temp.mid('rude words')), 'ok');
select testkit.assert_eq('a second report neither', pg_temp.j(pg_temp.u('F'), format($q$select report_chat_message(%s, 'harassment')::text$q$, pg_temp.mid('rude words')))->>'moderation', 'ok');
update convoys set password_hash = null, password_salt = null where id = (select v::uuid from fx where k = 'cv');
select testkit.as_user(pg_temp.u('G'), format('select join_convoy(%L)', (select v from fx where k = 'cv')));
select testkit.assert_eq('three different reporters hide the line', pg_temp.j(pg_temp.u('G'), format($q$select report_chat_message(%s, 'abuse')::text$q$, pg_temp.mid('rude words')))->>'moderation', 'hidden');
select testkit.assert_eq('a hidden line keeps its place but loses its text', (select ((m->>'body') is null)::text from jsonb_array_elements(pg_temp.msgs(pg_temp.u('L'))) m where (m->>'id')::bigint = pg_temp.mid('rude words')), 'true');
select testkit.assert_eq('...and says why', (select m->>'moderation' from jsonb_array_elements(pg_temp.msgs(pg_temp.u('L'))) m where (m->>'id')::bigint = pg_temp.mid('rude words')), 'hidden');
select testkit.assert_eq('a clean line is untouched', (select m->>'moderation' from jsonb_array_elements(pg_temp.msgs(pg_temp.u('L'))) m where m->>'body' = 'hello all'), 'ok');
-- blocking
select testkit.expect_error('authenticated', pg_temp.u('L'), format('select block_player(%L)', pg_temp.u('L')), 'cannot_block_self');
select testkit.expect_error('authenticated', pg_temp.u('L'), 'select block_player(gen_random_uuid())', 'player_not_found');
select testkit.as_user(pg_temp.u('L'), format('select block_player(%L)', pg_temp.u('F')));
select testkit.as_user(pg_temp.u('L'), format('select block_player(%L)', pg_temp.u('F')));
select testkit.assert_eq('blocking twice is harmless', (select count(*)::text from player_blocks where blocker_id = pg_temp.u('L')), '1');
select testkit.assert_eq('a blocked player''s lines disappear for the blocker', (select count(*)::text from jsonb_array_elements(pg_temp.msgs(pg_temp.u('L'))) m where m->>'body' = 'hello all'), '0');
select testkit.assert_eq('...but not for anyone else', (select count(*)::text from jsonb_array_elements(pg_temp.msgs(pg_temp.u('M1'))) m where m->>'body' = 'hello all'), '1');
select testkit.assert_eq('the blocked list names them', pg_temp.j(pg_temp.u('L'), 'select list_blocked_players()::text')->'items'->0->>'display_name', 'SpF');
select testkit.assert_eq('nobody else sees the list', jsonb_array_length(pg_temp.j(pg_temp.u('M1'), 'select list_blocked_players()::text')->'items')::text, '0');
select testkit.as_user(pg_temp.u('L'), format('select unblock_player(%L)', pg_temp.u('F')));
select testkit.assert_eq('unblocking restores the lines', (select count(*)::text from jsonb_array_elements(pg_temp.msgs(pg_temp.u('L'))) m where m->>'body' = 'hello all'), '1');
select testkit.expect_error('authenticated', null, 'select block_player(gen_random_uuid())', 'not_authenticated');
select testkit.expect_error('anon', null, 'select list_blocked_players()', 'permission denied');
select testkit.expect_error('authenticated', pg_temp.u('L'), 'select * from player_blocks', 'permission denied');
select testkit.expect_error('authenticated', pg_temp.u('L'), 'select * from chat_reports', 'permission denied');

-- ===== disband
select testkit.expect_error('authenticated', pg_temp.u('M1'), 'select disband_convoy()', 'not_leader');
select testkit.expect_error('authenticated', pg_temp.u('S'), 'select disband_convoy()', 'not_leader');
select testkit.expect_error('anon', null, 'select disband_convoy()', 'permission denied');
select testkit.as_user(pg_temp.u('L'), 'select disband_convoy()');
select testkit.assert_eq('the convoy is disbanded', (select (disbanded_at is not null)::text from convoys where id = (select v::uuid from fx where k = 'cv')), 'true');
select testkit.assert_eq('every member is out', (select count(*)::text from convoy_members where convoy_id = (select v::uuid from fx where k = 'cv')), '0');
select testkit.assert_eq('members have no convoy', (pg_temp.cv(pg_temp.u('M1')))::text, 'null');
select testkit.assert_eq('members are told', (select count(*)::text from notifications where title = 'Convoy disbanded' and player_id in (pg_temp.u('M1'), pg_temp.u('F'), pg_temp.u('G'))), '3');
select testkit.assert_eq('the leader is not told about their own act', (select count(*)::text from notifications where title = 'Convoy disbanded' and player_id = pg_temp.u('L')), '0');
select testkit.assert_eq('pending invites are revoked', (select count(*)::text from convoy_invites where convoy_id = (select v::uuid from fx where k = 'cv') and status = 'pending'), '0');
select testkit.assert_eq('analytics: convoy_disbanded', (select count(*)::text from analytics_events where name = 'convoy_disbanded' and player_id = pg_temp.u('L')), '1');
select testkit.assert_eq('a disbanded convoy is not listed', (select count(*)::text from jsonb_array_elements(testkit.as_user(pg_temp.u('S'), 'select list_open_convoys()::text')::jsonb->'items') e where e->>'name' = 'Lagos Ibadan Night Run'), '0');
select testkit.expect_error('authenticated', pg_temp.u('S'), format('select join_convoy(%L)', (select v from fx where k = 'cv')), 'convoy_not_found');
select testkit.assert_eq('the leader can create a new convoy afterwards', (testkit.as_user(pg_temp.u('L'), $$select create_convoy_lobby('Second Night Run')$$) is not null)::text, 'true');

-- ===== nearby players
update game_sessions set ended_at = now() where ended_at is null;
create function pg_temp.near(p_uid uuid) returns jsonb language sql as $$ select testkit.as_user(p_uid, 'select list_nearby_players()::text')::jsonb $$;
create function pg_temp.nnames(p_uid uuid) returns text language sql as $$ select coalesce(string_agg(e->>'display_name', ',' order by e->>'display_name'), 'none') from jsonb_array_elements(pg_temp.near(p_uid)->'items') e $$;
select testkit.assert_eq('no live session: no scope, no list', pg_temp.near(pg_temp.u('S'))->>'scope', 'none');
select testkit.as_user(pg_temp.u(k), 'select start_session()::text') from (values ('S'),('M1'),('F'),('G'),('H'),('Z')) t(k);
select testkit.assert_eq('a session without a place has no scope', pg_temp.near(pg_temp.u('S'))->>'scope', 'none');
create function pg_temp.hb(p_k text, p_route text, p_city text) returns void language plpgsql as $$
declare v_sess uuid;
begin
  select id into v_sess from game_sessions where player_id = pg_temp.u(p_k) and ended_at is null order by started_at desc limit 1;
  perform testkit.as_user(pg_temp.u(p_k), format($q$select heartbeat(%L, 'driving', %L, %L)::text$q$, v_sess, p_route, p_city));
end $$;
select pg_temp.hb('S', 'lagos-ibadan', 'lagos'); select pg_temp.hb('M1', 'lagos-ibadan', 'lagos'); select pg_temp.hb('F', null, 'lagos'); select pg_temp.hb('G', 'lagos-ibadan', 'ibadan'); select pg_temp.hb('H', 'abuja-kaduna', 'abuja');
-- Players Nearby is opt-in (20261012000007): a new account is in nobody's list until it says so
select testkit.assert_eq('a new account is hidden by default', (select string_agg(appear_nearby::text, ',') from profiles where id in (pg_temp.u('S'), pg_temp.u('M1'), pg_temp.u('F'), pg_temp.u('G'), pg_temp.u('H'), pg_temp.u('Z'))), 'false,false,false,false,false,false');
select testkit.assert_eq('nobody who has not opted in is listed, even on the same route', pg_temp.nnames(pg_temp.u('S')), 'none');
select testkit.assert_eq('the default is also shown to its owner', pg_temp.near(pg_temp.u('S'))->>'visible', 'false');
select testkit.assert_eq('opting in is explicit and answers true', testkit.as_user(pg_temp.u('S'), 'select set_nearby_visibility(true)::text'), 'true');
select testkit.as_user(pg_temp.u(k), 'select set_nearby_visibility(true)') from (values ('M1'),('F'),('G'),('H'),('Z')) t(k);
select testkit.assert_eq('route scope', pg_temp.near(pg_temp.u('S'))->>'scope', 'route');
select testkit.assert_eq('route scope names the route', pg_temp.near(pg_temp.u('S'))->>'scope_name', 'lagos-ibadan');
select testkit.assert_eq('nearby = same route (not myself, not another route)', pg_temp.nnames(pg_temp.u('S')), 'SpG,SpM1');
select testkit.assert_eq('city scope when there is no route', pg_temp.near(pg_temp.u('F'))->>'scope', 'city');
select testkit.assert_eq('nearby = same city', pg_temp.nnames(pg_temp.u('F')), 'SpM1,SpS');
select testkit.assert_eq('nearby: level, vehicle and online are the server''s', (select (e->>'level' = '3' and e->>'vehicle' is not null and e->>'online' = 'true')::text from jsonb_array_elements(pg_temp.near(pg_temp.u('S'))->'items') e where e->>'display_name' = 'SpG'), 'true');
select testkit.assert_eq('nearby: no coordinates or distances are invented', (pg_temp.near(pg_temp.u('S'))::text ~ '(distance|world_x|lat|lng)')::text, 'false');
select testkit.assert_eq('nearby: a player in no convoy has a null convoy', (select (e->'convoy')::text from jsonb_array_elements(pg_temp.near(pg_temp.u('S'))->'items') e where e->>'display_name' = 'SpG'), 'null');
insert into fx select 'cv4', testkit.as_user(pg_temp.u('G'), $$select create_convoy_lobby('Ibadan Crew', null, false, 5, 'relaxed', 1, 'en', 'pw1234')$$);
select testkit.assert_eq('nearby: a public convoy is offered with its rules', (select e->'convoy'->>'name' || '/' || (e->'convoy'->>'has_password') || '/' || (e->'convoy'->>'joinable') from jsonb_array_elements(pg_temp.near(pg_temp.u('S'))->'items') e where e->>'display_name' = 'SpG'), 'Ibadan Crew/true/true');
select pg_temp.j(pg_temp.u('G'), 'select set_convoy_options(null, true)::text');
select testkit.assert_eq('nearby: a private convoy is not offered', (select (e->'convoy')::text from jsonb_array_elements(pg_temp.near(pg_temp.u('S'))->'items') e where e->>'display_name' = 'SpG'), 'null');
select testkit.assert_eq('opting out removes me from nearby lists', (testkit.as_user(pg_temp.u('G'), 'select set_nearby_visibility(false)::text')), 'false');
select testkit.assert_eq('...they are gone', pg_temp.nnames(pg_temp.u('S')), 'SpM1');
select testkit.assert_eq('the opt-out is shown to its owner', pg_temp.near(pg_temp.u('G'))->>'visible', 'false');
select testkit.as_user(pg_temp.u('G'), 'select set_nearby_visibility(true)');
select testkit.as_user(pg_temp.u('S'), format('select block_player(%L)', pg_temp.u('M1')));
select testkit.assert_eq('blocked players are not nearby', pg_temp.nnames(pg_temp.u('S')), 'SpG');
update game_sessions set last_seen_at = now() - interval '10 minutes' where player_id = pg_temp.u('G');
select testkit.assert_eq('offline players are not nearby', pg_temp.nnames(pg_temp.u('S')), 'none');
select testkit.expect_error('anon', null, 'select list_nearby_players()', 'permission denied');
select testkit.expect_error('authenticated', null, 'select list_nearby_players()', 'not_authenticated');
select testkit.expect_error('anon', null, 'select set_nearby_visibility(false)', 'permission denied');

-- ===== server list
update game_sessions set ended_at = now() where ended_at is null;
select testkit.as_user(pg_temp.u(k), 'select start_session()::text') from (values ('S'),('M1'),('F')) t(k);
create function pg_temp.srv() returns jsonb language sql as $$ select testkit.as_user(pg_temp.u('S'), 'select list_servers()::text')::jsonb->'items'->0 $$;
select testkit.assert_eq('the registered server is listed', pg_temp.srv()->>'name', 'African Roads Online');
select testkit.assert_eq('population is the live session count', pg_temp.srv()->>'population', '3');
select testkit.assert_eq('region is not invented', (pg_temp.srv()->'region')::text, 'null');
select testkit.assert_eq('capacity is not invented', (pg_temp.srv()->'capacity')::text, 'null');
select testkit.assert_eq('status: online', pg_temp.srv()->>'status', 'ONLINE');
select admin_set_server('main', null, 3, 'West Africa');
select testkit.assert_eq('region and capacity appear only when an operator sets them', pg_temp.srv()->>'region' || '/' || (pg_temp.srv()->>'capacity'), 'West Africa/3');
select testkit.assert_eq('status: full at capacity', pg_temp.srv()->>'status', 'FULL');
select admin_set_server('main', null, 100);
select testkit.assert_eq('status: back to online below capacity', pg_temp.srv()->>'status', 'ONLINE');
select admin_set_server('main', 'MAINTENANCE');
select testkit.assert_eq('status: maintenance', pg_temp.srv()->>'status', 'MAINTENANCE');
select admin_set_server('main', 'ONLINE');
select testkit.expect_error('postgres', null, $$select admin_set_server('ghost', 'ONLINE')$$, 'server_not_found');
select testkit.expect_error('authenticated', pg_temp.u('S'), $$select admin_set_server('main', 'MAINTENANCE')$$, 'permission denied');
select testkit.expect_error('authenticated', pg_temp.u('S'), 'select * from game_servers', 'permission denied');
select testkit.expect_error('anon', null, 'select list_servers()', 'permission denied');
select testkit.expect_error('authenticated', null, 'select list_servers()', 'not_authenticated');

-- ===== edge cases that the mutation run showed were unchecked
create temp table fx2(k text primary key, v text);
insert into fx2 select k, testkit.make_user(n)::text from (values ('P','SpP'),('Q','SpQ'),('R','SpR'),('T','SpT'),('U','SpU'),('V','SpV')) t(k, n);
create function pg_temp.u2(k text) returns uuid language sql as $$ select v::uuid from fx2 where fx2.k = $1 $$;
update game_sessions set ended_at = now() where ended_at is null;
update convoys set disbanded_at = now() where disbanded_at is null;
update profiles set level = 5 where id in (pg_temp.u2('P'), pg_temp.u2('Q'), pg_temp.u2('R'), pg_temp.u2('T'), pg_temp.u2('U'), pg_temp.u2('V'));
insert into fx2 select 'cvP', testkit.as_user(pg_temp.u2('P'), $$select create_convoy_lobby('Edge Convoy', null, false, 2, 'relaxed', 4)$$);
-- a convoy without a password is not joined through the password door
select testkit.assert_eq('no password set: the password join answers wrong_password and joins nothing', pg_temp.j(pg_temp.u2('Q'), format('select join_convoy_with_password(%L, %L)::text', (select v from fx2 where k = 'cvP'), 'anything'))->>'error', 'wrong_password');
select testkit.assert_eq('...and nobody joined', (select count(*)::text from convoy_members where player_id = pg_temp.u2('Q')), '0');
-- the password and its salt can never be selected by a client
select testkit.expect_error('authenticated', pg_temp.u2('Q'), 'select password_hash from convoys', 'permission denied');
select testkit.expect_error('authenticated', pg_temp.u2('Q'), 'select password_salt from convoys', 'permission denied');
-- the open list tells a player below the entry level that the convoy is not joinable for them
update profiles set level = 2 where id = pg_temp.u2('R');
select testkit.assert_eq('open list: joinable depends on the viewer''s level', (select e->>'joinable' from jsonb_array_elements(testkit.as_user(pg_temp.u2('R'), 'select list_open_convoys()::text')::jsonb->'items') e where e->>'name' = 'Edge Convoy'), 'false');
select testkit.assert_eq('...and for a player who meets it', (select e->>'joinable' from jsonb_array_elements(testkit.as_user(pg_temp.u2('Q'), 'select list_open_convoys()::text')::jsonb->'items') e where e->>'name' = 'Edge Convoy'), 'true');
-- member invites: capacity and "already in a convoy" are checked
select testkit.as_user(pg_temp.u2('Q'), format('select join_convoy(%L)', (select v from fx2 where k = 'cvP')));     -- capacity 2: now full
insert into fx2 select 'fr1', pg_temp.j(pg_temp.u2('Q'), format($q$select send_friend_request(%L)::text$q$, pg_temp.u2('T')))->>'request_id';
select testkit.as_user(pg_temp.u2('T'), format($q$select respond_friend_request(%L, true)$q$, (select v from fx2 where k = 'fr1')));
select testkit.expect_error('authenticated', pg_temp.u2('Q'), format('select invite_friend_to_convoy(%L)', pg_temp.u2('T')), 'convoy_full');
update convoys set capacity = 8 where id = (select v::uuid from fx2 where k = 'cvP');
insert into fx2 select 'cvT', testkit.as_user(pg_temp.u2('T'), $$select create_convoy_lobby('T Convoy')$$);
select testkit.expect_error('authenticated', pg_temp.u2('Q'), format('select invite_friend_to_convoy(%L)', pg_temp.u2('T')), 'player_in_convoy');
-- expired invites are not listed as pending
update convoys set min_level = 1 where id = (select v::uuid from fx2 where k = 'cvP');
select testkit.as_user(pg_temp.u2('P'), format('select invite_to_convoy(%L)', pg_temp.u2('U')));
update convoy_invites set expires_at = now() - interval '1 minute' where invitee_id = pg_temp.u2('U');
select testkit.assert_eq('an expired invite is not shown as invited', jsonb_array_length(pg_temp.j(pg_temp.u2('P'), 'select get_my_convoy()::text')->'convoy'->'invited')::text, '0');
-- a line from another convoy cannot be reported
select pg_temp.j(pg_temp.u2('P'), $$select post_convoy_message('only for this convoy')::text$$);
select testkit.expect_error('authenticated', pg_temp.u2('T'), format('select report_chat_message(%s, %L)', (select id from convoy_messages where body = 'only for this convoy'), 'abuse'), 'message_not_found');
-- blocks: several players may block the same person, and one unblocking does not free it for the others
select testkit.as_user(pg_temp.u2('P'), format('select block_player(%L)', pg_temp.u2('V')));
select testkit.as_user(pg_temp.u2('Q'), format('select block_player(%L)', pg_temp.u2('V')));
select testkit.as_user(pg_temp.u2('P'), format('select unblock_player(%L)', pg_temp.u2('V')));
select testkit.assert_eq('unblocking removes only my own block', (select count(*)::text from player_blocks where blocked_id = pg_temp.u2('V')), '1');
-- blocked players are not in my active-player list
select testkit.as_user(pg_temp.u2(k), 'select start_session()::text') from (values ('P'),('V'),('R')) t(k);
select testkit.assert_eq('active players: a blocked player is hidden from the blocker', (select count(*)::text from jsonb_array_elements(testkit.as_user(pg_temp.u2('Q'), 'select list_active_players()::text')::jsonb->'items') e where e->>'display_name' = 'SpV'), '0');
select testkit.assert_eq('...but visible to others', (select count(*)::text from jsonb_array_elements(testkit.as_user(pg_temp.u2('R'), 'select list_active_players()::text')::jsonb->'items') e where e->>'display_name' = 'SpV'), '1');
-- the block list is bounded
do $$ declare i int; v_err text := null; v_me uuid := (select v::uuid from fx2 where k = 'U');
begin
  for i in 1..200 loop
    insert into auth.users(id, email, raw_user_meta_data) values (gen_random_uuid(), 'blk' || i || '@test', jsonb_build_object('display_name', 'Blk' || i));
  end loop;
  insert into player_blocks(blocker_id, blocked_id) select v_me, id from profiles where display_name like 'Blk%' limit 200;
  begin perform testkit.as_user(v_me, format($q$select block_player(%L)$q$, (select v from fx2 where k = 'P')));
  exception when others then v_err := sqlerrm; end;
  if v_err is distinct from 'block_limit' then raise exception 'ASSERT block limit: expected block_limit, got %', coalesce(v_err, 'no error'); end if;
end $$;
-- nearby: the list is bounded and "joinable" respects the viewer's level
update game_sessions set ended_at = now() where ended_at is null;
insert into fx2 select 'cvU', testkit.as_user(pg_temp.u2('U'), $$select create_convoy_lobby('Rank Convoy', null, false, 8, 'relaxed', 4)$$);
select testkit.as_user(pg_temp.u2(k), 'select start_session()::text') from (values ('P'),('Q'),('R'),('U')) t(k);
create function pg_temp.hb2(p_k text, p_route text, p_city text) returns void language plpgsql as $$
declare v_sess uuid;
begin
  select id into v_sess from game_sessions where player_id = pg_temp.u2(p_k) and ended_at is null order by started_at desc limit 1;
  perform testkit.as_user(pg_temp.u2(p_k), format($q$select heartbeat(%L, 'driving', %L, %L)::text$q$, v_sess, p_route, p_city));
end $$;
select testkit.as_user(pg_temp.u2(k), 'select set_nearby_visibility(true)') from (values ('P'),('Q'),('R'),('U')) t(k);
select pg_temp.hb2('P', 'lagos-ibadan', 'lagos'); select pg_temp.hb2('Q', 'lagos-ibadan', 'lagos'); select pg_temp.hb2('R', 'lagos-ibadan', 'lagos'); select pg_temp.hb2('U', 'lagos-ibadan', 'lagos');
select testkit.assert_eq('nearby: a convoy above my level is not joinable for me', (select e->'convoy'->>'joinable' from jsonb_array_elements(testkit.as_user(pg_temp.u2('R'), 'select list_nearby_players()::text')::jsonb->'items') e where e->>'display_name' = 'SpU'), 'false');
select testkit.assert_eq('nearby: ...and joinable for someone who meets it', (select e->'convoy'->>'joinable' from jsonb_array_elements(testkit.as_user(pg_temp.u2('P'), 'select list_nearby_players()::text')::jsonb->'items') e where e->>'display_name' = 'SpU'), 'true');
do $$ declare i int; v_ids uuid[] := '{}'; v_id uuid; v_sess uuid;
begin
  for i in 1..40 loop
    v_id := testkit.make_user('Crowd' || i);
    update profiles set appear_nearby = true where id = v_id;      -- fixture: a crowd that opted in
    insert into game_sessions(player_id, route_code, activity) values (v_id, 'lagos-ibadan', 'driving');
  end loop;
end $$;
select testkit.assert_eq('nearby: the list is bounded by the page size', jsonb_array_length(testkit.as_user(pg_temp.u2('P'), 'select list_nearby_players(1000)::text')::jsonb->'items')::text, '30');
select testkit.assert_eq('nearby: a small page is honoured', jsonb_array_length(testkit.as_user(pg_temp.u2('P'), 'select list_nearby_players(5)::text')::jsonb->'items')::text, '5');

select 'CONVOY SPEC TESTS PASSED' as result;
