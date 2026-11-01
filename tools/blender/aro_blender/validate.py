"""Asset validation: the checks an asset must pass before it is exported or approved. Every number it reports is measured from the scene, not claimed.
The budgets are the GAME'S OWN where the game has them: PropCatalogValidator.Lod0Budget (buildings 6000, vegetation 3000, signs 800, lights 1200, props and environment 2500 LOD0 triangles;
LOD triangle counts strictly decreasing, 1 to 4 LODs; at most 80 m on a side) and the conventions in Assets/_Project/Art/README.md (LOD groups {id}_lod0..3, the last LOD at most 10% of
LOD0, collision = none or simple boxes for props, one convex mesh for vehicles, never the render mesh). Vehicles use VehicleAssetValidator's hero budget. Roads have no game budget yet:
that figure is a first-pass estimate to be re-set from measured Unity frame time and memory. A changed budget is a reviewed change, never an edit to make an asset pass."""
from __future__ import annotations
import re
from . import scene as sc

# kind -> (LOD0 triangle budget, plausible size range in metres for the longest side, collision meshes required?, prop-catalogue category or None)
RULES = {
    "buildings":    (6000, (2.5, 80.0), False, "buildings"),
    "vegetation":   (3000, (0.3, 40.0), False, "vegetation"),
    "signs":        (800, (0.3, 12.0), False, "signs"),
    "lights":       (1200, (1.0, 20.0), False, "lights"),
    "props":        (2500, (0.1, 20.0), False, "props"),
    "environments": (2500, (3.0, 80.0), False, "environment"),
    "roads":        (8000, (2.0, 200.0), False, None),
    "vehicles":     (60000, (1.5, 25.0), True, None),
}
BUDGET = {k: v[0] for k, v in RULES.items()}
COL_MAX_TRIS = 255
BOX_TRIS = 12
NAME_OK = re.compile(r"^[A-Za-z0-9]+(?:_[A-Za-z0-9]+)*$")
DUP_SUFFIX = re.compile(r"\.\d{3}$")
LOD_RX = re.compile(r"^(.+)_lod([0-3])$")
COL_RX = re.compile(r"^col_", re.I)


def _f(sev, rule, msg): return {"severity": sev, "rule": rule, "message": msg}


def _lod_level(o):
    """The LOD an object belongs to: its own or an ancestor's name {id}_lodN; objects outside any LOD group are LOD0."""
    n = o
    while n is not None:
        m = LOD_RX.match(n.name)
        if m: return int(m.group(2))
        n = n.parent
    return 0


