"""Family G: fuel stations (the owner's city construction directive of 2026-11-14, reference R-040: the owner's generated concept of a modern Kano filling station at dusk).
Three COMPLETE station environments, not pumps on empty ground. Each one has a forecourt, a canopy on columns, pump islands with dispensers, a shop or a cashier office, signage, an entrance and an exit, air and
water, the fill points and vents of the underground tanks, planters and a perimeter wall:
  `kano_fuel_station_large_a`    after R-040: a 30 m canopy with a lit fascia and a raised brand box, four numbered islands with two dispensers each, a convenience mart with a glazed front, a price totem
                                  (brand, three fuel prices, 24 HOURS, services), a services board (shop, ATM, air, toilets), an ATM kiosk, a generator house, kerbed planters with bollard lights (46 x 36 m)
  `kano_fuel_station_medium_a`   a neighbourhood station of the same fictional chain: a 20 m canopy on two rows of columns, two islands, a kiosk shop, a car-wash bay, a smaller totem (32 x 26 m)
  `rg_ngn_fuel_station_small_a`  an independent roadside filling station: a pitched zinc canopy on steel posts, one island with two older pumps, a block cashier office with a painted FILLING STATION board,
                                  a hand-painted price board, oil drums, jerry cans, a bench and a generator, a worn concrete forecourt with laterite margins (20 x 16 m)
The brand is FICTIONAL: KANO OIL is the name in the owner's concept image R-040 (a generated image, not a company); the small station carries generic words only. Words and the flame mark are PAINTED into the
surface-bake atlas (transport_kit.surface.sign_key). Real metres, front (the road) at -Y, origin on the ground at the centre of the footprint, three LODs."""
from __future__ import annotations
import math
from kano_kit.geo import Part
from .common import prism_y, circle_pts, extra
from . import surface as SB

REF_FUEL = ["R-040"]
NAVY, YEL, WHITE, CON, PAV, GAL, BLK, GLOW = "paint_navy", "paint_brand_yellow", "paint_white", "concrete", "pavement_grey", "metal_galvanised", "metal_gate_black", "lantern_glow"
NAVY_C, YEL_C, WHITE_C = (0.075, 0.11, 0.29), (0.95, 0.70, 0.10), (0.97, 0.97, 0.97)

# ---- the painted signs of the fictional chain and of the independent station
S_FASCIA = SB.sign_key("sign_kanooil_fascia", NAVY_C, WHITE_C, ["KANO OIL"], logo=True, accent=YEL_C)
S_TOTEM = SB.sign_key("sign_kanooil_totem", NAVY_C, WHITE_C, ["KANO OIL"], logo=True, accent=YEL_C, layout="stack")
S_MART = SB.sign_key("sign_kanooil_mart", NAVY_C, (0.98, 0.80, 0.22), ["KANO OIL MART"], logo=True)
S_SHOP = SB.sign_key("sign_kanooil_shop", NAVY_C, WHITE_C, ["KANO OIL SHOP"], logo=True)
S_PRICES = SB.sign_key("sign_fuel_prices", (0.045, 0.05, 0.07), (0.98, 0.26, 0.10), ["PETROL      945.00", "DIESEL     1,089.00", "KEROSENE    785.00"])
S_24H = SB.sign_key("sign_24_hours", YEL_C, NAVY_C, ["24 HOURS"])
S_SERV = SB.sign_key("sign_fuel_services", NAVY_C, WHITE_C, ["SHOP", "ATM", "AIR & WATER", "TOILETS"])
S_TSERV = SB.sign_key("sign_totem_services", NAVY_C, WHITE_C, ["SHOP   ATM", "FOOD   WC"])
S_AIR = SB.sign_key("sign_air_water", NAVY_C, WHITE_C, ["AIR", "WATER"])
S_ATM = SB.sign_key("sign_atm", (0.10, 0.42, 0.24), WHITE_C, ["ATM"])
S_WASH = SB.sign_key("sign_car_wash", (0.12, 0.40, 0.68), WHITE_C, ["CAR WASH"])
S_ENTRY = SB.sign_key("sign_entry", (0.10, 0.42, 0.24), WHITE_C, ["IN"])
S_EXIT = SB.sign_key("sign_exit", (0.62, 0.10, 0.08), WHITE_C, ["OUT"])
S_PUMP = [SB.sign_key(f"sign_pump_{k}", NAVY_C, WHITE_C, [str(k)]) for k in range(1, 5)]
S_FILL = SB.sign_key("sign_filling_station", (0.93, 0.92, 0.86), (0.10, 0.36, 0.20), ["FILLING STATION"], accent=(0.62, 0.12, 0.10))
S_HAND = SB.sign_key("sign_price_handpainted", (0.92, 0.91, 0.86), (0.08, 0.08, 0.08), ["PETROL  N945", "DIESEL  N1,089", "KERO  N785"])
S_OIL = SB.sign_key("sign_engine_oil", (0.80, 0.14, 0.10), WHITE_C, ["ENGINE OIL"])


