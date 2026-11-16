"""Family K of the Kano city construction (owner directive of 2026-11-16: "Create additional assets in Blender wherever necessary, especially markets, trees, vegetation, parked vehicles, universities.
Do not keep creating models without integrating them into the actual city."). Every asset here is placed by the city route (tools/kano/make_city.py) the day it is made:
  kano_university_faculty_a   a university faculty campus on a 60 x 44 m lot: a three-storey teaching block with sun fins round a lawn quad, two two-storey wings, a portico, a gate and a
                              generic UNIVERSITY sign (no real institution, no crest), a flag, trees, staff parking (the institutional streets of D08 and the institutional quarter)
  kano_market_neighbourhood_a a neighbourhood market on a 40 x 30 m lot: three rows of open zinc sheds on posts with trading tables and produce, umbrellas, a row of lock-up shops at the back,
                              a MARKET sign at the gate (the commercial belt and the shop streets; not Kurmi market, which stays pending on references)
  aro_car_parked_set_a        three generic saloon cars parked in a row (white, blue, red; no make, no badge, no plate text), built lengthwise along the lot's width so that, placed facing a road
                              by a scatter rule, they stand parallel to it at the kerb (the city streets)
All GENERIC designs in the language of the registered street references (R-006, R-009, R-016, R-022, R-039). Real metres, front (road side) at -Y, origin on the ground at the lot centre, three LODs."""
from __future__ import annotations
from kano_kit.geo import Part
from .common import prism_x, prism_y, extra
from .city import tank
from .blocks import (S, W1, W2, CON, PAV, GAL, BLK, WIN, WHITE_C, REF_STREET, windows, band, roof_flat, compound, paving, bays, sign, sign_on_posts, flagpole, tree, columns, canopy_slab,
                     steps, shop_unit)

S_UNI = S("sign_university", (0.08, 0.20, 0.38), WHITE_C, ["UNIVERSITY", "FACULTY OF SCIENCE"], accent=(0.86, 0.70, 0.36))
S_UNI_GATE = S("sign_university_gate", (0.08, 0.20, 0.38), WHITE_C, ["UNIVERSITY"], accent=(0.86, 0.70, 0.36))
S_MARKET = S("sign_market", (0.55, 0.22, 0.08), (0.99, 0.88, 0.55), ["MARKET"], accent=(0.10, 0.45, 0.26))
S_SHOPS = [S("sign_shop_provisions", (0.10, 0.40, 0.24), WHITE_C, ["PROVISIONS"]), S("sign_shop_grains", (0.62, 0.44, 0.10), WHITE_C, ["GRAINS"]),
           S("sign_shop_fabrics", (0.42, 0.10, 0.36), WHITE_C, ["FABRICS"]), S("sign_shop_kitchen", (0.14, 0.30, 0.55), WHITE_C, ["POTS & PANS"])]


