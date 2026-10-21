\set ON_ERROR_STOP on
-- Admin foundation (migration 20261021000001): who may call what, multi-factor sign-in, reasons, the audit log, role management and the three tools (health, find, view).
create temp table fx(k text primary key, v text);
insert into fx select k, testkit.make_user(n)::text from (values
  ('OWN','AdmOwner'),('OWN2','AdmOwner2'),('OPS','AdmOps'),('SUP','AdmSupport'),('ANA','AdmAnalyst'),('OFF','AdmInactive'),('P','AdmPlayer'),('T','AdmTarget')) t(k, n);
create function pg_temp.u(k text) returns uuid language sql as $$ select v::uuid from fx where fx.k = $1 $$;
-- the database operator makes the first owner (and, here, the other admins) with SQL: there is deliberately no client path to that
insert into admin_users(user_id, role) values (pg_temp.u('OWN'), 'owner'), (pg_temp.u('OPS'), 'operations'), (pg_temp.u('SUP'), 'support'), (pg_temp.u('ANA'), 'analyst');
insert into admin_users(user_id, role, active) values (pg_temp.u('OFF'), 'owner', false);

-- run p_sql as a signed-in user whose token has the given authenticator assurance level (null = no aal claim at all)
create function pg_temp.as(p_k text, p_aal text, p_sql text) returns text language plpgsql as $$
declare r text;
begin
  perform set_config('request.jwt.claim.sub', pg_temp.u(p_k)::text, true);
  perform set_config('request.jwt.claim.aal', coalesce(p_aal, ''), true);
  execute 'set local role authenticated';
  execute p_sql into r;
  execute 'reset role';
  return r;
exception when others then execute 'reset role'; raise;
end $$;
create function pg_temp.ok(p_k text, p_sql text) returns text language sql as $$ select pg_temp.as($1, 'aal2', $2) $$;
create function pg_temp.denied(p_k text, p_aal text, p_sql text, p_msg text) returns void language plpgsql as $$
begin
  begin perform pg_temp.as(p_k, p_aal, p_sql);
  exception when others then
    if sqlerrm like '%' || p_msg || '%' then return; end if;
    raise exception 'WRONG ERROR for [%] as % (aal %): expected "%", got "%"', p_sql, p_k, p_aal, p_msg, sqlerrm;
  end;
  raise exception 'NOT REJECTED: [%] as % (aal %) should have failed with "%"', p_sql, p_k, p_aal, p_msg;
end $$;
create function pg_temp.j(p_k text, p_sql text) returns jsonb language sql as $$ select pg_temp.as($1, 'aal2', $2)::jsonb $$;

-- ===== 1. a player (not an admin) gets nothing from any admin function
select testkit.assert_eq('whoami: a player is told they are not an admin', pg_temp.j('P', $$select admin_whoami()::text$$)->>'is_admin', 'false');
select testkit.assert_eq('whoami: and has no role', coalesce(pg_temp.j('P', $$select admin_whoami()::text$$)->>'role', 'null'), 'null');
select pg_temp.denied('P', 'aal2', $$select admin_health()::text$$, 'not_admin');
select pg_temp.denied('P', 'aal2', $$select admin_generate_jobs(5, 'testing it')::text$$, 'not_admin');
select pg_temp.denied('P', 'aal2', $$select admin_find_players('Adm')::text$$, 'not_admin');
select pg_temp.denied('P', 'aal2', format($$select admin_get_player(%L, 'testing it')::text$$, pg_temp.u('T')), 'not_admin');
select pg_temp.denied('P', 'aal2', $$select admin_list_audit()::text$$, 'not_admin');
select pg_temp.denied('P', 'aal2', $$select admin_list_admins()::text$$, 'not_admin');
select pg_temp.denied('P', 'aal2', format($$select admin_set_role(%L, 'owner', 'make me owner')::text$$, pg_temp.u('P')), 'not_admin');
-- an account that was an admin and has been switched off is a player again
select pg_temp.denied('OFF', 'aal2', $$select admin_health()::text$$, 'not_admin');
select testkit.assert_eq('whoami: a deactivated admin is not an admin', pg_temp.j('OFF', $$select admin_whoami()::text$$)->>'is_admin', 'false');
-- signed-out and anonymous callers
select testkit.expect_error('anon', null, 'select admin_health()', 'permission denied');
select testkit.expect_error('anon', null, 'select admin_whoami()', 'permission denied');
select testkit.expect_error('authenticated', null, 'select admin_health()', 'not_authenticated');

