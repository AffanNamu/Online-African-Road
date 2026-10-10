# Security: verification record

## Phase 1 result (2026-10-09) — VERIFIED on PostgreSQL 16.15
Environment: throwaway local Postgres 16.15 cluster, `supabase/tests/run.sh`; logs in `docs/test-results/`.

| Suite | Content | Result |
|---|---|---|
| `10_game_rules.sql` | happy path + 18 must-be-rejected checks | PASS |
| `20_security_matrix.sql` | 67 must-be-rejected checks, 26 equality assertions, catalog invariants (RLS on every table, pinned `search_path` on every SECURITY DEFINER fn), ledger-sum invariant | PASS |
| `30_concurrency.sh` | 4 races between real concurrent sessions: single-slot job (1 winner), double delivery (+1000 once), double repair (-200 once), double refuel (-240 once, tank not overfilled) | PASS |
| `mutation.py` | 34 deliberate protection breakages (grants, RLS, each validation, locks, idempotency, caps) | **33 killed**, 1 survived by design (dropping only the wallet CHECK; the ledger CHECK still blocks overdraft — defence in depth). Two vacuous tests and one weak test were found by this run and fixed. |

### Requirement -> evidence
| Requirement | Evidence |
|---|---|
| Clients cannot modify wallets | R1 (update/insert/delete/truncate denied), mutants "wallet UPDATE re-granted", "RLS wallet visible" killed |
| Clients cannot award XP / stats | R1 `update profiles ...` denied; mutant "profiles UPDATE re-granted" killed |
| Cannot complete jobs by editing state | R1 `update/insert job_assignments` denied; mutant killed |
| Rewards cannot be replayed | R6 + race 2 + mutants "completed-state guard removed", "idempotency lookup removed" killed |
| Impossible location rejected | R5 (0,0 / 600 m off), mutant "position radius widened" killed |
| Impossible travel time rejected | R5 `delivery_too_fast`, mutant "time check removed" killed |
| Wrong vehicle state rejected | R5 fuel>tank/negative/damage>100, R5b free-repair; mutants killed |
| Fuel/repair not duplicable | R8 + races 3/4; mutants "row lock removed" killed |
| Convoy abuse | R9: duplicate create/join, full (8), disbanded, hijack/kick denied, leadership transfer |
| RLS behaves as intended | R3 isolation + catalog check; anon denied everywhere (R2) |

## Phase 4 result (2026-10-09) — server-verifiable telemetry, VERIFIED on PostgreSQL 16.15
`start_job`/`complete_job` no longer accept client distance, position, fuel or time. Migration `20261009000003_server_telemetry.sql`:
- `submit_telemetry`: only the **server clock** is used. A sample is accepted if distance moved <= (top speed x 1.3) x elapsed + 15 m. Rejected samples do not advance state; 10 rejections flag the assignment (completion then refused). Samples are rate-limited (0.5 s) and require an in-progress assignment owned by the caller. Function never raises for cheating, so thresholds cannot be probed from error text.
- `start_job(asg, x, z)`: reported position must be within 120 m of the pickup; server stamps the start time.
- `complete_job(asg, damage)`: requires fresh (<60 s) last accepted position within 150 m of the destination, server-verified distance within [0.8x, 5x] of route length, trip time >= distance / (1.25 x top speed), no flag. **Fuel is computed server-side** (verified km x vehicle burn rate). **Damage may only rise**, by at most 3 % per verified km. Profile distance uses verified km.
- Reward, XP, bonus come only from the `jobs` row.

| Suite | Result |
|---|---|
| `20_security_matrix.sql` (all of phase 1 + teleport, speed-hack, flood, wait-then-teleport, stale, wrong place, inconsistent route length both directions, flag-after-10, completed-assignment samples, server fuel burn, damage monotonicity/cap, telemetry table inaccessible, only-intended-RPCs-executable catalog check) | PASS |
| `30_concurrency.sh` (4 races, now through the telemetry path) | PASS |
| `mutation.py` | **50 of 50 protections caught**; 1 survivor by design (wallet CHECK alone). It found and led to one more test (verified-distance ceiling). |

