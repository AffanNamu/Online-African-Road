-- Backend foundation, step 3: player profile + presence.
--   * profiles gain username, avatar, country, updated_at, last_seen_at, lifetime_earnings (all server-controlled)
--   * clients may change ONLY display_name / username / avatar_id / country_code, and only through update_my_profile
--   * presence is derived from game_sessions heartbeats with a server-side timeout (cfg presence_ttl_seconds)
create temp table _fn_before as select oid from pg_proc where pronamespace = 'public'::regnamespace;

-- ---------------------------------------------------------------- profile columns
alter table profiles
  add column username          text,
  add column avatar_id         text   not null default 'default' check (avatar_id ~ '^[a-z0-9_-]{1,32}$'),
  add column country_code      text   references countries(code),
  add column updated_at        timestamptz not null default now(),
  add column last_seen_at      timestamptz,
  add column lifetime_earnings bigint not null default 0 check (lifetime_earnings >= 0);

-- A URL/mention-safe handle derived from the display name; unique, lower-case.
create function _make_username(p_name text, p_id uuid) returns text
language plpgsql stable security definer set search_path = public as $$
declare v_base text; v_try text; v_hex text := substr(replace(p_id::text, '-', ''), 1, 8); i integer := 0;
begin
  v_base := regexp_replace(lower(coalesce(p_name, '')), '[^a-z0-9_]+', '_', 'g');
  v_base := regexp_replace(v_base, '^_+|_+$', '', 'g');
  if char_length(v_base) < 3 then v_base := 'driver_' || substr(v_hex, 1, 6); end if;
  v_base := left(v_base, 19);
  v_try := v_base;
  while exists (select 1 from profiles where username = v_try) loop
    i := i + 1;
    v_try := left(v_base, 19 - char_length(i::text)) || '_' || (case when i = 1 then substr(v_hex, 1, 3) else i::text end);
    exit when i > 50;
  end loop;
  return v_try;
end $$;

create function _profiles_set_username() returns trigger
language plpgsql security definer set search_path = public as $$
begin
  if new.username is null then new.username := _make_username(new.display_name, new.id); end if;
  return new;
end $$;
create trigger profiles_username_default before insert on profiles
  for each row execute function _profiles_set_username();

-- Backfill existing players one statement at a time so each pick sees the previous ones.
do $$ declare r record; begin
  for r in select id, display_name from profiles where username is null order by created_at, id loop
    update profiles set username = _make_username(r.display_name, r.id) where id = r.id;
  end loop;
end $$;
alter table profiles alter column username set not null;
alter table profiles add constraint profiles_username_fmt check (username ~ '^[a-z0-9_]{3,24}$');
create unique index profiles_username_key on profiles(username);

-- Backfill lifetime earnings from the immutable ledger (the ledger stays the source of truth).
update profiles p set lifetime_earnings = coalesce((
  select sum(t.amount) from transactions t
   where t.player_id = p.id and t.amount > 0 and t.kind in ('JOB_REWARD','BONUS')), 0);

create function _profiles_touch() returns trigger
language plpgsql security definer set search_path = public as $$
begin new.updated_at := now(); return new; end $$;
create trigger profiles_touch before update of display_name, username, avatar_id, country_code on profiles
  for each row execute function _profiles_touch();

-- Leaderboard indexes (one per ranked metric; id breaks ties so pagination is stable).
create index profiles_rank_xp_idx       on profiles(experience        desc, id);
create index profiles_rank_distance_idx on profiles(distance_km       desc, id);
create index profiles_rank_jobs_idx     on profiles(jobs_completed    desc, id);
create index profiles_rank_earnings_idx on profiles(lifetime_earnings desc, id);

-- Clients keep reading public profile columns directly (existing behaviour); private columns are RPC-only.
revoke select on profiles from authenticated;
grant select (id, display_name, username, avatar_id, country_code, level, experience, distance_km, jobs_completed, created_at)
  on profiles to authenticated;

-- ---------------------------------------------------------------- XP maths (single definition, server-side)
-- level_for_xp(xp) = 1 + floor(sqrt(xp/100))  =>  level L starts at 100 * (L-1)^2 XP.
create function xp_for_level(p_level integer) returns bigint
language sql immutable as $$ select (100 * (greatest(p_level, 1) - 1) ^ 2)::bigint $$;

