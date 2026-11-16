"""Exports the crowd figures for the game: one file of meshes and pre-posed frames (people.bytes) and one texture atlas (people_atlas.png).

For every design of figure.DESIGNS:
  * two levels of detail, reduced from the full figure (about 3,200 and 900 triangles) by Blender's collapse decimation; LOD 1 is reduced from LOD 0 and shares its texture;
  * a texture painted from the FULL figure, texel by texel: for each texel the nearest point of the full model gives the colour (the face, lips, brows, hair and eyes of its vertices, and the cloth
    patterns evaluated at that very point, so prints and thread work stay sharp after the mesh is reduced), times the ambient occlusion of the standing figure. The face gets a larger share of the
    texture than its size (its UV islands are unwrapped from a head scaled up);
  * one tile per skin tone (the skin, the hair, the eyes) and one per colourway (the clothes), in a 2048 x 2048 atlas of 256 px tiles: a person is any skin tone with any colourway;
  * 8 walk frames and 4 idle frames posed with the rig (rig.walking, rig.standing), positions and normals, in the game's axes (Unity: x right, y up, z forward; the figure faces +z).
Deterministic: the same code and the same MakeHuman data give the same bytes.

Run under Blender's Python (bpy):  PYTHONPATH=tools/blender python -m people_kit.export --out <dir> [design ids]
The file format is read by ARO.NetCore.PeopleAsset (Assets/_Project/Scripts/NetCore/PeopleAsset.cs); the two must change together (VERSION)."""
from __future__ import annotations
import argparse, math, struct, sys
from pathlib import Path
import numpy as np
import bpy
from mathutils import Vector
from mathutils.bvhtree import BVHTree
from mathutils.kdtree import KDTree
from . import figure as FIG
from .rig import Rig, walking, standing

VERSION = 1
LOD_TRIS = (3200, 900)
SMALL_PART = 420                                            # parts of at most this many triangles are not reduced at LOD 0
WALK_FRAMES, IDLE_FRAMES = 8, 4
TILE, ATLAS = 256, 2048
POS_SCALE = 4.0 / 32767.0                                   # int16 positions: +-4 m at 0.12 mm
AO_RAYS, AO_DIST, AO_STRENGTH = 28, 0.22, 0.55
HEAD_UV_SCALE = 1.9                                         # the face gets this much more texture (linear) than its size alone would give

