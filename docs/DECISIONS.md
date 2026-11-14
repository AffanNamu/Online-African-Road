# Decisions
- Economy/progression only via SECURITY DEFINER RPCs; client tables are read-only under RLS. Rewards come from the `jobs` row, never from the client.
- Delivery validation: time vs. vehicle top speed, driven distance vs. route, final position within 150 m, vehicle-state sanity, single-use idempotency key.
- Realtime vehicle sync is Unity Netcode/Relay only; Supabase holds persistent state (never positions).
- Vehicle visuals are swappable behind `VehicleRig`; physics/economy are data (`VehicleDefinition` / `vehicle_definitions.stats`).
- No local fake backend: unconfigured = offline notice, not simulated success.
- Known limit: client-reported distance/position can still be forged within plausible bounds; server-side telemetry sampling from Netcode is the planned hardening.
- **2026-11-14, owner (answers to the functional-places proposal, `docs/FUNCTIONAL_PLACES_PROPOSAL.md`):**
  * P1 (refuel at a pump: stop, Refuel, the existing `buy_fuel` charges) and P2 (visible people at terminals, stops, shops, markets and mosques; boarding passengers walk into the bus) are **approved**. Both are client-only, with no server change.
  * P3 (Kano locations, cargo jobs at warehouse docks and a Kano bus route in the Supabase data) comes **later, after P1 and P2**.
  * People are **built by code** in the Blender pipeline: no third-party human models.
  * **Kano is to be added to the player's route selection** (a production-configuration change the owner approved).
  * The rule above holds for refuelling too: no fake backend, so with no server the game says it is offline, it does not fill the tank.
- **2026-11-14, owner:** `buy_fuel` may take the game's current fuel reading and use it when it is LOWER than the stored one. A pump can then refuel mid-job; claiming less fuel only means paying for more litres. This is a Supabase migration with database tests, approved as the one server change of P1.
