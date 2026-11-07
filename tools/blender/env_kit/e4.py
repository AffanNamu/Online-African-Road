"""Batch E4 (2026-11-08): corrections to the environmental kit found in the in-game review of 2026-11-07 (docs/IN_GAME_REVIEW_2026-11-07.md). NEW VERSIONS of existing asset ids; the frozen E1 to E3 builders and their
freeze manifests are untouched (this module only adds builders, materials and textures; `env_kit.build_e4` builds them as the next version of each asset).

  aro_road_pothole_set_a   potholes whose outline is cut EXACTLY along the contour (no stair-stepped squares), a steep wall and a flat floor, and a dark gravel base instead of the bright orange fill
  aro_road_mud_rutted_a    wheel ruts with a flat floor and steep walls (a trough that holds water) instead of V-shaped slots between rounded crowns; a finer grid across the ruts
  aro_water_flood_sheet_a  a smooth shoreline, a deeper and less transparent centre, a lighter shallow rim and a wet fringe of mud around the edge, so the water no longer reads as a pale translucent band
  rg_ngn_hausa_compound_a, rg_ngn_hausa_compound_b, rg_ngn_hausa_wall_gate_set_a
                           the wall finish is red-pigmented earth (kasa) render, as the documentary sources describe (R-024, R-027), instead of the grey-chipped sand-cement texture; geometry unchanged

Everything here is a static mesh: no mud physics, flooding behaviour or water shader (RB-18 to RB-25 are proposals). Colours are first-pass estimates from documentary sources; no colour value is established (M-01, M-15)."""
from __future__ import annotations
import math
import numpy as np
from aro_blender import materials as AM
from kano_kit import coplanar as CP
from kano_kit.geo import Part
from . import surfaces as S, hausa as H
from .textures import _fbm, _noise, _normal_from_height, pbr_material, water_color, water_normal, SIZE, _grid

# ------------------------------------------------------------------------------------------------------------------------------------------------ materials and textures (additive)
NEW_MATERIALS = {
    "kasa_plaster": ((0.57, 0.39, 0.28), 0.94, 0.0, 0.0, "red-pigmented earth (kasa) render of traditional Hausa walls, a reddish-brown finish (R-024, R-027); first-pass colour, not established"),
    "pothole_base": ((0.27, 0.21, 0.16), 0.96, 0.0, 0.0, "dark compacted gravel and earth base inside a pothole"),
    "water_flood": ((0.24, 0.17, 0.11), 0.03, 0.0, 0.0, "deep turbid flood water: darker and less transparent than the shallow rim (water_brown)"),
}
for _k, _v in NEW_MATERIALS.items(): AM.LIBRARY.setdefault(_k, _v)
AM.TEXTURED.setdefault("kasa_plaster", {"texture": "T_env_kasa_BaseColor", "normal": "T_env_kasa_Normal"})
AM.TEXTURED.setdefault("pothole_base", {"texture": "T_env_potholebase_BaseColor", "normal": "T_env_potholebase_Normal"})
AM.TEXTURED.setdefault("water_flood", {"texture": "T_env_waterflood_BaseColor", "normal": "T_env_water_Normal"})
AM.ALPHA.setdefault("water_flood", 0.90)


def kasa_maps(size=SIZE, seed=241):
    """Earth (kasa) render: warm red-brown, trowel streaks, straw flecks, hairline drying cracks, faint lighter dust. No grey block chips (the sand-cement texture's chipped patches read as grey blotches in the game)."""
    f = _fbm(size, 4, 4, seed); fine = _fbm(size, 60, 2, seed + 2); trowel = np.tile(_noise(size, 14, seed + 3)[:, :1], (1, size)); rnd = np.random.default_rng(seed)
    base = np.array((0.57, 0.39, 0.28)); col = base[None, None, :] * (0.86 + 0.28 * f[..., None]) * (0.97 + 0.06 * (fine[..., None] - 0.5))
    col *= (1 - 0.10 * np.clip(trowel - 0.5, 0, 1) * 2)[..., None]
    h = 0.4 * f + 0.5 * fine + 0.15 * trowel
    crack = np.abs(_fbm(size, 7, 3, seed + 6) - 0.5) < 0.010; col[crack] *= 0.72; h[crack] -= 0.35
    for _ in range(420):                                                                               # straw flecks: short light strokes
        y0, x0 = rnd.integers(0, size, 2); L = int(rnd.integers(3, 9)); xs = (x0 + np.arange(L)) % size; col[y0, xs] = col[y0, xs] * 0.5 + np.array((0.82, 0.70, 0.45)) * 0.5
    return np.clip(col, 0, 1), _normal_from_height(h, 2.6)


