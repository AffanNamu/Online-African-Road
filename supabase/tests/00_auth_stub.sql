-- Minimal stand-in for Supabase's auth schema so migrations run on plain Postgres (CI / local).
do $$ begin
  if not exists (select 1 from pg_roles where rolname = 'anon') then create role anon nologin; end if;
  if not exists (select 1 from pg_roles where rolname = 'authenticated') then create role authenticated nologin; end if;
end $$;
create schema if not exists auth;
create table if not exists auth.users (id uuid primary key default gen_random_uuid(), email text, raw_user_meta_data jsonb default '{}'::jsonb);
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
