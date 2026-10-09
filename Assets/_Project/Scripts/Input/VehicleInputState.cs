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
        public bool CameraToggled;
    }

    public interface IVehicleInput
    {
        VehicleInputState Read();
    }
}
