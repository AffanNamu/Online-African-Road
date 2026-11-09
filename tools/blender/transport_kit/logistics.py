"""Family A: logistics and warehouse site (district D10; references R-013, R-014, R-015, RB-13). Parametric builders; every function takes the LOD (0 detailed, 1 medium, 2 mass) and returns a Part.
Real dimensions (metres), front at -Y, origin on the ground at the footprint centre. No brand, no plate, no logo: liveries are plain colours. Documented in docs/TRANSPORT_KIT.md."""
from __future__ import annotations
import math
from mathutils import Vector
from kano_kit.geo import Part
from .common import prism_x, circle_pts, rib_run, extra

GAL, BLK, CON, CDK, SHT = "metal_galvanised", "metal_gate_black", "concrete", "concrete_dark", "shutter_steel"
REF = ["R-013", "R-014", "R-015", "RB-13"]
DOCK_H = 1.2          # dock floor height above the yard: matches a truck bed (about 1.1 to 1.3 m)
DOOR_W, DOOR_H = 3.0, 3.2


# ------------------------------------------------------------------------------------------------------------------------------------------------ shared pieces
def dock_front(p: Part, centres, y0, lod, door_h=DOOR_H, lamp=True):
    """Loading docks on the front face (y = y0): platform edge, door, frame, bumpers, hood and lamp for every centre x. The platform itself is built by the caller."""
    for cx in centres:
        if lod >= 2: continue
        p.box(cx - DOOR_W / 2, y0 - 0.06, DOCK_H, cx + DOOR_W / 2, y0, DOCK_H + door_h, SHT, skip=("bottom", "back"))                                  # roller door
        if lod == 0:
            for k in range(1, 8): p.box(cx - DOOR_W / 2, y0 - 0.09, DOCK_H + door_h * k / 8 - 0.015, cx + DOOR_W / 2, y0 - 0.06, DOCK_H + door_h * k / 8 + 0.015, GAL, skip=("bottom", "back"))
        p.box(cx - DOOR_W / 2 - 0.25, y0 - 0.12, DOCK_H, cx - DOOR_W / 2, y0, DOCK_H + door_h + 0.25, CDK, skip=("bottom", "back"))                    # jambs and header
        p.box(cx + DOOR_W / 2, y0 - 0.12, DOCK_H, cx + DOOR_W / 2 + 0.25, y0, DOCK_H + door_h + 0.25, CDK, skip=("bottom", "back"))
        p.box(cx - DOOR_W / 2 - 0.25, y0 - 0.12, DOCK_H + door_h, cx + DOOR_W / 2 + 0.25, y0, DOCK_H + door_h + 0.25, CDK, skip=("bottom", "back"))
        for s in (-1, 1): p.box(cx + s * 1.1 - 0.15, y0 - 1.0 - 0.12, 0.55, cx + s * 1.1 + 0.15, y0 - 1.0, 1.0, "rubber", skip=("bottom", "back"))     # bumpers on the platform face
        if lod == 0:
            p.box(cx - DOOR_W / 2 - 0.3, y0 - 0.65, DOCK_H + door_h + 0.3, cx + DOOR_W / 2 + 0.3, y0, DOCK_H + door_h + 0.38, GAL)                      # hood
            if lamp:
                p.box(cx - 0.06, y0 - 0.12, DOCK_H + door_h + 0.55, cx + 0.06, y0, DOCK_H + door_h + 0.9, GAL); p.box(cx - 0.18, y0 - 0.34, DOCK_H + door_h + 0.45, cx + 0.18, y0 - 0.12, DOCK_H + door_h + 0.7, "lantern_glow")


def platform(p: Part, x0, x1, y0, depth, lod):
    """The dock platform: a concrete slab DOCK_H high with a yellow edge and (lod 0) a flight of three steps with a handrail at the left end."""
    p.box(x0, y0 - depth, 0.0, x1, y0, DOCK_H, CON, skip=("bottom",))
    if lod <= 1: p.box(x0, y0 - depth, DOCK_H, x1, y0 - depth + 0.12, DOCK_H + 0.012, "warning_yellow", skip=("bottom",))
    if lod == 0:
        yf = y0 - depth
        for i in (1, 2, 3): p.box(x0 + 0.2, yf - 0.3 * i, 0.0, x0 + 1.4, yf - 0.3 * (i - 1), DOCK_H - 0.3 * i, CON, skip=("bottom",))
        for sx in (x0 + 0.2, x0 + 1.4):
            p.tube((sx, yf - 0.9, 0.3), (sx, yf - 0.9, 1.0), 0.025, 0.025, 6, GAL, caps=(False, True)); p.tube((sx, yf - 0.9, 1.0), (sx, yf, DOCK_H + 0.9), 0.025, 0.025, 6, GAL)


def pitched_roof(p: Part, x0, x1, y0, y1, eave, ridge, ov, lod, roof="roof_zinc", wall="sheet_cream"):
    """Gable roof along X: roof prism with overhang, gable wall triangles, ridge cap and gutters."""
    ym = (y0 + y1) / 2
    prism_x(p, [(y0 - ov, eave - 0.18), (ym, ridge), (y1 + ov, eave - 0.18)], x0 - ov * 0.5, x1 + ov * 0.5, roof)
    if lod >= 2: return
    prism_x(p, [(y0, eave - 0.01), (ym, ridge - 0.28), (y1, eave - 0.01)], x0 - 0.02, x1 + 0.02, wall)
    if lod == 0:
        p.box(x0 - ov * 0.5, ym - 0.12, ridge - 0.06, x1 + ov * 0.5, ym + 0.12, ridge + 0.1, GAL)
        for yy in (y0 - ov, y1 + ov - 0.12): p.box(x0 - ov * 0.5, yy, eave - 0.32, x1 + ov * 0.5, yy + 0.12, eave - 0.18, GAL)


def walls(p: Part, x0, x1, y0, y1, eave, lod, wall="sheet_cream", pitch=3.0, plinth=1.0):
    """Four walls as one box (no bottom) over a concrete plinth, with vertical ribs (a cheap stand-in for profiled sheet) on the long faces at lod 0."""
    p.box(x0, y0, 0.0, x1, y1, plinth, CON, skip=("bottom",)); p.box(x0 + 0.02, y0 + 0.02, plinth, x1 - 0.02, y1 - 0.02, eave, wall, skip=("bottom",))
    if lod == 0:
        rib_run(p, x0 + 1.0, x1 - 1.0, y0 + 0.02, plinth, eave, wall, pitch, face=-1); rib_run(p, x0 + 1.0, x1 - 1.0, y1 - 0.02, plinth, eave, wall, pitch, face=1)


