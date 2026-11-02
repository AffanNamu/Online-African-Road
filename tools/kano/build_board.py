#!/usr/bin/env python3
"""Builds the Kano review page, revision 2 (HTML + downscaled JPEGs) into an UNTRACKED folder for publishing as a PRIVATE artifact.
Reads the generated evidence files in docs/kano/. The supplied reference screenshots and the renders are binary files: they are only read from where they are, downscaled, and written to --out;
they never enter the repository. The page must not be shared (the references are not cleared for any redistribution).
Usage: python3 tools/kano/build_board.py --uploads <dir with the 5 screenshots> --renders <workspace renders dir> --layout <layout.png> --out <empty dir>"""
import argparse, glob, html, json, os
from pathlib import Path
from PIL import Image

ap = argparse.ArgumentParser(); ap.add_argument("--uploads", required=True); ap.add_argument("--renders", required=True); ap.add_argument("--layout", required=True); ap.add_argument("--out", required=True)
a = ap.parse_args(); out = Path(a.out); (out / "img").mkdir(parents=True, exist_ok=True)
K = Path(__file__).resolve().parents[2] / "docs" / "kano"
reg = json.loads((K / "reference_register.json").read_text()); st = json.loads((K / "kano_slice_stats.json").read_text()); fr = json.loads((K / "kano_world_frame.json").read_text())
wk = json.loads((K / "kano_workarounds.json").read_text()); tx = json.loads((K / "texture_findings.json").read_text()); inv = json.loads((K / "kano_asset_inventory.json").read_text())


def jpeg(src, name, width, quality=80):
    im = Image.open(src).convert("RGB")
    if im.width > width: im = im.resize((width, round(im.height * width / im.width)), Image.LANCZOS)
    p = out / "img" / (name + ".jpg"); im.save(p, "JPEG", quality=quality, optimize=True); return "img/" + p.name


def latest(sub):
    return sorted(glob.glob(os.path.join(a.renders, sub, "*.png")))[-1]


E = html.escape
U = lambda h: os.path.join(a.uploads, h + "-image.png")
SRC = {"R-001": "61644f7c", "R-002": None, "R-003": "06a8e437", "R-004": "5724097c", "R-005": "9cceedcf"}      # R-002 duplicates R-003 and is not shown twice
thumbs = {k: jpeg(U(h), "ref_" + k.lower(), 560, 70) for k, h in SRC.items() if h}
ref_cards = ""
for r in reg["references"]:
    img = f'<img src="{thumbs[r["id"]]}" alt="{E(r["label"])}">' if r["id"] in thumbs else '<div class="dup">Same photograph as R-003, as a phone screenshot with viewer buttons. Not shown twice.</div>'
    chips = '<span class="chip">ChatGPT-provided research</span><span class="chip">Source unknown</span><span class="chip">Location not established</span><span class="chip">Rights unknown</span>' + ('<span class="chip">Watermarked</span>' if r["id"] == "R-005" else "")
    ref_cards += f'''<article class="card">{img}<div class="body"><p class="no">{r["id"]}</p><h3>{E(r["label"])}</h3><div class="chips">{chips}</div>
<p><b>Owner's reading:</b> {E(r["ownerInterpretation"])}</p><p>{E(r["observed"])}</p><p class="note">{E(r["actualLocation"])}</p>
<dl><dt>Usable for</dt><dd>{E(r["usableFor"])}</dd><dt>Not usable for</dt><dd>{E(r["notUsableFor"])}</dd><dt>Private reference</dt><dd>study aid only, owner's decision</dd><dt>Public documentation</dt><dd><b>no</b></dd><dt>Redistribution</dt><dd><b>no</b></dd></dl></div></article>'''
miss = "".join(f'<tr><td>{m["id"]}</td><td>{E(m["area"])}</td><td>{E(m["need"])}</td><td class="st">{E(m["status"])}</td></tr>' for m in reg["missingReferences"])
views = [("overview", "Oblique overview from behind the entrance", "overview"), ("plan_a", "Plan: modern district (stations 450 to 830)", "plan_a_modern_district"), ("plan_b", "Plan: commercial spine (830 to 1240)", "plan_b_commercial_spine"),
         ("plan_c", "Plan: old-neighbourhood edge (1240 to 1650)", "plan_c_old_neighbourhood"), ("v1", "Entrance, station 30", "01_entrance_s030"), ("v2", "Modern district, station 480", "02_modern_s480"),
         ("v3", "Approaching junction j1, station 590", "03_junction_j1_s590"), ("v4", "Commercial spine, station 870", "04_commercial_s870"), ("v5", "Damaged section, station 1,060", "05_damaged_s1060"),
         ("v6", "Old-neighbourhood edge, station 1,270", "06_old_s1270"), ("v7", "Approaching the mosque, station 1,380", "07_mosque_s1380"), ("v8", "Down mosque street (j5)", "08_mosque_street_j5")]
