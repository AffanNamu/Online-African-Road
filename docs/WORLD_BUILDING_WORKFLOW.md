# African Roads Online: visual reference to 3D production workflow

**Standing process (owner's directive, 2026-11-01).** The owner generates visual references (with ChatGPT) continuously; the main game agent turns each into genuine 3D assets, keeps them in one permanent library, brings them into the real Unity game, and proves the result in the running game. Kano is the first city, not the only one: Lagos, Ibadan, Abuja, Kaduna, Zaria and others follow, so everything is built to be reused or deliberately not reused.

## 0. Rules that never relax
1. **The supplied images are the visual direction.** An image is never placed on a plane, used as a texture, or replaced by a planning diagram and called built. Geometry, proportions, composition, materials and details are read from it and rebuilt in Blender.
2. **A change to an approved design is explained first.** If a reference cannot be reproduced (engine limit, budget, rights, a factual error in it), the difference is reported and the asset is improved; a lower standard is never accepted silently.
3. **Distinguish what a reference is.** Every element is exactly one of: a *visual design requirement*, a *verified geographical fact*, a *fictional architectural concept*, or an *actual landmark* (needs accurate references). Generated text, distances, names, logos and landmarks are not facts.
4. **Three different proofs, never confused:** *Blender validation* (geometry, budgets, names, UVs, collision, LODs: the toolchain's validator), *Unity validation* (the files import, the project compiles, prefabs and materials build and pass tests), *in-game verification* (the running game shows it, compared with the reference). A render is not a Unity asset; an import is not an in-game result; neither is an approval.
5. **The existing game is preserved.** Gameplay, multiplayer, vehicles, networking, the road model and the scatter are not changed by asset work; what a design needs from the engine is a proposal (`ROAD_ENGINE_PROPOSAL.md`).
6. **Licensing is recorded for everything.** Original geometry says so; any third-party content or tool is named with its licence in `LICENSES.md` of the asset repository before the asset is used. Reference images are not redistributed and never become textures.

## 1. The nine steps for every new reference
| # | Step | Output | Gate |
|---|---|---|---|
| 1 | **Register the reference** | an entry in `reference_register.json` (`R-0xx`: label, SHA-256, kind, what it shows, the four-way classification of its elements, flags such as real organisations or wrong facts); the image itself stays private | the owner's approval of the interpretation |
| 2 | **Identify assets and components** | the reference-to-asset matrix entry: visual requirements, reference camera, comparison checklist, the asset list | the owner approves the breakdown |
| 3 | **Classify reuse** | each asset `GLOBAL`, `REGIONAL`, `CITY_SPECIFIC` or `LANDMARK`; reused assets are looked up in the registry first | no duplicate of an approved asset |
| 4 | **Model in Blender** | blockout, then detail, materials, UVs, LODs, collision; code in `tools/blender/kano_kit`-style builders so an asset is rebuilt in seconds | blockout compared with the reference at its camera |
| 5 | **Validate geometry and performance** | the toolchain validator report (triangles per LOD, size, pivot, collision, UVs, names) plus the winding check | `blender_validation_passed` only if it passes |
| 6 | **Export and preserve** | versioned `.blend`, FBX and GLB with textures, manifest with SHA-256 and measurements, preview renders from the relevant cameras; versions are append-only | in the private asset repository, with Git LFS |
| 7 | **Import into Unity** (once approved for integration) | prefab (identity root, LODGroup, BoxCollider, kit materials with textures, cutout, transparency, normal maps), catalogue entry | the project compiles; the Unity tests pass |
| 8 | **Verify in the running game** | screenshots and the game's own `[Perf]` numbers from the review scene (`?smoke=kanostreet`), compared with the reference; deviations listed with causes | the owner reviews; critical mismatches block the next asset |
| 9 | **Update the registry** | status, paths, validation and review records, flags for open differences | the registry validator passes; no status without its evidence |

## 2. What every asset must have (and where it lives in `ARO-CONTENT`)
| Required | Registry field / location |
|---|---|
| unique asset ID (`aro_`, `rg_<region>_`, `<city>_`, `lmk_<city>_`) | `id`; class and prefix are validated together |
| original Blender source file | `blenderSource`: `blender/projects/<kind>/<id>/vNNN/<id>.blend` |
| FBX or GLB export as appropriate | `export`: `unity/Models/<id>/vNNN/` (FBX default, GLB alongside) |
| required textures and materials | `unity/Textures/`, `unity/Materials/aro_materials.json` (the shared `M_<surface>` kit); `textures` and `materials` listed in the registry |
| preview renders from relevant camera angles | `previews`: `previews/<set>/vNNN/` (reference camera, front, side, street level, plan as relevant) |
| dimensions and polygon information | `dimensions` (m) and `lodTriangles` from the validator |
| collision information | `collision` (`box` with size, `none`, or `mesh` where the catalogue allows) |
| LOD information | `lods` (levels and triangle counts) |
| Unity import and compatibility status | `unity`: `{imported, compiled, prefab, inGameVerified}` with dates and the run that proved each |
| licensing and ownership | `licence`: kind, evidence, third-party content named; and a row in `LICENSES.md` |
| reuse classification | `assetClass` and, for `REGIONAL`, `region`; for `CITY_SPECIFIC` and `LANDMARK`, `city`; `approvedCities` |

## 3. Reuse and identity
* **Look in the registry first.** Reusable systems are built once and varied by data: walls, gates, windows, doors, roofs, streetlights, road markings, sidewalks, kerbs, electricity poles, signs, vegetation. A variation is a parameter (proportions, material, colour, ornament), not a new model.
* **`GLOBAL`** assets are neutral infrastructure usable in most cities. **`REGIONAL`** assets belong to an architectural or geographical family (for example Northern Nigerian Hausa architecture, `rg_ngn_`). **`CITY_SPECIFIC`** assets give one city its identity. **`LANDMARK`** assets are real, recognisable places and need accurate references; they are never placed in another city.
* **Cities must not look identical.** Distinctive architecture (the Kano compound, a Lagos high-rise, a Abuja government building) is not reused across cities; neutral infrastructure is. Where two cities share a system, they differ in materials, colours, dimensions and arrangement.
* **Registry first for every new city.** Moving to Lagos, Ibadan, Abuja, Kaduna or Zaria starts with the registry: reuse what is `GLOBAL` or `REGIONAL`, create only what is missing, and register new references for what is city-specific.

## 4. The fidelity gate (step 8 in detail)
1. Build the review scene in the real game and take the views the reference calls for from the **reference camera** and the others in the matrix.
2. Put each beside the reference and the Blender render of the same camera.
3. List every significant difference with its cause: geometry, material, lighting, engine, or budget.
4. Correct and rebuild (a new asset version; an approved version is never overwritten) until no critical mismatch remains. The owner decides when a result is acceptable; passing checks never approves an asset.
5. Do not start the next asset family or the next district while a critical mismatch is open.

## 5. Status vocabulary (an asset moves one step at a time)
`needs_concept`, `concept_approved`, `blockout_completed`, `detailed_modelling_completed`, `materials_completed`, `blender_validation_passed`, `unity_import_verified`, `in_game_visual_review_passed`, `production_approved`. The registry validator refuses a status whose evidence is missing; `production_approved` needs a person.

## 6. Where things are
| What | Where |
|---|---|
| the game, the Blender builders (`tools/blender/kano_kit`), the Unity builder and review scene | `AffanNamu/Online-African-Road` (public, code only; branch `work/kano-residential` until reviewed) |
| sources, exports, textures, previews, registry, licences | `AffanNamu/ARO-CONTENT` (private; Git LFS) |
| the private Unity review build and its private screenshots | `ARO-CONTENT` workflow `unity-review.yml` (needs the owner's Unity secrets; see that repository's `docs/UNITY_REVIEW_SETUP.md`) |
| decisions and flags | `docs/kano/DECISIONS_*.md`, the reference register |
| the existing in-game map and how assets fit it | `docs/KANO_EXISTING_MAP.md` |

## 7. What every report to the owner says
What was imported; what compiled; what was tested in the running game; what failed; the performance numbers (from the game's own probe, not estimates); what still differs from the reference. Blender validation, Unity validation and in-game verification are reported separately.
