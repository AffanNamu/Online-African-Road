"""A dressed person from the shaped human base: what is seen of the body (head, hands, feet), painted skin, hair, brows, lips and eyes, and clothes fitted from the base mesh's own helpers
(the full-body "tights" helper for shirts, trousers, sleeves and robe tops; the "skirt" helper for wrappers, dresses and robe skirts), which follow the body's shape through every target.
Pure numpy: a figure is a list of parts (name, vertices in metres, faces, vertex colours in sRGB 0..1). Blender only turns them into meshes."""
from __future__ import annotations
import json
from functools import lru_cache
import numpy as np
from .human import Human, base, data_dir

SKIN = [(0.27, 0.17, 0.125), (0.37, 0.24, 0.17), (0.48, 0.32, 0.225)]             # deep, medium and lighter brown
HAIR = (0.035, 0.03, 0.028); GREY_HAIR = (0.62, 0.61, 0.60)


@lru_cache(maxsize=1)
def bone_of():
    """For every vertex of the body: the index of its strongest bone in the game rig, and the bone names."""
    w = json.loads((data_dir() / "rigs" / "standard" / "weights.game_engine.json").read_text())["weights"]; names = sorted(w)
    n = len(base()[0]); best = np.full(n, -1); bw = np.zeros(n)
    for i, b in enumerate(names):
        for v, x in w[b]:
            if x > bw[v]: bw[v] = x; best[v] = i
    return best, names


def _region(names_wanted):
    best, names = bone_of(); want = {i for i, n in enumerate(names) if any(n.startswith(p) for p in names_wanted)}
    return np.isin(best, list(want))


def _normals(verts, faces):
    n = np.zeros_like(verts)
    for f in faces:
        p = verts[f]; fn = np.cross(p[1] - p[0], p[2] - p[0])
        for i in f: n[i] += fn
    l = np.linalg.norm(n, axis=1, keepdims=True); l[l < 1e-12] = 1.0
    return n / l


def _sub(verts, faces, keep_vertex, colours=None):
    """The faces whose vertices are all kept, re-indexed."""
    idx = -np.ones(len(verts), dtype=np.int64); ids = np.nonzero(keep_vertex)[0]; idx[ids] = np.arange(len(ids))
    out = [[int(idx[i]) for i in f] for f in faces if all(keep_vertex[i] for i in f)]
    return verts[ids].copy(), out, (colours[ids].copy() if colours is not None else None), ids


def _nearest_bone(points, body_pts, body_bone, chunk=400):
    out = np.empty(len(points), dtype=np.int64)
    for a in range(0, len(points), chunk):
        d = ((points[a:a + chunk, None, :] - body_pts[None, :, :]) ** 2).sum(-1); out[a:a + chunk] = body_bone[d.argmin(1)]
    return out


def _mask_at_uv(name, h: Human):
    """A UV mask of the base data (lips, eyelids, fingernails) sampled at every vertex: 0..1."""
    from PIL import Image
    im = np.asarray(Image.open(data_dir() / "textures" / name).convert("L"), dtype=np.float64) / 255.0
    vt = np.zeros((len(h.verts), 2))
    for f, fu in zip(h.faces, h.fuv):
        for v, u in zip(f, fu):
            if u >= 0: vt[v] = h.uvs[u]
    x = np.clip((vt[:, 0] * (im.shape[1] - 1)).astype(int), 0, im.shape[1] - 1); y = np.clip(((1.0 - vt[:, 1]) * (im.shape[0] - 1)).astype(int), 0, im.shape[0] - 1)
    return im[y, x]


def lathe(rings, sides=20, cap_top=True, cap_bottom=False):
    """A turned shape about a vertical axis: rings of (y, cx, cz, rx, rz) from the bottom up. Returns (verts, faces)."""
    v = []; f = []
    for (y, cx, cz, rx, rz) in rings:
        for i in range(sides):
            a = 2 * np.pi * (i + 0.5) / sides; v.append([cx + np.sin(a) * rx, y, cz + np.cos(a) * rz])
    for r in range(len(rings) - 1):
        for i in range(sides):
            a0, c0, a1, c1 = r * sides + i, r * sides + (i + 1) % sides, (r + 1) * sides + i, (r + 1) * sides + (i + 1) % sides; f.append([a0, c0, c1, a1])
    if cap_top:
        y, cx, cz, _, _ = rings[-1]; v.append([cx, y, cz]); c = len(v) - 1; k = (len(rings) - 1) * sides
        for i in range(sides): f.append([c, k + i, k + (i + 1) % sides])
    if cap_bottom:
        y, cx, cz, _, _ = rings[0]; v.append([cx, y, cz]); c = len(v) - 1
        for i in range(sides): f.append([c, (i + 1) % sides, i])
    return np.array(v), f


def smooth(verts, faces, iterations=3, lam=0.5, keep=None):
    """Laplacian smoothing: every vertex moves towards the mean of its neighbours. `keep` vertices (a boolean mask) stay where they are (hems, collars)."""
    n = len(verts); nb = [set() for _ in range(n)]
    for f in faces:
        for a, b in zip(f, f[1:] + f[:1]): nb[a].add(b); nb[b].add(a)
    v = verts.copy()
    for _ in range(iterations):
        m = np.array([v[list(x)].mean(0) if x else v[i] for i, x in enumerate(nb)]); nv = v + lam * (m - v)
        if keep is not None: nv[keep] = v[keep]
        v = nv
    return v


