-- African Roads Online: core schema (phase 1 slice).
-- Principle: clients may READ their own data. All economy/progression writes go through
-- SECURITY DEFINER functions (see 20261009000002). Direct table writes are denied by RLS.

create extension if not exists pgcrypto;

-- ---------------------------------------------------------------- geography
-- Purpose: world hierarchy (country > city > location). Roads/sectors/chunks arrive with streaming.
create table countries (
  code        text primary key check (code ~ '^[A-Z]{2}$'),
  name        text not null unique
);

create table cities (
  id          uuid primary key default gen_random_uuid(),
  country_code text not null references countries(code),
  name        text not null,
  lat         double precision not null check (lat between -90 and 90),
  lng         double precision not null check (lng between -180 and 180),
  unique (country_code, name)
);

-- Purpose: pickup/delivery points. world_x/z are Unity-space metres, used for delivery validation.
create table locations (
  id          uuid primary key default gen_random_uuid(),
  city_id     uuid not null references cities(id),
  slug        text not null unique,
  name        text not null,
  kind        text not null check (kind in ('depot','port','market','factory','station','warehouse')),
  world_x     double precision not null,
  world_z     double precision not null
);
create index locations_city_idx on locations(city_id);

-- ---------------------------------------------------------------- vehicles
-- Purpose: data-driven vehicle catalogue. `stats` jsonb mirrors the Unity VehicleDefinition asset.
create table vehicle_definitions (
  id          text primary key,
  category    text not null check (category in ('truck','bus','van')),
  name        text not null,
  price       bigint not null check (price >= 0),
  cargo_capacity_kg integer not null default 0 check (cargo_capacity_kg >= 0),
  passenger_capacity integer not null default 0 check (passenger_capacity >= 0),
  fuel_capacity_l numeric not null check (fuel_capacity_l > 0),
  max_speed_kmh numeric not null check (max_speed_kmh > 0),
  stats       jsonb not null default '{}'::jsonb
);

-- ---------------------------------------------------------------- players
-- Purpose: public player profile. 1:1 with auth.users.
create table profiles (
  id            uuid primary key references auth.users(id) on delete cascade,
  display_name  text not null check (char_length(display_name) between 3 and 24),
  level         integer not null default 1 check (level >= 1),
  experience    bigint not null default 0 check (experience >= 0),
  distance_km   numeric not null default 0 check (distance_km >= 0),
  jobs_completed integer not null default 0 check (jobs_completed >= 0),
  created_at    timestamptz not null default now()
);
create unique index profiles_display_name_ci on profiles (lower(display_name));

-- Purpose: wallet balance. Only mutated by ledger functions; balance can never go negative.
create table player_wallets (
  player_id   uuid primary key references profiles(id) on delete cascade,
  balance     bigint not null default 0 check (balance >= 0),
  updated_at  timestamptz not null default now()
);

-- Purpose: append-only audit ledger. idempotency_key makes every economy event replay-safe.
create type transaction_kind as enum
  ('JOB_REWARD','FUEL_PURCHASE','REPAIR','VEHICLE_PURCHASE','PENALTY','BONUS','STARTER_GRANT');

create table transactions (
  id            bigint generated always as identity primary key,
  player_id     uuid not null references profiles(id),
  kind          transaction_kind not null,
  amount        bigint not null check (amount <> 0),
  balance_after bigint not null check (balance_after >= 0),
  ref_type      text,
  ref_id        uuid,
  idempotency_key text not null unique,
  created_at    timestamptz not null default now()
);
create index transactions_player_idx on transactions(player_id, created_at desc);

-- Purpose: vehicles a player owns, with persistent condition.
create table vehicle_ownership (
  id          uuid primary key default gen_random_uuid(),
  player_id   uuid not null references profiles(id) on delete cascade,
  definition_id text not null references vehicle_definitions(id),
  fuel_l      numeric not null check (fuel_l >= 0),
  damage_pct  numeric not null default 0 check (damage_pct between 0 and 100),
  odometer_km numeric not null default 0 check (odometer_km >= 0),
  acquired_at timestamptz not null default now()
);
create index vehicle_ownership_player_idx on vehicle_ownership(player_id);

-- ---------------------------------------------------------------- jobs
create type job_status as enum ('open','closed','expired');
create type assignment_status as enum ('accepted','in_progress','completed','failed','abandoned');

