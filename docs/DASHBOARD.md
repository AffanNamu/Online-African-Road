# Dashboard and Jobs page

> Placeholders, stand-ins and unbuilt parts of every page are tracked in [`PLACEHOLDERS.md`](PLACEHOLDERS.md).

Both pages live in one screen (`DashboardScreen`, partial class: `DashboardScreen.cs` = shell + Home, `DashboardJobs.cs` = Jobs) on a 1920x1080 stage that
letterboxes to any window. The sidebar and header are shared; the sidebar's Jobs item (or Home's "View All", a hero button, the world map's "Jobs"
button) switches page without rebuilding the header. Escape on the Jobs page returns Home.

Every value on either page has one defined source, and a failed source shows **Unavailable + Try again**, never made-up content.

## Jobs page: where each thing on screen comes from

| On screen | Source |
|---|---|
| Rows (cargo, from -> to, distance, time, weight, reward, difficulty tag) | `list_available_jobs` (server job board; reward/XP/distance/est. minutes are the server's numbers) |
| Category chips (Freight, Construction, Fuel & Chemicals, Agriculture, Manufacturing, Vehicles) | applied **by the server** (`p_category`): the client never filters data it was not sent; totals are the server's counts |
| Search box | `p_search` (cargo, place or city; case-insensitive, `%`/`_` are literals, max 40 chars), debounced 0.45 s, one request per pause |
| "Showing N of M", infinite scroll | `total` from the server, pages of 30 |
| Hero (category, cargo name, route, difficulty, distance, time, weight, base payment) | the selected job |
| Route Preview | the supplied West Africa artwork cropped around the two cities, the validated route polyline (`world_map.json`), pins at the job's own endpoints. Both ends in one city (every job seeded today): the crop zooms to the city and the two depots are placed by their real world coordinates, captioned "Local route ... (schematic)". Fuel litres are the server's estimate |
| Cargo Information (description, weight, class, Fragile, Special Handling) | `cargo_types` catalogue (server table), joined into the job reply |
| Requirements: Minimum Level / Required Vehicle / Route Access | level: job `min_level` vs the player's level; vehicle: `requirements.has_vehicle` (server: owns a usable vehicle of that category); route: server `blocked_reason` |
| Accept Job | enabled only when the server says `eligible`; otherwise it shows the server's reason ("Requires level 5", "Route not available", ...). The click calls `accept_job` through GameFlow; a refusal re-reads the board |
| Earnings strip: Potential Earnings, Est. Fuel Cost, XP Reward, Estimated Profit | server `estimate` for this player: fuel = distance x the burn rate of the vehicle they would drive x `fuel_price_per_l` (same price `buy_fuel` charges; a test fails if they drift); profit = reward - fuel |

States: loading skeleton, empty ("No jobs match" + Clear filters / "No open jobs right now"), unavailable (message + Try again), blocked job (red reason on the row and on the button).

## Differences from the approved Jobs mockup (deliberate, and why)

| Mockup | Build | Why |
|---|---|---|
| Photographic cargo pictures and a photographic hero | **done**: real pictures cut from the approved mockups by `tools/crop_ref_art.py` (rows of the Jobs mockup for containers, cement, fuel tanker, steel pipes, food, machinery; the container hero from the Jobs hero; crated freight from the dashboard concept). The hero is the same photograph at full height over a blurred backdrop of itself. **No car-carrier picture exists in any mockup, so `vehicles` shows an ARO tractor-trailer until you supply one.** Resolution is that of the mockups (about 190x107 per cargo picture, upscaled 3x with Lanczos): drop higher-resolution files named `job_<key>.jpg` / `job_<key>_hero.jpg` in `Assets/_Project/Resources/UI/` and they replace these with no code change |
| "Est. Toll Cost" (strip) and "Toll" (route card) | "XP Reward" and the fuel litres | tolls do not exist in the game economy; showing a toll would be invented data. If tolls become a real mechanic (charged at completion through the ledger) the cell is a one-line swap |
| Requirement "Required License: Class A" | "Route Access" | no licence system exists |
| Routes between Lagos, Abeokuta, Akure, Benin City, Abuja, Kaduna... | whatever jobs the server has | only Lagos-Ibadan is OPEN in the world state; jobs for planned routes are not generated. They appear when the server opens those routes |
| Currency symbol and header balance "N 85,500" | same (naira sign), Montserrat has the glyph | - |

## Tests

* SQL: `supabase/tests/39_job_catalog.sql` (cargo details, estimates vs `buy_fuel`, filter, search incl. wildcard/injection, pagination, clamp, generator) + 21 mutants in `mutants_backend.py`.
* API contract: C# `BoardJob`/`BoardCargo`/`BoardEstimate` vs the real RPC output (`tools/check_api_contract.py`); Unity EditMode `BackendClientTests` parses the real sample replies.
* Browser (`tools/webgl-e2e.js`, real build + real PostgREST + real database): open the Jobs page, row count equals the database, category filter, empty category, typing a search, selecting rows, Accept Job creates the assignment with the server's reward.


# Multiplayer page

`DashboardMultiplayer.cs` (layout, polling), `DashboardMultiplayerActions.cs` (loading, server actions, dialogs), `DashboardMultiplayerLobby.cs` (lobby tab, nearby players,
server list, create / settings / disband / player / report dialogs). The convoy is the centre of the page. Convoy list pictures, the banner and the Your Truck card are real photographs cut from the approved mockups the same way. Flow, each step decided by the server
(`supabase/migrations/20261011000002_convoy_lobby.sql`, `...03_discovery.sql`):

```
MULTIPLAYER -> Find / Create convoy -> Select route -> Join / create lobby -> Invite players -> Ready check -> Start driving -> Shared world -> Jobs together -> Rewards / XP
```

## States are derived, never painted

`ARO.NetCore.ConvoyStates` (pure C#, unit tested) turns facts the **server** and the **live session** reported into the states the screens show.

| Convoy state | Derived from |
|---|---|
| NO_CONVOY | the server says the player is in no convoy |
| CREATING / JOINING / STARTING | a call of ours is in flight (Starting also: the convoy is driving but this player has not joined the live session) |
| SEARCHING | the convoy list is loading while the player has no convoy |
| IN_LOBBY / READY_CHECK | lobby phase; ready check once a non-leader pressed Ready |
| IN_SESSION | convoy driving **and** the live session is `InSession` |
| RECONNECTING | convoy driving and the live session is reconnecting |
| DISBANDED | a convoy that existed disappeared without the player leaving (also shown as a toast) |

| Member state | Derived from |
|---|---|
| INVITED | a pending, unexpired invite from the leader or a member |
| JOINED | in the lobby, not ready, joined less than 15 s ago (server timestamps) |
| NOT_READY / READY | the member's ready flag (the leader counts as ready) |
| DRIVING | convoy driving and the member's heartbeat is live |
| DISCONNECTED | the member's heartbeat stopped (server: live session within the TTL) |

The **Start button is disabled until the lobby's real requirements are met** (leader, route chosen, every other member ready, nobody offline) and says which one is missing;
the server checks the same rules again (`start_convoy_drive`: route, all ready, everyone online, every member's level meets the route).

## What each part of the page is made of

| On screen | Source / rule |
|---|---|
| Available Convoys (name, Open / In Progress / Full, members, route distance and time, tags, language, Join) | `list_open_convoys` (public convoys; server search and tag filter; lobbies sort before convoys on the road; `joinable` accounts for capacity and the viewer's level). A password convoy asks for the password; a wrong one is an *answer* (not an error) so the server counts every guess (8 per 10 minutes) |
| **Create Convoy** (name, route, public / invite only, 2-8 players, driving mode relaxed or realistic, required level up to the creator's own, language, up to 3 tags, optional password) | `create_convoy_lobby`: ONE atomic call, so the lobby never exists without its level limit or password. The password is stored salted and hashed and is never returned (only `has_password`) |
| Voice chat and Shared jobs options | shown **disabled** with the reason (no voice system; no job-sharing logic) |
| Convoy Lobby tab: rules, route, distance, estimated time, member table (level, truck, state, connection, voice), leader controls | `get_my_convoy` polled every 3 s |
| Leader: Start Convoy, Invite, Change Route, Settings, Kick, Make leader, **Disband** | `start_convoy_drive`, `invite_to_convoy`, `set_convoy_route`, `set_convoy_settings`, `kick_convoy_member`, `transfer_convoy_leader`, `disband_convoy` |
| Member: Ready, Leave, **Invite Friend** | `set_my_ready`, `leave_convoy`, `invite_friend_to_convoy` (friends only; public convoys; blocked players refused) |
| Connection column | the member's live-session state; for **you** it also shows your measured round trip. Other players' ping is not measurable and is not shown |
| Voice column | `IVoiceProvider`; with no provider every state is "Not available". The UI never shows "Voice Connected" unless a provider reports it |
| Players Nearby | `list_nearby_players`: players in the same **route** (else same **city**) in their own live presence. Positions never reach the database, so there is **no distance**; the panel names the scope instead. **Opt-in**: a player appears only after turning it on (onboarding or Settings; the database default is `false`, `20261012000007_nearby_opt_in`), and blocked players are excluded. Click a row: View Profile / Invite / Join Convoy / Add Friend / Block |
| Active Players, Friends | `list_active_players` (activity visible to friends only), `list_friends` |
| Server List | `list_servers`: name, region, population (live sessions), capacity, status ONLINE / FULL / MAINTENANCE; region and capacity appear only when an operator set them; **UNAVAILABLE** is what the page shows when the server cannot be reached. Ping = your measured round trip |
| Server Status | `get_server_status` |
| Convoy Chat / System | `list_convoy_messages` (first load = newest 50, then polling), `post_convoy_message`. Bodies pass `ChatText.Clean` again on the client (no markup, no control characters, bounded) |
| Chat moderation, report, block | each line carries a moderation state; `hidden` / `removed` lines show a notice, never their text. Click another player's line: **Report** (spam, abuse, harassment, other; three different reporters hide the line automatically) or **Block** (their lines disappear for you; they leave your player lists and cannot be invited by you). **Mute** is covered by Block |
| Global Chat | still **closed**: open chat with strangers needs moderators; report/block now exist, a global channel does not |

## In session

The convoy panel in the driving HUD joins the server's member list with the avatars that are really in the live session (`LiveRoster`): connected members show distance to the leader and
progress along the route (spline projection of the avatar's position); a member with no avatar shows **disconnected**; the header shows Reconnecting when the session is. No movement is simulated.
Each player's job reward is still validated by the server from their own telemetry; the session is cosmetic.

## Not built, and said so

| Item | State |
|---|---|
| Voice (CONNECTED / CONNECTING / MUTED / SPEAKING states) | the states and the `IVoiceProvider` seam exist; **no voice system is installed**, so everything reads "Not available" |
| Shared jobs option | disabled; jobs are still accepted one driver at a time (server bonus per extra participant exists, nothing coordinates it) |
| Global chat | closed (see above) |
| Several servers / regions | one server row is registered; more rows need no code |
| Distance in Players Nearby | positions are not stored server-side; scope (route / city) is shown instead |
| Per-player ping | only your own |
| Development fixtures | none exist; there is no fake data to switch on |

## Tests

* SQL `41_convoy_lobby.sql`, `42_convoy_spec.sql` (atomic create and every setting, password and its guess limit, entry level, settings rules, disband, member invites, reports and blocks, connection state, nearby players, server list) and mutation testing of both migrations (every protection has a mutant; survivors found along the way exposed two real defects that are now fixed: the nearby page limit, and a wrong password that rolled back its own attempt counter).
* xunit `ConvoyModelTests` (all convoy and member states, the start gate, voice never claiming a connection, chat sanitising and moderation display, route progress, live roster).
* API contract: 44 endpoints, C# DTOs vs the real RPC output; EditMode tests parse the real replies.
* Browser (`tools/webgl-e2e.js`, real build, real PostgREST and database): lists from the database, create (route, tag), lobby with a second player and ready flag seen by polling, chat both ways, report and block, Start with the live-session service unreachable (the convoy must return to the lobby), server list, Leave, create with rules and a password, Disband, failing list shows Unavailable.
* **Not verified anywhere:** the shared world itself (Unity Multiplayer Services / Relay, avatars, two or more real clients) and everything that happens after the live session starts, including the HUD convoy panel. CI has no Multiplayer Services project.


# Trucks & Buses page

`DashboardVehicles.cs` (page), `DashboardVehiclesActions.cs` (select, buy, compare), pure rules in `ARO.NetCore.VehicleRules` (`VehicleModel.cs`, unit tested).
Backend: `supabase/migrations/20261012000001_vehicle_catalog.sql`.

**One catalogue, many views.** `list_vehicle_catalog` returns every model once; the tabs (All, Trucks, Buses, Light Vehicles = cars, vans and motorcycles, Owned, Shop), the search and the sort are filters over that list. Nothing is duplicated per tab.

| On screen | Source / rule |
|---|---|
| Vehicle cards and the vehicle panel (name, class, drive, description, power, torque, top speed, fuel tank, cargo or seats, weight, price, level) | the model's row in `vehicle_definitions`. A value the brand sheet does not give is NULL and shows "n/a"; it is never invented. Bars are the value against the largest value in the catalogue |
| Buy / Select / Coming soon / Requires Level N / Need N more | `purchase_state`, computed **by the server** from the caller's own wallet and level (`owned`, `coming_soon`, `not_for_sale`, `level_required`, `insufficient_funds`, `available`) |
| Condition and fuel of an owned vehicle | `list_my_vehicles` (server values). Bands: condition Ready >= 70 %, Maintenance recommended 30-70 %, Repair required < 30 % (server `needs_repair` at 70 % damage), Critical <= 10 %; fuel Healthy >= 25 %, Low 10-25 %, Critical < 10 % (server `low_fuel` below 10 %) |
| "Can take N of the M open jobs" | `get_vehicle_job_fit`: the rule `accept_job` enforces (the job's required category equals the vehicle's category); a model that is not released says so |
| Compare Vehicles | 2 to 4 distinct vehicles from the same catalogue. Fields that do not apply to a kind (cargo for a bus, seats for a truck) show "n/a"; the better value is highlighted; identical values are not "better"; acceleration, handling and licence are not part of the data, so they are not shown |
| Open Garage | the existing garage (refuel, repair). Cosmetic customisation exists server-side (`set_vehicle_customization`) but has no screen yet, so the button is called Open Garage, not Customize |

**Purchase.** Buy -> confirmation (server price, balance, balance after) -> `buy_vehicle` -> the page re-reads the catalogue, the garage and the wallet. The server refuses before any money moves (unknown model, not released, not for sale, already owned, level too low) and the debit and the ownership row are one transaction. One copy of a model per player (unique index + advisory lock): a double click, two tabs or a race charge once. A failure shows "Purchase failed. Your balance has not been changed." with the server's reason.

**Selection.** Select Vehicle -> `set_active_vehicle` (ownership checked server-side) -> the garage is re-read.

**The line-up.** The three drivable vehicles (Savanna light truck, Harmattan medium truck, Danfo city bus) are `available`. The ARO Motors line-up from the brand sheets (TITAN 480, HAULER 620, RUNNER 300, CITYLINER 45, URBAN 32, TRANSIT 16, ANCORA, NOMAD, CITYGO, CARGO VAN, RIDE 150) is in the catalogue with its real specification and photographs but is `coming_soon`: **no drivable model exists for them yet, so they cannot be bought**. Hp, torque, engine, transmission, seats, gross weight and features are the brand sheet's; price, top speed, fuel tank and capacities of those models are authored game-design values that may change before release. Cars and motorcycles are catalogued and ready for the racing/challenge system; no racing exists yet.

**Pictures.** Real crops of the ARO Motors sheets (`tools/crop_vehicle_art.py`): the catalogue card, a showroom picture, the sheet's front / side / rear views for every line-up model, and for the TITAN 480 five views and the driver cabin (Interior toggle). The three drivable vehicles use the nearest-class card of the Trucks & Buses mockup (about 100 px, the softest pictures; no photograph of those exact models exists). Higher-resolution files named `veh_<picture_key>.jpg`, `_hero`, `_v1..v5`, `_int` in `Assets/_Project/Resources/UI/` replace them without code changes.

**Not built, and said so on screen.** 3D showroom (rotate, zoom, 360 degrees: the button explains it arrives with the vehicle models; `VehicleRules.Showroom` reports what exists), licence requirements (no licence system), vehicle customisation screen, condition affecting gameplay beyond what the server already does, separate phone/portrait layouts (the page scales on the 1920x1080 stage like the other pages), vehicle racing.

**Tests.** SQL `43_vehicle_catalog.sql` (catalogue shape, specs, every purchase state, purchase atomicity, one copy per model, job fit, grants, schema guards) + concurrency race 9 (double purchase) + 33 mutants; xunit `VehicleModelTests` (69 tests: tabs, search, sort, actions, condition and fuel bands, bars, capacity, comparison, job-fit sentences, showroom honesty); API contract (45 endpoints); EditMode parses the real replies; browser E2E (`tools/webgl-e2e.js` section 2c: catalogue equals the database, tabs, search, announced model disabled, cannot-afford, buy with confirmation and exact debit, select persisted, compare, garage, unavailable + retry).


# Garage page and 3D showroom

`DashboardGarage.cs` (page, My Vehicles, viewer UI, vehicle panel), `DashboardGarageWork.cs` (paint skins, maintenance, the "not available yet" panels), `ShowroomStage.cs`
(the 3D room), `Vehicles/ShowroomFactory.cs` + `ShowroomModel.cs` (the model slot), pure rules in `ARO.NetCore` `GarageModel.cs` (camera maths and garage rules: unit tested).
Backend: `supabase/migrations/20261012000002_ledger_skin_kind.sql`, `20261012000003_garage.sql`.

**The 3D showroom is real 3D**, not a picture: a small workshop is built far below the world on its own layer, a camera renders it into a RenderTexture that the page shows, and the
vehicle model is loaded into it. The reusable inspection camera (`OrbitCameraModel`) gives orbit (drag), zoom (wheel / pinch, limited to a distance range derived from the vehicle's size),
pan (right-drag / two fingers, limited), predefined views (Front, Rear, Left, Right, Top, Wheels), a turntable (360 degrees), Reset, smooth frame-rate independent interpolation and pitch limits.
It renders **on demand** (only while the camera moves or something changes), so an idle Garage costs no GPU time. While the Garage is open the world's sun and ambient are suspended and restored on exit.
Clicking a part (cabin, wheels, headlights, taillights, mirrors, exhaust, chassis, cargo body) highlights it and names it.

**The model slot.** The vehicle comes from the same slot the drivable vehicle uses: `VehicleDefinition.visualPrefab` or `Resources/Vehicles/{id}` (a prefab with a `VehicleRig`). A production prefab
supplies its own paint renderers, light emitters, wheel visuals, interior camera anchor (enables the Interior button) and LOD groups (the showroom forces a lower LOD far away). **No production
ARO model exists yet**, so the Garage shows a development placeholder built from primitives (marked "Placeholder model" on screen); dropping the real prefab into the slot replaces it with no Garage change.
The ARO TITAN 480 is not drivable yet (no model, no physics definition), so it is in the catalogue (Trucks & Buses) but cannot be owned, and therefore does not appear in the Garage.

| On screen | Source / rule |
|---|---|
| My Vehicles | `list_my_vehicles` (the player's real ownership); the chip (Active, Ready, Damaged, Maintenance, Low fuel, On a job) is derived from the server's status, selection and condition |
| Vehicle panel (Overview bars, Stats table, Info) | the model's row in `vehicle_definitions` (via `list_vehicle_catalog`); values the definition does not have are not shown. Bars are relative to the catalogue maximum. Handling, braking and fuel efficiency are **not shown**: the game has no such values |
| Condition and Fuel Level | the vehicle's live state (server) |
| Set as Active | `set_active_vehicle` (ownership validated server-side), then the page re-reads the garage |
| Paint & Skins | `vehicle_skins` (server catalogue: price, level, colours, metallic, roughness), `player_skins` (ownership), `list_vehicle_skins`, `buy_vehicle_skin` (one ledger row `SKIN_PURCHASE`, one ownership row, one transaction, a lock against double clicks), `apply_vehicle_skin` (ownership of the skin and the vehicle checked; stored in the vehicle's customization). Selecting a tile previews the colour on the 3D vehicle (not saved); Equip saves it. Free skins belong to everyone, paid ones need a purchase. The skin applies colour, metallic and smoothness through property blocks (the shared materials are never changed) |
| Maintenance | `get_vehicle_service_quote`: repair and refuel prices from the same formulas `repair_vehicle` and `buy_fuel` charge (a test compares the quote with the real charge). Repair and Refuel call those functions and the page reads the result back. **Service** is disabled (there is no service system). Engine, tyre and brake condition are not tracked |
| Customize categories | Paint works. Wheels, Tires, Accessories, Body Parts, Lights, Interior, Decals are listed with the reason ("arrive with the production vehicle models"): no fake items |
| Upgrades, Performance, Accessories sections | listed, each category marked "Not available yet": there is no upgrade, price or effect in the game, so none is invented |
| Lights button | previews the model's headlight and taillight emitters |

**Not built.** Wheel / tyre / accessory / light / decal customisation, performance upgrades, engine-tyre-brake condition, a service system, custom skins ("Custom" filter is empty and says so), the driver view, component-level
condition, and separate phone / tablet layouts (the page scales on the 1920x1080 stage like the others; the touch gestures are implemented through the pointer events: one finger orbits, two fingers pinch and pan,
but they have not been tried on a device). **Not yet seen rendered:** the showroom's lighting and look were written without a Unity editor; the CI screenshots are the first time they are seen.

**Tests.** SQL `44_garage.sql` (catalogue, states, purchase atomicity, equip, customization keeps the skin, quote equals the real charges, grants, schema guards) + concurrency race 10 + 34 mutants; xunit `GarageModelTests`
(camera limits, presets, smoothing, reset, tags, slots, skins, colours); API contract (47 endpoints); EditMode parses the real replies; browser E2E section 2d (real mouse drag, wheel, presets, reset, turntable, part pick,
skin preview / buy / equip, repair, refuel, vehicle selection, Set as Active, all checked in the database).


# Settings page

`DashboardSettings.cs` (page, rows, dropdown, slider, cards), `SettingsStore.cs` (device storage and applying), pure catalogue and rules in `ARO.NetCore` `SettingsModel.cs`
(unit tested), `Core/RuntimeSettings.cs` (what the running game reads). Reached from the sidebar, the header gear and the Account tab. Laid out from the supplied mockup: seven tabs (Game, Graphics,
Controls, Audio, Gameplay, Account, Language); the Game tab is the overview of everything in three columns, every other tab shows its own area with a side card.

**Every row is one of four honest kinds** (`SettingStatus`): **Live** (applied when the player presses Save), **OnReload** (stored at once, takes effect when the game next loads: the world is built once
per load), **Soon** (the game has no system for it: shown, disabled, with a SOON chip, and tapping the label says why) and **Fixed** (a real value that cannot change: region, distance units, currency).
Edits are a *draft*: "Save Settings" stores and applies, "Reset to Default Settings" resets the draft, leaving the page with unsaved changes discards them and says so.

| Setting | What it really does |
|---|---|
| Speed Unit | the speedometer shows km/h or mph (`Hud`) |
| Time of Day | Real Time (West Africa clock) or a fixed Morning / Midday / Evening / Night (`TimeOfDay`) |
| Traffic Density, Graphics Quality | OnReload: the traffic scale and the quality tier chosen when the world is built (`RenderingRig.Select`; a `?quality=` in the address still wins). The shared tier objects are never modified |
| Shadows, Post Processing | live: the sun's shadows and the post-processing volume (`RenderingRig`) |
| Show Minimap | the driving view's route map (`Hud`, `MinimapView`) |
| Camera Shake | scales the chase camera's shake (`FollowCamera`) |
| Display Mode | `Screen.fullScreen` when Save is pressed (the browser only allows it after a click, so it is a session choice, never replayed at start-up) |
| Show FPS Counter | a small counter bottom-right (`FpsOverlay`) |
| Show Player Names | the name above other players' vehicles (`NetworkVehicle`) |
| Master Volume, Engine Sound | `AudioListener.volume` and the engine's volume (`EngineAudio`); the engine is the only sound in the game |
| Steering Sensitivity | how quickly the steering answers keys and sticks (`KeyboardVehicleInput`); 50 % is the original feel |
| Appear in Nearby Players | **account**, OFF until the player turns it on: `set_nearby_visibility`, saved at once; the current value is read from `list_nearby_players`. It controls Players Nearby only: country, level, leaderboard rows, friends and the map do not depend on it |
| Blocked Players | **account**: `list_blocked_players` / `unblock_player` |
| Anti-Aliasing, View Distance | read-only: what the active quality tier really uses |
| Controls tab | the bindings `KeyboardVehicleInput` and `FollowCamera` read, shown read-only; a test fails if the list and the code disagree |

Device settings are stored by `SettingsStore` in PlayerPrefs (the browser's storage in WebGL) as `id=value` lines. A damaged or older file never stops the game: unknown keys, bad numbers and options that no longer exist fall back to defaults.

**Not built (shown as SOON, listed in `docs/PLACEHOLDERS.md` P-64):** Game Mode, Weather System, In-Game Hints, Navigation Assistance, Automatic Parking, Language (English only), Textures, Reflections,
Brightness / Contrast / Saturation, Online Notifications, Chat Messages, Profanity Filter, Environment / Radio / UI / Voice volume, Brake and Acceleration Sensitivity, Invert Y-Axis, Force Feedback, key rebinding,
Change Password, Delete Account. The mockup's Resolution and VSync are shown as facts (a web game follows its window and the browser), Day/Night Cycle is covered by Time of Day.

**Tests.** xunit `SettingsModelTests` (catalogue shape, validation, step snapping, corruption, round trips, effects, plus two source-reading tests: every changeable setting must be read by game code, and the
controls shown must equal the keys the input code reads); EditMode and semcheck compile the page; browser E2E section 2e (tabs, dropdown, toggles, a real mouse drag on a slider, Reset, Save, apply, other tabs, the privacy toggle and
Unblock against the database, discard on leaving, and a reload that proves the saved quality is built as the High tier).


# Friends page

`DashboardFriends.cs` (page, rows, side panels, dialogs, actions), pure rules in `ARO.NetCore` `FriendsModel.cs` (filter, sort, activity text, which actions a card offers, which rows to build; unit tested),
backend `supabase/migrations/20261012000004_person_vehicle.sql` (the vehicle on a player card), `FriendService` (+ `Find`, `PublicProfile`) in `BackendServices.cs`. Reached from the sidebar.

Nothing on the page is hard-coded: every count, name, level, online state, activity, vehicle and request state is what the server sent, and every action is a backend call followed by a fresh read.

| On screen | Source / rule |
|---|---|
| All Friends / Online | `list_friends` (a friend card: level, country, presence: online, activity, route, last seen; and, new, the **vehicle** the friend drives). Online keeps the online ones. Sorted Online First (or Name, Level, Recently Active) |
| activity line | presence `activity` (menu, driving, bus, convoy, idle) with the route name from the world map; an offline friend shows "Last seen 2 h ago" (server time, so a wrong device clock does not matter) |
| vehicle picture | the friend's `vehicle_id` looked up in the catalogue's `picture_key` (a real photograph); no vehicle id, no picture |
| Requests | `list_friend_requests`: incoming (Accept / Decline / View Profile) and sent (Cancel Request) |
| Recent Players | `list_recent_players` (met in convoys); Add Friend, or a chip: Friends, Request sent |
| Find Players | `find_player`: an **exact username** (the server does not list or prefix-search players, so nobody can be enumerated). Found, not found and error are three different states |
| search box | filters the loaded list by name or username as you type; Enter looks up that exact username |
| Add / Accept / Decline / Remove / Cancel | `send_friend_request` (a request from someone who already asked you becomes a friendship), `respond_friend_request`, `remove_friend` (also cancels your own pending request). Remove and Block ask for confirmation |
| Invite Convoy | `invite_friend_to_convoy`; enabled for an **online** friend while **you** are in a convoy, otherwise tapping says why. The server's refusals (already invited, convoy full, level too low...) are shown in words |
| View Profile | `get_public_profile` (experience, jobs, distance, achievements, member since, vehicle) in a dialog; your own card opens yours |
| side panels | your card (friends, online, jobs done, distance driven), the first three requests with Accept / Decline, the first recent players |
| refresh | the lists are read again when the page opens, every 15 s, and after every action |

**States.** Loading, empty (a different message for no friends, none online, no recent players, no match, no requests), error with *Try again* (one per list), pending ("Request Sent"), already friends (chip), success and failure toasts that carry the server's reason. A failed or in-flight action never shows as a success.
**Long lists.** Only the rows near the scroll window are built (`FriendsRules.VisibleRange`); pictures are only loaded for those rows. The backend caps a player at 100 friends, so there is no server paging to use.

**Deliberate differences from the mockup (and why).** *Message* is shown disabled with the reason: the game has no messaging system (convoy chat only). The profile card shows Friends / Online / Jobs done / Distance instead of "Convoys joined", which the backend does not report. Avatars are initials on a colour per player (no player photographs exist). "Mutual friends" on a request is not shown: the backend does not provide it. The list/grid toggle is not built. Search by player *name* is done on your own lists only; the server lookup is the exact username.

**Not responsive.** Like every page, Friends is laid out on a 1920x1080 stage that scales to the window. At 1440x900 and 1280x720 that is fine; at 1024x768 it is letterboxed; at 768x1024 and 390x844 it is a small picture of the desktop page, not a mobile layout (see P-17: the shell, sidebar and header need a narrow-screen design first). The browser E2E takes a screenshot at all six sizes so this is visible, not hidden.

**Tests.** SQL `45_person_vehicle.sql` (a friend and yourself see the vehicle, strangers and people met in passing never do, the selected vehicle is the one shown, no coins or e-mail) + 3 mutants; xunit `FriendsModelTests` (85); API contract and EditMode parse tests for the new fields; browser E2E section 2f: the page opens, the lists equal the database, presence, Message and Invite states, the real invite and its refusal, search, tabs, sort, Accept, Decline, Cancel, Add from Recent, find (found / not found) and add, profiles, Remove with confirmation, Block, an error with retry, the empty state, six window sizes, no errors.

# Leaderboards page

`DashboardLeaderboards.cs` (page, podium, table, pager, search results, side panels), pure rules in `ARO.NetCore` `LeaderboardModel.cs` (metric and period keys, which period a metric supports, number and rank text, medals, page window, podium order; unit tested).
The sidebar item opens it; "Coming Soon" is gone. Every number is a server value: the client sends only the metric, the period, the page and the search text.

| Part | Source |
|---|---|
| Category tabs: XP, Earnings, Distance, Jobs | `get_leaderboard(metric, period, limit, offset)`; one board per metric. Only the four metrics the backend ranks are offered |
| Period menu: All Time / This Week | the same RPC's `period` (`weekly` is computed from the server ledgers: transactions for earnings, completed jobs for jobs and distance). XP has no weekly record: choosing This Week there shows All Time and says so |
| Podium (top 3) | the first rows of page 1 of the global board, in 2nd-1st-3rd order. Real photographs behind the medals, initials avatars, never invented players |
| Table, 12 rows per page, pager | the board page for the offset; Previous / numbers / Next. The total comes from the server, so the pager is exact at any size |
| Your Rank (always visible) | the `me` block of the board reply: rank and value, even when you are not on the page. A player with no score is shown as **Unranked**, never given a rank |
| Everyone / My Friends | `get_friends_leaderboard(metric, period)` (new): you and your accepted friends, ranked among themselves from the same server numbers. Also the Friends Leaderboard panel |
| Top Drivers This Week | the weekly jobs board, top 3 |
| Search | `search_leaderboard(metric, period, query, limit)` (new): matches name or username (plain text, `%` and `_` are ordinary characters), only players the board lists, with their **global** rank; 2+ characters, 10 results, rate limited. Debounced; a miss says so |
| Profile | any row, podium card, friend row or Your Rank opens the same public-profile dialog as Friends (Add Friend / Invite there) |

**States.** Loading placeholders, an error with a working Retry per region (board, Your Rank, friends, weekly, search), a friends empty state with Find Friends, a no-match search state. Refreshes by itself every 30 s while open; a reply for a board the player has already left is discarded.
**Security.** Scores are never sent by the client; the new functions are SECURITY DEFINER with a pinned search path, rate limited, and expose nothing a board does not already list (value > 0 only, public fields only).
**Not responsive** like every page (P-72); not built: region/season boards, rank movement, vehicles on rows (P-73, P-74).
**Tests.** SQL `46_leaderboard_social.sql` (friends board membership and order per metric, pending requests excluded, weekly window, search matching and global ranks, validation, rate limits, privileges) + 12 mutants; xunit `LeaderboardModelTests` (49); API contract + EditMode parse tests for the two new replies; browser E2E section 2g (opens from the sidebar; boards equal the database; page 2; podium and row profiles; Your Rank off the page; four categories; weekly and the XP fallback; friends scope; search short/none/found; error + Retry; unranked; six window sizes; no errors).

# Onboarding (first run) and the Profile page

**Onboarding** (`DashboardOnboarding.cs`, rules in `ARO.NetCore` `OnboardingModel.cs`). A full-screen page above the dashboard, shown once to an account the server says has not been onboarded (`get_my_profile.onboarded = false`). Until it is finished nothing under it can be opened (a click-blocker covers the sidebar and `ShowPage` refuses). Players who existed before onboarding was added were marked onboarded by the migration, so only new accounts are asked.

| Step | What is asked | Where it goes |
|---|---|---|
| 1 Name | Driver name (3-24 letters, numbers, space, `-`, `_`), live preview card | `complete_onboarding.p_display_name` (only sent if changed); the server decides if it is taken |
| 2 Where | Country (searchable list of the server's `countries`), language (the server's `languages`; English is the only one the game is translated into and the page says so) | `p_country_code`, `p_language_code`. The browser's locale (`navigator.language`, via `ARO_BrowserLocale`) only **suggests** a starting point, and only from what the server offers |
| 3 Style | How you want to drive: Deliver freight / Run bus routes / Drive with friends / Explore Africa; speed unit; appear in Players Nearby (**off by default**) | `p_play_style` (decides the first screen: Jobs, Trucks & Buses, Multiplayer or Home); speed unit is a device setting applied at once; the Players Nearby choice is always sent explicitly after `complete_onboarding` through `set_nearby_visibility` (off unless the player tapped it on) |

Nothing is stored before the last step: one call, `complete_onboarding`, validates and stores everything and is replay-safe (the first completion time is kept). A refusal (name taken, unknown country, rate limit) brings the player back to the right step with the server's reason and keeps their answers. If the country/language lists cannot load the step says so and offers Try again. Sign out stays available (it may be the wrong account).

**Backend** (`20261012000006_onboarding.sql`, SQL suite 47, 19 mutants): `languages` reference table (read-only for clients), private `profiles.language_code / play_style / onboarded_at` (never in public profiles, friends or boards), `complete_onboarding`, `set_my_preferences`, and `get_my_profile` now also returns the country and language names, the preferences, `onboarded`, `vehicle_count` and `friend_count`.

**Players Nearby is opt-in** (`20261012000007_nearby_opt_in.sql`, SQL suite 48, 11 mutants, upgrade test 40, E2E onboarding + Settings). `20261011000003_discovery` shipped `appear_nearby` with default `true`, so every account would have appeared without being asked. The fix is a forward migration (the published file is untouched): default `false`, every existing account set to `false` (the `true` they hold is the old default, not a choice), and `set_nearby_visibility(null)` means hidden. It reads and writes no other column. Tests prove the other profile columns are byte-identical before and after, existing players keep their country (a player with none keeps none), countries/cities/locations are identical, and the leaderboards, friends list, public profile and world map still carry the country after opting out.

**Profile** (`DashboardProfile.cs`, opened from the avatar, name and level in the header; `GameFlow` no longer has its own plain panel). Hero (avatar initials, name, @username, country, language, member since, level and the server's XP window, **Edit profile**, **How others see me**), five numbers (jobs completed + completion rate, distance, lifetime earnings + balance, vehicles, friends), leaderboard rankings (four `get_leaderboard` calls, `me` block: rank, value, total; a ranking opens that board; **Unranked** when there is no score), achievements (`list_my_achievements`, unlocked first, progress bars), preferences (country and language each open a picker; play style changes in place), and abandon-job / abandon-bus-route behind a confirmation when one is running. Every change is one server call and the page shows the server's answer; each block has loading, error with Try again, and empty states.

**Not built:** avatar art/upload, bio, translated game text (P-79), a driving tutorial (P-80), responsive layouts (P-72); see P-79 to P-83.
**Tests.** SQL `47_onboarding.sql` (validation of every field, nothing stored on a refusal, replay-safe, later changes, privacy of the preferences, the profile's counts, rate limit); mutation test of the migration (19 mutants); xunit `OnboardingModelTests` (steps, name and username rules, country filtering with accents, language order and notes, locale suggestions, play styles, server error sentences) and `ProfileRulesTests`; API contract + EditMode parse tests for the new replies; browser E2E section 2h (new player lands on onboarding, lists equal the database, locale suggestion, sidebar blocked, bad name blocked, search, language note, style/speed/nearby, **name taken refused by the server and answers kept**, database row equals the choices, landing page, never asked again after a reload, profile numbers/ranking equal the database, edit with a taken username refused, country/language/style saved, public card, six window sizes, error with retry for achievements and for the lists).

# Bus Routes page

`DashboardBusRoutes.cs` (page under Trucks & Buses, reached from the Bus Routes button there, the Home "bus" quick action, the Map and the pause flow; the sidebar keeps Trucks & Buses highlighted), pure rules in `ARO.NetCore` `BusRoutesModel.cs`. It replaces the old plain text panel (`ShowBus`), which is gone.

| Part | Source |
|---|---|
| Route cards | `bus_routes` with `city:cities(name)` and the embedded `bus_route_stops` (code, name, fare per passenger, XP reward, stops with waiting passengers 0..80 and the share that get off). Stops are drawn as a line; stops, fare, XP and demand as facts |
| Demand level | a reading of the server's per-stop numbers (P-86) |
| Detail (right) | the selected route: photo, chips, facts, a scrollable stop list (waiting bar, "N% get off here"), **Your bus** (the owned bus the server would use: the selected one if it can run, else the best one: picture, seats, condition, fuel) |
| Start route | `_bus.Begin` (server `start_bus_run`); the button is off, with the reason under it, when the bus is wrecked or empty, a freight job is active, or no bus is owned: the same cases the server refuses. The server still decides and its reason is shown |
| Run in progress | `BusSession` state: stops completed / current / upcoming, next stop, passengers aboard of the bus capacity, fares so far (paid by the server at the end of the route); **Resume driving**, **Abandon route (no pay)** behind a confirmation |
| States | loading, error with Try again, no routes, no bus (leads to Trucks & Buses, Buses tab) |

**Honest gaps:** one seeded route, no route types or per-route art (P-84, P-85), demand level thresholds (P-86), a run is not restored after a reload (P-87), page not seen rendered (P-88).
**Tests.** xunit `BusRoutesModelTests` (demand, texts, stop states, progress, bus choice, why Start is blocked), EditMode `BusDtoTests` (the route's city), backend smoke (the routes query with the city embed), browser E2E section 2i (opens from Trucks & Buses, routes from the database, error with retry, no bus, a wrecked bus is refused by the page, a real start, the run in progress, Abandon with confirmation and nothing paid, six window sizes, no errors).

