"""Family B: bus terminals and stops (district D09; references R-009, RB-13, RB-07). Real bus sizes drive the geometry: an intercity coach is 12 m x 2.55 m x 3.5 m, so a bay is 3.6 m wide and 13.5 m deep, canopies clear
4.2 m, the arrivals canopy 5.0 m. Parametric builders; every function takes the LOD (0 detailed, 1 medium, 2 mass: a few boxes). Front at -Y, origin at the footprint centre. Generic names and colours, no operator brand."""
from __future__ import annotations
import math
from mathutils import Matrix
from kano_kit.geo import Part
from .common import prism_x, extra

GAL, BLK, CON, CDK = "metal_galvanised", "metal_gate_black", "concrete", "concrete_dark"
REF = ["R-009", "RB-13", "RB-07"]
BUS_L, BUS_W, BUS_H = 12.0, 2.55, 3.5
BAY_W, BAY_D = 3.6, 13.5


def terminal_hall(lod):
    """40 x 22 m terminal (9 m to the roof, roof plant above): a glazed concourse and ticket hall under a flat roof, a covered arrivals canopy on eight columns along the front, a set-back upper volume behind a
    terracotta brise-soleil, a name board on the canopy fascia, a departures display over the doors, benches and bollards under the canopy, roof plant and a water tank."""
    p = Part("terminal"); seg = 12 if lod == 0 else 6
    x0, x1 = -20.0, 20.0; yf = -5.0; yb = 11.0
    p.box(x0, yf, 0.0, x1, yb, 5.2, "plaster_cream", skip=("bottom",))                                                                                 # ground floor
    p.box(x0 - 0.3, yf - 0.3, 5.2, x1 + 0.3, yb + 0.3, 5.55, CON, skip=("bottom",))                                                                    # roof slab with overhang
    p.box(-14.0, 0.0, 5.55, 14.0, yb, 9.0, "plaster_white", skip=("bottom",))                                                                          # upper volume
    p.box(-14.3, -0.3, 9.0, 14.3, yb + 0.3, 9.25, CON, skip=("bottom",))                                                                              # upper roof / parapet cap
    p.box(x0 - 0.2, -11.0, 5.0, x1 + 0.2, yf, 5.35, CON, skip=("bottom",))                                                                              # arrivals canopy slab
    p.box(x0 - 0.2, -11.0, 5.35, x1 + 0.2, -10.9, 6.25, "sign_blue", skip=("bottom",))                                                                  # name-board fascia
    if lod >= 2: return p
    for i in range(8): p.tube((x0 + 2.5 + i * 5.0, -10.5, 0.0), (x0 + 2.5 + i * 5.0, -10.5, 5.0), 0.22, 0.22, seg, CON, caps=(False, False), smooth=(lod == 0))
    for i in range(10):                                                                                                                                # glazing between mullions on the ground floor
        gx = x0 + 1.0 + i * 3.8
        if i in (4, 5): continue
        p.box(gx, yf - 0.04, 0.9, gx + 3.4, yf, 4.6, "glass_dark", skip=("bottom", "back"))
    for i in range(11): p.box(x0 + 0.8 + i * 3.8 - 0.12, yf - 0.12, 0.0, x0 + 0.8 + i * 3.8 + 0.12, yf, 5.2, CON, skip=("bottom", "back"))
    if lod == 1: p.box(-3.8, yf - 0.04, 0.0, 3.8, yf, 4.7, "glass_dark", skip=("bottom", "back"))
    for i in range(5): p.box(-3.7 + i * 1.5, yf - 0.08, 0.0, -2.4 + i * 1.5, yf, 3.2, "glass_dark", skip=("bottom", "back"))                              # entrance doors
    p.box(-4.0, yf - 0.1, 3.2, 4.0, yf, 4.7, "board_dark", skip=("bottom", "back"))                                                                    # departures display over the doors
    if lod == 1: return p
    for i in range(5): p.box(-3.8 + i * 1.5, yf - 0.1, 0.0, -3.7 + i * 1.5, yf, 3.3, GAL, skip=("bottom", "back"))                                    # door frames
    p.box(3.7, yf - 0.1, 0.0, 3.8, yf, 3.3, GAL, skip=("bottom", "back"))
    for j in range(4):                                                                                                                                 # ticket-hall windows with counters (left of the doors)
        wx = x0 + 1.0 + j * 3.8
        p.box(wx + 0.3, yf - 0.12, 1.0, wx + 3.1, yf - 0.04, 1.08, "timber_light"); p.box(wx + 0.3, yf - 0.1, 2.35, wx + 3.1, yf, 2.45, GAL)
    for i in range(10):                                                                                                                                # transoms across the glazing
        gx = x0 + 1.0 + i * 3.8
        if i in (4, 5): continue
        p.box(gx, yf - 0.06, 2.6, gx + 3.4, yf, 2.68, GAL, skip=("bottom", "back"))
    for k in range(55): p.box(-13.8 + 0.5 * k, -0.7, 5.8, -13.8 + 0.5 * k + 0.1, 0.0, 8.7, "plaster_terracotta", skip=("bottom", "back"))                # brise-soleil fins
    p.box(-14.2, -0.75, 5.7, 14.2, -0.65, 5.85, "plaster_terracotta"); p.box(-14.2, -0.75, 8.65, 14.2, -0.65, 8.8, "plaster_terracotta")             # fin rails
    for i in range(8):
        cx = x0 + 2.5 + i * 5.0
        p.box(cx - 0.3, -10.8, 4.85, cx + 0.3, -10.2, 5.0, CON); p.box(cx - 0.28, -10.8, 4.78, cx + 0.28, -10.2, 4.84, "lantern_glow")             # column capitals, canopy downlights
        p.box(cx - 0.34, -10.84, 0.0, cx + 0.34, -10.16, 0.15, CON, skip=("bottom",))                                                                  # column bases
    for k in range(6): p.box(-9.0 + 3.2 * k, 3.0, 9.25, -7.4 + 3.2 * k, 5.0, 10.2, GAL, skip=("bottom",))                                              # roof plant
    p.tube((9.0, 7.0, 9.25), (9.0, 7.0, 10.5), 1.1, 1.1, 14, "metal_gate_blue", caps=(False, True), smooth=False)                                       # water tank
    for k in range(3): p.tube((9.0, 7.0, 9.25 + 0.45 * k), (9.0, 7.0, 9.3 + 0.45 * k), 1.14, 1.14, 14, GAL, caps=(False, False), smooth=False)           # tank bands
    for i in range(9):                                                                                                                                 # benches under the arrivals canopy
        bx = x0 + 3.0 + i * 4.3
        p.box(bx - 0.9, -9.4, 0.42, bx + 0.9, -8.9, 0.48, "timber_light"); p.box(bx - 0.9, -8.95, 0.48, bx + 0.9, -8.9, 0.9, "timber_light")
        for sx in (-0.8, 0.8): p.box(bx + sx - 0.04, -9.35, 0.0, bx + sx + 0.04, -8.95, 0.42, BLK)
    for i in range(10): p.tube((x0 + 2.0 + i * 4.0, -10.9, 0.0), (x0 + 2.0 + i * 4.0, -10.9, 0.8), 0.09, 0.09, 8, "warning_yellow", caps=(False, True))     # kerbside bollards
    for tx in (-8.0, 8.0): p.box(tx - 0.4, -8.0, 0.0, tx + 0.4, -7.8, 2.2, "sign_blue", skip=("bottom",)); p.box(tx - 0.35, -8.02, 0.9, tx + 0.35, -8.0, 2.1, "paint_white", skip=("bottom", "back"))   # information totems
    for i in range(12): p.box(-13.9 + 2.4 * i, yb, 5.55, -13.9 + 2.4 * i + 0.3, yb + 0.12, 9.0, CON, skip=("bottom", "front"))                         # rear pilasters of the upper volume
    p.box(x0 + 0.2, yf + 0.0, 0.0, x0 + 1.2, yf + 0.4, 0.5, CON); p.box(x1 - 1.2, yf + 0.0, 0.0, x1 - 0.2, yf + 0.4, 0.5, CON)                           # corner planters
    return p


