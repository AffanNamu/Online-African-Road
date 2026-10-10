# Visual direction and gap analysis

Status: written 2026-10-10 after the owner rejected the current 3D visuals. This document replaces "CI is green" as the definition of progress for anything the player can see.

**Decision: all 3D world / vehicle / prop work built from primitives is stopped.** Nothing new is added to `TruckFactory` placeholders, `ChunkStreamer` cube props or the flat ground plane. The 2D UI (login, dashboard) is scaffolding that waits for the owner's key art; it is not the visual foundation.

A visual milestone is only "done" when the owner has looked at rendered screenshots against the reference images and approved them. CI passing, a test passing or a screenshot existing is not approval.

## 1. What is unacceptable today (evidence: CI screenshots, 2026-10-10)

| Area | What the build actually shows |
|---|---|
| Vehicles | The player truck is two cubes and four cylinders, flat yellow/brown. Tail lights were magenta (a Standard-shader material in a URP player build). The ambient traffic uses the same box mesh. |
| Road | One 14 m ribbon with a flat colour. The mesh has UVs for markings but no road texture exists, so there are no lane lines, edges, shoulders, kerbs, barriers, potholes or camber. |
| Terrain | One flat plane per 200 m tile in a single green. No elevation, no soil/grass variation, no water. |
| Buildings | Stretched grey/brown cubes 6 to 20 m from the road edge. No doors, windows, roofs, signage or African architecture. |
| Vegetation | None. |
| Props | None: no signs, street lights, poles, bridges, fuel stations, terminals, markets, pedestrians or animals. |
| Sky and light | Unity's default procedural sky, one directional light, no post-processing, no ambient occlusion, no reflection probes, no haze, no baked GI. |
| Traffic | The pooled model exists, but what it draws is cubes. The road reads as empty. |
| Audio | A placeholder engine loop only. |

This is a systems test scene. It proves physics, streaming, networking and the backend work. It proves nothing about the product's look.

## 2. What must be replaced
Every 3D visual: vehicle meshes and materials, the road mesh and material, the terrain, all roadside content, the sky/lighting rig, traffic visuals, wheel/light/mirror models, and audio assets.

## 3. What can be retained (engineering, not visuals)
- Backend: server-authoritative economy, telemetry validation, bus system, RLS/tests (verified).
- Network layer, convoy sessions, `NetworkVehicle`, the pure-C# logic in `NetCore` (traffic IDM model, HUD math, level math) with its tests.
- `VehicleController` physics shell and the `VehicleRig` contract (wheel colliders, steering, lights, centre of mass). Production models plug into this.
- `ChunkStreamer`'s pattern (route-driven sectors, frame-budgeted pump, pooling). The content it generates is replaced.
- `TimeOfDay` / `WeatherSystem` scaffolding (systems retained, visuals rebuilt).
- CI, the browser drive test, and the screenshot branch (these are how rendered output gets reviewed).

## 4. What is needed (asset categories)
Hero trucks, buses and minibuses, traffic cars, motorcycles/tricycles, road kit (lanes, junctions, ramps, bridges, barriers, signs, lights, poles), terrain/ground materials, vegetation, buildings and shops, fuel stations / terminals / markets, NPCs and animals, sky/HDRI/weather VFX, and audio (engines, tyres, horns, ambience, weather).

## 5. What I can realistically build (code and tooling, no art)
- Rendering foundation: linear colour, URP quality tiers, post-processing stack, physically-motivated day/night lighting rig (sun, ambient, fog/haze, tonemapping), reflection-probe setup, wet-road shader, rain/fog/dust systems.
- Spline road generator: cross-sections with lanes, shoulders, kerbs, camber, junction/ramp pieces, markings in the shader, guardrails and poles extruded along the spline. Takes the owner's textures and meshes.
- Corridor terrain generator: height tiles from public elevation data, road carving, material blending, water bodies.
- Scatter/instancing system for vegetation and props with zone rules and LODs (uses supplied meshes).
- Traffic system (AI, lanes, overtaking, junctions, pooling, LOD tiers) on top of the tested IDM model.
- Vehicle integration pipeline: rig mapping, LOD setup, light/emissive wiring, damage, cockpit camera.
- Addressables sector streaming, memory/frame budgets, an in-game performance HUD and a scripted fly-through benchmark.
- Editor tooling: URP material conversion, LOD generation checks, prefab validation.

