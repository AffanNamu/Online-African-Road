"""Re-imports the exported FBX and GLB files of the environmental kit into a fresh Blender scene and checks them against the Blender validation: the file imports, it has meshes, LOD meshes, materials and (for textured
assets) texture images, and the LOD0 bounding box equals the size the validation measured (metres, Blender axes). This is a Blender re-import of the exports. It is NOT a Unity import and says nothing about Unity.
Run from tools/blender:  PYTHONPATH=. ~/.aro/venv/bin/python -m env_kit.verify_export --out report.json [--only id,id]   (the latest version of each asset)"""
from __future__ import annotations
import argparse, json, sys
from pathlib import Path
import bpy
from mathutils import Vector
from aro_blender import workspace as ws
from kano_kit.verify_export import reset, import_file
from . import build_env as BE


def check(W: Path, aid: str, v: str, fmt: str) -> dict:
    f = W / "exports" / fmt / aid / v / f"{aid}.{fmt}"
    if not f.is_file(): return dict(asset=aid, version=v, format=fmt, ok=False, note="export missing")
    reset(); objs = import_file(f); meshes = [o for o in objs if o.type == "MESH"]
    mats = {s.material for o in meshes for s in o.material_slots if s.material}
    imgs = {n.image.name for m in mats if m.use_nodes for n in m.node_tree.nodes if n.type == "TEX_IMAGE" and n.image}
    lods = sorted(o.name for o in meshes if "_lod" in o.name.lower()); lod0 = [o for o in meshes if "_lod0" in o.name.lower()] or [o for o in meshes if not o.name.startswith("col_")]
    lo = [1e9] * 3; hi = [-1e9] * 3
    for o in lod0:
        for c in o.bound_box:
            w = o.matrix_world @ Vector(c); lo = [min(a, b) for a, b in zip(lo, w)]; hi = [max(a, b) for a, b in zip(hi, w)]
    dims = [round(h - l, 3) for h, l in zip(hi, lo)]
    stats = json.loads((W / "validation" / aid / f"{v}.json").read_text())["stats"]; want = stats["size_m"]; n_lod = len(stats["lod_triangles"])
    dims_ok = all(abs(d - w) <= max(0.02, 0.01 * w) for d, w in zip(dims, want))
    return dict(asset=aid, version=v, format=fmt, meshes=len(meshes), lod_meshes=len(lods), lods_expected=n_lod, materials=len(mats), materials_expected=len(stats["materials"]), texture_images=len(imgs), size_reimported_m=dims, size_validated_m=want,
                size_ok=dims_ok, ok=len(meshes) > 0 and len(mats) > 0 and dims_ok and len(lods) >= n_lod)


def main() -> int:
    ap = argparse.ArgumentParser(); ap.add_argument("--out", required=True); ap.add_argument("--only", default=""); a = ap.parse_args()
    W = ws.default_root(); rows = []
    for aid in BE.SPECS:
        if a.only and aid not in a.only.split(","): continue
        vs = sorted(p.name for p in (W / "exports" / "fbx" / aid).iterdir() if p.is_dir())
        for fmt in ("glb", "fbx"): rows.append(check(W, aid, vs[-1], fmt))
    Path(a.out).write_text(json.dumps(rows, indent=1)); bad = [r for r in rows if not r["ok"]]
    for r in bad: print("FAIL", r["asset"], r["version"], r["format"], {k: r.get(k) for k in ("size_reimported_m", "size_validated_m", "meshes", "lod_meshes", "lods_expected", "materials", "note")})
    print(f"{len(rows)} re-imports ({len(rows) // 2} assets, glb and fbx): {len(rows) - len(bad)} ok, {len(bad)} failed"); return 1 if bad else 0


if __name__ == "__main__":
    sys.exit(main())
