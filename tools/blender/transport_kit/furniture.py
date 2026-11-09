"""Family C: road furniture and street kit (all phases; references R-004, R-022, R-023, RB-07, RB-09). Generic fictional signs (shapes and numerals only, no operator, no brand, no plate), street lighting that follows
the catalogue light model (lamp heads declared in `extra`), barriers on the usual profiles. Sign faces look toward -Y. Every function takes the LOD (0 detailed, 1 medium, 2 mass: a few boxes)."""
from __future__ import annotations
import math
from mathutils import Vector
from kano_kit.geo import Part
from kano_kit.lights import _text_mesh
from .common import prism_x, prism_y, circle_pts, extra

GAL, BLK, CON, CDK = "metal_galvanised", "metal_gate_black", "concrete", "concrete_dark"
REF = ["R-004", "R-012", "R-022", "R-023", "RB-07", "RB-09"]


# ------------------------------------------------------------------------------------------------------------------------------------------------ street lights
def streetlight_single(lod):
    """Single-arm 9 m street light: tapered 12-sided galvanised pole on a base plate with a service door, a trussed 2.6 m arm swept toward -Y (the road) and a cobra-head luminaire with a lens. Lamp head at the arm tip."""
    p = Part("light"); seg = 12 if lod == 0 else 8; H = 9.0
    p.tube((0, 0, 0.02), (0, 0, H - 0.2), 0.14, 0.065, seg, GAL, caps=(False, True), smooth=(lod == 0))
    if lod >= 2: p.box(-0.15, -2.6, 8.9, 0.15, 0.15, 9.2, GAL); return p
    p.box(-0.24, -0.24, 0.0, 0.24, 0.24, 0.03, GAL); p.tube((0, 0, 0.03), (0, 0, 0.55), 0.19, 0.15, seg, GAL, caps=(False, True))
    arm = [Vector((0, -2.5 * t, H - 0.35 + 0.55 * (1 - (1 - t) ** 2.0))) for t in [k / (7 if lod == 0 else 3) for k in range(8 if lod == 0 else 4)]]
    p.path_tube(arm, [0.055 - 0.02 * (i / (len(arm) - 1)) for i in range(len(arm))], 8 if lod == 0 else 4, GAL, caps=(False, True))
    ex, ez = arm[-1].y, arm[-1].z
    p.box(-0.18, ex - 0.55, ez - 0.03, 0.18, ex + 0.25, ez + 0.1, GAL); p.box(-0.15, ex - 0.5, ez - 0.055, 0.15, ex + 0.2, ez - 0.03, "lantern_glow")
    if lod == 0:
        p.frustum(-0.18, ex - 0.55, ez + 0.1, 0.18, ex + 0.25, ez + 0.17, GAL, 0.05, skip=("bottom",))
        p.box(-0.05, -0.155, 0.7, 0.05, -0.135, 1.6, BLK); p.tube((0, -0.4, H - 0.8), (0, -1.6, H - 0.12), 0.02, 0.02, 6, GAL)                              # service door, arm tie
        for k in range(4): a = k * math.pi / 2 + math.pi / 4; p.tube((math.cos(a) * 0.2, math.sin(a) * 0.2, 0.03), (math.cos(a) * 0.2, math.sin(a) * 0.2, 0.13), 0.025, 0.025, 6, GAL)   # anchor bolts
        p.box(-0.04, ex + 0.25, ez - 0.02, 0.04, ex + 0.33, ez + 0.07, BLK)
    return p


def streetlight_double(lod):
    """Twin-arm 12 m street light for a planter median: a tapered pole on a concrete plinth, two raked arms toward -X and +X (one over each carriageway) and a luminaire on each."""
    p = Part("light"); seg = 12 if lod == 0 else 8; H = 12.0
    p.box(-0.45, -0.45, 0.0, 0.45, 0.45, 0.35, CON, skip=("bottom",))
    p.tube((0, 0, 0.35), (0, 0, H), 0.17, 0.08, seg, GAL, caps=(False, True), smooth=(lod == 0))
    if lod >= 2: p.box(-2.9, -0.15, 11.4, 2.9, 0.15, 11.9, GAL); return p
    for s in (-1, 1):
        arm = [Vector((s * 2.6 * t, 0, H - 0.5 + 0.5 * (1 - (1 - t) ** 2.0))) for t in [k / (7 if lod == 0 else 3) for k in range(8 if lod == 0 else 4)]]
        p.path_tube(arm, [0.06 - 0.02 * (i / (len(arm) - 1)) for i in range(len(arm))], 8 if lod == 0 else 4, GAL, caps=(False, True))
        ex, ez = arm[-1].x, arm[-1].z
        p.box(ex - 0.25 - 0.28 * s, -0.18, ez - 0.03, ex + 0.25 - 0.28 * s, 0.18, ez + 0.1, GAL); p.box(ex - 0.22 - 0.28 * s, -0.15, ez - 0.055, ex + 0.22 - 0.28 * s, 0.15, ez - 0.03, "lantern_glow")
        if lod == 0:
            p.tube((s * 0.3, 0, H - 1.2), (s * 2.4, 0, H - 0.12), 0.02, 0.02, 6, GAL); p.frustum(ex - 0.25 - 0.28 * s, -0.18, ez + 0.1, ex + 0.25 - 0.28 * s, 0.18, ez + 0.17, GAL, 0.05, skip=("bottom",))
    if lod == 0:
        p.tube((0, 0, 0.35), (0, 0, 0.9), 0.23, 0.17, seg, GAL, caps=(False, True)); p.box(-0.06, -0.19, 1.0, 0.06, -0.17, 2.1, BLK)
        for sx in (-0.3, 0.3):
            for sy in (-0.3, 0.3): p.tube((sx, sy, 0.35), (sx, sy, 0.45), 0.025, 0.025, 6, GAL)
        p.tube((0, 0, 11.8), (0, 0, 12.15), 0.07, 0.03, 8, GAL)
    return p


