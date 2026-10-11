using System.Collections.Generic;
using UnityEngine;

namespace ARO.Vehicles
{
    /// <summary>One inspectable part of a vehicle model (cabin, wheels, headlights...). The Garage highlights its renderers and names it.</summary>
    public sealed class ShowroomPartInfo
    {
        public string kind, label; public List<Renderer> renderers = new List<Renderer>();
        public Bounds WorldBounds() { Bounds b = default; bool first = true; foreach (var r in renderers) { if (r == null) continue; if (first) { b = r.bounds; first = false; } else b.Encapsulate(r.bounds); } return b; }
    }

    /// <summary>
    /// What the Garage's 3D showroom needs to know about a vehicle model, independent of how the model was made. A production prefab (Resources/Vehicles/{id}
    /// with a VehicleRig) and the development placeholder both produce one of these, so the Garage never depends on which it got:
    /// paintable renderers, accent renderers, light emitters, inspectable parts, wheel points, an optional interior camera anchor and LOD groups.
    /// </summary>
    public sealed class ShowroomModel : MonoBehaviour
    {
        public bool isPlaceholder; public string source = "";
        public Bounds localBounds;                                     // relative to the model root (which faces +Z, stands on y = 0)
        public readonly List<Renderer> paint = new List<Renderer>(), accent = new List<Renderer>(), headEmitters = new List<Renderer>(), tailEmitters = new List<Renderer>();
        public readonly List<ShowroomPartInfo> parts = new List<ShowroomPartInfo>();
        public Transform interiorAnchor;                               // null = the model has no interior to look into
        public Vector3[] wheelPoints = new Vector3[0];                 // vehicle space, one per wheel
        public LODGroup[] lodGroups = new LODGroup[0];

        public bool HasInterior => interiorAnchor != null;
        public int LodLevels { get { int n = 0; foreach (var g in lodGroups) if (g != null) n = Mathf.Max(n, g.lodCount); return n; } }

        public ShowroomPartInfo Part(string kind, string label)
        {
            foreach (var p in parts) if (p.kind == kind) return p;
            var np = new ShowroomPartInfo { kind = kind, label = label }; parts.Add(np); return np;
        }

        /// <summary>Forces one LOD level on every LOD group (-1 = automatic). The Garage uses the highest detail close up and a lower one far away.</summary>
        public void ForceLod(int level) { foreach (var g in lodGroups) if (g != null) g.ForceLOD(level); }

        public void ComputeBounds()
        {
            Bounds b = default; bool first = true;
            foreach (var r in GetComponentsInChildren<Renderer>()) { if (r is ParticleSystemRenderer) continue; if (first) { b = r.bounds; first = false; } else b.Encapsulate(r.bounds); }
            localBounds = first ? new Bounds(Vector3.up, Vector3.one * 2f) : new Bounds(b.center - transform.position, b.size);
        }
    }
}