def bus_shelter_long(lod):
    """18 x 4 m platform canopy for three bays: a mono-pitch roof on twelve posts with a fascia, three timber benches, three route panels on the back rail, a bay-number fascia and bins."""
    p = Part("canopy"); seg = 10 if lod == 0 else 6
    prism_x(p, [(-2.0, 3.7), (2.0, 4.1), (2.0, 4.2), (-2.0, 3.8)], -9.0, 9.0, "roof_zinc")
    if lod >= 2: p.box(-8.6, 1.5, 0.0, 8.6, 1.7, 3.8, GAL); return p
    for i in range(6):
        x = -8.5 + i * 3.4
        p.tube((x, 1.6, 0.0), (x, 1.6, 3.8), 0.07, 0.07, seg, GAL, caps=(False, True)); p.tube((x, -1.6, 0.0), (x, -1.6, 3.74), 0.07, 0.07, seg, GAL, caps=(False, True))
    p.box(-9.0, -2.05, 3.55, 9.0, -1.95, 4.1, "paint_blue_bus", skip=("bottom",))                                                                      # bay-number fascia
    for i in range(3):
        bx = -6.0 + i * 6.0
        p.box(bx - 1.2, 0.2, 0.42, bx + 1.2, 0.7, 0.48, "timber_light"); p.box(bx - 1.2, 0.2, 0.9, bx + 1.2, 0.26, 1.4, "timber_light")             # bench seat and back
        for sx in (-1.1, 1.1): p.box(bx + sx - 0.04, 0.25, 0.0, bx + sx + 0.04, 0.65, 0.42, BLK)
        p.box(bx - 1.3, 1.55, 1.0, bx + 1.3, 1.62, 2.6, "paint_white", skip=("bottom", "front")); p.box(bx - 1.3, 1.5, 1.0, bx + 1.3, 1.55, 2.6, "paint_orange", skip=("bottom", "back"))   # route panel
        p.box(bx - 0.25, -1.75, 3.3, bx + 0.25, -1.7, 3.55, "lantern_glow")
        if lod == 0:
            p.box(bx - 1.3, -1.9, 0.0, bx + 1.3, -1.8, 0.12, "warning_yellow"); p.box(bx - 1.2, 0.2, 0.48, bx + 1.2, 0.25, 0.9, BLK)             # kerb edge, seat support
            for k in range(5): p.box(bx - 1.2 + 0.48 * k, 0.2, 0.48, bx - 1.2 + 0.48 * k + 0.4, 0.7, 0.5, "timber_light")                              # slats
            p.box(bx + 2.1, 0.8, 0.0, bx + 2.6, 1.2, 0.8, GAL)                                                                                          # litter bin
    if lod == 0:
        for i in range(6):
            x = -8.5 + i * 3.4; p.box(x - 0.2, 1.4, 0.0, x + 0.2, 1.8, 0.1, CON); p.box(x - 0.2, -1.8, 0.0, x + 0.2, -1.4, 0.1, CON)
        p.box(-9.0, 1.58, 3.2, 9.0, 1.64, 3.7, GAL)                                                                                                    # back rail
        p.box(-9.05, -2.05, 4.1, 9.05, 2.05, 4.2, GAL)                                                                                                # roof edge flashing
    return p


