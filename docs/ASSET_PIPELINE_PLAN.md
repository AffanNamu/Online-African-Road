# Realistic asset pipeline: architecture and plan for the first production truck

Status: PLAN ONLY. Nothing here is implemented. It refines section 4 and milestones M0/M1 of `docs/REALISM_AUDIT.md`.
Evidence labels: **measured** = read from a file or a CI log; **estimate** = derived, to be replaced by a measurement in the player build; **unverified** = nobody has run it.

## 1. What the existing vehicle system already gives us (do not rewrite)

| Piece | Contract |
|---|---|
| `TruckFactory.Create` | Uses `VehicleDefinition.visualPrefab`, else `Resources/Vehicles/{id}.prefab`, else a primitive placeholder. A prefab needs a `VehicleRig`; the factory adds `Rigidbody` (mass from the definition), `VehicleController`, `VehicleLights`, input and audio. Used by the player (`DrivingSession`), convoy avatars (`NetworkVehicle`) and traffic. |
| `VehicleRig` | `axles[]` (each `WheelAxle`: left/right `WheelCollider`, left/right visual `Transform`, `steer`, `drive`), `centerOfMass`, `cockpitCameraAnchor`, `bumperCameraAnchor`, `driverRig`, headlight `Light`s and emissive renderers (head, brake, reverse, indicators), `dirtyRenderers` (paint). |
| `VehicleController` | Generic over any number of axles. It pose-syncs only the one `leftVisual`/`rightVisual` transform per wheel, so dual rear tyres must be children of that one transform. |
| `ShowroomFactory.FromPrefab` | The Garage already builds its model from the same prefab (paint renderers, light emitters, inspectable parts, wheel points, `LODGroup`s, optional interior anchor). |
| `PropCatalogValidator`, `PerfBudget`, `PerfProbe` | Budgets and `[Perf]` counters (draw estimate, triangles, texture MB, heap MB) already exist and run in CI. |
| Camera | Chase/bumper/cockpit slots prefer the prefab's anchors; the driver view stays unavailable until a complete `DriverRig` exists (P-92). |

## 2. The candidate first asset and what it is

`ARO TITAN 480` is the procedural reference vehicle from ARO 3D Studio (`docs/acceptance/ARO-TITAN-480.glb`, 3.26 MB). Facts **measured** from that GLB:

| | |
|---|---|
| Structure | 601 nodes, 566 meshes, 15 flat-colour PBR materials, **no textures**, 6x4 tractor, 3 axles, 10 wheel positions (dual rear tyres), rig nodes `Susp_*`, `Steer_*`, `Spin_*`, door/mirror/wiper/steering-wheel pivots, lights, attachment points, `COL_Body` (252 tris) and `COL_Cabin` (142 tris) |
| Triangles per LOD | LOD0 42,140 / LOD1 23,758 / LOD2 12,720 / LOD3 6,434 |
| Separate meshes per LOD | LOD0 **165**, LOD1 165, LOD2 159, LOD3 75 (every one is a separate draw call unless merged) |
| Handedness | glTF convention: +X is the vehicle's left (left door hinge at x=+1.245, steering wheel at x=+0.5). Unity is +X right, so the import must mirror X. The Studio says this is **unverified** (its Unity import "has never been run"). |

Honest assessment: it is a clean, well-structured, **stylised** model, good for proving the pipeline end to end. It is not "realistic" in the sense of the reference screenshots: no textures, no interior detail, no wear, no real glass behaviour. Realism is a separate art task (commission or purchase, section 6), not a property of this file.

## 3. Proposed architecture

```
Source (artist DCC, commissioned model, or ARO 3D Studio export)
   -> GLB + sidecar JSON (vehicle, rig, lods, materials, colliders, validation report, checksums)
   -> asset store (see section 7: public only if owner-original/CC0; otherwise private)
   -> EDITOR-TIME converter  "ARO > Build Vehicle Prefab"  (ours, new, Editor-only)
   -> Assets/_Project/Resources/Vehicles/{id}.prefab + generated meshes + materials   (the existing slot)
   -> validator (EditMode tests + CI gate)  -> smoke drive test with that vehicle  -> measured build report
```

