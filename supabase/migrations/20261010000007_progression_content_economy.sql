-- Backend foundation, step 7: wallet API + ledger audit trail, progression + achievements, authored content, daily-challenge
-- foundation, leaderboards, and the one-round-trip dashboard summary.
-- Money stays append-only: every coin movement is an immutable `transactions` row with a unique idempotency key and a reason.
create temp table _fn_before as select oid from pg_proc where pronamespace = 'public'::regnamespace;

-- ---------------------------------------------------------------- ledger audit + derived stats
alter table transactions add column reason text check (reason is null or char_length(reason) <= 200);
create index transactions_kind_created_idx on transactions(kind, created_at desc);

-- Every row carries a human-readable reason. Server code may pass one through the transaction-local setting aro.tx_reason
-- (admin tooling does); otherwise it defaults to the kind, e.g. 'job_reward'. Clients have no insert path at all.
create function _transactions_before_insert() returns trigger
language plpgsql security definer set search_path = public as $$
begin
  new.reason := coalesce(new.reason, nullif(current_setting('aro.tx_reason', true), ''), lower(new.kind::text));
  return new;
end $$;
create trigger transactions_reason before insert on transactions for each row execute function _transactions_before_insert();

create function _transactions_after_insert() returns trigger
language plpgsql security definer set search_path = public as $$
declare v_code text;
begin
  if new.amount > 0 and new.kind in ('JOB_REWARD','BONUS') then
    update profiles set lifetime_earnings = lifetime_earnings + new.amount where id = new.player_id;
  end if;
  -- One payout notification per delivery (bus stops pay per stop and are not announced individually).
  if new.kind = 'JOB_REWARD' and new.ref_type = 'job_assignment' then
    select j.code into v_code from job_assignments a join jobs j on j.id = a.job_id where a.id = new.ref_id;
    perform _notify(new.player_id, 'payout', 'Delivery paid', 'You earned ' || new.amount || ' coins for job ' || coalesce(v_code, '') || '.',
                    jsonb_build_object('amount', new.amount, 'assignment_id', new.ref_id, 'balance_after', new.balance_after),
                    'payout:' || new.id);
  end if;
  return new;
end $$;
create trigger transactions_after after insert on transactions for each row execute function _transactions_after_insert();

-- ---------------------------------------------------------------- wallet API
create function get_my_wallet() returns jsonb
language plpgsql security definer set search_path = public as $$
declare v_uid uuid := auth.uid();
begin
  if v_uid is null then raise exception 'not_authenticated'; end if;
  perform _rate_limit('wallet_read', 120, 60);
  return jsonb_build_object('balance', coalesce((select balance from player_wallets where player_id = v_uid), 0),
    'lifetime_earnings', (select lifetime_earnings from profiles where id = v_uid), 'server_time', now());
end $$;

create function list_my_transactions(p_limit integer default 30, p_before_id bigint default null) returns jsonb
language plpgsql security definer set search_path = public as $$
declare v_uid uuid := auth.uid();
begin
  if v_uid is null then raise exception 'not_authenticated'; end if;
  perform _rate_limit('wallet_read', 120, 60);
  return jsonb_build_object('items', coalesce((
    select jsonb_agg(jsonb_build_object('id', t.id, 'kind', t.kind, 'amount', t.amount, 'balance_after', t.balance_after,
             'reason', t.reason, 'ref_type', t.ref_type, 'ref_id', t.ref_id, 'created_at', t.created_at) order by t.id desc)
      from (select * from transactions where player_id = v_uid and (p_before_id is null or id < p_before_id)
             order by id desc limit least(greatest(coalesce(p_limit, 30), 1), 100)) t), '[]'::jsonb), 'server_time', now());
end $$;

-- Operator tooling (service role; never granted to clients). Each is an immutable ledger row, replay-safe by its key.
create function admin_adjust_wallet(p_player uuid, p_amount bigint, p_reason text, p_key text) returns boolean
language plpgsql security definer set search_path = public as $$
declare v_ok boolean;
begin
  if p_amount = 0 then raise exception 'invalid_amount'; end if;
  if p_reason is null or char_length(trim(p_reason)) < 3 then raise exception 'reason_required'; end if;
  if p_key is null or char_length(trim(p_key)) < 3 then raise exception 'key_required'; end if;
  perform set_config('aro.tx_reason', left(trim(p_reason), 200), true);
  begin
    v_ok := _apply_transaction(p_player, 'ADJUSTMENT', p_amount, 'adj:' || trim(p_key), 'admin', null);
  exception when check_violation then raise exception 'insufficient_funds';
  end;
  perform set_config('aro.tx_reason', '', true);
  return v_ok;
