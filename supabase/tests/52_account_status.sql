\set ON_ERROR_STOP on
-- Immediate ban enforcement (migration 20261023000001). PostgREST runs enforce_account_status() before every request; here it is called the way PostgREST calls it:
-- as the request role, with the token's claims and the request path set. tools/admin_e2e.js proves the same thing over real HTTP with the real PostgREST setting.
create temp table fx(k text primary key, v text);
insert into fx select k, testkit.make_user(n)::text from (values ('OWN','AsOwner'),('SUP','AsSupport'),('P','AsPlayer'),('M','AsMuted'),('S','AsSuspended'),('B','AsBanned'),('X','AsExpired'),('A','AsAdminSanctioned')) t(k, n);
create function pg_temp.u(k text) returns uuid language sql as $$ select v::uuid from fx where fx.k = $1 $$;
insert into admin_users(user_id, role) values (pg_temp.u('OWN'), 'owner'), (pg_temp.u('SUP'), 'support'), (pg_temp.u('A'), 'support');

-- the hook as PostgREST runs it: role, claims, path (p_path null = PostgREST did not provide one)
create function pg_temp.hook(p_k text, p_path text) returns text language plpgsql as $$
begin
  perform set_config('request.jwt.claim.sub', coalesce(pg_temp.u(p_k)::text, ''), true);
  perform set_config('request.jwt.claim.aal', 'aal2', true);
  perform set_config('request.path', coalesce(p_path, ''), true);
  execute 'set local role authenticated';
  begin perform enforce_account_status(); exception when others then execute 'reset role'; return sqlerrm; end;
  execute 'reset role';
  return 'ok';
end $$;
create function pg_temp.apply(p_by text, p_k text, p_kind text, p_hours int) returns void language plpgsql as $$
declare v_target uuid := pg_temp.u(p_k);
begin
  perform set_config('request.jwt.claim.sub', pg_temp.u(p_by)::text, true); perform set_config('request.jwt.claim.aal', 'aal2', true);
  execute 'set local role authenticated';
  perform admin_apply_sanction(v_target, p_kind, p_hours, 'account status test');
  execute 'reset role';
end $$;

-- ===== 1. nobody restricted: everything passes
select testkit.assert_eq('a normal player passes on a game path', pg_temp.hook('P', '/rpc/get_dashboard_summary'), 'ok');
select testkit.assert_eq('... and on a table path', pg_temp.hook('P', '/profiles'), 'ok');
do $$ begin
  perform set_config('request.jwt.claim.sub', '', true); perform set_config('request.path', '/rpc/get_dashboard_summary', true);
  set local role authenticated; perform enforce_account_status(); reset role;
end $$;

-- ===== 2. a mute never blocks the API (it only stops chat)
select pg_temp.apply('SUP', 'M', 'mute', 2);
select testkit.assert_eq('a muted player passes on a game path', pg_temp.hook('M', '/rpc/get_dashboard_summary'), 'ok');
select testkit.assert_eq('... and on the join-convoy path', pg_temp.hook('M', '/rpc/join_convoy'), 'ok');

-- ===== 3. a suspension blocks every request except the two exemptions
select pg_temp.apply('SUP', 'S', 'suspend', 24);
select testkit.assert_eq('a suspended player is refused on a game path', pg_temp.hook('S', '/rpc/get_dashboard_summary'), 'account_suspended');
select testkit.assert_eq('... on the session-start path', pg_temp.hook('S', '/rpc/start_session'), 'account_suspended');
select testkit.assert_eq('... on the heartbeat path (an existing session)', pg_temp.hook('S', '/rpc/heartbeat'), 'account_suspended');
select testkit.assert_eq('... on the chat path', pg_temp.hook('S', '/rpc/post_convoy_message'), 'account_suspended');
select testkit.assert_eq('... on the job-accept path', pg_temp.hook('S', '/rpc/accept_job'), 'account_suspended');
select testkit.assert_eq('... on the wallet path', pg_temp.hook('S', '/rpc/get_wallet'), 'account_suspended');
select testkit.assert_eq('... on a direct table read', pg_temp.hook('S', '/profiles'), 'account_suspended');
select testkit.assert_eq('... on a path that merely starts like an exemption', pg_temp.hook('S', '/rpc/get_my_restrictions_extra'), 'account_suspended');
select testkit.assert_eq('... on a path that merely contains admin', pg_temp.hook('S', '/rpc/not_admin_at_all'), 'account_suspended');
select testkit.assert_eq('... and when PostgREST gives no path at all (fail closed)', pg_temp.hook('S', null), 'account_suspended');
select testkit.assert_eq('the player can still ask what is in force', pg_temp.hook('S', '/rpc/get_my_restrictions'), 'ok');
select testkit.assert_eq('the admin endpoints are not blocked by the hook (they do their own role and multi-factor checks)', pg_temp.hook('S', '/rpc/admin_whoami'), 'ok');
select testkit.assert_eq('... and a restricted player still gets nothing from them', (select (testkit.as_user(pg_temp.u('S'), $$select admin_whoami()::text$$)::jsonb)->>'is_admin'), 'false');
select testkit.assert_eq('another player is unaffected', pg_temp.hook('P', '/rpc/get_dashboard_summary'), 'ok');