# ------------------------------------------------------------------------------------------------------------------------------------------------ warehouses
def warehouse_hall_a(lod):
    """48 x 24 x 10 m pitched-roof steel-frame hall: six dock doors on the front, a platform along the front, a flat-roofed office annex at the left end, side pedestrian door and louvres."""
    p = Part("hall"); W, D = 48.0, 22.0; x0, x1, y0, y1 = -W / 2, W / 2, -D / 2, D / 2
    walls(p, x0, x1, y0, y1, 8.0, lod, pitch=3.0); pitched_roof(p, x0, x1, y0, y1, 8.0, 10.0, 1.0, lod)
    platform(p, x0 + 8.0, x1, y0, 1.0, lod)
    centres = [-12 + 6.0 * i for i in range(6)]; dock_front(p, centres, y0, lod)
    # office annex (flat roof, parapet, glazed front) at the left end, flush with the platform's front
    p.box(x0 + 0.1, y0 - 1.0, 0.0, x0 + 8.0, y0 + 7.0, 3.8, "plaster_cream", skip=("bottom",)); p.box(x0 + 0.0, y0 - 1.1, 3.8, x0 + 8.1, y0 + 7.1, 4.2, CON, skip=("bottom",))
    if lod <= 1:
        for k in range(3): p.box(x0 + 0.9 + 2.2 * k, y0 - 1.03, 1.0, x0 + 2.5 + 2.2 * k, y0 - 1.0, 2.8, "glass_dark", skip=("bottom", "back"))
        p.box(x0 + 7.0, y0 - 1.03, 0.0, x0 + 7.9, y0 - 1.0, 2.2, "wood_door", skip=("bottom", "back"))
    if lod == 0:
        for k in range(3): p.box(x0 + 0.8 + 2.2 * k, y0 - 1.06, 0.95, x0 + 2.6 + 2.2 * k, y0 - 1.0, 1.0, CDK, skip=("bottom", "back")); p.box(x0 + 0.8 + 2.2 * k, y0 - 1.06, 2.8, x0 + 2.6 + 2.2 * k, y0 - 1.0, 2.85, CDK, skip=("bottom", "back"))
        p.box(x1 - 0.05, -1.0, 0.0, x1 + 0.12, 0.0, 2.3, SHT, skip=("bottom", "left")); p.box(x1 - 0.05, -1.1, 2.3, x1 + 0.22, 0.1, 2.4, GAL)                  # side pedestrian door and canopy
        for k in range(5): p.box(x0 + 6 + 8 * k, y1 - 0.06, 6.6, x0 + 8 + 8 * k, y1, 7.6, BLK, skip=("bottom", "front"))                                  # rear ventilation louvres
    return p


def warehouse_hall_b(lod):
    """36 x 20 x 8 m low-bay distribution shed: sawtooth roof (north-light glazing), four docks, an open canopy along the front."""
    p = Part("shed"); W, D = 36.0, 18.0; x0, x1, y0, y1 = -W / 2, W / 2, -D / 2, D / 2; eave = 7.0
    walls(p, x0, x1, y0, y1, eave, lod, wall="sheet_green", pitch=3.0, plinth=0.9)
    teeth = 5; w = D / teeth
    for i in range(teeth):
        ya = y0 + w * i; yb = ya + w
        prism_x(p, [(ya, eave - 0.1), (yb, eave - 0.1), (yb, 8.0)], x0 - 0.2, x1 + 0.2, "roof_zinc")
        if lod <= 1: p.box(x0, yb, eave, x1, yb + 0.02, 8.0, "glass_dark", skip=("bottom",))
    platform(p, x0 + 0.0, x1, y0, 1.0, lod)
    centres = [-11.0 + 7.3 * i for i in range(4)]; dock_front(p, centres, y0, lod)
    if lod <= 1:
        p.box(x0, y0 - 3.0, 4.9, x1, y0 - 0.0, 5.05, GAL)                                                                                           # canopy over the platform (open)
        for x in (x0 + 0.4, 0.0, x1 - 0.4): p.box(x - 0.1, y0 - 2.95, DOCK_H, x + 0.1, y0 - 2.75, 4.9, GAL)
    if lod == 0:
        p.box(x1 - 0.05, -1.0, 0.0, x1 + 0.12, 0.0, 2.3, SHT, skip=("bottom", "left")); p.box(-2.0, y1 - 0.06, 2.0, 2.0, y1, 4.4, SHT, skip=("bottom", "front"))
    return p


def warehouse_cold_a(lod):
    """30 x 18 x 9 m insulated cold store: flat roof with parapet, plant deck with three condenser units, vertical panel joints, three insulated dock doors."""
    p = Part("cold"); x0, x1, y0, y1 = -15.0, 15.0, -8.0, 8.0; top = 7.0
    walls(p, x0, x1, y0, y1, top, lod, wall="plaster_white", pitch=1.2, plinth=0.8)
    p.box(x0 - 0.1, y0 - 0.1, top, x1 + 0.1, y1 + 0.1, top + 0.35, CON, skip=("bottom",))                                                            # roof slab and parapet
    platform(p, x0 + 2.0, x1 - 2.0, y0, 1.0, lod)
    centres = [-8.0, 0.0, 8.0]; dock_front(p, centres, y0, lod, door_h=3.0)
    deck_z = top + 0.35
    if lod <= 1:
        p.box(x0 + 3.0, y1 - 7.0, deck_z, x1 - 3.0, y1 - 0.5, deck_z + 1.4, CDK, skip=("bottom",))                                                  # plant deck
        for k in range(3):
            cx = x0 + 7.0 + k * 8.0; p.box(cx - 1.8, y1 - 6.4, deck_z + 1.4, cx + 1.8, y1 - 1.2, deck_z + 2.5, GAL, skip=("bottom",))
            if lod == 0: p.tube((cx, y1 - 3.8, deck_z + 2.5), (cx, y1 - 3.8, deck_z + 2.62), 1.4, 1.4, 14, BLK, caps=(True, True), smooth=False)
        p.box(x1 + 0.0, y0 + 2.0, 0.0, x1 + 0.5, y0 + 4.0, 0.9, CDK, skip=("bottom",))
    if lod == 0:
        p.tube((x1 + 0.05, y0 + 0.5, 0.3), (x1 + 0.05, y0 + 0.5, 8.4), 0.045, 0.045, 6, GAL, caps=(False, True)); p.tube((x1 + 0.05, y0 + 0.9, 0.3), (x1 + 0.05, y0 + 0.9, 8.4), 0.045, 0.045, 6, GAL, caps=(False, True))
        for k in range(14): p.box(x1 + 0.0, y0 + 0.5, 0.5 + k * 0.5, x1 + 0.2, y0 + 0.9, 0.53 + k * 0.5, GAL) if k % 2 == 0 else None                  # access ladder rungs
        p.tube((x1 + 0.5, y0 + 3.0, 0.9), (x1 + 0.5, y0 + 3.0, 6.9), 0.07, 0.07, 6, CDK, caps=(False, True))                                         # service riser to the roof
    return p


