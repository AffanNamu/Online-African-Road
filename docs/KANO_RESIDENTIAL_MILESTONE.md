# Kano residential environment: first Blender production milestone

**Status (2026-11-01): Blender validation passed for 14 assets. NOT imported into the running game, NOT visually approved, NOT production-ready.** The owner's rule applies: a saved Blender file, a passing validator or a Blender render is not proof that the environment works or looks right in the game. This document says what exists, what was measured, what is still different from the concept, and what is blocked.

Target (owner's execution decision, section E): the Kano residential environment, built from the supplied one-way residential street reference (concept **R-006**, ChatGPT-generated, the owner's compulsory design standard): the road and its markings, residential buildings on both sides, compound walls with detailed gates, doors and windows, sidewalks and kerbs, trees and landscaping, electricity poles with overhead wires, streetlights and supporting details. Built as **real Blender geometry**: no image is used as geometry anywhere.

## 1. What was built

A 96 m street section with four plots on each side, from 14 kit assets. Source code: `tools/blender/kano_kit/` (rebuilds everything in seconds); Blender sources, exports and validation reports are in the private repository `AffanNamu/ARO-CONTENT`, branch `assets/kano-residential-v001` (public repository: code and tests only).

| Asset id | Kind | LOD triangles | Size (m) | Materials | Notes |
|---|---|---|---|---|---|
| `kano_street_oneway_section_a` | roads | 1,518 / 38 | 66 x 96 x 0.15 | 7 | asphalt with markings cut flush into the surface (dashed lane line, solid edge lines), 4 pavement arrows (6 mm decals), painted black/white kerb blocks (1 m), paver sidewalks, concrete edge strip, grass verge, plot ground; two textures (paver, asphalt) |
| `aro_villa_2storey_hip_a` | buildings | 3,042 / 824 / 58 | 13.6 x 12.9 x 9.5 | 9 | two-storey villa: recessed windows with frames, sills and lintels, columned porch, balcony with glass balustrade, grey cladding band, hipped roof with real tile courses |
| `aro_villa_2storey_hip_b` | buildings | 2,898 / 774 / 70 | 16.5 x 13.2 x 9.1 | 8 | second type: gabled front bay, stone-clad ground floor, garage wing with a ribbed roller shutter and parapet roof |
| `aro_wall_compound_panel_a` | props | 50 | 4.25 x 0.37 x 2.4 | 2 | wall bay: grey plinth, cream bands, grey cladding panel on both faces, coping |
| `aro_pier_stone_a` | props | 144 / 34 / 12 | 0.68 x 0.68 x 2.75 | 2 | 12 stone courses, cap block, stepped top |
| `aro_gate_ornate_steel_a` | props | 2,156 / 500 / 12 | 4.0 x 0.07 x 2.3 | 1 | double leaf, arched top rail, 30 bars with spear tips, scroll rings, three rails, lock box |
| `aro_lantern_wall_a` | props | 106 | 0.18 x 0.19 x 0.34 | 2 | wall lantern with glowing glass |
| `aro_streetlight_single_arm_a` | lights | 256 / 94 / 22 | 0.44 x 3.2 x 9.5 | 3 | 9 m tapered pole, swept 2.4 m arm, cobra-head luminaire |
| `aro_pole_electric_concrete_span_a` | environments | 918 / 234 / 34 | 1.9 x 30.2 x 9.5 | 5 | **owner-requested, NOT in the concept, no Kano reference (M-06)**: concrete pole, cross-arm, three pin insulators, four spool insulators, seven conductors with sag over a 30 m span |
| `aro_sign_oneway_a` | signs | 357 / 69 / 24 | 0.92 x 0.13 x 2.67 | 3 | blue sign with border, arrow and extruded "One Way" lettering |
| `rg_ngn_tree_street_broadleaf_a`, `_b` | vegetation | 948 / 213 / 22 | about 7.2 x 7.5 x 8.7 / 9.5 | 2 | trunk and scaffold branches; crown of alpha-cut leaf cards from a procedural leaf atlas; generic species (the real street trees are not established, M-07) |
| `aro_hedge_clipped_a` | vegetation | 66 | 2.3 x 1.2 x 1.0 | 2 | dark core wrapped in leaf cards |
| `aro_shrub_ixora_red_a` | vegetation | 508 / 36 | 1.4 x 1.4 x 1.1 | 3 | dome of leaf cards with red flower clusters |

