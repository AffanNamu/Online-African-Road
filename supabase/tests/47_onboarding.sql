\set ON_ERROR_STOP on
-- First-run onboarding: country, language, play style (and the driver name) are validated by the server, private to the player, and replay-safe.
create function pg_temp.j(p_uid uuid, p_sql text) returns jsonb language sql as $$ select testkit.as_user(p_uid, p_sql)::jsonb $$;
create temp table fx(k text primary key, v text);
insert into fx select k, testkit.make_user(n)::text from (values ('A','ObAlpha'),('B','ObBravo'),('C','ObCharlie'),('D','ObDelta')) t(k, n);
create function pg_temp.u(k text) returns uuid language sql as $$ select v::uuid from fx where fx.k = $1 $$;
create function pg_temp.me(k text) returns jsonb language sql as $$ select pg_temp.j(pg_temp.u($1), 'select get_my_profile()::text') $$;
insert into countries(code, name) values ('GH', 'Ghana') on conflict do nothing;

-- ===== a new account is not onboarded and has no preferences
select testkit.assert_eq('a new player is not onboarded', pg_temp.me('A')->>'onboarded', 'false');
select testkit.assert_eq('no language yet', coalesce(pg_temp.me('A')->>'language_code', 'none'), 'none');
select testkit.assert_eq('no play style yet', coalesce(pg_temp.me('A')->>'play_style', 'none'), 'none');
select testkit.assert_eq('no completion time yet', coalesce(pg_temp.me('A')->>'onboarded_at', 'none'), 'none');

-- ===== the languages are reference data players can read and never change
select testkit.assert_eq('English is the only language the game is translated into', (select string_agg(code, ',' order by code) from languages where ui_ready), 'en');
select testkit.assert_eq('a signed-in player can read the languages', testkit.as_user(pg_temp.u('A'), 'select (count(*) >= 10)::text from languages'), 'true');
select testkit.expect_error('authenticated', pg_temp.u('A'), $$insert into languages(code, name, native_name) values ('xx','Fake','Fake')$$, 'permission denied');
select testkit.expect_error('authenticated', pg_temp.u('A'), $$update languages set ui_ready = true$$, 'permission denied');
select testkit.expect_error('authenticated', pg_temp.u('A'), $$delete from languages$$, 'permission denied');
select testkit.expect_error('anon', null, $$select * from languages$$, 'permission denied');

-- ===== validation: every field is checked by the server, nothing is stored on a refusal
select testkit.expect_error('authenticated', pg_temp.u('A'), $$select complete_onboarding('ZZ', 'en', 'jobs')$$, 'unknown_country');
select testkit.expect_error('authenticated', pg_temp.u('A'), $$select complete_onboarding('ng', 'en', 'jobs')$$, 'unknown_country');
select testkit.expect_error('authenticated', pg_temp.u('A'), $$select complete_onboarding(null, 'en', 'jobs')$$, 'unknown_country');
select testkit.expect_error('authenticated', pg_temp.u('A'), $$select complete_onboarding('NG', 'xx', 'jobs')$$, 'unknown_language');
select testkit.expect_error('authenticated', pg_temp.u('A'), $$select complete_onboarding('NG', null, 'jobs')$$, 'unknown_language');
select testkit.expect_error('authenticated', pg_temp.u('A'), $$select complete_onboarding('NG', 'en', 'racing')$$, 'invalid_play_style');
select testkit.expect_error('authenticated', pg_temp.u('A'), $$select complete_onboarding('NG', 'en', null)$$, 'invalid_play_style');
select testkit.expect_error('authenticated', pg_temp.u('A'), $$select complete_onboarding('NG', 'en', 'jobs', 'ab')$$, 'invalid_display_name');
select testkit.expect_error('authenticated', pg_temp.u('A'), $$select complete_onboarding('NG', 'en', 'jobs', 'bad;name')$$, 'invalid_display_name');
select testkit.expect_error('authenticated', pg_temp.u('A'), $$select complete_onboarding('NG', 'en', 'jobs', 'ObBravo')$$, 'display_name_taken');
select testkit.expect_error('anon', null, $$select complete_onboarding('NG', 'en', 'jobs')$$, 'permission denied');
select testkit.expect_error('authenticated', null, $$select complete_onboarding('NG', 'en', 'jobs')$$, 'not_authenticated');
select testkit.assert_eq('after every refusal the player is still not onboarded', pg_temp.me('A')->>'onboarded', 'false');