# ------------------------------------------------------------------------------------------------------------------------------------------------ dock, containers, pallets
def dock_leveller(lod):
    """One dock bay, 3.5 m pitch (tile it x1 to x6): a wall segment (4.4 m) with the roller door, bumpers, shelter, the platform and the leveller plate; the footprint is centred on y = 0 (wall at the back)."""
    p = Part("dock"); y1 = 0.15; y0 = -0.15; dh = 2.95
    p.box(-1.75, y0, 0.0, 1.75, y1, 4.4, "plaster_cream", skip=("bottom",))
    p.box(-1.75, y0 - 1.05, 0.0, 1.75, y0, DOCK_H, CON, skip=("bottom",))                                                                         # platform
    p.box(-1.75, y0 - 1.05, DOCK_H, 1.75, y0 - 0.93, DOCK_H + 0.012, "warning_yellow", skip=("bottom",))
    if lod < 2:
        p.box(-1.5, y0 - 0.06, DOCK_H, 1.5, y0, DOCK_H + dh, SHT, skip=("bottom", "back"))
        for s in (-1, 1): p.box(s * 1.1 - 0.15, y0 - 1.05 - 0.12, 0.55, s * 1.1 + 0.15, y0 - 1.05, 1.0, "rubber", skip=("bottom", "back"))
        p.box(-1.45, y0 - 0.9, DOCK_H + 0.003, 1.45, y0 - 0.06, DOCK_H + 0.035, GAL, skip=("bottom",))                                               # leveller plate
    if lod == 0:
        for k in range(1, 8): p.box(-1.5, y0 - 0.09, DOCK_H + dh * k / 8 - 0.015, 1.5, y0 - 0.06, DOCK_H + dh * k / 8 + 0.015, GAL, skip=("bottom", "back"))
        for s in (-1, 1): p.box(s * 1.5 - (0.0 if s > 0 else 0.25), y0 - 0.12, DOCK_H, s * 1.5 + (0.25 if s > 0 else 0.0), y0, DOCK_H + dh + 0.25, CDK, skip=("bottom", "back"))
        p.box(-1.75, y0 - 0.12, DOCK_H + dh, 1.75, y0, DOCK_H + dh + 0.25, CDK, skip=("bottom", "back"))
        for s in (-1, 1): p.box(s * 1.3 - 0.03, y0 - 0.55, DOCK_H + 0.4, s * 1.3 + 0.03, y0 - 0.12, DOCK_H + dh + 0.2, "rubber")                      # shelter side curtains
        p.box(-1.3, y0 - 0.55, DOCK_H + dh + 0.1, 1.3, y0 - 0.12, DOCK_H + dh + 0.2, "rubber")
        for s in (-1, 1):
            p.box(s * 0.7 - 0.15, y0 - 0.9, DOCK_H + 0.035, s * 0.7 + 0.15, y0 - 0.5, DOCK_H + 0.05, "warning_yellow", skip=("bottom",))             # hinge lip markings
            p.box(s * 1.65 - 0.05, y0 - 1.0, 0.0, s * 1.65 + 0.05, y0 - 0.9, DOCK_H, GAL)                                                             # edge guard posts
            for k in range(3): p.box(s * 1.6 - 0.05, y0 - 0.5 - 0.15 * k, DOCK_H, s * 1.6 + 0.05, y0 - 0.4 - 0.15 * k, DOCK_H + 0.03, "kerb_black")      # anti-slip strips
        p.box(-0.1, y0 - 0.16, DOCK_H + dh + 0.04, 0.1, y0 - 0.12, DOCK_H + dh + 0.21, "signal_green")                                                # signal lamp on the header
        p.box(1.0, y0 - 0.1, DOCK_H + 0.4, 1.3, y0, DOCK_H + 0.85, GAL); p.box(1.08, y0 - 0.12, DOCK_H + 0.55, 1.22, y0 - 0.1, DOCK_H + 0.7, "signal_red")      # control box
        p.tube((-1.2, y0 - 0.5, DOCK_H), (-1.2, y0 - 0.5, DOCK_H + 0.3), 0.05, 0.05, 6, BLK, caps=(False, True))                                      # hydraulic ram
    from mathutils import Matrix
    p.xform(0, Matrix.Translation((0, 0.525, 0))); return p


CONTAINER_LIVERIES = {"a": "container_blue", "b": "container_red", "c": "container_green", "d": "container_grey"}


def _container(p: Part, cx, cy, z0, L, key, lod, ribs, doors_plus_x=True, ry=2.438, H=2.591):
    """A shipping container, long axis X: corrugated side walls (`ribs` per side), corner posts, top and bottom rails, door end with locking bars, roof."""
    hx, hy = L / 2, ry / 2; x0, x1, y0, y1, z1 = cx - hx, cx + hx, cy - hy, cy + hy, z0 + H
    p.box(x0 + 0.04, y0 + 0.05, z0 + 0.13, x1 - 0.04, y1 - 0.05, z1 - 0.1, key, skip=("bottom",) if z0 == 0.0 else ())
    if lod >= 2: return
    for sx in (x0, x1 - 0.12):
        for sy in (y0, y1 - 0.12): p.box(sx, sy, z0, sx + 0.12, sy + 0.12, z1, key, skip=("bottom",) if z0 == 0.0 else ())                               # corner posts
    p.box(x0, y0, z1 - 0.1, x1, y1, z1, key); p.box(x0, y0, z0, x1, y0 + 0.1, z0 + 0.13, key, skip=("bottom",) if z0 == 0.0 else ()); p.box(x0, y1 - 0.1, z0, x1, y1, z0 + 0.13, key, skip=("bottom",) if z0 == 0.0 else ())
    if lod >= 1: return
    n = ribs
    for sy, face in ((y0 + 0.05, -1), (y1 - 0.05, 1)):
        rib_run(p, x0 + 0.3, x1 - 0.3, sy, z0 + 0.2, z1 - 0.15, key, (L - 0.6) / n, w=0.16, d=0.05, face=face)
    ex = x1 if doors_plus_x else x0
    for k, off in enumerate((-0.32, -0.1, 0.1, 0.32)): sx = ex - 0.04 if doors_plus_x else ex + 0.04; p.box(min(sx, sx + (0.05 if doors_plus_x else -0.05)), cy + off - 0.015, z0 + 0.2, max(sx, sx + (0.05 if doors_plus_x else -0.05)), cy + off + 0.015, z1 - 0.2, GAL)


def container_20(variant):
    def build(lod):
        p = Part("container"); _container(p, 0.0, 0.0, 0.0, 6.058, CONTAINER_LIVERIES[variant], lod, ribs=11); return p
    return build


def container_40(variant):
    def build(lod):
        p = Part("container"); _container(p, 0.0, 0.0, 0.0, 12.192, CONTAINER_LIVERIES[variant], lod, ribs=11); return p
    return build


def container_stack(lod):
    """Three 40 ft containers stacked 3 high (7.8 m), alternating liveries and door ends, merged into one mesh to save draw calls."""
    p = Part("stack")
    for i, (key, d) in enumerate((("container_blue", True), ("container_red", False), ("container_grey", True))):
        _container(p, 0.0, 0.0, 2.6 * i + (0.0 if i == 0 else 0.0), 12.192, key, 1 if lod == 0 and False else lod, ribs=6, doors_plus_x=d, H=2.6)
    return p


