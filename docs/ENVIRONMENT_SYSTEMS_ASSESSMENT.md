# Environment systems assessment: what the game can already do, what it cannot, and what a change would touch

**Status (2026-11-01): ASSESSMENT ONLY.** Nothing in driving physics, the road system, the weather system, time of day, networking, the backend or any live data was changed to write this. Everything below is **read from the code** at commit `1099154` of `work/kano-residential` (the same code as `main` for these systems). **Nothing here is a measurement**: no number in sections 3 to 6 was produced by running the game. Where a figure is arithmetic on a value found in the code, the formula is shown; where it is a literature range or my own proposal, it says so.

The owner's direction (2026-11-01): Kano must show authentic local environments, not only the modern residential street: traditional compounds, older close-set neighbourhoods, rural villages, markets and roadside shops; road conditions from new asphalt to broken asphalt, compacted earth, sand, mud; the **rainy season as a gameplay feature** that changes driving physics; a **seasonal system** that changes the whole world; and a reusable environmental library. The player should feel the difference between a modern city, a dusty village road and a muddy rural road in the rainy season. Eight new references (R-016 to R-023, `docs/kano/reference_register.json`, private images) show that target.

## 1. Short answer
* **What the game already has** is a *scaffold*: a clock-driven sun/sky/fog model, a rain toggle that darkens and glosses two road materials, spawns camera-following rain particles and lowers tyre grip by one scalar on the player's vehicle, a three-value road condition (`good | worn | damaged`) that bumps the road surface by centimetres and slows traffic, a zone vocabulary (`urban | commercial | industrial | rural | highway`) that selects scatter rules, and a deterministic world generated from data on every client.
* **What it does not have** is almost everything that makes the three experiences different: **no unpaved road type, no mud, no puddles or standing water, no pothole, patch or crack, no per-surface grip, no splash, no vehicle dirt at runtime, no windscreen or wipers, no season, and no way for a player or the server to start rain at all** (the only trigger is a developer key, F2, in the Editor or a debug build; the Settings row "Weather System" is marked *Soon* and a CI test asserts it has no control).
* **The world has no traditional architecture at all**: the scatter catalogue is 16 development placeholders (cubes and primitives), my 14 Blender assets are the first production content, and they are modern villas.
* **Almost all of the missing capability can be added additively** (new enum values, new materials, new data, a pure testable model for weather like `LightingModel`), but **three items change shared behaviour and need your approval first** (section 5): a per-surface grip source for the wheels, new road surface kinds in the cross-section, and a weather/season state that every client agrees on.
* **What I can do now without touching any of that** (section 6): author the environmental asset library in Blender (traditional buildings, markets, vegetation in dry and wet variants, kerbs, drains, road-damage geometry), register all references, and build review scenes that show materials in the real game build, once the private build can run.

## 2. Capabilities the direction asks for, and where each stands
Status: **HAVE** (works in the game today), **PART** (a piece exists), **NONE** (nothing exists), **HOOK** (a field or class exists that nothing drives).

### 2.1 Architecture and settlements
| # | Need | Status | What exists | What is missing |
|---|---|---|---|---|
| A1 | Modern residential | PART | 14 production assets (villas, walls, gates, lights, poles, sign, trees, hedge, shrub); prefabs build from the exports; the review scene shows them | real-asset Unity import and in-game verification are still blocked (owner prerequisites); no Kano route or world-map entry to place them |
| A2 | Traditional / local compounds | NONE | placeholder `concrete_house` only | everything: Hausa-inspired compound walls, entrance blocks, mud-brick and rendered houses, corrugated and flat roofs, courtyards, water tanks (R-016 to R-021 show them). Needs references that are *verified*, not only generated |
| A3 | Older, closely arranged neighbourhoods | NONE | `PropScatter` places props in rows by zone rules along one corridor; per-chunk density caps | a plot/lot layout (rows of different, touching buildings with shared party walls and narrow lanes). Today every building is a free-standing scatter instance; touching rows need either authored row assets or the "sites" idea of the engine proposal (RB-13) |
| A4 | Rural villages | PART | `rural` zone with `concrete_house`, `palm_tree`, `tropical_tree`, `bush` rules | village form (clusters of small houses, fences, granaries, tracks to the road), as assets and as site data |
| A5 | Markets and roadside shops | PART | `commercial` zone rules: `shop_row`, `kiosk`, `market_stall`, `bus_stop` (all placeholders, no sign text except four placeholder labels) | authored shop rows and stalls with umbrellas and awnings, readable sign text slots (RB-07), people-free but busy-looking frontage, goods |
| A6 | Modern boulevard / highway | PART | divided profiles with a concrete median and jersey barrier, bridges with parapets, junction stubs | **kerbs, sidewalks, drains, planted medians, flyover crossing over a carriageway, gantry signs** (R-022 and R-023). These are cross-section and grade-separation features (engine proposal E1, E5), not assets |