# ---------------------------------------------------------------------------------------------------------------------------------------- the university faculty
def university_faculty_a(lod):
    """A 60 x 44 m faculty campus: a three-storey teaching block (44 x 12 m) with terracotta sun fins and a columned portico at the head of a lawn quad, two two-storey wings down its sides,
    paths, trees, a flag, staff parking, a walled lot with a gate and a UNIVERSITY sign."""
    p = Part("university"); sh = 3.6
    paving(p, -30.0, -22.0, 30.0, 22.0, "laterite")
    bx0, bx1, by0, by1 = -22.0, 22.0, 6.0, 18.0                                                        # teaching block
    p.box(bx0, by0, 0.0, bx1, by1, sh * 3, W1, skip=("bottom",))
    for wx0, wx1 in ((-29.0, -21.0), (21.0, 29.0)): p.box(wx0, -8.0, 0.0, wx1, 6.0, sh * 2, W2, skip=("bottom",))   # the wings
    if lod >= 2: return p
    paving(p, -18.0, -16.0, 18.0, 3.5, "hedge_green", h=0.05)                                           # the quad lawn
    paving(p, -1.6, -21.0, 1.6, 6.0, PAV, h=0.09); paving(p, -18.0, 3.6, 18.0, 6.0, PAV, h=0.09)        # paths
    roof_flat(p, bx0, by0, bx1, by1, sh * 3, W1, lod, h=0.8)
    for wx0, wx1 in ((-29.0, -21.0), (21.0, 29.0)): roof_flat(p, wx0, -8.0, wx1, 6.0, sh * 2, W2, lod, h=0.6)
    band(p, bx0, by0, bx1, by1, 0.0, h=0.7, key="plaster_terracotta", out=0.03)
    windows(p, "-y", bx0 + 0.5, bx1 - 0.5, by0, [0, 1, 2], lod, z0=1.0, sh=sh, bay=3.2, w=1.8, h=1.6, skip={(0, 6), (0, 7)})
    windows(p, "+y", bx0 + 0.5, bx1 - 0.5, by1, [0, 1, 2], lod, z0=1.0, sh=sh, bay=3.2, w=1.8, h=1.6)
    for wx0, wx1, face, at in ((-29.0, -21.0, "+x", -21.0), (21.0, 29.0, "-x", 21.0)):
        windows(p, face, -7.5, 5.5, at, [0, 1], lod, z0=1.0, sh=sh, bay=3.0, w=1.6, h=1.5)
        band(p, wx0, -8.0, wx1, 6.0, 0.0, h=0.6, key="plaster_terracotta", out=0.03)
    # portico: columns, a canopy slab, steps and the faculty sign above the entrance
    columns(p, [(-6.0 + 3.0 * k, 3.4) for k in range(5)], 0.0, sh * 2, 0.3, key=W1, lod=lod)
    canopy_slab(p, -7.5, 2.6, 7.5, by0, sh * 2, key=W1, t=0.35); steps(p, -5.0, 5.0, 2.6, 3)
    sign(p, "-y", 0.0, by0, sh * 2 + 1.2, 16.0, 1.6, S_UNI)
    p.box(-2.0, by0 - 0.05, 0.0, 2.0, by0, 3.0, "glass_dark", skip=("back",))                           # the entrance glazing
    if lod == 0:
        for k in range(14):                                                                            # sun fins on the quad face
            x = bx0 + 1.6 + k * 3.2
            if abs(x) < 7.0: continue
            p.box(x - 0.12, by0 - 0.7, 0.9, x + 0.12, by0, sh * 3 - 0.2, "plaster_terracotta", skip=("back",))
        flagpole(p, -10.0, -12.0, 10.0, lod)
        for k, (tx, ty) in enumerate([(-14.0, -12.0), (-7.0, -6.0), (7.0, -6.0), (14.0, -12.0), (-14.0, -1.0), (14.0, -1.0)]): tree(p, tx, ty, 8.0, 3.0, lod, seed=70 + k)
        bays(p, 14.0, -20.5, 5, pitch=2.6, length=5.0)
        tank(p, 18.0, 15.0, sh * 3 + 0.8)
    sign_on_posts(p, 9.0, -20.5, 8.0, 1.5, S_UNI_GATE, z0=1.6)
    compound(p, -30.0, -22.0, 30.0, 22.0, 2.4, lod, gate_c=0.0, gate_w=8.0, key=W2, gate_key="metal_gate_blue", pier=4.0)
    return p


# ---------------------------------------------------------------------------------------------------------------------------------------- the neighbourhood market
def _shed(p: Part, x0, x1, y0, y1, h, lod, key="roof_zinc_rust"):
    """An open market shed: posts every 4 m and a shallow gable zinc roof along x."""
    yc = (y0 + y1) / 2
    if lod <= 1:
        n = max(int((x1 - x0) / 4.0), 1)
        for i in range(n + 1):
            x = x0 + (x1 - x0) * i / n
            for y in (y0 + 0.3, y1 - 0.3): p.box(x - 0.08, y - 0.08, 0.0, x + 0.08, y + 0.08, h, GAL, skip=("bottom",))
    prism_x(p, [(y0 - 0.5, h), (y1 + 0.5, h), (y1 + 0.5, h + 0.06), (yc, h + 0.9), (y0 - 0.5, h + 0.06)], x0 - 0.4, x1 + 0.4, key)


