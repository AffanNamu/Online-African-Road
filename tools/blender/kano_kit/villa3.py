"""Residential kit v003: the two villas with a richer facade (owner decision of 2026-11-03: bring them closer to the approved concept R-006 without simplifying them excessively).

NEW module: kano_kit/villa.py is the v002 builder and stays byte-reproducible (the frozen v002 outputs are rebuilt from it). This file borrows its helpers (roofs, balcony slab, dormer) and changes:
  * window frames, grille bars and other slender parts are boxes with only their VISIBLE faces (a frame bar set into a reveal shows its front and its inner edge, not five faces): about 1,200 triangles saved,
    spent on the facade;
  * a stone-clad entrance with a round-arched fanlight over the double door (a real arch: spandrels, glass fan, arch reveal, stone arch ring, keystone), round columns with bases and capitals under the balcony,
    stone pilasters and grey cladding panels (proud, with joints) on the front, pediments over the ground-floor windows, wall lanterns beside the door, a warmly lit window, quoins on both types, an oculus in
    the gable of type B, cladding panels on its garage wing;
  * the builders use the v002 material keys; `kano_kit.build3` then separates overlapping coplanar faces and folds the flat colours into the shared palette materials (kano_kit.palette).
Axes as villa.py: asset front -Y, origin on the ground at the centre of the footprint."""
from __future__ import annotations
import math
from mathutils import Vector
from .geo import Part
from . import villa as V
from .villa import Fr, CREAM, GREY, STONE, ROOF, FRAME, GLASS, WHITE, DIRNAME, roof_slope, hip_roof, gable_roof, gable_dormer

_NAMES = {"u+": lambda fr: fr.u, "u-": lambda fr: -fr.u, "v+": lambda fr: fr.v, "v-": lambda fr: -fr.v, "n+": lambda fr: fr.n, "n-": lambda fr: -fr.n}


def fb(p, fr, u0, u1, v0, v1, w0, w1, key, skip=("n-",)):
    """A box in the facade frame with the named faces left out (u+/u-: along the wall, v+/v-: up/down, n+/n-: outward/inward). Default: not the face that looks into the wall."""
    a = fr.pt(u0, v0, w0); b = fr.pt(u1, v1, w1); sk = tuple(DIRNAME[tuple(int(round(c)) for c in _NAMES[s](fr))] for s in skip)
    p.box(min(a.x, b.x), min(a.y, b.y), min(a.z, b.z), max(a.x, b.x), max(a.y, b.y), max(a.z, b.z), key, skip=sk)


def extrude(p, fr, pts_uv, w0, w1, key, caps=True):
    """A prism: the polygon `pts_uv` (u, v) in the facade plane, extruded from w0 to w1 (outward). Front cap (if caps) and the side quads; the back is never seen."""
    n = len(pts_uv); P0 = [fr.pt(u, v, w0) for u, v in pts_uv]; P1 = [fr.pt(u, v, w1) for u, v in pts_uv]
    if caps: p.face_n(P1, key, fr.n)
    for i in range(n):
        j = (i + 1) % n; a, b = pts_uv[i], pts_uv[j]; t = (b[0] - a[0], b[1] - a[1]); out = fr.u * t[1] + fr.v * (-t[0])         # the edge's outward-pointing normal for a counter-clockwise polygon
        p.face_n([P0[i], P0[j], P1[j], P1[i]], key, out)


def arc(uc, vs, r, n, a0=math.pi, a1=0.0):
    return [(uc + r * math.cos(a0 + (a1 - a0) * k / n), vs + r * math.sin(a0 + (a1 - a0) * k / n)) for k in range(n + 1)]


