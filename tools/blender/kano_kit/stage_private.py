"""Stages the Kano residential street kit into a checkout of the PRIVATE asset repository (ARO-CONTENT), in the layout its README and validator define:
  blender/projects/<kind>/<id>/vNNN/   the .blend, project.json and the texture sources
  blender/validation/<id>/vNNN.json     the measured validation report of the toolchain
  unity/Models/<id>/vNNN/              <id>.fbx, <id>.glb and <id>.manifest.json in the repository's manifest schema (files with SHA-256, source, makers, licence evidence, references, budgets)
  unity/Textures/kano_kit/             the shared kit textures (once, not per asset)
  unity/Materials/aro_materials.json    the material kit (colours, roughness, metallic, textures, cutout) for the Unity builder
  unity/placements/kano_residential_section_a.json   the placements of the review scene (Blender axes)
  previews/kano_residential_section_a/v001/            the Blender review renders
  LICENSES.md                           one row per asset
  registry/asset_registry.json          status, source, export and validation per asset
Nothing is committed here: the caller reviews the diff. Usage:
  PYTHONPATH=tools/blender ~/.aro/venv/bin/python -m kano_kit.stage_private --content <ARO-CONTENT checkout> --placements <json> --renders <dir>
Status written to the registry is `blender_validation_passed` for an asset whose geometry and materials are modelled and whose validation report passed; nothing is marked Unity-verified or approved."""
from __future__ import annotations
import argparse, hashlib, json, shutil, sys, time
from pathlib import Path
from aro_blender import workspace as ws
from . import build_all

REF = "R-006"
# what was modelled with what, for the registry: id -> (concepts, class, engine dependencies, open visual differences against the concept)
OPEN_ISSUES = {
    "aro_villa_2storey_hip_a": "first detailed pass; the concept's villas show richer cladding, arched details and landscaping than this model; compare in game before approval",
    "aro_villa_2storey_hip_b": "second villa type with a gabled bay and garage wing: a design variation in the spirit of the concept (the concept shows two house types), not a copy of a concept house",
    "aro_gate_ornate_steel_a": "ornament is simplified (square bars, spear tips, scroll rings); the concept's gate scrollwork is richer",
    "rg_ngn_tree_street_broadleaf_a": "quality pass 3 (v002): about 1,080 small alpha-cut leaf cards from a darker procedural atlas on a branching trunk; still stylised next to the concept's trees; the species is generic (M-07); needs the cutout kit material in Unity (proved on a fixture, not yet on this asset)",
    "rg_ngn_tree_street_broadleaf_b": "as form A, different seed and proportions",
    "aro_pole_electric_concrete_span_a": "requested by the owner but NOT in the concept and no Kano reference exists (M-06): a generic concrete pole with a cross-arm, pin and spool insulators and conductors; needs review against a real reference",
    "kano_street_oneway_section_a": "delivers the kerb (aro_kerb_painted_blackwhite_a), the paver sidewalk (aro_sidewalk_paver_a) and the straight arrow (aro_marking_arrow_straight_a) of the registry as parts of one 96 m mesh; the engine cannot yet build these as road elements (RB-04, RB-12, RB-17)",
    "aro_streetlight_single_arm_a": "luminaire is a simplified cobra head",
    "aro_sign_oneway_a": "regulatory sign with real lettering; the concept's sign post is a plain grey post",
    "aro_hedge_clipped_a": "quality pass 3 (v002): about 160 small leaf cards on an opaque core, denser than v001; clipped surface still softer than the concept",
    "aro_shrub_ixora_red_a": "quality pass 3 (v002): 80 leaf cards and 46 flower clusters; species to confirm; flower clusters are small faceted blobs",
    "aro_wall_compound_panel_a": "grey cladding panel is a ledgestone texture on a flat panel; plaster tint neutralised in v002 (pale warm white, no yellow cast)",
    "aro_pier_stone_a": "stone courses are stepped boxes with a travertine texture; tint paler and less red in v002",
    "aro_lantern_wall_a": "simplified lantern",
}
PALETTE_NOTE = "v003: overlapping coplanar faces separated; flat colours folded into the shared palette materials (kano_paint, kano_metal, kano_glass: one roughness and metallic per family); geometry otherwise as v002"
OPEN_V3 = {
    "aro_villa_2storey_hip_a": "v003: richer facade (stone-clad entrance with a round-arched fanlight, round columns, pilasters, proud grey cladding panels with joints, pediments over the ground windows, wall lanterns, a warmly lit window), 7 materials instead of 11, overlaps separated; still differs from R-006 (landscaping and ornament, roof and glass simplification); compare in game before approval",
    "aro_villa_2storey_hip_b": "v003: arched fanlight over the stone-clad entrance, pilasters at the bay corners, an oculus in the gable, pediments, quoins, cladding panels on the garage wing, a warmly lit window, 7 materials instead of 10, overlaps separated; still differs from R-006; compare in game before approval",
    "rg_ngn_tree_street_broadleaf_a": "v003: bark, leaf cards in ONE vegetation atlas material (kano_veg; the v002 leaf atlas unchanged, the bark normal map not carried over); species generic (M-07); needs the cutout kit material in Unity",
    "rg_ngn_tree_street_broadleaf_b": "v003: as form A",
    "aro_hedge_clipped_a": "v003: body, leaf cards in ONE vegetation atlas material (kano_veg); needs the cutout kit material in Unity",
    "aro_shrub_ixora_red_a": "v003: body, leaf cards and flowers in ONE vegetation atlas material (kano_veg); needs the cutout kit material in Unity",
}
NEW_ASSETS = {   # not in the first registry
    "kano_street_oneway_section_a": dict(name="One-way residential street section, 96 m: asphalt with flush markings, painted kerbs, paver sidewalks, verge and plot ground", assetClass="CITY_SPECIFIC", category="environment", city="kano", region=None, library="road_asphalt"),
    "aro_pole_electric_concrete_span_a": dict(name="Concrete electricity pole with cross-arm, insulators and a 30 m span of overhead conductors", assetClass="GLOBAL", category="environment", city=None, region=None, library="road_structure_signs_lighting"),
}
CONCEPTS = {"aro_pole_electric_concrete_span_a": []}   # no concept: owner request, M-06


