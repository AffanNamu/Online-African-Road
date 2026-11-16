using ARO.World;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;

namespace ARO.Tests
{
    /// <summary>RB-15: the instancing plan read from a building prefab keeps its LOD meshes, materials, transforms, thresholds and shadow flags; prefabs the instancer cannot draw faithfully
    /// (a light inside, a renderer outside the LODs) are refused and stay prefab instances.</summary>
    public class PropInstancerTests
    {
        static GameObject Part(PrimitiveType t, Transform parent, Vector3 pos, Material m, ShadowCastingMode shadow)
        {
            var g = GameObject.CreatePrimitive(t); g.transform.SetParent(parent, false); g.transform.localPosition = pos;
            Object.DestroyImmediate(g.GetComponent<Collider>()); var r = g.GetComponent<MeshRenderer>(); r.sharedMaterial = m; r.shadowCastingMode = shadow; return g;
        }

        static Material Mat() => new Material(Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard"));

        [Test]
        public void APrefabWithLodsBecomesAPlanThatKeepsItsMeshesMaterialsTransformsAndThresholds()
        {
            var root = new GameObject("building"); var m0 = Mat(); var m1 = Mat();
            try
            {
                var near = Part(PrimitiveType.Cube, root.transform, new Vector3(2f, 0.5f, 0f), m0, ShadowCastingMode.On);
                var far = Part(PrimitiveType.Sphere, root.transform, Vector3.zero, m1, ShadowCastingMode.Off);
                var lg = root.AddComponent<LODGroup>();
                lg.SetLODs(new[] { new LOD(0.3f, new Renderer[] { near.GetComponent<Renderer>() }), new LOD(0.05f, new Renderer[] { far.GetComponent<Renderer>() }) }); lg.RecalculateBounds();
                var plan = PropInstancer.PlanFor(root, "test_building");
                Assert.IsNotNull(plan);
                Assert.AreEqual(2, plan.Lods.Length); Assert.AreEqual(0.3f, plan.Heights[0], 1e-5f); Assert.AreEqual(0.05f, plan.Heights[1], 1e-5f);
                Assert.AreSame(near.GetComponent<MeshFilter>().sharedMesh, plan.Lods[0][0].Mesh); Assert.AreSame(m0, plan.Lods[0][0].Mat);
                Assert.AreEqual(2f, plan.Lods[0][0].Local.m03, 1e-5f); Assert.AreEqual(0.5f, plan.Lods[0][0].Local.m13, 1e-5f);     // the part's place inside the prefab
                Assert.AreEqual(ShadowCastingMode.On, plan.Lods[0][0].Shadow); Assert.AreEqual(ShadowCastingMode.Off, plan.Lods[1][0].Shadow);
                Assert.IsTrue(m0.enableInstancing && m1.enableInstancing);
                Assert.AreEqual(lg.size, plan.Size, 1e-5f);
                Assert.AreSame(plan, PropInstancer.PlanFor(root, "test_building"), "a plan is read once per prefab");
            }
            finally { Object.DestroyImmediate(root); Object.DestroyImmediate(m0); Object.DestroyImmediate(m1); }
        }

        [Test]
        public void APrefabWithALightOrARendererOutsideItsLodsStaysAPrefab()
        {
            var lit = new GameObject("lit"); var stray = new GameObject("stray"); var m = Mat();
            try
            {
                var a = Part(PrimitiveType.Cube, lit.transform, Vector3.zero, m, ShadowCastingMode.On);
                lit.AddComponent<LODGroup>().SetLODs(new[] { new LOD(0.1f, new Renderer[] { a.GetComponent<Renderer>() }) });
                new GameObject("lamp", typeof(Light)).transform.SetParent(lit.transform, false);
                Assert.IsNull(PropInstancer.PlanFor(lit, "lit"), "a light inside: the instancer would not draw it");

                var b = Part(PrimitiveType.Cube, stray.transform, Vector3.zero, m, ShadowCastingMode.On); Part(PrimitiveType.Cube, stray.transform, Vector3.up, m, ShadowCastingMode.On);
                stray.AddComponent<LODGroup>().SetLODs(new[] { new LOD(0.1f, new Renderer[] { b.GetComponent<Renderer>() }) });
                Assert.IsNull(PropInstancer.PlanFor(stray, "stray"), "a renderer outside the LODs: the instancer would not draw it");
            }
            finally { Object.DestroyImmediate(lit); Object.DestroyImmediate(stray); Object.DestroyImmediate(m); }
        }
    }
}
