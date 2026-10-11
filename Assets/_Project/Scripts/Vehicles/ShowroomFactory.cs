using System.Collections.Generic;
using UnityEngine;

namespace ARO.Vehicles
{
    /// <summary>
    /// Builds the visual-only model the Garage shows (no physics, no input, no audio). The slot is the same one the drivable vehicle uses: the definition's
    /// visualPrefab or Resources/Vehicles/{id} (a prefab with a VehicleRig). When no production model exists a clearly marked PLACEHOLDER is built from
    /// primitives with the same layout, and the Garage says so on screen.
    /// </summary>
    public static class ShowroomFactory
    {
        public static ShowroomModel Create(VehicleDefinition def, Material bodyMat, Material wheelMat, Transform parent)
        {
            var prefab = def.visualPrefab != null ? def.visualPrefab : Resources.Load<GameObject>("Vehicles/" + def.id);
            if (prefab != null) { var m = FromPrefab(def, prefab, parent); if (m != null) return m; }
            return BuildPlaceholder(def, bodyMat, wheelMat, parent);
        }

        // ------------------------------------------------------------------------------------------------------------------ production model
        static ShowroomModel FromPrefab(VehicleDefinition def, GameObject prefab, Transform parent)
        {
            var root = Object.Instantiate(prefab, parent); root.name = def.displayName; root.transform.localPosition = Vector3.zero; root.transform.localRotation = Quaternion.identity;
            var rig = root.GetComponent<VehicleRig>();
            if (rig == null) { Debug.LogError($"[Showroom] Prefab for '{def.id}' lacks a VehicleRig."); Object.Destroy(root); return null; }
            foreach (var wc in root.GetComponentsInChildren<WheelCollider>()) wc.enabled = false;
            foreach (var rb in root.GetComponentsInChildren<Rigidbody>()) rb.isKinematic = true;
            var model = root.AddComponent<ShowroomModel>(); model.isPlaceholder = false; model.source = "production prefab";
            if (rig.dirtyRenderers != null) foreach (var r in rig.dirtyRenderers) if (r != null) model.paint.Add(r);
            if (rig.headlightEmitters != null) { model.headEmitters.AddRange(rig.headlightEmitters); AddPart(model, "headlights", "Headlights", rig.headlightEmitters); }
            if (rig.brakeEmitters != null) { model.tailEmitters.AddRange(rig.brakeEmitters); AddPart(model, "taillights", "Taillights", rig.brakeEmitters); }
            var wheels = new List<Renderer>(); var pts = new List<Vector3>();
            if (rig.axles != null) foreach (var a in rig.axles) foreach (var t in new[] { a.leftVisual, a.rightVisual }) if (t != null) { pts.Add(root.transform.InverseTransformPoint(t.position)); wheels.AddRange(t.GetComponentsInChildren<Renderer>()); }
            if (wheels.Count > 0) AddPart(model, "wheels", "Wheels", wheels.ToArray());
            model.wheelPoints = pts.ToArray(); model.interiorAnchor = rig.cockpitCameraAnchor; model.lodGroups = root.GetComponentsInChildren<LODGroup>();
            model.ComputeBounds(); return model;
        }
        static void AddPart(ShowroomModel m, string kind, string label, Renderer[] rs) { var p = m.Part(kind, label); foreach (var r in rs) if (r != null) p.renderers.Add(r); }

        // ------------------------------------------------------------------------------------------------------------------ development placeholder
        static Material Mat(Material template, Color c, float smooth, float metal, Color? emission = null)
        {
            var m = template != null ? new Material(template) : new Material(Shader.Find("Universal Render Pipeline/Lit"));
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c); else m.color = c;
            if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", smooth);
            if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", metal);
            if (emission.HasValue) { m.EnableKeyword("_EMISSION"); if (m.HasProperty("_EmissionColor")) m.SetColor("_EmissionColor", emission.Value); }
            return m;
        }

        static Renderer Box(Transform root, string name, PrimitiveType type, Vector3 pos, Vector3 scale, Material mat, Vector3? euler = null)
        {
            var g = GameObject.CreatePrimitive(type); g.name = name; Object.Destroy(g.GetComponent<Collider>());
            g.transform.SetParent(root, false); g.transform.localPosition = pos; g.transform.localScale = scale; if (euler.HasValue) g.transform.localEulerAngles = euler.Value;
            var r = g.GetComponent<Renderer>(); r.sharedMaterial = mat; return r;
        }

