# Dashboard and Jobs page

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
| Photographic cargo pictures and a photographic hero | generated stand-in illustrations (960x400, `tools/gen_dash_art.py`) | no licensed photography is in the repo (public repository). **Drop your own images in `Assets/_Project/Resources/UI/` as `job_containers|tanker|pipes|food|general|cement|machinery|vehicles.jpg` and they replace the stand-ins with no code change** |
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
server list, create / settings / disband / player / report dialogs). The convoy is the centre of the page. Flow, each step decided by the server
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
| Players Nearby | `list_nearby_players`: players in the same **route** (else same **city**) in their own live presence. Positions never reach the database, so there is **no distance**; the panel names the scope instead. Players can opt out ("You appear in nearby lists" toggle) and blocked players are excluded. Click a row: View Profile / Invite / Join Convoy / Add Friend / Block |
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
