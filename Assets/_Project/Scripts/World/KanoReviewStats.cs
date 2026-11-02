using System.Collections.Generic;
using ARO.NetCore;
using UnityEngine;

namespace ARO.World
{
    /// <summary>
    /// Review diagnostics only ("?smoke=kanostreet"): every few seconds splits the PerfProbe's draw-call estimate into the review kit (per asset), the review ground and everything else, and logs it as
    /// "[KanoReview] draws ..." lines, so a budget overrun can be traced to what causes it instead of being guessed. It counts exactly what PerfProbe counts (enabled, active, visible mesh renderers,
    /// one draw per material slot) and changes nothing in the scene.
    /// </summary>
    public sealed class KanoReviewStats : MonoBehaviour
    {
        public Transform Root; public float IntervalSeconds = 4f;
        float _next = 8f; readonly Dictionary<Mesh, int> _tris = new Dictionary<Mesh, int>();

        void Update()
        {
            if (Root == null || Time.unscaledTime < _next) return;
            _next = Time.unscaledTime + IntervalSeconds; Log();
        }

        int TrisOf(Mesh m)
        {
            if (_tris.TryGetValue(m, out int t)) return t;
            long n = 0; for (int i = 0; i < m.subMeshCount; i++) n += m.GetIndexCount(i);
            return _tris[m] = (int)(n / 3);
        }

        /// <summary>The name of the review-prefab child (the placed asset) that owns this transform, or null when it is not part of the review prefab.</summary>
        string AssetOf(Transform t)
        {
            while (t != null && t.parent != Root) t = t.parent;
            return t != null ? t.name : null;
        }

        void Log()
        {
            var b = new DrawBreakdown(); var kitMaterials = new HashSet<Material>(); int instancingOn = 0;
            foreach (var r in FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None))
            {
                if (!r.enabled || !r.gameObject.activeInHierarchy || !r.isVisible) continue;
                var mf = r.GetComponent<MeshFilter>(); if (mf == null || mf.sharedMesh == null) continue;
                var mats = r.sharedMaterials; int draws = Mathf.Min(mats.Length, mf.sharedMesh.subMeshCount);
                var ids = new int[draws]; for (int i = 0; i < draws; i++) ids[i] = mats[i] != null ? mats[i].GetInstanceID() : 0;
                int mesh = mf.sharedMesh.GetInstanceID(); long tris = TrisOf(mf.sharedMesh); string asset = AssetOf(r.transform);
                if (asset == null) { b.Add("rest", mesh, ids, draws, tris); continue; }
                if (asset == "review_ground") { b.Add("ground", mesh, ids, draws, tris); continue; }
                b.Add("kit", mesh, ids, draws, tris); b.Add("asset:" + asset, mesh, ids, draws, tris);
                for (int i = 0; i < draws; i++) if (mats[i] != null && kitMaterials.Add(mats[i]) && mats[i].enableInstancing) instancingOn++;
            }
            int total = b.Draws("kit", "ground", "rest");
            Debug.Log("[KanoReview] draws total=" + total + " kitShare=" + (total > 0 ? (100f * b.Draws("kit") / total).ToString("0.0") : "0") + "%");
            Debug.Log("[KanoReview] draws " + b.Summary("kit")); Debug.Log("[KanoReview] draws " + b.Summary("ground")); Debug.Log("[KanoReview] draws " + b.Summary("rest"));
            Debug.Log("[KanoReview] draws byAsset (draws/renderers/tris) " + b.Ranked("asset:"));
            Debug.Log("[KanoReview] draws kitMaterialsInstancingOn=" + instancingOn + "/" + kitMaterials.Count);
        }
    }
}
