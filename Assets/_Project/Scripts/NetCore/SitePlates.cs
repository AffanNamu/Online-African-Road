using System;
using System.Collections.Generic;
using System.Linq;

namespace ARO.NetCore
{
    // ====================================================================================================================================================================================
    // E2 site plates (docs/ROAD_ENGINE_E2.md): a flat paved rectangle beside or at the end of a road, for a logistics yard, a bus terminal, a fuel forecourt or a market square. A plate is DATA
    // (position along a corridor, size, yaw, surface, kerb, entrances, pavement marks, props in plate coordinates). The terrain is flattened under it and blended round it, the mesh is built per
    // chunk like a road and has a collider, nothing is drawn by hand. A route without `sites` is untouched (the golden master proves it).
    // ====================================================================================================================================================================================

    /// <summary>A gap in the kerb ring. `edge`: front (+v, the way the plate faces), back (-v), left (-u) or right (+u). from and to are metres from the MIDDLE of that edge.</summary>
    [Serializable] public class SiteEntranceSpec { public string edge = "front"; public float from = -6f, to = 6f; }

    /// <summary>
    /// A pavement mark in plate coordinates (u across to the right, v along the way the plate faces; metres from the plate centre). Kinds: line (a painted bar of `length` x `width`), bay_row (`count` bays of
    /// `width` x `length`, separator lines and an optional back line), hatch (diagonal stripes filling a `width` x `length` rectangle: keep-clear areas), arrow_straight / arrow_left / arrow_right (the tail is at x, z).
    /// angleDeg turns the mark about its anchor (0 = along +v).
    /// </summary>
    [Serializable]
    public class SiteMarkSpec
    {
        public string kind;
        public float x, z, angleDeg, length, width;
        public int count = 1;
        public bool closed, yellow;
    }

    /// <summary>A prop placed on the plate in plate coordinates, standing on its surface.</summary>
    [Serializable] public class SitePropSpec { public string id, prop, text; public float x, z, yawDeg, scale = 1f; }

    [Serializable]
    public class SitePlateSpec
    {
        public string id, name;
        public string corridor = "";            // "" = the main road; otherwise a junction id
        public float s;                         // station along that corridor of the point the plate is placed from
        public float lateral;                   // from that point, to the right of the corridor (negative = left), metres
        public float forward;                   // from that point, along the corridor heading, metres (a plate at the END of a stub: s = the stub length, forward = half the depth)
        public float widthM = 40f, depthM = 40f;
        public float yawDeg;                    // turns the plate to the right of the corridor heading
        public string surface = "asphalt";      // asphalt | concrete | paving | gravel
        public string kerb = "plain";           // none | plain | black_white
        public float kerbHeight = 0.12f;
        public float heightOffset;              // plate surface above (+) or below (-) the paved edge of the corridor at s, metres
        public bool dropRoadKerb = true;        // when the plate stands beside a kerbed road: the road's kerb drops along the plate (a driveway), the edge line is interrupted
        public SiteEntranceSpec[] entrances = new SiteEntranceSpec[0];
        public SiteMarkSpec[] marks = new SiteMarkSpec[0];
        public SitePropSpec[] props = new SitePropSpec[0];
    }

    /// <summary>A site plate placed in the world.</summary>
    public sealed class SitePlate
    {
        public const float KerbInset = RoadCrossSection.KerbFace + RoadCrossSection.KerbTopWidth;     // 0.22 m
        public const float CellMetres = 6f, KerbPiece = 3f;

        public SitePlateSpec Spec; public string Id => Spec.id;
        public float Cx, Cz, BaseY, FwdX, FwdZ, RightX, RightZ, HalfW, HalfD, KerbHeight;
        public int TopSub, KerbSub;                                  // road submesh slots; KerbSub = -1 when there is no kerb
        public float HeadingDeg => (float)(Math.Atan2(FwdX, FwdZ) * 180.0 / Math.PI);

        public static int SurfaceSub(string surface) => surface == "concrete" ? RoadSub.Concrete : surface == "paving" ? RoadSub.Sidewalk : surface == "gravel" ? RoadSub.Shoulder : RoadSub.Asphalt;

        public V3 ToWorld(float u, float v, float lift = 0f) => new V3(Cx + RightX * u + FwdX * v, BaseY + lift, Cz + RightZ * u + FwdZ * v);
        public void ToLocal(float x, float z, out float u, out float v) { float dx = x - Cx, dz = z - Cz; u = dx * RightX + dz * RightZ; v = dx * FwdX + dz * FwdZ; }

