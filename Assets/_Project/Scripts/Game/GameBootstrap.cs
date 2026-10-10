using System.Collections.Generic;
using ARO.Backend;
using ARO.Multiplayer;
using ARO.Vehicles;
using ARO.World;
using UnityEngine;

namespace ARO.Game
{
    /// <summary>Single entry point placed in the Bootstrap scene. Wires services, world, driving and UI.</summary>
    public class GameBootstrap : MonoBehaviour
    {
        public RouteDefinition route;
        public VehicleDefinition[] vehicleDefinitions;
        public Material roadGood, roadWorn, roadDamaged, ground, truckBody, truckWheel, marker;
        public Material[] buildingMats;

        void Start()
        {
            Application.targetFrameRate = 60;
            var cfg = Resources.Load<BackendConfig>("BackendConfig");
            if (cfg == null) { cfg = ScriptableObject.CreateInstance<BackendConfig>(); Debug.LogWarning("[Boot] No Resources/BackendConfig asset - running unconfigured."); }
            Debug.Log($"[Boot] African Roads Online starting, backend {(cfg.IsConfigured ? "configured" : "NOT configured")}.");
            var svc = new GameServices(cfg);

            // Route data first (the streamer, traffic and the vehicle spawn all depend on it), then the rendering foundation.
            bool haveModel = route.Prepare();
            Light sun = null;
            foreach (var l in FindObjectsByType<Light>(FindObjectsSortMode.None)) if (l.type == LightType.Directional) { sun = l; break; }
            var rig = new GameObject("RenderingRig").AddComponent<RenderingRig>(); rig.Init(Camera.main, sun);
            var tier = rig.Tier;
            var worldMats = haveModel ? WorldMaterials.Create(roadGood) : null;

            var streamer = new GameObject("ChunkStreamer").AddComponent<ChunkStreamer>();
            streamer.route = route; streamer.roadGood = roadGood; streamer.roadWorn = roadWorn; streamer.roadDamaged = roadDamaged;
            streamer.groundMat = ground; streamer.buildingMats = buildingMats; streamer.materials = worldMats; streamer.tier = tier;

            var defs = new Dictionary<string, VehicleDefinition>();
            foreach (var d in vehicleDefinitions) defs[d.id] = d;

            var drive = new GameObject("DrivingSession").AddComponent<DrivingSession>();
            drive.Init(svc, route, defs, truckBody, truckWheel, marker, streamer);
            // Networked puppets are built from the same definitions/materials as the local vehicle.
            NetVisualContext.Definitions = defs; NetVisualContext.BodyMaterial = truckBody; NetVisualContext.WheelMaterial = truckWheel;
            NetVisualContext.LocalVehicle = () => drive.Vehicle;
            NetVisualContext.LocalName = () => svc.Profile != null ? svc.Profile.display_name : "Driver";
            NetVisualContext.LocalVehicleDefinitionId = () => drive.Vehicle != null && drive.Vehicle.definition != null ? drive.Vehicle.definition.id : "truck_light_01";
            var hud = new GameObject("Hud").AddComponent<Hud>(); hud.Init(drive, svc);
            var bus = new GameObject("BusSession").AddComponent<BusSession>(); bus.Init(svc, drive); hud.Bus = bus;
            new GameObject("GameFlow").AddComponent<GameFlow>().Init(svc, drive, hud, bus);

            // Ambient traffic (pooled, quality-scaled); follows the player's vehicle once spawned.
            var traffic = new GameObject("Traffic").AddComponent<TrafficManager>();
            traffic.route = route; traffic.bodyTemplate = truckBody; traffic.tier = tier;
            drive.VehicleSpawned += v => traffic.player = v.transform;

            // Atmosphere: live West Africa Time sky + weather that wets the roads and reduces grip.
            var tod = new GameObject("TimeOfDay").AddComponent<TimeOfDay>(); tod.sun = sun;
            var weather = new GameObject("Weather").AddComponent<WeatherSystem>();
            weather.timeOfDay = tod; weather.roadMaterials = haveModel ? worldMats.WeatherSet : new[] { roadGood, roadWorn, roadDamaged };
            rig.timeOfDay = tod; rig.weather = weather;
            var probe = new GameObject("PerfProbe").AddComponent<PerfProbe>(); probe.streamer = streamer; probe.tier = tier;
            drive.VehicleSpawned += v => { weather.vehicle = v.transform; v.SetLights(tod.IsNight); };
            tod.NightChanged += night => { if (drive.Vehicle != null) drive.Vehicle.SetLights(night); };

            // Menu backdrop camera position: above the start of the route.
            var n0 = route.nodes[0].position; streamer.target = new GameObject("MenuFocus").transform; streamer.target.position = n0;
            Camera.main.transform.position = n0 + new Vector3(-20, 12, -30); Camera.main.transform.LookAt(n0 + Vector3.up * 3);
        }
    }
}
