"""Mosque family for Kano (owner directive of 2026-11-09, references R-037, R-038, R-039 and R-009/R-016..R-019 for the recurring generic skyline mosque; traditional forms from the documentary sources R-024..R-029).
Ten genuine 3D mosques of four classes, none of them a model of any real mosque (owner decision C1: the Kano Central Mosque is a real landmark, blocked on references M-11, and is NOT touched):
  A  grand mosques (CITY_SPECIFIC `kano_mosque_grand_a/b`): R-037 (four minarets, one large ribbed dome, a tall pointed entrance) and R-038 (a wide symmetrical facade, a central iwan with steps)
  B  medium neighbourhood mosques (REGIONAL `rg_ngn_mosque_medium_a/b/c`): a domed hall with two minarets, an arcaded flat-roof hall with small domes and one minaret, a pointed-dome hall with twin slim minarets
  C  small local mosques (REGIONAL `rg_ngn_mosque_small_a/b/c`): compact halls with a small dome or a flat roof and a short minaret
  D  traditional mosques (REGIONAL `rg_ngn_mosque_traditional_a/b`): earth (kasa) render, battered walls, corner pinnacles (zankwaye), relief panels, a tapering minaret: a FLAGGED interpretation of documentary sources, no licensed photograph (M-01 partly open)
Every mosque stands in its compound: forecourt, a yard, a wall round the whole plot with a railed front and a gate, so a placed mosque belongs to its street. Real metres, front at -Y (the road side), origin at the centre of the footprint.

VERSION 2 (2026-11-10, the realism pass; v001 is frozen and untouched). The in-game pictures of review run 42684305591 showed flat ivory boxes with black window holes and a slab floating above every minaret balcony
(a full plate where a rail was meant). v2: a real balcony rail on a corbel, windows that are framed lattice screens (the surface bake paints the frame, the screen and the glass inside the face), a crescent on every main
dome, horn loudspeakers on the minarets, a mihrab on the qibla wall, windows on the back wall, merlons on the parapets, an ablution shelter, a plinth course, and the compound wall carried round the plot.
The assets are painted by transport_kit.surface (one atlas material per LOD), so the colours named here are material PATTERNS, not flat paint."""
from __future__ import annotations
import math
from mathutils import Vector
from kano_kit.geo import Part
from .common import prism_x, extra

STONE, TRIM, DOME, GOLD, RAIL, DARK, GLOW, PAVER, YARD = "stone_ivory", "plaster_cream", "dome_green", "trim_gold", "railing_green", "interior_dark", "lantern_glow", "paver_red", "pavement_grey"
KASA, KASA2, WIN, WINT, DOOR, STEEL = "kasa_render", "plaster_terracotta", "window_lattice", "window_timber", "wood_door", "metal_galvanised"
REF_MOSQUE = ["R-037", "R-038", "R-039", "R-009"]
REF_TRAD = ["R-024", "R-025", "R-026", "R-027", "R-028", "R-029"]
Z = Vector((0.0, 0.0, 1.0))
FACE = {"-y": (Vector((1, 0, 0)), Vector((0, -1, 0))), "+y": (Vector((-1, 0, 0)), Vector((0, 1, 0))), "-x": (Vector((0, -1, 0)), Vector((-1, 0, 0))), "+x": (Vector((0, 1, 0)), Vector((1, 0, 0)))}      # tangent along the wall, outward normal


# ------------------------------------------------------------------------------------------------------------------------------------------------ primitives
def pointed_arch(w, h, rise=0.34, fast=False):
    """Outline (x, z) of a pointed arch of width w and height h: straight jambs up to the springing, then two arcs to the apex (9 points; 5 when `fast`, a gable-topped slot for small openings far from the eye)."""
    s = h * (1 - rise)
    if fast: return [(-w / 2, 0.0), (-w / 2, s), (0.0, h), (w / 2, s), (w / 2, 0.0)]
    return [(-w / 2, 0.0), (-w / 2, s), (-w * 0.41, s + h * rise * 0.45), (-w * 0.2, s + h * rise * 0.82), (0.0, h), (w * 0.2, s + h * rise * 0.82), (w * 0.41, s + h * rise * 0.45), (w / 2, s), (w / 2, 0.0)]


def relief(p: Part, pts, o, U, n, depth, key, sides=True):
    """A polygon (pts = (along, up) pairs in the wall plane through `o`, tangent U, normal n, with U x Z = n) raised `depth` metres off the wall: its front face and, when `sides`, the thin walls round it.
    No back face (it would be buried in the wall)."""
    area = sum(pts[i][0] * pts[(i + 1) % len(pts)][1] - pts[(i + 1) % len(pts)][0] * pts[i][1] for i in range(len(pts)))
    if area < 0: pts = list(reversed(pts))
    P0 = [o + U * a + Z * b for a, b in pts]; P1 = [q + n * depth for q in P0]; p.face_n(P1, key, n)
    if not sides or depth <= 1e-4: return
    for i in range(len(pts)):
        j = (i + 1) % len(pts); d = P0[j] - P0[i]
        if d.length < 1e-6: continue
        if abs(d.z) < 1e-6 and abs(P0[i].z) < 0.06: continue                                       # the sill edge at ground level: nothing to see
        p.face_n([P0[i], P0[j], P1[j], P1[i]], key, d.cross(n))                                   # the outline runs counter-clockwise seen from n, so d x n points out of it


def arch_window(p: Part, x, y, z0, w, h, face="-y", key=WIN, depth=0.07, fast=False, lod=0):
    """A pointed arched window on the wall through (x, y), sill at z0, standing `depth` proud of the wall (a rendered surround): the surface bake paints the frame along its outline and the lattice screen inside it."""
    U, n = FACE[face]; relief(p, pointed_arch(w, h, fast=fast or lod >= 1), Vector((x, y, z0)), U, n, depth if lod == 0 else 0.03, key, sides=(lod == 0))


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


def crescent(p: Part, cx, cy, z, r=0.55, key=GOLD):
    """A crescent with its horns up: a thin blade seen from the road and from behind (two faces)."""
    pts = [(r * math.cos(a), r * math.sin(a)) for a in [math.radians(34.8 - k * (249.6 / 8)) for k in range(9)]]
    pts += [(0.85 * r * math.cos(a), 0.35 * r + 0.85 * r * math.sin(a)) for a in [math.radians(165.0 + k * (210.0 / 6)) for k in range(1, 6)]]
    for sgn in (-1, 1): relief(p, pts, Vector((cx, cy + sgn * 0.02, z + r)), Vector((1, 0, 0)), Vector((0, sgn, 0)), 0.0, key, sides=False)


def finial(p: Part, cx, cy, z, h=1.6, key=GOLD, lod=0, moon=False):
    """A gold finial: a stem, a ball and, on the main dome, a crescent."""
    p.tube((cx, cy, z), (cx, cy, z + h), 0.07, 0.03, 6, key, caps=(False, True))
    if lod == 0: p.blob((cx, cy, z + h * 0.35), (0.17, 0.17, 0.17), key, subdiv=1)
    if moon and lod <= 1: crescent(p, cx, cy, z + h, 0.5 if h > 2.0 else 0.38, key)


def horn(p: Part, cx, cy, z, ang):
    """A horn loudspeaker pointing outwards at angle `ang` (radians): the call to prayer is part of every mosque's silhouette."""
    dx, dy = math.cos(ang), math.sin(ang); p.tube((cx + dx * 0.08, cy + dy * 0.08, z), (cx + dx * 0.5, cy + dy * 0.5, z + 0.04), 0.045, 0.2, 6, STEEL, caps=(True, True), smooth=False)


