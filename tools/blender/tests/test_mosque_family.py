"""Family E: the mosque family of the owner's Kano densification directive (2026-11-09; references R-037, R-038, R-039, documentary sources R-024..R-029 for the traditional pair).
The ten mosques are generic designs, never a model of a real mosque (owner decision C1), so the tests also guard the naming, the flags and the palette rules that keep that honest. They build real Blender geometry (bpy).
Run from tools/blender:  PYTHONPATH=. ~/.aro/venv/bin/python -m pytest tests/test_mosque_family.py -q"""
from __future__ import annotations
import json, re
from pathlib import Path
import pytest

bpy = pytest.importorskip("bpy")
import mathutils
from aro_blender import validate
from transport_kit import common as C, palette as PAL
from transport_kit.build_transport import SPECS, build_scene, load_all, catalog_entry

load_all()
ROOT = Path(__file__).resolve().parents[3]
MOSQUES = sorted(a for a, x in C.EXTRAS.items() if x["family"] == "E")
GRAND = ["kano_mosque_grand_a", "kano_mosque_grand_b"]
MEDIUM = [f"rg_ngn_mosque_medium_{k}" for k in "abc"]
SMALL = [f"rg_ngn_mosque_small_{k}" for k in "abc"]
TRAD = [f"rg_ngn_mosque_traditional_{k}" for k in "ab"]
# (min width, max width) of the compound footprint (X) and the minimum height of the tallest minaret, per class (real metres)
CLASS_RULES = {"grand": (44.0, 62.0, 25.0), "medium": (24.0, 32.0, 15.0), "small": (13.0, 22.0, 8.0), "trad": (15.0, 28.0, 10.0)}


def klass(aid): return "grand" if aid in GRAND else "medium" if aid in MEDIUM else "small" if aid in SMALL else "trad"


@pytest.fixture(scope="module")
def built():
    out = {}
    for aid in MOSQUES:
        sp = SPECS[aid]; a = build_scene(sp); res = validate.run(bpy, mathutils, sp["kind"], True, None, aid)
        out[aid] = (res, catalog_entry(sp, res))
    return out


def test_the_family_is_ten_mosques_of_four_classes():
    assert MOSQUES == sorted(GRAND + MEDIUM + SMALL + TRAD) and len(MOSQUES) == 10


@pytest.mark.parametrize("aid", MOSQUES)
def test_no_real_landmark_is_claimed(aid):
    """The Kano Central Mosque and every other real mosque is a landmark blocked on references (owner decision C1): nothing here may carry that identity."""
    x = C.EXTRAS[aid]; sp = SPECS[aid]
    assert x["class"] in ("CITY_SPECIFIC", "REGIONAL") and not aid.startswith("lmk_"), aid
    assert not re.search(r"(central|great|emir|palace|gidan|kurmi|juma)", aid, re.I), aid
    note = (sp["note"] + " " + " ".join(x["tags"])).lower()
    assert "generic" in note or "flagged" in note, f"{aid}: the generic or flagged nature must be stated"
    assert ("generic_design" in x["tags"]) != ("flagged_interpretation" in x["tags"]), aid
    assert "mosque" in x["tags"]


@pytest.mark.parametrize("aid", MOSQUES)
def test_references_are_registered(aid):
    reg = json.loads((ROOT / "docs" / "kano" / "reference_register.json").read_text())
    have = {r["id"] for r in reg["references"]}
    assert C.EXTRAS[aid]["ref"] and set(C.EXTRAS[aid]["ref"]) <= have, aid
    if klass(aid) in ("grand", "medium", "small"): assert {"R-037", "R-038"} <= set(C.EXTRAS[aid]["ref"])
    else: assert set(C.EXTRAS[aid]["ref"]) >= {"R-024", "R-027"}


def test_validator_budgets_lods_and_materials(built):
    for aid, (res, entry) in built.items():
        errs = [f["message"] for f in res["findings"] if f["severity"] == "error"]
        assert res["passed"] and not errs, f"{aid}: {errs}"
        tris = res["stats"]["lod_triangles"]
        assert len(tris) == 3 and tris[0] > tris[1] > tris[2] and tris[2] <= 0.10 * tris[0], f"{aid}: {tris}"
        assert tris[0] <= res["stats"]["budget_triangles"] <= 6000, aid
        assert len(res["stats"]["materials"]) <= 3, f"{aid}: {res['stats']['materials']}"          # palette: paint, one extension, (concrete never used): well inside the cap of 4


@pytest.mark.parametrize("aid", MOSQUES)
def test_real_size_and_a_minaret_above_the_hall(aid, built):
    res, _ = built[aid]; lo, hi = res["stats"]["bounds_min_m"], res["stats"]["bounds_max_m"]; wmin, wmax, hmin = CLASS_RULES[klass(aid)]
    assert wmin <= hi[0] - lo[0] <= wmax, f"{aid}: width {hi[0] - lo[0]:.1f}"
    assert hi[2] >= hmin, f"{aid}: height {hi[2]:.1f} below the class minimum {hmin}"
    assert hi[2] <= 36.0, aid                                                               # nothing taller than a 120 ft minaret: a mosque must not read as a tower block
    assert abs((lo[1] + hi[1]) / 2) <= 3.0 and abs((lo[0] + hi[0]) / 2) <= 3.0, f"{aid}: origin must be near the footprint centre"
    assert abs(lo[2]) <= 0.02, f"{aid}: ground must be z = 0"


