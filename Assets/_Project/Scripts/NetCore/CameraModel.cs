using System;
using System.Collections.Generic;

namespace ARO.NetCore
{
    // ===================================================================================================== views
    /// <summary>The eight driving camera views. The order is the direct-select number: 1 = Cockpit ... 8 = FrontBumper.</summary>
    public enum CameraView { Cockpit = 0, DriverCabin = 1, RearChase = 2, FarChase = 3, Left = 4, Right = 5, Overhead = 6, FrontBumper = 7 }

    public enum CameraSelectResult { Changed, Unchanged, Unavailable, Invalid }

    public static class CameraViews
    {
        public const int Count = 8;
        /// <summary>The view the camera starts in and falls back to.</summary>
        public const CameraView Default = CameraView.RearChase;

        /// <summary>1..8 (the number keys) to a view; false outside that range.</summary>
        public static bool TryFromDigit(int digit, out CameraView view)
        {
            view = Default;
            if (digit < 1 || digit > Count) return false;
            view = (CameraView)(digit - 1); return true;
        }
        public static int Digit(CameraView v) => (int)v + 1;
    }

    /// <summary>
    /// How one view places the camera. Rigid views (cockpit, driver cabin, bumper) are fixed to the vehicle: no lag, no obstruction test, no chase
    /// effects. The others follow the vehicle's heading with a damped lag, may be pulled in by obstacles, and live in "yaw space": the vehicle's position
    /// and heading only, so pitching and rolling over bumps do not tilt the view.
    /// </summary>
    public sealed class CameraViewConfig
    {
        public CameraView View;
        public string Name = "", Hint = "";
        public bool Available = true; public string UnavailableReason = "";
        public bool Rigid;
        /// <summary>Camera offset: vehicle-local for rigid views, yaw-space for the others (metres, +x right, +y up, +z forward).</summary>
        public float X, Y, Z;
        /// <summary>The point the camera looks at, above the vehicle origin and ahead of it (yaw-space). Unused by rigid views.</summary>
        public float LookY, LookZ;
        /// <summary>Rigid views: extra downward pitch in degrees.</summary>
        public float PitchDeg;
        public float Fov = 55f, FovSpeedBoost;
        /// <summary>Per-second rates of the damped follow (0 for rigid views).</summary>
        public float PositionLag, RotationLag;
        /// <summary>Brake/accelerate pitch and the Camera Shake setting apply (rear chase only).</summary>
        public bool ChaseEffects;
        public bool CollisionAware;
        /// <summary>Radius of the sphere swept from the vehicle to the camera, and the closest the camera may be pulled in to.</summary>
        public float Radius = 0.35f, MinPullIn;
        /// <summary>Follow-distance scale range (mouse wheel). Equal values mean the view is not zoomable.</summary>
        public float MinZoom = 1f, MaxZoom = 1f;

        public bool Zoomable => MaxZoom > MinZoom + 1e-4f;
        /// <summary>Distance from the vehicle origin at scale 1, for validation.</summary>
        public float Distance => (float)Math.Sqrt(X * X + Y * Y + Z * Z);
        public CameraViewConfig Clone() => (CameraViewConfig)MemberwiseClone();
    }

    /// <summary>The eight view configurations of one vehicle category. Built by <see cref="CameraProfiles"/>; never shared between vehicles.</summary>
    public sealed class VehicleCameraProfile
    {
        public string Category = "truck";
        readonly CameraViewConfig[] _views = new CameraViewConfig[CameraViews.Count];

        public CameraViewConfig this[CameraView v] { get => _views[(int)v]; set => _views[(int)v] = value; }
        public IReadOnlyList<CameraViewConfig> All => _views;
        public bool IsAvailable(CameraView v) { var c = _views[(int)v]; return c != null && c.Available; }
        public int AvailableCount { get { int n = 0; foreach (var c in _views) if (c != null && c.Available) n++; return n; } }
    }

    // ===================================================================================================== profiles
    public static class CameraProfiles
    {
        public const string DriverCabinMissing = "No driver model, rig and steering animation exist for this vehicle yet (docs/PLACEHOLDERS.md P-92).";

