using System;
using System.Collections.Generic;
using System.Linq;

namespace ARO.NetCore
{
    /// <summary>
    /// E4b: the road network of a route as a graph (docs/ROAD_ENGINE_E4.md). Nodes are the two ends of the main road, every junction station on it, and the far end of every side road; edges are the stretches
    /// of road between them (the main road is split at each junction, every side road is one edge). `Locate` answers "which road is this point on", `ShortestPath` finds a route between two nodes and
    /// `Sample` walks a path as points. Pure and deterministic. NOTHING in the game uses it yet: traffic, the minimap, job progress and `Main` keep their meaning until a decision is taken to wire them
    /// (that is a gameplay change).
    /// </summary>
    public sealed class RoadGraph
    {
        public sealed class Node { public string Id; public float X, Z; public string Corridor; public float S; public List<Edge> Edges = new List<Edge>(); }
        public sealed class Edge { public string Id; public RoadCorridor Corridor; public float S0, S1, Lead; public Node A, B; public float Length => S1 - S0 + Lead; }       // Lead: a side road starts at the paved edge of the main road; the connector from the junction node on the centre line to there
        public struct Step { public Edge Edge; public bool Forward; }
        public struct PathPoint { public float X, Z, Y; public string Corridor; public float S; }

        public IReadOnlyList<Node> Nodes => _nodes;
        public IReadOnlyList<Edge> Edges => _edges;
        readonly List<Node> _nodes = new List<Node>(); readonly List<Edge> _edges = new List<Edge>();
        readonly Dictionary<string, Node> _byId = new Dictionary<string, Node>();
        RouteModel _model;

        public Node NodeById(string id) => _byId.TryGetValue(id, out var n) ? n : null;

        public static RoadGraph Build(RouteModel m)
        {
            var g = new RoadGraph { _model = m };
            var main = m.Main;
            // junction stations on the main road (a four-way is one station with two stubs)
            var stations = new SortedDictionary<float, List<RoadCorridor>>();
            foreach (var side in m.Sides)
            {
                main.Spline.Nearest(side.Spline.PositionAt(0f).X, side.Spline.PositionAt(0f).Z, 1000f, out float s, out _, out _);
                float key = (float)Math.Round(s, 1);
                if (!stations.TryGetValue(key, out var list)) stations[key] = list = new List<RoadCorridor>();
                list.Add(side);
            }
            Node Add(string id, float x, float z, string cor, float s) { var n = new Node { Id = id, X = x, Z = z, Corridor = cor, S = s }; g._nodes.Add(n); g._byId[id] = n; return n; }
            void Link(string id, RoadCorridor c, float s0, float s1, Node a, Node b) { var e = new Edge { Id = id, Corridor = c, S0 = s0, S1 = s1, A = a, B = b }; g._edges.Add(e); a.Edges.Add(e); b.Edges.Add(e); }

            var p0 = main.Spline.PositionAt(0f); var prev = Add("main:start", p0.X, p0.Z, main.Id, 0f); float prevS = 0f; int seg = 0;
            foreach (var kv in stations)
            {
                var pos = main.Spline.PositionAt(kv.Key);
                var node = Add("junction:" + kv.Key.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture), pos.X, pos.Z, main.Id, kv.Key);
                Link("main:" + seg++, main, prevS, kv.Key, prev, node);
                foreach (var stub in kv.Value)
                {
                    var e = stub.Spline.PositionAt(stub.Length);
                    var end = Add("end:" + stub.Id, e.X, e.Z, stub.Id, stub.Length);
                    Link("side:" + stub.Id, stub, 0f, stub.Length, node, end);
                    var sp = stub.Spline.PositionAt(0f); g._edges[g._edges.Count - 1].Lead = (float)Math.Sqrt((sp.X - pos.X) * (sp.X - pos.X) + (sp.Z - pos.Z) * (sp.Z - pos.Z));
                }
                prev = node; prevS = kv.Key;
            }
            var pe = main.Spline.PositionAt(main.Length); var last = Add("main:end", pe.X, pe.Z, main.Id, main.Length);
            Link("main:" + seg, main, prevS, main.Length, prev, last);
            return g;
        }

