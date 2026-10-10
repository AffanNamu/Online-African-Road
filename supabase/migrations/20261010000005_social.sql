-- Backend foundation, step 5: friends, recent players and persistent convoys.
-- Rules: a player may only create/modify relationships that involve themselves; nobody is a "friend" until a request is
-- accepted; presence is only shown to self, friends and convoy mates (recent players see online/offline only).
create temp table _fn_before as select oid from pg_proc where pronamespace = 'public'::regnamespace;

-- ---------------------------------------------------------------- friendships
create table friendships (
  id           uuid primary key default gen_random_uuid(),
  requester_id uuid not null references profiles(id) on delete cascade,
  addressee_id uuid not null references profiles(id) on delete cascade,
  status       text not null default 'pending' check (status in ('pending','accepted','rejected')),
  created_at   timestamptz not null default now(),
  responded_at timestamptz,
  check (requester_id <> addressee_id)
);
-- One row per unordered pair, whoever asked first.
create unique index friendships_pair_key on friendships (least(requester_id, addressee_id), greatest(requester_id, addressee_id));
create index friendships_addressee_idx on friendships(addressee_id, status);
create index friendships_requester_idx on friendships(requester_id, status);
alter table friendships enable row level security;
revoke all on friendships from anon, authenticated;

-- Everyone a player has recently shared a convoy with (powers "Recent players"; capped per player).
create table player_encounters (
  player_id uuid not null references profiles(id) on delete cascade,
  other_id  uuid not null references profiles(id) on delete cascade,
  last_at   timestamptz not null default now(),
  times     integer not null default 1,
  context   text not null default 'convoy' check (context in ('convoy')),
  primary key (player_id, other_id),
  check (player_id <> other_id)
);
create index player_encounters_recent_idx on player_encounters(player_id, last_at desc);
alter table player_encounters enable row level security;
revoke all on player_encounters from anon, authenticated;

-- ---------------------------------------------------------------- helpers
create function _friend_status(p_viewer uuid, p_other uuid) returns text
language plpgsql stable security definer set search_path = public as $$
declare f friendships;
begin
  if p_viewer = p_other then return 'self'; end if;
  select * into f from friendships
   where least(requester_id, addressee_id) = least(p_viewer, p_other) and greatest(requester_id, addressee_id) = greatest(p_viewer, p_other);
  if not found then return 'none'; end if;
  if f.status = 'accepted' then return 'friends'; end if;
  if f.status = 'pending' then return case when f.requester_id = p_viewer then 'outgoing' else 'incoming' end; end if;
  return 'none';   -- rejected: indistinguishable from "never asked"
end $$;

create function _shares_convoy(p_a uuid, p_b uuid) returns boolean
language sql stable security definer set search_path = public as $$
  select exists (select 1 from convoy_members a join convoy_members b on b.convoy_id = a.convoy_id
                  where a.player_id = p_a and b.player_id = p_b) $$;

-- 'full' (activity, route, city) | 'basic' (online flag + last seen) | 'none'
create function _presence_visibility(p_viewer uuid, p_other uuid) returns text
language plpgsql stable security definer set search_path = public as $$
begin
  if p_viewer = p_other or _friend_status(p_viewer, p_other) = 'friends' or _shares_convoy(p_viewer, p_other) then return 'full'; end if;
  if exists (select 1 from player_encounters where player_id = p_viewer and other_id = p_other) then return 'basic'; end if;
  return 'none';
end $$;

-- Public card for another player. Never contains coins, e-mail or anything private.
create function _person_json(p_viewer uuid, p_other uuid) returns jsonb
language plpgsql stable security definer set search_path = public as $$
declare p profiles; v_vis text; v_pres jsonb;
begin
  select * into p from profiles where id = p_other;
  if not found then return null; end if;
  v_vis := _presence_visibility(p_viewer, p_other);
  v_pres := case v_vis when 'full' then _presence_of(p_other)
                       when 'basic' then (select jsonb_build_object('online', x->'online', 'last_seen_at', x->'last_seen_at') from (select _presence_of(p_other) x) q)
                       else null end;
  return jsonb_build_object('player_id', p.id, 'display_name', p.display_name, 'username', p.username, 'avatar_id', p.avatar_id,
    'country_code', p.country_code, 'level', p.level, 'friend_status', _friend_status(p_viewer, p_other), 'presence', v_pres);
