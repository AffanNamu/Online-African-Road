# Unity texture settings, confirmed from a real import, and the texture budget they allow

**Status (2026-11-01): measured.** Everything below comes from a real Unity 6000.0.58f2 run in CI (GameCI WebGL editor image, **cold Library**, nothing cached), driven by `ARO.Editor.TextureReport` on the branch `work/texture-report` (a report tool and its workflow only; nothing in the game was changed), plus the game's own browser `[Perf]` log lines from the main CI run. Runs: `41276805664` (settings), `41277333598` (all textures), `41277975374` (GPU bytes and the two experiments). No setting was changed.

## 1. Targets, settings and overrides
| Item | Confirmed value | How it was confirmed |
|---|---|---|
| Unity editor | 6000.0.58f2, URP 17.0.3 | `ProjectVersion.txt`, `Packages/manifest.json`, run log |
| Targets with a **build entry point** | **WebGL** (`BuildScript.BuildWebGL`, built in every CI run) and **Windows 64-bit** (`BuildWindows`; entry point only, not built in CI) | `Assets/_Project/Scripts/Editor/BuildScript.cs`, `ci.yml` |
| **Android and iOS** | **no build entry point**; the only trace is the world-map importer's per-platform overrides | code search |
| Committed player settings | `ProjectSettings.asset` is a 142-byte stub (only `activeInputHandler`): **every player setting below is Unity's default** | the file |
| Colour space | **Gamma** (`m_ActiveColorSpace = 0`); the game prints `colorSpace=Gamma` in every browser test | run log, `RenderingRig` log line |
| Build-wide texture override | **none**: `overrideTextureCompression = NoOverride`, `overrideMaxTextureSize = 0` | `EditorUserBuildSettings` dump |
| Maximum texture size | **2048** on every platform by default (the flags set 128) | importer dump |
| Automatic format for a default texture (opaque / with alpha) | **WebGL: DXT1 / DXT5**; Windows: DXT1 / DXT5; Android: ETC_RGB4 / ETC2_RGBA8; iOS: ASTC 6x6 | `TextureImporter.GetAutomaticFormat` per platform |
| WebGL build | Gzip build compression (`decompressionFallback` is set to true by `BuildScript` during a build); data caching on; memory 32 MB initial, **2048 MB maximum**, geometric growth (step 0.2, cap 96 MB); mip stripping off; GPU skinning off | `PlayerSettings` and `PlayerSettings.WebGL` dump |
| Unity quality levels | the six Unity defaults; "Very Low" has `globalTextureMipmapLimit = 1` (half-resolution textures), the others 0; anisotropic: Very Low and Low off, Medium and High per-texture, Very High and Ultra forced | quality dump (run 1) |
| The game's own tiers | set `QualitySettings.lodBias` only (`RenderingRig`); **no tier sets a texture limit** | code search |
| Splash screen | Unity splash shown (`m_ShowUnitySplashScreen = true`); its built-in logo texture is 2.7 MB in the build report | player dump, Build Report |

Per-texture importer rules in the project: the world map (`Resources/WorldMap`) overrides WebGL to DXT1 and Android/iOS to ASTC 6x6, max 2048, `npotScale = None` (`WorldMapTextureImporter`); the flags (`Resources/Flags`, 55 images of 96x72) are uncompressed RGB24/RGBA32, max 128; **everything else uses the default importer** (Compressed, max 2048, mipmaps on).

## 2. What was imported (WebGL target, cold import)
**210 texture assets (155 plus 55 flags) use 22.3 MiB of GPU memory with mipmaps** (23,408,779 bytes, computed from each texture's real format and mip chain; the editor's own figure is double, as checked on the world map and on a 2048 x 1024 DXT1 image, because it also counts the CPU copy). The main CI Build Report lists 28.3 MB of textures in the shipped build: the extra is built-in textures (the Unity splash logo alone is 2.7 MB).
* 126 UI images in `Resources/UI`: DXT1 or DXT5, 9-12 mips each, 1024x512 hero images at 0.33 MiB.
* The world map: **6.0 MiB**, see finding 1.
* 55 flags: uncompressed, about 1.7 MiB together.

