# Family D (roundabout, island and flyover components): reuse-first plan and what was built

Written 2026-11-09. The owner's decision: **Family D follows E5**, and reusable assets come first. E5 is built (`ROAD_ENGINE_E5.md`), so D is unblocked, and **the new-build rows were then modelled in the same session** (12 ids in `tools/blender/transport_kit/island.py`, see `TRANSPORT_KIT.md` section 3, D; the reuse rows were not rebuilt); this plan says what to build, what to reuse, and what the engine already draws so that nothing is made twice. The rows are those of `KANO_TRANSPORT_ASSET_BREAKDOWN.md` section D. The lesson of the transport kit (`TRANSPORT_KIT.md` section 5b) is applied: the **private library registry was read first** and every row mapped to what it already holds.

## What the engine already draws (do not model it)
E5 builds, as road geometry: the ring carriageway with its kerbs and markings, the central island (kerbed or paved or grass fill), the splitter islands, yield lines and zebra crossings, the flyover **deck with parapets, the slab underside and the piers** (the existing bridge code, `BridgeSpec.overRoad`), and the ramps' embankments. The island's **props** (lights, palms, kiosks, signs) come from the existing prop rules and the catalogue. So the Family D assets are *dressing and detail on top of this*, not the structure.

## Verdict per row

| Row (id in the breakdown) | In the registry already | Verdict |
|---|---|---|
| `aro_palm_ornamental_a/b` | `aro_palm_ornamental_a` planned (concept approved), `kano_date_palm_a` and the E-kit trees | **Reuse / build the planned entry**: one ornamental palm realises the planned id; a second variant only if the review wants it. Date palm first. |
| `aro_shrub_flowering_a/b` | `aro_shrub_flowering_pink_a` planned, `aro_shrub_ixora_red_a` **built and imported**, `aro_agave_a` planned | **Reuse** the ixora; build the planned pink shrub; no new id needed. |
| `aro_tree_shade_a/b` | `rg_ngn_tree_large_shade_a` and `_wet_a` **built and imported**, `rg_ngn_tree_street_broadleaf_a/b` **built and imported** | **Reuse. Do not build.** |
| `aro_roundabout_hedge_ring_a` | `aro_hedge_clipped_a` **built and imported** (2 m strip) | **Reuse** the strip placed along the ring by the island rules; a curved module only if the in-game picture shows the straight strip's facets. |
| `aro_roundabout_fountain_a` | none | **Build** (GLOBAL, 8 x 8 x 1.6 m, no animation, glow x4). |
| `lmk_kano_roundabout_monument_a` | none | **Build** (LANDMARK): a **fictional designed civic monument, flagged as such** (the owner's default of 2026-11-09), stepped plinth, arches in the regional geometric language, uplights. No real Kano monument is claimed. |
| `rg_ngn_pattern_panel_a/b/c` | `rg_ngn_screen_geometric_arch_a` planned (an arched facade panel, from R-009/R-010) | **Build** three tileable relief panels as a **flagged interpretation of the documentary sources R-024 to R-029** (Hausa-inspired geometric language); no licensed photograph or measured drawing exists (M-01 partly open), so every proportion is a design choice. |
| `rg_ngn_island_wall_a` | `aro_wall_perimeter_pier_rail_a` and plain wall planned (not the same), E4's `kasa_plaster` material | **Build** a 3 m red-earth rendered wall run with coping using the existing `kasa_plaster` (R-024, R-027). |
| `aro_flyover_deck_segment_a` | `aro_flyover_deck_pier_a` planned (R-023: concrete deck on piers with yellow-and-black bollard stripes) | **Build as the parapet and bracket kit** for that entry (the deck mesh is the engine's). |
| `aro_flyover_pier_a/b` | `aro_flyover_deck_pier_a` | **Build** round and square-capped pier detail (cap, bearing) to be placed on the engine's piers, **or** skip if the engine's piers are accepted in the in-game review. |
| `rg_ngn_pier_panel_a` | none | **Build** lattice cladding for the interchange (needs the same flagged interpretation as the pattern panels). |
| `aro_deck_edge_light_a` | none | **Build** (GLOBAL luminaire, lamp x1; needs the night system's lamp heads, already declared the same way in the transport kit). |
| `aro_noise_wall_set` | none | **Build** transparent and concrete sound-wall modules for the ramps. |

Net: **of the 13 rows, 4 are reuse** (palms, shrubs, shade trees, hedge ring: the registry already holds the built and imported or planned asset) and **9 are new builds** (fountain, monument, pattern panels, island wall, deck kit, piers, pier panel, deck edge light, noise wall; about 14 ids because panels and piers have variants).

## Order, budgets and checks (what was done, what is left)

**Done:** fountain, island wall, three pattern panels, flyover parapet and bracket kit, two piers, pier panel, deck edge light, noise wall and the monument, all validated (at most 4 materials, three LODs), staged with the rest of the kit, in a fourth review scene. **Not done:** placing any of them (no island rule, no route), the Unity review, the owner's look at the monument and the panels. The numbered list below is the order and the checks that still apply to placement.
1. Fountain, island wall, deck edge light, noise wall (no reference risk), then the deck and pier kit, then the **flagged** pieces (pattern panels, pier panel, monument) with their flags in the registry and no claim of Kano accuracy.
2. Same toolchain as the transport kit (`tools/blender/transport_kit`: palette materials, at most 4 materials, three LODs, catalogue light heads and text faces, the registry staging). Budgets as in the breakdown (monument 5,400, fountain 1,600, panels 500 each).
3. Before any placement on the E5 test routes: a test that an island prop's footprint stays inside the island and off the ring's paved edge (the island radii are in `ROAD_ENGINE_E5.md`), and that nothing on a ramp or deck reduces the 5.5 m headroom.
4. The **monument and the traditional gate are the owner's visual decisions**: model them only after the owner has seen the plan's picture of the roundabout as built (the E5 roundabouts carry the game's grey placeholder props today).