Design decisions:
1. **Convert at editor time to native Unity assets; nothing glTF-related ships in the player build.** The runtime keeps loading a normal prefab through the unchanged slot.
2. **Importer choice (the one unverified dependency):** Unity's glTFast package (`com.unity.cloud.gltfast`) in the Editor, or a small custom GLB reader (positions, normals, indices, no textures needed for this asset). glTFast is the standard route but adds a package whose resolution in our CI (game-ci, Unity 6000.0.58f2) nobody has tested. Spike first (section 5, step 1); fall back to the custom reader if resolution or WebGL stripping misbehaves.
3. **Merge for draw calls:** merge meshes per (LOD, material, side for lights). Keep separate: each wheel group (rotates), steering wheel, doors and mirrors (pivots), left/right light emitters. Expected LOD0 renderers: from 165 down to roughly 25 (estimate).
4. **Rig mapping table (data, tested):** `Susp_Axle_*` to `WheelAxle`; `Spin_*` positions to `WheelCollider`s (radius from the wheel spec, one collider per side per axle); the extra dual tyre becomes a child visual of the same holder; `Light_Head_*` to `Light`s using the existing `HeadlightSpec`; emissive materials to `headlightEmitters`/`brakeEmitters`/indicators split left/right by node side; `Attach_Camera_Chase` to the chase offset; **cockpit and bumper anchors are not in the GLB and must be authored** (derived from `Pivot_SteeringWheel`, then checked by the camera tests).
5. **Collision:** `COL_*` hulls become convex `MeshCollider`s on a child object (no render mesh colliders), centre of mass from the vehicle spec, mass from the definition. The existing `VehicleController` is not edited.
6. **Materials:** shared URP/Lit material set from `materials.json` (base colour, metallic, smoothness = 1 - roughness); glass as one transparent material; emissive materials driven by the existing `_EmissionColor` property blocks. Gamma colour space is kept; base colours are checked on screen, not assumed.
7. **LODs:** one `LODGroup` (heights from `lods.json`), cross-fade off on WebGL; remote vehicles (convoy partners, traffic) force LOD1 or lower so a crowd is not 40k-triangle trucks.
8. **No gameplay change:** the new vehicle is exercised through a smoke-only local `VehicleDefinition` (`?smoke=drive&vehicle=aro_titan_480`), which needs no backend row. The backend row (`vehicle_definitions`, ownership, purchase) is a later migration and needs approval.

## 4. Validation (every item automatic and failing the build)

| Check | Rule |
|---|---|
| Budget: triangles | hero LOD0 <= 60k (measured asset 42k), LOD1 <= 60% of LOD0, LOD2 <= 35%, LOD3 <= 20% (measured ratios 56/30/15%); traffic LOD0 <= 12k |
| Budget: draw calls | hero LOD0 <= 40 renderers after merge; traffic <= 8 |
| Budget: materials / textures | <= 20 materials, URP/Lit only (no Standard shader), no texture > 2048, texture memory <= 16 MB hero / 2 MB traffic (estimate, then measured) |
| Orientation / scale / pivot | forward +Z (front lights at z > 0), left-hand-drive cab means the steering wheel is at x < 0 in Unity, ground at y = 0 (+/- 0.05), length 5 to 20 m |
| Rig | every `VehicleRig` slot the design needs is filled; wheel collider radius within 5% of the visual radius; each collider sits at its visual's position; centre of mass inside the bounds; no missing scripts |
| Collision | convex hulls <= 255 tris, enclose the body, separate from render meshes |
| Lights | head/brake/reverse/indicator emitters respond in a play-mode test (property block `_EmissionColor` changes) |
| Cameras | chase, bumper, cockpit anchors exist; near plane does not clip the body; cockpit eye height plausible |
| Physics regression | drive test with the new vehicle: >= 30 m six game seconds after throttle, > 10 km/h, all axles grounded at rest, no flip, steering visuals turn the right way; the placeholder trucks' drive test is unchanged and still passes |
| Licence | the asset id has an entry in `docs/ASSET_LICENSES.md` (source, licence, date, owner confirmation); the safety gate refuses third-party files in the public tree |
| Build and memory | WebGL build size recorded (**measured today: 35 MB** uncompressed, budget 120); `[Perf]` visible triangles, draw estimate, textureMB, heapMB recorded with the truck in view, Low and Medium; a regression beyond agreed deltas fails |

Performance claims: CI renders with software GL, so draw/triangle/memory counters are real but frame times are not a device measurement. Frame-time acceptance needs the hardware plan in `docs/PERFORMANCE_VALIDATION.md`.