gal = "".join(f'<figure><img src="{jpeg(latest("kano_greybox_" + sub), k, 1000)}" alt="{E(cap)}"><figcaption>{E(cap)}</figcaption></figure>' for (k, cap, sub) in views)
layout = jpeg(a.layout, "layout", 1300, 86)
T = st["tiers"]
tier_rows = "".join(f"<tr><td>{n}</td><td>{T[n]['instancesInSlice']}</td><td>{T[n]['worstMoment']['drawnUpperBoundDrawCalls']} of {T[n]['budget']['drawCalls']}</td><td>{T[n]['worstMoment']['plannedTriangles']:,} of {T[n]['budget']['triangles']:,}</td></tr>" for n in ("low", "medium", "high"))
wk_rows = "".join(f"<tr><td>{w['id']}</td><td>{E(w['what'])}</td><td>{', '.join(w['standsInFor'])}</td><td>{E(w['removalPath'])}</td></tr>" for w in wk["workarounds"])
tex_rows = "".join(f"<li><b>{E(f['title'])}</b> {E(f['detail'])}</li>" for f in tx["findings"])
tex_settings = "".join(f"<tr><td>{E(k)}</td><td>{E(v)}</td></tr>" for k, v in tx["settingsTable"])
RB = [("RB-01", "Roundabouts"), ("RB-02", "Four-way intersections"), ("RB-03", "Flyovers, interchanges, bridges over roads"), ("RB-04", "Sidewalks and kerbs"), ("RB-05", "Drainage infrastructure"), ("RB-06", "Patched asphalt, cracks, surface-condition layers"),
      ("RB-07", "Text-bearing production road signs"), ("RB-08", "Side-street scatter and junction spacing"), ("RB-09", "Median and near-edge furniture"), ("RB-10", "Production-prefab streaming"), ("RB-11", "Junction mouth geometry")]
rb = "".join(f"<li><b>{i}</b> {n}</li>" for i, n in RB)
ct = fr["canonicalAnchor"]; pin = fr["worldMapPin"]