def potholebase_maps(size=SIZE, seed=251):
    """Dark compacted gravel and earth: fine pebbles on a brown matrix."""
    f = _fbm(size, 5, 4, seed); fine = _fbm(size, 40, 3, seed + 1); peb = _fbm(size, 70, 2, seed + 2) > 0.64
    col = np.array((0.27, 0.21, 0.16))[None, None, :] * (0.78 + 0.40 * f[..., None]); h = 0.5 * f + 0.4 * fine
    col[peb] = col[peb] * 0.45 + np.array((0.42, 0.37, 0.31)) * 0.55 * (0.85 + 0.3 * fine[peb][:, None]); h[peb] += 0.55
    return np.clip(col, 0, 1), _normal_from_height(h, 3.6)


E4_TEXTURES = {"kasa_plaster": ("T_env_kasa_BaseColor", "T_env_kasa_Normal", kasa_maps), "pothole_base": ("T_env_potholebase_BaseColor", "T_env_potholebase_Normal", potholebase_maps),
               "water_flood": ("T_env_waterflood_BaseColor", "T_env_water_Normal", lambda: (water_color((0.24, 0.17, 0.11), seed=211), water_normal()))}       # key -> (base colour image, normal image, generator)


def apply_e4_pbr(keys=None) -> list:
    """Wires the E4 texture sets into whichever E4 materials exist in the file."""
    import bpy
    done = []
    for key, (bn, nn, gen) in E4_TEXTURES.items():
        if bpy.data.materials.get(f"M_{key}") is None or (keys and key not in keys): continue
        base, nrm = gen(); pbr_material(key, bn, base, nn, nrm); done.append(key)
    return done


# ------------------------------------------------------------------------------------------------------------------------------------------------ surfaces: exact contours
def _clip(poly, k, keep_inside):
    """Sutherland-Hodgman against one scalar field: keeps the part of the convex polygon where field k <= 0 (inside) or > 0; field values are interpolated linearly along edges."""
    out = []; n = len(poly)
    for i in range(n):
        u, v = poly[i], poly[(i + 1) % n]; iu = (u[k] <= 0) == keep_inside; iv = (v[k] <= 0) == keep_inside
        if iu: out.append(u)
        if iu != iv:
            a, b = (u, v) if (u[0], u[1]) <= (v[0], v[1]) else (v, u); t = a[k] / (a[k] - b[k]); out.append(tuple(p + t * (q - p) for p, q in zip(a, b)))     # canonical edge order: neighbours get identical points
    return out


def clipped_cells(xs, ys, phis, key_fn):
    """Two triangles per grid cell, each split along the zero contour of every field in `phis` (functions X, Y -> array; <= 0 is inside). key_fn(sides) -> material key or None (dropped); `sides` is a tuple of booleans
    (inside each field). Returns [(key, [(x, y), ...]), ...] convex polygons whose corners on the contours are exact, so outlines are smooth at any grid size."""
    X, Y = np.meshgrid(xs, ys, indexing="ij"); F = [np.asarray(f(X, Y), float) for f in phis]; items = []
    def pt(i, j): return (float(xs[i]), float(ys[j])) + tuple(float(f[i, j]) for f in F)
    for i in range(len(xs) - 1):
        for j in range(len(ys) - 1):
            a, b, c, d = pt(i, j), pt(i + 1, j), pt(i + 1, j + 1), pt(i, j + 1)
            for tri in ((a, b, c), (a, c, d)):
                parts = [(list(tri), ())]
                for k in range(len(phis)):
                    nxt = []
                    for poly, sides in parts:
                        sg = {p[2 + k] <= 0 for p in poly}
                        if len(sg) == 1: nxt.append((poly, sides + (sg.pop(),))); continue
                        for inside in (True, False):
                            q = _clip(poly, 2 + k, inside)
                            if len(q) >= 3: nxt.append((q, sides + (inside,)))
                    parts = nxt
                for poly, sides in parts:
                    key = key_fn(sides)
                    if key is not None: items.append((key, [(p[0], p[1]) for p in poly]))
    return items


