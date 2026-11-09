"""Stages the transport kit for the UNITY side, next to (never over) what kano_kit.stage_unity staged: the layout the game's editor code (ARO.Editor.BlenderImport.KanoReviewBuilder) reads under Assets/ThirdParty/ARO.
  Models/<id>/vNNN/<id>.fbx and <id>.manifest.json   the latest version of every kit asset (the manifest's catalogue entry carries the light heads, text face and tags)
  Textures/kano_kit/T_*.png                          the palette textures and the shared concrete maps of the kit's versions
  Materials/aro_materials.json                       MERGED with the file already staged (a kit built in a separate workspace has only its own material list; names already staged are kept)
  placements/env_<scene>_dry.json                    the three review compositions (transport_kit.scenes)
It copies files only; nothing is committed and Assets/ThirdParty is git-ignored in the game repository.
Run from the repository root, after the other kits are staged:  ARO_WORKSPACE=<workspace of the transport build> PYTHONPATH=tools/blender python -m transport_kit.stage_unity --out Assets/ThirdParty/ARO"""
from __future__ import annotations
import argparse, json, shutil, sys
from pathlib import Path


def latest(dirs) -> str:
    vs = sorted(p.name for p in dirs if p.is_dir() and len(p.name) == 4 and p.name[0] == "v")
    if not vs: raise SystemExit("no version folder found")
    return vs[-1]


def merge_materials(existing: dict | None, new: dict) -> dict:
    """The union of two material kits by material name. A name that is already staged keeps its staged entry (the residential and environmental kits' own values); names only the transport kit knows are added."""
    if not existing: return new
    have = {m["name"] for m in existing["materials"]}
    return {**existing, "materials": existing["materials"] + [m for m in new["materials"] if m["name"] not in have]}


def stage(W: Path, out: Path, ids, scenes) -> dict:
    fbx_root = W / "exports" / "fbx"; staged = {}
    for aid in sorted(ids):
        adir = fbx_root / aid
        if not adir.is_dir(): raise SystemExit(f"{aid}: no export in {fbx_root}")
        v = latest(adir.iterdir()); src = adir / v; dst = out / "Models" / aid / v; dst.mkdir(parents=True, exist_ok=True)
        for name in (f"{aid}.fbx", f"{aid}.manifest.json"):
            if not (src / name).is_file(): raise SystemExit(f"{aid} {v}: {name} is missing in {src}")
            shutil.copy2(src / name, dst / name)
        staged[aid] = v
    tex = out / "Textures" / "kano_kit"; tex.mkdir(parents=True, exist_ok=True)
    for aid, v in staged.items():
        for proj in (W / "projects").glob(f"*/{aid}/{v}"):
            for f in proj.glob("T_*.png"): shutil.copy2(f, tex / f.name)
    mats = W / "exports" / "unity" / "aro_materials.json"
    if not mats.is_file(): raise SystemExit(f"{mats} is missing (build_transport writes it)")
    dst_m = out / "Materials" / "aro_materials.json"; dst_m.parent.mkdir(parents=True, exist_ok=True)
    merged = merge_materials(json.loads(dst_m.read_text()) if dst_m.is_file() else None, json.loads(mats.read_text())); dst_m.write_text(json.dumps(merged, indent=1))
    (out / "placements").mkdir(parents=True, exist_ok=True)
    for sc in scenes: j = sc.as_json(); (out / "placements" / f"{j['scene']}.json").write_text(json.dumps(j, indent=1))
    return staged


def main() -> int:
    ap = argparse.ArgumentParser(); ap.add_argument("--out", required=True); a = ap.parse_args()
    from aro_blender import workspace as ws
    from . import scenes as SC
    from .build_transport import load_all, SPECS
    load_all(); sc = SC.all_scenes()
    staged = stage(ws.default_root(), Path(a.out), set(SPECS), sc)
    print(f"staged {len(staged)} transport-kit assets into {a.out}; scenes: {[s.as_json()['scene'] for s in sc]} ({sum(len(s.items) for s in sc)} placements, {len(SC.asset_ids())} distinct assets used)")
    return 0 if len(staged) == len(SPECS) else 1


if __name__ == "__main__":
    sys.exit(main())