def kerb_run(p: Part, x0, y0, x1, y1, h=0.25, w=0.25, seg=1.6):
    """A kerb of alternating black and white painted blocks along a straight line (axis-aligned)."""
    L = abs(x1 - x0) + abs(y1 - y0); n = max(1, int(round(L / seg)))
    for k in range(n):
        a = k / n; b = (k + 1) / n; key = "kerb_black" if k % 2 == 0 else "kerb_white"
        if abs(x1 - x0) >= abs(y1 - y0): p.box(x0 + (x1 - x0) * a, y0 - w / 2, 0.0, x0 + (x1 - x0) * b, y0 + w / 2, h, key, skip=("bottom",))
        else: p.box(x0 - w / 2, y0 + (y1 - y0) * a, 0.0, x0 + w / 2, y0 + (y1 - y0) * b, h, key, skip=("bottom",))


def planter(p: Part, x0, y0, x1, y1, lod, seed=0, lights=0):
    """A kerbed planter: painted kerb all round, a bed of earth, clipped shrubs and flowering bushes, optional bollard lights."""
    if lod >= 2: p.box(x0, y0, 0.0, x1, y1, 0.3, "hedge_green", skip=("bottom",)); return
    kerb_run(p, x0, y0, x1, y0); kerb_run(p, x0, y1, x1, y1); kerb_run(p, x0, y0 + 0.125, x0, y1 - 0.125); kerb_run(p, x1, y0 + 0.125, x1, y1 - 0.125)
    p.box(x0 + 0.125, y0 + 0.125, 0.0, x1 - 0.125, y1 - 0.125, 0.18, "dry_earth", skip=("bottom",))
    if lod == 1: p.box(x0 + 0.4, y0 + 0.4, 0.18, x1 - 0.4, y1 - 0.4, 0.8, "hedge_green", skip=("bottom",)); return
    L = max(x1 - x0, y1 - y0); n = max(1, int(L / 2.2)); along_x = (x1 - x0) >= (y1 - y0)
    for k in range(n):
        t = (k + 0.5) / n; cx = x0 + (x1 - x0) * t if along_x else (x0 + x1) / 2; cy = (y0 + y1) / 2 if along_x else y0 + (y1 - y0) * t
        key = "flower_red" if (k + seed) % 3 == 1 else "hedge_green"; r = 0.45 + 0.1 * ((k * 7 + seed) % 3)
        p.blob((cx, cy, 0.18 + r * 0.7), (r * 1.2, r, r * 0.8), key, subdiv=0, jitter=0.1, seed=seed * 31 + k)
    for k in range(lights):
        t = (k + 0.5) / lights; cx = x0 + (x1 - x0) * t if along_x else x0 + 0.35; cy = y0 + 0.35 if along_x else y0 + (y1 - y0) * t
        p.tube((cx, cy, 0.18), (cx, cy, 0.95), 0.06, 0.06, 8, BLK, caps=(False, False)); p.tube((cx, cy, 0.95), (cx, cy, 1.15), 0.09, 0.09, 8, GLOW, caps=(False, True)); p.tube((cx, cy, 1.15), (cx, cy, 1.2), 0.11, 0.11, 8, BLK, caps=(False, True))


def dispenser(p: Part, x, y, z, lod, body=WHITE, side=NAVY, accent=YEL, header=NAVY, old=False):
    """A fuel dispenser on an island that runs along Y: displays, nozzles and hoses on both long sides (+X and -X), a lit header. `old`: a squarer older pump."""
    w, d, h = (0.32, 0.45, 1.75) if old else (0.3, 0.5, 2.05)
    p.box(x - w, y - d, z, x + w, y + d, z + h, body, skip=("bottom",))
    if lod >= 2: return
    p.box(x - w - 0.02, y - d - 0.02, z + h, x + w + 0.02, y + d + 0.02, z + h + 0.28, header, skip=("bottom",))
    if lod == 1: return
    for s in (-1, 1):
        fx = x + s * w; o = 0.012 * s
        p.box(min(fx, fx + o), y - d + 0.06, z + 0.3, max(fx, fx + o), y + d - 0.06, z + h - 0.15, side, skip=("bottom", "left" if s > 0 else "right"))                  # side panel
        q = fx + o; o2 = 0.02 * s
        p.box(min(q, q + o2), y - 0.24, z + h - 0.75, max(q, q + o2), y + 0.24, z + h - 0.35, "glass_dark", skip=("bottom", "left" if s > 0 else "right"))           # display
        p.box(min(fx, fx + 0.03 * s), y - d - 0.01, z, max(fx, fx + 0.03 * s), y + d + 0.01, z + 0.28, accent, skip=("bottom", "left" if s > 0 else "right"))  # kick stripe
        for hy in (-0.3, 0.3):
            hx = fx + s * 0.08; p.box(min(fx, hx), y + hy - 0.07, z + 0.95, max(fx, hx), y + hy + 0.07, z + 1.25, BLK, skip=("left" if s > 0 else "right",))              # holster
        p.path_tube([(fx + s * 0.1, y - 0.3, z + 1.15), (fx + s * 0.16, y, z + 0.55), (fx + s * 0.02, y + 0.42, z + h - 0.3)], [0.028] * 3, 5, "rubber", caps=(True, True), smooth=True)   # one hose a side
    if not old:
        for s in (-1, 1): p.box(x - 0.2, y + s * (d + 0.0), z + h + 0.03, x + 0.2, y + s * (d + 0.03), z + h + 0.25, GLOW, skip=("bottom",))                                     # lit header ends


