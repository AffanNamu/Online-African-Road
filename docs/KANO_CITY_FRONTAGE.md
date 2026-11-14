# Kano city frontage: the placement engine, the two city test routes and what is proven (2026-11-09)

Written for the owner's directive "ARO: Kano city densification and mosque integration". The audit that preceded this is `KANO_CITY_DENSIFICATION_AUDIT.md`; the assets are in `TRANSPORT_KIT.md` (families E and F). **Nothing here is visually approved; nothing is a production Kano network.** Levels used below: 1 implemented, 2 automated tests, 3 built and run in Unity, 4 verified in gameplay, 5 visually reviewed.

## 1. What was missing and what was added

The roadside scatter (`PropRule`) places independent points: no footprint test (a 40 m mosque whose origin is 6 m from the road had its façade on the carriageway), one design per rule, no lots, main corridor only, no anchors, slope ignored. A city needs the opposite, so a second system was added **beside** it (the scatter is unchanged, byte for byte, for every route that declares no `frontage`):

* `Assets/_Project/Scripts/NetCore/Frontage.cs` (pure C#, no Unity): `FrontageRule`, `FrontageLot`, `FrontagePlacer`, `FrontageValidator`.
* `RouteSpec.frontage` (schemaVersion 3), called from `PropScatter.PlaceChunk` for the chunk being streamed.
* Test data: two routes (section 4) and 25 catalogue entries (section 5).

## 2. How a lot is decided (all of it deterministic)

| Step | Rule |
|---|---|
| Blocks | Along a corridor (the main road, a junction stub, a road or a ring) the road is cut into blocks of `blockLength` metres from `sFrom`. **A block is the unit of determinism**: a chunk computes exactly the blocks it touches, so the result never depends on which chunks are loaded or in which order. The only source of choice is `Mathx.Hash01(route seed, rule, block, row, slot, purpose)`: no state, no random numbers, no clock. The same route data gives the same buildings on every client (multiplayer agrees without sending anything). |
| Lots | From the block start, lots are laid one after another along the road: a design is picked from the rule's **weighted mix** (never the same design twice side by side when the mix has a choice), its footprint (width along the road, depth away from it) is set back from the road formation by `setbackMin..setbackMax` (a major road 10 to 15 m, a collector 4.5 to 8 m, a local street 3.5 to 6 m, a service road 8 to 14 m), turned to face the road with a small yaw jitter, and separated from its neighbour by `gapMin..gapMax`. `presence` leaves some plots empty. `rows` (1 to 4) adds rows behind the first, separated by a back lane (`rowGap`), so a city is deeper than one lot. |
| Rejection | A lot is **rejected** if any sample of its footprint (a 5 m grid including the edges) lies within `clearance` metres of **any** road formation, site plate or island of the route (not only its own road: side roads, the E5 ring, ramps), if the ground under it varies by more than `maxHeightRange`, if the road beside it is on a **valley** bridge (beside a flyover **deck** there is open ground and the city is built there, but never under or on the deck), or if it would overlap a lot already placed in the block (separating-axis test). The lot stands on the lowest terrain sample (it sinks into a slope, it never floats). |
| Anchors | `anchors` + `anchorEveryBlocks` N: every N-th block on each side starts with a civic lot (the mosques), never thinned by the quality tier (`density` thins ordinary lots only). An anchor rejected at its first position is retried with the other anchor designs and then every 8 m along the block before it is given up. `anchorPhase` lets the designer choose the phase; the right side is staggered by half a period. |
| Validator | `RouteSpecValidator` now checks every rule (known props, weights, footprints 3 to 80 m, `blockLength` holds the widest lot, setbacks at least `clearance` + 0.5 m, anchors and `anchorEveryBlocks` together, corridor ids, profiles, zones) and **refuses two rules that share a side and a stretch of the same corridor** (their lots could overlap): separate them by corridor, side, profile, zone or station range. |

Performance: all lots of a 1.5 to 1.8 km route are computed in about 0.1 s; one chunk in a few milliseconds; instances use the existing streaming, LODs by ring and cull distances (catalogue `cullDistance` 220 to 600 m by class), and the quality tier's prop density.

## 3. Tests (level 2)

`tests/NetCore.Tests/FrontageTests.cs`: **38 tests, the whole suite is 1,406 and passes** (it was 1,368; the golden masters of the Lagos to Ibadan route, E1 and E5 are unchanged). Synthetic roads: same data same buildings, a different seed a different city, chunks agree with the whole route and with each other in any order, no overlap, every footprint stays back from the road by the setback and clearance, every lot faces the road, sides honoured, weights followed (3:1 measured on 8 km), presence and density thinning, anchors keep their spacing and are never thinned, designer phase, rows, hillsides are not built on, valley bridges are skipped, 15 validator refusals. Both city routes: validator passes with the shipped catalogue, every lot's footprint equals its catalogue entry, **no lot overlaps any other across all rules and corridors**, no footprint corner or front edge is on a road, plate or island and the ground is level, chunks agree with the whole route on the real data, a varied mix (at least four designs, none above 40%, never more than three identical in a row, at least eight kinds of building), a mosque at regular intervals on every road class, no stretch of the main road longer than 90 m without a building, the F1 flyover looks over buildings on both sides with a mosque within 400 m, and a **golden digest of every lot** so a later change is deliberate.

## 4. The two city test routes (engine test routes, not districts)

Generated by `tools/kano/make_city_routes.py` from the E1 corridor and the F1 interchange (the originals and their golden digests are untouched). Reached only by `?smoke=drive&route=ng-kano-city-e1|ng-kano-city-f1` (and the CI checks); `?corridor=<id>&start=<m>` measures the start along a side street and `&facing=back` faces the other way (new smoke options, smoke pages only).

| Route and rule | Corridor | Lots | Mosques | Mix |
|---|---|---|---|---|
| **ng-kano-city-e1** `arterial_major` (divided, planter median) | main, 0 to 760 m | 78 | 6 | villa 20, apartments 20, shopping parade 18, office 14, grand mosque 5, medium 1 |
| `collector_main` (single carriageway, kerbs, walkways) | main, 760 to 1500 m | 51 | 5 | shop row 16, Hausa compound 8, parade 7, market shed 6, villa 6, apartments 3, mosques 5 |
| `cross_street_right` / `_left` | j_cross, j_cross_left | 7 + 4 | 2 + 1 | shops, apartments, villa, small mosques |
| `local_street` (residential) | j_local | 12 | 2 | house 4, villa 3, Hausa compound 3, small mosques 2 |
| `service_road` | j_service | 4 | 0 | warehouses |
| `collector_cross_2`, `terminal_access` | j_collector, j_terminal | 9 + 5 | 2 + 0 | shops, sheds, villa, mosques |
| **total** | 1,501 m | **170** | **18** | up to 25 designs of 14 kinds |
| **ng-kano-city-f1** `flank_urban` (3 rows each side of the expressway) | main, urban zone | 105 | 5 | villa 33, apartments 27, parade 17, office 16, Hausa compound 7, mosques 5 (since 2026-11-13; was villa 31, apartments 29, parade 16, office 15, Hausa compound 9) |
| `flank_highway` | main, highway zone | 39 | 4 | house 13, Hausa compound 13, villa 9, mosques 4 |
| `ring_edge` | r_highway | 1 | 0 | villa |
| **total** | 1,800 m | **145** | **9** | |

Gaps measured by the tests on E1: a mosque at least every 420 m on each side of the arterial and every 520 m on each side of the collector; the grand mosques stand 10 m or more from the carriageway (R-037); at least 20 buildings on each side beside the F1 deck (asserted). The unavoidable emptiness is real ground: embankments (the terrain test refuses slopes), the ramps, the logistics yard and the bus terminal plates.

**What these routes show, honestly.** The roads are the engine's test corridors. The buildings are the assets of the private library (residential kit v3, environmental kit, the transport kit's families E and F) placed by the engine. **Families the library does not have are not faked**: hotels, schools, hospitals, police stations, government buildings, restaurants, car dealerships, workshops and a petrol-station-with-shop (hospital and police have approved concepts R-010 and R-011 but no model; the rest have no concept). They are reported as gaps; the mix uses villas, apartment blocks, offices, parades, shop rows, market sheds, Hausa compounds, houses, warehouses and mosques. Building heights top out at a seven-storey office block; no tower with a lantern crown was invented (a possible landmark, not built).

