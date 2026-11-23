using System;
using System.Collections.Generic;

namespace ARO.NetCore
{
    /// <summary>Engine-neutral mesh under construction (positions, UVs, one triangle list per submesh). The Unity layer converts it to a Mesh.</summary>
    public sealed class MeshBuffers
    {
        public readonly List<V3> Vertices = new List<V3>();
        public readonly List<V2> Uvs = new List<V2>();
        public readonly List<int>[] Triangles;

        public MeshBuffers(int submeshes)
        {
            Triangles = new List<int>[submeshes];
            for (int i = 0; i < submeshes; i++) Triangles[i] = new List<int>();
        }

        public int VertexCount => Vertices.Count;
        public int TriangleCount { get { int n = 0; foreach (var t in Triangles) n += t.Count / 3; return n; } }
        public bool IsEmpty => TriangleCount == 0;

        public int Add(V3 p, V2 uv) { Vertices.Add(p); Uvs.Add(uv); return Vertices.Count - 1; }

        /// <summary>Appends another mesh: its vertices and UVs after these, its triangles (re-indexed) onto the same submesh slots. The result draws exactly what the two drew separately, in one mesh
        /// (RB-15: a chunk draws all its road surfaces with one renderer, one draw per material, instead of one renderer per corridor).</summary>
        public void Append(MeshBuffers o)
        {
            if (o.Triangles.Length > Triangles.Length) throw new ArgumentException("the appended mesh has more submeshes (" + o.Triangles.Length + ") than this one (" + Triangles.Length + ")");
            int baseIndex = Vertices.Count;
            Vertices.AddRange(o.Vertices); Uvs.AddRange(o.Uvs);
            for (int sub = 0; sub < o.Triangles.Length; sub++) { var t = Triangles[sub]; foreach (int i in o.Triangles[sub]) t.Add(baseIndex + i); }
        }
        public void Tri(int sub, int a, int b, int c) { var t = Triangles[sub]; t.Add(a); t.Add(b); t.Add(c); }

        /// <summary>
        /// One mesh from several (RB-15: every road surface of a chunk). The result has as many submesh slots as the largest part, and at least `minSubmeshes`, so parts may come in any order:
        /// a corridor without the E1 strips has 7 slots, one with them, a roundabout island or a site plate 12 (review run 68: the streamer sized the buffer from the first part, and a chunk
        /// whose first part was a plain road lost its whole road surface when a kerbed one followed). Empty parts are skipped. Null when there is nothing to draw.
        /// </summary>
        public static MeshBuffers Merge(IEnumerable<MeshBuffers> parts, int minSubmeshes = 1)
        {
            var list = new List<MeshBuffers>(); int subs = Math.Max(1, minSubmeshes);
            foreach (var p in parts) { if (p == null || p.IsEmpty) continue; list.Add(p); subs = Math.Max(subs, p.Triangles.Length); }
            if (list.Count == 0) return null;
            var all = new MeshBuffers(subs); foreach (var p in list) all.Append(p);
            return all;
        }

        /// <summary>Adds the standard quad pattern (a,c,b),(b,c,d) for corners a,b (near edge, left to right) and c,d (far edge).</summary>
        public void Quad(int sub, int a, int b, int c, int d) { Tri(sub, a, c, b); Tri(sub, b, c, d); }

        /// <summary>Adds a quad of four fresh vertices, choosing the winding so the face looks along `outward`.</summary>
        public void QuadFacing(int sub, V3 p0, V3 p1, V3 p2, V3 p3, V3 outward, V2 uv0, V2 uv1, V2 uv2, V2 uv3)
        {
            int a = Add(p0, uv0), b = Add(p1, uv1), c = Add(p2, uv2), d = Add(p3, uv3);
            var n = Cross(p2 - p0, p1 - p0);
            if (Dot(n, outward) >= 0f) Quad(sub, a, b, c, d);
            else { Tri(sub, a, b, c); Tri(sub, b, d, c); }
        }

        public static V3 Cross(V3 a, V3 b) => new V3(a.Y * b.Z - a.Z * b.Y, a.Z * b.X - a.X * b.Z, a.X * b.Y - a.Y * b.X);
        public static float Dot(V3 a, V3 b) => a.X * b.X + a.Y * b.Y + a.Z * b.Z;

        /// <summary>Geometric normal of triangle (i0,i1,i2) in Unity's convention.</summary>
        public V3 TriangleNormal(int i0, int i1, int i2) => Cross(Vertices[i1] - Vertices[i0], Vertices[i2] - Vertices[i0]);

        /// <summary>Every vertex finite, every index in range, no degenerate index triples. Returns false with a reason.</summary>
        public bool Validate(out string error)
        {
            for (int i = 0; i < Vertices.Count; i++) if (!Vertices[i].IsFinite) { error = "vertex " + i + " is not finite: " + Vertices[i]; return false; }
            if (Uvs.Count != Vertices.Count) { error = "uv count differs from vertex count"; return false; }
            for (int s = 0; s < Triangles.Length; s++)
            {
                var t = Triangles[s];
                if (t.Count % 3 != 0) { error = "submesh " + s + " index count is not a multiple of 3"; return false; }
                for (int i = 0; i < t.Count; i++) if (t[i] < 0 || t[i] >= Vertices.Count) { error = "submesh " + s + " index " + t[i] + " out of range"; return false; }
                for (int i = 0; i < t.Count; i += 3) if (t[i] == t[i + 1] || t[i + 1] == t[i + 2] || t[i] == t[i + 2]) { error = "submesh " + s + " has a degenerate triangle"; return false; }
            }
            error = null; return true;
        }

        public void MinMax(out V3 min, out V3 max)
        {
            min = new V3(float.MaxValue, float.MaxValue, float.MaxValue); max = new V3(float.MinValue, float.MinValue, float.MinValue);
            foreach (var v in Vertices)
            {
                min = new V3(Math.Min(min.X, v.X), Math.Min(min.Y, v.Y), Math.Min(min.Z, v.Z));
                max = new V3(Math.Max(max.X, v.X), Math.Max(max.Y, v.Y), Math.Max(max.Z, v.Z));
            }
        }
    }
}
