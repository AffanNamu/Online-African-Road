# Status (updated 2026-10-09)

Legend: IMPLEMENTED = code written; NOT TESTED = never compiled or run (no Unity Editor in the authoring environment).

| Area | State |
|---|---|
| Repo, LFS rules, CI workflow | IMPLEMENTED, CI NOT RUN |
| Supabase schema, RLS, ledger, job state machine, delivery validation, convoy RPCs | IMPLEMENTED, NOT TESTED (tests written: `supabase/tests`) |
| Unity client: auth, job board, accept/load/deliver loop, garage refuel/repair, profile | IMPLEMENTED, NOT COMPILED |
| Vehicle controller (gearbox, fuel, damage, rain grip), VehicleRig contract, lights | IMPLEMENTED, NOT COMPILED |
| Chunk streaming + procedural road/roadside | IMPLEMENTED (placeholder geometry), NOT COMPILED |
| Time of day (live WAT), rain, wet roads, camera, synth engine audio | IMPLEMENTED, NOT COMPILED |
| Multiplayer / convoy networking (Netcode) | **INCOMPLETE - not started** (only DB convoy tables/RPCs exist) |
| Traffic | **INCOMPLETE - not started** |
| Navigation map/minimap | INCOMPLETE (HUD distance + direction arrow only) |
| Bus gameplay, companies, leaderboards, touch UI, Flutter shell | INCOMPLETE - not started |
| Production art (truck/bus models, PBR road/terrain, buildings, props, signage, pedestrians) | **INCOMPLETE - all visuals are primitives** |
| Windscreen rain, interior, per-gear audio loops, occlusion, baked lighting, LOD | INCOMPLETE |
| Web build / performance measurements | NOT DONE - no numbers exist yet |
