-- Admin site, part 2: content and messages (news and hero banners with scheduling, broadcasts, routes and servers) and moderation (the chat-report queue and player sanctions).
-- Additive. The only existing objects touched are admin_set_role (re-created to accept the new 'content' role) and two small BEFORE INSERT triggers that refuse a muted player's chat line
-- and a suspended or banned player's new game session. Nothing here reads a client-supplied identity, reward, price or amount.
--
--   * role 'content'     may edit news and hero banners and nothing else.
--   * content            owner and content: list, save (create or edit), publish or unpublish. Text is trimmed and stripped of control characters and < >. Hero banners need their
--                        headline, button and one of the known actions. A schedule is publish_at / expires_at, so an item can be prepared in advance and expire by itself.
--   * broadcast          owner and operations: one in-game notification to every player, at most 3 an hour, the recipient count is audited.
--   * routes, servers    owner and operations: open, plan or lock a route (a route with no playable game route cannot be opened), set a server online or in maintenance, its capacity and region.
--   * reports            owner and support: the queue of reported chat lines, with surrounding lines, then dismiss (line shown again) or remove (line stays hidden for everyone).
--   * sanctions          owner and support: mute (cannot chat), suspend (cannot start a session), both for 1 hour to 90 days; ban (owner only, no end date). Admins cannot be sanctioned.
--                        Enforcement is additive: a trigger refuses the chat line or the new session, open sessions of a suspended or banned player are ended, and the sign-in service is told
--                        through auth.users.banned_until so the account cannot sign in again. A token the player already holds keeps working for the other endpoints until it expires (at most an hour).
create temp table _fn_before as select oid from pg_proc where pronamespace = 'public'::regnamespace;

alter type admin_role add value if not exists 'content';

-- ---------------------------------------------------------------- tables
create table chat_report_reviews (
  message_id  bigint primary key references convoy_messages(id) on delete cascade,
  decision    text not null check (decision in ('dismiss', 'remove')),
  reviewer_id uuid not null,
  reason      text not null check (char_length(reason) between 3 and 300),
  reviewed_at timestamptz not null default now()
);
alter table chat_report_reviews enable row level security;
revoke all on chat_report_reviews from anon, authenticated;

create table player_sanctions (
  id          uuid primary key default gen_random_uuid(),
  player_id   uuid not null references profiles(id) on delete cascade,
  kind        text not null check (kind in ('mute', 'suspend', 'ban')),
  reason      text not null check (char_length(reason) between 3 and 300),
  message_id  bigint,                                         -- the reported line that led to it, when there was one (not a foreign key: chat lines are pruned)
  created_by  uuid not null,
  created_at  timestamptz not null default now(),
  expires_at  timestamptz,
  lifted_at   timestamptz,
  lifted_by   uuid,
  lift_reason text check (lift_reason is null or char_length(lift_reason) between 3 and 300),
  check ((kind = 'ban') = (expires_at is null)),
  check (expires_at is null or expires_at > created_at),
  check ((lifted_at is null) = (lifted_by is null))
);
create index player_sanctions_player_idx on player_sanctions(player_id, created_at desc);
create index player_sanctions_live_idx on player_sanctions(player_id) where lifted_at is null;
alter table player_sanctions enable row level security;
revoke all on player_sanctions from anon, authenticated;

-- ---------------------------------------------------------------- helpers (never granted to clients)
create function _admin_text(p_val text, p_min integer, p_max integer, p_err text) returns text
language plpgsql immutable as $$
declare t text := btrim(regexp_replace(regexp_replace(coalesce(p_val, ''), '[\x01-\x1f\x7f]+', ' ', 'g'), '[<>]', '', 'g'));
begin
  if char_length(t) < p_min or char_length(t) > p_max then raise exception '%', p_err; end if;
  return t;
end $$;

create function _sanction_live(p_player uuid, p_kinds text[]) returns boolean
language sql stable security definer set search_path = public as $$
  select exists (select 1 from player_sanctions s where s.player_id = p_player and s.kind = any (p_kinds) and s.lifted_at is null and (s.expires_at is null or s.expires_at > now())) $$;

