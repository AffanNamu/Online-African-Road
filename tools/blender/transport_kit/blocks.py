"""Families H (commercial), I (public) and J (residential) of the owner's city construction directive of 2026-11-14: the buildings Kano was missing, built from ONE set of parametric parts so that whole families come
out of each pass instead of one building at a time. Every asset is a complete lot (building, forecourt or compound, wall and gate, parking, signs, the things a lived-in building carries) and is painted by the
surface bake (transport_kit.surface: patterns, painted signs, occlusion, weathering).
  H  commercial:  supermarket, restaurant, fast food, hotel, bank, pharmacy, phone and electronics row, car showroom, auto workshop, spare-parts row, shopping plaza, wholesale depot
  I  public:      hospital, clinic, school, police station, fire station, government offices, motor park, factory
  J  residential: low-cost row ("face-me-I-face-you"), bungalow, semi-detached pair, premium duplex, mansion with a pool
All are GENERIC designs: no real building is modelled, every sign carries generic words (SUPERMARKET, PHARMACY, HOSPITAL ...) and no brand, no emblem of a real institution (no red cross: a blue H, a green
cross). References: the owner's directive (no concept image per building; the façade language follows the registered Kano street references R-006, R-009, R-016, R-022, R-034, R-039). Real metres, front (the
road) at -Y, origin on the ground at the centre of the lot, three LODs."""
from __future__ import annotations
import math
from kano_kit.geo import Part
from .common import prism_x, prism_y, circle_pts, extra
from .city import pane, sill, aircon, downpipe, tank, dish
from .fuel import kerb_run, planter, wall_run, post_sign, tank_fill
from . import surface as SB

REF_STREET = ["R-006", "R-009", "R-016", "R-022", "R-039"]
W1, W2, W3, W4 = "plaster_white", "plaster_cream", "plaster_ochre", "plaster_terracotta"
CON, CDK, PAV, GAL, BLK, GLOW, WIN, SHOP = "concrete", "concrete_dark", "pavement_grey", "metal_galvanised", "metal_gate_black", "lantern_glow", "window_plain", "glass_shop"
WHITE_C, NAVY_C = (0.97, 0.97, 0.97), (0.075, 0.11, 0.29)


def S(key, bg, fg, lines, logo=False, accent=None, layout="auto"): return SB.sign_key(key, bg, fg, lines, logo=logo, accent=accent, layout=layout)


# ---------------------------------------------------------------------------------------------------------------------------------------- shared parts
FACES = ("-y", "+y", "-x", "+x")


def windows(p: Part, face, a0, a1, at, floors, lod, z0=0.9, sh=3.2, bay=3.2, w=1.4, h=1.5, key=WIN, sills=True, shade=False, ac=0.0, seed=0, skip=()):
    """A grid of windows on one face: one per bay of width `bay` between a0 and a1 (along the face), on each floor of `floors` (floor index, storey height `sh`, sill `z0` above the floor). LOD0 adds sills,
    concrete sunshades over the windows (`shade`) and air-conditioner units on a share `ac` of the windows."""
    n = max(1, int((a1 - a0) / bay)); off = a0 + ((a1 - a0) - n * bay) / 2 + bay / 2
    for f in floors:
        z = f * sh + z0
        for i in range(n):
            if (f, i) in skip: continue
            a = off + bay * i; pane(p, face, a, at, z, w, h, key)
            if lod: continue
            if sills: sill(p, face, a, at, z, w)
            if shade: sill(p, face, a, at, z + h + 0.32, w + 0.3, key=CON, out=0.45)
            if ac and ((i * 7 + f * 3 + seed) % 10) < ac * 10: aircon(p, face, a + w / 2 + 0.6, at, z + 0.1)


def band(p: Part, x0, y0, x1, y1, z, h=0.22, key=CON, out=0.06):
    """A string course all round a block at height z."""
    p.box(x0 - out, y0 - out, z, x1 + out, y1 + out, z + h, key, skip=())


def roof_flat(p: Part, x0, y0, x1, y1, z, wall, lod, h=0.7, deck="concrete_dark"):
    """A parapet and a roof deck (the parapet is the top of the walls; the deck sits on it)."""
    p.box(x0 - 0.05, y0 - 0.05, z - 0.02, x1 + 0.05, y1 + 0.05, z + h, wall, skip=("bottom",))
    if lod <= 1: p.box(x0 + 0.25, y0 + 0.25, z + h, x1 - 0.25, y1 - 0.25, z + h + 0.03, deck, skip=("bottom",))


def hip_roof(p: Part, x0, y0, x1, y1, z, rise, key, over=0.6):
    """A hipped roof with an overhang, its soffit underneath."""
    X0, Y0, X1, Y1 = x0 - over, y0 - over, x1 + over, y1 + over; zr = z + rise
    if X1 - X0 >= Y1 - Y0:
        r = (Y1 - Y0) / 2; cy = (Y0 + Y1) / 2; a, b = (X0 + r, cy, zr), (X1 - r, cy, zr)
        p.face_n([(X0, Y0, z), (X1, Y0, z), b, a], key, (0, -1, 1)); p.face_n([(X1, Y1, z), (X0, Y1, z), a, b], key, (0, 1, 1))
        p.face_n([(X0, Y1, z), (X0, Y0, z), a], key, (-1, 0, 1)); p.face_n([(X1, Y0, z), (X1, Y1, z), b], key, (1, 0, 1))
    else:
        r = (X1 - X0) / 2; cx = (X0 + X1) / 2; a, b = (cx, Y0 + r, zr), (cx, Y1 - r, zr)
        p.face_n([(X0, Y1, z), (X0, Y0, z), a, b], key, (-1, 0, 1)); p.face_n([(X1, Y0, z), (X1, Y1, z), b, a], key, (1, 0, 1))
        p.face_n([(X0, Y0, z), (X1, Y0, z), a], key, (0, -1, 1)); p.face_n([(X1, Y1, z), (X0, Y1, z), b], key, (0, 1, 1))
    p.face_n([(X0, Y0, z), (X1, Y0, z), (X1, Y1, z), (X0, Y1, z)], CON, (0, 0, -1))


def gable_roof_x(p: Part, x0, x1, y0, y1, z, rise, key, over=0.5):
    """A pitched roof with its ridge along X (gables at the ends), profile extruded along X."""
    prism_x(p, [(y0 - over, z - 0.12), (y1 + over, z - 0.12), (y1 + over, z), ((y0 + y1) / 2, z + rise), (y0 - over, z)], x0 - over, x1 + over, key)


def compound(p: Part, x0, y0, x1, y1, h, lod, gate_c=0.0, gate_w=4.0, key=W2, gate_key=BLK, back=True, pier=3.5):
    """A compound wall on all sides of a lot with a steel gate in the front wall (two leaves between capped piers)."""
    g0, g1 = gate_c - gate_w / 2, gate_c + gate_w / 2
    if g0 - x0 > 0.3: wall_run(p, x0, y0, g0 - 0.25, y0, h, lod, key=key, pier=pier)
    if x1 - g1 > 0.3: wall_run(p, g1 + 0.25, y0, x1, y0, h, lod, key=key, pier=pier)
    wall_run(p, x0, y0, x0, y1, h, lod, key=key, pier=pier); wall_run(p, x1, y0, x1, y1, h, lod, key=key, pier=pier)
    if back: wall_run(p, x0, y1, x1, y1, h, lod, key=key, pier=pier)
    if lod >= 2: return
    for gx in (g0 - 0.25, g1 + 0.25): p.box(gx - 0.3, y0 - 0.3, 0.0, gx + 0.3, y0 + 0.3, h + 0.5, key, skip=("bottom",))
    p.box(g0, y0 - 0.04, 0.05, gate_c - 0.02, y0 + 0.04, h - 0.1, gate_key, skip=()); p.box(gate_c + 0.02, y0 - 0.04, 0.05, g1, y0 + 0.04, h - 0.1, gate_key, skip=())
    if lod == 0:
        for gx in (g0 - 0.25, g1 + 0.25): p.box(gx - 0.36, y0 - 0.36, h + 0.5, gx + 0.36, y0 + 0.36, h + 0.62, CON, skip=()); p.box(gx - 0.12, y0 - 0.34, h + 0.1, gx + 0.12, y0 - 0.3, h + 0.35, GLOW, skip=("back",))


def paving(p: Part, x0, y0, x1, y1, key=PAV, h=0.08): p.box(x0, y0, 0.0, x1, y1, h, key, skip=("bottom",))


def bays(p: Part, x0, y, n, pitch=2.6, length=5.0, z=0.08, dirn=1):
    """Painted parking-bay lines: n bays starting at x0, the lines running from y towards +Y (dirn 1) or -Y."""
    for k in range(n + 1):
        x = x0 + pitch * k; p.box(x - 0.06, min(y, y + dirn * length), z, x + 0.06, max(y, y + dirn * length), z + 0.006, "road_white", skip=("bottom",))


def sign(p: Part, face, a, at, z0, w, h, key, proud=0.12):
    """A painted sign board standing `proud` off a wall (the painted face outward, its edges plain)."""
    if face == "-y": p.box(a - w / 2, at - proud, z0, a + w / 2, at, z0 + h, key, skip=("back",))
    elif face == "+y": p.box(a - w / 2, at, z0, a + w / 2, at + proud, z0 + h, key, skip=("front",))
    elif face == "-x": p.box(at - proud, a - w / 2, z0, at, a + w / 2, z0 + h, key, skip=("right",))
    else: p.box(at, a - w / 2, z0, at + proud, a + w / 2, z0 + h, key, skip=("left",))


def sign_on_posts(p: Part, x, y, w, h, key, z0=1.2, post=GAL):
    """A free-standing board on two posts, painted on both faces (school, police, clinic gate boards)."""
    for sx in (-1, 1): p.tube((x + sx * (w / 2 - 0.15), y, 0.0), (x + sx * (w / 2 - 0.15), y, z0 + h - 0.05), 0.06, 0.06, 6, post, caps=(False, True))       # the posts end inside the board
    p.box(x - w / 2, y - 0.08, z0, x + w / 2, y - 0.02, z0 + h, key, skip=("back",)); p.box(x - w / 2, y - 0.02, z0, x + w / 2, y + 0.04, z0 + h, key, skip=("front",))


def flagpole(p: Part, x, y, h=8.0, lod=0):
    p.tube((x, y, 0.0), (x, y, h), 0.06, 0.04, 6, GAL, caps=(False, True))
    if lod == 0:
        for k, key in enumerate(("paint_green_dark", "paint_white", "paint_green_dark")): p.box(x + 0.06 + 0.5 * k, y - 0.01, h - 1.0, x + 0.06 + 0.5 * (k + 1), y + 0.01, h - 0.1, key, skip=())


def tree(p: Part, x, y, h, r, lod, seed=0, key="leaf_green"):
    """A shade tree: a trunk and a crown (an icosphere, coarser on LOD1)."""
    if lod >= 2: return
    p.tube((x, y, 0.0), (x, y, h * 0.55), 0.16, 0.12, 6, "timber_weathered", caps=(False, False))
    p.blob((x, y, h * 0.55 + r * 0.7), (r, r, r * 0.75), key, subdiv=1 if lod == 0 else 0, jitter=0.15, seed=seed)


def palm(p: Part, x, y, h, lod, seed=0):
    if lod >= 2: return
    p.tube((x, y, 0.0), (x, y, h), 0.14, 0.11, 6, "timber_weathered", caps=(False, False))
    p.blob((x, y, h + 0.2), (1.8, 1.8, 0.55), "leaf_green_dark", subdiv=0 if lod else 1, jitter=0.3, seed=seed)


def balcony(p: Part, face, a, at, z, w, d, lod, rail=BLK, slab=CON):
    """A balcony slab with a rail (a solid lower panel and a top bar)."""
    s = -1 if face in ("-y", "-x") else 1
    if face in ("-y", "+y"):
        y0, y1 = sorted((at, at + s * d)); p.box(a - w / 2, y0, z - 0.18, a + w / 2, y1, z, slab, skip=())
        if lod: return
        yf = at + s * d; p.box(a - w / 2, min(yf, yf - s * 0.05), z, a + w / 2, max(yf, yf - s * 0.05), z + 1.0, rail, skip=("bottom",))
    else:
        x0, x1 = sorted((at, at + s * d)); p.box(x0, a - w / 2, z - 0.18, x1, a + w / 2, z, slab, skip=())
        if lod: return
        xf = at + s * d; p.box(min(xf, xf - s * 0.05), a - w / 2, z, max(xf, xf - s * 0.05), a + w / 2, z + 1.0, rail, skip=("bottom",))