end $$;

create function admin_refund_transaction(p_tx bigint, p_reason text) returns boolean
language plpgsql security definer set search_path = public as $$
declare t transactions; v_ok boolean;
begin
  select * into t from transactions where id = p_tx;
  if not found then raise exception 'transaction_not_found'; end if;
  if t.amount >= 0 or t.kind not in ('FUEL_PURCHASE','REPAIR','VEHICLE_PURCHASE','PENALTY') then raise exception 'not_refundable'; end if;
  if p_reason is null or char_length(trim(p_reason)) < 3 then raise exception 'reason_required'; end if;
  perform set_config('aro.tx_reason', left('refund of transaction ' || p_tx || ': ' || trim(p_reason), 200), true);
  v_ok := _apply_transaction(t.player_id, 'REFUND', -t.amount, 'refund:' || p_tx, 'transaction', null);
  perform set_config('aro.tx_reason', '', true);
  return v_ok;
end $$;

create function admin_broadcast(p_title text, p_message text, p_key text) returns integer
language plpgsql security definer set search_path = public as $$
declare r record; n integer := 0;
begin
  if p_key is null or char_length(trim(p_key)) < 3 then raise exception 'key_required'; end if;
  for r in select id from profiles loop
    if _notify(r.id, 'server', p_title, p_message, '{}'::jsonb, 'broadcast:' || trim(p_key)) is not null then n := n + 1; end if;
  end loop;
  return n;
end $$;

-- ---------------------------------------------------------------- achievements + progression
create table achievement_definitions (
  id          text primary key check (id ~ '^[a-z0-9_]{3,40}$'),
  title       text not null,
  description text not null,
  metric      text not null check (metric in ('jobs_completed','distance_km','level')),
  threshold   numeric not null check (threshold > 0),
  sort_order  integer not null default 0
);
create table player_achievements (
  player_id      uuid not null references profiles(id) on delete cascade,
  achievement_id text not null references achievement_definitions(id),
  unlocked_at    timestamptz not null default now(),
  primary key (player_id, achievement_id)
);
alter table achievement_definitions enable row level security;
alter table player_achievements enable row level security;
revoke all on achievement_definitions, player_achievements from anon, authenticated;

-- Definitions are game design data. The distance ladder is the dashboard's milestone ladder (50 km ... 25,000 km).
insert into achievement_definitions(id, title, description, metric, threshold, sort_order) values
  ('first_delivery',  'First delivery',   'Complete your first job.',            'jobs_completed', 1,     10),
  ('deliveries_10',   'Regular driver',   'Complete 10 jobs.',                   'jobs_completed', 10,    11),
  ('deliveries_50',   'Road veteran',     'Complete 50 jobs.',                   'jobs_completed', 50,    12),
  ('deliveries_100',  'Century hauler',   'Complete 100 jobs.',                  'jobs_completed', 100,   13),
  ('distance_50',     'Drive 50 km',      'Drive 50 km in total.',               'distance_km',    50,    20),
  ('distance_100',    'Drive 100 km',     'Drive 100 km in total.',              'distance_km',    100,   21),
  ('distance_250',    'Drive 250 km',     'Drive 250 km in total.',              'distance_km',    250,   22),
  ('distance_500',    'Drive 500 km',     'Drive 500 km in total.',              'distance_km',    500,   23),
  ('distance_1000',   'Drive 1,000 km',   'Drive 1,000 km in total.',            'distance_km',    1000,  24),
  ('distance_2500',   'Drive 2,500 km',   'Drive 2,500 km in total.',            'distance_km',    2500,  25),
  ('distance_5000',   'Drive 5,000 km',   'Drive 5,000 km in total.',            'distance_km',    5000,  26),
  ('distance_10000',  'Drive 10,000 km',  'Drive 10,000 km in total.',           'distance_km',    10000, 27),
  ('distance_25000',  'Drive 25,000 km',  'Drive 25,000 km in total.',           'distance_km',    25000, 28),
  ('level_5',         'Level 5',          'Reach level 5.',                      'level',          5,     30),
  ('level_10',        'Level 10',         'Reach level 10.',                     'level',          10,    31),
  ('level_25',        'Level 25',         'Reach level 25.',                     'level',          25,    32)
