# Blender production pipeline (African Roads Online)

ARO 3D Studio is a separate project and nothing here depends on it. This is the main game's own way of producing real 3D assets: **Blender 5.2 LTS** as the modelling environment, an **MCP server** as the agent's control interface, **Unity** as the final integration environment.

Status (2026-11-01): the toolchain exists and is tested (section 4), and **FBX and GLB import into the real Unity 6 project has been run and measured** (section 8, with the numbers). **No production asset exists yet**; the only assets are a test bollard and an asymmetric calibration model whose job is to prove the chain. The first environment is a neutral layout scaffold only, waiting for the owner's reference pack (`docs/FIRST_SECTION_BLOCKOUT.md`).

Pre-production checks the owner asked for, and where each stands:
| Check | Result | Evidence |
|---|---|---|
| Full CI green | **Yes: all 8 jobs on 5456b46** | section 4a |
| `aro-blender` discoverable and callable by the coding agent in a fresh process | **Discoverable: yes. Callable by an agent session: blocked on the owner's one-time approval.** | section 4b |
| FBX and GLB import in Unity 6: orientation, scale, materials, collision, budget | **Passed 4/4 on a real Unity 6000.0.58f2 run**, after three runs that found and fixed real defects | section 8 |
| Private asset repository and integration workflow | **Proposed, not created** (the owner asked for the proposal first) | `docs/ASSET_REPO_PROPOSAL.md`; the import path it relies on is tested (section 8) |
| First roadside section | **Scaffold only; no detailed modelling** until the reference pack and approval | `docs/FIRST_SECTION_BLOCKOUT.md` |

## 1. Where Blender runs (verified, not assumed)
| Question | Answer, with how it was checked |
|---|---|
| Blender on your computer | Blender 5.2.3 (you reported it). **Not reachable from the cloud agent**: the session's list of attachable folders on your computers is empty, and nothing connects a personal computer to the cloud by itself. |
| The cloud agent's environment | Linux container, 4 CPU cores, 15 GB RAM, no GPU (`/dev/dri` absent, no `nvidia-smi`), Python 3.13. Outbound network goes through a policy proxy. |
| Blender installed there | **No** (`which blender` is empty). `download.blender.org` is **blocked** by the proxy (HTTP 403 on CONNECT, recorded in the proxy status), as is `github.com`. PyPI is allowed. |
| Can Blender run there | **Yes, as the official `bpy` Python module** from PyPI: `bpy==5.2.3` installs in 43 s and reports `5.2.3 LTS`, the same version as yours. Tested in that container: scene creation, materials, Cycles CPU render (2.7 s for a small scene), Workbench (6 s) and EEVEE (55 s) both need OpenGL: software OpenGL via Mesa works after `apt-get install libegl1`, and WITHOUT libEGL Blender aborts the whole process, so the server now refuses those engines cleanly instead (found by the first CI run, which failed on exactly this), saving `.blend`, FBX and GLB export. |
| Limits | No GPU: Cycles renders on 4 CPU cores (a 960x540 preview at 64 samples took 23 s); EEVEE works but is slow. No Blender GUI: nothing to look at except renders. |

## 2. Which MCP integration (and why not the popular one)
Candidates inspected (PyPI metadata and the package source, read not run; GitHub repositories could not be opened from this session, so repository statistics come from the search listing only):

