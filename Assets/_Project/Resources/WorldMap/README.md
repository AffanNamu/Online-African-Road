# World map assets

| File | What it is |
|---|---|
| `west_africa_world_map.jpg` | The supplied strategic-map artwork, 1672x941, padded with 3 replicated rows to 1672x944 so it can be block-compressed. Illustration only. |
| `world_map.json` | The authoritative data: cities (lat/lon), routes (ordered city ids), statuses and unlock levels, and the artwork's pixel anchors. |

Import settings are applied automatically by `Scripts/Editor/WorldMapTextureImporter.cs` (DXT1 for WebGL, ASTC 6x6 for mobile, mipmaps, clamped, not readable).
Full documentation: `docs/WORLD_MAP.md`. To replace the artwork keep the same file name, or change `image.file` in the JSON, and update the pixel anchors.
