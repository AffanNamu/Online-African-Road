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
        static readonly string[] Sides = { "both", "left", "right" };

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
                if (!Sides.Contains(r.side)) e.Add(tag + ": side must be both, left or right");
                if (r.offsetMin < 0f || r.offsetMax < r.offsetMin || r.offsetMax > 120f) e.Add(tag + ": offsets must satisfy 0 <= min <= max <= 120");
                if (r.spacingMin < 3f || r.spacingMax < r.spacingMin) e.Add(tag + ": spacing must satisfy 3 <= min <= max");
                if (r.presence < 0f || r.presence > 1f) e.Add(tag + ": presence must be 0..1");
                if (r.scaleMin <= 0f || r.scaleMax < r.scaleMin) e.Add(tag + ": scale range invalid");
                if (!string.IsNullOrWhiteSpace(r.zones))
                    foreach (var z in r.zones.Split(',')) if (!RouteVocab.TryZone(z, out _)) e.Add(tag + ": unknown zone '" + z.Trim() + "'");
            }
            seen.Clear();
            foreach (var p in spec.props ?? new PropPlacementSpec[0])
            {
                if (p == null) { e.Add("a prop placement is null"); continue; }
                if (string.IsNullOrEmpty(p.id) || !seen.Add(p.id)) e.Add("a prop placement id is missing or duplicate");
                if (string.IsNullOrEmpty(p.prop)) e.Add("prop placement '" + p.id + "': prop is required");
                else if (knownProps != null && !knownProps.Contains(p.prop)) e.Add("prop placement '" + p.id + "': unknown prop '" + p.prop + "'");
                if (p.s < 0f || p.s > L) e.Add("prop placement '" + p.id + "': s outside the route");
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
