"""The ARO material library: named PBR (Principled) materials, created on demand, always called M_<surface> (the shared-kit naming the owner approved).
Colours below are sRGB (what you would type as a hex colour); they are converted to the linear values Blender and the exporters work with. First-pass estimates for West African
roadside surfaces, meant to be tuned against references and baked to textures (docs/BLENDER_PIPELINE.md); an optional `variation` adds a procedural colour break-up in Blender so large flat
surfaces do not read as plastic. The Principled node's own base colour is ALWAYS set to the surface colour, even when the break-up is wired in, because the FBX and glTF exporters read that value
for a material with no image texture (an unset value exports as default grey). Nothing here uses the Standard shader or a custom shader."""
from __future__ import annotations


def srgb_to_linear(c: float) -> float:
    return c / 12.92 if c <= 0.04045 else ((c + 0.055) / 1.055) ** 2.4

# key: (base colour sRGB 0-1, roughness, metallic, variation 0-1, note)
LIBRARY: dict[str, tuple[tuple[float, float, float], float, float, float, str]] = {
    "asphalt":           ((0.045, 0.045, 0.05), 0.92, 0.0, 0.35, "wearing road surface"),
    "asphalt_worn":      ((0.09, 0.09, 0.095), 0.95, 0.0, 0.5, "old, bleached asphalt"),
    "concrete":          ((0.52, 0.51, 0.48), 0.88, 0.0, 0.4, "poured concrete, kerbs, slabs"),
    "concrete_dark":     ((0.30, 0.30, 0.29), 0.9, 0.0, 0.4, "stained concrete, drains"),
    "plaster_cream":     ((0.78, 0.70, 0.55), 0.9, 0.0, 0.3, "sand-cement render painted cream"),
    "plaster_ochre":     ((0.66, 0.45, 0.22), 0.92, 0.0, 0.35, "ochre painted render"),
    "plaster_white":     ((0.82, 0.80, 0.74), 0.9, 0.0, 0.3, "whitewashed render"),
    "plaster_green":     ((0.35, 0.50, 0.38), 0.9, 0.0, 0.3, "green painted render"),
    "laterite":          ((0.42, 0.20, 0.11), 0.95, 0.0, 0.5, "red laterite earth, unpaved shoulders, mud walls"),
    "dry_earth":         ((0.55, 0.40, 0.25), 0.97, 0.0, 0.5, "dusty ground"),
    "mud_brick":         ((0.50, 0.33, 0.20), 0.96, 0.0, 0.5, "adobe / sun-dried brick"),
    "block_wall":        ((0.58, 0.55, 0.50), 0.9, 0.0, 0.4, "unplastered sandcrete blocks"),
    "roof_zinc":         ((0.55, 0.57, 0.58), 0.45, 0.85, 0.25, "new corrugated zinc sheet"),
    "roof_zinc_rust":    ((0.40, 0.22, 0.12), 0.7, 0.6, 0.6, "rusted corrugated sheet"),
    "roof_tile_red":     ((0.52, 0.22, 0.14), 0.8, 0.0, 0.35, "clay roof tile"),
    "metal_gate_blue":   ((0.10, 0.22, 0.45), 0.5, 0.7, 0.25, "painted steel gate"),
    "metal_gate_black":  ((0.03, 0.03, 0.035), 0.55, 0.8, 0.2, "black painted steel"),
    "metal_galvanised":  ((0.60, 0.62, 0.64), 0.4, 0.9, 0.2, "galvanised steel, poles, rails"),
    "timber_pole":       ((0.30, 0.20, 0.12), 0.9, 0.0, 0.45, "treated timber pole"),
    "wood_door":         ((0.28, 0.16, 0.08), 0.7, 0.0, 0.35, "painted/varnished door timber"),
    "glass":             ((0.55, 0.70, 0.75), 0.05, 0.0, 0.0, "window glass (transmission added at export tuning)"),
    "road_white":        ((0.80, 0.80, 0.78), 0.8, 0.0, 0.3, "road marking, white"),
    "road_yellow":       ((0.80, 0.60, 0.05), 0.8, 0.0, 0.3, "road marking, yellow"),
    "rubber":            ((0.02, 0.02, 0.02), 0.9, 0.0, 0.0, "tyres, rubber"),
    "bark":              ((0.22, 0.15, 0.10), 0.95, 0.0, 0.5, "tree trunk"),
    "leaf_green":        ((0.12, 0.30, 0.08), 0.75, 0.0, 0.5, "neem / mango foliage"),
    "grass_dry":         ((0.50, 0.45, 0.20), 0.95, 0.0, 0.55, "dry savanna grass"),
    "paint_red":         ((0.55, 0.05, 0.04), 0.6, 0.0, 0.2, "red paint, signs"),
    "paint_white":       ((0.85, 0.85, 0.83), 0.6, 0.0, 0.15, "white paint"),
    "sign_green":        ((0.04, 0.35, 0.18), 0.5, 0.0, 0.1, "highway direction sign"),
    # --- added for the Kano residential street kit (concept R-006): sRGB read from the supplied concept by eye, to be tuned against in-game screenshots
    "plaster_cream_light": ((0.86, 0.78, 0.64), 0.9, 0.0, 0.25, "pale cream sand-cement render, villa and compound walls"),
    "cladding_grey":     ((0.43, 0.43, 0.45), 0.8, 0.0, 0.3, "dark grey stone-effect cladding panels"),
    "stone_travertine":  ((0.64, 0.50, 0.39), 0.85, 0.0, 0.35, "tan stone cladding on gate piers"),
    "roof_tile_charcoal": ((0.17, 0.17, 0.19), 0.7, 0.0, 0.3, "dark grey concrete roof tile"),
    "frame_aluminium_dark": ((0.10, 0.10, 0.11), 0.4, 0.7, 0.1, "dark aluminium window and door frames"),
    "glass_dark":        ((0.13, 0.18, 0.23), 0.05, 0.0, 0.0, "tinted window glass seen from outside"),
    "kerb_black":        ((0.05, 0.05, 0.055), 0.8, 0.0, 0.2, "painted kerb block, black"),
    "kerb_white":        ((0.84, 0.84, 0.82), 0.8, 0.0, 0.2, "painted kerb block, white"),
    "paver_mix":         ((0.62, 0.48, 0.44), 0.85, 0.0, 0.0, "interlocking paver sidewalk (pink, grey, beige); the Blender file carries a texture"),
    "grass_green":       ((0.20, 0.42, 0.10), 0.95, 0.0, 0.4, "green verge grass"),
    "hedge_green":       ((0.10, 0.32, 0.07), 0.9, 0.0, 0.3, "clipped hedge"),
    "leaf_green_light":  ((0.27, 0.48, 0.12), 0.75, 0.0, 0.0, "sunlit foliage"),
    "leaf_green_dark":   ((0.07, 0.22, 0.05), 0.8, 0.0, 0.0, "shaded foliage"),
    "flower_red":        ((0.72, 0.07, 0.05), 0.6, 0.0, 0.0, "red flowering shrub (ixora)"),
    "sign_blue":         ((0.04, 0.17, 0.62), 0.5, 0.0, 0.0, "regulatory sign face, blue"),
    "insulator_brown":   ((0.42, 0.22, 0.10), 0.35, 0.0, 0.0, "glazed porcelain insulator"),
    "leaf_cutout":       ((0.20, 0.38, 0.10), 0.8, 0.0, 0.0, "foliage cards: an RGBA leaf-cluster texture, alpha clipped at 0.5, drawn on both sides (the kit material must be cutout)"),
    "lantern_glow":      ((0.95, 0.76, 0.42), 0.4, 0.0, 0.0, "warm lantern glass"),
}


