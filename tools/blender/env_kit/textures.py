"""Procedural PBR texture sets (sRGB base colour + tangent-space normal, 512 px, tileable) for the environmental kit: mud, compacted earth, sand, cracked asphalt, corrugated sheet, mud brick,
weathered plaster, weathered timber and water ripples. They SUPPORT the geometry (ruts, potholes, brick courses are modelled), never replace it. Colours are first-pass estimates read from the
generated references R-016 to R-023; the soil and mud colour of Kano is not established (gap M-15). Values are sRGB, stored as they are (see kano_kit.textures._to_image for why)."""
from __future__ import annotations
import numpy as np
import bpy
from kano_kit import textures as KT
from kano_kit.textures import _fbm, _noise, _normal_from_height, pbr_material

SIZE = 512


def _grid(size=SIZE):
    y, x = np.mgrid[0:size, 0:size].astype(float); return x / size, y / size


def mud_maps(wet=True, size=SIZE, seed=101):
    """Clay mud: rounded clods and drag marks. Wet: dark, glossy troughs; crest: lighter, drier, cracked clods."""
    f = _fbm(size, 5, 5, seed); g = _fbm(size, 18, 3, seed + 3); ridge = 1 - np.abs(2 * _fbm(size, 7, 4, seed + 7) - 1)
    h = 0.55 * f + 0.25 * g + 0.30 * ridge
    base = np.array((0.34, 0.19, 0.12) if wet else (0.52, 0.33, 0.20))
    col = base[None, None, :] * (0.72 + 0.55 * h[..., None]) * (0.95 + 0.1 * (g[..., None] - 0.5))
    if not wet:
        cr = np.abs(_fbm(size, 9, 3, seed + 11) - 0.5) < 0.018; col[cr] *= 0.55                   # dry cracks between clods
    else:
        col *= (0.9 + 0.12 * np.sin(h * 9.0))[..., None]
    return np.clip(col, 0, 1), _normal_from_height(h, 3.4 if wet else 4.2)


def earth_maps(size=SIZE, seed=111):
    """Compacted laterite earth with fine grit, scattered pebbles and faint wheel scuffing."""
    rnd = np.random.default_rng(seed); f = _fbm(size, 6, 4, seed); grit = _fbm(size, 90, 2, seed + 2); h = 0.6 * f + 0.4 * grit
    col = np.array((0.60, 0.42, 0.27))[None, None, :] * (0.82 + 0.3 * f[..., None] + 0.08 * (grit[..., None] - 0.5))
    for _ in range(260):                                                                         # pebbles: small lighter stones with a rim
        cx, cy = rnd.integers(0, size, 2); r = int(rnd.integers(2, 6)); yy, xx = np.ogrid[-r:r + 1, -r:r + 1]; m = (xx * xx + yy * yy) <= r * r
        ys = (np.arange(cy - r, cy + r + 1) % size)[:, None]; xs = (np.arange(cx - r, cx + r + 1) % size)[None, :]
        tone = rnd.uniform(0.9, 1.25); col[ys, xs][m] = col[ys, xs][m] * 0 + np.array((0.66, 0.58, 0.48)) * tone; h[ys, xs] = np.where(m, h[ys, xs] + 0.35, h[ys, xs])
    return np.clip(col, 0, 1), _normal_from_height(h, 2.6)


def sand_maps(size=SIZE, seed=121):
    """Loose dusty sand: fine grain and wind ripples."""
    x, y = _grid(size); f = _fbm(size, 4, 4, seed); grain = _noise(size, 220, seed + 1); warp = _fbm(size, 3, 3, seed + 5)
    ripple = 0.5 + 0.5 * np.sin((y * 38 + warp * 6.0) * np.pi); h = 0.55 * ripple + 0.3 * f + 0.15 * grain
    col = np.array((0.76, 0.64, 0.46))[None, None, :] * (0.88 + 0.16 * f[..., None] + 0.06 * (grain[..., None] - 0.5) + 0.05 * (ripple[..., None] - 0.5))
    return np.clip(col, 0, 1), _normal_from_height(h, 1.3)