def streetlight_ornamental(lod):
    """Decorative 6 m lantern pole for islands and plazas: a stepped base, a fluted tapered shaft, a collar, two scroll brackets and a glazed lantern with a roof and a finial."""
    p = Part("light"); seg = 12 if lod == 0 else 8
    if lod >= 2: p.box(-0.1, -0.1, 0.0, 0.1, 0.1, 5.0, BLK, skip=("bottom", "top")); p.box(-0.25, -0.25, 5.0, 0.25, 0.25, 6.0, "lantern_glow"); return p
    p.box(-0.4, -0.4, 0.0, 0.4, 0.4, 0.15, CON, skip=("bottom",)); p.box(-0.3, -0.3, 0.15, 0.3, 0.3, 0.45, CON, skip=("bottom",))
    p.tube((0, 0, 0.45), (0, 0, 4.9), 0.14, 0.07, seg, BLK, caps=(False, True), smooth=(lod == 0))
    p.tube((0, 0, 4.9), (0, 0, 5.0), 0.12, 0.12, seg, BLK, caps=(True, True), smooth=False)
    p.frustum(-0.26, -0.26, 5.0, 0.26, 0.26, 5.1, BLK, 0.05)
    p.box(-0.2, -0.2, 5.1, 0.2, 0.2, 5.65, "lantern_glow", skip=("bottom",))
    for sx, sy in ((-0.2, -0.2), (0.2, -0.2), (0.2, 0.2), (-0.2, 0.2)): p.box(sx - 0.015, sy - 0.015, 5.1, sx + 0.015, sy + 0.015, 5.65, BLK)
    p.frustum(-0.3, -0.3, 5.65, 0.3, 0.3, 5.9, BLK, 0.24, skip=("bottom",))
    if lod == 0:
        p.tube((0, 0, 5.9), (0, 0, 6.1), 0.03, 0.01, 6, BLK)
        for s in (-1, 1): p.path_tube([Vector((s * 0.07, 0, 3.6)), Vector((s * 0.35, 0, 3.7)), Vector((s * 0.45, 0, 3.95)), Vector((s * 0.3, 0, 4.15))], 0.015, 5, BLK, caps=(True, True))     # scroll brackets
        p.tube((0, 0, 3.2), (0, 0, 3.3), 0.1, 0.1, seg, BLK, caps=(True, True), smooth=False); p.tube((0, 0, 1.5), (0, 0, 1.6), 0.12, 0.12, seg, BLK, caps=(True, True), smooth=False)   # shaft rings
    return p


# ------------------------------------------------------------------------------------------------------------------------------------------------ signs
def _pole(p: Part, lod, top, x=0.0):
    """The 3 m post: a box at LOD2; a 6-sided tube at LOD1; at LOD0 a 12-sided tube with a ground sleeve, a cap and two clamp bands."""
    if lod >= 2: p.box(x - 0.035, 0.015, 0.0, x + 0.035, 0.085, top, GAL, skip=("bottom", "top")); return
    p.tube((x, 0.05, 0.0), (x, 0.05, top), 0.035, 0.035, 12 if lod == 0 else 6, GAL, caps=(True, True), smooth=(lod == 0))
    if lod == 0:
        p.tube((x, 0.05, 0.0), (x, 0.05, 0.35), 0.05, 0.05, 12, GAL, caps=(False, True), smooth=False); p.tube((x, 0.05, top), (x, 0.05, top + 0.03), 0.045, 0.045, 12, GAL, caps=(True, True), smooth=False)
        for z in (top - 0.28, top - 0.72): p.tube((x, 0.05, z), (x, 0.05, z + 0.04), 0.043, 0.043, 12, GAL, caps=(False, False), smooth=False)


def _numerals(p: Part, text, width, zc, key="kerb_black", y=-0.0275):
    for pts in _text_mesh(text, width, 0.01): p.face([(q.x, y - q.z, zc + q.y) for q in pts], key)


def sign_stop(lod):
    """Octagonal red STOP sign, 0.76 m across flats, white border and white lettering, on a 3 m post."""
    p = Part("sign"); zc = 2.45; r = 0.76 / 2 / math.cos(math.pi / 8)
    _pole(p, lod, 2.45 if lod >= 2 else 2.9)
    if lod >= 2: p.box(-0.38, -0.02, zc - 0.38, 0.38, 0.03, zc + 0.38, "paint_red"); return p
    prism_y(p, circle_pts(r, 8, 0.0, zc, math.pi / 8), 0.0, 0.03, "paint_white")                                                                        # rim and back
    prism_y(p, circle_pts(r * 0.93, 8, 0.0, zc, math.pi / 8), -0.012, 0.0, "paint_red")
    if lod == 0:
        _numerals(p, "STOP", 0.52, zc, "paint_white", -0.0125)
    p.box(-0.06, 0.03, zc - 0.25, 0.06, 0.07, zc - 0.21, GAL); p.box(-0.06, 0.03, zc + 0.21, 0.06, 0.07, zc + 0.25, GAL)
    return p


