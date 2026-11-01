#!/usr/bin/env python3
"""A GENERIC fixture for the Unity builder's texture and cutout support (no Kano design in it): a 2 m paver patch, two crossed foliage cards and a post, with a box collider and two LODs, built with
the kit's own helpers and exported with the toolchain (FBX with its textures next to it, GLB, manifest, aro_materials.json).
   python tools/blender/make_kit_fixture.py --out Assets/ThirdParty/ARO_KitFixture
Used by .github/workflows/blender-unity.yml; the output is never committed."""
from __future__ import annotations
import argparse, json, sys
from pathlib import Path
HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))
import bpy, mathutils  # noqa: E402
from aro_blender import export as exp, materials, validate  # noqa: E402
from kano_kit import geo, textures  # noqa: E402
from mathutils import Vector  # noqa: E402

ID = "aro_kit_fixture_a"


def part(lod: int) -> geo.Part:
    import math
    p = geo.Part("fixture"); p.uv_scale = {"paver_mix": 0.5}
    n = 4 if lod == 0 else 1
    for i in range(n):
        for j in range(n):
            x0, x1, y0, y1 = -1 + 2 * i / n, -1 + 2 * (i + 1) / n, -1 + 2 * j / n, -1 + 2 * (j + 1) / n
            p.face_n([(x0, y0, 0.0), (x1, y0, 0.0), (x1, y1, 0.0), (x0, y1, 0.0)], "paver_mix", (0, 0, 1))
    if lod == 0: p.tube((0, 0, 0), (0, 0, 1.6), 0.04, 0.04, 8, "metal_galvanised")
    for yaw in ((0.0, 60.0, 120.0) if lod == 0 else (0.0,)):
        r = Vector((math.cos(math.radians(yaw)), math.sin(math.radians(yaw)), 0)); p.card((0, 0, 1.0), r, Vector((0, 0, 1)), 1.4, 1.4, "leaf_cutout", (0, 0), (0.5, 0.5))
    return p


def main() -> int:
    ap = argparse.ArgumentParser(); ap.add_argument("--out", required=True); a = ap.parse_args(); out = Path(a.out).resolve(); out.mkdir(parents=True, exist_ok=True)
    asset = geo.Asset(ID, "props"); textures.paver_material(); textures.leaf_material()
    for lv in (0, 1): asset.lod(lv, part(lv))
    asset.collision(-1, -1, 0, 1, 1, 1.6)
    res = validate.run(bpy, mathutils, "props", True, None, ID)
    if not res["passed"]: print(json.dumps(res, indent=1)); print("fixture failed validation: nothing written", file=sys.stderr); return 1
    textures.write_images(out)
    files = [exp.export(bpy, out, ID, fmt) for fmt in ("fbx", "glb")]
    exp.write_manifest(out, ID, 1, "props", files, res, bpy.app.version_string, "tools/blender/make_kit_fixture.py")
    (out / "aro_materials.json").write_text(json.dumps(materials.kit_json(), indent=1))
    print("wrote", ", ".join(f.name for f in files), "and the kit to", out); return 0


if __name__ == "__main__":
    sys.exit(main())