-- Keeps the sign-in service in step: banned_until is the latest end of a live suspension, or far in the future for a ban, or empty when nothing is live.
-- Returns false (and never fails the moderation action) if the auth schema refuses the update; the audit log records which.
create function _sync_ban(p_player uuid) returns boolean
language plpgsql security definer set search_path = public as $$
declare v_until timestamptz;
begin
  select max(case when kind = 'ban' then now() + interval '100 years' else expires_at end) into v_until
    from player_sanctions where player_id = p_player and kind in ('suspend', 'ban') and lifted_at is null and (expires_at is null or expires_at > now());
  update auth.users set banned_until = v_until where id = p_player;
  return true;
exception when others then
  return false;
end $$;

create function _block_muted_chat() returns trigger
language plpgsql security definer set search_path = public as $$
begin
  if new.kind = 'chat' and new.sender_id is not null and _sanction_live(new.sender_id, array['mute', 'suspend', 'ban']) then raise exception 'muted'; end if;
  return new;
end $$;
create trigger convoy_messages_sanction before insert on convoy_messages for each row execute function _block_muted_chat();

create function _block_sanctioned_session() returns trigger
language plpgsql security definer set search_path = public as $$
begin
  if _sanction_live(new.player_id, array['suspend', 'ban']) then raise exception 'account_suspended'; end if;
  return new;
end $$;
create trigger game_sessions_sanction before insert on game_sessions for each row execute function _block_sanctioned_session();

create function _content_state(c content_items) returns text
language sql stable as $$
  select case when not c.published then 'draft' when c.publish_at > now() then 'scheduled' when c.expires_at is not null and c.expires_at <= now() then 'expired' else 'live' end $$;

create function _content_json(c content_items) returns jsonb
language sql stable set search_path = public as $$
  select jsonb_build_object('id', c.id, 'kind', c.kind, 'category', c.category, 'title', c.title, 'body', c.body, 'image_key', c.image_key, 'priority', c.priority,
    'published', c.published, 'publish_at', c.publish_at, 'expires_at', c.expires_at, 'eyebrow', c.eyebrow, 'headline', c.headline, 'headline_accent', c.headline_accent,
    'action', c.action, 'button', c.button, 'state', _content_state(c)) $$;

-- ---------------------------------------------------------------- roles: 'content' joins the list (otherwise identical to the foundation version)
create or replace function admin_set_role(p_target uuid, p_role text, p_reason text) returns jsonb
language plpgsql security definer set search_path = public as $$
declare v_reason text; v_old admin_users; v_new_role admin_role; v_active boolean; v_owners int;
begin
  if auth.uid() is null then raise exception 'not_authenticated'; end if;
  perform _admin_actor(array['owner']);
  v_reason := _admin_reason(p_reason);
  perform _rate_limit('admin_write', 30, 60);
  if p_role is null or p_role not in ('owner', 'operations', 'support', 'analyst', 'content', 'none') then raise exception 'invalid_role'; end if;
  if not exists (select 1 from auth.users where id = p_target) then raise exception 'user_not_found'; end if;
  select * into v_old from admin_users where user_id = p_target for update;
  if p_role = 'none' then
    if not found or not v_old.active then raise exception 'not_an_admin'; end if;
    v_new_role := v_old.role; v_active := false;
  else v_new_role := p_role::admin_role; v_active := true; end if;
  -- never leave the project without an active owner
  if found and v_old.active and v_old.role = 'owner' and not (v_active and v_new_role = 'owner') then
    select count(*) into v_owners from admin_users where active and role = 'owner' and user_id <> p_target;
    if v_owners = 0 then raise exception 'last_owner'; end if;
  end if;
  insert into admin_users(user_id, role, active, created_by) values (p_target, v_new_role, v_active, auth.uid())
  on conflict (user_id) do update set role = excluded.role, active = excluded.active, updated_at = now();
  perform _admin_log('set_role', 'admin', p_target::text, v_reason,
                     jsonb_build_object('from', case when v_old.user_id is null or not v_old.active then 'none' else v_old.role::text end, 'to', case when v_active then v_new_role::text else 'none' end));
  return jsonb_build_object('user_id', p_target, 'role', case when v_active then v_new_role::text else 'none' end);