All 14 pass the toolchain's validation (`validate.run`): the game's own triangle ceilings, strictly decreasing LODs with the last at most 10%, ground pivot, applied transforms, naming, UVs, box collision (12 triangles), plus a winding check (no inside-out closed shell). 73 tests of the Blender job pass (`tools/blender/tests`, including one per asset). The villa, tree and wall counts are far under the ceilings (buildings 6,000, vegetation 3,000, props 2,500): there is room to add detail.

### Cross-section and layout (measured, in `street.py` and `assemble.py`)
Carriageway 7.0 m (two 3.5 m lanes), kerb 0.2 m x 0.15 m high, paver sidewalk 2.4 m (0.15 m above the road), concrete edge strip 0.1 m, grass verge 1.1 m, compound wall at 7.4 m (0.25 m thick, 2.4 m high, piers 2.75 m). Plots 24 m wide in five 4.8 m bays, one bay holds the gate; villas A and B alternate, set back 5.5 m behind the wall with the porch aligned to the gate; trees every 12 m on the verge, streetlights every 36 m staggered, concrete poles every 30 m on one side, the sign near the start. The Blender review scene repeats the section to 384 m with low LODs so the street does not end in front of the camera.

## 2. The four kinds of information in R-006 (owner's decision B)
| Element of the concept | Kind | Handling |
|---|---|---|
| lane count, kerb pattern, paver sidewalk, markings, arrows, wall and gate design, villa massing, trees, lights, shrubs | visual design requirement | reproduced as geometry |
| the villas, the street itself | fictional architectural concept | built and labelled fictional; not a claim about any real Kano street |
| "One Way" sign | visual design requirement (generic regulatory sign) | text is generic; no place name |
| the mosque with a teal dome at the far end | would be a landmark if it were Kano's Central Mosque | **not built**: treated as a generic mosque until an authentic reference exists |
| electricity poles and overhead wires | owner-requested addition; not in the concept; real Kano poles not documented (M-06) | generic design, flagged `ownerRequest` in the registry |
| any real location, coordinate or distance | verified geographical fact | none is used; nothing in this milestone claims a location |

