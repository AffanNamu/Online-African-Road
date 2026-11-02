"""Roadside and village buildings of the environmental kit (REGIONAL: northern Nigerian vernacular; NOT verified against real references, gap M-14): a row of three single-storey shops with a deep awning
and roller shutters in different states (R-016 to R-019), and a mud-brick house with a rusted corrugated roof (R-020, R-021). Real geometry: wall pieces around every opening, lintels, shutters slat by slat,
roof sheets with fascia, posts and plinths. Front is -Y; the origin is the centre of the footprint on the ground."""
from __future__ import annotations
import math, random
from mathutils import Vector
from kano_kit.geo import Part

PW, T, ROOF, CONC = "plaster_weathered", "timber_weathered", "roof_zinc_rust", "concrete"


def _gable(p, x0, x1, y0, y1, z0, ze, zr, key):
    """The triangular end of a gabled roof, wall thickness x0..x1, spanning y0..y1 with the ridge at the middle (zr) and the eaves at ze; z0 is the bottom (equal to ze when the wall below is built separately)."""
    ym = (y0 + y1) / 2
    for xx, nx in ((x0, -1), (x1, 1)):
        poly = [(xx, y0, ze), (xx, y1, ze), (xx, ym, zr)] if ze <= z0 + 1e-6 else [(xx, y0, z0), (xx, y1, z0), (xx, y1, ze), (xx, ym, zr), (xx, y0, ze)]
        p.face_n(poly, key, (nx, 0, 0))
    for (ya, za, yb, zb) in ((y0, ze, ym, zr), (ym, zr, y1, ze)): p.face_n([(x0, ya, za), (x1, ya, za), (x1, yb, zb), (x0, yb, zb)], key, _up_normal(ya, za, yb, zb))
    if ze > z0 + 1e-6: p.face_n([(x0, y0, z0), (x1, y0, z0), (x1, y0, ze), (x0, y0, ze)], key, (0, -1, 0)); p.face_n([(x0, y1, z0), (x1, y1, z0), (x1, y1, ze), (x0, y1, ze)], key, (0, 1, 0))


def _up_normal(ya, za, yb, zb):
    d = Vector((0, yb - ya, zb - za)); n = Vector((0, -d.z, d.y)); return tuple(n if n.z >= 0 else -n)


