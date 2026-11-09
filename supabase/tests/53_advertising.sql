\set ON_ERROR_STOP on
-- Advertising (migration 20261110000001): the role matrix, owner-only approval, the public enquiry form, storage policies, the campaign state machine, placement conflicts,
-- the manifest, exposure aggregates, analytics (measured against estimated), the audit trail and privacy.
create temp table fx(k text primary key, v text);
insert into fx select k, testkit.make_user(n)::text from (values
  ('OWN','AdsOwner'),('ADV','AdsAdvertising'),('ADV2','AdsAdvertising2'),('ANA','AdsAnalyst'),('OPS','AdsOps'),('SUP','AdsSupport'),('CNT','AdsContent'),('P','AdsPlayer')) t(k, n);
create function pg_temp.u(k text) returns uuid language sql as $$ select v::uuid from fx where fx.k = $1 $$;
insert into admin_users(user_id, role) values (pg_temp.u('OWN'), 'owner'), (pg_temp.u('ADV'), 'advertising'), (pg_temp.u('ADV2'), 'advertising'), (pg_temp.u('ANA'), 'analyst'),
  (pg_temp.u('OPS'), 'operations'), (pg_temp.u('SUP'), 'support'), (pg_temp.u('CNT'), 'content');

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
create function pg_temp.j(p_k text, p_sql text) returns jsonb language sql as $$ select pg_temp.as($1, 'aal2', $2)::jsonb $$;
create function pg_temp.denied(p_k text, p_aal text, p_sql text, p_msg text) returns void language plpgsql as $$
begin
  begin perform pg_temp.as(p_k, p_aal, p_sql);
  exception when others then
    if sqlerrm like '%' || p_msg || '%' then return; end if;
    raise exception 'WRONG ERROR for [%] as % (aal %): expected "%", got "%"', p_sql, p_k, p_aal, p_msg, sqlerrm;
  end;
  raise exception 'NOT REJECTED: [%] as % (aal %) should have failed with "%"', p_sql, p_k, p_aal, p_msg;
end $$;
-- run as the anonymous role
create function pg_temp.anon(p_sql text) returns text language plpgsql as $$
declare r text;
begin
  perform set_config('request.jwt.claim.sub', '', true);
  execute 'set local role anon';
  execute p_sql into r;
  execute 'reset role';
  return r;
exception when others then execute 'reset role'; raise;
end $$;
create function pg_temp.anon_denied(p_sql text, p_msg text) returns void language plpgsql as $$
begin
  begin perform pg_temp.anon(p_sql);
  exception when others then
    if sqlerrm like '%' || p_msg || '%' then return; end if;
    raise exception 'WRONG ERROR for [%] as anon: expected "%", got "%"', p_sql, p_msg, sqlerrm;
  end;
  raise exception 'NOT REJECTED as anon: [%] should have failed with "%"', p_sql, p_msg;
end $$;
create function pg_temp.audits(p_action text) returns integer language sql as $$ select count(*)::int from admin_audit_log where action = $1 $$;

-- fixtures the migration deliberately does not ship: two boards with faces, owner-set prices
insert into ad_boards(id, route_code, city, district, road_label, station_m, side, board_type, width_m, height_m, illuminated, status, source) values
  ('test:board:a', 'ng-test', 'Testville', 'Old', 'A2', 1000, 'right', 'roadside', 12.2, 3.0, true, 'available', 'manual'),
  ('test:board:b', 'ng-test', 'Testville', 'New', 'A2', 3000, 'right', 'highway', 14.6, 4.2, true, 'available', 'manual'),
  ('test:board:c', 'ng-other', 'Elsewhere', null, 'B1', 100, 'left', 'urban', 3.0, 6.0, true, 'planned', 'manual');
insert into ad_slots(id, board_id, face, aspect, width_m, height_m) values
  ('test:board:a#1', 'test:board:a', 1, '2:1', 12.2, 3.0), ('test:board:b#1', 'test:board:b', 1, '2:1', 14.6, 4.2), ('test:board:c#1', 'test:board:c', 1, '1:2', 3.0, 6.0);

-- ===== 00. the route inventory seed (generated from the route data): boards start planned, loading it twice changes nothing, and it never overwrites a board the owner changed
select testkit.assert_eq('the seed made boards from the route data', (select (count(*) >= 3)::text from ad_boards where source = 'route_data'), 'true');
select testkit.assert_eq('all of them start planned', (select count(*)::text from ad_boards where source = 'route_data' and status <> 'planned'), '0');
select testkit.assert_eq('each board has one face', (select count(*)::text from ad_boards b where source = 'route_data' and (select count(*) from ad_slots s where s.board_id = b.id) <> 1), '0');
update ad_boards set status = 'available', est_daily_passes = 1234 where id = (select min(id) from ad_boards where source = 'route_data');
\i seed_ads.sql
select testkit.assert_eq('loading the seed again keeps what the owner set', (select status || ':' || est_daily_passes from ad_boards where id = (select min(id) from ad_boards where source = 'route_data')), 'available:1234');
update ad_boards set status = 'planned', est_daily_passes = null where source = 'route_data';

-- ===== 0. what the migration ships: packages without prices, the bucket settings, the surface
select testkit.assert_eq('ten packages ship', (select count(*)::text from ad_packages), '10');
select testkit.assert_eq('NO price is hard coded: the migration ships none', (select count(*)::text from ad_package_prices), '0');
select testkit.assert_eq('the public bucket is public, small', (select public::text || ':' || file_size_limit::text from storage.buckets where id = 'ad-creatives-public'), 'true:153600');
select testkit.assert_eq('the submissions bucket is private, 300 KB', (select public::text || ':' || file_size_limit::text from storage.buckets where id = 'ad-submissions'), 'false:307200');
select testkit.assert_eq('the exposure table holds no player, session or device column',
  (select count(*)::text from information_schema.columns where table_name = 'ad_exposure_daily' and column_name ~* '(player|user|session|device|ip|profile)'), '0');
select testkit.assert_eq('the analytics definitions say measured and estimated are different', (select (pg_temp.j('OWN', $$select admin_ad_analytics(null, null)::text$$) -> 'definitions' ? 'estimated')::text), 'true');

-- ===== 1. who may call what
select testkit.expect_error('anon', null, 'select admin_ad_overview()', 'permission denied');
select testkit.expect_error('anon', null, 'select get_ad_manifest()', 'permission denied');
select testkit.expect_error('anon', null, 'select record_ad_exposure(''[]'')', 'permission denied');
select pg_temp.denied('P', 'aal2', $$select admin_ad_overview()::text$$, 'not_admin');
select pg_temp.denied('P', 'aal2', $$select admin_ad_list_enquiries()::text$$, 'not_admin');
select pg_temp.denied('OWN', 'aal1', $$select admin_ad_overview()::text$$, 'mfa_required');
select pg_temp.denied('OWN', null, $$select admin_ad_list_campaigns()::text$$, 'mfa_required');
select pg_temp.denied('ADV', 'aal1', $$select admin_ad_list_enquiries()::text$$, 'mfa_required');
select pg_temp.denied('OPS', 'aal2', $$select admin_ad_overview()::text$$, 'not_allowed');
select pg_temp.denied('SUP', 'aal2', $$select admin_ad_list_campaigns()::text$$, 'not_allowed');
select pg_temp.denied('CNT', 'aal2', $$select admin_ad_list_creatives()::text$$, 'not_allowed');
select pg_temp.denied('ANA', 'aal2', $$select admin_ad_list_enquiries()::text$$, 'not_allowed');           -- the analyst reads aggregates, not people's contact details
select pg_temp.denied('ANA', 'aal2', $$select admin_ad_list_creatives()::text$$, 'not_allowed');
select testkit.assert_eq('owner reads the overview', (pg_temp.j('OWN', $$select admin_ad_overview()::text$$) ? 'boards')::text, 'true');
select testkit.assert_eq('advertising reads the overview', (pg_temp.j('ADV', $$select admin_ad_overview()::text$$) ? 'boards')::text, 'true');
select testkit.assert_eq('analyst reads the overview', (pg_temp.j('ANA', $$select admin_ad_overview()::text$$) ? 'boards')::text, 'true');
select testkit.assert_eq('analyst reads the analytics', (pg_temp.j('ANA', $$select admin_ad_analytics(null, null)::text$$) ? 'campaigns')::text, 'true');
select pg_temp.denied('OPS', 'aal2', $$select admin_ad_analytics(null, null)::text$$, 'not_allowed');
-- the role can be given by the owner and by no one else
select testkit.assert_eq('the owner can give the advertising role', pg_temp.j('OWN', format($$select admin_set_role(%L, 'advertising', 'a new team member')::text$$, pg_temp.u('P')))->>'role', 'advertising');
select pg_temp.denied('ADV', 'aal2', format($$select admin_set_role(%L, 'owner', 'make me owner')::text$$, pg_temp.u('ADV')), 'not_allowed');
select pg_temp.j('OWN', format($$select admin_set_role(%L, 'none', 'not on the team after all')::text$$, pg_temp.u('P')));

