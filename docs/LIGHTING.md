# Lighting (Phase 1: look-development)

All lighting is driven by one pure model, `ARO.NetCore.LightingModel.Evaluate(hour, overcast)`, so it can be unit-tested without Unity. `World/TimeOfDay.cs` applies the result
(sun, moon, procedural sky, trilight ambient, fog) and `World/RenderingRig.cs` applies the colour-grade part (post exposure and filter).

## Colour space
**Gamma, unchanged.** The `[Render]` log line reports `colorSpace=` at start-up. Switching to Linear changes every UI colour, material and the sky, so it needs a side-by-side screenshot
comparison and a UI check first; there is no evidence yet that it is necessary, so it has not been done.

## Test hour (reproducible screenshots)
`?hour=` in the page address wins over everything: `dawn` (6.4), `noon` (12), `sunset` (17.6), `night` (23), a decimal (`17.25`) or `HH:MM`. Precedence: URL, then the Time-of-Day setting,
then the real West Africa Time clock. Invalid values are ignored. `[Light] hour=… source=url|setting|clock` shows which one applied. `?quality=low|medium|high` works the same way.

## Refinement iteration (after commit f096663)
Findings from the f096663 screenshots (measured pixels, not impressions):
* **Post-processing was never drawn.** Low and Medium frames had identical corners and sky/terrain colours (e.g. terrain `233,187,126` vs `233,187,127`), so the Medium vignette and colour grade, and probably
  the ACES tonemapper, did not render. The renderer asset the build generates (`SceneBuilder.EnsureUrp`) was made with `CreateInstance`, which leaves its post-process data empty. The fix assigns URP's own
  default asset; `[Render] ... rendererPostProcessData=` and `[Render] post-processing check ...` log whether it took, and `webgl-lighting.js --compare` checks the Medium corners are darker than Low's.
* **Lemon-yellow truck.** The body material is orange (0.85, 0.55, 0.10) and its true hue shows where only ambient light falls (`100,67,15`). The horizontal top received sun 1.35 plus ambient (R 1.5, G 1.06 before tonemapping)
  and clipped to `255,255,58`. Cause: too much light and no tonemapper, not the material. Noon is now sun 0.95 with lower ambient; a unit test keeps an orange body from clipping to yellow.
* **Dark golden-hour terrain.** A sun 6 degrees up gives flat ground 10% of its light, and the ambient was a weak 0.31. The light is now lifted by up to 12 degrees at golden hour (fading out by 22 degrees,
  monotonic), the sun is stronger there, and the warm fill is brighter. Flat-ground light rose from about 0.39 to about 0.6.
* **Dawn and sunset** share the sun height but now differ in character: sunset light is deeper gold, haze orange and the fill warm; dawn light is paler and rosier, the fill cooler and there is a morning mist (about twice the haze).
* **Night / headlights:** intensity 80 to 240, range 100 m, inner cone 25 degrees (the beam was too weak to reach the road far ahead).
* **Reproducible screenshots:** `?smoke=drive&hold=1` freezes the truck 3 s after it lands, so chase and bumper shots come from the same place. New regions measure the road, the truck top and the terrain, plus
  saturation and "red and green both clipped"; the old bands are kept for comparison.

### Result of enabling post-processing, and the tonemapper decision
Once the renderer had its post-process data (`[Render] post-processing check ... rendererPostProcessData=present volumeTonemap=ACES`), the CI run of commit 0634c3c showed that **ACES is the wrong tonemapper for this
project**: colours are in the Gamma colour space, so the tonemapper sees display-referred values and ACES crushed the dark range (night frame mean 39 to 19 with 59% crushed blacks; golden-hour ground 46 to 17-36).
The default is now **Neutral** (rolls off highlights, leaves darks and mid-tones alone); `?tonemap=aces|none` stays available, and CI renders the Low tier with all three so the comparison is reproducible.
Measured at Low quality, commit 28ba0ee (chase view, mean luma, ground = terrain right of the road):

| | f096663 (no post-processing drawn) | ACES | none | Neutral (default) |
|---|---|---|---|---|
| noon whole / truck top | 161 / `255,255,58` | 110 / `218,166,46` | 129 / 179 | 126 / `214,170,43` |
| dawn ground / sunset ground | 46 / 45 | 36 / 17 | 65 / 42 | 67 / 69 |
| night whole / crushed blacks | 39 / 0% | 19 / 59% | 38 / 0% | 40 / 0% |

The Medium tier's grade now also exists: its saturation was +8 (the oranges were already vivid) and is 0; contrast is still +10 and the vignette 0.22.
Other changes in the iteration after that run: the olive-green upper sky at dawn and sunset (an orange sun colour times a thick atmosphere; measured zenith `153,157,108` at dawn) is addressed by a thinner
golden-hour atmosphere and a less orange sun; noon sun 0.95 to 0.85 so the truck's green/red ratio gets closer to its material; golden-hour lift 12 to 13 degrees; the frozen-truck test hook now stands the truck up
and drops it again if it is not upright (a Medium run froze it on its side).

