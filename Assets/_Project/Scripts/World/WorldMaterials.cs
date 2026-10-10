using ARO.NetCore;
using UnityEngine;

namespace ARO.World
{
    /// <summary>
    /// Materials for the generated world. Everything derives from one URP Lit template (so the shader is guaranteed to be in the player build)
    /// and uses small PROCEDURAL tiling textures (no external art). These are development materials: production PBR sets replace the textures
    /// without any code change. Texture memory: about 0.7 MB asphalt, 0.7 MB per terrain set, the rest below 0.25 MB each.
    /// </summary>
    public sealed class WorldMaterials
    {
        public readonly Material[] Road = new Material[RoadSub.Count];
        public readonly Material[] Prop = new Material[PropSub.Count];
        public Material TerrainLush, TerrainDry;

        public Material[] WeatherSet => new[] { Road[RoadSub.Asphalt], Road[RoadSub.Shoulder] };

        public static WorldMaterials Create(Material template)
        {
            Shader fallback = Shader.Find("Universal Render Pipeline/Lit");
            Material Make(string name, Color col, float smooth, float metal, Texture2D tex)
            {
                var m = template != null ? new Material(template) : new Material(fallback);
                m.name = "ARO_" + name; m.enableInstancing = true;
                if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", col); else m.color = col;
                if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", smooth);
                if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", metal);
                if (tex != null) { if (m.HasProperty("_BaseMap")) m.SetTexture("_BaseMap", tex); else m.mainTexture = tex; }
                return m;
            }
            var w = new WorldMaterials();
            w.Road[RoadSub.Asphalt] = Make("Asphalt", Color.white, 0.18f, 0f, ProcTex.Grain(256, new Color(0.15f, 0.15f, 0.16f), new Color(0.24f, 0.24f, 0.25f), 3, 11, 0.07f));
            w.Road[RoadSub.Shoulder] = Make("Shoulder", Color.white, 0.12f, 0f, ProcTex.Grain(128, new Color(0.30f, 0.29f, 0.27f), new Color(0.40f, 0.38f, 0.34f), 3, 12, 0.06f));
            w.Road[RoadSub.Verge] = Make("Verge", Color.white, 0.05f, 0f, ProcTex.Grain(128, new Color(0.40f, 0.27f, 0.17f), new Color(0.52f, 0.37f, 0.23f), 4, 13, 0.08f));
            w.Road[RoadSub.Concrete] = Make("Concrete", Color.white, 0.10f, 0f, ProcTex.Grain(128, new Color(0.58f, 0.58f, 0.56f), new Color(0.72f, 0.72f, 0.70f), 3, 14, 0.04f));
            w.Road[RoadSub.MarkWhite] = Make("MarkWhite", new Color(0.92f, 0.92f, 0.88f), 0.25f, 0f, null);
            w.Road[RoadSub.MarkYellow] = Make("MarkYellow", new Color(0.95f, 0.72f, 0.10f), 0.25f, 0f, null);
            w.Road[RoadSub.Metal] = Make("GuardrailMetal", new Color(0.62f, 0.64f, 0.66f), 0.55f, 0.6f, null);
            w.TerrainLush = Make("TerrainLush", Color.white, 0.04f, 0f, ProcTex.Patchy(256, new Color(0.16f, 0.28f, 0.09f), new Color(0.30f, 0.40f, 0.13f), new Color(0.36f, 0.27f, 0.16f), 21));
            w.TerrainDry = Make("TerrainDry", Color.white, 0.04f, 0f, ProcTex.Patchy(256, new Color(0.46f, 0.35f, 0.22f), new Color(0.58f, 0.46f, 0.30f), new Color(0.30f, 0.36f, 0.14f), 22));
            w.Prop[PropSub.Wall] = Make("PropWall", new Color(0.80f, 0.74f, 0.64f), 0.08f, 0f, null);
            w.Prop[PropSub.Roof] = Make("PropRoof", new Color(0.46f, 0.30f, 0.24f), 0.30f, 0.25f, null);
            w.Prop[PropSub.Foliage] = Make("PropFoliage", new Color(0.13f, 0.31f, 0.09f), 0.05f, 0f, null);
            w.Prop[PropSub.Trunk] = Make("PropTrunk", new Color(0.36f, 0.26f, 0.17f), 0.05f, 0f, null);
            w.Prop[PropSub.Metal] = Make("PropMetal", new Color(0.50f, 0.52f, 0.55f), 0.45f, 0.5f, null);
            w.Prop[PropSub.SignPanel] = Make("PropSignPanel", new Color(0.03f, 0.34f, 0.19f), 0.20f, 0f, null);
            w.Prop[PropSub.Accent] = Make("PropAccent", new Color(0.86f, 0.55f, 0.10f), 0.20f, 0f, null);
            w.Prop[PropSub.Glass] = Make("PropGlass", new Color(0.10f, 0.14f, 0.20f), 0.90f, 0f, null);
            return w;
        }

