#!/usr/bin/env python3
"""Builds the Kano reference-and-greybox review page (HTML + downscaled JPEGs) into an UNTRACKED folder for publishing as a private artifact.
The supplied photographs and the renders are binary files and stay OUT of the public repository: this script only reads them from where they are and writes to --out.
Usage: python3 tools/kano/build_board.py --uploads <dir with the 5 supplied images> --renders <workspace renders dir> --out <empty output dir>"""
import argparse, glob, html, json, os, shutil
from pathlib import Path
from PIL import Image

ap = argparse.ArgumentParser(); ap.add_argument("--uploads", required=True); ap.add_argument("--renders", required=True); ap.add_argument("--out", required=True)
a = ap.parse_args(); out = Path(a.out); out.mkdir(parents=True, exist_ok=True); (out / "img").mkdir(exist_ok=True)

def jpeg(src, name, width):
    im = Image.open(src).convert("RGB")
    if im.width > width: im = im.resize((width, round(im.height * width / im.width)), Image.LANCZOS)
    p = out / "img" / (name + ".jpg"); im.save(p, "JPEG", quality=82, optimize=True); return "img/" + p.name

def latest(sub):
    fs = sorted(glob.glob(os.path.join(a.renders, sub, "*.png"))); return fs[-1]

# supplied photographs: #1 = 61644f7c, #2 (phone screenshot of #3) = 328778af (not shown twice), #3 = 06a8e437, #4 = 5724097c, #5 = 9cceedcf
U = lambda h: os.path.join(a.uploads, h + "-image.png")
photos = [
    ("1", jpeg(U("61644f7c"), "ref1", 1000), "Dense old urban fabric around a rock outcrop",
     "Oblique aerial of a dense, low-rise fabric around a large quarried outcrop with a reddish-brown face. Flat or low-pitched roofs, many with corrugated sheet and roof tanks. Tan and ochre walls, irregular lanes, one straight main road along the top, very few trees. Warm brown-orange palette overall.",
     "Consistent with Dala Hill and the old city, as the brief describes, but the image alone cannot confirm it.",
     "Palette, density, roofscape, the outcrop as a landmark silhouette.", "Building proportions (too distant).", ["Location unconfirmed", "Rights unconfirmed"]),
    ("2 and 3", jpeg(U("06a8e437"), "ref3", 1000), "Planned modern residential district",
     "Two to four storey buildings with hipped or low-pitched roofs in red, brown, cream, white and green. Walled plots, mature trees and palms, a wide street with parked cars at the left, a line of hills on the horizon. Image 2 is a phone screenshot of the same photograph.",
     "The hills on the horizon are not what I would expect on Kano's flat plain. This may be another Nigerian city.",
     "Modern building typology and street rhythm, once the city is confirmed.", "Kano evidence, until you confirm where it is.", ["Location unconfirmed", "Rights unconfirmed"]),
    ("4", jpeg(U("5724097c"), "ref4", 1000), "Elevated road, roundabout garden, mosque",
     "Elevated multi-lane road with curving ramps, a landscaped roundabout with a fountain, a mosque with two minarets and a green dome at the right, mixed mid-rise buildings, mature trees, streetlights at regular spacing along the road edges.",
     "Reads as a Northern Nigerian city; which one is not stated.",
     "Mosque type, modern road furniture, interchange form.", "Anything the game's road generator must build today: it cannot make ramps or roundabouts (see the road limits).", ["Location unconfirmed", "Rights unconfirmed"]),
    ("5", jpeg(U("9cceedcf"), "ref5", 1000), "Multi-level interchange over a hazy flat city",
     "A grade-separated interchange with several loops over a flat, hazy city, a tall tower at the right edge. The image carries a photographer's watermark.",
     "Flat plain and dust haze match Kano's setting; the location is not stated.",
     "Atmosphere: haze, flat horizon, dusty light.", "Texture or model source in any form. A watermarked image is someone's work: private reference only.", ["Watermarked", "Location unconfirmed", "Rights unconfirmed"]),
]
views = [("overview", "Oblique overview from behind the entrance", "overview"), ("plan_A", "Plan: modern district (stations 450 to 830)", "plan_a_modern_district"), ("plan_B", "Plan: commercial spine (830 to 1240)", "plan_b_commercial_spine"),
         ("plan_C", "Plan: old-neighbourhood edge (1240 to 1650)", "plan_c_old_neighbourhood"), ("v1", "Entrance, station 30: gantry, median lights, neem rows", "01_entrance_s030"),
         ("v2", "Modern district, station 480", "02_modern_s480"), ("v3", "Approaching junction j1, station 590", "03_junction_j1_s590"), ("v4", "Commercial spine, station 870", "04_commercial_s870"),
         ("v5", "Damaged section, station 1,060", "05_damaged_s1060"), ("v6", "Old-neighbourhood edge, station 1,270", "06_old_s1270"), ("v7", "Approaching the mosque, station 1,380", "07_mosque_s1380"),
         ("v8", "Down mosque street (j5) from the main road", "08_mosque_street_j5")]
