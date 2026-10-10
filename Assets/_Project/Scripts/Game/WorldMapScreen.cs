using System;
using System.Collections.Generic;
using System.Linq;
using ARO.Backend;
using ARO.NetCore;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using UInput = UnityEngine.Input;

namespace ARO.Game
{
    public sealed class WorldMapActions { public Action Back, Jobs, Bus, FreeDrive; public Action<JobDto> AcceptJob; }

    /// <summary>
    /// The strategic world map: the supplied artwork in a zoomable, pannable viewport with selectable cities and routes.
    /// This is UI only. It is NOT the 3D world and shares no code with the road, terrain, vehicle or traffic systems.
    /// All geography comes from Resources/WorldMap/world_map.json (validated); the artwork is illustration and decides nothing.
    /// </summary>
    public sealed class WorldMapScreen : MonoBehaviour
    {
        const float MaxZoom = 8f, TopInset = 96f, Gap = 24f;
        static readonly Color Gold = Brand.Gold, Ink = Brand.Ink, Muted = Brand.Muted, Grey = new Color(0.62f, 0.64f, 0.70f);
        static readonly Color PanelFill = new Color(0.06f, 0.068f, 0.09f, 0.97f), Line = new Color(1, 1, 1, 0.12f);

        // ---- model + services
        static WorldMapModel _shared; static bool _loadTried;
        public static WorldMapModel Model()
        {
            if (_loadTried) return _shared; _loadTried = true;
            var ta = Resources.Load<TextAsset>("WorldMap/world_map");
            if (ta == null) { Debug.LogError("[WorldMap] Resources/WorldMap/world_map.json is missing."); return null; }
            var data = JsonUtility.FromJson<WorldMapData>(ta.text);
            var errors = WorldMapValidator.Validate(data);
            if (errors.Count > 0) { Debug.LogError("[WorldMap] data failed validation:\n  " + string.Join("\n  ", errors)); return null; }
            return _shared = new WorldMapModel(data);
        }
        public static Texture2D Artwork(WorldMapModel m) => m == null ? null : Resources.Load<Texture2D>("WorldMap/" + m.Data.image.file);

        GameServices _svc; DrivingSession _drive; WorldMapActions _act; WorldMapModel _m; Texture2D _tex; JobDto[] _jobs; public JobDto[] JobsOverride;
        int Level => _svc.Profile == null ? 1 : Math.Max(_svc.Profile.level, LevelMath.LevelFor(_svc.Profile.experience));
        float CW => _m.Data.image.contentWidth; float CH => _m.Data.image.contentHeight;

        // ---- view state (pan = offset of the artwork centre from the viewport centre, in canvas units)
        float _zoom = 1f, _zoomT = 1f; Vector2 _pan, _panT; bool _pinching; float _pinchPrev;
        Canvas _canvas; RectTransform _root, _vp, _frame, _contentRt, _lineLayer, _markerLayer, _panel, _panelContent, _tip; ScrollRect _scroll;
        RawImage _art; Text _zoomLabel, _tipText, _hint, _playerText, _notice; float _noticeUntil; RectTransform _player; Image _playerGlow;
        Vector2 _lastCanvas; bool _landscape = true; float _panelW;
        readonly List<MarkerVis> _markers = new List<MarkerVis>(); readonly List<RouteVis> _routes = new List<RouteVis>();
        Texture2D _dashTex, _solidTex;
        enum Mode { Overview, City, Route } Mode _mode; string _selId, _hoverCity; float _logAt; bool _logDirty;

        sealed class MarkerVis { public MapCity city; public RectTransform rt; public Image ring, dot, glow, lockIcon; public CityState state; }
        sealed class Leg { public RectTransform gRt, cRt; public RawImage g, c; public MapCity a, b; }
        sealed class RouteVis { public MapRoute route; public List<Leg> legs = new List<Leg>(); }

        public static WorldMapScreen Create(GameServices svc, DrivingSession drive, WorldMapActions act, JobDto[] jobsOverride = null)
        {
            var go = new GameObject("WorldMap"); var s = go.AddComponent<WorldMapScreen>(); s.JobsOverride = jobsOverride; s.Build(svc, drive, act); return s;
        }

        // ================================================================== build
        void Build(GameServices svc, DrivingSession drive, WorldMapActions act)
        {
            _svc = svc; _drive = drive; _act = act; _m = Model(); _tex = Artwork(_m);
            _canvas = UIKit.CreateCanvas("WorldMapCanvas", 25); _canvas.transform.SetParent(transform, false); _root = (RectTransform)_canvas.transform;
            var bg = Ui.Flat(_root, "Bg", new Color(0.028f, 0.033f, 0.048f, 1f), 0, 0, 10, 10); Stretch(bg.rectTransform, 0, 0, 0, 0);
            BuildTopBar();
            if (_m == null || _tex == null) { Ui.Label(_root, "The world map data could not be loaded.", "SemiBold", 28, Brand.Bad, 60, 160, 1200, 40); Debug.LogError("[WorldMap] model or artwork missing; showing the error screen."); return; }
            _dashTex = MakeTex(16, 4, (x, y) => x < 9 ? new Color(1, 1, 1, 1) : new Color(1, 1, 1, 0), TextureWrapMode.Repeat);
            _solidTex = MakeTex(4, 4, (x, y) => Color.white, TextureWrapMode.Clamp);
            BuildViewport(); BuildMarkers(); BuildPanel();
            _jobs = JobsOverride; if (_jobs == null) LoadJobs();
            ShowOverview();
            Debug.Log($"[WorldMap] opened. artwork={_tex.width}x{_tex.height} content={CW}x{CH} cities={_m.Cities.Count} routes={_m.Routes.Count} level={Level} texFormat={_tex.graphicsFormat} texMB={UnityEngine.Profiling.Profiler.GetRuntimeMemorySizeLong(_tex) / 1048576f:0.0}");
        }

        static Texture2D MakeTex(int w, int h, Func<int, int, Color> f, TextureWrapMode wrap)
        {
            var t = new Texture2D(w, h, TextureFormat.RGBA32, false) { wrapMode = wrap, filterMode = FilterMode.Bilinear };
            for (int y = 0; y < h; y++) for (int x = 0; x < w; x++) t.SetPixel(x, y, f(x, y)); t.Apply(false, true); return t;
        }
        static void Stretch(RectTransform rt, float l, float b, float r, float t)
        { rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one; rt.pivot = new Vector2(0.5f, 0.5f); rt.offsetMin = new Vector2(l, b); rt.offsetMax = new Vector2(-r, -t); }
        static RectTransform CenterBox(Transform parent, string name)
        {
            var go = new GameObject(name, typeof(RectTransform)); go.transform.SetParent(parent, false); var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f); rt.sizeDelta = Vector2.zero; return rt;
        }

