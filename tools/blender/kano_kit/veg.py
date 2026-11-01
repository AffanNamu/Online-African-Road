"""Vegetation for the residential street: street trees (trunk and branches as swept tubes, crown as many displaced foliage clumps in three greens), clipped hedge module and red ixora shrub.
Real geometry throughout; foliage is opaque clumps (alpha-cut leaf cards need a transparent kit material the pipeline does not have yet: noted in the registry)."""
from __future__ import annotations
import math, random
from mathutils import Vector
from .geo import Part

LEAF = ("leaf_green_dark", "leaf_green", "leaf_green_light")


def _tone(c: Vector, rnd: random.Random) -> str:
    h = c.normalized().z + rnd.uniform(-0.25, 0.25)
    return LEAF[2] if h > 0.35 else LEAF[0] if h < -0.15 else LEAF[1]


def tree(seed: int = 1, height: float = 8.0, crown_w: float = 6.0, lod: int = 0) -> Part:
    """Street tree: flared trunk to ~2.7 m, five scaffold branches and a crown of alpha-cut leaf cards (a 2 x 2 atlas of leafy clusters in four greens), cards crossed at random angles inside
    a crown ellipsoid. LOD1 has fewer, larger cards; LOD2 a handful of big ones. The cards are two-sided cutouts: the kit material M_leaf_cutout carries the texture."""
    rnd = random.Random(seed); p = Part("tree"); seg = 7 if lod == 0 else 5
    th = height * 0.34; lean = Vector((rnd.uniform(-0.15, 0.15), rnd.uniform(-0.15, 0.15), 0))
    trunk = [Vector((lean.x * t, lean.y * t, th * t)) for t in (0, 0.04, 0.12, 0.3, 0.55, 0.8, 1.0)]
    if lod >= 2: p.tube((0, 0, 0), (lean.x, lean.y, th), 0.2, 0.10, 4, "bark", caps=(False, True), smooth=False)
    else: p.path_tube(trunk, [0.30, 0.22, 0.17, 0.15, 0.13, 0.115, 0.10], seg, "bark", caps=(False, True), smooth=True)
    top = trunk[-1]; n_br = 5 if lod == 0 else 3; ends = []
    for i in range(n_br):
        a = 2 * math.pi * i / n_br + rnd.uniform(-0.3, 0.3); reach = crown_w * rnd.uniform(0.22, 0.32); up = height * rnd.uniform(0.18, 0.30)
        end = top + Vector((math.cos(a) * reach, math.sin(a) * reach, up)); mid = top.lerp(end, 0.5) + Vector((0, 0, 0.15))
        if lod < 2: p.path_tube([top - Vector((0, 0, 0.1)), mid, end], [0.085, 0.06, 0.035], 5 if lod == 0 else 4, "bark", caps=(False, True), smooth=True)
        ends.append(end)
    cz = th + (height - th) * 0.58; rz = (height - th) * 0.46; rx = crown_w / 2
    n_cl, per, size = {0: (62, 6, (1.3, 2.0)), 1: (16, 3, (2.1, 3.0)), 2: (3, 2, (3.8, 4.6))}[lod]
    centre = Vector((top.x, top.y, cz)); pts = list(ends)
    while len(pts) < n_cl:
        v = Vector((rnd.gauss(0, 1), rnd.gauss(0, 1), rnd.gauss(0, 1))).normalized() * (0.35 + 0.65 * rnd.random() ** 0.5)       # inside an ellipsoid, biased to the surface
        pts.append(centre + Vector((v.x * rx, v.y * rx, v.z * rz)))
    for c in pts:
        for _ in range(per):
            out_ = (c - centre); out_ = out_.normalized() if out_.length > 1e-6 else Vector((0, 0, 1))
            n = (out_ * 0.7 + Vector((rnd.uniform(-1, 1), rnd.uniform(-1, 1), rnd.uniform(-0.6, 1.0))) * 0.8).normalized()
            right = n.cross(Vector((0, 0, 1))); right = right if right.length > 0.1 else Vector((1, 0, 0)); up = n.cross(right)
            s_ = rnd.uniform(*size); qx, qy = rnd.choice((0, 0.5)), rnd.choice((0, 0.5))
            p.card(c + Vector((rnd.uniform(-0.3, 0.3), rnd.uniform(-0.3, 0.3), rnd.uniform(-0.3, 0.3))), right, up, s_, s_, "leaf_cutout", (qx, qy), (qx + 0.5, qy + 0.5))
    return p


