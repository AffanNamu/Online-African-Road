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
    /// The Trucks &amp; Buses page of the dashboard: the vehicle catalogue (list_vehicle_catalog), the player's own vehicles (list_my_vehicles) and the
    /// vehicle panel. ONE catalogue feeds every tab; the tabs, the search and the sort are views over it (ARO.NetCore.VehicleRules). Every number on screen is a
    /// column of the server's vehicle definition; whether a model can be bought, and why not, is the server's own verdict (purchase_state). Buying and selecting
    /// are server calls whose answer decides what is shown afterwards; the client never debits or grants anything itself.
    /// Pictures are real photographs cut from the approved ARO Motors sheets (tools/crop_vehicle_art.py). There is no 3D showroom yet and the page says so.
    /// </summary>
    public sealed partial class DashboardScreen
    {
        const float VehCardW = 303, VehCardH = 132, VehCardGap = 14;

        VehicleTab _vehTab = VehicleTab.All; VehicleSort _vehSort = VehicleSort.Catalogue;
        string _vehSearch = "", _vehSearchPending = "", _vehSel; float _vehSearchDue = -1f, _vehAt = -1000f;
        int _vehView; bool _vehInterior, _vehLoaded, _vehBusy; string _vehBusyText;
        List<VehicleRow> _vehRows = new List<VehicleRow>();
        readonly Dictionary<string, JobFitDto> _vehFit = new Dictionary<string, JobFitDto>(); readonly HashSet<string> _vehFitAsked = new HashSet<string>();
        InputField _vehSearchField;

        static readonly (VehicleTab tab, string label, string icon)[] VehTabs =
        {
            (VehicleTab.All, "All Vehicles", "ic_bus"), (VehicleTab.Trucks, "Trucks", "ic_box"), (VehicleTab.Buses, "Buses", "ic_bus"),
            (VehicleTab.Light, "Light Vehicles", "ic_car"), (VehicleTab.Owned, "Owned", "ic_check_circle"), (VehicleTab.Shop, "Shop", "ic_cart"),
        };

        public void OpenVehicles() => ShowPage(Page.Vehicles);

        void VehOpened()
        {
            if (!_vehLoaded || Time.unscaledTime - _vehAt > 20f) _ = LoadVehicles(true);
            else { Refill("VehShow", BuildVehShow); Refill("VehCards", BuildVehCards); }
        }

        // ================================================================================================================ data
        VehicleRow ToRow(CatalogItem c) => new VehicleRow
        {
            Id = c.definition_id, Name = c.name ?? "", Category = c.category ?? "truck", VehicleClass = c.vehicle_class ?? "", Manufacturer = c.manufacturer ?? "", Drive = Nz(c.drive), Engine = Nz(c.engine),
            Transmission = Nz(c.transmission), FuelType = Nz(c.fuel_type), Description = c.description ?? "", PictureKey = c.picture_key ?? "", Availability = c.availability ?? "available",
            PurchaseState = c.purchase_state ?? "available", Price = c.price, NeedMore = c.need_more, UnlockLevel = c.unlock_level, Owned = c.owned,
            Hp = c.engine_hp > 0 ? (int?)c.engine_hp : null, TorqueNm = c.torque_nm > 0 ? (int?)c.torque_nm : null, WeightKg = c.weight_kg > 0 ? (int?)c.weight_kg : null,
            TopSpeedKmh = c.top_speed_kmh, FuelCapacityL = c.fuel_capacity_l, CargoKg = c.cargo_capacity_kg, Passengers = c.passenger_capacity, Features = c.features ?? new string[0],
        };
        static string Nz(string s) => string.IsNullOrEmpty(s) ? null : s;

        string ActiveDefinitionId()
        {
            var g = _svc.Garage.Garage.Value; if (g == null || g.items == null) return null;
            var v = g.items.FirstOrDefault(x => x.selected) ?? g.items.FirstOrDefault(x => x.id == g.selected_id);
            return v != null ? v.definition_id : null;
        }
        VehicleInfo MyVehicle(string defId) { var g = _svc.Garage.Garage.Value; return g != null && g.items != null ? g.items.FirstOrDefault(x => x.definition_id == defId) : null; }
        VehicleRow SelRow() => _vehRows.FirstOrDefault(r => r.Id == _vehSel);
        bool IsActive(VehicleRow r) { var v = MyVehicle(r.Id); return v != null && v.selected; }

        async Task LoadVehicles(bool force)
        {
            if (_vehBusy && !force) return;
            _vehAt = Time.unscaledTime;
            Refill("VehShow", BuildVehShow); Refill("VehCards", BuildVehCards);                        // shows "Loading catalog..." while the Loadable is loading
            var cat = _svc.Garage.LoadCatalog(); var own = _svc.Garage.LoadGarage();
            await cat; await own;
            if (this == null) return;
            _vehLoaded = true; _vehAt = Time.unscaledTime; _vehFit.Clear(); _vehFitAsked.Clear();
            var list = _svc.Garage.Catalog.Value; _vehRows = list != null && list.items != null ? list.items.Select(ToRow).ToList() : new List<VehicleRow>();
            if (SelRow() == null) _vehSel = ActiveDefinitionId() ?? (_vehRows.Count > 0 ? _vehRows[0].Id : null);
            Debug.Log($"[Vehicles] catalog state={_svc.Garage.Catalog.State.ToString().ToLowerInvariant()} total={_vehRows.Count} level={(list != null ? list.level : 0)} balance={(list != null ? list.balance : 0)} owned={_vehRows.Count(r => r.IsOwned)} garage={_svc.Garage.Garage.State.ToString().ToLowerInvariant()}");
            Refill("VehTop", BuildVehTop); Refill("VehTabs", BuildVehTabs); Refill("VehShow", BuildVehShow); Refill("VehCards", BuildVehCards);
            AskJobFit(_vehSel);
        }

        void AskJobFit(string defId)
        {
            if (string.IsNullOrEmpty(defId) || _vehFitAsked.Contains(defId)) return; _vehFitAsked.Add(defId);
            _ = AskJobFitAsync(defId);
        }
        async Task AskJobFitAsync(string defId)
        {
            var r = await _svc.Garage.JobFit(defId);
            if (this == null) return;
            if (r.Ok) { _vehFit[defId] = r.Value; Debug.Log($"[Vehicles] jobfit id={defId} compatible={r.Value.compatible} fits={r.Value.fits} open={r.Value.open_jobs} drivable={r.Value.drivable} reason={r.Value.reason}"); }
            else { _vehFit.Remove(defId); _vehFitAsked.Remove(defId); Debug.Log($"[Vehicles] jobfit id={defId} failed"); }
            if (_page == Page.Vehicles && _vehSel == defId) Refill("VehShow", BuildVehShow);
        }

        List<VehicleRow> Shown() => VehicleRules.Filter(_vehRows, _vehTab, _vehSearch, _vehSort);

        void SelectVehicle(string defId)
        {
            if (_vehSel == defId) return;
            _vehSel = defId; _vehView = 0; _vehInterior = false; AskJobFit(defId);
            var r = SelRow(); if (r != null) { var a = VehicleRules.ActionFor(r, IsActive(r)); Debug.Log($"[Vehicles] panel id={r.Id} state={r.PurchaseState} action={a.Kind} enabled={a.Enabled} drivable={r.Drivable}"); }
            Refill("VehShow", BuildVehShow); Refill("VehCards", BuildVehCards);
        }

        void SetVehTab(VehicleTab t)
        {
            if (_vehTab == t) return; _vehTab = t;
            var shown = Shown(); if (shown.Count > 0 && !shown.Any(r => r.Id == _vehSel)) { _vehSel = shown[0].Id; _vehView = 0; _vehInterior = false; AskJobFit(_vehSel); }
            Debug.Log($"[Vehicles] view tab={t.ToString().ToLowerInvariant()} sort={_vehSort.ToString().ToLowerInvariant()} search=\"{_vehSearch}\" shown={shown.Count} selected={_vehSel}");
            Refill("VehTabs", BuildVehTabs); Refill("VehShow", BuildVehShow); Refill("VehCards", BuildVehCards);
        }

        void ApplyVehSearch()
        {
            string q = (_vehSearchPending ?? "").Trim(); if (q == _vehSearch) return; _vehSearch = q;
            var shown = Shown(); if (shown.Count > 0 && !shown.Any(r => r.Id == _vehSel)) { _vehSel = shown[0].Id; _vehView = 0; _vehInterior = false; AskJobFit(_vehSel); }
            Debug.Log($"[Vehicles] view tab={_vehTab.ToString().ToLowerInvariant()} sort={_vehSort.ToString().ToLowerInvariant()} search=\"{_vehSearch}\" shown={shown.Count} selected={_vehSel}");
            Refill("VehShow", BuildVehShow); Refill("VehCards", BuildVehCards);
        }

        static string SortLabel(VehicleSort s)
        {
            switch (s) { case VehicleSort.PriceLow: return "Price: low"; case VehicleSort.PriceHigh: return "Price: high"; case VehicleSort.Power: return "Power"; case VehicleSort.Capacity: return "Capacity"; case VehicleSort.Level: return "Level"; default: return "Catalogue"; }
        }

        void VehUpdate(float now)
        {
            if (_vehSearchDue > 0f && now > _vehSearchDue) { _vehSearchDue = -1f; ApplyVehSearch(); }
            if (_vehLoaded && !_vehBusy && now - _vehAt > 45f) { _vehAt = now; _ = LoadVehicles(false); }       // the wallet, the level and ownership stay current while the page is open
            float dy = 0f; Vector2 mp = Vector2.zero;
            try { dy = UnityEngine.Input.mouseScrollDelta.y; mp = UnityEngine.Input.mousePosition; } catch (InvalidOperationException) { }
            if (dy != 0f)
            {
                _areas.RemoveAll(a => a.View == null);
                foreach (var a in _areas) if (a.Name.StartsWith("veh_") && a.View.gameObject.activeInHierarchy && RectTransformUtility.RectangleContainsScreenPoint(a.View, mp, null)) { ApplyScroll(a, a.Pos - dy * 60f); break; }
            }
            try { bool typing = _vehSearchField != null && _vehSearchField.isFocused; if (!typing && _modal == null && UnityEngine.Input.GetKeyDown(KeyCode.Escape)) ShowPage(Page.Home); }
            catch (InvalidOperationException) { }
        }

        // ================================================================================================================ build
        void BuildVehiclesPage()
        {
            Region("VehTop", 0.02f); BuildVehTop();
            Region("VehTabs", 0.05f); BuildVehTabs();
            Region("VehShow", 0.08f); BuildVehShow();
            Region("VehCards", 0.12f); BuildVehCards();
        }

        void BuildVehTop()
        {
            Ui.Icon(_stage, "ic_bus", Gold, 322, 138, 56);
            var t = Ui.Label(_stage, "Trucks & Buses", "ExtraBold", 52, White, 396, 128, 760, 64); Ui.Drop(t, 0.5f);
            Ui.Label(_stage, "Choose your vehicle, build your fleet and prepare for the road.", "Regular", 20, new Color(0.84f, 0.86f, 0.9f), 396, 186, 1000, 28);
            const float bx = 1590, by = 130, bw = 299, bh = 50;
            Ui.Outlined(_stage, "CompareBg", new Color(0.05f, 0.055f, 0.075f, 0.92f), new Color(1, 1, 1, 0.2f), bx, by, bw, bh, 14);
            Ui.Icon(_stage, "ic_gauge", White, bx + 18, by + 11, 28); Ui.Label(_stage, "Compare Vehicles", "SemiBold", 20, White, bx + 58, by + 11, bw - 66, 30);
            Ui.Click(_stage, "Btn_VehCompare", bx, by, bw, bh, OpenCompare);
        }

        void BuildVehTabs()
        {
            float x = 308;
            foreach (var t in VehTabs)
            {
                bool on = t.tab == _vehTab; int count = _vehRows.Count(r => VehicleRules.InTab(r, t.tab));
                string label = t.label;
                var probe = Ui.Label(_stage, label, on ? "Bold" : "SemiBold", 19, White, 0, 0, 10, 10); float tw = probe.preferredWidth; Destroy(probe.gameObject);
                float w = 18 + 26 + 10 + tw + 18, y = 207, h = 46;
                if (on) Ui.Rounded(_stage, "TabOn", Gold, x, y, w, h, 12); else Ui.Outlined(_stage, "Tab", new Color(0.05f, 0.055f, 0.075f, 0.9f), new Color(1, 1, 1, 0.14f), x, y, w, h, 12);
                Ui.Icon(_stage, t.icon, on ? Ink : new Color(0.86f, 0.88f, 0.92f), x + 16, y + 10, 26);
                Ui.Label(_stage, label, on ? "Bold" : "SemiBold", 19, on ? Ink : White, x + 50, y + 10, tw + 6, 28);
                var tab = t.tab; Ui.Click(_stage, "Btn_VehTab_" + tab, x, y, w, h, () => SetVehTab(tab));
                x += w + 10;
            }
            // sort (cycles) and search
            float sx = 1352, sw = 190, sh = 46, sy = 207;
            Ui.Outlined(_stage, "SortBg", new Color(0.05f, 0.055f, 0.075f, 0.9f), new Color(1, 1, 1, 0.16f), sx, sy, sw, sh, 12);
            Ui.Label(_stage, "Sort: " + SortLabel(_vehSort), "SemiBold", 17, White, sx, sy + 11, sw, 26, TextAnchor.UpperCenter);
            Ui.Click(_stage, "Btn_VehSort", sx, sy, sw, sh, () => { _vehSort = (VehicleSort)(((int)_vehSort + 1) % 6); Refill("VehTabs", BuildVehTabs); Refill("VehCards", BuildVehCards); Debug.Log($"[Vehicles] view tab={_vehTab.ToString().ToLowerInvariant()} sort={_vehSort.ToString().ToLowerInvariant()} search=\"{_vehSearch}\" shown={Shown().Count} selected={_vehSel}"); });
            float qx = 1554, qw = 335;
            Ui.Outlined(_stage, "VehSearchBox", new Color(0.05f, 0.055f, 0.075f, 0.92f), new Color(1, 1, 1, 0.16f), qx, sy, qw, sh, 12);
            var c = new Color(0.82f, 0.84f, 0.88f);
            Ui.Disc(_stage, "SearchRing", c, qx + 14, sy + 10, 17); Ui.Disc(_stage, "SearchHole", new Color(0.05f, 0.055f, 0.075f, 1f), qx + 16, sy + 12, 13); Ui.Segment(_stage, new Vector2(qx + 28, sy + 25), new Vector2(qx + 36, sy + 33), 3, c);
            var areaGo = new GameObject("VehSearchArea", typeof(RectTransform), typeof(RectMask2D));
            var area = Ui.Place(areaGo, _stage, Ui.Anchor.TopLeft, qx + 46, sy, qw - 58, sh);
            var txt = Ui.Label(area, "", "Regular", 18, White, 0, 0, qw - 58, sh, TextAnchor.MiddleLeft);
            var ph = Ui.Label(area, "Search vehicles...", "Regular", 18, new Color(1, 1, 1, 0.5f), 0, 0, qw - 58, sh, TextAnchor.MiddleLeft);
            var hit = areaGo.AddComponent<Image>(); hit.color = new Color(1, 1, 1, 0.001f); areaGo.name = "Veh_Search_Input";
            var f = areaGo.AddComponent<InputField>(); f.targetGraphic = hit; f.textComponent = txt; f.placeholder = ph; f.lineType = InputField.LineType.SingleLine; f.characterLimit = 40; f.text = _vehSearch; f.transition = Selectable.Transition.None;
            f.customCaretColor = true; f.caretColor = Gold; f.selectionColor = new Color(Gold.r, Gold.g, Gold.b, 0.35f);
            f.onValueChanged.AddListener(v => { _vehSearchPending = v ?? ""; _vehSearchDue = Time.unscaledTime + 0.35f; });
            _vehSearchField = f;
        }

        // ---------------------------------------------------------------- the vehicle panel (info + showroom picture + views)
        const float ShowY = 268, ShowH = 420, InfoW = 330, ShowX = 308;

        void BuildVehShow()
        {
            var cat = _svc.Garage.Catalog;
            if (cat.State == LoadState.Idle || cat.State == LoadState.Loading)
            { Ui.Outlined(_stage, "ShowFrame", PanelFill, PanelLine, ShowX, ShowY, 1581, ShowH, 20, 1.5f); Ui.Label(_stage, "Loading catalog...", "SemiBold", 26, Muted, ShowX, ShowY + ShowH / 2 - 20, 1581, 40, TextAnchor.UpperCenter); return; }
            if (cat.State == LoadState.Unavailable)
            {
                Ui.Outlined(_stage, "ShowFrame", PanelFill, PanelLine, ShowX, ShowY, 1581, ShowH, 20, 1.5f);
                Ui.Icon(_stage, "ic_bus", new Color(1, 1, 1, 0.2f), 760, ShowY + 120, 90);
                Ui.Label(_stage, "Unable to load vehicles.", "Bold", 30, Brand.Bad, 880, ShowY + 126, 700, 40);
                Ui.Paragraph(_stage, cat.Error ?? "Please try again.", "Regular", 19, Soft, 880, ShowY + 168, 700, 54);
                Ui.Rounded(_stage, "RetryBtn", Gold, 880, ShowY + 232, 190, 48, 12); Ui.Label(_stage, "Retry", "Bold", 20, Ink, 880, ShowY + 243, 190, 28, TextAnchor.UpperCenter);
                Ui.Click(_stage, "Btn_RetryVehicles", 880, ShowY + 232, 190, 48, () => _ = LoadVehicles(true)); return;
            }
            var r = SelRow();
            if (r == null)
            {
                Ui.Outlined(_stage, "ShowFrame", PanelFill, PanelLine, ShowX, ShowY, 1581, ShowH, 20, 1.5f);
                Ui.Icon(_stage, "ic_bus", new Color(1, 1, 1, 0.2f), 760, ShowY + 140, 90);
                Ui.Label(_stage, "No vehicles found.", "Bold", 30, White, 880, ShowY + 150, 700, 40); Ui.Label(_stage, "Try another tab or search.", "Regular", 19, Soft, 880, ShowY + 194, 700, 28); return;
            }
            BuildInfoPanel(r); BuildShowcase(r); BuildViewThumbs(r);
        }

        void BuildInfoPanel(VehicleRow r)
        {
            float X = ShowX, Y = ShowY, W = InfoW, H = ShowH;
            Ui.Outlined(_stage, "InfoPanel", PanelFill, PanelLine, X, Y, W, H, 20, 1.5f);
            Ui.Label(_stage, r.Manufacturer.ToUpperInvariant(), "Bold", 14, Gold, X + 22, Y + 14, W - 44, 20);
            var nm = Ui.Label(_stage, r.Name, "ExtraBold", 31, White, X + 22, Y + 32, W - 44, 40); nm.resizeTextForBestFit = true; nm.resizeTextMinSize = 18; nm.resizeTextMaxSize = 31; nm.horizontalOverflow = HorizontalWrapMode.Wrap; nm.verticalOverflow = VerticalWrapMode.Truncate;
            string sub = string.IsNullOrEmpty(r.Drive) ? r.VehicleClass : r.Drive + " " + r.VehicleClass;
            Ui.Label(_stage, sub, "SemiBold", 17, White, X + 22, Y + 74, W - 44, 24);
            var ds = Ui.Paragraph(_stage, r.Description, "Regular", 14, new Color(0.8f, 0.83f, 0.88f), X + 22, Y + 102, W - 44, 58); ds.verticalOverflow = VerticalWrapMode.Truncate;

            // the four bars: each value against the largest in the catalogue (relative, never a rating nobody measured)
            double maxHp = _vehRows.Where(v => v.Hp.HasValue).Select(v => (double)v.Hp.Value).DefaultIfEmpty(0).Max();
            double maxSpeed = _vehRows.Select(v => v.TopSpeedKmh).DefaultIfEmpty(0).Max(), maxFuel = _vehRows.Select(v => v.FuelCapacityL).DefaultIfEmpty(0).Max();
            bool pax = VehicleRules.CapacityLabel(r) == "Passengers";
            double maxCap = _vehRows.Where(v => (VehicleRules.CapacityLabel(v) == "Passengers") == pax).Select(VehicleRules.Capacity).DefaultIfEmpty(0).Max();
            float y = Y + 166; const float pitch = 31;
            StatRow(X + 22, y, W - 44, "Power", r.Hp.HasValue ? r.Hp + " HP" : "n/a", r.Hp.HasValue ? VehicleRules.Fraction(r.Hp.Value, maxHp) : 0, Gold); y += pitch;
            StatRow(X + 22, y, W - 44, "Top speed", r.TopSpeedKmh.ToString("0") + " km/h", VehicleRules.Fraction(r.TopSpeedKmh, maxSpeed), Blue); y += pitch;
            StatRow(X + 22, y, W - 44, "Fuel tank", r.FuelCapacityL.ToString("N0") + " L", VehicleRules.Fraction(r.FuelCapacityL, maxFuel), Brand.Good); y += pitch;
            StatRow(X + 22, y, W - 44, VehicleRules.CapacityLabel(r), VehicleRules.CapacityText(r), VehicleRules.Fraction(VehicleRules.Capacity(r), maxCap), new Color(0.72f, 0.5f, 0.95f)); y += pitch + 2;
            // job compatibility: the server's answer about the open jobs
            string fitText; _vehFit.TryGetValue(r.Id, out var fit);
            fitText = fit == null ? (_vehFitAsked.Contains(r.Id) ? "Checking the open jobs..." : "Job compatibility is not available right now.")
                : VehicleRules.JobFitText(true, fit.drivable, fit.compatible, fit.reason, fit.fits, fit.open_jobs, fit.category);
            Color fc = fit != null && fit.drivable && fit.compatible ? Brand.Good : fit != null && fit.drivable ? Gold : Muted;
            Ui.Disc(_stage, "FitDot", fc, X + 22, y + 6, 10);
            var ft = Ui.Paragraph(_stage, fitText, "Regular", 14, new Color(0.84f, 0.86f, 0.9f), X + 40, y, W - 62, 36); ft.verticalOverflow = VerticalWrapMode.Truncate;
            // primary action (the server's purchase_state decides) + Garage
            var a = VehicleRules.ActionFor(r, IsActive(r)); float by = Y + H - 96;
            Color bg = a.Kind == VehicleActionKind.Buy || a.Kind == VehicleActionKind.Select ? Gold : a.Kind == VehicleActionKind.Selected ? new Color(Brand.Good.r, Brand.Good.g, Brand.Good.b, 0.22f) : new Color(0.16f, 0.18f, 0.23f);
            Color fg = a.Kind == VehicleActionKind.Buy || a.Kind == VehicleActionKind.Select ? Ink : a.Kind == VehicleActionKind.Selected ? Brand.Good : new Color(0.78f, 0.8f, 0.85f);
            if (_vehBusy) { bg = new Color(0.16f, 0.18f, 0.23f); fg = new Color(0.78f, 0.8f, 0.85f); }
            Ui.Rounded(_stage, "PrimaryBg", bg, X + 22, by, W - 44, 44, 12);
            Ui.Label(_stage, _vehBusy ? _vehBusyText : a.Label, "Bold", 19, fg, X + 22, by + 8, W - 44, 28, TextAnchor.UpperCenter);
            Ui.Click(_stage, "Btn_VehPrimary", X + 22, by, W - 44, 44, () => OnPrimary(r));
            Ui.Outlined(_stage, "GarageBg", new Color(0.05f, 0.055f, 0.075f, 0.9f), new Color(0.72f, 0.5f, 0.95f, 0.55f), X + 22, by + 52, W - 44, 36, 11);
            Ui.Icon(_stage, "ic_wrench", new Color(0.8f, 0.62f, 1f), X + 34, by + 56, 26); Ui.Label(_stage, "Open Garage", "SemiBold", 17, White, X + 66, by + 60, W - 100, 26);
            Ui.Click(_stage, "Btn_VehGarage", X + 22, by + 52, W - 44, 36, () => _act.Garage());
        }

        void StatRow(float x, float y, float w, string label, string value, double fraction, Color c)
        {
            Ui.Label(_stage, label, "Regular", 14, new Color(0.7f, 0.73f, 0.8f), x, y, w * 0.5f, 18);
            Ui.Label(_stage, value, "SemiBold", 14, White, x + w * 0.45f, y, w * 0.55f, 18, TextAnchor.UpperRight);
            Ui.Bar(_stage, x, y + 20, w, 5, (float)fraction, c);
        }

        // ---------------------------------------------------------------- the showroom picture
        string[] ViewKeys(VehicleRow r)
        {
            var l = new List<string> { "veh_" + r.PictureKey + "_hero" };
            for (int i = 1; i <= 5; i++) if (Brand.Texture("veh_" + r.PictureKey + "_v" + i) != null) l.Add("veh_" + r.PictureKey + "_v" + i);
            return l.ToArray();
        }

        void BuildShowcase(VehicleRow r)
        {
            float X = ShowX + InfoW + 12, Y = ShowY, W = 1581 - InfoW - 12 - 150, H = ShowH;
            var keys = ViewKeys(r); bool hasInt = Brand.Texture("veh_" + r.PictureKey + "_int") != null;
            string key = _vehInterior && hasInt ? "veh_" + r.PictureKey + "_int" : keys[Mathf.Clamp(_vehView, 0, keys.Length - 1)];
            var tex = Brand.Texture(key) ?? Brand.Texture("veh_" + r.PictureKey);
            Ui.Outlined(_stage, "ShowFrame", new Color(0.02f, 0.025f, 0.04f, 1f), new Color(Gold.r, Gold.g, Gold.b, 0.45f), X, Y, W, H, 20, 1.5f);
            if (tex != null) Ui.Photo(_stage, "ShowPhoto", tex, Ui.Cover(tex, 0f, 0f, 1f, (W - 3f) / (H - 3f)), X + 1.5f, Y + 1.5f, W - 3, H - 3, 19);
            else Ui.Label(_stage, "No picture for this vehicle yet.", "SemiBold", 22, Muted, X, Y + H / 2 - 16, W, 32, TextAnchor.UpperCenter);
            Ui.Gradient(_stage, "ShowShadeB", new Color(0, 0, 0, 0), new Color(0, 0, 0, 0.7f), false, X + 2, Y + H - 90, W - 4, 88);
            // state badge
            var act = VehicleRules.ActionFor(r, IsActive(r)); string badge = VehicleRules.CardBadge(r, IsActive(r));
            Color bc = r.IsOwned ? Brand.Good : r.PurchaseState == "coming_soon" ? Blue : r.PurchaseState == "level_required" ? Brand.Bad : Gold;
            var probe = Ui.Label(_stage, badge, "Bold", 16, White, 0, 0, 10, 10); float bw = probe.preferredWidth + 28; Destroy(probe.gameObject);
            Ui.Rounded(_stage, "BadgeBg", new Color(bc.r * 0.28f, bc.g * 0.28f, bc.b * 0.28f, 0.95f), X + 18, Y + 18, bw, 32, 9); Ui.Label(_stage, badge, "Bold", 16, bc, X + 18, Y + 24, bw, 22, TextAnchor.UpperCenter);
            // Exterior / Interior / 360: only what exists is enabled
            float tx = X + W - 18; float[] ws = { 118, 118, 134 }; string[] labels = { "Exterior", "Interior", "360° View" };
            for (int i = 2; i >= 0; i--)
            {
                tx -= ws[i]; bool on = (i == 0 && !_vehInterior) || (i == 1 && _vehInterior); bool enabled = i == 0 || (i == 1 && hasInt);
                if (on) Ui.Rounded(_stage, "SegOn", Gold, tx, Y + 16, ws[i], 38, 10); else Ui.Outlined(_stage, "Seg", new Color(0.04f, 0.045f, 0.06f, 0.82f), new Color(1, 1, 1, enabled ? 0.22f : 0.1f), tx, Y + 16, ws[i], 38, 10, 1.2f);
                Ui.Label(_stage, labels[i], on ? "Bold" : "SemiBold", 16, on ? Ink : enabled ? White : new Color(1, 1, 1, 0.38f), tx, Y + 24, ws[i], 24, TextAnchor.UpperCenter);
                int idx = i; Ui.Click(_stage, idx == 0 ? "Btn_VehExterior" : idx == 1 ? "Btn_VehInterior" : "Btn_Veh360", tx, Y + 16, ws[i], 38, () =>
                {
                    if (idx == 0) { _vehInterior = false; Refill("VehShow", BuildVehShow); }
                    else if (idx == 1) { if (hasInt) { _vehInterior = true; Refill("VehShow", BuildVehShow); } else Toast("There is no interior picture for this vehicle yet."); }
                    else Toast(new VehicleRules.Showroom().Unavailable3DReason);
                });
                tx -= 8;
            }
            // the player's own vehicle: live condition and fuel from the server
            var mine = MyVehicle(r.Id);
            if (mine != null)
            {
                var cs = VehicleRules.ConditionOf(mine.condition_pct); var fs = VehicleRules.FuelOf(mine.fuel_pct);
                Color cc = cs == ConditionState.Ready ? Brand.Good : cs == ConditionState.MaintenanceRecommended ? Gold : Brand.Bad;
                Color fcc = fs == FuelState.Healthy ? Brand.Good : fs == FuelState.Low ? Gold : Brand.Bad;
                float cy = Y + H - 62;
                Ui.Rounded(_stage, "StatusBg", new Color(0.02f, 0.025f, 0.04f, 0.82f), X + 18, cy, 520, 44, 12);
                Ui.Disc(_stage, "CondDot", cc, X + 34, cy + 17, 11); Ui.Label(_stage, $"Condition {mine.condition_pct:0}%  {VehicleRules.ConditionText(mine.condition_pct)}", "SemiBold", 17, White, X + 52, cy + 10, 310, 24);
                Ui.Disc(_stage, "FuelDot", fcc, X + 372, cy + 17, 11); Ui.Label(_stage, $"Fuel {mine.fuel_pct:0}%", "SemiBold", 17, White, X + 390, cy + 10, 140, 24);
            }
        }

        void BuildViewThumbs(VehicleRow r)
        {
            float X = ShowX + 1581 - 138, Y = ShowY; var keys = ViewKeys(r);
            Ui.Outlined(_stage, "ThumbsPanel", PanelFill, PanelLine, X - 8, Y, 146, ShowH, 20, 1.5f);
            int n = Mathf.Min(keys.Length, 5); float ty = Y + 10; const float th = 72, pitch = 80;
            for (int i = 0; i < n; i++)
            {
                var tex = Brand.Texture(keys[i]); bool on = !_vehInterior && _vehView == i;
                Ui.Outlined(_stage, "ThumbFrame", new Color(0, 0, 0, 1), on ? Gold : new Color(1, 1, 1, 0.14f), X, ty, 130, th, 10, on ? 2.5f : 1.2f);
                if (tex != null) Ui.Photo(_stage, "ThumbPhoto", tex, Ui.Cover(tex, 0f, 0f, 1f, 126f / (th - 4f)), X + 2, ty + 2, 126, th - 4, 8);
                int idx = i; Ui.Click(_stage, "Btn_VehView_" + i, X, ty, 130, th, () => { _vehView = idx; _vehInterior = false; Refill("VehShow", BuildVehShow); });
                ty += pitch;
            }
        }

        // ---------------------------------------------------------------- the catalogue cards
        void BuildVehCards()
        {
            const float X = 308, Y = 704, W = 1581, H = 366;
            var cat = _svc.Garage.Catalog;
            if (cat.State == LoadState.Idle || cat.State == LoadState.Loading) { Ui.Label(_stage, "Loading catalog...", "SemiBold", 22, Muted, X, Y + 20, W, 32, TextAnchor.UpperCenter); return; }
            if (cat.State == LoadState.Unavailable) return;         // the panel above carries the error and the Retry button
            var shown = Shown();
            if (shown.Count == 0)
            {
                string title = !string.IsNullOrEmpty(_vehSearch) ? "No vehicles found." : _vehTab == VehicleTab.Owned ? "No owned vehicles yet." : _vehTab == VehicleTab.Buses ? "No buses available." : _vehTab == VehicleTab.Trucks ? "No trucks match your filters." : "No vehicles found.";
                Ui.Icon(_stage, "ic_bus", new Color(1, 1, 1, 0.2f), X + 20, Y + 40, 64); Ui.Label(_stage, title, "Bold", 26, White, X + 100, Y + 40, 900, 34);
                Ui.Label(_stage, !string.IsNullOrEmpty(_vehSearch) ? "Try a different search." : "Try another tab.", "Regular", 18, Soft, X + 100, Y + 78, 900, 26);
                if (!string.IsNullOrEmpty(_vehSearch)) { Ui.Rounded(_stage, "ClearBtn", Gold, X + 100, Y + 116, 200, 42, 11); Ui.Label(_stage, "Clear search", "Bold", 18, Ink, X + 100, Y + 125, 200, 26, TextAnchor.UpperCenter); Ui.Click(_stage, "Btn_VehClearSearch", X + 100, Y + 116, 200, 42, () => { _vehSearch = ""; _vehSearchPending = ""; if (_vehSearchField != null) _vehSearchField.text = ""; Refill("VehShow", BuildVehShow); Refill("VehCards", BuildVehCards); }); }
                return;
            }
            // sections by kind (the same rows, grouped for reading)
            var sections = new List<(string title, List<VehicleRow> rows)>();
            foreach (var g in new[] { ("Trucks", "truck"), ("Buses", "bus"), ("Light Vehicles", "light") })
            {
                var rows = shown.Where(v => g.Item2 == "light" ? VehicleRules.IsLight(v.Category) : v.Category == g.Item2).ToList();
                if (rows.Count > 0) sections.Add((g.Item1, rows));
            }
            int perRow = Mathf.Max(1, Mathf.FloorToInt((W - 24 + VehCardGap) / (VehCardW + VehCardGap)));
            float contentH = 0; foreach (var s in sections) contentH += 40 + Mathf.CeilToInt(s.rows.Count / (float)perRow) * (VehCardH + VehCardGap);
            var area = MakeScroll("veh_cards", X, Y, W, H, contentH + 6);
            float y = 0;
            foreach (var s in sections)
            {
                Ui.Label(area.Content, s.title, "Bold", 24, White, 4, y + 2, 260, 32);
                var cnt = Ui.Label(area.Content, s.rows.Count.ToString(), "Bold", 15, Muted, 4 + 14 + 12 * s.title.Length + 10, y + 8, 40, 22);
                y += 40;
                for (int i = 0; i < s.rows.Count; i++)
                {
                    float cx = 2 + (i % perRow) * (VehCardW + VehCardGap), cy = y + (i / perRow) * (VehCardH + VehCardGap);
                    VehCard(area.Content, s.rows[i], cx, cy);
                }
                y += Mathf.CeilToInt(s.rows.Count / (float)perRow) * (VehCardH + VehCardGap);
            }
            RestoreScroll(area);
        }

        void VehCard(Transform host, VehicleRow r, float x, float y)
        {
            bool sel = r.Id == _vehSel, active = IsActive(r);
            if (sel) Ui.Rounded(host, "CardGlow", new Color(Gold.r, Gold.g, Gold.b, 0.12f), x - 3, y - 3, VehCardW + 6, VehCardH + 6, 18);
            Ui.Outlined(host, "VehCard", CardFill, sel ? Gold : new Color(1, 1, 1, 0.10f), x, y, VehCardW, VehCardH, 15, sel ? 2.5f : 1.3f);
            var tex = Brand.Texture("veh_" + r.PictureKey);
            Color tint = r.Drivable || r.IsOwned ? Color.white : new Color(0.78f, 0.78f, 0.82f);
            if (tex != null) Ui.Photo(host, "VehPhoto", tex, Ui.Cover(tex, 0f, 0f, 1f, 150f / (VehCardH - 6f)), x + 3, y + 3, 150, VehCardH - 6, 12, tint);
            float tx = x + 164, tw = VehCardW - 164 - 10;
            var nm = Ui.Label(host, r.Name, "Bold", 17, White, tx, y + 8, tw, 44); nm.resizeTextForBestFit = true; nm.resizeTextMinSize = 12; nm.resizeTextMaxSize = 17; nm.horizontalOverflow = HorizontalWrapMode.Wrap; nm.verticalOverflow = VerticalWrapMode.Truncate;
            string sub = string.IsNullOrEmpty(r.Drive) ? r.VehicleClass : r.Drive + " " + r.VehicleClass;
            var sb = Ui.Label(host, sub, "Regular", 13, Muted, tx, y + 54, tw, 34); sb.horizontalOverflow = HorizontalWrapMode.Wrap; sb.verticalOverflow = VerticalWrapMode.Truncate;
            string key = r.Hp.HasValue ? r.Hp + " HP" : VehicleRules.CapacityText(r);
            Ui.Label(host, key, "SemiBold", 14, new Color(0.86f, 0.88f, 0.92f), tx, y + 88, tw, 20);
            string badge = VehicleRules.CardBadge(r, active);
            Color bc = r.IsOwned ? Brand.Good : r.PurchaseState == "coming_soon" ? Blue : r.PurchaseState == "level_required" ? Brand.Bad : Lime;
            Ui.Label(host, badge, "Bold", r.PurchaseState == "available" || r.PurchaseState == "insufficient_funds" ? 17 : 14, bc, tx, y + 108, tw, 22);
            var cr = r; Ui.Click(host, "Btn_Veh_" + r.Id, x, y, VehCardW, VehCardH, () => SelectVehicle(cr.Id));
        }
    }
}