on conflict (id) do nothing;

create function _achievement_value(p profiles, p_metric text) returns numeric
language sql immutable as $$
  select case p_metric when 'jobs_completed' then p.jobs_completed::numeric when 'distance_km' then p.distance_km when 'level' then p.level::numeric end $$;

-- Unlocks every definition the player now satisfies. Idempotent (primary key); each unlock notifies once. Returns how many unlocked now.
create function _check_achievements(p_player uuid) returns integer
language plpgsql security definer set search_path = public as $$
declare p profiles; d achievement_definitions; v_rows integer; n integer := 0;
begin
  select * into p from profiles where id = p_player;
  for d in select * from achievement_definitions order by sort_order loop
    if _achievement_value(p, d.metric) >= d.threshold then
      insert into player_achievements(player_id, achievement_id) values (p_player, d.id) on conflict do nothing;
      get diagnostics v_rows = row_count;
      if v_rows > 0 then
        n := n + 1;
        perform _notify(p_player, 'achievement', 'Achievement unlocked', d.title || ': ' || d.description,
                        jsonb_build_object('achievement_id', d.id), 'achv:' || d.id);
        perform _track(p_player, 'achievement_unlocked', jsonb_build_object('achievement_id', d.id));
      end if;
    end if;
  end loop;
  return n;
end $$;

create function _progression_json(p_player uuid) returns jsonb
language plpgsql stable security definer set search_path = public as $$
declare p profiles; v_start bigint; v_next bigint; m achievement_definitions; v_target numeric; v_cur numeric;
begin
  select * into p from profiles where id = p_player;
  v_start := xp_for_level(p.level); v_next := xp_for_level(p.level + 1);
  select d.* into m from achievement_definitions d
   where d.metric = 'distance_km' and not exists (select 1 from player_achievements a where a.player_id = p_player and a.achievement_id = d.id)
   order by d.threshold limit 1;
  v_cur := p.distance_km;
  if m.id is not null then v_target := m.threshold;
  else v_target := (floor(p.distance_km / 25000) + 1) * 25000; end if;     -- past the ladder: next multiple of 25,000 km
  return jsonb_build_object(
    'level', p.level, 'experience', p.experience, 'xp_level_start', v_start, 'xp_next_level', v_next,
    'xp_into_level', p.experience - v_start, 'xp_to_next', v_next - p.experience,
    'level_progress', round(((p.experience - v_start)::numeric / greatest(v_next - v_start, 1))::numeric, 4),
    'distance_km', p.distance_km, 'jobs_completed', p.jobs_completed,
    'achievements', jsonb_build_object('unlocked', (select count(*) from player_achievements where player_id = p_player),
                                       'total', (select count(*) from achievement_definitions)),
    'next_milestone', jsonb_build_object('id', m.id, 'title', 'Drive ' || to_char(v_target, 'FM999,999,999') || ' km',
                        'target', v_target, 'current', round(v_cur, 2), 'fraction', round(least(1, v_cur / v_target), 4)));
end $$;

create function get_my_progression() returns jsonb
language plpgsql security definer set search_path = public as $$
begin
  if auth.uid() is null then raise exception 'not_authenticated'; end if;
  perform _rate_limit('progress_read', 120, 60);
  return _progression_json(auth.uid()) || jsonb_build_object('server_time', now());
end $$;

create function list_my_achievements() returns jsonb
language plpgsql security definer set search_path = public as $$
declare v_uid uuid := auth.uid(); p profiles;
begin
  if v_uid is null then raise exception 'not_authenticated'; end if;
  perform _rate_limit('progress_read', 120, 60);
  select * into p from profiles where id = v_uid;
  return jsonb_build_object('items', coalesce((
    select jsonb_agg(jsonb_build_object('id', d.id, 'title', d.title, 'description', d.description, 'metric', d.metric, 'threshold', d.threshold,
             'current', least(_achievement_value(p, d.metric), d.threshold), 'unlocked_at', a.unlocked_at) order by d.sort_order)
      from achievement_definitions d left join player_achievements a on a.achievement_id = d.id and a.player_id = v_uid), '[]'::jsonb),
    'server_time', now());
end $$;