-- ===== 4. a ban blocks too; lifting restores at once
select pg_temp.apply('OWN', 'B', 'ban', null);
select testkit.assert_eq('a banned player is refused', pg_temp.hook('B', '/rpc/get_dashboard_summary'), 'account_suspended');
select testkit.assert_eq('... including the chat path', pg_temp.hook('B', '/rpc/post_convoy_message'), 'account_suspended');
do $$ begin
  perform set_config('request.jwt.claim.sub', pg_temp.u('B')::text, true); perform set_config('request.path', '/rpc/get_dashboard_summary', true);
  set local role authenticated;
  begin perform enforce_account_status(); reset role; raise exception 'NOT REJECTED'; exception when others then reset role; if sqlerrm <> 'account_suspended' then raise; end if; end;
end $$;
select set_config('request.jwt.claim.sub', pg_temp.u('OWN')::text, true);
do $$ declare v_id uuid; begin
  select id into v_id from player_sanctions where player_id = pg_temp.u('B') and kind = 'ban' and lifted_at is null;
  perform set_config('request.jwt.claim.sub', pg_temp.u('OWN')::text, true); perform set_config('request.jwt.claim.aal', 'aal2', true);
  set local role authenticated; perform admin_lift_sanction(v_id, 'appeal accepted'); reset role;
end $$;
select testkit.assert_eq('lifting the ban restores the account immediately', pg_temp.hook('B', '/rpc/get_dashboard_summary'), 'ok');

-- ===== 5. an ended suspension needs no lifting
select pg_temp.apply('SUP', 'X', 'suspend', 1);
select testkit.assert_eq('a running suspension blocks', pg_temp.hook('X', '/rpc/get_dashboard_summary'), 'account_suspended');
update player_sanctions set created_at = now() - interval '3 hours', expires_at = now() - interval '1 hour' where player_id = pg_temp.u('X');
select testkit.assert_eq('an ended suspension does not', pg_temp.hook('X', '/rpc/get_dashboard_summary'), 'ok');

-- ===== 6. administrative recovery: an admin restricted by SQL can still reach the admin endpoints (and only those)
insert into player_sanctions(player_id, kind, reason, created_by, expires_at) values (pg_temp.u('A'), 'suspend', 'set by the operator in SQL', pg_temp.u('OWN'), now() + interval '1 day');
select testkit.assert_eq('a restricted admin passes on the admin path', pg_temp.hook('A', '/rpc/admin_list_sanctions'), 'ok');
select testkit.assert_eq('... and on the sanctions-lifting path', pg_temp.hook('A', '/rpc/admin_lift_sanction'), 'ok');
select testkit.assert_eq('... but not on the game paths', pg_temp.hook('A', '/rpc/get_dashboard_summary'), 'account_suspended');
do $$ begin
  perform set_config('request.jwt.claim.sub', pg_temp.u('A')::text, true); perform set_config('request.jwt.claim.aal', 'aal2', true);
  set local role authenticated; perform admin_list_sanctions('active'); reset role;
end $$;

-- ===== 7. get_my_restrictions
select testkit.assert_eq('a player sees their own mute', (testkit.as_user(pg_temp.u('M'), $$select get_my_restrictions()::text$$)::jsonb)->'items'->0->>'kind', 'mute');
select testkit.assert_eq('... with an end time', ((testkit.as_user(pg_temp.u('M'), $$select get_my_restrictions()::text$$)::jsonb)->'items'->0->>'expires_at' is not null)::text, 'true');
select testkit.assert_eq('... and never the moderator''s reason', ((testkit.as_user(pg_temp.u('M'), $$select get_my_restrictions()::text$$)::jsonb)->'items'->0 ? 'reason')::text, 'false');
select testkit.assert_eq('a clean player sees nothing', jsonb_array_length((testkit.as_user(pg_temp.u('P'), $$select get_my_restrictions()::text$$)::jsonb)->'items')::text, '0');
select testkit.assert_eq('an ended restriction is not listed', jsonb_array_length((testkit.as_user(pg_temp.u('X'), $$select get_my_restrictions()::text$$)::jsonb)->'items')::text, '0');
select testkit.assert_eq('a lifted restriction is not listed', jsonb_array_length((testkit.as_user(pg_temp.u('B'), $$select get_my_restrictions()::text$$)::jsonb)->'items')::text, '0');
select testkit.assert_eq('a suspended player sees the suspension', (testkit.as_user(pg_temp.u('S'), $$select get_my_restrictions()::text$$)::jsonb)->'items'->0->>'kind', 'suspend');
select testkit.expect_error('anon', null, 'select get_my_restrictions()', 'permission denied');
select testkit.expect_error('authenticated', null, 'select get_my_restrictions()', 'not_authenticated');
select testkit.expect_error('anon', null, 'select enforce_account_status()', 'permission denied');

do $$ begin
  perform set_config('request.jwt.claim.sub', pg_temp.u('P')::text, true);
  set local role authenticated;
  for i in 1..65 loop perform get_my_restrictions(); end loop;
  reset role; raise exception 'NOT LIMITED: 65 calls in a minute were all accepted';
exception when others then reset role; if sqlerrm not like '%rate_limited%' then raise; end if;
end $$;

-- ===== 8. PostgREST is pointed at the hook
select testkit.assert_eq('the authenticator role is configured to run the hook before every request', (
  select count(*)::text from pg_db_role_setting s join pg_roles r on r.oid = s.setrole
   where r.rolname = 'authenticator' and 'pgrst.db_pre_request=public.enforce_account_status' = any (s.setconfig)), '1');

select 'ACCOUNT STATUS ENFORCEMENT TESTS PASSED' as result;