def market_neighbourhood_a(lod):
    """A 40 x 30 m neighbourhood market: three rows of open zinc sheds with trading tables, sacks, baskets and produce, umbrellas in the front yard, a row of four lock-up shops at the back,
    a MARKET sign over the gate, a low wall."""
    p = Part("market"); paving(p, -20.0, -15.0, 20.0, 15.0, CON); paving(p, -18.0, -13.0, 18.0, 9.0, "laterite", h=0.1)
    rows = [(-11.0, -6.5), (-3.0, 1.5), (5.0, 9.5)]
    for y0, y1 in rows: _shed(p, -16.0, 16.0, y0, y1, 3.0, lod, key="roof_zinc_rust" if y0 < 0 else "roof_zinc")
    p.box(-18.0, 10.5, 0.0, 18.0, 15.0, 3.6, W2, skip=("bottom",))                                       # lock-up shops at the back
    if lod >= 2: return p
    roof_flat(p, -18.0, 10.5, 18.0, 15.0, 3.6, W2, lod, h=0.4)
    for k in range(4): shop_unit(p, -18.0 + 9.0 * k + 0.5, -18.0 + 9.0 * (k + 1) - 0.5, 10.5, 0.0, 3.4, S_SHOPS[k], lod)
    if lod == 0:
        goods = ["flower_red", "leaf_green", "plaster_ochre", "burlap", "paint_orange", "leaf_green_dark"]
        for r, (y0, y1) in enumerate(rows):
            for k in range(7):
                x = -14.0 + k * 4.6; yt = (y0 + y1) / 2
                p.box(x - 1.6, yt - 0.7, 0.0, x + 1.6, yt + 0.7, 0.8, "timber_weathered", skip=("bottom",))         # trading table
                for g in range(3):
                    gx = x - 1.0 + g * 1.0; key = goods[(r * 7 + k + g) % len(goods)]
                    p.blob((gx, yt, 0.95), (0.42, 0.42, 0.18), key, subdiv=0, jitter=0.2, seed=r * 31 + k * 7 + g)
                p.box(x + 1.7, yt - 0.35, 0.0, x + 2.3, yt + 0.35, 0.75, "burlap", skip=("bottom",))                 # a sack
        for k, x in enumerate((-12.0, -4.0, 4.0, 12.0)):                                                              # umbrellas in the front yard
            p.tube((x, -13.0, 0.1), (x, -13.0, 2.5), 0.04, 0.04, 6, GAL, caps=(False, False))
            p.frustum(x - 1.4, -14.4, 2.4, x + 1.4, -11.6, 2.9, "fabric_red" if k % 2 else "fabric_white", inset=1.25, skip=("bottom",))
            p.box(x - 0.8, -13.6, 0.0, x + 0.8, -12.4, 0.75, "timber_weathered", skip=("bottom",))
        tank(p, 15.0, 13.0, 3.6 + 0.6)
    sign_on_posts(p, 0.0, -14.6, 9.0, 1.6, S_MARKET, z0=2.4)
    compound(p, -20.0, -15.0, 20.0, 15.0, 1.4, lod, gate_c=0.0, gate_w=10.0, key=W2, gate_key=BLK, back=False, pier=5.0)
    return p


# ---------------------------------------------------------------------------------------------------------------------------------------- parked cars
CAR_L, CAR_W, CAR_H = 4.4, 1.76, 1.46
CARS = [(-5.6, "paint_white"), (0.0, "paint_blue_bus"), (5.6, "paint_red")]                          # centre x, body colour (generic saloons, no make)


def _car(p: Part, cx, body, lod):
    """A generic saloon, its length along x: a body, a glazed cabin, a roof, four wheels, lamps. LOD2 is two boxes."""
    hl, hw = CAR_L / 2, CAR_W / 2
    p.box(cx - hl, -hw, 0.32, cx + hl, hw, 0.86, body, skip=("bottom",))
    if lod >= 2:
        p.box(cx - 1.2, -hw + 0.08, 0.86, cx + 0.8, hw - 0.08, CAR_H, body, skip=("bottom",)); return
    prism_y(p, [(cx - 1.35, 0.86), (cx + 1.05, 0.86), (cx + 0.45, 1.38), (cx - 0.95, 1.38)], -hw + 0.1, hw - 0.1, "glass_dark")   # the glazed cabin
    p.box(cx - 0.95, -hw + 0.12, 1.38, cx + 0.45, hw - 0.12, CAR_H, body, skip=("bottom",))                                         # its roof
    seg = 10 if lod == 0 else 6
    for wx in (cx - 1.38, cx + 1.38):
        for y0, y1 in ((-hw - 0.02, -hw + 0.22), (hw - 0.22, hw + 0.02)): p.tube((wx, y0, 0.32), (wx, y1, 0.32), 0.32, 0.32, seg, "rubber")
    if lod == 0:
        for y in (-hw + 0.3, hw - 0.3):
            p.box(cx + hl - 0.02, y - 0.22, 0.62, cx + hl + 0.02, y + 0.22, 0.74, "lantern_glow")              # head lamps
            p.box(cx - hl - 0.02, y - 0.22, 0.64, cx - hl + 0.02, y + 0.22, 0.76, "signal_red")                # tail lamps
        p.box(cx + hl - 0.03, -hw + 0.55, 0.38, cx + hl + 0.03, hw - 0.55, 0.52, "metal_galvanised")          # grille
        for y in (-hw - 0.08, hw + 0.08): p.box(cx + 0.2, y - 0.04, 0.95, cx + 0.38, y + 0.04, 1.05, body)     # mirrors


