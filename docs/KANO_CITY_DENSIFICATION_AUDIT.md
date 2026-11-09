# Kano city densification and mosque integration: audit and plan (Phase 1, 2026-11-09)

Written for the owner's directive "ARO: Kano city densification and mosque integration". **Phase 1 is an audit: what exists, what the game can and cannot do today, what is empty.** The plan and its limits are at the end. Nothing here claims visual approval.

## 1. What "Kano" is in the game today (the most important finding)

| Thing | State |
|---|---|
| Production routes the game ships | **One: `ng-lagos-ibadan`** (Lagos to Ibadan). There is **no production Kano road network**. |
| Kano routes in `Assets/_Project/Resources/Routes/` | `ng-kano-e1-streets` (1.5 km engine **test corridor**: arterial, collector, local and service profiles, a four-way crossing, a logistics yard and a bus terminal on site plates), `ng-kano-e5-roundabouts` and `ng-kano-e5-f1` (isolated **E5 test routes**: six roundabouts; the flagship interchange with a deck over a roundabout, drivable upper and lower roads, ramps). All reached only by `?smoke=drive&route=...` and the CI checks. |
| Kano master plan (`KANO_CITY_MASTER_PLAN.md`) | 11 named districts (D01 to D11) and 3 filler zones; polygons and plot counts are **planning assumptions**. District geography is **REAL_ANCHORED** (D01 old city, D02 palace and **Central Mosque precinct**, D04 Dala Hill: all **blocked on references, never to be invented**), **INDICATIVE** (D03, D05, D08, D09, D10, D11) or **FICTIONAL** (D06 modern estate, D07 traditional suburb). A draft route `ng-kano-slice.route.draft.json` exists in docs, not wired. |
| What lines the E1 and E5 roads | **The game's grey placeholder props** (`concrete_house`, `shop_row`, `warehouse`, `kiosk`, `bus_stop`, `junction_mast`, palms) scattered by `PropRule`s; the island props of the E5 rings are the same placeholders. The Blender-built Kano assets (villas, Hausa compounds, shop rows, the transport kit, the family D pieces) **appear only in the private review scenes**, never along a driven road. |

**Consequence.** "Populating Kano" cannot mean editing a finished city: the city's roads are test corridors and the production network does not exist. What can be done, **inside the existing architecture and without silently changing production road data**: (a) give the game a deterministic, footprint-aware way to line *any* road with varied buildings; (b) make the Kano test corridors (E1 streets, the E5 flyover route) the proving ground, populated with the real assets, so the driver's-seat, flyover and street views can be shown from the actual game build; (c) build the missing mosque family. Connecting any of it to a production Kano network is a separate decision (E5 safeguard: isolated test routes first).

## 2. Assets that exist (registry, `ARO-CONTENT` branch `assets/transport-kit-v1`)

Built and usable as buildings: two villas (`aro_villa_2storey_hip_a/b`), three Hausa-style compound assets and a wall-and-gate set, three rural houses (mud-brick a/b, block with corrugated roof), a roadside shop row, a market shed, stalls and a kiosk, three warehouses and a cold store, a bus terminal hall, a truck fuel island, the compound-wall, pier and gate kit, the transport kit's furniture, the monument and island kit. **Mosques: none.** Registry entries `kano_neighbourhood_mosque_a` (blocked on M-03) and `lmk_kano_great_mosque` (blocked on M-11, a real landmark: **not touched**). Missing from the directive's list: mid-rise offices, hotels, shopping complexes, schools, hospitals, police stations, government buildings, restaurants, workshops, car dealerships (hospital and police have approved concepts R-010 and R-011; the rest have none: M-02, M-04, M-10).

## 3. The placement system and its limits (read from the code)

