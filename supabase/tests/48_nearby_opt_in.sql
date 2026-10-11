\set ON_ERROR_STOP on
-- Players Nearby is opt-in (20261012000007) and the preference touches nothing else: country, flag data, leaderboards, friends, public profiles, the map.
-- Covers: the database default, accounts that existed before the migration (their country survives byte for byte), new-player onboarding, explicit opt-in, opt-out, null.
create function pg_temp.j(p_uid uuid, p_sql text) returns jsonb language sql as $$ select testkit.as_user(p_uid, p_sql)::jsonb $$;
create temp table fx(k text primary key, v text);
insert into fx select k, testkit.make_user(n)::text from (values ('A','NoAlpha'),('B','NoBravo'),('C','NoCharlie'),('D','NoDelta')) t(k, n);
create function pg_temp.u(k text) returns uuid language sql as $$ select v::uuid from fx where fx.k = $1 $$;
update game_sessions set ended_at = now() where ended_at is null;      -- other suites left live players on this route; the lists below must hold only this suite's players
insert into countries(code, name) values ('GH', 'Ghana'), ('KE', 'Kenya') on conflict do nothing;
create function pg_temp.near(k text) returns jsonb language sql as $$ select pg_temp.j(pg_temp.u($1), 'select list_nearby_players(30)::text') $$;
create function pg_temp.seen_by(k text) returns text language sql as $$ select coalesce(string_agg(e->>'display_name', ',' order by e->>'display_name'), 'none') from jsonb_array_elements(pg_temp.near($1)->'items') e where e->>'display_name' like 'No%' $$;
create function pg_temp.live(k text) returns void language plpgsql as $$
declare v_sess uuid;
begin
  perform testkit.as_user(pg_temp.u(k), 'select start_session()::text');
  select id into v_sess from game_sessions where player_id = pg_temp.u(k) and ended_at is null order by started_at desc limit 1;
  perform testkit.as_user(pg_temp.u(k), format($q$select heartbeat(%L, 'driving', 'lagos-ibadan', 'lagos')::text$q$, v_sess));
end $$;
-- everything about a profile except the one preference under test
create function pg_temp.profile_fp() returns text language sql as $$ select md5(coalesce(string_agg((to_jsonb(p) - 'appear_nearby')::text, ';' order by p.id), '')) from profiles p $$;
create function pg_temp.geo_fp() returns text language sql as $$
  select md5((select coalesce(string_agg(c::text, ';' order by c.code), '') from countries c) || (select coalesce(string_agg(c::text, ';' order by c.id), '') from cities c)
          || (select coalesce(string_agg(l::text, ';' order by l.id), '') from locations l)) $$;

-- ===== the database default
select testkit.assert_eq('the column default is false', (select column_default from information_schema.columns where table_name = 'profiles' and column_name = 'appear_nearby' and table_schema = 'public'), 'false');
select testkit.assert_eq('a new account is hidden by default', (select string_agg(appear_nearby::text, ',' order by display_name) from profiles where id in (pg_temp.u('A'), pg_temp.u('B'), pg_temp.u('C'), pg_temp.u('D'))), 'false,false,false,false');
select testkit.assert_eq('the default is also what the player reads back', pg_temp.near('A')->>'visible', 'false');

-- ===== accounts that existed before the migration: they hold the OLD default (true) and some have a country, some do not
update profiles set appear_nearby = true where id in (pg_temp.u('B'), pg_temp.u('C'), pg_temp.u('D'));
update profiles set country_code = 'NG' where id = pg_temp.u('B');
update profiles set country_code = 'KE' where id = pg_temp.u('D');                                  -- C has no country at all (stays that way: nothing is inferred)
update profiles set experience = 4000, distance_km = 90, jobs_completed = 7, lifetime_earnings = 12000 where id = pg_temp.u('B');
select pg_temp.live('B'); select pg_temp.live('C'); select pg_temp.live('D');
select testkit.assert_eq('before the fix the old default exposes players (the situation the migration removes)', pg_temp.seen_by('D'), 'NoBravo,NoCharlie');
create temp table before_fp as select pg_temp.profile_fp() as p, pg_temp.geo_fp() as g,
  (select count(*) from profiles) as n, (select string_agg(id::text || ':' || coalesce(country_code, '-'), ',' order by id) from profiles) as countries;
