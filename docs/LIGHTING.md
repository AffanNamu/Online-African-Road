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
