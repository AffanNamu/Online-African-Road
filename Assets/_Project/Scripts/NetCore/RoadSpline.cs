using System;
using System.Collections.Generic;

namespace ARO.NetCore
{
    public struct V2
    {
        public float X, Y;
        public V2(float x, float y) { X = x; Y = y; }
    }

    /// <summary>Minimal 3D vector (no UnityEngine dependency, so the road/terrain maths is unit-tested outside Unity). x east, y up, z north.</summary>
    public struct V3
    {
        public float X, Y, Z;
        public V3(float x, float y, float z) { X = x; Y = y; Z = z; }
        public static V3 operator +(V3 a, V3 b) => new V3(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
        public static V3 operator -(V3 a, V3 b) => new V3(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
        public static V3 operator *(V3 a, float k) => new V3(a.X * k, a.Y * k, a.Z * k);
        public float Length => (float)Math.Sqrt(X * X + Y * Y + Z * Z);
        public bool IsFinite => !(float.IsNaN(X) || float.IsNaN(Y) || float.IsNaN(Z) || float.IsInfinity(X) || float.IsInfinity(Y) || float.IsInfinity(Z));
        public static V3 Lerp(V3 a, V3 b, float t) => new V3(a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t, a.Z + (b.Z - a.Z) * t);
        public override string ToString() => $"({X:0.###}, {Y:0.###}, {Z:0.###})";
    }

    public static class Mathx
    {
        public static float Clamp01(float v) => v < 0f ? 0f : v > 1f ? 1f : v;
        public static float Clamp(float v, float lo, float hi) => v < lo ? lo : v > hi ? hi : v;
        public static float Lerp(float a, float b, float t) => a + (b - a) * t;
        public static float SmoothStep(float t) { t = Clamp01(t); return t * t * (3f - 2f * t); }
        public static float SmootherStep(float t) { t = Clamp01(t); return t * t * t * (t * (t * 6f - 15f) + 10f); }
        /// <summary>Stable string hash (FNV-1a). string.GetHashCode() is randomised per process on .NET Core, which would break deterministic world generation.</summary>
        public static int StableHash(string s)
        {
            unchecked { uint h = 2166136261u; if (s != null) foreach (char ch in s) { h ^= ch; h *= 16777619u; } return (int)h; }
        }
        /// <summary>Deterministic 32-bit hash to [0,1). Same inputs, same output, on every platform (no System.Random state).</summary>
        public static float Hash01(int seed, int a, int b = 0)
        {
            unchecked
            {
                uint h = (uint)seed * 374761393u + (uint)a * 668265263u + (uint)b * 2147483647u;
                h = (h ^ (h >> 13)) * 1274126177u; h ^= h >> 16;
                h = (h ^ (h >> 15)) * 2246822519u; h ^= h >> 13;
                return (h & 0xFFFFFF) / 16777216f;
            }
        }
    }

    /// <summary>
    /// A smooth road centre line through control points (x, z, elevation). Horizontal shape: centripetal Catmull-Rom (no cusps or
    /// self-loops, passes exactly through every control point). Elevation: monotone cubic (PCHIP) over arc length, so it never
    /// overshoots between control points. Everything is queried by arc length s (metres from the start).
    /// </summary>
    public sealed class RoadSpline
    {
        readonly float[] _x, _z, _y, _s, _tx, _tz;     // dense polyline
        readonly float[] _knotS;                        // arc length at each control point
        readonly Dictionary<long, List<int>> _grid = new Dictionary<long, List<int>>();
        const float Cell = 40f;

        public int PointCount => _s.Length;
        public float Length => _s[_s.Length - 1];
        public int ControlCount => _knotS.Length;
        public float ControlS(int i) => _knotS[i];

        public RoadSpline(IList<float> xs, IList<float> zs, IList<float> elevations, float chordStep = 4f)
        {
            int n = xs.Count;
            if (n < 2 || zs.Count != n || elevations.Count != n) throw new ArgumentException("a spline needs at least two control points with x, z and elevation each");
            if (chordStep < 0.5f) throw new ArgumentException("chordStep too small");
            for (int i = 0; i < n; i++) if (float.IsNaN(xs[i]) || float.IsNaN(zs[i]) || float.IsNaN(elevations[i]) || float.IsInfinity(xs[i]) || float.IsInfinity(zs[i]) || float.IsInfinity(elevations[i]))
                    throw new ArgumentException("control point " + i + " is not finite");

            var px = new List<float>(); var pz = new List<float>(); var knot = new List<int>();
            for (int i = 0; i < n - 1; i++)
            {
                double x0 = i > 0 ? xs[i - 1] : 2.0 * xs[0] - xs[1], z0 = i > 0 ? zs[i - 1] : 2.0 * zs[0] - zs[1];
                double x1 = xs[i], z1 = zs[i], x2 = xs[i + 1], z2 = zs[i + 1];
                double x3 = i + 2 < n ? xs[i + 2] : 2.0 * xs[n - 1] - xs[n - 2], z3 = i + 2 < n ? zs[i + 2] : 2.0 * zs[n - 1] - zs[n - 2];
                double chord = Math.Sqrt((x2 - x1) * (x2 - x1) + (z2 - z1) * (z2 - z1));
                if (chord < 1e-3) throw new ArgumentException("control points " + i + " and " + (i + 1) + " coincide");
                double t0 = 0, t1 = t0 + Math.Pow(Dist(x0, z0, x1, z1), 0.5), t2 = t1 + Math.Pow(chord, 0.5), t3 = t2 + Math.Pow(Dist(x2, z2, x3, z3), 0.5);
                if (t1 - t0 < 1e-6) t1 = t0 + 1e-3; if (t3 - t2 < 1e-6) t3 = t2 + 1e-3;
                int steps = Math.Max(4, (int)Math.Ceiling(chord / chordStep));
                knot.Add(px.Count);
                for (int k = 0; k < steps; k++)
                {
                    double t = t1 + (t2 - t1) * k / steps;
                    // Barry-Goldman pyramid
                    double ax = Lerp(x0, x1, t0, t1, t), az = Lerp(z0, z1, t0, t1, t);
                    double bx = Lerp(x1, x2, t1, t2, t), bz = Lerp(z1, z2, t1, t2, t);
                    double cx = Lerp(x2, x3, t2, t3, t), cz = Lerp(z2, z3, t2, t3, t);
                    double dx = Lerp(ax, bx, t0, t2, t), dz = Lerp(az, bz, t0, t2, t);
                    double ex = Lerp(bx, cx, t1, t3, t), ez = Lerp(bz, cz, t1, t3, t);
                    px.Add((float)Lerp(dx, ex, t1, t2, t)); pz.Add((float)Lerp(dz, ez, t1, t2, t));
                }
            }
            knot.Add(px.Count); px.Add(xs[n - 1]); pz.Add(zs[n - 1]);

            int m = px.Count;
            _x = px.ToArray(); _z = pz.ToArray(); _y = new float[m]; _s = new float[m]; _tx = new float[m]; _tz = new float[m];
            for (int i = 1; i < m; i++) _s[i] = _s[i - 1] + (float)Dist(_x[i - 1], _z[i - 1], _x[i], _z[i]);
            _knotS = new float[n]; for (int i = 0; i < n; i++) _knotS[i] = _s[knot[i]];

            // Elevation: PCHIP over the knots' arc lengths.
            var h = new double[n - 1]; var delta = new double[n - 1];
            for (int i = 0; i < n - 1; i++) { h[i] = _knotS[i + 1] - _knotS[i]; delta[i] = (elevations[i + 1] - elevations[i]) / h[i]; }
            var d = new double[n];
            if (n == 2) { d[0] = d[1] = delta[0]; }
            else
            {
                for (int i = 1; i < n - 1; i++)
                {
                    if (delta[i - 1] * delta[i] <= 0) d[i] = 0;
                    else { double w1 = 2 * h[i] + h[i - 1], w2 = h[i] + 2 * h[i - 1]; d[i] = (w1 + w2) / (w1 / delta[i - 1] + w2 / delta[i]); }
                }
                d[0] = EndSlope(h[0], h[1], delta[0], delta[1]); d[n - 1] = EndSlope(h[n - 2], h[n - 3], delta[n - 2], delta[n - 3]);
            }
            int seg = 0;
            for (int i = 0; i < m; i++)
            {
                while (seg < n - 2 && _s[i] > _knotS[seg + 1]) seg++;
                double t = (_s[i] - _knotS[seg]) / h[seg]; t = Math.Max(0, Math.Min(1, t));
                double t2 = t * t, t3 = t2 * t;
                double h00 = 2 * t3 - 3 * t2 + 1, h10 = t3 - 2 * t2 + t, h01 = -2 * t3 + 3 * t2, h11 = t3 - t2;
                _y[i] = (float)(h00 * elevations[seg] + h10 * h[seg] * d[seg] + h01 * elevations[seg + 1] + h11 * h[seg] * d[seg + 1]);
            }

            // Smoothed tangents (central differences), unit length in the horizontal plane.
            for (int i = 0; i < m; i++)
            {
                int a = Math.Max(0, i - 1), b = Math.Min(m - 1, i + 1);
                double dx = _x[b] - _x[a], dz = _z[b] - _z[a], len = Math.Sqrt(dx * dx + dz * dz);
                if (len < 1e-9) { dx = 0; dz = 1; len = 1; }
                _tx[i] = (float)(dx / len); _tz[i] = (float)(dz / len);
            }

            for (int i = 0; i < m - 1; i++)
            {
                int cx0 = (int)Math.Floor(Math.Min(_x[i], _x[i + 1]) / Cell), cx1 = (int)Math.Floor(Math.Max(_x[i], _x[i + 1]) / Cell);
                int cz0 = (int)Math.Floor(Math.Min(_z[i], _z[i + 1]) / Cell), cz1 = (int)Math.Floor(Math.Max(_z[i], _z[i + 1]) / Cell);
                for (int cx = cx0; cx <= cx1; cx++)
                    for (int cz = cz0; cz <= cz1; cz++)
                    {
                        long key = Key(cx, cz);
                        if (!_grid.TryGetValue(key, out var list)) _grid[key] = list = new List<int>();
                        list.Add(i);
                    }
            }
        }

        static double EndSlope(double h0, double h1, double d0, double d1)
        {
            double s = ((2 * h0 + h1) * d0 - h0 * d1) / (h0 + h1);
            if (Math.Sign(s) != Math.Sign(d0)) return 0;
            if (Math.Sign(d0) != Math.Sign(d1) && Math.Abs(s) > 3 * Math.Abs(d0)) return 3 * d0;
            return s;
        }

        static double Dist(double ax, double az, double bx, double bz) => Math.Sqrt((bx - ax) * (bx - ax) + (bz - az) * (bz - az));
        static double Lerp(double a, double b, double ta, double tb, double t) => tb - ta < 1e-9 ? a : (tb - t) / (tb - ta) * a + (t - ta) / (tb - ta) * b;
        static long Key(int cx, int cz) => ((long)cx << 32) ^ (uint)cz;

        int SegmentFor(float s)
        {
            if (s <= 0f) return 0; if (s >= Length) return _s.Length - 2;
            int lo = 0, hi = _s.Length - 1;
            while (hi - lo > 1) { int mid = (lo + hi) >> 1; if (_s[mid] <= s) lo = mid; else hi = mid; }
            return lo;
        }

        public V3 PositionAt(float s)
        {
            s = Mathx.Clamp(s, 0f, Length); int i = SegmentFor(s);
            float t = (s - _s[i]) / Math.Max(1e-6f, _s[i + 1] - _s[i]);
            return new V3(Mathx.Lerp(_x[i], _x[i + 1], t), Mathx.Lerp(_y[i], _y[i + 1], t), Mathx.Lerp(_z[i], _z[i + 1], t));
        }

        public float ElevationAt(float s) => PositionAt(s).Y;

        /// <summary>Horizontal unit tangent (tx, tz) at s, smoothly interpolated.</summary>
        public void TangentAt(float s, out float tx, out float tz)
        {
            s = Mathx.Clamp(s, 0f, Length); int i = SegmentFor(s);
            float t = (s - _s[i]) / Math.Max(1e-6f, _s[i + 1] - _s[i]);
            float x = Mathx.Lerp(_tx[i], _tx[i + 1], t), z = Mathx.Lerp(_tz[i], _tz[i + 1], t);
            float len = (float)Math.Sqrt(x * x + z * z); if (len < 1e-6f) { x = _tx[i]; z = _tz[i]; len = 1f; }
            tx = x / len; tz = z / len;
        }

        /// <summary>Position plus the horizontal forward and right unit vectors (right = forward rotated clockwise seen from above).</summary>
        public void FrameAt(float s, out V3 pos, out V3 forward, out V3 right)
        {
            pos = PositionAt(s); TangentAt(s, out float tx, out float tz);
            forward = new V3(tx, 0f, tz); right = new V3(tz, 0f, -tx);
        }

        /// <summary>Slope (rise over run) along the road at s.</summary>
        public float SlopeAt(float s)
        {
            const float ds = 3f;
            float a = Mathx.Clamp(s - ds, 0f, Length), b = Mathx.Clamp(s + ds, 0f, Length);
            return b - a < 1e-3f ? 0f : (ElevationAt(b) - ElevationAt(a)) / (b - a);
        }

        /// <summary>Signed curvature in 1/m, positive when the road turns right.</summary>
        public float CurvatureAt(float s)
        {
            const float ds = 6f;
            float a = Mathx.Clamp(s - ds, 0f, Length), b = Mathx.Clamp(s + ds, 0f, Length);
            if (b - a < 1e-3f) return 0f;
            TangentAt(a, out float ax, out float az); TangentAt(b, out float bx, out float bz);
            float cross = ax * bz - az * bx, dot = ax * bx + az * bz;
            return -(float)Math.Atan2(cross, dot) / (b - a);
        }

        /// <summary>Smallest turning radius (metres) anywhere on the spline, sampled every 5 m.</summary>
        public float MinRadius()
        {
            float worst = float.MaxValue;
            for (float s = 0f; s <= Length; s += 5f) { float k = Math.Abs(CurvatureAt(s)); if (k > 1e-6f) worst = Math.Min(worst, 1f / k); }
            return worst;
        }

        /// <summary>Largest absolute grade (rise/run) between sample points 20 m apart.</summary>
        public float MaxGrade()
        {
            float worst = 0f;
            for (float s = 0f; s + 20f <= Length; s += 10f) worst = Math.Max(worst, Math.Abs(ElevationAt(s + 20f) - ElevationAt(s)) / 20f);
            return worst;
        }

        /// <summary>Nearest point on the centre line within maxDist. lateral is signed (positive = right of travel).</summary>
        public bool Nearest(float x, float z, float maxDist, out float s, out float lateral, out float dist)
        {
            s = 0f; lateral = 0f; dist = float.MaxValue; bool found = false;
            int cx0 = (int)Math.Floor((x - maxDist) / Cell), cx1 = (int)Math.Floor((x + maxDist) / Cell);
            int cz0 = (int)Math.Floor((z - maxDist) / Cell), cz1 = (int)Math.Floor((z + maxDist) / Cell);
            float best = maxDist * maxDist;
            for (int cx = cx0; cx <= cx1; cx++)
                for (int cz = cz0; cz <= cz1; cz++)
                {
                    if (!_grid.TryGetValue(Key(cx, cz), out var list)) continue;
                    foreach (int i in list)
                    {
                        float ax = _x[i], az = _z[i], bx = _x[i + 1] - ax, bz = _z[i + 1] - az;
                        float len2 = bx * bx + bz * bz; if (len2 < 1e-9f) continue;
                        float t = Mathx.Clamp01(((x - ax) * bx + (z - az) * bz) / len2);
                        float qx = ax + bx * t, qz = az + bz * t, dx = x - qx, dz = z - qz, d2 = dx * dx + dz * dz;
                        if (d2 > best) continue;
                        best = d2; found = true; dist = (float)Math.Sqrt(d2);
                        s = _s[i] + t * (float)Math.Sqrt(len2);
                        TangentAt(s, out float tx, out float tz);
                        lateral = dx * tz - dz * tx;
                    }
                }
            return found;
        }
    }
}