def island(p: Part, x, y0, y1, lod, n_disp=2, old=False, barrier=True, ends=YEL):
    """A raised pump island along Y with painted ends, impact barriers and an extinguisher cabinet."""
    p.box(x - 0.7, y0, 0.0, x + 0.7, y1, 0.22, CON, skip=("bottom",))
    for k in range(n_disp): dispenser(p, x, y0 + (y1 - y0) * (k + 1) / (n_disp + 1), 0.22, lod, old=old)
    if lod >= 1: return
    for yy, s in ((y0, -1), (y1, 1)):
        p.box(x - 0.71, min(yy, yy - s * 0.4), 0.0, x + 0.71, max(yy, yy - s * 0.4), 0.23, ends, skip=("bottom",))
        if barrier:
            yb = yy + s * 0.35; p.path_tube([(x - 0.55, yb, 0.0), (x - 0.55, yb, 0.85), (x + 0.55, yb, 0.85), (x + 0.55, yb, 0.0)], [0.06] * 4, 8, YEL, caps=(False, False), smooth=True)
    p.box(x - 0.25, (y0 + y1) / 2 - 0.2, 0.22, x + 0.25, (y0 + y1) / 2 + 0.2, 1.0, "signal_red", skip=("bottom",))                                                         # extinguisher cabinet


def canopy(p: Part, x0, x1, y0, y1, zu, lod, brand=True, lights=(3, 6), fascia_h=1.2, flat=False):
    """A forecourt canopy: a white soffit with recessed lights, a navy fascia all round with a yellow cap and a glowing strip along its foot, a raised brand box on the road side."""
    p.box(x0, y0, zu, x1, y1, zu + 0.45, WHITE, skip=())
    if lod >= 2: return
    t = 0.12
    p.box(x0 - t, y0 - t, zu - 0.05, x1 + t, y0, zu + fascia_h, NAVY, skip=("back",)); p.box(x0 - t, y1, zu - 0.05, x1 + t, y1 + t, zu + fascia_h, NAVY, skip=("front",))
    p.box(x0 - t, y0, zu - 0.05, x0, y1, zu + fascia_h, NAVY, skip=("right", "front", "back")); p.box(x1, y0, zu - 0.05, x1 + t, y1, zu + fascia_h, NAVY, skip=("left", "front", "back"))
    p.box(x0, y0, zu + fascia_h - 0.1, x1, y1, zu + fascia_h - 0.05, "roof_zinc", skip=("bottom",))                                                                       # the roof deck behind the fascia
    if lod == 1:
        if brand: p.box((x0 + x1) / 2 - 4.0, y0 - 0.45, zu + 0.1, (x0 + x1) / 2 + 4.0, y0 - t, zu + fascia_h + 0.6, S_FASCIA, skip=("back",))
        return
    c = 0.3; z0c, z1c = zu + fascia_h, zu + fascia_h + 0.12; X0, Y0, X1, Y1 = x0 - t - 0.03, y0 - t - 0.03, x1 + t + 0.03, y1 + t + 0.03                                    # yellow cap: a rim, the zinc deck shows inside it
    for (a0, b0, a1, b1) in ((X0, Y0, X1, Y0 + c), (X0, Y1 - c, X1, Y1), (X0, Y0 + c, X0 + c, Y1 - c), (X1 - c, Y0 + c, X1, Y1 - c)): p.box(a0, b0, z0c, a1, b1, z1c, YEL, skip=())
    for (a0, b0, a1, b1, o) in ((x0 - t, y0 - t - 0.05, x1 + t, y0 - t, "front"), (x0 - t, y1 + t, x1 + t, y1 + t + 0.05, "back")):
        p.box(a0, b0, zu + 0.02, a1, b1, zu + 0.1, GLOW, skip=("back" if o == "front" else "front",))
    for (a0, b0, a1, b1, o) in ((x0 - t - 0.05, y0 - t, x0 - t, y1 + t, "left"), (x1 + t, y0 - t, x1 + t + 0.05, y1 + t, "right")):
        p.box(a0, b0, zu + 0.02, a1, b1, zu + 0.1, GLOW, skip=("right" if o == "left" else "left",))
    if brand:
        bx = (x0 + x1) / 2; p.box(bx - 4.2, y0 - 0.5, zu - 0.1, bx + 4.2, y0 - t, zu + fascia_h + 0.75, NAVY, skip=("back",))
        p.box(bx - 4.0, y0 - 0.53, zu + 0.05, bx + 4.0, y0 - 0.5, zu + fascia_h + 0.6, S_FASCIA, skip=("back",))
        p.box(bx - 4.25, y0 - 0.55, zu + fascia_h + 0.75, bx + 4.25, y0 - t, zu + fascia_h + 0.85, YEL, skip=())
    nx, ny = lights
    for i in range(nx):
        for j in range(ny):
            cx = x0 + (x1 - x0) * (j + 0.5) / ny; cy = y0 + (y1 - y0) * (i + 0.5) / nx; p.box(cx - 0.3, cy - 0.3, zu - 0.04, cx + 0.3, cy + 0.3, zu, GLOW, skip=("top",))


