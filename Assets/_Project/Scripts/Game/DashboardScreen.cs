using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ARO.Backend;
using ARO.Multiplayer;
using ARO.NetCore;
using UnityEngine;
using UnityEngine.UI;

namespace ARO.Game
{
    /// <summary>Callbacks the dashboard raises; GameFlow owns what they do.</summary>
    public sealed class DashboardActions
    {
        public Action Jobs, Garage, Shop, Convoy, Bus, Profile, FreeDrive, Resume, SignOut, WorldMap;
        public Action<JobDto> AcceptJob;
        public Action QuickJob;
    }

    /// <summary>
    /// The home dashboard: sidebar, header (progression, fuel, presence, notices), hero carousel, quick actions, your truck, milestone, featured jobs,
    /// popular routes, recent players, news and convoy. Everything shown comes from real state (profile, wallet, vehicles, job board, the world map
    /// and route data, the convoy roster) or, where no backend exists yet, from clearly labelled local data (news.json, recent players on this device).
    /// Every list has loading, empty and error states. Laid out on a 1920x1080 stage that letterboxes to any window shape.
    /// </summary>
    public sealed class DashboardScreen : MonoBehaviour
    {
        static readonly Color Gold = Brand.Gold, Ink = Brand.Ink, Muted = Brand.Muted;
        static readonly Color PanelFill = new Color(0.062f, 0.070f, 0.092f, 0.94f), PanelLine = new Color(1f, 1f, 1f, 0.10f);
        static readonly Color White = Color.white, Soft = new Color(0.86f, 0.88f, 0.92f), Blue = new Color(0.35f, 0.65f, 1f);
        const float HeroX = 331, HeroY = 142, HeroW = 1146, HeroH = 318, HeroAspect = HeroW / HeroH;

        GameServices _svc; DrivingSession _drive; BusSession _bus; DashboardActions _act; NoticeLog _notices;
        RectTransform _root, _stage, _jobsHost, _popover, _sidebar;
        Text _toast;
        float _toastUntil, _nextSlide, _heroDip = -10f, _nextNews, _nextBell; int _slide, _lastUnread = -1;
        public JobDto[] JobsOverride;   // test/preview fixture only

        // job board state
        enum JobState { Loading, Ready, Empty, Error }
        JobState _jobState = JobState.Loading; string _jobError; JobDto[] _jobs; bool _jobsLoaded, _errorServed; int _loadToken;

        // hero
        struct Slide { public string eyebrow, t1, t2, desc, button; public Action go; public Rect uv; public bool textRight; }
        Slide[] _slides; RawImage _heroPhoto; KenBurnsView _kb; Image[] _dots; Image _shadeL, _shadeR;
        Text _heroEyebrow, _heroT1, _heroT2, _heroDesc, _heroBtnText; RectTransform _heroText; GroupFade _heroFade;

        // live text that depends on loaded data
        readonly Text[] _qDesc = new Text[4]; readonly string[] _qDefault = new string[4];
        Image _bellBadge; Text _bellCount; GameObject _jobsBadge;

        // news
        NewsFeedSpec _news; List<NewsItem> _newsItems = new List<NewsItem>(); int _newsIndex; RectTransform _newsHost; Text _newsCounter;

        // data resolved once per build
        OwnedVehicleDto _vehicle; VehicleDefDto _def;

        public static DashboardScreen Create(GameServices svc, DrivingSession drive, BusSession bus, DashboardActions act, JobDto[] jobsOverride = null, NoticeLog notices = null)
        {
            var go = new GameObject("Dashboard");
            var d = go.AddComponent<DashboardScreen>(); d.JobsOverride = jobsOverride; d._notices = notices; d.Build(svc, drive, bus, act);
            return d;
        }

        public void Refresh() { if (_root != null) { Destroy(transform.GetChild(0).gameObject); _jobsLoaded = false; BuildCanvas(); } }

        void Build(GameServices svc, DrivingSession drive, BusSession bus, DashboardActions act) { _svc = svc; _drive = drive; _bus = bus; _act = act; BuildCanvas(); }

        static bool Url(string s) { var u = Application.absoluteURL; return u != null && u.Contains(s); }

        // ================================================================================================================ build
        void BuildCanvas()
        {
            ResolveVehicle(); LoadNews();
            var tex = Brand.Scene(); var model = WorldMapScreen.Model();
            string place = "Lagos, Nigeria";
            var spec = _drive != null && _drive.Route != null ? _drive.Route.Spec : null;
            if (spec != null && spec.meta != null && model != null) place = spec.meta.from + ", " + model.CountryName(spec.meta.country);
            _heroPlace = place;
            string Eb(string a, string b, string c) => a + "  <color=#F9B521>•</color>  " + b + "  <color=#F9B521>•</color>  " + c;
            _slides = new[]
            {
                new Slide { eyebrow = Eb("EXPLORE", "DELIVER", "CONNECT"), t1 = "DRIVE ACROSS", t2 = "AFRICA", desc = "Take real jobs, deliver cargo, explore cities\nand build your trucking career.",
                            button = _drive.Vehicle != null ? "Resume Driving" : "Find a Job", go = () => { if (_drive.Vehicle != null) _act.Resume(); else _act.Jobs(); },
                            uv = Ui.Cover(tex, 0f, 0.36f, 1f, HeroAspect), textRight = true },        // the truck sits on the left of this picture, so the text goes right
                new Slide { eyebrow = Eb("CONVOY", "SHARE", "EARN"), t1 = "DRIVE IN", t2 = "CONVOY", desc = "Team up with friends on the same route.\nShared roads, shared rewards.",
                            button = "Join a Convoy", go = () => _act.Convoy(), uv = Ui.Cover(tex, 0.40f, 0.50f, 0.50f, HeroAspect) },
                new Slide { eyebrow = Eb("CITIES", "PASSENGERS", "FARES"), t1 = "KEEP THE CITY", t2 = "MOVING", desc = "Run a bus route, pick up passengers at every\nstop and earn a fare for each one.",
                            button = _bus != null && _bus.Active ? "Continue Route" : "Bus Routes", go = () => _act.Bus(), uv = Ui.Cover(tex, 0.30f, 0.16f, 0.70f, HeroAspect) },
            };

            var canvas = UIKit.CreateCanvas("DashboardCanvas", 20);
            canvas.transform.SetParent(transform, false);
            var root = (RectTransform)canvas.transform;
            var bg = Ui.Flat(root, "Bg", new Color(0.028f, 0.033f, 0.048f, 1f), 0, 0, 10, 10); var brt = bg.rectTransform; brt.anchorMin = Vector2.zero; brt.anchorMax = Vector2.one; brt.offsetMin = brt.offsetMax = Vector2.zero;
            var stageGo = new GameObject("Stage", typeof(RectTransform), typeof(StageFitter)); stageGo.transform.SetParent(root, false);
            _root = (RectTransform)stageGo.transform; _root.anchorMin = _root.anchorMax = _root.pivot = new Vector2(0.5f, 0.5f); _root.sizeDelta = new Vector2(1920, 1080); _root.anchoredPosition = Vector2.zero;
            _stage = _root;

            Region("Header", 0f); BuildHeader();
            _sidebar = Region("Sidebar", 0.04f); BuildSidebar();
            Region("Hero", 0.10f); BuildHero();
            Region("Milestone", 0.16f); BuildMilestone();
            Region("Truck", 0.22f); BuildYourTruck();
            Region("Quick", 0.28f); BuildQuick();
            Region("Jobs", 0.34f); BuildFeatured();
            Region("Routes", 0.40f); BuildRoutes();
            Region("Players", 0.46f); BuildPlayers();
            Region("News", 0.50f); BuildNews();
            Region("Convoy", 0.54f); BuildConvoyCard();

            _stage = _root;                                         // toast and the notice popover sit above everything
            _toast = Ui.Label(_root, "", "SemiBold", 24, Gold, 0, 1010, 1920, 40, TextAnchor.MiddleCenter);
            _toast.rectTransform.sizeDelta = new Vector2(1920, 40);
            BuildPopover();
            ShowSlide(_slide, false); _nextSlide = Time.unscaledTime + 8f; _nextNews = Time.unscaledTime + 10f;
            if (!_jobsLoaded) { _jobsLoaded = true; _ = LoadJobs(); } else FillJobs();
            _builtAt = Time.unscaledTime;
            Debug.Log("[Dashboard] built for " + (_svc.Profile != null ? _svc.Profile.display_name : "?"));
        }

        string _heroPlace = "Lagos, Nigeria";

