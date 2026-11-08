# In-game review of the batch E4 corrections (2026-11-08)

**Status: an agent's reading of real in-game pictures. NOT the owner's review and NOT an approval.** Nothing is marked `in_game_visual_review_passed` or `production_approved`. No live gameplay, Supabase, driving physics, weather system or production configuration was touched; the wet scenes are static meshes (no mud physics, flooding behaviour or weather).

## 1. What was reviewed
Review run [42429096946](https://github.com/AffanNamu/Online-African-Road/actions/runs/42429096946) (branch `work/kano-residential`, commit `7c0afc6`, `env_kit=e4`): the residential kit v003, the E3 environmental kit and the six E4 versions, 26 pictures of the same cameras as the run of 2026-11-07 (41489466501), so each E4 picture can be set against its E3 twin (labelled before/after sheets). Same method: the game's own lighting at 15:30, tier medium, software GL, 1280 x 720. The pictures are kept in the private repository (`previews/in_game_review/2026-11-08/`).

## 2. Findings of the first E4 run
| Correction | What the pictures show | Verdict |
|---|---|---|
| **Mud ruts** (`aro_road_mud_rutted_a` v002) | In `env_village_wet-drive` the ruts are clearly recessed, straight-edged, flat-floored troughs between flat ridges; the sawtooth and the raised-slot look of E3 are gone. **But the rut floors show bright green**: the review scene's ground slab sat flush with the nominal road level and covered everything below it (E3 had green slivers for the same reason). A review-scene defect, not an asset defect; it hid the very thing the correction was for. | geometry improved; scene defect found and fixed in the next run (section 3) |
| **Hausa walls** (compounds A and B, wall-and-gate set; E4 versions) | The grey-beige walls with blue-grey blotches are replaced by red-brown earth render with readable relief and no blotches: credible as earth architecture and consistent with the documentary sources (R-024, R-027: red-pigmented earth render). **They read darker than expected** (dark red-brown, almost maroon in shade), darker than the orange ground. The colour value is not established (M-01); a lighter variant is a one-parameter change and a new version: left to the owner's eye (no endless tweaking). | improved; colour is the owner's call |
| **Potholes** (`aro_road_pothole_set_a` v002) | **Not visible**: the set stands at y = +6 m, 50 m from the driver camera. The flat dark rectangles on the market street are the asphalt repair patches (see the correction in `IN_GAME_REVIEW_2026-11-07.md`). | not yet seen: close view `potholes` added |
| **Flood sheet** (`aro_water_flood_sheet_a` v002) | The wet market's driver camera starts **inside** the sheet, so the foreground shows a pinkish wet-mud fringe strip and dark water beyond but not the flood as an object. The aerial cannot resolve it. | not yet seen from outside: close view `flood` added |
| **Environments overall** | Everything else is unchanged and consistent with the pictures of 2026-11-07 (same draw counts per scene: Hausa 179 to 195, village 149 to 160, market street 237 to 272 kit draws). | no regression seen |

## 3. What changed because of this review (and is waiting for its own run)
* **Review ground:** for environment scenes the ground slab is now sunk 0.57 m below the verge under the footprint of the road patches (`KanoReviewBuilder`, editor-only review code), so ruts and potholes lie above a base slab instead of under grass, and the verge keeps its level around the road.
* **Close views:** `potholes` and `flood` (market street), `ruts` (village, wet Hausa lane) and `wall` (Hausa quarter) in the scene definitions and in the review driver, so each correction is seen from a camera that can see it.
* The results of that second run are recorded in section 4 when it has finished.

## 4. Second E4 run
(pending: filled in with the run id, the before/after pictures and an honest verdict when the run has finished)

## 5. Draw calls
The E4 versions cost the same draws as the E3 versions in every scene (materials per asset: potholes 2, ruts 2, flood sheet 3 instead of 1, Hausa assets 5 as before): the flood sheet adds two draws per instance, once per wet market scene. The per-LOD measurement of villa draws is in `docs/PERFORMANCE_REPORT_V003.md` section 10.
