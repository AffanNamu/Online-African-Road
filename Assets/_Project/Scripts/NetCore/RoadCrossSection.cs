using System;
using System.Collections.Generic;
using System.Text;

namespace ARO.NetCore
{
    /// <summary>Submesh (material) slots of generated road geometry. The Unity layer maps each to a material.</summary>
    public static class RoadSub
    {
        public const int Asphalt = 0, Shoulder = 1, Verge = 2, Concrete = 3, MarkWhite = 4, MarkYellow = 5, Metal = 6, Count = 7;
    }

    /// <summary>One vertex of the cross-section polyline. Strip = submesh of the surface between this point and the next. Y is relative to the centre-line elevation.</summary>
    public struct CrossPoint
    {
        public float X, Y; public int Strip;
        public CrossPoint(float x, float y, int strip) { X = x; Y = y; Strip = strip; }
    }

    public sealed class MarkingLine
    {
        public float X, Width, Dash, Gap;   // Gap == 0: solid line
        public bool Yellow;
        public string Name;
        public bool IsEdge; public int Side;   // Side: -1 left, +1 right, 0 centre
    }

    /// <summary>
    /// The lateral layout of a road at one station, derived from a RoadProfileSpec. The polyline runs left to right with the outside
    /// of every surface on the LEFT of the direction of travel (that is what makes the triangle winding face outwards everywhere).
    /// </summary>
    public sealed class RoadCrossSection
    {
        public const float MedianHeight = 0.18f;

        public RoadProfileSpec Spec { get; private set; }
        public CrossPoint[] Points { get; private set; }
        public MarkingLine[] Markings { get; private set; }
        public float CarriagewayHalfWidth { get; private set; }   // centre line (or median edge) to the outer lane edge, plus the median half width
        public float PavedHalfWidth { get; private set; }         // including shoulders
        public float FormationHalfWidth { get; private set; }     // including the verge
        public float EdgeY { get; private set; }                  // surface height at the formation edge, relative to the centre-line elevation
        public bool HasMedian { get; private set; }
        public string TopologyKey { get; private set; }
        public float[] LaneCentres { get; private set; }          // signed lateral centres of every driving lane (negative = oncoming side)