-- ===== completing it
select testkit.assert_eq('the reply is the profile and says onboarded', pg_temp.j(pg_temp.u('A'), $$select complete_onboarding('NG', 'ha', 'convoy')::text$$)->>'onboarded', 'true');
select testkit.assert_eq('country stored', pg_temp.me('A')->>'country_code', 'NG');
select testkit.assert_eq('with its name', pg_temp.me('A')->>'country_name', 'Nigeria');
select testkit.assert_eq('language stored', pg_temp.me('A')->>'language_code', 'ha');
select testkit.assert_eq('with its name', pg_temp.me('A')->>'language_name', 'Hausa');
select testkit.assert_eq('play style stored', pg_temp.me('A')->>'play_style', 'convoy');
select testkit.assert_eq('the name was left alone', pg_temp.me('A')->>'display_name', 'ObAlpha');
select testkit.assert_eq('completion time recorded', ((pg_temp.me('A')->>'onboarded_at') is not null)::text, 'true');
create temp table first_done as select pg_temp.me('A')->>'onboarded_at' as t;
-- replay-safe: doing it again changes the answers but not the first completion time
select testkit.assert_eq('a second call updates the answers', pg_temp.j(pg_temp.u('A'), $$select complete_onboarding('GH', 'en', 'bus', 'ObAlphaNew')::text$$)->>'play_style', 'bus');
select testkit.assert_eq('country changed', pg_temp.me('A')->>'country_code', 'GH');
select testkit.assert_eq('name changed', pg_temp.me('A')->>'display_name', 'ObAlphaNew');
select testkit.assert_eq('the first completion time is kept', pg_temp.me('A')->>'onboarded_at', (select t from first_done));
select testkit.assert_eq('the username is not touched by a name change', pg_temp.me('A')->>'username', 'obalpha');

-- ===== later changes
select testkit.assert_eq('language only', pg_temp.j(pg_temp.u('A'), $$select set_my_preferences('fr')::text$$)->>'language_code', 'fr');
select testkit.assert_eq('the play style was left alone', pg_temp.me('A')->>'play_style', 'bus');
select testkit.assert_eq('play style only', pg_temp.j(pg_temp.u('A'), $$select set_my_preferences(null, 'explore')::text$$)->>'play_style', 'explore');
select testkit.assert_eq('the language was left alone', pg_temp.me('A')->>'language_code', 'fr');
select testkit.expect_error('authenticated', pg_temp.u('A'), $$select set_my_preferences('xx')$$, 'unknown_language');
select testkit.expect_error('authenticated', pg_temp.u('A'), $$select set_my_preferences(null, 'racing')$$, 'invalid_play_style');
select testkit.expect_error('anon', null, $$select set_my_preferences('en')$$, 'permission denied');
select testkit.expect_error('authenticated', null, $$select set_my_preferences('en')$$, 'not_authenticated');
select testkit.assert_eq('changing preferences never onboards anyone', pg_temp.j(pg_temp.u('B'), $$select set_my_preferences('sw', 'jobs')::text$$)->>'onboarded', 'false');

-- ===== privacy: nothing about preferences leaves the player's own profile
select testkit.expect_error('authenticated', pg_temp.u('B'), format($$select language_code from profiles where id = %L$$, pg_temp.u('A')), 'permission denied');
select testkit.expect_error('authenticated', pg_temp.u('B'), format($$select play_style from profiles where id = %L$$, pg_temp.u('A')), 'permission denied');
select testkit.expect_error('authenticated', pg_temp.u('B'), format($$select onboarded_at from profiles where id = %L$$, pg_temp.u('A')), 'permission denied');
select testkit.expect_error('authenticated', pg_temp.u('B'), format($$update profiles set onboarded_at = null where id = %L$$, pg_temp.u('B')), 'permission denied');
select testkit.assert_eq('a public profile carries no preferences', (select count(*)::text from jsonb_object_keys(pg_temp.j(pg_temp.u('B'), format($$select get_public_profile(%L)::text$$, pg_temp.u('A')))) k where k in ('language_code','play_style','onboarded','onboarded_at','coins')), '0');
select testkit.assert_eq('a leaderboard row carries no preferences', (select count(*)::text from jsonb_array_elements(pg_temp.j(pg_temp.u('B'), $$select get_leaderboard('xp','all',50,0)::text$$)->'items') e where e ? 'language_code' or e ? 'play_style'), '0');

-- ===== the profile's own numbers
select testkit.assert_eq('vehicle_count counts the starter vehicle', pg_temp.me('C')->>'vehicle_count', '1');
select testkit.assert_eq('friend_count starts at 0', pg_temp.me('C')->>'friend_count', '0');
insert into fx select 'req', pg_temp.j(pg_temp.u('C'), format('select send_friend_request(%L)::text', pg_temp.u('D')))->>'request_id';
select testkit.assert_eq('a pending request is not a friend', pg_temp.me('C')->>'friend_count', '0');
select testkit.as_user(pg_temp.u('D'), format('select respond_friend_request(%L, true)', (select v from fx where k = 'req')));
select testkit.assert_eq('an accepted one is', pg_temp.me('C')->>'friend_count', '1');
select testkit.assert_eq('and counts for both', pg_temp.me('D')->>'friend_count', '1');

-- ===== floods are refused
do $$
declare i int; limited int := 0; ok int := 0;
begin
  for i in 1..25 loop
    begin perform testkit.as_user((select v::uuid from fx where k = 'C'), $q$select complete_onboarding('NG', 'en', 'jobs')$q$); ok := ok + 1;
    exception when others then if sqlerrm like '%rate_limited%' then limited := limited + 1; else raise; end if; end;
  end loop;
  if limited < 5 or ok > 20 then raise exception 'complete_onboarding was not rate limited (ok %, limited %)', ok, limited; end if;
end $$;
select 'ONBOARDING TESTS PASSED' as result;