PAGE = f'''<!DOCTYPE html>
<html lang="en"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width, initial-scale=1">
<title>Kano Review Package</title>
<link rel="preconnect" href="https://fonts.googleapis.com"><link rel="preconnect" href="https://fonts.gstatic.com" crossorigin>
<link href="https://fonts.googleapis.com/css2?family=Fraunces:opsz,wght@9..144,600&family=Public+Sans:wght@400;500;600&display=swap" rel="stylesheet">
<style>
/* layout: a review sheet in the order of the owner's list. Status strip, then one section per deliverable. Indigo (the Kofar Mata dye) on dust. */
:root {{ --bg:#f3f0ea; --panel:#fbf9f5; --ink:#1d2230; --mute:#5c6070; --line:#d9d4c8; --accent:#2c3f7a; --warn:#8a4b00; --warn-bg:#f8e9d2; --ok:#1d6b45; --display:"Fraunces", Georgia, serif; --body:"Public Sans", system-ui, sans-serif; }}
@media (prefers-color-scheme: dark) {{ :root:not([data-theme="light"]) {{ --bg:#14171f; --panel:#1b1f2a; --ink:#e8e6e1; --mute:#a2a5b2; --line:#303544; --accent:#8fa6f0; --warn:#f0c27a; --warn-bg:#3a2d17; --ok:#7fd0a3; }} }}
:root[data-theme="dark"] {{ --bg:#14171f; --panel:#1b1f2a; --ink:#e8e6e1; --mute:#a2a5b2; --line:#303544; --accent:#8fa6f0; --warn:#f0c27a; --warn-bg:#3a2d17; --ok:#7fd0a3; color-scheme:dark; }}
* {{ box-sizing:border-box; }} html,body {{ margin:0; }} body {{ background:var(--bg); color:var(--ink); font:16px/1.55 var(--body); }}
main {{ max-width:1180px; margin:0 auto; padding:24px 16px 64px; }}
h1,h2,h3 {{ font-family:var(--display); font-weight:600; text-wrap:balance; line-height:1.15; margin:0; }} h1 {{ font-size:clamp(28px,4vw,40px); }} h2 {{ font-size:26px; margin:44px 0 6px; }} h3 {{ font-size:19px; }}
p {{ margin:0 0 10px; max-width:72ch; }} a {{ color:var(--accent); }} .sub {{ color:var(--mute); max-width:78ch; }}
.private {{ background:var(--warn-bg); color:var(--warn); border-radius:6px; padding:10px 14px; font-weight:600; margin:14px 0; max-width:none; }}
.strip {{ display:grid; grid-template-columns:repeat(auto-fit,minmax(170px,1fr)); gap:10px; margin:16px 0 6px; }}
.stat {{ background:var(--panel); border:1px solid var(--line); border-left:4px solid var(--accent); padding:10px 14px; border-radius:6px; }} .stat b {{ display:block; font:600 26px var(--display); font-variant-numeric:tabular-nums; }} .stat span {{ color:var(--mute); font-size:14px; }} .stat.warn {{ border-left-color:var(--warn); }}
.cards {{ display:grid; grid-template-columns:repeat(auto-fit,minmax(min(100%,520px),1fr)); gap:16px; margin-top:16px; }}
.card {{ background:var(--panel); border:1px solid var(--line); border-radius:8px; overflow:hidden; }} .card img {{ width:100%; height:auto; display:block; }} .dup {{ padding:40px 16px; color:var(--mute); background:var(--bg); }} .card .body {{ padding:14px 16px 16px; }}
.no {{ color:var(--mute); font-size:13px; text-transform:uppercase; letter-spacing:.06em; margin:0 0 4px; }} .chips {{ display:flex; flex-wrap:wrap; gap:6px; margin:8px 0; }} .chip {{ background:var(--warn-bg); color:var(--warn); border-radius:99px; padding:2px 10px; font-size:13px; font-weight:600; }}
.note {{ color:var(--warn); font-weight:500; }} dl {{ margin:10px 0 0; display:grid; grid-template-columns:130px 1fr; gap:4px 12px; font-size:15px; }} dt {{ color:var(--mute); }} dd {{ margin:0; }}
.panel {{ background:var(--panel); border:1px solid var(--line); border-radius:8px; padding:8px 16px 12px; margin-top:12px; overflow-x:auto; }} .cols {{ display:grid; grid-template-columns:repeat(auto-fit,minmax(min(100%,520px),1fr)); gap:16px; }}
table {{ border-collapse:collapse; width:100%; font-size:14.5px; }} th,td {{ text-align:left; vertical-align:top; padding:7px 10px; border-bottom:1px solid var(--line); }} th {{ color:var(--mute); font-weight:600; }} td.st {{ color:var(--warn); font-weight:500; }}
.gallery {{ display:grid; grid-template-columns:repeat(auto-fit,minmax(min(100%,540px),1fr)); gap:14px; margin-top:14px; }} figure {{ margin:0; background:var(--panel); border:1px solid var(--line); border-radius:8px; overflow:hidden; }} figure img {{ width:100%; height:auto; display:block; }} figcaption {{ padding:8px 12px; font-size:14px; color:var(--mute); }}
.layout img {{ width:100%; height:auto; border:1px solid var(--line); border-radius:8px; }} ul.l {{ margin:6px 0; padding-left:20px; }} ul.l li {{ margin:4px 0; }} .two {{ columns:2 280px; }}
</style></head><body><main>
<h1>Kano Review Package</h1>
<p class="sub">Revision 2, answering the review decisions of 2026-11-01. A reference-provenance report, the private asset repository plan, the coordinate recommendation, the road-system limits and backlog, the Unity texture findings, the layout and greybox, and the updated inventory and budgets. Pre-production: nothing is modelled.</p>
<p class="private">PRIVATE REVIEW PAGE. It shows low-resolution copies of the supplied research images, whose original source and rights are unknown. Do not share it, and do not copy the images out.</p>
<div class="strip">
 <div class="stat warn"><b>0 of 5</b><span>reference locations established</span></div><div class="stat warn"><b>0 of 5</b><span>reference rights confirmed</span></div>
 <div class="stat"><b>0</b><span>modelled assets</span></div><div class="stat"><b>{len(wk["workarounds"])}</b><span>Kano workarounds, all data-only</span></div>
 <div class="stat warn"><b>{len(tx["findings"])}</b><span>texture findings to decide</span></div><div class="stat"><b>0</b><span>coordinates changed</span></div>
</div>

<h2>1. Reference provenance and usage rights</h2>
<p class="sub">All five are <b>ChatGPT-provided visual research references</b>: you screenshotted and forwarded them; you did not source or photograph them. None is claimed to show Kano. Original source: unknown. Rights: unknown. Allowed use: private study aid only (palette, density, rhythm, layout), never public documentation, never redistribution, never a texture. The images are stored in no repository; the register records a SHA-256 for each.</p>
<div class="cards">{ref_cards}</div>
<h3 style="margin-top:28px">Missing references for authentic Kano architecture and streets</h3>
<div class="panel"><table><thead><tr><th>Id</th><th>Area</th><th>What is needed</th><th>Status</th></tr></thead><tbody>{miss}</tbody></table></div>
<p class="sub" style="margin-top:10px">Acceptable sources: your own photographs, licensed or commissioned photographs, openly licensed images after reading each file's licence, archival collections after reading their terms. Generated concept images are welcome as supplements if labelled <b>CONCEPT: not a photograph of an actual Kano location</b>. Nothing is copied or traced.</p>

<h2>2. Private asset repository</h2>
<div class="cols"><div class="panel"><h3>Structure</h3><ul class="l"><li><b>blender/projects</b> editable sources, versioned <code>vNNN</code>, append-only</li><li><b>unity/</b> exactly what Unity needs: Models (FBX, GLB, manifest), Textures, Materials <code>M_&lt;surface&gt;</code>, prefabs</li><li><b>references/</b> the register (metadata) and a restricted folder that is never exported</li><li><b>previews/</b> review renders &nbsp; <b>manifests/</b> release records &nbsp; <b>LICENSES.md</b> a row per asset</li><li>Git LFS for every binary; a validator with 9 tests enforces the rules</li></ul></div>
<div class="panel"><h3>Status</h3><ul class="l"><li><b>Created:</b> <b>AffanNamu/ARO-CONTENT</b>, private (checked), by you.</li><li><b>Populated:</b> the text-only skeleton (layout, LFS rules, manifest schema, licence register, validator with 9 tests) and the metadata-only reference register. No image, binary or secret.</li><li>Its own validation workflow passed on the first push.</li><li>Open: browser-test artifact boundary (fold or encrypt), LFS quota, written ownership, read token and variable (yours to set).</li></ul></div></div>

<h2>3. Coordinates</h2>
<div class="cols"><div class="panel"><table><thead><tr><th></th><th>Point</th></tr></thead><tbody>
<tr><td>Game world-map pin</td><td>12.0022N, 8.592E (source not recorded; also in the live backend seed)</td></tr>
<tr><td>Canonical anchor (recommended)</td><td>{ct["lat"]}N, {ct["lon"]}E: GeoNames Kano, Wikipedia agrees to 13 m</td></tr>
<tr><td>Distance, pin to anchor</td><td>8.2 km (pin is east)</td></tr><tr><td>Gidan Rumfa (3 listings)</td><td>11.9923N, 8.5160E, 0.9 km from the anchor</td></tr><tr><td>Dala Hill (1 source)</td><td>12.0093N, 8.5070E, 1.5 km from the anchor</td></tr></tbody></table></div>
<div class="panel"><ul class="l"><li>The pin is not a Kano-only slip: all eight city pins differ from GeoNames by 1.7 to 10.8 km.</li><li>Recommended: a <b>canonical anchor</b> plus a <b>separate transform</b> <code>K0</code> into Unity metres (error 17 m at 25 km, measured with PROJ).</li><li>Impact: existing Lagos-Ibadan corridor none; Kaduna-Kano map distance 209.6 to 204.5 km if the pin ever moved; the backend seed is live data.</li><li><b>Nothing was changed.</b> OSM, Wikidata and the GeoNames API are not reachable from this session; allow them to repeat the check directly.</li></ul></div></div>

<h2>4. Road-system limits and future backlog</h2>
<div class="cols"><div class="panel"><h3>Decision: the shared road engine is preserved</h3><ul class="l two">{rb}</ul></div>
<div class="panel"><h3>Kano workarounds (declared, data-only, test-enforced)</h3><table><thead><tr><th>Id</th><th>What</th><th>Removed by</th><th>How</th></tr></thead><tbody>{wk_rows}</tbody></table></div></div>

<h2>5. Unity textures (real settings, cold import in CI)</h2>
<div class="cols"><div class="panel"><h3>Settings</h3><table><tbody>{tex_settings}</tbody></table></div><div class="panel"><h3>Findings</h3><ul class="l">{tex_rows}</ul></div></div>

<h2>6. Layout and greybox</h2>
<p class="sub">The layout is unchanged by the decisions; the draft greybox is kept as a reviewable draft. Plain volumes from the game's own road model, terrain and placement; colours code the category; judge rhythm, scale and layout only.</p>
<div class="layout"><img src="{layout}" alt="Annotated layout plan of the Kano slice"></div>
<div class="gallery">{gal}</div>

<h2>7. Inventory and budgets</h2>
<p class="sub">{len(inv["props"])} planned prefabs and 12 kit modules, every one blocked by named reference gaps; plots also get a no-apron twin so the apron can be dropped when kerbs and drains exist. Budgets are computed from the real scatter and the game's own LOD policy (upper bounds, not frame times); the design target is 60% of a tier's draw calls and 45% of its triangles.</p>
<div class="panel"><table><thead><tr><th>Tier</th><th>Instances in slice</th><th>Draw calls at the worst spot</th><th>Planned triangles</th></tr></thead><tbody>{tier_rows}</tbody></table></div>
<p class="sub" style="margin-top:14px">Everything above is written up in <code>docs/KANO_VERTICAL_SLICE.md</code> with the data files. The Kano package is committed locally only: nothing is pushed or merged until you resolve the CI and asset-storage decisions.</p>
</main></body></html>'''
(out / "index.html").write_text(PAGE, encoding="utf-8")
print("wrote", out / "index.html", sum(p.stat().st_size for p in out.rglob("*") if p.is_file()), "bytes")
