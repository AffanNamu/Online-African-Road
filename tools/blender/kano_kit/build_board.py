"""Builds the private review page of the Kano residential milestone (HTML + downscaled images) into an UNTRACKED folder, for publishing as a PRIVATE artifact.
It shows the concept R-006 beside the Blender render from the same camera, the other views, the asset list with measured numbers, the honest differences and what is not done. The concept image is only read,
downscaled and written to --out; it never enters the repository. The page must not be shared.
Usage: python3 -m kano_kit.build_board --concept <R-006 image> --renders <dir with kano_residential_*.png> --report <build report json> --out <empty dir>"""
import argparse, html, json
from pathlib import Path
from PIL import Image

ap = argparse.ArgumentParser(); ap.add_argument("--concept", required=True); ap.add_argument("--renders", required=True); ap.add_argument("--report", required=True); ap.add_argument("--out", required=True)
a = ap.parse_args(); out = Path(a.out); (out / "img").mkdir(parents=True, exist_ok=True); e = html.escape


def jpg(src, name, width=1280, q=84):
    im = Image.open(src).convert("RGB")
    if im.width > width: im = im.resize((width, round(im.height * width / im.width)), Image.LANCZOS)
    p = out / "img" / (name + ".jpg"); im.save(p, "JPEG", quality=q, optimize=True); return "img/" + p.name


R = Path(a.renders); report = json.loads(Path(a.report).read_text())
views = [("ref", "Reference camera", "Eye level 1.7 m at the left kerb line, 28 mm, looking down the street: the framing of concept R-006."), ("front", "Front view", "From the left sidewalk, facing the right-hand frontages: walls, piers, gate and villa."),
         ("side", "Side view", "Along the street from beyond the section start: the street profile and both rows."), ("street1", "Street level, pedestrian", "On the right sidewalk, looking back toward the left-hand gate and wall."),
         ("street2", "Street level, driver", "Driver's eye (1.2 m) in the left lane."), ("top", "Plan view", "The 96 m section from above: four plots a side.")]
img = {k: jpg(R / f"kano_residential_{k}.png", k) for k, _, _ in views if (R / f"kano_residential_{k}.png").is_file()}
concept = jpg(a.concept, "concept_r006", 1280)
rows = "".join(f"<tr><td><code>{e(x['id'])}</code></td><td>{e(x['kind'])}</td><td>{x['stats']['lod_triangles']}</td><td>{x['stats']['size_m']}</td><td>{len(x['stats']['materials'])}</td><td>{'passed' if x['passed'] else 'FAILED'}</td></tr>" for x in report)
cards = "".join(f'<figure><img src="{img[k]}" alt="{e(t)}"><figcaption><b>{e(t)}</b> {e(d)}</figcaption></figure>' for k, t, d in views[1:] if k in img)
DIFF = [("villas", "plainer than the concept's; mostly hidden by trees and walls from the reference camera", "significant", "second detail pass: stone cladding, arches, richer balcony and landscaping"),
        ("gate", "ornate with an arched top; scrollwork simpler than the concept's", "moderate", "richer scroll pattern"), ("trees", "leaf-card crowns, darker and with jagged leaf edges", "moderate", "brighter, finer leaf atlas; real species when known"),
        ("paver sidewalk", "slightly more orange variation than the concept's pink and grey", "small", "tone the palette down"), ("materials", "flat colours plus two textures; no normal or roughness maps", "moderate to significant", "PBR set after the Unity import shows what matters"),
        ("hedges and shrubs", "present, less lush than the concept's", "moderate", "denser leaf cards and flowers"), ("glass (balcony balustrade, windows)", "the balustrade reads as an opaque pale panel: the kit has no transparent material yet; window glass is a dark opaque pane", "moderate", "a transparent kit material in the Unity builder and Blender export"), ("far end", "a long street without the concept's skyline and mosque", "intentional", "a generic skyline later; the mosque is treated as generic until an authentic reference exists")]
