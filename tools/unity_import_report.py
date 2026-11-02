#!/usr/bin/env python3
"""The Unity IMPORT proof for assets, taken from the log of a review run (blender-unity.yml with [kano-review]). The review job imports every FBX through the real Unity importer, builds each asset's production prefab
(LODGroup, materials from the kit, colliders) and logs one `[KanoReviewBuilder] <id>: prefab ... LOD triangles [...] materials N collider X` line per asset. This tool

  extract <job.log> <out.json> --run ID --commit SHA     writes a report: for every asset, what Unity measured (LOD triangles, materials, collider) and the number of import problems in the log
  apply <report.json> <ARO-CONTENT checkout>             for every registry entry whose Blender figures (lodTriangles, materials) EQUAL what Unity measured, sets status `unity_import_verified` (only if it is
                                                         `blender_validation_passed`), unity.imported true, validation.unityImport = the report file (copied into the repository); entries that differ are left alone and listed

`unity_import_verified` is the second of the three separate proofs (Blender validation, Unity import, in-game visual review); it is NOT approval and says nothing about how the asset looks in the game: inGameVerified stays false
and the status never moves past it here. Only a person marks `production_approved`."""
from __future__ import annotations
import json, re, shutil, sys
from pathlib import Path

LINE = re.compile(r"\[KanoReviewBuilder\] (\S+): prefab \S+ LOD triangles \[([\d, ]+)\] materials (\d+) collider (\w+)")
PROBLEM = re.compile(r"is not in the kit|did not import as a GameObject|no FBX under|the prefab was not saved|no placements file")


def extract(log: str, run, commit: str) -> dict:
    assets = {}
    for l in log.splitlines():
        m = LINE.search(l)
        if m: assets[m.group(1)] = {"lodTriangles": [int(x) for x in m.group(2).split(",")], "materials": int(m.group(3)), "collider": m.group(4) == "True"}
    return {"run": run, "commit": commit, "unity": "6000.0.58f2", "importer": "the real Unity importer (FBX) and ARO.Editor.BlenderImport.BlenderAssetBuilder, built by the review job", "problemsInLog": sum(1 for l in log.splitlines() if PROBLEM.search(l)), "assets": assets}


def apply(report: dict, content: Path) -> tuple[list, list]:
    reg_path = content / "registry" / "asset_registry.json"; reg = json.loads(reg_path.read_text()); done, skipped = [], []
    rel = f"unity/import_reports/review_run_{report['run']}.json"; dst = content / rel; dst.parent.mkdir(parents=True, exist_ok=True); dst.write_text(json.dumps(report, indent=1) + "\n")
    for e in reg["assets"]:
        u = report["assets"].get(e["id"])
        if u is None or e.get("status") != "blender_validation_passed": continue
        if report["problemsInLog"]: skipped.append((e["id"], "import problems in the log")); continue
        if e.get("lodTriangles") != u["lodTriangles"] or len(e.get("materials") or []) != u["materials"]: skipped.append((e["id"], f"Blender {e.get('lodTriangles')} / {len(e.get('materials') or [])} materials, Unity {u['lodTriangles']} / {u['materials']}")); continue
        e["status"] = "unity_import_verified"; e["validation"]["unityImport"] = rel
        e["unity"] = {"imported": True, "compiled": True, "prefab": None, "inGameVerified": False, "note": f"imported by the real Unity importer in review run {report['run']} (commit {report['commit'][:7]}): LOD triangles and material count equal the Blender validation; the in-game visual review is for the owner and has NOT happened; not approved"}
        done.append(e["id"])
    reg_path.write_text(json.dumps(reg, indent=1, ensure_ascii=False) + "\n"); return done, skipped


def main(argv) -> int:
    if len(argv) >= 4 and argv[1] == "extract":
        run = argv[argv.index("--run") + 1]; commit = argv[argv.index("--commit") + 1]
        rep = extract(open(argv[2], encoding="utf-8", errors="replace").read(), int(run) if run.isdigit() else run, commit); json.dump(rep, open(argv[3], "w"), indent=1)
        print(f"{len(rep['assets'])} assets, {rep['problemsInLog']} import problems in the log"); return 0
    if len(argv) == 4 and argv[1] == "apply":
        done, skipped = apply(json.loads(Path(argv[2]).read_text()), Path(argv[3])); print(f"unity_import_verified: {len(done)}; left alone: {len(skipped)}")
        for s in skipped: print("  ", *s)
        return 0
    print(__doc__); return 2


if __name__ == "__main__":
    sys.exit(main(sys.argv))
