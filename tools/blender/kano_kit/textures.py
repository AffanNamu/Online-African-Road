"""Procedural textures that SUPPORT the geometry (they never replace it): an interlocking-paver pattern and a worn-asphalt tile. Made with numpy, written as T_<name>_BaseColor.png images,
tile seamlessly, and are small (512 px). Colours are sRGB values read from the concept R-006 by eye."""
from __future__ import annotations
import numpy as np
import bpy
from aro_blender import materials as AM


def _to_image(name: str, rgb: np.ndarray):
    h, w, _ = rgb.shape; img = bpy.data.images.new(name, w, h, alpha=False)
    lin = np.where(rgb <= 0.04045, rgb / 12.92, ((rgb + 0.055) / 1.055) ** 2.4)          # the image is sRGB colour data: Blender stores linear floats
    px = np.concatenate([lin, np.ones((h, w, 1))], axis=2).astype(np.float32); img.pixels.foreach_set(px.ravel()); img.pack(); img.colorspace_settings.name = "sRGB"; img.filepath_raw = f"//{name}.png"; img.file_format = "PNG"
    return img


def paver_pixels(size=512, tile_m=2.0, seed=7):
    """Rectangular pavers 0.20 x 0.10 m laid in a herringbone-like running bond, each paver pink, rose-grey, beige or grey, with dark joints."""
    rnd = np.random.default_rng(seed); px_m = size / tile_m
    pal = np.array([(0.68, 0.54, 0.52), (0.62, 0.48, 0.48), (0.72, 0.62, 0.56), (0.58, 0.55, 0.56), (0.66, 0.58, 0.56), (0.50, 0.49, 0.51), (0.74, 0.58, 0.58)])
    w_b, h_b = int(0.20 * px_m), int(0.10 * px_m); img = np.zeros((size, size, 3)); joint = np.array((0.30, 0.28, 0.27))
    img[:] = joint; rows = size // h_b
    for r in range(rows):
        off = (r % 2) * (w_b // 2); y0 = r * h_b
        for c in range(-1, size // w_b + 1):
            x0 = c * w_b + off; col = pal[rnd.integers(0, len(pal))] * rnd.uniform(0.92, 1.06)
            xs = [(x0 + k) % size for k in range(1, w_b - 1)]; img[y0 + 1:y0 + h_b - 1, xs, :] = col
    img += rnd.normal(0, 0.012, img.shape)
    return np.clip(img, 0, 1)


def asphalt_pixels(size=512, seed=3):
    rnd = np.random.default_rng(seed); base = np.full((size, size, 3), 0.075)
    blot = rnd.normal(0, 1, (size // 16, size // 16)); blot = np.kron(blot, np.ones((16, 16))); k = np.ones(9) / 9.0
    for ax in (0, 1): blot = np.apply_along_axis(lambda m: np.convolve(np.r_[m[-8:], m, m[:8]], k, mode="same")[8:-8], ax, blot)
    img = base + 0.02 * blot[..., None] + rnd.normal(0, 0.018, (size, size, 1)); sp = rnd.random((size, size)) > 0.985; img[sp] += 0.10
    return np.clip(img, 0.02, 0.4)


def textured_material(key: str, image_name: str, pixels: np.ndarray, uv_scale: float = 1.0):
    """The kit material M_<key> with the image wired into Base Color (the Principled base colour stays the surface colour for the exporters)."""
    m = AM.get(key, bpy); nt = m.node_tree; bsdf = nt.nodes["Principled BSDF"]
    for n in list(nt.nodes):
        if n.type in ("TEX_NOISE", "VALTORGB", "TEX_COORD", "TEX_IMAGE", "MAPPING", "UVMAP"): nt.nodes.remove(n)
    img = bpy.data.images.get(image_name) or _to_image(image_name, pixels)
    uv = nt.nodes.new("ShaderNodeUVMap"); tex = nt.nodes.new("ShaderNodeTexImage"); tex.image = img; tex.interpolation = "Linear"; tex.extension = "REPEAT"
    nt.links.new(uv.outputs["UV"], tex.inputs["Vector"]); nt.links.new(tex.outputs["Color"], bsdf.inputs["Base Color"])
    return m


def paver_material():  return textured_material("paver_mix", "T_kano_paver_BaseColor", paver_pixels())
def asphalt_material(): return textured_material("asphalt", "T_kano_asphalt_BaseColor", asphalt_pixels())


def leaf_pixels(size=512, seed=21, per_cell=170):
    """A 2 x 2 atlas of foliage clusters (RGBA, background transparent). Each cell is an irregular blob densely covered by individual leaves (rotated ellipses with a lighter midrib and a dark rim),
    in four greens: sunlit, mid, shaded and mixed. The alpha is the leaf shape, so the card silhouette is leafy, not a quad."""
    rnd = np.random.default_rng(seed); out = np.zeros((size, size, 4)); cell = size // 2
    greens = [(0.42, 0.66, 0.16), (0.30, 0.54, 0.13), (0.17, 0.36, 0.09), (0.36, 0.58, 0.14)]
    yy, xx = np.mgrid[0:cell, 0:cell].astype(float)
    for ci in range(4):
        cx0, cy0 = (ci % 2) * cell, (ci // 2) * cell; img = np.zeros((cell, cell, 4)); base = np.array(greens[ci])
        ang = np.arctan2(yy - cell / 2, xx - cell / 2); rad = np.hypot(yy - cell / 2, xx - cell / 2)
        edge = cell * 0.42 * (1 + 0.14 * np.sin(3 * ang + ci) + 0.10 * np.sin(7 * ang + 2 * ci) + 0.06 * np.sin(13 * ang))
        blob = rad < edge * 0.80; img[blob, :3] = base * 0.45; img[blob, 3] = 1.0                       # dark fill so the cluster is dense
        for _ in range(per_cell):
            a = rnd.uniform(0, 2 * np.pi); r = (edge.mean() * 1.02) * np.sqrt(rnd.random()); lx, ly = cell / 2 + r * np.cos(a), cell / 2 + r * np.sin(a)
            la, lb = rnd.uniform(15, 24), rnd.uniform(5.5, 8.5); th = rnd.uniform(0, np.pi)
            x0, x1, y0, y1 = int(max(lx - la - 2, 0)), int(min(lx + la + 2, cell)), int(max(ly - la - 2, 0)), int(min(ly + la + 2, cell))
            if x1 <= x0 or y1 <= y0: continue
            gx, gy = xx[y0:y1, x0:x1] - lx, yy[y0:y1, x0:x1] - ly; u = gx * np.cos(th) + gy * np.sin(th); v = -gx * np.sin(th) + gy * np.cos(th)
            shape = (u / la) ** 2 + (v / (lb * (1 - 0.5 * np.abs(u / la)))) ** 2 < 1
            if not shape.any(): continue
            k = rnd.uniform(0.75, 1.30); grad = (0.8 + 0.3 * (u / la + 1) / 2)[..., None]; shade = np.clip(base[None, None, :] * k * grad, 0, 1); rib = np.abs(v) < 0.9
            sub = img[y0:y1, x0:x1]; sub[shape, :3] = shade[shape]; sub[shape & rib, :3] = np.clip(shade[shape & rib] * 1.25 + 0.04, 0, 1); sub[shape, 3] = 1.0
            rim = shape & ((u / la) ** 2 + (v / lb) ** 2 > 0.72); sub[rim, :3] *= 0.7
        out[cy0:cy0 + cell, cx0:cx0 + cell] = img
    return out


def leaf_material():
    """M_leaf_cutout with the leaf atlas wired into Base Color and its alpha into Alpha; Blender renders it through the alpha, Unity gets a cutout material built from the kit (alpha clip 0.5, two-sided)."""
    m = AM.get("leaf_cutout", bpy); nt = m.node_tree; bsdf = nt.nodes["Principled BSDF"]
    for n in list(nt.nodes):
        if n.type in ("TEX_NOISE", "VALTORGB", "TEX_COORD", "TEX_IMAGE", "UVMAP"): nt.nodes.remove(n)
    px = leaf_pixels(); name = "T_kano_leaves_BaseColor"; img = bpy.data.images.get(name)
    if img is None:
        h, w, _ = px.shape; img = bpy.data.images.new(name, w, h, alpha=True); rgb = px[..., :3]
        lin = np.where(rgb <= 0.04045, rgb / 12.92, ((rgb + 0.055) / 1.055) ** 2.4); data = np.concatenate([lin, px[..., 3:4]], axis=2).astype(np.float32)
        img.pixels.foreach_set(data.ravel()); img.alpha_mode = "STRAIGHT"; img.pack(); img.filepath_raw = f"//{name}.png"; img.file_format = "PNG"
    uv = nt.nodes.new("ShaderNodeUVMap"); tex = nt.nodes.new("ShaderNodeTexImage"); tex.image = img; tex.interpolation = "Linear"; tex.extension = "CLIP"
    nt.links.new(uv.outputs["UV"], tex.inputs["Vector"]); nt.links.new(tex.outputs["Color"], bsdf.inputs["Base Color"]); nt.links.new(tex.outputs["Alpha"], bsdf.inputs["Alpha"])
    try: m.surface_render_method = "DITHERED"
    except Exception: pass
    return m


def write_images(folder) -> list[str]:
    """Writes every T_* image to `folder` as PNG and points the image at it, so the FBX exporter (path_mode COPY) can copy the texture next to the model. Packed copies stay in the .blend."""
    import pathlib
    folder = pathlib.Path(folder); folder.mkdir(parents=True, exist_ok=True); names = []
    for img in bpy.data.images:
        if not img.name.startswith("T_"): continue
        p = folder / f"{img.name}.png"; img.filepath_raw = str(p); img.file_format = "PNG"; img.save(); names.append(p.name)
    return names