def bus_shelter_single(lod):
    """5 x 2 m single-bay roadside shelter (replaces the dev placeholder `bus_stop`): flat roof with fascia, glazed back and sides, bench, a route panel and a stop pole with a flag; faces -Y."""
    p = Part("shelter"); seg = 8 if lod == 0 else 6
    p.box(-2.5, -1.0, 2.85, 2.5, 1.0, 3.0, "paint_blue_bus", skip=("bottom",))
    if lod >= 2: p.box(-2.4, 0.8, 0.0, 2.4, 0.9, 2.85, GAL); return p
    for sx in (-2.4, 2.4):
        for sy in (-0.9, 0.9): p.tube((sx, sy, 0.0), (sx, sy, 2.85), 0.04, 0.04, 6, GAL, caps=(False, True))
    p.box(-2.4, 0.86, 0.3, 2.4, 0.9, 2.85, "glass", skip=("bottom",)); p.box(-2.4, -0.9, 0.3, -2.36, 0.9, 2.85, "glass", skip=("bottom",)); p.box(2.36, -0.9, 0.3, 2.4, 0.9, 2.85, "glass", skip=("bottom",))
    p.box(-1.8, 0.35, 0.42, 1.8, 0.8, 0.48, "timber_light"); p.box(-1.8, 0.78, 0.48, 1.8, 0.84, 0.9, "timber_light")
    for sx in (-1.7, 1.7): p.box(sx - 0.04, 0.4, 0.0, sx + 0.04, 0.75, 0.42, BLK)
    p.box(-0.25, -0.98, 2.7, 0.25, -0.95, 2.82, "lantern_glow")
    if lod == 0:
        p.box(-2.0, 0.6, 1.1, -0.6, 0.64, 2.2, "paint_orange", skip=("bottom",))                                                                        # route panel inside the glazing
        p.box(-2.5, -1.02, 2.85, 2.5, -0.98, 3.0, "sign_blue"); p.box(-2.52, -1.0, 3.0, 2.52, 1.02, 3.04, GAL)
        p.tube((3.0, -0.8, 0.0), (3.0, -0.8, 3.0), 0.045, 0.045, seg, GAL, caps=(False, True)); p.box(2.85, -0.84, 2.45, 3.15, -0.76, 2.95, "sign_blue", skip=("bottom",))      # stop pole and flag
        p.box(-2.5, -1.0, 0.0, 2.5, -0.9, 0.08, CON, skip=("bottom",)); p.box(-2.5, 0.9, 0.0, 2.5, 1.0, 0.08, CON, skip=("bottom",))                        # base kerbs
        for k in range(7): p.box(-1.8 + 0.6 * k, 0.35, 0.48, -1.8 + 0.6 * k + 0.5, 0.8, 0.5, "timber_light")                                              # seat slats
        p.box(1.9, 0.3, 0.0, 2.3, 0.7, 0.75, GAL)                                                                                                        # litter bin
    return p


