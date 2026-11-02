# Environmental kit, batch E3 (Blender content production, 2026-11-02)

**Status: Blender validation passed for all 15 assets listed (10 new, 5 corrected); nothing is imported into Unity, nothing is verified in the game, nothing is approved.** This is content only. **No driving physics, road generation, weather, vehicle behaviour or production configuration was changed, and none of the engine proposals RB-18 to RB-25 was started.** Mud, flooding, rainy-season and seasonal behaviour still do not exist in the game: these are meshes and textures. The kit is **not Unity-verified** and must not be described as such until it has passed an actual Unity import and an in-game review.

## What exists
| Asset | Version | Class | Library category | References | Triangles per LOD | Size m (x y z) | Materials (draw calls per LOD0 instance) |
|---|---|---|---|---|---|---|---|
| `rg_ngn_shed_market_corrugated_a` | v001 | REGIONAL | markets_roadside_businesses | R-021 | 2708 / 92 | 15.4 x 9.44 x 4.36 | 9 |
| `rg_ngn_kiosk_lockup_a` | v001 | REGIONAL (new registry entry) | markets_roadside_businesses | R-016, R-021 | 906 / 28 | 4.12 x 3.35 x 2.64 | 9 |
| `rg_ngn_house_mudbrick_b` | v001 | REGIONAL | rural_settlements | R-020, R-021 | 776 / 30 | 10.84 x 7.49 x 3.96 | 8 |
| `rg_ngn_house_block_corrugated_a` | v001 | REGIONAL | rural_settlements | R-016, R-018, R-021 | 1022 / 34 | 10.24 x 7.24 x 4.26 | 8 |
| `rg_ngn_hausa_compound_b` | v001 | REGIONAL | traditional_architecture | **none (M-01)** | 1998 / 96 | 14.14 x 12.18 x 4.59 | 5 |
| `rg_ngn_hausa_wall_gate_set_a` | v001 | REGIONAL | boundaries_walls_gates | **none (M-01)** | 934 / 36 | 16.0 x 1.62 x 4.69 | 5 |
| `rg_ngn_banana_plant_a` | v001 | REGIONAL | vegetation_foliage | R-020 | 1385 / 362 / 78 | 4.36 x 3.97 x 3.48 | 4 |
| `rg_ngn_bougainvillea_wall_a` | v001 | REGIONAL | vegetation_foliage | R-016 | 1206 / 106 / 32 | 4.5 x 1.54 x 2.63 | 5 |
| `rg_ngn_tree_large_shade_wet_a` | v001 | REGIONAL (new registry entry) | vegetation_foliage | R-019, R-020 | 2244 / 213 / 22 | 12.27 x 11.26 x 11.99 | 2 |
| `rg_ngn_grass_tall_wet_a` | v001 | REGIONAL (new registry entry) | vegetation_foliage | R-019, R-020 | 60 | 1.34 x 1.40 x 1.59 | 1 |
| *corrected:* `rg_ngn_hausa_compound_a` | **v002** (v001 kept) | REGIONAL | traditional_architecture | none (M-01) | 2406 / 108 | 20.14 x 16.18 x 7.21 | 5 |
| *corrected:* `rg_ngn_shop_row_roadside_a` | **v002** (v001 kept) | REGIONAL | markets_roadside_businesses | R-016 to R-019 | 1304 / 28 | 15.81 x 6.77 x 3.40 | 12 |
| *corrected:* `rg_ngn_house_mudbrick_a` | **v002** (v001 kept) | REGIONAL | rural_settlements | R-020, R-021 | 582 / 38 | 8.84 x 7.92 x 4.46 | 8 |
| *corrected:* `aro_drain_open_slab_a` | **v002** (v001 kept) | GLOBAL | drainage_flooding | R-016, R-019 | 282 / 12 | 1.1 x 6.0 x 0.95 | 4 |
| *corrected:* `aro_water_puddle_set_a` | **v002** (v001 kept) | GLOBAL | road_mud_wet | R-018, R-020, R-021 | 168 | 7.55 x 7.75 x 0.007 | 2 |

All 15 passed the toolchain validation (budgets, LOD order, ground pivot, names, UVs, closed correctly wound shells). Sources, exports, previews and 184 file hashes are recorded in `docs/kano/ENVIRONMENT_KIT_E3.freeze.json` (commit of the producing code recorded inside).