\set mf :MIG /20261012000007_nearby_opt_in.sql
\i :mf
select testkit.assert_eq('after the migration every existing account is hidden', (select count(*) from profiles where appear_nearby)::text, '0');
select testkit.assert_eq('the migration changed nothing else on any profile (all other columns, byte for byte)', pg_temp.profile_fp(), (select p from before_fp));
select testkit.assert_eq('every existing player kept their country (and the player with none still has none)', (select string_agg(id::text || ':' || coalesce(country_code, '-'), ',' order by id) from profiles), (select countries from before_fp));
select testkit.assert_eq('countries, cities and locations are untouched', pg_temp.geo_fp(), (select g from before_fp));
select testkit.assert_eq('no account was added or removed', (select count(*) from profiles)::text, (select n::text from before_fp));
select testkit.assert_eq('the default for the next account is false', (select column_default from information_schema.columns where table_name = 'profiles' and column_name = 'appear_nearby' and table_schema = 'public'), 'false');
select testkit.assert_eq('existing players are no longer in each other''s lists', pg_temp.seen_by('D'), 'none');
select testkit.assert_eq('the migration can be repeated safely', (select count(*) from profiles where appear_nearby)::text, '0');

-- ===== explicit opt-in of an existing player: visible, and country, level and everything else are as they were
select testkit.assert_eq('an existing player opts in', testkit.as_user(pg_temp.u('B'), 'select set_nearby_visibility(true)::text'), 'true');
select testkit.assert_eq('...and appears for another player on the same route', pg_temp.seen_by('D'), 'NoBravo');
select testkit.assert_eq('...the other existing players stay hidden', (select appear_nearby::text from profiles where id = pg_temp.u('C')), 'false');
select testkit.assert_eq('opting in changed no profile column but the preference', pg_temp.profile_fp(), (select p from before_fp));

-- ===== a new player: onboarding does not switch it on, the player's explicit choice does
select pg_temp.live('A');
select testkit.assert_eq('a new account is not visible before onboarding', pg_temp.seen_by('D'), 'NoBravo');
select testkit.assert_eq('onboarding (country, language, style) is accepted', pg_temp.j(pg_temp.u('A'), $$select complete_onboarding('GH', 'en', 'jobs')::text$$)->>'onboarded', 'true');
select testkit.assert_eq('onboarding stored the chosen country', (select country_code from profiles where id = pg_temp.u('A')), 'GH');
select testkit.assert_eq('...and did not make the player visible', (select appear_nearby::text from profiles where id = pg_temp.u('A')), 'false');
select testkit.assert_eq('...so nobody sees the new player', pg_temp.seen_by('D'), 'NoBravo');
select testkit.assert_eq('the player chooses to appear (what the onboarding toggle sends)', testkit.as_user(pg_temp.u('A'), 'select set_nearby_visibility(true)::text'), 'true');
select testkit.assert_eq('...and is listed for others', pg_temp.seen_by('D'), 'NoAlpha,NoBravo');
select testkit.assert_eq('the choice is read back by its owner', pg_temp.near('A')->>'visible', 'true');

-- ===== opt-out: hidden, and no country, flag data, leaderboard row, friendship, profile or map entry goes with it
update profiles set experience = 6000, distance_km = 150, jobs_completed = 9, lifetime_earnings = 20000 where id = pg_temp.u('A');
select testkit.as_user(pg_temp.u('D'), format('select send_friend_request(%L)::text', pg_temp.u('A')));
select testkit.as_user(pg_temp.u('A'), format('select respond_friend_request(%L, true)', (select id from friendships where addressee_id = pg_temp.u('A') and requester_id = pg_temp.u('D'))));
create temp table pre_out as select pg_temp.profile_fp() as p, pg_temp.geo_fp() as g,
  (select count(*) from jsonb_array_elements(pg_temp.j(pg_temp.u('D'), $$select get_world_state()::text$$)->'cities')) as cities;
