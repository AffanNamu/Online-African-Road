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

### Caveats (what this does NOT prove)
- Tests run on plain Postgres with a **stub `auth` schema** and role switching, not on real Supabase (GoTrue/PostgREST). RLS and GRANT behaviour is standard Postgres, but PostgREST/JWT plumbing is unverified. Run the same SQL against a Supabase branch before launch.
- Client-reported distance/position are still trusted within plausibility bounds. Phase 4 (server telemetry) addresses this; see below once verified.
