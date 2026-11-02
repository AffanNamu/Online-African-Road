"""Finds faces of one mesh that lie in the SAME plane with the SAME orientation and overlap (the signature of two boxes that share a wall plane over an overlapping area, e.g. two wall plinths meeting at a
corner). Such faces fight for the same pixels: Cycles renders them black, a game renders them as flickering z-fighting. Faces with opposite orientation (the two sides of a thin slab) or that only touch
along an edge are not reported. The check is exact for axis-aligned quads and boxes (everything the kits build their walls from) and uses each face's bounding rectangle in its plane for other faces,
so a reported pair of non-rectangular faces is a candidate to look at, not proof.
Run from tools/blender:  PYTHONPATH=. ~/.aro/venv/bin/python -m kano_kit.coplanar [--kit kano|env|all]"""
from __future__ import annotations
import sys
from collections import defaultdict

EPS = 1e-4


def overlaps(part) -> list[tuple]:
    """Pairs of same-plane, same-orientation faces whose in-plane bounding rectangles overlap with positive area. Returns [(material key a, material key b, plane, overlap area)]."""
    bm = part.bm; bm.normal_update(); groups = defaultdict(list)
    for f in bm.faces:
        n = f.normal
        if n.length < 0.5: continue
        c = f.calc_center_median()
        if n.z < -0.99 and c.z < 0.005: continue                                                 # undersides on the ground are never seen
        d = n.dot(c)
        groups[(round(n.x, 3), round(n.y, 3), round(n.z, 3), round(d, 3))].append(f)
    out = []
    for key, fs in groups.items():
        if len(fs) < 2: continue
        nx, ny, nz = key[:3]; ax = max(range(3), key=lambda i: abs((nx, ny, nz)[i])); u, v = [i for i in range(3) if i != ax]
        rects = []
        for f in fs:
            us = [vv.co[u] for vv in f.verts]; vs = [vv.co[v] for vv in f.verts]; rects.append((min(us), max(us), min(vs), max(vs), f))
        rects.sort(key=lambda r: r[0])
        for i in range(len(rects)):
            for j in range(i + 1, len(rects)):
                if rects[j][0] >= rects[i][1] - EPS: break
                a, b = rects[i], rects[j]; du = min(a[1], b[1]) - max(a[0], b[0]); dv = min(a[3], b[3]) - max(a[2], b[2])
                if du > EPS and dv > EPS: out.append((part.keys[a[4].material_index], part.keys[b[4].material_index], key, round(du * dv, 4)))
    return out


def degenerate_faces(part, min_area: float = 1e-7) -> list[int]:
    """Indices of faces with (almost) zero area: invisible, they cost triangles and can break normals and exporters."""
    return [f.index for f in part.bm.faces if f.calc_area() < min_area]


def duplicate_faces(part) -> int:
    """The number of faces that have exactly the same corner positions (rounded to 0.1 mm) as another face, whatever their order: the same surface built twice."""
    seen: dict = {}; dup = 0
    for f in part.bm.faces:
        k = tuple(sorted(tuple(round(c, 4) for c in v.co) for v in f.verts))
        if k in seen: dup += 1
        seen[k] = True
    return dup


def separate(part, eps: float = 0.003) -> int:
    """Resolves overlapping coplanar faces in place: of two overlapping same-plane faces the one created LATER (the detail, as the builders add details after the body) is moved `eps` metres along its normal, a third
    one 2 * eps, and so on. The moved face is a few millimetres proud of the one beneath it, so the two no longer fight for the same pixels; 3 mm is more than the depth buffer can confuse out to about
    100 m (24-bit depth, 0.3 m near plane), beyond which the LODs apply (which are separated too). Faces keep their own vertices (the kits never weld them), so moving one leaves a hairline gap at its rim,
    invisible except at grazing angles. Opt-in: nothing calls it unless a builder asks, so the output of existing builders is unchanged. Returns the number of faces moved."""
    bm = part.bm; bm.normal_update(); groups = defaultdict(list)
    for f in bm.faces:
        n = f.normal
        if n.length < 0.5: continue
        c = f.calc_center_median()
        if n.z < -0.99 and c.z < 0.005: continue
        groups[(round(n.x, 3), round(n.y, 3), round(n.z, 3), round(n.dot(c), 3))].append(f)
    moved = 0
    for key, fs in groups.items():
        if len(fs) < 2: continue
        ax = max(range(3), key=lambda i: abs(key[i])); u, v = [i for i in range(3) if i != ax]; fs.sort(key=lambda f: f.index); rects = []; levels = []
        for f in fs:
            us = [vv.co[u] for vv in f.verts]; vs = [vv.co[v] for vv in f.verts]; r = (min(us), max(us), min(vs), max(vs)); lvl = 0
            for r2, l2 in zip(rects, levels):
                if min(r[1], r2[1]) - max(r[0], r2[0]) > EPS and min(r[3], r2[3]) - max(r[2], r2[2]) > EPS: lvl = max(lvl, l2 + 1)
            rects.append(r); levels.append(lvl)
        for f, lvl in zip(fs, levels):
            if lvl:
                for vv in f.verts: vv.co += f.normal * (eps * lvl)
                moved += 1
    bm.normal_update(); return moved


def main() -> int:
    import bpy  # noqa: F401
    from argparse import ArgumentParser
    ap = ArgumentParser(); ap.add_argument("--kit", default="all", choices=["kano", "env", "all"]); ap.add_argument("--min-area", type=float, default=0.002); a = ap.parse_args()
    from . import build_all
    specs = {}
    if a.kit in ("kano", "all"): specs.update({k: v for k, v in build_all.SPECS.items()})
    if a.kit in ("env", "all"):
        from env_kit import build_env; specs.update(build_env.SPECS)
    bad = 0
    for aid, sp in specs.items():
        part = sp["lods"][0](); hits = [h for h in overlaps(part) if h[3] >= a.min_area]
        print(f"{'OVERLAP' if hits else 'ok     '} {aid}: {len(hits)} overlapping coplanar face pairs" + (f" (largest {max(h[3] for h in hits)} m2; materials {sorted({(h[0], h[1]) for h in hits})[:3]})" if hits else ""))
        bad += bool(hits)
    print(f"{bad} of {len(specs)} assets have overlapping coplanar faces"); return 0


if __name__ == "__main__":
    sys.exit(main())
