# Refuel at a pump (P1 of the functional places, approved by the owner on 2026-11-14)

The owner's words: *"The filling station ... cars will enter and refuel. It's not just a building."* Decisions recorded in `docs/DECISIONS.md` (2026-11-14). The proposal is `docs/FUNCTIONAL_PLACES_PROPOSAL.md`.

**State:** level 1 (implemented) and level 2 (automated tests: database, .NET, API contract). Level 3 (Unity compile and the browser runs) waits for CI. Level 4 (seen in gameplay) and level 5 (owner review) are not reached. **Nothing is approved.**

## 1. What the player does

1. Drive into a station and stop beside a pump island. A pump counts when the vehicle's centre is within 5 m of the pump point, on the same level (2 m), and the vehicle is stopped (under 1 km/h).
2. The HUD shows `Pump 2  ·  F  Refuel 63 L for 126 coins` and a green **REFUEL** button (touch and mouse). The litres come from the game's own fuel gauge; the price per litre comes from the same server quote the Garage uses.
3. Press **F** (gamepad **B / east**, or the button). The game asks the server to sell a full tank (`buy_fuel`). **The server decides the litres, the price and the money.** Only when it has answered does the pump run.
4. The nozzle leaves the pump and the hose reaches the filler on the pump side of the vehicle. A meter above the dispenser and the HUD count the litres and the coins. The tank gauge rises to the level the server answered (2 s plus 1 s per 25 L, at most 6 s).
5. Driving off during the animation stops it. The litres were already paid for, so the tank is set to the server's level.

**Without a server** (the public test build and the review build bake none in) the game says *"Offline: fuel is paid on the server and there is no connection. The tank was not filled."* and fills nothing. This follows the rule *no fake backend* (`docs/DECISIONS.md`).

**Not modelled.** There is no engine on or off: the vehicle must only be stopped. The pump attendant, the mart and paying at the till are not modelled.

## 2. Where the pumps are

| Station (catalogue id) | Pumps | Where |
|---|---|---|
| `kano_fuel_station_large_a` (after R-040) | 4 | Kano city routes, every road class with stations |
| `kano_fuel_station_medium_a` | 2 | Kano city routes |
| `rg_ngn_fuel_station_small_a` | 1 | Kano city routes |
| `fuel_station` (the development placeholder of the shipped route) | 2 | Lagos to Ibadan, km 20.5 (`fuel_aro_km20`) |

Every pump on these routes is placed at the start (`ServicePointsTests`: 12 or more stations on E1, 4 or more on F1, and the Lagos station).

**Correction to the proposal.** The truck fuel island (`aro_fuel_island_truck`) carries pump sockets, but it is not placed on any route of the game today. It therefore does not work in the game yet.

