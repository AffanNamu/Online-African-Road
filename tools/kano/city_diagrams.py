#!/usr/bin/env python3
"""Draws the Kano CITY master-plan diagrams from docs/kano/city/city_plan.json (planning diagrams, NOT concept art and NOT 3D):
  master_plan.png      the aerial master plan: districts, road hierarchy, junction types, verified and placeholder landmarks, old-city extent, ring road
  engine_gap.png       the same network coloured by what the current road engine can build
  streaming.png        fidelity zones and the LOD-ladder bands around a viewer in the densest hero district
  layout_<id>.png      district-level layouts for the hero districts that have concept references (D06, D08, D09, D10, D11)
Images are written to --out and are NOT committed (binary files stay out of the public repository).
Usage: python3 tools/kano/city_diagrams.py --out <dir>"""
import argparse, json, math
from pathlib import Path
from PIL import Image, ImageDraw, ImageFont

ROOT = Path(__file__).resolve().parents[2]
ap = argparse.ArgumentParser(); ap.add_argument("--out", required=True); a = ap.parse_args(); out = Path(a.out); out.mkdir(parents=True, exist_ok=True)
P = json.loads((ROOT / "docs/kano/city/city_plan.json").read_text())
try: F = ImageFont.truetype("DejaVuSans.ttf", 15); FB = ImageFont.truetype("DejaVuSans-Bold.ttf", 17); FS = ImageFont.truetype("DejaVuSans.ttf", 12); FT = ImageFont.truetype("DejaVuSans-Bold.ttf", 22)
except Exception: F = FB = FS = FT = ImageFont.load_default()
BG = (241, 237, 229)
KIND_COL = dict(gateway=(160, 175, 205), industrial=(175, 170, 190), transport=(190, 205, 170), institutional=(150, 190, 215), modern_residential=(180, 205, 235), commercial=(240, 215, 150),
                traditional=(228, 188, 150), landmark=(150, 205, 170), market=(235, 175, 130))
ROAD = dict(expressway=((60, 60, 70), 9), arterial_ring=((75, 75, 85), 7), arterial=((90, 90, 100), 6), collector=((130, 130, 135), 4), local=((160, 160, 160), 2), lane=((175, 160, 140), 2))
NODE_COL = dict(terminal=(90, 90, 90), gateway=(40, 90, 200), T=(110, 110, 110), cross4=(210, 120, 30), roundabout=(190, 40, 40), interchange=(140, 40, 160), gate_cross=(210, 120, 30))


def mapper(x0, z0, x1, z1, S, pad=40, top=70, right=0):
    W, H = int((x1 - x0) * S) + 2 * pad + right, int((z1 - z0) * S) + pad + top
    return W, H, (lambda x, z: ((x - x0) * S + pad, H - ((z - z0) * S + pad)))


def title(d, W, t, sub):
    d.text((20, 12), t, fill=(20, 20, 20), font=FT); d.text((20, 42), sub, fill=(80, 80, 80), font=F)


def ellipse_pts(cx, cz, a_, b_, n=120): return [(cx + a_ * math.cos(2 * math.pi * k / n), cz + b_ * math.sin(2 * math.pi * k / n)) for k in range(n + 1)]


