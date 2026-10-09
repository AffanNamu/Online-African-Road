# Decisions
- Economy/progression only via SECURITY DEFINER RPCs; client tables are read-only under RLS. Rewards come from the `jobs` row, never from the client.
- Delivery validation: time vs. vehicle top speed, driven distance vs. route, final position within 150 m, vehicle-state sanity, single-use idempotency key.
- Realtime vehicle sync is Unity Netcode/Relay only; Supabase holds persistent state (never positions).
- Vehicle visuals are swappable behind `VehicleRig`; physics/economy are data (`VehicleDefinition` / `vehicle_definitions.stats`).
- No local fake backend: unconfigured = offline notice, not simulated success.
- Known limit: client-reported distance/position can still be forged within plausible bounds; server-side telemetry sampling from Netcode is the planned hardening.
