using UnityEngine;

namespace ARO.Input
{
    /// <summary>Device-agnostic driver intent. Gameplay reads this, never a keyboard or touch API.</summary>
    public struct VehicleInputState
    {
        public float Steer;      // -1 (left) .. 1 (right)
        public float Throttle;   // 0..1
        public float Brake;      // 0..1
        public bool Handbrake;
        public bool HornHeld;
        public bool LightsToggled;       // edge: true for one frame
        public bool IndicatorLeftToggled;
        public bool IndicatorRightToggled;
        public bool CameraToggled;       // edge: next camera view (C, gamepad Y, touch button)
        public bool CameraPrevious;      // edge: previous camera view (Shift+C)
        public int CameraSelect;         // edge: 1..8 = that camera view (number keys), 0 = none
    }

    /// <summary>A driver's request for another camera view. Local presentation only: it is raised on the owner's machine and never networked.</summary>
    public readonly struct CameraRequest
    {
        /// <summary>1..8 for a direct choice, 0 for a step.</summary>
        public readonly int Select;
        /// <summary>+1 next, -1 previous (when Select is 0).</summary>
        public readonly int Step;
        public CameraRequest(int select, int step) { Select = select; Step = step; }
        public static CameraRequest Next => new CameraRequest(0, 1);
        public static CameraRequest Previous => new CameraRequest(0, -1);
        public static CameraRequest Direct(int number) => new CameraRequest(number, 0);
    }

    public interface IVehicleInput
    {
        VehicleInputState Read();
    }
}
