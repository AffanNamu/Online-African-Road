"""The merged plot-frontage prototype (kano_kit.frontage) changes the number of renderers and nothing else: every face of every wall panel, pier and gate-pier lantern of a plot side, placed as the v002 review
scene places them, is a face of the merged frontage placed at its own placement, with the same material, and vice versa. Also: the five prototype assets pass the toolchain validation, and the merged
placement list accounts for every piece it replaces and leaves every other placement untouched. Needs the bpy module; builds in memory only."""
import math
import pytest

bpy = pytest.importorskip("bpy")
from mathutils import Matrix, Vector  # noqa: E402
from kano_kit import build_all, frontage as F, walls, assemble  # noqa: E402


@pytest.mark.parametrize("asset_id", sorted(F.SPECS))
def test_each_prototype_asset_passes_validation_with_outward_facing_shells(asset_id):
    r = build_all.produce(F.SPECS[asset_id], do_export=False)
    assert r["errors"] == [], r["errors"]
    assert r["inside_out_shells"] == {}, r["inside_out_shells"]
    assert r["passed"]


def _place(it) -> Matrix:
    return Matrix.Translation(Vector((it["x"], it["y"], it["z"]))) @ Matrix.Rotation(math.radians(it["yawDeg"]), 4, "Z")


def _signatures(part, m: Matrix) -> list:
    """One signature per face: the material key and the world-space corner set, rounded to a tenth of a millimetre."""
    out = []
    for f in part.bm.faces:
        out.append((part.keys[f.material_index], tuple(sorted(tuple(round(c, 4) for c in (m @ v.co)) for v in f.verts))))
    return sorted(out)


BUILDERS = {"aro_wall_compound_panel_a": lambda: walls.wall_panel(0, assemble.PANEL_LEN), "aro_pier_stone_a": lambda: walls.pier(0), "aro_lantern_wall_a": lambda: walls.lantern(0)}


@pytest.fixture(scope="module")
def scene_items():
    return build_all.build_scene().items


def _near_plot_items(items, q, side):
    """The individual v002 pieces of one near plot side, found the way the merged list finds them."""
    y0 = -48.0 + F.PLOT * q; out = []
    for it in items:
        if it["asset"] not in F.MERGED_FROM or (1 if it["x"] > 0 else -1) != side or abs(abs(it["x"]) - F.PIER_X) > 0.31: continue
        if it["asset"] == "aro_pier_stone_a" and not (-1e-3 <= it["y"] - y0 <= 4 * F.PITCH + 1e-3): continue
        if y0 - 1e-3 <= it["y"] < y0 + F.PLOT - 1e-3: out.append(it)
    return out


@pytest.mark.parametrize("q,side", [(0, 1), (0, -1), (1, 1), (1, -1)])
def test_the_merged_frontage_has_exactly_the_faces_of_the_individual_pieces(scene_items, q, side):
    merged = F.merged_items(scene_items)
    y0 = -48.0 + F.PLOT * q; fr = [m for m in merged if m["asset"].startswith("aro_frontage_") and abs(m["y"] - (y0 + F.PLOT / 2)) < 1e-6 and (1 if m["x"] > 0 else -1) == side]
    assert len(fr) == 1, f"plot {q} side {side}: expected one frontage placement, got {fr}"
    local_gate = int(fr[0]["asset"].split("_")[2][1]); sd, g = F.VARIANTS[fr[0]["asset"]]; assert sd == side and g == local_gate
    base = []
    for it in _near_plot_items(scene_items, q, side): base += _signatures(BUILDERS[it["asset"]](), _place(it))
    base = sorted(base); mine = _signatures(F.frontage(0, side, local_gate), _place(fr[0]))
    assert len(mine) == len(base), f"face counts differ: merged {len(mine)} against individual {len(base)}"
    assert mine == base, "the merged frontage differs from the individual pieces in at least one face"


def test_the_merged_list_accounts_for_every_replaced_piece_and_changes_nothing_else(scene_items):
    merged = F.merged_items(scene_items)
    kept = [i for i in scene_items if i["asset"] not in F.MERGED_FROM]
    for i in kept: assert i in merged, f"an unrelated placement was lost or changed: {i}"
    frontages = [m for m in merged if m["asset"].startswith("aro_frontage_") and m["asset"] != F.FAR_RUN]
    far = [m for m in merged if m["asset"] == F.FAR_RUN]
    assert len(frontages) == 8, "four plots on each side of the near section"
    assert len(far) == 24, "three far sections of eight plot sides"
    left = [m for m in scene_items if m["asset"] in F.MERGED_FROM]
    removed = len(scene_items) - len(kept)
    leftover = [m for m in merged if m["asset"] in F.MERGED_FROM]
    assert len(leftover) == 2, f"only the two end piers of the near section stay separate, got {leftover}"
    assert all(m["asset"] == "aro_pier_stone_a" for m in leftover)
    near_replaced = removed - len(leftover) - 5 * 24                                   # 24 far plot sides with five panels each
    assert near_replaced == 8 * (4 + 5 + 2), f"near pieces replaced: {near_replaced}"
    assert len(merged) == len(scene_items) - removed + len(leftover) + len(frontages) + len(far)


def test_every_merged_placement_names_an_asset_that_exists():
    ids = set(F.SPECS) | set(build_all.SPECS)
    items = build_all.build_scene().items
    unknown = {i["asset"] for i in F.merged_items(items)} - ids
    assert unknown == set(), unknown
