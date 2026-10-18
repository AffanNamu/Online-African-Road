\set ON_ERROR_STOP on
-- Leaderboards: the friends board (me + accepted friends, ranked among themselves) and the ranked-player search (global ranks).
create function pg_temp.j(p_uid uuid, p_sql text) returns jsonb language sql as $$ select testkit.as_user(p_uid, p_sql)::jsonb $$;
create temp table fx(k text primary key, v text);
insert into fx select k, testkit.make_user(n)::text from (values ('A','ZqAlpha'),('B','ZqBravo'),('C','ZqCharlie'),('D','ZqDelta'),('E','ZqEcho'),('F','ZqFoxtrot')) t(k, n);
create function pg_temp.u(k text) returns uuid language sql as $$ select v::uuid from fx where fx.k = $1 $$;
create function pg_temp.fb(k text, m text, per text default 'all') returns jsonb language sql as $$ select pg_temp.j(pg_temp.u($1), format('select get_friends_leaderboard(%L, %L)::text', $2, $3)) $$;
create function pg_temp.sr(k text, m text, per text, q text) returns jsonb language sql as $$ select pg_temp.j(pg_temp.u($1), format('select search_leaderboard(%L, %L, %L)::text', $2, $3, $4)) $$;
create function pg_temp.befriend(a text, b text) returns void language plpgsql as $$
declare v_id text;
begin
  v_id := pg_temp.j(pg_temp.u(a), format('select send_friend_request(%L)::text', pg_temp.u(b)))->>'request_id';
  perform testkit.as_user(pg_temp.u(b), format('select respond_friend_request(%L, true)', v_id));
end $$;

-- A is friends with B, C and E; D and F are strangers to A. Stats are server records (set here as the server would)
select pg_temp.befriend('A', 'B'); select pg_temp.befriend('A', 'C'); select pg_temp.befriend('A', 'E');
update profiles set experience = 5000, distance_km = 120, jobs_completed = 9,  lifetime_earnings = 40000 where id = pg_temp.u('A');
update profiles set experience = 9000, distance_km = 300, jobs_completed = 30, lifetime_earnings = 90000 where id = pg_temp.u('B');
update profiles set experience = 7000, distance_km = 80,  jobs_completed = 5,  lifetime_earnings = 10000 where id = pg_temp.u('C');
update profiles set experience = 99000, distance_km = 5000, jobs_completed = 400, lifetime_earnings = 900000 where id = pg_temp.u('D');   -- a stranger far ahead
update profiles set experience = 0 where id = pg_temp.u('E');                                                                    -- a friend with nothing yet
update profiles set experience = 7000 where id = pg_temp.u('F');                                                                 -- a stranger tied with C

-- ===== friends board
select testkit.assert_eq('the board has me and my 3 friends, nobody else', pg_temp.fb('A', 'xp')->>'total', '4');
select testkit.assert_eq('4 rows', jsonb_array_length(pg_temp.fb('A', 'xp')->'items')::text, '4');
select testkit.assert_eq('ranked by value: the best friend first', pg_temp.fb('A', 'xp')->'items'->0->>'display_name', 'ZqBravo');
select testkit.assert_eq('then the next', pg_temp.fb('A', 'xp')->'items'->1->>'display_name', 'ZqCharlie');
select testkit.assert_eq('I am third among friends', pg_temp.fb('A', 'xp')->'me'->>'rank', '3');
select testkit.assert_eq('with my own value', pg_temp.fb('A', 'xp')->'me'->>'value', '5000');
select testkit.assert_eq('a friend with nothing is listed last', pg_temp.fb('A', 'xp')->'items'->3->>'display_name', 'ZqEcho');
select testkit.assert_eq('every row carries its own rank, in order (ties would share one)', (select string_agg(e->>'rank', ',' order by o) from jsonb_array_elements(pg_temp.fb('A', 'xp')->'items') with ordinality t(e, o)), '1,2,3,4');
select testkit.assert_eq('and the whole order is by value, not by id', (select string_agg(e->>'display_name', ',' order by o) from jsonb_array_elements(pg_temp.fb('A', 'xp')->'items') with ordinality t(e, o)), 'ZqBravo,ZqCharlie,ZqAlpha,ZqEcho');
select testkit.assert_eq('the stranger far ahead is not on a friends board', (select count(*)::text from jsonb_array_elements(pg_temp.fb('A', 'xp')->'items') e where e->>'display_name' = 'ZqDelta'), '0');
select testkit.assert_eq('the scope is stated', pg_temp.fb('A', 'xp')->>'scope', 'friends');
select testkit.assert_eq('the metric chooses the order: distance', pg_temp.fb('A', 'distance')->'items'->0->>'display_name', 'ZqBravo');
select testkit.assert_eq('jobs: I am second of four', pg_temp.fb('A', 'jobs')->'me'->>'rank', '2');
select testkit.assert_eq('earnings: I am second', pg_temp.fb('A', 'earnings')->'me'->>'rank', '2');
select testkit.assert_eq('distance: my own value is my distance', pg_temp.fb('A', 'distance')->'me'->>'value', '120');
select testkit.assert_eq('jobs: my own value is my jobs', pg_temp.fb('A', 'jobs')->'me'->>'value', '9');
select testkit.assert_eq('earnings: my own value is my earnings', pg_temp.fb('A', 'earnings')->'me'->>'value', '40000');
select testkit.assert_eq('a friend value is theirs: C distance', (select e->>'value' from jsonb_array_elements(pg_temp.fb('A', 'distance')->'items') e where e->>'display_name' = 'ZqCharlie'), '80');
select testkit.assert_eq('the other side sees a different board (B has only A as a friend)', pg_temp.fb('B', 'xp')->>'total', '2');
select testkit.assert_eq('a player with no friends still sees themselves', pg_temp.fb('D', 'xp')->>'total', '1');
select testkit.assert_eq('and ranks first', pg_temp.fb('D', 'xp')->'me'->>'rank', '1');
select testkit.assert_eq('a pending request is not a friend', (pg_temp.j(pg_temp.u('F'), format('select send_friend_request(%L)::text', pg_temp.u('A')))->>'status') || ':' || (pg_temp.fb('A', 'xp')->>'total'), 'pending:4');
select testkit.assert_eq('the card carries what the screen needs', (select count(*)::text from jsonb_array_elements(pg_temp.fb('A', 'xp')->'items') e where e ? 'player_id' and e ? 'username' and e ? 'level' and e ? 'value' and e ? 'rank'), '4');

