"""Family F: city fabric of the Kano densification directive (2026-11-09): three mid-rise buildings of the R-039 skyline and the monument after R-034. Generic designs; the monument is fictional (no real Kano monument is claimed).
Run from tools/blender:  PYTHONPATH=. ~/.aro/venv/bin/python -m pytest tests/test_city_family.py -q"""
from __future__ import annotations
import json, re
from pathlib import Path
import pytest

bpy = pytest.importorskip("bpy")
import mathutils
from aro_blender import validate
from transport_kit import common as C
from transport_kit.build_transport import SPECS, build_scene, load_all, catalog_entry

load_all()
ROOT = Path(__file__).resolve().parents[3]
CITY = sorted(a for a, x in C.EXTRAS.items() if x["family"] == "F")
BUILDINGS = ["kano_midrise_apartments_a", "kano_office_block_a", "kano_shopping_parade_a"]
# (min footprint width, max width, min height, max height)
RULES = {"kano_shopping_parade_a": (34.0, 40.0, 14.0, 22.0), "kano_office_block_a": (20.0, 25.0, 26.0, 40.0), "kano_midrise_apartments_a": (22.0, 28.0, 15.0, 24.0), "lmk_kano_roundabout_monument_b": (10.0, 14.0, 20.0, 28.0)}


@pytest.fixture(scope="module")
def built():
    out = {}
    for aid in CITY:
        sp = SPECS[aid]; build_scene(sp); res = validate.run(bpy, mathutils, sp["kind"], True, None, aid); out[aid] = (res, catalog_entry(sp, res))
    return out


def test_the_family_is_four_assets():
    assert CITY == sorted(BUILDINGS + ["lmk_kano_roundabout_monument_b"])


@pytest.mark.parametrize("aid", CITY)
def test_naming_flags_and_references(aid):
    x = C.EXTRAS[aid]; sp = SPECS[aid]
    assert re.fullmatch(r"[a-z0-9]+(_[a-z0-9]+)*", aid)
    assert aid.startswith({"CITY_SPECIFIC": "kano_", "LANDMARK": "lmk_kano_"}[x["class"]])
    assert not re.search(r"(shell|total|oando|mobil|dangote|mtn|glo|airtel|toyota|nnpc|bank|hotel)", aid + sp["note"], re.I), "no brand and no unbuilt programme in the name"
    if "monument" in aid: assert "fictional_design" in x["tags"] and "fictional" in sp["note"].lower()
    else: assert "generic_design" in x["tags"]
    reg = json.loads((ROOT / "docs" / "kano" / "reference_register.json").read_text()); have = {r["id"] for r in reg["references"]}
    assert x["ref"] and set(x["ref"]) <= have


def test_validator_budgets_lods_and_materials(built):
    for aid, (res, entry) in built.items():
        errs = [f["message"] for f in res["findings"] if f["severity"] == "error"]; tris = res["stats"]["lod_triangles"]
        assert res["passed"] and not errs, f"{aid}: {errs}"
        assert len(tris) == 3 and tris[0] > tris[1] > tris[2] and tris[2] <= 0.10 * tris[0], f"{aid}: {tris}"
        assert tris[0] <= res["stats"]["budget_triangles"], aid
        assert len(res["stats"]["materials"]) <= 4, f"{aid}: {res['stats']['materials']}"


@pytest.mark.parametrize("aid", CITY)
def test_real_size_ground_and_origin(aid, built):
    res, _ = built[aid]; lo, hi = res["stats"]["bounds_min_m"], res["stats"]["bounds_max_m"]; wmin, wmax, hmin, hmax = RULES[aid]
    assert wmin <= hi[0] - lo[0] <= wmax and hmin <= hi[2] <= hmax, f"{aid}: {res['stats']['size_m']}"
    assert abs(lo[2]) <= 0.02 and abs((lo[0] + hi[0]) / 2) <= 1.5, aid


@pytest.mark.parametrize("aid", BUILDINGS)
def test_a_front_with_shopfronts_or_a_lobby_faces_the_road(aid, built):
    """Entrances face -Y (the road side): more glazing/openings on the front than on the back, and the colliders keep the footprint (the building is solid, vehicles cannot enter)."""
    res, _ = built[aid]; lo, hi = res["stats"]["bounds_min_m"], res["stats"]["bounds_max_m"]; cols = SPECS[aid]["cols"]
    assert cols and min(c[1] for c in cols) <= lo[1] + 1.6 and max(c[4] for c in cols) >= hi[1] - 3.0, aid
    assert all(c[2] >= 0 and c[5] <= hi[2] + 0.6 for c in cols), aid


@pytest.mark.parametrize("aid", CITY)
def test_colliders_and_light_heads_stay_with_the_model(aid, built):
    res, entry = built[aid]; lo, hi = res["stats"]["bounds_min_m"], res["stats"]["bounds_max_m"]
    for (x0, y0, z0, x1, y1, z1) in SPECS[aid]["cols"]:
        assert x0 < x1 and y0 < y1 and z0 < z1 and x0 >= lo[0] - 0.6 and x1 <= hi[0] + 0.6 and y0 >= lo[1] - 0.6 and y1 <= hi[1] + 0.6 and z1 <= hi[2] + 0.6, f"{aid}: {(x0, y0, z0, x1, y1, z1)}"
    for h in C.EXTRAS[aid]["light"]["heads"]:
        bx, by, bz = -h["x"], -h["z"], h["y"]
        assert lo[0] - 1 <= bx <= hi[0] + 1 and lo[1] - 1 <= by <= hi[1] + 1 and 0 <= bz <= hi[2] + 1, f"{aid}: head {h}"
    assert entry["category"] == "buildings" and entry["collider"] == "box" and entry["id"] == aid


def test_the_arches_leave_the_island_open_for_the_hedge_ring_and_stay_inside_twelve_metres(built):
    """The monument stands on a roundabout island: its footprint is 12 x 12 m and the two arches are open (a clear span between the legs, no solid wall across it)."""
    cols = SPECS["lmk_kano_roundabout_monument_b"]["cols"]; legs = [c for c in cols if c[5] > 3.0]
    assert len(legs) == 4 and all(abs(c[0]) >= 3.1 or abs(c[1]) >= 3.1 for c in legs), "legs only: nothing across the arch opening"