        /// <summary>A full-stage container for one region; it fades and rises in (staggered) when the dashboard opens. Everything built after this call lands inside it.</summary>
        RectTransform Region(string name, float delay)
        {
            var r = Ui.Box(_root, name, 0, 0, 1920, 1080); _stage = r; Entrance.On(r, delay); return r;
        }

        void ResolveVehicle()
        {
            _vehicle = null; _def = null; var list = _svc.Vehicles ?? new OwnedVehicleDto[0];
            if (_drive != null && !string.IsNullOrEmpty(_drive.VehicleId)) _vehicle = list.FirstOrDefault(v => v.id == _drive.VehicleId);
            if (_vehicle == null) _vehicle = list.FirstOrDefault();
            if (_vehicle != null && _svc.Definitions != null) _def = _svc.Definitions.FirstOrDefault(d => d.id == _vehicle.definition_id);
        }

        float FuelPercent() => _vehicle == null ? 0f : Vitals.FuelPercent(_vehicle.fuel_l, _def != null ? _def.fuel_capacity_l : 0);

        void LoadNews()
        {
            _news = null; _newsItems = new List<NewsItem>();
            var ta = Resources.Load<TextAsset>("Dashboard/news");
            if (ta == null) { Debug.LogWarning("[Dashboard] Resources/Dashboard/news.json is missing"); return; }
            var feed = JsonUtility.FromJson<NewsFeedSpec>(ta.text);
            var errors = NewsFeed.Validate(feed, new HashSet<string> { "highway", "convoy", "map", "fuel" });
            if (errors.Count > 0) { Debug.LogError("[Dashboard] invalid news.json: " + string.Join("; ", errors)); return; }
            _news = feed; _newsItems = NewsFeed.Latest(feed, 4);
        }

        // ================================================================================================================ sidebar
        void BuildSidebar()
        {
            Ui.Flat(_stage, "Sidebar", new Color(0.040f, 0.046f, 0.062f, 1f), 0, 0, 300, 1080);
            Ui.Flat(_stage, "SidebarEdge", new Color(1, 1, 1, 0.07f), 299, 0, 1, 1080);
            Ui.Icon(_stage, "logo_mark", White, 26, 24, 84);
            var a = Ui.Label(_stage, "AFRICAN", "ExtraBold", 30, White, 112, 26, 190, 36); Ui.Drop(a, 0.4f);
            Ui.Label(_stage, "ROADS", "ExtraBold", 44, Gold, 112, 54, 190, 52);
            Ui.Label(_stage, "O N L I N E", "Bold", 13, White, 114, 104, 190, 18);

            var items = new (string icon, string label, Action go, string soon)[]
            {
                ("ic_home", "Home", null, null), ("ic_jobs", "Jobs", _act.Jobs, null), ("ic_people", "Multiplayer", _act.Convoy, null),
                ("ic_bus", "Trucks & Buses", _act.Shop, null), ("ic_wrench", "Garage", _act.Garage, null), ("ic_map", "Map", _act.WorldMap, null),
                ("ic_people", "Friends", null, "Friends are coming soon."), ("ic_trophy", "Leaderboards", null, "Leaderboards are coming soon."),
                ("ic_cart", "Store", null, "The store is coming soon."), ("ic_gear", "Settings", _act.Profile, null),
            };
            for (int i = 0; i < items.Length; i++)
            {
                float y = 138 + i * 66f; bool active = i == 0;
                if (active) Ui.Rounded(_stage, "NavActive", Gold, 18, y, 264, 60, 14);
                Ui.Icon(_stage, items[i].icon, active ? Ink : new Color(0.82f, 0.84f, 0.88f), 44, y + 13, 34);
                Ui.Label(_stage, items[i].label, active ? "Bold" : "SemiBold", 24, active ? Ink : new Color(0.88f, 0.9f, 0.93f), 104, y + 14, 180, 32);
                var it = items[i];
                Ui.Click(_stage, "Nav_" + it.label.Replace(" ", "").Replace("&", "And"), 18, y, 264, 60, () => { if (it.go != null) it.go(); else if (it.soon != null) Toast(it.soon); });
            }
            // "Drive Africa. Build Together." - a real picture from the key art, not a flat block
            var tex = Brand.Scene();
            const float PX = 18, PY = 818, PW = 264, PH = 168;
            Ui.Outlined(_stage, "PromoFrame", new Color(0, 0, 0, 1), new Color(1, 1, 1, 0.10f), PX, PY, PW, PH, 16);
            Ui.Photo(_stage, "PromoPhoto", tex, Ui.Cover(tex, 0.50f, 0.30f, 0.40f, PW / PH), PX + 1.5f, PY + 1.5f, PW - 3, PH - 3, 15, new Color(0.85f, 0.85f, 0.88f));
            Ui.Gradient(_stage, "PromoShade", new Color(0.02f, 0.025f, 0.04f, 0.0f), new Color(0.02f, 0.025f, 0.04f, 0.92f), false, PX + 2, PY + 40, PW - 4, PH - 42);
            Ui.Label(_stage, "Drive Africa.", "ExtraBold", 24, Gold, PX + 16, PY + 98, 240, 30);
            Ui.Label(_stage, "Build Together.", "ExtraBold", 24, White, PX + 16, PY + 124, 240, 30);

            var so = Ui.Label(_stage, "Sign out", "SemiBold", 20, new Color(0.62f, 0.65f, 0.7f), 44, 1018, 200, 28);
            Ui.Click(_stage, "Nav_SignOut", 30, 1008, 240, 48, () => _act.SignOut());
        }

        /// <summary>The red count on the Jobs item: how many open jobs the server returned.</summary>
        void SetJobsBadge(int n)
        {
            if (_jobsBadge != null) Destroy(_jobsBadge);
            if (n <= 0 || _sidebar == null) return;
            var holder = new GameObject("JobsBadge", typeof(RectTransform)); holder.transform.SetParent(_sidebar, false); _jobsBadge = holder;
            var rt = (RectTransform)holder.transform; rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0, 1); rt.anchoredPosition = Vector2.zero; rt.sizeDelta = new Vector2(1920, 1080);
            string s = n > 99 ? "99+" : n.ToString(); float w = s.Length > 2 ? 40 : 30;
            Ui.Rounded(holder.transform, "Badge", new Color(0.92f, 0.22f, 0.22f), 256 - (w - 30), 218, w, 30, 15);
            Ui.Label(holder.transform, s, "Bold", 17, White, 256 - (w - 30), 222, w, 24, TextAnchor.UpperCenter);
        }

        // ================================================================================================================ header
        void Pill(float x, float y, float w, float h, string icon, Action click, string name = "PillBtn")
        {
            Ui.Outlined(_stage, "Pill", new Color(0.05f, 0.055f, 0.07f, 0.88f), new Color(1, 1, 1, 0.14f), x, y, w, h, 16);
            if (icon != null) Ui.Icon(_stage, icon, White, x + (w - 30) / 2f, y + (h - 30) / 2f, 30);
            Ui.Click(_stage, name, x, y, w, h, click);
        }

