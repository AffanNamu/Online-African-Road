\set ON_ERROR_STOP on
-- Admin site part 2 (migration 20261022000001): content and messages (news and hero banners, broadcasts, routes and servers) and moderation (reports queue, mute, suspend, ban).
create temp table fx(k text primary key, v text);
insert into fx select k, testkit.make_user(n)::text from (values
  ('OWN','CmOwner'),('CON','CmContent'),('OPS','CmOps'),('SUP','CmSupport'),('ANA','CmAnalyst'),
  ('L','CmLeader'),('B','CmBad'),('C','CmC'),('D','CmD'),('E','CmE')) t(k, n);
create function pg_temp.u(k text) returns uuid language sql as $$ select v::uuid from fx where fx.k = $1 $$;
insert into admin_users(user_id, role) values (pg_temp.u('OWN'), 'owner'), (pg_temp.u('CON'), 'content'), (pg_temp.u('OPS'), 'operations'), (pg_temp.u('SUP'), 'support'), (pg_temp.u('ANA'), 'analyst');

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
-- a player (no admin claim) acts
create function pg_temp.pl(p_k text, p_sql text) returns text language sql as $$ select testkit.as_user(pg_temp.u($1), $2) $$;
create function pg_temp.pl_fail(p_k text, p_sql text, p_msg text) returns void language sql as $$ select testkit.expect_error('authenticated', pg_temp.u($1), $2, $3) $$;

-- ===== 1. the new role exists and the owner can hand it out
select testkit.assert_eq('owner can make a content editor', pg_temp.j('OWN', format($$select admin_set_role(%L, 'content', 'needs to edit the news')::text$$, pg_temp.u('E')))->>'role', 'content');
select testkit.assert_eq('... and take it away', pg_temp.j('OWN', format($$select admin_set_role(%L, 'none', 'no longer editing')::text$$, pg_temp.u('E')))->>'role', 'none');
select pg_temp.denied('OWN', 'aal2', format($$select admin_set_role(%L, 'editor', 'not a real role')::text$$, pg_temp.u('E')), 'invalid_role');
select pg_temp.denied('CON', 'aal2', format($$select admin_set_role(%L, 'owner', 'make me owner')::text$$, pg_temp.u('CON')), 'not_allowed');
select pg_temp.denied('CON', 'aal2', $$select admin_health()::text$$, 'not_allowed');                 -- a content editor sees nothing but content
select pg_temp.denied('CON', 'aal2', $$select admin_list_audit()::text$$, 'not_allowed');

-- ===== 2. every new function: players get 'not_admin', no multi-factor gets 'mfa_required', anon gets nothing
create temp table calls(n text, sql text);
insert into calls values
 ('admin_list_content', $$select admin_list_content()::text$$),
 ('admin_save_content', $$select admin_save_content('x1','news','tip','t','b',null,0,null,null,false,null,null,null,null,null,'testing it')::text$$),
 ('admin_set_content_published', $$select admin_set_content_published('x1', true, 'testing it')::text$$),
 ('admin_send_broadcast', $$select admin_send_broadcast('hello there','hello world','testing it')::text$$),
 ('admin_list_broadcasts', $$select admin_list_broadcasts()::text$$),
 ('admin_list_world', $$select admin_list_world()::text$$),
 ('admin_set_route_state', $$select admin_set_route_state('lagos-ibadan','OPEN',null,'testing it')::text$$),
 ('admin_set_server_state', $$select admin_set_server_state('main','ONLINE',null,null,'testing it')::text$$),
 ('admin_list_reports', $$select admin_list_reports()::text$$),
 ('admin_get_report', $$select admin_get_report(1)::text$$),
 ('admin_review_report', $$select admin_review_report(1,'dismiss','testing it')::text$$),
 ('admin_apply_sanction', format($$select admin_apply_sanction(%L,'mute',1,'testing it')::text$$, gen_random_uuid())),
 ('admin_lift_sanction', format($$select admin_lift_sanction(%L,'testing it')::text$$, gen_random_uuid())),
 ('admin_list_sanctions', $$select admin_list_sanctions()::text$$);
do $$ declare c record;
begin
  for c in select * from calls loop
    perform pg_temp.denied('L', 'aal2', c.sql, 'not_admin');                      -- a player
    perform pg_temp.denied('OWN', 'aal1', c.sql, 'mfa_required');                 -- an owner who has not verified the code
    perform testkit.expect_error('anon', null, c.sql, 'permission denied');
    perform testkit.expect_error('authenticated', null, c.sql, 'not_authenticated');
  end loop;
end $$;