def minaret(p: Part, cx, cy, z0, H, w, lod=0, shape="square", body=STONE, cap=DOME, trim=TRIM, tiers=(0.28, 0.5, 0.75), horns=True):
    """A minaret: a base block, a square or octagonal shaft with pointed lattice openings, two balconies on corbels with a real rail, horn loudspeakers under the lantern, an onion cap with a finial.
    lod 0 full; 1 shaft, balconies and cap; 2 a box."""
    if lod >= 2: p.box(cx - w / 2, cy - w / 2, z0, cx + w / 2, cy + w / 2, z0 + H, body); return
    seg, roll = (4, math.pi / 4) if shape == "square" else (8, math.pi / 8); R = w / 2 * (math.sqrt(2) if shape == "square" else 1.0); flat = 1.0 if shape == "square" else math.cos(math.pi / 8)
    p.box(cx - w * 0.68, cy - w * 0.68, z0, cx + w * 0.68, cy + w * 0.68, z0 + 1.2, trim, skip=("bottom",))
    zb, zt = z0 + 1.2, z0 + H * 0.9
    p.tube((cx, cy, zb), (cx, cy, zt), R, R * 0.9, seg, body, caps=(False, True), smooth=False, roll=roll)
    for fz in (0.62, 0.9):                                                                                                  # two balconies: a corbel, a slab, and a rail of bars on corner posts
        z = z0 + H * fz; rb = w * 0.72; e = rb * 0.96
        if lod == 0: p.tube((cx, cy, z - 0.55), (cx, cy, z), R * 0.92, rb * math.sqrt(2) * 0.96, 4, trim, caps=(False, False), smooth=False, roll=math.pi / 4)
        p.box(cx - rb, cy - rb, z, cx + rb, cy + rb, z + 0.22, trim, skip=("bottom",) if lod == 0 else ())
        if lod == 0:
            for sx in (-1, 1):
                for sy in (-1, 1): p.box(cx + sx * e - 0.035, cy + sy * e - 0.035, z + 0.22, cx + sx * e + 0.035, cy + sy * e + 0.035, z + 1.12, RAIL, skip=("bottom",))
            for s in (-1, 1):
                for zz in (0.62, 1.05):
                    p.box(cx - e, cy + s * e - 0.025, z + zz, cx + e, cy + s * e + 0.025, z + zz + 0.05, RAIL, skip=("left", "right"))
                    p.box(cx + s * e - 0.025, cy - e, z + zz, cx + s * e + 0.025, cy + e, z + zz + 0.05, RAIL, skip=("front", "back"))
    hw = w * 0.34
    for tier in (tiers if lod == 0 else tiers[:1]):
        z = z0 + H * tier; off = w * 0.5 * flat * (1.0 - 0.1 * (z + H * 0.05 - zb) / (zt - zb)) - 0.015                     # the shaft tapers: the opening sits on the face at its own height
        for face, (dx, dy) in (("-y", (0, -1)), ("+y", (0, 1)), ("-x", (-1, 0)), ("+x", (1, 0))):
            if lod >= 1 and face != "-y": continue
            arch_window(p, cx + dx * off, cy + dy * off, z, hw, H * 0.1, face, WIN, 0.06, fast=True, lod=lod)
    zl = z0 + H * 0.9
    p.tube((cx, cy, zl), (cx, cy, zl + H * 0.04), R * 0.8, R * 0.8, seg, trim, caps=(False, True), smooth=False, roll=roll)
    if lod == 0 and horns:
        for k in range(4): horn(p, cx, cy, zl + 0.75, k * math.pi / 2 + math.pi / 4)
    dome(p, cx, cy, zl + H * 0.04, w * 0.62, H * 0.12, cap, seg=10, rows=5, kind="onion", lod=lod); finial(p, cx, cy, zl + H * 0.16, 1.4, GOLD, lod)


def boundary_wall(p: Part, x0, x1, y, lod=0, gate=(0.0, 4.0), key=TRIM, rail=RAIL, pitch=3.2, bars=5):
    """The compound's front boundary along the road at y: a low wall with capped piers carrying lanterns, a railing between the piers, and a gate (two taller piers, two leaves standing open) in the gap."""
    gx0, gx1 = gate[0] - gate[1] / 2, gate[0] + gate[1] / 2
    if lod >= 2: p.box(x0, y - 0.2, 0.0, x1, y + 0.2, 1.8, key); return
    nb = bars if lod == 0 else 2
    for xa, xb, gate_end in ((x0, gx0, 1), (gx1, x1, 0)):                                                                   # the run left of the gate and the run right of it, each in equal bays
        p.box(xa, y - 0.15, 0.0, xb, y + 0.15, 0.7, key, skip=("bottom",)); m = max(1, int(round((xb - xa) / pitch))); step = (xb - xa) / m
        for i in range(m + 1):
            if i == (m if gate_end else 0): continue                                                                        # that end is the gate pier
            x = xa + i * step; p.box(x - 0.28, y - 0.28, 0.0, x + 0.28, y + 0.28, 1.7, key, skip=("bottom",))
            if lod == 0: p.frustum(x - 0.36, y - 0.36, 1.7, x + 0.36, y + 0.36, 1.86, TRIM, 0.1, skip=("bottom",)); p.box(x - 0.11, y - 0.11, 1.86, x + 0.11, y + 0.11, 2.14, GLOW, skip=("bottom",))
        for i in range(m):
            a = xa + i * step + 0.3; b = xa + (i + 1) * step - 0.3
            p.box(a, y - 0.03, 0.7, b, y + 0.03, 0.78, rail, skip=("bottom",)); p.box(a, y - 0.03, 1.55, b, y + 0.03, 1.62, rail, skip=("bottom",))
            for k in range(nb): x = a + (b - a) * (k + 0.5) / nb; p.box(x - 0.02, y - 0.02, 0.78, x + 0.02, y + 0.02, 1.55, rail, skip=("bottom", "top"))
    for gx, s in ((gx0, 1), (gx1, -1)):                                                                                    # gate piers and the two leaves, swung open into the forecourt
        p.box(gx - 0.32, y - 0.32, 0.0, gx + 0.32, y + 0.32, 2.2, key, skip=("bottom",))
        if lod == 0:
            p.frustum(gx - 0.4, y - 0.4, 2.2, gx + 0.4, y + 0.4, 2.4, TRIM, 0.12, skip=("bottom",)); p.box(gx - 0.12, y - 0.12, 2.4, gx + 0.12, y + 0.12, 2.7, GLOW, skip=("bottom",))
            p.box(gx + s * 0.34, y + 0.34, 0.12, gx + s * 0.4, y + 0.34 + gate[1] * 0.46, 1.7, rail, skip=("bottom",))


def compound(p: Part, W, y0, yb, yf, lod, key=TRIM, yard=YARD, paving=PAVER, h=1.9):
    """The plot: a yard slab under everything, the paved forecourt between the road wall and the facade, and the plain wall along both sides and the back (the railed front is `boundary_wall`)."""
    p.box(-W / 2, y0, 0.0, W / 2, yb, 0.05, yard, skip=("bottom",))
    if paving: p.box(-W / 2 + 0.3, y0 + 0.2, 0.05, W / 2 - 0.3, yf, 0.09, paving, skip=("bottom",))
    for x in (-W / 2 + 0.15, W / 2 - 0.15):
        p.box(x - 0.15, y0 + 0.15, 0.05, x + 0.15, yb - 0.3, h, key, skip=("bottom",))
        if lod == 0: p.box(x - 0.2, y0 + 0.15, h, x + 0.2, yb - 0.3, h + 0.1, TRIM, skip=("bottom",))
    p.box(-W / 2, yb - 0.3, 0.05, W / 2, yb, h, key, skip=("bottom",))
    if lod == 0: p.box(-W / 2, yb - 0.35, h, W / 2, yb, h + 0.1, TRIM, skip=("bottom",))


