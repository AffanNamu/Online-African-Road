using ARO.NetCore;
using UnityEngine;

namespace ARO.Vehicles
{
    /// <summary>Drives headlights, brake, reverse and indicator emitters from vehicle state (works with any VehicleRig).</summary>
    [RequireComponent(typeof(VehicleController))]
    public class VehicleLights : MonoBehaviour
    {
        static readonly int Emission = Shader.PropertyToID("_EmissionColor");
        public Light[] headlights; public Renderer[] headEmit, brakeEmit, reverseEmit, leftEmit, rightEmit;
        VehicleController _v; MaterialPropertyBlock _mpb; bool _lastOn;
        const float IndicatorHz = 1.5f;

        void Awake() { _v = GetComponent<VehicleController>(); _mpb = new MaterialPropertyBlock(); }

        public void Bind(VehicleRig r)
        {
            headlights = r.headlightLights; headEmit = r.headlightEmitters; brakeEmit = r.brakeEmitters;
            reverseEmit = r.reverseEmitters; leftEmit = r.indicatorLeftEmitters; rightEmit = r.indicatorRightEmitters;
        }

        VehicleLightPolicy.Beam _beam = VehicleLightPolicy.Beam.Off; bool _beamKnown;
        static int _smokePage = -1; bool _ll, _lr, _lbrake, _loggedOnce;
        static bool SmokePage { get { if (_smokePage < 0) { var u = Application.absoluteURL; _smokePage = !string.IsNullOrEmpty(u) && u.Contains("smoke=") ? 1 : 0; } return _smokePage == 1; } }

        void Update()
        {
            bool blink = Mathf.Repeat(Time.time * IndicatorHz, 1f) < 0.5f;
            var beam = VehicleLightPolicy.BeamOf(_v.LightsOn, _v.HighBeam);
            if (!_beamKnown || beam != _beam) { _beam = beam; _beamKnown = true; ApplyBeam(beam); }
            foreach (var l in headlights) if (l != null) l.enabled = beam != VehicleLightPolicy.Beam.Off;
            if (_v.LightsOn != _lastOn)
            {   // lighting diagnostics: what the headlights actually are when they switch (tests and reviews read this)
                _lastOn = _v.LightsOn; int n = 0; Light first = null; foreach (var l in headlights) if (l != null) { n++; if (first == null) first = l; }
                var ci = System.Globalization.CultureInfo.InvariantCulture;
                Debug.Log($"[Light] headlights on={_v.LightsOn} lights={n}" + (first != null ? $" type={first.type} angle={first.spotAngle.ToString("0", ci)} range={first.range.ToString("0", ci)} intensity={first.intensity.ToString("0.0", ci)} enabled={first.enabled}" : "") + $" pixelLightCount={QualitySettings.pixelLightCount}");
            }
            Set(headEmit, beam == VehicleLightPolicy.Beam.High ? new Color(4.2f, 3.9f, 3.1f) : beam == VehicleLightPolicy.Beam.Low ? new Color(3f, 2.8f, 2.2f) : Color.black);
            // Tail lights glow dim when headlights are on, bright when braking (the brake works with the lights off too).
            float tail = VehicleLightPolicy.TailLevel(_v.LightsOn, _v.BrakeLit);
            Set(brakeEmit, tail >= 1f ? new Color(4f, 0f, 0f) : tail > 0f ? new Color(0.8f, 0f, 0f) : Color.black);
            Set(reverseEmit, _v.Reversing ? new Color(3f, 3f, 3f) : Color.black);
            VehicleLightPolicy.Indicators(_v.Indicator, _v.Hazard, blink, out bool left, out bool right);
            Set(leftEmit, left ? new Color(4f, 2f, 0f) : Color.black);
            Set(rightEmit, right ? new Color(4f, 2f, 0f) : Color.black);
            if (SmokePage)
            {   // CI reads these edges: proof that indicators and hazards flash and that the brake lamp follows the brake (what the lamps do, not only what the state says)
                bool brakeLamp = tail >= 1f;
                if (!_loggedOnce || left != _ll || right != _lr || brakeLamp != _lbrake)
                {
                    _loggedOnce = true; _ll = left; _lr = right; _lbrake = brakeLamp;
                    Debug.Log($"[Light] rear indicatorLeft={left} indicatorRight={right} hazard={_v.Hazard} indicatorState={_v.Indicator} brakeLamp={brakeLamp} tailLevel={tail.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture)} lights={_v.LightsOn} t={Time.time.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture)}");
                }
            }
        }

        float[] _baseAngle, _baseInner, _baseRange, _baseIntensity, _basePitch;
        Light[] _high;          // the high-beam lamps: a second pair beside the low-beam lamps, created the first time the high beam is used