def pallet_stack(lod):
    """Four wrapped pallets on a 3 x 2 m patch: sacks (three layers), cartons, a crate and a wrapped load."""
    p = Part("pallets"); spots = ((-0.75, -0.5), (0.75, -0.5), (-0.75, 0.6), (0.75, 0.6))
    for (cx, cy) in spots:
        if lod >= 2: p.box(cx - 0.6, cy - 0.5, 0.0, cx + 0.6, cy + 0.5, 0.14, "timber_weathered", skip=("bottom",)); continue
        if lod == 1: p.box(cx - 0.6, cy - 0.5, 0.0, cx + 0.6, cy + 0.5, 0.14, "timber_weathered", skip=("bottom",)); continue
        for k in range(5): p.box(cx - 0.6, cy - 0.5 + 0.22 * k, 0.1, cx + 0.6, cy - 0.5 + 0.22 * k + 0.12, 0.14, "timber_weathered")
        for k in range(3):
            p.box(cx - 0.6, cy - 0.5 + 0.44 * k, 0.0, cx + 0.6, cy - 0.5 + 0.44 * k + 0.1, 0.03, "timber_weathered")
            for j in range(3): p.box(cx - 0.6 + 0.54 * j, cy - 0.5 + 0.44 * k, 0.03, cx - 0.6 + 0.54 * j + 0.12, cy - 0.5 + 0.44 * k + 0.1, 0.1, "timber_weathered")
    def goods(box, key):
        p.box(*box, key, skip=("bottom",))
    if lod >= 2:
        for (cx, cy), h in zip(spots, (0.9, 0.9, 1.2, 1.8)): p.box(cx - 0.55, cy - 0.45, 0.14, cx + 0.55, cy + 0.45, 0.14 + h, "burlap")
        return p
    if lod == 1:
        goods((-1.3, -0.95, 0.14, -0.2, -0.05, 0.98), "burlap"); goods((0.2, -0.95, 0.14, 1.3, -0.05, 1.12), "plaster_cream"); goods((-1.3, 0.1, 0.14, -0.2, 1.1, 1.2), "timber_weathered"); goods((0.2, 0.1, 0.14, 1.3, 1.1, 1.8), "paint_white")
        for k in range(5): p.box(0.18, 0.08, 0.3 + 0.3 * k, 1.32, 1.12, 0.33 + 0.3 * k, "fabric_white", skip=("bottom",))
        for z in (0.4, 0.8, 1.2): p.box(-1.32, 0.08, z - 0.03, -0.18, 1.12, z + 0.03, "timber_weathered", skip=("bottom",))
        return p
    for k in range(3):
        for j in range(2): p.frustum(-1.3 + 0.55 * j, -0.95, 0.14 + 0.28 * k, -0.77 + 0.55 * j + 0.0, -0.05, 0.14 + 0.28 * k + 0.28, "burlap", 0.05)           # sacks, two stacks of three
    for k in range(3): goods((0.2, -0.95, 0.14 + 0.32 * k, 1.3, -0.05, 0.14 + 0.32 * (k + 1)), "plaster_cream")
    for k in range(3): p.box(0.2, -0.95, 0.14 + 0.32 * (k + 1) - 0.02, 1.3, -0.05, 0.14 + 0.32 * (k + 1) + 0.0, "timber_weathered")                         # strap lines
    p.box(0.2, -0.95, 1.1, 1.3, -0.05, 1.12, "paint_white")
    goods((-1.3, 0.1, 0.14, -0.2, 1.1, 1.2), "timber_weathered")
    for z in (0.4, 0.8, 1.2): p.box(-1.32, 0.08, z - 0.03, -0.18, 1.12, z + 0.03, "timber_weathered", skip=("bottom",))                                    # crate battens
    for sx in (-1.32, -0.2): p.box(sx, 0.08, 0.14, sx + 0.02, 0.12, 1.2, "timber_weathered")
    goods((0.2, 0.1, 0.14, 1.3, 1.1, 1.8), "paint_white")
    for k in range(5): p.box(0.18, 0.08, 0.3 + 0.3 * k, 1.32, 1.12, 0.33 + 0.3 * k, "fabric_white", skip=("bottom",))                                         # stretch-wrap bands
    p.box(0.18, 0.08, 1.8, 1.32, 1.12, 1.83, "fabric_white")
    return p


# ------------------------------------------------------------------------------------------------------------------------------------------------ gate, fences, walls
def gate_sliding(lod):
    """Two-lane yard gate: a centre guard hut on a kerbed island between an entry and an exit lane (3.5 m each, 9 m clear overall with the island), the two cantilever leaves OPEN and retracted along the fence line
    outside the posts, barrier arms up. Leaves and hut collide; the lanes are clear."""
    p = Part("gate")
    p.box(-1.0, -2.0, 0.0, 1.0, 2.0, 0.15, CON, skip=("bottom",))                                                                                       # island
    p.box(-0.9, -0.9, 0.15, 0.9, 0.9, 2.6, "plaster_cream", skip=("bottom",)); p.box(-1.0, -1.0, 2.6, 1.0, 1.0, 2.75, CON, skip=("bottom",))
    if lod <= 1:
        p.box(-0.6, -0.93, 1.1, 0.6, -0.9, 2.2, "glass_dark", skip=("bottom", "back"))
    if lod == 0: p.box(0.92, -0.35, 0.15, 0.95, 0.35, 2.1, "wood_door")
    for s in (-1, 1):
        px = s * 4.75; x_in, x_out = (4.95, 8.4); a, b = (x_in, x_out) if s > 0 else (-x_out, -x_in)
        if lod >= 2: p.box(a, -0.15, 0.0, b, 0.15, 2.0, GAL); continue
        p.box(px - 0.15, -0.2, 0.0, px + 0.15, 0.2, 2.6, CON, skip=("bottom",))                                                                          # gate post
        p.box(a, -0.05, 0.25, b, 0.05, 0.3, GAL); p.box(a, -0.05, 1.95, b, 0.05, 2.0, GAL)                                                              # rails of the retracted leaf
        n = 18 if lod == 0 else 8
        for k in range(n): x = a + (b - a) * k / (n - 1); p.box(x - 0.025, -0.05, 0.3, x + 0.025, 0.05, 1.95, GAL)
        if lod == 0:
            p.box(a, -0.1, 0.0, b, 0.1, 0.15, CDK, skip=("bottom",)); p.tube((a, -0.2, 0.12), (b, -0.2, 0.12), 0.04, 0.04, 6, BLK, caps=(True, True))     # track
            for k in range(4): xw = a + (b - a) * (k + 0.5) / 4; p.tube((xw, -0.17, 0.0), (xw, -0.17, 0.12), 0.06, 0.06, 6, BLK, caps=(False, True))        # carriage wheels
            for k in range(2): p.box(a + (b - a) * (0.2 + 0.6 * k) - 0.05, -0.08, 2.0, a + (b - a) * (0.2 + 0.6 * k) + 0.05, 0.08, 2.15, GAL)           # top guide rollers
    if lod == 0:
        for lane in (-1, 1):
            cx = lane * 1.25; p.box(cx - 0.12, -1.9, 0.15, cx + 0.12, -1.66, 1.0, "warning_yellow", skip=("bottom",)); p.tube((cx, -1.78, 1.0), (cx, -1.78, 2.4), 0.035, 0.035, 6, "paint_white")
            p.box(cx - 0.07, -1.78 - 0.07, 2.2, cx + 0.07, -1.78 + 0.07, 2.4, "paint_red")
        p.box(-0.1, -1.0, 2.75, 0.1, -0.9, 3.05, GAL); p.box(-0.35, -1.12, 3.0, 0.35, -0.9, 3.12, "lantern_glow")                                            # lamp on the hut front
        for s in (-1, 1): p.box(s * 0.95 - 0.04, -1.9, 0.0, s * 0.95 + 0.04, -1.8, 0.8, "warning_yellow")                                                  # island bollards
    return p