def column(p: Part, x, y, zu, lod, num=None, r=0.35):
    """A clad canopy column: white with a navy band on the road side, a plinth, and a lit pump-number box."""
    p.box(x - r, y - r, 0.0, x + r, y + r, zu, WHITE, skip=("bottom", "top"))
    if lod >= 1: return
    p.box(x - r - 0.08, y - r - 0.08, 0.0, x + r + 0.08, y + r + 0.08, 0.35, CON, skip=("bottom",))
    p.box(x - r * 0.55, y - r - 0.02, 0.35, x + r * 0.55, y - r, zu - 0.2, NAVY, skip=("back", "bottom"))
    if num is not None:
        p.box(x - 0.32, y - r - 0.18, 3.3, x + 0.32, y - r - 0.02, 4.0, S_PUMP[num - 1], skip=("back",)); p.box(x - 0.32, y + r + 0.02, 3.3, x + 0.32, y + r + 0.18, 4.0, S_PUMP[num - 1], skip=("front",))


def totem(p: Part, x, y, h, lod, small=False):
    """The price totem: a navy pylon on a planted plinth; on both faces the brand box, the price board, 24 HOURS and the services."""
    hw = 0.9 if small else 1.2
    p.box(x - hw - 0.3, y - 0.6, 0.0, x + hw + 0.3, y + 0.6, 0.9, CON, skip=("bottom",)); p.box(x - hw, y - 0.35, 0.9, x + hw, y + 0.35, h, NAVY, skip=("bottom",))
    if lod >= 2: return
    zs = [(h - 2.3, h - 0.15, S_TOTEM), (h - 4.6, h - 2.45, S_PRICES), (h - 5.5, h - 4.75, S_24H)] + ([] if small else [(1.3, h - 5.65, S_TSERV)])
    for z0, z1, key in zs:
        p.box(x - hw + 0.1, y - 0.38, z0, x + hw - 0.1, y - 0.35, z1, key, skip=("back",)); p.box(x - hw + 0.1, y + 0.35, z0, x + hw - 0.1, y + 0.38, z1, key, skip=("front",))
    if lod == 0:
        p.box(x - hw - 0.04, y - 0.39, h, x + hw + 0.04, y + 0.39, h + 0.15, YEL, skip=()); p.blob((x - hw + 0.1, y, 1.1), (0.45, 0.4, 0.35), "hedge_green", subdiv=1, jitter=0.1, seed=5); p.blob((x + hw - 0.1, y, 1.1), (0.45, 0.4, 0.35), "flower_red", subdiv=1, jitter=0.1, seed=6)


def tank_fill(p: Part, x, y, n=3, lod=0):
    """The fill points of the underground tanks: round steel covers in a painted surround, and a warning plate."""
    if lod >= 1: p.box(x - 0.6, y - 0.6, 0.0, x + 0.6 + 1.4 * (n - 1), y + 0.6, 0.13, "warning_yellow", skip=("bottom",)); return
    for k in range(n):
        cx = x + 1.4 * k; p.box(cx - 0.6, y - 0.6, 0.0, cx + 0.6, y + 0.6, 0.12, "warning_yellow", skip=("bottom",)); p.prism(circle_pts(0.42, 14, cx, y), 0.12, 0.14, GAL)


def vents(p: Part, x, y, n=3, h=4.6):
    for k in range(n):
        cx = x + 0.35 * k; p.tube((cx, y, 0.0), (cx, y, h), 0.05, 0.05, 8, GAL, caps=(False, False)); p.tube((cx, y, h), (cx, y, h + 0.18), 0.09, 0.07, 8, GAL, caps=(True, True))


def wall_run(p: Part, x0, y0, x1, y1, h, lod, key="plaster_cream", t=0.2, pier=4.0):
    """A compound wall along an axis-aligned line, with piers and a coping."""
    if abs(x1 - x0) >= abs(y1 - y0): p.box(min(x0, x1), y0 - t / 2, 0.0, max(x0, x1), y0 + t / 2, h, key, skip=("bottom",))
    else: p.box(x0 - t / 2, min(y0, y1), 0.0, x0 + t / 2, max(y0, y1), h, key, skip=("bottom",))
    if lod >= 1: return
    L = abs(x1 - x0) + abs(y1 - y0); n = max(1, int(L / pier))
    for k in range(n + 1):
        a = k / n; cx = x0 + (x1 - x0) * a; cy = y0 + (y1 - y0) * a; p.box(cx - 0.22, cy - 0.22, 0.0, cx + 0.22, cy + 0.22, h + 0.2, key, skip=("bottom",))
    if abs(x1 - x0) >= abs(y1 - y0): p.box(min(x0, x1), y0 - t / 2 - 0.05, h, max(x0, x1), y0 + t / 2 + 0.05, h + 0.08, CON, skip=())
    else: p.box(x0 - t / 2 - 0.05, min(y0, y1), h, x0 + t / 2 + 0.05, max(y0, y1), h + 0.08, CON, skip=())


