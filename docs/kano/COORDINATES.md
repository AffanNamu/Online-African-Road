# Kano coordinates: provenance, verification, recommendation and impact

**Status: investigation and recommendation only. No coordinate in the game, the backend or any route was changed.** The existing world-map value (12.0022°N, 8.592°E) and the proposed reference (about 12.0°N, 8.5167°E) were both kept as found.

## 1. Where the existing coordinate lives, and where it came from
| Location | What it is | Consequence of changing it |
|---|---|---|
| `Assets/_Project/Resources/WorldMap/world_map.json` (city `kano`) | the strategic map's city pin; `docs/WORLD_MAP.md` calls city `lat/lon` "authoritative" | the map's Haversine distances change (section 5) |
| `supabase/seed_world.sql` line 43 (`'NG', 'Kano', 12.0022, 8.592, 'kano', ...`) | a row of the backend world table, which is **production data** once the seed has run | a data migration on the live database: needs the owner's explicit authorization |
| `Assets/_Project/Tests/EditMode/ApiSamples/get_world_state.json` | the sample of the `get_world_state` RPC output used by the API-contract check | must change in the same commit as the seed |

**Provenance.** `git log -S` finds the value arriving with the whole world map (commit `4973744`, 2026-10-10) and with the backend seed (`a0d9b3f`, 2026-10-10). **Neither commit message nor any file records a source.** `docs/WORLD_MAP.md` asserts only Lagos and Ibadan against the backend (a test), so nothing ever checked Kano's value against the real world.

