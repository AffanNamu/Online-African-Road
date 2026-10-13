using UnityEngine;

namespace ARO.Vehicles
{
    /// <summary>
    /// The contract between a production vehicle model and the driving code. An artist builds a prefab
    /// (body, cab, glass, trailer, mirrors, interior) with WheelColliders and wheel meshes assigned here;
    /// no gameplay code changes. Trucks, buses and vans all use this same rig.
    /// </summary>
    public class VehicleRig : MonoBehaviour
    {
        public WheelAxle[] axles;
        public Transform centerOfMass, cockpitCameraAnchor, exhaust;
        [Header("Cameras (all optional: a vehicle without them uses the per-category placeholder positions)")]
        [Tooltip("Front bumper / low front camera position, looking forward.")]
        public Transform bumperCameraAnchor;
        [Tooltip("The driver at the wheel. Without a complete DriverRig the Driver cabin view is unavailable.")]
        public DriverRig driverRig;
        [Header("Lights (emissive renderers use _EmissionColor; Lights are optional real lights)")]
        public Light[] headlightLights;
        public Renderer[] headlightEmitters, brakeEmitters, reverseEmitters, indicatorLeftEmitters, indicatorRightEmitters;
        [Header("Surface")]
        public Renderer[] dirtyRenderers;   // body panels that receive road-grime tint
    }
}