def post_sign(p: Part, x, y, key, w=0.9, h=0.6, z=1.4):
    p.tube((x, y, 0.0), (x, y, z + h), 0.04, 0.04, 6, GAL, caps=(False, True)); p.box(x - w / 2, y - 0.06, z, x + w / 2, y - 0.04, z + h, key, skip=("back",)); p.box(x - w / 2, y - 0.04, z, x + w / 2, y - 0.02, z + h, GAL, skip=("front",))


def shopfront_building(p: Part, x0, x1, y0, y1, h, lod, sign, wall="plaster_white", band=NAVY, glow_line=True, awning=1.4):
    """A single-storey shop with a glazed front: full-height shop windows and a door, a navy fascia band with the painted name, a projecting awning, side windows, a back door, AC units on the roof."""
    p.box(x0, y0, 0.0, x1, y1, h, wall, skip=("bottom", "front"))
    if lod >= 2: p.box(x0, y0 - 0.01, 0.0, x1, y0, h, "glass_shop", skip=("back", "bottom")); return
    gz = 3.1; p.box(x0, y0, gz, x1, y0 + 0.2, h, band, skip=("bottom",))                                                                                                      # fascia band above the glazing
    p.box(x0 + 0.3, y0 + 0.15, 0.0, x1 - 0.3, y0 + 0.2, gz, "glass_shop", skip=("back", "bottom"))                                                                          # shop windows
    p.box(x0, y0 + 0.02, 0.0, x0 + 0.3, y0 + 0.2, gz, wall, skip=("bottom", "back")); p.box(x1 - 0.3, y0 + 0.02, 0.0, x1, y0 + 0.2, gz, wall, skip=("bottom", "back"))
    if lod == 1: p.box((x0 + x1) / 2 - (x1 - x0) * 0.35, y0 - 0.08, gz + 0.15, (x0 + x1) / 2 + (x1 - x0) * 0.35, y0, h - 0.15, sign, skip=("back",)); return
    sw = min(x1 - x0 - 1.5, 9.0); p.box((x0 + x1) / 2 - sw / 2, y0 - 0.12, gz + 0.2, (x0 + x1) / 2 + sw / 2, y0, h - 0.1, sign, skip=("back",))
    if glow_line: p.box(x0, y0 - 0.04, gz + 0.02, x1, y0, gz + 0.1, GLOW, skip=("back",))
    if awning: p.box(x0 - 0.2, y0 - awning, gz - 0.05, x1 + 0.2, y0, gz + 0.12, "paint_grey_light", skip=())
    dx = (x0 + x1) / 2; p.box(dx - 0.9, y0 + 0.12, 0.0, dx + 0.9, y0 + 0.15, 2.4, "glass", skip=("back", "bottom")); p.box(dx - 1.0, y0 + 0.1, 2.4, dx + 1.0, y0 + 0.15, 2.5, "frame_aluminium_dark", skip=("back",))
    for xx in (x0, x1):
        s = -1 if xx == x0 else 1
        for yy in [y0 + 2.5 + 2.8 * k for k in range(int((y1 - y0 - 3.0) / 2.8))]:
            p.box(min(xx, xx + 0.05 * s), yy - 0.6, 1.0, max(xx, xx + 0.05 * s), yy + 0.6, 2.2, "window_plain", skip=("bottom", "left" if s > 0 else "right"))
    p.box(x1 - 2.5, y1, 0.0, x1 - 1.5, y1 + 0.05, 2.2, "wood_door", skip=("bottom", "front"))
    p.box(x0 - 0.06, y0 - 0.06, h - 0.02, x1 + 0.06, y1 + 0.06, h + 0.45, wall, skip=("bottom",))                                                                               # parapet
    p.box(x0 + 0.2, y0 + 0.2, h + 0.45, x1 - 0.2, y1 - 0.2, h + 0.47, "roof_zinc", skip=("bottom",))
    for k in range(max(1, int((x1 - x0) / 6))): ax = x0 + 2.0 + 5.5 * k; p.box(ax - 0.5, y1 - 2.2, h + 0.47, ax + 0.5, y1 - 1.6, h + 1.15, "paint_white", skip=("bottom",))