-- ---------------------------------------------------------------- profile API
create function get_my_profile() returns jsonb
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
    'country_code', p.country_code, 'level', p.level, 'experience', p.experience,
    'xp_level_start', xp_for_level(p.level), 'xp_next_level', xp_for_level(p.level + 1),
    'distance_km', p.distance_km, 'jobs_completed', p.jobs_completed,
    'jobs_total', (select count(*) from job_assignments where player_id = v_uid),
    'coins', coalesce(v_bal, 0), 'lifetime_earnings', p.lifetime_earnings,
    'created_at', p.created_at, 'updated_at', p.updated_at, 'last_seen_at', p.last_seen_at,
    'server_time', now());
end $$;

-- Null argument = leave unchanged. Protected statistics (XP, level, distance, jobs, coins) have no path from here.
create function update_my_profile(p_display_name text default null, p_username text default null,
                                  p_avatar_id text default null, p_country_code text default null) returns jsonb
language plpgsql security definer set search_path = public as $$
declare v_uid uuid := auth.uid();
begin
  if v_uid is null then raise exception 'not_authenticated'; end if;
  perform _rate_limit('profile_write', 10, 60);
  if p_display_name is not null and p_display_name !~ '^[A-Za-z0-9_ -]{3,24}$' then raise exception 'invalid_display_name'; end if;
  if p_username is not null and p_username !~ '^[a-z0-9_]{3,24}$' then raise exception 'invalid_username'; end if;
  if p_avatar_id is not null and p_avatar_id !~ '^[a-z0-9_-]{1,32}$' then raise exception 'invalid_avatar'; end if;
  if p_country_code is not null and p_country_code !~ '^[A-Z]{2}$' then raise exception 'unknown_country'; end if;
  begin
    update profiles set display_name = coalesce(trim(p_display_name), display_name),
                        username     = coalesce(p_username, username),
                        avatar_id    = coalesce(p_avatar_id, avatar_id),
                        country_code = coalesce(p_country_code, country_code)
     where id = v_uid;
  exception
    when unique_violation then
      if sqlerrm like '%username%' then raise exception 'username_taken'; end if;
      raise exception 'display_name_taken';
    when foreign_key_violation then raise exception 'unknown_country';
  end;
  return get_my_profile();
end $$;

-- ---------------------------------------------------------------- sessions + presence
create function _client_label(p_client text) returns text
language sql immutable as $$ select case when p_client ~ '^[A-Za-z0-9_.-]{1,24}$' then p_client else 'unknown' end $$;

-- Opens an authenticated app session. The id is generated by the server and is the handle later multiplayer code
-- can attach to; timestamps are always server time.
create function start_session(p_client text default 'webgl') returns jsonb
language plpgsql security definer set search_path = public as $$
declare v_uid uuid := auth.uid(); v_id uuid; v_ttl interval := make_interval(secs => cfg('presence_ttl_seconds'));
begin
  if v_uid is null then raise exception 'not_authenticated'; end if;
  perform _rate_limit('session_start', 10, 60);
  update game_sessions set ended_at = last_seen_at, end_reason = 'timeout'
   where player_id = v_uid and ended_at is null and last_seen_at <= now() - v_ttl;
  update game_sessions set ended_at = now(), end_reason = 'replaced'
   where id in (select id from game_sessions where player_id = v_uid and ended_at is null
                 order by last_seen_at desc offset greatest(cfg('session_live_cap')::int - 1, 0));
  insert into game_sessions(player_id, client) values (v_uid, _client_label(p_client)) returning id into v_id;
  update profiles set last_seen_at = now() where id = v_uid;
  perform _track(v_uid, 'login', jsonb_build_object('client', _client_label(p_client)), v_id);
  return jsonb_build_object('session_id', v_id, 'server_time', now(), 'ttl_seconds', cfg('presence_ttl_seconds'));
end $$;

