"""Geometry helpers on bmesh. A Part collects faces (with named kit materials) and becomes ONE mesh object with one material slot per kit material. Primitives are built as separate
shells (not welded to each other) so winding can be checked per shell; rounded things (tubes, blobs) share their vertices so they shade smooth."""
from __future__ import annotations
import math, random
import bpy, bmesh
from mathutils import Vector, Matrix
from aro_blender import materials as AM


# UV scale (texture repeats per metre) per kit material that carries an image: a 2 m tile is 0.5, a 4 m tile 0.25
DEFAULT_UV = {"plaster_cream_light": 0.5, "stone_travertine": 1.0, "cladding_grey": 1.0, "roof_tile_charcoal": 0.5, "concrete": 0.5, "bark": 1.0, "asphalt": 0.25, "paver_mix": 0.5, "grass_green": 0.2,
              "mud_wet": 0.4, "mud_crest": 0.4, "earth_compacted": 0.4, "sand_dusty": 0.3, "asphalt_cracked": 0.25, "roof_zinc_rust": 0.5, "mud_brick": 0.5, "plaster_weathered": 0.4, "timber_weathered": 0.8, "water_brown": 0.3, "water_rain": 0.3}


def V(p): return Vector(p)


class Part:
    def __init__(self, name: str = "body"):
        self.name = name; self.bm = bmesh.new(); self.keys: list[str] = []
        self.uv_scale = dict(DEFAULT_UV)

    # ---- materials / faces
    def mi(self, key: str) -> int:
        if key not in self.keys: self.keys.append(key)
        return self.keys.index(key)

    def face(self, pts, key: str, smooth: bool = False):
        vs = [self.bm.verts.new(V(p)) for p in pts]
        try: f = self.bm.faces.new(vs)
        except ValueError: return None
        f.material_index = self.mi(key); f.smooth = smooth; return f

    def face_n(self, pts, key: str, normal, smooth: bool = False):
        """A planar face oriented to point along `normal` whatever the order of the points."""
        vs = [V(p) for p in pts]; n = Vector((0, 0, 0))
        for i in range(len(vs)): a, b = vs[i], vs[(i + 1) % len(vs)]; n += Vector(((a.y - b.y) * (a.z + b.z), (a.z - b.z) * (a.x + b.x), (a.x - b.x) * (a.y + b.y)))
        return self.face(vs if n.dot(V(normal)) >= 0 else list(reversed(vs)), key, smooth)

    def card(self, c, right, up, w, h, key, uv0=(0, 0), uv1=(1, 1)):
        """A flat quad centred on c (foliage card) with its own UVs (kept by uv_world); its normal is right x up."""
        c, r, u = V(c), V(right).normalized() * (w / 2), V(up).normalized() * (h / 2); pts = [c - r - u, c + r - u, c + r + u, c - r + u]; n = r.cross(u)
        f = self.face_n(pts, key, n)
        if f is None: return None
        uvl = self.bm.loops.layers.uv.verify(); order = [(uv0[0], uv0[1]), (uv1[0], uv0[1]), (uv1[0], uv1[1]), (uv0[0], uv1[1])]
        cs = [p_ for p_ in pts]; vs = [l.vert.co.copy() for l in f.loops]
        for l in f.loops:
            k = min(range(4), key=lambda i: (cs[i] - l.vert.co).length); l[uvl].uv = order[k]
        if not hasattr(self, "_fixed"): self._fixed = set()
        self._fixed.add(f); return f

    def mark(self) -> int: return len(self.bm.verts)

    def xform(self, since: int, m: Matrix):
        self.bm.verts.ensure_lookup_table()
        for v in list(self.bm.verts)[since:]: v.co = m @ v.co

    # ---- primitives
    def box(self, x0, y0, z0, x1, y1, z1, key, skip: tuple = ()):
        """Axis-aligned box from min to max corners. skip: any of 'bottom','top','front','back','left','right' (front = -Y)."""
        if x1 < x0: x0, x1 = x1, x0
        if y1 < y0: y0, y1 = y1, y0
        if z1 < z0: z0, z1 = z1, z0
        F = {
            "bottom": [(x0, y0, z0), (x0, y1, z0), (x1, y1, z0), (x1, y0, z0)],
            "top": [(x0, y0, z1), (x1, y0, z1), (x1, y1, z1), (x0, y1, z1)],
            "front": [(x0, y0, z0), (x1, y0, z0), (x1, y0, z1), (x0, y0, z1)],
            "back": [(x1, y1, z0), (x0, y1, z0), (x0, y1, z1), (x1, y1, z1)],
            "left": [(x0, y1, z0), (x0, y0, z0), (x0, y0, z1), (x0, y1, z1)],
            "right": [(x1, y0, z0), (x1, y1, z0), (x1, y1, z1), (x1, y0, z1)],
        }
        for n, pts in F.items():
            if n not in skip: self.face(pts, key)

    def frustum(self, x0, y0, z0, x1, y1, z1, key, inset: float, skip: tuple = ()):
        """A box whose top face is inset on all sides (chamfered / tapered), e.g. a kerb or a pier cap."""
        a = [(x0, y0, z0), (x1, y0, z0), (x1, y1, z0), (x0, y1, z0)]; b = [(x0 + inset, y0 + inset, z1), (x1 - inset, y0 + inset, z1), (x1 - inset, y1 - inset, z1), (x0 + inset, y1 - inset, z1)]
        if "bottom" not in skip: self.face(list(reversed(a)), key)
        if "top" not in skip: self.face(b, key)
        for i in range(4):
            j = (i + 1) % 4; self.face([a[i], a[j], b[j], b[i]], key)

    def prism(self, poly, z0, z1, key, smooth=False, caps=True):
        """Extrusion of a simple polygon (x, y list, counter-clockwise seen from +Z)."""
        n = len(poly)
        if caps:
            self.face([(x, y, z0) for x, y in reversed(poly)], key)
            self.face([(x, y, z1) for x, y in poly], key)
        for i in range(n):
            j = (i + 1) % n; (xa, ya), (xb, yb) = poly[i], poly[j]
            self.face([(xa, ya, z0), (xb, yb, z0), (xb, yb, z1), (xa, ya, z1)], key, smooth)

    def ring_tube(self, rings, key, caps=(True, True), smooth=True):
        """A tube through a list of rings (each a list of Vectors, same count, counter-clockwise looking along the path). Vertices are shared so it shades smooth."""
        bm = self.bm; vr = [[bm.verts.new(p) for p in ring] for ring in rings]; n = len(vr[0]); mi = self.mi(key)
        for r in range(len(vr) - 1):
            for i in range(n):
                j = (i + 1) % n
                try: f = bm.faces.new([vr[r][i], vr[r][j], vr[r + 1][j], vr[r + 1][i]]); f.material_index = mi; f.smooth = smooth
                except ValueError: pass
        if caps[0]:
            try: f = bm.faces.new(list(reversed(vr[0]))); f.material_index = mi
            except ValueError: pass
        if caps[1]:
            try: f = bm.faces.new(vr[-1]); f.material_index = mi
            except ValueError: pass

    def tube(self, p0, p1, r0, r1, seg, key, caps=(True, True), smooth=True, roll=0.0):
        """A (tapered) tube from p0 to p1; seg=4 gives a square bar."""
        a, b = V(p0), V(p1); d = (b - a)
        if d.length < 1e-9: return
        d.normalize(); up = Vector((0, 0, 1)) if abs(d.z) < 0.95 else Vector((1, 0, 0)); u = d.cross(up).normalized(); w = d.cross(u).normalized()
        def ring(c, r): return [c + (u * math.cos(2 * math.pi * k / seg + roll) + w * math.sin(2 * math.pi * k / seg + roll)) * r for k in range(seg)]
        self.ring_tube([ring(a, r0), ring(b, r1)], key, caps, smooth)      # (u, w, d) is right-handed, so the rings run counter-clockwise about +d and the side faces point outward

    def path_tube(self, pts, radii, seg, key, caps=(True, True), smooth=True):
        """A tube along a polyline with a radius per point (parallel-transport frames): wires, branches, trunks, curved arms."""
        pts = [V(p) for p in pts]; rings = []; prev_u = None
        for i, c in enumerate(pts):
            t = (pts[min(i + 1, len(pts) - 1)] - pts[max(i - 1, 0)]).normalized()
            if prev_u is None:
                up = Vector((0, 0, 1)) if abs(t.z) < 0.95 else Vector((1, 0, 0)); u = t.cross(up).normalized()
            else:
                u = (prev_u - t * prev_u.dot(t)).normalized()
            w = t.cross(u).normalized(); prev_u = u; r = radii[i] if isinstance(radii, (list, tuple)) else radii
            rings.append([c + (u * math.cos(2 * math.pi * k / seg) + w * math.sin(2 * math.pi * k / seg)) * r for k in range(seg)])
        self.ring_tube(rings, key, caps, smooth)

    def blob(self, center, radii, key, subdiv=1, jitter=0.0, seed=0, faces_keys=None, smooth=False):
        """An icosphere scaled by radii with optional vertex jitter; faces_keys: callable(face_centre_local Vector)->kit key, for two-tone foliage."""
        rnd = random.Random(seed); bm2 = bmesh.new(); bmesh.ops.create_icosphere(bm2, subdivisions=subdiv, radius=1.0)
        for v in bm2.verts:
            if jitter: v.co *= 1.0 + rnd.uniform(-jitter, jitter)
            v.co = Vector((v.co.x * radii[0], v.co.y * radii[1], v.co.z * radii[2])) + V(center)
        vm = {v: self.bm.verts.new(v.co) for v in bm2.verts}
        for f in bm2.faces:
            nf = self.bm.faces.new([vm[v] for v in f.verts]); c = f.calc_center_median() - V(center)
            nf.material_index = self.mi(faces_keys(c) if faces_keys else key); nf.smooth = smooth
        bm2.free()

    def uv_world(self, scale: float = 0.25):
        """Box-projected UVs in world metres * scale (so a tiling texture can be sized in metres). `self.uv_scale` maps a kit key to its own scale (a paver tile of 2 m is 0.5)."""
        bm = self.bm; uvl = bm.loops.layers.uv.verify(); per = getattr(self, "uv_scale", {})
        fixed = getattr(self, "_fixed", set())
        for f in bm.faces:
            if f in fixed: continue
            n = f.normal; ax = max(range(3), key=lambda i: abs(n[i])); sc = per.get(self.keys[f.material_index], scale)
            for l in f.loops:
                p = l.vert.co; u, v = ((p.y, p.z) if ax == 0 else (p.x, p.z) if ax == 1 else (p.x, p.y))
                l[uvl].uv = (u * sc, v * sc)

    def tri_count(self) -> int:
        return sum(len(f.verts) - 2 for f in self.bm.faces)

    def to_object(self, name: str, parent=None, collection=None):
        bm = self.bm; bm.normal_update(); self.uv_world()
        mesh = bpy.data.meshes.new(name); bm.to_mesh(mesh); mesh.update()
        for k in self.keys: mesh.materials.append(AM.get(k, bpy))
        o = bpy.data.objects.new(name, mesh); (collection or bpy.context.scene.collection).objects.link(o)
        if parent is not None: o.parent = parent
        bm.free(); return o