def emit(p: Part, groups) -> float:
    """groups = [(items, hfun, dz)]: builds the faces (shared vertices, normals up, smooth) at z = hfun(x, y) + dz, shifted so the lowest vertex is z = 0; returns the datum (how far above z = 0 the nominal plane h = 0 sits)."""
    pts = {}
    for items, hfun, dz in groups:
        for _, poly in items:
            for x, y in poly: pts.setdefault((round(x, 6), round(y, 6)), None)
    keys = list(pts); X = np.array([k[0] for k in keys]); Y = np.array([k[1] for k in keys]); ztab = {}
    for items, hfun, dz in groups:
        H_ = np.asarray(hfun(X, Y), float) + dz
        for (idx, k) in enumerate(keys): ztab.setdefault(id(hfun), {})[k] = float(H_[idx])
    used = set()
    for items, hfun, dz in groups:
        for _, poly in items:
            for x, y in poly: used.add((id(hfun), (round(x, 6), round(y, 6))))
    zmin = min(ztab[h][k] for h, k in used); verts = {}
    def vert(gid, k):
        key = (gid, k)
        if key not in verts: verts[key] = p.bm.verts.new((k[0], k[1], ztab[gid][k] - zmin))
        return verts[key]
    for items, hfun, dz in groups:
        gid = id(hfun)
        for key, poly in items:
            kp = [(round(x, 6), round(y, 6)) for x, y in poly]
            for t in range(1, len(kp) - 1):
                tri = [kp[0], kp[t], kp[t + 1]]; area = (tri[1][0] - tri[0][0]) * (tri[2][1] - tri[0][1]) - (tri[2][0] - tri[0][0]) * (tri[1][1] - tri[0][1])
                if abs(area) < 4e-7: continue                                                   # slivers (area under 0.2 mm2) where a contour passes a corner: dropped
                if area < 0: tri = [tri[0], tri[2], tri[1]]
                try: f = p.bm.faces.new([vert(gid, q) for q in tri])
                except ValueError: continue
                f.material_index = p.mi(key); f.smooth = True
    return -zmin


def _lines(a, b, bands, fine, coarse):
    """Grid lines from a to b: spacing `fine` inside the bands [(lo, hi), ...], `coarse` elsewhere (so the mesh is dense only where the detail is)."""
    pts = {round(a, 6), round(b, 6)}
    for lo, hi in bands:
        lo, hi = max(a, lo), min(b, hi)
        if hi > lo: pts.update(round(v, 6) for v in np.linspace(lo, hi, max(2, int(round((hi - lo) / fine)) + 1)))
    s = sorted(pts); out = [s[0]]
    for v in s[1:]:
        gap = v - out[-1]
        if gap > coarse * 1.5: out.extend(np.linspace(out[-1], v, int(math.ceil(gap / coarse)) + 1)[1:])
        else: out.append(v)
    return np.array(sorted(set(round(float(v), 6) for v in out)))


