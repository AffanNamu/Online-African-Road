"""The surface bake (transport_kit/surface.py): every face of a model painted into its own rectangle of an atlas, with measured ambient occlusion, weathering, a normal map and smoothness in the alpha channel.
The tests are about what must be TRUE of a bake, not about how it looks (a person judges that from the renders and the in-game pictures):
deterministic, UVs inside the atlas and islands that never share a texel, occlusion that is darker under a slab than in the open, buried faces removed, glass smoother than render, textures within the validator's 2048 px,
one material per LOD on every asset that asks for the bake. Run from tools/blender:  PYTHONPATH=. ~/.aro/venv/bin/python -m pytest tests/test_surface_bake.py -q"""
from __future__ import annotations
import numpy as np
import pytest

bpy = pytest.importorskip("bpy")
import mathutils
from aro_blender import validate, materials as AM
from kano_kit.geo import Part
from transport_kit import surface as SB, palette as PAL
from transport_kit.build_transport import SPECS, build_scene, load_all

load_all()
BAKED = sorted(a for a, sp in SPECS.items() if sp.get("surface"))
from transport_kit import common as _C
REALISM = sorted(a for a in BAKED if _C.EXTRAS[a]["family"] in "ABCDEF")           # the realism pass of 2026-11-13 (version 2 of assets that had a version 1)
CITY_NEW = sorted(a for a in SPECS if _C.EXTRAS[a]["family"] in "GHIJ")             # the city construction directive of 2026-11-14 (new ids, version 1)


def house():
    """A wall box on the ground with a slab oversailing it, a window pane, a door and a box buried inside the wall."""
    p = Part("t"); p.box(-4, -3, 0, 4, 3, 3, "plaster_cream", skip=("bottom",)); p.box(-5, -4, 3, 5, 4, 3.2, "concrete")
    p.box(-1, -3.05, 1.0, 1, -3, 2.2, "window_plain", skip=("bottom", "top", "back", "left", "right")); p.box(2, -3.05, 0, 3, -3, 2.1, "wood_door", skip=("bottom", "top", "back", "left", "right"))
    p.box(-1, -1, 0.5, 1, 1, 2.0, "paint_red"); return p


def test_every_pattern_key_is_a_library_colour_and_gives_sane_values():
    X = dict(P=np.random.default_rng(1).random((500, 3)) * 6.0, u=np.linspace(0, 2, 500), v=np.linspace(0, 2.4, 500), fw=np.full(500, 2.0), fh=np.full(500, 2.4), edge=np.linspace(0, 1, 500), fid=np.arange(500) % 7, nz=np.zeros(500), along=np.linspace(0, 9, 500))
    for key, fn in SB.PATTERN.items():
        assert key in AM.LIBRARY, key
        rgb, h, s = fn(X, np.array(AM.LIBRARY[key][0], dtype=float))
        assert rgb.shape == (500, 3) and h.shape == (500,) and np.shape(s) == (500,), key
        assert np.isfinite(rgb).all() and np.isfinite(h).all() and np.isfinite(s).all(), key
        assert rgb.min() > -0.2 and rgb.max() < 1.6 and np.abs(h).max() < 0.05 and 0.0 <= np.min(s) and np.max(s) <= 1.0, key


def test_the_bake_is_deterministic():
    a = SB.bake(house(), 256); b = SB.bake(house(), 256)
    assert np.array_equal(a["base"], b["base"]) and np.array_equal(a["normal"], b["normal"]) and a["ppm"] == b["ppm"]


def test_uvs_lie_in_the_atlas_and_no_two_faces_share_a_texel():
    p = house(); r = SB.bake(p, 256); uvl = p.bm.loops.layers.uv.verify(); S = 256; owner = np.full((S, S), -1)
    for i, f in enumerate(p.bm.faces):
        us = [l[uvl].uv.x for l in f.loops]; vs = [l[uvl].uv.y for l in f.loops]
        assert min(us) >= 0.0 and max(us) <= 1.0 and min(vs) >= 0.0 and max(vs) <= 1.0
        x0, x1, y0, y1 = int(np.floor(min(us) * S + 1e-6)), int(np.ceil(max(us) * S - 1e-6)), int(np.floor(min(vs) * S + 1e-6)), int(np.ceil(max(vs) * S - 1e-6))
        block = owner[y0:y1, x0:x1]; assert (block == -1).all(), f"face {i} shares texels with face {int(block.max())}"
        block[:] = i
    assert 0.3 < r["fill"] <= 1.0 and r["ppm"] > 5.0


