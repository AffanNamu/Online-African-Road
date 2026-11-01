#!/usr/bin/env python3
"""Generates the DRAFT data of the Kano vertical slice (nothing here is read by the game yet):
  docs/kano/ng-kano-slice.route.draft.json   a RouteSpec in the game's own schema (road corridor, junctions, scatter rules, set pieces)
  docs/kano/kano_asset_inventory.json        the planned prefabs (catalogue-shaped, validated by the game's PropCatalogValidator) and the Blender kit modules
  docs/kano/kano_world_frame.json            the proposed coordinate frame so other cities and corridors can connect
Every number below that describes a building or a road is a PLACEHOLDER ENVELOPE until it is set from references the owner confirms; the data says so.
Usage: python3 tools/kano/make_slice_draft.py [--check]   (--check fails if the committed files differ from what this script generates)"""
from __future__ import annotations
import json, math, sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
from make_reference_register import MISSING as REF_MISSING      # which missing reference each planned prefab depends on

ROOT = Path(__file__).resolve().parents[2]
OUT = ROOT / "docs" / "kano"
EXCLUSION = 3.0            # PropScatter.ExclusionMargin: a scatter pivot closer than this to a road formation is dropped

# ------------------------------------------------------------------------------------------------------------------ frame
LAT0, LON0 = 12.00012, 8.51672             # canonical Kano city anchor: GeoNames id 2335204 (read offline from geonamescache 3.0.2); Wikipedia 12°00'N 8°31'E agrees to 13 m
M_PER_DEG_LAT = 110_700.0                  # at 12 N (WGS84 meridian arc, rounded)
M_PER_DEG_LON = 111_320.0 * math.cos(math.radians(LAT0))
SLICE_START = (-2400.0, -3200.0)           # on the 200 m chunk lattice; about 4.0 km SW of the reference point (design choice, NOT a survey)
BEARING = 36.0                             # the approach from Kaduna (SW) runs towards the city (NE); bearing from the great circle Kaduna -> Kano is about 36 deg
QIBLA_BEARING = 68.5                       # WGS84 geodesic initial azimuth Kano anchor -> Kaaba (21.4225N 39.8262E) = 68.46 deg (PROJ via pyproj 3.8.0)


def to_xz(lat: float, lon: float) -> tuple[float, float]:
    return ((lon - LON0) * M_PER_DEG_LON, (lat - LAT0) * M_PER_DEG_LAT)


# ------------------------------------------------------------------------------------------------------------------ inventory
def P(id, cat, w, d, h, lods, collider="box", cull=300.0, shadow=True, tags=(), refs="", note=""):
    return {"id": id, "category": cat, "status": "dev", "description": "PLANNED, NOT MODELLED. Envelope dimensions are placeholders until set from owner-confirmed references." + (" " + note if note else ""),
            "source": "dev:" + id, "collider": collider, "heightM": h, "widthM": w, "depthM": d, "cullDistance": cull, "lodTriangles": lods, "castShadow": shadow, "tags": list(tags), "referencesNeeded": refs}

