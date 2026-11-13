# Asset realism pass (2026-11-13): the transport kit's solid assets become v002

Written for the owner's instruction "polish all the assets, make this 3D highly realistic, every corner". **Nothing here is visually approved.** Levels: 1 implemented, 2 automated tests, 3 built and imported in Unity, 4 seen in gameplay, 5 visually reviewed by the owner.

## 1. What the in-game pictures showed (the audit)

Read from the review pictures of run 42684305591 (24 scene views, the city sheet, low tier, SwiftShader) and from Blender previews of every asset:

| Defect | Where | Why it looks wrong |
|---|---|---|
| Flat colours | every transport-kit asset (one palette colour per surface) | walls, roofs and paving read as plastic: no joints, courses, ribs, grain, dust or wear |
| Black window holes | mosques, city buildings, terminal hall, residential kit | dark glass with no frame, reflection or interior reads as a hole in the wall |
| Blank walls | the v1 apartment block's sides and back, the parade's back, office sides | a city block seen from the side street is a cardboard box |
| No contact shadow | everywhere | nothing sits on the ground or under an overhang; at low tier there are no real-time shadows to hide it |
| Floating slab | the v1 minaret balconies | a slab hovering over the shaft |
| Grey boxes at distance | LOD2 of the mosques | the far city shows grey blocks |
| **Flicker (z-fighting)** | **906 pairs of faces** in the 35 assets: the builders make solids from overlapping boxes, so two faces often lie in the same plane facing the same way | the renderer cannot order them: flicker in the game, black patches in a ray-traced preview (the pattern panel) |

## 2. What was done

### 2.1 Geometry, version 2 of families E and F
* **Mosques** (`transport_kit/mosque.py`): relief window surrounds with lattice glazing; minaret balconies on a corbel with corner posts and two rails a side (no floating slab); windows on every wall; cornices, plinths, parapet bands and merlons; the iwan as a recessed door with steps under a relief frame; a compound with a forecourt, side and back walls, a gate and open leaves; the drum with lattice windows; ribs on the dome.
* **City buildings** (`transport_kit/city.py`): windows with sills on every wall (no blank side), blank sign boards (no brand), roller shutters, back doors, air-conditioning units, downpipes, black roof water tanks on stands, satellite dishes.

### 2.2 The surface bake (`tools/blender/transport_kit/surface.py`)
Each of the 35 solid assets is painted into **one texture atlas per LOD** instead of flat palette colours. The steps, in order:

1. **Clean the inside of the model** (the residential kit has its own coplanar tool, `kano_kit.coplanar`, which compares bounding rectangles and is exact for axis-aligned quads; the bake tests the actual polygons, so diamonds, octagons and sloped faces count too, and it moves whole primitives). Pairs of coplanar faces facing the same way are separated: the smaller primitive moves 3 mm out along the shared normal (primitives are found by shared edges, because the builders do not share vertices between faces). A face wholly covered by a larger face of the same primitive is deleted. Faces buried inside another volume are deleted. Faces that cannot be seen (no ray leaves the model) are deleted.
2. **Lay out the atlas.** Every face gets its own chart. Charts are shelf-packed into a square atlas at the highest texel density that fits, found by binary search. Glass, doors, shutters and boards get 1.3× the wall density, roofs and paving 0.4–0.5×, soffits 0.3×.
3. **Paint the material patterns** in world space, so that faces meeting at a corner continue the same pattern:
   * ashlar stone (courses, blocks and joints) and render;
   * mud render and pavers;
   * corrugated and painted sheet;
   * glazing (frame along the face outline, mullions, a transom, sky reflection, curtains and reveal shadow) and lattice windows;
   * timber doors and roller shutters;
   * glazed dome tiles, gold, water, foliage and lamps.
4. **Bake ambient occlusion.** 20 rays per sample, out to 3.2 m, against the model and the ground plane. Hidden samples are not allowed to darken what is visible:
   * a buried sample takes the value of its open neighbours;
   * a sample with geometry within 15 cm in front of it (behind a sign, sill or cornice) does the same;
   * a lone sample more than 0.2 darker than the median of its 3×3 neighbourhood is lifted.
5. **Weather the surfaces.** Laterite dust a hand to a forearm high at the foot of walls, patchy. Rain streaks that start at a ledge and fade over 3 m. Stains on flat roofs. A little dirt where the occlusion is deep.
6. **Cap the albedo** at 0.82. Fresh white paint reflects about 0.8; brighter texels blow out to a flat white plane in sun.
7. **Write the maps.**
   * Base colour (sRGB) with smoothness in the alpha channel: glass and glazed tile are smooth, render and dust are rough.
   * A normal map from the pattern height, at half resolution.
   * One material `M_sb_<id>_v002_l<lod>` per LOD.

