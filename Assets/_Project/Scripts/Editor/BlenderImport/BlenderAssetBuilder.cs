using System.Collections.Generic;
using System.Linq;
using ARO.NetCore;
using UnityEngine;

namespace ARO.Editor.BlenderImport
{
    /// <summary>
    /// Turns a model imported from the Blender toolchain (FBX, or GLB through glTFast) into the structure the game needs from a production prop prefab (see PropPrefabPlan): the root named after the id,
    /// a LODGroup over the {id}_lod0..3 groups with thresholds that reproduce LodPolicy, a BoxCollider in place of each col_* mesh (the mesh renderer is removed, the collider is not drawn), and the shadow flag from the
    /// catalogue. Editor only; it never touches the running game, the vehicle system or any existing asset. The caller decides whether to save the result as Resources/Props/{id}.prefab.
    /// </summary>
    public static class BlenderAssetBuilder
    {
        public sealed class Result { public GameObject Root; public ImportedMeasure Measure; public bool ImporterMadeLodGroup; public List<string> Notes = new List<string>(); }

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

        public static Result Build(GameObject imported, PropDef def)
        {
            var res = new Result(); var root = Object.Instantiate(imported); root.name = def.id; root.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity); root.transform.localScale = Vector3.one;
            res.Root = root;

            // collision: one BoxCollider per col_* node, sized from its mesh, and the mesh itself not drawn
            int colRenderers = 0;
            foreach (var t in All(root.transform).ToList())
            {
                if (!t.name.StartsWith("col_", System.StringComparison.OrdinalIgnoreCase)) continue;
                var mf = t.GetComponent<MeshFilter>(); if (mf == null || mf.sharedMesh == null) { res.Notes.Add($"{t.name}: no mesh to take a box from"); continue; }
                var box = t.gameObject.AddComponent<BoxCollider>(); box.center = mf.sharedMesh.bounds.center; box.size = mf.sharedMesh.bounds.size;
                var mr = t.GetComponent<MeshRenderer>(); if (mr != null) Object.DestroyImmediate(mr); Object.DestroyImmediate(mf);
            }
            colRenderers = All(root.transform).Where(t => t.name.StartsWith("col_", System.StringComparison.OrdinalIgnoreCase)).Sum(t => t.GetComponents<Renderer>().Length);

            // LODs from {id}_lod0..3 (all renderers outside col_* nodes belong to LOD0 when there are no groups)
            var groups = new List<Transform>();
            for (int i = 0; i < 4; i++) { var g = Find(root.transform, def.id + "_lod" + i); if (g == null) break; groups.Add(g); }
            var colNodes = All(root.transform).Where(t => t.name.StartsWith("col_", System.StringComparison.OrdinalIgnoreCase)).ToList();
            bool InCol(Component c) => colNodes.Any(n => c.transform == n || c.transform.IsChildOf(n));
            var perLod = new List<Renderer[]>();
            if (groups.Count == 0) perLod.Add(root.GetComponentsInChildren<Renderer>(true).Where(r => !InCol(r)).ToArray());
            else foreach (var g in groups) perLod.Add(g.GetComponentsInChildren<Renderer>(true).Where(r => !InCol(r)).ToArray());

            var lg = root.GetComponent<LODGroup>(); res.ImporterMadeLodGroup = lg != null; if (lg == null) lg = root.AddComponent<LODGroup>();
            var b = Bounds(perLod[0]); float size = Mathf.Max(b.size.x, Mathf.Max(b.size.y, b.size.z));
            var heights = PropPrefabPlan.LodScreenHeights(perLod.Count, size, def.cullDistance);
            var lods = new LOD[perLod.Count]; for (int i = 0; i < lods.Length; i++) lods[i] = new LOD(heights[i], perLod[i]);
            lg.fadeMode = LODFadeMode.None; lg.SetLODs(lods); lg.RecalculateBounds();
            var shadow = def.castShadow ? UnityEngine.Rendering.ShadowCastingMode.On : UnityEngine.Rendering.ShadowCastingMode.Off;
            foreach (var arr in perLod) foreach (var r in arr) r.shadowCastingMode = shadow;

            res.Measure = Measure(root, perLod, colRenderers);
            return res;
        }

        /// <summary>The box's size in world units from its own size and the node's scale (axis-aligned boxes: the case here), not from Collider.bounds, which depends on physics having synchronised.</summary>
        public static Vector3 WorldSize(BoxCollider c) { var s = c.transform.lossyScale; return new Vector3(Mathf.Abs(c.size.x * s.x), Mathf.Abs(c.size.y * s.y), Mathf.Abs(c.size.z * s.z)); }

        static Bounds Bounds(Renderer[] rs) { var b = rs[0].bounds; foreach (var r in rs.Skip(1)) b.Encapsulate(r.bounds); return b; }

        static ImportedMeasure Measure(GameObject root, List<Renderer[]> perLod, int colRenderers)
        {
            var b = Bounds(perLod[0]); var boxes = root.GetComponentsInChildren<BoxCollider>(true);
            var mats = perLod.SelectMany(a => a).SelectMany(r => r.sharedMaterials).Where(m => m != null).Distinct().ToList();
            return new ImportedMeasure
            {
                WidthM = b.size.x, HeightM = b.size.y, DepthM = b.size.z, LodTriangles = perLod.Select(a => a.Sum(Triangles)).ToArray(), MaterialCount = mats.Count,
                ShaderNames = mats.Select(m => m.shader != null ? m.shader.name : "(none)").ToArray(), HasBoxCollider = boxes.Length > 0,
                ColliderSize = boxes.Length > 0 ? new[] { boxes.Max(x => WorldSize(x).x), boxes.Max(x => WorldSize(x).y), boxes.Max(x => WorldSize(x).z) } : new float[0], RenderersOnCollisionNodes = colRenderers
            };
        }
    }
}
