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
    /// <summary>
    /// Bus Routes (a page under Trucks &amp; Buses). Every route, stop, fare, XP value and stop demand is the server's (bus_routes / bus_route_stops); the player's bus, its
    /// condition and fuel are the server's too. Without a bus the page says so and leads to the shop; with a run in progress it shows the live state
    /// (next stop, passengers aboard, fares so far) with Resume and Abandon. Starting a route asks the server to start a run; the server may refuse
    /// (job active, no fuel, wrecked bus) and the page shows its reason. Nothing is invented: when the server has one route the list has one route.
    /// </summary>
    public sealed partial class DashboardScreen
    {
        const float BrX = 330, BrLW = 890, BrRX = 1250, BrRW = 630, BrTop = 262;
        RectTransform _brRoot; bool _brOpen; float _brLoadedAt = -999f; bool _brBusy;
        LoadState _brState = LoadState.Idle; BusRouteDto[] _brRoutes = new BusRouteDto[0]; string _brErr, _brSel;

        public void OpenBusRoutes() => ShowPage(Page.BusRoutes);

        // ---------------------------------------------------------------- the player's bus (server numbers)
        List<(OwnedVehicleDto v, VehicleDefDto d)> BrBuses()
        {
            var defs = (_svc.Definitions ?? new VehicleDefDto[0]).ToDictionary(d => d.id, d => d, StringComparer.Ordinal); var l = new List<(OwnedVehicleDto, VehicleDefDto)>();
            foreach (var v in _svc.Vehicles ?? new OwnedVehicleDto[0]) if (v != null && defs.TryGetValue(v.definition_id ?? "", out var d) && d.category == "bus") l.Add((v, d));
            return l;
        }
        BusChoice BrChoice((OwnedVehicleDto v, VehicleDefDto d) b) => new BusChoice { Id = b.v.id, Name = b.d.name, DamagePct = b.v.damage_pct, FuelL = b.v.fuel_l, Selected = b.v.selected, Seats = b.d.passenger_capacity };
        (OwnedVehicleDto v, VehicleDefDto d)? BrBus()
        {
            var all = BrBuses(); if (all.Count == 0) return null;
            if (_bus != null && _bus.Active && _drive != null && !string.IsNullOrEmpty(_drive.VehicleId)) { var cur = all.FirstOrDefault(x => x.v.id == _drive.VehicleId); if (cur.v != null) return cur; }
            var pick = BusRouteRules.PickBus(all.Select(BrChoice)); return pick == null ? (ValueTuple<OwnedVehicleDto, VehicleDefDto>?)null : all.First(x => x.v.id == pick.Id);
        }
        BusRouteDto BrRoute() => _bus != null && _bus.Active ? _bus.Route : _brRoutes.FirstOrDefault(r => r.id == _brSel) ?? _brRoutes.FirstOrDefault();
        static string BrCity(BusRouteDto r) => r != null && r.city != null && !string.IsNullOrEmpty(r.city.name) ? r.city.name : "";

        // ================================================================================================================ lifecycle
        void BrOpened()
        {
            _brOpen = true; Debug.Log("[BusRoutes] opened");
            if (_svc.Garage.Catalog.State != LoadState.Ready) _ = _svc.Garage.LoadCatalog().ContinueWith(t => { });
            if (_bus != null && _bus.Active) { Debug.Log($"[BusRoutes] run active route={_bus.Route.code} next={_bus.NextIndex} aboard={_bus.State.Aboard} fares={_bus.State.Revenue}"); RefreshBr(); return; }
            if (_brState != LoadState.Ready && _brState != LoadState.Empty || Time.unscaledTime - _brLoadedAt > 60f) _ = BrLoad(); else RefreshBr();
        }
        void BrClosed() { _brOpen = false; if (_modal != null) CloseModal(); }

        async Task BrLoad()
        {
            _brState = LoadState.Loading; _brErr = null; RefreshBr();
            var r = await _svc.Bus.LoadRoutes(); if (this == null) return;
            if (r.Ok) { _brRoutes = r.Value ?? new BusRouteDto[0]; _brState = _brRoutes.Length == 0 ? LoadState.Empty : LoadState.Ready; _brLoadedAt = Time.unscaledTime; if (_brRoutes.All(x => x.id != _brSel)) _brSel = _brRoutes.Length > 0 ? _brRoutes[0].id : null; }
            else { _brState = LoadState.Unavailable; _brErr = r.UserMessage; }
            var bus = BrBus();
            Debug.Log($"[BusRoutes] routes state={_brState.ToString().ToLowerInvariant()} count={_brRoutes.Length} buses={BrBuses().Count} bus={(bus.HasValue ? bus.Value.v.definition_id : "none")}" + (r.Ok ? "" : " error=" + r.ErrorCode));
            RefreshBr();
        }

        void RefreshBr() { if (_brRoot == null || !_brOpen) return; Refill("BrMain", BuildBrMain); Refill("BrSide", BuildBrSide); }

        // ================================================================================================================ build
        void BuildBusRoutesPage()
        {
            Region("BrTop", 0.02f); BuildBrTop();
            Region("BrMain", 0.06f); BuildBrMain();
            Region("BrSide", 0.10f); BuildBrSide();
        }

        void BuildBrTop()
        {
            Ui.Label(_stage, "<", "Bold", 20, Gold, BrX, 126, 20, 26); Ui.Label(_stage, "Trucks & Buses", "SemiBold", 17, Gold, BrX + 20, 128, 240, 24); Ui.Click(_stage, "Btn_BrBack", BrX - 8, 122, 240, 36, OpenVehicles);
            Ui.Icon(_stage, "ic_bus", Gold, 322, 160, 56);
            var t = Ui.Label(_stage, "Bus Routes", "ExtraBold", 48, White, 396, 150, 760, 60); Ui.Drop(t, 0.5f);
            Ui.Label(_stage, "Carry passengers between stops and earn fares.", "Regular", 20, new Color(0.84f, 0.86f, 0.9f), 396, 208, 800, 28);
        }

        void BrPanel(float x, float y, float w, float h) => Ui.Outlined(_stage, "BrPanel", PanelFill, PanelLine, x, y, w, h, 20, 1.4f);

        // ---------------------------------------------------------------- left side: the list, a run in progress, or why there is none
        void BuildBrMain()
        {
            bool running = _bus != null && _bus.Active; var bus = BrBus();
            if (running) { BuildBrRun(); return; }
            if (_brState == LoadState.Loading || _brState == LoadState.Idle) { BrCentre("ic_bus", "Loading bus routes...", null, null, null, null, false); return; }
            if (_brState == LoadState.Unavailable) { BrCentre("ic_info", "Unable to load bus routes", _brErr ?? "Please check your connection and try again.", "Try again", "Btn_BrRetry", () => _ = BrLoad(), true); return; }
            if (_brState == LoadState.Empty) { BrCentre("ic_bus", "No bus routes yet", "Routes are added by the game as new cities open. Check back soon.", null, null, null, false); return; }
            if (!bus.HasValue) { BrCentre("ic_bus", "You need a bus to run a route", "Buy a bus in Trucks & Buses to start earning fares.", "Browse buses", "Btn_BrBrowse", () => { _vehTab = VehicleTab.Buses; OpenVehicles(); }, false); return; }
            const float pitch = 198, ch = 186; var area = MakeScroll("br_routes", BrX, BrTop, BrLW, 1050 - BrTop, _brRoutes.Length * pitch); var host = area.Content;
            for (int i = 0; i < _brRoutes.Length; i++) BrCard(host, _brRoutes[i], i * pitch, ch);
            RestoreScroll(area);
        }

        /// <summary>One message in the middle of the left and right columns (nothing to list).</summary>
        void BrCentre(string icon, string title, string text, string button, string name, Action go, bool retry)
        {
            const float x = BrX, y = BrTop, w = 1550, h = 788; BrPanel(x, y, w, h);
            var tex = Brand.Texture("scene_highway"); if (tex != null) { var p = Ui.Photo(_stage, "BrBackdrop", tex, Ui.Cover(tex, 0f, 0.35f, 1f, w / h), x + 2, y + 2, w - 4, h - 4, 18, new Color(1, 1, 1, 0.16f)); }
            Ui.Icon(_stage, icon, retry || icon == "ic_info" ? Brand.Bad : Gold, x + w / 2 - 70, y + 190, 140);
            Ui.Label(_stage, title, "ExtraBold", 38, White, x, y + 370, w, 50, TextAnchor.UpperCenter);
            if (text != null) Ui.Label(_stage, text, "Regular", 21, Soft, x, y + 430, w, 30, TextAnchor.UpperCenter);
            if (button != null) { FriButton(_stage, name, x + w / 2 - 190, y + 500, 380, 64, button, Gold, Ink, go, true, retry ? "ic_reset" : "ic_bus", 22); }
        }

        // ---------------------------------------------------------------- a route in the list
        void BrCard(RectTransform host, BusRouteDto r, float y, float h)
        {
            bool sel = r.id == _brSel; float w = BrLW - 14;
            if (sel) Ui.Outlined(host, "BrCardOn", PanelFill, Gold, 0, y, w, h, 20, 2.2f); else Ui.Outlined(host, "BrCard", PanelFill, PanelLine, 0, y, w, h, 20, 1.4f);
            var tex = Brand.Texture("veh_aro_city_bus"); if (tex != null) Ui.Photo(host, "BrPhoto", tex, Ui.Cover(tex, 0f, 0f, 1f, 250f / 164f), 12, y + 12, 250, h - 24, 14);
            float x = 288; var stops = r.stops ?? new BusStopDto[0];
            float cw = BrChip(host, x, y + 16, r.code, true); string city = BrCity(r); if (city.Length > 0) BrChip(host, x + cw + 10, y + 16, city, false);
            var nm = Ui.Label(host, r.name, "ExtraBold", 28, White, x, y + 52, w - x - 210, 38); nm.resizeTextForBestFit = true; nm.resizeTextMinSize = 16; nm.resizeTextMaxSize = 28;
            var rr = r; FriButton(host, "Btn_BrView_" + r.code, w - 188, y + 22, 170, 46, sel ? "Selected" : "View Route", sel ? Color.clear : Gold, sel ? Gold : Ink, () => { _brSel = rr.id; Debug.Log("[BusRoutes] selected " + rr.code); RefreshBr(); }, true, null, 18);
            // the route as a line of stops
            float lx = x + 14, lw = w - x - 40; int n = stops.Length; float ly = y + 112;
            if (n >= 2) Ui.Flat(host, "Line", new Color(Gold.r, Gold.g, Gold.b, 0.55f), lx, ly + 6, lw, 2.5f);
            for (int i = 0; i < n; i++)
            {
                float cx = n == 1 ? lx : lx + lw * i / (n - 1f); Ui.Disc(host, "Dot", Gold, cx - 8, ly - 2, 16); Ui.Disc(host, "DotIn", new Color(0.062f, 0.070f, 0.092f), cx - 4, ly + 2, 8);
                var l = Ui.Label(host, stops[i].location != null ? stops[i].location.name : "", "SemiBold", 14, Soft, cx - 70, ly + 18, 140, 22, TextAnchor.UpperCenter); l.resizeTextForBestFit = true; l.resizeTextMinSize = 10; l.resizeTextMaxSize = 14;
            }
            BrStats(host, x, y + h - 38, r, w - x - 20, false);
        }

        float BrChipW(Transform host, string text) { var probe = Ui.Label(host, text, "SemiBold", 14, White, 0, 0, 10, 10); float tw = probe.preferredWidth; Destroy(probe.gameObject); return tw + 24; }
        float BrChip(Transform host, float x, float y, string text, bool gold)
        {
            float w = BrChipW(host, text);
            Ui.Outlined(host, "Chip", gold ? new Color(Gold.r, Gold.g, Gold.b, 0.10f) : new Color(0.2f, 0.4f, 0.8f, 0.18f), gold ? Gold : new Color(0.35f, 0.65f, 1f, 0.8f), x, y, w, 28, 8, 1.3f);
            Ui.Label(host, text, "SemiBold", 14, gold ? Gold : new Color(0.7f, 0.85f, 1f), x, y + 3, w, 22, TextAnchor.UpperCenter); return w;
        }

        /// <summary>Stops, fare per passenger, XP and demand as a row of facts (small in a list card, larger in the detail).</summary>
        void BrStats(Transform host, float x, float y, BusRouteDto r, float w, bool big)
        {
            var stops = r.stops ?? new BusStopDto[0]; var lvl = BusRouteRules.LevelOf(BusRouteRules.AverageDemand(stops.Select(s => s.demand).ToList()));
            Color dc = lvl == DemandLevel.High ? Brand.Good : lvl == DemandLevel.Medium ? Gold : Brand.Bad;
            float[] at = big ? new[] { 0f, 0.25f, 0.5f, 0.75f } : new[] { 0f, 0.16f, 0.50f, 0.72f };
            var facts = new[] { ("ic_bus", BusRouteRules.StopsText(stops.Length), big ? "" : "", White), ("ic_coin", BusRouteRules.FareText(r.fare), big ? "per passenger" : "/ passenger", White), ("ic_crown", BusRouteRules.XpText(r.xp_reward), big ? "when finished" : "", White), ("ic_bars", (big ? "" : "Demand: ") + BusRouteRules.LevelText(lvl), big ? "demand" : "", dc) };
            for (int i = 0; i < 4; i++)
            {
                float fx = x + w * at[i], fw = w * ((i < 3 ? at[i + 1] : 1f) - at[i]);
                if (big)
                {
                    Ui.Icon(host, facts[i].Item1, Gold, fx, y + 6, 28); Ui.Label(host, facts[i].Item2, "Bold", 18, facts[i].Item4, fx + 36, y, fw - 38, 24);
                    if (facts[i].Item3.Length > 0) Ui.Label(host, facts[i].Item3, "Regular", 13, Muted, fx + 36, y + 24, fw - 38, 18);
                }
                else
                {
                    Ui.Icon(host, facts[i].Item1, Gold, fx, y + 1, 22); var pr = Ui.Label(host, facts[i].Item2, "SemiBold", 15, facts[i].Item4, fx + 28, y, fw - 28, 22); float tw = pr.preferredWidth;
                    if (facts[i].Item3.Length > 0) Ui.Label(host, facts[i].Item3, "Regular", 13, Muted, fx + 28 + tw + 6, y + 2, Mathf.Max(10f, fw - 36 - tw), 20);
                }
            }
        }

        // ---------------------------------------------------------------- a run in progress
        void BuildBrRun()
        {
            var r = _bus.Route; var st = _bus.State; var stops = r.stops ?? new BusStopDto[0]; const float x = BrX, y = BrTop, w = BrLW, h = 480; var bus = BrBus(); int next = _bus.NextIndex;
            Ui.Outlined(_stage, "RunCard", PanelFill, Gold, x, y, w, h, 20, 2f);
            Ui.Rounded(_stage, "RunBadge", new Color(Gold.r, Gold.g, Gold.b, 0.16f), x + 22, y + 20, 250, 38, 10); Ui.Disc(_stage, "RunDot", Brand.Good, x + 38, y + 33, 12); Ui.Label(_stage, "ROUTE IN PROGRESS", "Bold", 17, Gold, x + 60, y + 28, 210, 24);
            string city = BrCity(r); if (city.Length > 0) BrChip(_stage, x + w - 22 - BrChipW(_stage, city), y + 25, city, false);
            BrChip(_stage, x + 22, y + 72, r.code, true); Ui.Label(_stage, r.name, "ExtraBold", 40, White, x + 22, y + 106, w - 44, 52);
            // the stops as a line: served = filled, current = ring, next ones hollow
            float lx = x + 90, lw = w - 180, ly = y + 214; int n = stops.Length;
            if (n >= 2) { Ui.Flat(_stage, "Track", new Color(1, 1, 1, 0.2f), lx, ly + 11, lw, 4); float fill = lw * BusRouteRules.LineFill(n, next); if (fill > 0f) Ui.Flat(_stage, "Fill", Gold, lx, ly + 11, fill, 4); }
            for (int i = 0; i < n; i++)
            {
                float cx = n == 1 ? lx : lx + lw * i / (n - 1f); var s = BusRouteRules.StopState(i, next);
                if (s == StopProgress.Completed) { Ui.Disc(_stage, "Done", Gold, cx - 16, ly - 3, 32); Ui.Icon(_stage, "icon_check", Ink, cx - 10, ly + 3, 20); }
                else if (s == StopProgress.Current) { Ui.Disc(_stage, "Cur", Gold, cx - 20, ly - 7, 40); Ui.Disc(_stage, "CurIn", new Color(0.062f, 0.070f, 0.092f), cx - 14, ly - 1, 28); Ui.Disc(_stage, "CurDot", Gold, cx - 7, ly + 6, 14); }
                else { Ui.Disc(_stage, "Up", new Color(1, 1, 1, 0.35f), cx - 15, ly - 2, 30); Ui.Disc(_stage, "UpIn", new Color(0.062f, 0.070f, 0.092f), cx - 12, ly + 1, 24); }
                var nm = Ui.Label(_stage, stops[i].location != null ? stops[i].location.name : "", "Bold", 18, s == StopProgress.Upcoming ? Soft : White, cx - 100, ly + 40, 200, 26, TextAnchor.UpperCenter); nm.resizeTextForBestFit = true; nm.resizeTextMinSize = 12; nm.resizeTextMaxSize = 18;
                Ui.Label(_stage, BusRouteRules.StopStateText(s), "Regular", 15, s == StopProgress.Upcoming ? Muted : Gold, cx - 100, ly + 66, 200, 22, TextAnchor.UpperCenter);
            }
            // live numbers
            int cap = bus.HasValue ? bus.Value.d.passenger_capacity : 0; float ty = y + 300, tw = (w - 44 - 24) / 3f, th = 104;
            string nextName = next < n && stops[next].location != null ? stops[next].location.name : "-";
            BrTile(x + 22, ty, tw, th, "ic_bus", "Next stop", nextName, city, null, White);
            BrTile(x + 22 + tw + 12, ty, tw, th, "ic_people", "Aboard", BusRouteRules.AboardText(st.Aboard, cap), null, cap > 0 ? (float?)BusRouteRules.AboardFraction(st.Aboard, cap) : null, White);
            BrTile(x + 22 + (tw + 12) * 2, ty, tw, th, "ic_coin", "Fares so far", BusRouteRules.Money(st.Revenue), "paid when the route is finished", null, Brand.Good);
            FriButton(_stage, "Btn_BrResume", x + 22, y + h - 82, (w - 56) / 2f, 60, "Resume driving", Gold, Ink, () => { Debug.Log("[BusRoutes] resume"); _act.Resume(); }, true, "ic_steering", 21);
            FriButton(_stage, "Btn_BrAbandon", x + 22 + (w - 56) / 2f + 12, y + h - 82, (w - 56) / 2f, 60, "Abandon route (no pay)", Color.clear, Brand.Bad, OpenBrAbandon, true, null, 20);
            // what is possible meanwhile
            const float iy = y + h + 16; BrPanel(x, iy, w, 1050 - iy);
            Ui.Icon(_stage, "ic_info", Soft, x + 28, iy + 30, 30); Ui.Label(_stage, "Other routes are available after you finish or abandon this one.", "SemiBold", 19, White, x + 76, iy + 26, w - 100, 26);
            Ui.Paragraph(_stage, "Finish the route to be paid your fares and earn the XP. You can leave this page and come back; the route stays open.", "Regular", 16, Muted, x + 76, iy + 58, w - 100, 56);
        }

        void BrTile(float x, float y, float w, float h, string icon, string label, string value, string sub, float? bar, Color valueColor)
        {
            Ui.Outlined(_stage, "RunTile", new Color(0.075f, 0.085f, 0.11f, 1f), new Color(1, 1, 1, 0.10f), x, y, w, h, 14, 1.2f);
            Ui.Icon(_stage, icon, Gold, x + 16, y + 18, 34); Ui.Label(_stage, label, "SemiBold", 16, Soft, x + 62, y + 12, w - 72, 22);
            var v = Ui.Label(_stage, value, "ExtraBold", 30, valueColor, x + 62, y + 36, w - 72, 40); v.resizeTextForBestFit = true; v.resizeTextMinSize = 16; v.resizeTextMaxSize = 30;
            if (bar.HasValue) Ui.Bar(_stage, x + 62, y + 82, w - 84, 8, bar.Value, Gold); else if (!string.IsNullOrEmpty(sub)) Ui.Label(_stage, sub, "Regular", 14, Muted, x + 62, y + 76, w - 72, 20);
        }

        // ---------------------------------------------------------------- right side: the selected route in detail, your bus, start
        void BuildBrSide()
        {
            bool running = _bus != null && _bus.Active; var r = BrRoute();
            if (r == null || (!running && _brState != LoadState.Ready) || (!running && !BrBus().HasValue)) return;
            var bus = BrBus(); var stops = r.stops ?? new BusStopDto[0]; const float x = BrRX, w = BrRW;
            // photo
            var tex = Brand.Texture("veh_aro_city_bus_hero") ?? Brand.Texture("veh_aro_city_bus"); if (bus.HasValue) { string key = VehiclePictureKey(bus.Value.v.definition_id); var t2 = key != null ? (Brand.Texture("veh_" + key + "_hero") ?? Brand.Texture("veh_" + key)) : null; if (t2 != null) tex = t2; }
            if (tex != null) Ui.Photo(_stage, "BrHero", tex, Ui.Cover(tex, 0f, 0f, 1f, w / 230f), x, 150, w, 230, 20);
            float y = 394; float cw = BrChip(_stage, x, y, r.code, true); string city = BrCity(r); if (city.Length > 0) BrChip(_stage, x + cw + 10, y, city, false);
            var nm = Ui.Label(_stage, r.name, "ExtraBold", 30, White, x, y + 36, w, 40); nm.resizeTextForBestFit = true; nm.resizeTextMinSize = 18; nm.resizeTextMaxSize = 30;
            BrStats(_stage, x, y + 84, r, w, true);
            // stops
            Ui.Label(_stage, "Route stops", "Bold", 21, White, x, 520, 300, 28); Ui.Flat(_stage, "StopsDiv", new Color(1, 1, 1, 0.08f), x, 552, w, 1.5f);
            const float pitch = 50; int next = running ? _bus.NextIndex : -1; var area = MakeScroll("br_stops", x, 560, w, 218, stops.Length * pitch + 4); var host = area.Content;
            for (int i = 0; i < stops.Length; i++)
            {
                var s = stops[i]; float ry = i * pitch + 2; var state = running ? BusRouteRules.StopState(i, next) : StopProgress.Upcoming; bool done = state == StopProgress.Completed, cur = state == StopProgress.Current;
                Ui.Disc(host, "SNum", done ? Brand.Good : cur || !running ? Gold : new Color(1, 1, 1, 0.14f), 0, ry + 6, 34); if (done) Ui.Icon(host, "icon_check", Ink, 7, ry + 13, 20); else Ui.Label(host, (i + 1).ToString(), "ExtraBold", 17, cur || !running ? Ink : White, 0, ry + 11, 34, 24, TextAnchor.UpperCenter);
                var sn = Ui.Label(host, s.location != null ? s.location.name : "", "SemiBold", 17, White, 48, ry + 4, 190, 22); sn.resizeTextForBestFit = true; sn.resizeTextMinSize = 11; sn.resizeTextMaxSize = 17;
                Ui.Label(host, cur ? "Current stop" : city, "Regular", 13, cur ? Gold : Muted, 48, ry + 25, 190, 18);
                Ui.Bar(host, 250, ry + 17, 120, 8, BusRouteRules.DemandFraction(s.demand), Gold); Ui.Label(host, s.demand + " waiting", "Regular", 13, Muted, 250, ry + 26, 120, 16);
                Ui.Label(host, BusRouteRules.AlightText(s.alight_pct, i == 0, i == stops.Length - 1), "SemiBold", 14, Soft, 380, ry + 12, w - 392, 22, TextAnchor.UpperRight);
            }
            RestoreScroll(area);
            // your bus
            const float by = 794; BrPanel(x, by, w, 138); Ui.Label(_stage, "Your bus", "Bold", 19, White, x + 20, by + 12, 300, 26);
            if (bus.HasValue)
            {
                var (v, d) = bus.Value; string key = VehiclePictureKey(v.definition_id); var bt = key != null ? Brand.Texture("veh_" + key) : Brand.Texture("veh_aro_city_bus");
                if (bt != null) Ui.Photo(_stage, "BrBusPic", bt, Ui.Cover(bt, 0f, 0f, 1f, 190f / 84f), x + 18, by + 44, 190, 84, 12);
                Ui.Label(_stage, d.name, "ExtraBold", 22, White, x + 226, by + 44, w - 246, 30);
                Ui.Icon(_stage, "ic_people", Soft, x + 226, by + 80, 22); Ui.Label(_stage, BusRouteRules.SeatsText(d.passenger_capacity), "SemiBold", 16, Soft, x + 254, by + 79, 120, 22);
                int cond = BusRouteRules.Condition(v.damage_pct); Ui.Icon(_stage, "ic_wrench", Soft, x + 226, by + 106, 20); Ui.Label(_stage, BusRouteRules.ConditionText(v.damage_pct), "SemiBold", 15, Soft, x + 252, by + 105, 160, 20);
                Ui.Bar(_stage, x + 410, by + 112, w - 430, 8, cond / 100f, cond >= 60 ? Brand.Good : cond >= 30 ? Gold : Brand.Bad);
                Ui.Label(_stage, $"Fuel {v.fuel_l:0} / {d.fuel_capacity_l:0} L", "Regular", 14, Muted, x + 410, by + 80, w - 430, 20);
            }
            if (!running) BuildBrStart(r, bus);
            else Ui.Label(_stage, "Fares are paid by the server when the route is completed. Abandoning a route pays nothing.", "Regular", 14, Muted, x, by + 150, w, 22);
        }

        void BuildBrStart(BusRouteDto r, (OwnedVehicleDto v, VehicleDefDto d)? bus)
        {
            const float x = BrRX, w = BrRW, y = 948; bool freight = Sum != null && Sum.active_job != null && Sum.active_job.Exists;
            string why = BusRouteRules.StartBlockedReason(bus.HasValue ? BrChoice(bus.Value) : null, freight, false, r != null); bool can = why == null && !_brBusy;
            FriButton(_stage, "Btn_BrStart", x, y, w, 62, _brBusy ? "Starting..." : "Start route", Gold, Ink, () => _ = BrStart(r, bus), can, "ic_steering", 22, why);
            Ui.Label(_stage, why ?? "Fares are paid by the server when the route is completed. Abandoning a route pays nothing.", "Regular", 14, why != null ? Brand.Bad : Muted, x, y + 70, w, 22);
        }

        // ================================================================================================================ actions
        async Task BrStart(BusRouteDto r, (OwnedVehicleDto v, VehicleDefDto d)? bus)
        {
            if (_brBusy || !bus.HasValue || r == null) return;
            bool freight = Sum != null && Sum.active_job != null && Sum.active_job.Exists; string why = BusRouteRules.StartBlockedReason(BrChoice(bus.Value), freight, false, true);
            if (why != null) { Toast(why, 5f); Debug.Log("[BusRoutes] start blocked: " + why); return; }
            _brBusy = true; RefreshBr();
            string err = await _act.StartBus(r, bus.Value.v); if (this == null) return; _brBusy = false;
            Debug.Log($"[BusRoutes] start route={r.code} ok={err == null}" + (err == null ? "" : " error=" + err));
            if (err != null) { Toast(err, 6f); RefreshBr(); } else RefreshBr();
        }

        void OpenBrAbandon()
        {
            var m = OpenModal("Abandon this bus route?", 640, 300); const float x0 = 640, y0 = 390;
            Ui.Paragraph(m, "You will not be paid your fares or earn the XP for this route.", "Regular", 19, Soft, x0 + 32, y0 + 84, 576, 60);
            ModalButton(m, "Btn_BrKeep", x0 + 32, y0 + 200, 250, "Keep going", new Color(1, 1, 1, 0.12f), White, CloseModal);
            ModalButton(m, "Btn_BrAbandonYes", x0 + 640 - 32 - 250, y0 + 200, 250, "Abandon", new Color(0.62f, 0.14f, 0.14f), White, () => { CloseModal(); _ = BrAbandon(); });
        }

        async Task BrAbandon()
        {
            Debug.Log("[BusRoutes] abandon"); await _bus.Abandon(); if (this == null) return;
            Debug.Log("[BusRoutes] abandoned active=" + _bus.Active.ToString().ToLowerInvariant()); _brLoadedAt = -999f; await BrLoad(); _ = RefreshSnapshot();
        }

        // ================================================================================================================ update
        void BrUpdate(float now)
        {
            float dy = 0f; Vector2 mp = Vector2.zero;
            try { dy = UnityEngine.Input.mouseScrollDelta.y; mp = UnityEngine.Input.mousePosition; } catch (InvalidOperationException) { }
            if (dy != 0f && _modal == null) { _areas.RemoveAll(a => a.View == null); foreach (var a in _areas) if (a.View.gameObject.activeInHierarchy && RectTransformUtility.RectangleContainsScreenPoint(a.View, mp, null)) { ApplyScroll(a, a.Pos - dy * 60f); break; } }
            try { if (UnityEngine.Input.GetKeyDown(KeyCode.Escape)) { if (_modal != null) CloseModal(); else OpenVehicles(); } }
            catch (InvalidOperationException) { }
        }
    }
}