end $$;

-- ---------------------------------------------------------------- content (news and hero banners)
create function admin_list_content(p_kind text default null) returns jsonb
language plpgsql security definer set search_path = public as $$
begin
  if auth.uid() is null then raise exception 'not_authenticated'; end if;
  perform _rate_limit('admin_read', 240, 60);
  perform _admin_actor(array['owner', 'content']);
  if p_kind is not null and p_kind not in ('news', 'hero') then raise exception 'invalid_kind'; end if;
  return jsonb_build_object('server_time', now(), 'items', coalesce((
    select jsonb_agg(_content_json(c) order by c.kind, c.priority desc, c.publish_at desc, c.id)
      from (select * from content_items where p_kind is null or kind = p_kind order by kind, priority desc, publish_at desc, id limit 200) c), '[]'::jsonb));
end $$;

create function admin_save_content(
  p_id text, p_kind text, p_category text, p_title text, p_body text, p_image_key text, p_priority integer,
  p_starts_at timestamptz, p_ends_at timestamptz, p_published boolean,
  p_eyebrow text, p_headline text, p_headline_accent text, p_action text, p_button text, p_reason text) returns jsonb
language plpgsql security definer set search_path = public as $$
declare
  v_reason text; v_old content_items; v_new content_items; v_existed boolean; v_publish timestamptz;
  v_title text; v_body text; v_eyebrow text; v_headline text; v_accent text; v_button text; v_image text; v_prio int := coalesce(p_priority, 0);
begin
  if auth.uid() is null then raise exception 'not_authenticated'; end if;
  perform _admin_actor(array['owner', 'content']);
  v_reason := _admin_reason(p_reason);
  perform _rate_limit('admin_write', 30, 60);
  if p_id is null or p_id !~ '^[a-z0-9_-]{2,40}$' then raise exception 'invalid_id'; end if;
  if p_kind is null or p_kind not in ('news', 'hero') then raise exception 'invalid_kind'; end if;
  if p_category is null or p_category not in ('update', 'event', 'tip', 'maintenance') then raise exception 'invalid_category'; end if;
  v_title := _admin_text(p_title, 1, 80, 'invalid_title');
  v_body := _admin_text(p_body, 1, 220, 'invalid_body');
  v_image := nullif(btrim(coalesce(p_image_key, '')), '');
  if v_image is not null and v_image !~ '^[a-z0-9_-]{1,40}$' then raise exception 'invalid_image'; end if;
  if v_prio < -1000 or v_prio > 1000 then raise exception 'invalid_priority'; end if;
  v_publish := coalesce(p_starts_at, now());
  if p_ends_at is not null and p_ends_at <= v_publish then raise exception 'invalid_schedule'; end if;
  if p_kind = 'hero' then
    v_headline := _admin_text(p_headline, 1, 40, 'invalid_headline');
    v_eyebrow := case when nullif(btrim(coalesce(p_eyebrow, '')), '') is null then null else _admin_text(p_eyebrow, 1, 80, 'invalid_eyebrow') end;
    v_accent := case when nullif(btrim(coalesce(p_headline_accent, '')), '') is null then null else _admin_text(p_headline_accent, 1, 40, 'invalid_headline') end;
    v_button := _admin_text(p_button, 1, 30, 'invalid_button');
    if p_action is null or p_action not in ('jobs', 'convoy', 'bus', 'map', 'garage') then raise exception 'invalid_action'; end if;
  end if;
  select * into v_old from content_items where id = p_id for update;
  v_existed := found;
  if v_existed and v_old.kind <> p_kind then raise exception 'kind_locked'; end if;
  insert into content_items(id, kind, category, title, body, image_key, priority, published, publish_at, expires_at, eyebrow, headline, headline_accent, action, button)
  values (p_id, p_kind, p_category, v_title, v_body, v_image, v_prio, coalesce(p_published, false), v_publish, p_ends_at,
          v_eyebrow, v_headline, v_accent, case when p_kind = 'hero' then p_action end, v_button)
  on conflict (id) do update set category = excluded.category, title = excluded.title, body = excluded.body, image_key = excluded.image_key, priority = excluded.priority,
    published = excluded.published, publish_at = excluded.publish_at, expires_at = excluded.expires_at, eyebrow = excluded.eyebrow, headline = excluded.headline,
    headline_accent = excluded.headline_accent, action = excluded.action, button = excluded.button
  returning * into v_new;
  perform _admin_log('save_content', 'content', p_id, v_reason, jsonb_build_object('created', not v_existed, 'kind', p_kind, 'published', v_new.published, 'state', _content_state(v_new),
                       'publish_at', v_new.publish_at, 'expires_at', v_new.expires_at));
  return _content_json(v_new);