end $$;

create function _record_encounters(p_convoy uuid, p_player uuid) returns void
language plpgsql security definer set search_path = public as $$
declare o record; v_keep integer := cfg('recent_players_keep')::int;
begin
  for o in select player_id from convoy_members where convoy_id = p_convoy and player_id <> p_player loop
    insert into player_encounters(player_id, other_id) values (p_player, o.player_id), (o.player_id, p_player)
    on conflict (player_id, other_id) do update set last_at = now(), times = player_encounters.times + 1;
    delete from player_encounters where player_id = o.player_id and other_id in (
      select other_id from player_encounters where player_id = o.player_id order by last_at desc offset v_keep);
  end loop;
  delete from player_encounters where player_id = p_player and other_id in (
    select other_id from player_encounters where player_id = p_player order by last_at desc offset v_keep);
end $$;

-- ---------------------------------------------------------------- friends API
create function find_player(p_username text) returns jsonb
language plpgsql security definer set search_path = public as $$
declare v_uid uuid := auth.uid(); v_id uuid;
begin
  if v_uid is null then raise exception 'not_authenticated'; end if;
  perform _rate_limit('find_player', 30, 60);
  -- exact handle only: no prefix search, so the player list cannot be enumerated
  select id into v_id from profiles where username = lower(trim(p_username));
  if v_id is null then raise exception 'player_not_found'; end if;
  return _person_json(v_uid, v_id);
end $$;

create function get_public_profile(p_player uuid) returns jsonb
language plpgsql security definer set search_path = public as $$
declare v_uid uuid := auth.uid(); p profiles;
begin
  if v_uid is null then raise exception 'not_authenticated'; end if;
  perform _rate_limit('profile_public', 60, 60);
  select * into p from profiles where id = p_player;
  if not found then raise exception 'player_not_found'; end if;
  return _person_json(v_uid, p_player) || jsonb_build_object(
    'experience', p.experience, 'distance_km', p.distance_km, 'jobs_completed', p.jobs_completed, 'member_since', p.created_at,
    'achievements', (select count(*) from player_achievements where player_id = p_player));
end $$;

create function get_friend_status(p_player uuid) returns text
language plpgsql security definer set search_path = public as $$
begin
  if auth.uid() is null then raise exception 'not_authenticated'; end if;
  perform _rate_limit('friend_read', 120, 60);
  return _friend_status(auth.uid(), p_player);
end $$;

create function send_friend_request(p_player uuid) returns jsonb
language plpgsql security definer set search_path = public as $$
declare v_uid uuid := auth.uid(); f friendships; v_name text; v_id uuid;
begin
  if v_uid is null then raise exception 'not_authenticated'; end if;
  perform _rate_limit('friend_request', 20, 3600);
  if p_player = v_uid then raise exception 'cannot_friend_self'; end if;
  if not exists (select 1 from profiles where id = p_player) then raise exception 'player_not_found'; end if;
  select display_name into v_name from profiles where id = v_uid;

  select * into f from friendships
   where least(requester_id, addressee_id) = least(v_uid, p_player) and greatest(requester_id, addressee_id) = greatest(v_uid, p_player) for update;
  if found then
    if f.status = 'accepted' then raise exception 'already_friends'; end if;
    if f.status = 'pending' and f.requester_id = v_uid then raise exception 'request_pending'; end if;
    if f.status = 'pending' then      -- they already asked us: asking back means yes
      if (select count(*) from friendships where status = 'accepted' and v_uid in (requester_id, addressee_id)) >= cfg('friend_limit') then raise exception 'friend_limit_reached'; end if;
      update friendships set status = 'accepted', responded_at = now() where id = f.id;
      perform _notify(p_player, 'friend_event', 'New friend', v_name || ' accepted your friend request.',
                      jsonb_build_object('player_id', v_uid), 'friendacc:' || f.id);
      perform _track(v_uid, 'friend_added', '{}'::jsonb);
      return jsonb_build_object('request_id', f.id, 'status', 'accepted');
    end if;
    -- rejected earlier: the person who was rejected must wait before asking again
    if f.requester_id = v_uid and f.responded_at > now() - make_interval(hours => cfg('friend_resend_hours')::int) then raise exception 'request_cooldown'; end if;
    update friendships set requester_id = v_uid, addressee_id = p_player, status = 'pending', created_at = now(), responded_at = null where id = f.id;
    v_id := f.id;
  else
    if (select count(*) from friendships where status = 'accepted' and v_uid in (requester_id, addressee_id)) >= cfg('friend_limit') then raise exception 'friend_limit_reached'; end if;
    if (select count(*) from friendships where status = 'pending' and requester_id = v_uid) >= cfg('friend_pending_limit') then raise exception 'too_many_pending'; end if;
    insert into friendships(requester_id, addressee_id) values (v_uid, p_player) returning id into v_id;
  end if;
  perform _notify(p_player, 'friend_event', 'Friend request', v_name || ' wants to be your friend.',
                  jsonb_build_object('request_id', v_id, 'player_id', v_uid),
                  'friendreq:' || v_id || ':' || extract(epoch from now())::bigint);
  return jsonb_build_object('request_id', v_id, 'status', 'pending');
