"""Mosque family for Kano (owner directive of 2026-11-09, references R-037, R-038, R-039 and R-009/R-016..R-019 for the recurring generic skyline mosque; traditional forms from the documentary sources R-024..R-029).
Ten genuine 3D mosques of four classes, none of them a model of any real mosque (owner decision C1: the Kano Central Mosque is a real landmark, blocked on references M-11, and is NOT touched):
  A  grand mosques (CITY_SPECIFIC `kano_mosque_grand_a/b`): R-037 (four minarets, one large ribbed dome, a tall pointed entrance) and R-038 (a wide symmetrical facade, a central iwan with steps)
  B  medium neighbourhood mosques (REGIONAL `rg_ngn_mosque_medium_a/b/c`): a domed hall with two minarets, an arcaded flat-roof hall with small domes and one minaret, a pointed-dome hall with twin slim minarets
  C  small local mosques (REGIONAL `rg_ngn_mosque_small_a/b/c`): compact halls with a small dome or a flat roof and a short minaret
  D  traditional mosques (REGIONAL `rg_ngn_mosque_traditional_a/b`): earth (kasa) render, battered walls, corner pinnacles (zankwaye), relief panels, a tapering minaret: a FLAGGED interpretation of documentary sources, no licensed photograph (M-01 partly open)
Every mosque stands in its compound: the asset includes the forecourt and the boundary wall with capped piers and a railing along the road, as in the references, so a placed mosque belongs to its street. Real metres, front at -Y (the road side),
origin at the centre of the footprint. Three LODs (the last a few primitives), at most 4 materials (palette: tk_paint, tk_paint_ext, tk_paint_ext2)."""
from __future__ import annotations
import math
from mathutils import Matrix, Vector
from kano_kit.geo import Part
from .common import prism_y, prism_x, circle_pts, extra

GAL, CON = "metal_galvanised", "concrete"
STONE, TRIM, DOME, GOLD, RAIL, DARK, GLOW, PAVER = "stone_ivory", "plaster_cream", "dome_green", "trim_gold", "railing_green", "interior_dark", "lantern_glow", "paver_red"
KASA, KASA2 = "kasa_render", "plaster_terracotta"
REF_MOSQUE = ["R-037", "R-038", "R-039", "R-009"]
REF_TRAD = ["R-024", "R-025", "R-026", "R-027", "R-028", "R-029"]


# ------------------------------------------------------------------------------------------------------------------------------------------------ primitives
def pointed_arch(w, h, rise=0.34, fast=False):
    """Outline (x, z) of a pointed arch of width w and height h: straight jambs up to the springing, then two arcs to the apex (9 points; 5 when `fast`, a gable-topped slot for small openings far from the eye)."""
    s = h * (1 - rise)
    if fast: return [(-w / 2, 0.0), (-w / 2, s), (0.0, h), (w / 2, s), (w / 2, 0.0)]
    return [(-w / 2, 0.0), (-w / 2, s), (-w * 0.41, s + h * rise * 0.45), (-w * 0.2, s + h * rise * 0.82), (0.0, h), (w * 0.2, s + h * rise * 0.82), (w * 0.41, s + h * rise * 0.45), (w / 2, s), (w / 2, 0.0)]


def arch_window(p: Part, x, y, z0, w, h, key=DARK, depth=0.12, lattice=None, face="-y", fast=False):
    """A pointed arched window set proud of the wall at y (face '-y', the front) or at x (faces '-x' and '+x'): a dark recess and, when `lattice` is a colour, a lighter frame around it."""
    pts = [(px + x, pz + z0) for px, pz in pointed_arch(w, h, fast=fast)] if face in ("-y", "+y") else [(px + y, pz + z0) for px, pz in pointed_arch(w, h, fast=fast)]
    if face == "-y":
        if lattice: prism_y(p, [(px + x, pz + z0) for px, pz in pointed_arch(w + 0.32, h + 0.2)], y - depth * 0.6, y, lattice)
        prism_y(p, pts, y - depth, y - depth * 0.6 if lattice else y, key)
    elif face == "+y":
        prism_y(p, pts, y, y + depth, key)
    else:
        sgn = -1 if face == "-x" else 1
        (prism_x)(p, pts, x - depth if sgn < 0 else x, x if sgn < 0 else x + depth, key)