# ------------------------------------------------------------------------------------------------------------------------------------------ the three stations
def large_a(lod):
    """After R-040: a 46 x 36 m modern station (front = the road at y = -18)."""
    p = Part("station")
    # forecourt: concrete under the canopy, pavers elsewhere (tiled without overlaps), entrance and exit aprons at the road
    p.box(-14.5, -13.0, 0.0, 16.5, 1.5, 0.12, CON, skip=("bottom",))
    for (a, b, c, d) in ((-23.0, -18.0, 23.0, -13.0), (-23.0, -13.0, -14.5, 1.5), (16.5, -13.0, 23.0, 1.5), (-23.0, 1.5, 23.0, 18.0)): p.box(a, b, 0.0, c, d, 0.12, PAV, skip=("bottom",))
    if lod >= 2:
        p.box(-14.5, -12.5, 5.8, 16.5, 0.5, 7.0, NAVY, skip=()); p.box(-22.0, 3.0, 0.0, -4.0, 14.0, 4.8, "plaster_white", skip=("bottom",)); p.box(18.3, -16.4, 0.0, 20.7, -15.6, 9.5, NAVY, skip=("bottom",))
        for x in (-9.5, -2.5, 4.5, 11.5): p.box(x - 0.4, -6.5, 0.0, x + 0.4, -5.5, 5.8, WHITE, skip=("bottom", "top"))
        return p
    canopy(p, -14.5, 16.5, -12.5, 0.5, 5.8, lod, lights=(3, 7))
    for k, x in enumerate((-9.5, -2.5, 4.5, 11.5)):
        island(p, x, -10.5, -1.5, lod); column(p, x, -6.0, 5.8, lod, num=k + 1)
    shopfront_building(p, -22.0, -4.0, 3.0, 14.0, 4.8, lod, S_MART)
    if lod == 0:
        p.box(-23.6, 2.6, 0.0, -22.2, 3.4, 4.4, NAVY, skip=("bottom",)); p.box(-23.55, 2.57, 0.4, -22.25, 2.6, 4.2, S_SERV, skip=("back",))                                # services board (shop, ATM, air, toilets)
        p.box(-3.6, 4.0, 0.0, -1.8, 5.4, 2.6, "paint_grey_light", skip=("bottom",)); p.box(-3.4, 3.97, 1.0, -2.0, 4.0, 2.0, "glass_dark", skip=("back",)); p.box(-3.5, 3.95, 2.1, -1.9, 4.0, 2.55, S_ATM, skip=("back",))   # ATM kiosk
        for k in range(6): bx = -20.5 + 2.8 * k; p.box(bx, -0.5, 0.12, bx + 0.12, 2.6, 0.13, "road_white", skip=("bottom",))                                                 # parking bays in front of the mart
    totem(p, 19.5, -16.0, 9.5, lod)
    planter(p, -14.0, -17.9, 15.0, -16.4, lod, seed=1, lights=6); planter(p, -22.9, -12.0, -16.0, -10.2, lod, seed=2, lights=2); planter(p, 17.5, -12.0, 22.9, -10.2, lod, seed=3, lights=2)
    if lod == 0:
        p.box(18.5, -3.0, 0.0, 19.1, -2.5, 1.5, NAVY, skip=("bottom",)); p.box(18.45, -3.03, 0.9, 19.15, -3.0, 1.45, S_AIR, skip=("back",))                                  # air and water
        p.tube((18.4, -2.75, 1.0), (18.4, -2.75, 0.4), 0.18, 0.18, 10, "rubber", caps=(True, True)); p.tube((19.2, -2.75, 1.0), (19.2, -2.75, 0.4), 0.18, 0.18, 10, "rubber", caps=(True, True))
        tank_fill(p, 4.0, 9.0, n=3, lod=lod); vents(p, 20.0, 17.4)
        post_sign(p, -19.0, -17.3, S_ENTRY); post_sign(p, 19.0, -14.0, S_EXIT)
    # the back of the site: a wall, a generator house
    wall_run(p, -23.0, 17.9, 23.0, 17.9, 2.4, lod); wall_run(p, -23.0, 1.5, -23.0, 17.9, 2.4, lod); wall_run(p, 23.0, 1.5, 23.0, 17.9, 2.4, lod)
    p.box(9.0, 12.5, 0.0, 15.0, 17.0, 3.0, "plaster_cream", skip=("bottom",))
    if lod == 0:
        p.box(9.6, 12.47, 0.8, 11.6, 12.5, 2.4, "shutter_steel", skip=("back",)); p.box(12.4, 12.47, 1.2, 14.4, 12.5, 2.2, "shutter_steel", skip=("back",)); p.box(8.9, 12.4, 3.0, 15.1, 17.1, 3.15, CON, skip=())
        p.tube((14.2, 16.4, 3.0), (14.2, 16.4, 4.4), 0.12, 0.12, 8, BLK, caps=(False, True))                                                                                  # exhaust
    return p


