using ARO.Input;
using ARO.NetCore;
using ARO.Vehicles;
using UnityEngine;

namespace ARO.Game
{
    /// <summary>
    /// Smoke pages only (?smoke=drive&autopilot=...): steers, accelerates and brakes the REAL vehicle along a path of an E5 test route, so CI can prove in the running game (real colliders, real wheel physics)
    /// that a car-sized truck, a medium truck and a bus can drive the roundabouts, the flyover deck, the road under it and the ramps. It is an IVehicleInput like the keyboard; nothing in the live game creates it.
    /// The log lines (prefix [Autopilot]) are what tools/webgl-e5.js reads.
    /// </summary>
    public sealed class SmokeAutopilot : MonoBehaviour, IVehicleInput
    {
        PathFollower _f; VehicleController _v; AutoPath _path; Rigidbody _rb; RouteDefinition _route; float _wheelbase;
        float _t0 = -1f, _nextLog, _offSince = -1f, _minY = float.MaxValue, _maxY = float.MinValue, _stillSince = -1f, _maxErr;
        bool _finished;
        public bool Finished => _finished;

        public void Init(VehicleController v, AutoPath path, DesignVehicle dv, RouteDefinition route)
        {
            _v = v; _path = path; _route = route; _rb = v.GetComponent<Rigidbody>(); _wheelbase = dv.Wheelbase;
            float maxSteer = v.definition != null ? v.definition.stats.maxSteerDeg : dv.MaxSteerDeg;
            _f = new PathFollower(path.Points, dv.Wheelbase, maxSteer, path.CruiseKmh);
            Debug.Log($"[Autopilot] {path.Name} with {(v.definition != null ? v.definition.id : dv.Name)}: {path.Points.Count} points, {path.Length:F0} m, cruise {path.CruiseKmh:F0} km/h");
        }

        public VehicleInputState Read()
        {
            var s = new VehicleInputState();
            if (_f == null || _rb == null) return s;
            if (_finished) { s.Brake = 1f; s.Handbrake = true; return s; }
            if (_rb.constraints == RigidbodyConstraints.FreezeAll) return s;       // still held while the ground is built
            if (_t0 < 0f) _t0 = Time.time;
            var t = transform; var fwd = Vector3.ProjectOnPlane(t.forward, Vector3.up).normalized;
            var rear = t.position - fwd * (_wheelbase * 0.5f);                     // the vehicle's root sits half a wheelbase from the rear axle
            float heading = Mathf.Atan2(fwd.z, fwd.x), speedMs = Vector3.Dot(_rb.linearVelocity, fwd);
            float maxKmh = _v.definition != null ? _v.definition.maxSpeedKmh : 100f;
            float authority = Mathf.Lerp(1f, 0.25f, Mathf.Abs(speedMs) * 3.6f / maxKmh);       // the controller shrinks the steering angle with speed
            _f.Control(rear.x, rear.z, heading, speedMs, out float steer, out float throttle, out float brake, authority);
            s.Steer = steer; s.Throttle = throttle; s.Brake = brake;
            Track(t.position, speedMs);
            return s;
        }

        void Track(Vector3 p, float speedMs)
        {
            _minY = Mathf.Min(_minY, p.y); _maxY = Mathf.Max(_maxY, p.y); _maxErr = Mathf.Max(_maxErr, Mathf.Abs(_f.CrossTrack));
            if (_f.Done) { Finish(true, p); return; }
            // failing: more than 6 m from the path for 3 s, or no progress for 25 s of game time
            if (Mathf.Abs(_f.CrossTrack) > 6f) { if (_offSince < 0f) _offSince = Time.time; else if (Time.time - _offSince > 3f) { Finish(false, p, "left the path (" + _f.CrossTrack.ToString("F1") + " m off)"); return; } } else _offSince = -1f;
            if (speedMs < 0.3f && Time.time - _t0 > 6f) { if (_stillSince < 0f) _stillSince = Time.time; else if (Time.time - _stillSince > 25f) { Finish(false, p, "stuck at " + (_f.Progress * 100f).ToString("F0") + " percent"); return; } } else _stillSince = -1f;
            if (Time.unscaledTime >= _nextLog)
            {
                _nextLog = Time.unscaledTime + 2f;
                float ground = _route != null ? _route.GroundY(p.x, p.z, p.y) : 0f;
                Debug.Log($"[Autopilot] progress {_f.Progress * 100f:F0}% pos=({p.x:F1},{p.y:F2},{p.z:F1}) ground={ground:F2} kmh={speedMs * 3.6f:F1} err={_f.CrossTrack:F2} ");
            }
        }

        void Finish(bool ok, Vector3 p, string why = "")
        {
            if (_finished) return;
            _finished = true;
            float seconds = Time.time - _t0;
            Debug.Log($"[Autopilot] {(ok ? "done" : "FAILED " + why)} path={_path.Name} reached={ok.ToString().ToLowerInvariant()} maxErr={_maxErr:F2} minY={_minY:F2} maxY={_maxY:F2} seconds={seconds:F1} end=({p.x:F1},{p.y:F2},{p.z:F1})");
        }
    }
}
