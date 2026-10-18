# Realism audit and phased plan (lighting baseline 0a85308; CI evidence from 2bda4bc)

Status: AUDIT AND PLAN ONLY. Nothing in section 5 onward is implemented. The owner approves the first milestone before any work starts.

Honesty rules used throughout: "measured" means a number from a CI log or screenshot in this repository's history; "software GL" means CI's SwiftShader renderer, which says nothing about real GPUs; "unverified" means no measurement exists. Colour space stays **Gamma**. FRVR material is direction only and is never copied.

## 1. Repository findings

| Item | Finding |
|---|---|
| Baseline | Lighting baseline = commit `0a85308` (local tag `lighting-baseline-0a85308`; the tag could not be pushed from this environment, so the commit hash is the recoverable reference). Later commits only add test diagnostics and docs. |
| Gameplay preserved | Terrain fix (`TerrainModel.InsideRoadAlong`, regression tests at cell sizes 10/20/40/80), driving test (30 m six game seconds after throttle press, 10 km/h), cameras, backend, multiplayer: untouched by this audit. |
| Asset content | **Zero 3D model files** in the repository. `Assets` is 8.8 MB (Resources 6.7 MB UI/flags/dashboard art; Scripts 1.7 MB; Art 96 KB, READMEs only). 210 textures, all UI or generated. |
| Vehicles | `TruckFactory` builds two cubes and four cylinders. Production slot exists and works by id (`Resources/Vehicles/{id}.prefab` or `VehicleDefinition.visualPrefab`, `VehicleRig` contract: axles, wheel colliders/meshes, centre of mass, light emitters, `cockpitCameraAnchor`, `bumperCameraAnchor`, `DriverRig`). No vehicle has a cockpit, driver or LOD (P-01, P-05, P-06, P-92, P-93). |
| Road | `RoadSpline`/`RoadCrossSection` (NetCore, tested) produce asphalt, shoulder, verge, concrete, metal sub-meshes with 256²/128² *procedural* grain textures. 10 m longitudinal subdivision. No PBR asphalt, no normal map, no kerbs, camber or guardrail geometry were found in the mesh builder. Lane markings are not verified in this audit; the render shows none that read as real paint. |
| Terrain | Corridor terrain tiles (20/40 cells per 200 m chunk), 256² patchy procedural textures, two materials (lush, dry). |
| Props | `prop_catalog.json`: 16 ids (palm, tropical tree, bush, house, apartment block, shop row, kiosk, market stall, warehouse, fuel station, bus stop, utility pole, street light, sign, billboard, telecom mast), **all `status: dev`**, generated boxes/cones by `DevPropGeometry`. Production prefabs at `Resources/Props/{id}` replace them by id; `PropCatalogValidator` enforces LOD triangle budgets. Props are batched into one mesh per chunk. |
| Sky/lighting | Procedural skybox, trilight ambient, exp² fog, tested `LightingModel`, Neutral tonemapper, Low/Medium/High tiers (see Appendix B and `docs/LIGHTING.md`). |
| Licensing | Repository is public. `Assets/ThirdParty/` and `Assets/AssetStore/` are gitignored and refused by `tools/check_repo_safety.py`. Private companion repo/submodule recommended in `docs/ASSET_PIPELINE.md` but **not adopted**. `docs/ASSET_LICENSES.md` lists only flag-icons (MIT) and Montserrat (OFL). Note P-33: AI-mockup crops are already committed publicly, which needs a licence decision. |
| Own docs verdict | `docs/VISUAL_STANDARD.md` records that the current 3D output **fails** "realistic", "commercial-grade", "authentically African" and "not placeholder". This audit agrees. |

## 2. Reusable systems (keep, do not rewrite)

* Vehicle contract: `VehicleRig`, `DriverRig`, `VehicleDefinition`, `VehicleLights` (emissive renderers + lights), `TruckFactory.FromPrefab`, camera anchors, `CameraProfiles`. A real model needs no gameplay change.
* Props: catalog + validator + `PropLibrary.ProductionPrefab` replacement by id, `PropBatcher`, `LodPolicy`.
* World: `ChunkStreamer` (rings, pooled, frame-budgeted), `RoadCrossSection`, `TerrainGeometry`, `RouteSpec` data with prop-id validation.
* Rendering: `RenderingRig` tiers, `LightingModel` + tests, `TimeOfDay`, `WeatherSystem` scaffolding, `PerfProbe`, `PerfBudget`.
* Process: CI browser drive test, lighting job with screenshot branches, repository safety gate, asset README conventions (1 unit = 1 m, +Z forward, naming, LOD, collision, texture sizes).

