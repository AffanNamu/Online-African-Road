"""Composition renders of the transport kit's review scenes (transport_kit.scenes): every placement built from the same geometry as the exported assets (LOD0), seen from the scene's own cameras, Cycles on the CPU.
Blender geometry only: not proof of anything in Unity or the game, not an approval.
Run from tools/blender:  PYTHONPATH=. ~/.aro/venv/bin/python -m transport_kit.render_scenes --out DIR [--scene logistics_yard] [--view drive] [--samples 16] [--w 960 --h 540]"""
from __future__ import annotations
import argparse, math
from pathlib import Path
import bpy
from kano_kit import geo, render_ref as R, textures
from . import palette as PAL, scenes as SC
from .build_transport import SPECS, load_all


def build(sc):
    geo.reset_scene(); textures.paver_material(); textures.asphalt_material(); templates = {}
    for aid in sorted({i["asset"] for i in sc.items}):
        part = SPECS[aid]["lods"][0](); PAL.palettize(part); templates[aid] = part.to_object(f"tpl_{aid}"); templates[aid].hide_render = True
    for n, it in enumerate(sc.items):
        o = bpy.data.objects.new(f"{it['asset']}_{n}", templates[it["asset"]].data); bpy.context.scene.collection.objects.link(o)
        o.location = (it["x"], it["y"], it["z"]); o.rotation_euler = (0.0, 0.0, math.radians(it["yawDeg"]))
    g = geo.Part("ground"); r = 160.0
    g.face_n([(-r, -r, -0.02), (r, -r, -0.02), (r, r + 40, -0.02), (-r, r + 40, -0.02)], sc.ground, (0, 0, 1)); g.to_object("review_ground")
    textures.apply_pbr(); PAL.apply_palettes()


def main():
    ap = argparse.ArgumentParser(); ap.add_argument("--out", required=True); ap.add_argument("--scene", default=""); ap.add_argument("--view", default=""); ap.add_argument("--samples", type=int, default=16)
    ap.add_argument("--exposure", type=float, default=-3.5); ap.add_argument("--w", type=int, default=960); ap.add_argument("--h", type=int, default=540); a = ap.parse_args()
    load_all(); out = Path(a.out); out.mkdir(parents=True, exist_ok=True)
    for sc in SC.all_scenes():
        if a.scene and a.scene != sc.name: continue
        build(sc); R.world(36, 200, clouds=True)
        for v in sc.views:
            if a.view and a.view != v["name"]: continue
            for ob in list(bpy.data.objects):
                if ob.type == "CAMERA": bpy.data.objects.remove(ob)
            R.camera(tuple(v["loc"]), tuple(v["look"]), v["lens"], f"cam_{v['name']}"); R.render(str(out / f"{sc.name}_{v['name']}.png"), a.w, a.h, a.samples, exposure=a.exposure)
        print("rendered", sc.name)


if __name__ == "__main__":
    main()