-- ===== 2. OWNER-ONLY publication: nobody else can approve, reject, reprice, terminate, resume, change a board or publish a file
select pg_temp.denied('ADV', 'aal2', $$select admin_ad_review_campaign(gen_random_uuid(), 'approve', true, 'let me approve')::text$$, 'not_allowed');
select pg_temp.denied('ADV', 'aal2', $$select admin_ad_review_creative(gen_random_uuid(), 'approve', true, 'let me approve')::text$$, 'not_allowed');
select pg_temp.denied('ADV', 'aal2', $$select admin_ad_confirm_variants(gen_random_uuid(), '[]', 'let me publish')::text$$, 'not_allowed');
select pg_temp.denied('ADV', 'aal2', $$select admin_ad_set_price('roadside_large', 'NGN', 1, 28, 'cheaper')::text$$, 'not_allowed');
select pg_temp.denied('ADV', 'aal2', $$select admin_ad_save_package('x_pack', 'XX', 'roadside', 'small', '{14}', false, null, true, 'new package')::text$$, 'not_allowed');
select pg_temp.denied('ADV', 'aal2', $$select admin_ad_set_board_status('test:board:a', 'retired', null, 'remove it')::text$$, 'not_allowed');
select pg_temp.denied('ADV', 'aal2', $$select admin_ad_set_payment_status(gen_random_uuid(), 'paid', 'mark paid')::text$$, 'not_allowed');
select pg_temp.denied('ADV', 'aal2', $$select admin_ad_set_campaign_state(gen_random_uuid(), 'terminate', 'end it now')::text$$, 'not_allowed');
select pg_temp.denied('ADV', 'aal2', $$select admin_ad_set_campaign_state(gen_random_uuid(), 'resume', 'back on the road')::text$$, 'not_allowed');
select pg_temp.denied('ANA', 'aal2', $$select admin_ad_review_campaign(gen_random_uuid(), 'approve', true, 'let me approve')::text$$, 'not_allowed');
select pg_temp.denied('OPS', 'aal2', $$select admin_ad_review_creative(gen_random_uuid(), 'approve', true, 'let me approve')::text$$, 'not_allowed');
select pg_temp.denied('OWN', 'aal1', $$select admin_ad_review_campaign(gen_random_uuid(), 'approve', true, 'no second step')::text$$, 'mfa_required');
select pg_temp.denied('OWN', 'aal2', $$select admin_ad_set_price('roadside_large', 'NGN', 1, 28, '')::text$$, 'reason_required');

-- ===== 3. packages and prices are owner data, audited with the old and the new amount
select pg_temp.j('OWN', $$select admin_ad_set_price('roadside_large', 'NGN', 25000000, 28, 'first price for 28 days, set by the owner')::text$$);
select pg_temp.j('OWN', $$select admin_ad_set_price('roadside_large', 'USD', 18000, 28, 'a dollar price is prepared, not used for billing')::text$$);
select testkit.assert_eq('the NGN price is minor units', (select amount_minor::text from ad_package_prices where package_code = 'roadside_large' and currency = 'NGN'), '25000000');
select pg_temp.j('OWN', $$select admin_ad_set_price('roadside_large', 'NGN', 27500000, 28, 'raised after the first month')::text$$);
select testkit.assert_eq('a price change is audited with old and new', (select details ->> 'old_amount_minor' || '>' || (details ->> 'new_amount_minor') from admin_audit_log where action = 'ad_price_set' and details ->> 'currency' = 'NGN' order by id desc limit 1), '25000000>27500000');
select pg_temp.denied('OWN', 'aal2', $$select admin_ad_set_price('roadside_large', 'ngn', 1, 28, 'lower case currency')::text$$, 'invalid_currency');
select pg_temp.denied('OWN', 'aal2', $$select admin_ad_set_price('nope', 'NGN', 1, 28, 'no such package')::text$$, 'package_not_found');
select pg_temp.denied('OWN', 'aal2', $$select admin_ad_set_price('roadside_large', 'NGN', -5, 28, 'negative price')::text$$, 'invalid_amount');
select testkit.assert_eq('listing shows the prices and the available boards', (pg_temp.j('ADV', $$select admin_ad_list_packages()::text$$) -> 'items' -> 0 ->> 'code'), 'commercial_wide');
select pg_temp.j('OWN', $$select admin_ad_save_package('led_special', 'LED special', 'led', 'large', '{7,14}', true, 'a test package', true, 'a new package')::text$$);
select testkit.assert_eq('a new package exists', (select board_type from ad_packages where code = 'led_special'), 'led');
select pg_temp.denied('OWN', 'aal2', $$select admin_ad_save_package('bad code', 'XX', 'roadside', 'small', '{14}', false, null, true, 'bad')::text$$, 'invalid_code');
select pg_temp.denied('OWN', 'aal2', $$select admin_ad_save_package('okcode', 'XX', 'roadside', 'small', '{0}', false, null, true, 'bad')::text$$, 'invalid_durations');

-- ===== 4. the public enquiry form (anonymous): validation, caps, the honeypot, flags, nothing is published
select testkit.assert_eq('anon can request an upload', (pg_temp.anon($$select request_ad_upload('png')::text$$)::jsonb ->> 'bucket'), 'ad-submissions');
select pg_temp.anon_denied($$select request_ad_upload('exe')::text$$, 'invalid_file_type');
create temp table tk(n serial, path text);
insert into tk(path) select pg_temp.anon($$select (request_ad_upload('png')::jsonb ->> 'path')$$);
-- storage: anonymous upload only at a ticketed path
create function pg_temp.put(p_role text, p_uid text, p_bucket text, p_name text, p_bytes bigint, p_mime text) returns void language plpgsql as $$
begin
  perform set_config('request.jwt.claim.sub', coalesce(p_uid, ''), true);
  perform set_config('request.jwt.claim.aal', 'aal2', true);
  execute format('set local role %I', p_role);
  insert into storage.objects(bucket_id, name, metadata) values (p_bucket, p_name, jsonb_build_object('size', p_bytes, 'mimetype', p_mime));
  execute 'reset role';
exception when others then execute 'reset role'; raise;
end $$;
create function pg_temp.put_denied(p_role text, p_uid text, p_bucket text, p_name text, p_what text) returns void language plpgsql as $$
begin
  begin perform pg_temp.put(p_role, p_uid, p_bucket, p_name, 1000, 'image/png');
  exception when others then
    if sqlerrm like '%row-level security%' then return; end if;
    raise exception 'WRONG ERROR for %: %', p_what, sqlerrm;
  end;
  raise exception 'NOT REJECTED: %', p_what;