        public void DestroyAll()
        {
            foreach (var m in Road) if (m != null) Object.Destroy(m);
            foreach (var m in Prop) if (m != null) Object.Destroy(m);
            if (TerrainLush != null) Object.Destroy(TerrainLush); if (TerrainDry != null) Object.Destroy(TerrainDry);
        }
    }

    /// <summary>Small seamless procedural textures (periodic value noise). Development art only.</summary>
    public static class ProcTex
    {
        static float Hash(int seed, int x, int y)
        {
            unchecked { uint h = (uint)(seed * 374761393 + x * 668265263 + y * 2147483647); h = (h ^ (h >> 13)) * 1274126177u; h ^= h >> 16; return (h & 0xFFFF) / 65535f; }
        }

        // Periodic value noise: `cells` lattice cells wrap around the texture, so it tiles without seams.
        static float Noise(float u, float v, int cells, int seed)
        {
            float x = u * cells, y = v * cells; int x0 = Mathf.FloorToInt(x), y0 = Mathf.FloorToInt(y); float fx = x - x0, fy = y - y0;
            fx = fx * fx * (3f - 2f * fx); fy = fy * fy * (3f - 2f * fy);
            int xa = ((x0 % cells) + cells) % cells, xb = (xa + 1) % cells, ya = ((y0 % cells) + cells) % cells, yb = (ya + 1) % cells;
            float a = Hash(seed, xa, ya), b = Hash(seed, xb, ya), c = Hash(seed, xa, yb), d = Hash(seed, xb, yb);
            return Mathf.Lerp(Mathf.Lerp(a, b, fx), Mathf.Lerp(c, d, fx), fy);
        }

        static float Fbm(float u, float v, int baseCells, int octaves, int seed)
        {
            float sum = 0f, amp = 0.5f, norm = 0f; int cells = baseCells;
            for (int o = 0; o < octaves; o++) { sum += amp * Noise(u, v, cells, seed + o * 31); norm += amp; amp *= 0.5f; cells *= 2; }
            return sum / norm;
        }

        static Texture2D Finish(Color32[] px, int size, string name)
        {
            var t = new Texture2D(size, size, TextureFormat.RGBA32, true, false) { name = name, wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Trilinear, anisoLevel = 4 };
            t.SetPixels32(px); t.Apply(true, false); return t;
        }

        /// <summary>Fine grain between two colours (asphalt, dust, concrete).</summary>
        public static Texture2D Grain(int size, Color a, Color b, int baseCells, int seed, float speckle)
        {
            var px = new Color32[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float u = x / (float)size, v = y / (float)size;
                    float n = Fbm(u, v, baseCells * 4, 4, seed);
                    float g = Hash(seed + 99, x, y);
                    var c = Color.Lerp(a, b, n); float s = (g - 0.5f) * speckle * 2f;
                    px[y * size + x] = new Color32(Cb(c.r + s), Cb(c.g + s), Cb(c.b + s), 255);
                }
            return Finish(px, size, "ProcGrain" + seed);
        }

        /// <summary>Large soft patches of a second colour over a two-colour base (grass and soil).</summary>
        public static Texture2D Patchy(int size, Color a, Color b, Color patch, int seed)
        {
            var px = new Color32[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float u = x / (float)size, v = y / (float)size;
                    var c = Color.Lerp(a, b, Fbm(u, v, 6, 4, seed));
                    float p = Mathf.Clamp01((Fbm(u, v, 3, 3, seed + 500) - 0.55f) / 0.25f); p = p * p * (3f - 2f * p); c = Color.Lerp(c, patch, p * 0.7f);
                    float g = (Hash(seed + 7, x, y) - 0.5f) * 0.08f;
                    px[y * size + x] = new Color32(Cb(c.r + g), Cb(c.g + g), Cb(c.b + g), 255);
                }
            return Finish(px, size, "ProcPatchy" + seed);
        }

        static byte Cb(float v) => (byte)Mathf.Clamp(Mathf.RoundToInt(v * 255f), 0, 255);
    }
}
