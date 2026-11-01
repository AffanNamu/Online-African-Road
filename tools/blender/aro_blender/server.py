"""ARO Blender MCP server (stdio). Blender runs inside this process as the `bpy` module: no GUI, no add-on, and NO network listener, so there is nothing to expose.
The agent that started this process is the only thing that can talk to it. Every file it reads or writes is inside the workspace (workspace.py); raw Python is checked
(safety.py) and can be switched off with ARO_BLENDER_RAW_PYTHON=0; approving an asset is deliberately not a tool (a person runs `python -m aro_blender.approve`)."""
from __future__ import annotations
import contextlib, io, json, os, signal, sys, time
from pathlib import Path

# Blender's C code prints to file descriptor 1, and its Python add-ons (the glTF exporter logs through sys.stdout) print too, but file descriptor 1 / sys.stdout is what MCP over stdio uses for
# the protocol. Keep the real stdout private for the protocol only and send every other write, C or Python, to stderr.
_proto_fd = os.dup(1); os.dup2(2, 1); _proto = os.fdopen(_proto_fd, "w", buffering=1, encoding="utf-8"); sys.stdout = sys.stderr

import bpy, mathutils  # noqa: E402
from mcp.server.fastmcp import FastMCP, Image  # noqa: E402
from . import __version__, export as exp, materials, render, safety, scene as sc, validate, workspace as ws  # noqa: E402

ROOT = ws.ensure(ws.default_root())
RAW_PYTHON = os.environ.get("ARO_BLENDER_RAW_PYTHON", "1") != "0"
SCRIPT_DIRS = [ROOT / "scripts", Path(__file__).resolve().parent.parent / "scripts"]       # the workspace's scripts and the repo's reviewed scripts
STATE = {"kind": None, "name": None, "version": None}

mcp = FastMCP("aro-blender", instructions=(
    "Headless Blender 5.2 for African Roads Online. Build REAL 3D geometry (never photos on planes). The game's conventions (Assets/_Project/Art/README.md): 1 unit = 1 m, Z up, the asset's FRONT "
    "faces -Y in Blender (Blender's own front-view convention, measured to arrive as +Z in Unity; its right is -X), origin on the ground at the centre of the footprint, transforms applied, a root empty named exactly like the asset id (lower_snake_case), "
    "LOD groups {id}_lod0 ... {id}_lod3 (empties holding that LOD's meshes; last LOD at most 10% of LOD0), materials from the library (list_materials), collision only as simple boxes named "
    "col_* (props/buildings) or one convex col_* mesh (vehicles). Triangle budgets are the game's: buildings 6000, vegetation 3000, signs 800, lights 1200, props 2500. Work in this order: new_scene, create parts, assign_material, render_preview (look at it), refine, validate_asset, save_project, export_asset. "
    "Never describe an asset as finished until validate_asset passes and you have looked at a render. Files only exist in the workspace."))


def _ok(**kw) -> str: return json.dumps({"ok": True, **kw}, indent=1)
def _outdir(sub: str, name: str) -> Path: d = ROOT / sub / name; d.mkdir(parents=True, exist_ok=True); return d


@mcp.tool()
def blender_info() -> str:
    """Blender version, available render engines, workspace root and whether raw Python is enabled."""
    return _ok(blender=bpy.app.version_string, python=sys.version.split()[0], toolchain=__version__, engines=list(render.ENGINES), workspace=str(ROOT), raw_python=RAW_PYTHON,
               current_project=STATE, unit_system=bpy.context.scene.unit_settings.system, unit_scale=bpy.context.scene.unit_settings.scale_length)


@mcp.tool()
def workspace_info() -> str:
    """The workspace layout and what is saved in it (asset kinds, names, versions and which are approved)."""
    assets = []
    for kind in ws.KINDS:
        base = ROOT / "projects" / kind
        for d in sorted(p for p in base.iterdir() if p.is_dir()) if base.is_dir() else []:
            vs = ws.versions(ROOT, kind, d.name); assets.append({"kind": kind, "name": d.name, "versions": [{"v": v, "approved": ws.is_approved(ROOT, kind, d.name, v)} for v in vs]})
    return _ok(root=str(ROOT), folders=ws.TREE, assets=assets, conventions="docs/BLENDER_PIPELINE.md")


