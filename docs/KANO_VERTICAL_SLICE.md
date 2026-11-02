# Kano vertical slice: review package, revision 2

> **SUPERSEDED IN SCOPE (2026-11-01).** The owner corrected the scope: the 1.65 km corridor described here is only a **road-system test corridor, now called TC-1**; it is **not** the city. The deliverable is Kano City. See **`KANO_CITY_MASTER_PLAN.md`** for the master plan, and `kano/city/` for the districts, road network, reference-to-asset matrix and asset registry. This package's findings on references, coordinates, texture compression and the engine backlog still stand and are referenced from the master plan.

**Status (2026-11-01): PRE-PRODUCTION. Detailed modelling has NOT started and will not until the reference and coordinate questions are resolved and the final layout is approved.** Nothing here is a finished asset, road or playable Kano. The draft greybox of revision 1 is kept as a reviewable draft (`environments/kano_slice_greybox/v002` in the workspace; its renders are on the review page). This revision answers the owner's review decisions of 2026-11-01. **The Kano package is committed locally only; it is not pushed or merged** until the CI and asset-storage decisions are resolved.

| # | Deliverable | Where | State |
|---|---|---|---|
| 1 | Reference provenance and usage-rights report | `docs/kano/REFERENCE_REGISTER.md`, `reference_register.json` | done: all five images classified; **no location established, rights unknown**; missing-reference list of nine areas |
| 2 | Private asset repository structure and integration workflow | `docs/ASSET_REPO_PROPOSAL.md`, `tools/asset-repo-template/` | **repository `AffanNamu/ARO-CONTENT` created by the owner (private, checked) and the text-only skeleton pushed**; template and validator tested (9 tests); its own validation workflow passed on the first push |
| 3 | Verified Kano coordinate recommendation | `docs/kano/COORDINATES.md`, `kano_world_frame.json` | done; **no coordinate changed**; canonical anchor and transform recommended, impact documented |
| 4 | Road-system limitations and future-improvement backlog | `docs/ROAD_ENGINE_BACKLOG.md`, `kano_workarounds.json` | done; **shared road engine untouched**; five Kano workarounds declared, all data-only and test-enforced |
| 5 | Confirmed Unity texture-compression report | `docs/TEXTURE_BUDGET.md`, `texture_findings.json` | from a real Unity 6000.0.58f2 run in CI (cold import); **four findings: one defect (the world map ships uncompressed), one setting (Gamma colour space), two risks** |
| 6 | Final greybox review renders and layout | the review page (layout diagram, plans, eight street views) | layout unchanged by the decisions; shown with annotations |
| 7 | Updated asset inventory and production budgets | sections 4 and 6 below | regenerated; per-prefab reference blockers; budgets from the real settings |

Evidence files (all in `docs/kano/`, regenerated and checked by `tests/NetCore.Tests/KanoSliceTests.cs`, 10 tests, which passes with the rest of the core suite): the route draft, the inventory, the world frame, the workaround register, the reference register, the generated geometry and statistics. Generators: `tools/kano/*.py`. Renders and the supplied images are **not** committed (binary files stay out of the public repository).

---
## 1. References: classification and what is missing
The owner clarified that the five images were **researched and supplied by ChatGPT; the owner took screenshots and forwarded them**. They are therefore classified as **ChatGPT-provided visual research references**: not original photographs by the owner, not verified images of any named place, not production assets. **No image is claimed to show Kano.** Full record, hashes and per-image reasoning: `docs/kano/REFERENCE_REGISTER.md`.

| Id | What it is | Owner's interpretation | Location | Rights |
|---|---|---|---|---|
| R-001 | Dense traditional settlement around a rocky hill | Visual reference for the Dala Hill environment, subject to geographical verification. | not established | rights unknown |
| R-002 | Modern residential district (phone screenshot of R-003) | Essentially the same image as R-003: contemporary residential layouts, building heights, roofs, greenery and street arrangements. | not established | rights unknown |
| R-003 | Modern residential district | Contemporary residential layouts, building heights, roofs, greenery and street arrangements. | not established | rights unknown |
| R-004 | Large urban junction and roundabout | Urban junction and roundabout design reference. Do not implement a roundabout until the shared road system supports it. | not established | rights unknown |
| R-005 | Large highway interchange | Highway interchange reference. Future road-infrastructure inspiration, not immediate implementation. | not established | rights unknown |
| R-006 | Gated modern residential street, one-way | Visual reference for modern residential districts. | not established | rights unknown |
| R-007 | Curved dual-carriageway expressway through hills | Expressway design reference. | not established | rights unknown |
| R-008 | A2 expressway with overhead gantry (planter median) | A2 gantry reference. | not established | rights unknown |
| R-009 | Kano Central Bus Terminal | Transport terminal reference. | not established | rights unknown |
| R-010 | Specialist hospital campus | Hospital reference. | not established | rights unknown |
| R-011 | Metropolitan police station beside a divided arterial | Police station reference. | not established | rights unknown |
| R-012 | A2 expressway with overhead gantry (concrete median barrier) | A2 gantry variant reference. | not established | rights unknown |
| R-013 | Truck fuel and service centre | Truck fuel and service reference. | not established | rights unknown |
| R-014 | Warehouse and logistics yard (aerial) | Warehouse yard reference (aerial). | not established | rights unknown |
| R-015 | Warehouse and logistics yard (ground level) | Warehouse yard reference (ground level). | not established | rights unknown |

