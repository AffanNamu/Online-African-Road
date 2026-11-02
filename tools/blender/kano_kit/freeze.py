"""Freezes a version of the Kano residential kit as a CANDIDATE for in-game review: writes a manifest that records, for every asset, the exact files (path, bytes, SHA-256) of the Blender source, the FBX and GLB exports,
the textures and the validation report, plus the commit of the game repository that holds the code which produced them and the validation result. The manifest is text; the files stay in the asset workspace and
in the private asset repository. A frozen version is never edited: a correction is a new version.
Freezing is NOT approval: `production_approved` needs the in-game review and a person (docs/WORLD_BUILDING_WORKFLOW.md).
Run from tools/blender:  PYTHONPATH=. ~/.aro/venv/bin/python -m kano_kit.freeze --version v002 --out docs/kano/KANO_RESIDENTIAL_v002.freeze.json   (output path relative to the repository root)"""
from __future__ import annotations
import argparse, hashlib, json, subprocess, sys, time
from pathlib import Path
from aro_blender import workspace as ws
from . import build_all

PRODUCERS = ["tools/blender/kano_kit/villa3.py", "tools/blender/kano_kit/palette.py", "tools/blender/kano_kit/veg3.py", "tools/blender/kano_kit/coplanar.py", "tools/blender/kano_kit/build3.py", "tools/blender/kano_kit/frontage.py",
             "tools/blender/aro_blender", "tools/blender/kano_kit/geo.py", "tools/blender/kano_kit/street.py", "tools/blender/kano_kit/walls.py", "tools/blender/kano_kit/villa.py", "tools/blender/kano_kit/lights.py",
             "tools/blender/kano_kit/veg.py", "tools/blender/kano_kit/textures.py", "tools/blender/kano_kit/assemble.py", "tools/blender/kano_kit/build_all.py", "tools/blender/env_kit"]


def git(root: Path, *a: str) -> str:
    return subprocess.run(["git", "-C", str(root), *a], capture_output=True, text=True, check=True).stdout.strip()


def sha(p: Path) -> str: return hashlib.sha256(p.read_bytes()).hexdigest()


def main() -> int:
    ap = argparse.ArgumentParser(); ap.add_argument("--version", default="", help="one version label for every asset; omitted: the LATEST version of each asset"); ap.add_argument("--out", required=True); ap.add_argument("--renders", default="")
    ap.add_argument("--kit", default="kano", choices=["kano", "env", "v3"]); ap.add_argument("--only", default="", help="comma-separated asset ids (default: the whole kit)"); ap.add_argument("--label", default=""); a = ap.parse_args()
    repo = Path(__file__).resolve().parents[3]; W = ws.default_root(); only = set(a.only.split(",")) if a.only else None
    producers_commit = git(repo, "log", "-1", "--format=%H", "--", *PRODUCERS); head = git(repo, "rev-parse", "HEAD"); dirty = bool(git(repo, "status", "--porcelain", "--", "tools/blender"))
    assets = []; all_pass = True
    if a.kit == "env":
        from env_kit import build_env
        specs = build_env.SPECS
    elif a.kit == "v3":
        from . import build3
        specs = {**build3.SPECS, **build3.FRONTAGE}
    else: specs = build_all.SPECS
    for aid, sp in specs.items():
        if only is not None and aid not in only: continue
        kind = sp["kind"]; v = a.version or sorted(p.name for p in (W / "exports" / "fbx" / aid).iterdir() if p.is_dir())[-1]; proj = W / "projects" / kind / aid / v; val = W / "validation" / aid / f"{v}.json"
        files = [proj / f"{aid}.blend", proj / "project.json", W / "exports" / "fbx" / aid / v / f"{aid}.fbx", W / "exports" / "glb" / aid / v / f"{aid}.glb", W / "exports" / "fbx" / aid / v / f"{aid}.manifest.json", val]
        files += sorted(proj.glob("T_*.png"))
        missing = [str(f.relative_to(W)) for f in files if not f.is_file()]
        if missing: print("MISSING", aid, missing); return 1
        res = json.loads(val.read_text()); all_pass &= bool(res["passed"]); st = res["stats"]
        assets.append({"id": aid, "kind": kind, "version": v, "validation": {"passed": res["passed"], "lodTriangles": st["lod_triangles"], "sizeM": st["size_m"], "materials": st["materials"]},
                       "files": [{"path": str(f.relative_to(W)), "bytes": f.stat().st_size, "sha256": sha(f)} for f in files]})
    doc = {"freeze": a.label or f"{'environment_kit_e1_e2' if a.kit == 'env' else 'kano_residential_section_a'} {a.version or 'latest'}", "status": "frozen candidate for in-game review: NOT approved (production_approved needs the in-game review and a person)", "date": time.strftime("%Y-%m-%dT%H:%M:%SZ", time.gmtime()),
           "gameRepository": {"repo": "AffanNamu/Online-African-Road", "branch": "work/kano-residential", "head": head, "producerCodeLastChangedIn": producers_commit, "uncommittedChangesUnderToolsBlender": dirty},
           "blender": __import__("bpy").app.version_string, "allAssetsPassedBlenderValidation": all_pass, "assetCount": len(assets), "assets": assets,
           "rule": "A frozen version is never edited. A correction is the next version. The files live in the asset workspace and in the private asset repository ARO-CONTENT; this manifest holds only their hashes."}
    out = repo / a.out; out.parent.mkdir(parents=True, exist_ok=True); out.write_text(json.dumps(doc, indent=1) + "\n")
    print(f"froze {len(assets)} assets of {a.version or 'latest'}; all passed: {all_pass}; producer code last changed in {producers_commit[:8]}; wrote {out.relative_to(repo)}"); return 0


if __name__ == "__main__":
    sys.exit(main())