def arched_head(p, fr, u0, u1, vs, reveal=0.16, n=10, ring=0.17, ring_w=0.08, lit=False, lod=0):
    """The round head of a door opening: the wall faces either side of the arc (spandrels) in the wall plane, a glass fan with a dark or warm room behind it, the arc reveal, a stone arch ring proud of the wall
    and a keystone. The rectangle (u0..u1) x (vs .. vs + r) must have been left open in the facade; the caller leaves the head reveal and lintel out."""
    r = (u1 - u0) / 2; uc = (u0 + u1) / 2; pts = arc(uc, vs, r, n); half = n // 2
    left = [fr.pt(u0, vs + r)] + [fr.pt(*q) for q in pts[:half + 1]]; right = [fr.pt(u1, vs + r)] + [fr.pt(*q) for q in pts[half:][::-1]]
    p.face_n(left, CREAM, fr.n); p.face_n(right, CREAM, fr.n)
    if lod >= 1: p.face_n([fr.pt(*q, -0.03) for q in pts], GLASS, fr.n); return
    z = -reveal + 0.05; glass = [fr.pt(uc, vs, z)] + [fr.pt(*q, z) for q in pts]                                                  # a fan of triangles from the springing centre
    for i in range(1, len(glass) - 1): p.face_n([glass[0], glass[i], glass[i + 1]], GLASS, fr.n)
    d = reveal + 0.40; room = "interior_warm" if lit else "interior_dark"
    p.face_n([fr.pt(uc, vs, -d)] + [fr.pt(*q, -d) for q in pts], room, fr.n)                                                         # the room behind, one polygon
    for i in range(n):                                                                                                               # the arc reveal: a band from the wall plane back to the glass
        a, b = pts[i], pts[i + 1]; mid = ((a[0] + b[0]) / 2, (a[1] + b[1]) / 2); inward = fr.u * (uc - mid[0]) + fr.v * (vs - mid[1])
        p.face_n([fr.pt(*a), fr.pt(*b), fr.pt(*b, -reveal), fr.pt(*a, -reveal)], CREAM, inward)
    outer = arc(uc, vs, r + ring, n); inner = pts                                                                                    # the stone arch ring, a half annulus
    poly = [q for q in inner] + [q for q in outer[::-1]]
    extrude(p, fr, poly, 0.0, ring_w, STONE, caps=True)
    fb(p, fr, uc - 0.16, uc + 0.16, vs + r - 0.02, vs + r + ring + 0.17, 0.0, ring_w + 0.04, STONE, skip=("n-",))                       # keystone


def pediment(p, fr, u0, u1, v, h=0.30, w=0.07, segmental=False):
    """A triangular pediment (cream) over a window head: base u0..u1 at height v, apex h above."""
    extrude(p, fr, [(u0, v), (u1, v), ((u0 + u1) / 2, v + h)], 0.0, w, CREAM)


def lantern_on(p, fr, u, v, w=0.0):
    """A small wall lantern on a facade: back plate, arm, a warm glass box and a cap (about 30 triangles)."""
    fb(p, fr, u - 0.05, u + 0.05, v, v + 0.30, w, w + 0.012, "metal_gate_black"); fb(p, fr, u - 0.07, u + 0.07, v + 0.04, v + 0.22, w + 0.012, w + 0.15, "lantern_glow", skip=("n-", "v-"))
    fb(p, fr, u - 0.09, u + 0.09, v + 0.22, v + 0.26, w + 0.01, w + 0.17, "metal_gate_black", skip=("n-", "v-"))


def column(p, x, y, z0, z1, r=0.20):
    """A round column: base block, a twelve-sided shaft, capital block (about 56 triangles)."""
    p.box(x - 0.30, y - 0.30, z0, x + 0.30, y + 0.30, z0 + 0.22, STONE, skip=("bottom",)); p.tube((x, y, z0 + 0.22), (x, y, z1 - 0.20), r, r * 0.92, 12, CREAM, caps=(False, False), smooth=False)
    p.box(x - 0.28, y - 0.28, z1 - 0.20, x + 0.28, y + 0.28, z1, STONE, skip=("bottom",))


