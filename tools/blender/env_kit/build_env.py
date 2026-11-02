"""Builds, validates, saves and exports the environmental kit with the toolchain's own functions (the same path as kano_kit.build_all: workspace versioning, validate.run, FBX and GLB export, manifest).
Run from tools/blender:  PYTHONPATH=. ~/.aro/venv/bin/python -m env_kit.build_env [--only id,id] [--no-export] [--report file]"""
from __future__ import annotations
import argparse, json, sys
from pathlib import Path
from aro_blender import workspace as ws
from kano_kit import build_all as KB
from . import surfaces as S, roadside as R, buildings as B, textures as ET

SPECS: dict = {}
DATUM: dict = {}                       # asset id -> metres the nominal surface sits above the asset's z = 0 (ground patches only)


def spec(asset_id, kind, lods, col=None, note=""):
    SPECS[asset_id] = dict(id=asset_id, kind=kind, lods={lv: (lambda lv=lv, f=f: f(lv)) for lv, f in lods.items()}, col=col, textured=False, note=note)   # every builder takes the LOD level


def patch(asset_id, fn):
    """A ground patch: remember its datum when LOD0 is built."""
    def make(lod):
        part = fn(lod)
        if lod == 0: DATUM[asset_id] = round(part.datum, 4)
        return part
    return make


spec("aro_road_mud_rutted_a", "roads", {0: patch("aro_road_mud_rutted_a", S.mud_rutted), 1: patch("aro_road_mud_rutted_a", S.mud_rutted)}, note="rutted clay mud road patch 8 x 24 m (references R-020, R-021); height field with four wandering wheel ruts, ridges and clods; the nominal surface plane sits at the datum above z = 0")
spec("aro_road_earth_compacted_a", "roads", {0: patch("aro_road_earth_compacted_a", S.earth_compacted), 1: patch("aro_road_earth_compacted_a", S.earth_compacted)}, note="compacted laterite earth road patch 8 x 24 m (R-016, R-020); crown, shallow tracks, potholes, runoff gullies, edge dust")
spec("aro_road_sand_loose_a", "roads", {0: patch("aro_road_sand_loose_a", S.sand_loose), 1: patch("aro_road_sand_loose_a", S.sand_loose)}, note="loose sandy road patch 8 x 24 m (R-016, R-017); wind ripples, soft tracks, drifted edges")
spec("aro_road_asphalt_cracked_a", "roads", {0: patch("aro_road_asphalt_cracked_a", S.asphalt_cracked), 1: patch("aro_road_asphalt_cracked_a", S.asphalt_cracked)}, note="old cracked asphalt patch 7 x 24 m (R-016, R-017); crack texture, four repair patches, raveled edges exposing earth")
spec("aro_road_pothole_set_a", "roads", {0: patch("aro_road_pothole_set_a", S.pothole_set), 1: patch("aro_road_pothole_set_a", S.pothole_set)}, note="nine potholes 7 to 20 cm deep on asphalt, 7 x 12 m (R-016); visual only unless the owner approves physical hazard sites")
spec("aro_water_puddle_set_a", "environments", {0: patch("aro_water_puddle_set_a", S.puddle_set)}, note="six puddles, brown over earth and grey over asphalt (R-018, R-020, R-021); the water shader and reflections are engine work (RB-21)")
spec("aro_water_flood_sheet_a", "environments", {0: patch("aro_water_flood_sheet_a", S.flood_sheet), 1: patch("aro_water_flood_sheet_a", S.flood_sheet)}, note="ragged sheet of turbid flood water 14 x 8 m (R-019); the water shader, depth query and flow are engine work (RB-21, RB-22)")
spec("aro_drain_open_slab_a", "environments", {0: R.drain_open_slab, 1: R.drain_open_slab}, note="6 m of concrete roadside drain with slab covers, one cracked and one missing (R-016, R-019); channel bottom at z = 0, road surface at z = 0.85")
spec("rg_ngn_stall_umbrella_a", "props", {0: R.stall_umbrella, 1: R.stall_umbrella}, note="market table stall with produce under a 2.6 m red and white umbrella (R-016 to R-019)")
spec("rg_ngn_stall_canopy_rough_a", "props", {0: R.stall_canopy_rough, 1: R.stall_canopy_rough}, note="rough roadside stall: timber poles, olive tarpaulin, counter, sacks, crates (R-020, R-021)")
spec("rg_ngn_fence_woven_timber_a", "props", {0: R.fence_woven_timber, 1: R.fence_woven_timber}, note="3 m woven timber fence module (R-020)")
spec("rg_ngn_shop_row_roadside_a", "buildings", {0: B.shop_row_roadside, 1: B.shop_row_roadside}, col=(-7.9, -3.25, 0, 7.9, 3.5, 3.4), note="roadside row of three shops with awning, roller shutters in three states, sign bands (R-016 to R-019)")
spec("rg_ngn_house_mudbrick_a", "buildings", {0: B.mudbrick_house, 1: B.mudbrick_house}, col=(-4.5, -3.8, 0, 4.5, 3.8, 4.4), note="mud-brick house with rusted corrugated gable roof (R-020, R-021)")


def main() -> int:
    ap = argparse.ArgumentParser(); ap.add_argument("--only", default=""); ap.add_argument("--no-export", action="store_true"); ap.add_argument("--report", default=""); a = ap.parse_args()
    KB.ROOT = ws.ensure(ws.default_root()); report = []
    for aid, sp in SPECS.items():
        if a.only and aid not in a.only.split(","): continue
        r = KB.produce(sp, not a.no_export, extra_pbr=ET.apply_env_pbr); r["datum"] = DATUM.get(aid); report.append(r)
        print(f"{'PASS' if r['passed'] else 'FAIL'} {aid}: tris {r['stats'].get('lod_triangles')} size {r['stats'].get('size_m')} materials {len(r['stats'].get('materials', []))}" + (f" datum {r['datum']}" if r["datum"] is not None else "")
              + (f" errors {[e['message'] for e in r['errors']]}" if r["errors"] else "") + (f" inside-out {r['inside_out_shells']}" if r["inside_out_shells"] else ""))
    if a.report: Path(a.report).write_text(json.dumps(report, indent=1))
    return 0 if all(r["passed"] for r in report) else 1


if __name__ == "__main__":
    sys.exit(main())
