"""Wet-season and village vegetation of the environmental kit (REGIONAL, northern Nigeria; the species that really grow along Kano's roads, compounds and villages, and how they look through the
seasons, are NOT established, gaps M-07 and M-17): the large shade tree and the tall grass in their green, wet-season dress (R-019 'lush green vegetation after rain', R-020 'banana plants, tall grass'),
a banana plant clump (R-020) and bougainvillea spilling over a compound wall (R-016). Real geometry; foliage is alpha-clipped cards, the banana leaves are bent strips of quads carrying a leaf texture
with torn edges. Seasonal switching of vegetation is engine work (RB-24), not done: these are separate assets in the seasonal dress the references show."""
from __future__ import annotations
import math, random
from mathutils import Vector
from kano_kit import veg as KV
from kano_kit.geo import Part


def tree_large_shade_wet(lod: int = 0) -> Part:
    """The same spreading shade tree as the dry-season asset (same trunk, scaffold branches and crown volume) in a dense, saturated green crown: more and larger cards, wet-season greens."""
    return KV.tree(5, 11.0, 11.0, lod, cards=(170, 6, (1.0, 1.6)), leaf_key="leaf_wet_cutout")


def grass_tall_wet(lod: int = 0) -> Part:
    """A clump of green grass after rain: 30 blade cards 0.4 to 0.6 m wide and 1.0 to 1.7 m tall, leaning outward, standing on the ground."""
    p = Part("grass"); rnd = random.Random(19)
    for k in range(30):
        th = rnd.uniform(0, 2 * math.pi); rr = rnd.uniform(0.0, 0.38); base = Vector((rr * math.cos(th), rr * math.sin(th), 0.0)); h = rnd.uniform(1.0, 1.7); w = rnd.uniform(0.40, 0.60)
        lean = Vector((math.cos(th), math.sin(th), 0.0)) * rnd.uniform(0.05, 0.32); up = (lean + Vector((0, 0, 1))).normalized(); right = Vector((-math.sin(th + rnd.uniform(-0.5, 0.5)), math.cos(th + rnd.uniform(-0.5, 0.5)), 0.0)).normalized()
        flip = rnd.random() < 0.5
        p.card(base + up * (h / 2), right, up, w, h, "grass_blade_wet_cutout", (1, 0) if flip else (0, 0), (0, 1) if flip else (1, 1))
    return p


# ------------------------------------------------------------------------------------------------------------------------------------------ banana
def _fixed_quad(p: Part, pts, uvs, key: str):
    """A quad with its own UVs (kept by Part.uv_world, like Part.card does for a flat card), facing up. Used for bent leaf strips."""
    a, b, c = (Vector(x) for x in pts[:3]); n = (b - a).cross(c - a)
    if n.length < 1e-9: return None
    f = p.face_n(pts, key, n if n.z >= 0 else -n)
    if f is None: return None
    uvl = p.bm.loops.layers.uv.verify(); cs = [Vector(x) for x in pts]
    for l in f.loops:
        k = min(range(4), key=lambda i: (cs[i] - l.vert.co).length); l[uvl].uv = uvs[k]
    if not hasattr(p, "_fixed"): p._fixed = set()
    p._fixed.add(f); return f


def _banana_leaf(p: Part, base: Vector, th: float, L: float, W: float, rise: float, segs: int, droop: float = 0.0):
    """One leaf blade as a V-shaped strip: it rises from the top of the stem, arches outward and its tip hangs down. u runs across the blade, v from base to tip."""
    dirh = Vector((math.cos(th), math.sin(th), 0.0)); perp = Vector((-math.sin(th), math.cos(th), 0.0)); up = Vector((0, 0, 1)); b = rise * 1.05 + 0.12 + droop
    def centre(t): return base + dirh * (L * 0.78 * t) + up * (L * (rise * t - b * t * t))
    rows = []
    for i in range(segs + 1):
        t = i / segs; c = centre(t); w = W * 0.5; sag = 0.07 * W
        rows.append((c + perp * w - up * sag, c, c - perp * w - up * sag, t))
    for i in range(segs):
        l0, m0, r0, t0 = rows[i]; l1, m1, r1, t1 = rows[i + 1]
        _fixed_quad(p, [l0, m0, m1, l1], [(0.0, t0), (0.5, t0), (0.5, t1), (0.0, t1)], "banana_leaf_cutout")
        _fixed_quad(p, [m0, r0, r1, m1], [(0.5, t0), (1.0, t0), (1.0, t1), (0.5, t1)], "banana_leaf_cutout")


