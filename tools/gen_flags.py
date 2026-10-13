#!/usr/bin/env python3
"""Rasterises the national flags the game shows (Assets/_Project/Resources/Flags/{iso2 lower case}.png, 96x72, 4:3) from the flag-icons SVG set.

Source: https://github.com/lipis/flag-icons (npm `flag-icons` 7.2.3), MIT licence (see Assets/_Project/Resources/Flags/LICENSE-flag-icons.txt).
The flags themselves are national symbols; the MIT licence covers the SVG artwork and allows redistribution with its notice.
Usage: python3 tools/gen_flags.py --src <extracted flag-icons package dir>          # writes the PNGs, manifest.json and checks the licence file is present
       python3 tools/gen_flags.py --check                                          # no source needed: the PNGs match manifest.json (size, hash) and the code list
The country list is ARO.NetCore.CountryFlags.Codes (the C# list is parsed from the source so the two cannot drift): every African state plus Western Sahara.
"""
import argparse, hashlib, io, json, os, re, sys
ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
OUT = os.path.join(ROOT, "Assets/_Project/Resources/Flags")
MODEL = os.path.join(ROOT, "Assets/_Project/Scripts/NetCore/CountryFlags.cs")
W, H = 96, 72

def codes():
    t = open(MODEL, encoding="utf-8").read()
    m = re.search(r'Codes\s*=\s*"([A-Z ]+)"', t)
    if not m: sys.exit("could not read the code list from CountryFlags.cs")
    return m.group(1).split()

def sha(b): return hashlib.sha256(b).hexdigest()

def check():
    man = json.load(open(os.path.join(OUT, "manifest.json")))
    want = codes(); got = sorted(f[:-4].upper() for f in os.listdir(OUT) if f.endswith(".png"))
    problems = []
    if sorted(want) != got: problems.append(f"code list and files differ: only in list {sorted(set(want) - set(got))}, only on disk {sorted(set(got) - set(want))}")
    from PIL import Image
    for c in want:
        p = os.path.join(OUT, c.lower() + ".png")
        if not os.path.exists(p): continue
        im = Image.open(p)
        if im.size != (W, H) or im.mode != "RGBA": problems.append(f"{c}: {im.size} {im.mode}, expected {W}x{H} RGBA")
        if man["flags"].get(c, {}).get("png_sha256") != sha(open(p, "rb").read()): problems.append(f"{c}: png does not match manifest.json")
    for need in ("LICENSE-flag-icons.txt", "SOURCE.md"):
        if not os.path.exists(os.path.join(OUT, need)): problems.append("missing " + need)
    if problems: print("FLAG ASSET CHECK FAILED:\n - " + "\n - ".join(problems)); sys.exit(1)
    print(f"flag assets OK: {len(want)} flags, {W}x{H} RGBA, manifest matches, licence + source notes present")

def build(src):
    import cairosvg
    from PIL import Image
    os.makedirs(OUT, exist_ok=True); man = {"source": "flag-icons 7.2.3 (npm), flags/4x3/*.svg", "licence": "MIT", "size": [W, H], "flags": {}}
    for c in codes():
        svg = os.path.join(src, "flags", "4x3", c.lower() + ".svg")
        if not os.path.exists(svg): sys.exit(f"{svg} not found in the flag-icons package")
        data = open(svg, "rb").read()
        png = cairosvg.svg2png(bytestring=data, output_width=W * 2, output_height=H * 2)          # render at 2x, then average down: smooth edges at the small sizes the UI uses
        im = Image.open(io.BytesIO(png)).convert("RGBA").resize((W, H), Image.LANCZOS)
        buf = io.BytesIO(); im.save(buf, "PNG", optimize=True); b = buf.getvalue()
        open(os.path.join(OUT, c.lower() + ".png"), "wb").write(b)
        man["flags"][c] = {"svg": f"flags/4x3/{c.lower()}.svg", "svg_sha256": sha(data), "png_sha256": sha(b)}
    json.dump(man, open(os.path.join(OUT, "manifest.json"), "w"), indent=1, sort_keys=True); open(os.path.join(OUT, "manifest.json"), "a").write("\n")
    lic = os.path.join(src, "LICENSE")
    open(os.path.join(OUT, "LICENSE-flag-icons.txt"), "w").write(open(lic).read())
    print(f"wrote {len(man['flags'])} flags to {OUT}")

if __name__ == "__main__":
    ap = argparse.ArgumentParser(); ap.add_argument("--src"); ap.add_argument("--check", action="store_true"); a = ap.parse_args()
    if a.check: check()
    elif a.src: build(a.src)
    else: ap.error("give --src <dir> or --check")
