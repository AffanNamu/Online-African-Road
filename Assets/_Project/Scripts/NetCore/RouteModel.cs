using System;
using System.Collections.Generic;
using System.Linq;

namespace ARO.NetCore
{
    /// <summary>A stretch of one corridor with a single cross-section, zone and surface (the road that starts at a control point).</summary>
    public sealed class RoadRun
    {
        public float S0, S1;
        public RoadCrossSection Section;
        public Zone Zone; public Surface Surface;
        public float ScaleAtStart = 1f, ScaleAtEnd = 1f;   // lateral scale used to meet a neighbouring run of a different topology edge to edge
    }

    /// <summary>A road centre line with its runs, ring table (where cross-sections are generated) and, on the main road, bridges.</summary>
    public sealed class RoadCorridor
    {
        public const float TransitionMetres = 40f;

        public string Id; public bool IsMain;
        public RoadSpline Spline;
        public RoadRun[] Runs;
        public BridgeSpec[] Bridges = new BridgeSpec[0];
        public float[] RingS, RingX, RingZ;
        public MarkingGap[] MarkingGaps = new MarkingGap[0];
        public int Seed;
        public float Length => Spline.Length;
        /// <summary>Largest formation half width on this corridor (cached; used to bound spatial queries).</summary>
        public float MaxFormationHalf { get { if (_maxHalf <= 0f) _maxHalf = Runs.Max(r => r.Section.FormationHalfWidth * Math.Max(1f, Math.Max(r.ScaleAtStart, r.ScaleAtEnd))); return _maxHalf; } }
        float _maxHalf;

        public int RunIndexAt(float s)
        {
            int lo = 0, hi = Runs.Length - 1;
            while (lo < hi) { int mid = (lo + hi + 1) >> 1; if (Runs[mid].S0 <= s + 1e-4f) lo = mid; else hi = mid - 1; }
            return lo;
        }
        public RoadRun RunAt(float s) => Runs[RunIndexAt(s)];
        public RoadCrossSection SectionAt(float s) => RunAt(s).Section;
        public Zone ZoneAt(float s) => RunAt(s).Zone;
        public Surface SurfaceAt(float s) => RunAt(s).Surface;

        /// <summary>Lateral scale at s: 1 inside a run, easing to the shared edge width over the last/first half transition length.</summary>
        public float ScaleAt(int runIndex, float s)
        {
            var r = Runs[runIndex]; float half = TransitionMetres * 0.5f, k = 1f;
            float a = r.S1 - s; if (a < half && r.ScaleAtEnd != 1f) k = Mathx.Lerp(r.ScaleAtEnd, 1f, Mathx.SmoothStep(a / half));
            float b = s - r.S0; if (b < half && r.ScaleAtStart != 1f) k *= Mathx.Lerp(r.ScaleAtStart, 1f, Mathx.SmoothStep(b / half));
            return k;
        }
        public float ScaleAt(float s) => ScaleAt(RunIndexAt(s), s);

        public float HalfFormationAt(float s) { int i = RunIndexAt(s); return Runs[i].Section.FormationHalfWidth * ScaleAt(i, s); }
        public float HalfPavedAt(float s) { int i = RunIndexAt(s); return Runs[i].Section.PavedHalfWidth * ScaleAt(i, s); }

        public bool InBridge(float s, out BridgeSpec bridge)
        {
            foreach (var b in Bridges) if (s >= b.startS && s <= b.endS) { bridge = b; return true; }
            bridge = null; return false;
        }

        /// <summary>Surface height at lateral offset x (before scaling) and station s, including road wear.</summary>
        public float SurfaceY(float s, float lateral)
        {
            int ri = RunIndexAt(s); var run = Runs[ri]; float k = ScaleAt(ri, s);
            float x = Math.Abs(lateral) > run.Section.FormationHalfWidth * k ? Math.Sign(lateral) * run.Section.FormationHalfWidth * k : lateral;
            return Spline.ElevationAt(s) + run.Section.HeightAt(x / k) + RoadWear.Height(run.Surface, Seed, s, x);
        }

        static float StepFor(Surface s) => s == Surface.Damaged ? 2.5f : s == Surface.Worn ? 3f : 4f;