## 3. Renders (previews of Blender geometry, not Unity)
Six renders at 1280 x 720 (Cycles, 64 samples) from the camera set: `ref` (the reference camera: eye level 1.7 m at the left kerb line, 28 mm, looking down the street), `front` (the right-hand frontages from the left sidewalk), `side` (the street profile along the street), `street1` (pedestrian on the right sidewalk), `street2` (driver's eye in the left lane) and `top` (plan view). They are in the private repository (`previews/kano_residential_section_a/v001/`) and on the private review page. A texture-free orthographic front and side elevation of each building is a further step (not done).

## 4. Honest comparison with the concept (Blender renders against R-006)
| Aspect | Concept R-006 | Blender render | Difference | Correction planned |
|---|---|---|---|---|
| composition: wall, gate and tree on the left, sign, kerb, lane lines and arrows, trees and lights down the right | yes | yes | close | none |
| kerb | bold black/white blocks, about 1 m | same | close | none |
| paver sidewalk | pale pink, grey and beige | pink, grey, beige with more orange variation | slightly too saturated | tone the palette down |
| villas | prominent, richly detailed (cladding, arches, balcony glass, landscaping) | present but plainer; mostly hidden by trees and walls in the reference view | **significant**: the buildings carry less detail than the concept | a second detail pass with the owner's list; stone cladding and ornament; the concept's second house type with arched details |
| gate | ornate steel with rich scrollwork, arched top | ornate with arched top, simplified scrolls | moderate | richer scroll pattern |
| trees | dense detailed leaves, bright | leaf-card crowns, darker, jagged leaf edges | moderate | brighter, finer leaf atlas; real species when known |
| sky and light | saturated blue with clouds, warm sun | saturated blue with clouds, warm sun (render setup, not a game feature) | close | the game's own lighting decides in Unity |
| far end | skyline with a mosque | a long street, no skyline | intentional | generic skyline later |
| shrubs and hedges | lush, red flowers | leaf-card shrubs with red clusters | moderate | denser flowers |
| materials | rich | flat colours plus two textures | **moderate to significant** | PBR texture set (normal, roughness) after the Unity import shows what matters |

These differences are the open list for the owner's review. No concept is claimed matched.

## 5. Unity integration (written; verification status stated plainly)
| Piece | Where | Status |
|---|---|---|
| Kit materials with base-colour textures, alpha-clip cutout and two-sided drawing | `BlenderAssetBuilder.KitMaterial`, `MaterialKitEntry`, `materials.kit_json()` | written; tested by the generic fixture test `TexturedAndCutoutKitMaterialsSurviveTheBuilderWithTheirLodsAndCollision` in CI (the result is recorded below) |
| Prefabs from the exports (identity root, LODGroup, BoxCollider, kit materials) and the review prefab from the placement list | `KanoReviewBuilder` (Editor) | written; runs only with the private assets present; **not yet run on the real assets** |
| The game shows the street with its own lighting, camera and post-processing | `?smoke=kanostreet&view=ref` (`SmokeMode.KanoStreet`, `KanoReviewScene`, `GameFlow`), `tools/webgl-kanoreview.js` | written; compiled and booted in CI as part of the normal build (recorded below); **not yet shown with the real assets** |
| Private build, screenshots and logs | `.github/workflows/unity-review.yml` in `ARO-CONTENT` | written; **needs the three Unity secrets in that private repository, which only the owner can set** |

**What stops the in-game screenshots today.** The assets are private and the game repository is public: its artifacts, logs and published screenshots are readable by anyone signed in to GitHub, and the owner's rule is that nothing built from these assets leaves a private place. The Unity license secrets exist only in the game repository. The private repository therefore needs `UNITY_LICENSE`, `UNITY_EMAIL` and `UNITY_PASSWORD` to run `unity-review.yml`. Until then: the Unity-side code is verified on a generic fixture, and the real assets are **not** verified in Unity. Also: `lfs.github.com` is refused by this environment's network policy, so the binaries are committed to the private branch as ordinary blobs and must be converted to LFS from the owner's machine (instructions in that branch).

### Expected draw cost (an estimate, not a measurement)
Counting every instance in the 96 m section with each of its materials as one draw call (no batching or culling) gives roughly 400 draw calls, the desktop budget, before the road, terrain, traffic and UI. Three consequences: (1) the **one plot, one mesh, one atlas material** step of the production plan is required, not optional; (2) GPU instancing on the shared kit materials and the SRP batcher will reduce it, by how much only the game can tell (`PerfProbe` in the review run reports it); (3) this is why city-density streaming (RB-15) is in the engine proposal.

## 6. The visual-fidelity gate for the next step
1. Build the review prefab in the real game and take the six views with `webgl-kanoreview.js`.
2. Put each beside the concept and the Blender render of the same camera; list every significant difference with a cause (geometry, material, lighting, engine).
3. Correct, rebuild (new asset versions; approved versions are never overwritten), repeat. Do not move to the next asset family while a critical mismatch is open.
4. Only then the owner may move assets to `in_game_visual_review_passed` and `production_approved`.

## 7. Rebuild
```
cd tools/blender && python3.13 -m venv ~/.aro/venv && ~/.aro/venv/bin/pip install -r requirements.txt   # once (setup.sh)
PYTHONPATH=. ~/.aro/venv/bin/python -m kano_kit.build_all                       # build, validate, save projects, export FBX and GLB (versioned, never overwritten)
PYTHONPATH=. ~/.aro/venv/bin/python -m kano_kit.render_scene --out DIR --views ref,front,side,street1,street2,top --placements DIR/placements.json
PYTHONPATH=. ~/.aro/venv/bin/python -m kano_kit.stage_private --content <ARO-CONTENT checkout> --placements DIR/placements.json --renders DIR
```