### 2.2 Roads (the surface the player actually drives on)
`Surface` has three values. In the route-model path (the one in production) the value changes **only** the geometry undulation (`RoadWear`: 0 / 1.4 / 4.5 cm amplitude, i.e. about ±5 cm at most for `damaged`) and the traffic speed limit (x1 / 0.85 / 0.55). **It does not change the material in the model path** (`WorldMaterials` has one `Asphalt` material; the `roadGood/Worn/Damaged` materials exist only in the legacy fallback path), so a `damaged` road looks like new asphalt that is slightly bumpy.

| # | Need | Status | What exists | What is missing |
|---|---|---|---|---|
| B1 | Modern asphalt | HAVE | `RoadSub.Asphalt`, lane markings, shoulders, concrete median; procedural 256 px grain texture | PBR asphalt with normal map; B/W kerb blocks (R-022); sidewalks |
| B2 | Older, deteriorated asphalt | PART | `worn` (1.4 cm undulation, traffic x0.85) | visible wear (faded markings, raveled surface, edge break-up, patches) |
| B3 | Patched and repaired | NONE | | patch layer (decal or geometry) |
| B4 | Cracked, broken, potholes | NONE | `RoadWear` documents "Potholes proper are NOT modelled in geometry (they need a decal/normal-map layer)" | R-016 shows 10 to 20 cm deep holes about 1 m wide with exposed earth. `damaged` undulation is smooth noise of about ±5 cm sampled every 2.5 m, so a 1 m hole cannot exist in it. Needs a visual layer **and** a decision on whether potholes are physical (see 5.3) |
| B5 | Compacted earth | NONE | `RoadSub.Verge` is a brown soil strip beside the paved road | a road whose carriageway is earth: new strip kind, material, markings off, camber and edge profile |
| B6 | Loose sand / dust | NONE | | material, drifting dust on the surface, dust kicked up by vehicles |
| B7 | Mud | NONE | | material with rut geometry (R-020 and R-021 show ruts 20 to 40 cm deep), water in the ruts, per-surface grip, sinkage |
| B8 | Rural road with potholes and drainage problems | NONE | bridges (parapets) only | culverts, roadside drains (R-016 shows an open concrete drain beside the kerb), erosion at the edge |
| B9 | Surface condition changes along a route | HAVE | per control point, `RoadRun` merges equal neighbours | more values (section 5.2) |