-- ===== 2. multi-factor sign-in is required by every admin function (whoami still answers, so the site knows to ask for the code)
select pg_temp.denied('OWN', 'aal1', $$select admin_health()::text$$, 'mfa_required');
select pg_temp.denied('OWN', null, $$select admin_health()::text$$, 'mfa_required');
select pg_temp.denied('OWN', 'aal1', $$select admin_list_admins()::text$$, 'mfa_required');
select pg_temp.denied('OWN', 'aal1', format($$select admin_set_role(%L, 'support', 'testing it')::text$$, pg_temp.u('P')), 'mfa_required');
select pg_temp.denied('OWN', 'aal1', $$select admin_generate_jobs(5, 'testing it')::text$$, 'mfa_required');
select pg_temp.denied('SUP', 'aal1', format($$select admin_get_player(%L, 'testing it')::text$$, pg_temp.u('T')), 'mfa_required');
select testkit.assert_eq('whoami at aal1 says the admin must verify', (pg_temp.as('OWN', 'aal1', $$select admin_whoami()::text$$)::jsonb)->>'mfa_ok', 'false');
select testkit.assert_eq('whoami at aal1 still names the role', (pg_temp.as('OWN', 'aal1', $$select admin_whoami()::text$$)::jsonb)->>'role', 'owner');
select testkit.assert_eq('whoami at aal2', pg_temp.j('OWN', $$select admin_whoami()::text$$)->>'mfa_ok', 'true');
-- the aal claim also works the way PostgREST delivers it, as one JSON claims object
select set_config('request.jwt.claims', '{"sub":"' || pg_temp.u('OWN') || '","aal":"aal2"}', true) is not null;

-- ===== 3. role matrix: each role can call exactly what it should
select testkit.assert_eq('health: owner', (pg_temp.j('OWN', $$select admin_health()::text$$) ? 'jobs')::text, 'true');
select testkit.assert_eq('health: operations', (pg_temp.j('OPS', $$select admin_health()::text$$) ? 'jobs')::text, 'true');
select testkit.assert_eq('health: support', (pg_temp.j('SUP', $$select admin_health()::text$$) ? 'jobs')::text, 'true');
select testkit.assert_eq('health: analyst', (pg_temp.j('ANA', $$select admin_health()::text$$) ? 'jobs')::text, 'true');
select pg_temp.denied('SUP', 'aal2', $$select admin_generate_jobs(5, 'testing it')::text$$, 'not_allowed');
select pg_temp.denied('ANA', 'aal2', $$select admin_generate_jobs(5, 'testing it')::text$$, 'not_allowed');
select pg_temp.denied('ANA', 'aal2', $$select admin_find_players('Adm')::text$$, 'not_allowed');
select pg_temp.denied('OPS', 'aal2', format($$select admin_get_player(%L, 'testing it')::text$$, pg_temp.u('T')), 'not_allowed');
select pg_temp.denied('ANA', 'aal2', format($$select admin_get_player(%L, 'testing it')::text$$, pg_temp.u('T')), 'not_allowed');
select pg_temp.denied('OPS', 'aal2', $$select admin_list_audit()::text$$, 'not_allowed');
select pg_temp.denied('SUP', 'aal2', $$select admin_list_audit()::text$$, 'not_allowed');
select pg_temp.denied('OPS', 'aal2', $$select admin_list_admins()::text$$, 'not_allowed');
select pg_temp.denied('SUP', 'aal2', format($$select admin_set_role(%L, 'owner', 'make me owner')::text$$, pg_temp.u('SUP')), 'not_allowed');
select pg_temp.denied('OPS', 'aal2', format($$select admin_set_role(%L, 'owner', 'make me owner')::text$$, pg_temp.u('OPS')), 'not_allowed');
select pg_temp.denied('ANA', 'aal2', format($$select admin_set_role(%L, 'owner', 'make me owner')::text$$, pg_temp.u('ANA')), 'not_allowed');
select pg_temp.denied('OPS', 'aal2', $$select admin_find_players('adm@test')::text$$, 'not_allowed');           -- e-mail look-ups are for owner and support only

