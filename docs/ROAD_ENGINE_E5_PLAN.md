# Road engine stage E5: roundabouts and grade separation. Written technical and physics plan

Written 2026-11-08. **E5 is NOT built.** The project rules require a written technical proposal (`ROAD_ENGINE_PROPOSAL.md` section 5, `KANO_TRANSPORT_EXPANSION_PLAN.md` section 5) and the owner's explicit approval before any engine work that changes physics, and the plan marks E5 as the one stage that does: it changes what the ground is where roads stack. Stages E1 to E4 were built on the owner's "start building them now, don't stop until finish all"; for E5 this document is the physics plan, and **section 8 lists what the agent needs from the owner before writing any E5 code**.

## 1. What E5 must deliver

The seven roundabout designs (Landmark at MA225, Modern urban at RR90, Traditional at RR180, Large multi-lane at RR270, Commercial at MA0, Green at RR0, Highway interchange at RR225), the three flyover candidates (F1 RR225 flagship, F2 the A2 over the logistics junction X_LOG, F3 a radial over the middle ring at MA270) and the ramps that join them to the network. Each is a real piece of the road network: vehicles drive on it and the world (ground, terrain, props, lights, traffic) agrees with it.

## 2. Ring corridors (the roundabout itself)

* **Geometry.** A ring is a closed corridor: a circle (centre, radius R, direction counter-clockwise seen from above, right-hand traffic) sampled as a polyline. New: `RoadSpline` closed mode (wrap-around `FrameAt`, `Nearest`, arc length) or an analytic `RingSpline`; `RouteSpecValidator`'s 40 m minimum-radius rule applies to the main spline only, a ring may be as small as 12 m (island radius 12 to 30 m plus 1 to 3 circulating lanes of 3.5 m).
* **Cross-section.** The existing profile (lanes, shoulders, kerb, planter or painted rim from E1) with a crown **away from the island** (single-sided camber, 2 percent); the island side carries the E1 mountable kerb and a 2.5 m mountable apron on the large designs.
* **Mesh density.** Rings use 1.5 m stations (the 4 m station of straight roads would leave a 9 cm sag on a 22 m radius).
* **Legs.** Side corridors start at the ring's outer paved edge at an angle (radial direction), as E3 stubs do from the main road; entry flares reuse the E3 fillet, measured in the ring's frame; **splitter islands** are small E1 kerb polygons (new `IslandSpec`); yield lines and lane arrows are E1 symbols in ring coordinates.
* **Central island.** A flat disc (planter, painted or paved) with an E1 kerb rim and props placed by the plate machinery of E2 (monument, planting, uplights).

## 3. Grade separation (the flyover)

