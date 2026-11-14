"""Families G (fuel stations), H (commercial), I (public), J (residential) of the owner's city construction directive of 2026-11-14 (transport_kit.fuel, transport_kit.blocks). They build real Blender geometry (bpy).
What is checked: every asset builds, is baked and passes the toolchain validator within the building budget; the stations are complete environments (canopy clear for a truck, numbered islands with lanes between
them, an entrance and an exit); every sign carries generic words or the FICTIONAL chain of the owner's concept R-040, never a real brand and never the protected red cross; footprints match their declared lots.
Run from tools/blender:  PYTHONPATH=. ~/.aro/venv/bin/python -m pytest tests/test_city_families.py -q"""
from __future__ import annotations
import re
import numpy as np
import pytest

bpy = pytest.importorskip("bpy")
import mathutils
from aro_blender import validate
from transport_kit import common as C, surface as SB
from transport_kit.build_transport import SPECS, build_scene, load_all, catalog_entry

load_all()
NEW = sorted(a for a in SPECS if C.EXTRAS[a]["family"] in "GHIJ")
FAM = {f: sorted(a for a in NEW if C.EXTRAS[a]["family"] == f) for f in "GHIJ"}
STATIONS = FAM["G"]
# real brands, banks, chains, networks and institutions that must never appear on a generated sign (a deny-list, not exhaustive; the rule is: generic words or the fictional chain only)
DENY = ["NNPC", "TOTAL", "TOTALENERGIES", "MOBIL", "OANDO", "CONOIL", "MRS", "SHELL", "ARDOVA", "FORTE", "ETERNA", "RAINOIL", "AA RANO", "BOVAS", "ZENITH", "GTBANK", "GTCO", "ACCESS BANK", "FIRST BANK", "UBA",
        "ECOBANK", "STANBIC", "JAIZ", "KFC", "MR BIGGS", "CHICKEN REPUBLIC", "SHOPRITE", "SPAR", "MTN", "GLO", "AIRTEL", "9MOBILE", "RED CROSS", "RED CRESCENT", "NIGERIA POLICE", "NPF", "TOYOTA", "HONDA", "INNOSON"]


@pytest.fixture(scope="module")
def built():
    out = {}
    for aid in NEW:
        sp = SPECS[aid]; a = build_scene(sp); res = validate.run(bpy, mathutils, sp["kind"], True, None, aid)
        out[aid] = (res, catalog_entry(sp, res), a.bake)
    return out


def test_the_four_families_and_their_versions():
    assert [len(FAM[f]) for f in "GHIJ"] == [3, 12, 8, 5], {f: len(v) for f, v in FAM.items()}
    assert all(SPECS[a]["version"] == 1 and SPECS[a]["surface"] and SPECS[a]["kind"] == "buildings" for a in NEW)
    for a in NEW: assert a.startswith(("kano_", "rg_ngn_")) and C.EXTRAS[a]["class"] in ("CITY_SPECIFIC", "REGIONAL"), a


@pytest.mark.parametrize("aid", NEW)
def test_every_new_asset_passes_the_validator_baked_within_the_building_budget(aid, built):
    res, entry, bake = built[aid]
    assert res["passed"], [f["message"] for f in res["findings"] if f["severity"] == "error"]
    t = res["stats"]["lod_triangles"]; assert len(t) == 3 and t[0] > t[1] > t[2] and t[2] <= 0.1 * t[0] and t[0] <= 6000, t
    assert res["stats"]["materials"] == [f"M_sb_{aid}_v001_l{lv}" for lv in (0, 1, 2)]
    assert bake[0]["coplanar_left_mixed"] == 0 and bake[0]["ppm"] >= 8.0, bake[0]
    lo, hi = res["stats"]["bounds_min_m"], res["stats"]["bounds_max_m"]; tx, ty, tz = SPECS[aid]["target"]
    assert abs((hi[0] - lo[0]) - tx) <= 0.15 * tx + 1.0 and abs((hi[1] - lo[1]) - ty) <= 0.15 * ty + 1.0, (aid, hi[0] - lo[0], hi[1] - lo[1], tx, ty)
    assert abs((lo[0] + hi[0]) / 2) <= 3.0 and abs((lo[1] + hi[1]) / 2) <= 3.0 and lo[2] >= -0.05, f"{aid}: origin near the lot centre, nothing below the ground"
    l = C.EXTRAS[aid]["light"]
    for h in l["heads"]:
        bx, by, bz = -h["x"], -h["z"], h["y"]; assert lo[0] - 1 <= bx <= hi[0] + 1 and lo[1] - 1 <= by <= hi[1] + 1 and 0 <= bz <= hi[2] + 1, (aid, h)
    assert entry["category"] == "buildings" and entry["collider"] == "box" and entry["id"] == aid


def test_the_stations_are_complete_environments_a_truck_fits_and_the_islands_have_lanes():
    for aid in STATIONS:
        socks = {s["name"]: s for s in C.EXTRAS[aid]["sockets"]}
        assert "entry" in socks and "exit" in socks and sum(1 for n in socks if n.startswith("pump_")) >= 1, aid
        assert "fuel_station" in C.EXTRAS[aid]["tags"]
    cols = SPECS["kano_fuel_station_large_a"]["cols"]; columns = [c for c in cols if (c[3] - c[0]) < 1.0 and (c[4] - c[1]) < 1.0 and c[5] > 5.0]
    assert len(columns) == 4 and all(c[5] >= 5.6 for c in columns), "canopy columns stand at least 5.6 m: a 4.2 m truck passes under the 5.8 m soffit"
    islands = sorted([c for c in cols if abs((c[3] - c[0]) - 1.4) < 0.01 and c[5] < 3.0], key=lambda c: c[0])
    assert len(islands) == 4 and all(b[0] - a[3] >= 5.0 for a, b in zip(islands, islands[1:])), "a 5 m lane between neighbouring islands"
    assert sum(1 for s in C.EXTRAS["kano_fuel_station_large_a"]["sockets"] if s["name"].startswith("pump_")) == 4


def test_every_sign_carries_generic_words_or_the_fictional_chain_and_never_a_real_brand_or_a_red_cross():
    for key, s in SB.SIGNS.items():
        text = " ".join(s["lines"]).upper()
        for bad in DENY: assert not re.search(r"(^|[^A-Z])" + re.escape(bad) + r"([^A-Z]|$)", text), f"{key}: {bad}"
        r, g, b = s["bg"]
        if "+" in text: assert not (r > 0.5 and g < 0.3 and b < 0.3) and not (s["fg"][0] > 0.5 and s["fg"][1] < 0.3), f"{key}: a red cross is a protected emblem"
    users = {k for k, s in SB.SIGNS.items() if "KANO OIL" in " ".join(s["lines"]).upper()}
    assert users and all(k.startswith("sign_kanooil") for k in users)


def test_a_painted_sign_shows_its_words():
    from kano_kit.geo import Part
    p = Part("t"); p.box(-3.0, -0.1, 0.0, 3.0, 0.1, 1.2, "sign_kanooil_fascia"); r = SB.bake(p, 256, ground=False); b = r["base"][..., :3].reshape(-1, 3)
    navy = np.abs(b - np.array([0.075, 0.11, 0.29])).sum(axis=1) < 0.12; white = b.min(axis=1) > 0.75; flame = (b[:, 0] > 0.6) & (b[:, 2] < 0.25)
    assert navy.mean() > 0.2 and white.mean() > 0.01 and flame.mean() > 0.002, (navy.mean(), white.mean(), flame.mean())
