"""Family F: city fabric for the Kano densification directive (2026-11-09): the buildings of the R-039 skyline that the library did not have (references R-034, R-039, R-022, R-009/R-010 for the facade language), and the
monument rebuilt after R-034. Three mid-rise buildings and one monument:
  `kano_shopping_parade_a`   a four-storey shop-house parade: an arcade with shutter shopfronts on the ground floor, three floors of windows with sunshades above, roof tanks (36 x 16 m, 16 m high)
  `kano_office_block_a`      a seven-storey office block: concrete frame with glazed bands, a glass lobby with a canopy, a rooftop plant enclosure with a mast (22 x 20 m, 30 m high)
  `kano_midrise_apartments_a` a five-storey residential block with balconies on every floor and a stair core (24 x 14 m, 17 m high)
  `lmk_kano_roundabout_monument_b`  a civic monument of two tall crossing arches on a dark plinth after R-034 (a FICTIONAL designed monument: no real Kano monument is claimed)
All are GENERIC designs (never a copy of a real building, no brand, no billboard text: the sign bands are blank). Real metres, front at -Y, origin on the ground at the centre of the footprint, three LODs, at most 4 materials (palette)."""
from __future__ import annotations
import math
from mathutils import Matrix, Vector
from kano_kit.geo import Part
from .common import prism_x, prism_y, extra

GL, GLD = "glass", "glass_dark"
WALL, WALL2, TRIMC, ACC, FRAME, STEEL, CDK, BOARD = "plaster_white", "plaster_cream", "plaster_ochre", "plaster_terracotta", "frame_aluminium_dark", "shutter_steel", "concrete_dark", "board_dark"
REF_CITY = ["R-034", "R-039", "R-022", "R-009"]


def _win(p: Part, x, y, z0, w, h, glass=GL, frame=FRAME, sill=True, face="-y"):
    """A flat window pane on the wall at y (front face -Y) with a sill and a head: five or six faces, cheap enough to repeat a hundred times."""
    p.box(x - w / 2, y - 0.07, z0, x + w / 2, y, z0 + h, glass, skip=("bottom", "top", "back", "left", "right"))
    if sill: p.box(x - w / 2 - 0.08, y - 0.22, z0 - 0.1, x + w / 2 + 0.08, y, z0, frame, skip=("bottom",))
    p.box(x - w / 2 - 0.08, y - 0.12, z0 + h, x + w / 2 + 0.08, y, z0 + h + 0.1, frame, skip=("bottom",))


def _tank(p: Part, cx, cy, z, r=0.9, h=1.8, key=WALL):
    p.tube((cx, cy, z), (cx, cy, z + h), r, r, 10, key, caps=(False, True), smooth=False); p.tube((cx, cy, z + h), (cx, cy, z + h + 0.12), r * 0.35, r * 0.35, 8, FRAME, caps=(False, True), smooth=False)