def shop_row_roadside(lod: int = 0) -> Part:
    """Three shop units, 15 m x 6.5 m: plinth and awning in front, a front wall of piers, lintel and sign band, side and back walls, one corrugated mono-pitch roof. Unit 1 has its roller shutter down, unit 2
    half raised, unit 3 fully raised on its drum; the sign bands are blank panels (their text is a data slot, RB-07)."""
    p = Part("shops"); yf = -1.75; yb = 3.25; zf = 0.25
    p.box(-7.7, -3.25, 0.0, 7.7, yf, zf, CONC)                                              # pavement plinth (also the shop floor level in front)
    if lod >= 1:
        p.box(-7.5, yf, zf, 7.5, yb, 3.1, PW); p.face([(-7.9, -3.25, 3.3), (7.9, -3.25, 3.3), (7.9, 3.5, 2.8), (-7.9, 3.5, 2.8)], ROOF); p.face([(-7.9, 3.5, 2.8), (7.9, 3.5, 2.8), (7.9, -3.25, 3.3), (-7.9, -3.25, 3.3)], ROOF)
        return p
    piers = [(-7.5, -7.1), (-2.9, -2.1), (2.1, 2.9), (7.1, 7.5)]
    for (a, b) in piers: p.box(a, yf, zf, b, yf + 0.25, 3.4, PW)
    p.box(-7.5, yf, 2.6, 7.5, yf + 0.25, 3.4, PW)                                           # lintel and sign band over the three openings
    for c in (-5.0, 0.0, 5.0):
        p.box(c - 2.05, yf - 0.03, 2.66, c + 2.05, yf, 3.34, "plaster_white")               # blank sign panel
        for (xa, xb) in ((c - 2.1, c - 2.05), (c + 2.05, c + 2.1)): p.box(xa, yf - 0.04, 2.62, xb, yf, 3.38, "paint_red")
        p.box(c - 2.1, yf - 0.04, 2.62, c + 2.1, yf, 2.66, "paint_red"); p.box(c - 2.1, yf - 0.04, 3.34, c + 2.1, yf, 3.38, "paint_red")   # a painted border
        p.box(c - 2.1, yf + 0.25, zf, c + 2.1, yf + 0.55, 0.30, CONC)                       # threshold
    # roller shutters, slat by slat (0.105 m each)
    def slats(c, z0, z1, y):
        z = z0
        while z + 0.1 <= z1 + 1e-6: p.box(c - 2.05, y, z, c + 2.05, y + 0.05, z + 0.095, "shutter_steel", skip=("bottom", "back", "left", "right")); z += 0.105
    slats(-5.0, zf + 0.05, 2.55, yf + 0.12)                                                 # unit 1: down
    slats(0.0, 1.50, 2.55, yf + 0.12)                                                       # unit 2: half raised
    p.box(-2.05, yf + 0.10, 2.45, 2.05, yf + 0.30, 2.60, "shutter_steel")                   # roller drum above unit 2 and 3
    p.box(2.95, yf + 0.10, 2.45, 7.05, yf + 0.30, 2.60, "shutter_steel")
    for c in (-5.0, 0.0, 5.0):                                                              # the dark room behind each opening
        p.box(c - 2.1, yf + 0.25, zf, c + 2.1, yf + 0.30, 2.6, "interior_dark", skip=("bottom", "top", "left", "right", "back"))
    for c in (0.0, 5.0):                                                                    # shelves and goods seen through the open shutters
        p.box(c - 2.0, yf + 1.2, zf, c + 2.0, yf + 1.3, 2.4, "interior_dark")
        for k in range(4): p.box(c - 1.95, yf + 0.95, 0.65 + k * 0.5, c + 1.95, yf + 1.2, 0.69 + k * 0.5, T)
        rnd = random.Random(int(c) + 9)
        for k in range(10): p.box(c - 1.8 + k * 0.36, yf + 1.0, 0.69 + (k % 4) * 0.5, c - 1.8 + k * 0.36 + 0.26, yf + 1.15, 0.69 + (k % 4) * 0.5 + 0.22 * rnd.uniform(0.6, 1.0), rnd.choice(("paint_red", "paint_white", "plaster_ochre", "sign_blue")))
    # side and back walls, party walls
    p.box(-7.5, yf + 0.25, zf, -7.1, yb, 3.1, PW); p.box(7.1, yf + 0.25, zf, 7.5, yb, 3.1, PW); p.box(-7.5, yb - 0.25, zf, 7.5, yb, 3.1, PW)
    for x in (-2.5, 2.5): p.box(x - 0.1, yf + 0.25, zf, x + 0.1, yb, 3.0, PW)
    p.box(-7.5, yf + 0.25, 0.0, 7.5, yb, zf, CONC)                                          # shop floor slab
    # corrugated mono-pitch roof with fascia, on posts at the awning edge
    z_front, z_back = 3.25, 2.75; y_front, y_back = -3.25, 3.50
    q = [(-7.9, y_front, z_front), (7.9, y_front, z_front), (7.9, y_back, z_back), (-7.9, y_back, z_back)]
    p.face(q, ROOF, smooth=False); p.face(list(reversed([(x, y, z - 0.05) for x, y, z in q])), ROOF)
    for (x0, y0, z0, x1, y1, z1) in ((-7.9, y_front, z_front - 0.05, 7.9, y_front + 0.04, z_front + 0.01), (-7.9, y_back - 0.04, z_back - 0.05, 7.9, y_back, z_back + 0.01), (-7.9, y_front, z_front - 0.05, -7.86, y_back, z_back + 0.01), (7.86, y_front, z_front - 0.05, 7.9, y_back, z_back + 0.01)): p.box(x0, y0, z0, x1, y1, z1, T)
    for x in (-7.2, -2.5, 2.5, 7.2): p.tube((x, -3.1, zf), (x, -3.1, z_front - 0.05), 0.045, 0.045, 8, "shutter_steel", caps=(False, True))          # awning posts
    p.box(-7.9, y_front - 0.02, z_front - 0.22, 7.9, y_front + 0.02, z_front - 0.05, "metal_galvanised")                                            # rain gutter
    # hanging goods under the awning: basins and buckets on a rail
    p.tube((-6.9, -3.0, 2.55), (6.9, -3.0, 2.55), 0.012, 0.012, 4, "metal_galvanised", caps=(False, False), smooth=False)
    rnd = random.Random(4)
    for k in range(9):
        x = -6.2 + k * 1.5 + rnd.uniform(-0.2, 0.2); p.tube((x, -3.0, 2.55), (x, -3.0, 2.30), 0.006, 0.006, 4, "metal_galvanised", caps=(False, False), smooth=False)
        p.tube((x, -3.0, 2.30), (x, -3.0, 2.05), 0.16, 0.20, 8, rnd.choice(("paint_red", "sign_blue", "metal_galvanised")), caps=(False, True), smooth=False)
    return p