end $$;

create function admin_set_content_published(p_id text, p_published boolean, p_reason text) returns jsonb
language plpgsql security definer set search_path = public as $$
declare v_reason text; v_new content_items;
begin
  if auth.uid() is null then raise exception 'not_authenticated'; end if;
  perform _admin_actor(array['owner', 'content']);
  v_reason := _admin_reason(p_reason);
  perform _rate_limit('admin_write', 30, 60);
  if p_published is null then raise exception 'invalid_state'; end if;
  update content_items set published = p_published where id = p_id returning * into v_new;
  if not found then raise exception 'content_not_found'; end if;
  perform _admin_log(case when p_published then 'publish_content' else 'unpublish_content' end, 'content', p_id, v_reason, jsonb_build_object('state', _content_state(v_new)));
  return _content_json(v_new);
end $$;

-- ---------------------------------------------------------------- broadcast (one in-game notification to every player)
create function admin_send_broadcast(p_title text, p_message text, p_reason text) returns jsonb
language plpgsql security definer set search_path = public as $$
declare v_reason text; v_title text; v_msg text; v_key text; v_n integer;
begin
  if auth.uid() is null then raise exception 'not_authenticated'; end if;
  perform _admin_actor(array['owner', 'operations']);
  v_reason := _admin_reason(p_reason);
  perform _rate_limit('admin_broadcast', 3, 3600);
  v_title := _admin_text(p_title, 3, 80, 'invalid_title');
  v_msg := _admin_text(p_message, 3, 300, 'invalid_message');
  v_key := 'admin-' || replace(gen_random_uuid()::text, '-', '');
  v_n := admin_broadcast(v_title, v_msg, v_key);
  perform _admin_log('broadcast', 'players', v_key, v_reason, jsonb_build_object('title', v_title, 'message', v_msg, 'recipients', v_n));
  return jsonb_build_object('key', v_key, 'recipients', v_n);
end $$;

create function admin_list_broadcasts(p_limit integer default 20) returns jsonb
language plpgsql security definer set search_path = public as $$
begin
  if auth.uid() is null then raise exception 'not_authenticated'; end if;
  perform _rate_limit('admin_read', 240, 60);
  perform _admin_actor(array['owner', 'operations']);
  return jsonb_build_object('items', coalesce((
    select jsonb_agg(jsonb_build_object('at', l.at, 'admin_id', l.admin_id, 'key', l.target_id, 'title', l.details ->> 'title', 'message', l.details ->> 'message',
             'recipients', (l.details ->> 'recipients')::int, 'reason', l.reason) order by l.id desc)
      from (select * from admin_audit_log where action = 'broadcast' order by id desc limit least(greatest(coalesce(p_limit, 20), 1), 50)) l), '[]'::jsonb));
end $$;

-- ---------------------------------------------------------------- routes and servers
create function admin_list_world() returns jsonb
language plpgsql security definer set search_path = public as $$
declare v_pop integer := get_online_count();
begin
  if auth.uid() is null then raise exception 'not_authenticated'; end if;
  perform _rate_limit('admin_read', 240, 60);
  perform _admin_actor(array['owner', 'operations']);
  return jsonb_build_object('server_time', now(), 'online_now', v_pop,
    'routes', coalesce((select jsonb_agg(jsonb_build_object('code', r.code, 'name', r.name, 'region', r.region, 'distance_km', r.distance_km, 'status', r.status,
                'unlock_level', r.unlock_level, 'playable', r.game_route_id is not null) order by r.code) from world_routes r), '[]'::jsonb),
    'servers', coalesce((select jsonb_agg(jsonb_build_object('id', g.id, 'name', g.name, 'region', g.region, 'capacity', g.capacity, 'status', g.status) order by g.name) from game_servers g), '[]'::jsonb));
