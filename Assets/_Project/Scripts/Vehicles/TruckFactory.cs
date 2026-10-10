using ARO.Input;
using UnityEngine;

namespace ARO.Vehicles
{
    /// <summary>
    /// Builds a drivable vehicle from a VehicleDefinition using primitives (placeholder art).
    /// Replace the visual child with a real model prefab later; physics/setup stay identical.
    /// Works for any category: bus/van use the same wheel layout with different body dimensions.
    /// </summary>
    public static class TruckFactory
    {
        public static VehicleController Create(VehicleDefinition def, Vector3 pos, Quaternion rot, Material bodyMat, Material wheelMat, bool localPlayer = true)
        {
            if (def.visualPrefab != null) return FromPrefab(def, pos, rot, localPlayer);
            Debug.LogWarning($"[Vehicles] '{def.id}' has no visualPrefab - building a PLACEHOLDER. Assign a production model with a VehicleRig.");
            var root = new GameObject(def.displayName) { layer = 0 };
            root.transform.SetPositionAndRotation(pos, rot);
            var rb = root.AddComponent<Rigidbody>(); rb.mass = def.stats.massKg;

            bool bus = def.category == VehicleCategory.Bus;
            Vector3 bodySize = bus ? new Vector3(2.5f, 2.6f, 10f) : new Vector3(2.4f, 1.3f, 6f);
            Part(root, PrimitiveType.Cube, "Body", new Vector3(0, 1.2f + bodySize.y / 2f - 0.5f, -0.4f), bodySize, bodyMat, collider: true);
            if (!bus) Part(root, PrimitiveType.Cube, "Cab", new Vector3(0, 2.3f, 2.4f), new Vector3(2.3f, 1.6f, 1.8f), bodyMat, collider: true);
            float wheelZFront = bodySize.z / 2f - 0.8f, wheelZRear = -bodySize.z / 2f + 1f, track = 1.15f;

            var com = new GameObject("CoM").transform; com.SetParent(root.transform, false); com.localPosition = new Vector3(0, 0.5f, 0);
            float spring = 35000f * def.stats.massKg / 4200f;
            var front = MakeAxle(root, wheelZFront, track, spring, wheelMat, steer: true, drive: false);
            var rear = MakeAxle(root, wheelZRear, track, spring, wheelMat, steer: false, drive: true);

            var v = root.AddComponent<VehicleController>();
            v.definition = def; v.axles = new[] { front, rear }; v.centerOfMass = com; v.fuelL = def.fuelCapacityL;
            v.Initialize();
            AddPlaceholderLights(root, v, bodySize, bodyMat);
            Finish(root, v, localPlayer);
            return v;
        }

        static VehicleController FromPrefab(VehicleDefinition def, Vector3 pos, Quaternion rot, bool localPlayer)
        {
            var root = Object.Instantiate(def.visualPrefab, pos, rot);
            var rig = root.GetComponent<VehicleRig>();
            if (rig == null) { Debug.LogError($"[Vehicles] Prefab for '{def.id}' lacks a VehicleRig."); Object.Destroy(root); return null; }
            if (!root.TryGetComponent(out Rigidbody rb)) rb = root.AddComponent<Rigidbody>();
            rb.mass = def.stats.massKg;
            var v = root.AddComponent<VehicleController>();
            v.definition = def; v.axles = rig.axles; v.centerOfMass = rig.centerOfMass; v.fuelL = def.fuelCapacityL;
            v.Initialize();
            var lights = root.AddComponent<VehicleLights>(); lights.Bind(rig);
            Finish(root, v, localPlayer);
            return v;
        }

        static void Finish(GameObject root, VehicleController v, bool localPlayer)
        {
            if (!localPlayer) return;
            var input = root.AddComponent<KeyboardVehicleInput>(); v.SetInput(input);
            root.AddComponent<EngineAudio>();
        }