Atlas sizes per LOD, as declared in `specs.py`:

| Assets | LOD0 / LOD1 / LOD2 |
|---|---|
| grand mosques | 2048 / 512 / 128 px |
| other mosques, city buildings, warehouses, terminal hall | 1024 / 512 / 128 |
| small props | 256–512 / 128–256 / 32–64 |

### 2.3 Unity
`BlenderAssetBuilder.KitMaterial` reads `smoothness_alpha` from the material kit and sets the URP/Lit material to:
* take smoothness from the base map's alpha (`_SMOOTHNESS_TEXTURE_ALBEDO_CHANNEL_A`);
* import the atlas as sRGB, with alpha kept as data, mip maps, anisotropic filtering 4 and clamp addressing;
* import the normal map as a normal map.

### 2.4 Versions: nothing frozen is touched
The realism pass is **version 2** of every asset it bakes (`registry.REALISM_VERSION`). The version is in every material and texture name, so the append-only library never sees the same name with other bytes. A fresh workspace, as in the CI review, labels these assets v002 (`build_transport.next_version`). In the private library, v001 stays beside v002, unchanged. The registry entry points at v002 with its status set back to `blender_validation_passed`, and the batch has its own release and freeze manifests. The other 43 transport-kit assets keep v001.

## 3. Per-asset numbers (from the build report of 2026-11-13, `build_transport --report`)

| Asset | Atlas px (LOD0/1/2) | LOD triangles v001 → v002 | texels per metre on walls (LOD0) | hidden faces removed (LOD0) | coplanar fixes (LOD0: primitives moved + covered faces deleted) |
|---|---|---|---|---|---|
| rg_ngn_warehouse_hall_a | 1024/512/128 | 1450/396/58 → 1060/290/42 | 16 | 167 | 25 |
| rg_ngn_warehouse_hall_b | 1024/512/128 | 1048/370/70 → 744/272/54 | 19 | 132 | 20 |
| rg_ngn_warehouse_cold_a | 1024/512/128 | 1294/244/40 → 952/192/34 | 23 | 140 | 12 |
| aro_container_20ft_a | 256/128/64 | 322/82/10 → 312/80/10 | 21 | 5 | 8 |
| aro_container_20ft_b | 256/128/64 | 322/82/10 → 312/80/10 | 21 | 5 | 8 |
| aro_container_20ft_c | 256/128/64 | 322/82/10 → 312/80/10 | 21 | 5 | 8 |
| aro_container_40ft_a | 512/128/64 | 322/82/10 → 312/80/10 | 40 | 5 | 12 |
| aro_container_40ft_b | 512/128/64 | 322/82/10 → 312/80/10 | 40 | 5 | 12 |
| aro_container_stack_3 | 512/256/64 | 754/274/34 → 672/216/34 | 21 | 41 | 12 |
| aro_truck_trailer_parked_a | 256/128/64 | 572/240/42 → 528/230/12 | 20 | 16 | 6 |
| aro_truck_trailer_parked_b | 512/128/64 | 576/160/32 → 466/146/12 | 39 | 52 | 7 |
| kano_bus_terminal_hall_a | 1024/512/128 | 2388/364/60 → 2094/286/56 | 18 | 147 | 16 |
| aro_roundabout_fountain_a | 512/256/64 | 978/300/28 → 738/300/28 | 47 | 120 | 0 |
| rg_ngn_island_wall_a | 256/128/32 | 242/30/12 → 146/24/12 | 53 | 48 | 11 |
| rg_ngn_pattern_panel_a | 256/128/32 | 374/34/12 → 268/34/12 | 52 | 53 | 5 |
| rg_ngn_pattern_panel_b | 256/128/32 | 426/34/12 → 424/34/12 | 55 | 1 | 1 |
| rg_ngn_pattern_panel_c | 256/128/32 | 302/34/12 → 230/34/12 | 56 | 18 | 0 |
| aro_flyover_pier_a | 256/128/32 | 280/76/24 → 232/70/22 | 22 | 18 | 3 |
| aro_flyover_pier_b | 256/128/32 | 274/38/24 → 244/36/22 | 18 | 15 | 2 |
| rg_ngn_pier_panel_a | 256/128/32 | 582/18/10 → 480/18/10 | 27 | 45 | 7 |
| lmk_kano_roundabout_monument_a | 512/256/64 | 1788/186/22 → 1650/180/22 | 15 | 67 | 4 |
| kano_mosque_grand_a | 2048/512/128 | 5410/2168/108 → 4936/1487/108 | 19 | 314 | 49 |
| kano_mosque_grand_b | 2048/512/128 | 5370/2450/108 → 5109/1737/106 | 19 | 214 | 41 |
| rg_ngn_mosque_medium_a | 1024/512/128 | 2850/1238/96 → 2923/867/96 | 17 | 145 | 52 |
| rg_ngn_mosque_medium_b | 1024/512/128 | 2904/1444/36 → 2833/992/36 | 16 | 134 | 18 |
| rg_ngn_mosque_medium_c | 1024/512/128 | 3300/1440/96 → 3309/1073/96 | 16 | 156 | 44 |
| rg_ngn_mosque_small_a | 1024/512/128 | 1774/780/36 → 1694/525/36 | 28 | 92 | 14 |
| rg_ngn_mosque_small_b | 1024/512/128 | 1768/906/36 → 1654/573/36 | 26 | 111 | 18 |
| rg_ngn_mosque_small_c | 1024/512/128 | 1526/728/36 → 1477/542/36 | 34 | 74 | 22 |
| rg_ngn_mosque_traditional_a | 1024/512/128 | 1358/472/36 → 1358/268/36 | 21 | 37 | 5 |
| rg_ngn_mosque_traditional_b | 1024/512/128 | 730/234/36 → 753/216/36 | 32 | 28 | 4 |
| kano_shopping_parade_a | 1024/512/128 | 1300/214/24 → 1194/280/22 | 15 | 18 | 0 |
| kano_office_block_a | 1024/512/128 | 916/268/24 → 870/232/22 | 12 | 80 | 0 |
| kano_midrise_apartments_a | 1024/512/128 | 1226/484/12 → 1368/612/12 | 16 | 109 | 0 |
| lmk_kano_roundabout_monument_b | 512/256/64 | 474/222/36 → 366/162/32 | 15 | 54 | 0 |
| **35 assets** | | LOD0 45,844 → 42,332 | | 2671 | 446 |

