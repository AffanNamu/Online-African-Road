"""Per-asset preview renders of the environmental kit (the registry's `previews`): ground patches and water from an oblique view above and from driver height along the road, everything else from a
three-quarter view and a front view, in the same light as the other kit renders. Previews of Blender geometry only; not proof of anything in Unity or in the game.
Run from tools/blender:  PYTHONPATH=. ~/.aro/venv/bin/python -m env_kit.render_env --out DIR [--only id,id] [--samples 32] [--exposure -3.0]"""
from __future__ import annotations
import argparse
from pathlib import Path
import bpy
from mathutils import Vector
from kano_kit import geo, render_ref as R
from kano_kit.render_assets import bounds
from . import build_env as BE, textures as ET


def main():
    ap = argparse.ArgumentParser(); ap.add_argument("--out", required=True); ap.add_argument("--only", default=""); ap.add_argument("--samples", type=int, default=32); ap.add_argument("--exposure", type=float, default=-3.5); a = ap.parse_args()
    out = Path(a.out); out.mkdir(parents=True, exist_ok=True)
    for aid, sp in BE.SPECS.items():
        if a.only and aid not in a.only.split(","): continue
        asset = geo.Asset(aid, sp["kind"])
        for lv, fn in sorted(sp["lods"].items()): asset.lod(lv, fn())
        ET.apply_env_pbr()
        from kano_kit import textures as KT; KT.apply_pbr()
        for lv, o in asset.lods.items():
            if lv > 0: o.hide_render = True
        lo, hi = bounds([asset.lods[0]]); c = (lo + hi) / 2; size = hi - lo; surface = aid.startswith(("aro_road_", "aro_water_")); d = max(size.x, size.y, size.z)
        g = geo.Part("ground"); r_ = max(size.x, size.y, 10) * 1.6; z = lo.z - 0.02
        g.face_n([(c.x - r_, c.y - r_, z), (c.x + r_, c.y - r_, z), (c.x + r_, c.y + r_, z), (c.x - r_, c.y + r_, z)], "concrete_dark", (0, 0, 1)); g.to_object("preview_ground")
        R.world(36, 200, clouds=True)
        if surface:
            views = (("iso", (c.x - 0.45 * d, c.y - 0.62 * d, c.z + 0.55 * d), (c.x, c.y, c.z), 30), ("front", (c.x, lo.y + 0.6, lo.z + 1.5), (c.x, c.y + d * 0.3, lo.z + 0.2), 28))      # oblique from above; driver height along the patch
        else:
            k = 1.55 * (0.8 if aid.startswith("aro_drain") else 0.62 if aid.startswith("rg_ngn_shop_row") else 0.85 if aid.startswith("rg_ngn_house") else 1.0)
            views = (("iso", (c.x - 0.85 * d * k, c.y - 1.05 * d * k, c.z + 0.35 * d * k), (c.x, c.y, c.z), 34), ("front", (c.x, c.y - 1.5 * d * k, c.z + 0.05 * d * k), (c.x, c.y, c.z), 34))
        for name, loc, tgt, lens in views:
            for ob in list(bpy.data.objects):
                if ob.type == "CAMERA": bpy.data.objects.remove(ob)
            R.camera(loc, tgt, lens, f"cam_{name}"); R.render(str(out / f"{aid}_{name}.png"), 800, 450, a.samples, exposure=a.exposure)
        print("previewed", aid)


if __name__ == "__main__":
    main()
