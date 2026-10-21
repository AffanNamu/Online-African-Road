-- Admin foundation: who may operate the game, how every action is recorded, and the first three admin tools (live health, player lookup, role management).
-- Nothing here changes any existing table, function or grant; it only adds.
--
--   * admin_users        who is an admin and in which role (owner, operations, support, analyst). Written ONLY by an owner through admin_set_role, or by the database operator
--                        with SQL for the very first owner (a person must be trusted before anyone can be trusted through the site). No client can read or write the table.
--   * admin_audit_log    append-only record of every admin read of personal data and every admin write: who, role, what, on whom, why, when. Updates, deletes and truncates are refused.
--   * every admin_* function checks, on the server and in this order: the caller is signed in; the caller is an active admin; the sign-in passed multi-factor authentication
--     (the JWT's aal claim is aal2); the role is allowed to call THIS function. A player who calls one gets 'not_admin' and nothing else. No client-side check is trusted.
--   * actions that change something, and views of one player's personal data, need a written reason (3 to 300 characters) which is stored in the audit log.
-- Denied attempts raise an error, which rolls the statement back, so they are not written to the audit log (they show in the API logs); the limit is stated in docs/ADMIN.md.
create temp table _fn_before as select oid from pg_proc where pronamespace = 'public'::regnamespace;

create type admin_role as enum ('owner', 'operations', 'support', 'analyst');

create table admin_users (
  user_id    uuid primary key references auth.users(id) on delete cascade,
  role       admin_role not null,
  active     boolean not null default true,
  note       text check (note is null or char_length(note) <= 200),
  created_at timestamptz not null default now(),
  created_by uuid,
  updated_at timestamptz not null default now()
);
alter table admin_users enable row level security;
revoke all on admin_users from anon, authenticated;

create table admin_audit_log (
  id         bigint generated always as identity primary key,
  at         timestamptz not null default now(),
  admin_id   uuid not null,                                   -- deliberately not a foreign key: the record must outlive the account
  admin_role admin_role not null,
  action     text not null check (char_length(action) between 1 and 60),
  target_type text check (target_type is null or char_length(target_type) <= 40),
  target_id  text check (target_id is null or char_length(target_id) <= 80),
  reason     text check (reason is null or char_length(reason) <= 300),
  details    jsonb not null default '{}'::jsonb
);
create index admin_audit_log_at_idx on admin_audit_log(at desc);
alter table admin_audit_log enable row level security;
revoke all on admin_audit_log from anon, authenticated;

create function _admin_audit_is_append_only() returns trigger language plpgsql as $$
begin raise exception 'audit_is_append_only'; end $$;
create trigger admin_audit_no_change before update or delete on admin_audit_log for each row execute function _admin_audit_is_append_only();
create trigger admin_audit_no_truncate before truncate on admin_audit_log for each statement execute function _admin_audit_is_append_only();

-- ---------------------------------------------------------------- guards and helpers (never granted to clients)
-- Returns the caller's role when every check passes; otherwise raises. p_roles = the roles allowed to call the function that asks.
create function _admin_actor(p_roles text[]) returns text
language plpgsql stable security definer set search_path = public as $$
declare v_role admin_role; v_aal text;
begin
  if auth.uid() is null then raise exception 'not_authenticated'; end if;
  select a.role into v_role from admin_users a where a.user_id = auth.uid() and a.active;
  if v_role is null then raise exception 'not_admin'; end if;
  v_aal := coalesce(nullif(current_setting('request.jwt.claims', true), '')::jsonb ->> 'aal', nullif(current_setting('request.jwt.claim.aal', true), ''), 'aal1');
  if v_aal <> 'aal2' then raise exception 'mfa_required'; end if;
  if not (v_role::text = any (p_roles)) then raise exception 'not_allowed'; end if;
  return v_role::text;
end $$;

create function _admin_reason(p_reason text) returns text language plpgsql immutable as $$
declare r text := btrim(coalesce(p_reason, ''));
begin
  if char_length(r) < 3 or char_length(r) > 300 then raise exception 'reason_required'; end if;
  return r;
end $$;

create function _admin_log(p_action text, p_target_type text, p_target_id text, p_reason text, p_details jsonb) returns void
language plpgsql security definer set search_path = public as $$
declare v_role admin_role;
begin
  select role into v_role from admin_users where user_id = auth.uid();
  insert into admin_audit_log(admin_id, admin_role, action, target_type, target_id, reason, details)
  values (auth.uid(), v_role, p_action, p_target_type, p_target_id, p_reason, coalesce(p_details, '{}'::jsonb));
end $$;

create function _mask_email(e text) returns text language sql immutable as $$
  select case when e is null or position('@' in e) < 2 then null else left(e, 1) || '***' || substr(e, position('@' in e)) end $$;

-- ---------------------------------------------------------------- who am I (the site calls this first; it never raises for a non-admin)
create function admin_whoami() returns jsonb
language plpgsql security definer set search_path = public as $$
declare v_role admin_role; v_aal text;
begin
  if auth.uid() is null then raise exception 'not_authenticated'; end if;
  perform _rate_limit('admin_read', 240, 60);
  select a.role into v_role from admin_users a where a.user_id = auth.uid() and a.active;
  v_aal := coalesce(nullif(current_setting('request.jwt.claims', true), '')::jsonb ->> 'aal', nullif(current_setting('request.jwt.claim.aal', true), ''), 'aal1');
  return jsonb_build_object('is_admin', v_role is not null, 'role', v_role, 'aal', v_aal, 'mfa_ok', v_aal = 'aal2', 'server_time', now());
end $$;

-- ---------------------------------------------------------------- live health (aggregates only: no personal data, so reads are not audited)
create function admin_health() returns jsonb
language plpgsql security definer set search_path = public as $$
declare
  v_open int; v_soon int; v_new24 int; v_last timestamptz; v_players int; v_new_players int; v_online int; v_by_activity jsonb;
  v_ttl interval := make_interval(secs => coalesce(cfg('presence_ttl_seconds'), 90)); v_mig_n int; v_mig_last text; v_alerts text[] := '{}';
begin
  if auth.uid() is null then raise exception 'not_authenticated'; end if;
  perform _rate_limit('admin_read', 240, 60);
  perform _admin_actor(array['owner', 'operations', 'support', 'analyst']);
  select count(*) filter (where status = 'open' and expires_at > now()),
         count(*) filter (where status = 'open' and expires_at > now() and expires_at <= now() + interval '6 hours'),
         count(*) filter (where created_at > now() - interval '24 hours'), max(created_at)
    into v_open, v_soon, v_new24, v_last from jobs;
  select count(*), count(*) filter (where created_at > now() - interval '24 hours') into v_players, v_new_players from profiles;
  select count(distinct player_id) into v_online from game_sessions where ended_at is null and last_seen_at > now() - v_ttl;
  select coalesce(jsonb_object_agg(activity, n), '{}'::jsonb) into v_by_activity
    from (select activity, count(distinct player_id) n from game_sessions where ended_at is null and last_seen_at > now() - v_ttl group by activity) x;
  if to_regclass('public._aro_migrations') is not null then execute 'select count(*)::int, max(name) from public._aro_migrations' into v_mig_n, v_mig_last; end if;
  if v_open = 0 then v_alerts := array_append(v_alerts, 'jobs_empty'); elsif v_open < 10 then v_alerts := array_append(v_alerts, 'jobs_low'); end if;
  if v_last is null or v_last < now() - interval '12 hours' then v_alerts := array_append(v_alerts, 'jobs_not_generated_for_12h'); end if;
  return jsonb_build_object(
    'server_time', now(), 'alerts', to_jsonb(v_alerts),
    'jobs', jsonb_build_object('open', v_open, 'expiring_within_6h', v_soon, 'created_last_24h', v_new24, 'last_created_at', v_last),
    'players', jsonb_build_object('total', v_players, 'new_last_24h', v_new_players, 'online_now', v_online, 'online_by_activity', v_by_activity),
    'schema', jsonb_build_object('migrations_applied', v_mig_n, 'last_migration', v_mig_last));
end $$;

-- ---------------------------------------------------------------- generate jobs now (the site's "top up" button; the scheduled top-up does the same on a timer)
create function admin_generate_jobs(p_count integer, p_reason text) returns jsonb
language plpgsql security definer set search_path = public as $$
declare v_reason text; v_n int := least(greatest(coalesce(p_count, 0), 1), 40); v_made int; v_open int;
begin
  if auth.uid() is null then raise exception 'not_authenticated'; end if;
  perform _admin_actor(array['owner', 'operations']);
  v_reason := _admin_reason(p_reason);
  perform _rate_limit('admin_write', 30, 60);
  v_made := generate_jobs(v_n);
  select count(*) into v_open from jobs where status = 'open' and expires_at > now();
  perform _admin_log('generate_jobs', 'jobs', null, v_reason, jsonb_build_object('asked', p_count, 'clamped_to', v_n, 'made', v_made, 'open_after', v_open));
  return jsonb_build_object('made', v_made, 'open', v_open);
end $$;

-- ---------------------------------------------------------------- find players (username or display-name prefix, exact player id, or exact e-mail for owner and support)
create function admin_find_players(p_query text, p_limit integer default 20) returns jsonb
language plpgsql security definer set search_path = public as $$
declare
  v_role text := _admin_actor(array['owner', 'support', 'operations']);
  v_q text := btrim(coalesce(p_query, '')); v_lim int := least(greatest(coalesce(p_limit, 20), 1), 50); v_kind text; v_items jsonb;
  v_ttl interval := make_interval(secs => coalesce(cfg('presence_ttl_seconds'), 90)); v_like text;
begin
  if auth.uid() is null then raise exception 'not_authenticated'; end if;
  perform _rate_limit('admin_find', 120, 60);
  if char_length(v_q) < 2 or char_length(v_q) > 64 then raise exception 'query_too_short'; end if;
  if v_q ~* '^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$' then v_kind := 'id';
  elsif position('@' in v_q) > 0 then
    if v_role not in ('owner', 'support') then raise exception 'not_allowed'; end if;
    v_kind := 'email';
  else v_kind := 'name'; end if;
  v_like := replace(replace(replace(lower(v_q), '\', '\\'), '%', '\%'), '_', '\_') || '%';
  select coalesce(jsonb_agg(row order by (row ->> 'display_name')), '[]'::jsonb) into v_items from (
    select jsonb_build_object('id', p.id, 'username', p.username, 'display_name', p.display_name, 'level', p.level, 'country_code', p.country_code,
             'created_at', p.created_at, 'last_seen_at', p.last_seen_at,
             'online', exists (select 1 from game_sessions s where s.player_id = p.id and s.ended_at is null and s.last_seen_at > now() - v_ttl)) as row
      from profiles p
     where (v_kind = 'id' and p.id = v_q::uuid)
        or (v_kind = 'email' and p.id in (select u.id from auth.users u where lower(u.email) = lower(v_q)))
        or (v_kind = 'name' and (lower(p.username) like v_like or lower(p.display_name) like v_like))
     limit v_lim) x;
  perform _admin_log('find_players', 'players', null, null, jsonb_build_object('kind', v_kind, 'results', jsonb_array_length(v_items)));
  return jsonb_build_object('items', v_items, 'kind', v_kind);
end $$;

-- ---------------------------------------------------------------- one player's record (read-only; needs a reason; audited)
create function admin_get_player(p_id uuid, p_reason text) returns jsonb
language plpgsql security definer set search_path = public as $$
declare
  v_reason text; p profiles; v_email text; v_balance bigint; v_ledger jsonb; v_vehicles jsonb; v_jobs jsonb; v_sessions jsonb; v_friends int; v_blocked_by int; v_blocking int; v_online boolean;
  v_ttl interval := make_interval(secs => coalesce(cfg('presence_ttl_seconds'), 90));
begin
  if auth.uid() is null then raise exception 'not_authenticated'; end if;
  perform _admin_actor(array['owner', 'support']);
  v_reason := _admin_reason(p_reason);
  perform _rate_limit('admin_view', 60, 60);
  select * into p from profiles where id = p_id;
  if not found then raise exception 'player_not_found'; end if;
  select _mask_email(email) into v_email from auth.users where id = p_id;
  select balance into v_balance from player_wallets where player_id = p_id;
  select coalesce(jsonb_agg(jsonb_build_object('id', t.id, 'kind', t.kind, 'amount', t.amount, 'balance_after', t.balance_after, 'reason', t.reason, 'created_at', t.created_at) order by t.id desc), '[]'::jsonb)
    into v_ledger from (select * from transactions where player_id = p_id order by id desc limit 30) t;
  select coalesce(jsonb_agg(jsonb_build_object('id', v.id, 'definition_id', v.definition_id, 'nickname', v.nickname, 'fuel_l', v.fuel_l, 'damage_pct', v.damage_pct, 'odometer_km', v.odometer_km, 'acquired_at', v.acquired_at) order by v.acquired_at), '[]'::jsonb)
    into v_vehicles from vehicle_ownership v where v.player_id = p_id;
  select coalesce(jsonb_agg(jsonb_build_object('assignment_id', a.id, 'job_code', j.code, 'status', a.status, 'reward', j.reward, 'accepted_at', a.accepted_at, 'completed_at', a.completed_at, 'reported_distance_km', a.reported_distance_km) order by a.accepted_at desc), '[]'::jsonb)
    into v_jobs from (select * from job_assignments where player_id = p_id order by accepted_at desc limit 15) a join jobs j on j.id = a.job_id;
  select coalesce(jsonb_agg(jsonb_build_object('id', s.id, 'client', s.client, 'activity', s.activity, 'started_at', s.started_at, 'last_seen_at', s.last_seen_at, 'ended_at', s.ended_at, 'end_reason', s.end_reason) order by s.started_at desc), '[]'::jsonb)
    into v_sessions from (select * from game_sessions where player_id = p_id order by started_at desc limit 10) s;
  select count(*) into v_friends from friendships where status = 'accepted' and p_id in (requester_id, addressee_id);
  select count(*) into v_blocked_by from player_blocks where blocked_id = p_id;
  select count(*) into v_blocking from player_blocks where blocker_id = p_id;
  select exists (select 1 from game_sessions s where s.player_id = p_id and s.ended_at is null and s.last_seen_at > now() - v_ttl) into v_online;
  perform _admin_log('view_player', 'player', p_id::text, v_reason, '{}'::jsonb);
  return jsonb_build_object(
    'profile', jsonb_build_object('id', p.id, 'username', p.username, 'display_name', p.display_name, 'level', p.level, 'experience', p.experience, 'distance_km', p.distance_km,
        'jobs_completed', p.jobs_completed, 'lifetime_earnings', p.lifetime_earnings, 'country_code', p.country_code, 'language_code', p.language_code, 'play_style', p.play_style,
        'created_at', p.created_at, 'last_seen_at', p.last_seen_at, 'onboarded_at', p.onboarded_at, 'email_masked', v_email, 'online', v_online),
    'wallet', jsonb_build_object('balance', coalesce(v_balance, 0)), 'ledger', v_ledger, 'vehicles', v_vehicles, 'jobs', v_jobs, 'sessions', v_sessions,
    'social', jsonb_build_object('friends', v_friends, 'blocked_by', v_blocked_by, 'blocking', v_blocking), 'server_time', now());
end $$;

-- ---------------------------------------------------------------- audit log (owner and analyst can read it; nobody can change it)
create function admin_list_audit(p_limit integer default 50, p_before bigint default null) returns jsonb
language plpgsql security definer set search_path = public as $$
declare v_lim int := least(greatest(coalesce(p_limit, 50), 1), 200); v_items jsonb;
begin
  if auth.uid() is null then raise exception 'not_authenticated'; end if;
  perform _rate_limit('admin_read', 240, 60);
  perform _admin_actor(array['owner', 'analyst']);
  select coalesce(jsonb_agg(jsonb_build_object('id', l.id, 'at', l.at, 'admin_id', l.admin_id, 'admin_name', pr.display_name, 'role', l.admin_role, 'action', l.action,
           'target_type', l.target_type, 'target_id', l.target_id, 'reason', l.reason, 'details', l.details) order by l.id desc), '[]'::jsonb) into v_items
    from (select * from admin_audit_log where p_before is null or id < p_before order by id desc limit v_lim) l left join profiles pr on pr.id = l.admin_id;
  return jsonb_build_object('items', v_items);
end $$;

-- ---------------------------------------------------------------- roles (owner only)
create function admin_list_admins() returns jsonb
language plpgsql security definer set search_path = public as $$
declare v_items jsonb;
begin
  if auth.uid() is null then raise exception 'not_authenticated'; end if;
  perform _rate_limit('admin_read', 240, 60);
  perform _admin_actor(array['owner']);
  select coalesce(jsonb_agg(jsonb_build_object('user_id', a.user_id, 'display_name', p.display_name, 'username', p.username, 'role', a.role, 'active', a.active, 'note', a.note,
           'created_at', a.created_at, 'updated_at', a.updated_at) order by a.created_at), '[]'::jsonb) into v_items from admin_users a left join profiles p on p.id = a.user_id;
  return jsonb_build_object('items', v_items);
end $$;

-- p_role is one of owner, operations, support, analyst, or 'none' (take admin access away). The last active owner can never be removed or demoted.
create function admin_set_role(p_target uuid, p_role text, p_reason text) returns jsonb
language plpgsql security definer set search_path = public as $$
declare v_reason text; v_old admin_users; v_new_role admin_role; v_active boolean; v_owners int;
begin
  if auth.uid() is null then raise exception 'not_authenticated'; end if;
  perform _admin_actor(array['owner']);
  v_reason := _admin_reason(p_reason);
  perform _rate_limit('admin_write', 30, 60);
  if p_role is null or p_role not in ('owner', 'operations', 'support', 'analyst', 'none') then raise exception 'invalid_role'; end if;
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

-- ---------------------------------------------------------------- lock-down (same convention as every migration)
do $$ declare r record;
begin
  for r in select p.oid::regprocedure as sig, p.proname from pg_proc p
            where p.pronamespace = 'public'::regnamespace and p.oid not in (select oid from _fn_before) loop
    execute format('revoke execute on function %s from public, anon, authenticated', r.sig);
    if r.proname in ('admin_whoami', 'admin_health', 'admin_generate_jobs', 'admin_find_players', 'admin_get_player', 'admin_list_audit', 'admin_list_admins', 'admin_set_role') then
      execute format('grant execute on function %s to authenticated', r.sig);
    end if;
  end loop;
end $$;
