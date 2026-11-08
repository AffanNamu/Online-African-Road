# Day-and-night environment: technical implementation plan (street, vehicle and building lighting)

Milestone requested by the owner on 2026-11-08: "a proper day-and-night environment" for the actual gameplay world, not for Blender previews. This document is the plan that comes
before the implementation, as asked. Section 12 is the status table; it is updated as work lands and says what is verified in the running game and what is not.

Visual targets: the two night references the owner supplied (registered as **R-030** and **R-031**, kind `generated_night_reference`, design references only, nothing copied into the game and
nothing claimed as verified Kano). What they show, and what each item below answers:

| Observed in R-030 / R-031 | Answer in this plan |
|---|---|
| single-arm curved amber luminaires, hot core and orange halo, 25 to 30 m apart on both sides | `street_light` with an emissive head (`PropSub.Lamp`), a camera-facing halo, spacing per environment (section 4) |
| pools of warm light on road, sidewalk and walls | additive light-pool quads on the road surface (section 5.3) |
| long vertical amber reflections of the lamps on the wet road | wet-road streak quads, strength driven by `WeatherSystem.Wetness` read-only (section 5.3) |
| small warm lanterns on compound piers, lit windows, balcony light | `PropSub.Glow` emissive windows and porch lanterns on `concrete_house`, `apartment_block`, `shop_row` (section 7) |
| roadside stalls lit by bulbs under awnings | bulb strings on `market_stall` and `kiosk` (section 7) |
| red lights on the telecom mast | blinking `PropSub.Beacon` on `telecom_mast` (section 7) |
| oncoming headlights visible from far away | traffic vehicles get emissive head and tail lamps plus halos; the two nearest get real lights (section 8.3) |
| moon and cloud: shapes stay readable | already present: `TimeOfDay` moon light and the night ambient floor; unchanged |
| utility poles with wires | `utility_pole` already exists; no wires (not in scope) |

## 1. What was inspected (and what is reused)

Nothing below is rewritten. Every system listed is reused as is, or extended additively.

| System | File | What it already does | Used by this plan |
|---|---|---|---|
| Time of day | `Scripts/World/TimeOfDay.cs`, `NetCore/LightingModel.cs` | hour from `?hour=` URL, then the player setting, then the West Africa clock; `State : LightingState` from the pure `LightingModel.Evaluate(hour, overcast)`; sun and a moon light; `IsNight` / `NightChanged` (hour < 5.6 or > 18.4) | the single source of "how dark is it". The lamp ramp is a pure function of `LightingState.Elevation` and `OvercastIn`. **No second clock is created.** |
| Weather | `Scripts/World/WeatherSystem.cs` | `rain`, `Wetness`, writes `timeOfDay.overcast`, wets the road materials | read only: `Wetness` scales the reflection streaks, `overcast` (already in the state) moves the lamp ramp earlier. Weather is **not modified**. |
| Headlights | `Vehicles/VehicleLights.cs`, `HeadlightSpec`, `TruckFactory.AddPlaceholderLights` | spot lights (240 / 100 m / 55° / 25° / 1.5° down), emissive head, brake, reverse and indicator emitters, state from `VehicleController.LightsOn / BrakeLit / Reversing / Indicator`; key `L`; indicators `Q` / `E`; `GameBootstrap` links `tod.NightChanged` to `SetLights` | kept. Added: high beam and hazards on the same `Light` objects and emitters (section 8). |
| Vehicle network state | `Multiplayer/VehicleNetCodec.cs` | flags byte, bits 1 2 4 8 16 32 used | bits 64 and 128 (high beam, hazards) are free; older clients ignore them |
| Roadside props | `NetCore/WorldPolicy.cs` (`PropDef`, `PropScatter`), `NetCore/PropBatcher.cs`, `NetCore/DevPropGeometry.cs`, `Resources/Props/prop_catalog.json`, route `propRules` | data-driven, deterministic, one merged mesh per chunk, LOD and cull distance per prop, tier density; production prefab overrides by id | luminaires **are catalog props**; their placement is route data. No new placement system. |
| Chunk streaming | `World/ChunkStreamer.cs` (`StageProps`, `ClearProps`) | builds and drops the per-chunk prop mesh as the player moves | the hook where a chunk's lamps are registered and dropped |
| Quality tiers | `QualityTier` in `NetCore/WorldPolicy.cs`, `RenderingRig` | cost levers per tier, `[Render]` log | gets lamp budgets (real lights, glow range, wet effects) |
| Rendering | `SceneBuilder.EnsureUrp`, URP 17.0.3 defaults | Forward, additional lights per pixel, **per-object limit 4**, no additional-light shadows, gamma colour space, Neutral tonemapper, Bloom on High only | drives the main design decision in section 5.1 |

