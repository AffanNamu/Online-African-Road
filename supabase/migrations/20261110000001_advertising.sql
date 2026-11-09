-- Advertising (docs/ADVERTISING_SYSTEM.md): billboard inventory, packages and prices as data, public advertiser enquiries, creatives with an owner-only approval, campaigns and placements
-- that cannot overlap, the manifest the game reads, aggregated exposure analytics, and the two storage buckets. Authorised by the owner on 2026-11-09 (phase A1 to A5).
-- Additive: new tables, new functions, one new admin role ('advertising'), admin_set_role re-created to accept it, two storage buckets and their policies. Nothing existing is changed.
--
-- Who may do what (enforced HERE, in the database, never by hiding a button):
--   * owner        everything; the ONLY role that approves or rejects a creative or a campaign (the only way an advertisement becomes publishable), activates it again after a pause,
--                  terminates it, changes a package, a price or a board's status, sets the manual payment status, and uploads to the public bucket.
--   * advertising  prepares: reviews enquiries, converts them to draft campaigns, edits drafts, submits them for review, registers submitted artwork, reserves placements for a draft
--                  campaign, pauses a live one. Cannot approve, reject, terminate, resume, change prices or publish anything.
--   * analyst      reads the overview and the aggregated analytics.
--   * every other role, players and anonymous callers: nothing (except the three public functions below).
-- Public (clients): submit_ad_enquiry and request_ad_upload (anonymous, strictly validated, capped without any identity), get_ad_manifest and record_ad_exposure (signed-in players).
-- Nothing here collects payment: no provider, no checkout, no billing; prices are rows (minor units + ISO 4217 code), payment_status is a manual field only the owner sets.
-- Privacy: exposure is stored as daily aggregates per slot and campaign. No player id, no session id, no device id is stored with it; nothing links two sessions of a player.
create schema if not exists extensions;
create extension if not exists btree_gist with schema extensions;      -- for the "no two placements on one face at the same time" constraint; its functions live outside public
create temp table _fn_before as select oid from pg_proc where pronamespace = 'public'::regnamespace;

alter type admin_role add value if not exists 'advertising';

-- ================================================================ tables
create table ad_packages (
  code        text primary key check (code ~ '^[a-z0-9_]{2,40}$'),
  name        text not null check (char_length(name) between 2 and 80),
  board_type  text not null check (board_type in ('roadside', 'highway', 'urban', 'commercial', 'flyover', 'terminal', 'wall', 'pylon', 'led', 'promo')),
  size_class  text not null check (size_class in ('small', 'medium', 'large', 'xl')),
  durations   integer[] not null default '{14,28,56}' check (cardinality(durations) between 1 and 8),
  digital     boolean not null default false,
  description text check (description is null or char_length(description) <= 300),
  active      boolean not null default true,
  created_at  timestamptz not null default now(),
  updated_at  timestamptz not null default now()
);
-- A price is a row: nothing is hard coded anywhere. NGN first; any other ISO 4217 currency (USD is prepared) is just another row. amount_minor is per period_days.
create table ad_package_prices (
  package_code text not null references ad_packages(code) on delete cascade,
  currency     text not null check (currency ~ '^[A-Z]{3}$'),
  amount_minor bigint not null check (amount_minor >= 0 and amount_minor <= 100000000000),
  period_days  integer not null default 28 check (period_days between 1 and 365),
  updated_by   uuid,
  updated_at   timestamptz not null default now(),
  primary key (package_code, currency)
);
create table ad_boards (
  id          text primary key check (id ~ '^[A-Za-z0-9_:.-]{3,80}$'),
  route_code  text check (route_code is null or route_code ~ '^[a-z0-9-]{2,60}$'),
  city        text check (city is null or char_length(city) <= 60),
  district    text check (district is null or char_length(district) <= 60),
  road_label  text check (road_label is null or char_length(road_label) <= 80),
  station_m   numeric check (station_m is null or station_m >= 0),
  side        text not null default 'right' check (side in ('left', 'right', 'both', 'none')),
  board_type  text not null check (board_type in ('roadside', 'highway', 'urban', 'commercial', 'flyover', 'terminal', 'wall', 'pylon', 'led', 'promo')),
  width_m     numeric not null check (width_m > 0 and width_m <= 40),
  height_m    numeric not null check (height_m > 0 and height_m <= 20),
  illuminated boolean not null default false,
  digital     boolean not null default false,
  status      text not null default 'planned' check (status in ('planned', 'available', 'maintenance', 'retired')),
  est_daily_passes integer check (est_daily_passes is null or est_daily_passes between 0 and 10000000),   -- an ESTIMATE entered by the owner; never shown as a measurement
  source      text not null default 'manual' check (source in ('route_data', 'manual')),
  created_at  timestamptz not null default now(),
  updated_at  timestamptz not null default now()
);
create table ad_slots (
  id        text primary key check (id ~ '^[A-Za-z0-9_:.#-]{3,90}$'),
  board_id  text not null references ad_boards(id) on delete cascade,
  face      integer not null check (face between 1 and 4),
  aspect    text not null check (aspect in ('2:1', '3:1', '1:2', '16:9', '5:3', '1:1')),
  width_m   numeric not null check (width_m > 0 and width_m <= 40),
  height_m  numeric not null check (height_m > 0 and height_m <= 20),
  unique (board_id, face)
);
create table ad_advertisers (
  id            uuid primary key default gen_random_uuid(),
  business_name text not null check (char_length(business_name) between 2 and 120),
  contact_name  text not null check (char_length(contact_name) between 2 and 80),
  email         text not null check (email ~* '^[^@\s]+@[^@\s]+\.[^@\s]+$' and char_length(email) <= 120),
  phone         text check (phone is null or char_length(phone) <= 30),
  category      text not null,
  website       text check (website is null or char_length(website) <= 300),
  rights_confirmed_at timestamptz,
  created_at    timestamptz not null default now()
);
create table ad_enquiries (
  id            uuid primary key default gen_random_uuid(),
  created_at    timestamptz not null default now(),
  business_name text not null check (char_length(business_name) between 2 and 120),
  contact_name  text not null check (char_length(contact_name) between 2 and 80),
  email         text not null check (email ~* '^[^@\s]+@[^@\s]+\.[^@\s]+$' and char_length(email) <= 120),
  phone         text check (phone is null or char_length(phone) <= 30),
  whatsapp      text check (whatsapp is null or char_length(whatsapp) <= 30),
  category      text not null,
  description   text not null check (char_length(description) between 10 and 1000),
  preferred_city text check (preferred_city is null or char_length(preferred_city) <= 60),
  preferred_placement text check (preferred_placement is null or preferred_placement in ('roadside', 'highway', 'urban', 'commercial', 'flyover', 'terminal', 'wall', 'pylon', 'led', 'promo')),
  duration_days integer check (duration_days is null or duration_days between 7 and 365),
  message       text check (message is null or char_length(message) <= 1000),
  artwork_path  text,
  artwork_bytes integer check (artwork_bytes is null or artwork_bytes between 1 and 307200),
  artwork_mime  text check (artwork_mime is null or artwork_mime in ('image/jpeg', 'image/png', 'image/webp')),
  rights_confirmed boolean not null check (rights_confirmed),
  policy_flags  text[] not null default '{}',
  state         text not null default 'new' check (state in ('new', 'contacted', 'approved', 'rejected', 'converted')),
  notes         text check (notes is null or char_length(notes) <= 1000),
  handled_by    uuid,
  handled_at    timestamptz,
  campaign_id   uuid
);
create index ad_enquiries_state_idx on ad_enquiries(state, created_at desc);
create index ad_enquiries_email_idx on ad_enquiries(lower(email), created_at desc);
create table ad_upload_tickets (
  path       text primary key,
  created_at timestamptz not null default now(),
  expires_at timestamptz not null,
  used_at    timestamptz
);
create table ad_campaigns (
  id           uuid primary key default gen_random_uuid(),
  advertiser_id uuid not null references ad_advertisers(id),
  enquiry_id   uuid references ad_enquiries(id),
  title        text not null check (char_length(title) between 2 and 100),
  package_code text references ad_packages(code),
  state        text not null default 'draft' check (state in ('draft', 'in_review', 'approved', 'rejected', 'paused', 'terminated')),
  start_date   date,
  end_date     date,
  sponsored_label boolean not null default true,
  destination_url text,
  agreed_currency text check (agreed_currency is null or agreed_currency ~ '^[A-Z]{3}$'),
  agreed_amount_minor bigint check (agreed_amount_minor is null or agreed_amount_minor >= 0),
  payment_status text not null default 'unbilled' check (payment_status in ('unbilled', 'invoiced', 'paid', 'waived')),
  policy_ack   boolean not null default false,
  review_reason text,
  reviewed_by  uuid,
  reviewed_at  timestamptz,
  created_by   uuid not null,
  created_at   timestamptz not null default now(),
  updated_at   timestamptz not null default now(),
  check (end_date is null or start_date is null or end_date >= start_date),
  check ((agreed_currency is null) = (agreed_amount_minor is null))
);
create index ad_campaigns_state_idx on ad_campaigns(state, start_date);
create table ad_creatives (
  id           uuid primary key default gen_random_uuid(),
  advertiser_id uuid not null references ad_advertisers(id),
  campaign_id  uuid references ad_campaigns(id),
  name         text not null check (char_length(name) between 2 and 100),
  aspect       text not null check (aspect in ('2:1', '3:1', '1:2', '16:9', '5:3', '1:1')),
  status       text not null default 'submitted' check (status in ('submitted', 'approved', 'rejected', 'withdrawn')),
  submission_path text not null,
  submission_mime text not null check (submission_mime in ('image/jpeg', 'image/png', 'image/webp')),
  submission_bytes integer not null check (submission_bytes between 1 and 307200),
  expected_paths jsonb not null default '[]'::jsonb,          -- where the owner's processed variants must be uploaded; set when the owner approves
  variants     jsonb not null default '[]'::jsonb,            -- the confirmed public variants: [{aspect, path, width, height, bytes, sha256, mime}]
  public_ready boolean not null default false,
  policy_ack   boolean not null default false,
  review_reason text,
  reviewed_by  uuid,
  reviewed_at  timestamptz,
  created_by   uuid not null,
  created_at   timestamptz not null default now(),
  check (not public_ready or status = 'approved')
);
create index ad_creatives_status_idx on ad_creatives(status, created_at desc);
create table ad_assignments (
  id          uuid primary key default gen_random_uuid(),
  campaign_id uuid not null references ad_campaigns(id) on delete cascade,
  slot_id     text not null references ad_slots(id),
  creative_id uuid not null references ad_creatives(id),
  start_date  date not null,
  end_date    date not null,
  status      text not null default 'reserved' check (status in ('reserved', 'released')),
  created_by  uuid not null,
  created_at  timestamptz not null default now(),
  check (end_date >= start_date),
  -- two reserved placements on one slot can never overlap in time: the database refuses it, whatever the client does
  exclude using gist (slot_id with =, daterange(start_date, end_date, '[]') with &&) where (status = 'reserved')
);
create index ad_assignments_campaign_idx on ad_assignments(campaign_id);
create table ad_exposure_daily (
  day              date not null,
  slot_id          text not null,
  campaign_id      uuid not null,
  measured_seconds numeric not null default 0 check (measured_seconds >= 0),
  measured_views   integer not null default 0 check (measured_views >= 0),
  loaded           integer not null default 0 check (loaded >= 0),
  clicks           integer not null default 0 check (clicks >= 0),
  primary key (day, slot_id, campaign_id)
);
create table ad_policy_terms (
  term text primary key check (term = lower(term) and char_length(term) between 3 and 40),
  flag text not null check (flag in ('gambling', 'tobacco_nicotine', 'alcohol', 'adult', 'weapons', 'scam_fraud', 'misleading', 'political'))
);
create table ad_public_limits (bucket text not null, window_start timestamptz not null, hits integer not null default 0, primary key (bucket, window_start));