end $$;
select pg_temp.put_denied('anon', null, 'ad-submissions', 'enquiries/' || gen_random_uuid() || '.png', 'anonymous upload without a ticket');
select pg_temp.put_denied('anon', null, 'ad-submissions', 'admin/sneaky.png', 'anonymous upload at an admin path');
select pg_temp.put_denied('anon', null, 'ad-creatives-public', (select path from tk), 'anonymous upload into the PUBLIC bucket with a valid ticket for the other bucket');
select pg_temp.put('anon', null, 'ad-submissions', (select path from tk), 120000, 'image/png');
select testkit.assert_eq('the anonymous uploader cannot read anything back', pg_temp.anon($$select count(*)::text from storage.objects where bucket_id = 'ad-submissions'$$), '0');

create function pg_temp.enq(p_extra jsonb default '{}') returns jsonb language sql as $$
  select jsonb_build_object('business_name', 'Kano Test Traders', 'contact_name', 'Aminu Test', 'email', 'aminu@example.test', 'phone', '+2348012345678', 'category', 'retail',
    'description', 'We sell household goods in Kano and want a roadside board for a month.', 'preferred_city', 'Kano', 'preferred_placement', 'roadside', 'duration_days', 28,
    'message', 'Please call after 4pm.', 'rights_confirmed', 'true') || p_extra $$;
select testkit.assert_eq('a valid enquiry is received without artwork', pg_temp.anon(format($$select submit_ad_enquiry(%L::jsonb)::text$$, pg_temp.enq()))::jsonb ->> 'status', 'received');
select testkit.assert_eq('it is stored as new, and nothing is published', (select state from ad_enquiries where email = 'aminu@example.test'), 'new');
select testkit.assert_eq('no campaign exists because of it', (select count(*)::text from ad_campaigns), '0');
select pg_temp.anon_denied(format($$select submit_ad_enquiry(%L::jsonb)::text$$, pg_temp.enq('{"email":"not-an-email"}')), 'invalid_email');
select pg_temp.anon_denied(format($$select submit_ad_enquiry(%L::jsonb)::text$$, pg_temp.enq('{"rights_confirmed":"false"}')), 'rights_confirmation_required');
select pg_temp.anon_denied(format($$select submit_ad_enquiry(%L::jsonb)::text$$, pg_temp.enq('{"category":"gambling"}')), 'category_not_allowed');
select pg_temp.anon_denied(format($$select submit_ad_enquiry(%L::jsonb)::text$$, pg_temp.enq('{"category":"tobacco_nicotine"}')), 'category_not_allowed');
select pg_temp.anon_denied(format($$select submit_ad_enquiry(%L::jsonb)::text$$, pg_temp.enq('{"category":"alcohol"}')), 'category_not_allowed');
select pg_temp.anon_denied(format($$select submit_ad_enquiry(%L::jsonb)::text$$, pg_temp.enq('{"category":"adult"}')), 'category_not_allowed');
select pg_temp.anon_denied(format($$select submit_ad_enquiry(%L::jsonb)::text$$, pg_temp.enq('{"category":"weapons"}')), 'category_not_allowed');
select pg_temp.anon_denied(format($$select submit_ad_enquiry(%L::jsonb)::text$$, pg_temp.enq('{"category":"political"}')), 'category_not_allowed');
select pg_temp.anon_denied(format($$select submit_ad_enquiry(%L::jsonb)::text$$, pg_temp.enq('{"category":"astrology"}')), 'invalid_category');
select pg_temp.anon_denied(format($$select submit_ad_enquiry(%L::jsonb)::text$$, pg_temp.enq('{"phone":"call me","whatsapp":null}')), 'invalid_phone');
select pg_temp.anon_denied(format($$select submit_ad_enquiry(%L::jsonb)::text$$, pg_temp.enq('{"duration_days":3}')), 'invalid_duration');
select pg_temp.anon_denied(format($$select submit_ad_enquiry(%L::jsonb)::text$$, pg_temp.enq('{"duration_days":"lots"}')), 'invalid_duration');
select pg_temp.anon_denied(format($$select submit_ad_enquiry(%L::jsonb)::text$$, pg_temp.enq('{"description":"short"}')), 'invalid_description');
select pg_temp.anon_denied(format($$select submit_ad_enquiry(%L::jsonb)::text$$, pg_temp.enq(jsonb_build_object('description', repeat('x', 1200)))), 'invalid_description');
select pg_temp.anon_denied(format($$select submit_ad_enquiry(%L::jsonb)::text$$, pg_temp.enq('{"preferred_placement":"moon"}')), 'invalid_placement');
select pg_temp.anon_denied($$select submit_ad_enquiry('[1,2,3]'::jsonb)::text$$, 'invalid_request');
select pg_temp.anon_denied(format($$select submit_ad_enquiry(%L::jsonb)::text$$, jsonb_build_object('blob', repeat('x', 9000))), 'invalid_request');
select testkit.assert_eq('hostile text is stored as plain text (no angle brackets)', pg_temp.anon(format($$select submit_ad_enquiry(%L::jsonb)::text$$,
  pg_temp.enq('{"business_name":"<script>alert(1)</script> Shop","email":"x1@example.test"}')))::jsonb ->> 'status', 'received');
select testkit.assert_eq('angle brackets are removed from stored text', (select business_name from ad_enquiries where email = 'x1@example.test'), 'scriptalert(1)/script Shop');
-- the honeypot: accepted silently, stored nowhere
select testkit.assert_eq('the honeypot answers like a success', pg_temp.anon(format($$select submit_ad_enquiry(%L::jsonb)::text$$, pg_temp.enq('{"website_url":"http://bots.example","email":"bot@example.test"}')))::jsonb ->> 'status', 'received');
select testkit.assert_eq('and stores nothing', (select count(*)::text from ad_enquiries where email = 'bot@example.test'), '0');
-- per-address cap: three a day
select pg_temp.anon(format($$select submit_ad_enquiry(%L::jsonb)::text$$, pg_temp.enq('{"email":"cap@example.test"}')));
select pg_temp.anon(format($$select submit_ad_enquiry(%L::jsonb)::text$$, pg_temp.enq('{"email":"cap@example.test"}')));
select pg_temp.anon(format($$select submit_ad_enquiry(%L::jsonb)::text$$, pg_temp.enq('{"email":"cap@example.test"}')));
select pg_temp.anon_denied(format($$select submit_ad_enquiry(%L::jsonb)::text$$, pg_temp.enq('{"email":"cap@example.test"}')), 'rate_limited');
-- artwork: needs a ticket, an object, a small size and an image type
select pg_temp.anon_denied(format($$select submit_ad_enquiry(%L::jsonb)::text$$, pg_temp.enq('{"email":"a1@example.test","artwork_path":"enquiries/00000000-0000-0000-0000-000000000000.png"}')), 'invalid_artwork');
select pg_temp.anon_denied(format($$select submit_ad_enquiry(%L::jsonb)::text$$, pg_temp.enq('{"email":"a1@example.test","artwork_path":"../../etc/passwd"}')), 'invalid_artwork');
insert into tk(path) select pg_temp.anon($$select (request_ad_upload('jpg')::jsonb ->> 'path')$$);
select pg_temp.anon_denied(format($$select submit_ad_enquiry(%L::jsonb)::text$$, pg_temp.enq(jsonb_build_object('email', 'a2@example.test', 'artwork_path', (select path from tk order by n desc limit 1)))), 'artwork_not_uploaded');
select pg_temp.put('anon', null, 'ad-submissions', (select path from tk order by n desc limit 1), 400000, 'image/jpeg');
select pg_temp.anon_denied(format($$select submit_ad_enquiry(%L::jsonb)::text$$, pg_temp.enq(jsonb_build_object('email', 'a2@example.test', 'artwork_path', (select path from tk order by n desc limit 1)))), 'artwork_too_large');
insert into tk(path) select pg_temp.anon($$select (request_ad_upload('png')::jsonb ->> 'path')$$);
select pg_temp.put('anon', null, 'ad-submissions', (select path from tk order by n desc limit 1), 90000, 'application/x-msdownload');
select pg_temp.anon_denied(format($$select submit_ad_enquiry(%L::jsonb)::text$$, pg_temp.enq(jsonb_build_object('email', 'a3@example.test', 'artwork_path', (select path from tk order by n desc limit 1)))), 'invalid_artwork');
-- a good one, from the first ticket
select testkit.assert_eq('a valid enquiry with artwork is received', pg_temp.anon(format($$select submit_ad_enquiry(%L::jsonb)::text$$, pg_temp.enq(jsonb_build_object('email', 'art@example.test', 'artwork_path', (select path from tk order by n limit 1)))))::jsonb ->> 'status', 'received');
select testkit.assert_eq('the artwork is recorded', (select artwork_bytes::text from ad_enquiries where email = 'art@example.test'), '120000');
select pg_temp.anon_denied(format($$select submit_ad_enquiry(%L::jsonb)::text$$, pg_temp.enq(jsonb_build_object('email', 'art2@example.test', 'artwork_path', (select path from tk order by n limit 1)))), 'invalid_artwork');   -- a ticket works once
-- policy screening: a flagged enquiry is accepted for review, flagged
select pg_temp.anon(format($$select submit_ad_enquiry(%L::jsonb)::text$$, pg_temp.enq('{"email":"flag@example.test","description":"We run a betting shop and a casino night every Friday, come and win."}')));
select testkit.assert_eq('the screening terms flag it', (select policy_flags::text from ad_enquiries where email = 'flag@example.test'), '{gambling}');
select testkit.assert_eq('the clean enquiry has no flags', (select cardinality(policy_flags)::text from ad_enquiries where email = 'aminu@example.test'), '0');