def medium_a(lod):
    """A 32 x 26 m neighbourhood station of the same fictional chain: two islands under a 20 m canopy, a kiosk shop, a car-wash bay, a smaller totem."""
    p = Part("station")
    p.box(-9.0, -10.0, 0.0, 11.0, 0.0, 0.12, CON, skip=("bottom",))
    for (a, b, c, d) in ((-16.0, -13.0, 16.0, -10.0), (-16.0, -10.0, -9.0, 0.0), (11.0, -10.0, 16.0, 0.0), (-16.0, 0.0, 16.0, 13.0)): p.box(a, b, 0.0, c, d, 0.12, PAV, skip=("bottom",))
    if lod >= 2:
        p.box(-9.0, -9.5, 5.4, 11.0, -0.5, 6.4, NAVY, skip=()); p.box(-15.0, 3.0, 0.0, -6.0, 9.0, 4.2, "plaster_white", skip=("bottom",)); p.box(13.3, -12.2, 0.0, 15.1, -11.6, 7.0, NAVY, skip=("bottom",))
        for x in (-3.0, 5.0): p.box(x - 0.4, -5.5, 0.0, x + 0.4, -4.5, 5.4, WHITE, skip=("bottom", "top"))
        return p
    canopy(p, -9.0, 11.0, -9.5, -0.5, 5.4, lod, lights=(2, 5), fascia_h=1.0)
    for k, x in enumerate((-3.0, 5.0)):
        island(p, x, -8.0, -2.0, lod); column(p, x, -5.0, 5.4, lod, num=k + 1)
    shopfront_building(p, -15.0, -6.0, 3.0, 9.0, 4.2, lod, S_SHOP, awning=1.0)
    totem(p, 14.2, -11.9, 7.0, lod, small=True)
    # car-wash bay: an open shelter on steel posts with a sign, a drain channel
    p.box(4.0, 4.0, 4.0, 12.0, 10.0, 4.25, "roof_zinc", skip=())
    if lod == 0:
        for (x, y) in ((4.2, 4.2), (11.8, 4.2), (4.2, 9.8), (11.8, 9.8)): p.tube((x, y, 0.12), (x, y, 4.0), 0.1, 0.1, 8, GAL, caps=(False, False))
        p.box(6.0, 3.85, 4.25, 10.0, 4.0, 4.95, S_WASH, skip=("back",)); p.box(4.5, 6.8, 0.12, 11.5, 7.2, 0.125, "kerb_black", skip=("bottom",))
        p.box(12.0, 9.0, 0.12, 12.6, 9.6, 1.4, "paint_sky", skip=("bottom",))                                                                                                # water tank stand
        p.box(-6.0, 3.0, 0.0, -5.4, 3.5, 1.5, NAVY, skip=("bottom",)); p.box(-6.05, 2.97, 0.9, -5.35, 3.0, 1.45, S_AIR, skip=("back",))
        tank_fill(p, -2.0, 7.0, n=2, lod=lod); vents(p, 14.5, 12.4, n=2)
        post_sign(p, -13.0, -12.3, S_ENTRY); post_sign(p, 13.0, -9.0, S_EXIT)
    planter(p, -8.5, -12.9, 10.5, -11.4, lod, seed=4, lights=4); planter(p, -15.9, -9.0, -11.0, -7.5, lod, seed=5, lights=1)
    wall_run(p, -16.0, 12.9, 16.0, 12.9, 2.2, lod); wall_run(p, -16.0, 0.0, -16.0, 12.9, 2.2, lod); wall_run(p, 16.0, 0.0, 16.0, 12.9, 2.2, lod)
    return p


