"""Stages batch E4 (the six corrections of the in-game review, `env_kit.e4`) into a checkout of the unified library (ARO-CONTENT). ONLY the six ids and ONLY the version E4 produced are touched; nothing else is re-staged
(the older stage tool re-stages the whole kit and resets every registry status, which would erase the Unity import evidence of the unchanged assets). Rules, enforced here:
  * append-only: an existing file is never overwritten with different bytes (the run stops and says which file);
  * the registry entry of each asset moves to the new version with status `blender_validation_passed` and no Unity evidence (the old version's import record stays in unity/import_reports and in the release snapshots);
  * the three Hausa assets get `documentaryReferences` (R-024 to R-029), never a concept id; nothing is marked reviewed or approved;
  * the combined material kit gets the three new materials and every existing definition must be identical.
Run from tools/blender:  PYTHONPATH=. ~/.aro/venv/bin/python -m env_kit.stage_e4 --content <ARO-CONTENT checkout> --game-docs <game repository docs/kano> [--previews DIR]"""
from __future__ import annotations
import argparse, json, shutil, sys, time
from pathlib import Path
from aro_blender import workspace as ws
from kano_kit.stage_private import sha, LICENCE_EVIDENCE, TOOLS
from .stage_private import with_open
from . import e4 as E4

DOCUMENTARY = ["R-024", "R-025", "R-026", "R-027", "R-028", "R-029"]
OPEN4 = {
    "aro_road_pothole_set_a": "E4 (2026-11-08): outline cut exactly along the contour, steep wall, flat floor, dark gravel base (M_pothole_base, colour a first-pass estimate); still static geometry (a decal and normal-map layer is the proper answer, RB-06); visual only unless the owner approves physical hazard sites (RB-22); in-game review pending",
    "aro_road_mud_rutted_a": "E4 (2026-11-08): flat-floored, steep-walled ruts on a grid that follows the wandering ruts; mud colour for Kano NOT established (M-15); no water in the ruts (puddles are a separate asset, the water shader is RB-21); in-game review pending",
    "aro_water_flood_sheet_a": "E4 (2026-11-08): smooth shoreline, dark deep centre, lighter shallow rim, wet mud fringe; still a static translucent mesh: no flow, depth query, bow wave or reflection (RB-21, RB-22); in-game review pending",
    "rg_ngn_hausa_compound_a": "E4 (2026-11-08): walls finished in red-pigmented earth (kasa) render per documentary sources R-024 and R-027 (colour values NOT established); gap M-01 only partly addressed (documentary sources, no licensed photograph or measured drawing of a Kano compound); every proportion is a design choice; in-game review pending",
    "rg_ngn_hausa_compound_b": "E4 (2026-11-08): earth (kasa) render per R-024 and R-027 (colour values NOT established); design VARIATION of compound A; M-01 only partly addressed; every proportion is a design choice; in-game review pending",
    "rg_ngn_hausa_wall_gate_set_a": "E4 (2026-11-08): earth (kasa) render per R-024 and R-027 (colour values NOT established); street frontage is a design interpretation; M-01 only partly addressed; in-game review pending",
}


def put(src: Path, dst: Path, log: list):
    """Copies src to dst; refuses to overwrite a file that has other bytes (append-only)."""
    dst.parent.mkdir(parents=True, exist_ok=True)
    if dst.exists():
        if dst.read_bytes() != src.read_bytes(): raise SystemExit(f"REFUSED: {dst} exists with different bytes (append-only library)")
        return
    shutil.copy2(src, dst); log.append(str(dst))