create function heartbeat(p_session uuid, p_activity text default 'menu', p_route text default null, p_city text default null) returns jsonb
language plpgsql security definer set search_path = public as $$
declare v_uid uuid := auth.uid(); s game_sessions; v_ttl interval := make_interval(secs => cfg('presence_ttl_seconds'));
begin
  if v_uid is null then raise exception 'not_authenticated'; end if;
  perform _rate_limit('heartbeat', 20, 60);
  select * into s from game_sessions where id = p_session and player_id = v_uid for update;
  if not found then raise exception 'session_not_found'; end if;
  if s.ended_at is not null or s.last_seen_at <= now() - v_ttl then raise exception 'session_expired'; end if;
  if p_activity not in ('menu','driving','bus','convoy','idle') then raise exception 'invalid_activity'; end if;
  if p_route is not null and not exists (select 1 from world_routes where code = p_route) then raise exception 'unknown_route'; end if;
  if p_city  is not null and not exists (select 1 from cities where slug = p_city) then raise exception 'unknown_city'; end if;
  update game_sessions set last_seen_at = now(), activity = p_activity, route_code = p_route, city_slug = p_city where id = p_session;
  update profiles set last_seen_at = now() where id = v_uid and (last_seen_at is null or last_seen_at < now() - interval '30 seconds');
  return jsonb_build_object('server_time', now(), 'online_players', get_online_count());
end $$;

-- Idempotent: ending an already-ended session is a no-op. Someone else's session is "not found".
create function end_session(p_session uuid) returns void
language plpgsql security definer set search_path = public as $$
declare v_uid uuid := auth.uid(); s game_sessions;
begin
  if v_uid is null then raise exception 'not_authenticated'; end if;
  perform _rate_limit('session_end', 20, 60);
  select * into s from game_sessions where id = p_session and player_id = v_uid for update;
  if not found then raise exception 'session_not_found'; end if;
  if s.ended_at is not null then return; end if;
  update game_sessions set ended_at = now(), end_reason = 'logout' where id = p_session;
  update profiles set last_seen_at = now() where id = v_uid;
  perform _track(v_uid, 'logout', jsonb_build_object('duration_s', round(extract(epoch from (now() - s.started_at)))), p_session);
end $$;

-- Distinct players with a live session. Counts only; no identities.
create function get_online_count() returns integer
language plpgsql security definer set search_path = public as $$
begin
  if auth.uid() is null then raise exception 'not_authenticated'; end if;
  perform _rate_limit('online_count', 120, 60);
  return (select count(distinct player_id)::int from game_sessions
           where ended_at is null and last_seen_at > now() - make_interval(secs => cfg('presence_ttl_seconds')));
end $$;

-- Presence of one player from the server's point of view (internal; visibility rules live in the public endpoints).
create function _presence_of(p_player uuid) returns jsonb
language plpgsql stable security definer set search_path = public as $$
declare s game_sessions;
begin
  select * into s from game_sessions
   where player_id = p_player and ended_at is null and last_seen_at > now() - make_interval(secs => cfg('presence_ttl_seconds'))
   order by last_seen_at desc limit 1;
  if found then
    return jsonb_build_object('online', true, 'session_id', s.id, 'activity', s.activity, 'route_code', s.route_code,
                              'city_slug', s.city_slug, 'last_seen_at', s.last_seen_at);
  end if;
  return jsonb_build_object('online', false, 'last_seen_at', (select last_seen_at from profiles where id = p_player));
end $$;

create function get_my_presence() returns jsonb
language plpgsql security definer set search_path = public as $$
declare v_uid uuid := auth.uid(); v_conv convoys;
begin
  if v_uid is null then raise exception 'not_authenticated'; end if;
  perform _rate_limit('presence_read', 120, 60);
  select c.* into v_conv from convoy_members m join convoys c on c.id = m.convoy_id
   where m.player_id = v_uid and c.disbanded_at is null;
  return _presence_of(v_uid) || jsonb_build_object('convoy_id', v_conv.id, 'convoy_name', v_conv.name, 'server_time', now());
end $$;

-- Closes sessions whose heartbeat stopped (service role / cron).
create function sweep_stale_sessions() returns integer
language plpgsql security definer set search_path = public as $$
declare n integer;
begin
  update game_sessions set ended_at = last_seen_at, end_reason = 'timeout'
   where ended_at is null and last_seen_at <= now() - make_interval(secs => cfg('presence_ttl_seconds'));
  get diagnostics n = row_count;
  return n;
end $$;

-- ---------------------------------------------------------------- lock-down
do $$ declare r record;
begin
  for r in select p.oid::regprocedure as sig, p.proname from pg_proc p
            where p.pronamespace = 'public'::regnamespace and p.oid not in (select oid from _fn_before) loop
    execute format('revoke execute on function %s from public, anon, authenticated', r.sig);
    if r.proname in ('get_my_profile','update_my_profile','start_session','heartbeat','end_session','get_online_count','get_my_presence') then
      execute format('grant execute on function %s to authenticated', r.sig);
    end if;
  end loop;
end $$;