def asphalt_cracked_maps(size=SIZE, seed=131):
    """Dry asphalt: aggregate speckle, oxidised pale patches, a network of cracks (thin dark lines, recessed in the normal map) and alligator cracking in one area."""
    rnd = np.random.default_rng(seed); f = _fbm(size, 4, 4, seed); agg = _noise(size, 170, seed + 1)
    col = np.array((0.24, 0.24, 0.25))[None, None, :] * (0.80 + 0.35 * f[..., None] + 0.20 * (agg[..., None] - 0.5)); h = 0.3 * agg + 0.1 * f
    n1 = _fbm(size, 5, 3, seed + 2); n2 = _fbm(size, 9, 3, seed + 3); n3 = _fbm(size, 22, 3, seed + 4)
    crack = (np.abs(n1 - 0.5) < 0.010) | (np.abs(n2 - 0.5) < 0.008)
    x, y = _grid(size); zone = (_fbm(size, 2, 2, seed + 9) > 0.55); crack |= zone & (np.abs(n3 - 0.5) < 0.030)         # alligator cracking: a dense network inside one zone
    col[crack] = np.array((0.05, 0.05, 0.055)); h[crack] -= 0.6
    pale = _fbm(size, 3, 3, seed + 6) > 0.62; col[pale] *= 1.18
    return np.clip(col, 0, 1), _normal_from_height(h, 3.0)


def rustsheet_maps(size=SIZE, seed=141):
    """Corrugated sheet roofing, rusted: 26 ridges across the 2 m tile (the usual 76 mm pitch), galvanised grey showing through where the rust is thin, streaks running down the sheet."""
    x, y = _grid(size); prof = 0.5 + 0.5 * np.sin(2 * np.pi * 26 * x); f = _fbm(size, 4, 4, seed); fine = _fbm(size, 60, 2, seed + 2)
    streak = _noise(size, 14, seed + 3)[:1, :]; streak = np.tile(streak, (size, 1)); rust = np.clip((f * 1.25 + 0.35 * (streak - 0.5) + 0.2 * (1 - prof) * 0.5 - 0.30), 0, 1)
    zinc = np.array((0.56, 0.58, 0.58)); rustc = np.array((0.42, 0.22, 0.11)); dark = np.array((0.24, 0.12, 0.07))
    col = zinc[None, None, :] * (1 - rust[..., None]) + rustc[None, None, :] * rust[..., None]; col = col * (1 - 0.35 * (rust[..., None] > 0.7)) + dark[None, None, :] * 0.35 * (rust[..., None] > 0.7)
    col *= (0.85 + 0.25 * prof[..., None]) * (0.95 + 0.1 * (fine[..., None] - 0.5))
    return np.clip(col, 0, 1), _normal_from_height(0.8 * prof + 0.2 * fine, 3.5)


def mudbrick_maps(size=SIZE, seed=151):
    """Sun-dried brick (tubali) wall: courses 0.1 m high, bricks about 0.3 m long and rounded, thick mud mortar, rain erosion streaks and patches of remaining mud render."""
    rnd = np.random.default_rng(seed); rows, per = 20, 7; rh = size // rows; bw = size // per; h = np.zeros((size, size)); col = np.zeros((size, size, 3)); base = np.array((0.50, 0.33, 0.20)); mortar = np.array((0.42, 0.28, 0.18))
    col[:] = mortar
    for r in range(rows):
        off = (r % 2) * bw // 2
        for c in range(per + 1):
            x0 = c * bw + off; tone = rnd.uniform(0.82, 1.12); xs = np.arange(x0 + 3, x0 + bw - 3) % size; ys = np.arange(r * rh + 2, (r + 1) * rh - 2)
            u = np.linspace(-1, 1, len(xs))[None, :]; v = np.linspace(-1, 1, len(ys))[:, None]; bulge = np.clip(1 - (u * u * 0.8 + v * v * 0.9), 0, 1)
            h[np.ix_(ys, xs)] = bulge; col[np.ix_(ys, xs)] = (base * tone)[None, None, :] * (0.85 + 0.2 * bulge[..., None])
    f = _fbm(size, 5, 4, seed); streak = np.tile(_noise(size, 18, seed + 2)[:1, :], (size, 1)); col *= (0.88 + 0.3 * f[..., None]) * (1 - 0.25 * np.clip(streak - 0.5, 0, 1)[..., None] * 2)
    patch = _fbm(size, 3, 3, seed + 4) > 0.64; col[patch] = col[patch] * 0.35 + np.array((0.62, 0.50, 0.36)) * 0.65; h[patch] = h[patch] * 0.4 + 0.2
    return np.clip(col, 0, 1), _normal_from_height(h + 0.1 * _fbm(size, 40, 2, seed + 5), 3.2)