## 6. What must be sourced manually (or commissioned)
Everything with artistic content: models, textures, animation, audio, and the African-specific content. Marketplace coverage of West African vehicles and architecture is thin, so expect to commission part of it.

## 7. Unity asset pipeline (recommended)
- Unity 6000.0 LTS, URP (the project is already on 6000.0.58f2). HDRP-only packs are not usable.
- Every purchased or commissioned asset lives under `Assets/ThirdParty/<vendor>/`, imported unmodified, with a thin `Assets/_Project/` wrapper (prefab variants, materials converted to project URP shaders, LOD groups, rig components). Vendor updates then never overwrite our changes.
- Textures: author at 2k (hero) / 1k (props), PBR metal-smoothness, ASTC for mobile and DXT for desktop builds; texture arrays for terrain/road materials.
- Meshes: LOD0-LOD3 per prop and vehicle, cross-fade, lightmap UVs only where baked.
- Content ships through Addressables, one group per sector and per vehicle, so the initial download stays small and the rest streams.

## 8. Environment architecture: a corridor world, not an open world
The game is route-based, so build a corridor of 1 to 2 km around each route rather than an open map.
- Routes are splines (Unity Splines package) with real geometry derived from public data, sectioned into about 500 m sectors.
- Each sector: height tile (road carved in), road mesh from the road kit, roadside dressing from zone presets (urban Lagos, peri-urban, savannah, rainforest, harmattan), hand-placed set pieces (terminals, fuel stations, markets, bridges).
- Far field: baked ridge/skyline meshes and layered sky/haze for depth; these are cheap and carry most of the "large world" feeling.
- Streaming: load 1.5 to 2 km ahead along the route and unload behind. Physics colliders only near the player.
- Avoid DOTS for the web target: WebGL has no practical multithreading and limited DOTS rendering support.

## 9. Traffic architecture
- Ambient traffic is local and deterministic (seed from sector and time of day), so convoy partners see consistent traffic without network cost. Only player vehicles are networked.
- Lane-following on route splines: IDM for following (already implemented and tested), MOBIL-style lane change for overtaking, junction priority and traffic-light state machines, oncoming traffic.
- Three LOD tiers per traffic vehicle: near (full model, animated wheels, simplified collision), mid (reduced model, no animation), far (lights/impostor only). Pools per vehicle type; density curves by zone and time of day; hard caps by device tier.
- Vehicle mix per zone: danfo-style minibuses, motorcycles/tricycles, saloons, SUVs, tankers, container trucks, long-distance buses.

## 10. Performance strategy
Starting targets, to be validated on real devices (not on CI, which renders in software):

| Tier | Visible triangles | Draw calls | Shadows | Near traffic | Textures in memory |
|---|---|---|---|---|---|
| Desktop browser (WebGL2) | 0.6 to 1.0 M | under 400 | 1 sun, 2 cascades to about 120 m | about 20 | under about 300 MB |
| Mobile | 150 to 250 k | under 150 | one short cascade or blob | about 8 | under about 150 MB |

Techniques: LOD0-3 with cross-fade, impostors for distant trees, SRP Batcher and GPU instancing, texture arrays, mesh and texture compression, pooling for traffic and props, sector streaming with a per-frame time budget, baked lighting only for static set pieces, realtime sun with light probes for the day/night cycle. A scripted fly-through that logs frame time, draw calls and memory runs on target devices and gates every milestone.

WebGL notes: WebGL2 only (no compute shaders), no practical multithreading, plan for roughly 1 GB of heap on desktop browsers and much less on mobile Safari, keep the initial download small and stream the rest.

