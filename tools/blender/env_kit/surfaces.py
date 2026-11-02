"""Ground-surface patches of the environmental kit, built as real height-field geometry: rutted mud road, compacted earth road, loose sandy road, cracked asphalt, a pothole set, puddles and a flood sheet.
A patch is an open sheet. The validator wants the lowest point on z = 0, so every height field is shifted until its lowest point (the bottom of the deepest rut or hole) is z = 0; the NOMINAL road surface
(the plane the road would have without ruts, holes or camber) then sits at `datum` metres above the asset's origin, and the asset must be placed that much lower. The datum is returned with the Part and
recorded in the registry. These are surfaces for review scenes and, later, for hazard sites; nothing here changes the game's road model, physics or weather."""
from __future__ import annotations
import math
import numpy as np
from kano_kit.geo import Part

_LAT = {}


def _lattice(seed):
    if seed not in _LAT: _LAT[seed] = np.random.default_rng(seed).random((257, 257))
    return _LAT[seed]


def vnoise(x, y, seed):
    """Smooth value noise in 0..1 at arbitrary coordinates (bilinear over a 256 x 256 lattice, wrapped)."""
    g = _lattice(seed); x = np.asarray(x, float); y = np.asarray(y, float); x0 = np.floor(x); y0 = np.floor(y); fx = x - x0; fy = y - y0
    fx = fx * fx * (3 - 2 * fx); fy = fy * fy * (3 - 2 * fy); i0 = x0.astype(int) % 256; j0 = y0.astype(int) % 256; i1 = (i0 + 1) % 256; j1 = (j0 + 1) % 256
    return (g[i0, j0] * (1 - fx) + g[i1, j0] * fx) * (1 - fy) + (g[i0, j1] * (1 - fx) + g[i1, j1] * fx) * fy


def fbm(x, y, seed, scale, octaves=3):
    """Fractal noise in 0..1; `scale` is features per metre of the lowest octave."""
    out = 0.0; amp = 1.0; tot = 0.0
    for o in range(octaves): out = out + amp * vnoise(np.asarray(x) * scale * 2 ** o, np.asarray(y) * scale * 2 ** o, seed + 13 * o); tot += amp; amp *= 0.5
    return out / tot


def smooth(a, b, x):
    t = np.clip((np.asarray(x, float) - a) / (b - a), 0, 1); return t * t * (3 - 2 * t)


def grid_surface(p: Part, nx, ny, x0, x1, y0, y1, hfun, keyfun, mask=None) -> float:
    """Quads over a regular grid. hfun(X, Y) -> (H, aux); keyfun(xc, yc, hc, auxc) -> kit material key per quad; mask(xc, yc) -> bool per quad (ragged edges). The sheet is shifted so its lowest vertex is z = 0;
    returns the datum: how far above the asset's z = 0 the nominal (h = 0) surface plane sits."""
    xs = np.linspace(x0, x1, nx + 1); ys = np.linspace(y0, y1, ny + 1); X, Y = np.meshgrid(xs, ys, indexing="ij"); H, A = hfun(X, Y)
    xc = (X[:-1, :-1] + X[1:, 1:]) / 2; yc = (Y[:-1, :-1] + Y[1:, 1:]) / 2; hc = (H[:-1, :-1] + H[1:, :-1] + H[1:, 1:] + H[:-1, 1:]) / 4; ac = (A[:-1, :-1] + A[1:, :-1] + A[1:, 1:] + A[:-1, 1:]) / 4
    keep = np.ones_like(xc, bool) if mask is None else mask(xc, yc)
    used = np.zeros((nx + 1, ny + 1), bool)
    for i in range(nx):
        for j in range(ny):
            if keep[i, j]: used[i:i + 2, j:j + 2] = True
    zmin = float(H[used].min()); verts = {}
    def vert(i, j):
        if (i, j) not in verts: verts[(i, j)] = p.bm.verts.new((float(xs[i]), float(ys[j]), float(H[i, j] - zmin)))
        return verts[(i, j)]
    for i in range(nx):
        for j in range(ny):
            if not keep[i, j]: continue
            f = p.bm.faces.new([vert(i, j), vert(i + 1, j), vert(i + 1, j + 1), vert(i, j + 1)])
            f.material_index = p.mi(keyfun(float(xc[i, j]), float(yc[i, j]), float(hc[i, j]), float(ac[i, j]))); f.smooth = True
    return -zmin


