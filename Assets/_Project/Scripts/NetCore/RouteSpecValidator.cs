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
            if (spec.schemaVersion != 1) e.Add("schemaVersion must be 1 (got " + spec.schemaVersion + ")");
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
                    var j = junctions.FirstOrDefault(x => x.id == s.corridor);
                    if (j == null) e.Add(tag + ": corridor '" + s.corridor + "' is neither the main road nor a junction"); else corridorLength = j.length;
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
                    var j = junctions.FirstOrDefault(x => x.id == y.corridor);
                    if (j == null) { e.Add(tag + ": corridor '" + y.corridor + "' is neither the main road nor a junction"); continue; }
                    corridorLength = j.length; corridorProfile = j.profile;
                }
                if (y.s < 3f || y.s > corridorLength - 3f) e.Add(tag + ": s must lie inside its corridor (3 m from the ends)");
                if (corridorProfile != null && sections.TryGetValue(corridorProfile, out var pr))
                {
                    if (y.lane > pr.lanesPerDirection) e.Add(tag + ": lane " + y.lane + " does not exist on profile '" + pr.id + "'");
                }
                else if (corridorProfile == null && y.lane > 0 && spec.profiles != null && spec.profiles.All(pr2 => pr2.lanesPerDirection < y.lane)) e.Add(tag + ": lane " + y.lane + " does not exist on any profile of this route");
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
                if (j.kind != "t_junction") e.Add(tag + ": only kind 't_junction' is supported");
                if (j.s < 60f || j.s > L - 60f) e.Add(tag + ": s must be at least 60 m from both ends");
                if (Math.Abs(j.angleDeg) < 30f || Math.Abs(j.angleDeg) > 150f) e.Add(tag + ": |angleDeg| must be 30..150");
                if (j.length < 40f || j.length > 600f) e.Add(tag + ": length must be 40..600 m");
                if (Math.Abs(j.rise) > j.length * MaxGrade) e.Add(tag + ": rise is steeper than the maximum grade");
                if (i > 0 && j.s - junctions[i - 1].s < 150f) e.Add(tag + ": closer than 150 m to the previous junction");
                if (DividedAt(Mathx.Clamp(j.s, 0f, L))) e.Add(tag + ": junctions on a divided section would need an interchange (not supported)");
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
                    var pj = junctions.FirstOrDefault(x => x.id == p.corridor);
                    if (pj == null) e.Add("prop placement '" + p.id + "': corridor '" + p.corridor + "' is neither the main road nor a junction"); else { propLength = pj.length; where = "its corridor"; }
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
