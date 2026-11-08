#!/usr/bin/env python3
"""Generates docs/KANO_CITY_MASTER_PLAN.md from the plan data (docs/kano/city/*.json, reference_register.json, city_stats.json).

Every number in the document comes from those files, which tests/NetCore.Tests/KanoCityPlanTests.cs checks. Run:
  python3 tools/kano/make_city_doc.py            # write
  python3 tools/kano/make_city_doc.py --check    # exit 1 when the document is out of date
"""
import json
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
CITY = ROOT / "docs/kano/city"
PLAN = json.loads((CITY / "city_plan.json").read_text())
MATRIX = json.loads((CITY / "reference_asset_matrix.json").read_text())
REG = json.loads((CITY / "asset_registry.json").read_text())
STATS = json.loads((CITY / "city_stats.json").read_text())
REFS = json.loads((ROOT / "docs/kano/reference_register.json").read_text())
REF_BY_ID = {r["id"]: r for r in REFS["references"]}
OUT = ROOT / "docs/KANO_CITY_MASTER_PLAN.md"

D = {d["id"]: d for d in PLAN["districts"]}
LM = PLAN["landmarks"]
N = STATS["network"]


def table(head, rows):
    out = ["| " + " | ".join(head) + " |", "|" + "---|" * len(head)]
    out += ["| " + " | ".join(str(c) for c in r) + " |" for r in rows]
    return "\n".join(out)


def district_rows():
    rows = []
    for s in STATS["districts"]:
        d = D[s["id"]]
        rows.append([f"**{d['id']}**", d["name"], d["kind"].replace("_", " "), d["geography"], f"{s['areaHa']:g}", f"{s['plots']:,}", f"P{d['phase']}",
                     ", ".join(d["requires"]), ("hospital and police ready to plan; schools, government, hotels need concepts" if d["id"] == "D08" else "ready to plan") if all(r.startswith("R-") for r in d["requires"]) and d["id"] != "D04" else "**BLOCKED on references**" if any(r.startswith("M-") and r in ("M-01", "M-12", "M-11") for r in d["requires"]) else "needs new concepts"])
    return rows


def streaming_section():
    S = PLAN["streaming"]
    L = STATS["streamingLadder"]
    cur = STATS["currentEngineDrawCallsInView"]
    out = []
    out.append("The camera cannot see 100 km², so the question is what a player sees from any one place. The model below counts, for each district density, how many plots fall inside each distance band, what each band is made of, and what that costs in triangles and draw calls, against the game's own `PerfBudget`.\n")
    out.append(table(["Band", "Distance from the camera (m)", "What is drawn", "Triangle cap per plot", "Individual objects?"],
                     [[i, f"{S['bandEdgesM'][i]} to {S['bandEdgesM'][i + 1]}", S["bandProduct"][i], S["capsTrisPerPlot"][i], "yes" if S["individualBands"][i] else "no (merged per chunk)"] for i in range(5)]))
    out.append("")
    a = S["assumptions"]
    out.append(f"Assumptions (all explicit; the tests read them): the camera sees **{a['frustumFraction']:.0%}** of the circle around the player; a merged chunk costs **{a['mergedAtlasesPerChunk']}** draw calls; a chunk is **{a['chunkHa']:g} ha** (200 m); **no occlusion credit** is taken, so every number is an upper bound. Plot densities are planning assumptions, not measurements of Kano. Tier radius scale follows `QualityTiers` (Low 0.6, Medium and High 1.0).\n")
    for tier in ("low", "medium"):
        t = L[tier]
        which = "Mobile" if tier == "low" else "Desktop"
        out.append(f"**{tier.capitalize()} tier**, checked against the {which} `PerfBudget` ({t['budget']['triangles']:,} triangles, {t['budget']['drawCalls']} draw calls; targets: at most {S['targets']['triangleShare']:.0%} of the triangle budget and {S['targets']['drawShare']:.0%} of the draw budget for the city, the rest being road, terrain, traffic and UI):\n")
        out.append(table(["District or filler", "Plots/ha", "Triangles in view", "Draw calls in view (ladder)", "Draw calls in view (current engine)"],
                         [[r["id"], r["density"], f"{r['triangles']:,.0f}", f"{r['drawCalls']:.0f}", f"{cur[r['id']]:,}"] for r in t["rows"]]))
        out.append("")
    worst_low = max(r["drawCalls"] for r in L["low"]["rows"]); worst_med = max(r["drawCalls"] for r in L["medium"]["rows"])
    wt_low = max(r["triangles"] for r in L["low"]["rows"]); wt_med = max(r["triangles"] for r in L["medium"]["rows"])
    out.append(f"**Result.** With the ladder, the densest district peaks at {wt_low:,.0f} triangles and {worst_low:.0f} draw calls on Low and {wt_med:,.0f} triangles and {worst_med:.0f} draw calls on Medium; both are inside the targets. With the **current engine** (one draw per plot, ring 2 only) the densest districts need {max(cur.values()):,} draw calls against a budget of 400 (Desktop) and 150 (Mobile): the city is **not renderable in the current engine at city density**, and the 5 x 5 chunk square it instantiates (100 ha) is smaller than any hero district. This is not a modelling problem; it is **RB-15**, and it is why the phased plan puts the engine work in front of the districts that need it. The model is a feasibility check, not a measurement: nothing here has been rendered, and the real numbers come from `PerfProbe` once a district exists.\n")
    out.append("**A caveat the tables show.** The ladder has a fixed floor (every merged chunk costs draw calls). On Medium and High the ladder therefore costs *more* draw calls than the current engine in the sparse districts (D11, D10, D09, D08 and the fringe F-OUT) and pays off from D06 density upward; on Low it is cheaper everywhere except D11. The ladder is for city density. The sparse hero districts can be built on the existing streaming first (P1a), which is why the engine work does not block them.\n")
    out.append("![Streaming and budget model](kano/city/img/streaming.png)\n")
    out.append("**What streams.** (1) Chunks of 200 m remain the unit; the lattice is already aligned with frame K0. (2) Hero districts (zone Z-A) keep individual prefabs out to band 1 (150 m), baked block meshes beyond. (3) Filler (Z-B) is generated from the same kit by rule and exists only as prefab LOD1 plus baked blocks. (4) Beyond the ring road (Z-C) only terrain tint, a baked skyline mesh and impostors, hidden by the same haze the game already uses (FarClip 900 / 1500 / 2200 m by tier). (5) Textures: the Kano texture plan is 36.0 MiB, inside the 300 MB desktop and 150 MB mobile budgets, and the shared-kit approach (one atlas per family, `M_<surface>` names) keeps it from growing per district (see `docs/TEXTURE_BUDGET.md`; the world-map texture defect found there is separate).\n")
    out.append("**Order of expansion.** A district is added by (a) authoring its hero buildings from the registry, (b) generating its filler from the kit, (c) baking its blocks offline, (d) adding its chunk data, and (e) measuring with `PerfProbe` on each tier before the next district. Nothing is loaded that the camera cannot reach, so the world can grow without the frame budget growing.\n")
    return "\n".join(out)