# ------------------------------------------------------------------------------------------------------------------------------------------------ rutted mud road (8 m x 24 m)
def mud_rutted(lod=0):
    """An unpaved clay road after heavy rain: crowned, four wheel ruts (0.1 to 0.3 m deep, wandering), ridges between the ruts and along the centre, clods, banks falling away to a side drain at both edges.
    Material: wet glossy mud in the ruts, drier mud on the ridges."""
    p = Part("surface"); p.planar_xy = True; seed = 21; ruts = (-3.0, -1.35, 1.35, 3.0)
    def h(X, Y):
        H = 0.10 * (1 - (X / 4.0) ** 2); D = np.zeros_like(X)
        for k, xc in enumerate(ruts):
            wander = 0.20 * np.sin(0.33 * Y + xc * 1.7) + 0.16 * (fbm(np.full_like(Y, xc * 3.1), Y, seed + k, 0.16, 2) - 0.5)
            depth = np.clip(0.17 + 0.12 * (fbm(np.full_like(Y, xc * 2.3), Y, seed + 7 + k, 0.11, 3) - 0.5) * 2, 0.06, 0.30)
            r = depth * np.exp(-(((X - xc - wander) / 0.25) ** 2)); D += r
        ridge = 0.08 * np.exp(-(((X + 2.175) / 0.45) ** 2)) + 0.08 * np.exp(-(((X - 2.175) / 0.45) ** 2)) + 0.10 * np.exp(-((X / 0.6) ** 2))
        clods = 0.05 * (fbm(X, Y, seed + 30, 1.4, 3) - 0.5) * 2 + 0.02 * (fbm(X, Y, seed + 31, 5.0, 2) - 0.5) * 2
        edge = -0.28 * smooth(3.1, 4.0, np.abs(X))
        return H - D + ridge + clods + edge, D
    nx, ny = (36, 90) if lod == 0 else (10, 30)
    p.datum = grid_surface(p, nx, ny, -4.0, 4.0, -12.0, 12.0, h, lambda x, y, hc, d: "mud_wet" if d > 0.075 else "mud_crest"); return p


# ------------------------------------------------------------------------------------------------------------------------------------------------ compacted earth road (8 m x 24 m)
def earth_compacted(lod=0):
    """A compacted laterite road in the dry season: crowned, shallow wheel tracks, small potholes, runoff gullies cut across the shoulders and dust drifted along the edges."""
    p = Part("surface"); p.planar_xy = True; seed = 41
    def h(X, Y):
        H = 0.12 * (1 - (X / 4.0) ** 2); D = np.zeros_like(X)
        for xc in (-2.6, -1.0, 1.0, 2.6): D += 0.028 * np.exp(-(((X - xc) / 0.32) ** 2))
        H = H - D + 0.014 * (fbm(X, Y, seed, 1.2, 3) - 0.5) * 2 + 0.006 * (fbm(X, Y, seed + 3, 6.0, 2) - 0.5) * 2
        for yc in (-8.5, -2.5, 4.0, 9.5): H -= 0.11 * np.exp(-(((Y - yc - 0.4 * X) / 0.35) ** 2)) * smooth(2.3, 3.4, np.abs(X))
        for (cx, cy, r, d) in ((-0.6, -5.0, 0.55, 0.07), (1.5, 2.0, 0.45, 0.06), (-1.9, 7.5, 0.5, 0.07)): H -= d * np.clip(1 - ((X - cx) ** 2 + (Y - cy) ** 2) / r ** 2, 0, 1) ** 1.2
        dust = smooth(3.0, 3.9, np.abs(X)) * (0.4 + 0.6 * fbm(X, Y, seed + 9, 0.8, 2)); H += 0.07 * dust
        return H, dust
    nx, ny = (36, 90) if lod == 0 else (10, 30)
    p.datum = grid_surface(p, nx, ny, -4.0, 4.0, -12.0, 12.0, h, lambda x, y, hc, d: "sand_dusty" if d > 0.55 else "earth_compacted"); return p


