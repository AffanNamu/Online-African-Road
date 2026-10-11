-- Multiplayer: the convoy lobby.
--   MULTIPLAYER -> Find/Create convoy -> Select route -> Invite/Join -> Lobby -> Ready check -> Start driving -> Shared world
-- A convoy is a DURABLE lobby in Postgres. The live Relay session is created only when the leader starts driving (the code is then stored
-- with set_convoy_session_code and released to members through get_convoy_session_code, as before). Authority:
--   * only the leader chooses the route, tags and when to start; the server checks route access for the leader and for EVERY member at start
--   * every member sets only their own ready flag; changing the route clears everyone's ready flag
--   * the leader can start only when a route is chosen and every other member is ready
--   * chat is members-only, rate limited, length bounded, stripped of control and angle-bracket characters; system lines are written by the server
--   * players list / server status expose only what leaderboards already expose (name, level, vehicle) - presence details stay friends-only
create temp table _fn_before as select oid from pg_proc where pronamespace = 'public'::regnamespace;

-- ---------------------------------------------------------------- columns
alter table convoys
  add column phase      text not null default 'lobby' check (phase in ('lobby','driving')),
  add column route_code text references world_routes(code) on delete set null,
  add column tags       text[] not null default '{}' check (cardinality(tags) <= 3 and tags <@ array['jobs','freight','chill_drive','realistic_driving','training','help_support','friendly','long_haul','events']),
  add column language   text not null default 'en' check (language ~ '^[a-z]{2}$'),
  add column started_at timestamptz,
  add column driving_mode text not null default 'relaxed' check (driving_mode in ('relaxed','realistic')),
  add column min_level    integer not null default 1 check (min_level between 1 and 50),
  add column password_salt text,
  add column password_hash text,
  add constraint convoy_password_pair check ((password_salt is null) = (password_hash is null));
alter table convoy_members add column ready boolean not null default false;
create index convoys_phase_idx on convoys(phase) where disbanded_at is null;

-- the analytics whitelist gains the new event (the old list is kept as is)
alter table analytics_events drop constraint analytics_events_name_check;
alter table analytics_events add constraint analytics_events_name_check check (name in (
  'login','logout','job_accepted','job_completed','job_abandoned','route_unlocked','vehicle_purchased',
  'convoy_created','convoy_joined','convoy_left','multiplayer_session_started','multiplayer_session_ended',
  'level_up','achievement_unlocked','challenge_completed','friend_added','convoy_started','convoy_disbanded'));

-- ---------------------------------------------------------------- chat
create table convoy_messages (
  id        bigint generated always as identity primary key,
  convoy_id uuid not null references convoys(id) on delete cascade,
  sender_id uuid references profiles(id) on delete set null,
  kind      text not null default 'chat' check (kind in ('chat','system')),
  body      text not null check (char_length(body) between 1 and 200),
  moderation text not null default 'ok' check (moderation in ('ok','hidden','removed')),
  created_at timestamptz not null default clock_timestamp(),
  check ((kind = 'system') = (sender_id is null))
);
create index convoy_messages_convoy_idx on convoy_messages(convoy_id, id);
alter table convoy_messages enable row level security;
revoke all on convoy_messages from anon, authenticated;

