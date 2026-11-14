# Kano city construction (owner directive of 2026-11-14): what was built and placed

The directive: stop planning and BUILD Kano, with fuel stations first, every building family, and integration into the game. This document records what exists now. **Nothing here is visually approved.** Levels: 1 implemented, 2 automated tests, 3 built and imported in Unity, 4 seen in gameplay, 5 visually reviewed by the owner.

## 1. The 28 new buildings (four families, all v001)

All 28 are complete lots: building, forecourt or compound, walls and gate, parking and the details of a lived-in place. Each comes in three LODs with collision boxes and lamp heads, and each is painted by the surface bake (`transport_kit/surface.py`). Sign words are painted into the textures: either generic words (SUPERMARKET, PHARMACY, HOSPITAL and so on) or the **fictional** chain KANO OIL from your concept R-040. There is no real brand, bank, institution or emblem; there is a blue H and a green cross, and never a red cross.

### G. Fuel stations (`transport_kit/fuel.py`, after your concept R-040)

| Id | Lot (m) | LOD triangles | What it is |
|---|---|---|---|
| `kano_fuel_station_large_a` | 47 x 36 x 10 | 4406 / 796 / 90 | **Your image, in 3D.** A 30 m canopy with a navy fascia, a glowing strip and a raised KANO OIL brand box. White columns with numbered pump boxes 1 to 4, four islands with eight dispensers (displays, holsters, hoses, yellow U-barriers, extinguishers). A KANO OIL MART with lit shop windows and shelves, a services board (shop, ATM, air and water, toilets), an ATM kiosk. A 9.5 m price totem (brand, PETROL / DIESEL / KEROSENE prices, 24 HOURS, services). Air and water, tank fill points and vents, IN and OUT signs. Kerbed planters with flowers and bollard lights, a generator house, a walled back yard. The canopy soffit is at 5.8 m, so a 4.2 m truck passes under it, with 5 m lanes between the islands. |
| `kano_fuel_station_medium_a` | 32 x 26 x 7 | 2610 / 544 / 74 | A neighbourhood station of the same chain: a 20 m canopy, two islands, a KANO OIL SHOP kiosk, a car-wash bay, a smaller totem, air and water, a walled yard. |
| `rg_ngn_fuel_station_small_a` | 20 x 16 x 5 | 1046 / 136 / 62 | An independent roadside FILLING STATION: a pitched zinc canopy on red steel posts, one island with two older pumps, a block office with burglar bars and a painted board, a hand-painted price board. Oil drums, jerry cans, a bench, a generator, an engine-oil rack, a worn forecourt with laterite margins. |

### H. Commercial, I. Public, J. Residential (`transport_kit/blocks.py`, one parametric generator)

| Id | Lot (m) | LOD triangles | Id | Lot (m) | LOD triangles |
|---|---|---|---|---|---|
| `kano_supermarket_a` | 44 x 36 | 1086 / 204 / 40 | `kano_hospital_general_a` | 49 x 39 | 2687 / 414 / 28 |
| `kano_restaurant_a` | 24 x 22 | 1135 / 82 / 26 | `rg_ngn_clinic_a` | 24 x 21 | 688 / 138 / 20 |
| `kano_fastfood_a` | 26 x 24 | 616 / 158 / 20 | `kano_school_a` | 50 x 37 | 1476 / 262 / 30 |
| `kano_hotel_a` (8 storeys) | 36 x 32 | 2473 / 412 / 34 | `kano_police_division_a` | 28 x 25 | 892 / 198 / 20 |
| `kano_bank_a` | 28 x 24 | 550 / 116 / 26 | `kano_fire_station_a` | 30 x 24 | 376 / 124 / 36 |
| `rg_ngn_pharmacy_a` | 14 x 13 | 242 / 68 / 20 | `kano_government_offices_a` | 48 x 35 | 2043 / 316 / 20 |
| `rg_ngn_phone_shops_a` | 21 x 12 | 376 / 88 / 10 | `kano_motor_park_a` | 46 x 36 | 916 / 182 / 52 |
| `rg_ngn_spare_parts_row_a` | 23 x 11 | 776 / 88 / 10 | `rg_ngn_factory_a` | 46 x 35 | 856 / 338 / 30 |
| `kano_car_showroom_a` | 36 x 30 | 340 / 86 / 32 | `rg_ngn_lowcost_row_a` | 22 x 14 | 324 / 76 / 20 |
| `rg_ngn_auto_workshop_a` | 22 x 18 | 764 / 128 / 30 | `rg_ngn_bungalow_a` | 24 x 25 | 829 / 148 / 36 |
| `kano_shopping_plaza_a` | 42 x 25 | 1038 / 216 / 28 | `kano_semidetached_a` | 26 x 23 | 932 / 202 / 34 |
| `rg_ngn_wholesale_depot_a` | 32 x 25 | 580 / 146 / 20 | `kano_duplex_a` | 30 x 29 | 1112 / 238 / 38 |
| | | | `kano_mansion_a` (pool) | 42 x 37 | 1486 / 218 / 46 |