def small_a(lod):
    """An independent roadside filling station, 20 x 16 m: a pitched zinc canopy on four steel posts, one island with two older pumps, a block cashier office with a painted board, the clutter of a working
    roadside business (oil drums, jerry cans, a bench, a generator), a worn concrete forecourt with laterite margins."""
    p = Part("station")
    p.box(-7.0, -8.0, 0.0, 7.0, 3.0, 0.1, CON, skip=("bottom",))
    for (a, b, c, d) in ((-10.0, -8.0, -7.0, 8.0), (7.0, -8.0, 10.0, 8.0), (-7.0, 3.0, 7.0, 8.0)): p.box(a, b, 0.0, c, d, 0.06, "laterite", skip=("bottom",))
    if lod >= 2:
        p.box(-4.0, -5.5, 4.2, 4.0, 0.5, 4.8, "roof_zinc", skip=()); p.box(-9.0, 2.5, 0.0, -5.0, 6.5, 3.0, "plaster_cream", skip=("bottom",)); p.box(-0.5, -3.0, 0.0, 0.5, -1.4, 1.9, "paint_red", skip=("bottom",))
        return p
    prism_y(p, [(-4.6, 4.0), (4.6, 4.0), (4.6, 4.15), (0.0, 4.9), (-4.6, 4.15)], -5.8, 0.8, "roof_zinc")                                                                       # pitched canopy (gable profile along Y)
    if lod == 0:
        for (x, y) in ((-3.8, -5.0), (3.8, -5.0), (-3.8, 0.0), (3.8, 0.0)): p.tube((x, y, 0.1), (x, y, 4.05), 0.09, 0.09, 8, "paint_red", caps=(False, False))
        p.box(-4.6, -5.85, 3.6, 4.6, -5.75, 4.05, "paint_white", skip=())                                                                                                    # fascia board
        for x in (-2.0, 2.0): p.box(x - 0.3, -2.8, 3.96, x + 0.3, -2.2, 4.0, GLOW, skip=("top",))
    else:
        for (x, y) in ((-3.8, -5.0), (3.8, -5.0), (-3.8, 0.0), (3.8, 0.0)): p.box(x - 0.09, y - 0.09, 0.1, x + 0.09, y + 0.09, 4.05, "paint_red", skip=("bottom", "top"))
    island(p, 0.0, -4.0, -0.6, lod, n_disp=2, old=True, barrier=False, ends="paint_red")
    # cashier office with a painted board on its roof
    p.box(-9.0, 2.5, 0.0, -5.0, 6.5, 3.0, "plaster_cream", skip=("bottom",))
    if lod == 0:
        p.box(-8.2, 2.45, 1.0, -6.6, 2.5, 2.0, "window_plain", skip=("back",)); p.box(-6.2, 2.45, 0.0, -5.4, 2.5, 2.1, "metal_gate_blue", skip=("back",))
        for k in range(5): bx = -8.1 + 0.35 * k; p.box(bx, 2.4, 1.0, bx + 0.04, 2.44, 2.0, BLK, skip=("back",))                                                                 # burglar bars
        p.box(-9.1, 2.4, 3.0, -4.9, 6.6, 3.12, CON, skip=())
        p.box(-8.8, 3.3, 3.12, -5.2, 3.45, 4.3, S_FILL, skip=("bottom",)); p.box(-8.8, 3.45, 3.12, -5.2, 3.5, 4.3, "paint_white", skip=("bottom", "front"))
        p.box(-4.7, -7.6, 0.0, -4.62, -7.5, 1.8, GAL, skip=("bottom",)); p.box(-3.4, -7.6, 0.0, -3.32, -7.5, 1.8, GAL, skip=("bottom",))                                      # price board on legs
        p.box(-4.8, -7.68, 0.7, -3.22, -7.6, 1.8, S_HAND, skip=("back",)); p.box(-4.8, -7.6, 0.7, -3.22, -7.55, 1.8, "paint_white", skip=("front",))
        for k, key in enumerate(("paint_blue_bus", "paint_red", "paint_blue_bus")):                                                                                          # oil drums
            p.tube((5.4 + 0.65 * k, 4.2, 0.1), (5.4 + 0.65 * k, 4.2, 0.98), 0.29, 0.29, 12, key, caps=(False, True))
        for k in range(6): p.box(5.2 + 0.36 * (k % 3), 5.2 + 0.3 * (k // 3), 0.1, 5.5 + 0.36 * (k % 3), 5.45 + 0.3 * (k // 3), 0.55, "warning_yellow", skip=("bottom",))    # jerry cans
        p.box(-4.6, 2.9, 0.1, -3.6, 4.6, 0.55, "timber_weathered", skip=("bottom",)); p.box(-4.6, 4.5, 0.55, -3.6, 4.6, 1.0, "timber_weathered", skip=())                    # bench
        p.box(2.0, 5.2, 0.1, 3.6, 6.2, 1.2, "paint_green_dark", skip=("bottom",)); p.tube((3.4, 6.0, 1.2), (3.4, 6.0, 1.9), 0.06, 0.06, 6, BLK, caps=(False, True))           # generator
        p.box(-2.0, 5.6, 0.1, 0.8, 6.0, 1.6, S_OIL, skip=("bottom",))                                                                                                         # engine-oil display rack
        tank_fill(p, 1.5, 2.0, n=2, lod=lod); vents(p, 9.0, 7.6, n=2, h=3.8)
    wall_run(p, -10.0, 7.9, 10.0, 7.9, 1.8, lod, key="plaster_weathered", pier=3.0)
    return p


extra("kano_fuel_station_large_a", "G", "CITY_SPECIFIC", REF_FUEL, heads=[(-9.5, -6.0, 5.7), (-2.5, -6.0, 5.7), (4.5, -6.0, 5.7), (11.5, -6.0, 5.7), (-13.0, 2.5, 3.0), (19.5, -16.0, 8.5)],
      preset="glow", tags=["fuel_station", "commercial", "roadside_service", "fictional_brand", "canopy", "shop"], intensity=1.2, range=14, poolRadius=5.0,
      sockets=[("pump_1", "fuel_pump", (-8.4, -6.0, 0.0), 90), ("pump_2", "fuel_pump", (-1.4, -6.0, 0.0), 90), ("pump_3", "fuel_pump", (5.6, -6.0, 0.0), 90), ("pump_4", "fuel_pump", (12.6, -6.0, 0.0), 90),
               ("entry", "drive_in", (-19.0, -18.0, 0.0), 0), ("exit", "drive_out", (19.0, -18.0, 0.0), 180)])
extra("kano_fuel_station_medium_a", "G", "CITY_SPECIFIC", REF_FUEL, heads=[(-3.0, -5.0, 5.3), (5.0, -5.0, 5.3), (-10.5, 2.5, 2.8), (8.0, 7.0, 3.9)], preset="glow",
      tags=["fuel_station", "commercial", "roadside_service", "fictional_brand", "canopy", "shop", "car_wash"], intensity=1.1, range=12, poolRadius=4.5,
      sockets=[("pump_1", "fuel_pump", (-1.9, -5.0, 0.0), 90), ("pump_2", "fuel_pump", (6.1, -5.0, 0.0), 90), ("entry", "drive_in", (-13.0, -13.0, 0.0), 0), ("exit", "drive_out", (13.0, -13.0, 0.0), 180)])
extra("rg_ngn_fuel_station_small_a", "G", "REGIONAL", REF_FUEL + ["R-016"], heads=[(-2.0, -2.5, 3.9), (2.0, -2.5, 3.9), (-7.0, 2.3, 2.6)], preset="glow",
      tags=["fuel_station", "roadside_service", "independent", "generic_words", "canopy"], intensity=0.9, range=10, poolRadius=4.0,
      sockets=[("pump_1", "fuel_pump", (1.1, -2.3, 0.0), 90), ("entry", "drive_in", (-6.0, -8.0, 0.0), 0), ("exit", "drive_out", (6.0, -8.0, 0.0), 180)])