@mcp.tool()
def list_materials() -> str:
    """The material library: keys to use with assign_material (base colour, roughness, metallic, what it is for)."""
    return _ok(materials=materials.describe())


@mcp.tool()
def new_scene() -> str:
    """Empties the scene (metric units, 1 unit = 1 m). Unsaved work is discarded."""
    sc.clear(bpy); STATE.update(kind=None, name=None, version=None); return _ok(objects=0)


@mcp.tool()
def inspect_scene(max_objects: int = 200) -> str:
    """Every object with its type, parent, transform, dimensions, triangle count, materials and UV layers, plus totals, bounds and the size of the whole asset in metres."""
    return json.dumps(sc.inspect(bpy, mathutils, max(1, min(max_objects, 1000))), indent=1)


@mcp.tool()
def create_primitive(kind: str, name: str, location: list[float] = [0, 0, 0], dimensions: list[float] | None = None, rotation_deg: list[float] = [0, 0, 0],
                     parent: str | None = None, collection: str | None = None, vertices: int = 24) -> str:
    """Creates a cube, plane, cylinder, cone, sphere, torus, circle or empty (an empty groups parts: use one as the asset root). `dimensions` are metres [x, y, z] and are applied to the mesh
    (the object's scale stays 1). Parts that need real shapes should be built with execute_python (bmesh) or a reviewed script."""
    o = sc.create_primitive(bpy, kind, name, tuple(location), tuple(dimensions) if dimensions else None, tuple(rotation_deg), parent, collection, vertices)
    return _ok(name=o.name, type=o.type, location=[round(v, 4) for v in o.location], dimensions_m=[round(v, 4) for v in o.dimensions])


@mcp.tool()
def transform_object(name: str, location: list[float] | None = None, rotation_deg: list[float] | None = None, scale: list[float] | None = None, apply: bool = False) -> str:
    """Moves, rotates or scales an object. `apply=True` bakes rotation and scale into the mesh (do this before validation: unapplied scale is an error)."""
    o = sc.transform(bpy, name, location, rotation_deg, scale, apply)
    return _ok(name=o.name, location=[round(v, 4) for v in o.location], rotation_deg=[round(v, 3) for v in (x * 57.29578 for x in o.rotation_euler)], scale=[round(v, 4) for v in o.scale])


@mcp.tool()
def delete_objects(names: list[str]) -> str:
    """Deletes objects by name."""
    return _ok(deleted=sc.delete(bpy, names))


@mcp.tool()
def assign_material(object: str, material: str, replace: bool = True) -> str:
    """Assigns a library material (see list_materials) to a mesh object."""
    m = sc.assign_material(bpy, object, material, replace); return _ok(object=object, material=m.name)


@mcp.tool()
def execute_python(code: str, timeout_seconds: int = 60) -> str:
    """Runs Python in the Blender session (bpy, bmesh, mathutils, math are already imported; set a variable `result` to return data). The code is checked first: no os/sys/files/network,
    no eval/exec/open, no Blender file operators (saving, exporting and rendering have their own tools), no handlers or registration. For anything longer or reusable write a script
    under the workspace's scripts folder and call run_script."""
    if not RAW_PYTHON: raise ValueError("raw Python is switched off on this server (ARO_BLENDER_RAW_PYTHON=0)")
    try: safety.check(code)
    except safety.Rejected as e: return json.dumps({"ok": False, "rejected": e.problems}, indent=1)
    import builtins, bmesh, math, random
    allowed = set(safety.ALLOWED_IMPORTS)
    def guarded_import(name, globals=None, locals=None, fromlist=(), level=0):
        if name.split(".")[0] not in allowed: raise ImportError(f"import of '{name}' is not allowed")
        return builtins.__import__(name, globals, locals, fromlist, level)
    safe = {k: getattr(builtins, k) for k in ("abs", "all", "any", "bool", "dict", "enumerate", "filter", "float", "int", "isinstance", "len", "list", "map", "max", "min", "pow", "print",
                                               "range", "reversed", "round", "set", "slice", "sorted", "str", "sum", "tuple", "zip", "True", "False", "None", "ValueError", "TypeError",
                                               "KeyError", "IndexError", "RuntimeError", "Exception", "StopIteration", "divmod", "hash", "repr", "chr", "ord", "frozenset")}
    safe["__import__"] = guarded_import
    ns = {"__builtins__": safe, "bpy": bpy, "bmesh": bmesh, "mathutils": mathutils, "math": math, "random": random, "Vector": mathutils.Vector, "Matrix": mathutils.Matrix,
          "Euler": mathutils.Euler, "get_material": lambda k: materials.get(k, bpy)}
    out = io.StringIO()
    def _timeout(signum, frame): raise TimeoutError(f"the script ran longer than {timeout_seconds} s")
    old = signal.signal(signal.SIGALRM, _timeout); signal.alarm(max(1, min(int(timeout_seconds), 600)))
    try:
        with contextlib.redirect_stdout(out): exec(compile(code, "<execute_python>", "exec"), ns)
    except Exception as e:
        return json.dumps({"ok": False, "error": f"{type(e).__name__}: {e}", "stdout": out.getvalue()[-4000:]}, indent=1)
    finally:
        signal.alarm(0); signal.signal(signal.SIGALRM, old)
    res = ns.get("result")
    try: json.dumps(res)
    except Exception: res = repr(res)
    return json.dumps({"ok": True, "result": res, "stdout": out.getvalue()[-4000:]}, indent=1)


