using System;

namespace ARO.NetCore
{
    /// <summary>Material slots of a prop mesh. The Unity layer maps each slot to a material (dev palette, or the production prefab's own materials).</summary>
    public static class PropSub
    {
        public const int Wall = 0, Roof = 1, Foliage = 2, Trunk = 3, Metal = 4, SignPanel = 5, Accent = 6, Glass = 7, Count = 8;
    }

    /// <summary>
    /// DEVELOPMENT PLACEHOLDER geometry for catalog props, generated from the catalog entry (real-world dimensions and LOD levels), so the
    /// scatter, batching, LOD and culling systems can be validated with believable silhouettes and counts. Everything here is replaced, prop by
    /// prop, by production models at Resources/Props/{id} with no change to the systems that consume it. Never ship this as final art.
    /// </summary>
    public static class DevPropGeometry
    {
        /// <summary>Builds the mesh for prop `def` at LOD `lod` (0 = most detailed; clamped to the catalog's level count). Base sits at y = 0, centred on x = z = 0, front faces +z.</summary>
        public static MeshBuffers Build(PropDef def, int lod)
        {
            int levels = def.lodTriangles != null ? def.lodTriangles.Length : 1;
            lod = Math.Max(0, Math.Min(levels - 1, lod));
            int detail = lod == 0 ? 2 : lod == 1 ? 1 : 0;
            var mb = new MeshBuffers(PropSub.Count);
            float h = def.heightM, w = def.widthM, d = def.depthM;
            switch (def.id)
            {
                case "palm_tree": Palm(mb, h, w, detail); break;
                case "tropical_tree": Tree(mb, h, w, detail); break;
                case "bush": Bush(mb, h, w, detail); break;
                case "concrete_house": House(mb, w, d, h, detail); break;
                case "apartment_block": Block(mb, w, d, h, detail); break;
                case "shop_row": ShopRow(mb, w, d, h, detail); break;
                case "kiosk": Kiosk(mb, w, d, h, detail); break;
                case "market_stall": Stall(mb, w, d, h, detail); break;
                case "warehouse": Warehouse(mb, w, d, h, detail); break;
                case "fuel_station": FuelStation(mb, w, d, h, detail); break;
                case "bus_stop": BusStop(mb, w, d, h, detail); break;
                case "utility_pole": Pole(mb, h, 0.2f, detail); Box(mb, PropSub.Trunk, 0, h - 0.7f, 0, 2.2f, 0.12f, 0.12f); break;
                case "street_light": StreetLight(mb, h, w, detail); break;
                case "road_sign": Sign(mb, w, h, d, detail); break;
                case "billboard": Billboard(mb, w, h, d, detail); break;
                case "telecom_mast": Mast(mb, h, w, detail); break;
                default: Box(mb, PropSub.Wall, 0, 0, 0, w, h, d); break;
            }
            return mb;
        }