Simulation note: tests move the server clock by back-dating `last_at`/`started_at` (superuser fixtures) and drive real simulated paths through `submit_telemetry`; they do not sleep.

## Convoy sessions (2026-10-09) — VERIFIED
Migration `20261009000004`: the Unity join code column is hidden from clients (column-level grants); only convoy members can fetch it
(`get_convoy_session_code`), only the leader can change it, format-checked, unique. Tests: member/non-member/leaver, leader/non-leader,
bad format, duplicate, column and `select *` denied. Mutation run: 56 of 57 caught (survivor = intended defence in depth).
GitHub CI (Postgres 15) passes the same suite and mutation run.

## Bus system and vehicle shop (2026-10-10) — VERIFIED on PostgreSQL 16 (local) and 15 (GitHub CI run 24: matrix, 6 races, 103-mutant run all green)
Migration `20261009000005`: `buy_vehicle`, `start_bus_run`, `submit_bus_telemetry`, `serve_stop`, `abandon_bus_run`; tables `bus_routes`,
`bus_route_stops`, `bus_runs`, `bus_run_stops` (read-only for clients, own-rows RLS on runs).
Authority model: the client sends only a run id. Passenger counts come from a server-side hash of (run, stop) and the stop's demand,
capped by seat capacity; fares are `alighted x route fare`; a stop is served only if the server-verified position is within 60 m of it,
telemetry is fresh (30 s), the run is not flagged, and at least (inter-stop distance / top speed x 1.25) of server time has passed since the previous stop.
Tests (`supabase/tests/25_bus_and_shop.sql`): shop (unknown, not-for-sale, one coin short vs exact funds, ledger row, full tank, no change on failure),
start rejections (wrong category, not owner, 0,0 and 121 m from stop, out of fuel, destroyed, freight job active, double start, anon),
teleport rejection, rate limit, flag after 10, stale telemetry, other player cannot submit/serve/abandon, replay at same stop,
too-soon, radius edge (31 m ok, 80 m refused), capacity never exceeded (fixture route with demand 80), revenue = fares = wallet delta,
xp once, post-completion rejection, a legitimately driven run (verified km 3.6-3.8, fuel burn = km x 0.28), abandon, direct writes denied, RLS isolation,
ledger invariant. Races 5 and 6: concurrent terminus serve pays once; concurrent purchases with money for one bus buy one.
Mutation run (all files): **99 of 103 caught**; the 4 survivors are labelled defence in depth: wallet CHECK alone, fare idempotency key alone,
bus-run row lock alone, row lock + fare key together (each is backed by another independent layer: wallet CHECK / stop-log primary key / status guard).
Removing the row lock and the stop-log primary key together IS caught by race 5.

### What is still forgeable (honest residual risk)
- A cheater who runs a modified client that sends *legal-looking* positions at legal speeds along any path still earns the reward - but only after spending the real driving time. There is no check that samples follow the road, so a bot cutting straight across terrain is not detected.
- Position samples are not signed; a stolen JWT can submit samples from another machine.
- Damage figure is client-reported within bounds; it can only hurt the player.
- Planned hardening: path-adherence check against route geometry (stored server-side), multiplayer server-authoritative position from Netcode host/dedicated server.

### Caveats (what this does NOT prove)
- Tests run on plain Postgres with a **stub `auth` schema** and role switching, not on real Supabase (GoTrue/PostgREST). RLS and GRANT behaviour is standard Postgres, but PostgREST/JWT plumbing is unverified. Run the same SQL against a Supabase branch before launch.
- Bus fares: a bot that drives the real route at legal speeds earns the fare after spending real time, same as freight. Passenger counts are deterministic per run, so they cannot be farmed by re-serving a stop (PK + status guard), but repeated full runs are bounded only by driving time. Route adherence is not checked.