class Figure:
    """A person in the rest pose: `parts` is a list of dicts (name, verts, faces, colours, kind = skin | cloth | eye, and `ids` = the base-mesh vertex each vertex was made from, or `bone`)."""
    def __init__(self, h: Human, skin=1, hair=HAIR):
        self.h = h; self.parts = []; self.skin = np.array(SKIN[skin]); self.hair = np.array(hair)
        self.best, self.names = bone_of()
        self.eye_l, self.eye_r = h.joint("l-eye"), h.joint("r-eye"); self.eye_y = float((self.eye_l[1] + self.eye_r[1]) / 2)
        self.head = _region(["head", "neck"]) & h.mask("body")
        hp = h.verts[_region(["head"]) & h.mask("body")]
        self.head_c = np.array([0.0, 0.0, float((hp[:, 2].max() + hp[:, 2].min()) / 2)]); self.head_hw = float(np.abs(hp[:, 0]).max()); self.head_hd = float((hp[:, 2].max() - hp[:, 2].min()) / 2)
        self.top = float(hp[:, 1].max()); self.k = h.stature / 1.75                                            # sizes of garments scale with the person
        self.body_faces = [f for f in h.faces if max(f) < 13380]
        n = np.zeros_like(h.verts); bn = _normals(h.verts, self.body_faces); n[:] = bn; self.body_n = n
        self.shoulder_y = float(h.joint("l-shoulder")[1]); self.waist_y = float(h.joint("spine-4")[1]); self.hip_y = float(h.joint("l-upper-leg")[1])
        self.knee_y = float(h.joint("l-knee")[1]); self.ankle_y = float(h.joint("l-ankle")[1])

    def _add(self, name, verts, faces, colour, kind, ids=None, bone=None, pattern=None):
        c = np.tile(np.array(colour, dtype=np.float64), (len(verts), 1)) if np.ndim(colour) == 1 else colour
        if pattern is not None and np.ndim(colour) == 1: c = pattern(np.asarray(verts, dtype=np.float64), c)   # previews show the pattern per vertex; the texture bake evaluates it per texel
        self.parts.append(dict(name=name, verts=verts, faces=faces, colours=c, kind=kind, ids=ids, bone=bone, pattern=pattern, colour=(np.array(colour, dtype=np.float64) if np.ndim(colour) == 1 else None)))

    # ------------------------------------------------------------------------------------------ the body that shows
    def skin_colours(self, hair_painted=True, beard=None):
        h = self.h; n = len(h.verts); c = np.tile(self.skin, (n, 1)).astype(np.float64); v = h.verts; k = self.k
        lips = _mask_at_uv("mpfb_lips.jpg", h)[:, None]; c = c * (1 - 0.5 * lips) + np.array([0.36, 0.15, 0.14]) * 0.5 * lips
        lids = _mask_at_uv("mpfb_eyelids.jpg", h)[:, None]; c = c * (1 - 0.25 * lids)
        nails = _mask_at_uv("mpfb_fingernails.jpg", h)[:, None]; c = c * (1 - 0.5 * nails) + np.array([0.80, 0.64, 0.57]) * 0.5 * nails
        head = self.head; only_head = _region(["head"])
        for e in (self.eye_l, self.eye_r):                                                                     # eyebrows: an arc above each eye
            dx = (v[:, 0] - e[0]) / (0.031 * k); arc = e[1] + (0.027 - 0.006 * dx * dx) * k
            w = np.clip(1 - np.abs(dx), 0, 1) ** 0.5 * np.clip(1 - np.abs(v[:, 1] - arc) / (0.0065 * k), 0, 1) * (v[:, 2] > e[2] - 0.015) * head
            c = c * (1 - 0.9 * w[:, None]) + self.hair * 0.9 * w[:, None]
        if hair_painted:                                                                                       # cropped hair: the scalp above a hairline that falls from the forehead to the nape
            back = np.clip((self.eye_l[2] - 0.02 - v[:, 2]) / (self.head_hd * 1.1), 0, 1)
            line = self.eye_y + (0.066 - 0.115 * back ** 1.2) * k
            w = np.clip((v[:, 1] - line) / (0.008 * k), 0, 1) * head * only_head
            ear = (np.abs(v[:, 0]) > self.head_hw * 0.9) & (v[:, 1] < self.eye_y + 0.03 * k); w = w * ~ear
            c = c * (1 - w[:, None]) + self.hair * w[:, None]
        if beard is not None:                                                                                  # a short beard along the jaw and the chin, not on the neck
            lo, hi = self.eye_y - 0.128 * k, self.eye_y - 0.078 * k; front = np.clip((v[:, 2] - (self.head_c[2] + self.head_hd * 0.05)) / (0.03 * k), 0, 1)
            w = np.clip((hi - v[:, 1]) / (0.012 * k), 0, 1) * np.clip((v[:, 1] - lo) / (0.010 * k), 0, 1) * front * only_head * (1 - lips[:, 0])
            c = c * (1 - 0.8 * w[:, None]) + np.array(beard) * 0.8 * w[:, None]
        return c

    def add_body(self, show, colours):
        """The skin that shows: the named regions, and the base of the neck down to just under the neckline (so no gap opens between the neck and a collar)."""
        v0 = self.h.verts; throat = (v0[:, 1] > self.shoulder_y - 0.045 * self.k) & ~(self._arm_param(v0) > 0.12)
        v, f, c, ids = _sub(self.h.verts, self.body_faces, (show | throat) & self.h.mask("body"), colours)
        self._add("body", v, f, c, "skin", ids=ids)

    def add_eyes(self):
        h = self.h
        for g in ("helper-l-eye", "helper-r-eye"):
            m = h.mask(g); v, f, _, ids = _sub(h.verts, h.faces, m); ctr = v.mean(0); d = v - ctr; d /= np.linalg.norm(d, axis=1, keepdims=True) + 1e-9
            ang = np.degrees(np.arccos(np.clip(d[:, 2], -1, 1)))
            c = np.tile(np.array([0.92, 0.90, 0.87]), (len(v), 1)); c[ang < 27] = (0.17, 0.09, 0.05); c[ang < 11] = (0.01, 0.01, 0.01)
            self._add(g, v, f, c, "eye", ids=ids)

    # ------------------------------------------------------------------------------------------ clothes from the helpers
    def _tights(self):
        h = self.h; m = h.mask("helper-tights"); faces = [f for f in h.faces if all(m[i] for i in f)]
        ids = np.nonzero(m)[0]; body = h.mask("body"); bone = _nearest_bone(h.verts[ids], h.verts[body], self.best[body])
        vb = np.full(len(h.verts), -1); vb[ids] = bone
        return m, faces, vb, _normals(h.verts, faces)

    def _arm_param(self, v):
        """How far along the arm a point lies: 0 at the shoulder, 1 at the elbow, 2 at the wrist (each side by its own joints); -1 for points that are not on an arm."""
        h = self.h; out = np.full(len(v), -1.0)
        for side, k in ((1, "l"), (-1, "r")):
            sh, el, wr = h.joint(f"{k}-shoulder"), h.joint(f"{k}-elbow"), h.joint(f"{k}-hand")
            a = (v - sh) @ (el - sh) / ((el - sh) @ (el - sh)); b = (v - el) @ (wr - el) / ((wr - el) @ (wr - el))
            p = np.where(a <= 1.0, a, 1.0 + b); on = (np.sign(v[:, 0]) == side) & (np.abs(v[:, 0]) > abs(sh[0]) * 0.9)
            out = np.where(on, p, out)
        return out

    def garment(self, name, bones, colour, offset, hem=None, top=None, neck=None, sleeve=None, loosen=0.0, smoothing=2, leg_to=None, straight_leg=0.0, kind="cloth", sleeve_flare=0.0, pattern=None):
        """
        A fitted piece: the tights helper over the named bones, pushed out along its normals by `offset` metres (and sideways by `loosen`), with clean edges: `hem` (nothing below this
        height), `top` (nothing above it: a waistband), `neck` (a round neckline of this radius about the neck), `sleeve` (nothing beyond this arm position: 1 = elbow, 2 = wrist),
        `leg_to` (trouser legs end at this height). Vertices just beyond an edge are moved onto it, so every edge is a clean line. `straight_leg` gives trouser legs at least that radius.
        """
        m, faces, vb, nrm = self._tights(); names = self.names; h = self.h; k = self.k
        want = np.array([any(names[b].startswith(p) for p in bones) if b >= 0 else False for b in vb]) & m
        v = h.verts.copy(); v += nrm * offset
        if loosen: v[:, [0, 2]] += nrm[:, [0, 2]] * loosen
        if straight_leg:
            for kk in ("l", "r"):                                                                              # a trouser leg hangs straight: no narrower than this below the thigh
                hip, knee, ank = h.joint(f"{kk}-upper-leg"), h.joint(f"{kk}-knee"), h.joint(f"{kk}-ankle")
                leg = np.array([names[b].startswith((f"thigh_{kk}", f"calf_{kk}")) if b >= 0 else False for b in vb]) & (v[:, 1] < hip[1] - 0.10 * k)
                t = np.clip((knee[1] - v[:, 1]) / (knee[1] - ank[1]), 0, 1); u = np.clip((hip[1] - v[:, 1]) / (hip[1] - knee[1]), 0, 1)
                axis = np.where((v[:, 1] > knee[1])[:, None], hip[[0, 2]] + (knee[[0, 2]] - hip[[0, 2]]) * u[:, None], knee[[0, 2]] + (ank[[0, 2]] - knee[[0, 2]]) * t[:, None])
                d = v[:, [0, 2]] - axis; r = np.linalg.norm(d, axis=1); r[r < 1e-6] = 1e-6; grow = np.maximum(r, straight_leg * k * np.clip(u * 1.5, 0, 1)) / r
                v[leg] = np.column_stack([axis[:, 0] + d[:, 0] * grow, v[:, 1], axis[:, 1] + d[:, 1] * grow])[leg]
        arm_bone = np.array([names[b].startswith(("upperarm", "lowerarm", "hand")) if b >= 0 else False for b in vb])
        arm = np.where(arm_bone, self._arm_param(h.verts), -1.0); on_arm = arm > 0.25                    # only what the rig moves with an arm is sleeve (a thigh beside the hip is not)
        if sleeve_flare:                                                                                       # a wide sleeve: it opens out from the shoulder to the wrist
            for side, kk in ((1, "l"), (-1, "r")):
                s0, el, wr = h.joint(f"{kk}-shoulder"), h.joint(f"{kk}-elbow"), h.joint(f"{kk}-hand"); on = (arm > 0.3) & (np.sign(h.verts[:, 0]) == side)
                ap = np.where((arm <= 1)[:, None], s0 + (el - s0) * np.clip(arm, 0, 1)[:, None], el + (wr - el) * np.clip(arm - 1, 0, 1)[:, None])
                v[on] = (ap + (v - ap) * (1 + sleeve_flare * np.clip(arm / 2.0, 0, 1))[:, None])[on]
        lo = hem if hem is not None else leg_to
        keep = want.copy(); edge = np.zeros(len(v), dtype=bool)
        if lo is not None:
            below = (v[:, 1] < lo) & ~on_arm; band = below & (v[:, 1] > lo - 0.035); v[band, 1] = lo; edge |= band; keep &= ~(below & ~band)
        if top is not None:
            above = v[:, 1] > top; band = above & (v[:, 1] < top + 0.035); v[band, 1] = top; edge |= band; keep &= ~(above & ~band)
        if neck is not None:                                                                                   # a round neckline: a cylinder about the neck
            n0 = h.joint("neck"); d = v[:, [0, 2]] - n0[[0, 2]]; r = np.linalg.norm(d, axis=1); r[r < 1e-6] = 1e-6
            inside = (r < neck) & (v[:, 1] > self.shoulder_y - 0.03 * k); band = inside & (r > neck - 0.035)
            v[band, 0] = n0[0] + d[band, 0] / r[band] * neck; v[band, 2] = n0[2] + d[band, 1] / r[band] * neck; edge |= band; keep &= ~(inside & ~band)
        if sleeve is not None:
            for side, kk in ((1, "l"), (-1, "r")):
                a0, a1 = (h.joint(f"{kk}-shoulder"), h.joint(f"{kk}-elbow")) if sleeve <= 1 else (h.joint(f"{kk}-elbow"), h.joint(f"{kk}-hand"))
                t = sleeve if sleeve <= 1 else sleeve - 1; d = a1 - a0; cut = a0 + d * t; beyond = (arm > sleeve) & (np.sign(h.verts[:, 0]) == side); band = beyond & (arm < sleeve + 0.2)
                v[band] -= np.outer((v[band] - cut) @ d / (d @ d), d); edge |= band; keep &= ~(beyond & ~band)
        faces = [f for f in faces if not all(edge[i] for i in f)]                                              # no faces lying in a cut itself
        pv, pf, _, ids = _sub(v, faces, keep)
        used = np.zeros(len(pv), dtype=bool); used[[i for f in pf for i in f]] = True
        if smoothing: pv = smooth(pv, pf, smoothing, 0.5, keep=edge[ids])
        self._add(name, pv, pf, colour, kind, ids=ids, pattern=pattern)

    def skirt(self, name, colour, hem, flare=0.22, offset=0.014, smoothing=2, pattern=None):
        """A wrapper, dress skirt or robe skirt: the skirt helper stretched from the waist down to `hem` metres above the ground, widening to the hem. Its top ring stays on the waist."""
        h = self.h; m = h.mask("helper-skirt"); faces = [f for f in h.faces if all(m[i] for i in f)]
        v = h.verts.copy(); nrm = _normals(v, faces); ys = v[m][:, 1]; top, bot = ys.max(), ys.min(); axis = v[m][:, [0, 2]].mean(0)
        t = np.clip((top - v[:, 1]) / (top - bot), 0, 1)
        v[:, [0, 2]] = axis + (v[:, [0, 2]] - axis) * (1 + flare * t[:, None]) + nrm[:, [0, 2]] * offset
        v[:, 1] = top - t * (top - hem)
        pv, pf, _, ids = _sub(v, faces, m)
        if smoothing: pv = smooth(pv, pf, smoothing, 0.4, keep=(pv[:, 1] > top - 0.01) | (pv[:, 1] < hem + 0.01))
        self._add(name, pv, pf, colour, "cloth", ids=ids, pattern=pattern); return top

    def shoes(self, colour, height_extra=0.008):
        """Closed shoes: for each foot a smooth shell from heel to toe (flat sole, rounded upper) sized on the foot itself at eight stations along it. One rigid piece per foot."""
        h = self.h; v = h.verts; feet = _region(["foot", "ball"]) & h.mask("body") & (v[:, 1] < self.ankle_y + 0.01)
        for side, kk in ((1, "l"), (-1, "r")):
            fv = v[feet & (np.sign(v[:, 0]) == side)]
            if len(fv) == 0: continue
            z0, z1 = fv[:, 2].min() - 0.004, fv[:, 2].max() + 0.006; n = 9; arc = 8; rings = []
            for i in range(n):
                z = z0 + (z1 - z0) * i / (n - 1); slab = fv[np.abs(fv[:, 2] - z) < 0.016]
                if len(slab) < 3: slab = fv[np.argsort(np.abs(fv[:, 2] - z))[:12]]
                xc = (slab[:, 0].max() + slab[:, 0].min()) / 2; hw = (slab[:, 0].max() - slab[:, 0].min()) / 2 + 0.006; top = min(slab[:, 1].max() + height_extra, self.ankle_y + 0.02)
                if i == 0 or i == n - 1: hw *= 0.55; top *= 0.75                                                # rounded heel and toe
                rings.append([(xc + hw * np.cos(a), 0.004 + (top - 0.004) * np.sin(a) ** 0.8, z) for a in np.linspace(0, np.pi, arc)])
            pts = np.array([p for r in rings for p in r]); faces = []
            for i in range(n - 1):
                for a in range(arc - 1): faces.append([i * arc + a, (i + 1) * arc + a, (i + 1) * arc + a + 1, i * arc + a + 1])
                faces.append([i * arc + arc - 1, (i + 1) * arc + arc - 1, (i + 1) * arc, i * arc])              # the sole
            faces.append(list(range(arc))); faces.append(list(range((n - 1) * arc + arc - 1, (n - 1) * arc - 1, -1)))
            faces = [f[::-1] for f in faces]                                                                # outward (the rings run over the top from +x to -x)
            self._add(f"shoe_{kk}", pts, faces, colour, "cloth", bone=f"foot_{kk}")

    # ------------------------------------------------------------------------------------------ headwear
    def kufi(self, colour, pattern=None):
        k = self.k; y0 = self.eye_y + 0.062 * k; c = self.head_c
        v, f = lathe([(y0, 0, c[2] - 0.004, self.head_hw + 0.004, self.head_hd + 0.004), (y0 + 0.045 * k, 0, c[2] - 0.006, self.head_hw + 0.002, self.head_hd + 0.0),
                      (self.top + 0.012 * k, 0, c[2] - 0.008, self.head_hw * 0.93, self.head_hd * 0.93)], sides=24)
        self._add("kufi", v, f, colour, "cloth", bone="head", pattern=pattern)

    def hijab(self, colour, drape_to):
        """
        A hijab that frames the real face. The head piece is the surface of the head and the neck with an oval left open for the face; the cloth hugs the face along that oval and hangs
        loose further back: no point of it lies inside an ellipse that widens from the widest part of the head to the shoulders, so the ears and the neck do not show through. The cape
        falls from the base of the neck over the shoulders and the arms to a straight hem at `drape_to` metres. It is fitted round the body as it stands with the arms down (the pose
        people are seen in), cloth only ever falling straight or outwards, and it is carried by the upper spine, so the arms swing under it.
        """
        from .rig import Rig, standing
        h = self.h; k = self.k; v = h.verts; body = h.mask("body"); sh = self.shoulder_y; neck = h.joint("neck")
        cy = self.eye_y - 0.030 * k; ex, ey = 0.058 * k, 0.084 * k                                             # the face oval: chin to above the brows, cheek to cheek
        fo = np.hypot(v[:, 0] / ex, (v[:, 1] - cy) / ey); front = v[:, 2] > self.head_c[2] + self.head_hd * 0.25
        face = front & (fo < 1.0)
        cover = (self.head | ((v[:, 1] > sh - 0.01 * k) & ~(self._arm_param(v) > 0.1))) & body & ~face
        edge = front & (fo >= 1.0) & (fo < 1.25)
        p = v.copy(); p[edge, 0] = v[edge, 0] / fo[edge]; p[edge, 1] = cy + (v[edge, 1] - cy) / fo[edge]          # a clean oval edge
        hug = np.clip((fo - 1.0) / 0.55, 0, 1) * front + (~front)                                               # 0 on the edge: the cloth lies on the face there
        p = p + self.body_n * (0.004 + 0.008 * hug)[:, None]
        y_top = self.eye_y + 0.035 * k; t = np.clip((y_top - p[:, 1]) / (y_top - sh), 0, 1); tt = t ** 1.5
        ax = self.head_c[[0, 2]][None, :] * (1 - t[:, None]) + neck[[0, 2]][None, :] * t[:, None]
        rx = (self.head_hw + 0.014) * (1 - tt) + 0.135 * k * tt; fr = p[:, 2] > ax[:, 1]
        rz = np.where(fr, self.head_hd * 0.80 * (1 - t ** 1.2) + 0.105 * k * t ** 1.2, (self.head_hd + 0.016) * (1 - tt) + 0.118 * k * tt)
        d = p[:, [0, 2]] - ax; e = np.hypot(d[:, 0] / rx, d[:, 1] / rz); e[e < 1e-6] = 1e-6
        w = np.clip((self.eye_y + 0.065 * k - p[:, 1]) / (0.03 * k), 0, 1) * hug                               # fades in below the crown and away from the face
        p[:, [0, 2]] = ax + d * (1 + (np.maximum(1.0 / e, 1.0) - 1) * w)[:, None]
        pv, pf, _, ids = _sub(p, self.body_faces, cover)
        self._add("hijab_head", smooth(pv, pf, 3, 0.5, keep=edge[ids]), pf, colour, "cloth", ids=ids)
        # the cape, fitted in the standing pose and carried back to the rest pose by its bone
        rig = Rig(h); tr = rig.transforms(*standing()); bi = rig.index["spine_03"]; R, T = tr[0][bi], tr[1][bi]
        part = (_region(["spine_02", "spine_03", "clavicle", "upperarm", "neck"]) & body); pts = [rig.skin(v[part], ids=np.nonzero(part)[0], pose=tr)]
        for q in self.parts:                                                                                   # and the clothes already on: the cape lies over them
            if q["kind"] == "cloth": pts.append(rig.skin(q["verts"], ids=q["ids"], bone=q["bone"], pose=tr))
        sv = np.concatenate(pts); sv = sv[sv[:, 1] > drape_to - 0.05]
        nk = rig.skin(neck[None, :], bone="neck_01", pose=tr)[0] if "neck_01" in rig.index else neck
        top = float(neck[1]) - 0.012 * k                                                                       # from the base of the neck (the shoulder tops are 5 cm above the shoulder joint)
        ys = list(np.linspace(top, sh + 0.035 * k, 5)) + [sh + 0.01 * k, sh - 0.03 * k, sh - 0.08 * k, sh - 0.15 * k, (sh - 0.15 * k + drape_to) / 2, drape_to]
        sides = 36; ang = 2 * np.pi * (np.arange(sides) + 0.5) / sides; cz = float(sv[:, 2].mean()) * 0.5 + float(nk[2]) * 0.5
        rel = sv[:, [0, 2]] - np.array([0.0, cz]); sa = np.arctan2(rel[:, 0], rel[:, 1]); sr = np.hypot(rel[:, 0], rel[:, 1])
        rings = []; prev = None
        for j, y in enumerate(ys):
            base = np.full(sides, 0.085 * k) if j == 0 else np.full(sides, 0.10 * k)
            near = np.abs(sv[:, 1] - y) < 0.013 * k; r = base.copy()
            for i, a0 in enumerate(ang):
                m = near & (np.abs(np.angle(np.exp(1j * (sa - a0)))) < 0.2)
                if m.any(): r[i] = max(r[i], float(sr[m].max()) + 0.014 * k)
            if prev is not None: r = np.maximum(r, prev * (1.0 if y > sh - 0.05 * k else 1.015))              # cloth falls straight, or opens a little below the shoulders
            r = (np.roll(r, 1) + 2 * r + np.roll(r, -1)) / 4; rings.append(r); prev = r
        cv = np.array([[np.sin(a0) * r[i], y, cz + np.cos(a0) * r[i]] for y, r in zip(ys, rings) for i, a0 in enumerate(ang)])
        nr = len(ys); cf = []
        for jj in range(nr - 1):                                                                               # rows run downwards here: wind each quad so it faces out
            for i in range(sides):
                a0_, c0, a1, c1 = jj * sides + i, jj * sides + (i + 1) % sides, (jj + 1) * sides + i, (jj + 1) * sides + (i + 1) % sides; cf.append([a0_, a1, c1, c0])
        rest = (cv - T) @ R                                                                                    # inverse of the bone's rigid transform (R is orthonormal)
        self._add("hijab_cape", rest, cf, colour, "cloth", bone="spine_03", pattern=folds(11, 0.10, sh - 0.03 * k, drape_to))

    def head_tie(self, colour, pattern=None):
        """A head-tie: a turban of cloth wound round the head above the brow, wider than the head and closed on top."""
        k = self.k; c = self.head_c; y0 = self.eye_y + 0.046 * k; hw, hd = self.head_hw, self.head_hd
        v, f = lathe([(y0, 0, c[2] - 0.004, hw + 0.006, hd + 0.008), (y0 + 0.035 * k, 0, c[2] - 0.008, hw + 0.022, hd + 0.024), (self.top - 0.005 * k, 0, c[2] - 0.016, hw + 0.030, hd + 0.030),
                      (self.top + 0.035 * k, 0, c[2] - 0.024, hw + 0.012, hd + 0.014), (self.top + 0.058 * k, 0, c[2] - 0.028, hw * 0.62, hd * 0.62)], sides=24)
        self._add("head_tie", v, f, colour, "cloth", bone="head", pattern=pattern); return self.top + 0.058 * k

    def basin(self, y, colour=(0.93, 0.93, 0.90), rim=(0.16, 0.30, 0.62), goods=(0.80, 0.22, 0.10)):
        """An enamel basin carried on the head, heaped with produce."""
        k = self.k; c = self.head_c; z = c[2] - 0.02
        v, f = lathe([(y, 0, z, 0.10 * k, 0.10 * k), (y + 0.025 * k, 0, z, 0.165 * k, 0.165 * k), (y + 0.105 * k, 0, z, 0.235 * k, 0.235 * k), (y + 0.112 * k, 0, z, 0.245 * k, 0.245 * k),
                      (y + 0.118 * k, 0, z, 0.215 * k, 0.215 * k), (y + 0.150 * k, 0, z, 0.15 * k, 0.15 * k), (y + 0.170 * k, 0, z, 0.06 * k, 0.06 * k)], sides=24, cap_bottom=True)
        y_rim = y + 0.105 * k; rim = np.array(rim); goods = np.array(goods)
        def pat(p, c0):
            out = c0.copy(); out[(p[:, 1] > y_rim - 0.004) & (p[:, 1] < y_rim + 0.012 * k)] = rim; top = p[:, 1] >= y_rim + 0.012 * k
            cell = np.floor(p[:, 0] / 0.045) * 7.0 + np.floor(p[:, 2] / 0.045) * 13.0; shade = 0.78 + 0.3 * (np.sin(cell * 12.9898) * 0.5 + 0.5)
            out[top] = (goods[None, :] * shade[:, None])[top]; return out
        self._add("basin", v, f, colour, "cloth", bone="head", pattern=pat)

    def bag(self, colour):
        """A school bag on the back."""
        h = self.h; k = self.k; v = h.verts; sh = self.shoulder_y; torso = _region(["spine", "clavicle"]) & h.mask("body"); slab = v[torso & (np.abs(v[:, 1] - (sh - 0.16 * k)) < 0.05 * k)]
        zb = float(slab[:, 2].min()) - 0.045 * k; y0, y1 = sh - 0.36 * k, sh - 0.02 * k
        bv, bf = lathe([(y0, 0, zb, 0.105 * k, 0.045 * k), (y0 + 0.05 * k, 0, zb, 0.135 * k, 0.066 * k), (y1 - 0.06 * k, 0, zb, 0.135 * k, 0.066 * k), (y1, 0, zb, 0.10 * k, 0.045 * k)], sides=14, cap_bottom=True)
        self._add("bag", bv, bf, colour, "cloth", bone="spine_03")