**2026-11-13 (realism pass, `ASSET_REALISM_PASS.md`).** The v002 mosques and city buildings have new footprints: the mosque compounds are 1.1 to 2.8 m deeper. The catalogue entries and both routes were regenerated from them, and the golden digests of the city lots changed deliberately (old values are in the test's comment). Measured again:
* the totals are unchanged: E1 has 170 lots and 18 mosques, F1 has 145 lots and 9 mosques;
* every E1 rule and the other F1 rules keep their counts and mix;
* only F1 `flank_urban`'s mix moved (table above);
* every structural test still passes on the new footprints.

**2026-11-14 (city construction, `KANO_CITY_CONSTRUCTION.md`).** The mixes of section 4 have been replaced by district mixes with the 28 new buildings, and the F1 highway flank was split: the left side is the edge of town, the right side a civic and premium edge in two stretches clear of the urban rows. The current counts are in `KANO_CITY_CONSTRUCTION.md` section 3; the table of section 4 is the state of 2026-11-09.

## 5. Catalogue and prefabs

`prop_catalog.json` gained 25 entries (text data, all `dev` placeholders of the real footprint: **additions only, no existing entry changed**, nothing in the Lagos to Ibadan route uses them). In the shipped build, which has no private prefab, they draw as plain volumes (`DevPropGeometry.CityBuilding`: one box, a roof slab, a door and windows, no lamp geometry); in the review build the production prefab with the same id replaces the placeholder. The review scenes `env_kano_mosques_dry` and `env_kano_city_row_dry` carry all 14 new assets so that the review build makes prefabs for them.

## 6. Verification state (what is true today)

| Level | Result |
|---|---|
| 1 and 2 | Engine, validator, routes, catalogue entries, smoke options, review wiring: yes. 1,406 .NET tests, the whole Blender suite (384 tests), the private repository's validator and 52 tests pass. |
| 3 Unity | **Yes.** Review run 42684305591 (workflow_dispatch on `main`, commit 78509ca): all 78 transport-kit assets built from the code and validated, the game built in Unity 6 with them, the 24 scene views taken, and `tools/webgl-kanocity.js` drove the two city routes at eight places: **35 of 35 checks passed**. The full CI on 78509ca (run 42671326212, a re-run of a run GitHub had failed with no jobs) is **green on all 8 jobs**: 1,406 .NET tests, Unity tests and WebGL build, the browser boot, the four-hour lighting job, Blender, database, backend, safety gate. Failures met on the way, all separated: (code, mine, fixed) one Unity EditMode test pinned the catalogue at 19 entries; the flyover places were taken under the deck, twice (hold timed out before the deck's collider existed; the wide place started over the ring); (GitHub infrastructure, not code) run 42644510896's lighting job failed only at "publish the screenshots to the ci-lighting branch" (`remote: fatal error in commit_refs`, after the measurements passed), run 42670986113 lost its browser-boot job and could not be retried (403), and one branch run failed at start-up with no jobs. |
| 4 gameplay | **Measured in the real build, not seen.** In run 42684305591 the cab stood at y = 17.87, 17.88 and 17.88 m (the deck surface is 17.8 m) at the three flyover places, north, south and the wide view, released 1.0 to 2.4 s after the deck's collider existed. The game's own `[Perf]` lines, tier **low, SwiftShader on a CI CPU, not a real GPU** (frame times of 230 to 365 ms are the software renderer, not a player's frame rate): single carriageway 30,291 visible triangles / 158 estimated draws / 82 renderers; commercial road 34,093 / 197 / 85; residential street 27,069 / 120 / 57; arterial with a grand mosque 36,235 / 178 / 90; urban density 35,280 / 122 / 47; flyover north 54,983 / 218 / 69; flyover south 54,130 / 212 / 65; wide flyover view 48,058 / 178 / 55; 63 to 163 props loaded. The 150-draw mobile budget is exceeded on the busier views (it was already exceeded by the kit alone, `PERFORMANCE_REPORT_V003.md`), the 400-draw desktop budget is not. **The pictures of these runs (24 scene views and the city sheet) are unread: the artifact's storage host (`productionresultssaN.blob.core.windows.net`, a different N every run) is not on the environment's allow-list and the request to add it could not be shown on the owner's app, so no frame of the game has been looked at by anyone.** The artifact `kano-review-in-game` of run 42684305591 holds `kanocity-sheet.png` and `kanocity-report.json` (district, road and camera of every frame) for seven days. |
| 5 visual | **No.** Agent reading only; the owner decides. |

## 7. Not claimed

Visual approval; a production Kano network (these are test routes: connecting any of it to a production route is a separate decision under the E5 safeguard); the real Kano Central Mosque or any real landmark (the mosques are generic designs; D02 is real-anchored and not modelled); that the density is final; performance on a real GPU; multiplayer synchronisation beyond determinism (the same seed gives the same placement on every client: tested, not sent over a network); the missing building families above.