-- ===== this week (the ledger)
select _apply_transaction(pg_temp.u('C'), 'BONUS', 5000, 'old:' || gen_random_uuid());
update transactions set created_at = now() - interval '30 days' where player_id = pg_temp.u('C') and idempotency_key like 'old:%';          -- money from last month must not count this week
select _apply_transaction(pg_temp.u('B'), 'BONUS', 500, 'wk:' || gen_random_uuid());
select _apply_transaction(pg_temp.u('A'), 'BONUS', 900, 'wk:' || gen_random_uuid());
select testkit.assert_eq('weekly earnings: I lead my friends with 900', pg_temp.fb('A', 'earnings', 'weekly')->'me'->>'rank', '1');
select testkit.assert_eq('with the value from the ledger', pg_temp.fb('A', 'earnings', 'weekly')->'me'->>'value', '900');
select testkit.assert_eq('friends with nothing this week are still listed', pg_temp.fb('A', 'earnings', 'weekly')->>'total', '4');
select testkit.assert_eq('last month does not count in a friends weekly board', (select e->>'value' from jsonb_array_elements(pg_temp.fb('A', 'earnings', 'weekly')->'items') e where e->>'display_name' = 'ZqCharlie'), '0');
select testkit.assert_eq('weekly jobs start at zero for everyone, all tied for first', pg_temp.fb('A', 'jobs', 'weekly')->'me'->>'rank', '1');

-- ===== refusals
select testkit.expect_error('authenticated', pg_temp.u('A'), $$select get_friends_leaderboard('coins')$$, 'invalid_metric');
select testkit.expect_error('authenticated', pg_temp.u('A'), $$select get_friends_leaderboard('xp', 'monthly')$$, 'invalid_period');
select testkit.expect_error('authenticated', pg_temp.u('A'), $$select get_friends_leaderboard('xp', 'weekly')$$, 'unsupported_period');
select testkit.expect_error('authenticated', pg_temp.u('A'), $$select get_friends_leaderboard('xp; drop table profiles')$$, 'invalid_metric');
select testkit.expect_error('anon', null, $$select get_friends_leaderboard('xp')$$, 'permission denied');
select testkit.expect_error('authenticated', null, $$select get_friends_leaderboard('xp')$$, 'not_authenticated');
select testkit.expect_error('authenticated', pg_temp.u('A'), $$select search_leaderboard('coins', 'all', 'zq')$$, 'invalid_metric');
select testkit.expect_error('authenticated', pg_temp.u('A'), $$select search_leaderboard('xp', 'weekly', 'zq')$$, 'unsupported_period');
select testkit.expect_error('authenticated', pg_temp.u('A'), $$select search_leaderboard('xp', 'all', 'l')$$, 'query_too_short');
select testkit.expect_error('authenticated', pg_temp.u('A'), $$select search_leaderboard('xp', 'all', '   ')$$, 'query_too_short');
select testkit.expect_error('anon', null, $$select search_leaderboard('xp', 'all', 'zq')$$, 'permission denied');
select testkit.expect_error('authenticated', null, $$select search_leaderboard('xp', 'all', 'zq')$$, 'not_authenticated');

