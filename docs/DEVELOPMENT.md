# Development setup

## Backend (live Supabase, all through GitHub - nothing to install)
Use a **dedicated** Supabase project for this game (the schema adds `profiles`, `jobs`, ... in `public` and a trigger on `auth.users`, so it must
not share a project with another app). In the GitHub repo set:
1. Secret `SUPABASE_DB_URL` (Project Settings > Database > Connection string > URI, password filled in). Never paste it in chat or commit it.
2. Actions > **Supabase apply schema** > Run workflow (type `dedicated-project`): applies the migrations once each, seeds, generates jobs, and verifies RLS/grants on the live DB.
   It refuses to run if our table names already exist and were not created by it.
3. Supabase > Authentication > Providers > Email: turn **Confirm email** OFF (the game signs players in immediately).
4. Variable `SUPABASE_URL` and secret `SUPABASE_ANON_KEY` (the public anon key only; never the service-role key).
5. Actions > **Backend smoke test**: signs up a throwaway player, tries to cheat (must fail), and delivers a real job in real time (~3 min).
6. Push (or re-run CI): the WebGL build is then baked with the backend (`-supabaseUrl/-supabaseAnonKey` from the variable/secret above).

## Backend (manual alternative)
1. Create a Supabase project. Run `supabase/migrations/*.sql` in order, then `supabase/seed.sql`.
2. Run job generation with the service role (never from the client): `select generate_jobs(20);` — schedule it (pg_cron).
3. Tests: `PGHOST=localhost PGUSER=postgres PGPASSWORD=... supabase/tests/run.sh` (needs Postgres; also runs in CI).

## Unity
1. Install Unity **6000.0.x LTS** (pinned in `ProjectSettings/ProjectVersion.txt`; verify the exact patch and the package
   versions in `Packages/manifest.json` resolve on first open - they were written without an Editor).
2. Open the project, create a URP asset and assign it (Project Settings > Graphics).
3. Create `Assets/_Project/Resources/BackendConfig` (Create > African Roads > Backend Config): Supabase URL + **anon** key only.
4. Menu **African Roads > Build Bootstrap Scene**, open `Scenes/Bootstrap`, press Play.
5. Keys: W/S throttle-brake(reverse), A/D steer, Space handbrake, L lights, H horn, C camera, Esc menu, mouse wheel zoom,
   F2 rain / F3 +2h (editor and dev builds only).
Without a BackendConfig the game shows the login screen with a notice; jobs/persistence are disabled (no fake backend).
