# Development setup

## Backend
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