def fence_palisade(lod):
    """4 m run of steel palisade fence 2.4 m high: a post at the left end, three rails, 16 pickets with a bevelled head (kept lean: a fence run is tiled hundreds of times, the row budget is 220 triangles). Tile it end to end."""
    p = Part("fence")
    if lod >= 2: p.box(-2.0, -0.05, 0.0, 2.0, 0.05, 2.4, GAL); return p
    p.box(-2.0, -0.06, 0.0, -1.88, 0.06, 2.55, GAL, skip=("bottom",))
    for z in (0.3, 1.2, 2.0): p.box(-1.88, -0.035, z, 2.0, 0.035, z + 0.1, GAL, skip=("bottom", "left", "right"))
    n = 16 if lod == 0 else 7
    for k in range(n):
        x = -1.78 + 3.78 * k / (n - 1)
        if lod == 0: p.frustum(x - 0.02, -0.02, 0.05, x + 0.02, 0.02, 2.4, GAL, 0.012, skip=("bottom",))
        else: p.box(x - 0.02, -0.02, 0.05, x + 0.02, 0.02, 2.4, GAL, skip=("bottom",))
    return p


def wall_plot(lod):
    """4 m run of rendered plot wall 2.4 m high with a coping, a plinth, a recessed panel and a pier at the left end. Tile it end to end."""
    p = Part("wall")
    if lod >= 2: p.box(-2.0, -0.125, 0.0, 2.0, 0.125, 2.4, "plaster_cream"); return p
    p.box(-1.9, -0.11, 0.0, 2.0, 0.11, 2.3, "plaster_cream", skip=("bottom",)); p.box(-2.0, -0.125, 0.0, -1.84, 0.125, 2.4, CON, skip=("bottom",))
    p.box(-1.9, -0.125, 2.3, 2.0, 0.125, 2.4, CON, skip=("bottom",))
    if lod == 1: return p
    p.box(-1.9, -0.125, 0.0, 2.0, 0.125, 0.3, CON, skip=("bottom",))                                                                                      # plinth
    for k in range(2):
        x0 = -1.6 + 1.9 * k; p.box(x0, -0.12, 0.5, x0 + 1.6, -0.11, 2.1, "plaster_white", skip=("bottom", "back")); p.box(x0 - 0.04, -0.125, 0.46, x0 + 1.64, -0.11, 0.5, CON, skip=("bottom", "back"))
        p.box(x0 - 0.04, -0.125, 2.1, x0 + 1.64, -0.11, 2.14, CON, skip=("bottom", "back"))
    for k in range(2):
        x0 = -1.6 + 1.9 * k; p.box(x0, 0.11, 0.5, x0 + 1.6, 0.12, 2.1, "plaster_white", skip=("bottom", "front")); p.box(x0 - 0.04, 0.11, 0.46, x0 + 1.64, 0.125, 0.5, CON, skip=("bottom", "front")); p.box(x0 - 0.04, 0.11, 2.1, x0 + 1.64, 0.125, 2.14, CON, skip=("bottom", "front"))
    for k in range(4): p.box(-2.0, -0.13 if k % 2 == 0 else -0.135, 0.3 + 0.5 * k, -1.82, 0.13, 0.3 + 0.5 * k + 0.16, CON, skip=("bottom",))                       # pier quoins
    p.box(-2.02, -0.14, 2.4, -1.82, 0.14, 2.48, CON, skip=("bottom",))                                                                                    # pier cap
    return p


# ------------------------------------------------------------------------------------------------------------------------------------------------ weighbridge, fuel island, forklift, trailers
def weighbridge(lod):
    """18 m truck weighbridge: a 3.4 m wide steel deck (drive-over, 0.12 m high, chamfered, so a truck rolls over it and no collider is placed on it), deck joints and load-cell covers, a cabin with a window,
    a pole-mounted weight display and bollards at the entry."""
    p = Part("wb"); y0, y1 = -3.0, 0.4
    p.frustum(-9.0, y0, 0.0, 9.0, y1, 0.12, CDK, 0.04, skip=("bottom",)); p.box(-8.85, y0 + 0.1, 0.12, 8.85, y1 - 0.1, 0.135, GAL, skip=("bottom",))
    p.box(-3.5, 1.0, 0.0, 0.5, 3.0, 2.8, "plaster_cream", skip=("bottom",))
    if lod >= 2: return p
    p.box(-3.7, 0.8, 2.8, 0.7, 3.2, 3.0, CON, skip=("bottom",))
    p.box(-3.0, 0.97, 1.1, -0.2, 1.0, 2.3, "glass_dark", skip=("bottom", "back")); p.box(-0.2, 0.97, 0.0, 0.4, 1.0, 2.1, "wood_door", skip=("bottom", "back"))
    p.tube((8.5, y0 + 0.6, 0.0), (8.5, y0 + 0.6, 2.6), 0.06, 0.06, 6, GAL, caps=(False, True)); p.box(7.7, y0 + 0.5, 2.6, 9.3, y0 + 0.7, 3.2, "board_dark", skip=("bottom",))
    if lod == 0:
        for k in range(8): p.box(-8.0 + 2.0 * k, y0 + 0.02, 0.135, -7.9 + 2.0 * k, y1 - 0.02, 0.15, CDK)                                                 # deck joints
        for k in range(4):
            for sy in (y0 + 0.5, y1 - 0.5): p.box(-7.0 + 4.6 * k - 0.3, sy - 0.2, 0.135, -7.0 + 4.6 * k + 0.3, sy + 0.2, 0.16, BLK, skip=("bottom",))      # load-cell covers
        for sx in (-9.6, 9.6):
            for sy in (y0 - 0.2, y1 + 0.2): p.tube((sx, sy, 0.0), (sx, sy, 0.9), 0.07, 0.07, 8, "warning_yellow", caps=(False, True))
        p.box(-2.0, 0.995, 2.7, 0.0, 1.05, 2.78, GAL)                                                                                                    # window canopy
        p.box(-3.2, 3.0, 2.0, -2.2, 3.35, 2.6, GAL, skip=("bottom",)); p.tube((-2.7, 3.2, 2.6), (-2.7, 3.2, 2.9), 0.25, 0.25, 8, BLK)                  # air conditioner and fan
        for k in range(3): p.box(0.5, 1.2 + 0.25 * k, 0.1 * (k + 1), 1.5, 1.45 + 0.25 * k, 0.1 * (k + 1) + 0.03, CON)                                   # cabin steps (flat treads)
        for k in range(6): p.box(7.75 + 0.27 * k, y0 + 0.44, 2.7, 7.95 + 0.27 * k, y0 + 0.5, 3.1, "signal_amber")                                      # display digits (blank plates)
        p.box(7.6, y0 + 0.48, 2.5, 9.4, y0 + 0.72, 2.6, GAL); p.box(7.6, y0 + 0.48, 3.2, 9.4, y0 + 0.72, 3.3, GAL)
    return p


