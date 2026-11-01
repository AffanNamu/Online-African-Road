"""kano_street_oneway_section_a: 96 m of the one-way residential street of concept R-006, as real geometry. Cross-section (x, right = +X; street axis along Y):
carriageway |x| <= 3.5 m (two 3.5 m lanes, white dashed lane line, solid white edge lines 0.25 m inside the kerbs), painted black/white kerb blocks 3.5 to 3.7 m (0.15 m high),
paver sidewalk 3.7 to 6.1 m (0.15 m above the road), concrete edge strip, grass verge 6.2 to 7.3 m, then the compound walls at 7.4 m. Road markings are cut flush into the asphalt
surface (same plane, own material), arrows are 6 mm decals. Plot yards behind the walls are a flat ground sheet so the section is never floating."""
from __future__ import annotations
from .geo import Part

LEN = 96.0; HALF = LEN / 2
KERB_IN, KERB_OUT = 3.50, 3.70; WALK_OUT = 6.10; EDGE_OUT = 6.20; VERGE_OUT = 7.30; WALL_X = 7.40; YARD_OUT = 33.0
WALK_Z = 0.15; VERGE_Z = 0.10
DASH, GAP = 3.0, 6.0
UP = (0, 0, 1)


def arrow_poly(cx, y0, length=3.2, head_w=0.9, shaft_w=0.30, head_l=1.1):
    """A straight-ahead pavement arrow pointing +Y, as a simple polygon (x, y), counter-clockwise."""
    sw, hw = shaft_w / 2, head_w / 2; yh = y0 + length - head_l
    return [(cx - sw, y0), (cx + sw, y0), (cx + sw, yh), (cx + hw, yh), (cx, y0 + length), (cx - hw, yh), (cx - sw, yh)]


def build(lod: int = 0) -> Part:
    p = Part("street"); p.uv_scale = {"asphalt": 0.25, "paver_mix": 0.5, "grass_green": 0.2}
    # ---- carriageway as a grid so the markings lie flush with the asphalt
    ex0, ex1 = 0.25, 0.40                                               # solid edge line, distance from the kerb
    xb = [-KERB_IN, -KERB_IN + ex0, -KERB_IN + ex1, -0.075, 0.075, KERB_IN - ex1, KERB_IN - ex0, KERB_IN]
    yb = [-HALF]; dashes = []; y = -HALF + 2.0
    while y + DASH <= HALF - 1.0:
        dashes.append((y, y + DASH)); y += DASH + GAP
    for a, b in dashes: yb += [a, b]
    yb.append(HALF)
    if lod >= 1: xb = [-KERB_IN, KERB_IN]; yb = [-HALF, HALF]; dashes = []
    def road_key(xc, yc):
        if lod >= 1: return "asphalt"
        if abs(xc) < 0.1 and any(a <= yc <= b for a, b in dashes): return "road_white"
        if abs(abs(xc) - (KERB_IN - (ex0 + ex1) / 2)) < 0.1: return "road_white"
        return "asphalt"
    for i in range(len(xb) - 1):
        for j in range(len(yb) - 1):
            if yb[j + 1] - yb[j] < 1e-6: continue
            xc, yc = (xb[i] + xb[i + 1]) / 2, (yb[j] + yb[j + 1]) / 2
            p.face_n([(xb[i], yb[j], 0), (xb[i + 1], yb[j], 0), (xb[i + 1], yb[j + 1], 0), (xb[i], yb[j + 1], 0)], road_key(xc, yc), UP)
    if lod == 0:                                                         # arrows: one per lane every 48 m, 6 mm decals
        for ax in (-1.75, 1.75):
            for ay in (-HALF + 14.0, 14.0):
                p.face_n([(x, y_, 0.006) for x, y_ in arrow_poly(ax, ay)], "road_white", UP)
    # ---- kerbs, sidewalks, verge, yards on both sides (built for +X, mirrored by s)
    block = 1.0 if lod == 0 else LEN; n = int(round(LEN / block))
    for s in (1, -1):
        for k in range(n):
            y0 = -HALF + k * block; y1 = y0 + block; key = ("kerb_black" if k % 2 == 0 else "kerb_white") if lod == 0 else "concrete"
            X = lambda x: s * x
            p.face_n([(X(KERB_IN), y0, 0.0), (X(KERB_IN), y1, 0.0), (X(KERB_IN), y1, 0.12), (X(KERB_IN), y0, 0.12)], key, (-s, 0, 0))                       # road face
            p.face_n([(X(KERB_IN), y0, 0.12), (X(KERB_IN), y1, 0.12), (X(KERB_IN + 0.03), y1, 0.15), (X(KERB_IN + 0.03), y0, 0.15)], key, (-s, 0, 1))      # chamfer
            p.face_n([(X(KERB_IN + 0.03), y0, 0.15), (X(KERB_OUT), y0, 0.15), (X(KERB_OUT), y1, 0.15), (X(KERB_IN + 0.03), y1, 0.15)], key, UP)            # top
        quad = lambda x0, x1, z, key: p.face_n([(s * x0, -HALF, z), (s * x1, -HALF, z), (s * x1, HALF, z), (s * x0, HALF, z)], key, UP)
        quad(KERB_OUT, WALK_OUT, WALK_Z, "paver_mix"); quad(WALK_OUT, EDGE_OUT, WALK_Z, "concrete")
        p.face_n([(s * EDGE_OUT, -HALF, VERGE_Z), (s * EDGE_OUT, HALF, VERGE_Z), (s * EDGE_OUT, HALF, WALK_Z), (s * EDGE_OUT, -HALF, WALK_Z)], "concrete", (s, 0, 0))   # step down to the verge
        quad(EDGE_OUT, WALL_X, VERGE_Z, "grass_green"); quad(WALL_X, YARD_OUT, 0.0, "grass_green")
        p.face_n([(s * WALL_X, -HALF, 0.0), (s * WALL_X, HALF, 0.0), (s * WALL_X, HALF, VERGE_Z), (s * WALL_X, -HALF, VERGE_Z)], "grass_green", (s, 0, 0))              # step at the wall line
    return p