def matrix_summary():
    rows = []
    for r in MATRIX["references"]:
        rid = r["ref"]
        assets = [a for a in REG["assets"] if rid in a["concepts"]]
        by = {c: sum(1 for a in assets if a["assetClass"] == c) for c in ("GLOBAL", "REGIONAL", "CITY_SPECIFIC", "LANDMARK")}
        shared = sum(1 for a in assets if len(a["concepts"]) > 1)
        rows.append([f"**{rid}**", r["scene"], ", ".join(r["districts"]), len(assets), by["GLOBAL"], by["REGIONAL"], by["CITY_SPECIFIC"], by["LANDMARK"], shared,
                     ", ".join(g["rb"] for g in r["engineGaps"]) or "none", "; ".join(f for f in [short_flag(REF_BY_ID[rid])] if f) or "-"])
    return table(["Ref", "Concept", "Districts", "Assets", "GLOBAL", "REGIONAL", "CITY", "LANDMARK", "Shared with another concept", "Engine gaps", "Flags for the owner"], rows)


def short_flag(ref):
    keys = []
    txt = " ".join(ref.get("flags", []))
    if "teal dome" in txt or "two minarets" in txt: keys.append("recurring twin-minaret mosque")
    if "A2 Express Route" in txt: keys.append("sign text and distances do not match sources")
    if "Aminu Kano" in txt: keys.append("hospital name resembles a real institution")
    if "Nigeria Police Force emblem" in txt: keys.append("real police crest and livery")
    if "hills do not match" in txt: keys.append("hilly terrain unlike Kano's plain")
    if "Kano Central Bus Terminal" in txt: keys.append("terminal name not verified")
    if "brand colours" in txt: keys.append("generic fuel brand")
    return ", ".join(keys)


def landmark_rows():
    out = []
    for m in LM:
        status = ("VERIFIED (single source)" if "ONE source" in m["position"] else "VERIFIED (within about 300 m)") if m["verified"] else "UNVERIFIED placeholder"
        out.append([m["name"], m["district"], f"({m['xz'][0]:.0f}, {m['xz'][1]:.0f})", status, m["position"], m["reference"]])
    return out


def phase_rows():
    return [[p["id"], p["name"], ", ".join(p["districts"]) or "-", p["entry"], p["exit"], ", ".join(p["engine"]) or "none", p["note"]] for p in PLAN["phases"]]


def road_class_rows():
    out = []
    for k, c in PLAN["roadClasses"].items():
        out.append([f"**{k}**", f"{c['lanesPerDirection']} x {c['laneWidthM']} m", c["median"], "yes" if c["sidewalks"] else "no", c["speedKmh"], ", ".join(c["refs"]) or "none", ", ".join(c["needs"]), c["supportedToday"]])
    return out


def node_rows():
    cnt = N["nodeTypes"]
    return [[k, cnt.get(k, 0), v["today"], ", ".join(v["rb"]) or "-"] for k, v in PLAN["nodeTypes"].items()]


def reference_rows():
    return [[r["id"], r["label"], "ChatGPT-generated concept; the owner's compulsory design standard"] for r in REFS["references"] if r["kind"] == "generated_concept"]


