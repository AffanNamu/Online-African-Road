#!/usr/bin/env python3
"""Generates the two Kano CITY test routes and the catalogue entries their frontage rules need (owner directive "Kano city densification and mosque integration", 2026-11-09).

  ng-kano-city-e1   the E1 street-kit corridor (arterial, collector, local, service roads, a four-way crossing, a logistics yard and a bus terminal) with its roadside buildings replaced by FRONTAGE rules
  ng-kano-city-f1   the E5 flyover interchange (expressway on a deck over a ground roundabout) with frontage along the expressway and around the ring

Both are ENGINE TEST ROUTES reached only by ?smoke=drive&route=...; the production Lagos to Ibadan route and the original E1 and E5 test routes are not touched (their golden digests stay).
The frontage engine is Assets/_Project/Scripts/NetCore/Frontage.cs. Run from the repository root:  python3 tools/kano/make_city_routes.py   (writes the two route files and merges the catalogue entries; --check writes nothing and fails if a file would change)."""
import json, sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
ROUTES = ROOT / "Assets" / "_Project" / "Resources" / "Routes"
CATALOG = ROOT / "Assets" / "_Project" / "Resources" / "Props" / "prop_catalog.json"
TK = ROOT / "docs" / "kano" / "TRANSPORT_KIT_catalog.json"

# Built assets that are not in the transport kit (their numbers are those of the private library registry, status unity_import_verified): id -> (width, depth, height, LOD triangles)
KIT = {
    "aro_villa_2storey_hip_a": (13.6, 12.9, 10.45, [4802, 965, 58]), "aro_villa_2storey_hip_b": (16.48, 13.2, 9.114, [4289, 803, 70]),
    "rg_ngn_hausa_compound_a": (20.14, 16.18, 7.21, [2364, 108]), "rg_ngn_hausa_compound_b": (14.14, 12.18, 4.59, [1958, 96]),
    "rg_ngn_house_mudbrick_a": (8.84, 7.92, 4.46, [582, 38]), "rg_ngn_house_mudbrick_b": (10.84, 7.49, 3.96, [776, 30]), "rg_ngn_house_block_corrugated_a": (10.24, 7.24, 4.26, [1022, 34]),
    "rg_ngn_shop_row_roadside_a": (15.806, 6.77, 3.403, [1304, 28]), "rg_ngn_shed_market_corrugated_a": (15.4, 9.44, 4.36, [2708, 92]),
}
CULL = {"grand": 600, "medium": 450, "small": 350, "trad": 350, "city": 500, "warehouse": 300, "villa": 260, "house": 220}


def lot(prop, w, d, weight, tag): return {"prop": prop, "weight": weight, "widthM": w, "depthM": d, "tag": tag}


def dims():
    """id -> (width, depth, height, lods, entry or None) from the transport catalogue (E, F, warehouses) and KIT."""
    out = {}
    for p in json.loads(TK.read_text())["props"]: out[p["id"]] = (p["widthM"], p["depthM"], p["heightM"], p["lodTriangles"], p)
    for k, (w, d, h, t) in KIT.items(): out[k] = (w, d, h, t, None)
    return out


D = dims()


def L(prop, weight, tag):
    w, d, *_ = D[prop]; return lot(prop, round(w, 2), round(d, 2), weight, tag)


# ---- the mixes (a road class's families). Weights are shares of the slots, not counts.
MAJOR = [L("kano_office_block_a", 1.0, "office"), L("kano_midrise_apartments_a", 1.2, "apartments"), L("kano_shopping_parade_a", 1.5, "shopping_parade"), L("aro_villa_2storey_hip_a", 0.4, "villa"), L("aro_villa_2storey_hip_b", 0.4, "villa")]
GRAND = [L("kano_mosque_grand_a", 1.0, "mosque_grand"), L("kano_mosque_grand_b", 1.0, "mosque_grand"), L("rg_ngn_mosque_medium_a", 0.6, "mosque_medium"), L("rg_ngn_mosque_medium_c", 0.6, "mosque_medium")]
COMMERCIAL = [L("kano_shopping_parade_a", 1.2, "shopping_parade"), L("rg_ngn_shop_row_roadside_a", 1.5, "shop_row"), L("kano_midrise_apartments_a", 0.8, "apartments"), L("aro_villa_2storey_hip_a", 0.4, "villa"),
              L("rg_ngn_hausa_compound_b", 0.7, "hausa_compound"), L("rg_ngn_shed_market_corrugated_a", 0.6, "market_shed")]
