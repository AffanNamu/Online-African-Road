using System;
using System.Collections.Generic;

namespace ARO.NetCore
{
    public struct BoxCollider3 { public V3 Centre, Size; public float YawDeg; public string Prop; }

    /// <summary>Merges many prop instances into ONE mesh (one draw call per material slot per chunk instead of one per object). Pure and unit-tested.</summary>
    public static class PropBatcher
    {
        /// <summary>Transforms a local point by uniform scale, yaw (clockwise seen from above, local +z -> heading) and translation.</summary>
        public static V3 Place(V3 local, V3 pos, float yawDeg, float scale)
        {
            float y = yawDeg * (float)Math.PI / 180f, c = (float)Math.Cos(y), s = (float)Math.Sin(y);
            float lx = local.X * scale, ly = local.Y * scale, lz = local.Z * scale;
            return new V3(pos.X + c * lx + s * lz, pos.Y + ly, pos.Z - s * lx + c * lz);
        }

        public static MeshBuffers Merge(IList<PropInstance> instances, Func<string, int, MeshBuffers> geometry, int lod, V3 origin, Func<PropInstance, bool> include = null)
        {
            var outMb = new MeshBuffers(PropSub.Count);
            var cache = new Dictionary<string, MeshBuffers>();
            foreach (var inst in instances)
            {
                if (include != null && !include(inst)) continue;
                if (!cache.TryGetValue(inst.Prop, out var g)) cache[inst.Prop] = g = geometry(inst.Prop, lod);
                if (g == null) continue;
                int baseIndex = outMb.VertexCount;
                var local = new V3(inst.Pos.X - origin.X, inst.Pos.Y - origin.Y, inst.Pos.Z - origin.Z);
                for (int i = 0; i < g.Vertices.Count; i++) outMb.Add(Place(g.Vertices[i], local, inst.YawDeg, inst.Scale), g.Uvs[i]);
                for (int s = 0; s < g.Triangles.Length && s < PropSub.Count; s++)
                    foreach (int idx in g.Triangles[s]) outMb.Triangles[s].Add(baseIndex + idx);
            }
            return outMb;
        }

        /// <summary>Static box colliders for props whose catalog entry says collider = box (only built near the player).</summary>
        public static List<BoxCollider3> BoxColliders(IList<PropInstance> instances, Func<string, PropDef> lookup, Func<PropInstance, bool> include = null)
        {
            var res = new List<BoxCollider3>();
            foreach (var inst in instances)
            {
                if (include != null && !include(inst)) continue;
                var def = lookup(inst.Prop);
                if (def == null || def.collider != "box") continue;
                res.Add(new BoxCollider3
                {
                    Prop = def.id, YawDeg = inst.YawDeg,
                    Centre = new V3(inst.Pos.X, inst.Pos.Y + def.heightM * inst.Scale * 0.5f, inst.Pos.Z),
                    Size = new V3(def.widthM * inst.Scale, def.heightM * inst.Scale, def.depthM * inst.Scale)
                });
            }
            return res;
        }
    }
}
