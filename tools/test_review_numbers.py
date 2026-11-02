import json, sys
from pathlib import Path
sys.path.insert(0, str(Path(__file__).parent))
import review_numbers as R

PERF = "[Perf] tier=medium visibleTris=1000 drawEstimate=300 visibleRenderers=50 chunks=49 textures=112 textureMB=20.9 heapMB=4.4 avgFrameMs=1000.0 engineDraws=600 engineTris=2000 setPass=20 terrainTris=0"
V = {"quality": "medium", "perf": [PERF + "\n"], "draws": {"kit": "kit renderers=50 draws=299 tris=1000 distinctMaterials=20 distinctMeshes=10 distinctMeshMaterialPairs=40", "byAsset": "draws byAsset (draws/renderers/tris) a=200/20/500, b=99/30/500"}, "image": {"magentaFraction": 0, "nearBlackFraction": 0.01}}


def test_extract_reads_the_numbers_lines_and_ignores_the_rest():
    log = "2026-11-02T13:00:00.0000000Z some other line\n2026-11-02T13:28:17.2713Z ref " + json.dumps(V) + "\n2026-11-02T13:28:17.2714Z env_village_wet-drive " + json.dumps(V) + "\nnot a line {}\n"
    v = R.extract(log); assert set(v) == {"ref", "env_village_wet-drive"}


def test_helpers_read_the_game_lines():
    assert R.num(V, "drawEstimate") == 300 and R.num(V, "engineDraws") == 600 and R.draws(V, "kit") == 299 and R.by_asset(V) == {"a": (200, 20, 500), "b": (99, 30, 500)}


def test_compare_shows_the_change_in_percent():
    after = {"views": {"ref": dict(V, perf=[PERF.replace("drawEstimate=300", "drawEstimate=240").replace("engineDraws=600", "engineDraws=480")])}}
    t = R.compare({"views": {"ref": V}}, after); assert "-20.0%" in t and "| ref | 300 | 240 |" in t
