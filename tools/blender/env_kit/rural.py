"""Rural houses of the environmental kit, batch E3 (REGIONAL, northern Nigerian village; vernacular NOT verified against real references, gap M-14): a longer mud-brick house with a lean-to veranda on timber
posts (R-020, R-021) and a plastered block house with a hipped corrugated roof and barred windows (R-016, R-018, R-021). Real geometry: wall pieces around every opening, lintels, bars, roof sheets with fascia and
rafter tails. Front is -Y; the origin is the centre of the footprint on the ground."""
from __future__ import annotations
import math
from mathutils import Vector
from kano_kit.geo import Part
from .buildings import _gable, _up_normal

T, ROOF, M, PW = "timber_weathered", "roof_zinc_rust", "mud_brick", "plaster_weathered"


def _sheet(p, pts, key, thick=0.04):
    a, b, c = (Vector(x) for x in pts[:3]); n = (b - a).cross(c - a); n = n if n.z >= 0 else -n
    p.face_n(pts, key, n); p.face_n([(x, y, z - thick) for x, y, z in pts], key, -n)


def mudbrick_house_b(lod: int = 0) -> Part:
    """A mud-brick house 10 x 5 m on plan, 2.7 m to the eaves, gable roof of rusted corrugated sheet, two plank doors and a shuttered window on the front under a lean-to veranda on five timber posts, a window on
    each side. Walls are 0.35 m thick; every opening is framed by wall pieces and a lintel."""
    p = Part("house"); W = 10.0; D = 5.0; H = 2.7; R = 3.9; t = 0.35; ov = 0.5; x0, x1, y0, y1 = -W / 2, W / 2, -D / 2, D / 2
    p.box(x0 - 0.1, y0 - 0.1, 0.0, x1 + 0.1, y1 + 0.1, 0.30, "mud_crest", skip=("bottom",))
    if lod >= 1:
        p.box(x0, y0, 0.3, x1, y1, H, M); ze = H - ov * (R - H) / (D / 2)
        for sgn in (-1, 1): _sheet(p, [(x0 - 0.4, sgn * (D / 2 + ov), ze), (x1 + 0.4, sgn * (D / 2 + ov), ze), (x1 + 0.4, 0.0, R), (x0 - 0.4, 0.0, R)], ROOF)
        return p
    # front wall around two doors (x -3.6..-2.6 and 2.6..3.6, to 2.0 m) and a window (x -0.6..0.6, 1.0..1.9 m)
    for (a, b) in ((x0, -3.6), (-2.6, -0.6), (0.6, 2.6), (3.6, x1)): p.box(a, y0, 0.3, b, y0 + t, H, M)
    for (a, b) in ((-3.6, -2.6), (2.6, 3.6)): p.box(a, y0, 2.0, b, y0 + t, H, M)
    p.box(-0.6, y0, 0.3, 0.6, y0 + t, 1.0, M); p.box(-0.6, y0, 1.9, 0.6, y0 + t, H, M)
    # back wall, side walls (a window in each: 1.0 .. 1.8 m, 0.8 m wide)
    p.box(x0, y1 - t, 0.3, x1, y1, H, M)
    for xs0, xs1 in ((x0, x0 + t), (x1 - t, x1)):
        p.box(xs0, y0 + t, 0.3, xs1, -0.4, H, M); p.box(xs0, 0.4, 0.3, xs1, y1 - t, H, M); p.box(xs0, -0.4, 0.3, xs1, 0.4, 1.0, M); p.box(xs0, -0.4, 1.8, xs1, 0.4, H, M)
        p.box(xs0 - 0.02, -0.4, 1.0, xs1 + 0.02, 0.4, 1.8, "interior_dark", skip=("bottom", "top", "front", "back"))
    ze = H - ov * (R - H) / (D / 2)
    _gable(p, x0, x0 + t, y0, y1, H, H, R, M); _gable(p, x1 - t, x1, y0, y1, H, H, R, M)
    # doors, window shutters, lintels
    for cx in (-3.1, 3.1): p.box(cx - 0.45, y0 + 0.05, 0.3, cx + 0.45, y0 + 0.12, 1.98, "wood_door"); p.box(cx - 0.55, y0 - 0.02, 2.0, cx + 0.55, y0 + t, 2.08, T)
    p.box(-0.40, y0 - 0.02, 0.98, 0.40, y0 + t, 1.06, T); p.box(-0.40, y0 - 0.02, 1.9, 0.40, y0 + t, 1.98, T); p.box(-0.33, y0 + 0.12, 1.06, 0.33, y0 + 0.16, 1.9, "interior_dark")
    p.box(0.36, y0 - 0.52, 1.06, 0.43, y0 + 0.02, 1.9, T)                                              # one shutter open at a right angle
    # roof: two corrugated planes with overhang, fascia and rafter tails
    for sgn in (-1, 1):
        ya = sgn * (D / 2 + ov); q = [(x0 - 0.4, ya, ze), (x1 + 0.4, ya, ze), (x1 + 0.4, 0.0, R), (x0 - 0.4, 0.0, R)]; _sheet(p, q, ROOF)
        p.box(x0 - 0.4, ya - 0.02, ze - 0.10, x1 + 0.4, ya + 0.02, ze, T)
        for k in range(12): xx = x0 - 0.3 + k * (W + 0.6) / 11; p.box(xx, ya * 0.94 - 0.03, ze - 0.12, xx + 0.07, ya * 0.94 + 0.03, ze - 0.04, T)
    p.box(x0 - 0.42, -0.06, R - 0.04, x1 + 0.42, 0.06, R + 0.06, "metal_galvanised")
    # lean-to veranda along the front: a mono-pitch sheet under the main eave on five posts, with a trodden earth floor
    p.box(x0, y0 - 1.9, 0.0, x1, y0, 0.10, "earth_compacted", skip=("bottom",))
    vq = [(x0 - 0.3, y0 - 1.95, 2.18), (x1 + 0.3, y0 - 1.95, 2.18), (x1 + 0.3, y0 - ov - 0.05, ze - 0.12), (x0 - 0.3, y0 - ov - 0.05, ze - 0.12)]; _sheet(p, vq, ROOF)
    p.box(x0 - 0.3, y0 - 1.97, 2.08, x1 + 0.3, y0 - 1.93, 2.20, T)
    for k in range(5): xx = x0 + 0.4 + k * (W - 0.8) / 4; p.tube((xx, y0 - 1.85, 0.10), (xx + 0.015, y0 - 1.85, 2.15), 0.055, 0.045, 6, T, caps=(False, True), smooth=False)
    p.tube((x0 + 0.4, y0 - 1.85, 2.12), (x1 - 0.4, y0 - 1.85, 2.12), 0.04, 0.04, 4, T, caps=(False, False), smooth=False)
    return p


