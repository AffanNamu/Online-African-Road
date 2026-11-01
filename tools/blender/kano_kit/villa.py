"""Two-storey villas for the residential street (concept R-006): rendered cream walls with grey stone-effect cladding, dark hipped tile roof with real tile courses, recessed windows with
frames, sills and lintels, a balcony with a glass balustrade over a columned porch. Asset front is -Y, origin on the ground at the centre of the footprint."""
from __future__ import annotations
import math
from mathutils import Vector
from .geo import Part

CREAM, GREY, STONE, ROOF, FRAME, GLASS, WHITE = "plaster_cream_light", "cladding_grey", "stone_travertine", "roof_tile_charcoal", "frame_aluminium_dark", "glass_dark", "paint_white"
DIRNAME = {(1, 0, 0): "right", (-1, 0, 0): "left", (0, 1, 0): "back", (0, -1, 0): "front", (0, 0, 1): "top", (0, 0, -1): "bottom"}


class Fr:
    """A facade frame: origin, u (along the wall), v (up), n (outward). All three are axis vectors, so boxes in the frame stay axis aligned."""
    def __init__(self, o, u, n): self.o = Vector(o); self.u = Vector(u); self.v = Vector((0, 0, 1)); self.n = Vector(n)
    def pt(self, u, v, w=0.0): return self.o + self.u * u + self.v * v + self.n * w


def fbox(p, fr, u0, u1, v0, v1, w0, w1, key, skip_inside=True):
    a = fr.pt(u0, v0, w0); b = fr.pt(u1, v1, w1); skip = (DIRNAME[tuple(int(round(c)) for c in -fr.n)],) if skip_inside else ()
    p.box(min(a.x, b.x), min(a.y, b.y), min(a.z, b.z), max(a.x, b.x), max(a.y, b.y), max(a.z, b.z), key, skip=skip)


