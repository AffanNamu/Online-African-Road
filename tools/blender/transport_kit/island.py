"""Family D: roundabout, island and flyover components (references R-004, R-005, R-023, R-034..R-036, documentary R-024..R-029). Built reuse-first (`docs/KANO_FAMILY_D_PLAN.md`): trees, shrubs and hedges are NOT rebuilt, the registry already holds them.
The engine draws the ring, the islands, the deck, its parapets and its piers; these assets are dressing and detail on top. The monument and the Hausa-inspired panels are **fictional designed interpretations** (the owner's defaults of 2026-11-09), never
a claim about a real monument or a real Kano pattern. Same rules as the rest of the kit: real metres, front at -Y, origin on the ground at the footprint centre, three LODs (the last a few boxes), at most 4 materials (palette)."""
from __future__ import annotations
import math
from mathutils import Matrix, Vector
from kano_kit.geo import Part
from .common import prism_x, prism_y, circle_pts, extra

GAL, CON, CDK = "metal_galvanised", "concrete", "concrete_dark"
REF_D = ["R-004", "R-005", "R-023", "R-034", "R-035", "R-036"]
REF_HAUSA = ["R-024", "R-025", "R-026", "R-027", "R-028", "R-029"]
WATER = "water_pool"


def fountain(lod):
    """Low fountain basin 8 m across (no animation, no water shader): a kerb ring of 24 segments, a flat water surface, a three-tier pedestal with a bowl and spout, four low jets and four basin lights."""
    p = Part("fountain"); R = 3.9; n = 24
    if lod >= 2: p.tube((0, 0, 0.0), (0, 0, 1.2), 3.95, 3.95, 8, CON, caps=(True, True), smooth=False); return p
    seg = 24 if lod == 0 else 12; n = 24 if lod == 0 else 12
    for k in range(n):                                                                                                  # kerb ring: rotated quads extruded 0.9 m
        a0, a1 = 2 * math.pi * k / n, 2 * math.pi * (k + 1) / n; ri, ro = R - 0.45, R
        p.prism([(ri * math.cos(a0), ri * math.sin(a0)), (ro * math.cos(a0), ro * math.sin(a0)), (ro * math.cos(a1), ro * math.sin(a1)), (ri * math.cos(a1), ri * math.sin(a1))], 0.0, 0.9, CON, caps=(lod == 0))
    p.tube((0, 0, 0.0), (0, 0, 0.62), R - 0.45, R - 0.45, seg, CDK, caps=(False, True), smooth=False)                    # basin floor and wall below the water
    p.tube((0, 0, 0.62), (0, 0, 0.66), R - 0.46, R - 0.46, seg, WATER, caps=(False, True), smooth=False)                 # water surface
    for k, (r0, r1, z0, z1) in enumerate(((1.5, 1.5, 0.0, 0.8), (1.0, 1.0, 0.8, 1.35), (0.55, 0.55, 1.35, 1.6))): p.tube((0, 0, z0), (0, 0, z1), r0, r1, seg, CON, caps=(False, True), smooth=False)   # pedestal tiers
    p.tube((0, 0, 0.8), (0, 0, 1.0), 1.5, 1.2, seg, CON, caps=(False, True), smooth=False)
    if lod >= 1: return p
    for k in range(4):
        a = k * math.pi / 2 + math.pi / 4; dx, dy = math.cos(a), math.sin(a)
        p.tube((dx * 2.3, dy * 2.3, 0.64), (dx * 2.3, dy * 2.3, 1.15), 0.045, 0.02, 6, WATER, caps=(False, True))        # jets
        p.box(dx * 3.25 - 0.12, dy * 3.25 - 0.12, 0.64, dx * 3.25 + 0.12, dy * 3.25 + 0.12, 0.72, "lantern_glow", skip=("bottom",))     # basin lights
    p.tube((0, 0, 1.6), (0, 0, 1.95), 0.12, 0.02, 8, WATER, caps=(False, True))
    for k in range(12): a = 2 * math.pi * k / 12; p.box(math.cos(a) * 1.5 - 0.05, math.sin(a) * 1.5 - 0.05, 0.8, math.cos(a) * 1.5 + 0.05, math.sin(a) * 1.5 + 0.05, 0.86, GAL)   # tier studs
    return p