end $$;

-- Only the addressee can answer. Anyone else (or a stale id) gets "not found".
create function respond_friend_request(p_request uuid, p_accept boolean) returns text
language plpgsql security definer set search_path = public as $$
declare v_uid uuid := auth.uid(); f friendships; v_name text;
begin
  if v_uid is null then raise exception 'not_authenticated'; end if;
  perform _rate_limit('friend_respond', 60, 60);
  select * into f from friendships where id = p_request and addressee_id = v_uid and status = 'pending' for update;
  if not found then raise exception 'request_not_found'; end if;
  if p_accept then
    if (select count(*) from friendships where status = 'accepted' and v_uid in (requester_id, addressee_id)) >= cfg('friend_limit')
       or (select count(*) from friendships where status = 'accepted' and f.requester_id in (requester_id, addressee_id)) >= cfg('friend_limit') then
      raise exception 'friend_limit_reached';
    end if;
    update friendships set status = 'accepted', responded_at = now() where id = f.id;
    select display_name into v_name from profiles where id = v_uid;
    perform _notify(f.requester_id, 'friend_event', 'New friend', v_name || ' accepted your friend request.',
                    jsonb_build_object('player_id', v_uid), 'friendacc:' || f.id);
    perform _track(v_uid, 'friend_added', '{}'::jsonb);
    return 'accepted';
  end if;
  update friendships set status = 'rejected', responded_at = now() where id = f.id;
  return 'rejected';
end $$;

-- Unfriend, or cancel a request you sent.
create function remove_friend(p_player uuid) returns void
language plpgsql security definer set search_path = public as $$
declare v_uid uuid := auth.uid();
begin
  if v_uid is null then raise exception 'not_authenticated'; end if;
  perform _rate_limit('friend_write', 60, 60);
  delete from friendships
   where least(requester_id, addressee_id) = least(v_uid, p_player) and greatest(requester_id, addressee_id) = greatest(v_uid, p_player)
     and (status = 'accepted' or (status = 'pending' and requester_id = v_uid));
  if not found then raise exception 'not_friends'; end if;
end $$;

create function list_friends() returns jsonb
language plpgsql security definer set search_path = public as $$
declare v_uid uuid := auth.uid();
begin
  if v_uid is null then raise exception 'not_authenticated'; end if;
  perform _rate_limit('friend_read', 120, 60);
  return jsonb_build_object('items', coalesce((
    select jsonb_agg(_person_json(v_uid, x.other) || jsonb_build_object('since', x.since)
                     order by (_presence_of(x.other)->>'online')::boolean desc, x.name)
      from (select case when f.requester_id = v_uid then f.addressee_id else f.requester_id end as other,
                   f.responded_at as since,
                   (select display_name from profiles where id = case when f.requester_id = v_uid then f.addressee_id else f.requester_id end) as name
              from friendships f where f.status = 'accepted' and v_uid in (f.requester_id, f.addressee_id)) x), '[]'::jsonb),
    'server_time', now());
