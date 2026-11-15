# The realistic look (owner direction of 2026-11-15)

The owner sent a reference picture (a rendered tropical island: deep blue sky with lit cumulus clouds, rich colour, dense vegetation) and said: *"If I say realistic, that is how I want realistic to be. Look at the sky. Look at the colour. That is how I want my city to look. Even the people: their faces have to show like real people."* The figures built by code on 2026-11-14 were rejected as unrealistic. **Nothing here is approved.**

## 1. Where the game stands against that picture (read from the CI and review pictures of 2026-11-14)

| What | State on 2026-11-14 | Against the reference |
|---|---|---|
| Sky | A plain blue gradient, no clouds | Far. **Being replaced now (section 2).** |
| Ground | Flat brown terrain with one texture | Far. Not started. |
| Vegetation | A few palms and bushes | Far. Not started. |
| Buildings in the review build | The Blender models with painted surfaces | Closer, but lit flat and standing on bare ground. |
| Buildings in the playable public build | Grey boxes of the right size | Far. The real models are not shipped: they wait for the owner's approval and a decision on shipping them (section 4). |
| People | Code-built figures with blank faces | Rejected by the owner. **Next (section 3).** |
| Light and colour | Tuned and measured (`docs/REALISM_AUDIT.md`), but flat on bare ground | Needs the sky, the ground and shadows to read as real. |

## 2. The sky (implemented, level 1 and 2; in-game pictures pending)

* `Assets/_Project/Shaders/ARO_SkyClouds.shader` (`ARO/SkyClouds`), now the game's sky. It keeps the tuned three-stop gradient of `SkyGradientModel` and adds, per pixel and from noise only (no textures):
  * a bank of cumulus heaps above the horizon: flat bases, billowing tops, sunlit sides, grey-blue bases, sinking into the horizon haze;
  * smaller cumulus and thin high cloud overhead;
  * the sun's glow and disc dimmed behind cloud, with a silver lining towards the sun;
  * stars at night.
* `SkyCloudModel` (NetCore, 5 tests) gives the cloud colours for the hour and the weather: white over grey-blue by day, the horizon's own peach or orange at the golden hours, dim blue shapes at night, flat grey when overcast. Sunlit white is 0.93, so bright cloud never counts as a blown-out frame in the lighting checks.
* The clouds drift with the day's clock, so every player sees the same sky. The low quality tier uses fewer noise layers.
* Only the look of the sky changes. The sun, the ambient light, the fog and the weather rules are untouched. `?sky=procedural` on test pages shows the old sky for comparison.
* The maths was prototyped in Python and rendered to pictures first (noon, dawn, sunset, night), then written as the shader. It has not been seen in the game yet: CI builds it next.

## 3. People (next)
Real faces need a real human base: anatomy and a face cannot be drawn convincingly from tubes and boxes. The plan is the option the first proposal already listed: a public-domain (CC0) parametric human base, shaped and dressed by our own code in Blender (Kano dress, skin tones, ages), with painted skin and cloth textures, staged privately like every binary, and drawn by the existing crowd system in place of the code-built figures. This replaces the owner's earlier answer "people built by code" for the body and the face; the clothes stay our code.

## 4. Decisions that are the owner's
1. **Ship the real models in the playable build.** Today the playable build draws grey boxes; the Blender models appear only in the review build. Approving them (after looking at the review pictures) and shipping them is the largest single step towards the reference look.
2. **The human base** (section 3), if a public-domain base is not wanted.