-- ===== 5. the admin enquiry workflow (advertising prepares; nothing publishes)
select pg_temp.denied('SUP', 'aal2', $$select admin_ad_list_enquiries()::text$$, 'not_allowed');
select testkit.assert_eq('advertising lists enquiries', (pg_temp.j('ADV', $$select admin_ad_list_enquiries('new')::text$$) -> 'items' -> 0 ->> 'state'), 'new');
select testkit.assert_eq('viewing the enquiry list is audited (it holds contact details)', (pg_temp.audits('ad_enquiries_list') >= 1)::text, 'true');
create temp table ids(k text primary key, v text);
insert into ids select 'enq', id::text from ad_enquiries where email = 'aminu@example.test';
insert into ids select 'flag', id::text from ad_enquiries where email = 'flag@example.test';
select pg_temp.denied('ADV', 'aal2', format($$select admin_ad_convert_enquiry(%L, 'Kano Test Traders: January', 'convert it')::text$$, (select v from ids where k = 'enq')), 'enquiry_not_approved');
select pg_temp.denied('ADV', 'aal2', format($$select admin_ad_update_enquiry(%L, 'approved', 'ok', false, 'good fit')::text$$, (select v from ids where k = 'flag')), 'policy_ack_required');
select pg_temp.denied('ADV', 'aal2', format($$select admin_ad_update_enquiry(%L, 'approved', 'ok', false, '')::text$$, (select v from ids where k = 'enq')), 'reason_required');
select pg_temp.denied('ADV', 'aal2', format($$select admin_ad_update_enquiry(%L, 'published', 'ok', false, 'a made up state')::text$$, (select v from ids where k = 'enq')), 'invalid_state');
select testkit.assert_eq('contacted', pg_temp.j('ADV', format($$select admin_ad_update_enquiry(%L, 'contacted', 'Called, wants a month', false, 'spoke to the advertiser')::text$$, (select v from ids where k = 'enq')))->>'state', 'contacted');
select testkit.assert_eq('approved', pg_temp.j('ADV', format($$select admin_ad_update_enquiry(%L, 'approved', null, false, 'brief and category are fine')::text$$, (select v from ids where k = 'enq')))->>'state', 'approved');
select testkit.assert_eq('approving an ENQUIRY does not create an advertisement', (select count(*)::text from ad_campaigns), '0');
select testkit.assert_eq('a flagged enquiry can be approved with the acknowledgement', pg_temp.j('ADV', format($$select admin_ad_update_enquiry(%L, 'approved', null, true, 'sports shop, no betting advert intended; checked')::text$$, (select v from ids where k = 'flag')))->>'state', 'approved');
insert into ids select 'camp', (pg_temp.j('ADV', format($$select admin_ad_convert_enquiry(%L, 'Kano Test Traders: January', 'convert it')::text$$, (select v from ids where k = 'enq')))->>'id');
select testkit.assert_eq('conversion makes a DRAFT campaign', (select state from ad_campaigns where id = (select v::uuid from ids where k = 'camp')), 'draft');
select testkit.assert_eq('and an advertiser', (select business_name from ad_advertisers a join ad_campaigns c on c.advertiser_id = a.id where c.id = (select v::uuid from ids where k = 'camp')), 'Kano Test Traders');
select pg_temp.denied('ADV', 'aal2', format($$select admin_ad_convert_enquiry(%L, 'again', 'convert it again')::text$$, (select v from ids where k = 'enq')), 'already_converted');
select testkit.assert_eq('the enquiry is converted and points at the campaign', (select state || ':' || (campaign_id = (select v::uuid from ids where k = 'camp'))::text from ad_enquiries where id = (select v::uuid from ids where k = 'enq')), 'converted:true');


-- ===== 5b. a campaign for a direct sale (no enquiry): rights must be confirmed, the category policy applies, and it starts as a draft
select pg_temp.denied('SUP', 'aal2', $$select admin_ad_create_campaign('Direct Co', 'Dee Rect', 'dee@example.test', null, 'retail', 'Direct campaign', true, 'direct sale')::text$$, 'not_allowed');
select pg_temp.denied('ADV', 'aal2', $$select admin_ad_create_campaign('Direct Co', 'Dee Rect', 'dee@example.test', null, 'retail', 'Direct campaign', false, 'direct sale')::text$$, 'rights_confirmation_required');
select pg_temp.denied('ADV', 'aal2', $$select admin_ad_create_campaign('Direct Co', 'Dee Rect', 'dee@example.test', null, 'gambling', 'Direct campaign', true, 'direct sale')::text$$, 'category_not_allowed');
select pg_temp.denied('ADV', 'aal2', $$select admin_ad_create_campaign('Direct Co', 'Dee Rect', 'not-an-email', null, 'retail', 'Direct campaign', true, 'direct sale')::text$$, 'invalid_email');
select pg_temp.denied('ADV', 'aal2', $$select admin_ad_create_campaign('Direct Co', 'Dee Rect', 'dee@example.test', null, 'retail', 'Direct campaign', true, '')::text$$, 'reason_required');
select testkit.assert_eq('a direct campaign is a draft', pg_temp.j('ADV', $$select admin_ad_create_campaign('Direct Co', 'Dee Rect', 'dee@example.test', '+2348000000000', 'retail', 'Direct campaign', true, 'direct sale agreed on the phone')::text$$)->>'state', 'draft');
select testkit.assert_eq('with the rights confirmation recorded', (select (rights_confirmed_at is not null)::text from ad_advertisers where email = 'dee@example.test'), 'true');
select testkit.assert_eq('creating a campaign is audited', pg_temp.audits('ad_campaign_create')::text, '1');