# ---------------------------------------------------------------------------------------------- cloth patterns
# A pattern gives the colour of points of a part (rest positions in metres, base colour rows) so the texture bake can evaluate it at every texel: prints and thread work are sharper than the mesh.
def folds(count, depth, top, hem):
    """Soft vertical folds: the cloth darkens in its valleys, more towards the hem."""
    def f(p, c):
        ang = np.arctan2(p[:, 0], p[:, 2]); t = np.clip((top - p[:, 1]) / max(top - hem, 1e-6), 0, 1); s = 0.5 + 0.5 * np.sin(ang * count + 1.7 * np.sin(ang * 3.0))
        return c * (1 - depth * t * s)[:, None]
    return f


def ankara(c2, c3, cell=0.085, radius=0.17, fold=None):
    """A wax print: staggered rows of rings (c2) with a dot (c3) in each, over the base colour."""
    c2 = np.array(c2); c3 = np.array(c3)
    def f(p, c):
        v = p[:, 1] / cell; row = np.floor(v); u = np.arctan2(p[:, 0], p[:, 2]) * radius / cell + 0.5 * (row % 2); du = u - np.floor(u) - 0.5; dv = v - row - 0.5; r = np.hypot(du, dv)
        out = c.copy(); out[(r < 0.42) & (r > 0.28)] = c2; out[r < 0.16] = c3; out[(np.abs(du) > 0.46) & (np.abs(dv) < 0.12)] = c3
        return fold(p, out) if fold else out
    return f


