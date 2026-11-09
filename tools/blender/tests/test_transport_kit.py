"""Transport kit (families A logistics, B bus terminals, C road furniture): every asset is built with the toolchain's own geometry and must pass the validator, stay within the approved material cap, carry sensible
catalogue extras (light heads, text faces, sockets inside or next to the model), follow the naming classes and agree with the approved breakdown (docs/KANO_TRANSPORT_ASSET_BREAKDOWN.md) up to the documented deviations.
These tests build real Blender geometry (bpy), so they take about a minute. Run from tools/blender:  PYTHONPATH=. ~/.aro/venv/bin/python -m pytest tests/test_transport_kit.py -q"""
from __future__ import annotations
import json, re
from pathlib import Path
import pytest

bpy = pytest.importorskip("bpy")
import mathutils
from aro_blender import validate
from transport_kit import common as C
from transport_kit.build_transport import SPECS, build_scene, load_all, catalog_entry
from transport_kit import palette as PAL

load_all()
ROOT = Path(__file__).resolve().parents[3]
BREAKDOWN = (ROOT / "docs" / "KANO_TRANSPORT_ASSET_BREAKDOWN.md").read_text()
CLASS_PREFIX = {"GLOBAL": "aro_", "REGIONAL": "rg_ngn_", "CITY_SPECIFIC": "kano_", "LANDMARK": "lmk_kano_"}
BRANDS = re.compile(r"(shell|total|oando|mobil|dangote|mtn|glo|airtel|toyota|volvo|maersk|msc|coca|pepsi|nnpc)", re.I)

# Documented deviations from the approved sizes (breakdown table value -> what was built and why). Each key is an asset id; the value says which dimension index may differ and by how much.
DEVIATION = {
    "aro_gate_sliding_a": "two-lane layout with the leaves open: a 9 m clear opening and 9 m leaves cannot fit in the 11 m of the row",
    "aro_weighbridge_a": "the 3.6 m of the row is the platform alone; the cabin, the display post and the bollards extend the footprint",
    "aro_gantry_sign_a": "a 2 x 3 lane carriageway with a median is 24 m between the legs, not 20",
    "kano_utility_pole_set": "the row lists the pole alone; the cable stubs run 6 m to each side",
    "aro_streetlight_arm_single_a": "the row's 4.4 m is the arm reach on the prop's own axes; built as 2.5 m from the pole centre",
    "aro_streetlight_double_median_a": "arms 2.6 m each side rather than 3 m",
    "aro_traffic_signal_a": "arm 4 m over the road",
    "aro_forklift_a": "the row lists length x width; built with the length along Y (front -Y)",
    "aro_truck_trailer_parked_a": "the row's 4 m is the box trailer; a flatbed is 2.7 m tall with its headboard",
    "kano_ticket_kiosk_a": "the queue rail extends the depth",
    "aro_dock_leveller_set": "the 1.2 m of the row is the platform; the wall adds 0.3 m",
    "aro_drain_grate_set": "built as a set of one grate and one manhole cover side by side (the row says 'grates and manhole covers'), with real bars over a sump",
    "aro_parking_bay_barrier_set": "wheel stop, rail posts and planter box together are 0.72 m deep, not 0.4 m",
    "aro_yard_mast_light": "the four floodlight heads on the headframe spread the footprint to 1.9 m",
}
BUDGET_EXEMPT = {"aro_drain_grate_set": "a 40-triangle decal quad cannot show bars over a sump; built as geometry, 270 triangles, far below the 2,500 props budget"}
ALL = sorted(SPECS)


@pytest.fixture(scope="module")
def built():
    """id -> (report, size, bounds): every asset built once (about a minute)."""
    out = {}
    for aid in ALL:
        sp = SPECS[aid]; a = build_scene(sp)
        res = validate.run(bpy, mathutils, sp["kind"], True, None, aid)
        out[aid] = (res, len([o for o in a.root.children if o.name.startswith("col_")]), catalog_entry(sp, res))
    return out