def plasterweathered_maps(size=SIZE, seed=161):
    """Weathered sand-cement render: stains running down from the top, damp darkening low on the wall, and chipped patches showing the block beneath."""
    x, y = _grid(size); f = _fbm(size, 4, 4, seed); fine = _fbm(size, 70, 2, seed + 2); streak = np.tile(_noise(size, 12, seed + 3)[:1, :], (size, 1))
    col = np.array((0.80, 0.70, 0.55))[None, None, :] * (0.88 + 0.2 * f[..., None] + 0.05 * (fine[..., None] - 0.5)); col *= (1 - 0.22 * np.clip(streak - 0.45, 0, 1)[..., None] * 2)
    col *= (1 - 0.18 * np.clip((y - 0.7) / 0.3, 0, 1))[..., None]
    chip = _fbm(size, 6, 4, seed + 5) > 0.67; col[chip] = np.array((0.58, 0.55, 0.50)) * (0.9 + 0.2 * fine[chip][:, None]); h = 0.5 * fine + 0.3 * f; h[chip] -= 0.5
    return np.clip(col, 0, 1), _normal_from_height(h, 2.4)


def timber_maps(size=SIZE, seed=171):
    """Weathered timber: grain running along the texture's v axis, knots and cracks."""
    x, y = _grid(size); f = _fbm(size, 3, 3, seed); warp = _fbm(size, 5, 3, seed + 1); grain = 0.5 + 0.5 * np.sin((x * 70 + warp * 9 + f * 3) * np.pi); fine = _noise(size, 150, seed + 2)
    col = np.array((0.40, 0.30, 0.20))[None, None, :] * (0.72 + 0.4 * grain[..., None] + 0.1 * (fine[..., None] - 0.5)) * (0.92 + 0.18 * f[..., None])
    return np.clip(col, 0, 1), _normal_from_height(0.7 * grain + 0.3 * fine, 2.2)


def water_normal(size=SIZE, seed=181):
    """Small ripples for standing water: a sum of directional waves and a fine noise."""
    x, y = _grid(size); h = np.zeros((size, size)); rnd = np.random.default_rng(seed)
    for _ in range(7): a = rnd.uniform(0, np.pi); k = rnd.integers(6, 22); h += np.sin(2 * np.pi * k * (x * np.cos(a) + y * np.sin(a)) + rnd.uniform(0, 6)) / 7
    return _normal_from_height(h * 0.5 + 0.3 * _fbm(size, 30, 2, seed + 1), 1.0)


def water_color(rgb, size=SIZE, seed=191):
    f = _fbm(size, 5, 3, seed); col = np.array(rgb)[None, None, :] * (0.9 + 0.2 * f[..., None]); return np.clip(col, 0, 1)


DRY_BLADES = [(0.72, 0.62, 0.30), (0.62, 0.52, 0.24), (0.50, 0.46, 0.20), (0.78, 0.70, 0.40), (0.42, 0.40, 0.16)]
WET_BLADES = [(0.30, 0.52, 0.14), (0.22, 0.44, 0.10), (0.38, 0.58, 0.18), (0.18, 0.36, 0.09), (0.46, 0.60, 0.22)]
WET_LEAF = [(0.22, 0.46, 0.13), (0.17, 0.38, 0.10), (0.12, 0.29, 0.07), (0.26, 0.50, 0.15)]               # saturated greens after rain


def grassblade_pixels(size=SIZE, seed=201, pal=None):
    """An RGBA atlas of grass blades (alpha = blade shape): about 46 tapered, slightly curved blades growing from the bottom edge. The default palette is dry (straw, tan, brown-green); WET_BLADES is green."""
    rnd = np.random.default_rng(seed); out = np.zeros((size, size, 4)); yy, xx = np.mgrid[0:size, 0:size].astype(float); v = 1 - yy / size           # v = 0 at the bottom edge
    pal = pal or DRY_BLADES
    for _ in range(46):
        x0 = rnd.uniform(0.06, 0.94) * size; lean = rnd.uniform(-0.22, 0.22) * size; hgt = rnd.uniform(0.55, 0.98); w0 = rnd.uniform(5, 11); col = np.array(pal[rnd.integers(0, len(pal))]) * rnd.uniform(0.85, 1.1)
        t = v / hgt; cx = x0 + lean * t ** 1.7; width = w0 * (1 - t) ** 0.8 + 0.8; m = (t >= 0) & (t <= 1) & (np.abs(xx - cx) < width)
        shade = (0.8 + 0.35 * np.clip(1 - np.abs(xx - cx) / np.maximum(width, 1), 0, 1) + 0.15 * t)[..., None]; out[m, :3] = np.clip(col[None, :] * shade[m], 0, 1); out[m, 3] = 1.0
    return out