def sign_yield(lod):
    """Downward-pointing triangular give-way sign, red border, white centre, 0.9 m sides, on a 3 m post."""
    p = Part("sign"); zc = 2.5
    _pole(p, lod, 2.2 if lod >= 2 else 2.9)
    outer = [(-0.45, zc + 0.26), (0.45, zc + 0.26), (0.0, zc - 0.52)]; inner = [(-0.29, zc + 0.19), (0.29, zc + 0.19), (0.0, zc - 0.31)]
    if lod >= 2: p.box(-0.45, -0.02, zc - 0.5, 0.45, 0.03, zc + 0.26, "paint_red"); return p
    prism_y(p, outer, 0.0, 0.03, "paint_white"); prism_y(p, [(x * 0.97, zc + (z - zc) * 0.97) for x, z in outer], -0.012, 0.0, "paint_red"); prism_y(p, inner, -0.02, -0.012, "paint_white")
    p.box(-0.06, 0.03, zc - 0.05, 0.06, 0.07, zc + 0.0, GAL)
    if lod == 0:
        p.box(-0.06, 0.03, zc + 0.17, 0.06, 0.07, zc + 0.22, GAL)
        for sx in (-0.41, 0.41): p.tube((sx, -0.02, zc + 0.2), (sx, -0.026, zc + 0.2), 0.016, 0.016, 8, GAL, caps=(True, True), smooth=False)       # corner bolts
        p.tube((0.0, -0.02, zc - 0.47), (0.0, -0.026, zc - 0.47), 0.016, 0.016, 8, GAL, caps=(True, True), smooth=False)
        prism_y(p, [(-0.47, zc + 0.275), (0.47, zc + 0.275), (0.0, zc - 0.55)], 0.03, 0.045, GAL)                                                       # back stiffening plate
    return p


def _speed(num):
    def build(lod):
        """Circular speed-limit sign (red ring, white disc, black numerals), 0.7 m across, on a 3 m post."""
        p = Part("sign"); zc = 2.5
        _pole(p, lod, 2.15 if lod >= 2 else 2.9)
        if lod >= 2: p.box(-0.35, -0.02, zc - 0.35, 0.35, 0.03, zc + 0.35, "paint_red"); return p
        n = 24 if lod == 0 else 14
        prism_y(p, circle_pts(0.36, n, 0.0, zc), 0.0, 0.03, "paint_white"); prism_y(p, circle_pts(0.35, n, 0.0, zc), -0.012, 0.0, "paint_red"); prism_y(p, circle_pts(0.255, n, 0.0, zc), -0.02, -0.012, "paint_white")
        if lod == 0: _numerals(p, str(num), 0.30, zc, "kerb_black", -0.0205)
        p.box(-0.06, 0.03, zc - 0.25, 0.06, 0.07, zc - 0.21, GAL); p.box(-0.06, 0.03, zc + 0.21, 0.06, 0.07, zc + 0.25, GAL)
        return p
    return build


def sign_no_entry(lod):
    """Circular no-entry sign (red disc, white bar), 0.7 m across, on a 3 m post."""
    p = Part("sign"); zc = 2.5
    _pole(p, lod, 2.15 if lod >= 2 else 2.9)
    if lod >= 2: p.box(-0.35, -0.02, zc - 0.35, 0.35, 0.03, zc + 0.35, "paint_red"); return p
    n = 24 if lod == 0 else 14
    prism_y(p, circle_pts(0.36, n, 0.0, zc), 0.0, 0.03, "paint_white"); prism_y(p, circle_pts(0.35, n, 0.0, zc), -0.012, 0.0, "paint_red"); p.box(-0.26, -0.024, zc - 0.055, 0.26, -0.012, zc + 0.055, "paint_white")
    p.box(-0.06, 0.03, zc - 0.25, 0.06, 0.07, zc - 0.21, GAL); p.box(-0.06, 0.03, zc + 0.21, 0.06, 0.07, zc + 0.25, GAL)
    return p


def sign_bus_stop(lod):
    """Square blue bus-stop sign, 0.6 m, with a white bus pictogram, on a 3 m post."""
    p = Part("sign"); zc = 2.6
    _pole(p, lod, 2.3 if lod >= 2 else 2.9)
    if lod >= 2: p.box(-0.3, -0.02, zc - 0.3, 0.3, 0.03, zc + 0.3, "sign_blue"); return p
    p.box(-0.3, 0.0, zc - 0.3, 0.3, 0.03, zc + 0.3, "paint_white", skip=("front",)); p.box(-0.28, -0.012, zc - 0.28, 0.28, 0.0, zc + 0.28, "sign_blue", skip=("back",))
    p.box(-0.2, -0.02, zc - 0.07, 0.2, -0.012, zc + 0.13, "paint_white", skip=("back",))                                                                 # body
    if lod == 0:
        for k in range(3): p.box(-0.17 + 0.12 * k, -0.026, zc + 0.02, -0.07 + 0.12 * k, -0.02, zc + 0.1, "sign_blue", skip=("back",))                       # windows
        for sx in (-0.12, 0.12): p.tube((sx, -0.02, zc - 0.09), (sx, -0.03, zc - 0.09), 0.035, 0.035, 10, "kerb_black", caps=(True, True))                   # wheels
        p.box(-0.2, -0.026, zc - 0.03, 0.2, -0.02, zc - 0.015, "sign_blue", skip=("back",))
    p.box(-0.06, 0.03, zc - 0.2, 0.06, 0.07, zc - 0.16, GAL); p.box(-0.06, 0.03, zc + 0.16, 0.06, 0.07, zc + 0.2, GAL)
    return p