# ------------------------------------------------------------------------------------------------------------------------------------------------ shopping parade
def shopping_parade(lod):
    """A four-storey shop-house parade 36 x 16 m: ground floor 4.6 m (shopfronts under an arcade 2.4 m deep), three floors of 3.4 m above. Nine 4 m bays; every bay has a roll-up shutter and a glass front below, a window and a sunshade above."""
    p = Part("parade"); W, D, nb = 36.0, 16.0, 9; y0, yb = -D / 2, D / 2; bay = W / nb; zg, zf = 4.6, 3.4; H = zg + 3 * zf; yf = y0 + 2.4
    if lod >= 2: p.box(-W / 2, yf, 0.0, W / 2, yb, zg, WALL2); p.box(-W / 2, y0, zg, W / 2, yb, H + 1.0, WALL2); return p
    p.box(-W / 2, yf, 0.0, W / 2, yb, zg, WALL2, skip=("bottom",))                                                      # ground-floor volume set back behind the arcade
    p.box(-W / 2, y0, zg, W / 2, yb, H, WALL, skip=("bottom",))                                                        # the three upper floors overhang the arcade
    p.box(-W / 2 - 0.15, y0 - 0.15, zg - 0.3, W / 2 + 0.15, yf, zg, TRIMC, skip=("bottom",))                          # arcade soffit beam
    for k in range(nb + 1): x = -W / 2 + bay * k; p.box(x - 0.25, y0 + 0.2, 0.0, x + 0.25, y0 + 0.7, zg - 0.3, WALL2, skip=("bottom", "top"))
    p.box(-W / 2 - 0.2, y0 - 0.2, H, W / 2 + 0.2, yb + 0.2, H + 0.25, TRIMC, skip=("bottom",)); p.box(-W / 2, y0, H + 0.25, W / 2, y0 + 0.25, H + 1.1, WALL, skip=("bottom",))   # cornice and front parapet
    p.box(-W / 2, yb - 0.25, H + 0.25, W / 2, yb, H + 1.1, WALL, skip=("bottom",)); p.box(-W / 2, y0 + 0.25, H + 0.25, -W / 2 + 0.25, yb - 0.25, H + 1.1, WALL, skip=("bottom",)); p.box(W / 2 - 0.25, y0 + 0.25, H + 0.25, W / 2, yb - 0.25, H + 1.1, WALL, skip=("bottom",))
    for k in range(nb):
        cx = -W / 2 + bay * (k + 0.5)
        p.box(cx - 1.55, yf - 0.1, 0.35, cx + 1.55, yf, 3.1, STEEL, skip=("bottom", "top", "back", "left", "right"))   # roll-up shutter
        p.box(cx - 1.75, yf - 0.1, 3.2, cx + 1.75, yf, 4.2, BOARD, skip=("bottom", "top", "back", "left", "right"))     # blank sign band above the shutter (no text, no brand)
        if lod == 0:
            p.box(cx - 1.65, yf - 0.05, 0.0, cx + 1.65, yf + 0.0, 0.35, TRIMC, skip=("bottom", "back"))
            for fl in range(3):
                z = zg + zf * fl + 0.9
                _win(p, cx, y0, z, 1.8, 1.55, GLD if (k + fl) % 3 == 0 else GL)
                p.box(cx - 1.2, y0 - 0.7, z + 1.85, cx + 1.2, y0, z + 1.97, FRAME, skip=("bottom",))                  # sunshade
        else: p.box(cx - 0.9, y0 - 0.07, zg + 0.9, cx + 0.9, y0, zg + 2.45, GL, skip=("bottom", "top", "back", "left", "right"))
    if lod == 0:
        for sx in (-1, 1):
            for fl in range(3):
                for yy in (-3.0, 3.0): p.box(sx * W / 2 - (0.07 if sx > 0 else 0), yy - 0.8, zg + zf * fl + 0.9, sx * W / 2 + (0 if sx > 0 else 0.07), yy + 0.8, zg + zf * fl + 2.45, GL, skip=("bottom", "top", "left", "right"))
        for k in range(2): _tank(p, -10.0 + 8.0 * k, 3.0, H + 1.1, 1.0, 1.8)
        p.box(12.0, 0.0, H + 1.1, 15.5, 4.2, H + 3.3, WALL2, skip=("bottom",))                                          # stair head
        p.box(-W / 2 - 0.1, yb - 0.02, zg, W / 2 + 0.1, yb, zg + 0.25, TRIMC, skip=("bottom",))
    return p


