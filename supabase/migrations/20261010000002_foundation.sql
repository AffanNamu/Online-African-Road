-- Backend foundation, step 2: shared infrastructure used by every later domain.
--   * game_config       tunables read by server functions (no magic numbers in client code)
--   * api_rate_limits   per-player fixed-window counters (_rate_limit)
--   * game_sessions     one row per app session: authenticated session id + presence source (RPCs in step 3)
--   * analytics_events  allow-listed, PII-free event stream (_track)
--   * notifications     persistent per-player inbox (_notify + RPCs)
-- Same authority model as before: the player is ALWAYS auth.uid(); clients cannot write any table directly.

-- Remember which functions exist before this migration so the lock-down at the end can strip the default
-- Supabase EXECUTE grant from every function this migration adds (and re-grant only the intended client RPCs).
create temp table _fn_before as select oid from pg_proc where pronamespace = 'public'::regnamespace;

-- ---------------------------------------------------------------- config
create table game_config (
  key   text primary key,
  value numeric not null,
  note  text
);
insert into game_config(key, value, note) values
  ('presence_ttl_seconds',    90,  'A session with no heartbeat for this long is offline'),
  ('session_live_cap',        5,   'Max simultaneously live sessions per player (oldest are replaced)'),
  ('convoy_max_capacity',     8,   'Hard cap on convoy size (matches join_convoy)'),
  ('friend_limit',            100, 'Max accepted friends per player'),
  ('friend_pending_limit',    50,  'Max outgoing pending friend requests per player'),
  ('friend_resend_hours',     24,  'A rejected request can be sent again after this many hours'),
  ('notifications_keep',      200, 'Notifications kept per player; older ones are pruned'),
  ('avg_speed_kmh',           50,  'Average speed used to estimate job / route durations'),
  ('daily_challenge_count',   3,   'Daily challenges assigned per player per UTC day'),
  ('recent_players_keep',     50,  'Recent players remembered per player')
on conflict (key) do nothing;
alter table game_config enable row level security;
revoke all on game_config from anon, authenticated;

create function cfg(p_key text) returns numeric
language sql stable security definer set search_path = public as $$
  select value from game_config where key = p_key $$;

-- ---------------------------------------------------------------- rate limiting
create table api_rate_limits (
  player_id    uuid not null references profiles(id) on delete cascade,
  bucket       text not null,
  window_start timestamptz not null,
  hits         integer not null default 0,
  primary key (player_id, bucket, window_start)
);
create index api_rate_limits_window_idx on api_rate_limits(window_start);
alter table api_rate_limits enable row level security;
revoke all on api_rate_limits from anon, authenticated;

-- Fixed-window limiter. A rejected call raises, which rolls back its own increment, so the counter stays at the
-- limit and every further call in the window is rejected too. Callers pass a bucket per endpoint family.
create function _rate_limit(p_bucket text, p_max integer, p_window_seconds integer) returns void
language plpgsql security definer set search_path = public as $$
declare v_uid uuid := auth.uid(); v_win timestamptz; v_hits integer;
begin
  if v_uid is null then raise exception 'not_authenticated'; end if;
  v_win := to_timestamp(floor(extract(epoch from now()) / p_window_seconds) * p_window_seconds);
  insert into api_rate_limits(player_id, bucket, window_start, hits) values (v_uid, p_bucket, v_win, 1)
  on conflict (player_id, bucket, window_start) do update set hits = api_rate_limits.hits + 1
  returning hits into v_hits;
  if v_hits > p_max then raise exception 'rate_limited'; end if;
end $$;

-- ---------------------------------------------------------------- sessions (presence source)
-- A session is created by start_session() (server-generated id), kept alive by heartbeat() and closed by end_session()
-- or by timeout. Presence is DERIVED: a player is online iff one of their sessions has a recent server-side heartbeat.
create table game_sessions (
  id           uuid primary key default gen_random_uuid(),
  player_id    uuid not null references profiles(id) on delete cascade,
  client       text not null default 'unknown' check (char_length(client) between 1 and 24),
  started_at   timestamptz not null default now(),
  last_seen_at timestamptz not null default now(),
  ended_at     timestamptz,
  end_reason   text check (end_reason in ('logout','timeout','replaced')),
  activity     text not null default 'menu' check (activity in ('menu','driving','bus','convoy','idle')),
  route_code   text,
  city_slug    text
);
create index game_sessions_live_idx   on game_sessions(last_seen_at desc) where ended_at is null;
create index game_sessions_player_idx on game_sessions(player_id, started_at desc);
alter table game_sessions enable row level security;
revoke all on game_sessions from anon, authenticated;

