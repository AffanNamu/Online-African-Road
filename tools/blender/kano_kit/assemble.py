"""The assembled review scene: the street section (96 m) with its plots built from the kit assets by LINKED instances (one mesh per asset, many placements), plus a far context made of the low
LODs so the street does not end in front of the camera. The placement list is also written as JSON for the Unity side, so Unity builds the same arrangement from the same prefabs.
Axes: street along +Y, right side = +X. Kit assets face -Y; a right-side plot is rotated -90 degrees about Z so its front faces -X (the street), a left-side plot +90 degrees."""
from __future__ import annotations
import math
import bpy
from mathutils import Vector, Matrix, Euler
from . import street, walls, villa, lights, veg
from .geo import Part, Asset

SECTION = street.LEN; PLOT = 24.0; PITCH = 4.8; PIER_X = 7.525          # wall centre line |x|
PANEL_LEN = PITCH - 0.55
FACE = {1: -90.0, -1: 90.0}                                               # side -> yaw so the asset front (-Y) faces the street


class Placements:
    def __init__(self): self.items = []; self.templates = {}; self.coll = bpy.context.scene.collection
    def template(self, key, part_fn, lod):
        k = (key, lod)
        if k not in self.templates:
            o = part_fn(lod).to_object(f"tpl_{key}_lod{lod}"); self.coll.objects.unlink(o); self.templates[k] = o
        return self.templates[k]
    def add(self, key, part_fn, x, y, yaw_deg=0.0, z=0.0, lod=0, scale=1.0, tag="", record=True):
        t = self.template(key, part_fn, lod); o = bpy.data.objects.new(f"{key}_{len(self.items):04d}", t.data); self.coll.objects.link(o)
        o.location = (x, y, z); o.rotation_euler = Euler((0, 0, math.radians(yaw_deg))); o.scale = (scale,) * 3
        if record: self.items.append({"asset": key, "x": round(x, 3), "y": round(y, 3), "z": round(z, 3), "yawDeg": round(yaw_deg, 2), "scale": scale, "lod": lod, "tag": tag})   # Unity places the same prefab: its LODGroup picks the level by distance
        return o


def gate_bays(plot_index: int, side: int) -> int:
    return 2 if (plot_index + (0 if side > 0 else 1)) % 2 == 0 else 3


def build_section(P: Placements, y_center: float, far: bool = False, detail_lod: int = 0):
    """One 96 m section centred on y_center. `far` uses LOD1/LOD2 and skips small details."""
    L0 = 0 if not far else 2
    P.add("kano_street_oneway_section_a", street.build, 0, y_center, 0, lod=(0 if not far else 1))
    y_start = y_center - SECTION / 2; n_plots = int(SECTION / PLOT)
    for side in (1, -1):
        yaw = FACE[side]
        for k in range(n_plots):
            y0 = y_start + k * PLOT; yc = y0 + PLOT / 2; g = gate_bays(k, side); gate_y = y0 + (g + 0.5) * PITCH
            if not far:
                # compound wall: five bays with a pier at every bay line; the gate bay holds the gate instead of a panel
                for b in range(5):
                    by = y0 + (b + 0.5) * PITCH
                    if b == g: P.add("aro_gate_ornate_steel_a", lambda lod: walls.gate(lod, width=PANEL_LEN - 0.25), side * PIER_X, by, yaw, tag="gate")
                    else: P.add("aro_wall_compound_panel_a", lambda lod: walls.wall_panel(lod, PANEL_LEN), side * PIER_X, by, yaw)
                for b in range(5):
                    P.add("aro_pier_stone_a", walls.pier, side * PIER_X, y0 + b * PITCH, 0)
                if k == n_plots - 1: P.add("aro_pier_stone_a", walls.pier, side * PIER_X, y0 + 5 * PITCH, 0)
                for b in (g, g + 1):                                                                     # lanterns on the street face of the two gate piers
                    P.add("aro_lantern_wall_a", walls.lantern, side * (PIER_X - 0.275), y0 + b * PITCH, yaw, z=1.75)
                # villa: type A on every plot (type B follows), door aligned with the gate
                cy = gate_y - 3.0 * (1 if side > 0 else -1)
                if (k + (0 if side > 0 else 1)) % 2 == 0: P.add("aro_villa_2storey_hip_a", villa.villa_a, side * 18.15, cy, yaw, tag="villa")
                else: P.add("aro_villa_2storey_hip_b", villa.villa_b, side * 18.15, gate_y + 2.1 * (1 if side > 0 else -1), yaw, tag="villa")
                # driveway from the gate to the porch
                P.add("kano_driveway_a", lambda lod: _driveway(), side * 10.4, gate_y, 0, record=False)
                # trees and shrubs on the verge
                for j, off in enumerate((6.0, 18.0)):
                    seed = 1 + (k * 2 + j + (0 if side > 0 else 1)) % 2
                    P.add("rg_ngn_tree_street_broadleaf_a" if seed == 1 else "rg_ngn_tree_street_broadleaf_b", (lambda lod, s=seed: veg.tree(s, 8.0 if s == 1 else 9.0, 6.0 if s == 1 else 5.2, lod)), side * 6.75, y0 + off + 0.3 * side, 0, lod=detail_lod)
                for b in range(5):
                    if b in (0, 4) and b != g: P.add("aro_hedge_clipped_a", veg.hedge, side * 6.95, y0 + (b + 0.5) * PITCH, 90.0, lod=detail_lod)
                for b in (g, g + 1): P.add("aro_shrub_ixora_red_a", veg.shrub_ixora, side * 6.55, y0 + b * PITCH + (0.9 if b == g else -0.9), 0, lod=detail_lod)
            else:
                P.add("aro_villa_2storey_hip_a", villa.villa_a, side * 18.15, yc, yaw, lod=2)
                for b in range(5): P.add("aro_wall_compound_panel_a", lambda lod: walls.wall_panel(lod, PANEL_LEN), side * PIER_X, y0 + (b + 0.5) * PITCH, yaw, lod=2)
                P.add("rg_ngn_tree_street_broadleaf_a", lambda lod: veg.tree(1, 8.0, 6.0, lod), side * 6.75, yc - 6, 0, lod=2)
    if not far:
        for k, y in enumerate((-36.0, 0.0, 36.0)):                                                     # streetlights: right side at y, left side offset by half a spacing
            P.add("aro_streetlight_single_arm_a", lights.streetlight, 3.95, y_center + y + 6, FACE[1])
            P.add("aro_streetlight_single_arm_a", lights.streetlight, -3.95, y_center + y - 12, FACE[-1])
        for k in range(4): P.add("aro_pole_electric_concrete_span_a", lights.pole_span, -6.55, y_start + 6 + 30 * k, 0, tag="pole")
        if y_center == 0.0: P.add("aro_sign_oneway_a", lights.sign_oneway, -4.35, -36.0, 0, tag="sign")


def _driveway() -> Part:
    """A concrete drive from the gate to the porch (review scene only: it is plot paving, not a kit asset)."""
    p = Part("drive"); p.box(-2.8, -2.0, 0.0, 2.8, 2.0, 0.025, "concrete", skip=("bottom",)); return p