        static void AddPlaceholderLights(GameObject root, VehicleController v, Vector3 body, Material baseMat)
        {
            var lights = root.AddComponent<VehicleLights>();
            var heads = new System.Collections.Generic.List<Light>(); var brake = new System.Collections.Generic.List<Renderer>();
            var rev = new System.Collections.Generic.List<Renderer>(); var left = new System.Collections.Generic.List<Renderer>(); var right = new System.Collections.Generic.List<Renderer>();
            float zFront = body.z / 2f + 0.1f, zRear = -body.z / 2f - 0.5f;
            foreach (int side in new[] { -1, 1 })
            {
                var h = new GameObject("Headlight").AddComponent<Light>(); h.transform.SetParent(root.transform, false);
                h.transform.localPosition = new Vector3(side * 0.9f, 1.3f, zFront + 1.4f); h.type = LightType.Spot; h.spotAngle = 65; h.range = 60; h.intensity = 8; h.enabled = false; heads.Add(h);
                brake.Add(Emitter(root, baseMat, new Vector3(side * 0.95f, 1.5f, zRear), new Vector3(0.35f, 0.2f, 0.05f)));
                rev.Add(Emitter(root, baseMat, new Vector3(side * 0.6f, 1.5f, zRear), new Vector3(0.2f, 0.15f, 0.05f)));
                (side < 0 ? left : right).Add(Emitter(root, baseMat, new Vector3(side * 1.1f, 1.5f, zRear), new Vector3(0.2f, 0.15f, 0.05f)));
            }
            lights.headlights = heads.ToArray(); lights.brakeEmit = brake.ToArray(); lights.reverseEmit = rev.ToArray();
            lights.leftEmit = left.ToArray(); lights.rightEmit = right.ToArray(); lights.headEmit = new Renderer[0];
        }

        static Renderer Emitter(GameObject root, Material baseMat, Vector3 pos, Vector3 scale)
        {
            var g = GameObject.CreatePrimitive(PrimitiveType.Cube); Object.Destroy(g.GetComponent<Collider>());
            g.transform.SetParent(root.transform, false); g.transform.localPosition = pos; g.transform.localScale = scale;
            var r = g.GetComponent<Renderer>();
            // The primitive's default material uses the built-in Standard shader, which a URP player build does not contain (renders magenta).
            // Derive from the (URP) body material instead.
            if (baseMat != null) { var m = new Material(baseMat); m.color = new Color(0.25f, 0.03f, 0.03f); if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", m.color); r.sharedMaterial = m; }
            r.material.EnableKeyword("_EMISSION"); return r;
        }

        static WheelAxle MakeAxle(GameObject root, float z, float track, float spring, Material mat, bool steer, bool drive)
        {
            var a = new WheelAxle { steer = steer, drive = drive };
            a.left = Wheel(root, new Vector3(-track, 0.6f, z), spring, out a.leftVisual, mat);
            a.right = Wheel(root, new Vector3(track, 0.6f, z), spring, out a.rightVisual, mat);
            return a;
        }

        static WheelCollider Wheel(GameObject root, Vector3 local, float spring, out Transform visual, Material mat)
        {
            var go = new GameObject("WheelCollider"); go.transform.SetParent(root.transform, false); go.transform.localPosition = local;
            var wc = go.AddComponent<WheelCollider>();
            wc.radius = 0.55f; wc.suspensionDistance = 0.35f; wc.mass = 60f;
            wc.suspensionSpring = new JointSpring { spring = spring, damper = spring * 0.12f, targetPosition = 0.5f };
            wc.forwardFriction = new WheelFrictionCurve { extremumSlip = 0.4f, extremumValue = 1f, asymptoteSlip = 0.8f, asymptoteValue = 0.5f, stiffness = 1.6f };
            wc.sidewaysFriction = new WheelFrictionCurve { extremumSlip = 0.25f, extremumValue = 1f, asymptoteSlip = 0.5f, asymptoteValue = 0.75f, stiffness = 1.8f };
            var vis = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            Object.Destroy(vis.GetComponent<Collider>());
            vis.name = "WheelVisual"; vis.transform.SetParent(root.transform, false);
            // Cylinder axis is Y; child rotation makes it roll about X. Pose sync sets world rot, so wrap it.
            var holder = new GameObject("WheelHolder").transform; holder.SetParent(root.transform, false);
            vis.transform.SetParent(holder, false);
            vis.transform.localRotation = Quaternion.Euler(0, 0, 90); vis.transform.localScale = new Vector3(1.1f, 0.15f, 1.1f);
            if (mat != null) vis.GetComponent<Renderer>().sharedMaterial = mat;
            visual = holder; return wc;
        }

        static void Part(GameObject root, PrimitiveType type, string name, Vector3 pos, Vector3 scale, Material mat, bool collider)
        {
            var p = GameObject.CreatePrimitive(type); p.name = name; p.transform.SetParent(root.transform, false);
            p.transform.localPosition = pos; p.transform.localScale = scale;
            if (!collider) Object.Destroy(p.GetComponent<Collider>());
            if (mat != null) p.GetComponent<Renderer>().sharedMaterial = mat;
        }
    }
}