# ------------------------------------------------------------------------------------------------------------------------------------------------ office block
def office_block(lod):
    """A seven-storey office block 22 x 20 m: a concrete frame (six 3.67 m bays on the front, five on the sides) with a glazed band and a spandrel on every floor, a glass lobby with a 4 m canopy, a plant enclosure and a mast."""
    p = Part("office"); W, D = 22.0, 20.0; y0, yb = -D / 2, D / 2; zg, zf, nf = 5.0, 3.6, 7; H = zg + (nf - 1) * zf; yt = H + 3.0
    if lod >= 2: p.box(-W / 2, y0, 0.0, W / 2, yb, H, WALL); p.box(-6.0, -4.0, H, 6.0, 4.0, yt, CDK); return p
    p.box(-W / 2, y0 + 3.0, 0.0, W / 2, yb, zg, CDK, skip=("bottom",))                                                  # ground floor: lobby core set back
    p.box(-W / 2, y0, zg, W / 2, yb, H, WALL, skip=("bottom",))                                                         # the tower block above stands on the full footprint
    nbx = 6; bx = W / nbx
    for fl in range(nf - 1):                                                                                           # glazed bands: one strip per floor on all four sides
        z = zg + zf * fl + 0.9; zh = zf - 1.8
        for k in range(nbx):
            cx = -W / 2 + bx * (k + 0.5); p.box(cx - bx / 2 + 0.28, y0 - 0.06, z, cx + bx / 2 - 0.28, y0, z + zh, GL if (k + fl) % 4 else GLD, skip=("bottom", "top", "back", "left", "right"))
            if lod == 0: p.box(cx - bx / 2 + 0.2, y0 - 0.14, z - 0.12, cx + bx / 2 - 0.2, y0, z, FRAME, skip=("bottom",))
        for sx in (-1, 1): p.box(sx * W / 2 - (0.06 if sx > 0 else 0), y0 + 1.5, z, sx * W / 2 + (0 if sx > 0 else 0.06), yb - 1.5, z + zh, GL, skip=("bottom", "top", "left", "right"))
        if lod == 0: p.box(-W / 2 + 1.5, yb, z, W / 2 - 1.5, yb + 0.06, z + zh, GL, skip=("bottom", "top", "front", "left", "right"))
    for k in range(nbx + 1): x = -W / 2 + bx * k; p.box(x - 0.28, y0 - 0.22, zg, x + 0.28, y0, H, WALL2, skip=("bottom", "top"))          # frame piers stand proud of the glass
    p.box(-W / 2 - 0.2, y0 - 0.25, H, W / 2 + 0.2, yb + 0.2, H + 0.4, WALL2, skip=("bottom",))
    p.box(-W / 2, y0 + 3.0, zg - 0.4, W / 2, y0 + 3.0 + 0.01, zg, CDK, skip=("bottom", "top", "right", "left"))
    p.box(-6.0, y0 + 3.0 - 0.2, 0.0, 6.0, y0 + 3.0 + 0.0, 4.2, GLD, skip=("bottom", "top", "back", "left", "right"))   # lobby glazing
    p.box(-7.0, y0 - 1.0, 4.2, 7.0, y0 + 3.0, 4.6, WALL2, skip=("bottom",)); p.box(-7.0, y0 - 1.0, 4.6, 7.0, y0 - 0.75, 5.0, WALL2, skip=("bottom",))   # canopy
    for x in (-6.3, 6.3): p.box(x - 0.2, y0 - 0.7, 0.0, x + 0.2, y0 - 0.3, 4.2, WALL2, skip=("bottom", "top"))
    if lod == 0:
        for k in range(8): x = -W / 2 + 2.0 + k * 2.6; p.box(x - 0.9, y0 + 3.0 - 0.1, 0.8, x + 0.9, y0 + 3.0, 3.4, GL, skip=("bottom", "top", "back", "left", "right")) if abs(x) > 6.2 else None
        p.box(-7.0, y0 + 3.0 - 0.0, 3.6, 7.0, y0 + 3.0 + 0.0, 4.2, BOARD, skip=("bottom", "top", "back", "left", "right"))
    p.box(-6.0, -4.0, H + 0.4, 6.0, 4.0, yt, CDK, skip=("bottom",)); p.box(-6.2, -4.2, yt, 6.2, 4.2, yt + 0.3, WALL2, skip=("bottom",))                  # plant enclosure
    if lod == 0:
        for k in range(10): z = H + 0.9 + 0.2 * k; p.box(-6.02, -4.1 - 0.0, z, 6.02, -4.0, z + 0.1, FRAME, skip=("bottom",))
        p.tube((4.5, 3.0, yt + 0.3), (4.5, 3.0, yt + 5.0), 0.12, 0.05, 6, STEEL, caps=(False, True))
        for k in range(3): _tank(p, -4.0 + 3.0 * k, 6.0, H + 0.4, 0.9, 1.4, WALL2)
    return p


