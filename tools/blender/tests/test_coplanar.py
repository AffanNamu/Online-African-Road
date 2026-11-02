"""Overlapping coplanar faces (two boxes sharing a wall plane over an overlapping area) fight for the same pixels: Cycles renders them black, a game as flickering z-fighting. The detector finds them; the
separator moves the later face 3 mm proud. The environmental kit must have none; the frozen residential kit v002 has a KNOWN set, recorded here as a ceiling so it cannot grow (a corrected v003 lowers it)."""
import pytest

bpy = pytest.importorskip("bpy")
from kano_kit import coplanar as C, build_all  # noqa: E402
from kano_kit.geo import Part  # noqa: E402


def two_boxes_sharing_a_plane():
    p = Part("t"); p.box(0, 0, 1, 2, 1, 2, "concrete"); p.box(1, 0, 1, 3, 1, 2, "paint_white"); return p      # their -Y faces (and +Y, top, bottom) are coplanar over x 1..2


def test_overlapping_coplanar_faces_are_found():
    hits = C.overlaps(two_boxes_sharing_a_plane())
    assert len(hits) >= 4 and all(h[3] > 0 for h in hits)


def test_touching_boxes_and_opposite_faces_are_not_overlaps():
    p = Part("t"); p.box(0, 0, 1, 1, 1, 2, "concrete"); p.box(1, 0, 1, 2, 1, 2, "paint_white")                    # abutting: the shared face is at x = 1 with opposite normals, the rest only touch along an edge
    assert C.overlaps(p) == []
    q = Part("t"); q.box(0, 0, 1, 2, 0.2, 2, "concrete"); assert C.overlaps(q) == []                                # one slab: its two big faces face opposite ways


def test_undersides_on_the_ground_are_ignored():
    p = Part("t"); p.box(0, 0, 0, 2, 1, 1, "concrete"); p.box(1, 0, 0, 3, 1, 1, "concrete")
    assert all(h[2][:3] != (0.0, 0.0, -1.0) for h in C.overlaps(p))


def test_separation_removes_the_overlap_and_moves_only_the_later_face_by_3_mm():
    p = two_boxes_sharing_a_plane(); n = C.separate(p)
    assert n > 0 and C.overlaps(p) == []
    ys = sorted({round(v.co.y, 4) for v in p.bm.verts}); assert min(ys) == pytest.approx(-0.003, abs=1e-4) and max(ys) == pytest.approx(1.003, abs=1e-4)   # the second box's faces stand 3 mm proud


def test_separation_of_a_clean_mesh_changes_nothing():
    p = Part("t"); p.box(0, 0, 0, 1, 1, 1, "concrete"); before = sorted(tuple(round(c, 6) for c in v.co) for v in p.bm.verts)
    assert C.separate(p) == 0 and sorted(tuple(round(c, 6) for c in v.co) for v in p.bm.verts) == before


def test_three_stacked_overlaps_get_distinct_levels():
    p = Part("t")
    for k in range(3): p.box(0, 0, 1, 2, 1, 2, "concrete")
    C.separate(p); assert C.overlaps(p) == []


def test_the_environmental_kit_has_no_overlapping_coplanar_faces():
    from env_kit import build_env
    bad = {aid: len([h for h in C.overlaps(sp["lods"][0]()) if h[3] >= 0.002]) for aid, sp in build_env.SPECS.items()}
    assert {k: v for k, v in bad.items() if v} == {}, bad


# The residential kit v002 is FROZEN: its overlaps are known and are not changed here. A corrected v003 must lower these numbers; they must never go up.
RESIDENTIAL_KNOWN = {"aro_villa_2storey_hip_a": 62, "aro_villa_2storey_hip_b": 69, "aro_gate_ornate_steel_a": 6, "aro_lantern_wall_a": 1, "aro_sign_oneway_a": 16}


def test_the_frozen_residential_kit_has_no_more_overlaps_than_recorded():
    for aid, sp in build_all.SPECS.items():
        n = len([h for h in C.overlaps(sp["lods"][0]()) if h[3] >= 0.002])
        assert n <= RESIDENTIAL_KNOWN.get(aid, 0), f"{aid}: {n} overlapping coplanar face pairs, recorded {RESIDENTIAL_KNOWN.get(aid, 0)}"