-- ===== 4. nothing is reachable except through the functions
select testkit.expect_error('authenticated', pg_temp.u('OWN'), 'select * from admin_users', 'permission denied');
select testkit.expect_error('authenticated', pg_temp.u('OWN'), $$insert into admin_users(user_id, role) values (auth.uid(), 'owner')$$, 'permission denied');
select testkit.expect_error('authenticated', pg_temp.u('P'), format($$insert into admin_users(user_id, role) values (%L, 'owner')$$, pg_temp.u('P')), 'permission denied');
select testkit.expect_error('authenticated', pg_temp.u('P'), format($$update admin_users set role = 'owner', active = true where user_id = %L$$, pg_temp.u('OPS')), 'permission denied');
select testkit.expect_error('authenticated', pg_temp.u('OWN'), 'select * from admin_audit_log', 'permission denied');
select testkit.expect_error('authenticated', pg_temp.u('OWN'), 'delete from admin_audit_log', 'permission denied');
select testkit.expect_error('authenticated', pg_temp.u('OWN'), $$select _admin_actor(array['owner'])$$, 'permission denied');
select testkit.expect_error('authenticated', pg_temp.u('OWN'), $$select _admin_log('x', null, null, null, '{}')$$, 'permission denied');

-- ===== 5. a reason is required for everything that changes something or opens one player's record
select pg_temp.denied('OWN', 'aal2', $$select admin_generate_jobs(5, null)::text$$, 'reason_required');
select pg_temp.denied('OWN', 'aal2', $$select admin_generate_jobs(5, 'ab')::text$$, 'reason_required');
select pg_temp.denied('OWN', 'aal2', $$select admin_generate_jobs(5, '   ')::text$$, 'reason_required');
select pg_temp.denied('SUP', 'aal2', format($$select admin_get_player(%L, '')::text$$, pg_temp.u('T')), 'reason_required');
select pg_temp.denied('OWN', 'aal2', format($$select admin_set_role(%L, 'support', null)::text$$, pg_temp.u('P')), 'reason_required');
select pg_temp.denied('OWN', 'aal2', format($$select admin_get_player(%L, %L)::text$$, pg_temp.u('T'), repeat('x', 301)), 'reason_required');

