using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using ARO.NetCore;
using ARO.World;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace ARO.Tests
{
    /// <summary>
    /// Runs the world-generation stack inside Unity: JsonUtility (not System.Text.Json) must read the shipped route/prop data, the generated
    /// geometry must convert to valid Unity meshes, and the materials/procedural textures must build. Pure maths is covered by the .NET tests.
    /// </summary>
    public class WorldGenerationTests
    {
        const string RouteResource = "Routes/ng-lagos-ibadan.route";

        static RouteSpec LoadSpec() { var ta = Resources.Load<TextAsset>(RouteResource); Assert.NotNull(ta, "Resources/" + RouteResource + ".json is missing"); return JsonUtility.FromJson<RouteSpec>(ta.text); }

        [Test] public void RouteDataParsesWithJsonUtilityAndBuilds()
        {
            var spec = LoadSpec();
            Assert.AreEqual("ng-lagos-ibadan", spec.routeId); Assert.AreEqual(13, spec.controlPoints.Length); Assert.AreEqual(6, spec.profiles.Length);
            Assert.AreEqual(2, spec.bridges.Length); Assert.AreEqual(3, spec.junctions.Length); Assert.Greater(spec.propRules.Length, 10); Assert.AreEqual("lagos-ibadan", spec.meta.worldMapRouteId);
            var errors = RouteSpecValidator.Validate(spec, PropLibrary.KnownIds());
            Assert.IsEmpty(errors, string.Join("\n", errors));
            var model = RouteModel.Build(spec);
            Assert.AreEqual(spec.meta.gameLengthKm, model.Length / 1000f, spec.meta.gameLengthKm * 0.01f);
            Assert.AreEqual(2, model.Main.Bridges.Length); Assert.AreEqual(3, model.Sides.Count);
        }

        [Test] public void PropCatalogParsesAndEveryPropIsMarkedAsAPlaceholder()
        {
            PropLibrary.EnsureLoaded();
            Assert.AreEqual(16, PropLibrary.All.Count());
            foreach (var d in PropLibrary.All) { Assert.AreEqual("dev", d.status, d.id); Assert.IsNotEmpty(d.lodTriangles, d.id); }
            Assert.IsNull(PropLibrary.Def("not_a_prop")); Assert.IsNull(PropLibrary.Geometry("not_a_prop", 0));
        }

        [Test] public void RouteDefinitionPreparesFromDataAndResamplesTheSpline()
        {
            var r = ScriptableObject.CreateInstance<RouteDefinition>();
            try
            {
                r.routeId = "ng-lagos-ibadan"; Assert.IsTrue(r.Prepare(), r.PrepareError); Assert.IsTrue(r.Prepare(), "idempotent");
                Assert.IsNotNull(r.Model); Assert.Greater(r.nodes.Length, 1800); Assert.AreEqual(r.Model.Length, r.TotalLength, r.Model.Length * 0.01f);
                Assert.AreEqual(ZoneType.Industrial, r.nodes[0].zone); Assert.AreEqual(RoadSurface.Good, r.nodes[0].surface);
                Assert.AreEqual(r.Model.Spec.controlPoints[0].elevation, r.nodes[0].position.y, 0.05f);
                var mid = r.PositionAt(r.TotalLength * 0.5f); r.Project(mid, out float lateral); Assert.AreEqual(0f, lateral, 1f);
                Assert.Greater(r.LaneOffset(100f, 0), 1f); Assert.Less(r.LaneOffset(100f, 0), 6f);
                Assert.Greater(r.GroundY(mid.x, mid.z), r.Model.Terrain.HeightAt(mid.x, mid.z) - 0.01f);
            }
            finally { Object.DestroyImmediate(r); }
        }

        [Test] public void MissingRouteDataFallsBackToTheLegacyPolylineInsteadOfFailing()
        {
            var r = ScriptableObject.CreateInstance<RouteDefinition>();
            try
            {
                r.routeId = "no-such-route";
                r.nodes = new[] { new RouteNode { position = Vector3.zero, width = 10 }, new RouteNode { position = new Vector3(100, 0, 0), width = 10 } };
                LogAssert.Expect(LogType.Warning, new Regex("no route data"));
                Assert.IsFalse(r.Prepare()); Assert.IsNull(r.Model); Assert.AreEqual(2, r.nodes.Length); Assert.AreEqual(100f, r.TotalLength, 0.01f);
                Assert.AreEqual(0f, r.GroundY(5, 5)); Assert.AreEqual(3.2f, r.LaneOffset(10f, 0));
            }
            finally { Object.DestroyImmediate(r); }
        }

        [Test] public void GeneratedRoadAndTerrainConvertToValidUnityMeshes()
        {
            var model = RouteModel.Build(LoadSpec()); var p = model.Main.Spline.PositionAt(5000f);
            float x0 = Mathf.Floor(p.X / 200f) * 200f, z0 = Mathf.Floor(p.Z / 200f) * 200f; var org = new V3(x0, 0, z0);
            var road = RoadGeometry.BuildChunk(model.Main, x0, z0, x0 + 200f, z0 + 200f, org, (x, z) => model.Terrain.HeightAt(x, z));
            Assert.IsFalse(road.IsEmpty); Assert.IsTrue(road.Validate(out var err), err);
            Mesh m = null, cm = null, t = null, slab = null;
            try
            {
                m = MeshUtil.ToMesh(road, "road", out var used);
                Assert.AreEqual(used.Length, m.subMeshCount); Assert.AreEqual(road.VertexCount, m.vertexCount); Assert.AreEqual(road.TriangleCount, MeshUtil.Triangles(m));
                Assert.Contains(RoadSub.Asphalt, used); Assert.IsFalse(float.IsNaN(m.bounds.size.x));
                cm = MeshUtil.ToCollisionMesh(road, "roadCol", RoadSub.Asphalt, RoadSub.Shoulder, RoadSub.Verge, RoadSub.Concrete, RoadSub.Metal);
                Assert.AreEqual(1, cm.subMeshCount); Assert.Less(MeshUtil.Triangles(cm), road.TriangleCount, "markings are paint, not collision geometry");
                var tile = TerrainGeometry.BuildTile(model.Terrain, x0, z0, 200f, 40, org); t = MeshUtil.ToMesh(tile, "terrain", out _);
                Assert.AreEqual(tile.TriangleCount, MeshUtil.Triangles(t)); Assert.Greater(t.normals.Length, 0);
                slab = MeshUtil.ToCollisionMesh(TerrainGeometry.BuildCollision(model.Terrain, x0, z0, 200f, LodPolicy.CollisionCells, org), "slab", TerrainSub.Ground);
                Assert.Greater(MeshUtil.Triangles(slab), 0);
                var mats = new Material[RoadSub.Count]; Assert.AreEqual(used.Length, MeshUtil.Pick(mats, used).Length);
            }
            finally { foreach (var o in new Object[] { m, cm, t, slab }) if (o != null) Object.DestroyImmediate(o); }
        }

        [Test] public void LargeMeshesSwitchToThirtyTwoBitIndices()
        {
            var mb = new MeshBuffers(1); for (int i = 0; i < 70000; i++) mb.Add(new V3(i, 0, 0), new V2(0, 0));
            mb.Tri(0, 0, 1, 2);
            var m = MeshUtil.ToMesh(mb, "big", out _);
            try { Assert.AreEqual(UnityEngine.Rendering.IndexFormat.UInt32, m.indexFormat); } finally { Object.DestroyImmediate(m); }
        }

        [Test] public void EveryCataloguedPropBuildsAMeshAtEveryLodWithinItsBudget()
        {
            foreach (var d in PropLibrary.All)
                for (int lod = 0; lod < d.lodTriangles.Length; lod++)
                {
                    var mb = PropLibrary.Geometry(d.id, lod); Assert.IsNotNull(mb, d.id);
                    var m = MeshUtil.ToMesh(mb, d.id, out var used);
                    try { Assert.LessOrEqual(MeshUtil.Triangles(m), d.lodTriangles[lod], d.id + " lod" + lod); Assert.LessOrEqual(used.Length, PropSub.Count); }
                    finally { Object.DestroyImmediate(m); }
                }
        }

        [Test] public void MaterialsBuildForEverySlotWithTexturesWhereIntended()
        {
            var w = WorldMaterials.Create(null);
            try
            {
                for (int i = 0; i < RoadSub.Count; i++) Assert.IsNotNull(w.Road[i], "road slot " + i);
                for (int i = 0; i < PropSub.Count; i++) Assert.IsNotNull(w.Prop[i], "prop slot " + i);
                Assert.IsNotNull(w.TerrainLush); Assert.IsNotNull(w.TerrainDry); Assert.AreEqual(2, w.WeatherSet.Length);
                Assert.IsNotNull(w.Road[RoadSub.Asphalt].GetTexture("_BaseMap")); Assert.IsNull(w.Road[RoadSub.MarkWhite].GetTexture("_BaseMap"));
                Assert.AreEqual(256, w.Road[RoadSub.Asphalt].GetTexture("_BaseMap").width);
            }
            finally { w.DestroyAll(); }
        }

        [Test] public void ProceduralTexturesTileSeamlesslyAndStayInsideTheMemoryBudget()
        {
            var t = ProcTex.Grain(64, new Color(0.1f, 0.1f, 0.1f), new Color(0.3f, 0.3f, 0.3f), 3, 5, 0f, keepReadable: true);   // no speckle: pure periodic noise
            var lean = ProcTex.Grain(64, new Color(0.1f, 0.1f, 0.1f), new Color(0.3f, 0.3f, 0.3f), 3, 5, 0f);                       // the form the game uses
            try
            {
                var px = t.GetPixels32(); float seam = 0f, interior = 0f;
                for (int y = 0; y < 64; y++) { seam += Mathf.Abs(px[y * 64].r - px[y * 64 + 63].r); interior += Mathf.Abs(px[y * 64 + 31].r - px[y * 64 + 32].r); }
                Assert.LessOrEqual(seam, interior * 2f + 64f, "left and right edges must continue into each other");
                Assert.AreEqual(TextureWrapMode.Repeat, t.wrapMode);
                Assert.IsTrue(t.isReadable); Assert.IsFalse(lean.isReadable, "game textures drop their CPU copy after upload");
                long readable = UnityEngine.Profiling.Profiler.GetRuntimeMemorySizeLong(t), gpuOnly = UnityEngine.Profiling.Profiler.GetRuntimeMemorySizeLong(lean);
                Assert.LessOrEqual(gpuOnly, readable, "dropping the CPU copy never costs memory"); Assert.Less(gpuOnly, 64 * 64 * 4 * 3 / 2, "RGBA32 + mip chain only");
            }
            finally { Object.DestroyImmediate(t); Object.DestroyImmediate(lean); }
        }

        [Test] public void ProductionPrefabsOverrideThePlaceholderForThatPropOnly()
        {
            PropLibrary.EnsureLoaded(); var stand = new GameObject("stand-in");
            try
            {
                Assert.IsNull(PropLibrary.ProductionPrefab("palm_tree"));
                PropLibrary.OverrideProduction("palm_tree", stand);
                Assert.AreSame(stand, PropLibrary.ProductionPrefab("palm_tree")); Assert.IsNull(PropLibrary.ProductionPrefab("bush"));
            }
            finally { PropLibrary.ClearOverrides(); Object.DestroyImmediate(stand); }
        }

        [Test] public void QualitySelectionHonoursTheUrlAndTheDevice()
        {
            Assert.AreEqual("low", RenderingRig.Select("http://x/index.html?quality=low", false, 16000, 8000).Name);
            Assert.AreEqual("high", RenderingRig.Select("http://x/index.html?smoke=drive&quality=HIGH#frag", true, 1000, 100).Name);
            Assert.AreEqual("low", RenderingRig.Select("http://x/index.html", true, 8000, 4000).Name);
            Assert.AreEqual("medium", RenderingRig.Select(null, false, 0, 0).Name);
            Assert.AreEqual("medium", RenderingRig.Select("http://x/?quality=ultra", false, 4000, 1000).Name);
        }

        [Test] public void ResourcesStayInsideTheDownloadBudget()
        {
            long total = 0;
            foreach (var f in Directory.GetFiles("Assets/_Project/Resources", "*", SearchOption.AllDirectories))
            {
                if (f.EndsWith(".meta")) continue; if (f.Replace('\\', '/').Contains("/Fonts/")) continue;
                long len = new FileInfo(f).Length; total += len;
                Assert.LessOrEqual(len, 1_500_000, f + " is " + len / 1024 + " KB (limit 1.5 MB per file)");
            }
            Assert.LessOrEqual(total, 6_000_000, "Resources total " + total / 1024 + " KB (limit 6 MB, excluding fonts)");
        }

        [Test] public void StreamerFallsBackToTheLegacyPathWithoutRouteData()
        {
            var go = new GameObject("streamer"); var s = go.AddComponent<ChunkStreamer>();
            try { Assert.IsFalse(s.UsesRouteModel); s.route = ScriptableObject.CreateInstance<RouteDefinition>(); Assert.IsFalse(s.UsesRouteModel); Assert.AreEqual(0, s.PropCount); }
            finally { if (s.route != null) Object.DestroyImmediate(s.route); Object.DestroyImmediate(go); }
        }
    }
}