def _cards_on(p, pts, normal, size, rnd, tilt=0.35):
    for c in pts:
        n = (Vector(normal) + Vector((rnd.uniform(-tilt, tilt), rnd.uniform(-tilt, tilt), rnd.uniform(-tilt, tilt)))).normalized(); right = n.cross(Vector((0, 0, 1)))
        right = right if right.length > 0.1 else Vector((1, 0, 0)); up = n.cross(right); qx, qy = rnd.choice((0, 0.5)), rnd.choice((0, 0.5)); s_ = size * rnd.uniform(0.85, 1.15)
        p.card(c, right, up, s_, s_, "leaf_cutout", (qx, qy), (qx + 0.5, qy + 0.5))


def hedge(lod: int = 0, length: float = 2.0) -> Part:
    """A clipped hedge module 2.0 x 0.7 x 0.9 m: a dark opaque core (so nothing is see-through) wrapped in leaf cards on the top and both long sides and the ends."""
    p = Part("hedge"); L = length / 2; rnd = random.Random(5)
    p.box(-L + 0.02, -0.30, 0.0, L - 0.02, 0.30, 0.84, "hedge_green", skip=("bottom",))
    if lod == 0:
        for sy in (-1, 1): _cards_on(p, [Vector((x, sy * 0.33, z)) for x in (-0.75, -0.25, 0.25, 0.75) for z in (0.40, 0.66)], (0, sy, 0.1), 0.56, rnd, 0.2)
        _cards_on(p, [Vector((x, y, 0.86)) for x in (-0.75, -0.25, 0.25, 0.75) for y in (-0.15, 0.15)], (0, 0, 1), 0.62, rnd)
        for sx in (-1, 1): _cards_on(p, [Vector((sx * (L + 0.0), 0, z)) for z in (0.4, 0.66)], (sx, 0, 0.1), 0.56, rnd, 0.2)
    return p


def shrub_ixora(lod: int = 0) -> Part:
    """A rounded evergreen shrub 1.1 m tall and 1.3 m wide: a dark core, a dome of leaf cards and red flower clusters (ixora)."""
    p = Part("shrub"); rnd = random.Random(11)
    p.tube((0, 0, 0.0), (0, 0, 0.30), 0.34, 0.30, 8, "hedge_green", caps=(False, False), smooth=False)
    p.blob((0, 0, 0.42), (0.42, 0.42, 0.34), "hedge_green", subdiv=2 if lod == 0 else 1, jitter=0.1, seed=3, faces_keys=lambda f: "hedge_green")
    if lod == 0:
        for _ in range(26):
            v = Vector((rnd.gauss(0, 1), rnd.gauss(0, 1), abs(rnd.gauss(0, 0.9)))).normalized(); c = Vector((v.x * 0.58, v.y * 0.58, 0.34 + v.z * 0.70)); _cards_on(p, [c], v, 0.50, rnd, 0.2)
        for k in range(18):
            a = rnd.uniform(0, 2 * math.pi); e = rnd.uniform(0.25, 1.25); r = 0.62 + 0.06 * rnd.random(); c = Vector((math.cos(a) * math.cos(e) * r * 1.0, math.sin(a) * math.cos(e) * r, 0.30 + math.sin(e) * r * 0.85))
            p.blob(c, (0.10, 0.10, 0.07), "flower_red", subdiv=1, jitter=0.2, seed=50 + k)
    return p
