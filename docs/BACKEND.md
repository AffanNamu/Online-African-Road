# Backend foundation

African Roads Online talks to Supabase (Postgres + PostgREST + GoTrue). **Every rule that matters lives in the database**: clients hold the public anon key and a player JWT, nothing else. This document is the map; the code is `supabase/migrations/*.sql`.

## Authority model

* The player is **always `auth.uid()`**. No function takes a player id to act as (a test fails if one ever does). Ids that appear as parameters are *targets* (a friend to ask, a vehicle to rename), and every one is checked against ownership or a relationship inside the function.
* Clients **cannot write any table** and cannot read private columns. All writes are `SECURITY DEFINER` functions with a pinned `search_path`; reads are RPCs shaped for the screen (plus a few public columns / own rows through RLS).
* **Money is a ledger.** `player_wallets.balance` only changes inside `_apply_transaction`, which inserts an immutable `transactions` row with a unique `idempotency_key` and a `reason`. Rewards, bonuses, refunds, adjustments and purchases are all rows; there is no `coins = X` anywhere.
* **The client never decides a reward, XP, distance or completion.** `complete_job` takes the assignment id and a bounded damage figure; everything else comes from server-verified telemetry (server clock, speed and position checks).
* Rules are enforced where rows are written (triggers on `job_assignments`, `vehicle_ownership`, `convoy_members`, `transactions`, `profiles`), so every path obeys them, including future code.
* `supabase/api_surface.txt` lists every function a client may execute. The security matrix, `verify_live.sql` and the structural test in `38_content_boards_security.sql` fail if the database exposes anything else, so adding an endpoint is a reviewed change.

## Migrations (forward-only, additive; applied once each by `supabase/apply.sh`)

| File | Adds |
|---|---|
| `20261010000001_ledger_kinds` | ledger kinds `ADJUSTMENT`, `REFUND`, `CHALLENGE_REWARD` |
| `…02_foundation` | `game_config`, per-player rate limiting (`_rate_limit`), `game_sessions`, `analytics_events` (allow-listed names, PII-free), `notifications` + RPCs |
| `…03_profile_presence` | profile username / avatar / country / `last_seen_at` / `lifetime_earnings`, public-vs-private column grants, sessions + heartbeat + online count (server-side expiry) |
| `…04_world_state` | `cities` slug/status/unlock, `world_routes`, `get_world_state` (OPEN / PLANNED / LOCKED, effective per player level) |
| `…05_social` | friendships, recent players, convoy capacity / privacy / invites / kick / leader transfer, membership rules as triggers |
| `…06_vehicles_jobs` | vehicle API (status, mileage, customisation, selection), job board API (ETA, requirements, eligibility reason), job requirement triggers (level, route unlock), analytics + notifications on accept / abandon |
| `…07_progression_content_economy` | ledger reason + earnings trigger + payout notification, wallet API, admin tools, XP maths, achievements (silent backfill for existing players), daily-challenge foundation, content (news + hero), leaderboards, `get_dashboard_summary` |

| `20261012000007_nearby_opt_in` | Players Nearby is opt-in: `profiles.appear_nearby` default `false`, existing accounts set to `false`, `set_nearby_visibility(null)` = hidden. Touches no other column (country, name, level, wallets, map data are untouched) |

Existing tables and APIs are untouched; `complete_job`, `accept_job`, telemetry and the ledger function keep their definitions. `supabase/tests/40_upgrade.sh` upgrades a database that already holds players, money, a delivered job and a convoy and proves the legacy data is byte-identical afterwards.

## Endpoints (RPC, `POST /rest/v1/rpc/<name>`)