end $$;

create function list_friend_requests() returns jsonb
language plpgsql security definer set search_path = public as $$
declare v_uid uuid := auth.uid();
begin
  if v_uid is null then raise exception 'not_authenticated'; end if;
  perform _rate_limit('friend_read', 120, 60);
  return jsonb_build_object(
    'incoming', coalesce((select jsonb_agg(jsonb_build_object('request_id', f.id, 'created_at', f.created_at, 'person', _person_json(v_uid, f.requester_id)) order by f.created_at desc)
                            from friendships f where f.addressee_id = v_uid and f.status = 'pending'), '[]'::jsonb),
    'outgoing', coalesce((select jsonb_agg(jsonb_build_object('request_id', f.id, 'created_at', f.created_at, 'person', _person_json(v_uid, f.addressee_id)) order by f.created_at desc)
                            from friendships f where f.requester_id = v_uid and f.status = 'pending'), '[]'::jsonb),
    'server_time', now());
end $$;

create function list_recent_players(p_limit integer default 20) returns jsonb
language plpgsql security definer set search_path = public as $$
declare v_uid uuid := auth.uid();
begin
  if v_uid is null then raise exception 'not_authenticated'; end if;
  perform _rate_limit('friend_read', 120, 60);
  return jsonb_build_object('items', coalesce((
    select jsonb_agg(_person_json(v_uid, e.other_id) || jsonb_build_object('last_played_at', e.last_at, 'times', e.times) order by e.last_at desc)
      from (select other_id, last_at, times from player_encounters where player_id = v_uid
             order by last_at desc limit least(greatest(coalesce(p_limit, 20), 1), 50)) e), '[]'::jsonb),
    'server_time', now());
end $$;

-- Presence for specific players, limited to those the caller may see (self, friends, convoy mates, recent players).
-- Players the caller may not see are simply absent from the answer.
create function get_presence(p_players uuid[]) returns jsonb
language plpgsql security definer set search_path = public as $$
declare v_uid uuid := auth.uid();
begin
  if v_uid is null then raise exception 'not_authenticated'; end if;
  perform _rate_limit('presence_read', 120, 60);
  if array_length(p_players, 1) > 50 then raise exception 'too_many_players'; end if;
  return jsonb_build_object('items', coalesce((
    select jsonb_agg(jsonb_build_object('player_id', i, 'presence', (_person_json(v_uid, i))->'presence'))
      from (select distinct unnest(p_players) as i) q
     where _presence_visibility(v_uid, i) <> 'none'), '[]'::jsonb), 'server_time', now());
end $$;

-- ---------------------------------------------------------------- convoys
alter table convoys
  add column capacity   integer not null default 8 check (capacity between 2 and 8),
  add column is_private boolean not null default false;
grant select (capacity, is_private) on convoys to authenticated;     -- session_code stays hidden (migration 4)
create index convoys_open_idx on convoys(created_at desc) where disbanded_at is null;

create table convoy_invites (
  id         uuid primary key default gen_random_uuid(),
  convoy_id  uuid not null references convoys(id) on delete cascade,
  inviter_id uuid not null references profiles(id) on delete cascade,
  invitee_id uuid not null references profiles(id) on delete cascade,
  status     text not null default 'pending' check (status in ('pending','accepted','declined','revoked')),
  created_at timestamptz not null default now(),
  expires_at timestamptz not null default now() + interval '1 day',
  check (inviter_id <> invitee_id)
);
create unique index convoy_invites_pending_key on convoy_invites(convoy_id, invitee_id) where status = 'pending';
create index convoy_invites_invitee_idx on convoy_invites(invitee_id, status);
alter table convoy_invites enable row level security;
revoke all on convoy_invites from anon, authenticated;

