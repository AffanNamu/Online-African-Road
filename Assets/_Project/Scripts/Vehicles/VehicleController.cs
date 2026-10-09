using System;
using ARO.Input;
using UnityEngine;

namespace ARO.Vehicles
{
    [Serializable]
    public class WheelAxle
    {
        public WheelCollider left, right;
        public Transform leftVisual, rightVisual;
        public bool steer, drive;
    }

    /// <summary>
    /// Wheel-collider vehicle with automatic gearbox, fuel and collision damage.
    /// Category-agnostic: behaviour comes from <see cref="VehicleDefinition"/>, not the vehicle type.
    /// Input comes from any <see cref="IVehicleInput"/>; networking can replace it with a remote state.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public class VehicleController : MonoBehaviour
    {
        public VehicleDefinition definition;
        public WheelAxle[] axles;
        public Transform centerOfMass;

        [Header("Runtime state (persisted by backend)")]
        public float fuelL = 120f;
        public float damagePct;
        public float odometerKm;

        public int CurrentGear { get; private set; } = 1;   // -1 reverse, 0 neutral, 1.. forward
        public float Rpm { get; private set; }
        public float SpeedKmh { get; private set; }
        public bool LightsOn { get; private set; }
        public int Indicator { get; private set; }          // -1 left, 0 off, 1 right
        public bool HornActive { get; private set; }
        public bool OutOfFuel => fuelL <= 0f;
        public bool BrakeLit { get; private set; }
        public bool Reversing => CurrentGear == -1;
        /// <summary>1 = dry. WeatherSystem lowers it in rain; applied to tyre friction.</summary>
        public float GripMultiplier { get; set; } = 1f;
        public float SuspensionJolt { get; private set; }   // 0..1 recent vertical impulse, for camera/audio
        public event Action<float> Damaged;                  // impact damage percent added

        Rigidbody _rb;
        IVehicleInput _input;
        VehicleInputState _state;
        float _lastKm;
        float[] _baseFwd, _baseSide; float _appliedGrip = 1f;
        float _lastVy;

        public void SetInput(IVehicleInput input) => _input = input;
        public void SetLights(bool on) => LightsOn = on;

        void Awake()
        {
            _rb = GetComponent<Rigidbody>();
            if (definition != null) _rb.mass = definition.stats.massKg;
            _rb.centerOfMass = centerOfMass != null ? transform.InverseTransformPoint(centerOfMass.position) : new Vector3(0, -0.6f, 0);
            _rb.interpolation = RigidbodyInterpolation.Interpolate;
            if (_input == null) _input = GetComponent<IVehicleInput>();
            CacheFriction();
        }

        void CacheFriction()
        {
            _baseFwd = new float[axles.Length * 2]; _baseSide = new float[axles.Length * 2];
            for (int i = 0; i < axles.Length; i++)
            {
                _baseFwd[i * 2] = axles[i].left.forwardFriction.stiffness; _baseFwd[i * 2 + 1] = axles[i].right.forwardFriction.stiffness;
                _baseSide[i * 2] = axles[i].left.sidewaysFriction.stiffness; _baseSide[i * 2 + 1] = axles[i].right.sidewaysFriction.stiffness;
            }
        }

        void ApplyGrip(float g)
        {
            _appliedGrip = g;
            for (int i = 0; i < axles.Length; i++)
            {
                SetStiff(axles[i].left, _baseFwd[i * 2] * g, _baseSide[i * 2] * g);
                SetStiff(axles[i].right, _baseFwd[i * 2 + 1] * g, _baseSide[i * 2 + 1] * g);
            }
        }
        static void SetStiff(WheelCollider w, float fwd, float side)
        {
            var f = w.forwardFriction; f.stiffness = fwd; w.forwardFriction = f;
            var sd = w.sidewaysFriction; sd.stiffness = side; w.sidewaysFriction = sd;
        }

        void Update()
        {
            if (_input == null) return;
            _state = _input.Read();
            if (_state.LightsToggled) LightsOn = !LightsOn;
            if (_state.IndicatorLeftToggled) Indicator = Indicator == -1 ? 0 : -1;
            if (_state.IndicatorRightToggled) Indicator = Indicator == 1 ? 0 : 1;
            HornActive = _state.HornHeld;
            UpdateVisuals();
        }

        void FixedUpdate()
        {
            if (definition == null) return;
            var st = definition.stats;
            if (!Mathf.Approximately(_appliedGrip, GripMultiplier)) ApplyGrip(GripMultiplier);
            float vy = _rb.linearVelocity.y; SuspensionJolt = Mathf.Lerp(SuspensionJolt, Mathf.Clamp01(Mathf.Abs(vy - _lastVy) * 4f), 0.3f); _lastVy = vy;
            float forwardSpeed = Vector3.Dot(_rb.linearVelocity, transform.forward);
            SpeedKmh = Mathf.Abs(forwardSpeed) * 3.6f;

            // Brake at standstill engages reverse; throttle at standstill returns to drive.
            float throttle = _state.Throttle, brake = _state.Brake;
            if (CurrentGear == -1)
            {
                if (_state.Throttle > 0.1f && forwardSpeed > -0.5f && _state.Brake < 0.1f) CurrentGear = 1;
                else { throttle = _state.Brake; brake = _state.Throttle; }
            }
            else if (_state.Brake > 0.1f && _state.Throttle < 0.1f && forwardSpeed < 0.5f) CurrentGear = -1;
            if (CurrentGear == 0) CurrentGear = 1;

            float gearRatio = CurrentGear == -1 ? -st.reverseRatio
                              : st.gears[Mathf.Clamp(CurrentGear - 1, 0, st.gears.Length - 1)];

            // Engine RPM from driven wheel speed.
            int driven = 0; float wheelRpm = 0f;
            foreach (var a in axles) if (a.drive) { wheelRpm += a.left.rpm + a.right.rpm; driven += 2; }
            wheelRpm = driven > 0 ? Mathf.Abs(wheelRpm / driven) : 0f;
            Rpm = Mathf.Clamp(wheelRpm * Mathf.Abs(gearRatio) * st.finalDrive, definition.idleRpm, definition.maxRpm);

            if (CurrentGear > 0)
            {
                if (Rpm > definition.upshiftRpm && CurrentGear < st.gears.Length && throttle > 0.3f) CurrentGear++;
                else if (Rpm < definition.downshiftRpm && CurrentGear > 1) CurrentGear--;
            }

            // Torque falls off toward redline; hard speed limiter at the model's top speed.
            float rpmFactor = 1f - Mathf.Pow(Mathf.InverseLerp(definition.idleRpm, definition.maxRpm, Rpm), 3f) * 0.8f;
            bool limited = SpeedKmh >= definition.maxSpeedKmh && CurrentGear > 0;
            float motor = (OutOfFuel || limited) ? 0f
                : throttle * st.torqueNm * rpmFactor * gearRatio * st.finalDrive / Mathf.Max(driven, 1);

            // Steering reduces with speed so a truck does not flip at 100 km/h.
            BrakeLit = brake > 0.05f || _state.Handbrake;
            float steerAngle = _state.Steer * st.maxSteerDeg * Mathf.Lerp(1f, 0.25f, SpeedKmh / definition.maxSpeedKmh);
            float brakeTorque = brake * st.brakeTorqueNm * 0.5f;
            float handbrake = _state.Handbrake ? st.brakeTorqueNm : 0f;

            foreach (var a in axles)
            {
                if (a.steer) { a.left.steerAngle = steerAngle; a.right.steerAngle = steerAngle; }
                float m = a.drive ? motor : 0f;
                a.left.motorTorque = m; a.right.motorTorque = m;
                float bt = brakeTorque + (a.drive ? handbrake : 0f);
                a.left.brakeTorque = bt; a.right.brakeTorque = bt;
            }

            // Fuel and odometer from distance actually travelled.
            float stepKm = Mathf.Abs(forwardSpeed) * Time.fixedDeltaTime / 1000f;
            odometerKm += stepKm;
            fuelL = Mathf.Max(0f, fuelL - stepKm * st.fuelBurnLPerKm * (0.5f + throttle * 0.5f));
        }

        void OnCollisionEnter(Collision c)
        {
            float impulse = c.impulse.magnitude;
            float threshold = _rb.mass * 3f;                // ~3 m/s change in velocity is free
            if (impulse <= threshold) return;
            float add = Mathf.Min((impulse - threshold) / (_rb.mass * 20f) * 100f, 100f - damagePct);
            if (add <= 0.01f) return;
            damagePct += add;
            Damaged?.Invoke(add);
        }

        void UpdateVisuals()
        {
            foreach (var a in axles)
            {
                Sync(a.left, a.leftVisual);
                Sync(a.right, a.rightVisual);
            }
        }

        static void Sync(WheelCollider col, Transform vis)
        {
            if (col == null || vis == null) return;
            col.GetWorldPose(out var pos, out var rot);
            vis.SetPositionAndRotation(pos, rot);
        }

        /// <summary>Distance travelled since last call; used for job progress reporting.</summary>
        public float ConsumeDistanceKm()
        {
            float d = odometerKm - _lastKm; _lastKm = odometerKm; return d;
        }
    }
}