# Plot prefabs INCLUDE a 4.5 m open apron in front (drain slab / walkway / pole line); the compound wall starts behind it. depthM counts the apron.
INVENTORY = [
    # --- buildings (LOD0 budget 6000)
    P("kano_hausa_compound_a", "buildings", 18, 26, 5.2, [5400, 1700, 480, 100], tags=["traditional", "plot"], refs="street-front elevation, zaure/gate, parapet with zankwaye, wall finish, roof edge"),
    P("kano_hausa_compound_b", "buildings", 20, 28, 7.4, [5600, 1800, 500, 100], tags=["traditional", "plot"], refs="two-storey front block (sources: traditional Hausa buildings seldom exceed two storeys)"),
    P("kano_hausa_compound_c", "buildings", 13, 21, 4.6, [4300, 1400, 400, 90], tags=["traditional", "plot"], refs="small single-storey compound"),
    P("kano_compound_modern_a", "buildings", 20, 30, 6.2, [5800, 1900, 520, 100], tags=["modern", "plot"], refs="walled bungalow/villa with gate: front, side, roof, finish"),
    P("kano_compound_modern_b", "buildings", 20, 30, 8.6, [5900, 1900, 520, 100], tags=["modern", "plot"], refs="two-storey walled villa"),
    P("kano_shop_row_a", "buildings", 18, 12, 4.2, [3300, 1100, 320, 80], tags=["commercial", "plot"], refs="single-storey shop row: frontage, shutters, signage, awning"),
    P("kano_shop_row_b", "buildings", 20, 13.5, 7.4, [4700, 1500, 420, 90], tags=["commercial", "plot"], refs="two-storey shop-and-flat block"),
    P("kano_plaza_a", "buildings", 30, 26, 13.0, [5900, 1900, 520, 110], cull=450.0, tags=["commercial", "landmark"], refs="three-storey commercial plaza"),
    P("kano_apartment_block_a", "buildings", 22, 20, 10.5, [5300, 1700, 480, 100], cull=420.0, tags=["modern"], refs="three-storey block (owner to confirm which city the supplied aerials 2 and 3 show before they are used)"),
    P("kano_midrise_a", "buildings", 26, 24, 24.0, [5900, 1900, 540, 110], cull=650.0, tags=["modern", "landmark"], refs="six to eight storey office/hotel block"),
    P("kano_mosque_neighbourhood_a", "buildings", 30, 34, 24.0, [5900, 1900, 540, 110], cull=650.0, tags=["religious", "landmark"], note="Front (+Z) is the qibla wall.", refs="a real neighbourhood mosque type: dome(s), minaret(s), ablution, wall, colours"),
    P("kano_filling_station_a", "buildings", 28, 30, 7.0, [4800, 1600, 450, 100], cull=420.0, tags=["commercial"], refs="forecourt, canopy, shop, signage"),
    # --- signs (LOD0 budget 800)
    P("kano_road_sign_small", "signs", 1.2, 0.3, 3.2, [420, 140, 40], cull=200.0, refs="regulatory/warning/direction sign types and mountings"),
    P("kano_road_sign_gantry", "signs", 16.0, 1.2, 6.5, [780, 260, 80], collider="none", cull=450.0, note="Spans the carriageway: no collider over the road.", refs="overhead gantry as installed"),
    P("kano_billboard_a", "signs", 12.0, 0.8, 9.0, [700, 240, 60], cull=500.0, refs="billboard frame and mounting"),
    # --- lights (LOD0 budget 1200)
    P("kano_streetlight_single", "lights", 2.4, 0.5, 9.0, [560, 200, 60], cull=300.0, refs="pole material, arm, luminaire as installed"),
    P("kano_streetlight_double_median", "lights", 4.0, 0.5, 10.0, [820, 280, 70], cull=320.0, refs="median-mounted double arm"),
    # --- props (LOD0 budget 2500)
    P("kano_utility_pole", "props", 1.6, 0.5, 9.5, [380, 140, 40], cull=260.0, refs="pole material (concrete / timber / steel), crossarms, insulators"),
    P("kano_transformer_pole", "props", 2.0, 1.4, 9.5, [900, 300, 70], cull=260.0, refs="pole-mounted transformer as installed"),
    P("kano_kiosk_a", "props", 3.0, 3.0, 2.6, [900, 300, 80], cull=220.0, refs="roadside kiosk"),
    P("kano_market_stall_a", "props", 3.5, 3.0, 2.6, [1000, 340, 90], cull=220.0, refs="stall frame, umbrella/tarpaulin, goods"),
    P("kano_bus_stop_a", "props", 6.0, 3.0, 3.0, [1500, 500, 120], cull=260.0, refs="roadside shelter/stop"),
    P("kano_parked_group_a", "props", 7.0, 4.0, 1.7, [2300, 800, 260, 70], cull=220.0, note="ONE mesh: a saloon car and two motorcycles. Grouped because every separate parked vehicle is a draw call (see the budget test).", refs="common saloon car and motorcycles (generic, no brand)"),
    P("kano_parked_group_b", "props", 6.5, 4.0, 1.8, [2300, 800, 260, 70], cull=220.0, note="ONE mesh: two motor tricycle taxis and a motorcycle.", refs="motor tricycle taxis and a motorcycle"),
    P("kano_parked_minibus_a", "props", 1.9, 5.2, 2.3, [2100, 700, 200, 60], cull=240.0, refs="commercial minibus (generic, no brand)"),
    # --- vegetation (LOD0 budget 3000)
    P("kano_neem_tree_a", "vegetation", 8.0, 8.0, 9.0, [1800, 600, 160, 40], collider="none", cull=320.0, tags=["tree"], refs="owner/reference to confirm the species that line these roads (neem is commonly reported; not verified here)"),
    P("kano_neem_tree_b", "vegetation", 9.0, 9.0, 11.0, [2100, 700, 180, 40], collider="none", cull=340.0, tags=["tree"], refs="as above, second form"),
    P("kano_date_palm_a", "vegetation", 5.0, 5.0, 12.0, [1500, 500, 120], collider="none", cull=340.0, tags=["tree"], refs="owner/reference to confirm"),
    P("kano_shrub_a", "vegetation", 2.0, 2.0, 1.4, [260, 100, 30], collider="none", cull=140.0, shadow=False, tags=["ground"], refs="dry-season shrubs / hedges"),
]
for _p in INVENTORY:      # which reference gaps block this prefab (register ids), and whether it can be modelled yet
    _p["referenceIds"] = [m["id"] for m in REF_MISSING if _p["id"] in m["drives"]]
    _p["readyToModel"] = False
    _p["readyReason"] = "blocked: no confirmed reference (" + ", ".join(_p["referenceIds"]) + ")"
for _p in INVENTORY:      # every plot prefab is exported twice from one Blender source: with its front apron (used today) and without it (catalogued only when RB-04/RB-05 exist)
    if "plot" in _p["tags"]: _p["noApronVariant"] = _p["id"] + "_noapron"
INV = {p["id"]: p for p in INVENTORY}