drows = "".join(f"<tr><td>{e(a_)}</td><td>{e(b)}</td><td>{e(c)}</td><td>{e(d)}</td></tr>" for a_, b, c, d in DIFF)
page = f"""<!DOCTYPE html>
<html lang="en"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width, initial-scale=1"><title>Kano Residential Street</title>
<link rel="preconnect" href="https://fonts.googleapis.com"><link href="https://fonts.googleapis.com/css2?family=Fraunces:wght@600&family=Public+Sans:wght@400;500;600&display=swap" rel="stylesheet">
<style>
/* layout: the concept beside its Blender render first, then the other views, the asset table, the open differences and the status. Indigo (the Kofar Mata dye) on dust. */
:root {{ --bg:#f3f0ea; --panel:#fbf9f5; --ink:#1d2230; --mute:#5c6070; --line:#d9d4c8; --accent:#2c3f7a; --warn:#8a4b00; --warn-bg:#f8e9d2; --display:"Fraunces", Georgia, serif; --body:"Public Sans", system-ui, sans-serif; }}
@media (prefers-color-scheme: dark) {{ :root:not([data-theme="light"]) {{ --bg:#14171f; --panel:#1b1f2a; --ink:#e8e6e1; --mute:#a2a5b2; --line:#303544; --accent:#8fa6f0; --warn:#f0c27a; --warn-bg:#3a2d17; color-scheme:dark; }} }}
:root[data-theme="dark"] {{ --bg:#14171f; --panel:#1b1f2a; --ink:#e8e6e1; --mute:#a2a5b2; --line:#303544; --accent:#8fa6f0; --warn:#f0c27a; --warn-bg:#3a2d17; color-scheme:dark; }}
* {{ box-sizing:border-box; }} html,body {{ margin:0; }} body {{ background:var(--bg); color:var(--ink); font:16px/1.55 var(--body); }}
main {{ max-width:1180px; margin:0 auto; padding:24px 16px 64px; }} h1,h2 {{ font-family:var(--display); font-weight:600; text-wrap:balance; line-height:1.15; margin:0; }} h1 {{ font-size:clamp(28px,4vw,40px); }} h2 {{ font-size:25px; margin:40px 0 6px; }}
p {{ margin:0 0 10px; max-width:76ch; }} .sub {{ color:var(--mute); }} .no {{ color:var(--mute); font-size:12.5px; text-transform:uppercase; letter-spacing:.06em; margin:0 0 4px; }}
.private,.status {{ border-radius:6px; padding:10px 14px; font-weight:600; margin:14px 0; max-width:none; background:var(--warn-bg); color:var(--warn); }}
.pair {{ display:grid; grid-template-columns:repeat(auto-fit,minmax(min(100%,560px),1fr)); gap:14px; margin-top:12px; }} .gallery {{ display:grid; grid-template-columns:repeat(auto-fit,minmax(min(100%,520px),1fr)); gap:14px; margin-top:12px; }}
figure {{ margin:0; background:var(--panel); border:1px solid var(--line); border-radius:8px; overflow:hidden; }} figure img {{ width:100%; height:auto; display:block; }} figcaption {{ padding:8px 12px; color:var(--mute); font-size:14px; }}
.panel {{ background:var(--panel); border:1px solid var(--line); border-radius:8px; padding:8px 16px 12px; margin-top:12px; overflow-x:auto; }} table {{ border-collapse:collapse; width:100%; font-size:14.5px; }}
th,td {{ text-align:left; vertical-align:top; padding:7px 10px; border-bottom:1px solid var(--line); }} th {{ color:var(--mute); font-weight:600; white-space:nowrap; }} ul {{ max-width:80ch; }}
</style></head><body><main>
<p class="no">African Roads Online · Kano · first Blender production milestone · 2026-11-01</p><h1>Kano residential street</h1>
<p class="sub">The one-way residential street of concept R-006 as real Blender geometry: road with markings, kerbs, paver sidewalks, villas on both sides, compound walls, ornate gates, trees and shrubs, streetlights, a concrete electricity pole with overhead wires. 14 assets, 96 m, validated against the game's budgets.</p>
<p class="private">PRIVATE REVIEW PAGE. It carries a thumbnail of the concept image, which is not cleared for redistribution. Do not share this page or its link.</p>
<p class="status">Status: Blender validation passed. NOT yet imported into the running game, NOT visually approved. These are Blender renders, not Unity screenshots.</p>
<h2>Concept and Blender render, same camera</h2>
<div class="pair"><figure><img src="{concept}" alt="Concept R-006"><figcaption><b>Concept R-006</b> (ChatGPT-generated; the visual design standard)</figcaption></figure>
<figure><img src="{img.get('ref', '')}" alt="Blender render from the reference camera"><figcaption><b>Blender render</b>, reference camera (1.7 m, 28 mm). Cycles, real geometry.</figcaption></figure></div>
<h2>Other views</h2><div class="gallery">{cards}</div>
<h2>The 14 assets</h2><div class="panel"><table><thead><tr><th>Asset</th><th>Kind</th><th>LOD triangles</th><th>Size (m)</th><th>Materials</th><th>Validation</th></tr></thead><tbody>{rows}</tbody></table></div>
<h2>What is still different from the concept</h2><div class="panel"><table><thead><tr><th>Element</th><th>Difference</th><th>Weight</th><th>Planned correction</th></tr></thead><tbody>{drows}</tbody></table></div>
<h2>What is not done</h2><ul><li>Unity: the code that builds the prefabs and shows the street in the running game is written, but not yet run on these assets. The assets are private and need a private build (three Unity secrets in the private repository, set by the owner).</li>
<li>The electricity pole and wires are an addition you asked for; the concept has none and no Kano reference exists, so the design is generic.</li><li>No mosque: treated as a generic mosque until an authentic reference exists.</li>
<li>The expected draw cost of the section is about 400 calls if nothing batches (an estimate): the game will tell, and the one-plot-one-mesh step stays required.</li></ul>
</main></body></html>
"""
(out / "index.html").write_text(page, encoding="utf-8"); print("wrote", out / "index.html", len(page))
