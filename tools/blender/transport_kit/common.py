"""Shared pieces of the transport kit: additive materials (flat colours; generic, no brand), extrusion helpers, and the catalogue extras (light heads, text faces, interaction sockets) that the Blender
file cannot carry. Coordinates in the builders are Blender's (X width, Y depth with the FRONT at -Y, Z up, 1 unit = 1 m, origin on the ground at the centre of the footprint); `U` converts a Blender point to
the game's prop-local axes (x right, y up, z forward) for the catalogue (measured in docs/BLENDER_PIPELINE.md section 8: the importers mirror X)."""
from __future__ import annotations
import math
from mathutils import Matrix
from aro_blender import materials as AM
from kano_kit.geo import Part

# ------------------------------------------------------------------------------------------------------------------------------------------------ materials (additive; first-pass colours, nothing established)
NEW_MATERIALS = {
    "container_blue":   ((0.08, 0.19, 0.40), 0.55, 0.3, 0.0, "shipping container, generic blue livery (no brand)"),
    "container_red":    ((0.46, 0.11, 0.08), 0.55, 0.3, 0.0, "shipping container, generic oxide-red livery (no brand)"),
    "container_green":  ((0.11, 0.30, 0.19), 0.55, 0.3, 0.0, "shipping container, generic green livery (no brand)"),
    "container_grey":   ((0.50, 0.52, 0.52), 0.55, 0.3, 0.0, "shipping container, generic light-grey livery (no brand)"),
    "sheet_cream":      ((0.74, 0.71, 0.62), 0.6, 0.3, 0.0, "profiled steel cladding sheet, cream"),
    "sheet_green":      ((0.20, 0.36, 0.28), 0.6, 0.3, 0.0, "profiled steel cladding sheet, green"),
    "warning_yellow":   ((0.86, 0.66, 0.04), 0.6, 0.0, 0.0, "safety yellow paint (bollards, dock edges)"),
    "board_dark":       ((0.04, 0.045, 0.05), 0.5, 0.0, 0.0, "dark board face (departures board, display) before the text is drawn by the game"),
    "signal_red":       ((0.80, 0.07, 0.05), 0.4, 0.0, 0.0, "traffic signal red lens (static)"),
    "signal_amber":     ((0.90, 0.55, 0.04), 0.4, 0.0, 0.0, "traffic signal amber lens (static)"),
    "signal_green":     ((0.06, 0.62, 0.22), 0.4, 0.0, 0.0, "traffic signal green lens (static)"),
    "sign_yellow":      ((0.88, 0.70, 0.06), 0.45, 0.0, 0.0, "warning sign yellow"),
    "pavement_grey":    ((0.40, 0.40, 0.39), 0.9, 0.0, 0.3, "light grey pavement slab"),
}
for _k, _v in NEW_MATERIALS.items(): AM.LIBRARY.setdefault(_k, _v)


def U(x, y, z): return (round(-x, 3), round(z, 3), round(-y, 3))
"""Blender point -> game prop-local axes (x right, y up, z forward)."""


# ------------------------------------------------------------------------------------------------------------------------------------------------ extrusions along X and Y
def _ccw(pts):
    """The polygon with counter-clockwise winding in its own plane (whatever order it was given in)."""
    area = sum(pts[i][0] * pts[(i + 1) % len(pts)][1] - pts[(i + 1) % len(pts)][0] * pts[i][1] for i in range(len(pts)))
    return list(pts) if area >= 0 else list(reversed(pts))


def prism_x(p: Part, prof, x0, x1, key):
    """Extrudes a polygon given as (y, z) points between x0 and x1 (gable ends, roof sections, canopies). A cyclic axis permutation, so the outward winding of the prism is kept."""
    s = p.mark(); p.prism(_ccw([(y, z) for y, z in prof]), x0, x1, key)
    p.xform(s, Matrix(((0, 0, 1, 0), (1, 0, 0, 0), (0, 1, 0, 0), (0, 0, 0, 1))))


def prism_y(p: Part, prof, y0, y1, key):
    """Extrudes a polygon given as (x, z) points between y0 and y1 (signs, plates, side profiles): the prism is built in (z, x, y) order and permuted back (cyclic, winding kept)."""
    s = p.mark(); p.prism(_ccw([(z, x) for x, z in prof]), y0, y1, key)
    p.xform(s, Matrix(((0, 1, 0, 0), (0, 0, 1, 0), (1, 0, 0, 0), (0, 0, 0, 1))))


