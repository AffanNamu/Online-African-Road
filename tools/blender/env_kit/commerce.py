"""Roadside commerce of the environmental kit, batch E3 (REGIONAL, northern Nigerian roadside; vernacular NOT verified against real references, gap M-14): a lock-up kiosk (one standalone unit of the shop
type seen in R-016 to R-021: block walls, a serving hatch with a raised steel shutter, a corrugated roof) and an open market shed (R-021 'market village': timber posts, a corrugated gable roof on purlins and
rafters, table stalls with produce and sacks beneath). The real traditional markets of Kano (Kurmi, Sabon Gari) have NO reference yet (M-04, M-12): neither asset depicts them. Real geometry throughout.
Front is -Y; the origin is the centre of the footprint on the ground."""
from __future__ import annotations
import math, random
from mathutils import Matrix, Vector
from kano_kit.geo import Part
from .roadside import _pile

T, PW, ROOF, CONC = "timber_weathered", "plaster_weathered", "roof_zinc_rust", "concrete"


def _roof_plane(p, pts, key, thick=0.04):
    """A roof sheet as a thin slab: the top face (normal up), the underside, and a drip edge."""
    a, b, c = (Vector(x) for x in pts[:3]); n = (b - a).cross(c - a)
    n = n if n.z >= 0 else -n
    p.face_n(pts, key, n); p.face_n([(x, y, z - thick) for x, y, z in pts], key, -n)