def banana_plant(lod: int = 0) -> Part:
    """A clump of three banana plants of different ages, 2.8 m, 2.2 m and 1.5 m, each with a pale pseudostem and a crown of arching leaf blades; the tallest carries a hanging bunch of green fruit. LOD1 has fewer
    and coarser leaves; LOD2 a stem and a few flat blades per plant."""
    p = Part("banana"); rnd = random.Random(14)
    plants = [((0.0, 0.0), 2.8, 9, 0.17), ((0.62, 0.30), 2.2, 8, 0.15), ((-0.50, 0.46), 1.5, 6, 0.12)]
    for (x, y), h, n_leaves, r0 in plants:
        seg = {0: 8, 1: 5, 2: 4}[lod]
        p.tube((x, y, 0.0), (x, y, h), r0, r0 * 0.62, seg, "banana_stem", caps=(False, True), smooth=lod == 0)
        if lod == 0:
            for k in range(3): p.tube((x, y, 0.0), (x, y, 0.35 + 0.15 * k), r0 * (1.12 - 0.04 * k), r0 * (1.04 - 0.04 * k), seg, "bark", caps=(False, False), smooth=False)       # dry brown sheaths at the base
        nl = {0: n_leaves, 1: max(4, n_leaves - 3), 2: 4}[lod]; segs = {0: 6, 1: 3, 2: 1}[lod]; golden = 2.39996
        for k in range(nl):
            th = k * golden + rnd.uniform(-0.2, 0.2); L = h * rnd.uniform(0.80, 1.0) * (1.0 if lod < 2 else 0.9); W = L * rnd.uniform(0.30, 0.36)
            rise = rnd.uniform(0.50, 1.0) if k < nl - 1 else 1.35                                      # the youngest leaf stands almost upright
            _banana_leaf(p, Vector((x, y, h - 0.05)), th, L, W, rise, segs, droop=0.06 * (k % 3))
    if lod <= 1:                                                                                       # the bunch: a stalk bending down from below the crown, five hands of fingers
        top = Vector((0.0, 0.0, 2.7)); stalk = [top, top + Vector((0.18, 0.05, -0.18)), top + Vector((0.30, 0.08, -0.55))]
        p.path_tube(stalk, [0.035, 0.030, 0.028], 5, "banana_stem", caps=(False, True), smooth=False)
        hands = 5 if lod == 0 else 2
        for hnd in range(hands):
            z = top.z - 0.25 - hnd * 0.075; cx = top.x + 0.18 + hnd * 0.032; cy = top.y + 0.05 + hnd * 0.012
            for f in range(6 if lod == 0 else 3):
                a = 2 * math.pi * f / (6 if lod == 0 else 3) + hnd * 0.5
                p.blob((cx + 0.075 * math.cos(a), cy + 0.075 * math.sin(a), z - 0.07), (0.022, 0.022, 0.085), "banana_fruit", subdiv=0, jitter=0.1, seed=70 + hnd * 7 + f)
    return p


# ------------------------------------------------------------------------------------------------------------------------------------------ bougainvillea
def bougainvillea_wall(lod: int = 0) -> Part:
    """A 4 m length of plastered compound wall (a plinth band, a capped top, a capped pier at each end) with a bougainvillea growing over the top and spilling down the street face: a few woody canes, green leaf cards
    and many magenta bract clusters. The wall is the same weathered render as the rest of the environmental kit. The street side is -Y."""
    p = Part("bougainvillea"); rnd = random.Random(23); L = 4.0
    pl, cap = "plaster_weathered", "concrete"
    if lod >= 2:
        p.box(-L / 2, -0.125, 0.0, L / 2, 0.125, 1.9, pl); p.blob((0, 0.0, 2.15), (1.9, 0.55, 0.5), "flower_bougainvillea", subdiv=0, jitter=0.1, seed=3); return p
    p.box(-L / 2, -0.125, 0.0, L / 2, 0.125, 0.4, "concrete", skip=("left", "right", "bottom")); p.box(-L / 2, -0.115, 0.4, L / 2, 0.115, 1.8, pl, skip=("left", "right", "bottom", "top"))
    p.box(-L / 2, -0.17, 1.8, L / 2, 0.17, 1.92, cap, skip=("left", "right"))
    for sx in (-1, 1): p.box(sx * (L / 2 - 0.2), -0.22, 0.0, sx * (L / 2 + 0.2), 0.22, 2.12, pl); p.box(sx * (L / 2 - 0.25), -0.27, 2.12, sx * (L / 2 + 0.25), 0.27, 2.24, cap)
    if lod >= 1:
        p.blob((0, -0.05, 2.1), (1.9, 0.6, 0.55), "flower_bougainvillea", subdiv=1, jitter=0.12, seed=4); p.blob((0, -0.35, 1.45), (1.5, 0.3, 0.45), "leaf_cutout", subdiv=0, jitter=0.1, seed=5); return p
    for k in range(4):                                                                                  # woody canes: up the pier, along the top, hanging over the face
        x0 = rnd.uniform(-1.6, 1.6); pts = [Vector((x0, 0.0, 0.0)), Vector((x0 + rnd.uniform(-0.2, 0.2), 0.02, 1.0)), Vector((x0 + rnd.uniform(-0.5, 0.5), -0.1, 1.95)), Vector((x0 + rnd.uniform(-0.9, 0.9), -0.45, 2.15))]
        p.path_tube(pts, [0.03, 0.025, 0.02, 0.012], 4, "bark", caps=(False, True), smooth=False)
    def spot():                                                                                         # a point of the mass: dense along the top, trailing down the street face
        u = rnd.random()
        if u < 0.62: return Vector((rnd.uniform(-1.9, 1.9), rnd.gauss(-0.1, 0.30), rnd.uniform(1.88, 2.45)))
        return Vector((rnd.uniform(-1.9, 1.9), rnd.uniform(-0.55, -0.25), rnd.uniform(1.0, 1.9)))
    for _ in range(78):
        c = spot(); n = Vector((rnd.uniform(-0.4, 0.4), -1.0 + rnd.uniform(-0.3, 0.3), rnd.uniform(0.0, 0.8))).normalized(); KV._cards_on(p, [c], n, 0.36, rnd, 0.3)
    for k in range(44):
        c = spot(); p.blob(c, (0.095, 0.095, 0.07), "flower_bougainvillea", subdiv=0, jitter=0.28, seed=300 + k)
    return p
