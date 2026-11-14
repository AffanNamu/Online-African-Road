using System;
using System.Collections.Generic;
using ARO.NetCore;
using UnityEngine;
using UnityEngine.Rendering;

namespace ARO.World
{
    /// <summary>
    /// P2 of docs/FUNCTIONAL_PLACES_PROPOSAL.md (approved by the owner on 2026-11-14): the people you can see. The crowd of the loaded chunks (CrowdPlanner, deterministic: every client sees the
    /// same people at the same places) plus the passengers of a bus stop are drawn as ONE mesh with ONE material (a small palette texture), rebuilt from pre-posed frames each frame: the nearest
    /// people first, at most 60 (20 on the low tier), within 140 m, two levels of detail. People are visual only: no collider, nothing networked.
    /// </summary>
    public class CrowdRenderer : MonoBehaviour
    {
        public ChunkStreamer streamer; public TimeOfDay timeOfDay; public Transform eye; public QualityTier tier = QualityTiers.Medium;
        public const float MaxDistance = 140f, Lod1Beyond = 35f;
        public int Cap => tier != null && tier.Name == "low" ? 20 : 60;
        public int Visible { get; private set; }
        public int Triangles { get; private set; }
        /// <summary>Test pages: the hour's density can be forced (?people=1 shows the day's crowd at any hour).</summary>
        public float DensityOverride = -1f;

        Mesh _mesh; MeshRenderer _mr; Material _mat; Texture2D _palette;
        readonly List<CrowdAgent> _agents = new List<CrowdAgent>();
        readonly List<CrowdAgent> _transient = new List<CrowdAgent>(); double _transientUntil;
        readonly List<Vector3> _v = new List<Vector3>(); readonly List<Vector3> _n = new List<Vector3>(); readonly List<Vector2> _uv = new List<Vector2>(); readonly List<int> _t = new List<int>();
        readonly Dictionary<long, Vector3[]> _normals = new Dictionary<long, Vector3[]>();
        float _nextLog;

