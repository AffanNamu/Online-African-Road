"""Residential kit v003 regression tests (owner request of 2026-11-03: the geometry defects found in v002 must not come back, and v002 itself must not change).
  * every LOD of every v003 asset has NO overlapping coplanar faces (they render black in Cycles and z-fight in a game), no degenerate faces and no duplicate faces;
  * every v003 asset passes the toolchain validation with correctly wound shells, and stays within the material count recorded for it (so a material cannot creep back in);
  * palette faces have a constant UV at their cell centre and the palette textures hold the colours of the v002 materials they replace;
  * the v002 builders are unchanged: the geometry hash of every v002 asset equals the one recorded when v002 was frozen (a change to a frozen builder fails here, before it can change a frozen export).
Needs the bpy module; builds in memory only."""
import json
from pathlib import Path
import pytest

bpy = pytest.importorskip("bpy")
from kano_kit import build_all, build3, coplanar as C, palette as PAL, veg3  # noqa: E402

MAX_MATERIALS = {"kano_street_oneway_section_a": 5, "aro_villa_2storey_hip_a": 7, "aro_villa_2storey_hip_b": 7, "aro_wall_compound_panel_a": 2, "aro_pier_stone_a": 2, "aro_gate_ornate_steel_a": 1, "aro_lantern_wall_a": 2,
                 "aro_streetlight_single_arm_a": 2, "aro_pole_electric_concrete_span_a": 3, "aro_sign_oneway_a": 2, "rg_ngn_tree_street_broadleaf_a": 1, "rg_ngn_tree_street_broadleaf_b": 1, "aro_hedge_clipped_a": 1, "aro_shrub_ixora_red_a": 1}
HASHES = Path(__file__).parent / "v002_geometry_hashes.json"


@pytest.mark.parametrize("asset_id", sorted(build3.SPECS))
def test_every_lod_of_a_v003_asset_is_free_of_the_geometry_defects(asset_id):
    for lv, fn in sorted(build3.SPECS[asset_id]["lods"].items()):
        part = fn()
        ov = [h for h in C.overlaps(part) if h[3] >= 0.002]
        assert ov == [], f"{asset_id} lod{lv}: {len(ov)} overlapping coplanar face pairs, largest {max(h[3] for h in ov)} m2"
        assert C.degenerate_faces(part) == [], f"{asset_id} lod{lv}: degenerate (zero-area) faces"
        assert C.duplicate_faces(part) == 0, f"{asset_id} lod{lv}: the same face built twice"


@pytest.mark.parametrize("asset_id", sorted(build3.SPECS))
def test_each_v003_asset_validates_and_keeps_its_material_count(asset_id):
    r = build_all.produce(build3.SPECS[asset_id], do_export=False, extra_pbr=build3.apply_all, leaf=False)
    assert r["errors"] == [] and r["inside_out_shells"] == {} and r["passed"], (r["errors"], r["inside_out_shells"])
    n = len(r["stats"]["materials"]); assert n <= MAX_MATERIALS[asset_id], f"{asset_id}: {n} materials ({r['stats']['materials']}), recorded {MAX_MATERIALS[asset_id]}"


def test_v003_villas_have_fewer_materials_than_v002_and_stay_within_the_triangle_budget():
    for aid in ("aro_villa_2storey_hip_a", "aro_villa_2storey_hip_b"):
        old = build_all.produce(build_all.SPECS[aid], do_export=False); new = build_all.produce(build3.SPECS[aid], do_export=False, extra_pbr=build3.apply_all, leaf=False)
        assert len(new["stats"]["materials"]) <= len(old["stats"]["materials"]) - 3, (aid, new["stats"]["materials"])
        assert new["stats"]["lod_triangles"][0] <= 6000                                             # the game's own catalogue budget for a building


def test_palette_faces_sit_at_their_cell_centre_and_the_palette_holds_the_old_colours():
    part = build3.SPECS["aro_villa_2storey_hip_a"]["lods"][0](); uvl = part.bm.loops.layers.uv.verify(); fam_cells = {fam: {tuple(round(c, 4) for c in PAL.cell_uv(k)) for k in ks} for fam, ks in PAL.FAMILIES.items()}
    n = 0
    for f in part.bm.faces:
        k = part.keys[f.material_index]
        if k in PAL.FAMILIES:
            uvs = {tuple(round(c, 4) for c in l[uvl].uv) for l in f.loops}; assert len(uvs) == 1 and next(iter(uvs)) in fam_cells[k], (k, uvs); n += 1
    assert n > 200, "the villa should have hundreds of palette faces"
    from aro_blender import materials as AM
    px = PAL._palette_pixels("kano_paint")
    for i, k in enumerate(PAL.FAMILIES["kano_paint"]):
        cx, cy = i % PAL.GRID, i // PAL.GRID; got = px[cy * PAL.CELL + 8, cx * PAL.CELL + 8, :3]
        assert all(abs(a - b) < 1e-6 for a, b in zip(got, AM.LIBRARY[k][0])), k
    g = PAL._palette_pixels("kano_glass")
    assert abs(g[8, 8, 3] * AM.ALPHA["kano_glass"] - AM.ALPHA["glass_dark"]) < 1e-3 and abs(g[8, 24, 3] * AM.ALPHA["kano_glass"] - AM.ALPHA["glass"]) < 2e-3        # window glass 0.42 and balcony glass 0.22 as in v002


def test_vegetation_atlas_keeps_the_v002_leaf_atlas_in_its_left_half():
    from kano_kit import textures as KT
    px = veg3.atlas_pixels(); assert px.shape == (512, 1024, 4)
    assert (px[:, :512, :] == KT.leaf_pixels()).all(), "the leaf clusters must be the v002 atlas, unchanged"


def geometry_hash(part) -> str:
    import hashlib
    rows = sorted((part.keys[f.material_index], tuple(sorted(tuple(round(c, 4) for c in v.co) for v in f.verts))) for f in part.bm.faces)
    return hashlib.sha256(json.dumps(rows).encode()).hexdigest()


def test_the_frozen_v002_builders_still_produce_the_recorded_geometry():
    """The hashes were recorded from the builders that produced the frozen v002 exports (whose bytes were verified identical on a fresh rebuild). If this fails, a v002 builder (or something it uses) changed."""
    rec = json.loads(HASHES.read_text())
    for aid, sp in build_all.SPECS.items():
        for lv, fn in sorted(sp["lods"].items()):
            assert geometry_hash(fn()) == rec[f"{aid}:{lv}"], f"{aid} lod{lv}: the v002 builder no longer produces the frozen geometry"
