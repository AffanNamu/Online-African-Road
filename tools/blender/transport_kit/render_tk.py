"""Preview renders of the transport kit (Blender geometry only, Cycles on the CPU, the same light as the other kit renders). A preview is not proof of anything in Unity or in the game and is not an approval.
Run from tools/blender:  PYTHONPATH=. ~/.aro/venv/bin/python -m transport_kit.render_tk --out DIR [--only id,id] [--family A|B|C] [--samples 16]"""
from __future__ import annotations
import argparse
from pathlib import Path
import bpy
from kano_kit import geo, render_ref as R
from kano_kit.render_assets import bounds
from . import common as C
from .build_transport import build_scene, load_all, SPECS


def main():
    ap = argparse.ArgumentParser(); ap.add_argument("--out", required=True); ap.add_argument("--only", default=""); ap.add_argument("--family", default=""); ap.add_argument("--samples", type=int, default=16)
    ap.add_argument("--exposure", type=float, default=-3.5); ap.add_argument("--w", type=int, default=800); ap.add_argument("--h", type=int, default=450); a = ap.parse_args()
    load_all(); out = Path(a.out); out.mkdir(parents=True, exist_ok=True)
    for aid, sp in SPECS.items():
        if a.only and aid not in a.only.split(","): continue
        if a.family and C.EXTRAS.get(aid, {}).get("family") != a.family: continue
        asset = build_scene(sp)
        for lv, o in asset.lods.items():
            if lv > 0: o.hide_render = True
        lo, hi = bounds([asset.lods[0]]); c = (lo + hi) / 2; size = hi - lo; d = max(size.x, size.y, size.z)
        g = geo.Part("ground"); r_ = max(size.x, size.y, 10) * 1.6; z = lo.z - 0.02
        g.face_n([(c.x - r_, c.y - r_, z), (c.x + r_, c.y - r_, z), (c.x + r_, c.y + r_, z), (c.x - r_, c.y + r_, z)], "concrete_dark", (0, 0, 1)); g.to_object("preview_ground")
        R.world(36, 200, clouds=True)
        k = 1.35 if d > 6 else 1.6
        views = (("iso", (c.x - 0.85 * d * k, c.y - 1.05 * d * k, c.z + 0.35 * d * k), (c.x, c.y, c.z), 34), ("front", (c.x, c.y - 1.5 * d * k, c.z + 0.05 * d * k), (c.x, c.y, c.z), 34))
        for name, loc, tgt, lens in views:
            for ob in list(bpy.data.objects):
                if ob.type == "CAMERA": bpy.data.objects.remove(ob)
            R.camera(loc, tgt, lens, f"cam_{name}"); R.render(str(out / f"{aid}_{name}.png"), a.w, a.h, a.samples, exposure=a.exposure)
        print("previewed", aid)


if __name__ == "__main__":
    main()
