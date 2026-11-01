using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ARO.NetCore;
using UnityEditor;
using UnityEngine;

namespace ARO.Editor.BlenderImport
{
    /// <summary>The material kit as the Blender toolchain writes it (aro_materials.json): the library's sRGB colours, roughness and metallic, one entry per surface.</summary>
    [Serializable] public sealed class MaterialKitFile { public int schema; public MaterialKitEntry[] materials; }
    [Serializable] public sealed class MaterialKitEntry { public string name; public float[] base_color_srgb; public float roughness, metallic; public string note; public string texture; public float cutout; public bool two_sided; }

    /// <summary>
    /// Turns a model imported from the Blender toolchain (FBX, or GLB through glTFast) into the structure the game needs from a production prop prefab (see PropPrefabPlan):
    ///  * a NEW root with identity rotation and scale named after the id, with the imported model as its child. ChunkStreamer.StageProps overwrites the instance's localRotation with the yaw and multiplies its localScale,
    ///    so any axis conversion the importer put on the model's own root would be lost if the model were the prefab root;
    ///  * a LODGroup over the {id}_lod0..3 groups with thresholds that reproduce LodPolicy;
    ///  * a BoxCollider in place of each col_* mesh (its renderer is removed, so it is not drawn);
    ///  * the imported materials replaced by the shared kit: URP/Lit, the library's own colours, GPU instancing on (imported materials come out as the Standard shader with the exporter's linear colour
    ///    taken as sRGB, which is wrong for the game and for the colour);
    ///  * the shadow flag from the catalogue.
    /// Editor only; it never touches the running game, the vehicle system or any existing asset. The caller decides whether to save the result as Resources/Props/{id}.prefab.
    /// </summary>
    public static class BlenderAssetBuilder
    {
        public sealed class Result { public GameObject Root; public ImportedMeasure Measure; public List<string> Notes = new List<string>(); public List<string> UnmatchedMaterials = new List<string>(); }

        public static Transform Find(Transform t, string name)
        {
            if (t.name == name) return t;
            for (int i = 0; i < t.childCount; i++) { var f = Find(t.GetChild(i), name); if (f != null) return f; }
            return null;
        }

        static IEnumerable<Transform> All(Transform t) { yield return t; for (int i = 0; i < t.childCount; i++) foreach (var c in All(t.GetChild(i))) yield return c; }

        public static int Triangles(Renderer r)
        {
            var mf = r.GetComponent<MeshFilter>(); Mesh m = mf != null ? mf.sharedMesh : (r is SkinnedMeshRenderer s ? s.sharedMesh : null);
            if (m == null) return 0; int n = 0; for (int i = 0; i < m.subMeshCount; i++) n += (int)m.GetIndexCount(i) / 3; return n;
        }

        public static MaterialKitFile LoadKit(string path) => JsonUtility.FromJson<MaterialKitFile>(File.ReadAllText(path));

        public static Color KitColour(MaterialKitEntry e) => new Color(e.base_color_srgb[0], e.base_color_srgb[1], e.base_color_srgb[2], 1f);        // sRGB, as Unity's Inspector and Color properties expect

        /// <summary>The Texture2D asset with this file name (without extension) anywhere in the project, or null.</summary>
        public static Texture2D FindTexture(string name)
        {
            foreach (var guid in AssetDatabase.FindAssets(name + " t:Texture2D"))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (Path.GetFileNameWithoutExtension(path) == name) return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            }
            return null;
        }

        /// <summary>Foliage cards need their alpha kept as coverage through the mip chain, or the crown thins out with distance.</summary>
        static void ConfigureCutoutTexture(Texture2D tex, float cutoff)
        {
            var path = AssetDatabase.GetAssetPath(tex); var ti = AssetImporter.GetAtPath(path) as TextureImporter; if (ti == null) return;
            if (ti.alphaIsTransparency && ti.mipMapsPreserveCoverage && Mathf.Abs(ti.alphaTestReferenceValue - cutoff) < 1e-4f) return;
            ti.alphaIsTransparency = true; ti.mipmapEnabled = true; ti.mipMapsPreserveCoverage = true; ti.alphaTestReferenceValue = cutoff; ti.SaveAndReimport();
        }