COLOURWAYS = {                                              # three per design (the game's CrowdAgent.Colourway is 0..2), colours in sRGB
    "man_kaftan_cap": [((0.90, 0.89, 0.86), (0.55, 0.12, 0.14), (0.22, 0.14, 0.09)), ((0.55, 0.72, 0.86), (0.12, 0.16, 0.34), (0.20, 0.13, 0.08)), ((0.86, 0.80, 0.66), (0.40, 0.10, 0.12), (0.24, 0.15, 0.09))],
    "man_babban_riga": [((0.16, 0.28, 0.62), (0.92, 0.92, 0.90), (0.82, 0.66, 0.30)), ((0.92, 0.91, 0.88), (0.90, 0.88, 0.84), (0.78, 0.62, 0.28)), ((0.10, 0.40, 0.42), (0.86, 0.84, 0.80), (0.82, 0.66, 0.30))],
    "man_shirt_trousers": [((0.30, 0.48, 0.70), (0.13, 0.15, 0.22), (0.07, 0.055, 0.05)), ((0.90, 0.90, 0.88), (0.25, 0.25, 0.27), (0.22, 0.13, 0.08)), ((0.82, 0.45, 0.18), (0.55, 0.48, 0.34), (0.20, 0.13, 0.08))],
    "man_kaftan_plain": [((0.55, 0.57, 0.58), (0.92, 0.92, 0.90), (0.22, 0.14, 0.09)), ((0.86, 0.80, 0.66), (0.40, 0.26, 0.15), (0.22, 0.14, 0.09)), ((0.24, 0.42, 0.28), (0.92, 0.92, 0.90), (0.22, 0.14, 0.09))],
    "woman_hijab_wrapper": [((0.12, 0.45, 0.25), (0.86, 0.66, 0.18), (0.08, 0.08, 0.08)), ((0.42, 0.20, 0.50), (0.88, 0.52, 0.66), (0.08, 0.08, 0.08)), ((0.50, 0.12, 0.14), (0.88, 0.48, 0.16), (0.08, 0.08, 0.08))],
    "woman_hijab_long": [((0.10, 0.36, 0.38), (0.86, 0.80, 0.68), (0.06, 0.06, 0.06)), ((0.80, 0.50, 0.60), (0.42, 0.20, 0.50), (0.06, 0.06, 0.06)), ((0.12, 0.16, 0.34), (0.55, 0.72, 0.86), (0.06, 0.06, 0.06))],
    "woman_abaya": [((0.05, 0.05, 0.06), (0.06, 0.06, 0.07), (0.05, 0.05, 0.05)), ((0.08, 0.10, 0.20), (0.05, 0.05, 0.06), (0.05, 0.05, 0.05)), ((0.20, 0.20, 0.22), (0.05, 0.05, 0.06), (0.05, 0.05, 0.05))],
    "woman_market_headload": [((0.80, 0.36, 0.10), (0.86, 0.66, 0.18), (0.10, 0.20, 0.45)), ((0.12, 0.45, 0.25), (0.86, 0.66, 0.18), (0.50, 0.12, 0.14)), ((0.42, 0.20, 0.50), (0.88, 0.48, 0.16), (0.10, 0.20, 0.45))],
    "boy_school": [((0.93, 0.93, 0.91), (0.12, 0.16, 0.34), (0.30, 0.18, 0.10)), ((0.62, 0.55, 0.38), (0.36, 0.25, 0.15), (0.08, 0.08, 0.08)), ((0.55, 0.72, 0.86), (0.12, 0.16, 0.34), (0.40, 0.10, 0.12))],
    "girl_school_hijab": [((0.12, 0.18, 0.36), (0.94, 0.94, 0.93), (0.06, 0.06, 0.06)), ((0.18, 0.40, 0.24), (0.94, 0.94, 0.93), (0.06, 0.06, 0.06)), ((0.40, 0.12, 0.14), (0.94, 0.94, 0.93), (0.06, 0.06, 0.06))],
}
CHILD = {"boy_school", "girl_school_hijab"}
ROBE = {"man_kaftan_cap", "man_kaftan_plain", "man_babban_riga", "woman_hijab_wrapper", "woman_hijab_long", "woman_abaya", "woman_market_headload", "girl_school_hijab"}
SKINS = 3


def stride_of(design):
    """How far the legs swing (rig.walking): the game moves a walker 1.4 m per cycle (0.95 m for a child), which this swing matches; a robe shortens it a little."""
    return (0.85 if design in CHILD else 1.0) * (0.85 if design in ROBE else 1.0)


def frames_of(design):
    """The 12 poses: walk phases 0, 1/8 .. 7/8, then an idle sway in four steps."""
    st = stride_of(design)
    return [walking(i / WALK_FRAMES, stride=st) for i in range(WALK_FRAMES)] + [standing(sway=math.sin(2 * math.pi * i / IDLE_FRAMES) * 0.7) for i in range(IDLE_FRAMES)]


# ------------------------------------------------------------------------------------------------------------------------------------------ Blender helpers
def _clear():
    for ob in list(bpy.data.objects): bpy.data.objects.remove(ob, do_unlink=True)
    for me in list(bpy.data.meshes): bpy.data.meshes.remove(me)


def _object(name, verts, faces):
    me = bpy.data.meshes.new(name); me.from_pydata([tuple(map(float, p)) for p in verts], [], [list(map(int, f)) for f in faces]); me.validate(); me.update()
    ob = bpy.data.objects.new(name, me); bpy.context.scene.collection.objects.link(ob); return ob


