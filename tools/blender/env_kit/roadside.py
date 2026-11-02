"""Roadside commerce and village props of the environmental kit (REGIONAL, northern Nigerian roadside; vernacular not yet verified against real references, gap M-14): a market table stall under a large
umbrella with produce, a rough canopy stall (pole frame, tarpaulin roof, counter, sacks), a woven timber fence module, and a roadside open drain with slab covers (GLOBAL). All real geometry; the
produce is modelled as small low-polygon fruit, not painted on."""
from __future__ import annotations
import math, random
from mathutils import Vector
from kano_kit.geo import Part

T = "timber_weathered"


def _pile(p, cx, cy, z0, key, r, ring, seed, ellipsoid=(1, 1, 1)):
    """A heap of fruit: a ring, a smaller ring and a crown, low-polygon balls."""
    rnd = random.Random(seed); layers = [(ring, 0.0, z0), (max(ring - 4, 2), r * 0.9, z0 + r * 1.55), (1, 0.0, z0 + r * 2.9)]
    for n, rad, z in layers:
        for k in range(n):
            a = 2 * math.pi * k / max(n, 1) + rnd.uniform(-0.2, 0.2); rr = (r * 2.2 if z == z0 else rad) * (1 if n > 1 else 0)
            p.blob((cx + rr * math.cos(a), cy + rr * math.sin(a), z + r), (r * ellipsoid[0], r * ellipsoid[1], r * ellipsoid[2]), key, subdiv=0, jitter=0.12, seed=rnd.randint(0, 999))


def stall_umbrella(lod: int = 0) -> Part:
    """A market table (1.8 x 0.9 m) with three heaps of produce (tomatoes, oranges, yams) under a 2.6 m umbrella of twelve red and white gores on a pole. The customer side is -Y."""
    p = Part("stall")
    if lod >= 1:
        p.box(-0.9, -0.45, 0.80, 0.9, 0.45, 0.86, T); p.box(-0.85, -0.40, 0.0, -0.79, -0.34, 0.80, T); p.box(0.79, 0.34, 0.0, 0.85, 0.40, 0.80, T)
        p.tube((0, 0.62, 0), (0, 0.62, 2.5), 0.03, 0.02, 4, "metal_galvanised", caps=(False, True), smooth=False)
        for k in range(6):
            a0 = 2 * math.pi * k / 6; a1 = 2 * math.pi * (k + 1) / 6; apex = (0, 0.62, 2.55); r0 = (1.3 * math.cos(a0), 0.62 + 1.3 * math.sin(a0), 2.15); r1 = (1.3 * math.cos(a1), 0.62 + 1.3 * math.sin(a1), 2.15)
            p.face([apex, r0, r1], "fabric_red" if k % 2 == 0 else "fabric_white")
        return p
    p.box(-0.9, -0.45, 0.82, 0.9, 0.45, 0.86, T)                                            # top
    for sx in (-1, 1):
        for sy in (-1, 1): p.box(sx * 0.85 - 0.03, sy * 0.40 - 0.03, 0.0, sx * 0.85 + 0.03, sy * 0.40 + 0.03, 0.82, T)
    p.box(-0.85, -0.40, 0.22, 0.85, 0.40, 0.25, T)                                          # lower shelf
    _pile(p, -0.55, 0.0, 0.86, "produce_tomato", 0.045, 8, 1); _pile(p, 0.0, 0.0, 0.86, "produce_orange", 0.05, 8, 2); _pile(p, 0.55, 0.0, 0.86, "produce_yam", 0.055, 7, 3, (1.5, 0.9, 0.9))
    for k, (x, c) in enumerate(((-0.45, "produce_tomato"), (0.2, "produce_orange"), (0.65, "produce_yam"))): p.blob((x, -0.2, 0.30), (0.10, 0.10, 0.07), c, subdiv=0, jitter=0.1, seed=40 + k)   # a few more on the lower shelf
    p.box(-0.30, 0.45, 0.0, 0.30, 0.95, 0.04, "concrete")                                   # stand for the pole
    p.tube((0, 0.62, 0.04), (0, 0.62, 2.5), 0.03, 0.02, 8, "metal_galvanised", caps=(False, True))
    apex = Vector((0, 0.62, 2.58)); R = 1.3; n = 12; rim = [Vector((R * math.cos(2 * math.pi * k / n), 0.62 + R * math.sin(2 * math.pi * k / n), 2.12)) for k in range(n)]
    for k in range(n):
        key = "fabric_red" if k % 2 == 0 else "fabric_white"; a, b = rim[k], rim[(k + 1) % n]; mid = (a + b) / 2 + Vector((0, 0, -0.03))
        p.face([apex, a, b], key); p.face([apex, b, a], key)                                # two-sided canopy
        p.tube(apex - Vector((0, 0, 0.04)), a, 0.008, 0.008, 4, "metal_galvanised", caps=(False, False), smooth=False)    # rib
        p.tube(a, b, 0.008, 0.008, 4, "metal_galvanised", caps=(False, False), smooth=False)                               # rim
    return p