def main() -> int:
    ap = argparse.ArgumentParser(); ap.add_argument("--content", required=True); ap.add_argument("--game-docs", required=True); ap.add_argument("--previews", default=""); a = ap.parse_args()
    C = Path(a.content).resolve(); G = Path(a.game_docs).resolve(); W = ws.default_root(); now = time.strftime("%Y-%m-%d", time.gmtime()); added: list = []
    reg_path = C / "registry" / "asset_registry.json"; reg = json.loads(reg_path.read_text()); by_id = {x["id"]: x for x in reg["assets"]}; lic = (C / "LICENSES.md").read_text(); staged = []
    for aid, sp in E4.SPECS_E4.items():
        e = by_id[aid]; kind = sp["kind"]; vers = sorted(p.name for p in (W / "exports" / "fbx" / aid).iterdir() if p.is_dir()); v = vers[-1]; n = int(v[1:])
        proj = W / "projects" / kind / aid / v; val = W / "validation" / aid / f"{v}.json"; man = json.loads((W / "exports" / "fbx" / aid / v / f"{aid}.manifest.json").read_text()); stats = json.loads(val.read_text())["stats"]
        if (C / "unity" / "Models" / aid / v).exists(): raise SystemExit(f"REFUSED: {aid} {v} is already in the library")
        for f in proj.iterdir():
            if f.is_file() and f.suffix.lower() != ".png": put(f, C / "blender" / "projects" / kind / aid / v / f.name, added)
        put(val, C / "blender" / "validation" / aid / f"{v}.json", added); u = C / "unity" / "Models" / aid / v; files = []
        for fmt in ("fbx", "glb"):
            src = W / "exports" / fmt / aid / v / f"{aid}.{fmt}"; put(src, u / src.name, added); files.append({"path": src.name, "sha256": sha(u / src.name), "bytes": (u / src.name).stat().st_size})
        put(W / "exports" / "fbx" / aid / v / f"{aid}.manifest.json", C / "blender" / "export_manifests" / aid / v / f"{aid}.manifest.json", added)       # the original export manifest, byte for byte
        for f in proj.glob("T_*.png"): put(f, C / "unity" / "Textures" / "env_kit" / f.name, added)
        concepts = e.get("concepts") or []; refs = concepts + (DOCUMENTARY if aid.startswith("rg_ngn_hausa") else [])
        manifest = {"id": aid, "version": n, "kind": kind, "files": files, "source": {"blend": f"blender/projects/{kind}/{aid}/{v}/{aid}.blend", "script": "tools/blender/env_kit/e4.py (Online-African-Road, branch work/kano-residential)", "toolchain": "aro-blender on Blender " + man["blender"]},
                    "madeBy": {"human": "the owner (direction" + (" and the references " + ", ".join(concepts) if concepts else "; no concept image exists for this asset, documentary sources " + ", ".join(refs)) + "); modelling code written by Claude", "tools": ["Blender " + man["blender"] + " (bpy)", "tools/blender/env_kit", "ChatGPT references (design reference only)"]},
                    "licence": {"kind": "owner-owned", "evidence": LICENCE_EVIDENCE.replace("concept R-006", "references " + ", ".join(refs)), "thirdParty": list(TOOLS)}, "references": refs, "budgets": {"lodTriangles": stats["lod_triangles"]}, "catalogEntry": man.get("catalog_entry"), "approval": None}
        (u / f"{aid}.manifest.json").write_text(json.dumps(manifest, indent=2)); added.append(str(u / f"{aid}.manifest.json"))
        rows = []
        for line in lic.split("\n"):
            if line.startswith(f"| {aid} |"):
                vs = sorted(int(d.name[1:]) for d in (C / "unity" / "Models" / aid).iterdir() if d.is_dir()); parts = line.split(" | "); parts[1] = ", ".join(map(str, vs)); line = " | ".join(parts)
            rows.append(line)
        lic = "\n".join(rows)
        e["status"] = "blender_validation_passed"; e["blenderSource"] = manifest["source"]["blend"]; e["export"] = f"unity/Models/{aid}/{v}/{aid}.fbx"; e["validation"] = {"blender": f"blender/validation/{aid}/{v}.json", "unityImport": None, "visualReview": None}
        e["flags"] = with_open(e.get("flags", ""), OPEN4[aid]); e["dimensions"] = stats["size_m"]; e["lodTriangles"] = stats["lod_triangles"]; e["lods"] = [{"level": i, "triangles": t} for i, t in enumerate(stats["lod_triangles"])]
        e["materials"] = stats["materials"]; e["textures"] = sorted(f.stem for f in proj.glob("T_*.png")); e["unity"] = {"imported": False, "compiled": False, "prefab": None, "inGameVerified": False, "note": f"{v} (batch E4) is not imported into Unity yet; Blender validation only"}
        if aid.startswith("rg_ngn_hausa"): e["documentaryReferences"] = DOCUMENTARY
        if E4.DATUM_E4.get(aid) is not None or aid in ("aro_road_pothole_set_a", "aro_road_mud_rutted_a", "aro_water_flood_sheet_a"):
            part = sp["lods"][0](); d_ = E4.DATUM_E4.get(aid); part.bm.free()
            if d_ is not None: e["surfaceDatumM"] = d_
        pv = []
        if a.previews:
            from PIL import Image
            for k in ("iso", "front"):
                src = Path(a.previews) / f"{aid}_{k}.png"
                if src.is_file(): dstp = C / "previews" / aid / v; dstp.mkdir(parents=True, exist_ok=True); Image.open(src).convert("RGB").save(dstp / f"{aid}_{k}.jpg", "JPEG", quality=88, optimize=True); pv.append(f"previews/{aid}/{v}/{aid}_{k}.jpg")
        e["previews"] = e.get("previews", []) + pv; staged.append((aid, v, stats["lod_triangles"]))
    # the combined material kit: add the E4 materials, every existing definition must be identical
    mk = C / "unity" / "Materials" / "aro_materials.json"; lib = json.loads(mk.read_text()); new = json.loads((W / "exports" / "unity" / "aro_materials.json").read_text()); have = {m["name"]: m for m in lib["materials"]}; add = []
    for m in new["materials"]:
        if m["name"] in have:
            if have[m["name"]] != m: raise SystemExit(f"REFUSED: material {m['name']} differs from the library's definition")
        else: add.append(m)
    lib["materials"] = sorted(lib["materials"] + add, key=lambda m: m["name"]); mk.write_text(json.dumps(lib, indent=1)); print("materials added:", [m["name"] for m in add])
    (C / "LICENSES.md").write_text(lic); reg_path.write_text(json.dumps(reg, indent=1, ensure_ascii=False) + "\n")
    for src, dst in ((G / "ENVIRONMENT_KIT_E4.freeze.json", "environment_kit_e4.freeze.json"), (G / "ENVIRONMENT_KIT_E4.export_verification.json", "environment_kit_e4.export_verification.json")):
        put(src, C / "manifests" / dst, added)
    rel = {"release": "environment_kit_e4", "date": now, "assets": [{"id": i, "version": v, "lodTriangles": t} for i, v, t in staged], "status": "blender_validation_passed (see registry); not imported into Unity until a review run says so",
           "note": "Corrections found in the in-game review of 2026-11-07, new versions of existing ids (docs/IN_GAME_REVIEW_2026-11-07.md of the game repository). Nothing is reviewed or approved."}
    (C / "manifests" / "environment_kit_e4.release.json").write_text(json.dumps(rel, indent=2)); added.append("manifests/environment_kit_e4.release.json")
    ref_src = G / "REFERENCE_REGISTER.md"; ref_dst = C / "references" / "REFERENCE_REGISTER.md"
    if ref_src.is_file(): shutil.copy2(ref_src, ref_dst); added.append(str(ref_dst))                      # the register is a mirror of the game repository's document; R-024 to R-029 must be in it (validator rule 7)
    print(f"staged {len(staged)} assets: " + ", ".join(f"{i} {v}" for i, v, _ in staged) + f"; {len(added)} files added or updated"); return 0


if __name__ == "__main__":
    sys.exit(main())
