# Road engine stage E2: site plates, text faces, side-road props and prefab colliders

Built 2026-11-08 after E1, on the owner's instruction "start building them now, don't stop until finish all" (plan: `docs/KANO_TRANSPORT_EXPANSION_PLAN.md` section 5.2, stage E2 of `docs/ROAD_ENGINE_PROPOSAL.md`). **Implemented in code and unit-tested; in-game verification is wired into CI and not yet read; nothing is approved by the owner.**

## What E2 adds

| Piece | Data | What the game does |
|---|---|---|
| **Site plate** (RB-13) | `RouteSpec.sites[]` (`SitePlateSpec`): corridor + `s` + `lateral` + `forward` (where it stands), `widthM`, `depthM`, `yawDeg`, `surface` (asphalt, concrete, paving, gravel), `kerb` (none, plain, black_white) and `kerbHeight`, `heightOffset`, `entrances[]` (kerb gaps), `marks[]`, `props[]`, `dropRoadKerb` | a flat paved rectangle: surface cells of 6 m, a mountable kerb ring (E1 profile) with gaps, a **mesh collider** like the roads, the terrain flattened under it (sunk 0.35 m below the surface, like a road) and blended round it from the top of the kerb, `GroundHeight` and the prop exclusion know it, the terrain tile is cut where the plate covers it. The plate's height follows the paved edge of the corridor it is placed from. |
| **Pavement marks on a plate** (RB-12) | `marks[]` in plate coordinates: `line`, `bay_row` (count, bay width and length, optional back line), `hatch` (45 degree keep-clear stripes), `arrow_straight/left/right`; `angleDeg`, `yellow` | painted quads in the white or yellow marking submesh, built by the chunk that holds the anchor |
| **Props on a plate** | `props[]` in plate coordinates (`SitePropSpec`) | normal prop instances standing on the plate, so lamps, windows and beacons of the night system work on them unchanged |
| **Props from a side road** | `PropPlacementSpec.corridor` (a junction id) | explicit props measured along a side road; previously only the main road could place props, and nothing beyond 130 m of it was ever furnished |
| **Text faces** (RB-07) | `PropDef.label` (`LabelSpec`: x, y, z, width, height, yaw, colour, fontMax) | any catalogued prop, dev or production, can show the text of its placement (`PropLabelLayout`); the four placeholder faces (road sign, billboard, fuel station, bus stop) keep their old numbers as built-in defaults |
| **Prefab colliders** (RB-10) | `PropDef.collider = "box"` | a production prefab without a collider of its own gets a box of its catalogue size within 2 rings of the player (dev placeholders already had this) |
| **Dropped road kerb** | automatic with `dropRoadKerb` | a plate beside a kerbed road drops that road's kerb along the plate and interrupts the edge line |

Everything is optional. A route without `sites` builds exactly what it built before: `tests/NetCore.Tests/golden/golden_master.json` (Lagos to Ibadan and the Kano slice draft) is unchanged. One legacy behaviour is deliberately kept for those routes and switched to the exact rule only when a route declares sites: the terrain "cap" beyond the end of a road (straight beyond a road end the sideways offset is about zero, so the ground was treated as road formation for roughly 80 m). With a yard at the end of a stub that cap would have surrounded the yard with a flat extension of the road.

Model-level checks (`RouteModel.CheckSites`, run at build): plates may not overlap each other and may not lie on a road (the road they are placed from may touch them at its end). The validator checks all ranges, corridor names, kerb and entrance data, mark kinds and that every plate prop is a catalogued id standing inside the plate.

## The test corridor now holds a logistics yard and a bus terminal

`Assets/_Project/Resources/Routes/ng-kano-e1-streets.route.json` (an engine test route; see `ROAD_ENGINE_E1.md`):

* **`site_yard`**: 80 x 100 m concrete yard at the end of the service road `j_service`: kerb with an 18 m entrance, stop line, lane arrows, two warehouses (placeholder `warehouse`, collision boxes), 2 x 8 loading bays, yellow keep-clear hatch, dock line, gate sign "LOGISTICS YARD GATE 1", an office kiosk, six street lights and a junction mast.
* **`site_terminal`**: 90 x 60 m concrete apron with a black and white kerb at the end of the new collector `j_terminal`: stop line, entry arrows, twelve bus bays, four shelters ("BAY 1-3" and so on), two ticket kiosks, three market stalls, a name board "KANO NORTH BUS TERMINAL", six lights, two trees.

These use the **placeholder dev props** of the game (grey boxes). They are layout and engine proof, not art: the real Kano warehouse, terminal and street-furniture assets are still to be built in Blender and registered in ARO-CONTENT, and none of this is a gameplay change (no job or bus route points at them).

## Tests (2026-11-08)

| Check | Result |
|---|---|
| .NET unit tests, whole suite | **1,279 passed**, 0 failed (E1: 1,241; E2 adds `SitePlateTests` 37 and 1 corridor test, plus the extended corridor checks) |
| Golden master (existing routes unchanged) | 3 passed, hashes untouched |
| E1/E2 corridor digest (road, plates, props, terrain) | `golden/e1_streets.sha256` |
| What `SitePlateTests` proves | placement and heading, local/world round trip, signed distance, yaw and height offset, terrain flattening and blending, ground height, exclusion, cell and kerb ownership by chunk (areas add up), entrance gaps, painted kerb, bay/hatch/arrow/line marks, props in the right chunk and far from the main road, dropped road kerb, validation of 15 kinds of bad plate, overlap and on-road refusal, text face defaults and catalogue labels |
| Furnishing of the two sites | every solid footprint lies inside its plate and no two overlap (`KanoE1RouteTests`) |
| In-game (real WebGL build, software GL) | `tools/webgl-e1.js` now has 14 places incl. the yard and the terminal (chase + overhead/bumper, day + night); `?smoke=drive&route=ng-kano-e1-streets&site=site_yard` starts the truck on a plate; CI lighting job; **not yet read** |

## Known limits

* Plates are **rectangles**, and two plates may not touch or overlap, so an L-shaped yard is not possible yet (use one larger plate).
* A plate is **flat** (no fall for drainage); the terrain is cut and blended round it, not graded.
* No fences, gates, walls or buildings are generated: those are props and need assets.
* The vehicle driving over the plate's collider and the kerb is covered by the CI drive scenario only for the road part; driving into the yard has not been tested by a person.
* Production-prop colliders by catalogue size are untested in the game (no production prop is in the public catalogue).