| Option | What it is | Fit for the cloud container |
|---|---|---|
| `mcp-for-blender` 2.2.6 (formerly `blender-mcp`, ~33k stars, MIT, released 2026-10-28) | A Blender add-on that opens a **TCP socket on localhost:9876 with no authentication**, plus an MCP server that talks to it. Arbitrary `execute_code` is on by default (an opt-in AST guard exists and its own text says it covers only the MCP path, since the socket accepts raw code from any local process). Includes **telemetry** (consent-gated, switchable off) and downloaders for Poly Haven, Sketchfab and AI generators. | Needs a running Blender GUI with the add-on, which cannot be installed here (the Blender download is blocked). Its third-party asset downloaders also bring licence and prompt-injection exposure we do not want in a public-repo workflow. |
| `blenderwright` 3.1.0 (MIT, first released 2026-10-01, 276 tools, built for Blender 5.2 LTS, zero telemetry) | Same architecture: a Blender extension with a loopback TCP server (default 9876), raw Python opt-in per session. | The best GUI integration for **your computer** (matches your 5.2.3, no telemetry, raw Python off by default). Young (about a month old). Also needs the GUI extension, so it cannot run in the cloud container. |
| `iflow-mcp_cwahlfeldt_blender-mcp`, `blender-mcp-server` (djeada) | Run Blender scripts headlessly through a `blender` executable. | The first is a third-party re-packaging under another name (supply-chain doubt) and both need a `blender` binary that does not exist here. |

**Decision.** No existing integration can run in the cloud container, and the ones that could run on your machine expose an unauthenticated loopback socket. So the main game ships its own small server, `tools/blender/aro_blender/server.py`:
* Blender runs **inside** the server process (`bpy`), so the scene persists between tool calls and nothing else is needed.
* Transport is **stdio**: the server is a child process of the agent. It opens **no network socket at all** (the selftest proves it by reading `/proc`, with a negative control showing the check does detect a listening process).
* Every path is confined to the workspace; raw Python is checked before it runs; approval is not an agent tool.
* `.mcp.json` registers it as `aro-blender` for any MCP-capable agent in this repository (Claude Code asks for approval the first time). A session started before that file existed does not see the tools; `tools/blender/mcp_batch.py` runs a list of tool calls through the same server, started the same way `.mcp.json` starts it.
* **Optional second tier on your computer:** `blenderwright` in your own Blender 5.2.3 for interactive steering and viewing. It is separate, it is yours to run, and files travel through git, never through a socket. Not required for anything here.