# ------------------------------------------------------------------------------------------------------------------------------------------------ potholes (7 m x 12 m)
def pothole_set2(lod=0):
    """The same nine holes as the E3 patch (same seed, same places) cut exactly along their ragged outline: a steep wall, a flat floor and a chipped rim, on a dark gravel base (M_pothole_base), asphalt around."""
    p = Part("surface"); p.planar_xy = True; rnd = np.random.default_rng(5); holes = []
    while len(holes) < 9:
        c = (rnd.uniform(-2.6, 2.6), rnd.uniform(-4.8, 4.8)); r = rnd.uniform(0.30, 0.70)
        if all(math.hypot(c[0] - o[0], c[1] - o[1]) > (r + o[2]) * 1.25 for o in holes): holes.append((c[0], c[1], r, rnd.uniform(0.07, 0.20), rnd.uniform(0, 6.28), rnd.uniform(0, 6.28)))
    def outline(dx, dy, r, p1, p2): th = np.arctan2(dy, dx); return r * (1 + 0.25 * np.sin(3 * th + p1) + 0.15 * np.sin(5 * th + p2))
    def height(X, Y):
        H_ = 0.003 * (S.fbm(X, Y, 91, 3.0, 2) - 0.5) * 2
        for (cx, cy, r, d, p1, p2) in holes:
            dx, dy = X - cx, Y - cy; rr = np.hypot(dx, dy); re = outline(dx, dy, r, p1, p2)
            t = np.clip(1 - (rr / re) ** 3, 0, 1); H_ = H_ - d * t ** 0.55 + 0.008 * np.exp(-(((rr - re) / 0.08) ** 2))
        return H_
    def phi(X, Y):
        v = np.full_like(X, 9.0)
        for (cx, cy, r, d, p1, p2) in holes:
            dx, dy = X - cx, Y - cy; v = np.minimum(v, np.hypot(dx, dy) / outline(dx, dy, r, p1, p2) - 1.0)
        return v
    nx, ny = (42, 74) if lod == 0 else (8, 14)
    xs, ys = np.linspace(-3.5, 3.5, nx + 1), np.linspace(-6.0, 6.0, ny + 1)
    items = clipped_cells(xs, ys, [phi], lambda s: "pothole_base" if s[0] else "asphalt_cracked")
    p.datum = emit(p, [(items, height, 0.0)]); return p


# ------------------------------------------------------------------------------------------------------------------------------------------------ rutted mud road (8 m x 24 m)
def mud_rutted2(lod=0):
    """A clay road after heavy rain with the E3 layout (four wandering ruts at x = -3, -1.35, 1.35, 3) but flat-floored, steep-walled ruts 0.30 m wide and 0.10 to 0.22 m deep between low flat ridges: troughs that hold water,
    glossy dark mud (M_mud_wet) on the floor, drier mud (M_mud_crest) on walls, ridges and banks. The grid is SHEARED so its lines follow the wandering ruts (the rut profile is evaluated across the rut, never on a slant),
    which keeps floor and wall edges straight in the mesh and the material boundary free of stair-steps; dense lines only across the ruts."""
    p = Part("surface"); p.planar_xy = True; seed = 21; ruts = (-3.0, -1.35, 1.35, 3.0); half = 0.30
    def wander(Y, xc, k): return 0.20 * np.sin(0.33 * Y + xc * 1.7) + 0.16 * (S.fbm(np.full_like(Y, xc * 3.1), Y, seed + k, 0.16, 2) - 0.5)
    def depth(Y, xc, k): return np.clip(0.16 + 0.12 * (S.fbm(np.full_like(Y, xc * 2.3), Y, seed + 7 + k, 0.11, 3) - 0.5) * 2, 0.10, 0.22)
    def field(U, Y):
        sh = np.zeros_like(U)
        for k, xc in enumerate(ruts): sh = sh + wander(Y, xc, k) * (1 - S.smooth(0.3, 1.4, np.abs(U - xc)))       # grid lines near a rut follow its path; the shear fades out between ruts (monotonic: no fold-over)
        X = U + sh; H_ = 0.055 * (1 - (X / 4.0) ** 2); D = np.zeros_like(U)
        for k, xc in enumerate(ruts): D = np.maximum(D, depth(Y, xc, k) * (1 - S.smooth(half, half + 0.20, np.abs(U - xc))))     # flat floor, steep wall, straight in grid space
        clods = 0.018 * (S.fbm(X, Y, seed + 30, 1.4, 3) - 0.5) * 2 + 0.008 * (S.fbm(X, Y, seed + 31, 5.0, 2) - 0.5) * 2
        edge = -0.28 * S.smooth(3.1, 4.0, np.abs(X)); floor_texture = 0.006 * (S.fbm(X, Y, seed + 40, 0.9, 2) - 0.5) * 2 * (D > 0.05)
        return X, H_ - D + clods + edge + floor_texture, D
    if lod == 0: us = _lines(-4.0, 4.0, [(xc - 0.8, xc + 0.8) for xc in ruts], 0.10, 0.45); ys = np.linspace(-12.0, 12.0, 49)
    else: us = np.linspace(-4.0, 4.0, 20); ys = np.linspace(-12.0, 12.0, 18)
    U, Y = np.meshgrid(us, ys, indexing="ij"); Xw, Hf, Df = field(U, Y); dc = (Df[:-1, :-1] + Df[1:, :-1] + Df[1:, 1:] + Df[:-1, 1:]) / 4; zmin = float(Hf.min()); verts = {}
    def vert(i, j):
        if (i, j) not in verts: verts[(i, j)] = p.bm.verts.new((float(Xw[i, j]), float(ys[j]), float(Hf[i, j] - zmin)))
        return verts[(i, j)]
    for i in range(len(us) - 1):
        for j in range(len(ys) - 1):
            f = p.bm.faces.new([vert(i, j), vert(i + 1, j), vert(i + 1, j + 1), vert(i, j + 1)])
            f.material_index = p.mi("mud_wet" if dc[i, j] > 0.075 else "mud_crest"); f.smooth = True
    p.datum = -zmin; return p


