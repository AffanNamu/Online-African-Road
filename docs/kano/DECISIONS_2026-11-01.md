# Kano: owner decisions of 2026-11-01 (execution decision) and how each one is applied

Source: the owner's "EXECUTION DECISION — KANO CITY MASTER PLAN AND 3D PRODUCTION" message. This file records the decisions and what was done about each; it changes none of them.

## A. Master plan
| Decision | Applied how |
|---|---|
| The 100 km² study area is approved **as a planning boundary** | `KANO_CITY_MASTER_PLAN.md` section 1 unchanged; nothing is built from it except the first district |
| The 54-junction, 83-road network stays a **planning target**, subject to geographical verification and engine feasibility | marked indicative everywhere; the engine feasibility is the proposal `ROAD_ENGINE_PROPOSAL.md` |
| **Not** permission to build the whole city at once; progressive, district by district | first production target is one residential street section (D06), see `KANO_RESIDENTIAL_MILESTONE.md` on the production branch |
| The 1.65 km corridor stays a road-system test (TC-1) | unchanged |

## B. Four kinds of information, kept apart
Nothing generated in a concept (text, distances, names, logos, landmarks) is treated as verified. Every element of a concept is classified as exactly one of:
1. **Visual design requirement**: what the game must look like (proportions, materials, layout, mood). Reproduced as geometry.
2. **Verified geographical fact**: only what a source verifies (the GeoNames anchor, Gidan Rumfa within about 300 m, route distances from road data). Never taken from a concept.
3. **Fictional architectural concept**: invented buildings and layouts of the concepts (the villas, the terminal, the hospital). Reproduced, labelled fictional.
4. **Actual Kano landmark**: a real place (Gidan Rumfa, the Central Mosque, Kurmi Market, Dala Hill). Built only from authentic references; none exists yet.
For concept R-006 (the first milestone) the classification is in `KANO_RESIDENTIAL_MILESTONE.md`. The asset registry keeps `flags` per asset.

## C. Decisions on the flagged concepts
| # | Decision | What is done |
|---|---|---|
| 1 | The teal-dome twin-minaret mosque is a **generic mosque** until an authentic reference exists; never labelled the Central Mosque | when it is built it is registered as a generic regional mosque asset (`rg_ngn_...`), never as `lmk_kano_great_mosque`; the landmark stays BLOCKED on M-11; no mosque is in the residential milestone (nothing registered yet) |
| 2 | Hospital name and police emblem are replaced by **fictional game identities** | when the R-010 and R-011 assets are built they carry a fictional name and a generic crest (to be designed, the names submitted for the owner's review); no real insignia is reproduced (not started) |
| 3 | The expressway's hills are **not** put into Kano's urban landscape; the concept is kept for a regional highway environment | D11 will be built on flat ground by default; RB-14 is for a regional highway environment, not for Kano (not started) |
| 4 | Road signs use **verified route information**; no incorrect distances or unverified route names are modelled permanently | the gantry sign will be built with a text slot (RB-07, not started); the text comes from verified route data (Kano to Zaria about 155 to 165 km, Kano to Kaduna about 230 km by road, A2 as the federal designation). It is not the concept's 138 km / 262 km / "Express Route" |
| 5 | Close-up vehicles keep **visual detail**: LODs and distance-based optimisation, not primitive vehicles | the vehicle triangle ceiling is to be handled as a separate vehicle class with its own LOD ladder (hero LOD0 within the existing `VehicleAssetValidator` hero budget of 60,000 triangles, LOD chain down to 10%); the 2,500-triangle prop ceiling applies to props only. Vehicle physics and gameplay are untouched |
| 6 | Resolve the world-map coordinate discrepancy from **verified control points**; do not arbitrarily move the city | see below |

### C6: the coordinate discrepancy, resolved without moving anything
* **Control points** (`COORDINATES.md`): GeoNames Kano 12.00012° N, 8.51672° E (id 2335204; Wikipedia agrees to 13 m), Gidan Rumfa about 11.9923° N, 8.5160° E (three listings within 300 m), Dala Hill 12.0093° N, 8.5070° E (one source). The city frame **K0** is anchored on the first and checked against the other two.
* **The world-map pin** (12.0022° N, 8.592° E) is 8.2 km east of the anchor, has no recorded source, and all eight Nigerian and Nigerien pins differ from GeoNames by 1.7 to 10.8 km: it is a strategic-map marker, not a survey point.
* **Resolution:** the two are kept as two frames. **The city (K0) uses the verified anchor.** The strategic-map pin is not used for any city geometry. Nothing was moved.
* **If the owner wants the map pins corrected**, all eight are corrected from GeoNames in **one reviewed change**: `world_map.json`, `supabase/seed_world.sql` (a data migration on the live database, which needs the owner's explicit authorisation), `get_world_state.json` (the API sample) and the Haversine distances of the map. No such change has been made, prepared as a migration, or applied.

## D. Engine development
The owner asked for a technical proposal **before** any work on RB-12 to RB-17: `ROAD_ENGINE_PROPOSAL.md`. No engine file was changed and nothing from RB-12 to RB-17 was implemented. The proposal asks for approval of stage E0 (measurement only) and a separate decision for each later stage.

## E, F. Production and visual approval
The first Blender production milestone was started: the one-way residential street of concept R-006. What exists, what passed and what is still open is in `KANO_RESIDENTIAL_MILESTONE.md` (production branch `work/kano-residential`).

## G. Repository and CI
* The locally committed Kano work was preserved in the private repository as a text patch series: branch `backup/kano-slice-2026-11-01` of `AffanNamu/ARO-CONTENT` (scanned for secrets and binaries; no reference image; eight small planning diagrams inside one patch).
* The camera branch (`work/blender-camera`, CI green) and its database-readiness fix stay separate and unmerged. The production branch `work/kano-residential` is stacked on it and merges nothing.