### 2.3 Rain and the rainy season
| # | Need | Status | What exists (all in `WeatherSystem`, 79 lines) | What is missing |
|---|---|---|---|---|
| C1 | Rain visible | HAVE | camera-following particle box, 40 m x 40 m, cap 6,000, emission 4,500 x rain per second, fall 22 m/s | rain streak quality; rain sound |
| C2 | Wet asphalt with reflections | PART | `Asphalt` and `Shoulder` materials: base colour x lerp(1, 0.55, wet), smoothness to 0.9. **Verge, terrain, props, kerbs, markings do not wet.** URP Lit with no reflection probe reflects only the sky environment (expected behaviour of the pipeline; **not verified in this build**), so the headlight and brake-light streaks of R-018 cannot appear | wet response on every surface class; a wet shader with a puddle mask; reflections that show lights (probe or fake) |
| C3 | Deep puddles | NONE | | puddle layer on the road (depth, edge, ripples) |
| C4 | Water accumulation, flooded sections | NONE | terrain has no water bodies | flood water plane per site, depth that the vehicle can query, splash |
| C5 | Water flowing along edges and drains | NONE | | runoff along the kerb, drain outlets |
| C6 | Traction loss | PART | `GripMultiplier = 1 - Wetness x rainGripPenalty` (0.35 default, 0.3 if no definition) applied to **forward and sideways stiffness of every wheel** of the **player's vehicle** only | grip that depends on **what is under the wheel** (asphalt, earth, mud, water depth), per wheel; traffic and other players unaffected (traffic agents are kinematic and take speed from zone and surface only) |
| C7 | Longer braking | PART (implicit) | brake torque is independent of weather; braking distance changes only because tyre friction stiffness changes | nothing was ever tested for wet braking; there is no ABS, so wheels lock at the asymptote of the friction curve. Needs a braking test before and after any change |
| C8 | Tyre splash | NONE | | splash particles per wheel on water; mud spray |
| C9 | Mud on the vehicle | HOOK | `VehicleRig.dirtyRenderers` ("body panels that receive road-grime tint"). **Nothing at run time writes to it** (only the Garage showroom collects them) | a dirt value per vehicle, driven by surface and distance, applied to those renderers |
| C10 | Reduced visibility | PART | fog is `ExponentialSquared`; density 0.00035 (clear) to 0.0011 (full overcast) | see arithmetic below: full overcast still gives about 1.8 km of visibility |
| C11 | Windscreen rain and working wipers | NONE | the Cockpit camera anchor exists; **docs/CAMERA.md: no windscreen, dashboard or interior exists** (Cockpit is "Limited"); `WeatherSystem` says "NOT implemented yet"; a comment reserves wiper keys | windscreen surface with droplet shader, wiper animation and input, an interior worth looking out of |
| C12 | Vehicles getting stuck | NONE | `WheelAxle.drive` per axle (so 4x2 and 4x4 are expressible) | sinkage or rolling resistance in soft ground; a recovery state; per-vehicle capability numbers |
| C13 | Different vehicles behave differently in mud and water | PART | `VehicleDefinition.rainGripPenalty` per model; `VehicleStats` mirrors the backend `stats` jsonb (extra fields are ignored by older clients) | mud capability (ground clearance, tyre type, drive axles, wading depth) as data |
| C14 | Rain actually starts | NONE | `target` toggled by F2 in Editor or debug builds; `GameBootstrap` creates the component; the Settings row "Weather System" is `Soon` ("Weather is not scheduled in the world yet") and `tools/webgl-e2e.js` asserts it has no control | a source of truth for weather (schedule, route data, server) |

Arithmetic on the fog values (exp-squared fog, remaining contrast f = exp(-(d x rho)^2); a conventional 2 % visibility limit gives d = 1.98 / rho):

| State | rho | Distance at 2 % contrast |
|---|---|---|
| clear (code value) | 0.00035 | about 5.7 km |
| full overcast, i.e. rain 0.9 and above (code value 0.0011) | 0.0011 | **about 1.8 km** |
| a haze to match the Harmattan reference, if its detail fades by about 400 m (an eyeball reading of a generated image, not data) | about 0.005 | about 400 m |
| heavy-rain visibility of about 300 to 500 m (my proposal, to be tuned) | about 0.004 to 0.007 | 300 to 500 m |

So the existing weather darkens the sky but does not shorten the view. Far clip is 900 / 1500 / 2200 m by tier, so for the Harmattan and heavy-rain cases fog, not far clip, has to do the work.

