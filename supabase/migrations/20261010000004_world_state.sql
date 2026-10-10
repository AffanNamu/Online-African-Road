-- Backend foundation, step 4: authoritative world state (cities, routes, unlock requirements).
-- Gameplay-critical state (is this road drivable? what level does it need?) lives HERE, never only in the client.
--   OPEN    the route can be driven (subject to the player's level)
--   PLANNED the route is on the roadmap; nothing can be done on it yet
--   LOCKED  the route exists but is closed (stored lock, or an OPEN route above the player's level: "effective" status)
create temp table _fn_before as select oid from pg_proc where pronamespace = 'public'::regnamespace;

create type world_status as enum ('OPEN','PLANNED','LOCKED');

alter table cities
  add column slug         text,
  add column region       text,
  add column status       world_status not null default 'PLANNED',
  add column unlock_level integer not null default 1 check (unlock_level >= 1),
  add column map_x        numeric,
  add column map_y        numeric;

-- Existing rows (the seeded Lagos / Ibadan corridor) keep working: they get a slug and, because jobs and locations
-- already exist there, are OPEN.
update cities set slug = trim(both '-' from regexp_replace(lower(name), '[^a-z0-9]+', '-', 'g')) where slug is null;
update cities set status = 'OPEN' where id in (select city_id from locations);
create function _cities_default_slug() returns trigger
language plpgsql security definer set search_path = public as $$
begin
  if new.slug is null then new.slug := trim(both '-' from regexp_replace(lower(new.name), '[^a-z0-9]+', '-', 'g')); end if;
  return new;
end $$;
-- Older seeds / tooling insert cities without a slug: derive it so they keep working.
create trigger cities_default_slug before insert on cities for each row execute function _cities_default_slug();
alter table cities alter column slug set not null;
alter table cities add constraint cities_slug_fmt check (slug ~ '^[a-z0-9-]{2,40}$');
create unique index cities_slug_key on cities(slug);

create table world_routes (
  id              uuid primary key default gen_random_uuid(),
  code            text not null unique check (code ~ '^[a-z0-9-]{3,60}$'),
  name            text not null,
  via             text[] not null check (array_length(via, 1) >= 2),   -- ordered city slugs; first = origin, last = destination
  from_city_id    uuid not null references cities(id),
  to_city_id      uuid not null references cities(id),
  region          text,
  distance_km     numeric check (distance_km is null or distance_km > 0),
  distance_source text not null default 'estimated' check (distance_source in ('road','estimated')),
  status          world_status not null default 'PLANNED',
  unlock_level    integer not null default 1 check (unlock_level >= 1),
  game_route_id   text,           -- id of the playable route spec (Resources/Routes/<id>.route.json), if one exists
  description     text,
  created_at      timestamptz not null default now(),
  check (from_city_id <> to_city_id)
);
create index world_routes_status_idx on world_routes(status);
create index world_routes_from_idx on world_routes(from_city_id);
create index world_routes_to_idx on world_routes(to_city_id);

alter table world_routes enable row level security;
revoke all on world_routes from anon, authenticated;

-- The one road that is playable today must exist for already-seeded databases; fresh databases get it from seed_world.sql.
insert into world_routes(code, name, via, from_city_id, to_city_id, region, distance_km, distance_source, status, unlock_level, game_route_id, description)
select 'lagos-ibadan', 'Lagos → Ibadan', array['lagos','ibadan'], a.id, b.id, 'South-West Nigeria', 128, 'road', 'OPEN', 1, 'ng-lagos-ibadan',
       'The first corridor of African Roads Online. The 3D environment running here is a prototype used to test jobs, bus routes and convoys.'
  from cities a, cities b where a.slug = 'lagos' and b.slug = 'ibadan'
on conflict (code) do nothing;

-- ---------------------------------------------------------------- effective status
create function _effective_status(p_stored world_status, p_unlock_level integer, p_level integer) returns text
language sql immutable as $$
  select case when p_stored = 'PLANNED' then 'PLANNED'
              when p_stored = 'LOCKED' then 'LOCKED'
              when p_level < p_unlock_level then 'LOCKED'
              else 'OPEN' end $$;

-- Is there an open, unlocked route connecting two cities for this player level? Same-city trips need no route.
-- Returns 'ok' | 'route_unavailable' (no route, or only PLANNED ones) | 'route_locked' (closed, or needs a higher level).
create function _route_access(p_city_a uuid, p_city_b uuid, p_level integer) returns text
language plpgsql stable security definer set search_path = public as $$
declare v_a text; v_b text; v_best text := 'route_unavailable'; r record;
begin
  if p_city_a = p_city_b then return 'ok'; end if;
  select slug into v_a from cities where id = p_city_a;
  select slug into v_b from cities where id = p_city_b;
  for r in select status, unlock_level from world_routes where via @> array[v_a, v_b] loop
    if r.status = 'OPEN' and p_level >= r.unlock_level then return 'ok'; end if;
    if r.status = 'LOCKED' or (r.status = 'OPEN' and p_level < r.unlock_level) then v_best := 'route_locked'; end if;
  end loop;
  return v_best;
end $$;

-- ---------------------------------------------------------------- API
create function get_world_state() returns jsonb
language plpgsql security definer set search_path = public as $$
declare v_uid uuid := auth.uid(); v_level integer; v_avg numeric := cfg('avg_speed_kmh'); v_cities jsonb; v_routes jsonb;
begin
  if v_uid is null then raise exception 'not_authenticated'; end if;
  perform _rate_limit('world_read', 60, 60);
  select level into v_level from profiles where id = v_uid;
  select coalesce(jsonb_agg(jsonb_build_object(
      'slug', c.slug, 'name', c.name, 'country_code', c.country_code, 'country', co.name, 'region', c.region,
      'lat', c.lat, 'lng', c.lng, 'map_x', c.map_x, 'map_y', c.map_y,
      'status', c.status, 'effective_status', _effective_status(c.status, c.unlock_level, v_level),
      'unlock_level', c.unlock_level) order by c.name), '[]'::jsonb) into v_cities
    from cities c join countries co on co.code = c.country_code;
  select coalesce(jsonb_agg(jsonb_build_object(
      'code', r.code, 'name', r.name, 'via', to_jsonb(r.via), 'region', r.region,
      'from', fa.slug, 'to', fb.slug,
      'distance_km', r.distance_km, 'distance_source', r.distance_source,
      'est_minutes', case when r.distance_km is null then null else round(r.distance_km / v_avg * 60)::int end,
      'status', r.status, 'effective_status', _effective_status(r.status, r.unlock_level, v_level),
      'unlock_level', r.unlock_level,
      'locked_reason', case when r.status = 'OPEN' and v_level < r.unlock_level then 'level'
                            when r.status = 'LOCKED' then 'closed' else null end,
      'game_route_id', r.game_route_id, 'description', r.description) order by r.code), '[]'::jsonb) into v_routes
    from world_routes r join cities fa on fa.id = r.from_city_id join cities fb on fb.id = r.to_city_id;
  return jsonb_build_object('player_level', v_level, 'cities', v_cities, 'routes', v_routes, 'server_time', now());
end $$;

-- Operator tooling (service role): change a route's stored status / required level.
create function admin_set_route(p_code text, p_status world_status, p_unlock_level integer default null) returns void
language plpgsql security definer set search_path = public as $$
begin
  update world_routes set status = p_status, unlock_level = coalesce(p_unlock_level, unlock_level) where code = p_code;
  if not found then raise exception 'route_not_found'; end if;
end $$;

-- ---------------------------------------------------------------- lock-down
do $$ declare r record;
begin
  for r in select p.oid::regprocedure as sig, p.proname from pg_proc p
            where p.pronamespace = 'public'::regnamespace and p.oid not in (select oid from _fn_before) loop
    execute format('revoke execute on function %s from public, anon, authenticated', r.sig);
    if r.proname in ('get_world_state') then
      execute format('grant execute on function %s to authenticated', r.sig);
    end if;
  end loop;
end $$;