LICENCE_EVIDENCE = ("Original geometry and procedural textures generated by code in this project (tools/blender/kano_kit); the design follows the ChatGPT-generated concept R-006 supplied by the owner as a private design reference; "
                    "no photograph, third-party model or third-party texture is used. The owner confirmed on 2026-11-01 that the generated Blender assets are intended for this project, subject to accurate licensing records of any third-party content and tools (listed in thirdParty).")
TOOLS = ["Blender 5.2.3 (GPL; the models and textures it writes are not encumbered by it)", "numpy (BSD) and Pillow (HPND), used by the generator scripts only"]


def third_party(aid: str) -> list:
    out = list(TOOLS)
    if aid == "aro_sign_oneway_a": out.append("lettering 'One Way' converted to mesh from Blender's built-in default font (Bfont); Blender bundles Inter (SIL OFL 1.1) and DejaVu (free licence permitting use and embedding): the exact face is to be confirmed from the Blender distribution before release")
    return out


def sha(p: Path) -> str: return hashlib.sha256(p.read_bytes()).hexdigest()


def with_open(flags: str, text: str) -> str:
    """The registry `flags` with its OPEN segment REPLACED by `text` (re-staging must not stack the same segment again)."""
    base = (flags or "").split(" | OPEN: ")[0]
    if base.startswith("OPEN: "): base = ""
    return (base + " | " if base else "") + "OPEN: " + text