def ticket_kiosk(lod):
    """3 x 3 m ticket booth with a counter window and a queue rail: a 2 x 1.5 m box with an overhanging roof, a window and counter, a door at the side, a sign band and a three-post queue rail in front."""
    p = Part("kiosk")
    p.box(-1.0, 0.0, 0.0, 1.0, 1.5, 2.6, "plaster_white", skip=("bottom",)); p.box(-1.25, -0.25, 2.6, 1.25, 1.75, 2.8, "paint_blue_bus", skip=("bottom",))
    if lod >= 2: return p
    p.box(-0.7, -0.02, 1.2, 0.7, 0.0, 2.2, "glass_dark", skip=("bottom", "back")); p.box(-0.8, -0.1, 1.1, 0.8, 0.0, 1.2, "timber_light")                      # window and counter
    if lod <= 1: p.box(1.0, 0.6, 0.0, 1.04, 1.3, 2.1, "wood_door")
    if lod == 0:
        p.box(-0.8, -0.12, 2.25, 0.8, -0.02, 2.5, "sign_blue"); p.box(-0.8, -0.12, 1.1, -0.74, 0.0, 2.3, GAL); p.box(0.74, -0.12, 1.1, 0.8, 0.0, 2.3, GAL)    # sign band, window frame
        p.box(-1.0, -0.02, 0.0, 1.0, 0.0, 0.12, CON)
        for k in range(3):
            x = -1.0 + k * 1.0; p.tube((x, -1.6, 0.0), (x, -1.6, 1.0), 0.03, 0.03, 6, GAL, caps=(False, True))
        p.tube((-1.0, -1.6, 0.95), (1.0, -1.6, 0.95), 0.025, 0.025, 6, GAL); p.tube((-1.0, -1.6, 0.55), (1.0, -1.6, 0.55), 0.02, 0.02, 6, GAL)           # queue rail
        p.box(-0.2, -0.2, 2.45, 0.2, -0.12, 2.6, "lantern_glow")
    p.xform(0, Matrix.Translation((0, -0.06, 0))); return p


