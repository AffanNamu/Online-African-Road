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
    /// <summary>A flared corner at a junction mouth on the main road: station of the junction, side (+1 right, -1 left), radius, half width of the side road's paved mouth.</summary>
    public struct Fillet { public float S; public int Side; public float Radius, HalfMouth; }

    public sealed class RoadCorridor
    {
        public const float TransitionMetres = 40f;

        public string Id; public bool IsMain;
        public RoadSpline Spline;
        public RoadRun[] Runs;
        public BridgeSpec[] Bridges = new BridgeSpec[0];
        public float[] RingS, RingX, RingZ;
        public MarkingGap[] MarkingGaps = new MarkingGap[0];
        public MarkingGap[] MedianOpenings = new MarkingGap[0];            // E3: stretches where a styled median is cut for a junction (Side is unused)
        public Fillet[] Fillets = new Fillet[0];                           // E3: flared corners of junction mouths
        public MarkingSymbolSpec[] Symbols = new MarkingSymbolSpec[0];     // E1 pavement symbols on this corridor (empty for every route that declares none)
        public int Seed;
        /// <summary>True when any run uses the E1 strips (kerb, walkway, planter, gutter); only then does the mesh carry the extra submeshes.</summary>
        public bool NeedsE1Strips { get { foreach (var r in Runs) if (r.Section.UsesE1Strips) return true; return false; } }
        /// <summary>The kerb and walkway are dropped (flat paving) where a side road leaves on `side` (-1 left, +1 right): the road mouth is a driveway, not a wall.</summary>
        public bool MedianOpen(float s)
        {
            foreach (var g in MedianOpenings) if (s >= g.S0 - 1e-3f && s <= g.S1 + 1e-3f) return true;
            return false;
        }

        public bool KerbDropped(float s, int side)
        {
            foreach (var g in MarkingGaps) if (g.Side == side && s >= g.S0 - 1e-3f && s <= g.S1 + 1e-3f) return true;
            return false;
        }
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

    /// <summary>
    /// E4a: which corridors can possibly answer a point query. A city network has dozens of corridors, and every terrain sample, ground-height query and prop exclusion test asked each of them;
    /// this grid (128 m cells) lists, per cell, the corridors whose centre line comes within the largest query reach of that cell, in the same order as AllCorridors, so the answers are IDENTICAL
    /// (the first corridor that contains a point still wins). Used only by routes with many corridors; a route with few keeps the plain loop, byte for byte.
    /// </summary>
    public sealed class CorridorIndex
    {
        public const float CellMetres = 128f, Slack = 8f;
        readonly Dictionary<long, RoadCorridor[]> _cells = new Dictionary<long, RoadCorridor[]>();
        static long Key(int cx, int cz) => ((long)cx << 32) ^ (uint)cz;

        public CorridorIndex(IEnumerable<RoadCorridor> corridors)
        {
            var lists = new Dictionary<long, List<RoadCorridor>>();
            foreach (var c in corridors)                                  // in AllCorridors order, so every cell list keeps that order
            {
                float reach = c.MaxFormationHalf + TerrainModel.MaxReachBeyondFormation + Slack;
                var mine = new HashSet<long>();
                for (int i = 0; i < c.RingX.Length; i++)
                {
                    int x0 = (int)Math.Floor((c.RingX[i] - reach) / CellMetres), x1 = (int)Math.Floor((c.RingX[i] + reach) / CellMetres);
                    int z0 = (int)Math.Floor((c.RingZ[i] - reach) / CellMetres), z1 = (int)Math.Floor((c.RingZ[i] + reach) / CellMetres);
                    for (int cx = x0; cx <= x1; cx++) for (int cz = z0; cz <= z1; cz++) mine.Add(Key(cx, cz));
                }
                foreach (var k in mine) { if (!lists.TryGetValue(k, out var l)) lists[k] = l = new List<RoadCorridor>(); l.Add(c); }
            }
            foreach (var kv in lists) _cells[kv.Key] = kv.Value.ToArray();
        }

        static readonly RoadCorridor[] None = new RoadCorridor[0];
        public RoadCorridor[] Near(float x, float z) => _cells.TryGetValue(Key((int)Math.Floor(x / CellMetres), (int)Math.Floor(z / CellMetres)), out var l) ? l : None;
        public int CellCount => _cells.Count;
    }

    /// <summary>Everything derived from a RouteSpec: corridors (main road + junction side roads), terrain, lookups. Pure; unit-tested.</summary>
    public sealed class RouteModel
    {
        public RouteSpec Spec { get; private set; }
        public RoadCorridor Main { get; private set; }
        public IReadOnlyList<RoadCorridor> Sides { get; private set; }
        public IReadOnlyList<SitePlate> Sites { get; private set; } = new SitePlate[0];      // E2 plates (empty for every route that declares none)
        public TerrainModel Terrain { get; private set; }
        public float Length => Main.Length;
        public IEnumerable<RoadCorridor> AllCorridors { get { yield return Main; foreach (var c in Sides) yield return c; } }

        /// <summary>Routes with this many corridors or more answer point queries through the spatial index (E4a). Fewer: the plain loop, as always.</summary>
        public const int IndexThreshold = 8;
        CorridorIndex _index; bool _indexChecked; bool? _forceIndex; RoadCorridor[] _all;
        /// <summary>For tests: true = always use the index, false = never, null = by the threshold.</summary>
        public bool? ForceIndex { get { return _forceIndex; } set { _forceIndex = value; _indexChecked = false; _index = null; } }
        public bool Indexed { get { EnsureIndex(); return _index != null; } }
        public CorridorIndex Index { get { EnsureIndex(); return _index; } }
        void EnsureIndex()
        {
            if (_indexChecked) return;
            _indexChecked = true;
            _all = AllCorridors.ToArray();
            bool use = _forceIndex ?? (Sides.Count + 1 >= IndexThreshold);
            _index = use ? new CorridorIndex(_all) : null;
        }
        /// <summary>The corridors that can contain or influence (x, z), in AllCorridors order. For a small route that is all of them. An array, so the loops that call this thousands of times per chunk allocate nothing.</summary>
        public RoadCorridor[] CorridorsNear(float x, float z) { EnsureIndex(); return _index != null ? _index.Near(x, z) : _all; }

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
            var gaps = new List<MarkingGap>(); var openings = new List<MarkingGap>(); var fillets = new List<Fillet>();
            foreach (var j0 in spec.junctions ?? new JunctionSpec[0])
            {
                var legs = new List<JunctionSpec> { j0 };
                if (j0.kind == "four_way")                // E3: the opposite stub, same station, mirrored angle
                    legs.Add(new JunctionSpec { id = j0.id + "_left", name = j0.name, kind = "t_junction", profile = string.IsNullOrEmpty(j0.profileLeft) ? j0.profile : j0.profileLeft, s = j0.s, angleDeg = -j0.angleDeg,
                        length = j0.lengthLeft > 0f ? j0.lengthLeft : j0.length, rise = j0.rise });
                float widest = 0f;
                foreach (var j in legs)
                {
                    sides.Add(MakeJunction(spec, main, sections, j));
                    float half = sections[j.profile].FormationHalfWidth; widest = Math.Max(widest, half);
                    int side = j.angleDeg >= 0f ? 1 : -1;
                    float reach = j0.fillet > 0f ? half + j0.fillet : half;
                    gaps.Add(new MarkingGap { S0 = j.s - reach, S1 = j.s + reach, Side = side });
                    if (j0.fillet > 0f) fillets.Add(new Fillet { S = j.s, Side = side, Radius = j0.fillet, HalfMouth = sections[j.profile].PavedHalfWidth });
                }
                if (j0.medianOpening) openings.Add(new MarkingGap { S0 = j0.s - widest, S1 = j0.s + widest });
            }
            main.MarkingGaps = gaps.ToArray(); main.MedianOpenings = openings.ToArray(); main.Fillets = fillets.ToArray();
            if (spec.symbols != null && spec.symbols.Length > 0)
            {
                main.Symbols = spec.symbols.Where(y => string.IsNullOrEmpty(y.corridor) || y.corridor == spec.routeId).ToArray();
                foreach (var sc in sides) sc.Symbols = spec.symbols.Where(y => y.corridor == sc.Id).ToArray();
            }
            var model = new RouteModel { Spec = spec, Main = main, Sides = sides };
            if (spec.sites != null && spec.sites.Length > 0)
            {
                var plates = new List<SitePlate>();
                foreach (var ss in spec.sites)
                {
                    var plate = SitePlate.Place(model, ss); plates.Add(plate);
                    if (ss.dropRoadKerb) DropRoadKerbAlong(model, plate);
                }
                model.Sites = plates;
                var problems = model.CheckSites();
                if (problems.Count > 0) throw new ArgumentException("invalid site plates:\n" + string.Join("\n", problems));
            }
            model.Terrain = new TerrainModel(spec, model);
            return model;
        }

        /// <summary>A plate that stands beside a kerbed road drops that road's kerb along the plate (a driveway): the same gap that interrupts the edge line.</summary>
        static void DropRoadKerbAlong(RouteModel m, SitePlate plate)
        {
            var c = string.IsNullOrEmpty(plate.Spec.corridor) || plate.Spec.corridor == m.Spec.routeId ? m.Main : m.CorridorById(plate.Spec.corridor);
            if (c == null || Math.Abs(plate.Spec.lateral) < 1f) return;                                   // a plate on the corridor's own axis (the end of a stub) needs no gap
            float half = Math.Max(plate.HalfW, plate.HalfD) * Math.Abs((float)Math.Cos(Math.PI * plate.Spec.yawDeg / 180.0)) + Math.Min(plate.HalfW, plate.HalfD) * Math.Abs((float)Math.Sin(Math.PI * plate.Spec.yawDeg / 180.0));
            var gaps = c.MarkingGaps.ToList(); gaps.Add(new MarkingGap { S0 = plate.Spec.s - half, S1 = plate.Spec.s + half, Side = plate.Spec.lateral >= 0f ? 1 : -1 });
            c.MarkingGaps = gaps.ToArray();
        }

        /// <summary>Why the plates cannot stand where they are: overlapping each other, or covering a road (a plate may touch the road it is placed from, but not lie on it).</summary>
        public List<string> CheckSites()
        {
            var e = new List<string>();
            for (int i = 0; i < Sites.Count; i++)
            {
                var a = Sites[i];
                for (int j = i + 1; j < Sites.Count; j++) if (Overlap(a, Sites[j])) e.Add("site '" + a.Id + "' overlaps site '" + Sites[j].Id + "'");
                // interior points (1 m inside the edge) must not lie in any road formation
                for (float u = -a.HalfW + 1f; u <= a.HalfW - 1f + 1e-3f; u += 4f)
                    for (float v = -a.HalfD + 1f; v <= a.HalfD - 1f + 1e-3f; v += 4f)
                    {
                        var w = a.ToWorld(u, v);
                        foreach (var c in AllCorridors)
                        {
                            if (!c.Spline.Nearest(w.X, w.Z, c.MaxFormationHalf + 2f, out float s, out float lat, out _)) continue;
                            if (s <= 1e-3f || s >= c.Spline.Length - 1e-3f) continue;               // beyond the end of a road is not on it
                            if (Math.Abs(lat) < c.HalfFormationAt(s) - 0.5f) { e.Add("site '" + a.Id + "' lies on road '" + c.Id + "' at s=" + s.ToString("0") + " m"); goto next; }
                        }
                    }
                next:;
            }
            return e.Distinct().ToList();
        }

        static bool Overlap(SitePlate a, SitePlate b)
        {   // separating axis test of two rectangles
            foreach (var ax in new[] { new[] { a.FwdX, a.FwdZ }, new[] { a.RightX, a.RightZ }, new[] { b.FwdX, b.FwdZ }, new[] { b.RightX, b.RightZ } })
            {
                float d = Math.Abs((b.Cx - a.Cx) * ax[0] + (b.Cz - a.Cz) * ax[1]);
                float ra = a.HalfW * Math.Abs(a.RightX * ax[0] + a.RightZ * ax[1]) + a.HalfD * Math.Abs(a.FwdX * ax[0] + a.FwdZ * ax[1]);
                float rb = b.HalfW * Math.Abs(b.RightX * ax[0] + b.RightZ * ax[1]) + b.HalfD * Math.Abs(b.FwdX * ax[0] + b.FwdZ * ax[1]);
                if (d > ra + rb) return false;
            }
            return true;
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
            foreach (var c in CorridorsNear(x, z))
            {
                if (!c.Spline.Nearest(x, z, c.MaxFormationHalf + 2f, out float s, out float lat, out _)) continue;
                int ri = c.RunIndexAt(s); float k = c.ScaleAt(ri, s);
                if (Math.Abs(lat) <= c.Runs[ri].Section.FormationHalfWidth * k)
                    return c.Spline.ElevationAt(s) + c.Runs[ri].Section.HeightAt(lat / k);
            }
            foreach (var p in Sites) if (p.Contains(x, z)) return p.BaseY;
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
            foreach (var c in _route.CorridorsNear(x, z))
            {
                float reach = c.MaxFormationHalf + MaxReachBeyondFormation;
                if (!c.Spline.Nearest(x, z, reach, out float s, out float lat, out _)) continue;
                // Legacy: straight beyond the end of a road the sideways offset is about zero, so the ground there counts as "inside the formation" for up to `reach` metres. That is kept as it is for routes
                // without site plates (the golden master); a route with plates (E2) uses the exact rule, so a yard at the end of a stub is not surrounded by a flat extension of the road.
                if (_route.Sites.Count > 0 && (s <= EndEpsilon || s >= c.Spline.Length - EndEpsilon)) continue;
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
            foreach (var p in _route.Sites)
            {
                float d = p.SignedDistance(x, z);
                if (d >= bestD || d > MaxReachBeyondFormation) continue;
                float h;
                if (d <= 0f) h = p.BaseY - SinkUnderRoad;
                else
                {
                    float edgeY = p.BaseY + p.KerbHeight;                      // the ground outside starts at the top of the kerb
                    float delta = nat - edgeY;
                    float blend = Math.Min(MaxReachBeyondFormation, Math.Max(_t.minBlendDistance, Math.Abs(delta) / Math.Max(0.05f, _t.maxEmbankmentSlope) * 1.5f));
                    h = edgeY + delta * Mathx.SmoothStep(d / blend);
                }
                bestD = d; result = h;
            }
            return result;
        }

        /// <summary>
        /// Like <see cref="InsideFormation"/>, but measured along the road: a point beyond a road's first or last point is NOT inside. InsideFormation only looks at the sideways
        /// offset, and for a point straight ahead of (or behind) an end of the centre line that offset is about zero, so it reports "inside the road" for a cap roughly
        /// MaxFormationHalf + 2 m long beyond every end. The terrain tiles delete the quads that are inside the road (the road mesh covers them); in that cap there is no road mesh,
        /// and on the Medium and High tiers (finer terrain cells, so whole quads fit in the cap) it showed as a hole in front of the spawn point at the start of the route.
        /// Props still use InsideFormation (keeping the ends of the road clear is wanted there).
        /// </summary>
        public bool InsideRoadAlong(float x, float z, float margin = 0f)
        {
            foreach (var c in _route.CorridorsNear(x, z))
            {
                float reach = c.MaxFormationHalf + 2f;
                if (!c.Spline.Nearest(x, z, reach, out float s, out float lat, out _)) continue;
                if (s <= EndEpsilon || s >= c.Spline.Length - EndEpsilon) continue;     // the nearest point is an end of the centre line: this point is beyond the road, not on it
                if (Math.Abs(lat) <= c.HalfFormationAt(s) + margin) return true;
            }
            foreach (var p in _route.Sites) if (p.Contains(x, z, margin)) return true;
            return false;
        }
        const float EndEpsilon = 1e-3f;

        /// <summary>True when (x, z) lies inside a road formation shrunk by margin metres (negative margin = strictly inside).</summary>
        public bool InsideFormation(float x, float z, float margin = 0f)
        {
            foreach (var c in _route.CorridorsNear(x, z))
            {
                float reach = c.MaxFormationHalf + 2f;
                if (!c.Spline.Nearest(x, z, reach, out float s, out float lat, out _)) continue;
                if (Math.Abs(lat) <= c.HalfFormationAt(s) + margin) return true;
            }
            foreach (var p in _route.Sites) if (p.Contains(x, z, margin)) return true;
            return false;
        }
    }
}