def _read(ob, evaluated=True):
    """Vertices (N, 3) and triangles (M, 3) of an object (with its modifiers applied), plus per-loop UVs if it has them."""
    src = ob.evaluated_get(bpy.context.evaluated_depsgraph_get()) if evaluated else ob
    me = src.to_mesh(); me.calc_loop_triangles()
    v = np.zeros(len(me.vertices) * 3); me.vertices.foreach_get("co", v); v = v.reshape(-1, 3)
    t = np.zeros(len(me.loop_triangles) * 3, dtype=np.int64); me.loop_triangles.foreach_get("vertices", t); t = t.reshape(-1, 3)
    uv = None
    if me.uv_layers.active is not None:
        lt = np.zeros(len(me.loop_triangles) * 3, dtype=np.int64); me.loop_triangles.foreach_get("loops", lt)
        lu = np.zeros(len(me.loops) * 2); me.uv_layers.active.data.foreach_get("uv", lu); uv = lu.reshape(-1, 2)[lt].reshape(-1, 3, 2)
    src.to_mesh_clear(); return v, t, uv


def _decimate(verts, faces, ratio, name="dec"):
    ob = _object(name, verts, faces)
    if ratio < 0.999:
        m = ob.modifiers.new("dec", "DECIMATE"); m.decimate_type = "COLLAPSE"; m.ratio = float(ratio); m.use_collapse_triangulate = True
    v, t, _ = _read(ob); bpy.data.objects.remove(ob, do_unlink=True); return v, t


def _unwrap(verts, tris, unwrap_verts):
    """Smart UV projection of the mesh (computed on `unwrap_verts`, the same topology with the head scaled up) packed into 0..1. Returns per-corner UVs (M, 3, 2)."""
    ob = _object("uv", unwrap_verts, tris)
    for o in bpy.data.objects:
        if o is not None and o.name in bpy.context.view_layer.objects: o.select_set(False)
    bpy.context.view_layer.objects.active = ob; ob.select_set(True)
    bpy.ops.object.mode_set(mode="EDIT"); bpy.ops.mesh.select_all(action="SELECT")
    bpy.ops.uv.smart_project(angle_limit=math.radians(62), island_margin=0.006, area_weight=0.0, scale_to_bounds=False)
    bpy.ops.uv.pack_islands(rotate=True, margin=0.008)
    bpy.ops.object.mode_set(mode="OBJECT")
    _, t2, uv = _read(ob, evaluated=False); bpy.data.objects.remove(ob, do_unlink=True)
    assert np.array_equal(np.sort(t2, axis=1), np.sort(np.asarray(tris), axis=1)) or len(t2) == len(tris)
    # loop_triangles may rotate the corners of a triangle: match the corner order to `tris`
    out = np.zeros((len(tris), 3, 2))
    for i, (a, b) in enumerate(zip(np.asarray(tris), t2)):
        for k in range(3): out[i, k] = uv[i, list(b).index(a[k])]
    return out


def _decimate_uv(verts, tris, uv, ratio):
    """Decimates a mesh that already carries UVs (Blender interpolates them and keeps the seams): LOD 1 from LOD 0, sharing its texture."""
    me = bpy.data.meshes.new("lod1"); me.from_pydata([tuple(map(float, p)) for p in verts], [], [list(map(int, f)) for f in tris]); me.update()
    layer = me.uv_layers.new(name="uv"); flat = np.zeros((len(me.loops), 2))
    for p in me.polygons:
        for k, li in enumerate(range(p.loop_start, p.loop_start + p.loop_total)): flat[li] = uv[p.index, k]
    layer.data.foreach_set("uv", flat.ravel()); ob = bpy.data.objects.new("lod1", me); bpy.context.scene.collection.objects.link(ob)
    m = ob.modifiers.new("dec", "DECIMATE"); m.decimate_type = "COLLAPSE"; m.ratio = float(ratio); m.use_collapse_triangulate = True
    v, t, u = _read(ob); bpy.data.objects.remove(ob, do_unlink=True); return v, t, u


# ------------------------------------------------------------------------------------------------------------------------------------------ geometry of a design
def _groups(fig):
    """The parts painted in the skin tile (the body that shows, the eyes) and in the cloth tile (everything else)."""
    return [i for i, p in enumerate(fig.parts) if p["kind"] in ("skin", "eye")], [i for i, p in enumerate(fig.parts) if p["kind"] not in ("skin", "eye")]


