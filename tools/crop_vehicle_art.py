#!/usr/bin/env python3
"""Cuts the real (photographic) vehicle pictures out of the approved ARO Motors sheets and the Trucks & Buses mockup into Assets/_Project/Resources/UI/.
The sheets are not stored in the repository; pass them:
    python3 tools/crop_vehicle_art.py <lineup.png (1536x1024 line-up sheet)> <titan.png (1536x1024 TITAN 480 sheet)> <screen.png (1672x941 Trucks & Buses mockup)>
Outputs per vehicle key K (the vehicle_definitions.picture_key):
    veh_K.jpg        catalogue card picture
    veh_K_hero.jpg   showroom picture (the vehicle centred over a blurred backdrop of itself)
    veh_K_v1..v5.jpg the real view pictures the sheet shows (front / side / rear ...), where the sheet has them
    veh_aro_titan_480_int.jpg   the driver cabin
Crops are upscaled with Lanczos + a light unsharp mask; nothing is drawn or invented. Boxes are pixel coordinates of the sheets."""
import os, sys
from PIL import Image, ImageFilter

LINE, TITAN, SCREEN = [Image.open(p).convert("RGB") for p in sys.argv[1:4]]
OUT = sys.argv[4] if len(sys.argv) > 4 else "Assets/_Project/Resources/UI"

def up(im, scale):
    im = im.resize((max(1, round(im.width * scale)), max(1, round(im.height * scale))), Image.LANCZOS)
    return im.filter(ImageFilter.UnsharpMask(radius=1.4, percent=55, threshold=2))

def save(im, name, q=88): im.save(os.path.join(OUT, name), quality=q, optimize=True); print(name, im.size)

def card(im, w=380, h=300):
    """the whole vehicle inside the card picture (no side cut off), on a blurred backdrop of itself where the aspect differs"""
    return hero(im, w, h)

def hero(im, W=1400, H=510):
    """the vehicle at full height in the middle, feathered into a dark blurred backdrop of itself"""
    fg = up(im, H / im.height)
    if fg.width > W: fg = fg.resize((W, round(fg.height * W / fg.width)), Image.LANCZOS)
    bg = Image.eval(im.resize((W, H), Image.LANCZOS).filter(ImageFilter.GaussianBlur(26)), lambda v: int(v * 0.5))
    mask = Image.new("L", fg.size, 255); px = mask.load(); ramp = max(10, int(fg.width * 0.16))
    for x in range(ramp):
        a = int(255 * x / ramp)
        for y in range(fg.height): px[x, y] = a; px[fg.width - 1 - x, y] = a
    bg.paste(fg, ((W - fg.width) // 2, (H - fg.height) // 2), mask)
    return bg

# ------------------------------------------------------------------ ARO line-up sheet: hero box and the three view boxes (front / side / rear) of each model
LU = {
  "aro_hauler_620":   ((1024, 150, 1278, 342), [(1278, 165, 1355, 278), (1358, 165, 1455, 278), (1458, 165, 1530, 278)]),
  "aro_cityliner_45": ((206, 366, 506, 562),   [(510, 415, 595, 512), (598, 398, 780, 500), (783, 398, 865, 512)]),
  "aro_urban_32":     ((1034, 380, 1264, 548), [(1268, 412, 1338, 500), (1342, 412, 1455, 500), (1458, 412, 1525, 500)]),
  "aro_runner_300":   ((192, 562, 432, 748),   [(441, 598, 525, 702), (530, 598, 680, 692), (686, 598, 785, 712)]),
  "aro_transit_16":   ((948, 600, 1216, 746),  [(1228, 608, 1300, 702), (1310, 608, 1440, 692), (1445, 618, 1520, 710)]),
  "aro_ancora":       ((58, 804, 356, 904),    [(118, 912, 185, 982), (188, 912, 290, 982), (295, 912, 352, 982)]),
  "aro_nomad":        ((452, 782, 648, 902),   [(385, 908, 458, 982), (462, 908, 578, 982), (582, 908, 640, 982)]),
  "aro_citygo":       ((748, 796, 945, 892),   [(672, 910, 746, 982), (753, 910, 867, 982), (872, 910, 940, 982)]),
  "aro_cargo_van":    ((1072, 790, 1275, 896), [(995, 910, 1060, 982), (1066, 910, 1180, 982), (1188, 910, 1252, 982)]),
  "aro_ride_150":     ((1378, 782, 1536, 892), [(1285, 908, 1345, 988), (1370, 908, 1478, 982), (1480, 908, 1530, 988)]),
}
for k, (hb, views) in LU.items():
    h = LINE.crop(hb)
    save(card(h), f"veh_{k}.jpg"); save(hero(h), f"veh_{k}_hero.jpg")
    for i, vb in enumerate(views, 1): save(up(LINE.crop(vb), 2.0), f"veh_{k}_v{i}.jpg")

# ------------------------------------------------------------------ TITAN 480 sheet (larger pictures): hero, five views, driver cabin
def clear_title(img):
    """The sheet's title ("480", "TRACTOR UNIT") sits on a smooth dark dusk sky just left of the cab. Continue the clean sky of each row over the letters."""
    img = img.copy(); px = img.load()
    for y in range(22, 93):
        src_x, x_end = (497, 492) if y <= 66 else (470, 466)
        c = px[src_x, y]
        for x in range(380, x_end + 1): px[x, y] = c
    patch = img.crop((380, 22, 493, 93)).filter(ImageFilter.GaussianBlur(3)); img.paste(patch, (380, 22))
    return img
t = clear_title(TITAN).crop((425, 5, 1065, 465))
save(card(t), "veh_aro_titan_480.jpg"); save(hero(t), "veh_aro_titan_480_hero.jpg")
for i, vb in enumerate([(3, 478, 252, 708), (258, 478, 490, 708), (493, 478, 967, 708), (971, 478, 1213, 708), (1216, 478, 1533, 708)], 1):
    save(up(TITAN.crop(vb), 1.0), f"veh_aro_titan_480_v{i}.jpg")
save(up(TITAN.crop((3, 738, 607, 1010)), 1.0), "veh_aro_titan_480_int.jpg")

# ------------------------------------------------------------------ the three vehicles that exist in the game today: nearest-class cards of the Trucks & Buses mockup
# (about 100 px square in the mockup, so these are the softest pictures; no photograph of those exact models exists yet)
for k, box in {"aro_light_180": (1205, 638, 1304, 735), "aro_cargo_260": (975, 638, 1074, 735), "aro_city_bus": (515, 798, 619, 901)}.items():
    c = SCREEN.crop(box)
    save(card(c), f"veh_{k}.jpg"); save(hero(c), f"veh_{k}_hero.jpg")