def block_house_corrugated(lod: int = 0) -> Part:
    """A plastered block house 9 x 6 m on plan, 2.8 m to the eaves, a hipped corrugated roof (ridge 3.0 m long, 4.2 m high) with fascia and rafter tails, a plank door under a small concrete step and two windows
    with steel burglar bars (vertical rods and two cross rails) on the front, a window on each side, a darker concrete plinth band."""
    p = Part("house"); W = 9.0; D = 6.0; H = 2.8; R = 4.2; t = 0.25; ov = 0.6; x0, x1, y0, y1 = -W / 2, W / 2, -D / 2, D / 2; ex, ey = W / 2 + ov, D / 2 + ov; rl = 1.5
    ze = H - ov * (R - H) / (D / 2)
    p.box(x0 - 0.05, y0 - 0.05, 0.0, x1 + 0.05, y1 + 0.05, 0.35, "concrete", skip=("bottom",))
    if lod >= 1:
        p.box(x0, y0, 0.35, x1, y1, H, PW)
        for pts in ([(-ex, -ey, ze), (ex, -ey, ze), (rl, 0, R), (-rl, 0, R)], [(ex, ey, ze), (-ex, ey, ze), (-rl, 0, R), (rl, 0, R)], [(ex, -ey, ze), (ex, ey, ze), (rl, 0, R)], [(-ex, ey, ze), (-ex, -ey, ze), (-rl, 0, R)]): _sheet(p, pts, ROOF)
        return p
    # front wall (y0): door x -0.5..0.5 (to 2.1 m); windows at x -3.2..-1.9 and 1.9..3.2 (1.05 .. 1.95 m)
    for (a, b) in ((x0, -3.2), (-1.9, -0.5), (0.5, 1.9), (3.2, x1)): p.box(a, y0, 0.35, b, y0 + t, H, PW)
    p.box(-0.5, y0, 2.1, 0.5, y0 + t, H, PW)
    for (a, b) in ((-3.2, -1.9), (1.9, 3.2)): p.box(a, y0, 0.35, b, y0 + t, 1.05, PW); p.box(a, y0, 1.95, b, y0 + t, H, PW)
    # back wall and side walls with a window each (y -0.65..0.65, 1.05 .. 1.95)
    p.box(x0, y1 - t, 0.35, x1, y1, H, PW)
    for xs0, xs1 in ((x0, x0 + t), (x1 - t, x1)):
        p.box(xs0, y0 + t, 0.35, xs1, -0.65, H, PW); p.box(xs0, 0.65, 0.35, xs1, y1 - t, H, PW); p.box(xs0, -0.65, 0.35, xs1, 0.65, 1.05, PW); p.box(xs0, -0.65, 1.95, xs1, 0.65, H, PW)
        p.box(xs0 - 0.01, -0.65, 1.05, xs1 + 0.01, 0.65, 1.95, "interior_dark", skip=("bottom", "top", "front", "back"))
        for k in range(8): yy = -0.6 + k * 1.2 / 7; p.tube((xs0 - 0.03 if xs0 < 0 else xs1 + 0.03, yy, 1.05), (xs0 - 0.03 if xs0 < 0 else xs1 + 0.03, yy, 1.95), 0.008, 0.008, 4, "metal_gate_black", caps=(False, False), smooth=False)
    # door, windows with bars, step
    p.box(-0.45, y0 + 0.04, 0.35, 0.45, y0 + 0.10, 2.08, "wood_door"); p.box(-0.55, y0 - 0.02, 2.08, 0.55, y0 + t, 2.16, "concrete"); p.box(-0.9, y0 - 0.55, 0.0, 0.9, y0, 0.22, "concrete")
    for cx in (-2.55, 2.55):
        p.box(cx - 0.65, y0 + 0.06, 1.05, cx + 0.65, y0 + 0.10, 1.95, "interior_dark")
        p.box(cx - 0.72, y0 - 0.03, 1.0, cx + 0.72, y0 + t, 1.06, "concrete"); p.box(cx - 0.72, y0 - 0.03, 1.94, cx + 0.72, y0 + t, 2.0, "concrete")                  # sill and lintel
        for k in range(9): xx = cx - 0.60 + k * 1.2 / 8; p.tube((xx, y0 - 0.025, 1.06), (xx, y0 - 0.025, 1.94), 0.009, 0.009, 4, "metal_gate_black", caps=(False, False), smooth=False)
        for z in (1.35, 1.65): p.box(cx - 0.65, y0 - 0.04, z - 0.01, cx + 0.65, y0 - 0.01, z + 0.01, "metal_gate_black")
    # hipped roof: four planes, fascia on the long eaves, rafter tails
    for pts in ([(-ex, -ey, ze), (ex, -ey, ze), (rl, 0, R), (-rl, 0, R)], [(ex, ey, ze), (-ex, ey, ze), (-rl, 0, R), (rl, 0, R)], [(ex, -ey, ze), (ex, ey, ze), (rl, 0, R)], [(-ex, ey, ze), (-ex, -ey, ze), (-rl, 0, R)]): _sheet(p, pts, ROOF)
    for sgn in (-1, 1):
        p.box(-ex, sgn * ey - 0.02, ze - 0.10, ex, sgn * ey + 0.02, ze, T)
        for k in range(12): xx = -ex + 0.1 + k * (2 * ex - 0.3) / 11; p.box(xx, sgn * ey * 0.95 - 0.03, ze - 0.12, xx + 0.07, sgn * ey * 0.95 + 0.03, ze - 0.04, T)
    for sgn in (-1, 1): p.box(sgn * ex - 0.02, -ey, ze - 0.10, sgn * ex + 0.02, ey, ze, T)
    p.box(-rl - 0.02, -0.06, R - 0.04, rl + 0.02, 0.06, R + 0.06, "metal_galvanised")
    return p
