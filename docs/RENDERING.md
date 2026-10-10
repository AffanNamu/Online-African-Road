# Rendering foundation

`RenderingRig` (ARO.World) picks a **quality tier** and applies it. Nothing is enabled that the tier does not allow, and every number is a cost lever in `QualityTier` (monotonic Low <= Medium <= High, enforced by tests).

| Lever | Low | Medium | High |
|---|---|---|---|
| Sun shadows | 60 m x1 cascade, hard | 110 m x2, hard | 150 m x3, soft |
| MSAA / render scale | off / 0.8 | 2x / 1.0 | 4x / 1.0 |
| Tonemapping (ACES) | on | on | on |
| Colour grading + vignette | off | on | on |
| Bloom | off | off | on |
| LOD bias | 0.7 | 1.0 | 1.5 |
| Chunks loaded (radius) | 2 | 3 | 4 |
| Prop density / cull scale | 0.5 / 0.6 | 0.8 / 1.0 | 1.0 / 1.4 |
| Terrain detail | 0.5x | 1x | 1.25x |
| Traffic agents | 20 | 40 | 80 |
| Far clip | 900 m | 1500 m | 2200 m |

Selection: `?quality=low|medium|high` in the URL wins; otherwise phones are Low, small or unknown memory is Low/Medium, large desktops High. WebGL reports clamped memory numbers, so detection is conservative on purpose.

What the rig does: URP asset (shadow distance/cascades, MSAA, render scale), camera far clip and post-processing flag, sun shadow mode and bias, `QualitySettings.lodBias`, a global post-processing volume built in code, and a grade that follows the clock (cooler/darker at night, warmer at golden hour). Sky, ambient and fog stay in `TimeOfDay`; rain, wet roads and grip in `WeatherSystem` (both are the weather/time-of-day hooks).

Materials are URP/Lit derived from one template (so the shader is guaranteed to be in the player) with small PROCEDURAL tiling textures (`WorldMaterials`, `ProcTex`): development art that real PBR sets replace without code changes. Foliage is geometry (two-sided strips), not alpha-blended transparency.

Deliberately NOT done: real-time lights on props or the road, screen-space effects beyond the tier table, reflection probes (no baked data pipeline yet), baked GI/occlusion culling (needs authored scenes), SSAO. Each needs measurement on a real GPU first.

Verification status: the tier tables and selection are unit-tested; that the post-processing volume and URP settings really take effect in the WebGL player is checked only through the game's own `[Render]` log line and screenshots in CI (software renderer). Real-GPU visual quality and frame rate are NOT verified.
