using System;
using System.Collections.Generic;
using System.Linq;

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

        sealed class Ring { public V3 Pos, Fwd, Right; public float S, Scale; public int Run; public int[] Verts; public bool DropL, DropR, OpenMedian; }

        public static MeshBuffers BuildChunk(RoadCorridor c, float minX, float minZ, float maxX, float maxZ, V3 origin, Func<float, float, float> ground = null)
        {
            var mb = new MeshBuffers(c.NeedsE1Strips ? RoadSub.CountE1 : RoadSub.Count);
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
                bool onBridge = c.InBridge(sMid, out _);

                for (int j = 0; j + 1 < sec.Points.Length; j++)
                {
                    int sub = sec.Points[j].Strip;
                    if (onBridge && sub == RoadSub.Verge) continue;           // no dirt verge on a deck
                    if (RoadSub.IsE1(sub) && Dropped(sec, a, b, j)) sub = RoadSub.Shoulder;     // a dropped kerb is plain paving
                    if (a.OpenMedian && b.OpenMedian && InMedianBand(sec, j)) sub = RoadSub.Asphalt;       // E3: a median cut for a junction is carriageway
                    mb.Quad(sub, a.Verts[j], a.Verts[j + 1], b.Verts[j], b.Verts[j + 1]);
                }

                if (sec.HasMedian && sec.Spec.medianStyle == "jersey" && !(a.OpenMedian && b.OpenMedian)) Extrude(mb, RoadSub.Concrete, a, b, 0f, sec.HeightAt(0f), Jersey, origin);
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
            if (c.Symbols.Length > 0) AddSymbols(mb, c, minX, minZ, maxX, maxZ, origin);
            if (c.Fillets.Length > 0) AddFillets(mb, c, minX, minZ, maxX, maxZ, origin);
            if (ground != null) AddPiers(mb, c, minX, minZ, maxX, maxZ, origin, ground);
            if (!c.IsMain && !c.NoStopLine) AddStopLine(mb, c, minX, minZ, maxX, maxZ, origin);
            return mb;
        }

        static Ring MakeRing(RoadCorridor c, int k, int run, V3 origin, MeshBuffers mb)
        {
            float s = c.RingS[k];
            c.Spline.FrameAt(s, out var pos, out var fwd, out var right);
            var sec = c.Runs[run].Section; float scale = c.ScaleAt(run, s);
            var r = new Ring { Pos = pos, Fwd = fwd, Right = right, S = s, Scale = scale, Run = run, Verts = new int[sec.Points.Length] };
            var surface = c.Runs[run].Surface;
            if (sec.HasKerb) { r.DropL = c.KerbDropped(s, -1); r.DropR = c.KerbDropped(s, 1); }
            r.OpenMedian = sec.HasMedian && sec.Spec.medianStyle != "jersey" && c.MedianOpen(s);
            float band = sec.Spec.medianWidth * 0.5f + RoadCrossSection.MedianFace + 1e-4f;
            float paved = sec.PavedHalfWidth, yPaved = sec.HeightAt(paved), yPavedL = sec.HeightAt(-paved);
            for (int j = 0; j < sec.Points.Length; j++)
            {
                var p = sec.Points[j]; float x = p.X * scale;
                bool concrete = p.Strip == RoadSub.Concrete || RoadSub.IsE1(p.Strip) || (j > 0 && RoadSub.IsE1(sec.Points[j - 1].Strip));       // kerbs, walkways and planters are cast, not worn: neither their own vertices nor the far edge of such a strip ripple
                float wear = concrete ? 0f : RoadWear.Height(surface, c.Seed, s, x);
                float py = p.Y;
                if (r.OpenMedian && Math.Abs(p.X) <= band) py = 0f;                                                      // the median is cut down to the carriageway
                if (r.DropR && p.X > paved + 1e-4f) py = j == sec.Points.Length - 1 ? Math.Min(py, yPaved) : yPaved;       // the kerb falls to the paving level at a road mouth
                else if (r.DropL && p.X < -paved - 1e-4f) py = j == 0 ? Math.Min(py, yPavedL) : yPavedL;
                var w = new V3(pos.X + right.X * x - origin.X, pos.Y + py + wear - origin.Y, pos.Z + right.Z * x - origin.Z);
                r.Verts[j] = mb.Add(w, new V2(x / TileMetres, s / TileMetres));
            }
            return r;
        }

        /// <summary>True when strip j lies wholly inside the median band (the rim, the top and the sloped faces): the strips a junction opening turns into carriageway.</summary>
        static bool InMedianBand(RoadCrossSection sec, int j)
        {
            float band = sec.Spec.medianWidth * 0.5f + RoadCrossSection.MedianFace + 1e-4f;
            return sec.Points[j].X >= -band && sec.Points[j + 1].X <= band;
        }

        /// <summary>True when strip j (between cross-section points j and j+1) lies outside the paved edge on a side where both rings are in a kerb drop.</summary>
        static bool Dropped(RoadCrossSection sec, Ring a, Ring b, int j)
        {
            if (!sec.HasKerb) return false;
            float paved = sec.PavedHalfWidth;
            if (sec.Points[j].X >= paved - 1e-4f) return a.DropR && b.DropR;
            if (sec.Points[j + 1].X <= -paved + 1e-4f) return a.DropL && b.DropL;
            return false;
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

        // ---------------------------------------------------------------------------------------------------------- junction fillets (E3)
        public const float FilletLift = 0.012f;
        public const int FilletSteps = 6;

        /// <summary>
        /// The two flared corners of a junction mouth, as polygons in (station along the main road, outward lateral from the centre line) coordinates. For the corner downstream of the mouth
        /// (k = +1) the corner point C is where the main road's paved edge meets the side road's paved edge; the flare is the region between C and a circle of the fillet radius that touches both edges.
        /// Pure geometry: unit-tested without a mesh. Every polygon is a fan around its first point.
        /// </summary>
        public static List<V2[]> FilletPolygons(float s0, float paved, float halfMouth, float radius)
        {
            var res = new List<V2[]>();
            foreach (int k in new[] { 1, -1 })
            {
                var poly = new List<V2> { new V2(s0 + k * halfMouth, paved) };                                   // the corner
                float ox = s0 + k * (halfMouth + radius), oy = paved + radius;                                   // centre of the circle
                for (int i = 0; i <= FilletSteps; i++)
                {
                    double phi = Math.PI * 0.5 * i / FilletSteps;                                                // 0 = tangent to the side road's edge, 90 degrees = tangent to the main road's edge
                    poly.Add(new V2(ox - k * radius * (float)Math.Cos(phi), oy - radius * (float)Math.Sin(phi)));
                }
                res.Add(poly.ToArray());
            }
            return res;
        }

        static void AddFillets(MeshBuffers mb, RoadCorridor c, float minX, float minZ, float maxX, float maxZ, V3 origin)
        {
            foreach (var f in c.Fillets)
            {
                var anchor = c.Spline.PositionAt(Math.Max(0f, Math.Min(c.Length, f.S)));
                if (anchor.X < minX || anchor.X >= maxX || anchor.Z < minZ || anchor.Z >= maxZ) continue;          // the chunk that holds the junction builds both corners
                float k = c.ScaleAt(f.S), paved = c.HalfPavedAt(f.S);
                foreach (var poly in FilletPolygons(f.S, paved, f.HalfMouth, f.Radius))
                {
                    var pts = new V3[poly.Length];
                    for (int i = 0; i < poly.Length; i++)
                    {
                        float st = Math.Max(0f, Math.Min(c.Length, poly[i].X)); c.Spline.FrameAt(st, out var pos, out _, out var right);
                        float lat = f.Side * poly[i].Y, y = c.Spline.ElevationAt(st) + c.SectionAt(st).HeightAt(f.Side * paved / k) + FilletLift;       // flat at the height of the paved edge
                        pts[i] = new V3(pos.X + right.X * lat - origin.X, y - origin.Y, pos.Z + right.Z * lat - origin.Z);
                    }
                    for (int i = 1; i + 1 < pts.Length; i++) FillConvex(mb, RoadSub.Asphalt, new[] { pts[0], pts[i], pts[i + 1] });
                }
            }
        }

        // ---------------------------------------------------------------------------------------------------------- pavement symbols (RB-12)
        /// <summary>Default dimensions in metres (the sizes of Nigerian and common African highway practice; every one can be changed by `length`).</summary>
        public const float ArrowLength = 4f, ZebraDepth = 3f, ZebraBar = 0.5f, ZebraPitch = 1.0f, StopLineDepth = 0.5f, YieldTooth = 0.5f, BayPitch = 2.5f, BayLine = 0.12f;

        static void AddSymbols(MeshBuffers mb, RoadCorridor c, float minX, float minZ, float maxX, float maxZ, V3 origin)
        {
            foreach (var sy in c.Symbols)
            {
                var p = c.Spline.PositionAt(Math.Max(0f, Math.Min(c.Length, sy.s)));
                if (p.X < minX || p.X >= maxX || p.Z < minZ || p.Z >= maxZ) continue;          // the chunk that holds the anchor builds the symbol
                foreach (var piece in SymbolPieces(c, sy))
                {
                    int sub = sy.yellow ? RoadSub.MarkYellow : RoadSub.MarkWhite;
                    var pts = new V3[piece.Length];
                    for (int i = 0; i < pts.Length; i++) pts[i] = SymbolPoint(c, piece[i].X, piece[i].Y, origin);
                    FillConvex(mb, sub, pts);
                }
            }
        }

        /// <summary>The convex polygons of one symbol as (station, lateral) pairs. Pure geometry: unit-tested without a mesh.</summary>
        public static List<V2[]> SymbolPieces(RoadCorridor c, MarkingSymbolSpec sy)
        {
            var res = new List<V2[]>();
            float s0 = Math.Max(0f, Math.Min(c.Length, sy.s));
            var sec = c.SectionAt(s0); float k = c.ScaleAt(s0);
            float w = sec.Spec.laneWidth * k, xi = sec.InnerLaneEdge * k, xo = sec.CarriagewayHalfWidth * k;
            int L = sec.Spec.lanesPerDirection;
            bool both = sy.side == "both", rightSide = sy.side != "left";

            // lateral span of the lanes the symbol governs, for one side (sgn = +1 right, -1 left)
            void Span(int sgn, out float lo, out float hi)
            {
                float a = xi, b = xo;
                if (sy.lane > 0) { a = xi + (sy.lane - 1) * w; b = xi + Math.Min(sy.lane, L) * w; }
                lo = sgn > 0 ? a : -b; hi = sgn > 0 ? b : -a;
            }
            V2[] Rect(float d0, float d1, float x0, float x1) => new[] { new V2(s0 + d0, x0), new V2(s0 + d0, x1), new V2(s0 + d1, x1), new V2(s0 + d1, x0) };

            switch (sy.kind)
            {
                case "zebra":
                {
                    float depth = sy.length > 0f ? sy.length : ZebraDepth;
                    var spans = new List<float[]>();
                    if (both && !sec.HasMedian) spans.Add(new[] { -xo, xo });
                    else { foreach (int sgn in both ? new[] { -1, 1 } : new[] { rightSide ? 1 : -1 }) { Span(sgn, out float lo, out float hi); spans.Add(new[] { lo, hi }); } }
                    foreach (var sp in spans)
                        for (float x = sp[0] + (ZebraPitch - ZebraBar) * 0.5f; x + ZebraBar <= sp[1] + 1e-3f; x += ZebraPitch)
                            res.Add(Rect(-depth * 0.5f, depth * 0.5f, x, x + ZebraBar));
                    break;
                }
                case "stop_line":
                {
                    float depth = sy.length > 0f ? sy.length : StopLineDepth;
                    foreach (int sgn in both ? new[] { -1, 1 } : new[] { rightSide ? 1 : -1 })
                    {
                        Span(sgn, out float lo, out float hi);
                        float d0 = sgn > 0 ? 0f : -depth, d1 = sgn > 0 ? depth : 0f;       // the line lies on the approaching side of s, as seen by that carriageway
                        res.Add(Rect(d0, d1, lo + 0.1f, hi - 0.1f));
                    }
                    break;
                }
                case "yield":
                {
                    int sgn = rightSide ? 1 : -1; float dir = sgn;
                    Span(sgn, out float lo, out float hi);
                    float tooth = sy.length > 0f ? sy.length : YieldTooth;
                    for (float x = lo + 0.15f; x + tooth <= hi - 0.05f; x += tooth * 1.5f)
                        res.Add(new[] { new V2(s0, x), new V2(s0 + dir * tooth * 1.2f, x - tooth * 0.5f), new V2(s0 + dir * tooth * 1.2f, x + tooth * 0.5f) });
                    break;
                }
                case "arrow_straight":
                case "arrow_left":
                case "arrow_right":
                {
                    int sgn = rightSide ? 1 : -1; float dir = sgn;                           // the flow is towards +s on the right-hand carriageway
                    int ln = Math.Max(1, Math.Min(L, sy.lane <= 0 ? 1 : sy.lane));
                    float xc = sgn * (xi + (ln - 0.5f) * w);
                    float len = sy.length > 0f ? sy.length : ArrowLength;
                    // local frame: forward = along the flow, side = towards the driver's right. For the left-hand carriageway the flow is -s, so the driver's right is -x.
                    foreach (var piece in ArrowPieces(sy.kind, len))
                        res.Add(piece.Select(q => new V2(s0 + dir * q.X, xc + dir * q.Y)).ToArray());
                    break;
                }
                case "bay":
                {
                    int sgn = rightSide ? 1 : -1; float dir = sgn;
                    float pitch = sy.length > 0f ? sy.length : BayPitch;
                    float depth = Math.Max(1.0f, sec.Spec.shoulderWidth * k);
                    for (int i = 0; i <= Math.Max(1, sy.count); i++)
                    {
                        float d = i * pitch * dir;
                        float x0 = sgn * xo, x1 = sgn * (xo + depth);
                        res.Add(new[] { new V2(s0 + d - BayLine * 0.5f, x0), new V2(s0 + d - BayLine * 0.5f, x1), new V2(s0 + d + BayLine * 0.5f, x1), new V2(s0 + d + BayLine * 0.5f, x0) });
                    }
                    break;
                }
            }
            return res;
        }

        /// <summary>The convex polygons of an arrow in its own frame: X = forward from the tail (0) to the tip (len), Y = towards the driver's right. Shared by road lanes and site plates.</summary>
        public static List<V2[]> ArrowPieces(string kind, float len)
        {
            var res = new List<V2[]>();
            float stemW = 0.11f, headW = 0.5f;
            if (kind == "arrow_straight")
            {
                float stemEnd = len - 1.4f;
                res.Add(new[] { new V2(0f, -stemW), new V2(0f, stemW), new V2(stemEnd, stemW), new V2(stemEnd, -stemW) });
                res.Add(new[] { new V2(stemEnd, -headW), new V2(stemEnd, headW), new V2(len, 0f) });
            }
            else
            {
                float turn = kind == "arrow_right" ? 1f : -1f;                               // +Y = the driver's right
                float stemEnd = len * 0.5f;
                res.Add(new[] { new V2(0f, -stemW), new V2(0f, stemW), new V2(stemEnd + stemW, stemW), new V2(stemEnd + stemW, -stemW) });
                // arm: from the top of the stem, leaning 40 degrees to the turning side
                float ax = (float)Math.Cos(Math.PI * 40 / 180), ay = (float)Math.Sin(Math.PI * 40 / 180) * turn;
                float armLen = len * 0.28f;
                var a = new V2(stemEnd, 0f); var b = new V2(stemEnd + ax * armLen, ay * armLen);
                float px = -ay * stemW, py = ax * stemW;                                      // half-width vector perpendicular to the arm
                res.Add(new[] { new V2(a.X + px, a.Y + py), new V2(a.X - px, a.Y - py), new V2(b.X - px, b.Y - py), new V2(b.X + px, b.Y + py) });
                float hl = 0.9f; var tip = new V2(b.X + ax * hl, b.Y + ay * hl); float hx = -ay, hy = ax;
                res.Add(new[] { new V2(b.X + hx * headW, b.Y + hy * headW), new V2(b.X - hx * headW, b.Y - hy * headW), tip });
            }
            return res;
        }

        /// <summary>World (origin-relative) position of station s and scaled lateral x on the road surface, lifted by MarkingLift.</summary>
        static V3 SymbolPoint(RoadCorridor c, float s, float x, V3 origin)
        {
            s = Math.Max(0f, Math.Min(c.Length, s));
            c.Spline.FrameAt(s, out var pos, out _, out var right);
            return new V3(pos.X + right.X * x - origin.X, c.SurfaceY(s, x) + MarkingLift - origin.Y, pos.Z + right.Z * x - origin.Z);
        }

        /// <summary>A convex polygon as a triangle fan facing up.</summary>
        public static void FillConvex(MeshBuffers mb, int sub, V3[] pts)
        {
            if (pts.Length < 3) return;
            var idx = new int[pts.Length];
            for (int i = 0; i < pts.Length; i++) idx[i] = mb.Add(pts[i], new V2(i % 2, i / 2));
            for (int i = 1; i + 1 < pts.Length; i++)
            {
                var n = MeshBuffers.Cross(pts[i] - pts[0], pts[i + 1] - pts[0]);
                if (n.Y >= 0f) mb.Tri(sub, idx[0], idx[i], idx[i + 1]); else mb.Tri(sub, idx[0], idx[i + 1], idx[i]);
            }
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
                    if (c.InBridge(s, out _)) continue;
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
            foreach (var b in c.Bridges)
                for (float s = b.startS + b.pierSpacing; s < b.endS - 4f; s += b.pierSpacing)
                {
                    if (InPierGap(b, s)) continue;                                       // E5: the clear span over a lower road
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

        static bool InPierGap(BridgeSpec b, float s)
        {
            if (b.pierGaps == null) return false;
            foreach (var g in b.pierGaps) if (s >= g.from && s <= g.to) return true;
            return false;
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
