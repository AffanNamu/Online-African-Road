using System;
using System.Collections.Generic;

namespace ARO.NetCore
{
    /// <summary>A gap in a marking line (where a side road joins the main road): edge line on `Side` between S0 and S1 is not painted.</summary>
    public struct MarkingGap { public float S0, S1; public int Side; }

    /// <summary>
    /// Turns a corridor + its cross-sections into mesh buffers for ONE chunk. Segments between consecutive rings are owned by the chunk
    /// that contains their midpoint, and the ring table is global, so neighbouring chunks join exactly and nothing is built twice.
    /// </summary>
    public static class RoadGeometry
    {
        public const float TileMetres = 4f;           // asphalt texture tile (u across, v along)
        public const float MarkingLift = 0.03f;
        static readonly V2[] Jersey =                  // clockwise, outside on the left of travel (x lateral, y up), base at y = 0
        {
            new V2(-0.30f, 0f), new V2(-0.30f, 0.07f), new V2(-0.17f, 0.33f), new V2(-0.10f, 0.81f),
            new V2(0.10f, 0.81f), new V2(0.17f, 0.33f), new V2(0.30f, 0.07f), new V2(0.30f, 0f)
        };
        static readonly V2[] Rail = { new V2(-0.04f, 0.55f), new V2(-0.04f, 0.86f), new V2(0.04f, 0.86f), new V2(0.04f, 0.55f) };

        sealed class Ring { public V3 Pos, Fwd, Right; public float S, Scale; public int Run; public int[] Verts; }

        public static MeshBuffers BuildChunk(RoadCorridor c, float minX, float minZ, float maxX, float maxZ, V3 origin, Func<float, float, float> ground = null)
        {
            var mb = new MeshBuffers(RoadSub.Count);
            var cache = new Dictionary<long, Ring>();
            Ring GetRing(int k, int run)
            {
                long key = (long)k * 64 + run;
                if (cache.TryGetValue(key, out var r)) return r;
                r = MakeRing(c, k, run, origin, mb); cache[key] = r; return r;
            }

            int n = c.RingS.Length;
            for (int k = 0; k + 1 < n; k++)
            {
                float mx = (c.RingX[k] + c.RingX[k + 1]) * 0.5f, mz = (c.RingZ[k] + c.RingZ[k + 1]) * 0.5f;
                if (mx < minX || mx >= maxX || mz < minZ || mz >= maxZ) continue;
                float sMid = (c.RingS[k] + c.RingS[k + 1]) * 0.5f;
                int run = c.RunIndexAt(sMid);
                var a = GetRing(k, run); var b = GetRing(k + 1, run);
                var sec = c.Runs[run].Section;
                bool onBridge = c.IsMain && c.InBridge(sMid, out _);

                for (int j = 0; j + 1 < sec.Points.Length; j++)
                {
                    int sub = sec.Points[j].Strip;
                    if (onBridge && sub == RoadSub.Verge) continue;           // no dirt verge on a deck
                    mb.Quad(sub, a.Verts[j], a.Verts[j + 1], b.Verts[j], b.Verts[j + 1]);
                }

                if (sec.HasMedian) Extrude(mb, RoadSub.Concrete, a, b, 0f, sec.HeightAt(0f), Jersey, origin);
                if (sec.Spec.edgeBarrier == "guardrail" && !onBridge)
                    foreach (int side in new[] { -1, 1 })
                    {
                        float x = side * (sec.PavedHalfWidth + sec.Spec.vergeWidth * 0.5f);
                        Extrude(mb, RoadSub.Metal, a, b, x, sec.HeightAt(x), Rail, origin);
                    }
                if (onBridge && c.InBridge(sMid, out var br))
                {
                    float p = sec.PavedHalfWidth;
                    foreach (int side in new[] { -1, 1 })
                        Extrude(mb, RoadSub.Concrete, a, b, side * (p - 0.35f), sec.HeightAt(side * (p - 0.35f)), Jersey, origin);
                    float ye = sec.HeightAt(p), yl = sec.HeightAt(-p);
                    var under = new[] { new V2(p, ye), new V2(p, Math.Min(ye, yl) - br.deckThickness), new V2(-p, Math.Min(ye, yl) - br.deckThickness), new V2(-p, yl) };
                    Extrude(mb, RoadSub.Concrete, a, b, 0f, 0f, under, origin, absoluteY: true);
                }
                AddMarkings(mb, c, sec, k, run, a, b, origin);
            }

            AddGuardrailPosts(mb, c, minX, minZ, maxX, maxZ, origin);
            if (ground != null) AddPiers(mb, c, minX, minZ, maxX, maxZ, origin, ground);
            if (!c.IsMain) AddStopLine(mb, c, minX, minZ, maxX, maxZ, origin);
            return mb;
        }

