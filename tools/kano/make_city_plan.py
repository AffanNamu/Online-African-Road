#!/usr/bin/env python3
"""Generates the Kano CITY master-plan data from tools/kano/city_data.py and tools/kano/city_matrix.py:
  docs/kano/city/city_plan.json               districts, landmarks, road classes, road graph, district layouts, streaming model, phases
  docs/kano/city/reference_asset_matrix.json  the reference-to-asset production matrix (and REFERENCE_ASSET_MATRIX.md)
  docs/kano/city/asset_registry.json          the initial asset registry (also pushed to the private ARO-CONTENT repository as registry/asset_registry.json)
Usage: python3 tools/kano/make_city_plan.py [--check]"""
import json, math, sys
from pathlib import Path
sys.path.insert(0, str(Path(__file__).resolve().parent))
import city_data as D, city_matrix as M

ROOT = Path(__file__).resolve().parents[2]; OUT = ROOT / "docs" / "kano" / "city"
REG = json.loads((ROOT / "docs/kano/reference_register.json").read_text()); REFS_BY_ID = {r["id"]: r for r in REG["references"]}
FRAME = json.loads((ROOT / "docs/kano/kano_world_frame.json").read_text())
RB_TITLES = {"RB-01": "Roundabouts", "RB-02": "Four-way intersections", "RB-03": "Flyovers and interchanges", "RB-04": "Sidewalks and kerbs", "RB-05": "Drainage infrastructure", "RB-07": "Text-bearing production road signs",
             "RB-09": "Median and near-edge furniture", "RB-12": "Pavement symbols, crossings, bay lines", "RB-13": "Off-road paved areas", "RB-14": "Terrain authoring", "RB-15": "City-density streaming", "RB-16": "Road network graph", "RB-17": "Road class catalogue"}


def area_ha(poly):
    s = 0.0
    for i in range(len(poly)):
        x1, z1 = poly[i]; x2, z2 = poly[(i + 1) % len(poly)]; s += x1 * z2 - x2 * z1
    return abs(s) / 2 / 10000.0


PHASES = [
    dict(id="P0", name="Foundations (no modelling)", districts=[], entry="this master plan reviewed and approved by the owner", exit="asset registry live in the private repository; reference-comparison harness defined; engine roadmap items RB-15, RB-16 and RB-17 designed and approved or declined; reference gaps and coordinate decisions closed",
         engine=["RB-15", "RB-16", "RB-17"], note="Everything that does not need the engine or a missing reference can start the moment P0 exits."),
    dict(id="P1a", name="Gateway and civic assets (engine-independent)", districts=["D11", "D10", "D09", "D08", "D06"], entry="P0 exit; the asset breakdown of each concept approved", exit="every concept-backed asset of the matrix reaches unity_import_verified in a test scene, with the in-game screenshot compared against its concept",
         engine=[], note="Buildings, kit modules, props and vehicles import and render in the existing engine; placement uses a test scene, not the city network."),
    dict(id="P1b", name="Gateway and civic districts in the game", districts=["D11", "D10", "D09", "D08", "D06"], entry="P1a exit and the shared-engine items approved", exit="the five districts are drivable, compared against their concepts in game screenshots, and measured against the tier budgets",
         engine=["RB-01", "RB-02", "RB-03", "RB-04", "RB-07", "RB-09", "RB-12", "RB-13", "RB-15", "RB-16", "RB-17"], note="Blocked until the engine delivers a road network, kerbs and sidewalks, pavement symbols, off-road areas and city-density streaming."),
    dict(id="P2", name="Commercial belt and civic completion", districts=["D05", "D08"], entry="concepts for modern commercial frontage, schools, government buildings and hotels (M-02, M-04, M-10)", exit="D05 and the rest of D08 as P1b", engine=[], note="Needs new concepts first."),
    dict(id="P3", name="Traditional districts", districts=["D01", "D03", "D07"], entry="authentic references for Hausa compounds, old-city lanes and markets (M-01, M-04, M-12)", exit="as P1b; hero sector first", engine=["RB-17"], note="BLOCKED: nothing is invented without references."),
    dict(id="P4", name="Landmarks", districts=["D02", "D04"], entry="rights-cleared real references or measured drawings for each landmark (M-11)", exit="each landmark reviewed against its source", engine=["RB-14"], note="A landmark is never invented; positions of unverified landmarks are fixed from sources first."),
    dict(id="P5", name="Expansion", districts=[], entry="the pipeline and library proven on Kano", exit="further Kano sectors, the Kaduna-Kano corridor (compressed, anchored at both cities) and the next cities, reusing the registry before creating assets", engine=["RB-15", "RB-16"], note="Each new city checks the registry for GLOBAL and REGIONAL assets first."),
]
FIDELITY = [dict(id="Z-A", name="Hero districts", rule="hand-authored layout and hero buildings; full prefab LOD0 and LOD1", districts="D01 to D11"),
            dict(id="Z-B", name="Filler fabric", rule="inside the ring road but outside hero districts: generated from the kit by rules (plots, streets), prefab LOD1 and baked blocks only", districts="rest of the ring"),
            dict(id="Z-C", name="Distant city", rule="beyond the ring road: terrain tint, baked skyline and impostors only; haze limits the view", districts="outside the ring")]


