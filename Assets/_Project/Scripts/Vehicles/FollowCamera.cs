using ARO.Input;
using ARO.NetCore;
using UnityEngine;
using UnityEngine.InputSystem;

namespace ARO.Vehicles
{
    /// <summary>
    /// The driving camera manager: the one component on the scene's Main Camera that follows the player's vehicle in one of eight views
    /// (cockpit, driver cabin, chase, far chase, left, right, overhead, front bumper). It is purely local presentation: nothing here is part of the
    /// vehicle state, nothing is networked, and other players' cameras and puppets never see it.
    ///
    /// The numbers (offsets, lags, field of view, obstruction floors) come from <see cref="CameraProfiles"/> for the vehicle's category, scaled by the
    /// model's own framing. The choice rules (next / previous / direct, unavailable views, fallback) are <see cref="CameraSelector"/>; both are covered by tests
    /// in tests/NetCore.Tests. This class only turns them into a camera pose each frame:
    ///   - rigid views (cockpit, driver cabin, bumper) sit on the vehicle (a rig anchor when the model has one) with no lag and no chase effects;
    ///   - following views live in yaw space, damped, and are pulled in by anything the sphere sweep from the vehicle meets (never the vehicle itself);
    ///   - changing view blends from the camera's current pose over about half a second.
    /// The old behaviour is kept for the chase view: lag, speed FOV, brake/accelerate pitch, road-roughness shake scaled by the Camera Shake setting, mouse wheel distance.
    /// </summary>
    public class FollowCamera : MonoBehaviour
    {
        public Transform target;

        /// <summary>Raised when something the player asked for cannot be done (a view that is unavailable), with the reason to show.</summary>
        public event System.Action<string> Notice;

        public CameraView View => _sel.Current;
        public CameraViewConfig Config => _profile != null ? _profile[_sel.Current] : null;
        public string ViewName => Config != null ? Config.Name : "";
        public VehicleCameraProfile Profile => _profile;
        public bool IsBlending => _blending;
        /// <summary>The scale of the chase distance in the current view (1 = the configured distance).</summary>
        public float Zoom => _zoom[(int)_sel.Current];

        const int HitBuffer = 32;   // a vehicle model has many colliders of its own; they must not use up the slots an obstacle needs

        readonly CameraSelector _sel = new CameraSelector();
        readonly float[] _zoom = { 1f, 1f, 1f, 1f, 1f, 1f, 1f, 1f };
        readonly RaycastHit[] _hits = new RaycastHit[HitBuffer];
        VehicleCameraProfile _profile;
        Transform _bound; Rigidbody _rb; VehicleController _vc; VehicleRig _rig;
        Camera _cam;
        // following-view state
        Vector3 _smPos; Quaternion _smRot = Quaternion.identity; bool _smValid; float _pull = 1f;
        // transition state
        bool _blending; float _blendT; Vector3 _fromPos; Quaternion _fromRot;
        // chase effects
        float _lastSpeed, _pitch;

        void Awake()
        {
            _cam = GetComponent<Camera>();
            Debug.Log($"[Camera] manager ready on '{name}' tag={tag} isMainCamera={(Camera.main == _cam)} cameras={Camera.allCamerasCount}");
        }

        // ---------------------------------------------------------------- choosing a view
        /// <summary>A key, gamepad or touch request from the driver's input.</summary>
        public CameraSelectResult Request(CameraRequest r, string source = "input")
        {
            if (_profile == null) return CameraSelectResult.Invalid;
            if (r.Select != 0)
            {
                CameraViews.TryFromDigit(r.Select, out var asked);
                return Report(_sel.SelectDigit(r.Select), source, asked);
            }
            return Report(_sel.Step(r.Step), source, _sel.Current);
        }

        public CameraSelectResult Next(string source = "button") => Request(CameraRequest.Next, source);
        public CameraSelectResult Previous(string source = "button") => Request(CameraRequest.Previous, source);
        public CameraSelectResult Select(CameraView v, string source = "code") => _profile == null ? CameraSelectResult.Invalid : Report(_sel.Select(v), source, v);

