"""Family K of the city construction (owner directive of 2026-11-16: markets, parked vehicles, universities, each placed in the city the day it is made; transport_kit.family_k). Real Blender geometry (bpy).
What is checked: the three assets build, are baked and pass the toolchain validator within their budgets; the lots match their declared sizes and stand on their centre; every sign carries generic words (no real
university, market or brand); the parked cars lie lengthwise along x (so a road-facing placement parks them parallel to the kerb), each with its own collision box, and the city places every one of them.
Run from tools/blender:  PYTHONPATH=. ~/.aro/venv/bin/python -m pytest tests/test_family_k.py -q"""
from __future__ import annotations
import json
from pathlib import Path
import pytest

bpy = pytest.importorskip("bpy")
import mathutils
from aro_blender import validate
from transport_kit import common as C, family_k as K
from transport_kit.build_transport import SPECS, build_scene, load_all, catalog_entry

load_all()
FAM = sorted(a for a in SPECS if C.EXTRAS[a]["family"] == "K")
ROOT = Path(__file__).resolve().parents[3]


@pytest.fixture(scope="module")
def built():
    out = {}
    for aid in FAM:
        sp = SPECS[aid]; build_scene(sp); res = validate.run(bpy, mathutils, sp["kind"], True, None, aid); out[aid] = (res, catalog_entry(sp, res))
    return out


def test_the_family():
    assert FAM == ["aro_car_parked_set_a", "kano_keke_rank_a", "kano_market_neighbourhood_a", "kano_university_faculty_a", "rg_ngn_container_shops_a", "rg_ngn_roadside_stalls_a"]
    assert all(SPECS[a]["kind"] == "buildings" for a in FAM if a != "aro_car_parked_set_a") and SPECS["aro_car_parked_set_a"]["kind"] == "props"
    assert all(SPECS[a]["version"] == 1 and SPECS[a]["surface"] for a in FAM)


@pytest.mark.parametrize("aid", FAM)
def test_each_asset_passes_the_validator_baked_within_budget_and_matches_its_lot(aid, built):
    res, entry = built[aid]
    assert res["passed"], [f["message"] for f in res["findings"] if f["severity"] == "error"]
    t = res["stats"]["lod_triangles"]; assert len(t) == 3 and t[0] > t[1] > t[2] and t[2] <= 0.1 * t[0] and t[0] <= 6000, t
    assert res["stats"]["materials"] == [f"M_sb_{aid}_v001_l{lv}" for lv in (0, 1, 2)]
    lo, hi = res["stats"]["bounds_min_m"], res["stats"]["bounds_max_m"]; tx, ty, tz = SPECS[aid]["target"]
    assert abs((hi[0] - lo[0]) - tx) <= 0.1 * tx + 1.0 and abs((hi[1] - lo[1]) - ty) <= 0.1 * ty + 1.0, (aid, hi[0] - lo[0], hi[1] - lo[1])
    assert abs((lo[0] + hi[0]) / 2) <= 1.5 and abs((lo[1] + hi[1]) / 2) <= 1.5 and lo[2] >= -0.05, f"{aid}: origin at the lot centre, nothing below the ground"
    assert entry["id"] == aid and entry["collider"] == "box"


def test_the_keke_rank_parks_three_tricycles_nose_out_in_front_of_the_passengers_canopy():
    cols = SPECS["kano_keke_rank_a"]["cols"]; assert len(cols) == len(K.KEKES) == 3                      # one collision box per tricycle: the passengers stand under the canopy, not in a box
    for x0, y0, z0, x1, y1, z1 in cols: assert y1 <= -1.5 and y0 >= -4.5 and (y1 - y0) > 1.5 * (x1 - x0) and z1 <= 1.9   # in the front strip of the lot (-Y is the road), lengthwise towards the road (a keke is 2.7 x 1.4 m)
    assert all(K.KEKES[i + 1] - K.KEKES[i] >= K.KEKE_W + 1.5 for i in range(len(K.KEKES) - 1)), "room to walk between the tricycles"
    assert C.EXTRAS["kano_keke_rank_a"]["light"]["heads"][0]["z"] < -0.8, "the canopy light is behind the tricycles (game axes: +z is the road), over the bench"
    assert "keke_rank" in C.EXTRAS["kano_keke_rank_a"]["tags"]                                           # the crowd seats waiting passengers at it (NetCore Crowd)


def test_signs_carry_generic_words_only():
    words = [w for k in (K.S_UNI, K.S_UNI_GATE, K.S_MARKET, *K.S_SHOPS, *K.S_CONT) for w in C_lines(k)]
    assert words and all(w.replace("&", "").replace(" ", "").isalpha() for w in words), words
    for bad in ("BAYERO", "KURMI", "SABON GARI", "SHOPRITE", "KFC", "MTN"): assert not any(bad in w for w in words), bad


def C_lines(key):
    from transport_kit import surface as SB
    return SB.SIGNS[key]["lines"]


def test_the_parked_cars_lie_along_x_each_with_its_box():
    cols = SPECS["aro_car_parked_set_a"]["cols"]; assert len(cols) == len(K.CARS) == 3
    for x0, y0, z0, x1, y1, z1 in cols: assert (x1 - x0) > 2 * (y1 - y0) and z1 <= 1.6
    assert all(abs(K.CARS[i + 1][0] - K.CARS[i][0]) >= K.CAR_L + 1.0 for i in range(len(K.CARS) - 1)), "a gap between the cars"


def test_the_city_places_every_family_k_asset():
    r = json.loads((ROOT / "Assets" / "_Project" / "Resources" / "Routes" / "ng-kano-city.route.json").read_text())
    shared = {m["id"]: m["lots"] for m in r.get("mixes", [])}                     # the rules name shared lot lists (mixId, anchorsId)
    lots = {l["prop"] for f in r["frontage"] for l in (f.get("mix") or shared[f["mixId"]]) + (f.get("anchors") or shared.get(f.get("anchorsId", ""), []))}; rules = {p["prop"] for p in r["propRules"]}
    assert {"kano_university_faculty_a", "kano_market_neighbourhood_a", "rg_ngn_roadside_stalls_a", "rg_ngn_container_shops_a"} <= lots and "aro_car_parked_set_a" in rules