def build_plan():
    nodes, edges = D.build_network()
    dists = []
    for d in D.DISTRICTS:
        e = dict(d); e["polygon"] = [[round(x, 1), round(z, 1)] for x, z in d["polygon"]]; e["areaHa"] = round(area_ha(d["polygon"]), 1)
        e["netAreaHa"] = round(e["areaHa"] - sum(area_ha(c["polygon"]) for c in D.DISTRICTS if c.get("parent") == d["id"]), 1)      # a parent excludes its child districts
        e["plots"] = round(e["netAreaHa"] * d["density"]); e["densityPlotsPerHa"] = e.pop("density")
        e["densityNote"] = "PLANNING ASSUMPTION, to be replaced by reference measurement"; dists.append(e)
    lm = []
    frame_anchors = {a["id"]: a for a in FRAME["reservedAnchors"]}
    for l in D.LANDMARKS:
        e = dict(l); e["xz"] = list(l["xz"]); e["verified"] = l["position"].startswith("VERIFIED")
        if l["frame_id"]: e["frameXz"] = frame_anchors[l["frame_id"]]["xz_m"]
        lm.append(e)
    return {
        "schemaVersion": 1, "id": "ksa-1", "name": "Kano study area KSA-1: master plan (draft for owner approval)",
        "status": "PLAN ONLY. Nothing here is modelled, surveyed or read by the game. Districts, roads and plots are INDICATIVE FICTIONAL placements except where a landmark position is marked VERIFIED.",
        "frame": "kano-frame-k0 (docs/kano/kano_world_frame.json)", "studyArea": dict(xzMin=[D.STUDY[0], D.STUDY[1]], xzMax=[D.STUDY[2], D.STUDY[3]], areaKm2=100.0, chunks=2500),
        "oldCity": dict(centre=list(D.CENTRE), semiAxesM=[D.WALL_A, D.WALL_B], status="INDICATIVE: an ellipse whose perimeter (13.8 km) matches the 14 km UNESCO figure (Daily Trust says 17 km); the real wall line is not surveyed here"),
        "ringRoad": dict(radiusM=D.RING_R, centre=list(D.CENTRE), status="INDICATIVE"), "roadTestCorridor": dict(id="TC-1", note="The 1.65 km corridor of revision 1 is kept only as the road-system test corridor TC-1 (docs/kano/ng-kano-slice.route.draft.json); it is not the city."),
        "districts": dists, "fillerZones": D.FILLER, "landmarks": lm, "fidelityZones": FIDELITY, "roadClasses": D.ROAD_CLASSES, "nodeTypes": D.NODE_TYPES,
        "nodes": [dict(n, xz=list(n["xz"])) for n in nodes.values()], "edges": edges, "layouts": D.LAYOUTS, "streaming": D.STREAMING, "phases": PHASES,
    }