def island_wall(lod):
    """3 m run of red-earth (kasa) rendered island wall, 1.2 m high, 0.4 m thick: plinth, coping, two recessed panels each side and a pier at the left end. Tile it end to end; it follows a curve as short straight runs."""
    p = Part("wall")
    if lod >= 2: p.box(-1.5, -0.2, 0.0, 1.5, 0.2, 1.2, "kasa_render"); return p
    p.box(-1.4, -0.18, 0.0, 1.5, 0.18, 1.1, "kasa_render", skip=("bottom",)); p.box(-1.5, -0.2, 0.0, -1.34, 0.2, 1.2, "kasa_render", skip=("bottom",))
    p.box(-1.4, -0.2, 1.1, 1.5, 0.2, 1.2, CON, skip=("bottom",))
    if lod == 1: return p
    p.box(-1.4, -0.2, 0.0, 1.5, 0.2, 0.18, CON, skip=("bottom",))                                                          # plinth
    for k in range(2):
        x0 = -1.2 + 1.35 * k
        for s in (-1, 1):
            y = s * 0.18; p.box(x0, y - 0.012, 0.35, x0 + 1.2, y + 0.012, 0.95, "plaster_cream"); p.box(x0 - 0.04, y - 0.018, 0.31, x0 + 1.24, y + 0.018, 0.35, "kasa_render"); p.box(x0 - 0.04, y - 0.018, 0.95, x0 + 1.24, y + 0.018, 0.99, "kasa_render")
    p.box(-1.54, -0.24, 1.2, -1.3, 0.24, 1.28, CON, skip=("bottom",))                                                      # pier cap
    for k in range(4): p.box(-1.5 - 0.01, -0.2, 0.2 + 0.25 * k, -1.34 + 0.01, 0.2, 0.2 + 0.25 * k + 0.02, "plaster_cream")   # bands on the pier
    return p


def _diamond(p, cx, cz, r, y0, y1, key): prism_y(p, [(cx, cz - r), (cx + r, cz), (cx, cz + r), (cx - r, cz)], y0, y1, key)


