using System;
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