def embroidery(colour, centre, half_w, depth):
    """Thread work on the chest of a gown, the Hausa way: a panel under the neckline, pointed at the bottom, with a solid border, a spine down the middle and chevrons either side
    (lines 6 to 8 mm wide, so they still read in a 256 px tile)."""
    colour = np.array(colour)
    def f(p, c):
        dx = (p[:, 0] - centre[0]) / half_w; dy = (centre[1] - p[:, 1]) / depth; q = np.abs(dx) + np.clip(dy, 0, 1) * 0.55
        inside = (p[:, 2] > centre[2]) & (dy > -0.15) & (dy < 1) & (q < 1.0)
        border = q > 0.86; spine = np.abs(dx) < 0.07; chev = (np.abs(((np.abs(dx) * half_w + (centre[1] - p[:, 1])) / 0.034) % 1.0 - 0.5) < 0.13) & (np.abs(dx) > 0.16) & (q < 0.74)
        out = c.copy(); out[inside & (border | spine | chev)] = colour; return out
    return f


def lattice(colour, y0, y1, period=0.028):
    """A woven band round a cap between two heights: a diamond lattice."""
    colour = np.array(colour)
    def f(p, c):
        ang = np.arctan2(p[:, 0], p[:, 2]) * 0.09 / period; t = (p[:, 1] - y0) / period
        m = (p[:, 1] > y0) & (p[:, 1] < y1) & ((np.abs(((ang + t) % 1.0) - 0.5) < 0.16) | (np.abs(((ang - t) % 1.0) - 0.5) < 0.16))
        out = c.copy(); out[m] = colour; return out
    return f


