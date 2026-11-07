# In-game review of the Kano residential kit (v002, v003) and the environmental kit (2026-11-07)

**Status: an agent's reading of the real in-game pictures. It is NOT the owner's review and NOT an approval.** Nobody has set `in_game_visual_review_passed` or `production_approved` for any asset; the registry's `visualReview` field stays empty until the owner has looked and decided. No live gameplay, Supabase, driving physics, weather system or production configuration was touched.

## 1. What was looked at, and how it was kept
The pictures are real screenshots of the running Unity WebGL game (the review scene, the game's own lighting at 15:30, quality tier medium, software GL, 1280 x 720) from three GitHub Actions review runs: **41450050093** (v002 kit), **41476407715** (v003 kit and the environmental kit, 25 of its 26 assets in a scene) and **41489466501** (v003 kit and all 26 environmental assets, plus the low tier). They were downloaded on 2026-11-07 (the GitHub artifacts expire on 2026-11-09), labelled by version and camera view with `tools/label_review_pictures.py`, and **preserved as Git LFS objects in the private repository**: branch `reviews/in-game-2026-11-07`, folder `previews/in_game_review/2026-11-07/` (raw pictures, the game's own summary JSON, the commit each run built, labelled sheets; verified through a fresh clone, 68 of 68 objects intact). The sheets were also sent to the owner. Frame times are meaningless (software GL); the draw-call figures are the game's own counts.

## 2. v002 against v003 (same camera, same lighting)
| View | What I see | Draw-call estimate v002 / v003 |
|---|---|---|
| `front` (left sidewalk, facing the frontages) | The clearest difference. v003 shows the round-arched entrance with its stone surround behind the gate, stone-clad pilasters and bays, proud grey cladding panels beside the first-floor windows, a stepped cornice under the balcony and warm wall lanterns; v002's facade is plainer plaster. Gate, walls, piers, hedges, shrubs, tree, kerb and pavers look the same. | 107 / 75 |
| `street1` (pedestrian, right sidewalk) | The arched entrance and the cladding panels are visible on the nearest villa; everything else is unchanged. | 650 / 521 |
| `ref` (R-006 reference camera), `street2`, `side`, `top` | Differences are small at these distances (the villa facades are far away); the street reads the same: tree rhythm, streetlights, One Way sign, kerb stripes and road markings. No colour shift from the shared palette materials is visible (the compromise of one roughness per material family is not visible at these views). | `ref` 690 / 550 |
Nothing broke: no magenta pixels, no missing materials, no black or z-fighting faces on the villas are visible in the stills (the coplanar fix shows no defect in any view; flicker cannot be judged from stills). What is **not** established: whether the facade is close enough to the concept R-006 (the concept shows more landscaping and ornament; the registry flags list the known differences) and whether v003's smaller visible gain at distance justifies it. That is the owner's call.

## 3. The environmental kit in the running game
All 26 assets were imported by the real Unity importer (LOD triangles and material counts equal the Blender validation) and six scenes were driven (`docs/ENVIRONMENT_SCENES.md`). What the pictures show:
* **Works:** every scene renders with its materials and textures (no error shader, no missing asset); scale looks right against the trees and the lane; the dry and wet versions differ where they should (foliage colour, grass, ground, mud, puddles); shadows and lighting behave like the residential street.
* **Hausa-style quarter (design interpretation, no reference, gap M-01):** high parapeted walls with corner spikes, a gate tower, a dark brick plinth, open courtyards seen from the aerial camera, drains along the lane.
* **Village (vernacular not verified, M-14):** mud-brick houses with rusted corrugated roofs, woven fences, banana plants, a rough canopy stall, a rutted mud lane when wet.
* **Market street (M-14; real Kano markets are not depicted):** shop rows with roller shutters and canopies, umbrellas and produce on tables, an open shed, lock-up kiosks, a utility pole with overhead wires, cracked asphalt.

**Visible weaknesses (candidates for a v004; none is a bug in the pipeline):**
1. **Potholes** read as flat dark rectangles with small orange ticks at their edges, not as depressions (known: stair-stepped outlines at 0.17 m cells; a decal and normal-map layer is the proper answer, RB-06).
2. **The flood sheet** in the wet market reads as a pale flat translucent band across the road, not as water (known: no water shader, RB-21).
3. **Rutted mud at the driver's eye** (wet village) looks like raised rounded ridges with grey-white puddle strips; whether the ruts read as recessed needs a closer camera.
4. **Hausa walls** read grey-beige with bluish blotches; the relief motifs look like stains, and the colour is cooler than an earth (tubali) wall might be. A design choice without a reference.
5. **Open drains** read as pale slabs with rebar stubs; fine from above, flat strips at driver height.
6. **The review ground** is a single-colour plane to a hard horizon (orange dry, green wet): a limitation of the review scene, not of the assets, but it makes the dry scenes look bare. The game's own terrain would replace it.
The wet versions are static meshes: **no mud physics, flooding behaviour or seasonal weather is implemented** (RB-18 to RB-25 are proposals).

## 4. Draw calls in these views (kit estimate, the game's own count)
v002 standard 618 to 690 over the hard views; v003 standard 495 to 550; v003 merged-frontage prototypes 305 to 340; low tier 447 and 415 (mobile budget 150). The environment scenes alone cost 150 to 161 (village), 180 to 196 (Hausa quarter) and 241 to 274 (market street) on top of the engine's baseline. Details and the shadow-pass breakdown: `docs/PERFORMANCE_REPORT_V003.md`, `docs/DRAW_CALL_INVESTIGATION_2026-11-02.md`.

## 5. The release chain at this date
`python3 tools/release_chain.py` (see `docs/ASSET_RELEASE_CHAIN.md`) checks reference, Blender source, exports, Unity prefab (import report), validation and in-game review for every produced asset. Result: **no broken link in any of the three asset branches**; the open links are the owner's review (every asset) and, for three Hausa assets and the concrete pole, a documented missing reference.
