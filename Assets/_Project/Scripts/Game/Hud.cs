using ARO.Multiplayer;
using ARO.NetCore;
using UnityEngine;
using UnityEngine.UI;

namespace ARO.Game
{
    /// <summary>
    /// In-drive HUD (dark glass): speed dial, vehicle status, job card, minimap + ETA, convoy list, toasts.
    /// Deliberately compact so the road stays visible; every number comes from tested formatting/gauge code.
    /// </summary>
    public class Hud : MonoBehaviour
    {
        const float MaxDialKmh = 140f;
        static readonly Color Glass = new Color(0.05f, 0.07f, 0.09f, 0.72f);

        DrivingSession _s; GameServices _svc; MinimapView _map;
        /// <summary>Optional: when a bus run is active the job card shows the route instead.</summary>
        public BusSession Bus;
        Text _jobTitle;
        Text _speed, _gear, _job1, _job2, _jobReward, _eta, _wallet, _convoy, _indicators, _prompt;
        Image _arc, _fuelFill, _dmgFill, _progress; RectTransform _needle; GameObject _root, _jobCard, _convoyPanel;
        Text[] _toast = new Text[3]; Image[] _toastBg = new Image[3];
        float _smoothSpeed;

        public void Init(DrivingSession s, GameServices svc)
        {
            _s = s; _svc = svc;
            var canvas = UIKit.CreateCanvas("HUD", 5); _root = canvas.gameObject; transform.SetParent(_root.transform, false);
            var t = canvas.transform;
            BuildDial(t); BuildJobCard(t); BuildMinimap(t); BuildConvoy(t); BuildToasts(t);

            // Contextual prompt (centre-bottom), e.g. "Stop to load cargo".
            _prompt = UIKit.Label(t, "", 30, UIKit.TextCol, TextAnchor.MiddleCenter, new Vector2(0.25f, 0.16f), new Vector2(0.75f, 0.24f), Vector2.zero, Vector2.zero);
            UIKit.Label(t, "W/S drive   A/D steer   Space brake   L lights   Q/E indicators   H horn   C camera   Esc menu", 16,
                new Color(1, 1, 1, 0.35f), TextAnchor.LowerCenter, new Vector2(0, 0), new Vector2(1, 0.035f), Vector2.zero, Vector2.zero);
            SetVisible(false);
        }

        // ---------------------------------------------------------------- layout
        void BuildDial(Transform t)
        {
            var panel = UIGfx.Panel(t, "DialPanel", Glass, new Vector2(0, 0), new Vector2(0, 0), new Vector2(28, 28), new Vector2(560, 272)).transform;
            // Zero-size node at the panel's bottom-left: children use (0,0) anchors, so anchoredPosition is measured from there.
            var originGo = new GameObject("Origin", typeof(RectTransform)); originGo.transform.SetParent(panel, false);
            var originRt = (RectTransform)originGo.transform; originRt.anchorMin = originRt.anchorMax = Vector2.zero; originRt.pivot = Vector2.zero; originRt.anchoredPosition = Vector2.zero; originRt.sizeDelta = Vector2.zero;
            var dial = originGo.transform;
            const float size = 214f; var c = new Vector2(122, 122);
            var back = UIGfx.Shape(dial, "Back", UIGfx.Ring, new Color(1, 1, 1, 0.10f), c, size, Vector2.zero); back.type = Image.Type.Filled; Arc(back, 0.75f);
            _arc = UIGfx.Shape(dial, "Arc", UIGfx.Ring, UIKit.Accent, c, size, Vector2.zero); _arc.type = Image.Type.Filled; Arc(_arc, 0f);
            // tick marks every 20 km/h
            for (int v = 0; v <= (int)MaxDialKmh; v += 20)
            {
                float a = Gauge.Angle(v, 0, MaxDialKmh, 135f, -135f);
                var tick = UIGfx.Panel(dial, "Tick", new Color(1, 1, 1, 0.45f), Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero);
                var rt = (RectTransform)tick.transform; rt.anchorMin = rt.anchorMax = new Vector2(0, 0); rt.pivot = new Vector2(0.5f, 0.5f);
                rt.sizeDelta = new Vector2(3f, 11f);
                float r = size * 0.5f - 24f; float rad = (a + 90f) * Mathf.Deg2Rad;
                rt.anchoredPosition = c + new Vector2(Mathf.Cos(rad), Mathf.Sin(rad)) * r; rt.localRotation = Quaternion.Euler(0, 0, a);
            }
            var needle = UIGfx.Panel(dial, "Needle", UIKit.TextCol, Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero, pill: true);
            _needle = (RectTransform)needle.transform; _needle.anchorMin = _needle.anchorMax = Vector2.zero; _needle.pivot = new Vector2(0.5f, 0f);
            _needle.sizeDelta = new Vector2(5f, 78f); _needle.anchoredPosition = c;
            UIGfx.Shape(dial, "Hub", UIGfx.Disc, new Color(0.1f, 0.12f, 0.15f, 1f), c, 26f, Vector2.zero);

            _speed = Lbl(dial, "0", 62, UIKit.TextCol, TextAnchor.MiddleCenter, c + new Vector2(0, -52), new Vector2(130, 70));
            Lbl(dial, "km/h", 20, UIKit.Muted, TextAnchor.MiddleCenter, c + new Vector2(0, -92), new Vector2(100, 24));
            _gear = Lbl(dial, "D1", 26, UIKit.Accent, TextAnchor.MiddleCenter, c + new Vector2(0, 38), new Vector2(80, 30));

            // status column
            Lbl(dial, "FUEL", 17, UIKit.Muted, TextAnchor.MiddleLeft, new Vector2(262, 196), new Vector2(120, 22), true);
            _fuelFill = Bar(dial, new Vector2(262, 172), 260f, UIKit.Good);
            Lbl(dial, "CONDITION", 17, UIKit.Muted, TextAnchor.MiddleLeft, new Vector2(262, 128), new Vector2(160, 22), true);
            _dmgFill = Bar(dial, new Vector2(262, 104), 260f, UIKit.Good);
            _indicators = Lbl(dial, "", 20, UIKit.TextCol, TextAnchor.MiddleLeft, new Vector2(262, 52), new Vector2(260, 30), true);
        }