def facade(p, fr, W, H, openings, key_fn, reveal=0.16, lod=0, sills=True, split_u=(), split_v=()):
    """A wall as a grid of cells with the openings left out; recessed reveals, frame, glass, sill and lintel for each opening."""
    us = sorted({0.0, W, *split_u, *[o[0] for o in openings], *[o[1] for o in openings]}); vs = sorted({0.0, H, *split_v, *[o[2] for o in openings], *[o[3] for o in openings]})
    inside = lambda uc, vc: any(o[0] < uc < o[1] and o[2] < vc < o[3] for o in openings)
    for i in range(len(us) - 1):
        for j in range(len(vs) - 1):
            uc, vc = (us[i] + us[i + 1]) / 2, (vs[j] + vs[j + 1]) / 2
            if inside(uc, vc): continue
            p.face_n([fr.pt(us[i], vs[j]), fr.pt(us[i + 1], vs[j]), fr.pt(us[i + 1], vs[j + 1]), fr.pt(us[i], vs[j + 1])], key_fn(uc, vc), fr.n)
    for (u0, u1, v0, v1, kind) in openings:
        if lod >= 1:
            p.face_n([fr.pt(u0, v0, -0.03), fr.pt(u1, v0, -0.03), fr.pt(u1, v1, -0.03), fr.pt(u0, v1, -0.03)], GLASS, fr.n); continue
        r = reveal; wk = CREAM
        p.face_n([fr.pt(u0, v0), fr.pt(u1, v0), fr.pt(u1, v0, -r), fr.pt(u0, v0, -r)], wk, fr.v)             # sill (faces up)
        p.face_n([fr.pt(u0, v1), fr.pt(u1, v1), fr.pt(u1, v1, -r), fr.pt(u0, v1, -r)], wk, -fr.v)            # head (faces down)
        p.face_n([fr.pt(u0, v0), fr.pt(u0, v1), fr.pt(u0, v1, -r), fr.pt(u0, v0, -r)], wk, fr.u)             # left jamb (faces +u)
        p.face_n([fr.pt(u1, v0), fr.pt(u1, v1), fr.pt(u1, v1, -r), fr.pt(u1, v0, -r)], wk, -fr.u)            # right jamb (faces -u)
        if kind == "garage":                                                                                       # roller shutter: galvanised panel with ribs, recessed in the reveal
            p.face_n([fr.pt(u0, v0, -r + 0.05), fr.pt(u1, v0, -r + 0.05), fr.pt(u1, v1, -r + 0.05), fr.pt(u0, v1, -r + 0.05)], "metal_galvanised", fr.n)
            for k in range(1, 12): fbox(p, fr, u0, u1, v0 + k * (v1 - v0) / 12 - 0.012, v0 + k * (v1 - v0) / 12 + 0.012, -r + 0.05, -r + 0.075, "metal_galvanised")
            fbox(p, fr, u0 - 0.06, u1 + 0.06, v1, v1 + 0.10, 0.0, 0.06, GREY); continue
        f = 0.07; w0, w1 = -r + 0.02, -r + 0.09                                                                 # aluminium frame, set back in the reveal
        fbox(p, fr, u0, u1, v0, v0 + f, w0, w1, FRAME); fbox(p, fr, u0, u1, v1 - f, v1, w0, w1, FRAME)
        fbox(p, fr, u0, u0 + f, v0 + f, v1 - f, w0, w1, FRAME); fbox(p, fr, u1 - f, u1, v0 + f, v1 - f, w0, w1, FRAME)
        if kind in ("door", "pair") or (u1 - u0) > 1.6: fbox(p, fr, (u0 + u1) / 2 - f / 2, (u0 + u1) / 2 + f / 2, v0 + f, v1 - f, w0, w1, FRAME)      # centre mullion / meeting stile
        if kind == "window" and (v1 - v0) > 1.5: fbox(p, fr, u0 + f, u1 - f, (v0 + v1) / 2 - 0.025, (v0 + v1) / 2 + 0.025, w0, w1, FRAME)             # transom
        p.face_n([fr.pt(u0 + f, v0 + f, -r + 0.05), fr.pt(u1 - f, v0 + f, -r + 0.05), fr.pt(u1 - f, v1 - f, -r + 0.05), fr.pt(u0 + f, v1 - f, -r + 0.05)], GLASS, fr.n)
        if sills and kind == "window": fbox(p, fr, u0 - 0.07, u1 + 0.07, v0 - 0.06, v0, 0.0, 0.08, WHITE)                                                 # projecting sill
        if sills: fbox(p, fr, u0 - 0.05, u1 + 0.05, v1, v1 + 0.08, 0.0, 0.05, WHITE)                                                                       # lintel band
        if kind in ("door", "pair") and v0 < 0.05: fbox(p, fr, u0 - 0.1, u1 + 0.1, 0.0, 0.12, 0.0, 0.30, GREY)                                             # door step


def roof_slope(p, A, B, D, C, courses, key, up_hint, thick=0.045, tiles=True):
    """A roof plane A-B (eave) to D-C (ridge; C == D for a hip triangle) with sawtooth tile courses: each course steps up `thick` at its lower edge."""
    A, B, D, C = map(Vector, (A, B, D, C)); nrm = (B - A).cross(D - A).normalized()
    if nrm.dot(Vector(up_hint)) < 0: nrm = -nrm
    down = (A - D).normalized()
    def P(s, t, off=0.0):
        lo = A.lerp(B, t); hi = D.lerp(C, t); return lo.lerp(hi, s) + nrm * off
    if not tiles:
        pts = [A, B, C, D] if (C - D).length > 1e-6 else [A, B, D]; p.face_n(pts, key, nrm); return
    N = courses
    for k in range(N):
        s0, s1 = k / N, (k + 1) / N
        tri = (C - D).length < 1e-6 and k == N - 1
        top = [P(s0, 0, thick), P(s0, 1, thick), P(s1, 1, 0.0), P(s1, 0, 0.0)] if not tri else [P(s0, 0, thick), P(s0, 1, thick), P(1.0, 0.5, 0.0)]
        p.face_n(top, key, nrm)
        p.face_n([P(s0, 0, 0.0), P(s0, 1, 0.0), P(s0, 1, thick), P(s0, 0, thick)], key, down)                  # riser facing the eave


