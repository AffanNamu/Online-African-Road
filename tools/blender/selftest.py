#!/usr/bin/env python3
"""End-to-end test of the Blender MCP integration over the REAL protocol: this script starts the server as a child process (stdio, no network), connects as an MCP client, and exercises
discovery, scene inspection, object creation, materials, raw-Python guarding, reviewed scripts, rendering (the image comes back through MCP), validation, versioned saving, export and
the refusals (outside paths, overwrites, failing assets). Usage: python tools/blender/selftest.py [--keep]  (needs the packages in requirements.txt)."""
from __future__ import annotations
import asyncio, json, os, shutil, struct, sys, tempfile
from pathlib import Path
from mcp import ClientSession
from mcp.client.stdio import StdioServerParameters, stdio_client

HERE = Path(__file__).resolve().parent
passed = failed = 0; failures: list[str] = []


def check(ok: bool, what: str, detail: str = "") -> None:
    global passed, failed
    if ok: passed += 1; print("PASS ", what)
    else: failed += 1; failures.append(what); print("FAIL ", what, ("  -> " + detail) if detail else "")


async def call(s: ClientSession, _tool: str, **args):
    r = await s.call_tool(_tool, args)
    texts = [c.text for c in r.content if c.type == "text"]; images = [c for c in r.content if c.type == "image"]
    body = None
    for t in texts:
        try: body = json.loads(t); break
        except Exception: continue
    return r, body, images, texts


def listening_sockets_of(marker: str) -> tuple[int, int]:
    """(processes whose command line contains marker, listening TCP/UDP/unix-listen sockets those processes hold) read from /proc. Linux only."""
    inodes = set()
    for f in ("/proc/net/tcp", "/proc/net/tcp6"):
        try: inodes |= {l.split()[9] for l in Path(f).read_text().splitlines()[1:] if l.split()[3] == "0A"}          # state 0A = LISTEN
        except OSError: pass
    for f in ("/proc/net/udp", "/proc/net/udp6"):
        try: inodes |= {l.split()[9] for l in Path(f).read_text().splitlines()[1:]}                                   # a bound UDP socket can receive from anyone who can reach it
        except OSError: pass
    try: inodes |= {l.split()[6] for l in Path("/proc/net/unix").read_text().splitlines()[1:] if len(l.split()) >= 8 and l.split()[5] == "00010000"}   # unix sockets in the listening state
    except OSError: pass
    procs = held = 0
    for d in Path("/proc").iterdir():
        if not d.name.isdigit(): continue
        try: cmd = (d / "cmdline").read_bytes().replace(b"\0", b" ").decode(errors="replace")
        except OSError: continue
        if marker not in cmd or "selftest" in cmd: continue
        procs += 1
        try:
            for fd in (d / "fd").iterdir():
                t = os.readlink(fd)
                if t.startswith("socket:[") and t[8:-1] in inodes: held += 1
        except OSError: pass
    return procs, held