def windows(p: Part, face, a0, a1, at, z0, w, h, n, lod, key=WIN):
    """A row of n pointed windows spread between a0 and a1 along the wall whose plane is at `at` (x for the side walls, y for the front and back). LOD 1 keeps them flat; LOD 2 has none."""
    if lod >= 2 or n <= 0: return
    for i in range(n):
        a = a0 + (a1 - a0) * (i + 0.5) / n
        if face in ("-y", "+y"): arch_window(p, a, at, z0, w, h, face, key, lod=lod)
        else: arch_window(p, at, a, z0, w, h, face, key, lod=lod)


def iwan(p: Part, cx, yf, w, h, lod, depth=1.2):
    """A tall pointed-arch entrance (iwan): a pale frame standing off the facade, a shadowed recess, the plank door at the back of it, and steps."""
    ft = 0.5 if depth >= 0.8 else depth * 0.55; U, n = FACE["-y"]
    relief(p, pointed_arch(w + 1.0, h + 0.6), Vector((cx, yf, 0.0)), U, n, ft, TRIM)
    relief(p, pointed_arch(w, h), Vector((cx, yf - ft, 0.0)), U, n, 0.02, DARK, sides=False)
    if lod <= 1: relief(p, pointed_arch(w * 0.62, h * 0.72, fast=(lod == 1)), Vector((cx, yf - ft - 0.02, 0.0)), U, n, 0.05, DOOR, sides=(lod == 0))
    if lod == 0:
        for k in range(4): p.box(cx - w / 2 - 0.5 - k * 0.2, yf - ft - 0.35 * (k + 1), 0.0, cx + w / 2 + 0.5 + k * 0.2, yf - ft - 0.35 * k, 0.15 * (4 - k), STONE, skip=("bottom",))


def cornice(p: Part, x0, x1, y0, y1, z, key=TRIM):
    p.box(x0 - 0.3, y0 - 0.3, z, x1 + 0.3, y1 + 0.3, z + 0.35, key, skip=("bottom",))


def plinth(p: Part, x0, x1, y0, y1, key=TRIM, h=0.55):
    """A plinth course standing 6 cm off the walls."""
    p.box(x0 - 0.06, y0 - 0.06, 0.0, x1 + 0.06, y1 + 0.06, h, key, skip=("bottom",))


def parapet_band(p: Part, x0, x1, y0, y1, z, h=0.9, key=STONE):
    p.box(x0, y0, z, x1, y0 + 0.3, z + h, key, skip=("bottom",)); p.box(x0, y1 - 0.3, z, x1, y1, z + h, key, skip=("bottom",))                      # front and back runs; the side runs stop at them (no coplanar overlap)
    p.box(x0, y0 + 0.3, z, x0 + 0.3, y1 - 0.3, z + h, key, skip=("bottom",)); p.box(x1 - 0.3, y0 + 0.3, z, x1, y1 - 0.3, z + h, key, skip=("bottom",))


def merlons(p: Part, x0, x1, y, z, lod, pitch=1.7, key=TRIM):
    """Merlons along a front parapet (LOD 0 only): the crenellated skyline of the references."""
    if lod > 0: return
    n = max(2, int(round((x1 - x0) / pitch)))
    for i in range(n): x = x0 + (x1 - x0) * (i + 0.5) / n; p.frustum(x - 0.36, y - 0.02, z, x + 0.36, y + 0.3, z + 0.62, key, 0.1, skip=("bottom",))


def mihrab(p: Part, cx, yb, h, w=3.0, key=STONE):
    """The mihrab seen from outside: a half-octagon standing out of the qibla (back) wall under a flat cap."""
    pts = [(cx - w / 2, yb), (cx + w / 2, yb), (cx + w / 2, yb + 0.5), (cx + w * 0.22, yb + 1.3), (cx - w * 0.22, yb + 1.3), (cx - w / 2, yb + 0.5)]
    p.prism(pts, 0.0, h, key, caps=False); p.face([(x, y, h) for x, y in pts], TRIM)


def ablution(p: Part, cx, cy, lod, w=5.0, d=2.4):
    """An ablution shelter: a sheet roof on four posts over a low tiled bench with a splash wall."""
    if lod >= 2: return
    for sx in (-1, 1):
        for sy in (-1, 1): p.box(cx + sx * (w / 2 - 0.1) - 0.07, cy + sy * (d / 2 - 0.1) - 0.07, 0.05, cx + sx * (w / 2 - 0.1) + 0.07, cy + sy * (d / 2 - 0.1) + 0.07, 2.5, STEEL, skip=("bottom", "top"))
    p.box(cx - w / 2 - 0.25, cy - d / 2 - 0.25, 2.5, cx + w / 2 + 0.25, cy + d / 2 + 0.25, 2.6, "sheet_green")
    p.box(cx - w / 2 + 0.3, cy - 0.25, 0.05, cx + w / 2 - 0.3, cy + 0.25, 0.5, "tile_floor", skip=("bottom",))
    if lod == 0: p.box(cx - w / 2 + 0.3, cy - 0.06, 0.5, cx + w / 2 - 0.3, cy + 0.06, 1.0, TRIM, skip=("bottom",))


def drum(p: Part, cx, cy, z0, z1, r, lod, n=8, key=STONE):
    """A dome's drum with a ring of small lattice windows."""
    seg = (20 if r > 6 else 16) if lod == 0 else 10
    p.tube((cx, cy, z0), (cx, cy, z1), r, r, seg, key, caps=(False, True), smooth=False)
    if lod >= 2: return
    m = n if lod == 0 else n // 2
    for k in range(m):
        a = 2 * math.pi * (k + 0.5) / m; nv = Vector((math.cos(a), math.sin(a), 0.0)); U = Z.cross(nv)
        relief(p, pointed_arch(min(1.1, r * 0.22), (z1 - z0) * 0.62, fast=True), Vector((cx, cy, z0 + (z1 - z0) * 0.2)) + nv * (r - 0.01), U, nv, 0.05, WIN, sides=(lod == 0))


def ribs(p: Part, cx, cy, z0, r, h, n=12, key=GOLD):
    """Thin meridian ribs on a hemispherical dome (LOD 0)."""
    for k in range(n):
        a = 2 * math.pi * k / n; pts = [Vector((cx + math.cos(a) * r * math.cos(t * math.pi / 2 * 0.97) * 1.005, cy + math.sin(a) * r * math.cos(t * math.pi / 2 * 0.97) * 1.005, z0 + h * math.sin(t * math.pi / 2) + 0.03)) for t in (0.02, 0.35, 0.65, 0.9)]
        p.path_tube(pts, 0.085, 3, key, caps=(True, True), smooth=False)


