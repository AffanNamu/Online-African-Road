"""Builds the transport kit (families A logistics, B bus terminals, C road furniture): every asset is built with the toolchain's own geometry, validated by `aro_blender.validate` (measured, not claimed), saved as the
NEXT version in the workspace, exported to FBX and GLB with a manifest whose catalogue entry also carries the light heads, text face and tags of `transport_kit.common.EXTRAS`.
Run from tools/blender:  PYTHONPATH=. ~/.aro/venv/bin/python -m transport_kit.build_transport [--only id,id] [--family A|B|C] [--no-export] [--report file] [--catalog file]"""
from __future__ import annotations
import argparse, json, sys, time
from pathlib import Path
import bpy, bmesh, mathutils
from aro_blender import workspace as ws, validate, export as exp, materials as AMat
from kano_kit import geo, textures
from . import common as C, palette as PAL, surface as SB

from .registry import SPECS, spec  # noqa: F401


def _collision(asset, box, suffix):
    p = geo.Part("col"); p.box(*box, "concrete_dark"); bmesh.ops.remove_doubles(p.bm, verts=p.bm.verts, dist=1e-5); name = f"col_{asset.id}{suffix}"
    mesh = bpy.data.meshes.new(name); p.bm.to_mesh(mesh); p.bm.free(); o = bpy.data.objects.new(name, mesh); bpy.context.scene.collection.objects.link(o); o.parent = asset.root; o.display_type = "WIRE"


def build_scene(sp, version=None):
    """`version`: the version folder this build will be saved in (names of the baked materials and textures carry it); default the spec's design version."""
    a = geo.Asset(sp["id"], sp["kind"]); textures.paver_material(); textures.asphalt_material(); a.version = int(version or sp.get("version", 1))
    a.bake = {}
    for lv, fn in sorted(sp["lods"].items()):
        part = fn()
        if sp.get("surface"): a.bake[lv] = SB.apply(part, sp["id"], lv, sp["surface"][min(lv, len(sp["surface"]) - 1)], sp.get("ground", True), a.version, sp.get("bake_rules", 1))      # painted atlas: one material per LOD (transport_kit.surface)
        else: PAL.palettize(part)                                               # flat colours go to the shared palette materials (at most 4 materials per asset)
        a.lod(lv, part)
    textures.apply_pbr(); PAL.apply_palettes()
    for i, box in enumerate(sp["cols"]): _collision(a, box, "" if i == 0 else f"_{chr(ord('a') + i)}")
    return a


def catalog_entry(sp, res):
    e = dict(res.get("catalog_entry") or {}); x = C.EXTRAS.get(sp["id"], {})
    e["collider"] = "box" if sp["cols"] else "none"
    e["tags"] = sorted(set(e.get("tags", [])) | set(x.get("tags", [])))
    e["description"] = f"Blender-authored transport kit, family {x.get('family', '?')}, class {x.get('class', '?')}: {sp['note']} References: {', '.join(x.get('ref', []))}. No brand, plate or logo."
    if "light" in x: e["light"] = x["light"]
    if "label" in x: e["label"] = x["label"]
    return e


def next_version(root: Path, sp: dict) -> int:
    """The version folder the next save of this asset gets: after the workspace's last one, and never below the spec's design version (so a fresh workspace, as in the CI review, labels a v002 design v002)."""
    return max(max(ws.versions(root, sp["kind"], sp["id"]), default=0) + 1, int(sp.get("version", 1)), int(__import__("os").environ.get("ARO_VERSION_FLOOR") or 0))