KIT_MODULES = [
    {"id": "kit_wall_plain", "use": "compound walls (plain rendered)", "referencesNeeded": "wall height, thickness, finish, coping"},
    {"id": "kit_wall_decorated", "use": "wall-top treatment", "referencesNeeded": "parapet profile, zankwaye pinnacles (verified feature of Hausa buildings)"},
    {"id": "kit_gate_pier", "use": "gate piers", "referencesNeeded": "pier section, cap"},
    {"id": "kit_gate_leaf", "use": "metal gate leaves (several patterns)", "referencesNeeded": "gate patterns as installed"},
    {"id": "kit_zaure_entrance", "use": "street-front entrance room of a traditional compound", "referencesNeeded": "zaure elevation and opening sizes"},
    {"id": "kit_flat_roof_parapet", "use": "flat roofs with parapets, water tanks", "referencesNeeded": "parapet height, roof furniture"},
    {"id": "kit_openings", "use": "doors, windows, shutters, security bars", "referencesNeeded": "opening sizes and styles"},
    {"id": "kit_shopfront", "use": "shop fronts with roll shutters and sign band", "referencesNeeded": "shopfront types"},
    {"id": "kit_storey_modern", "use": "modern storeys, balconies, stairs", "referencesNeeded": "modern facade types"},
    {"id": "kit_dome_minaret", "use": "mosque dome, minaret, ablution block", "referencesNeeded": "mosque type"},
    {"id": "kit_apron_drain", "use": "frontage apron: concrete drain slab, walkway, steps", "referencesNeeded": "drain and apron as built"},
    {"id": "kit_pole_fittings", "use": "crossarms, insulators, cables, lamp arms", "referencesNeeded": "installed fittings"},
]

# ------------------------------------------------------------------------------------------------------------------ road
PROFILES = [   # copied from the game's existing Lagos-Ibadan profiles (placeholders until road references exist)
    {"id": "urban_4lane_median", "lanesPerDirection": 2, "laneWidth": 3.5, "medianWidth": 2.0, "centreLine": "none", "centreLineColor": "yellow", "shoulderWidth": 1.5, "innerShoulderWidth": 0.6, "vergeWidth": 1.0, "camberPercent": 2.0, "edgeBarrier": "none", "ditchWidth": 2.0, "ditchDepth": 0.4, "laneLineDash": 3.0, "laneLineGap": 9.0},
    {"id": "urban_4lane", "lanesPerDirection": 2, "laneWidth": 3.5, "medianWidth": 0.0, "centreLine": "double_solid", "centreLineColor": "yellow", "shoulderWidth": 1.5, "innerShoulderWidth": 0.6, "vergeWidth": 1.0, "camberPercent": 2.0, "edgeBarrier": "none", "ditchWidth": 2.0, "ditchDepth": 0.4, "laneLineDash": 3.0, "laneLineGap": 9.0},
    {"id": "commercial_2lane", "lanesPerDirection": 1, "laneWidth": 3.75, "medianWidth": 0.0, "centreLine": "dashed", "centreLineColor": "yellow", "shoulderWidth": 2.0, "innerShoulderWidth": 0.6, "vergeWidth": 1.0, "camberPercent": 2.0, "edgeBarrier": "none", "ditchWidth": 2.0, "ditchDepth": 0.4, "laneLineDash": 3.0, "laneLineGap": 9.0},
]
FORMATION_HALF = {"urban_4lane_median": 2.0 + 0.0 + 2 * 3.5 + 1.5 + 1.0, "urban_4lane": 2 * 3.5 + 1.5 + 1.0, "commercial_2lane": 3.75 + 2.0 + 1.0}   # approximate, for the generator's windows only

# (s, zone, surface, profile) of each control point: the road that STARTS there. 12 points, nominal stations.
CPS = [
    (0, "highway", "good", "urban_4lane_median"), (150, "highway", "good", "urban_4lane_median"), (300, "highway", "good", "urban_4lane_median"),
    (450, "urban", "good", "urban_4lane_median"), (600, "urban", "good", "urban_4lane"), (750, "urban", "worn", "urban_4lane"),
    (900, "commercial", "worn", "urban_4lane"), (1050, "commercial", "damaged", "urban_4lane"), (1200, "commercial", "worn", "commercial_2lane"),
    (1350, "urban", "worn", "commercial_2lane"), (1500, "urban", "good", "commercial_2lane"), (1650, "urban", "good", "commercial_2lane"),
]
NAMES = {0: "City entrance", 450: "Modern district", 900: "Commercial spine", 1350: "Old neighbourhood", 1650: "Slice end"}
JUNCTIONS = [  # (id, name, s, angle, length, profile)  - all on undivided road, at least 150 m apart (the game's validator)
    ("j1_modern_street", "Modern district street", 650, 90, 260, "commercial_2lane"),
    ("j2_plaza_street", "Plaza street", 830, -80, 220, "commercial_2lane"),
    ("j3_back_street", "Commercial back street", 1000, 100, 170, "commercial_2lane"),
    ("j4_market_lane", "Market lane", 1230, -90, 240, "commercial_2lane"),
    ("j5_mosque_street", "Mosque street", 1390, 85, 300, "commercial_2lane"),
    ("j6_compound_lane", "Compound lane", 1550, -95, 200, "commercial_2lane"),
]


