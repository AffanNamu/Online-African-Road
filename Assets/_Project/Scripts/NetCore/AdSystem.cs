using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace ARO.NetCore
{
    // ====================================================================================================================================================================================
    // Advertising, game side (docs/ADVERTISING_SYSTEM.md section 6). The pure rules: which prop faces can show an advertisement and what their board and slot ids are, how images share ONE atlas
    // (one extra draw call, 8 MB), when the manifest is fetched, retried and dropped, which images are worth downloading, and how exposure is MEASURED (seconds and views) without identifying anyone.
    // The Unity layer (Game/AdService.cs) only does the I/O. The game must keep working with the service down: every rule here has a fallback answer.
    // ====================================================================================================================================================================================

    /// <summary>One face of a board in the world that can carry an advertisement.</summary>
    public struct AdFace
    {
        public string BoardId, SlotId, Aspect; public V3 Centre, Right, Normal; public float YawDeg, Width, Height, DesignDistance;
        /// <summary>The corners of the face (bottom-left, bottom-right, top-right, top-left) seen from the front, in world coordinates. Right and Normal come from the same placement the prop mesh uses.</summary>
        public V3[] Corners()
        {
            float hx = Width * 0.5f, hy = Height * 0.5f; var c = Centre; var r = Right;       // locals: a struct's local function cannot capture `this`
            V3 P(float lx, float ly) => new V3(c.X + r.X * lx, c.Y + ly, c.Z + r.Z * lx);
            return new[] { P(-hx, -hy), P(hx, -hy), P(hx, hy), P(-hx, hy) };
        }
    }

    public static class AdFaces
    {
        /// <summary>The prop ids whose label face doubles as an advertising face. A production billboard declares a `label` face in the catalogue; today the placeholder `billboard` does it by default.</summary>
        public static readonly string[] BoardProps = { "billboard" };
        static readonly Regex IdRx = new Regex(@"^[A-Za-z0-9_:.-]{3,80}$", RegexOptions.Compiled);

        /// <summary>A stable, deterministic id of a board: route, the rule or placement that put it there, and its rounded position. The same route data always gives the same ids (a test proves it).</summary>
        public static string BoardId(string routeId, PropInstance i)
        {
            string origin = new string((i.Origin ?? "x").Where(c => char.IsLetterOrDigit(c) || c == '_' || c == '-' || c == '.').ToArray());
            string id = routeId + ":" + origin + ":" + (int)Math.Round(i.Pos.X) + "_" + (int)Math.Round(i.Pos.Z);
            return id.Length <= 80 && IdRx.IsMatch(id) ? id : routeId.Substring(0, Math.Min(routeId.Length, 30)) + ":b:" + (int)Math.Round(i.Pos.X) + "_" + (int)Math.Round(i.Pos.Z);
        }
        public static string SlotId(string boardId, int face = 1) => boardId + "#" + face;

        public static string AspectOf(float w, float h)
        {
            float r = w / Math.Max(0.01f, h); string best = "2:1"; float bd = float.MaxValue;
            foreach (var (name, v) in new[] { ("2:1", 2f), ("3:1", 3f), ("1:2", 0.5f), ("16:9", 16f / 9f), ("5:3", 5f / 3f), ("1:1", 1f) })
            { float d = Math.Abs((float)Math.Log(r / v)); if (d < bd) { bd = d; best = name; } }
            return best;
        }

        /// <summary>The advertising faces among the placed props.</summary>
        public static IEnumerable<AdFace> Place(string routeId, IEnumerable<PropInstance> instances, Func<string, PropDef> lookup)
        {
            foreach (var inst in instances)
            {
                if (!BoardProps.Contains(inst.Prop)) continue;
                var def = lookup(inst.Prop); if (!PropLabelLayout.Resolve(def, out var face)) continue;
                var wp = PropBatcher.Place(face.Local, inst.Pos, inst.YawDeg, inst.Scale);
                float s = inst.Scale > 0f ? inst.Scale : 1f, w = face.Width * s, h = face.Height * s;
                string board = BoardId(routeId, inst);
                var px = PropBatcher.Place(new V3(face.Local.X + 1f, face.Local.Y, face.Local.Z), inst.Pos, inst.YawDeg, inst.Scale);
                var pz = PropBatcher.Place(new V3(face.Local.X, face.Local.Y, face.Local.Z + 1f), inst.Pos, inst.YawDeg, inst.Scale);
                V3 Unit(V3 a, V3 b) { float dx = a.X - b.X, dz = a.Z - b.Z, l = (float)Math.Sqrt(dx * dx + dz * dz); return l < 1e-6f ? new V3(1f, 0f, 0f) : new V3(dx / l, 0f, dz / l); }
                yield return new AdFace { BoardId = board, SlotId = SlotId(board), Aspect = AspectOf(w, h), Centre = new V3(wp.X, wp.Y, wp.Z), Right = Unit(px, wp), Normal = Unit(pz, wp), YawDeg = inst.YawDeg + face.YawDeg, Width = w, Height = h, DesignDistance = Math.Min(250f, Math.Max(60f, h * 100f)) };
            }
        }
    }

    // ------------------------------------------------------------------------------------------------------------------------------------------------------------------------ the atlas
    /// <summary>
    /// One 2048 x 1024 texture (8 MB RGBA) shared by every advertising face: a fixed grid of cells, so there is no fragmentation and no per-board texture. Landscape images (2:1, 3:1, 16:9, 5:3, 1:1) use the 512 x 256 cells,
    /// portrait images (1:2) the 256 x 512 cells. An image of another shape than its cell is letterboxed (never stretched): UvRect says where it sits. Cell 0 holds the neutral fallback artwork.
    /// Least recently used cells are reused; a cell that a board near the player is using right now is pinned by being touched every frame.
    /// </summary>
    public sealed class AdAtlas
    {
        public const int Width = 2048, Height = 1024;
        public struct Cell { public int Index, X, Y, W, H; public bool Portrait; }
        public readonly Cell[] Cells;
        readonly string[] _owner; readonly long[] _used; readonly Dictionary<string, int> _byKey = new Dictionary<string, int>();
        public const int FallbackCell = 0, FallbackPortraitCell = 12;

        public AdAtlas()
        {
            var list = new List<Cell>();
            for (int row = 0; row < 4; row++) for (int col = 0; col < 2; col++) list.Add(new Cell { Index = list.Count, X = col * 512, Y = row * 256, W = 512, H = 256 });                // 0..7: left half
            for (int row = 0; row < 4; row++) list.Add(new Cell { Index = list.Count, X = 1536, Y = row * 256, W = 512, H = 256 });                                                      // 8..11: right strip
            for (int row = 0; row < 2; row++) for (int col = 0; col < 2; col++) list.Add(new Cell { Index = list.Count, X = 1024 + col * 256, Y = row * 512, W = 256, H = 512, Portrait = true });   // 12..15: portrait
            Cells = list.ToArray(); _owner = new string[Cells.Length]; _used = new long[Cells.Length];
            _owner[FallbackCell] = "@fallback"; _owner[FallbackPortraitCell] = "@fallback"; _used[FallbackCell] = _used[FallbackPortraitCell] = long.MaxValue;
        }

        public static bool IsPortrait(string aspect) => aspect == "1:2";
        public int Count => Cells.Length;
        public int CellFor(string key) => _byKey.TryGetValue(key, out var i) ? i : -1;
        public string OwnerOf(int cell) => _owner[cell];

        /// <summary>The cell for an image (by content hash), loading it into the least recently used cell of the right kind when it is not in the atlas. Returns the cell and whether it has to be (re)drawn.</summary>
        public int Assign(string key, string aspect, long tick, out bool needsUpload)
        {
            needsUpload = false;
            if (_byKey.TryGetValue(key, out var have)) { _used[have] = tick; return have; }
            bool portrait = IsPortrait(aspect); int pick = -1; long oldest = long.MaxValue;
            for (int i = 0; i < Cells.Length; i++)
            {
                if (Cells[i].Portrait != portrait || _owner[i] == "@fallback") continue;
                if (_owner[i] == null) { pick = i; break; }
                if (_used[i] < oldest) { oldest = _used[i]; pick = i; }
            }
            if (pick < 0) return portrait ? FallbackPortraitCell : FallbackCell;
            if (_owner[pick] != null) _byKey.Remove(_owner[pick]);
            _owner[pick] = key; _used[pick] = tick; _byKey[key] = pick; needsUpload = true; return pick;
        }
        public void Touch(int cell, long tick) { if (cell >= 0 && cell < Cells.Length && _owner[cell] != "@fallback") _used[cell] = tick; }
        public void Release(string key) { if (_byKey.TryGetValue(key, out var i)) { _byKey.Remove(key); _owner[i] = null; _used[i] = 0; } }
        public int CellsInUse => _byKey.Count;

        /// <summary>The part of the cell an image of this shape covers, as texture coordinates (u0, v0, u1, v1; v measured from the BOTTOM like Unity textures), centred in the cell, never stretched.</summary>
        public (float u0, float v0, float u1, float v1) UvRect(int cell, string aspect)
        {
            var c = Cells[cell]; float ar = AspectValue(aspect), cr = c.W / (float)c.H; float w = c.W, h = c.H;
            if (ar > cr) h = c.W / ar; else w = c.H * ar;
            float x = c.X + (c.W - w) * 0.5f, y = c.Y + (c.H - h) * 0.5f;
            return (x / Width, 1f - (y + h) / Height, (x + w) / Width, 1f - y / Height);
        }
        public static float AspectValue(string aspect)
        {
            switch (aspect) { case "2:1": return 2f; case "3:1": return 3f; case "1:2": return 0.5f; case "16:9": return 16f / 9f; case "5:3": return 5f / 3f; case "1:1": return 1f; default: return 2f; }
        }
    }

    // ------------------------------------------------------------------------------------------------------------------------------------------------------------------------ manifest policy
    public sealed class AdItem { public string SlotId, BoardId, CampaignId, CreativeId, Aspect, Label, Image, Sha256, Link; public int Width, Height; }

    /// <summary>When to ask for the manifest, what to do when it fails, when to throw it away, and which images to fetch. No network here: the caller passes the time and reports outcomes.</summary>
    public sealed class AdManifestState
    {
        public const double RefreshSeconds = 420.0, MaxAgeSeconds = 24 * 3600.0, MaxBackoffSeconds = 1800.0, ImageRetrySeconds = 120.0;
        public const float StreamRadius = 400f;
        public string Version { get; private set; } = ""; public IReadOnlyList<AdItem> Items { get; private set; } = new AdItem[0];
        double _fetchedAt = double.NegativeInfinity, _nextTry; int _failures;
        readonly Dictionary<string, double> _imageRetryAt = new Dictionary<string, double>(); readonly HashSet<string> _failedHard = new HashSet<string>();

        public bool HasManifest => Items.Count > 0 && !double.IsNegativeInfinity(_fetchedAt);
        public int Failures => _failures;
        public bool ShouldFetch(double now) => now >= _nextTry;
        public void OnFetched(double now, string version, bool unchanged, IReadOnlyList<AdItem> items)
        {
            _failures = 0; _fetchedAt = now; _nextTry = now + RefreshSeconds;
            if (!unchanged) { Version = version ?? ""; Items = items ?? new AdItem[0]; }
        }
        /// <summary>Any error (network, 4xx, 5xx, bad JSON, expired token): remember nothing about it except when to try again (1, 2, 4 ... minutes, at most 30). The last good manifest stays in use.</summary>
        public void OnFailed(double now) { _failures++; _nextTry = now + Math.Min(MaxBackoffSeconds, 60.0 * Math.Pow(2, Math.Min(_failures - 1, 5))); }
        /// <summary>A manifest older than a day is dropped: the boards show the fallback artwork.</summary>
        public IReadOnlyList<AdItem> Active(double now) => now - _fetchedAt > MaxAgeSeconds ? new AdItem[0] : Items;
        public AdItem ItemFor(string slotId, double now) => Active(now).FirstOrDefault(i => i.SlotId == slotId);

        /// <summary>Images worth fetching now: items whose face is within the stream radius of the player and whose image is not in the atlas, nearest first.</summary>
        public List<AdItem> Wanted(double now, V3 player, IEnumerable<AdFace> faces, Func<AdItem, bool> inAtlas, int max = 1)
        {
            var near = new List<(float d, AdItem it)>();
            foreach (var f in faces)
            {
                var it = ItemFor(f.SlotId, now); if (it == null || string.IsNullOrEmpty(it.Image) || _failedHard.Contains(it.Sha256) || inAtlas(it)) continue;
                if (_imageRetryAt.TryGetValue(it.Sha256, out var at) && now < at) continue;
                float dx = f.Centre.X - player.X, dz = f.Centre.Z - player.Z, d = (float)Math.Sqrt(dx * dx + dz * dz);
                if (d <= StreamRadius) near.Add((d, it));
            }
            return near.OrderBy(x => x.d).Select(x => x.it).Distinct().Take(max).ToList();
        }
        public void ImageFailed(string sha, double now, bool permanent) { if (permanent) _failedHard.Add(sha); else _imageRetryAt[sha] = now + ImageRetrySeconds; }
        public void ImageLoaded(string sha) { _imageRetryAt.Remove(sha); _failedHard.Remove(sha); }
    }

    // ------------------------------------------------------------------------------------------------------------------------------------------------------------------------ exposure
    /// <summary>
    /// MEASURED exposure, per face. A board counts as exposed only while the player is within its design reading distance, it is inside the camera's view, faces the player within 60 degrees, is not hidden behind
    /// something, and shows a REAL advertisement (not the fallback and not a texture that is merely loaded). A view is at least 1.5 seconds of exposure in one pass; a lapse of more than 1 second ends the pass.
    /// Seconds and views are summed per face; nothing identifies a player. `loaded` is a separate diagnostic count and is never a view.
    /// </summary>
    public sealed class AdExposureMeter
    {
        public const float ViewSeconds = 1.5f, LapseSeconds = 1f, MaxFaceAngleDeg = 60f, FlushSeconds = 60f, MaxSecondsPerFlush = 900f;
        sealed class Acc { public float Seconds, Pass, SinceLast; public int Views; public bool PassCounted; public int Loaded; public int Clicks; }
        readonly Dictionary<string, Acc> _acc = new Dictionary<string, Acc>(); float _sinceFlush;

        public static bool Exposed(float distance, float designDistance, float angleDeg, bool inView, bool occluded, bool showingReal)
            => showingReal && inView && !occluded && distance <= designDistance && angleDeg <= MaxFaceAngleDeg;

        public void Observe(string slotId, float dt, bool exposed)
        {
            if (!_acc.TryGetValue(slotId, out var a)) _acc[slotId] = a = new Acc();
            if (exposed)
            {
                a.Seconds += dt; a.Pass += dt; a.SinceLast = 0f;
                if (!a.PassCounted && a.Pass >= ViewSeconds) { a.Views++; a.PassCounted = true; }
            }
            else
            {
                a.SinceLast += dt;
                if (a.SinceLast > LapseSeconds) { a.Pass = 0f; a.PassCounted = false; }
            }
            _sinceFlush += dt;
        }
        public void Loaded(string slotId) { if (!_acc.TryGetValue(slotId, out var a)) _acc[slotId] = a = new Acc(); a.Loaded = 1; }
        public void Clicked(string slotId) { if (!_acc.TryGetValue(slotId, out var a)) _acc[slotId] = a = new Acc(); a.Clicks = 1; }
        public bool DueToFlush => _sinceFlush >= FlushSeconds;

        public struct Entry { public string SlotId; public float Seconds; public int Views, Loaded, Clicks; }
        /// <summary>The entries to send (at most 40 faces, at most MaxSecondsPerFlush seconds in all) and a reset of what was taken. Faces with nothing to report are left out.</summary>
        public List<Entry> Flush()
        {
            var res = new List<Entry>(); float total = 0f;
            foreach (var kv in _acc.OrderByDescending(k => k.Value.Seconds).ToList())
            {
                var a = kv.Value; if (a.Seconds <= 0f && a.Views == 0 && a.Loaded == 0 && a.Clicks == 0) continue;
                if (res.Count >= 40 || total + a.Seconds > MaxSecondsPerFlush) break;
                float sec = Math.Min(120f, a.Seconds); total += sec;
                res.Add(new Entry { SlotId = kv.Key, Seconds = (float)Math.Round(sec, 1), Views = Math.Min(5, a.Views), Loaded = a.Loaded, Clicks = a.Clicks });
                a.Seconds -= sec; a.Views = 0; a.Loaded = 0; a.Clicks = 0;
            }
            _sinceFlush = 0f; return res;
        }
        public static string ToJson(IEnumerable<Entry> entries)
        {
            var sb = new System.Text.StringBuilder("[");
            bool first = true;
            foreach (var e in entries)
            {
                if (!first) sb.Append(','); first = false;
                sb.Append("{\"slot_id\":\"").Append(e.SlotId.Replace("\\", "").Replace("\"", "")).Append("\",\"seconds\":").Append(e.Seconds.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture))
                  .Append(",\"views\":").Append(e.Views).Append(",\"loaded\":").Append(e.Loaded).Append(",\"clicks\":").Append(e.Clicks).Append('}');
            }
            return sb.Append(']').ToString();
        }
    }
}