def keys() -> list[str]:
    return sorted(LIBRARY)


# Surfaces that carry an image on top of the flat colour: key -> (texture name, cutout alpha threshold or 0, two sided). The texture files are written next to the model at export time.
TEXTURED: dict[str, tuple[str, float, bool]] = {
    "paver_mix": ("T_kano_paver_BaseColor", 0.0, False),
    "asphalt": ("T_kano_asphalt_BaseColor", 0.0, False),
    "leaf_cutout": ("T_kano_leaves_BaseColor", 0.5, True),
}


def kit_json() -> dict:
    """The material kit as data for the Unity side: the same sRGB colours, roughness and metallic the library uses in Blender, so the Unity materials (URP/Lit, GPU instancing on) and the Blender renders agree by construction.
    A textured surface also names its base-colour texture, an alpha-clip threshold (foliage cards) and whether it is drawn on both sides."""
    out = []
    for k, v in sorted(LIBRARY.items()):
        e = {"name": f"M_{k}", "base_color_srgb": list(v[0]), "roughness": v[1], "metallic": v[2], "note": v[4], "texture": "", "cutout": 0.0, "two_sided": False}
        if k in TEXTURED: e["texture"], e["cutout"], e["two_sided"] = TEXTURED[k]
        out.append(e)
    return {"schema": 2, "materials": out}