-- ---------------------------------------------------------------- daily challenges (foundation; the UI still says "coming soon")
create table challenge_definitions (
  id           text primary key check (id ~ '^[a-z0-9_]{3,40}$'),
  title        text not null,
  description  text not null,
  metric       text not null check (metric in ('jobs_completed','distance_km')),
  target       numeric not null check (target > 0),
  reward_coins bigint not null default 0 check (reward_coins >= 0),
  reward_xp    integer not null default 0 check (reward_xp >= 0),
  active       boolean not null default false
);
create table player_challenges (
  id           uuid primary key default gen_random_uuid(),
  player_id    uuid not null references profiles(id) on delete cascade,
  challenge_id text not null references challenge_definitions(id),
  day          date not null,
  progress     numeric not null default 0 check (progress >= 0),
  completed_at timestamptz,
  claimed_at   timestamptz,
  expires_at   timestamptz not null,
  unique (player_id, challenge_id, day)
);
create index player_challenges_player_day_idx on player_challenges(player_id, day);
alter table challenge_definitions enable row level security;
alter table player_challenges enable row level security;
revoke all on challenge_definitions, player_challenges from anon, authenticated;

-- Assigns today's (UTC) challenges from the ACTIVE definitions. No active definitions => nothing is assigned: no placeholders.
create function _assign_daily(p_player uuid) returns void
language plpgsql security definer set search_path = public as $$
declare v_day date := (now() at time zone 'utc')::date; v_have integer;
begin
  select count(*) into v_have from player_challenges where player_id = p_player and day = v_day;
  if v_have >= cfg('daily_challenge_count') then return; end if;
  insert into player_challenges(player_id, challenge_id, day, expires_at)
  select p_player, d.id, v_day, ((v_day + 1)::timestamp at time zone 'utc')
    from challenge_definitions d
   where d.active and not exists (select 1 from player_challenges c where c.player_id = p_player and c.challenge_id = d.id and c.day = v_day)
   order by md5(p_player::text || v_day::text || d.id)
   limit greatest(cfg('daily_challenge_count')::int - v_have, 0)
  on conflict do nothing;
end $$;

create function _challenge_progress(p_player uuid, p_metric text, p_delta numeric) returns void
language plpgsql security definer set search_path = public as $$
declare c record;
begin
  if p_delta <= 0 then return; end if;
  perform _assign_daily(p_player);
  for c in select pc.id, pc.progress, d.target, d.title from player_challenges pc join challenge_definitions d on d.id = pc.challenge_id
            where pc.player_id = p_player and d.metric = p_metric and pc.completed_at is null and pc.expires_at > now() for update of pc loop
    update player_challenges set progress = least(c.target, c.progress + p_delta),
           completed_at = case when c.progress + p_delta >= c.target then now() end where id = c.id;
    if c.progress + p_delta >= c.target then
      perform _notify(p_player, 'achievement', 'Challenge complete', c.title || ' is done. Claim your reward.',
                      jsonb_build_object('challenge_id', c.id), 'chal:' || c.id);
      perform _track(p_player, 'challenge_completed', '{}'::jsonb);
    end if;
  end loop;
end $$;

create function get_my_daily_challenges() returns jsonb
language plpgsql security definer set search_path = public as $$
declare v_uid uuid := auth.uid(); v_day date := (now() at time zone 'utc')::date;
begin
  if v_uid is null then raise exception 'not_authenticated'; end if;
  perform _rate_limit('challenge_read', 60, 60);
  perform _assign_daily(v_uid);
  return jsonb_build_object(
    'available', exists (select 1 from challenge_definitions where active),
    'items', coalesce((select jsonb_agg(jsonb_build_object('id', pc.id, 'challenge_id', d.id, 'title', d.title, 'description', d.description,
                'metric', d.metric, 'target', d.target, 'progress', pc.progress, 'completed', pc.completed_at is not null,
                'claimed', pc.claimed_at is not null, 'reward_coins', d.reward_coins, 'reward_xp', d.reward_xp, 'expires_at', pc.expires_at)
                order by d.id)
         from player_challenges pc join challenge_definitions d on d.id = pc.challenge_id
        where pc.player_id = v_uid and pc.day = v_day), '[]'::jsonb),
    'server_time', now());
end $$;

