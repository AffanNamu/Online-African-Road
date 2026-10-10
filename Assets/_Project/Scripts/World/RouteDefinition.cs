using System;
using System.Collections.Generic;
using ARO.NetCore;
using UnityEngine;

namespace ARO.World
{
    public enum ZoneType { Urban, Industrial, Commercial, Rural, Highway }
    public enum RoadSurface { Good, Worn, Damaged }

    [Serializable]
    public struct RouteNode
    {
        public Vector3 position;
        public float width;         // metres, full carriageway
        public ZoneType zone;       // drives roadside content
        public RoadSurface surface; // surface of the segment that STARTS at this node
    }

    /// <summary>
    /// A drivable corridor as data (no hard-coded Lagos route in code). The chunk streamer builds
    /// road and roadside content from this; the server's `locations` rows reference the same coordinates.
    /// </summary>
    [CreateAssetMenu(menuName = "African Roads/Route Definition")]
    public class RouteDefinition : ScriptableObject
    {
        public string routeId = "ng-lagos-ibadan";
        public int seed = 1180;
        public RouteNode[] nodes = new RouteNode[0];
        [Tooltip("Route data (RouteSpec JSON). When present it is the source of truth: Prepare() validates it, builds the spline/terrain model and regenerates `nodes` from it. The hand-written `nodes` are only the fallback.")]
        public TextAsset specJson;

        public RouteSpec Spec { get; private set; }
        public RouteModel Model { get; private set; }
        public string PrepareError { get; private set; }
        bool _prepared;
        const float NodeSpacing = 25f;

        /// <summary>Loads and validates the route data and derives the polyline `nodes` used by traffic, the minimap and projection. Idempotent. Returns false (and keeps the legacy nodes) when the data is missing or invalid.</summary>
        public bool Prepare()
        {
            if (_prepared) return Model != null;
            _prepared = true;
            var ta = specJson != null ? specJson : Resources.Load<TextAsset>("Routes/" + routeId + ".route");
            if (ta == null) { PrepareError = "no route data for '" + routeId + "'"; Debug.LogWarning("[Route] " + PrepareError + "; using the legacy polyline"); return false; }
            try
            {
                var spec = JsonUtility.FromJson<RouteSpec>(ta.text);
                var errors = RouteSpecValidator.Validate(spec, PropLibrary.KnownIds());
                if (errors.Count > 0) { PrepareError = string.Join("; ", errors); Debug.LogError("[Route] invalid route data for '" + routeId + "': " + PrepareError); return false; }
                var model = RouteModel.Build(spec);
                Spec = spec; Model = model; routeId = spec.routeId; seed = spec.seed;
                nodes = ResampleNodes(model); _cum = null;
                Debug.Log($"[Route] {spec.routeId}: {spec.controlPoints.Length} control points, {model.Length / 1000f:0.0} km (real road {spec.meta.realRoadKm:0} km), {model.Main.Runs.Length} profile runs, {model.Main.Bridges.Length} bridges, {model.Sides.Count} junctions, {nodes.Length} polyline nodes");
                return true;
            }
            catch (Exception e) { PrepareError = e.Message; Debug.LogError("[Route] could not build '" + routeId + "': " + e); Model = null; return false; }
        }

        static RouteNode[] ResampleNodes(RouteModel m)
        {
            var main = m.Main; var list = new List<RouteNode>(); float L = main.Length;
            for (float s = 0f; ; s += NodeSpacing)
            {
                float at = Mathf.Min(s, L); var p = main.Spline.PositionAt(at); var run = main.RunAt(Mathf.Min(at + 0.01f, L));
                list.Add(new RouteNode
                {
                    position = new Vector3(p.X, p.Y, p.Z), width = run.Section.CarriagewayHalfWidth * 2f,
                    zone = (ZoneType)Enum.Parse(typeof(ZoneType), run.Zone.ToString()), surface = (RoadSurface)Enum.Parse(typeof(RoadSurface), run.Surface.ToString())
                });
                if (at >= L) break;
            }
            return list.ToArray();
        }

