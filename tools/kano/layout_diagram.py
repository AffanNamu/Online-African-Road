#!/usr/bin/env python3
"""Draws the annotated plan of the Kano slice layout from the game's own generated data (docs/kano/kano_slice_geometry.json) and the inventory envelopes.
North is up, as in the game (x east, z north). The PNG is a review image: it is written to --out and is NOT committed (binary files stay out of the public repository).
Usage: python3 tools/kano/layout_diagram.py --out <file.png>"""
import argparse, json, math
from pathlib import Path
from PIL import Image, ImageDraw, ImageFont

ROOT = Path(__file__).resolve().parents[2]
ap = argparse.ArgumentParser(); ap.add_argument("--out", required=True); a = ap.parse_args()
G = json.loads((ROOT / "docs/kano/kano_slice_geometry.json").read_text()); INV = {p["id"]: p for p in json.loads((ROOT / "docs/kano/kano_asset_inventory.json").read_text())["props"]}
FR = json.loads((ROOT / "docs/kano/kano_world_frame.json").read_text()); ROUTE = json.loads((ROOT / "docs/kano/ng-kano-slice.route.draft.json").read_text())
S = 0.62        # pixels per metre
main = G["corridors"][0]["samples"]; xs = [p[1] for c in G["corridors"] for p in c["samples"]]; zs = [p[3] for c in G["corridors"] for p in c["samples"]]
x0, x1, z0, z1 = min(xs) - 230, max(xs) + 130, min(zs) - 90, max(zs) + 90
W, H = int((x1 - x0) * S) + 420, int((z1 - z0) * S) + 150
im = Image.new("RGB", (W, H), (240, 236, 228)); d = ImageDraw.Draw(im)
try: F = ImageFont.truetype("DejaVuSans.ttf", 15); FB = ImageFont.truetype("DejaVuSans-Bold.ttf", 17); FS = ImageFont.truetype("DejaVuSans.ttf", 12)
except Exception: F = FB = FS = ImageFont.load_default()
P = lambda x, z: ((x - x0) * S + 20, H - ((z - z0) * S + 60))      # world -> pixel (north up)

# zones along the main road (background bands drawn as thick translucent strokes)
ZCOL = [(0, 450, (150, 170, 200), "1  City entrance"), (450, 830, (175, 190, 215), "2  Modern district"), (830, 1240, (235, 215, 150), "3  Commercial spine"), (1240, 1660, (230, 190, 150), "4  Old-neighbourhood edge")]
def at(s):
    for p, q in zip(main, main[1:]):
        if p[0] <= s <= q[0]:
            t = (s - p[0]) / (q[0] - p[0]); return [p[k] + (q[k] - p[k]) * t for k in range(1, 7)]
    return main[-1][1:7]
ov = Image.new("RGBA", im.size, (0, 0, 0, 0)); od = ImageDraw.Draw(ov)
for (a_, b_, col, name) in ZCOL:      # each zone is one polygon: the centre line offset 125 m to each side
    ss = list(range(int(a_), int(min(b_, main[-1][0])) + 1, 10)); left, right = [], []
    for s_ in ss:
        x, y, z, fx, fz, k = at(s_); rx, rz = fz, -fx
        left.append(P(x - rx * 125, z - rz * 125)); right.append(P(x + rx * 125, z + rz * 125))
    od.polygon(left + right[::-1], fill=col + (95,))
im.paste(Image.alpha_composite(im.convert("RGBA"), ov).convert("RGB")); d = ImageDraw.Draw(im)

# roads: formation (verge), paved (asphalt), median
for cor in G["corridors"]:
    sm = cor["samples"]; runs = cor["runs"]
    for p, q in zip(sm, sm[1:]):
        run = runs[p[7]]; k = p[6]
        for half, col in ((run["formationHalf"] * k, (176, 150, 112)), (run["pavedHalf"] * k, (132, 132, 128)), (run["carriagewayHalf"] * k, {"Good": (62, 62, 66), "Worn": (86, 82, 78), "Damaged": (118, 70, 60)}[run["surface"]])):
            d.line([P(p[1], p[3]), P(q[1], q[3])], fill=col, width=max(2, int(half * 2 * S)))
        if run["hasMedian"]: d.line([P(p[1], p[3]), P(q[1], q[3])], fill=(215, 215, 210), width=max(2, int(2.0 * S)))

# plots and set pieces as oriented envelopes
CAT = {"traditional": (190, 117, 66), "modern": (140, 169, 204), "commercial": (219, 184, 56), "landmark": (158, 133, 199), "religious": (51, 158, 92), "fuel": (209, 66, 51)}
def colour(pid):
    if pid.startswith("kano_hausa"): return CAT["traditional"]
    if pid.startswith("kano_compound_modern"): return CAT["modern"]
    if pid.startswith("kano_shop_row"): return CAT["commercial"]
    if pid in ("kano_plaza_a", "kano_apartment_block_a", "kano_midrise_a"): return CAT["landmark"]
    if pid == "kano_mosque_neighbourhood_a": return CAT["religious"]
    if pid == "kano_filling_station_a": return CAT["fuel"]
    return None
for i in G["instances"]:
    c = colour(i["prop"])
    if c is None: continue
    dd = INV[i["prop"]]; w, dp = dd["widthM"] * i["scale"], dd["depthM"] * i["scale"]; ya = math.radians(i["yaw"]); fx, fz = math.sin(ya), math.cos(ya); rx, rz = fz, -fx
    cs = [(i["x"] + sx * rx * w / 2 + sy * fx * dp / 2, i["z"] + sx * rz * w / 2 + sy * fz * dp / 2) for sx, sy in ((1, 1), (-1, 1), (-1, -1), (1, -1))]
    d.polygon([P(x, z) for x, z in cs], fill=c, outline=(70, 60, 50))
    # the front edge (towards the street) is drawn heavier so the orientation reads
    d.line([P(*cs[0]), P(*cs[1])], fill=(40, 30, 25), width=2)
