using System;
using System.Collections.Generic;
using System.Linq;

namespace ARO.NetCore
{
    /// <summary>
    /// The mesh of the parts of a roundabout that are not the circulating road itself: the central island (core and apron) and the splitter islands on its legs. Built per chunk like every other road
    /// geometry: a triangle or quad belongs to the chunk that holds its centroid, so a big island that crosses chunk borders is built once, without gaps.
    /// </summary>
    public static class RingGeometry
    {
        public const float SplitterHeight = 0.12f, SplitterStart = 0.6f, SplitterTip = 0.25f, RadialStep = 8f, MinArc = 1.6f;

        public static MeshBuffers BuildChunk(Roundabout rb, float minX, float minZ, float maxX, float maxZ, V3 origin, Func<string, RoadCorridor> corridors)
        {
            var mb = new MeshBuffers(RoadSub.CountE1);
            bool Own(float x, float z) => x >= minX && x < maxX && z >= minZ && z < maxZ;
            AddIsland(mb, rb.Island, Own, origin);
            foreach (var sp in rb.Splitters) AddSplitter(mb, corridors(sp.CorridorId), sp, Own, origin);
            return mb;
        }

        // ---------------------------------------------------------------------------------------------------------------------------------------------------------------------------- island
        /// <summary>The radii that divide the island into annuli: the apron edge (when there is an apron) and steps of at most RadialStep.</summary>
        public static List<float> IslandRadii(RingIsland isl)
        {
            var r = new List<float>();
            float core = Math.Max(0f, isl.Radius - isl.Apron);
            int n = Math.Max(1, (int)Math.Ceiling(core / RadialStep));
            for (int i = 1; i <= n; i++) r.Add(core * i / n);
            if (isl.Apron > 0.01f)
            {
                int m = Math.Max(1, (int)Math.Ceiling(isl.Apron / RadialStep));
                for (int i = 1; i <= m; i++) r.Add(core + isl.Apron * i / m);
            }
            return r;
        }

        static void AddIsland(MeshBuffers mb, RingIsland isl, Func<float, float, bool> own, V3 origin)
        {
            var radii = IslandRadii(isl); float core = Math.Max(0f, isl.Radius - isl.Apron);
            for (int k = 0; k < radii.Count; k++)
            {
                float r0 = k == 0 ? 0f : radii[k - 1], r1 = radii[k];
                if (r1 - r0 < 1e-3f) continue;
                int sub = r1 <= core + 1e-3f ? isl.CoreSub : isl.ApronSub;
                int steps = Math.Max(16, (int)Math.Ceiling(2.0 * Math.PI * r1 / MinArc));
                for (int i = 0; i < steps; i++)
                {
                    double a0 = 2.0 * Math.PI * i / steps, a1 = 2.0 * Math.PI * (i + 1) / steps;
                    V3 P(float r, double a) => new V3(isl.Cx + r * (float)Math.Cos(a), isl.TopY, isl.Cz + r * (float)Math.Sin(a));
                    if (r0 <= 1e-3f)
                    {
                        var c = new V3(isl.Cx, isl.TopY, isl.Cz); var p0 = P(r1, a0); var p1 = P(r1, a1);
                        if (!own((c.X + p0.X + p1.X) / 3f, (c.Z + p0.Z + p1.Z) / 3f)) continue;
                        RoadGeometry.FillConvex(mb, sub, new[] { Rel(c, origin), Rel(p0, origin), Rel(p1, origin) });
                    }
                    else
                    {
                        var a = P(r0, a0); var b = P(r0, a1); var c = P(r1, a1); var d = P(r1, a0);
                        if (!own((a.X + b.X + c.X + d.X) * 0.25f, (a.Z + b.Z + c.Z + d.Z) * 0.25f)) continue;
                        RoadGeometry.FillConvex(mb, sub, new[] { Rel(a, origin), Rel(b, origin), Rel(c, origin), Rel(d, origin) });
                    }
                }
            }
        }

        static V3 Rel(V3 w, V3 origin) => new V3(w.X - origin.X, w.Y - origin.Y, w.Z - origin.Z);

        // ---------------------------------------------------------------------------------------------------------------------------------------------------------------------- splitter
        /// <summary>The splitter's outline in (station on its corridor, lateral) coordinates: a teardrop that is Width wide at the ring end and tapers to a rounded tip. Convex.</summary>
        public static V2[] SplitterOutline(RoadCorridor c, RingSplitter sp, float inset = 0f)
        {
            float dir = sp.AtStart ? 1f : -1f, s0 = sp.AtStart ? SplitterStart : c.Length - SplitterStart, s1 = s0 + dir * (sp.Length - SplitterStart);
            float w0 = Math.Max(0.4f, sp.Width * 0.5f - inset), w1 = Math.Max(0.1f, SplitterTip - inset);
            float t0 = s0 + dir * inset, t1 = s1 - dir * inset;
            return new[] { new V2(t0, -w0), new V2(t0, w0), new V2(t1, w1), new V2(t1, -w1) };
        }

        static void AddSplitter(MeshBuffers mb, RoadCorridor c, RingSplitter sp, Func<float, float, bool> own, V3 origin)
        {
            var anchor = c.Spline.PositionAt(sp.AtStart ? sp.Length * 0.5f : c.Length - sp.Length * 0.5f);
            if (!own(anchor.X, anchor.Z)) return;
            var outer = SplitterOutline(c, sp, 0f); var top = SplitterOutline(c, sp, SitePlate.KerbInset);
            V3 W(V2 q, float lift)
            {
                float s = Math.Max(0f, Math.Min(c.Length, q.X));
                c.Spline.FrameAt(s, out var pos, out _, out var right);
                return new V3(pos.X + right.X * q.Y - origin.X, c.SurfaceY(s, q.Y) + lift - origin.Y, pos.Z + right.Z * q.Y - origin.Z);
            }
            int kerb = RoadSub.KerbPainted;
            for (int i = 0; i < outer.Length; i++)
            {
                int j = (i + 1) % outer.Length;
                RoadGeometry.FillConvex(mb, kerb, new[] { W(outer[i], 0.01f), W(outer[j], 0.01f), W(top[j], SplitterHeight), W(top[i], SplitterHeight) });
            }
            RoadGeometry.FillConvex(mb, RoadSub.Sidewalk, top.Select(q => W(q, SplitterHeight)).ToArray());
        }
    }
}