def _clean(p):
    """A part's vertices and faces without loose vertices (faces re-indexed); `keep` maps the new vertices to the old."""
    used = np.zeros(len(p["verts"]), dtype=bool); used[[i for f in p["faces"] for i in f]] = True; idx = -np.ones(len(used), dtype=np.int64); keep = np.nonzero(used)[0]; idx[keep] = np.arange(len(keep))
    return p["verts"][keep], [[int(idx[i]) for i in f] for f in p["faces"]], keep


def _head_scale(fig, v):
    """Positions with the head scaled up about its centre (smoothly from the base of the neck), so the face gets more texture."""
    c = np.array([0.0, fig.eye_y, fig.head_c[2]]); y0, y1 = fig.shoulder_y + 0.03 * fig.k, fig.eye_y - 0.10 * fig.k
    t = np.clip((v[:, 1] - y0) / (y1 - y0), 0, 1); s = 1 + (HEAD_UV_SCALE - 1) * t * t * (3 - 2 * t)
    return c + (v - c) * s[:, None]


class Lod:
    """A level of detail of one design: welded positions (rest pose), triangles, per-corner UVs, the part and the source vertex of every vertex, and the group (0 skin, 1 cloth) of every triangle."""
    def __init__(self, v, t, uv, part, group):
        self.v, self.t, self.uv, self.part, self.group = v, np.asarray(t, dtype=np.int64), uv, part, group


def build_lods(fig, ratio_scale=1.0):
    groups = _groups(fig); tris = {i: sum(len(f) - 2 for f in fig.parts[i]["faces"]) for g in groups for i in g}
    small = {i for i, n in tris.items() if n <= SMALL_PART or fig.parts[i]["kind"] == "eye"}       # caps, capes, shoes, basin, bag, eyes: already lean, a cut would break their shape
    kept = sum(tris[i] if fig.parts[i]["kind"] != "eye" else int(tris[i] * 0.35) for i in small)
    r0 = min(1.0, max(0.05, (LOD_TRIS[0] * ratio_scale - kept) / max(1, sum(n for i, n in tris.items() if i not in small)))); lods = []
    V, T, UV, PART, GROUP = [], [], [], [], []
    for gi, g in enumerate(groups):
        gv, gt, gp = [], [], []
        for i in g:
            pv, pf, _ = _clean(fig.parts[i]); dv, dt = _decimate(pv, pf, 0.35 if fig.parts[i]["kind"] == "eye" else (1.0 if i in small else r0))
            gt.append(dt + sum(len(x) for x in gv)); gv.append(dv); gp.append(np.full(len(dv), i))
        if not gv: continue
        gv = np.concatenate(gv); gt = np.concatenate(gt); gp = np.concatenate(gp)
        uv = _unwrap(gv, gt, _head_scale(fig, gv) if gi == 0 else gv)
        T.append(gt + sum(len(x) for x in V)); V.append(gv); UV.append(uv); PART.append(gp); GROUP.append(np.full(len(gt), gi))
    l0 = Lod(np.concatenate(V), np.concatenate(T), np.concatenate(UV), np.concatenate(PART), np.concatenate(GROUP)); lods.append(l0)
    # LOD 1: each group reduced from LOD 0 with its UVs
    V, T, UV, PART, GROUP = [], [], [], [], []
    for gi in (0, 1):
        sel = l0.group == gi
        if not sel.any(): continue
        tt = l0.t[sel]; used = np.unique(tt); idx = -np.ones(len(l0.v), dtype=np.int64); idx[used] = np.arange(len(used))
        dv, dt, du = _decimate_uv(l0.v[used], idx[tt], l0.uv[sel], LOD_TRIS[1] / LOD_TRIS[0])
        kd = KDTree(len(used))
        for j, p in enumerate(l0.v[used]): kd.insert(Vector(p), j)
        kd.balance(); near = np.array([kd.find(Vector(p))[1] for p in dv])
        T.append(dt + sum(len(x) for x in V)); V.append(dv); UV.append(du); PART.append(l0.part[used][near]); GROUP.append(np.full(len(dt), gi))
    lods.append(Lod(np.concatenate(V), np.concatenate(T), np.concatenate(UV), np.concatenate(PART), np.concatenate(GROUP)))
    return lods


