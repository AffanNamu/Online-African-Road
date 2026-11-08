# Kano transport expansion: asset breakdown matrix (PROPOSED, needs the owner's approval before any asset is modelled)

Written 2026-11-08. The project rules (handover section 2, plan section 3) require an approved **breakdown** before an asset is built: what each piece is, its class, size, triangle and material budget, collision, light, text face, the reference it follows, and what must exist in the engine first. This is that breakdown for the logistics sites, the bus terminals, the road furniture and the roundabout and flyover components of `KANO_TRANSPORT_EXPANSION_PLAN.md`. **Nothing here is modelled. No status in the registry changes.** The engine stages E1 to E4 it depends on exist (`ROAD_ENGINE_E1.md` to `E4.md`); the two sites on the test corridor currently use the game's grey placeholder props.

**Rules every row follows (not repeated per row).** Naming: GLOBAL `aro_`, REGIONAL `rg_ngn_` (northern Nigeria), CITY_SPECIFIC `kano_`, LANDMARK `lmk_kano_`; reuse an existing registry asset before creating one. Category budgets are the catalogue validator's (LOD0 triangles: buildings 6,000, vegetation 3,000, signs 800, lights 1,200, props and environment 2,500; each LOD strictly fewer). At most **4 materials** per asset (draw-call budget: 150 mobile / 400 desktop in view). Real metres, origin at the ground centre of the footprint, +Z the way the front faces. Every asset gets LOD0 to LOD2, a collision proxy where marked, lamp heads declared in the catalogue `light` spec (lamp/glow/beacon) and, for sign faces, a catalogue `label`. **No real brand marks, plates or logos** (generic fictional brands only). References are private design references (R-xxx), never textures. Each built asset goes through the full chain: Blender validation, Unity import, in-game picture, performance report, **the owner's approval** (`production_approved` is the owner's alone).

Engine dependency column: E1 cross-section/symbols, E2 site plates/labels/colliders, E3 junction forms, E5 rings and decks (**unbuilt, waits for approval**), none = places on the existing scatter.

## A. Logistics and warehouse site (brief phase 3; district D10; references R-013, R-014, R-015, RB-13)

| Asset id | Class | What | Size (m) | LOD0 tris | Mats | Collider | Light | Label | Depends on |
|---|---|---|---|---|---|---|---|---|---|
| `rg_ngn_warehouse_hall_a` | REGIONAL | pitched-roof steel-frame warehouse, 6 docks, roller doors, office annex | 48 x 24 x 10 | 5,200 | 4 | box | glow x6 (dock lamps) | name board | E2 |
| `rg_ngn_warehouse_hall_b` | REGIONAL | low-bay distribution shed, sawtooth roof, 4 docks | 36 x 20 x 8 | 4,600 | 4 | box | glow x4 | name board | E2 |
| `rg_ngn_warehouse_cold_a` | REGIONAL | insulated cold store, plant deck and condenser units | 30 x 18 x 9 | 4,800 | 4 | box | glow x3 | none | E2 |
| `aro_dock_leveller_set` | GLOBAL | dock door frame, bumper and shelter, tileable x1..x6 | 3.5 x 1.2 x 4.4 | 600 | 2 | none | none | none | E2 |
| `aro_container_20ft_a/b/c` | GLOBAL | 20 ft shipping container, three liveries (generic, no brand) | 6.1 x 2.4 x 2.6 | 450 | 1 | box | none | none | none |
| `aro_container_40ft_a/b` | GLOBAL | 40 ft container, two liveries | 12.2 x 2.4 x 2.6 | 450 | 1 | box | none | none | none |
| `aro_container_stack_3` | GLOBAL | merged stack of three containers (draw-call saver) | 12.2 x 2.4 x 7.8 | 900 | 2 | box | none | none | none |
| `aro_pallet_stack_a` | GLOBAL | wrapped pallets and crates cluster | 3 x 2 x 1.8 | 900 | 2 | box | none | none | none |
| `aro_gate_sliding_a` | GLOBAL | sliding steel yard gate, 9 m opening, guard hut | 11 x 1.2 x 2.6 | 1,100 | 3 | box | glow x2 | "GATE n" | E2 |
| `aro_fence_palisade_set` | GLOBAL | palisade and chain-link fence run (tileable 4 m) and corner posts | 4 x 0.1 x 2.4 | 220 | 1 | box | none | none | none |
| `aro_wall_plot_set` | GLOBAL | rendered plot wall run, coping, two colours (tileable 4 m) | 4 x 0.25 x 2.4 | 160 | 1 | box | none | none | none |
| `aro_weighbridge_a` | GLOBAL | truck weighbridge with cabin and display | 18 x 3.6 x 3.2 | 1,300 | 3 | box | glow x1 | weight display | E2 |
| `aro_fuel_island_truck` | GLOBAL | truck fuel island: canopy, two pumps, bollards (generic brand) | 16 x 8 x 6.5 | 2,400 | 3 | box | glow x4 | price board | E2 |
| `aro_forklift_a` | GLOBAL | static forklift (prop only) | 3.2 x 1.2 x 2.2 | 1,500 | 2 | box | none | none | none |
| `aro_truck_trailer_parked_a/b` | GLOBAL | parked flatbed and box trailer, no cab | 13 x 2.5 x 4 | 1,800 | 3 | box | none | none | none |
| `aro_yard_mast_light` | GLOBAL | 20 m high-mast floodlight (4 heads) | 1.2 x 1.2 x 20 | 700 | 2 | none | lamp x4 (large pool) | none | night system |
| `aro_bollard_set` | GLOBAL | steel and concrete bollards, yellow-black | 0.3 x 0.3 x 1 | 120 | 1 | box | none | none | none |

## B. Bus terminal and stops (brief phase 4; district D09; reference R-009, RB-13, RB-07)

| Asset id | Class | What | Size (m) | LOD0 tris | Mats | Collider | Light | Label | Depends on |
|---|---|---|---|---|---|---|---|---|---|
| `kano_bus_terminal_hall_a` | CITY_SPECIFIC | terminal building: concourse, ticket hall, covered arrivals, flat roof with brise-soleil | 40 x 22 x 9 | 5,600 | 4 | box | glow x8 | name board, departures board | E2 |
| `kano_bus_shelter_long_a` | CITY_SPECIFIC | long platform canopy for 3 bays, benches, route panels | 18 x 4 x 4 | 2,200 | 3 | none (posts box) | glow x3 | bay numbers | E2 |
| `aro_bus_shelter_a` | GLOBAL | single-bay roadside shelter (replaces the dev `bus_stop`) | 5 x 2 x 3 | 1,400 | 3 | none | glow x1 | stop name | none |
| `kano_ticket_kiosk_a` | CITY_SPECIFIC | ticket booth with window and queue rail | 3 x 3 x 2.8 | 900 | 2 | box | glow x1 | "TICKETS" | E2 |
| `aro_bus_bay_marking_set` | GLOBAL | pavement decals as marks (bay numbers, hatch) for site plates | n/a | 0 | 1 | none | none | none | E2 marks (data, not mesh) |
| `kano_departure_board_a` | CITY_SPECIFIC | double-face departures board on a post | 3 x 0.3 x 4.5 | 500 | 2 | box | glow x1 | board text | E2 |
| `kano_minibus_stand_canopy_a` | CITY_SPECIFIC | minibus (danfo/korope-style) stand: tin roof on poles, route boards | 12 x 6 x 3.6 | 1,300 | 3 | none | glow x2 | route names | E2 |
| `kano_tricycle_rank_a` | CITY_SPECIFIC | tricycle taxi rank: canopy, painted kerb, queue marks | 8 x 5 x 3 | 900 | 2 | none | glow x1 | none | E2 |
| `aro_parking_bay_barrier_set` | GLOBAL | wheel stops, low rail, planter box (tileable) | 2.5 x 0.4 x 0.6 | 140 | 1 | box | none | none | none |

## C. Road furniture and street kit (all phases; references R-004, R-022, R-023, RB-07, RB-09)

| Asset id | Class | What | Size (m) | LOD0 tris | Mats | Collider | Light | Label | Depends on |
|---|---|---|---|---|---|---|---|---|---|
| `aro_streetlight_arm_single_a` | GLOBAL | single-arm 9 m pole, cobra head | 4.4 x 0.6 x 9 | 700 | 2 | none | lamp x1 | none | night system |
| `aro_streetlight_double_median_a` | GLOBAL | twin-arm 12 m pole for a planter median | 6 x 0.6 x 12 | 900 | 2 | none | lamp x2 | none | E1 median |
| `aro_streetlight_ornamental_a` | GLOBAL | decorative lantern pole for roundabout islands and plazas | 1 x 1 x 6 | 1,000 | 2 | none | lamp x1 (warm) | none | night system |
| `aro_road_sign_post_set` | GLOBAL | regulatory and warning signs on posts (stop, yield, speed 50/80, no entry, bus, zebra, turn bans) | 0.8 x 0.1 x 3 | 150 each | 1 | none | none | none | none |
| `aro_road_sign_direction_a` | GLOBAL | blue-green destination sign on two posts, text face | 3 x 0.3 x 4 | 350 | 2 | none | none | destination text | E2 labels |
| `aro_gantry_sign_a` | GLOBAL | overhead gantry spanning a 2 x 3 lane carriageway, 2 text faces | 20 x 1 x 7 | 1,800 | 3 | box (legs) | none | two faces | E2 labels, E1 profiles |
| `aro_traffic_signal_a` | GLOBAL | signal head set on a mast (visual first; no logic) | 5 x 0.4 x 6 | 700 | 3 | none | glow x3 (red/amber/green static) | none | none |
| `aro_guardrail_post_set` | GLOBAL | W-beam run and end terminal | 4 x 0.3 x 0.8 | 200 | 1 | box | none | none | none |
| `aro_jersey_barrier_a` | GLOBAL | concrete safety barrier module (the engine builds the road one; this is for sites) | 3 x 0.6 x 0.8 | 150 | 1 | box | none | none | none |
| `aro_kerb_painted_run_a` | GLOBAL | black and white painted kerb segment for islands off-road | 2 x 0.25 x 0.15 | 80 | 1 | box | none | none | none |
| `aro_drain_grate_set` | GLOBAL | drain inlet grates and manhole covers as decal quads | 0.6 x 0.6 | 40 | 1 | none | none | none | none |
| `aro_bench_a` / `aro_litter_bin_a` | GLOBAL | street bench and bin | 1.8 x 0.6 x 0.9 | 400 | 1 | box | none | none | none |
| `aro_planter_island_a/b` | GLOBAL | kerbed planter box for medians and islands, two lengths | 4 x 2 x 0.6 | 300 | 2 | box | none | none | E1 planter |
| `kano_utility_pole_set` | CITY_SPECIFIC | wooden and concrete poles with transformers and cables | 0.4 x 0.4 x 11 | 600 | 2 | none | none | none | none |

## D. Roundabout, island and flyover components (phases 1 and 2; references R-004, R-005, R-023, R-034, R-035, R-036). **These need E5, which is not built.**

| Asset id | Class | What | Size (m) | LOD0 tris | Mats | Collider | Light | Label | Depends on |
|---|---|---|---|---|---|---|---|---|---|
| `lmk_kano_roundabout_monument_a` | LANDMARK | the Landmark roundabout monument: a **designed civic monument, flagged fictional** (decision 4); stepped plinth, arches inspired by the regional geometric language, uplights | 12 x 12 x 22 | 5,400 | 4 | box | glow x6 (uplights) | plaque | E5 + owner decision |
| `rg_ngn_pattern_panel_a/b/c` | REGIONAL | geometric (Hausa-inspired) relief panels for island walls, three patterns, tileable | 3 x 0.3 x 1.4 | 500 | 1 | box | none | none | E5; flagged interpretation of R-024..R-029 |
| `rg_ngn_island_wall_a` | REGIONAL | red-earth rendered island wall run with coping (tileable 3 m) | 3 x 0.4 x 1.2 | 200 | 1 | box | none | none | E5 |
| `aro_roundabout_hedge_ring_a` | GLOBAL | clipped hedge ring segment (tileable arc) | 3 x 0.8 x 0.9 | 350 | 1 | box | none | none | E5 |
| `aro_roundabout_fountain_a` | GLOBAL | low fountain basin for the green and landmark islands (no animation) | 8 x 8 x 1.6 | 1,600 | 3 | box | glow x4 | none | E5 |
| `aro_palm_ornamental_a/b` | GLOBAL | ornamental palms for islands and boulevards (reuse `kano_date_palm_a` first) | 4 x 4 x 9 | 1,800 | 2 | none | none | none | none |
| `aro_shrub_flowering_a/b` | GLOBAL | flowering shrub clusters for planters (colour by season) | 2.5 x 2.5 x 1.4 | 600 | 2 | none | none | none | none |
| `aro_tree_shade_a/b` | GLOBAL | neem-like shade tree (reuse `kano_neem_tree_a/b` first) | 9 x 9 x 12 | 2,400 | 2 | none | none | none | none |
| `aro_flyover_deck_segment_a` | GLOBAL | deck module with parapets and edge-light brackets, 2 x 3 lanes | 12 x 24 x 1.6 | 1,200 | 2 | n/a (the engine builds the deck mesh; this is the parapet and bracket kit) | lamp (brackets) | none | E5 |
| `aro_flyover_pier_a/b` | GLOBAL | round and square-capped pier with bearing | 2.4 x 2.4 x 6..8 | 700 | 2 | box | none | none | E5 |
| `rg_ngn_pier_panel_a` | REGIONAL | lattice-panelled pier cladding for the interchange | 3 x 0.3 x 6 | 600 | 1 | box | none | none | E5 |
| `aro_deck_edge_light_a` | GLOBAL | deck edge luminaire (wall washer) | 0.4 x 0.3 x 0.6 | 200 | 1 | none | lamp x1 | none | E5, night system |
| `aro_noise_wall_set` | GLOBAL | transparent and concrete sound wall modules along ramps | 4 x 0.3 x 3 | 250 | 2 | box | none | none | E5 |

## E. Supporting buildings (phase 5; districts D05, D08; references R-022, R-031; some references MISSING: M-02 and M-04)

| Asset id | Class | What | Size (m) | LOD0 tris | Mats | Collider | Light | Depends on |
|---|---|---|---|---|---|---|---|---|
| `kano_commercial_frontage_a/b/c` | CITY_SPECIFIC | 3 to 5 storey commercial frontages with signage bands (generic brands) | 24 x 14 x 16 | 5,800 | 4 | box | glow x10 | none; **needs M-02 references** |
| `kano_office_midrise_a` | CITY_SPECIFIC | 8-storey office block with podium | 30 x 22 x 30 | 5,900 | 4 | box | glow x16 | none; needs M-02 |
| `kano_hotel_a` | CITY_SPECIFIC | 6-storey hotel with canopy and sign | 34 x 20 x 22 | 5,800 | 4 | box | glow x12 | none; needs M-02/M-04 |
| `kano_market_hall_a` | CITY_SPECIFIC | covered market hall, trader stalls inside | 40 x 30 x 8 | 5,400 | 4 | box | glow x10 | none; needs M-04 |
| `kano_filling_station_b` | CITY_SPECIFIC | corner filling station with canopy and shop (generic brand) | 30 x 22 x 7 | 4,200 | 4 | box | glow x8 | E2 |
| `kano_petrol_kiosk_row_a` | CITY_SPECIFIC | roadside trader kiosk row | 12 x 3 x 3 | 1,400 | 3 | box | glow x3 | none |

## Totals and order of work (if approved)

* 73 asset ids in 59 rows (variants share a row). Triangle and material budgets above fit the catalogue validator; the full set at typical density is **not** measured: E0's baseline still applies (the plan estimates 360 draws in view for the densest district against a 400 desktop budget, **an estimate until measured**).
* **Order**: A and B first (the two sites already exist on the test corridor with placeholder props, so each asset can replace a grey box and be judged in place), then C (furniture), then E, then D (D waits for E5).
* **Each asset** is a new registry entry with a reference, built in Blender by the validated toolchain, imported and shown in the game by the CI review (`[kano-review]`), and approved or rejected by the owner after looking at pictures. Binaries go to ARO-CONTENT as Git LFS objects only.

## What the owner is asked

1. Approve, change or strike each family (A to E), or give the order.
2. For families E: send the missing references M-02 and M-04 (commercial frontage, market), or approve building from the documentary sources only with the flag "design interpretation".
3. Confirm that generic fictional brands are fine on every sign, board and canopy.
