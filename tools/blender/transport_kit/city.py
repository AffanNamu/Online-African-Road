"""Family F: city fabric for the Kano densification directive (2026-11-09): the buildings of the R-039 skyline that the library did not have (references R-034, R-039, R-022, R-009/R-010 for the facade language), and the
monument rebuilt after R-034. Three mid-rise buildings and one monument:
  `kano_shopping_parade_a`   a four-storey shop-house parade: an arcade with shutter shopfronts on the ground floor, three floors of windows with sunshades above, roof tanks (36 x 16 m, 16 m high)
  `kano_office_block_a`      a seven-storey office block: concrete frame with glazed bands, a glass lobby with a canopy, a rooftop plant enclosure with a mast (22 x 20 m, 30 m high)
  `kano_midrise_apartments_a` a five-storey residential block with balconies on every floor and a stair core (24 x 14 m, 17 m high)
  `lmk_kano_roundabout_monument_b`  a civic monument of two tall crossing arches on a dark plinth after R-034 (a FICTIONAL designed monument: no real Kano monument is claimed)
All are GENERIC designs (never a copy of a real building, no brand, no billboard text: the sign boards are blank coloured panels). Real metres, front at -Y, origin on the ground at the centre of the footprint, three LODs.

VERSION 2 (2026-11-10, the realism pass; v001 is frozen and untouched). In the game (review run 42684305591) these were grey boxes with blank side and back walls. v2: every wall has windows, the surface bake
(transport_kit.surface) paints frames, mullions, curtains, shutters, sign boards, stains and contact shadows, and the things a lived-in building carries are modelled: air-conditioner units, downpipes, black water
tanks, a stair head, a satellite dish, an entrance apron."""
from __future__ import annotations
import math
from mathutils import Vector
from kano_kit.geo import Part
from .common import prism_x, prism_y, extra

GL, GLD, WINP = "glass", "glass_dark", "window_plain"
WALL, WALL2, TRIMC, ACC, FRAME, STEEL, CDK, BOARD, TANK, APRON = "plaster_white", "plaster_cream", "plaster_ochre", "plaster_terracotta", "frame_aluminium_dark", "shutter_steel", "concrete_dark", "board_dark", "kerb_black", "pavement_grey"
REF_CITY = ["R-034", "R-039", "R-022", "R-009"]
ONLY = {"-y": ("bottom", "top", "back", "left", "right"), "+y": ("bottom", "top", "front", "left", "right"), "-x": ("bottom", "top", "front", "back", "right"), "+x": ("bottom", "top", "front", "back", "left")}


def pane(p: Part, face, a, at, z0, w, h, key=WINP, proud=0.05):
    """One flat window or door pane standing 5 cm off a wall: the face on the wall whose plane is at `at` (y for the front and back, x for the sides), centred at `a` along it. The surface bake paints its frame,
    mullions, glass and curtains."""
    if face == "-y": p.box(a - w / 2, at - proud, z0, a + w / 2, at, z0 + h, key, skip=ONLY[face])
    elif face == "+y": p.box(a - w / 2, at, z0, a + w / 2, at + proud, z0 + h, key, skip=ONLY[face])
    elif face == "-x": p.box(at - proud, a - w / 2, z0, at, a + w / 2, z0 + h, key, skip=ONLY[face])
    else: p.box(at, a - w / 2, z0, at + proud, a + w / 2, z0 + h, key, skip=ONLY[face])


def sill(p: Part, face, a, at, z0, w, key=FRAME, out=0.18):
    """A projecting sill under a pane."""
    if face == "-y": p.box(a - w / 2 - 0.08, at - out, z0 - 0.1, a + w / 2 + 0.08, at, z0, key, skip=("bottom", "back"))
    elif face == "+y": p.box(a - w / 2 - 0.08, at, z0 - 0.1, a + w / 2 + 0.08, at + out, z0, key, skip=("bottom", "front"))
    elif face == "-x": p.box(at - out, a - w / 2 - 0.08, z0 - 0.1, at, a + w / 2 + 0.08, z0, key, skip=("bottom", "right"))
    else: p.box(at, a - w / 2 - 0.08, z0 - 0.1, at + out, a + w / 2 + 0.08, z0, key, skip=("bottom", "left"))


