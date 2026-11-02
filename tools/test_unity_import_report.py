import json, sys
from pathlib import Path
sys.path.insert(0, str(Path(__file__).parent))
import unity_import_report as U

LOG = "2026 [KanoReviewBuilder] aro_a: prefab Assets/x/aro_a.prefab LOD triangles [100, 20] materials 3 collider True\n2026 [KanoReviewBuilder] aro_b: prefab Assets/x/aro_b.prefab LOD triangles [50] materials 2 collider False\nnoise\n"


def reg(tmp, entries):
    (tmp / "registry").mkdir(); (tmp / "registry" / "asset_registry.json").write_text(json.dumps({"assets": entries}))


def test_extract_reads_one_line_per_asset_and_counts_problems():
    r = U.extract(LOG + "x: material 'M_q' is not in the kit\n", 7, "abc1234"); assert r["assets"]["aro_a"] == {"lodTriangles": [100, 20], "materials": 3, "collider": True} and r["problemsInLog"] == 1 and r["run"] == 7


def test_only_entries_whose_figures_equal_unitys_are_marked(tmp_path):
    reg(tmp_path, [{"id": "aro_a", "status": "blender_validation_passed", "lodTriangles": [100, 20], "materials": ["a", "b", "c"], "validation": {}}, {"id": "aro_b", "status": "blender_validation_passed", "lodTriangles": [51], "materials": ["a", "b"], "validation": {}},
                   {"id": "aro_c", "status": "concept_approved", "lodTriangles": [1], "materials": [], "validation": {}}])
    done, skipped = U.apply(U.extract(LOG, 7, "abc1234"), tmp_path); r = {e["id"]: e for e in json.loads((tmp_path / "registry" / "asset_registry.json").read_text())["assets"]}
    assert done == ["aro_a"] and skipped[0][0] == "aro_b" and r["aro_a"]["status"] == "unity_import_verified" and r["aro_b"]["status"] == "blender_validation_passed" and r["aro_c"]["status"] == "concept_approved"
    assert r["aro_a"]["unity"]["imported"] is True and r["aro_a"]["unity"]["inGameVerified"] is False and (tmp_path / r["aro_a"]["validation"]["unityImport"]).is_file()


def test_problems_in_the_log_block_every_entry(tmp_path):
    reg(tmp_path, [{"id": "aro_a", "status": "blender_validation_passed", "lodTriangles": [100, 20], "materials": ["a", "b", "c"], "validation": {}}])
    done, skipped = U.apply(U.extract(LOG + "aro_q: no FBX under Assets\n", 7, "abc1234"), tmp_path); assert done == [] and skipped[0][1] == "import problems in the log"
