using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ARO.Backend;
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
    /// The home dashboard: sidebar, header, hero carousel, quick actions, your truck, featured jobs, popular routes, convoy.
    /// Real data wherever the backend has it (profile, wallet, vehicles, job board); sections whose backend does not exist yet say so honestly.
    /// Laid out on a 1920x1080 stage that letterboxes to any window shape.
    /// </summary>
    public sealed class DashboardScreen : MonoBehaviour
    {
        static readonly Color Gold = Brand.Gold, Ink = Brand.Ink, Muted = Brand.Muted;
        static readonly Color PanelFill = new Color(0.062f, 0.070f, 0.092f, 0.94f), PanelLine = new Color(1f, 1f, 1f, 0.10f);
        static readonly Color White = Color.white;

        GameServices _svc; DrivingSession _drive; BusSession _bus; DashboardActions _act;
        RectTransform _stage, _jobsHost; Text _toast, _heroTag, _heroT1, _heroT2, _heroDesc, _heroBtnText; Image[] _dots;
        float _toastUntil, _nextSlide; int _slide;
        Text _heroLoc;
        public JobDto[] JobsOverride;   // test/preview fixture only
        bool _jobsLoaded;
        JobDto[] _jobs;

        struct Slide { public string tag, t1, t2, desc, button; public Action go; public Rect uv; public bool sky; }
        Slide[] _slides;

        public static DashboardScreen Create(GameServices svc, DrivingSession drive, BusSession bus, DashboardActions act, JobDto[] jobsOverride = null)
        {
            var go = new GameObject("Dashboard");
            var d = go.AddComponent<DashboardScreen>(); d.JobsOverride = jobsOverride; d.Build(svc, drive, bus, act);
            return d;
        }

        public void Refresh() { if (_stage != null) { Destroy(transform.GetChild(0).gameObject); _jobsLoaded = false; BuildCanvas(); } }

        void Build(GameServices svc, DrivingSession drive, BusSession bus, DashboardActions act) { _svc = svc; _drive = drive; _bus = bus; _act = act; BuildCanvas(); }

        void BuildCanvas()
        {
            _slides = new[]
            {
                new Slide { tag = "EXPLORE", t1 = "DRIVE ACROSS", t2 = "AFRICA", desc = "Take real jobs, deliver cargo, explore cities\nand build your trucking career.",
                            button = _drive.Vehicle != null ? "Resume Driving" : "Find a Job", go = () => { if (_drive.Vehicle != null) _act.Resume(); else _act.Jobs(); }, uv = Ui.Top(0f, 0.30f, 1f, 0.49f) },
                new Slide { tag = "TOGETHER", t1 = "DRIVE IN", t2 = "CONVOY", desc = "Team up with friends on the same route.\nShared roads, shared rewards.",
                            button = "Join a Convoy", go = () => _act.Convoy(), uv = Ui.Top(0.30f, 0.46f, 0.70f, 0.344f) },
                new Slide { tag = "CITY ROUTES", t1 = "KEEP THE CITY", t2 = "MOVING", desc = "Run a bus route, pick up passengers at every\nstop and earn a fare for each one.",
                            button = _bus != null && _bus.Active ? "Continue Route" : "Bus Routes", go = () => _act.Bus(), uv = Ui.Top(0f, 0.38f, 0.72f, 0.353f), sky = true },
            };
            var canvas = UIKit.CreateCanvas("DashboardCanvas", 20);
            canvas.transform.SetParent(transform, false);
            var root = (RectTransform)canvas.transform;
            var bg = Ui.Flat(root, "Bg", new Color(0.028f, 0.033f, 0.048f, 1f), 0, 0, 10, 10); var brt = bg.rectTransform; brt.anchorMin = Vector2.zero; brt.anchorMax = Vector2.one; brt.offsetMin = brt.offsetMax = Vector2.zero;
            var stageGo = new GameObject("Stage", typeof(RectTransform), typeof(StageFitter)); stageGo.transform.SetParent(root, false);
            _stage = (RectTransform)stageGo.transform; _stage.anchorMin = _stage.anchorMax = _stage.pivot = new Vector2(0.5f, 0.5f); _stage.sizeDelta = new Vector2(1920, 1080); _stage.anchoredPosition = Vector2.zero;

            BuildHeader(); BuildSidebar(); BuildHero(); BuildChallenge(); BuildYourTruck(); BuildQuick(); BuildFeatured(); BuildRoutes(); BuildPlayers(); BuildNews(); BuildConvoyCard();
            _toast = Ui.Label(_stage, "", "SemiBold", 24, Gold, 0, 1010, 1920, 40, TextAnchor.MiddleCenter);
            _toast.rectTransform.sizeDelta = new Vector2(1920, 40);
            ShowSlide(_slide); _nextSlide = Time.unscaledTime + 7f;
            if (!_jobsLoaded) { _jobsLoaded = true; _ = LoadJobs(); } else FillJobs();
            Debug.Log("[Dashboard] built for " + (_svc.Profile != null ? _svc.Profile.display_name : "?"));
        }

        // ------------------------------------------------------------------ sidebar
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
            var so = Ui.Label(_stage, "Sign out", "SemiBold", 20, new Color(0.62f, 0.65f, 0.7f), 44, 1018, 200, 28);
            Ui.Click(_stage, "Nav_SignOut", 30, 1008, 240, 48, () => _act.SignOut());
        }

        // ------------------------------------------------------------------ header
        void BuildHeader()
        {
            var photo = Ui.Photo(_stage, "HeaderSky", Brand.Texture("login_bg"), Ui.Top(0f, 0.40f, 1f, 0.1356f), 300, 0, 1620, 124, 0);
            Ui.Gradient(_stage, "HeaderShade", new Color(0.03f, 0.035f, 0.05f, 0.92f), new Color(0.03f, 0.035f, 0.05f, 0.25f), true, 300, 0, 1620, 124);
            Ui.Gradient(_stage, "HeaderFloor", new Color(0.028f, 0.033f, 0.048f, 0f), new Color(0.028f, 0.033f, 0.048f, 1f), false, 300, 70, 1620, 54);

            var p = _svc.Profile; string name = p != null ? p.display_name : "Driver"; long xp = p != null ? p.experience : 0; int lvl = p != null ? Math.Max(p.level, LevelMath.LevelFor(xp)) : 1;
            Ui.Disc(_stage, "AvatarRing", Gold, 330, 14, 100);
            var av = Ui.Disc(_stage, "AvatarBg", new Color(0.12f, 0.14f, 0.18f), 334, 18, 92);
            Ui.Icon(_stage, "avatar_default", White, 334, 18, 92);
            Ui.Label(_stage, "Good to see you,", "Regular", 22, new Color(0.9f, 0.92f, 0.95f), 452, 20, 400, 30);
            var nm = Ui.Label(_stage, name, "ExtraBold", 42, White, 452, 44, 700, 54);
            Ui.Icon(_stage, "ic_crown", Gold, 452 + nm.preferredWidth + 14, 56, 32);
            Ui.Label(_stage, "Level " + lvl, "SemiBold", 22, White, 452, 96, 120, 28);
            var (into, span, frac) = LevelMath.Progress(xp);
            Ui.Bar(_stage, 556, 104, 190, 11, frac, Gold);
            Ui.Label(_stage, $"{into:N0} / {span:N0} XP", "Regular", 18, new Color(0.88f, 0.9f, 0.94f), 762, 98, 220, 26);

            // right cluster
            string bal = _svc.Wallet != null ? _svc.Wallet.balance.ToString("N0") : "0";
            Pill(1352, 24, 200, 56, null, () => Toast("Your balance is paid out by the server when a job or route completes."));
            Ui.Icon(_stage, "ic_coin", Gold, 1370, 33, 38);
            Ui.Label(_stage, bal, "Bold", 26, White, 1420, 34, 120, 36);
            Pill(1572, 24, 150, 56, null, () => Toast("You are connected to the game server."));
            Ui.Disc(_stage, "OnlineDot", Brand.Good, 1594, 45, 14);
            Ui.Label(_stage, "Online", "SemiBold", 22, White, 1620, 34, 100, 34);
            Pill(1744, 24, 60, 56, "ic_bell", () => Toast("No new notifications."));
            Pill(1824, 24, 60, 56, "ic_gear", _act.Profile);
        }

        void Pill(float x, float y, float w, float h, string icon, Action click)
        {
            var o = Ui.Outlined(_stage, "Pill", new Color(0.05f, 0.055f, 0.07f, 0.88f), new Color(1, 1, 1, 0.14f), x, y, w, h, 16);
            if (icon != null) Ui.Icon(_stage, icon, White, x + (w - 30) / 2f, y + (h - 30) / 2f, 30);
            Ui.Click(_stage, "PillBtn", x, y, w, h, click);
        }

        // ------------------------------------------------------------------ hero
        void BuildHero()
        {
            const float X = 331, Y = 142, W = 1146, H = 318;
            Ui.Outlined(_stage, "HeroFrame", new Color(0, 0, 0, 1), new Color(Gold.r, Gold.g, Gold.b, 0.85f), X - 2, Y - 2, W + 4, H + 4, 22, 2f);
            var ph = Ui.Photo(_stage, "HeroPhoto", Brand.Scene(), _slides[_slide].uv, X, Y, W, H, 20);
            _heroPhoto = ph;
            Ui.Gradient(_stage, "HeroShade", new Color(0.02f, 0.025f, 0.04f, 0.92f), new Color(0.02f, 0.025f, 0.04f, 0f), true, X, Y, 760, H);
            Ui.Gradient(_stage, "HeroFloor", new Color(0, 0, 0, 0), new Color(0, 0, 0, 0.45f), false, X, Y + H - 90, W, 90);

            var chip = Ui.Outlined(_stage, "HeroChip", new Color(0.35f, 0.25f, 0.04f, 0.75f), Gold, X + 32, Y + 26, 124, 34, 8, 1.5f);
            _heroTag = Ui.Label(_stage, "EXPLORE", "Bold", 17, White, X + 32, Y + 31, 124, 24, TextAnchor.UpperCenter);
            _heroT1 = Ui.Label(_stage, "DRIVE ACROSS", "ExtraBold", 66, White, X + 30, Y + 62, 760, 80); Ui.Drop(_heroT1);
            _heroT2 = Ui.Label(_stage, "AFRICA", "ExtraBold", 66, Gold, X + 30, Y + 124, 760, 80); Ui.Drop(_heroT2);
            _heroDesc = Ui.Paragraph(_stage, "", "Regular", 22, White, X + 32, Y + 206, 560, 60); Ui.Drop(_heroDesc);
            var btn = Ui.Rounded(_stage, "HeroButton", Gold, X + 32, Y + 262, 250, 54, 12);
            _heroBtnText = Ui.Label(_stage, "Find a Job", "Bold", 24, Ink, X + 32 + 28, Y + 273, 170, 32);
            Ui.Icon(_stage, "icon_arrow", Ink, X + 32 + 250 - 58, Y + 273, 32);
            Ui.Click(_stage, "Btn_HeroAction", X + 32, Y + 262, 250, 54, () => _slides[_slide].go());

            var loc = Ui.Outlined(_stage, "LocChip", new Color(0.03f, 0.035f, 0.05f, 0.82f), new Color(1, 1, 1, 0.12f), X + W - 218, Y + H - 66, 190, 44, 12);
            Ui.Icon(_stage, "ic_pin", Gold, X + W - 218 + 16, Y + H - 66 + 9, 26);
            _heroLoc = Ui.Label(_stage, "Lagos, Nigeria", "SemiBold", 18, White, X + W - 218 + 50, Y + H - 66 + 10, 140, 26);

            _dots = new Image[_slides.Length];
            for (int i = 0; i < _slides.Length; i++)
            {
                int idx = i; _dots[i] = Ui.Disc(_stage, "Dot" + i, new Color(1, 1, 1, 0.4f), X + 540 + i * 26, Y + H - 36, 13);
                Ui.Click(_stage, "Btn_Dot" + i, X + 534 + i * 26, Y + H - 44, 26, 28, () => { _slide = idx; ShowSlide(idx); _nextSlide = Time.unscaledTime + 8f; });
            }
        }
        RawImage _heroPhoto;

        void ShowSlide(int i)
        {
            if (_heroT1 == null) return; var s = _slides[i];
            _heroTag.text = s.tag; _heroT1.text = s.t1; _heroT2.text = s.t2; _heroDesc.text = s.desc; _heroBtnText.text = s.button;
            if (_heroPhoto != null) { _heroPhoto.texture = s.sky ? Brand.Texture("login_bg") ?? Brand.Scene() : Brand.Scene(); _heroPhoto.uvRect = s.uv; }
            for (int k = 0; k < _dots.Length; k++) _dots[k].color = k == i ? Gold : new Color(1, 1, 1, 0.4f);
        }

        void Update()
        {
            if (_slides != null && Time.unscaledTime > _nextSlide) { _slide = (_slide + 1) % _slides.Length; ShowSlide(_slide); _nextSlide = Time.unscaledTime + 7f; }
            if (_toast != null && _toast.text.Length > 0 && Time.unscaledTime > _toastUntil) _toast.text = "";
        }

        public void Toast(string m, float s = 3f) { if (_toast != null) { _toast.text = m; _toastUntil = Time.unscaledTime + s; } }

        // ------------------------------------------------------------------ right column: challenge + truck
        Image Panel(float x, float y, float w, float h, bool gold = false)
            => Ui.Outlined(_stage, "Panel", PanelFill, gold ? new Color(Gold.r, Gold.g, Gold.b, 0.7f) : PanelLine, x, y, w, h, 18, gold ? 2f : 1.5f);

        void BuildChallenge()
        {
            Panel(1497, 142, 402, 170, true);
            Ui.Icon(_stage, "ic_trophy", Gold, 1520, 164, 52);
            Ui.Label(_stage, "Today's Challenge", "Bold", 28, White, 1590, 164, 300, 36);
            Ui.Label(_stage, "Daily challenges are coming soon.", "Regular", 18, Muted, 1590, 202, 300, 26);
            Ui.Bar(_stage, 1520, 248, 260, 12, 0f, Gold);
            Ui.Label(_stage, "Reward", "Regular", 17, Muted, 1520, 270, 100, 24);
            Ui.Icon(_stage, "ic_lightning", Gold, 1520, 292 - 2, 16);
            Ui.Label(_stage, "Complete jobs to level up meanwhile", "SemiBold", 16, new Color(0.9f, 0.92f, 0.95f), 1542, 289, 300, 22);
        }

        void BuildYourTruck()
        {
            Panel(1497, 328, 402, 132);
            Ui.Label(_stage, "Your Truck", "Bold", 26, White, 1518, 340, 200, 34);
            SmallPill(1812, 342, 68, 26, "View All", _act.Garage);
            var v = _svc.Vehicles != null && _svc.Vehicles.Length > 0 ? _svc.Vehicles[0] : null;
            var def = v != null && _svc.Definitions != null ? _svc.Definitions.FirstOrDefault(d => d.id == v.definition_id) : null;
            Ui.Icon(_stage, "truck_thumb", White, 1516, 372, 130 * 1.0f);
            if (v == null) { Ui.Label(_stage, "No vehicle yet", "SemiBold", 22, Muted, 1660, 400, 200, 30); return; }
            Ui.Label(_stage, def != null ? def.name : v.definition_id, "SemiBold", 21, White, 1660, 380, 190, 56).horizontalOverflow = HorizontalWrapMode.Wrap;
            double cap = def != null && def.fuel_capacity_l > 0 ? def.fuel_capacity_l : 1;
            string status = v.damage_pct >= 70 ? "Damaged" : v.fuel_l / cap < 0.1 ? "Low fuel" : "Ready";
            Color sc = status == "Ready" ? Brand.Good : status == "Low fuel" ? Gold : Brand.Bad;
            Ui.Disc(_stage, "StatusDot", sc, 1660, 432, 14); Ui.Label(_stage, status, "SemiBold", 19, sc, 1682, 426, 120, 26);
            Ui.Rounded(_stage, "TruckGo", Gold, 1848, 412, 40, 40, 20); Ui.Icon(_stage, "ic_chevron_right", Ink, 1856, 420, 24);
            Ui.Click(_stage, "Btn_YourTruck", 1497, 328, 402, 132, _act.Garage);
        }

        Image SmallPill(float x, float y, float w, float h, string label, Action click)
        {
            var o = Ui.Outlined(_stage, "SmallPill", new Color(0.05f, 0.055f, 0.07f, 0.9f), new Color(1, 1, 1, 0.16f), x, y, w, h, 8, 1.2f);
            Ui.Label(_stage, label, "SemiBold", 15, White, x, y + 3, w, h - 4, TextAnchor.UpperCenter);
            Ui.Click(_stage, "Btn_" + label.Replace(" ", ""), x, y, w, h, click); return o;
        }

        // ------------------------------------------------------------------ quick actions
        void BuildQuick()
        {
            var cards = new (string title, string desc, string icon, Color tint, Rect uv, Action go)[]
            {
                ("Quick Job", "Find and start a job\nimmediately", "ic_bus", new Color(0.95f, 0.62f, 0.10f), Ui.Top(0.55f, 0.52f, 0.40f, 0.245f), _act.QuickJob),
                ("Multiplayer\nConvoy", "Drive with friends\nacross Africa", "ic_people", new Color(0.25f, 0.55f, 1f), Ui.Top(0.28f, 0.58f, 0.36f, 0.22f), _act.Convoy),
                ("Explore Map", "Discover cities, roads and\nlocations", "ic_map", new Color(0.20f, 0.80f, 0.55f), Ui.Top(0.05f, 0.40f, 0.40f, 0.245f), _act.WorldMap),
                ("Garage", "Customize and upgrade\nyour vehicles", "ic_wrench", new Color(0.65f, 0.40f, 1f), Ui.Top(0.70f, 0.55f, 0.30f, 0.184f), _act.Garage),
            };
            float w = 382, gap = 18, x = 316;
            foreach (var c in cards)
            {
                Ui.Outlined(_stage, "QCardFrame", new Color(0, 0, 0, 1), new Color(1, 1, 1, 0.14f), x, 474, w, 132, 18);
                Ui.Photo(_stage, "QCardPhoto", c.uv.x < 0.1f ? (Brand.Texture("login_bg") ?? Brand.Scene()) : Brand.Scene(), c.uv, x + 1.5f, 475.5f, w - 3, 129, 17, new Color(0.62f, 0.62f, 0.66f));
                Ui.Flat(_stage, "QTint", new Color(c.tint.r * 0.3f, c.tint.g * 0.3f, c.tint.b * 0.3f, 0.45f), x + 2, 476, w - 4, 128);
                Ui.Gradient(_stage, "QShade", new Color(0.02f, 0.025f, 0.04f, 0.88f), new Color(0.02f, 0.025f, 0.04f, 0.1f), true, x + 2, 476, w - 4, 128);
                Ui.Rounded(_stage, "QTile", new Color(c.tint.r * 0.28f, c.tint.g * 0.28f, c.tint.b * 0.28f, 0.95f), x + 20, 506, 68, 68, 16);
                Ui.Icon(_stage, c.icon, c.tint, x + 34, 520, 40);
                Ui.Label(_stage, c.title, "Bold", 27, White, x + 104, c.title.Contains("\n") ? 490 : 506, 260, 66).lineSpacing = 0.9f;
                Ui.Paragraph(_stage, c.desc, "Regular", 18, new Color(0.9f, 0.92f, 0.95f), x + 104, c.title.Contains("\n") ? 548 : 544, 240, 48);
                Ui.Rounded(_stage, "QGo", Gold, x + w - 62, 546, 44, 44, 22); Ui.Icon(_stage, "ic_chevron_right", Ink, x + w - 54, 554, 28);
                Ui.Click(_stage, "Btn_Quick_" + c.title.Replace("\n", "").Replace(" ", ""), x, 474, w, 132, () => c.go());
                x += w + gap;
            }
        }

        // ------------------------------------------------------------------ featured jobs
        void BuildFeatured()
        {
            Panel(316, 622, 1065, 292);
            Ui.Label(_stage, "Featured Jobs", "Bold", 26, White, 338, 636, 300, 34);
            SmallPill(1290, 640, 70, 26, "View All", _act.Jobs);
            _jobsHost = Ui.Box(_stage, "JobsHost", 316, 622, 1065, 292);
        }

        async Task LoadJobs()
        {
            if (JobsOverride != null) { _jobs = JobsOverride; FillJobs(); return; }
            var r = await _svc.Jobs.LoadBoard();
            if (this == null) return;
            _jobs = r.Ok ? r.Value : null; FillJobs(r.Ok ? null : r.UserMessage);
        }

        void FillJobs(string error = null)
        {
            if (_jobsHost == null) return;
            for (int i = _jobsHost.childCount - 1; i >= 0; i--) Destroy(_jobsHost.GetChild(i).gameObject);
            if (_jobs == null) { Ui.Label(_jobsHost, error ?? "Loading jobs...", "Regular", 22, error != null ? Brand.Bad : Muted, 24, 110, 900, 30); return; }
            if (_jobs.Length == 0) { Ui.Label(_jobsHost, "No open jobs right now. The server posts new ones regularly.", "Regular", 22, Muted, 24, 110, 900, 30); return; }
            var owned = new HashSet<string>((_svc.Vehicles ?? new OwnedVehicleDto[0]).Select(v => CategoryOf(v.definition_id)));
            var pick = _jobs.OrderByDescending(j => owned.Contains(j.required_category)).ThenByDescending(j => j.reward).Take(4).ToArray();
            float cw = 246, gap = 14, x0 = 22;
            for (int i = 0; i < pick.Length; i++) JobCard(pick[i], x0 + i * (cw + gap), 58, cw);
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

        void JobCard(JobDto j, float x, float y, float w)
        {
            var host = _jobsHost;
            Ui.Outlined(host, "JobCard", new Color(0.045f, 0.052f, 0.07f, 0.98f), new Color(1, 1, 1, 0.09f), x, y, w, 226, 12, 1.2f);
            Ui.Photo(host, "JobPhoto", Brand.Texture(CargoArt(j.cargo_type)), new Rect(0, 0, 1, 1), x + 1.2f, y + 1.2f, w - 2.4f, 84, 11);
            string route = $"{(j.origin != null ? j.origin.name : "?")} → {(j.destination != null ? j.destination.name : "?")}";
            var rt = Ui.Label(host, route, "Bold", 20, White, x + 10, y + 92, w - 14, 28); rt.horizontalOverflow = HorizontalWrapMode.Wrap; rt.resizeTextForBestFit = true; rt.resizeTextMinSize = 14; rt.resizeTextMaxSize = 20;
            Ui.Icon(host, "ic_box", Muted, x + 10, y + 122, 22); Ui.Label(host, j.cargo_type, "Regular", 17, new Color(0.84f, 0.86f, 0.9f), x + 38, y + 121, w - 48, 24);
            Ui.Icon(host, "ic_road", Muted, x + 10, y + 148, 22); Ui.Label(host, $"{j.distance_km:0.#} km", "Regular", 17, new Color(0.84f, 0.86f, 0.9f), x + 38, y + 147, 100, 24);
            Ui.Icon(host, "ic_coin", Gold, x + 10, y + 173, 22); Ui.Label(host, j.reward.ToString("N0"), "SemiBold", 17, White, x + 38, y + 172, 90, 24);
            Color dc = j.difficulty <= 2 ? Brand.Good : j.difficulty == 3 ? Gold : Brand.Bad; string dl = j.difficulty <= 2 ? "Easy" : j.difficulty == 3 ? "Medium" : "Hard";
            Ui.Icon(host, "ic_bars", dc, x + w - 112, y + 173, 22); Ui.Label(host, dl, "SemiBold", 17, dc, x + w - 86, y + 172, 80, 24);
            var ab = Ui.Rounded(host, "AcceptBg", Gold, x + 8, y + 196 - 2, w - 16, 28, 7);
            Ui.Label(host, "Accept", "Bold", 17, Ink, x + 8, y + 199, w - 16, 24, TextAnchor.UpperCenter);
            var job = j; Ui.Click(host, "Btn_Accept_" + j.code, x + 8, y + 194, w - 16, 30, () => _act.AcceptJob(job));
        }

        // ------------------------------------------------------------------ routes: the real artwork + the validated route data (same source as the world map)
        void BuildRoutes()
        {
            Panel(1397, 622, 502, 292);
            Ui.Label(_stage, "Popular Routes", "Bold", 26, White, 1418, 636, 300, 34);
            SmallPill(1808, 640, 70, 26, "Map", _act.WorldMap);
            const float bx = 1417, by = 678, bw = 462, bh = 218;
            var model = WorldMapScreen.Model(); var tex = WorldMapScreen.Artwork(model);
            if (model == null || tex == null) { Ui.Label(_stage, "The map is unavailable.", "Regular", 18, Muted, bx + 16, by + 90, 400, 26); return; }
            // a crop of the artwork around the Gulf of Guinea coast, in artwork pixels (origin top-left)
            const float cx0 = 880f, cy0 = 440f, cw = 760f; float ch = cw * bh / bw;
            float tw = model.Data.image.textureWidth, th = model.Data.image.textureHeight;
            Ui.Photo(_stage, "MapPhoto", tex, new Rect(cx0 / tw, (th - (cy0 + ch)) / th, cw / tw, ch / th), bx, by, bw, bh, 12);
            var host = Ui.Box(_stage, "MapHost", bx, by, bw, bh);
            Func<MapCity, Vector2> at = c => new Vector2((c.mapX - cx0) / cw * bw, (c.mapY - cy0) / ch * bh);
            Func<MapCity, bool> inside = c => c.mapX > cx0 && c.mapX < cx0 + cw && c.mapY > cy0 && c.mapY < cy0 + ch;
            int level = _svc.Profile != null ? Math.Max(_svc.Profile.level, LevelMath.LevelFor(_svc.Profile.experience)) : 1;
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
            foreach (var c in model.Cities)
            {
                if (!inside(c)) continue; var p = at(c); bool open = model.StateFor(c, level) == CityState.Open;
                if (open) { Ui.Disc(host, "CityGlow", new Color(Gold.r, Gold.g, Gold.b, 0.3f), p.x - 11, p.y - 11, 22); Ui.Disc(host, "City", Gold, p.x - 5, p.y - 5, 10); var l = Ui.Label(host, c.name, "Bold", 15, White, p.x + 10, p.y - 24, 110, 20); Ui.Drop(l, 0.9f); }
            }
            Ui.Disc(_stage, "LegendLive", Gold, bx + 10, by + bh - 24, 9); Ui.Label(_stage, "Open now (prototype)", "SemiBold", 14, White, bx + 26, by + bh - 29, 150, 20);
            Ui.Disc(_stage, "LegendSoon", new Color(1, 1, 1, 0.55f), bx + 190, by + bh - 24, 9); Ui.Label(_stage, "Planned", "SemiBold", 14, new Color(0.9f, 0.9f, 0.9f), bx + 206, by + bh - 29, 110, 20);
            Ui.Click(_stage, "Btn_RoutesMap", bx, by, bw, bh, _act.WorldMap);
        }

        // ------------------------------------------------------------------ bottom row
        void BuildPlayers()
        {
            Panel(21, 924, 760, 142);
            Ui.Label(_stage, "Recent Players", "Bold", 24, White, 42, 938, 300, 32);
            Ui.Icon(_stage, "ic_people", new Color(1, 1, 1, 0.22f), 44, 984, 52);
            Ui.Label(_stage, "Players you drive with will appear here.", "SemiBold", 21, Muted, 112, 988, 600, 28);
            Ui.Label(_stage, "Friends and a recent-players list are coming soon.", "Regular", 17, new Color(0.55f, 0.58f, 0.64f), 112, 1018, 600, 24);
        }

        void BuildNews()
        {
            Panel(800, 924, 581, 142);
            Ui.Label(_stage, "Latest News", "Bold", 24, White, 822, 938, 300, 32);
            Ui.Photo(_stage, "NewsPhoto", Brand.Texture("login_bg"), Ui.Top(0.18f, 0.40f, 0.30f, 0.27f), 822, 978, 150, 76, 8);
            Ui.Label(_stage, "The Lagos–Ibadan corridor is open", "SemiBold", 19, White, 990, 980, 380, 26);
            Ui.Paragraph(_stage, "Take jobs, run bus routes and drive in a convoy. More cities and roads are on the way.", "Regular", 16, new Color(0.84f, 0.86f, 0.9f), 990, 1008, 380, 44);
            Ui.Label(_stage, DateTime.Now.ToString("MMM d, yyyy"), "Regular", 15, Muted, 990, 1042, 200, 20);
        }

        void BuildConvoyCard()
        {
            Ui.Outlined(_stage, "ConvoyFrame", new Color(0, 0, 0, 1), new Color(1, 1, 1, 0.14f), 1397, 924, 502, 142, 18);
            Ui.Photo(_stage, "ConvoyPhoto", Brand.Scene(), Ui.Top(0.30f, 0.58f, 0.50f, 0.258f), 1398.5f, 925.5f, 499, 139, 17, new Color(0.7f, 0.7f, 0.74f));
            Ui.Gradient(_stage, "ConvoyShade", new Color(0.02f, 0.025f, 0.04f, 0.9f), new Color(0.02f, 0.025f, 0.04f, 0.1f), true, 1399, 926, 498, 138);
            Ui.Rounded(_stage, "ConvoyTile", new Color(0.1f, 0.2f, 0.4f, 0.95f), 1420, 944, 60, 60, 14); Ui.Icon(_stage, "ic_people", new Color(0.35f, 0.65f, 1f), 1432, 956, 36);
            var t = Ui.Label(_stage, "Join a Convoy", "ExtraBold", 30, White, 1420, 1010, 360, 38); Ui.Drop(t);
            Ui.Label(_stage, "Drive together. Earn more. Make new friends.", "Regular", 17, new Color(0.9f, 0.92f, 0.95f), 1420, 1044 - 4, 420, 24);
            Ui.Rounded(_stage, "ConvoyGo", Gold, 1832, 1004, 46, 46, 23); Ui.Icon(_stage, "ic_chevron_right", Ink, 1841, 1013, 28);
            Ui.Click(_stage, "Btn_JoinConvoy", 1397, 924, 502, 142, _act.Convoy);
        }
    }
}