-- safety tools: a player can block another (their chat lines disappear for the blocker, they leave the blocker's player lists) and report a chat line;
-- three different reporters hide the line automatically until a moderator looks at it
create table player_blocks (
  blocker_id uuid not null references profiles(id) on delete cascade,
  blocked_id uuid not null references profiles(id) on delete cascade,
  created_at timestamptz not null default now(),
  primary key (blocker_id, blocked_id), check (blocker_id <> blocked_id)
);
create table chat_reports (
  message_id  bigint not null references convoy_messages(id) on delete cascade,
  reporter_id uuid not null references profiles(id) on delete cascade,
  reason      text not null check (reason in ('spam','abuse','harassment','other')),
  created_at  timestamptz not null default now(),
  primary key (message_id, reporter_id)
);
alter table player_blocks enable row level security; alter table chat_reports enable row level security;
revoke all on player_blocks, chat_reports from anon, authenticated;

create function _chat_report_added() returns trigger
language plpgsql security definer set search_path = public as $$
begin
  if (select count(*) from chat_reports where message_id = new.message_id) >= 3 then update convoy_messages set moderation = 'hidden' where id = new.message_id and moderation = 'ok'; end if;
  return new;
end $$;
create trigger chat_reports_added after insert on chat_reports for each row execute function _chat_report_added();

create function _convoy_sys(p_convoy uuid, p_body text) returns void
language sql security definer set search_path = public as $$
  insert into convoy_messages(convoy_id, kind, body) values (p_convoy, 'system', left(p_body, 200)) $$;

create function _name_of(p_player uuid) returns text
language sql stable security definer set search_path = public as $$ select coalesce(display_name, 'A driver') from profiles where id = p_player $$;

-- system lines for membership and readiness (triggers, so every path writes them)
create function _convoy_sys_member_added() returns trigger
language plpgsql security definer set search_path = public as $$
begin
  perform _convoy_sys(new.convoy_id, _name_of(new.player_id) || case when new.player_id = (select leader_id from convoys where id = new.convoy_id) then ' created the convoy.' else ' joined the convoy.' end);
  return new;
end $$;
create trigger convoy_members_sys_added after insert on convoy_members for each row execute function _convoy_sys_member_added();

create function _convoy_sys_member_removed() returns trigger
language plpgsql security definer set search_path = public as $$
begin
  if exists (select 1 from convoys where id = old.convoy_id and disbanded_at is null) then
    perform _convoy_sys(old.convoy_id, _name_of(old.player_id) || ' left the convoy.');
  end if;
  return old;
end $$;
create trigger convoy_members_sys_removed after delete on convoy_members for each row execute function _convoy_sys_member_removed();

create function _convoy_sys_ready() returns trigger
language plpgsql security definer set search_path = public as $$
begin
  if new.ready is distinct from old.ready then
    perform _convoy_sys(new.convoy_id, _name_of(new.player_id) || case when new.ready then ' is ready.' else ' is not ready.' end);
  end if;
  return new;
end $$;
create trigger convoy_members_sys_ready after update of ready on convoy_members for each row execute function _convoy_sys_ready();

-- ---------------------------------------------------------------- json shapes (replaces the 20261010000005 version, same signature)
create function _route_json(p_code text) returns jsonb
language sql stable security definer set search_path = public as $$
  select case when r.id is null then null else jsonb_build_object('code', r.code, 'name', r.name,
           'from', (select name from cities where id = r.from_city_id), 'to', (select name from cities where id = r.to_city_id),
           'distance_km', r.distance_km, 'est_minutes', case when r.distance_km is null then null else greatest(1, round(r.distance_km / cfg('avg_speed_kmh') * 60))::int end,
           'unlock_level', r.unlock_level, 'status', r.status::text) end
  from (select 1) x left join world_routes r on r.code = p_code $$;

create function _member_vehicle(p_player uuid) returns text
language sql stable security definer set search_path = public as $$
  select d.name from vehicle_ownership vo join vehicle_definitions d on d.id = vo.definition_id
   where vo.player_id = p_player order by (vo.id = (select selected_vehicle_id from profiles where id = p_player)) desc, vo.acquired_at limit 1 $$;

create or replace function _convoy_json(p_viewer uuid, c convoys) returns jsonb
language plpgsql stable security definer set search_path = public as $$
declare v_members jsonb; v_count integer;
begin
  select coalesce(jsonb_agg((_person_json(p_viewer, m.player_id) - 'friend_status') || jsonb_build_object(
           'is_leader', m.player_id = c.leader_id, 'joined_at', m.joined_at, 'ready', m.ready or m.player_id = c.leader_id,
           'vehicle', _member_vehicle(m.player_id),
           'connected', exists (select 1 from game_sessions s where s.player_id = m.player_id and s.ended_at is null and s.last_seen_at > now() - make_interval(secs => cfg('presence_ttl_seconds')))) order by m.joined_at, m.player_id), '[]'::jsonb), count(*)
    into v_members, v_count from convoy_members m where m.convoy_id = c.id;
  return jsonb_build_object('id', c.id, 'name', c.name, 'leader_id', c.leader_id, 'is_leader', p_viewer = c.leader_id,
    'capacity', c.capacity, 'is_private', c.is_private, 'member_count', v_count,
    'status', case when c.disbanded_at is not null then 'disbanded' when c.phase = 'driving' then 'in_progress' when v_count >= c.capacity then 'full'
                   when c.session_code is not null then 'in_session' else 'open' end,
    'phase', c.phase, 'route', _route_json(c.route_code), 'tags', to_jsonb(c.tags), 'language', c.language, 'started_at', c.started_at,
    'driving_mode', c.driving_mode, 'min_level', c.min_level, 'has_password', c.password_hash is not null,
    'invited', coalesce((select jsonb_agg(jsonb_build_object('player_id', i.invitee_id, 'display_name', p.display_name, 'level', p.level, 'created_at', i.created_at) order by i.created_at)
                           from convoy_invites i join profiles p on p.id = i.invitee_id where i.convoy_id = c.id and i.status = 'pending' and i.expires_at > now()), '[]'::jsonb),
    'all_ready', not exists (select 1 from convoy_members m where m.convoy_id = c.id and m.player_id <> c.leader_id and not m.ready),
    'session_active', c.session_code is not null, 'created_at', c.created_at, 'members', v_members);
end $$;

-- ---------------------------------------------------------------- leader actions
create function _my_led_convoy() returns convoys
language plpgsql security definer set search_path = public as $$
declare c convoys;
begin
  select * into c from convoys where leader_id = auth.uid() and disbanded_at is null for update;
  if not found then raise exception 'not_leader'; end if;
  return c;
end $$;

create function set_convoy_route(p_route text) returns jsonb
language plpgsql security definer set search_path = public as $$
declare v_uid uuid := auth.uid(); c convoys; r world_routes; v_level integer;
begin
  if v_uid is null then raise exception 'not_authenticated'; end if;
  perform _rate_limit('convoy_write', 60, 60);
  c := _my_led_convoy();
  if c.phase <> 'lobby' then raise exception 'convoy_driving'; end if;
  if p_route is not null then
    select * into r from world_routes where code = p_route;
    if not found then raise exception 'route_not_found'; end if;
    select level into v_level from profiles where id = v_uid;
    if r.status = 'PLANNED' then raise exception 'route_unavailable'; end if;
    if _effective_status(r.status, r.unlock_level, v_level) <> 'OPEN' then raise exception 'route_locked'; end if;
  end if;
  if c.route_code is distinct from p_route then
    update convoy_members set ready = false where convoy_id = c.id;               -- the ready check is about THIS route
    update convoys set route_code = p_route where id = c.id returning * into c;
    perform _convoy_sys(c.id, case when p_route is null then 'The route was cleared.' else 'Route set: ' || r.name || '.' end);
  end if;
  return _convoy_json(v_uid, c);
end $$;

create function set_convoy_tags(p_tags text[] default null, p_language text default null) returns jsonb
language plpgsql security definer set search_path = public as $$
declare v_uid uuid := auth.uid(); c convoys;
begin
  if v_uid is null then raise exception 'not_authenticated'; end if;
  perform _rate_limit('convoy_write', 60, 60);
  c := _my_led_convoy();
  begin
    update convoys set tags = coalesce((select array_agg(distinct t order by t) from unnest(p_tags) t), tags), language = coalesce(p_language, language)
     where id = c.id returning * into c;
  exception when check_violation then raise exception 'invalid_tags';
  end;
  return _convoy_json(v_uid, c);
end $$;

create function start_convoy_drive() returns jsonb
language plpgsql security definer set search_path = public as $$
declare v_uid uuid := auth.uid(); c convoys; r world_routes; m record;
begin
  if v_uid is null then raise exception 'not_authenticated'; end if;
  perform _rate_limit('convoy_write', 60, 60);
  c := _my_led_convoy();
  if c.phase <> 'lobby' then raise exception 'convoy_driving'; end if;
  if c.route_code is null then raise exception 'route_required'; end if;
  select * into r from world_routes where code = c.route_code;
  if r.status <> 'OPEN' then raise exception 'route_unavailable'; end if;
  if exists (select 1 from convoy_members where convoy_id = c.id and player_id <> v_uid and not ready) then raise exception 'members_not_ready'; end if;
  -- everyone must actually be online: a member whose heartbeat has stopped could not join the live session, so the start would only strand them
  if exists (select 1 from convoy_members cm where cm.convoy_id = c.id and not exists (
               select 1 from game_sessions gs where gs.player_id = cm.player_id and gs.ended_at is null and gs.last_seen_at > now() - make_interval(secs => cfg('presence_ttl_seconds')))) then
    raise exception 'members_offline';
  end if;
  for m in select cm.player_id, p.level from convoy_members cm join profiles p on p.id = cm.player_id where cm.convoy_id = c.id loop
    if m.level < r.unlock_level then raise exception 'member_level_too_low'; end if;
  end loop;
  update convoys set phase = 'driving', started_at = now() where id = c.id returning * into c;
  perform _convoy_sys(c.id, 'The convoy is on the road: ' || r.name || '.');
  for m in select player_id from convoy_members where convoy_id = c.id and player_id <> v_uid loop
    perform _notify(m.player_id, 'convoy_event', 'Convoy on the road', c.name || ' has started driving: ' || r.name || '.', jsonb_build_object('convoy_id', c.id));
  end loop;
  perform _track(v_uid, 'convoy_started', jsonb_build_object('convoy_id', c.id, 'players', (select count(*) from convoy_members where convoy_id = c.id)));
  return _convoy_json(v_uid, c);
end $$;

-- Back to the lobby (also how a failed live-session start is rolled back): the code is cleared and everyone must ready up again.
create function end_convoy_drive() returns jsonb
language plpgsql security definer set search_path = public as $$
declare v_uid uuid := auth.uid(); c convoys;
begin
  if v_uid is null then raise exception 'not_authenticated'; end if;
  perform _rate_limit('convoy_write', 60, 60);
  c := _my_led_convoy();
  if c.phase = 'driving' then
    update convoy_members set ready = false where convoy_id = c.id;
    update convoys set phase = 'lobby', started_at = null, session_code = null where id = c.id returning * into c;
    perform _convoy_sys(c.id, 'The convoy is back in the lobby.');
  end if;
  return _convoy_json(v_uid, c);
end $$;

-- ---------------------------------------------------------------- member actions
create function set_my_ready(p_ready boolean) returns jsonb
language plpgsql security definer set search_path = public as $$
declare v_uid uuid := auth.uid(); c convoys;
begin
  if v_uid is null then raise exception 'not_authenticated'; end if;
  perform _rate_limit('convoy_ready', 30, 60);
  select cv.* into c from convoy_members m join convoys cv on cv.id = m.convoy_id where m.player_id = v_uid and cv.disbanded_at is null for update of cv;
  if not found then raise exception 'not_in_convoy'; end if;
  if c.phase <> 'lobby' then raise exception 'convoy_driving'; end if;
  if p_ready and c.route_code is null then raise exception 'route_required'; end if;
  update convoy_members set ready = coalesce(p_ready, false) where convoy_id = c.id and player_id = v_uid;
  return _convoy_json(v_uid, c);
end $$;

create function post_convoy_message(p_body text) returns jsonb
language plpgsql security definer set search_path = public as $$
declare v_uid uuid := auth.uid(); v_convoy uuid; v_body text; v_id bigint;
begin
  if v_uid is null then raise exception 'not_authenticated'; end if;
  perform _rate_limit('convoy_chat', 20, 30);
  select m.convoy_id into v_convoy from convoy_members m join convoys c on c.id = m.convoy_id where m.player_id = v_uid and c.disbanded_at is null;
  if v_convoy is null then raise exception 'not_in_convoy'; end if;
  v_body := trim(regexp_replace(regexp_replace(coalesce(p_body, ''), '[\x01-\x1f\x7f]+', ' ', 'g'), '[<>]', '', 'g'));
  if char_length(v_body) < 1 or char_length(v_body) > 200 then raise exception 'invalid_message'; end if;
  insert into convoy_messages(convoy_id, sender_id, body) values (v_convoy, v_uid, v_body) returning id into v_id;
  delete from convoy_messages where convoy_id = v_convoy and id <= v_id - 200;       -- keep the last 200 lines per convoy
  return jsonb_build_object('id', v_id);
end $$;

create function list_convoy_messages(p_after bigint default 0, p_limit integer default 50) returns jsonb
language plpgsql security definer set search_path = public as $$
declare v_uid uuid := auth.uid(); v_convoy uuid;
begin
  if v_uid is null then raise exception 'not_authenticated'; end if;
  perform _rate_limit('convoy_read', 240, 60);
  select m.convoy_id into v_convoy from convoy_members m join convoys c on c.id = m.convoy_id where m.player_id = v_uid and c.disbanded_at is null;
  if v_convoy is null then raise exception 'not_in_convoy'; end if;
  return jsonb_build_object('items', coalesce((
      select jsonb_agg(jsonb_build_object('id', x.id, 'kind', x.kind, 'body', case when x.moderation = 'ok' then x.body else null end, 'moderation', x.moderation, 'created_at', x.created_at,
               'sender', case when x.sender_id is null then null else jsonb_build_object('player_id', x.sender_id, 'display_name', p.display_name, 'level', p.level, 'avatar_id', p.avatar_id) end) order by x.id)
        from (select * from (
                select * from convoy_messages where convoy_id = v_convoy and id > greatest(coalesce(p_after, 0), 0)
                   and not exists (select 1 from player_blocks b where b.blocker_id = v_uid and b.blocked_id = convoy_messages.sender_id)
                 order by case when coalesce(p_after, 0) <= 0 then -id else id end        -- first load: the NEWEST lines; polling: the next lines in order
                 limit least(greatest(coalesce(p_limit, 50), 1), 100)) y order by id) x left join profiles p on p.id = x.sender_id), '[]'::jsonb),
    'server_time', now());
end $$;

-- ---------------------------------------------------------------- finding convoys and players
drop function list_open_convoys(integer, integer);
create function list_open_convoys(p_limit integer default 20, p_offset integer default 0, p_search text default null, p_tag text default null)
returns jsonb
language plpgsql security definer set search_path = public as $$
declare v_uid uuid := auth.uid(); v_q text; v_total integer; v_items jsonb; v_off integer; v_lim integer; v_level integer;
begin
  if v_uid is null then raise exception 'not_authenticated'; end if;
  perform _rate_limit('convoy_read', 120, 60);
  select level into v_level from profiles where id = v_uid;
  if p_tag is not null and p_tag not in ('jobs','freight','chill_drive','realistic_driving','training','help_support','friendly','long_haul','events') then raise exception 'invalid_tag'; end if;
  v_q := nullif(trim(coalesce(p_search, '')), '');
  if v_q is not null then
    if char_length(v_q) > 40 then raise exception 'invalid_search'; end if;
    v_q := '%' || replace(replace(replace(lower(v_q), '\', '\\'), '%', '\%'), '_', '\_') || '%';
  end if;
  v_off := greatest(coalesce(p_offset, 0), 0); v_lim := least(greatest(coalesce(p_limit, 20), 1), 50);
  with f as (
    select c.id, row_number() over (order by (c.phase = 'lobby') desc, c.created_at desc, c.id) as seq
      from convoys c join profiles l on l.id = c.leader_id left join world_routes r on r.code = c.route_code
     where c.disbanded_at is null and not c.is_private
       and (p_tag is null or p_tag = any (c.tags))
       and (v_q is null or lower(c.name) like v_q or lower(l.display_name) like v_q or lower(coalesce(r.name, '')) like v_q))
  select (select count(*)::int from f),
         coalesce((select jsonb_agg(jsonb_build_object('id', c.id, 'name', c.name, 'leader', l.display_name, 'capacity', c.capacity,
                     'member_count', (select count(*) from convoy_members where convoy_id = c.id), 'phase', c.phase,
                     'session_active', c.session_code is not null, 'created_at', c.created_at, 'route', _route_json(c.route_code),
                     'tags', to_jsonb(c.tags), 'language', c.language, 'driving_mode', c.driving_mode, 'min_level', c.min_level, 'has_password', c.password_hash is not null,
                     'joinable', (select count(*) from convoy_members where convoy_id = c.id) < c.capacity and v_level >= c.min_level) order by f.seq)
                     from f join convoys c on c.id = f.id join profiles l on l.id = c.leader_id where f.seq > v_off and f.seq <= v_off + v_lim), '[]'::jsonb)
    into v_total, v_items;
  return jsonb_build_object('total', v_total, 'items', v_items, 'server_time', now());
end $$;

create function list_active_players(p_limit integer default 30, p_offset integer default 0, p_search text default null) returns jsonb
language plpgsql security definer set search_path = public as $$
declare v_uid uuid := auth.uid(); v_q text; v_total integer; v_items jsonb; v_off integer; v_lim integer; v_ttl integer := cfg('presence_ttl_seconds')::int;
begin
  if v_uid is null then raise exception 'not_authenticated'; end if;
  perform _rate_limit('players_read', 60, 60);
  v_q := nullif(trim(coalesce(p_search, '')), '');
  if v_q is not null then
    if char_length(v_q) > 40 then raise exception 'invalid_search'; end if;
    v_q := '%' || replace(replace(replace(lower(v_q), '\', '\\'), '%', '\%'), '_', '\_') || '%';
  end if;
  v_off := greatest(coalesce(p_offset, 0), 0); v_lim := least(greatest(coalesce(p_limit, 30), 1), 50);
  with f as (
    select p.id, row_number() over (order by p.level desc, p.display_name, p.id) as seq
      from profiles p
     where p.id <> v_uid
       and not exists (select 1 from player_blocks b where b.blocker_id = v_uid and b.blocked_id = p.id)
       and exists (select 1 from game_sessions s where s.player_id = p.id and s.ended_at is null and s.last_seen_at > now() - make_interval(secs => v_ttl))
       and (v_q is null or lower(p.display_name) like v_q or lower(coalesce(p.username, '')) like v_q))
  select (select count(*)::int from f),
         coalesce((select jsonb_agg(jsonb_build_object('player_id', p.id, 'display_name', p.display_name, 'username', p.username, 'avatar_id', p.avatar_id,
                     'country_code', p.country_code, 'level', p.level, 'friend_status', _friend_status(v_uid, p.id), 'vehicle', _member_vehicle(p.id),
                     'activity', case when _presence_visibility(v_uid, p.id) = 'full' then _presence_of(p.id)->>'activity' else null end,
                     'in_convoy', exists (select 1 from convoy_members m join convoys c on c.id = m.convoy_id where m.player_id = p.id and c.disbanded_at is null)) order by f.seq)
                     from f join profiles p on p.id = f.id where f.seq > v_off and f.seq <= v_off + v_lim), '[]'::jsonb)
    into v_total, v_items;
  return jsonb_build_object('total', v_total, 'items', v_items, 'server_time', now());
end $$;

create function get_server_status() returns jsonb
language plpgsql security definer set search_path = public as $$
begin
  if auth.uid() is null then raise exception 'not_authenticated'; end if;
  perform _rate_limit('players_read', 60, 60);
  return jsonb_build_object('players_online', get_online_count(),
    'active_convoys', (select count(*) from convoys where disbanded_at is null),
    'convoys_driving', (select count(*) from convoys where disbanded_at is null and phase = 'driving'),
    'server_time', now());
end $$;

-- ---------------------------------------------------------------- settings, passwords, disbanding, member invites
-- A second membership guard (the original one keeps enforcing capacity and invite-only): required level and password.
create function _convoy_member_guard2() returns trigger
language plpgsql security definer set search_path = public as $$
declare c convoys; v_level integer;
begin
  select * into c from convoys where id = new.convoy_id;
  if new.player_id = c.leader_id then return new; end if;
  select level into v_level from profiles where id = new.player_id;
  if coalesce(v_level, 1) < c.min_level then raise exception 'level_too_low'; end if;
  if c.password_hash is not null
     and not exists (select 1 from convoy_invites i where i.convoy_id = c.id and i.invitee_id = new.player_id and i.status = 'accepted')
     and current_setting('aro.convoy_pw_ok', true) is distinct from c.id::text then
    raise exception 'password_required';
  end if;
  return new;
end $$;
create trigger convoy_members_guard2 before insert on convoy_members for each row execute function _convoy_member_guard2();

create function _convoy_pw_hash(p_salt text, p_password text) returns text
language sql immutable set search_path = public as $$ select encode(sha256(convert_to(p_salt || ':' || p_password, 'UTF8')), 'hex') $$;

create function _check_route_for_leader(p_route text, p_leader uuid) returns void
language plpgsql security definer set search_path = public as $$
declare r world_routes; v_level integer;
begin
  select * into r from world_routes where code = p_route;
  if not found then raise exception 'route_not_found'; end if;
  select level into v_level from profiles where id = p_leader;
  if r.status = 'PLANNED' then raise exception 'route_unavailable'; end if;
  if _effective_status(r.status, r.unlock_level, v_level) <> 'OPEN' then raise exception 'route_locked'; end if;
end $$;

-- One atomic step: the lobby never exists, even for a moment, without its level limit or password.
create function create_convoy_lobby(p_name text, p_route text default null, p_private boolean default false, p_capacity integer default 8,
                                    p_driving_mode text default 'relaxed', p_min_rank integer default 1, p_language text default 'en',
                                    p_password text default null, p_tags text[] default '{}') returns uuid
language plpgsql security definer set search_path = public as $$
declare v_uid uuid := auth.uid(); v_id uuid; v_level integer; v_salt text; v_tags text[];
begin
  if v_uid is null then raise exception 'not_authenticated'; end if;
  perform _rate_limit('convoy_write', 60, 60);
  if p_name is null or char_length(trim(p_name)) not between 3 and 32 then raise exception 'invalid_name'; end if;
  if coalesce(p_capacity, 8) < 2 or coalesce(p_capacity, 8) > cfg('convoy_max_capacity') then raise exception 'invalid_capacity'; end if;
  if coalesce(p_driving_mode, 'relaxed') not in ('relaxed','realistic') then raise exception 'invalid_driving_mode'; end if;
  select level into v_level from profiles where id = v_uid;
  if coalesce(p_min_rank, 1) < 1 or coalesce(p_min_rank, 1) > least(50, v_level) then raise exception 'invalid_min_level'; end if;
  if p_password is not null and char_length(p_password) not between 4 and 32 then raise exception 'invalid_password'; end if;
  v_tags := coalesce((select array_agg(distinct t order by t) from unnest(p_tags) t), '{}');
  if cardinality(v_tags) > 3 or not (v_tags <@ array['jobs','freight','chill_drive','realistic_driving','training','help_support','friendly','long_haul','events']) then raise exception 'invalid_tags'; end if;
  if p_language is null or p_language !~ '^[a-z]{2}$' then raise exception 'invalid_tags'; end if;
  if p_route is not null then perform _check_route_for_leader(p_route, v_uid); end if;
  if p_password is not null then v_salt := md5(random()::text || clock_timestamp()::text || v_uid::text); end if;
  insert into convoys(leader_id, name, route_code, is_private, capacity, driving_mode, min_level, language, tags, password_salt, password_hash)
  values (v_uid, trim(p_name), p_route, coalesce(p_private, false), coalesce(p_capacity, 8), coalesce(p_driving_mode, 'relaxed'), coalesce(p_min_rank, 1), p_language, v_tags,
          v_salt, case when v_salt is null then null else _convoy_pw_hash(v_salt, p_password) end) returning id into v_id;
  insert into convoy_members(convoy_id, player_id) values (v_id, v_uid);
  return v_id;
exception when unique_violation then raise exception 'already_in_convoy_or_code_taken';
end $$;

-- A wrong password is an ANSWER, not an exception: an exception would roll back the attempt counter and make guessing free.
create function join_convoy_with_password(p_convoy uuid, p_password text) returns jsonb
language plpgsql security definer set search_path = public as $$
declare v_uid uuid := auth.uid(); c convoys;
begin
  if v_uid is null then raise exception 'not_authenticated'; end if;
  perform _rate_limit('convoy_pw', 8, 600);                    -- guesses are limited per player
  select * into c from convoys where id = p_convoy and disbanded_at is null;
  if not found then raise exception 'convoy_not_found'; end if;
  if c.password_hash is null or p_password is null or _convoy_pw_hash(c.password_salt, p_password) <> c.password_hash then
    return jsonb_build_object('joined', false, 'error', 'wrong_password');
  end if;
  perform set_config('aro.convoy_pw_ok', c.id::text, true);
  perform join_convoy(p_convoy);
  return jsonb_build_object('joined', true);
end $$;

create function set_convoy_settings(p_driving_mode text default null, p_min_rank integer default null, p_language text default null,
                                    p_password text default null, p_clear_password boolean default false) returns jsonb
language plpgsql security definer set search_path = public as $$
declare v_uid uuid := auth.uid(); c convoys; v_salt text;
begin
  if v_uid is null then raise exception 'not_authenticated'; end if;
  perform _rate_limit('convoy_write', 60, 60);
  c := _my_led_convoy();
  if (p_driving_mode is not null or p_min_rank is not null or p_password is not null or p_clear_password) and c.phase <> 'lobby' then raise exception 'convoy_driving'; end if;
  if p_driving_mode is not null and p_driving_mode not in ('relaxed','realistic') then raise exception 'invalid_driving_mode'; end if;
  if p_min_rank is not null then
    if p_min_rank < 1 or p_min_rank > 50 or p_min_rank > (select level from profiles where id = v_uid) then raise exception 'invalid_min_level'; end if;
    if exists (select 1 from convoy_members m join profiles p on p.id = m.player_id where m.convoy_id = c.id and p.level < p_min_rank) then raise exception 'member_below_level'; end if;
  end if;
  if p_language is not null and p_language !~ '^[a-z]{2}$' then raise exception 'invalid_language'; end if;
  if p_password is not null and char_length(p_password) not between 4 and 32 then raise exception 'invalid_password'; end if;
  if p_password is not null then v_salt := md5(random()::text || clock_timestamp()::text || v_uid::text); end if;
  update convoys set driving_mode = coalesce(p_driving_mode, driving_mode), min_level = coalesce(p_min_rank, min_level), language = coalesce(p_language, language),
         password_salt = case when p_clear_password then null when p_password is not null then v_salt else password_salt end,
         password_hash = case when p_clear_password then null when p_password is not null then _convoy_pw_hash(v_salt, p_password) else password_hash end
   where id = c.id returning * into c;
  return _convoy_json(v_uid, c);
end $$;

create function disband_convoy() returns void
language plpgsql security definer set search_path = public as $$
declare v_uid uuid := auth.uid(); c convoys; m record;
begin
  if v_uid is null then raise exception 'not_authenticated'; end if;
  perform _rate_limit('convoy_write', 60, 60);
  c := _my_led_convoy();
  for m in select player_id from convoy_members where convoy_id = c.id and player_id <> v_uid loop
    perform _notify(m.player_id, 'convoy_event', 'Convoy disbanded', c.name || ' was disbanded by its leader.', jsonb_build_object('convoy_id', c.id));
  end loop;
  update convoy_invites set status = 'revoked' where convoy_id = c.id and status = 'pending';
  update convoys set disbanded_at = now(), session_code = null, phase = 'lobby', started_at = null where id = c.id;
  delete from convoy_members where convoy_id = c.id;
  perform _track(v_uid, 'convoy_disbanded', jsonb_build_object('convoy_id', c.id));
end $$;

-- A member may invite a FRIEND into a public convoy (private convoys stay leader-only: invite_to_convoy)
create function invite_friend_to_convoy(p_player uuid) returns uuid
language plpgsql security definer set search_path = public as $$
declare v_uid uuid := auth.uid(); c convoys; v_id uuid; v_name text;
begin
  if v_uid is null then raise exception 'not_authenticated'; end if;
  perform _rate_limit('convoy_invite', 30, 3600);
  select cv.* into c from convoy_members m join convoys cv on cv.id = m.convoy_id where m.player_id = v_uid and cv.disbanded_at is null for update of cv;
  if not found then raise exception 'not_in_convoy'; end if;
  if c.is_private and c.leader_id <> v_uid then raise exception 'invite_leader_only'; end if;
  if p_player = v_uid then raise exception 'cannot_invite_self'; end if;
  if not exists (select 1 from profiles where id = p_player) then raise exception 'player_not_found'; end if;
  if _friend_status(v_uid, p_player) <> 'friends' then raise exception 'not_friends'; end if;
  if exists (select 1 from player_blocks where (blocker_id = v_uid and blocked_id = p_player) or (blocker_id = p_player and blocked_id = v_uid)) then raise exception 'player_blocked'; end if;
  if exists (select 1 from convoy_members where player_id = p_player) then raise exception 'player_in_convoy'; end if;
  if (select count(*) from convoy_members where convoy_id = c.id) >= c.capacity then raise exception 'convoy_full'; end if;
  if (select level from profiles where id = p_player) < c.min_level then raise exception 'level_too_low'; end if;
  update convoy_invites set status = 'revoked' where convoy_id = c.id and invitee_id = p_player and status = 'pending' and expires_at <= now();
  begin
    insert into convoy_invites(convoy_id, inviter_id, invitee_id) values (c.id, v_uid, p_player) returning id into v_id;
  exception when unique_violation then raise exception 'already_invited';
  end;
  select display_name into v_name from profiles where id = v_uid;
  perform _notify(p_player, 'convoy_event', 'Convoy invite', v_name || ' invited you to ' || c.name || '.',
                  jsonb_build_object('invite_id', v_id, 'convoy_id', c.id, 'from_player_id', v_uid), 'convinv:' || v_id);
  return v_id;
end $$;

-- ---------------------------------------------------------------- reporting and blocking
create function report_chat_message(p_message bigint, p_reason text default 'other') returns jsonb
language plpgsql security definer set search_path = public as $$
declare v_uid uuid := auth.uid(); v_convoy uuid; msg convoy_messages;
begin
  if v_uid is null then raise exception 'not_authenticated'; end if;
  perform _rate_limit('chat_report', 10, 3600);
  if p_reason is null or p_reason not in ('spam','abuse','harassment','other') then raise exception 'invalid_reason'; end if;
  select m.convoy_id into v_convoy from convoy_members m join convoys c on c.id = m.convoy_id where m.player_id = v_uid and c.disbanded_at is null;
  if v_convoy is null then raise exception 'not_in_convoy'; end if;
  select * into msg from convoy_messages where id = p_message and convoy_id = v_convoy and kind = 'chat';
  if not found then raise exception 'message_not_found'; end if;
  if msg.sender_id = v_uid then raise exception 'cannot_report_self'; end if;
  begin insert into chat_reports(message_id, reporter_id, reason) values (p_message, v_uid, p_reason);
  exception when unique_violation then raise exception 'already_reported'; end;
  return jsonb_build_object('status', 'received', 'moderation', (select moderation from convoy_messages where id = p_message));
end $$;

create function block_player(p_player uuid) returns void
language plpgsql security definer set search_path = public as $$
declare v_uid uuid := auth.uid();
begin
  if v_uid is null then raise exception 'not_authenticated'; end if;
  perform _rate_limit('block_write', 30, 3600);
  if p_player = v_uid then raise exception 'cannot_block_self'; end if;
  if not exists (select 1 from profiles where id = p_player) then raise exception 'player_not_found'; end if;
  if (select count(*) from player_blocks where blocker_id = v_uid) >= 200 then raise exception 'block_limit'; end if;
  insert into player_blocks(blocker_id, blocked_id) values (v_uid, p_player) on conflict do nothing;
end $$;

create function unblock_player(p_player uuid) returns void
language plpgsql security definer set search_path = public as $$
begin
  if auth.uid() is null then raise exception 'not_authenticated'; end if;
  perform _rate_limit('block_write', 30, 3600);
  delete from player_blocks where blocker_id = auth.uid() and blocked_id = p_player;
end $$;

create function list_blocked_players() returns jsonb
language plpgsql security definer set search_path = public as $$
begin
  if auth.uid() is null then raise exception 'not_authenticated'; end if;
  perform _rate_limit('block_read', 60, 60);
  return jsonb_build_object('items', coalesce((select jsonb_agg(jsonb_build_object('player_id', b.blocked_id, 'display_name', p.display_name, 'level', p.level, 'since', b.created_at) order by b.created_at desc)
                                                from player_blocks b join profiles p on p.id = b.blocked_id where b.blocker_id = auth.uid()), '[]'::jsonb), 'server_time', now());
end $$;

-- ---------------------------------------------------------------- lock-down (same convention as every migration)
do $$ declare r record;
begin
  for r in select p.oid::regprocedure as sig, p.proname from pg_proc p
            where p.pronamespace = 'public'::regnamespace and p.oid not in (select oid from _fn_before) loop
    execute format('revoke execute on function %s from public, anon, authenticated', r.sig);
    if r.proname in ('set_convoy_route','set_convoy_tags','start_convoy_drive','end_convoy_drive','set_my_ready','post_convoy_message','list_convoy_messages',
                     'list_open_convoys','list_active_players','get_server_status','create_convoy_lobby','join_convoy_with_password','set_convoy_settings','disband_convoy',
                     'invite_friend_to_convoy','report_chat_message','block_player','unblock_player','list_blocked_players') then
      execute format('grant execute on function %s to authenticated', r.sig);
    end if;
  end loop;
end $$;
