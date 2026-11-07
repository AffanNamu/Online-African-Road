#!/usr/bin/env python3
"""Labels the in-game review pictures of a Kano review run (artifact `kano-review-in-game`) with version, camera view, what the camera is, the run, the lighting and the measured draw calls, and builds the comparison sheets the owner asked for
(2026-11-07: "the actual Unity screenshots for v002 and v003, clearly labelled by camera view and version").
It only reads the downloaded pictures and the run's summary JSON and writes new PNG files; nothing is published (the pictures show only the project's own assets, but they are kept in the private repository, never in this public one).

  gh api repos/<owner>/<repo>/actions/artifacts/<id>/zip > a.zip && unzip a.zip -d run          # needs the artifact storage host in the cloud's network allow-list
  python3 tools/label_review_pictures.py --run run --run-id 41489466501 --version v003 --out out [--other run2 --other-id 41450050093 --other-version v002]
Writes out/<version>/<picture>.png (labelled, one per picture), out/compare_<view>.png (the other version on the left, this one on the right, for the six standard views) and out/scene_<scene>.png (dry and wet, drive and aerial)."""
from __future__ import annotations
import argparse, json, re
from pathlib import Path
from PIL import Image, ImageDraw, ImageFont

VIEWS = {  # from Assets/_Project/Scripts/World/KanoReviewScene.cs and tools/blender/env_kit/scenes.py
    "ref": "the reference camera of concept R-006: eye level 1.7 m at the left kerb line, 28 mm",
    "front": "from the left sidewalk, facing the right-hand frontages, 30 mm",
    "side": "along the street from beyond the section start, 4.6 m high, 24 mm",
    "street1": "pedestrian on the right sidewalk, 28 mm",
    "street2": "driver's eye in the left lane, 28 mm",
    "top": "from 95 m straight down, 50 mm",
    "drive": "the driver's eye in the left lane, looking along the lane, 28 mm",
    "aerial": "three-quarter view from above the left side, 28 mm",
}
VARIANTS = {
    "merged": "merged-frontage prototype placements (draw-call investigation; unapproved)",
    "noshadow": "shadow pass removed (measurement only, not a game setting)",
    "shadowsmall": "small kit items cast no shadow (review-only measurement of a shadow policy)",
    "low": "quality tier low (the mobile budget)",
}
SCENES = {
    "env_hausa_quarter": "Hausa-style quarter lane (design interpretation: no real reference exists, M-01)",
    "env_village": "village lane (vernacular forms not verified, M-14)",
    "env_market_street": "market street (vernacular not verified, M-14; real Kano markets are not depicted)",
}
SEASON = {"dry": "dry season", "wet": "wet season (static meshes: puddles, mud, flood sheet; no weather or physics simulation)"}
STANDARD = ["ref", "front", "side", "street1", "street2", "top"]


def parse(stem: str) -> dict:
    """'ref', 'ref-merged', 'env_village_wet-drive' -> {'scene': 'env_village_wet' or '', 'view': ..., 'variant': ...}."""
    s = stem[len("kanoreview-"):] if stem.startswith("kanoreview-") else stem
    if s.startswith("env_"):
        scene, view = s.rsplit("-", 1); return {"scene": scene, "view": view, "variant": ""}
    view, _, variant = s.partition("-"); return {"scene": "", "view": view, "variant": variant}


def describe(p: dict) -> str:
    if p["scene"]:
        base, season = p["scene"].rsplit("_", 1)
        return f"scene {p['scene']}: {SCENES.get(base, base)}, {SEASON.get(season, season)}; camera `{p['view']}`: {VIEWS.get(p['view'], '')}"
    t = f"camera `{p['view']}`: {VIEWS.get(p['view'], '')}"
    return t + (f"; variant `{p['variant']}`: {VARIANTS.get(p['variant'], '')}" if p["variant"] else "")


def numbers(summary: dict, stem: str) -> str:
    e = summary.get(stem[len("kanoreview-"):] if stem.startswith("kanoreview-") else stem) or {}
    txt = " ".join(e.get("perf") or []) if isinstance(e.get("perf"), list) else str(e.get("perf", ""))
    est = re.search(r"drawEstimate=(\d+)", txt); eng = re.search(r"engineDraws=(\d+)", txt); tri = re.search(r"visibleTris=(\d+)", txt)
    if not est: return ""
    return f"kit draw estimate {est.group(1)}" + (f", engine draws {eng.group(1)}" if eng else "") + (f", visible triangles {tri.group(1)}" if tri else "") + " (software GL: frame times are meaningless)"