-- ===== 6. generate jobs: clamped, audited
-- earlier suites left jobs behind (some with assignments, which cannot be deleted): close them and make them old, which is what an unattended board looks like
update jobs set status = 'closed', created_at = now() - interval '30 days';
select testkit.assert_eq('health says the board is empty', (pg_temp.j('OWN', $$select admin_health()::text$$)->'alerts' ? 'jobs_empty')::text, 'true');
select testkit.assert_eq('health says jobs were never generated', (pg_temp.j('OWN', $$select admin_health()::text$$)->'alerts' ? 'jobs_not_generated_for_12h')::text, 'true');
select testkit.assert_eq('generate: asking for 1000 makes at most 40', (((pg_temp.j('OPS', $$select admin_generate_jobs(1000, 'board was empty')::text$$)->>'made')::int) between 30 and 40)::text, 'true');
select testkit.assert_eq('the audit row records the clamp', (select (details->>'clamped_to') from admin_audit_log where action = 'generate_jobs' order by id desc limit 1), '40');
select pg_temp.j('OWN', $$select admin_generate_jobs(0, 'one is the least')::text$$);
select testkit.assert_eq('asking for 0 is raised to 1', (select (details->>'clamped_to') from admin_audit_log where action = 'generate_jobs' order by id desc limit 1), '1');
select testkit.assert_eq('the audit row names the actor, role and reason', (select admin_id::text || '|' || admin_role::text || '|' || reason from admin_audit_log where action = 'generate_jobs' order by id desc limit 1), pg_temp.u('OWN')::text || '|owner|one is the least');
select testkit.assert_eq('health counts the open jobs', pg_temp.j('OPS', $$select admin_health()::text$$)->'jobs'->>'open', (select count(*)::text from jobs where status = 'open' and expires_at > now()));
select testkit.assert_eq('health: with 30+ open jobs there is no jobs alert', (pg_temp.j('OPS', $$select admin_health()::text$$)->'alerts' ? 'jobs_empty')::text, 'false');
select testkit.assert_eq('health: ... and none about age', (pg_temp.j('OPS', $$select admin_health()::text$$)->'alerts' ? 'jobs_not_generated_for_12h')::text, 'false');
update jobs set status = 'closed' where id in (select id from jobs where status = 'open' order by created_at offset 5);
select testkit.assert_eq('health: fewer than 10 open jobs is "low"', (pg_temp.j('OPS', $$select admin_health()::text$$)->'alerts' ? 'jobs_low')::text, 'true');
update jobs set created_at = now() - interval '13 hours';
select testkit.assert_eq('health: nothing created for 13 hours is flagged', (pg_temp.j('OPS', $$select admin_health()::text$$)->'alerts' ? 'jobs_not_generated_for_12h')::text, 'true');
select testkit.assert_eq('health: counts what expires soon', (pg_temp.j('OPS', $$select admin_health()::text$$)->'jobs' ? 'expiring_within_6h')::text, 'true');

-- ===== 7. health is aggregate only and counts players online
select testkit.as_user(pg_temp.u('T'), $$select start_session('test')::text$$);
select testkit.assert_eq('health: one player online', ((pg_temp.j('OWN', $$select admin_health()::text$$)->'players'->>'online_now')::int >= 1)::text, 'true');
select testkit.assert_eq('health: by activity', (pg_temp.j('OWN', $$select admin_health()::text$$)->'players'->'online_by_activity' ? 'menu')::text, 'true');
select testkit.assert_eq('health: total players', pg_temp.j('OWN', $$select admin_health()::text$$)->'players'->>'total', (select count(*)::text from profiles));
select testkit.assert_eq('health carries no e-mail or name', (pg_temp.as('OWN', 'aal2', $$select admin_health()::text$$) ~* '@test|AdmTarget')::text, 'false');