def master(mode):
    S = 0.105; sx0, sz0, sx1, sz1 = P["studyArea"]["xzMin"][0], P["studyArea"]["xzMin"][1], P["studyArea"]["xzMax"][0], P["studyArea"]["xzMax"][1]
    W, H, T = mapper(sx0, sz0, sx1, sz1, S, right=330)
    im = Image.new("RGB", (W, H), BG); d = ImageDraw.Draw(im, "RGBA")
    d.rectangle([T(sx0, sz1), T(sx1, sz0)], outline=(150, 150, 150), width=2)
    # 1 km grid
    for g in range(int(sx0), int(sx1) + 1, 1000):
        d.line([T(g, sz0), T(g, sz1)], fill=(215, 211, 203), width=1); d.line([T(sx0, g), T(sx1, g)], fill=(215, 211, 203), width=1)
    rr = P["ringRoad"]; oc = P["oldCity"]
    d.rectangle([T(sx0, sz1), T(sx1, sz0)], fill=(205, 214, 190, 120))                                                        # Z-C: peri-urban fringe, terrain tint, baked skyline
    d.polygon([T(x, z) for x, z in ellipse_pts(rr["centre"][0], rr["centre"][1], rr["radiusM"], rr["radiusM"])], fill=(232, 224, 205, 255))   # Z-B: mixed urban filler inside the ring
    d.polygon([T(x, z) for x, z in ellipse_pts(oc["centre"][0], oc["centre"][1], *oc["semiAxesM"])], fill=(226, 196, 160, 255), outline=(170, 120, 80, 255))   # Z-B: old-city filler
    for g in range(int(sx0), int(sx1) + 1, 1000):
        d.line([T(g, sz0), T(g, sz1)], fill=(200, 196, 188, 70), width=1); d.line([T(sx0, g), T(sx1, g)], fill=(200, 196, 188, 70), width=1)
    d.text(T(2200, 4600), "Z-C peri-urban fringe", fill=(70, 90, 50), font=FS); d.text(T(1500, -3000), "Z-B mixed urban filler (kit-generated)", fill=(110, 95, 70), font=FS); d.text(T(-1500, 1650), "Z-B old-city filler", fill=(130, 90, 60), font=FS)
    for dist in P["districts"]:
        col = KIND_COL[dist["kind"]]; poly = [T(x, z) for x, z in dist["polygon"]]
        blocked = dist["phase"] >= 3 or dist["id"] == "D05"
        d.polygon(poly, fill=col + ((235,) if mode == "master" else (90,)), outline=(60, 60, 60, 255))
        px_ = [p[0] for p in poly]; py_ = [p[1] for p in poly]
        ly_ = (max(py_) - 22) if dist.get("parent") else (min(py_) + 4); d.rectangle([min(px_) + 4, ly_, min(px_) + 4 + 34, ly_ + 20], fill=(255, 255, 255, 200)); d.text((min(px_) + 7, ly_ + 2), dist["id"], fill=(20, 20, 20), font=FB)
        if mode == "master" and not dist.get("parent"): d.text((min(px_) + 6, max(py_) - 18), f"phase {dist['phase']}" + (" BLOCKED" if blocked else ""), fill=(150, 30, 20) if blocked else (30, 90, 40), font=FS)
    nodes = {n["id"]: n for n in P["nodes"]}
    for e in P["edges"]:
        col, w = ROAD[e["cls"]]
        if mode == "engine":
            col = (150, 150, 150) if e["cls"] in ("local", "lane") else (60, 60, 70)
        d.line([T(*nodes[e["a"]]["xz"]), T(*nodes[e["b"]]["xz"])], fill=col + (255,), width=w)
    for n in P["nodes"]:
        x, y = T(*n["xz"]); r = 6 if n["type"] not in ("T",) else 2.5; c = NODE_COL[n["type"]]
        if mode == "engine": c = (60, 160, 70) if n["type"] in ("terminal", "T", "gateway") else (200, 50, 40)
        d.ellipse([x - r, y - r, x + r, y + r], fill=c, outline=(255, 255, 255))
    for l in P["landmarks"]:
        x, y = T(*l["xz"]); ver = l["verified"]; nm = l["name"].split(" (")[0]; off = {"great_mosque": (11, -20), "gidan_rumfa": (11, -6), "kurmi_market": (-96, 4), "kofar_mata_dye_pits": (-150, -16), "dala_hill": (11, -9)}.get(l["id"], (11, -9))
        d.rectangle([x + off[0] - 2, y + off[1] - 1, x + off[0] + len(nm) * 6.6, y + off[1] + 14], fill=(255, 255, 255, 190))
        d.polygon([(x, y - 9), (x + 8, y + 6), (x - 8, y + 6)], fill=(30, 130, 60) if ver else (255, 255, 255), outline=(30, 90, 40), width=2); d.text((x + off[0], y + off[1]), nm, fill=(20, 60, 30), font=FS)
    # legend
    lx = W - 315; ly = 90
    if mode == "master":
        d.text((lx, ly - 30), "Districts (kind, hero zone Z-A)", fill=(20, 20, 20), font=FB)
        for k, c in KIND_COL.items(): d.rectangle([lx, ly, lx + 18, ly + 14], fill=c, outline=(60, 60, 60)); d.text((lx + 26, ly - 1), k.replace("_", " "), fill=(30, 30, 30), font=F); ly += 22
        ly += 12; d.text((lx, ly), "Road hierarchy", fill=(20, 20, 20), font=FB); ly += 28
        for k, (c, w) in ROAD.items(): d.line([(lx, ly + 7), (lx + 30, ly + 7)], fill=c, width=w); d.text((lx + 38, ly), k.replace("_", " "), fill=(30, 30, 30), font=F); ly += 22
        ly += 10; d.text((lx, ly), "Junction types", fill=(20, 20, 20), font=FB); ly += 28
        for k, c in NODE_COL.items(): d.ellipse([lx + 2, ly + 2, lx + 12, ly + 12], fill=c); d.text((lx + 26, ly - 1), k.replace("_", " "), fill=(30, 30, 30), font=F); ly += 22
        ly += 10; d.polygon([(lx + 8, ly), (lx + 16, ly + 14), (lx, ly + 14)], fill=(30, 130, 60), outline=(30, 90, 40)); d.text((lx + 26, ly - 1), "landmark, position VERIFIED", fill=(30, 30, 30), font=F); ly += 22
        d.polygon([(lx + 8, ly), (lx + 16, ly + 14), (lx, ly + 14)], fill=(255, 255, 255), outline=(30, 90, 40)); d.text((lx + 26, ly - 1), "landmark, UNVERIFIED placeholder", fill=(30, 30, 30), font=F)
        title(d, W, "KANO STUDY AREA KSA-1: AERIAL MASTER PLAN (draft for approval)", "10 x 10 km, north up, 1 km grid. INDICATIVE FICTIONAL layout; only the two green landmarks are verified positions. Old-city ellipse and ring road are indicative.")
    else:
        d.text((lx, ly - 30), "What the road engine can build today", fill=(20, 20, 20), font=FB)
        d.ellipse([lx + 2, ly + 2, lx + 12, ly + 12], fill=(60, 160, 70)); d.text((lx + 26, ly - 1), "supported: end, T junction*, gantry prop", fill=(30, 30, 30), font=F); ly += 22
        d.ellipse([lx + 2, ly + 2, lx + 12, ly + 12], fill=(200, 50, 40)); d.text((lx + 26, ly - 1), "needs engine work: four-way, roundabout,", fill=(30, 30, 30), font=F); d.text((lx + 26, ly + 17), "interchange, gate crossing (RB-01..03, RB-16)", fill=(30, 30, 30), font=F); ly += 56
        d.text((lx, ly), "* T junctions: 150 m apart, undivided,", fill=(60, 60, 60), font=FS); d.text((lx, ly + 16), "straight stub; and no network at all:", fill=(60, 60, 60), font=FS); d.text((lx, ly + 32), "one main spline per route (RB-16).", fill=(60, 60, 60), font=FS)
        ct = {}
        for n in P["nodes"]: ct[n["type"]] = ct.get(n["type"], 0) + 1
        ly += 70; d.text((lx, ly), "Junction counts in this plan", fill=(20, 20, 20), font=FB); ly += 26
        for k, v in sorted(ct.items(), key=lambda kv: -kv[1]): d.text((lx, ly), f"{k.replace('_', ' ')}: {v}", fill=(30, 30, 30), font=F); ly += 20
        title(d, W, "KANO KSA-1: ROAD NETWORK AGAINST THE CURRENT ENGINE", "The plan is a network; the engine builds one corridor with straight T-junction stubs. Nothing in the engine was changed.")
    im.save(out / ("master_plan.png" if mode == "master" else "engine_gap.png")); print("wrote", "master_plan.png" if mode == "master" else "engine_gap.png", im.size)


