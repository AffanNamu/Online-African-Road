# Road engine stage E5: roundabouts, flyovers and ramps (built 2026-11-09)

**Authorised by the owner on 2026-11-09** ("E5 approved", defaults: landmark roundabout = a fictional designed civic monument, traditional roundabout = a fictional Hausa-inspired architectural interpretation, road signs = generic fictional brands, first flagship interchange = F1). This document replaces the plan `ROAD_ENGINE_E5_PLAN.md` (kept for the reasoning). Nothing here touches the shipped routes: the golden master of `ng-lagos-ibadan` and the Kano slice draft is unchanged, and no existing route uses a single E5 field.

## 1. Verification levels (what is true today)

| Level | Roundabouts (6 designs) and F1 |
|---|---|
| 1. Implemented | yes: engine (`RouteModelE5.cs`, `RingGeometry.cs`, `RoadNetworkChecks.cs`, `SweptPath.cs`, `AutoPaths.cs`), data model, two generated test routes, Unity wiring (`ChunkStreamer`, `DrivingSession`, `SmokeAutopilot`) |
| 2. Automated tests passed | yes: **1,353 .NET tests pass** (1,309 earlier ones unchanged + 44 for E5): 568 swept paths (7 designs x 4 vehicles x every entry/exit pair), headroom, grades, piers, ground by level, terrain, determinism, a golden digest of both routes |
| 3. Imported into / compiled by Unity | **yes**: CI run 325 (commit ab45790): the Unity EditMode tests pass and the WebGL build succeeds with the E5 code (the first push, run 324, failed on a missing `using ARO.World;` in `SmokeAutopilot.cs`, fixed in ab45790). The WebGL build boots and the existing drive scenarios still pass on the older routes. |
| 4. Verified in actual gameplay | **automated drive in the real WebGL build: yes; a person's play: no.** CI run 325, lighting job, `tools/webgl-e5.js`: **74 of 74 checks passed**. An autopilot (a follower on the swept-path model, driving the game's real vehicle physics on a software renderer, 8 scenarios) took: the **bus up the embankment, over the roundabout on the F1 deck and down the far side** (reached the end, largest distance from its lane 0.02 m, height 12.3 to 17.9 m, on the deck at 17 m or more); the **medium truck along the ground road under the deck** (reached the end, 0.23 m, 9.9 to 11.5 m, never on the deck); the **bus down the off ramp** (0.03 m) and the **light truck up the on ramp** (0.03 m); the **bus round the landmark roundabout** (0.10 m) and the **large four-lane roundabout** (0.08 m); the **medium truck round the traditional roundabout** (0.17 m); the **light truck round the green roundabout** (0.11 m). Every drive: rendered frames at 20, 50 and 80 percent of the path, no exception, no invalid route or road network, the vehicle never left the world. What this does **not** show: a person driving, handling and camera feel, frame rate on a real GPU, the commercial and highway roundabouts (four of six were driven), the **camera under the deck** (frames were taken, nobody has looked at them), multiplayer. |
| 5. Visually reviewed | **no**. The island props are the game's grey placeholders (`junction_mast`, `palm_tree`, `kiosk`, ...); the monument of the landmark roundabout and the traditional gate are **not modelled** (they are CITY_SPECIFIC / LANDMARK assets for later). Nothing is `production_approved` **Agent reading of the real frames (2026-11-09, not the owner's review):** the 24-frame contact sheet of CI run 325 (`webgl-light-e5-sheet.png`, branch `ci-lighting`) shows the expressway with its median barrier and the F1 deck standing on piers in the distance, the bus passing close to a concrete pier beside the ramp, five roundabouts with kerbed rings, painted kerb blocks on the splitter islands, flared mouths, zebra crossings, planted and paved islands (trees, palms, the game's placeholder props) and the minimap drawing the ring and legs. The geometry reads as designed in every frame; what a person would still judge (feel of the camera on the deck, scale of the islands, the placeholder props, night) has not been seen by anyone. |

## 2. What exists

### 2.1 Ring corridor
`RoadSpline.Circle` (closed, counter-clockwise, right side outwards, exact tangents so it closes without a kink) + `RingSpec`. The circulating carriageway is an ordinary `RoadProfileSpec` (all lanes circulate the same way; the centre line is a white lane line or none), built with 1.5 m stations (a 4 m station would sag 9 cm on a 20 m ring; the sag is under 3 cm). The island is a flat disc (`RingIsland`) at the height of the ring's inner kerb top, with an optional paved apron and a core of planter, paved, grass or earth; its props are hand-placed in island coordinates. Terrain, ground height, prop exclusion and the terrain-tile cut all know the island.

### 2.2 Legs, mouths, splitters, markings
* A leg is a corridor that meets the ring's outer paved edge **radially** (checked: within 12 degrees, 0.4 m of the edge, 0.25 m in height). Legs are generated (`RingLegSpec`: a bearing in world degrees, a length, a rise), or are existing corridors that end on the ring (`attachTo`: the ring is placed at the end of a corridor; `connect`: a road whose end touches the ring).
* `mouthScale` (1 to 2) widens every leg where it meets the ring and eases back over 20 m: the flared kerbed entry that buses and trucks need. `flare` is the E3 fillet for unkerbed rings.
* The ring's outer kerb and edge line are dropped across every mouth; the island side is never dropped. The ring's own station zero is put in the widest gap between legs so no mouth straddles the seam.
* A raised splitter island (teardrop, 12 cm, painted kerb) sits on the centre line of every leg; every leg gets a yield line on its inbound lanes and a zebra crossing. Hand-placed pavement symbols (bus bays etc.) work on legs, ramps and the ring.

### 2.3 Flyover, deck and ramps
* `BridgeSpec.overRoad` marks a **grade-separated deck**: no valley is dug, the road is not on the ground there. The deck, parapets, slab underside and piers are the existing bridge code, now available on **any corridor** (the main road and `RoadSpec` roads). `pierGaps` leave the clear span over a lower road.
* `RoadSpec` (schemaVersion 3) adds free-form roads with their own control points, decks and `joins` (where an end meets the paved edge of another road: the gap in that road's kerb and edge line). Role `ramp` or `flyover` is held to a **6 percent** grade (the engine's general limit stays 8).
* **Ground by level.** On a route with a deck, `GroundHeight(x, z)` returns the *lowest* surface (the kill floor and the lamp pools need the ground, a vehicle on the deck is never below it) and `GroundHeight(x, z, nearY)` the surface closest to `nearY` (a vehicle on the deck and one under it standing at the same x, z). On a route without decks it is the first corridor that contains the point, exactly as before.
* **Terrain.** The terrain under a deck is whatever lies below it (another road, or natural ground), not the deck; the terrain tile is not cut away under a deck. A road climbing to a deck sits on an embankment (cut-and-fill slope as for any road, blended out over the last 12 m: there is no retaining wall).
* **Network checks** (run when the route builds; a failure throws and names the roads): same-level overlap of two roads without a junction between them, a road passing over another without a deck, **headroom under a deck at least 5.5 m** (to the underside of the slab), decks nearly level (4 percent), ramp grade, tightest curve 40 m, piers standing on a road, legs that do not meet their ring, mouths that overlap, rings with fewer than three legs.
* **Swept-path model** (`SweptPath`): a pure-pursuit-free front-axle (Stanley) simulation of a bicycle model of each *design vehicle* along the lane path, which measures steering demanded, body strikes (island core, leaving the road) and how far a wheel runs outside the pavement. Design vehicles are the game's own placeholders: truck_light_01 (6 m, wheelbase 4.2 m, 32 degrees), bus_city_01 (10 m, wheelbase 8.2 m, 30 degrees), a car, and a stated design value for the not-yet-built truck_medium_01 (9 m, wheelbase 5.6 m, 28 degrees). **A real production model with other dimensions must be re-checked**: run `dotnet test --filter KanoE5SweptPath`.

### 2.4 Test routes (never connected to the production network)
Both are fictional engine tests, traffic off (`densityScale` 0, because the traffic AI follows only the main road), generated by `python3 tools/e5/gen_e5_routes.py` so the numbers that must agree (a link on a ring's paved edge, a ramp on the expressway's edge, pier gaps over the carriageway) are computed:

| Route | Contents |
|---|---|
| `ng-kano-e5-roundabouts` | a chain: main road, then six roundabouts joined by 260 m links |
| `ng-kano-e5-f1` | a four-lane ground roundabout (island 38 m), six legs (two arterials, four local), a dual expressway on a 600 m deck flying over it (the main road at 17.8 m; headroom under the slab 6.2 m against the 5.5 m minimum), an off ramp and an on ramp (about 3 percent) |

The seven designs differ in shape, not only size (asserted by a test):

| Design | Island | Circulating | Legs | Kerb | Mouth | Notes |
|---|---|---|---|---|---|---|
| Landmark (MA225) | 22 m, paved | 2 lanes, 8.0 m | 5, irregular star | black/white | 1.9 | promenade walkway round the island; centre `junction_mast` is a **placeholder** for the monument (fictional civic monument, not modelled) |
| Modern urban (RR90) | 18 m, planter | 2 lanes, 7.0 m | 4 | black/white | 1.9 | 2 m mountable apron, trees and bushes |
| Traditional (RR180) | 15 m, earth | 1 wide lane, 6.8 m | **3** | none | 1.8 | unkerbed, 9 m flares, ring of palms; the Hausa-inspired architecture is **not modelled** |
| Large multi-lane (RR270) | 30 m, planter | **4 lanes**, 13.6 m | **6** (arterial legs) | black/white | 1.8 | 22 m splitters |
| Commercial (MA0) | 14 m, paved | 2 lanes, 7.0 m | 4 | black/white | 1.9 | 3 m market walkways, kiosks on the island, bus bays on the legs |
| Green (RR0) | 24 m, grass | 1 wide lane, 7.6 m | 4, one exit rising 4 m | none | 1.8 | 11 m flares, ten trees, fifteen bushes |
| Highway interchange F1 (RR225) | 38 m, planter | **4 lanes**, 13.2 m | 8 (6 + 2 ramps) | black/white | 1.65 | **grade separated** |

Measured wheel overrun at the mouths (the worst of all entry/exit pairs, metres a wheel runs outside the pavement): car 0.50 to 0.73, light truck 0.58 to 0.90, medium truck 0.80 to 1.16, bus 1.46 to 2.28. The bus's value is the largest because the game's placeholder bus has an 8.2 m wheelbase on a 10 m body; a real city bus is shorter between axles. Limits in the test: 1.0 / 1.2 / 1.5 / 2.4. No vehicle strikes the island or leaves the road on any of the 568 paths.

## 3. What is not done (honest list)
* One-way signing, turn rules and lane connectivity (the road graph of E4 is not extended to rings or decks); traffic AI does not use rings, ramps or decks; job progress (`DistanceAlong`) still follows the main road only.
* **Camera under a deck**: the follow camera has no ceiling logic. CI frames will show whether it is acceptable; the owner decides. A fix (shorter boom or ignoring the deck) is not made.
* Retaining walls at deck abutments, crash barriers on ramps, lighting of the deck underside, gantry signs, noise walls.
* The monument, the traditional gate, the pier panels and the gantries are assets (Family C/D, LANDMARK/CITY_SPECIFIC), not engine.
* Multiplayer: the world is built from shared data and every E5 generator is deterministic (a test builds each route twice and compares digests); no network code changed.
* Elevated terrain that cuts across a deck's footprint (a hill higher than the underside) is not handled; the test routes are on flat ground.

## 4. Migration and rollback
* **Migration.** Additive. A route opts in with `schemaVersion: 3` and `rings` / `roads` / `bridges[].overRoad`. Routes at 1 or 2 are untouched and cannot use the new fields (the validator says so). Production road data (`ng-lagos-ibadan`, the Kano slice) was not edited. To put a roundabout on a real route later: end the through road at the ring (`attachTo`) or build the ring on a spur; replacing a through road by a ring on the main road needs a *cut* (not implemented, because it changes where the main road runs and what the route's progress means: an owner decision).
* **Rollback.** Delete the two E5 route files (they are only reachable through the smoke page allow-list `SmokeMode.TestRoute` and the tests) or revert the commits named below. Nothing in the live game loads them. The engine paths run only for routes that declare E5 data; removing the data restores the previous behaviour bit for bit (the golden master proves the engine part).
* **Changed behaviour for existing routes: none** (`GroundHeight` uses the first-corridor rule unless the route has a deck; `TerrainModel`, `PropScatter`, `RoadGeometry` take the old path for every old route).

## 5. Files
`NetCore/RoadSpline.cs` (Circle), `RouteSpec.cs` (data), `RouteSpecValidator.cs` (version 3 rules), `RouteModel.cs` (corridor fields, ground and terrain by level), `RouteModelE5.cs` (ring, leg, island, joins), `RingGeometry.cs`, `RoadNetworkChecks.cs`, `SweptPath.cs`, `AutoPaths.cs` (paths and the follower), `RoadGeometry.cs` (decks and piers on any corridor); Unity: `World/ChunkStreamer.cs`, `World/RouteDefinition.cs`, `Game/DrivingSession.cs`, `Game/SmokeAutopilot.cs`, `Game/SmokeMode.cs`; tools: `tools/e5/gen_e5_routes.py`, `tools/webgl-e5.js`; tests: `RoadE5Tests.cs`, `KanoE5RouteTests.cs`, `AutoPathTests.cs`, `golden/e5_routes.json`.

## 6. Bugs the checks found while building this (kept as regression tests)
* The seam of a closed ring was treated as the *end of a road*: a thin wedge outside the ring near the seam was not road for the terrain, ground height and tile cut (`TheSeamOfEveryRingIsRoadNotTheEndOfOne`).
* A leg's wide flared mouth also claimed the ground beyond its end (the legacy "end cap" rule): ground height now ignores the cap on routes with E5 elements, as the terrain already did for routes with site plates.
* Entering a ring with a sharp corner between the leg's lane and the ring's lane needs more steering than any of the vehicles has; the path is now a tangent arc and the legs flare.