## 11. Upgrade order
0. Repository and licence hygiene (below). Primitives frozen.
1. Rendering foundation and look-dev scene (I build; needs a few free CC0 HDRIs and one or two real assets to judge light against).
2. Hero vehicles: 1 truck, 1 bus, then 6 traffic vehicles (needs assets). I integrate rigs, LODs, lights, damage, cockpit.
3. Road system and corridor terrain; first real 20 km of Lagos to Ibadan.
4. Vegetation, props and settlement kits with scatter and instancing (needs assets).
5. Traffic system running real vehicles.
6. Weather, wet roads, day/night, audio (needs audio assets).
7. NPCs, animals, interiors.
8. Device profiling, Addressables/CDN streaming, quality tiers.
9. Next routes (Lagos to Abuja, Abuja to Kaduna, Port Harcourt to Enugu, Kano to Maiduguri).

What I can start before any assets arrive: steps 1 (systems), 3 (generators), 5 (AI), and the pipeline/tooling/benchmark in section 5. Blocked on assets: everything the player actually sees in steps 2, 4, 6 and 7.

## Manual acquisition plan
Verify on every listing before buying: Unity 6 / URP support, WebGL notes, mobile suitability, and a licence that allows use in a commercial game. Prices and availability change; treat names as starting points, not endorsements.

| Priority | Need | Where to look | Format and requirements | Licence note |
|---|---|---|---|---|
| Essential | Hero trucks (container truck, tanker, flatbed) | Unity Asset Store, TurboSquid, CGTrader, or commission | FBX or prefab with separate wheels, steering, doors/mirrors, light meshes, 3+ LODs, PBR textures | Check "game use"; raw files must not be redistributed |
| Essential | City bus / danfo minibus / long-distance coach | Same, or commission (West African models are rare) | Same | Same |
| Essential | 6+ traffic vehicles (saloons, SUV, pickup, motorbike, tricycle) | Asset Store vehicle packs | Low-poly LODs suitable for pooling | Same |
| Essential | Road kit (lanes, junctions, ramps, bridges, guardrails, signs, poles, lights) | Asset Store road kits or a road-system package such as EasyRoads3D or Road Architect | URP-compatible; I can build the generator if only meshes and textures are supplied | Check redistribution |
| Essential | Ground / asphalt / soil PBR textures | Poly Haven, ambientCG | 2k PBR sets | Both are CC0 (free, commercial use) |
| Essential | Sky HDRIs (dawn, noon, sunset, overcast, night) | Poly Haven | 2k to 4k HDR | CC0 |
| Essential | Tropical and savannah vegetation (palms, trees, bush, grass) with LODs and billboards | Asset Store nature packs | URP shaders, LOD groups, wind optional on web | Check |
| Essential | Buildings and shops (concrete block, zinc roofs, kiosks, markets, fuel stations, terminals) | Asset Store city/rural packs; commission West African specifics | Modular, LODs, atlas textures | Check |
| Essential | Engine, tyre, horn, ambience, rain/wind audio | Asset Store audio packs, Freesound (per-file licence), commissioned recording | WAV or OGG, loopable engine layers by RPM | Per-file licence |
| Later | Character/NPC models and animation | Asset Store characters; Mixamo animations | Rigged humanoid, 2 to 3 LODs | Check Mixamo terms |
| Later | Terrain/vegetation tooling (Gaia, MicroVerse, Vegetation Studio) | Asset Store | Only if my own scatter/terrain tooling proves insufficient | Check |
| Later | Sky/weather package (e.g. Enviro) | Asset Store | URP, WebGL support must be confirmed | Check |
| Later | Real elevation and road geometry | Copernicus/SRTM elevation; OpenStreetMap for roads | Public datasets | OSM is ODbL: attribution and share-alike apply |

**Repository warning:** `AffanNamu/Online-African-Road` is public. Asset Store licences generally forbid redistributing the source assets, and committing them to a public repo does exactly that. Before any paid asset is added, either make the repository private, or keep third-party assets in a private companion repository or submodule that the CI pulls with a token. Compiled game builds are normally permitted; raw asset files are not.
