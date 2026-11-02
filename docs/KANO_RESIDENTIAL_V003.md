# Residential kit v003 (2026-11-03): what changed, how it was checked, what is and is not established

**Status: Blender validation passed for all 14 assets; frozen with its own manifest; imported into Unity and measured in the running game in run [41476407715](https://github.com/AffanNamu/Online-African-Road/actions/runs/41476407715) (section 6). NOT approved: neither v002 nor v003 is `production_approved`, and neither will be marked so until the owner has reviewed the actual in-game screenshots.** No live gameplay, Supabase configuration, driving physics, weather system, vehicle behaviour or production configuration was touched.

## 1. v002 stays exactly as it was
v002 is the frozen original review candidate. Its manifests (`docs/kano/KANO_RESIDENTIAL_v002.*`), its asset hashes and its files in the private repository are not modified; v003 is a **new version** of the same asset ids (the workspace is append-only: v001, v002, v003). The v002 builders (`kano_kit/build_all.py` and the modules it uses) are not changed: v003 lives in new modules (`villa3`, `palette`, `veg3`, `build3`) and a test (`test_the_frozen_v002_builders_still_produce_the_recorded_geometry`) holds a geometry hash of every v002 LOD, so a change to a frozen builder fails before it can change a frozen export. A fresh rebuild after all of this work still reproduces the v002 freeze byte for byte (42 of 42 textures, 14 of 14 GLB files).

## 2. Scope, as approved by the owner (2026-11-03)
| Approved item | Done |
|---|---|
| Villa facade improvements towards the approved R-006 | yes, both villa types (section 3) |
| Villa material reduction and draw-call optimisation | yes: villas 11 and 10 materials down to 7; trees, hedges and shrubs down to one material each; shared palette materials for the flat colours (section 4) |
| Correction of overlapping geometry and coplanar faces | yes: none left in any LOD of any v003 asset (section 5) |
| Regression tests so the defects cannot return | yes (section 7) |
| Do not simplify the villas excessively | the triangle counts went DOWN (A 5,454 to 4,802, B 4,808 to 4,289) only because faces that cannot be seen were removed; every visible feature of v002 is still there and features were added (section 3) |

## 3. Villa facades
Added to both villa types (same massing, openings and footprints as v002, so the street layout is unchanged):
* **Villa A:** a stone-clad entrance bay with a **round-arched fanlight over the double door** (real arch geometry: spandrels in the wall plane, a glass fan, an arc reveal, a stone arch ring and a keystone), **round columns with bases and capitals** under the balcony instead of square ones, a stepped cornice under the balcony slab, stone pilasters flanking the entrance bay, **proud grey cladding panels with joints** round the right-hand windows on both floors, **pediments** over the ground-floor windows, two **wall lanterns** beside the door and a **warmly lit** ground-floor window.
* **Villa B:** an **arched fanlight** over the stone-clad entrance, stone pilasters at the corners of the projecting bay, an **oculus** in the front gable, a pediment, **quoins** on the main block, **cladding panels with joints** beside the garage door, wall lanterns, a warmly lit first-floor bay window.
* Unchanged from v002 and kept: hipped tiled roofs, dormer (A), chimney, balcony glass balustrade, window frames, architraves, keystones, security grilles on ground windows, quoins (A), cornice, downpipes, garage roller shutter (B).
The triangles saved without losing anything visible: window frame bars, grille bars and other slender parts are now built with only their visible faces (a bar set into a reveal shows its front and its inner edge, not five faces).
**What is still different from R-006** (the concept shows more landscaping and ornament than a single model can carry, simpler roof and glazing here, no interior light sources): recorded in the registry flags; the owner's view of the in-game screenshots decides whether this is enough.

## 4. Materials (the count of materials of an asset is the number of draw calls one instance costs: the game batches nothing)
| Asset | Materials v002 | v003 | Triangles v002 (LOD0/1/2) | v003 |
|---|---|---|---|---|
| `kano_street_oneway_section_a` | 7 | 5 | 1518 / 38 | 1518 / 38 |
| `aro_villa_2storey_hip_a` | 11 | 7 | 5454 / 824 / 58 | 4802 / 965 / 58 |
| `aro_villa_2storey_hip_b` | 10 | 7 | 4808 / 774 / 70 | 4289 / 803 / 70 |
| `aro_wall_compound_panel_a` | 2 | 2 | 50 | 50 |
| `aro_pier_stone_a` | 2 | 2 | 144 / 34 / 12 | 144 / 34 / 12 |
| `aro_gate_ornate_steel_a` | 1 | 1 | 2156 / 500 / 12 | 2156 / 500 / 12 |
| `aro_lantern_wall_a` | 2 | 2 | 106 | 106 |
| `aro_streetlight_single_arm_a` | 3 | 2 | 256 / 94 / 22 | 256 / 94 / 22 |
| `aro_pole_electric_concrete_span_a` | 5 | 3 | 918 / 234 / 34 | 918 / 234 / 34 |
| `aro_sign_oneway_a` | 3 | 2 | 357 / 69 / 24 | 357 / 69 / 24 |
| `rg_ngn_tree_street_broadleaf_a` and `_b` | 2 each | 1 each | 2364 / 213 / 22 | 2364 / 213 / 22 |
| `aro_hedge_clipped_a` | 2 | 1 | 328 | 328 |
| `aro_shrub_ixora_red_a` | 3 | 1 | 1176 / 36 | 1176 / 36 |
| **all 14** | **55** | **37** | | |
How: three shared **palette materials** (`kano_paint`, `kano_metal`, `kano_glass`) replace the flat-colour materials (white paint, dark and warm interiors, kerb paint, road white, sign blue, insulator brown, dark concrete, rubber; dark aluminium, galvanised and black steel; window and balcony glass). A palette is a 64 x 64 texture of 16 px constant-colour cells holding exactly the colours of the v002 materials; a face's UV is its cell centre (a constant, so nothing tiles, blurs or bleeds through the mip chain). The compromise, stated: one roughness and one metallic value per family (white paint 0.6, dark interior 0.9, insulator 0.35 become 0.7; the three steels become 0.8 metallic and 0.45 rough); glass alpha is the cell alpha times the material alpha (0.42 window and 0.22 balcony, as in v002). Trees, shrubs and hedges use **one vegetation atlas material** (`kano_veg`): the v002 leaf-cluster atlas unchanged in the left half of a 1024 x 512 texture (a test proves it), a bark cell (the v002 bark texture; its normal map is not carried over, a loss on a thin trunk) and flat cells for hedge green and flower red.
The textured surfaces (plaster, grey cladding, travertine, roof tile, concrete, asphalt, pavers, grass) stay separate: they tile, and a tiling texture cannot share an atlas without a shader.

## 5. Geometry defects corrected
Overlapping coplanar faces (two faces in one plane over an overlapping area: Cycles renders them black, a game z-fights) in the frozen v002: villa A 62 pairs, villa B 69, ornate gate 6, wall lantern 1, One Way sign 16. In v003 **every LOD of every asset has none** (`kano_kit.coplanar.separate` moves the later face 3 mm proud, more than the depth buffer confuses out to about 100 m; LODs are separated too), no degenerate (zero-area) face and no duplicate face.

## 6. Verification
| Check | Result |
|---|---|
| Blender validation (budgets, LOD order, ground pivot, names, UVs, closed correctly wound shells) | **14 of 14 passed** (the 5 merged-frontage prototype assets built with the same treatment also passed) |
| Export re-import in a fresh Blender scene (FBX and GLB, LODs, materials, exact LOD0 size) | **28 of 28 ok** (`docs/kano/KANO_RESIDENTIAL_v003.export_verification.json`) |
| Fresh rebuild from the code compared with the frozen v003 manifest | **88 of 88 textures and 19 of 19 GLB files byte-identical** |
| Frozen manifest | `docs/kano/KANO_RESIDENTIAL_v003.freeze.json`: 19 assets, 202 file hashes, the commit of the producing code recorded |
| Unity import, in-game screenshots, measured draw calls | **passed**: all 14 v003 kit assets (and the 5 prototype frontages) imported by the real Unity importer into prefabs with LOD triangles and material counts **equal to the Blender validation**, 0 import problems logged; 24 views taken in the running game with no page error and no magenta (error-shader) pixel; draw calls measured (`PERFORMANCE_REPORT_V003.md`): standard placement 495 to 550 at the hardest views (v002: 618 to 690), **with merged frontages 305 to 340, all within 400**; the CI run also rebuilt the kit and found **88 of 88 textures and 19 of 19 GLB files identical to the frozen v003 manifest**. In-game screenshots: artifact `kano-review-in-game` (kept 7 days); **the owner's visual review of them has not happened** |

## 7. Regression tests (`tools/blender/tests/test_v3_regression.py`, `test_coplanar.py`, `test_env_scenes.py`)
Every LOD of every v003 asset: no overlapping coplanar faces, no degenerate faces, no duplicate faces; every v003 asset passes validation with correctly wound shells and stays within its recorded material count (so a material cannot creep back); palette faces sit at their cell centre and the palette textures hold the v002 colours; the vegetation atlas keeps the v002 leaf atlas; the frozen v002 builders still produce the recorded geometry; the residential v002 overlaps are held at their recorded ceiling.

## 8. What v003 is not
Not approved, not production. The villa facade is closer to R-006 but not a copy of it. The draw-call figures that matter are the Unity measurements in section 6, not estimates. The palette compromise (one roughness and metallic per family) is real and is the owner's to accept after looking at the pictures. Nothing here changes how the game behaves.