* **Original source of every image: unknown** (the screenshots carry no page address or credit). R-005 carries a photographer's watermark ("NUUMAN ASHAKA"); a web search found no portfolio or profile for that name.
* **Allowed uses:** private reference only (palette, density, rhythm, layout ideas), pending the owner's decision. **Public documentation: no.** **Production redistribution: no** (never a texture, decal, reference plane or part of any build). The images are not stored in either repository; a SHA-256 identifies each. The review page that shows them is private and must not be shared.
* **R-004 (roundabout) and R-005 (interchange)** are design inspiration for backlog items RB-01 and RB-03 only; nothing is implemented from them.
* Generated concept images may supplement real photographs but must be labelled **CONCEPT: not a photograph of an actual Kano location**.

### Missing references (what authentic Kano architecture and streets still need)
Each entry in `reference_register.json` lists the shots needed, the prefabs and kit modules it unblocks, and acceptable provenance (owner-taken, licensed or commissioned, openly licensed per file, archival terms read; never copied or traced).
| Id | Area | Status |
|---|---|---|
| M-01 | Traditional Hausa compounds and gates | MISSING (no usable reference; R-001 is too distant) |
| M-02 | Modern Kano buildings | CONCEPTS SUPPLIED for modern villas, hospital, police station, fuel/service and warehouses (R-006, R-010, R-011, R-013 to R-015: generated, not Kano-verified); Kano-specific modern buildings still need real references |
| M-03 | Mosques | PARTIAL: a generated skyline mosque recurs in R-006/R-008/R-009/R-010/R-012 (owner to say whether it stands for the Central Mosque); R-004 shows one real-looking mosque type, location not established; no real Kano mosque reference |
| M-04 | Commercial streets and markets | MISSING for traditional markets and streets; modern commercial frontage is not covered by any concept |
| M-05 | Road surfaces and drainage | CONCEPTS SUPPLIED for road surfaces, kerbs, paver sidewalks, markings and drains (R-006, R-007, R-011, R-013, R-015: generated); real Kano surfaces, patches, cracks and drains still MISSING |
| M-06 | Electricity poles and streetlights | CONCEPTS SUPPLIED for streetlights and median double-arm lights (R-006 to R-008, R-011, R-012); poles, transformers and cables as installed in Kano still MISSING |
| M-07 | Northern Nigerian trees and vegetation | CONCEPT ONLY (broad-leaf street trees, ixora and hedges in R-006, R-007); the species that really line Kano roads are MISSING |
| M-08 | Local vehicles and roadside activity | CONCEPTS SUPPLIED for coaches, trucks, an ambulance, police pickups and SUVs (R-009, R-010, R-011, R-013 to R-015); motor tricycles, motorcycles and minibuses as seen in Kano still MISSING |
| M-09 | Light and atmosphere | PARTIAL (R-005 for haze, location not established) |
| M-10 | Schools, government buildings, hotels | MISSING |
| M-11 | Landmark references: palace gate, Great Mosque, Kurmi Market, Kofar Mata dye pits, city wall and gates, Dala Hill | MISSING |
| M-12 | Old-city lanes and traditional residential streets | MISSING |
| M-13 | Interchanges, flyovers and roundabouts | INSPIRATION ONLY (R-004, R-005); nothing buildable yet |

**Verified facts about Kano** (Gidan Rumfa, Kurmi Market, Dala Hill, Kofar Mata, the walls, Hausa building features, Harmattan) with sources, and what was searched for and not found, are in revision 1 of this document's research section, preserved in `docs/kano/RESEARCH.md`.

