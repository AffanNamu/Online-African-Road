"""Builds, validates, saves and exports the Kano residential street kit with the toolchain's own functions (workspace versioning, validate.run, export), then the assembled scene and the
review renders. Run:  PYTHONPATH=tools/blender ~/.aro/venv/bin/python -m kano_kit.build_all [--only id,id] [--no-export] [--scene] [--renders]  (from tools/blender)."""
from __future__ import annotations
import argparse, json, sys, time
from pathlib import Path
import bpy, mathutils
from aro_blender import workspace as ws, validate, export as exp
from . import geo, street, walls, villa, lights, veg, textures, assemble, render_ref as R

SPECS = {}


def spec(asset_id, kind, lods, col=None, textured=False, note=""):
    SPECS[asset_id] = dict(id=asset_id, kind=kind, lods=lods, col=col, textured=textured, note=note)


spec("kano_street_oneway_section_a", "roads", {0: lambda: street.build(0), 1: lambda: street.build(1)}, textured=True, note="96 m one-way street section: asphalt with flush markings, painted kerbs, paver sidewalks, verge, plot ground (concept R-006)")
spec("aro_villa_2storey_hip_a", "buildings", {0: lambda: villa.villa_a(0), 1: lambda: villa.villa_a(1), 2: lambda: villa.villa_a(2)}, col=(-6.0, -7.1, 0, 6.0, 5.0, 6.9), note="two-storey villa A (concept R-006)")
spec("aro_villa_2storey_hip_b", "buildings", {0: lambda: villa.villa_b(0), 1: lambda: villa.villa_b(1), 2: lambda: villa.villa_b(2)}, col=(-10.2, -7.7, 0, 5.5, 4.8, 6.9), note="two-storey villa B with a gabled front bay and a garage wing (concept R-006 style)")
spec("aro_wall_compound_panel_a", "props", {0: lambda: walls.wall_panel(0, assemble.PANEL_LEN)}, col=(-assemble.PANEL_LEN / 2, -0.19, 0, assemble.PANEL_LEN / 2, 0.19, 2.4), note="compound wall bay with grey cladding panel")
spec("aro_pier_stone_a", "props", {0: lambda: walls.pier(0), 1: lambda: walls.pier(1), 2: lambda: walls.pier(2)}, col=(-0.34, -0.34, 0, 0.34, 0.34, 2.75), note="stone-clad wall pier with cap")
spec("aro_gate_ornate_steel_a", "props", {0: lambda: walls.gate(0, assemble.PANEL_LEN - 0.25), 1: lambda: walls.gate(1, assemble.PANEL_LEN - 0.25), 2: lambda: walls.gate(2, assemble.PANEL_LEN - 0.25)}, note="ornate double steel gate with arched top and scroll rings")
spec("aro_lantern_wall_a", "props", {0: lambda: walls.lantern(0)}, note="wall lantern")
spec("aro_streetlight_single_arm_a", "lights", {0: lambda: lights.streetlight(0), 1: lambda: lights.streetlight(1), 2: lambda: lights.streetlight(2)}, col=(-0.2, -0.2, 0, 0.2, 0.2, 9.0), note="single-arm streetlight, 9 m")
spec("aro_pole_electric_concrete_span_a", "environments", {0: lambda: lights.pole_span(0), 1: lambda: lights.pole_span(1), 2: lambda: lights.pole_span(2)}, col=(-0.25, -0.25, 0, 0.25, 0.25, 9.5), note="concrete pole with 30 m of overhead conductors (owner-requested; generic, no reference: M-06)")
spec("aro_sign_oneway_a", "signs", {0: lambda: lights.sign_oneway(0), 1: lambda: lights.sign_oneway(1), 2: lambda: lights.sign_oneway(2)}, note="blue One Way sign with real lettering")
spec("rg_ngn_tree_street_broadleaf_a", "vegetation", {0: lambda: veg.tree(1, 8.0, 6.0, 0), 1: lambda: veg.tree(1, 8.0, 6.0, 1), 2: lambda: veg.tree(1, 8.0, 6.0, 2)}, note="street tree form A (generic broad-leaf; the real species are not established, M-07)")
spec("rg_ngn_tree_street_broadleaf_b", "vegetation", {0: lambda: veg.tree(2, 9.0, 5.2, 0), 1: lambda: veg.tree(2, 9.0, 5.2, 1), 2: lambda: veg.tree(2, 9.0, 5.2, 2)}, note="street tree form B")
spec("aro_hedge_clipped_a", "vegetation", {0: lambda: veg.hedge(0)}, note="clipped hedge module 2 m")
spec("aro_shrub_ixora_red_a", "vegetation", {0: lambda: veg.shrub_ixora(0), 1: lambda: veg.shrub_ixora(1)}, note="red-flowering ixora shrub (species to confirm)")