## How the pieces map to the owner's scope
| Scope item | Built in E3 | Still missing |
|---|---|---|
| Hausa compounds | a second compound type (zaure at the end of the frontage, a long back room, a left room) and a 16 m street frontage with an entrance block, both **designed from the documented qualitative features only** | compound C, neighbourhood mosque, old-city lanes; **every one needs real references (M-01, M-03, M-12)** before it can be approved |
| Rural settlements | a second mud-brick house with a lean-to veranda, a plastered block house with a hipped roof and barred windows | granaries, village layout, roof kit; the vernacular is **not verified (M-14)** |
| Traditional markets, kiosks, commercial areas | an open market shed with six table stalls, a lock-up kiosk | the real Kano markets (Kurmi, Sabon Gari) have **no reference (M-04, M-12)** and are not depicted; signage and goods sets |
| Seasonal vegetation | wet-season shade tree and wet-season grass (separate assets), banana plant, bougainvillea over a wall | harmattan dress, bare dry-season forms, other species (**not established, M-07, M-17**); switching dress by season is engine work (RB-24), not done |

## A defect found and corrected: overlapping coplanar faces
While looking at the E3 previews, black squares appeared at the plinth corners of the Hausa compounds (they are also visible in the E2 contact sheet sent earlier, which I should have caught then). Cause: two boxes that share a wall plane over an overlapping area, so two faces occupy the same plane. Cycles renders such faces black; a game renders them as flickering z-fighting. `kano_kit.coplanar` now finds them (same plane, same orientation, overlapping area; undersides on the ground are ignored) and an opt-in `separate` moves the later face 3 mm proud (more than the depth buffer confuses out to about 100 m; beyond that the LODs apply and are separated too). Results, counted with an overlap area of at least 0.002 m2:

| Kit | Before | After |
|---|---|---|
| Environmental kit, 26 assets | 10 assets with overlaps (compound A 109 pairs, shop row 67, house A 23, drain 12, puddles 9, and the new compounds, houses, kiosk and gate set) | **0**, enforced by a test (`test_the_environmental_kit_has_no_overlapping_coplanar_faces`) |
| **Residential kit v002 (frozen)** | villa A 62 pairs, villa B 69, ornate gate 6, wall lantern 1, One Way sign 16 (largest 0.6 m2 on villa A) | **unchanged: v002 is frozen and was not touched**; the numbers are recorded as a ceiling in the tests so they cannot grow |

**What this means for the residential approval:** the villas carry overlapping coplanar faces where the white trim meets the cream plaster and where frames meet frames. In the game these can show as small flickering patches on the villa facades. I have not seen the in-game pictures (see `KANO_RESIDENTIAL_MILESTONE.md` section 11.3); if they show it, the fix is the same separation in a corrected **v003**, which would be built only after the owner's review of v002, never by editing v002.

## What the assets are not
* They are **not** Unity-verified, **not** in-game verified and **not** approved. A Blender validation and a Blender preview prove nothing about Unity.
* The vernacular forms (houses, kiosk, market shed) are design interpretations of what the generated references show; **the generated references are design targets, not evidence of how Kano looks**.
* The two Hausa-style assets have no reference at all: every proportion and every plan is a design choice; no landmark, real compound or real gate is depicted.
* The foliage assets are alpha-clipped cards and bent leaf strips: they need the kit's cutout materials in Unity (the material kit JSON carries them). The banana leaf's tears are in the texture, not the geometry, and its green is stylised.
* The draw-call cost of every asset is its material count (the last column): the game batches nothing today. The kiosk was cut from 13 to 9 materials for that reason. Merging is a content-side option measured in `DRAW_CALL_INVESTIGATION_2026-11-02.md`; engine batching is a separate proposal.

## Reuse and identity
`aro_` assets (drain, puddles) are GLOBAL. `rg_ngn_` assets are REGIONAL, northern Nigerian roadside, village and compound forms, not to be reused outside the region without a decision. The wet-season tree and grass are separate assets (the exported material is baked into the file) that record `seasonVariantOf` the dry-season assets; choosing between them by season is engine work (RB-24), not done.

## Preservation and verification status
* Code, tests (131 Blender-job tests), the frozen hash manifest and this document are in this repository.
* **Export verification (a Blender re-import, not Unity):** the latest export of all 26 environmental assets (FBX and GLB, 52 files) re-imports into a fresh Blender scene with its LOD meshes, materials and a bounding box equal to the validated size: 52 of 52 ok (`docs/kano/ENVIRONMENT_KIT_E3.export_verification.json`).
* The Blender sources, exports and previews are staged in the private asset repository's local branch `assets/environment-kit-v1` (Git LFS pointers). **It cannot be pushed until the environment allows `lfs.github.com`**, and a fresh-clone verification of every object will probably also need `github-cloud.githubusercontent.com` (both answer 403 from this environment today; see `INFRASTRUCTURE_INVESTIGATION_2026-11-02.md` section 5).
* The per-asset table of IDs, categories, regions, references, Blender files, exports and statuses is `ENVIRONMENT_LIBRARY_HANDOVER.md` (generated from the registry).