def aircon(p: Part, face, a, at, z0):
    """A split air-conditioner's outdoor unit on a bracket (0.8 x 0.3 x 0.55 m)."""
    o = 0.34
    if face == "-y": p.box(a - 0.4, at - o, z0, a + 0.4, at - 0.04, z0 + 0.55, "paint_white", skip=("back",))
    elif face == "+y": p.box(a - 0.4, at + 0.04, z0, a + 0.4, at + o, z0 + 0.55, "paint_white", skip=("front",))
    elif face == "-x": p.box(at - o, a - 0.4, z0, at - 0.04, a + 0.4, z0 + 0.55, "paint_white", skip=("right",))
    else: p.box(at + 0.04, a - 0.4, z0, at + o, a + 0.4, z0 + 0.55, "paint_white", skip=("left",))


def downpipe(p: Part, x, y, z0, z1): p.box(x - 0.06, y - 0.06, z0, x + 0.06, y + 0.06, z1, "metal_galvanised", skip=("bottom", "top"))


def tank(p: Part, cx, cy, z, r=0.9, h=1.8):
    """A black plastic water tank on a low stand: the roofline of every block in the city."""
    p.box(cx - r * 0.8, cy - r * 0.8, z, cx + r * 0.8, cy + r * 0.8, z + 0.5, CDK, skip=("bottom",))
    p.tube((cx, cy, z + 0.5), (cx, cy, z + 0.5 + h), r, r * 0.96, 10, TANK, caps=(False, False), smooth=True); p.tube((cx, cy, z + 0.5 + h), (cx, cy, z + 0.75 + h), r * 0.96, r * 0.3, 10, TANK, caps=(False, True), smooth=True)


def dish(p: Part, cx, cy, z):
    """A satellite dish on a short mast."""
    p.tube((cx, cy, z), (cx, cy, z + 0.9), 0.04, 0.04, 5, "metal_galvanised", caps=(False, False)); p.tube((cx, cy - 0.05, z + 0.9), (cx, cy - 0.3, z + 1.25), 0.1, 0.62, 10, "paint_white", caps=(True, False), smooth=True)


