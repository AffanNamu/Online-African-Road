"""The environmental kit builds, and every asset passes the toolchain's own validation (budgets, LOD order, ground pivot, names, UVs) with correctly wound closed shells; ground patches record their datum
and use only declared kit materials. Needs the bpy module like the rest of the Blender job; builds in memory only."""
import pytest

bpy = pytest.importorskip("bpy")
from kano_kit import build_all as KB  # noqa: E402
from env_kit import build_env as BE, textures as ET  # noqa: E402
from aro_blender import materials as AM  # noqa: E402


@pytest.mark.parametrize("asset_id", sorted(BE.SPECS))
def test_each_environmental_asset_passes_validation_with_outward_facing_shells(asset_id):
    r = KB.produce(BE.SPECS[asset_id], do_export=False, extra_pbr=ET.apply_env_pbr)
    assert r["errors"] == [], r["errors"]
    assert r["inside_out_shells"] == {}, r["inside_out_shells"]
    assert r["passed"]


def test_ground_patches_record_a_datum_and_stay_within_a_sane_relief():
    for aid in ("aro_road_mud_rutted_a", "aro_road_earth_compacted_a", "aro_road_sand_loose_a", "aro_road_asphalt_cracked_a", "aro_road_pothole_set_a"):
        KB.produce(BE.SPECS[aid], do_export=False, extra_pbr=ET.apply_env_pbr)
        assert 0.0 < BE.DATUM[aid] < 0.6, (aid, BE.DATUM[aid])               # how far above its origin the nominal surface sits: the deepest rut or hole defines z = 0


def test_every_environmental_material_is_declared_in_the_kit_and_every_texture_entry_has_a_generator():
    used = set()
    for sp in BE.SPECS.values():
        r = KB.produce(sp, do_export=False, extra_pbr=ET.apply_env_pbr); used |= set(r["stats"]["materials"])
    names = {f"M_{k}" for k in AM.LIBRARY}
    assert {m for m in used if m not in names} == set(), "an asset uses a material that is not in the kit"
    gen = {"mud_wet", "mud_crest", "earth_compacted", "sand_dusty", "asphalt_cracked", "roof_zinc_rust", "mud_brick", "plaster_weathered", "timber_weathered", "water_brown", "water_rain"}
    assert {k for k in AM.TEXTURED if "T_env_" in AM.TEXTURED[k]["texture"]} == gen
