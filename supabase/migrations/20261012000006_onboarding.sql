-- First-run onboarding + the player's own preferences.
--   * languages: the languages a player can choose (reference data, readable by signed-in players, never writable by clients).
--     ui_ready says whether the game is actually translated into it; today only English is, and the client says so instead of pretending.
--   * profiles gain language_code, play_style and onboarded_at. They are PRIVATE (RPC-only): public profiles, friends and boards never carry them.
--   * complete_onboarding(country, language, play style, optional driver name) is the one call a new player makes; it is replay-safe and keeps the first completion time.
--   * set_my_preferences(language, play style) changes them later (the profile page).
--   * get_my_profile now also reports them, plus the names of the country and the language, so the client needs no second lookup.
--   * Everyone who already played before this migration counts as onboarded; only accounts created afterwards are asked.
create temp table _fn_before as select oid from pg_proc where pronamespace = 'public'::regnamespace;

create table languages (
  code        text primary key check (code ~ '^[a-z]{2,3}$'),
  name        text not null unique,
  native_name text not null,
  ui_ready    boolean not null default false
);
insert into languages(code, name, native_name, ui_ready) values
  ('en', 'English',    'English',    true),
  ('fr', 'French',     'Français',   false),
  ('pt', 'Portuguese', 'Português',  false),
  ('ar', 'Arabic',     'Arabic',     false),
  ('sw', 'Swahili',    'Kiswahili',  false),
  ('ha', 'Hausa',      'Hausa',      false),
  ('yo', 'Yoruba',     'Yorùbá',     false),
  ('ig', 'Igbo',       'Igbo',       false),
  ('am', 'Amharic',    'Amharic',    false),
  ('zu', 'Zulu',       'isiZulu',    false),
  ('af', 'Afrikaans',  'Afrikaans',  false),
  ('so', 'Somali',     'Somali',     false);
alter table languages enable row level security;
create policy "read languages" on languages for select to authenticated using (true);
revoke all on languages from public, anon, authenticated;
grant select on languages to authenticated;

alter table profiles
  add column language_code text references languages(code),
  add column play_style    text check (play_style in ('jobs','bus','convoy','explore')),
  add column onboarded_at  timestamptz;
update profiles set onboarded_at = now() where onboarded_at is null;       -- players from before onboarding existed are not asked again

-- ---------------------------------------------------------------- profile read (adds the preferences)
create or replace function get_my_profile() returns jsonb
language plpgsql security definer set search_path = public as $$
declare v_uid uuid := auth.uid(); p profiles; v_bal bigint;
begin
  if v_uid is null then raise exception 'not_authenticated'; end if;
  perform _rate_limit('profile_read', 120, 60);
  select * into p from profiles where id = v_uid;
  if not found then raise exception 'profile_not_found'; end if;
  select balance into v_bal from player_wallets where player_id = v_uid;
  return jsonb_build_object(
    'id', p.id, 'display_name', p.display_name, 'username', p.username, 'avatar_id', p.avatar_id,
    'country_code', p.country_code, 'country_name', (select c.name from countries c where c.code = p.country_code),
    'language_code', p.language_code, 'language_name', (select l.name from languages l where l.code = p.language_code),
    'play_style', p.play_style, 'onboarded', p.onboarded_at is not null, 'onboarded_at', p.onboarded_at,
    'level', p.level, 'experience', p.experience,
    'xp_level_start', xp_for_level(p.level), 'xp_next_level', xp_for_level(p.level + 1),
    'distance_km', p.distance_km, 'jobs_completed', p.jobs_completed,
    'jobs_total', (select count(*) from job_assignments where player_id = v_uid),
    'coins', coalesce(v_bal, 0), 'lifetime_earnings', p.lifetime_earnings,
    'vehicle_count', (select count(*) from vehicle_ownership where player_id = v_uid),
    'friend_count', (select count(*) from friendships f where f.status = 'accepted' and v_uid in (f.requester_id, f.addressee_id)),
    'created_at', p.created_at, 'updated_at', p.updated_at, 'last_seen_at', p.last_seen_at,
    'server_time', now());
end $$;

-- ---------------------------------------------------------------- shared validation
create function _check_preferences(p_language text, p_play_style text) returns void
language plpgsql stable security definer set search_path = public as $$
begin
  if p_language is not null and not exists (select 1 from languages where code = p_language) then raise exception 'unknown_language'; end if;
  if p_play_style is not null and p_play_style not in ('jobs','bus','convoy','explore') then raise exception 'invalid_play_style'; end if;
end $$;

-- ---------------------------------------------------------------- the onboarding call
create function complete_onboarding(p_country_code text, p_language_code text, p_play_style text, p_display_name text default null) returns jsonb
language plpgsql security definer set search_path = public as $$
declare v_uid uuid := auth.uid();
begin
  if v_uid is null then raise exception 'not_authenticated'; end if;
  perform _rate_limit('onboarding', 10, 60);
  if p_country_code is null or p_country_code !~ '^[A-Z]{2}$' or not exists (select 1 from countries where code = p_country_code) then raise exception 'unknown_country'; end if;
  if p_language_code is null then raise exception 'unknown_language'; end if;
  if p_play_style is null then raise exception 'invalid_play_style'; end if;
  perform _check_preferences(p_language_code, p_play_style);
  if p_display_name is not null and p_display_name !~ '^[A-Za-z0-9_ -]{3,24}$' then raise exception 'invalid_display_name'; end if;
  begin
    update profiles set country_code = p_country_code, language_code = p_language_code, play_style = p_play_style,
                        display_name = coalesce(trim(p_display_name), display_name),
                        onboarded_at = coalesce(onboarded_at, now())
     where id = v_uid;
  exception when unique_violation then raise exception 'display_name_taken';
  end;
  return get_my_profile();
end $$;

-- ---------------------------------------------------------------- later changes (null leaves a value alone)
create function set_my_preferences(p_language_code text default null, p_play_style text default null) returns jsonb
language plpgsql security definer set search_path = public as $$
declare v_uid uuid := auth.uid();
begin
  if v_uid is null then raise exception 'not_authenticated'; end if;
  perform _rate_limit('profile_write', 10, 60);
  perform _check_preferences(p_language_code, p_play_style);
  update profiles set language_code = coalesce(p_language_code, language_code), play_style = coalesce(p_play_style, play_style) where id = v_uid;
  return get_my_profile();
end $$;

-- ---------------------------------------------------------------- lock-down
do $$ declare r record;
begin
  for r in select p.oid::regprocedure as sig, p.proname from pg_proc p
            where p.pronamespace = 'public'::regnamespace and p.oid not in (select oid from _fn_before) loop
    execute format('revoke execute on function %s from public, anon, authenticated', r.sig);
    if r.proname in ('complete_onboarding','set_my_preferences') then
      execute format('grant execute on function %s to authenticated', r.sig);
    end if;
  end loop;
end $$;