def shell_volumes(obj) -> list[float]:
    """Signed volume of every shell (connected component) of a mesh: a negative or zero value for a closed shell means inside-out winding. Open sheets are skipped."""
    bm = bmesh.new(); bm.from_mesh(obj.data); bm.faces.ensure_lookup_table(); seen = set(); out = []
    for f0 in bm.faces:
        if f0.index in seen: continue
        comp = []; stack = [f0]; seen.add(f0.index); closed = True
        while stack:
            f = stack.pop(); comp.append(f)
            for e in f.edges:
                if len(e.link_faces) != 2: closed = False
                for g in e.link_faces:
                    if g.index not in seen: seen.add(g.index); stack.append(g)
        if not closed: continue
        vol = 0.0
        for f in comp:
            vs = f.verts
            for i in range(1, len(vs) - 1):
                a, b, c = vs[0].co, vs[i].co, vs[i + 1].co; vol += a.dot(b.cross(c)) / 6.0
        out.append(vol)
    bm.free(); return out


def reset_scene():
    bpy.ops.wm.read_factory_settings(use_empty=True)


class Asset:
    """Root empty named after the asset id, LOD empties {id}_lodN, one mesh per LOD, optional col_<id> box."""
    def __init__(self, asset_id: str, kind: str):
        self.id = asset_id; self.kind = kind; reset_scene()
        self.root = bpy.data.objects.new(asset_id, None); bpy.context.scene.collection.objects.link(self.root); self.lods: dict[int, object] = {}
        self.tris: dict[int, int] = {}

    def lod(self, level: int, part: Part):
        e = bpy.data.objects.new(f"{self.id}_lod{level}", None); bpy.context.scene.collection.objects.link(e); e.parent = self.root
        self.tris[level] = part.tri_count(); o = part.to_object(f"{self.id}_lod{level}_{part.name}", parent=e); self.lods[level] = o; return o

    def collision(self, x0, y0, z0, x1, y1, z1):
        p = Part("col"); p.box(x0, y0, z0, x1, y1, z1, "concrete_dark"); bmesh.ops.remove_doubles(p.bm, verts=p.bm.verts, dist=1e-5); name = f"col_{self.id}"
        mesh = bpy.data.meshes.new(name); p.bm.to_mesh(mesh); p.bm.free(); o = bpy.data.objects.new(name, mesh); bpy.context.scene.collection.objects.link(o); o.parent = self.root
        o.display_type = "WIRE"; return o
