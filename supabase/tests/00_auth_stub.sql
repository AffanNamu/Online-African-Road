-- Minimal stand-in for Supabase's auth schema so migrations run on plain Postgres (CI / local).
do $$ begin
  if not exists (select 1 from pg_roles where rolname = 'anon') then create role anon nologin; end if;
  if not exists (select 1 from pg_roles where rolname = 'authenticated') then create role authenticated nologin; end if;
  if not exists (select 1 from pg_roles where rolname = 'authenticator') then create role authenticator nologin; end if;     -- the role PostgREST connects as on Supabase; the account-status hook is configured on it
  alter role authenticator reset pgrst.db_pre_request;      -- role settings are cluster-wide: start every test database from a clean slate so the migration must set it itself
end $$;
create schema if not exists auth;
create table if not exists auth.users (id uuid primary key default gen_random_uuid(), email text, raw_user_meta_data jsonb default '{}'::jsonb, banned_until timestamptz);
-- Same lookup order as Supabase's own auth.uid(): the per-claim setting (used by the SQL tests) or the JSON claims PostgREST sets per request.
create or replace function auth.uid() returns uuid language sql stable as
  $$ select coalesce(nullif(current_setting('request.jwt.claim.sub', true), ''),
                     (nullif(current_setting('request.jwt.claims', true), '')::jsonb ->> 'sub'))::uuid $$;
grant usage on schema public, auth to anon, authenticated;

-- Reproduce Supabase's default privileges: the platform grants these on everything created in `public`,
-- which is exactly why our migrations must REVOKE the write grants (and function EXECUTE) explicitly.
alter default privileges in schema public grant all on tables to anon, authenticated;
alter default privileges in schema public grant all on sequences to anon, authenticated;
alter default privileges in schema public grant execute on functions to anon, authenticated;

-- Minimal stand-in for Supabase Storage (buckets, objects, the row-level security the real service applies to storage.objects). The migration creates the advertising buckets and policies when this schema exists.
create schema if not exists storage;
create table if not exists storage.buckets (id text primary key, name text not null, public boolean not null default false, file_size_limit bigint, allowed_mime_types text[]);
create table if not exists storage.objects (id uuid primary key default gen_random_uuid(), bucket_id text references storage.buckets(id), name text, owner uuid, metadata jsonb default '{}'::jsonb, created_at timestamptz default now(), unique (bucket_id, name));
alter table storage.objects enable row level security;
grant usage on schema storage to anon, authenticated;
grant select, insert, update, delete on storage.objects to anon, authenticated;
grant select on storage.buckets to anon, authenticated;
