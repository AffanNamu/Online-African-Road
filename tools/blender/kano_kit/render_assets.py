"""Per-asset preview renders (the registry's `previews`): each kit asset alone, LOD0, from a three-quarter view and a front view, in the same light as the street renders. Previews of Blender geometry only.
Run from tools/blender:  PYTHONPATH=. ~/.aro/venv/bin/python -m kano_kit.render_assets --out DIR [--only id,id] [--samples 32]"""
from __future__ import annotations
import argparse, math
from pathlib import Path
import bpy
from mathutils import Vector
from . import build_all, geo, textures, render_ref as R


def bounds(objs):
    lo = Vector((1e9,) * 3); hi = Vector((-1e9,) * 3)
    for o in objs:
        for c in o.bound_box:
            w = o.matrix_world @ Vector(c); lo = Vector(min(a, b) for a, b in zip(lo, w)); hi = Vector(max(a, b) for a, b in zip(hi, w))
    return lo, hi


def main():
    ap = argparse.ArgumentParser(); ap.add_argument("--out", required=True); ap.add_argument("--only", default=""); ap.add_argument("--samples", type=int, default=32); a = ap.parse_args(); out = Path(a.out); out.mkdir(parents=True, exist_ok=True)
    for aid, sp in build_all.SPECS.items():
        if a.only and aid not in a.only.split(","): continue
        asset = geo.Asset(aid, sp["kind"])
        if sp["textured"]: textures.paver_material(); textures.asphalt_material()
        if aid.startswith(("rg_ngn_tree", "aro_hedge", "aro_shrub")): textures.leaf_material()
        for lv, fn in sorted(sp["lods"].items()): asset.lod(lv, fn())
        textures.apply_pbr()
        for lv, o in asset.lods.items():
            if lv > 0: o.hide_render = True
        lo, hi = bounds([asset.lods[0]]); c = (lo + hi) / 2; size = hi - lo; d = max(size.x, size.y, size.z) * (1.55 if aid != "kano_street_oneway_section_a" else 0.55)
        g = geo.Part("ground"); r_ = max(size.x, size.y, 10) * 1.5; g.face_n([(c.x - r_, c.y - r_, lo.z - 0.02), (c.x + r_, c.y - r_, lo.z - 0.02), (c.x + r_, c.y + r_, lo.z - 0.02), (c.x - r_, c.y + r_, lo.z - 0.02)], "concrete", (0, 0, 1)); g.to_object("preview_ground")
        R.world(36, 200, clouds=True)
        for name, loc in (("iso", (c.x - 0.85 * d, c.y - 1.05 * d, c.z + 0.35 * d)), ("front", (c.x, c.y - 1.5 * d, c.z + 0.05 * d))):
            for ob in list(bpy.data.objects):
                if ob.type == "CAMERA": bpy.data.objects.remove(ob)
            R.camera(loc, (c.x, c.y, c.z), 34 if aid != "kano_street_oneway_section_a" else 28, f"cam_{name}")
            R.render(str(out / f"{aid}_{name}.png"), 800, 450, a.samples, exposure=-2.9)
        print("previewed", aid)


if __name__ == "__main__":
    main()