def source_vertices(fig, lod):
    """For every LOD vertex: the nearest vertex of the full figure in the same part (its skin weights pose the LOD vertex)."""
    src = np.zeros(len(lod.v), dtype=np.int64)
    for pi in np.unique(lod.part):
        pv = fig.parts[pi]["verts"]; kd = KDTree(len(pv))
        for j, p in enumerate(pv): kd.insert(Vector(p), j)
        kd.balance(); sel = np.nonzero(lod.part == pi)[0]
        for j in sel: src[j] = kd.find(Vector(lod.v[j]))[1]
    return src


def pose_lod(fig, lod, src, pose, rig=None):
    """Posed positions of a LOD's vertices: each follows the skin weights (or the one bone) of its source vertex."""
    rig = rig or Rig(fig.h); tr = rig.transforms(*pose); out = np.zeros_like(lod.v)
    for pi in np.unique(lod.part):
        p = fig.parts[pi]; sel = np.nonzero(lod.part == pi)[0]
        out[sel] = rig.skin(lod.v[sel], ids=(p["ids"][src[sel]] if p["bone"] is None else None), bone=p["bone"], pose=tr)
    return out


def vertex_normals(v, t):
    fn = np.cross(v[t[:, 1]] - v[t[:, 0]], v[t[:, 2]] - v[t[:, 0]]); n = np.zeros_like(v)
    for k in range(3): np.add.at(n, t[:, k], fn)
    l = np.linalg.norm(n, axis=1, keepdims=True); l[l < 1e-12] = 1.0; return n / l


# ------------------------------------------------------------------------------------------------------------------------------------------ the texture
def _rasterize(uv, size):
    """Texel centres covered by each triangle of a UV layout: (triangle index, barycentric weights, texel x, texel y)."""
    T, B, X, Y = [], [], [], []
    for i, tri in enumerate(uv):
        p = tri * size; x0, y0 = np.floor(p.min(0)).astype(int); x1, y1 = np.ceil(p.max(0)).astype(int)
        xs, ys = np.meshgrid(np.arange(max(x0, 0), min(x1 + 1, size)), np.arange(max(y0, 0), min(y1 + 1, size))); xs = xs.ravel(); ys = ys.ravel()
        if len(xs) == 0: continue
        q = np.column_stack([xs + 0.5, ys + 0.5]); a, b, c = p; d = (b[1] - c[1]) * (a[0] - c[0]) + (c[0] - b[0]) * (a[1] - c[1])
        if abs(d) < 1e-12: continue
        w0 = ((b[1] - c[1]) * (q[:, 0] - c[0]) + (c[0] - b[0]) * (q[:, 1] - c[1])) / d; w1 = ((c[1] - a[1]) * (q[:, 0] - c[0]) + (a[0] - c[0]) * (q[:, 1] - c[1])) / d; w2 = 1 - w0 - w1
        e = -0.02; ok = (w0 >= e) & (w1 >= e) & (w2 >= e)
        if not ok.any(): continue
        T.append(np.full(ok.sum(), i)); B.append(np.column_stack([w0, w1, w2])[ok]); X.append(xs[ok]); Y.append(ys[ok])
    return np.concatenate(T), np.concatenate(B), np.concatenate(X), np.concatenate(Y)


def _dilate(img, filled, steps=6):
    """Spreads the painted texels a few texels into the empty space round every island, so filtering and mipmaps do not bleed the background into the seams."""
    img = img.copy(); f = filled.copy()
    for _ in range(steps):
        acc = np.zeros_like(img); cnt = np.zeros(f.shape)
        for dy, dx in ((0, 1), (0, -1), (1, 0), (-1, 0), (1, 1), (-1, -1), (1, -1), (-1, 1)):
            acc += np.roll(img * f[..., None], (dy, dx), (0, 1)); cnt += np.roll(f.astype(float), (dy, dx), (0, 1))
        new = ~f & (cnt > 0); img[new] = acc[new] / cnt[new][:, None]; f = f | new
    if (~f).any(): img[~f] = img[f].mean(0)
    return img