def build_matrix():
    refs = []
    for rid, info in M.REFS.items():
        ids = [a for a, v in M.ASSETS.items() if rid in v["refs"]]; assets = [M.ASSETS[a] for a in ids]
        deps = sorted({d for a in assets for d in a["engineDeps"]})
        refs.append(dict(ref=rid, scene=info["scene"], districts=info["districts"], visualRequirements=REFS_BY_ID[rid]["visualRequirements"], flags=REFS_BY_ID[rid]["flags"], referenceCamera=info["camera"], comparisonChecklist=info["check"],
                         assets=[dict(id=a["id"], name=a["name"], cls=a["cls"], category=a["category"], engineDeps=a["engineDeps"], flags=a["flags"], alsoUsedBy=[r for r in a["refs"] if r != rid]) for a in assets],
                         reusableComponents=[a["id"] for a in assets if a["cls"] in (M.G, M.R)], cityComponents=[a["id"] for a in assets if a["cls"] in (M.C, M.L)],
                         engineGaps=[dict(rb=d, title=RB_TITLES.get(d, d)) for d in deps], blenderTasks=M.BLENDER_TASKS, unityTasks=M.UNITY_TASKS))
    blocked = [dict(id=a["id"], name=a["name"], cls=a["cls"], category=a["category"], blockedBy=a["blockedBy"], districts=a["districts"], engineDeps=a["engineDeps"]) for a in M.ASSETS.values() if "blockedBy" in a]
    by_cls = {}
    for a in M.ASSETS.values(): by_cls[a["cls"]] = by_cls.get(a["cls"], 0) + 1
    return dict(schemaVersion=1, rule="A reference is a design specification to reproduce in genuine 3D, not an image to place on a plane. Deviations from a concept must be explained to the owner first.", references=refs, blockedAssets=blocked,
                counts=dict(assets=len(M.ASSETS), byClass=by_cls, conceptBacked=len(M.ASSETS) - len(blocked), blocked=len(blocked)), statusVocabulary=M.STATUS_VOCAB)


CAT_LOD0 = dict(buildings=5000, vegetation=3000, signs=800, lights=1200, props=2500, environment=2500)
COMPOSED = {"kano_bus_terminal_a": ["aro_bus_terminal_hall_a", "aro_canopy_bus_bay_solar_a", "aro_guardhouse_a", "aro_barrier_arm_gate_a", "aro_wall_perimeter_pier_rail_a", "aro_planter_island_kerbed_a", "aro_sign_totem_service_a", "aro_sign_building_band_a"],
            "kano_hospital_a": ["aro_hospital_block_glass_a", "aro_hospital_wing_a", "aro_canopy_emergency_a", "aro_covered_walkway_a", "aro_planter_island_kerbed_a", "aro_sign_totem_service_a", "aro_bollard_light_a"],
            "kano_police_station_a": ["aro_police_station_a", "aro_guardhouse_a", "aro_gate_sliding_steel_a", "aro_wall_perimeter_pier_rail_a", "aro_flagpole_a"]}


def build_registry():
    entries = []
    for a in M.ASSETS.values():
        blocked = "blockedBy" in a
        region = "ng_north" if a["cls"] == M.R else None; city = "kano" if a["cls"] in (M.C, M.L) else None
        e = dict(id=a["id"], name=a["name"], assetClass=a["cls"], category=a["category"], region=region, city=city, approvedCities=[] if a["cls"] in (M.G, M.R) else ["kano"],
                 concepts=a["refs"], districts=a["districts"], status="needs_concept" if blocked else "concept_approved", breakdownApproved=False,
                 blenderSource=None, export=None, unityPrefab=None, placements=[], validation=dict(blender=None, unityImport=None, visualReview=None), approvedBy=None,
                 plannedBudget=dict(lod0TrisMax=CAT_LOD0[a["category"]], note="catalogue ceiling for the category; the LOD ladder of the master plan applies"), engineDependencies=a["engineDeps"], flags=a["flags"])
        if a["id"] in COMPOSED: e["composedOf"] = COMPOSED[a["id"]]
        if blocked: e["blockedBy"] = a["blockedBy"]
        entries.append(e)
    return dict(schemaVersion=1, note="Initial asset registry. Chain: visual concept -> Blender source -> production export -> Unity prefab -> city placement. Nothing is production-ready: no asset has a Blender source yet. 'concept_approved' means a concept was supplied as the compulsory design standard; the owner's approval of each asset breakdown is the next gate (breakdownApproved).",
                statusVocabulary=M.STATUS_VOCAB, classes=[M.G, M.R, M.C, M.L], idPrefix={M.G: "aro_", M.R: "rg_<region>_", M.C: "<city>_", M.L: "lmk_<city>_"}, assets=entries)