# ------------------------------------------------------------------------------------------------------------------------------------------------ flood sheet (14 m x 8 m)
def flood_sheet2(lod=0):
    """Standing flood water 14 x 8 m with a smooth ragged shoreline (exact contour), a deep centre (M_water_flood, 90% opaque, dark), a shallow rim (M_water_brown, more transparent) and a 0.4 m wet fringe of mud
    (M_mud_wet) on the ground around the edge, 14 mm lower than the water. No flow, depth query, bow wave or reflection (RB-21, RB-22)."""
    p = Part("water"); seed = 101
    def metric(X, Y): return (X / 7.0) ** 2 + (Y / 4.0) ** 2 + 0.55 * (S.fbm(X, Y, seed + 4, 0.35, 3) - 0.5)
    def phi_out(X, Y): return metric(X, Y) - 0.92
    def phi_deep(X, Y): return metric(X, Y) - 0.62
    def phi_fringe(X, Y): return metric(X, Y) - 1.12
    def ripples(X, Y): return 0.02 + 0.006 * (S.fbm(X, Y, seed, 0.7, 3) - 0.5) * 2
    def ground(X, Y): return 0.006 + 0.002 * (S.fbm(X, Y, seed + 9, 1.1, 2) - 0.5) * 2
    nx, ny = (40, 22) if lod == 0 else (6, 4)
    xs, ys = np.linspace(-8.0, 8.0, nx + 1), np.linspace(-5.0, 5.0, ny + 1)
    water = clipped_cells(xs, ys, [phi_out, phi_deep], lambda s: None if not s[0] else ("water_flood" if s[1] else "water_brown"))
    ring = clipped_cells(xs, ys, [phi_fringe, phi_out], lambda s: "mud_wet" if (s[0] and not s[1]) else None)
    p.datum = emit(p, [(water, ripples, 0.0), (ring, ground, 0.0)]); return p


# ------------------------------------------------------------------------------------------------------------------------------------------------ Hausa walls: earth render instead of sand-cement
EARTH_RENDER = {"plaster_weathered": "kasa_plaster"}