def centre(s: float) -> tuple[float, float]:
    """Nominal centre line: straight bearing with a gentle sinusoidal wander (radius of curvature > 4 km, far above the game's 40 m limit)."""
    b = math.radians(BEARING); dx, dz = math.sin(b), math.cos(b); rx, rz = dz, -dx      # forward and right
    lat = 30.0 * math.sin(s / 420.0)
    return (SLICE_START[0] + dx * s + rx * lat, SLICE_START[1] + dz * s + rz * lat)


def heading_deg(s: float) -> float:
    x1, z1 = centre(s - 1); x2, z2 = centre(s + 1)
    return math.degrees(math.atan2(x2 - x1, z2 - z1)) % 360


# frontage sections: (id, a, b, [prop ids], pitch, side setback of the front edge from the formation edge)
SECTIONS = [
    ("modern", 470, 820, ["kano_compound_modern_a", "kano_compound_modern_b"], 24.0),
    ("shops", 850, 1225, ["kano_shop_row_a", "kano_shop_row_b"], 20.0),
    ("hausa", 1245, 1640, ["kano_hausa_compound_a", "kano_hausa_compound_b", "kano_hausa_compound_c"], 22.0),
]
SIDE_WINDOW = 48.0     # half-width along the main road, on the junction's own side, taken by the side street's plots (6.75 formation + 1 setback + up to 30 plot depth + half a main-road plot)
SIDE_FRONT = 120.0     # the game scatters only along the MAIN road: the side streets are lined by hand-placed plots over this length (the part a player sees when turning in)
SIDE_PLOTS = {"j1_modern_street": ["kano_compound_modern_a", "kano_compound_modern_b"], "j2_plaza_street": ["kano_compound_modern_b", "kano_compound_modern_a"],
              "j3_back_street": ["kano_shop_row_a", "kano_shop_row_b"], "j4_market_lane": ["kano_shop_row_b", "kano_shop_row_a"],
              "j5_mosque_street": ["kano_hausa_compound_a", "kano_hausa_compound_c", "kano_hausa_compound_b"], "j6_compound_lane": ["kano_hausa_compound_c", "kano_hausa_compound_b", "kano_hausa_compound_a"]}
SETBACK = 1.0      # front edge of a plot prefab (its apron) starts this far from the formation edge

# hand-placed set pieces: (id, prop, s, side(+1 right/-1 left/0 centre), setback from formation edge, yawDeg(relative to travel), text)
HEROES = [
    ("plaza_1", "kano_plaza_a", 735, +1, SETBACK, None),
    ("midrise_1", "kano_midrise_a", 520, -1, SETBACK, None),
    ("midrise_2", "kano_midrise_a", 780, -1, SETBACK, None),
    ("apartments_1", "kano_apartment_block_a", 560, +1, SETBACK, None),
    ("fuel_1", "kano_filling_station_a", 1105, -1, SETBACK, None),
    ("busstop_1", "kano_bus_stop_a", 925, +1, 3.5, None),
    ("busstop_2", "kano_bus_stop_a", 1190, -1, 3.5, None),
    ("mosque_1", "kano_mosque_neighbourhood_a", 1462, +1, SETBACK, "qibla"),
]


def half(profile: str) -> float:
    return FORMATION_HALF[profile]