def matrix_md(m):
    L = ["# Reference-to-asset production matrix", "", "Generated by `tools/kano/make_city_plan.py`. **Status: planning. No asset below has a Blender source yet.** Each concept (R-006 to R-015, ChatGPT-generated, the owner's compulsory design standard) is a design specification to reproduce in genuine 3D geometry; it is never placed on a plane, used as a texture or shipped. Where the game cannot yet reproduce something, the engine gap is named (RB items, `docs/ROAD_ENGINE_BACKLOG.md`) and the deviation is explained instead of silently simplified.", "",
         f"**{m['counts']['assets']} assets**: {', '.join(f'{k} {v}' for k, v in m['counts']['byClass'].items())}; {m['counts']['conceptBacked']} backed by a concept, **{m['counts']['blocked']} blocked** (no concept or verified reference yet; nothing is invented for them).", "",
         "Classes: **GLOBAL** (`aro_`) reusable across most cities; **REGIONAL** (`rg_<region>_`) for a geographic or architectural region (`ngn` = Northern Nigeria); **CITY_SPECIFIC** (`kano_`) designed for one city; **LANDMARK** (`lmk_kano_`) a distinctive real location, never copied to another city.", "",
         "Status vocabulary (an asset moves through these in order and never skips): " + " -> ".join(m["statusVocabulary"]) + ".", ""]
    for r in m["references"]:
        L += [f"## {r['ref']}: {r['scene']}", "", f"Districts: {', '.join(r['districts'])}. Reference camera for the in-game comparison: {r['referenceCamera']}.", "", "**Visual requirements:** " + "; ".join(r["visualRequirements"]) + ".", ""]
        if r["flags"]: L += ["**Flags to resolve:** " + " ".join(r["flags"]), ""]
        L += ["**Compare in the game:** " + "; ".join(r["comparisonChecklist"]) + ".", "", "| Asset | Class | Category | Also used by | Engine gap | Flag |", "|---|---|---|---|---|---|"]
        for a in r["assets"]:
            L.append(f"| `{a['id']}` {a['name']} | {a['cls']} | {a['category']} | {', '.join(a['alsoUsedBy']) or '-'} | {', '.join(a['engineDeps']) or '-'} | {a['flags'] or '-'} |")
        L += ["", f"Reusable ({len(r['reusableComponents'])}): " + ", ".join(f"`{x}`" for x in r["reusableComponents"]) + ". City-specific: " + (", ".join(f"`{x}`" for x in r["cityComponents"]) or "none") + ".", "",
              "Engine gaps for this concept: " + (", ".join(f"{g['rb']} ({g['title']})" for g in r["engineGaps"]) or "none") + ".", ""]
    L += ["## Common tasks for every asset", "", "**Blender:**", ""] + [f"{i + 1}. {t}" for i, t in enumerate(m["references"][0]["blenderTasks"])] + ["", "**Unity:**", ""] + [f"{i + 1}. {t}" for i, t in enumerate(m["references"][0]["unityTasks"])] + [""]
    L += ["## Blocked assets (no concept or verified reference yet)", "", "| Asset | Class | Category | Blocked by | Districts | Engine gap |", "|---|---|---|---|---|---|"]
    for a in m["blockedAssets"]: L.append(f"| `{a['id']}` {a['name']} | {a['cls']} | {a['category']} | {a['blockedBy']} | {', '.join(a['districts'])} | {', '.join(a['engineDeps']) or '-'} |")
    return "\n".join(L) + "\n"


def main():
    OUT.mkdir(parents=True, exist_ok=True); m = build_matrix()
    outputs = {"city_plan.json": json.dumps(build_plan(), indent=1) + "\n", "reference_asset_matrix.json": json.dumps(m, indent=1) + "\n", "REFERENCE_ASSET_MATRIX.md": matrix_md(m), "asset_registry.json": json.dumps(build_registry(), indent=1) + "\n"}
    bad = 0
    for name, text in outputs.items():
        p = OUT / name
        if "--check" in sys.argv:
            if not p.is_file() or p.read_text(encoding="utf-8") != text: print("OUT OF DATE:", p.relative_to(ROOT)); bad += 1
        else: p.write_text(text, encoding="utf-8"); print("wrote", p.relative_to(ROOT), f"({len(text)} bytes)")
    return 1 if bad else 0


if __name__ == "__main__": sys.exit(main())
