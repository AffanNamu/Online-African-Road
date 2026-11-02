"""CONTENT-SIDE DRAW-CALL PROTOTYPE (owner instruction of 2026-11-02): merges the compound-wall components of one plot frontage into a single mesh asset, to measure how many draw calls that saves in the
running game. These are NEW assets; the frozen v002 assets are not touched, not rebuilt differently and not replaced. The geometry is the v002 geometry (the same builders, the same dimensions) placed
exactly where the individual pieces stand in the review scene; only the number of renderers changes.

What is merged, per plot side (a 24 m frontage of five 4.8 m bays): four wall panels, five piers and the two gate-pier lanterns. The gate stays a separate asset (its 2,156 triangles would put the frontage over
the 2,500 triangle budget of its kind) and is placed exactly as before. Each far plot's five wall panels (LOD2 distance) become one far run.

Why four variants: the merged piece is a mirror pair. Seen from the street, the right-hand plots are rotated -90 degrees and the left-hand plots +90 degrees, so the local order of piers and the position of
the gate bay differ between the sides; the review scene alternates the gate bay between bay 2 and bay 3 (world order). In the asset's local order the variants are left/gate bay 2, left/gate bay 3,
right/gate bay 1 and right/gate bay 2 (a different layout needs different frontage assets: that is the price of merging, recorded in the evaluation).

Local frame of a frontage: wall along X, thickness along Y, the street face is -Y (as the wall panel), origin at the centre of the 24 m frontage on the ground.
Run from tools/blender:  PYTHONPATH=. ~/.aro/venv/bin/python -m kano_kit.frontage [--only id,id] [--no-export] [--report file]"""
from __future__ import annotations
import argparse, json, math, sys
from pathlib import Path
import bpy
from mathutils import Matrix, Vector
from aro_blender import workspace as ws
from . import walls, assemble
from .geo import Part

PITCH, PLOT, PIER_X, PANEL_LEN, FACE = assemble.PITCH, assemble.PLOT, assemble.PIER_X, assemble.PANEL_LEN, assemble.FACE
LANTERN_X_FACE = 0.275            # the lantern sits on the street face of its pier (half the pier width)
LANTERN_Z = 1.75
FAR_RUN = "aro_frontage_far_run_a"
VARIANTS = {"aro_frontage_l2_a": (-1, 2), "aro_frontage_l3_a": (-1, 3), "aro_frontage_r1_a": (1, 1), "aro_frontage_r2_a": (1, 2)}   # id -> (street side, local gate bay)
MERGED_FROM = ("aro_wall_compound_panel_a", "aro_pier_stone_a", "aro_lantern_wall_a")


def merge(dst: Part, src: Part, m: Matrix) -> None:
    """Copies the faces of `src` into `dst` under the rigid transform `m` (rotation and translation only: the winding is kept). Materials are matched by their kit key."""
    vmap = {v: dst.bm.verts.new(m @ v.co) for v in src.bm.verts}                                   # keyed by the vertex itself: v.index is not valid on a freshly built bmesh
    for f in src.bm.faces:
        try: nf = dst.bm.faces.new([vmap[v] for v in f.verts])
        except ValueError: continue                                                              # a duplicate face: the source had one too and it is not drawn twice
        nf.material_index = dst.mi(src.keys[f.material_index]); nf.smooth = f.smooth
    src.bm.free()


def _at(x: float, y: float = 0.0, z: float = 0.0) -> Matrix: return Matrix.Translation(Vector((x, y, z)))


def pier_lines(side: int) -> range:
    """Local pier lines (0..5, x = (j - 2.5) * pitch): the left plots own lines 0..4, the right plots 1..5 (their world lines 4..0 mirrored); the sixth world pier of the last plot stays a separate asset."""
    return range(0, 5) if side < 0 else range(1, 6)


def frontage(lod: int, side: int, gate_bay: int) -> Part:
    """One plot side: four wall panels, five piers, two lanterns (LOD0 and LOD1; none at LOD2) in the local frame described above."""
    p = Part("frontage")
    for j in range(5):
        if j == gate_bay: continue                                                               # the gate stands in this bay
        merge(p, walls.wall_panel(lod, PANEL_LEN), _at((j - 2.0) * PITCH))
    for j in pier_lines(side): merge(p, walls.pier(lod), _at((j - 2.5) * PITCH))
    if lod <= 1:
        for j in (gate_bay, gate_bay + 1): merge(p, walls.lantern(0 if lod == 0 else 1), _at((j - 2.5) * PITCH, -LANTERN_X_FACE, LANTERN_Z))
    return p