def hip_roof(p, x0, x1, y0, y1, z_eave, z_ridge, rx, courses=18, tiles=True):
    """Hipped roof over the eave rectangle with the ridge from -rx to +rx at y = 0; the slopes get tile courses, ridge and hips get capping."""
    ym = (y0 + y1) / 2; e = [(x0, y0, z_eave), (x1, y0, z_eave), (x1, y1, z_eave), (x0, y1, z_eave)]; rl = (-rx, ym, z_ridge); rr = (rx, ym, z_ridge)
    roof_slope(p, e[0], e[1], rl, rr, courses, ROOF, (0, -1, 1), tiles=tiles)         # front
    roof_slope(p, e[1], e[2], rr, rr, courses, ROOF, (1, 0, 1), tiles=tiles)          # right hip
    roof_slope(p, e[2], e[3], rr, rl, courses, ROOF, (0, 1, 1), tiles=tiles)          # back
    roof_slope(p, e[3], e[0], rl, rl, courses, ROOF, (-1, 0, 1), tiles=tiles)         # left hip
    if tiles:
        p.tube((rl[0] - 0.05, rl[1], rl[2] + 0.05), (rr[0] + 0.05, rr[1], rr[2] + 0.05), 0.09, 0.09, 4, ROOF, smooth=False, roll=math.pi / 4)
        for c, r_ in ((e[0], rl), (e[1], rr), (e[2], rr), (e[3], rl)): p.tube((c[0], c[1], c[2] + 0.04), (r_[0], r_[1], r_[2] + 0.05), 0.07, 0.07, 4, ROOF, smooth=False, roll=math.pi / 4)
    # fascia and soffit
    t = 0.10; zf0, zf1 = z_eave - 0.14, z_eave + 0.10
    p.box(x0, y0 - t, zf0, x1, y0, zf1, WHITE, skip=("bottom",)); p.box(x0, y1, zf0, x1, y1 + t, zf1, WHITE, skip=("bottom",))
    p.box(x0 - t, y0, zf0, x0, y1, zf1, WHITE, skip=("bottom",)); p.box(x1, y0, zf0, x1 + t, y1, zf1, WHITE, skip=("bottom",))


def balcony(p, x0, x1, y_front, y_wall, z_slab, with_columns=True, lod=0):
    """Projecting balcony slab with a glass balustrade (steel posts and top rail) over a porch with two square columns."""
    p.box(x0 - 0.1, y_front - 0.1, z_slab, x1 + 0.1, y_wall, z_slab + 0.30, CREAM, skip=("back",))
    p.box(x0 - 0.1, y_front - 0.1, z_slab - 0.10, x1 + 0.1, y_wall, z_slab, WHITE, skip=("back", "top"))
    if with_columns:
        for cx in (x0 + 0.05, x1 - 0.45):
            p.box(cx, y_front + 0.10, 0.12, cx + 0.40, y_front + 0.50, z_slab, CREAM, skip=("bottom", "top")); p.box(cx - 0.05, y_front + 0.05, 0.12, cx + 0.45, y_front + 0.55, 0.30, STONE, skip=("bottom",))
    p.box(x0 - 0.1, y_front - 0.1, 0.0, x1 + 0.1, y_wall, 0.12, STONE, skip=("bottom", "back"))                   # porch floor
    if lod >= 1: return
    zt = z_slab + 0.30; zb = zt + 1.05
    p.box(x0, y_front - 0.01, zt + 0.06, x1, y_front + 0.01, zb - 0.04, "glass"); p.box(x0 - 0.01, y_front, zt + 0.06, x0 + 0.01, y_wall, zb - 0.04, "glass"); p.box(x1 - 0.01, y_front, zt + 0.06, x1 + 0.01, y_wall, zb - 0.04, "glass")
    for (ax, ay, bx, by) in [(x0, y_front, x1, y_front), (x0, y_front, x0, y_wall), (x1, y_front, x1, y_wall)]: p.tube((ax, ay, zb), (bx, by, zb), 0.03, 0.03, 6, "metal_galvanised")
    for (x, y) in [(x0, y_front), (x1, y_front), ((x0 + x1) / 2, y_front), (x0, y_wall - 0.02), (x1, y_wall - 0.02)]: p.tube((x, y, zt), (x, y, zb), 0.025, 0.025, 6, "metal_galvanised")