        static void Arc(Image img, float fill)
        {
            img.fillMethod = Image.FillMethod.Radial360; img.fillOrigin = (int)Image.Origin360.Top; img.fillClockwise = true;
            img.fillAmount = fill; img.rectTransform.localRotation = Quaternion.Euler(0, 0, 135f);   // arc starts bottom-left, sweeps 270 deg clockwise
        }

        void BuildJobCard(Transform t)
        {
            var card = UIGfx.Panel(t, "JobCard", Glass, new Vector2(0, 1), new Vector2(0, 1), new Vector2(28, -214), new Vector2(520, -28));
            _jobCard = card.gameObject; var c = card.transform;
            _jobTitle = UIKit.Label(c, "CURRENT JOB", 16, UIKit.Accent, TextAnchor.UpperLeft, new Vector2(0, 1), new Vector2(1, 1), new Vector2(22, -34), new Vector2(-22, -10));
            _job1 = UIKit.Label(c, "", 26, UIKit.TextCol, TextAnchor.UpperLeft, new Vector2(0, 1), new Vector2(1, 1), new Vector2(22, -78), new Vector2(-22, -36));
            _job2 = UIKit.Label(c, "", 20, UIKit.Muted, TextAnchor.UpperLeft, new Vector2(0, 1), new Vector2(1, 1), new Vector2(22, -112), new Vector2(-22, -80));
            _jobReward = UIKit.Label(c, "", 22, UIKit.Accent, TextAnchor.UpperLeft, new Vector2(0, 1), new Vector2(1, 1), new Vector2(22, -146), new Vector2(-22, -114));
            UIGfx.Panel(c, "ProgBack", new Color(1, 1, 1, 0.10f), new Vector2(0, 0), new Vector2(1, 0), new Vector2(22, 16), new Vector2(-22, 24), true);
            _progress = UIGfx.Panel(c, "Prog", UIKit.Accent, new Vector2(0, 0), new Vector2(1, 0), new Vector2(22, 16), new Vector2(-22, 24), true);
            _progress.type = Image.Type.Sliced;
        }