# ------------------------------------------------------------------------------------------------------------------------------------------------ A: grand mosques
def grand_a(lod):
    """Grand mosque after R-037: a central block with a tall pointed iwan, a large ribbed green dome on a windowed drum, two tall front minarets and two smaller rear minarets, wings with pointed lattice windows and green
    corner domes, a mihrab, an ablution shelter, a forecourt and a railed front wall. 46 x 40 m."""
    p = Part("mosque"); W, D = 46.0, 40.0; y0, yb = -D / 2, D / 2; yf = -4.0; bx0, bx1, by1 = -16.0, 16.0, yb - 3.0; wx0, wx1 = -22.0, 22.0
    if lod >= 2:
        p.box(wx0, yf, 0.0, wx1, by1, 9.0, STONE); p.box(-9.0, 0.0, 9.0, 9.0, 18.0, 13.0, STONE); dome(p, 0, 9, 13.0, 9.5, 9.0, DOME, seg=8, rows=3, lod=2)
        for x in (-17.0, 17.0): p.box(x - 1.6, yf - 1.6, 0.0, x + 1.6, yf + 1.6, 30.0, STONE)
        p.box(-W / 2, y0 - 0.2, 0.0, W / 2, y0 + 0.2, 1.8, TRIM); return p
    compound(p, W, y0, yb, yf, lod)
    p.box(wx0, yf, 0.0, wx1, by1, 7.5, STONE, skip=("bottom",)); cornice(p, wx0, wx1, yf, by1, 7.5)                          # lower hall and wings
    p.box(bx0, yf - 1.0, 0.0, bx1, by1, 12.5, STONE, skip=("bottom",)); cornice(p, bx0, bx1, yf - 1.0, by1, 12.5)         # taller central block
    if lod == 0: plinth(p, wx0, wx1, yf, by1); merlons(p, bx0, bx1, yf - 1.3, 12.85, lod, 2.0)
    iwan(p, 0.0, yf - 1.0, 8.0, 11.0, lod)
    drum(p, 0.0, 9.0, 12.85, 16.5, 8.3, lod, 12)
    p.tube((0, 9.0, 16.5), (0, 9.0, 16.9), 8.6, 8.6, 20 if lod == 0 else 12, TRIM, caps=(False, True), smooth=False)
    dome(p, 0, 9.0, 16.9, 8.4, 8.5, DOME, seg=24, rows=10, lod=lod); p.tube((0, 9.0, 24.8), (0, 9.0, 25.6), 1.1, 0.6, 12, TRIM, caps=(False, True), smooth=False); finial(p, 0, 9.0, 25.6, 2.4, GOLD, lod, moon=True)
    if lod == 0: ribs(p, 0.0, 9.0, 16.9, 8.4, 8.5)
    for sx in (-1, 1):                                                                                                   # wings: windows, green corner domes
        x0, x1 = (wx0, bx0) if sx < 0 else (bx1, wx1)
        windows(p, "-y", x0 + 0.6, x1 - 0.6, yf, 1.2, 1.4, 3.8, 3, lod)
        dome(p, (x0 + x1) / 2, yf + 5.0, 7.85, 2.9, 3.4, DOME, seg=14, rows=6, lod=lod); finial(p, (x0 + x1) / 2, yf + 5.0, 11.2, 1.4, GOLD, lod)
        windows(p, "-x" if sx < 0 else "+x", yf + 2.0, by1 - 2.0, wx0 if sx < 0 else wx1, 1.4, 1.4, 4.0, 5, lod)
        minaret(p, sx * 17.5, yf - 0.5, 0.0, 30.0, 3.4, lod); minaret(p, sx * 11.0, by1 - 2.0, 0.0, 20.0, 2.6, lod, tiers=(0.45,), horns=False)
    windows(p, "-y", bx0 + 1.0, -5.5, yf - 1.0, 1.4, 1.5, 4.4, 2, lod); windows(p, "-y", 5.5, bx1 - 1.0, yf - 1.0, 1.4, 1.5, 4.4, 2, lod)
    windows(p, "+y", -9.0, -2.6, by1, 1.4, 1.4, 4.0, 2, lod); windows(p, "+y", 2.6, 9.0, by1, 1.4, 1.4, 4.0, 2, lod); mihrab(p, 0.0, by1, 8.0, 3.6)
    ablution(p, 18.5, -11.0, lod, 5.0, 2.4)
    boundary_wall(p, -W / 2, W / 2, y0, lod, gate=(0.0, 5.0), bars=3, pitch=4.0)
    return p


def grand_b(lod):
    """Grand mosque after R-038: a wide symmetrical facade with a tall central iwan and a flight of steps, a green dome on a windowed drum, two tall corner minarets, two small minarets beside the dome, long arcades of
    lattice windows, low green wing domes, a mihrab, an ablution shelter, a forecourt and a railed wall. 58 x 42 m."""
    p = Part("mosque"); W, D = 58.0, 42.0; y0, yb = -D / 2, D / 2; yf = -5.0; by1 = yb - 3.0
    if lod >= 2:
        p.box(-26.0, yf, 0.0, 26.0, by1, 8.0, STONE); p.box(-8.0, 0.0, 8.0, 8.0, 16.0, 14.0, STONE); dome(p, 0, 8, 14.0, 8.0, 8.0, DOME, seg=8, rows=3, lod=2)
        for x in (-27.0, 27.0): p.box(x - 1.5, yf - 1.5, 0.0, x + 1.5, yf + 1.5, 28.0, STONE)
        p.box(-W / 2, y0 - 0.2, 0.0, W / 2, y0 + 0.2, 1.8, TRIM); return p
    compound(p, W, y0, yb, yf - 3.0, lod)
    p.box(-26.0, yf, 0.0, 26.0, by1, 8.0, STONE, skip=("bottom",)); cornice(p, -26.0, 26.0, yf, by1, 8.0)                    # long hall
    p.box(-9.0, yf - 3.0, 0.0, 9.0, by1, 14.0, STONE, skip=("bottom",)); cornice(p, -9.0, 9.0, yf - 3.0, by1, 14.0)       # tall central block with the iwan
    if lod == 0: plinth(p, -26.0, 26.0, yf, by1); merlons(p, -9.0, 9.0, yf - 3.3, 14.35, lod, 1.8)
    iwan(p, 0.0, yf - 3.0, 8.5, 12.0, lod, 1.6)
    drum(p, 0.0, 8.0, 14.35, 17.8, 7.2, lod, 12); p.tube((0, 8.0, 17.8), (0, 8.0, 18.2), 7.5, 7.5, 20 if lod == 0 else 12, TRIM, caps=(False, True), smooth=False)
    dome(p, 0, 8.0, 18.2, 7.3, 7.6, DOME, seg=20, rows=9, lod=lod); finial(p, 0, 8.0, 25.8, 2.4, GOLD, lod, moon=True)
    for sx in (-1, 1):
        xa, xb = (9.0, 25.5) if sx > 0 else (-25.5, -9.0)
        windows(p, "-y", xa + 0.6, xb - 0.6, yf, 1.1, 1.3, 4.6, 6, lod)                                                    # long arcade
        for k in range(3): dome(p, xa + (xb - xa) * (k + 0.5) / 3, yf + 6.0, 8.35, 2.6, 2.8, DOME, seg=10, rows=4, lod=lod)
        minaret(p, sx * 28.0, yf - 1.0, 0.0, 27.0, 3.2, lod); minaret(p, sx * 12.5, 9.0, 0.0, 19.0, 2.4, lod, shape="oct", tiers=(0.45,), horns=False)
        windows(p, "-x" if sx < 0 else "+x", yf + 2.0, by1 - 2.0, sx * 26.0, 1.4, 1.4, 4.2, 5, lod)
        windows(p, "+y", xa + 1.0, xb - 1.0, by1, 1.4, 1.3, 4.0, 3, lod)
    windows(p, "-y", -8.0, -5.0, yf - 3.0, 1.2, 1.2, 4.4, 1, lod); windows(p, "-y", 5.0, 8.0, yf - 3.0, 1.2, 1.2, 4.4, 1, lod); mihrab(p, 0.0, by1, 9.0, 3.8)
    ablution(p, 20.0, -14.0, lod, 6.0, 2.4)
    boundary_wall(p, -W / 2, W / 2, y0, lod, gate=(0.0, 6.0), bars=3, pitch=4.5)
    return p