### Data flow
* **Sockets.** The pump points are authored on the Blender models (`transport_kit/fuel.py`, `extra(... sockets=...)`). They are written to `docs/kano/TRANSPORT_KIT_catalog.json` (`sockets`) and copied to the game catalogue `Resources/Props/prop_catalog.json` by `tools/kano/make_city_routes.py`. A socket's `yawDeg` points from the pump point towards the island.
* **Collision.** A station used to be one solid box over its whole lot, so a vehicle could not enter it. The places a vehicle drives into now carry `boxes`: the model's own collision boxes (shop, canopy columns, islands, totem, walls), from the new `collision` section of the transport catalogue (`build_transport.collision_section`, in game axes). `PropBatcher.BoxColliders` builds one collider per box. A production prefab without its own colliders gets the same boxes. Every other building keeps its one box (tested).
* **Public build.** The development placeholder of a Kano station is drawn from the same boxes, with a canopy slab over the columns, a thin forecourt and the pump faces. What is drawn is what is solid. The shipped route's placeholder station keeps its old look and gets boxes and two pump sockets beside its two pumps.
* **Runtime.** `NetCore/ServicePoints.cs` is pure and tested. It turns placed props into world points (`FromInstances`), applies the trigger rule (`PumpAt`), the price estimate (`Cost`, the server's formula) and the meter (`Meter`). It also writes the HUD wording. `ChunkStreamer` keeps the points of each loaded chunk and answers `PumpAt` around the vehicle. `Game/PumpStop.cs` runs the prompt, the purchase, the hose and the meter. Everything is deterministic and computed on every client; nothing new is sent over the network.

## 3. The one server change (approved): `buy_fuel` takes the game's fuel reading

Migration `supabase/migrations/20261114000001_pump_refuel.sql`:
* `buy_fuel(p_vehicle, p_liters, p_current_l default null)`. The reading is used **only when it is lower** than the server's own level: claiming less fuel only means paying for more litres. A higher reading, no reading or NaN changes nothing; a negative reading is refused (`invalid_reading`). The Garage's two-argument call is unchanged.
* **Mid-job, never twice.** `complete_job` and `serve_stop` deduct the whole verified distance's fuel when the job or bus run completes. So the server's level at the pump counts what the activity in progress has already burned. The litres bought are stored on top of that pending deduction. The tank ends the activity at: the level at the pump, plus the litres bought, minus the fuel burned after the pump.
* **Abandoned or failed**: nothing is deducted (as before), and the tank is capped at its capacity (a trigger), so a refuel mid-activity never leaves more than a full tank.
* The price (`ceil(litres x 2)`), ownership, row lock, ledger row (`FUEL_PURCHASE`) and the answers `tank_full` and `insufficient_funds` are those of the original. The answer adds `fuel_l`, the level right after the pump.

## 4. Tests

| Where | What |
|---|---|
| `supabase/tests/54_pump_refuel.sql` | A lower reading is used, a higher one ignored, NaN ignored, a negative one refused, the Garage call unchanged, refusals change nothing. A real job refuelled halfway and completed: the tank is the level at the pump minus only the burn after it. A job refuelled and abandoned: capped at full. A bus run refuelled mid-run: the run's burn counted, priced on those litres, capped when abandoned. One `buy_fuel` with three arguments; the trigger function is not callable. |
| `supabase/tests/mutation.py` | The three `buy_fuel` mutants (search_path, ownership, row lock) now break the new definition. Six new ones: a higher reading believed, a negative reading accepted, the job's burn ignored, the bus run's burn ignored, the cap trigger removed, anon granted. All killed. |
| `tools/check_api_contract.py` | `buy_fuel(pump)` against the client's `FuelPurchase`. |
| `tests/NetCore.Tests/ServicePointsTests.cs` (20) | Every station has pumps and its own boxes; only the four stations replace their footprint box. A 2.4 x 8 m truck fits beside every pump of every station without touching a box. The validator refuses bad sockets and boxes. The socket follows position, yaw and scale. The trigger needs stopped, near and the same level. Every pump on E1, F1 and Lagos is off the carriageway and its 5 m trigger never reaches a road. The first pump is found deterministically. The drive-in collision. The price and meter formulas and the HUD wording. |
| `tests/NetCore.Tests` (existing) | The placeholder triangle budgets and LOD order; no emissive geometry on a prop without a light; every key read is listed in Controls (F, gamepad B). |
| `tools/blender/tests/test_transport_kit.py` | The catalogue's `collision` and `sockets` sections are those of the specs (game axes). |
| `tools/webgl-pump.js` (CI, public build; review build with the real models) | `?smoke=drive&pump=1`: the truck starts beside the first pump, lands, the game says it is at the pump, and the Refuel button shows. F gives the offline notice, nothing is sold and the gauge does not move. Informational until two runs agree. |
| `tools/webgl-e2e.js` section 2p (CI, real backend) | The signed-in player's own truck, with 30 L in the database, beside the shipped route's station. F buys 90 L for 180 coins; the pump runs to 120 L; the database holds 120 L, the wallet paid 180 and one ledger row was written. Screenshots of the prompt, the hose and the full tank. Informational until two runs agree. |

## 5. Not done (honest list)
* The truck fuel island is not placed on any route (see section 2).
* The production station prefabs keep their 0.12 m forecourt slab without a collider, so wheels sink 0.12 m into it visually. The asset needs a collision box for the slab: a new asset version, not done.
* No queue or other vehicles at the pumps, no attendant, no payment at the mart (P4).
* The prompt says `F` on every device; the button is the touch control.
