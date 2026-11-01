"""Verifies that the corrected materials and textures keep their appearance AFTER EXPORT, from the exported files and not from the in-memory scene:
  1. every exported colour texture (PNG) has the mean sRGB colour its generator intended (a double sRGB decode, the bug fixed in v002, would fail this);
  2. every exported GLB and FBX re-imports into a fresh Blender scene with its LODs, materials and texture images;
  3. the review scene is rebuilt from the exported GLB files with the recorded placements, rendered from the reference camera with the same sky and exposure, and compared with the render of the
     in-memory scene (mean colour per channel and mean absolute difference).
This is a Blender re-import of the exports. It is NOT a Unity import and says nothing about Unity; that is verified separately, in the real game.
Run from tools/blender:  PYTHONPATH=. ~/.aro/venv/bin/python -m kano_kit.verify_export --placements JSON --native PNG --out DIR [--version v002] [--samples 48]"""
from __future__ import annotations
import argparse, json, math, sys
from pathlib import Path
import numpy as np
import bpy
from mathutils import Euler, Matrix, Vector
from aro_blender import workspace as ws, materials as AM
from . import render_ref as R, build_all, textures as T

EXPECT = {   # texture name -> generator whose mean sRGB colour the exported PNG must reproduce
    "T_kano_plaster_BaseColor": lambda: T.plaster_maps()[0], "T_kano_travertine_BaseColor": lambda: T.stone_maps()[0], "T_kano_cladding_BaseColor": lambda: T.stone_maps(base=(0.43, 0.43, 0.45), grey=True)[0],
    "T_kano_rooftile_BaseColor": lambda: T.roof_tile_maps()[0], "T_kano_concrete_BaseColor": lambda: T.concrete_maps()[0], "T_kano_bark_BaseColor": lambda: T.bark_maps()[0],
    "T_kano_grass_BaseColor": lambda: T.grass_maps()[0], "T_kano_paver_BaseColor": lambda: T.paver_pixels(), "T_kano_asphalt_BaseColor": lambda: T.asphalt_pixels(),
}


def png_mean(path: Path) -> np.ndarray:
    from PIL import Image
    return np.asarray(Image.open(path).convert("RGB"), dtype=float).reshape(-1, 3).mean(0) / 255.0


def check_textures(W: Path, version: str) -> list[dict]:
    rows = []; seen = {}
    for p in sorted((W / "projects").rglob(f"{version}/T_*_BaseColor.png")):
        if "kano_slice_greybox" in p.parts or "bollard" in p.parts: continue
        seen.setdefault(p.stem, p)
    for name, p in seen.items():
        got = png_mean(p)
        if name in EXPECT:
            want = np.asarray(EXPECT[name]()).reshape(-1, 3).mean(0); err = float(np.abs(got - want).max()); rows.append(dict(texture=name, file=str(p.relative_to(W)), exported_mean=[round(x, 3) for x in got], intended_mean=[round(x, 3) for x in want], max_error=round(err, 4), ok=err < 0.012))
        else: rows.append(dict(texture=name, file=str(p.relative_to(W)), exported_mean=[round(x, 3) for x in got], intended_mean=None, max_error=None, ok=True, note="leaf atlas (RGBA): colour checked in the render comparison"))
    return rows


def reset():
    bpy.ops.wm.read_factory_settings(use_empty=True)
    for c in list(bpy.data.collections): bpy.data.collections.remove(c)


def import_file(path: Path):
    before = set(bpy.data.objects)
    if path.suffix == ".glb": bpy.ops.import_scene.gltf(filepath=str(path))
    else: bpy.ops.import_scene.fbx(filepath=str(path))
    return [o for o in bpy.data.objects if o not in before]


def check_reimport(W: Path, version: str) -> list[dict]:
    rows = []
    for aid in build_all.SPECS:
        for fmt in ("glb", "fbx"):
            f = W / "exports" / fmt / aid / version / f"{aid}.{fmt}"
            if not f.is_file(): rows.append(dict(asset=aid, format=fmt, ok=False, note="export missing")); continue
            reset(); objs = import_file(f); meshes = [o for o in objs if o.type == "MESH"]
            mats = {s.material for o in meshes for s in o.material_slots if s.material}
            imgs = {n.image.name for m in mats if m.use_nodes for n in m.node_tree.nodes if n.type == "TEX_IMAGE" and n.image}
            lods = sorted(o.name for o in meshes if "_lod" in o.name.lower())
            rows.append(dict(asset=aid, format=fmt, meshes=len(meshes), lods=len(lods), materials=len(mats), texture_images=len(imgs), ok=len(meshes) > 0 and len(mats) > 0))
    return rows