def columns(p: Part, pts, z0, z1, r, key=W1, lod=0):
    for (x, y) in pts:
        if lod: p.box(x - r, y - r, z0, x + r, y + r, z1, key, skip=("bottom", "top"))
        else: p.tube((x, y, z0), (x, y, z1), r, r, 10, key, caps=(False, False))


def canopy_slab(p: Part, x0, y0, x1, y1, z, key=W1, t=0.3, edge=None):
    p.box(x0, y0, z, x1, y1, z + t, key, skip=())
    if edge: p.box(x0 - 0.03, y0 - 0.03, z + t, x1 + 0.03, y1 + 0.03, z + t + 0.12, edge, skip=())


def steps(p: Part, x0, x1, y_front, n, rise=0.16, tread=0.32, key=CON):
    """Steps up to a raised floor at the front of a building (towards -Y)."""
    for k in range(n): p.box(x0, y_front - tread * (n - k), 0.0, x1, y_front, rise * (k + 1), key, skip=("bottom",))


def shop_unit(p: Part, x0, x1, y, z0, h, sign_key, lod, shutter_up=0.65):
    """One shop bay in a row (front at y, facing -Y): a half-raised roller shutter over a lit window, a sign board over it."""
    w = x1 - x0
    if lod >= 1: p.box(x0 + 0.15, y - 0.03, z0, x1 - 0.15, y, z0 + h * 0.8, SHOP, skip=("back",)); return
    zs = z0 + h * 0.8 * shutter_up
    p.box(x0 + 0.15, y - 0.04, z0, x1 - 0.15, y, zs, SHOP, skip=("back",)); p.box(x0 + 0.15, y - 0.06, zs, x1 - 0.15, y - 0.04, z0 + h * 0.8, "shutter_steel", skip=("back",))
    p.box(x0 + 0.1, y - 0.2, z0 + h * 0.8, x1 - 0.1, y - 0.06, z0 + h * 0.8 + 0.25, GAL, skip=("back",))                                                                   # shutter box
    sign(p, "-y", (x0 + x1) / 2, y, z0 + h * 0.8 + 0.3, w - 0.3, min(0.9, h * 0.25), sign_key)


# ---------------------------------------------------------------------------------------------------------------------------------------- H: commercial
S_SUPER = S("sign_supermarket", (0.10, 0.45, 0.26), WHITE_C, ["SUPERMARKET"], accent=(0.95, 0.70, 0.10))
S_REST = S("sign_restaurant", (0.45, 0.12, 0.10), (0.98, 0.86, 0.55), ["RESTAURANT"])
S_FAST = S("sign_fast_food", (0.80, 0.12, 0.10), (0.99, 0.84, 0.20), ["FAST FOOD"], accent=(0.99, 0.84, 0.20))
S_MENU = S("sign_menu_board", (0.10, 0.10, 0.12), (0.99, 0.84, 0.20), ["MENU", "CHICKEN  RICE", "SUYA  CHIPS", "DRINKS"])
S_HOTEL = S("sign_hotel", (0.12, 0.12, 0.14), (0.86, 0.70, 0.36), ["HOTEL"])
S_HOTELV = S("sign_hotel_vertical", (0.12, 0.12, 0.14), (0.86, 0.70, 0.36), ["H", "O", "T", "E", "L"], layout="stack")
S_BANK = S("sign_bank", (0.10, 0.20, 0.40), WHITE_C, ["BANK"], accent=(0.86, 0.70, 0.36))
S_PHARM = S("sign_pharmacy", (0.10, 0.50, 0.28), WHITE_C, ["PHARMACY"])
S_CROSS = S("sign_green_cross", WHITE_C, (0.10, 0.55, 0.28), ["+"])
S_SHOWROOM = S("sign_car_showroom", (0.15, 0.16, 0.18), WHITE_C, ["CAR SHOWROOM"], accent=(0.80, 0.12, 0.10))
S_REPAIR = S("sign_auto_repairs", (0.93, 0.92, 0.86), (0.62, 0.10, 0.08), ["AUTO REPAIRS", "MECHANIC  VULCANISER"])
S_PLAZA = S("sign_shopping_plaza", (0.42, 0.10, 0.36), WHITE_C, ["SHOPPING PLAZA"], accent=(0.95, 0.70, 0.10))
S_WHOLESALE = S("sign_wholesale", (0.14, 0.30, 0.55), WHITE_C, ["WHOLESALE DEPOT"])
ROW_PHONE = [S("sign_shop_phones", (0.10, 0.36, 0.62), WHITE_C, ["PHONES"]), S("sign_shop_electronics", (0.80, 0.30, 0.10), WHITE_C, ["ELECTRONICS"]),
             S("sign_shop_accessories", (0.18, 0.18, 0.20), (0.99, 0.84, 0.20), ["ACCESSORIES"]), S("sign_shop_repairs", (0.12, 0.45, 0.30), WHITE_C, ["PHONE REPAIRS"]),
             S("sign_shop_laptops", (0.55, 0.12, 0.40), WHITE_C, ["LAPTOPS"])]
ROW_PARTS = [S("sign_shop_spare_parts", (0.62, 0.10, 0.08), WHITE_C, ["SPARE PARTS"]), S("sign_shop_tyres", (0.15, 0.15, 0.16), (0.99, 0.84, 0.20), ["TYRES"]),
             S("sign_shop_batteries", (0.10, 0.36, 0.62), WHITE_C, ["BATTERIES"]), S("sign_shop_engine_oil", (0.80, 0.55, 0.10), (0.10, 0.10, 0.12), ["ENGINE OIL"]),
             S("sign_shop_auto_electrical", (0.12, 0.45, 0.30), WHITE_C, ["AUTO ELECTRICAL"])]
ROW_PLAZA = [S("sign_shop_fashion", (0.70, 0.20, 0.40), WHITE_C, ["FASHION"]), S("sign_shop_shoes", (0.18, 0.18, 0.20), WHITE_C, ["SHOES & BAGS"]),
             S("sign_shop_bakery", (0.85, 0.62, 0.30), (0.30, 0.15, 0.08), ["BAKERY"]), S("sign_shop_cafe", (0.30, 0.18, 0.10), WHITE_C, ["CAFE"]),
             S("sign_shop_salon", (0.55, 0.12, 0.40), WHITE_C, ["SALON"]), S("sign_shop_books", (0.10, 0.36, 0.62), WHITE_C, ["BOOKS"]),
             S("sign_shop_fabrics", (0.12, 0.45, 0.30), WHITE_C, ["FABRICS"]), S("sign_shop_mart", (0.80, 0.30, 0.10), WHITE_C, ["MINI MART"])]


def supermarket_a(lod):
    """A 44 x 36 m supermarket: a 34 x 17 m hall with a glazed front and a portal entrance, a big sign band, a parking forecourt with 2 x 11 bays, a trolley shelter, a totem, a walled service yard."""
    p = Part("supermarket"); x0, x1, y0, y1, h = -17.0, 17.0, -1.0, 16.0, 6.6
    paving(p, -22.0, -18.0, 22.0, -1.0, PAV); paving(p, -22.0, -1.0, -17.0, 18.0, CON); paving(p, 17.0, -1.0, 22.0, 18.0, CON)
    p.box(x0, y0, 0.0, x1, y1, h, "paint_grey_light", skip=("bottom", "front"))
    if lod >= 2: p.box(x0, y0 - 0.02, 0.0, x1, y0, h, S_SUPER, skip=("back", "bottom")); return p
    p.box(x0, y0, 3.6, x1, y0 + 0.25, h, "paint_green_dark", skip=("bottom",)); p.box(x0 + 0.4, y0 + 0.2, 0.0, x1 - 0.4, y0 + 0.25, 3.6, SHOP, skip=("back", "bottom"))
    sign(p, "-y", -4.0, y0, 4.0, 16.0, 2.2, S_SUPER, proud=0.2)
    p.box(4.0, y0 - 3.0, 3.8, 10.0, y0, 4.2, "paint_green_dark", skip=())                                                                                          # entrance canopy
    roof_flat(p, x0, y0, x1, y1, h, "paint_grey_light", lod, h=0.6)
    if lod == 0:
        for x in (4.2, 9.8): p.tube((x, y0 - 2.8, 0.08), (x, y0 - 2.8, 3.8), 0.12, 0.12, 8, GAL, caps=(False, False))
        p.box(5.0, y0 + 0.15, 0.0, 9.0, y0 + 0.2, 2.6, "glass", skip=("back", "bottom"))
        for k in range(4): p.box(-12.0 + 7.0 * k, y1 - 3.0, h + 0.6, -10.6 + 7.0 * k, y1 - 1.6, h + 1.6, "paint_white", skip=("bottom",))                         # roof plant
        bays(p, -20.5, -17.6, 11, dirn=1); bays(p, -20.5, -7.5, 11, dirn=1)
        p.box(13.0, -8.0, 2.3, 18.0, -6.0, 2.45, "roof_zinc", skip=())
        for x in (13.2, 17.8): p.tube((x, -7.0, 0.08), (x, -7.0, 2.3), 0.05, 0.05, 6, GAL, caps=(False, False))                                                   # trolley shelter
        post_sign(p, 20.0, -17.0, S_SUPER, w=3.0, h=1.0, z=3.0)
        windows(p, "-x", y0 + 2.0, y1 - 1.0, x0, [0], lod, z0=2.4, bay=4.0, w=1.6, h=1.0, key=WIN, sills=False)
        p.box(x1 - 6.0, y1, 0.0, x1 - 2.0, y1 + 0.05, 4.0, "shutter_steel", skip=("front", "bottom"))
    planter(p, -21.8, -2.6, -6.0, -1.2, lod, seed=7, lights=3)
    wall_run(p, -22.0, 17.9, 22.0, 17.9, 2.4, lod); wall_run(p, -21.9, -1.0, -21.9, 17.9, 2.4, lod); wall_run(p, 21.9, -1.0, 21.9, 17.9, 2.4, lod)
    return p


def restaurant_a(lod):
    """A 24 x 22 m restaurant: a single-storey dining hall with big windows, a pergola terrace with tables, a sign, hedges and a walled back."""
    p = Part("restaurant"); x0, x1, y0, y1, h = -8.0, 8.0, -2.0, 9.0, 4.6
    paving(p, -12.0, -11.0, 12.0, -2.0, "paver_red"); paving(p, -12.0, -2.0, 12.0, 11.0, CON)
    p.box(x0, y0, 0.0, x1, y1, h, W2, skip=("bottom",))
    if lod >= 2: return p
    windows(p, "-y", x0 + 1.0, x1 - 1.0, y0, [0], lod, z0=0.5, bay=3.0, w=2.4, h=2.4, key=WIN, sills=False)
    sign(p, "-y", 0.0, y0, 3.3, 8.0, 1.0, S_REST); roof_flat(p, x0, y0, x1, y1, h, W2, lod, h=0.5, deck="roof_tile_red")
    p.box(-1.0, y0 - 0.05, 0.0, 1.0, y0, 2.4, "wood_door", skip=("back",))
    canopy_slab(p, x0, -9.0, x1, y0, 3.0, key="timber_light", t=0.15)                                                                                               # pergola deck
    if lod == 0:
        for x in (x0 + 0.2, -2.7, 2.7, x1 - 0.2): p.box(x - 0.12, -8.8, 0.08, x + 0.12, -8.56, 3.0, "timber_weathered", skip=("bottom",))
        for k in range(6):
            tx = -6.0 + 2.4 * k; ty = -5.5 if k % 2 else -7.0; p.tube((tx, ty, 0.08), (tx, ty, 0.75), 0.05, 0.05, 6, BLK, caps=(False, False)); p.prism(circle_pts(0.5, 10, tx, ty), 0.75, 0.8, "paint_white")
            for cx in (-0.75, 0.75): p.box(tx + cx - 0.2, ty - 0.2, 0.08, tx + cx + 0.2, ty + 0.2, 0.45, "paint_red", skip=("bottom",))
        planter(p, -11.8, -10.8, -9.0, -2.5, lod, seed=8, lights=2); planter(p, 9.0, -10.8, 11.8, -2.5, lod, seed=9, lights=2)
        tank(p, 5.0, 6.0, h + 0.5); aircon(p, "+x", 4.0, x1, 2.0); aircon(p, "+x", 6.5, x1, 2.0)
    wall_run(p, -12.0, 10.9, 12.0, 10.9, 2.2, lod)
    return p