## 3. Most important visual gaps (ranked by what the driver sees most)

1. **Own vehicle** (chase, bumper, cockpit): primitives, no glass, wheels, mirrors, interior, driver. Largest single gap.
2. **Road surface**: grain texture only; no lane paint, edge lines, wear, camber, kerbs, guardrails, patches. Fills the lower half of every view.
3. **Roadside**: 16 placeholder props, no real vegetation, settlements or signage; terrain is two flat patchy materials.
4. **Sky colour**: the procedural shader cannot give an independent warm horizon and clean blue zenith (see lighting part).
5. **Traffic**: same primitive vehicle.
6. **Material response**: no reflection probe, no wet road, no glass reflection; matters once real models exist.

## 4. Recommended asset pipeline

### 4.1 Licensing and repository (blocking decision)

Paid or commissioned assets cannot enter this public repository. Choose one before any asset is bought:
* (a) make the repository private (simplest), or
* (b) private companion repo/submodule `Assets/ThirdParty/` fetched in CI by a token secret; the safety gate keeps refusing tracked third-party files in the public repo and needs a test that the gate accepts the pointer but not the content.
CC0 sources (Poly Haven, ambientCG) may be committed publicly, each recorded in `ASSET_LICENSES.md` with URL, date and licence text.

### 4.2 Sources by category

| Category | Realistic route | Notes |
|---|---|---|
| Hero truck/bus | Commission, or purchase a licensed model and adapt | West-African vehicle models are rare; budget for commissioning. Not generated by us. |
| Traffic vehicles | Licensed low-poly packs, adapted | Pooled, LOD, one atlas per class |
| Asphalt, soil, grass, concrete | CC0 PBR sets (ambientCG/Poly Haven), 1k–2k | Texture arrays or tiled materials; compression measured in the player build |
| Road kit (markings, kerb, guardrail, poles) | **Built in code/shader on the existing cross-section**, meshes for rails/poles authored simply | Markings as shader/decal, not meshes |
| Vegetation, buildings | Licensed packs + commissioned African set pieces | Impostors/billboards for far trees |
| Sky | Custom gradient sky shader (code); CC0 HDRIs only for reflection probes | No HDRI skybox cost |
| Audio | Out of scope for this plan | P-08 |

### 4.3 The "ARO 3D Studio" project

It is **not in this repository** and I have not seen it, so I cannot state what it produces. What can be said generally:
* It may be useful for concept meshes, blockouts, proxies and for generating preview/turntable renders.
* Image-to-3D or text-to-3D output must **not** be assumed production-ready. Every generated mesh needs: manual retopology to a poly budget, clean UVs, baked or authored PBR maps, separate wheels/doors/mirrors/glass/light meshes, pivots and scale (1 unit = 1 m, +Z forward), simple collision, LODs, rig mapping to `VehicleRig`, and a rights review of the generator's terms and training-data claims.
* Acceptance test for any generated asset: it passes the same import validator and rig check as a purchased asset, plus a human visual review. The pipeline does not treat the source differently.
* Recommended first use: one pilot asset (a bush or kiosk) through the full process to measure the real clean-up hours before relying on it for vehicles. That is a measurement, not an assumption.

### 4.4 Import and validation (build this first, it needs no art)

* Editor/CI asset validator: triangle count per LOD (reuse `PropCatalogValidator` budgets; add a vehicle budget), texture size/format, material shader = URP/Lit, no Standard shader, pivot/scale/orientation, wheel hierarchy, required `VehicleRig` slots, collider rules, naming, licence entry present.
* Budgets proposed (to be confirmed by hardware measurement): hero vehicle LOD0 ≤ 80k tris (desktop) with LOD1 ≤ 40%, LOD2 ≤ 15%, LOD3 ≤ 5%, interior ≤ 40k, 2k textures (body, interior, glass/ORM) ≈ 12–16 MB compressed; traffic vehicle LOD0 ≤ 15k, 1k atlas; prop LOD0 per existing catalog.
* Measured evidence rule: sizes are reported from the WebGL player build (`Library` build report and in-browser `PerfProbe`), never from the source files.

## 5. Proposed milestones