-- ===== 6. a campaign, its artwork, its placement, its approval
-- dates relative to the server day (Lagos): start in 2 days, 28 days long
insert into ids select 'start', (_ad_today() + 2)::text;
insert into ids select 'end', (_ad_today() + 29)::text;
select pg_temp.denied('ADV', 'aal2', format($$select admin_ad_save_campaign(%L, 'Kano Test Traders: January', 'roadside_large', %L, %L, true, 'http://insecure.example', 'set the link')::text$$, (select v from ids where k = 'camp'), (select v from ids where k = 'start'), (select v from ids where k = 'end')), 'invalid_url');
select pg_temp.denied('ADV', 'aal2', format($$select admin_ad_save_campaign(%L, 'Kano Test Traders: January', 'roadside_large', %L, %L, true, 'https://user:pass@example.com/x', 'set the link')::text$$, (select v from ids where k = 'camp'), (select v from ids where k = 'start'), (select v from ids where k = 'end')), 'invalid_url');
select pg_temp.denied('ADV', 'aal2', format($$select admin_ad_save_campaign(%L, 'Kano Test Traders: January', 'roadside_large', %L, %L, true, 'https://192.168.1.1/x', 'set the link')::text$$, (select v from ids where k = 'camp'), (select v from ids where k = 'start'), (select v from ids where k = 'end')), 'invalid_url');
select pg_temp.denied('ADV', 'aal2', format($$select admin_ad_save_campaign(%L, 'Kano Test Traders: January', 'roadside_large', %L, %L, true, null, 'dates backwards')::text$$, (select v from ids where k = 'camp'), (select v from ids where k = 'end'), (select v from ids where k = 'start')), 'invalid_schedule');
select pg_temp.denied('ADV', 'aal2', format($$select admin_ad_save_campaign(%L, 'Kano Test Traders: January', 'no_such_package', %L, %L, true, null, 'bad package')::text$$, (select v from ids where k = 'camp'), (select v from ids where k = 'start'), (select v from ids where k = 'end')), 'package_not_found');
select testkit.assert_eq('campaign saved with a sponsored label and a link', pg_temp.j('ADV', format($$select admin_ad_save_campaign(%L, 'Kano Test Traders: January', 'roadside_large', %L, %L, true, 'https://example.com/kano-traders', 'set up the campaign')::text$$, (select v from ids where k = 'camp'), (select v from ids where k = 'start'), (select v from ids where k = 'end')))->>'sponsored_label', 'true');
select testkit.assert_eq('the agreed price is copied from the owner''s price for 28 days (NGN minor units)', (select agreed_currency || ':' || agreed_amount_minor::text from ad_campaigns where id = (select v::uuid from ids where k = 'camp')), 'NGN:27500000');
select pg_temp.j('OWN', $$select admin_ad_set_price('roadside_large', 'NGN', 99900000, 28, 'a later price change')::text$$);
select testkit.assert_eq('a later price change does not rewrite the agreed price', (select agreed_amount_minor::text from ad_campaigns where id = (select v::uuid from ids where k = 'camp')), '27500000');
-- artwork: an admin uploads to the quarantine bucket, then registers it
select pg_temp.put_denied('authenticated', pg_temp.u('ANA')::text, 'ad-submissions', 'admin/analyst.png', 'an analyst upload');
select pg_temp.put_denied('authenticated', pg_temp.u('P')::text, 'ad-submissions', 'admin/player.png', 'a player upload');
select pg_temp.put_denied('authenticated', pg_temp.u('ADV')::text, 'ad-submissions', 'enquiries/' || gen_random_uuid() || '.png', 'an admin upload outside admin/');
select pg_temp.put('authenticated', pg_temp.u('ADV')::text, 'ad-submissions', 'admin/kano-traders-1.png', 80000, 'image/png');
select pg_temp.put('authenticated', pg_temp.u('ADV')::text, 'ad-submissions', 'admin/kano-traders-portrait.png', 70000, 'image/png');
select testkit.assert_eq('a player cannot read the quarantine bucket', (select pg_temp.as('P', 'aal2', $$select count(*)::text from storage.objects where bucket_id = 'ad-submissions'$$)), '0');
select testkit.assert_eq('an analyst cannot read the quarantine bucket', (select pg_temp.as('ANA', 'aal2', $$select count(*)::text from storage.objects where bucket_id = 'ad-submissions'$$)), '0');
select testkit.assert_eq('advertising can read the quarantine bucket', (select (pg_temp.as('ADV', 'aal2', $$select count(*)::text from storage.objects where bucket_id = 'ad-submissions'$$)::int > 0)::text), 'true');
select testkit.assert_eq('advertising at the first step only (aal1) cannot', (select pg_temp.as('ADV', 'aal1', $$select count(*)::text from storage.objects where bucket_id = 'ad-submissions'$$)), '0');
select pg_temp.denied('ADV', 'aal2', format($$select admin_ad_register_creative(%L, 'Banner', '2:1', 'admin/does-not-exist.png', 'register it')::text$$, (select v from ids where k = 'camp')), 'artwork_not_uploaded');
select pg_temp.denied('ADV', 'aal2', format($$select admin_ad_register_creative(%L, 'Banner', '2:1', 'admin/../x.png', 'register it')::text$$, (select v from ids where k = 'camp')), 'invalid_path');
select pg_temp.denied('ADV', 'aal2', format($$select admin_ad_register_creative(%L, 'Banner', '9:9', 'admin/kano-traders-1.png', 'register it')::text$$, (select v from ids where k = 'camp')), 'invalid_aspect');
insert into ids select 'cre', (pg_temp.j('ADV', format($$select admin_ad_register_creative(%L, 'Banner wide', '2:1', 'admin/kano-traders-1.png', 'registered the supplied banner')::text$$, (select v from ids where k = 'camp')))->>'id');
insert into ids select 'cre_p', (pg_temp.j('ADV', format($$select admin_ad_register_creative(%L, 'Banner portrait', '1:2', 'admin/kano-traders-portrait.png', 'registered the portrait banner')::text$$, (select v from ids where k = 'camp')))->>'id');
select testkit.assert_eq('a registered creative waits for the owner', (select status from ad_creatives where id = (select v::uuid from ids where k = 'cre')), 'submitted');
-- placement
select pg_temp.denied('ADV', 'aal2', format($$select admin_ad_assign(%L, 'test:board:c#1', %L, 'reserve the planned board')::text$$, (select v from ids where k = 'camp'), (select v from ids where k = 'cre_p')), 'board_not_available');
select pg_temp.denied('ADV', 'aal2', format($$select admin_ad_assign(%L, 'test:board:a#1', %L, 'wrong aspect')::text$$, (select v from ids where k = 'camp'), (select v from ids where k = 'cre_p')), 'aspect_mismatch');
select pg_temp.denied('ADV', 'aal2', format($$select admin_ad_assign(%L, 'nope#1', %L, 'no such slot')::text$$, (select v from ids where k = 'camp'), (select v from ids where k = 'cre')), 'slot_not_found');
select pg_temp.denied('ADV', 'aal2', format($$select admin_ad_submit_campaign(%L, 'submit it')::text$$, (select v from ids where k = 'camp')), 'placement_required');
select testkit.assert_eq('a face is reserved for the campaign dates', pg_temp.j('ADV', format($$select admin_ad_assign(%L, 'test:board:a#1', %L, 'reserve the roadside board for the month')::text$$, (select v from ids where k = 'camp'), (select v from ids where k = 'cre')))->>'slot_id', 'test:board:a#1');
-- a second campaign cannot take the same face at the same time
insert into ad_advertisers(id, business_name, contact_name, email, category) values ('00000000-0000-0000-0000-0000000000a2', 'Rival Ltd', 'Rita Rival', 'r@example.test', 'retail');
insert into ad_campaigns(id, advertiser_id, title, created_by, start_date, end_date) values ('00000000-0000-0000-0000-0000000000c2', '00000000-0000-0000-0000-0000000000a2', 'Rival', pg_temp.u('ADV'), _ad_today() + 10, _ad_today() + 20);
insert into ad_creatives(id, advertiser_id, campaign_id, name, aspect, submission_path, submission_mime, submission_bytes, created_by)
  values ('00000000-0000-0000-0000-0000000000d2', '00000000-0000-0000-0000-0000000000a2', '00000000-0000-0000-0000-0000000000c2', 'Rival art', '2:1', 'admin/rival.png', 'image/png', 1000, pg_temp.u('ADV'));