def fastfood_a(lod):
    """A 26 x 24 m fast-food outlet: a red-and-yellow box with big windows, a drive-through lane with a menu board and a window canopy, parking, a roof sign."""
    p = Part("fastfood"); x0, x1, y0, y1, h = -9.0, 5.0, -2.0, 8.0, 5.0
    paving(p, -13.0, -12.0, 13.0, 12.0, CON)
    p.box(x0, y0, 0.0, x1, y1, h, "paint_red", skip=("bottom",))
    if lod >= 2: return p
    p.box(x0 - 0.05, y0 - 0.05, h - 1.2, x1 + 0.05, y1 + 0.05, h, "paint_brand_yellow", skip=("bottom",))
    windows(p, "-y", x0 + 0.5, x1 - 0.5, y0, [0], lod, z0=0.4, bay=2.8, w=2.4, h=2.6, key=SHOP, sills=False); windows(p, "-x", y0 + 0.5, y1 - 0.5, x0, [0], lod, z0=0.4, bay=2.8, w=2.4, h=2.6, key=SHOP, sills=False)
    sign(p, "-y", (x0 + x1) / 2, y0 - 0.05, h - 1.1, 9.0, 1.0, S_FAST)
    roof_flat(p, x0, y0, x1, y1, h, "paint_brand_yellow", lod, h=0.4)
    canopy_slab(p, x1, 1.0, x1 + 3.5, 5.0, 3.2, key="paint_red", t=0.25)                                                                                           # drive-through window canopy
    if lod == 0:
        p.box(x1 - 0.05, 2.2, 1.0, x1, 3.8, 2.2, "glass", skip=("left",)); p.tube((x1 + 3.3, 1.2, 0.0), (x1 + 3.3, 1.2, 3.2), 0.1, 0.1, 8, GAL, caps=(False, False)); p.tube((x1 + 3.3, 4.8, 0.0), (x1 + 3.3, 4.8, 3.2), 0.1, 0.1, 8, GAL, caps=(False, False))
        p.box(x1 + 4.0, 7.5, 0.0, x1 + 4.3, 9.5, 0.4, CON, skip=("bottom",)); p.box(x1 + 4.05, 7.6, 0.4, x1 + 4.25, 9.4, 2.2, S_MENU, skip=("bottom",))                     # menu board
        kerb_run(p, x1 + 1.0, -10.0, x1 + 1.0, 10.0, h=0.15, w=0.3, seg=2.0)
        bays(p, -12.5, -11.6, 6, dirn=1); bays(p, -12.5, 9.5, 6, length=2.4, dirn=1)
        for k in range(3): p.box(x0 + 2.0 + 3.5 * k, y1 - 2.5, h + 0.4, x0 + 3.0 + 3.5 * k, y1 - 1.5, h + 1.1, "paint_white", skip=("bottom",))
        p.box(x0 + 2.0, (y0 + y1) / 2 - 0.3, h + 0.4, x1 - 2.0, (y0 + y1) / 2, h + 1.8, "paint_red", skip=("bottom",)); p.box(x0 + 2.2, (y0 + y1) / 2 - 0.33, h + 0.5, x1 - 2.2, (y0 + y1) / 2 - 0.3, h + 1.7, S_FAST, skip=("back",))
    planter(p, -12.8, -11.8, -6.0, -10.4, lod, seed=10, lights=2)
    return p


def hotel_a(lod):
    """A 36 x 32 m hotel: an eight-storey slab above a lobby, balconies, a porte-cochère with columns and a sign, a vertical HOTEL sign on the corner, flags, a drop-off loop, palms."""
    p = Part("hotel"); x0, x1, y0, y1 = -13.0, 13.0, -2.0, 12.0; lob, sh, nf = 5.0, 3.2, 8; top = lob + sh * nf
    paving(p, -18.0, -16.0, 18.0, -2.0, PAV); paving(p, -18.0, -2.0, 18.0, 16.0, CON)
    p.box(x0, y0, 0.0, x1, y1, lob, "stone_ivory", skip=("bottom",)); p.box(x0 + 0.4, y0 + 0.4, lob, x1 - 0.4, y1 - 0.4, top, W1, skip=("bottom",))
    if lod >= 2: return p
    p.box(x0 + 1.0, y0 - 0.05, 0.0, x1 - 1.0, y0, lob - 0.6, "glass", skip=("back", "bottom"))
    floors = list(range(nf))
    windows(p, "-y", x0 + 0.6, x1 - 0.6, y0 + 0.4, [f for f in floors], lod, z0=lob + 0.6, sh=sh, bay=3.25, w=1.8, h=1.9, key=WIN, sills=False, ac=0.3)
    windows(p, "+y", x0 + 0.6, x1 - 0.6, y1 - 0.4, floors, lod, z0=lob + 0.6, sh=sh, bay=3.25, w=1.8, h=1.9, key=WIN, sills=False, ac=0.3)
    windows(p, "-x", y0 + 0.6, y1 - 0.6, x0 + 0.4, floors, lod, z0=lob + 0.6, sh=sh, bay=3.4, w=1.4, h=1.6, key=WIN, sills=True)
    windows(p, "+x", y0 + 0.6, y1 - 0.6, x1 - 0.4, floors, lod, z0=lob + 0.6, sh=sh, bay=3.4, w=1.4, h=1.6, key=WIN, sills=True)
    if lod == 0:
        n = 8; off = x0 + 0.6 + ((x1 - x0 - 1.2) - n * 3.25) / 2 + 3.25 / 2
        for f in range(1, nf):
            for i in range(0, n, 2): balcony(p, "-y", off + 3.25 * i, y0 + 0.4, lob + sh * f, 2.4, 1.0, lod)
        for f in range(nf + 1): band(p, x0 + 0.4, y0 + 0.4, x1 - 0.4, y1 - 0.4, lob + sh * f - 0.12, h=0.12, key=CON, out=0.04)
    roof_flat(p, x0 + 0.4, y0 + 0.4, x1 - 0.4, y1 - 0.4, top, W1, lod, h=0.9)
    p.box(-4.0, 3.0, top + 0.9, 4.0, 8.0, top + 3.6, W1, skip=("bottom",))                                                                                        # lift and stair head
    sign(p, "-y", 0.0, 3.0, top + 1.4, 7.0, 1.8, S_HOTEL); sign(p, "-y", 0.0, y0, lob - 0.55, 8.0, 0.5, S_HOTEL, proud=0.05) if lod == 0 else None
    canopy_slab(p, -6.0, -9.0, 6.0, y0, lob - 0.7, key="stone_ivory", t=0.5, edge="trim_gold")                                                                   # porte-cochère
    if lod == 0:
        columns(p, [(-5.4, -8.4), (5.4, -8.4)], 0.08, lob - 0.7, 0.32, key="stone_ivory")
        p.box(x1 - 0.2, y0 - 1.2, lob + 2.0, x1 + 0.2, y0 - 0.1, lob + 2.0 + 9.0, "sign_hotel_vertical", skip=())
        for k in range(3): flagpole(p, -15.0 + 1.6 * k, -14.5, 9.0, lod)
        for x in (-16.0, 16.0): palm(p, x, -4.0, 7.0, lod, seed=int(x)); palm(p, x, 8.0, 7.0, lod, seed=int(x) + 3)
        planter(p, -6.0, -14.0, 6.0, -12.0, lod, seed=11, lights=4)
        dish(p, 8.0, 9.0, top + 0.9)
    return p


def bank_a(lod):
    """A 28 x 24 m bank: three storeys with a glazed front on columns, steps and a ramp, a BANK sign, an ATM gallery on the side, a security post, parking and a flag."""
    p = Part("bank"); x0, x1, y0, y1, sh = -10.0, 10.0, -3.0, 10.0, 3.6; top = sh * 3
    paving(p, -14.0, -12.0, 14.0, -3.0, PAV); paving(p, -14.0, -3.0, 14.0, 12.0, CON)
    p.box(x0, y0 + 1.5, 0.0, x1, y1, top, "stone_ivory", skip=("bottom",))
    if lod >= 2: return p
    p.box(x0 + 0.5, y0 + 1.45, sh, x1 - 0.5, y0 + 1.5, top - 0.3, "glass", skip=("back", "bottom"))                                                              # curtain wall, floors 1 and 2
    p.box(x0 + 2.0, y0 + 1.45, 0.4, x1 - 2.0, y0 + 1.5, sh - 0.3, "glass", skip=("back", "bottom"))
    canopy_slab(p, x0, y0 - 0.5, x1, y0 + 1.5, sh, key="stone_ivory", t=0.45)
    sign(p, "-y", 0.0, y0 - 0.5, sh + 0.05, 8.0, 0.38, S_BANK, proud=0.04); sign(p, "-y", 0.0, y0 + 1.45, top - 1.4, 7.0, 1.1, S_BANK)
    roof_flat(p, x0, y0 + 1.5, x1, y1, top, "stone_ivory", lod, h=0.8)
    windows(p, "-x", y0 + 2.5, y1 - 1.0, x0, [0, 1, 2], lod, z0=0.9, sh=sh, bay=3.0, w=1.4, h=2.0, sills=False, shade=True)
    windows(p, "+x", y0 + 2.5, y1 - 1.0, x1, [1, 2], lod, z0=0.9, sh=sh, bay=3.0, w=1.4, h=2.0, sills=False, shade=True)
    if lod == 0:
        columns(p, [(x, y0 - 0.2) for x in (-8.5, -3.0, 3.0, 8.5)], 0.32, sh, 0.3, key="stone_ivory")
        steps(p, -6.0, 6.0, y0 - 0.4, 2)
        p.box(x1, 1.0, 0.0, x1 + 0.6, 6.0, 2.8, "stone_ivory", skip=("bottom",))
        for k in range(2): p.box(x1 + 0.6, 2.0 + 2.0 * k, 0.9, x1 + 0.65, 3.2 + 2.0 * k, 2.0, "glass_dark", skip=("left",))                                       # ATMs
        p.box(x1 + 0.6, 1.5, 2.2, x1 + 0.7, 5.5, 2.7, "sign_atm", skip=("left",))
        p.box(11.0, -11.0, 0.08, 13.0, -9.0, 2.6, "paint_white", skip=("bottom",)); p.box(10.8, -11.2, 2.6, 13.2, -8.8, 2.8, CON, skip=()); p.box(10.97, -10.6, 1.0, 11.0, -9.4, 2.0, "glass", skip=("right",))   # security post
        flagpole(p, -12.0, -10.5, 8.0, lod); bays(p, -13.5, -11.8, 8, dirn=1)
        for k in range(3): aircon(p, "+y", -6.0 + 6.0 * k, y1, 4.0)
    return p