-- Membership rules enforced where the row is written, so EVERY path (join_convoy, invite accept, future code) obeys them.
-- Locking the convoy row serialises concurrent joins: capacity can never be exceeded by a race.
create function _convoy_member_guard() returns trigger
language plpgsql security definer set search_path = public as $$
declare c convoys;
begin
  select * into c from convoys where id = new.convoy_id for update;
  if not found or c.disbanded_at is not null then raise exception 'convoy_not_found'; end if;
  if (select count(*) from convoy_members where convoy_id = new.convoy_id) >= c.capacity then raise exception 'convoy_full'; end if;
  if c.is_private and new.player_id <> c.leader_id
     and not exists (select 1 from convoy_invites i where i.convoy_id = c.id and i.invitee_id = new.player_id and i.status = 'accepted') then
    raise exception 'invite_required';
  end if;
  return new;
end $$;
create trigger convoy_members_guard before insert on convoy_members for each row execute function _convoy_member_guard();

create function _convoy_member_added() returns trigger
language plpgsql security definer set search_path = public as $$
declare c convoys; v_name text;
begin
  select * into c from convoys where id = new.convoy_id;
  if new.player_id = c.leader_id then
    perform _track(new.player_id, 'convoy_created', jsonb_build_object('convoy_id', c.id));
  else
    select display_name into v_name from profiles where id = new.player_id;
    perform _track(new.player_id, 'convoy_joined', jsonb_build_object('convoy_id', c.id));
    perform _notify(c.leader_id, 'convoy_event', 'Convoy update', v_name || ' joined ' || c.name || '.',
                    jsonb_build_object('convoy_id', c.id, 'player_id', new.player_id));
  end if;
  perform _record_encounters(new.convoy_id, new.player_id);
  return new;
end $$;
create trigger convoy_members_added after insert on convoy_members for each row execute function _convoy_member_added();

create function _convoy_member_removed() returns trigger
language plpgsql security definer set search_path = public as $$
declare c convoys; v_name text;
begin
  select * into c from convoys where id = old.convoy_id;
  perform _track(old.player_id, 'convoy_left', jsonb_build_object('convoy_id', old.convoy_id));
  if c.id is not null and c.disbanded_at is null and old.player_id <> c.leader_id and auth.uid() is distinct from c.leader_id then
    select display_name into v_name from profiles where id = old.player_id;
    perform _notify(c.leader_id, 'convoy_event', 'Convoy update', v_name || ' left ' || c.name || '.',
                    jsonb_build_object('convoy_id', c.id, 'player_id', old.player_id));
  end if;
  return old;
end $$;
create trigger convoy_members_removed after delete on convoy_members for each row execute function _convoy_member_removed();

create function _convoy_leader_changed() returns trigger
language plpgsql security definer set search_path = public as $$
begin
  if new.leader_id <> old.leader_id then
    perform _notify(new.leader_id, 'convoy_event', 'You lead the convoy', 'You are now the leader of ' || new.name || '.',
                    jsonb_build_object('convoy_id', new.id));
  end if;
  return new;
end $$;
create trigger convoys_leader_changed after update of leader_id on convoys for each row execute function _convoy_leader_changed();

create function _convoy_json(p_viewer uuid, c convoys) returns jsonb
language plpgsql stable security definer set search_path = public as $$
declare v_members jsonb; v_count integer;
begin
  select coalesce(jsonb_agg((_person_json(p_viewer, m.player_id) - 'friend_status') || jsonb_build_object(
           'is_leader', m.player_id = c.leader_id, 'joined_at', m.joined_at) order by m.joined_at, m.player_id), '[]'::jsonb), count(*)
    into v_members, v_count from convoy_members m where m.convoy_id = c.id;
  return jsonb_build_object('id', c.id, 'name', c.name, 'leader_id', c.leader_id, 'is_leader', p_viewer = c.leader_id,
    'capacity', c.capacity, 'is_private', c.is_private, 'member_count', v_count,
    'status', case when c.disbanded_at is not null then 'disbanded' when v_count >= c.capacity then 'full'
                   when c.session_code is not null then 'in_session' else 'open' end,
    'session_active', c.session_code is not null, 'created_at', c.created_at, 'members', v_members);
end $$;