## 5. Steps of the first production-truck milestone (each reviewable, none started)

1. **Importer spike (no art committed):** in CI, import the TITAN GLB (kept out of the repo, fetched from the Studio repository in the job) with glTFast, assert node names, per-LOD triangle counts, and the X mirroring (left door at x < 0 after import). Output: a pass/fail evidence file. Decides glTFast vs custom reader.
2. **Converter + validator:** editor tool, mapping table, EditMode tests for each rule above using the spike output as a fixture.
3. **Prefab in the slot + smoke drive:** `Resources/Vehicles/aro_titan_480.prefab` (only after the licence/storage decision below), smoke definition, drive test and lighting screenshots with the truck, Garage showroom check.
4. **Measured report:** build size, `[Perf]` counters, screenshots for dawn/noon/sunset/night, remaining gaps (textures, interior, driver) stated plainly.

## 6. ARO 3D Studio assessment (repository read, not run)

Read from `AffanNamu/ARO-3D-STUDIO` (commit db0c4a1, shallow clone). Its own README and acceptance report are candid, and I rely on them rather than claiming more:
* **Works and tested by its own suite (246 tests, as it reports):** project/asset/version platform, geometry inspection and repair, PBR materials, rig/axle/pivot spec, convex collision, LOD0-3 generation, 46-rule validation, GLB/glTF/OBJ/Unity-package export (Khronos validator: 0 errors on the TITAN GLB), Docker.
* **Never run:** real AI reconstruction (TripoSR has produced no output; no GPU in its environment), Unity 6 import (its importer script is "untested until you run it once"), Hugging Face deployment. Its report is explicit: "NOT COMPLETE - production readiness is not claimed".
* **Not implemented:** automatic part detection or separation from a single mesh, retopology, auto-rigging, text-to-3D. Wheel, axle and pivot positions come from a spec written by an artist.
* **Licence:** the Studio's own licence is undecided ("all rights reserved" placeholder).

What that means for us:
* **Usable now:** the TITAN reference as pipeline fixture and stylised first vehicle; the sidecar JSON schema (rig, LOD, materials, colliders) as our converter's input contract; its validator as a pre-check before our own.
* **Starting meshes from images:** a TripoSR-style result is a single fused mesh with vertex colours and no separated wheels, doors, glass or interior, and no reliable proportions or scale. Even with a perfect run it needs, by hand: retopology to a triangle budget, splitting into rigid parts, clean UVs and baked or painted textures, wheel/axle/pivot placement, glass and light meshes, collision, LODs, scale/orientation fix, and a visual review against the reference. Treat it as **concept blockout at best, not an asset**. Nothing about its quality is known until the first real run exists.
* **Recommended use:** (1) the Studio's rig/LOD/collision/validation stages applied to an artist-made or commissioned mesh; (2) a time-boxed pilot: one prop (a kiosk or bush) through reconstruction and clean-up to measure the true hours; (3) no vehicle depends on AI output until that pilot succeeds and a human signs off the visuals.
* Needed from the Studio side before we rely on it: one verified Unity import (its own runbook), the handedness check, and an owner licence statement.

## 7. Repository and privacy options (no change made; needs the owner's decision)

| Option | What it means | For | Against |
|---|---|---|---|
| A. Stay public, owner-original and CC0 only | TITAN and CC0 PBR/HDRI only; nothing bought | No cost, nothing to set up, CI unchanged | Cannot use purchased or commissioned assets; the realism target mostly needs them |
| B. Make this repository private | One repo, any assets | Simplest workflow | Changes visibility for everyone; **GitHub Actions minutes**: our run is roughly 100+ minutes (Unity build + browser suites) and every push can start two, so a free private plan's monthly minutes would run out quickly (check the account's plan and billing before choosing); screenshot branches also become private |
| C. Private companion asset repo, pulled by CI with a secret | Public game repo; private `aro-assets` (or a submodule at `Assets/ThirdParty/`) | Keeps the game public, protects paid assets, builds can still ship them | Needs a new private repo and an Actions secret (token or deploy key) from the owner; the safety gate must accept a submodule pointer but still refuse tracked asset files; forks and outside contributors build with placeholders (the placeholder fallback already exists) |

Recommendation: **C** if purchased or commissioned assets are planned (they are), **A** meanwhile for the TITAN pilot once the owner confirms ownership and licence of both repositories. B only if the owner wants the whole project private for other reasons and has checked the Actions cost.