select pg_temp.denied('ADV', 'aal2', $$select admin_ad_assign('00000000-0000-0000-0000-0000000000c2', 'test:board:a#1', '00000000-0000-0000-0000-0000000000d2', 'try the same face')::text$$, 'slot_conflict');
select testkit.assert_eq('a different face is free for the rival', (pg_temp.j('ADV', $$select admin_ad_assign('00000000-0000-0000-0000-0000000000c2', 'test:board:b#1', '00000000-0000-0000-0000-0000000000d2', 'the highway board instead')::text$$)->>'slot_id'), 'test:board:b#1');
-- and the constraint holds even if a function were bypassed
do $$ begin begin insert into ad_assignments(campaign_id, slot_id, creative_id, start_date, end_date, created_by) values
  ((select v::uuid from ids where k = 'camp'), 'test:board:b#1', (select v::uuid from ids where k = 'cre'), _ad_today() + 12, _ad_today() + 14, pg_temp.u('ADV')); raise exception 'overlap accepted by the table';
  exception when exclusion_violation then null; end; end $$;
-- a released placement frees the face
select testkit.assert_eq('release frees the face', pg_temp.j('ADV', format($$select admin_ad_unassign(%L, 'the rival picked another face')::text$$, (select id::text from ad_assignments where campaign_id = '00000000-0000-0000-0000-0000000000c2' limit 1)))->>'released', 'true');
select testkit.assert_eq('the rival can take it later', (pg_temp.j('ADV', $$select admin_ad_assign('00000000-0000-0000-0000-0000000000c2', 'test:board:b#1', '00000000-0000-0000-0000-0000000000d2', 'back to the highway board')::text$$)->>'slot_id'), 'test:board:b#1');
-- submit, then everything is locked for the preparers
select testkit.assert_eq('submitted for review', pg_temp.j('ADV', format($$select admin_ad_submit_campaign(%L, 'ready for the owner')::text$$, (select v from ids where k = 'camp')))->>'state', 'in_review');
select pg_temp.denied('ADV', 'aal2', format($$select admin_ad_save_campaign(%L, 'Edited after submit', 'roadside_large', %L, %L, true, null, 'try to edit')::text$$, (select v from ids where k = 'camp'), (select v from ids where k = 'start'), (select v from ids where k = 'end')), 'campaign_locked');
select pg_temp.denied('ADV', 'aal2', format($$select admin_ad_assign(%L, 'test:board:b#1', %L, 'add a face after submit')::text$$, (select v from ids where k = 'camp'), (select v from ids where k = 'cre')), 'campaign_locked');
select pg_temp.denied('ADV', 'aal2', format($$select admin_ad_register_creative(%L, 'Late art', '2:1', 'admin/kano-traders-1.png', 'late art')::text$$, (select v from ids where k = 'camp')), 'campaign_locked');
-- the owner cannot approve a campaign whose artwork is not approved and published
select pg_temp.denied('OWN', 'aal2', format($$select admin_ad_review_campaign(%L, 'approve', false, 'looks good')::text$$, (select v from ids where k = 'camp')), 'creative_not_ready');
-- creative approval: owner only; nothing is public yet
select pg_temp.denied('OWN', 'aal2', format($$select admin_ad_confirm_variants(%L, '[]', 'publish the files')::text$$, (select v from ids where k = 'cre')), 'creative_not_approved');
select testkit.assert_eq('the owner approves the artwork', pg_temp.j('OWN', format($$select admin_ad_review_creative(%L, 'approve', false, 'on brief, no brand problems')::text$$, (select v from ids where k = 'cre')))->>'status', 'approved');
select testkit.assert_eq('the public paths are fixed by the approval', (select expected_paths ->> 0 from ad_creatives where id = (select v::uuid from ids where k = 'cre')), 'creatives/' || (select v from ids where k = 'cre') || '/2x1_v1.jpg');
select testkit.assert_eq('and it is still not public', (select public_ready::text from ad_creatives where id = (select v::uuid from ids where k = 'cre')), 'false');
select testkit.assert_eq('a rejected creative stays out', pg_temp.j('OWN', format($$select admin_ad_review_creative(%L, 'reject', false, 'the portrait banner is off brand')::text$$, (select v from ids where k = 'cre_p')))->>'status', 'rejected');
select pg_temp.denied('OWN', 'aal2', format($$select admin_ad_review_creative(%L, 'approve', false, 'second decision')::text$$, (select v from ids where k = 'cre')), 'invalid_transition');
-- the PUBLIC bucket: owner only, only at a path an approved creative expects
select pg_temp.put_denied('authenticated', pg_temp.u('ADV')::text, 'ad-creatives-public', 'creatives/' || (select v from ids where k = 'cre') || '/2x1_v1.jpg', 'advertising publishing a file');
select pg_temp.put_denied('authenticated', pg_temp.u('ANA')::text, 'ad-creatives-public', 'creatives/' || (select v from ids where k = 'cre') || '/2x1_v1.jpg', 'analyst publishing a file');
select pg_temp.put_denied('authenticated', pg_temp.u('P')::text, 'ad-creatives-public', 'creatives/' || (select v from ids where k = 'cre') || '/2x1_v1.jpg', 'player publishing a file');
select pg_temp.put_denied('anon', null, 'ad-creatives-public', 'creatives/' || (select v from ids where k = 'cre') || '/2x1_v1.jpg', 'anon publishing a file');
select pg_temp.put_denied('authenticated', pg_temp.u('OWN')::text, 'ad-creatives-public', 'creatives/' || (select v from ids where k = 'cre_p') || '/1x2_v1.jpg', 'the owner publishing a REJECTED creative');
-- a creative that WAS approved and has since been withdrawn keeps its expected paths in the row, but the public bucket takes nothing more for it
insert into ad_creatives(id, advertiser_id, campaign_id, name, aspect, status, submission_path, submission_mime, submission_bytes, expected_paths, created_by)
  values ('00000000-0000-0000-0000-0000000000d3', '00000000-0000-0000-0000-0000000000a2', '00000000-0000-0000-0000-0000000000c2', 'Withdrawn art', '2:1', 'withdrawn', 'admin/withdrawn.png', 'image/png', 1000, '["creatives/withdrawn/2x1_v1.jpg"]', pg_temp.u('ADV'));