        /// <summary>Distance to the plate's rectangle: negative inside (the depth of the point under the edge), positive outside.</summary>
        public float SignedDistance(float x, float z)
        {
            ToLocal(x, z, out float u, out float v);
            float dx = Math.Abs(u) - HalfW, dz = Math.Abs(v) - HalfD;
            if (dx <= 0f && dz <= 0f) return Math.Max(dx, dz);
            float ox = Math.Max(dx, 0f), oz = Math.Max(dz, 0f); return (float)Math.Sqrt(ox * ox + oz * oz);
        }
        public bool Contains(float x, float z, float margin = 0f) => SignedDistance(x, z) <= margin;
        public bool HasKerb => KerbSub >= 0;

        public bool InEntrance(string edge, float t)
        {
            foreach (var e in Spec.entrances ?? new SiteEntranceSpec[0]) if (e.edge == edge && t >= e.from - 1e-4f && t <= e.to + 1e-4f) return true;
            return false;
        }

        /// <summary>Plates of a route. The base height follows the corridor's paved edge at s (plus the offset): a plate beside a road is level with the road's edge.</summary>
        public static SitePlate Place(RouteModel m, SitePlateSpec s)
        {
            var c = string.IsNullOrEmpty(s.corridor) || s.corridor == m.Spec.routeId ? m.Main : m.CorridorById(s.corridor);
            if (c == null) throw new ArgumentException("site '" + s.id + "': unknown corridor '" + s.corridor + "'");
            float ss = Mathx.Clamp(s.s, 0f, c.Length);
            c.Spline.FrameAt(ss, out var pos, out var fwd, out var right);
            var sec = c.SectionAt(ss); float k = c.ScaleAt(ss);
            float cx = pos.X + right.X * s.lateral + fwd.X * s.forward, cz = pos.Z + right.Z * s.lateral + fwd.Z * s.forward;
            float edge = Math.Min(Math.Abs(s.lateral), sec.PavedHalfWidth * k) * (s.lateral < 0f ? -1f : 1f);
            float baseY = pos.Y + sec.HeightAt(edge / k) + s.heightOffset;      // pos.Y is the centre-line elevation
            double th = Math.PI * s.yawDeg / 180.0; float ct = (float)Math.Cos(th), st = (float)Math.Sin(th);
            // the plate heading: the corridor heading turned towards the right by yawDeg
            float fx = fwd.X * ct + right.X * st, fz = fwd.Z * ct + right.Z * st, fl = (float)Math.Sqrt(fx * fx + fz * fz); fx /= fl; fz /= fl;
            float rx = fz, rz = -fx;                                               // x east, z north: right of (fx, fz) is (fz, -fx)
            int kerb = s.kerb == "black_white" ? RoadSub.KerbPainted : s.kerb == "plain" ? RoadSub.Kerb : -1;
            return new SitePlate { Spec = s, Cx = cx, Cz = cz, BaseY = baseY, FwdX = fx, FwdZ = fz, RightX = rx, RightZ = rz, HalfW = s.widthM * 0.5f, HalfD = s.depthM * 0.5f,
                KerbHeight = kerb >= 0 ? s.kerbHeight : 0f, TopSub = SurfaceSub(s.surface), KerbSub = kerb };
        }

        /// <summary>Instances of the plate's own props, in world coordinates, for the chunk box.</summary>
        public IEnumerable<PropInstance> Props(float minX, float minZ, float maxX, float maxZ)
        {
            foreach (var p in Spec.props ?? new SitePropSpec[0])
            {
                var w = ToWorld(p.x, p.z);
                if (w.X < minX || w.X >= maxX || w.Z < minZ || w.Z >= maxZ) continue;
                yield return new PropInstance { Prop = p.prop, Origin = Spec.id + "/" + p.id, Text = p.text, Pos = w, YawDeg = HeadingDeg + p.yawDeg, Scale = p.scale > 0f ? p.scale : 1f };
            }
        }
    }

    /// <summary>The mesh of a site plate for one chunk. Cells and kerb pieces belong to the chunk that holds their centre, so a plate that crosses chunk borders is built once, without gaps.</summary>
    public static class SiteGeometry
    {
        public const float MarkLift = 0.03f, LineWidth = 0.12f;

