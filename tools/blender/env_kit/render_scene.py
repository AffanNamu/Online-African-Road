"""Blender views of the review environments (env_kit.scenes), built from the EXPORTED GLB files of the asset workspace with the recorded placements and cameras, so the composition (what faces the road, what stands
where, what overlaps what) can be looked at before Unity is involved. A Blender render of exported geometry: it says nothing about Unity or the game's lighting.
Run from tools/blender:  PYTHONPATH=. ~/.aro/venv/bin/python -m env_kit.render_scene --out DIR [--only name_season,...] [--samples 24] [--views drive,aerial]"""
from __future__ import annotations
import argparse, math
from pathlib import Path
import bpy
from mathutils import Euler, Matrix, Vector
from aro_blender import workspace as ws, materials as AM
from kano_kit import render_ref as R, geo
from kano_kit.verify_export import reset, import_file
from . import scenes as SC, layout as LAY


def latest(W: Path, aid: str) -> str:
    return sorted(p.name for p in (W / "exports" / "glb" / aid).iterdir() if p.is_dir())[-1]


def build(W: Path, scene: dict):
    reset(); coll = bpy.context.scene.collection; templates = {}
    for aid in sorted({i["asset"] for i in scene["items"]}):
        objs = import_file(W / "exports" / "glb" / aid / latest(W, aid) / f"{aid}.glb")
        for o in objs:
            if o.type == "MESH" and (o.name.lower().endswith(("_lod1", "_lod2")) or "_lod1" in o.name.lower() or "_lod2" in o.name.lower() or o.name.startswith("col_")): o.hide_render = True
        templates[aid] = objs
    for it in scene["items"]:
        for o in templates[it["asset"]]:
            if o.type != "MESH" or o.hide_render: continue
            base = o.matrix_world.copy(); c = o.copy(); c.data = o.data; c.hide_render = False; c.hide_viewport = False
            for s_ in list(c.users_collection): s_.objects.unlink(c)
            coll.objects.link(c); c.parent = None
            place = Matrix.Translation(Vector((it["x"], it["y"], it["z"]))) @ Euler((0, 0, math.radians(it["yawDeg"])), "XYZ").to_matrix().to_4x4()
            c.matrix_world = place @ base
    for objs in templates.values():
        for o in objs:
            if o.users_collection: o.hide_render = True; o.hide_viewport = True
    g = geo.Part("ground"); g.face_n([(-1500, -1500, -0.03), (1500, -1500, -0.03), (1500, 1500, -0.03), (-1500, 1500, -0.03)], scene["ground"], (0, 0, 1)); g.to_object("review_ground")


def main():
    ap = argparse.ArgumentParser(); ap.add_argument("--out", required=True); ap.add_argument("--only", default=""); ap.add_argument("--samples", type=int, default=24); ap.add_argument("--views", default="drive,aerial"); ap.add_argument("--exposure", type=float, default=-2.9); a = ap.parse_args()
    W = ws.default_root(); out = Path(a.out); out.mkdir(parents=True, exist_ok=True)
    lay = LAY.measure(sorted(SC.asset_ids()))
    for s in SC.all_scenes(lay):
        j = s.as_json(); tag = f"{s.name}_{s.season}"
        if a.only and tag not in a.only.split(","): continue
        build(W, j); R.world(36.0, 200.0)
        for v in s.views:
            if v["name"] not in a.views.split(","): continue
            cam = R.camera(tuple(v["loc"]), tuple(v["look"]), v["lens"], "cam"); bpy.context.scene.camera = cam
            R.render(str(out / f"{tag}_{v['name']}.png"), 800, 450, a.samples, exposure=a.exposure)
        print("rendered", tag)


if __name__ == "__main__":
    main()
