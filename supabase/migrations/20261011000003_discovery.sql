-- Discovery: players near the caller's route or city, and the server list.
-- Positions never reach the database, so "nearby" means "same route or same city in the player's own live presence", and the list says which.
-- A player can opt out of nearby lists (set_nearby_visibility). Server numbers come from live sessions; capacity and region are shown only when an operator set them.
create temp table _fn_before as select oid from pg_proc where pronamespace = 'public'::regnamespace;

alter table profiles add column appear_nearby boolean not null default true;

create function set_nearby_visibility(p_visible boolean) returns boolean
language plpgsql security definer set search_path = public as $$
begin
  if auth.uid() is null then raise exception 'not_authenticated'; end if;
  perform _rate_limit('profile_write', 30, 60);
  update profiles set appear_nearby = coalesce(p_visible, true) where id = auth.uid();
  return coalesce(p_visible, true);
end $$;

create function list_nearby_players(p_limit integer default 10) returns jsonb
language plpgsql security definer set search_path = public as $$
declare v_uid uuid := auth.uid(); s game_sessions; v_scope text := 'none'; v_items jsonb; v_ttl integer := cfg('presence_ttl_seconds')::int;
begin
  if v_uid is null then raise exception 'not_authenticated'; end if;
  perform _rate_limit('players_read', 60, 60);
  select * into s from game_sessions where player_id = v_uid and ended_at is null and last_seen_at > now() - make_interval(secs => v_ttl) order by last_seen_at desc limit 1;
  if found then v_scope := case when s.route_code is not null then 'route' when s.city_slug is not null then 'city' else 'none' end; end if;
  if v_scope = 'none' then
    return jsonb_build_object('scope', 'none', 'visible', (select appear_nearby from profiles where id = v_uid), 'items', '[]'::jsonb, 'server_time', now());
  end if;
  select coalesce(jsonb_agg(jsonb_build_object(
           'player_id', p.id, 'display_name', p.display_name, 'username', p.username, 'avatar_id', p.avatar_id, 'level', p.level,
           'vehicle', _member_vehicle(p.id), 'friend_status', _friend_status(v_uid, p.id), 'online', true,
           'convoy', (select jsonb_build_object('id', c.id, 'name', c.name, 'member_count', (select count(*) from convoy_members where convoy_id = c.id), 'capacity', c.capacity, 'phase', c.phase,
                        'has_password', c.password_hash is not null, 'min_level', c.min_level,
                        'joinable', (select count(*) from convoy_members where convoy_id = c.id) < c.capacity and (select level from profiles where id = v_uid) >= c.min_level)
                       from convoy_members m join convoys c on c.id = m.convoy_id where m.player_id = p.id and c.disbanded_at is null and not c.is_private)
         ) order by p.level desc, p.display_name), '[]'::jsonb)
    into v_items
    from (select * from profiles q
           where q.id <> v_uid and q.appear_nearby
             and not exists (select 1 from player_blocks b where b.blocker_id = v_uid and b.blocked_id = q.id)
             and exists (select 1 from game_sessions o where o.player_id = q.id and o.ended_at is null and o.last_seen_at > now() - make_interval(secs => v_ttl)
                           and case v_scope when 'route' then o.route_code = s.route_code else o.city_slug = s.city_slug end)
           order by q.level desc, q.display_name
           limit least(greatest(coalesce(p_limit, 10), 1), 30)) p;
  return jsonb_build_object('scope', v_scope, 'scope_name', case v_scope when 'route' then s.route_code else s.city_slug end,
                            'visible', (select appear_nearby from profiles where id = v_uid), 'items', v_items, 'server_time', now());
end $$;

-- ---------------------------------------------------------------- servers
create table game_servers (
  id       text primary key check (id ~ '^[a-z0-9-]{2,24}$'),
  name     text not null check (char_length(name) between 2 and 40),
  region   text check (region is null or char_length(region) between 2 and 40),
  capacity integer check (capacity is null or capacity > 0),
  status   text not null default 'ONLINE' check (status in ('ONLINE','MAINTENANCE')),
  created_at timestamptz not null default now()
);
alter table game_servers enable row level security;
revoke all on game_servers from anon, authenticated;
insert into game_servers(id, name) values ('main', 'African Roads Online') on conflict do nothing;

-- Population is the number of live sessions (there is one game backend, so every session belongs to it). FULL needs a capacity an operator configured.
-- UNAVAILABLE is never stored: it is what the client shows when it cannot reach this endpoint at all.
create function list_servers() returns jsonb
language plpgsql security definer set search_path = public as $$
declare v_pop integer := get_online_count();
begin
  if auth.uid() is null then raise exception 'not_authenticated'; end if;
  perform _rate_limit('players_read', 60, 60);
  return jsonb_build_object('items', coalesce((select jsonb_agg(jsonb_build_object('id', g.id, 'name', g.name, 'region', g.region, 'capacity', g.capacity, 'population', v_pop,
            'status', case when g.status = 'MAINTENANCE' then 'MAINTENANCE' when g.capacity is not null and v_pop >= g.capacity then 'FULL' else 'ONLINE' end) order by g.name) from game_servers g), '[]'::jsonb),
    'server_time', now());
end $$;

create function admin_set_server(p_id text, p_status text default null, p_capacity integer default null, p_region text default null) returns void
language plpgsql security definer set search_path = public as $$
begin
  update game_servers set status = coalesce(p_status, status), capacity = coalesce(p_capacity, capacity), region = coalesce(p_region, region) where id = p_id;
  if not found then raise exception 'server_not_found'; end if;
end $$;

-- ---------------------------------------------------------------- lock-down
do $$ declare r record;
begin
  for r in select p.oid::regprocedure as sig, p.proname from pg_proc p
            where p.pronamespace = 'public'::regnamespace and p.oid not in (select oid from _fn_before) loop
    execute format('revoke execute on function %s from public, anon, authenticated', r.sig);
    if r.proname in ('set_nearby_visibility','list_nearby_players','list_servers') then
      execute format('grant execute on function %s to authenticated', r.sig);
    end if;
  end loop;
end $$;
