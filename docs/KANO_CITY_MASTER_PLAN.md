# Kano City master plan (KSA-1), draft for owner approval

**Status (2026-11-01): PLAN ONLY. Nothing in this document is modelled, surveyed or read by the game. No detailed modelling has started and none will start until the owner has reviewed and approved this direction.** Every position, district boundary, road and plot is an **indicative fictional placement**, except where a landmark coordinate is marked VERIFIED and its source and firmness are given. A greybox is not a city, and nothing here claims otherwise.

This replaces the scope of the earlier "Kano vertical slice" package (`KANO_VERTICAL_SLICE.md`). Its 1.65 km corridor is kept, renamed **TC-1**, only as the road-system test corridor. The deliverable is a city.

---
## 0. Acknowledgement and scope reassessment

**The correction is accepted.** The previous package treated a 1.65 km road strip as the "vertical slice" of Kano. That is the wrong unit: a strip proves that the road engine can carry a route; it says nothing about a city's layout, districts, density, landmarks, asset reuse or streaming. This plan therefore sets out:

| # | Required by the owner | Where |
|---|---|---|
| 1 | A spatial master layout for a substantial city area | section 3 |
| 2 | Multiple connected districts and neighbourhoods | section 2 and section 5 |
| 3 | A road network with hierarchies | section 4 |
| 4 | Building-block and neighbourhood layouts | section 5 |
| 5 | Proposed landmark locations from verified geography | section 6 |
| 6 | Traditional, modern and commercial styles | section 7 |
| 7 | A 3D production strategy | sections 8 and 9 |
| 8 | A modular asset architecture | section 10 |
| 9 | A performance and world-streaming strategy | section 11 |
| 10 | A phased plan | section 12 |

Also delivered: the **reference-to-asset production matrix** (`kano/city/REFERENCE_ASSET_MATRIX.md`, section 9), the **shared asset library and registry** (section 10; `kano/city/asset_registry.json`, mirrored in the private repository), the **engine improvements required** (section 13, `ROAD_ENGINE_BACKLOG.md` RB-12 to RB-17) and the **decisions needed from the owner** (section 15). ChatGPT is to supply visual concepts (including the list in section 16); this repository supplies the technical inspection, Blender modelling, optimisation, validation and Unity integration.

### Honesty rules this plan follows

* Concepts are design specifications. Each is reproduced as **genuine 3D geometry** (walls, roofs, openings, structure, ornament), never as an image on a plane, never as a texture, and never redistributed.
* A concept is never presented as a verified image of Kano. The ten concepts are idealised, generated scenes; place names and sign text in them are illustrative.
* Where a concept needs a change to be buildable (engine limit, budget, rights, factual error) the change is **explained to the owner first** and never made silently.
* Where nothing exists to copy (Hausa compounds, old-city lanes, markets, the palace, the Great Mosque, schools, government buildings, hotels) the work is **BLOCKED on a reference**, and nothing is invented to fill it.
* Work stays within the current engine's limits. What the engine cannot do is documented as a backlog item (RB-12 to RB-17), not worked around permanently in Kano data.

### Update 2026-11-08: the transportation expansion brief

The owner's brief of 2026-11-08 (seven roundabout designs, urban highways, flyovers, logistics facilities, bus terminals, supporting infrastructure, day and night) is answered by **`docs/KANO_TRANSPORT_EXPANSION_PLAN.md`**: the inspection of the current road, prop, vehicle, job and bus systems, the technical proposal for the engine work it needs, the order of work, the phase-by-phase report and the decisions requested. It changes nothing in this plan's districts or network; it adds:

* **Where the brief lands here.** Roundabouts, four-way junctions, flyovers and interchanges are RB-01 to RB-03 and stages E3 to E5 of `ROAD_ENGINE_PROPOSAL.md`; the 21 of 54 junctions this plan says the engine cannot build are the same ones. Logistics (D10) and bus terminals (D09) need only E1 and E2 (RB-13 site plates, RB-07 text signs, kerbs), which is why the proposed order builds them before the roundabouts.
* **Seven roundabout designs on the same 54 nodes.** The four ring roundabouts (RR0, RR90, RR180, RR270) and two promoted four-way crossings (MA0, MA225) carry six designs; the interchange node RR225 carries the seventh (a grade-separated roundabout interchange). The junction count and the 21 unsupported junctions are unchanged. Proposed mapping: landmark MA225, modern urban RR90, traditional RR180, large multi-lane RR270, commercial MA0, green landscape RR0, highway interchange RR225. Indicative, for the owner to confirm.
* **Flyover candidates:** the ring interchange RR225, the A2 over the logistics junction X_LOG, and a radial arterial over the middle ring (for example MA270). They are corridors of the graph, never isolated structures.
* **Phase numbering.** The brief's phases 1 to 6 (roundabouts, highways and flyovers, logistics, bus terminals, supporting infrastructure, lighting) are work packages inside this plan's P1b to P5; they do not replace section 12. The proposed execution order is in section 6 of the expansion plan.
* **New references R-030 to R-036** (night, wet-road, highway, roundabout, flyover and interchange scenes) are registered as `generated_night_reference`: design references for lighting, signage and infrastructure layout; hash only, nothing stored; real brand marks and sign text in them are not used.
* **Nothing is built.** No engine file changed, no asset added; the lighting foundation (`docs/NIGHT_LIGHTING_PLAN.md`) is the only implementation work, and it adds code, catalogue entries and route rules only.

### Status vocabulary (an asset moves through these in order and never skips one)

| Status | Meaning |
|---|---|
| `needs_concept` | no concept or verified reference exists |
| `concept_approved` | a concept exists and the owner approves it as the standard |
| `blockout_completed` | primitive geometry at correct size and proportion, compared against the concept at the reference camera |
| `detailed_modelling_completed` | full geometry (LOD0) with real structure and ornament |
| `materials_completed` | UVs and PBR materials with shared `M_<surface>` names |
| `blender_validation_passed` | geometry, scale, normals, budgets and naming checked by the Blender validator |
| `unity_import_verified` | imported into Unity and measured (orientation, scale, materials, collision, LODs) |
| `in_game_visual_review_passed` | in-game screenshot at the reference camera compared with the concept and accepted |
| `production_approved` | the owner approves it for the game; the registry then records source, export, prefab, validation and approver |

