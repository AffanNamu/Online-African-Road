"""A small reader for BINARY FBX files, to read back what an export actually declares (axes, unit scale, per-model transforms, vertex extents) from the file itself, independently of
Blender. Used as evidence next to the Unity import test: Unity reads exactly these values. Format: https://code.blender.org/2013/08/fbx-binary-file-format-specification/ (versions 7.1 to 7.7)."""
from __future__ import annotations
import struct, zlib
from pathlib import Path

MAGIC = b"Kaydara FBX Binary  \x00\x1a\x00"


def _prop(b: bytes, o: int):
    t = chr(b[o]); o += 1
    if t == "Y": return struct.unpack_from("<h", b, o)[0], o + 2
    if t == "C": return bool(b[o]), o + 1
    if t == "I": return struct.unpack_from("<i", b, o)[0], o + 4
    if t == "F": return struct.unpack_from("<f", b, o)[0], o + 4
    if t == "D": return struct.unpack_from("<d", b, o)[0], o + 8
    if t == "L": return struct.unpack_from("<q", b, o)[0], o + 8
    if t in "fdlib":
        n, enc, clen = struct.unpack_from("<III", b, o); o += 12
        raw = b[o:o + clen]; o += clen
        if enc == 1: raw = zlib.decompress(raw)
        fmt = {"f": "f", "d": "d", "l": "q", "i": "i", "b": "?"}[t]
        return list(struct.unpack_from("<" + fmt * n, raw)), o
    if t in "SR":
        n = struct.unpack_from("<I", b, o)[0]; o += 4; raw = b[o:o + n]; o += n
        return (raw.decode("utf-8", "replace") if t == "S" else raw), o
    raise ValueError(f"unknown FBX property type {t!r}")


def parse(path: str | Path) -> list[dict]:
    b = Path(path).read_bytes()
    if not b.startswith(MAGIC): raise ValueError("not a binary FBX file")
    ver = struct.unpack_from("<I", b, 23)[0]; big = ver >= 7500; hdr = 25 if big else 13

    def node(o: int):
        if big: end, nprops, plen, nlen = struct.unpack_from("<QQQB", b, o); o += 25
        else: end, nprops, plen, nlen = struct.unpack_from("<IIIB", b, o); o += 13
        if end == 0: return None, o
        name = b[o:o + nlen].decode(); o += nlen
        props = []
        for _ in range(nprops):
            v, o = _prop(b, o); props.append(v)
        kids = []
        while o < end:
            k, o = node(o)
            if k is None: break
            kids.append(k)
        return {"name": name, "props": props, "children": kids}, end

    out, o = [], 27
    while o < len(b) - hdr:
        n, o = node(o)
        if n is None: break
        out.append(n)
    return out


def _find(nodes, name): return [n for n in nodes if n["name"] == name]


def _p70(node) -> dict:
    d = {}
    for p in _find(node["children"], "Properties70") or []:
        for q in p["children"]:
            if q["name"] == "P" and len(q["props"]) >= 5: d[q["props"][0]] = q["props"][4:] if len(q["props"]) > 5 else q["props"][4]
    return d


def summary(path: str | Path) -> dict:
    """What the file declares: GlobalSettings axes and unit scale, every Model's local transform, and the extent of every Geometry's vertices (in the file's own units)."""
    t = parse(path)
    gs = _find(t, "GlobalSettings"); g = _p70(gs[0]) if gs else {}
    models = {}
    for n in _find(_find(t, "Objects")[0]["children"], "Model"):
        p = _p70(n); models[n["props"][1].split("\x00")[0]] = {"type": n["props"][2], "translation": p.get("Lcl Translation"), "rotation": p.get("Lcl Rotation"), "scaling": p.get("Lcl Scaling")}
    geos = {}
    for n in _find(_find(t, "Objects")[0]["children"], "Geometry"):
        v = [c for c in n["children"] if c["name"] == "Vertices"]
        if v:
            xs = v[0]["props"][0]; pts = [xs[i:i + 3] for i in range(0, len(xs), 3)]
            geos[n["props"][1].split("\x00")[0]] = {"min": [min(p[i] for p in pts) for i in range(3)], "max": [max(p[i] for p in pts) for i in range(3)]}
    return {"UpAxis": g.get("UpAxis"), "UpAxisSign": g.get("UpAxisSign"), "FrontAxis": g.get("FrontAxis"), "FrontAxisSign": g.get("FrontAxisSign"), "CoordAxis": g.get("CoordAxis"),
            "CoordAxisSign": g.get("CoordAxisSign"), "UnitScaleFactor": g.get("UnitScaleFactor"), "OriginalUnitScaleFactor": g.get("OriginalUnitScaleFactor"), "models": models, "geometry_extents": geos}
