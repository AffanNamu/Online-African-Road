"""Compound-wall kit for the residential street (concept R-006): wall panel with grey cladding inset, stone-clad pier with cap, ornate steel gate, wall lantern. All geometry, no image planes.
Axes: wall length along X, thickness along Y, the street side is the front (-Y)."""
from __future__ import annotations
import math
from mathutils import Vector
from .geo import Part

CREAM, GREY, STONE, BLACK, WHITE = "plaster_cream_light", "cladding_grey", "stone_travertine", "metal_gate_black", "paint_white"
WALL_H = 2.40; PIER_H = 2.75


def wall_panel(lod: int = 0, length: float = 4.45) -> Part:
    """A 4.45 m wall bay (between two 0.55 m piers, 5.0 m pitch): grey plinth, cream bands, a grey cladding panel on both faces, coping with a drip overhang."""
    p = Part("wall"); L = length / 2; t = 0.125
    if lod >= 2: p.box(-L, -t, 0, L, t, WALL_H, CREAM); return p
    p.box(-L, -t, 0.0, L, t, 0.45, GREY, skip=("left", "right", "bottom"))                                  # plinth
    p.box(-L, -t, 0.45, L, t, 2.25, CREAM, skip=("left", "right", "bottom", "top"))
    if lod == 0:
        for s in (-1, 1):                                                                                   # grey cladding panel, proud of the face on both sides
            y0, y1 = (-t - 0.025, -t) if s < 0 else (t, t + 0.025)
            p.box(-L + 0.30, y0, 0.78, L - 0.30, y1, 1.92, GREY, skip=("bottom", "back" if s < 0 else "front"))
            for z in (0.62, 2.10):                                                                          # raised cream bands above and below the panel
                yy0, yy1 = (-t - 0.012, -t) if s < 0 else (t, t + 0.012)
                p.box(-L, yy0, z, L, yy1, z + 0.10, CREAM, skip=("left", "right", "bottom", "back" if s < 0 else "front"))
    p.box(-L, -t - 0.06, 2.25, L, t + 0.06, WALL_H, CREAM, skip=("left", "right"))                          # coping with overhang
    return p


def pier(lod: int = 0) -> Part:
    """A 0.55 m square pier clad in tan stone courses (0.2 m, each slightly set back for a joint shadow), cream cap block and a stepped top."""
    p = Part("pier"); w = 0.275
    if lod >= 2: p.box(-w, -w, 0, w, w, PIER_H, STONE); return p
    if lod == 0:
        n = 12
        for k in range(n):
            i = 0.012 if k % 2 else 0.0; p.box(-w + i, -w + i, k * 0.2, w - i, w - i, (k + 1) * 0.2, STONE, skip=("bottom",) if k else ())
    else: p.box(-w, -w, 0, w, w, 2.4, STONE)
    p.box(-0.34, -0.34, 2.40, 0.34, 0.34, 2.56, CREAM)                                                    # cap block
    p.frustum(-0.30, -0.30, 2.56, 0.30, 0.30, PIER_H, CREAM, 0.16, skip=("bottom",))                         # stepped top
    return p


def lantern(lod: int = 0) -> Part:
    """A wall lantern: back plate, bracket, glass body with four corner posts, pyramid cap. Origin at the wall face, hanging down from z 0.34 to 0."""
    p = Part("lantern"); BL = "metal_gate_black"
    p.box(-0.05, -0.012, 0.0, 0.05, 0.0, 0.34, BL, skip=("back",))
    p.box(-0.02, -0.07, 0.17, 0.02, 0.0, 0.20, BL)
    p.box(-0.075, -0.17, 0.0, 0.075, -0.02, 0.17, "lantern_glow")
    if lod == 0:
        for sx in (-1, 1):
            for sy in (-1, 1):
                x = sx * 0.07; y = -0.095 + sy * 0.065; p.box(x - 0.008, y - 0.008, 0.0, x + 0.008, y + 0.008, 0.17, BL)
        p.frustum(-0.09, -0.185, 0.17, 0.09, -0.005, 0.25, BL, 0.05)
        p.box(-0.012, -0.105, 0.25, 0.012, -0.08, 0.30, BL)
    else: p.frustum(-0.09, -0.185, 0.17, 0.09, -0.005, 0.25, BL, 0.05)
    return p


def _bar(p, x, z0, z1, w=0.02, tip=True, spear=0.05):
    y0, y1 = -0.015, 0.015
    p.box(x - w / 2, y0, z0, x + w / 2, y1, z1, BLACK)
    if tip: p.frustum(x - w * 0.9, -0.02, z1, x + w * 0.9, 0.02, z1 + spear, BLACK, w * 0.85, skip=("bottom",))


def _ring(p, cx, cz, r, seg=8, minor=0.012):
    pts = [Vector((cx + r * math.cos(2 * math.pi * k / (seg * 2)), 0.0, cz + r * math.sin(2 * math.pi * k / (seg * 2)))) for k in range(seg * 2)]
    p.path_tube(pts + [pts[0]], minor, 4, BLACK, caps=(False, False), smooth=False)


def gate(lod: int = 0, width: float = 3.60, height: float = 2.35) -> Part:
    """Double-leaf ornate steel gate (concept R-006, left foreground): square-bar frame, an arched top rail, vertical bars with spear tips, three cross rails, scroll rings in the upper panel
    and a centre meeting stile. Origin at the centre of the gate on the ground, thickness along Y."""
    p = Part("gate"); W = width / 2; fb = 0.05; zt = height; lift = 0.10                          # lift: clearance under the bars
    if lod >= 2: p.box(-W, -0.03, 0.05, W, 0.03, zt, BLACK); return p
    crown = zt - 0.08                                                                             # height of the arch at the centre
    def arch_z(x): return zt - 0.28 + 0.20 * (1 - (x / W) ** 2)                                  # arch rail centre height
    for x, top in ((-W + fb / 2, arch_z(-W) + 0.04), (0.0, crown), (W - fb / 2, arch_z(W) + 0.04)): p.box(x - fb / 2, -0.03, 0.0, x + fb / 2, 0.03, top, BLACK)
    for z in (lift + 0.04, 0.78, 1.45): p.box(-W, -0.025, z - 0.03, W, 0.025, z + 0.03, BLACK)    # three cross rails
    steps = 16 if lod == 0 else 8
    p.path_tube([Vector((-W + 2 * W * k / steps, 0.0, arch_z(-W + 2 * W * k / steps))) for k in range(steps + 1)], 0.03, 4, BLACK, caps=(True, True), smooth=False)
    n = int((2 * W - 2 * fb) / 0.12)
    for i in range(1, n):
        x = -W + fb + i * (2 * W - 2 * fb) / n
        if abs(x) < 0.05: continue
        _bar(p, x, lift, arch_z(x) + 0.01, 0.02, tip=(lod == 0), spear=0.06)
    if lod == 0:
        for leaf in (-1, 1):                                                                       # scroll rings in the upper panel, between the middle and top rails
            for cx, cz, r in [(0.45, 1.12, 0.11), (0.90, 1.12, 0.11), (1.35, 1.12, 0.11), (0.68, 1.62, 0.09), (1.13, 1.62, 0.09)]: _ring(p, leaf * cx, cz, r, 8, 0.011)
        p.box(-0.04, -0.035, 0.30, 0.04, 0.035, 0.50, BLACK)                                       # lock box on the meeting stile
    return p
