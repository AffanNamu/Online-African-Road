# Traffic through junctions and roundabouts: technical proposal for the owner's decision

**Status (2026-11-22): APPROVED by the owner on 2026-11-22 ("I approve every step") and implemented as stage 1: `NetCore/TrafficNetwork.cs` (`TrafficNet`, `NetTrafficSim`), `World/TrafficManager.cs` (used when the route sets `traffic.network`; only `ng-kano-city` does), tests in `tests/NetCore.Tests/TrafficNetworkTests.cs`. Verified in .NET; not yet seen in the game. What differs from the text below: free road ends (site roads, the city's edges) get a turning round, and a vehicle that reaches one out of sight leaves the map instead; a left turn off the priority road gives way to the oncoming lane; only one yielding vehicle crosses a junction box at a time; and a vehicle near the player looks along the next 24 m of its own path and stops short of the player's vehicle wherever it is (in a junction box, across a lane), because traffic bodies are kinematic and would push or pin it (seen in CI run 370; a test parks the player across a busy crossroads: 27 contacts without the rule, none with it). The text below is the proposal as written on 2026-11-16.**

**Original status (2026-11-16): PROPOSAL. Nothing in this document is implemented.** The owner asked on 2026-11-16 to "improve traffic activity across junctions and roundabouts". Traffic is vehicle behaviour, which the project rules (`CLAUDE.md`) do not let me change without a written proposal and the owner's explicit approval. This is that proposal.

## 1. What the game does today (read from the code)
* `NetCore/Traffic.cs` (`TrafficSim`): one-dimensional traffic on **one corridor**, the route's main road. Each vehicle has a position `S` along it and a direction; car following uses the Intelligent Driver Model (IDM); vehicles are spawned 350 to 800 m ahead and 120 to 250 m behind the player and removed beyond 1,100 m or past either end of the road. It is pure C# with unit tests.
* `World/TrafficManager.cs` draws the vehicles as pooled kinematic boxes in the right-hand lane of their direction (the player can hit them; traffic brakes for the player). Its own summary says: "Intersections and traffic lights are NOT implemented (route is a single corridor)".
* Traffic is ambient and local to each client: it is not sent to other players.
* **In Kano City since this change:** traffic is switched on along the main road (`densityScale` 0.8): the A2 from the gateway, over the flyover, to the middle ring roundabout. The other 357 roads and all ten roundabouts have no traffic. A vehicle that reaches the end of the A2 at the middle ring roundabout disappears there.

## 2. What I propose to build
A network traffic model used only by routes that ask for it (`traffic.network = true`; the Lagos to Ibadan road and every test route keep today's model unchanged, byte for byte):
1. **Corridors as lanes.** Every road and roundabout ring of the route becomes a traffic corridor, the vehicles keeping today's per-corridor 1-D state (`S`, speed, direction) and today's IDM car following, so the tested core is reused.
2. **Turning at junctions.** A road's ends and its joins (`joins` in the route data) are the junctions. When a vehicle reaches one it picks the next corridor from the network graph (`NetCore/RoadGraph.cs`, E4b, builds such a graph for the main road and its junction side roads and nothing uses it yet; it would be extended to the city's roads, their joins and the roundabout rings): straight on more often than a turn, never a U-turn, weighted by road class so arterials carry more traffic than neighbourhood streets.
3. **Priority.** At a T junction the vehicle on the joining road yields: it waits at the stop line until the gap on the through road is large enough (a gap-acceptance time, about 4 s). No traffic lights in this stage.
4. **Roundabouts.** The ring is a one-way circulating corridor (anticlockwise, Nigerian right-hand traffic). Entering vehicles yield to circulating ones (gap acceptance at the mouth), circulate and leave at the exit they chose. The splitter islands and mouths the road engine already builds give the entry and exit points.
5. **Spawning around the player.** Vehicles appear on any corridor between 350 and 800 m from the player (never in view close up) and vanish beyond 1,100 m, as today, with the same caps per quality tier (20 on Low, 40 on Medium, 80 on High), so the cost stays bounded.
6. **The A2 ramps and the flyover.** The ramps are corridors like the others: traffic leaves the A2 for the Ring Road roundabout and joins it again; through traffic stays on the deck.

## 3. What does not change
Driving physics, the player's vehicle, collisions and damage, weather, road generation, the shipped route's traffic, multiplayer (traffic stays local and ambient), the server.

## 4. Tests (written before the code, as for RB-15)
Pure C# unit tests in `tests/NetCore.Tests`: no two vehicles ever overlap on a corridor or across a junction; a joining vehicle waits for a gap; nobody enters a roundabout into a circulating vehicle; every vehicle that leaves a corridor continues on a connected one; no vehicle stops for ever (deadlock check over 10 simulated minutes); the old single-corridor model produces exactly what it produces today. A CI drive in the city counts the vehicles on side roads and on a roundabout and saves pictures.

## 5. Cost and risks
About two to three days of work. Risks: deadlocks at busy roundabouts (handled by the deadlock test and a release rule); CPU cost on phones (bounded by the per-tier caps; measured on the CI's Low tier and to be measured on a real phone); vehicles turning through each other inside a junction box (no conflict model inside the box in this stage: a vehicle commits once it enters, as the player does).

## 6. What I need from the owner
Approve, change or reject. Until then, traffic in Kano City stays on the main road only.
