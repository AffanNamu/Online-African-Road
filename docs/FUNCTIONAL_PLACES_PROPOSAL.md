# Proposal: places that WORK (fuel stations, warehouses, bus terminals, people)

**Status (2026-11-14, later the same day): the owner approved P1 and P2 (P3 later). P1 is implemented, see `docs/REFUEL_AT_A_PUMP.md`. P2 is implemented too, see `docs/PEOPLE_AND_CROWDS.md`. The people are built by code in the game's C#, not in Blender; that document lists what differs from the text below.** P1 needed one server change, which the owner approved: `buy_fuel` takes the game's fuel reading. Without it, a pump could not sell the right litres mid-job. Two corrections to the text below: the truck fuel island is not placed on any route yet, so it does not work in the game; and there is no engine on or off, so the vehicle must only be stopped.

Original text: Written 2026-11-14 for the owner's request: *"The filling station ... cars will enter and refuel. It's not just a building. Warehouse ... someone who has taken a job can go there and drop it. Bus terminal ... people moving around ... passengers until passengers fill the bus. We are building reality."* These are gameplay and engine changes. Under the repository rules (CLAUDE.md) they need this written proposal and the owner's explicit approval. Nothing below changes driving physics, vehicle behaviour, the weather or road generation.

## 1. What already exists (read from the code)

| Piece | Where | What it does today |
|---|---|---|
| Fuel | `VehicleController.fuelL`, burn per km | The tank drains as you drive and an empty tank stops the engine. **Refuelling only exists in the Garage menu** (`BackendServices.Refuel` → server RPC `buy_fuel`, priced and charged by the server). |
| Cargo jobs | `DrivingSession` | Pick up within 40 m and deliver within 100 m of server-defined locations, after stopping (< 6 km/h). The locations come from the server; **Kano has none yet** (`seed_world.sql`: Kano is `PLANNED`). |
| Bus routes | `BusSession`, `BusRoutesModel`, `BusRules` | A stop-to-stop loop: halt at the next stop and the server decides how many board and alight (0 to 80 waiting) and the fares. **Nobody is visible**: passengers are numbers in the HUD. |
| Interaction points on the models | `docs/kano/TRANSPORT_KIT_catalog.json` → `sockets` | Already authored on the models and **read by nothing**: <ul><li>station pumps 1 to 4, entry and exit</li><li>warehouse rear docks (6 on the hall, 3 on the cold store)</li><li>terminal bus bays 1 to 4</li><li>shelter boarding points</li><li>minibus loading points</li><li>the truck fuel island's two stops</li></ul> |
| Placement | Frontage engine and route props | Deterministic: every client computes the same buildings at the same places. Their sockets can therefore be computed on every client as well, with nothing sent over the network. |

## 2. The proposal, in four phases (each one shippable and testable on its own)

### P1. Refuel at a pump (client only, no server change)
* **Service points.** A pure, tested `NetCore/ServicePoints` index turns every placed prop that has sockets into world points with a heading: route props, frontage lots and site plates. It is computed per chunk with the rest of the chunk.
* **Driving in.** Stop beside a pump socket (within 2.5 m, under 1 km/h, any heading along the island). The HUD offers **Refuel** (a key or a tap) with the litres needed and the price from the existing server quote.
* **Paying.** Confirming calls the **existing** `buy_fuel` RPC. The server keeps the price, the money and the litres exactly as for the Garage button.
* **What you see.** The nozzle lifts from the holster and the hose reaches the filler cap (a small client-side animation). The pump display and the HUD count the litres up over a few seconds; the engine must be off. Paying at the mart is not required in P1.
* **Also:** the truck fuel island (`aro_fuel_island_truck`) and every station on the Kano routes (16 today) work from day one, because they already carry pump sockets.
* **Tests:** NetCore unit tests (socket to world transform, the trigger rule, nothing is triggered on the carriageway), and a CI smoke page that drives a truck onto a pump, refuels, and checks the litres and the screenshot.

### P2. People you can see (client only, no server change)
* **People assets.** A new Blender family, **built by code like every asset** (no third-party model): about eight adults and two children in Kano dress (kaftan and cap, babban riga, hijab and wrapper, shirt and trousers, a market woman with a head-load, a schoolboy), around 1,500 triangles each with three LODs. Each gets a walk cycle and an idle, baked to a **vertex-animation texture**, so a crowd costs one or two draw calls (GPU instancing) rather than one skinned mesh per person.
* **Crowd planner.** A pure and deterministic `NetCore/Crowd` planner (seeded per chunk, so every client sees the same crowd) places people on walkways and in forecourts: at terminals, shelters, shops, markets and mosque gates, and at stations by the mart. Density follows the district and the hour. **Nobody walks on the carriageway.** In P2 people are visual only: no collision with vehicles, and they step aside along their path.
* **Bus terminals.** Each bay and shelter shows waiting people. When your bus halts and the server answers *boarded N, alighted M*, N figures walk to the door and disappear into the bus, and M step out and walk away. **The bus fills before your eyes**, with the counts the server already decides.
* **Budgets** (the established ones): at most 60 people visible on desktop and 20 on low tier, plus 1 or 2 draw calls, within the 400 desktop and 150 mobile draw budgets measured by the review runs.
* **Tests:** planner determinism (chunks agree with the whole), never on a road, budgets, boarding counts equal the server reply; smoke screenshots of a terminal and a stop.

### P3. Warehouses and terminals as real job ends (needs server data: Supabase)
* **Server data.** Kano **locations** at the warehouse docks and the terminal bays of the Kano routes; **cargo jobs** between them, for example from the logistics yard to the wholesale depot, the supermarket or the stations' tanker deliveries; a Kano **bus route** between the terminal and the shelters. This is seed data and a migration in `supabase/`, which the rules forbid without your approval: it is listed separately so that you can approve P1 and P2 without it.
* **Docking (client).** To unload, reverse to a dock socket: rear within 2 m, heading within 20 degrees. A forklift and pallets animate the unloading. The job completes through the existing `Deliver` call.

### P4. Life around the city (client only)
Ambient traffic of people on every district's walkways, crowds at mosques at prayer times (from the in-game clock), market bustle, attendants at the pumps, a security man at bank and hospital gates. This uses the P2 planner and assets; nothing new on the server.

## 3. What does not change
Driving physics, vehicle behaviour, the weather, road generation, production configuration and multiplayer synchronisation (everything new is deterministic or comes from existing server replies). P1, P2 and P4 touch no server code. Kano stays on its test routes until you decide to put it in the player's route selection.

## 4. Order and size
P1 refuel at pumps first: the most "game" for the least risk. Then P2 people and the bus terminal. P3 needs your approval of the server data. Each phase ships with its tests, a review run with in-game pictures, and an honest report.

## 5. Decisions asked of the owner
1. Approve P1 (refuel at pumps) and P2 (visible people, the bus filling up): both client-side only.
2. Approve P3 (Kano jobs, docks and bus route in the server data, a Supabase migration).
3. People made by code in our pipeline (no licence questions; realistic proportions and dress, but not photoreal faces), or a CC0 base-human source to be allowed through the network policy for more realism.