# ------------------------------------------------------------------------------------------------------------------------------------------------ shopping parade
def shopping_parade(lod):
    """A four-storey shop-house parade 36 x 16 m: ground floor 4.6 m (shopfronts under an arcade 2.4 m deep), three floors of 3.4 m above. Nine 4 m bays; every bay has a roll-up shutter or a glass front below, a sign
    board, and a window with a sunshade on each floor above; windows, back doors and air-conditioners on the other three walls; tanks, a stair head and a dish on the roof."""
    p = Part("parade"); W, D, nb = 36.0, 16.0, 9; y0, yb = -D / 2, D / 2; bay = W / nb; zg, zf = 4.6, 3.4; H = zg + 3 * zf; yf = y0 + 2.4
    if lod >= 2: p.box(-W / 2, yf, 0.0, W / 2, yb, zg, WALL2); p.box(-W / 2, y0, zg, W / 2, yb, H + 1.0, WALL2); return p
    p.box(-W / 2, y0, 0.0, W / 2, yf, 0.12, "tile_floor", skip=("bottom",))                                             # the arcade floor, a step above the street
    p.box(-W / 2, yf, 0.0, W / 2, yb, zg, WALL2, skip=("bottom",))                                                      # ground-floor volume set back behind the arcade
    p.box(-W / 2, y0, zg, W / 2, yb, H, WALL, skip=("bottom",))                                                        # the three upper floors overhang the arcade
    p.box(-W / 2 - 0.15, y0 - 0.15, zg - 0.3, W / 2 + 0.15, yf, zg, TRIMC)                                             # arcade beam and soffit
    for k in range(nb + 1): x = -W / 2 + bay * k; p.box(x - 0.25, y0 + 0.2, 0.12, x + 0.25, y0 + 0.7, zg - 0.3, WALL2, skip=("bottom", "top"))
    p.box(-W / 2 - 0.2, y0 - 0.2, H, W / 2 + 0.2, yb + 0.2, H + 0.25, TRIMC, skip=("bottom",)); p.box(-W / 2, y0, H + 0.25, W / 2, y0 + 0.25, H + 1.1, WALL, skip=("bottom",))   # cornice and parapet
    p.box(-W / 2, yb - 0.25, H + 0.25, W / 2, yb, H + 1.1, WALL, skip=("bottom",)); p.box(-W / 2, y0 + 0.25, H + 0.25, -W / 2 + 0.25, yb - 0.25, H + 1.1, WALL, skip=("bottom",)); p.box(W / 2 - 0.25, y0 + 0.25, H + 0.25, W / 2, yb - 0.25, H + 1.1, WALL, skip=("bottom",))
    for k in range(nb):
        cx = -W / 2 + bay * (k + 0.5)
        pane(p, "-y", cx, yf, 0.12, 3.1, 3.0, GLD if k in (2, 6) else STEEL, 0.08)                                      # a roll-up shutter; two shops are open behind glass
        pane(p, "-y", cx, yf, 3.25, 3.5, 0.95, BOARD, 0.1)                                                               # the shop's sign board (blank: no text, no brand)
        for fl in range(3):
            z = zg + zf * fl + 0.9
            pane(p, "-y", cx, y0, z, 1.9, 1.6)
            if lod == 0:
                sill(p, "-y", cx, y0, z, 1.9); p.box(cx - 1.25, y0 - 0.7, z + 1.85, cx + 1.25, y0, z + 1.97, FRAME, skip=("back",))                 # sunshade
                if (k + fl) % 3 == 1: aircon(p, "-y", cx + 1.45, y0, z + 0.1)
    for fl in range(3):                                                                                                  # sides and back: windows on every floor
        z = zg + zf * fl + 0.9
        for sx in (-1, 1):
            for yy in (-4.5, 0.0, 4.5): pane(p, "-x" if sx < 0 else "+x", yy, sx * W / 2, z, 1.6, 1.55)
            if lod == 0: aircon(p, "-x" if sx < 0 else "+x", 2.2, sx * W / 2, z + 0.1)
        for k in range(nb if lod == 0 else 0, 0, -1):
            cx = -W / 2 + bay * (k - 0.5); pane(p, "+y", cx, yb, z, 1.6, 1.55)
            if lod == 0 and (k + fl) % 4 == 0: aircon(p, "+y", cx + 1.3, yb, z + 0.1)
    if lod == 0:
        for k in (1, 4, 7): pane(p, "+y", -W / 2 + bay * (k - 0.5), yb, 0.0, 1.2, 2.3, "wood_door")                    # back doors
        for x in (-W / 2 + 0.3, W / 2 - 0.3): downpipe(p, x, yb + 0.07, 0.0, H)
        tank(p, -10.0, 3.0, H + 0.25, 1.0, 1.7); tank(p, -6.8, 3.0, H + 0.25, 1.0, 1.7); dish(p, 4.0, 4.0, H + 0.25)
    p.box(12.0, 0.0, H + 0.25, 15.5, 4.2, H + 2.9, WALL2, skip=("bottom",))                                             # stair head
    if lod == 0: pane(p, "-y", 13.75, 0.0, H + 0.25, 1.0, 2.1, "wood_door")
    return p