-- ===== 3. role matrix
select pg_temp.denied('SUP', 'aal2', $$select admin_list_content()::text$$, 'not_allowed');
select pg_temp.denied('OPS', 'aal2', $$select admin_list_content()::text$$, 'not_allowed');
select pg_temp.denied('ANA', 'aal2', $$select admin_list_content()::text$$, 'not_allowed');
select pg_temp.denied('SUP', 'aal2', $$select admin_save_content('x1','news','tip','t','b',null,0,null,null,false,null,null,null,null,null,'testing it')::text$$, 'not_allowed');
select pg_temp.denied('OPS', 'aal2', $$select admin_set_content_published('x1', true, 'testing it')::text$$, 'not_allowed');
select pg_temp.denied('CON', 'aal2', $$select admin_send_broadcast('hello there','hello world','testing it')::text$$, 'not_allowed');
select pg_temp.denied('SUP', 'aal2', $$select admin_send_broadcast('hello there','hello world','testing it')::text$$, 'not_allowed');
select pg_temp.denied('CON', 'aal2', $$select admin_list_broadcasts()::text$$, 'not_allowed');
select pg_temp.denied('CON', 'aal2', $$select admin_list_world()::text$$, 'not_allowed');
select pg_temp.denied('SUP', 'aal2', $$select admin_set_route_state('lagos-ibadan','OPEN',null,'testing it')::text$$, 'not_allowed');
select pg_temp.denied('CON', 'aal2', $$select admin_set_server_state('main','ONLINE',null,null,'testing it')::text$$, 'not_allowed');
select pg_temp.denied('OPS', 'aal2', $$select admin_list_reports()::text$$, 'not_allowed');
select pg_temp.denied('CON', 'aal2', $$select admin_list_reports()::text$$, 'not_allowed');
select pg_temp.denied('ANA', 'aal2', $$select admin_list_reports()::text$$, 'not_allowed');
select pg_temp.denied('OPS', 'aal2', $$select admin_get_report(1)::text$$, 'not_allowed');
select pg_temp.denied('OPS', 'aal2', $$select admin_review_report(1,'dismiss','testing it')::text$$, 'not_allowed');
select pg_temp.denied('OPS', 'aal2', format($$select admin_apply_sanction(%L,'mute',1,'testing it')::text$$, pg_temp.u('B')), 'not_allowed');
select pg_temp.denied('ANA', 'aal2', format($$select admin_apply_sanction(%L,'mute',1,'testing it')::text$$, pg_temp.u('B')), 'not_allowed');
select pg_temp.denied('CON', 'aal2', format($$select admin_apply_sanction(%L,'mute',1,'testing it')::text$$, pg_temp.u('B')), 'not_allowed');
select pg_temp.denied('OPS', 'aal2', $$select admin_list_sanctions()::text$$, 'not_allowed');
select testkit.expect_error('authenticated', pg_temp.u('OWN'), 'select * from player_sanctions', 'permission denied');
select testkit.expect_error('authenticated', pg_temp.u('OWN'), 'select * from chat_report_reviews', 'permission denied');
select testkit.expect_error('authenticated', pg_temp.u('SUP'), format($$insert into player_sanctions(player_id, kind, reason, created_by, expires_at) values (%L, 'mute', 'abc', %L, now() + interval '1 hour')$$, pg_temp.u('B'), pg_temp.u('SUP')), 'permission denied');
select testkit.expect_error('authenticated', pg_temp.u('SUP'), $$select _sync_ban(gen_random_uuid())$$, 'permission denied');
select testkit.expect_error('authenticated', pg_temp.u('SUP'), $$select _sanction_live(gen_random_uuid(), array['ban'])$$, 'permission denied');