        static Ring MakeRing(RoadCorridor c, int k, int run, V3 origin, MeshBuffers mb)
        {
            float s = c.RingS[k];
            c.Spline.FrameAt(s, out var pos, out var fwd, out var right);
            var sec = c.Runs[run].Section; float scale = c.ScaleAt(run, s);
            var r = new Ring { Pos = pos, Fwd = fwd, Right = right, S = s, Scale = scale, Run = run, Verts = new int[sec.Points.Length] };
            var surface = c.Runs[run].Surface;
            for (int j = 0; j < sec.Points.Length; j++)
            {
                var p = sec.Points[j]; float x = p.X * scale;
                bool concrete = p.Strip == RoadSub.Concrete;
                float wear = concrete ? 0f : RoadWear.Height(surface, c.Seed, s, x);
                var w = new V3(pos.X + right.X * x - origin.X, pos.Y + p.Y + wear - origin.Y, pos.Z + right.Z * x - origin.Z);
                r.Verts[j] = mb.Add(w, new V2(x / TileMetres, s / TileMetres));
            }
            return r;
        }

        /// <summary>World-space (origin-relative) position of lateral offset x (already scaled) on ring r, including the cross-section height and wear.</summary>
        static V3 RingPoint(RoadCorridor c, Ring r, float x, V3 origin)
        {
            var sec = c.Runs[r.Run].Section;
            float y = r.Pos.Y + sec.HeightAt(x / r.Scale) + RoadWear.Height(c.Runs[r.Run].Surface, c.Seed, r.S, x);
            return new V3(r.Pos.X + r.Right.X * x - origin.X, y - origin.Y, r.Pos.Z + r.Right.Z * x - origin.Z);
        }

        /// <summary>Extrudes a cross-section polyline (relative lateral/height) along the segment between rings a and b. baseX is lateral (unscaled), baseY relative to the centre-line (ignored when absoluteY).</summary>
        static void Extrude(MeshBuffers mb, int sub, Ring a, Ring b, float baseX, float baseY, V2[] poly, V3 origin, bool absoluteY = false)
        {
            int m = poly.Length; var ia = new int[m]; var ib = new int[m];
            for (int j = 0; j < m; j++)
            {
                ia[j] = AddAt(mb, a, baseX, baseY, poly[j], origin, absoluteY);
                ib[j] = AddAt(mb, b, baseX, baseY, poly[j], origin, absoluteY);
            }
            for (int j = 0; j + 1 < m; j++) mb.Quad(sub, ia[j], ia[j + 1], ib[j], ib[j + 1]);
        }

        static int AddAt(MeshBuffers mb, Ring r, float baseX, float baseY, V2 p, V3 origin, bool absoluteY)
        {
            float x = (absoluteY ? p.X : baseX + p.X) * r.Scale;
            float y = r.Pos.Y + (absoluteY ? p.Y : baseY + p.Y);
            return mb.Add(new V3(r.Pos.X + r.Right.X * x - origin.X, y - origin.Y, r.Pos.Z + r.Right.Z * x - origin.Z), new V2(p.X, r.S / 2f));
        }