        CameraSelectResult Report(CameraSelectResult res, string source, CameraView asked)
        {
            if (res == CameraSelectResult.Changed)
            {
                StartBlend();
                Debug.Log($"[Camera] view={_sel.Current} number={CameraViews.Digit(_sel.Current)} name={ViewName} source={source} available={_profile.AvailableCount}/{CameraViews.Count} main={Camera.main == _cam} cameras={Camera.allCamerasCount}");
            }
            else if (res == CameraSelectResult.Unavailable)
            {
                Debug.Log($"[Camera] unavailable view={asked} number={CameraViews.Digit(asked)} source={source} stays={_sel.Current} reason={_sel.Reason}");
                Notice?.Invoke(string.IsNullOrEmpty(_sel.Reason) ? "That camera view is not available." : _sel.Reason);
            }
            else if (res == CameraSelectResult.Invalid) Debug.Log($"[Camera] ignored request source={source} (invalid view)");
            return res;
        }

        void StartBlend()
        {
            _fromPos = transform.position; _fromRot = transform.rotation; _blendT = 0f; _blending = true; _smValid = false;
        }

        // ---------------------------------------------------------------- the vehicle changed
        void Bind()
        {
            _bound = target; _rb = target.GetComponent<Rigidbody>(); _vc = target.GetComponent<VehicleController>(); _rig = target.GetComponent<VehicleRig>();
            var def = _vc != null ? _vc.definition : null;
            string category = def != null ? def.category.ToString() : "truck";
            bool cabin = false; string cabinReason = null;
            if (_rig != null && _rig.driverRig != null)
            {
                cabin = _rig.driverRig.IsComplete;
                if (!cabin) cabinReason = "The driver rig is missing " + _rig.driverRig.FirstMissing + " (docs/PLACEHOLDERS.md P-92).";
            }
            _profile = CameraProfiles.Build(category, def != null ? def.cameraFollowScale : 1f, def != null ? def.cameraHeightScale : 1f, cabin, cabinReason);
            var problems = CameraProfiles.Validate(_profile);
            if (problems.Count > 0) Debug.LogError("[Camera] invalid camera profile: " + string.Join("; ", problems));
            var res = _sel.SetProfile(_profile);
            _smValid = false; _blending = false; _pull = 1f; _pitch = 0f; _lastSpeed = _rb != null ? _rb.linearVelocity.magnitude : 0f;
            Debug.Log($"[Camera] bound vehicle={target.name} category={category} view={_sel.Current}{(res == CameraSelectResult.Changed ? " (fallback)" : "")} available={_profile.AvailableCount}/{CameraViews.Count} " +
                      $"cockpitAnchor={(_rig != null && _rig.cockpitCameraAnchor != null)} bumperAnchor={(_rig != null && _rig.bumperCameraAnchor != null)} driverRig={(_rig != null && _rig.driverRig != null ? (cabin ? "complete" : "incomplete") : "none")}");
        }

        // ---------------------------------------------------------------- each frame
        void LateUpdate() => Step(Time.deltaTime);

        /// <summary>One camera update. Public so the tests can drive it with a chosen frame time.</summary>
        public void Step(float deltaTime)
        {
            if (target == null) return;
            if (_cam == null) _cam = GetComponent<Camera>();   // Awake does not run for a component added in Edit Mode (the tests)
            if (target != _bound) Bind();
            float dt = CameraMath.StepTime(deltaTime);
            var cfg = _profile[_sel.Current];

            float speed = _rb != null ? _rb.linearVelocity.magnitude : 0f;
            float accel = (speed - _lastSpeed) / Mathf.Max(deltaTime, 0.0001f); _lastSpeed = speed;
            _pitch = Mathf.Lerp(_pitch, Mathf.Clamp(-accel * 0.15f, -2.5f, 2.5f), 1f - Mathf.Exp(-3f * dt));

            if (cfg.Zoomable && Mouse.current != null)
            {
                float wheel = Mouse.current.scroll.ReadValue().y;
                _zoom[(int)cfg.View] = CameraMath.WheelZoom(_zoom[(int)cfg.View], wheel, cfg);
            }

            Vector3 pos; Quaternion rot;
            if (cfg.Rigid) RigidPose(cfg, out pos, out rot); else FollowPose(cfg, dt, speed, out pos, out rot);

            if (_blending)
            {
                _blendT = CameraMath.BlendProgress(_blendT, dt);
                float e = CameraMath.Ease(_blendT);
                pos = Vector3.Lerp(_fromPos, pos, e); rot = Quaternion.Slerp(_fromRot, rot, e);
                if (_blendT >= 1f) _blending = false;
            }
            transform.SetPositionAndRotation(pos, rot);
            if (_cam != null) _cam.fieldOfView = CameraMath.Damp(_cam.fieldOfView, CameraMath.Fov(cfg, speed), 3f, dt);
        }