# ------------------------------------------------------------------------------------------------------------------------------------------------ B: medium mosques
def medium_a(lod):
    """Medium mosque: a square hall under one green dome on a windowed drum, an entrance with a pointed arch, two slim minarets flanking it, lattice windows on all four sides, a mihrab, an ablution shelter. 26 x 26 m."""
    p = Part("mosque"); W, D = 26.0, 26.0; y0, yb = -D / 2, D / 2; yf = -5.0; by1 = D / 2 - 1.5
    if lod >= 2:
        p.box(-9.0, yf, 0.0, 9.0, by1, 7.0, STONE); dome(p, 0, 3, 7.0, 5.5, 4.0, DOME, seg=8, rows=3, lod=2)
        for x in (-9.5, 9.5): p.box(x - 0.9, yf - 0.9, 0.0, x + 0.9, yf + 0.9, 15.0, STONE)
        p.box(-W / 2, y0 - 0.2, 0.0, W / 2, y0 + 0.2, 1.8, TRIM); return p
    compound(p, W, y0, yb, yf, lod); p.box(-9.0, yf, 0.0, 9.0, by1, 7.0, STONE, skip=("bottom",)); cornice(p, -9.0, 9.0, yf, by1, 7.0)
    if lod == 0: plinth(p, -9.0, 9.0, yf, by1); merlons(p, -8.6, 8.6, yf - 0.3, 7.35, lod)
    iwan(p, 0.0, yf, 3.6, 5.2, lod, 0.9)
    drum(p, 0.0, 3.0, 7.35, 9.4, 5.4, lod, 8); dome(p, 0, 3.0, 9.4, 5.5, 4.4, DOME, seg=20, rows=8, lod=lod); finial(p, 0, 3.0, 13.8, 1.8, GOLD, lod, moon=True)
    windows(p, "-y", -8.5, -2.6, yf, 1.0, 1.2, 3.4, 2, lod); windows(p, "-y", 2.6, 8.5, yf, 1.0, 1.2, 3.4, 2, lod)
    windows(p, "-x", yf + 1.5, by1 - 1.5, -9.0, 1.0, 1.2, 3.4, 4, lod); windows(p, "+x", yf + 1.5, by1 - 1.5, 9.0, 1.0, 1.2, 3.4, 4, lod)
    windows(p, "+y", -8.0, -2.4, by1, 1.0, 1.2, 3.4, 2, lod); windows(p, "+y", 2.4, 8.0, by1, 1.0, 1.2, 3.4, 2, lod); mihrab(p, 0.0, by1, 5.4, 2.8)
    for sx in (-1, 1): minaret(p, sx * 10.2, yf - 0.4, 0.0, 17.0, 1.9, lod)
    ablution(p, -9.6, -9.6, lod, 4.4, 2.2)
    boundary_wall(p, -W / 2, W / 2, y0, lod, gate=(0.0, 4.0), pitch=3.25)
    return p


def medium_b(lod):
    """Medium mosque: a long flat-roofed prayer hall behind an arcade of pointed bays, three small green domes along the roof, one tall octagonal minaret at the corner, a parapet with merlons, a mihrab. 30 x 24 m."""
    p = Part("mosque"); W, D = 30.0, 24.0; y0, yb = -D / 2, D / 2; yf = -4.0; by1 = D / 2 - 1.5
    if lod >= 2:
        p.box(-12.0, yf, 0.0, 12.0, by1, 7.0, STONE); p.box(11.0, yf - 0.8, 0.0, 12.8, yf + 1.0, 18.0, STONE); p.box(-W / 2, y0 - 0.2, 0.0, W / 2, y0 + 0.2, 1.8, TRIM); return p
    compound(p, W, y0, yb, yf - 2.2, lod); p.box(-12.0, yf, 0.0, 12.0, by1, 6.6, STONE, skip=("bottom",)); cornice(p, -12.0, 12.0, yf, by1, 6.6); parapet_band(p, -12.0, 12.0, yf, by1, 6.95, 0.9, STONE)
    merlons(p, -11.7, 11.7, yf, 7.85, lod)
    p.box(-12.0, yf - 2.2, 0.09, 12.0, yf, 0.3, STONE, skip=("bottom",)); p.box(-12.0, yf - 2.2, 4.6, 12.0, yf, 5.0, TRIM)                             # arcade floor and roof slab
    for k in range(9): x = -11.5 + k * 2.875; p.box(x - 0.25, yf - 2.0, 0.3, x + 0.25, yf - 1.6, 4.6, TRIM, skip=("bottom", "top"))                # arcade piers
    U, n = FACE["-y"]
    if lod == 0:                                                                                                                                    # the pointed heads of the arcade bays: spandrels between the piers
        for k in range(8):
            xc = -11.5 + (k + 0.5) * 2.875
            for s in (-1, 1): relief(p, [(s * 1.19, 3.2), (s * 1.19, 4.6), (0.0, 4.6)], Vector((xc, yf - 2.0, 0.0)), U, n, 0.0, TRIM, sides=False)
    windows(p, "-y", -11.0, -2.0, yf, 1.0, 1.4, 3.3, 3, lod); windows(p, "-y", 2.0, 11.0, yf, 1.0, 1.4, 3.3, 3, lod)
    relief(p, pointed_arch(2.2, 3.5, fast=(lod > 0)), Vector((0.0, yf, 0.3)), U, n, 0.08, DOOR, sides=(lod == 0))
    for k in range(3): dome(p, -6.0 + 6.0 * k, 5.0, 7.85, 2.8, 3.0, DOME, seg=14, rows=6, lod=lod); finial(p, -6.0 + 6.0 * k, 5.0, 10.8, 1.4, GOLD, lod, moon=(k == 1))
    windows(p, "-x", yf + 1.5, by1 - 1.5, -12.0, 1.0, 1.2, 3.4, 4, lod); windows(p, "+x", yf + 1.5, by1 - 1.5, 12.0, 1.0, 1.2, 3.4, 4, lod)
    windows(p, "+y", -11.0, -2.4, by1, 1.0, 1.2, 3.4, 3, lod); windows(p, "+y", 2.4, 11.0, by1, 1.0, 1.2, 3.4, 3, lod); mihrab(p, 0.0, by1, 5.2, 2.8)
    minaret(p, 13.8, yf + 0.2, 0.0, 18.0, 2.4, lod, shape="oct")
    ablution(p, -11.4, -9.6, lod, 4.4, 2.0)
    boundary_wall(p, -W / 2, W / 2, y0, lod, gate=(-4.0, 4.0), pitch=3.3)
    return p


def medium_c(lod):
    """Medium mosque: a hall under one pointed dome with four small corner domes, an entrance portico, twin slim minarets at the back corners, a mihrab between them. 28 x 28 m."""
    p = Part("mosque"); W, D = 28.0, 28.0; y0, yb = -D / 2, D / 2; yf = -5.0; by1 = D / 2 - 2.0
    if lod >= 2:
        p.box(-9.0, yf, 0.0, 9.0, by1, 8.0, STONE); dome(p, 0, 3.0, 8.0, 5.5, 5.0, DOME, seg=8, rows=3, lod=2)
        for x in (-9.0, 9.0): p.box(x - 0.8, by1 - 1.6, 0.0, x + 0.8, by1, 17.0, STONE)
        p.box(-W / 2, y0 - 0.2, 0.0, W / 2, y0 + 0.2, 1.8, TRIM); return p
    compound(p, W, y0, yb, yf - 2.4, lod); p.box(-9.0, yf, 0.0, 9.0, by1, 7.0, STONE, skip=("bottom",)); cornice(p, -9.0, 9.0, yf, by1, 7.0)
    if lod == 0: plinth(p, -9.0, 9.0, yf, by1)
    p.box(-4.0, yf - 2.4, 0.0, 4.0, yf, 5.4, STONE, skip=("bottom",)); cornice(p, -4.0, 4.0, yf - 2.4, yf - 0.3, 5.4); iwan(p, 0.0, yf - 2.4, 3.2, 4.3, lod, 0.8); merlons(p, -3.7, 3.7, yf - 2.7, 5.75, lod, 1.5)                # portico
    drum(p, 0.0, 3.0, 7.35, 9.0, 5.0, lod, 8); dome(p, 0, 3.0, 9.0, 5.0, 6.2, DOME, seg=20, rows=9, kind="pointed", lod=lod); finial(p, 0, 3.0, 15.2, 1.8, GOLD, lod, moon=True)
    for sx in (-1, 1):
        for sy in (yf + 1.8, by1 - 1.8): dome(p, sx * 7.0, sy, 7.35, 1.5, 1.8, DOME, seg=10, rows=5, lod=lod)
        minaret(p, sx * 9.8, by1 - 0.8, 0.0, 17.0, 1.8, lod)
        windows(p, "-x" if sx < 0 else "+x", yf + 1.5, by1 - 2.5, sx * 9.0, 1.0, 1.2, 3.4, 4, lod)
    windows(p, "-y", -8.5, -4.6, yf, 1.0, 1.1, 3.4, 2, lod); windows(p, "-y", 4.6, 8.5, yf, 1.0, 1.1, 3.4, 2, lod)
    windows(p, "+y", -7.6, -2.4, by1, 1.0, 1.2, 3.4, 2, lod); windows(p, "+y", 2.4, 7.6, by1, 1.0, 1.2, 3.4, 2, lod); mihrab(p, 0.0, by1, 5.4, 2.8)
    ablution(p, 10.4, -10.6, lod, 4.4, 2.2)
    boundary_wall(p, -W / 2, W / 2, y0, lod, gate=(0.0, 4.0), pitch=3.5)
    return p