-- ===== 8. find players
select testkit.assert_eq('find by username prefix', jsonb_array_length(pg_temp.j('SUP', $$select admin_find_players('admtar')::text$$)->'items')::text, '1');
select testkit.assert_eq('find by display-name prefix, any case', jsonb_array_length(pg_temp.j('OPS', $$select admin_find_players('ADMOWNER')::text$$)->'items')::text, '2');
select testkit.assert_eq('the result says what kind of search it was', pg_temp.j('OPS', $$select admin_find_players('admtar')::text$$)->>'kind', 'name');
select testkit.assert_eq('find by exact player id', jsonb_array_length(pg_temp.j('SUP', format($$select admin_find_players(%L)::text$$, pg_temp.u('T')))->'items')::text, '1');
select testkit.assert_eq('find by exact e-mail (support)', jsonb_array_length(pg_temp.j('SUP', $$select admin_find_players('AdmTarget@test')::text$$)->'items')::text, '1');
select testkit.assert_eq('find by exact e-mail (owner)', pg_temp.j('OWN', $$select admin_find_players('admtarget@test')::text$$)->>'kind', 'email');
select testkit.assert_eq('a partial e-mail finds nobody', jsonb_array_length(pg_temp.j('SUP', $$select admin_find_players('AdmTarg@test')::text$$)->'items')::text, '0');
select testkit.assert_eq('wildcards are not wildcards', jsonb_array_length(pg_temp.j('SUP', $$select admin_find_players('%%')::text$$)->'items')::text, '0');
select testkit.assert_eq('underscores are not wildcards', jsonb_array_length(pg_temp.j('SUP', $$select admin_find_players('A_m')::text$$)->'items')::text, '0');
select pg_temp.denied('SUP', 'aal2', $$select admin_find_players('a')::text$$, 'query_too_short');
select pg_temp.denied('SUP', 'aal2', $$select admin_find_players(null)::text$$, 'query_too_short');
select pg_temp.denied('SUP', 'aal2', format($$select admin_find_players(%L)::text$$, repeat('a', 65)), 'query_too_short');
select testkit.make_user('AdmBulk' || g) from generate_series(1, 60) g;           -- more matches than the limit allows
select testkit.assert_eq('the limit is applied', jsonb_array_length(pg_temp.j('SUP', $$select admin_find_players('adm', 3)::text$$)->'items')::text, '3');
select testkit.assert_eq('a huge limit is clamped to 50, not an error', jsonb_array_length(pg_temp.j('SUP', $$select admin_find_players('adm', 100000)::text$$)->'items')::text, '50');
select testkit.assert_eq('the default limit is 20', jsonb_array_length(pg_temp.j('SUP', $$select admin_find_players('adm')::text$$)->'items')::text, '20');
select testkit.assert_eq('a result never carries an e-mail address', (pg_temp.as('SUP', 'aal2', $$select admin_find_players('AdmTarget@test')::text$$) ~* '@test')::text, 'false');
select testkit.assert_eq('a result says whether the player is online', (pg_temp.j('SUP', $$select admin_find_players('admtar')::text$$)->'items'->0->>'online'), 'true');
select testkit.assert_eq('the audit row has the kind and count', (select details::text from admin_audit_log where action = 'find_players' and details->>'kind' = 'email' order by id desc limit 1), '{"kind": "email", "results": 1}');
select testkit.assert_eq('what was typed is never written to the audit log', (select count(*)::text from admin_audit_log where details::text ilike '%@test%' or details::text ilike '%admtar%' or coalesce(reason, '') ilike '%@test%'), '0');
select testkit.assert_eq('searching is audited', (select count(*)::text from admin_audit_log where action = 'find_players'), (select count(*)::text from admin_audit_log where action = 'find_players' and admin_role in ('owner', 'support', 'operations')));

-- ===== 9. one player's record
create function pg_temp.get(p_k text) returns jsonb language sql as $$
  select pg_temp.j($1, format($q$select admin_get_player(%L, 'ticket 4711: coins missing')::text$q$, pg_temp.u('T'))) $$;
select testkit.assert_eq('profile: name', pg_temp.get('SUP')->'profile'->>'display_name', 'AdmTarget');
select testkit.assert_eq('profile: the e-mail is masked', pg_temp.get('SUP')->'profile'->>'email_masked', 'A***@test');
select testkit.assert_eq('the full e-mail is nowhere in the record', (pg_temp.get('SUP')::text ~* 'AdmTarget@test')::text, 'false');
select testkit.assert_eq('wallet: the starter grant', pg_temp.get('OWN')->'wallet'->>'balance', '5000');
select testkit.assert_eq('ledger: the starter grant is listed', pg_temp.get('OWN')->'ledger'->0->>'kind', 'STARTER_GRANT');
select testkit.assert_eq('vehicles: the starter vehicle', jsonb_array_length(pg_temp.get('OWN')->'vehicles')::text, '1');
select testkit.assert_eq('sessions: the one just opened', jsonb_array_length(pg_temp.get('OWN')->'sessions')::text, '1');
select testkit.assert_eq('online flag', pg_temp.get('OWN')->'profile'->>'online', 'true');
select testkit.assert_eq('social block is present', (pg_temp.get('OWN')->'social' ? 'friends')::text, 'true');
select testkit.assert_eq('a job the player took shows in the record', (select jsonb_array_length(pg_temp.get('OWN')->'jobs')::text from (select testkit.as_user(pg_temp.u('T'), format($$select accept_job(%L, %L)$$, (select id from jobs where status = 'open' and expires_at > now() and required_category = 'truck' and min_level <= 1 order by code limit 1), (select id from vehicle_ownership where player_id = pg_temp.u('T') limit 1)))) x), '1');
select pg_temp.denied('SUP', 'aal2', format($$select admin_get_player(%L, 'ticket 4711')::text$$, gen_random_uuid()), 'player_not_found');
select testkit.assert_eq('viewing a record is audited with the target and the reason', (select target_id || '|' || reason || '|' || action from admin_audit_log where action = 'view_player' order by id desc limit 1), pg_temp.u('T')::text || '|ticket 4711: coins missing|view_player');
select testkit.assert_eq('the audit row for a view holds no personal data', (select details::text from admin_audit_log where action = 'view_player' order by id desc limit 1), '{}');