def bananaleaf_pixels(size=SIZE, seed=211):
    """An RGBA banana leaf blade for a strip whose u runs across the leaf and v from its base (0, bottom edge) to its tip (1, top edge): a long blade, widest at one third of its length, a pale midrib down the
    middle, lateral veins and the characteristic tears: narrow splits running from the edge inward along the lateral veins, deeper towards the tip. Alpha is the blade shape."""
    rnd = np.random.default_rng(seed); yy, xx = np.mgrid[0:size, 0:size].astype(float); u = xx / size; v = 1 - yy / size
    half = 0.485 * np.sin(np.pi * np.clip(v, 0, 1) ** 0.62) ** 0.9 + 0.012                                  # half width as a function of the position along the blade
    d = np.abs(u - 0.5); inside = (d < half) & (v > 0.0) & (v < 0.995)
    for _ in range(22):                                                                                     # tears: from the edge inward, along a lateral vein (the veins run slightly towards the tip)
        v0 = rnd.uniform(0.18, 0.96); depth = rnd.uniform(0.35, 0.80) * (0.5 + 0.7 * v0); side = rnd.choice((-1, 1)); w = 0.0035
        slope = 0.22; vv = v0 + slope * (d - 0.5 * half)
        cut = (np.abs(v - vv) < w) & (np.sign(u - 0.5) == side) & (d > half * (1 - depth) * 0.9)
        inside &= ~cut
    base = np.array((0.27, 0.48, 0.13)); light = np.array((0.52, 0.66, 0.26)); rib = np.array((0.74, 0.80, 0.46))
    fine = _fbm(size, 8, 3, seed + 1); vein = 0.5 + 0.5 * np.sin((v - 0.22 * d) * 150)
    col = base[None, None, :] * (0.82 + 0.35 * fine[..., None]) * (0.93 + 0.12 * vein[..., None]); col = col * (1 - np.clip(d / np.maximum(half, 0.02), 0, 1) ** 2 * 0.25)[..., None] + light[None, None, :] * (0.18 * np.clip(1 - d / 0.25, 0, 1))[..., None]
    ribm = d < (0.014 * (1.15 - 0.8 * v)); col[ribm] = rib * 0.95
    out = np.zeros((size, size, 4)); out[..., :3] = np.clip(col, 0, 1); out[..., 3] = inside.astype(float)
    return out


def cutout_material(key: str, image_name: str, px: np.ndarray):
    """The kit material M_<key> with an RGBA image wired into base colour and alpha (alpha-clipped cards, drawn on both sides), like kano_kit.textures.leaf_material but for any key and image."""
    from aro_blender import materials as AM
    m = AM.get(key, bpy); nt = m.node_tree; bsdf = nt.nodes["Principled BSDF"]
    for n in list(nt.nodes):
        if n.type in ("TEX_NOISE", "VALTORGB", "TEX_COORD", "TEX_IMAGE", "UVMAP"): nt.nodes.remove(n)
    img = bpy.data.images.get(image_name)
    if img is None:
        h, w, _ = px.shape; img = bpy.data.images.new(image_name, w, h, alpha=True); data = np.concatenate([px[..., :3], px[..., 3:4]], axis=2).astype(np.float32)
        img.pixels.foreach_set(data.ravel()); img.alpha_mode = "STRAIGHT"; img.pack(); img.filepath_raw = f"//{image_name}.png"; img.file_format = "PNG"
    uv = nt.nodes.new("ShaderNodeUVMap"); tex = nt.nodes.new("ShaderNodeTexImage"); tex.image = img; tex.interpolation = "Linear"; tex.extension = "CLIP"
    nt.links.new(uv.outputs["UV"], tex.inputs["Vector"]); nt.links.new(tex.outputs["Color"], bsdf.inputs["Base Color"]); nt.links.new(tex.outputs["Alpha"], bsdf.inputs["Alpha"])
    try: m.surface_render_method = "DITHERED"
    except Exception: pass
    return m


