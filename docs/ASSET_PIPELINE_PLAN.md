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