-- ===== 10. roles
select testkit.assert_eq('an owner lists the admins', jsonb_array_length(pg_temp.j('OWN', $$select admin_list_admins()::text$$)->'items')::text, '5');
select testkit.assert_eq('the list shows the deactivated one as inactive', (select count(*)::text from jsonb_array_elements(pg_temp.j('OWN', $$select admin_list_admins()::text$$)->'items') e where e->>'active' = 'false'), '1');
select pg_temp.denied('OWN', 'aal2', format($$select admin_set_role(%L, 'boss', 'typo in role name')::text$$, pg_temp.u('P')), 'invalid_role');
select pg_temp.denied('OWN', 'aal2', format($$select admin_set_role(%L, null, 'typo in role name')::text$$, pg_temp.u('P')), 'invalid_role');
select pg_temp.denied('OWN', 'aal2', format($$select admin_set_role(%L, 'support', 'no such person')::text$$, gen_random_uuid()), 'user_not_found');
select pg_temp.denied('OWN', 'aal2', format($$select admin_set_role(%L, 'none', 'never was an admin')::text$$, pg_temp.u('P')), 'not_an_admin');
select testkit.assert_eq('an owner makes a player a support admin', pg_temp.j('OWN', format($$select admin_set_role(%L, 'support', 'new support agent')::text$$, pg_temp.u('P')))->>'role', 'support');
select testkit.assert_eq('...who can now find players with MFA', (pg_temp.j('P', $$select admin_find_players('admtar')::text$$)->>'kind'), 'name');
select pg_temp.denied('P', 'aal1', $$select admin_find_players('admtar')::text$$, 'mfa_required');
select testkit.assert_eq('the grant is audited with from and to', (select details::text from admin_audit_log where action = 'set_role' order by id desc limit 1), '{"to": "support", "from": "none"}');
select testkit.assert_eq('the audit row names the person', (select target_id from admin_audit_log where action = 'set_role' order by id desc limit 1), pg_temp.u('P')::text);
select testkit.assert_eq('a role can be changed', pg_temp.j('OWN', format($$select admin_set_role(%L, 'analyst', 'moves to reporting')::text$$, pg_temp.u('P')))->>'role', 'analyst');
select pg_temp.denied('P', 'aal2', $$select admin_find_players('admtar')::text$$, 'not_allowed');
select testkit.assert_eq('access can be taken away', pg_temp.j('OWN', format($$select admin_set_role(%L, 'none', 'left the team')::text$$, pg_temp.u('P')))->>'role', 'none');
select pg_temp.denied('P', 'aal2', $$select admin_health()::text$$, 'not_admin');
select testkit.assert_eq('a revoked admin can be restored', pg_temp.j('OWN', format($$select admin_set_role(%L, 'operations', 'returns')::text$$, pg_temp.u('P')))->>'role', 'operations');
-- the last owner cannot be removed or demoted (the deactivated owner does not count)
select pg_temp.denied('OWN', 'aal2', format($$select admin_set_role(%L, 'analyst', 'stepping down')::text$$, pg_temp.u('OWN')), 'last_owner');
select pg_temp.denied('OWN', 'aal2', format($$select admin_set_role(%L, 'none', 'leaving')::text$$, pg_temp.u('OWN')), 'last_owner');
select testkit.assert_eq('with a second owner the first may step down', pg_temp.j('OWN', format($$select admin_set_role(%L, 'owner', 'second owner')::text$$, pg_temp.u('OWN2')))->>'role', 'owner');
select testkit.assert_eq('... and it works', pg_temp.j('OWN', format($$select admin_set_role(%L, 'analyst', 'stepping down')::text$$, pg_temp.u('OWN')))->>'role', 'analyst');
select pg_temp.denied('OWN2', 'aal2', format($$select admin_set_role(%L, 'none', 'leaving')::text$$, pg_temp.u('OWN2')), 'last_owner');
select pg_temp.denied('OWN', 'aal2', $$select admin_list_admins()::text$$, 'not_allowed');          -- the former owner is an analyst now
select testkit.assert_eq('the new owner can restore them', pg_temp.j('OWN2', format($$select admin_set_role(%L, 'owner', 'back again')::text$$, pg_temp.u('OWN')))->>'role', 'owner');