def dome(p: Part, cx, cy, z0, r, h, key, seg=20, rows=8, kind="hemi", lod=0):
    """A dome built from rings: 'hemi' (a slightly stilted hemisphere), 'onion' (a bulb with a pointed top), 'pointed' (a Persian-style pointed dome). Closed at the top by a tiny ring."""
    rows = max(3, rows if lod == 0 else max(3, rows // 2)); seg = seg if lod == 0 else max(8, seg // 2); rings = []
    for i in range(rows + 1):
        t = i / rows
        if kind == "hemi": rr, zz = r * math.cos(t * math.pi / 2 * 0.97), h * math.sin(t * math.pi / 2)
        elif kind == "onion": rr, zz = r * (math.sin(math.pi * min(1.0, 0.12 + 0.88 * t)) * 0.92 + 0.08 * (1 - t)) if t < 0.96 else r * 0.02, h * t
        else: rr, zz = r * (1 - t ** 1.7) * (1 + 0.1 * math.sin(math.pi * t)), h * (1 - (1 - t) ** 1.35)
        rr = max(rr, 0.015 * r); rings.append([Vector((cx + rr * math.cos(2 * math.pi * k / seg), cy + rr * math.sin(2 * math.pi * k / seg), z0 + zz)) for k in range(seg)])
    p.ring_tube(rings, key, caps=(False, False), smooth=(lod == 0))


def finial(p: Part, cx, cy, z, h=1.6, key=GOLD, lod=0):
    """A gold finial: a short stem, a ball and a crescent-like bar (kept abstract)."""
    p.tube((cx, cy, z), (cx, cy, z + h), 0.06, 0.02, 6, key, caps=(False, True))
    if lod == 0: p.blob((cx, cy, z + h * 0.35), (0.16, 0.16, 0.16), key, subdiv=1)


def minaret(p: Part, cx, cy, z0, H, w, lod=0, shape="square", body=STONE, cap=DOME, trim=TRIM, tiers=(0.28, 0.5, 0.75)):
    """A minaret: a base block, a square or octagonal shaft with pointed-arch openings in two tiers, two balconies with railings, a lantern and an onion cap with a finial. lod 0 full; 1 shaft, balconies and cap; 2 a box."""
    if lod >= 2: p.box(cx - w / 2, cy - w / 2, z0, cx + w / 2, cy + w / 2, z0 + H, body); return
    seg, roll = (4, math.pi / 4) if shape == "square" else (8, math.pi / 8); R = w / 2 * (math.sqrt(2) if shape == "square" else 1.0)
    p.box(cx - w * 0.68, cy - w * 0.68, z0, cx + w * 0.68, cy + w * 0.68, z0 + 1.2, trim, skip=("bottom",))
    zb, zt = z0 + 1.2, z0 + H * 0.9
    p.tube((cx, cy, zb), (cx, cy, zt), R, R * 0.9, seg, body, caps=(False, True), smooth=False, roll=roll)
    for fz in (0.62, 0.9):                                                                                                  # two balconies
        z = z0 + H * fz; rb = w * 0.72
        p.box(cx - rb, cy - rb, z, cx + rb, cy + rb, z + 0.3, trim, skip=("bottom",))
        if lod == 0:
            for k in range(4): a = 2 * math.pi * k / 4 + math.pi / 4; p.box(cx + math.cos(a) * rb * 0.95 - 0.03, cy + math.sin(a) * rb * 0.95 - 0.03, z + 0.3, cx + math.cos(a) * rb * 0.95 + 0.03, cy + math.sin(a) * rb * 0.95 + 0.03, z + 1.1, trim)
            p.box(cx - rb, cy - rb, z + 1.05, cx + rb, cy + rb, z + 1.1, trim, skip=("bottom",))
    if lod == 0:
        hw = w * 0.34
        for tier in tiers:
            z = z0 + H * tier
            for dx, dy, face in ((0, -1, "-y"), (0, 1, "+y")): arch_window(p, cx, cy + dy * (w / 2 + (0.0 if shape == "square" else -0.02)), z, hw, H * 0.1, DARK, 0.08, None, face, fast=True)
            arch_window(p, cx - w / 2, cy, z, hw, H * 0.1, DARK, 0.08, None, "-x", fast=True); arch_window(p, cx + w / 2, cy, z, hw, H * 0.1, DARK, 0.08, None, "+x", fast=True)
    zl = z0 + H * 0.9
    p.tube((cx, cy, zl), (cx, cy, zl + H * 0.04), R * 0.8, R * 0.8, seg, trim, caps=(False, True), smooth=False, roll=roll)
    dome(p, cx, cy, zl + H * 0.04, w * 0.62, H * 0.12, cap, seg=10, rows=5, kind="onion", lod=lod); finial(p, cx, cy, zl + H * 0.16, 1.4, GOLD, lod)


def boundary_wall(p: Part, x0, x1, y, lod=0, gate=(0.0, 4.0), key=TRIM, rail=RAIL, pitch=3.2, side_returns=None, bars=5):
    """The compound's front boundary along the road at y: a low wall, capped piers with lanterns every `pitch` m, a railing between the piers (a gap for the gate), and optional side returns (x0 and x1 down to y2)."""
    n = max(2, int(round((x1 - x0) / pitch))); step = (x1 - x0) / n
    p.box(x0, y - 0.15, 0.0, x1, y + 0.15, 0.7, key, skip=("bottom",))
    if lod >= 2: p.box(x0, y - 0.2, 0.0, x1, y + 0.2, 1.8, key); return
    for i in range(n + 1):
        x = x0 + i * step; p.box(x - 0.28, y - 0.28, 0.0, x + 0.28, y + 0.28, 1.7, key, skip=("bottom",))
        if lod == 0: p.box(x - 0.34, y - 0.34, 1.7, x + 0.34, y + 0.34, 1.8, TRIM, skip=("bottom",)); p.box(x - 0.1, y - 0.1, 1.8, x + 0.1, y + 0.1, 2.1, GLOW)
    gx0, gx1 = gate[0] - gate[1] / 2, gate[0] + gate[1] / 2
    bars = bars if lod == 0 else 2
    for i in range(n):
        xa = x0 + i * step + 0.28; xb = x0 + (i + 1) * step - 0.28
        if xa >= gx0 and xb <= gx1: continue
        p.box(xa, y - 0.03, 0.7, xb, y + 0.03, 0.78, rail, skip=("bottom",)); p.box(xa, y - 0.03, 1.55, xb, y + 0.03, 1.62, rail, skip=("bottom",))
        for k in range(bars): x = xa + (xb - xa) * (k + 0.5) / bars; p.box(x - 0.018, y - 0.018, 0.78, x + 0.018, y + 0.018, 1.55, rail, skip=("bottom", "top"))
    if side_returns:
        for x, y2 in ((x0, side_returns), (x1, side_returns)): p.box(x - 0.15, min(y, y2), 0.0, x + 0.15, max(y, y2), 1.2, key, skip=("bottom",))


def forecourt(p: Part, x0, x1, y0, y1, steps_at=None):
    """The paved forecourt between the wall and the building (a flat slab 0.08 m above the ground, red pavers)."""
    p.box(x0, y0, 0.0, x1, y1, 0.08, PAVER, skip=("bottom",))


def facade_windows(p: Part, x0, x1, yf, z0, w, h, n, lod, lattice=TRIM):
    """A row of n pointed arched windows (with a pale frame at lod 0) spread between x0 and x1 on the front face at yf."""
    if lod >= 2: return
    for i in range(n):
        x = x0 + (x1 - x0) * (i + 0.5) / n
        arch_window(p, x, yf, z0, w, h, DARK, 0.14, lattice if lod == 0 else None, "-y")


def side_windows(p: Part, y0, y1, xf, z0, w, h, n, lod, sign):
    if lod >= 2 or lod >= 1 and n > 4: n = min(n, 4) if lod == 1 else 0
    for i in range(n):
        y = y0 + (y1 - y0) * (i + 0.5) / max(n, 1); arch_window(p, xf, y, z0, w, h, DARK, 0.12, None, "-x" if sign < 0 else "+x")


def iwan(p: Part, cx, yf, w, h, lod, depth=1.2):
    """A tall pointed-arch entrance (iwan): a pale outer frame, a dark recessed opening, and at lod 0 a smaller inner frame, steps and a door."""
    ft = 0.5 if depth >= 0.8 else depth * 0.55                                                                          # thickness of the pale frame; the recess behind it is depth - ft (never zero)
    prism_y(p, [(px + cx, pz) for px, pz in pointed_arch(w + 1.0, h + 0.6)], yf - ft, yf, TRIM)
    prism_y(p, [(px + cx, pz) for px, pz in pointed_arch(w, h)], yf - depth, yf - ft, DARK)
    if lod == 0:
        prism_y(p, [(px + cx, pz + 0.0) for px, pz in pointed_arch(w * 0.62, h * 0.7, fast=True)], yf - depth - 0.05, yf - depth, "wood_door")
        for k in range(4): p.box(cx - w / 2 - 0.5 - k * 0.2, yf - ft - 0.35 * (k + 1), 0.0, cx + w / 2 + 0.5 + k * 0.2, yf - ft - 0.35 * k, 0.15 * (4 - k), STONE, skip=("bottom",))


def cornice(p: Part, x0, x1, y0, y1, z, key=TRIM):
    p.box(x0 - 0.3, y0 - 0.3, z, x1 + 0.3, y1 + 0.3, z + 0.35, key, skip=("bottom",))


def parapet_band(p: Part, x0, x1, y0, y1, z, h=0.9, key=STONE):
    p.box(x0, y0, z, x1, y0 + 0.3, z + h, key, skip=("bottom",)); p.box(x0, y1 - 0.3, z, x1, y1, z + h, key, skip=("bottom",))                      # front and back runs; the side runs stop at them (no coplanar overlap)
    p.box(x0, y0 + 0.3, z, x0 + 0.3, y1 - 0.3, z + h, key, skip=("bottom",)); p.box(x1 - 0.3, y0 + 0.3, z, x1, y1 - 0.3, z + h, key, skip=("bottom",))


def drum_slots(p: Part, cx, cy, r, z0, z1, n, w=1.0, d=0.2, key=DARK):
    """n dark window slots set into the wall of a round drum of radius r (centre cx, cy), each a thin box turned to face outwards."""
    for k in range(n):
        a = 2 * math.pi * (k + 0.5) / n; s0 = p.mark(); p.box(-w / 2, -d / 2, z0, w / 2, d / 2, z1, key)
        p.xform(s0, Matrix.Translation((cx + math.cos(a) * r, cy + math.sin(a) * r, 0.0)) @ Matrix.Rotation(a - math.pi / 2, 4, "Z"))


def centred(p: Part, yshift=0.0):
    """Moves the finished part so that the footprint is centred on y = 0 (the front wall at -D/2, the back at +D/2)."""
    p.xform(0, Matrix.Translation((0.0, yshift, 0.0)))


# ------------------------------------------------------------------------------------------------------------------------------------------------ A: grand mosques
def grand_a(lod):
    """Grand mosque after R-037: a central block with a tall pointed iwan, a large green dome on a windowed drum, two tall front minarets and two smaller rear minarets, wings with pointed windows and green corner domes, a forecourt and a railed front wall. 46 x 40 m."""
    p = Part("mosque"); W, D = 46.0, 40.0; y0, yb = -D / 2, D / 2; yf = -4.0                       # compound front y0; building facade at yf; back wall yb
    bx0, bx1, by1 = -16.0, 16.0, yb - 3.0                                                         # central block
    wx0, wx1 = -22.0, 22.0                                                                          # wings
    if lod >= 2:
        p.box(wx0, yf, 0.0, wx1, by1, 9.0, STONE); p.box(-9.0, 0.0, 9.0, 9.0, 18.0, 13.0, STONE); dome(p, 0, 9, 13.0, 9.5, 9.0, DOME, seg=8, rows=3, lod=2)
        for x, yy, hh in ((-17.0, yf, 30.0), (17.0, yf, 30.0)): p.box(x - 1.6, yy - 1.6, 0.0, x + 1.6, yy + 1.6, hh, STONE)
        p.box(-W / 2, y0 - 0.2, 0.0, W / 2, y0 + 0.2, 1.8, TRIM); return p
    forecourt(p, -W / 2, W / 2, y0, yf, None)
    p.box(wx0, yf, 0.0, wx1, by1, 7.5, STONE, skip=("bottom",)); cornice(p, wx0, wx1, yf, by1, 7.5)                          # lower hall and wings
    p.box(bx0, yf - 1.0, 0.0, bx1, by1, 12.5, STONE, skip=("bottom",)); cornice(p, bx0, bx1, yf - 1.0, by1, 12.5)         # taller central block
    iwan(p, 0.0, yf - 1.0, 8.0, 11.0, lod)
    p.tube((0, 9.0, 12.85), (0, 9.0, 16.5), 8.3, 8.3, 20 if lod == 0 else 12, STONE, caps=(False, True), smooth=False)    # drum
    if lod == 0:
        drum_slots(p, 0.0, 9.0, 8.3, 13.6, 15.6, 12)
    p.tube((0, 9.0, 16.5), (0, 9.0, 16.9), 8.6, 8.6, 20 if lod == 0 else 12, TRIM, caps=(False, True), smooth=False)
    dome(p, 0, 9.0, 16.9, 8.4, 8.5, DOME, seg=24, rows=10, lod=lod); p.tube((0, 9.0, 24.8), (0, 9.0, 25.6), 1.1, 0.6, 12, TRIM, caps=(False, True), smooth=False); finial(p, 0, 9.0, 25.6, 2.4, GOLD, lod)
    if lod == 0:                                                                                                         # ribs of the dome (12 thin meridians)
        for k in range(12):
            a = 2 * math.pi * k / 12; pts = [Vector((math.cos(a) * r * 1.0, 9.0 + math.sin(a) * r * 1.0, 16.9 + z)) for r, z in ((8.35, 0.1), (7.5, 3.2), (5.2, 6.6), (2.0, 8.2))]
            p.path_tube(pts, 0.1, 3, GOLD, caps=(True, True), smooth=False)
    for sx in (-1, 1):                                                                                                   # wings: windows, green corner domes
        x0, x1 = (wx0, bx0) if sx < 0 else (bx1, wx1)
        facade_windows(p, x0 + 1.0, x1 - 1.0, yf, 1.2, 1.5, 3.8, 4, lod)
        dome(p, (x0 + x1) / 2, yf + 5.0, 7.85, 3.2, 3.6, DOME, seg=14, rows=6, lod=lod); finial(p, (x0 + x1) / 2, yf + 5.0, 11.4, 1.4, GOLD, lod)
    facade_windows(p, bx0 + 1.0, -5.5, yf - 1.0, 1.4, 1.5, 4.4, 2, lod); facade_windows(p, 5.5, bx1 - 1.0, yf - 1.0, 1.4, 1.5, 4.4, 2, lod)
    side_windows(p, yf + 2.0, by1 - 2.0, wx0, 1.4, 1.4, 4.0, 5, lod, -1); side_windows(p, yf + 2.0, by1 - 2.0, wx1, 1.4, 1.4, 4.0, 5, lod, 1)
    for sx in (-1, 1):                                                                                                   # four minarets: two tall at the front, two smaller behind
        minaret(p, sx * 17.5, yf - 0.5, 0.0, 30.0, 3.4, lod); minaret(p, sx * 11.0, by1 - 2.0, 0.0, 20.0, 2.6, lod, tiers=(0.45,))
    boundary_wall(p, -W / 2, W / 2, y0, lod, gate=(0.0, 5.0), side_returns=y0 + 12.0, bars=3, pitch=4.0)
    centred(p, 0.0); return p


def grand_b(lod):
    """Grand mosque after R-038: a wide symmetrical facade with a tall central iwan and a flight of steps, a green dome on a windowed drum, two tall corner minarets, two small minarets beside the dome, long arcades, low green wing domes, a forecourt with a lawn strip and a railed wall. 58 x 42 m."""
    p = Part("mosque"); W, D = 58.0, 42.0; y0, yb = -D / 2, D / 2; yf = -5.0; by1 = yb - 3.0
    if lod >= 2:
        p.box(-26.0, yf, 0.0, 26.0, by1, 8.0, STONE); p.box(-8.0, 0.0, 8.0, 8.0, 16.0, 14.0, STONE); dome(p, 0, 8, 14.0, 8.0, 8.0, DOME, seg=8, rows=3, lod=2)
        for x in (-27.0, 27.0): p.box(x - 1.5, yf - 1.5, 0.0, x + 1.5, yf + 1.5, 28.0, STONE)
        p.box(-W / 2, y0 - 0.2, 0.0, W / 2, y0 + 0.2, 1.8, TRIM); return p
    forecourt(p, -W / 2, W / 2, y0, yf - 3.0, None); p.box(-W / 2 + 2.0, y0 + 2.0, 0.08, W / 2 - 2.0, y0 + 5.0, 0.35, TRIM, skip=("bottom",))
    p.box(-26.0, yf, 0.0, 26.0, by1, 8.0, STONE, skip=("bottom",)); cornice(p, -26.0, 26.0, yf, by1, 8.0)                    # long hall
    p.box(-9.0, yf - 3.0, 0.0, 9.0, by1, 14.0, STONE, skip=("bottom",)); cornice(p, -9.0, 9.0, yf - 3.0, by1, 14.0)       # tall central block with the iwan
    iwan(p, 0.0, yf - 3.0, 8.5, 12.0, lod, 1.6)
    p.tube((0, 8.0, 14.35), (0, 8.0, 17.8), 7.2, 7.2, 20 if lod == 0 else 12, STONE, caps=(False, True), smooth=False); p.tube((0, 8.0, 17.8), (0, 8.0, 18.2), 7.5, 7.5, 20 if lod == 0 else 12, TRIM, caps=(False, True), smooth=False)
    dome(p, 0, 8.0, 18.2, 7.3, 7.6, DOME, seg=20, rows=9, lod=lod); finial(p, 0, 8.0, 25.8, 2.4, GOLD, lod)
    for sx in (-1, 1):
        for x0, x1 in ((9.0 * sx, 25.5 * sx),):
            xa, xb = min(x0, x1), max(x0, x1)
            facade_windows(p, xa + 0.8, xb - 0.8, yf, 1.1, 1.3, 4.6, 7, lod)                                                  # long arcade
            for k in range(3): dome(p, xa + (xb - xa) * (k + 0.5) / 3, yf + 6.0, 8.35, 2.6, 2.8, DOME, seg=10, rows=4, lod=lod)
        minaret(p, sx * 28.0, yf - 1.0, 0.0, 27.0, 3.2, lod); minaret(p, sx * 12.5, 9.0, 0.0, 19.0, 2.4, lod, shape="oct", tiers=(0.45,))
    facade_windows(p, -8.0, -5.0, yf - 3.0, 1.2, 1.2, 4.4, 1, lod); facade_windows(p, 5.0, 8.0, yf - 3.0, 1.2, 1.2, 4.4, 1, lod)
    side_windows(p, yf + 2.0, by1 - 2.0, -26.0, 1.4, 1.4, 4.2, 5, lod, -1); side_windows(p, yf + 2.0, by1 - 2.0, 26.0, 1.4, 1.4, 4.2, 5, lod, 1)
    if lod == 0:
        for k in range(10): p.box(-9.0 + k * 1.8, yf - 3.0 - 0.1, 8.0, -9.0 + k * 1.8 + 0.9, yf - 3.0, 8.9, TRIM, skip=("bottom", "back"))     # crenellated cornice of the central block
    boundary_wall(p, -W / 2, W / 2, y0, lod, gate=(0.0, 6.0), side_returns=y0 + 14.0, bars=3, pitch=4.5)
    centred(p, 0.0); return p


# ------------------------------------------------------------------------------------------------------------------------------------------------ B: medium mosques
def medium_a(lod):
    """Medium mosque: a square hall under one green dome on a drum, an entrance porch with a pointed arch, two slim minarets flanking the entrance, arched windows on three sides, a wall with a railing. 26 x 26 m."""
    p = Part("mosque"); W, D = 26.0, 26.0; y0 = -D / 2; yf = -5.0; by1 = D / 2 - 1.5
    if lod >= 2:
        p.box(-9.0, yf, 0.0, 9.0, by1, 7.0, STONE); dome(p, 0, 3, 7.0, 5.5, 4.0, DOME, seg=8, rows=3, lod=2)
        for x in (-9.5, 9.5): p.box(x - 0.9, yf - 0.9, 0.0, x + 0.9, yf + 0.9, 15.0, STONE)
        p.box(-W / 2, y0 - 0.2, 0.0, W / 2, y0 + 0.2, 1.8, TRIM); return p
    forecourt(p, -W / 2, W / 2, y0, yf, None); p.box(-9.0, yf, 0.0, 9.0, by1, 7.0, STONE, skip=("bottom",)); cornice(p, -9.0, 9.0, yf, by1, 7.0)
    iwan(p, 0.0, yf, 3.6, 5.2, lod, 0.9)
    p.tube((0, 3.0, 7.35), (0, 3.0, 9.4), 5.4, 5.4, 16 if lod == 0 else 10, STONE, caps=(False, True), smooth=False)
    dome(p, 0, 3.0, 9.4, 5.5, 4.4, DOME, seg=20, rows=8, lod=lod); finial(p, 0, 3.0, 13.8, 1.8, GOLD, lod)
    facade_windows(p, -8.5, -2.6, yf, 1.0, 1.2, 3.4, 2, lod); facade_windows(p, 2.6, 8.5, yf, 1.0, 1.2, 3.4, 2, lod)
    side_windows(p, yf + 1.5, by1 - 1.5, -9.0, 1.0, 1.2, 3.4, 4, lod, -1); side_windows(p, yf + 1.5, by1 - 1.5, 9.0, 1.0, 1.2, 3.4, 4, lod, 1)
    for sx in (-1, 1): minaret(p, sx * 10.2, yf - 0.4, 0.0, 17.0, 1.9, lod)
    boundary_wall(p, -W / 2, W / 2, y0, lod, gate=(0.0, 4.0), pitch=3.25, side_returns=y0 + 8.0)
    centred(p); return p


def medium_b(lod):
    """Medium mosque: a long flat-roofed prayer hall with an arcaded front, three small green domes along the roofline, one tall octagonal minaret at the corner, a parapet with merlons, a railed front wall. 30 x 24 m."""
    p = Part("mosque"); W, D = 30.0, 24.0; y0 = -D / 2; yf = -4.0; by1 = D / 2 - 1.5
    if lod >= 2:
        p.box(-12.0, yf, 0.0, 12.0, by1, 7.0, STONE); p.box(11.0, yf - 0.8, 0.0, 12.8, yf + 1.0, 18.0, STONE); p.box(-W / 2, y0 - 0.2, 0.0, W / 2, y0 + 0.2, 1.8, TRIM); return p
    forecourt(p, -W / 2, W / 2, y0, yf, None); p.box(-12.0, yf, 0.0, 12.0, by1, 6.6, STONE, skip=("bottom",)); cornice(p, -12.0, 12.0, yf, by1, 6.6); parapet_band(p, -12.0, 12.0, yf, by1, 6.95, 0.9, STONE)
    p.box(-12.0, yf - 2.2, 0.0, 12.0, yf, 0.3, STONE, skip=("bottom",)); p.box(-12.0, yf - 2.2, 4.6, 12.0, yf, 5.0, TRIM, skip=("bottom",))      # arcade slab
    for k in range(9): x = -11.5 + k * 2.875; p.box(x - 0.25, yf - 2.0, 0.3, x + 0.25, yf - 1.6, 4.6, TRIM, skip=("bottom",))                     # arcade piers
    facade_windows(p, -11.0, 11.0, yf, 1.0, 1.4, 3.6, 7, lod)
    for k in range(3): dome(p, -6.0 + 6.0 * k, 5.0, 7.85, 2.8, 3.0, DOME, seg=14, rows=6, lod=lod); finial(p, -6.0 + 6.0 * k, 5.0, 10.8, 1.4, GOLD, lod)
    side_windows(p, yf + 1.5, by1 - 1.5, -12.0, 1.0, 1.2, 3.4, 4, lod, -1); side_windows(p, yf + 1.5, by1 - 1.5, 12.0, 1.0, 1.2, 3.4, 4, lod, 1)
    minaret(p, 13.8, yf + 0.2, 0.0, 18.0, 2.4, lod, shape="oct")
    boundary_wall(p, -W / 2, W / 2, y0, lod, gate=(-4.0, 4.0), pitch=3.3, side_returns=y0 + 7.0)
    centred(p); return p


def medium_c(lod):
    """Medium mosque: a hall under one pointed (onion-ridged) dome with four small corner domes, an entrance portico, twin slim minarets at the back corners, a railed front wall. 28 x 28 m."""
    p = Part("mosque"); W, D = 28.0, 28.0; y0 = -D / 2; yf = -5.0; by1 = D / 2 - 2.0
    if lod >= 2:
        p.box(-9.0, yf, 0.0, 9.0, by1, 8.0, STONE); dome(p, 0, 3.0, 8.0, 5.5, 5.0, DOME, seg=8, rows=3, lod=2)
        for x in (-9.0, 9.0): p.box(x - 0.8, by1 - 1.6, 0.0, x + 0.8, by1, 17.0, STONE)
        p.box(-W / 2, y0 - 0.2, 0.0, W / 2, y0 + 0.2, 1.8, TRIM); return p
    forecourt(p, -W / 2, W / 2, y0, yf, None); p.box(-9.0, yf, 0.0, 9.0, by1, 7.0, STONE, skip=("bottom",)); cornice(p, -9.0, 9.0, yf, by1, 7.0)
    p.box(-4.0, yf - 2.4, 0.0, 4.0, yf, 5.4, STONE, skip=("bottom",)); iwan(p, 0.0, yf - 2.4, 3.2, 4.3, lod, 0.8)                                  # portico
    p.tube((0, 3.0, 7.35), (0, 3.0, 9.0), 5.0, 5.0, 16 if lod == 0 else 10, STONE, caps=(False, True), smooth=False)
    dome(p, 0, 3.0, 9.0, 5.0, 6.2, DOME, seg=20, rows=9, kind="pointed", lod=lod); finial(p, 0, 3.0, 15.2, 1.8, GOLD, lod)
    for sx in (-1, 1):
        for sy in (yf + 1.8, by1 - 1.8): dome(p, sx * 7.0, sy, 7.35, 1.5, 1.8, DOME, seg=10, rows=5, lod=lod)
        minaret(p, sx * 9.8, by1 - 0.8, 0.0, 17.0, 1.8, lod)
    facade_windows(p, -8.5, -4.6, yf, 1.0, 1.1, 3.4, 2, lod); facade_windows(p, 4.6, 8.5, yf, 1.0, 1.1, 3.4, 2, lod)
    side_windows(p, yf + 1.5, by1 - 1.5, -9.0, 1.0, 1.2, 3.4, 4, lod, -1); side_windows(p, yf + 1.5, by1 - 1.5, 9.0, 1.0, 1.2, 3.4, 4, lod, 1)
    boundary_wall(p, -W / 2, W / 2, y0, lod, gate=(0.0, 4.0), pitch=3.5, side_returns=y0 + 8.0)
    centred(p); return p


# ------------------------------------------------------------------------------------------------------------------------------------------------ C: small mosques
def small_a(lod):
    """Small local mosque: a compact hall under one small green dome, a short square minaret beside it, a porch, a low railed wall. 18 x 16 m."""
    p = Part("mosque"); W, D = 18.0, 16.0; y0 = -D / 2; yf = -2.5; by1 = D / 2 - 1.0
    if lod >= 2: p.box(-5.0, yf, 0.0, 5.0, by1, 5.0, STONE); p.box(5.5, yf, 0.0, 7.0, yf + 1.5, 11.0, STONE); p.box(-W / 2, y0 - 0.15, 0.0, W / 2, y0 + 0.15, 1.5, TRIM); return p
    forecourt(p, -W / 2, W / 2, y0, yf, None); p.box(-5.0, yf, 0.0, 5.0, by1, 4.8, STONE, skip=("bottom",)); cornice(p, -5.0, 5.0, yf, by1, 4.8)
    iwan(p, 0.0, yf, 2.4, 3.3, lod, 0.6); facade_windows(p, -4.6, -1.8, yf, 0.8, 1.0, 2.4, 1, lod); facade_windows(p, 1.8, 4.6, yf, 0.8, 1.0, 2.4, 1, lod)
    dome(p, 0, (yf + by1) / 2, 5.15, 3.4, 3.2, DOME, seg=16, rows=7, lod=lod); finial(p, 0, (yf + by1) / 2, 8.3, 1.3, GOLD, lod)
    minaret(p, 6.3, yf + 0.7, 0.0, 11.0, 1.4, lod); side_windows(p, yf + 1.0, by1 - 1.0, -5.0, 0.9, 1.0, 2.4, 2, lod, -1); side_windows(p, yf + 1.0, by1 - 1.0, 5.0, 0.9, 1.0, 2.4, 2, lod, 1)
    boundary_wall(p, -W / 2, W / 2, y0, lod, gate=(0.0, 3.2), pitch=3.0, side_returns=y0 + 5.0)
    centred(p); return p


def small_b(lod):
    """Small local mosque: a flat-roofed rectangular prayer hall with a hip-roofed porch, a short square minaret, a low wall. 20 x 16 m."""
    p = Part("mosque"); W, D = 20.0, 16.0; y0 = -D / 2; yf = -2.5; by1 = D / 2 - 1.0
    if lod >= 2: p.box(-7.0, yf, 0.0, 7.0, by1, 4.6, STONE); p.box(7.5, yf, 0.0, 8.9, yf + 1.4, 10.0, STONE); p.box(-W / 2, y0 - 0.15, 0.0, W / 2, y0 + 0.15, 1.5, TRIM); return p
    forecourt(p, -W / 2, W / 2, y0, yf, None); p.box(-7.0, yf, 0.0, 7.0, by1, 4.4, STONE, skip=("bottom",)); cornice(p, -7.0, 7.0, yf, by1, 4.4); parapet_band(p, -7.0, 7.0, yf, by1, 4.75, 0.7, STONE)
    p.box(-2.2, yf - 1.8, 0.0, 2.2, yf, 3.4, TRIM, skip=("bottom",)); prism_x(p, [(yf - 2.0, 3.4), (yf + 0.2, 3.4), (yf + 0.2, 4.6), (yf - 1.0, 4.2)], -2.6, 2.6, DOME); iwan(p, 0.0, yf - 1.8, 2.0, 2.8, lod, 0.5)
    facade_windows(p, -6.5, -2.8, yf, 0.8, 1.0, 2.2, 2, lod); facade_windows(p, 2.8, 6.5, yf, 0.8, 1.0, 2.2, 2, lod)
    side_windows(p, yf + 1.0, by1 - 1.0, -7.0, 0.9, 1.0, 2.2, 3, lod, -1); side_windows(p, yf + 1.0, by1 - 1.0, 7.0, 0.9, 1.0, 2.2, 3, lod, 1)
    minaret(p, 8.2, yf + 0.7, 0.0, 10.0, 1.4, lod)
    boundary_wall(p, -W / 2, W / 2, y0, lod, gate=(0.0, 3.2), pitch=3.3, side_returns=y0 + 5.0)
    centred(p); return p


def small_c(lod):
    """Small local prayer house: a compact flat-roofed hall with a small dome over the prayer niche, a short minaret, a plain wall. 14 x 12 m."""
    p = Part("mosque"); W, D = 14.0, 12.0; y0 = -D / 2; yf = -1.8; by1 = D / 2 - 0.8
    if lod >= 2: p.box(-4.2, yf, 0.0, 4.2, by1, 4.0, STONE); p.box(4.5, yf, 0.0, 5.7, yf + 1.2, 8.0, STONE); p.box(-W / 2, y0 - 0.15, 0.0, W / 2, y0 + 0.15, 1.5, TRIM); return p
    forecourt(p, -W / 2, W / 2, y0, yf, None); p.box(-4.2, yf, 0.0, 4.2, by1, 3.8, STONE, skip=("bottom",)); cornice(p, -4.2, 4.2, yf, by1, 3.8); parapet_band(p, -4.2, 4.2, yf, by1, 4.15, 0.6, STONE)
    iwan(p, 0.0, yf, 1.8, 2.5, lod, 0.5); facade_windows(p, -3.9, -1.5, yf, 0.7, 0.9, 2.0, 1, lod); facade_windows(p, 1.5, 3.9, yf, 0.7, 0.9, 2.0, 1, lod)
    dome(p, 0, by1 - 1.6, 4.1, 1.8, 1.8, DOME, seg=12, rows=5, lod=lod); finial(p, 0, by1 - 1.6, 5.9, 1.0, GOLD, lod)
    minaret(p, 5.1, yf + 0.5, 0.0, 8.0, 1.1, lod); side_windows(p, yf + 1.0, by1 - 1.0, -4.2, 0.8, 0.9, 2.0, 2, lod, -1); side_windows(p, yf + 1.0, by1 - 1.0, 4.2, 0.8, 0.9, 2.0, 2, lod, 1)
    boundary_wall(p, -W / 2, W / 2, y0, lod, gate=(0.0, 2.8), pitch=3.5, side_returns=y0 + 4.0, rail=STONE)
    centred(p); return p


# ------------------------------------------------------------------------------------------------------------------------------------------------ D: traditional mosques
def _pinnacle(p, cx, cy, z, h=1.1, w=0.5, key=KASA):
    p.frustum(cx - w / 2, cy - w / 2, z, cx + w / 2, cy + w / 2, z + h, key, w * 0.38, skip=("bottom",))


def _trad_minaret(p, cx, cy, z0, H, w, lod, key=KASA):
    """A tapering earth minaret: a battered square tower in three stepped stages with a few small openings and a stepped parapet with pinnacles (documentary forms: R-024 to R-029; a flagged interpretation)."""
    if lod >= 2: p.box(cx - w / 2, cy - w / 2, z0, cx + w / 2, cy + w / 2, z0 + H, key); return
    for k, (f0, f1, w0, w1) in enumerate(((0.0, 0.5, 1.0, 0.82), (0.5, 0.82, 0.82, 0.66), (0.82, 1.0, 0.66, 0.58))):
        p.frustum(cx - w * w0 / 2, cy - w * w0 / 2, z0 + H * f0, cx + w * w0 / 2, cy + w * w0 / 2, z0 + H * f1, key, w * (w0 - w1) / 2, skip=("bottom",))
    if lod == 0:
        for z in (0.26, 0.6):
            for dx, dy, face in ((0, -1, "-y"), (0, 1, "+y")): arch_window(p, cx, cy + dy * w * 0.46, z0 + H * z, 0.5, 1.2, DARK, 0.06, None, face)
        for k in range(3): p.box(cx - w * 0.62, cy - w * 0.62, z0 + H * (0.2 + 0.23 * k), cx + w * 0.62, cy + w * 0.62, z0 + H * (0.2 + 0.23 * k) + 0.18, KASA2, skip=("bottom",))      # stage bands
        for sx in (-1, 1):
            for sy in (-1, 1): _pinnacle(p, cx + sx * w * 0.3, cy + sy * w * 0.3, z0 + H, 0.9, 0.32, key)
        for k in range(6): a = k * math.pi / 3; p.tube((cx + math.cos(a) * w * 0.28, cy + math.sin(a) * w * 0.28, z0 + H * 0.62), (cx + math.cos(a) * w * 0.52, cy + math.sin(a) * w * 0.52, z0 + H * 0.62), 0.05, 0.05, 5, "timber_light")   # projecting beams
    p.box(cx - w * 0.3, cy - w * 0.3, z0 + H, cx + w * 0.3, cy + w * 0.3, z0 + H + 0.35, KASA2, skip=("bottom",))


def traditional_a(lod):
    """Traditional mosque (earth render, Hausa-inspired, a FLAGGED interpretation of R-024 to R-029): a rectangular prayer hall with battered walls and a stepped parapet with corner pinnacles (zankwaye), shallow relief panels and pointed arched openings, a courtyard enclosed by a plastered wall with a decorated gateway, a tapering minaret. 26 x 22 m."""
    p = Part("mosque"); W, D = 26.0, 22.0; y0 = -D / 2; yf = -3.0; by1 = D / 2 - 1.5
    if lod >= 2: p.box(-9.0, yf, 0.0, 9.0, by1, 6.5, KASA); _trad_minaret(p, 10.5, yf + 1.2, 0.0, 14.0, 2.2, 2); p.box(-W / 2, y0 - 0.2, 0.0, W / 2, y0 + 0.2, 2.2, KASA); return p
    p.box(-W / 2, y0, 0.0, W / 2, yf, 0.06, "dry_earth", skip=("bottom",))
    p.frustum(-9.2, yf - 0.2, 0.0, 9.2, by1 + 0.2, 6.0, KASA, 0.35, skip=("bottom",)); p.box(-9.0, yf - 0.05, 5.7, 9.0, by1, 6.5, KASA, skip=("bottom",))
    for sx in (-1, 1):
        for yy in (yf + 0.3, by1 - 0.3): _pinnacle(p, sx * 8.7, yy, 6.5, 1.4, 0.7)
    if lod == 0:
        for k in range(9): x = -8.0 + 2.0 * k; _pinnacle(p, x, yf + 0.15, 6.5, 0.7, 0.4); _pinnacle(p, x, by1 - 0.15, 6.5, 0.7, 0.4)
        for sx in (-1, 1): p.box(sx * 9.2 - 0.15, yf, 5.4, sx * 9.2 + 0.15, by1, 5.7, KASA2)
    for x in (-5.5, 5.5): prism_y(p, [(px + x, pz + 0.0) for px, pz in pointed_arch(2.4, 3.6)], yf - 0.25, yf, KASA2); prism_y(p, [(px + x, pz + 0.0) for px, pz in pointed_arch(1.7, 3.0)], yf - 0.4, yf - 0.25, DARK)
    prism_y(p, [(px, pz) for px, pz in pointed_arch(3.2, 4.4)], yf - 0.3, yf, KASA2); prism_y(p, [(px, pz) for px, pz in pointed_arch(2.2, 3.6)], yf - 0.55, yf - 0.3, DARK)
    if lod == 0:
        for x in (-3.4, 3.4, -7.4, 7.4):
            prism_y(p, [(x - 0.7, 3.8), (x, 4.6), (x + 0.7, 3.8), (x, 3.0)], yf - 0.12, yf, "plaster_cream"); prism_y(p, [(x - 0.45, 5.0), (x, 5.45), (x + 0.45, 5.0), (x, 4.55)], yf - 0.12, yf, "plaster_cream")
        side_windows(p, yf + 1.5, by1 - 1.5, -9.0, 0.9, 0.7, 1.8, 3, lod, -1); side_windows(p, yf + 1.5, by1 - 1.5, 9.0, 0.9, 0.7, 1.8, 3, lod, 1)
    _trad_minaret(p, 10.8, yf + 1.5, 0.0, 15.0, 2.4, lod)
    # the plastered courtyard wall with a decorated gateway
    for xa, xb in ((-W / 2, -2.2), (2.2, W / 2)): p.box(xa, y0 - 0.25, 0.0, xb, y0 + 0.25, 2.2, KASA, skip=("bottom",)); p.box(xa, y0 - 0.3, 2.2, xb, y0 + 0.3, 2.45, KASA2, skip=("bottom",))
    if lod <= 1:
        for k in range(10):
            _pinnacle(p, -W / 2 + 1.2 + k * 1.05, y0, 2.45, 0.5, 0.3)
            if lod == 0: _pinnacle(p, 3.1 + k * 1.05, y0, 2.45, 0.5, 0.3)
    p.box(-2.4, y0 - 0.45, 0.0, -2.0, y0 + 0.45, 3.0, KASA, skip=("bottom",)); p.box(2.0, y0 - 0.45, 0.0, 2.4, y0 + 0.45, 3.0, KASA, skip=("bottom",)); p.box(-2.4, y0 - 0.45, 3.0, 2.4, y0 + 0.45, 3.4, KASA2, skip=("bottom",))
    centred(p); return p


def traditional_b(lod):
    """Traditional mosque, smaller (earth render, flagged interpretation): a compact hall with battered walls and corner pinnacles, one pointed entrance with a relief panel, a short stepped minaret, a plain plastered wall with a low gateway. 17 x 15 m."""
    p = Part("mosque"); W, D = 17.0, 15.0; y0 = -D / 2; yf = -2.0; by1 = D / 2 - 1.0
    if lod >= 2: p.box(-5.5, yf, 0.0, 5.5, by1, 5.0, KASA); _trad_minaret(p, 6.5, yf + 1.0, 0.0, 10.0, 1.8, 2); p.box(-W / 2, y0 - 0.2, 0.0, W / 2, y0 + 0.2, 2.0, KASA); return p
    p.box(-W / 2, y0, 0.0, W / 2, yf, 0.06, "dry_earth", skip=("bottom",))
    p.frustum(-5.7, yf - 0.2, 0.0, 5.7, by1 + 0.2, 4.6, KASA, 0.3, skip=("bottom",)); p.box(-5.5, yf - 0.05, 4.4, 5.5, by1, 5.0, KASA, skip=("bottom",))
    for sx in (-1, 1):
        for yy in (yf + 0.25, by1 - 0.25): _pinnacle(p, sx * 5.2, yy, 5.0, 1.1, 0.55)
    prism_y(p, [(px, pz) for px, pz in pointed_arch(2.6, 3.4)], yf - 0.3, yf, KASA2); prism_y(p, [(px, pz) for px, pz in pointed_arch(1.8, 2.8)], yf - 0.5, yf - 0.3, DARK)
    if lod == 0:
        for x in (-3.4, 3.4):
            prism_y(p, [(x - 0.6, 2.6), (x, 3.4), (x + 0.6, 2.6), (x, 1.8)], yf - 0.1, yf, "plaster_cream"); arch_window(p, x, yf, 0.8, 0.7, 1.4, DARK, 0.1, KASA2, "-y")
        for k in range(5): x = -4.0 + 2.0 * k; _pinnacle(p, x, yf + 0.12, 5.0, 0.55, 0.32)
    _trad_minaret(p, 6.6, yf + 1.0, 0.0, 10.5, 1.9, lod)
    p.box(-W / 2, y0 - 0.2, 0.0, -1.6, y0 + 0.2, 2.0, KASA, skip=("bottom",)); p.box(1.6, y0 - 0.2, 0.0, W / 2, y0 + 0.2, 2.0, KASA, skip=("bottom",)); p.box(-W / 2, y0 - 0.25, 2.0, -1.6, y0 + 0.25, 2.2, KASA2, skip=("bottom",)); p.box(1.6, y0 - 0.25, 2.0, W / 2, y0 + 0.25, 2.2, KASA2, skip=("bottom",))
    p.box(-1.8, y0 - 0.35, 0.0, -1.6, y0 + 0.35, 2.8, KASA, skip=("bottom",)); p.box(1.6, y0 - 0.35, 0.0, 1.8, y0 + 0.35, 2.8, KASA, skip=("bottom",))
    centred(p); return p


# ------------------------------------------------------------------------------------------------------------------------------------------------ catalogue extras
extra("kano_mosque_grand_a", "E", "CITY_SPECIFIC", REF_MOSQUE, heads=[(-17.5, -4.5, 22.0), (17.5, -4.5, 22.0), (0.0, -5.0, 12.0), (-20.0, -4.0, 6.0), (20.0, -4.0, 6.0), (-11.0, 18.0, 15.0)], preset="mast", tags=["mosque", "grand_mosque", "landmark_class", "generic_design"], intensity=1.1, range=24, poolRadius=9.0, realLight=False)
extra("kano_mosque_grand_b", "E", "CITY_SPECIFIC", REF_MOSQUE, heads=[(-28.0, -6.0, 20.0), (28.0, -6.0, 20.0), (0.0, -9.0, 10.0), (-14.0, -6.0, 6.0), (14.0, -6.0, 6.0), (0.0, 8.0, 21.0)], preset="mast", tags=["mosque", "grand_mosque", "landmark_class", "generic_design"], intensity=1.1, range=24, poolRadius=9.0, realLight=False)
for _k, _hz in (("a", [(-10.2, -5.4, 12.0), (10.2, -5.4, 12.0), (0.0, -6.0, 6.0)]), ("b", [(13.8, -3.8, 14.0), (0.0, -6.0, 5.0), (-8.0, -6.0, 5.0)]), ("c", [(-9.8, 11.0, 12.0), (9.8, 11.0, 12.0), (0.0, -7.0, 5.0)])):
    extra(f"rg_ngn_mosque_medium_{_k}", "E", "REGIONAL", REF_MOSQUE, heads=_hz, preset="glow", tags=["mosque", "neighbourhood_mosque", "generic_design"], intensity=1.0, range=16, poolRadius=6.0)
for _k, _hz in (("a", [(6.3, -1.8, 8.0), (0.0, -3.0, 4.0)]), ("b", [(8.2, -1.8, 8.0), (0.0, -4.0, 4.0)]), ("c", [(5.1, -1.3, 6.0), (0.0, -2.8, 3.0)])):
    extra(f"rg_ngn_mosque_small_{_k}", "E", "REGIONAL", REF_MOSQUE, heads=_hz, preset="glow", tags=["mosque", "local_mosque", "generic_design"], intensity=0.9, range=12, poolRadius=4.5)
for _k, _hz in (("a", [(10.8, -3.4, 10.0), (0.0, -5.0, 4.0)]), ("b", [(6.6, -2.4, 7.0), (0.0, -3.5, 3.5)])):
    extra(f"rg_ngn_mosque_traditional_{_k}", "E", "REGIONAL", REF_TRAD, heads=_hz, preset="lamp_warm", tags=["mosque", "traditional", "hausa_inspired", "flagged_interpretation"], intensity=0.8, range=12, poolRadius=4.0, realLight=False)