def pharmacy_a(lod):
    """A 14 x 14 m pharmacy: a two-storey shop-house, a lit shopfront with a PHARMACY sign, a green-cross blade sign, the owner's flat above, a roof tank."""
    p = Part("pharmacy"); x0, x1, y0, y1 = -6.0, 6.0, -3.0, 6.0
    paving(p, -7.0, -7.0, 7.0, -3.0, "tile_floor")
    p.box(x0, y0, 0.0, x1, y1, 6.6, W1, skip=("bottom",))
    if lod >= 2: return p
    p.box(x0 + 0.5, y0 - 0.05, 0.0, x1 - 0.5, y0, 2.8, SHOP, skip=("back", "bottom")); sign(p, "-y", 0.0, y0, 2.9, 9.0, 0.8, S_PHARM)
    windows(p, "-y", x0 + 0.5, x1 - 0.5, y0, [1], lod, z0=0.9, sh=3.3, bay=3.6, w=1.5, h=1.4, ac=0.5); windows(p, "-x", y0 + 1.0, y1 - 1.0, x0, [0, 1], lod, z0=0.9, sh=3.3, bay=3.0)
    roof_flat(p, x0, y0, x1, y1, 6.6, W1, lod, h=0.6)
    if lod == 0:
        p.box(x1 - 0.15, y0 - 1.2, 3.8, x1 + 0.15, y0 - 0.2, 4.8, S_CROSS, skip=())                                                                                  # green-cross blade sign
        canopy_slab(p, x0, y0 - 1.5, x1, y0, 3.7, key=CON, t=0.15); tank(p, 3.5, 3.5, 7.2); downpipe(p, x1 - 0.1, y1 - 0.1, 0.0, 6.6)
        for x in (x0 + 0.3, x1 - 0.3): p.tube((x, y0 - 1.3, 0.0), (x, y0 - 1.3, 3.7), 0.06, 0.06, 6, GAL, caps=(False, False))
    return p


def _row(p: Part, n, w, d, h, signs, lod, wall=W2, veranda=True, roof="roof_zinc"):
    """A row of n shop units of width w (front at y = -d/2), single storey, a veranda roof on posts, a parapet with a zinc roof behind."""
    x0 = -n * w / 2; y0 = -d / 2; y1 = d / 2
    p.box(x0, y0, 0.0, -x0, y1, h, wall, skip=("bottom",))
    if lod >= 2: return
    for k in range(n): shop_unit(p, x0 + w * k, x0 + w * (k + 1), y0, 0.0, h - 0.5, signs[k % len(signs)], lod, shutter_up=0.55 + 0.1 * (k % 3))
    gable_roof_x(p, x0, -x0, y0 + 0.4, y1, h, 1.0, roof, over=0.3)
    if veranda:
        p.box(x0 - 0.2, y0 - 2.2, h - 0.6, -x0 + 0.2, y0, h - 0.5, roof, skip=())
        if lod == 0:
            for k in range(n + 1): x = x0 + w * k; p.tube((x, y0 - 2.0, 0.08), (x, y0 - 2.0, h - 0.6), 0.06, 0.06, 6, GAL, caps=(False, False))
    p.box(x0, y0 - 2.4, 0.0, -x0, y0, 0.15, CON, skip=("bottom",))


def phone_row_a(lod):
    """A 22 x 12 m row of five phone and electronics shops: half-raised shutters over lit windows, five painted signs, a zinc veranda on posts, a raised walkway."""
    p = Part("row"); _row(p, 5, 4.0, 8.0, 4.2, ROW_PHONE, lod)
    if lod == 0:
        for k in range(3): p.box(-7.5 + 6.0 * k, -5.6, 0.15, -6.7 + 6.0 * k, -5.0, 1.0, "paint_white", skip=("bottom",))                                          # display cases
        p.box(5.3, 4.4, 0.0, 6.7, 5.8, 1.8, "timber_weathered", skip=("bottom",)); tank(p, 6.0, 5.1, 1.8, r=0.65, h=1.3)                                              # tank on a stand behind the row
    return p


def parts_row_a(lod):
    """A 24 x 12 m row of five spare-parts shops with tyres stacked in front, the same row type in the colours of the trade."""
    p = Part("row"); _row(p, 5, 4.4, 8.0, 4.2, ROW_PARTS, lod, wall=W3, roof="roof_zinc_rust")
    if lod == 0:
        for k in range(4):
            tx = -9.0 + 5.5 * k
            for j in range(4): p.tube((tx, -5.6, 0.15 + 0.24 * j), (tx, -5.6, 0.37 + 0.24 * j), 0.36, 0.36, 10, "rubber", caps=(False, True))
        for k in range(3): p.tube((5.0 + 0.7 * k, -5.4, 0.15), (5.0 + 0.7 * k, -5.4, 1.0), 0.28, 0.28, 10, "paint_blue_bus" if k % 2 else "paint_red", caps=(False, True))
    return p


def showroom_a(lod):
    """A 36 x 30 m car showroom: a tall glass box with a portal and a sign, a display forecourt with painted bays, feather-flag poles, a workshop with shutters behind."""
    p = Part("showroom"); x0, x1, y0, y1, h = -11.0, 11.0, -1.0, 11.0, 7.5
    paving(p, -18.0, -15.0, 18.0, -1.0, "tile_floor"); paving(p, -18.0, -1.0, 18.0, 15.0, CON)
    p.box(x0, y0 + 0.2, 0.0, x1, y1, h, "paint_grey_light", skip=("bottom", "front"))
    if lod >= 2: p.box(x0, y0 + 0.15, 0.0, x1, y0 + 0.2, h, "glass", skip=("back", "bottom")); return p
    p.box(x0 + 0.2, y0 + 0.15, 0.0, x1 - 0.2, y0 + 0.2, h - 1.6, "glass", skip=("back", "bottom")); p.box(x0, y0, h - 1.6, x1, y0 + 0.2, h, "paint_grey_light", skip=("bottom",))
    p.box(-4.0, y0 - 1.0, 0.0, 4.0, y0, h + 1.2, "paint_grey_light", skip=("bottom", "back")); p.box(-3.0, y0 - 1.02, 0.0, 3.0, y0 - 1.0, h - 2.2, "glass", skip=("back", "bottom"))   # portal
    sign(p, "-y", 0.0, y0 - 1.0, h - 1.9, 7.6, 1.5, S_SHOWROOM, proud=0.1); roof_flat(p, x0, y0, x1, y1, h, "paint_grey_light", lod, h=0.5)
    p.box(x0, y1, 0.0, x1, 15.0, 5.0, "sheet_cream", skip=("bottom",))                                                                                             # workshop
    if lod == 0:
        p.box(x1, y1 + 0.5, 0.0, x1 + 0.05, 14.5, 4.0, "shutter_steel", skip=("left",))
        for r in range(2):
            for k in range(6): p.box(-16.5 + 5.6 * k, -14.5 + 6.5 * r, 0.08, -16.38 + 5.6 * k, -9.5 + 6.5 * r, 0.086, "road_white", skip=("bottom",))
        for k in range(5): x = -17.0 + 8.5 * k; p.tube((x, -14.8, 0.0), (x, -14.8, 5.0), 0.04, 0.03, 6, GAL, caps=(False, True)); p.box(x + 0.04, -14.81, 2.8, x + 0.75, -14.79, 4.8, "paint_red" if k % 2 else "paint_brand_yellow", skip=())
    return p


def workshop_a(lod):
    """A 22 x 18 m auto workshop: an open steel shed with three bays, a block office with a painted sign, tyres, drums, an oil-stained apron and a back wall."""
    p = Part("workshop")
    paving(p, -11.0, -9.0, 11.0, 9.0, CON)
    p.box(-8.0, 0.0, 4.8, 8.0, 8.0, 5.0, "roof_zinc_rust", skip=())
    if lod >= 2: p.box(-11.0, 1.0, 0.0, -8.0, 8.0, 3.2, W2, skip=("bottom",)); return p
    gable_roof_x(p, -8.0, 8.0, 0.0, 8.0, 4.8, 0.8, "roof_zinc_rust", over=0.3)
    for x in (-8.0, -2.7, 2.7, 8.0):
        for y in (0.2, 7.8): p.box(x - 0.12, y - 0.12, 0.08, x + 0.12, y + 0.12, 4.8, BLK, skip=("bottom", "top"))
    p.box(-8.0, 7.8, 0.0, 8.0, 8.0, 4.8, "sheet_cream", skip=("bottom",))
    p.box(-11.0, 1.0, 0.0, -8.2, 8.0, 3.2, W2, skip=("bottom",)); sign(p, "-y", 0.0, 0.0, 5.0, 10.0, 1.2, S_REPAIR, proud=0.06)
    if lod == 0:
        p.box(-10.6, 0.97, 1.0, -9.0, 1.0, 2.2, WIN, skip=("back",)); p.box(-11.05, 0.95, 3.2, -8.15, 8.05, 3.35, CON, skip=())
        for k in range(3):
            for j in range(5): p.tube((7.0 + 0.8 * (k % 2), -6.0 + 0.9 * k, 0.08 + 0.24 * j), (7.0 + 0.8 * (k % 2), -6.0 + 0.9 * k, 0.3 + 0.24 * j), 0.36, 0.36, 10, "rubber", caps=(False, True))
        for k in range(3): p.tube((-6.0 + 0.7 * k, 6.8, 0.08), (-6.0 + 0.7 * k, 6.8, 0.95), 0.28, 0.28, 10, "paint_blue_bus" if k % 2 else "paint_red", caps=(False, True))
        for k in range(3): p.prism(circle_pts(1.2 + 0.3 * k, 8, -4.0 + 4.5 * k, -3.0 + 1.5 * (k % 2)), 0.08, 0.085, CDK)                  # oil stains
        p.box(-1.5, 3.0, 0.08, 1.5, 5.0, 0.5, "paint_red", skip=("bottom",))                                                                                         # a car lift base
    wall_run(p, -11.0, 8.9, 11.0, 8.9, 2.2, lod, key="plaster_weathered")
    return p


def plaza_a(lod):
    """A 42 x 26 m two-storey shopping plaza: eight lit shop units on the ground floor and eight on the upper floor, set back behind a gallery walkway with a rail, a big sign, parking in front."""
    p = Part("plaza"); x0, x1, y0, y1, sh = -20.0, 20.0, 0.0, 12.0, 4.2; g = 2.2
    paving(p, -21.0, -13.0, 21.0, 0.0, PAV); p.box(x0, y0, 0.0, x1, y1, sh, W1, skip=("bottom",)); p.box(x0, y0 + g, sh, x1, y1, sh * 2, W1, skip=("bottom",))     # ground floor; upper floor behind the gallery
    if lod >= 2: return p
    n = 8; w = (x1 - x0) / n
    for f, yf in ((0, y0), (1, y0 + g)):
        for k in range(n): shop_unit(p, x0 + w * k, x0 + w * (k + 1), yf, sh * f, sh - 0.4, ROW_PLAZA[(k + 3 * f) % len(ROW_PLAZA)], lod)
    canopy_slab(p, x0, y0 - g, x1, y0 + 0.4, sh - 0.25, key=CON, t=0.25)                                                                                            # gallery floor, the canopy of the ground floor
    if lod == 0:
        p.box(x0, y0 - g - 0.05, sh, x1, y0 - g + 0.05, sh + 1.05, "railing_green", skip=("bottom",))
        for k in range(9): x = x0 + 5.0 * k; p.box(x - 0.18, y0 - g + 0.1, 0.0, x + 0.18, y0 - g + 0.45, sh - 0.25, W1, skip=("bottom", "top"))
        bays(p, -20.5, -12.6, 15, pitch=2.7, dirn=1)
    p.box(-8.0, y0 + g, sh * 2, 8.0, y0 + g + 0.4, sh * 2 + 2.2, "paint_maroon", skip=("bottom",)); p.box(-7.6, y0 + g - 0.03, sh * 2 + 0.25, 7.6, y0 + g, sh * 2 + 2.0, S_PLAZA, skip=("back",))
    roof_flat(p, x0, y0 + g, x1, y1, sh * 2, W1, lod, h=0.6)
    if lod == 0:
        for k in range(4): tank(p, -15.0 + 9.0 * k, 9.0, sh * 2 + 0.6, r=0.8, h=1.5)
    return p


