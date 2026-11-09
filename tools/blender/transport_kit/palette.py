"""PALETTE MATERIALS for the transport kit. The approved breakdown caps every asset at 4 materials (draw-call budget, 150 mobile / 400 desktop in view); the flat colours of the kit would otherwise be 10 to 13 materials
per building. As for residential v003 (kano_kit.palette) every flat-colour surface is folded into one of three shared materials, a small texture of constant-colour cells: `tk_paint` (non-metal, rough 0.7), `tk_metal`
(metallic 0.55, rough 0.55: a preview render showed fully metallic steel nearly black against a plain sky, so the kit's steel is a little less metallic than kano_metal) and `tk_glass` (transparent). A face keeps its identity by its cell: its UV is the cell centre (a constant, so nothing tiles or bleeds). Surfaces with an image of their own (`concrete`)
stay separate, so an asset has at most paint + metal + glass + concrete = 4 materials. The cell colours are exactly the colours of the library entries they replace; what is shared is one roughness and one metallic
value per family, a small documented compromise (container paint and steel sheet read slightly rougher than metal, rubber and lantern glass slightly smoother)."""
from __future__ import annotations
import numpy as np
import bpy
from aro_blender import materials as AM
from . import common as C                                                  # noqa: F401  (registers the additive flat materials before the palettes read their colours)

GRID, CELL = 8, 16; SIZE = GRID * CELL                                     # 8 x 8 cells of 16 px: 128 x 128
FAMILIES = {
    "tk_paint": ["paint_white", "kerb_black", "kerb_white", "road_white", "road_yellow", "paint_red", "sign_blue", "sign_green", "sign_yellow", "warning_yellow", "lantern_glow", "rubber", "concrete_dark", "insulator_brown",
                 "interior_dark", "plaster_cream", "plaster_white", "plaster_ochre", "sheet_cream", "sheet_green", "board_dark", "signal_red", "signal_amber", "signal_green", "container_blue", "container_red",
                 "container_green", "container_grey", "wood_door", "timber_weathered", "burlap", "fabric_red", "fabric_white", "pavement_grey", "hedge_green", "leaf_green", "leaf_green_dark", "flower_red", "flower_bougainvillea",
                 "laterite", "dry_earth", "mud_brick", "tarp_olive", "roof_tile_red", "timber_light", "plaster_terracotta", "paint_blue_bus", "paint_orange", "tile_floor"],
    "tk_metal": ["metal_galvanised", "metal_gate_black", "metal_gate_blue", "frame_aluminium_dark", "shutter_steel", "roof_zinc", "roof_zinc_rust"],
    "tk_glass": ["glass_dark", "glass"],
}
NEW = {"timber_light": ((0.62, 0.46, 0.28), 0.8, 0.0, 0.0, "light sawn timber (benches, slats)"),
       "plaster_terracotta": ((0.62, 0.36, 0.24), 0.9, 0.0, 0.0, "terracotta painted render (brise-soleil, accent walls)"),
       "paint_blue_bus": ((0.10, 0.30, 0.55), 0.55, 0.0, 0.0, "generic blue bus-station paint (no brand)"),
       "paint_orange": ((0.88, 0.42, 0.08), 0.55, 0.0, 0.0, "generic orange paint (route panels, minibus stand)"),
       "tile_floor": ((0.66, 0.60, 0.52), 0.7, 0.0, 0.0, "light concourse floor tile")}
for _k, _v in NEW.items(): AM.LIBRARY.setdefault(_k, _v)
AM.LIBRARY.setdefault("tk_paint", ((0.80, 0.80, 0.78), 0.70, 0.0, 0.0, "transport kit palette of non-metal flat colours (colours of the library entries it folds)"))
AM.LIBRARY.setdefault("tk_metal", ((0.45, 0.45, 0.47), 0.55, 0.55, 0.0, "transport kit palette of metals (galvanised, black and blue painted steel, aluminium, shutter steel, roof sheet)"))
AM.LIBRARY.setdefault("tk_glass", ((0.30, 0.40, 0.45), 0.05, 0.0, 0.0, "transport kit palette of glass: dark and pale; alpha is the cell alpha times the material alpha 0.42"))
TEXTURE_NAME = {"tk_paint": "T_tk_paint_Palette", "tk_metal": "T_tk_metal_Palette", "tk_glass": "T_tk_glass_Palette"}
for _f, _t in TEXTURE_NAME.items(): AM.TEXTURED.setdefault(_f, {"texture": _t})
AM.ALPHA.setdefault("tk_glass", 0.42)
GLASS_CELL_ALPHA = {"glass_dark": 1.0, "glass": round(AM.ALPHA["glass"] / AM.ALPHA["glass_dark"], 3)}
TARGET = {k: fam for fam, ks in FAMILIES.items() for k in ks}
CELL_OF = {k: i for ks in FAMILIES.values() for i, k in enumerate(ks)}
for _fam, _ks in FAMILIES.items(): assert len(_ks) <= GRID * GRID and len(set(_ks)) == len(_ks), _fam
for _k in TARGET: assert _k in AM.LIBRARY, f"palette entry {_k} is not in the material library"