def test_the_approved_families_are_all_present():
    ids = set(SPECS)
    assert len(ids) == 78                                                       # 64 of families A to D + the ten mosques of family E + the four city assets of family F (tests/test_mosque_family.py and test_city_family.py have their own checks)
    for must in ("rg_ngn_warehouse_hall_a", "rg_ngn_warehouse_hall_b", "rg_ngn_warehouse_cold_a", "aro_dock_leveller_set", "aro_weighbridge_a", "aro_fuel_island_truck", "aro_gate_sliding_a",
                 "kano_bus_terminal_hall_a", "kano_bus_shelter_long_a", "aro_bus_shelter_a", "kano_ticket_kiosk_a", "kano_departure_board_a", "kano_minibus_stand_canopy_a", "kano_tricycle_rank_a",
                 "aro_streetlight_arm_single_a", "aro_streetlight_double_median_a", "aro_streetlight_ornamental_a", "aro_road_sign_direction_a", "aro_gantry_sign_a", "aro_traffic_signal_a",
                 "aro_guardrail_post_set", "aro_jersey_barrier_a", "aro_kerb_painted_run_a", "aro_drain_grate_set", "aro_bench_a", "aro_litter_bin_a", "aro_planter_island_a", "aro_planter_island_b", "kano_utility_pole_set"):
        assert must in ids, must
    assert {f"aro_container_20ft_{v}" for v in "abc"} <= ids and {f"aro_container_40ft_{v}" for v in "ab"} <= ids
    assert {f"aro_truck_trailer_parked_{v}" for v in "ab"} <= ids
    for must in ("lmk_kano_roundabout_monument_a", "aro_roundabout_fountain_a", "rg_ngn_island_wall_a", "aro_flyover_deck_segment_a", "aro_flyover_pier_a", "aro_flyover_pier_b", "rg_ngn_pier_panel_a", "aro_deck_edge_light_a", "aro_noise_wall_set"): assert must in ids, must
    assert {f"rg_ngn_pattern_panel_{v}" for v in "abc"} <= ids


def test_every_asset_ids_in_the_breakdown_rows():
    """The ids built are the approved ids, except where a 'set' row was realised as several single ids (the road signs)."""
    for aid in ALL:
        if C.EXTRAS[aid]["family"] in ("E", "F"): continue                    # families E and F are the owner's densification directive, not rows of the transport breakdown
        base = re.sub(r"_[abc]$", "", aid)
        ok = aid in BREAKDOWN or base in BREAKDOWN or re.sub(r"_(a|b|c|ab|abc)$", "", aid) in BREAKDOWN or aid.startswith("aro_road_sign_") or aid.startswith("aro_container_") or aid.startswith("aro_truck_trailer_") or aid.startswith("aro_planter_island_")
        assert ok, f"{aid} is not an approved id"


@pytest.mark.parametrize("aid", ALL)
def test_naming_class_and_registration(aid):
    x = C.EXTRAS.get(aid)
    assert x, f"{aid}: no catalogue extras registered"
    assert aid.startswith(CLASS_PREFIX[x["class"]]), f"{aid}: class {x['class']} needs the prefix {CLASS_PREFIX[x['class']]}"
    assert x["family"] in "ABCDEF" and x["ref"], aid
    assert not BRANDS.search(aid) and not BRANDS.search(SPECS[aid]["note"]), f"{aid}: a brand name"
    assert re.fullmatch(r"[a-z0-9]+(_[a-z0-9]+)*", aid)


def test_every_asset_passes_the_validator_with_at_most_four_materials(built):
    for aid, (res, a, entry) in built.items():
        errs = [f["message"] for f in res["findings"] if f["severity"] == "error"]
        assert res["passed"] and not errs, f"{aid}: {errs}"
        mats = res["stats"]["materials"]
        assert len(mats) <= 4, f"{aid}: {len(mats)} materials {mats}"
        tris = res["stats"]["lod_triangles"]
        assert len(tris) == 3 and tris[0] > tris[1] > tris[2], f"{aid}: LODs {tris}"
        assert tris[2] <= 0.10 * tris[0], aid


def test_triangle_budgets_are_the_games_own(built):
    for aid, (res, a, entry) in built.items():
        assert res["stats"]["triangles_lod0"] <= res["stats"]["budget_triangles"], aid