for i in G["instances"]:
    if i["prop"] in ("kano_neem_tree_a", "kano_neem_tree_b", "kano_date_palm_a"):
        px, py = P(i["x"], i["z"]); d.ellipse([px - 3, py - 3, px + 3, py + 3], fill=(40, 110, 45))
    elif i["prop"] in ("kano_streetlight_single", "kano_streetlight_double_median", "kano_utility_pole", "kano_transformer_pole"):
        px, py = P(i["x"], i["z"]); d.rectangle([px - 1, py - 1, px + 1, py + 1], fill=(25, 25, 28))

# station ticks every 100 m and junction labels
for s in range(0, int(main[-1][0]) + 1, 100):
    x, y, z, fx, fz, k = at(s); rx, rz = fz, -fx; px, py = P(x - rx * 28, z - rz * 28); qx, qy = P(x - rx * 22, z - rz * 22)
    d.line([(px, py), (qx, qy)], fill=(40, 40, 60), width=1); d.text((px - 34, py - 7), f"{s}", fill=(40, 40, 60), font=FS)
for j in ROUTE["junctions"]:
    x, y, z, fx, fz, k = at(j["s"]); side = 1 if j["angleDeg"] > 0 else -1; rx, rz = fz, -fx
    px, py = P(x + rx * side * 150, z + rz * side * 150); d.text((px - 10, py - 18), j["id"][:2].upper() + f" @{int(j['s'])}", fill=(20, 20, 20), font=FB)
for h in ("mosque_1", "plaza_1", "fuel_1", "midrise_1", "midrise_2", "apartments_1"):
    pr = next(p for p in ROUTE["props"] if p["id"] == h); x, y, z, fx, fz, k = at(pr["s"]); rx, rz = fz, -fx
    px, py = P(x + rx * pr["lateral"], z + rz * pr["lateral"]); d.text((px + 14, py - 8), {"mosque_1": "mosque (qibla)", "plaza_1": "plaza", "fuel_1": "petrol", "midrise_1": "mid-rise", "midrise_2": "mid-rise", "apartments_1": "apartments"}[h], fill=(20, 20, 20), font=F)
# zone captions, damaged stretch, qibla, north arrow, scale bar, legend
for (a_, b_, col, name) in ZCOL:
    x, y, z, fx, fz, k = at((a_ + min(b_, main[-1][0])) / 2); rx, rz = fz, -fx; px, py = P(x - rx * 128, z - rz * 128); d.text((px - 8 - len(name) * 9.5, py - 8), name, fill=(25, 25, 45), font=FB)
x, y, z, fx, fz, k = at(1125); px, py = P(x - fz * 18, z + fx * 18); d.text((px - 170, py + 22), "damaged 1050-1200", fill=(110, 40, 30), font=F)
mx, my = W - 140, 110; qa = math.radians(FR["qibla"]["bearingDeg"]); d.ellipse([mx - 44, my - 44, mx + 44, my + 44], outline=(60, 60, 60), width=2)
d.line([(mx, my), (mx + 40 * math.sin(qa), my - 40 * math.cos(qa))], fill=(51, 158, 92), width=4); d.line([(mx, my), (mx, my - 40)], fill=(60, 60, 60), width=2)
d.text((mx - 4, my - 62), "N", fill=(30, 30, 30), font=FB); d.text((mx - 60, my + 50), f"qibla {FR['qibla']['bearingDeg']}°", fill=(30, 100, 60), font=F)
bx, by = W - 300, H - 52; d.line([(bx, by), (bx + 100 * S, by)], fill=(30, 30, 30), width=3); d.text((bx, by + 6), "100 m", fill=(30, 30, 30), font=F)
ly = 190
for name, c in (("traditional compounds", CAT["traditional"]), ("modern compounds", CAT["modern"]), ("shop rows", CAT["commercial"]), ("plaza / apartments / mid-rise", CAT["landmark"]), ("mosque", CAT["religious"]), ("petrol station", CAT["fuel"])):
    d.rectangle([W - 310, ly, W - 292, ly + 14], fill=c, outline=(70, 60, 50)); d.text((W - 284, ly - 1), name, fill=(30, 30, 30), font=F); ly += 24
d.ellipse([W - 310, ly + 2, W - 302, ly + 10], fill=(40, 110, 45)); d.text((W - 284, ly - 1), "trees", fill=(30, 30, 30), font=F); ly += 24
d.rectangle([W - 310, ly + 2, W - 304, ly + 8], fill=(25, 25, 28)); d.text((W - 284, ly - 1), "poles and lights", fill=(30, 30, 30), font=F); ly += 30
for name, c in (("asphalt, good", (62, 62, 66)), ("asphalt, worn", (86, 82, 78)), ("asphalt, damaged", (118, 70, 60))):
    d.rectangle([W - 310, ly, W - 292, ly + 14], fill=c); d.text((W - 284, ly - 1), name, fill=(30, 30, 30), font=F); ly += 24
d.text((20, 14), "KANO VERTICAL SLICE: layout draft (fictional neighbourhood, placeholder envelopes)", fill=(20, 20, 20), font=FB)
d.text((20, 36), f"1:1 scale, north up, {G['routeLengthM']:.0f} m, six T-junctions, {len(G['instances'])} placed prefabs. Heavy edge of each plot = its front. Frame K0, anchor {FR['canonicalAnchor']['lat']}N {FR['canonicalAnchor']['lon']}E.", fill=(70, 70, 70), font=F)
im.save(a.out); print("wrote", a.out, im.size)