        /// <summary>Seconds on a clock every client shares (UTC), so walkers are at the same place for everyone.</summary>
        public static double Clock => (DateTime.UtcNow - new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalSeconds;

        public void Init(Material template)
        {
            _palette = new Texture2D(PeopleKit.PaletteSize, PeopleKit.PaletteSize, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp, name = "PeoplePalette" };
            var px = new Color32[PeopleKit.PaletteSize * PeopleKit.PaletteSize];
            for (int i = 0; i < px.Length; i++) px[i] = new Color32(128, 128, 128, 255);
            for (int i = 0; i < PeopleKit.Palette.Length; i++) { var c = PeopleKit.Palette[i]; px[i] = new Color32(c[0], c[1], c[2], 255); }
            _palette.SetPixels32(px); _palette.Apply(false, true);
            _mat = template != null ? new Material(template) : null; if (_mat == null) { Debug.LogWarning("[Crowd] no material template; people are not drawn"); enabled = false; return; }
            _mat.name = "People"; _mat.mainTexture = _palette; _mat.mainTextureScale = Vector2.one; _mat.mainTextureOffset = Vector2.zero;
            if (_mat.HasProperty("_BaseMap")) { _mat.SetTexture("_BaseMap", _palette); _mat.SetTextureScale("_BaseMap", Vector2.one); _mat.SetTextureOffset("_BaseMap", Vector2.zero); }
            if (_mat.HasProperty("_BaseColor")) _mat.SetColor("_BaseColor", Color.white);
            if (_mat.HasProperty("_Smoothness")) _mat.SetFloat("_Smoothness", 0.15f);
            foreach (var k in new[] { "_EMISSION", "_NORMALMAP" }) _mat.DisableKeyword(k);
            _mesh = new Mesh { name = "Crowd", indexFormat = IndexFormat.UInt32 }; _mesh.MarkDynamic();
            gameObject.AddComponent<MeshFilter>().sharedMesh = _mesh; _mr = gameObject.AddComponent<MeshRenderer>(); _mr.sharedMaterial = _mat;
            _mr.shadowCastingMode = tier != null && tier.Name == "low" ? ShadowCastingMode.Off : ShadowCastingMode.On; _mr.receiveShadows = true;
            Debug.Log($"[Crowd] ready: {PeopleKit.Designs.Length} designs, budget {Cap} people within {MaxDistance:0} m");
        }

        /// <summary>A bus stop: the server's boarded and alighted numbers walk between the kerb and the door (first off, then on).</summary>
        public void BusStop(Transform bus, float halfLength, int boarded, int alighted, float groundY)
        {
            if (bus == null) return;
            var right = bus.right; right.y = 0f; right.Normalize(); var fwd = bus.forward; fwd.y = 0f; fwd.Normalize();
            var door = bus.position + right * 1.35f + fwd * (halfLength - 1.6f); door.y = groundY;
            double t0 = Clock + 0.4; var list = CrowdPlanner.BusStop(new V3(door.x, door.y, door.z), new V3(right.x, 0f, right.z), new V3(fwd.x, 0f, fwd.z), boarded, alighted, t0, Mathf.RoundToInt(door.x * 7f + door.z * 13f));
            _transient.Clear(); _transient.AddRange(list); _transientUntil = CrowdPlanner.EndOf(list) + 1.0;
            Debug.Log($"[Crowd] bus stop: {alighted} alight, {boarded} board ({list.Count} people drawn) at ({door.x:F1}, {door.z:F1})");
        }

        void LateUpdate()
        {
            if (_mesh == null || streamer == null) return;
            var cam = eye != null ? eye : (Camera.main != null ? Camera.main.transform : null); if (cam == null) return;
            double t = Clock; if (_transient.Count > 0 && t > _transientUntil) _transient.Clear();
            _agents.Clear(); streamer.CollectCrowd(_agents); _agents.AddRange(_transient);
            float hour = timeOfDay != null ? timeOfDay.hour : 12f, density = DensityOverride >= 0f ? DensityOverride : CrowdPlanner.Density(hour);
            var p = cam.position; var vis = CrowdPlanner.Visible(_agents, t, new V3(p.x, p.y, p.z), MaxDistance, Cap, density);
            _v.Clear(); _n.Clear(); _uv.Clear(); _t.Clear(); int tris = 0;
            foreach (var (a, pos, yaw, frame, dist) in vis)
            {
                int lod = dist < Lod1Beyond ? 0 : 1;
                var m = PeopleKit.Mesh(a.Design, a.Colourway, a.Skin, lod); var f = PeopleKit.Frame(a.Design, a.Colourway, a.Skin, lod, frame); var nrm = Normals(a, lod, frame, f, m);
                var q = Quaternion.Euler(0f, yaw, 0f); var o = new Vector3(pos.X, pos.Y, pos.Z); int b = _v.Count;
                for (int i = 0; i < f.Length; i++) { _v.Add(o + q * new Vector3(f[i].X, f[i].Y, f[i].Z)); _n.Add(q * nrm[i]); _uv.Add(new Vector2(m.Uv[i].X, m.Uv[i].Y)); }
                for (int i = 0; i < m.Tris.Length; i++) _t.Add(b + m.Tris[i]);
                tris += m.TriangleCount;
            }
            _mesh.Clear(); _mesh.SetVertices(_v); _mesh.SetNormals(_n); _mesh.SetUVs(0, _uv); _mesh.SetTriangles(_t, 0, false); _mesh.RecalculateBounds();
            Visible = vis.Count; Triangles = tris;
            if (Time.unscaledTime >= _nextLog) { _nextLog = Time.unscaledTime + 5f; Debug.Log($"[Crowd] agents={_agents.Count} visible={vis.Count} tris={tris} density={density:0.00} hour={hour:0.0} transient={_transient.Count}"); }
        }

        /// <summary>Smooth normals of a posed frame (computed once per figure, level and frame).</summary>
        Vector3[] Normals(CrowdAgent a, int lod, int frame, V3[] f, PersonMesh m)
        {
            long key = ((long)a.Design << 32) | ((long)a.Colourway << 24) | ((long)a.Skin << 16) | ((long)lod << 8) | (long)frame;
            if (_normals.TryGetValue(key, out var n)) return n;
            var tmp = new Mesh(); var vs = new Vector3[f.Length]; for (int i = 0; i < f.Length; i++) vs[i] = new Vector3(f[i].X, f[i].Y, f[i].Z);
            tmp.vertices = vs; tmp.triangles = m.Tris; tmp.RecalculateNormals(); n = tmp.normals; Destroy(tmp);
            _normals[key] = n; return n;
        }

        void OnDestroy() { if (_mesh != null) Destroy(_mesh); if (_mat != null) Destroy(_mat); if (_palette != null) Destroy(_palette); }
    }
}
