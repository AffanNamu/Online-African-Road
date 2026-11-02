"""Procedural textures that SUPPORT the geometry (they never replace it): an interlocking-paver pattern and a worn-asphalt tile. Made with numpy, written as T_<name>_BaseColor.png images,
tile seamlessly, and are small (512 px). Colours are sRGB values read from the concept R-006 by eye."""
from __future__ import annotations
import numpy as np
import bpy
from aro_blender import materials as AM


def _to_image(name: str, rgb: np.ndarray):
    h, w, _ = rgb.shape; img = bpy.data.images.new(name, w, h, alpha=False)
    # An 8-bit image created from Python stores and saves the values it is given AS THEY ARE (checked: 0.5 is saved as byte 128). The first version converted sRGB to linear here, which double-decoded
    # every base-colour texture (darker and more saturated: the plaster read yellow, the stone red) in the renders AND in the exported PNGs that Unity reads. Colour data is therefore set as sRGB.
    px = np.concatenate([rgb, np.ones((h, w, 1))], axis=2).astype(np.float32); img.pixels.foreach_set(px.ravel()); img.pack(); img.colorspace_settings.name = "sRGB"; img.filepath_raw = f"//{name}.png"; img.file_format = "PNG"
    return img


def paver_pixels(size=512, tile_m=2.0, seed=7):
    """Rectangular pavers 0.20 x 0.10 m laid in a herringbone-like running bond, each paver pink, rose-grey, beige or grey, with dark joints."""
    rnd = np.random.default_rng(seed); px_m = size / tile_m
    pal = np.array([(0.70, 0.60, 0.58), (0.62, 0.55, 0.55), (0.74, 0.68, 0.62), (0.60, 0.58, 0.59), (0.68, 0.62, 0.60), (0.54, 0.53, 0.55), (0.72, 0.62, 0.62)])   # pale pink, rose-grey, beige and grey as in the concept
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
    rnd = np.random.default_rng(seed); base = np.full((size, size, 3), 0.21)
    blot = rnd.normal(0, 1, (size // 16, size // 16)); blot = np.kron(blot, np.ones((16, 16))); k = np.ones(9) / 9.0
    for ax in (0, 1): blot = np.apply_along_axis(lambda m: np.convolve(np.r_[m[-8:], m, m[:8]], k, mode="same")[8:-8], ax, blot)
    img = base + 0.02 * blot[..., None] + rnd.normal(0, 0.018, (size, size, 1)); sp = rnd.random((size, size)) > 0.985; img[sp] += 0.10
    return np.clip(img, 0.08, 0.5)


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


def leaf_pixels(size=512, seed=21, per_cell=900, greens=None):
    """A 2 x 2 atlas of foliage clusters (RGBA, background transparent). Each cell is an irregular blob densely covered by individual leaves (rotated ellipses with a lighter midrib and a dark rim),
    in four greens: sunlit, mid, shaded and mixed. The alpha is the leaf shape, so the card silhouette is leafy, not a quad."""
    rnd = np.random.default_rng(seed); out = np.zeros((size, size, 4)); cell = size // 2
    greens = greens or [(0.33, 0.55, 0.15), (0.24, 0.45, 0.12), (0.15, 0.31, 0.08), (0.28, 0.49, 0.13)]
    yy, xx = np.mgrid[0:cell, 0:cell].astype(float)
    for ci in range(4):
        cx0, cy0 = (ci % 2) * cell, (ci // 2) * cell; img = np.zeros((cell, cell, 4)); base = np.array(greens[ci])
        ang = np.arctan2(yy - cell / 2, xx - cell / 2); rad = np.hypot(yy - cell / 2, xx - cell / 2)
        edge = cell * 0.42 * (1 + 0.24 * np.sin(3 * ang + ci) + 0.17 * np.sin(5 * ang + 2 * ci) + 0.12 * np.sin(9 * ang + ci) + 0.07 * np.sin(17 * ang))      # lobed, not a disc: the first pass read as round cabbage leaves
        blob = rad < edge * 0.66; img[blob, :3] = base * 0.62; img[blob, 3] = 1.0                       # dark fill so the cluster is dense
        for _ in range(per_cell):
            a = rnd.uniform(0, 2 * np.pi); e_a = cell * 0.42 * (1 + 0.24 * np.sin(3 * a + ci) + 0.17 * np.sin(5 * a + 2 * ci) + 0.12 * np.sin(9 * a + ci) + 0.07 * np.sin(17 * a)); r = e_a * 1.04 * np.sqrt(rnd.random()); lx, ly = cell / 2 + r * np.cos(a), cell / 2 + r * np.sin(a)
            la, lb = rnd.uniform(6.0, 10.5), rnd.uniform(2.4, 3.9); th = rnd.uniform(0, np.pi)
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
        data = np.concatenate([rgb, px[..., 3:4]], axis=2).astype(np.float32)               # sRGB values as they are (see _to_image)
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


# ------------------------------------------------------------------------------------------------------------------------ PBR texture set (base colour + normal), all procedural, all tiling
def _noise(size, cells, seed):
    """Tileable value noise in 0..1 (bilinear over a wrapped random grid)."""
    rnd = np.random.default_rng(seed); g = rnd.random((cells, cells)); xs = np.linspace(0, cells, size, endpoint=False); i0 = xs.astype(int) % cells; i1 = (i0 + 1) % cells; f = xs - np.floor(xs)
    f = f * f * (3 - 2 * f); a = g[np.ix_(i0, i0)]; b = g[np.ix_(i0, i1)]; c = g[np.ix_(i1, i0)]; d = g[np.ix_(i1, i1)]
    fx = f[None, :]; fy = f[:, None]
    return (a * (1 - fx) + b * fx) * (1 - fy) + (c * (1 - fx) + d * fx) * fy


def _fbm(size, base, octaves, seed, gain=0.5):
    out = np.zeros((size, size)); amp = 1.0; tot = 0.0
    for o in range(octaves): out += amp * _noise(size, base * 2 ** o, seed + o * 17); tot += amp; amp *= gain
    return out / tot


def _normal_from_height(h, strength):
    """Tangent-space normal map (OpenGL / Y+, what Blender's Normal Map node, glTF and Unity expect) from a tileable height field."""
    gx = (np.roll(h, -1, 1) - np.roll(h, 1, 1)) * 0.5 * strength; gy = (np.roll(h, -1, 0) - np.roll(h, 1, 0)) * 0.5 * strength
    n = np.stack([-gx, gy, np.ones_like(h)], axis=2); n /= np.linalg.norm(n, axis=2, keepdims=True)
    return n * 0.5 + 0.5


def plaster_maps(size=512, seed=31, tint=(0.88, 0.83, 0.74)):
    f = _fbm(size, 4, 4, seed); fine = _fbm(size, 64, 2, seed + 5) ; stain = _fbm(size, 2, 3, seed + 9)
    col = np.array(tint)[None, None, :] * (0.93 + 0.10 * f[..., None] + 0.04 * (fine[..., None] - 0.5) - 0.05 * stain[..., None])
    return np.clip(col, 0, 1), _normal_from_height(fine * 0.6 + f * 0.4, 1.6)


def stone_maps(size=512, seed=41, base=(0.70, 0.63, 0.54), grey=False):
    """Stone cladding: a ledgestone pattern of stacked rectangular stones with grooves (grey) or travertine with horizontal veining and pores (tan)."""
    rnd = np.random.default_rng(seed); h = np.zeros((size, size)); col = np.zeros((size, size, 3)); b = np.array(base)
    if grey:
        rows = 8; rh = size // rows
        for r in range(rows):
            x = int(rnd.integers(0, 80))
            while x < size + 120:
                w = int(rnd.integers(70, 150)); tone = rnd.uniform(0.82, 1.14); y0, y1 = r * rh, (r + 1) * rh
                xs = np.arange(x, x + w) % size; col[y0:y1, xs] = b * tone; h[y0 + 4:y1 - 3, xs[3:-3] if len(xs) > 8 else xs] = 1.0 + rnd.uniform(-0.15, 0.15); x += w
        col[h == 0] = b * 0.45
    else:
        veins = _fbm(size, 3, 3, seed + 3); veins = 0.5 + 0.5 * np.sin(np.linspace(0, 40 * np.pi, size)[:, None] + 6 * veins); pores = _fbm(size, 48, 2, seed + 8)
        col = b[None, None, :] * (0.88 + 0.14 * veins[..., None] + 0.06 * (pores[..., None] - 0.5)); col[pores < 0.22] *= 0.72; h = 0.5 * veins + 0.5 * pores
    h = h + 0.15 * _fbm(size, 40, 2, seed + 11)
    return np.clip(col, 0, 1), _normal_from_height(h, 3.0 if grey else 1.4)


def roof_tile_maps(size=512, seed=51, base=(0.17, 0.17, 0.19)):
    """Overlapping concrete roof tiles: rows 0.1 m high, tiles 0.3 m wide (a 2 m tile of the texture), each row shifted, a rounded profile and a dark joint under each lower edge."""
    rows, cols = 20, 7; h = np.zeros((size, size)); col = np.zeros((size, size, 3)); rnd = np.random.default_rng(seed); rh, cw = size // rows, size // cols
    yy, xx = np.mgrid[0:size, 0:size]
    for r in range(rows):
        off = (r % 2) * cw // 2
        for c in range(cols + 1):
            x0 = c * cw + off; tone = rnd.uniform(0.85, 1.15)
            ys = slice(r * rh, (r + 1) * rh); xs = np.arange(x0, x0 + cw) % size
            prof = 0.5 + 0.5 * np.sin(np.linspace(0, np.pi, cw))[None, :]; slope = np.linspace(0.0, 1.0, rh)[:, None]
            h[ys, xs] = 0.6 * prof * slope + 0.4 * slope; col[ys, xs] = np.array(base) * tone * (0.75 + 0.35 * slope)[..., None]
    col *= (0.92 + 0.16 * _fbm(size, 6, 3, seed + 2))[..., None]
    return np.clip(col, 0, 1), _normal_from_height(h + 0.1 * _fbm(size, 60, 2, seed + 4), 3.2)


def concrete_maps(size=512, seed=61, base=(0.52, 0.51, 0.48)):
    f = _fbm(size, 5, 4, seed); pores = _fbm(size, 70, 2, seed + 2); col = np.array(base)[None, None, :] * (0.88 + 0.2 * f[..., None] + 0.05 * (pores[..., None] - 0.5)); col[pores < 0.18] *= 0.75
    return np.clip(col, 0, 1), _normal_from_height(pores * 0.7 + f * 0.3, 1.8)


def bark_maps(size=512, seed=71, base=(0.24, 0.17, 0.12)):
    f = _fbm(size, 3, 3, seed); streak = _noise(size, 6, seed + 1)[:1, :] ; ridge = _fbm(size, 24, 3, seed + 3); ridge = ridge * 0.6 + 0.4 * np.tile(_noise(size, 14, seed + 5)[0:1, :], (size, 1))
    col = np.array(base)[None, None, :] * (0.7 + 0.5 * ridge[..., None]); col *= (0.9 + 0.2 * f)[..., None]
    return np.clip(col, 0, 1), _normal_from_height(ridge, 3.0)


def grass_maps(size=512, seed=81, base=(0.20, 0.38, 0.10)):
    """Lawn: broad patches of two greens (one a little yellower), fine blade noise and a few dry-grass flecks; the normal map is the blade noise."""
    patch = _fbm(size, 5, 4, seed); blades = _fbm(size, 110, 2, seed + 2); fine = _noise(size, 200, seed + 4)
    a = np.array(base); b = np.array((0.31, 0.45, 0.13))
    col = (a[None, None, :] * (1 - patch[..., None]) + b[None, None, :] * patch[..., None]) * (0.78 + 0.34 * blades[..., None] + 0.12 * (fine[..., None] - 0.5))
    col[fine > 0.93] = np.array((0.42, 0.40, 0.18))
    return np.clip(col, 0, 1), _normal_from_height(blades * 0.7 + fine * 0.3, 2.2)


def asphalt_normal(size=512, seed=3):
    rnd = np.random.default_rng(seed); h = _fbm(size, 90, 2, seed + 1) * 0.7 + (rnd.random((size, size)) > 0.985) * 0.5
    return _normal_from_height(h, 1.2)


def paver_normal(size=512, tile_m=2.0, seed=7):
    px_m = size / tile_m; w_b, h_b = int(0.20 * px_m), int(0.10 * px_m); h = np.zeros((size, size)); rows = size // h_b
    for r in range(rows):
        off = (r % 2) * (w_b // 2); y0 = r * h_b
        for c in range(-1, size // w_b + 1):
            xs = [(c * w_b + off + k) % size for k in range(1, w_b - 1)]; h[y0 + 1:y0 + h_b - 1, xs] = 1.0
    return _normal_from_height(h + 0.05 * _fbm(size, 80, 2, seed), 2.4)


def _save(name, rgb, linear_data=False):
    h, w, _ = rgb.shape; img = bpy.data.images.get(name)
    if img: return img
    img = bpy.data.images.new(name, w, h, alpha=False)
    data = rgb                                                                             # raw for both: colour maps hold sRGB values, normal maps hold their vectors (see _to_image)
    px = np.concatenate([data, np.ones((h, w, 1))], axis=2).astype(np.float32); img.pixels.foreach_set(px.ravel()); img.pack(); img.filepath_raw = f"//{name}.png"; img.file_format = "PNG"
    img.colorspace_settings.name = "Non-Color" if linear_data else "sRGB"; return img


def pbr_material(key: str, base_name: str, base_rgb, normal_name: str, normal_rgb, normal_strength=1.0, uv_scale_note=None):
    """The kit material M_<key> with a base-colour image and a normal-map image (Non-Color) wired in; the Principled base colour stays the surface colour for the exporters."""
    m = AM.get(key, bpy); nt = m.node_tree; bsdf = nt.nodes["Principled BSDF"]
    for n in list(nt.nodes):
        if n.type in ("TEX_NOISE", "VALTORGB", "TEX_COORD", "TEX_IMAGE", "UVMAP", "NORMAL_MAP"): nt.nodes.remove(n)
    uv = nt.nodes.new("ShaderNodeUVMap"); t1 = nt.nodes.new("ShaderNodeTexImage"); t1.image = _save(base_name, base_rgb); t2 = nt.nodes.new("ShaderNodeTexImage"); t2.image = _save(normal_name, normal_rgb, True)
    nm = nt.nodes.new("ShaderNodeNormalMap"); nm.inputs["Strength"].default_value = normal_strength
    nt.links.new(uv.outputs["UV"], t1.inputs["Vector"]); nt.links.new(uv.outputs["UV"], t2.inputs["Vector"]); nt.links.new(t1.outputs["Color"], bsdf.inputs["Base Color"])
    nt.links.new(t2.outputs["Color"], nm.inputs["Color"]); nt.links.new(nm.outputs["Normal"], bsdf.inputs["Normal"]); return m


def apply_pbr(keys=None):
    """Wires the whole procedural PBR set into whichever kit materials exist in the file."""
    spec = {
        "plaster_cream_light": ("T_kano_plaster_BaseColor", "T_kano_plaster_Normal", lambda: plaster_maps(), 0.5),
        "stone_travertine": ("T_kano_travertine_BaseColor", "T_kano_travertine_Normal", lambda: stone_maps(), 0.5),
        "cladding_grey": ("T_kano_cladding_BaseColor", "T_kano_cladding_Normal", lambda: stone_maps(base=(0.43, 0.43, 0.45), grey=True), 0.5),
        "roof_tile_charcoal": ("T_kano_rooftile_BaseColor", "T_kano_rooftile_Normal", lambda: roof_tile_maps(), 0.5),
        "concrete": ("T_kano_concrete_BaseColor", "T_kano_concrete_Normal", lambda: concrete_maps(), 0.5),
        "bark": ("T_kano_bark_BaseColor", "T_kano_bark_Normal", lambda: bark_maps(), 0.5),
        "grass_green": ("T_kano_grass_BaseColor", "T_kano_grass_Normal", lambda: grass_maps(), 0.5),
    }
    done = []
    for key, (bn, nn, gen, _s) in spec.items():
        if bpy.data.materials.get(f"M_{key}") is None or (keys and key not in keys): continue
        base, nrm = gen(); pbr_material(key, bn, base, nn, nrm); done.append(key)
    for key, bn, norm in (("paver_mix", "T_kano_paver_BaseColor", paver_normal), ("asphalt", "T_kano_asphalt_BaseColor", asphalt_normal)):
        m = bpy.data.materials.get(f"M_{key}")
        if m is None or not m.use_nodes: continue
        nt = m.node_tree; bsdf = nt.nodes["Principled BSDF"]; nn = f"T_kano_{'paver' if key == 'paver_mix' else 'asphalt'}_Normal"
        if any(n.type == "NORMAL_MAP" for n in nt.nodes): continue
        uvn = [n for n in nt.nodes if n.type == "UVMAP"]
        if not uvn: continue
        t2 = nt.nodes.new("ShaderNodeTexImage"); t2.image = _save(nn, norm(), True); nm = nt.nodes.new("ShaderNodeNormalMap")
        nt.links.new(uvn[0].outputs["UV"], t2.inputs["Vector"]); nt.links.new(t2.outputs["Color"], nm.inputs["Color"]); nt.links.new(nm.outputs["Normal"], bsdf.inputs["Normal"]); done.append(key)
    return done