        /// <summary>The edge whose road is nearest to (x, z) within `maxDist`, with the station on that corridor and the signed lateral offset.</summary>
        public bool Locate(float x, float z, float maxDist, out Edge edge, out float s, out float lateral)
        {
            edge = null; s = 0f; lateral = 0f; float best = float.MaxValue;
            foreach (var c in _model.CorridorsNear(x, z))
            {
                if (!c.Spline.Nearest(x, z, maxDist, out float cs, out float lat, out float dist)) continue;
                if (dist >= best) continue;
                var e = EdgeAt(c, cs); if (e == null) continue;
                best = dist; edge = e; s = cs; lateral = lat;
            }
            return edge != null;
        }

        Edge EdgeAt(RoadCorridor c, float s)
        {
            Edge hit = null;
            foreach (var e in _edges)
                if (e.Corridor == c && s >= e.S0 - 1e-3f && s <= e.S1 + 1e-3f) { hit = e; if (s < e.S1 - 1e-3f) break; }      // on a node the earlier edge is returned unless s is strictly inside a later one
            return hit;
        }

        /// <summary>Shortest path by length between two nodes (Dijkstra). Null when there is none.</summary>
        public List<Step> ShortestPath(Node from, Node to)
        {
            if (from == null || to == null) return null;
            var dist = new Dictionary<Node, float> { [from] = 0f }; var prev = new Dictionary<Node, Step>(); var prevNode = new Dictionary<Node, Node>();
            var open = new List<Node> { from }; var done = new HashSet<Node>();
            while (open.Count > 0)
            {
                Node cur = open[0]; foreach (var n in open) if (dist[n] < dist[cur] || (dist[n] == dist[cur] && string.CompareOrdinal(n.Id, cur.Id) < 0)) cur = n;
                open.Remove(cur); if (!done.Add(cur)) continue;
                if (cur == to) break;
                foreach (var e in cur.Edges)
                {
                    var nb = e.A == cur ? e.B : e.A; if (done.Contains(nb)) continue;
                    float d = dist[cur] + e.Length;
                    if (!dist.TryGetValue(nb, out float old) || d < old - 1e-4f) { dist[nb] = d; prev[nb] = new Step { Edge = e, Forward = e.A == cur }; prevNode[nb] = cur; if (!open.Contains(nb)) open.Add(nb); }
                }
            }
            if (!prev.ContainsKey(to) && from != to) return null;
            var path = new List<Step>(); for (var n = to; n != from; n = prevNode[n]) path.Add(prev[n]);
            path.Reverse(); return path;
        }

        public static float Length(IEnumerable<Step> path) => path.Sum(s => s.Edge.Length);

        /// <summary>Points along a path every `step` metres (and at every node), in travel order, each with its corridor and station. A side road is entered from, or left towards, its junction node on the main road's centre line.</summary>
        public List<PathPoint> Sample(IEnumerable<Step> path, float step)
        {
            var res = new List<PathPoint>();
            foreach (var st in path)
            {
                var c = st.Edge.Corridor; float L = st.Edge.S1 - st.Edge.S0; int n = Math.Max(1, (int)Math.Ceiling(L / step));
                var pts = new List<PathPoint>();
                for (int i = 0; i <= n; i++)
                {
                    float t = (float)i / n; float s = st.Edge.S0 + L * t; var p = c.Spline.PositionAt(s);
                    pts.Add(new PathPoint { X = p.X, Y = p.Y, Z = p.Z, Corridor = c.Id, S = s });
                }
                if (st.Edge.Lead > 0f)                                      // the connector between the junction node and the start of the side road
                {
                    var nd = st.Edge.A; var p0 = c.Spline.PositionAt(0f);
                    pts.Insert(0, new PathPoint { X = nd.X, Y = p0.Y, Z = nd.Z, Corridor = c.Id, S = 0f });
                }
                if (!st.Forward) pts.Reverse();
                for (int i = 0; i < pts.Count; i++) { if (i == 0 && res.Count > 0) continue; res.Add(pts[i]); }          // the first point is the node the previous edge ended on
            }
            return res;
        }
    }
}