        // ------------------------------------------------------------------------------------------------------ vegetation
        static void Palm(MeshBuffers mb, float h, float w, int detail)
        {
            int sides = detail == 2 ? 8 : detail == 1 ? 6 : 4, rings = detail == 2 ? 5 : detail == 1 ? 3 : 1;
            float trunkH = h * 0.78f, r0 = w * 0.05f, r1 = w * 0.028f;
            for (int i = 0; i < rings; i++)
            {
                float y0 = trunkH * i / rings, y1 = trunkH * (i + 1) / rings;
                float lean0 = 0.35f * (y0 / trunkH) * (y0 / trunkH), lean1 = 0.35f * (y1 / trunkH) * (y1 / trunkH);
                Frustum(mb, PropSub.Trunk, lean0, 0f, y0, Lerp(r0, r1, y0 / trunkH), lean1, 0f, y1, Lerp(r0, r1, y1 / trunkH), sides);
            }
            float cx = 0.35f, cy = trunkH;
            int fronds = detail == 2 ? 10 : detail == 1 ? 7 : 4, segs = detail == 2 ? 3 : detail == 1 ? 2 : 1;
            float len = w * 0.55f;
            for (int f = 0; f < fronds; f++)
            {
                float a = f * 2f * (float)Math.PI / fronds + 0.3f * (f % 2);
                float dx = (float)Math.Cos(a), dz = (float)Math.Sin(a);
                for (int s = 0; s < segs; s++)
                {
                    float t0 = s / (float)segs, t1 = (s + 1) / (float)segs;
                    V3 P(float t, float side) => new V3(cx + dx * len * t - dz * side * w * 0.05f * (1f - 0.7f * t), cy + len * 0.35f * (float)Math.Sin(Math.PI * t * 0.8) - len * 0.35f * t * t, dx == 0 && dz == 0 ? 0 : dz * len * t + dx * side * w * 0.05f * (1f - 0.7f * t));
                    var a0 = P(t0, -1f); var b0 = P(t0, 1f); var a1 = P(t1, -1f); var b1 = P(t1, 1f);
                    mb.QuadFacing(PropSub.Foliage, a0, b0, a1, b1, new V3(0, 1, 0), new V2(0, t0), new V2(1, t0), new V2(0, t1), new V2(1, t1));
                    mb.QuadFacing(PropSub.Foliage, a0, b0, a1, b1, new V3(0, -1, 0), new V2(0, t0), new V2(1, t0), new V2(0, t1), new V2(1, t1));
                }
            }
        }

        static void Tree(MeshBuffers mb, float h, float w, int detail)
        {
            int sides = detail == 2 ? 8 : 5; float trunkH = h * 0.4f;
            Frustum(mb, PropSub.Trunk, 0, 0, 0, w * 0.045f, 0, 0, trunkH, w * 0.03f, sides);
            int lon = detail == 2 ? 10 : detail == 1 ? 7 : 5, lat = detail == 2 ? 6 : detail == 1 ? 4 : 3;
            Ellipsoid(mb, PropSub.Foliage, 0, h * 0.62f, 0, w * 0.5f, h * 0.3f, w * 0.5f, lon, lat);
            if (detail >= 1) { Ellipsoid(mb, PropSub.Foliage, w * 0.22f, h * 0.5f, w * 0.1f, w * 0.3f, h * 0.2f, w * 0.3f, lon, lat); }
            if (detail == 2) Ellipsoid(mb, PropSub.Foliage, -w * 0.2f, h * 0.55f, -w * 0.12f, w * 0.28f, h * 0.2f, w * 0.28f, lon, lat);
        }

        static void Bush(MeshBuffers mb, float h, float w, int detail)
        {
            int lon = detail == 2 ? 8 : 5, lat = detail == 2 ? 5 : 3;
            Ellipsoid(mb, PropSub.Foliage, 0, h * 0.5f, 0, w * 0.5f, h * 0.5f, w * 0.5f, lon, lat);
            if (detail == 2) Ellipsoid(mb, PropSub.Foliage, w * 0.25f, h * 0.35f, w * 0.1f, w * 0.3f, h * 0.35f, w * 0.3f, lon, lat);
        }

        // ------------------------------------------------------------------------------------------------------ buildings
        static void House(MeshBuffers mb, float w, float d, float h, int detail)
        {
            float wallH = h * 0.7f;
            Box(mb, PropSub.Wall, 0, 0, 0, w, wallH, d);
            Hip(mb, PropSub.Roof, 0, wallH, 0, w * 1.08f, h - wallH, d * 1.08f);
            if (detail >= 1)
            {
                Slab(mb, PropSub.Accent, -w * 0.25f, 0.0f, d * 0.5f + 0.01f, w * 0.18f, wallH * 0.65f);          // door
                Slab(mb, PropSub.Glass, w * 0.2f, wallH * 0.35f, d * 0.5f + 0.01f, w * 0.2f, wallH * 0.3f);        // window
            }
            if (detail == 2) Slab(mb, PropSub.Glass, w * 0.5f + 0.01f, wallH * 0.35f, 0f, 0f, 0f, d * 0.2f, wallH * 0.3f, true);
        }