def stall_canopy_rough(lod: int = 0) -> Part:
    """A rough roadside stall: four timber poles, a sagging olive tarpaulin roof tied down with ropes, a plank counter, a stack of sacks and crates of yams. The customer side is -Y."""
    p = Part("stall")
    if lod >= 1:
        for sx in (-1, 1): p.box(sx * 1.4 - 0.04, -1.0, 0.0, sx * 1.4 + 0.04, -0.92, 2.3, T)
        p.box(-1.2, -0.9, 0.8, 1.2, -0.35, 0.85, T)
        p.face([(-1.8, -1.4, 2.35), (1.8, -1.4, 2.35), (1.8, 1.4, 2.0), (-1.8, 1.4, 2.0)], "tarp_olive"); p.face([(-1.8, 1.4, 2.0), (1.8, 1.4, 2.0), (1.8, -1.4, 2.35), (-1.8, -1.4, 2.35)], "tarp_olive")
        return p
    for (x, y, h) in ((-1.4, -1.0, 2.3), (1.4, -1.0, 2.3), (-1.4, 1.0, 2.0), (1.4, 1.0, 2.0)): p.tube((x, y, 0), (x + 0.02, y, h), 0.05, 0.04, 6, T, caps=(False, True), smooth=False)
    nx, ny = 8, 6; pts = {}
    def zt(x, y): return 2.35 - 0.35 * (y + 1.4) / 2.8 - 0.07 * math.sin(math.pi * (x + 1.8) / 3.6) * math.sin(math.pi * (y + 1.4) / 2.8) + 0.02 * math.sin(7 * x + 3 * y)
    for i in range(nx + 1):
        for j in range(ny + 1): x = -1.8 + 3.6 * i / nx; y = -1.4 + 2.8 * j / ny; pts[(i, j)] = (x, y, zt(x, y))
    for i in range(nx):
        for j in range(ny):
            q = [pts[(i, j)], pts[(i + 1, j)], pts[(i + 1, j + 1)], pts[(i, j + 1)]]; p.face(q, "tarp_olive", smooth=True); p.face(list(reversed(q)), "tarp_olive", smooth=True)
    for (x, y, h) in ((-1.4, -1.0, 2.3), (1.4, -1.0, 2.3), (-1.4, 1.0, 2.0), (1.4, 1.0, 2.0)):          # ropes from the tarp corners to the poles
        p.tube((x * 1.28, y * 1.4, zt(x * 1.28, y * 1.4)), (x + 0.02, y, h - 0.1), 0.01, 0.01, 4, "burlap", caps=(False, False), smooth=False)
    p.box(-1.25, -0.90, 0.80, 1.25, -0.35, 0.85, T)                                         # counter top
    for sx in (-1, 1): p.box(sx * 1.15 - 0.04, -0.85, 0.0, sx * 1.15 + 0.04, -0.40, 0.80, T)
    p.box(-1.15, -0.85, 0.30, 1.15, -0.40, 0.33, T)
    rnd = random.Random(7)
    for k, (x, y, z) in enumerate(((-1.1, 0.55, 0.0), (-0.6, 0.6, 0.0), (-0.85, 0.55, 0.38), (-1.05, 0.0, 0.0), (-1.1, 0.25, 0.30))):       # sacks
        p.blob((x, y, z + 0.22), (0.26, 0.17, 0.22), "burlap", subdiv=1, jitter=0.07, seed=20 + k)
    for k in range(2): p.box(0.4 + k * 0.62, 0.35, 0.0, 0.95 + k * 0.62, 0.9, 0.34, T)       # crates
    for k in range(7): p.blob((0.55 + rnd.uniform(0, 1.1), 0.5 + rnd.uniform(0, 0.3), 0.40), (0.09, 0.07, 0.06), "produce_yam", subdiv=0, jitter=0.12, seed=60 + k)
    for k in range(5): p.blob((-0.8 + k * 0.4, -0.62, 0.88), (0.08, 0.08, 0.05), "produce_tomato" if k % 2 == 0 else "produce_orange", subdiv=0, jitter=0.1, seed=80 + k)
    return p


