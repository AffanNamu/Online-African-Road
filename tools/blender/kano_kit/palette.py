"""PALETTE MATERIALS for residential kit v003 (owner decision of 2026-11-03: reduce the number of materials, and so of draw calls, without degrading the look).

Flat-colour surfaces that v002 gave a material each (white paint, dark interiors, kerb paint, aluminium, galvanised and black steel, window and balcony glass, ...) are folded into three shared
materials, each a small texture of constant-colour cells: `kano_paint` (non-metal), `kano_metal` (metal) and `kano_glass` (transparent). A face keeps its identity by its cell: its UV is the cell centre
(a constant, so nothing tiles, blurs or bleeds through the mip chain), and it is kept out of Part.uv_world like a foliage card. The cell colours are exactly the colours the v002 materials had, so the look
is the same; what is shared is one roughness and one metallic value per family (white paint 0.6, dark interior 0.9, insulator 0.35 become one 0.7; the three steels become one 0.8 metallic, 0.45 rough), a
small, documented compromise.

`palettize(part)` is opt-in and works on a finished Part before to_object, so the v002 builders and their byte-identical outputs are untouched.
Run nothing from here; `kano_kit.build3` uses it."""
from __future__ import annotations
import numpy as np
import bpy
from aro_blender import materials as AM

GRID = 4; CELL = 16; SIZE = GRID * CELL                                    # 4 x 4 cells of 16 px: 64 x 64
FAMILIES = {                                                                # family material -> ordered original keys (cell index = position)
    "kano_paint": ["paint_white", "interior_dark", "lantern_glow", "kerb_black", "kerb_white", "road_white", "sign_blue", "insulator_brown", "concrete_dark", "rubber", "paint_red", "road_yellow", "interior_warm"],
    "kano_metal": ["frame_aluminium_dark", "metal_galvanised", "metal_gate_black", "metal_gate_blue"],
    "kano_glass": ["glass_dark", "glass"],
}
GLASS_CELL_ALPHA = {"glass_dark": 1.0, "glass": round(AM.ALPHA["glass"] / AM.ALPHA["glass_dark"], 3)}      # x the material alpha 0.42: 0.42 and 0.22 as in v002
TARGET = {k: fam for fam, ks in FAMILIES.items() for k in ks}               # original key -> family
CELL_OF = {k: i for ks in FAMILIES.values() for i, k in enumerate(ks)}      # original key -> cell index inside its family
TEXTURE_NAME = {"kano_paint": "T_kano_paint_Palette", "kano_metal": "T_kano_metal_Palette", "kano_glass": "T_kano_glass_Palette"}


def cell_uv(key: str) -> tuple[float, float]:
    i = CELL_OF[key]; return ((i % GRID + 0.5) / GRID, (i // GRID + 0.5) / GRID)


def palettize(part, only: set | None = None) -> dict:
    """Moves every face whose material is in FAMILIES onto its family material with the UV of its cell; rebuilds part.keys so that only used keys remain. Returns {family: faces moved}."""
    bm = part.bm; uvl = bm.loops.layers.uv.verify(); moved: dict = {}
    if not hasattr(part, "_fixed"): part._fixed = set()
    old_keys = list(part.keys); new_keys: list[str] = []; face_key: dict = {}
    for f in bm.faces:
        k = old_keys[f.material_index]
        if k in TARGET and (only is None or k in only):
            u, v = cell_uv(k)
            for l in f.loops: l[uvl].uv = (u, v)
            part._fixed.add(f); fam = TARGET[k]; moved[fam] = moved.get(fam, 0) + 1; key2 = fam
        else: key2 = k
        face_key[f] = key2
        if key2 not in new_keys: new_keys.append(key2)
    for f in bm.faces: f.material_index = new_keys.index(face_key[f])
    part.keys = new_keys; return moved


def _palette_pixels(family: str) -> np.ndarray:
    px = np.zeros((SIZE, SIZE, 4)); px[..., 3] = 1.0
    for i, k in enumerate(FAMILIES[family]):
        cx, cy = i % GRID, i // GRID; px[cy * CELL:(cy + 1) * CELL, cx * CELL:(cx + 1) * CELL, :3] = AM.LIBRARY[k][0]
        if family == "kano_glass": px[cy * CELL:(cy + 1) * CELL, cx * CELL:(cx + 1) * CELL, 3] = GLASS_CELL_ALPHA[k]
    for i in range(len(FAMILIES[family]), GRID * GRID):                        # unused cells: neutral grey so a stray UV is visible but harmless
        cx, cy = i % GRID, i // GRID; px[cy * CELL:(cy + 1) * CELL, cx * CELL:(cx + 1) * CELL, :3] = 0.5
    return px


def apply_palettes() -> list[str]:
    """Wires the palette texture into whichever of the three family materials exist in the file (as textures.apply_pbr does for the other kit materials)."""
    done = []
    for fam, name in TEXTURE_NAME.items():
        if bpy.data.materials.get(f"M_{fam}") is None: continue
        m = AM.get(fam, bpy); nt = m.node_tree; bsdf = nt.nodes["Principled BSDF"]
        for n in list(nt.nodes):
            if n.type in ("TEX_NOISE", "VALTORGB", "TEX_COORD", "TEX_IMAGE", "UVMAP", "MATH"): nt.nodes.remove(n)
        img = bpy.data.images.get(name)
        if img is None:
            px = _palette_pixels(fam); img = bpy.data.images.new(name, SIZE, SIZE, alpha=True)
            img.pixels.foreach_set(px.astype(np.float32).ravel()); img.alpha_mode = "STRAIGHT"; img.pack(); img.filepath_raw = f"//{name}.png"; img.file_format = "PNG"; img.colorspace_settings.name = "sRGB"
        uv = nt.nodes.new("ShaderNodeUVMap"); tex = nt.nodes.new("ShaderNodeTexImage"); tex.image = img; tex.interpolation = "Closest"; tex.extension = "CLIP"
        nt.links.new(uv.outputs["UV"], tex.inputs["Vector"]); nt.links.new(tex.outputs["Color"], bsdf.inputs["Base Color"])
        if fam == "kano_glass":
            mul = nt.nodes.new("ShaderNodeMath"); mul.operation = "MULTIPLY"; mul.inputs[1].default_value = AM.ALPHA[fam]
            nt.links.new(tex.outputs["Alpha"], mul.inputs[0]); nt.links.new(mul.outputs["Value"], bsdf.inputs["Alpha"])
        done.append(fam)
    return done