def facade3(p, fr, W, H, openings, key_fn, reveal=0.16, lod=0, sills=True, split_u=(), split_v=(), lit=()):
    """villa.facade with slender parts reduced to their visible faces, an `arch` opening kind (the rectangle of a door below a round head) and `lit` openings (indices) with a warm room behind."""
    us = sorted({0.0, W, *split_u, *[o[0] for o in openings], *[o[1] for o in openings]}); vs = sorted({0.0, H, *split_v, *[o[2] for o in openings], *[o[3] for o in openings]})
    inside = lambda uc, vc: any(o[0] < uc < o[1] and o[2] < vc < o[3] for o in openings)
    for i in range(len(us) - 1):
        for j in range(len(vs) - 1):
            uc, vc = (us[i] + us[i + 1]) / 2, (vs[j] + vs[j + 1]) / 2
            if inside(uc, vc): continue
            p.face_n([fr.pt(us[i], vs[j]), fr.pt(us[i + 1], vs[j]), fr.pt(us[i + 1], vs[j + 1]), fr.pt(us[i], vs[j + 1])], key_fn(uc, vc), fr.n)
    for idx, (u0, u1, v0, v1, kind) in enumerate(openings):
        if kind == "fanhole": continue                                                                                # filled by arched_head
        if lod >= 1:
            p.face_n([fr.pt(u0, v0, -0.03), fr.pt(u1, v0, -0.03), fr.pt(u1, v1, -0.03), fr.pt(u0, v1, -0.03)], GLASS, fr.n); continue
        r = reveal; wk = CREAM; arch = kind == "archdoor"; kind = "pair" if arch else kind; room = "interior_warm" if idx in lit else "interior_dark"
        p.face_n([fr.pt(u0, v0), fr.pt(u1, v0), fr.pt(u1, v0, -r), fr.pt(u0, v0, -r)], wk, fr.v)                                  # sill
        if not arch: p.face_n([fr.pt(u0, v1), fr.pt(u1, v1), fr.pt(u1, v1, -r), fr.pt(u0, v1, -r)], wk, -fr.v)                  # head
        p.face_n([fr.pt(u0, v0), fr.pt(u0, v1), fr.pt(u0, v1, -r), fr.pt(u0, v0, -r)], wk, fr.u); p.face_n([fr.pt(u1, v0), fr.pt(u1, v1), fr.pt(u1, v1, -r), fr.pt(u1, v0, -r)], wk, -fr.u)
        if kind == "garage":
            p.face_n([fr.pt(u0, v0, -r + 0.05), fr.pt(u1, v0, -r + 0.05), fr.pt(u1, v1, -r + 0.05), fr.pt(u0, v1, -r + 0.05)], "metal_galvanised", fr.n)
            for k in range(1, 12): fb(p, fr, u0, u1, v0 + k * (v1 - v0) / 12 - 0.012, v0 + k * (v1 - v0) / 12 + 0.012, -r + 0.05, -r + 0.075, "metal_galvanised", skip=("n-", "u+", "u-"))
            fb(p, fr, u0 - 0.06, u1 + 0.06, v1, v1 + 0.10, 0.0, 0.06, GREY); continue
        f = 0.07; w0, w1 = -r + 0.02, -r + 0.09
        fb(p, fr, u0, u1, v0, v0 + f, w0, w1, FRAME, ("u+", "u-", "v-", "n-")); fb(p, fr, u0, u1, v1 - f, v1, w0, w1, FRAME, ("u+", "u-", "v+", "n-") if not arch else ("u+", "u-", "v+", "n-"))
        fb(p, fr, u0, u0 + f, v0 + f, v1 - f, w0, w1, FRAME, ("u-", "v+", "v-", "n-")); fb(p, fr, u1 - f, u1, v0 + f, v1 - f, w0, w1, FRAME, ("u+", "v+", "v-", "n-"))
        if kind in ("door", "pair") or (u1 - u0) > 1.6: fb(p, fr, (u0 + u1) / 2 - f / 2, (u0 + u1) / 2 + f / 2, v0 + f, v1 - f, w0, w1, FRAME, ("v+", "v-", "n-"))
        if kind == "window" and (v1 - v0) > 1.5: fb(p, fr, u0 + f, u1 - f, (v0 + v1) / 2 - 0.025, (v0 + v1) / 2 + 0.025, w0, w1, FRAME, ("u+", "u-", "n-"))
        p.face_n([fr.pt(u0 + f, v0 + f, -r + 0.05), fr.pt(u1 - f, v0 + f, -r + 0.05), fr.pt(u1 - f, v1 - f, -r + 0.05), fr.pt(u0 + f, v1 - f, -r + 0.05)], GLASS, fr.n)
        d = r + 0.45
        p.face_n([fr.pt(u0, v0, -d), fr.pt(u1, v0, -d), fr.pt(u1, v1, -d), fr.pt(u0, v1, -d)], room, fr.n)
        p.face_n([fr.pt(u0, v0, -r), fr.pt(u1, v0, -r), fr.pt(u1, v0, -d), fr.pt(u0, v0, -d)], room, fr.v)
        if not arch: p.face_n([fr.pt(u0, v1, -r), fr.pt(u1, v1, -r), fr.pt(u1, v1, -d), fr.pt(u0, v1, -d)], room, -fr.v)
        p.face_n([fr.pt(u0, v0, -r), fr.pt(u0, v1, -r), fr.pt(u0, v1, -d), fr.pt(u0, v0, -d)], room, fr.u); p.face_n([fr.pt(u1, v0, -r), fr.pt(u1, v1, -r), fr.pt(u1, v1, -d), fr.pt(u1, v0, -d)], room, -fr.u)
        if kind == "window":                                                                                                                         # raised cream architrave, a keystone over upper windows, security grille on ground windows
            a_ = 0.11; fb(p, fr, u0 - a_, u0, v0 - 0.06, v1 + 0.10, 0.0, 0.035, CREAM); fb(p, fr, u1, u1 + a_, v0 - 0.06, v1 + 0.10, 0.0, 0.035, CREAM)
            fb(p, fr, u0 - a_, u1 + a_, v1 + 0.08, v1 + 0.19, 0.0, 0.05, CREAM)
            if v0 > 3.0: fb(p, fr, (u0 + u1) / 2 - 0.13, (u0 + u1) / 2 + 0.13, v1 + 0.19, v1 + 0.34, 0.0, 0.065, STONE)
            if v0 < 1.5:
                gw0 = -r + 0.12; gw1 = gw0 + 0.014; n_b = max(3, int((u1 - u0) / 0.19))
                for kk in range(1, n_b): x_ = u0 + kk * (u1 - u0) / n_b; fb(p, fr, x_ - 0.007, x_ + 0.007, v0 + f, v1 - f, gw0, gw1, "metal_gate_black", ("v+", "v-", "n-"))
                fb(p, fr, u0 + f, u1 - f, v0 + f, v0 + f + 0.03, gw0, gw1, "metal_gate_black", ("u+", "u-", "n-")); fb(p, fr, u0 + f, u1 - f, v1 - f - 0.03, v1 - f, gw0, gw1, "metal_gate_black", ("u+", "u-", "n-"))
        if sills and kind == "window": fb(p, fr, u0 - 0.07, u1 + 0.07, v0 - 0.06, v0, 0.0, 0.08, WHITE)
        if sills and not arch: fb(p, fr, u0 - 0.05, u1 + 0.05, v1, v1 + 0.08, 0.0, 0.05, WHITE)
        if kind in ("door", "pair") and v0 < 0.05: fb(p, fr, u0 - 0.1, u1 + 0.1, 0.0, 0.12, 0.0, 0.30, GREY)


