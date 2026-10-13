using UnityEngine;

namespace ARO.Vehicles
{
    /// <summary>
    /// The contract for the Driver cabin camera (view 2): a driver at the wheel that the camera can look at. A vehicle prefab adds this component under its
    /// <see cref="VehicleRig"/> and fills every slot. The view stays UNAVAILABLE (greyed out, an explicit message when chosen) until
    /// <see cref="IsComplete"/> is true; there is no fallback that fakes it. No vehicle has these assets yet (docs/PLACEHOLDERS.md P-92).
    /// </summary>
    public class DriverRig : MonoBehaviour
    {
        [Tooltip("Where the camera sits to see the driver (usually beside and slightly behind the seat, looking at the driver's upper body and the wheel).")]
        public Transform cabinCameraAnchor;
        [Tooltip("Root of the driver model (mesh and skeleton).")]
        public Transform driverModel;
        [Tooltip("The steering wheel the driver's hands follow.")]
        public Transform steeringWheel;
        [Tooltip("Animator with a steering/idle controller. It is given the Steer parameter (-1..1) once the steering animation feed is implemented.")]
        public Animator animator;
        public string steerParameter = "Steer";

        /// <summary>The first slot still empty, or null when the rig is complete. Shown in the log and the HUD message.</summary>
        public string FirstMissing =>
            cabinCameraAnchor == null ? "a cabin camera anchor"
            : driverModel == null ? "a driver model"
            : steeringWheel == null ? "a steering wheel"
            : animator == null || animator.runtimeAnimatorController == null ? "a steering animation"
            : null;

        public bool IsComplete => FirstMissing == null;
    }
}