        // ---------------------------------------------------------------------------------------------------------- markings
        static void AddMarkings(MeshBuffers mb, RoadCorridor c, RoadCrossSection sec, int k, int run, Ring a, Ring b, V3 origin)
        {
            float s0 = c.RingS[k], s1 = c.RingS[k + 1];
            foreach (var m in sec.Markings)
            {
                var pieces = new List<float[]>();
                if (m.Gap <= 0f) pieces.Add(new[] { s0, s1 });
                else
                {
                    float period = m.Dash + m.Gap;
                    for (int n = (int)Math.Floor(s0 / period); n <= (int)Math.Floor(s1 / period); n++)
                    {
                        float ds = n * period, de = ds + m.Dash;
                        float lo = Math.Max(ds, s0), hi = Math.Min(de, s1);
                        if (hi - lo > 0.05f) pieces.Add(new[] { lo, hi });
                    }
                }
                if (m.IsEdge && c.MarkingGaps != null)
                    foreach (var g in c.MarkingGaps)
                        if (g.Side == m.Side) pieces = Subtract(pieces, g.S0, g.S1);
                foreach (var pc in pieces)
                {
                    float t0 = (pc[0] - s0) / (s1 - s0), t1 = (pc[1] - s0) / (s1 - s0);
                    var l0 = Lerp(RingPoint(c, a, (m.X - m.Width * 0.5f) * a.Scale, origin), RingPoint(c, b, (m.X - m.Width * 0.5f) * b.Scale, origin), t0);
                    var r0 = Lerp(RingPoint(c, a, (m.X + m.Width * 0.5f) * a.Scale, origin), RingPoint(c, b, (m.X + m.Width * 0.5f) * b.Scale, origin), t0);
                    var l1 = Lerp(RingPoint(c, a, (m.X - m.Width * 0.5f) * a.Scale, origin), RingPoint(c, b, (m.X - m.Width * 0.5f) * b.Scale, origin), t1);
                    var r1 = Lerp(RingPoint(c, a, (m.X + m.Width * 0.5f) * a.Scale, origin), RingPoint(c, b, (m.X + m.Width * 0.5f) * b.Scale, origin), t1);
                    var up = new V3(0f, MarkingLift, 0f);
                    int sub = m.Yellow ? RoadSub.MarkYellow : RoadSub.MarkWhite;
                    int i0 = mb.Add(l0 + up, new V2(0f, pc[0])), i1 = mb.Add(r0 + up, new V2(1f, pc[0]));
                    int i2 = mb.Add(l1 + up, new V2(0f, pc[1])), i3 = mb.Add(r1 + up, new V2(1f, pc[1]));
                    mb.Quad(sub, i0, i1, i2, i3);
                }
            }
        }

        static V3 Lerp(V3 a, V3 b, float t) => V3.Lerp(a, b, t);

        static List<float[]> Subtract(List<float[]> pieces, float g0, float g1)
        {
            var res = new List<float[]>();
            foreach (var p in pieces)
            {
                if (p[1] <= g0 || p[0] >= g1) { res.Add(p); continue; }
                if (p[0] < g0 - 0.05f) res.Add(new[] { p[0], g0 });
                if (p[1] > g1 + 0.05f) res.Add(new[] { g1, p[1] });
            }
            return res;
        }

        /// <summary>Stop line across the lanes that lead into the main road, at the start of a side road.</summary>
        static void AddStopLine(MeshBuffers mb, RoadCorridor c, float minX, float minZ, float maxX, float maxZ, V3 origin)
        {
            const float at = 9f, depth = 0.5f;
            var p = c.Spline.PositionAt(at);
            if (p.X < minX || p.X >= maxX || p.Z < minZ || p.Z >= maxZ) return;
            var sec = c.Runs[0].Section; float lanesW = sec.Spec.lanesPerDirection * sec.Spec.laneWidth;
            c.Spline.FrameAt(at, out _, out var fwd, out var right);
            c.Spline.FrameAt(at + depth, out var p2, out _, out _);
            float y0 = c.SurfaceY(at, -lanesW * 0.5f) + MarkingLift, y1 = c.SurfaceY(at + depth, -lanesW * 0.5f) + MarkingLift;
            var q0 = new V3(p.X - origin.X, y0 - origin.Y, p.Z - origin.Z);
            V3 L0 = new V3(q0.X + right.X * -lanesW, q0.Y, q0.Z + right.Z * -lanesW), R0 = new V3(q0.X, q0.Y, q0.Z);
            var q1 = new V3(p2.X - origin.X, y1 - origin.Y, p2.Z - origin.Z);
            V3 L1 = new V3(q1.X + right.X * -lanesW, q1.Y, q1.Z + right.Z * -lanesW), R1 = new V3(q1.X, q1.Y, q1.Z);
            int a = mb.Add(L0, new V2(0, 0)), b = mb.Add(R0, new V2(1, 0)), cc = mb.Add(L1, new V2(0, 1)), d = mb.Add(R1, new V2(1, 1));
            mb.Quad(RoadSub.MarkWhite, a, b, cc, d);
        }

        // ---------------------------------------------------------------------------------------------------------- guardrail posts and bridge piers
        const float PostSpacing = 4f;

