#!/usr/bin/env python3
"""Generates the dashboard's placeholder pictures -> Assets/_Project/Resources/UI/: job_*.jpg, truck_thumb.png, avatar_default.png.
Deterministic, no third-party imagery. Replace any file with real art of the same name to override."""
import math, os, sys
import numpy as np
from PIL import Image, ImageDraw, ImageFilter

OUT = sys.argv[1] if len(sys.argv) > 1 else "Assets/_Project/Resources/UI"
os.makedirs(OUT, exist_ok=True)
rng = np.random.default_rng(11)

def fbm(w, h, cell, octaves, seed):
    out = np.zeros((h, w), np.float32); amp = 1.0; tot = 0.0
    for o in range(octaves):
        c = max(2, int(cell / (2 ** o))); r = np.random.default_rng(seed + o * 13)
        g = r.random((h // c + 3, w // c + 3)).astype(np.float32)
        ys, xs = np.mgrid[0:h, 0:w].astype(np.float32); gx, gy = xs / c, ys / c
        x0, y0 = gx.astype(int), gy.astype(int); fx, fy = gx - x0, gy - y0; fx = fx * fx * (3 - 2 * fx); fy = fy * fy * (3 - 2 * fy)
        a = g[y0, x0] * (1 - fx) + g[y0, x0 + 1] * fx; b = g[y0 + 1, x0] * (1 - fx) + g[y0 + 1, x0 + 1] * fx
        out += (a * (1 - fy) + b * fy) * amp; tot += amp; amp *= 0.5
    return out / tot

# ------------------------------------------------------------------ cargo scenes (480x200)
CW, CH = 480, 200
def scene(name, painter):
    ys = np.linspace(0, 1, CH, dtype=np.float32)[:, None, None]
    top = np.array([40, 70, 110], np.float32); hor = np.array([250, 170, 90], np.float32)
    sky = top * (1 - ys) + hor * ys; sky = np.broadcast_to(sky, (CH, CW, 3)).copy()
    xx = np.arange(CW)[None, :, None]
    sky += np.exp(-np.abs(xx - 330) / 120.0) * np.exp(-np.abs(ys - 0.65) / 0.25) * np.array([90, 50, 10], np.float32)
    im = Image.fromarray(np.clip(sky, 0, 255).astype(np.uint8)); d = ImageDraw.Draw(im, "RGBA")
    d.rectangle([0, 150, CW, CH], fill=(58, 52, 48, 255)); d.rectangle([0, 148, CW, 152], fill=(80, 70, 60, 255))
    painter(d); a = np.array(im, np.float32)
    a *= (1 - 0.35 * np.clip(np.hypot((np.arange(CW)[None, :] - CW / 2) / (CW * .8), (np.arange(CH)[:, None] - CH / 2) / (CH * .9)), 0, 1) ** 2)[..., None]
    a += rng.normal(0, 2.5, a.shape)
    Image.fromarray(np.clip(a, 0, 255).astype(np.uint8)).filter(ImageFilter.GaussianBlur(0.5)).save(f"{OUT}/{name}.jpg", quality=84)
def lit(c, f): return tuple(int(min(255, v * f)) for v in c) + (255,)
def container(d, x, y, w, h, c):
    d.rectangle([x, y, x + w, y + h], fill=lit(c, 0.85)); d.rectangle([x, y, x + 4, y + h], fill=lit(c, 1.25))
    for k in range(1, int(w / 6)): d.line([(x + k * 6, y + 2), (x + k * 6, y + h - 2)], fill=lit(c, 0.65), width=1)
    d.rectangle([x, y, x + w, y + 3], fill=lit(c, 1.1))
def containers(d):
    cols = [(30, 120, 140), (200, 90, 40), (160, 40, 40), (50, 80, 150), (210, 170, 50), (90, 120, 70)]; r = np.random.default_rng(3)
    for row in range(3):
        x = 12 + row * 14
        for k in range(5 - row // 2):
            c = cols[int(r.integers(0, len(cols)))]; container(d, x, 148 - 30 * (row + 1), 78, 28, c); x += 82
    d.line([(420, 148), (420, 30)], fill=(30, 28, 34, 255), width=6); d.line([(380, 34), (470, 34)], fill=(30, 28, 34, 255), width=5); d.line([(400, 34), (400, 110)], fill=(20, 20, 24, 200), width=1)
def tanker(d):
    d.rounded_rectangle([60, 88, 340, 138], radius=24, fill=(150, 150, 158, 255)); d.rounded_rectangle([60, 88, 340, 106], radius=10, fill=(215, 215, 222, 255))
    d.rectangle([340, 100, 412, 138], fill=(36, 40, 52, 255)); d.polygon([(340, 104), (390, 104), (412, 126), (340, 126)], fill=(60, 70, 90, 255))
    d.rectangle([40, 136, 412, 144], fill=(28, 28, 32, 255))
    for cx in (96, 128, 300, 340, 384): d.ellipse([cx - 13, 130, cx + 13, 156], fill=(14, 14, 16, 255)); d.ellipse([cx - 6, 137, cx + 6, 149], fill=(120, 120, 126, 255))
    d.rectangle([402, 118, 410, 124], fill=(255, 230, 160, 255))
def pipes(d):
    r = 18
    for row in range(4):
        for k in range(8 - row):
            cx = 60 + k * 37 + row * 18; cy = 140 - row * 32
            d.ellipse([cx - r, cy - r, cx + r, cy + r], fill=(150, 146, 140, 255)); d.ellipse([cx - r + 6, cy - r + 6, cx + r - 6, cy + r - 6], fill=(30, 28, 28, 255))
            d.arc([cx - r, cy - r, cx + r, cy + r], 200, 300, fill=(215, 205, 190, 255), width=3)
def crates(d):
    r = np.random.default_rng(5)
    for row in range(3):
        for k in range(7 - row):
            x = 20 + k * 62 + row * 30; y = 148 - (row + 1) * 36
            d.rectangle([x, y, x + 58, y + 34], fill=(160, 112, 60, 255)); d.rectangle([x, y, x + 58, y + 4], fill=(205, 150, 84, 255))
            for p in range(1, 4): d.line([(x, y + p * 8 + 4), (x + 58, y + p * 8 + 4)], fill=(110, 74, 38, 255), width=1)
            d.line([(x + 2, y), (x + 56, y + 34)], fill=(120, 80, 40, 255), width=2)
    for k in range(5): d.ellipse([360 + k * 20, 130, 384 + k * 20, 152], fill=(206, 188, 140, 255))
def general(d):
    r = np.random.default_rng(8)
    for k in range(9):
        w = int(r.integers(40, 70)); h = int(r.integers(30, 60)); x = 14 + k * 50
        c = [(176, 130, 80), (120, 140, 160), (190, 160, 90), (130, 90, 70)][k % 4]; d.rectangle([x, 148 - h, x + w, 148], fill=lit(c, 0.9)); d.rectangle([x, 148 - h, x + w, 152 - h], fill=lit(c, 1.2))
for n, f in [("job_containers", containers), ("job_tanker", tanker), ("job_pipes", pipes), ("job_food", crates), ("job_general", general)]: scene(n, f)

# ------------------------------------------------------------------ truck thumbnail (side view, transparent)
S = 4; tw, th = 240 * S, 140 * S; im = Image.new("RGBA", (tw, th), (0, 0, 0, 0)); d = ImageDraw.Draw(im)
def R(a, b, c, e, **kw): d.rectangle([a * S, b * S, c * S, e * S], **kw)
R(14, 34, 150, 100, fill=(38, 44, 60, 255)); R(14, 34, 150, 40, fill=(70, 82, 110, 255))
for k in range(1, 19): d.line([((14 + k * 7.5) * S, 42 * S), ((14 + k * 7.5) * S, 98 * S)], fill=(30, 34, 46, 255), width=S)
d.polygon([(154 * S, 52 * S), (196 * S, 52 * S), (222 * S, 82 * S), (222 * S, 100 * S), (154 * S, 100 * S)], fill=(52, 58, 76, 255)); d.polygon([(160 * S, 58 * S), (192 * S, 58 * S), (210 * S, 80 * S), (160 * S, 80 * S)], fill=(120, 150, 190, 255))
R(10, 98, 226, 108, fill=(20, 20, 24, 255)); R(212, 88, 222, 94, fill=(255, 220, 140, 255))
for cx in (44, 66, 126, 148, 196): d.ellipse([(cx - 14) * S, 94 * S, (cx + 14) * S, 124 * S], fill=(14, 14, 16, 255)); d.ellipse([(cx - 6) * S, 102 * S, (cx + 6) * S, 116 * S], fill=(150, 150, 156, 255))
im.resize((240, 140), Image.LANCZOS).save(f"{OUT}/truck_thumb.png")

# ------------------------------------------------------------------ default avatar
N = 256; av = Image.new("RGBA", (N * 2, N * 2), (0, 0, 0, 0)); d = ImageDraw.Draw(av)
d.ellipse([0, 0, N * 2, N * 2], fill=(52, 60, 78, 255)); d.ellipse([N * 0.62, N * 0.36, N * 1.38, N * 1.12], fill=(190, 198, 214, 255)); d.ellipse([N * 0.2, N * 1.2, N * 1.8, N * 2.6], fill=(190, 198, 214, 255))
m = Image.new("L", (N * 2, N * 2), 0); ImageDraw.Draw(m).ellipse([0, 0, N * 2, N * 2], fill=255); av.putalpha(Image.composite(av.split()[3], Image.new("L", av.size, 0), m))
av.resize((N, N), Image.LANCZOS).save(f"{OUT}/avatar_default.png")

# contact sheet for review
names = ["job_containers", "job_tanker", "job_pipes", "job_food", "job_general"]
sheet = Image.new("RGB", (CW * 3, CH * 2 + 20), (20, 20, 24))
for i, n in enumerate(names): sheet.paste(Image.open(f"{OUT}/{n}.jpg"), ((i % 3) * CW, (i // 3) * CH))
t = Image.open(f"{OUT}/truck_thumb.png"); sheet.paste(t, (CW * 2 + 20, CH + 10), t)
sheet.save("/tmp/art_sheet.png"); print("art ->", OUT)
