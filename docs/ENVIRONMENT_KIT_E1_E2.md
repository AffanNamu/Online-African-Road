# Environmental kit, batches E1 and E2 (Blender content production, 2026-11-02)

**Status: Blender validation passed for all 16 assets; nothing is imported into Unity, nothing is verified in the game, nothing is approved.** This is content only. **No driving physics, road generation, weather, vehicle behaviour or production configuration was changed, and none of the engine proposals RB-18 to RB-25 was started** (see `ENVIRONMENT_SYSTEMS_ASSESSMENT.md`). The assets are real geometry built in Blender by `tools/blender/env_kit` through the same toolchain as the residential kit (the toolchain's validator, FBX and GLB export with a manifest); textures only support the geometry.

## What exists
| Asset | Class | Library category | References | Triangles per LOD | Size m (x y z) | Surface datum m |
|---|---|---|---|---|---|---|
| `aro_road_mud_rutted_a` | GLOBAL | road_mud_wet | R-020, R-021 | 6480 / 600 | 8.0 x 24.0 x 0.562 | 0.3225 |
| `aro_road_earth_compacted_a` | GLOBAL | road_earth_sand | R-016, R-020 | 6480 / 600 | 8.0 x 24.0 x 0.194 | 0.0628 |
| `aro_road_sand_loose_a` | GLOBAL | road_earth_sand | R-016, R-017 | 6480 / 600 | 8.0 x 24.0 x 0.289 | 0.1021 |
| `aro_road_asphalt_cracked_a` | GLOBAL | road_damaged | R-016, R-017 | 5376 / 384 | 7.0 x 24.0 x 0.067 | 0.0538 |
| `aro_road_pothole_set_a` | GLOBAL | road_damaged | R-016, R-017 | 6048 / 360 | 7.0 x 12.0 x 0.202 | 0.1918 |
| `aro_water_puddle_set_a` | GLOBAL | road_mud_wet | R-018, R-020, R-021 | 168 | 7.549 x 7.745 x 0.004 | 0.01 |
| `aro_water_flood_sheet_a` | GLOBAL | drainage_flooding | R-019 | 640 / 42 | 12.5 x 7.5 x 0.01 | -0.015 |
| `aro_drain_open_slab_a` | GLOBAL | drainage_flooding | R-016, R-019 | 282 / 12 | 1.1 x 6.0 x 0.95 | - |
| `rg_ngn_stall_umbrella_a` | REGIONAL | markets_roadside_businesses | R-016, R-017, R-018, R-019 | 1122 / 52 | 2.615 x 2.615 x 2.58 | - |
| `rg_ngn_stall_canopy_rough_a` | REGIONAL | markets_roadside_businesses | R-020, R-021 | 700 / 40 | 3.6 x 2.814 x 2.39 | - |
| `rg_ngn_fence_woven_timber_a` | REGIONAL | rural_settlements | R-020 | 1174 / 76 | 3.087 x 0.142 x 1.45 | - |
| `rg_ngn_shop_row_roadside_a` | REGIONAL | markets_roadside_businesses | R-016, R-017, R-018, R-019 | 1304 / 28 | 15.8 x 6.77 x 3.4 | - |
| `rg_ngn_house_mudbrick_a` | REGIONAL | rural_settlements | R-020, R-021 | 582 / 38 | 8.84 x 7.92 x 4.46 | - |
| `rg_ngn_hausa_compound_a` | REGIONAL | traditional_architecture | owner request, no reference | 2406 / 108 | 20.14 x 16.18 x 7.21 | - |
| `rg_ngn_tree_large_shade_a` | REGIONAL | vegetation_foliage | R-016, R-017, R-018, R-019, R-020, R-021 | 1404 / 213 / 22 | 11.763 x 11.414 x 11.868 | - |
| `rg_ngn_grass_tall_verge_a` | REGIONAL | vegetation_foliage | R-016, R-020, R-021 | 44 | 1.043 x 1.132 x 1.291 | - |

*Surface datum:* the validator wants an asset's lowest point on z = 0, so each ground patch is shifted until the bottom of its deepest rut or hole is z = 0; the nominal road surface (no ruts, holes or camber) then sits at this height above the origin, and the asset is placed that much lower. It is recorded as `surfaceDatumM` in the private registry.

## How the pieces map to the owner's scope of 2026-11-02
| Scope item | Built in E1/E2 | Not built yet |
|---|---|---|
| Traditional Kano residential architecture and Hausa compounds | one Hausa-style walled compound (zaure, parapets, zankwaye, relief) | further compound types (B, C), a compound wall and gate set, a neighbourhood mosque, old-city lanes; **all need real references (M-01, M-03, M-12)** |
| Rural settlements and villages | a mud-brick house, a woven timber fence, a rough canopy stall | more house types, granaries, a village layout, roofs as a kit |
| Markets, roadside shops, commercial areas | a three-unit roadside shop row, a market umbrella stall, a rough canopy stall | market sheds, kiosks, stalls as a set, signage, goods sets |
| Dry-season and Harmattan vegetation and environment | a dry shade tree, a dry grass clump | the wet-season variants, bougainvillea, banana, neem and date-palm forms (species NOT established, M-07, M-17); the Harmattan haze and dust film are **engine work (RB-20), not done** |
| Mud, unpaved, sandy and damaged roads | rutted mud, compacted earth, loose sand, cracked asphalt with repair patches, a pothole set | patched asphalt variants, edge failures, speed humps; **how these behave under a vehicle is engine work (RB-18, RB-19), not done** |
| Rainy-season assets, puddles, flood water | a puddle set and a flood sheet (geometry only) | the water shader, reflections, depth, flow, splash: **engine work (RB-21, RB-23), not done** |
| Roadside drainage and channels | a concrete open drain with slab covers | culverts, outlets, channels across roads (RB-05) |

## What the assets are not
* They are **not** a rainy-season or mud simulation. A mud road mesh does not make a vehicle slide; nothing in the game reads these surfaces.
* The vernacular forms (shop row, mud-brick house, stalls, fence) and the soil, mud and sand colours are **not verified against real references** (gaps M-14 and M-15). The generated reference images are design targets, not evidence.
* The compound has **no real reference at all** (M-01): it is a design interpretation of the qualitative features recorded in `docs/kano/RESEARCH.md`, every proportion is a design choice, no landmark is depicted, and it must be checked before approval.
* Water assets are flat sheets: no reflection, depth or flow until the water work (RB-21) is approved and built.
* Potholes are real depressions but their outlines are stair-stepped at 0.17 m cells (the 8,000-triangle budget limits the grid); a decal and normal-map layer is the proper answer (RB-06).

## Reuse and identity
`aro_` assets (road and water patches, the drain) are GLOBAL: neutral and usable in any city; the data decides where each applies. `rg_ngn_` assets (stalls, fence, shop row, house, compound, dry tree and grass) are REGIONAL, northern Nigerian roadside and village forms, not to be reused outside the region without a decision; none is CITY_SPECIFIC or a LANDMARK. A variation (colour, goods, state) is a parameter of an existing asset, not a new model.

## Preservation and verification status
* Code, tests (95 Blender-job tests), the freeze manifest with 160 file hashes (`docs/kano/ENVIRONMENT_KIT_E1_E2_v001.freeze.json`) and this document are in this repository.
* The Blender sources, exports and previews are staged in the private asset repository's local branch `assets/environment-kit-v1` (108 Git LFS pointers; registry status `blender_validation_passed`, every open difference in the entry's `flags`). **It cannot be pushed until the environment allows `lfs.github.com`** (`docs/INFRASTRUCTURE_INVESTIGATION_2026-11-02.md`, section 5).
* Unity import and in-game review of this kit: **not done**; the same opt-in review mechanism as the residential kit can be pointed at it once the residential review is complete.