select testkit.assert_eq('opting out answers false', testkit.as_user(pg_temp.u('A'), 'select set_nearby_visibility(false)::text'), 'false');
select testkit.assert_eq('...and removes the player from Players Nearby', pg_temp.seen_by('D'), 'NoBravo');
select testkit.assert_eq('...and the owner reads it back', pg_temp.near('A')->>'visible', 'false');
select testkit.assert_eq('the country is still stored', (select country_code from profiles where id = pg_temp.u('A')), 'GH');
select testkit.assert_eq('my own profile still reports country code and name', (select (pg_temp.j(pg_temp.u('A'), 'select get_my_profile()::text')->>'country_code') || '/' || (pg_temp.j(pg_temp.u('A'), 'select get_my_profile()::text')->>'country_name')), 'GH/Ghana');
select testkit.assert_eq('another player''s view of the profile still carries the country', pg_temp.j(pg_temp.u('D'), format('select get_public_profile(%L)::text', pg_temp.u('A')))->>'country_code', 'GH');
select testkit.assert_eq('the global leaderboard still carries the country of a hidden player', (select e->>'country_code' from jsonb_array_elements(pg_temp.j(pg_temp.u('D'), $$select get_leaderboard('xp')::text$$)->'items') e where e->>'display_name' = 'NoAlpha'), 'GH');
select testkit.assert_eq('the leaderboard still lists every ranked player with their own country', (select string_agg(e->>'display_name' || ':' || coalesce(e->>'country_code', '-'), ',' order by e->>'display_name') from jsonb_array_elements(pg_temp.j(pg_temp.u('D'), $$select get_leaderboard('xp')::text$$)->'items') e where e->>'display_name' like 'No%'), 'NoAlpha:GH,NoBravo:NG');
select testkit.assert_eq('the friends leaderboard still carries the country', (select e->>'country_code' from jsonb_array_elements(pg_temp.j(pg_temp.u('D'), $$select get_friends_leaderboard('xp')::text$$)->'items') e where e->>'display_name' = 'NoAlpha'), 'GH');
select testkit.assert_eq('the ranked-player search still carries the country', (select e->>'country_code' from jsonb_array_elements(pg_temp.j(pg_temp.u('D'), $$select search_leaderboard('xp', 'all', 'NoAlpha')::text$$)->'items') e where e->>'display_name' = 'NoAlpha'), 'GH');
select testkit.assert_eq('the friends list still carries the country', (select e->>'country_code' from jsonb_array_elements(pg_temp.j(pg_temp.u('D'), $$select list_friends()::text$$)->'items') e where e->>'display_name' = 'NoAlpha'), 'GH');
select testkit.assert_eq('opting out changed no profile column but the preference', pg_temp.profile_fp(), (select p from pre_out));
select testkit.assert_eq('the map is untouched: same countries, cities and locations', pg_temp.geo_fp(), (select g from pre_out));
select testkit.assert_eq('the world map still lists every city with its country', (select count(*) from jsonb_array_elements(pg_temp.j(pg_temp.u('D'), $$select get_world_state()::text$$)->'cities') c where c->>'country_code' is not null and c->>'country' is not null)::text, (select cities::text from pre_out));
select testkit.assert_eq('opting back in works', testkit.as_user(pg_temp.u('A'), 'select set_nearby_visibility(true)::text'), 'true');
select testkit.assert_eq('...and the player is listed again', pg_temp.seen_by('D'), 'NoAlpha,NoBravo');

-- ===== a client that sends nothing never exposes anyone
select testkit.assert_eq('null means hidden', testkit.as_user(pg_temp.u('A'), 'select set_nearby_visibility(null)::text'), 'false');
select testkit.assert_eq('...and is stored as hidden', (select appear_nearby::text from profiles where id = pg_temp.u('A')), 'false');
select testkit.assert_eq('...and the country is still there', (select country_code from profiles where id = pg_temp.u('A')), 'GH');
select testkit.expect_error('anon', null, 'select set_nearby_visibility(true)', 'permission denied');
select testkit.expect_error('authenticated', null, 'select set_nearby_visibility(true)', 'not_authenticated');
select testkit.assert_eq('a client cannot write the preference directly', (select count(*) from information_schema.column_privileges where table_name = 'profiles' and column_name = 'appear_nearby' and grantee in ('authenticated','anon') and privilege_type = 'UPDATE')::text, '0');

select 'NEARBY OPT-IN TESTS PASSED' as result;