        void BuildHeader()
        {
            var photo = Ui.Photo(_stage, "HeaderSky", Brand.Texture("login_bg"), Ui.Top(0f, 0.40f, 1f, 0.1356f), 300, 0, 1620, 124, 0);
            Ui.Gradient(_stage, "HeaderShade", new Color(0.03f, 0.035f, 0.05f, 0.92f), new Color(0.03f, 0.035f, 0.05f, 0.25f), true, 300, 0, 1620, 124);
            Ui.Gradient(_stage, "HeaderFloor", new Color(0.028f, 0.033f, 0.048f, 0f), new Color(0.028f, 0.033f, 0.048f, 1f), false, 300, 70, 1620, 54);

            var p = _svc.Profile; string name = p != null ? p.display_name : "Driver"; long xp = p != null ? p.experience : 0; int lvl = p != null ? Math.Max(p.level, LevelMath.LevelFor(xp)) : 1;
            Ui.Disc(_stage, "AvatarRing", Gold, 330, 14, 100);
            Ui.Disc(_stage, "AvatarBg", new Color(0.12f, 0.14f, 0.18f), 334, 18, 92);
            Ui.Icon(_stage, "avatar_default", White, 334, 18, 92);
            Ui.Label(_stage, "Good to see you,", "Regular", 22, new Color(0.9f, 0.92f, 0.95f), 452, 20, 400, 30);
            var nm = Ui.Label(_stage, name, "ExtraBold", 42, White, 452, 44, 700, 54);
            Ui.Icon(_stage, "ic_crown", Gold, 452 + nm.preferredWidth + 14, 56, 32);
            Ui.Label(_stage, "Level " + lvl, "SemiBold", 22, White, 452, 96, 120, 28);
            var (into, span, frac) = LevelMath.Progress(xp);
            var fill = Ui.Bar(_stage, 556, 104, 190, 11, frac, Gold); BarGrow.Run(fill, 11, Mathf.Max(11f, 190f * frac), 0.35f, 1.0f);
            var xpText = Ui.Label(_stage, "", "Regular", 18, new Color(0.88f, 0.9f, 0.94f), 762, 98, 220, 26);
            CountText.Run(xpText, 0, into, "", $" / {span:N0} XP", 0.35f, 1.0f);

            // right cluster: fuel of the truck you drive, coins, presence, notices, settings
            BuildFuelChip();
            long balance = _svc.Wallet != null ? _svc.Wallet.balance : 0;
            Pill(1352, 24, 200, 56, null, () => Toast("Your balance is paid out by the server when a job or route completes."), "Btn_Balance");
            Ui.Icon(_stage, "ic_coin", Gold, 1370, 33, 38);
            var bal = Ui.Label(_stage, "", "Bold", 26, White, 1420, 34, 120, 36); CountText.Run(bal, 0, balance, "", "", 0.3f, 1.1f);

            bool signedIn = _svc.Api.IsSignedIn || SmokeMode.Dashboard || SmokeMode.WorldMap;     // the smoke fixtures stand in for a signed-in player
            int members = NetworkVehicle.Players.Count; string presence = Presence.Describe(signedIn, members);
            Pill(1572, 24, 150, 56, null, () => Toast(signedIn ? (members > 1 ? $"You are driving in a convoy of {members}." : "You are connected to the game server.") : "You are not connected."), "Btn_Presence");
            var dot = Ui.Disc(_stage, "OnlineDot", signedIn ? Brand.Good : Muted, 1594, 45, 14); if (signedIn) PulseImage.On(dot, 2.2f, 0.85f, 1.2f, 0.55f, 1f);
            Ui.Label(_stage, presence, "SemiBold", presence.Length > 8 ? 18 : 22, White, 1620, presence.Length > 8 ? 37 : 34, 100, 34);

            Pill(1744, 24, 60, 56, "ic_bell", ToggleNotices, "Btn_Bell");
            _bellBadge = Ui.Disc(_stage, "BellBadge", new Color(0.92f, 0.22f, 0.22f), 1782, 14, 26);
            _bellCount = Ui.Label(_stage, "", "Bold", 15, White, 1782, 17, 26, 20, TextAnchor.UpperCenter);
            RefreshBell(true);
            Pill(1824, 24, 60, 56, "ic_gear", _act.Profile, "Btn_Settings");
        }

        void BuildFuelChip()
        {
            Pill(1166, 24, 170, 56, null, () => Toast(_vehicle == null ? "You do not own a vehicle yet." : $"Fuel: {_vehicle.fuel_l:0} L of {(_def != null ? _def.fuel_capacity_l : 0):0} L."), "Btn_Fuel");
            if (_vehicle == null) { Ui.Icon(_stage, "ic_fuel", Muted, 1184, 36, 32); Ui.Label(_stage, "No vehicle", "SemiBold", 18, Muted, 1226, 40, 110, 26); return; }
            float pct = FuelPercent(); Color c = pct < 10f ? Brand.Bad : pct < 25f ? Gold : Brand.Good;
            Ui.Icon(_stage, "ic_fuel", c, 1182, 36, 32);
            Ui.Label(_stage, "Fuel", "Regular", 13, Muted, 1226, 29, 60, 16);
            var t = Ui.Label(_stage, "", "Bold", 22, White, 1226, 44, 70, 28); CountText.Run(t, 0, (long)Mathf.Round(pct), "", "%", 0.3f, 0.9f);
            var bar = Ui.Bar(_stage, 1290, 58, 36, 7, pct / 100f, c); BarGrow.Run(bar, 7, Mathf.Max(7f, 36f * pct / 100f), 0.3f, 0.9f);
        }

        // ---- notices (the bell): real in-session events, newest first
        void RefreshBell(bool force = false)
        {
            int n = _notices != null ? _notices.Unread : 0;
            if (!force && n == _lastUnread) return; _lastUnread = n;
            if (_bellBadge == null) return;
            _bellBadge.gameObject.SetActive(n > 0); _bellCount.gameObject.SetActive(n > 0);
            _bellCount.text = n > 9 ? "9+" : n.ToString();
            if (n > 0 && _bellBadge.GetComponent<PulseImage>() == null) PulseImage.On(_bellBadge, 1.4f, 0.92f, 1.14f, 0.7f, 1f);
        }

        void BuildPopover()
        {
            _popover = Ui.Box(_root, "NoticePopover", 0, 0, 1920, 1080); _popover.gameObject.SetActive(false);
        }

        void ToggleNotices()
        {
            if (_popover == null) return;
            if (_popover.gameObject.activeSelf) { _popover.gameObject.SetActive(false); return; }
            for (int i = _popover.childCount - 1; i >= 0; i--) Destroy(_popover.GetChild(i).gameObject);
            var host = _popover; var items = _notices != null ? _notices.Recent(5) : new List<Notice>();
            Ui.Click(host, "Btn_NoticeClose", 0, 0, 1920, 1080, () => _popover.gameObject.SetActive(false));
            float h = 70 + Mathf.Max(1, items.Count) * 62 + 18; const float X = 1470, Y = 94, W = 430;
            Ui.Outlined(host, "NoticePanel", new Color(0.05f, 0.058f, 0.078f, 0.98f), new Color(Gold.r, Gold.g, Gold.b, 0.55f), X, Y, W, h, 18, 1.5f);
            Ui.Click(host, "NoticeSwallow", X, Y, W, h, () => { });
            Ui.Label(host, "Notifications", "Bold", 24, White, X + 22, Y + 14, 260, 32);
            SmallPillIn(host, "Mark read", X + W - 112, Y + 18, 92, 26, () => { if (_notices != null) _notices.MarkAllRead(); RefreshBell(true); _popover.gameObject.SetActive(false); });
            if (items.Count == 0)
            {
                Ui.Label(host, "Nothing new.", "SemiBold", 20, Muted, X + 22, Y + 74, W - 44, 28);
                Ui.Paragraph(host, "Payouts, server messages and errors will show up here.", "Regular", 16, new Color(0.6f, 0.63f, 0.69f), X + 22, Y + 102, W - 44, 40);
            }
            for (int i = 0; i < items.Count; i++)
            {
                var n = items[i]; float y = Y + 66 + i * 62; Color c = n.Level == NoticeLevel.Bad ? Brand.Bad : n.Level == NoticeLevel.Good ? Brand.Good : Blue;
                Ui.Disc(host, "Dot", c, X + 22, y + 10, 12);
                var t = Ui.Paragraph(host, n.Count > 1 ? $"{n.Text}  (x{n.Count})" : n.Text, n.Read ? "Regular" : "SemiBold", 17, n.Read ? Soft : White, X + 46, y, W - 70, 44);
                Ui.Label(host, Ago(Time.unscaledTime - (float)n.At), "Regular", 14, Muted, X + 46, y + 40, 200, 18);
            }
            _popover.gameObject.SetActive(true);
            Debug.Log($"[Dashboard] notices open count={items.Count} unread={(_notices != null ? _notices.Unread : 0)}");
            Entrance.On(host, 0f, 0.25f, 10f);
            if (_notices != null) { _notices.MarkAllRead(); RefreshBell(true); }
        }

        static string Ago(float seconds) => seconds < 60 ? "just now" : seconds < 3600 ? (int)(seconds / 60) + " min ago" : (int)(seconds / 3600) + " h ago";

