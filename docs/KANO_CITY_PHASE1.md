# Kano City Phase 1 (after the owner's World Design Reference, R-041)

The owner sent the **Kano City World Design Reference** board on 2026-11-16 (registered as R-041, `docs/kano/REFERENCE_REGISTER.md`): a 10 km x 10 km map of 11 districts round a ring road with arterials and roundabouts, and a panel for the look of each part of the city. The old city, Kurmi market and Dala Hill are marked **Pending** on the board itself. This is the first drivable piece of that city.

**State:** level 1 (implemented: `tools/kano/make_city_p1.py` writes `Resources/Routes/ng-kano-city-p1.route.json`) and level 2 (the game's own route builder and network checks, and every city test of E1 and F1 now also run on Phase 1; 1,479 .NET tests pass). Level 3 (WebGL build and drive) waits for CI. **Nothing is approved.**

## 1. What you drive

A spine from the gateway into the centre, heading north-west as on the board (gateway in the south-east, centre in the middle):

| Stretch | District on the board | Road | Buildings (placed by the frontage engine) |
|---|---|---|---|
| A2 gateway road, 1.5 km | Gateway | divided arterial with a planted median | 60: fuel stations, car showrooms, factories, workshops, a few mosques |
| **Ring Road roundabout** (large, two-lane) | - | - | - |
| Ring Road, north-east arm, 1.5 km, curving on a 3.3 km radius round the centre | Industrial | divided arterial | 53: warehouses, wholesale depots, factories, workshops |
| Ring Road, south-west arm, 1.5 km | Mixed Residential | divided arterial | 101: bungalows, semi-detached houses, villas, shop rows, pharmacies, neighbourhood mosques |
| Transport road, 0.9 km | Transport | divided arterial | 47: motor parks, workshops, spare parts, fast food |
| **Transport roundabout** (commercial design) | | | |
| Terminal road, 0.45 km (bus bays) | Transport | collector | 23 |
| Service road, 0.45 km | Industrial | service road | 17 |
| Commercial Belt, 0.9 km | Commercial Belt | divided arterial, two rows of buildings | 100: shop rows, parades, plazas, banks, supermarkets, offices, phone shops, restaurants |
| **Central District roundabout** (landmark island with a mast and palms) | Central | | |
| Estate road, 0.6 km | Modern Estate / Premium Residential | local street | 34: villas, duplexes, mansions, small mosques |
| Suburb road, 0.6 km | Traditional Suburb | local street | 51: Hausa compounds, mud-brick houses, low-cost rows, traditional mosques |
| Central road, 0.7 km | Central District | divided arterial, two rows | 62: offices, apartments, hotels, banks, **grand mosques** |
| **Institutional roundabout** | Institutional | | |
| Institutional road, 0.6 km | Institutional | collector | 19: schools, government offices, hospitals |
| Middle road, 0.6 km | Middle-Class Residential | local street | 37: bungalows, semi-detached houses, villas |

Totals: **10.6 km of road, 4 roundabouts, 604 buildings in 50 designs, 48 mosques (10 grand), 15 fuel stations** (about one per 700 m of road; the shorter test routes show more because they had to show every station design). Street lights, palms, kiosks and bus stops follow the E1 rules for each cross-section; every roundabout is lit. People stand and walk wherever the crowd rules put them (stations, shops, mosques, homes, bus stops). The pumps of every station work.

Choose it on the world map: Kano panel, **DRIVE HERE**, "Kano City (Phase 1)" (listed first). Free drive: jobs, bus routes and convoys in Kano come with the server's Kano data.

## 2. What it is not (honest)

* **The ring road is two curved arms of 1.5 km, not the whole loop.** The engine's ring primitive runs all its lanes one way (it is a roundabout), so the full 20 km two-way ring needs to be built as a chain of arc roads and junction roundabouts all the way round: the same mechanism Phase 1 uses, repeated (next step 1).
* **The gateway flyover** of the board stands on the separate F1 route (the expressway on a deck over a roundabout); Phase 1's gateway is an at-grade divided road (next step 2).
* **Mall and market designs** do not exist yet: the commercial belt uses shop rows, parades and plazas; there is no "Kano Mall" building, and no real brand appears on any sign (the board's brand names are flagged in R-041).
* **The old city, Kurmi market and Dala Hill** stay pending, as the board says: they need authentic references (M-01, M-04, M-11, M-12).
* **Traffic** is off on Phase 1, as on the engine's roundabout test routes (traffic on roundabouts is not modelled yet).
* Positions follow the board's intent, not a survey; sign text with real place names needs verified route data (RB-07).

## 3. Next steps (in order)

1. **The whole ring road**: arc roads between roundabouts round the full circle, with the arterials of the board meeting it, so every outer district sits on it.
2. **The gateway expressway with its flyover** in the same route (the F1 interchange as the A2's crossing of the ring road).
3. **New designs the board shows**: a shopping mall (generic shop names), a bus terminal hall on a site plate at the terminal road, a larger hospital campus.
4. **Districts in depth**: estate loops and cross streets inside each district (the frontage engine already fills any road it is given).
5. **Standing vegetation** (grass, shrubs, street trees) and the long-range performance work for the full 10 km city (RB-15/16/17: proposals that need the owner's approval).

## 4. Where it is

* Generator: `tools/kano/make_city_p1.py` (reuses `tools/kano/make_city_routes.py` mixes and `tools/e5/gen_e5_routes.py` cross-section maths and roundabout designs). `--check` reports whether the committed route is up to date.
* Route: `Assets/_Project/Resources/Routes/ng-kano-city-p1.route.json`; in the world map `NetCore/DriveRegions.cs`.
* Tests: `FrontageTests`, `PeopleTests`, `ServicePointsTests` run every city check on Phase 1 too (no overlap, nothing on a road, nobody on a carriageway, every pump off the road, the mix varied, a golden digest); `DriveRegionsTests`.
* CI: a drive on Phase 1 (`webgl-drive-p1-*`) and the pump and people pictures on Phase 1 (`webgl-pump-ng-kano-city-p1*`) in the playable build.