-- The caller's current convoy (members, leader, capacity, status) or null. The join code is NOT included: members fetch it
-- through get_convoy_session_code, which stays the single place that releases it.
create function get_my_convoy() returns jsonb
language plpgsql security definer set search_path = public as $$
declare v_uid uuid := auth.uid(); c convoys;
begin
  if v_uid is null then raise exception 'not_authenticated'; end if;
  perform _rate_limit('convoy_read', 120, 60);
  select cv.* into c from convoy_members m join convoys cv on cv.id = m.convoy_id where m.player_id = v_uid and cv.disbanded_at is null;
  if not found then return jsonb_build_object('convoy', null, 'server_time', now()); end if;
  return jsonb_build_object('convoy', _convoy_json(v_uid, c), 'server_time', now());
end $$;

create function list_open_convoys(p_limit integer default 20, p_offset integer default 0) returns jsonb
language plpgsql security definer set search_path = public as $$
declare v_uid uuid := auth.uid();
begin
  if v_uid is null then raise exception 'not_authenticated'; end if;
  perform _rate_limit('convoy_read', 120, 60);
  return jsonb_build_object('items', coalesce((
    select jsonb_agg(jsonb_build_object('id', c.id, 'name', c.name, 'leader', (select display_name from profiles where id = c.leader_id),
             'capacity', c.capacity, 'member_count', (select count(*) from convoy_members where convoy_id = c.id),
             'session_active', c.session_code is not null, 'created_at', c.created_at) order by c.created_at desc)
      from (select * from convoys where disbanded_at is null and not is_private order by created_at desc
             limit least(greatest(coalesce(p_limit, 20), 1), 50) offset greatest(coalesce(p_offset, 0), 0)) c), '[]'::jsonb),
    'server_time', now());
end $$;

create function set_convoy_options(p_capacity integer default null, p_private boolean default null) returns jsonb
language plpgsql security definer set search_path = public as $$
declare v_uid uuid := auth.uid(); c convoys; v_n integer;
begin
  if v_uid is null then raise exception 'not_authenticated'; end if;
  perform _rate_limit('convoy_write', 60, 60);
  select * into c from convoys where leader_id = v_uid and disbanded_at is null for update;
  if not found then raise exception 'not_leader'; end if;
  select count(*) into v_n from convoy_members where convoy_id = c.id;
  if p_capacity is not null and (p_capacity < greatest(v_n, 2) or p_capacity > cfg('convoy_max_capacity')) then raise exception 'invalid_capacity'; end if;
  update convoys set capacity = coalesce(p_capacity, capacity), is_private = coalesce(p_private, is_private) where id = c.id returning * into c;
  return _convoy_json(v_uid, c);
end $$;

create function invite_to_convoy(p_player uuid) returns uuid
language plpgsql security definer set search_path = public as $$
declare v_uid uuid := auth.uid(); c convoys; v_id uuid; v_name text;
begin
  if v_uid is null then raise exception 'not_authenticated'; end if;
  perform _rate_limit('convoy_invite', 30, 3600);
  select * into c from convoys where leader_id = v_uid and disbanded_at is null for update;
  if not found then raise exception 'not_leader'; end if;
  if p_player = v_uid then raise exception 'cannot_invite_self'; end if;
  if not exists (select 1 from profiles where id = p_player) then raise exception 'player_not_found'; end if;
  if exists (select 1 from convoy_members where player_id = p_player) then raise exception 'player_in_convoy'; end if;
  if (select count(*) from convoy_members where convoy_id = c.id) >= c.capacity then raise exception 'convoy_full'; end if;
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

create function list_convoy_invites() returns jsonb
language plpgsql security definer set search_path = public as $$
declare v_uid uuid := auth.uid();
begin
  if v_uid is null then raise exception 'not_authenticated'; end if;
  perform _rate_limit('convoy_read', 120, 60);
  return jsonb_build_object('items', coalesce((
    select jsonb_agg(jsonb_build_object('invite_id', i.id, 'convoy_id', c.id, 'convoy_name', c.name, 'created_at', i.created_at,
             'expires_at', i.expires_at, 'from', _person_json(v_uid, i.inviter_id)) order by i.created_at desc)
      from convoy_invites i join convoys c on c.id = i.convoy_id
     where i.invitee_id = v_uid and i.status = 'pending' and i.expires_at > now() and c.disbanded_at is null), '[]'::jsonb),
    'server_time', now());