        void BuildTopBar()
        {
            Ui.Outlined(_root, "BackBtnFrame", new Color(0.05f, 0.055f, 0.07f, 0.9f), new Color(1, 1, 1, 0.18f), 24, 20, 132, 56, 16);
            var arrow = Ui.Icon(_root, "ic_chevron_right", White, 36, 32, 32); arrow.rectTransform.localEulerAngles = new Vector3(0, 0, 180);
            Ui.Label(_root, "Back", "SemiBold", 24, White, 76, 32, 70, 32);
            Ui.Click(_root, "Btn_Back", 24, 20, 132, 56, () => Close());
            Ui.Label(_root, "WORLD MAP", "ExtraBold", 32, White, 184, 18, 500, 40);
            Ui.Label(_root, "W E S T   A F R I C A", "SemiBold", 14, Gold, 186, 58, 400, 20);
            _notice = Ui.Label(_root, "", "SemiBold", 18, Gold, 620, 34, 900, 28);
        }
        public void Notify(string m, float seconds = 3f) { if (_notice != null) { _notice.text = m; _noticeUntil = Time.unscaledTime + seconds; } }
        static readonly Color White = Color.white;

        void BuildViewport()
        {
            var frameGo = new GameObject("Frame", typeof(RectTransform)); frameGo.transform.SetParent(_root, false); _frame = (RectTransform)frameGo.transform;
            var vpGo = new GameObject("Viewport", typeof(RectTransform), typeof(Image), typeof(RectMask2D), typeof(MapSurface)); vpGo.transform.SetParent(_root, false);
            _vp = (RectTransform)vpGo.transform; vpGo.GetComponent<Image>().color = new Color(0.02f, 0.025f, 0.035f, 1f);
            var surf = vpGo.GetComponent<MapSurface>(); surf.Dragged = OnDragMap; surf.Scrolled = OnScrollMap; surf.Clicked = OnClickMap;
            var artGo = new GameObject("Artwork", typeof(RectTransform), typeof(RawImage)); artGo.transform.SetParent(_vp, false); _contentRt = (RectTransform)artGo.transform;
            _contentRt.anchorMin = _contentRt.anchorMax = _contentRt.pivot = new Vector2(0.5f, 0.5f); _contentRt.sizeDelta = new Vector2(CW, CH);
            _art = artGo.GetComponent<RawImage>(); _art.texture = _tex; _art.raycastTarget = false;
            var (u, v, w, h) = _m.ContentUv(); _art.uvRect = new Rect(u, v, w, h);
            // soft vignette so the artwork melts into the frame
            Edge("VigTop", true, true); Edge("VigBottom", true, false); Edge("VigLeft", false, true); Edge("VigRight", false, false);
            _lineLayer = CenterBox(_vp, "Lines"); _markerLayer = CenterBox(_vp, "Markers");
            // gold hairline frame with corner accents (decorative, drawn outside the clipped viewport)
            Stretch(_frame, 0, 0, 0, 0);
            Hair("FT", 0, 0, 1, 0, 2f, true); Hair("FB", 0, 0, 1, 0, 2f, false); Hair("FL", 0, 0, 0, 1, 2f, true); Hair("FR", 0, 0, 0, 1, 2f, false);
            // controls
            var hintPill = Ui.Rounded(_vp, "HintBg", new Color(0.03f, 0.035f, 0.05f, 0.8f), 0, 0, 470, 34, 17, Ui.Anchor.BottomLeft); hintPill.rectTransform.anchoredPosition = new Vector2(16, 16);
            _hint = Ui.Label(hintPill.transform, "Scroll or pinch to zoom   ·   drag to pan   ·   click a city or a route", "Regular", 15, new Color(0.88f, 0.9f, 0.94f), 0, 0, 470, 34, TextAnchor.MiddleCenter);
            _hint.rectTransform.anchorMin = Vector2.zero; _hint.rectTransform.anchorMax = Vector2.one; _hint.rectTransform.offsetMin = _hint.rectTransform.offsetMax = Vector2.zero;
            ZoomButton("Btn_ZoomIn", "+", 0, () => ZoomBy(1.5f)); ZoomButton("Btn_ZoomOut", "-", 1, () => ZoomBy(1f / 1.5f)); ZoomButton("Btn_ZoomFit", "Fit", 2, ResetView);
            _zoomLabel = Ui.Label(_vp, "x1.0", "SemiBold", 15, Muted, 0, 0, 80, 22, TextAnchor.MiddleRight);
            var zr = _zoomLabel.rectTransform; zr.anchorMin = zr.anchorMax = zr.pivot = new Vector2(1, 0); zr.anchoredPosition = new Vector2(-16, 18 + 3 * 54);
        }
        void Edge(string name, bool horizontal, bool first)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image)); go.transform.SetParent(_vp, false); var rt = (RectTransform)go.transform; var img = go.GetComponent<Image>(); img.raycastTarget = false;
            var dark = new Color(0.02f, 0.025f, 0.035f, 0.85f); var clear = new Color(0.02f, 0.025f, 0.035f, 0f);
            if (horizontal) { rt.anchorMin = new Vector2(0, first ? 1 : 0); rt.anchorMax = new Vector2(1, first ? 1 : 0); rt.pivot = new Vector2(0.5f, first ? 1 : 0); rt.sizeDelta = new Vector2(0, 70); img.sprite = Ui.GradientSprite(first ? dark : clear, first ? clear : dark, false); }
            else { rt.anchorMin = new Vector2(first ? 0 : 1, 0); rt.anchorMax = new Vector2(first ? 0 : 1, 1); rt.pivot = new Vector2(first ? 0 : 1, 0.5f); rt.sizeDelta = new Vector2(70, 0); img.sprite = Ui.GradientSprite(first ? dark : clear, first ? clear : dark, true); }
        }
        void Hair(string name, float a, float b, float c, float d, float thick, bool topOrLeft)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image)); go.transform.SetParent(_frame, false); var rt = (RectTransform)go.transform; var img = go.GetComponent<Image>(); img.raycastTarget = false; img.color = new Color(Gold.r, Gold.g, Gold.b, 0.55f);
            bool horiz = name == "FT" || name == "FB";
            if (horiz) { rt.anchorMin = new Vector2(0, topOrLeft ? 1 : 0); rt.anchorMax = new Vector2(1, topOrLeft ? 1 : 0); rt.pivot = new Vector2(0.5f, topOrLeft ? 1 : 0); rt.sizeDelta = new Vector2(0, thick); }
            else { rt.anchorMin = new Vector2(topOrLeft ? 0 : 1, 0); rt.anchorMax = new Vector2(topOrLeft ? 0 : 1, 1); rt.pivot = new Vector2(topOrLeft ? 0 : 1, 0.5f); rt.sizeDelta = new Vector2(thick, 0); }
        }
        void ZoomButton(string name, string label, int index, Action click)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button)); go.transform.SetParent(_vp, false); var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(1, 0); rt.sizeDelta = new Vector2(46, 46); rt.anchoredPosition = new Vector2(-16, 16 + index * 54);
            var img = go.GetComponent<Image>(); img.sprite = UIGfx.Glass; img.type = Image.Type.Sliced; img.pixelsPerUnitMultiplier = 18f / 12f; img.color = new Color(0.05f, 0.055f, 0.07f, 0.92f);
            var b = go.GetComponent<Button>(); b.targetGraphic = img; var cb = b.colors; cb.highlightedColor = new Color(1f, 0.9f, 0.6f); cb.pressedColor = new Color(0.7f, 0.7f, 0.7f); b.colors = cb; b.onClick.AddListener(() => click());
            var t = Ui.Label(go.transform, label, "Bold", label.Length > 1 ? 15 : 26, Gold, 0, 0, 46, 46, TextAnchor.MiddleCenter); var tr = t.rectTransform; tr.anchorMin = Vector2.zero; tr.anchorMax = Vector2.one; tr.offsetMin = tr.offsetMax = Vector2.zero;
        }

        void BuildMarkers()
        {
            foreach (var r in _m.Routes)
            {
                var rv = new RouteVis { route = r };
                for (int i = 1; i < r.via.Length; i++)
                {
                    var leg = new Leg { a = _m.City(r.via[i - 1]), b = _m.City(r.via[i]) };
                    leg.g = NewLine(_lineLayer, "Glow_" + r.id); leg.c = NewLine(_lineLayer, "Line_" + r.id); leg.gRt = leg.g.rectTransform; leg.cRt = leg.c.rectTransform; rv.legs.Add(leg);
                }
                _routes.Add(rv);
            }
            foreach (var c in _m.Cities)
            {
                var go = new GameObject("City_" + c.id, typeof(RectTransform), typeof(Image), typeof(Button), typeof(MarkerHover)); go.transform.SetParent(_markerLayer, false);
                var rt = (RectTransform)go.transform; rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f); rt.sizeDelta = new Vector2(46, 46);
                var hit = go.GetComponent<Image>(); hit.color = new Color(1, 1, 1, 0.001f);
                var btn = go.GetComponent<Button>(); btn.targetGraphic = hit; btn.transition = Selectable.Transition.None; string id = c.id; btn.onClick.AddListener(() => SelectCity(id));
                var hv = go.GetComponent<MarkerHover>(); hv.Enter = () => { _hoverCity = id; }; hv.Exit = () => { if (_hoverCity == id) _hoverCity = null; };
                var mv = new MarkerVis { city = c, rt = rt };
                mv.glow = Spr(rt, "Glow", UIGfx.Disc, 56); mv.ring = Spr(rt, "Ring", UIGfx.Ring, 28); mv.dot = Spr(rt, "Dot", UIGfx.Disc, 10);
                var lockGo = Ui.Icon(rt, "icon_lock", Grey, 0, 0, 16); mv.lockIcon = lockGo; var lr = lockGo.rectTransform; lr.anchorMin = lr.anchorMax = lr.pivot = new Vector2(0.5f, 0.5f); lr.anchoredPosition = new Vector2(12, -12);
                _markers.Add(mv);
            }
            // tooltip chip + player marker sit above everything
            var tip = Ui.Outlined(_markerLayer, "Tip", new Color(0.04f, 0.045f, 0.06f, 0.95f), new Color(Gold.r, Gold.g, Gold.b, 0.8f), 0, 0, 170, 34, 10, 1.2f); _tip = tip.rectTransform;
            _tip.anchorMin = _tip.anchorMax = _tip.pivot = new Vector2(0.5f, 0.5f); _tipText = Ui.Label(_tip, "", "SemiBold", 17, White, 0, 0, 170, 34, TextAnchor.MiddleCenter);
            var tt = _tipText.rectTransform; tt.anchorMin = Vector2.zero; tt.anchorMax = Vector2.one; tt.offsetMin = tt.offsetMax = Vector2.zero; _tip.gameObject.SetActive(false);
            _player = CenterBox(_markerLayer, "Player"); _player.sizeDelta = new Vector2(30, 30);
            _playerGlow = Spr(_player, "Pulse", UIGfx.Disc, 44); _playerGlow.color = new Color(0.3f, 0.85f, 1f, 0.35f);
            var pd = Spr(_player, "Disc", UIGfx.Disc, 16); pd.color = new Color(0.3f, 0.85f, 1f, 1f); var pr = Spr(_player, "Ring", UIGfx.Ring, 24); pr.color = White;
            var chip = Ui.Rounded(_player, "YouChip", new Color(0.05f, 0.35f, 0.45f, 0.95f), 0, 0, 52, 22, 11); chip.rectTransform.anchorMin = chip.rectTransform.anchorMax = chip.rectTransform.pivot = new Vector2(0.5f, 0.5f); chip.rectTransform.anchoredPosition = new Vector2(0, -26);
            _playerText = Ui.Label(chip.transform, "YOU", "Bold", 13, White, 0, 0, 52, 22, TextAnchor.MiddleCenter); var ptr = _playerText.rectTransform; ptr.anchorMin = Vector2.zero; ptr.anchorMax = Vector2.one; ptr.offsetMin = ptr.offsetMax = Vector2.zero;
        }
        static Image Spr(Transform parent, string name, Sprite sp, float size)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image)); go.transform.SetParent(parent, false); var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f); rt.sizeDelta = new Vector2(size, size); var img = go.GetComponent<Image>(); img.sprite = sp; img.raycastTarget = false; return img;
        }
        RawImage NewLine(Transform parent, string name)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(RawImage)); go.transform.SetParent(parent, false); var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f); var raw = go.GetComponent<RawImage>(); raw.raycastTarget = false; return raw;
        }

        void BuildPanel()
        {
            var go = new GameObject("Panel", typeof(RectTransform), typeof(Image)); go.transform.SetParent(_root, false); _panel = (RectTransform)go.transform;
            var img = go.GetComponent<Image>(); img.sprite = UIGfx.Glass; img.type = Image.Type.Sliced; img.pixelsPerUnitMultiplier = 18f / 18f; img.color = PanelFill;
            var sGo = new GameObject("Scroll", typeof(RectTransform), typeof(ScrollRect), typeof(RectMask2D), typeof(Image)); sGo.transform.SetParent(_panel, false); var sr = (RectTransform)sGo.transform;
            Stretch(sr, 0, 0, 0, 0); sGo.GetComponent<Image>().color = new Color(1, 1, 1, 0.001f);
            var cGo = new GameObject("Content", typeof(RectTransform)); cGo.transform.SetParent(sr, false); _panelContent = (RectTransform)cGo.transform;
            _panelContent.anchorMin = new Vector2(0, 1); _panelContent.anchorMax = new Vector2(1, 1); _panelContent.pivot = new Vector2(0.5f, 1f); _panelContent.sizeDelta = new Vector2(0, 800); _panelContent.anchoredPosition = Vector2.zero;
            _scroll = sGo.GetComponent<ScrollRect>(); _scroll.content = _panelContent; _scroll.horizontal = false; _scroll.vertical = true; _scroll.movementType = ScrollRect.MovementType.Clamped; _scroll.scrollSensitivity = 30f; _scroll.inertia = true;
        }

        // ================================================================== layout + per-frame update
        void Layout()
        {
            var size = _root.rect.size; if (size.x < 2 || size == _lastCanvas) return; _lastCanvas = size;
            _landscape = size.x / size.y >= 1.25f;
            if (_landscape)
            {
                _panelW = Mathf.Clamp(size.x * 0.28f, 380f, 540f);
                _panel.anchorMin = new Vector2(1, 0); _panel.anchorMax = new Vector2(1, 1); _panel.pivot = new Vector2(1, 0.5f);
                _panel.sizeDelta = new Vector2(_panelW, -(TopInset + Gap)); _panel.anchoredPosition = new Vector2(-Gap, (Gap - TopInset) / 2f);
                Stretch(_vp, Gap, Gap, _panelW + Gap * 2, TopInset);
            }
            else
            {
                float ph = size.y * 0.42f; _panelW = size.x - Gap * 2;
                _panel.anchorMin = new Vector2(0, 0); _panel.anchorMax = new Vector2(1, 0); _panel.pivot = new Vector2(0.5f, 0);
                _panel.sizeDelta = new Vector2(-Gap * 2, ph); _panel.anchoredPosition = new Vector2(0, Gap);
                Stretch(_vp, Gap, ph + Gap * 2, Gap, TopInset);
            }
            Stretch(_frame, _vp.offsetMin.x, _vp.offsetMin.y, -_vp.offsetMax.x, -_vp.offsetMax.y);
            ClampImmediate(); RebuildPanel(); _logDirty = true; _logAt = Time.unscaledTime + 0.6f;
        }
        void ClampImmediate() { ClampPan(); _pan = _panT; _zoom = _zoomT; }

        float Fit() { var s = _vp.rect.size; return Mathf.Max(0.0001f, Mathf.Min(s.x / CW, s.y / CH)); }
        float ScaleNow => Fit() * _zoom;
        Vector2 ToView(float px, float py) { float s = ScaleNow; return new Vector2(_pan.x + (px - CW * 0.5f) * s, _pan.y - (py - CH * 0.5f) * s); }
        void ClampPan()
        {
            float s = Fit() * _zoomT; var vs = _vp.rect.size; float hx = Mathf.Max(0, CW * s * 0.5f - vs.x * 0.5f), hy = Mathf.Max(0, CH * s * 0.5f - vs.y * 0.5f);
            _panT = new Vector2(Mathf.Clamp(_panT.x, -hx, hx), Mathf.Clamp(_panT.y, -hy, hy));
        }

        void ZoomAt(Vector2 local, float factor)
        {
            float nz = Mathf.Clamp(_zoomT * factor, 1f, MaxZoom); if (Mathf.Approximately(nz, _zoomT)) return;
            float k = nz / _zoomT; _panT = local - (local - _panT) * k; _zoomT = nz; ClampPan(); Touched(); HideHint();
        }
        void ZoomBy(float factor) => ZoomAt(Vector2.zero, factor);
        void ResetView() { _zoomT = 1f; _panT = Vector2.zero; Touched(); }
        void Touched() { _logDirty = true; _logAt = Time.unscaledTime + 0.8f; }
        void HideHint() { if (_hint != null) _hint.transform.parent.gameObject.SetActive(false); }

        Vector2 LocalOf(Vector2 screen) { RectTransformUtility.ScreenPointToLocalPointInRectangle(_vp, screen, null, out var l); return l; }
        void OnDragMap(PointerEventData e) { if (_pinching) return; _panT += e.delta / _canvas.scaleFactor; ClampPan(); Touched(); HideHint(); }
        void OnScrollMap(PointerEventData e) { float y = e.scrollDelta.y; if (Mathf.Abs(y) < 0.01f) return; ZoomAt(LocalOf(e.position), Mathf.Exp(Mathf.Sign(y) * 0.22f)); }
        void OnClickMap(PointerEventData e)
        {
            var local = LocalOf(e.position); string best = null; float bd = 18f;   // canvas units
            foreach (var rv in _routes) foreach (var leg in rv.legs)
            {
                var a = ToView(leg.a.mapX, leg.a.mapY); var b = ToView(leg.b.mapX, leg.b.mapY); float d = DistToSegment(local, a, b);
                if (d < bd) { bd = d; best = rv.route.id; }
            }
            if (best != null) SelectRoute(best);
        }
        static float DistToSegment(Vector2 p, Vector2 a, Vector2 b) { var ab = b - a; float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / Mathf.Max(ab.sqrMagnitude, 1e-4f)); return (p - (a + ab * t)).magnitude; }

        void HandleKeys(float dt)
        {
            try
            {
                if (UInput.GetKeyDown(KeyCode.Escape)) { Close(); return; }
                float pan = 720f * dt, zf = Mathf.Exp(1.3f * dt); Vector2 d = Vector2.zero;
                if (UInput.GetKey(KeyCode.LeftArrow) || UInput.GetKey(KeyCode.A)) d.x += pan; if (UInput.GetKey(KeyCode.RightArrow) || UInput.GetKey(KeyCode.D)) d.x -= pan;
                if (UInput.GetKey(KeyCode.UpArrow) || UInput.GetKey(KeyCode.W)) d.y -= pan; if (UInput.GetKey(KeyCode.DownArrow) || UInput.GetKey(KeyCode.S)) d.y += pan;
                if (d != Vector2.zero) { _panT += d; ClampPan(); Touched(); HideHint(); }
                if (UInput.GetKey(KeyCode.Equals) || UInput.GetKey(KeyCode.KeypadPlus) || UInput.GetKey(KeyCode.E)) ZoomAt(Vector2.zero, zf);
                if (UInput.GetKey(KeyCode.Minus) || UInput.GetKey(KeyCode.KeypadMinus) || UInput.GetKey(KeyCode.Q)) ZoomAt(Vector2.zero, 1f / zf);
                if (UInput.GetKeyDown(KeyCode.Alpha0) || UInput.GetKeyDown(KeyCode.Home)) ResetView();
                // two-finger pinch (touch screens); one finger drags through the normal pointer events
                if (UInput.touchCount == 2)
                {
                    var t0 = UInput.GetTouch(0); var t1 = UInput.GetTouch(1); float dist = Vector2.Distance(t0.position, t1.position);
                    if (_pinching && _pinchPrev > 1f) ZoomAt(LocalOf((t0.position + t1.position) * 0.5f), dist / _pinchPrev);
                    _pinching = true; _pinchPrev = dist;
                }
                else { _pinching = false; _pinchPrev = 0f; }
            }
            catch (InvalidOperationException) { /* legacy input disabled */ }
        }

        void Update()
        {
            if (_m == null || _tex == null) return;
            float dt = Time.unscaledDeltaTime; Layout(); if (_vp.rect.width < 2) return;
            HandleKeys(dt);
            float k = 1f - Mathf.Exp(-14f * dt); _zoom = Mathf.Lerp(_zoom, _zoomT, k); _pan = Vector2.Lerp(_pan, _panT, k);
            if (Mathf.Abs(_zoom - _zoomT) < 0.0005f) _zoom = _zoomT; if ((_pan - _panT).sqrMagnitude < 0.01f) _pan = _panT;
            Apply();
            if (_logDirty && Time.unscaledTime > _logAt) { _logDirty = false; LogState(); }
            if (_notice != null && _notice.text.Length > 0 && Time.unscaledTime > _noticeUntil) _notice.text = "";
        }

        void Apply()
        {
            float s = ScaleNow; _contentRt.localScale = new Vector3(s, s, 1f); _contentRt.anchoredPosition = _pan;
            if (_zoomLabel != null) _zoomLabel.text = "x" + _zoom.ToString("0.0");
            float pulse = 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 3f);
            int level = Level;
            foreach (var mv in _markers)
            {
                var p = ToView(mv.city.mapX, mv.city.mapY); mv.rt.anchoredPosition = p;
                mv.state = _m.StateFor(mv.city, level); bool sel = _mode == Mode.City && _selId == mv.city.id, hov = _hoverCity == mv.city.id;
                Color c = mv.state == CityState.Open ? Gold : mv.state == CityState.Planned ? new Color(0.92f, 0.94f, 0.98f, 0.95f) : new Color(0.62f, 0.64f, 0.70f, 0.6f);
                float rs = sel ? 36f : hov ? 33f : 27f; mv.ring.rectTransform.sizeDelta = new Vector2(rs, rs); mv.ring.color = c;
                mv.dot.color = c; mv.dot.rectTransform.sizeDelta = Vector2.one * (sel ? 12f : 9f);
                mv.glow.color = new Color(Gold.r, Gold.g, Gold.b, sel ? 0.25f + 0.2f * pulse : hov ? 0.18f : (mv.state == CityState.Open ? 0.10f : 0f));
                mv.lockIcon.enabled = mv.state == CityState.Locked && Brand.Icon("icon_lock") != null;
            }
            // route lines
            foreach (var rv in _routes)
            {
                var st = _m.StateFor(rv.route, level); bool sel = _mode == Mode.Route && _selId == rv.route.id; bool inCity = _mode == Mode.City && rv.route.via.Contains(_selId);
                foreach (var leg in rv.legs)
                {
                    var a = ToView(leg.a.mapX, leg.a.mapY); var b = ToView(leg.b.mapX, leg.b.mapY); var d = b - a; float len = d.magnitude; float ang = Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg; var mid = (a + b) * 0.5f;
                    bool dashed = st != RouteState.Prototype && st != RouteState.Live; float thick = sel ? 5.5f : inCity ? 4.5f : dashed ? 3f : 4.5f;
                    Color col = st == RouteState.Prototype || st == RouteState.Live ? Gold : st == RouteState.PlannedAvailable ? new Color(Gold.r, Gold.g, Gold.b, 0.75f) : new Color(0.7f, 0.72f, 0.78f, 0.45f);
                    if (sel || inCity) col = Gold;
                    Place(leg.cRt, mid, len, thick, ang); leg.c.color = col; leg.c.texture = dashed && !(sel || inCity) ? _dashTex : _solidTex;
                    leg.c.uvRect = dashed && !(sel || inCity) ? new Rect(0, 0, len / 16f, 1) : new Rect(0, 0, 1, 1);
                    Place(leg.gRt, mid, len, thick + (sel ? 12f : 8f), ang); leg.g.texture = _solidTex; leg.g.color = new Color(Gold.r, Gold.g, Gold.b, sel ? 0.30f : (st == RouteState.Prototype ? 0.16f : 0f));
                }
            }
            // tooltip chip for the hovered or selected city
            string tipId = _hoverCity ?? (_mode == Mode.City ? _selId : null);
            if (tipId != null) { var c = _m.City(tipId); var p = ToView(c.mapX, c.mapY) + new Vector2(0, 40); _tipText.text = c.name; _tip.gameObject.SetActive(true); _tip.anchoredPosition = p; _tip.SetAsLastSibling(); }
            else _tip.gameObject.SetActive(false);
            // player marker
            _player.anchoredPosition = ToView(PlayerPx().x, PlayerPx().y); _playerGlow.rectTransform.sizeDelta = Vector2.one * (40f + 10f * pulse); _player.SetAsLastSibling();
        }
        static void Place(RectTransform rt, Vector2 mid, float len, float thick, float ang) { rt.anchoredPosition = mid; rt.sizeDelta = new Vector2(len, thick); rt.localRotation = Quaternion.Euler(0, 0, ang); }

        // ---- where the player is (derived, not invented): at the prototype corridor's depot, or along it while driving in it
        Vector2 PlayerPx() { var r = _m.Route("lagos-ibadan"); var a = _m.City(r.via[0]); var b = _m.City(r.via[r.via.Length - 1]); float f = CorridorProgress(); return new Vector2(Mathf.Lerp(a.mapX, b.mapX, f), Mathf.Lerp(a.mapY, b.mapY, f)); }
        public float CorridorProgress()
        {
            if (_drive == null || _drive.Vehicle == null || _drive.Route == null || _drive.Route.nodes.Length < 2) return 0f;
            var n = _drive.Route.nodes; var p = new Vector2(_drive.Vehicle.transform.position.x, _drive.Vehicle.transform.position.z);
            float total = 0f, bestD = float.MaxValue, bestAt = 0f;
            for (int i = 1; i < n.Length; i++)
            {
                var a = new Vector2(n[i - 1].position.x, n[i - 1].position.z); var b = new Vector2(n[i].position.x, n[i].position.z); float seg = (b - a).magnitude;
                var ab = b - a; float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / Mathf.Max(ab.sqrMagnitude, 1e-4f)); float d = (p - (a + ab * t)).magnitude;
                if (d < bestD) { bestD = d; bestAt = total + seg * t; } total += seg;
            }
            return total <= 0 ? 0f : Mathf.Clamp01(bestAt / total);
        }
        string PlayerSentence() => _drive != null && _drive.Vehicle != null
            ? $"You are on the prototype corridor, about {CorridorProgress() * 100f:0}% of the way from Lagos to Ibadan."
            : "Your depot: Lagos (Apapa Port Depot).";

        // ================================================================== selection + panel
        void SelectCity(string id) { _mode = Mode.City; _selId = id; Debug.Log($"[WorldMap] selected city={id} state={_m.StateFor(_m.City(id), Level)}"); RebuildPanel(); Touched(); }
        void SelectRoute(string id) { _mode = Mode.Route; _selId = id; var r = _m.Route(id); Debug.Log($"[WorldMap] selected route={id} state={_m.StateFor(r, Level)} straightKm={_m.StraightLineKm(r):0} roadKm={r.roadKm:0}"); RebuildPanel(); Touched(); }
        void ShowOverview() { _mode = Mode.Overview; _selId = null; RebuildPanel(); Touched(); }

        void RebuildPanel()
        {
            if (_panelContent == null || _m == null) return;
            for (int i = _panelContent.childCount - 1; i >= 0; i--) Destroy(_panelContent.GetChild(i).gameObject);
            float W = _panelW - 44f; float y = 22f; var p = _panelContent;
            switch (_mode) { case Mode.Overview: y = DrawOverview(p, W, y); break; case Mode.City: y = DrawCity(p, W, y); break; default: y = DrawRoute(p, W, y); break; }
            _panelContent.sizeDelta = new Vector2(0, y + 24f); _scroll.verticalNormalizedPosition = 1f;
            Debug.Log($"[WorldMap] panel mode={_mode} id={_selId}");
        }

        float Back(Transform p, float W, float y)
        {
            var a = Ui.Icon(p, "ic_chevron_right", Gold, 22, y + 2, 22); a.rectTransform.localEulerAngles = new Vector3(0, 0, 180);
            Ui.Label(p, "All routes", "SemiBold", 17, Gold, 48, y, 200, 26); Ui.Click(p, "Btn_PanelBack", 18, y - 6, 160, 38, () => ShowOverview()); return y + 36f;
        }
        static string StateText(RouteState s, int unlock) => s == RouteState.Prototype ? "PROTOTYPE" : s == RouteState.Live ? "LIVE" : s == RouteState.PlannedAvailable ? "PLANNED" : "LOCKED · LEVEL " + unlock;
        static Color StateColor(RouteState s) => s == RouteState.Prototype ? Gold : s == RouteState.Live ? Brand.Good : s == RouteState.PlannedAvailable ? new Color(0.88f, 0.9f, 0.95f) : Grey;
        float Chip(Transform p, string text, Color c, float x, float y)
        {
            var t = Ui.Label(p, text, "Bold", 13, c, x + 12, y + 5, 300, 20); float w = t.preferredWidth + 24f;
            Ui.Outlined(p, "Chip", new Color(c.r * 0.18f, c.g * 0.18f, c.b * 0.18f, 0.9f), new Color(c.r, c.g, c.b, 0.7f), x, y, w, 30, 15, 1.2f).transform.SetSiblingIndex(t.transform.GetSiblingIndex()); t.transform.SetAsLastSibling(); return w + 8f;
        }
        float Para(Transform p, string text, int size, Color c, float W, float y, string weight = "Regular")
        { var t = Ui.Paragraph(p, text, weight, size, c, 22, y, W, 20); float h = t.preferredHeight; t.rectTransform.sizeDelta = new Vector2(W, h); return y + h + 10f; }
        float Row(Transform p, string label, string value, float W, float y)
        { Ui.Label(p, label, "Regular", 16, Muted, 22, y, 160, 24); var v = Ui.Paragraph(p, value, "SemiBold", 17, White, 190, y - 1, W - 168, 24); float h = Mathf.Max(24f, v.preferredHeight); v.rectTransform.sizeDelta = new Vector2(W - 168, h); return y + h + 8f; }
        float Btn(Transform p, string name, string label, float W, float y, bool primary, bool enabled, Action click)
        {
            var o = Ui.Rounded(p, name + "_Bg", enabled ? (primary ? Gold : new Color(0.16f, 0.18f, 0.23f, 1f)) : new Color(0.14f, 0.15f, 0.19f, 1f), 22, y, W, 50, 12);
            Ui.Label(p, label, "Bold", 19, enabled ? (primary ? Ink : White) : new Color(0.5f, 0.52f, 0.58f), 22, y + 10, W, 30, TextAnchor.UpperCenter);
            if (enabled) Ui.Click(p, name, 22, y, W, 50, click); return y + 62f;
        }
        float Section(Transform p, string text, float y) { Ui.Label(p, text, "Bold", 13, Muted, 22, y, 400, 20); Ui.Flat(p, "Sep", Line, 22, y + 24, 100, 1); return y + 34f; }

        float DrawOverview(Transform p, float W, float y)
        {
            Ui.Label(p, "STRATEGIC MAP", "Bold", 13, Gold, 22, y, 400, 20); y += 24;
            Ui.Label(p, _m.Data.name, "ExtraBold", 32, White, 22, y, W, 42); y += 50;
            int proto = _m.Routes.Count(q => WorldMapModel.StatusOf(q) != RouteStatus.Planned), planned = _m.Routes.Count - proto;
            float cx = 22; cx += Chip(p, proto + (proto == 1 ? " PROTOTYPE ROUTE" : " PROTOTYPE ROUTES"), Gold, cx, y); Chip(p, planned + " PLANNED", new Color(0.88f, 0.9f, 0.95f), cx, y); y += 44;
            y = Para(p, PlayerSentence(), 16, new Color(0.55f, 0.88f, 1f), W, y);
            y = Section(p, "ROUTES", y);
            int level = Level;
            foreach (var r in _m.Routes.OrderBy(q => WorldMapModel.StatusOf(q) == RouteStatus.Planned ? 1 : 0).ThenBy(q => q.unlockLevel))
            {
                var st = _m.StateFor(r, level); string id = r.id;
                Ui.Outlined(p, "RouteCard", new Color(0.05f, 0.056f, 0.075f, 1f), st == RouteState.Prototype ? new Color(Gold.r, Gold.g, Gold.b, 0.55f) : Line, 22, y, W, 64, 12, 1.2f);
                Ui.Label(p, r.name, "Bold", 19, st == RouteState.PlannedLocked ? Grey : White, 38, y + 8, W - 60, 26);
                Ui.Label(p, StateText(st, r.unlockLevel), "SemiBold", 13, StateColor(st), 38, y + 36, 260, 20);
                string km = r.roadKm > 0 ? $"{r.roadKm:0} km" : $"≈ {_m.StraightLineKm(r):0} km"; Ui.Label(p, km, "SemiBold", 15, Muted, 22 + W - 120, y + 20, 100, 24, TextAnchor.MiddleRight);
                Ui.Click(p, "Route_" + id, 22, y, W, 64, () => SelectRoute(id)); y += 74;
            }
            y = Section(p, "MARKERS", y + 6);
            y = Legend(p, "o", Gold, "Open: something real can be driven today (prototype only)", W, y);
            y = Legend(p, "o", new Color(0.92f, 0.94f, 0.98f), "Planned: not built yet, cannot be driven", W, y);
            y = Legend(p, "L", Grey, "Locked: reach the level shown to discover it", W, y);
            y = Para(p, "The map is an illustration. Distances come from geographic coordinates, never from the picture.", 14, Muted, W, y + 8);
            return y;
        }
        float Legend(Transform p, string kind, Color c, string text, float W, float y)
        {
            Ui.Disc(p, "LegendDot", c, 24, y + 5, 12); var t = Ui.Paragraph(p, text, "Regular", 15, new Color(0.84f, 0.86f, 0.9f), 48, y, W - 30, 20); float h = Mathf.Max(22f, t.preferredHeight); t.rectTransform.sizeDelta = new Vector2(W - 30, h); return y + h + 6f;
        }

        float DrawCity(Transform p, float W, float y)
        {
            var c = _m.City(_selId); int level = Level; var st = _m.StateFor(c, level);
            y = Back(p, W, y);
            Ui.Label(p, c.name, "ExtraBold", 34, White, 22, y, W, 44); y += 46;
            Ui.Label(p, _m.CountryName(c.country), "SemiBold", 18, Muted, 22, y, W, 26); y += 36;
            float cx = 22; string stText = st == CityState.Open ? "OPEN · PROTOTYPE" : st == CityState.Locked ? "LOCKED" : "PLANNED"; Chip(p, stText, st == CityState.Open ? Gold : st == CityState.Locked ? Grey : new Color(0.88f, 0.9f, 0.95f), cx, y); y += 44;
            y = Row(p, "Coordinates", $"{c.lat:0.0000}°, {c.lon:0.0000}°", W, y);
            y = Section(p, "ROUTES THROUGH " + c.name.ToUpperInvariant(), y + 6);
            var rs = _m.RoutesAt(c.id).ToList();
            if (rs.Count == 0) y = Para(p, "No route is planned through this city yet.", 16, Muted, W, y);
            foreach (var r in rs)
            {
                var rst = _m.StateFor(r, level); string id = r.id;
                Ui.Outlined(p, "RouteCard", new Color(0.05f, 0.056f, 0.075f, 1f), Line, 22, y, W, 56, 12, 1.2f);
                Ui.Label(p, r.name, "Bold", 18, rst == RouteState.PlannedLocked ? Grey : White, 38, y + 6, W - 40, 26); Ui.Label(p, StateText(rst, r.unlockLevel), "SemiBold", 13, StateColor(rst), 38, y + 32, 260, 20);
                Ui.Click(p, "Route_" + id, 22, y, W, 56, () => SelectRoute(id)); y += 66;
            }
            if (st == CityState.Open)
            {
                var proto = rs.First(q => WorldMapModel.IsDrivable(_m.StateFor(q, level)));
                y = Section(p, "OPEN JOBS", y + 6); int n = JobsTouching(proto, c).Count();
                y = Para(p, _jobs == null ? "Loading jobs..." : $"{n} open job{(n == 1 ? "" : "s")} touch {c.name}.", 17, White, W, y, "SemiBold");
                y = Btn(p, "Btn_ViewJobs", "View jobs", W, y + 4, true, true, () => _act.Jobs());
            }
            else y = Para(p, st == CityState.Locked ? "Not discovered yet. Keep levelling up to reveal the routes here." : "Planned. The road, terrain, vehicles and traffic for this region are not built yet, so nothing here can be driven.", 16, Muted, W, y + 6);
            return y;
        }

        IEnumerable<JobDto> JobsTouching(MapRoute r, MapCity only = null) =>
            (_jobs ?? new JobDto[0]).Where(j => j.origin != null && j.destination != null && (only == null
                ? (_m.TouchesRoute(r, j.origin.slug) || _m.TouchesRoute(r, j.destination.slug))
                : ((!string.IsNullOrEmpty(only.locationSlugPrefix)) && (j.origin.slug.StartsWith(only.locationSlugPrefix) || j.destination.slug.StartsWith(only.locationSlugPrefix)))));

        float DrawRoute(Transform p, float W, float y)
        {
            var r = _m.Route(_selId); int level = Level; var st = _m.StateFor(r, level); bool drivable = WorldMapModel.IsDrivable(st);
            y = Back(p, W, y);
            var title = Ui.Paragraph(p, r.name, "ExtraBold", 32, White, 22, y, W, 44); title.rectTransform.sizeDelta = new Vector2(W, title.preferredHeight); y += title.preferredHeight + 6;
            float cx = 22; cx += Chip(p, StateText(st, r.unlockLevel), StateColor(st), cx, y); y += 44;
            double sl = _m.StraightLineKm(r);
            y = Row(p, "Road distance", r.roadKm > 0 ? $"{r.roadKm:0} km" : "not surveyed yet", W, y);
            y = Row(p, "Straight line", $"{sl:0} km", W, y);
            y = Row(p, "Via", string.Join(" → ", r.via.Select(id => _m.City(id).name)), W, y);
            y = Row(p, "Region", !string.IsNullOrEmpty(r.region) ? r.region : string.Join(", ", r.via.Select(id => _m.CountryName(_m.City(id).country)).Distinct()), W, y);
            y = Row(p, "Unlocks at", $"Level {r.unlockLevel}" + (level >= r.unlockLevel ? "  (reached)" : $"  (you are level {level})"), W, y);
            y += 4;
            if (drivable)
            {
                y = Para(p, r.description, 16, new Color(0.84f, 0.86f, 0.9f), W, y);
                Ui.Outlined(p, "Notice", new Color(0.18f, 0.13f, 0.03f, 0.9f), new Color(Gold.r, Gold.g, Gold.b, 0.6f), 22, y, W, 62, 12, 1.2f);
                var nt = Ui.Paragraph(p, "Prototype environment: placeholder 3D, not the final world.", "SemiBold", 15, Gold, 36, y + 9, W - 28, 44); y += 76;
                y = Section(p, "OPEN JOBS ON THIS CORRIDOR", y);
                var jobs = JobsTouching(r).OrderByDescending(x => x.reward).ToList();
                if (_jobs == null) y = Para(p, "Loading jobs...", 16, Muted, W, y);
                else if (jobs.Count == 0) y = Para(p, "No open jobs touch Lagos or Ibadan right now.", 16, Muted, W, y);
                foreach (var j in jobs.Take(3)) y = JobRow(p, j, W, y);
                y = Btn(p, "Btn_ViewJobs", "View all jobs", W, y + 6, true, true, () => _act.Jobs());
                y = Btn(p, "Btn_BusRoutes", "Bus routes", W, y - 6, false, true, () => _act.Bus());
                y = Btn(p, "Btn_FreeDrive", "Free drive (prototype)", W, y - 6, false, true, () => _act.FreeDrive());
            }
            else
            {
                y = Para(p, st == RouteState.PlannedLocked ? $"Reach level {r.unlockLevel} to discover this route." : "Discovered, but not built yet.", 17, White, W, y, "SemiBold");
                y = Para(p, "This route needs its production road, terrain, vehicles and traffic before it can be driven. Nothing about it is playable today.", 16, Muted, W, y);
                y = Btn(p, "Btn_NotAvailable", "Not available yet", W, y + 6, false, false, null);
            }
            return y;
        }

        float JobRow(Transform p, JobDto j, float W, float y)
        {
            Ui.Outlined(p, "JobRow", new Color(0.05f, 0.056f, 0.075f, 1f), Line, 22, y, W, 74, 12, 1.2f);
            var t = Ui.Label(p, $"{j.origin.name} → {j.destination.name}", "Bold", 16, White, 36, y + 8, W - 130, 24); t.horizontalOverflow = HorizontalWrapMode.Wrap; t.resizeTextForBestFit = true; t.resizeTextMinSize = 12; t.resizeTextMaxSize = 16;
            Ui.Label(p, $"{j.cargo_type}  ·  {j.distance_km:0.#} km  ·  {j.reward:N0} coins", "Regular", 14, Muted, 36, y + 40, W - 130, 22);
            Ui.Rounded(p, "AcceptBg", Gold, 22 + W - 92, y + 20, 80, 34, 9); Ui.Label(p, "Accept", "Bold", 15, Ink, 22 + W - 92, y + 27, 80, 22, TextAnchor.UpperCenter);
            var job = j; Ui.Click(p, "Btn_Accept_" + j.code, 22 + W - 92, y + 20, 80, 34, () => _act.AcceptJob(job)); return y + 84;
        }

        async void LoadJobs()
        {
            try { var r = await _svc.Jobs.LoadBoard(); if (this == null) return; _jobs = r.Ok ? r.Value : new JobDto[0]; RebuildPanel(); }
            catch (Exception e) { Debug.LogWarning("[WorldMap] could not load jobs: " + e.Message); if (this != null) _jobs = new JobDto[0]; }
        }

        public void Close() { Debug.Log("[WorldMap] closed"); _act.Back(); }

        // ================================================================== test hooks (smoke mode only)
        void LogState()
        {
            if (!SmokeMode.WorldMap) return;
            var corners = new Vector3[4]; _contentRt.GetWorldCorners(corners); float w = corners[2].x - corners[0].x, h = corners[2].y - corners[0].y;
            Debug.Log($"[WorldMap] state screen={Screen.width}x{Screen.height} layout={(_landscape ? "landscape" : "portrait")} viewport={_vp.rect.width * _canvas.scaleFactor:0}x{_vp.rect.height * _canvas.scaleFactor:0} artworkOnScreen={w:0.#}x{h:0.#} aspect={(h > 0 ? w / h : 0):0.0000} zoom={_zoomT:0.00} pan={_panT.x:0.#},{_panT.y:0.#}");
            var sb = new System.Text.StringBuilder("[WorldMap] targets");
            void T(string name, RectTransform rt)
            {
                var c = new Vector3[4]; rt.GetWorldCorners(c); sb.Append($"\n  TARGET {name} x={(c[0].x + c[2].x) * 0.5f / Screen.width:0.000} y={1f - (c[0].y + c[2].y) * 0.5f / Screen.height:0.000}");
            }
            foreach (var b in _root.GetComponentsInChildren<Button>(false)) T(b.name, (RectTransform)b.transform);
            Debug.Log(sb.ToString());
        }

        // ================================================================== input surfaces
        sealed class MapSurface : MonoBehaviour, IDragHandler, IScrollHandler, IPointerClickHandler
        {
            public Action<PointerEventData> Dragged, Scrolled, Clicked;
            public void OnDrag(PointerEventData e) => Dragged?.Invoke(e);
            public void OnScroll(PointerEventData e) => Scrolled?.Invoke(e);
            public void OnPointerClick(PointerEventData e) { if (!e.dragging) Clicked?.Invoke(e); }
        }
        sealed class MarkerHover : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
        {
            public Action Enter, Exit;
            public void OnPointerEnter(PointerEventData e) => Enter?.Invoke();
            public void OnPointerExit(PointerEventData e) => Exit?.Invoke();
        }
    }
}