-- Purpose: contracts on the board. reward/distance are authoritative here, never client-supplied.
create table jobs (
  id          uuid primary key default gen_random_uuid(),
  code        text not null unique,
  origin_id   uuid not null references locations(id),
  destination_id uuid not null references locations(id),
  cargo_type  text not null,
  cargo_weight_kg integer not null check (cargo_weight_kg > 0),
  distance_km numeric not null check (distance_km > 0),
  difficulty  smallint not null check (difficulty between 1 and 5),
  reward      bigint not null check (reward > 0),
  xp_reward   integer not null check (xp_reward >= 0),
  required_category text not null check (required_category in ('truck','bus','van')),
  max_participants smallint not null default 1 check (max_participants between 1 and 8),
  status      job_status not null default 'open',
  created_at  timestamptz not null default now(),
  expires_at  timestamptz not null default now() + interval '1 day',
  check (origin_id <> destination_id)
);
create index jobs_open_idx on jobs(status, expires_at);
create sequence job_code_seq start 1;

-- Purpose: one row per player per job; the state machine the server validates.
create table job_assignments (
  id          uuid primary key default gen_random_uuid(),
  job_id      uuid not null references jobs(id),
  player_id   uuid not null references profiles(id),
  vehicle_id  uuid not null references vehicle_ownership(id),
  status      assignment_status not null default 'accepted',
  accepted_at timestamptz not null default now(),
  started_at  timestamptz,
  completed_at timestamptz,
  reported_distance_km numeric,
  unique (job_id, player_id)
);
-- A player may have only one active assignment.
create unique index one_active_assignment on job_assignments(player_id)
  where status in ('accepted','in_progress');
create index job_assignments_job_idx on job_assignments(job_id);

-- ---------------------------------------------------------------- convoys
-- Purpose: social grouping for shared jobs; realtime movement lives in Unity Netcode, not here.
create table convoys (
  id          uuid primary key default gen_random_uuid(),
  leader_id   uuid not null references profiles(id),
  name        text not null check (char_length(name) between 3 and 32),
  session_code text unique,
  created_at  timestamptz not null default now(),
  disbanded_at timestamptz
);
create table convoy_members (
  convoy_id   uuid not null references convoys(id) on delete cascade,
  player_id   uuid not null references profiles(id) on delete cascade,
  joined_at   timestamptz not null default now(),
  primary key (convoy_id, player_id)
);
create unique index one_convoy_per_player on convoy_members(player_id);

-- ---------------------------------------------------------------- RLS
alter table countries          enable row level security;
alter table cities             enable row level security;
alter table locations          enable row level security;
alter table vehicle_definitions enable row level security;
alter table profiles           enable row level security;
alter table player_wallets     enable row level security;
alter table transactions       enable row level security;
alter table vehicle_ownership  enable row level security;
alter table jobs               enable row level security;
alter table job_assignments    enable row level security;
alter table convoys            enable row level security;
alter table convoy_members     enable row level security;

-- Static catalogue: readable by any signed-in player, writable by nobody (service role bypasses RLS).
create policy "read countries" on countries for select to authenticated using (true);
create policy "read cities" on cities for select to authenticated using (true);
create policy "read locations" on locations for select to authenticated using (true);
create policy "read vehicle defs" on vehicle_definitions for select to authenticated using (true);
create policy "read open jobs" on jobs for select to authenticated using (true);

-- Player data: own rows only, read-only. No insert/update/delete policies => denied.
create policy "read profiles" on profiles for select to authenticated using (true);
create policy "own wallet" on player_wallets for select to authenticated using (player_id = auth.uid());
create policy "own transactions" on transactions for select to authenticated using (player_id = auth.uid());
create policy "own vehicles" on vehicle_ownership for select to authenticated using (player_id = auth.uid());
create policy "own assignments" on job_assignments for select to authenticated using (player_id = auth.uid());
create policy "read convoys" on convoys for select to authenticated using (true);
create policy "read convoy members" on convoy_members for select to authenticated using (true);

-- Defence in depth: strip default write grants from client roles.
revoke insert, update, delete, truncate on all tables in schema public from anon, authenticated;
revoke all on all tables in schema public from anon;
