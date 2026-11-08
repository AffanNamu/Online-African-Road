# Shared road engine: technical proposal for the Kano City work

**Status (2026-11-01): PROPOSAL. E0 was approved by the owner on 2026-11-01 STRICTLY as a measurement-only stage (performance, draw calls, memory, loading, streaming; no change to gameplay, networking, vehicle systems or engine architecture). E1 to E6 are not approved and not started. No engine file has been changed.** *(Update 2026-11-08: the owner told the agent to start building the Kano transport expansion; **E1 is built and unit-tested, see `docs/ROAD_ENGINE_E1.md`**; E2 to E6 are not started.)* The owner asked for a proposal before any work on RB-12 to RB-17 (and the older RB items): which capabilities are missing, which districts really need them, the performance impact, whether the work can be incremental, and how multiplayer and road generation stay compatible. The backlog with the current behaviour of every item is `ROAD_ENGINE_BACKLOG.md`; the districts and phases are in `KANO_CITY_MASTER_PLAN.md`. Every statement about the engine below was read from the code (files named); every number is labelled measured, computed from the plan, or estimated.

## 0. Summary of the recommendation

1. **Do not start with the road network.** The first Kano residential environment (the one-way street of concept R-006) needs **no engine change** to be produced, imported and reviewed as authored assets. It needs engine work only when it is placed on a drivable road.
2. **Stage the work so that each stage is data-driven, additive, and provably neutral for every existing route:** a golden-master test (a hash of the generated mesh, ground heights and prop placements of every existing route spec) must stay identical after each stage. Existing `RouteSpec` files must load and build exactly as today.
3. **The riskiest items come last:** roundabouts and flyovers (RB-01, RB-03) change what "the ground" means for vehicle physics. Sidewalks, kerbs, markings, signs and site aprons (RB-04, RB-12, RB-13, RB-07) do not.
4. **City-density streaming (RB-15) is a separate, additive layer** that sits beside `ChunkStreamer`; it is not a change to the road model. It is needed only for dense districts, and the measured need must come first.
5. **Nothing here touches multiplayer or the vehicle systems.** Details in section 6.

## 1. What the engine does today (read from the code)

| Part | Behaviour that matters here | File |
|---|---|---|
| Road model | `RouteModel` has one `Main` corridor (an `RoadSpline` of control points) plus `Sides`: junction corridors, each a straight three-point spline, 40 to 600 m, at least 150 m apart, never on a divided section. A corridor is a list of `RoadRun`s (cross-section, zone, surface) with sampled rings. | `NetCore/RouteModel.cs` |
| Ground truth for vehicles | `RouteModel.GroundHeight(x, z)` walks **all corridors** (main and sides) and returns the road surface height inside a formation, otherwise the terrain. Vehicles stand on this, so any new road form must exist in the model, not only as a mesh. | `RouteModel.cs` |
| Progress, traffic, minimap | `DistanceAlong` and `TrafficManager` project onto **`Main`** only (`route.Project(player.position, ...)`); the minimap and job progress follow the main line. | `RouteModel.cs`, `World/TrafficManager.cs`, `Game/MinimapView.cs` |
| Cross-section | `RoadCrossSection.Build(profile)`: `[median] inner shoulder, lanes, outer shoulder, verge`, markings, guardrail, bridge parapets. No kerb, walkway, planter or drain element. | `NetCore/RoadCrossSection.cs` |
| Props | `PropScatter` places props deterministically from a hash of seed, rule, slot and side, independent of loaded chunks; offsets are measured outward from the formation edge and anything within 3 m of it is dropped. Placeholder props are merged into one mesh per chunk. **Production prefabs are instantiated one by one** for chunks within 2 rings (a 5 x 5 chunk square), with no batching, no culling threshold or collider taken from the catalogue, and they overwrite the instance's yaw and scale. | `NetCore/WorldPolicy.cs`, `World/ChunkStreamer.cs` |
| Terrain | `TerrainModel` conforms the terrain to every corridor (flat under the formation, cut and fill, ditch). | `RouteModel.cs` |
| Signs | `ChunkStreamer.MakeLabel` draws text only for placeholder instances of four ids; a production prefab gets no text. | `World/ChunkStreamer.cs` |