do $$ declare t text; begin
  foreach t in array array['ad_packages', 'ad_package_prices', 'ad_boards', 'ad_slots', 'ad_advertisers', 'ad_enquiries', 'ad_upload_tickets', 'ad_campaigns', 'ad_creatives',
                           'ad_assignments', 'ad_exposure_daily', 'ad_policy_terms', 'ad_public_limits'] loop
    execute format('alter table %I enable row level security', t);
    execute format('revoke all on %I from anon, authenticated', t);
  end loop;
end $$;

-- seed: the screening terms and the package catalogue (NO prices: the owner sets them)
insert into ad_policy_terms(term, flag) values
  ('betting', 'gambling'), ('casino', 'gambling'), ('lottery', 'gambling'), ('jackpot', 'gambling'), ('sportybet', 'gambling'), ('bet9ja', 'gambling'),
  ('cigarette', 'tobacco_nicotine'), ('tobacco', 'tobacco_nicotine'), ('nicotine', 'tobacco_nicotine'), ('vape', 'tobacco_nicotine'), ('shisha', 'tobacco_nicotine'),
  ('beer', 'alcohol'), ('whisky', 'alcohol'), ('whiskey', 'alcohol'), ('vodka', 'alcohol'), ('liquor', 'alcohol'), ('alcohol', 'alcohol'), ('palm wine', 'alcohol'),
  ('adult content', 'adult'), ('escort', 'adult'), ('porn', 'adult'), ('sex shop', 'adult'),
  ('firearm', 'weapons'), ('rifle', 'weapons'), ('pistol', 'weapons'), ('ammunition', 'weapons'), ('gun shop', 'weapons'),
  ('get rich quick', 'scam_fraud'), ('double your money', 'scam_fraud'), ('ponzi', 'scam_fraud'), ('guaranteed returns', 'scam_fraud'), ('send money to receive', 'scam_fraud'),
  ('miracle cure', 'misleading'), ('100% guaranteed', 'misleading'), ('cures cancer', 'misleading'),
  ('vote for', 'political'), ('election', 'political'), ('campaign for office', 'political'), ('political party', 'political')
on conflict do nothing;
insert into ad_packages(code, name, board_type, size_class, durations, digital, description) values
  ('roadside_large', 'Roadside billboard, large', 'roadside', 'large', '{14,28,56}', false, 'A large roadside board on rural and industrial roads'),
  ('highway_xl', 'Highway board, extra large', 'highway', 'xl', '{28,56,84}', false, 'A high board set back from the expressway'),
  ('urban_portrait', 'Urban portrait board', 'urban', 'medium', '{14,28,56}', false, 'A lit portrait board on city streets'),
  ('commercial_wide', 'Commercial district board', 'commercial', 'medium', '{14,28,56}', false, 'A wide board on shopping and market streets'),
  ('flyover_parapet', 'Flyover and interchange board', 'flyover', 'medium', '{28,56}', false, 'A board on a flyover parapet (when the flyovers are in the game)'),
  ('terminal_board', 'Bus terminal board', 'terminal', 'small', '{14,28,56}', false, 'A board at a bus terminal'),
  ('wall_wide', 'Warehouse wall board', 'wall', 'large', '{28,56}', false, 'A large wall board at warehouses and depots'),
  ('pylon_two_face', 'Fuel station pylon', 'pylon', 'small', '{28,56,84}', false, 'A two-face board on a fuel station price pylon'),
  ('led_rotating', 'Digital LED screen', 'led', 'large', '{7,14,28}', true, 'A self-lit screen that rotates up to six frames'),
  ('promo_small', 'Small promotional sign', 'promo', 'small', '{14,28}', false, 'A small roadside sign')
on conflict do nothing;

-- ================================================================ helpers (never granted to clients)
-- a caller with no identity (an advertiser filling in the public form) is limited by shared counters instead of by player
create function _ad_public_limit(p_bucket text, p_max integer, p_window_seconds integer) returns void
language plpgsql security definer set search_path = public as $$
declare v_win timestamptz := to_timestamp(floor(extract(epoch from now()) / p_window_seconds) * p_window_seconds); v_hits integer;
begin
  insert into ad_public_limits(bucket, window_start, hits) values (p_bucket, v_win, 1)
  on conflict (bucket, window_start) do update set hits = ad_public_limits.hits + 1 returning hits into v_hits;
  if v_hits > p_max then raise exception 'rate_limited'; end if;
  delete from ad_public_limits where window_start < now() - interval '3 days';
end $$;

-- https only, no credentials, a host with a dot, never an IP address, no spaces or control characters
create function _ad_url_ok(p_url text) returns boolean language sql immutable as $$
  select p_url is not null and char_length(p_url) <= 300 and p_url ~* '^https://[a-z0-9]([a-z0-9-]*[a-z0-9])?(\.[a-z0-9]([a-z0-9-]*[a-z0-9])?)+(:[0-9]{1,5})?(/[^\s<>"]*)?$'
         and p_url !~* '^https://[0-9.]+(:|/|$)' and p_url !~ '@' $$;

create function _ad_policy_flags(p_text text) returns text[] language sql stable security definer set search_path = public as $$
  select coalesce(array_agg(distinct t.flag order by t.flag), '{}') from ad_policy_terms t where lower(coalesce(p_text, '')) like '%' || t.term || '%' $$;