**Today every one of the 132 assets sits at `concept_approved` (113 concept-backed) or `needs_concept` (19 blocked), with the asset breakdown still awaiting the owner's approval. Not one has a Blender source.**

---
## 1. The study area

* **KSA-1** is a 100 km² square (10 km x 10 km, 2,500 chunks of 200 m) in the local frame **K0** (x east, z north, metres, aligned with the game's chunk lattice), centred on the old city, not on the GeoNames point (the old city's centre sits at x 0, z -200 m).
* **The frame** (`kano/COORDINATES.md`, `kano_world_frame.json`) is a local tangent plane at the canonical anchor GeoNames Kano 12.00012° N, 8.51672° E (Wikipedia agrees to 13 m). Its error against a geodesic is 0.4 m at 1 km, 5.1 m at 10 km and 17 m at 25 km, so no point of the 10 km study area (at most about 7 km from the anchor) is off by more than about 5 m. **No coordinate in the game, backend or any route has been changed.** The existing world-map pin (12.0022° N, 8.592° E) is 8.2 km east of the anchor and has no recorded source; that decision is the owner's (section 15).
* **The old city** is drawn as an ellipse of 2300 x 2100 m whose perimeter matches the 14 km UNESCO figure for the wall (the Daily Trust gives 17 km; sources disagree). **It is indicative.** The real wall line has not been surveyed here; it needs OpenStreetMap or a survey.
* **The ring road** (radius 3300 m) and every other road are indicative.
* **TC-1** (the former corridor, 1.65 km, bearing 36° out of the south-west) remains in the repository as the road-system test: `ng-kano-slice.route.draft.json`. It tests the existing engine; it is not part of the city.

### What is verified and what is not

| Item | Position | Status | Source and firmness |
|---|---|---|---|
| Canonical anchor (Kano) | 12.00012° N, 8.51672° E | VERIFIED | GeoNames id 2335204; Wikipedia agrees to 13 m; OSM's place node is 0.9 km away (one more reason it is a locality point, not a monument) |
| Gidan Rumfa | about 11.9923° N, 8.5160° E (K0 -66, -874) | VERIFIED within about 300 m | three web listings agree; not surveyed; a heritage inventory differs by 0.3 km |
| Dala Hill | 12.0093° N, 8.5070° E (K0 -1061, 1017) | ONE source | height disputed (534 m or 453.67 m above sea level) |
| Great Mosque, Kurmi Market, Kofar Mata Dye Pits | north of the palace, in the old city, near the wall | **UNVERIFIED placeholders** | sources give relations, not coordinates |
| Qibla | 68.46° from Kano (WGS84 geodesic) | computed | a neighbourhood mosque oriented to it is a proposal |
| Direction of the A2 entrance | from the south-west | firm to the great circle | the bearing Kaduna to Kano; the entrance position is chosen |
| Terrain | a nearly flat plain, about 484 to 488 m | firm to about 1 km | the green rocky hills of R-007 do not match Kano (section 14) |

**What would add real geography.** A local, offline extract of OpenStreetMap, Wikidata and GeoNames would give the wall line, main roads, the mosque and market positions and building footprints, each with licence and accuracy recorded. The proxy for this session blocks those hosts. Allowing them (or supplying an OSM extract) is the single step that most improves this plan; the layout here is **replaced**, not patched, when real data arrives. Until then the positions above stay marked indicative.

---
## 2. Districts and neighbourhoods

11 named districts and 3 filler zones. Plot counts and areas are computed from the district polygons and the density assumption in the plan (a planning assumption, to be replaced by measurement from references).

| Id | District | Kind | Geography | Area (ha) | Plots | Phase | Needs | State |
|---|---|---|---|---|---|---|---|---|
| **D11** | A2 expressway gateway | gateway | INDICATIVE | 109 | 54 | P1 | R-007, R-008, R-012 | ready to plan |
| **D10** | Industrial and logistics zone | industrial | INDICATIVE | 99 | 148 | P1 | R-013, R-014, R-015 | ready to plan |
| **D09** | Transport hub | transport | INDICATIVE | 69 | 138 | P1 | R-009 | ready to plan |
| **D08** | Institutional corridor | institutional | INDICATIVE | 167 | 501 | P1 | R-010, R-011 | hospital and police ready to plan; schools, government, hotels need concepts |
| **D06** | Modern residential estate | modern residential | FICTIONAL | 155 | 1,860 | P1 | R-006 | ready to plan |
| **D05** | Commercial belt | commercial | INDICATIVE | 143 | 3,575 | P2 | M-02, M-04 | needs new concepts |
| **D01** | Old city hero sector | traditional | REAL_ANCHORED | 255 | 7,300 | P3 | M-01, M-12 | **BLOCKED on references** |
| **D02** | Palace and Central Mosque precinct | landmark | REAL_ANCHORED | 51.5 | 52 | P4 | M-11 | **BLOCKED on references** |
| **D03** | Kurmi market quarter | market | INDICATIVE | 21 | 1,260 | P3 | M-04, M-11 | **BLOCKED on references** |
| **D04** | Dala Hill district | traditional | REAL_ANCHORED | 71 | 2,485 | P4 | R-001, M-11 | **BLOCKED on references** |
| **D07** | Traditional residential suburb | traditional | FICTIONAL | 143 | 4,290 | P3 | M-01, M-12 | **BLOCKED on references** |

`geography`: **REAL_ANCHORED** means tied to a verified or sourced real feature; **INDICATIVE** means placed by plan logic where the real position is not verified; **FICTIONAL** means an invented neighbourhood, not a real one.

**The three visual families.**

* **Modern residential** (D06): gated villas, compound walls, one-way loop streets, paver sidewalks, painted kerbs, street trees. Reference R-006.
* **Commercial and civic** (D05, D08, D09, D10, D11): shopping streets (no reference yet), hospital, police, bus terminal, logistics, highway gateway. References R-007 to R-015.
* **Traditional** (D01, D02, D03, D04, D07): Hausa compounds, old-city lanes, markets, the palace, the Great Mosque, Dala Hill. **BLOCKED**: R-001 (a distant view of a rocky hill) is the only picture, its location is not established, and it cannot teach compound architecture. They are scheduled after the references arrive.

