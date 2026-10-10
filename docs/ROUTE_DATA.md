# Route data, road generator and terrain

```
WORLD MAP (world_map.json)  --gameRouteId-->  ROUTE DATA (Resources/Routes/{id}.route.json)
      |                                              |
      | consumes metadata, never duplicates it       v
      |                                      RouteModel (pure, unit-tested)
      |                                        spline + runs + bridges + junctions + terrain + scatter
      |                                              |
      |                                              v
      |                                  ChunkStreamer (Unity) -> terrain, road, props, colliders
      v                                              |
 jobs / journeys / HUD                       vehicle, traffic, driving
```

Nothing about Lagos to Ibadan is hard-coded in code. The generator reads a `RouteSpec`; adding a route means adding a JSON file (and a row in `world_map.json`).

## RouteSpec (schema version 1)
| Section | Meaning |
|---|---|
| `meta` | country, from/to, `status` (planned/prototype/live, same vocabulary as the map), `worldMapRouteId`, `realRoadKm`, `gameLengthKm`, `gameCoverage` (honest prose), `elevationSource` |
| `terrain` | base elevation, regional trend plane, hill and detail noise, maximum embankment slope |
| `profiles[]` | road cross-sections: lanes per direction, lane width, median, shoulders, verge, camber, ditch, centre-line style, edge barrier |
| `controlPoints[]` | x, z, elevation, zone, surface, profile. The spline passes exactly through each. Zone, surface and profile apply to the road that STARTS at the point |
| `bridges[]` | span in route distance, valley depth, deck thickness, pier spacing |
| `junctions[]` | T-junction side road: distance, angle (+ right, - left), length, rise, profile |
| `propRules[]` | procedural roadside scatter by zone and side (offsets measured from the road formation edge, never the centre) |
| `props[]` | hand-placed set pieces (signs with text, fuel stations, bus stops) |
| `traffic` | density and speed-limit scale, vehicle mix (informational until production traffic models exist) |
| `waypoints[]` | named places along the route (navigation data; not yet shown in the HUD) |

`RouteSpecValidator` rejects bad data before anything is generated (unknown profile/zone/surface, non-finite or coincident points, curves tighter than 40 m radius, grades over 8 %, bridges that overlap, are too short for their valley or straddle a profile change, junctions on divided roads or too close to bridges, unknown props, unordered waypoints, ...). Every rule has a test.

## Road generator (`RoadSpline`, `RoadCrossSection`, `RoadGeometry`)
* **Spline**: centripetal Catmull-Rom in plan (no cusps/loops), monotone cubic elevation (never overshoots), arc-length queries, nearest-point queries with a spatial grid.
* **Cross-section**: `[median] - inner shoulder - lanes - outer shoulder - verge`, camber, lane/edge/centre markings (dashed, solid, double solid; yellow or white), jersey median barrier, guardrails with posts, bridge parapets, deck underside and piers.
* **Profile changes** keep the same road edge: both sides are scaled laterally over 40 m to meet at the mean formation width.
* **Wear**: worn and damaged surfaces get longitudinal undulation (deterministic) and finer ring spacing (3 m / 2.5 m vs 4 m). Potholes are NOT modelled in geometry (they need a decal/normal-map layer).
* **Chunk ownership**: rings are placed on a global table; each segment belongs to the chunk containing its midpoint, so chunks join exactly and nothing is built twice (tested over the whole route).
* **Junction mouths** are square (no fillets yet); the main road's edge line is interrupted on the junction side and the side road gets a stop line.

## Terrain (`TerrainModel`, `TerrainGeometry`)
Hills are noise on a trend plane. Roads conform: flat under the formation (0.35 m below the surface so they never z-fight), cut and fill slopes no steeper than `maxEmbankmentSlope`, a roadside ditch, and a valley under bridges. Tiles are built per chunk at 5/10/20 m cells by distance (`LodPolicy`), with skirts so neighbouring LODs never crack, cells under wide roads cut out, and a separate closed collision slab (only near the player) so heavy vehicles cannot tunnel through a sheet.
Known limitation: one terrain material per chunk (lush or dry by zone), so a material change is a straight edge at a chunk border. A blended terrain shader is the next step.

## Roadside content
`PropScatter` places props deterministically (every slot decides from a hash of seed, rule, slot and side), independent of which chunks are loaded; tested for determinism, no duplicates/drops across chunks, nothing on the road, zone/side/offset rules. Props are merged into ONE mesh per chunk (one draw call per material slot) and chunk LOD decides detail and culling. Box colliders exist for buildings within ~400 m of the player. **All prop geometry is DEVELOPMENT PLACEHOLDER** generated from `prop_catalog.json` (`DevPropGeometry`); a production prefab at `Resources/Props/{id}.prefab` replaces it per id.

## Lagos to Ibadan: what is implemented
Measured by tests, not claimed:
* Playable corridor **47.6 km = 37 %** of the real 128 km road (compressed, hand-authored from design coordinates; it passes through every seeded job location).
* 13 control points, 6 road profiles (urban 4-lane, commercial 2-lane, urban 4-lane divided, expressway 4- and 6-lane divided, rural 2-lane with guardrail), 2 bridges (Ogun, Ona), 3 junctions, 17 scatter rules, 7 hand-placed set pieces, 10 waypoints.
* Elevations are DESIGN values (trend plane +-1 to 2 m), not survey data. Real alignment, interchanges, toll plazas, real signage and real roadside buildings are not modelled.
* The in-game route status stays `prototype`; the world map shows it as such.

## Not implemented (honest list)
Potholes/decals, junction fillets, roundabouts and interchanges, traffic lights, terrain material blending, water bodies, vegetation impostors, real elevation data, navigation in the HUD, production art of any kind.
