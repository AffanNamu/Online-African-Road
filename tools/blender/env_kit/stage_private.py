"""Stages the environmental kit into a checkout of the PRIVATE asset repository (ARO-CONTENT), exactly like kano_kit.stage_private does for the residential kit: Blender projects (the textures are packed in
the .blend and kept once under unity/Textures/env_kit), validation reports, FBX + GLB + manifests, the shared material kit, previews, licence rows and the registry entries (status
`blender_validation_passed` with the complete package fields and the open differences in `flags`; ground patches also record `surfaceDatumM`). Nothing is committed here; nothing is marked Unity-verified or approved.
Run from tools/blender:  PYTHONPATH=. ~/.aro/venv/bin/python -m env_kit.stage_private --content <ARO-CONTENT checkout> --previews <dir> --report <build report json>"""
from __future__ import annotations
import argparse, json, shutil, sys, time
from pathlib import Path
from aro_blender import workspace as ws
from kano_kit.stage_private import sha, LICENCE_EVIDENCE, TOOLS
from . import build_env as BE

# open visual and factual differences, written into each registry entry's `flags`
OPEN = {
    "aro_road_mud_rutted_a": "first pass: reads more orange than the references' deep red-brown mud; no water in the ruts (puddles are a separate asset, the water shader is RB-21); ruts are modelled at 0.22 m resolution; soil colour for Kano NOT established (M-15)",
    "aro_road_earth_compacted_a": "first pass: pebbles and gullies are texture and shallow geometry; colour and compaction look not verified against real photographs (M-15)",
    "aro_road_sand_loose_a": "first pass: ripples and drifts only; sand colour NOT established (M-15)",
    "aro_road_asphalt_cracked_a": "first pass: cracks are a texture; repair patches are flat rectangles; raveled edges are stair-stepped at 0.25 m cells",
    "aro_road_pothole_set_a": "first pass: potholes are real depressions but their outlines are stair-stepped at 0.17 m cells (the 8,000-triangle budget limits resolution); a decal and normal-map layer is the proper answer (RB-06); visual only unless the owner approves physical hazard sites (RB-22)",
    "aro_water_puddle_set_a": "flat translucent sheets: no reflection, ripple animation or depth; the water shader is engine work (RB-21), not done",
    "aro_water_flood_sheet_a": "a flat ragged sheet: no flow, depth query, bow wave or reflection (RB-21, RB-22), not done",
    "aro_drain_open_slab_a": "channel bottom at z = 0, road surface at 0.85 m (asset to be placed 0.85 m lower); the engine has no drain element yet (RB-05); rebar stubs rise to 0.95 m",
    "rg_ngn_stall_umbrella_a": "vernacular and goods not verified against real references (M-14); produce is low-polygon",
    "rg_ngn_stall_canopy_rough_a": "vernacular not verified (M-14); tarpaulin sag is modelled, not simulated",
    "rg_ngn_fence_woven_timber_a": "weave pattern simplified (sinusoidal rods); not verified against real fences (M-14)",
    "rg_ngn_shop_row_roadside_a": "vernacular not verified (M-14); sign bands are blank (text is a data slot, RB-07); roof corrugation is a normal map on a flat sheet; no interior beyond shelves seen through the openings",
    "rg_ngn_house_mudbrick_a": "NOT established as Hausa-traditional or as common vernacular; treated as northern Nigerian regional rural (M-14); brick courses are a texture on flat walls",
}