# ------------------------------------------------------------------------------------------------------------------------------------------------ mid-rise apartments
def midrise_apartments(lod):
    """A five-storey residential block 24 x 14 m: a ground floor with an entrance and shops' shutters, four floors of 3.2 m with a balcony and a window pair per bay, a stair core at the rear, a roof with tanks."""
    p = Part("apartments"); W, D = 24.0, 14.0; y0, yb = -D / 2, D / 2; zg, zf = 3.8, 3.2; nf = 5; H = zg + (nf - 1) * zf; nb = 6; bay = W / nb
    if lod >= 2: p.box(-W / 2, y0, 0.0, W / 2, yb, H, TRIMC); return p
    p.box(-W / 2, y0, 0.0, W / 2, yb, H, TRIMC, skip=("bottom",)); p.box(-4.0, yb, 0.0, 4.0, yb + 2.6, H + 2.6, WALL2, skip=("bottom",))                                                          # block and rear stair core
    p.box(-W / 2 - 0.15, y0 - 0.15, H, W / 2 + 0.15, yb + 0.15, H + 0.3, WALL, skip=("bottom",)); p.box(-W / 2, y0, H + 0.3, W / 2, y0 + 0.2, H + 1.1, TRIMC, skip=("bottom",))
    for k in range(nb):
        cx = -W / 2 + bay * (k + 0.5)
        p.box(cx - 1.6, y0 - 0.07, 0.3, cx + 1.6, y0, 3.2, STEEL if k != 2 else GLD, skip=("bottom", "top", "back", "left", "right"))                    # ground floor: shutters; the middle bay is the entrance
        for fl in range(nf - 1):
            z = zg + zf * fl
            p.box(cx - 1.7, y0 - 1.3, z, cx + 1.7, y0, z + 0.2, WALL, skip=("bottom",))                                                                      # balcony slab
            p.box(cx - 1.7, y0 - 1.3, z + 0.2, cx + 1.7, y0 - 1.24, z + 1.1, WALL2, skip=("bottom", "top", "back"))                                         # solid balcony front
            if lod == 0:
                p.box(cx - 1.7, y0 - 1.3, z + 0.2, cx - 1.64, y0, z + 1.1, WALL2, skip=("bottom",)); p.box(cx + 1.64, y0 - 1.3, z + 0.2, cx + 1.7, y0, z + 1.1, WALL2, skip=("bottom",))
                p.box(cx - 1.4, y0 - 0.06, z + 0.4, cx - 0.1, y0, z + 2.2, GL, skip=("bottom", "top", "back", "left", "right")); p.box(cx + 0.35, y0 - 0.06, z + 0.2, cx + 1.4, y0, z + 2.6, GLD, skip=("bottom", "top", "back", "left", "right"))   # window + door
            else: p.box(cx - 1.4, y0 - 0.06, z + 0.4, cx + 1.4, y0, z + 2.4, GL, skip=("bottom", "top", "back", "left", "right"))
    if lod == 0:
        for sx in (-1, 1):
            for fl in range(nf - 1):
                z = zg + zf * fl + 0.6
                for yy in (-3.0, 2.0): p.box(sx * W / 2 - (0.06 if sx > 0 else 0), yy - 0.7, z, sx * W / 2 + (0 if sx > 0 else 0.06), yy + 0.7, z + 1.6, GL, skip=("bottom", "top", "left", "right"))
        for k in range(3): _tank(p, -6.0 + 5.0 * k, 3.0, H + 0.3, 0.95, 1.7)
    return p


# ------------------------------------------------------------------------------------------------------------------------------------------------ monument after R-034
def _arch_poly(span, rise, t, n=14):
    """Outer and inner centreline outline points of an arch of clear span `span` (between the inner faces of the legs), crown height `rise` and leg thickness t: returns a list of convex quads (x, z) from the left foot to the right foot."""
    def pt(a, r_x, r_z): return (r_x * math.cos(a), r_z * math.sin(a))
    quads = []
    rx_i, rz_i = span / 2, rise - t; rx_o, rz_o = span / 2 + t, rise
    for i in range(n):
        a0, a1 = math.pi - math.pi * i / n, math.pi - math.pi * (i + 1) / n
        quads.append([pt(a0, rx_o, rz_o), pt(a1, rx_o, rz_o), pt(a1, rx_i, rz_i), pt(a0, rx_i, rz_i)])
    return quads