def test_buried_faces_are_removed_and_visible_ones_kept():
    p = house(); n0 = len(p.bm.faces); r = SB.bake(p, 256)
    # the red box stands inside the closed wall volume (six faces) and the top of the wall is covered by the slab (one face): seven faces nobody can see. The underside of the slab where it oversails the wall
    # faces the ground, yet it is seen from the street: it must stay (a soffit, an arcade ceiling, a canopy).
    assert r["buried_faces_removed"] == 7, r["buried_faces_removed"]
    assert any(f.normal.z < -0.9 and f.calc_center_median().z > 2.9 for f in p.bm.faces), "the slab's soffit was removed"
    assert len(p.bm.faces) == n0 - r["buried_faces_removed"] and len(p.bm.faces) >= 12
    assert not [v for v in p.bm.verts if not v.link_faces]


def test_occlusion_is_darker_under_the_slab_and_at_the_foot_of_the_wall_than_on_the_open_roof():
    p = Part("t"); p.box(-4, -3, 0, 4, 3, 3, "plaster_cream", skip=("bottom",)); p.box(-5, -4, 3, 5, 4, 3.2, "plaster_cream"); p.bm.normal_update(); fr = SB._frames(p.bm); ao, _ = SB._occlusion(p.bm, fr)
    wall = next(g for F, g in zip(fr, ao) if abs(F["n"].y + 1) < 1e-6 and F["h"] > 2.5); roof = next(g for F, g in zip(fr, ao) if F["n"].z > 0.9 and F["z0"] > 3.1)
    assert roof.min() > 0.97, "an open roof sees the whole sky"
    assert wall[-1].mean() < wall[len(wall) // 2].mean() - 0.1, "the top of the wall, under the oversailing slab, is darker than its middle"
    assert wall[0].mean() < wall[len(wall) // 2].mean() - 0.03, "the foot of the wall is darkened by the ground"
    lifted, _ = SB._occlusion(p.bm, fr, ground=False); wl = next(g for F, g in zip(fr, lifted) if abs(F["n"].y + 1) < 1e-6 and F["h"] > 2.5)
    assert wl[0].mean() > wall[0].mean() + 0.03, "a piece that does not stand on the ground has no ground shadow"


def test_glass_is_smoother_than_render_and_the_normal_map_is_unit_length():
    p = house(); r = SB.bake(p, 256); a = r["base"][..., 3]
    assert a.max() > 0.8 and np.median(a) < 0.35, "glass texels are glossy, the wall is matt"
    n = r["normal"] * 2.0 - 1.0; ln = np.linalg.norm(n, axis=2); assert np.abs(ln - 1.0).max() < 1e-6 and (n[..., 2] > 0.2).all()
    h = SB._half(r["normal"]); assert h.shape == (128, 128, 3) and np.abs(np.linalg.norm(h * 2 - 1, axis=2) - 1.0).max() < 1e-6


def test_a_bake_that_cannot_fit_says_so():
    p = Part("t")
    for k in range(400): p.box(k * 0.3, 0, 0, k * 0.3 + 0.2, 0.2, 0.2, "paint_red")
    with pytest.raises(ValueError): SB.bake(p, 32)


def test_the_baked_assets_are_the_solid_ones_and_the_glass_shelters_stay_on_the_palette():
    assert len(REALISM) == 35 and len(CITY_NEW) == 28 and set(CITY_NEW) <= set(BAKED) and len(BAKED) == 35 + 28
    for must in ("kano_mosque_grand_a", "rg_ngn_mosque_traditional_b", "kano_shopping_parade_a", "kano_office_block_a", "kano_midrise_apartments_a", "rg_ngn_warehouse_hall_a", "kano_bus_terminal_hall_a", "aro_container_stack_3"): assert must in BAKED, must
    for must_not in ("aro_bus_shelter_a", "kano_bus_shelter_long_a", "aro_streetlight_arm_single_a", "aro_road_sign_stop_a", "aro_noise_wall_set", "aro_guardrail_post_set"): assert must_not not in BAKED, must_not
    for a in BAKED: assert all(32 <= s <= 2048 and s & (s - 1) == 0 for s in SPECS[a]["surface"]) and len(SPECS[a]["surface"]) == 3, a


@pytest.mark.parametrize("aid", ["rg_ngn_mosque_small_c", "kano_midrise_apartments_a", "aro_container_20ft_a"])
def test_a_baked_asset_has_one_atlas_material_per_lod_and_passes_the_validator(aid):
    sp = SPECS[aid]; a = build_scene(sp); res = validate.run(bpy, mathutils, sp["kind"], True, None, aid); v = sp["version"]
    assert res["passed"], [f["message"] for f in res["findings"] if f["severity"] == "error"]
    assert v == 2 and a.version == 2
    assert sorted(res["stats"]["materials"]) == sorted(f"M_sb_{aid}_v002_l{lv}" for lv in (0, 1, 2))
    for lv, o in a.lods.items():
        assert [s.material.name for s in o.material_slots] == [f"M_{SB.material_key(aid, lv, v)}"]
        bn, nn = SB.texture_names(aid, lv, v); assert bn == f"T_{aid}_v002_l{lv}_BaseColor"
        base = bpy.data.images[bn]; nrm = bpy.data.images[nn]; S = sp["surface"][lv]
        assert tuple(base.size) == (S, S) and tuple(nrm.size) == ((S // 2, S // 2) if S >= 128 else (S, S)) and base.colorspace_settings.name == "sRGB" and nrm.colorspace_settings.name == "Non-Color" and base.alpha_mode == "CHANNEL_PACKED"
        uv = o.data.uv_layers.active.data; assert all(0.0 <= d.uv.x <= 1.0 and 0.0 <= d.uv.y <= 1.0 for d in uv)
    assert a.bake[0]["ppm"] >= (9.0 if sp["kind"] == "buildings" else 20.0), a.bake[0]
    for lv, o in a.lods.items():                                                       # nothing left that the renderer could z-fight over, and no texel brighter than real paint
        assert a.bake[lv]["coplanar_left_mixed"] == 0, (aid, lv, a.bake[lv])
        px = np.array(bpy.data.images[SB.texture_names(aid, lv, v)[0]].pixels[:]).reshape(-1, 4)[:, :3]; assert px.max() <= SB.ALBEDO_MAX + 1.5 / 255, (aid, lv, px.max())
    e = AM.kit_json(); m = next(x for x in e["materials"] if x["name"] == f"M_sb_{aid}_v002_l0")
    assert m["texture"] == f"T_{aid}_v002_l0_BaseColor" and m["normal"] == f"T_{aid}_v002_l0_Normal" and m["smoothness_alpha"] is True


def test_versions_baked_assets_are_v002_in_a_fresh_workspace_and_the_rest_v001(tmp_path, monkeypatch):
    """The realism pass is version 2 of what it bakes (version 1 is frozen in the private library). A fresh workspace (the CI review) must label it so, and a workspace that already has v002 must go on to v003, never reuse it."""
    from transport_kit.build_transport import next_version
    from aro_blender import workspace as ws
    monkeypatch.delenv("ARO_VERSION_FLOOR", raising=False)
    assert all(SPECS[a]["version"] == 2 for a in REALISM) and all(sp["version"] == 1 for a, sp in SPECS.items() if a not in REALISM)
    assert next_version(tmp_path, SPECS["kano_fuel_station_large_a"]) == 1                                               # a new family starts at v001 even though it is baked
    assert next_version(tmp_path, SPECS["kano_mosque_grand_a"]) == 2 and next_version(tmp_path, SPECS["aro_bus_shelter_a"]) == 1
    v, d = ws.new_version_dir(tmp_path, "buildings", "kano_mosque_grand_a", floor=2); assert v == 2 and d.name == "v002"
    assert next_version(tmp_path, SPECS["kano_mosque_grand_a"]) == 3
    assert SB.material_key("x", 1, 3) == "sb_x_v003_l1"


def test_coplanar_overlapping_faces_are_found_and_separated_and_back_to_back_faces_are_not():
    """Two boxes that share a wall plane (the builders make solids by overlapping boxes) give faces in the same plane facing the same way: the renderer cannot order them, the surface flickers in the game and a
    ray-traced preview shows black patches (found on the pattern panel). The bake moves the smaller primitive 3 mm out; the larger one does not move."""
    p = Part("t"); p.box(0, 0, 0, 2, 1, 1, "paint_white"); p.box(1, 0, 0, 3, 1, 0.5, "paint_red")
    p.bm.normal_update(); pairs = SB.coplanar_overlaps(p.bm); assert len(pairs) >= 2, pairs              # the front (y = 0) and back (y = 1) planes at least
    big = [v.co.copy() for v in p.bm.verts][:8]; small = [v.co.copy() for v in p.bm.verts][8:]; n = SB.separate_coplanar(p.bm); assert n >= 1 and not SB.coplanar_overlaps(p.bm)
    assert [v.co for v in p.bm.verts][:8] == big, "the larger primitive stays where it is"
    moved = max((v.co - w).length for v, w in zip(list(p.bm.verts)[8:], small)); assert 0.002 < moved <= 0.02, moved     # the smaller one moved a few millimetres, no more
    q = Part("t"); q.box(0, 0, 0, 1, 1, 1, "paint_white"); q.box(0.2, -0.1, 0.2, 0.8, 0.0, 0.8, "paint_red")   # a relief on a wall: back to back, not a z-fight (the bake removes the buried back face instead)
    q.bm.normal_update(); assert SB.coplanar_overlaps(q.bm) == []


def test_the_pattern_panel_has_no_coplanar_faces_after_the_bake():
    sp = SPECS["rg_ngn_pattern_panel_a"]; p = sp["lods"][0](); p.bm.normal_update(); assert len(SB.coplanar_overlaps(p.bm)) >= 10      # the overlapping diamonds of the design
    r = SB.bake(p, 256); assert r["coplanar_separated"] >= 5 and SB.coplanar_overlaps(p.bm) == [] and r["coplanar_left_mixed"] == 0


def test_a_face_covered_by_a_larger_one_of_the_same_primitive_is_deleted_and_a_partial_one_is_kept():
    p = Part("t"); p.face_n([(0, 0, 0), (2, 0, 0), (2, 0, 1), (0, 0, 1)], "paint_white", (0, -1, 0)); p.face_n([(0, 0, 0), (2, 0, 0), (2, 0, 0.5), (0, 0, 0.5)], "paint_white", (0, -1, 0))   # shares the bottom edge: one primitive, wholly covered
    p.face_n([(0, 0, 0), (0, 1, 0), (0, 1, 1), (0, 0, 1)], "paint_white", (-1, 0, 0))
    p.bm.normal_update(); n0 = len(p.bm.faces); SB.separate_coplanar(p.bm); assert len(p.bm.faces) == n0 - 1 and not SB.coplanar_overlaps(p.bm)
    assert max(f.calc_area() for f in p.bm.faces) == pytest.approx(2.0), "the large face stays"
    q = Part("t"); q.face_n([(0, 0, 0), (2, 0, 0), (2, 0, 1), (0, 0, 1)], "paint_white", (0, -1, 0)); q.face_n([(0, 0, 0), (3, 0, 0), (3, 0, 0.5), (0, 0, 0.5)], "paint_red", (0, -1, 0))   # sticks out: partial, kept
    q.bm.normal_update(); SB.separate_coplanar(q.bm); assert len(q.bm.faces) == 2


def test_the_wall_behind_a_proud_sign_board_takes_the_value_of_the_open_wall_around_it():
    """A point of the wall 8 cm behind a sign sees almost no sky; it is hidden, but bilinear upsampling would smear its darkness half a metre onto the visible wall (black blotches round boards, sills and
    cornices in the first previews). Such samples are filled from the open ones."""
    p = Part("t"); p.box(-5, 0, 0, 5, 0.3, 6, "plaster_cream", skip=("bottom",)); p.box(-0.4, -0.2, 2.3, 0.4, -0.08, 2.7, "board_dark")      # a small sign on brackets, 8 cm off the wall
    p.bm.normal_update(); fr = SB._frames(p.bm); ao, _ = SB._occlusion(p.bm, fr, True)
    i = next(k for k, F in enumerate(fr) if F["n"].y < -0.9 and F["area"] > 50); F = fr[i]; g = ao[i]; nv, nu = g.shape
    xs = F["u0"] + F["w"] * (0.04 + 0.92 * np.arange(nu) / (nu - 1)); zs = F["v0"] + F["h"] * (0.04 + 0.92 * np.arange(nv) / (nv - 1))
    behind = g[np.ix_((zs > 2.3) & (zs < 2.7), (xs > -0.4) & (xs < 0.4))]; assert behind.size and behind.min() > 0.72, np.round(behind, 2)     # 0.65 without the rule