        /// <summary>
        /// Builds the eight views for a vehicle category ("truck", "bus", "van"; anything else gets the truck set).
        /// <paramref name="followScale"/> scales the horizontal distances of the non-rigid views and <paramref name="heightScale"/> their heights, per vehicle model.
        /// <paramref name="driverCabin"/> is true only when the vehicle's rig has a complete driver (model, steering wheel, animation, anchor): see DriverRig.
        /// </summary>
        public static VehicleCameraProfile Build(string category, float followScale = 1f, float heightScale = 1f, bool driverCabin = false, string cabinReason = null)
        {
            string cat = (category ?? "truck").Trim().ToLowerInvariant();
            if (cat != "bus" && cat != "van") cat = "truck";
            followScale = Clamp(followScale, 0.75f, 2f); heightScale = Clamp(heightScale, 0.75f, 2f);
            bool bus = cat == "bus", van = cat == "van";
            float len = bus ? 1.35f : van ? 0.85f : 1f;       // how much longer/shorter than the reference truck

            var p = new VehicleCameraProfile { Category = cat };

            // Seat positions are for the placeholder bodies (TruckFactory); a production model supplies VehicleRig anchors instead.
            p[CameraView.Cockpit] = new CameraViewConfig
            {
                View = CameraView.Cockpit, Name = "Cockpit", Hint = "Driver's eye view. Limited: no dashboard, wheel or mirrors until a cabin interior exists",
                Rigid = true, X = bus ? -0.75f : -0.5f, Y = bus ? 2.55f : 2.5f, Z = bus ? 3.7f : 2.6f, PitchDeg = 0f, Fov = 72f
            };
            p[CameraView.DriverCabin] = new CameraViewConfig
            {
                View = CameraView.DriverCabin, Name = "Driver cabin", Hint = "The driver at the wheel",
                Rigid = true, X = bus ? 0.2f : 0.3f, Y = bus ? 2.9f : 2.85f, Z = bus ? 2.6f : 1.7f, PitchDeg = 8f, Fov = 62f,
                Available = driverCabin, UnavailableReason = driverCabin ? "" : (string.IsNullOrEmpty(cabinReason) ? DriverCabinMissing : cabinReason)
            };
            p[CameraView.RearChase] = new CameraViewConfig
            {
                View = CameraView.RearChase, Name = "Chase", Hint = "Behind the vehicle. Mouse wheel changes the distance",
                Y = 4.6f * heightScale, Z = -13f * followScale * len, LookY = 2.2f * heightScale, Fov = 55f, FovSpeedBoost = 8f,
                PositionLag = 7f, RotationLag = 6f, ChaseEffects = true, CollisionAware = true, Radius = 0.35f, MinPullIn = 7f * followScale * len,
                MinZoom = 8f / 13f, MaxZoom = 24f / 13f
            };
            p[CameraView.FarChase] = new CameraViewConfig
            {
                View = CameraView.FarChase, Name = "Far chase", Hint = "Wide cinematic view from far behind. Mouse wheel changes the distance",
                Y = 8f * heightScale, Z = -28f * followScale * len, LookY = 2.6f * heightScale, Fov = 44f, FovSpeedBoost = 3f,
                PositionLag = 3.2f, RotationLag = 3f, CollisionAware = true, Radius = 0.5f, MinPullIn = 9f * followScale * len,
                MinZoom = 0.5f, MaxZoom = 1.6f
            };
            p[CameraView.Left] = Side(CameraView.Left, "Left side", -1f, len, followScale, heightScale);
            p[CameraView.Right] = Side(CameraView.Right, "Right side", 1f, len, followScale, heightScale);
            p[CameraView.Overhead] = new CameraViewConfig
            {
                View = CameraView.Overhead, Name = "Overhead", Hint = "Looking down from above",
                Y = 30f * heightScale, Z = -5f * followScale, LookZ = 5f, Fov = 50f,
                PositionLag = 5f, RotationLag = 5f, CollisionAware = true, Radius = 0.5f, MinPullIn = 8f
            };
            p[CameraView.FrontBumper] = new CameraViewConfig
            {
                View = CameraView.FrontBumper, Name = "Bumper", Hint = "Low, at the front of the vehicle",
                Rigid = true, X = 0f, Y = 0.9f, Z = bus ? 4.9f : van ? 2.9f : 3.6f, PitchDeg = 2f, Fov = 76f
            };
            return p;
        }

        static CameraViewConfig Side(CameraView v, string name, float sign, float len, float followScale, float heightScale) => new CameraViewConfig
        {
            View = v, Name = name, Hint = "From the " + (sign < 0 ? "left" : "right") + " of the vehicle",
            X = sign * 9.5f * followScale * (len > 1f ? 1.2f : 1f), Y = 3.4f * heightScale, LookY = 1.7f * heightScale, LookZ = 0.4f, Fov = 50f,
            PositionLag = 6f, RotationLag = 7f, CollisionAware = true, Radius = 0.4f, MinPullIn = 4f * followScale
        };