NEIGHBOURHOOD = [L("rg_ngn_mosque_medium_a", 1.0, "mosque_medium"), L("rg_ngn_mosque_medium_b", 1.0, "mosque_medium"), L("rg_ngn_mosque_medium_c", 1.0, "mosque_medium"), L("rg_ngn_mosque_small_a", 1.2, "mosque_small"),
                 L("rg_ngn_mosque_small_b", 1.2, "mosque_small"), L("rg_ngn_mosque_traditional_a", 0.8, "mosque_traditional")]
LOCAL_MOSQUES = [L("rg_ngn_mosque_small_a", 1.0, "mosque_small"), L("rg_ngn_mosque_small_b", 1.0, "mosque_small"), L("rg_ngn_mosque_small_c", 1.0, "mosque_small"), L("rg_ngn_mosque_traditional_b", 1.0, "mosque_traditional")]
RESIDENTIAL = [L("aro_villa_2storey_hip_a", 1.2, "villa"), L("aro_villa_2storey_hip_b", 1.2, "villa"), L("rg_ngn_hausa_compound_a", 1.0, "hausa_compound"), L("rg_ngn_hausa_compound_b", 1.2, "hausa_compound"),
               L("rg_ngn_house_mudbrick_a", 1.2, "house"), L("rg_ngn_house_mudbrick_b", 1.0, "house"), L("rg_ngn_house_block_corrugated_a", 1.0, "house")]
INDUSTRIAL = [L("rg_ngn_warehouse_hall_a", 1.0, "warehouse"), L("rg_ngn_warehouse_hall_b", 1.4, "warehouse"), L("rg_ngn_warehouse_cold_a", 0.8, "warehouse")]
FLANK = [L("kano_office_block_a", 1.0, "office"), L("kano_midrise_apartments_a", 1.4, "apartments"), L("kano_shopping_parade_a", 1.2, "shopping_parade"), L("aro_villa_2storey_hip_a", 0.5, "villa"), L("aro_villa_2storey_hip_b", 0.5, "villa"),
         L("rg_ngn_hausa_compound_a", 0.5, "hausa_compound")]


def rule(rid, mix, anchors=None, every=0, corridor="", profiles="", zones="", side="both", s_from=0.0, s_to=0.0, setback=(5.0, 9.0), gap=(1.5, 6.0), block=120.0, presence=0.9, clearance=2.5, **kw):
    r = {"id": rid, "corridor": corridor, "zones": zones, "profiles": profiles, "side": side, "sFrom": s_from, "sTo": s_to, "setbackMin": setback[0], "setbackMax": setback[1], "gapMin": gap[0], "gapMax": gap[1],
         "blockLength": block, "blockMargin": 3.0, "presence": presence, "clearance": clearance, "maxHeightRange": 1.6, "yawJitterDeg": 1.5, "fixedDensity": False, "rows": 1, "rowGap": 8.0, "mix": mix, "anchors": anchors or [], "anchorEveryBlocks": every, "anchorPhase": -1}
    r.update(kw); return r


def city_e1():
    r = json.loads((ROUTES / "ng-kano-e1-streets.route.json").read_text())
    r["schemaVersion"] = 3; r["routeId"] = "ng-kano-city-e1"; r["seed"] = 8842
    r["name"] = "Kano city street corridor (densification test route; the E1 corridor with frontage rules)"
    r["propRules"] = [p for p in r["propRules"] if p["id"] not in ("shops_collector", "houses_local", "warehouses_service")]          # their buildings are replaced by the frontage rules
    r["frontage"] = [
        rule("arterial_major", MAJOR, GRAND, 2, profiles="kano_arterial", setback=(10.0, 15.0), block=150.0, presence=0.92, rows=2),
        rule("collector_main", COMMERCIAL, NEIGHBOURHOOD, 3, profiles="kano_collector", setback=(4.5, 8.0), block=110.0, presence=0.92),
        rule("cross_street_right", COMMERCIAL, LOCAL_MOSQUES, 2, corridor="j_cross", s_from=100.0, setback=(4.5, 7.0), block=90.0, presence=0.9),
        rule("cross_street_left", COMMERCIAL, LOCAL_MOSQUES, 2, corridor="j_cross_left", s_from=100.0, setback=(4.5, 7.0), block=90.0, presence=0.9),
        rule("local_street", RESIDENTIAL, LOCAL_MOSQUES, 2, corridor="j_local", s_from=60.0, setback=(3.5, 6.0), gap=(1.0, 4.0), block=80.0, presence=0.92),
        rule("service_road", INDUSTRIAL, None, 0, corridor="j_service", s_from=60.0, s_to=0.0, setback=(8.0, 14.0), gap=(3.0, 8.0), block=100.0, presence=0.9),
        rule("collector_cross_2", COMMERCIAL, LOCAL_MOSQUES, 2, corridor="j_collector", s_from=72.0, setback=(4.5, 7.0), block=90.0, presence=0.9),
        rule("terminal_access", COMMERCIAL, None, 0, corridor="j_terminal", s_from=72.0, s_to=140.0, setback=(4.5, 7.0), block=90.0, presence=0.85),
    ]
    return r