| # | Milestone | Needs art? | Blocks on |
|---|---|---|---|
| M0 | Licensing/repo decision + asset validator + CI gate | No | Owner decision (4.1) |
| M1 | **Pipeline vertical slice: one truck through the whole pipeline** (prefab → VehicleRig → LODs → lights → camera anchors → Garage and driving → WebGL size/memory measurement) | One truck model (or a licensed/CC0 trial vehicle to prove the pipeline) | M0, asset source |
| M2 | Cockpit and driver: interior, dashboard, wheel, mirrors, driver rig, steering feed; views 1 and 2 become available | Interior + driver | M1 |
| M3 | Road surface: PBR asphalt, markings, edge lines, shoulder/verge blend, kerbs, guardrails, camber, wear | CC0 textures only | none |
| M4 | Sky gradient shader + Medium tuning, from measured evidence | No | Lighting evidence (Phase 2) |
| M5 | Terrain materials, vegetation and settlement kits (scatter, impostors) | Yes | Asset source |
| M6 | Traffic vehicles (6) + LOD tiers | Yes | M1 |
| M7 | Weather/wet road/reflection probes | Maybe | M3 |
| M8 | Device performance: real-hardware benchmark, tier tuning, streaming | No | Hardware |

M3 and M4 are independent of M1/M2 and can proceed while models are procured.

## 6. Performance and compatibility risks

* WebGL2 only, no practical threads; ~1 GB desktop heap, far less on mobile Safari. `PerfBudget` desktop 1 M tris / 400 draws / 300 MB textures / 120 MB build; mobile 250 k / 150 / 150 MB / 80 MB. A single 2k PBR set is ~5 MB compressed, so a hero vehicle plus road/terrain/vegetation sets already approaches the build budget: texture memory must be measured per milestone.
* Build size: 120 MB desktop budget vs. no assets today (UI art 6.7 MB). Streaming (Addressables) probably needed from M5.
* Shadows (110–150 m cascades) are the main Medium/High cost; glass, reflection probes and wet shaders add fill cost.
* URP Lit on software GL looks nothing like a GPU; **all performance numbers so far are CI software-GL frame times and are not hardware measurements** (`docs/PERFORMANCE_VALIDATION.md` has the hardware plan, not yet executed).
* Gamma space limits PBR accuracy (darks, spec); migration to Linear is a separate approved decision, not part of these milestones.
* Physics: a real vehicle mesh must not change wheel collider radius, centre of mass or mass; the existing driving test is the regression gate.
* Mobile/touch and gamepad remain untested on real devices.
* Legal: rights of purchased/generated assets, AI-generated content terms, public-repo exposure of P-33 crops.

## 7. Acceptance criteria and tests per milestone

| # | Acceptance (all measurable; a visual criterion also needs owner review of screenshots) | Tests |
|---|---|---|
| M0 | Public repo contains no third-party asset files; CI fetches the private asset source (if option b) and fails if missing; validator rejects an over-budget or Standard-shader fixture | `test_repo_safety.py` cases; validator unit tests with good/bad fixtures; CI job |
| M1 | Truck prefab loads by id; `VehicleRig` complete; wheels spin/steer; lights/brake emitters respond; LOD0..3 within budget; collider unchanged; driving test still passes (30 m / 6 s, 10 km/h, 3+ views); lighting job screenshots with the model at dawn/noon/sunset/night; player-build size and texture memory recorded and below budget | Edit-mode prefab validation, existing drive test, lighting job with the model, build report parse in CI, screenshot review |
| M2 | `DriverRig.IsComplete`; views 1 and 2 available; camera tests (eye height, no clipping, near plane) pass; steering animation follows input within one frame | Camera edit-mode tests, browser camera check, screenshot review |
| M3 | Markings: lane centre, edges visible at 100 m; road luminance within the existing lighting thresholds (road ≥25 dawn/sunset, ≥80 noon, ≥12 night); no z-fighting; texture memory ≤ agreed budget | NetCore cross-section tests (kerb/camber offsets), lighting job regions, screenshot review |
| M4 | Dawn vs sunset warmth separation retained, zenith not olive (R/B skyHigh < 1.0 at dawn, hue numeric check), sky shader in WebGL build, no regression in existing thresholds; Medium adjustment only with isolating measurement | `LightingModelTests`, lighting job (sky study), build includes shader |
| M5 | Props marked `production`; polygon/texture budgets pass; visible tris < budget in a fly-through | Catalog validator, fly-through benchmark |
| M6 | Traffic LOD tiers; draw calls and pool within caps | Traffic tests, benchmark |
| M7 | Wet vs dry luminance and specular differ by measured amount | Lighting job variants |
| M8 | Frame time/draw calls/heap measured on named devices | Hardware benchmark report (owner-supplied devices) |