def sign_zebra(lod):
    """Square blue pedestrian-crossing sign, 0.6 m, white triangle with a figure, on a 3 m post."""
    p = Part("sign"); zc = 2.6
    _pole(p, lod, 2.3 if lod >= 2 else 2.9)
    if lod >= 2: p.box(-0.3, -0.02, zc - 0.3, 0.3, 0.03, zc + 0.3, "sign_blue"); return p
    p.box(-0.3, 0.0, zc - 0.3, 0.3, 0.03, zc + 0.3, "paint_white", skip=("front",)); p.box(-0.28, -0.012, zc - 0.28, 0.28, 0.0, zc + 0.28, "sign_blue", skip=("back",))
    prism_y(p, [(-0.24, zc - 0.22), (0.24, zc - 0.22), (0.0, zc + 0.22)], -0.02, -0.012, "paint_white")
    prism_y(p, [(-0.19, zc - 0.185), (0.19, zc - 0.185), (0.0, zc + 0.15)], -0.026, -0.02, "sign_blue")
    if lod == 0:
        p.box(-0.02, -0.032, zc - 0.12, 0.02, -0.026, zc + 0.04, "paint_white", skip=("back",)); p.box(-0.06, -0.032, zc - 0.03, 0.06, -0.026, zc + 0.0, "paint_white", skip=("back",))
        p.tube((0.0, -0.034, zc + 0.08), (0.0, -0.036, zc + 0.08), 0.03, 0.03, 10, "paint_white", caps=(True, True))
        for k in range(3): p.box(-0.2 + 0.14 * k, -0.026, zc - 0.215, -0.12 + 0.14 * k, -0.02, zc - 0.19, "paint_white", skip=("back",))
    p.box(-0.06, 0.03, zc - 0.2, 0.06, 0.07, zc - 0.16, GAL); p.box(-0.06, 0.03, zc + 0.16, 0.06, 0.07, zc + 0.2, GAL)
    return p


def sign_no_left_turn(lod):
    """Circular no-left-turn sign (red ring, white disc, black left arrow, red diagonal bar), 0.7 m across, on a 3 m post."""
    p = Part("sign"); zc = 2.5
    _pole(p, lod, 2.15 if lod >= 2 else 2.9)
    if lod >= 2: p.box(-0.35, -0.02, zc - 0.35, 0.35, 0.03, zc + 0.35, "paint_red"); return p
    n = 24 if lod == 0 else 14
    prism_y(p, circle_pts(0.36, n, 0.0, zc), 0.0, 0.03, "paint_white"); prism_y(p, circle_pts(0.35, n, 0.0, zc), -0.012, 0.0, "paint_red"); prism_y(p, circle_pts(0.255, n, 0.0, zc), -0.02, -0.012, "paint_white")
    prism_y(p, [(0.14, zc - 0.03), (-0.04, zc - 0.03), (-0.04, zc - 0.11), (-0.18, zc + 0.0), (-0.04, zc + 0.11), (-0.04, zc + 0.03), (0.14, zc + 0.03)], -0.026, -0.02, "kerb_black")                    # left arrow
    if lod == 0: prism_y(p, [(-0.2, zc + 0.2), (-0.16, zc + 0.24), (0.2, zc - 0.2), (0.16, zc - 0.24)], -0.032, -0.026, "paint_red")                      # diagonal bar
    p.box(-0.06, 0.03, zc - 0.25, 0.06, 0.07, zc - 0.21, GAL); p.box(-0.06, 0.03, zc + 0.21, 0.06, 0.07, zc + 0.25, GAL)
    return p


def sign_direction(lod):
    """Blue-green destination sign on two posts: a 3 m x 1.2 m panel with a white border and a blank text field (the game draws the destination text), 4 m overall, faces -Y."""
    p = Part("dsign"); seg = 8 if lod == 0 else 6
    for sx in (-1.1, 1.1):
        if lod >= 2: p.box(sx - 0.045, 0.035, 0.0, sx + 0.045, 0.125, 2.5, GAL, skip=("bottom", "top"))
        else: p.tube((sx, 0.08, 0.0), (sx, 0.08, 3.9), 0.045, 0.045, seg, GAL, caps=(True, True))
    p.box(-1.5, 0.0, 2.5, 1.5, 0.06, 3.9, "paint_white", skip=("front",))
    if lod >= 2: return p
    p.box(-1.46, -0.012, 2.54, 1.46, 0.0, 3.86, "sign_green", skip=("back",)); p.box(-1.38, -0.018, 2.62, 1.38, -0.012, 3.78, "sign_green", skip=("back",))
    for z in (2.54, 3.84): p.box(-1.46, -0.016, z - 0.0, 1.46, -0.012, z + 0.02, "paint_white", skip=("back",))
    for x in (-1.46, 1.44): p.box(x, -0.016, 2.54, x + 0.02, -0.012, 3.86, "paint_white", skip=("back",))
    if lod == 0:
        for sx in (-1.1, 1.1):
            for z in (2.7, 3.7): p.box(sx - 0.1, 0.06, z - 0.05, sx + 0.1, 0.14, z + 0.05, GAL)
        for sx in (-1.1, 1.1): p.tube((sx, 0.08, 0.0), (sx, 0.08, 0.4), 0.06, 0.06, 8, GAL, caps=(False, True), smooth=False); p.tube((sx, 0.08, 3.9), (sx, 0.08, 3.94), 0.055, 0.055, 8, GAL)
        p.box(-1.5, 0.06, 2.45, 1.5, 0.1, 2.5, GAL); p.box(-1.5, 0.06, 3.9, 1.5, 0.1, 3.95, GAL)
        p.box(-1.35, -0.02, 2.66, -0.55, -0.018, 2.9, "paint_white", skip=("back",))                                                                       # route shield blank
    return p