---
## 2. Layout (unchanged by the decisions)
One fictional corridor, 1.65 km at 1:1 scale, authored in the proposed Kano frame `K0` (section 5.1) and connected to the existing world-map route `kaduna-kano`. Zones in driving order: **city entrance** (divided road, median lights, gantry), **modern district**, **commercial spine** (with a 150 m damaged section), **old-neighbourhood edge** with a qibla-oriented neighbourhood mosque. Six staggered T-junctions lead to side streets whose first 120 m are lined with plots. The annotated plan is on the review page; the real landmarks (Gidan Rumfa, Kurmi Market, Dala Hill, Kofar Mata) are not in this slice and are reserved for an old-city slice (R-001 is the candidate reference for a Dala Hill environment, subject to geographic verification).

---
## 3. Road network: the decision and the workarounds
**Decision: preserve the current road-generation architecture.** The slice is authored as route data in the game's own format and validated with the game's own `RouteSpecValidator` and `RouteModel` (no building overlaps another or enters a road; the mosque faces the qibla within 1 degree; every plot has its no-apron twin planned). The limits found, and the future improvements (roundabouts, four-way intersections, flyovers, sidewalks and kerbs, drainage, surface-condition layers, text-bearing signs, side-street scatter, and more), are recorded in **`docs/ROAD_ENGINE_BACKLOG.md`** as RB-01 to RB-11 with what proper work involves.

**No permanent Kano-specific workaround.** The five workarounds the slice uses are declared in `docs/kano/kano_workarounds.json`; each names the backlog item that removes it and a **data-only** removal path; `KanoSliceTests.EveryWorkaroundIsDeclaredAndDataOnly` fails if a placement is neither a declared workaround nor a declared set piece (checked with a negative control).
| Id | Workaround | Removed by | Removal path |
|---|---|---|---|
| W-01 | A 4.5 m open front apron (drain slab, walkway, pole line) inside each plot prefab | RB-04, RB-05 | in the route rules replace each plot prop id by its `_noapron` variant and move the offset 4.5 m towards the road (data only) |
| W-02 | Side-street plots as hand-placed set pieces (the scatter runs along the main road only) | RB-08 | delete the `side_*` placements and add scatter rules for the side corridors (data only) |
| W-03 | Median double streetlights as hand-placed set pieces (rule offsets only go outward from the formation edge) | RB-09 | replace the placements with a median rule (data only) |
| W-04 | The word KANO baked into the gantry sign prefab (production prefabs get no text) | RB-07 | point the placement at the text-bearing sign prefab; the placement already carries the text (data only) |
| W-05 | Staggered T-junctions at least 150 m apart instead of four-way crossings | RB-02 | replace a staggered pair by one four-way junction in the route data (data only) |

---
## 4. Updated asset inventory
29 planned prefabs and 12 Blender kit modules. Every prefab now names the reference gaps that **block it** (register ids), and none is ready to model. Plot prefabs are exported twice from one Blender source: with the 4.5 m apron (W-01, used now) and as `<id>_noapron` (catalogued only when RB-04/RB-05 exist). The "placed" column is the count in the full slice at full density, generated from the real scatter. All ids are new (`kano_*`); dimensions are placeholder envelopes.

