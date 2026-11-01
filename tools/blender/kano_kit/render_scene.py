"""Review renders of the assembled street: the reference camera of concept R-006 (eye level 1.7 m at the left kerb line looking down the street, 28 mm), and the front, side and street-level views.
Run from tools/blender:  PYTHONPATH=. ~/.aro/venv/bin/python -m kano_kit.render_scene --out DIR [--views ref,front,side,street1,street2] [--w 1280 --h 720 --samples 48]"""
from __future__ import annotations
import argparse, math, json
from pathlib import Path
import bpy
from . import build_all, render_ref as R

# name -> (camera location, look-at, lens mm, ortho scale or None)
VIEWS = {
    "ref": ((-3.2, -45.0, 1.7), (0.6, 60.0, 2.6), 28.0, None),            # reference camera: R-006
    "front": ((-5.2, 10.0, 2.1), (16.0, 10.0, 3.6), 30.0, None),            # from the left sidewalk, facing the right-hand frontages: walls, gates and villas
    "side": ((0.0, -62.0, 4.6), (0.0, 0.0, 4.0), 24.0, None),             # along the street from beyond the section start: the street profile (side view)
    "street1": ((4.3, -30.0, 1.6), (-6.0, -4.0, 1.6), 28.0, None),        # pedestrian on the right sidewalk, looking back toward the left-hand gate and wall
    "street2": ((-1.7, -20.0, 1.2), (5.0, 10.0, 1.5), 28.0, None),        # driver's-eye view in the left lane
    "top": ((0.0, 0.0, 95.0), (0.0, 0.0, 0.0), 50.0, None),
}


def main():
    ap = argparse.ArgumentParser(); ap.add_argument("--out", required=True); ap.add_argument("--views", default="ref"); ap.add_argument("--w", type=int, default=1280); ap.add_argument("--h", type=int, default=720)
    ap.add_argument("--samples", type=int, default=48); ap.add_argument("--sun", type=float, default=200.0); ap.add_argument("--elev", type=float, default=36.0); ap.add_argument("--exposure", type=float, default=-2.9)   # was -1.9 while the textures were double-decoded (too dark); with correct colours the same sun needs about one stop less
    ap.add_argument("--placements", default="")
    a = ap.parse_args(); out = Path(a.out); out.mkdir(parents=True, exist_ok=True)
    P = build_all.build_scene()
    if a.placements: Path(a.placements).write_text(json.dumps({"scene": "kano_residential_section_a", "frame": "street along +Y, right +X, metres, Blender axes", "items": P.items}, indent=1))
    R.world(a.elev, a.sun)
    for name in [v for v in a.views.split(",") if v]:
        loc, tgt, lens, ortho = VIEWS[name]; cam = R.camera(loc, tgt, lens, f"cam_{name}", ortho)
        if name == "top": cam.data.type = "ORTHO"; cam.data.ortho_scale = 110
        bpy.context.scene.camera = cam
        R.render(str(out / f"kano_residential_{name}.png"), a.w, a.h, a.samples, exposure=a.exposure); print("rendered", name)


if __name__ == "__main__":
    main()
