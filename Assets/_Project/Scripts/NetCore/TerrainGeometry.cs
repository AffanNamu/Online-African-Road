using System;

namespace ARO.NetCore
{
    public static class TerrainSub { public const int Ground = 0, Count = 1; }

    /// <summary>Terrain tiles from a TerrainModel: a visual tile (cells under the road are cut out, edges get skirts so neighbouring LODs never show cracks) and a thick collision slab.</summary>
    public static class TerrainGeometry
    {
        public const float TileMetres = 16f;      // ground texture tile
        public const float SkirtDepth = 2.5f;

        /// <summary>Visual tile covering [x0, x0+size] x [z0, z0+size] with cells x cells quads, vertices relative to origin.</summary>
        public static MeshBuffers BuildTile(TerrainModel t, float x0, float z0, float size, int cells, V3 origin, bool cutRoad = true, bool skirts = true)
        {
            if (cells < 1) throw new ArgumentException("cells must be >= 1");
            var mb = new MeshBuffers(TerrainSub.Count);
            int n = cells + 1; float step = size / cells;
            var h = new float[n, n]; var inside = new bool[n, n];
            for (int i = 0; i < n; i++)
                for (int j = 0; j < n; j++)
                {
                    float x = x0 + i * step, z = z0 + j * step;
                    h[i, j] = t.HeightAt(x, z);
                    inside[i, j] = cutRoad && t.InsideFormation(x, z, -1.0f);
                }
            var idx = new int[n, n];
            for (int i = 0; i < n; i++)
                for (int j = 0; j < n; j++)
                {
                    float x = x0 + i * step, z = z0 + j * step;
                    idx[i, j] = mb.Add(new V3(x - origin.X, h[i, j] - origin.Y, z - origin.Z), new V2(x / TileMetres, z / TileMetres));
                }
            for (int i = 0; i < cells; i++)
                for (int j = 0; j < cells; j++)
                {
                    if (inside[i, j] && inside[i + 1, j] && inside[i, j + 1] && inside[i + 1, j + 1]) continue;      // covered by the road
                    mb.Quad(TerrainSub.Ground, idx[i, j], idx[i + 1, j], idx[i, j + 1], idx[i + 1, j + 1]);
                }
            if (skirts) AddSkirts(mb, x0, z0, size, cells, h, origin, SkirtDepth, null);
            return mb;
        }

        /// <summary>Closed slab: terrain surface, four walls and a flat underside `thickness` below the lowest point. Mesh colliders on a slab cannot be tunnelled by heavy bodies.</summary>
        public static MeshBuffers BuildCollision(TerrainModel t, float x0, float z0, float size, int cells, V3 origin, float thickness = 3f)
        {
            var mb = new MeshBuffers(TerrainSub.Count);
            int n = cells + 1; float step = size / cells, minY = float.MaxValue;
            var h = new float[n, n];
            for (int i = 0; i < n; i++) for (int j = 0; j < n; j++) { h[i, j] = t.HeightAt(x0 + i * step, z0 + j * step); minY = Math.Min(minY, h[i, j]); }
            var idx = new int[n, n];
            for (int i = 0; i < n; i++) for (int j = 0; j < n; j++) idx[i, j] = mb.Add(new V3(x0 + i * step - origin.X, h[i, j] - origin.Y, z0 + j * step - origin.Z), new V2(0, 0));
            for (int i = 0; i < cells; i++) for (int j = 0; j < cells; j++) mb.Quad(TerrainSub.Ground, idx[i, j], idx[i + 1, j], idx[i, j + 1], idx[i + 1, j + 1]);
            float bottom = minY - thickness;
            AddSkirts(mb, x0, z0, size, cells, h, origin, 0f, bottom);
            // Underside, facing down.
            mb.QuadFacing(TerrainSub.Ground,
                new V3(x0 - origin.X, bottom - origin.Y, z0 - origin.Z), new V3(x0 + size - origin.X, bottom - origin.Y, z0 - origin.Z),
                new V3(x0 - origin.X, bottom - origin.Y, z0 + size - origin.Z), new V3(x0 + size - origin.X, bottom - origin.Y, z0 + size - origin.Z),
                new V3(0, -1, 0), new V2(0, 0), new V2(1, 0), new V2(0, 1), new V2(1, 1));
            return mb;
        }

        // Skirts: a vertical flap down each border edge (fixed depth, or to a fixed absolute height `bottom`).
        static void AddSkirts(MeshBuffers mb, float x0, float z0, float size, int cells, float[,] h, V3 origin, float depth, float? bottom)
        {
            float step = size / cells;
            void Edge(int i0, int j0, int i1, int j1, V3 outward)
            {
                float xa = x0 + i0 * step, za = z0 + j0 * step, xb = x0 + i1 * step, zb = z0 + j1 * step;
                float ya = h[i0, j0], yb = h[i1, j1];
                float ba = bottom ?? ya - depth, bb = bottom ?? yb - depth;
                mb.QuadFacing(TerrainSub.Ground,
                    new V3(xa - origin.X, ya - origin.Y, za - origin.Z), new V3(xb - origin.X, yb - origin.Y, zb - origin.Z),
                    new V3(xa - origin.X, ba - origin.Y, za - origin.Z), new V3(xb - origin.X, bb - origin.Y, zb - origin.Z),
                    outward, new V2(xa / TileMetres, za / TileMetres), new V2(xb / TileMetres, zb / TileMetres), new V2(xa / TileMetres, za / TileMetres), new V2(xb / TileMetres, zb / TileMetres));
            }
            for (int i = 0; i < cells; i++)
            {
                Edge(i, 0, i + 1, 0, new V3(0, 0, -1));            // south
                Edge(i, cells, i + 1, cells, new V3(0, 0, 1));     // north
            }
            for (int j = 0; j < cells; j++)
            {
                Edge(0, j, 0, j + 1, new V3(-1, 0, 0));            // west
                Edge(cells, j, cells, j + 1, new V3(1, 0, 0));     // east
            }
        }
    }
}