# ------------------------------------------------------------------------------------------------------------------------------------------------ C: small mosques
def small_a(lod):
    """Small local mosque: a compact hall under one small green dome, a short square minaret beside it, a pointed entrance, a mihrab, a low railed wall. 18 x 16 m."""
    p = Part("mosque"); W, D = 18.0, 16.0; y0, yb = -D / 2, D / 2; yf = -2.5; by1 = D / 2 - 1.0
    if lod >= 2: p.box(-5.0, yf, 0.0, 5.0, by1, 5.0, STONE); p.box(5.5, yf, 0.0, 7.0, yf + 1.5, 11.0, STONE); p.box(-W / 2, y0 - 0.15, 0.0, W / 2, y0 + 0.15, 1.5, TRIM); return p
    compound(p, W, y0, yb, yf, lod, h=1.7); p.box(-5.0, yf, 0.0, 5.0, by1, 4.8, STONE, skip=("bottom",)); cornice(p, -5.0, 5.0, yf, by1, 4.8)
    if lod == 0: plinth(p, -5.0, 5.0, yf, by1, h=0.45); merlons(p, -4.7, 4.7, yf - 0.3, 5.15, lod, 1.5)
    iwan(p, 0.0, yf, 2.4, 3.3, lod, 0.6); windows(p, "-y", -4.6, -1.8, yf, 0.8, 1.0, 2.4, 1, lod); windows(p, "-y", 1.8, 4.6, yf, 0.8, 1.0, 2.4, 1, lod)
    dome(p, 0, (yf + by1) / 2, 5.15, 3.4, 3.2, DOME, seg=16, rows=7, lod=lod); finial(p, 0, (yf + by1) / 2, 8.3, 1.3, GOLD, lod, moon=True)
    minaret(p, 6.3, yf + 0.7, 0.0, 11.0, 1.4, lod)
    windows(p, "-x", yf + 1.0, by1 - 1.0, -5.0, 0.9, 1.0, 2.4, 3, lod); windows(p, "+x", yf + 3.0, by1 - 1.0, 5.0, 0.9, 1.0, 2.4, 2, lod)
    windows(p, "+y", -4.4, -1.8, by1, 0.9, 1.0, 2.4, 1, lod); windows(p, "+y", 1.8, 4.4, by1, 0.9, 1.0, 2.4, 1, lod); mihrab(p, 0.0, by1, 3.6, 2.2)
    boundary_wall(p, -W / 2, W / 2, y0, lod, gate=(0.0, 3.2), pitch=3.0)
    return p


def small_b(lod):
    """Small local mosque: a flat-roofed rectangular prayer hall with a hip-roofed porch, a short square minaret, a parapet, a mihrab, a low wall. 20 x 16 m."""
    p = Part("mosque"); W, D = 20.0, 16.0; y0, yb = -D / 2, D / 2; yf = -2.5; by1 = D / 2 - 1.0
    if lod >= 2: p.box(-7.0, yf, 0.0, 7.0, by1, 4.6, STONE); p.box(7.5, yf, 0.0, 8.9, yf + 1.4, 10.0, STONE); p.box(-W / 2, y0 - 0.15, 0.0, W / 2, y0 + 0.15, 1.5, TRIM); return p
    compound(p, W, y0, yb, yf - 1.8, lod, h=1.7); p.box(-7.0, yf, 0.0, 7.0, by1, 4.4, STONE, skip=("bottom",)); cornice(p, -7.0, 7.0, yf, by1, 4.4); parapet_band(p, -7.0, 7.0, yf, by1, 4.75, 0.7, STONE)
    if lod == 0: plinth(p, -7.0, 7.0, yf, by1, h=0.45)
    p.box(-2.2, yf - 1.8, 0.0, 2.2, yf, 3.4, TRIM, skip=("bottom",)); prism_x(p, [(yf - 2.0, 3.4), (yf + 0.2, 3.4), (yf + 0.2, 4.6), (yf - 1.0, 4.2)], -2.6, 2.6, "sheet_green"); iwan(p, 0.0, yf - 1.8, 2.0, 2.8, lod, 0.5)
    windows(p, "-y", -6.5, -2.8, yf, 0.8, 1.0, 2.2, 2, lod); windows(p, "-y", 2.8, 6.5, yf, 0.8, 1.0, 2.2, 2, lod)
    windows(p, "-x", yf + 1.0, by1 - 1.0, -7.0, 0.9, 1.0, 2.2, 3, lod); windows(p, "+x", yf + 3.0, by1 - 1.0, 7.0, 0.9, 1.0, 2.2, 2, lod)
    windows(p, "+y", -6.2, -1.8, by1, 0.9, 1.0, 2.2, 2, lod); windows(p, "+y", 1.8, 6.2, by1, 0.9, 1.0, 2.2, 2, lod); mihrab(p, 0.0, by1, 3.4, 2.2)
    minaret(p, 8.2, yf + 0.7, 0.0, 10.0, 1.4, lod)
    boundary_wall(p, -W / 2, W / 2, y0, lod, gate=(0.0, 3.2), pitch=3.3)
    return p


def small_c(lod):
    """Small local prayer house: a compact flat-roofed hall with a small dome over the prayer niche, a short minaret, a plain wall. 14 x 12 m."""
    p = Part("mosque"); W, D = 14.0, 12.0; y0, yb = -D / 2, D / 2; yf = -1.8; by1 = D / 2 - 0.8
    if lod >= 2: p.box(-4.2, yf, 0.0, 4.2, by1, 4.0, STONE); p.box(4.5, yf, 0.0, 5.7, yf + 1.2, 8.0, STONE); p.box(-W / 2, y0 - 0.15, 0.0, W / 2, y0 + 0.15, 1.5, TRIM); return p
    compound(p, W, y0, yb, yf, lod, h=1.6, paving=None); p.box(-4.2, yf, 0.0, 4.2, by1, 3.8, STONE, skip=("bottom",)); cornice(p, -4.2, 4.2, yf, by1, 3.8); parapet_band(p, -4.2, 4.2, yf, by1, 4.15, 0.6, STONE)
    if lod == 0: plinth(p, -4.2, 4.2, yf, by1, h=0.4)
    iwan(p, 0.0, yf, 1.8, 2.5, lod, 0.5); windows(p, "-y", -3.9, -1.5, yf, 0.7, 0.9, 2.0, 1, lod); windows(p, "-y", 1.5, 3.9, yf, 0.7, 0.9, 2.0, 1, lod)
    dome(p, 0, by1 - 1.6, 4.1, 1.8, 1.8, DOME, seg=12, rows=5, lod=lod); finial(p, 0, by1 - 1.6, 5.9, 1.0, GOLD, lod, moon=True)
    minaret(p, 5.1, yf + 0.5, 0.0, 8.0, 1.1, lod, tiers=(0.3, 0.6))
    windows(p, "-x", yf + 1.0, by1 - 1.0, -4.2, 0.8, 0.9, 2.0, 2, lod); windows(p, "+x", yf + 2.4, by1 - 1.0, 4.2, 0.8, 0.9, 2.0, 2, lod); mihrab(p, 0.0, by1, 3.0, 1.9)
    boundary_wall(p, -W / 2, W / 2, y0, lod, gate=(0.0, 2.8), pitch=3.5, rail=STONE)
    return p


