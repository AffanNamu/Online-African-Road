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
    /// <summary>Garage work area (under the showroom): paint skins, maintenance, and the honest "not available yet" panels.</summary>
    public sealed partial class DashboardScreen
    {
        void GarUpdate(float now)
        {
            if (_show != null) _show.Tick(Time.unscaledDeltaTime);
            if (_gDataLoaded && !_gBusy && now - _gAt > 45f) { _gAt = now; _ = LoadGarage(); }
            float dy = 0f; Vector2 mp = Vector2.zero;
            try { dy = UnityEngine.Input.mouseScrollDelta.y; mp = UnityEngine.Input.mousePosition; } catch (InvalidOperationException) { }
            if (dy != 0f)
            {
                _areas.RemoveAll(a => a.View == null);
                foreach (var a in _areas) if (a.Name.StartsWith("veh_") && a.View.gameObject.activeInHierarchy && RectTransformUtility.RectangleContainsScreenPoint(a.View, mp, null)) { ApplyScroll(a, a.Pos - dy * 60f); break; }
            }
            try { if (_modal == null && UnityEngine.Input.GetKeyDown(KeyCode.Escape)) ShowPage(Page.Home); } catch (InvalidOperationException) { }
        }

        void BuildGarBottom()
        {
            float X = 308, Y = GarBottomY, W = 1581, H = GarBottomH;
            var v = GVeh();
            if (v == null) return;
            switch (_gSection)
            {
                case GarageSection.Customize: BuildCustomize(X, Y, W, H, v); break;
                case GarageSection.PaintSkins: _gCat = "paint"; BuildCustomize(X, Y, W, H, v); break;
                case GarageSection.Maintenance: BuildMaintenance(X, Y, W, H, v); break;
                case GarageSection.Upgrades: BuildUnavailable(X, Y, W, H, "Upgrades", GarageRules.UpgradeCategories, GarageRules.UpgradeReason); break;
                case GarageSection.Performance: BuildUnavailable(X, Y, W, H, "Performance", new[] { "Power", "Torque", "Top speed", "Handling", "Braking", "Fuel efficiency" }, "Performance tuning is not part of the game yet: the values on the vehicle panel are the vehicle's own definition and cannot be changed here."); break;
                default: BuildUnavailable(X, Y, W, H, "Accessories", new[] { "Roof accessories", "Mirrors", "Side steps", "Bull bars", "Exhaust", "Mudguards", "Horns", "Antenna" }, "Accessories arrive with the production vehicle models, which define where each accessory attaches."); break;
            }
        }

        // ---------------------------------------------------------------- customize: categories, paint skins
        void BuildCustomize(float X, float Y, float W, float H, VehicleInfo v)
        {
            bool paintOk = _show != null && _show.State == ShowroomState.Ready && _show.Model != null && _show.Model.paint.Count > 0;
            var slots = GarageRules.CustomSlots(paintOk); float cx = X;
            foreach (var s in slots)
            {
                bool on = s.Key == _gCat; var probe = Ui.Label(_stage, s.Label, on ? "Bold" : "SemiBold", 17, White, 0, 0, 10, 10); float tw = probe.preferredWidth; Destroy(probe.gameObject); float w = tw + 56;
                if (on) Ui.Outlined(_stage, "CatOn", new Color(Gold.r, Gold.g, Gold.b, 0.12f), Gold, cx, Y, w, 46, 12, 2.2f); else Ui.Outlined(_stage, "Cat", new Color(0.05f, 0.055f, 0.075f, 0.9f), new Color(1, 1, 1, 0.12f), cx, Y, w, 46, 12, 1.2f);
                Ui.Icon(_stage, s.Available ? "ic_wrench" : "ic_lock", on ? Gold : s.Available ? White : new Color(1, 1, 1, 0.4f), cx + 14, Y + 11, 24);
                Ui.Label(_stage, s.Label, on ? "Bold" : "SemiBold", 17, on ? White : s.Available ? White : new Color(1, 1, 1, 0.55f), cx + 44, Y + 11, tw + 8, 26);
                string key = s.Key; Ui.Click(_stage, "Btn_GarCat_" + key, cx, Y, w, 46, () => { _gCat = key; Debug.Log($"[Garage] category={key}"); Refill("GarBottom", BuildGarBottom); }); cx += w + 8;
            }
            float py = Y + 58, ph = H - 58;
            Ui.Outlined(_stage, "WorkPanel", PanelFill, PanelLine, X, py, W, ph, 18, 1.4f);
            var slot = slots.First(s => s.Key == _gCat);
            if (!slot.Available)
            {
                Ui.Icon(_stage, "ic_lock", new Color(1, 1, 1, 0.25f), X + 40, py + 70, 64); Ui.Label(_stage, slot.Label + " is not available yet.", "Bold", 28, White, X + 124, py + 74, 900, 36);
                Ui.Paragraph(_stage, slot.Reason ?? "", "Regular", 18, Soft, X + 124, py + 118, 1100, 60); return;
            }
            BuildSkins(X, py, W, ph, v);
        }

        void BuildSkins(float X, float Y, float W, float H, VehicleInfo v)
        {
            Ui.Label(_stage, "Paint & Skins", "Bold", 26, White, X + 24, Y + 14, 400, 34);
            if (_gSkinState == LoadState.Idle || _gSkinState == LoadState.Loading) { Ui.Label(_stage, "Loading paints...", "SemiBold", 20, Muted, X, Y + H / 2 - 16, W, 30, TextAnchor.UpperCenter); return; }
            if (_gSkinState == LoadState.Unavailable)
            {
                Ui.Label(_stage, "Unable to load the paints.", "Bold", 24, Brand.Bad, X + 40, Y + 80, 800, 32); Ui.Label(_stage, _gSkinErr ?? "Please try again.", "Regular", 17, Soft, X + 40, Y + 116, 900, 26);
                Ui.Rounded(_stage, "RetryBtn", Gold, X + 40, Y + 160, 170, 44, 11); Ui.Label(_stage, "Retry", "Bold", 18, Ink, X + 40, Y + 169, 170, 26, TextAnchor.UpperCenter); Ui.Click(_stage, "Btn_RetrySkins", X + 40, Y + 160, 170, 44, () => _ = LoadSkins()); return;
            }
            // filters (left)
            (string key, string label)[] filters = { ("all", "All Skins"), ("official", "Official ARO"), ("custom", "Custom"), ("mine", "My Skins") }; float fy = Y + 58;
            foreach (var f in filters)
            {
                bool on = f.key == _gSkinFilter; if (on) Ui.Rounded(_stage, "FilterOn", Gold, X + 18, fy, 190, 42, 11); else Ui.Outlined(_stage, "Filter", new Color(0.05f, 0.055f, 0.075f, 0.9f), new Color(1, 1, 1, 0.1f), X + 18, fy, 190, 42, 11, 1.1f);
                Ui.Label(_stage, f.label, on ? "Bold" : "SemiBold", 17, on ? Ink : White, X + 18, fy + 9, 190, 26, TextAnchor.UpperCenter); string fk = f.key; Ui.Click(_stage, "Btn_SkinFilter_" + fk, X + 18, fy, 190, 42, () => { _gSkinFilter = fk; Refill("GarBottom", BuildGarBottom); }); fy += 50;
            }
            var all = _gSkins != null && _gSkins.items != null ? _gSkins.items : new SkinItem[0];
            var shown = all.Where(s => _gSkinFilter == "all" || (_gSkinFilter == "official" && s.official) || (_gSkinFilter == "custom" && !s.official) || (_gSkinFilter == "mine" && s.owned)).ToList();
            float gx = X + 226, gw = W - 226 - 20;
            if (_gPreview != null) { var ps = all.FirstOrDefault(s => s.id == _gPreview); if (ps != null && !ps.equipped) Ui.Label(_stage, "Preview: " + ps.name + " (not saved until you equip it)", "SemiBold", 16, Gold, X + 300, Y + 20, 800, 24); }
            if (shown.Count == 0)
            {
                Ui.Label(_stage, _gSkinFilter == "custom" ? "No custom skins yet." : _gSkinFilter == "mine" ? "You have not bought any skin yet." : "No skins.", "Bold", 24, White, gx + 20, Y + 90, 900, 32);
                Ui.Label(_stage, _gSkinFilter == "custom" ? "Custom skins arrive with the livery editor." : "Official skins are listed under All Skins.", "Regular", 17, Soft, gx + 20, Y + 128, 900, 26); return;
            }
            const float tw = 176, th = 238, gap = 12; var area = MakeScroll("veh_skins", gx, Y + 52, gw, H - 64, Mathf.Ceil(shown.Count * (tw + gap) / (gw - 10)) * (th + gap));
            int perRow = Mathf.Max(1, Mathf.FloorToInt((gw - 10 + gap) / (tw + gap)));
            for (int i = 0; i < shown.Count; i++)
            {
                var s = shown[i]; float x = 2 + (i % perRow) * (tw + gap), y = 2 + (i / perRow) * (th + gap);
                bool prev = _gPreview == s.id, eq = s.equipped; Color bc = eq ? Gold : prev ? Purple : new Color(1, 1, 1, 0.1f);
                Ui.Outlined(area.Content, "SkinTile", eq ? new Color(Gold.r, Gold.g, Gold.b, 0.08f) : CardFill, bc, x, y, tw, th, 14, eq || prev ? 2.4f : 1.2f);
                Color baseC = GarageRules.TryParseHex(s.base_color, out float r, out float g, out float b) ? new Color(r, g, b) : new Color(0.6f, 0.62f, 0.66f);
                Color acc = GarageRules.TryParseHex(s.accent_color, out r, out g, out b) ? new Color(r, g, b) : new Color(0.85f, 0.62f, 0.12f);
                Ui.Rounded(area.Content, "Swatch", baseC, x + 10, y + 10, tw - 20, 108, 10);
                Ui.Gradient(area.Content, "SwatchShade", new Color(1, 1, 1, 0.16f), new Color(0, 0, 0, 0.38f), false, x + 10, y + 10, tw - 20, 108);
                Ui.Rounded(area.Content, "SwatchStripe", acc, x + 10, y + 78, tw - 20, 12, 3);
                if (string.IsNullOrEmpty(s.base_color)) Ui.Label(area.Content, "Model's own livery", "SemiBold", 13, new Color(0.1f, 0.1f, 0.12f), x + 10, y + 38, tw - 20, 20, TextAnchor.UpperCenter);
                Ui.Label(area.Content, s.name, "Bold", 16, White, x + 10, y + 126, tw - 20, 22, TextAnchor.UpperCenter);
                string lab = GarageRules.SkinAction(s.state, s.price, s.unlock_level, s.need_more);
                Color lc = s.state == "equipped" ? Brand.Good : s.state == "owned" ? White : s.state == "available" ? Lime : s.state == "level_required" ? Brand.Bad : Gold;
                Ui.Label(area.Content, s.owned ? (eq ? "Equipped" : "Owned") : GarageRules.Naira(s.price), "SemiBold", 14, s.owned ? Brand.Good : Lime, x + 10, y + 152, tw - 20, 20, TextAnchor.UpperCenter);
                bool act = s.state == "owned" || s.state == "available";
                Ui.Rounded(area.Content, "SkinBtn", act ? Gold : new Color(0.16f, 0.18f, 0.23f), x + 12, y + th - 50, tw - 24, 38, 10);
                Ui.Label(area.Content, lab, "Bold", 15, act ? Ink : lc, x + 12, y + th - 42, tw - 24, 24, TextAnchor.UpperCenter);
                var sk = s; Ui.Click(area.Content, "Btn_Skin_" + s.id, x, y, tw, th - 56, () => PreviewSkin(sk));
                Ui.Click(area.Content, "Btn_SkinAct_" + s.id, x + 12, y + th - 50, tw - 24, 38, () => SkinAction(sk));
            }
            RestoreScroll(area);
        }

        void PreviewSkin(SkinItem s)
        {
            if (_show == null || _show.State != ShowroomState.Ready) { Toast("The showroom is not ready."); return; }
            _gPreview = s.equipped ? null : s.id; PaintShowroom(s); Debug.Log($"[Garage] preview skin={s.id} base={s.base_color}"); Refill("GarBottom", BuildGarBottom);
        }

        void SkinAction(SkinItem s)
        {
            if (_gBusy) return;
            switch (s.state)
            {
                case "equipped": Toast(s.name + " is already equipped."); break;
                case "owned": _ = EquipSkin(s); break;
                case "available": OpenSkinBuyDialog(s, null, false); break;
                case "level_required": Toast("Reach level " + s.unlock_level + " to unlock " + s.name + "."); break;
                default: Toast("You need " + GarageRules.Naira(s.need_more) + " more for " + s.name + "."); break;
            }
        }

        async Task EquipSkin(SkinItem s)
        {
            var v = GVeh(); if (v == null) return;
            _gBusy = true; _gBusyText = "Applying customization..."; Toast("Applying customization...", 2f);
            var r = await _svc.Garage.ApplySkin(v.id, s.id); if (this == null) return;
            Debug.Log($"[Garage] skin id={s.id} ok={r.Ok} error={r.ErrorCode}"); _gBusy = false;
            if (r.Ok) { _gPreview = null; Toast(s.name + " equipped.", 3f); await _svc.Garage.LoadGarage(); }
            else { Toast("Customization could not be applied. No changes were saved. " + (r.UserMessage ?? ""), 5f); _gPreview = null; }
            await LoadSkins(); _ = RefreshSnapshot();
        }

        void OpenSkinBuyDialog(SkinItem s, string error, bool processing)
        {
            const float W = 640, H = 400; float X = 960 - W / 2, Y = 540 - H / 2; long bal = _gSkins != null ? _gSkins.balance : 0;
            var m = OpenModal("Buy paint", W, H);
            Color baseC = GarageRules.TryParseHex(s.base_color, out float r, out float g, out float b) ? new Color(r, g, b) : Color.gray;
            Ui.Rounded(m, "BuySwatch", baseC, X + 28, Y + 80, 200, 150, 14); Ui.Gradient(m, "BuyShade", new Color(1, 1, 1, 0.16f), new Color(0, 0, 0, 0.4f), false, X + 28, Y + 80, 200, 150);
            Ui.Label(m, s.name, "ExtraBold", 30, White, X + 252, Y + 84, W - 280, 40); Ui.Label(m, GarageRules.Naira(s.price), "ExtraBold", 38, Lime, X + 252, Y + 128, W - 280, 48);
            Ui.Label(m, "Your balance", "Regular", 17, Muted, X + 252, Y + 190, 180, 24); Ui.Label(m, GarageRules.Naira(bal), "SemiBold", 17, White, X + 400, Y + 190, W - 428, 24, TextAnchor.UpperRight);
            Ui.Label(m, "After purchase", "Regular", 17, Muted, X + 252, Y + 218, 180, 24); Ui.Label(m, GarageRules.Naira(Math.Max(0, bal - s.price)), "SemiBold", 17, White, X + 400, Y + 218, W - 428, 24, TextAnchor.UpperRight);
            if (error != null) { Ui.Rounded(m, "ErrBg", new Color(Brand.Bad.r * 0.25f, Brand.Bad.g * 0.25f, Brand.Bad.b * 0.25f, 1f), X + 28, Y + 246, W - 56, 52, 12); Ui.Label(m, "Purchase failed. Your balance has not been changed.", "Bold", 16, Brand.Bad, X + 44, Y + 252, W - 88, 22); Ui.Label(m, error, "Regular", 15, Soft, X + 44, Y + 273, W - 88, 22); }
            else if (processing) Ui.Label(m, "Processing purchase...", "Bold", 20, Gold, X + 28, Y + 262, W - 56, 28, TextAnchor.UpperCenter);
            else Ui.Paragraph(m, "The paint is yours for every vehicle you own. The server checks your wallet and level again before anything is charged.", "Regular", 15, new Color(0.7f, 0.73f, 0.8f), X + 28, Y + 250, W - 56, 44);
            if (processing) return;
            ModalButton(m, "Btn_SkinBuyCancel", X + 28, Y + H - 66, 180, "Cancel", new Color(1, 1, 1, 0.1f), White, CloseModal);
            ModalButton(m, "Btn_SkinBuyConfirm", X + W - 28 - 280, Y + H - 66, 280, error != null ? "Try again" : "Confirm Purchase", Gold, Ink, () => _ = BuySkin(s));
        }

        async Task BuySkin(SkinItem s)
        {
            if (_gBusy) return; _gBusy = true; OpenSkinBuyDialog(s, null, true);
            var r = await _svc.Garage.BuySkin(s.id); if (this == null) return;
            Debug.Log($"[Garage] buy skin={s.id} ok={r.Ok} error={r.ErrorCode}"); _gBusy = false;
            if (r.Ok) { CloseModal(); Toast(s.name + " is yours. Press Equip to use it.", 4f); await LoadSkins(); _ = RefreshSnapshot(); }
            else { OpenSkinBuyDialog(s, r.UserMessage ?? "Please try again.", false); await LoadSkins(); if (_modal != null) OpenSkinBuyDialog(s, r.UserMessage ?? "Please try again.", false); }
        }

        // ---------------------------------------------------------------- maintenance
        void BuildMaintenance(float X, float Y, float W, float H, VehicleInfo v)
        {
            Ui.Outlined(_stage, "WorkPanel", PanelFill, PanelLine, X, Y, W, H, 18, 1.4f);
            Ui.Label(_stage, "Maintenance", "Bold", 26, White, X + 24, Y + 14, 400, 34);
            if (_gQuoteState == LoadState.Idle || _gQuoteState == LoadState.Loading) { Ui.Label(_stage, "Loading service prices...", "SemiBold", 20, Muted, X, Y + H / 2 - 16, W, 30, TextAnchor.UpperCenter); return; }
            if (_gQuoteState == LoadState.Unavailable)
            {
                Ui.Label(_stage, "Unable to load the service prices.", "Bold", 24, Brand.Bad, X + 40, Y + 80, 800, 32); Ui.Label(_stage, _gQuoteErr ?? "Please try again.", "Regular", 17, Soft, X + 40, Y + 116, 900, 26);
                Ui.Rounded(_stage, "RetryBtn", Gold, X + 40, Y + 160, 170, 44, 11); Ui.Label(_stage, "Retry", "Bold", 18, Ink, X + 40, Y + 169, 170, 26, TextAnchor.UpperCenter); Ui.Click(_stage, "Btn_RetryQuote", X + 40, Y + 160, 170, 44, () => _ = LoadQuote()); return;
            }
            var q = _gQuote; Color cc = VehicleRules.ConditionOf(q.condition_pct) == ConditionState.Ready ? Brand.Good : VehicleRules.ConditionOf(q.condition_pct) == ConditionState.MaintenanceRecommended ? Gold : Brand.Bad;
            float fp = q.fuel_capacity_l > 0 ? (float)(q.fuel_l / q.fuel_capacity_l * 100.0) : 0; Color fc = VehicleRules.FuelOf(fp) == FuelState.Healthy ? Blue : VehicleRules.FuelOf(fp) == FuelState.Low ? Gold : Brand.Bad;
            // state (left)
            Ui.Label(_stage, "Condition", "SemiBold", 18, Soft, X + 28, Y + 70, 160, 24); Ui.Bar(_stage, X + 190, Y + 78, 330, 10, (float)(q.condition_pct / 100.0), cc); Ui.Label(_stage, q.condition_pct.ToString("0") + "%  " + VehicleRules.ConditionText(q.condition_pct), "SemiBold", 16, White, X + 540, Y + 70, 380, 24);
            Ui.Label(_stage, "Fuel", "SemiBold", 18, Soft, X + 28, Y + 110, 160, 24); Ui.Bar(_stage, X + 190, Y + 118, 330, 10, fp / 100f, fc); Ui.Label(_stage, $"{q.fuel_l:0} / {q.fuel_capacity_l:0} L", "SemiBold", 16, White, X + 540, Y + 110, 380, 24);
            Ui.Label(_stage, "Your balance", "SemiBold", 18, Soft, X + 28, Y + 150, 160, 24); Ui.Label(_stage, GarageRules.Naira(q.balance), "Bold", 18, Lime, X + 190, Y + 150, 330, 24);
            Ui.Label(_stage, "Engine, tyre and brake condition are not tracked yet: the game keeps one condition value per vehicle.", "Regular", 15, Muted, X + 28, Y + 196, 900, 24);
            // actions (right)
            float ax = X + W - 560; bool canR = q.repair_needed && q.can_repair, canF = q.refuel_liters > 0.05 && q.can_refuel;
            MaintBtn(ax, Y + 64, "Btn_GarRepair", GarageRules.RepairLabel(q.repair_needed, q.repair_cost), canR, q.repair_needed && !q.can_repair ? "Not enough money" : null, () => _ = DoService(v, true));
            MaintBtn(ax, Y + 134, "Btn_GarRefuel", GarageRules.RefuelLabel(q.refuel_liters, q.refuel_cost), canF, q.refuel_liters > 0.05 && !q.can_refuel ? "Not enough money" : null, () => _ = DoService(v, false));
            MaintBtn(ax, Y + 204, "Btn_GarService", "Service", false, "There is no service system yet", () => Toast("There is no service system yet."));
            if (_gBusy) Ui.Label(_stage, _gBusyText, "Bold", 18, Gold, ax, Y + 280, 520, 26, TextAnchor.UpperCenter);
        }

        void MaintBtn(float x, float y, string name, string label, bool enabled, string note, Action go)
        {
            Ui.Rounded(_stage, "MaintBg", enabled ? Gold : new Color(0.16f, 0.18f, 0.23f), x, y, 520, 56, 12); Ui.Label(_stage, label, "Bold", 20, enabled ? Ink : new Color(0.78f, 0.8f, 0.85f), x, y + (note != null ? 6 : 14), 520, 28, TextAnchor.UpperCenter);
            if (note != null) Ui.Label(_stage, note, "Regular", 13, new Color(0.7f, 0.72f, 0.78f), x, y + 33, 520, 18, TextAnchor.UpperCenter);
            Ui.Click(_stage, name, x, y, 520, 56, () => { if (enabled && !_gBusy) go(); else if (!enabled) Toast(note ?? "Nothing to do."); });
        }

        async Task DoService(VehicleInfo v, bool repair)
        {
            _gBusy = true; _gBusyText = repair ? "Repairing..." : "Refuelling..."; Refill("GarBottom", BuildGarBottom);
            var r = repair ? await _svc.Garage.Repair(v.id) : await _svc.Garage.Refuel(v.id); if (this == null) return;
            Debug.Log($"[Garage] {(repair ? "repair" : "refuel")} ok={r.Ok} error={r.ErrorCode}"); _gBusy = false;
            Toast(r.Ok ? (repair ? "Repaired." : "Tank filled.") : (repair ? "Repair failed. " : "Refuel failed. ") + (r.UserMessage ?? "Try again."), 4f);
            await _svc.Garage.LoadGarage(); await LoadQuote(); _ = RefreshSnapshot(); if (this == null) return; RefreshGarage();
        }

        // ---------------------------------------------------------------- categories the game has no system for
        void BuildUnavailable(float X, float Y, float W, float H, string title, string[] names, string reason)
        {
            Ui.Outlined(_stage, "WorkPanel", PanelFill, PanelLine, X, Y, W, H, 18, 1.4f);
            Ui.Label(_stage, title, "Bold", 26, White, X + 24, Y + 14, 400, 34);
            Ui.Paragraph(_stage, reason, "Regular", 17, Soft, X + 24, Y + 54, W - 48, 50);
            float cw = 360, chh = 62, gx = 24, gy = 130; int perRow = Mathf.Max(1, Mathf.FloorToInt((W - 48 + 12) / (cw + 12)));
            for (int i = 0; i < names.Length; i++)
            {
                float x = X + gx + (i % perRow) * (cw + 12), y = Y + gy + (i / perRow) * (chh + 10);
                Ui.Outlined(_stage, "UnavCard", new Color(0.05f, 0.055f, 0.075f, 0.9f), new Color(1, 1, 1, 0.1f), x, y, cw, chh, 12, 1.1f);
                Ui.Icon(_stage, "ic_lock", new Color(1, 1, 1, 0.35f), x + 14, y + 18, 26); Ui.Label(_stage, names[i], "SemiBold", 18, new Color(1, 1, 1, 0.7f), x + 52, y + 8, cw - 64, 24); Ui.Label(_stage, "Not available yet", "Regular", 14, Muted, x + 52, y + 33, cw - 64, 20);
            }
        }
    }
}