-- Reward = ledger row (idempotency key) + XP, exactly once; the row lock serialises double-clicks.
create function claim_challenge_reward(p_id uuid) returns jsonb
language plpgsql security definer set search_path = public as $$
declare v_uid uuid := auth.uid(); pc player_challenges; d challenge_definitions;
begin
  if v_uid is null then raise exception 'not_authenticated'; end if;
  perform _rate_limit('challenge_write', 30, 60);
  select * into pc from player_challenges where id = p_id and player_id = v_uid for update;
  if not found then raise exception 'challenge_not_found'; end if;
  if pc.completed_at is null then raise exception 'challenge_not_complete'; end if;
  if pc.claimed_at is not null then raise exception 'already_claimed'; end if;
  if pc.expires_at <= now() then raise exception 'challenge_expired'; end if;
  select * into d from challenge_definitions where id = pc.challenge_id;
  update player_challenges set claimed_at = now() where id = pc.id;
  if d.reward_coins > 0 then
    perform _apply_transaction(v_uid, 'CHALLENGE_REWARD', d.reward_coins, 'challenge:' || pc.id, 'challenge', pc.id);
  end if;
  if d.reward_xp > 0 then
    update profiles set experience = experience + d.reward_xp, level = level_for_xp(experience + d.reward_xp) where id = v_uid;
  end if;
  return jsonb_build_object('coins', d.reward_coins, 'xp', d.reward_xp,
                            'balance', (select balance from player_wallets where player_id = v_uid));
end $$;

-- ---------------------------------------------------------------- progress hook (level-ups, route unlocks, achievements, challenges)
create function _profile_progress() returns trigger
language plpgsql security definer set search_path = public as $$
declare r record;
begin
  if new.level > old.level then
    perform _notify(new.id, 'level_up', 'Level up!', 'You reached level ' || new.level || '.', jsonb_build_object('level', new.level), 'level:' || new.level);
    perform _track(new.id, 'level_up', jsonb_build_object('level', new.level));
    for r in select code, name from world_routes where status = 'OPEN' and unlock_level > old.level and unlock_level <= new.level loop
      perform _notify(new.id, 'system', 'New route unlocked', r.name || ' is now open to you.', jsonb_build_object('route_code', r.code), 'route:' || r.code);
      perform _track(new.id, 'route_unlocked', jsonb_build_object('route_code', r.code));
    end loop;
  end if;
  perform _check_achievements(new.id);
  if new.jobs_completed > old.jobs_completed then perform _challenge_progress(new.id, 'jobs_completed', new.jobs_completed - old.jobs_completed); end if;
  if new.distance_km > old.distance_km then perform _challenge_progress(new.id, 'distance_km', new.distance_km - old.distance_km); end if;
  return new;
end $$;
create trigger profiles_progress after update of experience, level, distance_km, jobs_completed on profiles
  for each row execute function _profile_progress();

-- ---------------------------------------------------------------- authored content
create table content_items (
  id         text primary key check (id ~ '^[a-z0-9_-]{2,40}$'),
  kind       text not null default 'news' check (kind in ('news','hero')),
  category   text not null default 'update' check (category in ('update','event','tip','maintenance')),
  title      text not null check (char_length(title) between 1 and 80),
  body       text not null check (char_length(body) between 1 and 220),
  image_key  text check (image_key is null or image_key ~ '^[a-z0-9_-]{1,40}$'),
  priority   integer not null default 0,
  published  boolean not null default false,
  publish_at timestamptz not null default now(),
  expires_at timestamptz,
  source     text not null default 'authored' check (source = 'authored'),   -- in-house content; never presented as external news
  created_at timestamptz not null default now()
);
create index content_items_feed_idx on content_items(kind, priority desc, publish_at desc) where published;
alter table content_items enable row level security;
revoke all on content_items from anon, authenticated;

create function get_content(p_kind text default 'news', p_limit integer default 10) returns jsonb
language plpgsql security definer set search_path = public as $$
begin
  if auth.uid() is null then raise exception 'not_authenticated'; end if;
  perform _rate_limit('content_read', 60, 60);
  return jsonb_build_object('items', coalesce((
    select jsonb_agg(jsonb_build_object('id', c.id, 'kind', c.kind, 'category', c.category, 'title', c.title, 'body', c.body,
             'image_key', c.image_key, 'priority', c.priority, 'publish_at', c.publish_at, 'expires_at', c.expires_at, 'source', c.source)
             order by c.priority desc, c.publish_at desc, c.id)
      from (select * from content_items where kind = p_kind and published and publish_at <= now() and (expires_at is null or expires_at > now())
             order by priority desc, publish_at desc, id limit least(greatest(coalesce(p_limit, 10), 1), 50)) c), '[]'::jsonb),
    'server_time', now());