def balcony3(p, x0, x1, y_front, y_wall, z_slab, lod=0):
    """The balcony of villa v002 with round columns (base, shaft, capital) instead of the square ones, a stepped cornice under the slab and the glass balustrade."""
    p.box(x0 - 0.1, y_front - 0.1, z_slab, x1 + 0.1, y_wall, z_slab + 0.30, CREAM, skip=("back",))
    p.box(x0 - 0.1, y_front - 0.1, z_slab - 0.10, x1 + 0.1, y_wall, z_slab, WHITE, skip=("back", "top"))
    if lod == 0: p.box(x0 - 0.04, y_front - 0.04, z_slab - 0.22, x1 + 0.04, y_wall, z_slab - 0.10, STONE, skip=("back", "top"))
    for cx in (x0 + 0.25, x1 - 0.25): column(p, cx, y_front + 0.30, 0.12, z_slab - 0.22 if lod == 0 else z_slab, 0.19)
    p.box(x0 - 0.1, y_front - 0.1, 0.0, x1 + 0.1, y_wall, 0.12, STONE, skip=("bottom", "back"))
    if lod >= 1: return
    zt = z_slab + 0.30; zb = zt + 1.05
    p.box(x0, y_front - 0.01, zt + 0.06, x1, y_front + 0.01, zb - 0.04, "glass"); p.box(x0 - 0.01, y_front, zt + 0.06, x0 + 0.01, y_wall, zb - 0.04, "glass"); p.box(x1 - 0.01, y_front, zt + 0.06, x1 + 0.01, y_wall, zb - 0.04, "glass")
    for (ax, ay, bx, by) in [(x0, y_front, x1, y_front), (x0, y_front, x0, y_wall), (x1, y_front, x1, y_wall)]: p.tube((ax, ay, zb), (bx, by, zb), 0.03, 0.03, 6, "metal_galvanised")
    for (x, y) in [(x0, y_front), (x1, y_front), ((x0 + x1) / 2, y_front), (x0, y_wall - 0.02), (x1, y_wall - 0.02)]: p.tube((x, y, zt), (x, y, zb), 0.025, 0.025, 6, "metal_galvanised")