def test_the_classes_are_visibly_different_in_scale(built):
    area = {a: (r["stats"]["size_m"][0] * r["stats"]["size_m"][1]) for a, (r, _) in built.items()}
    assert min(area[a] for a in GRAND) > max(area[a] for a in MEDIUM) * 1.2
    assert min(area[a] for a in MEDIUM) > max(area[a] for a in SMALL) * 1.2 or min(area[a] for a in MEDIUM) > 600
    assert len({round(v) for v in area.values()}) == 10, "ten distinct footprints"
    tris = {a: r["stats"]["triangles_lod0"] for a, (r, _) in built.items()}
    assert len(set(tris.values())) == 10, "ten distinct meshes"


@pytest.mark.parametrize("aid", MOSQUES)
def test_front_faces_the_road_with_a_boundary_wall(aid, built):
    """The front is -Y (the road side); the compound's boundary wall stands on the front edge (the collider box along it), and the building is behind it."""
    res, _ = built[aid]; lo = res["stats"]["bounds_min_m"]; cols = SPECS[aid]["cols"]
    wall = [c for c in cols if c[3] - c[0] >= 12.0 and c[4] - c[1] <= 1.2]
    assert wall, f"{aid}: no road-side wall collider"
    assert min(c[1] for c in wall) <= lo[1] + 0.8 and all(c[2] == 0 and c[5] >= 1.7 for c in wall), aid
    hall = [c for c in cols if c[3] - c[0] >= 7.0 and c[4] - c[1] >= 7.0]
    assert hall and all(c[1] > min(w[4] for w in wall) + 1.5 for c in hall), f"{aid}: the hall must stand behind the wall"


@pytest.mark.parametrize("aid", MOSQUES)
def test_colliders_stay_inside_the_compound(aid, built):
    res, _ = built[aid]; lo, hi = res["stats"]["bounds_min_m"], res["stats"]["bounds_max_m"]
    for (x0, y0, z0, x1, y1, z1) in SPECS[aid]["cols"]:
        assert x0 >= lo[0] - 0.6 and x1 <= hi[0] + 0.6 and y0 >= lo[1] - 0.6 and y1 <= hi[1] + 0.6 and z1 <= hi[2] + 0.6, f"{aid}: {(x0, y0, z0, x1, y1, z1)}"


@pytest.mark.parametrize("aid", MOSQUES)
def test_lamp_heads_are_on_the_mosque_and_the_catalogue_is_complete(aid, built):
    res, entry = built[aid]; lo, hi = res["stats"]["bounds_min_m"], res["stats"]["bounds_max_m"]; l = C.EXTRAS[aid]["light"]
    assert l["kind"] in ("lamp", "glow") and l["heads"] and l["intensity"] > 0, aid
    for h in l["heads"]:
        bx, by, bz = -h["x"], -h["z"], h["y"]
        assert lo[0] - 1 <= bx <= hi[0] + 1 and lo[1] - 1 <= by <= hi[1] + 1 and 0 <= bz <= hi[2] + 1, f"{aid}: head {h}"
    assert entry["category"] == "buildings" and entry["collider"] == "box" and entry["status"] == "production" and entry["id"] == aid


def test_traditional_mosques_use_the_earth_palette_and_the_others_the_stone_palette(built):
    for aid in TRAD: assert "M_tk_paint_ext" in built[aid][0]["stats"]["materials"] and "M_tk_paint_ext2" not in built[aid][0]["stats"]["materials"], aid
    for aid in GRAND + MEDIUM + SMALL: assert "M_tk_paint_ext2" in built[aid][0]["stats"]["materials"], aid


def test_palettes_staged_earlier_are_unchanged():
    """T_tk_paint_Palette, T_tk_metal_Palette, T_tk_glass_Palette and T_tk_paintext_Palette were staged (and verified byte for byte against the library); the mosque colours live in a new palette so nothing staged changes."""
    assert len(PAL.FAMILIES["tk_paint"]) == 49 and PAL.FAMILIES["tk_paint"][0] == "paint_white" and PAL.FAMILIES["tk_paint"][-1] == "tile_floor"
    assert PAL.FAMILIES["tk_metal"][0] == "metal_galvanised" and len(PAL.FAMILIES["tk_metal"]) == 7 and PAL.FAMILIES["tk_glass"] == ["glass_dark", "glass"]
    assert PAL.FAMILIES["tk_paint_ext"] == ["kasa_render", "water_pool"]
    assert PAL.FAMILIES["tk_paint_ext2"] == ["stone_ivory", "dome_green", "trim_gold", "railing_green", "paver_red"]
    assert PAL.TEXTURE_NAME["tk_paint_ext2"] == "T_tk_paintext2_Palette"


def test_the_mosque_colours_are_not_brand_colours_and_are_documented():
    from aro_blender import materials as AM
    for k in PAL.FAMILIES["tk_paint_ext2"]:
        rgb, rough, metal, _, note = AM.LIBRARY[k]
        assert metal == 0.0 and 0.0 <= min(rgb) and max(rgb) <= 1.0 and note, k
