# Status (updated 2026-10-10, after CI run 25)

Legend: **VERIFIED** = executed and passing in CI; **COMPILES** = Unity compiles it in CI (Tundra build success) but it has never run;
**WRITTEN** = code exists, no automated evidence beyond review; **INCOMPLETE** = not built.

| Area | State |
|---|---|
| Supabase schema, RLS, ledger, job state machine, server telemetry, convoy session privacy | **VERIFIED** (Postgres 16 local + 15 in CI): security matrix, 6 concurrency races, 105 mutants -> 101 caught (4 survivors, each a labelled defence-in-depth layer). Includes the bus/passenger system and vehicle shop (server side). `docs/SECURITY.md` |
| Pure-C# logic: session lifecycle/reconnect, vehicle state codec, roster, traffic model (IDM), HUD math, notification queue | **VERIFIED**: ~110 .NET tests green in CI |
| Whole Unity project (gameplay, networking, traffic, HUD, editor tools) | **COMPILES** on Unity 6000.0.58f2 (all packages resolve); run 25 (bus client included): Tundra build success, no CS errors, then the license failure |
| Unity EditMode tests (14) | **NOT RUN** - blocked by license |
| **Unity build -> WebGL artifact** | **NOT DONE. Blocked: `No valid Unity Editor license found` (exit 198)** - see `docs/BUILD.md`. Project is NOT build-ready |
| Driving, jobs flow, garage, auth UI, streaming, weather, audio, camera | COMPILES; never run |
| Multiplayer (Netcode avatars, Relay sessions, convoy UI) | COMPILES; never run; 2-4 player play NOT TESTED |
| Traffic (pooled, IDM) | model VERIFIED; Unity layer COMPILES, never run. No intersections/lights |
| HUD (dial, job card, minimap, ETA, toasts) | math VERIFIED; UI COMPILES, never seen rendered |
| Bus system (server): route, stops, passengers, fares, shop | **VERIFIED** on Postgres. Unity client (route list, run loop, stop serving, bus HUD card, shop screen) is **WRITTEN, compiled by CI once it runs, NEVER RUN**; its pure rules (`BusRules`, `BusRunState`) have xunit tests and its JSON payloads have EditMode tests (blocked by license) |
| Companies, leaderboards, touch UI, Flutter shell | INCOMPLETE - not started |
| Production art (vehicles, roads, buildings, vegetation, signage) | INCOMPLETE - all visuals are primitives |
| Performance measurements | NOT DONE - nothing has run |