## 8. Recommendation for the first milestone

**M0 + M1 together as "Pipeline foundation and one-vehicle vertical slice"**, preceded by two owner decisions: (1) repository private vs private companion repo, (2) the source of the first truck (commissioned model, licensed model, or an ARO 3D Studio output treated as a pilot with its clean-up cost measured). The vehicle is the largest visual gap and the only way to validate the whole chain (import rules, rig, LOD, lights, cameras, texture memory, WebGL build size) before spending on environment art.

If no vehicle asset can be supplied soon, run **M3 (road surface)** and **M4 (sky gradient)** first; they need no purchases, are fully testable by the existing lighting job, and every driving frame benefits.

## Appendix B. Lighting iteration: evidence and closing assessment (Phase 2)

Source: CI run 38705487149 (commit 2bda4bc), job "Lighting at four hours"; screenshots on branch `ci-lighting`. All numbers are mean luminance 0..255 of the chase view rendered with **software GL**; they say nothing about real-GPU appearance or performance. I looked at the Low and Medium sunset and night chase screenshots; that is a reading of CI screenshots, not an owner review.

### B.1 Recoverable baseline
Commit `0a85308` is the lighting baseline (model, tonemapper, tier settings, thresholds). `LightingModel` is pure and tested, and every later lighting change should be a separate commit on top of it so `git revert` or `git checkout 0a85308 -- <files>` restores it. A tag was created locally but could not be pushed from this environment; a branch `baseline/lighting-0a85308` needs the owner's permission and has not been created.

### B.2 Low vs Medium (whole-frame mean, chase)

| Hour | Low | Medium (shipping) | Medium, vignette off | Medium, contrast 0 + vignette off | Medium, shadows off |
|---|---|---|---|---|---|
| dawn | 85.6 | 75.7 | 82.4 | 83.2 | 74.3 |
| noon | 119.7 | 108.4 | 118.3 | 113.2 | 111.6 |
| sunset | 87.7 | 76.0 | 82.6 | 84.1 | 75.0 |
| night | 39.4 | 29.5 | 33.4 | 34.8 | 29.7 |

Corner/centre luminance (vignette drawn): Medium corners are 0.70 (dawn), 0.77 (noon), 0.75 (sunset), 0.52 (night) of Low's corners.

Attribution (measured):
* **The vignette (0.22) is the main cause**: removing it restores about 6.7 (dawn), 9.9 (noon), 6.6 (sunset) and 3.6 (night) of the 9.9/11.3/11.7/9.9 deficit.
* **Shadows are not a cause**: shadows off changes the mean by −1.4 to +3.2, the noise level of a single frozen frame.
* **Contrast +10 is not darkening**: with contrast 0 the noon mean falls (118.3 → 113.2) and the dark hours rise by only about 1 to 1.5. Setting contrast to 0 also drops the noon road region to 77.7, below the enforced 80 for the Low view, so it is not a free change.
* **Unattributed remainder**: about 3 (dawn), 1 (noon), 5 (sunset), 6 (night). Candidates are render scale (0.8 vs 1.0), MSAA and the longer load radius putting more dark buildings in view, but none was isolated, so this is not a finding.
* Caveat: the Medium hold logged "not settled upright, stood up and dropped again" in most study frames, so the truck pose differs slightly between variants. The effect on whole-frame luminance is small but unmeasured.

### B.3 Is Medium unnecessarily dark?
Whole-frame, 10 to 12 luma below Low at every hour, mostly from the vignette. At night the Medium corners are about half as bright as Low's, which costs peripheral road visibility. **Evidence-backed proposal (not implemented):** reduce Medium vignette intensity from 0.22 to about 0.10 (and keep it from darkening the night frame more than Low's corners). Expected effect, to be re-measured: means within ~5 of Low at every hour, Medium corner/centre ratio ≥ 0.85, night corners ≥ 0.75 of Low. Acceptance: the existing enforced thresholds still pass for Low and Medium, `--compare` still reports the vignette as drawn (ratio < 0.93), night road ≥ 12.