def pattern_panel(kind):
    def build(lod):
        """Hausa-inspired geometric relief panel (a FICTIONAL interpretation of the documentary sources R-024..R-029, not a copy of a real pattern): 3 m x 1.4 m, a kasa-render slab with a frame and a raised pattern: %s.""" % {"a": "interlocked diamonds", "b": "stepped zigzag bands", "c": "octagon rosettes"}[kind]
        p = Part("panel")
        if lod >= 2: p.box(-1.5, -0.15, 0.0, 1.5, 0.15, 1.4, "kasa_render"); return p
        p.box(-1.5, -0.15, 0.0, 1.5, 0.15, 1.4, "kasa_render", skip=("bottom",))
        p.box(-1.5, -0.17, 0.0, 1.5, -0.15, 0.1, "plaster_cream", skip=("bottom", "back")); p.box(-1.5, -0.17, 1.3, 1.5, -0.15, 1.4, "plaster_cream", skip=("bottom", "back"))
        if lod >= 1: p.box(-1.3, -0.17, 0.2, 1.3, -0.15, 1.2, "plaster_cream", skip=("bottom", "back")); return p
        if kind == "a":
            for i in range(6):
                cx = -1.25 + 0.5 * i; _diamond(p, cx, 0.7, 0.3, -0.2, -0.15, "plaster_cream"); _diamond(p, cx, 0.7, 0.17, -0.22, -0.2, "kasa_render")
                if i < 5: _diamond(p, cx + 0.25, 0.7, 0.14, -0.2, -0.15, "plaster_cream")
            for i in range(6): _diamond(p, -1.25 + 0.5 * i, 0.2, 0.1, -0.2, -0.15, "plaster_cream"); _diamond(p, -1.25 + 0.5 * i, 1.2, 0.1, -0.2, -0.15, "plaster_cream")
        elif kind == "b":
            for row in range(4):
                z = 0.3 + 0.27 * row
                for i in range(12): x = -1.4 + 0.25 * i; p.box(x, -0.2, z + (0.1 if i % 2 else 0.0), x + 0.25, -0.15, z + (0.2 if i % 2 else 0.1), "plaster_cream", skip=("bottom", "back"))
            for z in (0.25, 1.15): p.box(-1.4, -0.19, z, 1.4, -0.15, z + 0.04, "plaster_cream", skip=("bottom", "back"))
        else:
            for i in range(3):
                cx = -0.95 + 0.95 * i; prism_y(p, circle_pts(0.38, 8, cx, 0.7, math.pi / 8), -0.2, -0.15, "plaster_cream"); prism_y(p, circle_pts(0.24, 8, cx, 0.7, 0.0), -0.23, -0.2, "kasa_render"); prism_y(p, circle_pts(0.1, 8, cx, 0.7, 0.0), -0.25, -0.23, "plaster_cream")
            for i in range(4): _diamond(p, -1.425 + 0.95 * i, 0.7, 0.1, -0.2, -0.15, "plaster_cream") if abs(-1.425 + 0.95 * i) < 1.4 else None
        return p
    return build


def deck_parapet_kit(lod):
    """Parapet and bracket kit for a flyover deck module 24 m long and 12 m between the parapets (the engine draws the deck itself): two edge beams with a concrete parapet and a two-rail railing on posts every 2 m, and a luminaire bracket every 4 m."""
    p = Part("deck")
    for s in (-1, 1):
        x = s * 5.7
        p.box(x - 0.3, -12.0, 0.0, x + 0.3, 12.0, 0.9, CON, skip=("bottom",))                                                # edge beam
        if lod >= 2: p.box(x - 0.2, -12.0, 0.9, x + 0.2, 12.0, 1.6, CON); continue
        prism_y(p, [(x - 0.2, 0.9), (x + 0.2, 0.9), (x + 0.15, 1.2), (x - 0.15, 1.2)], -12.0, 12.0, CON)                     # parapet base
        if lod >= 1:
            p.box(x - 0.04, -12.0, 1.5, x + 0.04, 12.0, 1.6, GAL); continue
        for k in range(13): y = -12.0 + 2.0 * k; p.box(x - 0.05, y - 0.05, 1.2, x + 0.05, y + 0.05, 1.6, GAL, skip=("bottom",))
        for z in (1.35, 1.58): p.box(x - 0.03, -12.0, z, x + 0.03, 12.0, z + 0.04, GAL)
        for k in range(7): y = -12.0 + 4.0 * k; p.box(x - 0.1 * s - 0.1, y - 0.08, 0.5, x - 0.1 * s + 0.1, y + 0.08, 0.7, CDK); p.box(x - 0.55 * s - 0.04, y - 0.04, 0.55, x - 0.1 * s, y + 0.04, 0.62, GAL)    # luminaire brackets
        for k in range(5): y = -10.0 + 5.0 * k; p.box(x - 0.31 * s - 0.01, y - 0.4, 0.2, x - 0.3 * s + 0.01, y + 0.4, 0.7, "plaster_cream")   # edge-beam recess
    return p