        // ================================================================================================================ hero
        void BuildHero()
        {
            const float X = HeroX, Y = HeroY, W = HeroW, H = HeroH;
            Ui.Outlined(_stage, "HeroFrame", new Color(0, 0, 0, 1), new Color(Gold.r, Gold.g, Gold.b, 0.85f), X - 2, Y - 2, W + 4, H + 4, 22, 2f);
            var tex = Brand.Scene();
            _heroPhoto = Ui.Photo(_stage, "HeroPhoto", tex, _slides[_slide].uv, X, Y, W, H, 20);
            _kb = KenBurnsView.On(_heroPhoto, _slides[_slide].uv);
            var dark = new Color(0.02f, 0.025f, 0.04f, 0.90f); var clear = new Color(0.02f, 0.025f, 0.04f, 0f);
            _shadeL = Ui.Gradient(_stage, "HeroShadeL", dark, clear, true, X, Y, 760, H);
            _shadeR = Ui.Gradient(_stage, "HeroShadeR", clear, dark, true, X + W - 760, Y, 760, H);
            Ui.Gradient(_stage, "HeroFloor", new Color(0, 0, 0, 0), new Color(0, 0, 0, 0.45f), false, X, Y + H - 90, W, 90);

            _heroText = Ui.Box(_stage, "HeroText", 0, 0, 1920, 1080); _heroFade = GroupFade.On(_heroText);
            _heroEyebrow = Ui.Label(_heroText, "", "SemiBold", 18, Soft, X + 32, Y + 26, 640, 26);
            _heroT1 = Ui.Label(_heroText, "", "ExtraBold", 66, White, X + 30, Y + 62, 760, 80); Ui.Drop(_heroT1);
            _heroT2 = Ui.Label(_heroText, "", "ExtraBold", 66, Gold, X + 30, Y + 124, 760, 80); Ui.Drop(_heroT2);
            _heroDesc = Ui.Paragraph(_heroText, "", "Regular", 22, White, X + 32, Y + 206, 560, 60); Ui.Drop(_heroDesc);
            var glow = Ui.Rounded(_heroText, "HeroButtonGlow", new Color(Gold.r, Gold.g, Gold.b, 0.25f), X + 26, Y + 256, 262, 66, 16); PulseImage.On(glow, 2.4f, 0.98f, 1.05f, 0.35f, 1f);
            Ui.Rounded(_heroText, "HeroButton", Gold, X + 32, Y + 262, 250, 54, 12);
            _heroBtnText = Ui.Label(_heroText, "Find a Job", "Bold", 24, Ink, X + 32 + 28, Y + 273, 170, 32);
            Ui.Icon(_heroText, "icon_arrow", Ink, X + 32 + 250 - 58, Y + 273, 32);
            Ui.Click(_heroText, "Btn_HeroAction", X + 32, Y + 262, 250, 54, () => _slides[_slide].go());

            Ui.Outlined(_stage, "LocChip", new Color(0.03f, 0.035f, 0.05f, 0.82f), new Color(1, 1, 1, 0.12f), X + W - 238, Y + H - 66, 210, 44, 12);
            Ui.Icon(_stage, "ic_pin", Gold, X + W - 238 + 16, Y + H - 66 + 9, 26);
            Ui.Label(_stage, _heroPlace, "SemiBold", 18, White, X + W - 238 + 50, Y + H - 66 + 10, 160, 26);

            _dots = new Image[_slides.Length];
            for (int i = 0; i < _slides.Length; i++)
            {
                int idx = i; _dots[i] = Ui.Disc(_stage, "Dot" + i, new Color(1, 1, 1, 0.4f), X + W - 100 + i * 26, Y + 24, 13);
                Ui.Click(_stage, "Btn_Dot" + i, X + W - 106 + i * 26, Y + 16, 26, 28, () => { _slide = idx; ShowSlide(idx, true); _nextSlide = Time.unscaledTime + 9f; });
            }
        }

        void ShowSlide(int i, bool animate)
        {
            if (_heroT1 == null) return; var s = _slides[i];
            Debug.Log("[Dashboard] slide=" + i);
            _heroEyebrow.text = s.eyebrow; _heroT1.text = s.t1; _heroT2.text = s.t2; _heroDesc.text = s.desc; _heroBtnText.text = s.button;
            float dx = s.textRight ? 548f : 0f; var home = new Vector2(dx, 0f);
            _heroFade.SetHome(home); if (animate) { _heroFade.Play(0.55f, 30f); _heroDip = Time.unscaledTime; }
            _shadeL.enabled = !s.textRight; _shadeR.enabled = s.textRight;
            if (_kb != null) _kb.Rebase(s.uv); else if (_heroPhoto != null) _heroPhoto.uvRect = s.uv;
            for (int k = 0; k < _dots.Length; k++) _dots[k].color = k == i ? Gold : new Color(1, 1, 1, 0.4f);
        }

        void Update()
        {
            float now = Time.unscaledTime;
            if (_slides != null && now > _nextSlide) { _slide = (_slide + 1) % _slides.Length; ShowSlide(_slide, true); _nextSlide = now + 8f; }
            if (_heroPhoto != null) { float p = Tween.Progress(now - _heroDip, 0f, 0.7f); _heroPhoto.color = new Color(1, 1, 1, Mathf.Lerp(0.15f, 1f, p)); }   // dips through dark and back on every slide change
            if (_toast != null && _toast.text.Length > 0 && now > _toastUntil) _toast.text = "";
            if (now > _nextBell) { _nextBell = now + 0.5f; RefreshBell(); }
            if (_newsItems.Count > 1 && now > _nextNews) { _nextNews = now + 10f; NextNews(); }
            if ((SmokeMode.WorldMap || SmokeMode.Dashboard) && !_targetsLogged && now > _builtAt + 2.5f) LogTargets();
        }

        // Smoke mode only: the browser tests need the dashboard's own click positions.
        bool _targetsLogged; float _builtAt = float.MaxValue;
        void LogTargets()
        {
            _targetsLogged = true;
            var sb = new System.Text.StringBuilder("[WorldMap] targets (dashboard)");
            foreach (var b in _root.GetComponentsInChildren<Button>(false))
            {
                var c = new Vector3[4]; ((RectTransform)b.transform).GetWorldCorners(c);
                sb.Append($"\n  TARGET {b.name} x={(c[0].x + c[2].x) * 0.5f / Screen.width:0.000} y={1f - (c[0].y + c[2].y) * 0.5f / Screen.height:0.000}");
            }
            Debug.Log(sb.ToString());
        }

        public void Toast(string m, float s = 3f) { if (_toast != null) { _toast.text = m; _toastUntil = Time.unscaledTime + s; } }

        // ================================================================================================================ milestone + truck
        Image Panel(float x, float y, float w, float h, bool gold = false)
            => Ui.Outlined(_stage, "Panel", PanelFill, gold ? new Color(Gold.r, Gold.g, Gold.b, 0.7f) : PanelLine, x, y, w, h, 18, gold ? 2f : 1.5f);

        /// <summary>The "challenge" card. There is no daily-challenge backend yet, so it shows a REAL goal: the next lifetime-distance milestone.</summary>
        void BuildMilestone()
        {
            var p = _svc.Profile; double dist = p != null ? p.distance_km : 0; var m = Milestones.Next(dist);
            Panel(1497, 142, 402, 148, true);
            Ui.Icon(_stage, "ic_trophy", Gold, 1518, 158, 46);
            Ui.Label(_stage, "Next Milestone", "Bold", 26, White, 1578, 156, 300, 34);
            Ui.Label(_stage, "Daily challenges are coming soon", "Regular", 15, Muted, 1578, 190, 300, 20);
            Ui.Label(_stage, m.Title, "SemiBold", 21, White, 1518, 224, 200, 28);
            var cur = Ui.Label(_stage, "", "Bold", 19, Gold, 1640, 226, 240, 26, TextAnchor.UpperRight);
            CountText.Run(cur, 0, (long)Math.Round(m.Current), "", $" / {Money.Format(m.Target)} km", 0.45f, 1.1f);
            var fill = Ui.Bar(_stage, 1518, 262, 362, 12, m.Fraction, Gold); BarGrow.Run(fill, 12, Mathf.Max(12f, 362f * m.Fraction), 0.5f, 1.1f);
            Ui.Icon(_stage, "ic_check_circle", Brand.Good, 1518, 266, 16);
            Ui.Label(_stage, $"{(p != null ? p.jobs_completed : 0)} jobs completed", "Regular", 14, Soft, 1540, 267, 200, 18);
        }