### B.4 Night and headlights
Night chase road region: Low 19.0, Medium 15.3 (15.0 with shadows off, 18.0 with the vignette removed); the enforced floor (12) holds but the advisory (20) is missed on Medium. In the screenshots the headlight pools are visible only a few metres ahead; the terrain and far road are near black, and the buildings read as dark silhouettes against the sky. Headlight intensity and range were fixed in the earlier iteration; no further change is proposed without a measurement of the lit road ahead (add a "road at 15 to 40 m" region to the night capture first).

### B.5 Can the procedural sky give a convincing dawn/sunset?
Measured on Medium: dawn skyHigh R/B 0.85 → sunset 1.18, skyHorizon 1.40 → 2.26 (separation exists, the earlier "dawn = sunset" defect is fixed). But zenith saturation is only 0.15 to 0.19 at both hours, so the upper sky reads as pale grey-green, and the screenshots show a yellow horizon fading to grey, not the orange-to-deep-blue gradient of the references. The shader multiplies its blue channel by the sun colour, so a thicker atmosphere warms the horizon but also dulls the zenith; the two cannot be tuned independently with the current parameters (`atmosphere`, `skyTint`, `exposure`).

**Proposal: a small custom sky-gradient shader** (horizon colour, zenith colour, sun-glow colour and radius, ground colour; driven from `LightingModel`, which already computes sun colour and elevation). Needs: the shader included in the WebGL build (Always Included Shaders or referenced material), a `LightingModel` extension with unit tests (zenith stays blue at every hour, horizon warmth rises towards the sun), and the existing lighting job extended with zenith-saturation and R/B-delta assertions. This is milestone M4; no code written yet. Real-device appearance remains unverified.

### B.6 Performance
No hardware measurement exists. Software-GL frame times (about 260 to 300 ms) are CPU emulation and must not be quoted as performance. The hardware plan is in `docs/PERFORMANCE_VALIDATION.md`.

## Appendix A. Current state and CI (Phase 1)

Repository: `main` and `claude/intelligent-faraday-sykb0x` carry the same history; terrain fix, driving-test calibration and gameplay are unchanged since 0a85308 (later commits add test diagnostics, test-script fixes and docs only). Details and per-check table: `docs/CI_HEALTH.md`.

| Result (commit 2bda4bc, main run 38705487149) | Status |
|---|---|
| Safety gate, .NET core, DB migrations/rules, Backend HTTP, Unity tests + WebGL build | pass |
| WebGL boot, Drive (Low), Drive (Medium) | pass |
| Lighting job (Low thresholds enforced) | pass |
| Browser E2E | **fail, 497/500**: one root failure (selecting the second Garage vehicle) plus two checks that depend on it |
| World map | pass |

Intermittent failures, separated:
1. **Infrastructure (no test ran):** Docker image pull `toomanyrequests: Data limit exceeded` in "Initialize containers" (seen on several jobs and commits). Re-run.
2. **Backend rate-limit test, cause established:** the limiter is a fixed 60 s window and the test's 45 calls straddled a boundary (`ok:45, limited:0`). Test-only fix, assertion unchanged, not yet seen green after the fix.
3. **Browser E2E click failures, cause NOT established:** the first diagnostics show the click reached the game at the right place on a visible card and the card did not react; my first classifier printed a misleading "MISSED", now corrected. An `onClick` log is deployed to split "Unity UI did not invoke the button" from "the handler ignored it". A CI run with it is in flight (commit c20f75b) and will say which; until then this is an open, real problem, not infrastructure, not caused by the lighting work (the suite failed before it, at 4f7ace0, 2e9468d and 550f9fd).

## Appendix C. Graphics experiments and their pre-declared criteria (written before the results were read)

Both experiments are isolated and default-off: the vignette is tried with the existing smoke-page override (`&vignette=0.1`), and the gradient sky is selected only by `&sky=gradient` on a smoke page (`TimeOfDay.UseGradientSky`, `Resources/Sky/ARO_SkyGradient` made by `SceneBuilder`, shader `Assets/_Project/Shaders/ARO_SkyGradient.shader`, colours from the tested `SkyGradientModel`, 13 unit tests, NetCore suite 1030 passing locally). Without the URL flag the game is unchanged; reverting is deleting those files and the flag.

