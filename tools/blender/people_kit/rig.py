"""The skeleton of the human base (MakeHuman's game rig: 53 bones, weights for every vertex of the base mesh and its helpers) and linear blend skinning, in numpy.
A pose names the direction a bone should point in the world; a bone that is not named follows its parent. Standing and walking poses are computed here, so a person can be drawn with the
arms down, the knees and elbows bending, and the clothes (which carry the weights of the helper vertices they were made from) following the limbs."""
from __future__ import annotations
import json
from functools import lru_cache
import numpy as np
from .human import Human, base, data_dir


@lru_cache(maxsize=1)
def rig_data():
    r = json.loads((data_dir() / "rigs" / "standard" / "rig.game_engine.json").read_text())
    w = json.loads((data_dir() / "rigs" / "standard" / "weights.game_engine.json").read_text())["weights"]
    names = []; todo = dict(r)
    while todo:                                                               # parents before children
        for n in sorted(todo):
            if todo[n]["parent"] == "" or todo[n]["parent"] in names: names.append(n); del todo[n]; break
    n_verts = len(base()[0]); W = np.zeros((n_verts, len(names)), dtype=np.float32)
    for b, pairs in w.items():
        j = names.index(b)
        for v, x in pairs: W[v, j] = x
    s = W.sum(1, keepdims=True); s[s < 1e-9] = 1.0
    return r, names, W / s


def _rot_between(a, b):
    """The smallest rotation taking unit vector a to unit vector b."""
    a = a / np.linalg.norm(a); b = b / np.linalg.norm(b); v = np.cross(a, b); c = float(a @ b); s = np.linalg.norm(v)
    if s < 1e-9: return np.eye(3)
    k = np.array([[0, -v[2], v[1]], [v[2], 0, -v[0]], [-v[1], v[0], 0]]) / s
    return np.eye(3) + s * k + (1 - c) * (k @ k)


def _joint(h: Human, spec):
    if spec.get("cube_name"): return h.joint(spec["cube_name"][len("joint-"):])
    if spec.get("vertex_indices"): return h.verts[spec["vertex_indices"]].mean(0)
    return np.array(spec.get("default_position", [0, 0, 0]), dtype=np.float64)


class Rig:
    def __init__(self, h: Human):
        self.h = h; r, self.names, self.W = rig_data(); self.index = {n: i for i, n in enumerate(self.names)}
        self.parent = [self.index.get(r[n]["parent"], -1) for n in self.names]
        self.head = np.array([_joint(h, r[n]["head"]) for n in self.names]); self.tail = np.array([_joint(h, r[n]["tail"]) for n in self.names])

    def rest_dir(self, bone):
        i = self.index[bone]; d = self.tail[i] - self.head[i]; return d / np.linalg.norm(d)

    def transforms(self, dirs: dict, root_offset=(0.0, 0.0, 0.0)):
        """World transform (rotation, translation) of every bone for a pose given as bone -> world direction."""
        R = [None] * len(self.names); T = [None] * len(self.names); off = np.array(root_offset, dtype=np.float64)
        for i, n in enumerate(self.names):
            p = self.parent[i]; Rp = R[p] if p >= 0 else np.eye(3); Tp = T[p] if p >= 0 else off
            if n in dirs:
                Wb = _rot_between(self.rest_dir(n), np.array(dirs[n], dtype=np.float64)); Rb = Rp.T @ Wb         # local rotation so that the bone points where asked
            else: Rb = np.eye(3)
            R[i] = Rp @ Rb; T[i] = Rp @ (self.head[i] - Rb @ self.head[i]) + Tp
        return np.array(R), np.array(T)

    def skin(self, verts, ids=None, bone=None, pose=None):
        """Posed positions of `verts` (rest positions, metres). `ids`: the base-mesh vertex each one was made from (its weights are used); or `bone`: one bone carries them all."""
        R, T = pose
        if bone is not None: i = self.index[bone]; return verts @ R[i].T + T[i]
        W = self.W[ids]; out = np.zeros_like(verts)
        for j in np.nonzero(W.sum(0) > 1e-6)[0]:
            out += W[:, j:j + 1] * (verts @ R[j].T + T[j])
        return out


def _dir(side, lateral, pitch_deg):
    """A direction that points down, tilted `pitch_deg` forward (+) or back (-) and `lateral` outward on the given side."""
    a = np.radians(pitch_deg); return np.array([side * lateral, -np.cos(a), np.sin(a)])


def standing(sway=0.0):
    """Arms down with the elbows a little bent, legs under the hips."""
    d = {}
    for s, k in ((1.0, "l"), (-1.0, "r")):
        d[f"upperarm_{k}"] = _dir(s, 0.13, -3 + sway * s * 1.5); d[f"lowerarm_{k}"] = _dir(s, 0.07, 14 + sway * s * 2.0); d[f"hand_{k}"] = _dir(s, 0.03, 20)
        d[f"thigh_{k}"] = _dir(s, 0.035, 1.5); d[f"calf_{k}"] = _dir(s, 0.02, -2.0)
    return d, (0.0, 0.0, 0.0)


def walking(phase, stride=1.0):
    """A walk cycle (phase 0..1): legs and arms in opposition, the knee flexing as the leg swings through, the elbow bending on the forward swing, the body rising and falling a little."""
    d = {}; two_pi = 2 * np.pi
    for s, k, ph in ((1.0, "l", phase), (-1.0, "r", phase + 0.5)):
        sw = np.sin(two_pi * ph); thigh = 23.0 * sw * stride
        knee = (6.0 + 40.0 * max(0.0, np.sin(two_pi * ph + 2.2))) * (0.5 + 0.5 * stride)                 # flexes while the leg comes forward
        d[f"thigh_{k}"] = _dir(s, 0.03, thigh); d[f"calf_{k}"] = _dir(s, 0.02, thigh - knee)
        arm = -17.0 * sw * (0.4 + 0.6 * stride)
        d[f"upperarm_{k}"] = _dir(s, 0.12, arm - 2); d[f"lowerarm_{k}"] = _dir(s, 0.06, arm + 16 + 12 * max(0.0, -sw)); d[f"hand_{k}"] = _dir(s, 0.03, arm + 24 + 12 * max(0.0, -sw))
    bob = 0.018 * stride * abs(np.sin(two_pi * phase)) - 0.012 * stride
    return d, (0.0, bob, 0.0)