-- one list of what may be advertised and what may not (the owner's content policy: no gambling, tobacco or nicotine, alcohol, adult content, weapons, scams or misleading claims; political excluded for now)
create function _ad_check_category(p_cat text) returns text language plpgsql immutable as $$
declare v_cat text := lower(btrim(coalesce(p_cat, '')));
  v_banned text[] := array['gambling', 'tobacco_nicotine', 'alcohol', 'adult', 'weapons', 'political'];
  v_allowed text[] := array['retail', 'food_drink', 'telecom', 'banking_insurance', 'automotive', 'fuel_energy', 'logistics_transport', 'education', 'health_pharmacy', 'real_estate',
                            'technology', 'hospitality_travel', 'agriculture', 'entertainment_events', 'fashion_beauty', 'ngo_public_service', 'other'];
begin
  if v_cat = any (v_banned) then raise exception 'category_not_allowed'; end if;
  if not (v_cat = any (v_allowed)) then raise exception 'invalid_category'; end if;
  return v_cat;
end $$;

create function _ad_today() returns date language sql stable as $$ select (now() at time zone 'Africa/Lagos')::date $$;

create function _ad_is_owner() returns boolean language plpgsql stable security definer set search_path = public as $$
declare v_aal text;
begin
  if auth.uid() is null then return false; end if;
  v_aal := coalesce(nullif(current_setting('request.jwt.claims', true), '')::jsonb ->> 'aal', nullif(current_setting('request.jwt.claim.aal', true), ''), 'aal1');
  return v_aal = 'aal2' and exists (select 1 from admin_users a where a.user_id = auth.uid() and a.active and a.role = 'owner');
end $$;
create function _ad_is_reviewer() returns boolean language plpgsql stable security definer set search_path = public as $$
declare v_aal text;
begin
  if auth.uid() is null then return false; end if;
  v_aal := coalesce(nullif(current_setting('request.jwt.claims', true), '')::jsonb ->> 'aal', nullif(current_setting('request.jwt.claim.aal', true), ''), 'aal1');
  return v_aal = 'aal2' and exists (select 1 from admin_users a where a.user_id = auth.uid() and a.active and a.role::text in ('owner', 'advertising'));
end $$;
-- the public bucket takes an object only from the owner and only at a path an APPROVED creative expects
create function _ad_may_publish(p_name text) returns boolean language sql stable security definer set search_path = public as $$
  select public._ad_is_owner() and exists (select 1 from ad_creatives c where c.status = 'approved' and c.expected_paths ? p_name) $$;
-- the quarantine bucket takes an anonymous object only at a path with a live, unused upload ticket
create function _ad_ticket_ok(p_name text) returns boolean language sql stable security definer set search_path = public as $$
  select exists (select 1 from ad_upload_tickets t where t.path = p_name and t.used_at is null and t.expires_at > now()) $$;

create function _ad_object(p_bucket text, p_name text, out present boolean, out size_bytes bigint, out mime_type text)
language plpgsql stable security definer set search_path = public as $$
begin
  present := false; size_bytes := null; mime_type := null;
  if to_regclass('storage.objects') is null then raise exception 'storage_unavailable'; end if;
  execute 'select true, coalesce((metadata ->> ''size'')::bigint, 0), metadata ->> ''mimetype'' from storage.objects where bucket_id = $1 and name = $2' into present, size_bytes, mime_type using p_bucket, p_name;
  present := coalesce(present, false);
end $$;

create function _ad_enquiry_json(e ad_enquiries) returns jsonb language sql stable as $$
  select jsonb_build_object('id', e.id, 'created_at', e.created_at, 'business_name', e.business_name, 'contact_name', e.contact_name, 'email', e.email, 'phone', e.phone, 'whatsapp', e.whatsapp,
    'category', e.category, 'description', e.description, 'preferred_city', e.preferred_city, 'preferred_placement', e.preferred_placement, 'duration_days', e.duration_days,
    'message', e.message, 'artwork_path', e.artwork_path, 'artwork_bytes', e.artwork_bytes, 'artwork_mime', e.artwork_mime, 'policy_flags', to_jsonb(e.policy_flags),
    'state', e.state, 'notes', e.notes, 'handled_at', e.handled_at, 'campaign_id', e.campaign_id) $$;

-- the state a campaign is in RIGHT NOW on the server clock (the stored state is the decision; the dates decide whether an approved campaign is waiting, showing or over)
create function _ad_effective_state(c ad_campaigns) returns text language sql stable as $$
  select case when c.state <> 'approved' then c.state
              when c.start_date is null or c.end_date is null then 'approved'
              when public._ad_today() < c.start_date then 'scheduled'
              when public._ad_today() > c.end_date then 'ended'
              else 'live' end $$;

create function _ad_campaign_json(c ad_campaigns) returns jsonb language sql stable as $$
  select jsonb_build_object('id', c.id, 'advertiser_id', c.advertiser_id, 'advertiser', (select a.business_name from ad_advertisers a where a.id = c.advertiser_id), 'enquiry_id', c.enquiry_id,
    'title', c.title, 'package_code', c.package_code, 'state', c.state, 'effective_state', public._ad_effective_state(c), 'start_date', c.start_date, 'end_date', c.end_date,
    'sponsored_label', c.sponsored_label, 'destination_url', c.destination_url, 'agreed_currency', c.agreed_currency, 'agreed_amount_minor', c.agreed_amount_minor,
    'payment_status', c.payment_status, 'policy_ack', c.policy_ack, 'review_reason', c.review_reason, 'reviewed_at', c.reviewed_at, 'created_at', c.created_at,
    'creatives', (select coalesce(jsonb_agg(jsonb_build_object('id', k.id, 'name', k.name, 'status', k.status, 'public_ready', k.public_ready, 'aspect', k.aspect) order by k.created_at), '[]'::jsonb) from ad_creatives k where k.campaign_id = c.id),
    'placements', (select coalesce(jsonb_agg(jsonb_build_object('id', p.id, 'slot_id', p.slot_id, 'creative_id', p.creative_id, 'start_date', p.start_date, 'end_date', p.end_date, 'status', p.status) order by p.start_date), '[]'::jsonb) from ad_assignments p where p.campaign_id = c.id)) $$;

create function _ad_creative_json(k ad_creatives) returns jsonb language sql stable as $$
  select jsonb_build_object('id', k.id, 'advertiser_id', k.advertiser_id, 'campaign_id', k.campaign_id, 'name', k.name, 'aspect', k.aspect, 'status', k.status, 'submission_path', k.submission_path,
    'submission_mime', k.submission_mime, 'submission_bytes', k.submission_bytes, 'expected_paths', k.expected_paths, 'variants', k.variants, 'public_ready', k.public_ready,
    'policy_ack', k.policy_ack, 'review_reason', k.review_reason, 'reviewed_at', k.reviewed_at, 'created_at', k.created_at) $$;

-- ================================================================ roles: 'advertising' joins the list (otherwise identical to the last version)
create or replace function admin_set_role(p_target uuid, p_role text, p_reason text) returns jsonb
language plpgsql security definer set search_path = public as $$
declare v_reason text; v_old admin_users; v_new_role admin_role; v_active boolean; v_owners int;
begin
  if auth.uid() is null then raise exception 'not_authenticated'; end if;
  perform _admin_actor(array['owner']);
  v_reason := _admin_reason(p_reason);
  perform _rate_limit('admin_write', 30, 60);
  if p_role is null or p_role not in ('owner', 'operations', 'support', 'analyst', 'content', 'advertising', 'none') then raise exception 'invalid_role'; end if;
  if not exists (select 1 from auth.users where id = p_target) then raise exception 'user_not_found'; end if;
  select * into v_old from admin_users where user_id = p_target for update;
  if p_role = 'none' then
    if not found or not v_old.active then raise exception 'not_an_admin'; end if;
    v_new_role := v_old.role; v_active := false;
  else v_new_role := p_role::admin_role; v_active := true; end if;
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

-- ================================================================ PUBLIC: advertiser enquiries (anonymous callers; nothing here publishes anything)
create function request_ad_upload(p_ext text) returns jsonb
language plpgsql security definer set search_path = public as $$
declare v_ext text := lower(coalesce(p_ext, '')); v_path text;
begin
  if v_ext not in ('jpg', 'jpeg', 'png', 'webp') then raise exception 'invalid_file_type'; end if;
  perform _ad_public_limit('upload_hour', 60, 3600);
  perform _ad_public_limit('upload_day', 300, 86400);
  v_path := 'enquiries/' || gen_random_uuid()::text || '.' || v_ext;
  insert into ad_upload_tickets(path, expires_at) values (v_path, now() + interval '30 minutes');
  return jsonb_build_object('bucket', 'ad-submissions', 'path', v_path, 'expires_at', now() + interval '30 minutes', 'max_bytes', 307200, 'mime_types', jsonb_build_array('image/jpeg', 'image/png', 'image/webp'));
end $$;

create function submit_ad_enquiry(p_data jsonb) returns jsonb
language plpgsql security definer set search_path = public as $$
declare
  d jsonb := coalesce(p_data, '{}'::jsonb);
  v_business text; v_contact text; v_email text; v_phone text; v_whats text; v_cat text; v_desc text; v_city text; v_place text; v_dur int; v_msg text;
  v_path text := nullif(btrim(coalesce(d ->> 'artwork_path', '')), ''); v_bytes bigint; v_mime text; v_found boolean; v_flags text[]; v_id uuid;
begin
  if jsonb_typeof(d) <> 'object' or length(d::text) > 8000 then raise exception 'invalid_request'; end if;
  if nullif(btrim(coalesce(d ->> 'website_url', '')), '') is not null then   -- the honeypot: a person never fills this in. Accepted silently, nothing stored.
    return jsonb_build_object('status', 'received', 'reference', null);
  end if;
  perform _ad_public_limit('enquiry_hour', 40, 3600);
  perform _ad_public_limit('enquiry_day', 150, 86400);
  v_business := _admin_text(d ->> 'business_name', 2, 120, 'invalid_business_name');
  v_contact := _admin_text(d ->> 'contact_name', 2, 80, 'invalid_contact_name');
  v_email := lower(btrim(coalesce(d ->> 'email', '')));
  if v_email !~* '^[^@\s]+@[^@\s]+\.[^@\s]+$' or char_length(v_email) > 120 then raise exception 'invalid_email'; end if;
  v_phone := nullif(btrim(coalesce(d ->> 'phone', '')), '');
  if v_phone is not null and v_phone !~ '^\+?[0-9 ()-]{7,24}$' then raise exception 'invalid_phone'; end if;
  v_whats := nullif(btrim(coalesce(d ->> 'whatsapp', '')), '');
  if v_whats is not null and v_whats !~ '^\+?[0-9 ()-]{7,24}$' then raise exception 'invalid_phone'; end if;
  if v_phone is null and v_whats is null then raise exception 'contact_number_required'; end if;
  v_cat := _ad_check_category(d ->> 'category');
  v_desc := _admin_text(d ->> 'description', 10, 1000, 'invalid_description');
  v_city := case when nullif(btrim(coalesce(d ->> 'preferred_city', '')), '') is null then null else _admin_text(d ->> 'preferred_city', 1, 60, 'invalid_city') end;
  v_place := nullif(btrim(coalesce(d ->> 'preferred_placement', '')), '');
  if v_place is not null and v_place not in ('roadside', 'highway', 'urban', 'commercial', 'flyover', 'terminal', 'wall', 'pylon', 'led', 'promo') then raise exception 'invalid_placement'; end if;
  if d ? 'duration_days' and d ->> 'duration_days' !~ '^[0-9]{1,3}$' then raise exception 'invalid_duration'; end if;
  v_dur := nullif(d ->> 'duration_days', '')::int;
  if v_dur is not null and (v_dur < 7 or v_dur > 365) then raise exception 'invalid_duration'; end if;
  v_msg := case when nullif(btrim(coalesce(d ->> 'message', '')), '') is null then null else _admin_text(d ->> 'message', 1, 1000, 'invalid_message') end;
  if coalesce(d ->> 'rights_confirmed', 'false') <> 'true' then raise exception 'rights_confirmation_required'; end if;
  if (select count(*) from ad_enquiries where lower(email) = v_email and created_at > now() - interval '1 day') >= 3 then raise exception 'rate_limited'; end if;
  if v_path is not null then
    if v_path !~ '^enquiries/[0-9a-f-]{36}\.(jpg|jpeg|png|webp)$' then raise exception 'invalid_artwork'; end if;
    if not exists (select 1 from ad_upload_tickets t where t.path = v_path and t.used_at is null and t.expires_at > now() + interval '-10 minutes') then raise exception 'invalid_artwork'; end if;
    select o.present, o.size_bytes, o.mime_type into v_found, v_bytes, v_mime from _ad_object('ad-submissions', v_path) o;
    if not v_found then raise exception 'artwork_not_uploaded'; end if;
    if v_bytes < 1 or v_bytes > 307200 then raise exception 'artwork_too_large'; end if;
    if v_mime is null or v_mime not in ('image/jpeg', 'image/png', 'image/webp') then raise exception 'invalid_artwork'; end if;
    update ad_upload_tickets set used_at = now() where path = v_path;
  end if;
  v_flags := _ad_policy_flags(v_business || ' ' || v_desc || ' ' || coalesce(v_msg, ''));
  insert into ad_enquiries(business_name, contact_name, email, phone, whatsapp, category, description, preferred_city, preferred_placement, duration_days, message,
                           artwork_path, artwork_bytes, artwork_mime, rights_confirmed, policy_flags)
  values (v_business, v_contact, v_email, v_phone, v_whats, v_cat, v_desc, v_city, v_place, v_dur, v_msg, v_path, v_bytes, v_mime, true, v_flags) returning id into v_id;
  return jsonb_build_object('status', 'received', 'reference', upper(left(v_id::text, 8)));
end $$;

-- ================================================================ PLAYERS: the manifest the game reads, and the aggregated exposure it reports
create function get_ad_manifest(p_route text default null, p_since text default null) returns jsonb
language plpgsql security definer set search_path = public as $$
declare v_items jsonb; v_version text;
begin
  if auth.uid() is null then raise exception 'not_authenticated'; end if;
  perform _rate_limit('ad_manifest', 20, 60);
  if p_route is not null and p_route !~ '^[a-z0-9-]{2,60}$' then raise exception 'invalid_route'; end if;
  select coalesce(jsonb_agg(x order by x ->> 'slot_id'), '[]'::jsonb) into v_items from (
    select jsonb_build_object('slot_id', s.id, 'board_id', b.id, 'campaign_id', c.id, 'creative_id', k.id, 'aspect', s.aspect, 'width_m', s.width_m, 'height_m', s.height_m,
             'label', case when c.sponsored_label then 'Sponsored' else null end, 'ends', a.end_date,
             'image', (select v -> 'path' from jsonb_array_elements(k.variants) v where v ->> 'aspect' = s.aspect limit 1),
             'sha256', (select v -> 'sha256' from jsonb_array_elements(k.variants) v where v ->> 'aspect' = s.aspect limit 1),
             'width', (select v -> 'width' from jsonb_array_elements(k.variants) v where v ->> 'aspect' = s.aspect limit 1),
             'height', (select v -> 'height' from jsonb_array_elements(k.variants) v where v ->> 'aspect' = s.aspect limit 1),
             'link', case when c.destination_url is not null then c.destination_url else null end) as x
      from ad_assignments a join ad_campaigns c on c.id = a.campaign_id join ad_creatives k on k.id = a.creative_id join ad_slots s on s.id = a.slot_id join ad_boards b on b.id = s.board_id
     where a.status = 'reserved' and c.state = 'approved' and k.status = 'approved' and k.public_ready and b.status = 'available'
       and public._ad_today() between greatest(a.start_date, c.start_date) and least(a.end_date, c.end_date)
       and (p_route is null or b.route_code = p_route)
       and exists (select 1 from jsonb_array_elements(k.variants) v where v ->> 'aspect' = s.aspect)) q;
  v_version := md5(v_items::text);
  if p_since is not null and p_since = v_version then return jsonb_build_object('version', v_version, 'unchanged', true, 'server_time', now()); end if;
  return jsonb_build_object('version', v_version, 'unchanged', false, 'server_time', now(), 'bucket', 'ad-creatives-public', 'items', v_items);
end $$;

-- p_batch: [{slot_id, seconds, views, loaded, clicks}]. Measured, aggregated, no player identity stored. Entries for a slot that is not showing a live campaign now are ignored.
create function record_ad_exposure(p_batch jsonb) returns jsonb
language plpgsql security definer set search_path = public as $$
declare e jsonb; v_slot text; v_camp uuid; v_sec numeric; v_views int; v_loaded int; v_clicks int; v_ok int := 0; v_ignored int := 0; v_total numeric := 0;
begin
  if auth.uid() is null then raise exception 'not_authenticated'; end if;
  perform _rate_limit('ad_exposure', 30, 60);
  if p_batch is null or jsonb_typeof(p_batch) <> 'array' or jsonb_array_length(p_batch) > 40 then raise exception 'invalid_batch'; end if;
  for e in select * from jsonb_array_elements(p_batch) loop
    begin
      v_slot := e ->> 'slot_id';
      v_sec := least(greatest(coalesce((e ->> 'seconds')::numeric, 0), 0), 120);
      v_views := least(greatest(coalesce((e ->> 'views')::int, 0), 0), 5);
      v_loaded := least(greatest(coalesce((e ->> 'loaded')::int, 0), 0), 1);
      v_clicks := least(greatest(coalesce((e ->> 'clicks')::int, 0), 0), 1);
    exception when others then v_ignored := v_ignored + 1; continue;
    end;
    v_total := v_total + v_sec;
    if v_total > 900 then v_ignored := v_ignored + 1; continue; end if;                   -- a plausible ceiling for one call
    v_camp := null;
    select c.id into v_camp from ad_assignments a join ad_campaigns c on c.id = a.campaign_id join ad_creatives k on k.id = a.creative_id
     where a.slot_id = v_slot and a.status = 'reserved' and c.state = 'approved' and k.status = 'approved' and k.public_ready
       and public._ad_today() between greatest(a.start_date, c.start_date) and least(a.end_date, c.end_date) limit 1;
    if v_camp is null then v_ignored := v_ignored + 1; continue; end if;
    insert into ad_exposure_daily(day, slot_id, campaign_id, measured_seconds, measured_views, loaded, clicks) values (public._ad_today(), v_slot, v_camp, v_sec, v_views, v_loaded, v_clicks)
    on conflict (day, slot_id, campaign_id) do update set measured_seconds = ad_exposure_daily.measured_seconds + excluded.measured_seconds,
      measured_views = ad_exposure_daily.measured_views + excluded.measured_views, loaded = ad_exposure_daily.loaded + excluded.loaded, clicks = ad_exposure_daily.clicks + excluded.clicks;
    v_ok := v_ok + 1;
  end loop;
  return jsonb_build_object('accepted', v_ok, 'ignored', v_ignored);
end $$;

-- ================================================================ ADMIN: overview, inventory, packages and prices
create function admin_ad_overview() returns jsonb
language plpgsql security definer set search_path = public as $$
declare v_today date := _ad_today();
begin
  if auth.uid() is null then raise exception 'not_authenticated'; end if;
  perform _rate_limit('admin_read', 240, 60);
  perform _admin_actor(array['owner', 'advertising', 'analyst']);
  return jsonb_build_object('server_time', now(), 'today', v_today,
    'boards', jsonb_build_object('total', (select count(*) from ad_boards), 'available', (select count(*) from ad_boards where status = 'available'),
      'planned', (select count(*) from ad_boards where status = 'planned'), 'maintenance', (select count(*) from ad_boards where status = 'maintenance')),
    'slots', jsonb_build_object('total', (select count(*) from ad_slots s join ad_boards b on b.id = s.board_id where b.status <> 'retired'),
      'occupied_now', (select count(distinct a.slot_id) from ad_assignments a join ad_campaigns c on c.id = a.campaign_id where a.status = 'reserved' and c.state = 'approved' and v_today between a.start_date and a.end_date),
      'reserved_total', (select count(distinct a.slot_id) from ad_assignments a where a.status = 'reserved' and a.end_date >= v_today)),
    'campaigns', (select coalesce(jsonb_object_agg(st, n), '{}'::jsonb) from (select _ad_effective_state(c) st, count(*) n from ad_campaigns c group by 1) x),
    'enquiries', (select coalesce(jsonb_object_agg(state, n), '{}'::jsonb) from (select state, count(*) n from ad_enquiries group by state) x),
    'creatives_waiting', (select count(*) from ad_creatives where status = 'submitted'));
end $$;

create function admin_ad_list_boards(p_status text default null, p_type text default null) returns jsonb
language plpgsql security definer set search_path = public as $$
declare v_today date := _ad_today();
begin
  if auth.uid() is null then raise exception 'not_authenticated'; end if;
  perform _rate_limit('admin_read', 240, 60);
  perform _admin_actor(array['owner', 'advertising', 'analyst']);
  return jsonb_build_object('server_time', now(), 'today', v_today, 'items', coalesce((
    select jsonb_agg(jsonb_build_object('id', b.id, 'route_code', b.route_code, 'city', b.city, 'district', b.district, 'road_label', b.road_label, 'station_m', b.station_m, 'side', b.side,
        'board_type', b.board_type, 'width_m', b.width_m, 'height_m', b.height_m, 'illuminated', b.illuminated, 'digital', b.digital, 'status', b.status, 'est_daily_passes', b.est_daily_passes,
        'slots', (select coalesce(jsonb_agg(jsonb_build_object('id', s.id, 'face', s.face, 'aspect', s.aspect,
              'occupied_now', exists (select 1 from ad_assignments a join ad_campaigns c on c.id = a.campaign_id where a.slot_id = s.id and a.status = 'reserved' and c.state = 'approved' and v_today between a.start_date and a.end_date),
              'reserved_until', (select max(a.end_date) from ad_assignments a where a.slot_id = s.id and a.status = 'reserved' and a.end_date >= v_today)) order by s.face), '[]'::jsonb) from ad_slots s where s.board_id = b.id))
      order by b.route_code, b.station_m, b.id)
      from (select * from ad_boards where (p_status is null or status = p_status) and (p_type is null or board_type = p_type) order by route_code, station_m, id limit 500) b), '[]'::jsonb));
end $$;

create function admin_ad_set_board_status(p_id text, p_status text, p_est_daily_passes integer, p_reason text) returns jsonb
language plpgsql security definer set search_path = public as $$
declare v_reason text; v_old ad_boards; v_new ad_boards;
begin
  if auth.uid() is null then raise exception 'not_authenticated'; end if;
  perform _admin_actor(array['owner']);
  v_reason := _admin_reason(p_reason);
  perform _rate_limit('admin_write', 30, 60);
  if p_status is null or p_status not in ('planned', 'available', 'maintenance', 'retired') then raise exception 'invalid_status'; end if;
  if p_est_daily_passes is not null and (p_est_daily_passes < 0 or p_est_daily_passes > 10000000) then raise exception 'invalid_estimate'; end if;
  select * into v_old from ad_boards where id = p_id for update;
  if not found then raise exception 'board_not_found'; end if;
  update ad_boards set status = p_status, est_daily_passes = p_est_daily_passes, updated_at = now() where id = p_id returning * into v_new;
  perform _admin_log('ad_board_status', 'ad_board', p_id, v_reason, jsonb_build_object('from', v_old.status, 'to', p_status, 'est_from', v_old.est_daily_passes, 'est_to', p_est_daily_passes));
  return jsonb_build_object('id', v_new.id, 'status', v_new.status, 'est_daily_passes', v_new.est_daily_passes);
end $$;

create function admin_ad_list_packages() returns jsonb
language plpgsql security definer set search_path = public as $$
begin
  if auth.uid() is null then raise exception 'not_authenticated'; end if;
  perform _rate_limit('admin_read', 240, 60);
  perform _admin_actor(array['owner', 'advertising', 'analyst']);
  return jsonb_build_object('server_time', now(), 'items', coalesce((
    select jsonb_agg(jsonb_build_object('code', p.code, 'name', p.name, 'board_type', p.board_type, 'size_class', p.size_class, 'durations', to_jsonb(p.durations), 'digital', p.digital,
        'description', p.description, 'active', p.active,
        'prices', (select coalesce(jsonb_agg(jsonb_build_object('currency', x.currency, 'amount_minor', x.amount_minor, 'period_days', x.period_days) order by x.currency), '[]'::jsonb) from ad_package_prices x where x.package_code = p.code),
        'boards_available', (select count(*) from ad_boards b where b.board_type = p.board_type and b.status = 'available')) order by p.board_type, p.code)
      from ad_packages p), '[]'::jsonb));
end $$;

create function admin_ad_save_package(p_code text, p_name text, p_board_type text, p_size_class text, p_durations integer[], p_digital boolean, p_description text, p_active boolean, p_reason text) returns jsonb
language plpgsql security definer set search_path = public as $$
declare v_reason text; v_existed boolean; v_name text; v_desc text;
begin
  if auth.uid() is null then raise exception 'not_authenticated'; end if;
  perform _admin_actor(array['owner']);
  v_reason := _admin_reason(p_reason);
  perform _rate_limit('admin_write', 30, 60);
  if p_code is null or p_code !~ '^[a-z0-9_]{2,40}$' then raise exception 'invalid_code'; end if;
  v_name := _admin_text(p_name, 2, 80, 'invalid_name');
  v_desc := case when nullif(btrim(coalesce(p_description, '')), '') is null then null else _admin_text(p_description, 1, 300, 'invalid_description') end;
  if p_board_type is null or p_board_type not in ('roadside', 'highway', 'urban', 'commercial', 'flyover', 'terminal', 'wall', 'pylon', 'led', 'promo') then raise exception 'invalid_board_type'; end if;
  if p_size_class is null or p_size_class not in ('small', 'medium', 'large', 'xl') then raise exception 'invalid_size_class'; end if;
  if p_durations is null or cardinality(p_durations) not between 1 and 8 or exists (select 1 from unnest(p_durations) d where d < 1 or d > 365) then raise exception 'invalid_durations'; end if;
  v_existed := exists (select 1 from ad_packages where code = p_code);
  insert into ad_packages(code, name, board_type, size_class, durations, digital, description, active) values (p_code, v_name, p_board_type, p_size_class, p_durations, coalesce(p_digital, false), v_desc, coalesce(p_active, true))
  on conflict (code) do update set name = excluded.name, board_type = excluded.board_type, size_class = excluded.size_class, durations = excluded.durations, digital = excluded.digital,
    description = excluded.description, active = excluded.active, updated_at = now();
  perform _admin_log('ad_package_save', 'ad_package', p_code, v_reason, jsonb_build_object('created', not v_existed, 'board_type', p_board_type, 'active', coalesce(p_active, true)));
  return jsonb_build_object('code', p_code, 'created', not v_existed);
end $$;

-- p_amount_minor null removes that currency's price. Every change is audited with the old and the new amount.
create function admin_ad_set_price(p_code text, p_currency text, p_amount_minor bigint, p_period_days integer, p_reason text) returns jsonb
language plpgsql security definer set search_path = public as $$
declare v_reason text; v_old ad_package_prices;
begin
  if auth.uid() is null then raise exception 'not_authenticated'; end if;
  perform _admin_actor(array['owner']);
  v_reason := _admin_reason(p_reason);
  perform _rate_limit('admin_write', 30, 60);
  if p_currency is null or p_currency !~ '^[A-Z]{3}$' then raise exception 'invalid_currency'; end if;
  if not exists (select 1 from ad_packages where code = p_code) then raise exception 'package_not_found'; end if;
  select * into v_old from ad_package_prices where package_code = p_code and currency = p_currency for update;
  if p_amount_minor is null then
    if not found then raise exception 'price_not_found'; end if;
    delete from ad_package_prices where package_code = p_code and currency = p_currency;
    perform _admin_log('ad_price_remove', 'ad_package', p_code, v_reason, jsonb_build_object('currency', p_currency, 'old_amount_minor', v_old.amount_minor));
    return jsonb_build_object('code', p_code, 'currency', p_currency, 'removed', true);
  end if;
  if p_amount_minor < 0 or p_amount_minor > 100000000000 then raise exception 'invalid_amount'; end if;
  if coalesce(p_period_days, 28) < 1 or coalesce(p_period_days, 28) > 365 then raise exception 'invalid_period'; end if;
  insert into ad_package_prices(package_code, currency, amount_minor, period_days, updated_by) values (p_code, p_currency, p_amount_minor, coalesce(p_period_days, 28), auth.uid())
  on conflict (package_code, currency) do update set amount_minor = excluded.amount_minor, period_days = excluded.period_days, updated_by = auth.uid(), updated_at = now();
  perform _admin_log('ad_price_set', 'ad_package', p_code, v_reason, jsonb_build_object('currency', p_currency, 'old_amount_minor', v_old.amount_minor, 'new_amount_minor', p_amount_minor,
                       'period_days', coalesce(p_period_days, 28)));
  return jsonb_build_object('code', p_code, 'currency', p_currency, 'amount_minor', p_amount_minor, 'period_days', coalesce(p_period_days, 28));
end $$;

-- ================================================================ ADMIN: enquiries
create function admin_ad_list_enquiries(p_state text default null, p_limit integer default 50) returns jsonb
language plpgsql security definer set search_path = public as $$
declare v_lim int := least(greatest(coalesce(p_limit, 50), 1), 100);
begin
  if auth.uid() is null then raise exception 'not_authenticated'; end if;
  perform _rate_limit('admin_read', 240, 60);
  perform _admin_actor(array['owner', 'advertising']);
  if p_state is not null and p_state not in ('new', 'contacted', 'approved', 'rejected', 'converted') then raise exception 'invalid_state'; end if;
  perform _admin_log('ad_enquiries_list', 'ad_enquiry', null, null, jsonb_build_object('state', p_state));
  return jsonb_build_object('server_time', now(), 'items', coalesce((
    select jsonb_agg(_ad_enquiry_json(e) order by e.created_at desc) from (select * from ad_enquiries where p_state is null or state = p_state order by created_at desc limit v_lim) e), '[]'::jsonb));
end $$;

-- state: contacted | approved | rejected (a decision about the ENQUIRY, not about any advertisement: nothing is published by it). A flagged enquiry needs the policy acknowledgement to be approved.
create function admin_ad_update_enquiry(p_id uuid, p_state text, p_notes text, p_policy_ack boolean, p_reason text) returns jsonb
language plpgsql security definer set search_path = public as $$
declare v_reason text; e ad_enquiries; v_notes text;
begin
  if auth.uid() is null then raise exception 'not_authenticated'; end if;
  perform _admin_actor(array['owner', 'advertising']);
  v_reason := _admin_reason(p_reason);
  perform _rate_limit('admin_write', 30, 60);
  if p_state is null or p_state not in ('new', 'contacted', 'approved', 'rejected') then raise exception 'invalid_state'; end if;
  v_notes := case when nullif(btrim(coalesce(p_notes, '')), '') is null then null else _admin_text(p_notes, 1, 1000, 'invalid_notes') end;
  select * into e from ad_enquiries where id = p_id for update;
  if not found then raise exception 'enquiry_not_found'; end if;
  if e.state = 'converted' then raise exception 'already_converted'; end if;
  if p_state = 'approved' and cardinality(e.policy_flags) > 0 and not coalesce(p_policy_ack, false) then raise exception 'policy_ack_required'; end if;
  update ad_enquiries set state = p_state, notes = coalesce(v_notes, notes), handled_by = auth.uid(), handled_at = now() where id = p_id returning * into e;
  perform _admin_log('ad_enquiry_update', 'ad_enquiry', p_id::text, v_reason, jsonb_build_object('state', p_state, 'flags', to_jsonb(e.policy_flags)));
  return _ad_enquiry_json(e);
end $$;

-- the enquiry becomes an advertiser and a DRAFT campaign. It is never published by this call. Its artwork (if any) stays in the quarantine bucket until staff register it with admin_ad_register_creative and choose its aspect.
create function admin_ad_convert_enquiry(p_id uuid, p_title text, p_reason text) returns jsonb
language plpgsql security definer set search_path = public as $$
declare v_reason text; e ad_enquiries; v_adv uuid; v_camp ad_campaigns; v_title text;
begin
  if auth.uid() is null then raise exception 'not_authenticated'; end if;
  perform _admin_actor(array['owner', 'advertising']);
  v_reason := _admin_reason(p_reason);
  perform _rate_limit('admin_write', 30, 60);
  v_title := _admin_text(coalesce(p_title, ''), 2, 100, 'invalid_title');
  select * into e from ad_enquiries where id = p_id for update;
  if not found then raise exception 'enquiry_not_found'; end if;
  if e.state = 'converted' then raise exception 'already_converted'; end if;
  if e.state <> 'approved' then raise exception 'enquiry_not_approved'; end if;
  insert into ad_advertisers(business_name, contact_name, email, phone, category, rights_confirmed_at) values (e.business_name, e.contact_name, e.email, coalesce(e.phone, e.whatsapp), e.category, e.created_at)
  returning id into v_adv;
  insert into ad_campaigns(advertiser_id, enquiry_id, title, created_by) values (v_adv, e.id, v_title, auth.uid()) returning * into v_camp;
  update ad_enquiries set state = 'converted', campaign_id = v_camp.id, handled_by = auth.uid(), handled_at = now() where id = p_id;
  perform _admin_log('ad_enquiry_convert', 'ad_enquiry', p_id::text, v_reason, jsonb_build_object('campaign_id', v_camp.id, 'advertiser_id', v_adv));
  return _ad_campaign_json(v_camp);
end $$;

-- ================================================================ ADMIN: campaigns (everyone prepares, only the owner publishes)
-- a campaign for an advertiser who did not come through the public form (a direct sale). The advertiser's right to the brand and the artwork must be confirmed; the campaign starts as a DRAFT.
create function admin_ad_create_campaign(p_business text, p_contact text, p_email text, p_phone text, p_category text, p_title text, p_rights_confirmed boolean, p_reason text) returns jsonb
language plpgsql security definer set search_path = public as $$
declare v_reason text; v_adv uuid; c ad_campaigns; v_email text := lower(btrim(coalesce(p_email, ''))); v_phone text := nullif(btrim(coalesce(p_phone, '')), '');
begin
  if auth.uid() is null then raise exception 'not_authenticated'; end if;
  perform _admin_actor(array['owner', 'advertising']);
  v_reason := _admin_reason(p_reason);
  perform _rate_limit('admin_write', 30, 60);
  if not coalesce(p_rights_confirmed, false) then raise exception 'rights_confirmation_required'; end if;
  if v_email !~* '^[^@\s]+@[^@\s]+\.[^@\s]+$' or char_length(v_email) > 120 then raise exception 'invalid_email'; end if;
  if v_phone is not null and v_phone !~ '^\+?[0-9 ()-]{7,24}$' then raise exception 'invalid_phone'; end if;
  insert into ad_advertisers(business_name, contact_name, email, phone, category, rights_confirmed_at)
  values (_admin_text(p_business, 2, 120, 'invalid_business_name'), _admin_text(p_contact, 2, 80, 'invalid_contact_name'), v_email, v_phone, _ad_check_category(p_category), now()) returning id into v_adv;
  insert into ad_campaigns(advertiser_id, title, created_by) values (v_adv, _admin_text(p_title, 2, 100, 'invalid_title'), auth.uid()) returning * into c;
  perform _admin_log('ad_campaign_create', 'ad_campaign', c.id::text, v_reason, jsonb_build_object('advertiser_id', v_adv, 'category', _ad_check_category(p_category)));
  return _ad_campaign_json(c);
end $$;

create function admin_ad_list_campaigns(p_state text default null, p_limit integer default 100) returns jsonb
language plpgsql security definer set search_path = public as $$
declare v_lim int := least(greatest(coalesce(p_limit, 100), 1), 200);
begin
  if auth.uid() is null then raise exception 'not_authenticated'; end if;
  perform _rate_limit('admin_read', 240, 60);
  perform _admin_actor(array['owner', 'advertising', 'analyst']);
  return jsonb_build_object('server_time', now(), 'today', _ad_today(), 'items', coalesce((
    select jsonb_agg(_ad_campaign_json(c) order by c.created_at desc) from (select * from ad_campaigns c where p_state is null or c.state = p_state or _ad_effective_state(c) = p_state order by c.created_at desc limit v_lim) c), '[]'::jsonb));
end $$;

create function admin_ad_save_campaign(p_id uuid, p_title text, p_package_code text, p_start date, p_end date, p_sponsored boolean, p_destination_url text, p_reason text) returns jsonb
language plpgsql security definer set search_path = public as $$
declare v_reason text; c ad_campaigns; v_title text; v_url text := nullif(btrim(coalesce(p_destination_url, '')), ''); v_price ad_package_prices;
begin
  if auth.uid() is null then raise exception 'not_authenticated'; end if;
  perform _admin_actor(array['owner', 'advertising']);
  v_reason := _admin_reason(p_reason);
  perform _rate_limit('admin_write', 30, 60);
  v_title := _admin_text(p_title, 2, 100, 'invalid_title');
  if p_id is null then raise exception 'campaign_not_found'; end if;
  select * into c from ad_campaigns where id = p_id for update;
  if not found then raise exception 'campaign_not_found'; end if;
  if c.state not in ('draft', 'rejected') then raise exception 'campaign_locked'; end if;     -- once submitted, a campaign is changed only through a new review
  if p_package_code is not null and not exists (select 1 from ad_packages where code = p_package_code and active) then raise exception 'package_not_found'; end if;
  if p_start is not null and p_end is not null and p_end < p_start then raise exception 'invalid_schedule'; end if;
  if (p_start is not null and p_start < _ad_today() - 1) then raise exception 'invalid_schedule'; end if;
  if v_url is not null and not _ad_url_ok(v_url) then raise exception 'invalid_url'; end if;
  select * into v_price from ad_package_prices where package_code = p_package_code and currency = 'NGN';        -- the agreed price is copied at this moment, so a later price change never rewrites it
  update ad_campaigns set title = v_title, package_code = p_package_code, start_date = p_start, end_date = p_end, sponsored_label = coalesce(p_sponsored, true), destination_url = v_url,
    agreed_currency = case when v_price.package_code is not null and p_start is not null and p_end is not null then v_price.currency end,
    agreed_amount_minor = case when v_price.package_code is not null and p_start is not null and p_end is not null then ceil(v_price.amount_minor * ((p_end - p_start + 1)::numeric / v_price.period_days))::bigint end,
    state = 'draft', updated_at = now() where id = p_id returning * into c;
  perform _admin_log('ad_campaign_save', 'ad_campaign', p_id::text, v_reason, jsonb_build_object('package', p_package_code, 'start', p_start, 'end', p_end, 'sponsored', c.sponsored_label, 'has_link', v_url is not null));
  return _ad_campaign_json(c);
end $$;

create function admin_ad_submit_campaign(p_id uuid, p_reason text) returns jsonb
language plpgsql security definer set search_path = public as $$
declare v_reason text; c ad_campaigns;
begin
  if auth.uid() is null then raise exception 'not_authenticated'; end if;
  perform _admin_actor(array['owner', 'advertising']);
  v_reason := _admin_reason(p_reason);
  perform _rate_limit('admin_write', 30, 60);
  select * into c from ad_campaigns where id = p_id for update;
  if not found then raise exception 'campaign_not_found'; end if;
  if c.state not in ('draft', 'rejected') then raise exception 'invalid_transition'; end if;
  if c.start_date is null or c.end_date is null then raise exception 'schedule_required'; end if;
  if not exists (select 1 from ad_creatives k where k.campaign_id = c.id and k.status in ('submitted', 'approved')) then raise exception 'creative_required'; end if;
  if not exists (select 1 from ad_assignments a where a.campaign_id = c.id and a.status = 'reserved') then raise exception 'placement_required'; end if;
  update ad_campaigns set state = 'in_review', updated_at = now() where id = p_id returning * into c;
  perform _admin_log('ad_campaign_submit', 'ad_campaign', p_id::text, v_reason, '{}'::jsonb);
  return _ad_campaign_json(c);
end $$;

-- THE publication decision. Owner only. Approving needs every creative of the campaign that is used by a placement to be approved with its public variants confirmed.
create function admin_ad_review_campaign(p_id uuid, p_decision text, p_policy_ack boolean, p_reason text) returns jsonb
language plpgsql security definer set search_path = public as $$
declare v_reason text; c ad_campaigns; v_unready int; v_flags int;
begin
  if auth.uid() is null then raise exception 'not_authenticated'; end if;
  perform _admin_actor(array['owner']);
  v_reason := _admin_reason(p_reason);
  perform _rate_limit('admin_write', 30, 60);
  if p_decision is null or p_decision not in ('approve', 'reject') then raise exception 'invalid_decision'; end if;
  select * into c from ad_campaigns where id = p_id for update;
  if not found then raise exception 'campaign_not_found'; end if;
  if c.state <> 'in_review' then raise exception 'invalid_transition'; end if;
  if p_decision = 'approve' then
    select count(*) into v_unready from ad_assignments a join ad_creatives k on k.id = a.creative_id where a.campaign_id = c.id and a.status = 'reserved' and not (k.status = 'approved' and k.public_ready);
    if v_unready > 0 then raise exception 'creative_not_ready'; end if;
    select count(*) into v_flags from ad_enquiries e where e.id = c.enquiry_id and cardinality(e.policy_flags) > 0;
    if v_flags > 0 and not coalesce(p_policy_ack, false) then raise exception 'policy_ack_required'; end if;
    update ad_campaigns set state = 'approved', policy_ack = coalesce(p_policy_ack, false), review_reason = v_reason, reviewed_by = auth.uid(), reviewed_at = now(), updated_at = now() where id = p_id returning * into c;
  else
    update ad_campaigns set state = 'rejected', review_reason = v_reason, reviewed_by = auth.uid(), reviewed_at = now(), updated_at = now() where id = p_id returning * into c;
  end if;
  perform _admin_log(case when p_decision = 'approve' then 'ad_campaign_approve' else 'ad_campaign_reject' end, 'ad_campaign', p_id::text, v_reason,
                     jsonb_build_object('start', c.start_date, 'end', c.end_date, 'policy_ack', c.policy_ack));
  return _ad_campaign_json(c);
end $$;

-- pause: the owner or advertising (taking something OFF the road is always safe). resume: owner only (putting it back is a publication). terminate: owner only, final.
create function admin_ad_set_campaign_state(p_id uuid, p_action text, p_reason text) returns jsonb
language plpgsql security definer set search_path = public as $$
declare v_reason text; v_role text; c ad_campaigns;
begin
  if auth.uid() is null then raise exception 'not_authenticated'; end if;
  v_role := _admin_actor(array['owner', 'advertising']);
  v_reason := _admin_reason(p_reason);
  perform _rate_limit('admin_write', 30, 60);
  if p_action is null or p_action not in ('pause', 'resume', 'terminate') then raise exception 'invalid_action'; end if;
  if p_action in ('resume', 'terminate') and v_role <> 'owner' then raise exception 'not_allowed'; end if;
  select * into c from ad_campaigns where id = p_id for update;
  if not found then raise exception 'campaign_not_found'; end if;
  if p_action = 'pause' then
    if c.state <> 'approved' then raise exception 'invalid_transition'; end if;
    update ad_campaigns set state = 'paused', updated_at = now() where id = p_id returning * into c;
  elsif p_action = 'resume' then
    if c.state <> 'paused' then raise exception 'invalid_transition'; end if;
    update ad_campaigns set state = 'approved', updated_at = now() where id = p_id returning * into c;
  else
    if c.state in ('terminated', 'rejected') then raise exception 'invalid_transition'; end if;
    update ad_campaigns set state = 'terminated', review_reason = v_reason, updated_at = now() where id = p_id returning * into c;
    update ad_assignments set status = 'released' where campaign_id = p_id and status = 'reserved';
  end if;
  perform _admin_log('ad_campaign_' || p_action, 'ad_campaign', p_id::text, v_reason, jsonb_build_object('state', c.state));
  return _ad_campaign_json(c);
end $$;

create function admin_ad_set_payment_status(p_id uuid, p_status text, p_reason text) returns jsonb
language plpgsql security definer set search_path = public as $$
declare v_reason text; c ad_campaigns; v_old text;
begin
  if auth.uid() is null then raise exception 'not_authenticated'; end if;
  perform _admin_actor(array['owner']);
  v_reason := _admin_reason(p_reason);
  perform _rate_limit('admin_write', 30, 60);
  if p_status is null or p_status not in ('unbilled', 'invoiced', 'paid', 'waived') then raise exception 'invalid_status'; end if;
  select payment_status into v_old from ad_campaigns where id = p_id for update;
  if not found then raise exception 'campaign_not_found'; end if;
  update ad_campaigns set payment_status = p_status, updated_at = now() where id = p_id returning * into c;
  perform _admin_log('ad_payment_status', 'ad_campaign', p_id::text, v_reason, jsonb_build_object('from', v_old, 'to', p_status));
  return _ad_campaign_json(c);
end $$;

-- placements: reserve a face for a draft campaign over its dates; the exclusion constraint makes an overlap impossible
create function admin_ad_assign(p_campaign uuid, p_slot text, p_creative uuid, p_reason text) returns jsonb
language plpgsql security definer set search_path = public as $$
declare v_reason text; c ad_campaigns; s ad_slots; b ad_boards; k ad_creatives; v_id uuid;
begin
  if auth.uid() is null then raise exception 'not_authenticated'; end if;
  perform _admin_actor(array['owner', 'advertising']);
  v_reason := _admin_reason(p_reason);
  perform _rate_limit('admin_write', 60, 60);
  select * into c from ad_campaigns where id = p_campaign for update;
  if not found then raise exception 'campaign_not_found'; end if;
  if c.state not in ('draft', 'rejected') then raise exception 'campaign_locked'; end if;
  if c.start_date is null or c.end_date is null then raise exception 'schedule_required'; end if;
  select * into s from ad_slots where id = p_slot; if not found then raise exception 'slot_not_found'; end if;
  select * into b from ad_boards where id = s.board_id;
  if b.status <> 'available' then raise exception 'board_not_available'; end if;
  select * into k from ad_creatives where id = p_creative;
  if not found or k.campaign_id is distinct from c.id then raise exception 'creative_not_found'; end if;
  if k.aspect <> s.aspect then raise exception 'aspect_mismatch'; end if;
  if k.status not in ('submitted', 'approved') then raise exception 'creative_not_usable'; end if;
  begin
    insert into ad_assignments(campaign_id, slot_id, creative_id, start_date, end_date, created_by) values (c.id, s.id, k.id, c.start_date, c.end_date, auth.uid()) returning id into v_id;
  exception when exclusion_violation then raise exception 'slot_conflict';
  end;
  perform _admin_log('ad_assign', 'ad_campaign', c.id::text, v_reason, jsonb_build_object('slot', s.id, 'creative', k.id, 'start', c.start_date, 'end', c.end_date));
  return jsonb_build_object('id', v_id, 'slot_id', s.id, 'start_date', c.start_date, 'end_date', c.end_date);
end $$;

create function admin_ad_unassign(p_assignment uuid, p_reason text) returns jsonb
language plpgsql security definer set search_path = public as $$
declare v_reason text; a ad_assignments; c ad_campaigns;
begin
  if auth.uid() is null then raise exception 'not_authenticated'; end if;
  perform _admin_actor(array['owner', 'advertising']);
  v_reason := _admin_reason(p_reason);
  perform _rate_limit('admin_write', 60, 60);
  select * into a from ad_assignments where id = p_assignment for update;
  if not found then raise exception 'assignment_not_found'; end if;
  select * into c from ad_campaigns where id = a.campaign_id;
  if c.state not in ('draft', 'rejected', 'paused') and not _ad_is_owner() then raise exception 'campaign_locked'; end if;       -- taking a placement off a live campaign is the owner's call
  update ad_assignments set status = 'released' where id = p_assignment;
  perform _admin_log('ad_unassign', 'ad_campaign', a.campaign_id::text, v_reason, jsonb_build_object('slot', a.slot_id));
  return jsonb_build_object('id', p_assignment, 'released', true);
end $$;

-- ================================================================ ADMIN: creatives (artwork) - registering is open to advertising, approving is the owner's alone
create function admin_ad_list_creatives(p_status text default null, p_limit integer default 100) returns jsonb
language plpgsql security definer set search_path = public as $$
declare v_lim int := least(greatest(coalesce(p_limit, 100), 1), 200);
begin
  if auth.uid() is null then raise exception 'not_authenticated'; end if;
  perform _rate_limit('admin_read', 240, 60);
  perform _admin_actor(array['owner', 'advertising']);
  if p_status is not null and p_status not in ('submitted', 'approved', 'rejected', 'withdrawn') then raise exception 'invalid_status'; end if;
  return jsonb_build_object('server_time', now(), 'items', coalesce((
    select jsonb_agg(_ad_creative_json(k) order by k.created_at desc) from (select * from ad_creatives where p_status is null or status = p_status order by created_at desc limit v_lim) k), '[]'::jsonb));
end $$;

-- registers artwork that was uploaded to the quarantine bucket (by an admin, or by an advertiser through an enquiry). The object must exist, be small, and be an image type.
create function admin_ad_register_creative(p_campaign uuid, p_name text, p_aspect text, p_path text, p_reason text) returns jsonb
language plpgsql security definer set search_path = public as $$
declare v_reason text; c ad_campaigns; v_name text; v_found boolean; v_bytes bigint; v_mime text; k ad_creatives;
begin
  if auth.uid() is null then raise exception 'not_authenticated'; end if;
  perform _admin_actor(array['owner', 'advertising']);
  v_reason := _admin_reason(p_reason);
  perform _rate_limit('admin_write', 30, 60);
  v_name := _admin_text(p_name, 2, 100, 'invalid_name');
  if p_aspect is null or p_aspect not in ('2:1', '3:1', '1:2', '16:9', '5:3', '1:1') then raise exception 'invalid_aspect'; end if;
  if p_path is null or p_path !~ '^(enquiries|admin)/[A-Za-z0-9._/-]{3,120}\.(jpg|jpeg|png|webp)$' or p_path like '%..%' then raise exception 'invalid_path'; end if;
  select * into c from ad_campaigns where id = p_campaign for update;
  if not found then raise exception 'campaign_not_found'; end if;
  if c.state not in ('draft', 'rejected') then raise exception 'campaign_locked'; end if;
  select o.present, o.size_bytes, o.mime_type into v_found, v_bytes, v_mime from _ad_object('ad-submissions', p_path) o;
  if not v_found then raise exception 'artwork_not_uploaded'; end if;
  if v_bytes < 1 or v_bytes > 307200 then raise exception 'artwork_too_large'; end if;
  if v_mime is null or v_mime not in ('image/jpeg', 'image/png', 'image/webp') then raise exception 'invalid_artwork'; end if;
  insert into ad_creatives(advertiser_id, campaign_id, name, aspect, submission_path, submission_mime, submission_bytes, created_by) values (c.advertiser_id, c.id, v_name, p_aspect, p_path, v_mime, v_bytes, auth.uid())
  returning * into k;
  perform _admin_log('ad_creative_register', 'ad_creative', k.id::text, v_reason, jsonb_build_object('campaign', c.id, 'aspect', p_aspect, 'bytes', v_bytes));
  return _ad_creative_json(k);
end $$;

-- THE artwork decision. Owner only. Approving fixes the paths where the owner's processed variants must be uploaded (the public bucket accepts nothing else). Nothing is public yet.
create function admin_ad_review_creative(p_id uuid, p_decision text, p_policy_ack boolean, p_reason text) returns jsonb
language plpgsql security definer set search_path = public as $$
declare v_reason text; k ad_creatives; v_paths jsonb; v_flags int;
begin
  if auth.uid() is null then raise exception 'not_authenticated'; end if;
  perform _admin_actor(array['owner']);
  v_reason := _admin_reason(p_reason);
  perform _rate_limit('admin_write', 30, 60);
  if p_decision is null or p_decision not in ('approve', 'reject') then raise exception 'invalid_decision'; end if;
  select * into k from ad_creatives where id = p_id for update;
  if not found then raise exception 'creative_not_found'; end if;
  if k.status <> 'submitted' then raise exception 'invalid_transition'; end if;
  if p_decision = 'approve' then
    select count(*) into v_flags from ad_campaigns c join ad_enquiries e on e.id = c.enquiry_id where c.id = k.campaign_id and cardinality(e.policy_flags) > 0;
    if v_flags > 0 and not coalesce(p_policy_ack, false) then raise exception 'policy_ack_required'; end if;
    v_paths := jsonb_build_array('creatives/' || k.id::text || '/' || replace(k.aspect, ':', 'x') || '_v1.jpg');
    update ad_creatives set status = 'approved', expected_paths = v_paths, policy_ack = coalesce(p_policy_ack, false), review_reason = v_reason, reviewed_by = auth.uid(), reviewed_at = now() where id = p_id returning * into k;
  else
    update ad_creatives set status = 'rejected', review_reason = v_reason, reviewed_by = auth.uid(), reviewed_at = now() where id = p_id returning * into k;
  end if;
  perform _admin_log(case when p_decision = 'approve' then 'ad_creative_approve' else 'ad_creative_reject' end, 'ad_creative', p_id::text, v_reason, jsonb_build_object('campaign', k.campaign_id));
  return _ad_creative_json(k);
end $$;

-- after the owner uploaded the processed variants to the public bucket: check they are there, small, and an image type, and make the creative public_ready
-- p_variants: [{path, width, height, sha256}] - one per expected path
create function admin_ad_confirm_variants(p_id uuid, p_variants jsonb, p_reason text) returns jsonb
language plpgsql security definer set search_path = public as $$
declare v_reason text; k ad_creatives; v jsonb; v_found boolean; v_bytes bigint; v_mime text; v_out jsonb := '[]'::jsonb; v_w int; v_h int;
begin
  if auth.uid() is null then raise exception 'not_authenticated'; end if;
  perform _admin_actor(array['owner']);
  v_reason := _admin_reason(p_reason);
  perform _rate_limit('admin_write', 30, 60);
  select * into k from ad_creatives where id = p_id for update;
  if not found then raise exception 'creative_not_found'; end if;
  if k.status <> 'approved' then raise exception 'creative_not_approved'; end if;
  if p_variants is null or jsonb_typeof(p_variants) <> 'array' or jsonb_array_length(p_variants) <> jsonb_array_length(k.expected_paths) then raise exception 'invalid_variants'; end if;
  for v in select * from jsonb_array_elements(p_variants) loop
    if not (k.expected_paths ? (v ->> 'path')) then raise exception 'unexpected_path'; end if;
    if coalesce(v ->> 'sha256', '') !~ '^[0-9a-f]{64}$' then raise exception 'invalid_hash'; end if;
    if coalesce(v ->> 'width', '') !~ '^[0-9]{2,4}$' or coalesce(v ->> 'height', '') !~ '^[0-9]{2,4}$' then raise exception 'invalid_dimensions'; end if;
    v_w := (v ->> 'width')::int; v_h := (v ->> 'height')::int;
    if v_w > 2048 or v_h > 2048 or v_w < 64 or v_h < 64 then raise exception 'invalid_dimensions'; end if;
    select o.present, o.size_bytes, o.mime_type into v_found, v_bytes, v_mime from _ad_object('ad-creatives-public', v ->> 'path') o;
    if not v_found then raise exception 'variant_not_uploaded'; end if;
    if v_bytes < 1 or v_bytes > 153600 then raise exception 'variant_too_large'; end if;
    if v_mime is null or v_mime not in ('image/jpeg', 'image/webp', 'image/png') then raise exception 'invalid_variant_type'; end if;
    v_out := v_out || jsonb_build_object('aspect', k.aspect, 'path', v ->> 'path', 'width', v_w, 'height', v_h, 'bytes', v_bytes, 'sha256', v ->> 'sha256', 'mime', v_mime);
  end loop;
  update ad_creatives set variants = v_out, public_ready = true where id = p_id returning * into k;
  perform _admin_log('ad_creative_publish_variants', 'ad_creative', p_id::text, v_reason, jsonb_build_object('variants', jsonb_array_length(v_out)));
  return _ad_creative_json(k);
end $$;

-- ================================================================ ADMIN: analytics - MEASURED exposure and the owner's ESTIMATE are different columns, never added together
create function admin_ad_analytics(p_from date, p_to date, p_campaign uuid default null) returns jsonb
language plpgsql security definer set search_path = public as $$
declare v_from date := coalesce(p_from, _ad_today() - 30); v_to date := coalesce(p_to, _ad_today());
begin
  if auth.uid() is null then raise exception 'not_authenticated'; end if;
  perform _rate_limit('admin_read', 120, 60);
  perform _admin_actor(array['owner', 'advertising', 'analyst']);
  if v_to < v_from or v_to - v_from > 366 then raise exception 'invalid_range'; end if;
  return jsonb_build_object('server_time', now(), 'from', v_from, 'to', v_to,
    'definitions', jsonb_build_object(
      'measured', 'Measured by the game while the board was inside its reading distance, in view, facing the player and not hidden, showing a real advertisement. A view is at least 1.5 seconds in one pass. Aggregated; no player is identified or followed between sessions.',
      'loaded', 'The image was loaded for the board. Diagnostic only: loaded is not viewed.',
      'estimated', 'An estimate: the owner''s estimated daily passes for the board multiplied by the days the campaign was live in the range. It is not a measurement and not a count of people who saw the advertisement.'),
    'campaigns', coalesce((
      select jsonb_agg(row order by (row ->> 'title')) from (
        select jsonb_build_object('campaign_id', c.id, 'title', c.title, 'advertiser', ad.business_name, 'state', _ad_effective_state(c), 'start_date', c.start_date, 'end_date', c.end_date,
                 'placements', (select count(*) from ad_assignments a where a.campaign_id = c.id and a.status = 'reserved'),
                 'measured_seconds', coalesce((select sum(x.measured_seconds) from ad_exposure_daily x where x.campaign_id = c.id and x.day between v_from and v_to), 0),
                 'measured_views', coalesce((select sum(x.measured_views) from ad_exposure_daily x where x.campaign_id = c.id and x.day between v_from and v_to), 0),
                 'loaded', coalesce((select sum(x.loaded) from ad_exposure_daily x where x.campaign_id = c.id and x.day between v_from and v_to), 0),
                 'clicks', coalesce((select sum(x.clicks) from ad_exposure_daily x where x.campaign_id = c.id and x.day between v_from and v_to), 0),
                 'clicks_supported', false,
                 'estimated_passes', coalesce((
                    select sum(coalesce(b.est_daily_passes, 0) * greatest(0, (least(a.end_date, v_to, coalesce(c.end_date, v_to)) - greatest(a.start_date, v_from, coalesce(c.start_date, v_from)) + 1)))
                      from ad_assignments a join ad_slots s on s.id = a.slot_id join ad_boards b on b.id = s.board_id where a.campaign_id = c.id and a.status = 'reserved' and c.state in ('approved', 'paused')), 0)) as row
          from ad_campaigns c join ad_advertisers ad on ad.id = c.advertiser_id where p_campaign is null or c.id = p_campaign) q), '[]'::jsonb));
end $$;

-- ================================================================ storage: two buckets and their policies (a database without Supabase Storage skips this part)
do $$ begin
  if to_regclass('storage.buckets') is not null and to_regclass('storage.objects') is not null then
    insert into storage.buckets(id, name, public, file_size_limit, allowed_mime_types) values
      ('ad-creatives-public', 'ad-creatives-public', true, 153600, array['image/webp', 'image/jpeg', 'image/png']),
      ('ad-submissions', 'ad-submissions', false, 307200, array['image/webp', 'image/jpeg', 'image/png'])
    on conflict (id) do update set public = excluded.public, file_size_limit = excluded.file_size_limit, allowed_mime_types = excluded.allowed_mime_types;
    execute 'alter table storage.objects enable row level security';
    -- public bucket: the OWNER may add an object, and only at a path an approved creative expects; the owner may remove one (a takedown). Everyone may read what is there (public URL).
    execute 'drop policy if exists aro_ad_public_owner_insert on storage.objects';
    execute 'create policy aro_ad_public_owner_insert on storage.objects for insert to authenticated with check (bucket_id = ''ad-creatives-public'' and public._ad_may_publish(name))';
    execute 'drop policy if exists aro_ad_public_owner_delete on storage.objects';
    execute 'create policy aro_ad_public_owner_delete on storage.objects for delete to authenticated using (bucket_id = ''ad-creatives-public'' and public._ad_is_owner())';
    execute 'drop policy if exists aro_ad_public_read on storage.objects';
    execute 'create policy aro_ad_public_read on storage.objects for select to anon, authenticated using (bucket_id = ''ad-creatives-public'')';
    -- submissions bucket: an anonymous advertiser may add ONE object at a path with a live ticket; only owner and advertising may read, add or remove anything there
    execute 'drop policy if exists aro_ad_sub_anon_insert on storage.objects';
    execute 'create policy aro_ad_sub_anon_insert on storage.objects for insert to anon with check (bucket_id = ''ad-submissions'' and public._ad_ticket_ok(name))';
    execute 'drop policy if exists aro_ad_sub_admin_select on storage.objects';
    execute 'create policy aro_ad_sub_admin_select on storage.objects for select to authenticated using (bucket_id = ''ad-submissions'' and public._ad_is_reviewer())';
    execute 'drop policy if exists aro_ad_sub_admin_insert on storage.objects';
    execute 'create policy aro_ad_sub_admin_insert on storage.objects for insert to authenticated with check (bucket_id = ''ad-submissions'' and public._ad_is_reviewer() and name ~ ''^admin/'')';
    execute 'drop policy if exists aro_ad_sub_admin_delete on storage.objects';
    execute 'create policy aro_ad_sub_admin_delete on storage.objects for delete to authenticated using (bucket_id = ''ad-submissions'' and public._ad_is_reviewer())';
  else
    raise notice 'storage schema not present: the advertising buckets and their policies were not created (run supabase/storage_ads.sql once Storage is on)';
  end if;
end $$;

-- ================================================================ lock-down (same convention as every migration)
do $$ declare r record;
begin
  for r in select p.oid::regprocedure as sig, p.proname from pg_proc p
            where p.pronamespace = 'public'::regnamespace and p.oid not in (select oid from _fn_before) loop
    execute format('revoke execute on function %s from public, anon, authenticated', r.sig);
    -- signed-in players
    if r.proname in ('get_ad_manifest', 'record_ad_exposure') then execute format('grant execute on function %s to authenticated', r.sig); end if;
    -- the admin site
    if r.proname in ('admin_ad_overview', 'admin_ad_list_boards', 'admin_ad_set_board_status', 'admin_ad_list_packages', 'admin_ad_save_package', 'admin_ad_set_price', 'admin_ad_list_enquiries',
                     'admin_ad_update_enquiry', 'admin_ad_convert_enquiry', 'admin_ad_create_campaign', 'admin_ad_list_campaigns', 'admin_ad_save_campaign', 'admin_ad_submit_campaign', 'admin_ad_review_campaign',
                     'admin_ad_set_campaign_state', 'admin_ad_set_payment_status', 'admin_ad_assign', 'admin_ad_unassign', 'admin_ad_list_creatives', 'admin_ad_register_creative',
                     'admin_ad_review_creative', 'admin_ad_confirm_variants', 'admin_ad_analytics') then execute format('grant execute on function %s to authenticated', r.sig); end if;
    -- anonymous advertisers: exactly two functions
    if r.proname in ('submit_ad_enquiry', 'request_ad_upload') then execute format('grant execute on function %s to anon, authenticated', r.sig); end if;
    -- storage policies call these (booleans about the caller or about a path; they change nothing and reveal nothing else)
    if r.proname in ('_ad_is_owner', '_ad_is_reviewer', '_ad_may_publish', '_ad_ticket_ok') then execute format('grant execute on function %s to anon, authenticated', r.sig); end if;
  end loop;
end $$;
