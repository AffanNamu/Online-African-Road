#!/usr/bin/env python3
"""Builds the calibration fixture with the SAME toolchain code the MCP server uses (scene, validation, export), writing it flat into a directory the Unity project imports from:
   python tools/blender/make_fixtures.py --out Assets/ThirdParty/ARO_Calibration
Writes aro_calibration.fbx / .glb / .manifest.json (measurements, catalogue entry, the FBX's own declared axis settings). Refuses to write anything if the asset fails validation.
Used by .github/workflows/blender-unity.yml; the output is never committed (the folder is git-ignored and the repository safety gate would refuse it)."""
from __future__ import annotations
import argparse, json, os, sys
from pathlib import Path

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))
import bpy, mathutils  # noqa: E402
from aro_blender import export as exp, fbxinfo, materials, scene as sc, validate  # noqa: E402


def main() -> int:
    ap = argparse.ArgumentParser(); ap.add_argument("--out", required=True); ap.add_argument("--require-glb", action="store_true", help="also write a REQUIRE_GLB marker (the Unity test then fails instead of skipping when it cannot import GLB)")
    a = ap.parse_args(); out = Path(a.out).resolve(); out.mkdir(parents=True, exist_ok=True)
    sc.clear(bpy)
    ns = {"bpy": bpy, "mathutils": mathutils, "args": {}, "aro_materials": materials, "aro_scene": sc, "aro_validate": validate, "result": None}
    exec(compile((HERE / "scripts" / "calibration_marker.py").read_text(), "calibration_marker.py", "exec"), ns)
    res = validate.run(bpy, mathutils, "props", True, None, "aro_calibration")
    if not res["passed"]: print(json.dumps(res, indent=1)); print("calibration asset failed validation: nothing written", file=sys.stderr); return 1
    files = [exp.export(bpy, out, "aro_calibration", fmt) for fmt in ("fbx", "glb")]
    variants = {}
    for tag, opts in exp.fbx_variants().items():
        f = exp.export(bpy, out, "aro_calibration", "fbx", opts, stem=f"aro_calibration__{tag}"); variants[tag] = {"file": f.name, "options": opts}
    man = exp.write_manifest(out, "aro_calibration", 1, "props", files, res, bpy.app.version_string, "tools/blender/scripts/calibration_marker.py")
    m = json.loads(man.read_text()); m["fbx_declared"] = fbxinfo.summary(files[0]); m["fbx_default"] = exp.FBX_DEFAULT; m["fbx_default_variant"] = exp.variant_tag(exp.FBX_DEFAULT); m["fbx_variants"] = variants
    for tag, v in variants.items(): v["declared"] = {k: x for k, x in fbxinfo.summary(out / v["file"]).items() if k in ("UpAxis", "UpAxisSign", "FrontAxis", "FrontAxisSign", "CoordAxis", "CoordAxisSign", "UnitScaleFactor")}
    man.write_text(json.dumps(m, indent=2))
    if a.require_glb: (out / "REQUIRE_GLB").write_text("the Unity test must import the GLB (glTFast is installed for it)\n")
    print("wrote", ", ".join(str(f.relative_to(out)) for f in files + [man]), f"and {len(variants)} FBX variants to", out)
    for tag, v in variants.items(): print(" ", tag, json.dumps(v["declared"]))
    return 0


if __name__ == "__main__":
    sys.exit(main())