        static void Block(MeshBuffers mb, float w, float d, float h, int detail)
        {
            Box(mb, PropSub.Wall, 0, 0, 0, w, h, d);
            Box(mb, PropSub.Roof, 0, h, 0, w * 1.02f, 0.35f, d * 1.02f);
            if (detail == 0) return;
            int floors = Math.Max(2, (int)(h / 3.2f)), cols = Math.Max(2, (int)(w / 3f));
            if (detail == 1) { floors = Math.Min(floors, 3); }
            for (int f = 0; f < floors; f++)
                for (int c = 0; c < cols; c++)
                    Slab(mb, PropSub.Glass, -w * 0.5f + (c + 0.5f) * w / cols - 0.6f, 1.2f + f * (h - 2.4f) / floors, d * 0.5f + 0.01f, 1.2f, 1.3f);
        }

        static void ShopRow(MeshBuffers mb, float w, float d, float h, int detail)
        {
            Box(mb, PropSub.Wall, 0, 0, 0, w, h, d);
            Box(mb, PropSub.Roof, 0, h, 0, w * 1.02f, 0.25f, d * 1.02f);
            // awning: a sloped slab over the shop fronts
            Wedge(mb, PropSub.Accent, 0, h * 0.62f, d * 0.5f, w * 0.98f, 1.4f, 0.5f);
            if (detail == 0) return;
            int shops = Math.Max(2, (int)(w / 4.5f));
            for (int s = 0; s < shops; s++)
            {
                float cx = -w * 0.5f + (s + 0.5f) * w / shops;
                Slab(mb, PropSub.Glass, cx - 0.9f, 0.1f, d * 0.5f + 0.01f, 1.8f, h * 0.5f);
                if (detail == 2) Slab(mb, PropSub.SignPanel, cx - 1.1f, h * 0.7f, d * 0.5f + 0.02f, 2.2f, h * 0.2f);
            }
        }

        static void Kiosk(MeshBuffers mb, float w, float d, float h, int detail)
        {
            Box(mb, PropSub.Accent, 0, 0, 0, w, h * 0.85f, d);
            Box(mb, PropSub.Roof, 0, h * 0.85f, 0, w * 1.15f, h * 0.15f, d * 1.15f);
            if (detail == 2) Slab(mb, PropSub.Glass, -w * 0.3f, h * 0.35f, d * 0.5f + 0.01f, w * 0.6f, h * 0.3f);
        }

        static void Stall(MeshBuffers mb, float w, float d, float h, int detail)
        {
            float pr = 0.04f;
            foreach (int sx in new[] { -1, 1 }) foreach (int sz in new[] { -1, 1 }) Box(mb, PropSub.Metal, sx * (w * 0.5f - pr), 0, sz * (d * 0.5f - pr), pr * 2f, h, pr * 2f);
            Wedge(mb, PropSub.Accent, 0, h, 0, w * 1.1f, 0.5f, d * 1.1f);
            if (detail == 2) Box(mb, PropSub.Wall, 0, 0.7f, 0, w * 0.9f, 0.1f, d * 0.6f);
        }

        static void Warehouse(MeshBuffers mb, float w, float d, float h, int detail)
        {
            float wallH = h * 0.8f;
            Box(mb, PropSub.Wall, 0, 0, 0, w, wallH, d);
            Gable(mb, PropSub.Roof, 0, wallH, 0, w * 1.04f, h - wallH, d * 1.04f);
            if (detail < 2) return;
            int doors = Math.Max(2, (int)(w / 9f));
            for (int i = 0; i < doors; i++) Slab(mb, PropSub.Accent, -w * 0.5f + (i + 0.5f) * w / doors - 1.8f, 0f, d * 0.5f + 0.01f, 3.6f, 4.2f);
        }