        void BuildYourTruck()
        {
            Panel(1497, 306, 402, 154);
            Ui.Label(_stage, "Your Truck", "Bold", 26, White, 1518, 316, 200, 34);
            SmallPill(1812, 320, 68, 26, "View All", _act.Garage);
            if (_vehicle == null)
            {   // empty state: no vehicle yet
                Ui.Icon(_stage, "truck_thumb", new Color(1, 1, 1, 0.25f), 1512, 346, 112);
                Ui.Label(_stage, "No vehicle yet", "SemiBold", 22, Muted, 1650, 362, 220, 30);
                Ui.Rounded(_stage, "TruckCta", Gold, 1650, 402, 200, 38, 10); Ui.Label(_stage, "Visit the garage", "Bold", 17, Ink, 1650, 410, 200, 24, TextAnchor.UpperCenter);
                Ui.Click(_stage, "Btn_YourTruck", 1497, 306, 402, 154, _act.Garage); return;
            }
            float fuel = FuelPercent(); double damage = _vehicle.damage_pct;
            var state = Vitals.Status(true, fuel, damage, _drive != null && _drive.Job != null);
            Color sc = state == TruckState.Ready ? Brand.Good : state == TruckState.LowFuel ? Gold : state == TruckState.OnJob ? Blue : Brand.Bad;
            Ui.Icon(_stage, "truck_thumb", White, 1510, 336, 116);
            var nm = Ui.Label(_stage, _def != null ? _def.name : _vehicle.definition_id, "SemiBold", 21, White, 1642, 352, 200, 56); nm.horizontalOverflow = HorizontalWrapMode.Wrap;
            var dot = Ui.Disc(_stage, "StatusDot", sc, 1642, 396, 14); if (state == TruckState.Ready) PulseImage.On(dot, 2.2f, 0.85f, 1.2f, 0.6f, 1f);
            Ui.Label(_stage, Vitals.Label(state), "SemiBold", 19, sc, 1664, 390, 150, 26);
            Ui.Rounded(_stage, "TruckGo", Gold, 1844, 350, 40, 40, 20); Ui.Icon(_stage, "ic_chevron_right", Ink, 1852, 358, 24);

            // real stats from the vehicle definition and its saved state
            float cond = Vitals.ConditionPercent(damage); Color cc = cond >= 80f ? Brand.Good : cond >= 50f ? Gold : Brand.Bad;
            bool bus = _def != null && _def.category == "bus";
            string cap = _def == null ? "-" : bus ? _def.passenger_capacity + " seats" : Money.Format(_def.cargo_capacity_kg) + " kg";
            Stat(1518, "ic_gauge", "Top speed", _def != null ? $"{_def.max_speed_kmh:0} km/h" : "-", White);
            Stat(1650, bus ? "ic_people" : "ic_weight", bus ? "Capacity" : "Load", cap, White);
            Stat(1780, "ic_wrench", "Condition", $"{cond:0}%", cc);
            Ui.Click(_stage, "Btn_YourTruck", 1497, 306, 402, 154, _act.Garage);
        }

        void Stat(float x, string icon, string label, string value, Color vc)
        {
            Ui.Icon(_stage, icon, Muted, x, 428, 22);
            Ui.Label(_stage, label, "Regular", 13, Muted, x + 28, 420, 100, 16);
            Ui.Label(_stage, value, "Bold", 16, vc, x + 28, 435, 104, 22);
        }

        Image SmallPill(float x, float y, float w, float h, string label, Action click) => SmallPillIn(_stage, label, x, y, w, h, click);

        Image SmallPillIn(Transform parent, string label, float x, float y, float w, float h, Action click)
        {
            var o = Ui.Outlined(parent, "SmallPill", new Color(0.05f, 0.055f, 0.07f, 0.9f), new Color(1, 1, 1, 0.16f), x, y, w, h, 8, 1.2f);
            Ui.Label(parent, label, "SemiBold", 15, White, x, y + 3, w, h - 4, TextAnchor.UpperCenter);
            Ui.Click(parent, "Btn_" + label.Replace(" ", ""), x, y, w, h, click); return o;
        }

        // ================================================================================================================ quick actions
        void BuildQuick()
        {
            var scene = Brand.Scene(); var model = WorldMapScreen.Model(); var art = WorldMapScreen.Artwork(model);
            const float cw = 379, ch = 129, aspect = cw / ch;
            var cards = new (string title, string desc, string icon, Color tint, Texture tex, Rect uv, Action go)[]
            {
                ("Quick Job", "Find and start a job\nimmediately", "ic_bus", new Color(0.95f, 0.62f, 0.10f), scene, Ui.Cover(scene, 0.10f, 0.56f, 0.34f, aspect), _act.QuickJob),
                ("Multiplayer\nConvoy", "Drive with friends\nacross Africa", "ic_people", new Color(0.25f, 0.55f, 1f), scene, Ui.Cover(scene, 0.36f, 0.52f, 0.40f, aspect), _act.Convoy),
                ("Explore Map", "Discover cities, roads and\nlocations", "ic_map", new Color(0.20f, 0.80f, 0.55f), art != null ? art : scene, art != null ? Ui.Cover(art, 0.28f, 0.45f, 0.40f, aspect) : Ui.Cover(scene, 0.0f, 0.5f, 0.4f, aspect), _act.WorldMap),
                ("Garage", "Customize and upgrade\nyour vehicles", "ic_wrench", new Color(0.65f, 0.40f, 1f), scene, Ui.Cover(scene, 0.18f, 0.52f, 0.20f, aspect), _act.Garage),
            };
            float w = 382, gap = 18, x = 316; int idx = 0;
            foreach (var c in cards)
            {
                Ui.Outlined(_stage, "QCardFrame", new Color(0, 0, 0, 1), new Color(1, 1, 1, 0.14f), x, 474, w, 132, 18);
                Ui.Photo(_stage, "QCardPhoto", c.tex, c.uv, x + 1.5f, 475.5f, w - 3, 129, 17, new Color(0.62f, 0.62f, 0.66f));
                Ui.Flat(_stage, "QTint", new Color(c.tint.r * 0.3f, c.tint.g * 0.3f, c.tint.b * 0.3f, 0.45f), x + 2, 476, w - 4, 128);
                Ui.Gradient(_stage, "QShade", new Color(0.02f, 0.025f, 0.04f, 0.88f), new Color(0.02f, 0.025f, 0.04f, 0.1f), true, x + 2, 476, w - 4, 128);
                Ui.Rounded(_stage, "QTile", new Color(c.tint.r * 0.28f, c.tint.g * 0.28f, c.tint.b * 0.28f, 0.95f), x + 20, 506, 68, 68, 16);
                Ui.Icon(_stage, c.icon, c.tint, x + 34, 520, 40);
                Ui.Label(_stage, c.title, "Bold", 27, White, x + 104, c.title.Contains("\n") ? 490 : 506, 260, 66).lineSpacing = 0.9f;
                _qDefault[idx] = c.desc;
                _qDesc[idx] = Ui.Paragraph(_stage, c.desc, "Regular", 18, new Color(0.9f, 0.92f, 0.95f), x + 104, c.title.Contains("\n") ? 548 : 544, 240, 48);
                Ui.Rounded(_stage, "QGo", Gold, x + w - 62, 546, 44, 44, 22); Ui.Icon(_stage, "ic_chevron_right", Ink, x + w - 54, 554, 28);
                Ui.Click(_stage, "Btn_Quick_" + c.title.Replace("\n", "").Replace(" ", ""), x, 474, w, 132, () => c.go());
                x += w + gap; idx++;
            }
            UpdateQuickText();
        }

        /// <summary>Quick-card subtitles carry live facts once they are known (job board, convoy roster, world map, owned vehicles).</summary>
        void UpdateQuickText()
        {
            if (_qDesc[0] == null) return;
            string job = _qDefault[0];
            if (_jobState == JobState.Ready && _jobs != null && _jobs.Length > 0) job = $"{_jobs.Length} open job{(_jobs.Length == 1 ? "" : "s")}\nBest pays {Money.Format(_jobs.Max(j => j.reward))} coins";
            else if (_jobState == JobState.Empty) job = "No open jobs right now\nCheck back soon";
            _qDesc[0].text = job;
            int members = NetworkVehicle.Players.Count;
            _qDesc[1].text = members > 1 ? $"You are in a convoy\n{members} drivers on the road" : _svc.Convoys != null && !string.IsNullOrEmpty(_svc.Convoys.CurrentConvoyName) ? $"In convoy: {_svc.Convoys.CurrentConvoyName}" : _qDefault[1];
            var model = WorldMapScreen.Model();
            if (model != null)
            {
                int level = PlayerLevel(); int drivable = model.Routes.Count(r => WorldMapModel.IsDrivable(model.StateFor(r, level)));
                _qDesc[2].text = $"{model.Cities.Count} cities · {model.Routes.Count} routes\n{drivable} drivable now";
            }
            int n = _svc.Vehicles != null ? _svc.Vehicles.Length : 0;
            _qDesc[3].text = n == 0 ? "You do not own a\nvehicle yet" : $"{n} vehicle{(n == 1 ? "" : "s")} in your garage\nUpgrade and repair";
        }

        int PlayerLevel() => _svc.Profile != null ? Math.Max(_svc.Profile.level, LevelMath.LevelFor(_svc.Profile.experience)) : 1;

        // ================================================================================================================ featured jobs
        void BuildFeatured()
        {
            Panel(316, 622, 1065, 292);
            Ui.Label(_stage, "Featured Jobs", "Bold", 26, White, 338, 636, 300, 34);
            SmallPill(1290, 640, 70, 26, "View All", _act.Jobs);
            _jobsHost = Ui.Box(_stage, "JobsHost", 316, 622, 1065, 292);
        }