DRY_LEAF = [(0.52, 0.52, 0.20), (0.44, 0.46, 0.17), (0.34, 0.36, 0.13), (0.58, 0.54, 0.24)]       # dusty olive and yellow-green: a tree in the dry season with dust on its leaves


def apply_env_pbr(keys=None) -> list[str]:
    """Wires the environmental texture sets into whichever of the kit materials exist in the file (the same way kano_kit.textures.apply_pbr does for the residential kit)."""
    spec = {
        "mud_wet": ("T_env_mudwet_BaseColor", "T_env_mudwet_Normal", lambda: mud_maps(True)),
        "mud_crest": ("T_env_mudcrest_BaseColor", "T_env_mudcrest_Normal", lambda: mud_maps(False, seed=103)),
        "earth_compacted": ("T_env_earth_BaseColor", "T_env_earth_Normal", earth_maps),
        "sand_dusty": ("T_env_sand_BaseColor", "T_env_sand_Normal", sand_maps),
        "asphalt_cracked": ("T_env_asphaltcracked_BaseColor", "T_env_asphaltcracked_Normal", asphalt_cracked_maps),
        "roof_zinc_rust": ("T_env_rustsheet_BaseColor", "T_env_rustsheet_Normal", rustsheet_maps),
        "mud_brick": ("T_env_mudbrick_BaseColor", "T_env_mudbrick_Normal", mudbrick_maps),
        "plaster_weathered": ("T_env_plasterweathered_BaseColor", "T_env_plasterweathered_Normal", plasterweathered_maps),
        "timber_weathered": ("T_env_timber_BaseColor", "T_env_timber_Normal", timber_maps),
        "water_brown": ("T_env_waterbrown_BaseColor", "T_env_water_Normal", lambda: (water_color((0.31, 0.21, 0.13)), water_normal())),
        "water_rain": ("T_env_waterrain_BaseColor", "T_env_water_Normal", lambda: (water_color((0.30, 0.34, 0.36), seed=193), water_normal())),
    }
    done = []
    if bpy.data.materials.get("M_leaf_cutout") is not None: KT.leaf_material(); done.append("leaf_cutout")                       # the residential leaf atlas, for assets that mix both kits (the bougainvillea)
    if bpy.data.materials.get("M_leaf_dry_cutout") is not None and (not keys or "leaf_dry_cutout" in keys): cutout_material("leaf_dry_cutout", "T_env_leavesdry_BaseColor", KT.leaf_pixels(seed=33, greens=DRY_LEAF)); done.append("leaf_dry_cutout")
    if bpy.data.materials.get("M_grass_blade_cutout") is not None and (not keys or "grass_blade_cutout" in keys): cutout_material("grass_blade_cutout", "T_env_grassblades_BaseColor", grassblade_pixels()); done.append("grass_blade_cutout")
    if bpy.data.materials.get("M_leaf_wet_cutout") is not None and (not keys or "leaf_wet_cutout" in keys): cutout_material("leaf_wet_cutout", "T_env_leaveswet_BaseColor", KT.leaf_pixels(seed=35, greens=WET_LEAF)); done.append("leaf_wet_cutout")
    if bpy.data.materials.get("M_grass_blade_wet_cutout") is not None and (not keys or "grass_blade_wet_cutout" in keys): cutout_material("grass_blade_wet_cutout", "T_env_grassbladeswet_BaseColor", grassblade_pixels(seed=203, pal=WET_BLADES)); done.append("grass_blade_wet_cutout")
    if bpy.data.materials.get("M_banana_leaf_cutout") is not None and (not keys or "banana_leaf_cutout" in keys): cutout_material("banana_leaf_cutout", "T_env_bananaleaf_BaseColor", bananaleaf_pixels()); done.append("banana_leaf_cutout")
    for key, (bn, nn, gen) in spec.items():
        if bpy.data.materials.get(f"M_{key}") is None or (keys and key not in keys): continue
        base, nrm = gen(); pbr_material(key, bn, base, nn, nrm); done.append(key)
    return done
