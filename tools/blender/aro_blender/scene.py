"""Scene operations behind the MCP tools: a clean scene, inspection with real numbers, primitives, transforms, materials.
Conventions (docs/BLENDER_PIPELINE.md): 1 Blender unit = 1 metre; Z is up; an asset's FRONT faces -Y (Blender's own front-view convention; its right is -X, so a left-hand-drive steering wheel sits at x > 0);
the origin sits on the ground at the asset's centre; transforms are applied. The -Y-front choice is MEASURED, not assumed: the Unity import test (BlenderAssetImportTests) showed that both the FBX importer and glTFast mirror X, which puts a +Y-front asset facing -Z in Unity and a -Y-front
asset facing +Z (docs/BLENDER_PIPELINE.md section 8)."""
from __future__ import annotations
import math
from . import materials

PRIMITIVES = ("cube", "plane", "cylinder", "cone", "sphere", "torus", "circle", "empty")


def clear(bpy) -> None:
    bpy.ops.wm.read_factory_settings(use_empty=True)
    u = bpy.context.scene.unit_settings; u.system = "METRIC"; u.scale_length = 1.0; u.length_unit = "METERS"


def _world_bbox(obj, mathutils):
    pts = [obj.matrix_world @ mathutils.Vector(c) for c in obj.bound_box]
    return [min(p[i] for p in pts) for i in range(3)], [max(p[i] for p in pts) for i in range(3)]


def mesh_stats(obj, bpy):
    """(vertices, triangles) of the evaluated mesh (modifiers included)."""
    dg = bpy.context.evaluated_depsgraph_get(); ev = obj.evaluated_get(dg); me = ev.to_mesh()
    try: me.calc_loop_triangles(); return len(me.vertices), len(me.loop_triangles)
    finally: ev.to_mesh_clear()


def inspect(bpy, mathutils, limit: int = 200) -> dict:
    objs = sorted(bpy.context.scene.objects, key=lambda o: o.name)
    rows, tris, verts = [], 0, 0
    lo, hi = [1e9] * 3, [-1e9] * 3
    for o in objs[:limit]:
        row = {"name": o.name, "type": o.type, "parent": o.parent.name if o.parent else None, "collections": [c.name for c in o.users_collection],
               "location": [round(v, 4) for v in o.location], "rotation_deg": [round(math.degrees(v), 3) for v in o.rotation_euler], "scale": [round(v, 4) for v in o.scale],
               "visible": not o.hide_viewport and not o.hide_render}
        if o.type == "MESH":
            v, t = mesh_stats(o, bpy); verts += v; tris += t
            a, b = _world_bbox(o, mathutils); lo = [min(x, y) for x, y in zip(lo, a)]; hi = [max(x, y) for x, y in zip(hi, b)]
            row.update({"vertices": v, "triangles": t, "dimensions_m": [round(d, 3) for d in o.dimensions], "materials": [s.material.name if s.material else None for s in o.material_slots],
                        "uv_layers": [u.name for u in o.data.uv_layers], "modifiers": [m.type for m in o.modifiers]})
        rows.append(row)
    return {"object_count": len(objs), "shown": len(rows), "triangles_total": tris, "vertices_total": verts,
            "bounds_min_m": [round(v, 3) for v in lo] if tris else None, "bounds_max_m": [round(v, 3) for v in hi] if tris else None,
            "size_m": [round(h - l, 3) for h, l in zip(hi, lo)] if tris else None,
            "materials": sorted(m.name for m in bpy.data.materials), "collections": sorted(c.name for c in bpy.data.collections), "objects": rows}


def _collection(bpy, name: str | None):
    if not name: return bpy.context.scene.collection
    c = bpy.data.collections.get(name)
    if not c: c = bpy.data.collections.new(name); bpy.context.scene.collection.children.link(c)
    return c


def create_primitive(bpy, kind: str, name: str, location=(0, 0, 0), dimensions=None, rotation_deg=(0, 0, 0), parent: str | None = None, collection: str | None = None,
                     vertices: int = 24):
    if kind not in PRIMITIVES: raise ValueError(f"unknown primitive '{kind}': {', '.join(PRIMITIVES)}")
    if bpy.data.objects.get(name): raise ValueError(f"an object called '{name}' already exists")
    if kind == "empty":
        o = bpy.data.objects.new(name, None); o.empty_display_type = "PLAIN_AXES"
    else:
        ops = bpy.ops.mesh
        if kind == "cube": ops.primitive_cube_add(size=1)
        elif kind == "plane": ops.primitive_plane_add(size=1)
        elif kind == "cylinder": ops.primitive_cylinder_add(vertices=vertices, radius=0.5, depth=1)
        elif kind == "cone": ops.primitive_cone_add(vertices=vertices, radius1=0.5, radius2=0, depth=1)
        elif kind == "sphere": ops.primitive_uv_sphere_add(segments=vertices, ring_count=max(4, vertices // 2), radius=0.5)
        elif kind == "torus": ops.primitive_torus_add(major_radius=0.4, minor_radius=0.1)
        elif kind == "circle": ops.primitive_circle_add(vertices=vertices, radius=0.5, fill_type="NGON")
        o = bpy.context.object; o.name = name; o.data.name = name
        for c in list(o.users_collection): c.objects.unlink(o)
    _collection(bpy, collection).objects.link(o)
    if parent:
        p = bpy.data.objects.get(parent)
        if not p: raise ValueError(f"no parent object '{parent}'")
        o.parent = p
    o.location = location; o.rotation_euler = [math.radians(v) for v in rotation_deg]
    if dimensions and kind != "empty":
        o.dimensions = dimensions; bpy.context.view_layer.objects.active = o; o.select_set(True); bpy.ops.object.transform_apply(location=False, rotation=False, scale=True); o.select_set(False)
    return o


def transform(bpy, name: str, location=None, rotation_deg=None, scale=None, apply: bool = False):
    o = bpy.data.objects.get(name)
    if not o: raise ValueError(f"no object '{name}'")
    if location is not None: o.location = location
    if rotation_deg is not None: o.rotation_euler = [math.radians(v) for v in rotation_deg]
    if scale is not None: o.scale = scale
    if apply and o.type == "MESH":
        for x in bpy.context.selected_objects: x.select_set(False)
        bpy.context.view_layer.objects.active = o; o.select_set(True); bpy.ops.object.transform_apply(location=False, rotation=True, scale=True); o.select_set(False)
    return o


def delete(bpy, names: list[str]) -> list[str]:
    gone = []
    for n in names:
        o = bpy.data.objects.get(n)
        if o: bpy.data.objects.remove(o, do_unlink=True); gone.append(n)
    return gone


def assign_material(bpy, obj_name: str, material: str, replace: bool = True):
    o = bpy.data.objects.get(obj_name)
    if not o or o.type != "MESH": raise ValueError(f"no mesh object '{obj_name}'")
    m = materials.get(material, bpy)
    if replace: o.data.materials.clear()
    o.data.materials.append(m)
    return m