def car_parked_set_a(lod):
    """Three generic saloon cars parked in a row 1.2 m apart (white, blue, red), lengthwise along x."""
    p = Part("cars")
    for cx, body in CARS: _car(p, cx, body, lod)
    return p


# ---------------------------------------------------------------------------------------------------------------------------------------- roadside trade
S_CONT = [S("sign_container_recharge", (0.95, 0.80, 0.10), (0.10, 0.10, 0.12), ["RECHARGE CARDS"]), S("sign_container_tailor", (0.18, 0.40, 0.70), WHITE_C, ["TAILOR"]),
          S("sign_container_barber", (0.70, 0.12, 0.12), WHITE_C, ["BARBER"]), S("sign_container_provisions", (0.10, 0.45, 0.26), WHITE_C, ["PROVISIONS"])]
CONT_COLOURS = ["container_blue", "container_red", "container_green", "plaster_ochre"]


def roadside_stalls_a(lod):
    """A 16 x 7 m stretch of roadside trade: four timber stalls under zinc and tarpaulin canopies with produce, sacks and basins, a painted lock-up kiosk at one end, two umbrellas
    at the kerb side, a beaten-earth forecourt. The informal trade of every Kano shop street (generic; no brand)."""
    p = Part("stalls"); paving(p, -8.0, -3.5, 8.0, 3.5, "dry_earth", h=0.05)
    if lod >= 2:                                                                    # far away: one canopy over the row and the kiosk
        prism_x(p, [(-1.6, 2.25), (1.4, 2.65), (1.4, 2.7), (-1.6, 2.3)], -7.75, 5.75, "roof_zinc_rust"); p.box(6.0, -0.8, 0.0, 7.9, 1.6, 2.5, CONT_COLOURS[0], skip=("bottom",)); return p
    goods = ["flower_red", "leaf_green", "plaster_ochre", "paint_orange", "leaf_green_dark", "burlap"]
    for k in range(4):
        x0 = -7.6 + k * 3.3; x1 = x0 + 2.9
        if lod <= 1:
            for x in (x0 + 0.1, x1 - 0.1):
                for y in (-1.3, 1.1): p.box(x - 0.05, y - 0.05, 0.0, x + 0.05, y + 0.05, 2.3 if y < 0 else 2.6, "timber_weathered", skip=("bottom",))
        prism_x(p, [(-1.6, 2.25), (1.4, 2.65), (1.4, 2.7), (-1.6, 2.3)], x0 - 0.15, x1 + 0.15, "roof_zinc_rust" if k % 2 == 0 else "tarp_olive")
        p.box(x0 + 0.1, -1.1, 0.0, x1 - 0.1, 0.4, 0.85, "timber_weathered", skip=("bottom",))                                  # the trading table
        if lod == 0:
            for g in range(3): p.blob((x0 + 0.55 + g * 0.9, -0.35, 1.0), (0.38, 0.38, 0.17), goods[(k * 3 + g) % len(goods)], subdiv=0, jitter=0.2, seed=k * 11 + g)
            p.box(x0 + 0.2, 0.7, 0.0, x0 + 0.8, 1.3, 0.7, "burlap", skip=("bottom",))                                           # a sack behind
    p.box(6.0, -0.8, 0.0, 7.9, 1.6, 2.5, CONT_COLOURS[0], skip=("bottom",))                                                       # lock-up kiosk
    p.box(6.1, -0.83, 0.9, 7.8, -0.8, 2.0, "shutter_steel", skip=("back",)); sign(p, "-y", 6.95, -0.8, 2.05, 1.8, 0.4, S_CONT[0])
    if lod == 0:
        for k, x in enumerate((-5.0, 2.0)):
            p.tube((x, -3.0, 0.05), (x, -3.0, 2.3), 0.035, 0.035, 6, GAL, caps=(False, False))
            p.frustum(x - 1.2, -4.2, 2.2, x + 1.2, -1.8, 2.6, "fabric_red" if k else "fabric_white", inset=1.05, skip=("bottom",))
            p.box(x - 0.6, -3.5, 0.0, x + 0.6, -2.5, 0.7, "timber_weathered", skip=("bottom",))
    return p