* **Profile / progression**: `get_my_profile`, `update_my_profile` (display name, username, avatar, country only), `get_public_profile`, `find_player` (exact handle), `get_my_progression`, `list_my_achievements`, `get_dashboard_summary`
* **Presence**: `start_session`, `heartbeat`, `end_session`, `get_online_count`, `get_my_presence`, `get_presence` (self / friends / convoy mates / recent players only)
* **Friends**: `send_friend_request`, `respond_friend_request`, `remove_friend`, `get_friend_status`, `list_friends`, `list_friend_requests`, `list_recent_players`
* **Convoys**: `create_convoy`, `join_convoy`, `leave_convoy`, `get_my_convoy`, `list_open_convoys`, `set_convoy_options`, `invite_to_convoy`, `list_convoy_invites`, `respond_convoy_invite`, `kick_convoy_member`, `transfer_convoy_leader`, `get_convoy_session_code` / `set_convoy_session_code`; lobby (`20261011000002`): `set_convoy_route`, `set_convoy_tags`, `set_my_ready`, `start_convoy_drive`, `end_convoy_drive`, `post_convoy_message`, `list_convoy_messages`; discovery: `list_active_players`, `get_server_status`; job board (`20261011000001`): `list_available_jobs` takes `p_category` / `p_search`
* **Notifications**: `list_notifications`, `get_unread_notification_count`, `mark_notification_read`, `mark_all_notifications_read`
* **Vehicles / shop**: `list_my_vehicles`, `list_vehicle_catalog`, `set_active_vehicle`, `rename_vehicle`, `set_vehicle_customization`, `buy_vehicle`, `buy_fuel`, `repair_vehicle`
* **Jobs**: `list_available_jobs`, `get_job_details`, `get_my_active_job`, `accept_job`, `start_job`, `submit_telemetry`, `complete_job`, `abandon_job`; bus: `start_bus_run`, `submit_bus_telemetry`, `serve_stop`, `abandon_bus_run`
* **World / content / ranking**: `get_world_state`, `get_content('news'|'hero')`, `get_leaderboard(metric, period)` (xp, distance, jobs, earnings; all-time and weekly), `get_my_daily_challenges`, `claim_challenge_reward`
* **Wallet / analytics**: `get_my_wallet`, `list_my_transactions`, `track_event` (only multiplayer session facts; keys allow-listed)
* **Operator only (not executable by clients)**: `admin_adjust_wallet`, `admin_refund_transaction`, `admin_broadcast`, `admin_set_route`, `generate_jobs`, `sweep_stale_sessions`, `purge_old_data`
  * `generate_jobs` is scheduled: `.github/workflows/supabase-jobs.yml` runs `supabase/top_up_jobs.sh` every 15 minutes against the live database (secret `SUPABASE_DB_URL`). Jobs expire one day after creation. The script makes 20 when fewer than 10 are open, otherwise 4 at a time until 40 are open, so jobs also expire gradually; it changes no schema and no player data. Switch it off by disabling the workflow in the Actions tab.

## Presence, sessions and multiplayer

`start_session` returns a **server-generated session id**; `heartbeat` (every ~30 s) refreshes it with server timestamps; a session with no heartbeat for `presence_ttl_seconds` (90) is offline *by definition* (queries filter on it, `sweep_stale_sessions` tidies). The online count is distinct players with a live session. Job assignments record the live session id. Convoy membership, leader, capacity and the Relay join code are server state; realtime movement stays in Unity Netcode/Relay. Nothing in the dashboard assumes a transport.

## Rate limiting and replay

`_rate_limit(bucket, max, window)` is a per-player fixed window; every new endpoint family has a bucket. Replays are defeated by state machines (an assignment completes once), ledger idempotency keys, row locks and unique indexes, and are covered by the concurrency suite (races 1–8).

## Tests (all run in CI; none are decorative)

* `supabase/tests/run.sh`: security matrix, bus + shop, **35–38 domain suites** (profile authorization, presence expiry, rate limit, friend authorization, convoy membership, notification ownership, vehicle ownership, job transitions, duplicate completion, wallet idempotency, XP maths, route unlock, leaderboard authorization, structural API-security checks), **8 concurrency races**
* `supabase/tests/mutation.py`: **214 mutants** (each breaks one protection; the suite must fail). Documented survivors are defence-in-depth duplicates
* `supabase/tests/40_upgrade.sh`: migration of a populated legacy database via the real `apply.sh`, re-run safe
* `tools/check_api_contract.py`: every C# DTO versus the real output of the real RPCs
* `tools/e2e/api_e2e.js`: HTTP-level tests through real PostgREST and real JWTs (forged / expired / missing tokens, mass assignment, IDOR, rate limit, full job lifecycle)
* `tools/webgl-e2e.js`: the real WebGL build against that backend, every dashboard value compared with the database
* `tests/NetCore.Tests` (retry policy, failure classification, JSON bodies, heartbeat clock, loopback-only overrides, route-state fail-closed) and Unity EditMode `BackendClientTests` (real server replies parsed by the real `JsonUtility`)

## Operating it

`DATABASE_URL=… supabase/apply.sh` applies pending migrations and the idempotent seeds (`seed.sql`, `seed_world.sql`, `seed_content.sql`; the last two are *generated* from the authored JSON by `tools/gen_game_seed.py`, CI checks they are current). Never run it against a project that is not dedicated to this game (it refuses). Schedule `select sweep_stale_sessions(); select purge_old_data();` (e.g. pg_cron, every few minutes / daily).

## Known gaps (honest list)

* No Supabase-side scheduler is configured by the migrations (sweeping is lazy and correct without it; purge needs a cron).
* Leaderboards are index-backed queries, not materialised views: fine to tens of thousands of players; weekly boards aggregate per request.
* No blocking / reporting of players, no friend search beyond the exact handle, no push notifications (the bell polls every 30 s).
* The daily-challenge schema, assignment, progress and claim exist and are tested, but there are no challenge definitions and no UI (the dashboard says "coming soon").
* Avatars are ids only (no art yet); the dashboard shows initials.
* Friends and leaderboards have backends but no Unity screens yet (the sidebar still says "coming soon").
