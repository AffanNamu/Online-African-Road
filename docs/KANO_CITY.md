# Kano City: the actual city (owner directive of 2026-11-16)

The city is one route, `ng-kano-city`, generated from the master plan's own road graph (`docs/kano/city/city_plan.json`, KSA-1, 10 km x 10 km) by `tools/kano/make_city.py`. E1, F1 and Phase 1 are test environments, not the city. On the world map: Kano panel, DRIVE HERE, **Kano City** (first).

## Built (level 1 and 2; the WebGL build and drive run in CI)

| Part | What | Size |
|---|---|---|
| A2 expressway | from the study area's south-west edge through the D11 gateway to the ring road (divided, planted median, median lights); where the drive starts | 3.4 km |
| Ring Road | a 3.3 km circle, two-way divided arterial, roundabouts at 0, 90, 180, 225 (the A2) and 270 degrees, T junctions every 22.5 degrees | 20.7 km |
| Middle ring | ellipse 2.8 x 2.7 km, collector, crossing the radials, T junctions with the sub-radials | ~17 km |
| Road along the old city wall | ellipse 2.3 x 2.1 km, collector, four gate roundabouts (0, 90, 180, 270) | ~14 km |
| Radial arterials | eight, ring road to the wall, crossing the middle ring | ~9 km |
| Sub-radials | eight, ring road to the middle ring | ~4 km |
| **D06 Modern residential estate** | four concentric estate streets and cross streets every ~150 m between the 45-degree arterial and the 67.5-degree sub-radial; walled compounds (low-cost rows and houses on the inner street, bungalows and semi-detached houses, duplexes and villas, mansions on the outer edge), neighbourhood mosques; shops, offices and fuel stations on the rings and arterials round it; street lights and trees | 259 compounds |
| **D05 Commercial belt** | 22.5 to 45 degrees, middle ring to ring road: three concentric streets and six cross streets per ring; shop rows, phone shops, spare parts, pharmacies, parades and plazas, banks, offices and hotels on the outer street, neighbourhood and grand mosques | 136 buildings |
| **D08 Institutional corridor** | 67.5 to 90 degrees: two streets and five cross streets; the general hospital, schools, government offices, police divisions, a hotel and clinics on large plots | 57 buildings |
| **D09 Transport hub** | 202.5 to 225 degrees, beside the A2 interchange: two streets, five cross streets, motor parks, workshops, spare parts and shops; the **bus terminal** site plate (bays, apron, entrance) at the end of its own access road | 96 buildings, 1 terminal |
| **D10 Industrial and logistics** | four service roads off the A2 (440 m each, both sides of the expressway): warehouses, factories, workshops, wholesale depots; the **logistics yard** site plate at the end of road 1 | 52 buildings, 1 yard |
| **D11 Gateway services** | a service road off the A2 at the gateway: fuel stations, showrooms, fast food, a mosque | 16 buildings |
| **Mixed urban quarters (F-MID)** | the plan's filler zone ("modern residential, commercial frontage and institutional plots") with its own streets: nine 22.5-degree sectors of the outer band (two concentric streets, four cross streets each) and all eight 45-degree sectors between the wall road and the middle ring (two streets, up to seven cross streets each); bungalows, semi-detached houses, duplexes, villas and low-cost rows, shop streets, a sector of schools, hospitals and government offices, a premium sector with mansions, Friday mosques on three main streets | about 2,600 buildings |
| City fabric | every road carries buildings for its place: shops, offices, apartments, banks, hotels and grand mosques on the arterials; houses and shops with mosques on the rings; shops outside the wall; fuel stations, showrooms, factories and depots along the A2 | |
| **Street lights and trees on every street** | the street rules now run along every road of the network, not only the A2 (scatter rules with `allRoads`, an opt-in engine flag: no older route sets it, their golden masters are unchanged): street lights on every road class, lights on the medians of the divided arterials, palms along the ring road and radials, and **real tree models of the environment kit** (two broadleaf street trees on the neighbourhood streets and service roads, the large shade tree on the collectors) | about 8,000 lights, 4,300 trees, 160 palms |
| **Whole city** | | **5,480 buildings, 219 mosques (9 grand), 91 fuel stations, 12 hospitals, 45 schools; about 8,000 street lights and 4,300 trees** |

Network: 354 roads, 9 roundabouts, 179 km of streets. Left empty on purpose: the D07 sector (157.5 to 202.5 degrees of the outer band, the traditional suburb: BLOCKED on references in the plan) and everything inside the wall. The route builds in about 0.5 s and places its buildings in about 0.7 s (native). Every city check of the test routes also runs on the city: no building overlaps another or touches a road, corner lots of two roads never meet (each road leaves a 58 m window round every junction), nobody in the crowd stands on a carriageway or inside a building, every pump is off the road, the mix is varied, a golden digest.

## Not built yet (in order of work)
1. **RB-15 batching** (approved 2026-11-16): the instanced renderer is in the game and its tests pass in CI (run 356), but **it has not been seen at work yet**: both drives of run 356 counted 0 instanced buildings because neither passed a real building (the "city" page loaded the Lagos to Ibadan road, see 2, and the E1 street corridor has none near its start). The instancer now logs what it draws, culls and refuses (with the reason) and any draw error, and the drive test prints those lines; the next run's city drive, among the D10 warehouses, measures it.
2. **The CI pictures of the city**: until 2026-11-16 the test pages ignored `route=ng-kano-city` (only the test corridors were on their list), so the "city" drive and pump pictures of CI run 356 were of the Lagos to Ibadan road. Fixed: the city and Phase 1 are on the list; the city drive starts on the A2 at 2.2 km, among the D10 roads.
3. **New assets placed in the city**: the street trees are in (above); still to come: parked vehicles, a generic neighbourhood market, a university, and real street-light models (the lights are the game's placeholder lamps: the kit's lamp models need their night light heads in the catalogue first).
4. Traffic on the network (roundabouts and T junctions are not driven by traffic yet), and the A2 interchange as a flyover.
5. **Pending, by the owner's rule**: the old city inside the wall, Kurmi market, the palace and Central Mosque precinct, Dala Hill. Nothing is built inside the wall and no landmark is invented.