def mudbrick_house(lod: int = 0) -> Part:
    """A mud-brick house, 8 x 6 m on plan, 2.9 m to the eaves, gable roof of rusted corrugated sheet with a 0.7 m overhang and timber fascia, a plank door in the middle and a shuttered window each side (one
    shutter open). Walls are 0.35 m thick; every opening is framed by wall pieces and a lintel."""
    p = Part("house"); W = 8.0; D = 6.0; H = 2.9; t = 0.35; y0 = -D / 2; y1 = D / 2; x0 = -W / 2; x1 = W / 2; R = 4.4; ov = 0.7; M = "mud_brick"
    p.box(x0 - 0.1, y0 - 0.1, 0.0, x1 + 0.1, y1 + 0.1, 0.30, "mud_crest", skip=("bottom",))      # eroded plinth, slightly proud
    p.box(-1.2, y0 - 1.2, 0.0, 1.2, y0 - 0.1, 0.12, "earth_compacted")                         # trodden threshold apron
    if lod >= 1:
        p.box(x0, y0, 0.3, x1, y1, H, M); zr = R; ze = H - ov * (R - H) / (D / 2)
        for sgn in (-1, 1):
            ya = sgn * (D / 2 + ov); n = _up_normal(*((ya, ze, 0.0, zr) if sgn < 0 else (0.0, zr, ya, ze)))
            p.face_n([(x0 - 0.4, ya, ze), (x1 + 0.4, ya, ze), (x1 + 0.4, 0.0, zr), (x0 - 0.4, 0.0, zr)], ROOF, n)
        return p
    # front wall (y0 side) around the door and two windows
    def wall_y(ya, yb):
        pieces = [(x0, -2.75), (-2.05, -0.5), (0.5, 2.05), (2.75, x1)]
        for (a, b) in pieces: p.box(a, ya, 0.3, b, yb, H, M)
        for (a, b) in ((-2.75, -2.05), (2.05, 2.75)): p.box(a, ya, 0.3, b, yb, 1.1, M); p.box(a, ya, 2.0, b, yb, H, M)       # below and above the windows
        p.box(-0.5, ya, 2.05, 0.5, yb, H, M)                                                 # over the door
    wall_y(y0, y0 + t)
    # back wall, side walls
    p.box(x0, y1 - t, 0.3, x1, y1, H, M); p.box(x0, y0 + t, 0.3, x0 + t, y1 - t, H, M); p.box(x1 - t, y0 + t, 0.3, x1, y1 - t, H, M)
    # gable ends (mud brick up to the ridge)
    ze = H - ov * (R - H) / (D / 2)
    _gable(p, x0, x0 + t, y0, y1, H, H, R, M); _gable(p, x1 - t, x1, y0, y1, H, H, R, M)
    # door and window furniture
    p.box(-0.45, y0 + 0.05, 0.3, 0.45, y0 + 0.12, 2.0, "wood_door"); p.box(-0.55, y0 - 0.02, 2.0, 0.55, y0 + t, 2.08, T)
    for sx in (-1, 1):
        cx = sx * 2.4; p.box(cx - 0.40, y0 - 0.02, 1.02, cx + 0.40, y0 + t, 1.10, T); p.box(cx - 0.40, y0 - 0.02, 2.0, cx + 0.40, y0 + t, 2.08, T)    # sill and lintel
        p.box(cx - 0.33, y0 + 0.12, 1.1, cx + 0.33, y0 + 0.16, 2.0, "interior_dark")
    p.box(-2.75, y0 - 0.02, 1.1, -2.05, y0 + 0.05, 2.0, T)                                   # left shutter, closed
    p.box(2.05, y0 - 0.62, 1.1, 2.12, y0 + 0.0, 2.0, T)                                      # right shutter, open at a right angle
    # roof: two corrugated planes with overhang, thickness 0.03, fascia and rafter tails
    for sgn in (-1, 1):
        ya = sgn * (D / 2 + ov); q = [(x0 - 0.4, ya, ze), (x1 + 0.4, ya, ze), (x1 + 0.4, 0.0, R), (x0 - 0.4, 0.0, R)]
        n = _up_normal(*((ya, ze, 0.0, R) if sgn < 0 else (0.0, R, ya, ze))); p.face_n(q, ROOF, n); p.face_n([(x, y, z - 0.04) for x, y, z in q], ROOF, tuple(-c for c in n))
        p.box(x0 - 0.4, ya - 0.02, ze - 0.10, x1 + 0.4, ya + 0.02, ze, T)           # fascia
        for k in range(10): xx = x0 - 0.3 + k * (W + 0.6) / 9; p.box(xx, ya * 0.94 - 0.03, ze - 0.12, xx + 0.07, ya * 0.94 + 0.03, ze - 0.04, T)    # rafter tails
    p.box(x0 - 0.42, -0.06, R - 0.04, x1 + 0.42, 0.06, R + 0.06, "metal_galvanised")                          # ridge cap
    return p