def describe() -> list[dict]:
    return [{"key": k, "name": f"M_{k}", "base_color": list(v[0]), "roughness": v[1], "metallic": v[2], "variation": v[3], "note": v[4]} for k, v in sorted(LIBRARY.items())]


def get(key: str, bpy):
    """The material M_<key>, created if the file does not have it yet. Unknown keys raise KeyError listing the valid ones."""
    if key not in LIBRARY: raise KeyError(f"unknown material '{key}'. Library: {', '.join(keys())}")
    name = f"M_{key}"
    mat = bpy.data.materials.get(name)
    if mat: return mat
    (sr, sg, sb), rough, metal, var, _ = LIBRARY[key]
    r, g, b = srgb_to_linear(sr), srgb_to_linear(sg), srgb_to_linear(sb)
    mat = bpy.data.materials.new(name)
    try: mat.use_nodes = True
    except Exception: pass
    nt = mat.node_tree; bsdf = nt.nodes.get("Principled BSDF") or nt.nodes.new("ShaderNodeBsdfPrincipled")
    bsdf.inputs["Roughness"].default_value = rough; bsdf.inputs["Metallic"].default_value = metal
    bsdf.inputs["Base Color"].default_value = (r, g, b, 1.0)          # always: this is the value the exporters read
    if var > 0.0:
        coord = nt.nodes.new("ShaderNodeTexCoord"); noise = nt.nodes.new("ShaderNodeTexNoise"); ramp = nt.nodes.new("ShaderNodeValToRGB")
        noise.inputs["Scale"].default_value = 6.0; noise.inputs["Detail"].default_value = 8.0; noise.inputs["Roughness"].default_value = 0.65
        dark = (r * (1 - 0.45 * var), g * (1 - 0.45 * var), b * (1 - 0.45 * var), 1.0); light = (min(1, r * (1 + 0.25 * var)), min(1, g * (1 + 0.25 * var)), min(1, b * (1 + 0.25 * var)), 1.0)
        ramp.color_ramp.elements[0].color = dark; ramp.color_ramp.elements[1].color = light
        nt.links.new(coord.outputs["Object"], noise.inputs["Vector"]); nt.links.new(noise.outputs["Fac"], ramp.inputs["Fac"]); nt.links.new(ramp.outputs["Color"], bsdf.inputs["Base Color"])
    mat.diffuse_color = (r, g, b, 1.0)          # viewport colour (linear), so the solid-shaded preview shows the right hue
    mat["aro_srgb"] = [sr, sg, sb]               # the authoring colour, kept for checks
    return mat
