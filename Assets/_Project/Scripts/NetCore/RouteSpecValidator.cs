using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace ARO.NetCore
{
    /// <summary>Validates route data before anything is generated from it. Returns every problem found (empty = valid).</summary>
    public static class RouteSpecValidator
    {
        public const float MinRadiusMetres = 40f, MaxGrade = 0.08f, MinPointSpacing = 20f;
        static readonly Regex IdRx = new Regex(@"^[a-z0-9]+([-_][a-z0-9]+)*$", RegexOptions.Compiled);
        static readonly string[] Sides = { "both", "left", "right", "median" };

        public static List<string> Validate(RouteSpec spec, ISet<string> knownProps = null)
        {
            var e = new List<string>();
            if (spec == null) { e.Add("route spec is null"); return e; }
            if (spec.schemaVersion < 1 || spec.schemaVersion > 3) e.Add("schemaVersion must be 1, 2 or 3 (got " + spec.schemaVersion + ")");
            if (string.IsNullOrEmpty(spec.routeId) || !IdRx.IsMatch(spec.routeId)) e.Add("routeId must be lowercase letters, digits and dashes");
            if (string.IsNullOrWhiteSpace(spec.name)) e.Add("name is required");
            if (spec.meta == null) e.Add("meta is required");
            else
            {
                if (spec.meta.realRoadKm <= 0f) e.Add("meta.realRoadKm must be positive");
                if (string.IsNullOrWhiteSpace(spec.meta.gameCoverage)) e.Add("meta.gameCoverage must say honestly how much of the real road the playable corridor covers");
                if (string.IsNullOrWhiteSpace(spec.meta.elevationSource)) e.Add("meta.elevationSource is required (design | srtm | copernicus | ...)");
            }

            // profiles
            var profileIds = new HashSet<string>();
            if (spec.profiles == null || spec.profiles.Length == 0) e.Add("at least one road profile is required");
            else
                foreach (var p in spec.profiles)
                {
                    if (p == null || string.IsNullOrEmpty(p.id)) { e.Add("a road profile has no id"); continue; }
                    if (!profileIds.Add(p.id)) e.Add("duplicate profile id '" + p.id + "'");
                    try { RoadCrossSection.Build(p); } catch (ArgumentException ex) { e.Add("profile '" + p.id + "': " + ex.Message); }
                    if (p.centreLine != "dashed" && p.centreLine != "double_solid" && p.centreLine != "none") e.Add("profile '" + p.id + "': centreLine must be dashed, double_solid or none");
                    if (p.edgeBarrier != "none" && p.edgeBarrier != "guardrail") e.Add("profile '" + p.id + "': edgeBarrier must be none or guardrail");
                    if (p.camberPercent < 0f || p.camberPercent > 6f) e.Add("profile '" + p.id + "': camberPercent must be 0..6");
                    if (p.ditchDepth < 0f || p.ditchDepth > 1.5f || p.ditchWidth < 0.5f || p.ditchWidth > 8f) e.Add("profile '" + p.id + "': ditch size out of range");
                    ValidateE1(p, e);
                }

            // terrain
            if (spec.terrain == null) e.Add("terrain is required");
            else
            {
                var t = spec.terrain;
                if (t.maxEmbankmentSlope < 0.1f || t.maxEmbankmentSlope > 1.5f) e.Add("terrain.maxEmbankmentSlope must be 0.1..1.5");
                if (t.hillAmplitude < 0f || t.hillAmplitude > 200f || t.detailAmplitude < 0f || t.detailAmplitude > 20f) e.Add("terrain amplitudes out of range");
                if (t.hillWavelength < 100f || t.detailWavelength < 5f) e.Add("terrain wavelengths too small");
            }

            // control points
            bool pointsOk = spec.controlPoints != null && spec.controlPoints.Length >= 2;
            if (!pointsOk) e.Add("at least two control points are required");
            else
            {
                var ids = new HashSet<string>();
                for (int i = 0; i < spec.controlPoints.Length; i++)
                {
                    var c = spec.controlPoints[i]; string tag = "controlPoints[" + i + "]";
                    if (c == null) { e.Add(tag + " is null"); pointsOk = false; continue; }
                    if (string.IsNullOrEmpty(c.id) || !ids.Add(c.id)) e.Add(tag + ": id missing or duplicate");
                    if (float.IsNaN(c.x) || float.IsNaN(c.z) || float.IsNaN(c.elevation) || float.IsInfinity(c.x) || float.IsInfinity(c.z) || float.IsInfinity(c.elevation)) { e.Add(tag + ": coordinates must be finite"); pointsOk = false; }
                    if (c.elevation < -50f || c.elevation > 2500f) e.Add(tag + ": elevation out of range");
                    if (!RouteVocab.TryZone(c.zone, out _)) e.Add(tag + ": unknown zone '" + c.zone + "'");
                    if (!RouteVocab.TrySurface(c.surface, out _)) e.Add(tag + ": unknown surface '" + c.surface + "'");
                    if (c.profile == null || !profileIds.Contains(c.profile)) e.Add(tag + ": profile '" + c.profile + "' does not exist");
                    if (i > 0 && pointsOk)
                    {
                        var p = spec.controlPoints[i - 1];
                        if (p != null && Math.Sqrt((c.x - p.x) * (c.x - p.x) + (c.z - p.z) * (c.z - p.z)) < MinPointSpacing) { e.Add(tag + ": closer than " + MinPointSpacing + " m to the previous point"); pointsOk = false; }
                    }
                }
            }

            RoadSpline spline = null;
            if (pointsOk && e.Count == 0)
            {
                try
                {
                    spline = new RoadSpline(spec.controlPoints.Select(c => c.x).ToList(), spec.controlPoints.Select(c => c.z).ToList(), spec.controlPoints.Select(c => c.elevation).ToList());
                    float r = spline.MinRadius(); if (r < MinRadiusMetres) e.Add("tightest curve has a radius of " + r.ToString("0") + " m (minimum " + MinRadiusMetres + " m)");
                    float g = spline.MaxGrade(); if (g > MaxGrade) e.Add("steepest grade is " + (g * 100f).ToString("0.0") + "% (maximum " + (MaxGrade * 100f) + "%)");
                }
                catch (Exception ex) { e.Add("spline could not be built: " + ex.Message); }
            }

            if (spline != null) ValidateAlongRoute(spec, spline, profileIds, knownProps, e);
            return e;
        }

        static int RunIndexAtS(RouteSpec spec, RoadSpline spline, float s) { int i = 0; while (i < spec.controlPoints.Length - 2 && s > spline.ControlS(i + 1)) i++; return i; }

        /// <summary>The corridors of a junction: its own stub, and for a four-way junction the opposite stub named id + "_left".</summary>
        public static IEnumerable<(string id, float length, string profile)> JunctionCorridors(JunctionSpec j)
        {
            yield return (j.id, j.length, j.profile);
            if (j.kind == "four_way") yield return (j.id + "_left", j.lengthLeft > 0f ? j.lengthLeft : j.length, string.IsNullOrEmpty(j.profileLeft) ? j.profile : j.profileLeft);
        }

        /// <summary>Finds a side corridor by name among the junctions (null when there is none).</summary>
        static bool FindCorridor(IEnumerable<JunctionSpec> junctions, string name, out float length, out string profile)
        {
            foreach (var j in junctions) foreach (var c in JunctionCorridors(j)) if (c.id == name) { length = c.length; profile = c.profile; return true; }
            length = 0f; profile = null; return false;
        }

        /// <summary>The same, also among the E5 roads, ring legs and rings (their lengths as the model will build them).</summary>
        static bool FindCorridor(RouteSpec spec, IEnumerable<JunctionSpec> junctions, string name, out float length, out string profile)
        {
            if (FindCorridor(junctions, name, out length, out profile)) return true;
            foreach (var r in spec.roads ?? new RoadSpec[0])
                if (r != null && r.id == name && r.controlPoints != null && r.controlPoints.Length >= 2 && r.controlPoints.All(c => c != null))
                {
                    float len = 0f; for (int i = 1; i < r.controlPoints.Length; i++) len += (float)Math.Sqrt(Math.Pow(r.controlPoints[i].x - r.controlPoints[i - 1].x, 2) + Math.Pow(r.controlPoints[i].z - r.controlPoints[i - 1].z, 2));
                    length = len; profile = r.controlPoints[0].profile; return true;
                }
            foreach (var g in spec.rings ?? new RingSpec[0])
            {
                if (g == null) continue;
                foreach (var l in g.legs ?? new RingLegSpec[0]) if (l != null && l.id == name) { length = l.length; profile = l.profile; return true; }
            }
            length = 0f; profile = null; return false;
        }

        static readonly string[] SymbolKinds = { "zebra", "stop_line", "yield", "arrow_straight", "arrow_left", "arrow_right", "bay" };

        /// <summary>The E1 cross-section fields of one profile: ranges, and the combinations that make no sense (a walkway without a kerb, a barrier on a kerbed edge).</summary>
        static void ValidateE1(RoadProfileSpec p, List<string> e)
        {
            string tag = "profile '" + p.id + "'";
            if (p.kerbHeight < 0f || p.kerbHeight > 0.20f) e.Add(tag + ": kerbHeight must be 0..0.20 m (a mountable kerb)");
            else if (p.kerbHeight > 0.001f && p.kerbHeight < 0.06f) e.Add(tag + ": kerbHeight must be 0 (none) or 0.06..0.20 m");
            if (p.kerbPaint != "none" && p.kerbPaint != "black_white") e.Add(tag + ": kerbPaint must be none or black_white");
            if (p.sidewalkWidth < 0f || p.sidewalkWidth > 6f) e.Add(tag + ": sidewalkWidth must be 0..6 m");
            if (p.drainWidth < 0f || p.drainWidth > 1.2f) e.Add(tag + ": drainWidth must be 0..1.2 m");
            if ((p.sidewalkWidth > 0.001f || p.drainWidth > 0.001f) && p.kerbHeight <= 0.001f) e.Add(tag + ": a walkway or gutter needs kerbHeight > 0");
            if (p.kerbHeight > 0.001f && p.edgeBarrier != "none") e.Add(tag + ": a kerbed edge cannot also carry a guardrail");
            if (p.medianStyle != "jersey" && p.medianStyle != "planter" && p.medianStyle != "painted") e.Add(tag + ": medianStyle must be jersey, planter or painted");
            else if (p.medianStyle != "jersey" && p.medianWidth < 0.9f && p.medianWidth > 0.01f) e.Add(tag + ": a planter or painted median needs medianWidth of at least 0.9 m");
            if (!string.IsNullOrEmpty(p.roadClass) && !RoadClasses.IsKnown(p.roadClass)) e.Add(tag + ": unknown roadClass '" + p.roadClass + "' (expressway, arterial, collector, local, lane, service)");
        }

        static readonly string[] SiteSurfaces = { "asphalt", "concrete", "paving", "gravel" }, SiteKerbs = { "none", "plain", "black_white" }, SiteEdges = { "front", "back", "left", "right" },
            SiteMarkKinds = { "line", "bay_row", "hatch", "arrow_straight", "arrow_left", "arrow_right" };

        /// <summary>E2 site plates: data ranges only. Whether a plate lies on a road or on another plate is checked when the model is built (RouteModel.CheckSites), because it needs the splines.</summary>
        static void ValidateSites(RouteSpec spec, List<JunctionSpec> junctions, float mainLength, ISet<string> knownProps, List<string> e)
        {
            if (spec.sites == null) return;
            if (spec.sites.Length > 40) e.Add("at most 40 site plates per route");
            var seen = new HashSet<string>();
            foreach (var s in spec.sites)
            {
                if (s == null) { e.Add("a site plate is null"); continue; }
                string tag = "site '" + s.id + "'";
                if (string.IsNullOrEmpty(s.id) || !seen.Add(s.id)) e.Add("a site plate id is missing or duplicate");
                float corridorLength = mainLength;
                if (!string.IsNullOrEmpty(s.corridor) && s.corridor != spec.routeId)
                {
                    if (!FindCorridor(spec, junctions, s.corridor, out corridorLength, out _)) { corridorLength = mainLength; e.Add(tag + ": corridor '" + s.corridor + "' is neither the main road nor a junction"); }
                }
                if (s.s < 0f || s.s > corridorLength) e.Add(tag + ": s must lie on its corridor");
                if (Math.Abs(s.lateral) > 400f || Math.Abs(s.forward) > 400f) e.Add(tag + ": lateral and forward must be within 400 m");
                if (s.widthM < 6f || s.widthM > 250f || s.depthM < 6f || s.depthM > 250f) e.Add(tag + ": widthM and depthM must be 6..250 m");
                if (s.yawDeg < -180f || s.yawDeg > 180f) e.Add(tag + ": yawDeg must be -180..180");
                if (!SiteSurfaces.Contains(s.surface)) e.Add(tag + ": surface must be asphalt, concrete, paving or gravel");
                if (!SiteKerbs.Contains(s.kerb)) e.Add(tag + ": kerb must be none, plain or black_white");
                else if (s.kerb != "none" && (s.kerbHeight < 0.06f || s.kerbHeight > 0.20f)) e.Add(tag + ": kerbHeight must be 0.06..0.20 m");
                if (s.heightOffset < -0.5f || s.heightOffset > 1.0f) e.Add(tag + ": heightOffset must be -0.5..1.0 m");
                float hw = s.widthM * 0.5f, hd = s.depthM * 0.5f;
                foreach (var en in s.entrances ?? new SiteEntranceSpec[0])
                {
                    if (en == null || !SiteEdges.Contains(en.edge)) { e.Add(tag + ": an entrance edge must be front, back, left or right"); continue; }
                    float half = en.edge == "front" || en.edge == "back" ? hw : hd;
                    if (!(en.from < en.to) || en.from < -half || en.to > half) e.Add(tag + ": entrance on the " + en.edge + " edge must satisfy -" + half + " <= from < to <= " + half);
                    else if (en.to - en.from < 3f) e.Add(tag + ": an entrance must be at least 3 m wide");
                }
                foreach (var mk in s.marks ?? new SiteMarkSpec[0])
                {
                    if (mk == null || !SiteMarkKinds.Contains(mk.kind)) { e.Add(tag + ": mark kind must be one of " + string.Join(", ", SiteMarkKinds)); continue; }
                    if (Math.Abs(mk.x) > hw || Math.Abs(mk.z) > hd) e.Add(tag + ": a " + mk.kind + " mark starts outside the plate");
                    if (mk.count < 1 || mk.count > 60) e.Add(tag + ": mark count must be 1..60");
                    if (mk.length < 0f || mk.length > 250f || mk.width < 0f || mk.width > 250f) e.Add(tag + ": mark length and width must be 0..250 m");
                    if (mk.angleDeg < -360f || mk.angleDeg > 360f) e.Add(tag + ": mark angleDeg must be -360..360");
                }
                var pseen = new HashSet<string>();
                foreach (var p in s.props ?? new SitePropSpec[0])
                {
                    if (p == null) { e.Add(tag + ": a prop is null"); continue; }
                    if (string.IsNullOrEmpty(p.id) || !pseen.Add(p.id)) e.Add(tag + ": a prop id is missing or duplicate");
                    if (string.IsNullOrEmpty(p.prop)) e.Add(tag + ", prop '" + p.id + "': prop is required");
                    else if (knownProps != null && !knownProps.Contains(p.prop)) e.Add(tag + ", prop '" + p.id + "': unknown prop '" + p.prop + "'");
                    if (Math.Abs(p.x) > hw || Math.Abs(p.z) > hd) e.Add(tag + ", prop '" + p.id + "': stands outside the plate");
                    if (p.scale <= 0f) e.Add(tag + ", prop '" + p.id + "': scale must be positive");
                }
            }
        }

        static void ValidateSymbols(RouteSpec spec, List<JunctionSpec> junctions, float mainLength, List<string> e)
        {
            if (spec.symbols == null) return;
            var seen = new HashSet<string>();
            var sections = spec.profiles != null ? spec.profiles.Where(p => p != null && !string.IsNullOrEmpty(p.id)).GroupBy(p => p.id).ToDictionary(g => g.Key, g => g.First()) : new Dictionary<string, RoadProfileSpec>();
            foreach (var y in spec.symbols)
            {
                if (y == null) { e.Add("a pavement symbol is null"); continue; }
                string tag = "symbol '" + y.id + "'";
                if (string.IsNullOrEmpty(y.id) || !seen.Add(y.id)) e.Add("a pavement symbol id is missing or duplicate");
                if (!SymbolKinds.Contains(y.kind)) e.Add(tag + ": kind must be one of " + string.Join(", ", SymbolKinds));
                if (y.side != "right" && y.side != "left" && y.side != "both") e.Add(tag + ": side must be right, left or both");
                else if (y.side == "both" && y.kind != "zebra" && y.kind != "stop_line") e.Add(tag + ": side 'both' is for zebra crossings and stop lines only");
                if (y.lane < 0 || y.lane > 4) e.Add(tag + ": lane must be 0..4");
                if (y.length < 0f || y.length > 30f) e.Add(tag + ": length must be 0..30 m");
                if (y.count < 1 || y.count > 40) e.Add(tag + ": count must be 1..40");
                if (y.kind.StartsWith("arrow") && y.lane == 0) e.Add(tag + ": an arrow belongs to one lane (lane 1..4)");
                float corridorLength = mainLength; string corridorProfile = null;
                if (!string.IsNullOrEmpty(y.corridor) && y.corridor != spec.routeId)
                {
                    if (!FindCorridor(spec, junctions, y.corridor, out corridorLength, out corridorProfile)) { e.Add(tag + ": corridor '" + y.corridor + "' is neither the main road, a junction nor an E5 road or leg"); continue; }
                }
                if (y.s < 3f || y.s > corridorLength - 3f) e.Add(tag + ": s must lie inside its corridor (3 m from the ends)");
                if (corridorProfile != null && sections.TryGetValue(corridorProfile, out var pr))
                {
                    if (y.lane > pr.lanesPerDirection) e.Add(tag + ": lane " + y.lane + " does not exist on profile '" + pr.id + "'");
                }
                else if (corridorProfile == null && y.lane > 0 && spec.profiles != null && spec.profiles.All(pr2 => pr2.lanesPerDirection < y.lane)) e.Add(tag + ": lane " + y.lane + " does not exist on any profile of this route");
            }
        }

        static readonly string[] RoadRoles = { "road", "ramp", "flyover" }, IslandCovers = { "planter", "paved", "grass", "earth" };

        /// <summary>E5 (schemaVersion 3): extra roads, ring roads. Data ranges and references only; geometry (legs meeting rings, overlaps, clearance) is checked when the model is built.</summary>
        static void ValidateE5(RouteSpec spec, List<JunctionSpec> junctions, HashSet<string> profileIds, Dictionary<string, RoadCrossSection> sections, float mainLength, ISet<string> knownProps, List<string> e)
        {
            bool hasRoads = spec.roads != null && spec.roads.Length > 0, hasRings = spec.rings != null && spec.rings.Length > 0;
            if (!hasRoads && !hasRings) return;
            if (spec.schemaVersion < 3) { e.Add("roads and rings need schemaVersion 3"); return; }
            if ((spec.roads?.Length ?? 0) > 60) e.Add("at most 60 extra roads per route");
            if ((spec.rings?.Length ?? 0) > 12) e.Add("at most 12 roundabouts per route");
            var used = new HashSet<string> { spec.routeId };
            foreach (var j in junctions) foreach (var c in JunctionCorridors(j)) used.Add(c.id);
            bool Claim(string id, string what) { if (string.IsNullOrEmpty(id) || !IdRx.IsMatch(id)) { e.Add(what + ": id must be lowercase letters, digits, dashes or underscores"); return false; } if (!used.Add(id)) { e.Add(what + ": id '" + id + "' is already used by another corridor"); return false; } return true; }

            foreach (var r in spec.roads ?? new RoadSpec[0])
            {
                if (r == null) { e.Add("a road is null"); continue; }
                string tag = "road '" + r.id + "'";
                Claim(r.id, "road");
                if (!RoadRoles.Contains(r.role)) e.Add(tag + ": role must be road, ramp or flyover");
                if (r.controlPoints == null || r.controlPoints.Length < 2) { e.Add(tag + ": at least two control points are required"); continue; }
                bool pts = true;
                for (int i = 0; i < r.controlPoints.Length; i++)
                {
                    var c = r.controlPoints[i]; string ct = tag + ", point " + i;
                    if (c == null) { e.Add(ct + " is null"); pts = false; continue; }
                    if (float.IsNaN(c.x) || float.IsNaN(c.z) || float.IsNaN(c.elevation) || float.IsInfinity(c.x) || float.IsInfinity(c.z) || float.IsInfinity(c.elevation)) { e.Add(ct + ": coordinates must be finite"); pts = false; continue; }
                    if (c.elevation < -50f || c.elevation > 2500f) e.Add(ct + ": elevation out of range");
                    if (!RouteVocab.TryZone(c.zone, out _)) e.Add(ct + ": unknown zone '" + c.zone + "'");
                    if (!RouteVocab.TrySurface(c.surface, out _)) e.Add(ct + ": unknown surface '" + c.surface + "'");
                    if (c.profile == null || !profileIds.Contains(c.profile)) e.Add(ct + ": profile '" + c.profile + "' does not exist");
                    if (i > 0 && r.controlPoints[i - 1] != null && Math.Sqrt(Math.Pow(c.x - r.controlPoints[i - 1].x, 2) + Math.Pow(c.z - r.controlPoints[i - 1].z, 2)) < MinPointSpacing) { e.Add(ct + ": closer than " + MinPointSpacing + " m to the previous point"); pts = false; }
                }
                if (!pts) continue;
                float len = 0f; for (int i = 1; i < r.controlPoints.Length; i++) len += (float)Math.Sqrt(Math.Pow(r.controlPoints[i].x - r.controlPoints[i - 1].x, 2) + Math.Pow(r.controlPoints[i].z - r.controlPoints[i - 1].z, 2));
                foreach (var b in r.decks ?? new BridgeSpec[0])
                {
                    if (b == null) { e.Add(tag + ": a deck is null"); continue; }
                    string dt = tag + ", deck '" + b.id + "'";
                    if (string.IsNullOrEmpty(b.id)) e.Add(tag + ": a deck has no id");
                    if (!b.overRoad) e.Add(dt + ": decks of extra roads are flyover decks: overRoad must be true");
                    if (b.valleyDepth != 0f) e.Add(dt + ": valleyDepth must be 0");
                    if (b.startS < 0f || b.endS <= b.startS || b.endS > len * 1.02f + 1f) e.Add(dt + ": span must satisfy 0 <= startS < endS <= the road's length (about " + len.ToString("0") + " m)");
                    if (b.deckThickness < 0.3f || b.deckThickness > 4f) e.Add(dt + ": deckThickness must be 0.3..4 m");
                    if (b.pierSpacing < 8f) e.Add(dt + ": pierSpacing must be at least 8 m");
                    foreach (var g in b.pierGaps ?? new PierGap[0]) if (g == null || g.from >= g.to) e.Add(dt + ": a pierGap needs from < to");
                }
                foreach (var jn in r.joins ?? new RoadJoinSpec[0])
                {
                    if (jn == null) { e.Add(tag + ": a join is null"); continue; }
                    if (jn.end != "start" && jn.end != "end") e.Add(tag + ": a join's end must be start or end");
                    if (jn.side != 1 && jn.side != -1) e.Add(tag + ": a join's side must be +1 or -1");
                    if (jn.gap < 5f || jn.gap > 80f) e.Add(tag + ": a join's gap must be 5..80 m");
                    if (jn.corridor == spec.routeId) { if (jn.s < 0f || jn.s > mainLength) e.Add(tag + ": a join's s lies outside the main road"); }
                    else if (!FindCorridor(spec, junctions, jn.corridor ?? "", out float cl, out _)) e.Add(tag + ": a join names corridor '" + jn.corridor + "', which does not exist");
                    else if (jn.s < 0f || jn.s > cl) e.Add(tag + ": a join's s lies outside corridor '" + jn.corridor + "'");
                }
            }

            foreach (var g in spec.rings ?? new RingSpec[0])
            {
                if (g == null) { e.Add("a ring is null"); continue; }
                string tag = "ring '" + g.id + "'";
                Claim(g.id, "ring");
                if (g.profile == null || !sections.TryGetValue(g.profile, out var sec)) { e.Add(tag + ": profile '" + g.profile + "' does not exist"); continue; }
                if (sec.HasMedian) e.Add(tag + ": the carriageway profile of a ring has no median");
                if (sec.Spec.centreLine == "double_solid") e.Add(tag + ": the centre line of a ring carriageway is a lane line (dashed or none), not a double solid line");
                if (float.IsNaN(g.x) || float.IsNaN(g.z) || float.IsNaN(g.elevation) || float.IsInfinity(g.x) || float.IsInfinity(g.z) || float.IsInfinity(g.elevation)) e.Add(tag + ": position must be finite");
                if (g.radius < 4f || g.radius > 80f) e.Add(tag + ": radius must be 4..80 m");
                if (!IslandCovers.Contains(g.island)) e.Add(tag + ": island must be planter, paved, grass or earth");
                if (!RouteVocab.TryZone(g.zone, out _)) e.Add(tag + ": unknown zone '" + g.zone + "'");
                if (!RouteVocab.TrySurface(g.surface, out _)) e.Add(tag + ": unknown surface '" + g.surface + "'");
                if (!string.IsNullOrEmpty(g.attachTo))
                {
                    if (g.attachTo != spec.routeId && !FindCorridor(spec, junctions, g.attachTo, out _, out _)) e.Add(tag + ": attachTo '" + g.attachTo + "' does not exist");
                    if (g.attachEnd != "start" && g.attachEnd != "end") e.Add(tag + ": attachEnd must be start or end");
                }
                var legIds = new HashSet<string>();
                foreach (var l in g.legs ?? new RingLegSpec[0])
                {
                    if (l == null) { e.Add(tag + ": a leg is null"); continue; }
                    string lt = tag + ", leg '" + l.id + "'";
                    Claim(l.id, tag + " leg");
                    if (l.profile == null || !profileIds.Contains(l.profile)) e.Add(lt + ": profile '" + l.profile + "' does not exist");
                    if (float.IsNaN(l.angleDeg) || float.IsInfinity(l.angleDeg)) e.Add(lt + ": angleDeg must be finite");
                    if (l.length < 40f || l.length > 600f) e.Add(lt + ": length must be 40..600 m");
                    if (Math.Abs(l.rise) > l.length * RouteModel.MaxRampGrade) e.Add(lt + ": rise is steeper than " + (RouteModel.MaxRampGrade * 100f) + "%");
                    if (!string.IsNullOrEmpty(l.zone) && !RouteVocab.TryZone(l.zone, out _)) e.Add(lt + ": unknown zone '" + l.zone + "'");
                    if (!string.IsNullOrEmpty(l.surface) && !RouteVocab.TrySurface(l.surface, out _)) e.Add(lt + ": unknown surface '" + l.surface + "'");
                }
                foreach (var c in g.connect ?? new string[0])
                    if (string.IsNullOrEmpty(c) || (c != spec.routeId && !FindCorridor(spec, junctions, c, out _, out _))) e.Add(tag + ": connect names corridor '" + c + "', which does not exist");
                var pseen = new HashSet<string>();
                foreach (var p in g.props ?? new SitePropSpec[0])
                {
                    if (p == null) { e.Add(tag + ": a prop is null"); continue; }
                    if (string.IsNullOrEmpty(p.id) || !pseen.Add(p.id)) e.Add(tag + ": a prop id is missing or duplicate");
                    if (string.IsNullOrEmpty(p.prop)) e.Add(tag + ", prop '" + p.id + "': prop is required");
                    else if (knownProps != null && !knownProps.Contains(p.prop)) e.Add(tag + ", prop '" + p.id + "': unknown prop '" + p.prop + "'");
                    if (Math.Sqrt(p.x * p.x + p.z * p.z) > g.radius) e.Add(tag + ", prop '" + p.id + "': stands outside the island");
                    if (p.scale <= 0f) e.Add(tag + ", prop '" + p.id + "': scale must be positive");
                }
            }
        }

        static void ValidateAlongRoute(RouteSpec spec, RoadSpline spline, HashSet<string> profileIds, ISet<string> knownProps, List<string> e)
        {
            float L = spline.Length;
            var sections = spec.profiles.ToDictionary(p => p.id, RoadCrossSection.Build);
            string TopologyAt(float s)
            {
                int i = 0; while (i < spec.controlPoints.Length - 2 && s > spline.ControlS(i + 1)) i++;
                return sections[spec.controlPoints[i].profile].TopologyKey;
            }
            bool DividedAt(float s)
            {
                int i = 0; while (i < spec.controlPoints.Length - 2 && s > spline.ControlS(i + 1)) i++;
                return sections[spec.controlPoints[i].profile].HasMedian;
            }

            var bridges = (spec.bridges ?? new BridgeSpec[0]).Where(b => b != null).OrderBy(b => b.startS).ToList();
            var seen = new HashSet<string>();
            for (int i = 0; i < bridges.Count; i++)
            {
                var b = bridges[i]; string tag = "bridge '" + b.id + "'";
                if (string.IsNullOrEmpty(b.id) || !seen.Add(b.id)) e.Add("a bridge id is missing or duplicate");
                if (b.startS < 0f || b.endS > L || b.endS <= b.startS) { e.Add(tag + ": span must satisfy 0 <= startS < endS <= " + L.ToString("0")); continue; }
                if (b.valleyDepth < 0f || b.valleyDepth > 40f) e.Add(tag + ": valleyDepth must be 0..40 m");
                float span = b.endS - b.startS;
                if (span < 20f || span < 2f * b.valleyDepth / 0.6f) e.Add(tag + ": span " + span.ToString("0") + " m is too short for a valley " + b.valleyDepth + " m deep");
                if (b.deckThickness < 0.3f || b.deckThickness > 4f) e.Add(tag + ": deckThickness must be 0.3..4 m");
                if (b.overRoad && spec.schemaVersion < 3) e.Add(tag + ": overRoad needs schemaVersion 3");
                if (b.overRoad && b.valleyDepth != 0f) e.Add(tag + ": a flyover deck (overRoad) digs no valley: valleyDepth must be 0");
                if (b.pierSpacing < 8f) e.Add(tag + ": pierSpacing must be at least 8 m");
                if (i > 0 && b.startS < bridges[i - 1].endS + 30f) e.Add(tag + ": closer than 30 m to the previous bridge");
                if (TopologyAt(b.startS + 0.01f) != TopologyAt(b.endS - 0.01f) || TopologyAt(b.startS + 0.01f) != TopologyAt((b.startS + b.endS) * 0.5f)) e.Add(tag + ": the road profile changes on the bridge");
            }

            var junctions = (spec.junctions ?? new JunctionSpec[0]).Where(j => j != null).OrderBy(j => j.s).ToList();
            seen.Clear();
            for (int i = 0; i < junctions.Count; i++)
            {
                var j = junctions[i]; string tag = "junction '" + j.id + "'";
                if (string.IsNullOrEmpty(j.id) || !seen.Add(j.id)) e.Add("a junction id is missing or duplicate");
                if (j.profile == null || !profileIds.Contains(j.profile)) e.Add(tag + ": profile '" + j.profile + "' does not exist");
                bool v2 = spec.schemaVersion >= 2;
                if (j.kind != "t_junction" && !(j.kind == "four_way" && v2)) e.Add(tag + (v2 ? ": kind must be 't_junction' or 'four_way'" : ": only kind 't_junction' is supported (four_way needs schemaVersion 2)"));
                if (j.s < 60f || j.s > L - 60f) e.Add(tag + ": s must be at least 60 m from both ends");
                if (Math.Abs(j.angleDeg) < 30f || Math.Abs(j.angleDeg) > 150f) e.Add(tag + ": |angleDeg| must be 30..150");
                if (j.length < 40f || j.length > 600f) e.Add(tag + ": length must be 40..600 m");
                if (Math.Abs(j.rise) > j.length * MaxGrade) e.Add(tag + ": rise is steeper than the maximum grade");
                float minGap = v2 ? 60f : 150f;
                if (i > 0 && j.s - junctions[i - 1].s < minGap) e.Add(tag + ": closer than " + minGap + " m to the previous junction");
                if (v2 && i > 0 && profileIds.Contains(j.profile) && profileIds.Contains(junctions[i - 1].profile))
                {   // closer than the old 150 m: the two stubs must still not lie on top of each other
                    var p0 = junctions[i - 1]; bool sameSide = p0.kind == "four_way" || j.kind == "four_way" || (p0.angleDeg >= 0f) == (j.angleDeg >= 0f);
                    float need = sections[j.profile].FormationHalfWidth + sections[p0.profile].FormationHalfWidth + 2f * Math.Max(j.fillet, p0.fillet) + 2f;
                    if (sameSide && j.s - p0.s < need) e.Add(tag + ": its road would overlap the road of junction '" + p0.id + "' (needs " + need.ToString("0") + " m between them)");
                }
                bool divided = DividedAt(Mathx.Clamp(j.s, 0f, L));
                if (divided)
                {
                    var secHere = sections[spec.controlPoints[RunIndexAtS(spec, spline, Mathx.Clamp(j.s, 0f, L))].profile];
                    if (!v2) e.Add(tag + ": junctions on a divided section would need an interchange (not supported)");
                    else if (!j.medianOpening) e.Add(tag + ": a junction on a divided section needs medianOpening (and a planter or painted median)");
                    else if (secHere.Spec.medianStyle == "jersey") e.Add(tag + ": a jersey (barrier) median cannot be opened; use a planter or painted median");
                }
                else if (j.medianOpening) e.Add(tag + ": medianOpening needs a divided section");
                if (!v2 && (j.fillet != 0f || j.medianOpening || !string.IsNullOrEmpty(j.profileLeft) || j.lengthLeft != 0f)) e.Add(tag + ": fillet, medianOpening, profileLeft and lengthLeft need schemaVersion 2");
                if (j.fillet < 0f || j.fillet > 15f) e.Add(tag + ": fillet must be 0..15 m");
                else if (j.fillet > 0f)
                {
                    if (Math.Abs(Math.Abs(j.angleDeg) - 90f) > 10f) e.Add(tag + ": a fillet needs |angleDeg| within 10 degrees of 90");
                    var mainSec = sections[spec.controlPoints[RunIndexAtS(spec, spline, Mathx.Clamp(j.s, 0f, L))].profile];
                    if (mainSec.HasKerb) e.Add(tag + ": a fillet is for unkerbed roads (a kerbed junction mouth has a dropped kerb)");
                }
                if (j.kind == "four_way")
                {
                    if (j.lengthLeft != 0f && (j.lengthLeft < 40f || j.lengthLeft > 600f)) e.Add(tag + ": lengthLeft must be 0 or 40..600 m");
                    if (!string.IsNullOrEmpty(j.profileLeft) && !profileIds.Contains(j.profileLeft)) e.Add(tag + ": profileLeft '" + j.profileLeft + "' does not exist");
                    if (string.IsNullOrEmpty(j.id) || junctions.Any(o => o != j && o.id == j.id + "_left")) e.Add(tag + ": the id '" + j.id + "_left' (the opposite stub) is already used");
                }
                if (bridges.Any(b => j.s > b.startS - 40f && j.s < b.endS + 40f)) e.Add(tag + ": too close to a bridge");
            }

            foreach (var r in spec.propRules ?? new PropRule[0])
            {
                if (r == null) { e.Add("a prop rule is null"); continue; }
                string tag = "propRule '" + r.id + "'";
                if (string.IsNullOrEmpty(r.prop)) e.Add(tag + ": prop is required");
                else if (knownProps != null && !knownProps.Contains(r.prop)) e.Add(tag + ": unknown prop '" + r.prop + "'");
                if (!Sides.Contains(r.side)) e.Add(tag + ": side must be both, left, right or median");
                if (r.side == "median" && string.IsNullOrWhiteSpace(r.profiles) && !spec.profiles.Any(pr => pr.medianWidth > 0.01f)) e.Add(tag + ": side median needs a divided road profile in this route");
                if (!string.IsNullOrWhiteSpace(r.profiles))
                    foreach (var pid in r.profiles.Split(','))
                    {
                        var prof = spec.profiles.FirstOrDefault(pr => pr.id == pid.Trim());
                        if (prof == null) e.Add(tag + ": unknown road profile '" + pid.Trim() + "'");
                        else if (r.side == "median" && prof.medianWidth <= 0.01f) e.Add(tag + ": profile '" + prof.id + "' has no median");
                    }
                if (r.offsetMin < 0f || r.offsetMax < r.offsetMin || r.offsetMax > 120f) e.Add(tag + ": offsets must satisfy 0 <= min <= max <= 120");
                if (r.spacingMin < 3f || r.spacingMax < r.spacingMin) e.Add(tag + ": spacing must satisfy 3 <= min <= max");
                if (r.presence < 0f || r.presence > 1f) e.Add(tag + ": presence must be 0..1");
                if (r.exclusionMargin != -1f && !(r.exclusionMargin >= 0f && r.exclusionMargin <= 10f)) e.Add(tag + ": exclusionMargin must be -1 (default) or 0..10 m");
                if (r.scaleMin <= 0f || r.scaleMax < r.scaleMin) e.Add(tag + ": scale range invalid");
                if (!string.IsNullOrWhiteSpace(r.zones))
                    foreach (var z in r.zones.Split(',')) if (!RouteVocab.TryZone(z, out _)) e.Add(tag + ": unknown zone '" + z.Trim() + "'");
            }
            ValidateE5(spec, junctions, profileIds, sections, L, knownProps, e);
            ValidateSymbols(spec, junctions, L, e);
            ValidateSites(spec, junctions, L, knownProps, e);
            seen.Clear();
            foreach (var p in spec.props ?? new PropPlacementSpec[0])
            {
                if (p == null) { e.Add("a prop placement is null"); continue; }
                if (string.IsNullOrEmpty(p.id) || !seen.Add(p.id)) e.Add("a prop placement id is missing or duplicate");
                if (string.IsNullOrEmpty(p.prop)) e.Add("prop placement '" + p.id + "': prop is required");
                else if (knownProps != null && !knownProps.Contains(p.prop)) e.Add("prop placement '" + p.id + "': unknown prop '" + p.prop + "'");
                float propLength = L; string where = "the route";
                if (!string.IsNullOrEmpty(p.corridor) && p.corridor != spec.routeId)
                {
                    if (!FindCorridor(spec, junctions, p.corridor, out propLength, out _)) { propLength = L; e.Add("prop placement '" + p.id + "': corridor '" + p.corridor + "' is neither the main road nor a junction"); } else where = "its corridor";
                }
                if (p.s < 0f || p.s > propLength) e.Add("prop placement '" + p.id + "': s outside " + where);
                if (p.scale <= 0f) e.Add("prop placement '" + p.id + "': scale must be positive");
            }

            if (spec.traffic != null)
            {
                if (spec.traffic.densityScale < 0f || spec.traffic.densityScale > 4f) e.Add("traffic.densityScale must be 0..4");
                if (spec.traffic.speedLimitScale < 0.2f || spec.traffic.speedLimitScale > 2f) e.Add("traffic.speedLimitScale must be 0.2..2");
            }

            float prev = -1f; seen.Clear();
            foreach (var w in spec.waypoints ?? new WaypointSpec[0])
            {
                if (w == null) { e.Add("a waypoint is null"); continue; }
                if (string.IsNullOrEmpty(w.id) || !seen.Add(w.id)) e.Add("a waypoint id is missing or duplicate");
                if (w.s < 0f || w.s > L + 0.5f) e.Add("waypoint '" + w.id + "': s outside the route");
                if (w.s <= prev) e.Add("waypoint '" + w.id + "': waypoints must be in increasing order of s");
                prev = w.s;
            }
        }
    }
}