# ------------------------------------------------------------------------------------------------------------------------------------------------ loose sandy road (8 m x 24 m)
def sand_loose(lod=0):
    """A loose sand road: wind ripples, soft wheel tracks with a hard pan showing in the bottoms and sand drifted into low dunes along both edges."""
    p = Part("surface"); p.planar_xy = True; seed = 61
    def h(X, Y):
        warp = fbm(X, Y, seed, 0.5, 2); H = 0.018 * np.sin(1.0 * Y + 4.0 * warp + 0.3 * X) + 0.02 * (fbm(X, Y, seed + 2, 0.9, 3) - 0.5) * 2; D = np.zeros_like(X)
        for xc in (-2.8, -1.0, 1.0, 2.8): D += 0.08 * np.exp(-(((X - xc + 0.12 * np.sin(0.4 * Y + xc)) / 0.30) ** 2))
        drift = 0.14 * smooth(2.8, 3.9, np.abs(X)) * (0.5 + fbm(X, Y, seed + 5, 0.6, 3))
        return H - D + drift, D
    nx, ny = (36, 90) if lod == 0 else (10, 30)
    p.datum = grid_surface(p, nx, ny, -4.0, 4.0, -12.0, 12.0, h, lambda x, y, hc, d: "earth_compacted" if d > 0.06 else "sand_dusty"); return p


# ------------------------------------------------------------------------------------------------------------------------------------------------ cracked asphalt (7 m x 24 m)
PATCHES = ((-1.0, -6.0, 2.4, 1.3), (1.2, -1.0, 1.6, 2.6), (-1.5, 5.0, 1.2, 1.0), (0.5, 9.0, 2.0, 0.9))


def asphalt_cracked(lod=0):
    """Old asphalt: cracks (texture), four raised repair patches of different shapes, raveled edges that break up and expose the earth beneath and faint rutting in the wheel paths."""
    p = Part("surface"); p.planar_xy = True; seed = 81
    def h(X, Y):
        H = 0.003 * (fbm(X, Y, seed, 3.0, 2) - 0.5) * 2
        for xc in (-1.0, 1.0): H -= 0.016 * np.exp(-(((X - xc) / 0.5) ** 2))
        rav = smooth(2.9, 3.5, np.abs(X)) * (0.35 + 0.65 * fbm(np.zeros_like(X), Y, seed + 3, 0.9, 3)); H -= 0.06 * rav
        pm = np.zeros_like(X)
        for (cx, cy, w, l) in PATCHES: pm = np.maximum(pm, ((np.abs(X - cx) < w / 2) & (np.abs(Y - cy) < l / 2)).astype(float))
        return H + 0.012 * pm, np.stack([rav, pm])[0] + 10 * pm
    def key(x, y, hc, a):
        if a >= 5: return "asphalt_patch"
        return "pothole_earth" if a > 0.55 else "asphalt_cracked"
    nx, ny = (28, 96) if lod == 0 else (8, 24)
    p.datum = grid_surface(p, nx, ny, -3.5, 3.5, -12.0, 12.0, h, key); return p