end $$;

-- ---------------------------------------------------------------- leaderboards
-- Ranks come from server-side columns only (never client-reported). All-time boards read the per-metric indexes on profiles;
-- weekly boards aggregate this ISO week's completed jobs / ledger. xp has no weekly board (XP is not logged per event).
create function get_leaderboard(p_metric text, p_period text default 'all', p_limit integer default 20, p_offset integer default 0) returns jsonb
language plpgsql security definer set search_path = public as $$
declare
  v_uid uuid := auth.uid(); v_lim integer := least(greatest(coalesce(p_limit, 20), 1), 100); v_off integer := greatest(coalesce(p_offset, 0), 0);
  v_col text; v_items jsonb; v_total integer; v_me jsonb; v_week timestamptz := date_trunc('week', now());
begin
  if v_uid is null then raise exception 'not_authenticated'; end if;
  perform _rate_limit('leaderboard', 60, 60);
  if p_metric not in ('xp','distance','jobs','earnings') then raise exception 'invalid_metric'; end if;
  if p_period not in ('all','weekly') then raise exception 'invalid_period'; end if;
  if p_period = 'weekly' and p_metric = 'xp' then raise exception 'unsupported_period'; end if;

  if p_period = 'all' then
    v_col := case p_metric when 'xp' then 'experience' when 'distance' then 'distance_km' when 'jobs' then 'jobs_completed' else 'lifetime_earnings' end;
    execute format($q$
      select coalesce(jsonb_agg(jsonb_build_object('rank', x.rank, 'player_id', x.id, 'display_name', x.display_name, 'username', x.username,
               'avatar_id', x.avatar_id, 'level', x.level, 'country_code', x.country_code, 'value', x.value) order by x.rank, x.id), '[]'::jsonb)
        from (select p.id, p.display_name, p.username, p.avatar_id, p.level, p.country_code, p.%1$I as value,
                     (select count(*) + 1 from profiles o where o.%1$I > p.%1$I) as rank
                from profiles p where p.%1$I > 0 order by p.%1$I desc, p.id limit %2$s offset %3$s) x$q$, v_col, v_lim, v_off) into v_items;
    execute format('select count(*)::int from profiles where %1$I > 0', v_col) into v_total;
    execute format($q$select jsonb_build_object('rank', case when p.%1$I > 0 then (select count(*) + 1 from profiles o where o.%1$I > p.%1$I) end, 'value', p.%1$I)
                        from profiles p where p.id = %2$L$q$, v_col, v_uid) into v_me;
  else
    create temp table if not exists _lb(player_id uuid, value numeric) on commit drop;
    delete from _lb;
    if p_metric = 'jobs' then
      insert into _lb select player_id, count(*) from job_assignments where status = 'completed' and completed_at >= v_week group by player_id;
    elsif p_metric = 'distance' then
      insert into _lb select player_id, sum(coalesce(reported_distance_km, 0)) from job_assignments where status = 'completed' and completed_at >= v_week group by player_id;
    else
      insert into _lb select player_id, sum(amount) from transactions where kind in ('JOB_REWARD','BONUS') and amount > 0 and created_at >= v_week group by player_id;
    end if;
    select count(*)::int into v_total from _lb where value > 0;
    select coalesce(jsonb_agg(jsonb_build_object('rank', x.rank, 'player_id', x.player_id, 'display_name', pr.display_name, 'username', pr.username,
             'avatar_id', pr.avatar_id, 'level', pr.level, 'country_code', pr.country_code, 'value', x.value) order by x.rank, x.player_id), '[]'::jsonb) into v_items
      from (select player_id, value, rank() over (order by value desc) as rank from _lb where value > 0 order by value desc, player_id limit v_lim offset v_off) x
      join profiles pr on pr.id = x.player_id;
    select jsonb_build_object('rank', case when coalesce(l.value, 0) > 0 then (select count(*) + 1 from _lb o where o.value > l.value) end, 'value', coalesce(l.value, 0))
      into v_me from (select v_uid as id) u left join _lb l on l.player_id = u.id;
  end if;
  return jsonb_build_object('metric', p_metric, 'period', p_period, 'total', v_total, 'limit', v_lim, 'offset', v_off,
                            'items', v_items, 'me', v_me, 'server_time', now());
end $$;

