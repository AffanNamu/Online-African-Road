#!/usr/bin/env python3
"""Real scanned surface textures for the generated world (owner, 2026-11-22: "only highly realistic things").

The road and ground materials were small procedural noise textures (WorldMaterials, ProcTex). This tool brings in photographs of real surfaces from
Poly Haven (polyhaven.com), every one CC0 1.0 (public domain: no attribution required; authors recorded anyway). The game repository holds no binaries,
so the pictures are NOT committed: the manifest (tools/textures/scanned_textures.json: URL, md5, sha256, size on the ground, brightness tint) is, and CI
downloads and checks them into Assets/StreamingAssets/Scanned/ before the Unity build (the folder is git-ignored). The game streams them after start-up
(World/ScannedTextures.cs), so the initial download and its 6 MB Resources budget do not grow. Copies are preserved in ARO-CONTENT as Git LFS objects.

  python3 tools/textures/fetch_scanned.py                 # download into Assets/StreamingAssets/Scanned (CI)
  python3 tools/textures/fetch_scanned.py --check         # verify an existing download against the manifest
  python3 tools/textures/fetch_scanned.py --author        # refresh URL/md5/author/size from the Poly Haven API and compute the tints (needs numpy + Pillow)

Each entry replaces one road slot's procedural texture; a build without the download keeps the procedural ones. The tint scales the picture's mean linear brightness to the old texture's, so the lighting checks of CI (which measure
screen brightness) keep their meaning while the surface gains real detail and colour.
"""
import hashlib, json, os, sys, urllib.request

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
MANIFEST = os.path.join(ROOT, "tools", "textures", "scanned_textures.json")
OUT = os.path.join(ROOT, "Assets", "StreamingAssets", "Scanned")
UA = {"User-Agent": "game-asset-fetch/1.0"}

# slot -> (Poly Haven asset, target mean sRGB colour: that of the procedural texture it replaces, (a + b) / 2 of its ProcTex.Grain colours, except the asphalt:
# the old one was nearly black (fresh tar, 0.195); Kano's roads are sun-bleached grey, so it targets 0.26)
CHOICE = {
    "Asphalt":  ("asphalt_02",             (0.260, 0.260, 0.265)),     # grey asphalt with real cracks
    "Shoulder": ("gravel_road",            (0.350, 0.335, 0.305)),     # dusty compacted gravel
    "Verge":    ("dry_ground_rocks",       (0.460, 0.320, 0.200)),     # dry orange-brown soil with pebbles: the Sahel ground of Kano
    "Concrete": ("rough_concrete",         (0.650, 0.650, 0.630)),
    "Kerb":     ("rough_concrete",         (0.690, 0.690, 0.665)),
    "Sidewalk": ("concrete_pavement_02",   (0.670, 0.650, 0.605)),     # square concrete pavers
}


def _get(url):
    with urllib.request.urlopen(urllib.request.Request(url, headers=UA), timeout=120) as r:
        return r.read()


def _lin(c):
    return c / 12.92 if c <= 0.04045 else ((c + 0.055) / 1.055) ** 2.4


def author():
    import io
    import numpy as np
    from PIL import Image
    entries = []
    for slot, (asset, old) in CHOICE.items():
        info = json.loads(_get("https://api.polyhaven.com/info/" + asset))
        files = json.loads(_get("https://api.polyhaven.com/files/" + asset))
        f = files["Diffuse"]["1k"]["jpg"]; data = _get(f["url"])
        assert hashlib.md5(data).hexdigest() == f["md5"], asset
        px = np.asarray(Image.open(io.BytesIO(data)).convert("RGB"), dtype=np.float64) / 255.0
        lin = np.where(px <= 0.04045, px / 12.92, ((px + 0.055) / 1.055) ** 2.4)
        lum_new = float((lin * [0.2126, 0.7152, 0.0722]).sum(axis=2).mean())
        lum_old = sum(w * _lin(c) for w, c in zip((0.2126, 0.7152, 0.0722), old))
        k = max(0.6, min(1.5, lum_old / lum_new))           # within these limits the photo keeps its own look (a larger tint would wash out or crush it)
        entries.append({
            "slot": slot, "asset": asset, "name": info["name"], "authors": sorted(info.get("authors", {}).keys()), "licence": "CC0-1.0",
            "page": "https://polyhaven.com/a/" + asset, "url": f["url"], "md5": f["md5"], "sha256": hashlib.sha256(data).hexdigest(), "bytes": len(data),
            "metres": round(float(info["dimensions"][0]) / 1000.0, 3), "tint": round(k, 4),
        })
        print(f"{slot:9s} {asset:26s} {len(data) // 1024:5d} KB  {entries[-1]['metres']} m  tint {k:.3f}")
    doc = {"source": "Poly Haven (https://polyhaven.com), every texture CC0 1.0; authors listed per entry", "resolution": "1k jpg, albedo only",
           "note": "normal maps need a Resources material that keeps the _NORMALMAP shader variant in the WebGL build: not done yet", "textures": entries}
    with open(MANIFEST, "w") as fh:
        json.dump(doc, fh, indent=1, ensure_ascii=True)
        fh.write("\n")


def load():
    with open(MANIFEST) as fh:
        return json.load(fh)


def fetch(check_only=False):
    doc = load(); os.makedirs(OUT, exist_ok=True); bad = 0
    for e in doc["textures"]:
        path = os.path.join(OUT, e["slot"] + ".jpg")
        if not check_only and not (os.path.isfile(path) and hashlib.sha256(open(path, "rb").read()).hexdigest() == e["sha256"]):
            data = None
            for attempt in range(4):
                try:
                    data = _get(e["url"]); break
                except Exception as ex:      # network hiccup: retry, then report
                    print("retry", e["asset"], ex)
            if data is None:
                print("FAILED to download", e["url"]); bad += 1; continue
            with open(path, "wb") as fh:
                fh.write(data)
        ok = os.path.isfile(path) and hashlib.sha256(open(path, "rb").read()).hexdigest() == e["sha256"]
        print(("ok      " if ok else "MISMATCH"), e["slot"], e["asset"]); bad += 0 if ok else 1
    if not check_only:                                   # the game reads the size on the ground and the tint of each slot from this copy (StreamingAssets/Scanned/manifest.json)
        with open(os.path.join(OUT, "manifest.json"), "w") as fh:
            json.dump({"textures": [{"slot": e["slot"], "asset": e["asset"], "metres": e["metres"], "tint": e["tint"]} for e in doc["textures"]]}, fh, indent=1)
    return 1 if bad else 0


if __name__ == "__main__":
    if "--author" in sys.argv:
        author(); sys.exit(0)
    sys.exit(fetch(check_only="--check" in sys.argv))
