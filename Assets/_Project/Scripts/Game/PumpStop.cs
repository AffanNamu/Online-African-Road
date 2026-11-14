using System;
using ARO.Backend;
using ARO.NetCore;
using ARO.Vehicles;
using UnityEngine;
using UnityEngine.UI;

namespace ARO.Game
{
    /// <summary>
    /// P1 of docs/FUNCTIONAL_PLACES_PROPOSAL.md (approved by the owner on 2026-11-14): refuel at a pump. Stop beside a pump of a station and press F (gamepad B, or the HUD's Refuel button). The SERVER
    /// sells the litres (buy_fuel with the game's own fuel reading, which it believes only when it is lower than its own) and only then the pump runs: the nozzle leaves the pump, the hose reaches the
    /// filler, the meter and the tank count up to the level the server answered. With no server the game says so and fills nothing (docs/DECISIONS.md: no fake backend). Nothing here changes the
    /// vehicle's physics: the tank level is the one field the Garage refuel already sets.
    /// </summary>
    public class PumpStop : MonoBehaviour
    {
        DrivingSession _s; GameServices _svc; Material _hoseMat; VehicleController _hooked;
        /// <summary>The vehicle is stopped beside a pump.</summary>
        public bool AtPump { get; private set; }
        public ServicePoint Pump { get; private set; }
        public bool Filling => _filling;
        /// <summary>What the HUD shows about the pump ("" when not at one).</summary>
        public string Line { get; private set; } = "";

        double _price = -1; float _nextQuote, _next; bool _quoting, _buying, _filling;
        float _t0, _secs; double _from, _to, _litres; long _cost; VehicleController _fillVehicle;
        GameObject _rig; Transform[] _segs; Transform _nozzle; Text _meter; Vector3 _holster;
        const int Segs = 10; const float ReachSeconds = 0.8f;

        public void Init(DrivingSession s, GameServices svc, Material hose)
        {
            _s = s; _svc = svc; _hoseMat = hose;
            s.VehicleSpawned += Hook;
        }

        void Hook(VehicleController v)
        {
            if (_hooked != null) _hooked.RefuelRequested -= Request;
            _hooked = v; if (v != null) v.RefuelRequested += Request;
            EndFill(); AtPump = false; Line = "";
        }

        bool Online => _svc != null && _svc.Config != null && _svc.Config.IsConfigured && _svc.Api.IsSignedIn && !string.IsNullOrEmpty(_s.VehicleId) && _s.VehicleId != "smoke-truck";

        void Update()
        {
            var v = _s != null ? _s.Vehicle : null;
            if (v == null || !_s.Active) { if (AtPump || _filling) { EndFill(); AtPump = false; Line = ""; } return; }
            if (_filling) { TickFill(); return; }
            if (Time.unscaledTime >= _next)
            {
                _next = Time.unscaledTime + 0.2f;
                bool was = AtPump; AtPump = _s.Streamer != null && _s.Streamer.PumpAt(v.transform.position, v.SpeedKmh, out var p);
                if (AtPump)
                {
                    Pump = p;
                    if (!was) Debug.Log($"[Pump] at {p.Prop} {p.Name} ({ServicePoints.Label(p)}) fuel={v.fuelL:F1} of {v.definition.fuelCapacityL:F0} L");
                    if (_price < 0 && !_quoting && Online && Time.unscaledTime >= _nextQuote) Quote();
                }
            }
            if (!AtPump) { Line = ""; return; }
            double need = ServicePoints.LitresToFill(v.fuelL, v.definition.fuelCapacityL);
            Line = _buying ? ServicePoints.Label(Pump) + "  ·  Paying..." : ServicePoints.Prompt(ServicePoints.Label(Pump), need, ServicePoints.Cost(need, _price), _price > 0, "F");
        }

        /// <summary>The price per litre, from the same server quote the Garage uses (asked once per drive).</summary>
        async void Quote()
        {
            _quoting = true; var q = await _svc.Garage.Quote(_s.VehicleId); _quoting = false; if (this == null) return;
            if (q.Ok && q.Value != null && q.Value.fuel_price_per_l > 0) _price = q.Value.fuel_price_per_l; else _nextQuote = Time.unscaledTime + 20f;
        }