Gaps found: no street lighting of any kind (one `street_light` placeholder with a dark head, 48 to 56 m apart in every zone, the same everywhere); no emissive prop material; no lamp ramp (the
headlights follow `IsNight` only); no high beam or hazards; no lit buildings; traffic cars have no lights.

## 2. Design principles

1. One source of truth for time: `TimeOfDay`. One pure function turns its state into a lamp factor 0..1; everything lit follows that number.
2. Luminaires are props. Where they stand is route data (`propRules`, `props`), so a village road, a market street and an expressway differ by data and by tested rules, and a new route or city
   brings its own layout without code.
3. Cheap by construction, real lights by exception (section 5, 6). A lamp costs vertices in a mesh that already exists plus a share of one additive mesh. Only a handful of lamps near the
   player ever become real lights.
4. Do not touch physics, road generation, weather, Supabase or production configuration. The additions to `PropScatter` (a profile filter and a median position) are prop placement; the road
   meshes are untouched.
5. Everything new is switchable from the page address for tests (`?lamps=off|on|auto`, smoke pages only) and every number is logged (`[Lamp]` lines) so a screenshot can be explained by data.
6. Honest verification: unit tests prove the rules; only CI renders of the real WebGL build prove the look, and those are software GL at Low and Medium tier. The owner's own look decides.

## 3. Time-of-day integration

`NightLighting.LampFactor(LightingState)` (pure, tested): a smooth ramp on the sun's elevation (the sine used by `LightingModel`).
Fully off above elevation 0.16 (about 9 degrees up), fully on below -0.04 (about 2 degrees down); both thresholds move up by 0.10 times the overcast amount (a dark, rainy afternoon lights the
street earlier). With the game's model that is about 17:30 to 18:10 in the evening and the mirror image at dawn (the `sunset` preset 17.6 gives about 0.2, `night` 23 gives 1). It is
continuous in the hour, symmetric, and has no flicker or pop (tested over every minute and every overcast value).

Consumers: the lamp emission materials, the glow mesh, the real-light pool, the aviation beacons, the vehicles' automatic headlights (`LampsChanged` event with hysteresis, 0.5 on and 0.35 off,
replacing the `IsNight` link in `GameBootstrap` for **auto** switching only; the manual `L` toggle still works). `TimeOfDay` gets two additions: `LampFactor` (property) and `LampsChanged`.
`IsNight` and `NightChanged` stay as they are for any other consumer.

## 4. Layout per environment (route data, not code)

"Lighting density and arrangement should depend on the environment." The rules are `propRules` in `ng-lagos-ibadan.route.json`; they replace the single `urban_lights` rule (48 to 56 m,
presence 0.7, every zone). Two small additions to the prop model make them expressible: `PropRule.profiles` (comma separated road profile ids; empty = any) and `side: "median"` (on the centre line,
only where the profile has a median). Both are optional fields, so every existing rule behaves exactly as before.

| Environment | Where in the route data | Fixture (catalog id) | Arrangement | Spacing | Presence | Pole |
|---|---|---|---|---|---|---|
| Village / rural lane | `rural` zone, `rural_2lane` | `street_light_village` | sparse, either side, clusters at settlements | 70 to 110 m | 0.55 | 6.5 m, short bracket, small head |
| Residential street | `urban` zone, `urban_4lane` / `commercial_2lane` | `street_light` | both sides, opposite | 32 to 38 m | 0.9 | 9 m single curved arm |
| Commercial / market street | `commercial` zone | `street_light` | both sides, opposite, closer; shop fronts and stalls add their own light | 26 to 32 m | 0.95 | 9 m |
| Industrial / depot road | `industrial` zone, `urban_4lane` | `street_light` | both sides | 40 to 48 m | 0.85 | 9 m |
| Major city road (divided) | `urban_4lane_median` | `highway_light` | median, double arm | 38 to 44 m | 1.0 | 11 m, two heads |
| Expressway | `expressway_4lane_median`, `expressway_6lane_median`, highway zone | `highway_light` | median, double arm | 44 to 52 m | 1.0 | 12 m, two heads |
| Expressway through open country | same profiles, `rural` zone | `highway_light` | median | 70 to 90 m | 0.8 | 12 m |
| Junctions (3 T-junctions) | explicit `props` | `junction_mast` | opposite the side road, in line with the stem | one each | 1.0 | 18 m high mast, three heads |
| Bridges (2) | explicit `props` | `highway_light` | median, 12 m before and after the deck | 4 each | 1.0 | 12 m |
| Service station | `fuel_station` prop | (own geometry) | canopy downlights, forecourt, price sign | n/a | n/a | n/a |
| Bus stops, billboards, telecom masts | existing props | (own geometry) | shelter light, flood lamps, red aviation beacons | n/a | n/a | n/a |