How to read the table:
* **Triangles mostly fall**: the faces nobody can see are gone. Two assets rise:
  * the apartment block, from windows on every wall;
  * the medium mosque a, by 73 triangles at LOD0, from the v2 geometry.
* **Validation:** every LOD passes the validator. Every asset has 3 materials (one per LOD), against a cap of 4. The last LOD is at most 10% of LOD0 on every asset, and that rule was not relaxed.
* **Texel density:** 12 to 56 texels per metre on walls at LOD0. That is a 2 to 8 cm texel, enough for courses, joints, ribs, dust and occlusion from the road. It is **not** enough for close-up grain (see section 6).
* **Texture memory:** about 51 MB for all 35 atlases with all their LODs, counting block compression at 1 byte per texel and the mip chain; the two grand mosques take about 14 MB of that. This is computed, not measured in the build. A route loads only the assets it places.

## 4. Tests (level 2)

**Blender suite (402 pass).** `tools/blender/tests` passes in full: 402 tests, 16 of them new in `test_surface_bake.py`. The new tests cover:
* every pattern is finite and deterministic;
* charts stay inside the atlas and never share a texel;
* buried faces are removed and an overhanging soffit is kept;
* occlusion is darker under a slab and at the foot of a wall, with no ground shadow for a piece that does not stand on the ground;
* the wall behind a proud sign takes the open wall's value; glass is smoother than render;
* normals have unit length; an oversize bake says so;
* the 35 baked assets are the solid ones and the glass shelters stay on the palette;
* three real assets carry one atlas material per LOD, with the right image sizes, colour spaces and UV range, no texel above the albedo cap, and no coplanar overlap between different colours;
* the version labels: v002 in a fresh workspace, v003 after that;
* coplanar overlaps are found and separated; back-to-back faces are not flagged; a covered face is deleted and a partial one kept;
* the pattern panel bakes clean.

**Mosque palette test, restated not weakened.** `test_mosque_family`'s palette test now checks the colours each atlas was painted from, because the materials `M_tk_paint_ext` and `M_tk_paint_ext2` no longer exist on baked assets. The rule is the same: mud render on the traditional pair and never ivory stone; ivory stone on the others and never mud render. The test now also checks the dominant wall colour.

**Other checks:**

| Check | Result |
|---|---|
| `.NET` tests | 1,406 pass. The two city golden digests changed deliberately because the mosque footprints changed; every structural rule of the city routes still passes on the new footprints. |
| ARO-CONTENT validator and tests | pass, 52 tests |
| Preservation audit (`library_audit.py preserve`, previous tip cc8f426) | 1,689 of 1,693 paths identical; 4 reconciled text files; 0 missing; 0 deleted |
| Unity C# type check (semcheck) | no new errors |