def quoins(p, sx, y_face, left, n=12):
    """Alternating stone blocks up a front corner (front face and side face)."""
    for k in range(n):
        z0 = 0.4 + k * 0.54; w = 0.50 if k % 2 == 0 else 0.30
        p.box(sx - 0.04 if left else sx - w, y_face - 0.04, z0, sx + w if left else sx + 0.04, y_face, z0 + 0.50, STONE, skip=("bottom", "back"))
        p.box(sx - 0.04 if left else sx, y_face, z0, sx if left else sx + 0.04, y_face + w, z0 + 0.50, STONE, skip=("bottom", "right" if left else "left"))


def villa_a3(lod: int = 0) -> Part:
    """Villa type A, v003: the v002 massing and openings with a stone-clad arched entrance, round columns, stone pilasters and proud grey cladding panels with joints on the front, pediments over the ground-floor
    windows, wall lanterns beside the door and a warmly lit window."""
    p = Part("villa"); X0, X1, Y0, Y1 = -6.0, 6.0, -5.0, 5.0; H = 6.9
    if lod >= 2:
        p.box(X0, Y0, 0, X1, Y1, H, CREAM); hip_roof(p, X0 - 0.7, X1 + 0.7, Y0 - 0.7, Y1 + 0.7, H, 9.4, 2.2, 1, tiles=False); return p
    key_front = lambda u, v: STONE if (0.55 < u < 5.05 and v < 3.55) else GREY if (v < 0.40 or 5.5 < u < 6.6) else CREAM          # the entrance bay is stone-clad to the first floor
    key_side = lambda u, v: GREY if v < 0.40 else CREAM
    door = (1.9, 3.7, 0.0, 2.35, "archdoor"); fan = (1.9, 3.7, 2.35, 3.25, "fanhole"); bdoor = (2.0, 3.8, 3.95, 6.05, "pair")
    front = [door, fan, bdoor, (7.0, 9.4, 0.95, 2.80, "window"), (10.0, 11.3, 0.95, 2.80, "window"), (7.0, 9.4, 4.40, 6.10, "window"), (10.0, 11.3, 4.40, 6.10, "window"), (5.78, 6.32, 1.0, 2.7, "window"), (5.78, 6.32, 4.4, 6.1, "window")]
    FF = Fr((X0, Y0, 0), (1, 0, 0), (0, -1, 0))
    facade3(p, FF, 12.0, H, front, key_front, lod=lod, split_u=(0.55, 5.05, 5.5, 6.6), split_v=(0.40, 3.55, 3.85), lit=(3,))
    arched_head(p, FF, 1.9, 3.7, 2.35, lod=lod, lit=False)
    side = [(1.4, 3.2, 0.95, 2.80, "window"), (5.4, 7.2, 0.95, 2.80, "window"), (1.4, 3.2, 4.40, 6.10, "window"), (5.4, 7.2, 4.40, 6.10, "window"), (8.3, 9.3, 4.9, 6.1, "window")]
    facade3(p, Fr((X0, Y0, 0), (0, 1, 0), (-1, 0, 0)), 10.0, H, side, key_side, lod=lod, split_v=(0.40,))
    facade3(p, Fr((X1, Y0, 0), (0, 1, 0), (1, 0, 0)), 10.0, H, [(1.4, 3.2, 0.95, 2.80, "window"), (6.8, 8.6, 0.95, 2.80, "window"), (1.4, 3.2, 4.40, 6.10, "window"), (6.8, 8.6, 4.40, 6.10, "window")], key_side, lod=lod, split_v=(0.40,))
    facade3(p, Fr((X1, Y1, 0), (-1, 0, 0), (0, 1, 0)), 12.0, H, [(2.0, 3.6, 0.95, 2.8, "window"), (8.0, 9.6, 0.95, 2.8, "window"), (2.0, 3.6, 4.4, 6.1, "window"), (5.4, 7.0, 4.4, 6.1, "window"), (8.0, 9.6, 4.4, 6.1, "window")], key_side, lod=lod, split_v=(0.40,))
    if lod == 0:
        p.box(X0 - 0.04, Y0 - 0.05, 3.55, X1 + 0.04, Y0, 3.85, WHITE, skip=("bottom",))                                                                     # string course
        for cx in (X0 + 0.02, X1 - 0.02): p.tube((cx, Y0 - 0.12, 0.15), (cx, Y0 - 0.12, H - 0.05), 0.045, 0.045, 8, "metal_galvanised")                      # downpipes
        for sx in (X0, X1): quoins(p, sx, Y0, sx < 0)
        # front richness: stone pilasters flanking the entrance bay, grey cladding panels (proud, with joints) round the right-hand windows, pediments over the ground windows, lanterns beside the door
        for (u0, u1) in ((0.55, 1.0), (4.6, 5.05)):
            for (v0, v1) in ((0.40, 3.50), (3.90, 6.50)): fb(p, FF, u0, u1, v0, v1, 0.0, 0.06, CREAM if v0 < 1 else STONE, ("n-", "v-"))
        for (u0, u1) in ((9.52, 9.88), (11.45, 11.88)):
            for (v0, v1) in ((0.45, 2.95), (3.95, 6.45)): fb(p, FF, u0, u1, v0, v1, 0.0, 0.045, GREY, ("n-", "v-"))
            for v in (1.2, 1.95, 4.7, 5.45): fb(p, FF, u0 + 0.02, u1 - 0.02, v, v + 0.016, 0.045, 0.052, STONE, ("n-", "u+", "u-"))                      # joints
        pediment(p, FF, 6.78, 9.62, 3.00, 0.34); pediment(p, FF, 9.78, 11.52, 3.00, 0.28)
        lantern_on(p, FF, 1.45, 1.55); lantern_on(p, FF, 4.15, 1.55)
        p.box(-4.3, 1.1, 7.2, -3.5, 1.9, 10.3, STONE); p.box(-4.4, 1.0, 10.3, -3.4, 2.0, 10.45, CREAM)                                                      # chimney
        p.box(X1 + 0.0, -1.0, 4.2, X1 + 0.32, -0.2, 4.75, "paint_white", skip=("left",)); p.box(X1 + 0.32, -0.95, 4.28, X1 + 0.34, -0.25, 4.67, "metal_galvanised", skip=("left",))   # air-conditioner
        p.box(X0 - 0.15, Y0 - 0.15, 6.52, X1 + 0.15, Y1 + 0.15, 6.60, CREAM, skip=("bottom",)); p.box(X0 - 0.26, Y0 - 0.26, 6.60, X1 + 0.26, Y1 + 0.26, 6.74, CREAM, skip=("bottom",))   # cornice
        gable_dormer(p, 3.0, -3.4)
    balcony3(p, -5.0, -0.8, -7.0, Y0, 3.55, lod)
    hip_roof(p, X0 - 0.7, X1 + 0.7, Y0 - 0.7, Y1 + 0.7, H, 9.4, 2.2, 18 if lod == 0 else 8, tiles=True)
    return p