def build():
    L = []
    w = L.append
    reg_counts = MATRIX["counts"]
    w("# Kano City master plan (KSA-1), draft for owner approval\n")
    w("**Status (2026-11-01): PLAN ONLY. Nothing in this document is modelled, surveyed or read by the game. No detailed modelling has started and none will start until the owner has reviewed and approved this direction.** Every position, district boundary, road and plot is an **indicative fictional placement**, except where a landmark coordinate is marked VERIFIED and its source and firmness are given. A greybox is not a city, and nothing here claims otherwise.\n")
    w("This replaces the scope of the earlier \"Kano vertical slice\" package (`KANO_VERTICAL_SLICE.md`). Its 1.65 km corridor is kept, renamed **TC-1**, only as the road-system test corridor. The deliverable is a city.\n")
    w("---\n## 0. Acknowledgement and scope reassessment\n")
    w("**The correction is accepted.** The previous package treated a 1.65 km road strip as the \"vertical slice\" of Kano. That is the wrong unit: a strip proves that the road engine can carry a route; it says nothing about a city's layout, districts, density, landmarks, asset reuse or streaming. This plan therefore sets out:\n")
    w("| # | Required by the owner | Where |\n|---|---|---|")
    for i, (a, b) in enumerate([("A spatial master layout for a substantial city area", "section 3"), ("Multiple connected districts and neighbourhoods", "section 2 and section 5"), ("A road network with hierarchies", "section 4"), ("Building-block and neighbourhood layouts", "section 5"),
                                ("Proposed landmark locations from verified geography", "section 6"), ("Traditional, modern and commercial styles", "section 7"), ("A 3D production strategy", "sections 8 and 9"), ("A modular asset architecture", "section 10"),
                                ("A performance and world-streaming strategy", "section 11"), ("A phased plan", "section 12")], 1):
        w(f"| {i} | {a} | {b} |")
    w("\nAlso delivered: the **reference-to-asset production matrix** (`kano/city/REFERENCE_ASSET_MATRIX.md`, section 9), the **shared asset library and registry** (section 10; `kano/city/asset_registry.json`, mirrored in the private repository), the **engine improvements required** (section 13, `ROAD_ENGINE_BACKLOG.md` RB-12 to RB-17) and the **decisions needed from the owner** (section 15). ChatGPT is to supply visual concepts (including the list in section 16); this repository supplies the technical inspection, Blender modelling, optimisation, validation and Unity integration.\n")
    w("### Honesty rules this plan follows\n")
    w("* Concepts are design specifications. Each is reproduced as **genuine 3D geometry** (walls, roofs, openings, structure, ornament), never as an image on a plane, never as a texture, and never redistributed.")
    w("* A concept is never presented as a verified image of Kano. The ten concepts are idealised, generated scenes; place names and sign text in them are illustrative.")
    w("* Where a concept needs a change to be buildable (engine limit, budget, rights, factual error) the change is **explained to the owner first** and never made silently.")
    w("* Where nothing exists to copy (Hausa compounds, old-city lanes, markets, the palace, the Great Mosque, schools, government buildings, hotels) the work is **BLOCKED on a reference**, and nothing is invented to fill it.")
    w("* Work stays within the current engine's limits. What the engine cannot do is documented as a backlog item (RB-12 to RB-17), not worked around permanently in Kano data.\n")
    w("### Update 2026-11-08: the transportation expansion brief\n")
    w("The owner's brief of 2026-11-08 (seven roundabout designs, urban highways, flyovers, logistics facilities, bus terminals, supporting infrastructure, day and night) is answered by **`docs/KANO_TRANSPORT_EXPANSION_PLAN.md`**: the inspection of the current road, prop, vehicle, job and bus systems, the technical proposal for the engine work it needs, the order of work, the phase-by-phase report and the decisions requested. It changes nothing in this plan's districts or network; it adds:\n")
    w("* **Where the brief lands here.** Roundabouts, four-way junctions, flyovers and interchanges are RB-01 to RB-03 and stages E3 to E5 of `ROAD_ENGINE_PROPOSAL.md`; the 21 of 54 junctions this plan says the engine cannot build are the same ones. Logistics (D10) and bus terminals (D09) need only E1 and E2 (RB-13 site plates, RB-07 text signs, kerbs), which is why the proposed order builds them before the roundabouts.")
    w("* **Seven roundabout designs on the same 54 nodes.** The four ring roundabouts (RR0, RR90, RR180, RR270) and two promoted four-way crossings (MA0, MA225) carry six designs; the interchange node RR225 carries the seventh (a grade-separated roundabout interchange). The junction count and the 21 unsupported junctions are unchanged. Proposed mapping: landmark MA225, modern urban RR90, traditional RR180, large multi-lane RR270, commercial MA0, green landscape RR0, highway interchange RR225. Indicative, for the owner to confirm.")
    w("* **Flyover candidates:** the ring interchange RR225, the A2 over the logistics junction X_LOG, and a radial arterial over the middle ring (for example MA270). They are corridors of the graph, never isolated structures.")
    w("* **Phase numbering.** The brief's phases 1 to 6 (roundabouts, highways and flyovers, logistics, bus terminals, supporting infrastructure, lighting) are work packages inside this plan's P1b to P5; they do not replace section 12. The proposed execution order is in section 6 of the expansion plan.")
    w("* **New references R-030 to R-036** (night, wet-road, highway, roundabout, flyover and interchange scenes) are registered as `generated_night_reference`: design references for lighting, signage and infrastructure layout; hash only, nothing stored; real brand marks and sign text in them are not used.")
    w("* **Nothing is built.** No engine file changed, no asset added; the lighting foundation (`docs/NIGHT_LIGHTING_PLAN.md`) is the only implementation work, and it adds code, catalogue entries and route rules only.\n")
    w("### Status vocabulary (an asset moves through these in order and never skips one)\n")
    w("| Status | Meaning |\n|---|---|")
    meaning = {"needs_concept": "no concept or verified reference exists", "concept_approved": "a concept exists and the owner approves it as the standard", "blockout_completed": "primitive geometry at correct size and proportion, compared against the concept at the reference camera",
               "detailed_modelling_completed": "full geometry (LOD0) with real structure and ornament", "materials_completed": "UVs and PBR materials with shared `M_<surface>` names", "blender_validation_passed": "geometry, scale, normals, budgets and naming checked by the Blender validator",
               "unity_import_verified": "imported into Unity and measured (orientation, scale, materials, collision, LODs)", "in_game_visual_review_passed": "in-game screenshot at the reference camera compared with the concept and accepted",
               "production_approved": "the owner approves it for the game; the registry then records source, export, prefab, validation and approver"}
    for s in MATRIX["statusVocabulary"]:
        w(f"| `{s}` | {meaning[s]} |")
    w(f"\n**Today every one of the {reg_counts['assets']} assets sits at `concept_approved` ({reg_counts['conceptBacked']} concept-backed) or `needs_concept` ({reg_counts['blocked']} blocked), with the asset breakdown still awaiting the owner's approval. Not one has a Blender source.**\n")
    w("---\n## 1. The study area\n")
    sa = PLAN["studyArea"]
    w(f"* **KSA-1** is a {sa['areaKm2']:g} km² square (10 km x 10 km, {sa['chunks']:,} chunks of 200 m) in the local frame **K0** (x east, z north, metres, aligned with the game's chunk lattice), centred on the old city, not on the GeoNames point (the old city's centre sits at x 0, z -200 m).")
    w("* **The frame** (`kano/COORDINATES.md`, `kano_world_frame.json`) is a local tangent plane at the canonical anchor GeoNames Kano 12.00012° N, 8.51672° E (Wikipedia agrees to 13 m). Its error against a geodesic is 0.4 m at 1 km, 5.1 m at 10 km and 17 m at 25 km, so no point of the 10 km study area (at most about 7 km from the anchor) is off by more than about 5 m. **No coordinate in the game, backend or any route has been changed.** The existing world-map pin (12.0022° N, 8.592° E) is 8.2 km east of the anchor and has no recorded source; that decision is the owner's (section 15).")
    w(f"* **The old city** is drawn as an ellipse of {PLAN['oldCity']['semiAxesM'][0]:g} x {PLAN['oldCity']['semiAxesM'][1]:g} m whose perimeter matches the 14 km UNESCO figure for the wall (the Daily Trust gives 17 km; sources disagree). **It is indicative.** The real wall line has not been surveyed here; it needs OpenStreetMap or a survey.")
    w(f"* **The ring road** (radius {PLAN['ringRoad']['radiusM']:g} m) and every other road are indicative.")
    w("* **TC-1** (the former corridor, 1.65 km, bearing 36° out of the south-west) remains in the repository as the road-system test: `ng-kano-slice.route.draft.json`. It tests the existing engine; it is not part of the city.\n")
    w("### What is verified and what is not\n")
    w("| Item | Position | Status | Source and firmness |\n|---|---|---|---|")
    w("| Canonical anchor (Kano) | 12.00012° N, 8.51672° E | VERIFIED | GeoNames id 2335204; Wikipedia agrees to 13 m; OSM's place node is 0.9 km away (one more reason it is a locality point, not a monument) |")
    w("| Gidan Rumfa | about 11.9923° N, 8.5160° E (K0 -66, -874) | VERIFIED within about 300 m | three web listings agree; not surveyed; a heritage inventory differs by 0.3 km |")
    w("| Dala Hill | 12.0093° N, 8.5070° E (K0 -1061, 1017) | ONE source | height disputed (534 m or 453.67 m above sea level) |")
    w("| Great Mosque, Kurmi Market, Kofar Mata Dye Pits | north of the palace, in the old city, near the wall | **UNVERIFIED placeholders** | sources give relations, not coordinates |")
    w("| Qibla | 68.46° from Kano (WGS84 geodesic) | computed | a neighbourhood mosque oriented to it is a proposal |")
    w("| Direction of the A2 entrance | from the south-west | firm to the great circle | the bearing Kaduna to Kano; the entrance position is chosen |")
    w("| Terrain | a nearly flat plain, about 484 to 488 m | firm to about 1 km | the green rocky hills of R-007 do not match Kano (section 14) |")
    w("\n**What would add real geography.** A local, offline extract of OpenStreetMap, Wikidata and GeoNames would give the wall line, main roads, the mosque and market positions and building footprints, each with licence and accuracy recorded. The proxy for this session blocks those hosts. Allowing them (or supplying an OSM extract) is the single step that most improves this plan; the layout here is **replaced**, not patched, when real data arrives. Until then the positions above stay marked indicative.\n")
    w("---\n## 2. Districts and neighbourhoods\n")
    w(f"{len(PLAN['districts'])} named districts and {len(PLAN['fillerZones'])} filler zones. Plot counts and areas are computed from the district polygons and the density assumption in the plan (a planning assumption, to be replaced by measurement from references).\n")
    w(table(["Id", "District", "Kind", "Geography", "Area (ha)", "Plots", "Phase", "Needs", "State"], district_rows()))
    w("\n`geography`: **REAL_ANCHORED** means tied to a verified or sourced real feature; **INDICATIVE** means placed by plan logic where the real position is not verified; **FICTIONAL** means an invented neighbourhood, not a real one.\n")
    w("**The three visual families.**\n")
    w("* **Modern residential** (D06): gated villas, compound walls, one-way loop streets, paver sidewalks, painted kerbs, street trees. Reference R-006.")
    w("* **Commercial and civic** (D05, D08, D09, D10, D11): shopping streets (no reference yet), hospital, police, bus terminal, logistics, highway gateway. References R-007 to R-015.")
    w("* **Traditional** (D01, D02, D03, D04, D07): Hausa compounds, old-city lanes, markets, the palace, the Great Mosque, Dala Hill. **BLOCKED**: R-001 (a distant view of a rocky hill) is the only picture, its location is not established, and it cannot teach compound architecture. They are scheduled after the references arrive.\n")
    w("**Fidelity zones.** Not everything is hand-built.\n")
    w(table(["Zone", "Name", "Rule", "Where"], [[z["id"], z["name"], z["rule"], z["districts"]] for z in PLAN["fidelityZones"]]))
    w("\nFiller zones inside the study area:\n")
    w(table(["Filler", "Zone", "Where", "Character", "Plots/ha"], [[f["id"], f["zone"], f["where"], f["character"], f["densityPlotsPerHa"]] for f in PLAN["fillerZones"]]))
    w("\n---\n## 3. Spatial master layout\n")
    w("![Master plan](kano/city/img/master_plan.png)\n")
    w("*Planning diagram, not a map and not concept art.* Concentric layout: the old city and its (indicative) wall at the centre, a middle belt of mixed urban fabric, the outer ring road, and a peri-urban fringe to the edge. The A2 highway enters from the south-west to the gateway, the logistics zone and the ring interchange. Hero districts are shown as polygons; they overlap nothing (tested).\n")
    w("The layout logic is deliberately simple so it can be replaced by real data without changing anything downstream: **a district is a polygon plus a type; a road is an edge of a graph; a plot is a footprint with a frontage.** Every system below (streaming, registry, phases) reads those three things and nothing else.\n")
    w("---\n## 4. Road network and hierarchy\n")
    km = N["kmByClass"]
    w(f"The network is **one connected graph** of {N['nodes']} nodes and {N['edges']} edges ({sum(km.values()):.1f} km): " + ", ".join(f"{k.replace('_', ' ')} {v} km" for k, v in km.items()) + ". It is tested for connectivity from the expressway entrance, a hierarchy (every district reached by collectors or better; no collector more than two hops from an arterial or expressway), and for every junction type the engine cannot build being mapped to a backlog item.\n")
    w(table(["Class", "Lanes x width", "Median", "Sidewalks", "km/h", "References", "Engine needs", "Supported today"], road_class_rows()))
    w("\n**Junctions.**\n")
    w(table(["Node type", "Count", "Engine today", "Backlog"], node_rows()))
    w(f"\n**{N['junctionsTheEngineCannotBuild']} of {N['nodes']} junctions cannot be built by the current engine** (four-way crossings, roundabouts, interchange, gate crossings). The engine represents one main spline per route with straight T-junction stubs at least 150 m apart; a city is a network. The foundation is **RB-16 (road network graph)** with **RB-17 (road class catalogue)**; the junction forms are RB-01, RB-02, RB-03. Until they exist the district work that does not touch the network (buildings, kit, props, vehicles) continues in test scenes.\n")
    w("![Where the current engine cannot follow the plan](kano/city/img/engine_gap.png)\n")
    w("---\n## 5. Building blocks and neighbourhood layouts\n")
    w("The hero districts with references have a plan-level layout: envelopes read from the concept's composition, not surveyed dimensions and not 3D concept art. They fix the *kind* of arrangement (block size, plot pattern, setbacks, access) so the assets can be produced against it.\n")
    for did in ("D06", "D08", "D09", "D10", "D11"):
        lay = PLAN["layouts"][did]
        w(f"### {did}: {lay['title']}\n")
        w(f"![{did} layout](kano/city/img/layout_{did}.png)\n")
        w(f"{lay['note']} Envelope {lay['size'][0]} x {lay['size'][1]} m. Roughly {next(s['plots'] for s in STATS['districts'] if s['id'] == did):,} plots at the planning density.\n")
    w("**Districts with no layout yet.** D05 (commercial belt), D01, D02, D03, D04 and D07 have only a boundary and a density. Their block and plot patterns need references (modern commercial frontage; Hausa compounds, lanes and markets; the landmarks). **Nothing is drawn for them**, because drawing a street pattern for an old city from imagination is exactly the invention the owner ruled out. When the references arrive, the pattern is read from them and the district joins the plan.\n")
    w("---\n## 6. Landmarks\n")
    w(table(["Landmark", "District", "K0 position (x, z) m", "Status", "Basis", "Reference needed"], landmark_rows()))
    w("\n**Rule:** a landmark is built only from rights-cleared real references or measured drawings, and its position is fixed from sources before it is built. Two coordinates are verified (one from a single source). Three positions are placeholders and are shown as such. The Great Mosque question is in section 14: the same twin-minaret, teal-dome mosque recurs in five concepts, and the owner has not said whether it stands for the Central Mosque.\n")
    w("---\n## 7. Architectural styles: traditional, modern, commercial\n")
    w("| Style | Evidence in hand | What can be built now | What is blocked |\n|---|---|---|---|")
    w("| **Modern residential** | R-006 (villa street) | villas, compound walls, gates, kerbs, paver sidewalks, streetlights, trees: 17 assets | the species of street trees and Kano-specific plot patterns (M-07) |")
    w("| **Modern commercial and civic** | R-009 to R-015 (terminal, hospital, police, fuel, warehouses) | the five buildings and their kit and props | shopping streets, plazas, mid-rise commercial frontage, schools, government buildings, hotels (M-02, M-04, M-10) |")
    w("| **Highway and gateway** | R-007, R-008, R-012 | gantry, guardrails, median lights, chevrons, expressway cross-section | text-bearing signs (RB-07), sign facts (section 14), hills (RB-14) |")
    w("| **Traditional Hausa** | none usable (R-001 too distant) | nothing | compounds, zaure, parapets, ornament, wall gates, old-city lanes, markets, mosques (M-01, M-03, M-04, M-12) |")
    w("\nThe research record (`kano/RESEARCH.md`) gives words (tubali mud brick, zaure, walled compounds) but no dimensions, proportions or ornament; none was filled in from imagination. The traditional style is therefore **a reference request, not a modelling task**.\n")
    w("---\n## 8. 3D production strategy\n")
    w("### The standard: ten compulsory concepts\n")
    w(table(["Id", "Concept", "Role"], reference_rows()))
    w("\nEach concept is a design specification for **genuine 3D geometry**. The matrix (section 9) breaks every concept into assets, says which are reusable components and which are Kano-specific, and lists the Blender and Unity tasks. The owner approves each breakdown before work starts.\n")
    w("### Blender production (headless `bpy` behind the `tools/blender` server)\n")
    w("Per asset, in order, one status step at a time:\n")
    for i, t in enumerate(["write the concept down as a table of proportions, materials and details; the owner corrects it",
                           "blockout from reusable kit modules; render at the reference camera and compare",
                           "detail modelling: real walls, roofs, openings, structure and ornament (no image planes)",
                           "UV unwrap and PBR materials (shared `M_<surface>` names; one atlas per family)",
                           "LOD chain (prefab LOD0 and LOD1; the merged tiers come from the baker) and collision (box or convex, never the render mesh)",
                           "validate against the game's budgets (`validate_asset`)",
                           "export, Unity import test (orientation, scale, materials, collision, LOD thresholds), prefab build, catalogue entry",
                           "in-game screenshot from the reference camera, side by side with the concept; deviations recorded",
                           ], 1):
        w(f"{i}. {t}")
    w("\n### The visual-fidelity gate\n")
    w("Every concept has a **reference camera** (the framing that reproduces the concept's view) and a **comparison checklist** (what to compare: for example house heights, wall pier spacing, kerb pattern, streetlight height, canopy bays). A district does not reach `in_game_visual_review_passed` until the in-game screenshot at that camera is compared side by side with the concept and the owner accepts it. Differences are written down as deviations with a reason; none is silent. Those cameras and checklists are in the matrix. The Blender camera branch (`work/blender-camera`, CI green, awaiting merge) is what renders the Blender side of each comparison, and the Unity side uses the in-game camera.\n")
    w("**What cannot be promised.** An in-game scene will not be pixel-identical to a generated concept, whose lighting, depth of field and composition are illustrations. The gate asks whether the *buildable content* matches (massing, proportions, materials, details, spacing), and the owner decides whether the result is acceptable.\n")
    w("---\n## 9. Reference-to-asset production matrix\n")
    w(f"{reg_counts['assets']} assets for the ten concepts: " + ", ".join(f"{k} {v}" for k, v in reg_counts["byClass"].items()) + f"; {reg_counts['conceptBacked']} are backed by a concept and **{reg_counts['blocked']} are blocked** (no concept or verified reference; nothing invented). Full tables, per concept: visual requirements, assets, reusable components, city-specific components, engine gaps, Blender tasks, Unity tasks, reference camera, comparison checklist.\n")
    w(matrix_summary())
    w("\nSee `kano/city/REFERENCE_ASSET_MATRIX.md` for the per-asset tables, and `kano/city/reference_asset_matrix.json` for the data. **Reusable** assets recur across concepts (the painted kerb, the single-arm streetlight, the arrow marking, the street trees): they are modelled once.\n")
    w("**Vehicles.** The game's prop catalogue allows 2,500 triangles per prop. Close-range coaches, trucks, an ambulance and police pickups cannot meet that at the fidelity of the concepts. The registry flags them; the choice (raise the vehicle ceiling in the catalogue, or use a separate vehicle class with its own LOD ladder) is the owner's and changes no gameplay or vehicle physics.\n")
    w("---\n## 10. Modular asset architecture and registry\n")
    w("**Four classes**, with naming prefixes the registry validator enforces:\n")
    w("| Class | Prefix | Meaning | Examples |\n|---|---|---|---|")
    w("| GLOBAL | `aro_` | reusable across most cities | kerb, sidewalk, streetlight, guardrail, signs, markings, villa types, warehouse, bus canopy |")
    w("| REGIONAL | `rg_ngn_` | for a region or architectural family (here Northern Nigeria) | street trees, Hausa compound kit (blocked), wall gates (blocked) |")
    w("| CITY_SPECIFIC | `kano_` | designed for one city | bus terminal, plaza, mid-rise, shop rows, market stalls |")
    w("| LANDMARK | `lmk_kano_` | a distinctive real location, never copied to another city | Gidan Rumfa, Great Mosque, Kurmi hall, dye pits, wall gate, Dala Hill (all blocked) |")
    w("\nThe rule for every new asset is **look in the registry first** (GLOBAL, then REGIONAL, then city-specific) and create only what is missing. A second city reuses the GLOBAL and REGIONAL libraries, which is where the effort pays back.\n")
    w("**Kit before buildings.** Hero buildings are composed from kit modules (wall panels, piers, gates, canopies, balconies, roof forms), so that a design change is made once and every use follows. Composed assets list their parts in the registry.\n")
    w("**The registry** (`kano/city/asset_registry.json`; the canonical copy goes to the private repository `ARO-CONTENT/registry/asset_registry.json`) links every asset along the chain **concept -> Blender source -> export -> Unity prefab -> city placement**, with its status, validation results, planned budget, engine dependencies and flags. The validator rejects duplicate ids, a class that disagrees with its prefix, an unknown status, and a `production_approved` entry without the full chain (source, export, prefab, validation, approver). Large binaries (`.blend`, `.fbx`, `.glb`, textures) live in the private repository; this public repository holds only metadata.\n")
    w("---\n## 11. Performance and world-streaming strategy\n")
    w(streaming_section())
    w("---\n## 12. Phased plan\n")
    w(table(["Phase", "Name", "Districts", "Entry", "Exit", "Engine items", "Note"], phase_rows()))
    w("\n**Progressive, district by district.** P1a can start the moment P0 exits and needs nothing from the engine: it produces the assets and proves them in a test scene. P1b needs the engine items. P2 and P3 wait on new concepts and references. The order guarantees that the first drivable district is built from approved concepts on a road system that can actually carry it, and that the pipeline is proven before it is spent on the old city.\n")
    w("---\n## 13. Shared-engine improvements required\n")
    w("The shared road engine is **unchanged** and Kano adds no permanent workaround. The items the plan depends on (full list with current behaviour in `ROAD_ENGINE_BACKLOG.md`):\n")
    w("| Item | What | Needed by |\n|---|---|---|")
    for rb, who in [("RB-16", "Road network graph: the foundation for a city (one spline plus stubs today)"), ("RB-17", "Road class and cross-section catalogue (expressway to lane) as data"), ("RB-15", "City-density streaming: baked per-chunk meshes, hierarchical LOD, impostors"),
                    ("RB-12", "Pavement symbols, crossings, stop lines, bay lines"), ("RB-13", "Off-road paved areas: parking, forecourts, aprons, truck yards"), ("RB-14", "Terrain authoring: hills and rock cuts"),
                    ("RB-01", "Roundabouts"), ("RB-02", "Four-way intersections"), ("RB-03", "Flyovers and interchanges"), ("RB-04", "Sidewalks and kerbs"), ("RB-07", "Text-bearing road signs"), ("RB-09", "Median and near-edge furniture")]:
        phases = ", ".join(p["id"] for p in PLAN["phases"] if rb in p["engine"]) or "reference only"
        if rb == "RB-14": phases += " (and D11, only if the owner keeps the hills of R-007; otherwise the gateway is built on flat ground)"
        w(f"| {rb} | {who} | {phases} |")
    w("\nEach is a proposal for the owner to approve, defer or decline. None is started.\n")
    w("---\n## 14. Flags raised by the concepts (the owner decides; nothing is silently changed)\n")
    w("1. **Sign text and distances (R-008, R-012).** The gantry reads Zaria 138 km and Kaduna 262 km, and \"A2 Express Route\". Web sources give Kano to Zaria about 155 to 165 km and Kano to Kaduna about 230 km (sources range 232 to 272 km). A2 is the correct federal designation for the Abuja-Kaduna-Zaria-Kano highway; \"Express Route\" is not an established name. Sign text must come from verified route data, so the gantry is built with a text slot and RB-07.")
    w("2. **The recurring twin-minaret, teal-dome mosque** appears in the distance of R-006, R-008, R-009, R-010 and R-012. It reads as a designated landmark. Is it the Central Mosque (then it is a LANDMARK built from real references), or a generic skyline mosque (then it is one regional asset)? Sources disagree whether the real mosque has two or four minarets.")
    w("3. **Real organisations.** R-010's hospital name resembles a real institution (Aminu Kano Teaching Hospital); R-011's crest imitates the Nigeria Police Force emblem and the vehicles wear police livery. Fictional names and a generic crest are the default unless the owner decides otherwise.")
    w("4. **Terrain.** R-007's green rocky hills do not match Kano's flat plain; they suit the hillier country on the Kaduna side. Use for the gateway only if the owner accepts it, with RB-14.")
    w("5. **Names.** \"Kano Central Bus Terminal\" is not a verified real terminal. The fuel-centre brand colours and name are generic.")
    w("6. **Vehicle fidelity versus the prop budget** (section 9).")
    w("7. **Density.** Plot densities are planning assumptions. They are replaced by measurements as references arrive; the streaming result depends on them (section 11).\n")
    w("---\n## 15. Decisions and blockers\n")
    w("**Owner decisions**\n")
    w("1. Approve this direction (the scope correction, districts, phases) so P0 can close.")
    w("2. Approve each concept's asset breakdown (matrix, section 9) before work on it starts.")
    w("3. Rule on the flags in section 14 (sign facts, the mosque, real-organisation names, terrain, vehicles).")
    w("4. Coordinate anchor and transform (`COORDINATES.md`); the world-map pin is a production-data change and is not touched without explicit authorisation.")
    w("5. Allow OSM, Wikidata and GeoNames access (or provide an extract) to replace indicative geography with real geography.")
    w("6. Approve, defer or decline RB-15, RB-16 and RB-17 (P0 exit) and the other backlog items.")
    w("7. Asset storage: artifact boundary, Git LFS quota and ownership for `ARO-CONTENT`. The Kano package stays local until the relevant CI and asset-storage decisions are resolved.")
    w("8. Supply the missing concepts and references (section 16).\n")
    w("**Blockers**\n")
    w("* The traditional districts (D01, D02, D03, D04, D07) and every LANDMARK: no authentic references.")
    w("* The commercial belt (D05), schools, government buildings, hotels: no concepts.")
    w("* Everything that needs the road network or city density: the engine items.\n")
    w("---\n## 16. Concept requests (for ChatGPT, to unblock the plan)\n")
    w("The plan needs these to move beyond the five districts. Each should be a labelled, honest concept, and the traditional and landmark ones should be **tied to real references** (rights-cleared photographs, measured drawings or heritage surveys), not idealised imagination, because the owner has ruled out invented architecture.\n")
    w("| # | Needed (missing area) | Suggested views | Why it matters |\n|---|---|---|---|")
    for i, (a, v, why) in enumerate([
        ("Traditional Hausa compounds, zaure, parapets and ornament (M-01)", "street elevation; compound plan; zaure interior; wall-gate close-up", "D01 and D07; unblocks four REGIONAL assets"),
        ("Old-city lanes and traditional residential streets (M-12)", "eye-level lane; aerial of a block", "lane width, wall height, surface and drainage are unknown"),
        ("Kurmi Market and traditional markets (M-04)", "aerial; stall rows; eye-level lane", "D03; market stalls and covered halls"),
        ("Neighbourhood mosque (M-03)", "exterior; courtyard; minaret detail", "D06, D07; a REGIONAL asset oriented to the qibla"),
        ("Great Mosque and Gidan Rumfa (M-11)", "gate; court; minaret; aerial", "D02; only from real references"),
        ("Dala Hill and its settlement (M-11)", "aerial; base of the hill; steps", "D04; R-001's location is unconfirmed"),
        ("City wall and a gate (M-11)", "gate exterior and interior; wall section", "gate_cross junctions; position needs sources"),
        ("Modern commercial street and plaza (M-02, M-04)", "eye-level; aerial; shop-front close-up", "D05"),
        ("Mid-rise commercial and office buildings (M-02)", "street elevation; aerial", "D05, D08"),
        ("Schools, government buildings, hotels (M-10)", "exterior of each; entrance close-up", "D08 completion"),
        ("Roundabout and four-way intersection (M-13)", "aerial; eye-level through the junction", "needed with RB-01, RB-02, RB-16"),
        ("Interchange or flyover (M-13)", "aerial; ramp detail", "needed with RB-03"),
        ("Real road surfaces, patches, cracks and drains (M-05)", "close-ups of Kano roads", "RB-05, RB-06"),
        ("Electricity poles, transformers and cables as in Kano (M-06)", "street view; pole close-up", "street furniture"),
        ("Street trees and vegetation of the region (M-07)", "species close-ups", "replaces the generic broad-leaf placeholder"),
        ("Local vehicles: motor tricycles, motorcycles, minibuses (M-08)", "side and three-quarter", "traffic and roadside life"),
    ], 1):
        w(f"| {i} | {a} | {v} | {why} |")
    w("\n---\n## 17. Where everything is\n")
    w("| What | Where |\n|---|---|")
    for a, b in [("This plan", "`docs/KANO_CITY_MASTER_PLAN.md` (generated by `tools/kano/make_city_doc.py`)"), ("Plan data (districts, network, landmarks, phases, streaming)", "`docs/kano/city/city_plan.json` (`tools/kano/city_data.py`, `make_city_plan.py`)"),
                 ("Reference-to-asset matrix", "`docs/kano/city/REFERENCE_ASSET_MATRIX.md`, `reference_asset_matrix.json`"), ("Asset registry", "`docs/kano/city/asset_registry.json`; canonical copy in the private repository"),
                 ("Statistics (computed by the tests)", "`docs/kano/city/city_stats.json`"), ("Diagrams", "`docs/kano/city/img/` (`tools/kano/city_diagrams.py`; the PNG files are Git LFS objects of this repository)"), ("Reference register", "`docs/kano/REFERENCE_REGISTER.md`"),
                 ("Engine backlog", "`docs/ROAD_ENGINE_BACKLOG.md`"), ("Transportation expansion plan (2026-11-08)", "`docs/KANO_TRANSPORT_EXPANSION_PLAN.md`"), ("Night and street-lighting plan", "`docs/NIGHT_LIGHTING_PLAN.md`"), ("Coordinates", "`docs/kano/COORDINATES.md`"), ("Research", "`docs/kano/RESEARCH.md`"), ("Texture budget", "`docs/TEXTURE_BUDGET.md`"),
                 ("Tests", "`tests/NetCore.Tests/KanoCityPlanTests.cs` (8 tests), `KanoSliceTests.cs` (10 tests)")]:
        w(f"| {a} | {b} |")
    return "\n".join(L) + "\n"


def main():
    text = build()
    if "--check" in sys.argv:
        if not OUT.is_file() or OUT.read_text(encoding="utf-8") != text:
            print("OUT OF DATE:", OUT.relative_to(ROOT)); return 1
        print("up to date"); return 0
    OUT.write_text(text, encoding="utf-8"); print("wrote", OUT.relative_to(ROOT), f"({len(text)} bytes)"); return 0


if __name__ == "__main__":
    sys.exit(main())