end $$;

-- p_unlock_rank is the player level a route needs; null leaves it as it is. A route with no playable game route behind it cannot be opened.
create function admin_set_route_state(p_code text, p_status text, p_unlock_rank integer, p_reason text) returns jsonb
language plpgsql security definer set search_path = public as $$
declare v_reason text; r world_routes; v_before jsonb;
begin
  if auth.uid() is null then raise exception 'not_authenticated'; end if;
  perform _admin_actor(array['owner', 'operations']);
  v_reason := _admin_reason(p_reason);
  perform _rate_limit('admin_write', 30, 60);
  if p_status is null or p_status not in ('OPEN', 'PLANNED', 'LOCKED') then raise exception 'invalid_status'; end if;
  if p_unlock_rank is not null and (p_unlock_rank < 1 or p_unlock_rank > 100) then raise exception 'invalid_rank'; end if;
  select * into r from world_routes where code = p_code for update;
  if not found then raise exception 'route_not_found'; end if;
  if p_status = 'OPEN' and r.status <> 'OPEN' and r.game_route_id is null then raise exception 'route_not_playable'; end if;
  v_before := jsonb_build_object('status', r.status, 'unlock_level', r.unlock_level);
  update world_routes set status = p_status::world_status, unlock_level = coalesce(p_unlock_rank, unlock_level) where code = p_code returning * into r;
  perform _admin_log('set_route', 'route', p_code, v_reason, jsonb_build_object('from', v_before, 'to', jsonb_build_object('status', r.status, 'unlock_level', r.unlock_level)));
  return jsonb_build_object('code', r.code, 'status', r.status, 'unlock_level', r.unlock_level);
end $$;

-- p_capacity: null keeps it, 0 removes the limit. p_region: null keeps it.
create function admin_set_server_state(p_id text, p_status text, p_capacity integer, p_region text, p_reason text) returns jsonb
language plpgsql security definer set search_path = public as $$
declare v_reason text; g game_servers; v_before jsonb; v_region text;
begin
  if auth.uid() is null then raise exception 'not_authenticated'; end if;
  perform _admin_actor(array['owner', 'operations']);
  v_reason := _admin_reason(p_reason);
  perform _rate_limit('admin_write', 30, 60);
  if p_status is not null and p_status not in ('ONLINE', 'MAINTENANCE') then raise exception 'invalid_status'; end if;
  if p_capacity is not null and (p_capacity < 0 or p_capacity > 100000) then raise exception 'invalid_capacity'; end if;
  v_region := case when nullif(btrim(coalesce(p_region, '')), '') is null then null else _admin_text(p_region, 2, 40, 'invalid_region') end;
  select * into g from game_servers where id = p_id for update;
  if not found then raise exception 'server_not_found'; end if;
  v_before := jsonb_build_object('status', g.status, 'capacity', g.capacity, 'region', g.region);
  update game_servers set status = coalesce(p_status, status), region = coalesce(v_region, region),
         capacity = case when p_capacity is null then capacity when p_capacity = 0 then null else p_capacity end where id = p_id returning * into g;
  perform _admin_log('set_server', 'server', p_id, v_reason, jsonb_build_object('from', v_before, 'to', jsonb_build_object('status', g.status, 'capacity', g.capacity, 'region', g.region)));
  return jsonb_build_object('id', g.id, 'status', g.status, 'capacity', g.capacity, 'region', g.region);
end $$;

