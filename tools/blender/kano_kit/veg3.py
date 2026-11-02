"""Residential kit v003 vegetation: ONE material per plant (owner decision of 2026-11-03: fewer draw calls without degrading the look).

v002 trees used two materials (bark and leaf cards), shrubs three (leaf cards, dark green body, red flowers), hedges two. v003 folds them into `kano_veg`, an alpha-clipped two-sided material whose 1024 x 512 texture holds:
  * the SAME leaf-cluster atlas as v002 in the left half (u 0 to 0.5), at full resolution, so the foliage is identical;
  * a bark cell (u 0.5 to 0.75, v 0.5 to 1.0) taken from the v002 bark texture, mapped per face (the trunk is a thin, distant element; the normal map is not carried over);
  * a grid of flat colour cells (hedge green, flower red) at u 0.5 to 0.5625, v 0 to 0.125, 16 px each.
`convert(part)` rewrites a finished v002-style Part onto that material and those UVs. Opt-in: the v002 builders are untouched."""
from __future__ import annotations
import numpy as np
import bpy
from mathutils import Vector
from aro_blender import materials as AM
from . import textures as KT

W, H = 1024, 512
BARK = (512, 256, 256, 256)                                   # x, y, w, h in pixels (y from the bottom, Blender order)
FLAT = {"hedge_green": 0, "flower_red": 1, "leaf_green": 2, "leaf_green_dark": 3, "leaf_green_light": 4}
FLAT_ORIGIN = (512, 0); FLAT_CELL = 16; FLAT_GRID = 4
LEAF_KEYS = {"leaf_cutout"}


def atlas_pixels() -> np.ndarray:
    px = np.zeros((H, W, 4)); leaf = KT.leaf_pixels(); px[:, :512, :] = leaf                         # array row 0 is the BOTTOM row of the image (Blender order), as the v002 atlas is stored
    bark = KT.bark_maps()[0]; b = bark.reshape(256, 2, 256, 2, 3).mean(axis=(1, 3)); x, y, w, h = BARK; px[y:y + h, x:x + w, :3] = b; px[y:y + h, x:x + w, 3] = 1.0
    ox, oy = FLAT_ORIGIN
    for k, i in FLAT.items():
        cx, cy = i % FLAT_GRID, i // FLAT_GRID; px[oy + cy * FLAT_CELL:oy + (cy + 1) * FLAT_CELL, ox + cx * FLAT_CELL:ox + (cx + 1) * FLAT_CELL, :3] = AM.LIBRARY[k][0]; px[oy + cy * FLAT_CELL:oy + (cy + 1) * FLAT_CELL, ox + cx * FLAT_CELL:ox + (cx + 1) * FLAT_CELL, 3] = 1.0
    return px


def flat_uv(key: str) -> tuple[float, float]:
    i = FLAT[key]; ox, oy = FLAT_ORIGIN; return ((ox + (i % FLAT_GRID) * FLAT_CELL + FLAT_CELL / 2) / W, (oy + (i // FLAT_GRID) * FLAT_CELL + FLAT_CELL / 2) / H)


def convert(part) -> dict:
    """Moves the faces of leaf, bark and flat-colour keys onto `kano_veg`. Leaf cards keep their cluster quadrant (u halved, because the left half of the new atlas holds the old one); bark faces are mapped into the bark
    cell by planar projection on their own plane at 1 cell per 0.8 m, shifted to a hash-chosen spot of the cell (a thin trunk, seen from a distance); flat faces take their cell centre."""
    bm = part.bm; bm.normal_update(); uvl = bm.loops.layers.uv.verify(); fixed = getattr(part, "_fixed", set()); part._fixed = set(fixed); moved = {"leaf": 0, "bark": 0, "flat": 0}
    bx, by, bw, bh = BARK; ub0, vb0, du, dv = bx / W, by / H, bw / W, bh / H
    old_keys = list(part.keys)
    for f in bm.faces:
        k = old_keys[f.material_index]
        if k in LEAF_KEYS:
            if f in fixed:
                for l in f.loops: u, v = l[uvl].uv; l[uvl].uv = (u * 0.5, v)
            moved["leaf"] += 1
        elif k == "bark":
            n = f.normal; a = Vector((0, 0, 1)) if abs(n.z) < 0.9 else Vector((1, 0, 0)); e1 = n.cross(a).normalized(); e2 = n.cross(e1).normalized()
            ps = [(v.co.dot(e1), v.co.dot(e2)) for v in f.verts]; u0 = min(p[0] for p in ps); v0 = min(p[1] for p in ps); ext = max(max(p[0] for p in ps) - u0, max(p[1] for p in ps) - v0, 1e-6)
            sc = min(1.0 / 0.8, 0.9 / ext); c = f.calc_center_median(); h = (hash((round(c.x, 3), round(c.y, 3), round(c.z, 3))) & 0xFFFF) / 65535.0; h2 = ((hash((round(c.z, 3), round(c.x, 3))) >> 3) & 0xFFFF) / 65535.0
            su, sv = (1 - sc * ext) * h, (1 - sc * ext) * h2
            for l, p in zip(f.loops, ps): l[uvl].uv = (ub0 + du * (su + (p[0] - u0) * sc), vb0 + dv * (sv + (p[1] - v0) * sc))
            part._fixed.add(f); moved["bark"] += 1
        elif k in FLAT:
            u, v = flat_uv(k)
            for l in f.loops: l[uvl].uv = (u, v)
            part._fixed.add(f); moved["flat"] += 1
    new_keys: list[str] = []; fk = {}
    for f in bm.faces:
        k = old_keys[f.material_index]; k2 = "kano_veg" if (k in LEAF_KEYS or k == "bark" or k in FLAT) else k; fk[f] = k2
        if k2 not in new_keys: new_keys.append(k2)
    for f in bm.faces: f.material_index = new_keys.index(fk[f])
    part.keys = new_keys; return moved


def apply_atlas() -> list[str]:
    """Wires the atlas into M_kano_veg (base colour and alpha, alpha-clipped two-sided) when the file has the material."""
    if bpy.data.materials.get("M_kano_veg") is None: return []
    m = AM.get("kano_veg", bpy); nt = m.node_tree; bsdf = nt.nodes["Principled BSDF"]
    for n in list(nt.nodes):
        if n.type in ("TEX_NOISE", "VALTORGB", "TEX_COORD", "TEX_IMAGE", "UVMAP"): nt.nodes.remove(n)
    name = "T_kano_vegatlas_BaseColor"; img = bpy.data.images.get(name)
    if img is None:
        px = atlas_pixels(); img = bpy.data.images.new(name, W, H, alpha=True); img.pixels.foreach_set(px.astype(np.float32).ravel()); img.alpha_mode = "STRAIGHT"; img.pack(); img.filepath_raw = f"//{name}.png"; img.file_format = "PNG"
    uv = nt.nodes.new("ShaderNodeUVMap"); tex = nt.nodes.new("ShaderNodeTexImage"); tex.image = img; tex.interpolation = "Linear"; tex.extension = "CLIP"
    nt.links.new(uv.outputs["UV"], tex.inputs["Vector"]); nt.links.new(tex.outputs["Color"], bsdf.inputs["Base Color"]); nt.links.new(tex.outputs["Alpha"], bsdf.inputs["Alpha"])
    try: m.surface_render_method = "DITHERED"
    except Exception: pass
    return ["kano_veg"]
