"""The Kano residential street kit builds, and every asset passes the toolchain's own validation (budgets, LOD order, ground pivot, collision, names, UVs) with correctly wound (not inside-out) closed shells.
Needs the bpy module like the rest of the Blender job; builds in memory only (no workspace, no export)."""
import pytest

bpy = pytest.importorskip("bpy")
from kano_kit import build_all, geo, assemble  # noqa: E402


@pytest.mark.parametrize("asset_id", sorted(build_all.SPECS))
def test_each_kit_asset_passes_validation_with_outward_facing_shells(asset_id):
    r = build_all.produce(build_all.SPECS[asset_id], do_export=False)
    assert r["errors"] == [], r["errors"]
    assert r["inside_out_shells"] == {}, r["inside_out_shells"]
    assert r["passed"]


def test_every_placement_names_a_kit_asset_or_a_declared_review_only_part():
    P = build_all.build_scene()
    kit = set(build_all.SPECS)
    assert P.items, "the review scene placed nothing"
    unknown = {i["asset"] for i in P.items} - kit
    assert unknown == set(), f"placements name assets that are not in the kit: {sorted(unknown)}"


def test_the_street_section_matches_its_documented_cross_section():
    """The documented cross-section (docs/KANO_RESIDENTIAL_MILESTONE.md): carriageway 7.0 m, kerb 0.2 m, sidewalk 2.4 m, verge 1.1 m, wall line at 7.4 m, 96 m long."""
    from kano_kit import street
    assert (street.KERB_IN, street.KERB_OUT, street.WALK_OUT, street.VERGE_OUT, street.WALL_X, street.LEN) == (3.5, 3.7, 6.1, 7.3, 7.4, 96.0)
    assert round(street.KERB_IN * 2, 3) == 7.0
