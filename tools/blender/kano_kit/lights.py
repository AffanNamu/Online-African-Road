"""Street furniture for the residential street: single-arm streetlight (concept R-006), concrete electricity pole with a 30 m span of overhead conductors (requested by the owner; NOT in the concept
and no Kano reference exists yet: M-06, so it is a generic design), and the blue 'One Way' regulatory sign with real lettering."""
from __future__ import annotations
import math
import bpy
from mathutils import Vector
from .geo import Part

GALV, BLACK = "metal_galvanised", "metal_gate_black"


def streetlight(lod: int = 0) -> Part:
    """9 m galvanised pole (tapered, 12-sided), base plate, access door, swept 2.4 m arm toward -Y and a cobra-head luminaire with a glowing lens."""
    p = Part("light"); seg = 12 if lod == 0 else 6 if lod == 1 else 4; H = 9.0
    p.tube((0, 0, 0.02), (0, 0, H - 0.35), 0.13, 0.065, seg, GALV, caps=(False, True), smooth=(lod < 2))
    if lod >= 2:
        p.box(-0.2, -2.6, 9.0, 0.2, 0.1, 9.25, GALV); return p
    p.box(-0.22, -0.22, 0.0, 0.22, 0.22, 0.02, GALV); p.tube((0, 0, 0.02), (0, 0, 0.5), 0.17, 0.14, seg, GALV, caps=(False, True))        # base plate and collar
    if lod == 0: p.box(-0.05, -0.145, 0.7, 0.05, -0.125, 1.5, "metal_galvanised"); p.box(-0.04, -0.15, 1.0, 0.04, -0.14, 1.04, BLACK)       # access door
    arm = [Vector((0, -2.45 * t, H - 0.45 + 0.75 * (1 - (1 - t) ** 2.2))) for t in [k / (7 if lod == 0 else 3) for k in range(8 if lod == 0 else 4)]]
    p.path_tube(arm, [0.05 - 0.015 * (i / (len(arm) - 1)) for i in range(len(arm))], 8 if lod == 0 else 4, GALV, caps=(False, True))
    ex, ez = arm[-1].y, arm[-1].z                                                                                                          # luminaire at the arm tip
    p.box(-0.17, ex - 0.55, ez - 0.02, 0.17, ex + 0.30, ez + 0.12, GALV)
    p.box(-0.14, ex - 0.50, ez - 0.045, 0.14, ex + 0.25, ez - 0.02, "lantern_glow")
    if lod == 0: p.frustum(-0.17, ex - 0.55, ez + 0.12, 0.17, ex + 0.30, ez + 0.19, GALV, 0.05, skip=("bottom",))
    return p


def pole_span(lod: int = 0, span: float = 30.0) -> Part:
    """Concrete pole, 9.5 m, octagonal and tapered; steel cross-arm with three pin insulators (11 kV), a lower bracket with four spool insulators (low voltage), and `span` metres of conductors
    along +Y with a parabolic sag, ending where the next pole (30 m on, same orientation) takes them. Generic design, flagged: no Kano reference (M-06)."""
    p = Part("pole"); H = 9.5; seg = 8
    p.tube((0, 0, 0), (0, 0, H), 0.17, 0.095, seg, "concrete", caps=(False, True), smooth=False, roll=math.pi / 8)
    p.box(-0.22, -0.22, 0.0, 0.22, 0.22, 0.25, "concrete_dark")                                                                              # foundation collar
    if lod >= 2: return p
    p.box(-0.95, -0.04, 8.75, 0.95, 0.04, 8.83, GALV)                                                                                         # cross-arm (angle iron)
    hv = []; lv = []
    for x in (-0.75, 0.0, 0.75):
        p.tube((x, 0, 8.83), (x, 0, 9.07), 0.045, 0.02, 6, "insulator_brown"); p.box(x - 0.012, -0.012, 9.07, x + 0.012, 0.012, 9.12, GALV); hv.append(Vector((x, 0, 9.10)))
    p.box(-0.55, -0.03, 7.62, 0.55, 0.03, 7.68, GALV)
    for x in (-0.42, -0.14, 0.14, 0.42):
        p.tube((x, 0, 7.68), (x, 0, 7.82), 0.04, 0.025, 6, "insulator_brown"); lv.append(Vector((x, 0, 7.80)))
    if lod >= 1: return p
    p.tube((0.0, 0.17, 5.0), (0.0, 0.17, 7.6), 0.012, 0.012, 4, "metal_galvanised")                                                          # earth down-lead
    n = 12
    for w in hv + lv:
        sag = 0.7 if w.z > 8.5 else 0.5
        pts = [Vector((w.x, span * k / n, w.z - sag * 4 * (k / n) * (1 - k / n))) for k in range(n + 1)]
        p.path_tube(pts, 0.014 if w.z > 8.5 else 0.011, 4, "rubber", caps=(False, False), smooth=False)
    return p