### 2.4 Seasons
| # | Need | Status | What exists | What is missing |
|---|---|---|---|---|
| D1 | A season concept | NONE | `LightingModel.Evaluate(hour, overcast)`: no date, no season | a season/weather state |
| D2 | Dry season and Harmattan atmosphere | PART | daytime fog colour is pale grey-blue with the comment "harmattan-like"; the reference (R-017) is orange-brown, dense, with a flat sun | a haze parameter in the lighting model (colour, density, sun attenuation) |
| D3 | Dry versus green vegetation | PART | two terrain materials chosen **by zone** (`TerrainDry` for urban/commercial/industrial, `TerrainLush` otherwise); one foliage colour for all trees | vegetation and ground that change **with the season**; dry and wet variants of trees, shrubs, grass, soil |
| D4 | Transitions between seasons | NONE | | blend parameter (the lighting model is already continuous in hour, so the same method works) |
| D5 | Applied consistently across the world | NONE | every client generates the world from data deterministically; positions are never stored on the server | a rule that makes season/weather the same for everyone on the same road, or an explicit decision that it is local |

### 2.5 Performance envelope that every addition must fit
`PerfBudget` Desktop: 1 M triangles, 400 draw calls, 300 MB textures, 120 MB build; Mobile: 250 k, 150, 150 MB, 80 MB. WebGL2 only, no threads, no compute. Rain, water, transparent glass and wet shaders add **fill cost** (measured only on a software renderer so far, so not usable as a GPU figure). A single 2k PBR texture set is about 5 MB compressed (`REALISM_AUDIT.md`), so an environment library with many unique sets will not fit; **shared kit texture sets, texture arrays and tiling surfaces** are required, not optional. Build size, load time, memory and frame time for the *existing* game are what E0 is to measure; none is measured yet.