-- ===== 4. news and hero banners
select testkit.assert_eq('save: create a draft news item', pg_temp.j('CON', $$select admin_save_content('adm-news-1','news','event','  Race <b>weekend</b>  ','Drive the Lagos run this Saturday for bonus fun.',null,5,null,null,false,null,null,null,null,null,'announcing the event')::text$$)->>'state', 'draft');
select testkit.assert_eq('save: text is trimmed and < > are stripped', (select title from content_items where id = 'adm-news-1'), 'Race bweekend/b');
select testkit.assert_eq('a draft is not served to players', (select count(*)::text from jsonb_array_elements(pg_temp.pl('L', $$select get_content('news', 50)::text$$)::jsonb->'items') i where i->>'id' = 'adm-news-1'), '0');
select testkit.assert_eq('publish it', pg_temp.j('CON', $$select admin_set_content_published('adm-news-1', true, 'ready to go live')::text$$)->>'state', 'live');
select testkit.assert_eq('a live item is served to players', (select count(*)::text from jsonb_array_elements(pg_temp.pl('L', $$select get_content('news', 50)::text$$)::jsonb->'items') i where i->>'id' = 'adm-news-1'), '1');
select testkit.assert_eq('unpublish it', pg_temp.j('OWN', $$select admin_set_content_published('adm-news-1', false, 'pulling it back')::text$$)->>'state', 'draft');
select testkit.assert_eq('... and players stop seeing it', (select count(*)::text from jsonb_array_elements(pg_temp.pl('L', $$select get_content('news', 50)::text$$)::jsonb->'items') i where i->>'id' = 'adm-news-1'), '0');
select testkit.assert_eq('scheduled for later', pg_temp.j('CON', $$select admin_save_content('adm-news-2','news','update','Soon','Starts tomorrow.',null,0,now() + interval '1 day',now() + interval '2 days',true,null,null,null,null,null,'schedule it')::text$$)->>'state', 'scheduled');
select testkit.assert_eq('... players do not see it yet', (select count(*)::text from jsonb_array_elements(pg_temp.pl('L', $$select get_content('news', 50)::text$$)::jsonb->'items') i where i->>'id' = 'adm-news-2'), '0');
select testkit.assert_eq('already past its end', pg_temp.j('CON', $$select admin_save_content('adm-news-3','news','update','Over','Was yesterday.',null,0,now() - interval '3 days',now() - interval '1 day',true,null,null,null,null,null,'back-fill it')::text$$)->>'state', 'expired');
select testkit.assert_eq('the list shows every state', (select string_agg(i->>'state', ',' order by i->>'id') from jsonb_array_elements(pg_temp.j('CON', $$select admin_list_content('news')::text$$)->'items') i where i->>'id' like 'adm-news-%'), 'draft,scheduled,expired');
select testkit.assert_eq('editing keeps the id and updates the text', pg_temp.j('CON', $$select admin_save_content('adm-news-1','news','tip','Race weekend','Edited text.',null,7,null,null,true,null,null,null,null,null,'fixing a typo')::text$$)->>'body', 'Edited text.');
select testkit.assert_eq('... and does not duplicate it', (select count(*)::text from content_items where id = 'adm-news-1'), '1');
select testkit.assert_eq('a hero banner', pg_temp.j('CON', $$select admin_save_content('adm-hero-1','hero','event','Hero','Hero body.',null,100,null,null,true,'NEW','Drive the','Lagos run','jobs','Start now','new hero')::text$$)->>'action', 'jobs');
select testkit.assert_eq('players get it', (select count(*)::text from jsonb_array_elements(pg_temp.pl('L', $$select get_content('hero', 10)::text$$)::jsonb->'items') i where i->>'id' = 'adm-hero-1'), '1');
select pg_temp.j('CON', $$select admin_save_content('adm-news-4','news','tip','Plain news','No banner here.',null,0,null,null,false,'EYEBROW','Head','Accent','jobs','Button','news ignores banner fields')::text$$);
select testkit.assert_eq('a news item given banner fields keeps none of them', (select (eyebrow is null and headline is null and headline_accent is null and action is null and button is null)::text from content_items where id = 'adm-news-4'), 'true');
select testkit.assert_eq('a news item carries no hero fields', (select (headline is null and action is null and button is null)::text from content_items where id = 'adm-news-1'), 'true');
select pg_temp.denied('CON', 'aal2', $$select admin_save_content('Bad Id','news','tip','t','b',null,0,null,null,false,null,null,null,null,null,'testing it')::text$$, 'invalid_id');
select pg_temp.denied('CON', 'aal2', $$select admin_save_content('x1','video','tip','t','b',null,0,null,null,false,null,null,null,null,null,'testing it')::text$$, 'invalid_kind');
select pg_temp.denied('CON', 'aal2', $$select admin_save_content('x1','news','gossip','t','b',null,0,null,null,false,null,null,null,null,null,'testing it')::text$$, 'invalid_category');
select pg_temp.denied('CON', 'aal2', $$select admin_save_content('x1','news','tip','   ','b',null,0,null,null,false,null,null,null,null,null,'testing it')::text$$, 'invalid_title');
select pg_temp.denied('CON', 'aal2', format($$select admin_save_content('x1','news','tip',%L,'b',null,0,null,null,false,null,null,null,null,null,'testing it')::text$$, repeat('t', 81)), 'invalid_title');
select pg_temp.denied('CON', 'aal2', format($$select admin_save_content('x1','news','tip','t',%L,null,0,null,null,false,null,null,null,null,null,'testing it')::text$$, repeat('b', 221)), 'invalid_body');
select pg_temp.denied('CON', 'aal2', $$select admin_save_content('x1','news','tip','t','<>',null,0,null,null,false,null,null,null,null,null,'testing it')::text$$, 'invalid_body');
select pg_temp.denied('CON', 'aal2', $$select admin_save_content('x1','news','tip','t','b','Bad Key',0,null,null,false,null,null,null,null,null,'testing it')::text$$, 'invalid_image');
select pg_temp.denied('CON', 'aal2', $$select admin_save_content('x1','news','tip','t','b',null,5000,null,null,false,null,null,null,null,null,'testing it')::text$$, 'invalid_priority');
select pg_temp.denied('CON', 'aal2', $$select admin_save_content('x1','news','tip','t','b',null,0,now(),now() - interval '1 hour',false,null,null,null,null,null,'testing it')::text$$, 'invalid_schedule');
select pg_temp.denied('CON', 'aal2', $$select admin_save_content('x1','hero','tip','t','b',null,0,null,null,false,null,null,null,'jobs','Go','testing it')::text$$, 'invalid_headline');
select pg_temp.denied('CON', 'aal2', $$select admin_save_content('x1','hero','tip','t','b',null,0,null,null,false,null,'Head',null,'jobs',null,'testing it')::text$$, 'invalid_button');
select pg_temp.denied('CON', 'aal2', $$select admin_save_content('x1','hero','tip','t','b',null,0,null,null,false,null,'Head',null,'format-disk','Go','testing it')::text$$, 'invalid_action');
select pg_temp.denied('CON', 'aal2', $$select admin_save_content('adm-news-1','hero','tip','t','b',null,0,null,null,false,null,'Head',null,'jobs','Go','testing it')::text$$, 'kind_locked');
select pg_temp.denied('CON', 'aal2', $$select admin_save_content('x1','news','tip','t','b',null,0,null,null,false,null,null,null,null,null,'')::text$$, 'reason_required');
select pg_temp.denied('CON', 'aal2', $$select admin_set_content_published('no-such-item', true, 'testing it')::text$$, 'content_not_found');
select pg_temp.denied('CON', 'aal2', $$select admin_set_content_published('adm-news-1', null, 'testing it')::text$$, 'invalid_state');
select pg_temp.denied('CON', 'aal2', $$select admin_list_content('video')::text$$, 'invalid_kind');
select testkit.assert_eq('saves are audited with the reason', (select reason from admin_audit_log where action = 'save_content' and target_id = 'adm-news-1' order by id desc limit 1), 'fixing a typo');
select testkit.assert_eq('publishing is audited', (select count(*)::text from admin_audit_log where action = 'publish_content' and target_id = 'adm-news-1'), '1');
select testkit.assert_eq('so is unpublishing', (select count(*)::text from admin_audit_log where action = 'unpublish_content' and target_id = 'adm-news-1'), '1');
select testkit.assert_eq('a rejected save leaves nothing behind', (select count(*)::text from content_items where id in ('x1','Bad Id')), '0');