        static void AddGuardrailPosts(MeshBuffers mb, RoadCorridor c, float minX, float minZ, float maxX, float maxZ, V3 origin)
        {
            foreach (var run in c.Runs)
            {
                if (run.Section.Spec.edgeBarrier != "guardrail") continue;
                for (float s = (float)Math.Ceiling(run.S0 / PostSpacing) * PostSpacing; s < run.S1; s += PostSpacing)
                {
                    if (c.IsMain && c.InBridge(s, out _)) continue;
                    c.Spline.FrameAt(s, out var pos, out var fwd, out var right);
                    float k = c.ScaleAt(c.RunIndexAt(s), s);
                    foreach (int side in new[] { -1, 1 })
                    {
                        float x = side * (run.Section.PavedHalfWidth + run.Section.Spec.vergeWidth * 0.5f) * k;
                        float px = pos.X + right.X * x, pz = pos.Z + right.Z * x;
                        if (px < minX || px >= maxX || pz < minZ || pz >= maxZ) continue;
                        float y = pos.Y + run.Section.HeightAt(x / k);
                        AddBox(mb, RoadSub.Metal, new V3(px - origin.X, y - origin.Y - 0.15f, pz - origin.Z), 0.12f, 1.0f, 0.12f, fwd, right);
                    }
                }
            }
        }

        static void AddPiers(MeshBuffers mb, RoadCorridor c, float minX, float minZ, float maxX, float maxZ, V3 origin, Func<float, float, float> ground)
        {
            if (!c.IsMain) return;
            foreach (var b in c.Bridges)
                for (float s = b.startS + b.pierSpacing; s < b.endS - 4f; s += b.pierSpacing)
                {
                    int ri = c.RunIndexAt(s); var sec = c.Runs[ri].Section; float k = c.ScaleAt(ri, s);
                    c.Spline.FrameAt(s, out var pos, out var fwd, out var right);
                    float deckBottom = pos.Y + Math.Min(sec.HeightAt(sec.PavedHalfWidth), sec.HeightAt(-sec.PavedHalfWidth)) - b.deckThickness;
                    foreach (int side in new[] { -1, 1 })
                    {
                        float x = side * sec.PavedHalfWidth * 0.5f * k, px = pos.X + right.X * x, pz = pos.Z + right.Z * x;
                        if (px < minX || px >= maxX || pz < minZ || pz >= maxZ) continue;
                        float g = ground(px, pz) - 1.0f, h = deckBottom - g;
                        if (h < 0.5f) continue;
                        AddBox(mb, RoadSub.Concrete, new V3(px - origin.X, g - origin.Y, pz - origin.Z), 1.2f, h, 1.2f, fwd, right);
                    }
                }
        }

        /// <summary>Open-bottom box standing on `basePos`, aligned to the road direction, faces wound outwards.</summary>
        public static void AddBox(MeshBuffers mb, int sub, V3 basePos, float w, float h, float d, V3 fwd, V3 right)
        {
            float hw = w * 0.5f, hd = d * 0.5f;
            V3 P(float lx, float y, float lz) => new V3(basePos.X + right.X * lx + fwd.X * lz, basePos.Y + y, basePos.Z + right.Z * lx + fwd.Z * lz);
            var b00 = P(-hw, 0, -hd); var b10 = P(hw, 0, -hd); var b01 = P(-hw, 0, hd); var b11 = P(hw, 0, hd);
            var t00 = P(-hw, h, -hd); var t10 = P(hw, h, -hd); var t01 = P(-hw, h, hd); var t11 = P(hw, h, hd);
            var up = new V3(0, 1, 0);
            mb.QuadFacing(sub, t00, t10, t01, t11, up, new V2(0, 0), new V2(1, 0), new V2(0, 1), new V2(1, 1));
            mb.QuadFacing(sub, b00, b10, t00, t10, new V3(-fwd.X, 0, -fwd.Z), new V2(0, 0), new V2(1, 0), new V2(0, 1), new V2(1, 1));
            mb.QuadFacing(sub, b01, b11, t01, t11, new V3(fwd.X, 0, fwd.Z), new V2(0, 0), new V2(1, 0), new V2(0, 1), new V2(1, 1));
            mb.QuadFacing(sub, b00, b01, t00, t01, new V3(-right.X, 0, -right.Z), new V2(0, 0), new V2(1, 0), new V2(0, 1), new V2(1, 1));
            mb.QuadFacing(sub, b10, b11, t10, t11, new V3(right.X, 0, right.Z), new V2(0, 0), new V2(1, 0), new V2(0, 1), new V2(1, 1));
        }
    }
}