**Medium vignette 0.22 -> 0.10** (`webgl-lighting.js --vignette-verdict`, Low vs shipping Medium vs v10, chase view, software GL). Adopt only if ALL hold:
1. whole-frame mean rises by at least 1.5 luma over shipping Medium at all four hours;
2. corner brightness is at least 0.80 of Low's (0.65 at night) and still below 0.97 (the vignette remains drawn);
3. every enforced lighting threshold passes on the v10 run;
4. night road and whole-frame means are not lower than shipping Medium's.

**Gradient sky** (`--sky-verdict`, Low, chase view). Meets the goal only if ALL hold:
1. the log says `sky=gradient`;
2. dawn and sunset: zenith saturation >= 0.30 and R/B <= 1.0 (procedural: saturation 0.15 to 0.19, sunset R/B 1.18);
3. horizon warmth R/B >= 1.4 at dawn and >= 1.8 at sunset, and sunset at least 0.5 warmer than dawn;
4. noon: zenith saturation >= 0.35 and R/B <= 0.8;
5. every enforced lighting threshold passes with the gradient sky;
6. the lit world is unchanged: ground plus truck luma differ by <= 6 from the procedural sky at each hour (the sky change must not move the lighting).

Passing these numbers would still be software rendering of CI screenshots; a person has to look at the frames, and a real display has not been used. Results are recorded below once CI has run (not before).

### Appendix C results, iteration 1 (CI run 38720599652, commit da256f5; software GL, Low and Medium, chase view)

**Medium vignette 0.10: not adopted by the rule as written, by a hair.** Every criterion passed except one:
| Hour | whole mean: shipping Medium -> v10 (Low) | corner brightness vs Low: shipping -> v10 |
|---|---|---|
| dawn | 75.9 -> 81.1 (86.9) | 0.64 -> **0.80, FAIL: just under the 0.8 limit (the printout rounds)** |
| noon | 108.0 -> 115.6 (120.8) | 0.77 -> 0.95 |
| sunset | 76.0 -> 79.6 (87.6) | 0.75 -> 0.93 |
| night | 29.5 -> 32.2 (38.2) | 0.62 -> 0.81 |
Also passed: all enforced thresholds on the v10 run, night road 14.9 -> 15.5. I am not moving the goalposts after seeing the numbers: the verdict stays "do not adopt as is". The next run adds 0.08 under the same criteria; the decision between 0.10 and 0.08 (or keeping 0.22) is the owner's, with these numbers.

