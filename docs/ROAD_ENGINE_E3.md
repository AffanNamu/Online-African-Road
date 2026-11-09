# Road engine stage E3: junction forms (schemaVersion 2)

Built 2026-11-08 after E1 and E2, on the owner's instruction "start building them now, don't stop until finish all" (plan: `docs/KANO_TRANSPORT_EXPANSION_PLAN.md` section 5.2; `docs/ROAD_ENGINE_PROPOSAL.md` stage E3). **Implemented in code and unit-tested; the in-game check is wired into CI and not yet read; nothing is approved by the owner.** E3 is a **new route-data version**: `schemaVersion` 2. The validator accepts 1 and 2 and refuses anything else; under version 1 every rule is exactly as before and the new fields are refused, so no shipped route can change (the golden master proves it).

## What schemaVersion 2 allows

| Form | Data (`JunctionSpec`) | Result |
|---|---|---|
| **Four-way crossing** (RB-02) | `kind: "four_way"`, `profileLeft`, `lengthLeft` | two stubs at the same station on opposite sides: the stub named `id` (the side of the sign of `angleDeg`) and the opposite stub named **`id + "_left"`** (mirrored angle, own profile and length). Both interrupt the main road's edge line and, on kerbed roads, drop the kerb. Symbols, props and site plates can name either stub as their `corridor`. |
| **Junctions closer than 150 m** | none | the minimum spacing is 60 m, but the stubs must not lie on top of each other: the validator needs the two formation half widths plus two fillets plus 2 m between stubs on the same side. |
| **Junction on a divided road** (RB-11) | `medianOpening: true` | the median is cut across the junction: the strips of the median band become carriageway at carriageway level for the width of the widest stub, and the median-edge lines stop. Only a **planter or painted** median can be opened; a jersey (barrier) median is refused. A divided section without the flag is still refused. |
| **Flared corners (bellmouth)** (RB-02) | `fillet` 0..15 m | at each corner of the mouth a flat asphalt flare fills the region between the corner of the two paved edges and a circle of that radius tangent to both; the edge-line gap grows by the radius. For **unkerbed roads** and `|angleDeg|` within 10 degrees of 90 only (a kerbed junction mouth keeps its dropped kerb, a curved kerb with a walkway round the corner is not built). |

## Tests (2026-11-08)

| Check | Result |
|---|---|
| .NET unit tests, whole suite | **1,297 passed**, 0 failed (`JunctionFormsTests` 18 added; one existing test changed on purpose, see below) |
| Golden master (existing routes unchanged) | 3 passed, hashes untouched |
| What `JunctionFormsTests` proves | version gate (1 refuses the new fields, 2 accepts, 3 refused); four-way geometry, opposite stub profile and length, both edge-line gaps, referencing `id_left` from symbols, props and sites; spacing rule and the overlap rule; median-opening rules (version, flag, jersey, undivided) and the geometry (no planter or rim in the opening, carriageway level, faces up, mesh valid, painted median too); fillet polygon (tangent points, area = R squared times (1 - pi/4), within 3 to 10 percent for the 6-segment arc), asphalt added in the mesh (4 corners for a four-way, 2 for a T), gap growth, height on the road surface, 4 kinds of bad fillet refused |
| The existing test `RouteSpecTests ... schemaVersion = 2 is rejected` | **changed deliberately** to test version 99: version 2 is now a valid version (this is the stage the proposal calls "a new schemaVersion"); the test still proves that an unknown version is refused |
| Test corridor | `ng-kano-e1-streets` is now schemaVersion 2 and has `j_cross`: a four-way crossing (190 m south, 150 m north) on the arterial with a median opening and a zebra crossing on each cross-street stub |
| In-game (real WebGL build) | run 317 and the runs for `c63e523` and `2d48edf`: the corridor with the four-way crossing loads, all checks pass; **the median cut is visible in the frames at start 398 m** (the cross street runs straight across the planter median, the main road's kerbs dropped at the mouth; software GL, about 25 m ahead, level truck); the flare (fillet) is only unit-tested |

## Known limits

* No curved kerb or walkway round a junction corner: a kerbed junction keeps the E1 dropped-kerb mouth.
* The flare is flat and sits 1.2 cm above the paved edge; the terrain is not shaped round it (it is only visible where the terrain falls away from the verge).
* A junction opening is a gap in the median, not a turning lane: no lane geometry, no signals, and no traffic AI uses the crossing (traffic follows `Main` only).
* Stubs are still straight three-point lines (40 to 600 m): curved or branching side roads, and traffic or progress on them, need the road graph (E4).