class Painter:
    """Paints the LOD 0 layout of a design: where every texel lies on the full figure (once), then the colours of any skin tone or colourway."""
    def __init__(self, fig, lod, ao):
        self.fig = fig; self.lod = lod; self.tiles = {}
        for gi in (0, 1):
            sel = np.nonzero(lod.group == gi)[0]
            if len(sel) == 0: continue
            ti, bw, xs, ys = _rasterize(lod.uv[sel], TILE); tri = lod.t[sel][ti]
            pts = (lod.v[tri] * bw[:, :, None]).sum(1); part = lod.part[tri[:, 0]]; occ = (ao[tri] * bw).sum(1)
            hits = []                                                    # per texel: (part, hi-res triangle, barycentric) on the full model
            for pi in np.unique(part):
                p = fig.parts[pi]; pv = np.asarray(p["verts"], dtype=np.float64); tris = np.array([[f[0], f[k], f[k + 1]] for f in p["faces"] for k in range(1, len(f) - 1)])
                bvh = BVHTree.FromPolygons([tuple(x) for x in pv], [tuple(x) for x in tris], all_triangles=True); idx = np.nonzero(part == pi)[0]
                for j in idx:
                    loc, _, fi, _ = bvh.find_nearest(Vector(pts[j]))
                    if fi is None: hits.append((j, pi, -1, None, pts[j])); continue
                    a, b, c = pv[tris[fi]]; l = np.array(loc); v0, v1, v2 = b - a, c - a, l - a
                    d00, d01, d11, d20, d21 = v0 @ v0, v0 @ v1, v1 @ v1, v2 @ v0, v2 @ v1; den = d00 * d11 - d01 * d01 or 1e-12
                    w1 = (d11 * d20 - d01 * d21) / den; w2 = (d00 * d21 - d01 * d20) / den
                    hits.append((j, pi, tris[fi], np.array([1 - w1 - w2, w1, w2]), l))
            self.tiles[gi] = (xs, ys, occ, hits)

    def paint(self, gi, figc):
        """The tile of group gi coloured from figure `figc` (the same design built with another skin tone or colourway): (TILE, TILE, 3) sRGB."""
        xs, ys, occ, hits = self.tiles[gi]; col = np.zeros((len(xs), 3)); by_part = {}
        for j, pi, tri, w, loc in hits: by_part.setdefault(pi, []).append((j, tri, w, loc))
        for pi, lst in by_part.items():
            p = figc.parts[pi]; js = np.array([x[0] for x in lst]); locs = np.array([x[3] for x in lst])
            if p.get("pattern") is not None and p.get("colour") is not None:
                col[js] = p["pattern"](locs, np.tile(p["colour"], (len(js), 1)))
            else:
                c = p["colours"]; col[js] = np.array([(c[tri] * w[:, None]).sum(0) if w is not None else c.mean(0) for _, tri, w, _ in lst])
        col *= (1 - AO_STRENGTH * (1 - occ))[:, None]
        img = np.zeros((TILE, TILE, 3)); filled = np.zeros((TILE, TILE), dtype=bool); img[TILE - 1 - ys, xs] = col; filled[TILE - 1 - ys, xs] = True
        return np.clip(_dilate(img, filled), 0, 1)


def occlusion(fig, lod, src):
    """Ambient occlusion per LOD 0 vertex in the standing pose, against the full figure: 1 open, 0 enclosed (under the arms, inside a sleeve, between the legs, under a hijab)."""
    rig = Rig(fig.h); tr = rig.transforms(*standing()); V, F = [], []; off = 0
    for p in fig.parts:
        pv = rig.skin(p["verts"], ids=p["ids"], bone=p["bone"], pose=tr); V.append(pv); F += [[off + f[0], off + f[k], off + f[k + 1]] for f in p["faces"] for k in range(1, len(f) - 1)]; off += len(pv)
    V = np.concatenate(V); bvh = BVHTree.FromPolygons([tuple(x) for x in V], F, all_triangles=True)
    pv = pose_lod(fig, lod, src, standing(), rig); n = vertex_normals(pv, lod.t)
    rng = np.random.default_rng(7); d = rng.normal(size=(AO_RAYS, 3)); d /= np.linalg.norm(d, axis=1, keepdims=True); out = np.ones(len(pv))
    for i, (p, nn) in enumerate(zip(pv, n)):
        o = Vector(p + nn * 0.004); hit = 0.0; cnt = 0
        for r in d:
            if r @ nn < 0: r = -r
            c = r @ nn; cnt += c; h = bvh.ray_cast(o, Vector(r), AO_DIST)
            if h[0] is not None: hit += c * (1 - (h[3] / AO_DIST) ** 2)
        out[i] = 1 - hit / max(cnt, 1e-6)
    return out


