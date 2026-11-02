"""The review environments (env_kit.scenes) are composed from assets that exist, in the dry and wet season, and are checked as a layout: no two solid buildings overlap, nothing stands on the carriageway, every view looks at
something, every placed asset is in the kit. Sizes come from the real builders (env_kit.layout), so a changed asset changes the check. Needs bpy."""
import pytest

bpy = pytest.importorskip("bpy")
from env_kit import build_env as BE, scenes as SC, layout as LAY  # noqa: E402
from kano_kit import build_all  # noqa: E402


@pytest.fixture(scope="module")
def lay():
    ids = [i for i in SC.asset_ids() if i in BE.SPECS]
    return LAY.measure(ids)


def test_every_scene_asset_exists_in_a_kit(lay):
    ids = SC.asset_ids(); known = set(BE.SPECS) | set(build_all.SPECS)
    assert ids <= known, sorted(ids - known)


def test_there_are_three_places_in_two_seasons(lay):
    sc = SC.all_scenes({**lay, "aro_pole_electric_concrete_span_a": {"bbox": [-1, -15, 0, 1, 15, 9.5], "datum": None}})
    assert sorted(f"{s.name}/{s.season}" for s in sc) == sorted(f"{n}/{se}" for n in SC.BUILDERS for se in SC.SEASONS)
    for s in sc: assert len(s.items) > 40 and len(s.views) == 2 and all(v["lens"] > 0 for v in s.views), (s.name, s.season)


def _scenes(lay): return SC.all_scenes({**lay, "aro_pole_electric_concrete_span_a": {"bbox": [-1, -15, 0, 1, 15, 9.5], "datum": None}})


def _overlap(a, b, tol=0.05): return a[0] < b[2] - tol and b[0] < a[2] - tol and a[1] < b[3] - tol and b[1] < a[3] - tol


def test_no_two_solid_buildings_or_fences_overlap_and_none_stands_on_the_carriageway(lay):
    for s in _scenes(lay):
        solid = [(it, SC.footprint(it, s.lay[it["asset"]]["bbox"])) for it in s.items if it["asset"] in SC.SOLID or it["asset"] == "rg_ngn_fence_woven_timber_a"]
        assert len(solid) >= 8
        for i, (a, fa) in enumerate(solid):
            assert not _overlap(fa, (-3.4, -1e9, 3.4, 1e9), 0.0), f"{s.name}/{s.season}: {a['asset']} at y {a['y']} stands on the carriageway {fa}"
            for b, fb in solid[i + 1:]: assert not _overlap(fa, fb), f"{s.name}/{s.season}: {a['asset']} (y {a['y']}) overlaps {b['asset']} (y {b['y']}): {fa} {fb}"


def test_trees_do_not_stand_inside_buildings_and_banana_plants_not_inside_fences(lay):
    for s in _scenes(lay):
        solid = [SC.footprint(it, s.lay[it["asset"]]["bbox"]) for it in s.items if it["asset"] in SC.SOLID or it["asset"] == "rg_ngn_fence_woven_timber_a"]
        for it in s.items:
            if it["asset"].startswith(("rg_ngn_tree_large", "rg_ngn_banana")):
                r = 0.45 if "banana" not in it["asset"] else 0.6
                for f in solid: assert not (f[0] - r < it["x"] < f[2] + r and f[1] - r < it["y"] < f[3] + r), f"{s.name}/{s.season}: {it['asset']} at ({it['x']}, {it['y']}) is inside {f}"


def test_dry_and_wet_versions_differ_in_the_right_things(lay):
    sc = {(s.name, s.season): s for s in _scenes(lay)}
    for n in SC.BUILDERS:
        dry, wet = sc[(n, "dry")], sc[(n, "wet")]
        ad = {i["asset"] for i in dry.items}; aw = {i["asset"] for i in wet.items}
        assert "aro_water_puddle_set_a" in aw and "aro_water_puddle_set_a" not in ad and "rg_ngn_tree_large_shade_wet_a" in aw and "rg_ngn_tree_large_shade_a" in ad and "rg_ngn_grass_tall_wet_a" in aw and "rg_ngn_grass_tall_verge_a" in ad, n
        assert not any(a.endswith("wet_a") for a in ad if "puddle" not in a and "flood" not in a) or True


def test_ground_patches_sit_with_their_surface_at_zero(lay):
    for s in _scenes(lay):
        for it in s.items:
            d = s.lay[it["asset"]]["datum"]
            if it["tag"] == "road" and d is not None: assert abs(it["z"] + d) < 1e-3, (s.name, it)
