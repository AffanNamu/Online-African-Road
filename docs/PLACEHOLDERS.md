# Placeholder register: everything that is not real yet

This is the list of every part of African Roads Online that is a placeholder, a stand-in, authored-for-now data, an unbuilt system or something that has never been seen running. **All of it is going to be made real.** Nothing here is hidden in the product: where a player can see one of these, the screen says so ("Placeholder model", "Coming soon", "Not available yet", "n/a").

How to use this file
* Each item has an id (`P-xx`). Commits that replace one should name the id and move the row to **Done** at the bottom, with the commit and the evidence.
* "Real means" is the acceptance test: when it is true, the item is done. "Swap point" is where the real thing goes in without touching other code.
* **Blocked on** says what only the owner can supply (art, models, audio, accounts, decisions). Everything else is engineering that can be done in this repository.
* Priority: **A** = the player sees it on the main path (vehicles, garage, world), **B** = needed for launch, **C** = later.
* The standing rules still apply: no fake data presented as real, no primitive placeholder art shipped as final, honest verification (written, built, run, tested, fixed, verified).

---

## 1. Vehicles (3D)

| Id | Placeholder today | Real means | Swap point | Blocked on | Prio |
|---|---|---|---|---|---|
| P-01 | **Every drivable vehicle is built from primitives**: two cubes and four cylinders (`ARO.Vehicles.TruckFactory`), used for the player, convoy avatars (`NetworkVehicle`) and traffic. Flat colour, no glass, no interior | Production models with a `VehicleRig` (separate wheels, steering, doors, mirrors, light meshes, 3+ LODs, PBR textures, collision, interior) | `Assets/_Project/Resources/Vehicles/{vehicle_definition_id}.prefab` or `VehicleDefinition.visualPrefab`. `TruckFactory` already prefers the prefab. See `docs/ASSET_PIPELINE.md`, `docs/VISUAL_STANDARD.md` | Models (commission or purchase; repository must be private or assets in a private submodule first) | A |
| P-02 | **The Garage 3D showroom shows a placeholder model** built from primitives by `ShowroomFactory.BuildPlaceholder` (box cargo body, cab, windshield, grille, bumper, mirrors, exhaust, wheels with rims, accent stripes). The chip on screen says "Placeholder model" | The same prefab as P-01. The showroom takes its paint renderers, light emitters, wheel visuals, interior anchor and LOD groups from the model, so nothing in the Garage changes | Same prefab slot. `ShowroomModel` (`isPlaceholder = false` is set automatically for a prefab) | Same as P-01 | A |
| P-03 | **The ARO Motors line-up has no models**: TITAN 480, HAULER 620, RUNNER 300, CITYLINER 45, URBAN 32, TRANSIT 16, ANCORA, NOMAD, CITYGO, CARGO VAN, RIDE 150 exist only as catalogue rows (`availability = 'coming_soon'`) with photographs. They cannot be bought, owned, driven or shown in the Garage | For each: a model prefab (P-01) + a `VehicleDefinition` asset with physics stats (`stats` jsonb, gears, mass, brakes, steering) + `availability = 'available'` (new migration) | `vehicle_definitions` row, `Resources/Vehicles/{id}.prefab`, `GameBootstrap.vehicleDefinitions` (the ScriptableObject) | Models, then physics tuning by test drives | A |
| P-04 | **The three drivable vehicles are not ARO line-up models**: `truck_light_01` (Savanna 4x2 Light Truck), `truck_medium_01` (Harmattan 6x2 Medium Truck), `bus_city_01` (Danfo City Bus) keep their old names; their catalogue pictures are nearest-class cards from the Trucks & Buses mockup (about 100 px, the softest pictures in the game) | Either retire them for line-up models or give them their own photographs and rename deliberately (a migration, plus the tests and E2E that name them) | `vehicle_definitions.picture_key` -> `veh_{key}*.jpg` | Photographs or renders of those exact models | B |
| P-05 | **No interior**: the Interior button is disabled for every model (`ShowroomModel.interiorAnchor` is null). The TITAN 480 sheet has a cabin photograph (`veh_aro_titan_480_int.jpg`) but it is only shown on Trucks & Buses, not in 3D | An interior mesh and a camera anchor (`VehicleRig.cockpitCameraAnchor`); the Garage then moves the camera inside and allows looking around | `VehicleRig.cockpitCameraAnchor` | Interior models | A |
| P-06 | **No LODs** on any vehicle. The showroom has the hook (`ShowroomModel.ForceLod`, lower LOD far away) but nothing to switch | LOD0-LOD3 per vehicle, cross-fade, budgets per quality tier | `LODGroup` on the prefab | Models | B |
| P-07 | **No component data**: part picking names the part (Cabin, Wheels, Headlights...) but there is one condition value per vehicle; no tyre, brake or engine condition | A component model on the server (per-part wear), shown on pick | New tables and RPCs; `ShowroomPartInfo` | Design decision + backend work | C |
| P-08 | Vehicle audio: **one placeholder engine loop** (`EngineAudio`); no tyres, horns, indicators, ambience | Layered engine by RPM, tyres, horn, indicators, wind, rain | `EngineAudio` | Audio assets | B |