def wholesale_a(lod):
    """A 32 x 24 m wholesale depot: a clad hall with three roller shutters onto a loading apron, an office corner, a sign band, pallets of goods, a wall."""
    p = Part("depot"); x0, x1, y0, y1, h = -14.0, 14.0, 0.0, 11.0, 7.0
    paving(p, -16.0, -12.0, 16.0, 12.0, CON); p.box(x0, y0, 0.0, x1, y1, h, "sheet_cream", skip=("bottom",))
    if lod >= 2: return p
    gable_roof_x(p, x0, x1, y0, y1, h, 1.4, "roof_zinc", over=0.3)
    for k in range(3): p.box(-11.0 + 7.5 * k, y0 - 0.05, 0.0, -6.5 + 7.5 * k, y0, 4.5, "shutter_steel", skip=("back", "bottom"))
    p.box(10.0, y0 - 0.05, 1.0, 13.0, y0, 2.6, WIN, skip=("back",)); sign(p, "-y", -1.0, y0, 5.0, 16.0, 1.6, S_WHOLESALE, proud=0.08)
    if lod == 0:
        p.box(x0, y0 - 3.0, 4.8, x1, y0, 5.0, "roof_zinc", skip=())
        for x in (x0 + 0.2, 0.0, x1 - 0.2): p.tube((x, y0 - 2.8, 0.08), (x, y0 - 2.8, 4.8), 0.1, 0.1, 8, GAL, caps=(False, False))
        for k in range(5): p.box(-12.0 + 2.2 * k, -5.0, 0.08, -10.9 + 2.2 * k, -4.0, 0.22, "timber_weathered", skip=("bottom",)); p.box(-11.95 + 2.2 * k, -4.95, 0.22, -10.95 + 2.2 * k, -4.05, 1.3, "burlap" if k % 2 else "fabric_white", skip=("bottom",))
    compound(p, -16.0, -12.0, 16.0, 12.0, 2.4, lod, gate_c=0.0, gate_w=8.0, gate_key="metal_gate_blue")
    return p


# ---------------------------------------------------------------------------------------------------------------------------------------- I: public
S_HOSP = S("sign_hospital", WHITE_C, (0.10, 0.28, 0.62), ["HOSPITAL"])
S_H = S("sign_blue_h", (0.10, 0.28, 0.62), WHITE_C, ["H"])
S_EMERG = S("sign_emergency", (0.70, 0.10, 0.08), WHITE_C, ["EMERGENCY"])
S_CLINIC = S("sign_clinic", (0.10, 0.50, 0.28), WHITE_C, ["CLINIC", "HEALTH CENTRE"])
S_SCHOOL = S("sign_school", (0.10, 0.36, 0.62), WHITE_C, ["COMMUNITY SCHOOL", "KNOWLEDGE IS LIGHT"], accent=(0.95, 0.70, 0.10))
S_POLICE = S("sign_police", (0.06, 0.12, 0.30), WHITE_C, ["POLICE STATION"])
S_FIRE = S("sign_fire_station", (0.70, 0.10, 0.08), WHITE_C, ["FIRE STATION"])
S_GOV = S("sign_government", (0.10, 0.30, 0.20), WHITE_C, ["GOVERNMENT OFFICES"], accent=(0.86, 0.70, 0.36))
S_PARK = S("sign_motor_park", (0.10, 0.36, 0.62), WHITE_C, ["MOTOR PARK"], accent=(0.95, 0.70, 0.10))
S_ROUTES = S("sign_park_routes", (0.10, 0.10, 0.12), (0.99, 0.84, 0.20), ["KANO - ZARIA", "KANO - KADUNA", "KANO - ABUJA"])
S_TICKETS = S("sign_tickets", (0.12, 0.45, 0.30), WHITE_C, ["TICKETS"])
S_FACTORY = S("sign_factory", (0.18, 0.18, 0.20), WHITE_C, ["FACTORY", "NO ENTRY WITHOUT PERMIT"])


def hospital_a(lod):
    """A 48 x 38 m general hospital: a four-storey main block with sunshades, an entrance canopy and an emergency bay, a blue H on the roof, a two-storey rear wing, a walled compound with a gate, trees."""
    p = Part("hospital"); x0, x1, y0, y1, sh = -20.0, 20.0, -4.0, 9.0, 3.6; top = sh * 4
    paving(p, -24.0, -19.0, 24.0, 19.0, CON)
    p.box(x0, y0, 0.0, x1, y1, top, W1, skip=("bottom",)); p.box(-8.0, y1, 0.0, 8.0, 17.0, sh * 2, W1, skip=("bottom",))
    if lod >= 2: return p
    windows(p, "-y", x0 + 0.5, x1 - 0.5, y0, [0, 1, 2, 3], lod, z0=1.0, sh=sh, bay=3.3, w=1.6, h=1.6, shade=True, skip={(0, 5), (0, 6)}, ac=0.25)
    windows(p, "+y", x0 + 0.5, -8.5, y1, [0, 1, 2, 3], lod, z0=1.0, sh=sh, bay=3.3, w=1.6, h=1.6, shade=True); windows(p, "+y", 8.5, x1 - 0.5, y1, [0, 1, 2, 3], lod, z0=1.0, sh=sh, bay=3.3, w=1.6, h=1.6, shade=True)
    windows(p, "-x", y0 + 0.5, y1 - 0.5, x0, [0, 1, 2, 3], lod, z0=1.0, sh=sh, bay=3.0, w=1.4, h=1.6); windows(p, "+x", y0 + 0.5, y1 - 0.5, x1, [0, 1, 2, 3], lod, z0=1.0, sh=sh, bay=3.0, w=1.4, h=1.6)
    windows(p, "-x", y1 + 0.5, 16.5, -8.0, [0, 1], lod, z0=1.0, sh=sh, bay=3.0); windows(p, "+x", y1 + 0.5, 16.5, 8.0, [0, 1], lod, z0=1.0, sh=sh, bay=3.0)
    for f in range(1, 4): band(p, x0, y0, x1, y1, sh * f - 0.1, h=0.18, key="paint_sky", out=0.05)
    canopy_slab(p, -5.0, y0 - 6.0, 5.0, y0, 3.6, key=W1, t=0.35, edge="paint_sky"); p.box(-3.0, y0 - 0.05, 0.0, 3.0, y0, 2.8, "glass", skip=("back", "bottom"))
    sign(p, "-y", 0.0, y0 - 6.0, 3.95, 8.0, 0.9, S_HOSP, proud=0.05); sign(p, "-y", 0.0, y0, top - 1.8, 9.0, 1.4, S_HOSP)
    roof_flat(p, x0, y0, x1, y1, top, W1, lod, h=0.8); roof_flat(p, -8.0, y1, 8.0, 17.0, sh * 2, W1, lod, h=0.6)
    p.box(12.0, 2.0, top + 0.8, 16.0, 2.3, top + 4.8, S_H, skip=("bottom",))
    if lod == 0:
        columns(p, [(-4.6, y0 - 5.6), (4.6, y0 - 5.6)], 0.08, 3.6, 0.25)
        p.box(12.0, y0 - 4.0, 3.0, 19.0, y0, 3.25, W1, skip=()); sign(p, "-y", 15.5, y0 - 4.0, 3.3, 6.0, 0.8, S_EMERG, proud=0.05)                                 # emergency bay canopy
        for x in (12.3, 18.7): p.tube((x, y0 - 3.8, 0.08), (x, y0 - 3.8, 3.0), 0.12, 0.12, 8, GAL, caps=(False, False))
        bays(p, -23.0, -18.5, 7, pitch=2.8, dirn=1)
        for k in range(4): tree(p, -22.0 + 3.0 * k if k < 2 else 16.0 + 3.0 * (k - 2), 14.0, 7.0, 2.4, lod, seed=20 + k)
        for k in range(5): p.box(-14.0 + 6.0 * k, 5.0, top + 0.8, -12.8 + 6.0 * k, 6.2, top + 1.8, "paint_white", skip=("bottom",))
        flagpole(p, -21.0, -17.0, 9.0, lod)
    compound(p, -24.0, -19.0, 24.0, 19.0, 2.4, lod, gate_c=0.0, gate_w=8.0, key=W1, gate_key="metal_gate_blue")
    return p


def clinic_a(lod):
    """A 24 x 20 m clinic and health centre: a single-storey building with a deep veranda on columns, a hip roof, a CLINIC board on posts at the gate, a walled yard, a generator."""
    p = Part("clinic"); x0, x1, y0, y1, h = -9.0, 9.0, -1.0, 7.0, 3.6
    paving(p, -12.0, -10.0, 12.0, 10.0, CON); p.box(x0, y0, 0.0, x1, y1, h, W1, skip=("bottom",))
    if lod >= 2: return p
    hip_roof(p, x0, y0 - 2.5, x1, y1, h, 1.8, "roof_zinc", over=0.5)
    windows(p, "-y", x0 + 0.5, x1 - 0.5, y0, [0], lod, z0=0.9, bay=3.0, w=1.3, h=1.4, skip={(0, 2), (0, 3)}); p.box(-1.0, y0 - 0.05, 0.0, 1.0, y0, 2.4, "wood_door", skip=("back",))
    windows(p, "-x", y0 + 0.5, y1 - 0.5, x0, [0], lod, z0=0.9, bay=2.8); windows(p, "+x", y0 + 0.5, y1 - 0.5, x1, [0], lod, z0=0.9, bay=2.8)
    p.box(x0, y0 - 2.5, 0.0, x1, y0, 0.3, CON, skip=("bottom",)); sign(p, "-y", 0.0, y0, 2.6, 6.0, 0.8, S_CLINIC, proud=0.05)
    if lod == 0:
        columns(p, [(x, y0 - 2.2) for x in (-8.6, -4.3, 0.0, 4.3, 8.6)], 0.3, h, 0.18); band(p, x0, y0, x1, y1, 0.0, h=0.4, key="paint_sky", out=0.03)
        sign_on_posts(p, -6.0, -9.2, 3.2, 1.2, S_CLINIC); p.box(5.0, 7.5, 0.08, 7.0, 9.0, 1.4, "paint_green_dark", skip=("bottom",)); tank(p, -7.0, 8.5, 0.08, r=0.9, h=2.0)
        tree(p, 9.5, -6.0, 6.0, 2.2, lod, seed=31)
    compound(p, -12.0, -10.0, 12.0, 10.0, 2.0, lod, gate_c=2.0, gate_w=4.5, key=W1, gate_key="metal_gate_blue")
    return p


def school_a(lod):
    """A 50 x 36 m community school: a two-storey classroom block with open veranda corridors and rails on both floors, a stair at the end, a hipped zinc roof, a gate board, a flag, a laterite playground, trees."""
    p = Part("school"); x0, x1, y0, y1, sh = -20.0, 20.0, 6.0, 14.0, 3.4
    paving(p, -25.0, -18.0, 25.0, 18.0, "laterite"); paving(p, x0 - 1.0, y0 - 3.0, x1 + 3.0, y1 + 1.0, CON, h=0.1)
    p.box(x0, y0, 0.0, x1, y1, sh * 2, W2, skip=("bottom",))
    if lod >= 2: return p
    hip_roof(p, x0, y0 - 3.0, x1, y1, sh * 2, 2.2, "roof_zinc", over=0.6)
    for f in range(2):
        z = sh * f; p.box(x0, y0 - 3.0, z + (0.0 if f == 0 else -0.3), x1, y0, z + (0.12 if f == 0 else 0.0), CON, skip=() if f else ("bottom",))                    # corridor floors
        n = 8; w = (x1 - x0) / n
        for k in range(n):
            a = x0 + w * (k + 0.5); pane(p, "-y", a - 1.6, y0, z + 0.3, 1.0, 2.2, "wood_door"); pane(p, "-y", a + 0.6, y0, z + 1.0, 2.4, 1.5, WIN)
        if lod == 0:
            columns(p, [(x0 + 5.0 * k, y0 - 2.8) for k in range(9)], z, z + sh - (0.3 if f == 0 else 0.0), 0.15, key=W2)
            if f == 1: p.box(x0, y0 - 3.0, z, x1, y0 - 2.9, z + 1.0, "railing_green", skip=("bottom",))
    windows(p, "+y", x0 + 0.5, x1 - 0.5, y1, [0, 1], lod, z0=1.0, sh=sh, bay=2.5, w=1.8, h=1.4)
    p.box(x1, y0 - 3.0, 0.0, x1 + 3.0, y0 + 2.0, sh * 2 + 0.2, W2, skip=("bottom",))                                                                             # stair tower
    band(p, x0, y0, x1, y1, 0.0, h=0.6, key="paint_sky", out=0.03)
    if lod == 0:
        sign_on_posts(p, 0.0, -16.0, 6.0, 1.4, S_SCHOOL, z0=1.5); flagpole(p, -6.0, -6.0, 9.0, lod)
        for k in range(3): tree(p, -22.0, -12.0 + 9.0 * k, 7.5, 2.8, lod, seed=40 + k)
        for k in range(2): tree(p, 22.0, -12.0 + 9.0 * k, 7.0, 2.6, lod, seed=50 + k)
    compound(p, -25.0, -18.0, 25.0, 18.0, 2.0, lod, gate_c=0.0, gate_w=5.0, key=W2, gate_key="metal_gate_blue", pier=4.0)
    return p