@mcp.tool()
def run_script(script: str, args: dict | None = None) -> str:
    """Runs a REVIEWED script from the workspace's scripts folder or the repository's tools/blender/scripts (path relative to one of them, .py only). The script gets `bpy`, `bmesh`,
    `mathutils`, the `aro` modules (materials, scene, validate) and `args`; set `result` to return data. Use this for the reusable generators (houses, walls, roads, poles)."""
    p = None
    for d in SCRIPT_DIRS:
        c = (d / script).resolve()
        if c.suffix == ".py" and c.is_file() and str(c).startswith(str(d.resolve()) + os.sep): p = c; break
    if p is None: raise ValueError(f"no such script '{script}' in {', '.join(str(d) for d in SCRIPT_DIRS)}")
    import bmesh
    from . import materials as m, scene as s, validate as v
    ns = {"__name__": "__aro_script__", "bpy": bpy, "bmesh": bmesh, "mathutils": mathutils, "args": args or {}, "aro_materials": m, "aro_scene": s, "aro_validate": v, "result": None}
    out = io.StringIO()
    try:
        with contextlib.redirect_stdout(out): exec(compile(p.read_text(), str(p), "exec"), ns)
    except Exception as e:
        return json.dumps({"ok": False, "error": f"{type(e).__name__}: {e}", "stdout": out.getvalue()[-4000:]}, indent=1)
    res = ns.get("result")
    try: json.dumps(res)
    except Exception: res = repr(res)
    return json.dumps({"ok": True, "script": str(p), "result": res, "stdout": out.getvalue()[-4000:]}, indent=1)


@mcp.tool()
def render_preview(view: str = "iso", engine: str = "cycles", width: int = 960, height: int = 540, samples: int = 32, name: str = "scratch", ground: bool = True,
                   target: str | None = None) -> list:
    """Renders the scene from a named view (front, back, left, right, top, iso, iso_back, eye) and returns the image so you can LOOK at it, plus its path. Engines: cycles (CPU, most faithful,
    a few seconds), workbench (instant flat shading, good for blockout proportions), eevee (software GL here: slow). `target` frames one object and its children."""
    ws.check_name("props", name)
    p = ROOT / "renders" / name / f"{name}_{view}_{engine}_{time.strftime('%Y%m%dT%H%M%S', time.gmtime())}.png"; p.parent.mkdir(parents=True, exist_ok=True)
    t = time.time(); out = render.preview(bpy, mathutils, p, view, engine, width, height, samples, ground, target)
    return [Image(path=str(out)), _ok(path=str(out.relative_to(ROOT)), view=view, engine=engine, seconds=round(time.time() - t, 1), bytes=out.stat().st_size)]