-- ===== 5. broadcast
update notifications set dedupe_key = dedupe_key where false;
select testkit.assert_eq('broadcast reaches every player', pg_temp.j('OPS', $$select admin_send_broadcast('  Maintenance <b>tonight</b> ','We restart at 22:00 UTC for ten minutes.','planned maintenance notice')::text$$)->>'recipients', (select count(*)::text from profiles));
select testkit.assert_eq('a player has it in their inbox, cleaned', (select title from notifications where player_id = pg_temp.u('L') and dedupe_key like 'broadcast:admin-%' order by created_at desc limit 1), 'Maintenance btonight/b');
select testkit.assert_eq('the history shows it with the recipient count', pg_temp.j('OPS', $$select admin_list_broadcasts()::text$$)->'items'->0->>'recipients', (select count(*)::text from profiles));
select testkit.assert_eq('the audit log holds the text', (select details->>'message' from admin_audit_log where action = 'broadcast' order by id desc limit 1), 'We restart at 22:00 UTC for ten minutes.');
select pg_temp.denied('OPS', 'aal2', $$select admin_send_broadcast('ab','long enough message','testing it')::text$$, 'invalid_title');
select pg_temp.denied('OPS', 'aal2', $$select admin_send_broadcast('A title','x','testing it')::text$$, 'invalid_message');
select pg_temp.denied('OPS', 'aal2', format($$select admin_send_broadcast('A title',%L,'testing it')::text$$, repeat('m', 301)), 'invalid_message');
select pg_temp.denied('OPS', 'aal2', $$select admin_send_broadcast('A title','long enough message','')::text$$, 'reason_required');
select pg_temp.j('OPS', $$select admin_send_broadcast('Second notice','Second broadcast of the hour.','second notice')::text$$);
select pg_temp.j('OPS', $$select admin_send_broadcast('Third notice','Third broadcast of the hour.','third notice')::text$$);
select pg_temp.denied('OPS', 'aal2', $$select admin_send_broadcast('Fourth notice','A fourth broadcast in the hour.','fourth notice')::text$$, 'rate_limited');
select testkit.assert_eq('the history lists them newest first', pg_temp.j('OPS', $$select admin_list_broadcasts()::text$$)->'items'->0->>'title', 'Third notice');

-- ===== 6. routes and servers
select testkit.assert_eq('the world list names routes and servers', (pg_temp.j('OPS', $$select admin_list_world()::text$$) ?& array['routes', 'servers', 'online_now'])::text, 'true');
select testkit.assert_eq('the playable route is marked playable', (select (r->>'playable') from jsonb_array_elements(pg_temp.j('OPS', $$select admin_list_world()::text$$)->'routes') r where r->>'code' = 'lagos-ibadan'), 'true');
select testkit.assert_eq('lock a route', pg_temp.j('OPS', $$select admin_set_route_state('lagos-ibadan', 'LOCKED', 3, 'road closed for repairs')::text$$)->>'status', 'LOCKED');
select testkit.assert_eq('... the rank was raised too', (select unlock_level::text from world_routes where code = 'lagos-ibadan'), '3');
select testkit.assert_eq('... and the audit log has before and after', (select details->'from'->>'status' || '>' || (details->'to'->>'status') from admin_audit_log where action = 'set_route' order by id desc limit 1), 'OPEN>LOCKED');
select testkit.assert_eq('open it again, rank untouched when null', pg_temp.j('OWN', $$select admin_set_route_state('lagos-ibadan', 'OPEN', null, 'repairs finished')::text$$)->>'unlock_level', '3');
select pg_temp.j('OWN', $$select admin_set_route_state('lagos-ibadan', 'OPEN', 1, 'back to the start')::text$$);
do $$ declare v_code text;
begin
  select code into v_code from world_routes where game_route_id is null limit 1;
  if v_code is null then raise exception 'fixture: expected a route without a playable road in the seed'; end if;
  perform pg_temp.denied('OPS', 'aal2', format($q$select admin_set_route_state(%L, 'OPEN', null, 'trying to open it')::text$q$, v_code), 'route_not_playable');
  perform pg_temp.j('OPS', format($q$select admin_set_route_state(%L, 'LOCKED', null, 'keeping it shut')::text$q$, v_code));   -- locking such a route is fine
