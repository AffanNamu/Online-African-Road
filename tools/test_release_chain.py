import json, sys
from pathlib import Path
sys.path.insert(0, str(Path(__file__).resolve().parent))
import release_chain as R


def make(tmp, **over):
    c = tmp / ("c%d" % len(list(tmp.iterdir()))); (c / "registry").mkdir(parents=True); (c / "unity" / "import_reports").mkdir(parents=True)
    for rel in ("blender/projects/props/a/v001/a.blend", "unity/Models/a/v001/a.fbx", "unity/Models/a/v001/a.glb", "unity/Models/a/v001/a.manifest.json", "blender/validation/a/v001.json"):
        p = c / rel; p.parent.mkdir(parents=True, exist_ok=True); p.write_text('{"passed": true}')
    (c / "unity/import_reports/review_run_7.json").write_text(json.dumps({"assets": {"a": {"lodTriangles": [100, 10], "materials": 2}}}))
    a = {"id": "a", "status": "unity_import_verified", "concepts": ["R-001"], "blenderSource": "blender/projects/props/a/v001/a.blend", "export": "unity/Models/a/v001/a.fbx", "lodTriangles": [100, 10], "materials": ["m1", "m2"],
         "validation": {"blender": "blender/validation/a/v001.json", "unityImport": "unity/import_reports/review_run_7.json", "visualReview": None}}
    a.update(over); (c / "registry" / "asset_registry.json").write_text(json.dumps({"assets": [a]})); return c


def chain(c, runs=()):
    return R.check_asset(R.load_registry(c)[0], c, {"R-001"}, R.load_imports(c), set(runs))


def test_complete_chain_is_ok_until_the_owner_has_looked(tmp_path):
    r = chain(make(tmp_path), ["7"])
    assert [r[k][0] for k in R.LINKS] == ["ok", "ok", "ok", "ok", "ok", "open"] and "preserved" in r["in_game_review"][1]


def test_broken_links_are_reported_not_hidden(tmp_path):
    c = make(tmp_path, lodTriangles=[101, 10], concepts=["R-999"]); (c / "unity/Models/a/v001/a.glb").unlink()
    r = chain(c)
    assert r["unity_prefab"][0] == "mismatch" and r["reference"][0] == "mismatch" and r["export"][0] == "missing"


def test_asset_without_a_reference_stays_open_only_with_a_recorded_reason(tmp_path):
    assert chain(make(tmp_path, concepts=[], flags="NO real reference exists (M-01)"))["reference"][0] == "open"
    assert chain(make(tmp_path, concepts=[], flags="requested by the owner"))["reference"][0] == "open"
    assert chain(make(tmp_path, concepts=[], flags="nothing"))["reference"][0] == "missing"


def test_a_recorded_visual_review_must_exist(tmp_path):
    c = make(tmp_path); reg = json.loads((c / "registry/asset_registry.json").read_text()); reg["assets"][0]["validation"]["visualReview"] = "reviews/x.json"; (c / "registry/asset_registry.json").write_text(json.dumps(reg))
    assert chain(c)["in_game_review"][0] == "missing"