def fence_woven_timber(lod: int = 0, length: float = 3.0) -> Part:
    """A 3 m module of rural fence: four posts and nine woven horizontal rods that pass in front of and behind each post alternately, with thin vertical stakes between."""
    p = Part("fence"); L = length / 2
    xs = [-L + k * length / 3 for k in range(4)]
    for x in xs: p.tube((x, 0, 0), (x, 0, 1.45), 0.05, 0.035, 6 if lod == 0 else 4, T, caps=(False, True), smooth=False)
    if lod >= 1:
        for z in (0.35, 0.75, 1.15): p.box(-L, -0.02, z, L, 0.02, z + 0.05, T)
        return p
    for k in range(9):
        z = 0.20 + k * 0.14; ph = 0.0 if k % 2 == 0 else math.pi; pts = []
        for i in range(13):
            x = -L + length * i / 12; pts.append(Vector((x, 0.055 * math.sin(2 * math.pi * (x + L) / (length / 1.5) * 0.75 + ph + math.pi / 2), z)))
        p.path_tube(pts, 0.016, 4, T, caps=(True, True), smooth=False)
    for k in range(1, 24):
        x = -L + length * k / 24
        if min(abs(x - px) for px in xs) > 0.08: p.tube((x, 0.0, 0.02), (x, 0.0, 1.30), 0.014, 0.012, 4, T, caps=(False, True), smooth=False)
    return p


def drain_open_slab(lod: int = 0) -> Part:
    """A 6 m section of concrete roadside drain along Y: floor, two walls, five slab covers (one cracked and dropped, one missing, leaving an opening), silt on the floor and a few stones. The bottom of the channel is
    z = 0; the road surface beside it is at z = 0.85 (the asset is placed 0.85 m lower, datum 0.85)."""
    p = Part("drain"); C = "concrete"
    p.box(-0.45, -3.0, 0.0, 0.45, 3.0, 0.15, C)
    for sx in (-1, 1): p.box(sx * 0.45 - (0.15 if sx > 0 else 0), -3.0, 0.15, sx * 0.45 + (0 if sx > 0 else 0.15), 3.0, 0.85, C, skip=() )
    if lod >= 1: return p
    for k, (y0, y1) in enumerate(((-3.0, -2.0), (-2.0, -1.0), (1.0, 2.0), (2.0, 3.0))):                     # slabs
        z = 0.78 if k != 1 else 0.70
        p.box(-0.55, y0 + 0.03, z, 0.55, y1 - 0.03, z + 0.07, C)
    p.box(-0.55, -0.45, 0.78, 0.55, -0.02, 0.85, C); p.face_n([(-0.55, 0.02, 0.62), (0.55, 0.02, 0.62), (0.55, 0.45, 0.80), (-0.55, 0.45, 0.80)], C, (0, 0, 1))      # the cracked slab: one half level, one half dropped and tilted
    p.box(-0.40, -0.4, 0.15, 0.40, 0.4, 0.20, "mud_crest"); p.box(-0.40, 1.3, 0.15, 0.40, 2.1, 0.19, "mud_crest")        # silt
    rnd = random.Random(3)
    for k in range(5): p.blob((rnd.uniform(-0.3, 0.3), rnd.uniform(-2.8, 2.8), 0.20), (0.07, 0.06, 0.05), "pothole_earth", subdiv=0, jitter=0.2, seed=70 + k)
    for sx in (-1, 1):                                                                       # rebar stubs at the broken slab
        for k in range(3): p.tube((sx * 0.5, -0.05 + k * 0.05, 0.84), (sx * 0.5, -0.05 + k * 0.05, 0.95), 0.007, 0.007, 4, "metal_galvanised", caps=(False, True), smooth=False)
    return p