        public static RoadCrossSection Build(RoadProfileSpec p)
        {
            if (p == null) throw new ArgumentNullException(nameof(p));
            if (p.lanesPerDirection < 1 || p.lanesPerDirection > 4) throw new ArgumentException("lanesPerDirection must be 1..4");
            if (p.laneWidth < 2.5f || p.laneWidth > 4.5f) throw new ArgumentException("laneWidth must be 2.5..4.5 m");
            if (p.medianWidth < 0f || p.shoulderWidth < 0f || p.vergeWidth < 0f || p.innerShoulderWidth < 0f) throw new ArgumentException("widths cannot be negative");
            var cs = new RoadCrossSection { Spec = p };
            int L = p.lanesPerDirection; float w = p.laneWidth, c = p.camberPercent / 100f;
            bool divided = p.medianWidth > 0.01f; cs.HasMedian = divided;
            float m = p.medianWidth * 0.5f, ish = divided ? p.innerShoulderWidth : 0f;
            float xi = m + ish;                 // inner edge of the first lane
            float xo = xi + L * w;              // outer edge of the outer lane
            float xs = xo + p.shoulderWidth, xv = xs + p.vergeWidth;

            // Right half, centre outwards: (x, y, strip of the surface that STARTS at this point).
            var right = new List<CrossPoint>();
            float y, yi = 0f;
            if (divided)
            {
                right.Add(new CrossPoint(0f, MedianHeight, RoadSub.Concrete));
                right.Add(new CrossPoint(m, MedianHeight, RoadSub.Concrete));   // median top, then its vertical face
                right.Add(new CrossPoint(m, 0f, RoadSub.Shoulder));             // inner shoulder
                yi = -c * ish; right.Add(new CrossPoint(xi, yi, RoadSub.Asphalt));
            }
            else right.Add(new CrossPoint(0f, 0f, RoadSub.Asphalt));
            y = yi - c * (xo - xi);
            right.Add(new CrossPoint(xo, y, RoadSub.Shoulder));
            y -= c * 1.5f * p.shoulderWidth; right.Add(new CrossPoint(xs, y, RoadSub.Verge));
            y -= 0.05f * p.vergeWidth; right.Add(new CrossPoint(xv, y, 0));
            cs.EdgeY = y;

            // Mirror to the left. The strip index belongs to the surface starting at a point when travelling left to right.
            var pts = new List<CrossPoint>();
            for (int i = right.Count - 1; i >= 1; i--)
            {
                // Left of the centre the polyline runs outer -> inner, so the strip that starts at mirrored point i is the strip that ENDS at right[i] going outwards: right[i-1].Strip.
                pts.Add(new CrossPoint(-right[i].X, right[i].Y, right[i - 1].Strip));
            }
            pts.Add(new CrossPoint(0f, right[0].Y, right[0].Strip));      // centre point: starts the right half
            for (int i = 1; i < right.Count; i++) pts.Add(right[i]);
            // The centre point of a divided road is the median top (a single point); right[0].Strip is Concrete for both halves.
            cs.Points = pts.ToArray();

            cs.CarriagewayHalfWidth = xo;
            cs.PavedHalfWidth = xs;
            cs.FormationHalfWidth = xv;

            var lanes = new List<float>();
            for (int k = 0; k < L; k++) { float cc = xi + (k + 0.5f) * w; lanes.Add(cc); lanes.Add(-cc); }
            lanes.Sort(); cs.LaneCentres = lanes.ToArray();

            // Markings
            var mk = new List<MarkingLine>();
            if (!divided)
            {
                if (p.centreLine == "double_solid")
                {
                    mk.Add(new MarkingLine { X = -0.12f, Width = 0.12f, Yellow = true, Name = "centre_a", Side = 0 });
                    mk.Add(new MarkingLine { X = 0.12f, Width = 0.12f, Yellow = true, Name = "centre_b", Side = 0 });
                }
                else if (p.centreLine == "dashed")
                    mk.Add(new MarkingLine { X = 0f, Width = 0.15f, Yellow = p.centreLineColor == "yellow", Dash = p.laneLineDash, Gap = p.laneLineGap, Name = "centre", Side = 0 });
            }
            else
            {
                mk.Add(new MarkingLine { X = -(xi + 0.2f), Width = 0.15f, Yellow = true, Name = "median_edge_l", IsEdge = true, Side = -1 });
                mk.Add(new MarkingLine { X = xi + 0.2f, Width = 0.15f, Yellow = true, Name = "median_edge_r", IsEdge = true, Side = 1 });
            }
            for (int k = 1; k < L; k++)
            {
                mk.Add(new MarkingLine { X = xi + k * w, Width = 0.12f, Dash = p.laneLineDash, Gap = p.laneLineGap, Name = "lane_r" + k, Side = 1 });
                mk.Add(new MarkingLine { X = -(xi + k * w), Width = 0.12f, Dash = p.laneLineDash, Gap = p.laneLineGap, Name = "lane_l" + k, Side = -1 });
            }
            mk.Add(new MarkingLine { X = xo - 0.25f, Width = 0.15f, Name = "edge_r", IsEdge = true, Side = 1 });
            mk.Add(new MarkingLine { X = -(xo - 0.25f), Width = 0.15f, Name = "edge_l", IsEdge = true, Side = -1 });
            cs.Markings = mk.ToArray();

            cs.TopologyKey = new StringBuilder().Append(L).Append('|').Append(w).Append('|').Append(p.medianWidth).Append('|').Append(ish).Append('|')
                .Append(p.shoulderWidth).Append('|').Append(p.vergeWidth).Append('|').Append(p.centreLine).Append('|').Append(p.edgeBarrier).ToString();
            return cs;
        }

        /// <summary>Surface height (relative to the centre-line elevation) at lateral x, interpolated along the polyline. Beyond the edges it clamps.</summary>
        public float HeightAt(float x)
        {
            var pts = Points;
            if (x <= pts[0].X) return pts[0].Y;
            for (int i = 0; i < pts.Length - 1; i++)
            {
                float x0 = pts[i].X, x1 = pts[i + 1].X;
                if (x1 - x0 < 1e-5f) continue;                      // vertical face
                if (x >= x0 && x <= x1) return Mathx.Lerp(pts[i].Y, pts[i + 1].Y, (x - x0) / (x1 - x0));
            }
            return pts[pts.Length - 1].Y;
        }
    }

    /// <summary>Longitudinal surface wear (rutting and settlement bumps). Deterministic in (seed, s, x). Potholes proper are NOT modelled in geometry (they need a decal/normal-map layer).</summary>
    public static class RoadWear
    {
        public static float Amplitude(Surface s) => s == Surface.Damaged ? 0.045f : s == Surface.Worn ? 0.014f : 0f;

        public static float Height(Surface surface, int seed, float s, float x)
        {
            float a = Amplitude(surface); if (a <= 0f) return 0f;
            float n1 = Noise1(seed, s / 6.5f) * 2f - 1f;
            float n2 = Noise1(seed + 17, s / 2.6f + x * 0.35f) * 2f - 1f;
            return a * (0.7f * n1 + 0.45f * n2);
        }

        static float Noise1(int seed, float t)
        {
            int i = (int)Math.Floor(t); float f = Mathx.SmoothStep(t - i);
            return Mathx.Lerp(Mathx.Hash01(seed, i), Mathx.Hash01(seed, i + 1), f);
        }
    }
}
