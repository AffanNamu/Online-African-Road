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
        [Header("Lights (emissive renderers use _EmissionColor; Lights are optional real lights)")]
        public Light[] headlightLights;
        public Renderer[] headlightEmitters, brakeEmitters, reverseEmitters, indicatorLeftEmitters, indicatorRightEmitters;
        [Header("Surface")]
        public Renderer[] dirtyRenderers;   // body panels that receive road-grime tint
    }
}