        /// <summary>Height a vehicle or marker stands on at (x, z): road surface on the road, terrain elsewhere (0 without route data).</summary>
        public float GroundY(float x, float z) => Model != null ? Model.GroundHeight(x, z) : 0f;

        /// <summary>Lateral offset of the centre of a driving lane on the right of the direction of travel at polyline distance s. laneHash picks the lane on multi-lane roads.</summary>
        public float LaneOffset(float s, int laneHash)
        {
            if (Model == null) return 3.2f;
            float ms = Mathf.Clamp(s * Model.Length / Mathf.Max(1f, TotalLength), 0f, Model.Length);
            var run = Model.Main.RunAt(ms); var sec = run.Section; float k = Model.Main.ScaleAt(ms);
            int lanes = sec.Spec.lanesPerDirection; float first = sec.CarriagewayHalfWidth - lanes * sec.Spec.laneWidth;
            return (first + (Mathf.Abs(laneHash) % lanes + 0.5f) * sec.Spec.laneWidth) * k;
        }

        float[] _cum;   // cumulative length at each node (built lazily; invalidated by Rebuild)
        public void Rebuild()
        {
            _cum = new float[nodes.Length];
            for (int i = 1; i < nodes.Length; i++) _cum[i] = _cum[i - 1] + Vector3.Distance(nodes[i - 1].position, nodes[i].position);
        }
        float[] Cum { get { if (_cum == null || _cum.Length != nodes.Length) Rebuild(); return _cum; } }
        public float TotalLength => nodes.Length < 2 ? 0f : Cum[nodes.Length - 1];

        /// <summary>Segment index i (between node i and i+1) containing route distance s, plus the fraction along it.</summary>
        public int SegmentAt(float s, out float t)
        {
            var c = Cum; s = Mathf.Clamp(s, 0f, TotalLength);
            int i = 0;
            while (i < nodes.Length - 2 && s > c[i + 1]) i++;
            float len = Mathf.Max(0.0001f, c[i + 1] - c[i]);
            t = Mathf.Clamp01((s - c[i]) / len); return i;
        }

        public Vector3 PositionAt(float s) { int i = SegmentAt(s, out float t); return Vector3.Lerp(nodes[i].position, nodes[i + 1].position, t); }
        public Vector3 TangentAt(float s) { int i = SegmentAt(s, out _); return (nodes[i + 1].position - nodes[i].position).normalized; }

        /// <summary>Nearest route distance to a world point, and the signed lateral offset (positive = right of travel direction).</summary>
        public float Project(Vector3 p, out float lateral)
        {
            var c = Cum; float best = float.MaxValue, bestS = 0f; lateral = 0f;
            for (int i = 0; i < nodes.Length - 1; i++)
            {
                Vector3 a = nodes[i].position, b = nodes[i + 1].position, ab = b - a; ab.y = 0;
                float len2 = Mathf.Max(0.0001f, ab.sqrMagnitude);
                float t = Mathf.Clamp01(Vector3.Dot(new Vector3(p.x - a.x, 0, p.z - a.z), ab) / len2);
                Vector3 q = a + (b - a) * t; float d = (new Vector3(p.x - q.x, 0, p.z - q.z)).sqrMagnitude;
                if (d < best)
                {
                    best = d; bestS = c[i] + t * Mathf.Sqrt(len2);
                    Vector3 right = Vector3.Cross(Vector3.up, ab.normalized);
                    lateral = Vector3.Dot(new Vector3(p.x - q.x, 0, p.z - q.z), right);
                }
            }
            return bestS;
        }

        public float LengthMetres()
        {
            float d = 0f;
            for (int i = 1; i < nodes.Length; i++) d += Vector3.Distance(nodes[i - 1].position, nodes[i].position);
            return d;
        }
    }
}
