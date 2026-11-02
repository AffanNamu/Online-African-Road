"""Builds, validates, saves and exports the environmental kit with the toolchain's own functions (the same path as kano_kit.build_all: workspace versioning, validate.run, FBX and GLB export, manifest).
Run from tools/blender:  PYTHONPATH=. ~/.aro/venv/bin/python -m env_kit.build_env [--only id,id] [--no-export] [--report file]"""
from __future__ import annotations
import argparse, json, sys
from pathlib import Path
from aro_blender import workspace as ws
from kano_kit import build_all as KB, coplanar as CP
from . import surfaces as S, roadside as R, buildings as B, hausa as H, veg_dry as VD, veg_wet as VW, commerce as CM, rural as RU, textures as ET

SPECS: dict = {}
DATUM: dict = {}                       # asset id -> metres the nominal surface sits above the asset's z = 0 (ground patches only)


def _separated(f):
    """The builder's part with its overlapping coplanar faces separated by 3 mm (kano_kit.coplanar.separate)."""
    def make(lv):
        part = f(lv); CP.separate(part); return part
    return make


def spec(asset_id, kind, lods, col=None, note="", separate=False):
    """`separate`: resolve overlapping coplanar faces in every LOD (batch E3 and the corrected versions of E1/E2; the frozen v001 builds did not)."""
    SPECS[asset_id] = dict(id=asset_id, kind=kind, lods={lv: (lambda lv=lv, f=(_separated(f) if separate else f): f(lv)) for lv, f in lods.items()}, col=col, textured=False, note=note)   # every builder takes the LOD level


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
spec("aro_water_puddle_set_a", "environments", {0: patch("aro_water_puddle_set_a", S.puddle_set)}, note="six puddles, brown over earth and grey over asphalt (R-018, R-020, R-021); the water shader and reflections are engine work (RB-21)", separate=True)
spec("aro_water_flood_sheet_a", "environments", {0: patch("aro_water_flood_sheet_a", S.flood_sheet), 1: patch("aro_water_flood_sheet_a", S.flood_sheet)}, note="ragged sheet of turbid flood water 14 x 8 m (R-019); the water shader, depth query and flow are engine work (RB-21, RB-22)")
spec("aro_drain_open_slab_a", "environments", {0: R.drain_open_slab, 1: R.drain_open_slab}, note="6 m of concrete roadside drain with slab covers, one cracked and one missing (R-016, R-019); channel bottom at z = 0, road surface at z = 0.85", separate=True)
spec("rg_ngn_stall_umbrella_a", "props", {0: R.stall_umbrella, 1: R.stall_umbrella}, note="market table stall with produce under a 2.6 m red and white umbrella (R-016 to R-019)")
spec("rg_ngn_stall_canopy_rough_a", "props", {0: R.stall_canopy_rough, 1: R.stall_canopy_rough}, note="rough roadside stall: timber poles, olive tarpaulin, counter, sacks, crates (R-020, R-021)")
spec("rg_ngn_fence_woven_timber_a", "props", {0: R.fence_woven_timber, 1: R.fence_woven_timber}, note="3 m woven timber fence module (R-020)")
spec("rg_ngn_shop_row_roadside_a", "buildings", {0: B.shop_row_roadside, 1: B.shop_row_roadside}, col=(-7.9, -3.25, 0, 7.9, 3.5, 3.4), note="roadside row of three shops with awning, roller shutters in three states, sign bands (R-016 to R-019)", separate=True)
spec("rg_ngn_house_mudbrick_a", "buildings", {0: B.mudbrick_house, 1: B.mudbrick_house}, col=(-4.5, -3.8, 0, 4.5, 3.8, 4.4), note="mud-brick house with rusted corrugated gable roof (R-020, R-021)", separate=True)