def police_a(lod):
    """A 28 x 24 m police station: a two-storey block in white and blue, a POLICE STATION sign, a gate with a barrier and a guard post, a flag, a walled yard."""
    p = Part("police"); x0, x1, y0, y1, sh = -10.0, 10.0, 0.0, 9.0, 3.4
    paving(p, -14.0, -12.0, 14.0, 12.0, CON); p.box(x0, y0, 0.0, x1, y1, sh * 2, W1, skip=("bottom",))
    if lod >= 2: return p
    band(p, x0, y0, x1, y1, 0.0, h=1.2, key="paint_sky", out=0.02); band(p, x0, y0, x1, y1, sh - 0.1, h=0.25, key="paint_navy", out=0.05)
    windows(p, "-y", x0 + 0.5, x1 - 0.5, y0, [0, 1], lod, z0=1.0, sh=sh, bay=3.2, w=1.4, h=1.5, skip={(0, 2), (0, 3)}); windows(p, "-x", y0 + 0.5, y1 - 0.5, x0, [0, 1], lod, z0=1.0, sh=sh, bay=3.0)
    windows(p, "+x", y0 + 0.5, y1 - 0.5, x1, [0, 1], lod, z0=1.0, sh=sh, bay=3.0); windows(p, "+y", x0 + 0.5, x1 - 0.5, y1, [0, 1], lod, z0=1.0, sh=sh, bay=3.2)
    p.box(-1.5, y0 - 0.05, 0.0, 1.5, y0, 2.6, "metal_gate_blue", skip=("back",)); sign(p, "-y", 0.0, y0, sh * 2 - 1.5, 10.0, 1.1, S_POLICE)
    roof_flat(p, x0, y0, x1, y1, sh * 2, W1, lod, h=0.6)
    if lod == 0:
        canopy_slab(p, -3.0, y0 - 2.0, 3.0, y0, 3.0, key=W1, t=0.2); steps(p, -2.0, 2.0, y0, 2)
        p.box(8.0, -11.5, 0.08, 10.0, -9.5, 2.6, W1, skip=("bottom",)); p.box(7.8, -11.7, 2.6, 10.2, -9.3, 2.8, CON, skip=()); p.box(7.97, -11.0, 1.0, 8.0, -10.0, 2.0, "glass", skip=("right",))
        p.box(2.5, -12.1, 0.9, 7.6, -11.95, 1.05, "paint_red", skip=()); flagpole(p, -8.0, -9.0, 8.0, lod); sign_on_posts(p, -8.0, -11.6, 3.6, 1.0, S_POLICE, z0=1.4)
        tank(p, 6.0, 6.0, sh * 2 + 0.6)
    compound(p, -14.0, -12.0, 14.0, 12.0, 2.4, lod, gate_c=5.0, gate_w=5.0, key=W1, gate_key="metal_gate_blue")
    return p



def fire_a(lod):
    """A 30 x 24 m fire station: three engine bays with red doors on an apron, a two-storey office, a slim hose-drying tower, a FIRE STATION sign."""
    p = Part("fire"); x0, x1, y0, y1 = -11.0, 11.0, 0.0, 12.0
    paving(p, -15.0, -12.0, 15.0, 12.0, CON); p.box(x0, y0, 0.0, 4.0, y1, 6.0, W1, skip=("bottom",)); p.box(4.0, y0, 0.0, x1, y1, 7.2, W1, skip=("bottom",))
    if lod >= 2: p.box(-14.0, 6.0, 0.0, -11.0, 9.0, 14.0, W1, skip=("bottom",)); return p
    for k in range(3): p.box(x0 + 0.8 + 4.8 * k, y0 - 0.05, 0.0, x0 + 4.6 + 4.8 * k, y0, 4.6, "paint_red", skip=("back", "bottom"))
    sign(p, "-y", -3.5, y0, 4.9, 11.0, 0.9, S_FIRE); windows(p, "-y", 4.5, x1 - 0.5, y0, [0, 1], lod, z0=1.0, sh=3.6, bay=2.6, w=1.4, h=1.5)
    windows(p, "+x", y0 + 0.5, y1 - 0.5, x1, [0, 1], lod, z0=1.0, sh=3.6, bay=3.0); roof_flat(p, x0, y0, 4.0, y1, 6.0, W1, lod, h=0.5); roof_flat(p, 4.0, y0, x1, y1, 7.2, W1, lod, h=0.5)
    p.box(-14.0, 6.0, 0.0, -11.0, 9.0, 14.0, W1, skip=("bottom",))                                                                                                 # hose tower
    if lod == 0:
        band(p, x0, y0, x1, y1, 5.0, h=0.3, key="paint_red", out=0.04)
        for k in range(4): pane(p, "-x", 7.5, -14.0, 2.5 + 3.0 * k, 1.0, 1.2, WIN)
        p.box(-14.2, 5.8, 14.0, -10.8, 9.2, 14.3, CON, skip=())
        for k in range(3): p.box(x0 + 1.0 + 4.8 * k, -11.5, 0.08, x0 + 1.15 + 4.8 * k, y0, 0.086, "road_yellow", skip=("bottom",))
        p.box(x0 - 0.05, -11.9, 0.08, x1 + 0.05, -11.75, 0.086, "road_yellow", skip=("bottom",)); flagpole(p, 13.0, -10.0, 8.0, lod)
    wall_run(p, -15.0, 11.9, 15.0, 11.9, 2.2, lod, key=W1)
    return p


def government_a(lod):
    """A 48 x 34 m government office: a three-storey block with a columned portico and broad steps, a sign, three flags, a walled compound with a gate and a guard post, lawns and trees, parking."""
    p = Part("gov"); x0, x1, y0, y1, sh = -20.0, 20.0, -2.0, 12.0, 3.8; top = sh * 3
    paving(p, -24.0, -17.0, 24.0, 17.0, CON); p.box(x0, y0, 0.0, x1, y1, top, "stone_ivory", skip=("bottom",))
    if lod >= 2: return p
    windows(p, "-y", x0 + 0.5, x1 - 0.5, y0, [0, 1, 2], lod, z0=1.0, sh=sh, bay=3.3, w=1.4, h=2.0, skip={(f, i) for f in range(3) for i in (5, 6)}, ac=0.3)
    windows(p, "+y", x0 + 0.5, x1 - 0.5, y1, [0, 1, 2], lod, z0=1.0, sh=sh, bay=3.3, w=1.4, h=2.0); windows(p, "-x", y0 + 0.5, y1 - 0.5, x0, [0, 1, 2], lod, z0=1.0, sh=sh, bay=3.2, w=1.4, h=2.0)
    windows(p, "+x", y0 + 0.5, y1 - 0.5, x1, [0, 1, 2], lod, z0=1.0, sh=sh, bay=3.2, w=1.4, h=2.0)
    p.box(-6.0, y0 - 4.0, top - 1.0, 6.0, y0, top + 0.2, "stone_ivory", skip=())                                                                                  # portico roof
    prism_y(p, [(-6.2, top + 0.2), (6.2, top + 0.2), (0.0, top + 1.8)], y0 - 4.1, y0 - 3.6, "stone_ivory")                                                          # pediment
    sign(p, "-y", 0.0, y0 - 4.0, top - 0.95, 10.0, 0.8, S_GOV, proud=0.05); p.box(-2.0, y0 - 0.05, 0.0, 2.0, y0, 3.2, "wood_door", skip=("back",))
    roof_flat(p, x0, y0, x1, y1, top, "stone_ivory", lod, h=0.8)
    if lod == 0:
        columns(p, [(x, y0 - 3.5) for x in (-5.4, -3.2, -1.0, 1.0, 3.2, 5.4)], 0.48, top - 1.0, 0.35, key="stone_ivory"); steps(p, -6.5, 6.5, y0 - 0.0, 3, tread=1.3)
        for k in range(3): flagpole(p, -3.0 + 3.0 * k, -12.0, 10.0, lod)
        for k in range(4): tree(p, -21.0 + 6.0 * k if k < 2 else 9.0 + 6.0 * (k - 2), -12.0, 7.0, 2.6, lod, seed=60 + k)
        bays(p, 8.0, -16.5, 5, dirn=1); bays(p, -22.0, -16.5, 5, dirn=1)
        p.box(17.0, -16.4, 0.08, 19.0, -14.4, 2.6, W1, skip=("bottom",)); p.box(16.8, -16.6, 2.6, 19.2, -14.2, 2.8, CON, skip=())
        for k in range(1, 4): band(p, x0, y0, x1, y1, sh * k - 0.1, h=0.2, key=CON, out=0.04)
    compound(p, -24.0, -17.0, 24.0, 17.0, 2.4, lod, gate_c=12.0, gate_w=6.0, key="stone_ivory")
    return p


def motor_park_a(lod):
    """A 46 x 36 m motor park: a laterite yard behind a gate arch with a sign, two long shelters with benches, a ticket office with route boards, bus and taxi bays, a wall."""
    p = Part("park")
    paving(p, -23.0, -18.0, 23.0, 18.0, "laterite", h=0.05); paving(p, -23.0, -18.0, 23.0, -14.0, CON)
    for y in (-2.0, 8.0): p.box(-18.0, y, 3.4, 10.0, y + 4.0, 3.55, "roof_zinc", skip=())
    if lod >= 2: p.box(13.0, 4.0, 0.0, 21.0, 12.0, 3.6, W2, skip=("bottom",)); return p
    for y in (-2.0, 8.0):
        if lod == 0:
            for k in range(8): x = -17.6 + 3.9 * k; p.tube((x, y + 2.0, 0.05), (x, y + 2.0, 3.4), 0.08, 0.08, 6, GAL, caps=(False, False))
            for k in range(7): p.box(-17.0 + 3.9 * k, y + 1.4, 0.05, -15.0 + 3.9 * k, y + 1.8, 0.48, "timber_weathered", skip=("bottom",))
        else:
            for k in range(3): x = -17.6 + 13.6 * k; p.box(x - 0.08, y + 1.92, 0.05, x + 0.08, y + 2.08, 3.4, GAL, skip=("bottom", "top"))
    p.box(13.0, 4.0, 0.0, 21.0, 12.0, 3.6, W2, skip=("bottom",)); hip_roof(p, 13.0, 4.0, 21.0, 12.0, 3.6, 1.4, "roof_zinc_rust", over=0.5)
    p.box(14.0, 3.95, 1.0, 17.0, 4.0, 2.2, WIN, skip=("back",)); sign(p, "-y", 17.0, 4.0, 2.4, 5.0, 0.8, S_TICKETS, proud=0.05)
    for x in (-9.0, 9.0): p.box(x - 0.5, -17.0, 0.0, x + 0.5, -16.0, 6.0, W2, skip=("bottom",))                                                                     # gate arch
    p.box(-9.5, -17.1, 6.0, 9.5, -15.9, 7.6, W2, skip=()); sign(p, "-y", 0.0, -17.1, 6.1, 14.0, 1.4, S_PARK, proud=0.05)
    if lod == 0:
        sign_on_posts(p, 17.0, 2.5, 4.0, 1.6, S_ROUTES, z0=0.9)
        for k in range(6): p.box(-20.0 + 6.0 * k, -12.0, 0.05, -19.85 + 6.0 * k, -5.0, 0.056, "road_white", skip=("bottom",))
        for k in range(2): tree(p, 20.0, -10.0 + 7.0 * k, 7.0, 2.6, lod, seed=70 + k)
    wall_run(p, -23.0, 17.9, 23.0, 17.9, 2.2, lod, key="plaster_weathered"); wall_run(p, -23.0, -15.0, -23.0, 17.9, 2.2, lod, key="plaster_weathered"); wall_run(p, 23.0, -15.0, 23.0, 17.9, 2.2, lod, key="plaster_weathered")
    return p