-- ---------------------------------------------------------------- dashboard summary (one round trip for the home screen)
create function get_dashboard_summary() returns jsonb
language plpgsql security definer set search_path = public as $$
declare
  v_uid uuid := auth.uid(); p profiles; v_level integer; v_sel uuid; v_veh vehicle_ownership; v_conv convoys;
  v_vehicle jsonb; v_convoy jsonb; v_active jsonb; a job_assignments; j jobs;
begin
  if v_uid is null then raise exception 'not_authenticated'; end if;
  perform _rate_limit('dashboard_read', 60, 60);
  select * into p from profiles where id = v_uid;
  if not found then raise exception 'profile_not_found'; end if;
  v_level := p.level;
  v_sel := _selected_vehicle(v_uid);
  select * into v_veh from vehicle_ownership where id = v_sel;
  if found then v_vehicle := _vehicle_json(v_veh, (select d from vehicle_definitions d where d.id = v_veh.definition_id), v_sel); end if;
  select cv.* into v_conv from convoy_members m join convoys cv on cv.id = m.convoy_id where m.player_id = v_uid and cv.disbanded_at is null;
  if found then
    v_convoy := jsonb_build_object('id', v_conv.id, 'name', v_conv.name, 'is_leader', v_conv.leader_id = v_uid, 'capacity', v_conv.capacity,
                                   'member_count', (select count(*) from convoy_members where convoy_id = v_conv.id));
  end if;
  select * into a from job_assignments where player_id = v_uid and status in ('accepted','in_progress');
  if found then
    select * into j from jobs where id = a.job_id;
    v_active := jsonb_build_object('assignment_id', a.id, 'status', a.status, 'job_code', j.code, 'distance_km', j.distance_km, 'reward', j.reward,
      'verified_km', coalesce((select verified_km from job_telemetry where assignment_id = a.id), 0));
  end if;
  return jsonb_build_object(
    'server_time', now(),
    'profile', jsonb_build_object('id', p.id, 'display_name', p.display_name, 'username', p.username, 'avatar_id', p.avatar_id,
                                  'country_code', p.country_code, 'level', p.level),
    'coins', coalesce((select balance from player_wallets where player_id = v_uid), 0),
    'progression', _progression_json(v_uid),
    'vehicle', v_vehicle,
    'vehicle_count', (select count(*) from vehicle_ownership where player_id = v_uid),
    'jobs', jsonb_build_object(
      'available', (select count(*) from jobs x where x.status = 'open' and x.expires_at > now()
                       and not exists (select 1 from job_assignments y where y.job_id = x.id and y.player_id = v_uid)),
      'best_reward', (select max(x.reward) from jobs x where x.status = 'open' and x.expires_at > now()
                       and not exists (select 1 from job_assignments y where y.job_id = x.id and y.player_id = v_uid))),
    'active_job', v_active,
    'world', jsonb_build_object('cities', (select count(*) from cities), 'routes', (select count(*) from world_routes),
                                'routes_open', (select count(*) from world_routes where _effective_status(status, unlock_level, v_level) = 'OPEN')),
    'online_players', (select count(distinct player_id)::int from game_sessions
                        where ended_at is null and last_seen_at > now() - make_interval(secs => cfg('presence_ttl_seconds'))),
    'friends_online', (select count(*)::int from friendships f
                        where f.status = 'accepted' and v_uid in (f.requester_id, f.addressee_id)
                          and (_presence_of(case when f.requester_id = v_uid then f.addressee_id else f.requester_id end)->>'online')::boolean),
    'pending_friend_requests', (select count(*) from friendships where addressee_id = v_uid and status = 'pending'),
    'pending_convoy_invites', (select count(*) from convoy_invites where invitee_id = v_uid and status = 'pending' and expires_at > now()),
    'unread_notifications', (select count(*) from notifications where player_id = v_uid and read_at is null),
    'convoy', v_convoy);
end $$;

-- ---------------------------------------------------------------- lock-down
do $$ declare r record;
begin
  for r in select p.oid::regprocedure as sig, p.proname from pg_proc p
            where p.pronamespace = 'public'::regnamespace and p.oid not in (select oid from _fn_before) loop
    execute format('revoke execute on function %s from public, anon, authenticated', r.sig);
    if r.proname in ('get_my_wallet','list_my_transactions','get_my_progression','list_my_achievements','get_my_daily_challenges',
                     'claim_challenge_reward','get_content','get_leaderboard','get_dashboard_summary') then
      execute format('grant execute on function %s to authenticated', r.sig);
    end if;
  end loop;
end $$;
