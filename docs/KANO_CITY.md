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
| City fabric | every road carries buildings for its place: shops, offices, apartments, banks, hotels and grand mosques on the arterials; houses and shops with mosques on the rings; shops outside the wall; fuel stations, showrooms, factories and depots along the A2 | 3,353 buildings in all, 204 mosques, 68 fuel stations |

Network: 77 roads, 9 roundabouts, 76 km. The route builds in about 0.5 s and places its buildings in about 0.7 s (native). Every city check of the test routes also runs on the city: no building overlaps another or touches a road, corner lots of two roads never meet (each road leaves a 58 m window round every junction), nobody in the crowd stands on a carriageway or inside a building, every pump is off the road, the mix is varied, a golden digest.

## Not built yet (in order of work)
1. **RB-15 batching** (approved 2026-11-16): instanced drawing of repeated building meshes so the dense districts run on phones. Until it lands, the dense estate costs one draw call per building material.
2. **The other ready districts**: D11 gateway, D10 industrial and logistics, D09 transport hub, D08 institutional corridor, D05 commercial belt, as complete districts with their own streets.
3. **New assets placed in the city**: trees and vegetation, parked vehicles, a generic neighbourhood market, a university.
4. Traffic on the network (roundabouts and T junctions are not driven by traffic yet), and the A2 interchange as a flyover.
5. **Pending, by the owner's rule**: the old city inside the wall, Kurmi market, the palace and Central Mosque precinct, Dala Hill. Nothing is built inside the wall and no landmark is invented.