def streaming():
    S = 0.5; R = 1600; W, H, T = mapper(-R, -R, R, R, S, right=380)
    im = Image.new("RGB", (W, H), BG); d = ImageDraw.Draw(im, "RGBA"); st = P["streaming"]; edges = st["bandEdgesM"]
    cols = [(210, 60, 50, 150), (230, 140, 40, 130), (230, 200, 60, 110), (120, 190, 110, 100), (110, 160, 210, 90)]
    for i in range(4, -1, -1):
        r = edges[i + 1]; d.ellipse([T(-r, r), T(r, -r)], fill=cols[i], outline=(80, 80, 80, 200))
        d.text((T(0, 0)[0] + r * S * 0.62, T(0, 0)[1] - r * S * 0.62 - 8), f"{edges[i]}-{edges[i + 1]} m", fill=(20, 20, 20), font=FS)
    d.ellipse([T(-5, 5), T(5, -5)], fill=(0, 0, 0)); d.text((T(8, 0)[0], T(8, 0)[1] - 8), "player", fill=(0, 0, 0), font=FS)
    # the current engine: production prefabs exist only inside 2 chunk rings (a 1 km square)
    h = 500; d.rectangle([T(-h, h), T(h, -h)], outline=(30, 30, 160, 255), width=3); d.text((T(-h, h)[0] + 6, T(-h, h)[1] + 4), "current engine: production prefabs exist only in this 5 x 5 chunk square", fill=(30, 30, 160), font=FS)
    lx = W - 365; ly = 90; d.text((lx, ly - 30), "LOD ladder (design)", fill=(20, 20, 20), font=FB)
    for i in range(5):
        d.rectangle([lx, ly, lx + 18, ly + 14], fill=cols[i][:3], outline=(60, 60, 60)); d.text((lx + 26, ly - 1), f"{edges[i]}-{edges[i + 1]} m: {st['bandProduct'][i]}", fill=(30, 30, 30), font=FS)
        d.text((lx + 26, ly + 13), f"   at most {st['capsTrisPerPlot'][i]} triangles per plot", fill=(70, 70, 70), font=FS); ly += 42
    ly += 10
    for t in ("tier view-radius scale (bands shrink or stay):", f"low {st['tierRadiusScale']['low']}, medium {st['tierRadiusScale']['medium']}, high {st['tierRadiusScale']['high']}", "", "Beyond the blue square nothing but terrain and road", "exists today, although the far clip is 900 to 2,200 m."): d.text((lx, ly), t, fill=(40, 40, 40), font=F); ly += 20
    title(d, W, "KANO KSA-1: STREAMING BANDS AROUND A VIEWER", "Circles are the distance bands of the LOD ladder at the medium tier (scale 1.0); the square is what the current streamer instantiates.")
    im.save(out / "streaming.png"); print("wrote streaming.png", im.size)


