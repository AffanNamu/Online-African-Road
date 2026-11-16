using System.Collections.Generic;
using ARO.NetCore;
using UnityEngine;
using UnityEngine.Rendering;

namespace ARO.World
{
    /// <summary>
    /// RB-15, the engine batching the owner approved on 2026-11-16 (docs/ENGINE_BATCHING_PROPOSAL_RB15.md, option E): the real building models of the loaded chunks are drawn with GPU
    /// instancing instead of one GameObject per building. For each production prefab a plan is read once from its LODGroup (each LOD's meshes, sub-meshes, materials, their transform
    /// inside the prefab, shadow flags) and the LODGroup's size, reference point and transition heights. Every frame each building picks its LOD with Unity's own rule
    /// (NetCore.InstancedLod: the same choice the LODGroup would make), is culled by its catalogue cull distance and by the camera's view, and the buildings that share a (mesh, sub-mesh,
    /// material) are drawn together, up to 511 per draw call.
    /// What does not change: where buildings stand (the same deterministic placement for every client), their meshes, materials and LODs, their shadows, and their colliders (the chunk
    /// streamer keeps collider-only objects near the vehicle). A prefab with anything the instancer does not draw (a light, a particle system, a skinned mesh, a renderer outside its LODs)
    /// is instantiated as before. ?instancing=0 on a test page turns the whole path off for comparison.
    /// </summary>
    public class PropInstancer : MonoBehaviour
    {
        public static bool Enabled = true;
        public QualityTier tier = QualityTiers.Medium;
        public static int LastDrawCalls { get; private set; }
        public static long LastTriangles { get; private set; }
        public static int LastInstances { get; private set; }

        public sealed class Part { public Mesh Mesh; public int Sub; public Material Mat; public Matrix4x4 Local; public ShadowCastingMode Shadow; public bool Receive; public int Tris; }
        public sealed class Plan { public Part[][] Lods; public float[] Heights; public float Size; public Vector3 RefPoint; public string Id; }
        struct Inst { public Plan P; public Matrix4x4 World; public Vector3 Ref; public float Size; public float Cull; }
        sealed class Batch { public Part Part; public Matrix4x4[] M = new Matrix4x4[64]; public int N; }

        static readonly Dictionary<GameObject, Plan> _plans = new Dictionary<GameObject, Plan>();
        static readonly HashSet<GameObject> _refused = new HashSet<GameObject>();
        readonly Dictionary<Vector2Int, List<Inst>> _chunks = new Dictionary<Vector2Int, List<Inst>>();
        readonly Dictionary<(Mesh, int, Material, ShadowCastingMode), Batch> _batches = new Dictionary<(Mesh, int, Material, ShadowCastingMode), Batch>();
        readonly Plane[] _planes = new Plane[6];
        float _nextLog;

        /// <summary>The instancing plan of a prefab, or null when it must be instantiated (it carries something the instancer does not draw, or has no valid LODGroup).</summary>
        public static Plan PlanFor(GameObject prefab, string id)
        {
            if (prefab == null || _refused.Contains(prefab)) return null;
            if (_plans.TryGetValue(prefab, out var p)) return p;
            p = BuildPlan(prefab, id); if (p == null) { _refused.Add(prefab); Debug.Log($"[Instancing] '{id}' is drawn as a prefab (not instanceable)"); return null; }
            return _plans[prefab] = p;
        }

        static Plan BuildPlan(GameObject prefab, string id)
        {
            if (prefab.GetComponentInChildren<Light>(true) != null || prefab.GetComponentInChildren<ParticleSystem>(true) != null || prefab.GetComponentInChildren<SkinnedMeshRenderer>(true) != null) return null;
            var lg = prefab.GetComponent<LODGroup>(); var root = prefab.transform; var toRoot = root.worldToLocalMatrix;
            var inLods = new HashSet<Renderer>(); Part[][] lods; float[] heights; float size; Vector3 refPoint;
            if (lg != null)
            {
                var src = lg.GetLODs(); if (src.Length == 0) return null;
                lods = new Part[src.Length][]; heights = new float[src.Length];
                for (int i = 0; i < src.Length; i++)
                {
                    heights[i] = src[i].screenRelativeTransitionHeight; var parts = new List<Part>();
                    foreach (var r in src[i].renderers) { if (r == null) continue; inLods.Add(r); if (!AddParts(r, toRoot, parts)) return null; }
                    lods[i] = parts.ToArray();
                }
                size = lg.size; refPoint = lg.localReferencePoint;
            }
            else
            {   // a prefab without LODs: one level, culled by the catalogue distance only
                var parts = new List<Part>();
                foreach (var r in prefab.GetComponentsInChildren<MeshRenderer>(true)) { inLods.Add(r); if (!AddParts(r, toRoot, parts)) return null; }
                if (parts.Count == 0) return null;
                lods = new[] { parts.ToArray() }; heights = new[] { 1e-6f }; var b = new Bounds(); bool first = true;
                foreach (var r in prefab.GetComponentsInChildren<MeshRenderer>(true)) { var mf = r.GetComponent<MeshFilter>(); if (mf == null || mf.sharedMesh == null) continue; var wb = mf.sharedMesh.bounds; if (first) { b = wb; first = false; } else b.Encapsulate(wb); }
                size = Mathf.Max(b.size.x, b.size.y, b.size.z); refPoint = b.center;
            }
            foreach (var r in prefab.GetComponentsInChildren<Renderer>(true)) if (!inLods.Contains(r)) return null;      // a renderer outside the LODs: keep the prefab
            if (!InstancedLod.ValidThresholds(heights)) return null;
            return new Plan { Lods = lods, Heights = heights, Size = size, RefPoint = refPoint, Id = id };
        }

