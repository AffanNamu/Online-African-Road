"""The human base: MakeHuman's HM08 mesh (CC0) shaped by its macro and detail targets, in numpy. No Blender needed here.
Units: the data is in decimetres, y up, the figure faces +z; `Human.verts` is in metres with the soles at y = 0."""
from __future__ import annotations
import gzip, json, os
from functools import lru_cache
from pathlib import Path
import numpy as np


def data_dir() -> Path:
    tail = Path("makehumancommunity") / "mpfb2" / "src" / "mpfb" / "data"
    for p in [os.environ.get("ARO_MAKEHUMAN_DATA"), Path.home() / tail, Path("/home/user") / tail]:
        if p and (Path(p) / "3dobjs" / "base.obj").is_file(): return Path(p)
    raise FileNotFoundError("the MakeHuman base data was not found: clone makehumancommunity/mpfb2 and set ARO_MAKEHUMAN_DATA to its src/mpfb/data")


@lru_cache(maxsize=1)
def base():
    """(verts Nx3 dm, faces list of vertex index lists, face uv index lists, uvs Mx2, groups name -> [(lo, hi)])."""
    verts, uvs, faces, fuv = [], [], [], []
    for l in (data_dir() / "3dobjs" / "base.obj").read_text().splitlines():
        if l.startswith("v "): verts.append([float(x) for x in l.split()[1:4]])
        elif l.startswith("vt "): uvs.append([float(x) for x in l.split()[1:3]])
        elif l.startswith("f "):
            c = [t.split("/") for t in l.split()[1:]]; faces.append([int(t[0]) - 1 for t in c]); fuv.append([int(t[1]) - 1 if len(t) > 1 and t[1] else -1 for t in c])
    groups = json.loads((data_dir() / "mesh_metadata" / "basemesh_vertex_groups.json").read_text())
    return np.array(verts), faces, fuv, np.array(uvs), groups


@lru_cache(maxsize=None)
def target(name: str):
    """A shape target as (vertex indices, offsets in dm). `name` is relative to targets/, without the extension."""
    idx, off = [], []
    with gzip.open(data_dir() / "targets" / (name + ".target.gz"), "rt") as f:
        for l in f:
            p = l.split()
            if len(p) == 4 and not l.startswith("#"): idx.append(int(p[0])); off.append([float(p[1]), float(p[2]), float(p[3])])
    return np.array(idx, dtype=np.int64), np.array(off, dtype=np.float64).reshape(-1, 3)


def _two(v, names, cuts):
    """Weights of the named stops for a slider value: MakeHuman's piecewise-linear macro rule."""
    v = min(max(v, 0.0), 1.0)
    for (lo, hi), (a, b) in zip(zip(cuts, cuts[1:]), zip(names, names[1:])):
        if v <= hi or hi == cuts[-1]:
            t = (v - lo) / (hi - lo) if hi > lo else 0.0; t = min(max(t, 0.0), 1.0); return {a: 1.0 - t, b: t}
    return {names[-1]: 1.0}


def macro_targets(gender, age, muscle, weight, height, race="african"):
    """target name -> weight for the macro sliders (0..1 each; gender 0 = female, 1 = male; age 0.1875 = child, 0.5 = young adult, 1 = old)."""
    g = {"female": 1.0 - gender, "male": gender}
    a = _two(age, ["baby", "child", "young", "old"], [0.0, 0.1875, 0.5, 1.0])
    m = _two(muscle, ["minmuscle", "averagemuscle", "maxmuscle"], [0.0, 0.5, 1.0])
    w = _two(weight, ["minweight", "averageweight", "maxweight"], [0.0, 0.5, 1.0])
    out = {}
    for gk, gv in g.items():
        for ak, av in a.items():
            if gv * av > 1e-4: out[f"macrodetails/{race}-{gk}-{ak}"] = gv * av
            for mk, mv in m.items():
                for wk, wv in w.items():
                    k = gv * av * mv * wv
                    if k <= 1e-4: continue
                    out[f"macrodetails/universal-{gk}-{ak}-{mk}-{wk}"] = k
                    if abs(height - 0.5) > 1e-3: out[f"macrodetails/height/{gk}-{ak}-{mk}-{wk}-{'minheight' if height < 0.5 else 'maxheight'}"] = k * abs(height - 0.5) * 2.0
    return out


FACE_TARGETS = ["nose/nose-scale-horiz", "nose/nose-scale-vert", "nose/nose-width1", "nose/nose-width2", "nose/nose-width3", "nose/nose-point", "nose/nose-hump", "nose/nose-nostrils-width",
                "mouth/mouth-scale-horiz", "mouth/mouth-scale-vert", "mouth/mouth-lowerlip-volume", "mouth/mouth-upperlip-volume", "cheek/l-cheek-volume", "cheek/r-cheek-volume",
                "cheek/l-cheek-bones", "cheek/r-cheek-bones", "chin/chin-width", "chin/chin-height", "chin/chin-prominent", "chin/chin-jaw-angle", "forehead/forehead-bulge", "forehead/forehead-scale-vert",
                "head/head-scale-horiz", "head/head-scale-vert", "head/head-fat", "eyes/l-eye-scale", "eyes/r-eye-scale", "eyes/l-eye-height2", "eyes/r-eye-height2", "ears/l-ear-scale", "ears/r-ear-scale"]


def face_targets(seed: int, strength=0.45):
    """An individual face: every detail slider gets a seeded value between its two ends. Paired left and right sliders get the same value."""
    rng = np.random.default_rng(seed); out = {}; side = {}
    for t in FACE_TARGETS:
        key = t.replace("/l-", "/").replace("/r-", "/")
        v = side.setdefault(key, float(rng.uniform(-1.0, 1.0)) * strength)
        name = t + ("-incr" if v > 0 else "-decr")
        if (data_dir() / "targets" / (name + ".target.gz")).is_file() and abs(v) > 0.03: out[name] = abs(v)
    return out


class Human:
    """A shaped human: metres, soles at y = 0. `mask(group)` selects vertices of a base-mesh group; `joint(name)` is a joint position."""
    def __init__(self, gender=1.0, age=0.5, muscle=0.5, weight=0.5, height=0.5, seed=0, face_strength=0.45):
        v0, self.faces, self.fuv, self.uvs, self.groups = base()
        v = v0.copy(); self.params = dict(gender=gender, age=age, muscle=muscle, weight=weight, height=height, seed=seed)
        for name, w in {**macro_targets(gender, age, muscle, weight, height), **face_targets(seed, face_strength)}.items():
            idx, off = target(name); v[idx] += off * w
        ground = v[self._ids("joint-ground")].mean(0)[1]
        v[:, 1] -= ground; self.verts = v * 0.1

    def _ids(self, group):
        return np.concatenate([np.arange(lo, hi + 1) for lo, hi in self.groups[group]])

    def mask(self, group):
        m = np.zeros(len(self.verts), dtype=bool); m[self._ids(group)] = True; return m

    def joint(self, name):
        return self.verts[self._ids("joint-" + name)].mean(0)

    @property
    def stature(self): return float(self.verts[self.mask("body")][:, 1].max())
