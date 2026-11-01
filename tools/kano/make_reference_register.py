#!/usr/bin/env python3
"""Generates docs/kano/reference_register.json and docs/kano/REFERENCE_REGISTER.md: the provenance and usage-rights record of every visual reference, and the list of references still missing.
The register records METADATA ONLY (a SHA-256 and the pixel size identify a screenshot); the images themselves are never copied into this repository.
Usage: python3 tools/kano/make_reference_register.py [--check]"""
import json, sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]; OUT = ROOT / "docs" / "kano"
H = {  # sha256 / bytes / pixels of the screenshots as received (measured 2026-11-01)
    "R-001": ("dd807c5b1822e6c21c43ac460f19d6804904a434acc9a00a773b87b3115d0c28", 914168, "864x563"),
    "R-002": ("a84720102622a8d6cc3d22cc0c05fec6c8691643f520ddc4a8638d46448fdff1", 933161, "864x1728"),
    "R-003": ("9077fcdf3eaab52bb1568a9947a4fb043f61e732b3051821cabd79078af6b99f", 1006325, "864x637"),
    "R-004": ("335930e58b478f66e0e584fbb5c69a64a8fa7e34d2e0cb58507a293c4a342973", 1274879, "864x690"),
    "R-005": ("8433a1b706be9de879fcd120bd89e50ef21da78c67926b1d7c1ab40a1fc62c75", 945355, "864x506"),
}
CLASS = "ChatGPT-provided visual research reference (screenshot forwarded by the project owner). Not an original photograph by the project owner, not a verified image of any named place, not a production asset."
COMMON = {"receivedVia": "Researched and supplied by ChatGPT; the project owner took screenshots and forwarded them to Claude on 2026-11-01. The owner did not source or photograph them.",
          "originalSource": "UNKNOWN. The screenshots carry no page address, caption or credit (except R-005's watermark), and none was provided with them.",
          "rights": "UNKNOWN. Treated as all rights reserved by whoever made them.",
          "storedInRepository": False,
          "uses": {"privateReference": "Acceptable as a private visual study aid only (palette, density, rhythm, layout ideas), pending the owner's decision. No tracing, no copying of buildings.",
                   "publicDocumentation": "NO. Do not embed, link as hosted copies, or publish in any public repository, document, release note or social post. The review page that shows them is private and must not be shared.",
                   "productionRedistribution": "NO. Never a texture, decal, reference plane, baked source or any part of a build or a private-repository production asset."}}
REFS = [
    {"id": "R-001", "label": "Dense traditional settlement around a rocky hill", "ownerInterpretation": "Visual reference for the Dala Hill environment, subject to geographical verification.",
     "observed": "Oblique aerial; dense low-rise fabric; flat or low-pitched roofs with corrugated sheet and roof tanks; tan and ochre walls; irregular lanes; a quarried outcrop with a reddish-brown face; few trees.",
     "actualLocation": "NOT ESTABLISHED. Consistent in character with the Dala Hill area as described in the brief; nothing in the image proves it.", "verificationNeeded": "the originating page or photographer; a map comparison of the outcrop and street pattern against Dala Hill (12.0093N 8.5070E, one source)",
     "usableFor": "environment mood, roofscape and density; the outcrop as a landmark silhouette", "notUsableFor": "building proportions (too distant); any claim that it shows Kano"},
    {"id": "R-002", "label": "Modern residential district (phone screenshot of R-003)", "ownerInterpretation": "Essentially the same image as R-003: contemporary residential layouts, building heights, roofs, greenery and street arrangements.",
     "observed": "The same aerial as R-003 shown in a photo viewer (close and menu buttons visible at the top; large black bands).", "actualLocation": "NOT ESTABLISHED.", "verificationNeeded": "see R-003",
     "usableFor": "duplicate of R-003; kept only so the screenshot can be identified", "notUsableFor": "anything beyond R-003"},
    {"id": "R-003", "label": "Modern residential district", "ownerInterpretation": "Contemporary residential layouts, building heights, roofs, greenery and street arrangements.",
     "observed": "Two to four storey buildings with hipped or low-pitched roofs in red, brown, cream, white and green; walled plots; mature trees and palms; a wide street with parked cars; a line of hills on the horizon.",
     "actualLocation": "NOT ESTABLISHED. A distant line of hills is visible; the sources found describe Kano as a nearly flat plain, so the hills are a reason to check, not a conclusion.", "verificationNeeded": "the originating page or photographer; identify the hills and street pattern on a map",
     "usableFor": "contemporary residential massing, heights, roof shapes, greenery, street arrangement", "notUsableFor": "evidence of what Kano looks like until the location is established"},
    {"id": "R-004", "label": "Large urban junction and roundabout", "ownerInterpretation": "Urban junction and roundabout design reference. Do not implement a roundabout until the shared road system supports it.",
     "observed": "Elevated multi-lane road with curving ramps; a landscaped roundabout with a fountain; a mosque with two minarets and a green dome; mixed mid-rise buildings; streetlights along the road edges.",
     "actualLocation": "NOT ESTABLISHED.", "verificationNeeded": "the originating page or photographer", "usableFor": "future road-infrastructure design inspiration (backlog items for roundabouts and flyovers); mosque type as one example", "notUsableFor": "anything implemented now; any claim that it shows Kano"},
    {"id": "R-005", "label": "Large highway interchange", "ownerInterpretation": "Highway interchange reference. Future road-infrastructure inspiration, not immediate implementation.",
     "observed": "Grade-separated interchange with several loops over a flat, hazy city; a tall tower at the right edge. A watermark reading 'NUUMAN ASHAKA' with a ring logo is on the image.",
     "actualLocation": "NOT ESTABLISHED. Flat plain and haze are consistent with Kano's setting; nothing proves it.", "verificationNeeded": "the photographer's own page (a web search for the watermark name found no portfolio or profile, 2026-11-01); the originating page",
     "usableFor": "future interchange inspiration; atmosphere (haze, flat horizon)", "notUsableFor": "textures or models in any form: the watermark shows it is someone's identified work"},
]
for r in REFS:
    sha, size, px = H[r["id"]]; r.update({"classification": CLASS, "file": {"sha256": sha, "bytes": size, "pixels": px, "note": "the screenshot as received; NOT stored in any repository"}}); r.update({k: v for k, v in COMMON.items()})

