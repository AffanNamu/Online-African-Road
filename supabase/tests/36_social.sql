\set ON_ERROR_STOP on
-- Backend foundation tests, part 2: friends, recent players, convoys, notifications.
create function pg_temp.j(p_uid uuid, p_sql text) returns jsonb language sql as $$ select testkit.as_user(p_uid, p_sql)::jsonb $$;
create temp table fx(k text primary key, v text);
insert into fx select k, testkit.make_user(n)::text from (values
  ('A','SocA'),('B','SocB'),('C','SocC'),('D','SocD'),('E','SocE'),('F','SocF'),('G','SocG')) t(k, n);
create function pg_temp.u(k text) returns uuid language sql as $$ select v::uuid from fx where fx.k = $1 $$;
create function pg_temp.count_of(p_player uuid, p_type text) returns text language sql as $$
  select count(*)::text from notifications where player_id = p_player and type = p_type $$;

-- ===== FRIENDS: requests
insert into fx select 'req1', (pg_temp.j(pg_temp.u('A'), format('select send_friend_request(%L)::text', pg_temp.u('B')))->>'request_id');
select testkit.assert_eq('request is pending', (select status from friendships where id = (select v::uuid from fx where k = 'req1')), 'pending');
select testkit.assert_eq('addressee is notified', pg_temp.count_of(pg_temp.u('B'), 'friend_event'), '1');
select testkit.expect_error('authenticated', pg_temp.u('A'), format('select send_friend_request(%L)', pg_temp.u('B')), 'request_pending');
select testkit.expect_error('authenticated', pg_temp.u('A'), format('select send_friend_request(%L)', pg_temp.u('A')), 'cannot_friend_self');
select testkit.expect_error('authenticated', pg_temp.u('A'), 'select send_friend_request(gen_random_uuid())', 'player_not_found');
select testkit.assert_eq('status: outgoing', testkit.as_user(pg_temp.u('A'), format('select get_friend_status(%L)', pg_temp.u('B'))), 'outgoing');
select testkit.assert_eq('status: incoming', testkit.as_user(pg_temp.u('B'), format('select get_friend_status(%L)', pg_temp.u('A'))), 'incoming');
select testkit.assert_eq('status: stranger', testkit.as_user(pg_temp.u('C'), format('select get_friend_status(%L)', pg_temp.u('A'))), 'none');
select testkit.assert_eq('status: self', testkit.as_user(pg_temp.u('A'), format('select get_friend_status(%L)', pg_temp.u('A'))), 'self');
-- authorisation: only the addressee can answer; third parties and the requester cannot
select testkit.expect_error('authenticated', pg_temp.u('C'), format('select respond_friend_request(%L, true)', (select v from fx where k = 'req1')), 'request_not_found');
select testkit.expect_error('authenticated', pg_temp.u('A'), format('select respond_friend_request(%L, true)', (select v from fx where k = 'req1')), 'request_not_found');
select testkit.expect_error('authenticated', pg_temp.u('C'), format('select remove_friend(%L)', pg_temp.u('A')), 'not_friends');
select testkit.assert_eq('friend list is empty before acceptance', jsonb_array_length(pg_temp.j(pg_temp.u('A'), 'select list_friends()::text')->'items')::text, '0');
select testkit.assert_eq('incoming list', jsonb_array_length(pg_temp.j(pg_temp.u('B'), 'select list_friend_requests()::text')->'incoming')::text, '1');
select testkit.assert_eq('outgoing list', jsonb_array_length(pg_temp.j(pg_temp.u('A'), 'select list_friend_requests()::text')->'outgoing')::text, '1');
select testkit.assert_eq('a stranger sees neither', jsonb_array_length(pg_temp.j(pg_temp.u('C'), 'select list_friend_requests()::text')->'incoming')::text, '0');
-- accept
select testkit.assert_eq('accept', testkit.as_user(pg_temp.u('B'), format('select respond_friend_request(%L, true)', (select v from fx where k = 'req1'))), 'accepted');
select testkit.assert_eq('requester notified of acceptance', pg_temp.count_of(pg_temp.u('A'), 'friend_event'), '1');
select testkit.assert_eq('A lists B', jsonb_array_length(pg_temp.j(pg_temp.u('A'), 'select list_friends()::text')->'items')::text, '1');
select testkit.assert_eq('B lists A', jsonb_array_length(pg_temp.j(pg_temp.u('B'), 'select list_friends()::text')->'items')::text, '1');
select testkit.assert_eq('C sees no friends (not every player is a friend)', jsonb_array_length(pg_temp.j(pg_temp.u('C'), 'select list_friends()::text')->'items')::text, '0');
select testkit.expect_error('authenticated', pg_temp.u('C'), format('select remove_friend(%L)', pg_temp.u('A')), 'not_friends');
select testkit.expect_error('authenticated', pg_temp.u('C'), format('select remove_friend(%L)', pg_temp.u('B')), 'not_friends');
select testkit.assert_eq('a stranger cannot dissolve a friendship', (select status from friendships where least(requester_id, addressee_id) = least(pg_temp.u('A'), pg_temp.u('B')) and greatest(requester_id, addressee_id) = greatest(pg_temp.u('A'), pg_temp.u('B'))), 'accepted');
select testkit.assert_eq('status: friends', testkit.as_user(pg_temp.u('A'), format('select get_friend_status(%L)', pg_temp.u('B'))), 'friends');
select testkit.expect_error('authenticated', pg_temp.u('A'), format('select send_friend_request(%L)', pg_temp.u('B')), 'already_friends');
select testkit.expect_error('authenticated', pg_temp.u('B'), format('select respond_friend_request(%L, true)', (select v from fx where k = 'req1')), 'request_not_found');
select testkit.assert_eq('friend_added analytics', (select count(*)::text from analytics_events where name = 'friend_added'), '1');
-- presence of friends is visible, of strangers is not
insert into fx select 'sB', (pg_temp.j(pg_temp.u('B'), 'select start_session()::text')->>'session_id');
select testkit.assert_eq('friend list shows presence', pg_temp.j(pg_temp.u('A'), 'select list_friends()::text')->'items'->0->'presence'->>'online', 'true');
select testkit.assert_eq('get_presence returns a friend', jsonb_array_length(pg_temp.j(pg_temp.u('A'), format('select get_presence(array[%L]::uuid[])::text', pg_temp.u('B')))->'items')::text, '1');
select testkit.assert_eq('get_presence omits a stranger', jsonb_array_length(pg_temp.j(pg_temp.u('C'), format('select get_presence(array[%L]::uuid[])::text', pg_temp.u('B')))->'items')::text, '0');
select testkit.expect_error('authenticated', pg_temp.u('C'), $$select get_presence((select array_agg(id) from (select gen_random_uuid() id from generate_series(1, 60)) q))$$, 'too_many_players');
-- remove
select testkit.assert_eq('remove', testkit.as_user(pg_temp.u('A'), format('select remove_friend(%L)::text', pg_temp.u('B'))), '');
select testkit.assert_eq('friendship gone for both', (select count(*)::text from friendships where least(requester_id, addressee_id) = least(pg_temp.u('A'), pg_temp.u('B'))), '0');
select testkit.expect_error('authenticated', pg_temp.u('A'), format('select remove_friend(%L)', pg_temp.u('B')), 'not_friends');
-- rejection + cooldown
insert into fx select 'req2', (pg_temp.j(pg_temp.u('C'), format('select send_friend_request(%L)::text', pg_temp.u('A')))->>'request_id');
select testkit.assert_eq('reject', testkit.as_user(pg_temp.u('A'), format('select respond_friend_request(%L, false)', (select v from fx where k = 'req2'))), 'rejected');
select testkit.assert_eq('rejected request is invisible to the requester', testkit.as_user(pg_temp.u('C'), format('select get_friend_status(%L)', pg_temp.u('A'))), 'none');
select testkit.expect_error('authenticated', pg_temp.u('C'), format('select send_friend_request(%L)', pg_temp.u('A')), 'request_cooldown');
update friendships set responded_at = now() - interval '25 hours' where id = (select v::uuid from fx where k = 'req2');
select testkit.assert_eq('after the cooldown a new request is allowed', pg_temp.j(pg_temp.u('C'), format('select send_friend_request(%L)::text', pg_temp.u('A')))->>'status', 'pending');
-- a cancelled outgoing request
select testkit.assert_eq('requester can cancel', testkit.as_user(pg_temp.u('C'), format('select remove_friend(%L)::text', pg_temp.u('A'))), '');
-- mutual request = accepted
select pg_temp.j(pg_temp.u('D'), format('select send_friend_request(%L)::text', pg_temp.u('E')));
select testkit.assert_eq('asking back accepts', pg_temp.j(pg_temp.u('E'), format('select send_friend_request(%L)::text', pg_temp.u('D')))->>'status', 'accepted');
-- limits
update game_config set value = 1 where key = 'friend_limit';
select testkit.expect_error('authenticated', pg_temp.u('D'), format('select send_friend_request(%L)', pg_temp.u('F')), 'friend_limit_reached');
update game_config set value = 100 where key = 'friend_limit';
-- find_player: exact handle only (no enumeration)
select testkit.assert_eq('find by exact username', pg_temp.j(pg_temp.u('A'), $$select find_player('socf')::text$$)->>'display_name', 'SocF');
select testkit.assert_eq('find is case-insensitive', pg_temp.j(pg_temp.u('A'), $$select find_player('  SOCF ')::text$$)->>'display_name', 'SocF');
select testkit.expect_error('authenticated', pg_temp.u('A'), $$select find_player('soc')$$, 'player_not_found');
select testkit.expect_error('authenticated', pg_temp.u('A'), $$select find_player('%')$$, 'player_not_found');
-- raw tables are closed
select testkit.expect_error('authenticated', pg_temp.u('A'), 'select * from friendships', 'permission denied');
select testkit.expect_error('authenticated', pg_temp.u('A'), 'insert into friendships(requester_id, addressee_id, status) select auth.uid(), id, ''accepted'' from profiles limit 1', 'permission denied');
select testkit.expect_error('authenticated', pg_temp.u('A'), 'update friendships set status = ''accepted''', 'permission denied');
select testkit.expect_error('authenticated', pg_temp.u('A'), 'select * from player_encounters', 'permission denied');
select testkit.expect_error('anon', null, 'select list_friends()', 'permission denied');
select testkit.expect_error('authenticated', null, 'select list_friends()', 'not_authenticated');