def fuel_island_truck(lod):
    """Truck fuel island: a 16 x 8 m canopy (roof 6.5 m clear for trucks), four columns, two kerbed pump islands with two pumps each (generic, no brand), bollards and a price board on the fascia."""
    p = Part("fuel"); top = 6.5
    p.box(-8.0, -4.0, top - 0.7, 8.0, 4.0, top, "paint_white", skip=("bottom",)); p.box(-8.0, -4.02, top - 0.7, 8.0, -3.94, top, "sign_blue", skip=("bottom", "back"))
    seg = 12 if lod == 0 else 6
    for sx in (-6.0, 6.0):
        for sy in (-3.0, 3.0):
            p.tube((sx, sy, 0.0), (sx, sy, top - 0.7), 0.28, 0.28, seg, CON, caps=(False, False), smooth=(lod == 0))
            if lod == 0: p.box(sx - 0.4, sy - 0.4, 0.0, sx + 0.4, sy + 0.4, 0.25, CON, skip=("bottom",)); p.box(sx - 0.36, sy - 0.36, top - 0.78, sx + 0.36, sy + 0.36, top - 0.7, CON)
    for ix in (-3.5, 3.5):
        p.box(ix - 0.7, -3.0, 0.0, ix + 0.7, 3.0, 0.2, CON, skip=("bottom",))
        for iy in (-1.2, 1.2):
            if lod >= 2: continue
            p.box(ix - 0.35, iy - 0.28, 0.2, ix + 0.35, iy + 0.28, 1.5, "paint_red", skip=("bottom",))
            if lod == 0:
                p.box(ix - 0.28, iy - 0.3, 1.0, ix + 0.28, iy - 0.28, 1.4, "board_dark", skip=("bottom", "back")); p.box(ix - 0.22, iy - 0.31, 1.1, ix + 0.22, iy - 0.3, 1.3, "signal_amber", skip=("bottom", "back"))
                p.tube((ix + 0.35, iy, 1.3), (ix + 0.55, iy, 0.5), 0.03, 0.03, 6, "rubber", caps=(True, True)); p.box(ix - 0.4, iy - 0.32, 1.5, ix + 0.4, iy + 0.32, 1.58, GAL)
                p.box(ix + 0.35, iy - 0.08, 0.9, ix + 0.5, iy + 0.08, 1.2, BLK); p.tube((ix + 0.5, iy, 1.05), (ix + 0.7, iy, 0.95), 0.025, 0.02, 6, GAL)          # nozzle holster and nozzle
                p.box(ix - 0.4, iy - 0.3, 0.2, ix - 0.35, iy + 0.3, 0.5, "warning_yellow")                                                                  # guard strip
        if lod == 0:
            p.box(ix - 0.75, -3.0, 0.2, ix - 0.7, 3.0, 0.23, "warning_yellow"); p.box(ix + 0.7, -3.0, 0.2, ix + 0.75, 3.0, 0.23, "warning_yellow")
            p.box(ix - 0.3, 2.9, 0.2, ix + 0.3, 3.4, 1.0, "signal_red", skip=("bottom",)); p.box(ix - 0.25, 3.35, 0.7, ix + 0.25, 3.4, 0.95, "paint_white")        # extinguisher cabinet
    if lod <= 1:
        for lx in (-5.0, -1.7, 1.7, 5.0): p.box(lx - 0.35, -0.35, top - 0.76, lx + 0.35, 0.35, top - 0.7, "lantern_glow")
    if lod == 0:
        for bx in (-3.5, 3.5):
            for by in (-3.4, 3.4): p.tube((bx, by, 0.0), (bx, by, 0.9), 0.1, 0.1, 8, "warning_yellow", caps=(False, True)); p.tube((bx, by, 0.5), (bx, by, 0.6), 0.105, 0.105, 8, "kerb_black", caps=(False, False), smooth=False)
        p.box(-6.0, -3.97, top - 0.55, 6.0, -3.94, top - 0.05, "board_dark", skip=("bottom", "back"))
        for k in range(4): p.box(-5.5 + 3.0 * k, -3.975, top - 0.5, -3.5 + 3.0 * k, -3.945, top - 0.1, "signal_amber", skip=("bottom", "back"))               # price lines (blank panels)
        p.box(-8.0, -4.0, top - 0.8, 8.0, -3.9, top - 0.7, GAL); p.box(-8.0, 3.9, top - 0.8, 8.0, 4.0, top - 0.7, GAL)                                    # edge flashings
        for lx in (-5.0, -1.7, 1.7, 5.0): p.box(lx - 0.4, -0.4, top - 0.82, lx + 0.4, 0.4, top - 0.76, GAL)                                            # light housings
    return p


def forklift(lod):
    """Static counterbalance forklift, 1.2 m wide, 3.2 m long (forks toward -Y), 2.2 m to the top of the overhead guard. Prop only."""
    p = Part("forklift"); seg = 16 if lod == 0 else 8
    p.box(-0.55, 0.0, 0.25, 0.55, 1.5, 0.95, "warning_yellow", skip=("bottom",))                                                                          # chassis
    if lod >= 2: p.box(-0.55, -1.6, 0.0, 0.55, 1.6, 2.2, "warning_yellow"); return p
    p.box(-0.5, 1.3, 0.35, 0.5, 1.62, 1.15, BLK); p.box(-0.5, 0.2, 0.95, 0.5, 1.0, 1.05, BLK)                                                            # counterweight, floor plate
    for sx in (-0.55, 0.55):
        p.tube((sx, 0.1, 0.34), (sx + (-0.2 if sx > 0 else 0.2), 0.1, 0.34), 0.34, 0.34, seg, "rubber")
    for sx in (-0.5, 0.5): p.tube((sx - 0.08, 1.1, 0.26), (sx + 0.08, 1.1, 0.26), 0.26, 0.26, seg, "rubber")
    for sx in (-0.45, 0.45): p.box(sx - 0.04, -0.15, 1.05, sx + 0.04, -0.07, 2.2, BLK, skip=("bottom",)); p.box(sx - 0.04, 0.7, 1.05, sx + 0.04, 0.78, 2.2, BLK, skip=("bottom",))      # guard posts
    p.box(-0.5, -0.15, 2.15, 0.5, 0.78, 2.2, BLK)
    p.box(-0.3, 0.4, 1.05, 0.3, 0.9, 1.4, BLK)
    for sx in (-0.3, 0.3): p.box(sx - 0.05, -1.0, 0.1, sx + 0.05, -0.2, 1.9, GAL, skip=("bottom",))                                                            # mast
    p.box(-0.45, -1.05, 0.9, 0.45, -0.95, 1.1, GAL)
    for sx in (-0.3, 0.3): p.box(sx - 0.07, -1.6, 0.08, sx + 0.07, -0.95, 0.14, GAL, skip=("bottom",)); p.box(sx - 0.07, -1.0, 0.08, sx + 0.07, -0.95, 1.1, GAL)  # forks
    if lod == 1: return p
    for sx in (-0.55, 0.55):
        p.tube((sx, 0.1, 0.34), (sx + (0.1 if sx > 0 else -0.1), 0.1, 0.34), 0.17, 0.17, 10, GAL)                              # wheel hubs
    for sx in (-0.5, 0.5): p.tube((sx, 1.1, 0.26), (sx + (0.1 if sx > 0 else -0.1), 1.1, 0.26), 0.13, 0.13, 10, GAL)
    p.tube((0.0, 0.2, 1.05), (0.0, 0.0, 1.5), 0.02, 0.02, 6, BLK); p.tube((0.0, 0.0, 1.5), (0.0, -0.05, 1.5), 0.17, 0.17, 12, BLK, caps=(True, True), smooth=False)   # steering column and wheel
    p.box(-0.18, -0.12, 2.18, 0.18, 0.0, 2.24, "warning_yellow")                                                                                           # beacon base
    p.tube((0.0, -0.06, 2.24), (0.0, -0.06, 2.38), 0.07, 0.07, 8, "paint_orange", caps=(True, True))
    for sx in (-0.4, 0.4): p.box(sx - 0.09, -0.2, 0.85, sx + 0.09, -0.15, 0.95, "lantern_glow")                                                           # work lights
    p.box(-0.3, 0.88, 1.05, 0.3, 0.98, 1.75, BLK)                                                                                                         # seat back
    p.tube((0.3, 1.55, 0.7), (0.3, 1.75, 1.0), 0.04, 0.04, 6, GAL, caps=(False, True))                                                                     # exhaust
    for sx in (-0.15, 0.15): p.tube((sx, -0.9, 1.0), (sx, -0.3, 1.0), 0.02, 0.02, 6, BLK)                                                                  # hydraulic hoses
    for k in range(5): p.box(-0.45, -1.07, 0.2 + 0.4 * k, 0.45, -1.05, 0.24 + 0.4 * k, GAL, skip=("bottom", "back"))                                           # carriage chain links
    return p


