using System;
using System.Collections;
using System.Collections.Generic;
using ARO.Backend;
using ARO.NetCore;
using ARO.World;
using UnityEngine;
using UnityEngine.Networking;

namespace ARO.Game
{
    /// <summary>
    /// Advertising in the running game (docs/ADVERTISING_SYSTEM.md section 7). Optional by design: the game starts, plays and keeps playing with this switched off, with the service down, with a bad answer or with no
    /// image; a board then shows the neutral fallback. It never blocks sign-in, loading or driving, touches no physics and takes no collider.
    /// What it does: every few minutes it asks the server which creative is on which board (a few bytes when nothing changed), downloads only the images of boards within 400 m (one at a time), copies each into ONE
    /// shared 2048 x 1024 atlas, and draws all advertising faces of the loaded chunks as ONE mesh with ONE material (+1 draw call, 8 MB of texture). It measures how long each board was really readable (distance,
    /// view, angle, no occluder, a real advertisement showing) and sends the daily sums once a minute; it follows nobody and sends no identity.
    /// </summary>
    public sealed class AdService : MonoBehaviour
    {
        GameServices _svc; ChunkStreamer _streamer; string _routeId; Transform _player;
        readonly AdManifestState _state = new AdManifestState(); readonly AdAtlas _atlas = new AdAtlas(); readonly AdExposureMeter _meter = new AdExposureMeter();
        readonly Dictionary<Vector2Int, List<AdFace>> _chunks = new Dictionary<Vector2Int, List<AdFace>>(); readonly Dictionary<string, AdFace> _faces = new Dictionary<string, AdFace>();
        readonly Dictionary<string, int> _cellOf = new Dictionary<string, int>();             // slot id -> the atlas cell its CURRENT creative sits in (absent: the fallback)
        Texture2D _tex; Material _mat; Mesh _mesh; GameObject _go; bool _dirty, _fetching, _downloading, _loggedDown, _fixture, _sending; long _tick; double _clock; float _nextLog, _nextExposure, _nextRebuildLog;
        public int RealFaces { get; private set; } public int FaceCount => _faces.Count; public bool Enabled { get; private set; }

        public void Init(GameServices svc, RouteDefinition route, ChunkStreamer streamer, bool fixture)
        {
            _svc = svc; _streamer = streamer; _routeId = route != null && route.Model != null ? route.Model.Spec.routeId : null; _fixture = fixture;
            Enabled = _routeId != null && (fixture || (svc != null && svc.Config != null && svc.Config.IsConfigured));
            if (!Enabled) { Debug.Log("[Ads] off (" + (_routeId == null ? "no route data" : "no backend configured") + "); boards keep the shipped look"); return; }
            streamer.FacesLoaded += OnFaces; streamer.FacesUnloaded += OnUnloaded;
            Debug.Log("[Ads] ready (" + (fixture ? "fixture: no network" : "service") + "), route " + _routeId + ", atlas " + AdAtlas.Width + "x" + AdAtlas.Height + " (" + AdAtlas.Width * AdAtlas.Height * 4 / 1048576 + " MB)");
        }

        void OnDestroy() { if (_streamer != null) { _streamer.FacesLoaded -= OnFaces; _streamer.FacesUnloaded -= OnUnloaded; } if (_tex != null) Destroy(_tex); if (_mat != null) Destroy(_mat); if (_mesh != null) Destroy(_mesh); }