end $$;
select pg_temp.denied('OPS', 'aal2', $$select admin_set_route_state('lagos-ibadan', 'WIDE_OPEN', null, 'testing it')::text$$, 'invalid_status');
select pg_temp.denied('OPS', 'aal2', $$select admin_set_route_state('lagos-ibadan', 'OPEN', 0, 'testing it')::text$$, 'invalid_rank');
select pg_temp.denied('OPS', 'aal2', $$select admin_set_route_state('lagos-ibadan', 'OPEN', 101, 'testing it')::text$$, 'invalid_rank');
select pg_temp.denied('OPS', 'aal2', $$select admin_set_route_state('nowhere-road', 'OPEN', null, 'testing it')::text$$, 'route_not_found');
select pg_temp.denied('OPS', 'aal2', $$select admin_set_route_state('lagos-ibadan', 'OPEN', null, 'no')::text$$, 'reason_required');
select testkit.assert_eq('a server into maintenance', pg_temp.j('OPS', $$select admin_set_server_state('main', 'MAINTENANCE', 50, 'West Africa', 'planned restart')::text$$)->>'status', 'MAINTENANCE');
select testkit.assert_eq('players see maintenance', (pg_temp.pl('L', $$select list_servers()::text$$)::jsonb->'items'->0->>'status'), 'MAINTENANCE');
select testkit.assert_eq('capacity and region were set', (select capacity::text || '/' || region from game_servers where id = 'main'), '50/West Africa');
select testkit.assert_eq('null leaves them alone', pg_temp.j('OPS', $$select admin_set_server_state('main', 'ONLINE', null, null, 'restart finished')::text$$)->>'region', 'West Africa');
select testkit.assert_eq('0 removes the capacity limit', ((pg_temp.j('OPS', $$select admin_set_server_state('main', null, 0, null, 'no limit for now')::text$$)->>'capacity') is null)::text, 'true');
select pg_temp.denied('OPS', 'aal2', $$select admin_set_server_state('main', 'BROKEN', null, null, 'testing it')::text$$, 'invalid_status');
select pg_temp.denied('OPS', 'aal2', $$select admin_set_server_state('main', null, -1, null, 'testing it')::text$$, 'invalid_capacity');
select pg_temp.denied('OPS', 'aal2', $$select admin_set_server_state('main', null, 100001, null, 'testing it')::text$$, 'invalid_capacity');
select pg_temp.denied('OPS', 'aal2', $$select admin_set_server_state('ghost', 'ONLINE', null, null, 'testing it')::text$$, 'server_not_found');
select pg_temp.denied('OPS', 'aal2', $$select admin_set_server_state('main', null, null, 'x', 'testing it')::text$$, 'invalid_region');