def departure_board(lod):
    """Double-face departures board on two posts: 3 x 0.25 m panel, 1.2 m high, its lower edge 3.3 m above the ground (a bus can stand under it); faces -Y and +Y."""
    p = Part("board"); seg = 8 if lod == 0 else 6
    p.box(-1.5, -0.12, 3.3, 1.5, 0.12, 4.5, GAL, skip=("bottom",))
    if lod >= 2:
        for sx in (-1.3, 1.3): p.box(sx - 0.07, -0.07, 0.0, sx + 0.07, 0.07, 3.3, GAL, skip=("bottom", "top"))
        return p
    for sx in (-1.3, 1.3): p.tube((sx, 0.0, 0.0), (sx, 0.0, 4.5), 0.07, 0.07, seg, GAL, caps=(False, True)); p.box(sx - 0.2, -0.2, 0.0, sx + 0.2, 0.2, 0.1, CON, skip=("bottom",))
    p.box(-1.45, -0.14, 3.35, 1.45, -0.12, 4.45, "board_dark", skip=("bottom", "back")); p.box(-1.45, 0.12, 3.35, 1.45, 0.14, 4.45, "board_dark", skip=("bottom", "front"))
    for k in range(5): p.box(-1.4, -0.145, 3.45 + 0.2 * k, 1.4, -0.14, 3.5 + 0.2 * k, "signal_amber", skip=("bottom", "back"))                                  # blank text rows
    if lod == 0:
        for k in range(5): p.box(-1.4, 0.14, 3.45 + 0.2 * k, 1.4, 0.145, 3.5 + 0.2 * k, "signal_amber", skip=("bottom", "front"))
        p.box(-1.5, -0.14, 4.5, 1.5, 0.14, 4.56, GAL); p.box(-0.9, -0.16, 4.25, 0.9, -0.14, 4.3, "paint_white", skip=("bottom", "back"))
        for sx in (-1.3, 1.3):
            p.box(sx - 0.09, -0.09, 4.5, sx + 0.09, 0.09, 4.62, GAL)
            for bx in (-0.14, 0.14):
                for by in (-0.14, 0.14): p.tube((sx + bx, by, 0.1), (sx + bx, by, 0.16), 0.025, 0.025, 6, GAL)                                               # base bolts
            p.box(sx - 0.1, -0.02, 0.5, sx + 0.1, 0.02, 3.3, GAL)                                                                                         # post web
        p.tube((-1.3, 0.0, 3.0), (-0.1, 0.0, 3.3), 0.025, 0.025, 6, GAL); p.tube((1.3, 0.0, 3.0), (0.1, 0.0, 3.3), 0.025, 0.025, 6, GAL)                  # braces
        for sx in (-0.9, 0.9): p.box(sx - 0.15, -0.2, 4.56, sx + 0.15, -0.12, 4.7, "lantern_glow")                                                      # board lights
    return p


def minibus_stand(lod):
    """12 x 6 m minibus stand: a corrugated tin roof on eight round poles over a loading lane, three route boards on the back, a long bench, a queue rail along the front and a painted kerb."""
    p = Part("stand"); seg = 10 if lod == 0 else 6
    prism_x(p, [(-3.0, 3.3), (3.0, 3.6), (3.0, 3.66), (-3.0, 3.36)], -6.0, 6.0, "roof_zinc_rust")
    if lod >= 2: p.box(-6.0, 2.55, 0.0, 6.0, 2.75, 3.4, GAL); return p
    for i in range(4):
        x = -5.4 + i * 3.6
        for y in (-2.7, 2.7): p.tube((x, y, 0.0), (x, y, 3.3 if y < 0 else 3.6), 0.06, 0.06, seg, GAL, caps=(False, True))
    p.box(-6.0, 2.6, 0.0, 6.0, 2.7, 0.9, CDK, skip=("bottom",))                                                                                        # low back wall
    for i in range(3):
        bx = -4.0 + i * 4.0; p.box(bx - 1.6, 2.58, 1.4, bx + 1.6, 2.62, 3.0, "paint_orange", skip=("bottom",)); p.box(bx - 1.5, 2.55, 2.4, bx + 1.5, 2.58, 2.9, "paint_white", skip=("bottom",))
    p.box(-5.5, 1.5, 0.42, 5.5, 2.0, 0.48, "timber_light")
    for sx in (-5.0, -2.5, 0.0, 2.5, 5.0): p.box(sx - 0.05, 1.55, 0.0, sx + 0.05, 1.95, 0.42, BLK)
    p.box(-6.0, -3.0, 3.3, 6.0, -2.94, 3.6, "paint_orange", skip=("bottom",))                                                                           # front fascia
    if lod == 0:
        for k in range(18): p.box(-5.9 + 0.66 * k, -2.5, 3.58, -5.9 + 0.66 * k + 0.08, 2.9, 3.64, GAL)                                                  # roof ribs
        p.tube((-5.7, -2.2, 0.95), (5.7, -2.2, 0.95), 0.025, 0.025, 6, GAL)
        for x in (-5.7, -2.85, 0.0, 2.85, 5.7): p.tube((x, -2.2, 0.0), (x, -2.2, 0.95), 0.025, 0.025, 6, GAL, caps=(False, True))                       # queue rail
        for k in range(6): p.box(-6.0 + 2.0 * k, -3.0, 0.0, -5.0 + 2.0 * k, -2.4, 0.15, "kerb_black"); p.box(-5.0 + 2.0 * k, -3.0, 0.0, -4.0 + 2.0 * k, -2.4, 0.15, "kerb_white")   # painted kerb
        for x in (-4.0, 4.0): p.box(x - 0.25, -2.98, 3.2, x + 0.25, -2.92, 3.28, "lantern_glow")
    return p