**Fidelity zones.** Not everything is hand-built.

| Zone | Name | Rule | Where |
|---|---|---|---|
| Z-A | Hero districts | hand-authored layout and hero buildings; full prefab LOD0 and LOD1 | D01 to D11 |
| Z-B | Filler fabric | inside the ring road but outside hero districts: generated from the kit by rules (plots, streets), prefab LOD1 and baked blocks only | rest of the ring |
| Z-C | Distant city | beyond the ring road: terrain tint, baked skyline and impostors only; haze limits the view | outside the ring |

Filler zones inside the study area:

| Filler | Zone | Where | Character | Plots/ha |
|---|---|---|---|---|
| F-OLD | Z-B | inside the old-city ellipse, outside the hero sector | traditional compound fabric (BLOCKED on references, M-01) | 40.0 |
| F-MID | Z-B | between the wall and the ring road, outside hero districts | modern residential, commercial frontage and institutional plots | 12.0 |
| F-OUT | Z-C | beyond the ring road to the edge of the study area | low-density plots, farmland and scrub, terrain tint, baked skyline and impostors | 2.0 |

---
## 3. Spatial master layout

![Master plan](kano/city/img/master_plan.png)

*Planning diagram, not a map and not concept art.* Concentric layout: the old city and its (indicative) wall at the centre, a middle belt of mixed urban fabric, the outer ring road, and a peri-urban fringe to the edge. The A2 highway enters from the south-west to the gateway, the logistics zone and the ring interchange. Hero districts are shown as polygons; they overlap nothing (tested).

The layout logic is deliberately simple so it can be replaced by real data without changing anything downstream: **a district is a polygon plus a type; a road is an edge of a graph; a plot is a footprint with a frontage.** Every system below (streaming, registry, phases) reads those three things and nothing else.

---
## 4. Road network and hierarchy

The network is **one connected graph** of 54 nodes and 83 edges (82.4 km): arterial 8.8 km, arterial ring 20.6 km, collector 48.8 km, expressway 3.4 km, lane 0.7 km, local 0.1 km. It is tested for connectivity from the expressway entrance, a hierarchy (every district reached by collectors or better; no collector more than two hops from an arterial or expressway), and for every junction type the engine cannot build being mapped to a backlog item.

| Class | Lanes x width | Median | Sidewalks | km/h | References | Engine needs | Supported today |
|---|---|---|---|---|---|---|---|
| **expressway** | 3 x 3.65 m | planted, kerbed, about 4 m, double-arm lights (R-007, R-008); or concrete jersey barrier (R-012) | no | 100 | R-007, R-008, R-012 | RB-16, RB-17, RB-04, RB-09 | partial: lanes, markings, jersey median and guardrail yes; planted median, painted kerbs, median lights no |
| **arterial_ring** | 2 x 3.5 m | planted, kerbed, about 3 m, double-arm lights (R-011) | yes | 70 | R-011 | RB-16, RB-17, RB-04, RB-09, RB-12 | partial |
| **arterial** | 2 x 3.5 m | none or planted, by section | yes | 60 | R-011 | RB-16, RB-17, RB-04, RB-12 | partial |
| **collector** | 1 x 3.5 m | none | yes | 40 | R-006 | RB-16, RB-17, RB-04 | partial: a junction stub off one main road, 150 m apart, no network |
| **local** | 1 x 3.5 m | none (one-way loops use two lanes, R-006) | yes | 30 | R-006 | RB-16, RB-17, RB-04, RB-12 | partial |
| **lane** | 1 x 3.0 m | none | no | 15 | none | RB-16, RB-17, M-12 (no reference yet) | partial; width and surface unknown until M-12 |

**Junctions.**

| Node type | Count | Engine today | Backlog |
|---|---|---|---|
| terminal | 1 | supported (road end) | - |
| gateway | 1 | prop only: a gantry set piece; its text needs RB-07 | RB-07 |
| T | 31 | supported with limits: undivided road, at least 150 m between junctions, straight stub | - |
| cross4 | 8 | not supported | RB-02, RB-16 |
| roundabout | 4 | not supported | RB-01, RB-16 |
| interchange | 1 | not supported | RB-03, RB-16 |
| gate_cross | 8 | not supported (a four-way crossing at an old-city gate) | RB-02, RB-16 |

**21 of 54 junctions cannot be built by the current engine** (four-way crossings, roundabouts, interchange, gate crossings). The engine represents one main spline per route with straight T-junction stubs at least 150 m apart; a city is a network. The foundation is **RB-16 (road network graph)** with **RB-17 (road class catalogue)**; the junction forms are RB-01, RB-02, RB-03. Until they exist the district work that does not touch the network (buildings, kit, props, vehicles) continues in test scenes.

![Where the current engine cannot follow the plan](kano/city/img/engine_gap.png)

---
## 5. Building blocks and neighbourhood layouts

The hero districts with references have a plan-level layout: envelopes read from the concept's composition, not surveyed dimensions and not 3D concept art. They fix the *kind* of arrangement (block size, plot pattern, setbacks, access) so the assets can be produced against it.

### D06: Modern residential estate (R-006)

![D06 layout](kano/city/img/layout_D06.png)

Perimeter collector, one-way loop local streets, blocks of 140 x 80 m with six plots of about 22 x 32 m on each long side. Envelope 1300 x 1200 m. Roughly 1,860 plots at the planning density.

### D08: Institutional corridor (R-010, R-011)

![D08 layout](kano/city/img/layout_D08.png)

Divided arterial through the centre; hospital campus on one side, police station and future school, government building and hotel on the other. Envelope 1400 x 1200 m. Roughly 501 plots at the planning density.

### D09: Transport hub (R-009)

![D09 layout](kano/city/img/layout_D09.png)

Terminal building, canopied bays, apron with a landscaped island, gatehouse and barrier, parking, perimeter wall. Envelope 1000 x 700 m. Roughly 138 plots at the planning density.

### D10: Industrial and logistics zone (R-013 to R-015)

![D10 layout](kano/city/img/layout_D10.png)