# ------------------------------------------------------------------------------------------------------------------------------------------------ gantry and signal
def gantry(lod):
    """Overhead sign gantry spanning a 2 x 3 lane carriageway (20 m between the leg centres plus the legs): two box legs on base plates, a square-section truss (top and bottom chord, web diagonals) whose lower edge clears 5.6 m,
    and two sign panels on the truss front face (the game draws the text on the main panel)."""
    p = Part("gantry"); xs = 12.0; zb = 5.6; zt = 6.4                                                                                                   # 24 m between leg centres: 2 x 3 lanes of 3.5 m plus a median
    for s in (-1, 1):
        p.box(s * xs - 0.3, -0.3, 0.0, s * xs + 0.3, 0.3, zb + 0.1, GAL, skip=("bottom",))
        p.box(s * xs - 0.5, -0.5, 0.0, s * xs + 0.5, 0.5, 0.15, CON, skip=("bottom",))
    n = 20 if lod == 0 else 10
    if lod >= 2:
        p.box(-xs, -0.35, zb, xs, 0.35, zt, GAL); p.box(-10.0, -0.4, zb + 0.1, -2.0, -0.35, 7.0, "sign_green"); return p
    for y in (-0.35, 0.35):
        p.tube((-xs, y, zb), (xs, y, zb), 0.06, 0.06, 4 if lod == 0 else 4, GAL, caps=(True, True)); p.tube((-xs, y, zt), (xs, y, zt), 0.06, 0.06, 4, GAL, caps=(True, True))
    for k in range(n + 1):
        x = -xs + 2 * xs * k / n
        p.tube((x, -0.35, zb), (x, -0.35, zt), 0.03, 0.03, 4, GAL, caps=(True, True))
        if lod == 0 and k < n:
            x2 = -xs + 2 * xs * (k + 1) / n; p.tube((x, -0.35, zb if k % 2 == 0 else zt), (x2, -0.35, zt if k % 2 == 0 else zb), 0.025, 0.025, 4, GAL, caps=(True, True))
    p.tube((-xs, 0.35, zb), (-xs, -0.35, zb), 0.04, 0.04, 4, GAL) ; p.tube((xs, 0.35, zb), (xs, -0.35, zb), 0.04, 0.04, 4, GAL)
    for (a, b) in ((-10.6, -1.6), (1.6, 10.6)):                                                                                                           # sign panels on the front face of the truss
        p.box(a, -0.43, zb + 0.05, b, -0.38, 7.0, "paint_white", skip=("back",)); p.box(a + 0.05, -0.445, zb + 0.1, b - 0.05, -0.43, 6.95, "sign_green", skip=("back",))
        if lod == 0:
            for x in (a + 0.8, (a + b) / 2, b - 0.8): p.box(x - 0.04, -0.38, zb + 0.05, x + 0.04, -0.35, 7.0, GAL)
            p.box(a + 0.15, -0.452, zb + 0.15, a + 0.9, -0.445, zb + 0.9, "paint_white", skip=("back",))
    if lod == 0:
        for s in (-1, 1):
            p.tube((s * xs, 0.0, zb - 0.1), (s * (xs - 1.2), 0.0, zb), 0.04, 0.04, 6, GAL)                                                               # knee brace
            p.box(s * xs - 0.25, 0.3, 0.8, s * xs + 0.25, 0.34, 2.0, BLK)                                                                                # access plate
            for sx in (-0.4, 0.4):
                for sy in (-0.4, 0.4): p.tube((s * xs + sx, sy, 0.15), (s * xs + sx, sy, 0.25), 0.03, 0.03, 6, GAL)
        for x in (-6.0, 6.0): p.box(x - 0.2, -0.3, zt + 0.1, x + 0.2, 0.0, zt + 0.5, BLK); p.box(x - 0.18, -0.34, zt + 0.15, x + 0.18, -0.3, zt + 0.35, "lantern_glow")    # maintenance lights
    return p


def traffic_signal(lod):
    """Signal mast: a 6 m tapered pole with a 4 m arm toward -Y over the road, a three-aspect head at the arm tip and a second head on the pole; lenses are static (red lit, amber and green dark)."""
    p = Part("signal"); seg = 12 if lod == 0 else 8
    if lod >= 2: p.box(-0.1, -0.1, 0.0, 0.1, 0.1, 6.0, GAL, skip=("bottom", "top")); p.box(-0.05, -4.0, 5.4, 0.05, 0.05, 5.6, GAL, skip=("bottom", "top")); p.box(-0.17, -4.05, 4.5, 0.17, -3.75, 5.5, BLK); return p
    p.box(-0.3, -0.3, 0.0, 0.3, 0.3, 0.2, CON, skip=("bottom",))
    p.tube((0, 0, 0.2), (0, 0, 6.0), 0.14, 0.07, seg, GAL, caps=(False, True), smooth=(lod == 0))
    p.tube((0, 0, 5.4), (0, -4.0, 5.55), 0.07, 0.045, 8 if lod == 0 else 4, GAL, caps=(False, True))
    def head(cx, cy, cz, front=-1.0):
        p.box(cx - 0.17, cy - 0.15, cz - 0.5, cx + 0.17, cy + 0.15, cz + 0.5, BLK, skip=("bottom", "top"))
        p.box(cx - 0.17, cy - 0.15, cz + 0.5, cx + 0.17, cy + 0.15, cz + 0.53, BLK); p.box(cx - 0.17, cy - 0.15, cz - 0.53, cx + 0.17, cy + 0.15, cz - 0.5, BLK)
        if lod >= 2: return
        for k, key in enumerate(("signal_red", "signal_amber", "signal_green")):
            z = cz + 0.33 - 0.33 * k; p.tube((cx, cy - 0.15, z), (cx, cy - 0.17, z), 0.1, 0.1, 10 if lod == 0 else 6, key, caps=(True, True), smooth=False)
            if lod == 0: p.box(cx - 0.12, cy - 0.15, z + 0.1, cx + 0.12, cy - 0.28, z + 0.12, BLK)                                                         # hood
        if lod == 0: p.box(cx - 0.25, cy + 0.15, cz - 0.6, cx + 0.25, cy + 0.19, cz + 0.6, "paint_white")                                              # back plate
    head(0.0, -3.9, 5.0)
    head(0.0, -0.3, 3.0)
    if lod == 0: p.tube((0.0, -0.1, 4.6), (0.0, -2.0, 5.4), 0.025, 0.025, 6, GAL)                                                                       # arm stay
    return p