def build_route() -> dict:
    cps = []
    for i, (s, zone, surface, prof) in enumerate(CPS):
        x, z = centre(s)
        cp = {"id": f"cp{i:02d}", "x": round(x, 1), "z": round(z, 1), "elevation": round(0.4 * math.sin(s / 700.0) + 0.0008 * s, 2), "zone": zone, "surface": surface, "profile": prof}
        if s in NAMES: cp["name"] = NAMES[s]
        cps.append(cp)

    junctions = [{"id": i, "name": n, "kind": "t_junction", "profile": p, "s": float(s), "angleDeg": float(a), "length": float(l), "rise": 0.0} for (i, n, s, a, l, p) in JUNCTIONS]

    # exclusion windows per side (junction mouths, set pieces): scatter plots must not straddle them
    windows = {+1: [], -1: []}
    for (_, _, s, a, _, p) in JUNCTIONS: windows[1 if a > 0 else -1].append((s - SIDE_WINDOW, s + SIDE_WINDOW))
    for (_, prop, s, side, _, _) in HEROES: w = INV[prop]["widthM"] / 2 + 12.0; windows[side].append((s - w, s + w))

    def free_segments(a: float, b: float, side: int):
        segs, cur = [], a
        for (w0, w1) in sorted(windows[side]):
            if w1 <= cur or w0 >= b: continue
            if w0 > cur: segs.append((cur, w0))
            cur = max(cur, w1)
        if cur < b: segs.append((cur, b))
        return [(x, y) for (x, y) in segs if y - x > 40.0]

    rules = []
    def rule(id, prop, side, s0, s1, size, off0, off1, presence, zones="", yaw_jitter=2.0, scale=(1.0, 1.0), face=True):
        rules.append({"id": id, "prop": prop, "zones": zones, "side": "right" if side > 0 else "left" if side < 0 else "both", "sFrom": round(s0, 1), "sTo": round(s1, 1),
                      "offsetMin": round(off0, 2), "offsetMax": round(off1, 2), "spacingMin": round(size, 2), "spacingMax": round(size, 2),
                      "presence": presence, "scaleMin": scale[0], "scaleMax": scale[1], "yawJitterDeg": yaw_jitter, "faceRoad": face})

    # plots: interleaved rules so the variants alternate along the frontage and the plots abut without overlapping
    for (sid, a, b, props, pitch) in SECTIONS:
        n = len(props)
        for side in (+1, -1):
            for si, (x0, x1) in enumerate(free_segments(a, b, side)):
                for k, prop in enumerate(props):
                    off = SETBACK + INV[prop]["depthM"] / 2
                    rule(f"{sid}_{'r' if side > 0 else 'l'}{si}_{k}", prop, side, x0 + k * pitch, x1, n * pitch, off, off, 0.92)

    # poles, lights, trees, shrubs: they stand on the plot apron, 3.2 - 4.2 m beyond the formation edge (the game drops scatter closer than 3 m)
    rule("poles_r", "kano_utility_pole", +1, 450, 1650, 42.0, 3.4, 3.6, 1.0)
    rule("transformers_r", "kano_transformer_pole", +1, 520, 1650, 252.0, 3.4, 3.6, 0.8)
    rule("lights_modern_l", "kano_streetlight_single", -1, 450, 1650, 42.0, 3.4, 3.6, 1.0)
    rule("lights_modern_r", "kano_streetlight_single", +1, 471, 1650, 84.0, 3.4, 3.6, 0.9)
    rule("neem_entrance_a", "kano_neem_tree_a", +1, 0, 450, 26.0, 6.0, 12.0, 0.7, zones="highway", yaw_jitter=180.0, scale=(0.9, 1.15), face=False)
    rule("neem_entrance_b", "kano_neem_tree_b", -1, 13, 450, 26.0, 6.0, 12.0, 0.7, zones="highway", yaw_jitter=180.0, scale=(0.9, 1.15), face=False)
    rule("neem_street_a", "kano_neem_tree_a", +1, 470, 1640, 48.0, 3.4, 4.0, 0.45, yaw_jitter=180.0, scale=(0.9, 1.15), face=False)
    rule("neem_street_b", "kano_neem_tree_b", -1, 494, 1640, 48.0, 3.4, 4.0, 0.45, yaw_jitter=180.0, scale=(0.9, 1.15), face=False)
    rule("palms_street", "kano_date_palm_a", -1, 480, 1640, 96.0, 3.4, 4.0, 0.35, yaw_jitter=180.0, face=False)
    rule("shrubs_entrance", "kano_shrub_a", +1, 0, 450, 24.0, 3.4, 5.0, 0.6, zones="highway", yaw_jitter=180.0, face=False)
    rule("signs_r", "kano_road_sign_small", +1, 60, 1640, 160.0, 3.4, 3.6, 0.9)
    rule("billboards_entrance", "kano_billboard_a", +1, 150, 450, 300.0, 14.0, 14.0, 0.9, zones="highway")
    rule("kiosks_shops_r", "kano_kiosk_a", +1, 850, 1225, 70.0, 3.4, 3.6, 0.6)
    rule("stalls_shops_l", "kano_market_stall_a", -1, 861, 1225, 56.0, 3.4, 3.6, 0.6)
    rule("parked_group_a", "kano_parked_group_a", +1, 470, 1640, 52.0, 3.3, 3.5, 0.6)
    rule("parked_group_b", "kano_parked_group_b", -1, 850, 1640, 44.0, 3.3, 3.5, 0.6)
    rule("parked_minibus", "kano_parked_minibus_a", -1, 850, 1225, 170.0, 3.3, 3.5, 0.5)

    props = []
    # entrance: gantry sign, median double streetlights (placements: scatter cannot stand on a median)
    props.append({"id": "gantry_entrance", "prop": "kano_road_sign_gantry", "text": "KANO", "s": 70.0, "lateral": 0.0, "yawDeg": 0.0, "scale": 1.0})
    for i, s in enumerate(range(20, 440, 40)):
        props.append({"id": f"median_light_{i:02d}", "prop": "kano_streetlight_double_median", "text": "", "s": float(s), "lateral": 0.0, "yawDeg": 0.0, "scale": 1.0})
    # set pieces
    for (id, prop, s, side, setback, yaw) in HEROES:
        d = INV[prop]["depthM"]; w_ = INV[prop]["widthM"]; hf = half(next(c[3] for c in reversed(CPS) if c[0] <= s))
        if yaw == "qibla":
            ydeg = round(((QIBLA_BEARING - heading_deg(s) + 180) % 360) - 180, 1)     # prefab +Z (the qibla wall) points along the qibla bearing
            # the qibla rarely lines up with the street: a footprint turned by phi to the road reaches d/2*|cos| + w/2*|sin| sideways, so the plot sits further back (a skewed forecourt)
            phi = math.radians(ydeg + 90 * side)                                      # angle between the footprint's depth axis and the road's outward normal (side * 90 deg)
            ext = d / 2 * abs(math.cos(phi)) + w_ / 2 * abs(math.sin(phi))
            lat = side * (hf + setback + ext)
        else:
            ydeg = -90.0 * side                                                       # +Z (the plot front) points back to the road
            lat = side * (hf + setback + d / 2)
        props.append({"id": id, "prop": prop, "text": "", "s": float(s), "lateral": round(lat, 2), "yawDeg": ydeg, "scale": 1.0})

    # side streets: the plots stand on both sides of each junction road for SIDE_FRONT metres. Placements are expressed as (s, lateral) on the MAIN road (the only frame the data has),
    # so each position is converted from the side street's own frame; the overlap test then checks the result against the real road model.
    for (jid, _, js, ja, jl, jp) in JUNCTIONS:
        side = 1 if ja > 0 else -1; a = math.radians(abs(ja)); h = math.radians(heading_deg(js))
        F = (math.sin(h), math.cos(h)); R = (F[1], -F[0])
        runprof = next(c[3] for c in reversed(CPS) if c[0] <= js); paved = {"urban_4lane_median": 2.0 + 0.6 + 7.0 + 1.5, "urban_4lane": 7.0 + 1.5, "commercial_2lane": 3.75 + 2.0}[runprof]
        dirv = (F[0] * math.cos(a) + R[0] * side * math.sin(a), F[1] * math.cos(a) + R[1] * side * math.sin(a))      # side street direction (world x, z)
        perp = (dirv[1], -dirv[0])                                                                               # to the right of the side street's travel direction
        start = (centre(js)[0] + R[0] * side * paved, centre(js)[1] + R[1] * side * paved)
        street_half = FORMATION_HALF["commercial_2lane"]; ids = SIDE_PLOTS[jid]
        for e_sign in (+1, -1):
            d = paved * 0 + 14.0; k = 0
            while True:
                prop = ids[k % len(ids)]; w_ = INV[prop]["widthM"]; dep = INV[prop]["depthM"]
                d += w_ / 2
                if d + w_ / 2 > min(SIDE_FRONT, jl - 15.0): break
                e = e_sign * (street_half + SETBACK + dep / 2)
                wx, wz = start[0] + dirv[0] * d + perp[0] * e, start[1] + dirv[1] * d + perp[1] * e
                cx, cz = centre(js)
                dsx, dsz = wx - cx, wz - cz
                s_p = js + dsx * F[0] + dsz * F[1]; lat = dsx * R[0] + dsz * R[1]
                bearing = (math.degrees(math.atan2(dirv[0], dirv[1])) - 90 * e_sign) % 360       # the plot front faces the street
                yaw = round(((bearing - heading_deg(s_p) + 180) % 360) - 180, 1)
                props.append({"id": f"side_{jid[:2]}_{'r' if e_sign > 0 else 'l'}{k}", "prop": prop, "text": "", "s": round(s_p, 1), "lateral": round(lat, 2), "yawDeg": yaw, "scale": 1.0})
                d += w_ / 2 + 2.0; k += 1

    return {
        "schemaVersion": 1, "routeId": "ng-kano-slice", "name": "Kano vertical slice (DRAFT: SW approach, city entrance to old-neighbourhood edge)", "seed": 8052,
        "meta": {"country": "NG", "from": "Kano approach (from Kaduna)", "to": "Kano neighbourhood edge", "status": "planned", "worldMapRouteId": "kaduna-kano", "realRoadKm": 209.6, "gameLengthKm": 1.65,
                 "gameCoverage": "DESIGN DRAFT, NOT A SURVEY. realRoadKm is the great-circle distance Kaduna-Kano (a LOWER BOUND: the real road length is not in the project data). The playable corridor is a fictional 1.65 km corridor at 1:1 scale on the south-west approach to Kano, modelled on no surveyed road. Real: the city, the direction of the Kaduna approach, the climate and the architectural character. Fictional: every street, plot and building in it. See docs/KANO_VERTICAL_SLICE.md.",
                 "elevationSource": "design (the Kano plain is about 480-490 m a.s.l. and nearly flat; SRTM/Copernicus not yet imported)",
                 "notes": "Frame: docs/kano/kano_world_frame.json (x east, z north, metres, origin = the Kano city reference point). Control points are authored on the 200 m chunk lattice; the start is (-2400, -3200)."},
        "terrain": {"baseElevation": 0.0, "trendX": 0.0, "trendZ": 0.0008, "hillAmplitude": 1.5, "hillWavelength": 700.0, "detailAmplitude": 0.35, "detailWavelength": 55.0, "maxEmbankmentSlope": 0.5, "minBlendDistance": 6.0, "materialByZone": ""},
        "profiles": PROFILES, "controlPoints": cps, "bridges": [], "junctions": junctions, "propRules": rules, "props": props,
        "traffic": {"densityScale": 1.0, "speedLimitScale": 1.0, "vehicleMix": ["saloon_car", "minibus", "motorcycle", "tricycle_taxi", "truck"]},
        "waypoints": [{"id": "wp_entrance", "name": "City entrance", "kind": "gateway", "s": 70.0}, {"id": "wp_modern", "name": "Modern district", "kind": "district", "s": 520.0},
                      {"id": "wp_commercial", "name": "Commercial spine", "kind": "market", "s": 900.0}, {"id": "wp_mosque", "name": "Neighbourhood mosque", "kind": "mosque", "s": 1462.0}],
    }


