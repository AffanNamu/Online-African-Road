# Lighting (Phase 1: look-development)

All lighting is driven by one pure model, `ARO.NetCore.LightingModel.Evaluate(hour, overcast)`, so it can be unit-tested without Unity. `World/TimeOfDay.cs` applies the result
(sun, moon, procedural sky, trilight ambient, fog) and `World/RenderingRig.cs` applies the colour-grade part (post exposure and filter).

## Colour space
**Gamma, unchanged.** The `[Render]` log line reports `colorSpace=` at start-up. Switching to Linear changes every UI colour, material and the sky, so it needs a side-by-side screenshot
comparison and a UI check first; there is no evidence yet that it is necessary, so it has not been done.

## Test hour (reproducible screenshots)
`?hour=` in the page address wins over everything: `dawn` (6.4), `noon` (12), `sunset` (17.6), `night` (23), a decimal (`17.25`) or `HH:MM`. Precedence: URL, then the Time-of-Day setting,
then the real West Africa Time clock. Invalid values are ignored. `[Light] hour=… source=url|setting|clock` shows which one applied. `?quality=low|medium|high` works the same way.

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

## What this does not prove
CI renders with a software GL driver at the Low tier (and a second pass at Medium). Passing numbers do not say the lighting looks right: the screenshots must be looked at, and real-GPU appearance and
performance are not measured.