        static void FuelStation(MeshBuffers mb, float w, float d, float h, int detail)
        {
            float canopyY = 5.2f, cw = w * 0.62f, cd = d * 0.55f;
            Box(mb, PropSub.Accent, w * 0.1f, canopyY, d * 0.1f, cw, 0.6f, cd);
            foreach (int sx in new[] { -1, 1 }) foreach (int sz in new[] { -1, 1 }) Box(mb, PropSub.Metal, w * 0.1f + sx * (cw * 0.5f - 0.5f), 0, d * 0.1f + sz * (cd * 0.5f - 0.5f), 0.4f, canopyY, 0.4f);
            Box(mb, PropSub.Wall, -w * 0.34f, 0, -d * 0.3f, w * 0.26f, 3.4f, d * 0.4f);
            Box(mb, PropSub.Roof, -w * 0.34f, 3.4f, -d * 0.3f, w * 0.28f, 0.3f, d * 0.42f);
            if (detail == 2)
            {
                Box(mb, PropSub.SignPanel, w * 0.42f, 0, -d * 0.4f, 0.6f, h, 1.2f);                    // price pylon
                foreach (int p in new[] { -1, 1 }) Box(mb, PropSub.Accent, w * 0.1f + p * 2.2f, 0, d * 0.1f, 0.7f, 1.5f, 0.5f);   // pumps
            }
        }

        static void BusStop(MeshBuffers mb, float w, float d, float h, int detail)
        {
            foreach (int sx in new[] { -1, 1 }) Box(mb, PropSub.Metal, sx * (w * 0.5f - 0.1f), 0, 0, 0.12f, h, 0.12f);
            Box(mb, PropSub.Roof, 0, h, 0, w * 1.1f, 0.15f, d);
            if (detail == 2) { Box(mb, PropSub.Trunk, 0, 0.45f, -d * 0.25f, w * 0.7f, 0.08f, 0.4f); Slab(mb, PropSub.Glass, -w * 0.5f + 0.2f, 0.4f, -d * 0.5f + 0.01f, w - 0.4f, h - 0.7f); }
        }

        static void Pole(MeshBuffers mb, float h, float r, int detail)
        {
            int sides = detail == 2 ? 6 : 4;
            Frustum(mb, PropSub.Trunk, 0, 0, 0, r, 0, 0, h, r * 0.6f, sides);
        }

        static void StreetLight(MeshBuffers mb, float h, float w, int detail)
        {
            Pole(mb, h, 0.12f, detail);
            Box(mb, PropSub.Metal, 0, h - 0.1f, w * 0.25f, 0.1f, 0.1f, w * 0.5f);
            Box(mb, PropSub.Accent, 0, h - 0.25f, w * 0.5f, 0.35f, 0.15f, 0.7f);
        }

        static void Sign(MeshBuffers mb, float w, float h, float d, int detail)
        {
            Box(mb, PropSub.Metal, -w * 0.38f, 0, 0, 0.1f, h, 0.1f);
            if (detail == 2) Box(mb, PropSub.Metal, w * 0.38f, 0, 0, 0.1f, h, 0.1f);
            float py = h - 1.3f;
            Box(mb, PropSub.SignPanel, 0, py, 0.08f, w, 1.4f, d);
        }

        static void Billboard(MeshBuffers mb, float w, float h, float d, int detail)
        {
            foreach (int sx in new[] { -1, 1 }) Box(mb, PropSub.Metal, sx * w * 0.3f, 0, 0, 0.35f, h - 3.2f, 0.35f);
            Box(mb, PropSub.SignPanel, 0, h - 4.2f, 0, w, 4.2f, d);
            if (detail == 2) Box(mb, PropSub.Metal, 0, h - 4.3f, 0, w * 1.02f, 0.1f, d * 1.5f);
        }

        static void Mast(MeshBuffers mb, float h, float w, int detail)
        {
            int legs = 3; float r = w * 0.5f;
            for (int l = 0; l < legs; l++)
            {
                float a = l * 2f * (float)Math.PI / legs;
                float x0 = (float)Math.Cos(a) * r, z0 = (float)Math.Sin(a) * r, x1 = x0 * 0.2f, z1 = z0 * 0.2f;
                Frustum(mb, PropSub.Metal, x0, z0, 0, 0.12f, x1, z1, h, 0.06f, detail == 2 ? 4 : 3);
            }
            int platforms = detail == 2 ? 4 : detail == 1 ? 2 : 0;
            for (int p = 1; p <= platforms; p++) Box(mb, PropSub.Accent, 0, h * p / (platforms + 1), 0, r * 1.2f * (1f - 0.7f * p / (platforms + 1f)), 0.25f, r * 1.2f * (1f - 0.7f * p / (platforms + 1f)));
        }

