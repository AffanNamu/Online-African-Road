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

### What is still forgeable (honest residual risk)
- A cheater who runs a modified client that sends *legal-looking* positions at legal speeds along any path still earns the reward - but only after spending the real driving time. There is no check that samples follow the road, so a bot cutting straight across terrain is not detected.
- Position samples are not signed; a stolen JWT can submit samples from another machine.
- Damage figure is client-reported within bounds; it can only hurt the player.
- Planned hardening: path-adherence check against route geometry (stored server-side), multiplayer server-authoritative position from Netcode host/dedicated server.

### Caveats (what this does NOT prove)
- Tests run on plain Postgres with a **stub `auth` schema** and role switching, not on real Supabase (GoTrue/PostgREST). RLS and GRANT behaviour is standard Postgres, but PostgREST/JWT plumbing is unverified. Run the same SQL against a Supabase branch before launch.