-- ===== CONVOYS: membership, capacity, privacy, leadership
insert into fx select 'cv', testkit.as_user(pg_temp.u('A'), $$select create_convoy('Alpha Run')$$);
select testkit.assert_eq('creator is leader and sole member', pg_temp.j(pg_temp.u('A'), 'select get_my_convoy()::text')->'convoy'->>'member_count', '1');
select testkit.assert_eq('is_leader flag', pg_temp.j(pg_temp.u('A'), 'select get_my_convoy()::text')->'convoy'->>'is_leader', 'true');
select testkit.assert_eq('default capacity', pg_temp.j(pg_temp.u('A'), 'select get_my_convoy()::text')->'convoy'->>'capacity', '8');
select testkit.assert_eq('convoy_created analytics', (select count(*)::text from analytics_events where name = 'convoy_created' and player_id = pg_temp.u('A')), '1');
select testkit.assert_eq('no convoy => null', (pg_temp.j(pg_temp.u('G'), 'select get_my_convoy()::text')->'convoy')::text, 'null');
select testkit.expect_error('authenticated', pg_temp.u('A'), $$select create_convoy('Second Convoy')$$, 'already_in_convoy_or_code_taken');
select testkit.assert_eq('B joins', testkit.as_user(pg_temp.u('B'), format('select join_convoy(%L)::text', (select v from fx where k = 'cv'))), '');
select testkit.assert_eq('leader is told', pg_temp.count_of(pg_temp.u('A'), 'convoy_event'), '1');
select testkit.assert_eq('convoy_joined analytics', (select count(*)::text from analytics_events where name = 'convoy_joined' and player_id = pg_temp.u('B')), '1');
select testkit.assert_eq('members see each other', jsonb_array_length(pg_temp.j(pg_temp.u('B'), 'select get_my_convoy()::text')->'convoy'->'members')::text, '2');
select testkit.assert_eq('member list marks the leader', pg_temp.j(pg_temp.u('B'), 'select get_my_convoy()::text')->'convoy'->'members'->0->>'is_leader', 'true');
select testkit.assert_eq('members see presence of mates', pg_temp.j(pg_temp.u('A'), 'select get_my_convoy()::text')->'convoy'->'members'->1->'presence'->>'online', 'true');
select testkit.expect_error('authenticated', pg_temp.u('B'), format('select join_convoy(%L)', (select v from fx where k = 'cv')), 'already_in_convoy');
-- recent players are remembered from convoys (both directions)
select testkit.assert_eq('A remembers B', pg_temp.j(pg_temp.u('A'), 'select list_recent_players()::text')->'items'->0->>'display_name', 'SocB');
select testkit.assert_eq('B remembers A', pg_temp.j(pg_temp.u('B'), 'select list_recent_players()::text')->'items'->0->>'display_name', 'SocA');
select testkit.assert_eq('C remembers nobody', jsonb_array_length(pg_temp.j(pg_temp.u('C'), 'select list_recent_players()::text')->'items')::text, '0');
-- options: leader only, capacity bounds
select testkit.expect_error('authenticated', pg_temp.u('B'), 'select set_convoy_options(4)', 'not_leader');
select testkit.expect_error('authenticated', pg_temp.u('A'), 'select set_convoy_options(1)', 'invalid_capacity');
select testkit.expect_error('authenticated', pg_temp.u('A'), 'select set_convoy_options(9)', 'invalid_capacity');
select testkit.assert_eq('capacity 2', pg_temp.j(pg_temp.u('A'), 'select set_convoy_options(2)::text')->>'capacity', '2');
select testkit.assert_eq('status full', pg_temp.j(pg_temp.u('A'), 'select get_my_convoy()::text')->'convoy'->>'status', 'full');
select testkit.expect_error('authenticated', pg_temp.u('C'), format('select join_convoy(%L)', (select v from fx where k = 'cv')), 'convoy_full');
-- private convoys need an invite
select pg_temp.j(pg_temp.u('A'), 'select set_convoy_options(3, true)::text');
select testkit.assert_eq('private convoys are not listed', (select count(*)::text from jsonb_array_elements(pg_temp.j(pg_temp.u('G'), 'select list_open_convoys()::text')->'items') e where e->>'id' = (select v from fx where k = 'cv')), '0');
select testkit.expect_error('authenticated', pg_temp.u('C'), format('select join_convoy(%L)', (select v from fx where k = 'cv')), 'invite_required');
select testkit.expect_error('authenticated', pg_temp.u('B'), format('select invite_to_convoy(%L)', pg_temp.u('C')), 'not_leader');
select testkit.expect_error('authenticated', pg_temp.u('A'), format('select invite_to_convoy(%L)', pg_temp.u('A')), 'cannot_invite_self');
select testkit.expect_error('authenticated', pg_temp.u('A'), format('select invite_to_convoy(%L)', pg_temp.u('B')), 'player_in_convoy');
select testkit.expect_error('authenticated', pg_temp.u('A'), 'select invite_to_convoy(gen_random_uuid())', 'player_not_found');
insert into fx select 'inv', testkit.as_user(pg_temp.u('A'), format('select invite_to_convoy(%L)', pg_temp.u('C')));
select testkit.expect_error('authenticated', pg_temp.u('A'), format('select invite_to_convoy(%L)', pg_temp.u('C')), 'already_invited');
select testkit.assert_eq('invitee is notified', (select count(*)::text from notifications where player_id = pg_temp.u('C') and metadata->>'invite_id' = (select v from fx where k = 'inv')), '1');
select testkit.assert_eq('invitee lists the invite', jsonb_array_length(pg_temp.j(pg_temp.u('C'), 'select list_convoy_invites()::text')->'items')::text, '1');
select testkit.assert_eq('others do not see it', jsonb_array_length(pg_temp.j(pg_temp.u('D'), 'select list_convoy_invites()::text')->'items')::text, '0');
select testkit.expect_error('authenticated', pg_temp.u('D'), format('select respond_convoy_invite(%L, true)', (select v from fx where k = 'inv')), 'invite_not_found');
select testkit.expect_error('authenticated', pg_temp.u('A'), format('select respond_convoy_invite(%L, true)', (select v from fx where k = 'inv')), 'invite_not_found');
select testkit.assert_eq('accept', testkit.as_user(pg_temp.u('C'), format('select respond_convoy_invite(%L, true)', (select v from fx where k = 'inv'))), 'accepted');
select testkit.assert_eq('invitee is now a member', pg_temp.j(pg_temp.u('C'), 'select get_my_convoy()::text')->'convoy'->>'member_count', '3');
select testkit.assert_eq('inviter is told', (select count(*)::text from notifications where player_id = pg_temp.u('A') and title = 'Invite accepted'), '1');
select testkit.expect_error('authenticated', pg_temp.u('C'), format('select respond_convoy_invite(%L, true)', (select v from fx where k = 'inv')), 'invite_not_found');
-- decline path + expiry
select pg_temp.j(pg_temp.u('A'), 'select set_convoy_options(4)::text');
insert into fx select 'inv3', testkit.as_user(pg_temp.u('A'), format('select invite_to_convoy(%L)', pg_temp.u('D')));
select testkit.assert_eq('decline', testkit.as_user(pg_temp.u('D'), format('select respond_convoy_invite(%L, false)', (select v from fx where k = 'inv3'))), 'declined');
select testkit.expect_error('authenticated', pg_temp.u('D'), format('select join_convoy(%L)', (select v from fx where k = 'cv')), 'invite_required');
insert into fx select 'inv4', testkit.as_user(pg_temp.u('A'), format('select invite_to_convoy(%L)', pg_temp.u('E')));
update convoy_invites set expires_at = now() - interval '1 minute' where id = (select v::uuid from fx where k = 'inv4');
select testkit.expect_error('authenticated', pg_temp.u('E'), format('select respond_convoy_invite(%L, true)', (select v from fx where k = 'inv4')), 'invite_expired');
select testkit.assert_eq('expired invites are not listed', jsonb_array_length(pg_temp.j(pg_temp.u('E'), 'select list_convoy_invites()::text')->'items')::text, '0');
-- kick + leadership
select testkit.expect_error('authenticated', pg_temp.u('B'), format('select kick_convoy_member(%L)', pg_temp.u('C')), 'not_leader');
select testkit.expect_error('authenticated', pg_temp.u('A'), format('select kick_convoy_member(%L)', pg_temp.u('A')), 'cannot_kick_leader');
select testkit.expect_error('authenticated', pg_temp.u('A'), format('select kick_convoy_member(%L)', pg_temp.u('G')), 'not_a_member');
select testkit.assert_eq('leader kicks C', testkit.as_user(pg_temp.u('A'), format('select kick_convoy_member(%L)::text', pg_temp.u('C'))), '');
select testkit.assert_eq('C is told', (select count(*)::text from notifications where player_id = pg_temp.u('C') and title = 'Removed from convoy'), '1');
select testkit.expect_error('authenticated', pg_temp.u('C'), format('select join_convoy(%L)', (select v from fx where k = 'cv')), 'invite_required');
select testkit.expect_error('authenticated', pg_temp.u('B'), format('select transfer_convoy_leader(%L)', pg_temp.u('B')), 'not_leader');
select testkit.expect_error('authenticated', pg_temp.u('A'), format('select transfer_convoy_leader(%L)', pg_temp.u('G')), 'not_a_member');
select testkit.assert_eq('transfer', testkit.as_user(pg_temp.u('A'), format('select transfer_convoy_leader(%L)::text', pg_temp.u('B'))), '');
select testkit.assert_eq('new leader', (select leader_id::text from convoys where id = (select v::uuid from fx where k = 'cv')), pg_temp.u('B')::text);
select testkit.assert_eq('new leader is told', (select count(*)::text from notifications where player_id = pg_temp.u('B') and title = 'You lead the convoy'), '1');
select testkit.expect_error('authenticated', pg_temp.u('A'), 'select set_convoy_options(5)', 'not_leader');
-- leaving, disbanding
select testkit.assert_eq('A leaves', testkit.as_user(pg_temp.u('A'), 'select leave_convoy()::text'), '');
select testkit.assert_eq('convoy_left analytics', (select count(*)::text from analytics_events where name = 'convoy_left' and player_id = pg_temp.u('A')), '1');
select testkit.assert_eq('B leaves => convoy disbanded', testkit.as_user(pg_temp.u('B'), 'select leave_convoy()::text'), '');
select testkit.assert_eq('disbanded', (select (disbanded_at is not null)::text from convoys where id = (select v::uuid from fx where k = 'cv')), 'true');
select testkit.assert_eq('recent players (no longer convoy mates) get online/offline only', (select count(*)::text from jsonb_array_elements(pg_temp.j(pg_temp.u('A'), 'select list_recent_players()::text')->'items') e where (e->'presence') ? 'route_code' or (e->'presence') ? 'activity'), '0');
select testkit.expect_error('authenticated', pg_temp.u('G'), format('select join_convoy(%L)', (select v from fx where k = 'cv')), 'convoy_not_found');
-- the join code is still behind the membership gate
select testkit.expect_error('authenticated', pg_temp.u('G'), 'select session_code from convoys', 'permission denied');
select testkit.expect_error('authenticated', pg_temp.u('G'), 'select * from convoy_invites', 'permission denied');
select testkit.expect_error('authenticated', pg_temp.u('G'), 'update convoys set capacity = 8', 'permission denied');
select testkit.expect_error('authenticated', pg_temp.u('G'), 'update convoys set leader_id = auth.uid()', 'permission denied');