## 3. What already supports the vision (and should be kept)
1. **`LightingModel` is pure, continuous and unit-tested** (`LightingModelTests`): hour and overcast in, sun, moon, sky, ambient, fog and exposure out. A haze/dust or season input can be added the same way, with tests, without touching rendering code.
2. **`WeatherSystem` already has the right shape**: one `rain` value, one `Wetness` with fast-wet/slow-dry behaviour, one `GripMultiplier` handed to the vehicle through a single property, materials restored on destroy. It is the natural place to *consume* a richer weather state; it does not need replacing.
3. **`VehicleController.GripMultiplier` is the single traction input** (applied to all wheels' stiffness only when it changes). A per-surface grip source can feed it without changing the controller's structure.
4. **Zone vocabulary and scatter rules are data** (`RouteSpec`): a traditional neighbourhood is *rules plus assets*, no new engine code, as long as buildings are free-standing or authored as rows.
5. **The road is generated from a profile**: kerbs, drains and earth strips are new strips in `RoadCrossSection` (`RoadSub` has seven slots; the Unity layer maps each to a material), which is additive.
6. **Per-run `Surface` along the route** already lets a road change condition from one control point to the next: new surface kinds extend an existing mechanism.
7. **`VehicleRig.dirtyRenderers`, `WheelAxle.drive`, `rainGripPenalty`, the Cockpit camera anchor** are the vehicle-side hooks for mud, drive type, wet grip and windscreen.
8. **Quality tiers and `PerfProbe`/`PerfBudget`** exist and are tested; every new effect gets a tier switch and a measured cost.
9. **The Blender kit and the validated asset pipeline** (14 assets, glass, normal maps, kit materials, review scene) already produce the content side; the registry already stores class, region and city.

## 4. What is missing, in priority order for the stated goal
The goal is that a player *feels* three different drives. In order of how much each contributes to feeling (my judgement, to be checked in play):
1. **Per-surface and per-condition traction** (physics): dry asphalt, wet asphalt, compacted earth, loose sand, mud, standing water. Without it all the visuals are a skin.
2. **Surface kinds and materials**: earth, sand, mud, broken asphalt, wet variants; with rut and edge geometry for mud.
3. **A weather and season state** that something sets (a schedule or route data) and that every system reads (sky, fog, surfaces, vegetation, rain, grip).
4. **Water**: puddles, flood sections, splash.
5. **Vehicle feedback**: splash, spray, mud on the body, windscreen and wipers (needs a cockpit worth looking from), suspension and audio response on rough surfaces.
6. **Authentic local architecture and settlement form**: assets and rules.
7. **Urban kit for the modern side**: kerbs, sidewalks, drains, planted medians, flyovers, gantries.

## 5. Impact assessment: what changing each system would touch
"Gate" is what must be true before the change merges. **No change in this table has been started.** "Needs approval" marks the ones that change behaviour shared by every route and every player.

### 5.1 Vehicle wheel friction (`VehicleController`, `TruckFactory`, `WeatherSystem`)
| | |
|---|---|
| Used by | the player vehicle in every route and job; the driving test (30 m in six game seconds after throttle press, 10 km/h tolerance); multiplayer only through the owner's own physics (puppets are kinematic) |
| Today | one friction curve per wheel (forward stiffness 1.6, sideways 1.8; extremum slip 0.4 / 0.25), one scalar multiplier, no ground query, no `PhysicMaterial`, no `WheelHit` use |
| Smallest additive change | a `GroundGrip` provider that returns a multiplier per wheel from `RouteModel.SurfaceAt(s)` (and later from a surface layer and standing water) and multiplies with the existing wetness factor; **default 1.0 for every existing surface, so existing routes behave as now**; per-wheel application needs `WheelHit` or a lookup by station, both read-only queries |
| Risk | MEDIUM: friction stiffness is not the same as a friction coefficient, so numbers need tuning against a braking test; per-wheel grip differences make a truck pull to one side (desired in mud, a regression risk on asphalt if the default is not exactly 1) |
| Gate | a braking-distance test and a lane-keeping test at 30/60/90 km/h on dry asphalt that are **identical before and after** (the same tolerance as the existing driving test); new tests for each surface multiplier |
| Needs approval | **YES** (changes physics for all players) |

For orientation only (friction-limited stopping distance on flat ground, ideal physics, no reaction time, d = v^2 / (2 mu g); mu are textbook ranges for the surface class, **not game values**; the game's tyre model will not match them and must be tuned in play):

| Surface | mu (literature range) | mu used here | Stop from 60 km/h (16.7 m/s) |
|---|---|---|---|
| dry asphalt | 0.7 to 0.9 | 0.8 | 17.7 m |
| wet asphalt | 0.4 to 0.6 | 0.5 | 28.3 m |
| compacted dry earth | 0.4 to 0.6 | 0.5 | 28.3 m |
| loose sand | 0.3 to 0.4 | 0.35 | 40.5 m |
| wet mud | 0.1 to 0.3 | 0.2 | 70.8 m |

The point: from asphalt to wet mud the friction-limited stopping distance is about four times longer. That ratio, not the exact numbers, is what the player must feel.

### 5.2 Road surface kinds (`Surface`, `RoadSub`, `RoadCrossSection`, `RoadGeometry`, `RoadWear`, `RouteSpec`, `RouteSpecValidator`, `ChunkStreamer`, `WorldMaterials`)
| | |
|---|---|
| Used by | every route; ~1,100 NetCore tests (`RoadGeometryTests`, `RoadProfileTests`, `RouteSpecTests`, `RoadSplineTests`, `TerrainTests`); the Unity world generation tests; traffic speed; the minimap |
| Today | `Surface { Good, Worn, Damaged }` parsed from route JSON by `RouteVocab.TrySurface`; strips `Asphalt, Shoulder, Verge, Concrete, MarkWhite, MarkYellow, Metal` (7); one material per strip in the model path |
| Smallest additive change | new surface values (`earth`, `sand`, `mud`, `broken`, `patched`) in `RouteVocab`; new `RoadSub` strips with materials appended **after** the existing seven (existing indices unchanged); the cross-section picks the strip for the lanes from the surface of the run; markings off on unpaved. Existing route files do not name them and so are unchanged |
| Risk | MEDIUM: the strip count appears in mesh building, collision-mesh selection (`ToCollisionMesh` lists strips) and material picking; a missed list silently makes a strip non-colliding or un-rendered. Mitigation: tests that every strip has a material and a collider decision |
| Gate | the E0 golden-master hash of the existing route's geometry is **unchanged**; new geometry tests; schema version gate (`schemaVersion`) so old clients reject, not misread, a route that uses new values |
| Needs approval | **YES** (extends the shared road engine; engine proposal E1) |

### 5.3 Potholes, patches, cracks
Two honest options, with different costs:
* **Visual only** (decal or normal-map layer on the existing asphalt): no physics, no change to the road mesh or collision; cheap; the player sees potholes and drives straight over them. This is what most of the reference shows, but it fails the owner's stated aim ("physically convincing") if left alone.
* **Physical hazards as data sites**: a pothole is a position with radius and depth in route data; when a wheel passes through it the vehicle gets a jolt (the vehicle already tracks `SuspensionJolt`) and a damage chance. No mesh change, no collision change, deterministic, the same on every client. Needs the "sites" idea of the engine proposal (RB-13) and is **additive**.
Raising road mesh density to put real holes in the collision mesh costs triangles and collision cost over every damaged kilometre; not recommended.

### 5.4 Weather state (`WeatherSystem`, `TimeOfDay`, `LightingModel`, `RenderingRig`, Settings)
| | |
|---|---|
| Used by | every scene (it is created in `GameBootstrap`); CI lighting studies read `[Light]` lines; the Settings model has the "Weather System" row as *Soon*, and the e2e test asserts no control for it |
| Smallest additive change | a **pure** `WeatherState` model in `NetCore` (`rain`, `wetness`, `standingWater`, `dust`, `season`, `haze`) with tests like `LightingModel`; `WeatherSystem` consumes it and keeps today's behaviour when the state is "dry, clear" (the default, so existing sessions are unchanged); the lighting model gains `haze` and `season` inputs with defaults that reproduce today's output exactly (a test pins that) |
| Risk | LOW to MEDIUM: lighting is verified by golden numbers in CI; a default-preserving signature change is testable; the risk is accidental change of the day-clear look |
| Gate | `LightingModelTests` unchanged and passing with the new parameters at default; new tests for the haze and season cases |
| Needs approval | **YES for the part that makes weather start** (a schedule or route-driven weather is a gameplay decision and decides what other players see); **no** for building the pure model behind a flag that stays off |

### 5.5 Multiplayer and determinism
Today every client builds the world from data and only the owner's vehicle state (about 5 bytes at 10 Hz) travels; positions are never stored on the server. Consequences for weather and season:
* If weather is **local cosmetic** (each client rolls its own), two drivers in one convoy can see sun and a flood. Acceptable only if weather affects nothing shared.
* Because rainy-season **physics** makes the road different, two clients must agree. The cleanest rule: **weather and season are a pure function of (route data, UTC time, a fixed seed)**, evaluated identically on every client, so no new network traffic is needed and nothing is stored. That is a design decision with game-design consequences (what is the rainy season in real days? can a player avoid it?), so it is **your decision** before any implementation.

### 5.6 Traffic (`TrafficManager`, `TrafficSim`)
Agents are kinematic and take their speed from zone and surface. For consistency they should slow on mud and in rain, which is a change to `LimitFor`, a pure static function with tests (`UnityLogicTests`). **Low risk, additive, but it changes traffic speeds on existing routes if applied to existing surfaces, so default factors for existing surfaces must be exactly 1.** Needs approval only for non-default behaviour.

### 5.7 Props, scatter and assets (`PropScatter`, `WorldPolicy`, `prop_catalog.json`, my Blender kit)
New production assets are new ids, so nothing existing is replaced. Rules that reference new ids need catalogue entries (the validator checks prop ids). Touching rows of houses, row shops and compounds that wrap a plot are not expressible as independent scatter instances: they are authored as **row or block assets** (a 20 to 40 m frontage as one model with its own LODs), which keeps the scatter unchanged. **No approval needed** beyond what the owner has already approved for asset work.

### 5.8 Cockpit, windscreen, wipers
The cockpit view has no windscreen, interior or mirrors (`docs/CAMERA.md`: P-05, P-93). Wipers need an interior, a glass surface and an input; they are a vehicle-asset task first, a shader and input second. Out of scope until vehicle assets for that exist; rain on the **camera lens or the cockpit view** should not be faked with a screen overlay without the owner's say.

### 5.9 Backend and data
`vehicle_definitions.stats` is jsonb; adding fields such as wading depth or mud capability is backward compatible (`JsonUtility.FromJsonOverwrite` ignores unknown fields; missing ones default). **No production data is touched by this assessment; any migration is the owner's decision.**

## 6. A staged route that never breaks the production game
Each stage is separate and needs its own approval where marked. Every behavioural stage ships behind a default that reproduces today's behaviour and is proved with the E0 golden-master and the existing tests.

| Stage | Content | Touches shared systems? | Approval |
|---|---|---|---|
| **S0** | E0 measurement (approved): size, load, memory, `[Perf]`, golden-master hash of the existing route | no | already given |
| **S1** | Register R-016 to R-023, extend the reference-to-asset matrix, expand the library taxonomy in `ARO-CONTENT` | no | not needed |
| **S2** | Blender content: traditional compound set, row shops and market stalls, mud-brick and rendered houses, dry and wet vegetation variants, kerb, drain, culvert, road-damage geometry and decal sources, water tank, umbrella, awning; registered `needs_concept` or higher | no | breakdown approval per family (workflow step 2) |
| **S3** | Environment **review scene** (`?smoke=env...`): materials for earth, sand, mud, wet and dry asphalt on ground tiles, the assets in dry and wet variants, in the real game build, with `[Perf]` | adds a review scene only (like `KanoReviewScene`) | no (additive; same pattern as the approved review scene) |
| **S4** | Pure `WeatherState` and haze/season inputs to `LightingModel` with tests, default-preserving, **off** | adds NetCore code; existing output pinned | design decision 5.5 first |
| **S5** | New surface values and strips (5.2) | **yes** | **explicit approval** |
| **S6** | Per-surface grip source and braking tests (5.1) | **yes** | **explicit approval** |
| **S7** | Weather schedule on real routes, rain gameplay, season switch | **yes** | **explicit approval** |
| **S8** | Splash, spray, mud on bodies, puddles, flood sites | adds effects behind tier switches | after S5 to S7 |
| **S9** | Windscreen and wipers | needs vehicle interior assets | later |

## 7. What must be decided by the owner before S4 and beyond
1. **Is weather/season shared (deterministic function of route data and clock) or local?** (5.5)
2. **Real-time or compressed seasons?** (a rainy season of months in real time means most players never see it; compressed seasons need a defined cycle)
3. **Is rainy-season physics mandatory on every route, or a per-route flag in route data?** (my recommendation: per-route data with Kano's rural and village roads first, so existing Lagos to Ibadan drives are not made harder by surprise)
4. **Potholes: visual only, or physical hazard sites?** (5.3)
5. **How hard should mud be?** Stuck vehicles are a gameplay promise (recovery, towing, 4x4 advantage); decide the failure mode before building the physics.
6. **Real brand and plate marks in the generated references** (a Toyota-like grille emblem, "PRADO", "MERCEDES-BENZ", a plate "KANO 982XF", shop and building names, a government coat of arms on "Kano State Tower"): reproduce, replace with fictional marks, or leave out. Environmental assets contain none of these unless you decide.
7. **Authenticity of the rural references** (R-020, R-021): red laterite mud, banana plants and green hills read as plausible for northern Nigeria in the rains, but I have no verified source that they describe rural Kano State; hills of that kind conflict with decision C3 (no dramatic hills in the urban landscape). I treat them as **northern Nigerian regional rural** until a source says otherwise, and do not label assets "Kano" on this basis.

## 8. Verification status of this document
* Read from the code: yes (files named in each section). Executed: **no**. Measured: **none**. No build, test or screenshot was produced for this assessment.
* The existing CI tests are the regression net for sections 5.1 to 5.6; none was weakened or changed.
* Open from the previous milestone and unchanged by this document: the real-asset Unity import, the six in-game screenshots and the E0 measurements wait for the two owner actions (allow `lfs.github.com`; set `UNITY_LICENSE`, `UNITY_EMAIL`, `UNITY_PASSWORD` in `ARO-CONTENT`).