Warehouse with six docks, yard, truck-parking field, gatehouse, a truck fuel and service centre, perimeter wall; expressway frontage. Envelope 1000 x 1000 m. Roughly 148 plots at the planning density.

### D11: A2 expressway gateway (R-007, R-008, R-012)

![D11 layout](kano/city/img/layout_D11.png)

Gantry across the divided carriageway, planted median with double-arm lights, a curve with chevron signs and guardrails, and the approach to the interchange. Envelope 1100 x 1000 m. Roughly 54 plots at the planning density.

**Districts with no layout yet.** D05 (commercial belt), D01, D02, D03, D04 and D07 have only a boundary and a density. Their block and plot patterns need references (modern commercial frontage; Hausa compounds, lanes and markets; the landmarks). **Nothing is drawn for them**, because drawing a street pattern for an old city from imagination is exactly the invention the owner ruled out. When the references arrive, the pattern is read from them and the district joins the plan.

---
## 6. Landmarks

| Landmark | District | K0 position (x, z) m | Status | Basis | Reference needed |
|---|---|---|---|---|---|
| Gidan Rumfa (Emir's Palace) | D02 | (-66, -874) | VERIFIED (within about 300 m) | VERIFIED within about 300 m (three web listings agree; not surveyed) | M-11 |
| Dala Hill | D04 | (-1061, 1017) | VERIFIED (single source) | VERIFIED from ONE source; height disputed (534 m or 453.67 m above sea level) | R-001, M-11 |
| Great Mosque of Kano | D02 | (-70, -640) | UNVERIFIED placeholder | UNVERIFIED PLACEHOLDER: sources say it stands north of the palace | M-11 |
| Kurmi Market | D03 | (-680, -1180) | UNVERIFIED placeholder | UNVERIFIED PLACEHOLDER: in the old city near the palace | M-04, M-11 |
| Kofar Mata Dye Pits | D03 | (-860, -1050) | UNVERIFIED placeholder | UNVERIFIED PLACEHOLDER: old city, near the wall and the palace | M-11 |

**Rule:** a landmark is built only from rights-cleared real references or measured drawings, and its position is fixed from sources before it is built. Two coordinates are verified (one from a single source). Three positions are placeholders and are shown as such. The Great Mosque question is in section 14: the same twin-minaret, teal-dome mosque recurs in five concepts, and the owner has not said whether it stands for the Central Mosque.

---
## 7. Architectural styles: traditional, modern, commercial

| Style | Evidence in hand | What can be built now | What is blocked |
|---|---|---|---|
| **Modern residential** | R-006 (villa street) | villas, compound walls, gates, kerbs, paver sidewalks, streetlights, trees: 17 assets | the species of street trees and Kano-specific plot patterns (M-07) |
| **Modern commercial and civic** | R-009 to R-015 (terminal, hospital, police, fuel, warehouses) | the five buildings and their kit and props | shopping streets, plazas, mid-rise commercial frontage, schools, government buildings, hotels (M-02, M-04, M-10) |
| **Highway and gateway** | R-007, R-008, R-012 | gantry, guardrails, median lights, chevrons, expressway cross-section | text-bearing signs (RB-07), sign facts (section 14), hills (RB-14) |
| **Traditional Hausa** | none usable (R-001 too distant) | nothing | compounds, zaure, parapets, ornament, wall gates, old-city lanes, markets, mosques (M-01, M-03, M-04, M-12) |

The research record (`kano/RESEARCH.md`) gives words (tubali mud brick, zaure, walled compounds) but no dimensions, proportions or ornament; none was filled in from imagination. The traditional style is therefore **a reference request, not a modelling task**.

---
## 8. 3D production strategy

### The standard: ten compulsory concepts

| Id | Concept | Role |
|---|---|---|
| R-006 | Gated modern residential street, one-way | ChatGPT-generated concept; the owner's compulsory design standard |
| R-007 | Curved dual-carriageway expressway through hills | ChatGPT-generated concept; the owner's compulsory design standard |
| R-008 | A2 expressway with overhead gantry (planter median) | ChatGPT-generated concept; the owner's compulsory design standard |
| R-009 | Kano Central Bus Terminal | ChatGPT-generated concept; the owner's compulsory design standard |
| R-010 | Specialist hospital campus | ChatGPT-generated concept; the owner's compulsory design standard |
| R-011 | Metropolitan police station beside a divided arterial | ChatGPT-generated concept; the owner's compulsory design standard |
| R-012 | A2 expressway with overhead gantry (concrete median barrier) | ChatGPT-generated concept; the owner's compulsory design standard |
| R-013 | Truck fuel and service centre | ChatGPT-generated concept; the owner's compulsory design standard |
| R-014 | Warehouse and logistics yard (aerial) | ChatGPT-generated concept; the owner's compulsory design standard |
| R-015 | Warehouse and logistics yard (ground level) | ChatGPT-generated concept; the owner's compulsory design standard |
| R-016 | Dry season street (hot and dusty): cracked and potholed asphalt, roadside shops | ChatGPT-generated concept; the owner's compulsory design standard |
| R-017 | Harmattan season (dusty and hazy) | ChatGPT-generated concept; the owner's compulsory design standard |
| R-018 | Rainy season (heavy rain): wet asphalt, reflections, splash | ChatGPT-generated concept; the owner's compulsory design standard |
| R-019 | Flooded road (heavy-rain aftermath) | ChatGPT-generated concept; the owner's compulsory design standard |
| R-020 | Muddy unpaved road after rain (village) | ChatGPT-generated concept; the owner's compulsory design standard |
| R-021 | Muddy unpaved road after heavy rain (market village) | ChatGPT-generated concept; the owner's compulsory design standard |
| R-022 | Modern boulevard in the city centre | ChatGPT-generated concept; the owner's compulsory design standard |
| R-023 | Modern divided highway with a flyover | ChatGPT-generated concept; the owner's compulsory design standard |

Each concept is a design specification for **genuine 3D geometry**. The matrix (section 9) breaks every concept into assets, says which are reusable components and which are Kano-specific, and lists the Blender and Unity tasks. The owner approves each breakdown before work starts.

### Blender production (headless `bpy` behind the `tools/blender` server)

Per asset, in order, one status step at a time:

1. write the concept down as a table of proportions, materials and details; the owner corrects it
2. blockout from reusable kit modules; render at the reference camera and compare
3. detail modelling: real walls, roofs, openings, structure and ornament (no image planes)
4. UV unwrap and PBR materials (shared `M_<surface>` names; one atlas per family)
5. LOD chain (prefab LOD0 and LOD1; the merged tiers come from the baker) and collision (box or convex, never the render mesh)
6. validate against the game's budgets (`validate_asset`)
7. export, Unity import test (orientation, scale, materials, collision, LOD thresholds), prefab build, catalogue entry
8. in-game screenshot from the reference camera, side by side with the concept; deviations recorded

### The visual-fidelity gate

Every concept has a **reference camera** (the framing that reproduces the concept's view) and a **comparison checklist** (what to compare: for example house heights, wall pier spacing, kerb pattern, streetlight height, canopy bays). A district does not reach `in_game_visual_review_passed` until the in-game screenshot at that camera is compared side by side with the concept and the owner accepts it. Differences are written down as deviations with a reason; none is silent. Those cameras and checklists are in the matrix. The Blender camera branch (`work/blender-camera`, CI green, awaiting merge) is what renders the Blender side of each comparison, and the Unity side uses the in-game camera.

**What cannot be promised.** An in-game scene will not be pixel-identical to a generated concept, whose lighting, depth of field and composition are illustrations. The gate asks whether the *buildable content* matches (massing, proportions, materials, details, spacing), and the owner decides whether the result is acceptable.

---
## 9. Reference-to-asset production matrix

132 assets for the ten concepts: GLOBAL 92, REGIONAL 25, CITY_SPECIFIC 9, LANDMARK 6; 113 are backed by a concept and **19 are blocked** (no concept or verified reference; nothing invented). Full tables, per concept: visual requirements, assets, reusable components, city-specific components, engine gaps, Blender tasks, Unity tasks, reference camera, comparison checklist.

| Ref | Concept | Districts | Assets | GLOBAL | REGIONAL | CITY | LANDMARK | Shared with another concept | Engine gaps | Flags for the owner |
|---|---|---|---|---|---|---|---|---|---|---|
| **R-006** | Gated modern residential street, one-way | D06 | 17 | 15 | 2 | 0 | 0 | 9 | RB-04, RB-12 | recurring twin-minaret mosque |
| **R-007** | Curved dual-carriageway expressway | D11 | 10 | 5 | 5 | 0 | 0 | 7 | RB-04, RB-09, RB-14, RB-17 | hilly terrain unlike Kano's plain |
| **R-008** | A2 gantry, planter median | D11 | 9 | 7 | 2 | 0 | 0 | 9 | RB-04, RB-07, RB-09, RB-14, RB-17 | recurring twin-minaret mosque, sign text and distances do not match sources |
| **R-009** | Bus terminal | D09 | 18 | 16 | 1 | 1 | 0 | 13 | RB-04, RB-07, RB-12, RB-13 | recurring twin-minaret mosque, terminal name not verified |
| **R-010** | Hospital campus | D08 | 19 | 17 | 1 | 1 | 0 | 11 | RB-07, RB-12, RB-13 | recurring twin-minaret mosque, hospital name resembles a real institution |
| **R-011** | Police station beside a divided arterial | D08 | 14 | 13 | 0 | 1 | 0 | 10 | RB-04, RB-07, RB-09, RB-17 | real police crest and livery |
| **R-012** | A2 gantry, concrete median barrier | D11 | 9 | 7 | 2 | 0 | 0 | 8 | RB-04, RB-07, RB-09, RB-14 | recurring twin-minaret mosque, sign text and distances do not match sources |
| **R-013** | Truck fuel and service centre | D10 | 17 | 17 | 0 | 0 | 0 | 13 | RB-04, RB-07, RB-12, RB-13 | generic fuel brand |
| **R-014** | Warehouse and logistics yard, aerial | D10 | 18 | 18 | 0 | 0 | 0 | 16 | RB-04, RB-07, RB-12, RB-13 | - |
| **R-015** | Warehouse yard, ground level | D10 | 14 | 14 | 0 | 0 | 0 | 13 | RB-04, RB-05, RB-12, RB-13 | - |
| **R-016** | Dry season street: cracked and potholed asphalt, shops | D03, D05, D07 | 19 | 12 | 7 | 0 | 0 | 17 | RB-04, RB-05, RB-06, RB-07, RB-18, RB-22, RB-23, RB-24 | - |
| **R-017** | Harmattan: dusty haze over the same street | D03, D05, D07 | 13 | 10 | 3 | 0 | 0 | 12 | RB-04, RB-06, RB-07, RB-18, RB-20, RB-22, RB-23, RB-24 | - |
| **R-018** | Rainy season: heavy rain on wet asphalt | D03, D05, D07 | 11 | 7 | 4 | 0 | 0 | 11 | RB-04, RB-07, RB-21, RB-23, RB-24 | - |
| **R-019** | Flooded road after heavy rain | D03, D05, D07 | 8 | 5 | 3 | 0 | 0 | 7 | RB-04, RB-05, RB-07, RB-21, RB-22, RB-23, RB-24 | - |
| **R-020** | Muddy unpaved village road after rain |  | 14 | 4 | 10 | 0 | 0 | 11 | RB-14, RB-18, RB-19, RB-21, RB-23, RB-24 | - |
| **R-021** | Muddy unpaved road after heavy rain (market village) |  | 13 | 4 | 9 | 0 | 0 | 12 | RB-18, RB-19, RB-21, RB-23, RB-24 | - |
| **R-022** | Modern boulevard in the city centre | D05 | 15 | 14 | 1 | 0 | 0 | 12 | RB-04, RB-07, RB-09, RB-12, RB-17 | - |
| **R-023** | Modern divided highway with a flyover | D11 | 11 | 9 | 2 | 0 | 0 | 9 | RB-03, RB-04, RB-07, RB-09, RB-14, RB-17 | - |

See `kano/city/REFERENCE_ASSET_MATRIX.md` for the per-asset tables, and `kano/city/reference_asset_matrix.json` for the data. **Reusable** assets recur across concepts (the painted kerb, the single-arm streetlight, the arrow marking, the street trees): they are modelled once.

**Vehicles.** The game's prop catalogue allows 2,500 triangles per prop. Close-range coaches, trucks, an ambulance and police pickups cannot meet that at the fidelity of the concepts. The registry flags them; the choice (raise the vehicle ceiling in the catalogue, or use a separate vehicle class with its own LOD ladder) is the owner's and changes no gameplay or vehicle physics.

---
## 10. Modular asset architecture and registry

**Four classes**, with naming prefixes the registry validator enforces:

| Class | Prefix | Meaning | Examples |
|---|---|---|---|
| GLOBAL | `aro_` | reusable across most cities | kerb, sidewalk, streetlight, guardrail, signs, markings, villa types, warehouse, bus canopy |
| REGIONAL | `rg_ngn_` | for a region or architectural family (here Northern Nigeria) | street trees, Hausa compound kit (blocked), wall gates (blocked) |
| CITY_SPECIFIC | `kano_` | designed for one city | bus terminal, plaza, mid-rise, shop rows, market stalls |
| LANDMARK | `lmk_kano_` | a distinctive real location, never copied to another city | Gidan Rumfa, Great Mosque, Kurmi hall, dye pits, wall gate, Dala Hill (all blocked) |

The rule for every new asset is **look in the registry first** (GLOBAL, then REGIONAL, then city-specific) and create only what is missing. A second city reuses the GLOBAL and REGIONAL libraries, which is where the effort pays back.

**Kit before buildings.** Hero buildings are composed from kit modules (wall panels, piers, gates, canopies, balconies, roof forms), so that a design change is made once and every use follows. Composed assets list their parts in the registry.

**The registry** (`kano/city/asset_registry.json`; the canonical copy goes to the private repository `ARO-CONTENT/registry/asset_registry.json`) links every asset along the chain **concept -> Blender source -> export -> Unity prefab -> city placement**, with its status, validation results, planned budget, engine dependencies and flags. The validator rejects duplicate ids, a class that disagrees with its prefix, an unknown status, and a `production_approved` entry without the full chain (source, export, prefab, validation, approver). Large binaries (`.blend`, `.fbx`, `.glb`, textures) live in the private repository; this public repository holds only metadata.

---
## 11. Performance and world-streaming strategy

The camera cannot see 100 km², so the question is what a player sees from any one place. The model below counts, for each district density, how many plots fall inside each distance band, what each band is made of, and what that costs in triangles and draw calls, against the game's own `PerfBudget`.

| Band | Distance from the camera (m) | What is drawn | Triangle cap per plot | Individual objects? |
|---|---|---|---|---|
| 0 | 0 to 60 | prefab LOD0 | 5000 | yes |
| 1 | 60 to 150 | prefab LOD1 | 1500 | yes |
| 2 | 150 to 350 | baked block mesh (atlas) | 120 | no (merged per chunk) |
| 3 | 350 to 800 | baked block mesh, coarse | 12 | no (merged per chunk) |
| 4 | 800 to 1500 | impostor or skyline mesh | 3 | no (merged per chunk) |

Assumptions (all explicit; the tests read them): the camera sees **30%** of the circle around the player; a merged chunk costs **2** draw calls; a chunk is **4 ha** (200 m); **no occlusion credit** is taken, so every number is an upper bound. Plot densities are planning assumptions, not measurements of Kano. Tier radius scale follows `QualityTiers` (Low 0.6, Medium and High 1.0).

**Low tier**, checked against the Mobile `PerfBudget` (250,000 triangles, 150 draw calls; targets: at most 45% of the triangle budget and 60% of the draw budget for the city, the rest being road, terrain, traffic and UI):

| District or filler | Plots/ha | Triangles in view | Draw calls in view (ladder) | Draw calls in view (current engine) |
|---|---|---|---|---|
| D11 | 0.5 | 1,177 | 38 | 15 |
| D10 | 1.5 | 3,532 | 39 | 45 |
| D09 | 2 | 4,709 | 39 | 60 |
| D08 | 3 | 7,063 | 40 | 90 |
| D06 | 12 | 28,252 | 47 | 360 |
| D05 | 25 | 58,859 | 57 | 750 |
| D01 | 40 | 94,174 | 68 | 1,200 |
| D04 | 35 | 82,402 | 64 | 1,050 |
| D07 | 30 | 70,630 | 61 | 900 |
| F-OLD | 40 | 94,174 | 68 | 1,200 |
| F-MID | 12 | 28,252 | 47 | 360 |
| F-OUT | 2 | 4,709 | 39 | 60 |

**Medium tier**, checked against the Desktop `PerfBudget` (1,000,000 triangles, 400 draw calls; targets: at most 45% of the triangle budget and 60% of the draw budget for the city, the rest being road, terrain, traffic and UI):

| District or filler | Plots/ha | Triangles in view | Draw calls in view (ladder) | Draw calls in view (current engine) |
|---|---|---|---|---|
| D11 | 0.5 | 3,270 | 106 | 15 |
| D10 | 1.5 | 9,810 | 108 | 45 |
| D09 | 2 | 13,080 | 109 | 60 |
| D08 | 3 | 19,620 | 111 | 90 |
| D06 | 12 | 78,478 | 130 | 360 |
| D05 | 25 | 163,496 | 158 | 750 |
| D01 | 40 | 261,594 | 190 | 1,200 |
| D04 | 35 | 228,895 | 179 | 1,050 |
| D07 | 30 | 196,196 | 169 | 900 |
| F-OLD | 40 | 261,594 | 190 | 1,200 |
| F-MID | 12 | 78,478 | 130 | 360 |
| F-OUT | 2 | 13,080 | 109 | 60 |

**Result.** With the ladder, the densest district peaks at 94,174 triangles and 68 draw calls on Low and 261,594 triangles and 190 draw calls on Medium; both are inside the targets. With the **current engine** (one draw per plot, ring 2 only) the densest districts need 1,200 draw calls against a budget of 400 (Desktop) and 150 (Mobile): the city is **not renderable in the current engine at city density**, and the 5 x 5 chunk square it instantiates (100 ha) is smaller than any hero district. This is not a modelling problem; it is **RB-15**, and it is why the phased plan puts the engine work in front of the districts that need it. The model is a feasibility check, not a measurement: nothing here has been rendered, and the real numbers come from `PerfProbe` once a district exists.

**A caveat the tables show.** The ladder has a fixed floor (every merged chunk costs draw calls). On Medium and High the ladder therefore costs *more* draw calls than the current engine in the sparse districts (D11, D10, D09, D08 and the fringe F-OUT) and pays off from D06 density upward; on Low it is cheaper everywhere except D11. The ladder is for city density. The sparse hero districts can be built on the existing streaming first (P1a), which is why the engine work does not block them.

![Streaming and budget model](kano/city/img/streaming.png)

**What streams.** (1) Chunks of 200 m remain the unit; the lattice is already aligned with frame K0. (2) Hero districts (zone Z-A) keep individual prefabs out to band 1 (150 m), baked block meshes beyond. (3) Filler (Z-B) is generated from the same kit by rule and exists only as prefab LOD1 plus baked blocks. (4) Beyond the ring road (Z-C) only terrain tint, a baked skyline mesh and impostors, hidden by the same haze the game already uses (FarClip 900 / 1500 / 2200 m by tier). (5) Textures: the Kano texture plan is 36.0 MiB, inside the 300 MB desktop and 150 MB mobile budgets, and the shared-kit approach (one atlas per family, `M_<surface>` names) keeps it from growing per district (see `docs/TEXTURE_BUDGET.md`; the world-map texture defect found there is separate).

**Order of expansion.** A district is added by (a) authoring its hero buildings from the registry, (b) generating its filler from the kit, (c) baking its blocks offline, (d) adding its chunk data, and (e) measuring with `PerfProbe` on each tier before the next district. Nothing is loaded that the camera cannot reach, so the world can grow without the frame budget growing.

---
## 12. Phased plan

| Phase | Name | Districts | Entry | Exit | Engine items | Note |
|---|---|---|---|---|---|---|
| P0 | Foundations (no modelling) | - | this master plan reviewed and approved by the owner | asset registry live in the private repository; reference-comparison harness defined; engine roadmap items RB-15, RB-16 and RB-17 designed and approved or declined; reference gaps and coordinate decisions closed | RB-15, RB-16, RB-17 | Everything that does not need the engine or a missing reference can start the moment P0 exits. |
| P1a | Gateway and civic assets (engine-independent) | D11, D10, D09, D08, D06 | P0 exit; the asset breakdown of each concept approved | every concept-backed asset of the matrix reaches unity_import_verified in a test scene, with the in-game screenshot compared against its concept | none | Buildings, kit modules, props and vehicles import and render in the existing engine; placement uses a test scene, not the city network. |
| P1b | Gateway and civic districts in the game | D11, D10, D09, D08, D06 | P1a exit and the shared-engine items approved | the five districts are drivable, compared against their concepts in game screenshots, and measured against the tier budgets | RB-01, RB-02, RB-03, RB-04, RB-07, RB-09, RB-12, RB-13, RB-15, RB-16, RB-17 | Blocked until the engine delivers a road network, kerbs and sidewalks, pavement symbols, off-road areas and city-density streaming. |
| P2 | Commercial belt and civic completion | D05, D08 | concepts for modern commercial frontage, schools, government buildings and hotels (M-02, M-04, M-10) | D05 and the rest of D08 as P1b | none | Needs new concepts first. |
| P3 | Traditional districts | D01, D03, D07 | authentic references for Hausa compounds, old-city lanes and markets (M-01, M-04, M-12) | as P1b; hero sector first | RB-17 | BLOCKED: nothing is invented without references. |
| P4 | Landmarks | D02, D04 | rights-cleared real references or measured drawings for each landmark (M-11) | each landmark reviewed against its source | RB-14 | A landmark is never invented; positions of unverified landmarks are fixed from sources first. |
| P5 | Expansion | - | the pipeline and library proven on Kano | further Kano sectors, the Kaduna-Kano corridor (compressed, anchored at both cities) and the next cities, reusing the registry before creating assets | RB-15, RB-16 | Each new city checks the registry for GLOBAL and REGIONAL assets first. |

**Progressive, district by district.** P1a can start the moment P0 exits and needs nothing from the engine: it produces the assets and proves them in a test scene. P1b needs the engine items. P2 and P3 wait on new concepts and references. The order guarantees that the first drivable district is built from approved concepts on a road system that can actually carry it, and that the pipeline is proven before it is spent on the old city.

---
## 13. Shared-engine improvements required

The shared road engine is **unchanged** and Kano adds no permanent workaround. The items the plan depends on (full list with current behaviour in `ROAD_ENGINE_BACKLOG.md`):

| Item | What | Needed by |
|---|---|---|
| RB-16 | Road network graph: the foundation for a city (one spline plus stubs today) | P0, P1b, P5 |
| RB-17 | Road class and cross-section catalogue (expressway to lane) as data | P0, P1b, P3 |
| RB-15 | City-density streaming: baked per-chunk meshes, hierarchical LOD, impostors | P0, P1b, P5 |
| RB-12 | Pavement symbols, crossings, stop lines, bay lines | P1b |
| RB-13 | Off-road paved areas: parking, forecourts, aprons, truck yards | P1b |
| RB-14 | Terrain authoring: hills and rock cuts | P4 (and D11, only if the owner keeps the hills of R-007; otherwise the gateway is built on flat ground) |
| RB-01 | Roundabouts | P1b |
| RB-02 | Four-way intersections | P1b |
| RB-03 | Flyovers and interchanges | P1b |
| RB-04 | Sidewalks and kerbs | P1b |
| RB-07 | Text-bearing road signs | P1b |
| RB-09 | Median and near-edge furniture | P1b |

Each is a proposal for the owner to approve, defer or decline. None is started.

---
## 14. Flags raised by the concepts (the owner decides; nothing is silently changed)

1. **Sign text and distances (R-008, R-012).** The gantry reads Zaria 138 km and Kaduna 262 km, and "A2 Express Route". Web sources give Kano to Zaria about 155 to 165 km and Kano to Kaduna about 230 km (sources range 232 to 272 km). A2 is the correct federal designation for the Abuja-Kaduna-Zaria-Kano highway; "Express Route" is not an established name. Sign text must come from verified route data, so the gantry is built with a text slot and RB-07.
2. **The recurring twin-minaret, teal-dome mosque** appears in the distance of R-006, R-008, R-009, R-010 and R-012. It reads as a designated landmark. Is it the Central Mosque (then it is a LANDMARK built from real references), or a generic skyline mosque (then it is one regional asset)? Sources disagree whether the real mosque has two or four minarets.
3. **Real organisations.** R-010's hospital name resembles a real institution (Aminu Kano Teaching Hospital); R-011's crest imitates the Nigeria Police Force emblem and the vehicles wear police livery. Fictional names and a generic crest are the default unless the owner decides otherwise.
4. **Terrain.** R-007's green rocky hills do not match Kano's flat plain; they suit the hillier country on the Kaduna side. Use for the gateway only if the owner accepts it, with RB-14.
5. **Names.** "Kano Central Bus Terminal" is not a verified real terminal. The fuel-centre brand colours and name are generic.
6. **Vehicle fidelity versus the prop budget** (section 9).
7. **Density.** Plot densities are planning assumptions. They are replaced by measurements as references arrive; the streaming result depends on them (section 11).

---
## 15. Decisions and blockers

**Owner decisions**

1. Approve this direction (the scope correction, districts, phases) so P0 can close.
2. Approve each concept's asset breakdown (matrix, section 9) before work on it starts.
3. Rule on the flags in section 14 (sign facts, the mosque, real-organisation names, terrain, vehicles).
4. Coordinate anchor and transform (`COORDINATES.md`); the world-map pin is a production-data change and is not touched without explicit authorisation.
5. Allow OSM, Wikidata and GeoNames access (or provide an extract) to replace indicative geography with real geography.
6. Approve, defer or decline RB-15, RB-16 and RB-17 (P0 exit) and the other backlog items.
7. Asset storage: artifact boundary, Git LFS quota and ownership for `ARO-CONTENT`. The Kano package stays local until the relevant CI and asset-storage decisions are resolved.
8. Supply the missing concepts and references (section 16).

**Blockers**

* The traditional districts (D01, D02, D03, D04, D07) and every LANDMARK: no authentic references.
* The commercial belt (D05), schools, government buildings, hotels: no concepts.
* Everything that needs the road network or city density: the engine items.

---
## 16. Concept requests (for ChatGPT, to unblock the plan)

The plan needs these to move beyond the five districts. Each should be a labelled, honest concept, and the traditional and landmark ones should be **tied to real references** (rights-cleared photographs, measured drawings or heritage surveys), not idealised imagination, because the owner has ruled out invented architecture.

| # | Needed (missing area) | Suggested views | Why it matters |
|---|---|---|---|
| 1 | Traditional Hausa compounds, zaure, parapets and ornament (M-01) | street elevation; compound plan; zaure interior; wall-gate close-up | D01 and D07; unblocks four REGIONAL assets |
| 2 | Old-city lanes and traditional residential streets (M-12) | eye-level lane; aerial of a block | lane width, wall height, surface and drainage are unknown |
| 3 | Kurmi Market and traditional markets (M-04) | aerial; stall rows; eye-level lane | D03; market stalls and covered halls |
| 4 | Neighbourhood mosque (M-03) | exterior; courtyard; minaret detail | D06, D07; a REGIONAL asset oriented to the qibla |
| 5 | Great Mosque and Gidan Rumfa (M-11) | gate; court; minaret; aerial | D02; only from real references |
| 6 | Dala Hill and its settlement (M-11) | aerial; base of the hill; steps | D04; R-001's location is unconfirmed |
| 7 | City wall and a gate (M-11) | gate exterior and interior; wall section | gate_cross junctions; position needs sources |
| 8 | Modern commercial street and plaza (M-02, M-04) | eye-level; aerial; shop-front close-up | D05 |
| 9 | Mid-rise commercial and office buildings (M-02) | street elevation; aerial | D05, D08 |
| 10 | Schools, government buildings, hotels (M-10) | exterior of each; entrance close-up | D08 completion |
| 11 | Roundabout and four-way intersection (M-13) | aerial; eye-level through the junction | needed with RB-01, RB-02, RB-16 |
| 12 | Interchange or flyover (M-13) | aerial; ramp detail | needed with RB-03 |
| 13 | Real road surfaces, patches, cracks and drains (M-05) | close-ups of Kano roads | RB-05, RB-06 |
| 14 | Electricity poles, transformers and cables as in Kano (M-06) | street view; pole close-up | street furniture |
| 15 | Street trees and vegetation of the region (M-07) | species close-ups | replaces the generic broad-leaf placeholder |
| 16 | Local vehicles: motor tricycles, motorcycles, minibuses (M-08) | side and three-quarter | traffic and roadside life |

---
## 17. Where everything is

| What | Where |
|---|---|
| This plan | `docs/KANO_CITY_MASTER_PLAN.md` (generated by `tools/kano/make_city_doc.py`) |
| Plan data (districts, network, landmarks, phases, streaming) | `docs/kano/city/city_plan.json` (`tools/kano/city_data.py`, `make_city_plan.py`) |
| Reference-to-asset matrix | `docs/kano/city/REFERENCE_ASSET_MATRIX.md`, `reference_asset_matrix.json` |
| Asset registry | `docs/kano/city/asset_registry.json`; canonical copy in the private repository |
| Statistics (computed by the tests) | `docs/kano/city/city_stats.json` |
| Diagrams | `docs/kano/city/img/` (`tools/kano/city_diagrams.py`; the PNG files are Git LFS objects of this repository) |
| Reference register | `docs/kano/REFERENCE_REGISTER.md` |
| Engine backlog | `docs/ROAD_ENGINE_BACKLOG.md` |
| Transportation expansion plan (2026-11-08) | `docs/KANO_TRANSPORT_EXPANSION_PLAN.md` |
| Night and street-lighting plan | `docs/NIGHT_LIGHTING_PLAN.md` |
| Coordinates | `docs/kano/COORDINATES.md` |
| Research | `docs/kano/RESEARCH.md` |
| Texture budget | `docs/TEXTURE_BUDGET.md` |
| Tests | `tests/NetCore.Tests/KanoCityPlanTests.cs` (8 tests), `KanoSliceTests.cs` (10 tests) |