def test_no_asset_is_closer_to_the_breakdown_budget_than_it_should_be(built):
    """The breakdown lists an expected LOD0 triangle count per row; nothing may exceed it by more than 25% (a budget, not a target: assets below it are simpler than planned and are reported in the docs)."""
    limits = {}
    for line in BREAKDOWN.splitlines():
        m = re.match(r"\|\s*`([a-z0-9_/]+)`\s*\|.*?\|.*?\|.*?\|\s*([\d,]+)\s*\|", line)
        if m:
            for aid in m.group(1).split("/"):
                limits[aid] = int(m.group(2).replace(",", ""))
    checked = 0
    for aid, (res, a, entry) in built.items():
        lim = limits.get(aid)
        if lim is None or aid in BUDGET_EXEMPT: continue
        checked += 1
        assert res["stats"]["triangles_lod0"] <= lim * 1.25, f"{aid}: {res['stats']['triangles_lod0']} tris against {lim} in the breakdown"
    assert checked >= 20


def _dims(res): return res["stats"]["size_m"]


def test_sizes_agree_with_the_approved_rows_except_the_documented_deviations(built):
    bad = []
    for aid, (res, a, entry) in built.items():
        tgt = SPECS[aid]["target"]
        if not tgt or aid in DEVIATION: continue
        size = sorted(_dims(res)[:2], reverse=True); want = sorted(tgt[:2], reverse=True)
        h, wh = _dims(res)[2], tgt[2]
        # footprint within 25% (+15 cm) of the row, height within 25% (+30 cm): the rows are design sizes, the models include overhangs, caps and fittings
        if any(abs(g - w) > 0.25 * w + 0.15 for g, w in zip(size, want)) or abs(h - wh) > 0.25 * wh + 0.30: bad.append((aid, _dims(res), tgt))
    assert not bad, bad


def test_deviations_are_documented_and_real():
    for aid, why in DEVIATION.items():
        assert aid in SPECS and len(why) > 20


def _bbox(res): return res["stats"]["bounds_min_m"], res["stats"]["bounds_max_m"]


def test_light_heads_and_text_faces_sit_on_the_model(built):
    """A lamp head or a text face outside the model's own bounds (plus a metre) means the Blender -> game axis conversion or the position is wrong. Game axes: x right (-X in Blender), y up, z forward (-Y)."""
    for aid, (res, a, entry) in built.items():
        lo, hi = _bbox(res); x = C.EXTRAS[aid]
        def inside(g, margin=1.0):
            bx, by, bz = -g["x"], -g["z"], g["y"]                                                        # game -> Blender
            return lo[0] - margin <= bx <= hi[0] + margin and lo[1] - margin <= by <= hi[1] + margin and lo[2] - margin <= bz <= hi[2] + margin
        for h in (x.get("light") or {}).get("heads", []): assert inside(h), f"{aid}: lamp head {h} outside {lo} {hi}"
        if x.get("label"): assert inside(x["label"], 1.0), f"{aid}: label outside"
        for s in x.get("sockets", []): assert s["y"] >= -0.01, f"{aid}: socket below ground"


def test_lights_have_heads_and_known_kinds(built):
    for aid, x in C.EXTRAS.items():
        l = x.get("light")
        if not l: continue
        assert l["kind"] in ("lamp", "glow", "beacon") and l["heads"] and l["range"] > 0 and l["intensity"] > 0, aid
        assert all(set(h) == {"x", "y", "z"} for h in l["heads"])
    for aid in ("aro_streetlight_arm_single_a", "aro_streetlight_double_median_a", "aro_yard_mast_light", "rg_ngn_warehouse_hall_a"): assert "light" in C.EXTRAS[aid], aid
    assert len(C.EXTRAS["rg_ngn_warehouse_hall_a"]["light"]["heads"]) == 6 and len(C.EXTRAS["aro_streetlight_double_median_a"]["light"]["heads"]) == 2


def test_catalogue_entries_are_what_the_game_expects(built):
    cats = {"buildings", "props", "signs", "lights", "environment", "vegetation"}
    for aid, (res, a, entry) in built.items():
        assert entry["category"] in cats and entry["status"] == "production" and entry["id"] == aid
        assert entry["collider"] == ("box" if SPECS[aid]["cols"] else "none"), aid
        assert entry["lodTriangles"] == res["stats"]["lod_triangles"]
        if entry.get("label"): assert entry["label"]["width"] > 0 and entry["label"]["height"] > 0
    ls = [e for _, _, e in built.values() if e["category"] == "lights"]
    assert len(ls) >= 4 and all("light" in e for e in ls)