## 2. Garage and customisation

| Id | Placeholder today | Real means | Blocked on | Prio |
|---|---|---|---|---|
| P-10 | **Wheels, Tires, Accessories, Body Parts, Lights, Interior, Decals** are listed in the Garage with "not available yet" (`GarageRules.CustomSlots`). No items exist | Server catalogues for each slot (items, price, level, compatibility per vehicle), ownership, equip, and the 3D model changing (wheel model, tyre, accessory attach points, light config, decal layers). Gameplay effects (grip, braking, consumption) only once those systems exist | Models with attach points (P-01) + design of the slot data | A |
| P-11 | **Upgrades** (Engine, Transmission, Brakes, Suspension, Fuel system, Cooling, Tires, Aerodynamics) and **Performance** are listed as "not available yet"; no upgrade, price or effect exists | Server-authoritative upgrade levels per vehicle with cost, required level and effect that the physics really uses; the panel shows Current / Upgrade / Cost / Effect | Economy and tuning decisions | B |
| P-12 | **Service** button is disabled (no service system); **engine / tyre / brake condition** are not tracked | A service action with a price and a result, and per-component condition (P-07) | Design | C |
| P-13 | **Paint skins are flat colours**: a skin is a base colour, an accent colour, metallic and smoothness applied through property blocks. The "ARO Original" skin is the model's own colours. The six paid skins cost 15,000 (the mockup's price). The colour values, metallic/roughness and prices are authored for now | Real material/livery sets per vehicle (textures, decals, liveries), final names, prices and levels | Livery art + the production models | B |
| P-14 | **Custom skins / My Skins livery editor**: the "Custom" filter is empty and says so | A livery editor (colours, decals, text) saving a player-made skin with moderation | Design | C |
| P-15 | The Garage look (lighting, floor, wall, lamps, ARO logo) was written without a Unity editor and has not been seen rendered; the room is primitives and emissive bars. The "reflective floor" is a smooth dark disc, not a reflection probe | A look-dev pass against the mockup: reflection probe or planar reflection, baked or light-probe lighting, an ARO Motors sign with text, props (tool boards, tyres, lifts) | A rendered review by the owner | A |
| P-16 | **Touch input** for the showroom is implemented (one finger orbits, two fingers pinch and pan) but never tried on a device | Tested on phone and tablet browsers | A device | B |
| P-17 | **Responsive layouts**: every dashboard page is laid out once on a 1920x1080 stage that scales to the window. There are no separate tablet or phone layouts (drawer for stats and customisation, stacked sections) | Real layouts for 1024x768, 768x1024 and 390x844 | Design for narrow screens | B |
| P-18 | Garage "Handling / Braking / Fuel efficiency" bars: **not shown**, because no such values exist | Values in the vehicle definition (server) that the physics uses; then the bars | Physics tuning | B |

## 3. Trucks & Buses (catalogue)

| Id | Placeholder today | Real means | Blocked on | Prio |
|---|---|---|---|---|
| P-20 | **Authored numbers on the line-up**: price, top speed, fuel tank and cargo/passenger capacity of the 11 coming-soon models are design values, not measured (brand-sheet values are hp, torque, engine, transmission, seats, gross weight, features). Several models have no hp/torque/drive at all (shown "n/a") | Final specification per model from the vehicle design, tested in the physics | Vehicle design | B |
| P-21 | **Photographs are crops of the supplied mockups** (`tools/crop_vehicle_art.py`): about 190-300 px wide per vehicle, upscaled. The TITAN 480 is sharper (640 px) | Final renders or photographs of each model at 1920 px (hero) and 600 px (card), plus front / side / rear / 3-4 views | Final renders (can come from the models in P-01) | A |
| P-22 | **The Trucks & Buses showcase is a photograph**, not 3D. The "360 degree View" button explains it arrives with the models; Exterior/Interior only switch photographs | Reuse the Garage showroom stage inside the showcase, or link both to it | P-01 | A |
| P-23 | **Licence requirement** is not shown: there is no licence system | A licence system (classes, tests or levels, server-validated) and a required licence per vehicle and job | Design | C |
| P-24 | **Job compatibility** is the category rule only (`required_category = vehicle category`); weight, cargo type and trailer compatibility are not checked by `accept_job` | Per-vehicle compatible cargo types and capacity checks, server-side, with the explanation text | Design | B |
| P-25 | **Cars, vans and motorcycles have no gameplay.** They are catalogued for racing and challenges that do not exist | A racing / challenge mode that uses the player's owned light vehicle (rules, tracks, matchmaking, rewards, anti-cheat), and driving models for them | Design + models | B |
| P-26 | Compare Vehicles omits acceleration, handling and fuel efficiency (no data) | The values exist in the definition (P-18), then they appear | Physics tuning | B |
| P-27 | One copy per model per player (a deliberate rule today). A fleet of several identical trucks is not possible | A decision: keep, or allow several with per-copy state | Owner decision | C |