Approvals needed before step 3 of section 5: the storage option; the owner's written licence statement for the TITAN model (and the Studio code if it is to be public); creation of any private repository and its secret (by the owner). No purchase, visibility change or repository creation has been or will be made without that approval.

## 8. Risks

* glTFast resolution, version and WebGL stripping in CI: unverified, hence the spike.
* Handedness: a mirrored vehicle (steering wheel on the wrong side, doors swapped) is easy to miss; the validator checks it explicitly.
* 165 unmerged meshes would add 165 draw calls for every instance including traffic and convoy partners; merging and forced LODs are part of the converter, not a later optimisation.
* Wheel radius, track and mass change handling: the new vehicle needs its own definition and tuning pass; placeholder trucks must not change.
* Transparent glass on WebGL: sorting and fill cost; one material, no double-sided duplicates.
* `Resources` ships every prefab in every build; fine for one truck, Addressables beyond a handful (see `docs/VISUAL_DIRECTION.md`).
* Realism is not delivered by this milestone: a stylised flat-colour truck will still look placeholder-like next to the reference screenshots.

## 9. Options for the first realistic truck, compared (added after the owner's request to compare A, B and C)

Evidence limits: the market facts below come from a few web searches (listing titles and snippets) and **were not verified on the listings**; prices, licences, LOD counts, URP support and wheel setup must be read on each listing before any purchase. Nothing was downloaded or bought.

