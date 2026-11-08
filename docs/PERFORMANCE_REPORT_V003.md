# Performance report for v003 and the environment scenes: measured in Unity (2026-11-03)

**Source:** review run [41476407715](https://github.com/AffanNamu/Online-African-Road/actions/runs/41476407715) (commit `956aaed`, marker `[kano-review]`) and its repeat [41489466501](https://github.com/AffanNamu/Online-African-Road/actions/runs/41489466501) (commit `c582261`, the review driver fixed, so the low quality tier was measured too, and the sandy stretch added to the dry village). **The repeat reproduced every number of the first run exactly** (550, 521, 521, 495, 340, 321, 323, 305 draw calls and the rest), so the measurements are deterministic; the low-tier rows below come from the repeat (`docs/kano/KANO_REVIEW_RUN_41489466501_numbers.json`); the game's own `[Perf]` and `[KanoReview]` lines, recorded in `docs/kano/KANO_REVIEW_RUN_41476407715_numbers.json` and turned into the tables below by `tools/review_numbers.py`. The v002 figures are from run 41450050093 (`docs/kano/KANO_REVIEW_RUN_41450050093_numbers.json`), measured the same way. **No figure here is an estimate unless it says so.** Nothing is approved: this report is evidence for the owner's review of the actual in-game screenshots.

**How it was measured.** Unity 6000.0.58f2 WebGL build of the review scene (`BuildKanoReviewWebGL`, no backend baked in, not deployed), headless Chromium with software GL (SwiftShader) on a CI machine, quality tier `medium`, hour 15.5, 1280 x 720. *Draw calls* is the game's own estimate (`PerfProbe`, what `PerfBudget` judges, desktop 400): one draw per material slot of every visible mesh renderer, nothing batched. *Engine draws* is Unity's own counter for the frame and includes the shadow pass. **Frame times from software GL are not reported: they say nothing about a player's GPU.**

## 1. Did v003 do what was approved? Standard placement (every part a separate instance)
| View | Draw calls v002 | **v003** | Change | Engine draws v002 | v003 | Change | Budget 400 (draw calls) |
|---|---|---|---|---|---|---|---|
| ref | 690 | **550** | -20.3% | 1,271 | 988 | -22.3% | over |
| front | 107 | **75** | -29.9% | 189 | 132 | -30.2% | within |
| side | 654 | **521** | -20.3% | 1,267 | 994 | -21.5% | over |
| street1 | 650 | **521** | -19.8% | 1,242 | 967 | -22.1% | over |
| street2 | 618 | **495** | -19.9% | 1,156 | 900 | -22.1% | over |
| top | 331 | **242** | -26.9% | 518 | 379 | -26.8% | within |

* The kit's distinct materials fell from 27 to **12** and its distinct (mesh, material) pairs from 75 to 55 (reference view).
* **At the hardest views the standard placement is still over the 400 budget (495 to 550).** The reduction from v003 alone is about 20% (30% at the front view, 27% from above); it is not enough by itself.
* Visible triangles went down slightly (ref 180,789 to 175,559): the villas lost invisible faces, the facades gained visible ones.
* Page (script) errors in all 24 views: **none**. Magenta pixels (Unity's error-shader colour) in all 24 pictures: **none**.

## 2. v003 with the merged plot frontages (the content-side optimisation measured earlier)
| View (merged frontage variant) | Draw calls v002 merged | **v003 merged** | v002 standard | Change against v002 standard | Engine draws v003 merged | Budget 400 |
|---|---|---|---|---|---|---|
| ref | 480 | **340** | 690 | -50.7% | 614 | within |
| street1 | 450 | **321** | 650 | -50.6% | 601 | within |
| side | not run | **323** | 654 | -50.6% | 617 | within |
| street2 | not run | **305** | 618 | -50.6% | 562 | within |

* **With merged frontages, all four street views are within 400** (305 to 340), and **50% below the v002 standard placement** (690, 650, 654, 618). The merged variant also looks the same as the standard one: the per-pixel difference between the two pictures is 0.12 to 0.18 of 255 per channel on average and only 0.2% to 0.3% of the pixels differ by more than 24 (sum of channels), measured in the browser (`differenceFromStandard` in the numbers file).
* The price of merging, as before: the five frontage assets bake in the plot layout (gate position, 24 m frontage), so reuse across different plot widths and cities is lost. Merging is **not applied to the production placement**; it is a measured option for the owner.

## 3. Shadows (reference view, standard placement)
| Variant | Draw calls (game estimate) | Engine draws | Engine triangles |
|---|---|---|---|
| shadows on (as played) | 550 | 988 | 360,039 |
| `shadows=off` (game's own test switch) | 550 | 553 | 177,241 |
| small and thin kit assets cast no shadow (review-only policy: lanterns, hedges, shrubs, piers, wall panels, streetlights, poles, signs, gates) | 550 | **682** | 310,645 |
The shadow pass is **435 of the 988 engine draws (44%)**; the small-caster policy alone removes **306 engine draws (31%)** and 49,000 triangles while the villas and trees keep their shadows. The game's draw-call estimate does not see any of this (it counts renderers once). With the merged frontages the engine count at the reference view is 614. **Whether to change the shadow policy is a visual decision for the owner** (the pictures `kanoreview-ref.png` and `kanoreview-ref-shadowsmall.png` show it); it is a catalogue and tier change and was not made.

## 4. The complete game (what else will be drawn)
The review scene holds the kit and a ground slab only. The real game scene was measured in the same CI run family (the browser smoke job, run 41453779754, the game at the route start with its vehicle): **42 to 94 estimated draw calls and 138 to 198 engine draws** at the tiers sampled (medium: 57 to 94 and 138 to 198; low: 42 to 59 and 95 to 136). That is the road, terrain, props and vehicle of the early route, with few props; the Kano city will carry more.
| | Kit alone (measured, reference view) | plus the rest of the game (measured baseline, upper end) | Budget |
|---|---|---|---|
| v003, standard placement | 550 | 550 + 94 = **644** | 400 |
| v003, merged frontages | 340 | 340 + 94 = **434** | 400 |
| v003, merged frontages, engine draws with shadows | 614 | 614 + 198 = **812** | (no budget is defined for the engine count) |
The sum is an upper bound (the two were measured in different views, and the kit would not fill the whole frame in the game), not a measurement of the combined scene. Reading it honestly: **v003 with merged frontages fits the budget for the kit alone but not with the whole of the early-route baseline added (434 against 400), and the Kano city will add objects the baseline does not have.** The remaining draws after the merge are the villas (167 of 339) and the vegetation (about 67).

## 5. What the draws are made of now (reference view)
Standard placement, 549 kit draws: wall panels 164 (29.9%), villa A 139 (25.3%), piers 72 (13.1%), trees A 31, lanterns 28, villa B 28, street 20, hedges 14, shrubs 14, pole 12, streetlights 10, trees B 8, gate 7, sign 2. (v002: villa A 167, trees A 62, shrubs 42, hedges 28, villa B 40, street 28.) Merged frontages, 339 kit draws: villa A 139 (41.0%), trees A 31, villa B 28, street 20, hedges 14, shrubs 14, pole 12, the five frontage assets 50, streetlights 10, trees B 8, gate 7, piers 4, sign 2.

## 6. The environment scenes in the running game
| Scene and view | Draw calls | Engine draws (shadows on) | Visible renderers | Visible triangles | Magenta pixels | Near-black pixels |
|---|---|---|---|---|---|---|
| `env_hausa_quarter_dry-drive` | 192 | 445 | 56 | 56,162 | 0 | 0 |
| `env_hausa_quarter_dry-aerial` | 180 | 422 | 53 | 55,316 | 0 | 0 |
| `env_hausa_quarter_wet-drive` | 196 | 452 | 58 | 61,650 | 0 | 0 |
| `env_hausa_quarter_wet-aerial` | 184 | 428 | 55 | 60,804 | 0 | 0 |
| `env_village_dry-drive` | 153 | 367 | 56 | 76,546 | 0 | 0 |
| `env_village_dry-aerial` | 150 | 353 | 54 | 75,098 | 0 | 0 |
| `env_village_wet-drive` | 161 | 380 | 60 | 84,066 | 0 | 0 |
| `env_village_wet-aerial` | 158 | 368 | 58 | 81,762 | 0 | 0 |
| `env_market_street_dry-drive` | 269 | 609 | 58 | 55,624 | 0 | 0 |
| `env_market_street_dry-aerial` | 241 | 573 | 53 | 52,534 | 0 | 0.002 |
| `env_market_street_wet-drive` | 274 | 616 | 61 | 60,024 | 0 | 0 |
| `env_market_street_wet-aerial` | 245 | 580 | 55 | 56,294 | 0 | 0.002 |

* **All 26 environmental assets were built from the code, imported by the real Unity importer and built into prefabs: 44 prefabs in all (with the residential kit), 0 skipped, 0 import problems logged, and for every one of the 44 the LOD triangle counts and the material count Unity measured equal the Blender validation.** Every scene built with 0 skipped placements and its own cameras. (The one environmental asset no scene placed in the first run, the loose-sand road patch, was in the dry village scene of the repeat run: **all 26 environmental assets are now imported**, 45 prefabs in all with the residential kit and the frontage prototypes, 0 import problems, every LOD triangle and material count equal to the Blender validation.)
* No page errors in any of the 12 scene views; **no magenta pixels in any of the 24 pictures**.
* **Each scene on its own costs 150 to 274 estimated draw calls** (the market street the most). With the early-route baseline (up to 94) added, the market street would be about 363 in the dry season and 368 in the wet: inside 400 but with no margin.
* These are review compositions; nothing in the game places them. The wet scenes contain static puddle and flood meshes only.

## 7. What was not measured, and the bugs found in the tooling
* Frame time on a real GPU (the CI machine has none), memory on a device, the combined scene (kit plus the game's road, terrain, traffic and vehicle in one view).
* The first run's two low-tier views failed in the review driver (a bug: the variant check treated the empty query of `low` as unknown, so GitHub showed that run as *failed*; its build, import, 24 screenshots and numbers are complete); fixed, and measured in the repeat run (section 9).

## 8. The low quality tier (the mobile budget), measured in the repeat run
| View (standard placement) | Draw calls | Engine draws | Visible renderers | Visible triangles | Budget (mobile) |
|---|---|---|---|---|---|
| ref, tier low | **447** | 703 | 216 | 133,807 | 150: **about three times over** |
| street1, tier low | **415** | 674 | 197 | 134,060 | 150: **about 2.8 times over** |
The low tier already shortens its LOD and cull distances (the reference view at the medium tier is 550), yet the kit alone is three times the mobile budget of 150, before the rest of the game (42 to 59 draws at the low tier in the CI smoke run). **The residential street as built cannot meet the mobile budget by content changes of the size done here** (v003 and merged frontages together would still be about 250 to 280 at this tier, an extrapolation from the medium-tier ratio, not a measurement). Reaching it needs a lower-detail tier for the kit or engine batching; both are the owner's decision and neither was done.

## 9. Recommendations (the owner decides)
1. **Look at the pictures**: `kanoreview-ref.png`, `-street1`, `-street2`, `-side`, `-front`, `-top` (v003), the `-merged` and `-shadowsmall` pairs, and the 12 `env_*` scene pictures (artifact `kano-review-in-game` of the run, kept 7 days).
2. If the merged frontages are accepted as a production placement, v003 fits the 400 budget for the kit; to leave margin for the real game, the next content-side step is the villas' lower LODs (LOD1 uses six materials; a four-material LOD1 would save an estimated 2 draws per villa seen at that distance, roughly 30 to 40 on the reference view, at the cost of the stone and grey bands at that distance; **an estimate, not applied**).
3. The shadow policy of section 3 is the largest remaining lever on the real cost (31% of engine draws) and needs the owner's eye.
4. For the mobile tier (section 8) a dedicated lower-detail version of the kit (fewer, simpler parts at that tier) or engine batching is needed; neither is started.
5. Engine batching (`ENGINE_BATCHING_PROPOSAL_RB15.md`) remains a separate proposal; nothing in it is done. With these measurements the case for it is: after every content-side step the game's own estimate is still 434 on the reference view with the baseline added, and the engine count with shadows is 812.

## 10. Draws per LOD level, measured (2026-11-08): what a villa v004 could save
Owner decision of 2026-11-07: measure the draws per LOD first; build a villa v004 with cheaper LOD1 materials only if the improvement is meaningful. `KanoReviewStats` now logs the kit's draws per LOD level and per asset and LOD of the running game (review run [42429096946](https://github.com/AffanNamu/Online-African-Road/actions/runs/42429096946); numbers in `docs/kano/KANO_REVIEW_RUN_42429096946_numbers.json`, table by `python3 tools/review_numbers.py lods <file>`). Same method as before: software GL, tier medium, hour 15.5.

**Where the 550 kit draws of the reference view are.** LOD0 417 (76%), LOD1 102 (19%), LOD2 30 (5%). At LOD0 the largest are the wall panels 164, the piers 54, villa A 49, villa B 28, the wall lanterns 28 and the street 20: **the part of the cost that content can still remove is mostly at LOD0 and is what the merged frontages already attack** (-210 at this view). Villa B is at LOD0 in every standard view (4 instances); only villa A appears at LOD1 and LOD2.

**Villa A at its lower levels** (per instance: LOD1 six materials, LOD2 three materials, measured as draws divided by renderers):
| View | Kit draws | Villa A at LOD1 | at LOD2 | Saving, 4-material LOD1 | Saving, 3-material LOD1 and 2-material LOD2 | Kit draws after (best case) |
|---|---|---|---|---|---|---|
| ref | 550 | 10 inst, 60 draws | 10 inst, 30 draws | -20 (3.6%) | -40 (7.3%) | 510 |
| street1 | 521 | 12 inst, 72 draws | 8 inst, 24 draws | -24 (4.6%) | -44 (8.4%) | 477 |
| street2 | 495 | 10 inst, 60 draws | 8 inst, 24 draws | -20 (4.0%) | -38 (7.7%) | 457 |
| side | 521 | 10 inst, 60 draws | 10 inst, 30 draws | -20 (3.8%) | -40 (7.7%) | 481 |
| ref, merged frontages | 340 | 10 inst, 60 draws | 10 inst, 30 draws | -20 (5.9%) | -40 (11.8%) | 300 |
| street1, merged | 321 | 12 inst, 72 draws | 8 inst, 24 draws | -24 (7.5%) | -44 (13.7%) | 277 |
| street2, merged | 305 | 10 inst, 60 draws | 8 inst, 24 draws | -20 (6.6%) | -38 (12.5%) | 267 |
| side, merged | 323 | 10 inst, 60 draws | 10 inst, 30 draws | -20 (6.2%) | -40 (12.4%) | 283 |
(The savings are the measured instance counts times the material difference; they are exact for those counts, but they are not a measurement of a built v004: that does not exist.)

**Reading.** With the standard placement a villa LOD1 with four materials saves **3.6 to 4.6%** of the kit draws and the most aggressive variant (three materials at LOD1, two at LOD2) **7 to 8%**: not a meaningful improvement on its own, and it costs the stone and grey cladding bands at LOD1 distance. **With the merged frontages** the same changes are worth 6 to 8% and 12 to 14%. At the reference view the merged kit plus the measured baseline of the rest of the game is 340 + 94 = 434 draws against the 400 budget: a 4-material LOD1 brings that to 414 (still over), only the aggressive variant to 394 (under, by 6). **Recommendation: do not build v004 now.** It becomes worth building only if the owner adopts the merged frontages as the production placement and accepts the LOD1 and LOD2 simplification; the larger levers are unchanged: the merged frontages (-210 at the reference view), the small-item shadow policy (engine draws 988 to 682), a mobile tier and, as a separate proposal, engine batching. If a v004 is built it must be a new version of the two villas (LOD0 geometry unchanged, v002 and v003 frozen), with its own freeze, validation, Unity import, a check of the LOD transitions in the running game and new pictures.