async def main(keep: bool) -> int:
    ws = Path(tempfile.mkdtemp(prefix="aro-selftest-"))
    env = {**os.environ, "PYTHONPATH": str(HERE), "ARO_WORKSPACE": str(ws), "PYTHONUNBUFFERED": "1"}
    params = StdioServerParameters(command=sys.executable, args=["-m", "aro_blender.server"], env=env)
    async with stdio_client(params) as (rd, wr):
        async with ClientSession(rd, wr) as s:
            init = await s.initialize()
            check(init.serverInfo.name == "aro-blender", "the server answers the MCP handshake", str(init.serverInfo))
            tools = {t.name for t in (await s.list_tools()).tools}
            need = {"blender_info", "workspace_info", "list_materials", "new_scene", "inspect_scene", "create_primitive", "transform_object", "delete_objects", "assign_material", "execute_python",
                    "run_script", "render_preview", "validate_asset", "save_project", "open_project", "export_asset"}
            check(need <= tools, "the agent can discover every Blender tool", f"missing: {need - tools}")
            check("approve_version" not in tools and not any("approve" in t for t in tools), "approving an asset is not an agent tool")

            procs, held = listening_sockets_of("aro_blender.server")
            check(procs >= 1 and held == 0, "the running server holds no listening network socket (stdio only)", f"processes={procs} listening sockets={held}")
            _, info, _, _ = await call(s, "blender_info")
            check(info and info["blender"].startswith("5.2"), "Blender 5.2 is running inside the server", str(info and info.get("blender")))
            check(info and info["unit_system"] == "METRIC" and info["unit_scale"] == 1.0, "the scene is metric, 1 unit = 1 m")
            _, w, _, _ = await call(s, "workspace_info"); check(w and w["root"] == str(ws) and "exports/fbx" in w["folders"], "the workspace layout exists")
            _, mats, _, _ = await call(s, "list_materials"); check(mats and any(m["key"] == "laterite" for m in mats["materials"]), "the material library is listed")

            # build with the reviewed script, inspect it
            await call(s, "new_scene")
            _, res, _, _ = await call(s, "run_script", script="prop_bollard.py", args={"height": 0.9})
            check(res and res["ok"] and res["result"]["parts"] == ["bollard_body", "bollard_band", "bollard_low"], "a reviewed script builds objects", json.dumps(res)[:200])
            _, ins, _, _ = await call(s, "inspect_scene")
            check(ins and ins["object_count"] == 6 and ins["triangles_total"] > 0, "the scene can be inspected with real counts", str(ins and (ins["object_count"], ins["triangles_total"])))
            body = next((o for o in ins["objects"] if o["name"] == "bollard_body"), None)
            check(body and body["materials"] == ["M_concrete"] and body["uv_layers"], "materials and UV layers are reported")
            check(ins["size_m"] and abs(ins["size_m"][2] - 0.9) < 0.01, "the real size is measured (0.9 m tall)", str(ins["size_m"]))

            # objects by tool
            _, c, _, _ = await call(s, "create_primitive", kind="cube", name="Test_Block", location=[2, 0, 0.25], dimensions=[0.5, 0.5, 0.5])
            check(c and c["ok"] and c["dimensions_m"] == [0.5, 0.5, 0.5], "an object is created with real dimensions")
            _, a, _, _ = await call(s, "assign_material", object="Test_Block", material="laterite"); check(a and a["material"] == "M_laterite", "a library material is assigned")
            r, _, _, texts = await call(s, "create_primitive", kind="cube", name="Test_Block"); check(r.isError or "already exists" in " ".join(texts), "a duplicate name is refused")
            _, d, _, _ = await call(s, "delete_objects", names=["Test_Block"]); check(d and d["deleted"] == ["Test_Block"], "an object is deleted")

            # raw Python: accepted when benign, refused when hostile
            _, py, _, _ = await call(s, "execute_python", code="import bmesh\nbm = bmesh.new()\nbmesh.ops.create_cube(bm, size=1.0)\nme = bpy.data.meshes.new('Py_Mesh'); bm.to_mesh(me); bm.free()\nresult = len(me.vertices)")
            check(py and py["ok"] and py["result"] == 8, "benign raw Python runs and returns a result", json.dumps(py)[:200])
            for label, code in (("os import", "import os\nresult = os.listdir('/')"), ("open()", "result = open('/etc/passwd').read()"), ("file operator", "bpy.ops.wm.save_as_mainfile(filepath='/tmp/x.blend')"),
                                ("eval", "result = eval('1+1')")):
                _, h, _, _ = await call(s, "execute_python", code=code); check(h and h["ok"] is False and "rejected" in h, f"hostile raw Python is refused ({label})", json.dumps(h)[:160])
            _, loop, _, _ = await call(s, "execute_python", code="while True:\n    pass", timeout_seconds=1); check(loop and loop["ok"] is False and "ran longer" in json.dumps(loop), "a runaway script is stopped by the timeout")
            r, _, _, texts = await call(s, "run_script", script="../../../etc/passwd"); check(r.isError, "a script path outside the scripts folders is refused")

            # render: the image comes back through MCP (Cycles always; Workbench only where OpenGL exists, otherwise it must be refused cleanly)
            from aro_blender import render as _render
            engines = (("workbench", 480, 270, 1), ("cycles", 480, 270, 16)) if _render.gl_available() else (("cycles", 480, 270, 16),)
            if len(engines) == 1:
                r, _, _, texts = await call(s, "render_preview", view="iso", engine="workbench", width=480, height=270, samples=1, name="bollard")
                check(r.isError and "needs OpenGL" in " ".join(texts), "without OpenGL the Workbench engine is refused cleanly (the server stays up)")
            for eng, w_, h_, smp in engines:
                r, meta, images, _ = await call(s, "render_preview", view="iso", engine=eng, width=w_, height=h_, samples=smp, name="bollard")
                png = ws / meta["path"] if meta else None
                check(meta and meta["ok"] and png.is_file() and png.read_bytes()[:8] == b"\x89PNG\r\n\x1a\n", f"{eng} renders a PNG file", str(meta))
                check(len(images) == 1 and len(images[0].data) > 1000, f"{eng} preview is returned to the agent as an image")
            _, ins2, _, _ = await call(s, "inspect_scene"); check(not any(o["name"].startswith("__aro_") for o in ins2["objects"]), "preview leaves no temporary objects in the scene")

            # validation: a failing asset is refused, a good one passes
            await call(s, "create_primitive", kind="cube", name="Bad_Copy.001", location=[0, 3, 5], dimensions=[1, 1, 1])
            _, bad, _, _ = await call(s, "validate_asset", kind="props", asset_id="bollard")
            rules = {f["rule"] for f in bad["findings"] if f["severity"] == "error"}
            check(bad["passed"] is False and {"naming", "materials"} <= rules, "validation fails an unnamed (.001), material-less copy", str(rules))
            await call(s, "delete_objects", names=["Bad_Copy.001", "Py_Mesh"])
            await call(s, "transform_object", name="bollard", location=[0, 0, 0.5])
            _, lifted, _, _ = await call(s, "validate_asset", kind="props", asset_id="bollard")
            check(lifted["passed"] is False and any(f["rule"] == "pivot" for f in lifted["findings"]), "validation fails an asset whose ground is not z = 0")
            await call(s, "transform_object", name="bollard", location=[0, 0, 0])
            _, good, _, _ = await call(s, "validate_asset", kind="props", asset_id="bollard")
            check(good["passed"] is True and good["stats"]["triangles_lod0"] > 0, "the clean asset passes validation", json.dumps(good["findings"])[:300])
            check(good["stats"]["lod_triangles"][0] > good["stats"]["lod_triangles"][1] == 12, "the LODs are measured per group and strictly decrease", str(good["stats"]["lod_triangles"]))
            ce = good["catalog_entry"]
            check(ce and ce["id"] == "bollard" and ce["category"] == "props" and abs(ce["heightM"] - 0.9) < 0.01 and ce["lodTriangles"] == good["stats"]["lod_triangles"] and ce["source"] == "Props/bollard", "a prop-catalogue entry is produced from the measurements", str(ce))
            # the materials carry their colour where the exporters read it, as linear values converted from the sRGB the library is written in
            _, col, _, _ = await call(s, "execute_python", code="n = bpy.data.materials['M_concrete'].node_tree.nodes['Principled BSDF']\nresult = [list(n.inputs['Base Color'].default_value)[:3], list(bpy.data.materials['M_concrete']['aro_srgb'])]")
            lin, srgb = col["result"]
            check(col["ok"] and all(abs(l - ((c / 12.92) if c <= 0.04045 else ((c + 0.055) / 1.055) ** 2.4)) < 1e-4 for l, c in zip(lin, srgb)) and all(abs(l - c) > 0.01 for l, c in zip(lin, srgb)), "a library material's base colour is the sRGB colour converted to linear, and set even where a noise break-up is wired in", str(col))
            # LOD rules of the game: a LOD1 that is not under 10% of LOD0, or not lower at all, fails
            await call(s, "execute_python", code="o = bpy.data.objects['bollard_low']; mod = o.modifiers.new('s', 'SUBSURF'); mod.levels = 2; mod.subdivision_type = 'SIMPLE'")
            _, lodbad, _, _ = await call(s, "validate_asset", kind="props", asset_id="bollard")
            check(lodbad["passed"] is False and any(f_["rule"] == "lods" for f_ in lodbad["findings"]), "a LOD1 that is not under 10% of LOD0 fails")
            await call(s, "execute_python", code="bpy.data.objects['bollard_low'].modifiers.clear()")
            await call(s, "create_primitive", kind="cube", name="wrongroot", location=[0, 0, 0.1], dimensions=[0.1, 0.1, 0.2])
            await call(s, "delete_objects", names=["wrongroot"])
            _, badid, _, _ = await call(s, "validate_asset", kind="props", asset_id="not_the_root")
            check(badid["passed"] is False and any("root empty must be named" in f_["message"] for f_ in badid["findings"]), "the root empty must carry the asset id")
            _, hv, _, _ = await call(s, "validate_asset", kind="buildings", asset_id="bollard")
            check(hv["passed"] is False and any(f_["rule"] == "scale" for f_ in hv["findings"]), "the same object is wrong as a building (size range)")

            await call(s, "new_scene")
            _, cal, _, _ = await call(s, "run_script", script="calibration_marker.py")
            _, calv, _, _ = await call(s, "validate_asset", kind="props", asset_id="aro_calibration")
            check(cal["ok"] and calv["passed"] and calv["stats"]["lod_triangles"] == [252, 12] and calv["catalog_entry"]["collider"] == "box", "the calibration fixture builds and passes validation (the Unity import test imports it)", json.dumps(calv["findings"])[:300])
            await call(s, "new_scene"); await call(s, "run_script", script="prop_bollard.py")
            # versioned save, export, refusals
            _, s1, _, _ = await call(s, "save_project", kind="props", name="bollard", notes="selftest"); check(s1 and s1["version"] == "v001", "the project is saved as v001")
            _, s2, _, _ = await call(s, "save_project", kind="props", name="bollard", notes="again"); check(s2 and s2["version"] == "v002", "saving again makes v002 and keeps v001")
            check((ws / "projects/props/bollard/v001/bollard.blend").is_file() and (ws / "projects/props/bollard/v002/bollard.blend").is_file(), "both Blender source files exist")
            _, ex, _, _ = await call(s, "export_asset", kind="props", name="bollard", formats=["fbx", "glb"], version=2)
            check(ex and ex["ok"] and len(ex["files"]) == 3, "a validated asset exports (fbx, glb, manifest)", json.dumps(ex)[:300])
            fbx = ws / "exports/fbx/bollard/v002/bollard.fbx"; glb = ws / "exports/glb/bollard/v002/bollard.glb"
            check(fbx.is_file() and fbx.read_bytes()[:20] == b"Kaydara FBX Binary  ", "the FBX is a real binary FBX")
            check(glb.is_file() and glb.read_bytes()[:4] == b"glTF" and struct.unpack("<I", glb.read_bytes()[8:12])[0] == glb.stat().st_size, "the GLB is a real, complete glTF binary")
            from aro_blender import fbxinfo
            fi = fbxinfo.summary(fbx)
            check(fi["UpAxis"] == 1 and fi["UnitScaleFactor"] == 100.0 and {"bollard", "bollard_lod0", "bollard_body"} <= set(fi["models"]), "the FBX itself declares Y up, metre units and keeps the hierarchy", json.dumps({k: fi[k] for k in ("UpAxis", "UnitScaleFactor")}))
            ext = fi["geometry_extents"]["bollard_body"]; hgt = max(h - l for h, l in zip(ext["max"], ext["min"]))
            check(0.5 < hgt < 1.0, "the FBX geometry is in metres (the 0.72 m body measures between 0.5 and 1.0 in the file)", str(ext))
            man = json.loads((ws / "exports/fbx/bollard/v002/bollard.manifest.json").read_text())
            check(man["validation"]["passed"] and man["files"][0]["sha256"] and man["blender"].startswith("5.2"), "the manifest records the measurements and file hashes")
            r, _, _, texts = await call(s, "export_asset", kind="props", name="bollard", formats=["fbx"], version=2); check(r.isError and "already holds an export" in " ".join(texts), "exports never overwrite")
            await call(s, "create_primitive", kind="cube", name="Floating_Block", location=[0, 0, 5], dimensions=[1, 1, 1])
            _, no, _, _ = await call(s, "export_asset", kind="props", name="bollard", formats=["glb"], version=1)
            check(no and no["ok"] is False and "nothing was exported" in no["reason"] and not (ws / "exports/glb/bollard/v001").exists(), "an asset that fails validation is not exported")
            # reopen
            _, op, _, _ = await call(s, "open_project", kind="props", name="bollard", version=1); check(op and op["opened"] == "props/bollard/v001", "a saved project reopens")
            _, ins3, _, _ = await call(s, "inspect_scene"); check(ins3["object_count"] == 6, "the reopened scene is the saved one")
            r, _, _, texts = await call(s, "save_project", kind="props", name="Bad Name"); check(r.isError, "a bad asset name is refused")
            r, _, _, texts = await call(s, "open_project", kind="props", name="bollard", version=9); check(r.isError, "an unknown version is refused")

    if not keep: shutil.rmtree(ws, ignore_errors=True)
    else: print("workspace kept at", ws)
    print(f"\n{passed} passed, {failed} failed")
    if failed: print("BLENDER MCP SELFTEST FAILED:\n - " + "\n - ".join(failures)); return 1
    print("BLENDER MCP SELFTEST PASSED"); return 0


if __name__ == "__main__":
    sys.exit(asyncio.run(main("--keep" in sys.argv)))