| id | category | w x d x h (m) | planned LOD triangles | collider | cull (m) | placed | blocked by | ready to model |
|---|---|---|---|---|---|---|---|---|
| `kano_hausa_compound_a` | buildings | 18 x 26 x 5.2 | 5400/1700/480/100 | box | 300 | 13 | M-01 | blocked |
| `kano_hausa_compound_b` | buildings | 20 x 28 x 7.4 | 5600/1800/500/100 | box | 300 | 12 | M-01 | blocked |
| `kano_hausa_compound_c` | buildings | 13 x 21 x 4.6 | 4300/1400/400/90 | box | 300 | 14 | M-01 | blocked |
| `kano_compound_modern_a` | buildings | 20 x 30 x 6.2 | 5800/1900/520/100 | box | 300 | 13 | M-02 | blocked |
| `kano_compound_modern_b` | buildings | 20 x 30 x 8.6 | 5900/1900/520/100 | box | 300 | 14 | M-02 | blocked |
| `kano_shop_row_a` | buildings | 18 x 12 x 4.2 | 3300/1100/320/80 | box | 300 | 21 | M-02, M-04 | blocked |
| `kano_shop_row_b` | buildings | 20 x 13.5 x 7.4 | 4700/1500/420/90 | box | 300 | 21 | M-02, M-04 | blocked |
| `kano_plaza_a` | buildings | 30 x 26 x 13 | 5900/1900/520/110 | box | 450 | 1 | M-02 | blocked |
| `kano_apartment_block_a` | buildings | 22 x 20 x 10.5 | 5300/1700/480/100 | box | 420 | 1 | M-02 | blocked |
| `kano_midrise_a` | buildings | 26 x 24 x 24 | 5900/1900/540/110 | box | 650 | 2 | M-02 | blocked |
| `kano_mosque_neighbourhood_a` | buildings | 30 x 34 x 24 | 5900/1900/540/110 | box | 650 | 1 | M-03 | blocked |
| `kano_filling_station_a` | buildings | 28 x 30 x 7 | 4800/1600/450/100 | box | 420 | 1 | M-02 | blocked |
| `kano_road_sign_small` | signs | 1.2 x 0.3 x 3.2 | 420/140/40 | box | 200 | 9 | M-06 | blocked |
| `kano_road_sign_gantry` | signs | 16 x 1.2 x 6.5 | 780/260/80 | none | 450 | 1 | M-06 | blocked |
| `kano_billboard_a` | signs | 12 x 0.8 x 9 | 700/240/60 | box | 500 | 1 | M-04 | blocked |
| `kano_streetlight_single` | lights | 2.4 x 0.5 x 9 | 560/200/60 | box | 300 | 42 | M-06 | blocked |
| `kano_streetlight_double_median` | lights | 4 x 0.5 x 10 | 820/280/70 | box | 320 | 11 | M-06 | blocked |
| `kano_utility_pole` | props | 1.6 x 0.5 x 9.5 | 380/140/40 | box | 260 | 28 | M-06 | blocked |
| `kano_transformer_pole` | props | 2 x 1.4 x 9.5 | 900/300/70 | box | 260 | 3 | M-06 | blocked |
| `kano_kiosk_a` | props | 3 x 3 x 2.6 | 900/300/80 | box | 220 | 3 | M-04 | blocked |
| `kano_market_stall_a` | props | 3.5 x 3 x 2.6 | 1000/340/90 | box | 220 | 4 | M-04 | blocked |
| `kano_bus_stop_a` | props | 6 x 3 x 3 | 1500/500/120 | box | 260 | 2 | M-04 | blocked |
| `kano_parked_group_a` | props | 7 x 4 x 1.7 | 2300/800/260/70 | box | 220 | 12 | M-08 | blocked |
| `kano_parked_group_b` | props | 6.5 x 4 x 1.8 | 2300/800/260/70 | box | 220 | 13 | M-08 | blocked |
| `kano_parked_minibus_a` | props | 1.9 x 5.2 x 2.3 | 2100/700/200/60 | box | 240 | 1 | M-08 | blocked |
| `kano_neem_tree_a` | vegetation | 8 x 8 x 9 | 1800/600/160/40 | none | 320 | 23 | M-07 | blocked |
| `kano_neem_tree_b` | vegetation | 9 x 9 x 11 | 2100/700/180/40 | none | 340 | 19 | M-07 | blocked |
| `kano_date_palm_a` | vegetation | 5 x 5 x 12 | 1500/500/120 | none | 340 | 5 | M-07 | blocked |
| `kano_shrub_a` | vegetation | 2 x 2 x 1.4 | 260/100/30 | none | 140 | 9 | M-07 | blocked |

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

### 5.1 World architecture and coordinates
Recommended (not applied; see `docs/kano/COORDINATES.md`): a **canonical geographic anchor** (`kano_city_anchor` 12.00012N 8.51672E, GeoNames, cross-checked) and a **separate transform** `K0` into Unity coordinates (local tangent plane, metres, x east, z north, 200 m chunk-lattice aligned; error 5 m at 10 km and 17 m at 25 km measured against the WGS84 geodesic). The world-map pin (12.0022N, 8.592E, 8.2 km from the anchor, source unrecorded, like every other city pin offset by 1.7 to 10.8 km from GeoNames) is kept as a map pin and not used for geometry. **Neither number was replaced.** Impact on existing corridors: none (Lagos-Ibadan uses no latitude or longitude); future corridors anchor to the canonical anchor and declare their compression.