        // ------------------------------------------------------------------------------------------------------------------------------------------------ atlas and the one mesh
        void BuildAtlas()
        {
            _tex = new Texture2D(AdAtlas.Width, AdAtlas.Height, TextureFormat.RGBA32, false) { name = "AdAtlas", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            var fill = new Color32[AdAtlas.Width * AdAtlas.Height]; for (int i = 0; i < fill.Length; i++) fill[i] = new Color32(58, 62, 68, 255);
            _tex.SetPixels32(fill);
            foreach (var idx in new[] { AdAtlas.FallbackCell, AdAtlas.FallbackPortraitCell }) PaintFallback(idx);
            _tex.Apply(false);
            var sh = Shader.Find("Universal Render Pipeline/Lit"); if (sh == null) sh = Shader.Find("Universal Render Pipeline/Unlit"); if (sh == null) sh = Shader.Find("Sprites/Default");
            _mat = new Material(sh) { name = "AdFaces", mainTexture = _tex }; _mat.SetTexture("_BaseMap", _tex); _mat.SetColor("_BaseColor", Color.white); if (_mat.HasProperty("_Smoothness")) _mat.SetFloat("_Smoothness", 0.05f); if (_mat.HasProperty("_Metallic")) _mat.SetFloat("_Metallic", 0f);
            _mesh = new Mesh { name = "AdFaces" }; _mesh.MarkDynamic();
            _go = new GameObject("AdFaces", typeof(MeshFilter), typeof(MeshRenderer)); _go.GetComponent<MeshFilter>().sharedMesh = _mesh;
            var mr = _go.GetComponent<MeshRenderer>(); mr.sharedMaterial = _mat; mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; mr.receiveShadows = false;
        }

        void PaintFallback(int cellIndex)
        {
            var c = _atlas.Cells[cellIndex]; var px = new Color32[c.W * c.H];
            for (int y = 0; y < c.H; y++) for (int x = 0; x < c.W; x++) { bool stripe = ((x + y) / 24) % 2 == 0; byte v = (byte)(stripe ? 92 : 78); px[y * c.W + x] = new Color32(v, (byte)(v + 4), (byte)(v + 10), 255); }
            _tex.SetPixels32(c.X, AdAtlas.Height - c.Y - c.H, c.W, c.H, px);
        }

        void EnsureAtlas() { if (_tex == null) BuildAtlas(); }

        void RebuildMesh()
        {
            _dirty = false; var verts = new List<Vector3>(); var uvs = new List<Vector2>(); var tris = new List<int>(); int real = 0;
            bool any = _fixture; if (!any) foreach (var f in _faces.Values) if (_state.ItemFor(f.SlotId, _clock) != null) { any = true; break; }
            if (!any) { if (_go != null) _go.SetActive(false); RealFaces = 0; return; }                         // nothing is being advertised here: every board keeps the shipped look
            EnsureAtlas();
            foreach (var f in _faces.Values)
            {
                if (!_fixture && _state.ItemFor(f.SlotId, _clock) == null) continue;                              // a board without a campaign is left as it was
                var corners = f.Corners(); const float lift = 0.03f;
                string aspect = f.Aspect; int cell = _cellOf.TryGetValue(f.SlotId, out var c0) ? c0 : (AdAtlas.IsPortrait(aspect) ? AdAtlas.FallbackPortraitCell : AdAtlas.FallbackCell);
                if (_cellOf.ContainsKey(f.SlotId)) real++;
                var uv = _atlas.UvRect(cell, aspect); int b = verts.Count;
                for (int i = 0; i < 4; i++) verts.Add(new Vector3(corners[i].X + f.Normal.X * lift, corners[i].Y, corners[i].Z + f.Normal.Z * lift));
                uvs.Add(new Vector2(uv.u0, uv.v0)); uvs.Add(new Vector2(uv.u1, uv.v0)); uvs.Add(new Vector2(uv.u1, uv.v1)); uvs.Add(new Vector2(uv.u0, uv.v1));
                // wound so the face looks along its Normal
                var n = Vector3.Cross(verts[b + 1] - verts[b], verts[b + 3] - verts[b]); bool front = Vector3.Dot(n, new Vector3(f.Normal.X, 0f, f.Normal.Z)) > 0f;
                if (front) { tris.Add(b); tris.Add(b + 1); tris.Add(b + 2); tris.Add(b); tris.Add(b + 2); tris.Add(b + 3); } else { tris.Add(b); tris.Add(b + 2); tris.Add(b + 1); tris.Add(b); tris.Add(b + 3); tris.Add(b + 2); }
            }
            _mesh.Clear(); _mesh.SetVertices(verts); _mesh.SetUVs(0, uvs); _mesh.SetTriangles(tris, 0); _mesh.RecalculateBounds(); _mesh.RecalculateNormals();
            RealFaces = real; _go.SetActive(verts.Count > 0);
        }

        // ------------------------------------------------------------------------------------------------------------------------------------------------ chunks
        void OnFaces(Vector2Int chunk, List<AdFace> faces) { _chunks[chunk] = faces; foreach (var f in faces) _faces[f.SlotId] = f; _dirty = true; if (_fixture) InstallFixture(faces); }
        void OnUnloaded(Vector2Int chunk) { if (!_chunks.TryGetValue(chunk, out var fs)) return; _chunks.Remove(chunk); foreach (var f in fs) { _faces.Remove(f.SlotId); _cellOf.Remove(f.SlotId); } _dirty = true; }

        // ------------------------------------------------------------------------------------------------------------------------------------------------ the loop
        void Update()
        {
            if (!Enabled) return;
            _clock += Time.unscaledDeltaTime; _tick++;
            if (_player == null) { var d = FindFirstObjectByType<DrivingSession>(); if (d != null && d.Vehicle != null) _player = d.Vehicle.transform; }
            if (!_fixture) { if (!_fetching && _svc.Api.IsSignedIn && _state.ShouldFetch(_clock)) StartCoroutine(Fetch()); if (!_downloading && _player != null) MaybeDownload(); }
            if (_dirty) RebuildMesh();
            if (_player != null && Time.unscaledTime >= _nextExposure) { _nextExposure = Time.unscaledTime + 0.2f; Measure(0.2f); }
            if (!_fixture && !_sending && _meter.DueToFlush) StartCoroutine(Send());
            if (Time.unscaledTime >= _nextLog && (_faces.Count > 0 || _fixture)) { _nextLog = Time.unscaledTime + 20f; Debug.Log("[Ads] faces=" + _faces.Count + " real=" + RealFaces + " cellsInUse=" + _atlas.CellsInUse + " atlasMB=" + (AdAtlas.Width * AdAtlas.Height * 4 / 1048576) + (_fixture ? " fixture" : "")); }
        }

        IEnumerator Fetch()
        {
            _fetching = true; var task = _svc.Ads.LoadManifest(_routeId, _state.Version);
            while (!task.IsCompleted) yield return null;
            _fetching = false;
            if (task.IsFaulted || !task.Result.Ok || task.Result.Value == null)
            {
                _state.OnFailed(_clock); if (!_loggedDown) { _loggedDown = true; Debug.Log("[Ads] service unavailable; boards keep the last good advertisements or the fallback (" + (task.IsFaulted ? "error" : task.Result.ErrorCode) + ")"); }
                yield break;
            }
            _loggedDown = false; var m = task.Result.Value; var items = new List<AdItem>();
            if (!m.unchanged && m.items != null) foreach (var i in m.items) items.Add(new AdItem { SlotId = i.slot_id, BoardId = i.board_id, CampaignId = i.campaign_id, CreativeId = i.creative_id, Aspect = i.aspect, Label = i.label, Image = i.image, Sha256 = i.sha256, Link = i.link, Width = i.width, Height = i.height });
            _state.OnFetched(_clock, m.version, m.unchanged, items); _bucket = string.IsNullOrEmpty(m.bucket) ? _bucket : m.bucket;
            Debug.Log("[Ads] manifest version=" + (m.version ?? "").Substring(0, Math.Min(8, (m.version ?? "").Length)) + " items=" + (m.unchanged ? "unchanged" : items.Count.ToString()));
            // a slot whose creative changed or ended no longer points at its old image
            foreach (var slot in new List<string>(_cellOf.Keys)) { var it = _state.ItemFor(slot, _clock); if (it == null || _atlas.CellFor(it.Sha256) != _cellOf[slot]) { _cellOf.Remove(slot); _dirty = true; } }
            foreach (var kv in _state.Items) { if (_faces.ContainsKey(kv.SlotId)) { int c = _atlas.CellFor(kv.Sha256); if (c >= 0) { _cellOf[kv.SlotId] = c; _dirty = true; } } }
        }
        string _bucket = "ad-creatives-public";

        void MaybeDownload()
        {
            var wanted = _state.Wanted(_clock, new V3(_player.position.x, _player.position.y, _player.position.z), _faces.Values, it => _atlas.CellFor(it.Sha256) >= 0, 1);
            if (wanted.Count > 0) StartCoroutine(Download(wanted[0]));
        }

        IEnumerator Download(AdItem it)
        {
            _downloading = true;
            string url = _svc.Config.supabaseUrl.TrimEnd('/') + "/storage/v1/object/public/" + _bucket + "/" + it.Image;
            using (var req = UnityWebRequestTexture.GetTexture(url, false))
            {
                req.timeout = 12; yield return req.SendWebRequest();
                if (req.result != UnityWebRequest.Result.Success) { _state.ImageFailed(it.Sha256, _clock, req.responseCode == 404 || req.responseCode == 403); Debug.Log("[Ads] image not loaded (" + req.responseCode + "); the board keeps its current look"); _downloading = false; yield break; }
                var src = DownloadHandlerTexture.GetContent(req);
                if (src == null || src.width < 64 || src.height < 32 || src.width > 4096 || src.height > 4096) { _state.ImageFailed(it.Sha256, _clock, true); Destroy(src); _downloading = false; yield break; }
                PlaceImage(it, src); Destroy(src);
            }
            _downloading = false;
        }

        // copies a decoded image into its cell, scaled to the letterboxed rectangle (never stretched)
        void PlaceImage(AdItem it, Texture2D src)
        {
            EnsureAtlas();
            int cell = _atlas.Assign(it.Sha256, it.Aspect, _tick, out bool upload);
            if (upload)
            {
                var c = _atlas.Cells[cell]; var uv = _atlas.UvRect(cell, it.Aspect);
                int w = Mathf.Max(2, Mathf.RoundToInt((uv.u1 - uv.u0) * AdAtlas.Width)), h = Mathf.Max(2, Mathf.RoundToInt((uv.v1 - uv.v0) * AdAtlas.Height));
                int ox = Mathf.RoundToInt(uv.u0 * AdAtlas.Width) - c.X, oy = Mathf.RoundToInt(uv.v0 * AdAtlas.Height) - (AdAtlas.Height - c.Y - c.H);
                var bg = new Color32[c.W * c.H]; for (int i = 0; i < bg.Length; i++) bg[i] = new Color32(20, 20, 22, 255);
                var sp = src.GetPixels32(); int sw = src.width, sh = src.height;
                for (int y = 0; y < h; y++)
                {
                    float fy = (y + 0.5f) / h * sh - 0.5f; int y0 = Mathf.Clamp((int)Mathf.Floor(fy), 0, sh - 1), y1 = Mathf.Min(y0 + 1, sh - 1); float ty = fy - y0;
                    for (int x = 0; x < w; x++)
                    {
                        float fx = (x + 0.5f) / w * sw - 0.5f; int x0 = Mathf.Clamp((int)Mathf.Floor(fx), 0, sw - 1), x1 = Mathf.Min(x0 + 1, sw - 1); float tx = fx - x0;
                        var a = Color32.Lerp(sp[y0 * sw + x0], sp[y0 * sw + x1], tx); var b = Color32.Lerp(sp[y1 * sw + x0], sp[y1 * sw + x1], tx);
                        int px = ox + x, py = oy + y; if (px < 0 || py < 0 || px >= c.W || py >= c.H) continue;
                        bg[py * c.W + px] = Color32.Lerp(a, b, ty);
                    }
                }
                _tex.SetPixels32(c.X, AdAtlas.Height - c.Y - c.H, c.W, c.H, bg); _tex.Apply(false);
            }
            foreach (var f in _faces.Values) { var cur = _state.ItemFor(f.SlotId, _clock); if (cur != null && cur.Sha256 == it.Sha256) { _cellOf[f.SlotId] = cell; _meter.Loaded(f.SlotId); } }
            _state.ImageLoaded(it.Sha256); _dirty = true;
            Debug.Log("[Ads] image placed in cell " + cell + " (" + it.Aspect + ")");
        }

        // ------------------------------------------------------------------------------------------------------------------------------------------------ measurement
        void Measure(float dt)
        {
            var cam = Camera.main; if (cam == null) return; var cp = cam.transform.position; int budget = 6;
            foreach (var f in _faces.Values)
            {
                bool real = _cellOf.ContainsKey(f.SlotId);
                var centre = new Vector3(f.Centre.X, f.Centre.Y, f.Centre.Z); float dist = Vector3.Distance(cp, centre);
                if (!real || dist > f.DesignDistance) { _meter.Observe(f.SlotId, dt, false); continue; }
                var vp = cam.WorldToViewportPoint(centre); bool inView = vp.z > 0f && vp.x > 0f && vp.x < 1f && vp.y > 0f && vp.y < 1f;
                var toCam = (cp - centre); toCam.y = 0f; float angle = toCam.sqrMagnitude < 1e-4f ? 0f : Vector3.Angle(new Vector3(f.Normal.X, 0f, f.Normal.Z), toCam.normalized);
                bool occluded = false;
                if (inView && angle <= AdExposureMeter.MaxFaceAngleDeg && budget-- > 0) occluded = Physics.Linecast(cp, centre + new Vector3(f.Normal.X, 0f, f.Normal.Z) * 0.3f, ~0, QueryTriggerInteraction.Ignore);
                _meter.Observe(f.SlotId, dt, AdExposureMeter.Exposed(dist, f.DesignDistance, angle, inView, occluded, true));
            }
        }

        IEnumerator Send()
        {
            _sending = true; var entries = _meter.Flush();
            if (entries.Count > 0) { var task = _svc.Ads.SendExposure(AdExposureMeter.ToJson(entries)); while (!task.IsCompleted) yield return null; }
            _sending = false;
        }

        // ------------------------------------------------------------------------------------------------------------------------------------------------ the fixture (?smoke=drive&adtest=1): the same atlas and mesh path with generated images, no network
        void InstallFixture(List<AdFace> faces)
        {
            EnsureAtlas();
            int n = 0;
            foreach (var f in faces)
            {
                string key = "fixture-" + (n++ % 3); int cell = _atlas.Assign(key, f.Aspect, _tick, out bool up);
                if (up)
                {
                    var c = _atlas.Cells[cell]; var uv = _atlas.UvRect(cell, f.Aspect); var px = new Color32[c.W * c.H];
                    Color32 col = n % 3 == 0 ? new Color32(200, 40, 40, 255) : n % 3 == 1 ? new Color32(40, 120, 220, 255) : new Color32(240, 190, 30, 255);
                    for (int y = 0; y < c.H; y++) for (int x = 0; x < c.W; x++) px[y * c.W + x] = (((x / 32) + (y / 32)) % 2 == 0) ? col : new Color32(250, 250, 250, 255);
                    _tex.SetPixels32(c.X, AdAtlas.Height - c.Y - c.H, c.W, c.H, px); _tex.Apply(false);
                }
                _cellOf[f.SlotId] = cell; _meter.Loaded(f.SlotId);
            }
            _dirty = true; Debug.Log("[Ads] fixture installed on " + faces.Count + " faces in " + _atlas.CellsInUse + " cells");
        }
    }
}