HANDS = ["hand", "thumb", "index", "middle", "ring", "pinky"]


# ---------------------------------------------------------------------------------------------- designs
# The ten people of the game's crowd (the same ids as ARO.NetCore.PeopleKit.Designs, so the game can swap the figures one for one). Each takes a colourway (garment colours) and a skin tone.
def man_shirt_trousers(seed=1, skin=1, c=((0.30, 0.48, 0.70), (0.13, 0.15, 0.22), (0.07, 0.055, 0.05))):
    f = Figure(Human(gender=1.0, age=0.5, weight=0.5, height=0.46, seed=seed), skin)
    f.add_body(f.head | _region(HANDS + ["lowerarm", "upperarm"]), f.skin_colours()); f.add_eyes()
    f.garment("shirt", ["spine", "clavicle", "upperarm", "pelvis", "neck"], c[0], 0.020, hem=f.waist_y - 0.01 * f.k, neck=0.072 * f.k, sleeve=0.62, smoothing=3)      # tucked in
    f.garment("trousers", ["pelvis", "thigh", "calf", "spine_01"], c[1], 0.024, top=f.waist_y + 0.035 * f.k, leg_to=f.ankle_y + 0.012, smoothing=3, straight_leg=0.088)
    f.shoes(c[2]); return f


def man_kaftan(seed=2, skin=0, c=((0.90, 0.89, 0.86), (0.55, 0.12, 0.14), (0.22, 0.14, 0.09)), elder=False):
    f = Figure(Human(gender=1.0, age=0.82 if elder else 0.55, weight=0.56, height=0.45, seed=seed), skin, hair=GREY_HAIR if elder else HAIR)
    f.add_body(f.head | _region(HANDS), f.skin_colours(hair_painted=True, beard=GREY_HAIR if elder else None)); f.add_eyes()
    top = f.skirt("kaftan_skirt", c[0], hem=0.10 * f.k, flare=0.34, offset=0.034, pattern=folds(14, 0.07, f.hip_y, 0.1))
    n0 = f.h.joint("neck")
    f.garment("kaftan_top", ["spine", "clavicle", "upperarm", "lowerarm", "pelvis", "neck", "thigh"], c[0], 0.026, hem=top - 0.12, neck=0.070 * f.k, sleeve=1.9, loosen=0.006, smoothing=3,
              pattern=None if elder else embroidery(np.array(c[0]) * 0.72 + np.array(c[2]) * 0.1, (0.0, f.shoulder_y + 0.02 * f.k, n0[2]), 0.07 * f.k, 0.20 * f.k))
    y0 = f.eye_y + 0.062 * f.k; f.kufi(c[1], pattern=lattice(np.clip(np.array(c[1]) * 1.6 + 0.15, 0, 1), y0 + 0.004, y0 + 0.040 * f.k)); f.shoes(c[2]); return f


