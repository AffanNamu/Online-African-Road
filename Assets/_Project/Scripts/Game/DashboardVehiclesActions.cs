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
    /// <summary>Trucks &amp; Buses: the actions (select, buy, compare). Each is a server call; what the page shows afterwards is read back from the server.</summary>
    public sealed partial class DashboardScreen
    {
        enum BuyPhase { Confirm, Processing, Failed }
        readonly List<string> _cmpIds = new List<string>();

        void OnPrimary(VehicleRow r)
        {
            if (_vehBusy) return;
            var a = VehicleRules.ActionFor(r, IsActive(r));
            switch (a.Kind)
            {
                case VehicleActionKind.Select: _ = SelectActive(r); break;
                case VehicleActionKind.Buy: OpenBuyDialog(r, BuyPhase.Confirm, null); break;
                case VehicleActionKind.Selected: Toast(r.Name + " is your active vehicle."); break;
                case VehicleActionKind.ComingSoon: Toast(r.Name + " has not been released yet."); break;
                case VehicleActionKind.LevelRequired: Toast("Reach level " + r.UnlockLevel + " to unlock " + r.Name + "."); break;
                case VehicleActionKind.NeedFunds: Toast("You need " + VehicleRules.Naira(r.NeedMore) + " more."); break;
                default: Toast(r.Name + " is not for sale."); break;
            }
        }

        // ================================================================================================================ select
        async Task SelectActive(VehicleRow r)
        {
            var mine = MyVehicle(r.Id);
            if (mine == null) { Toast("Vehicle selection failed. Please try again."); return; }
            _vehBusy = true; _vehBusyText = "Saving selection..."; Refill("VehShow", BuildVehShow);
            var res = await _svc.Garage.SetActive(mine.id);
            if (this == null) return;
            Debug.Log($"[Vehicles] select id={r.Id} ok={res.Ok} error={res.ErrorCode}");
            _vehBusy = false;
            if (res.Ok) { Toast(r.Name + " is now your active vehicle."); }
            else Toast("Vehicle selection failed. " + (res.UserMessage ?? "Please try again."), 5f);
            await _svc.Garage.LoadGarage();
            _ = RefreshSnapshot();                          // the header's truck card and GameFlow's vehicle list follow the server
            if (this == null) return;
            Refill("VehShow", BuildVehShow); Refill("VehCards", BuildVehCards);
        }

        // ================================================================================================================ buy
        void OpenBuyDialog(VehicleRow r, BuyPhase phase, string error)
        {
            var list = _svc.Garage.Catalog.Value; long balance = list != null ? list.balance : 0;
            const float W = 700, H = 440; float X = 960 - W / 2, Y = 540 - H / 2;
            var m = OpenModal("Confirm Purchase", W, H);
            var tex = Brand.Texture("veh_" + r.PictureKey);
            if (tex != null) Ui.Photo(m, "BuyPhoto", tex, new Rect(0, 0, 1, 1), X + 28, Y + 78, 238, 188, 14);
            Ui.Label(m, r.Manufacturer.ToUpperInvariant(), "Bold", 14, Gold, X + 290, Y + 82, W - 320, 20);
            var nm = Ui.Label(m, r.Name, "ExtraBold", 30, White, X + 290, Y + 102, W - 320, 40); nm.resizeTextForBestFit = true; nm.resizeTextMinSize = 18; nm.resizeTextMaxSize = 30; nm.horizontalOverflow = HorizontalWrapMode.Wrap;
            Ui.Label(m, r.VehicleClass, "SemiBold", 17, Soft, X + 290, Y + 146, W - 320, 24);
            Ui.Label(m, VehicleRules.Naira(r.Price), "ExtraBold", 40, Lime, X + 290, Y + 182, W - 320, 52);
            Ui.Label(m, "Your balance", "Regular", 17, Muted, X + 290, Y + 240, 180, 24); Ui.Label(m, VehicleRules.Naira(balance), "SemiBold", 17, White, X + 450, Y + 240, W - 480, 24, TextAnchor.UpperRight);
            Ui.Label(m, "After purchase", "Regular", 17, Muted, X + 290, Y + 268, 180, 24); Ui.Label(m, VehicleRules.Naira(Math.Max(0, balance - r.Price)), "SemiBold", 17, White, X + 450, Y + 268, W - 480, 24, TextAnchor.UpperRight);
            var note = Ui.Paragraph(m, "The server checks your wallet, your level and the vehicle again before anything is charged. You can own one of each model.", "Regular", 15, new Color(0.7f, 0.73f, 0.8f), X + 28, Y + 286, W - 56, 44);
            if (phase == BuyPhase.Failed)
            {
                Ui.Rounded(m, "ErrBg", new Color(Brand.Bad.r * 0.25f, Brand.Bad.g * 0.25f, Brand.Bad.b * 0.25f, 1f), X + 28, Y + 326, W - 56, 52, 12);
                Ui.Label(m, "Purchase failed. Your balance has not been changed.", "Bold", 16, Brand.Bad, X + 44, Y + 332, W - 88, 22);
                Ui.Label(m, error ?? "Please try again.", "Regular", 15, Soft, X + 44, Y + 353, W - 88, 22);
            }
            else if (phase == BuyPhase.Processing) Ui.Label(m, "Processing purchase...", "Bold", 20, Gold, X + 28, Y + 340, W - 56, 28, TextAnchor.UpperCenter);
            float by = Y + H - 66;
            if (phase == BuyPhase.Processing) return;
            ModalButton(m, "Btn_BuyCancel", X + 28, by, 200, "Cancel", new Color(1, 1, 1, 0.1f), White, CloseModal);
            ModalButton(m, "Btn_BuyConfirm", X + W - 28 - 300, by, 300, phase == BuyPhase.Failed ? "Try again" : "Confirm Purchase", Gold, Ink, () => _ = PurchaseAsync(r));
        }

        async Task PurchaseAsync(VehicleRow r)
        {
            if (_vehBusy) return;
            _vehBusy = true; _vehBusyText = "Processing purchase..."; OpenBuyDialog(r, BuyPhase.Processing, null); Refill("VehShow", BuildVehShow);
            var res = await _svc.Bus.BuyVehicle(r.Id);
            if (this == null) return;
            Debug.Log($"[Vehicles] buy id={r.Id} ok={res.Ok} error={res.ErrorCode}");
            _vehBusy = false;
            if (res.Ok)
            {
                CloseModal(); Toast(r.Name + " is yours.", 4f);
                await LoadVehicles(true); _ = RefreshSnapshot();                   // wallet, ownership, state of every card: all read back from the server
            }
            else
            {
                OpenBuyDialog(r, BuyPhase.Failed, res.UserMessage);
                await LoadVehicles(true);                                          // the failure may mean the catalogue changed under us (level, price, availability)
                if (_modal != null) OpenBuyDialog(r, BuyPhase.Failed, res.UserMessage);
            }
        }

        // ================================================================================================================ compare
        void OpenCompare()
        {
            if (_vehRows.Count < 2) { Toast("There are not enough vehicles to compare yet."); return; }
            if (_cmpIds.Count == 0) { _cmpIds.Add(_vehSel ?? _vehRows[0].Id); var other = _vehRows.FirstOrDefault(v => !_cmpIds.Contains(v.Id) && v.Category != SelRow()?.Category) ?? _vehRows.First(v => !_cmpIds.Contains(v.Id)); _cmpIds.Add(other.Id); }
            ShowCompare();
        }

        void ShowCompare()
        {
            _cmpIds.RemoveAll(id => _vehRows.All(v => v.Id != id));
            const float W = 1560, H = 820; float X = 960 - W / 2, Y = 540 - H / 2;
            var m = OpenModal("Compare Vehicles", W, H);
            // the picker: one catalogue, 2 to 4 ticked
            Ui.Label(m, $"Choose 2 to 4 vehicles ({_cmpIds.Count} selected)", "SemiBold", 17, Soft, X + 28, Y + 70, 340, 24);
            float listH = H - 130; var area = new ScrollArea { Name = "veh_cmp_list", Height = listH, ContentH = _vehRows.Count * 46 + 6, X = X + 24, Y = Y + 100 };
            area.View = Ui.Box(m, "CmpListView", X + 24, Y + 100, 350, listH); area.View.gameObject.AddComponent<RectMask2D>(); area.Content = Ui.Box(area.View, "Content", 0, 0, 350, area.ContentH);
            area.Thumb = Ui.Rounded(m, "CmpThumb", new Color(1, 1, 1, 0.3f), X + 24 + 346, Y + 100, 4, 40, 2); _areas.Add(area);
            for (int i = 0; i < _vehRows.Count; i++)
            {
                var r = _vehRows[i]; bool on = _cmpIds.Contains(r.Id); float y = 2 + i * 46;
                if (on) Ui.Rounded(area.Content, "PickOn", new Color(Gold.r, Gold.g, Gold.b, 0.2f), 2, y, 336, 40, 10); else Ui.Outlined(area.Content, "Pick", new Color(0.05f, 0.055f, 0.075f, 0.9f), new Color(1, 1, 1, 0.12f), 2, y, 336, 40, 10, 1.2f);
                Ui.Rounded(area.Content, "Box", on ? Gold : new Color(1, 1, 1, 0.12f), 12, y + 10, 20, 20, 5); if (on) Ui.Label(area.Content, "✓", "Bold", 16, Ink, 12, y + 10, 20, 20, TextAnchor.UpperCenter);
                var nm = Ui.Label(area.Content, r.Name, on ? "Bold" : "SemiBold", 16, White, 44, y + 6, 290, 28); nm.resizeTextForBestFit = true; nm.resizeTextMinSize = 11; nm.resizeTextMaxSize = 16;
                var vr = r; Ui.Click(area.Content, "Btn_CmpPick_" + r.Id, 2, y, 336, 40, () =>
                {
                    if (_cmpIds.Contains(vr.Id)) _cmpIds.Remove(vr.Id);
                    else if (_cmpIds.Count >= VehicleRules.MaxCompare) { Toast("You can compare up to " + VehicleRules.MaxCompare + " vehicles."); return; }
                    else _cmpIds.Add(vr.Id);
                    ShowCompare();
                });
            }
            RestoreScroll(area);

            // the table
            var sel = _cmpIds.Select(id => _vehRows.First(v => v.Id == id)).ToList();
            var rows = VehicleRules.Compare(sel, out string err);
            float tx = X + 400, tw = W - 400 - 28; int n = Mathf.Max(sel.Count, 1); float lab = 200, col = (tw - lab) / n;
            if (err != null) { Ui.Label(m, err, "SemiBold", 22, Gold, tx, Y + 200, tw, 34); return; }
            Debug.Log($"[Vehicles] compare open n={sel.Count} rows={rows.Count}");
            for (int i = 0; i < n; i++)
            {
                var r = sel[i]; float cx = tx + lab + i * col;
                var tex = Brand.Texture("veh_" + r.PictureKey);
                Ui.Outlined(m, "CmpHead", CardFill, new Color(1, 1, 1, 0.12f), cx + 6, Y + 66, col - 12, 168, 14, 1.2f);
                if (tex != null) Ui.Photo(m, "CmpPhoto", tex, Ui.Cover(tex, 0f, 0f, 1f, (col - 24f) / 104f), cx + 12, Y + 72, col - 24, 104, 10);
                var nm = Ui.Label(m, r.Name, "Bold", 17, White, cx + 14, Y + 182, col - 28, 24, TextAnchor.UpperCenter); nm.resizeTextForBestFit = true; nm.resizeTextMinSize = 11; nm.resizeTextMaxSize = 17;
                Ui.Label(m, string.IsNullOrEmpty(r.Drive) ? r.VehicleClass : r.Drive + " " + r.VehicleClass, "Regular", 13, Muted, cx + 14, Y + 208, col - 28, 20, TextAnchor.UpperCenter);
            }
            float ry = Y + 250; const float rh = 46;
            for (int k = 0; k < rows.Count; k++)
            {
                if (k % 2 == 0) Ui.Rounded(m, "RowBand", new Color(1, 1, 1, 0.04f), tx, ry, tw, rh - 2, 8);
                Ui.Label(m, rows[k].Label, "SemiBold", 17, Soft, tx + 14, ry + 10, lab - 20, 26);
                for (int i = 0; i < n; i++)
                {
                    bool best = rows[k].Best.Contains(i); float cx = tx + lab + i * col;
                    if (best) Ui.Rounded(m, "BestBg", new Color(Gold.r, Gold.g, Gold.b, 0.18f), cx + 8, ry + 2, col - 16, rh - 6, 8);
                    string cell = rows[k].Cells[i]; bool na = cell == "n/a";
                    Ui.Label(m, cell, best ? "Bold" : "SemiBold", 18, best ? Gold : na ? new Color(1, 1, 1, 0.38f) : White, cx, ry + 10, col, 26, TextAnchor.UpperCenter);
                }
                ry += rh;
            }
            Ui.Label(m, "Highlighted = the better value. Acceleration, handling and licence are not part of the vehicle data yet, so they are not shown.", "Regular", 14, Muted, tx, Y + H - 40, tw, 22);
        }
    }
}