Lamp colour is warm high-pressure-sodium amber (about 2200 K) for roads (R-030 / R-031); the expressway uses a slightly whiter head so a highway reads differently from a village road.
`tests/NetCore.Tests` gets a layout test per row: spacing and presence ordering (a village is sparser than a residential street, which is sparser than an expressway), the median rule only on divided
profiles, every referenced prop exists, and each environment gets lamps over a sampled stretch of the actual route.

## 5. How a lamp is drawn, and why it is not "a real light per lamp"

### 5.1 The constraint that decides the design
The project's URP asset is the default one: Forward, additional lights per pixel, **at most 4 per object** (8 possible), no shadows from additional lights. Chunk meshes are large (a chunk
is 200 m and its road, terrain and props are each one merged mesh), and Unity picks the per-object lights by the object, not by the pixel: a long road mesh would show only the few lights
Unity ranks first for the whole mesh, and lights would pop as the ranking changes. A real light per lamp (hundreds are loaded) would be both unaffordable and wrong. The look therefore
does **not** depend on real lights.

### 5.2 Four layers, cheapest first
1. **Emissive head.** A new prop sub-material `PropSub.Lamp` (bright, hot) and `PropSub.Glow` (windows, lanterns, bulbs: softer), and `PropSub.Beacon` (red). They are three shared materials whose
   emission colour is multiplied by the lamp factor once per frame. No per-lamp cost; the lamp mesh is part of the existing merged prop mesh.
2. **Glow mesh.** One additive, unlit mesh for **all loaded chunks** (a single draw call): a camera-facing halo around every lamp head (orange halo, hot core, as in R-030), a soft pool of light
   draped on the road under it, and the wet-road streak. Shader `ARO/LampGlow` (the same CG style as `ARO/SkyGradient`, which is proven in CI), with the usual defensive fallback: if the shader
   is missing or unsupported the glow layer is skipped and the log says so. The pools are the equivalent of baked light for a world that is generated at run time (lightmaps need UV2 and an
   offline bake; the chunks do not exist until the player gets there).
3. **Pooled real lights.** `LampSelector` (pure, tested) picks the nearest lamps in front of and around the player within a radius; a small pool of Spot lights (pointing down, no shadows) is
   moved onto them with a fade so a hand-over is invisible. Budget per tier: Low 0, Medium 4, High 8 (and URP's per-object limit is raised to match on Medium and High). These add light on
   vehicles and nearby props; they are an enhancement, not what makes the road read as lit.
4. **Distance policy.** Heads are part of the prop mesh, so they follow the prop LOD and cull distance (`street_light` 260 m times the tier cull scale). The glow layer is not culled by prop distance:
   at night the far lamps shrinking into the distance are what give depth (R-030), and a halo is four vertices.

### 5.3 Wet road
`WeatherSystem` already wets the road materials (darker, glossier). With the lamps lit, `Wetness` raises the streak layer (elongated amber reflections along the road under each lamp) and the pool
softness. There is no screen-space reflection in this pipeline; this is an approximation, flagged as such in R-030 / R-031 and in the docs.

## 6. Performance budget

| Item | Low | Medium | High |
|---|---|---|---|
| real lamp lights (pool size) | 0 | 4 | 8 |
| glow range (m) | 250 | 400 | 600 |
| glow draw calls | 1 | 1 | 1 |
| extra materials | 4 (lamp, glow, beacon, glow-mesh) | same | same |
| wet streaks | off | on | on |
| bloom on lamp heads | no | no | yes (existing High bloom; threshold 1.1) |

Expected cost: +1 draw call for the glow mesh; the lamp heads add triangles to existing prop meshes (a street light head is a few dozen); nothing new per frame except three
material colour writes, the selector (at 5 Hz, a few hundred distance checks) and the glow mesh rebuilt when the set of loaded chunks changes (about every 200 m of driving; a few thousand
vertices). The `PerfProbe` draw estimate and the `[Lamp]` log report all of it; the existing performance budgets stay the acceptance limits (desktop 400 draws, mobile 150).

## 7. Environmental lighting (buildings and public places)
All inside the existing dev prop geometry (production models replace them id by id, as for every placeholder):
* `concrete_house`: porch lantern and two lit windows (`Glow`); `apartment_block`: a lit-window pattern; `shop_row`: lit shop fronts and a sign glow; `market_stall` and `kiosk`: bulb string under the
  awning; `fuel_station`: canopy downlights and a lit price sign; `bus_stop`: shelter light; `billboard`: two flood lamps; `warehouse`: two security lights; `telecom_mast`: red beacons on the top
  and mid level (slow pulse, `Beacon`). Lit windows are a fixed share of the windows, so a street is not a wall of light.
* Light is a function of the same lamp factor, so shops and stalls come on with the street lights. (Shops that stay open late and houses with dark windows: a per-instance hash can vary this
  later without code changes to the system.)

## 8. Vehicle lighting
### 8.1 Player vehicle (reuses `VehicleLights`)
* Low beam: today's `HeadlightSpec`. **High beam** (`K`): the same two `Light` objects with a narrower, longer, brighter spec (`HeadlightSpec.High`: intensity 420, range 170, outer 38°, inner 14°,
  pitch 0.3°). No extra lights.