def pier_round(lod):
    """Round flyover pier, 1.6 m across at the base widening to the cap, 7 m high: a base collar, a fluted-looking shaft (16-sided), a flared head, a 2.4 m square cap with bearing pads."""
    p = Part("pier"); seg = 16 if lod == 0 else 8
    if lod >= 2: p.box(-0.8, -0.8, 0.0, 0.8, 0.8, 6.4, CON); p.box(-1.2, -1.2, 6.4, 1.2, 1.2, 7.0, CON); return p
    p.tube((0, 0, 0.0), (0, 0, 0.3), 1.0, 0.9, seg, CON, caps=(False, True), smooth=False); p.tube((0, 0, 0.3), (0, 0, 5.9), 0.8, 0.8, seg, CON, caps=(False, True), smooth=(lod == 0)); p.tube((0, 0, 5.9), (0, 0, 6.4), 0.8, 1.1, seg, CON, caps=(False, True), smooth=False)
    p.box(-1.2, -1.2, 6.4, 1.2, 1.2, 7.0, CON, skip=("bottom",))
    if lod == 0:
        for sx in (-0.7, 0.7): p.box(sx - 0.2, -0.3, 6.35, sx + 0.2, 0.3, 6.4, "rubber")
        for k in range(8): a = k * math.pi / 4; p.box(math.cos(a) * 0.82 - 0.025, math.sin(a) * 0.82 - 0.025, 0.6, math.cos(a) * 0.82 + 0.025, math.sin(a) * 0.82 + 0.025, 5.6, CDK)    # shaft fluting strips
        p.box(-1.25, -1.25, 6.9, 1.25, 1.25, 7.0, CDK)
    return p


def pier_square(lod):
    """Square-capped flyover pier: a 1.4 m square shaft with chamfered corners, 8 m high, a 2.4 m square cap and a base plinth, bearing pads on the cap."""
    p = Part("pier")
    if lod >= 2: p.box(-0.7, -0.7, 0.0, 0.7, 0.7, 7.2, CON); p.box(-1.2, -1.2, 7.2, 1.2, 1.2, 8.0, CON); return p
    p.box(-0.9, -0.9, 0.0, 0.9, 0.9, 0.35, CON, skip=("bottom",))
    p.box(-0.7, -0.7, 0.35, 0.7, 0.7, 7.2, CON, skip=("bottom", "top"))
    p.box(-1.0, -1.0, 7.2, 1.0, 1.0, 7.5, CON, skip=("bottom",))
    p.box(-1.2, -1.2, 7.5, 1.2, 1.2, 8.0, CON, skip=("bottom",))
    if lod == 0:
        for sx in (-0.7, 0.7): p.box(sx - 0.2, -0.3, 7.95, sx + 0.2, 0.3, 8.0, "rubber")
        for sx in (-0.7, 0.7):
            for sy in (-0.7, 0.7): p.box(sx - 0.06, sy - 0.06, 0.35, sx + 0.06, sy + 0.06, 7.2, CDK)           # corner strips
        for z in (2.0, 4.0, 6.0): p.box(-0.72, -0.72, z, 0.72, 0.72, z + 0.04, CDK)
        for k in range(4):                                                                                                  # vertical ribs on the four faces
            x = -0.45 + 0.3 * k
            p.box(x - 0.04, -0.75, 0.5, x + 0.04, -0.7, 7.0, CDK, skip=("bottom", "back")); p.box(x - 0.04, 0.7, 0.5, x + 0.04, 0.75, 7.0, CDK, skip=("bottom", "front"))
            p.box(-0.75, x - 0.04, 0.5, -0.7, x + 0.04, 7.0, CDK, skip=("bottom", "right")); p.box(0.7, x - 0.04, 0.5, 0.75, x + 0.04, 7.0, CDK, skip=("bottom", "left"))
    return p