# ------------------------------------------------------------------------------------------------------------------------------------------------ office block
def office_block(lod):
    """A seven-storey office block 22 x 20 m: a concrete frame (six 3.67 m bays on the front, five on the sides) with a glazed band on every floor of all four sides, a glass lobby under a 4 m canopy with a blank
    sign band, an entrance apron, a plant enclosure with louvres, a mast and tanks."""
    p = Part("office"); W, D = 22.0, 20.0; y0, yb = -D / 2, D / 2; zg, zf, nf = 5.0, 3.6, 7; H = zg + (nf - 1) * zf; yt = H + 3.0; yl = y0 + 3.0
    if lod >= 2: p.box(-W / 2, y0, 0.0, W / 2, yb, H, WALL); p.box(-6.0, -4.0, H, 6.0, 4.0, yt, CDK); return p
    p.box(-W / 2, y0 - 1.0, 0.0, W / 2, yl, 0.1, APRON, skip=("bottom",))                                               # entrance apron
    p.box(-W / 2, yl, 0.0, W / 2, yb, zg, CDK, skip=("bottom",))                                                        # ground floor: the lobby core, set back
    p.box(-W / 2, y0, zg, W / 2, yb, H, WALL, skip=())                                                                  # the block above stands on the full footprint (its soffit is seen from the street)
    nbx = 6; bx = W / nbx; nby = 5; by = D / nby
    for fl in range(nf - 1):                                                                                           # glazed bands: one per bay per floor on all four sides
        z = zg + zf * fl + 0.85; zh = zf - 1.6
        for k in range(nbx):
            cx = -W / 2 + bx * (k + 0.5); pane(p, "-y", cx, y0, z, bx - 0.56, zh, GL if (k + fl) % 4 else GLD, 0.06)
            if lod == 0: pane(p, "+y", cx, yb, z, bx - 0.56, zh, GL, 0.06); sill(p, "-y", cx, y0, z, bx - 0.5, FRAME, 0.14)
        for k in range(nby if lod == 0 else 0):
            cy = -D / 2 + by * (k + 0.5)
            for sx in (-1, 1): pane(p, "-x" if sx < 0 else "+x", cy, sx * W / 2, z, by - 0.56, zh, GL if (k + fl) % 3 else GLD, 0.06)
        if lod > 0:
            for sx in (-1, 1): pane(p, "-x" if sx < 0 else "+x", 0.0, sx * W / 2, z, D - 3.0, zh, GL, 0.06)
    for k in range(nbx + 1): x = -W / 2 + bx * k; p.box(x - 0.28, y0 - 0.22, zg, x + 0.28, y0, H, WALL2, skip=("bottom", "top", "back"))          # frame piers stand proud of the glass
    if lod == 0:
        for k in range(nby + 1):
            y = -D / 2 + by * k
            for sx in (-1, 1): p.box(sx * W / 2 - (0.0 if sx > 0 else 0.2), y - 0.25, zg, sx * W / 2 + (0.2 if sx > 0 else 0.0), y + 0.25, H, WALL2, skip=("bottom", "top", "left" if sx > 0 else "right"))
    p.box(-W / 2 - 0.2, y0 - 0.25, H, W / 2 + 0.2, yb + 0.2, H + 0.4, WALL2, skip=("bottom",))
    pane(p, "-y", 0.0, yl, 0.1, 12.0, 3.5, GLD, 0.12)                                                                   # lobby glazing
    p.box(-7.0, y0 - 1.0, 4.2, 7.0, yl, 4.6, WALL2); p.box(-7.0, y0 - 1.0, 4.6, 7.0, y0 - 0.75, 5.0, WALL2, skip=("bottom",))                        # canopy
    for x in (-6.3, 6.3): p.box(x - 0.2, y0 - 0.7, 0.1, x + 0.2, y0 - 0.3, 4.2, WALL2, skip=("bottom", "top"))
    pane(p, "-y", 0.0, yl, 3.65, 12.0, 0.5, BOARD, 0.16)
    if lod == 0:
        for x in (-9.0, 9.0): pane(p, "-y", x, yl, 0.9, 2.6, 2.6, GL, 0.06)
        for k in range(4): pane(p, "+y", -6.6 + 4.4 * k, yb, 1.0, 2.4, 2.4, GL, 0.06)
        for sx in (-1, 1):
            for yy in (-3.0, 4.0): pane(p, "-x" if sx < 0 else "+x", yy, sx * W / 2, 1.0, 3.0, 2.4, GL, 0.06)
        for x in (-W / 2 + 0.4, W / 2 - 0.4): downpipe(p, x, yb + 0.07, 0.0, H)
    p.box(-6.0, -4.0, H + 0.4, 6.0, 4.0, yt, CDK, skip=("bottom",)); p.box(-6.2, -4.2, yt, 6.2, 4.2, yt + 0.3, WALL2, skip=("bottom",))                  # plant enclosure
    if lod == 0:
        pane(p, "-y", 0.0, -4.0, H + 0.8, 11.0, 1.9, "sheet_cream", 0.05)                                               # louvres
        p.tube((4.5, 3.0, yt + 0.3), (4.5, 3.0, yt + 5.0), 0.12, 0.05, 6, "metal_galvanised", caps=(False, True))
        for k in range(3): tank(p, -4.0 + 3.0 * k, 6.5, H + 0.4, 0.9, 1.3)
        dish(p, -8.0, -6.0, H + 0.4)
    return p