select pg_temp.put_denied('authenticated', pg_temp.u('OWN')::text, 'ad-creatives-public', 'creatives/withdrawn/2x1_v1.jpg', 'the owner publishing for a WITHDRAWN creative');
select pg_temp.put_denied('authenticated', pg_temp.u('OWN')::text, 'ad-creatives-public', 'creatives/anything-else.webp', 'the owner publishing at an unexpected path');
select pg_temp.put_denied('authenticated', pg_temp.u('OWN')::text, 'ad-creatives-public', 'creatives/' || gen_random_uuid() || '/2x1_v1.jpg', 'the owner publishing for a creative that does not exist');
select pg_temp.put('authenticated', pg_temp.u('OWN')::text, 'ad-creatives-public', 'creatives/' || (select v from ids where k = 'cre') || '/2x1_v1.jpg', 60000, 'image/jpeg');
-- confirm: sizes, types and hashes are checked, then the creative is public_ready
select pg_temp.denied('OWN', 'aal2', format($$select admin_ad_confirm_variants(%L, '[{"path":"creatives/other.webp","width":1024,"height":512,"sha256":"%s"}]', 'wrong path')::text$$, (select v from ids where k = 'cre'), repeat('a', 64)), 'unexpected_path');
select pg_temp.denied('OWN', 'aal2', format($$select admin_ad_confirm_variants(%L, '[{"path":"creatives/%s/2x1_v1.jpg","width":1024,"height":512,"sha256":"nothex"}]', 'bad hash')::text$$, (select v from ids where k = 'cre'), (select v from ids where k = 'cre')), 'invalid_hash');
select pg_temp.denied('OWN', 'aal2', format($$select admin_ad_confirm_variants(%L, '[{"path":"creatives/%s/2x1_v1.jpg","width":9999,"height":512,"sha256":"%s"}]', 'bad size')::text$$, (select v from ids where k = 'cre'), (select v from ids where k = 'cre'), repeat('a', 64)), 'invalid_dimensions');
select testkit.assert_eq('the owner confirms the variant', pg_temp.j('OWN', format($$select admin_ad_confirm_variants(%L, '[{"path":"creatives/%s/2x1_v1.jpg","width":1024,"height":512,"sha256":"%s"}]', 'uploaded the 1024x512 variant')::text$$, (select v from ids where k = 'cre'), (select v from ids where k = 'cre'), repeat('b', 64)))->>'public_ready', 'true');
-- the manifest is empty until the owner approves the CAMPAIGN
select testkit.assert_eq('nothing is in the manifest before the campaign is approved', jsonb_array_length(pg_temp.j('P', $$select get_ad_manifest()::text$$) -> 'items')::text, '0');
select pg_temp.denied('ADV', 'aal2', format($$select admin_ad_review_campaign(%L, 'approve', false, 'let me approve')::text$$, (select v from ids where k = 'camp')), 'not_allowed');
select pg_temp.denied('OWN', 'aal2', format($$select admin_ad_review_campaign(%L, 'approve', false, '')::text$$, (select v from ids where k = 'camp')), 'reason_required');
select pg_temp.denied('OWN', 'aal2', format($$select admin_ad_review_campaign(%L, 'maybe', false, 'undecided')::text$$, (select v from ids where k = 'camp')), 'invalid_decision');
select testkit.assert_eq('the owner approves the campaign', pg_temp.j('OWN', format($$select admin_ad_review_campaign(%L, 'approve', false, 'brand, claims and dates checked')::text$$, (select v from ids where k = 'camp')))->>'state', 'approved');
select testkit.assert_eq('it is scheduled, not live, before its start date', (pg_temp.j('OWN', $$select admin_ad_list_campaigns('scheduled')::text$$) -> 'items' -> 0 ->> 'effective_state'), 'scheduled');
select testkit.assert_eq('so the manifest still shows nothing', jsonb_array_length(pg_temp.j('P', $$select get_ad_manifest()::text$$) -> 'items')::text, '0');
-- time passes: the start date arrives (the test moves the dates, as a clock would)
update ad_campaigns set start_date = _ad_today() - 1, end_date = _ad_today() + 5 where id = (select v::uuid from ids where k = 'camp');
update ad_assignments set start_date = _ad_today() - 1, end_date = _ad_today() + 5 where campaign_id = (select v::uuid from ids where k = 'camp');
select testkit.assert_eq('on its dates the campaign is live', (pg_temp.j('OWN', $$select admin_ad_list_campaigns('live')::text$$) -> 'items' -> 0 ->> 'effective_state'), 'live');
create temp table mf(v text);
insert into mf select pg_temp.as('P', null, $$select get_ad_manifest('ng-test')::text$$);
select testkit.assert_eq('the manifest carries exactly the live placement', (jsonb_array_length((select v::jsonb from mf) -> 'items'))::text, '1');
select testkit.assert_eq('with the sponsored label', ((select v::jsonb from mf) -> 'items' -> 0 ->> 'label'), 'Sponsored');
select testkit.assert_eq('the image path and hash', ((select v::jsonb from mf) -> 'items' -> 0 ->> 'sha256'), repeat('b', 64));
select testkit.assert_eq('on the right board', ((select v::jsonb from mf) -> 'items' -> 0 ->> 'board_id'), 'test:board:a');
select testkit.assert_eq('another route sees nothing', jsonb_array_length(pg_temp.j('P', $$select get_ad_manifest('ng-other')::text$$) -> 'items')::text, '0');
select testkit.assert_eq('an unchanged manifest costs a few bytes', (pg_temp.j('P', format($$select get_ad_manifest('ng-test', %L)::text$$, (select v::jsonb ->> 'version' from mf)))->>'unchanged'), 'true');
select pg_temp.denied('P', 'aal2', $$select get_ad_manifest('Bad Route!')::text$$, 'invalid_route');
-- the manifest never carries pending, rejected or other people's files: only creatives that are approved AND public_ready
select testkit.assert_eq('the rival (not approved) is not in the manifest', (((select v::jsonb from mf) -> 'items')::text like '%rival%')::text, 'false');
-- exposure: measured, aggregated, no identity
select testkit.assert_eq('a batch is accepted for the live face', pg_temp.j('P', $$select record_ad_exposure('[{"slot_id":"test:board:a#1","seconds":12.5,"views":1,"loaded":1,"clicks":0}]')::text$$)->>'accepted', '1');
select testkit.assert_eq('a face that shows nothing live is ignored', pg_temp.j('P', $$select record_ad_exposure('[{"slot_id":"test:board:b#1","seconds":30,"views":1,"loaded":1}]')::text$$)->>'ignored', '1');
select testkit.assert_eq('absurd seconds are clamped to 120', (select (pg_temp.j('P', $$select record_ad_exposure('[{"slot_id":"test:board:a#1","seconds":99999,"views":99,"loaded":9,"clicks":9}]')::text$$)->>'accepted')), '1');
select testkit.assert_eq('the day row holds at most 12.5 + 120 seconds and 1 + 5 views', (select measured_seconds::text || ':' || measured_views::text from ad_exposure_daily where slot_id = 'test:board:a#1'), '132.5:6');
select pg_temp.denied('P', 'aal2', $$select record_ad_exposure('{"slot_id":"x"}')::text$$, 'invalid_batch');
select pg_temp.denied('P', 'aal2', format($$select record_ad_exposure(%L)::text$$, (select jsonb_agg(jsonb_build_object('slot_id', 'test:board:a#1', 'seconds', 1)) from generate_series(1, 41))), 'invalid_batch');
select testkit.assert_eq('a malformed entry is ignored, the others count', pg_temp.j('P', $$select record_ad_exposure('[{"slot_id":"test:board:a#1","seconds":"lots"},{"slot_id":"test:board:a#1","seconds":3}]')::text$$)->>'accepted', '1');
-- analytics: measured and estimated are separate, estimated is only what the owner entered
select pg_temp.j('OWN', $$select admin_ad_set_board_status('test:board:a', 'available', 4000, 'the owner''s estimate of daily passes')::text$$);
create function pg_temp.kano_row() returns jsonb language sql as $$ select e from jsonb_array_elements(pg_temp.j('ADV', 'select admin_ad_analytics(null, null)::text') -> 'campaigns') e where e ->> 'title' like 'Kano Test Traders%' $$;
select testkit.assert_eq('measured seconds', (pg_temp.kano_row() ->> 'measured_seconds')::numeric::text, '135.5');
select testkit.assert_eq('estimated passes are the owner''s estimate times the days live inside the range (2 days up to today, 4000 a day)', (pg_temp.kano_row() ->> 'estimated_passes'), '8000');
select testkit.assert_eq('clicks are reported as not supported yet', (pg_temp.kano_row() ->> 'clicks_supported'), 'false');
select pg_temp.denied('ADV', 'aal2', $$select admin_ad_analytics(current_date, current_date - 5)::text$$, 'invalid_range');
select pg_temp.denied('P', 'aal2', $$select admin_ad_analytics(null, null)::text$$, 'not_admin');