spec("rg_ngn_hausa_compound_a", "buildings", {0: H.hausa_compound_a, 1: H.hausa_compound_a}, note="Hausa-style walled compound 20 x 16 m built from the documented qualitative features (zaure, parapets, zankwaye, relief); proportions are design choices, NOT verified (M-01)", separate=True)
spec("rg_ngn_tree_large_shade_a", "vegetation", {0: VD.tree_large_shade, 1: VD.tree_large_shade, 2: VD.tree_large_shade}, note="large spreading shade tree with sparse dusty dry-season foliage (R-016 to R-021 show such trees; species NOT established, M-07, M-17)")
spec("rg_ngn_grass_tall_verge_a", "vegetation", {0: VD.grass_clump}, note="clump of dry grass blade cards (R-016, R-020, R-021; seasonal appearance not established, M-17)")


# ---------------------------------------------------------------------------------------------------------------------------------- batch E3 (2026-11-02)
spec("rg_ngn_shed_market_corrugated_a", "buildings", {0: CM.market_shed, 1: CM.market_shed}, note="open market shed 14.4 x 8.4 m: slab, 15 timber posts, corrugated gable roof on rafters and purlins, six table stalls with produce and sacks (R-021; the real Kano markets have no reference, M-04)", separate=True)
spec("rg_ngn_kiosk_lockup_a", "props", {0: CM.kiosk_lockup, 1: CM.kiosk_lockup}, col=(-1.55, -2.0, 0, 1.55, 1.4, 2.65), note="lock-up kiosk 2.4 m square: block walls, serving hatch with a raised steel shutter, counter, blank sign band, corrugated roof (one unit of the shop type in R-016 to R-021)", separate=True)
spec("rg_ngn_house_mudbrick_b", "buildings", {0: RU.mudbrick_house_b, 1: RU.mudbrick_house_b}, col=(-5.4, -4.6, 0, 5.4, 3.0, 4.0), note="mud-brick house 10 x 5 m with a lean-to veranda on timber posts, two doors, rusted corrugated gable roof (R-020, R-021)", separate=True)
spec("rg_ngn_house_block_corrugated_a", "buildings", {0: RU.block_house_corrugated, 1: RU.block_house_corrugated}, col=(-4.9, -3.6, 0, 4.9, 3.6, 4.3), note="plastered block house 9 x 6 m with a hipped corrugated roof and barred windows (R-016, R-018, R-021)", separate=True)
spec("rg_ngn_hausa_compound_b", "buildings", {0: H.hausa_compound_b, 1: H.hausa_compound_b}, note="second Hausa-style compound type 14 x 12 m, zaure at the right-hand end of the frontage, built from the documented qualitative features; proportions are design choices, NOT verified (M-01)", separate=True)
spec("rg_ngn_hausa_wall_gate_set_a", "buildings", {0: H.hausa_wall_gate_set_a, 1: H.hausa_wall_gate_set_a}, note="street frontage of a Hausa-style compound, 16 m: two wall runs with zankwaye and relief either side of an entrance block with a recessed doorway; design interpretation, NOT verified (M-01)", separate=True)
spec("rg_ngn_banana_plant_a", "vegetation", {0: VW.banana_plant, 1: VW.banana_plant, 2: VW.banana_plant}, note="clump of three banana plants with arching leaf blades (torn edges) and a bunch of green fruit (R-020; presence in Kano not established, M-17)", separate=True)
spec("rg_ngn_bougainvillea_wall_a", "vegetation", {0: VW.bougainvillea_wall, 1: VW.bougainvillea_wall, 2: VW.bougainvillea_wall}, note="4 m of plastered compound wall with magenta bougainvillea over the top and down the street face (R-016)", separate=True)
spec("rg_ngn_tree_large_shade_wet_a", "vegetation", {0: VW.tree_large_shade_wet, 1: VW.tree_large_shade_wet, 2: VW.tree_large_shade_wet}, note="wet-season variant of the large shade tree: same trunk and branches, dense saturated green crown (R-019, R-020; species and seasonal colour NOT established, M-07, M-17)", separate=True)
spec("rg_ngn_grass_tall_wet_a", "vegetation", {0: VW.grass_tall_wet}, note="clump of green grass after rain, 30 blade cards (R-019, R-020; seasonal appearance NOT established, M-17)", separate=True)


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
