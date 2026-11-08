# Kano City transportation infrastructure and urban development expansion: inspection, plan and phase breakdown

Written 2026-11-08 in answer to the owner's brief "ARO: Kano City transportation infrastructure and urban development expansion" (seven roundabout designs, urban highways, flyovers, logistics
facilities, bus terminals, supporting infrastructure, day and night, shared library). **This is the inspection and the plan the brief asks for first. Nothing in it is built.** No engine file was
changed for it, nothing was added to the asset library, and nothing is marked complete. Where the brief needs road-engine work, section 5 is the technical proposal that the project's rules require
before it starts; it needs the owner's decision (section 9).

It extends, and does not replace, `docs/KANO_CITY_MASTER_PLAN.md` (KSA-1: districts, road network graph, phases), `docs/ROAD_ENGINE_BACKLOG.md` (RB-01 to RB-25) and
`docs/ROAD_ENGINE_PROPOSAL.md` (stages E0 to E6). Section 1 shows where the brief lands in them. The night and street-lighting milestone requested the same day
(`docs/NIGHT_LIGHTING_PLAN.md`) is the foundation of the brief's "day and night" requirement; its status is in section 7.

New references supplied with the brief are registered as **R-032 to R-036** (hash only, nothing stored; flags: real brand names in the images must not be reproduced, sign text is illustrative, the
monument and the interchange layout are designs, not real places or engineering geometry): urban expressway at night, rural highway at night, landmark roundabout, flyover and urban highway, aerial interchange.