-- ===== 7. the reports queue
update convoys set disbanded_at = now() where disbanded_at is null;
delete from chat_reports;     -- earlier suites left reports behind
update game_sessions set ended_at = now() where ended_at is null;
insert into fx select 'cv', pg_temp.pl('L', $$select create_convoy('Moderation Run')$$);
select pg_temp.pl(k, format('select join_convoy(%L)', (select v from fx where k = 'cv'))) from (values ('B'), ('C'), ('D')) t(k);
select pg_temp.pl(k, 'select start_session()::text') from (values ('L'), ('B'), ('C'), ('D')) t(k);
select pg_temp.pl('C', $$select post_convoy_message('hello everyone')::text$$);
select pg_temp.pl('B', $$select post_convoy_message('you are all idiots')::text$$);
select pg_temp.pl('D', $$select post_convoy_message('nice drive')::text$$);
create function pg_temp.mid(p_like text) returns bigint language sql as $$ select max(id) from convoy_messages where body like $1 $$;
select pg_temp.pl('L', format($$select report_chat_message(%s, 'abuse')::text$$, pg_temp.mid('you are all idiots')));
select pg_temp.pl('C', format($$select report_chat_message(%s, 'harassment')::text$$, pg_temp.mid('you are all idiots')));
select testkit.assert_eq('two reports do not hide a line', (select moderation from convoy_messages where id = pg_temp.mid('you are all idiots')), 'ok');
select pg_temp.pl('D', format($$select report_chat_message(%s, 'abuse')::text$$, pg_temp.mid('you are all idiots')));
select testkit.assert_eq('three do (existing behaviour)', (select moderation from convoy_messages where id = pg_temp.mid('you are all idiots')), 'hidden');
select pg_temp.pl('L', format($$select report_chat_message(%s, 'spam')::text$$, pg_temp.mid('nice drive')));
select testkit.assert_eq('the queue holds both lines', jsonb_array_length(pg_temp.j('SUP', $$select admin_list_reports()::text$$)->'items')::text, '2');
select testkit.assert_eq('the most reported line is first', pg_temp.j('SUP', $$select admin_list_reports()::text$$)->'items'->0->>'body', 'you are all idiots');
select testkit.assert_eq('with the report count', pg_temp.j('SUP', $$select admin_list_reports()::text$$)->'items'->0->>'reports', '3');
select testkit.assert_eq('and the reasons', pg_temp.j('SUP', $$select admin_list_reports()::text$$)->'items'->0->'reasons'->>'abuse', '2');
select testkit.assert_eq('and who sent it', pg_temp.j('SUP', $$select admin_list_reports()::text$$)->'items'->0->>'sender_name', 'CmBad');
select testkit.assert_eq('the owner can open the queue too', jsonb_array_length(pg_temp.j('OWN', $$select admin_list_reports('all')::text$$)->'items')::text, '2');
select pg_temp.denied('SUP', 'aal2', $$select admin_list_reports('everything')::text$$, 'invalid_state');
-- the line with context
select testkit.assert_eq('a report shows the lines around it (chat and system lines, at most 8)', (jsonb_array_length(pg_temp.j('SUP', format($$select admin_get_report(%s)::text$$, pg_temp.mid('you are all idiots')))->'context') between 3 and 8)::text, 'true');
select testkit.assert_eq('... marks the reported one', (select count(*)::text from jsonb_array_elements(pg_temp.j('SUP', format($$select admin_get_report(%s)::text$$, pg_temp.mid('you are all idiots')))->'context') c where (c->>'reported')::boolean and c->>'body' = 'you are all idiots'), '1');
select testkit.assert_eq('every view is audited (three views above)', (select count(*)::text from admin_audit_log where action = 'view_report' and target_id = pg_temp.mid('you are all idiots')::text), '3');
select pg_temp.denied('SUP', 'aal2', format($$select admin_get_report(%s)::text$$, (select min(id) from convoy_messages where kind = 'system')), 'report_not_found');
select pg_temp.denied('SUP', 'aal2', $$select admin_get_report(999999999)::text$$, 'report_not_found');
-- dismissing and removing
select testkit.assert_eq('dismiss the harmless one', pg_temp.j('SUP', format($$select admin_review_report(%s, 'dismiss', 'it is only banter')::text$$, pg_temp.mid('nice drive')))->>'decision', 'dismiss');
select testkit.assert_eq('the open queue shrinks', jsonb_array_length(pg_temp.j('SUP', $$select admin_list_reports()::text$$)->'items')::text, '1');
select pg_temp.pl('B', format($$select report_chat_message(%s, 'spam')::text$$, pg_temp.mid('nice drive')));
select testkit.assert_eq('a fresh report on a dismissed line puts it back in the queue', jsonb_array_length(pg_temp.j('SUP', $$select admin_list_reports()::text$$)->'items')::text, '2');
select pg_temp.j('SUP', format($$select admin_review_report(%s, 'dismiss', 'still only banter')::text$$, pg_temp.mid('nice drive')));
select testkit.assert_eq('the reviewed list shows it', pg_temp.j('SUP', $$select admin_list_reports('reviewed')::text$$)->'items'->0->>'decision', 'dismiss');
select testkit.assert_eq('remove the abusive one', pg_temp.j('SUP', format($$select admin_review_report(%s, 'remove', 'plain abuse')::text$$, pg_temp.mid('you are all idiots')))->>'decision', 'remove');
select testkit.assert_eq('the line is removed for everyone', (select moderation from convoy_messages where id = pg_temp.mid('you are all idiots')), 'removed');
select testkit.assert_eq('nothing is left open', jsonb_array_length(pg_temp.j('SUP', $$select admin_list_reports()::text$$)->'items')::text, '0');
select pg_temp.denied('SUP', 'aal2', format($$select admin_review_report(%s, 'ignore', 'testing it')::text$$, pg_temp.mid('nice drive')), 'invalid_decision');
select pg_temp.denied('SUP', 'aal2', format($$select admin_review_report(%s, 'dismiss', '')::text$$, pg_temp.mid('nice drive')), 'reason_required');
select pg_temp.denied('SUP', 'aal2', $$select admin_review_report(999999999, 'dismiss', 'testing it')::text$$, 'report_not_found');
select testkit.assert_eq('a line nobody reported cannot be reviewed', (select count(*)::text from chat_report_reviews where message_id = pg_temp.mid('hello everyone')), '0');
select pg_temp.denied('SUP', 'aal2', format($$select admin_review_report(%s, 'remove', 'not reported')::text$$, pg_temp.mid('hello everyone')), 'report_not_found');
select testkit.assert_eq('reviews are audited with the sender', (select details->>'sender_id' from admin_audit_log where action = 'review_report' and target_id = pg_temp.mid('you are all idiots')::text), pg_temp.u('B')::text);