# ------------------------------------------------------------------------------------------------------------------------------------------ output
def tile_xy(index):
    n = ATLAS // TILE; return (index % n) * TILE, (index // n) * TILE


def export_design(design, d_index, atlas, log=print):
    make = FIG.DESIGNS[design]; cws = COLOURWAYS[design]
    fig = make(c=cws[0])
    lods = build_lods(fig); src = [source_vertices(fig, l) for l in lods]; ao = occlusion(fig, lods[0], src[0])
    painter = Painter(fig, lods[0], ao); tiles = {}
    for s in range(SKINS):
        figs = make(skin=s, c=cws[0]); i = d_index * 6 + s; x, y = tile_xy(i); atlas[y:y + TILE, x:x + TILE] = painter.paint(0, figs); tiles[("skin", s)] = i
    for c in range(3):
        figc = make(c=cws[c]) if c else fig; i = d_index * 6 + 3 + c; x, y = tile_xy(i)
        if 1 in painter.tiles: atlas[y:y + TILE, x:x + TILE] = painter.paint(1, figc)
        tiles[("cloth", c)] = i
    rig = Rig(fig.h); poses = frames_of(design); out = []
    for li, (l, sv) in enumerate(zip(lods, src)):
        # one game vertex per (vertex, UV, group): the welded vertices are split along the UV seams
        key = {}; vid = np.zeros((len(l.t), 3), dtype=np.int64); welded = []; uvs = []; grp = []
        for ti in range(len(l.t)):
            for k in range(3):
                kk = (int(l.t[ti, k]), int(round(l.uv[ti, k, 0] * 65535)), int(round(l.uv[ti, k, 1] * 65535)), int(l.group[ti]))
                if kk not in key: key[kk] = len(welded); welded.append(kk[0]); uvs.append(kk[1:3]); grp.append(kk[3])
                vid[ti, k] = key[kk]
        welded = np.array(welded); frames = []
        for pose in poses:
            pv = pose_lod(fig, l, sv, pose, rig); nv = vertex_normals(pv, l.t)
            frames.append((pv[welded] * [-1, 1, 1], nv[welded] * [-1, 1, 1]))               # Unity's axes: x mirrored (left-handed)
        out.append(dict(uv=np.clip(np.array(uvs), 0, 65535), group=np.array(grp), tris=vid[:, ::-1], frames=frames))   # mirroring turns the faces: reverse the winding
        log(f"  {design} LOD{li}: {len(l.t)} triangles, {len(welded)} vertices ({len(l.v)} welded)")
    return dict(id=design, height=float(fig.h.stature), child=design in CHILD, tiles=tiles, lods=out)


def write(designs, path):
    b = bytearray(); b += b"APPL"; b += struct.pack("<HHHHHH", VERSION, len(designs), WALK_FRAMES, IDLE_FRAMES, ATLAS, TILE)
    for d in designs:
        name = d["id"].encode(); b += struct.pack("<B", len(name)) + name
        b += struct.pack("<BfBB", 1 if d["child"] else 0, d["height"], SKINS, 3)
        for s in range(SKINS): b += struct.pack("<H", d["tiles"][("skin", s)])
        for c in range(3): b += struct.pack("<H", d["tiles"][("cloth", c)])
        b += struct.pack("<B", len(d["lods"]))
        for l in d["lods"]:
            n = len(l["group"]); b += struct.pack("<II", n, len(l["tris"]) * 3)
            b += l["group"].astype(np.uint8).tobytes(); b += l["uv"].astype(np.uint16).tobytes(); b += l["tris"].astype(np.uint16 if n < 65536 else np.uint32).ravel().tobytes()
            for pos, nrm in l["frames"]:
                b += np.clip(np.round(pos / POS_SCALE), -32767, 32767).astype(np.int16).tobytes(); b += np.clip(np.round(nrm * 127), -127, 127).astype(np.int8).tobytes()
    Path(path).write_bytes(bytes(b)); return len(b)


