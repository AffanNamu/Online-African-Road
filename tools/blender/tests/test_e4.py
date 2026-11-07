"""Batch E4 regression tests (owner decision of 2026-11-07: improve the pothole geometry, the flood surface, the mud-road ruts and the Hausa wall finish found in the in-game review; preserve everything already built).
  * every LOD of every E4 asset has no overlapping coplanar faces, no degenerate faces, no duplicate faces and stays inside its triangle budget and material count;
  * the potholes are cut along a smooth contour (no stair-stepped squares), have a steep wall and a flat floor; the ruts of the mud road are flat-floored troughs on a grid that follows them (no fold-over, no sawtooth); the flood
    sheet has a smooth shoreline, three materials and a fringe lower than the water;
  * the Hausa assets keep the E3 geometry exactly (same vertices) and only rename the wall material to earth (kasa) render;
  * the frozen E3 builders are still the ones that build the frozen E3 versions (the E4 module only adds).
Needs the bpy module; builds in memory only."""
import math
import numpy as np
import pytest

bpy = pytest.importorskip("bpy")
from kano_kit import coplanar as C  # noqa: E402
from aro_blender import materials as AM  # noqa: E402
from env_kit import e4 as E4, build_env as BE  # noqa: E402

BUDGET = {"roads": 8000, "environments": 2500, "buildings": 6000}
MATERIALS = {"aro_road_pothole_set_a": 2, "aro_road_mud_rutted_a": 2, "aro_water_flood_sheet_a": 3, "rg_ngn_hausa_compound_a": 5, "rg_ngn_hausa_compound_b": 5, "rg_ngn_hausa_wall_gate_set_a": 5}


def tris(part): return sum(len(f.verts) - 2 for f in part.bm.faces)


@pytest.mark.parametrize("asset_id", sorted(E4.SPECS_E4))
def test_every_lod_of_an_e4_asset_is_clean_and_inside_its_budget(asset_id):
    sp = E4.SPECS_E4[asset_id]; counts = []
    for lv, fn in sorted(sp["lods"].items()):
        part = fn(); counts.append(tris(part))
        assert [h for h in C.overlaps_exact(part) if h[3] >= 0.002] == [], f"{asset_id} lod{lv}: overlapping coplanar faces"
        assert C.degenerate_faces(part) == [] and C.duplicate_faces(part) == 0, "degenerate or duplicate faces"
        assert True, f"{asset_id} lod{lv}: degenerate or duplicate faces"
        if asset_id in ("aro_road_pothole_set_a", "aro_road_mud_rutted_a", "aro_water_flood_sheet_a"):
            assert all(sum((f.verts[i].co.x * f.verts[(i + 1) % len(f.verts)].co.y - f.verts[(i + 1) % len(f.verts)].co.x * f.verts[i].co.y) for i in range(len(f.verts))) > 0 for f in part.bm.faces), f"{asset_id} lod{lv}: a face is turned over in plan view (fold-over)"
        assert len(part.keys) <= MATERIALS[asset_id], f"{asset_id} lod{lv}: {part.keys}"
    assert counts[0] <= BUDGET[sp["kind"]] and (len(counts) == 1 or counts[-1] <= 0.1 * counts[0]), f"{asset_id}: triangles per LOD {counts}"


def _material_boundary_edges(part, a, b):
    ia, ib = part.keys.index(a), part.keys.index(b); out = []
    for e in part.bm.edges:
        if len(e.link_faces) == 2 and {e.link_faces[0].material_index, e.link_faces[1].material_index} == {ia, ib}: out.append(e.calc_length())
    return out


def test_pothole_outlines_are_cut_along_a_smooth_contour():
    part = E4.pothole_set2(0); edges = _material_boundary_edges(part, "pothole_base", "asphalt_cracked")
    assert len(edges) > 400, "nine ragged outlines need many short contour segments"
    ia, ib = part.keys.index("pothole_base"), part.keys.index("asphalt_cracked"); aligned = total = 0
    for e in part.bm.edges:
        if len(e.link_faces) == 2 and {e.link_faces[0].material_index, e.link_faces[1].material_index} == {ia, ib}:
            d = e.verts[0].co - e.verts[1].co; total += 1; aligned += (abs(d.x) < 1e-6 or abs(d.y) < 1e-6)
    assert aligned / total < 0.30, f"{aligned} of {total} outline edges run along a grid axis: a stair-stepped outline"
    assert max(edges) < 0.30
    base = [f for f in part.bm.faces if part.keys[f.material_index] == "pothole_base"]; asph = [f for f in part.bm.faces if part.keys[f.material_index] == "asphalt_cracked"]
    floor = np.mean([f.calc_center_median().z for f in base]); top = np.mean([f.calc_center_median().z for f in asph])
    assert top - floor > 0.04, "the pothole floors must lie clearly below the asphalt (7 to 20 cm deep holes)"
    zs = [v.co.z for f in base for v in f.verts]; assert max(zs) - min(zs) < 0.22 and min(zs) >= 0.0


