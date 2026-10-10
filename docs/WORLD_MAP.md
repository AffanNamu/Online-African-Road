# World map (strategic UI)

The world map is the **strategic menu** where the player sees West Africa, picks a city or route and reads what is available. It is a 2D UI feature.
It is **not** the 3D driving world, shares no code with the road, terrain, vehicle or traffic systems, and must never be stretched over terrain.

## Where things live
| Piece | Path |
|---|---|
| Artwork | `Assets/_Project/Resources/WorldMap/west_africa_world_map.jpg` |
| Data | `Assets/_Project/Resources/WorldMap/world_map.json` |
| Pure model, validator, great-circle maths (unit-tested) | `Assets/_Project/Scripts/NetCore/WorldMap.cs` |
| Screen (zoom, pan, markers, panel) | `Assets/_Project/Scripts/Game/WorldMapScreen.cs` |
| Import settings | `Assets/_Project/Scripts/Editor/WorldMapTextureImporter.cs` |
| Tests | `tests/NetCore.Tests/WorldMapTests.cs`, `Assets/_Project/Tests/EditMode/WorldMapAssetTests.cs`, `tools/webgl-worldmap.js` |

Entry points: the dashboard's **Map** nav item, the **Explore Map** card and the **Popular Routes** panel (which draws from the same artwork and data).

## Data model (authoritative vs illustrative)
* `cities[].lat/lon` are authoritative. Lagos and Ibadan are asserted equal to the backend `cities` rows in `supabase/seed.sql` by a test.
* `cities[].mapX/mapY` are pixel anchors on the **illustrative** artwork. The picture is stylised and not to scale (a cubic fit of lat/lon to pixels still misses by about 23 px RMS), so nothing is ever computed from pixels. Distances come from coordinates.
* A route is the ordered list of city ids in `via`; its geography is the great-circle legs between them. `roadKm` is given only where it is known (Lagos to Ibadan, 128 km). Other routes show their straight-line distance and "not surveyed yet".
* `status`: `planned` (not built), `prototype` (placeholder 3D exists), `live` (production 3D exists, none today). A planned route may not name a `gameRouteId`; a prototype or live route must, with a road length. The validator enforces this, so a route cannot be made to look playable by accident.
* `unlockLevel` values are design placeholders. Locked routes show what level reveals them; they are never playable regardless of level.

## Adding a route
1. Add the cities (id, country, lat, lon, pixel anchor, and `locationSlugPrefix` if backend locations exist) and the route to `world_map.json`.
2. `dotnet test tests/NetCore.Tests` validates it (coordinates inside West Africa, anchors on the artwork, east-west and north-south order kept, road length not shorter than the straight line, etc.).
3. Mark it `prototype` only when a playable environment really exists, and give it its `gameRouteId`.

## Controls
Mouse wheel zooms about the cursor; drag pans; click a city or a route line; `+`/`-` or `E`/`Q` zoom; arrows or WASD pan; `0`/Home fits; Esc goes back. Two-finger pinch zooms on touch screens and one finger drags. Zoom is 1x (whole artwork visible, aspect preserved, letterboxed) to 8x.

## Performance
The artwork is block-compressed per platform (DXT1 WebGL, ASTC 6x6 mobile), about 1 MB on the GPU with mipmaps. Markers and lines are a few dozen UI elements.

## Verification status
Verified in CI: data validation and model rules (xunit), JSON parsing through `JsonUtility`, texture size and import settings (Unity EditMode), and the browser scenario `tools/webgl-worldmap.js` (image loads, aspect ratio on four screen shapes, wheel zoom, drag pan, city and route selection, planned routes offer no way to drive, Back, dashboard entry, Escape).
Not verified: real touch devices, real GPUs, and end-to-end job acceptance from the map against the live backend.
