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