renders = [(jpeg(latest("kano_greybox_" + sub), k, 1100), cap) for (k, cap, sub) in views]

stats = json.load(open("docs/kano/kano_slice_stats.json")); frame = json.load(open("docs/kano/kano_world_frame.json"))
T = stats["tiers"]

def chips(c): return "".join(f'<span class="chip">{html.escape(x)}</span>' for x in c)
cards = "".join(f'''<article class="card"><img src="{src}" alt="{html.escape(title)}" loading="eager"><div class="body"><p class="no">Supplied image {no}</p><h3>{html.escape(title)}</h3><div class="chips">{chips(tags)}</div>
<p>{html.escape(read)}</p><p class="note">{html.escape(caveat)}</p><dl><dt>Usable for</dt><dd>{html.escape(use)}</dd><dt>Not usable for</dt><dd>{html.escape(notuse)}</dd></dl></div></article>''' for (no, src, title, read, caveat, use, notuse, tags) in photos)
gallery = "".join(f'<figure><img src="{src}" alt="{html.escape(cap)}"><figcaption>{html.escape(cap)}</figcaption></figure>' for (src, cap) in renders)

PAGE = f'''<!DOCTYPE html>
<html lang="en"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width, initial-scale=1">
<title>Kano Reference Board</title>
<link rel="preconnect" href="https://fonts.googleapis.com"><link rel="preconnect" href="https://fonts.gstatic.com" crossorigin>
<link href="https://fonts.googleapis.com/css2?family=Fraunces:opsz,wght@9..144,600&family=Public+Sans:wght@400;500;600&display=swap" rel="stylesheet">
<style>
/* layout: a review sheet. A status strip, supplied photographs with readings, verified facts beside design choices, gaps, then the greybox. Indigo (the Kofar Mata dye) on dust. */
:root {{ --bg:#f3f0ea; --panel:#fbf9f5; --ink:#1d2230; --mute:#5c6070; --line:#d9d4c8; --accent:#2c3f7a; --accent-ink:#ffffff; --warn:#8a4b00; --warn-bg:#f8e9d2; --ok:#1d6b45;
  --display:"Fraunces", Georgia, serif; --body:"Public Sans", system-ui, sans-serif; }}
@media (prefers-color-scheme: dark) {{ :root:not([data-theme="light"]) {{ --bg:#14171f; --panel:#1b1f2a; --ink:#e8e6e1; --mute:#a2a5b2; --line:#303544; --accent:#8fa6f0; --accent-ink:#10131c; --warn:#f0c27a; --warn-bg:#3a2d17; --ok:#7fd0a3; }} }}
:root[data-theme="dark"] {{ --bg:#14171f; --panel:#1b1f2a; --ink:#e8e6e1; --mute:#a2a5b2; --line:#303544; --accent:#8fa6f0; --accent-ink:#10131c; --warn:#f0c27a; --warn-bg:#3a2d17; --ok:#7fd0a3; color-scheme:dark; }}
* {{ box-sizing:border-box; }} html,body {{ margin:0; }} body {{ background:var(--bg); color:var(--ink); font:16px/1.55 var(--body); }}
main {{ max-width:1180px; margin:0 auto; padding:24px 16px 64px; }}
h1,h2,h3 {{ font-family:var(--display); font-weight:600; text-wrap:balance; line-height:1.15; margin:0; }} h1 {{ font-size:clamp(28px,4vw,40px); }} h2 {{ font-size:26px; margin:44px 0 6px; }} h3 {{ font-size:19px; }}
p {{ margin:0 0 10px; max-width:70ch; }} a {{ color:var(--accent); }} .sub {{ color:var(--mute); max-width:75ch; }}
.strip {{ display:grid; grid-template-columns:repeat(auto-fit,minmax(190px,1fr)); gap:10px; margin:20px 0 6px; }}
.stat {{ background:var(--panel); border:1px solid var(--line); border-left:4px solid var(--accent); padding:10px 14px; border-radius:6px; }} .stat b {{ display:block; font:600 26px var(--display); font-variant-numeric:tabular-nums; }} .stat span {{ color:var(--mute); font-size:14px; }}
.stat.warn {{ border-left-color:var(--warn); }}
.cards {{ display:grid; grid-template-columns:repeat(auto-fit,minmax(min(100%,520px),1fr)); gap:16px; margin-top:16px; }}
.card {{ background:var(--panel); border:1px solid var(--line); border-radius:8px; overflow:hidden; display:flex; flex-direction:column; }} .card img {{ width:100%; height:auto; display:block; }} .card .body {{ padding:14px 16px 16px; }}
.no {{ color:var(--mute); font-size:13px; text-transform:uppercase; letter-spacing:.06em; margin:0 0 4px; }}
.chips {{ display:flex; flex-wrap:wrap; gap:6px; margin:8px 0; }} .chip {{ background:var(--warn-bg); color:var(--warn); border-radius:99px; padding:2px 10px; font-size:13px; font-weight:600; }}
.note {{ color:var(--warn); font-weight:500; }} dl {{ margin:10px 0 0; display:grid; grid-template-columns:110px 1fr; gap:4px 12px; font-size:15px; }} dt {{ color:var(--mute); }} dd {{ margin:0; }}
.cols {{ display:grid; grid-template-columns:repeat(auto-fit,minmax(min(100%,520px),1fr)); gap:16px; margin-top:14px; }} .panel {{ background:var(--panel); border:1px solid var(--line); border-radius:8px; padding:6px 16px 12px; }}
.panel h3 {{ margin:12px 0 8px; }} .panel.verified h3 {{ color:var(--ok); }} .panel.creative h3 {{ color:var(--warn); }}
ul.f {{ list-style:none; margin:0; padding:0; }} ul.f li {{ padding:9px 0; border-top:1px solid var(--line); font-size:15px; }} ul.f li:first-child {{ border-top:0; }} ul.f .src {{ display:block; color:var(--mute); font-size:13px; }}
.gallery {{ display:grid; grid-template-columns:repeat(auto-fit,minmax(min(100%,540px),1fr)); gap:14px; margin-top:14px; }} figure {{ margin:0; background:var(--panel); border:1px solid var(--line); border-radius:8px; overflow:hidden; }} figure img {{ width:100%; height:auto; display:block; }} figcaption {{ padding:8px 12px; font-size:14px; color:var(--mute); }}
.tier {{ overflow-x:auto; }} table {{ border-collapse:collapse; width:100%; font-size:14.5px; font-variant-numeric:tabular-nums; }} th,td {{ text-align:left; padding:7px 10px; border-bottom:1px solid var(--line); white-space:nowrap; }} th {{ color:var(--mute); font-weight:600; }}
.legend {{ display:flex; flex-wrap:wrap; gap:8px 16px; margin:10px 0; font-size:14px; }} .legend i {{ display:inline-block; width:12px; height:12px; border-radius:2px; margin-right:6px; vertical-align:-1px; border:1px solid var(--line); }}
.check li {{ margin:4px 0; }}
</style></head><body><main>
<h1>Kano Reference Board</h1>
<p class="sub">A reference board and a greybox review sheet for the Kano vertical slice. It is not a model and not a city: five photographs you supplied with my reading of them, what research verified and what is design choice, what is still missing, and the plain-volume greybox built from the game's own road and placement data.</p>
<div class="strip">
  <div class="stat"><b>5</b><span>photographs supplied (4 distinct)</span></div>
  <div class="stat warn"><b>0</b><span>locations confirmed</span></div>
  <div class="stat warn"><b>0</b><span>Hausa compound references yet</span></div>
  <div class="stat"><b>{stats["instanceTotal"]}</b><span>greybox instances, 0 modelled assets</span></div>
  <div class="stat"><b>{stats["routeLengthM"]:.0f} m</b><span>draft corridor, 1:1 scale</span></div>
</div>

<h2>1. What you supplied</h2>
<p class="sub">I cannot verify where any of these were taken. Each card says what is visible and what it is good for. Please confirm the location and your right to use each as a reference.</p>
<div class="cards">{cards}</div>

<h2>2. Verified, and design choice</h2>
<div class="cols">
<div class="panel verified"><h3>Verified from sources</h3><ul class="f">
<li>Kano is at about 12°00'N 8°31'E, about 484 to 488 m above sea level, on a nearly flat plain.<span class="src"><a href="https://www.wikipedia.org/wiki/Kano_City">Wikipedia</a>, <a href="https://worldatlas.com/af/ng/kn/where-is-kano.html">WorldAtlas</a></span></li>
<li>Gidan Rumfa, the Emir's Palace, was built in the 15th century under Muhammadu Rumfa (reigned 1463 to 1499), covers about 33 acres, was partly rebuilt in the early 19th century, and sits beside the Central Mosque.<span class="src"><a href="https://archnet.org/sites/3778">ArchNet</a>, <a href="https://atlasobscura.com/places/gidan-rumfa-emirs-palace">Atlas Obscura</a></span></li>
<li>Kurmi Market was founded by Muhammad Rumfa in the 15th century as a trade point of the trans-Saharan trade.<span class="src"><a href="https://en.wikipedia.org/wiki/Kurmi_Market">Wikipedia</a>, <a href="https://whc.unesco.org/en/tentativelists/5171/">UNESCO tentative list</a></span></li>
<li>Dala Hill is in Dala Local Government Area; Kano was first called Dala after it.<span class="src"><a href="https://en.wikipedia.org/wiki/Dalla_Hill">Wikipedia</a></span></li>
<li>Kofar Mata Dye Pits: 1498 on the entrance sign, old city, near the wall and palace; one survivor of 13 pit complexes.<span class="src"><a href="https://en.wikipedia.org/wiki/Kofar_Mata_Dye_Pits">Wikipedia</a>, <a href="https://www.historynewsnetwork.org/article/38148">History News Network</a></span></li>
<li>Hausa urban building: a walled compound, a zaure (the front reception room), tubali mud brick, thick walls tapering upward, flat mud-arch roofs, rarely more than two storeys, high parapets giving a level skyline, zankwaye pinnacles.<span class="src"><a href="https://en.wikipedia.org/wiki/Hausa_architecture">Wikipedia</a>, <a href="https://www.africanhistoryextra.com/p/hausa-urban-architecture-construction">African History Extra</a>, <a href="https://archnet.org/sites/4180">ArchNet</a></span></li>
<li>Harmattan dust haze, most pronounced November to March.<span class="src"><a href="https://kanofocus.com/2026/01/02/delayed-weaker-harmattan-signals-climate-change-impact-across-northern-nigeria-anka">Kano Focus</a></span></li>
</ul>
<h3 style="color:var(--mute)">Searched for and not found</h3><ul class="f"><li>The height of the old city wall.</li><li>Dimensions of any Kano compound, zaure, gate or wall.</li><li>Whether neem is the typical street tree.</li><li>What Kano's electricity poles and streetlights are made of.</li><li>Surveyed coordinates of Gidan Rumfa, Kurmi Market and Kofar Mata.</li><li>Dala Hill's height: 534 m in most sources, 453.67 m in one.</li></ul></div>
<div class="panel creative"><h3>Design choice (mine, to review)</h3><ul class="f">
<li>The slice is a fictional neighbourhood on the Kaduna approach to Kano. No street, plot or building in it is real.</li>
<li>The corridor runs south-west to north-east (bearing 36°), the great-circle direction from Kaduna to Kano computed from the game's own world-map coordinates. Its position, about 4 km south-west of the Kano reference point, is a design choice.</li>
<li>Four zones in the order a driver meets them: city entrance, modern district, commercial spine, old-neighbourhood edge.</li>
<li>Every plot size, setback and spacing in the greybox is a placeholder envelope.</li>
<li>A neighbourhood mosque turned to the qibla ({frame["qibla"]["bearingDeg"]}° from Kano, computed), so it sits skewed to the street. Its type is yours to specify.</li>
<li>The real landmarks are left out of this slice and reserved for an old-city slice: their coordinates are unverified, so none are invented.</li>
</ul>
<h3 style="color:var(--warn)">Open decision</h3><ul class="f"><li>The game's world map puts Kano at 8.592°E; the reference sources put it at about 8.5167°E, which is about {frame["worldMapDiscrepancy"]["distanceKm"]} km west. Neither was changed.</li></ul></div>
</div>

<h2>3. What the reference pack still needs</h2>
<p class="sub">For each, a front, a side, a close detail and something for scale.</p>
<div class="panel"><ul class="check">
<li><b>Traditional Hausa compounds in Kano</b>: the street front and zaure, wall and wall top, gates and doors, parapet and zankwaye, roof edge, finishes. At least three distinct plots.</li>
<li><b>Modern buildings as seen in Kano</b>: walled bungalows and villas, shop rows, plazas, apartment blocks, mid-rise, a filling station.</li>
<li><b>A neighbourhood mosque</b> (not the Great Mosque).</li>
<li><b>Roads</b>: new, patched, cracked and damaged asphalt; edges, drains and slabs, kerbs, sidewalks, markings, bus stops.</li>
<li><b>Poles and lights as installed</b>, signs, billboards.</li>
<li><b>Trees and ground cover</b> that line these roads.</li>
<li><b>Vehicles</b> to park along the road, generic and brand-free.</li>
<li><b>Light and haze</b>, time of day.</li>
</ul></div>

<h2>4. The greybox</h2>
<p class="sub">Plain volumes, generated from the game's own road geometry, terrain and the {stats["instanceTotal"]} prefab instances its scatter and set pieces produce. Colours code the category of each volume and say nothing about real colours. Judge rhythm, scale and layout only.</p>
<div class="legend"><span><i style="background:#bd7542"></i>traditional plots</span><span><i style="background:#8ca9cc"></i>modern plots</span><span><i style="background:#dbb838"></i>shop rows</span><span><i style="background:#9e85c7"></i>plaza, apartments, mid-rise</span><span><i style="background:#339e5c"></i>mosque</span><span><i style="background:#d14233"></i>petrol station</span><span><i style="background:#eb8f1a"></i>street props</span><span><i style="background:#1a1a1c"></i>poles, lights, signs</span><span><i style="background:#21611f"></i>trees</span><span><i style="background:#4d85d9"></i>vehicles</span></div>
<div class="gallery">{gallery}</div>
<h3 style="margin-top:24px">Budget outlook (computed, not measured)</h3>
<p class="sub">Upper bounds from the game's own scatter and level-of-detail policy, with one draw call per placed prefab and planned triangle budgets. No model exists yet, and no frame time has been measured on any real GPU.</p>
<div class="panel tier"><table><thead><tr><th>Tier</th><th>Instances in slice</th><th>Draw calls at the worst spot (budget)</th><th>Planned triangles (budget)</th><th>Design target</th></tr></thead><tbody>
{"".join(f"<tr><td>{n}</td><td>{T[n]['instancesInSlice']}</td><td>{T[n]['worstMoment']['drawnUpperBoundDrawCalls']} ({T[n]['budget']['drawCalls']})</td><td>{T[n]['worstMoment']['plannedTriangles']:,} ({T[n]['budget']['triangles']:,})</td><td>60% draws, 45% triangles</td></tr>" for n in ("low","medium","high"))}
</tbody></table></div>
<p class="sub" style="margin-top:16px">The full package, the data files and the decisions I need are in <code>docs/KANO_VERTICAL_SLICE.md</code>.</p>
</main></body></html>'''
(out / "index.html").write_text(PAGE, encoding="utf-8")
print("wrote", out / "index.html", sum(p.stat().st_size for p in out.rglob("*") if p.is_file()), "bytes total")