def circle_pts(r, n, cx=0.0, cy=0.0, start=0.0): return [(cx + r * math.cos(start + 2 * math.pi * k / n), cy + r * math.sin(start + 2 * math.pi * k / n)) for k in range(n)]


# ------------------------------------------------------------------------------------------------------------------------------------------------ catalogue extras
EXTRAS: dict = {}
"""asset id -> {'light': LightSpec dict (game axes), 'label': LabelSpec dict, 'sockets': [...], 'tags': [...], 'family': 'A'|'B'|'C', 'cls': 'GLOBAL'|..., 'ref': [...] }"""

LIGHT_PRESETS = {
    "lamp":      dict(kind="lamp", r=1.0, g=0.78, b=0.5, intensity=1.0, range=24, haloSize=1.8, poolRadius=5.5, poolShift=3.0, realLight=True),
    "lamp_warm": dict(kind="lamp", r=1.0, g=0.72, b=0.38, intensity=0.9, range=18, haloSize=1.5, poolRadius=4.5, poolShift=0.0, realLight=False),
    "mast":      dict(kind="lamp", r=1.0, g=0.9, b=0.72, intensity=1.5, range=36, haloSize=2.4, poolRadius=11.0, poolShift=0.0, realLight=True),
    "glow":      dict(kind="glow", r=1.0, g=0.9, b=0.7, intensity=0.8, range=8, haloSize=1.5, poolRadius=3.0, poolShift=0.0, realLight=False),
    "glow_door": dict(kind="glow", r=1.0, g=0.9, b=0.7, intensity=0.9, range=10, haloSize=1.6, poolRadius=3.6, poolShift=1.6, realLight=False, shiftFront=True),
    "signal_r":  dict(kind="glow", r=1.0, g=0.1, b=0.06, intensity=0.9, range=3, haloSize=0.6, poolRadius=0, poolShift=0.0, realLight=False),
    "signal_a":  dict(kind="glow", r=1.0, g=0.6, b=0.08, intensity=0.9, range=3, haloSize=0.6, poolRadius=0, poolShift=0.0, realLight=False),
    "signal_g":  dict(kind="glow", r=0.1, g=1.0, b=0.35, intensity=0.9, range=3, haloSize=0.6, poolRadius=0, poolShift=0.0, realLight=False),
}


def extra(asset_id, family, cls, ref, heads=None, preset=None, light=None, label=None, sockets=None, tags=(), **over):
    """Registers what the catalogue needs beyond the measurements: `heads` are Blender points (converted here), `preset` a LIGHT_PRESETS key, `label` a dict with Blender centre `c`, width, height and optionally
    yawDeg and colour, `sockets` interaction points for gameplay (name, kind, Blender point, yaw about up in degrees; data only, nothing in the game reads them yet)."""
    e = {"family": family, "class": cls, "ref": list(ref), "tags": list(tags)}
    if heads:
        l = dict(LIGHT_PRESETS[preset or "glow"]); l.update(over); l["heads"] = [dict(zip("xyz", U(*h))) for h in heads]; e["light"] = l
    if label:
        c = label["c"]; x, y, z = U(*c); e["label"] = {"x": x, "y": y, "z": z, "width": label["width"], "height": label["height"], "yawDeg": label.get("yawDeg", 0), "colour": label.get("colour", "white"), "fontMax": label.get("fontMax", 0)}
    if sockets: e["sockets"] = [{"name": n, "kind": k, "x": U(*p)[0], "y": U(*p)[1], "z": U(*p)[2], "yawDeg": yaw} for n, k, p, yaw in sockets]
    EXTRAS[asset_id] = e


def rib_run(p: Part, x0, x1, y, z0, z1, key, pitch, w=0.12, d=0.06, face=-1):
    """Vertical ribs along a wall (a cheap stand-in for corrugation): boxes `d` proud of the wall at y, every `pitch` metres between x0 and x1."""
    n = max(int((x1 - x0) / pitch), 1)
    for i in range(n + 1):
        x = x0 + (x1 - x0) * i / n
        if face < 0: p.box(x - w / 2, y - d, z0, x + w / 2, y, z1, key, skip=("bottom", "back"))
        else: p.box(x - w / 2, y, z0, x + w / 2, y + d, z1, key, skip=("bottom", "front"))