The economic range of the homes runs from the low-cost "face-me-I-face-you" row with its veranda, rusty zinc, tank stand, washing lines and cooking corner, through the middle-class bungalow and semi-detached pair, to the premium duplex (stone feature wall, glass balcony, car porch, palms) and the mansion (pool and deck, gatehouse, gold gate). Low triangle counts are deliberate: windows, shutters, shelves, joints, signs, dust and shadows are painted by the bake instead of modelled.

## 2. The production chain, asset by asset

| Step | State |
|---|---|
| Blender source, validation, LODs, collision | Yes, for all 28. Every asset passes the toolchain validator within the 6,000-triangle building budget, with 3 materials (one atlas per LOD), strictly decreasing LODs and the last LOD at most 10% of LOD0. |
| Export | FBX, GLB and manifest, v001, in the asset workspace. |
| Asset library | ARO-CONTENT commit 7136536, release `transport_kit_city_v001`; hashes in `docs/kano/TRANSPORT_KIT_CITY_v001.freeze.json` (section 5). |
| Registry | One entry per asset, at status `blender_validation_passed`. |
| Unity prefab | Made by the review build. The four review scenes `env_kano_fuel_dry`, `env_kano_commerce_dry`, `env_kano_civic_dry` and `env_kano_homes_dry` carry all 28, so the review build makes their prefabs and photographs them (16 new views). |
| City placement | The catalogue entries and the frontage rules of both city routes; a .NET test fails if any of the 28 is never placed. |
| Refuel | Every pump of the three stations works in the game (P1, `docs/REFUEL_AT_A_PUMP.md`). Their catalogue entries carry the pump sockets and the model's own collision boxes, so the forecourt can be driven into. In the public build the station placeholder is drawn from those boxes. |
| Playable | Drivable today in the WebGL build at `?smoke=drive&route=ng-kano-city-e1` or `ng-kano-city-f1` (add `&start=<metres>`). In the public build they draw as footprint boxes; the review build draws the real models. **In the player's route selection since 2026-11-14** (owner decision): the world map's Kano panel, free drive (section 6). |

## 3. The city: district mixes on the two city routes

Every road class now has the character of a district (`tools/kano/make_city_routes.py`). Totals:
* **ng-kano-city-e1:** 151 lots, 43 designs, 32 kinds, 18 mosques, 12 fuel stations.
* **ng-kano-city-f1:** 114 lots, 33 designs, 29 kinds, 9 mosques, 4 fuel stations.

| Road (rule) | District | What stands there (lots) |
|---|---|---|
| E1 arterial (`arterial_major`) | commercial centre | 66: apartments 9, fast food 9, parades 8, **fuel stations 7**, offices 6, plazas 5, grand mosques 5, hotels 4, supermarkets 4, banks 3, villas 3, a medium mosque, a restaurant, a car showroom |
| E1 collector (`collector_main`, single carriageway) | mixed traditional street | 48: pharmacies 7, shop rows 7, Hausa compounds 6, low-cost rows 4, **fuel stations 4**, phone shops 4, spare parts 3, parades 3, restaurants 2, five mosques, a market shed, apartments, a clinic |
| E1 cross streets | old neighbourhood | 12: houses, low-cost rows, small shops, small mosques |
| E1 local street | middle class | 9: villas, duplexes, a bungalow, a house, small mosques |
| E1 second collector | premium estate | 8: semi-detached pairs, villas, duplexes, mosques |
| E1 service road | industrial | 5: warehouses, a wholesale depot, a workshop, spare parts |
| E1 terminal access | transport | 3: a motor park, a workshop, a fuel station |
| F1 beside the flyover (`flank_urban`, three rows) | city centre | 89: villas, apartments, offices, pharmacies, banks, parades, hotels, phone shops, **fuel stations 3**, restaurants, fast food, police, a hospital, fire stations, plazas, supermarkets, Hausa compounds, mosques |
| F1 highway flanks | edge of town | 24: houses, wholesale depots, Hausa compounds, workshops, a factory, a low-cost row, a fuel station; on the civic edge a school, a hospital, government offices, a mansion, bungalows; small mosques |