def villa_b3(lod: int = 0) -> Part:
    """Villa type B, v003: the v002 massing (main block, projecting gabled bay, garage wing) with an arched fanlight over the stone-clad entrance, stone pilasters at the bay corners, an oculus in the gable,
    pediments over the ground-floor windows, quoins on the main block and cladding panels with joints on the garage wing."""
    p = Part("villa"); X0, X1, Y0, Y1 = -5.5, 5.5, -4.8, 4.8; H = 6.9; BX0, BX1, BY = 0.4, 5.4, -6.8
    if lod >= 2:
        p.box(X0, Y0, 0, X1, Y1, H, CREAM); p.box(-10, -4, 0, X0, 3.5, 3.4, CREAM); hip_roof(p, X0 - 0.6, X1 + 0.6, Y0 - 0.6, Y1 + 0.6, H, 9.0, 2.0, 1, tiles=False); return p
    kf = lambda u, v: STONE if v < 0.9 else CREAM; kg = lambda u, v: GREY if v < 0.4 else CREAM
    facade3(p, Fr((X0, Y0, 0), (1, 0, 0), (0, -1, 0)), BX0 - X0, H, [(1.0, 3.0, 0.95, 2.80, "window"), (1.0, 3.0, 4.40, 6.10, "window"), (3.7, 4.9, 0.95, 2.80, "window"), (3.7, 4.9, 4.40, 6.10, "window")], kf, lod=lod, split_v=(0.9,))
    facade3(p, Fr((BX1, Y0, 0), (1, 0, 0), (0, -1, 0)), X1 - BX1, H, [], kf, lod=lod, split_v=(0.9,))
    facade3(p, Fr((X0, Y0, 0), (0, 1, 0), (-1, 0, 0)), Y1 - Y0, H, [(1.2, 3.0, 0.95, 2.8, "window"), (5.2, 7.0, 0.95, 2.8, "window"), (1.2, 3.0, 4.4, 6.1, "window"), (5.2, 7.0, 4.4, 6.1, "window"), (8.0, 9.0, 4.9, 6.1, "window")], kg, lod=lod, split_v=(0.4,))
    facade3(p, Fr((X1, Y0, 0), (0, 1, 0), (1, 0, 0)), Y1 - Y0, H, [(1.2, 3.0, 0.95, 2.8, "window"), (5.2, 7.0, 0.95, 2.8, "window"), (1.2, 3.0, 4.4, 6.1, "window"), (5.2, 7.0, 4.4, 6.1, "window")], kg, lod=lod, split_v=(0.4,))
    facade3(p, Fr((X1, Y1, 0), (-1, 0, 0), (0, 1, 0)), X1 - X0, H, [(1.5, 3.1, 0.95, 2.8, "window"), (5.5, 7.1, 0.95, 2.8, "window"), (1.5, 3.1, 4.4, 6.1, "window"), (5.5, 7.1, 4.4, 6.1, "window")], kg, lod=lod, split_v=(0.4,))
    bay_h = 6.4; BF = Fr((BX0, BY, 0), (1, 0, 0), (0, -1, 0))
    facade3(p, BF, BX1 - BX0, bay_h, [(0.8, 2.6, 0.0, 2.35, "archdoor"), (0.8, 2.6, 2.35, 3.25, "fanhole"), (3.0, 4.2, 0.95, 2.8, "window"), (0.7, 4.3, 3.9, 5.9, "window")], kf, lod=lod, split_v=(0.9,), lit=(3,))
    arched_head(p, BF, 0.8, 2.6, 2.35, lod=lod, lit=True)
    facade3(p, Fr((BX0, BY, 0), (0, 1, 0), (-1, 0, 0)), Y0 - BY, bay_h, [], lambda u, v: STONE if v < 0.9 else CREAM, lod=lod, split_v=(0.9,))
    facade3(p, Fr((BX1, BY, 0), (0, 1, 0), (1, 0, 0)), Y0 - BY, bay_h, [], lambda u, v: STONE if v < 0.9 else CREAM, lod=lod, split_v=(0.9,))
    if lod == 0:
        p.box(BX0 - 0.15, BY - 0.15, 3.55, BX1 + 0.15, BY + 0.0, 3.85, WHITE, skip=("bottom",))
        for (u0, u1) in ((-0.05, 0.40), (4.62, 5.07)): fb(p, BF, u0, u1, 0.9, 3.50, 0.0, 0.07, STONE, ("n-", "v-")); fb(p, BF, u0, u1, 3.90, 6.20, 0.0, 0.07, STONE, ("n-", "v-"))        # pilasters at the bay corners
        pediment(p, BF, 2.78, 4.42, 3.00, 0.30)
        lantern_on(p, BF, 3.0, 1.6); lantern_on(p, BF, -0.55, 1.6)
        for sx in (X0, X1): quoins(p, sx, Y0, sx < 0, 9)
        for cx in (X0 + 0.02, X1 - 0.02): p.tube((cx, Y0 - 0.12, 0.15), (cx, Y0 - 0.12, H - 0.05), 0.045, 0.045, 8, "metal_galvanised")
        p.box(BX0 + 0.4, BY - 0.5, 0.0, BX1 - 1.6, BY, 0.07, STONE, skip=("bottom",))
    p.box(BX0 + 0.6, BY - 0.9, 0.0, BX1 - 1.8, BY, 0.14, STONE, skip=("bottom", "back"))
    gable_roof(p, BX0 - 0.35, BX1 + 0.35, BY - 0.4, Y0 + 2.2, bay_h, 8.5, 9 if lod == 0 else 5)
    if lod == 0:                                                                                                                # an oculus in the front gable: stone ring, glass disc
        xm = (BX0 + BX1) / 2; zc = bay_h + 0.78; yo = BY - 0.4 - 0.02
        p.tube((xm, yo - 0.05, zc), (xm, yo + 0.02, zc), 0.34, 0.34, 16, STONE, caps=(False, False), smooth=False)
        p.face_n([(xm + 0.27 * math.cos(2 * math.pi * k / 16), yo - 0.055, zc + 0.27 * math.sin(2 * math.pi * k / 16)) for k in range(16)], GLASS, (0, -1, 0))
    hip_roof(p, X0 - 0.6, X1 + 0.6, Y0 - 0.6, Y1 + 0.6, H, 9.0, 2.0, 18 if lod == 0 else 8, tiles=True)
    GX0, GX1, GY0, GY1, GH = -10.2, X0, -4.0, 3.6, 3.4
    GF = Fr((GX0, GY0, 0), (1, 0, 0), (0, -1, 0))
    facade3(p, GF, GX1 - GX0, GH, [(0.7, 3.9, 0.0, 2.45, "garage")], lambda u, v: GREY if v < 0.4 else CREAM, lod=lod, split_v=(0.4,), sills=False)
    facade3(p, Fr((GX0, GY0, 0), (0, 1, 0), (-1, 0, 0)), GY1 - GY0, GH, [(2.8, 4.0, 1.2, 2.4, "window")], lambda u, v: GREY if v < 0.4 else CREAM, lod=lod, split_v=(0.4,), sills=(lod == 0))
    facade3(p, Fr((GX1, GY1, 0), (-1, 0, 0), (0, 1, 0)), GX1 - GX0, GH, [], lambda u, v: CREAM, lod=lod)
    if lod == 0:
        for (u0, u1) in ((4.15, 4.55), (4.75, 5.15)):                                                                          # cladding panels beside the garage door, with joints
            fb(p, GF, u0, u1, 0.45, 2.95, 0.0, 0.045, GREY, ("n-", "v-"))
            for v in (1.0, 1.55, 2.1, 2.55): fb(p, GF, u0 + 0.02, u1 - 0.02, v, v + 0.016, 0.045, 0.052, STONE, ("n-", "u+", "u-"))
    p.face_n([(GX0, GY0, GH), (GX1, GY0, GH), (GX1, GY1, GH), (GX0, GY1, GH)], WHITE, (0, 0, 1))
    for (a, b, c, d) in [(GX0, GY0, GX1, GY0 + 0.15), (GX0, GY1 - 0.15, GX1, GY1), (GX0, GY0, GX0 + 0.15, GY1), (GX1 - 0.15, GY0, GX1, GY1)]: p.box(a, b, GH, c, d, GH + 0.45, CREAM)
    return p