        public static MeshBuffers BuildChunk(SitePlate p, float minX, float minZ, float maxX, float maxZ, V3 origin)
        {
            var mb = new MeshBuffers(RoadSub.CountE1);
            bool Own(V3 w) => w.X >= minX && w.X < maxX && w.Z >= minZ && w.Z < maxZ;
            V3 Rel(V3 w) => new V3(w.X - origin.X, w.Y - origin.Y, w.Z - origin.Z);

            // surface: cells of up to CellMetres, flat
            int nu = Math.Max(1, (int)Math.Ceiling(2f * p.HalfW / SitePlate.CellMetres)), nv = Math.Max(1, (int)Math.Ceiling(2f * p.HalfD / SitePlate.CellMetres));
            for (int i = 0; i < nu; i++)
                for (int j = 0; j < nv; j++)
                {
                    float u0 = -p.HalfW + 2f * p.HalfW * i / nu, u1 = -p.HalfW + 2f * p.HalfW * (i + 1) / nu, v0 = -p.HalfD + 2f * p.HalfD * j / nv, v1 = -p.HalfD + 2f * p.HalfD * (j + 1) / nv;
                    if (!Own(p.ToWorld((u0 + u1) * 0.5f, (v0 + v1) * 0.5f))) continue;
                    RoadGeometry.FillConvex(mb, p.TopSub, new[] { Rel(p.ToWorld(u0, v0)), Rel(p.ToWorld(u0, v1)), Rel(p.ToWorld(u1, v1)), Rel(p.ToWorld(u1, v0)) });
                }

            if (p.HasKerb) AddKerb(mb, p, Own, Rel);
            foreach (var mk in p.Spec.marks ?? new SiteMarkSpec[0]) AddMark(mb, p, mk, Own, Rel);
            return mb;
        }

        static void AddKerb(MeshBuffers mb, SitePlate p, Func<V3, bool> own, Func<V3, V3> rel)
        {
            float h = p.KerbHeight, ins = SitePlate.KerbInset, top = RoadCrossSection.KerbTopWidth;
            // (edge name, fixed coordinate sign, runs along u (front/back) or v (left/right))
            foreach (var edge in new[] { "front", "back", "left", "right" })
            {
                bool alongU = edge == "front" || edge == "back";
                float half = alongU ? p.HalfW : p.HalfD, cross = alongU ? p.HalfD : p.HalfW;
                float sign = edge == "front" || edge == "right" ? 1f : -1f;
                float lo = -half + (alongU ? 0f : ins), hi = half - (alongU ? 0f : ins);          // front and back run the full width, left and right stop short so the corners are covered once
                int n = Math.Max(1, (int)Math.Ceiling((hi - lo) / SitePlate.KerbPiece));
                for (int k = 0; k < n; k++)
                {
                    float t0 = lo + (hi - lo) * k / n, t1 = lo + (hi - lo) * (k + 1) / n, tm = (t0 + t1) * 0.5f;
                    if (p.InEntrance(edge, tm)) continue;
                    V3 P(float t, float d, float y)        // d = distance in from the boundary
                    {
                        float c = sign * (cross - d);
                        return alongU ? p.ToWorld(t, c, y) : p.ToWorld(c, t, y);
                    }
                    if (!own(P(tm, 0f, 0f))) continue;
                    // slope face (foot -> kerb top start) then the kerb top (to the boundary)
                    RoadGeometry.FillConvex(mb, p.KerbSub, new[] { rel(P(t0, ins, 0f)), rel(P(t1, ins, 0f)), rel(P(t1, top, h)), rel(P(t0, top, h)) });
                    RoadGeometry.FillConvex(mb, p.KerbSub, new[] { rel(P(t0, top, h)), rel(P(t1, top, h)), rel(P(t1, 0f, h)), rel(P(t0, 0f, h)) });
                }
            }
        }