-- ---------------------------------------------------------------- analytics
create table analytics_events (
  id         bigint generated always as identity primary key,
  player_id  uuid references profiles(id) on delete set null,   -- events outlive accounts, de-linked
  session_id uuid,
  name       text not null check (name in (
    'login','logout','job_accepted','job_completed','job_abandoned','route_unlocked','vehicle_purchased',
    'convoy_created','convoy_joined','convoy_left','multiplayer_session_started','multiplayer_session_ended',
    'level_up','achievement_unlocked','challenge_completed','friend_added')),
  props      jsonb not null default '{}'::jsonb check (jsonb_typeof(props) = 'object' and pg_column_size(props) <= 2048),
  created_at timestamptz not null default now()
);
create index analytics_events_name_idx   on analytics_events(name, created_at desc);
create index analytics_events_player_idx on analytics_events(player_id, created_at desc);
alter table analytics_events enable row level security;
revoke all on analytics_events from anon, authenticated;

-- Server-side only. props must never carry e-mail, IP or free text; callers pass ids, counts and codes.
create function _track(p_player uuid, p_name text, p_props jsonb default '{}'::jsonb, p_session uuid default null) returns void
language plpgsql security definer set search_path = public as $$
begin
  insert into analytics_events(player_id, session_id, name, props)
  values (p_player,
          coalesce(p_session, (select s.id from game_sessions s where s.player_id = p_player and s.ended_at is null
                                order by s.last_seen_at desc limit 1)),
          p_name, coalesce(p_props, '{}'::jsonb));
end $$;

-- The only client-reportable events: facts only the client knows. Keys are allow-listed, everything else is dropped.
create function track_event(p_name text, p_props jsonb default '{}'::jsonb) returns void
language plpgsql security definer set search_path = public as $$
declare v_uid uuid := auth.uid(); v_clean jsonb;
begin
  if v_uid is null then raise exception 'not_authenticated'; end if;
  perform _rate_limit('track_event', 30, 60);
  if p_name not in ('multiplayer_session_started','multiplayer_session_ended') then raise exception 'event_not_allowed'; end if;
  select coalesce(jsonb_object_agg(k, v), '{}'::jsonb) into v_clean
    from jsonb_each(case when jsonb_typeof(p_props) = 'object' then p_props else '{}'::jsonb end) as e(k, v)
   where k in ('players','duration_s','region','reason') and jsonb_typeof(v) in ('number','string','boolean');
  perform _track(v_uid, p_name, v_clean);
end $$;

-- ---------------------------------------------------------------- notifications
create table notifications (
  id         uuid primary key default gen_random_uuid(),
  player_id  uuid not null references profiles(id) on delete cascade,
  type       text not null check (type in ('payout','job_event','convoy_event','friend_event','system','server','error','achievement','level_up')),
  title      text not null check (char_length(title) between 1 and 120),
  message    text not null check (char_length(message) between 1 and 500),
  metadata   jsonb not null default '{}'::jsonb check (jsonb_typeof(metadata) = 'object' and pg_column_size(metadata) <= 2048),
  dedupe_key text,
  read_at    timestamptz,
  created_at timestamptz not null default clock_timestamp()   -- not now(): events in one transaction must still sort in the order they happened
);
create index notifications_player_idx on notifications(player_id, created_at desc, id desc);
create index notifications_unread_idx on notifications(player_id) where read_at is null;
create unique index notifications_dedupe_idx on notifications(player_id, dedupe_key) where dedupe_key is not null;
alter table notifications enable row level security;
revoke all on notifications from anon, authenticated;

-- Server-side only. A dedupe key makes the notification idempotent (the same event never notifies twice).
create function _notify(p_player uuid, p_type text, p_title text, p_message text,
                        p_meta jsonb default '{}'::jsonb, p_dedupe text default null) returns uuid