# ------------------------------------------------------------------------------------------------------------------------------------------------ barriers, kerbs, drains
def guardrail(lod):
    """4 m run of W-beam guardrail 0.8 m high: C-section posts with blocks every 2 m, the W-beam (a real zig-zag profile) with a tapered end terminal at the right end. Tile it end to end (the terminal is dropped by tiling the next run)."""
    p = Part("rail"); W = [(0.0, 0.82), (0.03, 0.77), (0.0, 0.72), (0.03, 0.66), (0.0, 0.61), (0.03, 0.55), (0.0, 0.50), (-0.025, 0.50), (-0.025, 0.82)]
    if lod >= 2: p.box(-2.0, -0.03, 0.5, 2.0, 0.04, 0.82, GAL); return p
    prism_x(p, W, -2.0, 1.4 if lod == 0 else 2.0, GAL)
    for x in (-2.0, 0.0):
        p.box(x - 0.045, 0.06, 0.0, x + 0.045, 0.2, 0.9, GAL, skip=("bottom",)); p.box(x - 0.035, 0.03, 0.55, x + 0.035, 0.06, 0.8, BLK)                  # post and spacer block
    if lod == 1: p.box(1.4, -0.025, 0.5, 2.0, 0.0, 0.82, GAL); return p
    p.box(1.9, 0.06, 0.0, 1.99, 0.2, 0.9, GAL, skip=("bottom",))
    prism_y(p, [(1.4, 0.82), (2.0, 0.55), (2.0, 0.27), (1.4, 0.5)], -0.025, 0.0, GAL)                                                                    # tapered end dropping toward the ground
    p.box(1.95, -0.06, 0.2, 2.05, 0.04, 0.6, "warning_yellow")                                                                                          # end marker
    for x in (-1.0, 1.0): p.box(x - 0.012, -0.03, 0.6, x + 0.012, -0.025, 0.72, "paint_white", skip=("bottom",))                                          # reflectors
    p.tube((-2.0, 0.0, 0.0), (-2.0, 0.0, 0.05), 0.02, 0.02, 6, GAL)
    return p


def jersey_barrier(lod):
    """3 m concrete safety barrier module, the usual profile (0.6 m base, sloped faces, 0.3 m top, 0.8 m high) with lifting pockets and reflector slots."""
    p = Part("jersey"); prof = [(-0.3, 0.0), (0.3, 0.0), (0.3, 0.08), (0.225, 0.33), (0.15, 0.8), (-0.15, 0.8), (-0.225, 0.33), (-0.3, 0.08)]
    if lod >= 2: p.box(-1.5, -0.3, 0.0, 1.5, 0.3, 0.8, CON); return p
    prism_x(p, prof, -1.5, 1.5, CON)
    if lod >= 1:
        for x in (-1.2, 1.2): p.box(x - 0.1, -0.31, 0.05, x + 0.1, -0.29, 0.2, CDK)
        return p
    for x in (-1.2, 1.2):
        for s in (-1, 1): p.box(x - 0.1, s * 0.31 - 0.01, 0.05, x + 0.1, s * 0.31 + 0.01, 0.2, CDK, skip=("bottom", "back", "front"))             # lifting pockets
    for x in (-1.0, 0.0, 1.0): p.box(x - 0.06, -0.152, 0.62, x + 0.06, -0.15, 0.7, "warning_yellow", skip=("bottom", "back")); p.box(x - 0.06, 0.15, 0.62, x + 0.06, 0.152, 0.7, "paint_white", skip=("bottom", "front"))    # reflector patches
    p.box(-1.5, -0.3, 0.0, -1.46, 0.3, 0.12, CDK); p.box(1.46, -0.3, 0.0, 1.5, 0.3, 0.12, CDK)                                                             # end plates
    return p


def kerb_painted(lod):
    """2 m run of painted island kerb off-road: four 0.5 m blocks, black and white, 0.25 m wide, 0.15 m high with a chamfered top, on a footing with end plates (kept at about 100 triangles: the row budget is 80)."""
    p = Part("kerb")
    if lod >= 2: p.box(-1.0, -0.125, 0.0, 1.0, 0.125, 0.15, CON, skip=("bottom",)); return p
    for k in range(4): p.frustum(-1.0 + 0.5 * k, -0.125, 0.0, -0.5 + 0.5 * k, 0.125, 0.15, "kerb_black" if k % 2 == 0 else "kerb_white", 0.025, skip=("bottom",))
    if lod == 0:
        for k in range(4): p.box(-1.0 + 0.5 * k, -0.125, 0.0, -0.5 + 0.5 * k, 0.125, 0.015, CDK, skip=("bottom",))                                      # footing
        p.box(-1.0, -0.13, 0.0, -0.99, 0.13, 0.15, CDK, skip=("bottom",)); p.box(0.99, -0.13, 0.0, 1.0, 0.13, 0.15, CDK, skip=("bottom",))
    return p


def drain_set(lod):
    """A drain inlet grate (0.6 x 0.6 m, frame and seven bars over a dark sump) and a round manhole cover (0.7 m) side by side, as thin raised plates (3 cm). Decals in spirit: no collision."""
    p = Part("drain"); seg = 16 if lod == 0 else 8
    if lod >= 2: p.box(-0.85, -0.3, 0.0, 0.35, 0.3, 0.03, CDK); return p
    p.box(-0.85, -0.3, 0.0, -0.25, 0.3, 0.01, CDK, skip=("bottom",))
    p.box(-0.85, -0.3, 0.0, -0.25, -0.26, 0.03, GAL); p.box(-0.85, 0.26, 0.0, -0.25, 0.3, 0.03, GAL)
    p.box(-0.85, -0.3, 0.0, -0.81, 0.3, 0.03, GAL); p.box(-0.29, -0.3, 0.0, -0.25, 0.3, 0.03, GAL)
    for k in range(7 if lod == 0 else 4): x = -0.79 + 0.5 * k / (6 if lod == 0 else 3); p.box(x - 0.012, -0.26, 0.0, x + 0.012, 0.26, 0.028, GAL)
    p.tube((0.0, 0.0, 0.0), (0.0, 0.0, 0.03), 0.35, 0.35, seg, CDK, caps=(False, True), smooth=False)
    if lod == 0:
        p.tube((0.0, 0.0, 0.03), (0.0, 0.0, 0.034), 0.3, 0.3, seg, GAL, caps=(False, True), smooth=False)
        for k in range(3): p.box(-0.2, -0.1 + 0.1 * k, 0.034, 0.2, -0.08 + 0.1 * k, 0.037, CDK)
    return p


