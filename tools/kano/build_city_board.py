#!/usr/bin/env python3
"""Builds the Kano City master-plan review page (HTML + downscaled JPEGs) into an UNTRACKED folder, for publishing as a PRIVATE artifact.

Reads the generated plan data in docs/kano/city/ and the register. The ten ChatGPT-generated concepts (and the five research screenshots) are binary files: they are only read from where
they are, downscaled to thumbnails and written to --out; they never enter the repository. The page must not be shared (the references are not cleared for any redistribution).
Usage: python3 tools/kano/build_city_board.py --refs <json {R-id: image path}> --diagrams <dir with the planning diagrams> --out <empty dir>"""
import argparse
import html
import json
from pathlib import Path
from PIL import Image

ap = argparse.ArgumentParser(); ap.add_argument("--refs", required=True); ap.add_argument("--diagrams", required=True); ap.add_argument("--out", required=True)
a = ap.parse_args(); out = Path(a.out); (out / "img").mkdir(parents=True, exist_ok=True)
ROOT = Path(__file__).resolve().parents[2]; C = ROOT / "docs/kano/city"
plan = json.loads((C / "city_plan.json").read_text()); matrix = json.loads((C / "reference_asset_matrix.json").read_text()); registry = json.loads((C / "asset_registry.json").read_text())
stats = json.loads((C / "city_stats.json").read_text()); regs = json.loads((ROOT / "docs/kano/reference_register.json").read_text())
refpaths = json.loads(Path(a.refs).read_text()); REF = {r["id"]: r for r in regs["references"]}
e = html.escape


def jpeg(src, name, width, q=74):
    im = Image.open(src).convert("RGB")
    if im.width > width: im = im.resize((width, round(im.height * width / im.width)), Image.LANCZOS)
    p = out / "img" / (name + ".jpg"); im.save(p, "JPEG", quality=q, optimize=True); return "img/" + p.name


def png(src, name, width=1400):
    im = Image.open(src).convert("RGB")
    if im.width > width: im = im.resize((width, round(im.height * width / im.width)), Image.LANCZOS)
    p = out / "img" / (name + ".png"); im.save(p, "PNG", optimize=True); return "img/" + p.name


def table(head, rows, cls=""):
    h = "".join(f"<th>{e(str(x))}</th>" for x in head)
    b = "".join("<tr>" + "".join(f"<td>{c}</td>" for c in r) + "</tr>" for r in rows)
    return f'<div class="panel"><table class="{cls}"><thead><tr>{h}</tr></thead><tbody>{b}</tbody></table></div>'


D = {d["id"]: d for d in plan["districts"]}; N = stats["network"]; cur = stats["currentEngineDrawCallsInView"]; L = stats["streamingLadder"]
img = {n: png(Path(a.diagrams) / f"{n}.png", n) for n in ("master_plan", "engine_gap", "streaming", "layout_D06", "layout_D08", "layout_D09", "layout_D10", "layout_D11")}
thumb = {rid: jpeg(p, rid.lower(), 640) for rid, p in refpaths.items()}

stat = lambda n, t: f'<div class="stat"><b>{n}</b><span>{t}</span></div>'
strip = "".join([stat("100 km²", "study area KSA-1 (10 x 10 km)"), stat(len(plan["districts"]), "districts (5 ready to plan, 6 blocked or need concepts)"), stat(f"{N['nodes']} / {N['edges']}", "road network nodes / edges"),
                 stat(N["junctionsTheEngineCannotBuild"], "junctions the engine cannot build yet"), stat(matrix["counts"]["assets"], f"assets in the matrix; {matrix['counts']['blocked']} blocked on references"), stat("0", "assets modelled. No Blender source exists.")])

