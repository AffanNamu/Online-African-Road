# Kano residential environment: first Blender production milestone

**Status (2026-11-01, after quality pass 2): Blender validation passed for 14 assets. The Unity code compiles in CI and a generic fixture passes its Unity tests; the 14 assets themselves are NOT yet imported into the running game, NOT visually approved, NOT production-ready.** The owner's rule applies: a saved Blender file, a passing validator or a Blender render is not proof that the environment works or looks right in the game. This document says what exists, what was measured, what is still different from the concept, and what is blocked.

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

## 4. Honest comparison with the concept (Blender renders against R-006; this table describes v001, the v002 comparison is in section 10)
| Aspect | Concept R-006 | Blender render | Difference | Correction planned |
|---|---|---|---|---|
| composition: wall, gate and tree on the left, sign, kerb, lane lines and arrows, trees and lights down the right | yes | yes | close | none |
| kerb | bold black/white blocks, about 1 m | same | close | none |
| paver sidewalk | pale pink, grey and beige | pink, grey, beige with more orange variation | slightly too saturated | tone the palette down |
| villas | prominent, richly detailed (cladding, arches, balcony glass, landscaping) | present but plainer; mostly hidden by trees and walls in the reference view | **significant**: the buildings carry less detail than the concept | a second detail pass with the owner's list; stone cladding and ornament; the concept's second house type with arched details |
| gate | ornate steel with rich scrollwork, arched top | ornate with arched top, simplified scrolls | moderate | richer scroll pattern |
| trees | dense detailed leaves, bright | leaf-card crowns, darker, jagged leaf edges | moderate | brighter, finer leaf atlas; real species when known |
| sky and light | saturated blue with clouds, warm sun | saturated blue with clouds, warm sun (render setup, not a game feature) | close | the game's own lighting decides in Unity |
| glass | transparent balustrade and windows | the balustrade reads as an opaque pale panel; window glass is a dark opaque pane | moderate: the kit has no transparent material yet | a transparent kit material (Blender export and Unity builder) |
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

## 8. Quality pass 2 and where things stand now (owner's instructions of 2026-11-01, second message)
**Corrections made (Blender):** transparent balustrade and window glass (kit schema 3 carries `alpha`; a dark room sits behind window glass so it reads as glass, not as a hole); a procedural PBR set (base colour and normal maps for plaster, travertine, ledgestone cladding, roof tile, concrete, bark, paver, asphalt); villa detail (quoins, architraves with keystones, security grilles, cornice, dormer, chimney, air-conditioner; villa A 5,454 of 6,000 triangles, villa B 4,808); trees as 96 clusters of leaf cards from a finer, brighter atlas. All 14 pass the toolchain validation again; the 73 Blender-job tests pass.

**Three kinds of verification, reported separately**
| Level | What it proves | Result |
|---|---|---|
| Blender validation | geometry, budgets, names, UVs, collision, LODs, winding | **passed**, 14 of 14 (this machine and the CI Blender job) |
| Unity validation | the project compiles with the new C# and the Unity-side builder works | **passed on a generic fixture, not on the real assets.** Run 11 of "Blender import into Unity" (commit 00e620b, Unity 6000.0.58f2): the project compiled and **7 of 7 EditMode tests passed**: the textured and cutout material test, glass transparency and the normal-map importer type, the review builder (prefabs, review prefab, Blender-to-Unity axis conversion, a missing export reported), the camera field of view, and the earlier calibration tests. The full EditMode suite of the main CI run 263 also passed. (CI run 262, on the earlier commit, failed because one of my own tests expected 39.6 degrees where the correct value is 39.76; fixed in 00e620b.) The rest of run 263 (WebGL build, browser jobs) was still running when this was written |
| In-game verification | the running game shows the real assets, compared with the references | **not done.** It needs a private build, which needs two things only the owner can do: the three Unity secrets in `ARO-CONTENT`, and `lfs.github.com` reachable so the binaries can be pushed as LFS objects (section 9) |