MISSING = [
    {"id": "M-01", "area": "Traditional Hausa compounds and gates", "drives": ["kano_hausa_compound_a", "kano_hausa_compound_b", "kano_hausa_compound_c", "kit_zaure_entrance", "kit_wall_plain", "kit_wall_decorated", "kit_gate_pier", "kit_gate_leaf", "kit_flat_roof_parapet", "kit_openings"],
     "need": "At least three distinct Kano compounds: the street front with the zaure and its door, the compound wall and its top (parapet, zankwaye), gates (leaves, piers), roof edge and roof furniture, windows, wall finish and wear. For each: a front, a side, a close detail, and something for scale (a person or a door). Heights and thicknesses of walls and parapets.", "status": "MISSING (no usable reference; R-001 is too distant)"},
    {"id": "M-02", "area": "Modern Kano buildings", "drives": ["kano_compound_modern_a", "kano_compound_modern_b", "kano_shop_row_a", "kano_shop_row_b", "kano_plaza_a", "kano_apartment_block_a", "kano_midrise_a", "kano_filling_station_a", "kit_storey_modern", "kit_shopfront"],
     "need": "Walled bungalows and villas with gates, shop rows and shop-and-flat blocks, a commercial plaza, an apartment block, a mid-rise block, a filling station, each shown as they are in Kano.", "status": "PARTIAL IDEAS ONLY (R-003 for contemporary residential massing once its location is established; nothing confirmed as Kano)"},
    {"id": "M-03", "area": "Mosques", "drives": ["kano_mosque_neighbourhood_a", "kit_dome_minaret"],
     "need": "A real Kano neighbourhood mosque (not the Great Mosque): dome(s), minaret(s), prayer-hall walls, ablution block, boundary wall, colours and finishes; front, side and detail.", "status": "PARTIAL IDEA (R-004 shows one mosque type, location not established)"},
    {"id": "M-04", "area": "Commercial streets and markets", "drives": ["kano_shop_row_a", "kano_shop_row_b", "kano_kiosk_a", "kano_market_stall_a", "kano_bus_stop_a", "kano_billboard_a"],
     "need": "Shopfronts with shutters and sign bands, kiosks, market stalls (frames, tarpaulins, goods), bus stops, street-level views of a commercial street with its activity.", "status": "MISSING"},
    {"id": "M-05", "area": "Road surfaces and drainage", "drives": ["kit_apron_drain", "road surface-condition plan (future engine layer)"],
     "need": "New, patched, cracked and damaged asphalt; edges and shoulders; open drains and their slabs; kerbs and sidewalks if present; road markings; how roads meet compound entrances. Close ground-level views with a scale object.", "status": "MISSING"},
    {"id": "M-06", "area": "Electricity poles and streetlights", "drives": ["kano_utility_pole", "kano_transformer_pole", "kano_streetlight_single", "kano_streetlight_double_median", "kit_pole_fittings", "kano_road_sign_small", "kano_road_sign_gantry"],
     "need": "Poles as installed (material, height, crossarms, insulators, cables, transformer), streetlights (pole, arm, luminaire, median double-arm), road signs and gantries.", "status": "MISSING"},
    {"id": "M-07", "area": "Northern Nigerian trees and vegetation", "drives": ["kano_neem_tree_a", "kano_neem_tree_b", "kano_date_palm_a", "kano_shrub_a"],
     "need": "The species that actually line Kano roads and compounds (the typical street tree is NOT established by the sources found), their size and form, dry-season foliage, ground colour and cover.", "status": "MISSING"},
    {"id": "M-08", "area": "Local vehicles and roadside activity", "drives": ["kano_parked_group_a", "kano_parked_group_b", "kano_parked_minibus_a"],
     "need": "Common cars, minibuses, motor tricycle taxis and motorcycles as parked along roads (generic shapes, no brands), and roadside activity (traders, loading, waiting).", "status": "MISSING"},
    {"id": "M-09", "area": "Light and atmosphere", "drives": ["lighting reference for review renders"], "need": "Harmattan haze, dusty light, sky colour and shadow softness at several times of day.", "status": "PARTIAL (R-005 for haze, location not established)"},
]
SOURCES = {
    "acceptableProvenance": ["Photographs taken by the project owner or by a person who grants written permission for private reference (and for redistribution if they will be shown publicly).",
                             "Licensed or commissioned photographs, with the licence kept in the private asset repository's LICENSES.md.",
                             "Openly licensed images (for example Wikimedia Commons), only after reading each file's own licence page; the licence and attribution are recorded per file. Not every Commons file is freely reusable.",
                             "Archival collections (for example the MIT Dome photographs of Gidan Rumfa, the Smithsonian Eliot Elisofon archive): their terms are not established; treat as private study references until the holder's terms are read.",
                             "Generated concept images are allowed to supplement real photographs but must be labelled 'CONCEPT: not a photograph of an actual Kano location' in the register and wherever they are shown."],
    "neverAcceptable": ["Copying or tracing a protected image into a texture, decal or model.", "Presenting a generated or non-Kano image as a photograph of a Kano location."]}