        /// <summary>Global, deterministic ring table (independent of chunking): adjacent chunks share ring positions, so roads join without cracks.</summary>
        public void BuildRings()
        {
            var ss = new List<float> { 0f }; float s = 0f, len = Length;
            while (s < len - 1e-3f)
            {
                var run = RunAt(s + 1e-3f); float step = StepFor(run.Surface);
                float next = s + step;
                float boundary = run.S1;
                if (boundary > s + 1e-3f && next > boundary - step * 0.25f) next = boundary;      // land exactly on run boundaries
                if (len - next < step * 0.25f) next = len;
                ss.Add(next); s = next;
            }
            RingS = ss.ToArray(); RingX = new float[RingS.Length]; RingZ = new float[RingS.Length];
            for (int i = 0; i < RingS.Length; i++) { var p = Spline.PositionAt(RingS[i]); RingX[i] = p.X; RingZ[i] = p.Z; }
        }
    }

    /// <summary>Everything derived from a RouteSpec: corridors (main road + junction side roads), terrain, lookups. Pure; unit-tested.</summary>
    public sealed class RouteModel
    {
        public RouteSpec Spec { get; private set; }
        public RoadCorridor Main { get; private set; }
        public IReadOnlyList<RoadCorridor> Sides { get; private set; }
        public TerrainModel Terrain { get; private set; }
        public float Length => Main.Length;
        public IEnumerable<RoadCorridor> AllCorridors { get { yield return Main; foreach (var c in Sides) yield return c; } }

        public static RouteModel Build(RouteSpec spec)
        {
            var errors = RouteSpecValidator.Validate(spec);
            if (errors.Count > 0) throw new ArgumentException("invalid route spec:\n" + string.Join("\n", errors));
            return BuildUnchecked(spec);
        }

        public static bool TryBuild(RouteSpec spec, out RouteModel model, out List<string> errors)
        {
            errors = RouteSpecValidator.Validate(spec); model = null;
            if (errors.Count > 0) return false;
            try { model = BuildUnchecked(spec); return true; }
            catch (Exception e) { errors.Add("build failed: " + e.Message); return false; }
        }

        static RouteModel BuildUnchecked(RouteSpec spec)
        {
            var profiles = spec.profiles.ToDictionary(p => p.id);
            var sections = spec.profiles.ToDictionary(p => p.id, RoadCrossSection.Build);
            var cps = spec.controlPoints;
            var spline = new RoadSpline(cps.Select(c => c.x).ToList(), cps.Select(c => c.z).ToList(), cps.Select(c => c.elevation).ToList());

            var main = new RoadCorridor { Id = spec.routeId, IsMain = true, Spline = spline, Seed = spec.seed, Bridges = spec.bridges ?? new BridgeSpec[0] };
            main.Runs = MakeRuns(cps, spline, sections);
            main.BuildRings();

            var sides = new List<RoadCorridor>();
            var gaps = new List<MarkingGap>();
            foreach (var j in spec.junctions ?? new JunctionSpec[0])
            {
                sides.Add(MakeJunction(spec, main, sections, j));
                float half = sections[j.profile].FormationHalfWidth;
                gaps.Add(new MarkingGap { S0 = j.s - half, S1 = j.s + half, Side = j.angleDeg >= 0f ? 1 : -1 });
            }
            main.MarkingGaps = gaps.ToArray();
            var model = new RouteModel { Spec = spec, Main = main, Sides = sides };
            model.Terrain = new TerrainModel(spec, model);
            return model;
        }

        static RoadRun[] MakeRuns(ControlPoint[] cps, RoadSpline spline, Dictionary<string, RoadCrossSection> sections)
        {
            var runs = new List<RoadRun>();
            for (int i = 0; i < cps.Length - 1; i++)
            {
                RouteVocab.TryZone(cps[i].zone, out var z); RouteVocab.TrySurface(cps[i].surface, out var sf);
                var sec = sections[cps[i].profile];
                if (runs.Count > 0 && runs[runs.Count - 1].Section == sec && runs[runs.Count - 1].Zone == z && runs[runs.Count - 1].Surface == sf) { runs[runs.Count - 1].S1 = spline.ControlS(i + 1); continue; }
                runs.Add(new RoadRun { S0 = spline.ControlS(i), S1 = spline.ControlS(i + 1), Section = sec, Zone = z, Surface = sf });
            }
            // Where two neighbouring runs differ in topology, both are scaled laterally to meet at the mean formation width.
            for (int i = 0; i + 1 < runs.Count; i++)
            {
                var a = runs[i]; var b = runs[i + 1];
                if (a.Section.TopologyKey == b.Section.TopologyKey) continue;
                float ha = a.Section.FormationHalfWidth, hb = b.Section.FormationHalfWidth, mean = (ha + hb) * 0.5f;
                a.ScaleAtEnd = mean / ha; b.ScaleAtStart = mean / hb;
            }
            return runs.ToArray();
        }