        // ------------------------------------------------------------------------------------------------------ primitives
        static float Lerp(float a, float b, float t) => a + (b - a) * t;

        /// <summary>Open-bottom box. (cx, y0, cz) is the centre of the base; faces wind outwards.</summary>
        public static void Box(MeshBuffers mb, int sub, float cx, float y0, float cz, float w, float h, float d)
            => RoadGeometry.AddBox(mb, sub, new V3(cx, y0, cz), w, h, d, new V3(0, 0, 1), new V3(1, 0, 0));

        /// <summary>A flat quad on the +z (or +x when sideFacing) face, x0..x0+w, y0..y0+h, slightly proud of the wall.</summary>
        static void Slab(MeshBuffers mb, int sub, float x0, float y0, float z, float w, float h) => Slab(mb, sub, x0, y0, z, 0f, 0f, w, h, false);
        static void Slab(MeshBuffers mb, int sub, float x0, float y0, float z, float _a, float _b, float w, float h, bool sideFacing)
        {
            if (!sideFacing)
                mb.QuadFacing(sub, new V3(x0, y0, z), new V3(x0 + w, y0, z), new V3(x0, y0 + h, z), new V3(x0 + w, y0 + h, z), new V3(0, 0, 1), new V2(0, 0), new V2(1, 0), new V2(0, 1), new V2(1, 1));
            else
                mb.QuadFacing(sub, new V3(x0, y0, z - w * 0.5f), new V3(x0, y0, z + w * 0.5f), new V3(x0, y0 + h, z - w * 0.5f), new V3(x0, y0 + h, z + w * 0.5f), new V3(1, 0, 0), new V2(0, 0), new V2(1, 0), new V2(0, 1), new V2(1, 1));
        }

        /// <summary>Hip roof: four sloped faces meeting at a ridge.</summary>
        static void Hip(MeshBuffers mb, int sub, float cx, float y0, float cz, float w, float h, float d)
        {
            var c00 = new V3(cx - w / 2, y0, cz - d / 2); var c10 = new V3(cx + w / 2, y0, cz - d / 2); var c01 = new V3(cx - w / 2, y0, cz + d / 2); var c11 = new V3(cx + w / 2, y0, cz + d / 2);
            var apex = new V3(cx, y0 + h, cz);
            Tri(mb, sub, c00, c10, apex, new V3(0, 0.5f, -1)); Tri(mb, sub, c11, c01, apex, new V3(0, 0.5f, 1));
            Tri(mb, sub, c10, c11, apex, new V3(1, 0.5f, 0)); Tri(mb, sub, c01, c00, apex, new V3(-1, 0.5f, 0));
        }

        /// <summary>Gable roof: two slopes plus two triangular end walls.</summary>
        static void Gable(MeshBuffers mb, int sub, float cx, float y0, float cz, float w, float h, float d)
        {
            float x0 = cx - w / 2, x1 = cx + w / 2, z0 = cz - d / 2, z1 = cz + d / 2, ry = y0 + h;
            mb.QuadFacing(sub, new V3(x0, y0, z0), new V3(x0, y0, z1), new V3(cx, ry, z0), new V3(cx, ry, z1), new V3(-1, 1, 0), new V2(0, 0), new V2(1, 0), new V2(0, 1), new V2(1, 1));
            mb.QuadFacing(sub, new V3(x1, y0, z0), new V3(x1, y0, z1), new V3(cx, ry, z0), new V3(cx, ry, z1), new V3(1, 1, 0), new V2(0, 0), new V2(1, 0), new V2(0, 1), new V2(1, 1));
            Tri(mb, PropSub.Wall, new V3(x0, y0, z0), new V3(x1, y0, z0), new V3(cx, ry, z0), new V3(0, 0, -1));
            Tri(mb, PropSub.Wall, new V3(x0, y0, z1), new V3(x1, y0, z1), new V3(cx, ry, z1), new V3(0, 0, 1));
        }