def dedupe(part, drop_keys=("kasa_plaster",)) -> int:
    """Deletes faces that repeat an earlier face's corners exactly (to 0.1 mm), whatever the material: the same surface built twice (the E3 Hausa builders do it for 2 or 3 faces and for wall-top slabs that coincide with
    the earth roof, which would z-fight). Of a duplicate group the face whose material is NOT in `drop_keys` is kept (the roof earth rather than the wall render under it). No vertex is touched."""
    import bmesh
    groups = {}
    for f in part.bm.faces: groups.setdefault(tuple(sorted(tuple(round(c, 4) for c in v.co) for v in f.verts)), []).append(f)
    kill = []
    for fs in groups.values():
        if len(fs) < 2: continue
        fs.sort(key=lambda f: part.keys[f.material_index] in drop_keys); kill.extend(fs[1:])
    if kill: bmesh.ops.delete(part.bm, geom=kill, context="FACES_ONLY")
    return len(kill)


def remap(f, mapping):
    """The builder's part with materials renamed and exact duplicate faces removed (vertices untouched)."""
    def make(lod):
        part = f(lod); part.keys = [mapping.get(k, k) for k in part.keys]; dedupe(part); return part
    return make


# ------------------------------------------------------------------------------------------------------------------------------------------------ the E4 specs
SPECS_E4: dict = {}
DATUM_E4: dict = {}


def _patch(asset_id, fn):
    def make(lod):
        part = fn(lod)
        if lod == 0: DATUM_E4[asset_id] = round(part.datum, 4)
        return part
    return make


def _separated(f):
    def make(lv):
        part = f(lv); CP.separate(part); return part
    return make


def spec4(asset_id, kind, lods, note, separate=False):
    SPECS_E4[asset_id] = dict(id=asset_id, kind=kind, lods={lv: (lambda lv=lv, f=(_separated(f) if separate else f): f(lv)) for lv, f in lods.items()}, col=None, textured=False, note=note)


spec4("aro_road_pothole_set_a", "roads", {0: _patch("aro_road_pothole_set_a", pothole_set2), 1: _patch("aro_road_pothole_set_a", pothole_set2)},
      "E4 correction of the potholes found in the in-game review: nine potholes (same places as E3) cut exactly along their outline, steep wall, flat floor, dark gravel base (R-016); visual only; the nominal surface plane sits at the datum above z = 0")
spec4("aro_road_mud_rutted_a", "roads", {0: _patch("aro_road_mud_rutted_a", mud_rutted2), 1: _patch("aro_road_mud_rutted_a", mud_rutted2)},
      "E4 correction of the rutted mud road: four wandering flat-floored, steep-walled wheel ruts between low ridges (R-020, R-021); the nominal surface plane sits at the datum above z = 0")
spec4("aro_water_flood_sheet_a", "environments", {0: _patch("aro_water_flood_sheet_a", flood_sheet2), 1: _patch("aro_water_flood_sheet_a", flood_sheet2)},
      "E4 correction of the flood sheet: smooth shoreline, dark deep centre, lighter shallow rim and a wet mud fringe (R-019); the water shader, depth query and flow are engine work (RB-21, RB-22)")
spec4("rg_ngn_hausa_compound_a", "buildings", {0: remap(H.hausa_compound_a, EARTH_RENDER), 1: remap(H.hausa_compound_a, EARTH_RENDER)},
      "E4: the walls of the Hausa-style compound finished in red-pigmented earth (kasa) render as the documentary sources describe (R-024, R-027); geometry unchanged; proportions are design choices (M-01)", separate=True)
spec4("rg_ngn_hausa_compound_b", "buildings", {0: remap(H.hausa_compound_b, EARTH_RENDER), 1: remap(H.hausa_compound_b, EARTH_RENDER)},
      "E4: the second Hausa-style compound type with earth (kasa) render (R-024, R-027); geometry unchanged (M-01)", separate=True)
spec4("rg_ngn_hausa_wall_gate_set_a", "buildings", {0: remap(H.hausa_wall_gate_set_a, EARTH_RENDER), 1: remap(H.hausa_wall_gate_set_a, EARTH_RENDER)},
      "E4: the Hausa-style street frontage with earth (kasa) render (R-024, R-027); geometry unchanged (M-01)", separate=True)