def main() -> int:
    ap = argparse.ArgumentParser(); ap.add_argument("--content", required=True); ap.add_argument("--placements", required=True); ap.add_argument("--renders", default=""); ap.add_argument("--previews", default=""); ap.add_argument("--only", default="")
    a = ap.parse_args(); C = Path(a.content).resolve(); W = ws.default_root(); now = time.strftime("%Y-%m-%d", time.gmtime())
    reg_path = C / "registry" / "asset_registry.json"; reg = json.loads(reg_path.read_text()); by_id = {x["id"]: x for x in reg["assets"]}
    lic = (C / "LICENSES.md").read_text(); staged = []
    for exp_dir in sorted((W / "exports" / "fbx").iterdir()):
        aid = exp_dir.name
        if aid == "bollard" or (a.only and aid not in a.only.split(",")): continue
        vers = sorted(p.name for p in exp_dir.iterdir() if p.is_dir())
        for v in vers:                                                                  # every version is staged (the validator wants v001, v002, ... without gaps); the registry points at the latest
            latest = v == vers[-1]; n = int(v[1:])
            man = json.loads((exp_dir / v / f"{aid}.manifest.json").read_text()); kind = man["kind"]
            proj = W / "projects" / kind / aid / v; val = W / "validation" / aid / f"{v}.json"
            if not proj.is_dir() or not val.is_file(): print("skip (no project or validation):", aid); continue
            # blender sources and validation
            d = C / "blender" / "projects" / kind / aid / v; d.mkdir(parents=True, exist_ok=True)
            for f in proj.iterdir():
                if f.is_file() and f.suffix.lower() != ".png": shutil.copy2(f, d / f.name)     # textures are packed in the .blend and kept once under unity/Textures/kano_kit
            (C / "blender" / "validation" / aid).mkdir(parents=True, exist_ok=True); shutil.copy2(val, C / "blender" / "validation" / aid / f"{v}.json")
            # unity models: fbx, glb, repository-schema manifest; textures once under unity/Textures/kano_kit
            u = C / "unity" / "Models" / aid / v; u.mkdir(parents=True, exist_ok=True); files = []
            for fmt in ("fbx", "glb"):
                src = W / "exports" / fmt / aid / v / f"{aid}.{fmt}"; shutil.copy2(src, u / src.name); files.append({"path": src.name, "sha256": sha(u / src.name), "bytes": (u / src.name).stat().st_size})
            tex = C / "unity" / "Textures" / "kano_kit"; tex.mkdir(parents=True, exist_ok=True)
            for f in (proj.glob("T_*.png") if latest else []):                           # the shared kit textures come from the latest version only: Unity finds textures by name
                shutil.copy2(f, tex / f.name)
            stats = json.loads(val.read_text())["stats"]; concepts = CONCEPTS.get(aid, [REF])
            manifest = {"id": aid, "version": n, "kind": kind, "files": files, "source": {"blend": f"blender/projects/{kind}/{aid}/{v}/{aid}.blend", "script": "tools/blender/kano_kit (Online-African-Road, branch work/kano-residential)", "toolchain": "aro-blender on Blender " + man["blender"]},
                        "madeBy": {"human": "the owner (direction and the concept R-006); modelling code written by Claude", "tools": ["Blender " + man["blender"] + " (bpy)", "tools/blender/kano_kit", "ChatGPT concept R-006 (design reference only)"]},
                        "licence": {"kind": "owner-owned", "evidence": LICENCE_EVIDENCE, "thirdParty": third_party(aid)},
                        "references": concepts, "budgets": {"lodTriangles": stats["lod_triangles"]}, "catalogEntry": man.get("catalog_entry"), "approval": None}
            if not latest:
                manifest["superseded"] = ("superseded by " + vers[-1] + ": the colour textures of this version were decoded twice (darker, more saturated: yellow plaster, red stone); never reviewed or approved" if n == 1 else
                                          "superseded by " + vers[-1] + " (owner-approved v003 scope: richer villa facades, fewer materials, overlaps corrected). THIS VERSION IS FROZEN: it stays as the original in-game review candidate and is never edited; its manifest hashes are in the game repository (docs/kano/KANO_RESIDENTIAL_v002.freeze.json); awaiting the owner's visual and performance approval")
            (u / f"{aid}.manifest.json").write_text(json.dumps(manifest, indent=2))
            if not latest: continue
            if f"| {aid} |" not in lic:
                lic = lic.rstrip("\n") + f"\n| {aid} | {n} | Claude (code), owner (direction) | {', '.join(concepts) or '-'} | original geometry and procedural textures; design reference ChatGPT concept R-006; ownership confirmed by the owner 2026-11-01 (third-party tools listed in the registry) | pending, {now} |\n"
            # registry
            e = by_id.get(aid)
            if e is None:
                spec = NEW_ASSETS[aid]; e = {"id": aid, "name": spec["name"], "assetClass": spec["assetClass"], "category": spec["category"], "region": spec["region"], "city": spec["city"],
                                             "library": spec["library"], "approvedCities": ["kano"] if spec["city"] else [], "concepts": concepts, "districts": ["D06"], "status": "concept_approved", "breakdownApproved": False, "blenderSource": None, "export": None,
                                             "unityPrefab": None, "placements": [], "validation": {"blender": None, "unityImport": None, "visualReview": None}, "approvedBy": None, "plannedBudget": {"lod0TrisMax": 8000 if kind == "roads" else 2500},
                                             "engineDependencies": [], "flags": ""}
                if not concepts: e["ownerRequest"] = "requested by the owner on 2026-11-01 (execution decision, section E: electricity poles and overhead wires); not in concept R-006; no Kano reference exists (M-06)"; e["flags"] = "no concept: owner-requested addition"
                reg["assets"].append(e); by_id[aid] = e
            e["status"] = "blender_validation_passed"; e["blenderSource"] = manifest["source"]["blend"]; e["export"] = f"unity/Models/{aid}/{v}/{aid}.fbx"
            e["validation"]["blender"] = f"blender/validation/{aid}/{v}.json"; e["flags"] = with_open(e.get("flags", ""), OPEN_V3.get(aid, PALETTE_NOTE) if n >= 3 else OPEN_ISSUES.get(aid, ""))
            e["placements"] = [{"scene": "kano_residential_section_a", "district": "D06", "count": sum(1 for i in json.loads(Path(a.placements).read_text())["items"] if i["asset"] == aid)}]
            col = build_all.SPECS[aid]["col"]; e["dimensions"] = stats["size_m"]; e["lodTriangles"] = stats["lod_triangles"]
            e["collision"] = {"type": "box", "size": [round(col[3] - col[0], 3), round(col[4] - col[1], 3), round(col[5] - col[2], 3)]} if col else {"type": "none"}
            e["lods"] = [{"level": i, "triangles": t} for i, t in enumerate(stats["lod_triangles"])]
            e["materials"] = stats["materials"]; e["textures"] = sorted(f.stem for f in proj.glob("T_*.png"))
            e["licence"] = {"kind": "owner-owned", "evidence": LICENCE_EVIDENCE, "thirdParty": third_party(aid)}
            e["unity"] = {"imported": False, "compiled": False, "prefab": None, "inGameVerified": False, "note": "the Unity builder compiles and its fixture test passes in the public CI; this asset has not been imported into Unity yet"}
            pv = []
            if a.previews:
                for k in ("iso", "front"):
                    src = Path(a.previews) / f"{aid}_{k}.png"
                    if src.is_file():
                        from PIL import Image
                        dstp = C / "previews" / aid / v; dstp.mkdir(parents=True, exist_ok=True); Image.open(src).convert("RGB").save(dstp / f"{aid}_{k}.jpg", "JPEG", quality=88, optimize=True); pv.append(f"previews/{aid}/{v}/{aid}_{k}.jpg")
            e["previews"] = pv
            staged.append((aid, v, stats["lod_triangles"]))
    (C / "LICENSES.md").write_text(lic)
    (C / "unity" / "Materials").mkdir(parents=True, exist_ok=True); shutil.copy2(W / "exports" / "unity" / "aro_materials.json", C / "unity" / "Materials" / "aro_materials.json")
    (C / "unity" / "placements").mkdir(parents=True, exist_ok=True); shutil.copy2(a.placements, C / "unity" / "placements" / "kano_residential_section_a.json")
    for x in reg["assets"]:                                                       # the three parts delivered inside the street section
        if x["id"] in ("aro_kerb_painted_blackwhite_a", "aro_sidewalk_paver_a", "aro_marking_arrow_straight_a") and "kano_street_oneway_section_a" not in x["flags"]:
            x["flags"] = (x["flags"] + " | " if x["flags"] else "") + "built as part of kano_street_oneway_section_a (no separate asset yet)"
    reg["note"] = reg["note"].split(" UPDATE")[0] + f" UPDATE {now}: the first residential street kit (14 assets) has Blender sources, exports and passed the toolchain's validation (`blender_validation_passed`). None has been imported into the running game by the private build, none is visually reviewed or approved."
    reg_path.write_text(json.dumps(reg, indent=1) + "\n")
    if a.renders and Path(a.renders).is_dir():
        pv = C / "previews" / "kano_residential_section_a" / staged[-1][1]; pv.mkdir(parents=True, exist_ok=True)
        from PIL import Image
        for f in Path(a.renders).glob("kano_residential_*.png"): Image.open(f).convert("RGB").save(pv / (f.stem + ".jpg"), "JPEG", quality=88, optimize=True)
    rel = {"release": "kano_residential_section_a", "date": now, "assets": [{"id": i, "version": v, "lodTriangles": t} for i, v, t in staged], "status": "blender_validation_passed (see registry)", "note": "Blender review renders are previews of Blender geometry, not proof of Unity behaviour."}
    (C / "manifests").mkdir(exist_ok=True); (C / "manifests" / "kano_residential_section_a.release.json").write_text(json.dumps(rel, indent=2))
    print("staged", len(staged), "assets:", ", ".join(i for i, _, _ in staged)); return 0


if __name__ == "__main__":
    sys.exit(main())