        /// <summary>Waits without threads (WebGL has none): polls unscaled time once per frame.</summary>
        static async Task WaitSeconds(float seconds) { float until = Time.unscaledTime + seconds; while (Time.unscaledTime < until) await Task.Yield(); }

        async Task LoadJobs()
        {
            int token = ++_loadToken;
            SetJobState(JobState.Loading, null); FillJobs();
            if (SmokeMode.Dashboard && Url("jobs=slow")) { await WaitSeconds(9f); if (this == null || token != _loadToken) return; }
            if (SmokeMode.Dashboard && Url("jobs=error") && !_errorServed)
            {   // dev fixture: the first load fails so the error state and the retry button can be exercised; the retry succeeds
                _errorServed = true; await WaitSeconds(1.5f);
                if (this == null || token != _loadToken) return; _jobs = null; SetJobState(JobState.Error, "Could not reach the server. Check your connection and try again."); FillJobs(); return;
            }
            if (SmokeMode.Dashboard && Url("jobs=empty")) { _jobs = new JobDto[0]; SetJobState(JobState.Empty, null); FillJobs(); return; }
            if (JobsOverride != null) { _jobs = JobsOverride; SetJobState(_jobs.Length == 0 ? JobState.Empty : JobState.Ready, null); FillJobs(); return; }
            var r = await _svc.Jobs.LoadBoard();
            if (this == null || token != _loadToken) return;
            if (!r.Ok) { _jobs = null; SetJobState(JobState.Error, r.UserMessage); }
            else { _jobs = r.Value; SetJobState(_jobs.Length == 0 ? JobState.Empty : JobState.Ready, null); }
            FillJobs();
        }

        void SetJobState(JobState s, string error)
        {
            _jobState = s; _jobError = error;
            Debug.Log("[Dashboard] jobs state=" + s.ToString().ToLowerInvariant() + (error != null ? " error=" + error : "") + (_jobs != null ? " count=" + _jobs.Length : ""));
            if (s == JobState.Error && _notices != null) _notices.Add(error, NoticeLevel.Bad, Time.unscaledTime);
        }

        void FillJobs()
        {
            if (_jobsHost == null) return;
            for (int i = _jobsHost.childCount - 1; i >= 0; i--) Destroy(_jobsHost.GetChild(i).gameObject);
            UpdateQuickText();
            if (SmokeMode.Dashboard) { _targetsLogged = false; _builtAt = Time.unscaledTime - 1.5f; }      // smoke only: re-report click positions about a second after the board changes
            SetJobsBadge(_jobState == JobState.Ready && _jobs != null ? _jobs.Length : 0);
            switch (_jobState)
            {
                case JobState.Loading: BuildSkeleton(); return;
                case JobState.Error: BuildJobsMessage("ic_road", "We could not load the job board", _jobError ?? "Please try again.", Brand.Bad, "Try again", "Btn_RetryJobs", () => _ = LoadJobs()); return;
                case JobState.Empty: BuildJobsMessage("ic_box", "No open jobs right now", "The server posts new ones regularly. Check back soon, or run a bus route meanwhile.", Muted, "Open the job board", "Btn_OpenBoard", _act.Jobs); return;
            }
            var owned = new HashSet<string>((_svc.Vehicles ?? new OwnedVehicleDto[0]).Select(v => CategoryOf(v.definition_id)));
            var pick = _jobs.OrderByDescending(j => owned.Contains(j.required_category)).ThenByDescending(j => j.reward).Take(4).ToArray();
            const float cw = 246, gap = 14, x0 = 22;
            for (int i = 0; i < pick.Length; i++)
            {
                var card = Ui.Box(_jobsHost, "Card" + i, 0, 0, 1065, 292); Entrance.On(card, 0.06f * i, 0.5f, 14f);
                JobCard(card, pick[i], x0 + i * (cw + gap), 58, cw);
            }
        }

        void BuildSkeleton()
        {
            const float cw = 246, gap = 14, x0 = 22;
            for (int i = 0; i < 4; i++)
            {
                float x = x0 + i * (cw + gap), y = 58;
                Ui.Outlined(_jobsHost, "SkCard", new Color(0.045f, 0.052f, 0.07f, 0.98f), new Color(1, 1, 1, 0.07f), x, y, cw, 230, 12, 1.2f);
                var blocks = new[] { Ui.Rounded(_jobsHost, "SkPhoto", new Color(1, 1, 1, 0.3f), x + 1.2f, y + 1.2f, cw - 2.4f, 70, 11), Ui.Rounded(_jobsHost, "SkA", new Color(1, 1, 1, 0.3f), x + 10, y + 82, 170, 14, 6),
                    Ui.Rounded(_jobsHost, "SkB", new Color(1, 1, 1, 0.3f), x + 10, y + 106, 120, 14, 6), Ui.Rounded(_jobsHost, "SkC", new Color(1, 1, 1, 0.3f), x + 10, y + 138, 90, 12, 6),
                    Ui.Rounded(_jobsHost, "SkD", new Color(1, 1, 1, 0.3f), x + 10, y + 162, 70, 12, 6), Ui.Rounded(_jobsHost, "SkE", new Color(1, 1, 1, 0.3f), x + 10, y + 186, cw - 20, 28, 7) };
                for (int k = 0; k < blocks.Length; k++) Breath.On(blocks[k], i * 2 + k);
            }
            Ui.Label(_jobsHost, "Loading jobs...", "Regular", 16, Muted, 22, 36, 300, 20);
        }

        void BuildJobsMessage(string icon, string title, string body, Color titleColor, string button, string buttonName, Action onClick)
        {
            Ui.Icon(_jobsHost, icon, new Color(1, 1, 1, 0.22f), 60, 96, 84);
            Ui.Label(_jobsHost, title, "Bold", 26, titleColor == Muted ? White : titleColor, 176, 98, 800, 34);
            Ui.Paragraph(_jobsHost, body, "Regular", 19, Soft, 176, 138, 800, 56);
            Ui.Rounded(_jobsHost, "MsgBtn", Gold, 176, 204, 250, 50, 12); Ui.Label(_jobsHost, button, "Bold", 20, Ink, 176, 215, 250, 30, TextAnchor.UpperCenter);
            Ui.Click(_jobsHost, buttonName, 176, 204, 250, 50, onClick);
        }

        string CategoryOf(string defId) { var d = _svc.Definitions?.FirstOrDefault(x => x.id == defId); return d != null ? d.category : (defId != null && defId.StartsWith("bus") ? "bus" : "truck"); }

        static string CargoArt(string cargo)
        {
            switch (cargo)
            {
                case "Electronics": return "job_containers"; case "Food & Produce": case "Agricultural Goods": return "job_food";
                case "Building Materials": return "job_pipes"; case "Fuel Drums": return "job_tanker"; default: return "job_general";
            }
        }

        static string CargoChip(string cargo)
        {
            switch (cargo)
            {
                case "Electronics": return "Containers"; case "Food & Produce": case "Agricultural Goods": return "Food supplies";
                case "Building Materials": return "Construction"; case "Fuel Drums": return "Fuel"; default: return "General cargo";
            }
        }

