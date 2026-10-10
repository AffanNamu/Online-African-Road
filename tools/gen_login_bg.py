#!/usr/bin/env python3
"""Procedural sunset road scene for the login screen -> Assets/_Project/Resources/UI/login_bg.jpg (1920x1080).
Placeholder art, fully generated (no third-party imagery). Drop a hand-made Resources/UI/login_bg.jpg over it to replace.
Deterministic: same output every run."""
import math, sys
import numpy as np
from PIL import Image, ImageDraw, ImageFilter

W, H = 1920, 1080
rng = np.random.default_rng(7)
HORIZON = 640
SUN = (900, 585)

def smooth(t): return t * t * (3 - 2 * t)

def noise2d(w, h, cell, seed):
    r = np.random.default_rng(seed)
    gw, gh = w // cell + 3, h // cell + 3
    g = r.random((gh, gw)).astype(np.float32)
    ys, xs = np.mgrid[0:h, 0:w].astype(np.float32)
    gx, gy = xs / cell, ys / cell
    x0, y0 = gx.astype(int), gy.astype(int)
    fx, fy = smooth(gx - x0), smooth(gy - y0)
    a = g[y0, x0] * (1 - fx) + g[y0, x0 + 1] * fx
    b = g[y0 + 1, x0] * (1 - fx) + g[y0 + 1, x0 + 1] * fx
    return a * (1 - fy) + b * fy

def fbm(w, h, cell, octaves, seed, sx=1.0):
    out = np.zeros((h, w), np.float32); amp = 1.0; tot = 0.0
    for o in range(octaves):
        c = max(2, int(cell / (2 ** o)))
        n = noise2d(w, h, c, seed + o * 17)
        out += n * amp; tot += amp; amp *= 0.5
    return out / tot

