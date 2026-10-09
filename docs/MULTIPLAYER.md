# Multiplayer

## Architecture (as implemented)
- **Transport/session:** Unity Multiplayer Services *Sessions* with Relay (`SessionOptions.WithRelayNetwork()`), Netcode for GameObjects 2.x.
  The SDK starts the scene's `NetworkManager` (host on create, client on join). Max 8 players.
- **Avatars:** the local player's physics vehicle is *not* a network object. A separate owner-authoritative `NetworkVehicle`
  avatar follows it (`OwnerNetworkTransform`, interpolated) and publishes 5 bytes of state at 10 Hz (lights, indicators, horn,
  brake, reverse, speed, damage), plus display name and vehicle definition id. Remote players are non-colliding visual puppets.
- **Convoy = Supabase record + live session.** Membership, leader and the join code live in Postgres behind RLS; the code is
  released only to members (`get_convoy_session_code`). Positions never touch Supabase.
- **Identity in a session is cosmetic.** UGS auth is anonymous; names are self-declared. Rewards never depend on session data:
  each player's own telemetry is validated server-side (see SECURITY.md).
- **Lifecycle:** `SessionLifecycle` (pure C#) decides reconnect vs give-up; `SessionService` performs the SDK calls
  (`ReconnectToSessionAsync`, exponential backoff 1 s .. 15 s, 5 attempts). Host leaving ends the session (no host migration).

## Verification status
| Piece | Status |
|---|---|
| Session lifecycle, reconnect policy, state codec, roster (`Assets/_Project/Scripts/NetCore`) | **VERIFIED**: 49 xunit tests pass in CI (.NET 8). |
| Convoy session-code privacy (SQL) | **VERIFIED**: matrix tests + 6 mutants, Postgres 16 locally and Postgres 15 in CI. |
| `NetworkVehicle`, `OwnerNetworkTransform`, `SessionService`, `ConvoyService`, UI, scene wiring | **WRITTEN, NOT COMPILED, NOT RUN.** Blocked on the Unity CI license (see BUILD.md). API names for Multiplayer SDK 1.x / NGO 2.x were written from memory and may need adjustment at first compile. |
| 2 players / 4 players seeing each other, convoy, disconnect, reconnect | **NOT TESTED** (needs a Unity build and two clients). |

## Known gaps
- No vehicle-vehicle collision between players; no server-authoritative position (planned: host-side plausibility checks).
- No host migration; no join ticket proving Supabase identity to the host.
- Shared-job start/finish is not coordinated through the session yet (each member's job is validated individually; +10% per extra
  participant is applied server-side).
- Dependency versions in `Packages/manifest.json` are unverified until the first Unity import.