        static bool AddParts(Renderer r, Matrix4x4 toRoot, List<Part> parts)
        {
            if (!(r is MeshRenderer)) return false;
            var mf = r.GetComponent<MeshFilter>(); if (mf == null || mf.sharedMesh == null) return true;
            var mesh = mf.sharedMesh; var mats = r.sharedMaterials; var local = toRoot * r.transform.localToWorldMatrix;
            for (int s = 0; s < Mathf.Min(mats.Length, mesh.subMeshCount); s++)
            {
                var mat = mats[s]; if (mat == null) continue;
                if (!mat.enableInstancing) mat.enableInstancing = true;                       // the kit's materials already have it on; a shared material is only ever drawn instanced or by its prefab
                parts.Add(new Part { Mesh = mesh, Sub = s, Mat = mat, Local = local, Shadow = r.shadowCastingMode, Receive = r.receiveShadows, Tris = (int)(mesh.GetIndexCount(s) / 3) });
            }
            return true;
        }

        /// <summary>Hands a chunk's instanceable buildings to the renderer (replacing what it held for that chunk). `root` is the chunk's root transform; positions are local to it.</summary>
        public void Register(Vector2Int chunk, Transform root, List<(Plan plan, Vector3 localPos, float yawDeg, float scale, float cull)> items)
        {
            var list = new List<Inst>(items.Count); var w = root.localToWorldMatrix;
            foreach (var it in items)
            {
                var m = w * Matrix4x4.TRS(it.localPos, Quaternion.Euler(0f, it.yawDeg, 0f), Vector3.one * it.scale);
                list.Add(new Inst { P = it.plan, World = m, Ref = m.MultiplyPoint3x4(it.plan.RefPoint), Size = it.plan.Size * it.scale, Cull = it.cull });
            }
            _chunks[chunk] = list;
        }

        public void Unregister(Vector2Int chunk) => _chunks.Remove(chunk);
        public int ChunkCount => _chunks.Count;

        void LateUpdate()
        {
            var cam = Camera.main; if (cam == null) return;
            foreach (var b in _batches.Values) b.N = 0;
            var cp = cam.transform.position; float fov = cam.fieldOfView, bias = QualitySettings.lodBias; GeometryUtility.CalculateFrustumPlanes(cam, _planes);
            int instances = 0;
            foreach (var list in _chunks.Values)
                foreach (var it in list)
                {
                    float d = Vector3.Distance(cp, it.Ref);
                    int lod = InstancedLod.Choose(it.Size, d, fov, bias, it.P.Heights, it.Cull); if (lod < 0) continue;
                    if (!GeometryUtility.TestPlanesAABB(_planes, new Bounds(it.Ref, Vector3.one * it.Size))) continue;
                    instances++;
                    foreach (var part in it.P.Lods[lod])
                    {
                        var key = (part.Mesh, part.Sub, part.Mat, part.Shadow);
                        if (!_batches.TryGetValue(key, out var b)) _batches[key] = b = new Batch { Part = part };
                        if (b.N == b.M.Length) System.Array.Resize(ref b.M, b.M.Length * 2);
                        b.M[b.N++] = it.World * part.Local;
                    }
                }
            int draws = 0; long tris = 0; var bounds = new Bounds(cp, Vector3.one * 8000f);
            foreach (var b in _batches.Values)
            {
                if (b.N == 0) continue;
                var rp = new RenderParams(b.Part.Mat) { shadowCastingMode = b.Part.Shadow, receiveShadows = b.Part.Receive, worldBounds = bounds, layer = 0 };
                for (int start = 0; start < b.N; start += InstancedLod.MaxPerDraw)
                {
                    int n = Mathf.Min(InstancedLod.MaxPerDraw, b.N - start);
                    Graphics.RenderMeshInstanced(rp, b.Part.Mesh, b.Part.Sub, b.M, n, start); draws++;
                }
                tris += (long)b.Part.Tris * b.N;
            }
            LastDrawCalls = draws; LastTriangles = tris; LastInstances = instances;
            if (Time.unscaledTime >= _nextLog) { _nextLog = Time.unscaledTime + 10f; Debug.Log($"[Instancing] buildings={instances} draws={draws} tris={tris} chunks={_chunks.Count} plans={_plans.Count} prefabFallback={_refused.Count}"); }
        }
    }
}