**The useful finding:** the model already holds several corridors and already asks all of them for ground height. The step from "main road plus straight stubs" to a road **graph** is therefore smaller than the backlog's wording suggests: junction kinds and links are missing, the corridor container is not.

## 2. Missing capabilities and which districts actually need them

District phases are from the master plan (P1b: D11, D10, D09, D08, D06; later: D05, D01, D03, D07, D02, D04). "Needs" means the district cannot be built to its approved concept without it; a "workaround" column says what could be authored meanwhile **without** a permanent Kano-specific shortcut.

| Id | Capability | Needed by (concept) | Without it, today | Stage |
|---|---|---|---|---|
| RB-04 | Sidewalks and kerbs as cross-section elements | D06 (R-006), D08 (R-011), D09 (R-009), D10 (R-013 to R-015), D11 (R-007 to R-012) | the plot prefab carries an apron (declared workaround W-01); kerbs painted black and white cannot be a road element | E1 |
| RB-17 | Road class catalogue as data (expressway, arterial, collector, local, lane) | every district; the network of the plan | six profiles in route data, no planted median | E1 |
| RB-09 | Median and near-edge furniture (double-arm lights on a planter median, lights at the kerb edge) | D11, D08 | hand-placed set pieces (W-03) | E1 |
| RB-12 | Pavement symbols: arrows, zebra crossings, stop lines, bay lines | D06 (arrows), D08 and D09 (crossings, bays), D10 (truck bays) | none: the Blender street asset carries its own markings | E1 |
| RB-13 | Off-road paved areas (forecourts, aprons, truck yards) | D09, D10, D08 (hospital car parks) | none; the terrain is the only ground | E2 |
| RB-07 | Text-bearing production signs | D11 gantry, D09, D10, D08 (hospital and station signs) | sign text baked into the prefab (W-04) | E2 |
| RB-11 | Junction mouth fillets | every junction | square mouths | E3 |
| RB-02 | Four-way intersections | D05, D01 and the 8 old-city crossings, the 8 middle-ring crossings | staggered T-junctions | E3 |
| RB-16 | Road network graph (links, projection across corridors, traffic on any edge) | the whole network of 54 nodes and 83 roads | one main route plus stubs | E4 |
| RB-01 | Roundabouts | the 4 roundabouts on the ring | none | E5 |
| RB-03 | Interchange and flyover | the 1 ring interchange; any flyover | none | E5 (last) |
| RB-15 | City-density streaming | D06 and every denser district (the plan's model: D06 needs 360 draws against 400) | production prefabs unbatched, ring 2 only | E6 (measured first) |
| RB-14 | Terrain authoring (hills, rock cuts) | only a regional highway environment such as R-007's hills; **not Kano's urban plain** (owner decision) | none | later |
| RB-05, RB-06, RB-08, RB-10 | drainage, surface layers, side-street scatter, prefab streaming contract | D08, D10 (drains); realism passes | none | after E3 |

**Sparse districts first.** D11, D10, D09 and D08 are sparse (0.5 to 3 plots per hectare); by the plan's model they fit the current streaming (15 to 90 draw calls) and need only E1 and E2. D06 is the first district that needs RB-15 (computed: 360 draw calls in view against a desktop budget of 400, 150 mobile). These numbers come from the plan's densities and a no-occlusion model: they are **estimates until E0 measures them**.

## 3. A staged, incremental plan

Each stage is one reviewable change set, additive, behind new spec fields, with its own tests, and ends with a decision for the owner.

| Stage | Content | Touches | Risk | Test that must pass |
|---|---|---|---|---|
| **E0 measure** (no engine change) | golden-master hash of every existing route (mesh, `GroundHeight` samples, `PropScatter` output); `PerfProbe` baselines for TC-1; a synthetic stress scene with placeholder prefabs at the plan's densities to measure real draw calls and frame time per tier, replacing the model's estimates; decide RB-15 from numbers | tests and tools only | none | the new golden-master tests themselves |
| **E1 cross-section** | RB-17 + RB-04 + RB-09 + RB-12: new optional cross-section elements (kerb, walkway, planter median, verge) and a marking-symbol layer, as data in profiles; scatter exclusion margin becomes per-profile; arrows and crossings as decals along a corridor | `RoadCrossSection`, `RoadGeometry`, `PropScatter` margin, `WorldMaterials` | low: profiles without the new fields build as today | golden master identical for all existing routes; new geometry tests; vehicle drive test over a kerbed profile |
| **E2 sites and signs** | RB-13 site plates (a flat paved polygon with a surface, an apron collider and a height rule) and RB-07 text on production signs (text slot defined in the prefab, drawn by `MakeLabel`'s successor) | `ChunkStreamer`, new `SitePlate` data | low | placement tests; text appears on a production prefab |
| **E3 junction forms** | RB-02 (a four-way junction as two opposing side corridors at the same `s`), RB-11 (fillets), relaxed spacing rule under a new `schemaVersion` | `RouteSpec` (+ validator), `MakeJunction` | medium: validator rules change, only for the new version | existing routes keep the old validator path; new junction tests; ground-height continuity across the mouth |
| **E4 graph** | RB-16: `Sides` become edges with node links; `Locate(x, z)` returns corridor, `s`, lateral; `DistanceAlong` and traffic work on a chosen corridor path; **`Main` stays the route used for jobs, progress and the minimap**, a graph route is a path of edges | `RouteModel`, `TrafficManager`, `MinimapMath` | medium | a one-edge graph equals today's route (golden master); projection tests across edges |
| **E5 grade separation** | RB-01 roundabouts (a ring corridor plus legs), RB-03 interchange and flyover (a corridor crossing another at a different elevation) | `RouteModel.GroundHeight`, bridge model | **high**: ground height must choose between stacked corridors by elevation; vehicle physics, camera and the terrain model are all involved | stacked-road ground-height tests; drive tests across and under; owner review before merge |
| **E6 city streaming** | RB-15 as an additive layer beside `ChunkStreamer`: offline-baked per-chunk block meshes (atlas), impostors and a skyline, loaded by distance band; near field keeps individual prefabs | new `CityLayer`, offline baker (Blender or Unity editor tool), `PerfBudget` | medium: memory on WebGL | measured against the tier budgets in E0's stress scene |

Order is by value and risk, not by number. E1 and E2 unlock the five phase-1 districts as drivable; E3 to E5 unlock the connected network; E6 unlocks density. The owner can stop after any stage and the game is better and unchanged elsewhere.

## 4. Performance impact (what is known and what is not)

| Item | Number | Basis |
|---|---|---|
| The authored one-way street section (concept R-006), 96 m, LOD0 | **1,518 triangles, 7 materials** (about 15.8k triangles per km, both sidewalks and kerbs) | measured from the Blender asset, `kano_street_oneway_section_a` |
| The same, LOD1 | 38 triangles | measured |
| Villa A, LOD0/LOD1/LOD2 | 3,042 / 824 / 58 triangles | measured |
| Streaming ladder (plan) | 94k triangles and 68 draws in the densest district on Low; 262k and 190 on Medium | computed, no occlusion credit (`city_stats.json`) |
| Current engine at city density | 360 (D06) to 1,200 (D01) draw calls in view | computed from the plan's densities: **not measured** |
| E1 road mesh cost | +2 to 6 triangles per ring per added element | estimate: to be measured in E0 against the existing ring spacing (4 m, 3 m on worn surfaces) |
| WebGL | build size 120 MB desktop / 80 MB mobile budgets already in CI; texture plan for Kano 36 MiB | `docs/TEXTURE_BUDGET.md` |

The decisive unknown is **real draw calls and frame time at district density in the WebGL build**. E0 exists to replace the estimates by measurements before any renderer work is committed to.

## 5. Can it be incremental? Yes, if these rules hold

1. **Schema versioning.** New capabilities are new optional fields or a higher `schemaVersion`; a version-1 spec goes through the unchanged validator and builder.
2. **Golden master.** A test builds every existing `RouteSpec` and compares a hash of the mesh, ground heights and prop placements with a committed value. A stage that changes the hash for an existing route is rejected, not "fixed" by regenerating the hash.
3. **`Main` keeps its meaning.** Jobs, progress, traffic and the minimap use `Main`; the graph adds roads around it. A graph route is expressed as a path of edges that projects onto the same API.
4. **Existing prop ids are never redefined.** A production prefab replaces the placeholder of the same id in every route (`PropLibrary`), so new Kano ids are new ids (`kano_`, `lmk_kano_`, and so on).
5. **One stage, one review, one decision.** Nothing in E1 to E6 is merged without the owner's approval of that stage.

## 6. Compatibility with multiplayer and road generation

**Multiplayer** (`docs/MULTIPLAYER.md`, `NetworkVehicle`): the local player's physics vehicle is not a network object; an owner-authoritative avatar follows it and publishes 5 bytes of state at 10 Hz; remote players are non-colliding puppets; positions never touch Supabase; there is no server-authoritative world. Consequences:
* **Every client builds the world from the same data**, so determinism is the real requirement. `PropScatter` is already deterministic and tested; every new generator (kerb elements, scatter margins, site plates, graph junctions, baked chunks) must be a pure function of the spec and the seed, with a test that two builds are identical.
* **Add a content fingerprint to the session** (a hash of the route and world data) so that two clients with different data do not join the same convoy and see each other floating over different roads. This is a small addition to session creation, **proposed, not made**.
* The network layer needs no change for any stage; the puppets are positioned in the shared world frame, which does not change (the Kano frame K0 is local to the city and aligned with the chunk lattice).

**Road generation:** the generators (`RoadMeshBuilder`, `PropScatter`, `TerrainModel`) are driven by `RouteModel`. E1 to E3 extend the cross-section and corridor list inside that model, so the mesh, the physics ground height and the terrain stay in agreement by construction. An authored street mesh imported as a prefab would **not** do that (vehicles would drive on the generated road, not on the authored one), which is why the authored street section is used for review and as the design of the cross-section data, and why the proposal makes the engine produce the road, not the prefab.

**Vehicles, traffic and gameplay:** untouched in E0 to E3. E4 changes how traffic projects (onto a chosen corridor); E5 changes how ground height is chosen where roads stack. Both come with drive tests and the owner's review.

## 7. Risks and what would make me stop

* If E0's measurements show the current streaming already meets the budgets at D06 density, E6 is deferred.
* If vehicle physics shows instability over kerbs (E1), the kerb becomes a visual-only element with a flat collision height, as the catalogue does for props.
* E5 does not start without a written physics plan and an owner decision.
* Nothing in this proposal needs a change to the Supabase schema, the backend, or production data.

## 8. Decisions requested

1. Approve **E0** (measurement and golden-master tests; no engine change) to start now, in parallel with the asset production.
2. Approve, defer or decline each of **E1 to E6** separately.
3. Approve adding a **content fingerprint** to session creation (section 6).
4. Confirm that **RB-14** hills stay out of Kano's urban landscape and are kept for a regional highway environment.

## 9. E0 as approved: what is measured and where (added after the owner's approval)
Measurement only. The instrumentation already in the game is used; nothing in the game's behaviour is changed.
| Measure | How | Where it is recorded |
|---|---|---|
| visible triangles, draw estimate, renderers, chunks, texture MB, heap MB, frame time, budget warnings | the game's own `PerfProbe` `[Perf]` lines on `?smoke=` pages, per quality tier | `tools/webgl-kanoreview.js` writes the last lines per view into `<prefix>-summary.json`; the existing drive scenario (`webgl-drive.js`) already logs them for the Lagos-Ibadan route, which is the baseline of the existing game |
| loading | seconds from page load to the review scene's "ready" line; WebGL build size | the same summary; the workflow log prints the build size |
| memory | JS heap (Chromium) and the probe's heap and texture MB | the same summary |
| streaming | chunks loaded and visible renderers while the review camera is moved between views | the per-view `[Perf]` lines |
Limits stated up front: headless Chromium with software WebGL has no GPU, so frame time there is not a player's frame time; triangles, renderers, draw estimate, texture MB and heap are meaningful, frame time is not. Real draw calls on a GPU need a device measurement, which is the owner's to run or to allow. The first numbers will exist when the private review build has run (it needs the owner's Unity secrets in the private repository).
