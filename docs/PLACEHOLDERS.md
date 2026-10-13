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
| P-56 | **Store** screen: the sidebar says "coming soon". (Friends, Settings and Leaderboards are built, see below)  | The screens built to the mockups | B |
| P-57 | The **pause menu** is still the old plain text panel (`UIKit`), not a designed page. (Settings, Friends, Leaderboards, Profile, Onboarding and Bus Routes are designed pages now) | A designed pause menu | B |
| P-58 | **Tolls** (shown in the Jobs mockup) do not exist in the economy; the cell shows XP instead | A toll mechanic charged through the ledger | C |
| P-59 | **Companies**, **touch HUD**, the **Flutter shell**: not started | As designed | C |
| P-60 | **Push notifications** (the bell polls every 30 seconds); **player search** beyond the exact handle | Push, search | C |
| P-61 | **No Supabase-side scheduler** (sweeping is lazy; purge needs a cron). Leaderboards are index queries, fine to tens of thousands of players | Cron jobs; materialised boards when needed | C |
| P-62 | **Google / Apple sign-in** buttons need the providers enabled in the Supabase project | Providers configured and tested end to end | B |
| P-63 | Client-reported distance and position can still be forged within plausible bounds (the server samples telemetry; stronger hardening is planned) | Server-side sampling from Netcode, anti-cheat tuning | B |
| P-64 | **Settings rows with no game system behind them** (shown disabled with a SOON chip): Game Mode, Weather System, In-Game Hints, Navigation Assistance, Automatic Parking, Language (English only), Textures, Reflections, Brightness / Contrast / Saturation, Online Notifications, Chat Messages, Profanity Filter, Environment / Radio / UI / Voice volume, Brake and Acceleration Sensitivity, Invert Y-Axis, Force Feedback | Each system built, then its row switched to Live in `SettingsCatalog` (a test fails if a Soon row is already read by the game) | B |
| P-65 | **Key rebinding** (the Controls tab lists the bindings read-only); **Change Password** and **Delete Account** on the Account tab | An input-rebinding layer; auth flows; an account deletion service with data erasure | B |
| P-66 | **Settings live per device only** (browser storage): graphics, sound and controls do not follow the player to another device. Only the two privacy items are on the account | A `player_settings` table and sync, with the device value as the fallback | C |
| P-67 | **Graphics Quality and Traffic Density apply on the next load** (the world is built once); there are no per-effect graphics controls (shadow quality, texture quality, view distance, reflections) | A world that can rebuild or re-tier live; individual levers in `QualityTier` exposed | C |
| P-68 | The **Settings page has never been seen rendered** (written without a Unity editor); the browser E2E screenshots are its first look. **Display Mode** (full screen) is untested in a real browser window | A look at the screenshots and a real-browser pass | A |
| P-69 | **Messaging between friends**: the Message button on Friends is shown disabled with the reason; the game has convoy chat only | A direct-message system (table, RPCs with rate limit, moderation, report/block, notifications) and a conversation UI | B |
| P-70 | **Player avatars are initials on a colour** (the same colour for the same player). No player photographs or avatar artwork exist, `avatar_id` is stored but unused | Avatar art (or an editor) and a real picture per player; thumbnails served small | B |
| P-71 | **Friends: no mutual-friends count on requests, no "convoys joined" stat, no list/grid toggle, no search-by-name on the server** (the server answers an exact username on purpose, so players cannot be enumerated) | Backend fields or a deliberate decision on discovery: a mutual count, a convoys-joined counter, a privacy-safe name search | C |
| P-72 | **Friends and every other page are not responsive** (see P-17): the stage scales, there is no tablet or phone layout | A narrow-screen shell (bottom bar / drawer) and per-page reflow | A |
| P-73 | **Leaderboards: only the boards the backend can rank**: XP, Earnings, Distance, Jobs, All Time and This Week (This Week not for XP: no weekly XP record exists, the page says so and shows All Time). **No Country / Region / City boards, no months, no seasons, no multiplayer or vehicle-class boards** | Backend ledgers and fields for each; then a tab each |
| P-74 | **Leaderboards: no rank movement arrows** (no rank history is stored), **no vehicle on the podium or rows**, **no "points to next rank"** | A daily rank snapshot table; show the vehicle only where privacy allows (as Friends does) |
| P-75 | **Leaderboards: This Week is computed from the ledgers on each request** (transactions, completed jobs); fine for the current player count, to be materialised at scale (see P-61). The friends board and search read at most 101 friends / 25 results | Materialised weekly boards |
| P-76 | **Leaderboards: search matches names and usernames of players who already have a score**, 2+ characters, 10 results (server-limited), no pagination of results | Search paging if needed |
| P-77 | **Leaderboards page was written without a Unity editor** and is verified by the CI browser E2E and its screenshots only; the empty-global-board state is not reachable in the E2E (other players always exist) | Seeing it rendered; an isolated-database E2E |
| P-78 | **Keyboard focus and screen-reader support are not built on any page** (the UI is drawn with its own click targets; Enter and Escape work only where noted) | A focus model for the stage UI |
| P-79 | **Languages are saved, not translated**: the player picks a language at onboarding or on the profile and the server stores it, but the game text is English only (`languages.ui_ready` is true for English alone and the screens say so). Several of the languages are written in Latin script only (Arabic and Amharic are listed by their English names) because the game font has no other scripts | Translated string tables per language and fonts for the other scripts; then `ui_ready` per language |
| P-80 | **Onboarding asks three questions only** (driver name, country + language, how to start, with speed unit and Players Nearby): no age, no avatar choice (avatars are initials, P-70), no controls or graphics tutorial, no e-mail verification step | A short driving tutorial and avatar picker once those exist |
| P-81 | **The country and language suggested at onboarding come from the browser's language setting** (`navigator.language`), nothing else: no IP lookup, so a VPN or an English browser in another country gives a different suggestion (or none). It is only a starting point | An optional, consented geolocation hint |
| P-82 | **Profile: no avatar upload or avatar art, no banner, no bio, no title or badges** other than the real achievements; "How others see me" is the public card Friends shows. Achievements are listed from the server with progress; there are no achievement icons | Avatar and badge art; profile fields the backend would have to add |
| P-83 | **Onboarding and Profile pages were written without a Unity editor** and are verified by the CI browser E2E and its screenshots only (the same as P-68 and P-77) | Seeing them rendered |
| P-84 | **Bus Routes shows only the routes the server has**: the database seeds ONE route (LAG-R1, three stops in Lagos). The design mockups showed four routes including intercity ones (Lagos to Ibadan, Abuja to Kaduna...); those do not exist and no route type (city / intercity / regional) exists in the data, so the page shows the route's city instead | More routes and route types in the database (a content task), then the page lists them with no code change |
| P-85 | **Bus Routes: one stock bus photograph for every route** (no per-route photography); the player's own bus picture is the catalogue picture. No route distance, duration or "started 1 h ago" is shown because the server does not store them | Route art; run start time and distance in the backend |
| P-86 | **Demand level (Low / Medium / High) is a plain reading of the server's per-stop numbers** (average of the stops where people board: 40 or more High, 20 or more Medium); the thresholds are in `BusRouteRules` and are not a server value | A server-side demand rating if one is wanted |
| P-87 | **A bus run in progress is remembered by the running game only**: after a reload the server still has the run active but the client does not pick it up (the page offers Start and the server answers "run active"). Same limitation as before this page | A "my active bus run" read on the server and restoring the run on sign-in |
| P-88 | **Bus Routes page was written without a Unity editor** and is verified by the CI browser E2E and its screenshots only (same as P-68, P-77, P-83) | Seeing it rendered |
| P-89 | **National flags: only the 55 African states + Western Sahara**, drawn from the stored ISO-2 `country_code` through one mapping (`ARO.NetCore.CountryFlags`) and one renderer (`Ui.Flag` / `CountryTag` / `TextWithCountry`). A code with no artwork, a malformed code or no country draws a neutral slate box with a globe. Flags come from flag-icons (MIT) as 96x72 PNGs (`Resources/Flags/SOURCE.md`); they were verified in the CI screenshots, not in a Unity editor | Add a country's code to `CountryFlags.Codes`, run `tools/gen_flags.py --src <flag-icons>`; the Players Nearby, convoy-member and server-list screens do not show country at all (P-91) | Done (see Done list); non-African flags are not shipped |
| P-90 | **Players who played before onboarding have no country** (`profiles.country_code` was added nullable by `…10000003` and `…12000006` marks them onboarded). Nothing guesses it (no IP lookup, no back-fill). They see a neutral placeholder and a clear, non-blocking path: **Add your country** on the Profile chip and preference row and on Leaderboards > Your Rank, which opens the profile's own country picker | A one-time, dismissible prompt on the home screen for players with no country | Partly done: path exists, no prompt |
| P-91 | **Players Nearby rows carry no country**: `list_nearby_players` (`…11000003`) returns name, handle, avatar, level, vehicle, friend status and convoy only. Adding `country_code` means replacing that function, which is outside the privacy fix and would also need a client change | `country_code` in each nearby item and a country label on the row | Open |
| P-92 | **The Driver cabin camera (view 2) is unavailable.** The camera slot, the 8-view selection and the rig contract exist (`ARO.Vehicles.DriverRig`: cabin camera anchor, driver model, steering wheel, animator), but no vehicle has a driver model, a rig or steering animations. Choosing view 2 (key 2, or by cycling) does nothing except show a message and log `[Camera] unavailable`; next / previous skip it. The steering animation feed (the wheel angle into the animator) is NOT written: it belongs to the asset work | A driver model with a skeleton, a steering wheel the hands follow, a steering/idle animator, a `DriverRig` on the vehicle prefab with every slot filled (`DriverRig.IsComplete`); then the view becomes available by itself and sits on `cabinCameraAnchor` | `VehicleRig.driverRig` -> `DriverRig`; per-category placeholder offsets in `CameraProfiles` are only the slot's defaults | Driver model, rig and animations (P-01 models first) | A |
| P-93 | **The Cockpit camera (view 1) is a limited first-person view**: it sits at the driver's eye position of the placeholder cab and sees the road through an empty cab. No dashboard, steering wheel, mirrors, interior or windscreen exist (P-05), so it also has no wipers or rain on the glass. It follows the vehicle rigidly: no head movement or road cues (a later cockpit-motion milestone) | A cabin interior with dashboard, wheel, mirrors and glass, and `VehicleRig.cockpitCameraAnchor` set on the prefab; the camera then uses the anchor instead of the placeholder offset | `VehicleRig.cockpitCameraAnchor` (the camera already prefers it; so do `bumperCameraAnchor` and `driverRig.cabinCameraAnchor`) | Interior models (P-05) | A |
| P-94 | **The camera views were written without a Unity editor** and are verified by the xunit tests (selection, numbers, obstruction maths), the CI Unity EditMode tests and the CI browser drive test only. Offsets are tuned by geometry for the placeholder bodies (`TruckFactory`), not by looking: how the views look and feel, and whether the placeholder cab hides the chase view, are unverified until someone plays it | A manual pass in the running game (`docs/CAMERA.md`, Manual validation) and per-model `cameraFollowScale` / `cameraHeightScale` once production models exist | `CameraProfiles` (numbers), `VehicleDefinition.cameraFollowScale/HeightScale` | Seeing it run | B |

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