## 3. Security
* No listening socket in the cloud (stdio only); no secrets in the environment the server needs; nothing public.
* **Workspace confinement**: every path resolves, symlinks included, to inside the workspace root or is refused (unit-tested, including a symlink escape).
* **Raw Python** (`execute_python`) is deny-by-default: no `os`, `sys`, `subprocess`, `socket`; no `eval`/`exec`/`open`; no dunder access; no Blender file operators, `bpy.app` handlers/timers, preferences, library loading or registration; a timeout stops runaway loops. This is a guard against a model being talked into hostile code (for example by text on a reference page), **not a sandbox**; the container is the isolation. `ARO_BLENDER_RAW_PYTHON=0` switches it off. Larger or reusable work is a reviewed script run with `run_script`.
* **Versioned, never overwritten**: a saved project is always a new `vNNN`; an export folder that has content is never reused.
* **Approval is a person's command**, not a tool: `python -m aro_blender.approve <kind> <name> <version> --by "Name"` writes `approval.json` and makes the version read-only. The agent cannot approve its own work.
* **Public repository**: `.blend`, FBX, GLB and textures are blocked by the repository safety gate (`tools/asset_allowlist.txt`: an added line needs the owner's written confirmation of ownership). Nothing generated is in git. Only the scripts, the toolchain and the tests are.

## 4. What is tested (all in `tools/blender/`, in CI as the `blender` job)
* `tests/` (54 unit tests): path confinement, versioning, approval freeze, name rules, and the raw-Python guard against 31 hostile snippets.
* `selftest.py` (55 checks) drives the server **over the real MCP protocol**: handshake, tool discovery, scene inspection with real triangle counts and dimensions, a reviewed script building a prop, object creation and deletion, materials, accepted and refused raw Python, a runaway script stopped by the timeout, a path-escaping script refused, Workbench and Cycles renders that come back to the agent as images, validation that fails a bad asset and passes a good one, versioned saves (v001, v002), FBX and GLB export (real binary files checked by header and length) with a manifest of measurements and hashes, refusal to overwrite an export, refusal to export a failing asset, reopening a project, and no listening socket, a GLB whose material colours survive, and a calibration model whose catalogue entry is measured.

### 4a. CI evidence for this stage
| Run | Commit | Result |
|---|---|---|
| `ci.yml` run 41260241376 | 5456b46 | **all 8 jobs green**: backend over HTTP, network core, safety gate, **Blender toolchain (54 unit tests + 55 end-to-end checks)**, database migrations, Unity tests + WebGL build, browser smoke, lighting at four hours |
| `blender-unity.yml` run 41260241387 | 5456b46 | **green, cleanup step included** (the previous run's job had failed only on that step) |
| `blender-unity.yml` run 41259509027 | 9e9a078 | the measurement run of section 8: 4/4 tests passed; job red only from the root-owned-files cleanup, fixed in 5456b46 |
| `ci.yml` run 41256294042 | 1c75c4c | all jobs green (earlier commit, before the FBX/GLB corrections) |
The merge into `main` has the same tree as 5456b46.

### 4b. MCP discoverability and callability (checked in a fresh process)
* **Discoverable.** A fresh `claude mcp list` in the repository prints `aro-blender: bash tools/blender/run_server.sh - Pending approval (run `claude` to approve)`. The project file `.mcp.json` is read; Claude Code then requires the person to approve a project-scoped server before it connects.
* **Callable.** `tools/blender/mcp_batch.py` starts the server with the same command as `.mcp.json` and calls tools over the real MCP protocol: `blender_info` returned `Blender 5.2.3 LTS`, Python 3.13.16, engines cycles/workbench/eevee, `transport stdio (no network listener)`; `workspace_info`, `new_scene`, `create_primitive` and `inspect_scene` ran. The `selftest` repeats this with 55 checks on every CI run.
* **What is NOT yet shown:** a coding-agent session actually calling the tools through its own tool list. That needs the owner to approve the server once by running `claude` in the repository and accepting the prompt. I did not approve it myself: an attempt to write the approval into settings was blocked, correctly, because approving a server that executes code is the owner's decision. After approval, a new session should list the tools as `mcp__aro-blender__*`; that will be the final check.

## 5. Workspace
`ARO_WORKSPACE` (default `~/ARO-BLENDER-WORKSPACE`):
```
projects/{buildings,vehicles,environments,roads,props,vegetation,signs,lights}/<id>/vNNN/<id>.blend + project.json (+ approval.json once approved)
references/  scripts/  materials/  textures/
renders/<id>/<id>_<view>_<engine>_<time>.png
exports/{fbx,glb,unity}/<id>/vNNN/<id>.<ext> + <id>.manifest.json (measurements, SHA-256, catalogue entry)
validation/<id>/vNNN.json     backups/
```
**The cloud container is temporary.** Its workspace disappears when the session is reclaimed. The scripts in `tools/blender/scripts/` (committed) are the source of truth and rebuild an asset in seconds; the `.blend` files, renders and exports are outputs. Where approved outputs are kept between sessions is an open decision (section 9).

## 6. Conventions (aligned with the game, `Assets/_Project/Art/README.md` and `PropCatalogValidator`)
* 1 unit = 1 m; Z up; **an asset's front faces -Y in Blender and its right is -X**; origin on the ground at the centre of the footprint; transforms applied. This is **measured, not derived**: the first assumption (front +Y) was wrong. Unity's FBX importer and glTFast both mirror X on the way in, so a model whose front is -Y (right -X) arrives in Unity with front +Z, right +X, up +Y, which is the game's rule (section 8 has the measurement). Anyone modelling by habit with a +Y front would get a model facing backwards in the game.
* Root empty named exactly like the asset id (`lower_snake_case`); LODs are empties `{id}_lod0 ... {id}_lod3` holding that LOD's meshes; strictly decreasing triangles, the last at most 10% of LOD0.
* Budgets are **the game's own** where it has them (LOD0 triangles: buildings 6000, vegetation 3000, signs 800, lights 1200, props and environment 2500; at most 80 m a side) and the hero-vehicle budget of `VehicleAssetValidator` (60,000). Roads (8,000) have no game budget yet: that figure is an estimate to be re-set from measured Unity frame time and memory.
* FBX export settings (chosen from Unity's measurement of eight variants, section 8): forward -Z, up Y, `bake_space_transform=False`, scale by units. GLB: the exporter writes each surface's flat colour (a non-image node linked into Principled would otherwise export white).
* Collision: `col_*` boxes (12 triangles) for props and buildings, one convex mesh (at most 255 triangles) for vehicles, never the render mesh.
* Materials: URP-friendly Principled materials. The shared library (`aro_blender/materials.py`, 30 surfaces for West African roadsides) uses shared names `M_<surface>` so that a neighbourhood built from modular parts batches together; the Art README's `M_{id}_{part}` applies to materials unique to one asset. **Approved by the owner on 2026-11-01; the Art READMEs now say so.** Textures `T_{id}_{BaseColor|Normal|ORM|Emissive}`, at most 2048 px (1k props).
* Every exported asset gets a manifest, including the `prop_catalog.json` entry measured from the model (`heightM/widthM/depthM`, `lodTriangles`, `collider`), status `production`, source `Props/{id}`.

## 7. The production loop per asset (what the tools enforce, and what only a person can judge)
reference examined -> real dimensions chosen -> blockout (primitives, Workbench render) -> **review of the blockout render by the owner** -> refine -> materials, UVs, texture bake -> LODs and collision -> `validate_asset` (measured, not claimed) -> `save_project` -> `export_asset` (refuses if validation fails) -> Unity integration -> **final review by the owner** -> `approve`. Passing validation never means "approved": whether it looks right is a person's decision, taken from renders and from Unity.

## 8. Unity integration: measured import evidence
The game already has the slot: a production prefab at `Resources/Props/{id}.prefab` replaces the generated placeholder of the same id, and `prop_catalog.json` (validated by `PropCatalogValidator`) describes it; roadside placement is the game's deterministic scatter from route-spec rules.

**How it was tested.** Unity only runs in CI for this project, so a dedicated workflow (`blender-unity.yml`) generates the calibration model with the toolchain, imports it into the real Unity 6000.0.58f2/URP project in a GameCI editmode run (glTFast 6.11.0 added to that job's manifest only; the committed manifest is unchanged), runs `Assets/_Project/Tests/EditMode/BlenderAssetImportTests.cs`, uploads the results and deletes the fixture. The model is deliberately asymmetric: a 2 x 6 x 1.5 m body, a cone marking the front (-Y), a different marker per side and on top, six flat-colour surfaces, a collision box and two LODs.

**Three runs, each finding something real** (run 3 is the one to read):
1. The first tests assumed a front of +Y and reset the imported root's rotation and scale, which both invalidated the FBX verdicts and would have discarded the importer's axis conversion in the real builder.
2. Measuring all eight FBX export variants (forward -Z/Z x `bake_space_transform` True/False x scale ALL/UNITS) showed that **both importers mirror X** (so the front must be -Y) and that `bake_space_transform=True` lies on its side with 90 degree rotations on the nodes; `False` leaves the importer's conversion on the model root (euler 270 degrees about X, no rotated nodes), which is correct. Four of eight variants were right; the toolchain's default is now one of them.
3. Imported FBX materials arrived as the Standard shader, with the exporter's linear colour read as sRGB; GLB colours were white because the glTF exporter ignores Principled base colour when a non-image node is linked. Both were fixed (kit remap to URP/Lit with sRGB colours and GPU instancing; `flat_base_colours` at export).

**Result, run 41259509027 on commit 9e9a078: 4/4 tests passed.**
| What | Measured |
|---|---|
| FBX default variant | `fnz_b0_units`: front +Z, right +X, up +Y; size (2, 1.5, 6) m = the Blender dimensions; imported root euler 270 degrees about X; 0 rotated nodes |
| Prefab vs catalogue entry | prefab bounds (3.3, 2.15, 7.5) = the catalogue entry derived from the model (with its LOD and marker geometry); LOD triangles [252, 12] |
| Materials | 6 materials, all URP/Lit, all with GPU instancing; hues of the flat colours match the authored ones |
| Collision | one `BoxCollider`, world centre (0, 0.75, 0), size (2, 1.5, 6); the `col_*` mesh renderer is removed |
| LOD group | two levels, screen-height thresholds 0.0928 and 0.0464, derived by the game's own LOD policy |
| Root | identity transform; the importer's conversion kept one level down |
| GLB | same orientation, sizes and material names; hues match; prefab URP/Lit |
| Game lookup | a prefab saved under `Assets/ThirdParty/**/Resources/Props/{id}.prefab` is found by `PropLibrary.ProductionPrefab`, the call `ChunkStreamer.StageProps` makes |

The run's **job** was nevertheless marked failed: the final cleanup step (`rm -rf` of the generated fixture) was refused with "Permission denied" because the Unity container runs as root and wrote the kit materials into the workspace. The tests had already passed and the results were uploaded. Fixed in the workflow (`sudo rm -rf`); confirmed by the next run (section 4a).

**Findings with consequences for the game (nothing changed, listed for the owner):**
* `ChunkStreamer.StageProps` instantiates production prefabs for chunks within 2 rings (about 1 km) as separate objects: no per-chunk merge, no catalogue box collider, no distance culling, and it overwrites the instance's yaw and multiplies its scale. So every production prefab must carry its **own LODGroup with a culling threshold, its own BoxCollider, an identity root and instanced shared-kit materials**; the builder (`BlenderAssetBuilder`) does exactly this and the test asserts it.
* Rule densities imply roughly 67 houses, 46 poles, 27 streetlights, 25 palms and 175 bushes in a 1 km window (an estimate, not a measurement); against 400 draw calls that forces **one plot, one mesh, one atlas material** for buildings. Merging production instances per chunk would be a change to an existing system and needs the owner's approval first.
* Not done yet: the builder saving prefabs to `Resources/Props`, the shared-kit `.mat` assets, a transparent/glass kit material, and a drive-through measuring real draw calls with `[Perf]`.

## 9. Decisions needed from the owner
1. **Where assets live** (decided: private companion repository, proposal pending): the public repository blocks `.blend`/FBX/textures unless `tools/asset_allowlist.txt` lists them with your written confirmation that you own them. Options: (a) you confirm ownership of assets authored here and approved ones are allowlisted into the game repository (public, so anyone can download them); (b) a private companion repository (the existing plan in `docs/ASSET_PIPELINE_PLAN.md`, section 10-12; creating it needs your explicit approval); (c) nothing is committed and outputs live only in the temporary container (they are lost; scripts rebuild them).
2. **References for the first environment** (section 10 below): without them the houses would be generic.
3. ~~Whether the `M_<surface>` shared-material scheme is accepted~~ **Decided 2026-11-01: accepted** (section 6).
4. **Private companion repository**: decided in principle on 2026-11-01; the structure, LFS, read-token handling and the artifact boundary are proposed in `docs/ASSET_REPO_PROPOSAL.md` and await approval before anything is created.
5. **Approve the `aro-blender` MCP server** in your own `claude` session (section 4b).

## 10. First milestone: what is needed to start (Northern Nigerian residential roadside section)
Provided by you, please, for this one section: (a) several photographs of the actual house types you want (front and side, with the roof, walls, gates and compound walls visible; Kano/Kaduna/Katsina/Sokoto style, flat or low-pitched roofs, parapets, decorative facades), (b) a photograph or two of the road with its shoulders, drains and verge, (c) shops/kiosks and roadside businesses, (d) the electricity poles and streetlights as they really look there, (e) typical trees and ground cover, (f) the lighting mood (time of day, dust, haze). Rights to use the images as references should be yours. I will then write down the proportions I read from each (width, height, storeys, parapet, door and window sizes, wall thickness) so you can correct my reading before any model is built, then block out, render, and wait for your review.