        /// <summary>F, gamepad B or the HUD button.</summary>
        public async void Request()
        {
            var v = _s != null ? _s.Vehicle : null;
            if (v == null || !_s.Active || _filling || _buying) return;
            if (!AtPump)
            {
                if (_s.Streamer != null && _s.Streamer.PumpAt(v.transform.position, 0f, out _)) _s.Say("Stop at the pump first.", 3f);
                return;
            }
            double need = ServicePoints.LitresToFill(v.fuelL, v.definition.fuelCapacityL);
            if (need < 0.5) { _s.Say("The tank is already full.", 3f); return; }
            if (!Online)
            {
                Debug.Log($"[Pump] offline: no server, so nothing is sold and the tank is not filled (fuel stays {v.fuelL:F1} L)");
                _s.Say("Offline: fuel is paid on the server and there is no connection. The tank was not filled.", 6f, Severity.Warning);
                return;
            }
            _buying = true; float reading = v.fuelL; var pump = Pump;
            Debug.Log($"[Pump] buying at {pump.Prop} {pump.Name}: the game reads {reading:F2} L of {v.definition.fuelCapacityL:F0}");
            var r = await _svc.Garage.RefuelAtPump(_s.VehicleId, reading);
            _buying = false; if (this == null) return;
            if (!r.Ok) { Debug.Log("[Pump] refused: " + r.ErrorCode); _s.Say(r.UserMessage ?? "Refuel failed. Try again.", 6f, Severity.Error); return; }
            var fp = r.Value; Debug.Log($"[Pump] paid {fp.cost} coins for {fp.liters:F2} L; the server's level after the pump {fp.fuel_l:F2} L");
            _ = _svc.RefreshWallet();
            if (_s.Vehicle != v) { v.fuelL = (float)fp.fuel_l; return; }          // the vehicle was replaced meanwhile: nothing to animate
            BeginFill(v, pump, fp);
        }

        // ---------------------------------------------------------------- the pump running
        void BeginFill(VehicleController v, ServicePoint p, FuelPurchase fp)
        {
            _fillVehicle = v; _from = v.fuelL; _to = Math.Max(v.fuelL, fp.fuel_l); _litres = fp.liters; _cost = fp.cost;
            _t0 = Time.unscaledTime; _secs = ServicePoints.FillSeconds(fp.liters) + ReachSeconds; _filling = true;
            BuildRig(p);
            TickFill();
        }

        void TickFill()
        {
            var v = _fillVehicle; if (v == null) { EndFill(); return; }
            float t = Time.unscaledTime - _t0, run = Mathf.Max(0f, t - ReachSeconds), runSecs = _secs - ReachSeconds;
            bool drove = v.SpeedKmh > 3f;
            double meter = ServicePoints.Meter(0.0, _litres, drove ? runSecs : run, runSecs);
            v.fuelL = (float)ServicePoints.Meter(_from, _to, drove ? runSecs : run, runSecs);
            Line = ServicePoints.FillLine(ServicePoints.Label(Pump), meter, _litres, _cost);
            UpdateRig(v, Mathf.Clamp01(t / ReachSeconds), meter);
            if (drove)
            {
                Debug.Log($"[Pump] driven off during the fill; the {_litres:F2} L paid for are in the tank ({v.fuelL:F2} L)");
                _s.Say($"You drove off the pump. The {_litres:0.0} L you paid for ({_cost:N0} coins) are in the tank.", 5f, Severity.Warning);
                EndFill(); return;
            }
            if (t >= _secs)
            {
                Debug.Log($"[Pump] filled {_litres:F2} L for {_cost} coins; tank {v.fuelL:F2} L");
                _s.Say($"Tank filled: {_litres:0.0} L for {_cost:N0} coins.", 5f, Severity.Success);
                EndFill();
            }
        }

        void EndFill()
        {
            if (_rig != null) Destroy(_rig);
            _rig = null; _segs = null; _nozzle = null; _meter = null; _filling = false; _fillVehicle = null;
        }

