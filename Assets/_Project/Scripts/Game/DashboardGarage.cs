using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ARO.Backend;
using ARO.NetCore;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace ARO.Game
{
    /// <summary>
    /// The Garage page: My Vehicles (the player's real ownership), the 3D showroom (<see cref="ShowroomStage"/>: orbit, zoom, pan, presets, turntable, part
    /// inspection, live paint), the vehicle panel (definition values and live state), and the work area: paint skins (server catalogue, purchase, equip),
    /// maintenance (server quote, repair, refuel). Categories that the game has no system for yet (wheels, tyres, accessories, upgrades, performance) are
    /// listed with the reason instead of fake content. Everything that costs money or changes a vehicle is a server call read back afterwards.
    /// </summary>
    public sealed partial class DashboardScreen
    {
        const float GarListX = 308, GarTopY = 268, GarListW = 330, GarH = 414, GarViewX = 650, GarViewW = 860, GarInfoX = 1522, GarInfoW = 367, GarBottomY = 700, GarBottomH = 370;
        static readonly Color Purple = new Color(0.72f, 0.5f, 0.95f);

        string _gVeh, _gCat = "paint", _gSkinFilter = "all", _gPreview; GarageSection _gSection = GarageSection.Customize; int _gInfoTab;
        ShowroomStage _show; RawImage _gRaw; bool _gOpen, _gDataLoaded; float _gAt = -1000f;
        SkinList _gSkins; LoadState _gSkinState = LoadState.Idle; string _gSkinErr; ServiceQuote _gQuote; LoadState _gQuoteState = LoadState.Idle; string _gQuoteErr;
        bool _gBusy; string _gBusyText, _gPartLine; string _gShowFor;
        ShowroomSurface _surface;

        static readonly (GarageSection s, string label, string icon)[] GarTabs =
        {
            (GarageSection.Customize, "Customize", "ic_car"), (GarageSection.Upgrades, "Upgrades", "ic_gear"), (GarageSection.Maintenance, "Maintenance", "ic_wrench"),
            (GarageSection.PaintSkins, "Paint & Skins", "ic_leaf"), (GarageSection.Accessories, "Accessories", "ic_box"), (GarageSection.Performance, "Performance", "ic_gauge"),
        };

        public void OpenGarage() => ShowPage(Page.Garage);

        // ================================================================================================================ lifecycle
        void GarOpened()
        {
            _gOpen = true; if (_show == null) { _show = ShowroomStage.Create(1032, 497); }
            if (_gRaw != null) _gRaw.texture = _show.Output;
            _show.Enter(); Debug.Log("[Garage] opened");
            if (!_gDataLoaded || Time.unscaledTime - _gAt > 20f) _ = LoadGarage();
            else { _show.MarkDirty(); RefreshGarage(); }
        }
        void GarClosed() { _gOpen = false; if (_show != null) _show.Exit(); Debug.Log("[Garage] closed"); }
        void OnDestroy() { if (_show != null) Destroy(_show.gameObject); }

        void RefreshGarage() { Refill("GarTabs", BuildGarTabs); Refill("GarList", BuildGarList); Refill("GarViewUi", BuildGarViewUi); Refill("GarInfo", BuildGarInfo); Refill("GarBottom", BuildGarBottom); }

        // ================================================================================================================ data
        List<VehicleInfo> Owned() { var g = _svc.Garage.Garage.Value; return g != null && g.items != null ? g.items.ToList() : new List<VehicleInfo>(); }
        VehicleInfo GVeh() => Owned().FirstOrDefault(v => v.id == _gVeh);
        VehicleRow GRow(VehicleInfo v) => v == null ? null : _vehRows.FirstOrDefault(r => r.Id == v.definition_id);

        async Task LoadGarage()
        {
            _gAt = Time.unscaledTime; Refill("GarList", BuildGarList); Refill("GarInfo", BuildGarInfo);
            var own = _svc.Garage.LoadGarage(); var cat = _svc.Garage.LoadCatalog(); await own; await cat;
            if (this == null) return;
            var list = _svc.Garage.Catalog.Value; _vehRows = list != null && list.items != null ? list.items.Select(ToRow).ToList() : _vehRows;
            _gDataLoaded = true; _gAt = Time.unscaledTime;
            var owned = Owned(); if (owned.All(v => v.id != _gVeh)) _gVeh = (owned.FirstOrDefault(v => v.selected) ?? owned.FirstOrDefault())?.id;
            Debug.Log($"[Garage] vehicles state={_svc.Garage.Garage.State.ToString().ToLowerInvariant()} count={owned.Count} selected={_gVeh} active={owned.FirstOrDefault(v => v.selected)?.definition_id}");
            RefreshGarage(); LoadShowroom(); _ = LoadSkins(); _ = LoadQuote();
        }

        void LoadShowroom()
        {
            var v = GVeh(); if (v == null || _show == null) { Refill("GarViewUi", BuildGarViewUi); return; }
            _gShowFor = v.id; _gPartLine = null; _gPreview = null;
            _show.Highlight(null); _show.Load(v.definition_id); ApplyEquippedSkinToShowroom(); Refill("GarViewUi", BuildGarViewUi); Refill("GarInfo", BuildGarInfo); Refill("GarBottom", BuildGarBottom);
        }

        async Task LoadSkins()
        {
            var v = GVeh(); if (v == null) return; string id = v.id; _gSkinState = LoadState.Loading; _gSkinErr = null; Refill("GarBottom", BuildGarBottom);
            var r = await _svc.Garage.Skins(id); if (this == null || _gVeh != id) return;
            if (r.Ok) { _gSkins = r.Value; _gSkinState = LoadState.Ready; Debug.Log($"[Garage] skins state=ready count={r.Value.items.Length} equipped={r.Value.equipped_id}"); }
            else { _gSkinState = LoadState.Unavailable; _gSkinErr = r.UserMessage; Debug.Log("[Garage] skins state=unavailable"); }
            ApplyEquippedSkinToShowroom(); Refill("GarBottom", BuildGarBottom);
        }

        async Task LoadQuote()
        {
            var v = GVeh(); if (v == null) return; string id = v.id; _gQuoteState = LoadState.Loading; _gQuoteErr = null;
            var r = await _svc.Garage.Quote(id); if (this == null || _gVeh != id) return;
            if (r.Ok) { _gQuote = r.Value; _gQuoteState = LoadState.Ready; Debug.Log($"[Garage] quote state=ready repair={r.Value.repair_cost} refuel={r.Value.refuel_cost} litres={r.Value.refuel_liters:0}"); }
            else { _gQuoteState = LoadState.Unavailable; _gQuoteErr = r.UserMessage; Debug.Log("[Garage] quote state=unavailable"); }
            Refill("GarBottom", BuildGarBottom); Refill("GarInfo", BuildGarInfo);
        }

        SkinItem EquippedSkin() => _gSkins != null && _gSkins.items != null ? _gSkins.items.FirstOrDefault(s => s.equipped) : null;
        void ApplyEquippedSkinToShowroom() { if (_show == null || _show.State != ShowroomState.Ready) return; var s = _gPreview != null && _gSkins != null ? _gSkins.items.FirstOrDefault(x => x.id == _gPreview) : EquippedSkin(); PaintShowroom(s); }
        void PaintShowroom(SkinItem s) { if (s == null) _show.SetSkin(null, null, 0.35, 0.4); else _show.SetSkin(s.base_color, s.accent_color, s.metallic, s.roughness); }

        void SelectGarageVehicle(string id)
        {
            if (_gVeh == id || _gBusy) return;
            _gVeh = id; _gSkins = null; _gQuote = null; _gSkinState = LoadState.Idle; _gQuoteState = LoadState.Idle; _gInfoTab = 0;
            Debug.Log($"[Garage] select vehicle={id} definition={GVeh()?.definition_id}");
            RefreshGarage(); LoadShowroom(); _ = LoadSkins(); _ = LoadQuote();
        }

        // ================================================================================================================ build
        void BuildGaragePage()
        {
            Region("GarTop", 0.02f); BuildGarTop();
            Region("GarTabs", 0.05f); BuildGarTabs();
            Region("GarList", 0.08f); BuildGarList();
            Region("GarView", 0.10f); BuildGarView();
            Region("GarViewUi", 0.11f); BuildGarViewUi();
            Region("GarInfo", 0.12f); BuildGarInfo();
            Region("GarBottom", 0.16f); BuildGarBottom();
        }

        void BuildGarTop()
        {
            Ui.Icon(_stage, "ic_wrench", Gold, 322, 138, 56);
            var t = Ui.Label(_stage, "Garage", "ExtraBold", 52, White, 396, 128, 600, 64); Ui.Drop(t, 0.5f);
            Ui.Label(_stage, "Customize, upgrade and maintain your vehicles.", "Regular", 20, new Color(0.84f, 0.86f, 0.9f), 396, 186, 1000, 28);
        }

        void BuildGarTabs()
        {
            float x = 308;
            foreach (var t in GarTabs)
            {
                bool on = t.s == _gSection;
                var probe = Ui.Label(_stage, t.label, on ? "Bold" : "SemiBold", 19, White, 0, 0, 10, 10); float tw = probe.preferredWidth; Destroy(probe.gameObject);
                float w = 18 + 26 + 10 + tw + 18, y = 207, h = 46;
                if (on) Ui.Rounded(_stage, "TabOn", Gold, x, y, w, h, 12); else Ui.Outlined(_stage, "Tab", new Color(0.05f, 0.055f, 0.075f, 0.9f), new Color(1, 1, 1, 0.14f), x, y, w, h, 12);
                Ui.Icon(_stage, t.icon, on ? Ink : new Color(0.86f, 0.88f, 0.92f), x + 16, y + 10, 26); Ui.Label(_stage, t.label, on ? "Bold" : "SemiBold", 19, on ? Ink : White, x + 50, y + 10, tw + 6, 28);
                var sec = t.s; Ui.Click(_stage, "Btn_GarTab_" + sec, x, y, w, h, () => SetGarSection(sec)); x += w + 10;
            }
        }

        void SetGarSection(GarageSection s)
        {
            if (_gSection == s) return; _gSection = s; if (s == GarageSection.PaintSkins) _gCat = "paint";
            Debug.Log($"[Garage] section={s.ToString().ToLowerInvariant()} category={_gCat}");
            if (s == GarageSection.Maintenance) _ = LoadQuote();
            Refill("GarTabs", BuildGarTabs); Refill("GarBottom", BuildGarBottom);
        }

        // ---------------------------------------------------------------- my vehicles
        void BuildGarList()
        {
            float X = GarListX, Y = GarTopY, W = GarListW, H = GarH;
            Ui.Outlined(_stage, "ListPanel", PanelFill, PanelLine, X, Y, W, H, 20, 1.5f);
            var owned = Owned();
            Ui.Label(_stage, "My Vehicles", "Bold", 24, White, X + 20, Y + 14, 200, 32);
            Ui.Rounded(_stage, "CountBg", new Color(1, 1, 1, 0.12f), X + 176, Y + 18, 34, 24, 12); Ui.Label(_stage, owned.Count.ToString(), "Bold", 15, White, X + 176, Y + 20, 34, 20, TextAnchor.UpperCenter);
            Ui.Outlined(_stage, "AddBg", new Color(0.05f, 0.055f, 0.075f, 0.9f), new Color(1, 1, 1, 0.2f), X + W - 62, Y + 12, 42, 38, 10); Ui.Label(_stage, "+", "Bold", 26, White, X + W - 62, Y + 14, 42, 32, TextAnchor.UpperCenter);
            Ui.Click(_stage, "Btn_GarAddVehicle", X + W - 62, Y + 12, 42, 38, OpenVehicles);
            var gs = _svc.Garage.Garage;
            if ((gs.State == LoadState.Idle || gs.State == LoadState.Loading) && SmokeMode.E2E) Debug.Log($"[UIDiag] GarList is drawn in the LOADING state, without vehicle cards (state={gs.State}, previous list kept in memory={gs.Value != null && gs.Value.items != null}) t={Time.realtimeSinceStartup:0.00}");
            if (gs.State == LoadState.Idle || gs.State == LoadState.Loading) { Ui.Label(_stage, "Loading vehicles...", "SemiBold", 20, Muted, X, Y + 150, W, 30, TextAnchor.UpperCenter); return; }
            if (gs.State == LoadState.Unavailable)
            {
                Ui.Label(_stage, "Unable to load your vehicles.", "Bold", 20, Brand.Bad, X + 20, Y + 110, W - 40, 28); Ui.Paragraph(_stage, gs.Error ?? "Please try again.", "Regular", 15, Soft, X + 20, Y + 142, W - 40, 44);
                Ui.Rounded(_stage, "RetryBtn", Gold, X + 20, Y + 200, 150, 42, 11); Ui.Label(_stage, "Retry", "Bold", 18, Ink, X + 20, Y + 208, 150, 26, TextAnchor.UpperCenter); Ui.Click(_stage, "Btn_RetryGarage", X + 20, Y + 200, 150, 42, () => _ = LoadGarage()); return;
            }
            if (owned.Count == 0)
            {
                Ui.Icon(_stage, "ic_bus", new Color(1, 1, 1, 0.2f), X + 20, Y + 90, 56); Ui.Label(_stage, "You do not own a vehicle yet.", "Bold", 19, White, X + 88, Y + 92, W - 100, 26);
                Ui.Paragraph(_stage, "Visit Trucks & Buses to get one.", "Regular", 15, Soft, X + 88, Y + 122, W - 100, 44); return;
            }
            const float rowH = 104, pitch = 112; float viewH = H - 70;
            var area = MakeScroll("veh_garage_list", X + 10, Y + 62, W - 20, viewH, owned.Count * pitch + 4);
            for (int i = 0; i < owned.Count; i++)
            {
                var v = owned[i]; var row = GRow(v); float y = 2 + i * pitch; bool sel = v.id == _gVeh; var tag = GarageRules.TagFor(v.status, v.selected, v.condition_pct);
                Ui.Outlined(area.Content, "GarCard", sel ? new Color(Gold.r, Gold.g, Gold.b, 0.10f) : CardFill, sel ? Gold : new Color(1, 1, 1, 0.1f), 2, y, W - 28, rowH, 14, sel ? 2.5f : 1.3f);
                var tex = row != null ? Brand.Texture("veh_" + row.PictureKey) : null;
                if (tex != null) Ui.Photo(area.Content, "GarThumb", tex, Ui.Cover(tex, 0f, 0f, 1f, 112f / (rowH - 8f)), 6, y + 4, 112, rowH - 8, 10);
                var nm = Ui.Label(area.Content, v.name, "Bold", 17, White, 128, y + 10, W - 28 - 130, 26); nm.resizeTextForBestFit = true; nm.resizeTextMinSize = 12; nm.resizeTextMaxSize = 17;
                string sub = row != null ? (string.IsNullOrEmpty(row.Drive) ? row.VehicleClass : row.Drive + " " + row.VehicleClass) : v.category; Ui.Label(area.Content, sub, "Regular", 13, Muted, 128, y + 38, W - 28 - 130, 20);
                Color tc = tag == VehicleTag.Active || tag == VehicleTag.Ready ? Brand.Good : tag == VehicleTag.OnJob ? Blue : tag == VehicleTag.LowFuel || tag == VehicleTag.Maintenance ? Gold : Brand.Bad;
                Ui.Disc(area.Content, "TagDot", tc, 128, y + 70, 11); Ui.Label(area.Content, GarageRules.TagText(tag), "SemiBold", 15, tc, 146, y + 66, 140, 22);
                var vid = v.id; Ui.Click(area.Content, "Btn_GarVeh_" + v.definition_id, 2, y, W - 28, rowH, () => SelectGarageVehicle(vid));
            }
            RestoreScroll(area);
        }

        // ---------------------------------------------------------------- the 3D viewer (the picture is a RenderTexture of the real showroom)
        void BuildGarView()
        {
            float X = GarViewX, Y = GarTopY, W = GarViewW, H = GarH;
            Ui.Outlined(_stage, "ViewFrame", new Color(0.02f, 0.022f, 0.03f, 1f), new Color(Gold.r, Gold.g, Gold.b, 0.4f), X, Y, W, H, 20, 1.5f);
            var go = new GameObject("ShowroomView", typeof(RectTransform), typeof(RawImage), typeof(ShowroomSurface)); go.transform.SetParent(_stage, false);
            var rt = (RectTransform)go.transform; rt.anchorMin = rt.anchorMax = new Vector2(0, 1); rt.pivot = new Vector2(0, 1); rt.anchoredPosition = new Vector2(X + 2, -(Y + 2)); rt.sizeDelta = new Vector2(W - 4, H - 4);
            _gRaw = go.GetComponent<RawImage>(); _gRaw.texture = _show != null ? _show.Output : null; _gRaw.raycastTarget = true; _gRaw.color = Color.white;
            _surface = go.GetComponent<ShowroomSurface>(); _surface.Rect = rt;
            _surface.Orbit = (dx, dy) => { if (_show != null) _show.OrbitBy(dx, dy); };
            _surface.Pan = (dx, dy) => { if (_show != null) _show.PanBy(dx, dy); };
            _surface.Zoom = z => { if (_show != null) _show.ZoomBy(z); };
            _surface.Clicked = vp => OnShowroomClick(vp);
        }

        void OnShowroomClick(Vector2 vp)
        {
            if (_show == null || _show.State != ShowroomState.Ready) return;
            var p = _show.Pick(vp); _show.Highlight(p);
            _gPartLine = p == null ? null : p.label;
            Debug.Log($"[Showroom] pick part={(p != null ? p.kind : "none")}");
            Refill("GarViewUi", BuildGarViewUi);
        }

        void BuildGarViewUi()
        {
            float X = GarViewX, Y = GarTopY, W = GarViewW, H = GarH;
            var st = _show != null ? _show.State : ShowroomState.Idle;
            var v = GVeh();
            if (v == null && (_svc.Garage.Garage.State == LoadState.Ready || _svc.Garage.Garage.State == LoadState.Empty)) { Ui.Label(_stage, "Choose a vehicle to inspect.", "SemiBold", 22, Muted, X, Y + H / 2 - 16, W, 32, TextAnchor.UpperCenter); return; }
            if (st == ShowroomState.Idle || st == ShowroomState.Loading) { Ui.Label(_stage, v != null ? "Loading " + v.name + "..." : "Loading showroom...", "SemiBold", 24, Soft, X, Y + H / 2 - 18, W, 36, TextAnchor.UpperCenter); return; }
            if (st == ShowroomState.Failed)
            {
                Ui.Rounded(_stage, "FailBg", new Color(0.03f, 0.03f, 0.04f, 0.9f), X + 2, Y + 2, W - 4, H - 4, 19);
                Ui.Label(_stage, "Unable to load the showroom.", "Bold", 28, Brand.Bad, X, Y + 150, W, 38, TextAnchor.UpperCenter); Ui.Paragraph(_stage, _show.Error ?? "Please try again.", "Regular", 18, Soft, X + 120, Y + 196, W - 240, 56, TextAnchor.UpperCenter);
                Ui.Rounded(_stage, "RetryBtn", Gold, X + W / 2 - 90, Y + 266, 180, 46, 12); Ui.Label(_stage, "Retry", "Bold", 20, Ink, X + W / 2 - 90, Y + 276, 180, 28, TextAnchor.UpperCenter); Ui.Click(_stage, "Btn_ShowroomRetry", X + W / 2 - 90, Y + 266, 180, 46, LoadShowroom); return;
            }
            // view mode buttons (left): Exterior / Interior / 360 / Reset
            bool hasInt = _show.Model != null && _show.Model.HasInterior; bool turn = _show.Orbit != null && _show.Orbit.AutoRotate;
            (string name, string label, string icon, bool on, bool enabled, Action go)[] btns =
            {
                ("Btn_ShowExterior", "Exterior", "ic_car", !_show.InteriorView && !turn, true, () => { _show.SetView(ViewPreset.Default); AfterView(); }),
                ("Btn_ShowInterior", "Interior", "ic_user_circle", _show.InteriorView, hasInt, () => { if (hasInt) { _show.SetView(ViewPreset.Interior); AfterView(); } else Toast("This model has no interior yet."); }),
                ("Btn_Show360", "360° View", "ic_gauge", turn, true, () => { _show.SetTurntable(!turn); AfterView(); }),
                ("Btn_ShowReset", "Reset", "ic_clock", false, true, () => { _show.ResetCamera(); AfterView(); }),
            };
            float by = Y + 20;
            foreach (var b in btns)
            {
                Ui.Outlined(_stage, "ModeBg", b.on ? new Color(Gold.r, Gold.g, Gold.b, 0.2f) : new Color(0.03f, 0.035f, 0.05f, 0.78f), b.on ? Gold : new Color(1, 1, 1, b.enabled ? 0.22f : 0.1f), X + 14, by, 80, 78, 12, 1.4f);
                Ui.Icon(_stage, b.icon, b.on ? Gold : b.enabled ? White : new Color(1, 1, 1, 0.35f), X + 14 + 24, by + 8, 32);
                Ui.Label(_stage, b.label, "SemiBold", 13, b.on ? Gold : b.enabled ? White : new Color(1, 1, 1, 0.38f), X + 14, by + 50, 80, 20, TextAnchor.UpperCenter);
                var act = b.go; Ui.Click(_stage, b.name, X + 14, by, 80, 78, () => act()); by += 88;
            }
            // model status (top right) and the part inspection / hint line (bottom)
            string ms = _show.Model.isPlaceholder ? "Placeholder model" : "Production model"; var probe = Ui.Label(_stage, ms, "SemiBold", 14, White, 0, 0, 10, 10); float mw = probe.preferredWidth + 26; Destroy(probe.gameObject);
            Color mc = _show.Model.isPlaceholder ? Gold : Brand.Good; Ui.Outlined(_stage, "ModelChip", new Color(0.03f, 0.035f, 0.05f, 0.8f), new Color(mc.r, mc.g, mc.b, 0.7f), X + W - 16 - mw, Y + 16, mw, 30, 9, 1.2f); Ui.Label(_stage, ms, "SemiBold", 14, mc, X + W - 16 - mw, Y + 21, mw, 20, TextAnchor.UpperCenter);
            // presets and zoom (bottom)
            (string name, string label, Action go)[] chips =
            {
                ("Btn_ShowFront", "Front", () => { _show.SetView(ViewPreset.Front); AfterView(); }), ("Btn_ShowRear", "Rear", () => { _show.SetView(ViewPreset.Rear); AfterView(); }),
                ("Btn_ShowLeft", "Left", () => { _show.SetView(ViewPreset.Left); AfterView(); }), ("Btn_ShowRight", "Right", () => { _show.SetView(ViewPreset.Right); AfterView(); }),
                ("Btn_ShowTop", "Top", () => { _show.SetView(ViewPreset.Top); AfterView(); }), ("Btn_ShowWheel", "Wheels", () => { _show.SetView(ViewPreset.Wheel); AfterView(); }),
                ("Btn_ShowLights", _show.LightsOn ? "Lights: on" : "Lights: off", () => { _show.SetLights(!_show.LightsOn); Refill("GarViewUi", BuildGarViewUi); }),
                ("Btn_ShowZoomIn", "+", () => _show.ZoomBy(2.5f)), ("Btn_ShowZoomOut", "−", () => _show.ZoomBy(-2.5f)),
            };
            float cx = X + 112, cy = Y + H - 52;
            foreach (var c in chips)
            {
                var pr = Ui.Label(_stage, c.label, "SemiBold", 15, White, 0, 0, 10, 10); float cw = c.label.Length <= 1 ? 40 : pr.preferredWidth + 28; Destroy(pr.gameObject);
                Ui.Outlined(_stage, "PresetBg", new Color(0.03f, 0.035f, 0.05f, 0.8f), new Color(1, 1, 1, 0.2f), cx, cy, cw, 36, 10, 1.2f); Ui.Label(_stage, c.label, "SemiBold", 15, White, cx, cy + 7, cw, 22, TextAnchor.UpperCenter);
                var act = c.go; Ui.Click(_stage, c.name, cx, cy, cw, 36, () => act()); cx += cw + 8;
            }
            string line = _gPartLine != null ? "Selected: " + _gPartLine + "  ·  paint only; no separate part condition or upgrades exist yet" : "Drag to rotate · scroll to zoom · right-drag to pan · click a part to inspect";
            Ui.Label(_stage, line, "Regular", 14, new Color(0.8f, 0.83f, 0.88f), X + 112, Y + H - 78, W - 130, 20);
        }
        void AfterView() { Refill("GarViewUi", BuildGarViewUi); }

        // ---------------------------------------------------------------- the vehicle panel
        void BuildGarInfo()
        {
            float X = GarInfoX, Y = GarTopY, W = GarInfoW, H = GarH;
            Ui.Outlined(_stage, "InfoPanel", PanelFill, PanelLine, X, Y, W, H, 20, 1.5f);
            var v = GVeh(); var row = GRow(v);
            if (v == null) { Ui.Label(_stage, "No vehicle selected.", "SemiBold", 19, Muted, X, Y + 170, W, 28, TextAnchor.UpperCenter); return; }
            var nm = Ui.Label(_stage, v.name, "ExtraBold", 28, White, X + 20, Y + 14, W - 150, 38); nm.resizeTextForBestFit = true; nm.resizeTextMinSize = 15; nm.resizeTextMaxSize = 28; nm.horizontalOverflow = HorizontalWrapMode.Wrap; nm.verticalOverflow = VerticalWrapMode.Truncate;
            string sub = row != null ? (string.IsNullOrEmpty(row.Drive) ? row.VehicleClass : row.Drive + " " + row.VehicleClass) : v.category; Ui.Label(_stage, sub, "Regular", 16, Soft, X + 20, Y + 52, W - 40, 22);
            var tag = GarageRules.TagFor(v.status, v.selected, v.condition_pct); Color tc = tag == VehicleTag.Active || tag == VehicleTag.Ready ? Brand.Good : tag == VehicleTag.OnJob ? Blue : tag == VehicleTag.LowFuel || tag == VehicleTag.Maintenance ? Gold : Brand.Bad;
            var tp = Ui.Label(_stage, GarageRules.TagText(tag), "Bold", 14, White, 0, 0, 10, 10); float tw = tp.preferredWidth + 30; Destroy(tp.gameObject);
            Ui.Rounded(_stage, "TagBg", new Color(tc.r * 0.28f, tc.g * 0.28f, tc.b * 0.28f, 0.95f), X + W - 18 - tw, Y + 18, tw, 30, 9); Ui.Label(_stage, GarageRules.TagText(tag), "Bold", 14, tc, X + W - 18 - tw, Y + 24, tw, 20, TextAnchor.UpperCenter);
            // Overview / Stats / Info
            string[] tabs = { "Overview", "Stats", "Info" }; float tx = X + 18, tabW = (W - 36 - 12) / 3f;
            for (int i = 0; i < 3; i++)
            {
                bool on = i == _gInfoTab; if (on) Ui.Rounded(_stage, "InfoTabOn", Gold, tx, Y + 84, tabW, 38, 10); else Ui.Outlined(_stage, "InfoTab", new Color(0.05f, 0.055f, 0.075f, 0.9f), new Color(1, 1, 1, 0.12f), tx, Y + 84, tabW, 38, 10, 1.1f);
                Ui.Label(_stage, tabs[i], on ? "Bold" : "SemiBold", 16, on ? Ink : White, tx, Y + 92, tabW, 22, TextAnchor.UpperCenter); int idx = i; Ui.Click(_stage, "Btn_GarInfoTab_" + tabs[i], tx, Y + 84, tabW, 38, () => { _gInfoTab = idx; Refill("GarInfo", BuildGarInfo); }); tx += tabW + 6;
            }
            float y = Y + 130;
            if (row == null) { Ui.Label(_stage, "No specification is published for this vehicle.", "Regular", 15, Muted, X + 20, y, W - 40, 40); }
            else if (_gInfoTab == 0)
            {
                double maxHp = _vehRows.Where(r => r.Hp.HasValue).Select(r => (double)r.Hp.Value).DefaultIfEmpty(0).Max(), maxTq = _vehRows.Where(r => r.TorqueNm.HasValue).Select(r => (double)r.TorqueNm.Value).DefaultIfEmpty(0).Max();
                double maxSp = _vehRows.Select(r => r.TopSpeedKmh).DefaultIfEmpty(0).Max(), maxFu = _vehRows.Select(r => r.FuelCapacityL).DefaultIfEmpty(0).Max(); bool pax = VehicleRules.CapacityLabel(row) == "Passengers";
                double maxCap = _vehRows.Where(r => (VehicleRules.CapacityLabel(r) == "Passengers") == pax).Select(VehicleRules.Capacity).DefaultIfEmpty(0).Max();
                InfoBar(X, ref y, W, "Power", row.Hp.HasValue ? row.Hp + " HP" : "n/a", row.Hp.HasValue ? VehicleRules.Fraction(row.Hp.Value, maxHp) : 0, Gold);
                InfoBar(X, ref y, W, "Torque", row.TorqueNm.HasValue ? row.TorqueNm.Value.ToString("N0") + " Nm" : "n/a", row.TorqueNm.HasValue ? VehicleRules.Fraction(row.TorqueNm.Value, maxTq) : 0, Gold);
                InfoBar(X, ref y, W, "Top speed", row.TopSpeedKmh.ToString("0") + " km/h", VehicleRules.Fraction(row.TopSpeedKmh, maxSp), Blue);
                InfoBar(X, ref y, W, "Fuel capacity", row.FuelCapacityL.ToString("N0") + " L", VehicleRules.Fraction(row.FuelCapacityL, maxFu), Brand.Good);
                InfoBar(X, ref y, W, VehicleRules.CapacityLabel(row), VehicleRules.CapacityText(row), VehicleRules.Fraction(VehicleRules.Capacity(row), maxCap), Purple);
            }
            else if (_gInfoTab == 1)
            {
                var lines = new List<(string, string)>();
                void L(string k, string val) { if (!string.IsNullOrEmpty(val)) lines.Add((k, val)); }
                L("Manufacturer", row.Manufacturer); L("Class", row.VehicleClass); L("Drive", row.Drive); L("Engine", row.Engine); L("Transmission", row.Transmission); L("Fuel type", row.FuelType);
                if (row.WeightKg.HasValue) L(row.Id == "" ? "Weight" : "Weight (" + WeightBasis(v) + ")", row.WeightKg.Value.ToString("N0") + " kg");
                L("Mileage", v.mileage_km.ToString("N1") + " km"); L("Required level", row.UnlockLevel.ToString());
                foreach (var l in lines.Take(8)) { Ui.Label(_stage, l.Item1, "Regular", 15, new Color(0.7f, 0.73f, 0.8f), X + 20, y, 150, 20); Ui.Label(_stage, l.Item2, "SemiBold", 15, White, X + 150, y, W - 170, 20, TextAnchor.UpperRight); y += 26; }
            }
            else
            {
                var ds = Ui.Paragraph(_stage, string.IsNullOrEmpty(row.Description) ? "No description yet." : row.Description, "Regular", 14, new Color(0.8f, 0.83f, 0.88f), X + 20, y, W - 40, 92); ds.verticalOverflow = VerticalWrapMode.Truncate; y += 96;
                if (row.Features != null && row.Features.Length > 0) { Ui.Label(_stage, string.Join("  ·  ", row.Features.Take(4)), "SemiBold", 13, Gold, X + 20, y, W - 40, 36); }
                if (_show != null && _show.Model != null) Ui.Label(_stage, "Model: " + (_show.Model.isPlaceholder ? "development placeholder" : "production") + (_show.Model.LodLevels > 1 ? " · " + _show.Model.LodLevels + " LOD levels" : ""), "Regular", 13, Muted, X + 20, y + 40, W - 40, 20);
            }
            // live state: condition and fuel (server values), then the primary action
            float sy = Y + H - 138;
            Color cc = VehicleRules.ConditionOf(v.condition_pct) == ConditionState.Ready ? Brand.Good : VehicleRules.ConditionOf(v.condition_pct) == ConditionState.MaintenanceRecommended ? Gold : Brand.Bad;
            Color fc = VehicleRules.FuelOf(v.fuel_pct) == FuelState.Healthy ? Blue : VehicleRules.FuelOf(v.fuel_pct) == FuelState.Low ? Gold : Brand.Bad;
            StateBar(X, sy, W, "Condition", v.condition_pct, cc); StateBar(X, sy + 30, W, "Fuel Level", v.fuel_pct, fc);
            float by = Y + H - 76; bool active = v.selected;
            Ui.Rounded(_stage, "ActiveBg", active ? new Color(Brand.Good.r, Brand.Good.g, Brand.Good.b, 0.22f) : Gold, X + 18, by, W - 36, 52, 12);
            Ui.Label(_stage, _gBusy ? _gBusyText : active ? "Active vehicle" : "Set as Active", "Bold", 20, active ? Brand.Good : Ink, X + 18, by + 12, W - 36, 28, TextAnchor.UpperCenter);
            Ui.Click(_stage, "Btn_GarSetActive", X + 18, by, W - 36, 52, () => _ = SetActive(v));
        }
        string WeightBasis(VehicleInfo v) { var r = GRow(v); var item = _svc.Garage.Catalog.Value?.items?.FirstOrDefault(i => i.definition_id == v.definition_id); return item != null && item.weight_basis == "gross" ? "gross" : "kerb"; }

        void InfoBar(float x, ref float y, float w, string label, string value, double fraction, Color c)
        {
            Ui.Label(_stage, label, "Regular", 15, new Color(0.72f, 0.75f, 0.82f), x + 20, y, w * 0.5f, 20); Ui.Label(_stage, value, "SemiBold", 15, White, x + w * 0.45f, y, w * 0.55f - 20, 20, TextAnchor.UpperRight);
            Ui.Bar(_stage, x + 20, y + 22, w - 40, 6, (float)fraction, c); y += 28;       // five bars must end above the live Condition / Fuel block (36 each overlapped it)
        }
        void StateBar(float x, float y, float w, string label, double pct, Color c)
        {
            Ui.Label(_stage, label, "SemiBold", 15, new Color(0.8f, 0.83f, 0.88f), x + 20, y, 120, 20); Ui.Bar(_stage, x + 140, y + 7, w - 140 - 76, 8, (float)(pct / 100.0), c); Ui.Label(_stage, pct.ToString("0") + "%", "Bold", 15, White, x + w - 70, y, 52, 20, TextAnchor.UpperRight);
        }

        async Task SetActive(VehicleInfo v)
        {
            if (_gBusy || v == null) return; if (v.selected) { Toast(v.name + " is already your active vehicle."); return; }
            _gBusy = true; _gBusyText = "Saving selection..."; Refill("GarInfo", BuildGarInfo);
            var r = await _svc.Garage.SetActive(v.id); if (this == null) return;
            Debug.Log($"[Garage] active id={v.definition_id} ok={r.Ok} error={r.ErrorCode}"); _gBusy = false;
            Toast(r.Ok ? v.name + " is now your active vehicle." : "Vehicle update failed. " + (r.UserMessage ?? "Try again."), 4f);
            await _svc.Garage.LoadGarage(); _ = RefreshSnapshot(); if (this == null) return; RefreshGarage();
        }
    }

    /// <summary>The input surface of the showroom picture: drag = orbit, right/middle drag or two fingers = pan, wheel or pinch = zoom, click = inspect a part.</summary>
    public sealed class ShowroomSurface : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IDragHandler, IScrollHandler
    {
        public RectTransform Rect; public Action<float, float> Orbit, Pan; public Action<float> Zoom; public Action<Vector2> Clicked;
        readonly Dictionary<int, Vector2> _p = new Dictionary<int, Vector2>(); bool _dragged; float _pinch;
        float Dist() { var l = _p.Values.ToList(); return l.Count >= 2 ? Vector2.Distance(l[0], l[1]) : 0f; }

        public void OnPointerDown(PointerEventData e) { _p[e.pointerId] = e.position; if (_p.Count == 1) _dragged = false; if (_p.Count == 2) _pinch = Dist(); }
        public void OnDrag(PointerEventData e)
        {
            if (!_p.ContainsKey(e.pointerId)) _p[e.pointerId] = e.position; Vector2 prev = _p[e.pointerId]; _p[e.pointerId] = e.position; Vector2 d = e.position - prev; _dragged = true;
            if (_p.Count >= 2) { float nd = Dist(); Zoom?.Invoke((nd - _pinch) * 0.03f); _pinch = nd; Pan?.Invoke(d.x * 0.5f, d.y * 0.5f); }
            else if (e.button == PointerEventData.InputButton.Right || e.button == PointerEventData.InputButton.Middle) Pan?.Invoke(d.x, d.y);
            else Orbit?.Invoke(d.x, d.y);
        }
        public void OnPointerUp(PointerEventData e)
        {
            if (!_dragged && _p.Count == 1 && Clicked != null && Rect != null && RectTransformUtility.ScreenPointToLocalPointInRectangle(Rect, e.position, null, out Vector2 local))
            {
                var r = Rect.rect; Clicked(new Vector2((local.x - r.xMin) / r.width, (local.y - r.yMin) / r.height));
            }
            _p.Remove(e.pointerId);
        }
        public void OnScroll(PointerEventData e) { Zoom?.Invoke(e.scrollDelta.y); }
    }
}
