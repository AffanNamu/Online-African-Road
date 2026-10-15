# Realism audit and phased plan (commit 0a85308 + 2bda4bc diagnostics)

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
| Sky/lighting | Procedural skybox, trilight ambient, exp² fog, tested `LightingModel`, Neutral tonemapper, Low/Medium/High tiers (see section 2 of this file's lighting part, and `docs/LIGHTING.md`). |
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