## 4. Pictures and branding (UI art)

| Id | Placeholder today | Real means | Swap point | Blocked on | Prio |
|---|---|---|---|---|---|
| P-30 | **Cargo pictures** (`job_*.jpg`) are crops of the Jobs mockup, about 190 px wide, upscaled; the hero is the same picture over a blurred backdrop. `job_vehicles.jpg` (car carrier) has no source: it shows an ARO tractor-trailer | Final photographs at 1600 px per cargo type; a real car-carrier picture | `Assets/_Project/Resources/UI/job_{key}.jpg` and `job_{key}_hero.jpg` | Photographs | A |
| P-31 | **Convoy list pictures, the Multiplayer banner and the Home "Your Truck" photo** are crops of the mockups (`convoy_a|b|c.jpg`, `mp_banner.jpg`, `truck_photo.jpg`). The Home truck card shows an ARO-style tractor photo whatever vehicle the player has | Per-vehicle thumbnails from the real models; per-convoy imagery | `Resources/UI/*` | Models / renders | A |
| P-32 | **`login_bg.jpg`, `logo_mark.png`, `icon_*.png`, `feat_*.png`, `ic_*.png`, `avatar_default.png`, `truck_thumb.png`** are generated by code (`tools/gen_login_bg.py`, `gen_ui_icons.py`, `gen_dash_art.py`). The sunset login picture was waiting for a second image | Hand-made art of the same names | `Resources/UI/` (names in `docs/UI_ASSETS.md`) | The second sunset image, brand icons | A |
| P-33 | **The crops come from AI mockups supplied for design reference** and are committed to a **public** repository | A decision on provenance and licensing: replace with owned renders, or keep with the owner's confirmation | The repository privacy decision (`docs/ASSET_PIPELINE.md`) | Owner decision | A |
| P-34 | **Avatars** are initials on a coloured disc; the player avatar is a generic silhouette | Avatar art and an avatar picker (ids already exist in the profile) | Art | B |
| P-35 | **Home hero slides and news pictures** reuse the highway key art and the world map artwork | Per-slide art delivered by the content service | `content_items.image_key` | Art | B |
| P-36 | The **world map artwork** is the supplied stylised picture, not to scale; city markers are anchored by hand | Optionally a real cartographic base; the anchors stay data-driven | `docs/WORLD_MAP.md` | Optional | C |

## 5. The 3D world (driving)

| Id | Placeholder today | Real means | Blocked on | Prio |
|---|---|---|---|---|
| P-40 | **Road**: a generated spline ribbon with procedural tiling textures (`WorldMaterials`, `ProcTex`), markings, guardrails | PBR road kit, junctions, ramps, bridges, signs, real camber and surface variation | Road kit and textures | A |
| P-41 | **Terrain**: generated height tiles with procedural patchy textures | Terrain from public elevation data with real material blending, water | Elevation data, textures | A |
| P-42 | **Props are all dev placeholders** (cubes and cylinders, flagged `dev` in the catalog; a test requires the flag) | Production prefabs per prop id (`Resources/Props/{id}.prefab`) with LODs; catalog entries set to `production` | Vegetation, building, signage and settlement packs | A |
| P-43 | **Sky and light**: Unity procedural sky, one sun, flat ambient, no post-processing stack beyond the basics, no reflection probes | Full look-dev: HDRIs, tonemapping, probes, haze, wet roads, rain / fog / dust | HDRIs, tuning | A |
| P-44 | **Traffic** is cubes driven by the tested IDM model; no junctions or lights; mix of vehicle types absent | Traffic vehicles (danfo, motorcycles, tricycles, saloons, tankers...) with LOD tiers and junction behaviour | Traffic models | B |
| P-45 | **Only Lagos - Ibadan is playable**; the other 11 map routes are `planned`. The seeded jobs are all inside Lagos (both ends in one city), with generated cargo | Real routes (Lagos - Abuja, Abuja - Kaduna, Port Harcourt - Enugu, Kano - Maiduguri...), jobs between cities, route-specific content | Route data, world art | A |
| P-46 | **Route unlock levels** and world-state statuses are design placeholders | Final progression design | Owner decision | B |
| P-47 | No NPCs, animals, pedestrians, interiors of buildings | As needed | Assets | C |
| P-48 | **Performance has never been measured on real devices** (CI renders in software); budgets in `docs/VISUAL_DIRECTION.md` are targets | Frame-time, memory and download measurements on desktop and mobile browsers; quality tiers tuned | Devices | A |