def kiosk_lockup(lod: int = 0) -> Part:
    """A lock-up kiosk 2.4 x 2.4 m on a low plinth: block walls 2.35 m high with a serving hatch (1.5 x 0.9 m) on the front, the steel shutter raised and propped out as a canopy, a plank counter and shelf, a blank
    sign band, a plank door on the right-hand side, a mono-pitch corrugated roof overhanging the front, a bench with a heap of oranges and tomatoes on the counter."""
    p = Part("kiosk"); h = 1.2; zb = 0.12; zt = 2.35
    p.box(-1.35, -1.35, 0.0, 1.35, 1.35, zb, CONC)                                                   # plinth
    if lod >= 1:
        p.box(-h, -h, zb, h, h, zt, PW); q = [(-1.55, -1.6, 2.62), (1.55, -1.6, 2.62), (1.55, 1.4, 2.28), (-1.55, 1.4, 2.28)]; _roof_plane(p, q, ROOF); return p
    t = 0.2
    # walls: back and left full; right with a door opening; front around the hatch
    p.box(-h, h - t, zb, h, h, zt, PW); p.box(-h, -h, zb, -h + t, h - t, zt, PW)
    p.box(h - t, 0.62, zb, h, h - t, zt, PW); p.box(h - t, -h, zb, h, -0.22, zt, PW); p.box(h - t, -0.22, 2.05, h, 0.62, zt, PW)                        # right wall around the door (y -0.22 .. 0.62, up to 2.05)
    p.box(-h + t, -h, zb, -0.75, -h + t, zt, PW); p.box(0.75, -h, zb, h - t, -h + t, zt, PW)                                                          # front piers either side of the hatch
    p.box(-0.75, -h, zb, 0.75, -h + t, 0.95, PW); p.box(-0.75, -h, 1.85, 0.75, -h + t, zt, PW)                                                         # under and over the hatch
    p.box(-0.82, -h - 0.03, 0.93, 0.82, -h + t, 0.98, CONC)                                                                                           # sill
    p.box(-h + t, h - t, zb, h - t, h - t + 0.001, zt, "interior_dark", skip=("bottom", "top", "left", "right", "front"))                              # dark interior seen through the hatch
    p.box(-h + t, -h + t, zb, h - t, h - t, zb + 0.02, CONC)                                                                                          # floor
    # sign band: a blank panel with a painted border over the hatch
    p.box(-0.82, -h - 0.04, 1.86, 0.82, -h, 2.34, "paint_red")
    # the shutter raised and propped out as a canopy: hinged at the top of the hatch, 60 degrees from the wall
    m0 = p.mark(); p.box(-0.80, 0.0, 0.0, 0.80, 0.035, 0.80, "shutter_steel")
    p.xform(m0, Matrix.Translation((0.0, -h - 0.04, 1.86)) @ Matrix.Rotation(math.radians(62), 4, "X"))
    for sx in (-1, 1): p.tube((sx * 0.74, -h - 0.45, 1.86 + 0.70), (sx * 0.74, -h - 0.70, 0.98), 0.012, 0.012, 4, "metal_galvanised", caps=(False, False), smooth=False)   # props
    # plank counter in front of the sill and a shelf under it
    p.box(-0.95, -h - 0.34, 0.92, 0.95, -h - 0.03, 0.96, T)
    for sx in (-1, 1): p.box(sx * 0.9 - 0.03, -h - 0.30, 0.12, sx * 0.9 + 0.03, -h - 0.06, 0.92, T)
    p.box(-0.88, -h - 0.30, 0.45, 0.88, -h - 0.06, 0.48, T)
    # door on the right-hand side: plank leaf slightly open
    p.box(h - t + 0.02, -0.18, zb, h - t + 0.07, 0.58, 2.02, T); p.box(h - t - 0.02, -0.26, 2.02, h + 0.02, 0.66, 2.10, T)
    # window slot on the left with a steel grille
    p.box(-h - 0.01, -0.35, 1.25, -h + t + 0.01, 0.35, 1.85, "interior_dark")
    for k in range(5): p.tube((-h - 0.03, -0.30 + k * 0.15, 1.25), (-h - 0.03, -0.30 + k * 0.15, 1.85), 0.009, 0.009, 4, "metal_galvanised", caps=(False, False), smooth=False)
    # roof: one corrugated sheet, high at the front, with a timber fascia
    q = [(-1.55, -1.65, 2.62), (1.55, -1.65, 2.62), (1.55, 1.40, 2.28), (-1.55, 1.40, 2.28)]; _roof_plane(p, q, ROOF)
    p.box(-1.55, -1.67, 2.52, 1.55, -1.63, 2.64, T); p.box(-1.57, -1.65, 2.28, -1.53, 1.40, 2.64, T); p.box(1.53, -1.65, 2.28, 1.57, 1.40, 2.64, T)
    # goods on the counter and a bench beside the kiosk
    _pile(p, -0.45, -h - 0.19, 0.96, "produce_orange", 0.045, 8, 5); _pile(p, 0.35, -h - 0.19, 0.96, "produce_orange", 0.04, 8, 6)
    p.box(-2.55, -1.0, 0.40, -1.65, -0.70, 0.45, T)
    for sx in (-2.50, -1.70): p.box(sx - 0.03, -0.97, 0.0, sx + 0.03, -0.73, 0.40, T)
    return p