# ------------------------------------------------------------------------------------------------------------------------------------------------ mid-rise apartments
def midrise_apartments(lod):
    """A five-storey residential block 24 x 14 m: a ground floor with an entrance and shop shutters, four floors of 3.2 m with a balcony, a window and a balcony door per bay, windows on the sides and back, a stair
    core at the rear, air-conditioners, tanks and a dish on the roof."""
    p = Part("apartments"); W, D = 24.0, 14.0; y0, yb = -D / 2, D / 2; zg, zf = 3.8, 3.2; nf = 5; H = zg + (nf - 1) * zf; nb = 6; bay = W / nb
    if lod >= 2: p.box(-W / 2, y0, 0.0, W / 2, yb, H, TRIMC); return p
    p.box(-W / 2, y0 - 1.3, 0.0, W / 2, y0, 0.1, APRON, skip=("bottom",))
    p.box(-W / 2, y0, 0.0, W / 2, yb, H, TRIMC, skip=("bottom",)); p.box(-4.0, yb, 0.0, 4.0, yb + 2.6, H + 2.6, WALL2, skip=("bottom",))                                                          # block and rear stair core
    p.box(-W / 2 - 0.15, y0 - 0.15, H, W / 2 + 0.15, yb + 0.15, H + 0.3, WALL, skip=("bottom",)); p.box(-W / 2, y0, H + 0.3, W / 2, y0 + 0.2, H + 1.1, TRIMC, skip=("bottom",))
    for k in range(nb):
        cx = -W / 2 + bay * (k + 0.5)
        pane(p, "-y", cx, y0, 0.1, 3.2, 2.9, "wood_door" if k == 2 else (GLD if k == 3 else STEEL), 0.07)             # ground floor: shutters, the entrance door, one glazed shop
        if k != 2: pane(p, "-y", cx, y0, 3.05, 3.4, 0.6, BOARD, 0.09)
        for fl in range(nf - 1):
            z = zg + zf * fl
            p.box(cx - 1.7, y0 - 1.3, z, cx + 1.7, y0, z + 0.2, WALL)                                                                              # balcony slab
            p.box(cx - 1.7, y0 - 1.3, z + 0.2, cx + 1.7, y0 - 1.24, z + 1.1, WALL2, skip=("bottom",))                                             # solid balcony front
            if lod == 0:
                p.box(cx - 1.7, y0 - 1.24, z + 0.2, cx - 1.64, y0, z + 1.1, WALL2, skip=("bottom", "front")); p.box(cx + 1.64, y0 - 1.24, z + 0.2, cx + 1.7, y0, z + 1.1, WALL2, skip=("bottom", "front"))
                pane(p, "-y", cx - 0.75, y0, z + 1.0, 1.3, 1.3); pane(p, "-y", cx + 0.9, y0, z + 0.2, 1.0, 2.25, "wood_door" if (k + fl) % 3 == 0 else GLD)   # a window and the balcony door
                if (k + fl) % 2 == 0: aircon(p, "-y", cx - 0.75, y0, z + 0.25)
            else: pane(p, "-y", cx, y0, z + 0.4, 2.8, 2.0)
    for fl in range(nf - 1):
        z = zg + zf * fl + 0.9
        for sx in (-1, 1):
            for yy in (-4.0, 0.0, 4.0): pane(p, "-x" if sx < 0 else "+x", yy, sx * W / 2, z, 1.4, 1.4)
        if lod == 0:
            for cx in (-10.0, -6.5, 6.5, 10.0): pane(p, "+y", cx, yb, z, 1.4, 1.4)
            pane(p, "+y", 0.0, yb + 2.6, z + 0.4, 1.0, 1.4)
            if fl % 2 == 0: aircon(p, "+y", -8.3, yb, z + 0.1); aircon(p, "+x", 2.0, W / 2, z + 0.1)
    if lod == 0:
        for sx in (-1, 1): pane(p, "-x" if sx < 0 else "+x", 0.0, sx * W / 2, 0.9, 1.4, 1.4)
        for x in (-W / 2 + 0.3, W / 2 - 0.3): downpipe(p, x, yb + 0.07, 0.0, H)
        for k in range(3): tank(p, -6.0 + 5.0 * k, 3.0, H + 0.3, 0.95, 1.5)
        dish(p, 8.0, -3.0, H + 0.3); dish(p, -9.5, -2.0, H + 0.3)
    return p


# ------------------------------------------------------------------------------------------------------------------------------------------------ monument after R-034
def _arch_poly(span, rise, t, n=14):
    """Outer and inner outline of an arch of clear span `span` (between the inner faces of the legs), crown height `rise` and leg thickness t: a list of convex quads (x, z) from the left foot to the right foot."""
    def pt(a, r_x, r_z): return (r_x * math.cos(a), r_z * math.sin(a))
    quads = []
    rx_i, rz_i = span / 2, rise - t; rx_o, rz_o = span / 2 + t, rise
    for i in range(n):
        a0, a1 = math.pi - math.pi * i / n, math.pi - math.pi * (i + 1) / n
        quads.append([pt(a0, rx_o, rz_o), pt(a1, rx_o, rz_o), pt(a1, rx_i, rz_i), pt(a0, rx_i, rz_i)])
    return quads