        /// <summary>A sloped slab (an awning): high edge against the wall at z = back, low edge at z + depth.</summary>
        static void Wedge(MeshBuffers mb, int sub, float cx, float y0, float zWall, float w, float drop, float depth)
        {
            float x0 = cx - w / 2, x1 = cx + w / 2;
            mb.QuadFacing(sub, new V3(x0, y0, zWall), new V3(x1, y0, zWall), new V3(x0, y0 - drop, zWall + depth), new V3(x1, y0 - drop, zWall + depth), new V3(0, 1, 0.5f), new V2(0, 0), new V2(1, 0), new V2(0, 1), new V2(1, 1));
            mb.QuadFacing(sub, new V3(x0, y0, zWall), new V3(x1, y0, zWall), new V3(x0, y0 - drop, zWall + depth), new V3(x1, y0 - drop, zWall + depth), new V3(0, -1, -0.5f), new V2(0, 0), new V2(1, 0), new V2(0, 1), new V2(1, 1));
        }

        static void Tri(MeshBuffers mb, int sub, V3 a, V3 b, V3 c, V3 outward)
        {
            int i = mb.Add(a, new V2(0, 0)), j = mb.Add(b, new V2(1, 0)), k = mb.Add(c, new V2(0.5f, 1));
            var n = MeshBuffers.Cross(b - a, c - a);
            if (MeshBuffers.Dot(n, outward) >= 0f) mb.Tri(sub, i, j, k); else mb.Tri(sub, i, k, j);
        }

        /// <summary>A tapered cylinder segment between two circles (centres (x,z) at heights y0/y1).</summary>
        static void Frustum(MeshBuffers mb, int sub, float x0, float z0, float y0, float r0, float x1, float z1, float y1, float r1, int sides)
        {
            for (int s = 0; s < sides; s++)
            {
                float a0 = s * 2f * (float)Math.PI / sides, a1 = (s + 1) * 2f * (float)Math.PI / sides;
                var p00 = new V3(x0 + (float)Math.Cos(a0) * r0, y0, z0 + (float)Math.Sin(a0) * r0); var p10 = new V3(x0 + (float)Math.Cos(a1) * r0, y0, z0 + (float)Math.Sin(a1) * r0);
                var p01 = new V3(x1 + (float)Math.Cos(a0) * r1, y1, z1 + (float)Math.Sin(a0) * r1); var p11 = new V3(x1 + (float)Math.Cos(a1) * r1, y1, z1 + (float)Math.Sin(a1) * r1);
                float am = (a0 + a1) * 0.5f;
                mb.QuadFacing(sub, p00, p10, p01, p11, new V3((float)Math.Cos(am), 0.05f, (float)Math.Sin(am)), new V2(s / (float)sides, 0), new V2((s + 1) / (float)sides, 0), new V2(s / (float)sides, 1), new V2((s + 1) / (float)sides, 1));
            }
        }

        static void Ellipsoid(MeshBuffers mb, int sub, float cx, float cy, float cz, float rx, float ry, float rz, int lon, int lat)
        {
            V3 P(int i, int j)
            {
                float th = (float)Math.PI * j / lat, ph = 2f * (float)Math.PI * i / lon;
                return new V3(cx + rx * (float)(Math.Sin(th) * Math.Cos(ph)), cy + ry * (float)Math.Cos(th), cz + rz * (float)(Math.Sin(th) * Math.Sin(ph)));
            }
            for (int j = 0; j < lat; j++)
                for (int i = 0; i < lon; i++)
                {
                    var a = P(i, j); var b = P(i + 1, j); var c = P(i, j + 1); var e = P(i + 1, j + 1);
                    var centre = new V3((a.X + b.X + c.X + e.X) / 4f - cx, (a.Y + b.Y + c.Y + e.Y) / 4f - cy, (a.Z + b.Z + c.Z + e.Z) / 4f - cz);
                    if (j == 0) Tri(mb, sub, a, c, e, centre);
                    else if (j == lat - 1) Tri(mb, sub, a, b, c, centre);
                    else mb.QuadFacing(sub, a, b, c, e, centre, new V2(0, 0), new V2(1, 0), new V2(0, 1), new V2(1, 1));
                }
        }
    }

}