## 5. What is proven and what is not

| Level | State |
|---|---|
| 1 and 2 | **Yes.** Pipeline and tests as in section 4. 78 of 78 transport-kit assets build and pass the validator; 35 are v002. The catalogue, the city prop entries and the two city routes are regenerated from them. |
| Private library | **Yes.** ARO-CONTENT `assets/transport-kit-v1` commit a57f9fc: 461 new files beside the v001 ones (nothing overwritten or deleted), release `transport_kit_realism_v002`. The game repository keeps the hashes as `docs/kano/TRANSPORT_KIT_REALISM_v002.freeze.json`: 35 assets, 560 files. |
| 3 Unity | **Running at the time of writing**: review run 43523565815 on commit bf2705e built all 78 transport-kit assets from the code in CI (validation passed for every one, the 35 baked ones as v002), staged them, and is building the game in Unity 6. Its result is recorded here when it ends. |
| 4 gameplay | **Not yet.** The city routes are driven in the same review run (the eight places of `tools/webgl-kanocity.js`). Pictures and numbers follow when it ends. |
| 5 visual | **No.** Blender previews and agent reading only; the owner decides. `production_approved` is not set on anything. |

## 6. What this pass did not polish, and why

**43 transport-kit assets stay v001 on the shared palettes.** These are:
* the glass bus shelters: their glass must stay see-through, and the atlas alpha holds smoothness, not transparency;
* signs, lights, poles, rails, fences, guard rails, kerbs, bollards, bins and planters: a few flat colours on a thin piece, where the palettes let hundreds of instances share three materials;
* five small props: the dock leveller, pallet stack, fuel island, ticket kiosk and jersey barrier. With their buried faces removed, LOD0 becomes so lean that their last LOD would exceed 10% of it. The rule was kept; a leaner LOD2 builder would let them be baked too.

**The residential kit v3 (frozen, imported in Unity).** This covers the villas, Hausa compounds, mud-brick and block houses, the roadside shop row and the market shed, which fill much of the city frontage. Its walls already use textured materials. The visible defect is the same black window glass. A **v004 through the same bake** is the natural next step: it needs patterns for the kit's own textured surfaces (cladding, travertine, roof tile), a new freeze and a review run. It was not started, so that this pass could be finished and checked end to end.

**The environmental kit (E1 to E4).** Trees, vegetation and terrain pieces have their own materials, and they were not part of this pass.

**Close-up grain.** At 12 to 56 texels per metre the atlas carries the large and middle scale: courses, ribs, dust, streaks and occlusion. Within about 10 m it is soft. The usual remedy is a tiling **detail map** (URP/Lit's detail albedo and detail normal on a second UV, or a world-space detail shader). That changes the shared material template, so it is proposed here and not done.

**Lighting.** The CI pictures are the low tier on a software renderer, with no real-time shadows. The baked occlusion is what places a building on the ground there. Nothing in the lighting, weather or production configuration was touched.

**Plain painted panels.** A white panel lit head-on, such as a trailer's front end, is plain white. That is correct for white paint, but it has no detail.

## 7. How to rebuild

From `tools/blender` (toolchain: handover section 6):
```
PYTHONPATH=. ~/.aro/venv/bin/python -m pytest tests -q                                                              # 402 tests
PYTHONPATH=. ~/.aro/venv/bin/python -m transport_kit.build_transport --report /tmp/r.json --catalog ../../docs/kano/TRANSPORT_KIT_catalog.json   # 78 assets; the 35 baked ones come out v002 in a fresh workspace
PYTHONPATH=. ~/.aro/venv/bin/python -m transport_kit.render_tk --out /tmp/prev --only <ids>                          # previews (Cycles, CPU)
python3 ../kano/make_city_routes.py                                                                                  # the city prop entries and the two city routes, from the catalogue (run from the repository root)
PYTHONPATH=. ~/.aro/venv/bin/python -m transport_kit.stage_private --content <ARO-CONTENT> --game-docs ../../docs/kano --previews /tmp/prev --only <ids> --release <new name>   # a later version: new folders beside the old
PYTHONPATH=. ~/.aro/venv/bin/python -m kano_kit.freeze --kit transport --only <ids> --label <name> --out docs/kano/<NAME>.freeze.json
```
**A v003** of any of these assets means two things:
* raise its `version` in `specs.py` (or `REALISM_VERSION`), so that names and folders move on;
* never rebuild v002 under the same label: the library refuses other bytes for a frozen name.