-- ===== 11. the audit log: readable by owner and analyst, never changeable
select testkit.assert_eq('owner reads the audit log', (jsonb_array_length(pg_temp.j('OWN', $$select admin_list_audit()::text$$)->'items') > 10)::text, 'true');
select testkit.assert_eq('analyst reads the audit log', (jsonb_array_length(pg_temp.j('ANA', $$select admin_list_audit()::text$$)->'items') > 10)::text, 'true');
select testkit.assert_eq('the newest entry is first', pg_temp.j('OWN', $$select admin_list_audit(5)::text$$)->'items'->0->>'id', (select max(id)::text from admin_audit_log));
select testkit.assert_eq('the limit works', jsonb_array_length(pg_temp.j('OWN', $$select admin_list_audit(3)::text$$)->'items')::text, '3');
select testkit.assert_eq('paging works', pg_temp.j('OWN', format($$select admin_list_audit(2, %s)::text$$, (select max(id) - 1 from admin_audit_log)))->'items'->0->>'id', (select (max(id) - 2)::text from admin_audit_log));
select testkit.assert_eq('entries name the admin', ((pg_temp.j('OWN', $$select admin_list_audit(1)::text$$)->'items'->0->>'admin_name') is not null)::text, 'true');
create function pg_temp.must_fail(p_sql text, p_msg text) returns void language plpgsql as $$
begin
  begin execute p_sql; exception when others then if sqlerrm like '%' || p_msg || '%' then return; end if; raise exception 'WRONG ERROR for [%]: %', p_sql, sqlerrm; end;
  raise exception 'NOT REJECTED: [%]', p_sql;
end $$;
select pg_temp.must_fail($$update admin_audit_log set reason = 'rewritten'$$, 'audit_is_append_only');           -- even the database owner
select pg_temp.must_fail($$delete from admin_audit_log$$, 'audit_is_append_only');
select pg_temp.must_fail($$truncate admin_audit_log$$, 'audit_is_append_only');
select testkit.assert_eq('nothing was lost', (select count(*) > 10 from admin_audit_log)::text, 'true');

-- ===== 12. a flood of look-ups is limited
do $$ begin
  perform set_config('request.jwt.claim.sub', pg_temp.u('SUP')::text, true); perform set_config('request.jwt.claim.aal', 'aal2', true);
  set local role authenticated;
  for i in 1..125 loop perform admin_find_players('adm'); end loop;
  reset role; raise exception 'NOT LIMITED: 125 searches in a minute were all accepted';
exception when others then reset role; if sqlerrm not like '%rate_limited%' then raise; end if;
end $$;