def run(bpy, mathutils, kind: str, require_uvs: bool = True, require_collision: bool | None = None, asset_id: str | None = None) -> dict:
    if kind not in RULES: raise ValueError(f"unknown asset kind '{kind}': {', '.join(RULES)}")
    budget, (smin, smax), need_col_default, category = RULES[kind]
    scene = bpy.context.scene; f = []
    objs = [o for o in scene.objects if not o.name.startswith("__aro_")]
    meshes = [o for o in objs if o.type == "MESH"]
    cols = [o for o in meshes if COL_RX.match(o.name)]
    visual = [o for o in meshes if not COL_RX.match(o.name)]
    if not visual: return {"passed": False, "findings": [_f("error", "empty", "no visible mesh to validate")], "stats": {}}
    by_lod: dict[int, list] = {}
    for o in visual: by_lod.setdefault(_lod_level(o), []).append(o)
    lod_tris = {lv: sum(sc.mesh_stats(o, bpy)[1] for o in ms) for lv, ms in by_lod.items()}
    lod0 = by_lod.get(0, [])
    if not lod0: return {"passed": False, "findings": [_f("error", "lods", "there is no LOD0 mesh")], "stats": {}}

    tris = lod_tris[0]; verts = sum(sc.mesh_stats(o, bpy)[0] for o in lod0); lo, hi = [1e9] * 3, [-1e9] * 3
    for o in lod0:
        a, b = sc._world_bbox(o, mathutils); lo = [min(x, y) for x, y in zip(lo, a)]; hi = [max(x, y) for x, y in zip(hi, b)]
    size = [h - l for h, l in zip(hi, lo)]
    if tris > budget: f.append(_f("error", "triangles", f"LOD0 has {tris} triangles; the {kind} budget is {budget}"))
    levels = sorted(lod_tris)
    if levels != list(range(len(levels))): f.append(_f("error", "lods", f"LOD levels must be contiguous from lod0 (found {', '.join(f'lod{x}' for x in levels)})"))
    if len(levels) > 4: f.append(_f("error", "lods", "at most 4 LOD levels"))
    seq = [lod_tris[x] for x in levels]
    if any(b >= a for a, b in zip(seq, seq[1:])): f.append(_f("error", "lods", f"LOD triangle counts must strictly decrease (got {seq})"))
    if len(seq) > 1 and seq[-1] > 0.10 * seq[0]: f.append(_f("error", "lods", f"the last LOD has {seq[-1]} triangles: at most 10% of LOD0 ({0.10 * seq[0]:.0f})"))
    if len(seq) == 1 and kind in ("buildings", "vegetation", "vehicles"): f.append(_f("warning", "lods", f"{kind} are seen from far away: add lower LODs ({{id}}_lod1 ...)"))
    longest = max(size)
    if not (smin <= longest <= smax): f.append(_f("error", "scale", f"longest side {longest:.2f} m is outside {smin} to {smax} m for {kind} (is 1 unit one metre?)"))
    if abs(lo[2]) > 0.02: f.append(_f("error", "pivot", f"the lowest point is at z = {lo[2]:.3f} m; the ground must be z = 0 (+/- 0.02)"))
    cx, cy = (lo[0] + hi[0]) / 2, (lo[1] + hi[1]) / 2
    if kind in ("props", "signs", "lights", "vegetation") and (abs(cx) > 0.05 * max(size[0], 0.1) + 0.05 or abs(cy) > 0.05 * max(size[1], 0.1) + 0.05):
        f.append(_f("warning", "pivot", f"the origin is not at the centre of the footprint (centre at x={cx:.2f}, y={cy:.2f})"))

    for o in meshes:
        if any(abs(s_ - 1) > 1e-4 for s_ in o.scale): f.append(_f("error", "transform", f"{o.name}: scale {tuple(round(s_, 3) for s_ in o.scale)} is not applied"))
    roots = [o for o in objs if o.parent is None]
    empties = [o for o in roots if o.type == "EMPTY"]
    if not empties: f.append(_f("warning", "hierarchy", "no root empty: group the asset under one empty named after its id"))
    if len(empties) > 1: f.append(_f("warning", "hierarchy", "more than one root empty"))
    if asset_id and empties and not any(o.name == asset_id for o in empties): f.append(_f("error", "naming", f"the root empty must be named after the asset id '{asset_id}' (found {', '.join(o.name for o in empties)})"))
    if [o for o in roots if o.type == "MESH"]: f.append(_f("warning", "hierarchy", "meshes at the top level: parent them under the root empty"))
    for o in objs:
        if DUP_SUFFIX.search(o.name): f.append(_f("error", "naming", f"{o.name}: a duplicate suffix (.001) means a copy was never named"))
        elif not NAME_OK.match(o.name): f.append(_f("warning", "naming", f"{o.name}: use letters, digits and single underscores"))

    mats = set(); bad_img = []
    for o in visual:
        slots = [s_.material for s_ in o.material_slots]
        if not slots or any(m is None for m in slots): f.append(_f("error", "materials", f"{o.name}: has an empty or missing material slot"))
        for m in slots:
            if m is None: continue
            mats.add(m.name)
            if not m.name.startswith("M_"): f.append(_f("warning", "naming", f"material {m.name}: name materials M_<name>"))
            if m.use_nodes:
                for n in m.node_tree.nodes:
                    if n.type == "TEX_IMAGE" and n.image:
                        if max(n.image.size) > 2048: bad_img.append(f"{n.image.name} {tuple(n.image.size)}")
                        if not n.image.name.startswith("T_"): f.append(_f("warning", "naming", f"texture {n.image.name}: name textures T_<id>_<BaseColor|Normal|ORM|Emissive>"))
        if require_uvs and not o.data.uv_layers: f.append(_f("error", "uv", f"{o.name}: no UV map"))
    if len(mats) > 16: f.append(_f("warning", "materials", f"{len(mats)} materials: each is a draw call; share materials"))
    for b in bad_img: f.append(_f("error", "textures", f"texture larger than 2048 px: {b}"))

    need_col = need_col_default if require_collision is None else require_collision
    if need_col and not cols: f.append(_f("error", "collision", f"{kind} need collision: add one convex mesh named col_<id>"))
    for o in cols:
        v, t = sc.mesh_stats(o, bpy)
        if kind == "vehicles":
            if t > COL_MAX_TRIS: f.append(_f("error", "collision", f"{o.name}: {t} triangles; keep a collision hull at or under {COL_MAX_TRIS}"))
        elif t > BOX_TRIS or v > 8: f.append(_f("error", "collision", f"{o.name}: props and buildings use simple boxes only (12 triangles), never a mesh; this has {t}"))

    import bmesh
    zero_area = loose = 0
    for o in visual:
        bm = bmesh.new(); bm.from_mesh(o.data)
        zero_area += sum(1 for p in bm.faces if p.calc_area() < 1e-10); loose += sum(1 for v in bm.verts if not v.link_edges)
        bm.free()
    if zero_area: f.append(_f("error", "geometry", f"{zero_area} zero-area faces"))
    if loose: f.append(_f("warning", "geometry", f"{loose} loose vertices"))

    # what the game's prop catalogue needs, from the measurements (Blender X = width, Y = depth/front, Z = height)
    entry = None
    if category and asset_id:
        entry = {"id": asset_id, "category": category, "status": "production", "description": f"Blender-authored, source project projects/{kind}/{asset_id}", "source": f"Props/{asset_id}",
                 "collider": "box" if kind in ("buildings", "props", "signs", "lights", "environments") else "none", "heightM": round(size[2], 2), "widthM": round(size[0], 2), "depthM": round(size[1], 2),
                 "cullDistance": 350 if kind == "vegetation" else 250 if kind == "buildings" else 140, "lodTriangles": seq, "castShadow": kind != "signs", "tags": []}
    stats = {"triangles_lod0": tris, "lod_triangles": seq, "vertices_lod0": verts, "size_m": [round(s_, 3) for s_ in size], "bounds_min_m": [round(v, 3) for v in lo], "bounds_max_m": [round(v, 3) for v in hi],
             "visual_meshes": len(visual), "collision_meshes": len(cols), "materials": sorted(mats), "budget_triangles": budget}
    return {"passed": not any(x["severity"] == "error" for x in f), "kind": kind, "findings": f, "stats": stats, "catalog_entry": entry}