> **Update 2026-11-08 (later the same day): the owner said "start building them now, don't stop until finish all".** Stages **E1, E2, E3 and E4 are built**: `docs/ROAD_ENGINE_E4.md` (a spatial index for point queries, wired in for routes with 8 or more corridors with identical answers; a road-graph model with `Locate` and shortest paths, NOT wired into traffic, progress or the minimap), `docs/ROAD_ENGINE_E3.md` (schemaVersion 2: four-way crossings, 60 m junction spacing, junctions on a divided road with a median opening, flared corners), `docs/ROAD_ENGINE_E1.md` (kerbs, walkways, gutters, planter and painted medians, dropped kerbs, six road classes, pavement symbols) and `docs/ROAD_ENGINE_E2.md` (site plates, pavement marks on plates, props on plates and side roads, text faces, prefab colliders; a logistics yard and a bus terminal built from them on the test corridor with the game's grey placeholder props). 1,308 unit tests pass, the golden master of the shipped routes is unchanged, in-game verification is wired into CI and not yet read. E5 (roundabouts and flyovers) has a written technical and physics plan, `docs/ROAD_ENGINE_E5_PLAN.md`, and is NOT built: it waits for the owner's approval (section 8 of that file). E6 onwards, the asset work and every other row below are **not started**; the "None is approved or started" statements in this document describe the state when it was written.

## 0. Answers in one page

| Question | Answer |
|---|---|
| Can the current game build roundabouts, four-way junctions, flyovers or interchanges? | **No.** The road model is one main spline plus straight T-junction stubs (>= 150 m apart, undivided road only). Verified in code (section 2). The master plan already records this: 21 of the 54 junctions of the Kano network cannot be built. |
| Can a roundabout or flyover be made by importing a Blender mesh and placing it on the road? | **Not as usable infrastructure.** Vehicles would drive on the generated road and terrain, not on the authored mesh; ground height, terrain, props, traffic and the minimap all come from the route model. The brief forbids "decorative circles on top of roads", and the engine proposal reached the same conclusion on 2026-11-01. |
| What is needed? | Engine stages E1 to E5 of the existing proposal (cross-section elements, sites and signs, junction forms, a road graph, roundabouts and grade separation), each additive, versioned and golden-master tested. Section 5 gives the concrete design for the seven roundabouts and the flyovers. **None is approved or started.** |
| What can start now without engine changes? | Asset production in test scenes for everything that does not depend on the network: street furniture, signs, fences and walls, warehouse and terminal components, bus shelters, ticket offices, vegetation, pier and deck components. And the lighting foundation (in progress). |
| Which phase gives the most gameplay soonest? | By dependency, **logistics sites and bus terminals (brief phases 3 and 4) need only E1 and E2** (site plates, text signs, kerbs), not new junction types: they hang off the existing T-junction stubs. Roundabouts, flyovers and the city network (phases 1 and 2) need E3 to E5, the riskiest stages. I recommend the execution order in section 6 and ask the owner to confirm it (the brief numbers the phases 1 to 6; the order of work is a decision). |
| What blocks the gameplay of the sites? | Kano is a PLANNED city with no drivable route and no jobs or bus stops in the database; jobs and stops are server-owned data (`supabase/seed_world.sql`). Kano jobs, bus routes and a world-map status change are **production data changes that need separate approval**. |

## 1. Where the brief lands in the existing plan

| Brief section | Existing item | State |
|---|---|---|
| 2. Seven roundabouts | RB-01 (roundabouts), RB-16 (graph), network nodes: 4 roundabouts and 1 interchange in the plan | engine: not supported. References: landmark, multi-lane, green and interchange have usable references (R-034 to R-036, R-004, R-005, R-006); modern urban and commercial need commercial frontage references (M-02, M-04); the traditional design rests on documentary sources only (R-024 to R-029). The plan has 4 roundabout nodes; the brief needs 7 designs: mapped in 5.1 (two crossings become roundabouts, the interchange node carries the seventh design; the node count is unchanged). |
| 3. Urban highways | RB-04, RB-09, RB-12, RB-17 (kerbs, median furniture, symbols, road classes), D11 (A2 gateway), expressway 3.4 km and ring 20.6 km in the network | partly supported: lanes, markings, jersey-barrier median, guardrail, bridges over valleys. Not supported: planted medians, kerbs, sidewalks, drainage elements, painted barrier blocks as a profile element. |
| 4. Flyovers | RB-03 (flyovers and interchanges), E5 | not supported (bridges only span terrain valleys). |
| 5. Logistics | D10 industrial and logistics zone (R-013 to R-015), RB-13 (paved areas), RB-07 (signs) | sparse district; needs E1 and E2. The concepts exist; asset breakdown not yet approved. |
| 6. Bus terminals | D09 transport hub (R-009), existing bus routes system (`BusSession`, `BusRules`), RB-13, RB-07 | needs E1 and E2; gameplay needs server data (section 4). |
| 7. Supporting infrastructure | D05 commercial belt, D08 institutional (hospital R-010, police R-011), fuel and service (R-013 to R-015) | schools, government, hotels, shopping centres have no concept yet (M-02, M-04, M-10). |
| 8. Day and night | the night and street-lighting milestone | foundation built, runtime in progress (section 7). |
| 9. Shared library | `docs/AFRICAN_ENVIRONMENT_LIBRARY_ARCHITECTURE.md`, ARO-CONTENT registry (GLOBAL, REGIONAL, CITY_SPECIFIC, LANDMARK) | in place; every new id below follows its prefix rules (`aro_`, `rg_ngn_`, `kano_`, `lmk_kano_`). |

## 2. What was inspected (read from the code, 2026-11-08)

| Subject | Finding | Where |
|---|---|---|
| Road model | `RouteModel` = `Main` corridor (a spline of control points) plus `Sides`. Each side is a **straight three-point** junction stub of 40 to 600 m; `kind` must be `t_junction`; stubs at least 150 m apart; never on a divided section; `|angle|` 30 to 150; the main road's edge line is interrupted on the junction side; mouths are square (no fillets). | `NetCore/RouteModel.cs` (`MakeJunction`), `RouteSpecValidator.cs` |
| Curve limits | the validator rejects a main spline with a curve tighter than **40 m radius** and grades over 8%. A roundabout ring (about 20 to 45 m radius) is below that limit; the limit currently applies to the main spline only. | `RouteSpecValidator.cs` (`MinRadiusMetres`) |
| Splines | `RoadSpline` passes through every control point, no self-loops; there is no closed-loop mode. | `NetCore/RoadSpline.cs` |
| Ground truth | `RouteModel.GroundHeight(x, z)` asks every corridor in order and returns the **first** formation that contains the point; there is no elevation choice, so two roads stacked over each other (a flyover) cannot be represented. `TerrainModel.HeightAt` picks the corridor with the smallest distance beyond its formation, flat under the road (0.35 m under the surface). Both loop over **all** corridors per query (fine for a main road and 3 stubs; at 83 network edges it needs a per-chunk corridor list). | `RouteModel.cs` |
| Bridges | `BridgeSpec` spans a terrain **valley** on the main road only: deck, parapets, piers, a valley dug into the terrain. It is not a road crossing another road. | `RoadGeometry.cs`, `TerrainModel` |
| Cross-section | `[median] inner shoulder, lanes, outer shoulder, verge`, markings (dashes, solid, yellow), a concrete jersey median, guardrail, bridge parapets. No kerb, sidewalk, planter, drain, painted barrier, zebra crossing, arrow or bay marking. | `NetCore/RoadCrossSection.cs` |
| Props | deterministic data-driven scatter (`propRules`, `props`), one merged mesh per chunk for placeholders (9 to 11 sub-materials), production prefabs instantiated one by one within 2 rings, no catalogue collider, text on four placeholder ids only. **Found while preparing the lighting work: the old street-light and utility-pole rules (offset 1 to 2.5 m) were inside the 3 m clearance kept beside every road, so they placed nothing.** Fixed for the new lamp rules (`PropRule.exclusionMargin`); the old pole rules are untouched. | `WorldPolicy.cs`, `ChunkStreamer.cs` |
| Vehicles and collision | wheel colliders on road and terrain mesh colliders per chunk (road collider enabled to ring 3); vehicles stand on colliders, **not** on `GroundHeight`. Kill-floor and prop placement use the route model. Three drivable vehicles: 4x2 light truck (max steer 32 deg), 6x2 medium truck (28 deg), city bus (30 deg, 40 passengers). | `ChunkStreamer.cs`, `VehicleController.cs`, `supabase/seed.sql` |
| Traffic, progress, minimap | all follow `Main` only; traffic agents are kinematic and read speed limits by zone and surface. | `TrafficManager.cs`, `RouteModel.DistanceAlong` |
| Jobs | pickup within 40 m and below 6 km/h, delivery within 100 m and below 6 km/h; origin and destination are database locations (`world_x`, `world_z`). | `DrivingSession.cs` |
| Bus | a stop is served within 45 m (the server allows 60 m) below 6 km/h; stops, demand, fares and routes are server data; a route is an ordered list of stops. | `BusRules.cs`, `BusSession.cs`, `supabase/seed.sql` |
| Kano in the game | Kano is a PLANNED city on the world map; the only OPEN route is Lagos to Ibadan. Kano exists as a draft test corridor TC-1 (`docs/kano/ng-kano-slice.route.draft.json`, 1.65 km), the city plan (`docs/kano/city/city_plan.json`) and the in-game review scene (private assets). There is no drivable Kano. | `docs/kano/`, `world_map.json` |
| Network plan | 54 nodes and 83 edges: ring road 16 junctions (RR0 to RR337.5; RR0, RR90, RR180, RR270 roundabouts, RR225 the interchange), 8 radial crossings of the middle ring (MA0 to MA315, four-way), 8 old-city gate crossings (four-way), the A2 expressway from the south-west (terminal, gateway, logistics junction, interchange), district access junctions (T). | `tools/kano/city_data.py` |
| Assets | production kit: 14 residential assets (v002, v003 frozen), 13 environmental assets (E1 to E3) with six corrections (E4); the dev props (16 placeholders) include fuel station, bus stop, warehouse, shop row, kiosk, stall, billboard, apartment block, telecom mast; nothing is approved. | ARO-CONTENT registry, `prop_catalog.json` |

## 3. Rules that hold for every phase

1. **Reuse before create.** The registry is searched for GLOBAL and REGIONAL assets first; a new asset gets a class: GLOBAL `aro_`, REGIONAL `rg_ngn_` (northern Nigeria), CITY_SPECIFIC `kano_`, LANDMARK `lmk_kano_`.
2. **The chain, every asset:** reference (R-nnn, documentary sources where no image exists) -> asset breakdown approved by the owner -> Blender source -> exported FBX/GLB and manifest -> validation -> Unity import (prefab built by the importer) -> placement in city data -> **in-game drive and screenshot review**. A step is not skipped and a later step is not claimed from an earlier one.
3. **No flat substitutes.** No reference image as a plane or backdrop, no billboard standing in for a building, no coloured box standing in for a finished building. Dev placeholders (generated boxes) exist only as marked placeholders (`status dev`, `PLACEHOLDER` in the description), never as finished art.
4. **Documented per asset:** real-world dimensions, triangle budget per LOD, collision (kind and where), intended use, class, references. Budgets are set in the asset matrix before modelling and checked after.
5. **Engine changes only as stages** of the existing proposal: optional fields or a higher `schemaVersion`; a golden-master hash of every existing route (mesh, ground heights, prop placement) must not change; deterministic generators (every client builds the world from the same data); one stage, one review, one owner decision.
6. **Technical validation is not visual approval.** A Blender render, an import test or a passing drive test does not set `production_approved`; only the owner does, after looking at in-game pictures.
7. **Nothing the rules forbid without separate approval:** driving physics, the weather simulation, Supabase and production data (Kano jobs, bus routes, world-map status), multiplayer, production configuration. Where a phase needs one of them it says so.
8. **Design vehicles** for turning and parking checks: the three drivable vehicles (light truck, medium truck, city bus). Footprints and swept paths are measured from the vehicle assets and a drive test (`VehicleAssetValidator` already records length and pivot); a rule of thumb used only to size first drafts: a 12 m bus at 30 deg steering sweeps an outer front radius of about 13 m, so a single-lane roundabout carriageway needs about 8 m width plus a mountable apron. **These are draft sizes, to be replaced by measurement.**

Common acceptance for every environment (the definition of done): (a) reference and breakdown approved; (b) assets pass Blender validation and export checks; (c) imported, prefabs generated, LOD triangles equal the Blender record; (d) placed by data, deterministic; (e) **driven in the game with each design vehicle**: entry, exit, parking or docking as designed, no snagging on colliders, recorded as telemetry; (f) performance measured against the tier budgets (desktop 400 draws, mobile 150) with LOD transitions checked; (g) in-game screenshots for day, night and wet; (h) the owner's visual review recorded. Only (h) completes it.

## 4. Gameplay integration facts that shape the designs

* **Trucks:** the pickup radius is 40 m and the stop limit 6 km/h, so a loading bay is a place a truck stops within 40 m of the job's marker; there is no docking assist and no reversing aid, so bays are sized for a manual reversing manoeuvre with 1 m clearance each side. A dock-trigger system (precise docking) would be a gameplay change and is not assumed.
* **Buses:** the serve radius is 45 m (60 m server side) and a stop is one location; a terminal is therefore **one stop** in the server's model and its bays are visual and navigational (painted, signed, numbered). Per-bay stops would need server and data support.
* **Data:** Kano jobs, bus routes, stops and the world-map status are production data (seed and migrations). Placing the sites does not create gameplay; creating the gameplay needs the owner's separate approval and a data change. Until then the sites are drivable and testable in a test route.
* **Collision and navigation:** compound walls, gates, buildings and kerbs need colliders; the catalogue gives box colliders to placeholder props only and production prefabs get none (RB-10), so sites carry their own colliders (E2 defines it). Traffic AI is kinematic and follows `Main`; AI inside compounds and terminals is out of scope (parked decorative vehicles only).

## 5. Technical proposal: road-engine work for roundabouts, junction forms and flyovers (for the owner's decision)

This section is the "technical implementation proposal" the brief and the project rules require before any road-engine geometry is introduced or junction limits change. It refines stages E1 to E5 of `ROAD_ENGINE_PROPOSAL.md` with what this brief needs. **No code for it exists.**

### 5.1 The seven roundabouts, mapped to the network (indicative; the owner confirms)

The Kano plan has four roundabout nodes and one interchange node. The seven designs use them and promote two four-way crossings; the junction count stays 54.

| # | Design | Node and district | Draft geometry (to be validated by swept-path drive tests) | Markings and surroundings | New and reused assets | References and blockers |
|---|---|---|---|---|---|---|
| 1 | **Landmark roundabout** | MA225 (promoted from a four-way): the radial from the A2 interchange to the old city's south-west gate, beside D09 | central island radius about 22 m, single circulating lane about 8 m plus 2.5 m mountable apron, 5 legs with splitter islands, entry flares | yield lines, arrows on entries, black-and-white painted kerb, clipped hedge ring, ground uplights; surrounded by D09 hub buildings and commercial frontage | `lmk_kano_roundabout_monument_a` (a **designed civic monument**, flagged fictional; see decision 4), hedge ring, uplights, sign arm; reuse streetlights, planters | R-034 (monument, island, sign arm), R-004 (landscaped roundabout with a fountain). Needs the owner's ruling on the monument. |
| 2 | **Modern urban roundabout** | RR90 (existing): north ring beside D08 | island radius about 16 m, two circulating lanes (2 x 3.5 m), 4 legs, zebra crossings 5 m back from the yield lines | planted centre with shrubs and palms, single-arm lights, modern commercial buildings and a hotel plot | `aro_planter_island_*`, `aro_palm_ornamental_*`, `aro_shrub_flowering_*`; commercial frontage `kano_*` (needs M-02 references) | R-034, R-035, R-022 (modern boulevard: towers, planted median, banners); commercial frontage references M-02 missing |
| 3 | **Traditional African roundabout** | RR180 (existing): west ring beside D07 | island radius about 14 m, single lane about 8 m, 4 legs | patterned paving ring and island wall with geometric (Hausa-inspired) panels, red earth finish, local planting | `rg_ngn_pattern_panel_*`, `rg_ngn_island_wall_*`; reuse the E4 earth materials | documentary sources R-024 to R-029 only: **a flagged design interpretation** (the M-01 gap is only partly closed); owner decides whether to proceed on that basis |
| 4 | **Large multi-lane roundabout** | RR270 (existing): south ring, toward the palace precinct | island radius about 30 m, **three** circulating lanes (3 x 3.65 m), 6 legs with 3-lane flared entries | lane-use arrows, spiral lane guidance lines, yield lines, splitter islands with signs; truck-friendly apron | `aro_road_sign_arm_*`, guidance symbols (RB-12), reuse double-arm lights | R-004, R-034; needs RB-12 symbols |
| 5 | **Commercial district roundabout** | MA0 (promoted): radial of the east ring beside D05 | island radius about 12 m, two lanes, 4 legs, bus bays on two approaches | sidewalks about 3 m, zebra crossings, trader frontages (shops, stalls, offices), kerbed planters, bus shelters | `aro_bus_shelter_*`, `kano_commercial_frontage_*`, stalls (E3 market assets), billboards | R-034, R-031; commercial references M-02 and M-04 missing |
| 6 | **Green landscape roundabout** | RR0 (existing): east ring, suburban fringe | island radius about 20 m, single lane about 7.5 m, 4 legs with planted splitters | trees, grass, shrubs, low lights; residential frontage of the R-006 family | `aro_tree_shade_*`, `aro_shrub_*`, `aro_hedge_*` (seasonal vegetation exists in E3) | R-006 (frontage), R-035 (planting); no new reference needed for the plant kit |
| 7 | **Highway interchange roundabout** | RR225 (existing interchange node): A2 meets the ring | ground-level ring of radius about 45 m with two lanes, the A2 expressway passing over on a deck (E5), 120 m merge tapers on slip roads | planted islands, paved pier plazas, lattice-panelled piers, deck edge lights, gantry signs | `aro_flyover_deck_*`, `aro_flyover_pier_*`, `rg_ngn_pier_panel_*`, `aro_gantry_sign_*`, barriers | R-035, R-036, R-005 (interchange with loops), R-023 (divided highway with a flyover); layout must be **designed**: R-036's merges are not engineering-valid |

### 5.2 What the engine must learn (each an additive, versioned stage)

| Stage | What | How, against the code found in section 2 | Risk |
|---|---|---|---|
| **E1 cross-section** (RB-17, RB-04, RB-09, RB-12) | kerbs, sidewalks, planter and painted-barrier medians, drainage channel, road classes as data, zebra crossings, arrows, stop and bay lines, per-profile prop clearance | new optional elements in `RoadCrossSection` and profile fields; a marking-symbol layer; profiles without the new fields build exactly as today | low. Needed by every phase. |
| **E2 sites and signs** (RB-13, RB-07, RB-10) | **site plates**: a flat paved polygon (material, kerb edge, falls, collider, height rule) for yards, terminals, forecourts; text on production signs; prefab colliders from the catalogue | new `SitePlate` data placed by chunk; `MakeLabel` successor reads a text slot declared by the prefab | low |
| **E3 junction forms** (RB-02, RB-11) | four-way junctions, fillets, relaxed spacing and the divided-road rule under a new `schemaVersion` | two opposing side corridors at the same `s`; the validator keeps the old rules for version 1 | medium |
| **E4 road graph** (RB-16) | `Sides` become edges with node links; `Locate(x, z)` returns corridor, `s`, lateral; traffic, progress and the minimap work on a chosen path; **`Main` keeps its meaning for jobs and progress**; a **per-chunk corridor list** so `GroundHeight` and `TerrainModel.HeightAt` do not loop over 83 edges per query | edges, nodes, a spatial index | medium |
| **E5 roundabouts and grade separation** (RB-01, RB-03) | a roundabout = a **ring corridor** (a closed spline mode, with the 40 m radius rule relaxed for ring corridors only) plus leg corridors joined by entry and exit geometry; a flyover = a corridor crossing another at a different elevation: **ground height must choose between stacked corridors by elevation**, terrain and underpass cuts follow, deck and pier generation reuse the bridge code | `RoadSpline` closed mode; `RouteModel.GroundHeight` gets an elevation argument; terrain conformity per corridor level | **high** (physics, camera, terrain). Requires a written physics plan and the owner's decision before merge. |
| **E6 city streaming** (RB-15) | baked per-chunk blocks, impostors, skyline | additive layer beside `ChunkStreamer` | medium (WebGL memory); decided from E0 measurements |

Flyover candidates (the network decides; the owner picks): **F1** the ring interchange RR225 (the flagship, with roundabout design 7); **F2** the A2 over the logistics service junction X_LOG at D10 (an underpass for trucks); **F3** a radial arterial over the middle ring at a heavy crossing, for example MA270 toward the palace precinct. Each connects to the network by ramps that are corridors of the graph, never isolated structures.

Roundabout and flyover collision: the ring, legs, ramps and decks are generated road meshes with the same mesh colliders as roads; kerbs are low colliders (a flat collision height if wheels misbehave over a kerb, as the engine proposal says); piers are box or capsule colliders; underpasses have a clearance of at least 5.5 m (assumption, to be checked against the tallest design vehicle).

### 5.3 Compatibility
Multiplayer needs no change (the world is built from shared data, so every generator must be deterministic; a content fingerprint in session creation is proposed in the engine proposal and is not made). Driving physics, weather and Supabase are not touched by E1 to E4; E5 changes what the ground is where roads stack and comes with drive tests and the owner's review.

## 6. Proposed order of work (to be confirmed)

| Step | Content | Needs the owner | Unlocks |
|---|---|---|---|
| **S0** (now) | finish the lighting foundation and its CI verification; E0 measurement and golden-master tests (already approved as measurement-only); asset breakdowns for the engine-independent assets | the lighting milestone is already approved | night functionality, the measured baseline, the library work below |
| **S1** | engine-independent asset production in test scenes: street furniture (lights, barriers, signs, fences, bollards), warehouse components (doors, docks, gates, fences, offices, containers), terminal components (shelters, ticket offices, boards), vegetation, pier and deck components | approval of each asset breakdown (matrix) | library growth; nothing waits on the engine |
| **S2** | **E1 then E2** (cross-section, site plates, signs) | approve E1 and E2 | urban highways (partly), **logistics sites (phase 3), bus terminals (phase 4)**, supporting buildings (phase 5), sidewalks and drainage |
| **S3** | **E3 then E4** (four-way, fillets, graph) | approve E3 and E4 | the connected network, commercial and residential street grids, district junctions |
| **S4** | **E5** (roundabouts, flyovers, interchange) | approve E5 and a physics plan | phase 1 (seven roundabouts) and phase 2 (flyovers, interchange) in the network |
| **S5** | E6 and the performance pass | decided from measurements | city density |

Gameplay data (Kano route, jobs, bus routes, world-map status) is a separate approval that can come at any point after S2.

## 7. Phase breakdown (the report the brief asks for, per phase)

### Phase 1: seven roundabout designs and supporting road geometry

| Item | Report |
|---|---|
| **Exists** | the seven concepts are mapped to network nodes (5.1); references R-004, R-005, R-034, R-035, R-036, R-006, R-024 to R-029; the network plan with 4 roundabout nodes and 1 interchange; T-junction stubs; markings along the road only; the dev props for streetlights, signs and planters (placeholders) |
| **To build** | the ring and leg geometry of each design in the route model (E5), kerbs and painted kerbs, islands, splitters, zebra crossings and arrows (E1), junction fillets (E3), signs with text (E2), planting and lighting, the seven island compositions, surrounding frontage per district |
| **Reuse** | `PropScatter` and the catalogue for planting, furniture and frontage; `RoadCrossSection` for lane layouts; the lighting system (street-light rules by environment, light pooling) for the night requirement; E3/E4 vegetation and market assets; the bridge pier code for the interchange |
| **New assets** | per 5.1: `lmk_kano_roundabout_monument_a`, `rg_ngn_pattern_panel_*`, `rg_ngn_island_wall_*`, `aro_planter_island_*`, `aro_hedge_clipped_ring`, `aro_palm_ornamental_*`, `aro_shrub_flowering_*`, `aro_road_sign_arm_*`, `aro_uplight_ground`, `aro_bollard_light`, `aro_bus_shelter_*`, `kano_commercial_frontage_*` |
| **Engine changes** | E1, E3, E4, E5 (section 5); none is approved |
| **Risks** | ground height and terrain at a ring and at stacked roads; the 40 m curve rule; traffic and minimap are main-only until E4; turning clearance of the long vehicles; the traditional design rests on documentary sources only; the monument is an invented design; commercial frontage has no references |
| **Performance** | a ring adds a corridor and about 8 to 20 legs and islands of mesh per roundabout; the danger is the per-query loop over corridors (needs the per-chunk corridor list); frontage and planting at roundabout density are the draw-call risk (the plan estimated 360 draws in view for a dense residential district against a desktop budget of 400: **estimates until E0 measures**) |
| **Acceptance** | the common acceptance of section 3, plus per roundabout: each design vehicle completes a full lap and every entry-exit pair, with no collider snag, a swept-path margin of at least 1 m, correct yield and lane markings, kerb and crossing geometry present, lit at night, LOD transitions clean, frame budget held on Low and Medium in CI software GL measurements (frame time there is not a player's), in-game screenshots day, night and wet, owner review |

### Phase 2: urban highways, major junctions and flyovers

| Item | Report |
|---|---|
| **Exists** | expressway profiles with a jersey median and guardrail; lanes, markings, bridges over valleys; the lighting rules for median double-arm lights and junction masts (this milestone); the A2 gateway concept (R-007, R-008, R-012); the boulevard (R-022) and the divided highway with a flyover (R-023, which the register already flags as design inspiration until E5); the interchange (R-005); new references R-032, R-035, R-036 |
| **To build** | planted medians with bollard lights, painted barrier blocks, sidewalks, drains, access roads and junctions (E3), directional gantries and signs (E2), flyovers with entry and exit ramps and underpasses (E5), commercial and residential frontage along sections, landscaping; visual variety by section: dense commercial (D05), modern business (D08), residential (D06), traditional (D07, blocked), industrial (D10), suburban fringe |
| **Reuse** | the road classes and profiles (extended), bridge deck and pier generation for the flyover, the prop scatter with profile and median placement, the lighting rules, the E4 seasonal vegetation |
| **New assets** | `aro_flyover_deck_*`, `aro_flyover_pier_*`, `rg_ngn_pier_panel_*` (the lattice motif of R-035, generic), `aro_barrier_block_bw`, `aro_guardrail_*`, `aro_gantry_sign_*`, `aro_road_sign_*` set, `aro_bollard_light`, `aro_planter_median_*`, `aro_drain_cover_*`, billboard variants |
| **Engine changes** | E1 (planter median, kerb, sidewalk, drain), E2 (signs), E3 (junctions), E4 (graph), E5 (flyovers, ramps) |
| **Risks** | stacked-road ground height and underpass clearance; ramp geometry must be designed (R-036 is not engineering-valid); the 8% grade limit and 40 m curve rule on ramps; shadows and lighting on elevated decks (shadow distance, per-object light limit of 4 on long meshes); sign text needs verified route data (master plan flag 1) |
| **Performance** | a deck is a long mesh with piers every 15 to 25 m (the bridge code uses 18 to 20 m); lamp heads and pools on a deck add to the glow mesh (one draw call); medians with planting add instances (the vegetation is the cost); measured in E0 first |
| **Acceptance** | section 3, plus: a vehicle drives onto, over and under each flyover from every ramp; the underpass clearance holds for the tallest design vehicle; every ramp connects to the network (a graph connectivity test); lit decks; sign text from route data; in-game pictures; owner review |

### Phase 3: logistics warehouses and cargo distribution facilities

| Item | Report |
|---|---|
| **Exists** | D10 plan: warehouse with six docks, yard, truck-parking field, gatehouse, a truck fuel and service centre, perimeter wall, expressway frontage (R-013, R-014, R-015); dev props `warehouse`, `fuel_station` (placeholders); the job system (pickup within 40 m, below 6 km/h); cargo trucks; the lighting rules for compound lights |
| **To build** | three facilities: **A** large distribution warehouse (loading bays, wide access roads, parking and manoeuvring area, security gate and checkpoint, perimeter wall and fence, office and reception, cargo areas); **B** commercial goods warehouse (delivery parking, loading platforms, storage buildings, access road); **C** industrial logistics depot (several warehouses, truck park, container and cargo storage, turning zones, security, offices) |
| **Reuse** | E3 market and Hausa wall assets for perimeter walls where appropriate; fuel station placeholders as a base for the truck service centre; the vehicle asset validator for footprints; the lighting system (flood lights, gate lights) |
| **New assets** | `aro_warehouse_door_roller_*`, `aro_loading_dock_*`, `aro_dock_leveller`, `aro_gate_sliding_*`, `aro_gatehouse_*`, `aro_fence_chainlink_*`, `aro_fence_palisade_*`, `aro_container_*` (20 ft and 40 ft, stacked variants), `aro_pallet_*`, `aro_forklift_static`, `aro_weighbridge`, `aro_floodlight_mast`, `kano_warehouse_distribution_a`, `kano_warehouse_commercial_b`, `kano_depot_buildings_c`, `kano_office_logistics_*`, `kano_truck_service_centre` |
| **Engine changes** | **E1 and E2 only**: site plates for yards and truck parks (RB-13), prefab colliders (RB-10), text signs (RB-07), painted bays and arrows (RB-12). **No junction-form change**: the compound connects to the existing T-junction stubs (150 m apart) or to the test corridor |
| **Risks** | collision of walls, gates and buildings (colliders per prefab); gate openings must pass the widest design vehicle with 1 m clearance; yards need a flat paved surface without z-fighting against terrain; reversing to docks is manual (no assist); a production-data change is needed for jobs; draw calls of many repeated props (no instancing) |
| **Performance** | sparse district (about 0.5 to 3 plots per hectare in the plan; 15 to 90 draws estimated); a depot with containers and pallets is the repetition risk: merge or bake per site; measured against the tier budgets |
| **Acceptance** | section 3, plus: each design vehicle enters through the main gate, reaches each loading bay and the parking area, reverses to a dock without a collider snag, turns in the turning zones and leaves; a test job (in a test route or a test database, **not production**) picks up and delivers at the bay positions; night lighting of the yard, gate and docks; in-game pictures; owner review |

### Phase 4: major bus terminals, medium bus parks and local bus stations

| Item | Report |
|---|---|
| **Exists** | D09 plan: terminal building, canopied bays, apron with a landscaped island, gatehouse and barrier, parking, perimeter wall (R-009); dev prop `bus_stop`; bus routes and stops as server data; `BusSession` and `BusRules` (45 m serve radius, 6 km/h); the city bus with 40 passengers |
| **To build** | **A** major intercity terminal (ticket offices, waiting areas, departure bays, parking, arrival and departure zones, entrance and exit, security checkpoint, shops, information and destination boards, administration offices); **B** medium bus park (multiple bays, small ticket offices, shelter, boarding areas, gates, marked routes and destinations); **C** local bus station (boarding areas, sheltered waiting, a ticketing or payment point, roadside access); plus maintenance depot and parking |
| **Reuse** | the bus-stop placeholder, the commercial frontage assets, planting kit, lighting system (canopy lights, bay lights), text signs (E2) for destination boards |
| **New assets** | `aro_bus_shelter_*` (three sizes), `aro_ticket_office_*`, `aro_bay_canopy_*`, `aro_bus_bay_marking` (symbol set), `aro_departure_board`, `aro_gate_barrier_*`, `aro_kiosk_*`, `aro_bench_*`, `kano_terminal_major_a`, `kano_terminal_admin_*`, `kano_bus_park_b`, `kano_bus_station_c`, `kano_bus_depot_d` |
| **Engine changes** | E1 and E2 (site plates, markings, signs, colliders). No junction-form change |
| **Risks** | bays sized for a 12 m bus reversing (sawtooth or angled bays 45 to 60 deg, draft); **one terminal is one stop** in the server model, so bays are visual; passenger flow is not simulated (no pedestrians exist); a terminal's destination boards must show real route data; production-data change for routes and stops |
| **Performance** | sparse (D09: 138 plots in 69 ha); canopies and repeated bays are cheap if merged per site; boards with text cost UI canvases (the current label path is a world-space canvas per sign: limit them) |
| **Acceptance** | section 3, plus: a bus enters, reaches each designated bay, parks and departs without snagging; the stop is served inside the 45 m radius below 6 km/h (in a test route/database); the swept path of the bus clears islands and gates; night lighting of the apron, bays and boards; in-game pictures; owner review |

### Phase 5: supporting commercial buildings, service infrastructure and environmental details

| Item | Report |
|---|---|
| **Exists** | concepts for hospital (R-010), police (R-011), fuel and service (R-013 to R-015), modern villas (R-006); the dev prop set; E3 market and kiosk assets; E4 corrections; lit prop geometry for houses, shops, stalls and the fuel station (this milestone, placeholders) |
| **To build** | fuel stations (production), vehicle and truck service and repair, bus maintenance depot, parking areas, roadside markets, shopping centres, hotels, hospitals, police stations, government buildings, pedestrian crossings, traffic signals, roadside signs, drainage, streetlights, billboards, distributed by district (hospital and police in D08, markets in D03 and D05, services in D10 and D11) |
| **Reuse** | the registry first (GLOBAL and REGIONAL assets), the kit modules, the vegetation kit, the prop scatter by district |
| **New assets** | breakdowns per concept (the matrix lists them); shopping centre, hotel, government building and school have **no concept yet** (M-02, M-04, M-10): they are requested, not invented |
| **Engine changes** | E1 (crossings, drains), E2 (signs), RB-15 (city streaming) when density demands; traffic signals: a visual first, signal logic for traffic AI is a gameplay change needing approval |
| **Risks** | blocked concepts; real organisation names and crests in concepts (flags 3 and 5 of the master plan; generic names by default); density against draw-call budgets |
| **Performance** | the dense districts are where RB-15 matters (estimates 360 to 1,200 draws in view without streaming); E0 measures before any renderer work |
| **Acceptance** | section 3 per building, plus district-level checks: sensible distribution (rule-based, tested), draw-call measurements, drive-through review |

### Phase 6: lighting, night functionality, weather integration and performance

| Item | Report |
|---|---|
| **Exists** | `TimeOfDay`, `LightingModel` (sun, moon, ambient, fog, exposure), `WeatherSystem` (rain, wetness, grip), `VehicleLights` (headlights, brake, reverse, indicators), the tier system and `PerfProbe`; **built in this milestone so far** (pure, tested, not yet in the running game): the lamp ramp from the existing lighting state, light specs in the prop catalogue, per-environment street-light rules (village, residential, commercial, industrial, divided city road, expressway, junction masts, bridge ends), lamp field, glow geometry, the real-light selector; emissive lamp, window, stall and beacon geometry on the dev props; 1,158 unit tests pass |
| **To build** | the runtime (emissive materials, additive glow mesh and shader, pooled real lights, beacons, tiers, `?lamps=` hook), vehicle high beam and hazards with network flags, lit traffic, in-game night verification in CI, then the same lighting for every new environment (compound lights, terminal and yard lights, deck lights, roundabout uplights) as data |
| **Reuse** | everything above; no second clock, no duplicate system |
| **New assets** | luminaire production models by id (the catalogue ids stay), uplights, bollards, canopy and yard fixtures as part of each site |
| **Engine changes** | none for lighting beyond the additive prop fields already made; weather physics, puddles and reflections are separate: the pipeline has no screen-space reflections and no puddle or water layer (RB-21), so wet roads get an approximation (amber streaks under lamps, glossier asphalt) and **standing water and mud physics are not claimed** |
| **Risks** | nothing can be compiled or run here: Unity runs in CI (about 45 minutes per cycle); software GL at Low and Medium; per-object light limit of 4 on long meshes (why real lights are an enhancement); bloom only on High; lamp colours are estimates from generated references |
| **Performance** | +1 draw call for all lamp glow, three shared emissive materials, a handful of pooled real lights per tier (Low 0, Medium 4, High 8), measured with `PerfProbe` and the `[Lamp]` log |
| **Acceptance** | night and day screenshots per environment, lamps on and off, low and high beam, dry and wet; luminance thresholds; draw and triangle budgets; owner's visual review |

## 8. Honest status today

| Item | State |
|---|---|
| Inspection of the current road, prop, vehicle, job and bus systems | done (section 2) |
| This plan and the master plan update | done; needs the owner's decisions |
| New references R-032 to R-036 | registered (hash only) |
| Lighting foundation (pure models, catalogue light specs, street-light rules, dev geometry) | done and unit-tested (1,158 tests); **not yet in the running game**; not pushed |
| Lighting runtime and in-game verification | not started (next step of that milestone) |
| Any roundabout, flyover, highway, warehouse, terminal | **not started**; engine stages E1 to E6 not approved; no asset breakdown approved |
| Anything approved by the owner | nothing |

## 9. Decisions requested from the owner

1. **Order of work** (section 6): confirm S0 to S5, in particular building logistics sites and bus terminals (phases 3 and 4, E1 and E2) before roundabouts and flyovers (phases 1 and 2, E3 to E5).
2. **Engine stages:** approve, defer or decline **E1, E2, E3, E4, E5, E6** separately (section 5.2). E0 (measurement) is already approved.
3. **The mapping of the seven roundabouts** to the network (5.1) and the three flyover candidates (F1 to F3), or name others.
4. **The landmark monument** (design 1): it is an invented civic monument, not a real Kano landmark. Class it as CITY_SPECIFIC with a "fictional design" flag (my recommendation: the master plan reserves LANDMARK for real, verified landmarks), or as LANDMARK?
5. **The traditional roundabout** (design 3) rests on documentary sources only: proceed as a flagged design interpretation, or wait for references?
6. **Names and brands** in the new references (fuel station brand, supermarket and truck badges, sign text): generic fictional names and no real marks (the master plan default), unless you rule otherwise.
7. **Gameplay data for Kano** (route, jobs, bus routes and stops, world-map status): a production data change; approve separately, at a time of your choosing, after the sites exist.
8. **Missing concepts** (commercial frontage, shopping centre, hotel, government building, school; Hausa quarter references): please supply or confirm that I should proceed from the existing references and documentary sources with flagged interpretations.