`PropScatter.PlaceChunk` (NetCore, pure C#, deterministic: `Mathx.Hash01(seed, k, salt)`) places roadside props **per `PropRule`** along the **main corridor only**: slots every `spacing`, an `offset` from the formation edge, a `presence` probability, yaw towards the road. Chunks are 200 m; instances belong to the chunk of their origin; the Unity layer stages production prefabs per instance (LOD by ring: 0, 1, 2) and merges dev placeholders. Limits that matter for a dense city:

1. **No footprint test.** A prop is kept if its *origin point* is outside the road formation plus a 3 m margin (`InsideFormation(x, z, margin)`). A 40 m mosque whose origin is 6 m from the road has its façade on the carriageway.
2. **One asset per rule**, no weighted family: variety needs many rules and cannot guarantee a mix.
3. **No lot logic.** Slots are independent: two rules (a house and a shop) can overlap each other; spacing is a guess, not a footprint.
4. **Main corridor only.** Side roads, `RoadSpec` roads, ramps and rings get no scatter (they have hand-placed props and site plates only).
5. **No anchors.** Nothing guarantees a mosque (or any civic building) every so often; a random presence can leave 2 km without one.
6. **Slope and terrain are ignored** (the prop snaps to the terrain height at its origin; a footprint on a slope floats or sinks).
7. **No deck awareness off the main road**, no height-aware view logic: nothing makes a building visible from a flyover.
8. **Catalogue**: the shipped `prop_catalog.json` has 19 development placeholders; none of the Kano kits is in it. A production prefab replaces a placeholder of the *same id*, so a new id needs a catalogue entry (text data) before a rule can use it; without the private prefab the game draws a placeholder sized from the entry.

## 4. Empty areas today

Everything: along the E1 corridor the sides are sparse placeholders; the E5 roundabouts and the F1 flyover stand in open ground with grey props on the islands; no road has a mosque. The F1 deck looks out over bare terrain, the opposite of R-039.

## 5. The references (what the images require; `REFERENCE_REGISTER.md` section 2d)

R-037 and R-038 (large mosque beside a divided road), R-039 (driver's view from a flyover, skyline with a mosque, mid-rise blocks, a tower, street life below); the fourth image is **R-034** (twin-arch monument roundabout, mosque, commercial building), already registered (same SHA-256). **The mosque is a generic design, not the real Kano Central Mosque (owner decision C1).** My family D monument (a shaft with four arches) does **not** follow R-034's twin-arch gateway: it is rebuilt as a new version in this work.

## 6. Plan (and what will not be claimed)

1. **Mosque family** (Blender, `transport_kit/mosque.py`, same palette, 4-material and LOD rules): two *grand* mosques after R-037 and R-038 (CITY_SPECIFIC, generic), three medium and three small neighbourhood mosques (REGIONAL, `rg_ngn_`), two traditional earth-render mosques (REGIONAL, Hausa-inspired, documentary sources R-024 to R-029, a flagged interpretation), each with its boundary wall and railing; the monument rebuilt after R-034. Registered, staged, validated.
2. **Frontage placement system** (NetCore, `FrontageRule`): deterministic **blocks and lots** along any corridor and side; each lot takes a building from a **weighted mix** (a road class's families), sized by its footprint, **set back** from the formation, **rejected** if any footprint corner is inside any road, site or island (plus a clearance), on a deck, or on a slope beyond a limit; **anchors** (a civic building every N blocks: the mosques); yaw from the road; no overlap by construction. Pure C# with xunit tests (determinism, no overlap, clearance, anchor spacing, mosque presence per road class).
3. **Catalogue entries and rules** for the E1 corridor (single carriageway local street, collector with shops, arterial) and the F1 flyover route (both sides of the flyover, the legs), using **only assets that exist or are built here**. Families with no asset (hotels, schools, government, restaurants, dealerships, workshops) are **reported as gaps, not faked**; two commercial buildings (a 4-storey shopping parade, a 7-storey office block) are built from R-034, R-039 and R-022 as flagged generic interpretations.
4. **Pictures** from the real WebGL build (CI review run, private assets staged): ordinary single-carriageway road with a mosque, a commercial road, a residential street, an arterial, the flyover from the cab, and a wide view, each labelled with district, road and camera. **Only CI can run Unity**; the pictures arrive after the run.
5. **Not claimed**: visual approval, a production Kano network, the real Central Mosque or any real landmark, that the density is final, performance on a real GPU, multiplayer sync beyond determinism (the same seed gives the same placement on every client; tested).
6. **Where the work goes.** Branch `claude/intelligent-faraday-sykb0x` is 38 commits ahead of `main` (fast-forwardable). The owner asked for tested work on `main`; per the handover `main` is moved **once**, by fast-forward, into an empty Unity-licence queue after the branch's CI is green. See the final section of the report.