def far_run() -> Part:
    """The five far wall bays of one plot as one mesh (LOD2 boxes with the piers' gaps, as the individual far panels stand)."""
    p = Part("far_run")
    for j in range(5): merge(p, walls.wall_panel(2, PANEL_LEN), _at((j - 2.0) * PITCH))
    return p


SPECS: dict = {}


def _spec(aid, lods, note):
    SPECS[aid] = dict(id=aid, kind="environments", lods=lods, col=None, textured=False, note=note)


for _aid, (_side, _g) in VARIANTS.items():
    _spec(_aid, {lv: (lambda lv=lv, s=_side, g=_g: frontage(lv, s, g)) for lv in (0, 1, 2)},
          f"merged plot frontage, {'left' if _side < 0 else 'right'} side, gate in local bay {_g}: 4 wall panels, 5 piers, 2 lanterns of the v002 kit as ONE mesh (draw-call prototype; not a replacement of the v002 assets)")
_spec(FAR_RUN, {0: far_run}, "the five far wall bays of one plot as one LOD2-style mesh (draw-call prototype)")


# ------------------------------------------------------------------------------------------------------------------------------------- placements
def _plot(y: float) -> int: return math.floor((y + 48.0 + 1e-6) / PLOT)                         # absolute plot index along the street (plot 0 starts at y = -48)


def merged_items(items: list[dict]) -> list[dict]:
    """The review placements with each near plot side's panels, piers and lanterns replaced by one frontage asset and each far plot side's panels by one far run. Everything else is unchanged.
    The input is the placement list of kano_kit.assemble (Blender axes); nothing is guessed: the gate of a plot decides its variant, the pier at the end of the last plot stays separate."""
    gates = {}                                                                                   # (plot, side) -> gate bay in world order
    for it in items:
        if it["asset"] == "aro_gate_ornate_steel_a":
            side = 1 if it["x"] > 0 else -1; q = _plot(it["y"]); y0 = -48.0 + PLOT * q; gates[(q, side)] = int(round((it["y"] - y0) / PITCH - 0.5))
    out, far_done = [], set()
    for it in items:
        a = it["asset"]; side = 1 if it["x"] > 0 else -1
        if a not in MERGED_FROM or abs(abs(it["x"]) - PIER_X) > 0.31: out.append(it); continue   # lanterns stand 0.275 m street-ward of the wall line
        q = _plot(it["y"]); y0 = -48.0 + PLOT * q
        if (q, side) in gates:                                                                   # a near plot side
            if a == "aro_pier_stone_a" and not (-1e-3 <= it["y"] - y0 <= 4 * PITCH + 1e-3): out.append(it); continue   # the sixth pier closes the last plot of the section: its own asset
            if (q, side) in far_done: continue
            far_done.add((q, side)); g = gates[(q, side)]; local_gate = g if side < 0 else 4 - g
            aid = f"aro_frontage_{'l' if side < 0 else 'r'}{local_gate}_a"
            if aid not in VARIANTS: raise SystemExit(f"plot {q} side {side}: no frontage variant for local gate bay {local_gate}")
            out.append({"asset": aid, "x": round(side * PIER_X, 3), "y": round(y0 + PLOT / 2, 3), "z": 0.0, "yawDeg": FACE[side], "scale": 1.0, "lod": 0, "tag": "frontage"})
        else:                                                                                    # a far plot side: only panels
            if a != "aro_wall_compound_panel_a": out.append(it); continue
            if (q, side) in far_done: continue
            far_done.add((q, side)); out.append({"asset": FAR_RUN, "x": round(side * PIER_X, 3), "y": round(y0 + PLOT / 2, 3), "z": 0.0, "yawDeg": FACE[side], "scale": 1.0, "lod": 2, "tag": "frontage"})
    return out


def main() -> int:
    from . import build_all as KB
    ap = argparse.ArgumentParser(); ap.add_argument("--only", default=""); ap.add_argument("--no-export", action="store_true"); ap.add_argument("--report", default=""); a = ap.parse_args()
    KB.ROOT = ws.ensure(ws.default_root()); report = []
    for aid, sp in SPECS.items():
        if a.only and aid not in a.only.split(","): continue
        r = KB.produce(sp, not a.no_export); report.append(r)
        print(f"{'PASS' if r['passed'] else 'FAIL'} {aid}: tris {r['stats'].get('lod_triangles')} size {r['stats'].get('size_m')} materials {r['stats'].get('materials')}" + (f" errors {[e['message'] for e in r['errors']]}" if r["errors"] else ""))
    if a.report: Path(a.report).write_text(json.dumps(report, indent=1))
    return 0 if all(r["passed"] for r in report) else 1


if __name__ == "__main__":
    sys.exit(main())