### 5.2 The per-asset sequence
Reference reading written down and corrected by the owner -> blockout -> modelling from kit modules, UVs, one atlas material with the shared `M_<surface>` names, LODs, `col_*` box -> `validate_asset` (the game's own budgets) -> `save_project` (new `vNNN`) -> `export_asset` FBX and GLB with manifest (refuses to overwrite, refuses a failing asset) -> Unity import test (measured 4/4 on a real Unity 6 run) -> prefab builder (identity root, own LODGroup and BoxCollider, shared-kit materials) -> committed to the **private asset repository** with its manifest and licence row -> catalogue entry and placement rule here (text only) -> owner reviews and runs `approve`.

### 5.3 Gates (nothing proceeds without the owner's yes)
G0 review package approved -> **G1 references classified and the missing list filled** (blocker) and **coordinate decision** (blocker) -> G2 final layout approved -> G3 first plot end to end (`kano_hausa_compound_a`: Blender -> validate -> export -> Unity import -> prefab -> catalogue -> placed in a *test* route) -> G4 production by family -> G5 route registered in the game (a new `Resources/Routes` file and tests; no existing system touched) -> G6 driving and performance validation (vehicle alignment, collisions, `[Perf]` in the browser build) -> G7 owner review and approval.

---
## 6. Production budgets
### 6.1 Geometry and draw calls (computed from the real scatter and the game's LOD policy; not measured, not frame time)
Method (`kano_slice_stats.json`): the player stands on the centre line every 25 m; a prefab counts only inside its tier-scaled cull distance at the LOD level `LodPolicy.PropLodLevel` gives; planned LOD triangles; one draw call per instance; all directions, so an upper bound; excludes terrain, road, vehicles and UI.
| Tier | density / cull scale | instances in slice | alive at the worst moment | **drawn** draw calls, upper bound | planned triangles |
|---|---|---|---|---|---|
| low | 0.5 / 0.6 | 178 | 142 | 52 (of 150) | 75,060 (of 250,000) |
| medium | 0.8 / 1 | 246 | 200 | 119 (of 400) | 121,000 (of 1,000,000) |
| high | 1 / 1.4 | 300 | 242 | 193 (of 400) | 127,380 (of 1,000,000) |

Design targets asserted by the test: props use at most **60%** of a tier's draw-call budget and **45%** of its triangle budget in any 1 km window. Real-GPU frame time is unvalidated project-wide.

### 6.2 Textures (from the actual Unity settings: `docs/TEXTURE_BUDGET.md`)
Measured with a real Unity 6 cold import (docs/TEXTURE_BUDGET.md). WebGL and Windows pick DXT1/DXT5 automatically, there is no build-wide override, the default maximum is 2048, the project is in **Gamma** colour space, and Android/iOS have overrides but no build entry point. The game uses 22.3 MiB of texture memory in the WebGL import and measures 5.6 MB (low) / 8.4 MB (medium) while driving. Verified sizes: a 2048 opaque albedo is 2.67 MiB (DXT1), a 1024 normal map 1.33 MiB (DXT5). The Kano plan (6 building atlases, 4 street-prop atlases, 2 vegetation atlases) is **36.0 MiB**, about 44 MB with the driving baseline: 15% of the desktop budget and 30% of the mobile budget, **if the target compresses** (unverified on phones). Four findings in the existing project: a defect (the world map ships uncompressed, 6.0 MiB instead of 1.3, because of npotScale = None on a non-power-of-two image), a setting (Gamma, not Linear, colour space) and two risks (S3TC support on phones is unverified; Android and iOS have no build entry point).

---
## 7. The greybox
The draft greybox (placeholder volumes generated from the game's own road model, terrain and placement; category colours; `environments/kano_slice_greybox/v002`) is kept unchanged as a reviewable draft: the decisions of 2026-11-01 do not change the layout. The review page adds the annotated layout diagram. A greybox invites judgement of **rhythm, scale and layout only**; it contains no architecture.

---
## 8. Decisions and blockers
1. **Private repository:** `AffanNamu/ARO-CONTENT` exists, is private and holds the text-only skeleton and the metadata-only reference register (no image, no binary, no secret). Still open: the artifact boundary for browser tests ((a) fold, (b) encrypt, recommended (b)), the Git LFS quota, written confirmation of asset ownership, and the read token and variable (yours to set).
2. **References (blocks all modelling):** establish the original source, location and rights of R-001 to R-005 if you can, and supply the nine missing-reference areas of the register.
3. **Coordinates:** approve the canonical anchor and `K0` transform (no existing file changes) and decide whether world-map `lat/lon` means a pin or an anchor (a change to all eight pins and the live backend seed, authorised and migrated separately).
4. **Texture findings (section 6.2):** four findings in the existing project (a defect, a setting, two risks) are reported for your decision; none was changed.
5. **CI and branches:** the Kano package stays local until you resolve the above and say so. The camera branch is verified independently (see the report in the conversation); the texture-report branch (`work/texture-report`) is separate and contains only a report tool and its workflow.
6. **Network access (optional):** to read OpenStreetMap, Wikidata or the GeoNames API directly for coordinate verification, add `nominatim.openstreetmap.org`, `www.wikidata.org` and `api.geonames.org` to the environment's allowed domains.
