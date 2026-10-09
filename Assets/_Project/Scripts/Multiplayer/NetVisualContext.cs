using System;
using System.Collections.Generic;
using ARO.Vehicles;
using UnityEngine;

namespace ARO.Multiplayer
{
    /// <summary>Process-wide hooks the game layer fills in so networked puppets can be built without a dependency on game code.</summary>
    public static class NetVisualContext
    {
        public static Dictionary<string, VehicleDefinition> Definitions = new Dictionary<string, VehicleDefinition>();
        public static Material BodyMaterial, WheelMaterial;
        /// <summary>The local player's physics vehicle (null while in menus / before spawning).</summary>
        public static Func<VehicleController> LocalVehicle = () => null;
        public static Func<string> LocalName = () => "Driver";
        public static Func<string> LocalVehicleDefinitionId = () => "truck_light_01";
    }
}