def layout(did):
    L = P["layouts"][did]; w, h = L["size"]; S = 0.8 if max(w, h) < 1100 else 0.66; W, H, T = mapper(-w / 2, -h / 2, w / 2, h / 2, S, right=300)
    im = Image.new("RGB", (W, H), (226, 224, 214)); d = ImageDraw.Draw(im, "RGBA")
    ASPH, ASPH2, SIDE, BLD, BLD2, GRN, WAL, TRK, LINE = (92, 92, 98, 255), (112, 112, 120, 255), (190, 186, 176, 255), (205, 190, 165, 255), (170, 150, 125, 255), (150, 190, 130, 255), (110, 100, 90, 255), (225, 225, 228, 255), (240, 240, 235, 255)
    def poly(pts, fill, outline=(70, 65, 60, 255)): d.polygon([T(*p) for p in pts], fill=fill, outline=outline)
    def rect(cx, cz, rw, rd, fill, outline=(70, 65, 60, 255), yaw=0):
        c, s_ = math.cos(math.radians(yaw)), math.sin(math.radians(yaw)); poly([(cx + dx * c - dz * s_, cz + dx * s_ + dz * c) for dx, dz in ((-rw / 2, -rd / 2), (rw / 2, -rd / 2), (rw / 2, rd / 2), (-rw / 2, rd / 2))], fill, outline)
    def line(pts, col, wd): d.line([T(*p) for p in pts], fill=col, width=max(1, int(wd * S)))
    def text(x, z, t, col=(20, 20, 20), dx=0, dy=0, font=FS):
        X, Y = T(x, z); d.rectangle([X + dx - 2, Y + dy - 1, X + dx + len(t) * 6.4, Y + dy + 14], fill=(255, 255, 255, 170)); d.text((X + dx, Y + dy), t, fill=col, font=font)
    def tree(x, z, r=4): X, Y = T(x, z); d.ellipse([X - r * S * 1.6, Y - r * S * 1.6, X + r * S * 1.6, Y + r * S * 1.6], fill=(70, 130, 70, 215))
    def pole(x, z): X, Y = T(x, z); d.rectangle([X - 1.5, Y - 1.5, X + 1.5, Y + 1.5], fill=(30, 30, 35, 255))
    def road(p0, p1, wd, col=ASPH, side=0, marks=True):
        line([p0, p1], col, wd)
        if side: 
            dx, dz = p1[0] - p0[0], p1[1] - p0[1]; n = math.hypot(dx, dz) or 1; nx, nz = -dz / n, dx / n
            for sgn in (-1, 1): line([(p0[0] + sgn * nx * (wd / 2 + side / 2), p0[1] + sgn * nz * (wd / 2 + side / 2)), (p1[0] + sgn * nx * (wd / 2 + side / 2), p1[1] + sgn * nz * (wd / 2 + side / 2))], SIDE, side)
        if marks: line([p0, p1], (240, 240, 235, 200), 0.6)
    def bays(x0, z0, n, bw, bd, horizontal=True, col=LINE, fill=None, items=None):
        for i in range(n):
            cx, cz = (x0 + i * bw, z0) if horizontal else (x0, z0 + i * bw)
            rect(cx, cz, bw if not horizontal else bw, bd, fill or (0, 0, 0, 0), outline=col if col else (0, 0, 0, 0))
            if items and i in items: rect(cx, cz, (bw - 0.8) if horizontal else bd * 0.8, (bd * 0.8) if horizontal else (bw - 0.8), items[i], outline=(50, 50, 50, 255))
    def arrow(x, z, ang=0, ln=12):
        c, s_ = math.cos(math.radians(ang)), math.sin(math.radians(ang)); pts = [(x - ln / 2 * c, z - ln / 2 * s_), (x + ln / 2 * c, z + ln / 2 * s_)]; line(pts, LINE, 0.9)
        line([pts[1], (pts[1][0] - 3 * c + 2 * s_, pts[1][1] - 3 * s_ - 2 * c)], LINE, 0.9); line([pts[1], (pts[1][0] - 3 * c - 2 * s_, pts[1][1] - 3 * s_ + 2 * c)], LINE, 0.9)
    notes = []
    if did == "D06":
        for sx in (-w / 2 + 30, w / 2 - 30): line([(sx, -h / 2), (sx, h / 2)], ASPH, 16)
        for sz in (-h / 2 + 30, h / 2 - 30): line([(-w / 2, sz), (w / 2, sz)], ASPH, 16)
        bx, bz, gap = 140, 84, 16; cols = 7; rows = 11; x_start = -(cols * (bx + gap) - gap) / 2; z_start = -(rows * (bz + gap) - gap) / 2
        for ci in range(cols):
            for ri in range(rows):
                cx = x_start + ci * (bx + gap) + bx / 2; cz = z_start + ri * (bz + gap) + bz / 2
                if (ci, ri) in ((3, 5), (3, 4)): rect(cx, cz, bx, bz, GRN); tree(cx - 30, cz); tree(cx, cz + 10); tree(cx + 30, cz - 8); continue
                for k in range(6):
                    for sd in (-1, 1):
                        px = cx - bx / 2 + 11 + k * 22; pz = cz + sd * 21
                        rect(px, pz, 20, 30, (212, 200, 178, 255), outline=(150, 130, 105, 255)); rect(px, pz - sd * 4, 11, 13, BLD2, outline=(90, 75, 60, 255)); line([(px - 8, pz + sd * 14), (px + 8, pz + sd * 14)], WAL, 1.0)
                if ci in (2, 4) and ri == 3 and False: pass
        for ci in range(cols + 1):
            xx = x_start + ci * (bx + gap) - gap / 2
            line([(xx, z_start - gap), (xx, -z_start + gap)], ASPH2, 9)
        for ri in range(rows + 1):
            zz = z_start + ri * (bz + gap) - gap / 2
            line([(x_start - gap, zz), (-x_start + gap, zz)], ASPH2, 9)
            for k in range(0, int(-x_start * 2), 22): tree(x_start + k, zz + 7, 3.2)
        for k in range(-int(w / 2) + 60, int(w / 2) - 60, 44): pole(k, -h / 2 + 40)
        rect(-w / 2 + 70, -h / 2 + 30, 26, 18, (60, 160, 100, 255)); text(-w / 2 + 70, -h / 2 + 30, "estate gate", dx=-26, dy=-24); text(0, 0, "park", dx=-12, dy=-8); rect(380, 255, 50, 42, (60, 160, 100, 255)); text(380, 255, "mosque site", dx=-30, dy=-24)
        notes = ["Plots about 22 x 32 m face the street; villa, wall and gate per plot (R-006).", "Local streets are one-way loops with paver sidewalks, painted kerbs, trees and single-arm lights.", "Perimeter collector on four sides; one estate gate; a park and a mosque site break the grid.", L["note"]]
    elif did == "D08":
        road((-w / 2, 0), (w / 2, 0), 34, ASPH, side=5, marks=False); line([(-w / 2, 0), (w / 2, 0)], (100, 160, 100, 255), 6)
        for k in range(-int(w / 2) + 40, int(w / 2) - 40, 46): pole(k, 0)
        rect(-300, 250, 400, 280, (214, 224, 205, 255), outline=(90, 110, 90, 255)); rect(-300, 265, 150, 70, BLD, outline=(90, 80, 70, 255)); rect(-440, 290, 60, 110, BLD); rect(-160, 290, 60, 110, BLD); rect(-300, 205, 70, 24, (200, 60, 50, 255)); text(-300, 265, "hospital block", dx=-40, dy=-8); text(-300, 205, "emergency", dx=-26, dy=-22)
        bays(-410, 150, 12, 10, 24, True, (255, 255, 255, 220)); bays(-410, 110, 12, 10, 24, True, (255, 255, 255, 220)); rect(-300, 150, 60, 30, GRN)
        for k in range(8): tree(-480 + k * 60, 340, 4)
        rect(260, 200, 240, 200, (210, 214, 224, 255), outline=(60, 70, 120, 255)); rect(260, 235, 120, 60, (150, 160, 200, 255), outline=(40, 50, 100, 255)); rect(205, 120, 30, 22, (180, 190, 215, 255)); text(260, 235, "police station", dx=-38, dy=-8); text(205, 120, "gatehouse", dx=-20, dy=14)
        bays(190, 170, 8, 8, 18, True, (255, 255, 255, 220), items={1: (60, 60, 70, 255), 3: (60, 60, 70, 255)}); line([(140, 100), (380, 100)], (60, 70, 120, 255), 2)
        for rx, rz, rw, rd, t_ in ((380, -190, 170, 150, "school (phase 2)"), (130, -200, 180, 130, "government (phase 2)"), (-200, -200, 150, 110, "hotel (phase 2)")):
            rect(rx, rz, rw, rd, (200, 200, 200, 150)); text(rx, rz, t_, dx=-44, dy=-8)
        for k in range(-int(w / 2) + 40, int(w / 2) - 40, 60): tree(k, -40, 3.5); tree(k + 20, 40, 3.5)
        notes = ["Divided arterial: planted median, double-arm lights, paver sidewalks (R-011).", "Hospital campus (R-010): main block, wings, emergency canopy, two parking lots, landscaped island.", "Police station: station block, gatehouse, boundary wall, parking.", "Greyed blocks need new concepts (M-10).", L["note"]]
    elif did == "D09":
        rect(0, 250, 340, 110, (208, 218, 200, 255)); rect(0, 262, 220, 56, BLD, outline=(90, 80, 70, 255)); text(0, 262, "terminal hall", dx=-34, dy=-8)
        rect(-130, 120, 300, 54, (160, 160, 168, 255), outline=(70, 70, 80, 255)); cols_ = [(240, 240, 240, 255), (190, 50, 45, 255), (50, 140, 70, 255), (225, 190, 40, 255), (240, 240, 240, 255), (190, 50, 45, 255)]
        for i in range(6):
            rect(-250 + i * 50, 120, 12, 44, cols_[i], outline=(50, 50, 50, 255)); d.text((T(-250 + i * 50, 96)[0] - 8, T(-250 + i * 50, 96)[1]), f"A{i + 1}", fill=(20, 20, 20), font=FS)
        rect(0, -20, 380, 230, ASPH2, outline=(70, 70, 80, 255)); rect(0, 20, 90, 50, GRN, outline=(60, 90, 50, 255)); text(0, 20, "island", dx=-14, dy=-8)
        for ax, az, ang in ((-120, 40, 0), (-60, -40, 180), (80, -60, 200), (140, 30, 340)): arrow(ax, az, ang)
        for i in range(6): line([(-140 + i * 20, -70), (-140 + i * 20, -92)], LINE, 1.1)
        bays(230, -20, 10, 9, 26, False, (255, 255, 255, 220), items={2: (230, 230, 235, 255), 5: (80, 80, 90, 255)})
        rect(70, -220, 46, 34, BLD2, outline=(70, 60, 50, 255)); text(70, -220, "gatehouse", dx=-24, dy=-24); line([(40, -190), (110, -190)], (210, 60, 50, 255), 2.2); line([(-w / 2 + 30, -245), (w / 2 - 30, -245)], WAL, 3)
        road((-w / 2, -300), (w / 2, -300), 36, ASPH, side=4); line([(70, -300), (70, -190)], ASPH, 24)
        for k in range(-int(w / 2) + 50, int(w / 2) - 40, 70): tree(k, -270, 3.6)
        for k in range(-200, 220, 80): pole(k, -150)
        notes = ["Terminal hall with lattice-panelled arches (R-009).", "Six canopied bays with solar roofs and bay markers A1 to A6; coaches in four liveries.", "Apron with arrows, a zebra crossing and a landscaped island; parked SUVs.", "Gatehouse with barrier arm; perimeter wall with piers and railing.", L["note"]]
    elif did == "D10":
        road((-w / 2, -430), (w / 2, -430), 40, ASPH, side=5, marks=False); line([(-w / 2, -430), (w / 2, -430)], (100, 160, 100, 255), 5)
        rect(-120, 40, 520, 230, ASPH2, outline=(70, 70, 80, 255))
        rect(-120, 230, 520, 190, (205, 205, 210, 255), outline=(60, 70, 120, 255)); rect(-120, 240, 480, 120, (150, 160, 190, 255), outline=(40, 50, 100, 255)); text(-120, 250, "warehouse", dx=-26, dy=-8)
        for i in range(6): rect(-300 + i * 70, 178, 24, 10, (60, 60, 70, 255)); d.text((T(-300 + i * 70, 170)[0] - 4, T(-300 + i * 70, 170)[1] - 16), str(i + 1), fill=(20, 20, 20), font=FS); (i in (1, 3, 5)) and rect(-300 + i * 70, 150, 12, 30, (240, 240, 240, 255), outline=(50, 50, 50, 255))
        rect(-380, 260, 90, 60, BLD, outline=(90, 80, 70, 255)); text(-380, 262, "site office", dx=-26, dy=-8)
        rect(300, 120, 300, 340, ASPH2, outline=(70, 70, 80, 255))
        for r_ in range(5):
            for c_ in range(2):
                for k in range(8): line([(165 + c_ * 150 + 0, 10 + k * 40 + r_ * 0)] * 1 + [(165 + c_ * 150 + 130, 10 + k * 40)], LINE, 0.7) if r_ == 0 else None
        for k in range(8): rect(235, -5 + k * 40, 90, 3.4, (230, 230, 235, 255), outline=(60, 60, 60, 255)) if k % 2 == 0 else None
        for k in range(8): rect(385, -5 + k * 40, 90, 3.4, (190, 50, 45, 255) if k % 3 == 0 else (230, 230, 235, 255), outline=(60, 60, 60, 255)) if k % 2 == 1 else None
        text(300, 300, "truck-parking field", dx=-46, dy=-8)
        rect(360, -270, 300, 150, (212, 220, 202, 255)); rect(360, -250, 150, 40, (40, 120, 70, 255), outline=(20, 70, 40, 255)); text(360, -250, "fuel canopy", dx=-30, dy=-8)
        for i in range(3): rect(315 + i * 45, -250, 8, 22, (230, 200, 60, 255), outline=(60, 60, 60, 255))
        rect(320, -330, 110, 36, BLD, outline=(90, 80, 70, 255)); text(320, -330, "shop and restaurant", dx=-58, dy=-8); rect(460, -330, 90, 36, BLD2, outline=(90, 80, 70, 255)); text(460, -330, "service garage", dx=-40, dy=-24)
        rect(-60, -330, 52, 36, BLD2); text(-60, -330, "gatehouse", dx=-24, dy=-24); line([(-95, -300), (-25, -300)], (210, 60, 50, 255), 2.2); line([(-60, -430), (-60, -300)], ASPH, 26); line([(-w / 2 + 20, -300), (190, -300)], WAL, 3)
        d.ellipse([T(440, 260)[0] - 8, T(440, 260)[1] - 8, T(440, 260)[0] + 8, T(440, 260)[1] + 8], fill=(220, 220, 225, 255), outline=(60, 60, 60, 255)); text(440, 260, "water tower", dx=-30, dy=12)
        for k in range(-450, 480, 130): pole(k, -280); pole(k + 40, 210)
        notes = ["Warehouse with six numbered docks, trucks at the docks, yard, site office (R-014, R-015).", "Truck-parking field with painted bays and truck symbols.", "Fuel and service centre beside the entrance road (R-013): canopy with numbered islands, shop, garage.", "Gatehouse with barrier arm; perimeter wall; drain grating and speed hump across the entrance.", L["note"]]
    else:   # D11
        pts = []
        for k in range(61):
            t_ = k / 60.0; x_ = -w / 2 + 40 + t_ * (w - 80); z_ = -330 + 560 * t_ + 140 * math.sin(t_ * math.pi * 1.4)
            pts.append((x_, z_))
        def off(pts, o): 
            out_ = []
            for i, (x, z) in enumerate(pts):
                x2, z2 = pts[min(i + 1, len(pts) - 1)]; x1, z1 = pts[max(i - 1, 0)]; dx, dz = x2 - x1, z2 - z1; n = math.hypot(dx, dz) or 1; out_.append((x - dz / n * o, z + dx / n * o))
            return out_
        poly([(-w / 2, 130), (-w / 2 + 120, 80), (-w / 2 + 300, 150), (-w / 2 + 500, 120), (-w / 2 + 700, 230), (w / 2, 260), (w / 2, h / 2), (-w / 2, h / 2)], (170, 190, 150, 255), outline=(120, 140, 100, 255)); text(0, 430, "rock-cut slope and hill backdrop (hilly Kaduna side; not Kano's plain)", dx=-170, dy=-8)
        for o, wd, col in ((0, 56, ASPH), (0, 8, (110, 160, 100, 255))): line(pts, col, wd)
        for o in (-22, 22): line(off(pts, o), (225, 190, 40, 255), 1.0)
        for o in (-12, -24 + 8, 12, 24 - 8, 16, -16): pass
        for o in (-13, 13): line(off(pts, o), (240, 240, 235, 160), 0.6)
        for o in (-33, 33): line(off(pts, o), (150, 150, 155, 255), 1.6)
        for i in range(2, 60, 3): pole(*pts[i])
        gx, gz = pts[12]; rect(gx, gz, 8, 80, (60, 60, 70, 255), yaw=0); text(gx, gz, "gantry (R-008)", dx=-30, dy=-26)
        for i in range(34, 50, 3): x_, z_ = off(pts, 39)[i]; rect(x_, z_, 6, 6, (230, 190, 40, 255), outline=(30, 30, 30, 255))
        ix, iz = pts[56]; d.ellipse([T(ix - 70, iz + 30)[0], T(ix - 70, iz + 30)[1], T(ix + 70, iz - 110)[0], T(ix + 70, iz - 110)[1]], outline=(140, 40, 160, 255), width=3); text(ix, iz - 40, "interchange approach", dx=-50, dy=-8)
        for i in range(0, 60, 4): tree(*off(pts, -60)[i], 4)
        notes = ["Divided expressway: three lanes each way, planted median with double-arm lights, yellow edge lines, W-beam guardrails on a black/white kerb base (R-007).", "Gantry with green sign panel before the bend (R-008, R-012); chevron signs on the curve.", "The hills are the concept's; Kano itself is flat.", L["note"]]
    lx = W - 285; ly = 90
    d.text((lx, ly - 30), "Notes", fill=(20, 20, 20), font=FB)
    for n_ in notes:
        words = n_.split(); ln = ""
        for wd_ in words:
            if len(ln) + len(wd_) > 33: d.text((lx, ly), ln, fill=(40, 40, 40), font=FS); ly += 16; ln = ""
            ln += wd_ + " "
        d.text((lx, ly), ln, fill=(40, 40, 40), font=FS); ly += 26
    d.line([(lx, H - 70), (lx + 100 * S, H - 70)], fill=(30, 30, 30), width=3); d.text((lx, H - 62), "100 m", fill=(30, 30, 30), font=F)
    d.text((20, 12), f"{did} {L['title'].upper()}", fill=(20, 20, 20), font=FT); d.text((20, 42), "District layout (planning diagram: envelopes and positions read from the concept composition; NOT a model, NOT surveyed).", fill=(80, 80, 80), font=F)
    im.save(out / f"layout_{did}.png"); print("wrote", f"layout_{did}.png", im.size)


master("master"); master("engine"); streaming()
for did in P["layouts"]: layout(did)
