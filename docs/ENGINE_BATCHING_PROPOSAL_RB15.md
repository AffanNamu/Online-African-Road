# Engine batching: technical proposal (RB-15) for the owner's decision

**Status: PROPOSAL ONLY. Nothing in this document has been implemented, and nothing in it will be implemented without the owner's explicit approval.** No driving physics, road generation, weather, vehicle behaviour, rendering configuration or production setting was changed to write it. The only code added in the investigation that led here is review-only diagnostics (`KanoReviewStats`, `DrawBreakdown`) that count what the game already renders and change nothing in the scene.

## 1. The problem, with the numbers
`PerfBudget` for the desktop WebGL tier allows 400 draw calls. In the first in-game review of the residential kit (run 41391901470, `KANO_RESIDENTIAL_MILESTONE.md` section 11) four of six views exceeded it (618 to 690), two did not (107 and 331). The game's figure counts one draw per material slot of every visible mesh renderer, nothing batched.
The investigation (`DRAW_CALL_INVESTIGATION_2026-11-02.md`, run 41450050093) measured it in the running game:
* **The kit is 99.9% of the draws** in the review scene (the scene holds nothing else; the full game adds road, terrain, props and traffic on top).
* Three asset families make 90% of it on the reference view (689 kit draws): wall frontage (panels, piers, lanterns) 264 (38%), villas 207 (30%), vegetation 148 (21%). 82 wall panels of two materials each cost 164 draws for 4,100 triangles.
* The kit uses 27 materials, 21 meshes and **75 distinct (mesh, material) pairs**: 27 and 75 are the floors of perfect merging by material and of perfect GPU instancing, bounds and not predictions.
* All 27 materials already have GPU instancing enabled and 37 set-pass calls show the SRP Batcher working; **draw calls are unchanged by it**.

## 2. What the game does today (from reading the code, not from a guess)
* Every placed asset is a prefab instance with its own `LODGroup`; every kit material is URP/Lit with `enableInstancing = true` (`BlenderAssetBuilder.KitMaterial`), so the shaders are SRP-Batcher compatible and GPU-instancing capable. The measured set-pass count is small (12 to 37 in the first review) because the SRP Batcher already merges state changes; **it does not reduce the number of draw calls**, which is what the budget counts and what dominates CPU cost in WebGL.
* The prop prefabs are instantiated unbatched (`RB-10`); there is no static batching, no combining of meshes, no `BatchRendererGroup`, no `Graphics.RenderMeshInstanced`.
* Shadow casting follows the catalogue flag (`castShadow`, true for everything but signs). The engine's own counter (`engineDraws`) is about twice the game's estimate in the same frame (1,271 against 690 on the reference view): the difference is the shadow pass drawing the casters again. Measured with the game's own test-only `shadows=off` on the reference view: engine draws **1,271 to 693** (the shadow pass is 45% of the draws) and engine triangles 374,545 to 182,471. **The budget gate (the estimate) therefore understates the real submission cost by about 1.8 times whenever shadows are on.**
* Cull distance is 140 m for props, 250 m for buildings and 350 m for vegetation (`PropDef.cullDistance`); the LOD thresholds come from `PropPrefabPlan.LodScreenHeights`.

## 3. Options, from cheapest to most invasive
| # | Option | Where | Expected effect | Cost and risk |
|---|---|---|---|---|
| A | **Content-side merging** of repeated small parts into one mesh per plot side (what the frontage prototype does) | Blender assets, no engine change | **measured: -30%** (reference view 690 to 480, street1 650 to 450; engine draws -29%); alone it does not reach the 400 budget | one asset per layout variant: reuse across different plots is lost; more assets to maintain; no engine risk |
| B | **Fewer materials per asset** (a villa has 10 to 11; a trim-sheet atlas could make it 3 to 4) | Blender assets, no engine change | up to 60 to 70% of a villa's draws; villas are the largest single contributor per instance | atlas authoring; a new version of every affected asset (frozen versions are never edited) |
| C | **Shadow-caster policy**: small or low items (lanterns, hedges, shrubs, wall piers) cast no shadow, tier-dependent shadow distance | prefab flags in the importer (`castShadow` in the catalogue), tier data | removes the shadow pass for those renderers; the review variant `noshadow` measures the ceiling | visible change in shadows: needs the owner's eye; touches the catalogue values |
| D | **Static batching / mesh combining at load** for non-moving kit instances within a chunk | importer or chunk builder (engine) | merges all same-material static meshes of a chunk: the floor is the number of distinct materials | memory (combined meshes), LOD interaction (a combined mesh has one LOD), streaming cost; **needs a design for the chunk streamer** |
| E | **GPU instancing through `BatchRendererGroup` or `Graphics.RenderMeshInstanced`** for repeated identical meshes (piers, panels, lanterns, trees) | engine (renderer path) | the floor is the number of distinct (mesh, material) pairs, measured above | bypasses per-GameObject LODGroups and culling; WebGL 2 instancing limits; a new rendering path to maintain |
| F | **Tighter cull and LOD distances** | tier data and `LodPolicy` | fewer visible renderers | pop-in; interacts with the visual standard |

## 4. Recommendation
Stage the work, cheapest and least risky first, and re-measure with the same review run after each stage:
1. **Stage 1, content only (no engine change, no approval needed beyond the owner's visual review):** the frontage merge (measured -30%) and a villa material reduction in a corrected v003 (estimated to reach the edge of the budget, about 397 on the reference view; arithmetic on measured counts, not a measurement). This is where the owner's approval of v002 comes first.
2. **Stage 2, a shadow-caster policy (option C),** only if the owner agrees to the visual change: the measured ceiling is the 578 draws of the shadow pass on the reference view. It changes catalogue values and tier data, so it needs the owner's explicit decision after looking at pictures with and without the small shadows.
3. **Stage 3, engine work only if stages 1 and 2 leave no margin for the real game (road, terrain, traffic).** Of the engine options I would propose **E (instancing of repeated kit meshes through `BatchRendererGroup` or `Graphics.RenderMeshInstanced`) in preference to D (static merging)**: the measured floor is 75 draws against 689 (an 89% cut), it keeps the kit's assets reusable (D would bake layouts together), and it does not need new assets; its price is a custom LOD and culling path for the instanced items and WebGL 2 instancing limits, which is why it needs a technical design reviewed before any code, and a real-GPU measurement. **I am not recommending it now:** the evidence so far supports stages 1 and 2 first.

## 5. Acceptance criteria and test plan (for whatever the owner approves)
1. Draw calls: the reference view (`ref`) and the three street views stay at or under the desktop budget (400) in the game's own `[Perf]` line, measured by the same review run (`blender-unity.yml`, `[kano-review]`), with `KanoReviewStats` showing where the rest goes.
2. Appearance: the same views rendered before and after differ by less than a stated threshold (`differenceFromStandard`, the review driver already measures it), and the owner looks at the pictures.
3. Performance on a **real GPU** (not the CI software rasteriser, which says nothing about frame time): a frame-time measurement on at least one desktop and one mobile-class device, in the running game, before and after.
4. No change to physics, road generation, weather or vehicle behaviour; the existing test suites (1,085 .NET tests, the EditMode and WebGL smoke jobs) stay green.
5. A rollback: each stage is one reviewable commit, off by default behind a tier flag until verified.

## 6. What I need from the owner
Approve, change or reject a stage (the options are independent; A and B need no engine change and no approval beyond the content review). Until then: **no engine batching work is done.**