def build():
    return {"schemaVersion": 1, "generatedFor": "docs/KANO_VERTICAL_SLICE.md", "rule": "Metadata only. No image is stored in this repository. Do not claim any reference shows Kano unless its original location is established.",
            "references": REFS, "missingReferences": MISSING, "sourcing": SOURCES}

def markdown(d):
    L = ["# Kano reference provenance and usage-rights register", "",
         "**Rule:** this register holds metadata only. The images are not stored in this repository (a SHA-256 identifies each screenshot). **No image is claimed to show Kano** unless its original location is established. Generated by `tools/kano/make_reference_register.py` from `docs/kano/reference_register.json`.", "",
         "## 1. Classification of the five supplied images", "", f"All five: **{CLASS}**", "",
         "How they arrived: " + COMMON["receivedVia"], "", "Original source: " + COMMON["originalSource"], "", "Rights: " + COMMON["rights"], "",
         "| Id | What it is | Owner's interpretation | Actual location | Private reference | Public documentation | Production redistribution |", "|---|---|---|---|---|---|---|"]
    for r in d["references"]:
        L.append(f"| {r['id']} | {r['label']} | {r['ownerInterpretation']} | {r['actualLocation']} | acceptable as a private study aid (owner's decision) | **no** | **no** |")
    L += ["", "### Per image", ""]
    for r in d["references"]:
        L += [f"**{r['id']} ({r['label']})**", f"* File as received: SHA-256 `{r['file']['sha256']}`, {r['file']['bytes']:,} bytes, {r['file']['pixels']} px. Not stored in any repository.",
              f"* Observed: {r['observed']}", f"* To establish the location: {r['verificationNeeded']}.", f"* Usable for: {r['usableFor']}.", f"* Not usable for: {r['notUsableFor']}.", ""]
    L += ["### What each use means", "", f"* **Private reference:** {COMMON['uses']['privateReference']}", f"* **Public documentation:** {COMMON['uses']['publicDocumentation']}", f"* **Production redistribution:** {COMMON['uses']['productionRedistribution']}", "",
          "## 2. Missing references", "", "| Id | Area | What is needed | Drives | Status |", "|---|---|---|---|---|"]
    for m in d["missingReferences"]:
        L.append(f"| {m['id']} | {m['area']} | {m['need']} | {', '.join('`' + x + '`' for x in m['drives'])} | {m['status']} |")
    L += ["", "## 3. Where acceptable references can come from", ""] + ["* " + x for x in d["sourcing"]["acceptableProvenance"]] + ["", "Never acceptable:", ""] + ["* " + x for x in d["sourcing"]["neverAcceptable"]] + [""]
    return "\n".join(L)

def main():
    d = build(); js = json.dumps(d, indent=1, ensure_ascii=False) + "\n"; md = markdown(d) + "\n"; bad = 0
    for name, text in (("reference_register.json", js), ("REFERENCE_REGISTER.md", md)):
        p = OUT / name
        if "--check" in sys.argv:
            if not p.is_file() or p.read_text(encoding="utf-8") != text: print("OUT OF DATE:", name); bad += 1
        else: p.write_text(text, encoding="utf-8"); print("wrote", p.relative_to(ROOT))
    return 1 if bad else 0

if __name__ == "__main__": sys.exit(main())