| | A. Generate with ARO 3D Studio | B. Licensed game-ready asset | Hybrid (recommended) |
|---|---|---|---|
| What you get | A mesh from a reference image (TripoSR) or the procedural TITAN 480 | A modelled, textured, LOD'd vehicle (Asset Store, TurboSquid, CGTrader) or a commission | A purchased/commissioned body and interior, run through the Studio's rig/LOD/collision/validation stages and our converter |
| Proven today | Procedural TITAN 480 only (stylised, flat colours, 42k tris, 4 LODs, rig spec, Khronos-valid GLB). AI reconstruction **never run**; Unity import **never run** | Listings exist (a "Truck and Trailers" pack with 3 LODs per model, a "Heavy Duty Truck" with five LODs and a drivable prefab, an "EV Semi Truck" pack marked URP-compatible); suitability for our rig is unverified | Nothing yet; the converter and validator are the new work |
| Realism | Low to unknown: no textures on the reference; AI output needs retopology, UVs, textures by hand | Highest of the three, if the pack is good; real PBR textures | As B |
| African authenticity | Whatever we design (the TITAN is ours) | Generic European/US trucks; West African vehicles are rare, expect commission or heavy re-skinning (livery, dirt, modifications) | Commission for the African specifics |
| Licence risk | Own work; the Studio's own licence is undecided; a generated mesh needs a rights check of the generator's terms | Asset Store licences generally forbid redistributing source files; **public repo is the problem** (see section 7) | As B |
| Cost / time | Free, but unknown clean-up hours | Money (not purchased; owner's call) plus integration | Money plus integration |
| Technical fit | Rig conventions already match our node naming idea; handedness and Unity import unverified | Wheels/steering usually set for the vendor's own controller, not our `VehicleRig`; expect re-rigging | Our converter normalises both |
| Main risk | It will not look realistic; AI step unproven | Licence and public-repo handling; rig adaptation; may be too heavy (triangles, textures) | Two moving parts |

**Recommendation.** Do not choose between A and B yet; split the work by purpose:
1. **Prove the pipeline with A's one real asset (the TITAN 480):** it is ours, small, structured and already validated by the Studio; it exercises import, rig mapping, LODs, lights, cameras, colliders and the budgets. It will look placeholder-like and the plan must say so.
2. **Get realism from B (or a commission),** after the pipeline works and the owner has decided repository privacy. Shortlist 2 to 3 listings, then download the vendor's free/preview material or buy one under the owner's approval, and run it through the same validator before anything else is spent.
3. **Use A's AI route only as a measured pilot** on a prop (a kiosk or bush) to learn the real clean-up cost; never as the truck.

### Model acceptance checklist (applies to any generated, purchased or commissioned model)

| Area | Requirement | How it is checked |
|---|---|---|
| Unity compatibility | Exports as GLB/FBX that the converter imports; scale 1 unit = 1 m, forward +Z, up +Y, ground at y=0; left-hand-drive cab means steering wheel at x < 0 in Unity | converter spike + validator |
| Materials | URP/Lit (metallic-smoothness), no Standard shader, no per-object material copies, one glass material, emissive for lights, <= 20 materials | validator |
| UVs and textures | non-overlapping UV0 for baked parts, power-of-two textures <= 2048, sRGB only for base colour and emissive, compressed per platform, texture memory within budget | validator + build report |
| Geometry optimisation | triangle budgets per LOD (hero LOD0 <= 60k), no non-manifold or degenerate triangles, merged static parts per material, separate rigid parts only where they move | validator |
| Rigging | wheels as separate transforms about X, steer and spin split, dual tyres under one holder, doors/mirrors/wipers/steering wheel pivots with limits, axles map to `WheelAxle` | rig mapping test |
| Wheels | collider radius within 5% of the visual radius; track and wheelbase match the definition's physics | validator + drive test |
| Lights | head, brake, reverse and indicator emitters separate and split left/right; real `Light`s from `HeadlightSpec` | play-mode test |
| Cameras | chase offset, bumper and cockpit anchors; near plane does not clip; driver view only with a complete `DriverRig` | camera tests |
| Colliders | simple convex hulls (<= 255 tris) or primitives on a separate child, never the render mesh | validator |
| LODs | LOD0 to LOD3 with `LODGroup`, last LOD <= 20% of LOD0, remote vehicles forced to LOD1 or lower | validator + traffic test |
| Memory and build size | counters from the player build, not source files: build size (35 MB today, budget 120), `[Perf]` texture MB and heap MB, no regression beyond the agreed delta | CI build report |
| Licence | recorded in `docs/ASSET_LICENSES.md` with source, licence, date and the owner's confirmation | safety gate |

## 10. Keeping paid assets private while CI still builds (recommended mechanism; nothing created)

1. A private repository (for example `aro-assets`, created by the owner) holds the purchased/commissioned sources and the converter's input (GLB + sidecar JSON). The public game repository holds no asset bytes.
2. CI fetches it in the Unity job with a read-only deploy key or fine-grained token limited to that one repository, stored by the owner as an Actions secret, only when the secret is present: `if: ${{ secrets.ASSETS_KEY != '' }}`. Without it (forks, outside contributors, runs without the secret) the build uses the existing primitive placeholder and still passes, as it does today.
3. `tools/check_repo_safety.py` keeps refusing any tracked file under `Assets/ThirdParty/`; if a submodule pointer is used instead of a CI checkout, the gate must allow the gitlink entry and still refuse blobs. A test must cover both.
4. **Public-repo leaks to close before the first paid asset:** Actions artifacts of a public repository are downloadable by any signed-in GitHub user, so the `webgl-build` artifact (which would contain the converted meshes and textures) must not be uploaded from runs that fetched private assets, or must be restricted; screenshots published to the `ci-screenshots` and `ci-lighting` branches show the asset rendered (usually acceptable for a game, but check each licence); CI logs must not print asset file lists beyond ids.
5. Shipped builds contain the converted assets by nature; vendor licences normally allow that, but extractability of textures and meshes from a WebGL build is a licence point to read per asset.
6. If the owner prefers simplicity over this, making the game repository private removes items 1 to 4 (at the cost noted in section 7: Actions minutes).

Decisions needed from the owner (none taken): the repository option (section 7); whether to create the private asset repository and add the secret; confirmation of ownership and licence of the TITAN model and the Studio code; approval to shortlist and, separately, to purchase or commission a realistic truck.


## 11. The first production truck: licensed game-ready model, commissioned model, and what ARO 3D Studio adds (decision material; nothing purchased or commissioned)

| Criterion | Licensed game-ready model (Asset Store, TurboSquid, CGTrader) | Commissioned model | ARO 3D Studio |
|---|---|---|---|
| What it is for us | The fastest route to a textured, LOD'd, realistic-looking vehicle | The only route to an authentic West African truck or bus that exists nowhere on a shelf | A pipeline around the model, not a source of realistic art: rig spec, collision, LODs, validation, export |
| Realism and authenticity | High realism; generic (mostly European or US trucks) unless re-skinned | Exactly as briefed | Procedural TITAN is stylised; AI output unproven (never run) |
| Licence risk | Standard Asset Store terms generally forbid redistributing source files; extractable assets in a WebGL build are a point to read per listing; paid assets must stay out of this public repo (section 10) | Clean if the contract assigns the rights; AI-generated deliveries must be excluded by contract | Own work, but the owner has not chosen a licence and the code is partly AI-assisted (see `ASSET_LICENSES.md`) |
| Technical fit | Usually set up for the vendor's own vehicle controller; wheels, steering and lights need re-rigging to `VehicleRig`; LOD and texture budgets must be re-checked | Built to our rig, naming, budgets and validator from day one | Its rig-node naming and sidecar JSON are already our proposed contract; Unity import never run |
| Cost and time | Money (not spent) plus integration days; free or preview packs exist but are mostly low-poly or stylised | Money (not spent) plus weeks; quotes needed (no price is assumed here) | Free; clean-up hours unknown until a pilot |
| Control | Little: updates and changes belong to the vendor | Full, under the contract | Full |
| Main risk | Licence and public-repo handling; a model that cannot be adapted cheaply | Quality varies by artist; scope and revision costs | It will not look realistic by itself |

Recommendation (a recommendation, not a decision):
1. **Use the commissioning route for the hero truck** if the goal is an authentic realistic African truck, because it is the only one that gives authenticity, a clean licence and our rig in one deliverable. Use **a licensed game-ready model only as a stand-in** while the commission runs, or for traffic vehicles, after reading the exact listing terms.
2. **Use ARO 3D Studio for what it does**: the rig, collision, LOD and validation stages applied to whichever mesh arrives, and its sidecar JSON as the contract in the commission brief. Use the **TITAN 480 only as a pipeline fixture** until its ownership and licence are confirmed.
3. Do the **importer spike** (section 5, step 1) first; it needs no purchase and tells us whether our converter and the Studio's export agree on handedness, naming and LODs.

Commission brief (what the owner would send; for review, nothing has been sent): deliverables (GLB or FBX with separate wheels, steering wheel, doors, mirrors, glass, light meshes; LOD0 to LOD3 within the budgets of section 4; PBR textures at most 2048; collision hulls; sidecar JSON in the Studio's schema; source files); conventions (1 unit = 1 metre, +Z forward, ground at y = 0, left-hand-drive cab); acceptance (our validator passes, a drive test passes, the owner approves screenshots under the four lighting hours); stage payments tied to those gates (blockout, textured exterior, rigged and LOD'd, interior, final); rights (assignment of all rights to the owner, no use of AI generation or of third-party meshes, textures or brands, a written list of any licensed material used, delivery of editable sources); confidentiality.

## 12. Private-asset boundary: what is already enforced, and what enabling it takes
Already in the repository (inert until private assets exist; tested):
* `tools/check_repo_safety.py` refuses any tracked 3D or art-source file (fbx, glb, gltf, obj, blend, psd, tga, exr, hdr and others) and any generated prefab, material or asset under `Resources/Vehicles` or `Resources/Props` unless the path is in `tools/asset_allowlist.txt` (empty). 22 gate tests, including "allowlist is exact" and "an empty allowlist allows nothing". Derivatives of private art are refused as well as the art.
* The workflow is fail-closed: when the repository variable `ASSETS_REPO` is set, the build artifact, the smoke and lighting screenshot artifacts and the publishing of screenshots to the public `ci-screenshots` and `ci-lighting` branches are all skipped, so nothing built from private assets can leave the job through a public channel; the jobs that need the build artifact then fail loudly instead of silently running.
* Converter output (prefabs, meshes, materials made from private sources) is generated in CI and never committed; the gate enforces that.

Not built (needs the owner): the private repository itself, the read-only deploy key as an Actions secret, the `ASSETS_REPO` variable, and a fetch step in the Unity job (a plain `actions/checkout` of that repository into `Assets/ThirdParty` using the key, skipped when the key is absent so forks still build with placeholders). Because the smoke and lighting jobs need the build, the first real enablement also needs one of: run them in the same job as the build, or upload the build **encrypted** (for example `openssl enc -aes-256-cbc -pbkdf2` with a key from a secret) and decrypt it in the downstream job. Which of the two, and whether screenshots of the licensed vehicle may be published at all, depends on the licence text of the chosen asset; that is decided per asset, not in advance.

## 13. The three routes on the eight requested criteria, and the recommended first production vehicle route (decision material; nothing purchased, commissioned, created or changed in visibility)

How to read the evidence column: **verified** = observed in this repository or in the Studio's own files; **estimate** = general market knowledge, not checked against a listing or quote for this project; **unknown** = nothing in hand. Prices and delivery times are typical ranges to be replaced by real quotes; no figure here is a quote.

| Criterion | A. Licensed game-ready truck | B. Commissioned truck | C. ARO 3D Studio as an AI-assisted start |
|---|---|---|---|
| Realistic appearance | High if the pack is good (real PBR textures) - estimate; generic non-African body unless re-skinned | Exactly as briefed, including West African specifics - estimate | The procedural TITAN is stylised (flat colours, no textures; verified); AI reconstruction never run - **unknown**, and an AI mesh needs retopology, UVs and textures by hand |
| Unity compatibility | Usually ships as FBX/prefab for the vendor's controller and a specific render pipeline; URP support must be read per listing - estimate | Built to our conventions from day one if the brief says so | GLB with a sidecar JSON that matches the planned converter's contract (verified in the Studio files); Unity import never run - **unknown** |
| Geometry quality | Varies by seller; LOD counts differ - estimate; our budgets (`VehicleAssetValidator`) decide | Contractual: budgets and LOD ratios in the brief | TITAN: 42,140 / 56% / 30% / 15% triangles over four LODs, 165 LOD0 meshes, Khronos-valid GLB (verified); AI output quality unknown |
| Rigging requirements | Wheel/steering usually set for the vendor's own controller: expect re-rigging to `VehicleRig` - estimate | Rigged to our node names (`Susp_Axle_*`, `Spin_*`, `Light_Head_*`, `COL_*`, `Attach_Camera_*`) - contractual | Node naming already matches the plan (verified); no automatic part detection or auto-rig (verified); cockpit/bumper anchors must be authored |
| Optimisation effort | Medium to high: merge draw calls (165 meshes to about 25 is the TITAN estimate), cut textures, build LODs if absent | Low if the acceptance list is enforced by stage | Medium: the Studio already makes LODs and collision for its own procedural truck; for an AI mesh it would be high |
| Licence terms | Asset Store/marketplace terms generally forbid redistributing source files; a public repository is the main obstacle (the private-asset boundary of section 12 exists for this); read each listing | Clean only if the contract assigns the rights and excludes AI-generated deliveries - estimate | Own code, but the owner has chosen no licence and parts are AI-assisted (`ASSET_LICENSES.md`); a generated mesh needs a rights check of the generator's terms |
| Cost | Typically tens to a few hundred USD per pack - estimate; free/preview packs are mostly low-poly | Typically several hundred to a few thousand USD for one hero vehicle - estimate | No cash cost; unknown clean-up hours |
| Delivery time | Immediate download; integration days to a couple of weeks | Typically several weeks with stage gates - estimate | TITAN available now; an AI pilot takes days to learn the real clean-up cost |

### What exists now (all testable without any art file)
* `VehicleAssetValidator` (NetCore, 8 tests): the acceptance rules of section 4 as code (triangle and LOD budgets, draw calls, materials, textures, scale, pivot, orientation, rig nodes, wheel colliders, collision hulls, licence entry). It judges numbers from a manifest only; it loads no asset and touches no vehicle code. The first real model must pass it before any prefab exists.
* The private-asset boundary (section 12) and the safety gate that keeps art out of the public tree.
* The existing vehicle system is untouched: no prefab, no `VehicleDefinition`, no controller change.

### Recommended first production vehicle route (a recommendation, not a decision)
1. **Commission the hero truck (B)** against the brief in section 11 and the validator above, with stage payments tied to the gates. It is the only route that gives authentic West African appearance, a clean licence and our rig in one deliverable.
2. Meanwhile **do the importer spike with the TITAN fixture** (section 5, step 1; no purchase, no repository change). The TITAN stays a **fixture only**: its ownership and licence are not confirmed, so it is not shipped or distributed, and the pipeline's claim stops at what the spike demonstrates.
3. **Use a licensed model (A) only as a stand-in or for traffic vehicles**, after reading the exact listing terms and only once the private-asset storage decision is made.
4. **Treat ARO 3D Studio (C) as tooling**, not as the source of the realistic truck. Nothing in this repository claims that its AI reconstruction or its Unity import works end to end; that needs real inference and a real import to be demonstrated first.

Decisions needed from the owner before anything beyond the importer spike: the private-asset storage option (section 7/10), approval to request commission quotes, approval to buy or fetch any listed pack, confirmation of the TITAN's ownership and licence.