def man_babban_riga(seed=4, skin=1, c=((0.16, 0.28, 0.62), (0.92, 0.92, 0.90), (0.82, 0.66, 0.30))):
    """A babban riga: the wide gown over a kaftan and trousers, open sleeves to the wrists, thread work on the chest, an embroidered cap."""
    f = Figure(Human(gender=1.0, age=0.6, weight=0.62, height=0.47, seed=seed), skin)
    f.add_body(f.head | _region(HANDS), f.skin_colours(hair_painted=True)); f.add_eyes()
    f.garment("trousers", ["calf"], c[1], 0.020, leg_to=f.ankle_y + 0.012, smoothing=2, straight_leg=0.075)
    top = f.skirt("riga_skirt", c[0], hem=f.knee_y - 0.22 * f.k, flare=0.48, offset=0.05, pattern=folds(10, 0.16, f.hip_y, f.knee_y - 0.22 * f.k))
    n0 = f.h.joint("neck")
    f.garment("riga_top", ["spine", "clavicle", "upperarm", "lowerarm", "pelvis", "neck", "thigh"], c[0], 0.040, hem=top - 0.14, neck=0.074 * f.k, sleeve=1.85, loosen=0.03, smoothing=4,
              sleeve_flare=0.9, pattern=embroidery(c[2], (0.0, f.shoulder_y + 0.02 * f.k, n0[2]), 0.11 * f.k, 0.34 * f.k))
    y0 = f.eye_y + 0.062 * f.k; f.kufi(c[2], pattern=lattice(c[0], y0 + 0.004, f.top)); f.shoes((0.20, 0.13, 0.08)); return f