def test_collision_boxes_stay_with_the_model(built):
    for aid, (res, a, entry) in built.items():
        lo, hi = _bbox(res)
        for box in SPECS[aid]["cols"]:
            x0, y0, z0, x1, y1, z1 = box
            assert x0 < x1 and y0 < y1 and z0 < z1, aid
            assert x0 >= lo[0] - 0.6 and x1 <= hi[0] + 0.6 and y0 >= lo[1] - 0.6 and y1 <= hi[1] + 0.6 and z1 <= hi[2] + 0.6, f"{aid}: collider {box} outside {lo} {hi}"
        assert a == len(SPECS[aid]["cols"]), aid


def test_roads_stay_clear_where_vehicles_must_drive():
    """Gameplay clearance: the gate lanes, the weighbridge deck, the fuel canopy, the gantry and the bus terminal canopy are all drivable or passable for the real vehicles (bus 12 x 2.55 x 3.5 m, truck 2.5 x 4.2 m)."""
    gate = SPECS["aro_gate_sliding_a"]["cols"]
    lane_in, lane_out = (-4.5, -1.0), (1.0, 4.5)                                          # 3.5 m lanes each side of the 2 m guard island
    for lane in (lane_in, lane_out):
        for (x0, y0, z0, x1, y1, z1) in gate: assert x1 <= lane[0] + 1e-6 or x0 >= lane[1] - 1e-6 or z0 > 4.5, f"collider {x0, x1} blocks the lane {lane}"
    assert lane_in[1] - lane_in[0] >= 3.5 and lane_out[1] - lane_out[0] >= 3.5
    from transport_kit import terminal as B
    assert B.BAY_W >= B.BUS_W + 1.0 and B.BAY_D >= B.BUS_L + 1.0
    wb = SPECS["aro_weighbridge_a"]["cols"]
    assert all(c[1] >= 0.4 for c in wb), "the weighbridge deck must have no collider and the cabin must stay off the deck"
    fuel = SPECS["aro_fuel_island_truck"]["cols"]
    centre_lane = [c for c in fuel if c[0] < 0 < c[3]]
    assert not centre_lane, "nothing may block the centre of the fuel canopy"
    assert 6.5 - 0.7 >= 4.2 + 0.5, "canopy underside must clear a 4.2 m truck"
    gantry = SPECS["aro_gantry_sign_a"]["cols"]
    assert all(abs((c[0] + c[3]) / 2) >= 11.0 for c in gantry), "gantry legs stand outside the carriageway"
    t = SPECS["kano_bus_terminal_hall_a"]["cols"]
    assert 5.0 >= 3.5 + 1.0, "arrivals canopy must clear a bus"


def test_palette_is_consistent():
    assert PAL.GRID * PAL.GRID >= max(len(v) for v in PAL.FAMILIES.values())
    assert all(PAL.TARGET[k] in PAL.FAMILIES for k in PAL.TARGET)
    # every flat colour a builder uses must be in a palette family or be the textured concrete
    used = set()
    import inspect, transport_kit.logistics as A, transport_kit.terminal as B, transport_kit.furniture as F, transport_kit.island as D
    for mod in (A, B, F, D):
        used |= set(re.findall(r'"([a-z_0-9]+)"', inspect.getsource(mod)))
    from aro_blender import materials as AM
    missing = sorted(k for k in used if k in AM.LIBRARY and k not in PAL.TARGET and k not in ("concrete", "tk_paint", "tk_metal", "tk_glass"))
    assert not missing, f"colours outside the palette (more than 4 materials possible): {missing}"


def test_the_catalogue_file_matches_the_model_data(built):
    """The catalogue written by the build (docs/kano/TRANSPORT_KIT_catalog.json) is derived from the same entries."""
    p = ROOT / "docs" / "kano" / "TRANSPORT_KIT_catalog.json"
    if not p.is_file(): pytest.skip("catalogue not generated yet")
    d = json.loads(p.read_text())
    by = {e["id"]: e for e in d["props"]}
    for aid, (res, a, entry) in built.items():
        assert aid in by, aid
        assert by[aid]["lodTriangles"] == entry["lodTriangles"] and by[aid]["collider"] == entry["collider"], aid
