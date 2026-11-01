# First environment section: Northern Nigerian residential roadside (blockout scaffold, waiting for references)

Status: **scaffold only.** The owner's instruction is explicit: no Northern Nigerian architecture is invented and no detailed modelling starts before the reference pack exists and the blockout is approved. What exists is (1) a neutral layout of grey volumes so the *composition* can be discussed, (2) the budget envelope the game's own code imposes on this section, and (3) the list of what the reference pack must show. Every dimension in the scaffold is a placeholder and is marked as one.

## 1. What the section must contain (from the owner's milestone)
A properly modelled road; residential houses; compound walls and gates; shops and roadside structures; electricity poles; streetlights; trees and vegetation; environmental props; realistic materials and lighting. One coherent section, not a city.

## 2. The neutral layout scaffold (`tools/blender/scripts/section_massing.py`)
Rendered through the MCP server (`render_preview`, Cycles): plan, isometric, and a street-level view from a caller-placed camera (`view="camera"`, `camera_location`, `look_at`, `lens_mm`: a person 8 m into the road looking along it is `[0, -8, 1.7]` -> `[0, 30, 2.4]` at 28 mm). Plain boxes and cylinders; **no style claim of any kind**. The scene is a layout diagram, not an asset (it is not validated or exported).

| Parameter (m) | Scaffold value | Status |
|---|---|---|
| Road length shown | 100 | placeholder |
| Carriageway width | 7.3 | placeholder (two 3.65 m lanes is a common figure; **to be set from the reference road**) |
| Shoulder each side | 2.0 | placeholder |
| Drain each side | 1.0 wide, 0.5 deep | placeholder |
| Verge between drain and plot | 2.5 | placeholder |
| Plot frontage x depth | 20 x 30 | placeholder |
| Compound wall height / thickness / gate gap | 2.2 / 0.25 / 4.0 | placeholder |
| House volume (W x D x H) / setback | 10 x 12 x 3.2 / 6 | placeholder |
| Shops | four 6 x 4.6 x 3.2 volumes straight onto the verge | placeholder |
| Utility poles | 9 m, every 40 m, right side (the game's own urban rule spaces them 40 to 46 m) | placeholder |
| Streetlights | 8 m, every 30 m, left side | placeholder |
| Trees | 8 at fixed positions | placeholder |

What the scaffold is for: to agree the *rhythm* (how many plots, how far houses sit back, where poles stand, how the shops meet the road) and to give the reference-reading something to correct. If the references show a different arrangement, the scaffold changes, not the references.

## 3. The budget envelope (measured from the game's code and route data, not assumed)
| Fact | Source |
|---|---|
| The world streams in 200 m chunks; production prop prefabs are instantiated for chunks within 2 rings of the player (about 1 km of road) | `ChunkStreamer.chunkSize`, `StageProps` (`if (ring <= 2)`) |
| Generated placeholders are merged into one mesh per chunk, get their box colliders from the catalogue, and are culled by distance. **Production prefabs get none of these**: each instance is a separate `GameObject`, so the prefab must bring its own `LODGroup` (with a culling threshold), its own `BoxCollider`, and cost its own draw calls | `ChunkStreamer.StageProps`, `PropBatcher` |
| Desktop budget: 1,000,000 triangles, **400 draw calls**, 300 MB texture memory, 16.7 ms; mobile (low tier): 250,000 triangles, 150 draw calls | `PerfBudget` |
| LOD0 triangle budget per prop: buildings **6,000**, vegetation 3,000, lights 1,200, signs 800, props 2,500; at most 4 LODs, strictly decreasing | `PropCatalogValidator.Lod0Budget` |
| The existing urban rule set for `ng-lagos-ibadan` places, per side: houses every 18-30 m (presence 0.8), poles every 40-46 m, streetlights every 48-56 m (0.7), palms every 30-50 m (0.5), bushes every 6-10 m (0.7) | `Resources/Routes/ng-lagos-ibadan.route.json`, `propRules` |

Consequence, as arithmetic on those rules over a 1,000 m window (both sides): about **67 houses, 46 poles, 27 streetlights, 25 palms and 175 bushes** at once. If every one of them were its own draw call that is ~340 of the 400 allowed before the terrain, road, vehicles and UI. (This is an estimate from rule densities, not a measurement of a running build; measuring it is part of the Unity drive-through.)

Production rules that follow, to be tested rather than hoped for:
1. **One plot, one prefab, one mesh, one atlas material.** The house, compound wall and gate of a plot are modelled as modular parts in Blender but exported as a single plot-level prefab (the catalogue's 6,000-triangle budget then covers the whole plot, which is tight and means texture, not geometry, carries the detail).
2. **Shared-kit materials with GPU instancing enabled** (`M_<surface>`, one `.mat` per surface in the private repository), so identical prefab instances merge into few draw calls. The prefab builder remaps imported materials to the kit by name.
3. **Low-tier fallbacks**: every building has a LOD chain whose last level is at most 10% of LOD0 and a culling distance, because the game does not cull production prefabs for us.
4. These rules assume no change to `ChunkStreamer`. If the measurement shows production instances should be merged per chunk like the placeholders, that is a change to an existing system and **needs the owner's approval** first.

## 4. How the section would enter the game (nothing done; needs approval)
New catalogue ids in `prop_catalog.json` with `status: production, source: Props/<id>` and placement rules in a route spec. Both are data. The production prefabs live in the private asset repository (`docs/ASSET_REPO_PROPOSAL.md`) and appear under `Assets/ThirdParty/` at CI/development time; a fork without them shows the generated placeholder for each id. Which route carries the section (the existing Lagos-Ibadan route, or a small dedicated test route) is the owner's decision.

## 5. What the reference pack needs to show (so nothing is invented)
For each item, enough views to read proportions: the whole front, one side, the roof edge, the door/window/gate detail, and something for scale (a person, a car, a door).
1. **Houses**: at least four distinct house types you want in the section, front and side, with roofs, parapets, wall finishes, windows, doors and any ornament; their approximate plot and setback.
2. **Compound walls and gates**: wall height and finish, gate leaves, gate piers, any wall-top treatment.
3. **Shops and roadside businesses**: the building type, the signage style, kiosks, market stalls.
4. **The road**: surface condition, shoulders, drains and culverts, kerbs if any, markings, where the road meets compound entrances.
5. **Electricity poles and streetlights** as actually installed there (material, crossarms, fittings).
6. **Trees and ground cover**: the species that actually line such roads, ground colour and dryness.
7. **Light and atmosphere**: time of day and haze/dust, because the game's lighting is tuned per hour.
For each reference I will write down the proportions and materials I read from it (width, height, storeys, parapet, door and window sizes, wall thickness, finishes) **as a table you can correct before any model exists**.

## 6. Review gates (nothing proceeds without the owner's yes)
G0 reference reading corrected by the owner -> G1 blockout renders (plan, iso, eye-level) -> **owner approves the blockout** -> G2 detail modelling of one house type first -> G3 materials, UVs, texture bake, LODs, collision -> `validate_asset` (measured) -> G4 FBX imported in Unity with the import tests, then a drive-through with `[Perf]` numbers in the browser build -> G5 owner reviews renders and the drive-through and approves (the `approve` command freezes the version).