language plpgsql security definer set search_path = public as $$
declare v_id uuid;
begin
  insert into notifications(player_id, type, title, message, metadata, dedupe_key)
  values (p_player, p_type, left(p_title, 120), left(p_message, 500), coalesce(p_meta, '{}'::jsonb), p_dedupe)
  on conflict (player_id, dedupe_key) where dedupe_key is not null do nothing
  returning id into v_id;
  if v_id is not null then
    -- keep the inbox bounded
    delete from notifications where id in (
      select id from notifications where player_id = p_player
       order by created_at desc, id desc offset greatest(cfg('notifications_keep')::int, 20));
  end if;
  return v_id;
end $$;

create function _notification_json(n notifications) returns jsonb
language sql immutable set search_path = public as $$
  select jsonb_build_object('id', n.id, 'type', n.type, 'title', n.title, 'message', n.message,
                            'metadata', n.metadata, 'read', n.read_at is not null, 'created_at', n.created_at) $$;

create function list_notifications(p_limit integer default 30, p_before timestamptz default null,
                                   p_unread_only boolean default false) returns jsonb
language plpgsql security definer set search_path = public as $$
declare v_uid uuid := auth.uid(); v_items jsonb; v_lim integer := least(greatest(coalesce(p_limit, 30), 1), 100);
begin
  if v_uid is null then raise exception 'not_authenticated'; end if;
  perform _rate_limit('notifications_read', 120, 60);
  select coalesce(jsonb_agg(_notification_json(n) order by n.created_at desc, n.id desc), '[]'::jsonb) into v_items
    from (select * from notifications
           where player_id = v_uid and (p_before is null or created_at < p_before)
             and (not coalesce(p_unread_only, false) or read_at is null)
           order by created_at desc, id desc limit v_lim) n;
  return jsonb_build_object('items', v_items,
    'unread', (select count(*) from notifications where player_id = v_uid and read_at is null),
    'server_time', now());
end $$;

create function get_unread_notification_count() returns integer
language plpgsql security definer set search_path = public as $$
begin
  if auth.uid() is null then raise exception 'not_authenticated'; end if;
  perform _rate_limit('notifications_read', 120, 60);
  return (select count(*)::int from notifications where player_id = auth.uid() and read_at is null);
end $$;

-- Someone else's (or a nonexistent) id is reported as not found: no existence oracle.
create function mark_notification_read(p_id uuid) returns void
language plpgsql security definer set search_path = public as $$
begin
  if auth.uid() is null then raise exception 'not_authenticated'; end if;
  perform _rate_limit('notifications_write', 120, 60);
  update notifications set read_at = coalesce(read_at, now()) where id = p_id and player_id = auth.uid();
  if not found then raise exception 'notification_not_found'; end if;
end $$;

create function mark_all_notifications_read() returns integer
language plpgsql security definer set search_path = public as $$
declare v_n integer;
begin
  if auth.uid() is null then raise exception 'not_authenticated'; end if;
  perform _rate_limit('notifications_write', 120, 60);
  update notifications set read_at = now() where player_id = auth.uid() and read_at is null;
  get diagnostics v_n = row_count;
  return v_n;
end $$;

-- ---------------------------------------------------------------- maintenance (service role / cron only)
create function purge_old_data() returns jsonb
language plpgsql security definer set search_path = public as $$
declare a integer; r integer; s integer;
begin
  delete from analytics_events where created_at < now() - interval '180 days';       get diagnostics a = row_count;
  delete from api_rate_limits where window_start < now() - interval '1 day';          get diagnostics r = row_count;
  delete from game_sessions where ended_at is not null and ended_at < now() - interval '30 days'; get diagnostics s = row_count;
  return jsonb_build_object('analytics_events', a, 'rate_limits', r, 'sessions', s);
end $$;

-- ---------------------------------------------------------------- lock-down
do $$ declare r record;
begin
  for r in select p.oid::regprocedure as sig, p.proname from pg_proc p
            where p.pronamespace = 'public'::regnamespace and p.oid not in (select oid from _fn_before) loop
    execute format('revoke execute on function %s from public, anon, authenticated', r.sig);
    if r.proname in ('track_event','list_notifications','get_unread_notification_count','mark_notification_read','mark_all_notifications_read') then
      execute format('grant execute on function %s to authenticated', r.sig);
    end if;
  end loop;
end $$;