def villa_a(lod: int = 0) -> Part:
    """Villa type A: 12 x 10 m, ground floor 3.2 m plus 2.8 m, eave 6.9 m, ridge 9.4 m; columned porch with a balcony on the left, grey cladding band, two windows on the right."""
    p = Part("villa"); X0, X1, Y0, Y1 = -6.0, 6.0, -5.0, 5.0; H = 6.9
    if lod >= 2:
        p.box(X0, Y0, 0, X1, Y1, H, CREAM); hip_roof(p, X0 - 0.7, X1 + 0.7, Y0 - 0.7, Y1 + 0.7, H, 9.4, 2.2, 1, tiles=False); return p
    key_front = lambda u, v: GREY if (v < 0.40 or 5.5 < u < 6.6) else CREAM
    key_side = lambda u, v: GREY if v < 0.40 else CREAM
    door = (1.9, 3.7, 0.0, 2.35, "pair"); bdoor = (2.0, 3.8, 3.95, 6.05, "pair")
    front = [door, bdoor, (7.0, 9.4, 0.95, 2.80, "window"), (10.0, 11.3, 0.95, 2.80, "window"), (7.0, 9.4, 4.40, 6.10, "window"), (10.0, 11.3, 4.40, 6.10, "window"), (5.78, 6.32, 1.0, 2.7, "window"), (5.78, 6.32, 4.4, 6.1, "window")]
    facade(p, Fr((X0, Y0, 0), (1, 0, 0), (0, -1, 0)), 12.0, H, front, key_front, lod=lod, split_u=(5.5, 6.6), split_v=(0.40, 3.55, 3.85))
    side = [(1.4, 3.2, 0.95, 2.80, "window"), (5.4, 7.2, 0.95, 2.80, "window"), (1.4, 3.2, 4.40, 6.10, "window"), (5.4, 7.2, 4.40, 6.10, "window"), (8.3, 9.3, 4.9, 6.1, "window")]
    facade(p, Fr((X0, Y0, 0), (0, 1, 0), (-1, 0, 0)), 10.0, H, side, key_side, lod=lod, split_v=(0.40,))
    facade(p, Fr((X1, Y0, 0), (0, 1, 0), (1, 0, 0)), 10.0, H, [(1.4, 3.2, 0.95, 2.80, "window"), (6.8, 8.6, 0.95, 2.80, "window"), (1.4, 3.2, 4.40, 6.10, "window"), (6.8, 8.6, 4.40, 6.10, "window")], key_side, lod=lod, split_v=(0.40,))
    facade(p, Fr((X1, Y1, 0), (-1, 0, 0), (0, 1, 0)), 12.0, H, [(2.0, 3.6, 0.95, 2.8, "window"), (8.0, 9.6, 0.95, 2.8, "window"), (2.0, 3.6, 4.4, 6.1, "window"), (5.4, 7.0, 4.4, 6.1, "window"), (8.0, 9.6, 4.4, 6.1, "window")], key_side, lod=lod, split_v=(0.40,))
    if lod == 0:
        p.box(X0 - 0.04, Y0 - 0.05, 3.55, X1 + 0.04, Y0, 3.85, WHITE, skip=("bottom",))                         # string course at floor level (front)
        for cx in (X0 + 0.02, X1 - 0.02): p.tube((cx, Y0 - 0.12, 0.15), (cx, Y0 - 0.12, H - 0.05), 0.045, 0.045, 8, "metal_galvanised")   # downpipes
    balcony(p, -5.0, -0.8, -7.0, Y0, 3.55, True, lod)
    hip_roof(p, X0 - 0.7, X1 + 0.7, Y0 - 0.7, Y1 + 0.7, H, 9.4, 2.2, 18 if lod == 0 else 8, tiles=True)
    return p


