# Blender production pipeline (African Roads Online)

ARO 3D Studio is a separate project and nothing here depends on it. This is the main game's own way of producing real 3D assets: **Blender 5.2 LTS** as the modelling environment, an **MCP server** as the agent's control interface, **Unity** as the final integration environment.

Status: the toolchain exists and is tested (section 4). **No production asset exists yet**; the only asset is a test bollard proving the chain. Unity import has not been run (section 8).

## 1. Where Blender runs (verified, not assumed)
| Question | Answer, with how it was checked |
|---|---|
| Blender on your computer | Blender 5.2.3 (you reported it). **Not reachable from the cloud agent**: the session's list of attachable folders on your computers is empty, and nothing connects a personal computer to the cloud by itself. |
| The cloud agent's environment | Linux container, 4 CPU cores, 15 GB RAM, no GPU (`/dev/dri` absent, no `nvidia-smi`), Python 3.13. Outbound network goes through a policy proxy. |
| Blender installed there | **No** (`which blender` is empty). `download.blender.org` is **blocked** by the proxy (HTTP 403 on CONNECT, recorded in the proxy status), as is `github.com`. PyPI is allowed. |
| Can Blender run there | **Yes, as the official `bpy` Python module** from PyPI: `bpy==5.2.3` installs in 43 s and reports `5.2.3 LTS`, the same version as yours. Tested in that container: scene creation, materials, Cycles CPU render (2.7 s for a small scene), Workbench (6 s) and EEVEE (55 s, software OpenGL via Mesa after `apt-get install libegl1`), saving `.blend`, FBX and GLB export. |
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
* `tests/` (51 unit tests): path confinement, versioning, approval freeze, name rules, and the raw-Python guard against 31 hostile snippets.
* `selftest.py` (49 checks) drives the server **over the real MCP protocol**: handshake, tool discovery, scene inspection with real triangle counts and dimensions, a reviewed script building a prop, object creation and deletion, materials, accepted and refused raw Python, a runaway script stopped by the timeout, a path-escaping script refused, Workbench and Cycles renders that come back to the agent as images, validation that fails a bad asset and passes a good one, versioned saves (v001, v002), FBX and GLB export (real binary files checked by header and length) with a manifest of measurements and hashes, refusal to overwrite an export, refusal to export a failing asset, reopening a project, and no listening socket.

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
* 1 unit = 1 m; Z up; **front faces +Y in Blender**, right is +X; origin on the ground at the centre of the footprint; transforms applied. The game's rule is "+Z forward, +Y up, +X right" in Unity, which is exactly what Blender's +Y/+Z/+X becomes with the FBX/glTF exporters' conversion (forward -Z, up Y). This is derived, **not yet confirmed**: the first Unity import test will confirm it with a deliberately asymmetric test object.
* Root empty named exactly like the asset id (`lower_snake_case`); LODs are empties `{id}_lod0 ... {id}_lod3` holding that LOD's meshes; strictly decreasing triangles, the last at most 10% of LOD0.
* Budgets are **the game's own** where it has them (LOD0 triangles: buildings 6000, vegetation 3000, signs 800, lights 1200, props and environment 2500; at most 80 m a side) and the hero-vehicle budget of `VehicleAssetValidator` (60,000). Roads (8,000) have no game budget yet: that figure is an estimate to be re-set from measured Unity frame time and memory.
* Collision: `col_*` boxes (12 triangles) for props and buildings, one convex mesh (at most 255 triangles) for vehicles, never the render mesh.
* Materials: URP-friendly Principled materials. The shared library (`aro_blender/materials.py`, 30 surfaces for West African roadsides) uses shared names `M_<surface>` so that a neighbourhood built from modular parts batches together; the Art README's `M_{id}_{part}` applies to materials unique to one asset. **This is a proposed amendment to the README; say if you prefer one scheme only.** Textures `T_{id}_{BaseColor|Normal|ORM|Emissive}`, at most 2048 px (1k props).
* Every exported asset gets a manifest, including the `prop_catalog.json` entry measured from the model (`heightM/widthM/depthM`, `lodTriangles`, `collider`), status `production`, source `Props/{id}`.

## 7. The production loop per asset (what the tools enforce, and what only a person can judge)
reference examined -> real dimensions chosen -> blockout (primitives, Workbench render) -> **review of the blockout render by the owner** -> refine -> materials, UVs, texture bake -> LODs and collision -> `validate_asset` (measured, not claimed) -> `save_project` -> `export_asset` (refuses if validation fails) -> Unity integration -> **final review by the owner** -> `approve`. Passing validation never means "approved": whether it looks right is a person's decision, taken from renders and from Unity.

## 8. Unity integration plan (not started: it changes the game, so it follows the reviewed blockout)
The game already has the slot: a production prefab at `Resources/Props/{id}.prefab` replaces the generated placeholder of the same id, and `prop_catalog.json` (validated by `PropCatalogValidator`, enforced by tests) describes it; roadside placement is the game's deterministic scatter from route-spec rules. So an asset is: FBX imported, a prefab with its LOD group and box collider, a catalogue entry, a rule that places it. Steps, each verified in the real Unity project (CI is the only place Unity runs for this project):
1. **Orientation and scale calibration**: an asymmetric test object exported by the toolchain and imported by a Unity editor test that asserts the directions and size. Confirms or corrects section 6.
2. An importer/prefab builder (editor script) from FBX + manifest to prefab, LOD group, collider and catalogue entry, with a test.
3. Placement through the existing scatter rules; then a drive-through with `[Perf]` numbers, in the browser build.
Binary assets must reach the Unity project: that needs the allowlist decision below.

## 9. Decisions needed from the owner
1. **Where assets live**: the public repository blocks `.blend`/FBX/textures unless `tools/asset_allowlist.txt` lists them with your written confirmation that you own them. Options: (a) you confirm ownership of assets authored here and approved ones are allowlisted into the game repository (public, so anyone can download them); (b) a private companion repository (the existing plan in `docs/ASSET_PIPELINE_PLAN.md`, section 10-12; creating it needs your explicit approval); (c) nothing is committed and outputs live only in the temporary container (they are lost; scripts rebuild them).
2. **References for the first environment** (section 10 below): without them the houses would be generic.
3. Whether the `M_<surface>` shared-material scheme is accepted (section 6).

## 10. First milestone: what is needed to start (Northern Nigerian residential roadside section)
Provided by you, please, for this one section: (a) several photographs of the actual house types you want (front and side, with the roof, walls, gates and compound walls visible; Kano/Kaduna/Katsina/Sokoto style, flat or low-pitched roofs, parapets, decorative facades), (b) a photograph or two of the road with its shoulders, drains and verge, (c) shops/kiosks and roadside businesses, (d) the electricity poles and streetlights as they really look there, (e) typical trees and ground cover, (f) the lighting mood (time of day, dust, haze). Rights to use the images as references should be yours. I will then write down the proportions I read from each (width, height, storeys, parapet, door and window sizes, wall thickness) so you can correct my reading before any model is built, then block out, render, and wait for your review.