# ------------------------------------------------------------------------------------------------------------------------------------------------ bench, bin, planters, utility pole
def bench(lod):
    """1.8 m street bench: two concrete end frames, seven timber seat slats and four back slats on steel rails."""
    p = Part("bench")
    if lod >= 2: p.box(-0.9, -0.3, 0.0, 0.9, 0.3, 0.9, "timber_light"); return p
    for sx in (-0.8, 0.8): p.box(sx - 0.06, -0.28, 0.0, sx + 0.06, 0.28, 0.42, CON, skip=("bottom",)); p.box(sx - 0.05, 0.24, 0.42, sx + 0.05, 0.3, 0.9, CON, skip=("bottom",))
    for k in range(7 if lod == 0 else 3): p.box(-0.9, -0.3 + 0.085 * k, 0.42, 0.9, -0.3 + 0.085 * k + 0.075, 0.46, "timber_light")
    for k in range(4 if lod == 0 else 2): p.box(-0.9, 0.24, 0.52 + 0.1 * k, 0.9, 0.275, 0.6 + 0.1 * k, "timber_light")
    if lod == 0:
        for sx in (-0.8, 0.8):
            p.box(sx - 0.08, -0.3, 0.0, sx + 0.08, -0.22, 0.04, GAL); p.box(sx - 0.08, 0.22, 0.0, sx + 0.08, 0.3, 0.04, GAL)
        p.tube((-0.9, 0.2, 0.5), (0.9, 0.2, 0.5), 0.015, 0.015, 6, GAL); p.tube((-0.9, -0.2, 0.4), (0.9, -0.2, 0.4), 0.015, 0.015, 6, GAL)
        for k in range(4): p.box(-0.9 + 0.6 * k - 0.02, -0.3, 0.38, -0.9 + 0.6 * k + 0.02, 0.3, 0.42, BLK)
    return p


def litter_bin(lod):
    """0.5 m round street litter bin, 0.9 m high: a steel drum with a rim and a hooded opening on a short base."""
    p = Part("bin"); seg = 16 if lod == 0 else 8
    if lod >= 2: p.box(-0.25, -0.25, 0.0, 0.25, 0.25, 0.9, BLK); return p
    p.tube((0, 0, 0.1), (0, 0, 0.8), 0.24, 0.24, seg, BLK, caps=(True, False), smooth=(lod == 0))
    p.tube((0, 0, 0.0), (0, 0, 0.1), 0.2, 0.2, seg, GAL, caps=(True, False), smooth=False)
    p.frustum(-0.25, -0.25, 0.8, 0.25, 0.25, 0.9, GAL, 0.07, skip=("bottom",))
    p.box(-0.14, -0.27, 0.78, 0.14, -0.23, 0.86, CDK)
    if lod == 0:
        p.tube((0, 0, 0.45), (0, 0, 0.5), 0.245, 0.245, seg, GAL, caps=(False, False), smooth=False); p.tube((0, 0, 0.2), (0, 0, 0.25), 0.245, 0.245, seg, GAL, caps=(False, False), smooth=False)
        p.box(-0.12, 0.236, 0.55, 0.12, 0.25, 0.7, "paint_white")
    return p


def _planter(length):
    def build(lod):
        """Kerbed planter box for medians and islands, %.0f m x 2 m x 0.6 m: concrete kerb walls, soil, clipped shrub clusters and flowering shrubs.""" % length
        p = Part("planter"); hx = length / 2
        if lod >= 2: p.box(-hx, -1.0, 0.0, hx, 1.0, 0.6, "hedge_green"); return p
        p.box(-hx, -1.0, 0.0, hx, -0.8, 0.45, CON, skip=("bottom",)); p.box(-hx, 0.8, 0.0, hx, 1.0, 0.45, CON, skip=("bottom",))
        p.box(-hx, -0.8, 0.0, -hx + 0.2, 0.8, 0.45, CON, skip=("bottom",)); p.box(hx - 0.2, -0.8, 0.0, hx, 0.8, 0.45, CON, skip=("bottom",))
        p.box(-hx + 0.2, -0.8, 0.0, hx - 0.2, 0.8, 0.4, "dry_earth", skip=("bottom",))
        n = max(2, int(length / 1.1)) if lod == 0 else max(2, int(length / 2.0))
        for i in range(n):
            x = -hx + 0.8 + (length - 1.6) * i / (n - 1); key = "hedge_green" if i % 2 == 0 else "leaf_green_dark"
            p.blob((x, 0.0, 0.55), (0.5, 0.5, 0.25 if lod else 0.28), key, subdiv=1 if lod == 0 else 0)
            if lod == 0: p.blob((x + 0.25, 0.3, 0.7), (0.18, 0.18, 0.14), "flower_red", subdiv=1)
        if lod == 0:
            p.box(-hx - 0.04, -1.0, 0.45, hx + 0.04, -0.76, 0.5, CON); p.box(-hx - 0.04, 0.76, 0.45, hx + 0.04, 1.0, 0.5, CON)                      # coping
            for x in (-hx + 0.1, hx - 0.1): p.box(x - 0.05, -1.01, 0.25, x + 0.05, -1.0, 0.4, "warning_yellow")
        return p
    return build


