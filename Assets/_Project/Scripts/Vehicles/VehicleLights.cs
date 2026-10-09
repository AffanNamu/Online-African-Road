using UnityEngine;

namespace ARO.Vehicles
{
    /// <summary>Drives headlights, brake, reverse and indicator emitters from vehicle state (works with any VehicleRig).</summary>
    [RequireComponent(typeof(VehicleController))]
    public class VehicleLights : MonoBehaviour
    {
        static readonly int Emission = Shader.PropertyToID("_EmissionColor");
        public Light[] headlights; public Renderer[] headEmit, brakeEmit, reverseEmit, leftEmit, rightEmit;
        VehicleController _v; MaterialPropertyBlock _mpb;
        const float IndicatorHz = 1.5f;

        void Awake() { _v = GetComponent<VehicleController>(); _mpb = new MaterialPropertyBlock(); }

        public void Bind(VehicleRig r)
        {
            headlights = r.headlightLights; headEmit = r.headlightEmitters; brakeEmit = r.brakeEmitters;
            reverseEmit = r.reverseEmitters; leftEmit = r.indicatorLeftEmitters; rightEmit = r.indicatorRightEmitters;
        }

        void Update()
        {
            bool blink = Mathf.Repeat(Time.time * IndicatorHz, 1f) < 0.5f;
            foreach (var l in headlights) if (l != null) l.enabled = _v.LightsOn;
            Set(headEmit, _v.LightsOn ? new Color(3f, 2.8f, 2.2f) : Color.black);
            // Tail lights glow dim when headlights are on, bright when braking.
            Set(brakeEmit, _v.BrakeLit ? new Color(4f, 0f, 0f) : _v.LightsOn ? new Color(0.8f, 0f, 0f) : Color.black);
            Set(reverseEmit, _v.Reversing ? new Color(3f, 3f, 3f) : Color.black);
            Set(leftEmit, _v.Indicator == -1 && blink ? new Color(4f, 2f, 0f) : Color.black);
            Set(rightEmit, _v.Indicator == 1 && blink ? new Color(4f, 2f, 0f) : Color.black);
        }

        void Set(Renderer[] rs, Color c)
        {
            if (rs == null) return;
            _mpb.SetColor(Emission, c);
            foreach (var r in rs) if (r != null) r.SetPropertyBlock(_mpb);
        }
    }
}