districts = table(["Id", "District", "Kind", "Geography", "Area ha", "Plots", "Phase", "Needs", "State"], [
    [f"<b>{e(d['id'])}</b>", e(d["name"]), e(d["kind"].replace("_", " ")), e(d["geography"]), f"{s['areaHa']:g}", f"{s['plots']:,}", f"P{d['phase']}", e(", ".join(d["requires"])),
     ("<span class='ok'>ready to plan</span>" if d["id"] in ("D11", "D10", "D09", "D06") else "<span class='ok'>hospital and police ready</span>; schools, government, hotels need concepts" if d["id"] == "D08"
      else "<span class='note'>needs new concepts</span>" if d["id"] == "D05" else "<span class='note'>BLOCKED on references</span>")]
    for s in stats["districts"] for d in [D[s["id"]]]])

classes = table(["Class", "Lanes x width", "Median", "Sidewalks", "km/h", "Engine today"], [[f"<b>{e(k)}</b>", f"{c['lanesPerDirection']} x {c['laneWidthM']} m", e(c["median"]), "yes" if c["sidewalks"] else "no", c["speedKmh"], e(c["supportedToday"])] for k, c in plan["roadClasses"].items()])
nodes = table(["Junction type", "Count", "Engine today", "Backlog"], [[e(k), N["nodeTypes"].get(k, 0), e(v["today"]), e(", ".join(v["rb"]) or "-")] for k, v in plan["nodeTypes"].items()])

# district layouts next to the concept that set them
LAY = {"D06": ["R-006"], "D08": ["R-010", "R-011"], "D09": ["R-009"], "D10": ["R-013", "R-014", "R-015"], "D11": ["R-007", "R-008", "R-012"]}
lay = ""
for did, refs in LAY.items():
    l = plan["layouts"][did]; th = "".join(f'<figure><img src="{thumb[r]}" alt="{e(REF[r]["label"])}"><figcaption><b>{r}</b> {e(REF[r]["label"])} (private concept)</figcaption></figure>' for r in refs)
    lay += f'<h3>{did}: {e(l["title"])}</h3><p class="sub">{e(l["note"])} Envelope {l["size"][0]} x {l["size"][1]} m. Plan-level envelopes read from the concept, not surveyed.</p><div class="gallery"><figure><img src="{img["layout_" + did]}" alt="{did} plan layout"><figcaption>Plan layout (my diagram, indicative)</figcaption></figure>{th}</div>'

cards = ""
for r in matrix["references"]:
    rid = r["ref"]; assets = [x for x in registry["assets"] if rid in x["concepts"]]
    by = {c: sum(1 for x in assets if x["assetClass"] == c) for c in ("GLOBAL", "REGIONAL", "CITY_SPECIFIC", "LANDMARK")}
    chips = "".join(f'<span class="chip">{e(g["rb"])} {e(g["title"])}</span>' for g in r["engineGaps"]); flags = "".join(f"<li>{e(f)}</li>" for f in r["flags"])
    cards += (f'<div class="card"><img src="{thumb[rid]}" alt="{e(r["scene"])}"><div class="body"><p class="no">{rid} · ChatGPT-generated concept · districts {e(", ".join(r["districts"]))}</p><h3>{e(r["scene"])}</h3>'
              f'<p><b>{len(assets)} assets</b>: GLOBAL {by["GLOBAL"]}, REGIONAL {by["REGIONAL"]}, CITY {by["CITY_SPECIFIC"]}, LANDMARK {by["LANDMARK"]}.</p>'
              f'<dl><dt>Must reproduce</dt><dd>{e("; ".join(r["visualRequirements"]))}</dd><dt>Reference camera</dt><dd>{e(r["referenceCamera"])}</dd><dt>Compare</dt><dd>{e("; ".join(r["comparisonChecklist"]))}</dd></dl>'
              f'<div class="chips">{chips}</div>' + (f'<ul class="l note">{flags}</ul>' if flags else "") + "</div></div>")

lm = table(["Landmark", "District", "K0 position (x, z) m", "Status", "Basis"], [[e(m["name"]), m["district"], f"({m['xz'][0]:.0f}, {m['xz'][1]:.0f})",
           ("<span class='ok'>VERIFIED</span>" if m["verified"] else "<span class='note'>UNVERIFIED placeholder</span>"), e(m["position"])] for m in plan["landmarks"]])