**Gradient sky, default parameters: not yet.** The shader compiled and ran (log says `sky=gradient`, no fallback warning, all enforced thresholds pass). Measured against the criteria:
| Criterion | Dawn | Sunset | Noon |
|---|---|---|---|
| zenith saturation >= 0.30 (procedural) | **0.21 FAIL** (0.21) | 0.31 pass (0.09) | 0.38 pass |
| zenith blue-dominant R/B <= 1.0 (procedural) | 0.79 pass (0.79) | 0.69 pass (0.99) | 0.62 pass |
| horizon warmth R/B >= 1.4 dawn, >= 1.8 sunset (procedural) | **1.08 FAIL** (1.43) | **1.24 FAIL** (2.10) | |
| sunset horizon >= 0.5 warmer than dawn | **1.08 -> 1.24 FAIL** | | |
| lit world unchanged (ground+truck luma <= 6) | 4.4 pass | **8.0 FAIL** | 0.3 pass (night 1.6 pass) |
What it shows: the gradient fixes the zenith at sunset (saturation 0.09 -> 0.31, R/B 0.99 -> 0.69) and keeps the night readable, but the warm band was too thin: the horizon region sampled at about 2 to 6 degrees above the horizon came out less warm than the procedural sky. Two suspects, both untested: (1) the mid-sky stop sat at sqrt(elevation) 0.35, so by 3 degrees the colour was already mostly the blue mid stop; (2) the broad sun glow plus the Neutral tonemapper whiten the zenith at dawn. The "lit world unchanged" failure at sunset (8.0) may be run-to-run noise (the frozen truck's pose and top-surface luma vary between runs); that is not known, so iteration 2 measures the noise floor with a repeat run of the procedural sky.

Iteration 2 (this commit, still behind `?sky=gradient` on smoke pages): mid stop moved to 0.16 (about 9 degrees) with a linear height curve, peach-pink dawn horizon, and test-only overrides `skyglow`, `skymid`, `skyexp`. CI renders the defaults at four hours and three tuning variants at dawn and sunset, each judged by the same criteria, plus the noise-floor repeat. Results will be recorded here after CI has run.

### Appendix C results, iteration 2 (CI run 38727128955, commit 31c5752, main; software GL)

**Medium vignette 0.10 and 0.08, second sample.** Shipping Medium vs Low in this run: whole means 74.6 / 108.4 / 72.5 / 29.3 (Low 82.4 / 116.4 / 85.9 / 39.6).
| Hour | v10 whole mean | v10 corner vs Low | v08 whole mean | v08 corner vs Low | shipping corner vs Low |
|---|---|---|---|---|---|
| dawn | 78.5 | 0.92 | 81.1 | 0.90 | 0.71 |
| noon | 115.7 | 0.95 | 116.4 | 0.97 | 0.77 |
| sunset | 81.1 | 0.88 | 81.6 | 0.89 | 0.76 |
| night | 32.5 | 0.84 | 32.5 | 0.88 | 0.67 |
Every criterion passed except one, in both variants: "night road not lower than shipping Medium" (road 15.8 -> 15.2 for v10, -> 15.5 for v08). **Revision, stated openly:** shipping Medium's own night road mean was 14.9, 15.3, 15.8 and 15.8 across four runs, a spread (0.9) larger than the 0.3 to 0.6 shortfall, and the whole-frame night mean rose by about 3. The criterion now allows 1.0 below shipping; it was not met strictly. With the first sample (da256f5: v10 dawn corner ratio 0.80, a hair under 0.8) the picture is: both values lift Medium by 3 to 9 luma toward Low, no enforced threshold fails, corner brightness stays 0.80 to 0.97 of Low (vignette still drawn). The corner ratio itself varies by about 0.1 between identical runs (v10 dawn 0.80 then 0.92). **Evidence supports lowering the vignette to about 0.10; it is not applied** (it changes the Medium tier's look, so it needs the owner's decision). Software rendering of CI frames only.

**Gradient sky (iteration 2).** The shader works and nothing in the shipped game changed. Fit: the measurements of iteration 1 and 2 (eight data points) are reproduced by a three-parameter model (upper frame samples the sky near sin(elevation) 0.37, the band above the horizon near 0.04, displayed saturation 65% of the model's) to within about 0.03 in saturation and 0.06 in R/B. This showed why "zenith saturation" failed: the chase camera never sees the zenith; the visible upper sky is the MID stop. Results of the default iteration-2 palette: dawn upper sky saturation 0.17 (criterion 0.30), horizon R/B 1.36 (1.4); sunset 0.26 (0.30) and 1.83 (1.8); noon 0.35. Tuning variants moved the horizon (mid 0.22: dawn 1.40, sunset 2.00) but not the upper-sky saturation. The glow was not the cause (a 0.4 glow gave identical upper-sky numbers). Noise floor of identical procedural runs: ground+truck 2.4 (dawn) and 4.9 (sunset); a truck that did not settle gave 41 at noon in one run, so the pixel criterion "lit world unchanged (<= 6)" cannot be judged from pixels. **Second revision, stated openly:** that criterion is replaced by an exact one (the game's own [Light] state, which `LightingModel` produces and the sky does not touch, must be identical with the gradient sky); the pixel difference is printed as information.

**Iteration 3 (this commit):** palette retuned with the fitted model so the visible upper sky is a clear blue and the horizon band warm: predicted displayed upper-sky saturation 0.47 to 0.49, R/B about 0.5; horizon R/B 1.66 (dawn) and 2.34 (sunset), 0.68 apart; noon saturation 0.48. The prediction is now in the unit tests (`SkyGradientModelTests`, 1034 NetCore tests pass locally), and CI renders the default plus mid-stop 0.16 and 0.28 variants, whose predicted horizon R/B (1.53/2.06 and 1.72/2.49) test the model itself. Criteria unchanged except the two stated revisions. Results will be recorded after CI.

### Appendix C results, iteration 3 (CI run 39083408915, commit f341f46; software GL)

**Medium vignette, third sample (criteria with the one stated revision).**
| Variant | dawn corner vs Low | noon | sunset | night | whole-frame mean rise (dawn / noon / sunset / night) | verdict |
|---|---|---|---|---|---|---|
| shipping 0.22 | 0.73 | 0.77 | 0.72 | 0.63 | (74.9 / 108.5 / 76.0 / 29.5; Low 85.2 / 120.7 / 85.6 / 38.7) | |
| **0.10** | 0.91 | 0.95 | 0.88 | 0.82 | +4.8 / +6.9 / +5.2 / +2.7 | **every criterion met** |
| 0.08 | **0.99** | 0.97 | 0.90 | 0.83 | +2.5 / +7.4 / +5.7 / +3.1 | fails: at dawn the vignette is practically gone (0.99, limit < 0.97) |
Across the three CI runs, 0.10 gave corner ratios 0.80 to 0.92 (dawn), 0.95 (noon), 0.88 to 0.93 (sunset), 0.81 to 0.84 (night); no enforced lighting threshold ever failed. **Recommendation: change Medium's vignette from 0.22 to 0.10** (0.08 is too weak at dawn). Not applied; it is a one-line, easily reverted change in `RenderingRig` (the Medium tier's look) awaiting the owner's decision. Software rendering of CI frames only; no real display or device was used.