# ------------------------------------------------------------------------------------------------------------------------------------------------ D: traditional mosques
def _pinnacle(p, cx, cy, z, h=1.1, w=0.5, key=KASA):
    p.frustum(cx - w / 2, cy - w / 2, z, cx + w / 2, cy + w / 2, z + h, key, w * 0.38, skip=("bottom",))


def _beams(p, a0, a1, at, z, n, out, axis="x"):
    """The timber beams (toron) that stand out of an earth wall: a row of short poles from inside the wall at `at` to `out` metres beyond it (negative = towards -Y or -X)."""
    back = at - 0.2 * (1 if out > 0 else -1)
    for k in range(n):
        a = a0 + (a1 - a0) * (k + 0.5) / n
        if axis == "x": p.tube((a, back, z), (a, at + out, z), 0.055, 0.045, 5, "timber_weathered", caps=(False, True), smooth=False)
        else: p.tube((back, a, z), (at + out, a, z), 0.055, 0.045, 5, "timber_weathered", caps=(False, True), smooth=False)


def _trad_minaret(p, cx, cy, z0, H, w, lod, key=KASA):
    """A tapering earth minaret: a battered square tower in three stepped stages with small timber-framed openings, projecting beams and a parapet with pinnacles (documentary forms: R-024 to R-029; a flagged interpretation)."""
    if lod >= 2: p.box(cx - w / 2, cy - w / 2, z0, cx + w / 2, cy + w / 2, z0 + H, key); return
    for k, (f0, f1, w0, w1) in enumerate(((0.0, 0.5, 1.0, 0.82), (0.5, 0.82, 0.82, 0.66), (0.82, 1.0, 0.66, 0.58))):
        p.frustum(cx - w * w0 / 2, cy - w * w0 / 2, z0 + H * f0, cx + w * w0 / 2, cy + w * w0 / 2, z0 + H * f1, key, w * (w0 - w1) / 2, skip=("bottom",))
    if lod == 0:
        for z, ww in ((0.26, 0.905), (0.6, 0.77)):
            for face, (dx, dy) in (("-y", (0, -1)), ("+y", (0, 1))): arch_window(p, cx + dx * w * ww * 0.5, cy + dy * w * ww * 0.5, z0 + H * z, 0.5, 1.2, face, WINT, 0.05, fast=True)
        for k in range(3): p.box(cx - w * 0.62, cy - w * 0.62, z0 + H * (0.2 + 0.23 * k), cx + w * 0.62, cy + w * 0.62, z0 + H * (0.2 + 0.23 * k) + 0.18, KASA2, skip=("bottom",))      # stage bands
        for sx in (-1, 1):
            for sy in (-1, 1): _pinnacle(p, cx + sx * w * 0.3, cy + sy * w * 0.3, z0 + H, 0.9, 0.32, key)
        for k in range(6): a = k * math.pi / 3; p.tube((cx + math.cos(a) * w * 0.28, cy + math.sin(a) * w * 0.28, z0 + H * 0.62), (cx + math.cos(a) * w * 0.6, cy + math.sin(a) * w * 0.6, z0 + H * 0.62), 0.05, 0.05, 5, "timber_weathered", smooth=False)
    p.box(cx - w * 0.3, cy - w * 0.3, z0 + H, cx + w * 0.3, cy + w * 0.3, z0 + H + 0.35, KASA2, skip=("bottom",))


def _trad_wall(p, W, y0, yb, lod, gate_w, h, key=KASA):
    """The earth wall round a traditional compound: a decorated gateway on the road side, plain walls with a coping round the rest."""
    for xa, xb in ((-W / 2, -gate_w / 2), (gate_w / 2, W / 2)): p.box(xa, y0 - 0.25, 0.0, xb, y0 + 0.25, h, key, skip=("bottom",)); p.box(xa, y0 - 0.3, h, xb, y0 + 0.3, h + 0.22, KASA2, skip=("bottom",))
    if lod <= 1:
        for x in (-W / 2 + 0.2, W / 2 - 0.2): p.box(x - 0.2, y0 + 0.25, 0.0, x + 0.2, yb - 0.4, h - 0.2, key, skip=("bottom",))
        p.box(-W / 2, yb - 0.4, 0.0, W / 2, yb, h - 0.2, key, skip=("bottom",))
    if lod == 0:
        n = int((W / 2 - gate_w / 2 - 0.8) / 1.05)
        for k in range(n): _pinnacle(p, -W / 2 + 0.6 + k * 1.05, y0, h + 0.22, 0.5, 0.3); _pinnacle(p, W / 2 - 0.6 - k * 1.05, y0, h + 0.22, 0.5, 0.3)
    g = gate_w / 2
    p.box(-g - 0.2, y0 - 0.45, 0.0, -g + 0.2, y0 + 0.45, h + 0.75, key, skip=("bottom",)); p.box(g - 0.2, y0 - 0.45, 0.0, g + 0.2, y0 + 0.45, h + 0.75, key, skip=("bottom",))
    p.box(-g - 0.2, y0 - 0.45, h + 0.75, g + 0.2, y0 + 0.45, h + 1.15, KASA2, skip=("bottom",))
    if lod == 0:
        for sx in (-1, 1): _pinnacle(p, sx * g, y0, h + 1.15, 0.7, 0.4)