def woman_wrapper(seed=6, skin=2, c=((0.12, 0.45, 0.25), (0.86, 0.66, 0.18), (0.08, 0.08, 0.08)), head_load=False):
    """A blouse and a wax-print wrapper to the ankles: with a hijab, or (a market woman) a head-tie and an enamel basin on the head."""
    f = Figure(Human(gender=0.0, age=0.55 if head_load else 0.45, weight=0.62 if head_load else 0.52, height=0.46, seed=seed), skin)
    f.add_body(f.head | _region(HANDS + (["lowerarm"] if head_load else [])), f.skin_colours(hair_painted=False)); f.add_eyes()
    print_ = ankara(c[1], c[2], fold=folds(9, 0.10, f.hip_y, 0.07))
    top = f.skirt("wrapper", c[0], hem=0.07 * f.k, flare=0.12, offset=0.030, pattern=print_)
    f.garment("blouse", ["spine", "clavicle", "upperarm", "lowerarm", "pelvis", "neck"], c[0], 0.024, hem=top - 0.13, neck=0.066 * f.k, sleeve=1.25 if head_load else 1.9, loosen=0.008, smoothing=3,
              sleeve_flare=0.25, pattern=ankara(c[1], c[2]))
    if head_load: y = f.head_tie(c[1], pattern=ankara(c[0], c[2], cell=0.05, radius=0.11)); f.basin(y - 0.01)
    else: f.hijab(c[1] if sum(c[1]) < 2.4 else np.array(c[1]) * 0.9, drape_to=f.shoulder_y - 0.28 * f.k)
    f.shoes((0.10, 0.07, 0.06)); return f


