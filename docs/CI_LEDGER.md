# CI ledger

Cancelled jobs are recorded separately from test failures (`tools/ci_ledger.py`, classification rules in its docstring). **A `cancelled (licence queue)` job is not a failure of anything: it was waiting for the single Unity licence and a newer pending job replaced it; re-run it when the licence is free.**

Totals over these runs: cancelled (licence queue) 4, neutral (reporting job) 3, passed 56, skipped 6

| Run | Workflow | Branch | Commit | Event | passed | failed (test) | failed (infrastructure) | cancelled (licence queue) | cancelled (other) | skipped | not finished |
|---|---|---|---|---|---|---|---|---|---|---|---|
| 42459672682 | Supabase job board top-up | claude/intelligent-faraday-sykb0x | `d83e5fc` | schedule | 1 | 0 | 0 | 0 | 0 | 0 | 0 |
| 42446709983 | Blender import into Unity | work/kano-residential | `021401c` | workflow_dispatch | 1 | 0 | 0 | 0 | 0 | 0 | 0 |
| 42433899264 | Supabase job board top-up | claude/intelligent-faraday-sykb0x | `d83e5fc` | schedule | 1 | 0 | 0 | 0 | 0 | 0 | 0 |
| 42429096946 | Blender import into Unity | work/kano-residential | `7c0afc6` | workflow_dispatch | 1 | 0 | 0 | 0 | 0 | 0 | 0 |
| 42424791833 | Blender import into Unity | work/kano-residential | `7c0afc6` | push | 1 | 0 | 0 | 0 | 0 | 0 | 0 |
| 42424791672 | CI | work/kano-residential | `7c0afc6` | push | 8 | 0 | 0 | 0 | 0 | 0 | 0 |
| 42421805236 | CI | work/kano-residential | `c4a1fec` | push | 8 | 0 | 0 | 0 | 0 | 0 | 0 |
| 42421805226 | Blender import into Unity | work/kano-residential | `c4a1fec` | push | 0 | 0 | 0 | 1 | 0 | 0 | 0 |
| 42421467379 | CI | work/kano-residential | `84da998` | push | 5 | 0 | 0 | 1 | 0 | 2 | 0 |
| 42421464993 | CI | main | `84da998` | push | 5 | 0 | 0 | 1 | 0 | 2 | 0 |
| 42420533475 | CI | work/kano-residential | `a049e1a` | push | 5 | 0 | 0 | 1 | 0 | 2 | 0 |
| 42420484672 | CI | main | `a049e1a` | push | 8 | 0 | 0 | 0 | 0 | 0 | 0 |
| 42418647749 | Supabase job board top-up | claude/intelligent-faraday-sykb0x | `d83e5fc` | schedule | 1 | 0 | 0 | 0 | 0 | 0 | 0 |
| 42400466903 | Supabase job board top-up | claude/intelligent-faraday-sykb0x | `d83e5fc` | schedule | 1 | 0 | 0 | 0 | 0 | 0 | 0 |
| 42377230010 | Supabase job board top-up | claude/intelligent-faraday-sykb0x | `d83e5fc` | schedule | 1 | 0 | 0 | 0 | 0 | 0 | 0 |
| 42347481706 | Supabase job board top-up | claude/intelligent-faraday-sykb0x | `d83e5fc` | schedule | 1 | 0 | 0 | 0 | 0 | 0 | 0 |
| 42318662852 | Supabase job board top-up | claude/intelligent-faraday-sykb0x | `d83e5fc` | schedule | 1 | 0 | 0 | 0 | 0 | 0 | 0 |
| 42291734355 | Supabase job board top-up | claude/intelligent-faraday-sykb0x | `d83e5fc` | schedule | 1 | 0 | 0 | 0 | 0 | 0 | 0 |
| 42238553068 | Supabase job board top-up | claude/intelligent-faraday-sykb0x | `d83e5fc` | schedule | 1 | 0 | 0 | 0 | 0 | 0 | 0 |
| 42178170380 | Supabase job board top-up | claude/intelligent-faraday-sykb0x | `d83e5fc` | schedule | 1 | 0 | 0 | 0 | 0 | 0 | 0 |
| 42134803513 | Supabase job board top-up | claude/intelligent-faraday-sykb0x | `d83e5fc` | schedule | 1 | 0 | 0 | 0 | 0 | 0 | 0 |
| 42090362417 | Supabase job board top-up | claude/intelligent-faraday-sykb0x | `d83e5fc` | schedule | 1 | 0 | 0 | 0 | 0 | 0 | 0 |
| 42020628467 | Supabase job board top-up | claude/intelligent-faraday-sykb0x | `d83e5fc` | schedule | 1 | 0 | 0 | 0 | 0 | 0 | 0 |
| 41963319356 | Supabase job board top-up | claude/intelligent-faraday-sykb0x | `d83e5fc` | schedule | 1 | 0 | 0 | 0 | 0 | 0 | 0 |
