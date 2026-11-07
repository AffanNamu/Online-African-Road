"""Builds batch E4 (corrections found in the in-game review of 2026-11-07, `env_kit.e4`) as the NEXT version of each asset id in the workspace: validation, save, FBX and GLB export, manifest; the frozen earlier versions stay.
Run from tools/blender:  PYTHONPATH=. ~/.aro-venv/bin/python -m env_kit.build_e4 [--only id,id] [--no-export] [--report file]   (needs ARO_WORKSPACE or the default workspace)"""
from __future__ import annotations
import argparse, json, sys
from pathlib import Path
from aro_blender import workspace as ws
from kano_kit import build_all as KB
from . import e4 as E4, textures as ET


def pbr():
    ET.apply_env_pbr(); E4.apply_e4_pbr()


def main() -> int:
    ap = argparse.ArgumentParser(); ap.add_argument("--only", default=""); ap.add_argument("--no-export", action="store_true"); ap.add_argument("--report", default=""); a = ap.parse_args()
    KB.ROOT = ws.ensure(ws.default_root()); report = []
    for aid, sp in E4.SPECS_E4.items():
        if a.only and aid not in a.only.split(","): continue
        r = KB.produce(sp, not a.no_export, extra_pbr=pbr); r["datum"] = E4.DATUM_E4.get(aid); report.append(r)
        print(f"{'PASS' if r['passed'] else 'FAIL'} {aid}: tris {r['stats'].get('lod_triangles')} size {r['stats'].get('size_m')} materials {len(r['stats'].get('materials', []))}" + (f" datum {r['datum']}" if r["datum"] is not None else "")
              + (f" errors {[e['message'] for e in r['errors']]}" if r["errors"] else "") + (f" inside-out {r['inside_out_shells']}" if r["inside_out_shells"] else ""))
    if a.report: Path(a.report).write_text(json.dumps(report, indent=1))
    return 0 if all(r["passed"] for r in report) else 1


if __name__ == "__main__":
    sys.exit(main())