def produce(root: Path, sp: dict, do_export: bool = True) -> dict:
    nv = next_version(root, sp); a = build_scene(sp, nv)
    bad = {o.name: [round(v, 3) for v in geo.shell_volumes(o) if v <= 0] for o in a.lods.values()}; bad = {k: v for k, v in bad.items() if v}
    res = validate.run(bpy, mathutils, sp["kind"], True, None, sp["id"])
    cap = [] if len(res["stats"].get("materials", [])) <= 4 else [{"severity": "error", "rule": "materials", "message": f"{len(res['stats']['materials'])} materials; the approved breakdown allows at most 4 per asset"}]
    out = {"id": sp["id"], "kind": sp["kind"], "passed": res["passed"] and not bad and not cap, "inside_out_shells": bad, "stats": res["stats"], "target": sp["target"],
           "errors": [f for f in res["findings"] if f["severity"] == "error"] + cap, "warnings": [f for f in res["findings"] if f["severity"] == "warning"], "catalog_entry": catalog_entry(sp, res),
           "design_version": int(sp.get("version", 1)), "bake": {str(k): v for k, v in a.bake.items()}}
    if not out["passed"] or not do_export: return out
    v, d = ws.new_version_dir(root, sp["kind"], sp["id"], floor=nv); blend = d / f"{sp['id']}.blend"
    if v != nv: raise SystemExit(f"{sp['id']}: built as v{nv:03d} but the workspace gave v{v:03d}")
    textures.write_images(d); bpy.ops.wm.save_as_mainfile(filepath=str(blend), compress=True)
    (d / "project.json").write_text(json.dumps({"asset": sp["id"], "kind": sp["kind"], "version": f"v{v:03d}", "blender": bpy.app.version_string, "notes": sp["note"], "builder": "tools/blender/transport_kit",
                                                "saved_at": time.strftime("%Y-%m-%dT%H:%M:%SZ", time.gmtime())}, indent=2))
    files = []
    for fmt in ("fbx", "glb"):
        o = ws.export_dir(root, fmt, sp["id"], v); files.append(exp.export(bpy, o, sp["id"], fmt))
    res2 = dict(res); res2["catalog_entry"] = out["catalog_entry"]
    exp.write_manifest(files[0].parent, sp["id"], v, sp["kind"], files, res2, bpy.app.version_string, str(blend.relative_to(root)))
    vd = root / "validation" / sp["id"]; vd.mkdir(parents=True, exist_ok=True); (vd / f"v{v:03d}.json").write_text(json.dumps(res, indent=2))
    (root / "exports" / "unity").mkdir(parents=True, exist_ok=True); (root / "exports" / "unity" / "aro_materials.json").write_text(json.dumps(AMat.kit_json(), indent=1))
    out.update(version=f"v{v:03d}", blend=str(blend.relative_to(root)), exports=[str(f.relative_to(root)) for f in files]); return out


def load_all():
    from . import logistics, terminal, furniture, island, mosque, city, fuel, blocks  # noqa: F401  (importing registers the specs)
    from . import specs                                        # noqa: F401
    return SPECS


def main() -> int:
    ap = argparse.ArgumentParser(); ap.add_argument("--only", default=""); ap.add_argument("--family", default=""); ap.add_argument("--no-export", action="store_true"); ap.add_argument("--report", default=""); ap.add_argument("--catalog", default="")
    a = ap.parse_args(); load_all(); root = ws.ensure(ws.default_root()); report = []
    for aid, sp in SPECS.items():
        if a.only and aid not in a.only.split(","): continue
        if a.family and C.EXTRAS.get(aid, {}).get("family") != a.family: continue
        r = produce(root, sp, not a.no_export); report.append(r); s = r["stats"]
        print(f"{'PASS' if r['passed'] else 'FAIL'} {aid}: tris {s.get('lod_triangles')} size {s.get('size_m')} materials {len(s.get('materials', []))}" + (f" errors {[e['message'] for e in r['errors']]}" if r["errors"] else "")
              + (f" inside-out {r['inside_out_shells']}" if r["inside_out_shells"] else "") + (f" warnings {[w['message'] for w in r['warnings']]}" if r["warnings"] else ""))
    if a.report: Path(a.report).write_text(json.dumps(report, indent=1))
    if a.catalog: Path(a.catalog).write_text(json.dumps({"schemaVersion": 1, "note": "catalogue entries measured from the models plus the light heads, text faces and tags of transport_kit.common.EXTRAS; sockets are data for a future gameplay integration (nothing reads them)",
                                                         "props": [r["catalog_entry"] for r in report if r["passed"]], "bounds": {r["id"]: [r["stats"]["bounds_min_m"], r["stats"]["bounds_max_m"]] for r in report if r["passed"]}, "sockets": {k: v.get("sockets", []) for k, v in C.EXTRAS.items() if v.get("sockets")}}, indent=1))
    return 0 if all(r["passed"] for r in report) else 1


if __name__ == "__main__":
    sys.exit(main())