def build_inventory() -> dict:
    return {"schemaVersion": 1, "note": "PLANNED prefabs for the Kano vertical slice. Catalogue-shaped so the game's own PropCatalogValidator can check the budgets. Nothing here exists yet; every dimension is a placeholder envelope. "
                                        "Ids are NEW (kano_*) on purpose: a production prefab replaces the placeholder of the SAME id in every route, so reusing 'utility_pole' would also change Lagos-Ibadan.",
            "props": INVENTORY, "kitModules": KIT_MODULES}


def build_workarounds() -> dict:
    plots = [p["id"] for p in INVENTORY if "plot" in p["tags"]]
    return {"schemaVersion": 1, "rule": "Every Kano-specific workaround for a missing shared-engine feature is declared here with the docs/ROAD_ENGINE_BACKLOG.md item that removes it and a DATA-ONLY removal path. KanoSliceTests fails if a placement is neither a declared workaround nor a declared set piece, or if a removal needs code.",
            "workarounds": [
                {"id": "W-01", "what": "A 4.5 m open front apron (drain slab, walkway, pole line) inside each plot prefab", "standsInFor": ["RB-04", "RB-05"], "affects": plots,
                 "isolation": "the apron is the separate Blender kit module `kit_apron_drain`; each plot is exported twice from one source, with the apron and as `<id>_noapron`; the no-apron variants are catalogued only when the engine has kerb and drain strips",
                 "removalPath": "in the route rules replace each plot prop id by its `_noapron` variant and move the offset 4.5 m towards the road (data only)", "dataOnly": True},
                {"id": "W-02", "what": "Side-street plots as hand-placed set pieces (the scatter runs along the main road only)", "standsInFor": ["RB-08"], "placementIdPrefix": "side_",
                 "removalPath": "delete the `side_*` placements and add scatter rules for the side corridors (data only)", "dataOnly": True},
                {"id": "W-03", "what": "Median double streetlights as hand-placed set pieces (rule offsets only go outward from the formation edge)", "standsInFor": ["RB-09"], "placementIdPrefix": "median_light_",
                 "removalPath": "replace the placements with a median rule (data only)", "dataOnly": True},
                {"id": "W-04", "what": "The word KANO baked into the gantry sign prefab (production prefabs get no text)", "standsInFor": ["RB-07"], "placementIdPrefix": "gantry_",
                 "removalPath": "point the placement at the text-bearing sign prefab; the placement already carries the text (data only)", "dataOnly": True},
                {"id": "W-05", "what": "Staggered T-junctions at least 150 m apart instead of four-way crossings", "standsInFor": ["RB-02"], "junctionIds": [j[0] for j in JUNCTIONS],
                 "removalPath": "replace a staggered pair by one four-way junction in the route data (data only)", "dataOnly": True}],
            "setPieces": [h[0] for h in HEROES],
            "notWorkarounds": ["Parked vehicles grouped into single meshes: a draw-call optimisation that stays valid under any engine.", "The existing 'damaged' and 'worn' surface classes: used as they are, nothing Kano-specific added (RB-06 is future work)."]}