-- ===== 7. pause, resume, terminate
select testkit.assert_eq('advertising may pause', pg_temp.j('ADV', format($$select admin_ad_set_campaign_state(%L, 'pause', 'the advertiser asked to stop for a week')::text$$, (select v from ids where k = 'camp')))->>'state', 'paused');
select testkit.assert_eq('a paused campaign leaves the manifest at once', jsonb_array_length(pg_temp.j('P', $$select get_ad_manifest('ng-test')::text$$) -> 'items')::text, '0');
select pg_temp.denied('ADV', 'aal2', format($$select admin_ad_set_campaign_state(%L, 'resume', 'put it back')::text$$, (select v from ids where k = 'camp')), 'not_allowed');
select testkit.assert_eq('only the owner resumes', pg_temp.j('OWN', format($$select admin_ad_set_campaign_state(%L, 'resume', 'the week is over')::text$$, (select v from ids where k = 'camp')))->>'state', 'approved');
select testkit.assert_eq('and it is back', jsonb_array_length(pg_temp.j('P', $$select get_ad_manifest('ng-test')::text$$) -> 'items')::text, '1');
select pg_temp.denied('OWN', 'aal2', format($$select admin_ad_set_campaign_state(%L, 'resume', 'not paused')::text$$, (select v from ids where k = 'camp')), 'invalid_transition');
update ad_assignments set end_date = _ad_today() - 1, start_date = _ad_today() - 3 where campaign_id = (select v::uuid from ids where k = 'camp');
update ad_campaigns set end_date = _ad_today() - 1, start_date = _ad_today() - 3 where id = (select v::uuid from ids where k = 'camp');
select testkit.assert_eq('after its last day it is ended and gone from the manifest', jsonb_array_length(pg_temp.j('P', $$select get_ad_manifest('ng-test')::text$$) -> 'items')::text || ':' || (select _ad_effective_state(c) from ad_campaigns c where id = (select v::uuid from ids where k = 'camp')), '0:ended');
update ad_assignments set end_date = _ad_today() + 5, start_date = _ad_today() - 1 where campaign_id = (select v::uuid from ids where k = 'camp');
update ad_campaigns set end_date = _ad_today() + 5, start_date = _ad_today() - 1 where id = (select v::uuid from ids where k = 'camp');
select testkit.assert_eq('the owner terminates', pg_temp.j('OWN', format($$select admin_ad_set_campaign_state(%L, 'terminate', 'the advertiser broke the terms')::text$$, (select v from ids where k = 'camp')))->>'state', 'terminated');
select testkit.assert_eq('termination releases its placements', (select count(*)::text from ad_assignments where campaign_id = (select v::uuid from ids where k = 'camp') and status = 'reserved'), '0');
select testkit.assert_eq('and it is gone from the manifest', jsonb_array_length(pg_temp.j('P', $$select get_ad_manifest('ng-test')::text$$) -> 'items')::text, '0');
select pg_temp.denied('OWN', 'aal2', format($$select admin_ad_set_campaign_state(%L, 'resume', 'bring it back')::text$$, (select v from ids where k = 'camp')), 'invalid_transition');
select testkit.assert_eq('payment status is a manual field only the owner sets', pg_temp.j('OWN', format($$select admin_ad_set_payment_status(%L, 'invoiced', 'invoice sent by e-mail')::text$$, (select v from ids where k = 'camp')))->>'payment_status', 'invoiced');
select pg_temp.denied('OWN', 'aal2', format($$select admin_ad_set_payment_status(%L, 'charged', 'a made up status')::text$$, (select v from ids where k = 'camp')), 'invalid_status');
-- a rejection path: the rival's campaign goes to review and the owner rejects it; the preparers can then edit it again
select testkit.assert_eq('the rival is submitted', pg_temp.j('ADV', $$select admin_ad_submit_campaign('00000000-0000-0000-0000-0000000000c2', 'ready for the owner')::text$$)->>'state', 'in_review');
select testkit.assert_eq('the owner rejects it', pg_temp.j('OWN', $$select admin_ad_review_campaign('00000000-0000-0000-0000-0000000000c2', 'reject', false, 'the brief is not clear enough yet')::text$$)->>'state', 'rejected');
select testkit.assert_eq('a rejected campaign can be edited again', pg_temp.j('ADV', $$select admin_ad_save_campaign('00000000-0000-0000-0000-0000000000c2', 'Rival, second try', null, null, null, true, null, 'rewriting the brief')::text$$)->>'state', 'draft');
select pg_temp.denied('ADV', 'aal2', $$select admin_ad_submit_campaign('00000000-0000-0000-0000-0000000000c2', 'submit without dates')::text$$, 'schedule_required');

-- ===== 8. boards
select testkit.assert_eq('the owner sets a board status', pg_temp.j('OWN', $$select admin_ad_set_board_status('test:board:c', 'available', null, 'now installed')::text$$)->>'status', 'available');
select pg_temp.denied('OWN', 'aal2', $$select admin_ad_set_board_status('test:board:c', 'broken', null, 'bad status')::text$$, 'invalid_status');
select pg_temp.denied('OWN', 'aal2', $$select admin_ad_set_board_status('nope', 'available', null, 'no such board')::text$$, 'board_not_found');
select testkit.assert_eq('the board list shows faces and occupancy', (pg_temp.j('ADV', $$select admin_ad_list_boards('available')::text$$) -> 'items' -> 0 -> 'slots' -> 0 ? 'occupied_now')::text, 'true');

-- ===== 9. the audit trail: every publication-relevant action, with a reason
select testkit.assert_eq('creative approval audited', pg_temp.audits('ad_creative_approve')::text, '1');
select testkit.assert_eq('creative rejection audited', pg_temp.audits('ad_creative_reject')::text, '1');
select testkit.assert_eq('campaign approval audited', pg_temp.audits('ad_campaign_approve')::text, '1');
select testkit.assert_eq('termination audited', pg_temp.audits('ad_campaign_terminate')::text, '1');
select testkit.assert_eq('pause and resume audited', pg_temp.audits('ad_campaign_pause')::text || pg_temp.audits('ad_campaign_resume')::text, '11');
select testkit.assert_eq('price changes audited (three NGN/USD sets)', (pg_temp.audits('ad_price_set') >= 4)::text, 'true');
select testkit.assert_eq('assignment audited', (pg_temp.audits('ad_assign') >= 2)::text, 'true');
select testkit.assert_eq('publishing variants audited', pg_temp.audits('ad_creative_publish_variants')::text, '1');
select testkit.assert_eq('the approval row names the approver as the owner and carries the reason', (select admin_role::text || ':' || reason from admin_audit_log where action = 'ad_campaign_approve'), 'owner:brand, claims and dates checked');
select testkit.assert_eq('no advertising audit row was written by anyone but a role allowed to do it', (select count(*)::text from admin_audit_log where action in ('ad_campaign_approve', 'ad_campaign_reject', 'ad_creative_approve', 'ad_creative_reject', 'ad_price_set', 'ad_campaign_terminate') and admin_role <> 'owner'), '0');
do $$ begin begin update admin_audit_log set reason = 'edited' where action = 'ad_campaign_approve'; raise exception 'audit row edited';
  exception when others then if sqlerrm <> 'audit_is_append_only' then raise; end if; end; end $$;

-- ===== 10. no client can write a table; storage helpers are booleans
select testkit.expect_error('authenticated', pg_temp.u('ADV'), 'insert into ad_campaigns(advertiser_id, title, created_by) values (gen_random_uuid(), ''x'', gen_random_uuid())', 'permission denied');
select testkit.expect_error('authenticated', pg_temp.u('OWN'), 'update ad_creatives set status = ''approved''', 'permission denied');
select testkit.expect_error('authenticated', pg_temp.u('OWN'), 'select * from ad_enquiries', 'permission denied');
select testkit.expect_error('anon', null, 'select * from ad_enquiries', 'permission denied');
select testkit.expect_error('anon', null, 'insert into ad_enquiries(business_name) values (''x'')', 'permission denied');
select testkit.expect_error('anon', null, 'select * from ad_package_prices', 'permission denied');
select testkit.assert_eq('the storage helper is false for a stranger', pg_temp.anon($$select _ad_is_owner()::text$$), 'false');
select testkit.assert_eq('the storage helper is true for the owner at the second step', pg_temp.as('OWN', 'aal2', $$select _ad_is_owner()::text$$), 'true');
select testkit.assert_eq('and false at the first step', pg_temp.as('OWN', 'aal1', $$select _ad_is_owner()::text$$), 'false');

select 'ADVERTISING TESTS PASSED' as result;