# ------------------------------------------------------------------------------------------------------------------------------------------------ pothole set (7 m x 12 m)
def pothole_set(lod=0):
    """A damaged asphalt patch with nine potholes of 0.6 to 1.4 m across and 7 to 20 cm deep, ragged edges, a chipped rim and the earth and stone base showing at the bottom."""
    p = Part("surface"); p.planar_xy = True; rnd = np.random.default_rng(5); holes = []
    while len(holes) < 9:
        c = (rnd.uniform(-2.6, 2.6), rnd.uniform(-4.8, 4.8)); r = rnd.uniform(0.30, 0.70)
        if all(math.hypot(c[0] - o[0], c[1] - o[1]) > (r + o[2]) * 1.25 for o in holes): holes.append((c[0], c[1], r, rnd.uniform(0.07, 0.20), rnd.uniform(0, 6.28), rnd.uniform(0, 6.28)))
    def h(X, Y):
        H = 0.003 * (fbm(X, Y, 91, 3.0, 2) - 0.5) * 2; inside = np.zeros_like(X)
        for (cx, cy, r, d, ph1, ph2) in holes:
            dx, dy = X - cx, Y - cy; rr = np.hypot(dx, dy); th = np.arctan2(dy, dx); re = r * (1 + 0.25 * np.sin(3 * th + ph1) + 0.15 * np.sin(5 * th + ph2))
            t = np.clip(1 - (rr / re) ** 2, 0, 1); H -= d * t ** 1.3; H += 0.008 * np.exp(-(((rr - re) / 0.08) ** 2)); inside = np.maximum(inside, (rr < re * 1.02).astype(float))
        return H, inside
    nx, ny = (42, 72) if lod == 0 else (10, 18)
    p.datum = grid_surface(p, nx, ny, -3.5, 3.5, -6.0, 6.0, h, lambda x, y, hc, a: "pothole_earth" if a > 0.5 else "asphalt_cracked"); return p


# ------------------------------------------------------------------------------------------------------------------------------------------------ puddles (about 8 m x 8 m)
def puddle_set(lod=0):
    """Six irregular puddles lying on the ground: three brown over earth, three grey over asphalt, 0.8 to 3 m across, each a lobed outline filled with a triangle fan just above the ground."""
    p = Part("water"); rnd = np.random.default_rng(11); seg = 28 if lod == 0 else 10
    spec = [(-2.4, -2.0, 1.5, "water_brown"), (1.8, 2.6, 1.0, "water_brown"), (-0.6, 3.0, 0.7, "water_brown"), (2.2, -2.4, 1.2, "water_rain"), (-2.6, 2.2, 0.45, "water_rain"), (0.2, -0.4, 0.9, "water_rain")]
    for (cx, cy, R, key) in spec:
        ph = rnd.uniform(0, 6.28, 3); pts = []
        for k in range(seg):
            th = 2 * math.pi * k / seg; r = R * (1 + 0.28 * math.sin(3 * th + ph[0]) + 0.14 * math.sin(5 * th + ph[1]) + 0.07 * math.sin(9 * th + ph[2])); pts.append((cx + r * math.cos(th), cy + r * math.sin(th), 0.01))
        for k in range(seg): p.face([(cx, cy, 0.01), pts[k], pts[(k + 1) % seg]], key)
    p.datum = 0.01                                                                          # the water plane sits 1 cm above the ground it lies on
    return p


# ------------------------------------------------------------------------------------------------------------------------------------------------ flood sheet (14 m x 8 m)
def flood_sheet(lod=0):
    """Standing flood water over a carriageway: a ragged, gently rippled sheet of turbid brown water (the water shader, reflection and flow are engine work, RB-21)."""
    p = Part("water"); seed = 101
    def h(X, Y): return 0.02 + 0.007 * (fbm(X, Y, seed, 0.7, 3) - 0.5) * 2, np.zeros_like(X)
    def mask(x, y): return (x / 7.0) ** 2 + (y / 4.0) ** 2 + 0.55 * (fbm(x, y, seed + 4, 0.35, 3) - 0.5) < 0.92
    nx, ny = (28, 16) if lod == 0 else (7, 4)
    p.datum = grid_surface(p, nx, ny, -7.0, 7.0, -4.0, 4.0, h, lambda x, y, hc, a: "water_brown", mask=mask); return p