def traditional_a(lod):
    """Traditional mosque (earth render, Hausa-inspired, a FLAGGED interpretation of R-024 to R-029): a rectangular prayer hall with battered walls and a stepped parapet with corner pinnacles (zankwaye), relief panels,
    three pointed plank doors, projecting beams, a courtyard inside an earth wall with a decorated gateway, a tapering minaret. 26 x 22 m."""
    p = Part("mosque"); W, D = 26.0, 22.0; y0, yb = -D / 2, D / 2; yf = -3.0; by1 = D / 2 - 1.5
    if lod >= 2: p.box(-9.0, yf, 0.0, 9.0, by1, 6.5, KASA); _trad_minaret(p, 10.5, yf + 1.2, 0.0, 14.0, 2.2, 2); p.box(-W / 2, y0 - 0.2, 0.0, W / 2, y0 + 0.2, 2.2, KASA); return p
    p.box(-W / 2, y0, 0.0, W / 2, yb, 0.05, "dry_earth", skip=("bottom",))
    p.frustum(-9.2, yf - 0.2, 0.0, 9.2, by1 + 0.2, 6.0, KASA, 0.35, skip=("bottom",)); p.box(-9.0, yf - 0.05, 5.7, 9.0, by1, 6.5, KASA, skip=("bottom",))
    for sx in (-1, 1):
        for yy in (yf + 0.3, by1 - 0.3): _pinnacle(p, sx * 8.7, yy, 6.5, 1.4, 0.7)
    U, n = FACE["-y"]
    if lod == 0:
        for k in range(9): x = -8.0 + 2.0 * k; _pinnacle(p, x, yf + 0.15, 6.5, 0.7, 0.4); _pinnacle(p, x, by1 - 0.15, 6.5, 0.7, 0.4)
        for sx in (-1, 1): p.box(sx * 9.2 - 0.15, yf, 5.4, sx * 9.2 + 0.15, by1, 5.7, KASA2)
        _beams(p, -8.4, 8.4, yf + 0.1, 5.0, 9, -0.6); _beams(p, yf + 1.0, by1 - 1.0, -8.9, 5.0, 6, -0.6, "y"); _beams(p, yf + 1.0, by1 - 1.0, 8.9, 5.0, 6, 0.6, "y")
    for x, ww, hh in ((-5.5, 2.4, 3.6), (5.5, 2.4, 3.6), (0.0, 3.2, 4.4)):
        relief(p, pointed_arch(ww, hh), Vector((x, yf - 0.02, 0.0)), U, n, 0.25, KASA2); relief(p, pointed_arch(ww - 0.7, hh - 0.6, fast=(lod > 0)), Vector((x, yf - 0.27, 0.0)), U, n, 0.04, DOOR, sides=False)
    if lod == 0:
        for x in (-3.4, 3.4, -7.4, 7.4):
            relief(p, [(-0.7, 3.8), (0.0, 3.0), (0.7, 3.8), (0.0, 4.6)], Vector((x, yf - 0.02, 0.0)), U, n, 0.1, "plaster_cream"); relief(p, [(-0.45, 5.0), (0.0, 4.55), (0.45, 5.0), (0.0, 5.45)], Vector((x, yf - 0.02, 0.0)), U, n, 0.1, "plaster_cream")
        windows(p, "-x", yf + 1.5, by1 - 1.5, -9.12, 0.9, 0.7, 1.8, 3, lod, WINT); windows(p, "+x", yf + 1.5, by1 - 1.5, 9.12, 0.9, 0.7, 1.8, 3, lod, WINT)
    mihrab(p, 0.0, by1 + 0.2, 4.6, 2.6, KASA)
    _trad_minaret(p, 10.8, yf + 1.5, 0.0, 15.0, 2.4, lod)
    _trad_wall(p, W, y0, yb, lod, 4.4, 2.2)
    return p


def traditional_b(lod):
    """Traditional mosque, smaller (earth render, flagged interpretation): a compact hall with battered walls and corner pinnacles, one pointed plank door with relief panels, projecting beams, a short stepped minaret,
    an earth wall with a low gateway. 17 x 15 m."""
    p = Part("mosque"); W, D = 17.0, 15.0; y0, yb = -D / 2, D / 2; yf = -2.0; by1 = D / 2 - 1.0
    if lod >= 2: p.box(-5.5, yf, 0.0, 5.5, by1, 5.0, KASA); _trad_minaret(p, 6.5, yf + 1.0, 0.0, 10.0, 1.8, 2); p.box(-W / 2, y0 - 0.2, 0.0, W / 2, y0 + 0.2, 2.0, KASA); return p
    p.box(-W / 2, y0, 0.0, W / 2, yb, 0.05, "dry_earth", skip=("bottom",))
    p.frustum(-5.7, yf - 0.2, 0.0, 5.7, by1 + 0.2, 4.6, KASA, 0.3, skip=("bottom",)); p.box(-5.5, yf - 0.05, 4.4, 5.5, by1, 5.0, KASA, skip=("bottom",))
    for sx in (-1, 1):
        for yy in (yf + 0.25, by1 - 0.25): _pinnacle(p, sx * 5.2, yy, 5.0, 1.1, 0.55)
    U, n = FACE["-y"]
    relief(p, pointed_arch(2.6, 3.4), Vector((0.0, yf - 0.02, 0.0)), U, n, 0.28, KASA2); relief(p, pointed_arch(1.8, 2.8, fast=(lod > 0)), Vector((0.0, yf - 0.3, 0.0)), U, n, 0.04, DOOR, sides=False)
    if lod == 0:
        for x in (-3.4, 3.4): relief(p, [(-0.6, 2.6), (0.0, 1.8), (0.6, 2.6), (0.0, 3.4)], Vector((x, yf - 0.02, 0.0)), U, n, 0.1, "plaster_cream")
        for k in range(5): x = -4.0 + 2.0 * k; _pinnacle(p, x, yf + 0.12, 5.0, 0.55, 0.32)
        _beams(p, -5.0, 5.0, yf + 0.1, 3.9, 6, -0.55)
        windows(p, "-x", yf + 1.2, by1 - 1.2, -5.6, 0.9, 0.6, 1.5, 2, lod, WINT); windows(p, "+x", yf + 3.0, by1 - 1.2, 5.6, 0.9, 0.6, 1.5, 1, lod, WINT)
    mihrab(p, 0.0, by1 + 0.2, 3.6, 2.2, KASA)
    _trad_minaret(p, 6.6, yf + 1.0, 0.0, 10.5, 1.9, lod)
    _trad_wall(p, W, y0, yb, lod, 3.4, 2.0)
    return p


# ------------------------------------------------------------------------------------------------------------------------------------------------ catalogue extras
extra("kano_mosque_grand_a", "E", "CITY_SPECIFIC", REF_MOSQUE, heads=[(-17.5, -4.5, 22.0), (17.5, -4.5, 22.0), (0.0, -5.0, 12.0), (-20.0, -4.0, 6.0), (20.0, -4.0, 6.0), (-11.0, 15.0, 15.0)], preset="mast", tags=["mosque", "grand_mosque", "landmark_class", "generic_design"], intensity=1.1, range=24, poolRadius=9.0, realLight=False)
extra("kano_mosque_grand_b", "E", "CITY_SPECIFIC", REF_MOSQUE, heads=[(-28.0, -6.0, 20.0), (28.0, -6.0, 20.0), (0.0, -9.0, 10.0), (-14.0, -6.0, 6.0), (14.0, -6.0, 6.0), (0.0, 8.0, 21.0)], preset="mast", tags=["mosque", "grand_mosque", "landmark_class", "generic_design"], intensity=1.1, range=24, poolRadius=9.0, realLight=False)
for _k, _hz in (("a", [(-10.2, -5.4, 12.0), (10.2, -5.4, 12.0), (0.0, -6.0, 6.0)]), ("b", [(13.8, -3.8, 14.0), (0.0, -6.0, 5.0), (-8.0, -6.0, 5.0)]), ("c", [(-9.8, 11.0, 12.0), (9.8, 11.0, 12.0), (0.0, -7.0, 5.0)])):
    extra(f"rg_ngn_mosque_medium_{_k}", "E", "REGIONAL", REF_MOSQUE, heads=_hz, preset="glow", tags=["mosque", "neighbourhood_mosque", "generic_design"], intensity=1.0, range=16, poolRadius=6.0)
for _k, _hz in (("a", [(6.3, -1.8, 8.0), (0.0, -3.0, 4.0)]), ("b", [(8.2, -1.8, 8.0), (0.0, -4.0, 4.0)]), ("c", [(5.1, -1.3, 6.0), (0.0, -2.8, 3.0)])):
    extra(f"rg_ngn_mosque_small_{_k}", "E", "REGIONAL", REF_MOSQUE, heads=_hz, preset="glow", tags=["mosque", "local_mosque", "generic_design"], intensity=0.9, range=12, poolRadius=4.5)
for _k, _hz in (("a", [(10.8, -3.4, 10.0), (0.0, -5.0, 4.0)]), ("b", [(6.6, -2.4, 7.0), (0.0, -3.5, 3.5)])):
    extra(f"rg_ngn_mosque_traditional_{_k}", "E", "REGIONAL", REF_TRAD, heads=_hz, preset="lamp_warm", tags=["mosque", "traditional", "hausa_inspired", "flagged_interpretation"], intensity=0.8, range=12, poolRadius=4.0, realLight=False)