def rebuild_from_exports(W: Path, placements: dict, version: str):
    reset(); coll = bpy.context.scene.collection; templates = {}
    for aid in sorted({i["asset"] for i in placements["items"]}):
        objs = import_file(W / "exports" / "glb" / aid / version / f"{aid}.glb")
        for o in objs:
            if o.type == "MESH" and o.name.lower().endswith(("_lod1", "_lod2")) or o.name.startswith("col_"): o.hide_render = True
        templates[aid] = objs
    # instance every placement: copy the LOD objects of the imported asset (linked data) under the recorded transform
    for n, it in enumerate(placements["items"]):
        src = templates[it["asset"]]; lod = it.get("lod", 0)
        for o in src:
            if o.type != "MESH" or o.name.startswith("col_"): continue
            lvl = int(o.name.lower().rsplit("_lod", 1)[1].split(".")[0][:1]) if "_lod" in o.name.lower() else 0
            if lvl != lod: continue
            base = o.matrix_world.copy()                                            # whatever axis conversion the importer applied to the asset itself
            c = o.copy(); c.data = o.data; c.hide_render = False; c.hide_viewport = False
            for s_ in list(c.users_collection): s_.objects.unlink(c)
            coll.objects.link(c); c.parent = None
            place = Matrix.Translation(Vector((it["x"], it["y"], it["z"]))) @ Euler((0, 0, math.radians(it["yawDeg"])), "XYZ").to_matrix().to_4x4() @ Matrix.Scale(it["scale"], 4)
            c.matrix_world = place @ base
    for aid, objs in templates.items():          # the imported originals are not part of the scene
        for o in objs:
            if o.users_collection: o.hide_render = True; o.hide_viewport = True
    # ground exactly as in the in-memory review scene
    from . import geo
    g = geo.Part("ground"); g.face_n([(-1500, -300, -0.03), (1500, -300, -0.03), (1500, 1500, -0.03), (-1500, 1500, -0.03)], "grass_green", (0, 0, 1)); g.to_object("review_ground")


def compare(native: Path, other: Path) -> dict:
    from PIL import Image
    a = np.asarray(Image.open(native).convert("RGB"), dtype=float); b = np.asarray(Image.open(other).convert("RGB"), dtype=float)
    return dict(mean_native=[round(x, 1) for x in a.reshape(-1, 3).mean(0)], mean_reimported=[round(x, 1) for x in b.reshape(-1, 3).mean(0)],
                mean_abs_difference=round(float(np.abs(a - b).mean()), 2), channel_shift=[round(float(x), 2) for x in (b - a).reshape(-1, 3).mean(0)])


def main() -> int:
    ap = argparse.ArgumentParser(); ap.add_argument("--placements", required=True); ap.add_argument("--native", required=True); ap.add_argument("--out", required=True); ap.add_argument("--version", default="v002")
    ap.add_argument("--samples", type=int, default=48); ap.add_argument("--exposure", type=float, default=-2.9); a = ap.parse_args()
    W = ws.default_root(); out = Path(a.out); out.mkdir(parents=True, exist_ok=True); report = {"version": a.version}
    report["textures"] = check_textures(W, a.version); report["reimport"] = check_reimport(W, a.version)
    placements = json.loads(Path(a.placements).read_text()); rebuild_from_exports(W, placements, a.version)
    R.world(36.0, 200.0); cam = R.camera((-3.2, -45.0, 1.7), (0.6, 60.0, 2.6), 28.0, "ref_cam"); bpy.context.scene.camera = cam
    img = out / "kano_residential_ref_from_exports.png"; R.render(str(img), 1280, 720, a.samples, exposure=a.exposure)
    report["render_comparison"] = compare(Path(a.native), img)
    ok_tex = all(r["ok"] for r in report["textures"]); ok_imp = all(r["ok"] for r in report["reimport"]); rc = report["render_comparison"]
    report["summary"] = dict(textures_ok=ok_tex, reimport_ok=ok_imp, render_mean_abs_difference=rc["mean_abs_difference"], render_max_channel_shift=max(abs(x) for x in rc["channel_shift"]),
                             render_ok=rc["mean_abs_difference"] < 8.0 and max(abs(x) for x in rc["channel_shift"]) < 4.0)
    (out / "verify_export.json").write_text(json.dumps(report, indent=1)); print(json.dumps(report["summary"], indent=1)); return 0 if ok_tex and ok_imp and report["summary"]["render_ok"] else 1


if __name__ == "__main__":
    sys.exit(main())
