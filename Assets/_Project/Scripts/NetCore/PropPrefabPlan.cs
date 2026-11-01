using System;
using System.Collections.Generic;
using System.Linq;

namespace ARO.NetCore
{
    /// <summary>What an imported model measures in Unity (filled by the editor importer from the real imported GameObject; plain data so the rules can be tested without Unity).</summary>
    public sealed class ImportedMeasure
    {
        public float WidthM, HeightM, DepthM;                  // world-space bounds of LOD0 (X, Y, Z)
        public int[] LodTriangles = new int[0];                // triangles per LOD, from the imported meshes
        public int MaterialCount; public string[] ShaderNames = new string[0]; public bool AllMaterialsInstanced = true;
        public bool HasBoxCollider; public float[] ColliderSize = new float[0];
        public int RenderersOnCollisionNodes;                  // renderers left on col_* nodes (should be none)
    }

    /// <summary>
    /// Rules that turn an imported model into a production prop prefab, and the checks that prove it did. The game instantiates a production prefab for the rings around the player and does NOT
    /// batch it, give it a box collider or cull it by distance (ChunkStreamer.StageProps does all three only for generated placeholders), so a production prefab has to bring its own LODGroup,
    /// its own BoxCollider and its own culling. The LOD thresholds below reproduce LodPolicy.PropLodLevel (equal parts of the cull distance) as the screen-relative heights a LODGroup wants.
    /// </summary>
    public static class PropPrefabPlan
    {
        public const float FovDegrees = 60f, MinScreenHeight = 0.002f;

        /// <summary>Screen-relative height of an object of this size seen from this distance (vertical field of view <paramref name="fovDeg"/>).</summary>
        public static float ScreenHeightAt(float sizeM, float distance, float fovDeg = FovDegrees)
            => sizeM / (2f * Math.Max(0.01f, distance) * (float)Math.Tan(fovDeg * Math.PI / 360.0));

        /// <summary>
        /// LODGroup transition heights, one per LOD: element i is the screen height BELOW which LOD i is replaced by LOD i+1; the last is the height below which the prop is culled.
        /// LOD i+1 starts at distance (i+1) * cull / levels (LodPolicy with bias 1) and the prop is culled at the cull distance. Strictly decreasing; the floor is <see cref="MinScreenHeight"/>, and a level that would sit at or below the previous one steps 2% under it (a tiny prop culled far away), so thresholds stay strictly decreasing and positive.
        /// </summary>
        public static float[] LodScreenHeights(int levels, float sizeM, float cullDistance, float fovDeg = FovDegrees)
        {
            levels = Math.Max(1, levels); var h = new float[levels]; float prev = 1f;
            for (int i = 0; i < levels; i++)
            {
                float d = (i + 1) * cullDistance / levels;                    // i = last gives the cull distance itself
                float v = Math.Min(prev * 0.98f, Math.Max(MinScreenHeight, ScreenHeightAt(sizeM, d, fovDeg)));
                h[i] = v; prev = v;
            }
            return h;
        }

        /// <summary>The faults between a catalogue entry (what the Blender manifest promised and the game will believe) and what the importer measured. Empty means the model matches.</summary>
        public static List<string> Check(PropDef def, ImportedMeasure m, float tolerance = 0.03f, bool requireUrpLit = true)
        {
            var e = new List<string>();
            if (def == null || m == null) { e.Add("no definition or no measurement"); return e; }
            var spec = new PropCatalogSpec { props = new[] { def } };
            foreach (var x in PropCatalogValidator.Validate(spec)) e.Add("catalogue: " + x);
            void Dim(string what, float want, float got) { if (Math.Abs(want - got) > Math.Max(0.05f, want * tolerance)) e.Add($"{what}: the catalogue says {want:0.###} m, the imported model measures {got:0.###} m"); }
            Dim("width", def.widthM, m.WidthM); Dim("height", def.heightM, m.HeightM); Dim("depth", def.depthM, m.DepthM);
            var want = def.lodTriangles ?? new int[0];
            if (m.LodTriangles.Length != want.Length) e.Add($"LOD count: the catalogue lists {want.Length}, the import has {m.LodTriangles.Length}");
            else for (int i = 0; i < want.Length; i++) if (m.LodTriangles[i] != want[i]) e.Add($"LOD{i} triangles: the catalogue says {want[i]}, the import has {m.LodTriangles[i]}");
            if (m.MaterialCount > 16) e.Add($"{m.MaterialCount} materials: each is a draw call per instance (the game does not batch production prefabs)");
            if (requireUrpLit) foreach (var s in m.ShaderNames.Distinct()) if (s != "Universal Render Pipeline/Lit") e.Add($"shader '{s}': production materials must be Universal Render Pipeline/Lit");
            if (!m.AllMaterialsInstanced) e.Add("a material has GPU instancing off: the game does not batch production prefabs, so instancing is what lets many instances of one prop share draw calls");
            if (def.collider == "box")
            {
                if (!m.HasBoxCollider) e.Add("the catalogue says collider 'box' but the prefab has no BoxCollider (the game adds colliders for placeholders only)");
                else if (m.ColliderSize.Length == 3 && (m.ColliderSize[0] > m.WidthM * 1.05f + 0.05f || m.ColliderSize[1] > m.HeightM * 1.05f + 0.05f || m.ColliderSize[2] > m.DepthM * 1.05f + 0.05f)) e.Add("the box collider is larger than the model");
            }
            else if (m.HasBoxCollider) e.Add($"the prefab has a BoxCollider but the catalogue says collider '{def.collider}'");
            if (m.RenderersOnCollisionNodes > 0) e.Add("a collision node still has a renderer (it would be drawn)");
            return e;
        }
    }
}