def tricycle_rank(lod):
    """8 x 5 m tricycle taxi rank: a small flat canopy on four poles with a bench and a call-board, a painted kerb along the front and queue marks on the ground (thin raised paint)."""
    p = Part("rank"); seg = 8 if lod == 0 else 6
    p.box(-2.5, 0.2, 2.7, 2.5, 2.4, 2.9, "paint_orange", skip=("bottom",))
    if lod >= 2: p.box(-2.4, 2.3, 0.0, 2.4, 2.4, 2.7, GAL); return p
    for sx in (-2.3, 2.3):
        for sy in (0.4, 2.2): p.tube((sx, sy, 0.0), (sx, sy, 2.7), 0.05, 0.05, seg, GAL, caps=(False, True))
    p.box(-2.0, 1.8, 0.42, 2.0, 2.3, 0.48, "timber_light"); p.box(-2.0, 2.28, 0.48, 2.0, 2.34, 0.9, "timber_light")
    p.box(-1.2, 2.34, 1.2, 1.2, 2.38, 2.4, "paint_white", skip=("bottom",))
    for k in range(8): p.box(-4.0 + k, -2.5, 0.0, -3.0 + k, -1.9, 0.12, "kerb_black" if k % 2 == 0 else "kerb_white", skip=("bottom",))                      # painted kerb
    if lod == 0:
        for k in range(4): p.box(-3.3 + 2.2 * k, -1.7, 0.0, -3.2 + 2.2 * k, 0.1, 0.012, "paint_white", skip=("bottom",)); p.box(-3.3 + 2.2 * k, -1.7, 0.0, -2.1 + 2.2 * k, -1.6, 0.012, "paint_white", skip=("bottom",))   # queue marks
        for k in range(4): p.box(-2.0 + 1.2 * k, 1.85, 0.48, -1.9 + 1.2 * k, 2.25, 0.5, "timber_light")
        p.box(-2.45, 2.38, 0.0, -2.35, 2.5, 1.0, GAL); p.box(2.35, 2.38, 0.0, 2.45, 2.5, 1.0, GAL); p.box(-0.4, 0.25, 2.62, 0.4, 0.35, 2.7, "lantern_glow")
        p.box(-2.5, 0.2, 2.9, 2.5, 2.4, 2.96, GAL)
    return p