def gable_roof(p, x0, x1, y0, y1, z_eave, z_ridge, courses=10, tiles=True):
    """A gable roof with the ridge along Y (from y0 to y1) over the eave rectangle x0..x1; two tiled slopes and two triangular gable ends (cream) with a fascia."""
    xm = (x0 + x1) / 2
    roof_slope(p, (x0, y0, z_eave), (x0, y1, z_eave), (xm, y0, z_ridge), (xm, y1, z_ridge), courses, ROOF, (-1, 0, 1), tiles=tiles)
    roof_slope(p, (x1, y1, z_eave), (x1, y0, z_eave), (xm, y1, z_ridge), (xm, y0, z_ridge), courses, ROOF, (1, 0, 1), tiles=tiles)
    p.face_n([(x0, y0, z_eave), (x1, y0, z_eave), (xm, y0, z_ridge)], CREAM, (0, -1, 0)); p.face_n([(x0, y1, z_eave), (x1, y1, z_eave), (xm, y1, z_ridge)], CREAM, (0, 1, 0))
    if tiles: p.tube((xm, y0 - 0.05, z_ridge + 0.05), (xm, y1 + 0.05, z_ridge + 0.05), 0.09, 0.09, 4, ROOF, smooth=False, roll=math.pi / 4)
    for sx in (-1, 1):                                                                                          # barge boards along the gable edge
        a = (xm + sx * (x1 - xm), y0 - 0.06, z_eave); b = (xm, y0 - 0.06, z_ridge); p.tube(a, b, 0.05, 0.05, 4, WHITE, smooth=False, roll=math.pi / 4)