## 6. Systems that are not built

| Id | Not built | Real means | Prio |
|---|---|---|---|
| P-50 | **Voice chat**: the states and the `IVoiceProvider` seam exist; no provider, so every voice state reads "Not available" | A voice provider integrated, per-member state, mute, speaking | B |
| P-51 | **Global chat** (closed: needs moderators); only convoy chat exists (with report, block, auto-hide) | A moderated global channel | C |
| P-52 | **Shared jobs**: the server gives a bonus per extra participant; nothing coordinates a shared job and the Create dialog option is disabled | Coordinated start, shared progress, split rewards | B |
| P-53 | **Several game servers / regions**: one server row; region and capacity appear only if an operator sets them | More rows, region routing, ping per server | C |
| P-54 | **Nearby players have no distance** (positions are never stored server-side) and **only your own ping** is measurable | A decision on position telemetry or a relay-side measure | C |
| P-55 | **Daily challenges**: schema, progress and claim are built; no definitions, no screen | Authored challenges and a screen | B |
| P-56 | **Friends, Leaderboards, Store** screens: the backends exist; the sidebar says "coming soon" | The screens built to the mockups | B |
| P-57 | **Settings** and the **bus routes**, **profile** and **pause menu** are the old plain text panels (`UIKit`), not designed pages | Designed pages to the same standard as Jobs, Multiplayer, Trucks & Buses, Garage | B |
| P-58 | **Tolls** (shown in the Jobs mockup) do not exist in the economy; the cell shows XP instead | A toll mechanic charged through the ledger | C |
| P-59 | **Companies**, **touch HUD**, the **Flutter shell**: not started | As designed | C |
| P-60 | **Push notifications** (the bell polls every 30 seconds); **player search** beyond the exact handle | Push, search | C |
| P-61 | **No Supabase-side scheduler** (sweeping is lazy; purge needs a cron). Leaderboards are index queries, fine to tens of thousands of players | Cron jobs; materialised boards when needed | C |
| P-62 | **Google / Apple sign-in** buttons need the providers enabled in the Supabase project | Providers configured and tested end to end | B |
| P-63 | Client-reported distance and position can still be forged within plausible bounds (the server samples telemetry; stronger hardening is planned) | Server-side sampling from Netcode, anti-cheat tuning | B |

## 7. Authored data that is a stand-in

* **Job generator**: seeded cargo and jobs are generated; the cargo catalogue (descriptions, fragile / special handling) is authored, not researched. Reward, XP, distance and estimates are the server's numbers but come from placeholder tuning (`game_config`: fuel price, average speed).
* **Fuel price** `fuel_price_per_l = 2`, **repair price** `10 per percent of damage`: tuning placeholders.
* **Skin prices and colours**, **line-up prices and levels** (P-13, P-20).
* **Hero and news content** (authored seed content; labelled "authored").
* **World map unlock levels** (P-46).

## 8. Never seen running, or never seen rendered (verification debt)

These are written and compile, but there is no evidence they work end to end. Each needs a real run and a human look:

* The Garage page and the 3D showroom (look, lighting, input) and the Trucks & Buses page (rendered review of the real photographs).
* The shared world: Unity Multiplayer Services and Relay, avatars, two or more real clients, the in-session convoy panel, a failed live-session start in a real project. CI has no Multiplayer Services project.
* Driving beyond the smoke scenario; the bus run path against a live backend; job acceptance from the map against the live backend.
* Touch devices; real GPUs; frame rate and memory on real hardware.
* Everything the CI "browser end to end" step covers but that has only just started to pass (see `docs/STATUS.md` for the current evidence).

## 9. Things that are test scaffolding, not product (they are not placeholders and must stay out of the UI)

* `?smoke=drive`, `?smoke=worldmap` (fixture player and jobs, `GameFlow.LoadFixture`), `?fault=...` request fault injection, `?backend=` and `?autologin=` overrides: honoured only on loopback pages (`BackendOverride`), never reachable from the UI.
* `tools/e2e` auth shim, the PostgREST test harness, `supabase/tests/00_auth_stub.sql`.
* `tools/gen_*.py` generators (their output is the placeholder art above) and `tools/crop_*.py` (they cut pictures out of the supplied mockups, which are not stored in the repository).

## 10. Done

| Id | Commit | Evidence |
|---|---|---|
| _(nothing yet)_ | | |