## 2. What the existing value is
12.0022°N, 8.592°E matches a Kano point published as **12.00218°N, 8.59196°E, altitude 469 m** ([distancefromto.net](https://www.distancefromto.net/distance-from-kano-to-epe-ng) lists it; the search summary noted it is "a different location within the Kano area, not the standard reference coordinate"). So it is a real point in the Kano area, taken from a place-lookup listing, and it is about **8.2 km east** of where Kano's historic and administrative centre is placed. The repository does not say whether that was intended.

**It is not a Kano-only slip.** Comparing every Nigerian and Nigerien city pin in the game with the GeoNames city point (WGS84 geodesic distance, computed with PROJ):

| City | Game pin (lat, lon) | GeoNames (lat, lon) | Offset | Direction of GeoNames from the pin |
|---|---|---|---|---|
| Lagos | 6.5244, 3.3792 | 6.45407, 3.39467 | 8.0 km | 168° (SSE) |
| Ibadan | 7.3775, 3.9470 | 7.37756, 3.90591 | 4.5 km | 270° (W) |
| Benin City | 6.3350, 5.6037 | 6.33815, 5.62575 | 2.5 km | 82° (E) |
| Port Harcourt | 4.8156, 7.0498 | 4.77742, 7.01340 | 5.8 km | 224° (SW) |
| Abuja | 9.0765, 7.3986 | 9.05785, 7.49508 | 10.8 km | 101° (E) |
| Kaduna | 10.5105, 7.4165 | 10.52641, 7.43879 | 3.0 km | 54° (NE) |
| **Kano** | **12.0022, 8.5920** | **12.00012, 8.51672** | **8.2 km** | **268° (W)** |
| Niamey | 13.5116, 2.1254 | 13.51366, 2.10980 | 1.7 km | 278° (W) |

All eight differ by 1.7 to 10.8 km in different directions. The pins read as locality points from a map-lookup service, not as a reference convention. Changing Kano alone would make one city inconsistent with the rest.

## 3. Verification of the proposed reference
| Source | Value | Note |
|---|---|---|
| **GeoNames**, id 2335204 "Kano" (the offline dataset in the `geonamescache` 3.0.2 package; the `reverse_geocoder` 1.5.1 package returns the same entry for both candidate points) | **12.00012°N, 8.51672°E** | open dataset; chosen as the canonical anchor |
| Wikipedia "Kano (city)" | 12°00'N 8°31'E (= 12.0, 8.5167) | 13 m from GeoNames |
| OpenStreetMap city node (via the Mapcarta listing, a secondary source) | 11.994°N, 8.522°E | 0.9 km from GeoNames: the OSM place point sits slightly south-east |
| Gidan Rumfa | 11.9923°N 8.5160°E (Wikipedia 11°59'32"N 8°30'58"E; Plizio 11.9933 8.5161; AroundUs 11.9923 8.5160); a heritage inventory 8.519°E | within 300 m of one another; **0.88 km from the anchor** |
| Dala Hill | 12.0093°N 8.5070°E (one source) | 1.47 km from the anchor |

Every historic landmark lies within 1.5 km of the GeoNames/Wikipedia anchor and 8.3 to 9.3 km from the game's pin. The WGS84 geodesic distance from the pin to the anchor is **8.20 km at bearing 268°** (so the anchor lies due west of the pin).

**Limits of this verification.** The direct web services (OpenStreetMap Nominatim, Wikidata, the GeoNames API) are **not reachable from this session** (their hosts are outside the environment's allowed network domains), so the check used the offline GeoNames dataset, web listings and the PROJ geodesic library. If you want OpenStreetMap or Wikidata read directly, add `nominatim.openstreetmap.org`, `www.wikidata.org` and `api.geonames.org` to the environment's allowed domains (cloud environment menu, then Edit, Network access) and I will repeat the comparison.

## 4. Recommendation
**Do not replace either number.** Separate three things the repository currently conflates:

1. **Canonical geographic anchor (WGS84 / EPSG:4326)**: `kano_city_anchor` = **12.00012°N, 8.51672°E** (GeoNames, cross-checked above), tolerance about 1 km because it is a place point, not a survey benchmark. Landmarks keep their own anchors with their own sources (`kano_world_frame.json`: Gidan Rumfa has three agreeing web listings, Dala Hill one; Kurmi Market and Kofar Mata have no verified coordinates and say so).
2. **Transform into Unity world coordinates (`K0`)**: a local tangent plane at the anchor (x east, z north, metres, origin at the anchor, chunk-lattice aligned). Measured against the WGS84 geodesic with PROJ the error is 0.4 m at 1 km, 5.1 m at 10 km and **17 m at 25 km (0.05%)**: fine for city-scale slices. Beyond about 25 km do not extend `K0`: use UTM zone 32N (EPSG:32632; 8.5°E is inside zone 32) per corridor, or per-city frames with explicit connectors. City slices are 1:1; a compressed corridor (the shipped Lagos-Ibadan route is compressed) declares its compression and never shares a frame with 1:1 content.
3. **The world-map pin**: keep it as a **map pin** (a UI position for choosing a city), not as a geographic anchor, and never derive geometry from it. If you later want pins to be anchors, that is a data change for all eight cities at once (section 6), not a Kano edit.

The decisions are recorded in `docs/kano/kano_world_frame.json` (status PROPOSED). Nothing depends on it until you approve it.

## 5. Impact on existing and future road corridors
| Item | Impact |
|---|---|
| **Existing Lagos-Ibadan corridor** | **None.** Its control points are local metres from the Apapa depot, "compressed" by design (`meta.notes`); it uses no latitude or longitude. |
| World-map route lengths | The map computes great-circle lengths from city pins (`Geo.HaversineKm`). With the GeoNames Kano point: **Kaduna to Kano 209.6 km becomes 204.5 km (-2.4%)**, **Kano to Niamey 721.1 km becomes 713.2 km (-1.1%)**. Neither route has a `roadKm` in the data. |
| Tests | No test asserts Kano's coordinates; the Kano tests assert city state and unlock levels only. |
| Backend | `seed_world.sql` is the live world table: a change is a production data change, to be authorised separately and made together with the API sample. |
| **Future corridors** | A Kaduna-Kano corridor should end at the Kano anchor in `K0` and start at Kaduna's own anchor, with an explicit declared compression (it is 200 km, far beyond the 25 km tangent-plane limit). The 1.65 km slice is a 1:1 city slice and is not part of that corridor's geometry. |
| The Kano slice | Authored in `K0` relative to the canonical anchor (start (-2400, -3200) = 11.97121°N, 8.49468°E, about 4 km south-west). **It does not use the world-map pin and is unaffected by any later pin decision.** |

## 6. What I propose, for your decision (nothing applied)
1. Approve the **canonical anchor and the `K0` transform** as the basis for all Kano content. (No existing file changes.)
2. Decide the **meaning of world-map `lat/lon`**: pin or anchor. If anchor, authorise a **city-anchor audit** of all eight pins (table in section 2) with a coordinated change of `world_map.json`, `seed_world.sql`, the API sample and, for Lagos and Ibadan, the `locations` seeds and the test that ties them to the backend. The change touches production data and would be authorised and migrated separately.
3. Leave Kano's pin untouched until step 2 is decided.