-- ---------------------------------------------------------------- reports queue
-- p_state: open (never reviewed, or reported again since), reviewed, all. Newest and most-reported first for the open queue.
create function admin_list_reports(p_state text default 'open', p_limit integer default 30) returns jsonb
language plpgsql security definer set search_path = public as $$
declare v_lim int := least(greatest(coalesce(p_limit, 30), 1), 100); v_items jsonb;
begin
  if auth.uid() is null then raise exception 'not_authenticated'; end if;
  perform _rate_limit('admin_read', 240, 60);
  perform _admin_actor(array['owner', 'support']);
  if p_state is null or p_state not in ('open', 'reviewed', 'all') then raise exception 'invalid_state'; end if;
  select coalesce(jsonb_agg(x.row order by x.is_open desc, x.n desc, x.last_at desc), '[]'::jsonb) into v_items from (
    select jsonb_build_object('message_id', m.id, 'body', m.body, 'moderation', m.moderation, 'sent_at', m.created_at, 'sender_id', m.sender_id,
             'sender_name', pr.display_name, 'reports', t.n, 'reasons', t.reasons, 'first_reported_at', t.first_at, 'last_reported_at', t.last_at,
             'decision', rv.decision, 'reviewed_at', rv.reviewed_at, 'open', (rv.message_id is null or t.last_at > rv.reviewed_at),
             'sender_sanctions', coalesce((select jsonb_agg(distinct s.kind) from player_sanctions s
                                            where s.player_id = m.sender_id and s.lifted_at is null and (s.expires_at is null or s.expires_at > now())), '[]'::jsonb)) as row,
           (rv.message_id is null or t.last_at > rv.reviewed_at) as is_open, t.n, t.last_at
      from (select message_id, count(*)::int n, min(created_at) first_at, max(created_at) last_at,
                   (select jsonb_object_agg(reason, c) from (select reason, count(*) c from chat_reports r2 where r2.message_id = r.message_id group by reason) q) reasons
              from chat_reports r group by message_id) t
      join convoy_messages m on m.id = t.message_id
      left join profiles pr on pr.id = m.sender_id
      left join chat_report_reviews rv on rv.message_id = m.id
     where p_state = 'all'
        or (p_state = 'open' and (rv.message_id is null or t.last_at > rv.reviewed_at))
        or (p_state = 'reviewed' and rv.message_id is not null and t.last_at <= rv.reviewed_at)
     order by (rv.message_id is null or t.last_at > rv.reviewed_at) desc, t.n desc, t.last_at desc limit v_lim) x;
  return jsonb_build_object('items', v_items, 'server_time', now());
end $$;

-- One report with the lines around it and what is already known about the sender. Reading chat is personal data, so this one is audited.
create function admin_get_report(p_message bigint) returns jsonb
language plpgsql security definer set search_path = public as $$
declare m convoy_messages; v_context jsonb; v_removed int; v_prior int;
begin
  if auth.uid() is null then raise exception 'not_authenticated'; end if;
  perform _rate_limit('admin_view', 60, 60);
  perform _admin_actor(array['owner', 'support']);
  select * into m from convoy_messages where id = p_message and exists (select 1 from chat_reports where message_id = p_message);
  if not found then raise exception 'report_not_found'; end if;
  select coalesce(jsonb_agg(jsonb_build_object('id', c.id, 'kind', c.kind, 'sender_name', pr.display_name, 'body', c.body, 'moderation', c.moderation, 'at', c.created_at,
           'reported', c.id = p_message) order by c.id), '[]'::jsonb) into v_context
    from convoy_messages c left join profiles pr on pr.id = c.sender_id where c.convoy_id = m.convoy_id and c.id between p_message - 4 and p_message + 3;
  select count(*) into v_removed from convoy_messages c where c.sender_id = m.sender_id and c.moderation = 'removed';
  select count(*) into v_prior from player_sanctions s where s.player_id = m.sender_id;
  perform _admin_log('view_report', 'chat_message', p_message::text, null, jsonb_build_object('sender_id', m.sender_id));
  return jsonb_build_object('message_id', m.id, 'sender_id', m.sender_id, 'context', v_context,
    'sender', jsonb_build_object('removed_lines', v_removed, 'sanctions_so_far', v_prior));
end $$;

