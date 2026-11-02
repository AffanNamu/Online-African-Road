"""A Hausa-style walled compound (REGIONAL, northern Nigeria), built from the QUALITATIVE features recorded in docs/kano/RESEARCH.md: each compound is walled; the zaure is the entrance room at the front; walls of
sun-dried tubali brick are thick at the base and thinner towards the top; roofs are flat, with HIGH PARAPETS that give a level skyline; zankwaye (pinnacles) are a classic feature; decorative relief motifs; buildings
of more than two storeys are rare. NO dimensions of a Kano compound were found, so every proportion below is a DESIGN CHOICE, not a measurement, and no real Kano compound, the palace or any landmark is
depicted: this asset must be checked against real photographs and drawings (reference gap M-01) before it can be approved. Front is -Y; the origin is the centre of the footprint on the ground."""
from __future__ import annotations
import math
from mathutils import Matrix
from kano_kit.geo import Part

PL, MB, ER, DK, WD, RF = "plaster_weathered", "mud_brick", "earth_compacted", "interior_dark", "wood_door", "mud_crest"


def _zank(p, x, y, z, h=0.55, r0=0.19, r1=0.045):
    """A zankwaye: a tapering pinnacle on a small square base."""
    p.box(x - 0.17, y - 0.17, z, x + 0.17, y + 0.17, z + 0.12, PL); p.tube((x, y, z + 0.12), (x, y, z + 0.12 + h), r0, r1, 6, PL, caps=(False, True), smooth=False)


def _diamond(p, x, y, z, s=0.11):
    """A raised diamond of relief on a wall facing -Y."""
    m0 = p.mark(); p.box(-s, 0, -s, s, 0.05, s, PL); p.xform(m0, Matrix.Translation((x, y - 0.05, z)) @ Matrix.Rotation(math.radians(45), 4, "Y"))


def _flat_roof(p, x0, y0, x1, y1, z, parapet_h=0.5, zank=True):
    """A flat roof slab with a high parapet on all four sides and zankwaye at the corners and mid-lengths."""
    p.box(x0, y0, z, x1, y1, z + 0.12, RF)
    t = 0.22
    p.box(x0, y0, z + 0.12, x1, y0 + t, z + 0.12 + parapet_h, PL); p.box(x0, y1 - t, z + 0.12, x1, y1, z + 0.12 + parapet_h, PL)
    p.box(x0, y0 + t, z + 0.12, x0 + t, y1 - t, z + 0.12 + parapet_h, PL); p.box(x1 - t, y0 + t, z + 0.12, x1, y1 - t, z + 0.12 + parapet_h, PL)
    if zank:
        top = z + 0.12 + parapet_h; xs = [x0 + t, (x0 + x1) / 2, x1 - t]; ys = [y0 + t, y1 - t]
        for xx in xs:
            for yy in ys: _zank(p, xx, yy, top)
        for yy in ((y0 + y1) / 2,):
            _zank(p, x0 + t, yy, top); _zank(p, x1 - t, yy, top)


