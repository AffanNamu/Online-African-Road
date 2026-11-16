# The realistic look (owner direction of 2026-11-15)

The owner sent a reference picture (a rendered tropical island: deep blue sky with lit cumulus clouds, rich colour, dense vegetation) and said: *"If I say realistic, that is how I want realistic to be. Look at the sky. Look at the colour. That is how I want my city to look. Even the people: their faces have to show like real people."* The figures built by code on 2026-11-14 were rejected as unrealistic. **Nothing here is approved.**

## 1. Where the game stands against that picture (read from the CI and review pictures of 2026-11-14)

| What | State on 2026-11-14 | Against the reference |
|---|---|---|
| Sky | A plain blue gradient, no clouds | **Replaced (section 2):** cumulus, haze, stars; seen in the game (CI run 349). |
| Ground | Flat brown terrain with one texture | **Started (section 5):** savanna ground over 64 m instead of a 16 m carpet; not yet seen in the game. |
| Vegetation | A few palms and bushes | Far. Not started. |
| Buildings in the review build | The Blender models with painted surfaces | Closer, but lit flat and standing on bare ground. |
| Buildings in the playable public build | Grey boxes of the right size | Far. The real models are not shipped: they wait for the owner's approval and a decision on shipping them (section 4). |
| People | Code-built figures with blank faces | Rejected by the owner. **Realistic people built (section 3)**; in the review build, not yet seen in the game. |
| Light and colour | Tuned and measured (`docs/REALISM_AUDIT.md`), but flat on bare ground | Needs the sky, the ground and shadows to read as real. |

## 2. The sky (implemented; seen in the game in CI run 349, 2026-11-15)

* `Assets/_Project/Shaders/ARO_SkyClouds.shader` (`ARO/SkyClouds`), now the game's sky. It keeps the tuned three-stop gradient of `SkyGradientModel` and adds, per pixel and from noise only (no textures):
  * a bank of cumulus heaps above the horizon: flat bases, billowing tops, sunlit sides, grey-blue bases, sinking into the horizon haze;
  * smaller cumulus and thin high cloud overhead;
  * the sun's glow and disc dimmed behind cloud, with a silver lining towards the sun;
  * stars at night.
* `SkyCloudModel` (NetCore, 5 tests) gives the cloud colours for the hour and the weather: white over grey-blue by day, the horizon's own peach or orange at the golden hours, dim blue shapes at night, flat grey when overcast. Sunlit white is 0.93, so bright cloud never counts as a blown-out frame in the lighting checks.
* The clouds drift with the day's clock, so every player sees the same sky. The low quality tier uses fewer noise layers.
* Only the look of the sky changes. The sun, the ambient light, the fog and the weather rules are untouched. `?sky=procedural` on test pages shows the old sky for comparison.
* The maths was prototyped in Python and rendered to pictures first (noon, dawn, sunset, night), then written as the shader.
* **Seen in the game (CI run 349, commit 3da502a, all jobs green including the measured lighting checks at noon, dawn, sunset and night):** on the medium tier the cumulus heaps stand over the horizon with lit tops and grey-blue bases, like the reference. On the low tier the heaps were smeared into soft blobs (three noise octaves); the low tier now uses four for the cloud shapes (2026-11-16).

## 3. People (built 2026-11-16; review build only)
Real faces need a real human base: anatomy and a face cannot be drawn convincingly from tubes and boxes. The ten crowd designs now have realistic versions: the **CC0 MakeHuman** body and face (fetched at a pinned commit, never committed), shaped per person, with **Kano dress made by our code** (kaftans with thread work, babban riga, ankara wrappers, hijab, abaya, caps, head-tie and basin), three skin tones and three colourways, textured from the full model and reduced to about 3,200 and 900 triangles with walk and idle frames. Details, tests and limits: `docs/PEOPLE_AND_CROWDS.md`, section 0. The review build generates and draws them; the playable build keeps the code-built figures until the owner decides (section 4).

## 5. The ground (started 2026-11-16)
* The terrain was one 256 px patchy texture repeating every 16 m: from the road it read as a flat brown carpet. Both terrain materials now use `ProcTex.Ground` (`World/WorldMaterials.cs`): one 768 px texture over 64 m with grass patches (dry gold to green) over red-brown laterite at three scales (about 20 m, 5 m and 0.7 m), tufts with darker hollows and pebbles in bare soil. Out of town there is more grass; in town (urban, commercial and industrial zones) mostly laterite with sparse grass.
* Prototyped as pictures first (the same formulas in Python); the mean brightness is within 10% of the old textures, so the measured lighting checks should still hold. One edit-mode test (seamless edges, size, 64 m tiling).
* Cost: about 3 MB of texture memory per material and about 0.2 s (native) to build each at start-up.
* Not done yet: grass and shrubs standing on the ground, more trees, and blending the ground into the verges. Standing vegetation needs an alpha-clipped material whose shader variant must be kept in the build: that comes with a short proposal, because it touches how the world is drawn.

## 4. Decisions that are the owner's
1. **Ship the real models in the playable build.** Today the playable build draws grey boxes; the Blender models appear only in the review build. Approving them (after looking at the review pictures) and shipping them is the largest single step towards the reference look.
2. **The human base** (section 3): the CC0 MakeHuman base replaces "built by code" for the body and the face (`docs/DECISIONS.md`, 2026-11-16). If the owner does not want a public-domain base, the code-built figures stay.
3. **Real people in the playable build**: the same question as item 1, for the people (the review pictures come first).
