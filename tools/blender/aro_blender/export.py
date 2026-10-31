"""Export to Unity: FBX (primary) and GLB, plus a manifest of what was measured. Exports are written to a fresh versioned folder and never overwrite one.
The axis settings are the commonly used Blender-to-Unity pair (forward -Z, up Y, transforms baked); the first Unity import test (tools/blender/unity_calibration) confirms them or changes them."""
from __future__ import annotations
import hashlib, json, time
from pathlib import Path


def _select_exportable(bpy):
    for o in bpy.context.scene.objects: o.select_set(False)
    n = 0
    for o in bpy.context.scene.objects:
        if o.name.startswith("__aro_") or o.hide_viewport or o.hide_render or o.type not in ("MESH", "EMPTY", "ARMATURE"): continue
        o.select_set(True); n += 1
    return n


def _sha(p: Path) -> str:
    h = hashlib.sha256(); h.update(p.read_bytes()); return h.hexdigest()


def export(bpy, out_dir: Path, name: str, fmt: str) -> Path:
    n = _select_exportable(bpy)
    if not n: raise ValueError("nothing to export")
    if fmt == "fbx":
        p = out_dir / f"{name}.fbx"
        bpy.ops.export_scene.fbx(filepath=str(p), use_selection=True, object_types={"MESH", "EMPTY", "ARMATURE"}, use_mesh_modifiers=True, mesh_smooth_type="FACE",
                                 apply_scale_options="FBX_SCALE_ALL", global_scale=1.0, axis_forward="-Z", axis_up="Y", bake_space_transform=True, add_leaf_bones=False,
                                 path_mode="COPY", embed_textures=False, use_custom_props=False)
    elif fmt == "glb":
        p = out_dir / f"{name}.glb"
        bpy.ops.export_scene.gltf(filepath=str(p), export_format="GLB", use_selection=True, export_apply=True, export_yup=True, export_materials="EXPORT")
    else:
        raise ValueError(f"unknown format '{fmt}': fbx or glb")
    if not p.is_file() or p.stat().st_size == 0: raise RuntimeError(f"{fmt} export wrote no file")
    return p


def write_manifest(out_dir: Path, name: str, version: int, kind: str, files: list[Path], validation: dict, blender_version: str, project: str) -> Path:
    m = {"asset": name, "kind": kind, "version": f"v{version:03d}", "blender": blender_version, "source_project": project, "exported_at": time.strftime("%Y-%m-%dT%H:%M:%SZ", time.gmtime()),
         "files": [{"file": f.name, "bytes": f.stat().st_size, "sha256": _sha(f)} for f in files], "validation": validation, "catalog_entry": validation.get("catalog_entry"),
         "orientation_convention": "Blender: front +Y, up +Z, right +X, 1 unit = 1 m -> Unity: front +Z, up +Y, right +X (Art README); FBX forward -Z up Y; confirmed or corrected by the Unity calibration import"}
    p = out_dir / f"{name}.manifest.json"; p.write_text(json.dumps(m, indent=2)); return p