def woman_long(seed=3, skin=1, c=((0.10, 0.36, 0.38), (0.86, 0.80, 0.68), (0.06, 0.06, 0.06)), abaya=False, age=0.5, height=0.47):
    """A long dress (or a black abaya) with a long hijab over the shoulders and the chest."""
    f = Figure(Human(gender=0.0, age=age, weight=0.5, height=height, seed=seed), skin)
    f.add_body(f.head | _region(HANDS), f.skin_colours(hair_painted=False)); f.add_eyes()
    top = f.skirt("dress_skirt", c[0], hem=0.05 * f.k, flare=0.36 if abaya else 0.26, offset=0.034, pattern=folds(12, 0.12 if abaya else 0.08, f.hip_y, 0.05))
    f.garment("dress_top", ["spine", "clavicle", "upperarm", "lowerarm", "pelvis", "thigh"], c[0], 0.024, hem=top - 0.12, neck=0.06 * f.k, sleeve=1.92, smoothing=3, sleeve_flare=0.45 if abaya else 0.1)
    f.hijab(c[1], drape_to=f.shoulder_y - (0.40 if abaya else 0.32) * f.k); f.shoes(c[2]); return f


def boy_school(seed=8, skin=0, c=((0.93, 0.93, 0.91), (0.12, 0.16, 0.34), (0.30, 0.18, 0.10))):
    f = Figure(Human(gender=1.0, age=0.19, weight=0.45, height=0.5, seed=seed, face_strength=0.3), skin)
    f.add_body(f.head | _region(HANDS + ["lowerarm", "upperarm", "calf"]) | (f.h.verts[:, 1] < f.knee_y + 0.03), f.skin_colours()); f.add_eyes()
    f.garment("shirt", ["spine", "clavicle", "upperarm", "pelvis", "neck"], c[0], 0.016, hem=f.waist_y - 0.01 * f.k, neck=0.066 * f.k, sleeve=0.6, smoothing=3)
    f.garment("shorts", ["pelvis", "thigh", "spine_01"], c[1], 0.020, top=f.waist_y + 0.03 * f.k, leg_to=f.knee_y + 0.04, smoothing=3, straight_leg=0.07)
    f.bag(c[2]); f.shoes((0.05, 0.05, 0.05)); return f


DESIGNS = {
    "man_kaftan_cap": man_kaftan,
    "man_babban_riga": man_babban_riga,
    "man_shirt_trousers": man_shirt_trousers,
    "man_kaftan_plain": lambda seed=5, skin=1, c=((0.55, 0.57, 0.58), (0.92, 0.92, 0.90), (0.22, 0.14, 0.09)): man_kaftan(seed, skin, c, elder=True),
    "woman_hijab_wrapper": woman_wrapper,
    "woman_hijab_long": woman_long,
    "woman_abaya": lambda seed=7, skin=0, c=((0.05, 0.05, 0.06), (0.06, 0.06, 0.07), (0.05, 0.05, 0.05)): woman_long(seed, skin, c, abaya=True),
    "woman_market_headload": lambda seed=9, skin=0, c=((0.80, 0.36, 0.10), (0.86, 0.66, 0.18), (0.10, 0.20, 0.45)): woman_wrapper(seed, skin, c, head_load=True),
    "boy_school": boy_school,
    "girl_school_hijab": lambda seed=10, skin=2, c=((0.12, 0.18, 0.36), (0.94, 0.94, 0.93), (0.06, 0.06, 0.06)): woman_long(seed, skin, c, age=0.19, height=0.5),
}


def posed(fig: Figure, pose):
    """The figure's parts with their vertices moved to a pose (see rig.standing and rig.walking)."""
    from .rig import Rig
    rig = Rig(fig.h); tr = rig.transforms(*pose)
    return [dict(p, verts=rig.skin(p["verts"], ids=p["ids"], bone=p["bone"], pose=tr)) for p in fig.parts]
