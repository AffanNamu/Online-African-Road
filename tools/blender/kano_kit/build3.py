"""Builds residential kit v003: the same 14 assets as kano_kit.build_all (same ids, so they are NEW VERSIONS in the asset workspace, v003 there), with the owner's v003 scope of 2026-11-03:
  * villas with a richer facade (kano_kit.villa3);
  * overlapping coplanar faces separated in every asset (kano_kit.coplanar.separate);
  * flat-colour materials folded into three shared palette materials (kano_kit.palette), vegetation into one cutout atlas material each (kano_kit.veg3);
  * nothing else about the other assets changes: their geometry builders are the v002 ones.
The v002 builders (kano_kit.build_all and the modules it uses) are NOT touched, so the frozen v002 outputs stay reproducible byte for byte.
Run from tools/blender:  PYTHONPATH=. ~/.aro/venv/bin/python -m kano_kit.build3 [--only id,id] [--no-export] [--report file]"""
from __future__ import annotations
import argparse, json, sys
from pathlib import Path
from aro_blender import workspace as ws
from . import build_all, villa3, palette, coplanar, veg3

SPECS: dict = {}


def finish(part, extra=None):
    """Separate overlapping coplanar faces, then fold the flat colours into the palette materials (order matters: separation looks at geometry only)."""
    coplanar.separate(part)
    if extra: extra(part)
    palette.palettize(part); return part


def _wrap(fn, extra=None):
    def make(): return finish(fn(), extra)
    return make


def _spec(aid: str, lods: dict | None = None, extra=None, note_suffix: str = ""):
    base = build_all.SPECS[aid]
    src = lods if lods is not None else {lv: f for lv, f in base["lods"].items()}
    SPECS[aid] = dict(base, lods={lv: _wrap(f, extra) for lv, f in src.items()}, note=base["note"] + " [v003: " + (note_suffix or "overlaps separated, flat colours in shared palette materials") + "]")


VEGETATION = ("rg_ngn_tree_street_broadleaf_a", "rg_ngn_tree_street_broadleaf_b", "aro_hedge_clipped_a", "aro_shrub_ixora_red_a")


def apply_all():
    """Wires every v003 texture that a file may need: the palette materials and the vegetation atlas."""
    return palette.apply_palettes() + veg3.apply_atlas()


for _aid in build_all.SPECS:
    if _aid == "aro_villa_2storey_hip_a": _spec(_aid, {0: lambda: villa3.villa_a3(0), 1: lambda: villa3.villa_a3(1), 2: lambda: villa3.villa_a3(2)}, note_suffix="richer facade, round-arched entrance, columns, cladding panels, shared palette materials")
    elif _aid == "aro_villa_2storey_hip_b": _spec(_aid, {0: lambda: villa3.villa_b3(0), 1: lambda: villa3.villa_b3(1), 2: lambda: villa3.villa_b3(2)}, note_suffix="richer facade, arched fanlight, oculus, pilasters, cladding panels, shared palette materials")
    elif _aid in VEGETATION: _spec(_aid, extra=veg3.convert, note_suffix="overlaps separated, ONE vegetation atlas material (leaf atlas, bark and flat colours)")
    else: _spec(_aid)


FRONTAGE: dict = {}                                                 # the merged plot-frontage prototype (kano_kit.frontage) built from the same pieces with the same v003 treatment
from . import frontage as _fr
for _aid, _sp in _fr.SPECS.items(): FRONTAGE[_aid] = dict(_sp, lods={lv: _wrap(f) for lv, f in _sp["lods"].items()}, note=_sp["note"] + " [v003: overlaps separated, flat colours in shared palette materials]")


def main() -> int:
    ap = argparse.ArgumentParser(); ap.add_argument("--only", default=""); ap.add_argument("--no-export", action="store_true"); ap.add_argument("--report", default=""); ap.add_argument("--no-frontage", action="store_true"); a = ap.parse_args()
    build_all.ROOT = ws.ensure(ws.default_root()); report = []
    for aid, sp in list(SPECS.items()) + ([] if a.no_frontage else list(FRONTAGE.items())):
        if a.only and aid not in a.only.split(","): continue
        r = build_all.produce(sp, not a.no_export, extra_pbr=apply_all, leaf=False); report.append(r)
        print(f"{'PASS' if r['passed'] else 'FAIL'} {aid}: tris {r['stats'].get('lod_triangles')} size {r['stats'].get('size_m')} materials {len(r['stats'].get('materials', []))} {r['stats'].get('materials')}" + (f" errors {[e['message'] for e in r['errors']]}" if r["errors"] else "") + (f" inside-out {r['inside_out_shells']}" if r["inside_out_shells"] else ""))
    if a.report: Path(a.report).write_text(json.dumps(report, indent=1))
    return 0 if all(r["passed"] for r in report) else 1


if __name__ == "__main__":
    sys.exit(main())