create function admin_review_report(p_message bigint, p_decision text, p_reason text) returns jsonb
language plpgsql security definer set search_path = public as $$
declare v_reason text; m convoy_messages;
begin
  if auth.uid() is null then raise exception 'not_authenticated'; end if;
  perform _admin_actor(array['owner', 'support']);
  v_reason := _admin_reason(p_reason);
  perform _rate_limit('admin_write', 30, 60);
  if p_decision is null or p_decision not in ('dismiss', 'remove') then raise exception 'invalid_decision'; end if;
  select * into m from convoy_messages where id = p_message for update;
  if not found or not exists (select 1 from chat_reports where message_id = p_message) then raise exception 'report_not_found'; end if;
  update convoy_messages set moderation = case p_decision when 'remove' then 'removed' else 'ok' end where id = p_message;
  insert into chat_report_reviews(message_id, decision, reviewer_id, reason) values (p_message, p_decision, auth.uid(), v_reason)
  on conflict (message_id) do update set decision = excluded.decision, reviewer_id = excluded.reviewer_id, reason = excluded.reason, reviewed_at = now();
  perform _admin_log('review_report', 'chat_message', p_message::text, v_reason, jsonb_build_object('decision', p_decision, 'sender_id', m.sender_id, 'was', m.moderation));
  return jsonb_build_object('message_id', p_message, 'decision', p_decision);
end $$;

-- ---------------------------------------------------------------- sanctions
create function admin_apply_sanction(p_player uuid, p_kind text, p_hours integer, p_reason text, p_message bigint default null) returns jsonb
language plpgsql security definer set search_path = public as $$
declare v_role text; v_reason text; v_id uuid; v_expires timestamptz; v_synced boolean := null; v_ended int := 0;
begin
  if auth.uid() is null then raise exception 'not_authenticated'; end if;
  v_role := _admin_actor(array['owner', 'support']);
  v_reason := _admin_reason(p_reason);
  perform _rate_limit('admin_write', 30, 60);
  if p_kind is null or p_kind not in ('mute', 'suspend', 'ban') then raise exception 'invalid_kind'; end if;
  if p_kind = 'ban' and v_role <> 'owner' then raise exception 'not_allowed'; end if;
  if p_kind <> 'ban' then
    if p_hours is null or p_hours < 1 or p_hours > 2160 then raise exception 'invalid_duration'; end if;
    v_expires := now() + make_interval(hours => p_hours);
  end if;
  if not exists (select 1 from profiles where id = p_player) then raise exception 'player_not_found'; end if;
  if exists (select 1 from admin_users where user_id = p_player and active) then raise exception 'cannot_sanction_admin'; end if;
  if p_message is not null and not exists (select 1 from convoy_messages where id = p_message and sender_id = p_player) then raise exception 'message_mismatch'; end if;
  -- serialise per player so two admins cannot stack the same restriction
  perform 1 from profiles where id = p_player for update;
  if _sanction_live(p_player, array['ban']) then raise exception 'already_banned'; end if;
  if _sanction_live(p_player, array[p_kind]) then raise exception 'already_sanctioned'; end if;
  insert into player_sanctions(player_id, kind, reason, message_id, created_by, expires_at) values (p_player, p_kind, v_reason, p_message, auth.uid(), v_expires) returning id into v_id;
  if p_kind in ('suspend', 'ban') then
    with e as (update game_sessions set ended_at = now(), end_reason = 'logout' where player_id = p_player and ended_at is null returning 1) select count(*) into v_ended from e;
    v_synced := _sync_ban(p_player);
  end if;
  if p_kind <> 'ban' then
    perform _notify(p_player, 'server', case p_kind when 'mute' then 'Chat muted' else 'Account suspended' end,
                    case p_kind when 'mute' then 'You cannot send chat messages until ' else 'You cannot play online until ' end || to_char(v_expires at time zone 'UTC', 'YYYY-MM-DD HH24:MI') || ' UTC.',
                    '{}'::jsonb, 'sanction:' || v_id);
  end if;
  perform _admin_log('apply_sanction', 'player', p_player::text, v_reason,
                     jsonb_build_object('sanction_id', v_id, 'kind', p_kind, 'hours', p_hours, 'expires_at', v_expires, 'message_id', p_message, 'sessions_ended', v_ended, 'auth_synced', v_synced));
  return jsonb_build_object('id', v_id, 'kind', p_kind, 'expires_at', v_expires, 'sessions_ended', v_ended);
