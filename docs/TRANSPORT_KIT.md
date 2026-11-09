# Transport kit: asset families A, B, C and D (built 2026-11-09)

**Authorised by the owner on 2026-11-09** (asset families A logistics, B bus terminals, C road furniture; reusable assets first; family D, which "follows E5", was built in the same session as a reuse-first subset (section 3b); GLOBAL / REGIONAL / CITY_SPECIFIC / LANDMARK classes kept; family D follows E5; family E on hold, see `KANO_FAMILY_E_BREAKDOWN.md`). The approved rows are in `KANO_TRANSPORT_ASSET_BREAKDOWN.md`; this document says what was actually built and what is and is not proven.

## 1. Verification levels (what is true today)

| Level | Result |
|---|---|
| 1. Implemented | **64 asset ids** (family A 21, B 8, C 23, D 12) built by parametric Blender code (`tools/blender/transport_kit/`), four review scenes (`scenes.py`), Unity staging (`stage_unity.py`), the review-workflow steps. All 64 export to FBX and GLB with a manifest. The `aro_bus_bay_marking_set` row is road-plate data (E2 marks), not a mesh: no model was made. |
| 2. Automated tests passed | `tests/test_transport_kit.py` (every asset passes the toolchain validator, at most 4 materials, three strictly decreasing LODs with the last at most 10% of LOD0, the game's own triangle budgets, the breakdown's expected triangle counts (no row exceeded by more than 25%), sizes against the approved rows apart from the documented deviations, light heads and text faces on the model, colliders on the model, classification prefixes, no brand names, vehicle clearances) and `tests/test_transport_scenes.py` (the review scenes): **94 kit and scene tests pass**; the whole Blender suite passed (251 tests) before families D and the fourth scene were added. |
| 3. Imported into Unity | **pending.** The review workflow (`blender-unity.yml`, a `[kano-review]` commit) now builds the 52 assets from the code, stages them with the three scenes and takes 12 pictures; **it has not been run**. The Unity licence queue was busy (CI runs 325 and 326) when this was written. |
| 4. Verified in actual gameplay | **no.** Nothing in the game places these assets. The shipped `prop_catalog.json` and the routes are unchanged. |
| 5. Visually reviewed | **no.** I (the agent) looked at Blender renders of the three scenes and of single assets (Cycles, CPU) and corrected the one thing they showed (fully metallic steel rendering black); that is not the owner's review. **Nothing is approved; `production_approved` is the owner's alone.** |

## 2. How the kit is built