def ridge(width, base, amp, cell, seed, octaves=5):
    r = np.zeros(width, np.float32); a = 1.0; tot = 0.0
    for o in range(octaves):
        c = max(4, int(cell / (2 ** o)))
        g = np.random.default_rng(seed + o).random(width // c + 3).astype(np.float32)
        x = np.arange(width) / c; i = x.astype(int); f = smooth(x - i)
        r += (g[i] * (1 - f) + g[i + 1] * f) * a; tot += a; a *= 0.5
    return base - (r / tot) * amp

# ---------------- sky
ys = np.linspace(0, 1, H, dtype=np.float32)[:, None]
xs = np.linspace(0, 1, W, dtype=np.float32)[None, :]
stops = [(0.00, (22, 48, 84)), (0.28, (44, 82, 124)), (0.46, (178, 108, 98)), (0.56, (244, 142, 60)), (0.60, (255, 196, 96)), (1.0, (255, 214, 140))]
def grad(t):
    t = np.clip(t, 0, 1); out = np.zeros(t.shape + (3,), np.float32)
    for (t0, c0), (t1, c1) in zip(stops[:-1], stops[1:]):
        m = (t >= t0) & (t <= t1); f = ((t - t0) / (t1 - t0))[m][:, None]
        out[m] = np.array(c0) * (1 - f) + np.array(c1) * f
    return out
sky = grad(np.broadcast_to(ys / (HORIZON / H), (H, W)).copy())
# sun glow
yy, xx = np.mgrid[0:H, 0:W].astype(np.float32)
d = np.hypot(xx - SUN[0], (yy - SUN[1]) * 1.15)
glow = np.exp(-d / 260.0)[..., None] * np.array([255, 170, 70]) * 0.85 + np.exp(-d / 70.0)[..., None] * np.array([255, 235, 190]) * 0.9
sky = sky + glow * (yy < HORIZON + 80)[..., None]
# clouds: streaky fbm, lit from below by the sun
cl = fbm(W, H, 220, 6, 11); cl2 = fbm(W, H, 90, 5, 31)
streak = np.array(Image.fromarray((cl * 255).astype(np.uint8)).resize((W, H)).filter(ImageFilter.GaussianBlur(2)), np.float32) / 255
cover = np.clip((cl * 0.75 + cl2 * 0.35 - 0.52) * 4.0, 0, 1) * np.clip(1.15 - ys / (HORIZON / H) * 0.9, 0, 1)
cover *= np.clip((ys / (HORIZON / H)) * 6, 0, 1)
below_sun = np.clip(1.0 - np.hypot((xx - SUN[0]) / 1400, (yy - SUN[1]) / 700), 0, 1)[..., None]
lit = np.array([255, 150, 60], np.float32) * (0.35 + below_sun * 1.0)
shade = np.array([104, 84, 122], np.float32)
edge = np.clip(1 - cover * 1.0, 0, 1)[..., None]
cloudcol = shade * (1 - below_sun * 0.55) * (1 - edge * 0.3) + lit * edge * below_sun
sky = sky * (1 - cover[..., None] * 0.80) + cloudcol * cover[..., None] * 0.80
# sun disc
sky += (np.exp(-np.clip(d - 26, 0, None) / 6.0) * (d < 60))[..., None] * np.array([255, 250, 230]) * 0.9
img = sky.copy()

# ---------------- distant mountains (atmospheric perspective)
def mountain_layer(base, amp, cell, seed, col, haze):
    r = ridge(W, base, amp, cell, seed)
    mask = yy >= r[None, :]
    c = np.array(col, np.float32) * (1 - haze) + np.array([240, 150, 80], np.float32) * haze
    return mask, c
for base, amp, cell, seed, col, haze in [(HORIZON - 40, 150, 260, 5, (92, 78, 108), 0.62), (HORIZON - 10, 130, 180, 9, (66, 58, 84), 0.45), (HORIZON + 8, 90, 120, 21, (40, 38, 58), 0.30)]:
    m, c = mountain_layer(base, amp, cell, seed, col, haze)
    img = np.where(m[..., None] & (yy < HORIZON + 60)[..., None], c[None, None, :], img)

# ---------------- lagoon (reflects the sky, with sun glitter)
lag = (yy >= HORIZON) & (yy < HORIZON + 130)
refl = img[np.clip((2 * HORIZON - yy).astype(int), 0, H - 1), xx.astype(int)]
ripple = fbm(W, H, 14, 3, 77)
glitter = np.exp(-np.abs(xx - SUN[0] + (ripple - 0.5) * 120) / np.maximum(40 + (yy - HORIZON) * 0.8, 1.0)) * np.clip(1 - (yy - HORIZON) / 140, 0, 1)
water = refl * 0.55 + np.array([40, 60, 90], np.float32) * 0.2 + glitter[..., None] * np.array([255, 190, 110]) * (0.5 + ripple[..., None] * 0.9)
img = np.where(lag[..., None], water, img)

# ---------------- city skyline across the water
im = Image.fromarray(np.clip(img, 0, 255).astype(np.uint8)); dr = ImageDraw.Draw(im, "RGBA")
r2 = np.random.default_rng(3); x = 560
while x < 1500:
    bw = int(r2.integers(8, 26)); bh = int(r2.integers(14, 70) * (1.8 - abs(x - 1000) / 600))
    bh = max(10, bh); col = (52, 46, 66, 235)
    dr.rectangle([x, HORIZON + 12 - bh, x + bw, HORIZON + 14], fill=col)
    for wy in range(HORIZON + 16 - bh, HORIZON + 8, 6):
        for wx in range(x + 2, x + bw - 2, 5):
            if r2.random() < 0.35: dr.point((wx, wy), fill=(255, 205, 120, 230))
    x += bw + int(r2.integers(1, 6))
img = np.array(im, np.float32)

# ---------------- foreground land: dark green slopes left and right of the road
def slope(side):
    yb = ridge(W, HORIZON + 60, 40, 300, 41 if side < 0 else 53)
    return yb
land = np.zeros((H, W), bool)
left_edge = HORIZON + 40 + (1 - np.clip(xx / 1100, 0, 1)) * 0
tree = fbm(W, H, 40, 4, 91)
veg = np.array([22, 44, 30], np.float32) * (0.7 + tree[..., None] * 0.8)
rim = np.exp(-np.abs(xx - SUN[0]) / 900)[..., None] * np.array([120, 80, 30], np.float32) * (tree[..., None] ** 2)
veg = veg + rim * 0.5
shore = HORIZON - 70 + np.clip(xx / 900, 0, 1) ** 1.3 * 130
_t = np.clip((yy - (HORIZON + 4)) / (H - (HORIZON + 4)), 0, 1)
_cx = 1040 + (1 - _t) ** 2 * 260 - 40 * _t; _half = 40 + 760 * _t
land_l = (xx < _cx - _half - 2) & (yy > shore + (ridge(W, 0, 60, 90, 71)[None, :] * 0.6))
img = np.where(land_l[..., None], veg, img)

# ---------------- road (perspective), guardrail, markings
VP = (1040, HORIZON + 4)
def road_x(y, off):  # screen x of a lateral offset at row y; road centre curves gently
    t = (y - VP[1]) / (H - VP[1]); curve = (1 - t) ** 2 * 140
    return VP[0] + curve + off * t
rd = np.zeros((H, W, 3), np.float32); mask = np.zeros((H, W), bool)
t = np.clip((yy - VP[1]) / (H - VP[1]), 0, 1)
cx = VP[0] + (1 - t) ** 2 * 260 - 40 * t
half = 40 + 760 * t
inroad = (yy > VP[1]) & (np.abs(xx - cx) < half)
asp = np.array([34, 33, 38], np.float32) * (0.8 + fbm(W, H, 6, 3, 5)[..., None] * 0.5)
wet = np.exp(-np.abs(xx - SUN[0] - (cx - VP[0]) * 0.2) / (60 + 260 * t)) * np.clip(t * 1.4, 0, 1)
asp = asp + wet[..., None] * np.array([255, 150, 60], np.float32) * (0.35 + 0.65 * fbm(W, H, 18, 3, 8)[..., None])
img = np.where(inroad[..., None], asp, img)
# lane markings: edge lines + dashed centre
for off, wid in [(-0.90, 0.025), (0.90, 0.025)]:
    m = inroad & (np.abs(xx - cx - off * half) < np.maximum(wid * half, 1.0))
    img = np.where(m[..., None], np.array([220, 205, 170], np.float32) * (0.55 + 0.45 * np.clip(t * 2, 0, 1)[..., None]), img)
dash = (np.sin((1.0 / np.maximum(t, 0.02)) * 3.2) > 0.2)
m = inroad & (np.abs(xx - cx) < np.maximum(0.012 * half, 1.0)) & dash & (t > 0.04)
img = np.where(m[..., None], np.array([240, 190, 90], np.float32), img)
# verge beyond the road edge (right side hillside + gravel)
ver = (yy > VP[1]) & (xx >= cx + half)
img = np.where(ver[..., None], np.array([54, 52, 40], np.float32) * (0.6 + tree[..., None] * 0.8), img)
# guardrail on the right: a steel band along the road edge
gx = cx + half * 1.06
band = (np.abs(xx - gx) < 3 + 18 * t) & (yy > VP[1] + 6)
img = np.where(band[..., None], np.array([118, 98, 92], np.float32) * (0.5 + 0.8 * t[..., None]), img)

im = Image.fromarray(np.clip(img, 0, 255).astype(np.uint8)); dr = ImageDraw.Draw(im, "RGBA")
# guardrail posts
for k in range(60):
    tt = (k / 60.0) ** 2.2; y = VP[1] + 10 + tt * (H - VP[1]); tc = (y - VP[1]) / (H - VP[1])
    gxp = VP[0] + (1 - tc) ** 2 * 260 - 40 * tc + (40 + 760 * tc) * 1.06
    dr.rectangle([gxp - 1 - 4 * tc, y - 22 * tc - 4, gxp + 1 + 4 * tc, y + 4], fill=(40, 34, 36, 220))
# ---------------- palms + shrubs silhouettes (left foreground and right edge)
def palm(x, y, s, lean):
    pts = [(x + lean * ((i / 15) ** 1.6) * s * 0.35, y - (i / 15) * s) for i in range(16)]
    dr.line(pts, fill=(12, 18, 14, 255), width=max(3, int(s * 0.05)))
    tx, ty = pts[-1]
    for a in range(-170, 10, 22):
        ang = math.radians(a); L = s * 0.46
        ex, ey = tx + math.cos(ang) * L, ty + math.sin(ang) * L * 0.35 + L * 0.42 * (abs(math.cos(ang)) ** 1.2)
        mx, my = tx + math.cos(ang) * L * 0.55, ty + math.sin(ang) * L * 0.5 - L * 0.12
        curve = [((1 - f) ** 2 * tx + 2 * (1 - f) * f * mx + f * f * ex, (1 - f) ** 2 * ty + 2 * (1 - f) * f * my + f * f * ey) for f in [i / 12 for i in range(13)]]
        dr.line(curve, fill=(12, 20, 14, 255), width=max(2, int(s * 0.028)))
        for i in range(2, 13):
            px, py = curve[i]; f = i / 12; ll = s * 0.11 * (1 - f * 0.55)
            for side in (-1, 1):
                dr.line([(px, py), (px + side * ll * 0.55, py + ll)], fill=(12, 20, 14, 255), width=max(1, int(s * 0.012)))
    for k in range(5):
        dr.ellipse([tx - s * 0.03 + k * 2, ty - 1 + k, tx + s * 0.03 + k * 2, ty + s * 0.05], fill=(30, 22, 14, 255))
for (x, y, s, l) in [(1120, 640, 70, 0.6), (1190, 650, 90, -0.4), (1400, 700, 160, 0.5), (1760, 720, 280, -0.6), (1860, 760, 330, 0.3), (60, 760, 300, 0.5), (220, 700, 190, 0.2)]:
    palm(x, y + 40, s, l)
im = im.filter(ImageFilter.GaussianBlur(0.6))

# ---------------- a truck seen from behind, driving away on the road (rear view, tail lights on)
dr = ImageDraw.Draw(im, "RGBA")
def rear_truck(tt, lane):
    y = VP[1] + tt * (H - VP[1]); half_ = 40 + 760 * tt
    cxx = VP[0] + (1 - tt) ** 2 * 260 - 40 * tt + lane * half_ * 0.45
    w = half_ * 0.40; h = w * 1.18
    # ground shadow
    dr.ellipse([cxx - w * 0.75, y - h * 0.05, cxx + w * 0.75, y + h * 0.07], fill=(0, 0, 0, 120))
    # wheels (rear axle pair, dark) and chassis
    dr.rectangle([cxx - w * 0.5, y - h * 0.16, cxx - w * 0.30, y], fill=(8, 8, 10, 255)); dr.rectangle([cxx + w * 0.30, y - h * 0.16, cxx + w * 0.5, y], fill=(8, 8, 10, 255))
    dr.rectangle([cxx - w * 0.5, y - h * 0.22, cxx + w * 0.5, y - h * 0.13], fill=(18, 18, 22, 255))
    # container body with a warm rim light on the sun side
    dr.rectangle([cxx - w * 0.52, y - h, cxx + w * 0.52, y - h * 0.22], fill=(34, 32, 38, 255))
    for k in range(1, 7):
        xk = cxx - w * 0.52 + k * (w * 1.04 / 7); dr.line([(xk, y - h * 0.98), (xk, y - h * 0.24)], fill=(24, 22, 28, 255), width=max(1, int(w * 0.012)))
    dr.rectangle([cxx - w * 0.52, y - h, cxx - w * 0.49, y - h * 0.22], fill=(255, 150, 70, 150))
    dr.rectangle([cxx - w * 0.52, y - h, cxx + w * 0.52, y - h * 0.97], fill=(255, 170, 90, 90))
    # tail lights + glow
    for sx in (-1, 1):
        lx = cxx + sx * w * 0.42
        for rr, al in [(w * 0.16, 40), (w * 0.10, 90), (w * 0.05, 255)]:
            dr.ellipse([lx - rr, y - h * 0.30 - rr, lx + rr, y - h * 0.30 + rr], fill=(255, 40, 30, al))
    dr.rectangle([cxx - w * 0.12, y - h * 0.28, cxx + w * 0.12, y - h * 0.24], fill=(230, 230, 220, 255))   # plate
rear_truck(0.34, -0.5)
rear_truck(0.13, 0.5)

# ---------------- atmosphere: bloom, vignette, grain, grade
a = np.array(im, np.float32)
bright = np.clip(a - 190, 0, None) * 1.2
bl = np.array(Image.fromarray(np.clip(bright, 0, 255).astype(np.uint8)).filter(ImageFilter.GaussianBlur(38)), np.float32)
a = a + bl * 1.3
v = 1 - 0.55 * np.clip(np.hypot((xx - W / 2) / (W * 0.75), (yy - H / 2) / (H * 0.8)), 0, 1) ** 2.2
a = a * v[..., None]
# darken the lower-left and right-centre so overlaid text/cards stay readable
a *= (1 - 0.35 * np.clip((yy - 700) / 380, 0, 1) * np.clip(1 - xx / 1200, 0, 1))[..., None]
a *= (1 - 0.28 * np.clip((xx - 1180) / 400, 0, 1))[..., None]
a += rng.normal(0, 3.0, a.shape).astype(np.float32)
a = np.clip(a, 0, 255) ** 1.0
out = sys.argv[1] if len(sys.argv) > 1 else "Assets/_Project/Resources/UI/login_bg.jpg"
Image.fromarray(a.astype(np.uint8)).save(out, quality=86, optimize=True)
print("wrote", out)
