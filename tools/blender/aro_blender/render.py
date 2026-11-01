"""Preview renders: a camera framed on the whole asset, from named views, with simple fair lighting. Temporary objects are removed and the scene's own settings restored afterwards,
so a preview never changes what gets saved or exported. Views are relative to the ASSET: front is the side that faces -Y (the asset's right is -X), the convention that arrives in Unity as front +Z, right +X."""
from __future__ import annotations
import ctypes.util, math, os, time
from pathlib import Path

VIEWS = {"front": (0, -1, 0.18), "back": (0, 1, 0.18), "right": (-1, 0, 0.18), "left": (1, 0, 0.18), "top": (0, 0, 1), "iso": (-0.85, -0.95, 0.7), "iso_back": (0.85, 0.95, 0.7), "eye": (0.0, -1, 0.04)}
ORTHO = {"front", "back", "right", "left", "top"}
ENGINES = {"cycles": "CYCLES", "workbench": "BLENDER_WORKBENCH", "eevee": "BLENDER_EEVEE"}
TEMP = "__aro_preview_"


def gl_available() -> bool:
    """Workbench and EEVEE need an OpenGL context (libEGL; on a machine without a GPU, Mesa's software renderer). Without it Blender ABORTS the whole process, so ask first.
    ARO_BLENDER_NO_GL=1 pretends it is missing (for tests)."""
    return os.environ.get("ARO_BLENDER_NO_GL") != "1" and ctypes.util.find_library("EGL") is not None


def _bounds(bpy, mathutils):
    lo, hi = [1e9] * 3, [-1e9] * 3; n = 0
    for o in bpy.context.scene.objects:
        if o.type != "MESH" or o.name.startswith(TEMP) or o.hide_render or o.name.startswith("COL_"): continue
        n += 1
        for c in o.bound_box:
            p = o.matrix_world @ mathutils.Vector(c)
            for i in range(3): lo[i] = min(lo[i], p[i]); hi[i] = max(hi[i], p[i])
    if not n: raise ValueError("the scene has no visible mesh to render")
    return lo, hi