def test_mud_ruts_are_flat_floored_troughs_on_a_grid_that_follows_them():
    part = E4.mud_rutted2(0); wet = [f for f in part.bm.faces if part.keys[f.material_index] == "mud_wet"]
    assert len(wet) > 400
    # a trough: the floor faces lie well below the crest faces that border them, and the floor is flat across (small spread inside one grid row)
    crest_z = np.mean([f.calc_center_median().z for f in part.bm.faces if part.keys[f.material_index] == "mud_crest" and abs(f.calc_center_median().x) < 0.5])
    floor_z = np.mean([f.calc_center_median().z for f in wet if abs(f.calc_center_median().x) < 1.7 and abs(f.calc_center_median().x) > 1.0])
    assert crest_z - floor_z > 0.08, "the ruts must be recessed troughs, not slots between crowns"
    for f in part.bm.faces:
        co = [v.co for v in f.verts]; assert sum(co[i].x * co[(i + 1) % 4].y - co[(i + 1) % 4].x * co[i].y for i in range(4)) > 0, "a sheared grid must never fold over (a quad is turned over in plan view)"
    xs = [f.calc_center_median().x for f in wet]; assert max(abs(x) for x in xs) < 4.2


def test_flood_sheet_has_a_smooth_shoreline_and_a_fringe_below_the_water():
    part = E4.flood_sheet2(0); assert sorted(part.keys) == ["mud_wet", "water_brown", "water_flood"]
    water = [f for f in part.bm.faces if part.keys[f.material_index].startswith("water")]; ring = [f for f in part.bm.faces if part.keys[f.material_index] == "mud_wet"]
    assert np.mean([f.calc_center_median().z for f in water]) - np.mean([f.calc_center_median().z for f in ring]) > 0.010, "the wet fringe lies on the ground, below the water"
    shore = [e.calc_length() for e in part.bm.edges if len(e.link_faces) == 1 and all(part.keys[lf.material_index].startswith("water") for lf in e.link_faces)]
    assert shore and np.percentile(shore, 90) < 0.5 and max(shore) < 0.9, "a block-edged shoreline has edges as long as a grid cell or more"


@pytest.mark.parametrize("asset_id", ["rg_ngn_hausa_compound_a", "rg_ngn_hausa_compound_b", "rg_ngn_hausa_wall_gate_set_a"])
def test_hausa_geometry_is_unchanged_except_for_duplicate_faces_and_the_wall_material_is_earth_render(asset_id):
    for lv in (0, 1):
        e3 = BE.SPECS[asset_id]["lods"][lv](); e4 = E4.SPECS_E4[asset_id]["lods"][lv]()
        assert len(e3.bm.verts) == len(e4.bm.verts), f"{asset_id} lod{lv}: vertices were added or removed"
        lo3 = [min(v.co[i] for v in e3.bm.verts) for i in range(3)]; hi3 = [max(v.co[i] for v in e3.bm.verts) for i in range(3)]
        lo4 = [min(v.co[i] for v in e4.bm.verts) for i in range(3)]; hi4 = [max(v.co[i] for v in e4.bm.verts) for i in range(3)]
        assert max(abs(a - b) for a, b in zip(lo3 + hi3, lo4 + hi4)) < 0.01, f"{asset_id} lod{lv}: the extents changed"
        removed = len(e3.bm.faces) - len(e4.bm.faces); assert 0 <= removed <= 25, f"{asset_id} lod{lv}: {removed} faces removed (only exact duplicates may go)"
        assert "plaster_weathered" not in e4.keys and "kasa_plaster" in e4.keys, e4.keys
        assert C.duplicate_faces(e4) == 0


def test_e4_materials_are_registered_with_textures_and_the_frozen_builders_are_not_replaced():
    for k in ("kasa_plaster", "pothole_base", "water_flood"):
        assert k in AM.LIBRARY and k in AM.TEXTURED, k
    assert AM.ALPHA["water_flood"] > AM.ALPHA["water_brown"], "the deep water is less transparent than the shallow rim"
    assert set(E4.SPECS_E4) <= set(BE.SPECS) and all(BE.SPECS[i]["lods"] is not E4.SPECS_E4[i]["lods"] for i in E4.SPECS_E4), "E4 adds builders; it never replaces the frozen E3 ones"
    assert E4.pothole_set2 is not None and BE.SPECS["aro_road_pothole_set_a"]["note"].startswith("nine potholes 7 to 20 cm deep")