        void JobCard(Transform host, JobDto j, float x, float y, float w)
        {
            Ui.Outlined(host, "JobCard", new Color(0.045f, 0.052f, 0.07f, 0.98f), new Color(1, 1, 1, 0.09f), x, y, w, 230, 12, 1.2f);
            Ui.Photo(host, "JobPhoto", Brand.Texture(CargoArt(j.cargo_type)), new Rect(0, 0, 1, 1), x + 1.2f, y + 1.2f, w - 2.4f, 70, 11);
            Ui.Gradient(host, "JobPhotoShade", new Color(0, 0, 0, 0), new Color(0, 0, 0, 0.7f), false, x + 1.2f, y + 36, w - 2.4f, 35);
            Ui.Label(host, CargoChip(j.cargo_type), "SemiBold", 13, White, x + 10, y + 50, w - 20, 18);
            // origin and destination on two lines so long depot names never collide with the rows below
            string from = j.origin != null ? j.origin.name : "?", to = j.destination != null ? j.destination.name : "?";
            var t1 = Ui.Label(host, from, "Bold", 17, White, x + 10, y + 76, w - 16, 22); t1.horizontalOverflow = HorizontalWrapMode.Wrap; t1.resizeTextForBestFit = true; t1.resizeTextMinSize = 12; t1.resizeTextMaxSize = 17;
            var t2 = Ui.Label(host, "→ " + to, "Bold", 17, Gold, x + 10, y + 98, w - 16, 22); t2.horizontalOverflow = HorizontalWrapMode.Wrap; t2.resizeTextForBestFit = true; t2.resizeTextMinSize = 12; t2.resizeTextMaxSize = 17;
            Ui.Icon(host, "ic_box", Muted, x + 10, y + 124, 20); Ui.Label(host, j.cargo_type, "Regular", 16, new Color(0.84f, 0.86f, 0.9f), x + 36, y + 123, w - 46, 22);
            Ui.Icon(host, "ic_road", Muted, x + 10, y + 148, 20); Ui.Label(host, $"{j.distance_km:0.#} km", "Regular", 16, new Color(0.84f, 0.86f, 0.9f), x + 36, y + 147, 90, 22);
            Ui.Icon(host, "ic_clock", Muted, x + w - 112, y + 148, 20); Ui.Label(host, "~" + JobMath.FormatDuration(JobMath.EtaMinutes(j.distance_km)), "Regular", 16, new Color(0.84f, 0.86f, 0.9f), x + w - 88, y + 147, 84, 22);
            Ui.Icon(host, "ic_coin", Gold, x + 10, y + 172, 20); Ui.Label(host, Money.Format(j.reward), "SemiBold", 16, White, x + 36, y + 171, 90, 22);
            Color dc = j.difficulty <= 2 ? Brand.Good : j.difficulty == 3 ? Gold : Brand.Bad;
            Ui.Icon(host, "ic_bars", dc, x + w - 104, y + 172, 20); Ui.Label(host, JobMath.Difficulty(j.difficulty), "SemiBold", 16, dc, x + w - 80, y + 171, 74, 22);
            Ui.Rounded(host, "AcceptBg", Gold, x + 8, y + 196, w - 16, 28, 7);
            Ui.Label(host, "Accept", "Bold", 17, Ink, x + 8, y + 199, w - 16, 24, TextAnchor.UpperCenter);
            var job = j; Ui.Click(host, "Btn_Accept_" + j.code, x + 8, y + 194, w - 16, 30, () => _act.AcceptJob(job));
        }

        // ================================================================================================================ routes: the real artwork + the validated route data (same source as the world map)
        void BuildRoutes()
        {
            Panel(1397, 622, 502, 292);
            Ui.Label(_stage, "Popular Routes", "Bold", 26, White, 1418, 636, 300, 34);
            SmallPill(1808, 640, 70, 26, "Map", _act.WorldMap);
            const float bx = 1417, by = 678, bw = 462, bh = 218;
            var model = WorldMapScreen.Model(); var tex = WorldMapScreen.Artwork(model);
            if (model == null || tex == null) { Ui.Icon(_stage, "ic_map", new Color(1, 1, 1, 0.2f), bx + 190, by + 54, 70); Ui.Label(_stage, "The map is unavailable right now.", "SemiBold", 18, Muted, bx, by + 130, bw, 26, TextAnchor.UpperCenter); return; }
            // a crop of the artwork around the Gulf of Guinea coast, in artwork pixels (origin top-left)
            const float cx0 = 880f, cy0 = 440f, cw = 760f; float ch = cw * bh / bw;
            float tw = model.Data.image.textureWidth, th = model.Data.image.textureHeight;
            Ui.Photo(_stage, "MapPhoto", tex, new Rect(cx0 / tw, (th - (cy0 + ch)) / th, cw / tw, ch / th), bx, by, bw, bh, 12);
            var host = Ui.Box(_stage, "MapHost", bx, by, bw, bh);
            Func<MapCity, Vector2> at = c => new Vector2((c.mapX - cx0) / cw * bw, (c.mapY - cy0) / ch * bh);
            Func<MapCity, bool> inside = c => c.mapX > cx0 + 8 && c.mapX < cx0 + cw - 8 && c.mapY > cy0 + 8 && c.mapY < cy0 + ch - 8;
            int level = PlayerLevel();
            foreach (var r in model.Routes)
            {
                var st = model.StateFor(r, level); bool open = WorldMapModel.IsDrivable(st);
                for (int i = 1; i < r.via.Length; i++)
                {
                    var a = model.City(r.via[i - 1]); var b = model.City(r.via[i]); if (!inside(a) || !inside(b)) continue;
                    if (open) { Ui.Segment(host, at(a), at(b), 7f, new Color(Gold.r, Gold.g, Gold.b, 0.25f)); Ui.Segment(host, at(a), at(b), 3.5f, Gold); }
                    else Ui.Segment(host, at(a), at(b), 2f, new Color(1f, 1f, 1f, st == RouteState.PlannedLocked ? 0.22f : 0.45f));
                }
            }
            // markers: gold = drivable now, blue ring = planned, red lock = locked at your level. Labels avoid overlapping each other.
            var labelled = new List<Vector2>();
            var order = model.Cities.Where(inside).OrderByDescending(c => model.StateFor(c, level) == CityState.Open).ThenByDescending(c => model.RoutesAt(c.id).Count()).ToList();
            foreach (var c in order)
            {
                var p = at(c); var st = model.StateFor(c, level);
                if (st == CityState.Open)
                {
                    var glow = Ui.Disc(host, "CityGlow", new Color(Gold.r, Gold.g, Gold.b, 0.3f), p.x - 11, p.y - 11, 22);
                    if (IsHomeCity(c)) PulseImage.On(glow, 1.8f, 0.8f, 1.6f, 0.2f, 1f);
                    Ui.Disc(host, "City", Gold, p.x - 5, p.y - 5, 10);
                }
                else if (st == CityState.Planned) { Ui.Disc(host, "CityRing", Blue, p.x - 7, p.y - 7, 14); Ui.Disc(host, "CityDot", new Color(0.05f, 0.08f, 0.14f), p.x - 4, p.y - 4, 8); }
                else { Ui.Disc(host, "CityLock", Brand.Bad, p.x - 10, p.y - 10, 20); Ui.Icon(host, "ic_lock", White, p.x - 6.5f, p.y - 6.5f, 13); }
                if (labelled.Count >= 5 || labelled.Any(q => (q - p).magnitude < 64f)) continue;
                labelled.Add(p);
                var l = Ui.Label(host, c.name, "Bold", 15, White, p.x + 12, p.y - 22, 110, 20); Ui.Drop(l, 0.9f);
                int need = model.RoutesAt(c.id).Select(r => r.unlockLevel).DefaultIfEmpty(0).Min();
                if (st == CityState.Planned) Ui.Label(host, "Planned", "SemiBold", 12, Blue, p.x + 12, p.y - 6, 90, 16);
                else if (st == CityState.Locked) Ui.Label(host, $"Lv. {need}", "SemiBold", 12, Brand.Bad, p.x + 12, p.y - 6, 90, 16);
            }
            Ui.Disc(_stage, "LegendLive", Gold, bx + 10, by + bh - 22, 9); Ui.Label(_stage, "Open", "SemiBold", 13, White, bx + 25, by + bh - 27, 60, 18);
            Ui.Disc(_stage, "LegendPlanned", Blue, bx + 76, by + bh - 22, 9); Ui.Label(_stage, "Planned", "SemiBold", 13, new Color(0.9f, 0.9f, 0.9f), bx + 91, by + bh - 27, 80, 18);
            Ui.Disc(_stage, "LegendLocked", Brand.Bad, bx + 160, by + bh - 22, 9); Ui.Label(_stage, "Locked", "SemiBold", 13, new Color(0.9f, 0.9f, 0.9f), bx + 175, by + bh - 27, 80, 18);
            Ui.Click(_stage, "Btn_RoutesMap", bx, by, bw, bh, _act.WorldMap);
        }

        /// <summary>The city the playable route starts from (the player's depot) pulses to say "you are here".</summary>
        bool IsHomeCity(MapCity c)
        {
            var spec = _drive != null && _drive.Route != null ? _drive.Route.Spec : null;
            if (spec != null && spec.meta != null) return string.Equals(c.name, spec.meta.from, StringComparison.OrdinalIgnoreCase);
            return c.id == "lagos";
        }

        // ================================================================================================================ bottom row
        const string RecentKey = "aro.recent";

        RecentEntry[] LoadRecents()
        {
            try { var json = PlayerPrefs.GetString(RecentKey, ""); return string.IsNullOrEmpty(json) ? new RecentEntry[0] : (JsonUtility.FromJson<RecentPlayersData>(json)?.entries ?? new RecentEntry[0]); }
            catch (Exception) { return new RecentEntry[0]; }
        }