ROOT = None


def produce(sp: dict, do_export: bool = True) -> dict:
    a = geo.Asset(sp["id"], sp["kind"])
    if sp["textured"]: textures.paver_material(); textures.asphalt_material()
    if sp["id"].startswith(("rg_ngn_tree", "aro_hedge", "aro_shrub")): textures.leaf_material()
    for lv, fn in sorted(sp["lods"].items()): a.lod(lv, fn())
    if sp["col"]: a.collision(*sp["col"])
    bad = {o.name: [round(v, 3) for v in geo.shell_volumes(o) if v <= 0] for o in a.lods.values()}
    bad = {k: v for k, v in bad.items() if v}
    res = validate.run(bpy, mathutils, sp["kind"], True, None, sp["id"]); out = {"id": sp["id"], "kind": sp["kind"], "passed": res["passed"] and not bad, "inside_out_shells": bad, "stats": res["stats"],
                                                                               "errors": [f for f in res["findings"] if f["severity"] == "error"], "warnings": [f for f in res["findings"] if f["severity"] == "warning"]}
    if not out["passed"] or not do_export: return out
    v, d = ws.new_version_dir(ROOT, sp["kind"], sp["id"]); blend = d / f"{sp['id']}.blend"
    textures.write_images(d)                                                                     # texture sources next to the project, and where the FBX exporter copies them from
    bpy.ops.wm.save_as_mainfile(filepath=str(blend), compress=True)
    (d / "project.json").write_text(json.dumps({"asset": sp["id"], "kind": sp["kind"], "version": f"v{v:03d}", "blender": bpy.app.version_string, "notes": sp["note"], "builder": "tools/blender/kano_kit",
                                                "saved_at": time.strftime("%Y-%m-%dT%H:%M:%SZ", time.gmtime())}, indent=2))
    files = []
    for fmt in ("fbx", "glb"):
        o = ws.export_dir(ROOT, fmt, sp["id"], v); files.append(exp.export(bpy, o, sp["id"], fmt))
    man = exp.write_manifest(files[0].parent, sp["id"], v, sp["kind"], files, res, bpy.app.version_string, str(blend.relative_to(ROOT)))
    vd = ROOT / "validation" / sp["id"]; vd.mkdir(parents=True, exist_ok=True); (vd / f"v{v:03d}.json").write_text(json.dumps(res, indent=2))
    from aro_blender import materials as AMat
    (ROOT / "exports" / "unity").mkdir(parents=True, exist_ok=True); (ROOT / "exports" / "unity" / "aro_materials.json").write_text(json.dumps(AMat.kit_json(), indent=1))
    out.update(version=f"v{v:03d}", blend=str(blend.relative_to(ROOT)), exports=[str(f.relative_to(ROOT)) for f in files]); return out


def build_scene(save_to=None):
    geo.reset_scene(); textures.paver_material(); textures.asphalt_material(); textures.leaf_material(); P = assemble.Placements()
    g = geo.Part("ground"); g.face_n([(-1500, -300, -0.03), (1500, -300, -0.03), (1500, 1500, -0.03), (-1500, 1500, -0.03)], "grass_green", (0, 0, 1)); g.to_object("review_ground")   # review scene only: a plain ground so the street does not float
    assemble.build_section(P, 0.0)
    for i, yc in enumerate((96.0, 192.0, 288.0)): assemble.build_section(P, yc, far=True)
    return P


def main():
    global ROOT
    ap = argparse.ArgumentParser(); ap.add_argument("--only", default=""); ap.add_argument("--no-export", action="store_true"); ap.add_argument("--scene", action="store_true"); ap.add_argument("--report", default="")
    a = ap.parse_args(); ROOT = ws.ensure(ws.default_root()); report = []
    for aid, sp in SPECS.items():
        if a.only and aid not in a.only.split(","): continue
        r = produce(sp, not a.no_export); report.append(r)
        print(f"{'PASS' if r['passed'] else 'FAIL'} {aid}: tris {r['stats'].get('lod_triangles')} size {r['stats'].get('size_m')} materials {len(r['stats'].get('materials', []))}" + (f" errors {[e['message'] for e in r['errors']]}" if r["errors"] else "") + (f" inside-out {r['inside_out_shells']}" if r["inside_out_shells"] else ""))
    if a.report: Path(a.report).write_text(json.dumps(report, indent=1))


if __name__ == "__main__":
    main()