def pier_panel(lod):
    """Lattice cladding panel for the interchange piers (FICTIONAL Hausa-inspired interpretation, R-024..R-029): 3 m x 6 m x 0.3 m, a dark backing with a raised diagonal lattice and a frame."""
    p = Part("clad")
    p.box(-1.5, -0.15, 0.0, 1.5, 0.15, 6.0, "kasa_render", skip=("bottom",))
    if lod >= 2: return p
    if lod >= 1: p.box(-1.4, -0.17, 0.2, 1.4, -0.15, 5.8, "plaster_cream", skip=("bottom", "back")); return p
    for x in (-1.5, 1.4): p.box(x, -0.18, 0.0, x + 0.1, -0.15, 6.0, "plaster_cream", skip=("bottom", "back"))
    for z in (0.0, 5.9): p.box(-1.5, -0.18, z, 1.5, -0.15, z + 0.1, "plaster_cream", skip=("bottom", "back"))
    for r in range(10):
        z = 0.45 + 0.55 * r
        for c in range(5):
            x = -1.1 + 0.55 * c + (0.275 if r % 2 else 0.0)
            if abs(x) <= 1.2: _diamond(p, x, z + 0.2, 0.2, -0.2, -0.15, "plaster_cream")
    return p


def edge_light(lod):
    """Deck edge luminaire (wall washer) 0.4 x 0.3 x 0.6 m: a mounting plate, a housing with fins and a lens facing -Y (down and out). Lamp head at the lens."""
    p = Part("edge")
    if lod >= 2: p.box(-0.2, -0.15, 0.0, 0.2, 0.15, 0.6, GAL); return p
    p.box(-0.2, 0.1, 0.0, 0.2, 0.15, 0.6, CDK, skip=("bottom",)); p.box(-0.17, -0.1, 0.05, 0.17, 0.1, 0.5, GAL, skip=("bottom",)); p.box(-0.15, -0.14, 0.14, 0.15, -0.1, 0.46, "lantern_glow", skip=("bottom",))
    if lod >= 1: return p
    for k in range(5): p.box(-0.18, -0.07 + 0.03 * k - 0.01, 0.5, 0.18, -0.07 + 0.03 * k + 0.01, 0.56, GAL)
    for sx in (-0.15, 0.15):
        for sz in (0.1, 0.5): p.tube((sx, 0.15, sz), (sx, 0.17, sz), 0.02, 0.02, 6, GAL)
    p.box(-0.19, -0.12, 0.0, 0.19, 0.1, 0.06, CDK); p.box(-0.1, -0.15, 0.2, 0.1, -0.14, 0.4, "paint_white")
    return p


def noise_wall(lod):
    """4 m module of ramp sound wall, 3 m high: two concrete posts, a 1 m concrete base panel, a 1.9 m transparent upper panel in a frame and a cap. Tile it end to end."""
    p = Part("noise")
    if lod >= 2: p.box(-2.0, -0.15, 0.0, 2.0, 0.15, 3.0, CON); return p
    for sx in (-1.95, 1.95): p.box(sx - 0.1, -0.15, 0.0, sx + 0.1, 0.15, 3.0, CON, skip=("bottom",))
    p.box(-1.85, -0.12, 0.0, 1.85, 0.12, 1.0, CON, skip=("bottom",)); p.box(-1.85, -0.04, 1.05, 1.85, 0.04, 2.9, "glass", skip=("bottom",))
    p.box(-2.05, -0.17, 2.9, 2.05, 0.17, 3.0, CON, skip=("bottom",))
    if lod == 0:
        for z in (1.0, 2.85): p.box(-1.85, -0.07, z, 1.85, 0.07, z + 0.05, GAL)
        for x in (-1.3, -0.65, 0.0, 0.65, 1.3): p.box(x - 0.02, -0.06, 1.05, x + 0.02, 0.06, 2.85, GAL, skip=("bottom", "top"))
        for k in range(4): p.box(-1.7 + 1.1 * k, -0.125, 0.3, -1.0 + 1.1 * k, -0.12, 0.8, "plaster_cream")        # recessed panels on the base
        for sx in (-1.95, 1.95): p.box(sx - 0.14, -0.17, 0.0, sx + 0.14, 0.17, 0.12, CDK)
    return p


