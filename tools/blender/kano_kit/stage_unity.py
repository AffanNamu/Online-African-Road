"""Stages the Kano residential kit for the UNITY side: the layout the game's editor code (ARO.Editor.BlenderImport.KanoReviewBuilder) reads under Assets/ThirdParty/ARO, taken from the asset workspace
(the output of kano_kit.build_all):
  Models/<id>/vNNN/<id>.fbx and <id>.manifest.json   the latest version of every asset (FBX only: no glTF importer is needed, and textures are not duplicated next to the models)
  Textures/kano_kit/T_*.png                          the shared kit textures of the latest versions, once (Unity finds a texture by name)
  Materials/aro_materials.json                       the material kit (colours, roughness, metallic, textures, cutout, alpha)
  placements/kano_residential_section_a.json         where each asset stands in the review scene (Blender axes, converted by the Unity builder)
It copies files only; nothing is committed and Assets/ThirdParty is git-ignored in the game repository. The same layout is what the private asset repository holds under unity/.
Run from the repository root:  ARO_WORKSPACE=<workspace> PYTHONPATH=tools/blender python -m kano_kit.stage_unity --out Assets/ThirdParty/ARO"""
from __future__ import annotations
import argparse, json, shutil, sys
from pathlib import Path


def latest(dirs) -> str:
    vs = sorted(p.name for p in dirs if p.is_dir() and len(p.name) == 4 and p.name[0] == "v")
    if not vs: raise SystemExit("no version folder found")
    return vs[-1]


def stage(W: Path, out: Path, placements: dict, ids=None) -> dict:
    """Copies the latest FBX, manifest and textures of every asset into `out`; returns what was staged. `ids` defaults to every asset that has an FBX export in the workspace."""
    fbx_root = W / "exports" / "fbx"; staged = {}
    for adir in sorted(fbx_root.iterdir()):
        aid = adir.name
        if ids is not None and aid not in ids: continue
        v = latest(adir.iterdir()); src = adir / v
        dst = out / "Models" / aid / v; dst.mkdir(parents=True, exist_ok=True)
        for name in (f"{aid}.fbx", f"{aid}.manifest.json"):
            if not (src / name).is_file(): raise SystemExit(f"{aid} {v}: {name} is missing in {src}")
            shutil.copy2(src / name, dst / name)
        staged[aid] = v
    tex = out / "Textures" / "kano_kit"; tex.mkdir(parents=True, exist_ok=True)
    for aid, v in staged.items():                                                           # textures live with the Blender project of the version
        for proj in (W / "projects").glob(f"*/{aid}/{v}"):
            for f in proj.glob("T_*.png"): shutil.copy2(f, tex / f.name)
    mats = W / "exports" / "unity" / "aro_materials.json"
    if not mats.is_file(): raise SystemExit(f"{mats} is missing (build_all writes it)")
    (out / "Materials").mkdir(parents=True, exist_ok=True); shutil.copy2(mats, out / "Materials" / "aro_materials.json")
    (out / "placements").mkdir(parents=True, exist_ok=True); (out / "placements" / "kano_residential_section_a.json").write_text(json.dumps(placements, indent=1))
    return staged


def main() -> int:
    ap = argparse.ArgumentParser(); ap.add_argument("--out", required=True); a = ap.parse_args()
    from aro_blender import workspace as ws
    from . import build_all
    P = build_all.build_scene()                                                             # the same arrangement the review renders use (no render is made here)
    placements = {"scene": "kano_residential_section_a", "frame": "street along +Y, right +X, metres, Blender axes", "items": P.items}
    staged = stage(ws.default_root(), Path(a.out), placements, ids=set(build_all.SPECS))
    print(f"staged {len(staged)} assets into {a.out}: " + ", ".join(f"{k} {v}" for k, v in staged.items())); print(f"{len(P.items)} placements")
    return 0 if len(staged) == len(build_all.SPECS) else 1


if __name__ == "__main__":
    sys.exit(main())
