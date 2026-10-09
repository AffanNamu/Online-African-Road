using UnityEngine;

namespace ARO.Vehicles
{
    /// <summary>
    /// Procedural diesel engine + tyre/wind bed driven by RPM, load, speed and surface roughness.
    /// This is a physically-parameterised synth used until recorded engine loops are authored; swap by
    /// replacing this component with a clip-crossfading one that reads the same VehicleController fields.
    /// </summary>
    [RequireComponent(typeof(VehicleController))]
    public class EngineAudio : MonoBehaviour
    {
        VehicleController _v; AudioSource _src;
        // Written on main thread, read on audio thread (floats are atomic enough for audio params).
        volatile float _firingHz, _load, _speed01, _jolt;
        double _phase; System.Random _rng; float _noiseLp;
        float _sampleRate;

        void Awake()
        {
            _v = GetComponent<VehicleController>(); _rng = new System.Random(7);
            _sampleRate = AudioSettings.outputSampleRate;
            _src = gameObject.AddComponent<AudioSource>();
            _src.spatialBlend = 0f; _src.loop = true; _src.playOnAwake = false; _src.volume = 0.5f;
            _src.clip = AudioClip.Create("engine_sink", 2048, 1, (int)_sampleRate, false);  // silent clip; OnAudioFilterRead fills it
            _src.Play();
        }

        void Update()
        {
            // 6-cylinder 4-stroke: 3 firing events per revolution.
            _firingHz = _v.Rpm / 60f * 3f;
            _load = Mathf.Clamp01(_v.OutOfFuel ? 0f : 0.35f + (_v.SpeedKmh < 5f ? 0f : 0.2f));
            _speed01 = Mathf.Clamp01(_v.SpeedKmh / Mathf.Max(1f, _v.definition.maxSpeedKmh));
            _jolt = _v.SuspensionJolt;
        }

        void OnAudioFilterRead(float[] data, int channels)
        {
            float hz = _firingHz, load = _load, spd = _speed01, jolt = _jolt;
            double inc = hz / _sampleRate;
            for (int i = 0; i < data.Length; i += channels)
            {
                _phase += inc; if (_phase > 1.0) _phase -= 1.0;
                float p = (float)_phase;
                // Pulse-train core with harmonics gives the low "diesel chug"; sub-harmonic adds body.
                float s = Mathf.Sin(p * 6.2832f) * 0.55f + Mathf.Sin(p * 12.566f) * 0.3f + Mathf.Sin(p * 18.85f) * 0.18f
                        + Mathf.Sin(p * 3.1416f) * 0.25f;
                s *= 0.5f + 0.5f * Mathf.Pow(1f - p, 3f);          // combustion "thump" envelope
                float n = (float)(_rng.NextDouble() * 2.0 - 1.0);
                _noiseLp += (n - _noiseLp) * 0.05f;                 // low-passed noise = rumble / road
                float wind = n * 0.03f * spd * spd;
                float tyre = _noiseLp * 0.25f * spd + _noiseLp * jolt * 0.4f;
                float o = Mathf.Clamp(s * (0.25f + load * 0.35f) + tyre + wind, -1f, 1f) * 0.8f;
                for (int c = 0; c < channels; c++) data[i + c] = o;
            }
        }
    }
}