def factory_a(lod):
    """A 46 x 34 m factory: a sawtooth-roofed production hall, a three-storey office front, two silos and a chimney, a loading dock with shutters, a fenced yard with a gate."""
    p = Part("factory"); x0, x1, y0, y1, h = -20.0, 20.0, 2.0, 16.0, 7.0
    paving(p, -23.0, -17.0, 23.0, 17.0, CON); p.box(x0, y0, 0.0, x1, y1, h, "sheet_cream", skip=("bottom",))
    if lod >= 2: p.box(-20.0, -4.0, 0.0, -6.0, 2.0, 10.0, W1, skip=("bottom",)); return p
    for k in range(5):                                                                                                                                              # sawtooth roof (north lights towards +Y)
        a = y0 + 2.8 * k; prism_x(p, [(a, h), (a + 2.8, h), (a + 2.8, h + 2.0), (a + 2.6, h + 2.0)], x0, x1, "roof_zinc")
        if lod == 0: p.box(x0 + 0.05, a + 2.78, h + 0.2, x1 - 0.05, a + 2.8, h + 1.9, "glass", skip=("front",))
    p.box(-20.0, -4.0, 0.0, -6.0, 2.0, 10.0, W1, skip=("bottom",)); windows(p, "-y", -19.5, -6.5, -4.0, [0, 1, 2], lod, z0=1.0, sh=3.3, bay=2.6, w=1.8, h=1.6)
    roof_flat(p, -20.0, -4.0, -6.0, 2.0, 10.0, W1, lod, h=0.6); sign(p, "-y", -13.0, -4.0, 10.7, 10.0, 1.5, S_FACTORY)
    for k in range(3): p.box(-2.0 + 7.0 * k, y0 - 0.05, 0.0, 2.5 + 7.0 * k, y0, 4.5, "shutter_steel", skip=("back", "bottom"))
    p.box(-4.0, y0 - 3.0, 0.0, x1, y0, 1.2, CON, skip=("bottom",))                                                                                                 # loading dock
    for k in range(2): p.tube((-17.0 + 3.0 * k, -12.0, 0.0), (-17.0 + 3.0 * k, -12.0, 11.0), 1.3, 1.3, 12, GAL, caps=(False, True))                               # silos
    p.tube((19.0, 14.0, 0.0), (19.0, 14.0, 20.0), 0.7, 0.45, 10, CDK, caps=(False, True))                                                # chimney
    if lod == 0:
        for k in range(5): p.box(-3.5 + 4.0 * k, y0 - 2.9, 1.2, -2.4 + 4.0 * k, y0 - 2.0, 2.4, "burlap" if k % 2 else "timber_weathered", skip=("bottom",))
        p.box(-21.5, -15.0, 0.08, -19.0, -12.5, 2.6, W1, skip=("bottom",))
    compound(p, -23.0, -17.0, 23.0, 17.0, 2.6, lod, gate_c=4.0, gate_w=9.0, key=CON, gate_key="metal_gate_blue", pier=5.0)
    return p


# ---------------------------------------------------------------------------------------------------------------------------------------- J: residential
def lowcost_row_a(lod):
    """A 22 x 14 m low-cost row ('face-me-I-face-you'): a single-storey block of six rooms with doors and small windows onto a narrow veranda, a rusty zinc roof, a water tank on a stand, washing lines."""
    p = Part("row"); x0, x1, y0, y1, h = -9.0, 9.0, -2.0, 4.0, 3.0
    paving(p, -11.0, -7.0, 11.0, 7.0, "dry_earth", h=0.04)
    p.box(x0, y0, 0.0, x1, y1, h, "plaster_weathered", skip=("bottom",))
    if lod >= 2: return p
    p.box(x0, y0 - 1.6, 0.0, x1, y0, 0.25, CON, skip=("bottom",))                                                                                                 # veranda floor
    gable_roof_x(p, x0, x1, y0 - 1.6, y1, h, 1.0, "roof_zinc_rust", over=0.3)
    for k in range(6):
        a = x0 + 3.0 * k + 1.5; pane(p, "-y", a - 0.7, y0, 0.25, 0.85, 2.0, "wood_door"); pane(p, "-y", a + 0.6, y0, 1.1, 0.8, 0.9, "window_timber")
    windows(p, "+y", x0 + 0.5, x1 - 0.5, y1, [0], lod, z0=1.2, bay=3.0, w=0.8, h=0.8, key="window_timber", sills=False)
    if lod == 0:
        for k in range(7): x = x0 + 3.0 * k; p.box(x - 0.08, y0 - 1.5, 0.25, x + 0.08, y0 - 1.34, h, "timber_weathered", skip=("bottom",))
        p.box(9.4, 1.4, 0.04, 10.6, 2.6, 2.0, "timber_weathered", skip=("bottom",)); p.tube((10.0, 2.0, 2.0), (10.0, 2.0, 3.4), 0.6, 0.58, 10, "kerb_black", caps=(False, True))
        for k in range(2): p.tube((-8.0 + 8.0 * k, 5.5, 0.0), (-8.0 + 8.0 * k, 5.5, 1.9), 0.04, 0.04, 6, GAL, caps=(False, True))
        p.box(-3.0, -5.6, 0.04, -0.6, -5.2, 0.45, "timber_weathered", skip=("bottom",)); p.box(-3.0, -5.25, 0.45, -0.6, -5.15, 0.9, "timber_weathered", skip=())        # a bench in the yard
        p.box(4.0, -5.8, 0.04, 5.2, -4.8, 0.7, "concrete_dark", skip=("bottom",)); p.tube((4.6, -5.3, 0.7), (4.6, -5.3, 0.95), 0.28, 0.3, 8, "kerb_black", caps=(False, True))   # a cooking corner with a pot
        p.box(-8.0, 5.49, 1.85, 0.0, 5.51, 1.87, BLK, skip=())
        for k in range(6): p.box(-7.5 + 1.2 * k, 5.45, 1.1, -6.8 + 1.2 * k, 5.47, 1.85, ("fabric_red", "fabric_white", "paint_sky")[k % 3], skip=())
    return p


def bungalow_a(lod):
    """A 24 x 24 m middle-class bungalow: a hipped tile roof over a veranda on columns, windows with burglar-proof grilles, a carport, a walled compound with a gate, a mango tree."""
    p = Part("bungalow"); x0, x1, y0, y1, h = -7.0, 7.0, -1.0, 9.0, 3.4
    paving(p, -12.0, -12.0, 12.0, 12.0, "dry_earth", h=0.04); paving(p, 2.0, -12.0, 7.5, -1.0, "paver_red"); p.box(x0, y0, 0.0, x1, y1, h, W2, skip=("bottom",))
    if lod >= 2: hip_roof(p, x0, y0 - 2.0, x1, y1, h, 2.0, "roof_tile_red", over=0.6); return p
    hip_roof(p, x0, y0 - 2.0, x1, y1, h, 2.2, "roof_tile_red", over=0.6); p.box(x0, y0 - 2.0, 0.0, x1, y0, 0.3, "tile_floor", skip=("bottom",))
    windows(p, "-y", x0 + 0.5, x1 - 0.5, y0, [0], lod, z0=0.9, bay=3.4, w=1.5, h=1.4, skip={(0, 1)}); p.box(-3.0, y0 - 0.05, 0.3, -1.6, y0, 2.4, "wood_door", skip=("back",))
    windows(p, "-x", y0 + 0.5, y1 - 0.5, x0, [0], lod, z0=0.9, bay=3.0, w=1.3); windows(p, "+x", y0 + 0.5, y1 - 0.5, x1, [0], lod, z0=0.9, bay=3.0, w=1.3); windows(p, "+y", x0 + 0.5, x1 - 0.5, y1, [0], lod, z0=0.9, bay=3.2, w=1.2)
    if lod == 0:
        columns(p, [(x, y0 - 1.8) for x in (-6.6, -2.2, 2.2, 6.6)], 0.3, h, 0.16)
        p.box(2.5, -9.0, 2.6, 7.2, -3.4, 2.75, "roof_zinc", skip=())
        for (x, y) in ((2.7, -8.8), (7.0, -8.8)): p.tube((x, y, 0.04), (x, y, 2.6), 0.06, 0.06, 6, GAL, caps=(False, False))
        tree(p, -8.0, -7.0, 7.5, 3.2, lod, seed=81); tank(p, 9.5, 8.0, 0.04, r=0.8, h=1.6); planter(p, x0, -6.0, -1.0, -4.5, lod, seed=13)
    compound(p, -12.0, -12.0, 12.0, 12.0, 2.4, lod, gate_c=4.75, gate_w=4.5, key=W2)
    return p


def semidetached_a(lod):
    """A 26 x 22 m semi-detached pair: two mirrored two-storey houses under one hipped roof, two entrances with small porches, balconies, two gates in a shared wall."""
    p = Part("semi"); x0, x1, y0, y1, sh = -9.0, 9.0, -2.0, 8.0, 3.2
    paving(p, -13.0, -11.0, 13.0, 11.0, "dry_earth", h=0.04); paving(p, -13.0, -11.0, 13.0, -2.0, "paver_red"); p.box(x0, y0, 0.0, x1, y1, sh * 2, W1, skip=("bottom",))
    if lod >= 2: hip_roof(p, x0, y0, x1, y1, sh * 2, 2.0, "roof_tile_red", over=0.5); return p
    hip_roof(p, x0, y0, x1, y1, sh * 2, 2.4, "roof_tile_red", over=0.6); p.box(-0.15, y0 - 0.3, 0.0, 0.15, y1 + 0.3, sh * 2 + 0.3, W3, skip=("bottom",))         # party wall
    for s in (-1, 1):
        windows(p, "-y", (0.5 if s > 0 else x0 + 0.5), (x1 - 0.5 if s > 0 else -0.5), y0, [0, 1], lod, z0=0.9, sh=sh, bay=2.8, w=1.4, h=1.4, skip={(0, 1 if s > 0 else 1)})
        p.box(s * 4.5 - 0.6, y0 - 0.05, 0.0, s * 4.5 + 0.6, y0, 2.3, "wood_door", skip=("back",)); canopy_slab(p, s * 4.5 - 1.2, y0 - 1.4, s * 4.5 + 1.2, y0, 2.6, key=W1, t=0.15)
        if lod == 0: balcony(p, "-y", s * 2.0, y0, sh + 0.0, 2.8, 1.0, lod)
    windows(p, "-x", y0 + 0.5, y1 - 0.5, x0, [0, 1], lod, z0=0.9, sh=sh, bay=3.0); windows(p, "+x", y0 + 0.5, y1 - 0.5, x1, [0, 1], lod, z0=0.9, sh=sh, bay=3.0)
    windows(p, "+y", x0 + 0.5, x1 - 0.5, y1, [0, 1], lod, z0=0.9, sh=sh, bay=3.0, w=1.2, ac=0.3)
    if lod == 0:
        band(p, x0, y0, x1, y1, 0.0, h=0.5, key=W3, out=0.03); tank(p, -11.0, 9.0, 0.04, r=0.8, h=1.6); tank(p, 11.0, 9.0, 0.04, r=0.8, h=1.6)
        tree(p, -11.0, -8.0, 6.5, 2.4, lod, seed=91)
    compound(p, -13.0, -11.0, 13.0, 11.0, 2.4, lod, gate_c=-4.5, gate_w=3.6, key=W1)
    return p