        static RoadCorridor MakeJunction(RouteSpec spec, RoadCorridor main, Dictionary<string, RoadCrossSection> sections, JunctionSpec j)
        {
            int side = j.angleDeg >= 0f ? 1 : -1;
            main.Spline.FrameAt(j.s, out var pos, out var fwd, out var right);
            float paved = main.HalfPavedAt(j.s);
            float a = Math.Abs(j.angleDeg) * (float)Math.PI / 180f;
            // Heading: the travel direction rotated towards the side the road leaves on.
            var dir = new V3(fwd.X * (float)Math.Cos(a) + right.X * side * (float)Math.Sin(a), 0f, fwd.Z * (float)Math.Cos(a) + right.Z * side * (float)Math.Sin(a));
            float dl = (float)Math.Sqrt(dir.X * dir.X + dir.Z * dir.Z); dir = new V3(dir.X / dl, 0f, dir.Z / dl);
            var start = new V3(pos.X + right.X * side * paved, 0f, pos.Z + right.Z * side * paved);
            float y0 = main.SurfaceY(j.s, side * paved);
            var sec = sections[j.profile];
            // Three control points on a straight line keep the same spline code path as the main road.
            var xs = new List<float> { start.X, start.X + dir.X * j.length * 0.5f, start.X + dir.X * j.length };
            var zs = new List<float> { start.Z, start.Z + dir.Z * j.length * 0.5f, start.Z + dir.Z * j.length };
            var ys = new List<float> { y0, y0 + j.rise * 0.5f, y0 + j.rise };
            var sp = new RoadSpline(xs, zs, ys);
            var z = main.ZoneAt(j.s); var sf = main.SurfaceAt(j.s);
            var c = new RoadCorridor { Id = j.id, IsMain = false, Spline = sp, Seed = spec.seed ^ Mathx.StableHash(j.id), Runs = new[] { new RoadRun { S0 = 0f, S1 = sp.Length, Section = sec, Zone = z, Surface = sf } } };
            c.BuildRings();
            return c;
        }

        // ------------------------------------------------------------ queries
        public RoadCorridor CorridorById(string id) => AllCorridors.FirstOrDefault(c => c.Id == id);

        /// <summary>Position on the road centre line at s (x, elevation, z).</summary>
        public V3 PositionAt(float s) => Main.Spline.PositionAt(s);

        /// <summary>Height a vehicle or marker stands on at (x, z): the road surface (camber included, no wear) inside any road formation, otherwise the terrain.</summary>
        public float GroundHeight(float x, float z)
        {
            foreach (var c in AllCorridors)
            {
                if (!c.Spline.Nearest(x, z, c.MaxFormationHalf + 2f, out float s, out float lat, out _)) continue;
                int ri = c.RunIndexAt(s); float k = c.ScaleAt(ri, s);
                if (Math.Abs(lat) <= c.Runs[ri].Section.FormationHalfWidth * k)
                    return c.Spline.ElevationAt(s) + c.Runs[ri].Section.HeightAt(lat / k);
            }
            return Terrain.HeightAt(x, z);
        }

        public float DistanceAlong(float x, float z, out float lateral)
        {
            if (Main.Spline.Nearest(x, z, 10000f, out float s, out lateral, out _)) return s;
            lateral = 0f; return 0f;
        }

        /// <summary>The control point (or waypoint) distances along the route, for HUD/map consumers.</summary>
        public float WaypointS(string id)
        {
            var w = (Spec.waypoints ?? new WaypointSpec[0]).FirstOrDefault(x => x.id == id);
            return w != null ? w.s : -1f;
        }
    }

    // ============================================================================================ terrain
    /// <summary>
    /// Terrain height: smooth natural hills, then the roads CONFORM to it: flat under the formation (sunk a little under the road surface so the
    /// two never z-fight), cut and fill slopes no steeper than TerrainSpec.maxEmbankmentSlope beside it, a roadside ditch, and a valley under bridges.
    /// </summary>
    public sealed class TerrainModel
    {
        public const float SinkUnderRoad = 0.35f;
        public const float MaxReachBeyondFormation = 80f;
        readonly TerrainSpec _t; readonly RouteModel _route; readonly int _seed;

        public TerrainModel(RouteSpec spec, RouteModel route) { _t = spec.terrain ?? new TerrainSpec(); _route = route; _seed = spec.seed; }