-- ===== NOTIFICATIONS
insert into fx select 'n1', _notify(pg_temp.u('G'), 'system', 'Hello', 'First message', '{"k":1}'::jsonb, 'dk-1')::text;
select testkit.assert_eq('dedupe key makes it idempotent', (_notify(pg_temp.u('G'), 'system', 'Hello', 'First message', '{}'::jsonb, 'dk-1') is null)::text, 'true');
select _notify(pg_temp.u('G'), 'payout', 'Paid', 'Second');
select _notify(pg_temp.u('G'), 'error', 'Oops', 'Third');
select testkit.assert_eq('three notifications', jsonb_array_length(pg_temp.j(pg_temp.u('G'), 'select list_notifications()::text')->'items')::text, '3');
select testkit.assert_eq('unread counter in the list', pg_temp.j(pg_temp.u('G'), 'select list_notifications()::text')->>'unread', '3');
select testkit.assert_eq('unread count endpoint', testkit.as_user(pg_temp.u('G'), 'select get_unread_notification_count()'), '3');
select testkit.assert_eq('newest first', pg_temp.j(pg_temp.u('G'), 'select list_notifications()::text')->'items'->0->>'title', 'Oops');
select testkit.assert_eq('metadata round-trips', pg_temp.j(pg_temp.u('G'), 'select list_notifications()::text')->'items'->2->'metadata'->>'k', '1');
select testkit.assert_eq('a different player sees none of them', jsonb_array_length(pg_temp.j(pg_temp.u('F'), 'select list_notifications()::text')->'items')::text, '0');
select testkit.expect_error('authenticated', pg_temp.u('F'), format('select mark_notification_read(%L)', (select v from fx where k = 'n1')), 'notification_not_found');
select testkit.assert_eq('foreign mark did not change anything', testkit.as_user(pg_temp.u('G'), 'select get_unread_notification_count()'), '3');
select testkit.assert_eq('mark own read', testkit.as_user(pg_temp.u('G'), format('select mark_notification_read(%L)::text', (select v from fx where k = 'n1'))), '');
select testkit.assert_eq('unread decremented', testkit.as_user(pg_temp.u('G'), 'select get_unread_notification_count()'), '2');
select testkit.assert_eq('unread-only filter', jsonb_array_length(pg_temp.j(pg_temp.u('G'), 'select list_notifications(30, null, true)::text')->'items')::text, '2');
select testkit.assert_eq('limit', jsonb_array_length(pg_temp.j(pg_temp.u('G'), 'select list_notifications(1)::text')->'items')::text, '1');
insert into fx select 'newest', pg_temp.j(pg_temp.u('G'), 'select list_notifications(1)::text')->'items'->0->>'created_at';
select testkit.assert_eq('keyset pagination: strictly older than the newest', jsonb_array_length(pg_temp.j(pg_temp.u('G'), format('select list_notifications(30, %L)::text', (select v from fx where k = 'newest')))->'items')::text, '2');
select testkit.assert_eq('mark all', testkit.as_user(pg_temp.u('G'), 'select mark_all_notifications_read()'), '2');
select testkit.assert_eq('none unread', testkit.as_user(pg_temp.u('G'), 'select get_unread_notification_count()'), '0');
select testkit.assert_eq('mark all is idempotent', testkit.as_user(pg_temp.u('G'), 'select mark_all_notifications_read()'), '0');
-- the inbox is bounded
update game_config set value = 20 where key = 'notifications_keep';
do $$ declare i int; begin for i in 1..30 loop perform _notify((select v::uuid from fx where k = 'F'), 'server', 'Msg ' || i, 'body'); end loop; end $$;
select testkit.assert_eq('inbox bounded by notifications_keep', (select count(*)::text from notifications where player_id = pg_temp.u('F')), '20');
select testkit.assert_eq('oldest are pruned', (select count(*)::text from notifications where player_id = pg_temp.u('F') and title = 'Msg 1'), '0');
update game_config set value = 200 where key = 'notifications_keep';
-- closed paths
select testkit.expect_error('authenticated', pg_temp.u('G'), 'select * from notifications', 'permission denied');
select testkit.expect_error('authenticated', pg_temp.u('G'), 'update notifications set read_at = null', 'permission denied');
select testkit.expect_error('authenticated', pg_temp.u('G'), 'delete from notifications', 'permission denied');
select testkit.expect_error('authenticated', pg_temp.u('G'), $$insert into notifications(player_id, type, title, message) values (auth.uid(), 'system', 'x', 'y')$$, 'permission denied');
select testkit.expect_error('authenticated', pg_temp.u('G'), $$select _notify(auth.uid(), 'system', 'forged', 'forged')$$, 'permission denied');
select testkit.expect_error('authenticated', pg_temp.u('G'), $$select admin_broadcast('x', 'y', 'key')$$, 'permission denied');
select testkit.expect_error('anon', null, 'select list_notifications()', 'permission denied');
select testkit.expect_error('authenticated', null, 'select get_unread_notification_count()', 'not_authenticated');
do $$ begin begin perform _notify((select v::uuid from fx where k = 'G'), 'bogus', 't', 'm'); raise exception 'invalid type accepted';
  exception when check_violation then null; end; end $$;
select testkit.assert_eq('server broadcast reaches everyone once', (select (admin_broadcast('Maintenance', 'Servers restart at 02:00', 'bc-1') >= 7)::text), 'true');
select testkit.assert_eq('broadcast is idempotent per key', (select admin_broadcast('Maintenance', 'Servers restart at 02:00', 'bc-1')::text), '0');
select testkit.assert_eq('broadcast uses the server type', (select count(*)::text from notifications where player_id = pg_temp.u('G') and type = 'server'), '1');

select 'SOCIAL + NOTIFICATION TESTS PASSED' as result;