def monument_b(lod):
    """Civic monument after R-034 (a fictional design): a 12 x 12 m dark stone plinth with a stepped base and a brass plate on each face, and two tall cream arches (span 6.4 m, 22 m high, 1.7 m thick, 1.7 m deep)
    crossing at right angles over the centre, uplights at the corners of the base."""
    p = Part("monument"); n = 14 if lod == 0 else 8
    if lod >= 2: p.box(-6.0, -6.0, 0.0, 6.0, 6.0, 1.2, CDK); p.box(-4.4, -0.85, 1.2, 4.4, 0.85, 22.0, WALL2); p.box(-0.85, -4.4, 1.2, 0.85, 4.4, 22.0, WALL2); return p
    p.box(-6.0, -6.0, 0.0, 6.0, 6.0, 0.5, CDK, skip=("bottom",)); p.box(-5.2, -5.2, 0.5, 5.2, 5.2, 1.0, CDK, skip=("bottom",)); p.box(-4.6, -4.6, 1.0, 4.6, 4.6, 1.2, "stone_ivory", skip=("bottom",))
    if lod == 0:
        for face, (ax, at) in (("-y", (0.0, -5.2)), ("+y", (0.0, 5.2)), ("-x", (0.0, -5.2)), ("+x", (0.0, 5.2))): pane(p, face, ax, at, 0.56, 5.2, 0.38, "trim_gold", 0.03)        # the plate (blank: the game draws the name)
        for sx in (-1, 1):
            for sy in (-1, 1): p.box(sx * 4.9 - 0.25, sy * 4.9 - 0.25, 1.0, sx * 4.9 + 0.25, sy * 4.9 + 0.25, 1.15, "lantern_glow", skip=("bottom",))                               # uplights at the base corners
    for quad in _arch_poly(6.4, 22.0, 1.7, n): prism_y(p, [(x, z + 1.2) for x, z in quad], -0.85, 0.85, "stone_ivory")
    for quad in _arch_poly(6.4, 22.0, 1.7, n): prism_x(p, [(x, z + 1.2) for x, z in quad], -0.85, 0.85, "stone_ivory")
    if lod == 0:
        for sgn in (-1, 1):
            p.box(sgn * 4.0 - 0.95, -0.95, 1.2, sgn * 4.0 + 0.95, 0.95, 1.6, CDK, skip=("bottom",)); p.box(-0.95, sgn * 4.0 - 0.95, 1.2, 0.95, sgn * 4.0 + 0.95, 1.6, CDK, skip=("bottom",))      # leg bases
        p.blob((0, 0, 23.7), (0.9, 0.9, 0.9), "trim_gold", subdiv=1)
    return p


extra("kano_shopping_parade_a", "F", "CITY_SPECIFIC", REF_CITY, heads=[(-12.0, -8.2, 4.4), (0.0, -8.2, 4.4), (12.0, -8.2, 4.4), (-12.0, -8.2, 11.0), (12.0, -8.2, 11.0)], preset="glow_door", tags=["commercial", "shop_house", "mid_rise", "generic_design"], intensity=1.0, range=12, poolRadius=4.0)
extra("kano_office_block_a", "F", "CITY_SPECIFIC", REF_CITY, heads=[(-6.3, -10.0, 4.0), (6.3, -10.0, 4.0), (0.0, -7.0, 28.0), (0.0, 0.0, 33.0)], preset="glow_door", tags=["commercial", "office", "mid_rise", "generic_design"], intensity=1.0, range=14, poolRadius=4.0)
extra("kano_midrise_apartments_a", "F", "CITY_SPECIFIC", REF_CITY, heads=[(0.0, -7.2, 3.2), (-8.0, -7.2, 3.2), (8.0, -7.2, 3.2)], preset="glow_door", tags=["residential", "apartments", "mid_rise", "generic_design"], intensity=0.9, range=10, poolRadius=3.5)
extra("lmk_kano_roundabout_monument_b", "F", "LANDMARK", ["R-034", "R-035"], heads=[(-4.9, -4.9, 1.15), (4.9, -4.9, 1.15), (-4.9, 4.9, 1.15), (4.9, 4.9, 1.15)], preset="glow", tags=["monument", "roundabout", "fictional_design", "twin_arch"], intensity=1.4, range=30, poolRadius=6.0, realLight=False)