def market_shed(lod: int = 0) -> Part:
    """An open market shed 14.4 x 8.4 m: a concrete slab, three rows of five timber posts, a corrugated gable roof (eaves 3.0 m, ridge 4.3 m) on rafters and purlins, six table stalls under it with produce and sacks,
    and a tarpaulin hung as a divider. Open on all four sides. The ridge runs along X."""
    p = Part("shed"); X = 7.2; Y = 4.2; ze = 3.0; zr = 4.3; ov = 0.5
    p.box(-X, -Y, 0.0, X, Y, 0.15, CONC)
    if lod >= 1:
        for sx in (-1, 1):
            for sy in (-1, 0, 1): p.box(sx * 6.4 - 0.1, sy * 3.6 - 0.1, 0.15, sx * 6.4 + 0.1, sy * 3.6 + 0.1, ze + (zr - ze) * (sy == 0), T)
        for sgn in (-1, 1): _roof_plane(p, [(-X - ov, sgn * (Y + ov), ze - 0.2), (X + ov, sgn * (Y + ov), ze - 0.2), (X + ov, 0.0, zr), (-X - ov, 0.0, zr)], ROOF)
        return p
    xs = [-6.4 + k * 3.2 for k in range(5)]
    for x in xs:                                                                                      # posts
        for y, top in ((-3.6, ze), (0.0, zr - 0.05), (3.6, ze)): p.tube((x, y, 0.15), (x + 0.02, y, top), 0.08, 0.07, 6, T, caps=(False, True), smooth=False)
    for sgn in (-1, 1):                                                                                # rafters and the beams that carry them
        p.tube((-X, sgn * 3.6, ze - 0.05), (X, sgn * 3.6, ze - 0.05), 0.05, 0.05, 4, T, caps=(False, False), smooth=False)
        for x in [-X + 0.4 + k * (2 * X - 0.8) / 8 for k in range(9)]:
            p.tube((x, sgn * (Y + ov - 0.1), ze - 0.28), (x, 0.0, zr - 0.06), 0.035, 0.035, 4, T, caps=(False, False), smooth=False)
        for yy in (sgn * 1.4, sgn * 2.8): p.tube((-X - ov + 0.1, yy, ze + (zr - ze) * (1 - abs(yy) / (Y + ov)) - 0.06), (X + ov - 0.1, yy, ze + (zr - ze) * (1 - abs(yy) / (Y + ov)) - 0.06), 0.03, 0.03, 4, T, caps=(False, False), smooth=False)   # purlins
    p.tube((-X - ov, 0.0, zr - 0.06), (X + ov, 0.0, zr - 0.06), 0.05, 0.05, 4, T, caps=(False, False), smooth=False)      # ridge beam
    for sgn in (-1, 1):                                                                                # the two roof sheets with a ridge cap and a fascia
        _roof_plane(p, [(-X - ov, sgn * (Y + ov), ze - 0.28), (X + ov, sgn * (Y + ov), ze - 0.28), (X + ov, 0.0, zr), (-X - ov, 0.0, zr)], ROOF)
        p.box(-X - ov, sgn * (Y + ov) - 0.02, ze - 0.38, X + ov, sgn * (Y + ov) + 0.02, ze - 0.26, T)
    p.box(-X - ov, -0.07, zr - 0.02, X + ov, 0.07, zr + 0.06, "metal_galvanised")
    # six table stalls along the two long sides, each with produce or sacks
    rnd = random.Random(31); goods = ["produce_tomato", "produce_orange", "produce_yam"]
    for i, sgn in enumerate((-1, -1, -1, 1, 1, 1)):
        x = -4.2 + (i % 3) * 4.2; y = sgn * 2.3
        p.box(x - 0.95, y - 0.45, 0.80, x + 0.95, y + 0.45, 0.85, T)
        for sx in (-1, 1):
            for sy in (-1, 1): p.box(x + sx * 0.90 - 0.03, y + sy * 0.40 - 0.03, 0.15, x + sx * 0.90 + 0.03, y + sy * 0.40 + 0.03, 0.80, T)
        if i % 2 == 0: _pile(p, x - 0.45, y, 0.85, goods[i % 3], 0.05, 8, 100 + i); _pile(p, x + 0.40, y, 0.85, goods[(i + 1) % 3], 0.05, 8, 110 + i)
        else:
            for k in range(3): p.blob((x - 0.6 + k * 0.6, y, 0.85 + 0.20), (0.26, 0.17, 0.20), "burlap", subdiv=1, jitter=0.07, seed=120 + i * 3 + k)
        p.box(x - 0.92, y - 0.40, 0.30, x + 0.92, y + 0.40, 0.33, T)
    for k in range(3): p.box(-6.9 + k * 0.5, -0.35, 0.15, -6.5 + k * 0.5, 0.35, 0.55, T)                 # a stack of crates at the west end
    p.face([(-0.9, -3.55, 2.85), (2.1, -3.55, 2.85), (2.1, -3.55, 1.45), (-0.9, -3.55, 1.45)], "tarp_olive"); p.face([(-0.9, -3.55, 1.45), (2.1, -3.55, 1.45), (2.1, -3.55, 2.85), (-0.9, -3.55, 2.85)], "tarp_olive")     # a hung tarpaulin
    return p