        // ------------------------------------------------------------------------------------------------------------------------------------------------ marks
        /// <summary>The convex polygons of a mark in plate coordinates (u, v). Pure geometry: unit-tested without a mesh.</summary>
        public static List<V2[]> MarkPieces(SiteMarkSpec m)
        {
            var res = new List<V2[]>();
            double a = Math.PI * m.angleDeg / 180.0; float ca = (float)Math.Cos(a), sa = (float)Math.Sin(a);
            // local mark frame: f = along the mark (angle 0 = +v), r = to its right (+u). World plate coordinates: u = x + r*ca + f*sa, v = z + f*ca - r*sa
            V2 W(float f, float r) => new V2(m.x + r * ca + f * sa, m.z + f * ca - r * sa);
            switch (m.kind)
            {
                case "line":
                {
                    float len = m.length > 0f ? m.length : 3f, w = m.width > 0f ? m.width : 0.15f;
                    res.Add(new[] { W(0f, -w * 0.5f), W(0f, w * 0.5f), W(len, w * 0.5f), W(len, -w * 0.5f) });
                    break;
                }
                case "bay_row":
                {
                    float bw = m.width > 0f ? m.width : 3.5f, bl = m.length > 0f ? m.length : 6f; int n = Math.Max(1, m.count);
                    for (int i = 0; i <= n; i++) { float r = i * bw; res.Add(new[] { W(0f, r - LineWidth * 0.5f), W(0f, r + LineWidth * 0.5f), W(bl, r + LineWidth * 0.5f), W(bl, r - LineWidth * 0.5f) }); }
                    if (m.closed) res.Add(new[] { W(bl - LineWidth, -LineWidth * 0.5f), W(bl - LineWidth, n * bw + LineWidth * 0.5f), W(bl, n * bw + LineWidth * 0.5f), W(bl, -LineWidth * 0.5f) });
                    break;
                }
                case "hatch":
                {
                    // the rectangle is centred on (x, z); stripes run at 45 degrees (f - r = c); each stripe is clipped to the rectangle. (r, f) is across / along the rectangle.
                    float w = m.width > 0f ? m.width : 6f, l = m.length > 0f ? m.length : 6f, pitch = 1.2f * 1.4142f, bar = 0.35f * 1.4142f * 0.5f;       // 1.2 m apart, 0.35 m thick, measured across the stripes (f - r = c is a 45 degree line)
                    float hw = w * 0.5f, hl = l * 0.5f, reach = hw + hl;
                    for (float c = -reach; c <= reach; c += pitch)
                    {
                        var band = new List<V2> { new V2(-reach, -reach + c - bar), new V2(reach, reach + c - bar), new V2(reach, reach + c + bar), new V2(-reach, -reach + c + bar) };
                        var clipped = ClipToRect(band, -hw, -hl, hw, hl);
                        if (clipped.Count >= 3 && Area(clipped) > 0.02f) res.Add(clipped.Select(q => W(q.Y, q.X)).ToArray());
                    }
                    break;
                }
                case "arrow_straight":
                case "arrow_left":
                case "arrow_right":
                {
                    float len = m.length > 0f ? m.length : RoadGeometry.ArrowLength;
                    foreach (var piece in RoadGeometry.ArrowPieces(m.kind, len)) res.Add(piece.Select(q => W(q.X, q.Y)).ToArray());
                    break;
                }
            }
            return res;
        }

        static List<V2> ClipToRect(List<V2> poly, float x0, float y0, float x1, float y1)
        {
            List<V2> Clip(List<V2> pts, Func<V2, bool> inside, Func<V2, V2, V2> cut)
            {
                var o = new List<V2>(); if (pts.Count == 0) return o;
                for (int i = 0; i < pts.Count; i++)
                {
                    var a = pts[i]; var b = pts[(i + 1) % pts.Count]; bool ia = inside(a), ib = inside(b);
                    if (ia) o.Add(a);
                    if (ia != ib) o.Add(cut(a, b));
                }
                return o;
            }
            V2 AtX(V2 a, V2 b, float x) { float t = (x - a.X) / (b.X - a.X); return new V2(x, a.Y + (b.Y - a.Y) * t); }
            V2 AtY(V2 a, V2 b, float y) { float t = (y - a.Y) / (b.Y - a.Y); return new V2(a.X + (b.X - a.X) * t, y); }
            var r = Clip(poly, q => q.X >= x0, (a, b) => AtX(a, b, x0));
            r = Clip(r, q => q.X <= x1, (a, b) => AtX(a, b, x1));
            r = Clip(r, q => q.Y >= y0, (a, b) => AtY(a, b, y0));
            r = Clip(r, q => q.Y <= y1, (a, b) => AtY(a, b, y1));
            return r;
        }

        static float Area(List<V2> p) { float a = 0f; for (int i = 0; i < p.Count; i++) { var q = p[i]; var n = p[(i + 1) % p.Count]; a += q.X * n.Y - n.X * q.Y; } return Math.Abs(a) * 0.5f; }

        static void AddMark(MeshBuffers mb, SitePlate p, SiteMarkSpec m, Func<V3, bool> own, Func<V3, V3> rel)
        {
            if (!own(p.ToWorld(m.x, m.z))) return;
            int sub = m.yellow ? RoadSub.MarkYellow : RoadSub.MarkWhite;
            foreach (var piece in MarkPieces(m)) RoadGeometry.FillConvex(mb, sub, piece.Select(q => rel(p.ToWorld(q.X, q.Y, MarkLift))).ToArray());
        }
    }
}