        /// <summary>The kit material for an entry: the asset at folder/{name}.mat, created (URP/Lit, instancing on) if it does not exist yet. A textured entry gets its base-colour texture (found by name in the
        /// project; flat colour if the texture is not there), a cutout entry is alpha-clipped at its threshold, a two-sided entry is drawn on both faces.</summary>
        public static Material KitMaterial(MaterialKitEntry e, string folder)
        {
            string path = folder.TrimEnd('/') + "/" + e.name + ".mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null)
            {
                Directory.CreateDirectory(folder); m = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = e.name };
                AssetDatabase.CreateAsset(m, path);
            }
            m.SetColor("_BaseColor", KitColour(e)); m.SetFloat("_Metallic", e.metallic); m.SetFloat("_Smoothness", 1f - e.roughness); m.enableInstancing = true;
            var tex = string.IsNullOrEmpty(e.texture) ? null : FindTexture(e.texture);
            if (tex != null) { m.SetTexture("_BaseMap", tex); m.SetColor("_BaseColor", Color.white); if (e.cutout > 0f) ConfigureCutoutTexture(tex, e.cutout); }
            if (e.cutout > 0f)
            {
                m.SetFloat("_AlphaClip", 1f); m.SetFloat("_Cutoff", e.cutout); m.EnableKeyword("_ALPHATEST_ON"); m.SetOverrideTag("RenderType", "TransparentCutout");
                m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.AlphaTest;
            }
            m.SetFloat("_Cull", e.two_sided ? 0f : 2f);
            EditorUtility.SetDirty(m); return m;
        }

        /// <summary>The box a col_* node stands for, as its world-space axis-aligned bounds (the node may carry a rotation from the importer's axis conversion).</summary>
        public static Bounds WorldBox(BoxCollider c)
        {
            var m = c.transform.localToWorldMatrix; var half = c.size * 0.5f; Bounds b = default; bool first = true;
            for (int i = 0; i < 8; i++)
            {
                var corner = c.center + new Vector3((i & 1) == 0 ? -half.x : half.x, (i & 2) == 0 ? -half.y : half.y, (i & 4) == 0 ? -half.z : half.z);
                var w = m.MultiplyPoint3x4(corner); if (first) { b = new Bounds(w, Vector3.zero); first = false; } else b.Encapsulate(w);
            }
            return b;
        }

        public static Result Build(GameObject imported, PropDef def, MaterialKitFile kit = null, string kitFolder = null)
        {
            var res = new Result(); var root = new GameObject(def.id); res.Root = root;
            var model = UnityEngine.Object.Instantiate(imported, root.transform); model.name = "model";            // the importer's own root transform is kept: it may hold the axis conversion

            // collision: one BoxCollider per col_* node, sized from its mesh, and the mesh itself not drawn
            foreach (var t in All(model.transform).ToList())
            {
                if (!t.name.StartsWith("col_", StringComparison.OrdinalIgnoreCase)) continue;
                var mf = t.GetComponent<MeshFilter>(); if (mf == null || mf.sharedMesh == null) { res.Notes.Add(t.name + ": no mesh to take a box from"); continue; }
                var box = t.gameObject.AddComponent<BoxCollider>(); box.center = mf.sharedMesh.bounds.center; box.size = mf.sharedMesh.bounds.size;
                var mr = t.GetComponent<MeshRenderer>(); if (mr != null) UnityEngine.Object.DestroyImmediate(mr); UnityEngine.Object.DestroyImmediate(mf);
            }
            var colNodes = All(model.transform).Where(t => t.name.StartsWith("col_", StringComparison.OrdinalIgnoreCase)).ToList();
            int colRenderers = colNodes.Sum(t => t.GetComponents<Renderer>().Length);
            bool InCol(Component c) => colNodes.Any(n => c.transform == n || c.transform.IsChildOf(n));

            // LODs from {id}_lod0..3 (all renderers outside col_* nodes belong to LOD0 when there are no groups)
            var groups = new List<Transform>();
            for (int i = 0; i < 4; i++) { var g = Find(model.transform, def.id + "_lod" + i); if (g == null) break; groups.Add(g); }
            var perLod = new List<Renderer[]>();
            if (groups.Count == 0) perLod.Add(model.GetComponentsInChildren<Renderer>(true).Where(r => !InCol(r)).ToArray());
            else foreach (var g in groups) perLod.Add(g.GetComponentsInChildren<Renderer>(true).Where(r => !InCol(r)).ToArray());

            // materials: the kit instead of what the importer made
            if (kit != null && kitFolder != null)
            {
                var byName = kit.materials.ToDictionary(e => e.name, e => e);
                foreach (var r in perLod.SelectMany(a => a))
                {
                    var mats = r.sharedMaterials;
                    for (int i = 0; i < mats.Length; i++)
                    {
                        string n = mats[i] != null ? mats[i].name.Replace(" (Instance)", "") : "";
                        if (byName.TryGetValue(n, out var entry)) mats[i] = KitMaterial(entry, kitFolder); else if (!res.UnmatchedMaterials.Contains(n)) res.UnmatchedMaterials.Add(n);
                    }
                    r.sharedMaterials = mats;
                }
                AssetDatabase.SaveAssets();
            }

            var lg = root.AddComponent<LODGroup>();
            var b = Bounds(perLod[0]); float size = Mathf.Max(b.size.x, Mathf.Max(b.size.y, b.size.z));
            var heights = PropPrefabPlan.LodScreenHeights(perLod.Count, size, def.cullDistance);
            var lods = new LOD[perLod.Count]; for (int i = 0; i < lods.Length; i++) lods[i] = new LOD(heights[i], perLod[i]);
            lg.fadeMode = LODFadeMode.None; lg.SetLODs(lods); lg.RecalculateBounds();
            var shadow = def.castShadow ? UnityEngine.Rendering.ShadowCastingMode.On : UnityEngine.Rendering.ShadowCastingMode.Off;
            foreach (var arr in perLod) foreach (var r in arr) r.shadowCastingMode = shadow;

            res.Measure = Measure(root, perLod, colRenderers);
            return res;
        }

        static Bounds Bounds(Renderer[] rs) { var b = rs[0].bounds; foreach (var r in rs.Skip(1)) b.Encapsulate(r.bounds); return b; }

        static ImportedMeasure Measure(GameObject root, List<Renderer[]> perLod, int colRenderers)
        {
            var b = Bounds(perLod[0]); var boxes = root.GetComponentsInChildren<BoxCollider>(true);
            var mats = perLod.SelectMany(a => a).SelectMany(r => r.sharedMaterials).Where(m => m != null).Distinct().ToList();
            var wb = boxes.Select(WorldBox).ToList();
            return new ImportedMeasure
            {
                WidthM = b.size.x, HeightM = b.size.y, DepthM = b.size.z, LodTriangles = perLod.Select(a => a.Sum(Triangles)).ToArray(), MaterialCount = mats.Count,
                ShaderNames = mats.Select(m => m.shader != null ? m.shader.name : "(none)").ToArray(), AllMaterialsInstanced = mats.All(m => m.enableInstancing), HasBoxCollider = boxes.Length > 0,
                ColliderSize = wb.Count > 0 ? new[] { wb.Max(x => x.size.x), wb.Max(x => x.size.y), wb.Max(x => x.size.z) } : new float[0], RenderersOnCollisionNodes = colRenderers
            };
        }
    }
}