def build_frame() -> dict:
    dala = to_xz(12 + 33.498 / 3600, 8 + 30 / 60 + 25.106 / 3600)
    gr = to_xz(11 + 59 / 60 + 32 / 3600, 8 + 30 / 60 + 58 / 3600)
    wm = to_xz(12.0022, 8.592)
    return {"schemaVersion": 2, "id": "kano-frame-k0", "status": "PROPOSED (nothing else depends on it until the owner approves; no existing file uses it)",
            "units": "metres", "axes": "x east, z north, y up: the game's route axes",
            "canonicalAnchor": {"id": "kano_city_anchor", "crs": "EPSG:4326 (WGS84)", "lat": LAT0, "lon": LON0,
                                "source": "GeoNames id 2335204 (Kano), read offline from the geonamescache 3.0.2 dataset (the GeoNames web service is not reachable from this session)",
                                "crossChecks": [{"source": "Wikipedia 'Kano (city)': 12°00'N 8°31'E", "offsetFromAnchorM": 13},
                                                {"source": "OpenStreetMap city node as listed by Mapcarta (11.994N 8.522E; secondary listing, node 501540880)", "offsetFromAnchorM": 888}],
                                "tolerance": "about 1 km: a city anchor is a place point, not a surveyed benchmark. All the historic landmarks lie within 1.5 km of it."},
            "transformToUnity": {"name": "K0", "kind": "local tangent plane (equirectangular) at the anchor", "origin": [0, 0], "xFormula": "x = (lon - lon0) * 111320 * cos(lat0)", "zFormula": "z = (lat - lat0) * 110700",
                                 "maxErrorAgainstWgs84Geodesic": {"1 km": "0.4 m", "5 km": "2.3 m", "10 km": "5.1 m", "25 km": "17.2 m (0.05%)"},
                                 "beyondAbout25km": "do not extend K0: use UTM zone 32N (EPSG:32632; Kano at 8.5E is inside zone 32, 6E to 12E) or a per-corridor frame with explicit connectors",
                                 "chunkLattice": {"sizeM": 200, "note": "Every authored coordinate is a multiple of 200 m at chunk corners so neighbouring routes share the game's chunk lattice (ChunkStreamer.chunkSize)."},
                                 "scalePolicy": "City slices are 1:1. A compressed corridor (Lagos-Ibadan is compressed) declares its compression in meta and never shares a frame with 1:1 content."},
            "slice": {"routeId": "ng-kano-slice", "startXZ": list(SLICE_START), "startLatLon": [round(LAT0 + SLICE_START[1] / M_PER_DEG_LAT, 5), round(LON0 + SLICE_START[0] / M_PER_DEG_LON, 5)], "approachBearingDeg": BEARING, "fictional": True,
                      "note": "About 4 km south-west of the anchor on the Kaduna approach direction. A design choice, not a survey: no street here is a real street."},
            "qibla": {"bearingDeg": QIBLA_BEARING, "note": "WGS84 geodesic initial azimuth from the anchor to the Kaaba (21.4225N 39.8262E): 68.46 degrees. Mosques in the slice orient their qibla wall to this, whatever the road does."},
            "worldMapPin": {"gameValue": {"lat": 12.0022, "lon": 8.592}, "xzFromAnchor_m": [round(wm[0]), round(wm[1])], "distanceFromAnchorKm": 8.2, "bearingFromAnchorDeg": 88.4,
                            "provenanceInRepository": "introduced with the whole world map on 2026-10-10 (commit 4973744) and the backend seed (a0d9b3f); no source is recorded anywhere. docs/WORLD_MAP.md calls city lat/lon 'authoritative' and only tests Lagos and Ibadan against the backend.",
                            "whatItMatches": "a Kano point listed as 12.00218N 8.59196E, altitude 469 m (distancefromto.net), not the usual city reference; the sources describe it as a different location within the Kano area",
                            "isItIsolated": "no: every game city pin differs from GeoNames by 1.7 to 10.8 km (Kano 8.2, Abuja 10.8, Lagos 8.0), so it is a convention of the pins, not a Kano slip",
                            "action": "none taken; see docs/kano/COORDINATES.md"},
            "worldMapDiscrepancy": {"gameWorldMapKano": {"lat": 12.0022, "lon": 8.592}, "referenceXZ_m": [round(wm[0]), round(wm[1])], "distanceKm": 8.2,
                                    "note": "Kept for the test that the discrepancy stays recorded. Not changed; the owner decides."},
            "reservedAnchors": [
                {"id": "dala_hill", "name": "Dala Hill", "kind": "landmark", "lat": round(12 + 33.498 / 3600, 5), "lon": round(8 + 30 / 60 + 25.106 / 3600, 5), "xz_m": [round(dala[0]), round(dala[1])],
                 "verified": "ONE source (12°0'33.498\"N 8°30'25.106\"E); about 1.5 km from the city anchor; height disputed (534 m above sea level in most sources, 453.67 m in one)"},
                {"id": "gidan_rumfa", "name": "Gidan Rumfa (Emir's Palace)", "kind": "landmark", "lat": round(11 + 59 / 60 + 32 / 3600, 5), "lon": round(8 + 30 / 60 + 58 / 3600, 5), "xz_m": [round(gr[0]), round(gr[1])],
                 "verified": "three web listings agree within about 300 m (Wikipedia 11°59'32\"N 8°30'58\"E; Plizio 11.9933 8.5161; AroundUs 11.9923 8.5160); a heritage inventory gives 8.519E. Not surveyed: treat as +-300 m"},
                {"id": "kurmi_market", "name": "Kurmi Market", "kind": "landmark", "lat": None, "lon": None, "verified": "in the old city; coordinates NOT verified"},
                {"id": "kofar_mata_dye_pits", "name": "Kofar Mata Dye Pits", "kind": "landmark", "lat": None, "lon": None, "verified": "in the old city near the wall and palace; coordinates NOT verified"},
            ]}


def main() -> int:
    outputs = {"ng-kano-slice.route.draft.json": build_route(), "kano_asset_inventory.json": build_inventory(), "kano_world_frame.json": build_frame(), "kano_workarounds.json": build_workarounds()}
    check = "--check" in sys.argv; bad = 0
    OUT.mkdir(parents=True, exist_ok=True)
    for name, data in outputs.items():
        text = json.dumps(data, indent=1, ensure_ascii=False) + "\n"; p = OUT / name
        if check:
            if not p.is_file() or p.read_text(encoding="utf-8") != text: print("OUT OF DATE:", p.relative_to(ROOT)); bad += 1
        else:
            p.write_text(text, encoding="utf-8"); print("wrote", p.relative_to(ROOT), f"({len(text)} bytes)")
    return 1 if bad else 0


if __name__ == "__main__":
    sys.exit(main())