def trailer_flatbed(lod):
    """Parked flatbed semi-trailer, no cab: 13 m long, 2.5 m wide, bed 1.3 m high; kingpin end at -Y on a landing-gear pair, three-axle bogie, headboard and side posts."""
    p = Part("flatbed"); seg = 12 if lod == 0 else 8; y0, y1 = -6.5, 6.5
    p.box(-1.25, y0, 1.25, 1.25, y1, 1.4, "container_grey", skip=("bottom",))
    for sx in (-0.5, 0.5): p.box(sx - 0.1, y0 + 0.5, 0.95, sx + 0.1, y1 - 0.2, 1.25, BLK, skip=("bottom",))                                                 # chassis beams
    if lod >= 2: p.box(-1.25, y0, 0.0, 1.25, y1, 1.4, BLK); return p
    for k, yy in enumerate((2.3, 3.6, 4.9)):                                                                                                            # bogie
        for sx in (-1.0, 1.0): p.tube((sx - 0.12, yy, 0.5), (sx + 0.12, yy, 0.5), 0.5, 0.5, seg, "rubber")
        if lod == 0: p.tube((-1.0, yy, 0.5), (1.0, yy, 0.5), 0.07, 0.07, 6, BLK)
    p.box(-1.25, y0 + 0.01, 1.4, 1.25, y0 + 0.2, 2.7, GAL, skip=("bottom",))                                                                              # headboard
    p.box(-0.4, y0 + 0.15, 0.95, 0.4, y0 + 0.9, 1.25, BLK)
    for sx in (-0.55, 0.55): p.box(sx - 0.05, y0 + 1.4, 0.0, sx + 0.05, y0 + 1.5, 1.25, GAL, skip=("bottom",))                                          # landing legs
    if lod == 0:
        for k in range(7):
            yy = y0 + 1.2 + k * 1.8
            for sx in (-1.22, 1.22): p.box(sx - 0.03, yy - 0.03, 1.4, sx + 0.03, yy + 0.03, 1.8, BLK, skip=("bottom",))                                       # stake pockets
        p.box(-1.25, y1 - 0.15, 0.9, 1.25, y1, 1.2, "warning_yellow")                                                                                       # rear bumper
        for sx in (-1.2, 1.2): p.box(sx - 0.08, y1 - 0.02, 1.3, sx + 0.08, y1, 1.37, "signal_red")
    return p


def trailer_box(lod):
    """Parked box (dry-van) semi-trailer, no cab: 13.6 m long, 2.5 m wide, 4.0 m tall overall; ribbed white-grey body, rear doors, landing legs and tandem bogie."""
    p = Part("van"); seg = 12 if lod == 0 else 8; y0, y1 = -6.5, 6.5
    p.box(-1.25, y0, 1.2, 1.25, y1, 1.35, BLK, skip=("bottom",)); p.box(-1.25, y0 + 0.05, 1.35, 1.25, y1, 4.0, "plaster_white", skip=("bottom",))
    if lod >= 2: p.box(-1.25, y0, 0.0, 1.25, y1, 4.0, "plaster_white"); return p
    for k, yy in enumerate((3.4, 4.7)):
        for sx in (-1.0, 1.0): p.tube((sx - 0.12, yy, 0.5), (sx + 0.12, yy, 0.5), 0.5, 0.5, seg, "rubber")
    p.box(-1.25, y1 - 0.1, 1.35, 1.25, y1, 3.95, SHT, skip=("bottom", "front"))
    for sx in (-0.55, 0.55): p.box(sx - 0.05, y0 + 1.4, 0.0, sx + 0.05, y0 + 1.5, 1.2, GAL, skip=("bottom",))
    p.box(-1.0, y0 + 0.15, 0.95, 1.0, y0 + 0.9, 1.2, BLK) if lod == 0 else None
    if lod == 0:
        for sx, face in ((-1.25, -1), (1.25, 1)):
            for k in range(14):
                yy = y0 + 0.5 + k * 0.9; p.box(sx - (0.04 if face < 0 else 0.0), yy, 1.5, sx + (0.0 if face < 0 else 0.04), yy + 0.1, 3.9, "plaster_white", skip=("bottom",))
        for k in range(2): p.box(-1.1 + k * 1.1, y1 - 0.14, 1.5, -1.1 + k * 1.1 + 0.05, y1, 3.8, GAL)
        for sx in (-1.2, 1.2): p.box(sx - 0.08, y1 - 0.02, 1.0, sx + 0.08, y1, 1.07, "signal_red")
        p.box(-1.25, y1 - 0.15, 0.9, 1.25, y1, 1.2, "warning_yellow")
    return p


def mast_light(lod):
    """20 m high-mast floodlight: tapered 12-sided galvanised pole on a concrete base with bolt covers, a service door, a lowering-gear box, a headframe ring and four floodlight heads angled down."""
    p = Part("mast"); seg = 12 if lod == 0 else 8
    p.box(-0.6, -0.6, 0.0, 0.6, 0.6, 0.25, CON, skip=("bottom",))
    p.tube((0, 0, 0.25), (0, 0, 19.0), 0.28, 0.12, seg, GAL, caps=(False, True), smooth=(lod == 0))
    if lod >= 2: p.box(-1.0, -1.0, 19.0, 1.0, 1.0, 19.5, GAL); return p
    p.tube((0, 0, 19.0), (0, 0, 19.2), 0.7, 0.7, 16 if lod == 0 else 8, GAL, caps=(True, True), smooth=False)
    for k in range(4):
        a = math.pi / 4 + k * math.pi / 2; dx, dy = math.cos(a), math.sin(a)
        p.tube((dx * 0.45, dy * 0.45, 19.1), (dx * 0.95, dy * 0.95, 19.1), 0.04, 0.04, 6, GAL)
        p.box(dx * 0.95 - 0.28, dy * 0.95 - 0.28, 18.82, dx * 0.95 + 0.28, dy * 0.95 + 0.28, 19.18, GAL)
        p.box(dx * 0.95 - 0.24, dy * 0.95 - 0.24, 18.78, dx * 0.95 + 0.24, dy * 0.95 + 0.24, 18.82, "lantern_glow")
    if lod == 0:
        p.box(-0.32, -0.30, 0.6, 0.32, -0.24, 1.9, BLK)                                                                                                  # service door plate
        p.tube((0.0, 0.0, 9.0), (0.0, 0.0, 9.1), 0.19, 0.19, 12, GAL)                                                                                     # pole joint collar
        for k in range(8): a = k * math.pi / 4; p.tube((math.cos(a) * 0.45, math.sin(a) * 0.45, 0.25), (math.cos(a) * 0.45, math.sin(a) * 0.45, 0.4), 0.04, 0.04, 6, GAL)    # anchor bolt covers
        p.box(0.2, -0.4, 1.0, 0.55, -0.2, 1.6, BLK); p.tube((0.0, 0.0, 1.6), (0.0, 0.0, 12.0), 0.015, 0.015, 4, BLK)                                   # lowering gear box and winch cable
        for k in range(6): a = k * math.pi / 3; p.box(math.cos(a) * 0.6 - 0.05, math.sin(a) * 0.6 - 0.05, 19.2, math.cos(a) * 0.6 + 0.05, math.sin(a) * 0.6 + 0.05, 19.45, GAL)   # lightning finial cage
        p.tube((0.0, 0.0, 19.2), (0.0, 0.0, 19.9), 0.03, 0.01, 6, GAL)
    return p


