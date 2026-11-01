using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using ARO.Editor.BlenderImport;
using ARO.NetCore;
using ARO.World;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace ARO.Tests
{
    /// <summary>
    /// The real import test for assets made by the Blender toolchain (tools/blender, docs/BLENDER_PIPELINE.md). CI generates a deliberately asymmetric calibration model with the toolchain and puts it in
    /// Assets/ThirdParty/ARO_Calibration (git-ignored: it is never committed); Unity imports it as it would any FBX or GLB, and these tests measure what came out: which way front, right and up point (and
    /// whether the model is mirrored), the size in metres, stray rotations and scales, which material is on which part, the collision box, the LOD triangle counts against the catalogue entry.
    /// Without the fixture folder (a normal developer machine, or the main CI job) the tests are skipped, not failed.
    /// FBX: every export-setting variant the toolchain can write is imported and reported, and the toolchain's chosen default must be correct. GLB: needs the glTFast package (the workflow installs it and
    /// leaves a REQUIRE_GLB marker so a missing importer fails instead of skipping).
    /// </summary>
    public class BlenderAssetImportTests
    {
        const string Dir = "Assets/ThirdParty/ARO_Calibration/";
        const float Tol = 0.03f;
        static readonly Dictionary<string, Vector3> Centres = new Dictionary<string, Vector3>
        {
            { "marker_front", new Vector3(0f, 0.75f, 3.5f) }, { "marker_rear", new Vector3(0f, 0.75f, -3.5f) }, { "marker_right", new Vector3(1.4f, 0.75f, 0f) },
            { "marker_left", new Vector3(-1.4f, 0.75f, 0f) }, { "marker_top", new Vector3(0f, 1.9f, 0f) }
        };
        static readonly Dictionary<string, string> MarkerMaterial = new Dictionary<string, string>
        { { "marker_front", "M_paint_red" }, { "marker_rear", "M_paint_white" }, { "marker_right", "M_leaf_green" }, { "marker_left", "M_road_yellow" }, { "marker_top", "M_metal_gate_blue" }, { "aro_calibration_body", "M_concrete" } };

        [Serializable] class ManifestWrap { public PropDef catalog_entry; public string fbx_default_variant; }

        sealed class Obs
        {
            public string Label; public bool Imported; public GameObject Prefab;
            public readonly Dictionary<string, Vector3> Centre = new Dictionary<string, Vector3>();
            public int RotatedNodes; public Vector3 BodySize; public readonly List<string> Faults = new List<string>(), Info = new List<string>();
            public readonly Dictionary<string, string> MaterialName = new Dictionary<string, string>(); public readonly Dictionary<string, Color> MaterialColour = new Dictionary<string, Color>();
        }

        static bool FixturesPresent => Directory.Exists(Dir) && File.Exists(Dir + "aro_calibration.manifest.json");
        static ManifestWrap Manifest() => JsonUtility.FromJson<ManifestWrap>(File.ReadAllText(Dir + "aro_calibration.manifest.json"));

        static Color BaseColour(Material m)
        {
            if (m == null) return Color.magenta;
            foreach (var p in new[] { "baseColorFactor", "_BaseColor", "_Color" }) if (m.HasProperty(p)) return m.GetColor(p);
            return Color.magenta;
        }

        static Obs Observe(string label, string assetPath)
        {
            var o = new Obs { Label = label };
            AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceSynchronousImport);
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(assetPath); o.Prefab = prefab;
            if (prefab == null) { o.Faults.Add("the asset did not import as a GameObject (no importer for this file type?)"); return o; }
            o.Imported = true;
            var imp = AssetImporter.GetAtPath(assetPath) as ModelImporter;
            if (imp != null) o.Info.Add($"ModelImporter: globalScale={imp.globalScale} useFileScale={imp.useFileScale} fileScale={imp.fileScale}");
            var go = UnityEngine.Object.Instantiate(prefab);          // the importer's own root transform is kept: it may carry the axis conversion
            var re = go.transform.localEulerAngles; o.Info.Add("imported root: local euler (" + re.x.ToString("0.#") + ", " + re.y.ToString("0.#") + ", " + re.z.ToString("0.#") + ") local scale " + Fmt(go.transform.localScale));
            try
            {
                var rotated = new List<string>();
                foreach (var t in go.GetComponentsInChildren<Transform>(true))
                {
                    var s = t.lossyScale;
                    if (t != go.transform && Quaternion.Angle(t.localRotation, Quaternion.identity) > 0.5f) rotated.Add(t.name);
                    if (Mathf.Abs(s.x - 1f) > 0.001f || Mathf.Abs(s.y - 1f) > 0.001f || Mathf.Abs(s.z - 1f) > 0.001f) o.Faults.Add("node '" + t.name + "' has a world scale of " + Fmt(s) + " (a clean import is 1)");
                }
                o.Info.Add("nodes below the root that carry a rotation: " + rotated.Count + (rotated.Count > 0 ? " (" + string.Join(", ", rotated.Take(4)) + (rotated.Count > 4 ? ", ..." : "") + ")" : ""));
                o.RotatedNodes = rotated.Count;
                foreach (var kv in Centres)
                {
                    var t = BlenderAssetBuilder.Find(go.transform, kv.Key); var r = t != null ? t.GetComponent<Renderer>() : null;
                    if (r == null) { o.Faults.Add($"'{kv.Key}' was not found with a renderer"); continue; }
                    o.Centre[kv.Key] = r.bounds.center;
                    if (Vector3.Distance(r.bounds.center, kv.Value) > Tol) o.Faults.Add($"'{kv.Key}' is at {Fmt(r.bounds.center)} in Unity; expected {Fmt(kv.Value)}");
                }
                var body = BlenderAssetBuilder.Find(go.transform, "aro_calibration_body"); var br = body != null ? body.GetComponent<Renderer>() : null;
                if (br == null) o.Faults.Add("the body was not found");
                else
                {
                    o.BodySize = br.bounds.size;
                    if (Vector3.Distance(br.bounds.size, new Vector3(2f, 1.5f, 6f)) > Tol) o.Faults.Add($"the body measures {Fmt(br.bounds.size)} m; expected (2, 1.5, 6) in X, Y, Z");
                    if (Vector3.Distance(br.bounds.center, new Vector3(0f, 0.75f, 0f)) > Tol) o.Faults.Add($"the body's centre is at {Fmt(br.bounds.center)}; expected (0, 0.75, 0): the origin is not on the ground at the centre");
                }
                foreach (var kv in MarkerMaterial)
                {
                    var t = BlenderAssetBuilder.Find(go.transform, kv.Key); var r = t != null ? t.GetComponent<Renderer>() : null; if (r == null) continue;
                    var m = r.sharedMaterials.FirstOrDefault(); string n = m != null ? m.name : "(none)"; o.MaterialName[kv.Key] = n; var c = BaseColour(m); o.MaterialColour[kv.Key] = c;
                    if (!n.StartsWith(kv.Value)) o.Faults.Add($"'{kv.Key}' has material '{n}'; expected '{kv.Value}'");
                    if (r.sharedMaterials.Length != 1) o.Faults.Add($"'{kv.Key}' has {r.sharedMaterials.Length} material slots; expected 1");
                }
                CheckHue(o, "marker_front", c => c.r > c.g * 1.5f && c.r > c.b * 1.5f, "red");
                CheckHue(o, "marker_right", c => c.g > c.r * 1.5f && c.g > c.b * 1.5f, "green");
                CheckHue(o, "marker_left", c => c.r > c.b * 3f && c.g > c.b * 3f, "yellow");
                CheckHue(o, "marker_top", c => c.b > c.r * 1.5f && c.b > c.g * 1.2f, "blue");
                CheckHue(o, "marker_rear", c => Mathf.Min(c.r, Mathf.Min(c.g, c.b)) > 0.5f * Mathf.Max(c.r, Mathf.Max(c.g, c.b)) && Mathf.Max(c.r, Mathf.Max(c.g, c.b)) > 0.4f, "white");
            }
            finally { UnityEngine.Object.DestroyImmediate(go); }
            return o;
        }

        static void CheckHue(Obs o, string part, Func<Color, bool> ok, string what)
        {
            if (!o.MaterialColour.TryGetValue(part, out var c)) return;
            if (!ok(c)) o.Faults.Add($"'{part}' base colour ({c.r:0.###}, {c.g:0.###}, {c.b:0.###}) does not read as {what}");
        }

        static string Fmt(Vector3 v) => $"({v.x:0.###}, {v.y:0.###}, {v.z:0.###})";

        static string Report(Obs o)
        {
            var sb = new StringBuilder($"[BlenderImport] {o.Label}: {(o.Faults.Count == 0 ? "OK" : o.Faults.Count + " fault(s)")}\n");
            foreach (var kv in o.Centre)
            {
                string mat; if (!o.MaterialName.TryGetValue(kv.Key, out mat)) mat = "?";
                Color col; string colText = o.MaterialColour.TryGetValue(kv.Key, out col) ? "(" + col.r.ToString("0.###") + ", " + col.g.ToString("0.###") + ", " + col.b.ToString("0.###") + ")" : "?";
                sb.AppendLine("    " + kv.Key + " centre " + Fmt(kv.Value) + "  material " + mat + "  base colour " + colText);
            }
            sb.AppendLine($"    body size {Fmt(o.BodySize)}");
            foreach (var i in o.Info) sb.AppendLine("    " + i);
            foreach (var f in o.Faults.Take(12)) sb.AppendLine("    FAULT: " + f);
            return sb.ToString();
        }

        [Test] public void EveryFbxExportVariantIsImportedAndMeasured_AtLeastOneIsCorrect_AndTheChosenDefaultIs()
        {
            if (!FixturesPresent) Assert.Ignore("no calibration fixture (tools/blender/make_fixtures.py generates it in CI)");
            var man = Manifest(); var files = Directory.GetFiles(Dir, "aro_calibration__*.fbx").OrderBy(f => f).ToArray();
            Assert.IsNotEmpty(files, "no FBX variants were generated");
            var results = new List<Obs>(); foreach (var f in files) { var tag = Path.GetFileNameWithoutExtension(f).Replace("aro_calibration__", ""); results.Add(Observe("FBX " + tag, f.Replace('\\', '/'))); }
            foreach (var r in results) Debug.Log(Report(r));
            var good = results.Where(r => r.Imported && r.Faults.Count == 0).Select(r => r.Label).ToList();
            var clean = results.Where(r => r.Imported && r.Faults.Count == 0 && r.RotatedNodes == 0).Select(r => r.Label).ToList();
            Debug.Log("[BlenderImport] summary: " + good.Count + " of " + results.Count + " FBX variants import correctly: " + string.Join(", ", good) + "; of those " + clean.Count + " also carry no rotation on any node: " + string.Join(", ", clean) + "; toolchain default: " + man.fbx_default_variant);
            Assert.IsNotEmpty(good, "no FBX export variant imports correctly into Unity:\n" + string.Join("\n", results.Select(r => r.Label + ": " + string.Join(" | ", r.Faults.Take(3)))));
            var def = results.FirstOrDefault(r => r.Label == "FBX " + man.fbx_default_variant);
            Assert.NotNull(def, "the toolchain's default variant '" + man.fbx_default_variant + "' was not among the generated variants");
            Assert.IsEmpty(def.Faults, "the toolchain's default FBX settings (" + man.fbx_default_variant + ") import wrongly; correct variants: [" + string.Join(", ", good) + "]\n" + string.Join("\n", def.Faults));
        }

        [Test] public void TheDefaultFbxBecomesAProductionPrefabThatMatchesItsCatalogueEntry()
        {
            if (!FixturesPresent) Assert.Ignore("no calibration fixture");
            var man = Manifest(); var def = man.catalog_entry; Assert.NotNull(def, "the manifest carries no catalogue entry");
            AssetDatabase.ImportAsset(Dir + "aro_calibration.fbx", ImportAssetOptions.ForceSynchronousImport);
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(Dir + "aro_calibration.fbx"); Assert.NotNull(model, "aro_calibration.fbx did not import");
            var kit = BlenderAssetBuilder.LoadKit(Dir + "aro_materials.json");
            var built = BlenderAssetBuilder.Build(model, def, kit, Dir + "Materials");
            try
            {
                Assert.IsEmpty(built.UnmatchedMaterials, "materials the kit does not know: " + string.Join(", ", built.UnmatchedMaterials));
                var errors = PropPrefabPlan.Check(def, built.Measure);
                var m = built.Measure; var lg = built.Root.GetComponent<LODGroup>();
                string box = m.ColliderSize.Length == 3 ? "(" + m.ColliderSize[0].ToString("0.###") + ", " + m.ColliderSize[1].ToString("0.###") + ", " + m.ColliderSize[2].ToString("0.###") + ")" : "";
                Debug.Log("[BlenderImport] prefab: size (" + m.WidthM.ToString("0.###") + ", " + m.HeightM.ToString("0.###") + ", " + m.DepthM.ToString("0.###") + ") lodTriangles [" + string.Join(",", m.LodTriangles) + "] materials " + m.MaterialCount +
                          " shaders [" + string.Join(", ", m.ShaderNames.Distinct()) + "] instanced " + m.AllMaterialsInstanced + " boxCollider " + m.HasBoxCollider + " " + box + " lodGroupLevels " + (lg != null ? lg.lodCount : 0));
                if (lg != null) { var l = lg.GetLODs(); Debug.Log("[BlenderImport] lod thresholds: " + string.Join(", ", l.Select(x => x.screenRelativeTransitionHeight.ToString("0.####")))); }
                Assert.IsEmpty(errors, string.Join("\n", errors));
                Assert.NotNull(lg, "no LODGroup"); Assert.AreEqual(def.lodTriangles.Length, lg.lodCount);
                var expected = PropPrefabPlan.LodScreenHeights(def.lodTriangles.Length, Mathf.Max(m.WidthM, Mathf.Max(m.HeightM, m.DepthM)), def.cullDistance);
                var got = lg.GetLODs(); for (int i = 0; i < expected.Length; i++) Assert.AreEqual(expected[i], got[i].screenRelativeTransitionHeight, 1e-4f, $"LOD{i} transition height");
                var boxes = built.Root.GetComponentsInChildren<BoxCollider>(); Assert.AreEqual(1, boxes.Length, "one collision box expected");
                var wb = BlenderAssetBuilder.WorldBox(boxes[0]); Debug.Log("[BlenderImport] collision box in world space: centre " + Fmt(wb.center) + " size " + Fmt(wb.size));
                Assert.AreEqual(2f, wb.size.x, Tol); Assert.AreEqual(1.5f, wb.size.y, Tol); Assert.AreEqual(6f, wb.size.z, Tol); Assert.AreEqual(0.75f, wb.center.y, Tol, "the box sits on the ground");
                Assert.AreEqual(0f, wb.center.x, Tol); Assert.AreEqual(0f, wb.center.z, Tol);
                Assert.AreEqual(Quaternion.identity, built.Root.transform.localRotation, "the prefab root must have no rotation: the game overwrites it with the yaw"); Assert.AreEqual(Vector3.one, built.Root.transform.localScale);
                // every part carries the kit's colour (sRGB, as Unity expects), not the importer's guess
                foreach (var kv in MarkerMaterial)
                {
                    var t = BlenderAssetBuilder.Find(built.Root.transform, kv.Key); var r = t != null ? t.GetComponent<Renderer>() : null; Assert.NotNull(r, kv.Key);
                    var entry = kit.materials.First(e => e.name == kv.Value); var c = r.sharedMaterial.GetColor("_BaseColor"); var want = BlenderAssetBuilder.KitColour(entry);
                    Assert.AreEqual(kv.Value, r.sharedMaterial.name, kv.Key + " material"); Assert.AreEqual(want.r, c.r, 0.002f, kv.Key + " red"); Assert.AreEqual(want.g, c.g, 0.002f, kv.Key + " green"); Assert.AreEqual(want.b, c.b, 0.002f, kv.Key + " blue");
                    Assert.AreEqual(1f - entry.roughness, r.sharedMaterial.GetFloat("_Smoothness"), 0.002f, kv.Key + " smoothness"); Assert.IsTrue(r.sharedMaterial.enableInstancing, kv.Key + " instancing");
                }
            }
            finally { UnityEngine.Object.DestroyImmediate(built.Root); }
        }

        [Test] public void AProductionPrefabKeptUnderThePrivateAssetPathIsFoundByTheGamesOwnLookup()
        {
            // The seam the private asset repository relies on: assets live under Assets/ThirdParty (git-ignored, put there at CI or development time) and the game finds Resources/Props/{id}.prefab
            // there exactly as it would in the public tree, through PropLibrary.ProductionPrefab, the same call ChunkStreamer.StageProps makes.
            if (!FixturesPresent) Assert.Ignore("no calibration fixture");
            var man = Manifest(); var def = man.catalog_entry;
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(Dir + "aro_calibration.fbx"); Assert.NotNull(model, "aro_calibration.fbx did not import");
            var folder = Dir + "Resources/Props"; Directory.CreateDirectory(folder); AssetDatabase.Refresh();
            var built = BlenderAssetBuilder.Build(model, def); string path = folder + "/" + def.id + ".prefab";
            try
            {
                var saved = PrefabUtility.SaveAsPrefabAsset(built.Root, path); Assert.NotNull(saved, "the prefab was not saved");
                AssetDatabase.Refresh(); PropLibrary.ClearOverrides();
                var found = PropLibrary.ProductionPrefab(def.id);
                Assert.NotNull(found, "PropLibrary.ProductionPrefab('" + def.id + "') did not find " + path + ": a production prefab under Assets/ThirdParty/**/Resources/Props is not seen by the game");
                Assert.NotNull(found.GetComponent<LODGroup>(), "the found prefab has no LODGroup"); Assert.AreEqual(def.lodTriangles.Length, found.GetComponent<LODGroup>().lodCount);
                Assert.AreEqual(1, found.GetComponentsInChildren<BoxCollider>(true).Length, "the found prefab has no collision box");
                Debug.Log("[BlenderImport] the game's own lookup (PropLibrary.ProductionPrefab) found " + path + " with a LODGroup of " + found.GetComponent<LODGroup>().lodCount + " levels and " + found.GetComponentsInChildren<BoxCollider>(true).Length + " BoxCollider");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(built.Root); PropLibrary.ClearOverrides();
                AssetDatabase.DeleteAsset(path); AssetDatabase.DeleteAsset(Dir + "Resources");
            }
        }

        [Test] public void TheGlbImportsWithTheSameOrientationScaleAndMaterialNames()
        {
            if (!FixturesPresent) Assert.Ignore("no calibration fixture");
            bool required = File.Exists(Dir + "REQUIRE_GLB");
            AssetDatabase.ImportAsset(Dir + "aro_calibration.glb", ImportAssetOptions.ForceSynchronousImport);
            if (AssetDatabase.LoadAssetAtPath<GameObject>(Dir + "aro_calibration.glb") == null)
            {
                if (required) Assert.Fail("the GLB did not import although the workflow installed glTFast (REQUIRE_GLB is present)");
                Assert.Ignore("no GLB importer (install com.unity.cloud.gltfast to test GLB)");
            }
            var o = Observe("GLB", Dir + "aro_calibration.glb"); Debug.Log(Report(o));
            Assert.IsEmpty(o.Faults, string.Join("\n", o.Faults));
            var man = Manifest(); var kit = BlenderAssetBuilder.LoadKit(Dir + "aro_materials.json"); var built = BlenderAssetBuilder.Build(o.Prefab, man.catalog_entry, kit, Dir + "Materials");
            try
            {
                var errors = PropPrefabPlan.Check(man.catalog_entry, built.Measure);
                Debug.Log("[BlenderImport] GLB prefab: shaders [" + string.Join(", ", built.Measure.ShaderNames.Distinct()) + "] lodTriangles [" + string.Join(",", built.Measure.LodTriangles) + "]");
                Assert.IsEmpty(errors, string.Join("\n", errors));
            }
            finally { UnityEngine.Object.DestroyImmediate(built.Root); }
        }
    }
}