def cell_uv(key: str) -> tuple[float, float]:
    i = CELL_OF[key]; return ((i % GRID + 0.5) / GRID, (i // GRID + 0.5) / GRID)


def palettize(part) -> dict:
    """Moves every face whose material is in FAMILIES onto its family material with the UV of its cell; rebuilds part.keys so that only used keys remain. Returns {family: faces moved}."""
    bm = part.bm; uvl = bm.loops.layers.uv.verify(); moved: dict = {}
    if not hasattr(part, "_fixed"): part._fixed = set()
    old_keys = list(part.keys); new_keys: list[str] = []; face_key: dict = {}
    for f in bm.faces:
        k = old_keys[f.material_index]
        if k in TARGET:
            u, v = cell_uv(k)
            for l in f.loops: l[uvl].uv = (u, v)
            part._fixed.add(f); fam = TARGET[k]; moved[fam] = moved.get(fam, 0) + 1; key2 = fam
        else: key2 = k
        face_key[f] = key2
        if key2 not in new_keys: new_keys.append(key2)
    for f in bm.faces: f.material_index = new_keys.index(face_key[f])
    part.keys = new_keys; return moved


def _pixels(family: str) -> np.ndarray:
    px = np.zeros((SIZE, SIZE, 4)); px[..., 3] = 1.0
    for i in range(GRID * GRID):
        cx, cy = i % GRID, i // GRID; ks = FAMILIES[family]
        col = AM.LIBRARY[ks[i]][0] if i < len(ks) else (0.5, 0.5, 0.5)                                        # unused cells: neutral grey so a stray UV is visible but harmless
        px[cy * CELL:(cy + 1) * CELL, cx * CELL:(cx + 1) * CELL, :3] = col
        if family == "tk_glass" and i < len(ks): px[cy * CELL:(cy + 1) * CELL, cx * CELL:(cx + 1) * CELL, 3] = GLASS_CELL_ALPHA[ks[i]]
    return px


def apply_palettes() -> list[str]:
    """Wires the palette texture into whichever of the three family materials exist in the file."""
    done = []
    for fam, name in TEXTURE_NAME.items():
        if bpy.data.materials.get(f"M_{fam}") is None: continue
        m = AM.get(fam, bpy); nt = m.node_tree; bsdf = nt.nodes["Principled BSDF"]
        for n in list(nt.nodes):
            if n.type in ("TEX_NOISE", "VALTORGB", "TEX_COORD", "TEX_IMAGE", "UVMAP", "MATH"): nt.nodes.remove(n)
        img = bpy.data.images.get(name)
        if img is None:
            img = bpy.data.images.new(name, SIZE, SIZE, alpha=True)
            img.pixels.foreach_set(_pixels(fam).astype(np.float32).ravel()); img.alpha_mode = "STRAIGHT"; img.pack(); img.filepath_raw = f"//{name}.png"; img.file_format = "PNG"; img.colorspace_settings.name = "sRGB"
        uv = nt.nodes.new("ShaderNodeUVMap"); tex = nt.nodes.new("ShaderNodeTexImage"); tex.image = img; tex.interpolation = "Closest"; tex.extension = "CLIP"
        nt.links.new(uv.outputs["UV"], tex.inputs["Vector"]); nt.links.new(tex.outputs["Color"], bsdf.inputs["Base Color"])
        if fam == "tk_glass":
            mul = nt.nodes.new("ShaderNodeMath"); mul.operation = "MULTIPLY"; mul.inputs[1].default_value = AM.ALPHA[fam]
            nt.links.new(tex.outputs["Alpha"], mul.inputs[0]); nt.links.new(mul.outputs["Value"], bsdf.inputs["Alpha"])
        done.append(fam)
    return done
