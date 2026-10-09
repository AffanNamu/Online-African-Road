using System;
using UnityEngine;

namespace ARO.Vehicles
{
    public enum VehicleCategory { Truck, Bus, Van }

    /// <summary>
    /// Tunable physics/economy numbers. Field names match the `stats` jsonb in
    /// supabase vehicle_definitions so a definition can be built from backend data.
    /// </summary>
    [Serializable]
    public class VehicleStats
    {
        public float massKg = 4200f;
        public float torqueNm = 900f;
        public float[] gears = { 3.9f, 2.3f, 1.5f, 1.0f, 0.8f };
        public float reverseRatio = 3.6f;
        public float finalDrive = 4.1f;
        public float brakeTorqueNm = 4500f;
        public float maxSteerDeg = 32f;
        public float fuelBurnLPerKm = 0.22f;
    }

    /// <summary>One definition per vehicle model. Category-agnostic: trucks, buses and vans share it.</summary>
    [CreateAssetMenu(menuName = "African Roads/Vehicle Definition")]
    public class VehicleDefinition : ScriptableObject
    {
        public string id = "truck_light_01";   // matches vehicle_definitions.id
        public string displayName = "Savanna 4x2 Light Truck";
        public VehicleCategory category = VehicleCategory.Truck;
        public float maxSpeedKmh = 110f;
        public float fuelCapacityL = 120f;
        public int cargoCapacityKg = 4000;
        public int passengerCapacity = 0;
        public float idleRpm = 800f;
        public float maxRpm = 3200f;
        public float upshiftRpm = 2700f;
        public float downshiftRpm = 1400f;
        public VehicleStats stats = new VehicleStats();

        [Header("Visuals")]
        [Tooltip("Production model prefab with a VehicleRig. When empty, a primitive placeholder is built and a warning is logged.")]
        public GameObject visualPrefab;
        public float rainGripPenalty = 0.35f;   // fraction of grip lost in full rain

        /// <summary>Overlay backend `stats` json onto this definition (runtime instance only).</summary>
        public void ApplyBackendStats(string statsJson)
        {
            if (!string.IsNullOrEmpty(statsJson)) JsonUtility.FromJsonOverwrite(statsJson, stats);
        }
    }
}