* **Geometry**: `kano_kit.geo.Part` primitives (boxes, tapered boxes, tubes, prisms), real metres, front at -Y, origin on the ground at the footprint centre (poles at the foot of the pole by design: the arm reaches over the road). Every function takes the LOD (0 detailed, 1 medium, 2 a few boxes).
* **Four materials at most** (the approved breakdown's rule). Flat colours are folded into three palette materials, `tk_paint`, `tk_metal` and `tk_glass` (`transport_kit/palette.py`, the same mechanism as residential v003's palettes: each face's UV is the centre of a constant-colour cell, so nothing tiles or blurs); the textured `concrete` stays separate. Compromise, as in v003: one roughness per family. **A render showed fully metallic steel nearly black against a plain sky, so the kit's steel is 0.55 metallic, not 0.8** (first-pass values, to be judged in the game).
* **Generic, no brand**: liveries are plain colours, boards are blank (the game draws the text through the catalogue `label`), signs carry shapes and numerals only. The ids contain no brand (a test checks).
* **Catalogue extras** (`transport_kit/common.py`): lamp heads (the game's `LightSpec`), text faces (`LabelSpec`) and tags are added to each asset's measured catalogue entry in its export manifest, and all entries are written to **`docs/kano/TRANSPORT_KIT_catalog.json`** (with measured bounds and the gameplay sockets). It is **not merged into the shipped `prop_catalog.json`**: that is a change to the game's data and waits for the Unity review and the owner.
* **Rebuild**: `cd tools/blender && PYTHONPATH=. ~/.aro/venv/bin/python -m transport_kit.build_transport [--only id,id] [--family A|B|C] [--no-export] [--report file] [--catalog file]` (exports go to the next version of each asset in the workspace); previews: `-m transport_kit.render_tk --out DIR`; scene compositions: `-m transport_kit.render_scenes --out DIR`.

## 3. The assets

Triangles are LOD0/LOD1/LOD2; colliders are `col_*` boxes (the Unity builder makes one BoxCollider per box); sockets are gameplay interaction points (data only: nothing in the game reads them, section 5).

### A. Logistics and warehouse site

| Asset id | Class | Built size x/y/z (m) | Breakdown size | Tris LOD0/1/2 | Mats | Colliders | Light heads | Text face | Sockets |
|---|---|---|---|---|---|---|---|---|---|
| `rg_ngn_warehouse_hall_a` | REGIONAL | 49.0 x 24.92 x 10.1 | 48 x 24 x 10 | 1450/396/58 | 4 | 2 | glow x6 | yes | 6 |
| `rg_ngn_warehouse_hall_b` | REGIONAL | 36.4 x 21.04 x 8.0 | 36 x 20 x 8 | 1048/370/70 | 4 | 1 | glow x4 | yes | 4 |
| `rg_ngn_warehouse_cold_a` | REGIONAL | 30.66 x 18.02 x 9.97 | 30 x 18 x 9 | 1294/244/40 | 3 | 1 | glow x3 | - | 3 |
| `aro_dock_leveller_set` | GLOBAL | 3.5 x 1.47 x 4.4 | 3.5 x 1.2 x 4.4 | 348/64/30 | 3 | 1 | - | - | 1 |
| `aro_container_20ft_a` | GLOBAL | 6.07 x 2.44 x 2.59 | 6.1 x 2.4 x 2.6 | 322/82/10 | 2 | 1 | - | - | 1 |
| `aro_container_20ft_b` | GLOBAL | 6.07 x 2.44 x 2.59 | 6.1 x 2.4 x 2.6 | 322/82/10 | 2 | 1 | - | - | 1 |
| `aro_container_20ft_c` | GLOBAL | 6.07 x 2.44 x 2.59 | 6.1 x 2.4 x 2.6 | 322/82/10 | 2 | 1 | - | - | 1 |
| `aro_container_40ft_a` | GLOBAL | 12.2 x 2.44 x 2.59 | 12.2 x 2.4 x 2.6 | 322/82/10 | 2 | 1 | - | - | 1 |
| `aro_container_40ft_b` | GLOBAL | 12.2 x 2.44 x 2.59 | 12.2 x 2.4 x 2.6 | 322/82/10 | 2 | 1 | - | - | 1 |
| `aro_container_stack_3` | GLOBAL | 12.21 x 2.44 x 7.8 | 12.2 x 2.4 x 7.8 | 754/274/34 | 2 | 1 | - | - | 0 |
| `aro_pallet_stack_a` | GLOBAL | 2.7 x 2.12 x 1.83 | 3 x 2 x 1.8 | 1102/160/88 | 1 | 1 | - | - | 1 |
| `aro_gate_sliding_a` | GLOBAL | 16.85 x 4.0 x 3.12 | 11 x 1.2 x 2.6 | 918/298/54 | 4 | 3 | glow x1 | yes | 3 |
| `aro_fence_palisade_set` | GLOBAL | 4.02 x 0.12 x 2.55 | 4 x 0.1 x 2.4 | 188/98/12 | 1 | 1 | - | - | 0 |
| `aro_wall_plot_set` | GLOBAL | 4.02 x 0.28 x 2.48 | 4 x 0.25 x 2.4 | 186/30/12 | 2 | 1 | - | - | 0 |
| `aro_weighbridge_a` | GLOBAL | 19.34 x 6.72 x 3.3 | 18 x 3.6 x 3.2 | 528/82/30 | 4 | 1 | glow x1 | yes | 1 |
| `aro_fuel_island_truck` | GLOBAL | 16.0 x 8.02 x 6.5 | 16 x 8 x 6.5 | 1034/174/86 | 3 | 6 | lamp x4 | yes | 2 |
| `aro_forklift_a` | GLOBAL | 1.3 x 3.38 x 2.38 | 3.2 x 1.2 x 2.2 | 794/286/22 | 2 | 1 | - | - | 0 |
| `aro_truck_trailer_parked_a` | GLOBAL | 2.56 x 13.0 x 2.7 | 13 x 2.5 x 4 | 572/240/42 | 2 | 2 | - | - | 1 |
| `aro_truck_trailer_parked_b` | GLOBAL | 2.58 x 13.0 x 4.0 | 13 x 2.5 x 4 | 576/160/32 | 2 | 1 | - | - | 1 |
| `aro_yard_mast_light` | GLOBAL | 1.9 x 1.9 x 19.9 | 1.2 x 1.2 x 20 | 612/236/44 | 3 | 1 | lamp x4 | - | 0 |
| `aro_bollard_set` | GLOBAL | 0.3 x 0.3 x 1.0 | 0.3 x 0.3 x 1 | 150/76/12 | 3 | 1 | - | - | 0 |

### B. Bus terminals and stops

| Asset id | Class | Built size x/y/z (m) | Breakdown size | Tris LOD0/1/2 | Mats | Colliders | Light heads | Text face | Sockets |
|---|---|---|---|---|---|---|---|---|---|
| `kano_bus_terminal_hall_a` | CITY_SPECIFIC | 40.6 x 22.3 x 10.5 | 40 x 22 x 9 | 2388/364/60 | 4 | 9 | glow x8 | yes | 4 |
| `kano_bus_shelter_long_a` | CITY_SPECIFIC | 18.1 x 4.1 x 4.2 | 18 x 4 x 4 | 1042/442/24 | 3 | 0 | glow x3 | yes | 3 |
| `aro_bus_shelter_a` | GLOBAL | 5.67 x 2.04 x 3.04 | 5 x 2 x 3 | 346/164/22 | 4 | 0 | glow x1 | yes | 1 |
| `kano_ticket_kiosk_a` | CITY_SPECIFIC | 2.5 x 3.38 x 2.8 | 3 x 3 x 2.8 | 200/52/20 | 4 | 1 | glow x1 | yes | 1 |
| `kano_departure_board_a` | CITY_SPECIFIC | 3.0 x 0.4 x 4.7 | 3 x 0.3 x 4.5 | 462/118/26 | 3 | 2 | glow x1 | yes | 0 |
| `kano_minibus_stand_canopy_a` | CITY_SPECIFIC | 12.0 x 6.0 x 3.66 | 12 x 6 x 3.6 | 872/292/24 | 2 | 1 | glow x2 | yes | 3 |
| `kano_tricycle_rank_a` | CITY_SPECIFIC | 8.5 x 5.0 x 2.96 | 8 x 5 x 3 | 388/188/22 | 2 | 0 | glow x1 | - | 2 |
| `aro_parking_bay_barrier_set` | GLOBAL | 2.4 x 0.72 x 0.68 | 2.5 x 0.4 x 0.6 | 156/92/12 | 2 | 1 | - | - | 0 |

### C. Road furniture and street kit

| Asset id | Class | Built size x/y/z (m) | Breakdown size | Tris LOD0/1/2 | Mats | Colliders | Light heads | Text face | Sockets |
|---|---|---|---|---|---|---|---|---|---|
| `aro_streetlight_arm_single_a` | GLOBAL | 0.48 x 3.29 x 9.37 | 4.4 x 0.6 x 9 | 356/106/34 | 2 | 0 | lamp x1 | - | 0 |
| `aro_streetlight_double_median_a` | GLOBAL | 5.2 x 0.9 x 12.17 | 6 x 0.6 x 12 | 542/132/44 | 3 | 0 | lamp x2 | - | 0 |
| `aro_streetlight_ornamental_a` | GLOBAL | 0.93 x 0.8 x 6.1 | 1 x 1 x 6 | 358/150/20 | 3 | 0 | lamp x1 | - | 0 |
| `aro_road_sign_stop_a` | GLOBAL | 0.76 x 0.11 x 2.93 | 0.8 x 0.1 x 3 | 402/100/20 | 2 | 0 | - | - | 0 |
| `aro_road_sign_yield_a` | GLOBAL | 0.94 x 0.13 x 2.93 | 0.8 x 0.1 x 3 | 310/56/20 | 2 | 0 | - | - | 0 |
| `aro_road_sign_speed50_a` | GLOBAL | 0.72 x 0.12 x 2.93 | 0.8 x 0.1 x 3 | 546/200/20 | 2 | 0 | - | - | 0 |
| `aro_road_sign_speed80_a` | GLOBAL | 0.72 x 0.12 x 2.93 | 0.8 x 0.1 x 3 | 570/200/20 | 2 | 0 | - | - | 0 |
| `aro_road_sign_no_entry_a` | GLOBAL | 0.72 x 0.12 x 2.93 | 0.8 x 0.1 x 3 | 390/160/20 | 2 | 0 | - | - | 0 |
| `aro_road_sign_bus_stop_a` | GLOBAL | 0.6 x 0.13 x 2.93 | 0.8 x 0.1 x 3 | 336/74/20 | 2 | 0 | - | - | 0 |
| `aro_road_sign_zebra_a` | GLOBAL | 0.6 x 0.14 x 2.93 | 0.8 x 0.1 x 3 | 316/80/20 | 2 | 0 | - | - | 0 |
| `aro_road_sign_no_left_turn_a` | GLOBAL | 0.72 x 0.13 x 2.93 | 0.8 x 0.1 x 3 | 506/224/20 | 2 | 0 | - | - | 0 |
| `aro_road_sign_direction_a` | GLOBAL | 3.0 x 0.16 x 3.95 | 3 x 0.3 x 4 | 308/110/26 | 2 | 0 | - | yes | 0 |
| `aro_gantry_sign_a` | GLOBAL | 25.0 x 1.0 x 7.0 | 20 x 1 x 7 | 1008/284/64 | 3 | 2 | - | yes | 0 |
| `aro_traffic_signal_a` | GLOBAL | 0.6 x 4.48 x 6.0 | 5 x 0.4 x 6 | 462/226/28 | 3 | 0 | glow x2 | - | 0 |
| `aro_guardrail_post_set` | GLOBAL | 4.1 x 0.26 x 0.9 | 4 x 0.3 x 0.8 | 150/88/12 | 2 | 1 | - | - | 0 |
| `aro_jersey_barrier_a` | GLOBAL | 3.0 x 0.64 x 0.8 | 3 x 0.6 x 0.8 | 124/52/12 | 2 | 1 | - | - | 0 |
| `aro_kerb_painted_run_a` | GLOBAL | 2.0 x 0.26 x 0.15 | 2 x 0.25 x 0.15 | 100/40/10 | 2 | 1 | - | - | 0 |
| `aro_drain_grate_set` | GLOBAL | 1.2 x 0.7 x 0.04 | 0.6 x 0.6 x 0.03 | 270/128/12 | 2 | 0 | - | - | 0 |
| `aro_bench_a` | GLOBAL | 1.84 x 0.6 x 0.9 | 1.8 x 0.6 x 0.9 | 308/100/12 | 3 | 1 | - | - | 0 |
| `aro_litter_bin_a` | GLOBAL | 0.5 x 0.52 x 0.9 | 0.5 x 0.5 x 0.9 | 190/66/12 | 2 | 1 | - | - | 0 |
| `aro_planter_island_a` | GLOBAL | 4.08 x 2.01 x 0.84 | 4 x 2 x 0.6 | 218/90/12 | 2 | 1 | - | - | 0 |
| `aro_planter_island_b` | GLOBAL | 8.08 x 2.01 x 0.84 | 8 x 2 x 0.6 | 378/130/12 | 2 | 1 | - | - | 0 |
| `kano_utility_pole_set` | CITY_SPECIFIC | 14.0 x 1.08 x 11.0 | 0.4 x 0.4 x 11 | 434/272/20 | 3 | 0 | - | - | 0 |

### D. Roundabout, island and flyover components (a subset: trees, shrubs and hedges are reused, not rebuilt; `KANO_FAMILY_D_PLAN.md`)

| Asset id | Class | Built size x/y/z (m) | Breakdown size | Tris LOD0/1/2 | Mats | Colliders | Light heads | Text face | Sockets |
|---|---|---|---|---|---|---|---|---|---|
| `aro_roundabout_fountain_a` | GLOBAL | 7.8 x 7.8 x 1.95 | 8 x 8 x 1.6 | 978/300/28 | 3 | 1 | glow x4 | - | 0 |
| `rg_ngn_island_wall_a` | REGIONAL | 3.04 x 0.48 x 1.28 | 3 x 0.4 x 1.2 | 242/30/12 | 2 | 1 | - | - | 0 |
| `rg_ngn_pattern_panel_a` | REGIONAL | 3.1 x 0.37 x 1.4 | 3 x 0.3 x 1.4 | 374/34/12 | 1 | 1 | - | - | 0 |
| `rg_ngn_pattern_panel_b` | REGIONAL | 3.1 x 0.35 x 1.4 | 3 x 0.3 x 1.4 | 426/34/12 | 1 | 1 | - | - | 0 |
| `rg_ngn_pattern_panel_c` | REGIONAL | 3.0 x 0.4 x 1.4 | 3 x 0.3 x 1.4 | 302/34/12 | 1 | 1 | - | - | 0 |
| `aro_flyover_deck_segment_a` | GLOBAL | 12.0 x 24.16 x 1.62 | 12 x 24 x 1.6 | 808/68/44 | 3 | 0 | lamp x6 | - | 0 |
| `aro_flyover_pier_a` | GLOBAL | 2.5 x 2.5 x 7.0 | 2.4 x 2.4 x 7 | 280/76/24 | 2 | 1 | - | - | 0 |
| `aro_flyover_pier_b` | GLOBAL | 2.4 x 2.4 x 8.0 | 2.4 x 2.4 x 8 | 274/38/24 | 2 | 1 | - | - | 0 |
| `rg_ngn_pier_panel_a` | REGIONAL | 3.0 x 0.35 x 6.0 | 3 x 0.3 x 6 | 582/18/10 | 1 | 1 | - | - | 0 |
| `aro_deck_edge_light_a` | GLOBAL | 0.4 x 0.32 x 0.6 | 0.4 x 0.3 x 0.6 | 194/30/12 | 2 | 0 | lamp x1 | - | 0 |
| `aro_noise_wall_set` | GLOBAL | 4.39 x 0.34 x 3.0 | 4 x 0.3 x 3 | 186/50/12 | 4 | 1 | - | - | 0 |
| `lmk_kano_roundabout_monument_a` | LANDMARK | 10.9 x 10.9 x 22.0 | 12 x 12 x 22 | 1788/186/22 | 3 | 2 | glow x6 | yes | 0 |

The **monument** (`lmk_kano_roundabout_monument_a`) and the **pattern and lattice panels** are **fictional designed interpretations** (the owner's defaults of 2026-11-09; the documentary sources R-024 to R-029 only; no real Kano monument or pattern is claimed; every proportion is a design choice). The engine draws the ring, the islands, the deck and its piers; these assets are dressing on top. **Nothing places them yet**: no island rule or route uses them; the E5 test routes still carry the game's grey placeholder props.

Total LOD0 triangles of all 64 assets: 34,034. Every asset is far below its category budget (props 2,500, buildings 6,000, signs 800, lights 1,200). **The kit's cost in the game is not measured** (no draw-call or frame-time measurement exists: that needs the Unity review run).

## 4. Deviations from the approved rows (all intended, listed so the owner can overrule)

* **Triangles**: nothing exceeds its row by more than 25%. Most assets are *simpler* than the row: the three warehouses (1,000 to 1,450 against 4,600 to 5,200), the terminal hall (2,388 against 5,600), the fuel island (1,034 against 2,400) and others. More detail is possible if the in-game review asks for it; each added detail must keep the last LOD at 10% of LOD0 (a toolchain rule that forces single LOD2 boxes). The **road signs are heavier than the 150 triangles of their row** (300 to 570 each, with the real lettering and a real post, against a 800 triangle budget) because the LOD rule needs a detailed LOD0 for a 20-triangle LOD2; the drain set is 270 against a 40-triangle decal quad because it models bars over a sump.
* **Sizes** (test `DEVIATION` table): the **two-lane gate** is 16.9 m wide (a 9 m opening and 9 m leaves cannot fit in the row's 11 m: two 3.5 m lanes around a 2 m guard island, the leaves open beside the posts); the **weighbridge** is 19.3 x 6.7 m (the row's 3.6 m is the platform alone); the **gantry** spans 24 m between the legs (a 2 x 3 lane carriageway with a median is not 20 m), so it is registered as kind `environments` (the validator's prop limit is 20 m); the **utility pole** includes 6 m of cable each side; the forklift is built with its length along Y; the flatbed trailer is 2.7 m tall with its headboard; the single-arm street light's arm is 2.5 m from the pole.
* **"Sets" realised as single ids**: the row `aro_road_sign_post_set` became eight single-placement signs (`aro_road_sign_stop_a`, `yield`, `speed50`, `speed80`, `no_entry`, `bus_stop`, `zebra`, `no_left_turn`) because a row of eight signs on one prefab cannot be placed one at a time. The fence, wall, guardrail, jersey barrier, kerb and parking-bay barrier rows stay one tileable module each.
* **One text face per prop**: the catalogue supports one `label` per prop. The gantry has two sign panels: the left carries the text, the right is blank green. A second text face is engine work.
* **Traffic signal**: the catalogue light has one colour; only the **red** aspect is lit (a static red phase). Real signal timing is not built.

## 5. Gameplay support (family A and B): what exists and what does not

The owner asked that logistics sites support **enter, collect, park, load, unload, deliver** and that bus sites respect real bus dimensions. This is what the models and data provide; **no gameplay system was changed or added** (CLAUDE.md rule), so none of these verbs exists in the game yet.

| Need | In the kit |
|---|---|
| Enter the yard | two 3.5 m lanes through the gate (`aro_gate_sliding_a`), colliders only on the hut and the two open leaves (a test checks that no collider blocks a lane); sockets `lane_in`, `lane_out`, `check` |
| Weigh | `aro_weighbridge_a`: a drive-over deck 0.12 m high with **no collider** (the cabin has one, off the deck); socket `weigh` |
| Park / hitch | `aro_truck_trailer_parked_a/b` (sockets `hitch`), parking bays on the E2 site plate |
| Load and unload | docks at the truck-bed height (1.2 m) on the halls and `aro_dock_leveller_set` (socket `truck_dock_rear` at each door); containers (socket `container_lift_point`); pallets (socket `cargo_pickup`) |
| Fuel | `aro_fuel_island_truck`: canopy clear height 5.8 m (a truck is 4.2 m), centre kept free of colliders; sockets `truck_fuel_stop` |
| Bus bays and boarding | `BAY_W` 3.6 x `BAY_D` 13.5 m against a 12 x 2.55 x 3.5 m coach (a test checks the margins); arrivals canopy clearance 5.0 m; long platform canopy for three bays; sockets `bus_bay_stop`, `passenger_board`, `ticket_queue`, `minibus_load`, `tricycle_queue` |
| Turning and reversing | **not tested here.** The swept-path numbers of E5 (`ROAD_ENGINE_E5.md`) cover the roads, not these yards: yard geometry is a placement decision on the E2 site plate, and a bus-terminal turning test needs a layout. |

## 5b. Overlap with the library plan (found while registering the kit: please decide)

The approved breakdown (`KANO_TRANSPORT_ASSET_BREAKDOWN.md`) was written from the transport plan and uses its own ids. The **asset registry of the private library** (`ARO-CONTENT`, 137 entries from the master plan and the earlier kits) was **not checked against it when the kit was designed**, and it already lists, as planned or built, many of the same things under other ids. "Reuse before create" was therefore not fully honoured: 23 of the 52 new ids overlap another entry, one of them (`aro_streetlight_arm_single_a`) duplicates an asset that is **already built and imported** (`aro_streetlight_single_arm_a`, residential v003), and two ids are identical to planned entries. Nothing was merged or dropped: each overlapping entry carries `overlapsPlanned` and an OPEN flag in the registry, and the **owner decides which id the game uses** (the sensible default is the built or planned library id where its design is the same, and this kit's asset where it is better or already approved by the breakdown).

| New id (approved breakdown row) | Planned or built registry id(s) for the same thing |
|---|---|
| `rg_ngn_warehouse_hall_a` | `aro_warehouse_steel_large_a` |
| `aro_dock_leveller_set` | `aro_dock_door_bay_a` |
| `aro_gate_sliding_a` | `aro_gate_sliding_steel_a`, `aro_barrier_arm_gate_a` |
| `aro_fuel_island_truck` | `aro_fuel_canopy_truck_a`, `aro_fuel_pump_a` |
| `aro_truck_trailer_parked_b` | `aro_trailer_box_a` |
| `aro_yard_mast_light` | `aro_streetlight_pole_tall_yard_a` |
| `aro_bollard_set` | `aro_bollard_yellow_a` |
| `aro_wall_plot_set` | `aro_wall_perimeter_plain_a` |
| `kano_bus_terminal_hall_a` | `aro_bus_terminal_hall_a`, `kano_bus_terminal_a` |
| `kano_bus_shelter_long_a` | `aro_canopy_bus_bay_solar_a` |
| `aro_streetlight_arm_single_a` | `aro_streetlight_single_arm_a` |
| `aro_streetlight_double_median_a` | `aro_streetlight_double_arm_median_a` |
| `aro_gantry_sign_a` | `aro_gantry_truss_a`, `aro_sign_panel_overhead_green_a` |
| `aro_road_sign_direction_a` | `aro_sign_km_marker_a` |
| `aro_guardrail_post_set` | `aro_guardrail_wbeam_a` |
| `aro_jersey_barrier_a` | `aro_median_barrier_jersey_a` |
| `aro_kerb_painted_run_a` | `aro_kerb_painted_blackwhite_a`, `aro_kerb_painted_yellowblack_a` |
| `aro_drain_grate_set` | `aro_drain_grating_strip_a` |
| `aro_litter_bin_a` | `aro_bin_a` |
| `aro_planter_island_a` | `aro_planter_island_kerbed_a`, `aro_median_planter_kerbed_a` |
| `aro_planter_island_b` | `aro_planter_island_kerbed_a`, `aro_median_planter_kerbed_a` |
| `kano_utility_pole_set` | `aro_pole_electric_concrete_span_a`, `rg_ngn_pole_timber_span_a` |
| `aro_truck_trailer_parked_a` | `aro_trailer_box_a` |
| `aro_bench_a` | **same id**: the registry's planned entry moves to this version |
| `aro_pallet_stack_a` | **same id**: the registry's planned entry moves to this version |

## 6. Review scenes

`transport_kit/scenes.py` composes four review scenes from the kit only: **`env_logistics_yard_dry`** (a walled yard behind the two-lane gate, weighbridge in the entry lane, two halls and a cold store, fuel island, trailers, containers, forklift and pallets at the docks, mast lights), **`env_bus_terminal_dry`** (the terminal, its platform canopy, kiosk, board, minibus stand, tricycle rank, shelter, barriers, lights and signs along a road) **`env_road_furniture_dry`** (a lineup of all the furniture) and **`env_roundabout_kit_dry`** (the family D lineup on grass: monument, fountain, wall run, pattern panels, a flyover parapet module on two piers, pier panel, sound walls, edge lights). They are placement files of the same format as the environmental scenes, built by the same Unity code (`KanoReviewBuilder`) and shown with `?smoke=kanostreet&scene=env_<name>_dry&view=<view>`; the workflow step lists their 16 views. **They are review compositions, not game content**: nothing in the game places them. Layouts are design choices.

## 7. What is not done, and what the owner is asked

* The **Unity import and the in-game pictures** (levels 3 and 4): one `[kano-review]` commit when the licence queue is empty.
* **Registry and binaries: done on a branch, not on `main`.** The 52 assets were staged into ARO-CONTENT branch **`assets/transport-kit-v1`** (commit 48be9f9: `.blend`, FBX, GLB, manifests with the catalogue entry and the sockets, validation reports, 104 previews, palette textures, three review scenes, licence rows, release and freeze manifests) as **Git LFS objects**, with registry entries at `blender_validation_passed` (the private repository's validator and 52 tests pass; `aro_bench_a` and `aro_pallet_stack_a` moved from their planned entries). **Verified through a fresh clone** (`tools/verify_asset_clone.py`): **866 of 866 LFS objects intact, 321 of 321 frozen files byte for byte** (`docs/kano/TRANSPORT_KIT.freeze.json`). `ARO-CONTENT` `main` is **not** changed: merging the branch is the owner's decision, together with the overlap decision of section 5b. Note: the registry's `LIBRARY.md` index was regenerated on the branch (the validator refuses a stale one).
* **Not merged into the game**: the catalogue entries, routes that use the assets, replacing the E1 corridor's placeholder props (`warehouse`, `fuel_station`, `bus_stop`).
* Night look of the lit assets (heads declared, nothing seen at night), the colour and size of signs against real Nigerian sign standards (generic shapes only; **no regulatory accuracy is claimed**), and the use of the aerial concept references R-009, R-013 to R-015 and R-022 (generated, not Kano-verified) are open.
* Decisions for the owner: whether the first-pass steel (0.55 metallic) and palette colours are acceptable once seen in the game; whether the kit's catalogue may be merged after the review.
