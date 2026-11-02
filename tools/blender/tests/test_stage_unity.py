"""The Unity-side staging copies exactly what the game's editor code reads: the LATEST version of every asset's FBX and manifest, the shared textures once, the material kit and the placements.
Pure file handling (no Blender needed), tested on a synthetic workspace."""
import json
import pytest
from kano_kit import stage_unity as S


def make_ws(tmp_path):
    W = tmp_path / "ws"
    for aid, kind in (("aro_a", "props"), ("rg_b", "vegetation")):
        for v in ("v001", "v002"):
            d = W / "exports" / "fbx" / aid / v; d.mkdir(parents=True); (d / f"{aid}.fbx").write_text(f"{aid}-{v}-fbx"); (d / f"{aid}.manifest.json").write_text("{}"); (d / "T_stray.png").write_text("not copied")
            g = W / "exports" / "glb" / aid / v; g.mkdir(parents=True); (g / f"{aid}.glb").write_text("glb")
            p = W / "projects" / kind / aid / v; p.mkdir(parents=True); (p / f"T_{aid}_BaseColor.png").write_text(f"{aid}-{v}-png"); (p / f"{aid}.blend").write_text("blend")
    (W / "exports" / "unity").mkdir(parents=True); (W / "exports" / "unity" / "aro_materials.json").write_text(json.dumps({"materials": []}))
    return W


def test_the_latest_version_is_staged_with_its_textures_materials_and_placements(tmp_path):
    W = make_ws(tmp_path); out = tmp_path / "ARO"
    staged = S.stage(W, out, {"items": [{"asset": "aro_a"}]})
    assert staged == {"aro_a": "v002", "rg_b": "v002"}
    assert (out / "Models" / "aro_a" / "v002" / "aro_a.fbx").read_text() == "aro_a-v002-fbx"
    assert not (out / "Models" / "aro_a" / "v001").exists(), "an older version must not be staged"
    assert sorted(p.name for p in (out / "Models" / "aro_a" / "v002").iterdir()) == ["aro_a.fbx", "aro_a.manifest.json"], "no glb and no texture next to the model"
    assert (out / "Textures" / "kano_kit" / "T_aro_a_BaseColor.png").read_text() == "aro_a-v002-png", "textures come from the latest version"
    assert (out / "Materials" / "aro_materials.json").is_file()
    assert json.loads((out / "placements" / "kano_residential_section_a.json").read_text())["items"][0]["asset"] == "aro_a"


def test_a_missing_export_is_an_error_not_a_silent_gap(tmp_path):
    W = make_ws(tmp_path); (W / "exports" / "fbx" / "aro_a" / "v002" / "aro_a.fbx").unlink()
    with pytest.raises(SystemExit):
        S.stage(W, tmp_path / "ARO", {"items": []})


def test_only_the_requested_assets_are_staged(tmp_path):
    W = make_ws(tmp_path); out = tmp_path / "ARO"
    assert S.stage(W, out, {"items": []}, ids={"rg_b"}) == {"rg_b": "v002"} and not (out / "Models" / "aro_a").exists()


def test_extra_placement_files_are_written_next_to_the_main_one(tmp_path):
    W = make_ws(tmp_path); out = tmp_path / "ARO"
    S.stage(W, out, {"items": [{"asset": "aro_a"}]}, extra_placements={"kano_residential_section_a_merged": {"items": [{"asset": "rg_b"}]}})
    assert json.loads((out / "placements" / "kano_residential_section_a.json").read_text())["items"][0]["asset"] == "aro_a"
    assert json.loads((out / "placements" / "kano_residential_section_a_merged.json").read_text())["items"][0]["asset"] == "rg_b"


def test_without_extra_placements_only_the_main_file_exists(tmp_path):
    W = make_ws(tmp_path); out = tmp_path / "ARO"; S.stage(W, out, {"items": []})
    assert sorted(p.name for p in (out / "placements").iterdir()) == ["kano_residential_section_a.json"]
