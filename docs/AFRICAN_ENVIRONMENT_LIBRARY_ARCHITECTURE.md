# Architecture of the reusable African environment library (2026-11-07)

**Goal (owner, 2026-11-07):** a reusable library of African environments, not a Kano demo. Kano is the first city and northern Nigeria (`ngn`) the first region; everything below is written so that Lagos, Ibadan, Abuja, Accra or a Sahel route can be added without redoing what exists. Nothing here changes the game engine; the engine-dependent items are the proposals RB-01 to RB-25 and are **not** implemented.

## 1. Asset classes and names (one rule, in the registry)
| Class | Prefix | Meaning | Examples today |
|---|---|---|---|
| GLOBAL | `aro_` | usable in any country: street furniture, road parts, generic buildings, drains, puddles | `aro_villa_2storey_hip_a`, `aro_streetlight_single_arm_a`, `aro_drain_open_slab_a` |
| REGIONAL | `rg_<region>_` | a regional vernacular or ecology: vegetation, roofs, shops, compounds | `rg_ngn_tree_street_broadleaf_a`, `rg_ngn_shop_row_roadside_a`, `rg_ngn_hausa_compound_a` |
| CITY_SPECIFIC | `<city>_` | one city's own street or district | `kano_street_oneway_section_a` |
| LANDMARK | `lmk_<city>_` | a real, referenced landmark | none built (no reference exists, M-03, M-11) |
The unified registry (`registry/asset_registry.json` of the private repository) holds 137 entries: 93 GLOBAL, 28 REGIONAL, 10 CITY_SPECIFIC, 6 LANDMARK. Reuse is the default: a city asset is only made when no global or regional asset can serve.

## 2. Library categories (the browsable shelves)
`traditional_architecture`, `modern_architecture`, `rural_settlements`, `markets_roadside_businesses`, `boundaries_walls_gates`, `road_structure_signs_lighting`, `vegetation_foliage`, `site_props_vehicles`, `terrain_landforms`, `road_asphalt`, `road_damaged`, `road_earth_sand`, `road_mud_wet`, `drainage_flooding`, `effects_dust_rain_mud_water` (each with a description and an honest note of what is established). An asset has exactly one `library`. A new region adds assets to the same shelves; a new shelf is added only when the owner asks.

## 3. One chain per asset, whatever the region
reference -> Blender source -> exported files -> Unity prefab -> validation -> in-game review (`docs/ASSET_RELEASE_CHAIN.md`, checked by `tools/release_chain.py`). **A reference first**: the reference register (`docs/kano/reference_register.json`) names what exists and what is missing (gaps M-01...). Where no reference exists (the Hausa compounds, M-01) the asset is a flagged design interpretation and stays "open" at the reference link; nothing is invented as fact and no landmark is depicted without a reference.

## 4. Storage and versions
* **Private repository `AffanNamu/ARO-CONTENT`** is the archive of record: Blender sources, exports, textures and previews as **Git LFS** objects; registry, licences and reference metadata as text. The public game repository holds code, tests, frozen hash manifests and documents only.
* **Append-only versions** (`vNNN`): a released version is never edited; a correction is a new version with its own frozen manifest (`docs/kano/*.freeze.json`: SHA-256 of every file and the commit of the producing code), Blender validation, Unity import record, screenshots and performance report. Today: residential v001, v002 (frozen), v003; environmental kit E1 to E3.
* **Verified preservation**: every push is checked through a fresh clone (`tools/verify_asset_clone.py`); in-game pictures are preserved under `previews/in_game_review/<date>/` because the CI artifacts expire.
* **Unified (2026-11-08, owner-approved):** `main` of the private repository is the one authoritative library: one combined registry (137 entries: 93 GLOBAL, 28 REGIONAL, 10 CITY_SPECIFIC, 6 LANDMARK), every version of every asset, the unapproved prototypes kept apart, the historical branches kept untouched as release snapshots (`docs/LIBRARY_UNIFICATION_2026-11-08.md`).

## 5. Designed-in performance rules (so a library of hundreds of assets stays inside the budget)
The cost of an asset instance is its **material count** (the game batches nothing: each material is a draw call per instance) plus a shadow pass (about 44% of engine draws in the measurements). Rules every new asset follows: few materials (v003: 7 per villa instead of 11; shared palette materials for flat colours, one vegetation atlas); an LOD ladder (LOD0 to LOD2, switched by the game's LodPolicy); merged plot kits instead of many small parts where a frontage repeats (merged frontages cut the review view's draws by about 30%); no overlapping coplanar faces (regression-tested); a lower-detail mobile tier is a separate content variant (measured 447 and 415 draws against a mobile budget of 150: not met today). Engine batching (RB-15) is a separate proposal that needs the owner's approval.

## 6. Adding a region or a city (checklist)
1. Register the region/city in the registry (`regions`, `cities`); keep prefixes.
2. Add references to the register (or record the gaps); write what is *not* known.
3. For each needed asset: reuse a GLOBAL/REGIONAL asset if one fits; otherwise register a new one with class, library shelf, concept ids, budget and engine dependencies.
4. Produce through the chain; dry/wet or seasonal variants are separate registered assets or variants (static meshes; the engine has no seasonal or mud physics, RB-18 to RB-25 are proposals).
5. Compose complete review scenes, not isolated assets, and review them in the running game before anything is called approved. Only the owner sets `production_approved`.