def _text_mesh(text: str, width: float, depth: float) -> list[tuple[list[Vector], tuple[int, ...]]]:
    """Real lettering: a Blender font curve extruded, converted to a mesh, scaled to `width` metres. Returns the faces as (points, ...)."""
    c = bpy.data.curves.new("txt", "FONT"); c.body = text; c.extrude = depth / 2; c.resolution_u = 1; c.align_x = "CENTER"; c.align_y = "CENTER"; c.size = 1.0
    o = bpy.data.objects.new("txt_tmp", c); bpy.context.scene.collection.objects.link(o)
    deps = bpy.context.evaluated_depsgraph_get(); m = bpy.data.meshes.new_from_object(o.evaluated_get(deps)); xs = [v.co.x for v in m.vertices]; k = width / (max(xs) - min(xs))
    faces = [[Vector((v.co.x * k, v.co.y * k, v.co.z * k)) for v in (m.vertices[i] for i in poly.vertices)] for poly in m.polygons]
    bpy.data.objects.remove(o); bpy.data.curves.remove(c); bpy.data.meshes.remove(m); return faces


def sign_oneway(lod: int = 0) -> Part:
    """Blue 0.9 x 0.9 m 'One Way' sign on a 2.6 m post: face plate with a white border, a white up-arrow and extruded 'One Way' lettering. The sign face looks toward -Y."""
    p = Part("sign"); post = 0.035; zc = 2.15; s = 0.45
    if lod >= 2:
        p.box(-0.035, 0.025, 0.0, 0.035, 0.095, 2.6, GALV); p.box(-0.46, -0.025, zc - 0.46, 0.46, 0.035, zc + 0.46, "sign_blue"); return p
    p.tube((0, 0.06, 0.0), (0, 0.06, 2.6), post, post, 8 if lod == 0 else 6, GALV, caps=(True, True))
    p.box(-0.46, -0.01, zc - 0.46, 0.46, 0.035, zc + 0.46, "paint_white", skip=("front",))                  # back and rim plate (white)
    p.box(-0.425, -0.025, zc - 0.425, 0.425, 0.0, zc + 0.425, "sign_blue", skip=("back",))                 # blue face, proud of the rim
    p.box(-0.08, 0.035, zc - 0.20, 0.08, 0.10, zc - 0.16, GALV); p.box(-0.08, 0.035, zc + 0.16, 0.08, 0.10, zc + 0.20, GALV)
    if lod >= 2: return p
    ar = [(-0.07, -0.02), (0.07, -0.02), (0.07, 0.17), (0.17, 0.17), (0.0, 0.40), (-0.17, 0.17), (-0.07, 0.17)]                                 # up arrow (x, z offset above the arrow base)
    p.face_n([(x, -0.030, zc + 0.12 + z) for x, z in ar], "paint_white", (0, -1, 0))
    if lod == 0:                                                                                                                            # lettering: rotate the font mesh (XY plane, +Z front) to the XZ plane facing -Y
        for pts in _text_mesh("One Way", 0.62, 0.012): p.face([(q.x, -0.0275 - q.z, zc - 0.20 + q.y) for q in pts], "paint_white")
    return p