def _arch(p, half, rise, t, y0, y1, along):
    """A pointed arch frame, `half` wide each side and `rise` high, frame thickness t, as one prism between y0 and y1 (along 'y': seen from -Y; along 'x': seen from -X)."""
    outer = [(-half, 0.0), (-half, 3.2), (-half * 0.8, 4.2), (-half * 0.4, 5.1), (0.0, rise)]; outer = outer + [(-x, z) for x, z in reversed(outer[:-1])]
    h2 = half - t; inner = [(-h2, 0.0), (-h2, 3.2), (-h2 * 0.8, 4.0), (-h2 * 0.4, 4.7), (0.0, rise - t * 1.3)]; inner = inner + [(-x, z) for x, z in reversed(inner[:-1])]
    poly = outer + list(reversed(inner))
    (prism_y if along == "y" else prism_x)(p, poly, y0, y1, "plaster_cream")


def _shaft_r(z): return 2.4 - 1.3 * (z - 1.05) / 15.45


def monument(lod):
    """FICTIONAL civic monument for the landmark roundabout (the owner's default of 2026-11-09: a designed monument, not a real Kano one): a three-tier octagonal plinth, a tapering octagonal shaft, four pointed arches in the regional geometric language around its base, bands of diamonds, a sphere and a spire at 22 m, six uplights and a plaque."""
    p = Part("monument"); seg = 8; roll = math.pi / 8
    if lod >= 2: p.box(-3.0, -3.0, 0.0, 3.0, 3.0, 4.0, CON); p.box(-1.0, -1.0, 4.0, 1.0, 1.0, 22.0, CON, skip=("bottom",)); return p
    for k, (r, z0, z1) in enumerate(((5.9, 0.0, 0.35), (5.1, 0.35, 0.7), (4.3, 0.7, 1.05))): p.tube((0, 0, z0), (0, 0, z1), r, r, seg, CON if k < 2 else "plaster_cream", caps=(True, True), smooth=False, roll=roll)
    p.tube((0, 0, 1.05), (0, 0, 1.5), 2.9, 2.6, seg, CON, caps=(False, True), smooth=False, roll=roll)
    p.tube((0, 0, 1.5), (0, 0, 16.5), _shaft_r(1.5), 1.1, seg, "plaster_cream", caps=(False, True), smooth=False, roll=roll)
    p.tube((0, 0, 16.5), (0, 0, 17.4), 1.4, 0.8, seg, CON, caps=(False, True), smooth=False, roll=roll)
    p.blob((0, 0, 18.4), (1.2, 1.2, 1.2), "paint_orange", subdiv=2 if lod == 0 else 1); p.tube((0, 0, 19.5), (0, 0, 22.0), 0.35, 0.02, 8 if lod == 0 else 6, GAL, caps=(False, True))
    if lod >= 1: return p
    for along, y0, y1 in (("y", -3.5, -2.8), ("y", 2.8, 3.5), ("x", -3.5, -2.8), ("x", 2.8, 3.5)): _arch(p, 2.6, 6.2, 0.55, y0, y1, along)          # four arches: frames only, the shaft stands behind
    for z in (6.0, 9.5, 13.0):                                                                                                  # bands of eight diamonds on the shaft's flats
        ap = _shaft_r(z) * math.cos(math.pi / 8) + 0.04
        for a in range(8):
            s0 = p.mark(); _diamond(p, 0.0, z, 0.38, -0.04, 0.04, "paint_orange"); p.xform(s0, Matrix.Rotation(a * math.pi / 4, 4, "Z") @ Matrix.Translation((0, -ap, 0)))
    for z in (3.0, 7.75, 11.25, 14.75): p.tube((0, 0, z), (0, 0, z + 0.14), _shaft_r(z) + 0.03, _shaft_r(z) + 0.03, seg, "paint_orange", caps=(False, False), smooth=False, roll=roll)
    for k in range(6):
        a = 2 * math.pi * k / 6; cx, cy = math.cos(a) * 5.0, math.sin(a) * 5.0
        p.box(cx - 0.15, cy - 0.15, 1.05, cx + 0.15, cy + 0.15, 1.2, "lantern_glow", skip=("bottom",)); p.box(cx - 0.2, cy - 0.2, 1.0, cx + 0.2, cy + 0.2, 1.05, GAL)
    p.box(-1.1, -2.8, 0.55, 1.1, -2.7, 1.0, "paint_orange")                                                                    # plaque
    for k in range(32):                                                                                                         # a ring of 32 small posts on the second plinth tier
        a = 2 * math.pi * k / 32; cx, cy = math.cos(a) * 4.7, math.sin(a) * 4.7
        p.box(cx - 0.08, cy - 0.08, 0.7, cx + 0.08, cy + 0.08, 1.0, CON, skip=("bottom",)); p.box(cx - 0.1, cy - 0.1, 1.0, cx + 0.1, cy + 0.1, 1.05, "plaster_cream", skip=("bottom",))
    for k in range(8):                                                                                                          # eight fins up the shaft's corners
        a = k * math.pi / 4 + math.pi / 8; r0, r1 = _shaft_r(2.0), _shaft_r(16.0)
        for z0, z1 in ((2.0, 8.0), (10.0, 16.0)): s0 = p.mark(); p.box(-0.06, -0.06, z0, 0.06, 0.06, z1, "paint_orange", skip=("bottom", "top")); p.xform(s0, Matrix.Rotation(a, 4, "Z") @ Matrix.Translation((0, -(_shaft_r((z0 + z1) / 2) + 0.02), 0)))
    return p