        static ShowroomModel BuildPlaceholder(VehicleDefinition def, Material bodyTemplate, Material wheelTemplate, Transform parent)
        {
            var go = new GameObject(def.displayName + " (placeholder)"); go.transform.SetParent(parent, false);
            var model = go.AddComponent<ShowroomModel>(); model.isPlaceholder = true; model.source = "placeholder from primitives";
            var t = go.transform;
            bool bus = def.category == VehicleCategory.Bus;
            Color baseCol = bodyTemplate != null && bodyTemplate.HasProperty("_BaseColor") ? bodyTemplate.GetColor("_BaseColor") : new Color(0.85f, 0.85f, 0.88f);
            var paintMat = Mat(bodyTemplate, baseCol, 0.55f, 0.3f); var accentMat = Mat(bodyTemplate, new Color(0.85f, 0.62f, 0.12f), 0.5f, 0.4f);
            var glass = Mat(bodyTemplate, new Color(0.05f, 0.07f, 0.10f), 0.92f, 0.0f); var dark = Mat(bodyTemplate, new Color(0.07f, 0.07f, 0.08f), 0.3f, 0.2f);
            var steel = Mat(bodyTemplate, new Color(0.55f, 0.57f, 0.60f), 0.75f, 0.9f); var tyre = Mat(wheelTemplate != null ? wheelTemplate : bodyTemplate, new Color(0.05f, 0.05f, 0.055f), 0.2f, 0f);
            var head = Mat(bodyTemplate, new Color(0.9f, 0.9f, 0.85f), 0.8f, 0f, Color.black); var tail = Mat(bodyTemplate, new Color(0.35f, 0.03f, 0.03f), 0.6f, 0f, Color.black);
            var cabin = model.Part("cabin", bus ? "Body" : "Cabin"); var wheels = model.Part("wheels", "Wheels"); var heads = model.Part("headlights", "Headlights"); var tails = model.Part("taillights", "Taillights");
            var mirrors = model.Part("mirrors", "Mirrors"); var chassis = model.Part("chassis", "Chassis"); var cargo = model.Part("trailer", bus ? "Roof" : "Cargo body");

            float frontZ, rearZ, wheelFrontZ, wheelRearZ, trackX;
            if (bus)
            {
                var body = Box(t, "Body", PrimitiveType.Cube, new Vector3(0, 2.0f, 0), new Vector3(2.5f, 2.6f, 10f), paintMat); model.paint.Add(body); cabin.renderers.Add(body);
                foreach (int s in new[] { -1, 1 }) { var w = Box(t, "Windows", PrimitiveType.Cube, new Vector3(s * 1.26f, 2.45f, -0.3f), new Vector3(0.04f, 0.9f, 8.4f), glass); cabin.renderers.Add(w); var st = Box(t, "Stripe", PrimitiveType.Cube, new Vector3(s * 1.26f, 1.45f, 0), new Vector3(0.03f, 0.22f, 9.6f), accentMat); model.accent.Add(st); }
                var ws = Box(t, "Windshield", PrimitiveType.Cube, new Vector3(0, 2.5f, 5.02f), new Vector3(2.2f, 1.1f, 0.05f), glass); cabin.renderers.Add(ws);
                var roof = Box(t, "RoofUnit", PrimitiveType.Cube, new Vector3(0, 3.4f, -1.0f), new Vector3(1.6f, 0.2f, 2.4f), dark); cargo.renderers.Add(roof);
                frontZ = 5.05f; rearZ = -5.05f; wheelFrontZ = 3.9f; wheelRearZ = -3.6f; trackX = 1.13f;
            }
            else
            {
                float boxLen = def.cargoCapacityKg > 6000 ? 5.0f : 4.2f;
                var ch = Box(t, "Chassis", PrimitiveType.Cube, new Vector3(0, 0.95f, -0.3f), new Vector3(1.3f, 0.3f, 7.0f), dark); chassis.renderers.Add(ch);
                var box = Box(t, "CargoBody", PrimitiveType.Cube, new Vector3(0, 2.15f, -1.0f - (boxLen - 4.2f) / 2f), new Vector3(2.4f, 2.0f, boxLen), paintMat); model.paint.Add(box); cargo.renderers.Add(box);
                var cab = Box(t, "Cab", PrimitiveType.Cube, new Vector3(0, 2.05f, 2.35f), new Vector3(2.3f, 1.7f, 1.9f), paintMat); model.paint.Add(cab); cabin.renderers.Add(cab);
                var defl = Box(t, "RoofDeflector", PrimitiveType.Cube, new Vector3(0, 3.15f, 2.2f), new Vector3(2.2f, 0.45f, 1.5f), paintMat, new Vector3(-8f, 0, 0)); model.paint.Add(defl); cabin.renderers.Add(defl);
                var ws = Box(t, "Windshield", PrimitiveType.Cube, new Vector3(0, 2.4f, 3.31f), new Vector3(2.0f, 0.75f, 0.06f), glass); cabin.renderers.Add(ws);
                foreach (int s in new[] { -1, 1 })
                {
                    cabin.renderers.Add(Box(t, "SideWindow", PrimitiveType.Cube, new Vector3(s * 1.16f, 2.4f, 2.4f), new Vector3(0.05f, 0.65f, 1.15f), glass));
                    model.accent.Add(Box(t, "Stripe", PrimitiveType.Cube, new Vector3(s * 1.205f, 2.0f, -1.0f - (boxLen - 4.2f) / 2f), new Vector3(0.02f, 0.28f, boxLen - 0.3f), accentMat));
                    mirrors.renderers.Add(Box(t, "Mirror", PrimitiveType.Cube, new Vector3(s * 1.34f, 2.55f, 3.0f), new Vector3(0.12f, 0.5f, 0.18f), dark));
                    cabin.renderers.Add(Box(t, "Step", PrimitiveType.Cube, new Vector3(s * 1.2f, 1.1f, 2.4f), new Vector3(0.1f, 0.06f, 0.7f), steel));
                }
                var ex = Box(t, "ExhaustStack", PrimitiveType.Cylinder, new Vector3(0.95f, 2.9f, 1.25f), new Vector3(0.14f, 0.85f, 0.14f), steel); model.Part("exhaust", "Exhaust").renderers.Add(ex);
                var tank = Box(t, "FuelTank", PrimitiveType.Cylinder, new Vector3(-1.0f, 1.0f, 0.9f), new Vector3(0.5f, 0.7f, 0.5f), steel, new Vector3(0, 0, 90f)); chassis.renderers.Add(tank);
                frontZ = 3.34f; rearZ = -boxLen - 0.2f + 0.6f; wheelFrontZ = 2.2f; wheelRearZ = -1.2f - (boxLen - 4.2f) / 2f; trackX = 1.1f;
                model.interiorAnchor = null;
            }
            // grille, bumper, headlights, tail lights
            var grille = Box(t, "Grille", PrimitiveType.Cube, new Vector3(0, 1.45f, frontZ - 0.02f), new Vector3(bus ? 1.6f : 1.5f, 0.55f, 0.08f), dark); cabin.renderers.Add(grille);
            var bumper = Box(t, "Bumper", PrimitiveType.Cube, new Vector3(0, 0.95f, frontZ + 0.05f), new Vector3(bus ? 2.5f : 2.3f, 0.3f, 0.28f), steel); chassis.renderers.Add(bumper);
            foreach (int s in new[] { -1, 1 })
            {
                var hl = Box(t, "Headlight", PrimitiveType.Cube, new Vector3(s * (bus ? 0.95f : 0.85f), 1.5f, frontZ + 0.01f), new Vector3(0.4f, 0.22f, 0.08f), head); heads.renderers.Add(hl); model.headEmitters.Add(hl);
                var tl = Box(t, "Taillight", PrimitiveType.Cube, new Vector3(s * 0.95f, 1.3f, rearZ), new Vector3(0.3f, 0.2f, 0.06f), tail); tails.renderers.Add(tl); model.tailEmitters.Add(tl);
            }
            var pts = new List<Vector3>();
            foreach (float z in new[] { wheelFrontZ, wheelRearZ })
                foreach (int s in new[] { -1, 1 })
                {
                    var tire = Box(t, "Tyre", PrimitiveType.Cylinder, new Vector3(s * trackX, 0.55f, z), new Vector3(1.1f, 0.16f, 1.1f), tyre, new Vector3(0, 0, 90f));
                    var hub = Box(t, "Rim", PrimitiveType.Cylinder, new Vector3(s * (trackX + s * 0.04f), 0.55f, z), new Vector3(0.62f, 0.17f, 0.62f), steel, new Vector3(0, 0, 90f));
                    wheels.renderers.Add(tire); wheels.renderers.Add(hub); pts.Add(new Vector3(s * trackX, 0.55f, z));
                }
            model.wheelPoints = pts.ToArray(); model.ComputeBounds();
            return model;
        }
    }
}