end $$;

create function admin_lift_sanction(p_id uuid, p_reason text) returns jsonb
language plpgsql security definer set search_path = public as $$
declare v_role text; v_reason text; s player_sanctions; v_synced boolean := null;
begin
  if auth.uid() is null then raise exception 'not_authenticated'; end if;
  v_role := _admin_actor(array['owner', 'support']);
  v_reason := _admin_reason(p_reason);
  perform _rate_limit('admin_write', 30, 60);
  select * into s from player_sanctions where id = p_id for update;
  if not found then raise exception 'sanction_not_found'; end if;
  if s.kind = 'ban' and v_role <> 'owner' then raise exception 'not_allowed'; end if;
  if s.lifted_at is not null or (s.expires_at is not null and s.expires_at <= now()) then raise exception 'sanction_not_active'; end if;
  update player_sanctions set lifted_at = now(), lifted_by = auth.uid(), lift_reason = v_reason where id = p_id;
  if s.kind in ('suspend', 'ban') then v_synced := _sync_ban(s.player_id); end if;
  perform _admin_log('lift_sanction', 'player', s.player_id::text, v_reason, jsonb_build_object('sanction_id', p_id, 'kind', s.kind, 'auth_synced', v_synced));
  return jsonb_build_object('id', p_id, 'kind', s.kind);
end $$;

-- p_state: active, history (everything, newest first). p_player narrows it to one player.
create function admin_list_sanctions(p_state text default 'active', p_player uuid default null, p_limit integer default 50) returns jsonb
language plpgsql security definer set search_path = public as $$
declare v_lim int := least(greatest(coalesce(p_limit, 50), 1), 200);
begin
  if auth.uid() is null then raise exception 'not_authenticated'; end if;
  perform _rate_limit('admin_read', 240, 60);
  perform _admin_actor(array['owner', 'support']);
  if p_state is null or p_state not in ('active', 'history') then raise exception 'invalid_state'; end if;
  return jsonb_build_object('server_time', now(), 'items', coalesce((
    select jsonb_agg(jsonb_build_object('id', s.id, 'player_id', s.player_id, 'player_name', pr.display_name, 'kind', s.kind, 'reason', s.reason, 'message_id', s.message_id,
             'created_at', s.created_at, 'expires_at', s.expires_at, 'lifted_at', s.lifted_at, 'lift_reason', s.lift_reason, 'created_by_name', ab.display_name,
             'active', s.lifted_at is null and (s.expires_at is null or s.expires_at > now())) order by s.created_at desc)
      from (select * from player_sanctions q
             where (p_player is null or q.player_id = p_player)
               and (p_state = 'history' or (q.lifted_at is null and (q.expires_at is null or q.expires_at > now())))
             order by q.created_at desc limit v_lim) s
      join profiles pr on pr.id = s.player_id left join profiles ab on ab.id = s.created_by), '[]'::jsonb));
end $$;

-- ---------------------------------------------------------------- lock-down (same convention as every migration)
do $$ declare r record;
begin
  for r in select p.oid::regprocedure as sig, p.proname from pg_proc p
            where p.pronamespace = 'public'::regnamespace and p.oid not in (select oid from _fn_before) loop
    execute format('revoke execute on function %s from public, anon, authenticated', r.sig);
    if r.proname in ('admin_list_content', 'admin_save_content', 'admin_set_content_published', 'admin_send_broadcast', 'admin_list_broadcasts', 'admin_list_world',
                     'admin_set_route_state', 'admin_set_server_state', 'admin_list_reports', 'admin_get_report', 'admin_review_report', 'admin_apply_sanction',
                     'admin_lift_sanction', 'admin_list_sanctions') then
      execute format('grant execute on function %s to authenticated', r.sig);
    end if;
  end loop;
end $$;