        float Noise(float x, float z, int seedOffset)
        {
            int ix = (int)Math.Floor(x), iz = (int)Math.Floor(z); float fx = Mathx.SmootherStep(x - ix), fz = Mathx.SmootherStep(z - iz);
            float a = Mathx.Hash01(_seed + seedOffset, ix, iz), b = Mathx.Hash01(_seed + seedOffset, ix + 1, iz);
            float c = Mathx.Hash01(_seed + seedOffset, ix, iz + 1), d = Mathx.Hash01(_seed + seedOffset, ix + 1, iz + 1);
            return Mathx.Lerp(Mathx.Lerp(a, b, fx), Mathx.Lerp(c, d, fx), fz);
        }

        /// <summary>Hills only (no roads, no bridge valleys).</summary>
        public float HillsAt(float x, float z)
        {
            float h = _t.baseElevation + _t.trendX * x + _t.trendZ * z;
            float wl = Math.Max(10f, _t.hillWavelength);
            float n = Noise(x / wl, z / wl, 1) * 0.65f + Noise(x / (wl * 0.37f), z / (wl * 0.37f), 2) * 0.35f;
            h += _t.hillAmplitude * (n * 2f - 1f);
            float dw = Math.Max(5f, _t.detailWavelength);
            h += _t.detailAmplitude * (Noise(x / dw, z / dw, 3) * 2f - 1f);
            return h;
        }

        /// <summary>Hills minus any bridge valley.</summary>
        public float NaturalAt(float x, float z)
        {
            float h = HillsAt(x, z);
            var bridges = _route.Main.Bridges;
            if (bridges != null && bridges.Length > 0 && _route.Main.Spline.Nearest(x, z, 140f, out float s, out float lat, out _))
                foreach (var b in bridges)
                    if (s > b.startS && s < b.endS) h -= b.valleyDepth * ValleyAlong(b, s) * (1f - Mathx.SmoothStep(Math.Abs(lat) / 80f));
            return h;
        }

        static float ValleyAlong(BridgeSpec b, float s)
        {
            float t = (s - b.startS) / (b.endS - b.startS), sn = (float)Math.Sin(Math.PI * Mathx.Clamp01(t));
            return sn * sn;
        }

        static float BridgeMask(BridgeSpec b, float s) => Mathx.SmoothStep((s - b.startS) / 12f) * (1f - Mathx.SmoothStep((s - b.endS + 12f) / 12f));

        /// <summary>Final terrain height including road formation, embankments, ditches and bridge valleys.</summary>
        public float HeightAt(float x, float z)
        {
            float nat = NaturalAt(x, z);
            float bestD = float.MaxValue, result = nat;
            foreach (var c in _route.AllCorridors)
            {
                float reach = c.MaxFormationHalf + MaxReachBeyondFormation;
                if (!c.Spline.Nearest(x, z, reach, out float s, out float lat, out _)) continue;
                int ri = c.RunIndexAt(s); float k = c.ScaleAt(ri, s); var sec = c.Runs[ri].Section;
                float half = sec.FormationHalfWidth * k, d = Math.Abs(lat) - half;
                if (d >= bestD) continue;
                float elev = c.Spline.ElevationAt(s);
                float h;
                if (d <= 0f) h = elev + sec.HeightAt(Math.Min(Math.Abs(lat), half) / k * Math.Sign(lat == 0f ? 1f : lat)) - SinkUnderRoad;
                else
                {
                    float edgeY = elev + sec.EdgeY;
                    float delta = nat - edgeY;
                    float blend = Math.Min(MaxReachBeyondFormation, Math.Max(_t.minBlendDistance, Math.Abs(delta) / Math.Max(0.05f, _t.maxEmbankmentSlope) * 1.5f));
                    float w = Mathx.SmoothStep(d / blend);
                    h = edgeY + delta * w;
                    float dw = Math.Max(0.5f, sec.Spec.ditchWidth);
                    if (d < dw) h -= sec.Spec.ditchDepth * (float)Math.Sin(Math.PI * d / dw) * (1f - w);
                }
                if (c.IsMain && c.InBridge(s, out var b))
                {
                    float m = BridgeMask(b, s);
                    h = Mathx.Lerp(h, nat, m);
                }
                bestD = d; result = h;
            }
            return result;
        }

        /// <summary>True when (x, z) lies inside a road formation shrunk by margin metres (negative margin = strictly inside).</summary>
        public bool InsideFormation(float x, float z, float margin = 0f)
        {
            foreach (var c in _route.AllCorridors)
            {
                float reach = c.MaxFormationHalf + 2f;
                if (!c.Spline.Nearest(x, z, reach, out float s, out float lat, out _)) continue;
                if (Math.Abs(lat) <= c.HalfFormationAt(s) + margin) return true;
            }
            return false;
        }
    }
}
