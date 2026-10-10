using System.Collections.Generic;
using ARO.NetCore;
using Unity.Profiling;
using UnityEngine;

namespace ARO.World
{
    /// <summary>
    /// Measures what the world actually costs and logs it as [Perf] lines (browser console / CI). Triangles and draw-call estimates come from the
    /// renderers the camera really saw; texture and heap memory from the engine. Frame time is only judged against the budget when the player asks
    /// for it (?judge=1), because CI renders in software at a few frames per second and that number says nothing about a real GPU.
    /// </summary>
    public sealed class PerfProbe : MonoBehaviour
    {
        public ChunkStreamer streamer; public QualityTier tier = QualityTiers.Medium;
        public float intervalSeconds = 4f; public bool alwaysLog;
        public PerfSample Last { get; private set; }
        float _next = 6f, _frameMs; int _frames; ProfilerRecorder _draws, _tris, _setPass;
        readonly Dictionary<Mesh, int> _triCache = new Dictionary<Mesh, int>();

        void OnEnable()
        {
            try { _draws = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Draw Calls Count"); _tris = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Triangles Count"); _setPass = ProfilerRecorder.StartNew(ProfilerCategory.Render, "SetPass Calls Count"); }
            catch (System.Exception) { /* counters not available on this platform */ }
        }
        void OnDisable() { if (_draws.Valid) _draws.Dispose(); if (_tris.Valid) _tris.Dispose(); if (_setPass.Valid) _setPass.Dispose(); }

        void Update()
        {
            _frameMs += Time.unscaledDeltaTime * 1000f; _frames++;
            if (Time.unscaledTime < _next) return;
            _next = Time.unscaledTime + intervalSeconds;
            if (!alwaysLog && !ARO.World.PerfProbeSettings.Enabled) return;
            Sample();
        }

        int TrisOf(Mesh m)
        {
            if (m == null) return 0;
            if (_triCache.TryGetValue(m, out int t)) return t;
            long n = 0; for (int i = 0; i < m.subMeshCount; i++) n += m.GetIndexCount(i);
            return _triCache[m] = (int)(n / 3);
        }

        public PerfSample Sample()
        {
            long tris = 0; int draws = 0, visible = 0;
            foreach (var r in FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None))
            {
                if (!r.enabled || !r.gameObject.activeInHierarchy || !r.isVisible) continue;
                var mf = r.GetComponent<MeshFilter>(); if (mf == null || mf.sharedMesh == null) continue;
                visible++; tris += TrisOf(mf.sharedMesh); draws += Mathf.Min(r.sharedMaterials.Length, mf.sharedMesh.subMeshCount);
            }
            long texBytes = 0; int texCount = 0;
            foreach (var t in Resources.FindObjectsOfTypeAll<Texture>()) { texBytes += UnityEngine.Profiling.Profiler.GetRuntimeMemorySizeLong(t); texCount++; }
            var s = new PerfSample
            {
                Triangles = tris, DrawCalls = draws, ActiveRenderers = visible, LoadedChunks = streamer != null ? streamer.LoadedChunkCount : 0,
                TextureMb = texBytes / 1048576f, HeapMb = System.GC.GetTotalMemory(false) / 1048576f, FrameMs = _frames > 0 ? _frameMs / _frames : 0f
            };
            _frameMs = 0f; _frames = 0; Last = s;
            string rec = _draws.Valid && _draws.LastValue > 0 ? $"engineDraws={_draws.LastValue} engineTris={_tris.LastValue} setPass={_setPass.LastValue}" : "engineCounters=unavailable";
            string world = streamer != null ? $" terrainTris={streamer.TerrainTriangles} roadTris={streamer.RoadTriangles} propTris={streamer.PropTriangles} props={streamer.PropCount} pending={streamer.PendingWork} buildMs={streamer.Stats.BuildMsTotal:0} worstStageMs={streamer.Stats.WorstStageMs:0}" : "";
            Debug.Log($"[Perf] tier={tier.Name} visibleTris={s.Triangles} drawEstimate={s.DrawCalls} visibleRenderers={s.ActiveRenderers} chunks={s.LoadedChunks} textures={texCount} textureMB={s.TextureMb:0.0} heapMB={s.HeapMb:0.0} avgFrameMs={s.FrameMs:0.0} {rec}{world}");
            var judged = s; if (!PerfProbeSettings.JudgeFrameTime) judged.FrameMs = 0f;
            foreach (var v in PerfCheck.Evaluate(judged, PerfBudget.For(tier))) Debug.LogWarning("[Perf] BUDGET EXCEEDED: " + v + " (budget " + PerfBudget.For(tier).Name + ")");
            return s;
        }
    }

    public static class PerfProbeSettings
    {
        public static bool Enabled => Url("perf=1") || Url("smoke=");
        public static bool JudgeFrameTime => Url("judge=1");
        static bool Url(string s) { var u = Application.absoluteURL; return u != null && u.Contains(s); }
    }
}
