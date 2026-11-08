# Road engine stage E4: corridor index and road graph

Built 2026-11-08 after E1 to E3, on the owner's instruction "start building them now, don't stop until finish all" (plan: `docs/KANO_TRANSPORT_EXPANSION_PLAN.md` section 5.2; `docs/ROAD_ENGINE_PROPOSAL.md` stage E4). **Implemented in code and unit-tested. Part (a) is wired into the engine; part (b) is a model that nothing in the game uses yet, on purpose.** Nothing is approved by the owner.

## (a) The corridor index: wired in, answers identical

A city network has dozens of corridors (the Kano plan: 54 nodes, 83 edges), and every terrain sample, ground-height query, prop-exclusion test and terrain-tile cut looped over **all** of them. `CorridorIndex` is a 128 m grid that lists, per cell, the corridors whose centre line comes within the largest query reach of the cell, **in the same order as `AllCorridors`**, so the first corridor that contains a point still wins and every answer is identical. `RouteModel.CorridorsNear(x, z)` returns that list as an array (no allocation in the hot loops). Used by `RouteModel.GroundHeight` and by `TerrainModel.HeightAt / InsideRoadAlong / InsideFormation`.

* **Only routes with 8 or more corridors use it** (`RouteModel.IndexThreshold`). The Lagos to Ibadan route, the Kano slice draft and the test corridor keep the plain loop, byte for byte; the golden master does not move.
* The test builds a 61-corridor network (a four-way every 100 m on a 3.2 km main road), forces the plain loop and the index on two copies, and compares **4,000 random points** for ground height, terrain height, formation tests and tile cuts: all identical (exact float equality). It also compares points straight beyond the ends of 20 stubs (the legacy rule that treats that ground as road formation for the query reach). Mean candidates per point on that network: below 14 of 61 (the test asserts it).

## (b) The road graph: a model, not yet wired

`RoadGraph.Build(RouteModel)`: nodes are the two ends of the main road, every junction station on it (a four-way is one station with two stubs) and the far end of every side road; edges are the stretches between them (the main road is split at each junction, each side road is one edge, with a short **lead** for the connector from the junction node on the main centre line to the start of the stub at the paved edge). `Locate(x, z)` says which road a point is on, with station and lateral; `ShortestPath` is Dijkstra by length (deterministic ties); `Sample` walks a path as points no more than `step` metres apart.

**Not wired:** traffic (`TrafficManager`), job progress (`DistanceAlong`), the minimap and `Main` keep their meaning. Moving them onto a chosen path is a gameplay change (what traffic does, what progress means) and is **not** done; it needs the owner's decision. Side roads are still the straight three-point stubs of E3; curved or branching side roads, and the ring corridors of E5, extend the same graph.

## Tests (2026-11-08)

| Check | Result |
|---|---|
| .NET unit tests, whole suite | **1,308 passed**, 0 failed (+11: `CorridorIndexTests` 6, `RoadGraphTests` 5) |
| Golden master (existing routes unchanged) | 3 passed |
| Index equals the plain loop | 4,000 random points x 4 queries + 20 stub ends x 20 distances: identical |
| Graph | counts of nodes and edges for a 15-stub network, `Locate` on the main road and on right and left stubs, shortest path length and symmetry, an empty path to itself, continuous sampling, a route without junctions is one edge, the shipped Lagos route's graph |
| In-game | nothing in the game calls the graph; the index is not used by any shipped route, so there is nothing to see in a screenshot |

## Known limits

* The index is rebuilt per model (once, lazily); it is not updated if corridors change (they never do after `Build`).
* Query reaches above 88 m (`MaxFormationHalf + 80 + 8`) would need a larger slack; the terrain reach is the largest in the engine.
* The graph has no turn rules, no one-way flags, no lane connectivity and no elevation separation (E5 adds grade separation).