        /// <summary>The hose (a sagging chain of thin cylinders), the nozzle and the pump's meter above the dispenser.</summary>
        void BuildRig(ServicePoint p)
        {
            EndFillVisualOnly();
            float r = p.YawDeg * Mathf.Deg2Rad; var toIsland = new Vector3(Mathf.Sin(r), 0f, Mathf.Cos(r));
            _holster = new Vector3(p.Pos.X, p.Pos.Y, p.Pos.Z) + toIsland * 0.25f + Vector3.up * 1.25f;
            _rig = new GameObject("PumpRig");
            _segs = new Transform[Segs];
            for (int i = 0; i < Segs; i++) _segs[i] = Piece(PrimitiveType.Cylinder, "Hose" + i).transform;
            _nozzle = Piece(PrimitiveType.Cube, "Nozzle").transform; _nozzle.localScale = new Vector3(0.09f, 0.09f, 0.32f);
            var cgo = new GameObject("Meter", typeof(RectTransform), typeof(Canvas)); cgo.transform.SetParent(_rig.transform, false);
            cgo.GetComponent<Canvas>().renderMode = RenderMode.WorldSpace;
            var crt = (RectTransform)cgo.transform; crt.sizeDelta = new Vector2(160f, 70f); crt.localScale = Vector3.one * 0.01f; crt.position = _holster + Vector3.up * 1.15f;
            var bg = new GameObject("Back", typeof(RectTransform), typeof(Image)); bg.transform.SetParent(cgo.transform, false);
            var brt = (RectTransform)bg.transform; brt.anchorMin = Vector2.zero; brt.anchorMax = Vector2.one; brt.offsetMin = brt.offsetMax = Vector2.zero; bg.GetComponent<Image>().color = new Color(0.02f, 0.05f, 0.03f, 0.92f);
            var tgo = new GameObject("Text", typeof(RectTransform), typeof(Text)); tgo.transform.SetParent(cgo.transform, false);
            var trt = (RectTransform)tgo.transform; trt.anchorMin = Vector2.zero; trt.anchorMax = Vector2.one; trt.offsetMin = trt.offsetMax = Vector2.zero;
            _meter = tgo.GetComponent<Text>(); _meter.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"); _meter.alignment = TextAnchor.MiddleCenter; _meter.color = new Color(0.45f, 1f, 0.55f);
            _meter.fontStyle = FontStyle.Bold; _meter.resizeTextForBestFit = true; _meter.resizeTextMinSize = 8; _meter.resizeTextMaxSize = 30; _meter.raycastTarget = false;
        }

        void EndFillVisualOnly() { if (_rig != null) Destroy(_rig); _rig = null; }

        GameObject Piece(PrimitiveType type, string name)
        {
            var g = GameObject.CreatePrimitive(type); g.name = name; Destroy(g.GetComponent<Collider>()); g.transform.SetParent(_rig.transform, false);
            var mr = g.GetComponent<MeshRenderer>(); if (_hoseMat != null) mr.sharedMaterial = _hoseMat; mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return g;
        }

        void UpdateRig(VehicleController v, float reach, double meter)
        {
            if (_rig == null || _segs == null) return;
            var vt = v.transform; var side = Vector3.Dot(_holster - vt.position, vt.right) >= 0f ? 1f : -1f;
            var filler = vt.position + vt.right * side * 1.15f + Vector3.up * 0.55f - vt.forward * 0.6f;               // the tank cap on the pump side of the vehicle, behind the cab
            var tip = Vector3.Lerp(_holster, filler, Mathf.SmoothStep(0f, 1f, reach));
            var mid = (_holster + tip) * 0.5f + Vector3.down * (0.35f + 0.25f * Vector3.Distance(_holster, tip) / 3f);   // the hose sags between the pump and the nozzle
            Vector3 Curve(float u) => (1 - u) * (1 - u) * _holster + 2 * (1 - u) * u * mid + u * u * tip;
            for (int i = 0; i < Segs; i++)
            {
                Vector3 a = Curve(i / (float)Segs), b = Curve((i + 1) / (float)Segs), d = b - a; float len = Mathf.Max(0.01f, d.magnitude);
                var s = _segs[i]; s.position = (a + b) * 0.5f; s.rotation = Quaternion.FromToRotation(Vector3.up, d / len); s.localScale = new Vector3(0.05f, len * 0.5f, 0.05f);
            }
            var into = (filler - _holster); into.y = 0f; _nozzle.position = tip; _nozzle.rotation = into.sqrMagnitude > 1e-4f ? Quaternion.LookRotation(into.normalized) : Quaternion.identity;
            if (_meter != null)
            {
                _meter.text = meter.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + " L\n" + Math.Round(_cost * (_litres > 0 ? Math.Min(1.0, meter / _litres) : 1.0)).ToString("N0", System.Globalization.CultureInfo.InvariantCulture);
                var cam = Camera.main; if (cam != null) _meter.transform.parent.rotation = Quaternion.LookRotation(_meter.transform.parent.position - cam.transform.position);
            }
        }

        void OnDestroy() { if (_hooked != null) _hooked.RefuelRequested -= Request; if (_s != null) _s.VehicleSpawned -= Hook; }
    }
}