def monument_b(lod):
    """Civic monument after R-034 (a fictional design): a 12 x 12 m dark stone plinth with a stepped base and a lettering band on each face, and two tall cream arches (span 6.4 m, 22 m high, 1.7 m thick, 1.7 m deep) crossing at right angles over the centre, a ring of uplights on the base."""
    p = Part("monument"); n = 14 if lod == 0 else 8
    if lod >= 2: p.box(-6.0, -6.0, 0.0, 6.0, 6.0, 1.2, CDK); p.box(-4.4, -0.85, 1.2, 4.4, 0.85, 22.0, WALL2); p.box(-0.85, -4.4, 1.2, 0.85, 4.4, 22.0, WALL2); return p
    p.box(-6.0, -6.0, 0.0, 6.0, 6.0, 0.5, CDK, skip=("bottom",)); p.box(-5.2, -5.2, 0.5, 5.2, 5.2, 1.0, CDK, skip=("bottom",)); p.box(-4.6, -4.6, 1.0, 4.6, 4.6, 1.2, WALL2, skip=("bottom",))
    if lod == 0:
        for k in range(4):
            a = k * math.pi / 2; s0 = p.mark(); p.box(-2.6, -5.22, 0.55, 2.6, -5.2, 0.95, BOARD, skip=("bottom", "top", "back", "left", "right")); p.xform(s0, Matrix.Rotation(a, 4, "Z"))        # lettering band (blank: the game draws the name)
        for sx in (-1, 1):
            for sy in (-1, 1): p.box(sx * 4.9 - 0.25, sy * 4.9 - 0.25, 1.0, sx * 4.9 + 0.25, sy * 4.9 + 0.25, 1.15, "lantern_glow", skip=("bottom",))                                    # uplights at the base corners
    for quad in _arch_poly(6.4, 22.0, 1.7, n):
        prism_y(p, [(x, z + 1.2) for x, z in quad], -0.85, 0.85, WALL2)
    for quad in _arch_poly(6.4, 22.0, 1.7, n):
        prism_x(p, [(x, z + 1.2) for x, z in quad], -0.85, 0.85, WALL2)
    if lod == 0:
        for sgn in (-1, 1):
            p.box(sgn * 4.0 - 0.5, -0.88, 1.2, sgn * 4.0 + 0.5, 0.88, 1.5, CDK, skip=("bottom",)); p.box(-0.88, sgn * 4.0 - 0.5, 1.2, 0.88, sgn * 4.0 + 0.5, 1.5, CDK, skip=("bottom",))      # leg bases
        p.blob((0, 0, 23.6), (0.9, 0.9, 0.9), WALL2, subdiv=1)
    return p


extra("kano_shopping_parade_a", "F", "CITY_SPECIFIC", REF_CITY, heads=[(-12.0, -8.2, 4.4), (0.0, -8.2, 4.4), (12.0, -8.2, 4.4), (-12.0, -8.2, 11.0), (12.0, -8.2, 11.0)], preset="glow_door", tags=["commercial", "shop_house", "mid_rise", "generic_design"], intensity=1.0, range=12, poolRadius=4.0, shiftFront=True)
extra("kano_office_block_a", "F", "CITY_SPECIFIC", REF_CITY, heads=[(-6.3, -10.0, 4.0), (6.3, -10.0, 4.0), (0.0, -7.0, 28.0), (0.0, 0.0, 33.0)], preset="glow_door", tags=["commercial", "office", "mid_rise", "generic_design"], intensity=1.0, range=14, poolRadius=4.0, shiftFront=True)
extra("kano_midrise_apartments_a", "F", "CITY_SPECIFIC", REF_CITY, heads=[(0.0, -7.2, 3.2), (-8.0, -7.2, 3.2), (8.0, -7.2, 3.2)], preset="glow_door", tags=["residential", "apartments", "mid_rise", "generic_design"], intensity=0.9, range=10, poolRadius=3.5, shiftFront=True)
extra("lmk_kano_roundabout_monument_b", "F", "LANDMARK", ["R-034", "R-035"], heads=[(-4.9, -4.9, 1.15), (4.9, -4.9, 1.15), (-4.9, 4.9, 1.15), (4.9, 4.9, 1.15)], preset="glow", tags=["monument", "roundabout", "fictional_design", "twin_arch"], intensity=1.4, range=30, poolRadius=6.0, realLight=False)