* Rear lights: red tail lights on with the headlights, brighter on the brake (exists). Reverse lamps (exists). Indicators (exist). **Hazards** (`T`): both indicators together.
* Automatic: lights come on at dusk and go off in daylight from `LampsChanged` (section 3); a manual `L` choice is respected until the next auto transition, as today.
* State: new `HighBeam` and `Hazard` properties on `VehicleController`, `SetHighBeam`, `ToggleHazard`; input in `KeyboardVehicleInput` (K and T are the keys `docs/CAMERA.md` already reserves for them).
  HUD: a small lights indicator (dipped, high, hazard) on the existing HUD.
### 8.2 Remote players
`VehicleNetCodec` flags bit 64 = high beam, bit 128 = hazards. `ApplyRemoteState` sets them; a client without them simply ignores the bits. Codec tests cover the round trip and old payloads.
### 8.3 Traffic
Ambient traffic cars get emissive head and tail lamps and halos in the glow layer (cheap, visible from far, as the oncoming lights in R-031) and a real light only for the two nearest cars,
through the same pool. `TrafficManager` is otherwise unchanged.

## 9. Test and verification plan
Unit (xunit, `tests/NetCore.Tests`, run here): lamp factor (range, monotone, symmetric, continuous, overcast shift), `LampSelector` (budget, nearest-first, hysteresis, stability), catalog light specs
and validator, `PropScatter` profile filter and median side (existing rules unchanged), layout tests per environment against the real route data, lamp-head placement maths, glow-mesh geometry
(counts, finite values, indices), vehicle light state (auto, high beam, hazard priority), net codec flags. The Blender/Python suite and the existing Unity edit-mode tests must stay green.

In the real game (GitHub Actions only; Unity is never run here): `?smoke=drive&hold=1&hour=night&start=<metres>` (a smoke-only start distance, new) at a village stretch, a residential street, a
commercial street, a city divided road, an expressway, a junction and a bridge; screenshots (chase and bumper view) for night, dusk and day, lamps `on` and `off`, low and high beam, dry and wet;
the `[Lamp]` lines (lamps loaded, real lights active, glow vertices, factor, shader status), `[Light]` and `[Perf]` lines; luminance thresholds on the night road (lamps on must lift the road
and ground bands over lamps off); the day view must show no glow and the same frame as before within tolerance. The pictures are then looked at by hand; passing numbers do not mean
it looks right.

## 10. Risks and unknowns (stated, not hidden)
* Nothing can be compiled or run here: Unity compiles in CI. Each cycle is about 45 minutes; the plan expects two to three.
* CI renders on software GL. Frame times are meaningless; the look is judged from screenshots, and bloom/real-GPU appearance is unproven.
* Per-object light selection on large merged meshes is why real lights are an enhancement; the A/B screenshots (`lamplights=off|on`) decide the default for each tier.
* The wet-road streak is an approximation, not a reflection.
* `PropSub.Count` grows from 8 to 11; every user of the count (batcher, materials, validators, tests) is covered by the test suite.
* Lamp colours are estimates from two generated references; nothing about real Kano street lighting (lamp type, spacing, outage patterns) is established.

## 11. Out of scope / needs separate approval
Driving physics, road generation, weather simulation (including rain/flood/mud physics), Supabase, production configuration, engine batching (RB-15), real lightmap baking, production
luminaire models (placeholders now, same ids later), the Kano review scene (the Blender kit has no luminaires; a night review scene is a later step), power-cut gameplay.
The asset library and every frozen version are untouched; this milestone adds code, catalog entries and route rules only.

## 12. Status (updated as work lands)
| Step | State |
|---|---|
| Inspection of lighting, time of day, weather, vehicles, props, URP | done |
| References registered (R-030, R-031) | done |
| Technical plan (this document) | done |
| Pure models and tests (lamp factor, light specs, selector, scatter additions, vehicle state, codec) | planned |
| Catalog, dev geometry, materials, route rules | planned |
| Runtime: emissive materials, glow mesh and shader, light pool, beacons, tiers, URL hooks | planned |
| Vehicle lighting: high beam, hazards, auto, HUD, net flags, traffic lamps | planned |
| In-game verification in CI, pictures looked at | planned |
| Owner's visual review | not done; nothing is approved |
