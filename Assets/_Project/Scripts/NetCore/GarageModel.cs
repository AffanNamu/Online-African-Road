using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace ARO.NetCore
{
    // Pure rules for the Garage: the inspection camera, the vehicle states, the customisation slots. No Unity types, so every rule is unit tested.

    public enum ViewPreset { Default, Front, Rear, Left, Right, Top, Wheel, Interior }

    /// <summary>How far the camera may go for one vehicle (derived from its size by <see cref="ShowroomFraming"/>).</summary>
    public struct OrbitLimits { public float MinDistance, MaxDistance, MinPitch, MaxPitch, PanRadius; }

    /// <summary>Camera framing for a vehicle: distances scale with its size, so a bus and a motorcycle both fill the view.</summary>
    public static class ShowroomFraming
    {
        public static float BaseDistance(float sizeX, float sizeY, float sizeZ)
        {
            float biggest = Math.Max(Math.Max(sizeX, sizeY), Math.Max(sizeZ, 0.5f));
            return biggest * 1.55f + 1.5f;
        }
        public static OrbitLimits LimitsFor(float sizeX, float sizeY, float sizeZ)
        {
            float biggest = Math.Max(Math.Max(sizeX, sizeY), Math.Max(sizeZ, 0.5f)), b = BaseDistance(sizeX, sizeY, sizeZ);
            return new OrbitLimits { MinDistance = Math.Max(1.2f, biggest * 0.45f), MaxDistance = b * 2.2f, MinPitch = -4f, MaxPitch = 86f, PanRadius = biggest * 0.5f };
        }
    }

    /// <summary>
    /// The reusable vehicle inspection camera: orbit, zoom, pan, predefined viewpoints, smooth interpolation, limits and reset. It holds a TARGET state and a
    /// CURRENT state; input changes the target, <see cref="Step"/> moves the current state towards it (frame-rate independent), so every movement is smooth.
    /// Yaw 0 puts the camera in front of a vehicle that faces +Z; positive yaw turns the camera towards the vehicle's right side (+X).
    /// </summary>
    public sealed class OrbitCameraModel
    {
        public float Yaw, Pitch, Distance, PanX, PanY, PanZ;                         // current
        public float TargetYaw, TargetPitch, TargetDistance, TargetPanX, TargetPanY, TargetPanZ;
        public float CenterX, CenterY, CenterZ;                                      // the point of the vehicle the camera looks at (its bounds centre)
        public OrbitLimits Limits;
        public bool AutoRotate; public float AutoRotateDegPerSec = 18f;
        public float HomeYaw = 38f, HomePitch = 12f, HomeDistance = 10f;
        public float Smoothing = 9f;                                                 // larger = snappier
        public float ZoomSensitivity = 0.12f, OrbitSensitivity = 0.28f, PanSensitivity = 0.0035f;

        public OrbitCameraModel(OrbitLimits limits, float homeDistance) { Limits = limits; HomeDistance = Clamp(homeDistance, limits.MinDistance, limits.MaxDistance); Reset(true); }

        static float Clamp(float v, float lo, float hi) => v < lo ? lo : v > hi ? hi : v;
        public static float WrapDeg(float a) { a %= 360f; if (a > 180f) a -= 360f; if (a <= -180f) a += 360f; return a; }

        /// <summary>One-finger / left-drag: dx, dy in pixels.</summary>
        public void Orbit(float dxPixels, float dyPixels)
        {
            TargetYaw = WrapDeg(TargetYaw - dxPixels * OrbitSensitivity); TargetPitch = Clamp(TargetPitch + dyPixels * OrbitSensitivity, Limits.MinPitch, Limits.MaxPitch); AutoRotate = false;
        }
        /// <summary>Wheel or pinch: positive = closer. Multiplicative, so each notch feels the same at any distance.</summary>
        public void Zoom(float amount) { TargetDistance = Clamp(TargetDistance * (float)Math.Exp(-amount * ZoomSensitivity), Limits.MinDistance, Limits.MaxDistance); }
        /// <summary>Two-finger / right-drag pan in screen pixels; the target point may leave the vehicle's centre by at most PanRadius.</summary>
        public void Pan(float dxPixels, float dyPixels)
        {
            float k = PanSensitivity * TargetDistance, yaw = TargetYaw * Deg2Rad;
            float rx = (float)Math.Cos(yaw), rz = -(float)Math.Sin(yaw);               // the camera's right vector on the ground plane
            TargetPanX -= dxPixels * k * rx; TargetPanZ -= dxPixels * k * rz; TargetPanY = Clamp(TargetPanY + dyPixels * k, -Limits.PanRadius, Limits.PanRadius);
            float len = (float)Math.Sqrt(TargetPanX * TargetPanX + TargetPanZ * TargetPanZ);
            if (len > Limits.PanRadius) { float s = Limits.PanRadius / len; TargetPanX *= s; TargetPanZ *= s; }
        }
        const float Deg2Rad = (float)(Math.PI / 180.0);

        public void SetView(ViewPreset p, float wheelX = 0, float wheelY = 0, float wheelZ = 0)
        {
            AutoRotate = false; TargetPanX = TargetPanY = TargetPanZ = 0; float d = HomeDistance;
            switch (p)
            {
                case ViewPreset.Front: TargetYaw = 0f; TargetPitch = 6f; TargetDistance = d; break;
                case ViewPreset.Rear: TargetYaw = 180f; TargetPitch = 6f; TargetDistance = d; break;
                case ViewPreset.Left: TargetYaw = -90f; TargetPitch = 5f; TargetDistance = d; break;
                case ViewPreset.Right: TargetYaw = 90f; TargetPitch = 5f; TargetDistance = d; break;
                case ViewPreset.Top: TargetYaw = HomeYaw; TargetPitch = Limits.MaxPitch; TargetDistance = d * 1.05f; break;
                case ViewPreset.Wheel:
                    TargetYaw = wheelX >= 0 ? 62f : -62f; TargetPitch = 8f; TargetDistance = Limits.MinDistance * 1.6f;
                    TargetPanX = wheelX - CenterX; TargetPanY = wheelY - CenterY; TargetPanZ = wheelZ - CenterZ; ClampPanTarget(); break;
                default: TargetYaw = HomeYaw; TargetPitch = HomePitch; TargetDistance = d; break;
            }
            TargetDistance = Clamp(TargetDistance, Limits.MinDistance, Limits.MaxDistance);
        }
        void ClampPanTarget() { float len = (float)Math.Sqrt(TargetPanX * TargetPanX + TargetPanZ * TargetPanZ); if (len > Limits.PanRadius * 2f) { float s = Limits.PanRadius * 2f / len; TargetPanX *= s; TargetPanZ *= s; } TargetPanY = Clamp(TargetPanY, -Limits.PanRadius, Limits.PanRadius); }

        /// <summary>Back to the default view. With snap = true the current state jumps too (first frame), otherwise it glides.</summary>
        public void Reset(bool snap = false)
        {
            AutoRotate = false; SetView(ViewPreset.Default);
            if (snap) { Yaw = TargetYaw; Pitch = TargetPitch; Distance = TargetDistance; PanX = TargetPanX; PanY = TargetPanY; PanZ = TargetPanZ; }
        }

        /// <summary>Moves the current state towards the target. Safe for any dt (0, huge, negative never produce NaN or overshoot).</summary>
        public void Step(float dt)
        {
            if (!(dt > 0f)) return;
            if (dt > 0.25f) dt = 0.25f;
            if (AutoRotate) TargetYaw = WrapDeg(TargetYaw + AutoRotateDegPerSec * dt);
            float a = 1f - (float)Math.Exp(-Smoothing * dt);
            float dy = WrapDeg(TargetYaw - Yaw); Yaw = WrapDeg(Yaw + dy * a);                       // the short way round
            Pitch += (TargetPitch - Pitch) * a; Distance += (TargetDistance - Distance) * a;
            PanX += (TargetPanX - PanX) * a; PanY += (TargetPanY - PanY) * a; PanZ += (TargetPanZ - PanZ) * a;
            Distance = Clamp(Distance, Limits.MinDistance, Limits.MaxDistance); Pitch = Clamp(Pitch, Limits.MinPitch, Limits.MaxPitch);
        }

        public bool Settled => Math.Abs(WrapDeg(TargetYaw - Yaw)) < 0.05f && Math.Abs(TargetPitch - Pitch) < 0.05f && Math.Abs(TargetDistance - Distance) < 0.005f && !AutoRotate;

        /// <summary>World position of the camera and the point it looks at (vehicle space, +Z forward, +Y up).</summary>
        public void Pose(out float px, out float py, out float pz, out float lx, out float ly, out float lz)
        {
            lx = CenterX + PanX; ly = CenterY + PanY; lz = CenterZ + PanZ;
            float yaw = Yaw * Deg2Rad, pit = Pitch * Deg2Rad, c = (float)Math.Cos(pit);
            px = lx + Distance * c * (float)Math.Sin(yaw); py = ly + Distance * (float)Math.Sin(pit); pz = lz + Distance * c * (float)Math.Cos(yaw);
        }
    }

    // ---------------------------------------------------------------------------------------------------------------------------------- garage rules
    public enum GarageSection { Customize, Upgrades, Maintenance, PaintSkins, Accessories, Performance }
    public enum VehicleTag { Active, Ready, Damaged, Maintenance, LowFuel, OnJob, Locked }

    public sealed class CustomSlot { public string Key, Label; public bool Available; public string Reason; }

    public static class GarageRules
    {
        /// <summary>The state chip of a vehicle card. The server's status wins; the active vehicle says ACTIVE unless something needs attention.</summary>
        public static VehicleTag TagFor(string serverStatus, bool selected, double conditionPct)
        {
            switch (serverStatus)
            {
                case "destroyed": case "needs_repair": return VehicleTag.Damaged;
                case "low_fuel": return VehicleTag.LowFuel;
                case "on_job": case "on_route": return VehicleTag.OnJob;
            }
            if (conditionPct < 70) return VehicleTag.Maintenance;
            return selected ? VehicleTag.Active : VehicleTag.Ready;
        }
        public static string TagText(VehicleTag t)
        {
            switch (t) { case VehicleTag.Active: return "Active"; case VehicleTag.Damaged: return "Damaged"; case VehicleTag.Maintenance: return "Maintenance"; case VehicleTag.LowFuel: return "Low fuel"; case VehicleTag.OnJob: return "On a job"; case VehicleTag.Locked: return "Locked"; default: return "Ready"; }
        }

        /// <summary>The customisation categories of the mockup. A category is Available only when the game really has the system behind it.</summary>
        public static List<CustomSlot> CustomSlots(bool paintSupported)
        {
            const string soon = "Arrives with the production vehicle models.";
            return new List<CustomSlot>
            {
                new CustomSlot { Key = "paint", Label = "Paint", Available = paintSupported, Reason = paintSupported ? null : "Paint needs a vehicle model that supports it." },
                new CustomSlot { Key = "wheels", Label = "Wheels", Reason = soon }, new CustomSlot { Key = "tires", Label = "Tires", Reason = soon },
                new CustomSlot { Key = "accessories", Label = "Accessories", Reason = soon }, new CustomSlot { Key = "body", Label = "Body Parts", Reason = soon },
                new CustomSlot { Key = "lights", Label = "Lights", Reason = soon }, new CustomSlot { Key = "interior", Label = "Interior", Reason = soon }, new CustomSlot { Key = "decals", Label = "Decals", Reason = soon },
            };
        }

        public static readonly string[] UpgradeCategories = { "Engine", "Transmission", "Brakes", "Suspension", "Fuel System", "Cooling", "Tires", "Aerodynamics" };
        public static string UpgradeReason => "Performance upgrades are not part of the game yet, so no upgrade, price or effect is shown.";

        /// <summary>The button of a skin tile, from the server's own state.</summary>
        public static string SkinAction(string state, long price, int level, long needMore)
        {
            switch (state)
            {
                case "equipped": return "Equipped";
                case "owned": return "Equip";
                case "level_required": return "Level " + level;
                case "insufficient_funds": return "Need " + Naira(needMore) + " more";
                default: return "Buy " + Naira(price);
            }
        }
        public static string Naira(long n) => "₦" + n.ToString("N0", CultureInfo.InvariantCulture);

        /// <summary>Parses "#rrggbb"; anything else is refused (the server only stores that format, and so does the renderer).</summary>
        public static bool TryParseHex(string hex, out float r, out float g, out float b)
        {
            r = g = b = 0;
            if (string.IsNullOrEmpty(hex) || hex.Length != 7 || hex[0] != '#') return false;
            if (!int.TryParse(hex.Substring(1, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int ri) || !int.TryParse(hex.Substring(3, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int gi) || !int.TryParse(hex.Substring(5, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int bi)) return false;
            r = ri / 255f; g = gi / 255f; b = bi / 255f; return true;
        }

        /// <summary>Quote lines of the Maintenance section, all numbers from the server's quote.</summary>
        public static string RepairLabel(bool needed, long cost) => needed ? "Repair " + Naira(cost) : "Nothing to repair";
        public static string RefuelLabel(double liters, long cost) => liters > 0.05 ? "Refuel " + liters.ToString("0") + " L " + Naira(cost) : "Tank full";
    }
}
