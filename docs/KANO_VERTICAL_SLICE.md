# Kano vertical slice: pre-production package and greybox (Production Assignment 001)

**Status (2026-11-01): PRE-PRODUCTION. Nothing here is a finished asset, a finished road or a playable Kano.** What exists: research with sources, a reference board built from five photographs you supplied, a draft road network in the game's own data format that passes the game's own validator, a planned asset inventory checked against the game's own budgets, and a Blender greybox of plain placeholder volumes generated from the game's own road model and prop scatter. What does not exist: any modelled building, texture, material or LOD; any Unity import of Kano content; any driving test; any measured frame time. Detailed modelling waits for your approval of this package and for the references listed in section 1.4.

Evidence files (all in `docs/kano/`, regenerated and checked by `tests/NetCore.Tests/KanoSliceTests.cs`, which passes with the rest of the 1,086 core tests): `ng-kano-slice.route.draft.json`, `kano_asset_inventory.json`, `kano_world_frame.json`, `kano_slice_geometry.json`, `kano_slice_stats.json`. Generator: `tools/kano/make_slice_draft.py`. Greybox: `tools/blender/scripts/kano_greybox.py`. Renders were sent to you in the conversation (they are not committed: binary files stay out of this public repository).

---
## 1. Visual reference board and research

### 1.1 What you supplied (five images), and what I can and cannot read from them
I cannot verify where any of these photographs were taken; the table says what is visible and what it is usable for. **Please confirm the location and the rights of each.**

| # | What is visible | Use, and caveat |
|---|---|---|
| 1 | Oblique aerial of a dense old urban fabric around a large quarried rock outcrop with a reddish-brown laterite face. Low-rise, flat or low-pitched roofs, many with corrugated sheet and roof tanks; walls in tan and ochre tones; irregular lanes, a straight main road crossing the top, very few trees; warm brown-orange overall palette. Consistent with Dala Hill and the old city, as your brief describes, but I cannot confirm that from the image. | Colour palette, density, roofscape, the hill as a landmark silhouette. **Not a source of building proportions** (too far away). |
| 2 and 3 | The same aerial twice (2 is a phone screenshot of 3). A planned, modern residential district: two to four storey buildings, hipped or low-pitched roofs in red, brown, cream, white and green, walled plots, mature trees and palms, a wide street with parked cars on the left, a line of hills on the horizon. | Modern building typology and street rhythm. **The hill line on the horizon is not what I would expect on Kano's flat plain, so this may be another Nigerian city. I will not use it as Kano evidence until you confirm.** |
| 4 | Aerial of an elevated multi-lane road with curving ramps, a landscaped roundabout with a fountain, a mosque with two minarets and a green dome (right), mixed mid-rise buildings and mature trees, streetlights spaced regularly along the road edges. | Modern road furniture and mosque type; interchange form (the game cannot build this yet, section 3.4). Location unconfirmed. |
| 5 | Aerial of a multi-level interchange over a flat, hazy city, with a tall tower at the right edge. A **photographer's watermark ("Nuuman Ashaka") is on the image.** | Haze and flat-plain atmosphere, interchange form. **The watermark means the image is someone's work: it can be a private reference only, never copied into this repository, never a texture source.** |

None of these shows a Hausa compound, a zaure, a parapet or a gate at readable scale. They cannot yet drive any building model (section 1.4).