@mcp.tool()
def validate_asset(kind: str, asset_id: str | None = None, require_uvs: bool = True, require_collision: bool | None = None) -> str:
    """Measures and checks the current scene as a game asset of this kind (buildings, vegetation, signs, lights, props, environments, roads, vehicles) against the game's own budgets and the
    Art README conventions: triangles per LOD, LOD order, scale, ground pivot, applied transforms, naming, materials, UVs, collision, geometry. With `asset_id` it also checks the root name and
    returns the prop-catalogue entry (measured size, LOD triangles) ready for prop_catalog.json. `passed` is true only with zero errors."""
    return json.dumps(validate.run(bpy, mathutils, kind, require_uvs, require_collision, asset_id), indent=1)


@mcp.tool()
def save_project(kind: str, name: str, notes: str = "") -> str:
    """Saves the scene as a NEW version of an editable Blender project: projects/<kind>/<name>/v001, v002, ... Earlier versions are never overwritten."""
    v, d = ws.new_version_dir(ROOT, kind, name)
    f = d / f"{name}.blend"; bpy.ops.wm.save_as_mainfile(filepath=str(f), compress=True)
    (d / "project.json").write_text(json.dumps({"asset": name, "kind": kind, "version": f"v{v:03d}", "blender": bpy.app.version_string, "notes": notes,
                                                "saved_at": time.strftime("%Y-%m-%dT%H:%M:%SZ", time.gmtime())}, indent=2))
    STATE.update(kind=kind, name=name, version=v)
    return _ok(project=str(f.relative_to(ROOT)), version=f"v{v:03d}", bytes=f.stat().st_size)


@mcp.tool()
def open_project(kind: str, name: str, version: int | None = None) -> str:
    """Opens a saved project version (default: the latest). To change an approved version, open it and save: that makes a new version, the approved one stays untouched."""
    v, d = ws.existing_version_dir(ROOT, kind, name, version)
    bpy.ops.wm.open_mainfile(filepath=str(d / f"{name}.blend")); STATE.update(kind=kind, name=name, version=v)
    return _ok(opened=f"{kind}/{name}/v{v:03d}", approved=ws.is_approved(ROOT, kind, name, v))


@mcp.tool()
def export_asset(kind: str, name: str, formats: list[str] = ["fbx", "glb"], version: int | None = None, require_uvs: bool = True, require_collision: bool | None = None) -> str:
    """Validates the current scene and, only if it passes, exports it for Unity (fbx and/or glb) into exports/<format>/<name>/vNNN with a manifest (measured numbers, file hashes).
    The project must have been saved (save_project) so the export is tied to a source version. Exports never overwrite."""
    v, d = ws.existing_version_dir(ROOT, kind, name, version)
    res = validate.run(bpy, mathutils, kind, require_uvs, require_collision, name)
    if not res["passed"]: return json.dumps({"ok": False, "reason": "validation failed: nothing was exported", "validation": res}, indent=1)
    files = []
    for fmt in formats:
        out = ws.export_dir(ROOT, fmt, name, v); files.append(exp.export(bpy, out, name, fmt))
    man = exp.write_manifest(files[0].parent, name, v, kind, files, res, bpy.app.version_string, str((d / f"{name}.blend").relative_to(ROOT)))
    vd = ROOT / "validation" / name; vd.mkdir(parents=True, exist_ok=True); (vd / f"v{v:03d}.json").write_text(json.dumps(res, indent=2))
    return _ok(version=f"v{v:03d}", files=[str(f.relative_to(ROOT)) for f in files] + [str(man.relative_to(ROOT))], stats=res["stats"], warnings=[x for x in res["findings"] if x["severity"] == "warning"])


def main() -> None:
    sc.clear(bpy)
    print(f"aro-blender {__version__} on Blender {bpy.app.version_string}; workspace {ROOT}; raw python {'on' if RAW_PYTHON else 'off'}; transport stdio (no network listener)", file=sys.stderr, flush=True)
    import anyio
    from mcp.server import stdio as _stdio

    async def serve() -> None:
        sys.stdout = _proto                                    # the MCP transport takes its stdout handle at this moment ...
        async with _stdio.stdio_server() as (reader, writer):
            sys.stdout = sys.stderr                            # ... and from here on nothing but the protocol may use the real one
            await mcp._mcp_server.run(reader, writer, mcp._mcp_server.create_initialization_options())
    anyio.run(serve)


if __name__ == "__main__":
    main()