* **Deck over road.** A flyover is the existing bridge code (deck, parapets, piers, thickness) generalised from "the main road over a terrain valley" to **"any corridor over another corridor"**: `BridgeSpec` gains `over` (the corridor it crosses) and the validator requires a vertical clearance of at least **5.5 m** (assumption for the tallest design vehicle; the lower surface to the underside of the deck) and approach grades of at most 6 percent (the engine's maximum is 8 percent).
* **Ramps** are side corridors with `rise` (slip roads, 120 m merge tapers), the E3/E4 machinery.
* **Elevation choice.** `RouteModel.GroundHeight(x, z)` today returns the first corridor that contains the point. It gets a third argument `nearY` (the height of whatever is asking) and returns the surface closest to it; the two-argument form keeps returning the **lowest** surface (not the first), which is what the kill floor and the lamp pools need under a deck. For routes without stacked roads the answer is unchanged (golden master).
* **Terrain.** `TerrainModel.HeightAt` follows the **lowest** corridor that contains a point (the underpass road), not the deck; embankments of ramps use the existing cut and fill slope; the terrain tile cut (`InsideRoadAlong`) cuts under the lower road and not under the deck. Piers stand on the lowest surface.

## 4. Physics plan (what changes for a vehicle)

| Concern | Plan |
|---|---|
| Wheels and the road | vehicles stand on **mesh colliders of the generated road**, exactly as today; E5 changes meshes and the data that makes them, not the vehicle code. No change to `VehicleController`, wheel colliders, suspension or friction |
| Kerb and island | the E1 kerb is mountable (7 cm face, up to 20 cm); the central island is solid surface a vehicle can climb onto (as real roundabouts), nothing invisible; trees and monuments have catalogue box colliders (E2) |
| Underpass | the deck's collider is above the lower road; deck underside faces down (the existing slab underside); parapets and piers are box colliders |
| Kill floor | `GroundY(x, z)` returns the lowest surface at that point, so a vehicle on a deck is never "below the floor" and a vehicle under it is judged by the lower road |
| Camera | the chase camera stays outside decks only if it does not collide with them; E5 includes a test that drives under a deck in every camera view (CI `webgl-drive` with the E5 route) and a decision to push the camera, shorten the boom, or ignore ceilings when a deck is above |
| Traffic | AI follows `Main` only and does not use the ring or ramps (E4b is a model); AI cars passing under a deck on `Main` is unaffected |
| Multiplayer | the world is built from shared data, every generator is deterministic (golden master digest of the E5 test route) |

## 5. Test and verification plan

1. Pure unit tests: closed spline wrap-around, ring sampling error below 3 cm, legs join the ring at their tangent points, island geometry, `GroundHeight(nearY)` on stacked roads, terrain follows the lowest corridor, clearance validator, deck and pier geometry on a road over a road, golden master unchanged for every shipped route.
2. A **new test route** (never a shipped one): a ring with four legs, and a deck crossing a lower road with ramps; its own golden digest.
3. CI in the real WebGL build: the drive scenario on the test route (land on ground, accelerate, drive a lap of the ring, drive under and over the deck, no `vehicle left the world`), frames of each camera view under the deck, day and night, with the lamps of the night system on the ring.
4. **The owner drives it.** A software-GL CI run says the truck did not fall through and did not get stuck; it cannot say that the ring feels right at speed or that the camera is comfortable under a deck. That is the owner's call after looking at pictures and, preferably, driving the test route in the browser build.

## 6. Risks

| Risk | Mitigation |
|---|---|
| A change to `GroundHeight` or `HeightAt` alters an existing route | golden master (three tests) must stay green; the new argument and the lowest-surface rule only differ when corridors stack |
| Ring collision with wheels at speed on a 15 m radius | rings use 1.5 m stations, collider is the same mesh; the minimum radius is 12 m island + lane, tested by the CI drive lap |
| Camera under decks | tested per view; fallback is a documented camera rule |
| WebGL cost of long decks and rings | stations and piers are bounded; the index of E4a keeps point queries cheap; measured in the CI frame stats |
| The Highway interchange design (R-036) is not engineering valid | the layout is **designed** from the network (RR225), not copied; R-036 only informs the look |

## 7. Order of work if approved

1. Closed spline and ring corridor with legs, island and markings (roundabouts 2, 3, 5, 6, which are single-lane and need no deck).
2. Multi-lane rings (roundabout 4, the large design) and the landmark ring (roundabout 1, subject to the monument decision).
3. Flyover by deck over road, ramps, `GroundHeight(nearY)`, terrain rules (F1 with roundabout 7, then F2, F3).
4. The CI drive laps, the owner's review.

## 8. What the agent needs from the owner

1. **Approval to build E5** under this plan (the physics plan in sections 3 and 4, in particular: `GroundHeight` returning the lowest surface, terrain following the lowest corridor, the central island being a solid mountable surface).
2. **The three defaults the plan chose for the open decisions** (plan section 9): the **monument** of the Landmark roundabout is a designed civic monument, flagged fictional (CITY_SPECIFIC); the **Traditional roundabout** is a flagged design interpretation from documentary sources only; **brands** on signs and billboards are generic and fictional. Confirm or change.
3. Which **flyover first**: F1 (the ring interchange RR225, with roundabout 7) is the proposal.

Until these arrive, E5 stays unbuilt; everything else (real Blender assets for the logistics and bus sites, the gameplay data for Kano, the advertising system) is waiting on its own approvals, listed in the handover.
