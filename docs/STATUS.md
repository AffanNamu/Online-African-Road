# Status (updated 2026-10-10, after live backend smoke run 4)

Legend: **VERIFIED** = executed and passing in CI; **COMPILES** = Unity compiles it in CI (Tundra build success) but it has never run;
**WRITTEN** = code exists, no automated evidence beyond review; **INCOMPLETE** = not built.

| Area | State |
|---|---|
| Supabase schema, RLS, ledger, job state machine, server telemetry, convoy session privacy | **VERIFIED** (Postgres 16 local + 15 in CI): security matrix, 6 concurrency races, 105 mutants -> 101 caught (4 survivors, each a labelled defence-in-depth layer). Includes the bus/passenger system and vehicle shop (server side). `docs/SECURITY.md` |
| **Live Supabase backend** (project echmdzqxlbiszyeawbcd, dedicated) | **VERIFIED live**: schema applied + live RLS/grant check passed; end-to-end smoke 30/30 (real sign-in, Unity's queries, cheat attempts refused, real-time job delivery paid once). Bus-run path not exercised live. `docs/SECURITY.md` |
| Pure-C# logic: session lifecycle/reconnect, vehicle state codec, roster, traffic model (IDM), HUD math, notification queue | **VERIFIED**: ~110 .NET tests green in CI |
| Whole Unity project (gameplay, networking, traffic, HUD, editor tools) | **VERIFIED to compile and build**: Unity 6000.0.58f2 in CI (Personal serial + account login), 25/25 EditMode tests pass, WebGL build succeeds (18.5 MB), artifact `webgl-build` uploaded |
| Unity EditMode tests (25, incl. 12 bus DTO/error-message tests) | **VERIFIED**: 25/25 pass in CI |
| **WebGL build boots** | **VERIFIED** (run 31): headless Chromium (software WebGL2) loads it, no page errors, `GameBootstrap` runs, canvas renders. A screenshot from the owner's own browser shows the Sign-in screen over the dusk sky and terrain |
| Login screen (rendered in a real browser) | **VERIFIED visually** by owner screenshot |
| **UI input (mouse + keyboard) in the WebGL build** | **VERIFIED in CI** (run 57): headless Chromium clicks CREATE ACCOUNT (the validation message appears, 700 px changed) and types into the Email field (491 px changed). Root cause of the earlier dead input: no `activeInputHandler` was set, so the player had no Input System devices; now `Both` (committed `ProjectSettings/ProjectSettings.asset`) and the UI uses the legacy `StandaloneInputModule`. Driving input uses the new Input System and should work again, but has **not been exercised yet** |
| Driving, jobs flow, garage, bus routes, shop, streaming, weather, audio, camera | COMPILES and the app boots to the login screen; **gameplay beyond login never exercised** (needs a configured Supabase backend + a driven session) |
| Multiplayer (Netcode avatars, Relay sessions, convoy UI) | COMPILES; never run; 2-4 player play NOT TESTED |
| Traffic (pooled, IDM) | model VERIFIED; Unity layer COMPILES, never run. No intersections/lights |
| HUD (dial, job card, minimap, ETA, toasts) | math VERIFIED; UI COMPILES, never seen rendered |
| Bus system (server): route, stops, passengers, fares, shop | **VERIFIED** on Postgres. Unity client (route list, run loop, stop serving, bus HUD card, shop screen) compiles and is in the WebGL build, but **has never been driven** (needs a configured backend and a signed-in session); its pure rules (`BusRules`, `BusRunState`) have xunit tests and its JSON payloads have EditMode tests, both passing in CI |
| Companies, leaderboards, touch UI, Flutter shell | INCOMPLETE - not started |
| Production art (vehicles, roads, buildings, vegetation, signage) | INCOMPLETE - all visuals are primitives |
| Performance measurements | NOT DONE - nothing has run |