def utility_pole(lod):
    """11 m utility pole (concrete, octagonal, tapered): a timber cross-arm with three pin insulators, a pole-mounted transformer on a bracket, two fuse cut-outs, a low-voltage arm with four spools and the cable stubs
    (sagging) running 6 m to each side. Generic, Kano reference gap M-06."""
    p = Part("pole"); seg = 8
    if lod >= 2: p.box(-0.15, -0.15, 0.0, 0.15, 0.15, 11.0, CON, skip=("bottom", "top")); p.box(-1.2, -0.06, 10.2, 1.2, 0.06, 10.32, "timber_light"); return p
    p.box(-0.35, -0.35, 0.0, 0.35, 0.35, 0.2, CON, skip=("bottom",))
    p.tube((0, 0, 0.2), (0, 0, 11.0), 0.2, 0.1, seg, CON, caps=(False, True), smooth=False)
    p.box(-1.2, -0.06, 10.2, 1.2, 0.06, 10.32, "timber_light")
    for x in (-1.0, 0.0, 1.0): p.tube((x, 0, 10.32), (x, 0, 10.58), 0.05, 0.025, 6, "insulator_brown")
    p.box(-0.6, -0.05, 8.9, 0.6, 0.05, 8.98, "timber_light")
    for x in (-0.45, -0.15, 0.15, 0.45): p.tube((x, 0, 8.98), (x, 0, 9.14), 0.04, 0.025, 6, "insulator_brown")
    p.tube((0.3, -0.45, 8.3), (0.3, -0.45, 9.3), 0.28, 0.28, 12 if lod == 0 else 8, "interior_dark", caps=(True, True), smooth=(lod == 0)); p.box(-0.1, -0.3, 8.7, 0.45, -0.15, 8.78, GAL); p.box(-0.1, -0.3, 9.3, 0.45, -0.15, 9.38, GAL)
    for x in (-0.7, 0.7): p.box(x - 0.05, -0.1, 9.9, x + 0.05, 0.0, 10.15, "paint_orange")
    if lod >= 1: return p
    for x in (-1.0, 0.0, 1.0):
        p.path_tube([Vector((x, 0, 10.58)), Vector((x - 3.0 if x < 0 else x + 3.0 if x > 0 else x + 3.0, 0.0, 10.12)), Vector((x - 6.0 if x < 0 else x + 6.0, 0.0, 10.55))], 0.012, 4, "rubber", caps=(True, True), smooth=False)
    p.path_tube([Vector((0.3, -0.45, 8.6)), Vector((0.3, -0.5, 8.0)), Vector((0.0, -0.1, 7.2))], 0.02, 5, "rubber", caps=(True, True))               # transformer lead
    p.tube((0.0, 0.2, 2.0), (0.0, 0.2, 8.0), 0.015, 0.015, 4, GAL); p.box(-0.1, 0.19, 1.8, 0.1, 0.21, 2.1, GAL)                                         # earth down-lead and clamp
    for z in (3.0, 4.2, 5.4): p.box(-0.21, -0.03, z, 0.21, 0.03, z + 0.03, GAL)                                                                         # climbing steps
    return p


# ------------------------------------------------------------------------------------------------------------------------------------------------ catalogue extras
LAMP_ARM = [(0.0, -2.2, 8.9)]
extra("aro_streetlight_arm_single_a", "C", "GLOBAL", REF, heads=LAMP_ARM, preset="lamp", tags=["street_lighting", "residential", "commercial", "industrial"])
extra("aro_streetlight_double_median_a", "C", "GLOBAL", REF, heads=[(-2.55, 0.0, 11.5), (2.55, 0.0, 11.5)], preset="lamp", tags=["street_lighting", "expressway", "median"], intensity=1.15, range=30, poolRadius=6.5, poolShift=1.8)
extra("aro_streetlight_ornamental_a", "C", "GLOBAL", REF, heads=[(0.0, 0.0, 5.4)], preset="lamp_warm", tags=["street_lighting", "plaza", "island"], realLight=False)
for _n, _t in (("stop", "regulatory"), ("yield", "regulatory"), ("speed50", "regulatory"), ("speed80", "regulatory"), ("no_entry", "regulatory"), ("bus_stop", "information"), ("zebra", "warning"), ("no_left_turn", "regulatory")):
    extra(f"aro_road_sign_{_n}_a", "C", "GLOBAL", REF, tags=["road_sign", _t])
extra("aro_road_sign_direction_a", "C", "GLOBAL", REF, label={"c": (0.0, -0.085, 3.2), "width": 2.7, "height": 1.0}, tags=["road_sign", "direction"])
extra("aro_gantry_sign_a", "C", "GLOBAL", REF, label={"c": (-6.1, -0.452, 6.35), "width": 8.4, "height": 1.2}, tags=["road_sign", "gantry", "expressway"])
extra("aro_traffic_signal_a", "C", "GLOBAL", REF, heads=[(0.0, -4.07, 5.33), (0.0, -0.47, 3.33)], preset="signal_r", tags=["traffic_signal", "junction"])
extra("aro_guardrail_post_set", "C", "GLOBAL", REF, tags=["barrier", "tile_4m"])
extra("aro_jersey_barrier_a", "C", "GLOBAL", REF, tags=["barrier", "tile_3m"])
extra("aro_kerb_painted_run_a", "C", "GLOBAL", REF, tags=["kerb", "tile_2m"])
extra("aro_drain_grate_set", "C", "GLOBAL", REF, tags=["drainage"])
extra("aro_bench_a", "C", "GLOBAL", REF, tags=["street_furniture"])
extra("aro_litter_bin_a", "C", "GLOBAL", REF, tags=["street_furniture"])
extra("aro_planter_island_a", "C", "GLOBAL", REF, tags=["planter", "median", "island"])
extra("aro_planter_island_b", "C", "GLOBAL", REF, tags=["planter", "median", "island"])
extra("kano_utility_pole_set", "C", "CITY_SPECIFIC", ["R-021", "R-016", "RB-07", "M-06"], tags=["utility", "residential", "commercial"])
