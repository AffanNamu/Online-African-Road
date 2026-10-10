using System.IO;
using ARO.Game;
using ARO.Multiplayer;
using Unity.Netcode;
using Unity.Netcode.Components;
using Unity.Netcode.Transports.UTP;
using ARO.Vehicles;
using ARO.World;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace ARO.Editor
{
    /// <summary>
    /// One-click project setup: African Roads > Build Bootstrap Scene.
    /// Generates materials, vehicle definitions, the Lagos-Ibadan route and Assets/_Project/Scenes/Bootstrap.unity.
    /// Idempotent: re-running overwrites generated assets.
    /// </summary>
    public static class SceneBuilder
    {
        const string Root = "Assets/_Project";

        [MenuItem("African Roads/Build Bootstrap Scene")]
        public static void Build()
        {
            foreach (var d in new[] { "Data", "Materials", "Scenes", "Resources" }) Directory.CreateDirectory($"{Root}/{d}");

            EnsureUrp();
            var mats = new
            {
                good = Mat("RoadGood", new Color(0.16f, 0.16f, 0.17f)),
                worn = Mat("RoadWorn", new Color(0.24f, 0.22f, 0.20f)),
                damaged = Mat("RoadDamaged", new Color(0.32f, 0.27f, 0.22f)),
                ground = Mat("Ground", new Color(0.36f, 0.42f, 0.24f)),
                body = Mat("TruckBody", new Color(0.85f, 0.55f, 0.10f)),
                wheel = Mat("TruckWheel", new Color(0.05f, 0.05f, 0.05f)),
                marker = Mat("Marker", new Color(0.98f, 0.70f, 0.13f)),
            };
            var buildings = new[]
            {
                Mat("BuildingA", new Color(0.78f, 0.72f, 0.62f)), Mat("BuildingB", new Color(0.62f, 0.66f, 0.70f)),
                Mat("BuildingC", new Color(0.80f, 0.55f, 0.40f)), Mat("BuildingD", new Color(0.55f, 0.60f, 0.50f)),
            };

            var defs = new[]
            {
                Def("truck_light_01", "Savanna 4x2 Light Truck", VehicleCategory.Truck, 110, 120, 4000, 0, new VehicleStats()),
                Def("truck_medium_01", "Harmattan 6x2 Medium Truck", VehicleCategory.Truck, 100, 250, 9000, 0,
                    new VehicleStats { massKg = 8500, torqueNm = 1400, gears = new[] { 4.5f, 2.8f, 1.8f, 1.2f, 0.9f, 0.75f }, reverseRatio = 4f, finalDrive = 4.6f, brakeTorqueNm = 7500, maxSteerDeg = 28, fuelBurnLPerKm = 0.32f }),
                Def("bus_city_01", "Danfo City Bus", VehicleCategory.Bus, 90, 180, 0, 40,
                    new VehicleStats { massKg = 9500, torqueNm = 1100, reverseRatio = 3.8f, finalDrive = 4.4f, brakeTorqueNm = 6500, maxSteerDeg = 30, fuelBurnLPerKm = 0.28f }),
            };

            var route = MakeRoute();

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var camGo = new GameObject("Main Camera", typeof(Camera), typeof(AudioListener)) { tag = "MainCamera" };
            var cam = camGo.GetComponent<Camera>(); cam.farClipPlane = 1500f; cam.clearFlags = CameraClearFlags.Skybox;
            camGo.transform.position = new Vector3(0, 10, -20);
            var sun = new GameObject("Sun", typeof(Light)); sun.transform.rotation = Quaternion.Euler(50, -30, 0);
            var l = sun.GetComponent<Light>(); l.type = LightType.Directional; l.intensity = 1.2f; l.shadows = LightShadows.Soft;
            RenderSettings.fog = true; RenderSettings.fogMode = FogMode.Linear; RenderSettings.fogStartDistance = 400; RenderSettings.fogEndDistance = 1200;
            RenderSettings.fogColor = new Color(0.75f, 0.80f, 0.85f);

            var boot = new GameObject("GameBootstrap").AddComponent<GameBootstrap>();
            boot.route = route; boot.vehicleDefinitions = defs;
            boot.roadGood = mats.good; boot.roadWorn = mats.worn; boot.roadDamaged = mats.damaged; boot.ground = mats.ground;
            boot.truckBody = mats.body; boot.truckWheel = mats.wheel; boot.marker = mats.marker; boot.buildingMats = buildings;

            AddNetworking();

            string path = $"{Root}/Scenes/Bootstrap.unity";
            EditorSceneManager.SaveScene(scene, path);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(path, true) };
            AssetDatabase.SaveAssets();

            if (Resources.Load("BackendConfig") == null)
                Debug.LogWarning("[ARO] Create Resources/BackendConfig (Create > African Roads > Backend Config) and fill in your Supabase URL + anon key.");
            Debug.Log("[ARO] Bootstrap scene built: " + path);
        }

        /// <summary>
        /// NetworkManager (Unity Transport) + the NetworkPlayer prefab. The Multiplayer Services SDK starts/stops this
        /// NetworkManager when a Relay session is created/joined, so it must exist in the scene.
        /// </summary>
        static void AddNetworking()
        {
            Directory.CreateDirectory($"{Root}/Prefabs");
            var go = new GameObject("NetworkPlayer");
            go.AddComponent<NetworkObject>();
            var nt = go.AddComponent<OwnerNetworkTransform>(); nt.Interpolate = true;
            nt.SyncScaleX = nt.SyncScaleY = nt.SyncScaleZ = false;
            go.AddComponent<NetworkVehicle>();
            var prefab = PrefabUtility.SaveAsPrefabAsset(go, $"{Root}/Prefabs/NetworkPlayer.prefab");
            Object.DestroyImmediate(go);

            var list = AssetDatabase.LoadAssetAtPath<NetworkPrefabsList>($"{Root}/Prefabs/NetworkPrefabs.asset");
            if (list == null) { list = ScriptableObject.CreateInstance<NetworkPrefabsList>(); AssetDatabase.CreateAsset(list, $"{Root}/Prefabs/NetworkPrefabs.asset"); }
            list.Remove(new NetworkPrefab { Prefab = prefab }); list.Add(new NetworkPrefab { Prefab = prefab });
            EditorUtility.SetDirty(list);

            var nmGo = new GameObject("NetworkManager");
            var nm = nmGo.AddComponent<NetworkManager>(); var utp = nmGo.AddComponent<UnityTransport>();
            nm.NetworkConfig = new NetworkConfig { NetworkTransport = utp, PlayerPrefab = prefab, EnableSceneManagement = false, ConnectionApproval = false };
            nm.NetworkConfig.Prefabs.NetworkPrefabsLists.Add(list);
        }

        /// <summary>Creates and assigns a URP pipeline asset if the project has none (so headless CI renders with URP shaders).</summary>
        static void EnsureUrp()
        {
            if (GraphicsSettings.defaultRenderPipeline != null) return;
            string dir = $"{Root}/Settings"; Directory.CreateDirectory(dir);
            var rd = ScriptableObject.CreateInstance<UniversalRendererData>();
            AssetDatabase.CreateAsset(rd, $"{dir}/ARO_Renderer.asset");
            var pipe = UniversalRenderPipelineAsset.Create(rd);
            pipe.shadowDistance = 150f;
            AssetDatabase.CreateAsset(pipe, $"{dir}/ARO_URP.asset");
            GraphicsSettings.defaultRenderPipeline = pipe;
            for (int i = 0; i < QualitySettings.names.Length; i++) { QualitySettings.SetQualityLevel(i); QualitySettings.renderPipeline = pipe; }
            AssetDatabase.SaveAssets();
        }

        static Material Mat(string name, Color c)
        {
            string path = $"{Root}/Materials/{name}.mat";
            var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null) { m = new Material(shader); AssetDatabase.CreateAsset(m, path); }
            m.shader = shader; m.color = c;
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
            if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", 0.1f);
            EditorUtility.SetDirty(m); return m;
        }

        static VehicleDefinition Def(string id, string name, VehicleCategory cat, float maxKmh, float fuel, int cargo, int pax, VehicleStats stats)
        {
            string path = $"{Root}/Data/{id}.asset";
            var d = AssetDatabase.LoadAssetAtPath<VehicleDefinition>(path);
            if (d == null) { d = ScriptableObject.CreateInstance<VehicleDefinition>(); AssetDatabase.CreateAsset(d, path); }
            d.id = id; d.displayName = name; d.category = cat; d.maxSpeedKmh = maxKmh; d.fuelCapacityL = fuel;
            d.cargoCapacityKg = cargo; d.passengerCapacity = pax; d.stats = stats;
            EditorUtility.SetDirty(d); return d;
        }

        // Route passes through every seeded `locations` row (supabase/seed.sql) so jobs are physically reachable.
        static RouteDefinition MakeRoute()
        {
            string path = $"{Root}/Data/Route_Lagos_Ibadan.asset";
            var r = AssetDatabase.LoadAssetAtPath<RouteDefinition>(path);
            if (r == null) { r = ScriptableObject.CreateInstance<RouteDefinition>(); AssetDatabase.CreateAsset(r, path); }
            RouteNode N(float x, float z, float w, ZoneType zn, RoadSurface s) =>
                new RouteNode { position = new Vector3(x, 0, z), width = w, zone = zn, surface = s };
            r.routeId = "ng-lagos-ibadan";
            // Route DATA is the source of truth at runtime (RouteDefinition.Prepare); the nodes below are the legacy fallback if it is missing or invalid.
            r.specJson = AssetDatabase.LoadAssetAtPath<TextAsset>($"{Root}/Resources/Routes/ng-lagos-ibadan.route.json");
            r.nodes = new[]
            {
                N(0, 0, 14, ZoneType.Industrial, RoadSurface.Good),         // Apapa Port Depot
                N(1300, -300, 14, ZoneType.Urban, RoadSurface.Worn),
                N(2600, -900, 12, ZoneType.Commercial, RoadSurface.Damaged), // Mile 12 Market
                N(3400, 800, 14, ZoneType.Urban, RoadSurface.Worn),
                N(4200, 2500, 16, ZoneType.Industrial, RoadSurface.Good),    // Ikeja Industrial Estate
                N(6500, 3800, 20, ZoneType.Highway, RoadSurface.Good),
                N(13000, 7500, 20, ZoneType.Rural, RoadSurface.Good),
                N(20000, 11500, 18, ZoneType.Rural, RoadSurface.Worn),
                N(26000, 14500, 9, ZoneType.Rural, RoadSurface.Damaged),
                N(31000, 17000, 16, ZoneType.Highway, RoadSurface.Worn),
                N(36500, 19500, 14, ZoneType.Industrial, RoadSurface.Good),  // Ojoo Freight Depot
                N(38000, 21000, 12, ZoneType.Commercial, RoadSurface.Worn),  // Bodija Market
                N(39500, 22800, 12, ZoneType.Urban, RoadSurface.Good),       // Challenge Warehouse
            };
            EditorUtility.SetDirty(r); return r;
        }
    }
}