### Medium-tier ground defect (hole in front of the spawn): cause and fix
Seen as a grey slab and brown vertical faces behind the road start in Medium frames (not in Low). Traced with a test-only probe (`?smoke=drive&hold=1&probe=1`, which logs the renderers within 45 m and a 7 x 7 grid of
ground heights around the frozen truck): the spawn is the route start at the world origin; the road mesh exists only east of it; ground raycasts west of it hit only the invisible collision slab (so the truck never fell), and
Medium's terrain tiles had about 3.5 times Low's triangles (about 3,500 against 960: 40 cells per 200 m tile against 20). Cause: `RoadSpline.Nearest` returns `lateral` as the perpendicular part of the offset to the nearest centre-line point,
which is about zero for a point straight ahead of or behind an end, so `TerrainModel.InsideFormation` reported "inside the road" for a cap of about 14 m beyond every road end. `TerrainGeometry.BuildTile` deletes the quads whose four
corners are inside the road (the road mesh covers them); in the cap there is no road mesh, and only Medium's 5 m cells (against Low's 10 m) leave whole quads there. Not a streaming, camera-visibility or shadow problem.
Fix: `TerrainModel.InsideRoadAlong` (a point whose nearest centre-line point is an end of the line is not on the road) is used for the terrain cut; props keep `InsideFormation`. Regression tests (`TerrainTests`): the formation does not extend beyond the
main road's ends, and no cut cell lies wholly behind the road's first point at 10, 20, 40 and 80 cells per tile; with the old predicate they fail at 40 and 80 (a cut cell at (-15,-5) behind the start at (0,0)) and pass at 10 and 20, as observed.

### Driving test methodology (tools/webgl-drive.js)
The camera-while-driving checks used wall-clock windows (a 1 s pause after each view change, 5.5 s of braking, 4.5 s of steering). On the software renderer (140-290 ms per frame) the same drive covers a different amount of game time, which is why 30 m
in about 5 s measured 26 m after the renderer got slower. The windows are now defined on the game clock (the `t=` of each `[Drive]` line, `Time.time`): the throttle is held until 6 game seconds have passed, braking for 5, steering for 4 plus 1.5 coasting,
and the acceptance is 30 m within the first 4 game seconds (linearly interpolated), maximum speed above 10 km/h, at least 4 samples and at least 3 camera views seen in the telemetry. Nothing was skipped, the camera changes are all still made, the speed is not
altered, and the distance requirement is the original 30 m for the original 4 s. Calibration (CI run of 9d6c3b2, the same drive at two frame rates): speed against game time was the same, Low (about 220 ms per frame) 28, 38, 47, 54, 61, 68 km/h at 0, 1.1, 2.3, 3.4, 4.6, 5.8 s
and Medium (about 290 ms) 25, 35, 45, 53, 59, 67 km/h at 0, 1.1, 2.3, 3.6, 4.8, 6.0 s; 39 m and 40 m were covered by 3.4 s and 3.6 s, about 30% above the limit at both. A second drive at Medium quality runs in CI as a calibration step.
A truck that does not move (throttle swallowed after a camera change) covers 0 m and fails.

## What the model does
* Sun: warm and low at dawn / sunset (golden-hour tint, thicker atmosphere, orange fog), white overhead; switched off just below the horizon where its intensity is already ~0 (no pop).
* Moon: a cool, dim second directional light that fades in as the sun sets, so the road keeps its shape at night. Night ambient has a blue floor (never pure black).
* Exposure: a small post exposure and filter (cool at night, warm at golden hour), only on Medium and High quality tiers (as before).
* Headlights (`HeadlightSpec`): spot, intensity 80, range 90 m, 55 degree outer / 22 degree inner angle, pitched down 1.5 degrees, no shadows. The previous values (intensity 8, range 60) were
  measured to have almost no effect on the night road. Additional lights are per-pixel (up to 4) in the project's URP asset.

## Tests
* `tests/NetCore.Tests/LightingModelTests.cs` (xunit): hour parsing and precedence; every value finite and in range at every hour and overcast level; nothing pops between one game minute and the next;
  sun / moon rules; moonlight cool, sunset warm; night illumination floor and noon at least 6 times night; dawn and sunset symmetric; ambient and exposure ordering; the placeholder truck uses `HeadlightSpec`.
* `tools/webgl-lighting.js` (CI job `lighting`, real WebGL build in headless Chromium): loads the build at each of the four hours, checks the game acknowledged the requested hour from the URL, saves chase and
  bumper screenshots, prints the game's own `[Render]` / `[Light]` lines, and measures luminance (whole frame, sky band, road band; crushed blacks below 6, blown whites from 250).
  `--selftest` checks the measuring code on synthetic images (not on the game).

## Thresholds (enforced on the Low-quality chase view)
| Hour | Whole frame mean | Other |
|---|---|---|
| dawn, sunset | 45 to 130 | sky mean >= 80, road mean >= 25 |
| noon | 110 to 200 | blown whites <= 3%, road mean >= 80 |
| night | 28 to 90 | crushed blacks <= 10%, road mean >= 12, sky mean >= 30 |

Set from the Stage B measurement (night chase whole 38.8, road 18.0; baseline was 20.7 and 5.9, which these thresholds reject). They catch regressions only. The bumper view is measured and
published but not enforced: the scripted truck has driven off the road by the time it is taken, so its content varies. The Medium-tier pass is informational.

## What this does not prove
CI renders with a software GL driver at the Low tier (and a second pass at Medium). Passing numbers do not say the lighting looks right: the screenshots must be looked at, and real-GPU appearance and
performance are not measured.