**Measured in a real WebGL player** (the game's `PerfProbe`, software-GL Chromium in the browser test): while driving, **`textureMB = 5.6` on the low tier and `8.4` on the medium tier** (92 textures), because the UI images and the map are loaded only on menu screens. That is the live texture baseline the world content adds to.

## 3. Findings (all reported; none changed)
1. **The world map is not block-compressed, and the cause is established.** Its importer asks for DXT1 on WebGL, but the imported texture is **RGB24 with 11 mips, 6.0 MiB** instead of about 1.3 MiB. Experiments in the same run: (a) a forced reimport after all scripts were loaded changes nothing (so it is not an import-order effect); (b) the same 1672 x 944 image with default settings becomes **DXT1 at 2048 x 1024, 1.33 MiB**; (c) the same image with `npotScale = None` stays **RGB24, 6.0 MiB**; (d) a power-of-two image (1024 x 512) with `npotScale = None` is DXT1. **A non-power-of-two image with `npotScale = None` silently loses its compression on this target.** `WorldMapTextureImporter` sets exactly that, and its comment and `docs/WORLD_MAP.md` ("about 1 MB on the GPU") are wrong by 6x. Fix options for the owner, not applied (it is shared UI code): `npotScale = ToNearest` (4.7 MiB saved, the image is resampled to 2048 x 1024) or re-export the artwork at a power-of-two size.
2. **The project is in Gamma colour space, not Linear.** Unity then does no sRGB-to-linear conversion when sampling and lighting is calculated in gamma space; the lighting, tone-mapping and the hourly brightness tests are all tuned in it. A Blender-authored PBR asset (a linear workflow) will therefore look different in Unity than in a Blender render, and albedo, roughness and metalness need to be judged in Unity. Changing the colour space would change every lit surface in the game: a shared decision, not made here.
3. **The WebGL target uses DXT (S3TC) because nothing overrides it, and this CI cannot tell whether a phone supports it.** S3TC support is a property of the device's GPU and browser; the CI browser is a desktop software renderer. If a device lacks it, Unity would have to decompress to RGBA32 at load (4 to 8 times the memory); **I have not verified that behaviour**, so the mobile tier's 150 MB texture budget is **unvalidated**. Suggested check: log `SystemInfo.SupportsTextureFormat(TextureFormat.DXT5)` in the browser test and on the reference phones listed in `docs/PERFORMANCE_VALIDATION.md`.
4. **Android and iOS are declared but not buildable.** The Mobile performance budget (150 draw calls, 150 MB textures) and the ASTC overrides exist, but `BuildScript` has no mobile entry point; the only mobile route today is the WebGL build in a phone browser (finding 3).
5. Minor, for information: the 126 UI images keep full mip chains (about a quarter of their size) although UI rarely minifies; and the Unity splash logo is 2.7 MB of the build's textures. Neither was investigated further.

## 4. Verified texture sizes (WebGL target, default importer settings, full mip chain)
Probe textures were imported with default settings and read back. **My size formula reproduces all five of Unity's own numbers exactly**, so the arithmetic below is Unity's.
| Texture | Imported format (verified) | GPU size |
|---|---|---|
| 2048 x 2048 opaque albedo (sRGB) | DXT1 | 2.67 MiB (2,796,216 B, probe) |
| 2048 x 2048 with alpha | DXT5 | 5.33 MiB (computed) |
| 1024 x 1024 opaque albedo, or linear ORM | DXT1 | 0.67 MiB (699,064 B, probe) |
| 1024 x 1024 with alpha, or normal map (Normal Map type) | **DXT5** (not BC5) | 1.33 MiB (1,398,128 B, probe) |
| 512 x 512 opaque / alpha or normal | DXT1 / DXT5 | 0.17 / 0.33 MiB (computed) |
| declared mobile formats: 2048 and 1024 at ASTC 6x6 | ASTC_6x6 | 2.38 and 0.60 MiB (computed; not buildable today) |

## 5. The Kano texture budget these settings allow
Plan: shared atlases, one atlas material per prefab family, `M_<surface>` names for flat shared surfaces.
| Set | Contents | Each | Count | Total |
|---|---|---|---|---|
| building-family atlas | albedo 2048 DXT1 + normal 1024 DXT5 + ORM 1024 DXT1 | 4.67 MiB | 6 | 28.0 MiB |
| street-props atlas | albedo 1024 DXT1 + normal 512 DXT5 + ORM 512 DXT1 | 1.17 MiB | 4 | 4.7 MiB |
| vegetation atlas | albedo+alpha 1024 DXT5 + normal 512 DXT5 | 1.67 MiB | 2 | 3.3 MiB |
| **Kano plan** | | | | **36.0 MiB** |
Plus the measured driving baseline of **8.4 MB** (medium tier): **about 44 MB** against the **300 MB** desktop budget (15%) and the **150 MB** mobile budget (30%), **provided the target compresses**. The proposed design target is "textures at most half of a tier's budget" (so at most 150 MB desktop, 75 MB mobile): the plan fits both. If a device decompressed DXT to RGBA32 (finding 3, unverified) the same plan would be roughly 144 to 288 MB: it would not fit the mobile tier, which is why finding 3 must be checked on a phone before the mobile tier is promised.

Rules that follow, for the Blender pipeline: albedo as opaque DXT1 wherever possible (alpha doubles the size), normal maps authored for DXT5, ORM as a separate linear opaque texture, all power-of-two (or left at the default `npotScale`), never `npotScale = None` on a non-power-of-two image, atlases rather than many small textures, and judge colour in Unity because the project is in Gamma.