def villa_b(lod: int = 0) -> Part:
    """Villa type B: a 11 x 9.6 m main block with a projecting two-storey front bay under a gable (stone-clad ground floor), a hipped roof, and a garage wing with a roller shutter and a parapet roof.
    Ground floor 3.2 m plus 2.8 m, eave 6.9 m. Different massing, window pattern and cladding from type A so that a street is not a row of copies."""
    p = Part("villa"); X0, X1, Y0, Y1 = -5.5, 5.5, -4.8, 4.8; H = 6.9; BX0, BX1, BY = 0.4, 5.4, -6.8
    if lod >= 2:
        p.box(X0, Y0, 0, X1, Y1, H, CREAM); p.box(-10, -4, 0, X0, 3.5, 3.4, CREAM); hip_roof(p, X0 - 0.6, X1 + 0.6, Y0 - 0.6, Y1 + 0.6, H, 9.0, 2.0, 1, tiles=False); return p
    kf = lambda u, v: STONE if v < 0.9 else CREAM
    # main block: front (only the part left of the bay), sides, rear
    facade(p, Fr((X0, Y0, 0), (1, 0, 0), (0, -1, 0)), BX0 - X0, H, [(1.0, 3.0, 0.95, 2.80, "window"), (1.0, 3.0, 4.40, 6.10, "window"), (3.7, 4.9, 0.95, 2.80, "window"), (3.7, 4.9, 4.40, 6.10, "window")], kf, lod=lod, split_v=(0.9,))
    facade(p, Fr((BX1, Y0, 0), (1, 0, 0), (0, -1, 0)), X1 - BX1, H, [(0.0, 0.1, 0, 0, "x")][:0], kf, lod=lod, split_v=(0.9,))
    facade(p, Fr((X0, Y0, 0), (0, 1, 0), (-1, 0, 0)), Y1 - Y0, H, [(1.2, 3.0, 0.95, 2.8, "window"), (5.2, 7.0, 0.95, 2.8, "window"), (1.2, 3.0, 4.4, 6.1, "window"), (5.2, 7.0, 4.4, 6.1, "window"), (8.0, 9.0, 4.9, 6.1, "window")], lambda u, v: GREY if v < 0.4 else CREAM, lod=lod, split_v=(0.4,))
    facade(p, Fr((X1, Y0, 0), (0, 1, 0), (1, 0, 0)), Y1 - Y0, H, [(1.2, 3.0, 0.95, 2.8, "window"), (5.2, 7.0, 0.95, 2.8, "window"), (1.2, 3.0, 4.4, 6.1, "window"), (5.2, 7.0, 4.4, 6.1, "window")], lambda u, v: GREY if v < 0.4 else CREAM, lod=lod, split_v=(0.4,))
    facade(p, Fr((X1, Y1, 0), (-1, 0, 0), (0, 1, 0)), X1 - X0, H, [(1.5, 3.1, 0.95, 2.8, "window"), (5.5, 7.1, 0.95, 2.8, "window"), (1.5, 3.1, 4.4, 6.1, "window"), (5.5, 7.1, 4.4, 6.1, "window")], lambda u, v: GREY if v < 0.4 else CREAM, lod=lod, split_v=(0.4,))
    # projecting front bay (stone-clad ground floor, glazed double door with side lights, big first-floor window), gable above
    bay_h = 6.4
    facade(p, Fr((BX0, BY, 0), (1, 0, 0), (0, -1, 0)), BX1 - BX0, bay_h, [(0.8, 2.6, 0.0, 2.40, "pair"), (3.0, 4.2, 0.95, 2.8, "window"), (0.7, 4.3, 3.9, 5.9, "window")], kf, lod=lod, split_v=(0.9,))
    facade(p, Fr((BX0, BY, 0), (0, 1, 0), (-1, 0, 0)), Y0 - BY, bay_h, [], lambda u, v: STONE if v < 0.9 else CREAM, lod=lod, split_v=(0.9,))
    facade(p, Fr((BX1, BY, 0), (0, 1, 0), (1, 0, 0)), Y0 - BY, bay_h, [], lambda u, v: STONE if v < 0.9 else CREAM, lod=lod, split_v=(0.9,))
    p.box(BX0 - 0.15, BY - 0.15, 3.55, BX1 + 0.15, BY + 0.0, 3.85, WHITE, skip=("bottom",)) if lod == 0 else None                  # string course round the bay
    gable_roof(p, BX0 - 0.35, BX1 + 0.35, BY - 0.4, Y0 + 2.2, bay_h, 8.5, 9 if lod == 0 else 5)
    hip_roof(p, X0 - 0.6, X1 + 0.6, Y0 - 0.6, Y1 + 0.6, H, 9.0, 2.0, 18 if lod == 0 else 8, tiles=True)
    # entrance steps and lamp posts, downpipes
    p.box(BX0 + 0.6, BY - 0.9, 0.0, BX1 - 1.8, BY, 0.14, STONE, skip=("bottom", "back")); p.box(BX0 + 0.4, BY - 0.5, 0.0, BX1 - 1.6, BY - 0.0, 0.07, STONE, skip=("bottom",)) if lod == 0 else None
    if lod == 0:
        for cx in (X0 + 0.02, X1 - 0.02): p.tube((cx, Y0 - 0.12, 0.15), (cx, Y0 - 0.12, H - 0.05), 0.045, 0.045, 8, "metal_galvanised")
    # garage wing on the left
    GX0, GX1, GY0, GY1, GH = -10.2, X0, -4.0, 3.6, 3.4
    facade(p, Fr((GX0, GY0, 0), (1, 0, 0), (0, -1, 0)), GX1 - GX0, GH, [(0.7, 3.9, 0.0, 2.45, "garage")], lambda u, v: GREY if v < 0.4 else CREAM, lod=lod, split_v=(0.4,), sills=False)
    facade(p, Fr((GX0, GY0, 0), (0, 1, 0), (-1, 0, 0)), GY1 - GY0, GH, [(2.8, 4.0, 1.2, 2.4, "window")], lambda u, v: GREY if v < 0.4 else CREAM, lod=lod, split_v=(0.4,), sills=(lod == 0))
    facade(p, Fr((GX1, GY1, 0), (-1, 0, 0), (0, 1, 0)), GX1 - GX0, GH, [], lambda u, v: CREAM, lod=lod)
    p.face_n([(GX0, GY0, GH), (GX1, GY0, GH), (GX1, GY1, GH), (GX0, GY1, GH)], WHITE, (0, 0, 1))                                              # flat roof slab
    for (a, b, c, d) in [(GX0, GY0, GX1, GY0 + 0.15), (GX0, GY1 - 0.15, GX1, GY1), (GX0, GY0, GX0 + 0.15, GY1), (GX1 - 0.15, GY0, GX1, GY1)]: p.box(a, b, GH, c, d, GH + 0.45, CREAM)  # parapet
    return p
