# Status (updated 2026-10-09)

Legend: IMPLEMENTED = code written; NOT TESTED = never compiled or run (no Unity Editor in the authoring environment).

| Area | State |
|---|---|
| Supabase schema, RLS, ledger, job state machine, server telemetry, convoy session privacy | **VERIFIED** (Postgres 16 local + Postgres 15 CI): security matrix, 4 concurrency races, 57 mutants -> 56 caught, 1 survivor by design. See SECURITY.md, `docs/test-results/` |
| Network core logic (lifecycle, reconnect, codec, roster) | **VERIFIED**: 49 .NET tests green in CI |
| CI pipeline for SQL + .NET | **VERIFIED** (runs green on GitHub) |
| Unity client (auth, jobs, telemetry streaming, garage, HUD, camera, weather, audio, streaming) | WRITTEN, **NOT COMPILED** |
| Unity networking (Netcode avatars, Relay sessions, convoy UI) | WRITTEN, **NOT COMPILED, NOT RUN** |
| **Unity build -> artifact** | **NOT DONE. Blocked on a Unity license secret** (docs/BUILD.md). Project is NOT build-ready. |
| Two-player / four-player play, reconnect in a real session | NOT TESTED |
| Traffic | INCOMPLETE - not started |
| Minimap / full navigation | INCOMPLETE (distance + arrow only) |
| Premium UI/HUD to the visual standard | INCOMPLETE (basic dark uGUI) |
| Production art (vehicles, roads, buildings, vegetation, signage) | INCOMPLETE - all visuals are primitives |
| Bus gameplay, companies, leaderboards, touch UI, Flutter shell | INCOMPLETE - not started |
| Performance measurements (FPS, memory, load, bandwidth) | NOT DONE - no numbers exist |