-- ===== 8. sanctions
select testkit.assert_eq('mute for two hours', pg_temp.j('SUP', format($$select admin_apply_sanction(%L, 'mute', 2, 'abusive chat, see report', %s)::text$$, pg_temp.u('B'), pg_temp.mid('you are all idiots')))->>'kind', 'mute');
select pg_temp.pl_fail('B', $$select post_convoy_message('still here')::text$$, 'muted');
select testkit.assert_eq('a muted player can still play (start a session)', (pg_temp.pl('B', $$select start_session()::text$$)::jsonb ? 'session_id')::text, 'true');
select testkit.assert_eq('others can still chat', (pg_temp.pl('C', $$select post_convoy_message('hello again')::text$$)::jsonb ? 'id')::text, 'true');
select testkit.assert_eq('the player was told', (select count(*)::text from notifications where player_id = pg_temp.u('B') and title = 'Chat muted'), '1');
select pg_temp.denied('SUP', 'aal2', format($$select admin_apply_sanction(%L, 'mute', 2, 'again please')::text$$, pg_temp.u('B')), 'already_sanctioned');
select testkit.assert_eq('the list shows it active, with the line that led to it', (pg_temp.j('SUP', format($$select admin_list_sanctions('active', %L)::text$$, pg_temp.u('B')))->'items'->0->>'message_id')::bigint::text, pg_temp.mid('you are all idiots')::text);
select testkit.assert_eq('the queue marks the sender as muted', pg_temp.j('SUP', $$select admin_list_reports('reviewed')::text$$)->'items'->0->'sender_sanctions'->>0, 'mute');
-- lifting
select testkit.assert_eq('lift the mute', pg_temp.j('SUP', format($$select admin_lift_sanction(%L, 'apologised, lifting early')::text$$, (select id::text from player_sanctions where player_id = pg_temp.u('B') and kind = 'mute')))->>'kind', 'mute');
select testkit.assert_eq('they can chat again', (pg_temp.pl('B', $$select post_convoy_message('sorry all')::text$$)::jsonb ? 'id')::text, 'true');
select pg_temp.denied('SUP', 'aal2', format($$select admin_lift_sanction(%L, 'twice')::text$$, (select id::text from player_sanctions where player_id = pg_temp.u('B') and kind = 'mute')), 'sanction_not_active');
-- an expired mute needs no lifting
update player_sanctions set created_at = now() - interval '3 hours', expires_at = now() - interval '1 hour' where player_id = pg_temp.u('B');
select pg_temp.j('SUP', format($$select admin_apply_sanction(%L, 'mute', 1, 'second offence')::text$$, pg_temp.u('B')));
update player_sanctions set created_at = now() - interval '3 hours', expires_at = now() - interval '1 hour' where player_id = pg_temp.u('B') and lifted_at is null;
select testkit.assert_eq('a mute that ran out lets them chat', (pg_temp.pl('B', $$select post_convoy_message('back after the mute')::text$$)::jsonb ? 'id')::text, 'true');
select testkit.assert_eq('history keeps everything', jsonb_array_length(pg_temp.j('SUP', format($$select admin_list_sanctions('history', %L)::text$$, pg_temp.u('B')))->'items')::text, '2');
select testkit.assert_eq('active shows none', jsonb_array_length(pg_temp.j('SUP', format($$select admin_list_sanctions('active', %L)::text$$, pg_temp.u('B')))->'items')::text, '0');
-- suspend
select pg_temp.pl('C', $$select start_session()::text$$);
select testkit.assert_eq('suspend for a day', pg_temp.j('SUP', format($$select admin_apply_sanction(%L, 'suspend', 24, 'repeated spamming')::text$$, pg_temp.u('C')))->>'kind', 'suspend');
select testkit.assert_eq('their open sessions were ended', (select count(*)::text from game_sessions where player_id = pg_temp.u('C') and ended_at is null), '0');
select pg_temp.pl_fail('C', $$select start_session()::text$$, 'account_suspended');
select pg_temp.pl_fail('C', $$select post_convoy_message('can I talk')::text$$, 'muted');
select testkit.assert_eq('the sign-in service is told', (select (banned_until > now() + interval '23 hours' and banned_until < now() + interval '25 hours')::text from auth.users where id = pg_temp.u('C')), 'true');
select testkit.assert_eq('the audit row says it synced', (select details->>'auth_synced' from admin_audit_log where action = 'apply_sanction' and target_id = pg_temp.u('C')::text order by id desc limit 1), 'true');
select testkit.assert_eq('lifting clears it', pg_temp.j('SUP', format($$select admin_lift_sanction(%L, 'mistaken identity')::text$$, (select id::text from player_sanctions where player_id = pg_temp.u('C') and kind = 'suspend')))->>'kind', 'suspend');
select testkit.assert_eq('... banned_until is empty again', (select (banned_until is null)::text from auth.users where id = pg_temp.u('C')), 'true');
select testkit.assert_eq('... and they can play', (pg_temp.pl('C', $$select start_session()::text$$)::jsonb ? 'session_id')::text, 'true');
-- ban
select pg_temp.denied('SUP', 'aal2', format($$select admin_apply_sanction(%L, 'ban', null, 'cheating')::text$$, pg_temp.u('D')), 'not_allowed');
select testkit.assert_eq('owner bans permanently', (pg_temp.j('OWN', format($$select admin_apply_sanction(%L, 'ban', null, 'sold stolen accounts', null)::text$$, pg_temp.u('D')))->>'expires_at' is null)::text, 'true');
select pg_temp.pl_fail('D', $$select start_session()::text$$, 'account_suspended');
select testkit.assert_eq('banned_until is far in the future', (select (banned_until > now() + interval '50 years')::text from auth.users where id = pg_temp.u('D')), 'true');
select pg_temp.denied('SUP', 'aal2', format($$select admin_apply_sanction(%L, 'mute', 1, 'one more thing')::text$$, pg_temp.u('D')), 'already_banned');
select pg_temp.denied('SUP', 'aal2', format($$select admin_lift_sanction(%L, 'let them back')::text$$, (select id::text from player_sanctions where player_id = pg_temp.u('D') and kind = 'ban')), 'not_allowed');
select testkit.assert_eq('only an owner lifts a ban', pg_temp.j('OWN', format($$select admin_lift_sanction(%L, 'appeal accepted')::text$$, (select id::text from player_sanctions where player_id = pg_temp.u('D') and kind = 'ban')))->>'kind', 'ban');
select testkit.assert_eq('... and they are back', (pg_temp.pl('D', $$select start_session()::text$$)::jsonb ? 'session_id')::text, 'true');
-- a suspension still running keeps its end when a ban on top of it is lifted
select pg_temp.j('SUP', format($$select admin_apply_sanction(%L, 'suspend', 48, 'cooling off')::text$$, pg_temp.u('E')));
select pg_temp.j('OWN', format($$select admin_apply_sanction(%L, 'ban', null, 'then worse')::text$$, pg_temp.u('E')));
select pg_temp.denied('OWN', 'aal2', format($$select admin_apply_sanction(%L, 'ban', null, 'again')::text$$, pg_temp.u('E')), 'already_banned');
select pg_temp.j('OWN', format($$select admin_lift_sanction(%L, 'ban was too much')::text$$, (select id::text from player_sanctions where player_id = pg_temp.u('E') and kind = 'ban')));
select testkit.assert_eq('the suspension is still in force', (select (banned_until > now() + interval '47 hours' and banned_until < now() + interval '49 hours')::text from auth.users where id = pg_temp.u('E')), 'true');
select pg_temp.pl_fail('E', $$select start_session()::text$$, 'account_suspended');
-- rules that protect staff and bad input
select pg_temp.denied('SUP', 'aal2', format($$select admin_apply_sanction(%L, 'mute', 1, 'testing it')::text$$, pg_temp.u('OPS')), 'cannot_sanction_admin');
select pg_temp.denied('SUP', 'aal2', format($$select admin_apply_sanction(%L, 'mute', 1, 'testing it')::text$$, pg_temp.u('SUP')), 'cannot_sanction_admin');
select pg_temp.denied('SUP', 'aal2', format($$select admin_apply_sanction(%L, 'mute', 1, 'testing it')::text$$, gen_random_uuid()), 'player_not_found');
select pg_temp.denied('SUP', 'aal2', format($$select admin_apply_sanction(%L, 'shadowban', 1, 'testing it')::text$$, pg_temp.u('B')), 'invalid_kind');
select pg_temp.denied('SUP', 'aal2', format($$select admin_apply_sanction(%L, 'mute', 0, 'testing it')::text$$, pg_temp.u('B')), 'invalid_duration');
select pg_temp.denied('SUP', 'aal2', format($$select admin_apply_sanction(%L, 'mute', 2161, 'testing it')::text$$, pg_temp.u('B')), 'invalid_duration');
select pg_temp.denied('SUP', 'aal2', format($$select admin_apply_sanction(%L, 'mute', null, 'testing it')::text$$, pg_temp.u('B')), 'invalid_duration');
select pg_temp.denied('SUP', 'aal2', format($$select admin_apply_sanction(%L, 'mute', 1, '')::text$$, pg_temp.u('B')), 'reason_required');
select pg_temp.denied('SUP', 'aal2', format($$select admin_apply_sanction(%L, 'mute', 1, 'wrong line', %s)::text$$, pg_temp.u('C'), pg_temp.mid('you are all idiots')), 'message_mismatch');
select pg_temp.denied('SUP', 'aal2', format($$select admin_lift_sanction(%L, 'testing it')::text$$, gen_random_uuid()), 'sanction_not_found');
select pg_temp.denied('SUP', 'aal2', $$select admin_list_sanctions('forever')::text$$, 'invalid_state');
select testkit.assert_eq('every sanction is audited', (select count(*)::text from admin_audit_log where action = 'apply_sanction'), (select count(*)::text from player_sanctions));
select testkit.assert_eq('every lift is audited', (select count(*)::text from admin_audit_log where action = 'lift_sanction'), (select count(*)::text from player_sanctions where lifted_at is not null));
do $$ begin
  begin insert into player_sanctions(player_id, kind, reason, created_by, expires_at) values (pg_temp.u('B'), 'ban', 'abc', pg_temp.u('OWN'), now() + interval '1 day');
  exception when check_violation then return; end;
  raise exception 'NOT REJECTED: a ban with an end date';
end $$;

-- ===== 9. nothing about a player's wallet, progress or reward can be reached from here
select testkit.assert_eq('no endpoint of this migration takes a reward, amount or caller identity', (
  select coalesce(string_agg(proname || '(' || a || ')', ','), 'none') from pg_proc p, unnest(p.proargnames) a
   where p.pronamespace = 'public'::regnamespace and p.proname in (select n from calls) and a ~* '(reward|xp|coin|balance|amount|price|cost|distance|p_uid|p_user|p_caller)'), 'none');

select 'ADMIN CONTENT + MODERATION TESTS PASSED' as result;
