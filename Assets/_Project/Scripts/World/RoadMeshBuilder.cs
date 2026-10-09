using System.Collections.Generic;
using UnityEngine;

namespace ARO.World
{
    /// <summary>Builds a road ribbon (plus verge) mesh for one segment, with centre-line UVs for markings.</summary>
    public static class RoadMeshBuilder
    {
        const float SubdivisionMetres = 10f;

        public static Mesh BuildSegment(RouteNode a, RouteNode b, Vector3 chunkOrigin)
        {
            var verts = new List<Vector3>(); var uvs = new List<Vector2>(); var tris = new List<int>();
            Vector3 dir = (b.position - a.position); float len = dir.magnitude;
            if (len < 0.01f) return null;
            dir /= len;
            Vector3 right = Vector3.Cross(Vector3.up, dir).normalized;
            int steps = Mathf.Max(1, Mathf.CeilToInt(len / SubdivisionMetres));
            for (int i = 0; i <= steps; i++)
            {
                float t = i / (float)steps;
                Vector3 c = Vector3.Lerp(a.position, b.position, t) - chunkOrigin;
                float w = Mathf.Lerp(a.width, b.width, t) * 0.5f;
                float v = t * len / 10f;
                // Damaged roads get a little deterministic vertical noise (potholes read as bumps).
                float bump = a.surface == RoadSurface.Damaged ? (Mathf.PerlinNoise(c.x * 0.3f, c.z * 0.3f) - 0.5f) * 0.12f : 0f;
                verts.Add(c - right * w + Vector3.up * (0.02f + bump)); uvs.Add(new Vector2(0f, v));
                verts.Add(c + right * w + Vector3.up * (0.02f + bump)); uvs.Add(new Vector2(1f, v));
            }
            for (int i = 0; i < steps; i++)
            {
                int k = i * 2;
                tris.AddRange(new[] { k, k + 2, k + 1, k + 1, k + 2, k + 3 });
            }
            var m = new Mesh { name = "RoadSegment" };
            m.SetVertices(verts); m.SetUVs(0, uvs); m.SetTriangles(tris, 0);
            m.RecalculateNormals(); m.RecalculateBounds();
            return m;
        }
    }
}