        /// <summary>Problems with a profile; empty when every view is usable. Run in tests and logged when a vehicle is bound.</summary>
        public static IReadOnlyList<string> Validate(VehicleCameraProfile p)
        {
            var bad = new List<string>();
            if (p == null) { bad.Add("no profile"); return bad; }
            var names = new HashSet<string>();
            for (int i = 0; i < CameraViews.Count; i++)
            {
                var c = p[(CameraView)i];
                if (c == null) { bad.Add($"view {i + 1} has no configuration"); continue; }
                string w = $"{c.View}: ";
                if (c.View != (CameraView)i) bad.Add(w + "stored under the wrong slot");
                if (string.IsNullOrWhiteSpace(c.Name) || !names.Add(c.Name)) bad.Add(w + "name is empty or repeated");
                if (!(c.Fov >= 20f && c.Fov <= 110f)) bad.Add(w + "field of view outside 20..110");
                if (c.FovSpeedBoost < 0f || c.FovSpeedBoost > 20f) bad.Add(w + "speed FOV boost outside 0..20");
                if (c.MinZoom <= 0f || c.MaxZoom < c.MinZoom) bad.Add(w + "zoom range is not valid");
                if (!c.Available && string.IsNullOrWhiteSpace(c.UnavailableReason)) bad.Add(w + "unavailable without a reason");
                if (c.Rigid)
                {
                    if (c.PositionLag != 0f || c.RotationLag != 0f) bad.Add(w + "a rigid view must not lag");
                    if (c.ChaseEffects || c.CollisionAware) bad.Add(w + "a rigid view must not use chase effects or the obstruction test");
                    if (c.Zoomable) bad.Add(w + "a rigid view cannot zoom");
                    if (c.Distance > 8f) bad.Add(w + "a rigid view must stay on the vehicle");
                }
                else
                {
                    if (c.PositionLag <= 0f || c.RotationLag <= 0f) bad.Add(w + "a following view needs lag rates above 0");
                    if (c.Distance * c.MinZoom < 5f) bad.Add(w + "too close to the vehicle (could sit inside it)");
                    if (c.CollisionAware && (c.Radius <= 0f || c.Radius > 1f)) bad.Add(w + "obstruction radius outside 0..1");
                    if (c.CollisionAware && c.MinPullIn < 3f) bad.Add(w + "obstruction floor under 3 m could pull the camera into the vehicle");
                    if (c.ChaseEffects && c.View != CameraView.RearChase) bad.Add(w + "chase effects belong to the rear chase view only");
                }
            }
            return bad;
        }

        static float Clamp(float v, float lo, float hi) => float.IsNaN(v) ? 1f : v < lo ? lo : v > hi ? hi : v;
    }

    // ===================================================================================================== selection
    /// <summary>
    /// The player's current view and the rules for changing it: next / previous (skipping unavailable views, wrapping round), direct 1..8, and the
    /// fallback when a different vehicle does not offer the current view. Local presentation state only: never part of the networked vehicle state.
    /// </summary>
    public sealed class CameraSelector
    {
        VehicleCameraProfile _p;
        public CameraView Current { get; private set; } = CameraViews.Default;
        public VehicleCameraProfile Profile => _p;
        /// <summary>Why the last request answered Unavailable.</summary>
        public string Reason { get; private set; } = "";

        /// <summary>Use another vehicle's profile. If it does not offer the current view the selection falls back (Changed).</summary>
        public CameraSelectResult SetProfile(VehicleCameraProfile p)
        {
            _p = p;
            if (p == null || p.IsAvailable(Current)) return CameraSelectResult.Unchanged;
            Current = p.IsAvailable(CameraViews.Default) ? CameraViews.Default : FirstAvailable(p);
            return CameraSelectResult.Changed;
        }

        public CameraSelectResult Select(CameraView v)
        {
            Reason = "";
            if (_p == null || !Enum.IsDefined(typeof(CameraView), v)) return CameraSelectResult.Invalid;
            if (!_p.IsAvailable(v)) { Reason = _p[v] != null ? _p[v].UnavailableReason : ""; return CameraSelectResult.Unavailable; }
            if (v == Current) return CameraSelectResult.Unchanged;
            Current = v; return CameraSelectResult.Changed;
        }

        public CameraSelectResult SelectDigit(int digit) => CameraViews.TryFromDigit(digit, out var v) ? Select(v) : CameraSelectResult.Invalid;
        public CameraSelectResult Next() => Step(1);
        public CameraSelectResult Previous() => Step(-1);