def duplex_a(lod):
    """A 30 x 28 m premium duplex: two storeys and a roof terrace in the modern Abuja-Kano style (flat roofs with parapets, a stone-clad feature wall, tall glazing, a balcony with a glass rail), a car porch,
    a steel sliding gate, interlocking pavers, a lawn and palms."""
    p = Part("duplex"); x0, x1, y0, y1, sh = -9.0, 9.0, 0.0, 12.0, 3.5
    paving(p, -15.0, -14.0, 15.0, 14.0, "dry_earth", h=0.04); paving(p, -15.0, -14.0, 15.0, 0.0, "paver_red")
    p.box(x0, y0, 0.0, x1, y1, sh * 2, W1, skip=("bottom",)); p.box(-3.0, y0 + 2.0, sh * 2, 6.0, y1 - 1.0, sh * 3, W1, skip=("bottom",))
    if lod >= 2: return p
    p.box(-9.2, y0 - 0.2, 0.0, -5.0, y0 + 0.05, sh * 2 + 0.6, "stone_ivory", skip=("bottom", "back"))                                                               # stone-clad feature wall
    p.box(-4.6, y0 - 0.05, 0.3, 1.0, y0, sh * 2 - 0.4, "glass", skip=("back", "bottom")); p.box(1.6, y0 - 0.05, 0.0, 3.0, y0, 2.6, "wood_door", skip=("back",))
    windows(p, "-y", 3.5, x1 - 0.3, y0, [0, 1], lod, z0=0.6, sh=sh, bay=2.6, w=2.0, h=2.2, sills=False)
    windows(p, "-x", y0 + 0.5, y1 - 0.5, x0, [0, 1], lod, z0=0.9, sh=sh, bay=3.0, w=1.6); windows(p, "+x", y0 + 0.5, y1 - 0.5, x1, [0, 1], lod, z0=0.9, sh=sh, bay=3.0, w=1.6)
    windows(p, "+y", x0 + 0.5, x1 - 0.5, y1, [0, 1], lod, z0=0.9, sh=sh, bay=3.0, w=1.6, ac=0.4); windows(p, "-y", -2.5, 5.5, y0 + 2.0, [2], lod, z0=0.4, sh=sh, bay=2.6, w=2.0, h=2.4, sills=False)
    roof_flat(p, x0, y0, x1, y1, sh * 2, W1, lod, h=1.0); roof_flat(p, -3.0, y0 + 2.0, 6.0, y1 - 1.0, sh * 3, W1, lod, h=0.5)
    canopy_slab(p, 3.5, y0 - 6.0, 9.5, y0, 3.0, key=W1, t=0.3, edge="paint_grey_light")                                                                            # car porch
    if lod == 0:
        balcony(p, "-y", -1.8, y0, sh, 5.6, 1.4, lod, rail="glass"); columns(p, [(3.8, -5.7), (9.2, -5.7)], 0.08, 3.0, 0.15, key=W1, lod=1)
        for x in (-13.0, 13.0): palm(p, x, -10.0, 6.5, lod, seed=int(x) + 101)
        planter(p, -14.0, -2.5, -9.5, -1.2, lod, seed=14, lights=2); tree(p, -12.0, 9.0, 6.0, 2.2, lod, seed=103)
    compound(p, -15.0, -14.0, 15.0, 14.0, 2.6, lod, gate_c=6.5, gate_w=5.0, key=W1, gate_key=BLK)
    return p


def mansion_a(lod):
    """A 42 x 36 m luxury mansion: a large two-storey modern house with cantilevered slabs and tall glazing, a swimming pool with a tiled deck, a landscaped garden with palms, a gatehouse, a driveway,
    an ornamental steel gate in a high wall."""
    p = Part("mansion"); x0, x1, y0, y1, sh = -13.0, 11.0, 0.0, 14.0, 3.8
    paving(p, -21.0, -18.0, 21.0, 18.0, "dry_earth", h=0.04); paving(p, -21.0, -18.0, 21.0, -6.0, "paver_red"); paving(p, 11.0, -6.0, 21.0, 18.0, "tile_floor")
    p.box(x0, y0, 0.0, x1, y1, sh, W1, skip=("bottom",)); p.box(x0 + 2.0, y0 - 1.5, sh, x1 + 1.5, y1, sh * 2, W1, skip=("bottom",))
    if lod >= 2: return p
    p.box(x0 - 1.0, y0 - 3.0, sh * 2, x1 + 2.5, y1 + 0.5, sh * 2 + 0.4, W1, skip=())                                                                             # cantilevered roof slab
    p.box(x0 + 1.0, y0 - 2.0, sh - 0.3, x1 + 2.0, y0, sh, W1, skip=())                                                                                            # first-floor overhang slab
    p.box(x0 + 0.5, y0 - 0.05, 0.3, x1 - 0.5, y0, sh - 0.4, "glass", skip=("back", "bottom")); p.box(x0 + 2.5, y0 - 1.55, sh + 0.3, x1 + 1.0, y0 - 1.5, sh * 2 - 0.4, "glass", skip=("back", "bottom"))
    p.box(x0 - 0.2, y0 - 0.2, 0.0, x0 + 2.0, y1, sh * 2 + 0.4, "stone_ivory", skip=("bottom",))                                                                    # stone tower
    windows(p, "+y", x0 + 2.5, x1 - 0.5, y1, [0, 1], lod, z0=0.6, sh=sh, bay=3.0, w=2.2, h=2.4, sills=False, ac=0.3); windows(p, "+x", y0 + 0.5, y1 - 0.5, x1 + 1.5, [1], lod, z0=0.6, sh=sh, bay=3.0, w=2.0, h=2.4, sills=False)
    windows(p, "+x", y0 + 0.5, y1 - 0.5, x1, [0], lod, z0=0.6, sh=sh, bay=3.0, w=2.0, h=2.4, sills=False)
    p.box(12.0, 1.0, 0.04, 20.0, 12.0, 0.18, "tile_floor", skip=("bottom",)); p.box(13.0, 2.0, 0.18, 19.0, 11.0, 0.2, "water_pool", skip=("bottom",))               # pool and deck
    p.box(-20.5, -17.5, 0.0, -15.5, -13.0, 3.0, W1, skip=("bottom",))                                                                                               # gatehouse
    if lod == 0:
        p.box(-20.6, -17.6, 3.0, -15.4, -12.9, 3.2, CON, skip=()); p.box(-15.55, -16.5, 1.0, -15.5, -14.5, 2.2, "glass", skip=("left",))
        for k in range(4): palm(p, -19.0 + 12.0 * (k % 2) + (0 if k < 2 else 2.0), -9.0 + (0 if k < 2 else 22.0), 7.0, lod, seed=111 + k)
        tree(p, 18.0, -12.0, 7.0, 2.8, lod, seed=120); planter(p, -12.0, -5.5, 8.0, -3.8, lod, seed=15, lights=5)
        for k in range(3): p.box(13.5 + 2.0 * k, 11.6, 0.18, 14.7 + 2.0 * k, 12.3, 0.45, "paint_white", skip=("bottom",))                                         # loungers
        p.tube((16.0, 0.3, 0.18), (16.0, 0.3, 2.6), 0.04, 0.04, 6, GAL, caps=(False, False)); p.prism(circle_pts(1.4, 10, 16.0, 0.3), 2.5, 2.6, "fabric_white")        # umbrella
    compound(p, -21.0, -18.0, 21.0, 18.0, 3.0, lod, gate_c=-2.0, gate_w=6.0, key=W1, gate_key="trim_gold" if lod == 0 else BLK)
    return p


# ---------------------------------------------------------------------------------------------------------------------------------------- registration (catalogue extras: lamp heads, tags)
def _x(aid, fam, cls, tags, heads, ref=None): extra(aid, fam, cls, ref or REF_STREET, heads=heads, preset="glow_door", tags=list(tags) + ["generic_design"], intensity=1.0, range=12, poolRadius=4.0)


_x("kano_supermarket_a", "H", "CITY_SPECIFIC", ["commercial", "supermarket", "parking"], [(7.0, -3.5, 3.6), (-4.0, -1.5, 3.5)])
_x("kano_restaurant_a", "H", "CITY_SPECIFIC", ["commercial", "restaurant", "terrace"], [(0.0, -2.5, 3.2), (-5.0, -8.5, 2.8)])
_x("kano_fastfood_a", "H", "CITY_SPECIFIC", ["commercial", "fast_food", "drive_through"], [(-2.0, -2.5, 4.0), (7.5, 3.0, 3.0)])
_x("kano_hotel_a", "H", "CITY_SPECIFIC", ["commercial", "hotel", "high_rise"], [(0.0, -8.5, 4.0), (-4.0, -2.5, 4.4), (4.0, -2.5, 4.4)])
_x("kano_bank_a", "H", "CITY_SPECIFIC", ["commercial", "bank", "atm"], [(0.0, -3.5, 3.4), (10.8, 3.5, 2.6)])
_x("rg_ngn_pharmacy_a", "H", "REGIONAL", ["commercial", "pharmacy", "shop_house"], [(0.0, -3.5, 3.6)])
_x("rg_ngn_phone_shops_a", "H", "REGIONAL", ["commercial", "shop_row", "phones", "electronics"], [(-6.0, -6.0, 3.4), (2.0, -6.0, 3.4)])
_x("rg_ngn_spare_parts_row_a", "H", "REGIONAL", ["commercial", "shop_row", "spare_parts", "tyres"], [(-6.0, -6.0, 3.4), (2.0, -6.0, 3.4)])
_x("kano_car_showroom_a", "H", "CITY_SPECIFIC", ["commercial", "car_showroom", "dealership"], [(0.0, -2.5, 5.0), (-6.0, -1.5, 6.0), (6.0, -1.5, 6.0)])
_x("rg_ngn_auto_workshop_a", "H", "REGIONAL", ["commercial", "workshop", "mechanic"], [(0.0, 1.0, 4.4)])
_x("kano_shopping_plaza_a", "H", "CITY_SPECIFIC", ["commercial", "shopping_plaza", "parking"], [(-10.0, -1.5, 3.6), (10.0, -1.5, 3.6), (0.0, -1.5, 7.8)])
_x("rg_ngn_wholesale_depot_a", "H", "REGIONAL", ["commercial", "wholesale", "warehouse"], [(-8.75, -2.5, 4.6), (5.25, -2.5, 4.6)])
_x("kano_hospital_general_a", "I", "CITY_SPECIFIC", ["public", "hospital", "health"], [(0.0, -9.5, 3.4), (15.5, -7.5, 2.9), (-21.0, -18.5, 2.4)])
_x("rg_ngn_clinic_a", "I", "REGIONAL", ["public", "clinic", "health"], [(0.0, -3.0, 3.0), (2.0, -10.0, 2.3)])
_x("kano_school_a", "I", "CITY_SPECIFIC", ["public", "school", "education"], [(0.0, 3.5, 3.0), (0.0, -18.0, 2.3)])
_x("kano_police_division_a", "I", "CITY_SPECIFIC", ["public", "police", "security"], [(0.0, -1.5, 2.8), (5.0, -12.0, 2.6)])
_x("kano_fire_station_a", "I", "CITY_SPECIFIC", ["public", "fire_station", "emergency"], [(-3.5, -1.0, 4.6)])
_x("kano_government_offices_a", "I", "CITY_SPECIFIC", ["public", "government", "civic"], [(0.0, -5.5, 9.5), (12.0, -17.0, 2.6)])
_x("kano_motor_park_a", "I", "CITY_SPECIFIC", ["public", "transport", "motor_park"], [(0.0, -16.5, 5.6), (-4.0, 0.0, 3.2), (-4.0, 10.0, 3.2)])
_x("rg_ngn_factory_a", "I", "REGIONAL", ["industrial", "factory"], [(-13.0, -4.5, 3.0), (4.0, -17.0, 2.8)])
_x("rg_ngn_lowcost_row_a", "J", "REGIONAL", ["residential", "lowcost", "face_me_i_face_you"], [(-4.5, -3.0, 2.6), (4.5, -3.0, 2.6)], ref=["R-006", "R-016", "R-022"])
_x("rg_ngn_bungalow_a", "J", "REGIONAL", ["residential", "bungalow", "middle_class"], [(0.0, -2.5, 3.0), (4.75, -12.0, 2.6)], ref=["R-006", "R-009", "R-016"])
_x("kano_semidetached_a", "J", "CITY_SPECIFIC", ["residential", "semi_detached", "middle_class"], [(-4.5, -2.8, 2.4), (4.5, -2.8, 2.4)], ref=["R-006", "R-009"])
_x("kano_duplex_a", "J", "CITY_SPECIFIC", ["residential", "duplex", "premium"], [(6.5, -3.0, 2.8), (-2.0, -0.5, 3.2)], ref=["R-006", "R-009"])
_x("kano_mansion_a", "J", "CITY_SPECIFIC", ["residential", "mansion", "premium", "pool"], [(-2.0, -1.0, 3.4), (-18.0, -13.0, 2.6), (16.0, 6.0, 0.6)], ref=["R-006", "R-009"])