### 1.2 Verified real-world information (with sources and how firm it is)
| Fact | Source | Firmness |
|---|---|---|
| Kano city is at about 12°00'N 8°31'E, elevation about 484-488 m, on a nearly flat plain | [Wikipedia: Kano (city)](https://www.wikipedia.org/wiki/Kano_City), [WorldAtlas](https://worldatlas.com/af/ng/kn/where-is-kano.html) | firm to about 1 km |
| **Gidan Rumfa** (Emir's Palace): built in the 15th century under Muhammadu Rumfa (reigned 1463-1499); about 33 acres; parts rebuilt in the early 19th century; sits beside the Kano Central (Great) Mosque, which is on its north side | [ArchNet](https://archnet.org/sites/3778), [Atlas Obscura](https://atlasobscura.com/places/gidan-rumfa-emirs-palace), [Daily Trust](https://dailytrust.com/?p=392201) | date and builder well supported; the attribution of the Kofar Kudu gate to a 19th-century emir is **not** confirmed |
| **Kurmi Market**: founded by Muhammad Rumfa in the 15th century, an international trade point in the trans-Saharan trade; UNESCO groups it with the Emir's Palace and Dala Hill as associated sites of the ancient city; "over 600 years" is a local claim that is **not** firmly documented | [Wikipedia: Kurmi Market](https://en.wikipedia.org/wiki/Kurmi_Market), [UNESCO tentative list 5171](https://whc.unesco.org/en/tentativelists/5171/) | 15th century well supported |
| **Dala Hill**: in Dala Local Government Area, in the north-west part of the metropolis; Kano was originally called Dala after it; one source gives 12°0'33.498"N 8°30'25.106"E. **Height is disputed (534 m above sea level in most sources, 453.67 m in one)**, as is the number of steps (101 or 999) | [Wikipedia: Dalla Hill](https://en.wikipedia.org/wiki/Dalla_Hill), [UNESCO tentative list](https://whc.unesco.org/en/tentativelists/5171/) | location firm; dimensions unresolved |
| **Kofar Mata Dye Pits**: 1498 on the entrance sign, in the old city near the city wall and the palace; once 13 pit complexes, one survivor; the pit count is disputed (125 in one source) | [Wikipedia](https://en.wikipedia.org/wiki/Kofar_Mata_Dye_Pits), [History News Network](https://www.historynewsnetwork.org/article/38148) | 1498 and location well supported |
| Kano's ancient city wall: 14 km (UNESCO) or 17 km perimeter (Daily Trust); 15 traditional gates; Kofar Na'isa was demolished in 2010 for road works | [UNESCO tentative list](https://whc.unesco.org/en/tentativelists/5171/), [Daily Trust](https://dailytrust.com/kano-city-walls-an-endangered-1000-year-old-heritage/) | length disputed; **no source found for wall height** |
| Hausa urban building: the **zaure** is the principal ground-floor room at the front, where the master receives guests, beyond which a stranger does not go; each compound is walled; **tubali** (sun-dried, ovoid mud brick) laid in courses with thick mud mortar; walls thick at the base and thinner towards the top (one source: 600-1000 mm traditionally); floors and roofs on flat mud arches; buildings of more than two storeys are rare; **high parapets** surround the roofs so the town has a level skyline; **zankwaye** (pinnacles) are a classic feature; decorative relief motifs | [Wikipedia: Hausa architecture](https://en.wikipedia.org/wiki/Hausa_architecture), [African History Extra](https://www.africanhistoryextra.com/p/hausa-urban-architecture-construction), [ArchNet 4180](https://archnet.org/sites/4180) | qualitative features firm; **no dimensions** |
| Harmattan dust haze, most pronounced November to March, with reduced visibility; recent seasons later and weaker | [Kano Focus](https://kanofocus.com/2026/01/02/delayed-weaker-harmattan-signals-climate-change-impact-across-northern-nigeria-anka), [The Conclave](https://www.theconclaveng.com/dusty-harmattan-cold-weather-paralyse-early-activities-in-kano/) | firm |
| Districts: Sabon Gari and Fagge are the commercial centre; Nasarawa GRA is the upscale gated residential area; Zoo Road is a corridor of shopping plazas; Murtala Muhammed Way and Ibrahim Taiwo Road are commercial spines | [AllAfrica](https://allafrica.com/stories/202602240638.html), [Trust Radio](https://trustradio.com.ng/en/inside-kanos-most-expensive-property-locations) | descriptive only, from property-market articles; **no architectural detail** |
| The Great Mosque has been renovated several times (largest in 1951-52 and 1975); sources disagree whether it has two or four minarets | [ArchNet 3777](https://archnet.org/sites/3777) | unresolved |

**Searched for and not found:** the height of the old city wall; dimensions of any Kano compound, zaure, gate or wall; whether neem is the typical street tree; what electricity poles and streetlights in Kano are made of; the surveyed coordinates of Gidan Rumfa, Kurmi Market and Kofar Mata. These stay open and are marked as needing references; none was filled in from imagination.

### 1.3 Creative design (my proposals, NOT facts)
* The slice is a **fictional** neighbourhood on the Kaduna approach to Kano. No street, plot or building in it is real, and nothing in the data claims otherwise (`meta.gameCoverage`, `kano_world_frame.json`).
* The corridor direction (south-west to north-east, bearing 36°) is the great-circle direction from Kaduna to Kano computed from the game's own world-map coordinates; the corridor's position (about 4 km south-west of the Kano reference point) is a design choice.
* Zone order (entrance, modern district, commercial spine, old-neighbourhood edge), plot sizes, setbacks, spacing and every building envelope are **placeholders for review**.
* A neighbourhood mosque oriented to the qibla (bearing 68.4° from Kano, computed) is a design proposal; its type is yours to specify from a reference.

### 1.4 What the reference pack still has to supply (so nothing is invented)
For each: a front, a side, a close detail and something for scale (a person, a door, a car).
1. **Traditional Hausa compounds** in Kano: the street-front (zaure and entrance), the wall and its top, gates and doors, parapet and zankwaye, roof edge, finishes, windows. At least three distinct plots.
2. **Modern compounds** (walled bungalows and villas with gates), **shop rows**, **plazas**, **apartment blocks**, **mid-rise** buildings, a **filling station**, **as seen in Kano**.
3. **A neighbourhood mosque** (not the Great Mosque): dome(s), minaret(s), ablution block, wall, colours.
4. **Roads**: surface types (new asphalt, patched, cracked, damaged), edges, drains and slabs, kerbs, sidewalks if any, markings, bus stops.
5. **Poles and lights as installed** (material, crossarms, transformer, luminaire), **signs** (gantry, small signs), **billboards**.
6. **Trees and ground cover** that actually line these roads.
7. **Vehicles** to park along the road (cars, minibuses, motor tricycles, motorcycles), generic and brand-free.
8. **Light and atmosphere** (Harmattan haze, dusty light, time of day).

---
## 2. Proposed playable-area layout (draft, in the game's own route format)
One corridor, 1.65 km at **1:1 scale** (the shipped Lagos-Ibadan route is compressed; this one is not), authored in a proposed Kano world frame (section 5.1). It is the Kano end of the world-map route `kaduna-kano` (status planned), so it connects to data that already exists.

| Station (m) | Zone | Road | Surface | Content (all placeholders) |
|---|---|---|---|---|
| 0-450 | City entrance (zone `highway`) | divided, 2 lanes each way, 2 m median | good | gantry sign, median double streetlights every 40 m, neem rows, shrubs, one billboard |
| 450-830 | Modern district | median ends about 600 m; 2+2 lanes | good, then worn | modern walled compounds (two variants, alternating), one plaza, two mid-rises, one apartment block, streetlights, poles |
| 830-1240 | Commercial spine | 2+2 lanes | worn; **damaged 1,050-1,200** | shop rows (two variants), kiosks, stalls, two bus stops, a filling station, parked vehicles |
| 1240-1650 | Old-neighbourhood edge | one lane each way | worn, then good | traditional compounds (three variants), a **qibla-oriented neighbourhood mosque** near 1,460 |

Surface lengths (generated): good 901 m, worn 601 m, damaged 150 m. Six T-junctions lead to side streets of 170-300 m, the first 120 m of each lined with plots (section 3). Both sides of the main road are fronted by plots with a 4.5 m open **apron** in front (drain slab, walkway, pole line), because the road generator has no drain or sidewalk element (section 3.4); poles, lights and trees stand on the apron.

**Why this layout.** It puts the four things the brief asks for in the order a driver meets them, gives each a different road condition, and stays inside what the game can build today. It deliberately does **not** include the real landmarks (Gidan Rumfa, Kurmi Market, Dala Hill, Kofar Mata): their coordinates are unverified (`kano_world_frame.json` records them as reserved, with no invented numbers) and the old city is about 4 km from this corridor. They belong to a later old-city slice.

---
## 3. Road network and intersection blockout
Built from `ng-kano-slice.route.draft.json` with the game's own `RouteModel` (the same code Unity runs, compiled in the core test project), so lengths, widths, camber, junction mouths and ground height are the game's, not mine.

| Junction | Station (m) | Angle | Length (m) | Side street lined with |
|---|---|---|---|---|
| j1_modern_street | 650 | 90° right | 260 | modern compounds |
| j2_plaza_street | 830 | 80° left | 220 | modern compounds |
| j3_back_street | 1,000 | 100° right | 170 | shop rows |
| j4_market_lane | 1,230 | 90° left | 240 | shop rows |
| j5_mosque_street | 1,390 | 85° right | 300 | traditional compounds |
| j6_compound_lane | 1,550 | 95° left | 200 | traditional compounds |

### 3.1 Checked against the game's own rules
`RouteSpecValidator` accepts the draft (unknown ids, junction spacing of at least 150 m, no junction on a divided section, curve radius, grade); the road model builds; no two buildings overlap and no building corner enters any road formation (separating-axis check over every scatter result and every set piece); the mosque faces the qibla within 1°; every id used is planned and every planned id is used. **These checks failed repeatedly while I was authoring** (a missing positive `realRoadKm`, plots overlapping hero buildings, plot corners 0.1-0.2 m inside the road, the qibla-turned mosque 4.7 m inside the road, a draw-call overshoot): each was fixed in the data, none by loosening a test.

### 3.2 What the game's road system cannot express (measured from the code, not assumed)
| Need in the brief | The game today | Consequence for the slice |
|---|---|---|
| Intersections, smaller connecting streets | only **T-junction stubs** off the main road, 40-600 m long, at least **150 m apart**, never on a divided section; each stub is a straight 3-point road | no 4-way crossing, no street grid, no loop; six staggered T-junctions |
| Roundabouts, flyovers, interchanges (images 4 and 5) | not supported (`junctions on a divided section would need an interchange (not supported)`) | not in the slice; needs new road-generator features and your approval |
| Sidewalks, kerbs, open drains | the cross-section is lanes, shoulders, verge, optional median; the rule scatter keeps **3 m clear of the road formation**, so rule-placed props cannot sit nearer (hand-placed set pieces can, but 10 m drain slabs would cost one draw call each) | drains and walkways are modelled **inside each plot prefab's front apron**; vacant lots would show no drain |
| Patched asphalt, cracks, potholes | road wear is a longitudinal undulation of amplitude **4.5 cm** (damaged) and **1.4 cm** (worn); `potholes are NOT modelled in geometry (they need a decal/normal-map layer)` | the "damaged" section is drivable by construction; patches and cracks need a surface-detail layer that does not exist |
| Scatter along side streets | the scatter runs along the **main road only** | side-street plots are hand-placed set pieces (56 of them); streets beyond 120 m stay bare |
| Text on production signs | `MakeLabel` draws text only for *placeholder* instances of four ids; a production prefab gets none | the entrance sign's "KANO" must be baked into the sign prefab; variable text needs prefab variants or an engine change |
| A production prefab per id | a prefab at `Resources/Props/<id>` replaces that id's placeholder **in every route** | all Kano ids are new (`kano_*`) so Lagos-Ibadan is untouched |
| Culling and collision of production prefabs | the streamer does neither for production prefabs | every prefab must bring its own LODGroup, BoxCollider, identity root and instanced materials (the builder and its test already do) |

None of these was changed. Extending the road generator (4-way junctions, roundabouts, a kerb/drain/sidewalk strip, a road-detail layer) would change an existing system: **your decision** (section 9).

### 3.3 The world-map discrepancy
The game's `world_map.json` places Kano at 12.0022°N, **8.592°E**. The reference sources put the city at about 8.5167°E. The difference is **about 8.2 km east** (`kano_world_frame.json` records it). I did not change either; which is authoritative is yours to decide.

---
## 4. Building and environment asset inventory
Plot-level prefabs are what the game places; they are assembled in Blender from modular **kit modules** (12, listed below) and exported as **one mesh with one atlas material** each. Dimensions are placeholder envelopes (plots include the 4.5 m apron in their depth). The "placed" column is the count in the full slice at full density, generated from the real scatter.

| id | category | w x d x h (m) | planned LOD triangles | collider | cull (m) | placed | references needed |
|---|---|---|---|---|---|---|---|
| `kano_hausa_compound_a` | buildings | 18 x 26 x 5.2 | 5400/1700/480/100 | box | 300 | 13 | street-front elevation, zaure/gate, parapet with zankwaye, wall finish, roof edge |
| `kano_hausa_compound_b` | buildings | 20 x 28 x 7.4 | 5600/1800/500/100 | box | 300 | 12 | two-storey front block (sources: traditional Hausa buildings seldom exceed two storeys) |
| `kano_hausa_compound_c` | buildings | 13 x 21 x 4.6 | 4300/1400/400/90 | box | 300 | 14 | small single-storey compound |
| `kano_compound_modern_a` | buildings | 20 x 30 x 6.2 | 5800/1900/520/100 | box | 300 | 13 | walled bungalow/villa with gate: front, side, roof, finish |
| `kano_compound_modern_b` | buildings | 20 x 30 x 8.6 | 5900/1900/520/100 | box | 300 | 14 | two-storey walled villa |
| `kano_shop_row_a` | buildings | 18 x 12 x 4.2 | 3300/1100/320/80 | box | 300 | 21 | single-storey shop row: frontage, shutters, signage, awning |
| `kano_shop_row_b` | buildings | 20 x 13.5 x 7.4 | 4700/1500/420/90 | box | 300 | 21 | two-storey shop-and-flat block |
| `kano_plaza_a` | buildings | 30 x 26 x 13 | 5900/1900/520/110 | box | 450 | 1 | three-storey commercial plaza |
| `kano_apartment_block_a` | buildings | 22 x 20 x 10.5 | 5300/1700/480/100 | box | 420 | 1 | three-storey block (owner to confirm which city the supplied aerials 2 and 3 show before they are used) |
| `kano_midrise_a` | buildings | 26 x 24 x 24 | 5900/1900/540/110 | box | 650 | 2 | six to eight storey office/hotel block |
| `kano_mosque_neighbourhood_a` | buildings | 30 x 34 x 24 | 5900/1900/540/110 | box | 650 | 1 | a real neighbourhood mosque type: dome(s), minaret(s), ablution, wall, colours |
| `kano_filling_station_a` | buildings | 28 x 30 x 7 | 4800/1600/450/100 | box | 420 | 1 | forecourt, canopy, shop, signage |
| `kano_road_sign_small` | signs | 1.2 x 0.3 x 3.2 | 420/140/40 | box | 200 | 9 | regulatory/warning/direction sign types and mountings |
| `kano_road_sign_gantry` | signs | 16 x 1.2 x 6.5 | 780/260/80 | none | 450 | 1 | overhead gantry as installed |
| `kano_billboard_a` | signs | 12 x 0.8 x 9 | 700/240/60 | box | 500 | 1 | billboard frame and mounting |
| `kano_streetlight_single` | lights | 2.4 x 0.5 x 9 | 560/200/60 | box | 300 | 42 | pole material, arm, luminaire as installed |
| `kano_streetlight_double_median` | lights | 4 x 0.5 x 10 | 820/280/70 | box | 320 | 11 | median-mounted double arm |
| `kano_utility_pole` | props | 1.6 x 0.5 x 9.5 | 380/140/40 | box | 260 | 28 | pole material (concrete / timber / steel), crossarms, insulators |
| `kano_transformer_pole` | props | 2 x 1.4 x 9.5 | 900/300/70 | box | 260 | 3 | pole-mounted transformer as installed |
| `kano_kiosk_a` | props | 3 x 3 x 2.6 | 900/300/80 | box | 220 | 3 | roadside kiosk |
| `kano_market_stall_a` | props | 3.5 x 3 x 2.6 | 1000/340/90 | box | 220 | 4 | stall frame, umbrella/tarpaulin, goods |
| `kano_bus_stop_a` | props | 6 x 3 x 3 | 1500/500/120 | box | 260 | 2 | roadside shelter/stop |
| `kano_parked_group_a` | props | 7 x 4 x 1.7 | 2300/800/260/70 | box | 220 | 12 | common saloon car and motorcycles (generic, no brand) |
| `kano_parked_group_b` | props | 6.5 x 4 x 1.8 | 2300/800/260/70 | box | 220 | 13 | motor tricycle taxis and a motorcycle |
| `kano_parked_minibus_a` | props | 1.9 x 5.2 x 2.3 | 2100/700/200/60 | box | 240 | 1 | commercial minibus (generic, no brand) |
| `kano_neem_tree_a` | vegetation | 8 x 8 x 9 | 1800/600/160/40 | none | 320 | 23 | owner/reference to confirm the species that line these roads (neem is commonly reported; not verified here) |
| `kano_neem_tree_b` | vegetation | 9 x 9 x 11 | 2100/700/180/40 | none | 340 | 19 | as above, second form |
| `kano_date_palm_a` | vegetation | 5 x 5 x 12 | 1500/500/120 | none | 340 | 5 | owner/reference to confirm |
| `kano_shrub_a` | vegetation | 2 x 2 x 1.4 | 260/100/30 | none | 140 | 9 | dry-season shrubs / hedges |

**29 prefabs; 300 instances.** All pass the game's `PropCatalogValidator` (LOD0 ceilings: buildings 6,000, vegetation 3,000, signs 800, lights 1,200, props 2,500; at most 4 strictly decreasing LODs). Parked vehicles are grouped into two meshes (`kano_parked_group_a/b`) because every separate vehicle is a draw call (section 6).

### Blender kit modules (internal parts, not catalogue entries)
| module | use | references needed |
|---|---|---|
| `kit_wall_plain` | compound walls (plain rendered) | wall height, thickness, finish, coping |
| `kit_wall_decorated` | wall-top treatment | parapet profile, zankwaye pinnacles (verified feature of Hausa buildings) |
| `kit_gate_pier` | gate piers | pier section, cap |
| `kit_gate_leaf` | metal gate leaves (several patterns) | gate patterns as installed |
| `kit_zaure_entrance` | street-front entrance room of a traditional compound | zaure elevation and opening sizes |
| `kit_flat_roof_parapet` | flat roofs with parapets, water tanks | parapet height, roof furniture |
| `kit_openings` | doors, windows, shutters, security bars | opening sizes and styles |
| `kit_shopfront` | shop fronts with roll shutters and sign band | shopfront types |
| `kit_storey_modern` | modern storeys, balconies, stairs | modern facade types |
| `kit_dome_minaret` | mosque dome, minaret, ablution block | mosque type |
| `kit_apron_drain` | frontage apron: concrete drain slab, walkway, steps | drain and apron as built |
| `kit_pole_fittings` | crossarms, insulators, cables, lamp arms | installed fittings |

---
## 5. Blender to Unity production plan

### 5.1 World architecture so Kano can grow
* **One frame for all Kano content** (`kano_world_frame.json`, PROPOSED): x east, z north, metres, origin at the Kano reference point, local tangent plane, error under 0.1% within 25 km. Every authored coordinate sits on the game's 200 m chunk lattice, so a neighbouring slice or an intercity corridor shares chunk boundaries (the draft starts at (-2400, -3200)).
* **Modular data**: the road is route data, the buildings are catalogue prefabs placed by rules, the frame is separate from both. A second slice is a second route file and new rules; no foundation changes.
* **Landmarks reserved, not invented**: Dala Hill's one sourced coordinate is recorded; Gidan Rumfa, Kurmi Market and Kofar Mata have `null` coordinates and say so.
* **Prefab ids** are global to the game: all new ids are `kano_*`.

### 5.2 The per-asset sequence (what the toolchain already enforces and tests)
1. reference reading, written down as a table the owner corrects, then the blockout in Blender (primitives), reviewed;
2. modelling from kit modules; UVs; one atlas material using the shared `M_<surface>` names; LOD chain (last at most 10% of LOD0); `col_*` box;
3. `validate_asset` (the game's own budgets: triangles per LOD, scale, pivot, naming, UVs, collision), `save_project` (a new `vNNN`, editable `.blend`), `export_asset` FBX and GLB with manifest (refuses to overwrite, refuses a failing asset);
4. Unity: the import test (FBX and GLB; orientation, scale, materials, collision, LOD thresholds against the catalogue entry; **measured 4/4 on a real Unity 6 run**, `docs/BLENDER_PIPELINE.md` section 8); the prefab builder wraps the model under an identity root with its own LODGroup and BoxCollider;
5. the catalogue entry and the placement rule land in a pull request in this repository (text only); the binaries live in the private asset repository (`docs/ASSET_REPO_PROPOSAL.md`, awaiting your approval);
6. the owner reviews renders and the drive-through, then runs `approve` (freezes the version; the toolchain never overwrites an approved version).

### 5.3 Gates (nothing proceeds without your yes)
G0 this package approved (and the decisions in section 9) -> G1 reference pack delivered and my reading tables corrected by you -> **G2 first plot end to end** (`kano_hausa_compound_a`: Blender -> validate -> FBX/GLB -> Unity import test -> prefab -> catalogue entry -> placed by the draft route in a *test* route, measured) -> G3 production by family (traditional, modern, commercial, landmarks, street furniture, vegetation, vehicles) each with its import test -> G4 route registered in the game (a new `Resources/Routes` file and tests; **no existing system touched**) -> G5 driving and performance validation -> G6 your review and approval.

### 5.4 Verification Unity must pass before anything is called integrated
Scale and orientation (the import test), material and texture import (shared kit, URP/Lit, instancing), collision (box per plot; the gantry has none), **vehicle road alignment** (spawn at stations along the route, wheel contact against `RouteModel.GroundHeight`, no clipping into aprons), draw calls and frame time (`[Perf]` lines in the browser build; **real-GPU numbers are unvalidated project-wide**), chunk streaming (the existing streamer, ring 2).

---
## 6. Performance and polygon-budget plan
Numbers below are **computed from the real scatter and the game's own LOD policy**, not measured from any model (none exists) and not frame times. Method (`kano_slice_stats.json`): the player stands on the road centre line every 25 m; a production prefab counts only inside its cull distance (`LodPolicy.PropCullDistance`, scaled by the tier) at the LOD level `LodPolicy.PropLodLevel` gives; triangles are the PLANNED LOD budgets; each instance is one draw call (one mesh, one material); counted in all directions, so an upper bound; excludes terrain, road, vehicles and UI.

| Tier | density / cull scale | instances in the slice | alive at the worst moment | **drawn** draw calls, upper bound | planned triangles | buildings drawn |
|---|---|---|---|---|---|---|
| low | 0.5 / 0.6 | 178 | 142 | 52 (of 150) | 75,060 (of 250,000) | 31 |
| medium | 0.8 / 1 | 246 | 200 | 119 (of 400) | 121,000 (of 1,000,000) | 65 |
| high | 1 / 1.4 | 300 | 242 | 193 (of 400) | 127,380 (of 1,000,000) | 95 |

**Design targets** (asserted by the test, so a change that breaks them fails CI): production props use at most **60% of a tier's draw-call budget and 45% of its triangle budget** in any 1 km window, leaving the rest for terrain, road, vehicles and UI. Desktop budget: 1,000,000 triangles, 400 draw calls, 300 MB textures; mobile (the Low tier): 250,000, 150, 150 MB (`PerfBudget`).
* **What drove the design:** my first estimate (everything within 5 x 5 chunks at detail) exceeded my own target (256 draw calls against 240). I changed the *design*: median-only lights on the dual carriageway, fewer shrubs, parked vehicles merged into two grouped meshes. The estimate was then replaced by the distance-based one above because the ring-based one ignored the game's own cull and LOD policy; the targets did not move.
* **Texture budget (plan):** one atlas per building family at 2048 x 2048 and 1024 x 1024 for props, shared across the kit. Arithmetic: a 2048 x 2048 RGBA texture with mipmaps is about 5.6 MB compressed (DXT5 class, 8 bits per pixel) and about 22 MB uncompressed; a 1024 x 1024 one about 1.4 MB and 5.6 MB. Eight of the first plus twenty of the second are about 73 MB compressed and about 290 MB uncompressed. Compressed, that fits both tiers (150 MB mobile, 300 MB desktop); uncompressed it fits neither comfortably. **I have not verified the WebGL build's texture compression setting.**
* **What is not validated:** actual draw calls (the game's SRP batcher merges setup, not meshes), frame time on any GPU, WebGL memory. G5 measures these with the existing `[Perf]` probe.

---
## 7. The greybox: what was built and what it showed
`tools/blender/scripts/kano_greybox.py` builds the scene through the MCP server from `kano_slice_geometry.json` (the game's road cross-sections extruded along its centre lines, its terrain heights, and the 300 prefab instances the scatter and set pieces produce) and the inventory envelopes. Everything is a plain volume, coloured by **category** (a review aid, not a materials claim): traditional plots ochre, modern plots blue-grey, commercial yellow, tall modern lilac, mosque green, petrol station red, street props orange, poles/lights/signs black, trees green, vehicles blue. The `.blend` was saved as `environments/kano_slice_greybox/v002` in the container workspace (editable source; the container is temporary and the private asset repository does not exist yet, so it is **not** preserved anywhere durable until you approve that repository).

What the renders showed (and what changed because of it): the side streets were empty roads (the scatter ignores them) -> hand-placed plots added; the entrance trees read as oversized and uniform -> tree envelopes need reference sizes; the first overview was useless -> replaced by an oblique view and three zoomed plans. A greybox invites judgement of **rhythm, scale and layout only**.

---
## 8. Required deliverables checklist
| Deliverable | Where | State |
|---|---|---|
| 1. Kano visual reference board | sections 1.1-1.4, plus the board page (images with readings) | built from 5 supplied photographs; **locations and rights unconfirmed**; no Hausa compound reference yet |
| 2. Proposed playable-area layout | section 2 | proposal |
| 3. Road network and intersection blockout | section 3, `ng-kano-slice.route.draft.json` | validated by the game's own rules; engine limits listed |
| 4. Asset inventory | section 4, `kano_asset_inventory.json` | planned, budget-validated; **nothing modelled** |
| 5. Blender-to-Unity production plan | section 5 | plan, on a toolchain whose import path is measured |
| 6. Performance and polygon-budget plan | section 6 | computed, **not measured** |
| Initial greybox with renders | section 7 | built and rendered |

## 9. Decisions and blockers
1. **Approve or change the package**: slice scope, the fictional-corridor approach, the zone order, the `kano_*` ids.
2. **Engine limits (section 3.2)**: stay inside the corridor model for the slice (recommended: it needs no change to any existing system), or approve targeted road-generator work (4-way junctions, roundabout, kerb/drain/sidewalk strip, road-detail layer for patches and cracks, production sign text). Each would modify an existing system and needs your explicit approval and its own tests.
3. **The Kano coordinate discrepancy** (8.2 km, section 3.3) and **approval of frame K0**.
4. **References (blocker for all modelling)**: confirm what images 2-5 show and your rights to use them as references (image 5 carries a photographer's watermark), and supply the items in section 1.4.
5. **Private asset repository**: `docs/ASSET_REPO_PROPOSAL.md` still awaits your approval; until it exists no `.blend` or export is stored durably.
6. **Optional landmark**: Dala Hill as a distant backdrop prop is possible without an engine change (a large low-detail prop with a long cull distance) but its real height and form are unresolved in the sources.
7. **Texture compression**: confirm the WebGL build's texture compression so the texture budget can be checked rather than assumed.