def parking_bay_barrier(lod):
    """Tileable parking-bay edge, 2.5 m: a concrete wheel stop, a low tubular rail on two posts and a raised planter box with a clipped shrub."""
    p = Part("barrier"); seg = 6
    if lod >= 2: p.box(-1.25, -0.4, 0.0, 1.25, 0.3, 0.6, CON); return p
    p.frustum(-1.0, -0.15, 0.0, 1.0, 0.15, 0.14, CON, 0.04, skip=("bottom",))                                                                          # wheel stop
    p.box(-1.2, 0.1, 0.0, 1.2, 0.3, 0.45, CON, skip=("bottom",))                                                                                      # planter wall
    p.box(-1.1, 0.14, 0.45, 1.1, 0.26, 0.5, "dry_earth", skip=("bottom",))
    p.frustum(-0.6, 0.12, 0.5, 0.6, 0.28, 0.68, "hedge_green", 0.07, skip=("bottom",))
    for sx in (-1.0, 1.0): p.tube((sx, -0.35, 0.0), (sx, -0.35, 0.6), 0.035, 0.035, seg, "warning_yellow", caps=(False, True))
    p.tube((-1.0, -0.35, 0.55), (1.0, -0.35, 0.55), 0.03, 0.03, seg, "warning_yellow")
    if lod == 0:
        p.tube((-1.0, -0.35, 0.3), (1.0, -0.35, 0.3), 0.025, 0.025, 6, "warning_yellow")
        for sx in (-0.9, 0.9): p.frustum(sx - 0.2, 0.13, 0.5, sx + 0.2, 0.27, 0.62, "hedge_green", 0.05, skip=("bottom",))
        for sx in (-1.0, 1.0): p.box(sx - 0.07, -0.42, 0.0, sx + 0.07, -0.28, 0.05, CON)
    return p


# ------------------------------------------------------------------------------------------------------------------------------------------------ catalogue extras
extra("kano_bus_terminal_hall_a", "B", "CITY_SPECIFIC", REF, heads=[(-20.0 + 2.5 + i * 5.0, -10.5, 4.75) for i in range(8)], preset="glow_door", label={"c": (0.0, -11.05, 5.8), "width": 24.0, "height": 0.8},
      sockets=[(f"bay_{i + 1}", "bus_bay_stop", (-12.0 + 8.0 * i, -22.0, 0.0), 0) for i in range(4)], tags=["bus_terminal", "transport", "commercial"])
extra("kano_bus_shelter_long_a", "B", "CITY_SPECIFIC", REF, heads=[(-6.0 + 6.0 * i, -1.7, 3.4) for i in range(3)], preset="glow", label={"c": (0.0, -2.07, 3.82), "width": 17.0, "height": 0.45}, tags=["bus_terminal", "transport"],
      sockets=[(f"board_{i + 1}", "passenger_board", (-6.0 + 6.0 * i, -2.5, 0.0), 0) for i in range(3)])
extra("aro_bus_shelter_a", "B", "GLOBAL", REF, heads=[(0.0, -0.96, 2.76)], preset="glow", label={"c": (0.0, -1.03, 2.93), "width": 4.8, "height": 0.14}, tags=["bus_stop", "transport"], sockets=[("board", "passenger_board", (0.0, -1.8, 0.0), 0)])
extra("kano_ticket_kiosk_a", "B", "CITY_SPECIFIC", REF, heads=[(0.0, -0.3, 2.45)], preset="glow", label={"c": (0.0, -0.2, 2.37), "width": 1.5, "height": 0.22}, tags=["bus_terminal", "transport"], sockets=[("queue", "ticket_queue", (0.0, -2.0, 0.0), 0)])
extra("kano_departure_board_a", "B", "CITY_SPECIFIC", REF, heads=[(0.0, -0.2, 4.7)], preset="glow", label={"c": (0.0, -0.145, 3.9), "width": 2.9, "height": 1.0, "colour": "yellow"}, tags=["bus_terminal", "transport"])
extra("kano_minibus_stand_canopy_a", "B", "CITY_SPECIFIC", REF, heads=[(-4.0, -2.95, 3.2), (4.0, -2.95, 3.2)], preset="glow", label={"c": (0.0, -3.0, 3.45), "width": 11.0, "height": 0.3}, tags=["minibus", "transport"],
      sockets=[(f"load_{i + 1}", "minibus_load", (-4.0 + 4.0 * i, -5.0, 0.0), 0) for i in range(3)])
extra("kano_tricycle_rank_a", "B", "CITY_SPECIFIC", REF, heads=[(0.0, 0.3, 2.6)], preset="glow", tags=["tricycle", "transport"], sockets=[("rank_1", "tricycle_queue", (-3.0, -1.0, 0.0), 0), ("rank_2", "tricycle_queue", (-0.8, -1.0, 0.0), 0)])
extra("aro_parking_bay_barrier_set", "B", "GLOBAL", REF, tags=["transport", "tile_2_5m"])
