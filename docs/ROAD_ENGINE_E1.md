# Road engine stage E1: cross-section elements and pavement symbols

Built 2026-11-08 on the owner's instruction "start building them now, don't stop until finish all" (the plan: `docs/KANO_TRANSPORT_EXPANSION_PLAN.md` section 5.2, stage E1 of `docs/ROAD_ENGINE_PROPOSAL.md`). **Status vocabulary: implemented in code and unit-tested; in-game verification is a separate row below; nothing here is approved by the owner (he approves a stage after looking at in-game pictures).**

## What E1 adds (all optional, all additive)

| Element | Data (in a road profile, `RoadProfileSpec`) | Geometry | Notes |
|---|---|---|---|
| Kerb | `kerbHeight` 0.06 to 0.20 m, `kerbPaint` none or `black_white` | a **mountable** kerb (sloped 7 cm face, 15 cm top) at the outer edge of the paved shoulder; materials `Kerb`, `KerbPainted` (1 m black and white stripes) | not a vertical wall: wheels climb it |
| Walkway | `sidewalkWidth` 0 to 6 m (needs a kerb) | a level strip at kerb-top height beyond the kerb | `RoadSub.Sidewalk` |
| Gutter | `drainWidth` 0 to 1.2 m (needs a kerb) | a shallow concrete channel between the shoulder and the kerb | `RoadSub.Drain` |
| Planter median | `medianStyle` `planter` | soil and planting inside a kerb rim, sloped face down to the carriageway, **no barrier** | `RoadSub.Planter`; rim painted when `kerbPaint` is `black_white` |
| Painted median | `medianStyle` `painted` | a 10 cm paved island with the same rim | |
| Jersey median | `medianStyle` `jersey` (default) | exactly as before | |
| Dropped kerb | automatic | where a side road leaves, the kerb, gutter and walkway of that side fall to the paving level over one ring (4 m) and become paving | the mouth is a driveway, not a wall |
| Road classes | `roadClass` + `RoadClasses.Make` | the catalogue of six classes (expressway, arterial, collector, local, lane, service) as profiles | the validator knows the names |
| Pavement symbols | `RouteSpec.symbols[]` (`MarkingSymbolSpec`) | zebra crossing, stop line, yield triangles, straight, left and right arrows, parking and loading bay lines; painted white or yellow; anchored at a station of the main road or of a junction road | arrows follow the lane's flow (left-hand lanes point towards decreasing s); each symbol is built once by the chunk that holds its anchor |

A profile that sets none of the new fields builds **exactly** what it built before: the mesh keeps its seven submeshes, the golden master (`tests/NetCore.Tests/golden/golden_master.json`, three tests) is unchanged for the Lagos to Ibadan route and the Kano slice draft. The five new strips (`RoadSub.Kerb` 7 to `Drain` 11, `CountE1` 12) exist only in the mesh of a corridor that uses them (`RoadCorridor.NeedsE1Strips`).

Kerbs, walkways, planters and gutters are **solid road surface** (they are added to the road collision mesh); they carry no wear ripple on worn or damaged roads. Ground height (`RouteModel.GroundHeight`) and terrain conformity use the same cross-section polyline, so props, the kill-floor and the terrain follow the kerb and walkway by construction.

## Files

`NetCore/RoadCrossSection.cs` (strips, layout), `NetCore/RoadGeometry.cs` (dropped kerbs, symbols), `NetCore/RouteSpec.cs` (fields, `MarkingSymbolSpec`), `NetCore/RouteSpecValidator.cs` (ranges and impossible combinations), `NetCore/RoadClasses.cs`, `NetCore/RouteModel.cs`; Unity: `World/WorldMaterials.cs` (five materials, `ProcTex.Stripes`), `World/ChunkStreamer.cs` (collision slots), `Game/SmokeMode.cs` and `Game/GameBootstrap.cs` (`?smoke=drive&route=ng-kano-e1-streets`, smoke pages only).

The test corridor `Assets/_Project/Resources/Routes/ng-kano-e1-streets.route.json` (1.5 km: a 760 m arterial with a planter median, a collector street, a local street, a service road for logistics, three T-junctions, 17 pavement symbols) is an **engine test route**: not a survey, not registered in the world map, not reachable in the live game (only the smoke hook loads it).

## Tests (run on 2026-11-08)

| Check | Result |
|---|---|
| .NET unit tests, whole suite | **1,241 passed**, 0 failed (was 1,188; +53: `RoadE1Tests` 48, `KanoE1RouteTests` 5) |
| Golden master (existing routes unchanged) | 3 passed, hashes untouched |
| E1 corridor golden digest | `tests/NetCore.Tests/golden/e1_streets.sha256` (regenerate only for a deliberate change) |
| Unity edit-mode test for the E1 mesh conversion and the extra material slots | written; runs in CI (Unity cannot be compiled locally) |
| In-game (real WebGL build, software GL): `tools/webgl-e1.js` (8 places, day and night, chase and bumper views) and a drive-through (`webgl-drive.js` with `DRIVE_QUERY`) | wired into CI; **see the CI record below**; the pictures still have to be looked at |

## Known limits (honest list)

* **Vehicle behaviour over the kerb is not tested with a human driver.** The kerb is mountable (7 cm face for up to 20 cm height), the proposal's fallback (a flat collision height) is not needed unless the drive-through or the owner's test shows wheel trouble.
* A **junction on a divided road** is still refused by the validator (E3 changes that); the arterial of the test corridor therefore has no side roads. Zebra crossings are painted across the carriageway only (a refuge in the median is not built).
* The **bay** symbol draws separator lines across the paved shoulder; it does not widen the road. A lay-by wider than the profile's shoulder needs E2's site plates.
* Walkway, kerb and planter use development textures like every other road material; production PBR sets replace them without code changes.
* Street lights on a kerbed road stand behind the walkway (props are measured from the formation edge); a lamp on the median uses the existing `side: median` rule.
* Weather: the kerb and walkway materials are not in the wet set (the weather system is not changed); they do not darken in rain.