**Gradient sky, retuned palette: every pre-declared criterion met, in the default and in both variants.**
| Measured (Low, chase view) | dawn | sunset | noon | criterion | procedural sky |
|---|---|---|---|---|---|
| upper-sky saturation | 0.52 | 0.54 | 0.53 | >= 0.30 (noon 0.35) | 0.21 / 0.10 |
| upper-sky R/B (blue-dominant) | 0.48 | 0.46 | 0.47 | <= 1.0 (noon 0.8) | 0.79 / 0.99 |
| horizon-band R/B | 1.88 | 3.61 | | >= 1.4 dawn, >= 1.8 sunset | 1.39 / 2.21 |
| sunset warmer than dawn by | 1.73 | | | >= 0.5 | |
Also: all enforced thresholds pass; the game's [Light] state (sun, ambient, fog, grade) is identical with the gradient sky at all four hours; ground+truck pixel differences 1.7 to 5.9 (information; the noise between identical runs was 2.2 to 25 in this run because one control frame had a different truck pose). The mid-stop 0.16 and 0.28 variants also pass. **Model check:** predicted upper-sky saturation 0.47 to 0.49 vs measured 0.52 to 0.54; predicted horizon R/B 1.66 and 2.34 vs measured 1.88 and 3.61: the fitted model is right about the upper sky and underestimates the horizon warmth, so the horizon is warmer than predicted, not cooler.

**My reading of the frames (CI screenshots, software GL; not an owner review):** sunset shows a deep blue upper sky fading to an orange-gold band at the horizon: a recognisable sunset gradient, clearly closer to the reference direction than the pale yellow-grey procedural sky. Dawn has the same structure in peach-pink with a paler band. The two hours are distinct but similar in construction. Weaknesses: the blue is strongly saturated (slightly cartoonish), the band meets the terrain in a hard edge with no haze, there is no sun disc or cloud in these framings, and the terrain, truck and road are unchanged (flat brown). Night is a dark blue sky and stays readable. Whether it matches your references is a judgement only the owner can make.

**Status of the sky:** still flag-gated (`?sky=gradient` on smoke pages); the shipped game still uses the procedural sky. Making the gradient the default would be a separate, reversible change (TimeOfDay chooses the material) that needs the owner's approval after looking at the frames, plus the same evidence on a real display.

## Appendix D. Milestone 2: Medium vignette 0.22 to 0.10 (owner-approved; working branch `work/medium-vignette-010`)

Change: `QualityTier.VignetteIntensity` (NetCore) replaces the literal 0.22 in `RenderingRig`; Medium is 0.10, High keeps 0.22, Low has no vignette. Reversal is setting Medium's number back to 0.22 (one line), or not merging the branch. The branch was cut from `main` at ec28d46; `main` and the recorded baseline (`baseline/lighting-0a85308`) are unchanged by it.

How it is compared with the recorded baseline: the same CI run renders Low, Medium at the new default 0.10, and Medium forced back to 0.22 (`&vignette=0.22`, the control), then `--vignette-verdict` applies the criteria of Appendix C to (Low, control, new). The recorded numbers of the shipping 0.22 for reference (runs of 31c5752 and f341f46, software GL): whole-frame mean 74.6 to 74.9 / 108.4 to 108.5 / 72.5 to 76.0 / 29.3 to 29.5 for dawn / noon / sunset / night, corner brightness 0.63 to 0.77 of Low. The existing lighting thresholds are enforced on the new default by the normal Medium lighting step. Results are recorded below after CI; software rendering of CI frames is not real-device validation.