def container_shops_a(lod):
    """A 14 x 6 m row of four converted container shops (2.4 x 3 m each, painted red, blue, green and ochre, open doors and hand-painted signs: recharge cards, tailor, barber,
    provisions) under a shared zinc awning on posts, a step and a concrete apron."""
    p = Part("containers"); paving(p, -7.0, -3.0, 7.0, 3.0, CON, h=0.08)
    if lod >= 2: p.box(-6.6, -0.6, 0.08, 6.6, 1.8, 2.7, CONT_COLOURS[1], skip=("bottom",)); return p              # far away: one block
    for k in range(4):
        x0 = -6.6 + k * 3.3; x1 = x0 + 3.0; key = CONT_COLOURS[k]
        p.box(x0, -0.6, 0.08, x1, 1.8, 2.7, key, skip=("bottom",))
        if lod <= 1:
            p.box(x0 + 0.5, -0.63, 0.2, x1 - 0.5, -0.6, 2.3, "interior_dark", skip=("back",))                                     # the open door
            sign(p, "-y", (x0 + x1) / 2, -0.6, 2.32, 2.6, 0.36, S_CONT[k])
        if lod == 0:
            for r in range(5): p.box(x0 + 0.1 + r * 0.6, 1.8, 0.2, x0 + 0.14 + r * 0.6, 1.84, 2.6, key, skip=("front",))          # corrugation ribs at the back
    prism_y(p, [(-7.0, 2.9), (7.0, 2.9), (7.0, 3.0), (-7.0, 3.0)], -2.2, -0.6, "roof_zinc")                                     # the awning
    for x in (-6.8, -2.3, 2.3, 6.8): p.box(x - 0.05, -2.15, 0.08, x + 0.05, -2.05, 2.9, GAL, skip=("bottom",))
    if lod == 0:
        p.box(-6.6, -1.0, 0.08, 6.6, -0.6, 0.25, CON, skip=("bottom",))                                                           # the step
        p.box(-1.0, -2.6, 0.08, 0.0, -1.8, 0.5, "plaster_white", skip=("bottom",)); p.box(3.0, -2.5, 0.08, 3.6, -1.9, 0.9, "paint_blue_bus", skip=("bottom",))   # a bench, a cool box
    return p


# ---------------------------------------------------------------------------------------------------------------------------------------- registration (catalogue extras: lamp heads, tags)
REF_K = REF_STREET
extra("kano_university_faculty_a", "K", "CITY_SPECIFIC", REF_K, heads=[(0.0, 2.8, 6.6), (9.0, -20.0, 2.6), (-20.0, -9.0, 3.0), (20.0, -9.0, 3.0)], preset="glow_door",
      tags=["public", "university", "education", "campus", "generic_design"], intensity=1.0, range=12, poolRadius=4.0)
extra("kano_market_neighbourhood_a", "K", "CITY_SPECIFIC", REF_K, heads=[(0.0, -14.0, 3.6), (-9.0, 10.0, 3.2), (9.0, 10.0, 3.2)], preset="glow_door",
      tags=["commercial", "market", "stalls", "generic_design"], intensity=1.0, range=12, poolRadius=4.0)
extra("aro_car_parked_set_a", "K", "GLOBAL", REF_K, tags=["vehicle", "parked_cars", "street", "generic_design"])
extra("rg_ngn_roadside_stalls_a", "K", "REGIONAL", REF_K, heads=[(6.95, -1.0, 2.4), (-3.0, -0.5, 2.2)], preset="glow", tags=["commercial", "stalls", "roadside_trade", "generic_design"], intensity=0.8, range=8)
extra("rg_ngn_container_shops_a", "K", "REGIONAL", REF_K, heads=[(-3.3, -1.5, 2.8), (3.3, -1.5, 2.8)], preset="glow", tags=["commercial", "container_shops", "roadside_trade", "generic_design"], intensity=0.9, range=8)
