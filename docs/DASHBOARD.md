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

`DashboardMultiplayer.cs` (layout, polling), `DashboardMultiplayerActions.cs` (loading, server actions, dialogs). Flow, each step decided by the server
(`supabase/migrations/20261011000002_convoy_lobby.sql`):

```
MULTIPLAYER -> Find / Create convoy -> Select route -> Invite / Join -> Convoy lobby -> Ready check -> Start driving -> Shared world -> Jobs together -> Rewards / XP
```

| On screen | Source / rule |
|---|---|
| Available Convoys (name, Open / In Progress / Full, members, route distance and time, tags, language, Join) | `list_open_convoys` (public convoys only; server search and tag filter; lobbies sort before convoys already on the road) |
| Live Players (name, level, truck, status) and the Active Players tab | `list_active_players`: players with a live session (heartbeat inside the TTL), never yourself. Level and truck are public; **activity is shown only to friends** (strangers see "Online"/"Convoy") |
| Server Status (players online, active convoys, your ping) | `get_server_status`; ping is the measured round trip of the convoy poll |
| Create Convoy dialog (name, route, public / invite only, 2-8 players, up to 3 tags) | `create_convoy` then `set_convoy_route`, `set_convoy_options`, `set_convoy_tags`; the page reports which step the server refused. **No live session is created here** |
| Your Convoy: members (level, truck, Leader / Ready / Not ready), route and distance, tags | `get_my_convoy`, polled every 3 s; the leader counts as ready |
| Route picker | `get_world_state`: OPEN routes selectable, LOCKED show their level, PLANNED are listed but disabled; the server re-checks (leader and every member's level) |
| I'm Ready / Start Driving | `set_my_ready` (needs a route; changing the route clears everyone's ready flag); `start_convoy_drive` (leader only; route chosen, all others ready, every member's level meets the route) |
| Live session | created by the leader only after the server accepted the start; its code is stored with `set_convoy_session_code` and members fetch it through `get_convoy_session_code` (members only). If the live session cannot be created the convoy goes back to the lobby (`end_convoy_drive`), never stuck "driving" |
| Invite Player, Manage (capacity, visibility, Make leader, Kick, Back to lobby, Leave) | `invite_to_convoy`, `set_convoy_options`, `transfer_convoy_leader`, `kick_convoy_member`, `end_convoy_drive`, `leave_convoy`; invites arrive in the "Your Convoy" panel of the invited player (`list_convoy_invites`, `respond_convoy_invite`) |
| Convoy Chat / System | `list_convoy_messages` (first load = newest 50, then polling every 2.5 s), `post_convoy_message` (members only, 200 chars, 20 per 30 s, angle brackets and control characters stripped server-side, shown without rich text; the server keeps the last 200 lines per convoy). System lines (joined, ready, route, started) are written by the server |

## Not in the build, and said so on screen

| Mockup | Build | Why |
|---|---|---|
| Voice Chat tab | opens "Voice chat is not available yet" | there is no voice service |
| Global Chat tab | disabled, toast "needs moderation tools first" | open chat with strangers needs moderation, rate rules and reporting that do not exist |
| Server List tab | opens "Server selection is not available yet" | one game server; its numbers are in Server Status |
| Per-convoy latency (24 ms ...) | omitted | there is nothing real to measure per convoy |
| Distance column in Live Players | replaced by Status | positions never touch the database (see MULTIPLAYER.md), so a "distance" would be invented |
| Server "12 game servers" | "Active Convoys" | same reason |
| Convoy art | the key art, cropped differently per row | no per-convoy pictures exist |

## Tests

* SQL `supabase/tests/41_convoy_lobby.sql` (route rules, ready check, start/stop, tags, chat incl. flooding, retention, sanitising, convoy and player search, privacy of activity, server status, leaving) and 53 mutants (all caught).
* API contract: 9 new endpoints, C# DTOs vs the real RPC output; EditMode `ConvoyLobbyRepliesParseWithTheRealJsonUtility` parses the real replies.
* Browser (`tools/webgl-e2e.js`, real build, real PostgREST and database): list and players from the database, create convoy with route and tag, a second player joins and readies up (seen by polling), chat both ways, Start Driving with the live-session service unreachable (the convoy must return to the lobby), Leave passes leadership on, failing list shows Unavailable and Try again works, Voice tab is honest.
* **Not verified anywhere:** the live shared world itself (Unity Multiplayer Services / Relay, avatars, two or more real clients driving together) and completing a job together. The code path exists and is unchanged; it has never run because CI has no Multiplayer Services project.