S = plan["streaming"]
rows = []
for t, who in (("low", "Mobile"), ("medium", "Desktop")):
    for r in L[t]["rows"]:
        if r["id"] in ("D11", "D08", "D06", "D05", "D01"):
            rows.append([t, who, r["id"], r["density"], f"{r['triangles']:,.0f}", f"{r['drawCalls']:.0f}", f"{cur[r['id']]:,}", f"{L[t]['budget']['drawCalls']}"])
stream = table(["Tier", "Budget", "District", "Plots/ha", "Triangles in view", "Draw calls (ladder)", "Draw calls (current engine)", "Draw budget"], rows)

phases = table(["Phase", "Name", "Districts", "Entry", "Exit", "Engine items"], [[f"<b>{p['id']}</b>", e(p["name"]), e(", ".join(p["districts"]) or "-"), e(p["entry"]), e(p["exit"]), e(", ".join(p["engine"]) or "none")] for p in plan["phases"]])

REQ = [("Traditional Hausa compounds, zaure, parapets, ornament", "street elevation, compound plan, zaure interior, wall gate", "D01, D07 (M-01)"), ("Old-city lanes and traditional residential streets", "eye-level lane, aerial of a block", "lane width and surface unknown (M-12)"),
       ("Kurmi Market and traditional markets", "aerial, stall rows, eye-level lane", "D03 (M-04)"), ("Neighbourhood mosque", "exterior, courtyard, minaret", "D06, D07 (M-03)"), ("Great Mosque and Gidan Rumfa", "gate, court, minaret, aerial", "D02, only from real references (M-11)"),
       ("Dala Hill and its settlement", "aerial, base of the hill, steps", "D04 (M-11)"), ("City wall and a gate", "gate exterior and interior, wall section", "gate junctions (M-11)"), ("Modern commercial street and plaza", "eye-level, aerial, shop-front close-up", "D05 (M-02, M-04)"),
       ("Mid-rise commercial and office buildings", "street elevation, aerial", "D05, D08 (M-02)"), ("Schools, government buildings, hotels", "exterior of each, entrance", "D08 (M-10)"), ("Roundabout and four-way intersection", "aerial, eye-level through the junction", "with RB-01, RB-02, RB-16 (M-13)"),
       ("Interchange or flyover", "aerial, ramp detail", "with RB-03 (M-13)"), ("Real road surfaces, patches, cracks, drains", "close-ups of Kano roads", "RB-05, RB-06 (M-05)"), ("Electricity poles, transformers, cables", "street view, pole close-up", "(M-06)"),
       ("Street trees and vegetation of the region", "species close-ups", "replaces a generic placeholder (M-07)"), ("Local vehicles: tricycles, motorcycles, minibuses", "side and three-quarter", "(M-08)")]
req = table(["#", "Needed", "Suggested views", "Unblocks"], [[i + 1, e(a), e(b), e(c)] for i, (a, b, c) in enumerate(REQ)])

FLAGS = ["<b>Sign text (R-008, R-012).</b> The gantry reads Zaria 138 km, Kaduna 262 km and \"A2 Express Route\". Sources give Kano to Zaria about 155 to 165 km and to Kaduna about 230 km; \"Express Route\" is not an established designation. Sign text must come from verified route data.",
         "<b>The recurring twin-minaret teal-dome mosque</b> (R-006, R-008, R-009, R-010, R-012). Is it the Central Mosque (a landmark built from real references) or a generic skyline mosque?",
         "<b>Real organisations.</b> R-010's hospital name resembles Aminu Kano Teaching Hospital; R-011's crest imitates the Nigeria Police Force emblem and the vehicles wear police livery. Fictional names and a generic crest by default.",
         "<b>Terrain.</b> R-007's green rocky hills do not match Kano's flat plain (they suit the Kaduna side). Keep only if you accept it; needs RB-14.",
         "<b>Vehicles.</b> Close-range coaches, trucks, an ambulance and police pickups do not fit the 2,500-triangle prop budget.", "<b>Names.</b> \"Kano Central Bus Terminal\" is not a verified terminal; the fuel brand is generic."]
