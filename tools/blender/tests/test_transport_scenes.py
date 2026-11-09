"""The review scenes of the transport kit (transport_kit.scenes) and the Unity staging (transport_kit.stage_unity): pure Python, no Blender needed. Checks the rules the scene module states: every asset exists, footprints
never overlap (measured bounds), cameras stand outside every footprint, placement files have the shape the game's review builder reads, and the staging merges material lists without losing an entry."""
from __future__ import annotations
import json, tempfile
from pathlib import Path
import pytest
from transport_kit import scenes as SC, stage_unity as ST

B = SC.load_bounds()
SCENES = SC.all_scenes(B)
FLAT = {"aro_drain_grate_set", "aro_kerb_painted_run_a"}                  # thin ground details may sit beside others
SHRINK = 0.06                                                              # tileable runs (fence, wall, rails) overlap by a few centimetres where they meet


def shrunk(f, d=SHRINK): return (f[0] + d, f[1] + d, f[2] - d, f[3] - d)


def overlap(a, b): return a[0] < b[2] and b[0] < a[2] and a[1] < b[3] and b[1] < a[3]


@pytest.mark.parametrize("sc", SCENES, ids=lambda s: s.name)
def test_every_asset_exists_and_the_file_has_the_review_shape(sc):
    j = sc.as_json()
    assert j["kind"] == "environment" and j["scene"].startswith("env_") and j["ground"] and j["views"] and j["items"]
    for it in j["items"]:
        assert it["asset"] in B and set(it) >= {"asset", "x", "y", "z", "yawDeg", "scale"}, it
        assert it["yawDeg"] % 90 == 0, "yaws are multiples of 90 degrees so that footprints are exact"
    names = [v["name"] for v in j["views"]]
    assert "drive" in names or "front" in names, names
    for v in j["views"]: assert len(v["loc"]) == 3 and len(v["look"]) == 3 and 10 <= v["lens"] <= 60


@pytest.mark.parametrize("sc", SCENES, ids=lambda s: s.name)
def test_footprints_never_overlap(sc):
    fps = [(i, shrunk(SC.footprint(i, B))) for i in sc.items if i["asset"] not in FLAT]
    bad = []
    for n, (i, f) in enumerate(fps):
        for k, g in fps[n + 1:]:
            if overlap(f, g): bad.append((i["asset"], i["x"], i["y"], k["asset"], k["x"], k["y"]))
    assert not bad, bad[:5]


@pytest.mark.parametrize("sc", SCENES, ids=lambda s: s.name)
def test_cameras_stand_outside_every_footprint_or_above_it(sc):
    for v in sc.views:
        x, y, z = v["loc"]
        for i in sc.items:
            f = SC.footprint(i, B); top = B[i["asset"]][1][2]
            if f[0] <= x <= f[2] and f[1] <= y <= f[3]: assert z > top + 0.5, f"{sc.name}/{v['name']}: camera inside {i['asset']}"


def test_the_scenes_use_the_whole_kit_that_makes_sense_to_show():
    used = SC.asset_ids(B)
    for must in ("rg_ngn_warehouse_hall_a", "rg_ngn_warehouse_hall_b", "rg_ngn_warehouse_cold_a", "aro_gate_sliding_a", "aro_weighbridge_a", "aro_fuel_island_truck", "kano_bus_terminal_hall_a", "kano_bus_shelter_long_a",
                 "kano_minibus_stand_canopy_a", "kano_tricycle_rank_a", "aro_gantry_sign_a", "aro_traffic_signal_a", "aro_streetlight_double_median_a", "aro_road_sign_stop_a", "kano_utility_pole_set"):
        assert must in used, must
    assert len(used) >= 45, len(used)


def test_scene_geometry_makes_sense_for_vehicles():
    """Gameplay sense, not looks: the gate lane and the weighbridge lie on the same line, the bus stands in front of the terminal platform, nothing stands on the road in the middle of the bus terminal scene."""
    y = SCENES[0]; gate = next(i for i in y.items if i["asset"] == "aro_gate_sliding_a"); wb = next(i for i in y.items if i["asset"] == "aro_weighbridge_a")
    fw = SC.footprint(wb, B)
    assert fw[0] <= -2.75 <= fw[2], "the weighbridge deck must lie across the entry lane (x = -2.75)"
    assert (fw[3] - fw[1]) >= 17.0, "an 18 m deck lies along the street"
    t = SCENES[1]
    for i in t.items:
        f = SC.footprint(i, B)
        if i["tag"] in ("terminal", "shelter", "kiosk", "board", "minibus", "tricycle"): continue
        assert not (f[0] < 8.0 and f[2] > -8.0 and i["tag"] not in ("light", "sign", "barrier", "planter", "bench", "bin") and i["asset"] != "aro_streetlight_arm_single_a"), (i["asset"], f)


def test_staging_merges_the_material_lists():
    ex = {"schema": 3, "materials": [{"name": "M_a", "roughness": 0.1}, {"name": "M_b", "roughness": 0.2}]}
    new = {"schema": 3, "materials": [{"name": "M_b", "roughness": 0.9}, {"name": "M_tk_paint", "roughness": 0.7}]}
    m = ST.merge_materials(ex, new)
    names = [x["name"] for x in m["materials"]]
    assert names == ["M_a", "M_b", "M_tk_paint"] and m["materials"][1]["roughness"] == 0.2, "a staged entry is kept, a new one is added"
    assert ST.merge_materials(None, new) == new


def test_stage_copies_the_exports_and_writes_the_placements(tmp_path):
    W = tmp_path / "ws"; ids = ["aro_bench_a", "aro_litter_bin_a"]
    for i in ids:
        d = W / "exports" / "fbx" / i / "v001"; d.mkdir(parents=True); (d / f"{i}.fbx").write_bytes(b"FBX"); (d / f"{i}.manifest.json").write_text("{}")
        p = W / "projects" / "props" / i / "v001"; p.mkdir(parents=True); (p / "T_tk_paint_Palette.png").write_bytes(b"png")
    (W / "exports" / "unity").mkdir(); (W / "exports" / "unity" / "aro_materials.json").write_text(json.dumps({"schema": 3, "materials": [{"name": "M_tk_paint"}]}))
    out = tmp_path / "out"; (out / "Materials").mkdir(parents=True); (out / "Materials" / "aro_materials.json").write_text(json.dumps({"schema": 3, "materials": [{"name": "M_concrete"}]}))
    staged = ST.stage(W, out, ids, SCENES)
    assert staged == {"aro_bench_a": "v001", "aro_litter_bin_a": "v001"}
    assert (out / "Models" / "aro_bench_a" / "v001" / "aro_bench_a.fbx").is_file() and (out / "Textures" / "kano_kit" / "T_tk_paint_Palette.png").is_file()
    assert [m["name"] for m in json.loads((out / "Materials" / "aro_materials.json").read_text())["materials"]] == ["M_concrete", "M_tk_paint"]
    assert {p.name for p in (out / "placements").glob("*.json")} == {"env_logistics_yard_dry.json", "env_bus_terminal_dry.json", "env_road_furniture_dry.json"}
    with pytest.raises(SystemExit): ST.stage(W, out, ["aro_missing_a"], SCENES)