**What was imported into Unity:** nothing of the 14 assets. **What compiled:** the project with the Unity builder, review scene and tests, as above. **What was tested in the game:** nothing of the real assets; the review scene boots in the normal CI build and logs that it has no review prefab. **What failed:** CI run 262 (my test number, fixed); the push of LFS objects (network policy); the private build could not run (secrets and LFS). **Performance:** no measurement exists yet. The E0 instrumentation is ready: the review script records the game's own `[Perf]` line (visible triangles, draw estimate, renderers, texture MB, heap MB, frame time), the time from page load to the game's ready line, the JS heap, the WebGL build size and any budget-exceeded warning; the expected draw cost of about 400 calls per 96 m section (section 5) is an estimate until then.

## 9. What stops the in-game proof, exactly, and what the owner does
1. **Network allowance for Git LFS.** The agent environment's policy refuses `lfs.github.com` (HTTP 403 on CONNECT; the proxy status records it). The asset branch `assets/kano-residential-v1` of `ARO-CONTENT` is committed **locally** with all 93 binaries converted to LFS pointers; its LFS objects cannot be uploaded from here. In the environment's settings (Network access) add `lfs.github.com` to Allowed domains, then one `git push -u origin assets/kano-residential-v1` completes the migration. The earlier branch `assets/kano-residential-v001` holds the first version of the same assets as plain Git blobs and is superseded; delete it after the LFS branch is pushed. Nothing here is lost if this container is reclaimed: every asset, texture, render and export is rebuilt from the code on this branch.
2. **Unity secrets in the private repository.** Exact method and requirements: `ARO-CONTENT/docs/UNITY_REVIEW_SETUP.md` (Personal serial recovered from the `.ulf`, plus the Unity ID's e-mail and password; the Unity ID must have a password and no two-factor authentication). The values are set by the owner in the repository's secrets, never in chat.
3. Then push a branch `review/kano-residential` to `ARO-CONTENT` (or run the workflow): it builds the game with the private assets, takes the six views (driver level, front, side, complete street, walls and gates and trees and overhead wires, road and buildings together) and keeps the pictures private.

## 10. v002: corrections, verification and freeze (owner's instructions of 2026-11-01, third and fourth messages)
Status of the whole milestone, stated first: **Blender validation passed 14 of 14 for v002; the export verification passed; the assets have NOT been imported into the real Unity game and nothing has been seen running in African Roads Online.** That last step is blocked on two owner actions (section 9). A Blender render is not evidence of completion, and this section does not claim it is.

### 10.1 What changed from v001 and why
| Change | Cause found | Effect |
|---|---|---|
| **Colour pipeline bug fixed** (commit `f45072c`) | The kit converted every base-colour texture from sRGB to linear before storing it in an 8-bit Blender image, but Blender saves the values it is given as they are (checked: 0.5 is saved as byte 128). Every colour texture was therefore decoded twice: a displayed colour of about x squared, darker and more saturated. | This, not the palette, produced the **yellow plaster and red stone** in v001, in the renders **and in the exported PNGs that Unity would have read**. Fixed at the root; a regression test saves a PNG and checks the sRGB value survives. |
| Palette restored to the intended values | plaster 0.88/0.83/0.74, travertine 0.70/0.63/0.54, paler paver palette, asphalt texture 0.21, lawn texture | Colours now display as designed. |
| Lawn texture | the verge was a flat colour | procedural lawn with patches, blade noise and a normal map |
| Foliage | round "cabbage-leaf" crowns (disc-shaped leaf cards) | lobed leaf-atlas silhouettes, about 1,080 small cards per tree: **2,364 triangles (was 1,164; budget 3,000)**; hedge 328 (was 66); shrub 1,176 (was 508) with 46 flower clusters |
| Review renders | exposure was tuned against the darkened textures | exposure -2.9 (was -1.9); previews re-rendered at -3.5 |
| **Villa geometry** | **not changed** | 5,454 and 4,808 triangles; see 10.3 |

**Disclosure:** v001 (the `assets/kano-residential-v001` branch of `ARO-CONTENT` with plain blobs, and the unpushed local LFS branch) carries the faulty textures. It is superseded by v002 and was never reviewed or approved.

### 10.2 Visual verification (Blender, v002)
Six scene views and 28 previews (14 assets, iso and front) were rendered from the frozen v002. The reference-camera view was compared directly with the concept R-006 (private review page and the files sent to the owner).

| Aspect | Concept R-006 | v002 render | Verdict |
|---|---|---|---|
| plaster and stone colour | warm cream walls, tan stone piers | pale warm cream, tan travertine, no yellow or red cast | **close** |
| paver sidewalk, kerb stripes, lane markings, arrows | pale pink-grey pavers, bold black/white kerb, arrows | same character | **close** |
| wall, piers, lanterns, gate | cream wall with grey cladding panel, brick-stone piers, black ornate gate | same arrangement; gate scrollwork simpler | **close to moderate** |
| streetlights, sign, poles | single-arm lights, blue One Way sign | same | **close** |
| trees | airy mid-green crowns, visible branches | feathery lobed foliage, natural at close range (front view) and at driving distance (street views); denser and slightly more lime-lit than the concept | **moderate; acceptable** |
| hedges and shrubs | lush clipped hedges and large red-flowered shrubs | denser than v001; smaller and fewer than the concept | **moderate** |
| **villas** | prominent two-storey villas: richer facade (stone and grey cladding panels, columns, balcony glass, warm interior light) | present and correctly proportioned, but plainer; geometry unchanged since v001 | **SIGNIFICANT** |
| sky, light | saturated blue, warm golden sun | pale grey-white sky and neutral sun: **the review render setup, not game content**; the game's own lighting decides | not comparable |
| far end | mosque skyline | none (owner decision C1: no landmark without a reference) | intentional |

**The one significant remaining difference is the villa facade richness.** It is documented rather than patched blindly: v002 is frozen as the candidate for the in-game review, and a targeted villa detail pass (v003: facade cladding panels, entrance columns and balcony detail within the 6,000-triangle budget, or a budget decision) is planned once the in-game views show which villa views matter.

### 10.3 Export verification (Blender re-import of the exports; **not** a Unity import)
`tools/blender/kano_kit/verify_export.py`, report `docs/kano/KANO_RESIDENTIAL_v002.export_verification.json`. **No check failed.**
* **Colours:** all 9 exported colour textures reproduce their intended mean sRGB colour (largest error 0.0004; the v001 double decode would have failed this). The leaf atlas (RGBA) is checked through the render comparison.
* **Re-import:** all 14 GLB and all 14 FBX files import into a fresh Blender scene with their LOD meshes, materials and texture images.
* **Scale and geometry:** the LOD0 bounding box of each of the 28 re-imported files equals the size measured by the Blender validation (metres, exact to the stored precision).
* **Appearance:** the review scene rebuilt from the exported GLB files with the recorded placements and rendered from the reference camera with the same sky and exposure differs from the in-memory render by a mean absolute difference of **0.75 of 255** (largest channel shift 0.67).
What this does **not** show: how Unity's importer, URP/Lit materials, lighting, collision and LODGroups treat the files. That is the next step.

### 10.4 Performance: measured and estimated, kept apart
| Item | Value | Basis | Budget (`PerfBudget`) | Result |
|---|---|---|---|---|
| Triangles per asset, LOD0 / LOD1 / LOD2 | street 1,518/38; villa A 5,454/824/58; villa B 4,808/774/70; gate 2,156/500/12; tree 2,364/213/22 (both); shrub 1,176/36; hedge 328; pole 918/234/34; sign 357/69/24; streetlight 256/94/22; pier 144/34/12; lantern 106; wall 50 | **measured** by the toolchain validator | catalogue ceilings: buildings 6,000, vegetation 3,000, props 2,500, lights 1,200, signs 800, roads 8,000; last LOD at most 10% of LOD0 | **all pass** (largest use: villa A 91%, gate 86%, tree 79%) |
| Triangles in one 96 m section at LOD0 (166 instances) | 136,611 | computed: recorded placements times validated triangle counts (not a rendered count) | desktop 1,000,000; mobile 250,000 | **13.7% and 54.6%: within budget** |
| Draw calls for one 96 m section | about **432** | **estimate**: instances times materials, nothing batched (the engine instantiates production prefabs unbatched, RB-10) | desktop 400; mobile 150 | **NOT confirmed: the estimate is 8% over the desktop budget and about 2.9 times the mobile budget** |
| Texture memory, 19 textures of 512 x 512 | 25.3 MB uncompressed with mips; about 6.3 MB block-compressed | **estimate** from file sizes (Unity's import settings decide) | desktop 300 MB; mobile 150 MB | within budget |
| Build size, load time, JS heap, frame time, in-game draw calls | not measured | needs the running build (E0) | desktop build 120 MB; mobile 80 MB | **unknown** |

The draw-call estimate is dominated by repeated small parts: piers 84, wall panels 64, villas 84, shrubs 48, lanterns 32, hedges 32, trees 32 (of 432). A content-side mitigation is available without touching the engine: merge each plot's wall, piers, lanterns and gate into one frontage asset (about 98 instances to 8), which brings the estimate to about 284 (below the desktop budget, still above mobile); city-density streaming (RB-15) is the engine-side answer. Neither is done; the in-game `[Perf]` numbers decide whether it is needed.

### 10.5 Frozen candidate
`docs/kano/KANO_RESIDENTIAL_v002.freeze.json` records, for each of the 14 assets, the path, size and SHA-256 of the `.blend`, FBX, GLB, textures, manifest and validation report (126 files), the game-repository commit that holds the producing code (`bba95b2`, last change to the producing modules; branch head `90d20af` when frozen) and the validation result (14 of 14 passed). **v002 is frozen as a candidate for the in-game review. It is not approved:** `production_approved` needs the in-game review and the owner's decision. A change after this point is v003.

### 10.6 Completed and outstanding
*(Written before the in-game run; the Unity import, build size and in-game numbers are now in section 11.)*
| Done | Outstanding |
|---|---|
| 14 assets built as v002, validated (Blender), exported (FBX and GLB), hashed and frozen | **Import into the real Unity game and in-game screenshots** (owner actions in section 9) |
| root cause of the colour cast found and fixed, with a regression test | Unity verification of materials, lighting, orientation, scale, collisions and LODs |
| export verification passed: colours, re-import, exact sizes, appearance | in-game `[Perf]`, build size, load time, memory (E0) |
| comparison with R-006 and the one significant difference recorded | villa facade richness (v003, targeted) |
| sources, exports, previews and hashes preserved as a text manifest in the public code repository | binaries in the private asset repository as Git LFS objects (blocked: `lfs.github.com`) |

## 11. In-game review of the real kit (Unity import and running game): what was measured on 2026-11-02
Run: `blender-unity.yml`, run 41391901470 (commit `dcafeb0`, marker `[kano-review]`), in the game repository's existing workflow with its existing Unity credentials. The 14 residential assets were **built from the code** with the Blender toolchain (all 14 passed the toolchain validation), staged under the git-ignored `Assets/ThirdParty/ARO`, imported by Unity 6000.0.58f2 and built with `ARO.Editor.BuildScript.BuildKanoReviewWebGL` (no backend baked in, not deployed, nothing committed). The built player was then driven in headless Chromium with **software GL (SwiftShader) on a CI virtual machine**, six camera views, the game's own lighting, camera rig, tone mapping and quality tier `medium`. Every step of the job succeeded. This is an **import-and-render proof, not an approval**: `production_approved` needs a person.

### 11.1 Import (from the Unity build log)
| Check | Result |
|---|---|
| Models imported and prefabs built | **14 of 14** (`[KanoReviewBuilder]` line per asset) |
| LOD triangle counts in Unity against the Blender validation | **identical for every asset** (e.g. villa A 5,454 / 824 / 58; tree 2,364 / 213 / 22; street 1,518 / 38) |
| Materials per prefab | as validated (villa A 11, villa B 10, street 7, pole 5, others 1 to 3) |
| Review prefab | `kano_residential_section_a`: **337 placements, 0 skipped** (no missing prefab) |
| Asset-related warnings or errors in the Unity build log | **none found** (search for the asset paths, `kano`, `T_kano`) |
| Page (script) errors in the running game, six views | **none** |
| WebGL build | `WebGL.data.unityweb` 23,702,569 bytes against 20,105,528 in the baseline build of CI run 273: **+3.6 MB**; wasm and framework unchanged; **39 MB** on disk in total (baseline 35.9 MB); desktop budget 120 MB: within |

### 11.2 In-game numbers (the game's own `[Perf]` lines; each view at the first tier tried, `medium`, no fallback)
| View | Visible triangles | Draw calls (game estimate) | Visible renderers | Set-pass calls | Result against the desktop budget (1,000,000 triangles / 400 draw calls) |
|---|---|---|---|---|---|
| ref (reference camera) | 180,789 | **690** | 252 | 37 | triangles within; **draw calls exceed** |
| front | 40,154 | 107 | 30 | 15 | within |
| side | 157,589 | **654** | 248 | 27 | triangles within; **draw calls exceed** |
| street1 | 170,644 | **650** | 233 | 35 | triangles within; **draw calls exceed** |
| street2 | 167,496 | **618** | 219 | 37 | triangles within; **draw calls exceed** |
| top | 116,804 | 331 | 118 | 12 | within |

* **Draw calls exceed the desktop budget in 4 of 6 views (618 to 690 against 400).** The game's figure counts everything visible (terrain chunks, road, props and the kit), so it cannot be split into "kit" and "rest" from these lines; it is not the 432 estimated for the kit alone in section 10.4, and the two must not be compared. The finding stands either way: the unbatched instancing of the section needs the content-side merging described in section 10.4 and, for a real fix, the engine batching item RB-15. Neither was done here.
* **Texture memory:** 112 textures, 20.9 MB loaded in the whole scene (budget 300 MB): within.
* **Load:** about 4 to 5 s from page load to the game's own ready line (software GL, local file server, polled once a second; indicative only).
* **Frame time (470 to 1,160 ms per frame) is NOT a measurement of player performance.** It is CPU software rasterisation on a CI virtual machine. It says nothing about a GPU and must not be quoted as a frame rate. A real frame-time number needs a real GPU and is outstanding.
* **JS heap** 242 to 257 MB (browser, software GL, indicative only).

### 11.3 Materials and textures: what is and is not established
* **Established by numbers and logs:** the textures and materials import, nothing logs an error, the six views render without a script error, and the scene holds 112 textures (20.9 MB).
* **NOT established by me: that the materials look right.** The six pictures (`kanoreview-ref|front|side|street1|street2|top.png`, 1280 x 720) are only in the one-day artifact `kano-review-in-game` of the run (5.3 MB, expires 2026-11-03 05:51 UTC); this environment cannot download artifacts, so **I have not seen them**. Whether the plaster, stone, roof tiles, asphalt, leaves and grass render with the right colour and texture is for a person to judge from those pictures. Anything wrong goes into v003 (a new version; v002 is not edited). *Update: the exact files, camera names and a second, seven-day artifact are in section 12; the draw-call decomposition is in `DRAW_CALL_INVESTIGATION_2026-11-02.md`.*

### 11.4 What this run did not do
Mud, flooding, rainy-season or seasonal behaviour: none of it exists in the game or was built into this review (section 6 of `docs/ENVIRONMENT_SYSTEMS_ASSESSMENT.md`). No driving physics, road generation, weather, vehicle behaviour or production configuration was touched.

## 12. The review pictures: exact files, view names and cameras (owner request of 2026-11-02)
**Where they are.** The pictures are artifacts of the review runs; this environment cannot download artifacts, so I have **not seen them**, and the numbers in section 11 and below are the game's own log lines. A signed-in GitHub user opens the run page, scrolls to *Artifacts* and downloads `kano-review-in-game` (a zip).
| Run | Artifact | Size | Expires | Files |
|---|---|---|---|---|
| [41391901470](https://github.com/AffanNamu/Online-African-Road/actions/runs/41391901470) (first review) | `kano-review-in-game` | 5.3 MB | **2026-11-03 05:51 UTC** (one-day retention) | the six standard views and the summary |
| [41450050093](https://github.com/AffanNamu/Online-African-Road/actions/runs/41450050093) (draw-call investigation) | `kano-review-in-game` | 7.9 MB | **2026-11-09 13:28 UTC** (seven-day retention, set because the owner asked to keep it available) | the six standard views, the merged-frontage and shadow-off diagnostics, and the summary |
The second run is marked *failed* only because the review driver's last measurement step (the per-pixel difference of the merged pictures) had a bug, found and fixed afterwards; all nine screenshots were taken and uploaded and every game-side step succeeded. If the owner wants the pictures kept longer than seven days, a review run with the manual input `retention_days` set (up to the repository's maximum) does it; I will not extend it without being asked, because the artifact of a public repository is readable by signed-in GitHub users.

**Both runs show the frozen v002 content.** A fresh rebuild from the code of the assets (14 of 14) was compared with the frozen v002 manifest: all 42 textures and all 14 GLB exports are **byte-identical**, the validation figures (LOD triangles and sizes) are identical for 14 of 14, and the 14 rebuilt FBX files re-import with exactly the same vertices (to 0.1 mm) and material names as the frozen FBX files (the FBX bytes differ only in embedded paths and times).

| File (`kanoreview-<name>.png`) | View name | What it shows | Blender camera: position to target, lens | Unity camera (from the game's log) |
|---|---|---|---|---|
| `kanoreview-ref.png` | `ref` | the reference camera of concept R-006: eye level 1.7 m at the left kerb line, looking along the street | (-3.2, -45, 1.7) to (0.6, 60, 2.6), 28 mm | (6003.20, 1.70, 6045.00) |
| `kanoreview-front.png` | `front` | from the left sidewalk facing the right-hand frontages | (-5.2, 10, 2.1) to (16, 10, 3.6), 30 mm | (6005.20, 2.10, 5990.00) |
| `kanoreview-side.png` | `side` | along the street from beyond the section start, 4.6 m up | (0, -62, 4.6) to (0, 0, 4.0), 24 mm | (6000.00, 4.60, 6062.00) |
| `kanoreview-street1.png` | `street1` | a pedestrian on the right sidewalk | (4.3, -30, 1.6) to (-6, -4, 1.6), 28 mm | (5995.70, 1.60, 6030.00) |
| `kanoreview-street2.png` | `street2` | the driver's eye in the left lane | (-1.7, -20, 1.2) to (5, 10, 1.5), 28 mm | (6001.70, 1.20, 6020.00) |
| `kanoreview-top.png` | `top` | straight down from 95 m | (0, 0, 95) to (0, 0.5, 0), 50 mm | (6000.00, 95.00, 6000.00) |
| `kanoreview-ref-merged.png` (second run only) | `ref` with `variant=merged` | the reference view with the merged-frontage prototype | as `ref` | as `ref` |
| `kanoreview-street1-merged.png` (second run only) | `street1` with `variant=merged` | the street1 view with the merged-frontage prototype | as `street1` | as `street1` |
| `kanoreview-ref-noshadow.png` (second run only) | `ref` with `shadows=off` | the reference view without the shadow pass (to measure its share) | as `ref` | as `ref` |
Blender axes: street along +Y, right +X, up +Z, metres; the Unity camera is the game's own, converted (x, y, z becomes -x, z, -y) and placed at the review origin (6000, 0, 6000). All pictures: 1280 x 720, quality tier `medium`, 15:30 hour, the game's own lighting, sky and tone mapping, software GL.

**Objective checks made on the nine pictures of the second run** (measured in a browser canvas; they are not a judgement of looks): **no magenta pixels at all** (the colour of Unity's error shader) in any of them; 0.3% to 2.4% near-black pixels (dark interiors and shadows); mean channel values between 38 and 91 of 255, nothing flat or blank. The standard pictures' mean colours: ref 53.3 / 62.4 / 60.9, front 69.0 / 77.8 / 72.0, side 74.2 / 90.6 / 90.3, street1 63.2 / 71.3 / 69.3, street2 56.2 / 64.8 / 63.8, top 44.7 / 60.1 / 38.4.

**Known risk to look for in the pictures: z-fighting on the villas.** A new check (`kano_kit.coplanar`) found overlapping coplanar faces in the frozen v002 villas (62 pairs on villa A, 69 on villa B, largest 0.6 m2: white trim against cream plaster and frames against frames), the ornate gate (6), the wall lantern (1) and the One Way sign (16). Where two faces share a plane they fight for the pixels and flicker in a game. v002 was not touched; a corrected v003 (same separation as the environmental kit) would be built only after the owner's review of v002. Details in `ENVIRONMENT_KIT_E3.md`.
