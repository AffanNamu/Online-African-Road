using ARO.Vehicles;
using UnityEngine;
using UnityEngine.InputSystem;

namespace ARO.World
{
    /// <summary>
    /// Rain that actually changes the simulation: particles follow the camera, road materials get wet
    /// (darker, glossier -> reflections), fog/overcast darken the sky, and tyre grip drops on the player vehicle.
    /// Windscreen droplets/wipers (cockpit view) are NOT implemented yet.
    /// </summary>
    public class WeatherSystem : MonoBehaviour
    {
        public TimeOfDay timeOfDay;
        public Transform vehicle;
        public Material[] roadMaterials;
        [Range(0, 1)] public float rain, target;
        public float changeSpeed = 0.05f, dryingSpeed = 0.01f;
        public float Wetness { get; private set; }

        ParticleSystem _ps; ParticleSystem.EmissionModule _em;
        Color[] _baseColor; float[] _baseSmooth;
        static readonly int BaseColor = Shader.PropertyToID("_BaseColor"), Smooth = Shader.PropertyToID("_Smoothness");

        void Start()
        {
            var go = new GameObject("Rain"); go.transform.SetParent(transform, false);
            _ps = go.AddComponent<ParticleSystem>();
            var main = _ps.main; main.loop = true; main.startLifetime = 0.9f; main.startSpeed = 0f; main.startSize = 0.03f; main.maxParticles = 6000;
            main.simulationSpace = ParticleSystemSimulationSpace.World; main.gravityModifier = 0f; main.startColor = new Color(0.8f, 0.85f, 0.9f, 0.35f);
            var shape = _ps.shape; shape.shapeType = ParticleSystemShapeType.Box; shape.scale = new Vector3(40, 1, 40);
            var vel = _ps.velocityOverLifetime; vel.enabled = true; vel.space = ParticleSystemSimulationSpace.World; vel.y = new ParticleSystem.MinMaxCurve(-22f);
            _em = _ps.emission; _em.rateOverTime = 0f;
            var r = go.GetComponent<ParticleSystemRenderer>(); r.renderMode = ParticleSystemRenderMode.Stretch; r.lengthScale = 6f; r.velocityScale = 0.04f;
            var sh = Shader.Find("Universal Render Pipeline/Particles/Unlit") ?? Shader.Find("Sprites/Default");
            r.sharedMaterial = new Material(sh);
            _baseColor = new Color[roadMaterials.Length]; _baseSmooth = new float[roadMaterials.Length];
            for (int i = 0; i < roadMaterials.Length; i++)
            {
                _baseColor[i] = roadMaterials[i].HasProperty(BaseColor) ? roadMaterials[i].GetColor(BaseColor) : roadMaterials[i].color;
                _baseSmooth[i] = roadMaterials[i].HasProperty(Smooth) ? roadMaterials[i].GetFloat(Smooth) : 0.1f;
            }
        }

        void OnDestroy()
        {   // restore shared materials so rain state never leaks into the asset on disk (Editor)
            if (roadMaterials == null || _baseColor == null) return;
            for (int i = 0; i < roadMaterials.Length; i++) Apply(roadMaterials[i], _baseColor[i], _baseSmooth[i], 0f);
        }

        void Update()
        {
            if (Keyboard.current != null && (Application.isEditor || Debug.isDebugBuild))
            {
                if (Keyboard.current.f2Key.wasPressedThisFrame) target = target > 0.1f ? 0f : 0.9f;       // dev: toggle rain
                if (Keyboard.current.f3Key.wasPressedThisFrame && timeOfDay != null) { timeOfDay.followWestAfricaTime = false; timeOfDay.hour = Mathf.Repeat(timeOfDay.hour + 2f, 24f); }
            }
            rain = Mathf.MoveTowards(rain, target, changeSpeed * Time.deltaTime);
            // Surfaces get wet quickly and dry slowly.
            Wetness = rain > Wetness ? Mathf.MoveTowards(Wetness, rain, 0.2f * Time.deltaTime) : Mathf.MoveTowards(Wetness, rain, dryingSpeed * Time.deltaTime);

            var cam = Camera.main;
            if (cam != null) _ps.transform.position = cam.transform.position + Vector3.up * 14f;
            _em.rateOverTime = 4500f * rain;
            if (timeOfDay != null) timeOfDay.overcast = Mathf.Clamp01(rain * 1.1f);

            for (int i = 0; i < roadMaterials.Length; i++) Apply(roadMaterials[i], _baseColor[i], _baseSmooth[i], Wetness);
            if (vehicle != null && vehicle.TryGetComponent(out VehicleController v))
                v.GripMultiplier = 1f - Wetness * (v.definition != null ? v.definition.rainGripPenalty : 0.3f);
        }

        static void Apply(Material m, Color baseCol, float baseSmooth, float wet)
        {
            var c = baseCol * Mathf.Lerp(1f, 0.55f, wet); c.a = baseCol.a;
            if (m.HasProperty(BaseColor)) m.SetColor(BaseColor, c); else m.color = c;
            if (m.HasProperty(Smooth)) m.SetFloat(Smooth, Mathf.Lerp(baseSmooth, 0.9f, wet));
        }
    }
}