# ------------------------------------------------------------------------------------------------------------------------------------------------ catalogue extras
extra("aro_roundabout_fountain_a", "D", "GLOBAL", REF_D, heads=[(math.cos(k * math.pi / 2 + math.pi / 4) * 3.25, math.sin(k * math.pi / 2 + math.pi / 4) * 3.25, 0.7) for k in range(4)], preset="glow", tags=["island", "roundabout", "fountain"])
extra("rg_ngn_island_wall_a", "D", "REGIONAL", ["R-024", "R-027"] + REF_D[:1], tags=["island", "roundabout", "tile_3m"])
for _k in "abc": extra(f"rg_ngn_pattern_panel_{_k}", "D", "REGIONAL", REF_HAUSA, tags=["island", "pattern_panel", "fictional_interpretation"])
extra("aro_flyover_deck_segment_a", "D", "GLOBAL", ["R-023"], heads=[(5.2 * s, y, 0.6) for s in (-1, 1) for y in (-8.0, 0.0, 8.0)], preset="lamp", tags=["flyover", "deck", "tile_24m"], intensity=0.7, range=14, poolRadius=4.0, poolShift=2.0, realLight=False)
for _k in "ab": extra(f"aro_flyover_pier_{_k}", "D", "GLOBAL", ["R-023"], tags=["flyover", "pier"])
extra("rg_ngn_pier_panel_a", "D", "REGIONAL", REF_HAUSA + ["R-023"], tags=["flyover", "pier", "pattern_panel", "fictional_interpretation"])
extra("aro_deck_edge_light_a", "D", "GLOBAL", ["R-023"], heads=[(0.0, -0.14, 0.3)], preset="lamp", tags=["flyover", "street_lighting"], intensity=0.6, range=12, poolRadius=3.0, poolShift=1.0, realLight=False)
extra("aro_noise_wall_set", "D", "GLOBAL", ["R-023"], tags=["flyover", "ramp", "tile_4m"])
extra("lmk_kano_roundabout_monument_a", "D", "LANDMARK", REF_D[:2] + ["R-034"], heads=[(math.cos(k * math.pi / 3) * 5.0, math.sin(k * math.pi / 3) * 5.0, 1.3) for k in range(6)], preset="glow",
      label={"c": (0.0, -2.8, 0.78), "width": 2.0, "height": 0.4}, tags=["landmark", "roundabout", "monument", "fictional_design"], intensity=1.2, range=14, poolRadius=5.0, realLight=False)
