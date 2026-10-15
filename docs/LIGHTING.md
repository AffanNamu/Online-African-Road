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
The camera-while-driving checks used wall-clock windows (a 1 s pause after each view change, 5.5 s of braking, 4.5 s of steering). On the software renderer (140-290 ms per frame) the same drive covers a different amount of game time, which is why
"30 m in about 5 s" measured 26 m after the renderer got slower. Two fixes, the second after the first proved insufficient:
1. **Game-clock windows.** Every window is defined on the game clock (the `t=` of each `[Drive]` line, `Time.time`): braking is held for 5 game seconds, steering for 4 plus 1.5 coasting.
2. **Acceleration anchored to the real throttle press.** The first game-clock version measured from the first telemetry sample after the press (samples are logged once per second of wall-clock). That sample falls anywhere in the 1.1 s after the press, so the same truck
   measured 39 m in one CI run and 29 m in another (a run whose first sample read 0 km/h against one that read 28 km/h): the "30 m in 4 s" limit I first wrote is about what a truck covers *from rest* in 4 s, so it was marginal and my claim that it matched the old strictness was wrong.
   The game now logs `[Drive] throttle on t=<game time>` (smoke pages only, once per press), and the test measures from there: the truck starts at rest at the position of the last sample before the press, and the distance and speed are read 6 game seconds later
   (linear interpolation inside the segment that crosses the limit; checked offline on a constant-acceleration truck with 2,000 random sample phases: worst error 1.2%). Acceptance: at least 30 m 6 game seconds after the press, maximum speed above 10 km/h,
   at least 4 samples, at least 3 camera views seen in the telemetry. The throttle press must be logged (otherwise the test fails: the throttle did not reach the truck), the game clock must advance (a real stall fails), and a truck that does not move covers 0 m and fails.
Calibration (four earlier CI runs, the same drive at two frame rates; Low about 220 ms and Medium about 290 ms per frame): speed against game time agreed between frame rates (28, 38, 47, 54, 61, 68 km/h at 0, 1.1, 2.3, 3.4, 4.6, 5.8 s against 25, 35, 45, 53, 59, 67 km/h at 0, 1.1, 2.3, 3.6, 4.8, 6.0 s);
6 game seconds after the press the truck was at 51 to 75 km/h and had covered about 55 m (runs that started from rest) to about 100 m (runs whose first sample was already moving). Twelve CI measurements of the final method (Low and Medium, six runs): distance 46-59 m (the 30 m limit is about 0.65 of the lowest), interpolated speed at 6 s 38-58 km/h and maximum speed 52-68 km/h. A 35 km/h speed requirement that the first anchored version had was removed (its margin over the worst observation was 3 km/h; the original test never had it). The 30 m limit sits at roughly half of the typical distance; the number (30 m) is unchanged from the original test, the window
is now defined in game time from the press instead of "five samples of wall-clock". A second drive at Medium quality runs in CI as a calibration step, and each run prints the press time, the per-sample speeds relative to the press, and the distance and speed at the window.

### Browser E2E: a pre-existing intermittent failure (not changed by this milestone)
The end-to-end suite (about 530 checks against a real backend, `tools/webgl-e2e.js`) fails one to seven checks in roughly half of the CI runs, a different one each time, always a single click followed by a 60 s wait for the log line that click should cause.
**It predates the lighting work.** Failing checks seen at commits before post-processing was drawn or any lighting change: at 4f7ace0 (camera milestone, `main`) "the Recent Players tab opens" and three checks that depend on it; at 550f9fd "sorting by name is applied", "Accept", "Add Friend on a search result" and others; at 2e9468d too.
The same commit also passed on one CI run and failed on another (4f7ace0: branch run passed, `main` run failed). After the lighting changes the failing checks were "View Profile" (933f34f), "the Jobs board" (4450e2c, twice), "the Requests tab", "the Front preset" (cb45c26 and 64581f5), "an exact username is found" (64581f5), all the same shape.
What was tried and did not work: (1) making the harness repeat a click when the game logged no `[UIDiag] mouse pressed` line: the build logs that line for only a few presses, so the retry never activated (removed); (2) holding each press 700 ms instead of 150 ms (a hypothesis that a press shorter than a rendered frame can fall between two samples on the software renderer):
the suite still failed in the next run, so the idea is not confirmed and the change was reverted. `clickUntil` (the repo's existing repeat-until-effect helper) is now used for the two profile checks that were single clicks; no assertion, regex or timeout was changed anywhere.
**Treat this suite as flaky: a failure there, with the checks in this list's shape, is not evidence about lighting.** A proper fix needs its own task: for example have the game acknowledge every pointer press it receives through the Input System in E2E mode (the legacy `Input.GetMouseButtonDown` used by `UIDiagnostics` is rarely true in this project) so the harness can repeat a click only when it was really lost.

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