def main() -> int:
    ap = argparse.ArgumentParser(); ap.add_argument("--content", required=True); ap.add_argument("--previews", default=""); ap.add_argument("--report", required=True); a = ap.parse_args()
    C = Path(a.content).resolve(); W = ws.default_root(); now = time.strftime("%Y-%m-%d", time.gmtime()); rep = {r["id"]: r for r in json.loads(Path(a.report).read_text())}
    reg_path = C / "registry" / "asset_registry.json"; reg = json.loads(reg_path.read_text()); by_id = {x["id"]: x for x in reg["assets"]}; lic = (C / "LICENSES.md").read_text(); staged = []
    for aid, sp in BE.SPECS.items():
        e = by_id.get(aid)
        if e is None: print("not in the registry:", aid); return 1
        kind = sp["kind"]; vers = sorted(p.name for p in (W / "exports" / "fbx" / aid).iterdir() if p.is_dir())
        for v in vers:
            latest = v == vers[-1]; n = int(v[1:]); proj = W / "projects" / kind / aid / v; val = W / "validation" / aid / f"{v}.json"; man = json.loads((W / "exports" / "fbx" / aid / v / f"{aid}.manifest.json").read_text())
            d = C / "blender" / "projects" / kind / aid / v; d.mkdir(parents=True, exist_ok=True)
            for f in proj.iterdir():
                if f.is_file() and f.suffix.lower() != ".png": shutil.copy2(f, d / f.name)
            (C / "blender" / "validation" / aid).mkdir(parents=True, exist_ok=True); shutil.copy2(val, C / "blender" / "validation" / aid / f"{v}.json")
            u = C / "unity" / "Models" / aid / v; u.mkdir(parents=True, exist_ok=True); files = []
            for fmt in ("fbx", "glb"):
                src = W / "exports" / fmt / aid / v / f"{aid}.{fmt}"; shutil.copy2(src, u / src.name); files.append({"path": src.name, "sha256": sha(u / src.name), "bytes": (u / src.name).stat().st_size})
            if latest:
                tex = C / "unity" / "Textures" / "env_kit"; tex.mkdir(parents=True, exist_ok=True)
                for f in proj.glob("T_*.png"): shutil.copy2(f, tex / f.name)
            stats = json.loads(val.read_text())["stats"]; concepts = e["concepts"]
            manifest = {"id": aid, "version": n, "kind": kind, "files": files, "source": {"blend": f"blender/projects/{kind}/{aid}/{v}/{aid}.blend", "script": "tools/blender/env_kit (Online-African-Road, branch work/kano-residential)", "toolchain": "aro-blender on Blender " + man["blender"]},
                        "madeBy": {"human": "the owner (direction and the references " + ", ".join(concepts) + "); modelling code written by Claude", "tools": ["Blender " + man["blender"] + " (bpy)", "tools/blender/env_kit", "ChatGPT references (design reference only)"]},
                        "licence": {"kind": "owner-owned", "evidence": LICENCE_EVIDENCE.replace("concept R-006", "references " + ", ".join(concepts)), "thirdParty": list(TOOLS)}, "references": concepts, "budgets": {"lodTriangles": stats["lod_triangles"]}, "catalogEntry": man.get("catalog_entry"), "approval": None}
            (u / f"{aid}.manifest.json").write_text(json.dumps(manifest, indent=2))
            if not latest: continue
            if f"| {aid} |" not in lic: lic = lic.rstrip("\n") + f"\n| {aid} | {n} | Claude (code), owner (direction) | {', '.join(concepts)} | original geometry and procedural textures; design references ChatGPT {', '.join(concepts)}; ownership confirmed by the owner 2026-11-01 (third-party tools listed in the registry) | pending |"
            e["status"] = "blender_validation_passed"; e["blenderSource"] = manifest["source"]["blend"]; e["export"] = f"unity/Models/{aid}/{v}/{aid}.fbx"; e["validation"]["blender"] = f"blender/validation/{aid}/{v}.json"
            e["flags"] = (e.get("flags", "") + " | " if e.get("flags") else "") + "OPEN: " + OPEN.get(aid, ""); col = sp["col"]; e["dimensions"] = stats["size_m"]; e["lodTriangles"] = stats["lod_triangles"]
            e["collision"] = {"type": "box", "size": [round(col[3] - col[0], 3), round(col[4] - col[1], 3), round(col[5] - col[2], 3)]} if col else {"type": "none"}
            e["lods"] = [{"level": i, "triangles": t} for i, t in enumerate(stats["lod_triangles"])]; e["materials"] = stats["materials"]; e["textures"] = sorted(f.stem for f in proj.glob("T_*.png"))
            e["licence"] = {"kind": "owner-owned", "evidence": LICENCE_EVIDENCE.replace("concept R-006", "references " + ", ".join(concepts)), "thirdParty": list(TOOLS)}
            e["unity"] = {"imported": False, "compiled": False, "prefab": None, "inGameVerified": False, "note": "not imported into Unity yet; Blender validation only"}
            if rep.get(aid, {}).get("datum") is not None: e["surfaceDatumM"] = rep[aid]["datum"]
            pv = []
            if a.previews:
                from PIL import Image
                for k in ("iso", "front"):
                    src = Path(a.previews) / f"{aid}_{k}.png"
                    if src.is_file(): dstp = C / "previews" / aid / v; dstp.mkdir(parents=True, exist_ok=True); Image.open(src).convert("RGB").save(dstp / f"{aid}_{k}.jpg", "JPEG", quality=88, optimize=True); pv.append(f"previews/{aid}/{v}/{aid}_{k}.jpg")
            e["previews"] = pv; staged.append((aid, v, stats["lod_triangles"]))
    (C / "LICENSES.md").write_text(lic); (C / "unity" / "Materials").mkdir(parents=True, exist_ok=True); shutil.copy2(W / "exports" / "unity" / "aro_materials.json", C / "unity" / "Materials" / "aro_materials.json")
    reg_path.write_text(json.dumps(reg, indent=1, ensure_ascii=False) + "\n")
    rel = {"release": "environment_kit_e1", "date": now, "assets": [{"id": i, "version": v, "lodTriangles": t} for i, v, t in staged], "status": "blender_validation_passed (see registry)", "note": "Blender previews are previews of Blender geometry, not proof of Unity behaviour. Not imported into Unity; nothing here changes the game's physics, road generation or weather."}
    (C / "manifests").mkdir(exist_ok=True); (C / "manifests" / "environment_kit_e1.release.json").write_text(json.dumps(rel, indent=2))
    print("staged", len(staged), "assets:", ", ".join(i for i, _, _ in staged)); return 0


if __name__ == "__main__":
    sys.exit(main())