        void RigidPose(CameraViewConfig cfg, out Vector3 pos, out Quaternion rot)
        {
            var anchor = RigidAnchor(cfg.View);
            if (anchor != null) { pos = anchor.position; rot = anchor.rotation; return; }
            pos = target.TransformPoint(new Vector3(cfg.X, cfg.Y, cfg.Z));
            rot = target.rotation * Quaternion.Euler(cfg.PitchDeg, 0f, 0f);
        }

        Transform RigidAnchor(CameraView v)
        {
            if (_rig == null) return null;
            switch (v)
            {
                case CameraView.Cockpit: return _rig.cockpitCameraAnchor;
                case CameraView.FrontBumper: return _rig.bumperCameraAnchor;
                case CameraView.DriverCabin: return _rig.driverRig != null && _rig.driverRig.IsComplete ? _rig.driverRig.cabinCameraAnchor : null;
                default: return null;
            }
        }

        void FollowPose(CameraViewConfig cfg, float dt, float speed, out Vector3 pos, out Quaternion rot)
        {
            float zoom = cfg.Zoomable ? _zoom[(int)cfg.View] : 1f;
            var yaw = Quaternion.Euler(0f, target.eulerAngles.y, 0f);
            var offset = new Vector3(cfg.X, cfg.Y * Mathf.Lerp(0.8f, 1f, zoom), cfg.Z * zoom);
            var pivot = target.position + yaw * new Vector3(0f, cfg.LookY, cfg.LookZ);   // what the camera looks at, and where the obstruction sweep starts
            var want = target.position + yaw * offset;
            if (!_smValid) { _smPos = want; _smRot = Aim(pivot, want); _smValid = true; _pull = 1f; }
            else _smPos = Vector3.Lerp(_smPos, want, CameraMath.DampFactor(cfg.PositionLag, dt));

            pos = cfg.CollisionAware ? PulledIn(cfg, pivot, _smPos, dt) : _smPos;

            var aim = Aim(pivot, pos);
            if (cfg.ChaseEffects) aim *= Quaternion.Euler(_pitch, 0f, 0f);
            _smRot = Quaternion.Slerp(_smRot, aim, CameraMath.DampFactor(cfg.RotationLag, dt));
            rot = _smRot;

            if (cfg.ChaseEffects)
            {   // the road's roughness, scaled by the player's Camera Shake setting; nothing when the vehicle is not moving
                float jolt = _vc != null ? _vc.SuspensionJolt : 0f;
                float amp = (0.004f + 0.02f * jolt) * Mathf.Clamp01(speed / 15f) * ARO.Core.RuntimeSettings.CameraShake;
                pos += new Vector3(Mathf.PerlinNoise(Time.time * 18f, 0f) - 0.5f, Mathf.PerlinNoise(0f, Time.time * 21f) - 0.5f, 0f) * (amp * 2f);
            }
        }

        static Quaternion Aim(Vector3 lookAt, Vector3 cameraPos)
        {
            var d = lookAt - cameraPos;
            return d.sqrMagnitude < 1e-6f ? Quaternion.identity : Quaternion.LookRotation(d, Vector3.up);
        }

        /// <summary>Sweep a sphere from the vehicle towards the camera; anything that is not the vehicle itself pulls the camera in (never closer than the view's floor).</summary>
        Vector3 PulledIn(CameraViewConfig cfg, Vector3 pivot, Vector3 wanted, float dt)
        {
            var d = wanted - pivot; float dist = d.magnitude;
            if (dist < 0.01f) return wanted;
            var dir = d / dist;
            int n = Physics.SphereCastNonAlloc(pivot, cfg.Radius, dir, _hits, dist, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            float hit = -1f;
            for (int i = 0; i < n; i++)
            {
                var col = _hits[i].collider;
                if (col == null || col.transform.IsChildOf(target)) continue;   // the player's own body, cab and wheels never count
                if (hit < 0f || _hits[i].distance < hit) hit = _hits[i].distance;
            }
            float allowed = CameraMath.PullIn(dist, hit, cfg.Radius, cfg.MinPullIn);
            _pull = CameraMath.SmoothPull(_pull, allowed / dist, dt);
            return pivot + dir * (dist * _pull);
        }
    }
}