def font(size: int, bold=False):
    for f in ("/usr/share/fonts/truetype/dejavu/DejaVuSans%s.ttf" % ("-Bold" if bold else ""), "DejaVuSans.ttf"):
        try: return ImageFont.truetype(f, size)
        except OSError: pass
    return ImageFont.load_default()


def wrap(d: ImageDraw.ImageDraw, text: str, f, width: int) -> list[str]:
    out, cur = [], ""
    for w in text.split():
        t = (cur + " " + w).strip()
        if d.textlength(t, font=f) <= width: cur = t
        else: out.append(cur); cur = w
    return out + ([cur] if cur else [])


def captioned(img: Image.Image, title: str, lines: list[str], width: int | None = None) -> Image.Image:
    """The picture with a caption bar under it (title in bold, then wrapped detail lines)."""
    if width: img = img.resize((width, round(img.height * width / img.width)), Image.LANCZOS)
    big, small = font(max(14, img.width // 55), True), font(max(11, img.width // 80))
    probe = ImageDraw.Draw(Image.new("RGB", (img.width, 10)))
    rows = [(title, big)] + [(l, small) for t in lines for l in wrap(probe, t, small, img.width - 24)]
    h = 12 + sum(round(f.size * 1.35) for _, f in rows) + 8
    out = Image.new("RGB", (img.width, img.height + h), (24, 26, 30)); out.paste(img.convert("RGB"), (0, 0)); d = ImageDraw.Draw(out); y = img.height + 8
    for t, f in rows: d.text((12, y), t, fill=(240, 240, 240) if f is big else (200, 205, 210), font=f); y += round(f.size * 1.35)
    return out


def label(version: str, run_id: str, summary: dict, stem: str, img: Image.Image, width: int | None = None) -> Image.Image:
    p = parse(stem)
    return captioned(img, f"{version} | {stem[len('kanoreview-'):]} | NOT APPROVED",
                     [describe(p), f"Unity WebGL build of review run {run_id}, the game's own lighting at 15:30, quality tier medium unless the variant says otherwise, 1280 x 720; " + numbers(summary, stem)], width)


def main() -> int:
    ap = argparse.ArgumentParser(); ap.add_argument("--run", required=True); ap.add_argument("--run-id", required=True); ap.add_argument("--version", required=True); ap.add_argument("--out", required=True)
    ap.add_argument("--other", default=""); ap.add_argument("--other-id", default=""); ap.add_argument("--other-version", default="")
    a = ap.parse_args(); out = Path(a.out); (out / a.version).mkdir(parents=True, exist_ok=True)
    run = Path(a.run); summary = json.loads((run / "kanoreview-summary.json").read_text()); pics = sorted(run.glob("kanoreview-*.png"))
    for f in pics: label(a.version, a.run_id, summary, f.stem, Image.open(f)).save(out / a.version / f.name)
    print(f"{len(pics)} labelled pictures of {a.version} -> {out / a.version}")
    if a.other:
        other = Path(a.other); osum = json.loads((other / "kanoreview-summary.json").read_text()); n = 0
        for v in STANDARD:
            fa, fb = other / f"kanoreview-{v}.png", run / f"kanoreview-{v}.png"
            if not (fa.exists() and fb.exists()): continue
            L = label(a.other_version, a.other_id, osum, f"kanoreview-{v}", Image.open(fa), 960); R = label(a.version, a.run_id, summary, f"kanoreview-{v}", Image.open(fb), 960)
            sheet = Image.new("RGB", (1930, max(L.height, R.height)), (24, 26, 30)); sheet.paste(L, (0, 0)); sheet.paste(R, (970, 0)); sheet.save(out / f"compare_{v}_{a.other_version}_vs_{a.version}.png"); n += 1
        print(f"{n} comparison sheets ({a.other_version} left, {a.version} right)")
    n = 0
    for base in SCENES:
        cells = [(s, v) for s in ("dry", "wet") for v in ("drive", "aerial")]
        ims = [(f"kanoreview-{base}_{s}-{v}", run / f"kanoreview-{base}_{s}-{v}.png") for s, v in cells]
        if not all(p.exists() for _, p in ims): continue
        tiles = [label(a.version, a.run_id, summary, stem, Image.open(p), 640) for stem, p in ims]; w, h = tiles[0].size
        sheet = Image.new("RGB", (w * 2 + 10, h * 2 + 10), (24, 26, 30))
        for i, t in enumerate(tiles): sheet.paste(t, ((i % 2) * (w + 10), (i // 2) * (h + 10)))
        sheet.save(out / f"scene_{base}.png"); n += 1
    print(f"{n} scene sheets (dry on top, wet below; drive left, aerial right)")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
