# Kano in the African Roads Online game today, and where the residential assets fit

**Status (2026-11-01): INSPECTION ONLY. No map data, route data, seed or database row was changed.** The owner asked that the real in-game Kano map be inspected before the city is built further, not the planning diagram alone, and that differences be reported before any live data is touched.

## 1. What the Unity project contains for Kano (read from the repository, not from the plan)
| Where | What it says about Kano | Evidence |
|---|---|---|
| `Assets/_Project/Resources/WorldMap/world_map.json` | one **city pin**: id `kano`, Nigeria, lat 12.0022, lon 8.592, map pixel (1419.3, 458.3) on the illustrative west-Africa artwork | the file; `docs/WORLD_MAP.md` calls the lat/lon "authoritative" |
| same file, `routes` | two **edges** touching Kano: `kaduna-kano` and `kano-niamey` (strategic-map lines, no geometry) | the file |
| `supabase/seed_world.sql` (generated from the world map) | city row `Kano`, status **PLANNED**, unlock level 9; route `kaduna-kano` 272.5 km "estimated" (status PLANNED, no game route id); route `kano-niamey` 937.4 km "estimated" (PLANNED) | lines 43, 57, 61 |
| `Assets/_Project/Resources/Routes/` | **one** route only: `ng-lagos-ibadan.route.json` (prototype status, 47.6 km corridor, zones urban / commercial / industrial / highway / rural, 6 profiles, 3 junctions, 17 prop rules) | `ls`; `RouteDefinition.routeId` defaults to `ng-lagos-ibadan`; `SceneBuilder` wires only that file |
| `Assets/_Project/Resources/Props/prop_catalog.json` | the generic placeholder prop catalogue (no Kano-specific entry) | file |
| `docs/ROUTE_DATA.md`, `docs/WORLD_MAP.md` | statuses `planned` / `prototype` / `live`; "a planned route may not name a game route id"; **no route is live** | the docs |

## 2. What does NOT exist in the game
* **No Kano road network, district, boundary, junction or street.** There is no Kano map below the strategic level: Kano is a labelled point and two planned lines on an illustrated map.
* **No Kano route** in `Resources/Routes`, no Kano job locations in the backend seed, no Kano zone or prop rule.
* So the "existing Kano map" that the plan should agree with is **the pin and the two planned edges**, and nothing else. The master plan (`KANO_CITY_MASTER_PLAN.md`) is not an alternative to an existing Kano layout; there is none to replace, and it was not written as one (it is indicative planning on a verified anchor).

## 3. Differences between the game's data and the plan, reported before anything is touched
| # | Game | Plan / research | Consequence | Action taken |
|---|---|---|---|---|
| 1 | Kano pin 12.0022 N, 8.592 E | verified anchor GeoNames 12.00012 N, 8.51672 E (Wikipedia agrees to 13 m): the pin is **8.2 km east** | the city frame K0 and the strategic pin disagree by 8.2 km; all eight Nigerian and Nigerien pins differ from GeoNames by 1.7 to 10.8 km | none: two frames are kept (decision C6); correcting the pins is one reviewed data change needing the owner's authorisation (it touches the live `cities` rows) |
| 2 | Kaduna to Kano 272.5 km "estimated" | web sources 232 to 272 km by road; the concept sign says 262 km; Kano to Zaria about 155 to 165 km | the game's own estimate is at the top of the range of sources; sign text must use verified data | none; the estimate stays as the game's label; sign text is a data slot (RB-07) |
| 3 | Kano status PLANNED, unlock level 9, no route | the plan has 11 districts and a 54-junction network | nothing is drivable; the plan describes a target, not the game | none |
| 4 | the one route's zones are urban / commercial / industrial / highway / rural with generic prop rules | the plan has residential, commercial, civic, industrial, gateway and traditional districts with specific assets | a residential Kano district has no home in the existing zone vocabulary beyond `urban` | see section 4: zones are data, no engine change |
| 5 | roads are one main spline with straight junction stubs, no kerbs or sidewalks | the plan's network has 4-way crossings, roundabouts, an interchange, kerbs and sidewalks | 21 of 54 planned junctions cannot be built | `ROAD_ENGINE_PROPOSAL.md` (E0 measurement approved; nothing else started) |

No conflict required a change to live map data, so none was made.

## 4. How the 14 residential assets (and later assets) fit the existing game world
The game places production content in three ways; each is a level of integration, from what works today to what needs the engine proposal.

| Level | What it means | Status for the 14 assets | Needs |
|---|---|---|---|
| **L0 review scene** | the assets are placed in a prefab arrangement and the running game shows them with its own lighting, camera and post-processing (`?smoke=kanostreet`) | built; compiles and boots in CI with a generic fixture; **the real assets have not been run in it yet** (private build, section 6) | the owner's Unity secrets in `ARO-CONTENT` |
| **L1 scatter** | each asset is a production prefab under `Resources/Props/{id}.prefab` and a catalogue entry; `PropScatter` places it by zone rules along a route, deterministically; the road itself is the engine's | prefabs are built from the exports; the street section asset is **not** a scatter prop (the engine builds roads); villas, walls, gates, trees, hedges, shrubs, lights, signs and poles are scatter-capable, but the engine has no kerb or sidewalk, so a plot's apron problem (workaround W-01 of TC-1) returns | a Kano route spec (and, for it to be reachable, a world-map route status change and backend location rows: production data, not touched) |
| **L2 network** | a real city street graph with kerbs, sidewalks, markings and junctions generated by the road model | not possible today | engine stages E1 to E4 of the proposal, each separately approved |
| **L3 city streaming** | district density with baked chunks and impostors | not possible today | E6, decided from E0's measurements |

**Rules that follow for every future asset, so they fit the existing game and the existing map:**
1. A new asset id is new; a production prefab replaces the placeholder of the **same** id in every route, so Kano ids (`kano_`, `lmk_kano_`) or regional ids never collide with the generic ones.
2. Position in the world is the route spec's job. Assets carry no world coordinates; the K0 frame is local to the city and aligned with the 200 m chunk lattice, and is converted to the strategic map only by an explicit, reviewed transform.
3. Nothing in an asset package changes `RouteSpec`, `RouteModel`, `PropScatter`, `ChunkStreamer`, traffic, vehicles or networking. Where a design needs a change there, it is a proposal (`ROAD_ENGINE_PROPOSAL.md`), not a side effect.
4. The plan's districts stay indicative until they are tied to verified geography (OpenStreetMap, Wikidata, GeoNames; the proxy still refuses those hosts) and to a real route spec.