def hausa_compound_a(lod: int = 0) -> Part:
    """A 20 x 16 m compound: perimeter wall with a parapet and zankwaye, a two-level zaure entrance building with an arched doorway and relief motifs, two flat-roofed rooms along the walls and an earth courtyard.
    Proportions are design choices (see the module note)."""
    p = Part("compound"); X = 10.0; Y = 8.0; t = 0.45; H = 3.0
    if lod >= 1:
        for (a, b, c, d) in ((-X, -Y, -3, -Y + t), (3, -Y, X, -Y + t), (-X, -Y, -X + t, Y), (X - t, -Y, X, Y), (-X, Y - t, X, Y)): p.box(a, b, 0.0, c, d, H + 0.5, PL)
        p.box(-3, -Y, 0.0, 3, -3.5, 3.4, PL); p.box(-2, -7.4, 3.4, 2, -4.4, 5.8, PL); p.box(-9.55, -2.0, 0.0, -5.0, 6.5, 2.9, PL); p.box(-4.0, 4.2, 0.0, 9.5, 7.55, 2.7, PL)
        return p
    p.box(-X + t, -Y + t, 0.0, X - t, Y - t, 0.05, ER)                                       # trodden earth courtyard
    # perimeter wall: thick at the base (a proud plinth band), thinner above, parapet lip and zankwaye every 2.5 m
    def wall(x0, y0, x1, y1, sx, sy):
        p.box(x0, y0, 0.0, x1, y1, 0.55, MB); p.box(x0 + (0.03 if sx else 0), y0 + (0.03 if sy else 0), 0.55, x1 - (0.03 if sx else 0), y1 - (0.03 if sy else 0), H, PL)
    wall(-X, -Y, -3.0, -Y + t, 0, 1); wall(3.0, -Y, X, -Y + t, 0, 1); wall(-X, -Y, -X + t, Y, 1, 0); wall(X - t, -Y, X, Y, 1, 0); wall(-X, Y - t, X, Y, 0, 1)
    p.box(-X, -Y - 0.02, H, -3.0, -Y + 0.2, H + 0.45, PL); p.box(3.0, -Y - 0.02, H, X, -Y + 0.2, H + 0.45, PL); p.box(-X - 0.02, -Y, H, -X + 0.2, Y, H + 0.45, PL); p.box(X - 0.2, -Y, H, X + 0.02, Y, H + 0.45, PL); p.box(-X, Y - 0.2, H, X, Y + 0.02, H + 0.45, PL)
    for k in range(9):
        x = -X + 0.3 + k * 2.5
        if abs(x) > 3.6 and x < X - 0.2: _zank(p, x, -Y + 0.1, H + 0.45); _zank(p, x, Y - 0.1, H + 0.45)
    for k in range(1, 6): _zank(p, -X + 0.1, -Y + k * 2.5 + 0.0, H + 0.45); _zank(p, X - 0.1, -Y + k * 2.5, H + 0.45)
    # zaure: the entrance building. Front wall of piers, arch and band; passage through it; flat roof with parapet; a small upper room
    zx0, zx1, zy0, zy1, zh = -3.0, 3.0, -Y, -3.5, 3.4; r = 0.95; rc = 1.65; ro = 1.25
    p.box(zx0, zy0, 0.0, -r, zy0 + t, zh, PL); p.box(r, zy0, 0.0, zx1, zy0 + t, zh, PL)
    p.box(zx0, zy0 + t, 0.0, zx0 + t, zy1, zh, PL); p.box(zx1 - t, zy0 + t, 0.0, zx1, zy1, zh, PL); p.box(zx0, zy1 - t, 0.0, -r, zy1, zh, PL); p.box(r, zy1 - t, 0.0, zx1, zy1 - t + t, zh, PL)
    n = 12
    for k in range(n):                                                                         # voussoirs: wedge blocks around the arch
        a0 = math.pi * k / n; a1 = math.pi * (k + 1) / n
        pa = [(r * math.cos(a0), rc + r * math.sin(a0)), (r * math.cos(a1), rc + r * math.sin(a1)), (ro * math.cos(a1), rc + ro * math.sin(a1)), (ro * math.cos(a0), rc + ro * math.sin(a0))]
        for (ya, yb) in ((zy0 - 0.06, zy0 + t),):
            pts_f = [(x, ya, z) for x, z in pa]; pts_b = [(x, yb, z) for x, z in pa]
            p.face_n(pts_f, PL, (0, -1, 0)); p.face_n(pts_b, PL, (0, 1, 0))
            mid = ((pa[0][0] + pa[1][0]) / 2, (pa[0][1] + pa[1][1]) / 2); p.face_n([pts_f[0], pts_f[1], pts_b[1], pts_b[0]], PL, (-mid[0], 0, -(mid[1] - rc)))      # the intrados faces the doorway's centre; the radial joints and the outer face are hidden in the wall
    p.box(-ro, zy0, rc + ro, ro, zy0 + t, zh, PL)                                              # wall above the arch
    p.box(-r, zy0 + t - 0.02, 0.0, r, zy0 + t, rc, DK, skip=("bottom", "back", "left", "right", "top"))      # the dark passage seen through the doorway
    p.box(-r, zy0 + t, 0.0, r, zy1, 0.04, ER)                                                  # floor of the passage
    # relief motifs on the front wall: a band of diamonds and two plain courses
    for k in range(7): _diamond(p, -2.4 + k * 0.8 if abs(-2.4 + k * 0.8) > 1.3 else 2.4, zy0 - 0.0, 2.9)
    p.box(zx0, zy0 - 0.04, 3.1, zx1, zy0 + 0.02, 3.2, PL); p.box(zx0, zy0 - 0.04, 0.55, zx1, zy0 + 0.02, 0.62, PL)
    _flat_roof(p, zx0, zy0, zx1, zy1, zh)
    ux0, ux1, uy0, uy1, uz = -2.0, 2.0, -7.45, -4.45, zh + 0.12
    p.box(ux0, uy0, uz, ux1, uy1, uz + 2.4, PL)                                                # upper room
    for x in (-1.0, 1.0): p.box(x - 0.28, uy0 - 0.02, uz + 0.9, x + 0.28, uy0 + 0.04, uz + 1.7, DK); p.box(x - 0.34, uy0 - 0.03, uz + 1.7, x + 0.34, uy0 + 0.04, uz + 1.78, PL)
    _flat_roof(p, ux0, uy0, ux1, uy1, uz + 2.4)
    # two flat-roofed rooms: along the left wall and along the back wall, each with a door and a window facing the courtyard
    rx0, rx1, ry0, ry1, rh = -X + t, -5.0, -2.0, 6.5, 2.9
    p.box(rx0, ry0, 0.0, rx1, ry1, rh, PL)
    for y in (0.5, 4.0): p.box(rx1 - 0.04, y - 0.5, 0.0, rx1 + 0.02, y + 0.5, 2.0, DK); p.box(rx1 - 0.05, y - 0.58, 2.0, rx1 + 0.03, y + 0.58, 2.1, PL)
    p.box(rx1 - 0.04, 2.2, 1.0, rx1 + 0.02, 2.8, 1.8, DK)
    _flat_roof(p, rx0, ry0, rx1, ry1, rh)
    bx0, bx1, by0, by1, bh = -4.0, 9.5, 4.2, Y - t, 2.7
    p.box(bx0, by0, 0.0, bx1, by1, bh, PL)
    for x in (-2.5, 1.0, 6.0): p.box(x - 0.5, by0 - 0.04, 0.0, x + 0.5, by0 + 0.02, 2.0, DK); p.box(x - 0.58, by0 - 0.05, 2.0, x + 0.58, by0 + 0.03, 2.1, PL)
    for x in (-0.8, 3.6): p.box(x - 0.3, by0 - 0.04, 1.0, x + 0.3, by0 + 0.02, 1.8, DK)
    _flat_roof(p, bx0, by0, bx1, by1, bh)
    return p