        void BuildMinimap(Transform t)
        {
            var holder = new GameObject("MinimapHolder", typeof(RectTransform)); holder.transform.SetParent(t, false);
            var rt = (RectTransform)holder.transform; rt.anchorMin = rt.anchorMax = new Vector2(1, 1); rt.pivot = new Vector2(1, 1);
            rt.anchoredPosition = new Vector2(-28, -28); rt.sizeDelta = new Vector2(300, 300);
            _map = gameObject.AddComponent<MinimapView>(); _map.Build(holder.transform, Vector2.zero, 240f);
            var chip = UIGfx.Panel(t, "EtaChip", Glass, new Vector2(1, 1), new Vector2(1, 1), new Vector2(-298, -352), new Vector2(-28, -300), true);
            _eta = UIKit.Label(chip.transform, "", 22, UIKit.TextCol, TextAnchor.MiddleCenter, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            var wal = UIGfx.Panel(t, "Wallet", Glass, new Vector2(1, 1), new Vector2(1, 1), new Vector2(-298, -410), new Vector2(-28, -364), true);
            _wallet = UIKit.Label(wal.transform, "", 22, UIKit.Accent, TextAnchor.MiddleCenter, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        }

        void BuildConvoy(Transform t)
        {
            var panel = UIGfx.Panel(t, "Convoy", Glass, new Vector2(1, 0.5f), new Vector2(1, 0.5f), new Vector2(-300, -170), new Vector2(-28, 60));
            _convoyPanel = panel.gameObject;
            _convoy = UIKit.Label(panel.transform, "", 20, UIKit.TextCol, TextAnchor.UpperLeft, Vector2.zero, Vector2.one, new Vector2(18, 14), new Vector2(-18, -14));
        }

        void BuildToasts(Transform t)
        {
            for (int i = 0; i < _toast.Length; i++)
            {
                float y = -34f - i * 62f;
                var bg = UIGfx.Panel(t, "Toast" + i, Glass, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(-300, y - 50), new Vector2(300, y), true);
                _toastBg[i] = bg;
                _toast[i] = UIKit.Label(bg.transform, "", 22, UIKit.TextCol, TextAnchor.MiddleCenter, Vector2.zero, Vector2.one, new Vector2(20, 0), new Vector2(-20, 0));
                bg.gameObject.SetActive(false);
            }
        }

        // ---------------------------------------------------------------- helpers
        static Text Lbl(Transform parent, string text, int size, Color col, TextAnchor anchor, Vector2 pos, Vector2 box, bool leftAligned = false)
        {
            var tx = UIKit.Label(parent, text, size, col, anchor, Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero);
            var rt = tx.rectTransform; rt.anchorMin = rt.anchorMax = Vector2.zero; rt.pivot = leftAligned ? new Vector2(0, 0.5f) : new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = pos; rt.sizeDelta = box; return tx;
        }

        static Image Bar(Transform parent, Vector2 pos, float width, Color col)
        {
            var back = UIGfx.Panel(parent, "BarBack", new Color(1, 1, 1, 0.12f), Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero, true);
            var brt = (RectTransform)back.transform; brt.anchorMin = brt.anchorMax = Vector2.zero; brt.pivot = new Vector2(0, 0.5f); brt.anchoredPosition = pos; brt.sizeDelta = new Vector2(width, 10f);
            var fill = UIGfx.Panel(back.transform, "Fill", col, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero, true);
            return fill;
        }

        static void SetBar(Image fill, float f, Color good, Color bad)
        {
            f = Mathf.Clamp01(f); var rt = fill.rectTransform; rt.anchorMax = new Vector2(Mathf.Max(f, 0.04f), 1f);
            fill.color = Color.Lerp(bad, good, Mathf.Clamp01(f * 2f));
        }

        public void SetVisible(bool v) { if (_root) _root.SetActive(v); if (_map != null) _map.SetVisible(v); }

        // ---------------------------------------------------------------- update
        void Update()
        {
            if (_s == null) return;
            _s.Notifications.Tick(Time.unscaledDeltaTime);
            DrawToasts();
            if (_s.Vehicle == null || !_s.Active) return;

            var v = _s.Vehicle; var def = v.definition;
            _smoothSpeed = Mathf.Lerp(_smoothSpeed, v.SpeedKmh, 1f - Mathf.Exp(-12f * Time.unscaledDeltaTime));
            _speed.horizontalOverflow = HorizontalWrapMode.Overflow;   // 4 digits used to wrap onto a second line
            _speed.text = Mathf.Clamp(Mathf.RoundToInt(_smoothSpeed), 0, 999).ToString();
            _gear.text = v.CurrentGear < 0 ? "R" : "D" + v.CurrentGear;
            float frac = Gauge.Fraction(_smoothSpeed, 0f, MaxDialKmh);
            _arc.fillAmount = 0.75f * frac; _arc.color = frac > 0.85f ? UIKit.Bad : UIKit.Accent;
            _needle.localRotation = Quaternion.Euler(0, 0, Gauge.Angle(_smoothSpeed, 0f, MaxDialKmh, 135f, -135f));

            SetBar(_fuelFill, v.fuelL / Mathf.Max(1f, def.fuelCapacityL), UIKit.Good, UIKit.Bad);
            SetBar(_dmgFill, 1f - v.damagePct / 100f, UIKit.Good, UIKit.Bad);
            _indicators.text = (v.LightsOn ? "◐ LIGHTS   " : "") + (v.Indicator < 0 ? "◀  " : v.Indicator > 0 ? "▶  " : "") + (v.OutOfFuel ? "⚠ NO FUEL" : "");
            if (_svc.Wallet != null) _wallet.text = TripFormat.Money(_svc.Wallet.balance) + " coins";

            Vector3? dest = null;
            if (Bus != null && Bus.Active)
            {
                _jobCard.SetActive(true); _jobTitle.text = "BUS ROUTE";
                var stop = Bus.NextStop; float d = Bus.DistanceToNext; dest = Bus.NextStopPosition;
                _job1.text = Bus.Route.code + "  ·  " + Bus.Route.name;
                _job2.text = $"Stop {Bus.NextIndex + 1}/{Bus.Route.stops.Length}: {stop.location.name}  ·  {TripFormat.Distance(d)}";
                _jobReward.text = $"Aboard {Bus.State.Aboard}/{Bus.Capacity}   Fares {TripFormat.Money(Bus.State.Revenue)}";
                _progress.rectTransform.anchorMax = new Vector2(Mathf.Max(0.02f, Bus.Progress), 0f);
                _eta.text = "ETA " + TripFormat.Eta(EtaEstimator.Seconds(d, v.SpeedKmh / 3.6f));
                _prompt.text = BusRules.Prompt(stop.location.name, Bus.IsLastStop, Bus.IsFirstStop, d, v.SpeedKmh);
            }
            else if (_s.Job == null)
            {
                _jobCard.SetActive(false); _eta.text = ""; _prompt.text = "";
            }
            else
            {
                _jobCard.SetActive(true); _jobTitle.text = "CURRENT JOB";
                bool deliver = _s.Phase == JobPhase.ToDestination;
                var target = deliver ? _s.Job.destination : _s.Job.origin;
                dest = new Vector3((float)target.world_x, 0f, (float)target.world_z);
                float d = _s.DistanceToTarget;
                _job1.text = $"{_s.Job.code}  ·  {_s.Job.cargo_type}";
                _job2.text = (deliver ? "Deliver to " : "Pick up at ") + target.name + "  ·  " + TripFormat.Distance(d);
                _jobReward.text = TripFormat.Money(_s.Job.reward) + " coins   " + new string('★', Mathf.Clamp(_s.Job.difficulty, 0, 5));
                float prog = deliver && _s.RouteDistance > 1f ? 1f - Mathf.Clamp01(d / _s.RouteDistance) : 0f;
                _progress.rectTransform.anchorMax = new Vector2(Mathf.Max(0.02f, prog), 0f);
                _eta.text = "ETA " + TripFormat.Eta(EtaEstimator.Seconds(d, v.SpeedKmh / 3.6f));
                _prompt.text = d < (deliver ? DrivingSession.DeliverRadius : DrivingSession.PickupRadius) * 2f
                    ? (deliver ? "Come to a stop to deliver" : "Come to a stop to load cargo") : "";
            }
            _map.SetTarget(v.transform); _map.Tick(dest);

            bool inConvoy = NetworkVehicle.Players.Count > 0;
            _convoyPanel.SetActive(inConvoy);
            if (inConvoy)
            {
                var sb = new System.Text.StringBuilder($"CONVOY  ({NetworkVehicle.Players.Count})\n");
                var leader = NetworkVehicle.Players.Leader;
                foreach (var m in NetworkVehicle.Players.Members) sb.Append(m == leader ? "★ " : "● ").Append(m.Name).Append('\n');
                _convoy.text = sb.ToString();
            }
        }

        void DrawToasts()
        {
            var items = _s.Notifications.Visible;
            for (int i = 0; i < _toast.Length; i++)
            {
                bool on = i < items.Count; _toastBg[i].gameObject.SetActive(on);
                if (!on) continue;
                var n = items[items.Count - 1 - i];   // newest on top
                _toast[i].text = n.Text;
                _toast[i].color = n.Level == Severity.Error ? UIKit.Bad : n.Level == Severity.Success ? UIKit.Good : n.Level == Severity.Warning ? UIKit.Accent : UIKit.TextCol;
                _toastBg[i].color = new Color(Glass.r, Glass.g, Glass.b, Mathf.Lerp(0f, Glass.a, Mathf.Clamp01(n.Remaining * 2f)));
            }
        }
    }
}
