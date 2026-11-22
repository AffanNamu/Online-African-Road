# Real scanned surface textures (2026-11-22)

**Why.** The owner (2026-11-22): "in this work I only want highly realistic things". The roads were drawn with small procedural noise textures (128 to 256 px, `WorldMaterials` / `ProcTex`), which is one reason the city does not look real from the driver's seat. Real surfaces come from photographs of real surfaces.

**Source and licence.** Poly Haven (https://polyhaven.com): every texture is **CC0 1.0** (public domain; no attribution required, authors recorded anyway in the manifest). Network access to `api.polyhaven.com`, `dl.polyhaven.org` and `ambientcg.com` was approved by the owner on 2026-11-22.

| Road slot | Picture (Poly Haven id) | Covers on the ground | Brightness tint |
|---|---|---|---|
| Asphalt (carriageway) | `asphalt_02`: grey asphalt with real cracks | 3.0 m | 0.60 |
| Shoulder | `gravel_road`: dusty compacted gravel | 2.0 m | 0.80 |
| Verge | `dry_ground_rocks`: dry orange-brown soil with pebbles (the Sahel ground of Kano) | 4.0 m | 0.60 |
| Concrete (barriers, structures) | `rough_concrete` | 1.23 m | 0.81 |
| Kerb | `rough_concrete` (the same picture) | 1.23 m | (the concrete's) |
| Walkway | `concrete_pavement_02`: square concrete pavers | 1.8 m | 1.50 |

**How it reaches the game.** The game repository holds no binaries, so the pictures are not committed. `tools/textures/scanned_textures.json` (committed) records each picture's URL, md5, sha256, size on the ground and tint; `tools/textures/fetch_scanned.py` downloads and checks them in CI into the git-ignored `Assets/StreamingAssets/Scanned/` before the Unity build (`ci.yml`, and the review build of `blender-unity.yml`). The game streams them **after start-up** (`World/ScannedTextures.cs`), so the initial download and its 6 MB Resources budget (guarded by an edit-mode test) do not grow; until they arrive, and in any build without them, the procedural textures stay. The Low tier halves each picture to 512 px (phone memory).

**Brightness.** Each picture's mean brightness is scaled towards the old surface's (within 0.6 to 1.5, so the photograph keeps its own look), and the scale is baked into its pixels, because the weather system rewrites the road materials' base colour every frame. The old asphalt was nearly black (fresh tar); Kano's roads are sun-bleached grey, so the asphalt aims a little lighter. The CI lighting checks measure screen brightness and will show whether this holds.

**Not done yet (honest list).** Albedo only: no normal or roughness maps (a normal map needs a material in the build that keeps the `_NORMALMAP` shader variant; WebGL strips unused variants). The terrain, the buildings and the vehicles still use their own textures. Copies of the pictures are to be preserved in ARO-CONTENT as Git LFS objects. **Nothing is approved: the owner decides from the in-game pictures.**