end $$;

-- Only the invitee can answer; the convoy's capacity / privacy rules are enforced by the membership trigger.
create function respond_convoy_invite(p_invite uuid, p_accept boolean) returns text
language plpgsql security definer set search_path = public as $$
declare v_uid uuid := auth.uid(); i convoy_invites; c convoys; v_name text;
begin
  if v_uid is null then raise exception 'not_authenticated'; end if;
  perform _rate_limit('convoy_write', 60, 60);
  select * into i from convoy_invites where id = p_invite and invitee_id = v_uid and status = 'pending' for update;
  if not found then raise exception 'invite_not_found'; end if;
  if i.expires_at <= now() then raise exception 'invite_expired'; end if;
  if not p_accept then
    update convoy_invites set status = 'declined' where id = i.id;
    return 'declined';
  end if;
  select * into c from convoys where id = i.convoy_id;
  if c.disbanded_at is not null then raise exception 'convoy_not_found'; end if;
  if exists (select 1 from convoy_members where player_id = v_uid) then raise exception 'already_in_convoy'; end if;
  update convoy_invites set status = 'accepted' where id = i.id;
  insert into convoy_members(convoy_id, player_id) values (c.id, v_uid);
  select display_name into v_name from profiles where id = v_uid;
  perform _notify(i.inviter_id, 'convoy_event', 'Invite accepted', v_name || ' accepted your invite to ' || c.name || '.',
                  jsonb_build_object('convoy_id', c.id, 'player_id', v_uid));
  return 'accepted';
end $$;

create function kick_convoy_member(p_player uuid) returns void
language plpgsql security definer set search_path = public as $$
declare v_uid uuid := auth.uid(); c convoys;
begin
  if v_uid is null then raise exception 'not_authenticated'; end if;
  perform _rate_limit('convoy_write', 60, 60);
  select * into c from convoys where leader_id = v_uid and disbanded_at is null for update;
  if not found then raise exception 'not_leader'; end if;
  if p_player = v_uid then raise exception 'cannot_kick_leader'; end if;
  delete from convoy_members where convoy_id = c.id and player_id = p_player;
  if not found then raise exception 'not_a_member'; end if;
  update convoy_invites set status = 'revoked' where convoy_id = c.id and invitee_id = p_player and status in ('pending','accepted');
  perform _notify(p_player, 'convoy_event', 'Removed from convoy', 'You were removed from ' || c.name || '.', jsonb_build_object('convoy_id', c.id));
end $$;

create function transfer_convoy_leader(p_player uuid) returns void
language plpgsql security definer set search_path = public as $$
declare v_uid uuid := auth.uid(); c convoys;
begin
  if v_uid is null then raise exception 'not_authenticated'; end if;
  perform _rate_limit('convoy_write', 60, 60);
  select * into c from convoys where leader_id = v_uid and disbanded_at is null for update;
  if not found then raise exception 'not_leader'; end if;
  if p_player = v_uid or not exists (select 1 from convoy_members where convoy_id = c.id and player_id = p_player) then raise exception 'not_a_member'; end if;
  update convoys set leader_id = p_player where id = c.id;
end $$;

-- ---------------------------------------------------------------- lock-down
do $$ declare r record;
begin
  for r in select p.oid::regprocedure as sig, p.proname from pg_proc p
            where p.pronamespace = 'public'::regnamespace and p.oid not in (select oid from _fn_before) loop
    execute format('revoke execute on function %s from public, anon, authenticated', r.sig);
    if r.proname in ('find_player','get_public_profile','get_friend_status','send_friend_request','respond_friend_request','remove_friend',
                     'list_friends','list_friend_requests','list_recent_players','get_presence','get_my_convoy','list_open_convoys',
                     'set_convoy_options','invite_to_convoy','list_convoy_invites','respond_convoy_invite','kick_convoy_member',
                     'transfer_convoy_leader') then
      execute format('grant execute on function %s to authenticated', r.sig);
    end if;
  end loop;
end $$;