        void BuildPlayers()
        {
            Panel(21, 924, 760, 142);
            Ui.Label(_stage, "Recent Players", "Bold", 24, White, 42, 938, 300, 32);
            SmallPill(690, 942, 70, 26, "View All", _act.Convoy);
            long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds(); string me = _svc.Profile != null ? _svc.Profile.display_name : "";
            var presentNames = NetworkVehicle.Players.Members.Select(m => m.Name).ToList();
            var entries = Recents.Merge(LoadRecents(), presentNames, me, now, 8);
            if (presentNames.Count > 0) { try { PlayerPrefs.SetString(RecentKey, JsonUtility.ToJson(new RecentPlayersData { entries = entries })); PlayerPrefs.Save(); } catch (Exception) { /* storage may be unavailable (private window) */ } }
            var here = new HashSet<string>(presentNames);
            if (entries.Length == 0)
            {
                Ui.Icon(_stage, "ic_people", new Color(1, 1, 1, 0.22f), 44, 984, 52);
                Ui.Label(_stage, "Players you drive with will appear here.", "SemiBold", 21, Muted, 112, 986, 620, 28);
                Ui.Label(_stage, "Create or join a convoy and your crew is remembered on this device.", "Regular", 17, new Color(0.55f, 0.58f, 0.64f), 112, 1018, 620, 24);
                Ui.Click(_stage, "Btn_PlayersEmpty", 21, 924, 760, 142, _act.Convoy); return;
            }
            for (int i = 0; i < Math.Min(4, entries.Length); i++)
            {
                var e = entries[i]; float x = 42 + i * 180; bool online = here.Contains(e.name);
                People.AvatarRgb(e.name, out float r, out float g, out float b);
                Ui.Disc(_stage, "AvRing", online ? Brand.Good : new Color(1, 1, 1, 0.25f), x, 984, 64);
                Ui.Disc(_stage, "Av", new Color(r, g, b), x + 3, 987, 58);
                Ui.Label(_stage, People.Initials(e.name), "Bold", 22, White, x + 3, 1004, 58, 28, TextAnchor.UpperCenter);
                var n = Ui.Label(_stage, e.name, "Bold", 18, White, x + 74, 988, 100, 24); n.resizeTextForBestFit = true; n.resizeTextMinSize = 12; n.resizeTextMaxSize = 18; n.horizontalOverflow = HorizontalWrapMode.Wrap;
                Ui.Disc(_stage, "St", online ? Brand.Good : Muted, x + 74, 1020, 9);
                Ui.Label(_stage, Recents.Status(e, here, now), "Regular", 14, online ? Brand.Good : Muted, x + 88, 1015, 90, 18);
            }
        }

        void BuildNews()
        {
            Panel(800, 924, 581, 142);
            Ui.Label(_stage, "Latest News", "Bold", 24, White, 822, 938, 300, 32);
            _newsHost = Ui.Box(_stage, "NewsHost", 800, 924, 581, 142);
            if (_newsItems.Count == 0) { Ui.Icon(_newsHost, "ic_news", new Color(1, 1, 1, 0.22f), 24, 70, 48); Ui.Label(_newsHost, "No news right now.", "SemiBold", 20, Muted, 86, 76, 400, 28); return; }
            ShowNews(0);
            var nextBg = Ui.Rounded(_stage, "NewsNext", Gold, 1328, 1010, 40, 40, 20); Ui.Icon(_stage, "ic_chevron_right", Ink, 1336, 1018, 24);
            Ui.Click(_stage, "Btn_NewsNext", 1320, 1002, 56, 56, () => { NextNews(); _nextNews = Time.unscaledTime + 12f; });
            if (_newsItems.Count > 1) _newsCounter = Ui.Label(_stage, "1 / " + _newsItems.Count, "SemiBold", 14, Muted, 1290, 942, 70, 20, TextAnchor.UpperRight);
        }

        void NextNews() { if (_newsItems.Count == 0 || _newsHost == null) return; _newsIndex = (_newsIndex + 1) % _newsItems.Count; ShowNews(_newsIndex); Debug.Log($"[Dashboard] news index={_newsIndex} id={_newsItems[_newsIndex].id}"); }

        void ShowNews(int index)
        {
            for (int i = _newsHost.childCount - 1; i >= 0; i--) Destroy(_newsHost.GetChild(i).gameObject);
            var n = _newsItems[index]; var scene = Brand.Scene(); var model = WorldMapScreen.Model(); var map = WorldMapScreen.Artwork(model);
            Texture tex = scene; Rect uv = Ui.Cover(scene, 0.45f, 0.50f, 0.35f, 150f / 76f);
            switch (n.art)
            {
                case "map": if (map != null) { tex = map; uv = Ui.Cover(map, 0.30f, 0.42f, 0.30f, 150f / 76f); } break;
                case "fuel": tex = Brand.Texture("job_tanker") ?? scene; uv = tex == scene ? uv : new Rect(0, 0, 1, 1); break;
                case "convoy": uv = Ui.Cover(scene, 0.40f, 0.50f, 0.45f, 150f / 76f); break;
            }
            var group = Ui.Box(_newsHost, "NewsItem", 0, 0, 581, 142); GroupFade.On(group).Play(0.45f, 16f);
            Ui.Photo(group, "NewsPhoto", tex, uv, 22, 54, 150, 76, 8);
            Color kc = n.kind == "tip" ? Brand.Good : n.kind == "event" ? Blue : Gold;
            Ui.Rounded(group, "NewsKind", new Color(kc.r * 0.3f, kc.g * 0.3f, kc.b * 0.3f, 0.95f), 190, 56, 70, 20, 6); Ui.Label(group, n.kind.ToUpperInvariant(), "Bold", 12, kc, 190, 58, 70, 18, TextAnchor.UpperCenter);
            var title = Ui.Label(group, n.title, "SemiBold", 19, White, 190, 80, 370, 26); title.resizeTextForBestFit = true; title.resizeTextMinSize = 14; title.resizeTextMaxSize = 19;
            Ui.Paragraph(group, n.body, "Regular", 15, new Color(0.84f, 0.86f, 0.9f), 190, 104, 330, 40);
            DateTime d; string date = DateTime.TryParseExact(n.date, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out d) ? d.ToString("MMM d, yyyy", System.Globalization.CultureInfo.InvariantCulture) : n.date;
            Ui.Label(group, date, "Regular", 13, Muted, 22, 130, 150, 16);
            if (_newsCounter != null) _newsCounter.text = (index + 1) + " / " + _newsItems.Count;
        }

        void BuildConvoyCard()
        {
            Ui.Outlined(_stage, "ConvoyFrame", new Color(0, 0, 0, 1), new Color(1, 1, 1, 0.14f), 1397, 924, 502, 142, 18);
            var scene = Brand.Scene();
            Ui.Photo(_stage, "ConvoyPhoto", scene, Ui.Cover(scene, 0.40f, 0.50f, 0.50f, 499f / 139f), 1398.5f, 925.5f, 499, 139, 17, new Color(0.7f, 0.7f, 0.74f));
            Ui.Gradient(_stage, "ConvoyShade", new Color(0.02f, 0.025f, 0.04f, 0.9f), new Color(0.02f, 0.025f, 0.04f, 0.1f), true, 1399, 926, 498, 138);
            int members = NetworkVehicle.Players.Count; bool inConvoy = members > 1 || (_svc.Convoys != null && !string.IsNullOrEmpty(_svc.Convoys.CurrentConvoyId));
            Ui.Rounded(_stage, "ConvoyTile", new Color(0.1f, 0.2f, 0.4f, 0.95f), 1420, 944, 60, 60, 14); Ui.Icon(_stage, "ic_people", Blue, 1432, 956, 36);
            if (inConvoy) { var d = Ui.Disc(_stage, "LiveDot", Brand.Good, 1496, 962, 12); PulseImage.On(d, 1.6f, 0.85f, 1.25f, 0.5f, 1f); Ui.Label(_stage, members > 1 ? $"{members} drivers on the road" : "In a convoy", "SemiBold", 17, Brand.Good, 1514, 957, 300, 24); }
            var t = Ui.Label(_stage, inConvoy ? "Your Convoy" : "Join a Convoy", "ExtraBold", 30, White, 1420, 1010, 360, 38); Ui.Drop(t);
            Ui.Label(_stage, inConvoy ? "Stay together. Share the road and the rewards." : "Drive together. Earn more. Make new friends.", "Regular", 17, new Color(0.9f, 0.92f, 0.95f), 1420, 1040, 420, 24);
            Ui.Rounded(_stage, "ConvoyGo", Gold, 1832, 1004, 46, 46, 23); Ui.Icon(_stage, "ic_chevron_right", Ink, 1841, 1013, 28);
            Ui.Click(_stage, "Btn_JoinConvoy", 1397, 924, 502, 142, _act.Convoy);
        }
    }
}