def preview(bpy, mathutils, out_path: Path, view: str = "iso", engine: str = "cycles", width: int = 960, height: int = 540, samples: int = 32, ground: bool = True,
            target: str | None = None) -> Path:
    if view not in VIEWS: raise ValueError(f"unknown view '{view}': {', '.join(VIEWS)}")
    if engine not in ENGINES: raise ValueError(f"unknown engine '{engine}': {', '.join(ENGINES)}")
    if engine != "cycles" and not gl_available():
        raise ValueError(f"the {engine} engine needs OpenGL (libEGL) and this machine has none: use engine 'cycles', or install libegl1 libgl1 libgl1-mesa-dri (software OpenGL, no GPU needed)")
    width, height, samples = max(64, min(int(width), 1920)), max(64, min(int(height), 1080)), max(1, min(int(samples), 256))
    sc = bpy.context.scene
    if target:
        t = bpy.data.objects.get(target)
        if not t: raise ValueError(f"no object '{target}'")
        lo, hi = [1e9] * 3, [-1e9] * 3
        for o in [t] + list(t.children_recursive):
            if o.type == "MESH":
                for c in o.bound_box:
                    p = o.matrix_world @ mathutils.Vector(c)
                    for i in range(3): lo[i] = min(lo[i], p[i]); hi[i] = max(hi[i], p[i])
    else:
        lo, hi = _bounds(bpy, mathutils)
    centre = mathutils.Vector([(a + b) / 2 for a, b in zip(lo, hi)]); size = mathutils.Vector([b - a for a, b in zip(lo, hi)]); radius = max(size.length / 2, 0.5)
    saved = {"engine": sc.render.engine, "res": (sc.render.resolution_x, sc.render.resolution_y, sc.render.resolution_percentage), "camera": sc.camera, "world": sc.world,
             "path": sc.render.filepath, "fmt": sc.render.image_settings.file_format, "view": (sc.view_settings.view_transform, sc.view_settings.look)}
    made_objects, made_data = [], []
    try:
        sc.render.engine = ENGINES[engine]; sc.render.resolution_x, sc.render.resolution_y, sc.render.resolution_percentage = width, height, 100
        sc.render.image_settings.file_format = "PNG"
        sc.view_settings.view_transform = "Standard"; sc.view_settings.look = "None"      # no tone-mapping film look: colours are judged against references as they are
        if engine == "cycles":
            sc.cycles.device = "CPU"; sc.cycles.samples = samples
            try: sc.cycles.use_denoising = True
            except Exception: pass
        elif engine == "eevee":
            try: sc.eevee.taa_render_samples = samples
            except Exception: pass
        else:
            sh = sc.display.shading; sh.light = "STUDIO"; sh.color_type = "MATERIAL"
            try: sh.show_cavity = True
            except Exception: pass
        # world: a plain pale sky, so shadows and surfaces read the same in every preview
        w = bpy.data.worlds.new(TEMP + "world"); made_data.append(("worlds", w)); w.use_nodes = True
        bg = w.node_tree.nodes.get("Background")
        if bg: bg.inputs["Color"].default_value = (0.62, 0.74, 0.92, 1.0); bg.inputs["Strength"].default_value = 0.9
        sc.world = w
        # camera
        d = mathutils.Vector(VIEWS[view]).normalized()
        cd = bpy.data.cameras.new(TEMP + "cam"); made_data.append(("cameras", cd)); cam = bpy.data.objects.new(TEMP + "cam", cd); sc.collection.objects.link(cam); made_objects.append(cam)
        aspect = width / height
        if view in ORTHO:
            cd.type = "ORTHO"
            if view == "top": ext_h, ext_w = size.y, size.x
            elif view in ("front", "back"): ext_h, ext_w = size.z, size.x
            else: ext_h, ext_w = size.z, size.y
            cd.ortho_scale = max(ext_w, ext_h * aspect) * 1.18 + 0.2
            cam.location = centre + d * (radius * 3 + 10)
        else:
            cd.type = "PERSP"; cd.lens = 42 if view != "eye" else 28
            half = 18.0 / cd.lens                                                       # tangent of half the field of view across the sensor's long side (36 mm)
            fov = 2 * math.atan(half / aspect if aspect >= 1 else half * aspect)       # Blender fits the 36 mm sensor to the longer side: the narrower field is the shorter side
            cam.location = centre + d * (radius / math.sin(fov / 2) * 1.12 + (0 if view != "eye" else radius * 0.2))
        cam.rotation_euler = (centre - cam.location).to_track_quat("-Z", "Y").to_euler()
        cd.clip_start = 0.05; cd.clip_end = max(2000.0, radius * 40); sc.camera = cam
        # sun + optional ground
        ld = bpy.data.lights.new(TEMP + "sun", "SUN"); made_data.append(("lights", ld)); ld.energy = 3.2 if engine != "workbench" else 1.0; ld.angle = math.radians(4)
        lo_ = bpy.data.objects.new(TEMP + "sun", ld); sc.collection.objects.link(lo_); made_objects.append(lo_)
        lo_.rotation_euler = mathutils.Euler((math.radians(52), math.radians(8), math.radians(135 if view in ("iso", "front", "right") else 45)))
        if ground and view != "top":
            span = max(radius * 14, 60); me = bpy.data.meshes.new(TEMP + "ground"); made_data.append(("meshes", me))
            h = span / 2; me.from_pydata([(-h, -h, 0), (h, -h, 0), (h, h, 0), (-h, h, 0)], [], [(0, 1, 2, 3)]); me.update()
            g = bpy.data.objects.new(TEMP + "ground", me); sc.collection.objects.link(g); made_objects.append(g); g.location = (centre.x, centre.y, lo[2] - 0.001)
            gm = bpy.data.materials.new(TEMP + "gm"); made_data.append(("materials", gm)); gm.use_nodes = True
            b = gm.node_tree.nodes.get("Principled BSDF")
            if b: b.inputs["Base Color"].default_value = (0.30, 0.27, 0.22, 1); b.inputs["Roughness"].default_value = 0.95
            gm.diffuse_color = (0.30, 0.27, 0.22, 1); me.materials.append(gm)
        out_path.parent.mkdir(parents=True, exist_ok=True); sc.render.filepath = str(out_path.with_suffix(""))
        bpy.ops.render.render(write_still=True)
        p = Path(str(out_path.with_suffix("")) + ".png")
        if not p.is_file(): raise RuntimeError("the render finished but wrote no file")
        return p
    finally:
        for o in made_objects:
            try: bpy.data.objects.remove(o, do_unlink=True)
            except Exception: pass
        for coll, d in made_data:
            try: getattr(bpy.data, coll).remove(d)
            except Exception: pass
        sc.render.engine = saved["engine"]; sc.render.resolution_x, sc.render.resolution_y, sc.render.resolution_percentage = saved["res"]
        sc.camera = saved["camera"]; sc.world = saved["world"]; sc.render.filepath = saved["path"]; sc.render.image_settings.file_format = saved["fmt"]
        sc.view_settings.view_transform, sc.view_settings.look = saved["view"]