        /// <summary>
        /// The low beam is whatever the vehicle brought (the placeholder truck uses HeadlightSpec; a production model's own lights are kept as they are) and it stays on when the high beam is switched on.
        /// The high beam ADDS a second pair of lamps, as the main-beam lamps of a real vehicle do: narrower, reaching farther, aimed nearly level, in the position of the low-beam lamps (HeadlightSpec.High*,
        /// scaled from the vehicle's own lamps by the same ratios). CI run 306 showed the earlier design, the same two lights re-aimed and narrowed, rendering a DIMMER road ahead than the low beam
        /// (far road bands 126.7 against 98.6 in the bumper view); adding lamps can only add light, and it costs two lights only while the high beam is on (the low tier allows four per object).
        /// </summary>
        void ApplyBeam(VehicleLightPolicy.Beam beam)
        {
            int n = headlights.Length;
            if (_baseAngle == null)
            {
                _baseAngle = new float[n]; _baseInner = new float[n]; _baseRange = new float[n]; _baseIntensity = new float[n]; _basePitch = new float[n];
                for (int i = 0; i < n; i++) { var l = headlights[i]; if (l == null) continue; _baseAngle[i] = l.spotAngle; _baseInner[i] = l.innerSpotAngle; _baseRange[i] = l.range; _baseIntensity[i] = l.intensity; _basePitch[i] = l.transform.localEulerAngles.x; }
            }
            bool high = beam == VehicleLightPolicy.Beam.High;
            if (high && _high == null)
            {
                _high = new Light[n];
                for (int i = 0; i < n; i++)
                {
                    var l = headlights[i]; if (l == null || l.transform.parent == null) continue;
                    var go = new GameObject("HighBeam" + i); go.transform.SetParent(l.transform.parent, false);
                    go.transform.localPosition = l.transform.localPosition;
                    var e = l.transform.localEulerAngles; go.transform.localRotation = Quaternion.Euler(_basePitch[i] - (HeadlightSpec.PitchDownDeg - HeadlightSpec.HighPitchDownDeg), e.y, e.z);
                    var h = go.AddComponent<Light>(); h.type = LightType.Spot; h.color = l.color; h.shadows = LightShadows.None; h.renderMode = l.renderMode; h.cullingMask = l.cullingMask; h.bounceIntensity = 0f;
                    h.spotAngle = _baseAngle[i] * (HeadlightSpec.HighOuterAngleDeg / HeadlightSpec.OuterAngleDeg);
                    h.innerSpotAngle = _baseInner[i] * (HeadlightSpec.HighInnerAngleDeg / HeadlightSpec.InnerAngleDeg);
                    h.range = _baseRange[i] * (HeadlightSpec.HighRange / HeadlightSpec.Range);
                    h.intensity = _baseIntensity[i] * (HeadlightSpec.HighIntensity / HeadlightSpec.Intensity);
                    h.enabled = false; _high[i] = h;
                }
            }
            if (_high != null) foreach (var h in _high) if (h != null) h.enabled = high;
            int extra = 0; if (_high != null) foreach (var h in _high) if (h != null) extra++;
            Debug.Log($"[Light] beam={beam} lights={n} highBeamLamps={(high ? extra : 0)}");
            if (SmokePage)
            {   // what each lamp actually is while this beam is on (CI reads it to tell a wrong number from a wrong picture)
                var ci = System.Globalization.CultureInfo.InvariantCulture;
                for (int i = 0; i < n; i++)
                {
                    var l = headlights[i]; if (l != null) Debug.Log($"[Light] lamp low {i} intensity={l.intensity.ToString("0", ci)} range={l.range.ToString("0", ci)} angle={l.spotAngle.ToString("0", ci)} pitchDown={l.transform.eulerAngles.x.ToString("0.0", ci)} height={l.transform.position.y.ToString("0.00", ci)} on={l.enabled}");
                    if (_high != null && _high[i] != null) Debug.Log($"[Light] lamp high {i} intensity={_high[i].intensity.ToString("0", ci)} range={_high[i].range.ToString("0", ci)} angle={_high[i].spotAngle.ToString("0", ci)} pitchDown={_high[i].transform.eulerAngles.x.ToString("0.0", ci)} height={_high[i].transform.position.y.ToString("0.00", ci)} on={_high[i].enabled}");
                }
            }
        }

        void Set(Renderer[] rs, Color c)
        {
            if (rs == null) return;
            _mpb.SetColor(Emission, c);
            foreach (var r in rs) if (r != null) r.SetPropertyBlock(_mpb);
        }
    }
}