def main(argv=None) -> int:
    ap = argparse.ArgumentParser(); ap.add_argument("--out", required=True); ap.add_argument("designs", nargs="*"); a = ap.parse_args(argv)
    names = a.designs or list(FIG.DESIGNS); out = Path(a.out); out.mkdir(parents=True, exist_ok=True)
    bpy.ops.wm.read_factory_settings(use_empty=True); atlas = np.zeros((ATLAS, ATLAS, 3)); res = []
    for i, n in enumerate(names):
        print(f"[people] {n}"); res.append(export_design(n, i, atlas)); _clear()
    size = write(res, out / "people.bytes")
    from PIL import Image
    Image.fromarray((atlas * 255 + 0.5).astype(np.uint8)).save(out / "people_atlas.png", optimize=True)
    print(f"[people] {len(res)} designs, people.bytes {size / 1e6:.2f} MB, atlas {ATLAS} px")
    return 0


if __name__ == "__main__":
    sys.exit(main())


def read(path):
    """Reads people.bytes back (the same layout ARO.NetCore.PeopleAsset reads): a list of designs with their LODs, UVs in the atlas and frames in metres."""
    b = Path(path).read_bytes(); o = 4
    assert b[:4] == b"APPL"
    ver, nd, wf, idf, atlas, tile = struct.unpack_from("<HHHHHH", b, o); o += 12; nf = wf + idf; out = []
    for _ in range(nd):
        ln = b[o]; o += 1; name = b[o:o + ln].decode(); o += ln
        child, height, ns, nc = struct.unpack_from("<BfBB", b, o); o += 7
        skin = list(struct.unpack_from(f"<{ns}H", b, o)); o += 2 * ns; cloth = list(struct.unpack_from(f"<{nc}H", b, o)); o += 2 * nc
        nl = b[o]; o += 1; lods = []
        for _ in range(nl):
            n, ni = struct.unpack_from("<II", b, o); o += 8
            grp = np.frombuffer(b, np.uint8, n, o); o += n; uv = np.frombuffer(b, np.uint16, 2 * n, o).reshape(-1, 2) / 65535.0; o += 4 * n
            it = np.uint16 if n < 65536 else np.uint32; tris = np.frombuffer(b, it, ni, o).reshape(-1, 3).astype(np.int64); o += ni * np.dtype(it).itemsize
            frames = []
            for _ in range(nf):
                p = np.frombuffer(b, np.int16, 3 * n, o).reshape(-1, 3) * POS_SCALE; o += 6 * n; q = np.frombuffer(b, np.int8, 3 * n, o).reshape(-1, 3) / 127.0; o += 3 * n; frames.append((p, q))
            lods.append(dict(group=grp, uv=uv, tris=tris, frames=frames))
        out.append(dict(id=name, child=bool(child), height=height, skin_tiles=skin, cloth_tiles=cloth, lods=lods))
    assert o == len(b), "trailing bytes"
    return dict(version=ver, atlas=atlas, tile=tile, walk=wf, idle=idf, designs=out)


def atlas_uv(asset, uv, group, skin_tile, cloth_tile):
    """Atlas UVs (v up, as Unity and Blender sample) of a LOD's vertices for one skin tone and colourway."""
    n = asset["atlas"] // asset["tile"]; t = np.where(group == 0, skin_tile, cloth_tile); tx = (t % n) * asset["tile"]; ty = (t // n) * asset["tile"]
    u = (tx + uv[:, 0] * asset["tile"]) / asset["atlas"]; v = 1.0 - (ty + asset["tile"]) / asset["atlas"] + uv[:, 1] * asset["tile"] / asset["atlas"]
    return np.column_stack([u, v])
