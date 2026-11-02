"""Compares a workspace built from the CODE with a frozen manifest, file by file: every texture (PNG) and GLB export named in the manifest is hashed in the workspace and compared with the manifest's SHA-256. The review
job runs it after building the kit in a fresh workspace, so the log says whether the pictures show exactly the frozen version (the version label of a fresh workspace is v001, so `--map v003=v001` translates
the path). FBX files and manifests are not compared: the FBX embeds paths and times (its geometry is compared by re-import elsewhere).
Run from tools/blender:  PYTHONPATH=. python -m kano_kit.verify_freeze --freeze ../../docs/kano/KANO_RESIDENTIAL_v003.freeze.json --workspace DIR [--map v003=v001] [--strict]"""
from __future__ import annotations
import argparse, hashlib, json, sys
from pathlib import Path


def main() -> int:
    ap = argparse.ArgumentParser(); ap.add_argument("--freeze", required=True); ap.add_argument("--workspace", required=True); ap.add_argument("--map", default=""); ap.add_argument("--strict", action="store_true"); a = ap.parse_args()
    fr = json.loads(Path(a.freeze).read_text()); W = Path(a.workspace); old, _, new = a.map.partition("=")
    tot = {"png": [0, 0], "glb": [0, 0]}; bad = []
    for asset in fr["assets"]:
        for f in asset["files"]:
            name = Path(f["path"]).name; kind = "png" if name.endswith(".png") else "glb" if name.endswith(".glb") else None
            if kind is None: continue
            q = W / (f["path"].replace(old, new) if old else f["path"]); tot[kind][1] += 1
            if q.is_file() and hashlib.sha256(q.read_bytes()).hexdigest() == f["sha256"]: tot[kind][0] += 1
            else: bad.append((asset["id"], name, "missing" if not q.is_file() else "differs"))
    print(f"frozen manifest {Path(a.freeze).name}: textures identical {tot['png'][0]} of {tot['png'][1]}, GLB exports identical {tot['glb'][0]} of {tot['glb'][1]}")
    for b in bad[:12]: print("  NOT IDENTICAL:", *b)
    return 1 if (bad and a.strict) else 0


if __name__ == "__main__":
    sys.exit(main())