**What the tests hold** (`FrontageTests`, 1,407 .NET tests pass):
* No lot overlaps another, and every footprint stays clear of roads, plates and islands on level ground.
* Mosques stand at regular intervals on every E1 road class.
* No 90 m of main road is left without a building.
* At least 20 buildings stand on each side of the flyover deck, with mid-rise blocks and a mosque in view.
* No design takes more than 40% of a rule, and no more than three identical buildings stand in a row.
* **Every one of the 28 new designs stands somewhere.**
* Fuel stations stand on the arterial and on the single carriageway.
* Both cities' golden digests were updated deliberately (the old values are in the test's comment).

## 4. Tests (level 2)

**Blender (`tools/blender/tests`):**
* `test_city_families.py`, 32 tests. Every asset is checked against the validator, its budget, its materials and LODs, its footprint and its lamp heads. The stations are checked for a complete environment: entry and exit, pumps, column clearance for a truck, lanes between islands. Every sign must carry generic words or the fictional chain, never a real brand (a deny-list of real fuel companies, banks, chains and networks), and never a red cross. A painted sign must actually show its words.
* `test_surface_bake.py` and `test_transport_scenes.py` are extended: each of the 28 must be in a review scene.

**.NET (`FrontageTests`):** as in section 3.

**New bake features, all covered by these tests:**
* painted signs: words and a generic flame mark, drawn with Pillow's built-in Aileron font (CC0), so no font file is added to the repository;
* the lit shopfront pattern;
* painted cladding colours;
* separation rules 2 for the new families: no back-and-forth moves, and thin ground-slab sides deleted under wall ends.

The realism-pass assets keep rules 1, and their catalogue entries rebuild byte for byte.

## 5. Library and freeze

The 28 v001 assets are staged into ARO-CONTENT `assets/transport-kit-v1` (commit 7136536: 374 new files, nothing overwritten or deleted, library validator and its 52 tests pass, preservation audit 2,215 of 2,220 paths of the previous tip identical with 5 reconciled text files and 0 missing) as release `transport_kit_city_v001`. The game repository keeps their hashes in `docs/kano/TRANSPORT_KIT_CITY_v001.freeze.json`. Registry entries are at `blender_validation_passed`, with breakdown, Unity import, review and approval all open. The brand KANO OIL is flagged for a trademark check before any public release.

## 6. Decisions that are yours

1. **Expose Kano in the game.** *Decided by the owner on 2026-11-14: yes.* Done on the client: the world map's Kano panel has a **DRIVE HERE** section with the two city routes (`NetCore/DriveRegions.cs`, `Game/DriveRegionStore.cs`). The choice is kept on the device; choosing reloads the world and the saved session signs the player back in and starts the drive. Free drive only (the fuel station pumps work): jobs, bus routes and convoys in Kano are refused with a message until the server has Kano data (P3). The server's world state for Kano stays PLANNED; nothing on the server changed. Lagos to Ibadan's Free drive button switches back.
2. **The brand.** Is KANO OIL the chain the game should carry? It is your concept's name; it should pass a trademark search first.
3. **Approval** of any of the 28 after you have looked at the in-game pictures (`production_approved` is yours alone).

## 7. Not built yet (honest list)

* Universities as a separate design: the school generator could make one.
* A vehicle inspection centre, markets with stalls (the market shed exists), hotels beyond the one design, and banks, supermarkets and restaurants in a second design each.
* Interiors (the shop windows are painted), people, and parked vehicles inside the lots.
* A longer district-by-district Kano route: the two test routes are 1.5 and 1.8 km.