        /// <summary>One step through the available views, wrapping at both ends. Unchanged when nothing else is available.</summary>
        public CameraSelectResult Step(int direction)
        {
            Reason = "";
            if (_p == null) return CameraSelectResult.Invalid;
            if (direction == 0) return CameraSelectResult.Unchanged;
            int dir = direction > 0 ? 1 : -1, i = (int)Current;
            for (int n = 0; n < CameraViews.Count; n++)
            {
                i = (i + dir + CameraViews.Count) % CameraViews.Count;
                if ((CameraView)i == Current) break;
                if (_p.IsAvailable((CameraView)i)) { Current = (CameraView)i; return CameraSelectResult.Changed; }
            }
            return CameraSelectResult.Unchanged;
        }

        static CameraView FirstAvailable(VehicleCameraProfile p)
        {
            for (int i = 0; i < CameraViews.Count; i++) if (p.IsAvailable((CameraView)i)) return (CameraView)i;
            return CameraViews.Default;
        }
    }

    // ===================================================================================================== maths
    /// <summary>The camera's arithmetic, apart from the engine so the numbers can be tested. Every function is safe for a bad or huge frame time.</summary>
    public static class CameraMath
    {
        public const float BlendSeconds = 0.45f, MaxStep = 0.1f;

        /// <summary>The frame time the camera uses: never negative, never NaN, never more than 0.1 s (a stall must not fling the camera).</summary>
        public static float StepTime(float dt) => float.IsNaN(dt) || dt < 0f ? 0f : dt > MaxStep ? MaxStep : dt;

        /// <summary>Fraction of the remaining distance covered this frame by an exponential follow; frame-rate independent and never above 1.</summary>
        public static float DampFactor(float rate, float dt) => 1f - (float)Math.Exp(-Math.Max(0f, rate) * StepTime(dt));

        public static float Damp(float current, float target, float rate, float dt) => current + (target - current) * DampFactor(rate, dt);

        /// <summary>Smooth ease 0..1 for the transition between two views.</summary>
        public static float Ease(float t) { t = t < 0f ? 0f : t > 1f ? 1f : t; return t * t * (3f - 2f * t); }

        /// <summary>Advance a 0..1 transition by one frame.</summary>
        public static float BlendProgress(float t, float dt, float seconds = BlendSeconds) => Math.Min(1f, Math.Max(0f, t) + StepTime(dt) / Math.Max(0.05f, seconds));

        /// <summary>
        /// How far from the vehicle the camera may sit when something is in the way. <paramref name="hitDistance"/> is where the sphere sweep from the vehicle
        /// first touches something that is not the vehicle (negative or NaN = nothing). The result is never above what was asked for, and never below the
        /// view's floor, so the camera cannot be dragged into the vehicle.
        /// </summary>
        public static float PullIn(float desired, float hitDistance, float radius, float minPullIn)
        {
            if (!(desired > 0f)) return 0f;
            if (float.IsNaN(hitDistance) || hitDistance < 0f) return desired;
            float allowed = hitDistance - Math.Max(0f, radius) - 0.05f;
            float floor = Math.Min(Math.Max(0f, minPullIn), desired);
            return Math.Min(desired, Math.Max(allowed, floor));
        }

        /// <summary>Ratio 0..1 of the wanted distance that is kept: pulls in quickly (so the view is never inside a wall) and relaxes out slowly.</summary>
        public static float SmoothPull(float current, float target, float dt, float inRate = 40f, float outRate = 2.5f)
        {
            target = target < 0.05f ? 0.05f : target > 1f ? 1f : target;
            current = current < 0.05f ? 0.05f : current > 1f ? 1f : current;
            float v = Damp(current, target, target < current ? inRate : outRate, dt);
            return v < 0.05f ? 0.05f : v > 1f ? 1f : v;
        }

        public static float ClampZoom(float zoom, CameraViewConfig c) => float.IsNaN(zoom) ? 1f : zoom < c.MinZoom ? c.MinZoom : zoom > c.MaxZoom ? c.MaxZoom : zoom;

        /// <summary>One mouse-wheel notch: scrolling up moves the camera closer, as before.</summary>
        public static float WheelZoom(float zoom, float wheel, CameraViewConfig c) =>
            !c.Zoomable || Math.Abs(wheel) <= 0.01f ? ClampZoom(zoom, c) : ClampZoom(zoom - Math.Sign(wheel) * 0.08f, c);

        public static float Fov(CameraViewConfig c, float speedMetresPerSecond)
        {
            float k = speedMetresPerSecond <= 0f || float.IsNaN(speedMetresPerSecond) ? 0f : Math.Min(1f, speedMetresPerSecond / 28f);
            return c.Fov + c.FovSpeedBoost * k;
        }
    }
}