DEC = ["Approve this direction (scope, districts, phases).", "Approve each concept's asset breakdown before work on it starts.", "Rule on the six flags above.", "Coordinate anchor and transform; the world-map pin is a production-data change and is not touched without your authorisation.",
       "Allow OpenStreetMap, Wikidata and GeoNames (or supply an extract) so indicative geography can be replaced by real geography.", "Approve, defer or decline RB-15, RB-16 and RB-17 (P0 exit) and the other engine items.",
       "Asset storage: artifact boundary, Git LFS quota and ownership for ARO-CONTENT. The Kano package stays local until the CI and storage decisions are made.", "Supply the concepts and references in the list above."]

page = f"""<!DOCTYPE html>
<html lang="en"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width, initial-scale=1"><title>Kano City Master Plan</title>
<link rel="preconnect" href="https://fonts.googleapis.com"><link href="https://fonts.googleapis.com/css2?family=Fraunces:wght@600&family=Public+Sans:wght@400;500;600&display=swap" rel="stylesheet">
<style>
/* layout: a review sheet in the order of the owner's list. Status strip, then one section per requirement. Indigo (the Kofar Mata dye) on dust. */
:root {{ --bg:#f3f0ea; --panel:#fbf9f5; --ink:#1d2230; --mute:#5c6070; --line:#d9d4c8; --accent:#2c3f7a; --warn:#8a4b00; --warn-bg:#f8e9d2; --ok:#1d6b45; --display:"Fraunces", Georgia, serif; --body:"Public Sans", system-ui, sans-serif; }}
@media (prefers-color-scheme: dark) {{ :root:not([data-theme="light"]) {{ --bg:#14171f; --panel:#1b1f2a; --ink:#e8e6e1; --mute:#a2a5b2; --line:#303544; --accent:#8fa6f0; --warn:#f0c27a; --warn-bg:#3a2d17; --ok:#7fd0a3; color-scheme:dark; }} }}
:root[data-theme="dark"] {{ --bg:#14171f; --panel:#1b1f2a; --ink:#e8e6e1; --mute:#a2a5b2; --line:#303544; --accent:#8fa6f0; --warn:#f0c27a; --warn-bg:#3a2d17; --ok:#7fd0a3; color-scheme:dark; }}
* {{ box-sizing:border-box; }} html,body {{ margin:0; }} body {{ background:var(--bg); color:var(--ink); font:16px/1.55 var(--body); }}
main {{ max-width:1180px; margin:0 auto; padding:24px 16px 64px; }}
h1,h2,h3 {{ font-family:var(--display); font-weight:600; text-wrap:balance; line-height:1.15; margin:0; }} h1 {{ font-size:clamp(28px,4vw,40px); }} h2 {{ font-size:26px; margin:44px 0 6px; }} h3 {{ font-size:19px; margin:26px 0 4px; }}
p {{ margin:0 0 10px; max-width:74ch; }} a {{ color:var(--accent); }} .sub {{ color:var(--mute); max-width:80ch; }}
.private {{ background:var(--warn-bg); color:var(--warn); border-radius:6px; padding:10px 14px; font-weight:600; margin:14px 0; max-width:none; }}
.strip {{ display:grid; grid-template-columns:repeat(auto-fit,minmax(170px,1fr)); gap:10px; margin:16px 0 6px; }}
.stat {{ background:var(--panel); border:1px solid var(--line); border-left:4px solid var(--accent); padding:10px 14px; border-radius:6px; }} .stat b {{ display:block; font:600 26px var(--display); font-variant-numeric:tabular-nums; }} .stat span {{ color:var(--mute); font-size:13.5px; }}
.cards {{ display:grid; grid-template-columns:repeat(auto-fit,minmax(min(100%,520px),1fr)); gap:16px; margin-top:16px; }}
.card {{ background:var(--panel); border:1px solid var(--line); border-radius:8px; overflow:hidden; }} .card img {{ width:100%; height:auto; display:block; }} .card .body {{ padding:12px 16px 14px; }}
.no {{ color:var(--mute); font-size:12.5px; text-transform:uppercase; letter-spacing:.06em; margin:0 0 4px; }} .chips {{ display:flex; flex-wrap:wrap; gap:6px; margin:8px 0; }} .chip {{ background:var(--warn-bg); color:var(--warn); border-radius:12px; padding:2px 10px; font-size:13px; }}
.note {{ color:var(--warn); font-weight:500; }} .ok {{ color:var(--ok); font-weight:600; }} dl {{ margin:10px 0 0; display:grid; grid-template-columns:118px 1fr; gap:4px 12px; font-size:14.5px; }} dt {{ color:var(--mute); }} dd {{ margin:0; }}
.panel {{ background:var(--panel); border:1px solid var(--line); border-radius:8px; padding:8px 16px 12px; margin-top:12px; overflow-x:auto; }}
table {{ border-collapse:collapse; width:100%; font-size:14.5px; }} th,td {{ text-align:left; vertical-align:top; padding:7px 10px; border-bottom:1px solid var(--line); }} th {{ color:var(--mute); font-weight:600; white-space:nowrap; }}
.gallery {{ display:grid; grid-template-columns:repeat(auto-fit,minmax(min(100%,420px),1fr)); gap:14px; margin-top:12px; }} figure {{ margin:0; background:var(--panel); border:1px solid var(--line); border-radius:8px; overflow:hidden; }} figure img {{ width:100%; height:auto; display:block; }} figcaption {{ padding:8px 12px; color:var(--mute); font-size:13.5px; }}
.wide img {{ width:100%; height:auto; border:1px solid var(--line); border-radius:8px; }} ul.l {{ margin:6px 0; padding-left:20px; }} ul.l li {{ margin:5px 0; max-width:80ch; }}
nav {{ display:flex; flex-wrap:wrap; gap:6px 14px; margin:12px 0 0; font-size:14px; }}
</style></head><body><main>
<p class="no">African Roads Online · Kano · 2026-11-01</p>
<h1>Kano City master plan</h1>
<p class="sub">The 1.65 km corridor was a road-system test (now TC-1). This is the plan for the city: a 100 km² study area, eleven districts, a road network with a hierarchy, a reference-to-asset matrix for your ten concepts, a shared asset registry and a streaming strategy. <b>Plan only. Nothing is modelled and nothing here is a finished city.</b></p>
<p class="private">PRIVATE REVIEW PAGE. It contains thumbnails of the reference images, which are not cleared for any redistribution. Do not share this page or its link.</p>
<div class="strip">{strip}</div>
<nav><a href="#plan">Master plan</a><a href="#districts">Districts</a><a href="#roads">Roads</a><a href="#layouts">Layouts</a><a href="#matrix">Reference matrix</a><a href="#landmarks">Landmarks</a><a href="#streaming">Streaming</a><a href="#phases">Phases</a><a href="#decisions">Decisions</a><a href="#requests">Concept requests</a></nav>

<h2 id="plan">1. Spatial master layout</h2>
<p class="sub">Indicative. The old city (an ellipse whose perimeter matches the 14 km UNESCO figure for the wall), the ring road and every district polygon are plan logic, not survey. Two landmark coordinates are verified (Gidan Rumfa within about 300 m; Dala Hill from one source). The A2 highway enters from the south-west. A local OpenStreetMap extract would replace this layout with real geography.</p>
<div class="wide"><img src="{img['master_plan']}" alt="Master plan of the 10 x 10 km study area"></div>

<h2 id="districts">2. Districts and neighbourhoods</h2>
<p class="sub">Plot counts come from district areas and a planning density, to be replaced by measurement. Traditional districts, the palace precinct, the market and Dala Hill are <b>blocked</b>: no authentic reference exists, and nothing is invented.</p>
{districts}

<h2 id="roads">3. Road network and hierarchy</h2>
<p class="sub">One connected graph of {N['nodes']} nodes and {N['edges']} edges, {sum(N['kmByClass'].values()):.1f} km. The current engine builds one main road with straight T-junction stubs, so {N['junctionsTheEngineCannotBuild']} of the {N['nodes']} junctions cannot be built yet. The foundation is RB-16 (road network graph) with RB-17 (road class catalogue); the engine is unchanged.</p>
{classes}{nodes}
<div class="wide" style="margin-top:14px"><img src="{img['engine_gap']}" alt="Where the current engine cannot follow the plan"></div>

<h2 id="layouts">4. District layouts, beside the concepts that set them</h2>
<p class="sub">Plan-level envelopes read from each concept's composition. They fix the kind of arrangement (block size, plot pattern, setbacks, access) so assets can be produced against it. They are not 3D concept art; the visual concepts are yours to supply. The traditional districts have no layout because drawing one from imagination is what you ruled out.</p>
{lay}

<h2 id="matrix">5. Reference-to-asset matrix</h2>
<p class="sub">Each of your ten concepts is a design specification for genuine 3D geometry, never an image on a plane. {matrix['counts']['assets']} assets: GLOBAL {matrix['counts']['byClass']['GLOBAL']}, REGIONAL {matrix['counts']['byClass']['REGIONAL']}, CITY_SPECIFIC {matrix['counts']['byClass']['CITY_SPECIFIC']}, LANDMARK {matrix['counts']['byClass']['LANDMARK']}. {matrix['counts']['conceptBacked']} are backed by a concept; <b>{matrix['counts']['blocked']} are blocked</b>. Every asset is at <b>concept approved</b> or <b>needs concept</b>; none has a Blender source. Red-orange text is a question for you.</p>
<div class="cards">{cards}</div>

<h2 id="landmarks">6. Landmarks</h2>
{lm}
<p class="sub">A landmark is built only from rights-cleared real references or measured drawings, after its position is fixed from sources.</p>

<h2 id="streaming">7. Performance and streaming</h2>
<p class="sub">A distance-band ladder: individual prefabs to 150 m, baked block meshes to 800 m, impostors and a skyline to 1.5 km, hidden by haze. Checked against the game's own budgets (Low tier against the Mobile budget). It is a feasibility model with no occlusion credit and assumed densities, not a measurement. At city density the current engine needs {max(cur.values()):,} draw calls against a budget of 400, so city-density streaming (RB-15) comes before the dense districts. For the sparse hero districts the ladder costs more than the current engine, so those can be built first on what exists.</p>
<div class="wide"><img src="{img['streaming']}" alt="Streaming and budget model"></div>
{stream}

<h2 id="phases">8. Phased plan</h2>
{phases}

<h2 id="decisions">9. Flags and decisions</h2>
<h3>Flags raised by the concepts</h3><ul class="l">{''.join(f'<li>{f}</li>' for f in FLAGS)}</ul>
<h3>Decisions needed from you</h3><ol class="l">{''.join(f'<li>{d}</li>' for d in DEC)}</ol>

<h2 id="requests">10. Concept requests for ChatGPT</h2>
<p class="sub">Each should be a labelled concept. The traditional and landmark ones should be tied to real, rights-cleared references or heritage surveys, not idealised imagination.</p>
{req}
<p class="sub" style="margin-top:28px">Everything is generated from <code>docs/kano/city/</code> in the repository (branch <code>work/kano-slice</code>, local, not pushed). Full text: <code>docs/KANO_CITY_MASTER_PLAN.md</code>. The asset registry is also in the private repository ARO-CONTENT.</p>
</main></body></html>
"""
(out / "index.html").write_text(page, encoding="utf-8"); print("wrote", out / "index.html", f"({len(page):,} bytes)")
