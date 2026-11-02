# Draw-call investigation (2026-11-02): what causes the overrun, and what a content-side merge buys

**Scope and limits.** Investigation of the draw-call budget overrun found in the first in-game review of the residential kit (`KANO_RESIDENTIAL_MILESTONE.md` section 11). **No frozen v002 asset was changed, no engine system was changed, no engine batching was done** (that is a separate proposal needing approval: `ENGINE_BATCHING_PROPOSAL_RB15.md`). The only code added is review-only diagnostics (`KanoReviewStats`, `DrawBreakdown`) that count what the game already renders and alter nothing, and five NEW prototype assets (`kano_kit/frontage.py`) used only in a review variant of the scene. Everything below was **measured in the running game** (run [41450050093](https://github.com/AffanNamu/Online-African-Road/actions/runs/41450050093), commit `0eb8d2c`, the game's own `[Perf]` and `[KanoReview]` lines; the numbers are in `docs/kano/KANO_REVIEW_RUN_41450050093_numbers.json`) unless a row says *estimate*.

**How it was measured.** Unity 6000.0.58f2 WebGL build of the review scene, headless Chromium with software GL (SwiftShader) on a CI machine, quality tier `medium`, hour 15.5, 1280 x 720. "Draw calls" is the game's own estimate (`PerfProbe`): one draw per material slot of every visible mesh renderer, nothing batched; it is what `PerfBudget` judges (desktop 400). "Engine draws" is Unity's own counter for the frame, which includes the shadow pass. Frame times from software GL are not reported: they say nothing about a player's GPU.

## 1. How much of it is the residential kit? Essentially all of it
| View | Draw calls (estimate) | Kit | Review ground | Everything else | Kit share | Engine draws (with shadows) | Set-pass calls |
|---|---|---|---|---|---|---|---|
| ref | **690** | 689 | 1 | 0 | 99.9% | 1,271 | 37 |
| front | 107 | 106 | 1 | 0 | 99.1% | 189 | 15 |
| side | **654** | 653 | 1 | 0 | 99.8% | 1,267 | 27 |
| street1 | **650** | 649 | 1 | 0 | 99.8% | 1,242 | 35 |
| street2 | **618** | 617 | 1 | 0 | 99.8% | 1,156 | 37 |
| top | 331 | 330 | 1 | 0 | 99.7% | 518 | 12 |

The review scene holds the kit and a ground slab and nothing else, so "everything else" is zero by construction: **in the full game the road, terrain, props, traffic and vehicles come on top of these numbers.** The overrun in the review scene is entirely the kit.

## 2. Which assets
Reference view (689 kit draws), most draws first:
| Asset | Draws | Renderers | Share |
|---|---|---|---|
| `aro_villa_2storey_hip_a` | 167 | 27 | 24.2% |
| `aro_wall_compound_panel_a` | 164 | 82 | 23.8% |
| `aro_pier_stone_a` | 72 | 36 | 10.4% |
| `rg_ngn_tree_street_broadleaf_a` | 62 | 31 | 9.0% |
| `aro_shrub_ixora_red_a` | 42 | 14 | 6.1% |
| `aro_villa_2storey_hip_b` | 40 | 4 | 5.8% |
| `aro_hedge_clipped_a` | 28 | 14 | 4.1% |
| `aro_lantern_wall_a` | 28 | 14 | 4.1% |
| `kano_street_oneway_section_a` | 28 | 4 | 4.1% |
| `aro_pole_electric_concrete_span_a` | 18 | 4 | 2.6% |
| `rg_ngn_tree_street_broadleaf_b` | 16 | 8 | 2.3% |
| `aro_streetlight_single_arm_a` | 14 | 5 | 2.0% |
| `aro_gate_ornate_steel_a` | 7 | 7 | 1.0% |
| `aro_sign_oneway_a` | 3 | 1 | 0.4% |

Three families explain 90%: **the wall frontage (panels, piers, lanterns) 264 draws, 38.3%**; **the villas 207, 30.0%**; **the vegetation (trees, shrubs, hedges) 148, 21.5%**. The street views are the same picture (street1: 37.9%, 31.3%, 20.3%). The many small repeated parts are the cost: 82 wall panels of 2 materials each are 164 draws for 4,100 triangles.

## 3. What the floors are (same frame, reference view)
* **All 27 kit materials have GPU instancing enabled** and the shaders are URP/Lit (SRP-Batcher compatible), yet the draw calls stay 689: the SRP Batcher cuts state changes (37 set-pass calls) but **not draw calls**.
* The kit uses 27 distinct materials, 21 distinct meshes and **75 distinct (mesh, material) pairs**. Those are the floors of two idealised strategies, **bounds, not predictions**: perfect merging of everything with the same material would need 27 draws; perfect GPU instancing of identical meshes would need 75.

## 4. The shadow pass roughly doubles the true cost
With the game's own test-only switch `shadows=off` on the reference view: engine draws **1,271 to 693** (the shadow pass accounts for 578 draws, 45%) and engine triangles 374,545 to 182,471. The game's estimate (690) is the same in both cases because it counts visible renderers once. **The budget gate therefore understates the real submission cost by a factor of about 1.8 whenever shadows are on.** Whether to change the shadow policy (small items casting no shadow, shorter shadow distance) is a visual decision for the owner, and a catalogue change, so it is only listed (option C of the proposal), not done.

## 5. The content-side merge of plot frontages, evaluated
**What it is.** `kano_kit/frontage.py` builds five NEW assets: four merged plot frontages (the layout variants left/gate bay 2, left/3, right/1, right/2) that combine each plot side's four wall panels, five piers and two gate-pier lanterns into one mesh, and one far run that combines the five far wall bays. The geometry is the v002 geometry: a test (`test_the_merged_frontage_has_exactly_the_faces_of_the_individual_pieces`) proves, for all four variants, that the merged faces equal the individual v002 pieces face for face, with the same materials, when placed at their placements. The v002 assets are untouched; the review scene gains a variant (`?variant=merged`, 161 placements instead of 337) that uses them.

**Measured, merged against standard (the only two views run with the variant):**
| View | Draw calls standard | merged | Change | Engine draws standard | merged | Change | Visible renderers | Visible triangles |
|---|---|---|---|---|---|---|---|---|
| ref | 690 | **480** | **-210 (-30.4%)** | 1,271 | 897 | -374 (-29.4%) | 252 to 140 | 180,789 to 179,139 |
| street1 | 650 | **450** | **-200 (-30.8%)** | 1,242 | 876 | -366 (-29.5%) | 233 to 130 | 170,644 to 168,896 |

The frontage family falls from 264 draws to 54 on the reference view (and 246 to 46 on street1). **It is not enough on its own: 480 and 450 are still above the 400 budget.** After the merge the villas are 43% of the kit and the vegetation 31%.

**Does it look the same?** Not established beyond this: the mean colour of the merged pictures is within 0.04 of 255 per channel of the standard ones (ref: 53.317, 62.446, 60.853 against 53.355, 62.477, 60.872; street1: 63.169, 71.287, 69.316 against 63.201, 71.310, 69.330), no magenta (Unity error shader) pixels at all in any of the nine pictures, and the geometry is proven identical for the near frontages. **The per-pixel difference measurement failed** (a bug in the review driver, which passed two arguments to a function that takes one; fixed in the next commit, to be measured by the next review run). The far runs are LOD2-style boxes where the standard scene uses the full far panel, and the merged frontage texture phase differs (UVs follow the merged mesh's own origin). **The pictures `kanoreview-ref.png` and `kanoreview-ref-merged.png` (and the street1 pair) are the evidence the owner can judge directly.**

**What merging costs.** Five more assets; each merged asset bakes in its plot layout (24 m frontage, gate bay position, side), so reuse across different plot widths and cities is lost; piers and lanterns can no longer be placed individually; the frontage has one LODGroup, so the nearest piece decides the level for all of it. It is a trade of flexibility for draws.

## 6. What would bring the reference view under 400 (estimates, not measurements)
After the frontage merge the reference view is 480. Further content-side steps and their bounds:
* **Villa material reduction** (a trim-sheet atlas: 10 to 11 materials down to about 4): villas cost 207 draws across 31 visible renderers (6.7 per renderer, because lower LODs have fewer materials). At most 4 materials per renderer would be at most 124 draws: a saving of **at least 83**, giving **at most about 397**. That is at the edge of the budget, not safely under it, and it needs a new version of both villas (v003: frozen v002 is never edited).
* **Vegetation**: shrubs use 3 materials (leaf, body, flowers) and hedges 2; trees 2 (bark, leaf cards). Reducing shrubs to 2 and hedges to 1 saves about 14 + 14 = 28 (estimate).
* Together the content-side steps point at roughly 370 to 400 on the reference view with the frontage merge, i.e. **just under the budget at the hardest view in the review scene, with no margin for the road, terrain and traffic of the real game**. Engine batching (instancing or static merging) is what gives margin, and that is the separate proposal.

## 7. Conclusions
1. The overrun is the kit: 99.9% of the draws in the review scene (the scene holds nothing else).
2. Three families make 90%: wall frontage 38%, villas 30%, vegetation 21%.
3. The frontage merge is measured at **-30%** (690 to 480, 650 to 450); alone it does not reach 400.
4. The engine's real submission cost is about 1.8 times the game's estimate because of shadows (measured: 1,271 against 693 engine draws).
5. The remaining content-side lever is the villas' material count (an atlas, a new v003 after the owner's review), then small vegetation savings; the estimate lands at the edge of the budget.
6. Engine batching is the robust fix and is proposed separately; nothing about it is done.

## 8. Limitations
One scene (the review scene), software GL, no frame time, no real GPU, no road, terrain, traffic or vehicles. The merged variant was run on two of six views. The estimate in section 6 is arithmetic on measured counts, not a measurement. The review's per-pixel comparison of the merged pictures is missing (driver bug).
