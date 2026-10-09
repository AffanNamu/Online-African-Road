using UnityEngine;
using UnityEngine.UI;

namespace ARO.Game
{
    /// <summary>In-drive HUD: speed, gear, fuel, damage, job, distance, direction arrow, prompts.</summary>
    public class Hud : MonoBehaviour
    {
        DrivingSession _s; GameServices _svc;
        Text _speed, _gear, _job, _msg, _wallet, _status;
        RectTransform _fuelBar, _dmgBar, _arrow; GameObject _root;

        public void Init(DrivingSession s, GameServices svc)
        {
            _s = s; _svc = svc;
            var canvas = UIKit.CreateCanvas("HUD", 5);
            _root = canvas.gameObject; transform.SetParent(_root.transform, false);
            var t = canvas.transform;
            var left = UIKit.Box(t, "Gauges", UIKit.Panel, new Vector2(0, 0), new Vector2(0, 0), new Vector2(30, 30), new Vector2(430, 220));
            _speed = UIKit.Label(left, "0", 84, UIKit.TextCol, TextAnchor.MiddleLeft, new Vector2(0, 0.35f), new Vector2(0.62f, 1), new Vector2(24, 0), Vector2.zero);
            UIKit.Label(left, "km/h", 24, UIKit.Muted, TextAnchor.LowerLeft, new Vector2(0.45f, 0.45f), new Vector2(0.75f, 0.7f), Vector2.zero, Vector2.zero);
            _gear = UIKit.Label(left, "D1", 48, UIKit.Accent, TextAnchor.MiddleRight, new Vector2(0.6f, 0.45f), new Vector2(1, 1), Vector2.zero, new Vector2(-24, 0));
            UIKit.Label(left, "FUEL", 18, UIKit.Muted, TextAnchor.MiddleLeft, new Vector2(0, 0.2f), new Vector2(0.2f, 0.35f), new Vector2(24, 0), Vector2.zero);
            _fuelBar = Bar(left, new Vector2(0.2f, 0.22f), new Vector2(0.95f, 0.32f), UIKit.Good);
            UIKit.Label(left, "DMG", 18, UIKit.Muted, TextAnchor.MiddleLeft, new Vector2(0, 0.05f), new Vector2(0.2f, 0.2f), new Vector2(24, 0), Vector2.zero);
            _dmgBar = Bar(left, new Vector2(0.2f, 0.07f), new Vector2(0.95f, 0.17f), UIKit.Bad);

            var top = UIKit.Box(t, "Job", UIKit.Panel, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(-380, -150), new Vector2(380, -24));
            _job = UIKit.Label(top, "", 26, UIKit.TextCol, TextAnchor.MiddleLeft, Vector2.zero, Vector2.one, new Vector2(24, 0), new Vector2(-110, 0));
            var arrowBox = UIKit.Box(top, "Arrow", new Color(0, 0, 0, 0), new Vector2(1, 0.5f), new Vector2(1, 0.5f), new Vector2(-96, -40), new Vector2(-16, 40));
            _arrow = UIKit.Box(arrowBox, "ArrowImg", UIKit.Accent, new Vector2(0.4f, 0.1f), new Vector2(0.6f, 0.95f), Vector2.zero, Vector2.zero);

            var wal = UIKit.Box(t, "Wallet", UIKit.Panel, new Vector2(1, 1), new Vector2(1, 1), new Vector2(-330, -80), new Vector2(-30, -24));
            _wallet = UIKit.Label(wal, "", 26, UIKit.Accent, TextAnchor.MiddleCenter, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);

            _msg = UIKit.Label(t, "", 30, UIKit.TextCol, TextAnchor.MiddleCenter, new Vector2(0.2f, 0.12f), new Vector2(0.8f, 0.24f), Vector2.zero, Vector2.zero);
            _status = UIKit.Label(t, "[W/S] drive  [A/D] steer  [Space] handbrake  [L] lights  [H] horn  [C] camera  [Esc] menu", 18, UIKit.Muted,
                TextAnchor.LowerCenter, new Vector2(0, 0), new Vector2(1, 0.05f), Vector2.zero, Vector2.zero);
            SetVisible(false);
        }

        static RectTransform Bar(Transform parent, Vector2 a, Vector2 b, Color c)
        {
            UIKit.Box(parent, "BarBg", new Color(1, 1, 1, 0.08f), a, b, Vector2.zero, Vector2.zero);
            return UIKit.Box(parent, "BarFill", c, a, b, Vector2.zero, Vector2.zero);
        }

        public void SetVisible(bool v) { if (_root) _root.SetActive(v); }

        void Update()
        {
            if (_s == null || _s.Vehicle == null || !_s.Active) return;
            var v = _s.Vehicle; var def = v.definition;
            _speed.text = Mathf.RoundToInt(v.SpeedKmh).ToString();
            _gear.text = v.CurrentGear < 0 ? "R" : "D" + v.CurrentGear;
            _fuelBar.anchorMax = new Vector2(Mathf.Lerp(0.2f, 0.95f, Mathf.Clamp01(v.fuelL / def.fuelCapacityL)), 0.32f);
            _dmgBar.anchorMax = new Vector2(Mathf.Lerp(0.2f, 0.95f, Mathf.Clamp01(v.damagePct / 100f)), 0.17f);
            if (_svc.Wallet != null) _wallet.text = _svc.Wallet.balance.ToString("N0") + " coins";

            if (_s.Job == null) { _job.text = "No active job. Open the Job Board from the menu (Esc)."; _arrow.parent.gameObject.SetActive(false); }
            else
            {
                _arrow.parent.gameObject.SetActive(true);
                string where = _s.Phase == JobPhase.ToDestination ? "DELIVER TO " + _s.Job.destination.name : "PICK UP AT " + _s.Job.origin.name;
                _job.text = $"{_s.Job.code} · {_s.Job.cargo_type}\n{where} · {(_s.DistanceToTarget / 1000f):F1} km · {_s.Job.reward:N0} coins";
                ((RectTransform)_arrow.parent).localRotation = Quaternion.Euler(0, 0, -_s.BearingToTarget());
            }
            _msg.text = Time.unscaledTime < _s.MessageUntil ? _s.Message : "";
        }
    }
}