-- ===== search: ranked players whose name or username contains the text, with their GLOBAL rank
select testkit.assert_eq('search finds by name, any case', pg_temp.sr('A', 'xp', 'all', 'ZQDELTA')->'items'->0->>'display_name', 'ZqDelta');
select testkit.assert_eq('with the global rank (first of everyone)', pg_temp.sr('A', 'xp', 'all', 'delta')->'items'->0->>'rank', '1');
select testkit.assert_eq('the same rank the leaderboard gives', pg_temp.sr('A', 'xp', 'all', 'bravo')->'items'->0->>'rank',
  (select e->>'rank' from jsonb_array_elements(pg_temp.j(pg_temp.u('A'), $$select get_leaderboard('xp', 'all', 100)::text$$)->'items') e where e->>'display_name' = 'ZqBravo'));
select testkit.assert_eq('and the value', pg_temp.sr('A', 'xp', 'all', 'bravo')->'items'->0->>'value', '9000');
select testkit.assert_eq('search finds by username too', pg_temp.sr('A', 'xp', 'all', (select username from profiles where id = pg_temp.u('C')))->'items'->0->>'display_name', 'ZqCharlie');
select testkit.assert_eq('a substring matches several players', pg_temp.sr('A', 'xp', 'all', 'zq')->>'total', '5');   -- A B C D F (E has no XP so is not on the board)
select testkit.assert_eq('results come best first', pg_temp.sr('A', 'xp', 'all', 'zq')->'items'->0->>'display_name', 'ZqDelta');
select testkit.assert_eq('an unranked player (no XP) is not found: the board does not list them', pg_temp.sr('A', 'xp', 'all', 'echo')->>'total', '0');
select testkit.assert_eq('a miss is an empty list, not an error', jsonb_array_length(pg_temp.sr('A', 'xp', 'all', 'zzzzzz')->'items')::text, '0');
select testkit.assert_eq('tied players share a rank', pg_temp.sr('A', 'xp', 'all', 'foxtrot')->'items'->0->>'rank', pg_temp.sr('A', 'xp', 'all', 'charlie')->'items'->0->>'rank');
select testkit.assert_eq('% is a plain character, not a wildcard', pg_temp.sr('A', 'xp', 'all', '%%')->>'total', '0');
select testkit.assert_eq('_ is a plain character too', pg_temp.sr('A', 'xp', 'all', 'zq_')->>'total', '0');
select testkit.assert_eq('the limit is applied', jsonb_array_length(pg_temp.j(pg_temp.u('A'), $$select search_leaderboard('xp', 'all', 'zq', 2)::text$$)->'items')::text, '2');
select testkit.assert_eq('and the total still counts every match', pg_temp.j(pg_temp.u('A'), $$select search_leaderboard('xp', 'all', 'zq', 2)::text$$)->>'total', '5');
select testkit.assert_eq('the metric is respected: distance', pg_temp.sr('A', 'distance', 'all', 'bravo')->'items'->0->>'value', '300');
select testkit.assert_eq('weekly search uses the ledger', pg_temp.sr('A', 'earnings', 'weekly', 'bravo')->'items'->0->>'value', '500');
select testkit.assert_eq('and weekly ranks match the leaderboard', pg_temp.sr('A', 'earnings', 'weekly', 'bravo')->'items'->0->>'rank', (select e->>'rank' from jsonb_array_elements(pg_temp.j(pg_temp.u('A'), $$select get_leaderboard('earnings', 'weekly', 100)::text$$)->'items') e where e->>'display_name' = 'ZqBravo'));
select testkit.assert_eq('a player with nothing this week is not found', pg_temp.sr('A', 'earnings', 'weekly', 'charlie')->>'total', '0');
select testkit.assert_eq('the card carries what the screen needs', (select count(*)::text from jsonb_array_elements(pg_temp.sr('A', 'xp', 'all', 'zq')->'items') e where e ? 'player_id' and e ? 'username' and e ? 'level' and e ? 'rank'), '5');
select testkit.assert_eq('nothing private is returned', ((pg_temp.sr('A', 'xp', 'all', 'zq')->'items'->0) ? 'email')::text, 'false');
select 'LEADERBOARD SOCIAL TESTS PASSED' as result;