def bollard(lod):
    """One steel-and-concrete traffic bollard, 1 m high, yellow and black bands."""
    p = Part("bollard"); seg = 12 if lod == 0 else 8
    if lod >= 2: p.box(-0.14, -0.14, 0.0, 0.14, 0.14, 1.0, "warning_yellow"); return p
    p.tube((0, 0, 0.0), (0, 0, 0.12), 0.15, 0.15, seg, CON, caps=(False, True), smooth=False)
    for k, key in enumerate(("warning_yellow", "kerb_black", "warning_yellow")):
        z0 = 0.12 + 0.28 * k; p.tube((0, 0, z0), (0, 0, z0 + 0.28), 0.13, 0.13, seg, key, caps=(False, k == 2), smooth=False)
    p.tube((0, 0, 0.96), (0, 0, 1.0), 0.13, 0.11, seg, GAL, caps=(False, True), smooth=False) if lod == 0 else None
    return p


# ------------------------------------------------------------------------------------------------------------------------------------------------ catalogue extras (light heads, text faces, interaction sockets)
def _dock_heads(centres, y0, z=DOCK_H + DOOR_H + 0.7): return [(cx, y0 - 0.24, z) for cx in centres]


extra("rg_ngn_warehouse_hall_a", "A", "REGIONAL", REF, heads=_dock_heads([-12 + 6.0 * i for i in range(6)], -11.0), preset="glow_door", label={"c": (6.0, -12.1, 6.0), "width": 10.0, "height": 1.6},
      sockets=[(f"dock_{i + 1}", "truck_dock_rear", (-12 + 6.0 * i, -12.0 - 0.15, 0.0), 0) for i in range(6)], tags=["industrial", "logistics", "warehouse"])
extra("rg_ngn_warehouse_hall_b", "A", "REGIONAL", REF, heads=_dock_heads([-11.0 + 7.3 * i for i in range(4)], -9.0), preset="glow_door", label={"c": (0.0, -9.2, 6.2), "width": 14.0, "height": 1.2},
      sockets=[(f"dock_{i + 1}", "truck_dock_rear", (-11.0 + 7.3 * i, -10.0 - 0.15, 0.0), 0) for i in range(4)], tags=["industrial", "logistics", "warehouse"])
extra("rg_ngn_warehouse_cold_a", "A", "REGIONAL", REF, heads=_dock_heads([-8.0, 0.0, 8.0], -8.0, DOCK_H + 3.0 + 0.7), preset="glow_door",
      sockets=[(f"dock_{i + 1}", "truck_dock_rear", (-8.0 + 8.0 * i, -9.0 - 0.15, 0.0), 0) for i in range(3)], tags=["industrial", "logistics", "cold_store"])
extra("aro_dock_leveller_set", "A", "GLOBAL", REF, sockets=[("dock", "truck_dock_rear", (0.0, -0.85, 0.0), 0)], tags=["logistics", "tile_3_5m"])
for _v in "abc": extra(f"aro_container_20ft_{_v}", "A", "GLOBAL", REF, tags=["logistics", "container"], sockets=[("pickup", "container_lift_point", (0.0, 0.0, 2.59), 0)])
for _v in "ab": extra(f"aro_container_40ft_{_v}", "A", "GLOBAL", REF, tags=["logistics", "container"], sockets=[("pickup", "container_lift_point", (0.0, 0.0, 2.59), 0)])
extra("aro_container_stack_3", "A", "GLOBAL", REF, tags=["logistics", "container"])
extra("aro_pallet_stack_a", "A", "GLOBAL", REF, tags=["logistics", "cargo"], sockets=[("collect", "cargo_pickup", (0.0, -1.6, 0.0), 0)])
extra("aro_gate_sliding_a", "A", "GLOBAL", REF, heads=[(0.0, -1.0, 3.0)], preset="glow", label={"c": (0.0, -1.06, 2.2), "width": 1.1, "height": 0.35},
      sockets=[("lane_in", "gate_entry_lane", (-2.75, -4.0, 0.0), 0), ("lane_out", "gate_exit_lane", (2.75, -4.0, 0.0), 0), ("check", "checkpoint_stop", (-2.75, -2.8, 0.0), 0)], tags=["logistics", "gate"])
extra("aro_fence_palisade_set", "A", "GLOBAL", REF, tags=["logistics", "tile_4m"])
extra("aro_wall_plot_set", "A", "GLOBAL", REF, tags=["logistics", "tile_4m"])
extra("aro_weighbridge_a", "A", "GLOBAL", REF, heads=[(8.5, y, 3.3) for y in (-2.4,)], preset="glow", label={"c": (8.5, -2.44, 2.9), "width": 1.5, "height": 0.5, "colour": "yellow"},
      sockets=[("weigh", "truck_weigh_stop", (0.0, -1.3, 0.285), 90)], tags=["logistics", "weighbridge"])
extra("aro_fuel_island_truck", "A", "GLOBAL", REF, heads=[(lx, 0.0, 5.7) for lx in (-5.0, -1.7, 1.7, 5.0)], preset="mast", label={"c": (0.0, -4.0, 6.15), "width": 11.0, "height": 0.5, "colour": "yellow"},
      sockets=[("pump_west", "truck_fuel_stop", (-3.5, -4.5, 0.0), 0), ("pump_east", "truck_fuel_stop", (3.5, -4.5, 0.0), 0)], tags=["logistics", "fuel"], intensity=1.0, range=14, haloSize=1.6, poolRadius=7.0, realLight=False)
extra("aro_forklift_a", "A", "GLOBAL", REF, tags=["logistics", "prop"])
extra("aro_truck_trailer_parked_a", "A", "GLOBAL", REF, tags=["logistics", "trailer", "flatbed"], sockets=[("hitch", "trailer_hitch", (0.0, -6.4, 1.25), 0)])
extra("aro_truck_trailer_parked_b", "A", "GLOBAL", REF, tags=["logistics", "trailer", "box"], sockets=[("hitch", "trailer_hitch", (0.0, -6.4, 1.2), 0)])
extra("aro_yard_mast_light", "A", "GLOBAL", REF, heads=[(math.cos(math.pi / 4 + k * math.pi / 2) * 0.95, math.sin(math.pi / 4 + k * math.pi / 2) * 0.95, 18.7) for k in range(4)], preset="mast", tags=["street_lighting", "industrial", "high_mast"])
extra("aro_bollard_set", "A", "GLOBAL", REF, tags=["logistics", "prop"])
