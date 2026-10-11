#!/usr/bin/env python3
"""Cuts the real (photographic) cargo, truck and convoy pictures out of the project owner's approved mockups into Assets/_Project/Resources/UI/.
The mockups are not stored in the repository; pass the folder that holds them:
    python3 tools/crop_ref_art.py <dir with jobs.jpg (Jobs mockup), multiplayer.jpg (Multiplayer mockup), dashboard.jpg (dashboard concept 1672x941), dashboard2.webp (alt concept 1536x1024)>
Outputs: job_<key>.jpg (card / row picture, native aspect), job_<key>_hero.jpg (1400x388 hero: the picture on the right over a blurred backdrop of itself),
         convoy_a|b|c.jpg (convoy list pictures), mp_banner.jpg (1400x325 banner).
Crops are upscaled with Lanczos + a light unsharp mask; no picture is invented. The crop rectangles are the mockup pixel coordinates."""
import os, sys
from PIL import Image, ImageFilter

SRC = sys.argv[1]; OUT = sys.argv[2] if len(sys.argv) > 2 else "Assets/_Project/Resources/UI"
def load(n): return Image.open(os.path.join(SRC, n)).convert("RGB")
jobs, mp, dash, dash2 = load("jobs.jpg"), load("multiplayer.jpg"), load("dashboard.jpg"), load("dashboard2.webp")

def up(im, scale):
    im = im.resize((round(im.width * scale), round(im.height * scale)), Image.LANCZOS)
    return im.filter(ImageFilter.UnsharpMask(radius=1.6, percent=60, threshold=2))

def save(im, name, q=90): im.save(os.path.join(OUT, name), quality=q, optimize=True); print(name, im.size)

# --- cargo pictures: the six rows of the Jobs mockup (x 270..460), plus the dashboard concept for general freight
rows = {"containers": (245, 352), "cement": (371, 479), "tanker": (497, 604), "pipes": (621, 728), "food": (736, 831), "machinery": (840, 934)}
thumb = {k: jobs.crop((270, a, 460, b)) for k, (a, b) in rows.items()}
thumb["general"] = dash2.crop((268, 628, 456, 698))                      # crated freight (Featured Jobs card of the dashboard concept)
thumb["vehicles"] = dash.crop((705, 140, 1085, 345))                    # an ARO tractor-trailer on the highway: no car-carrier picture exists in the mockups yet
hero_src = {k: v for k, v in thumb.items()}
hero_src["containers"] = jobs.crop((1200, 246, 1540, 443))              # the hero of the Jobs mockup (right of its title and route text)

for k, im in thumb.items():
    save(up(im, 3.0), f"job_{k}.jpg")

def compose(im, W, H):
    """The picture at full height on the right, feathered into a dark blurred backdrop of itself (text sits on the left)."""
    fg = up(im, H / im.height)
    bg = Image.eval(im.resize((W, H), Image.LANCZOS).filter(ImageFilter.GaussianBlur(22)), lambda v: int(v * 0.55))
    mask = Image.new("L", fg.size, 255); px = mask.load(); ramp = max(8, int(fg.width * 0.22))
    for x in range(ramp):
        a = int(255 * x / ramp)
        for y in range(fg.height): px[x, y] = a
    bg.paste(fg, (W - fg.width, 0), mask)
    return bg

for k, im in hero_src.items():
    save(compose(im, 1400, 388), f"job_{k}_hero.jpg")

# --- the player's truck (dashboard concept, Your Truck card)
save(up(dash.crop((1290, 300, 1458, 391)), 4.0), "truck_photo.jpg")

# --- convoy list pictures (Multiplayer mockup rows 1, 2 and 4) and the banner
for name, (a, b) in {"a": (515, 607), "b": (621, 714), "c": (832, 924)}.items():
    save(up(mp.crop((270, a, 417, b)), 3.0), f"convoy_{name}.jpg")
save(compose(mp.crop((605, 235, 1010, 452)), 1400, 325), "mp_banner.jpg")