def city_f1():
    r = json.loads((ROUTES / "ng-kano-e5-f1.route.json").read_text().replace("ng-kano-e5-f1", "ng-kano-city-f1"))        # roads and rings name the main corridor by the route id
    r["routeId"] = "ng-kano-city-f1"; r["seed"] = 9317
    r["name"] = "Kano city flyover interchange (densification test route; the F1 interchange with frontage rules)"
    r["frontage"] = [
        rule("flank_urban", FLANK, GRAND, 2, zones="urban", setback=(32.0, 44.0), gap=(2.0, 8.0), block=150.0, presence=0.95, rows=3, anchorPhase=1),
        rule("flank_highway", RESIDENTIAL, LOCAL_MOSQUES, 4, zones="highway", setback=(34.0, 50.0), gap=(6.0, 20.0), block=150.0, presence=0.5),
        rule("ring_edge", FLANK, None, 0, corridor="r_highway", setback=(14.0, 22.0), gap=(2.0, 8.0), block=120.0, presence=0.9),
    ]
    return r


def catalog_entries():
    used = {}
    for r in (city_e1(), city_f1()):
        for fr in r["frontage"]:
            for l in fr["mix"] + fr["anchors"]: used[l["prop"]] = l
    out = []
    for pid in sorted(used):
        w, d, h, lods, entry = D[pid]
        cull = CULL["grand"] if "grand" in pid else CULL["medium"] if "medium" in pid else CULL["small"] if "small" in pid else CULL["trad"] if "traditional" in pid else CULL["city"] if pid.startswith("kano_") else CULL["warehouse"] if "warehouse" in pid else CULL["villa"] if "villa" in pid else CULL["house"]
        e = {"id": pid, "category": "buildings", "status": "dev",
             "description": "DEVELOPMENT PLACEHOLDER (a box of the real footprint) until the production prefab with this id is staged: the Blender-built asset of the private library (ARO-CONTENT), staged by the review build. Used only by the Kano city test routes (frontage rules). The night light heads of the private catalogue entry are not copied here: a development placeholder has no lamp head of its own.",
             "source": "dev:" + pid, "collider": "box", "heightM": round(h, 2), "widthM": round(w, 2), "depthM": round(d, 2), "cullDistance": float(cull), "lodTriangles": lods, "castShadow": True,
             "tags": sorted(set((entry or {}).get("tags", []) + ["kano_city_test"]))}
        out.append(e)
    return out


def dump(o): return json.dumps(o, indent=1, ensure_ascii=False) + "\n"


def main():
    check = "--check" in sys.argv; files = {}
    files[ROUTES / "ng-kano-city-e1.route.json"] = dump(city_e1()); files[ROUTES / "ng-kano-city-f1.route.json"] = dump(city_f1())
    cat = json.loads(CATALOG.read_text()); have = {p["id"]: p for p in cat["props"]}
    for e in catalog_entries(): have[e["id"]] = e
    order = [p["id"] for p in cat["props"]] + sorted(k for k in have if k not in {p["id"] for p in cat["props"]}); cat["props"] = [have[k] for k in order]
    new_cat = json.dumps(cat, indent=1, ensure_ascii=False)                       # the file has indent 1 and no trailing newline
    files[CATALOG] = new_cat
    bad = [str(p.relative_to(ROOT)) for p, t in files.items() if (p.read_text() if p.exists() else "") != t]
    if check:
        print("up to date" if not bad else "would change: " + ", ".join(bad)); return 1 if bad else 0
    for p, t in files.items(): p.write_text(t)
    print("wrote", ", ".join(str(p.relative_to(ROOT)) for p in files), "| catalogue entries:", len(catalog_entries())); return 0


if __name__ == "__main__":
    sys.exit(main())
