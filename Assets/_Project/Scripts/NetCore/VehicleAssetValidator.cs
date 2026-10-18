using System;
using System.Collections.Generic;
using System.Linq;

namespace ARO.NetCore
{
    /// <summary>What an incoming vehicle model reports about itself (the converter fills this from the imported asset; the ARO 3D Studio sidecar JSON carries the same numbers). Plain data, so the rules below can be tested without any art file.</summary>
    public sealed class VehicleAssetManifest
    {
        public string Id = "";
        public bool Hero = true;                                   // false = traffic or convoy-partner vehicle
        public int[] LodTriangles = new int[0];                    // LOD0, LOD1, ...
        public int Renderers;                                      // LOD0 renderers after merging
        public int Materials, MaxTextureSize; public float TextureMemoryMb; public bool UsesStandardShader;
        public float LengthM, GroundY, SteeringWheelX, FrontLightZ;
        public string[] Nodes = new string[0];
        public float[] WheelVisualRadius = new float[0], WheelColliderRadius = new float[0];
        public int[] CollisionHullTriangles = new int[0];
        public bool CenterOfMassInsideBounds = true;
    }

    public enum AssetSeverity { Error, Warning }

    public sealed class AssetFinding
    {
        public AssetSeverity Severity; public string Rule, Message;
        public override string ToString() => Severity + " [" + Rule + "] " + Message;
    }

    /// <summary>
    /// The acceptance rules for a vehicle model (docs/ASSET_PIPELINE_PLAN.md, section 4), as code. Nothing here loads an asset or touches the vehicle system: it only judges numbers. An Error blocks the model;
    /// a Warning is reported. The numbers are budgets chosen from the one measured asset (the TITAN 480 fixture: 42,140 / 56% / 30% / 15% triangles over four LODs, 165 meshes, 15 materials, no textures) and
    /// stay estimates until measured on a real build.
    /// </summary>
    public static class VehicleAssetValidator
    {
        public const int HeroLod0Max = 60000, TrafficLod0Max = 12000, HeroRenderersMax = 40, TrafficRenderersMax = 8, MaterialsMax = 20, TextureMax = 2048, HullTrianglesMax = 255;
        public const float HeroTextureMb = 16f, TrafficTextureMb = 2f, GroundTolerance = 0.05f, WheelRadiusTolerance = 0.05f, LengthMin = 5f, LengthMax = 20f;
        static readonly float[] LodRatioMax = { 1f, 0.60f, 0.35f, 0.20f };

        static void Add(List<AssetFinding> l, AssetSeverity s, string rule, string msg) => l.Add(new AssetFinding { Severity = s, Rule = rule, Message = msg });
        static bool Has(string[] nodes, string prefix) => nodes.Any(n => n != null && n.StartsWith(prefix, StringComparison.Ordinal));

        /// <param name="licenceKnown">Whether the asset id has an entry in docs/ASSET_LICENSES.md with the owner's confirmation.</param>
        public static List<AssetFinding> Validate(VehicleAssetManifest m, Func<string, bool> licenceKnown)
        {
            var f = new List<AssetFinding>();
            if (m == null || string.IsNullOrEmpty(m.Id)) { Add(f, AssetSeverity.Error, "identity", "the asset has no id"); return f; }
            if (licenceKnown == null || !licenceKnown(m.Id)) Add(f, AssetSeverity.Error, "licence", $"{m.Id} has no confirmed licence entry (docs/ASSET_LICENSES.md); it may not be used or distributed");

            // triangles
            if (m.LodTriangles.Length == 0) Add(f, AssetSeverity.Error, "lods", "no LOD triangle counts reported");
            else
            {
                int lod0 = m.LodTriangles[0], cap = m.Hero ? HeroLod0Max : TrafficLod0Max;
                if (lod0 > cap) Add(f, AssetSeverity.Error, "triangles", $"LOD0 has {lod0} triangles; the {(m.Hero ? "hero" : "traffic")} budget is {cap}");
                if (m.Hero && m.LodTriangles.Length < 3) Add(f, AssetSeverity.Error, "lods", $"a hero vehicle needs at least 3 LODs, this has {m.LodTriangles.Length}");
                for (int i = 1; i < m.LodTriangles.Length && lod0 > 0; i++)
                {
                    float max = LodRatioMax[Math.Min(i, LodRatioMax.Length - 1)], r = m.LodTriangles[i] / (float)lod0;
                    if (r > max) Add(f, AssetSeverity.Error, "lods", $"LOD{i} is {r:P0} of LOD0; the limit is {max:P0}");
                }
            }

            // draw calls, materials, textures
            int rcap = m.Hero ? HeroRenderersMax : TrafficRenderersMax;
            if (m.Renderers > rcap) Add(f, AssetSeverity.Error, "renderers", $"{m.Renderers} LOD0 renderers after merging; the budget is {rcap}");
            if (m.Materials > MaterialsMax) Add(f, AssetSeverity.Error, "materials", $"{m.Materials} materials; the budget is {MaterialsMax}");
            if (m.UsesStandardShader) Add(f, AssetSeverity.Error, "shader", "uses the built-in Standard shader; only URP/Lit is allowed");
            if (m.MaxTextureSize > TextureMax) Add(f, AssetSeverity.Error, "textures", $"a texture is {m.MaxTextureSize} px; the limit is {TextureMax}");
            float mb = m.Hero ? HeroTextureMb : TrafficTextureMb;
            if (m.TextureMemoryMb > mb) Add(f, AssetSeverity.Error, "textures", $"{m.TextureMemoryMb:0.#} MB of textures; the budget is {mb:0.#} MB");

            // orientation, scale, pivot
            if (Math.Abs(m.GroundY) > GroundTolerance) Add(f, AssetSeverity.Error, "pivot", $"the ground is at y = {m.GroundY:0.###}; it must be 0 (+/- {GroundTolerance})");
            if (m.LengthM < LengthMin || m.LengthM > LengthMax) Add(f, AssetSeverity.Error, "scale", $"length {m.LengthM:0.##} m is outside {LengthMin} to {LengthMax} m (is 1 unit one metre?)");
            if (m.FrontLightZ <= 0f) Add(f, AssetSeverity.Error, "orientation", "the front lights are not at z > 0: the vehicle does not face +Z");
            if (m.SteeringWheelX >= 0f) Add(f, AssetSeverity.Error, "orientation", "the steering wheel is not at x < 0: the cab must be left-hand drive (or mirrored)");

            // rig
            if (!Has(m.Nodes, "Susp_Axle_")) Add(f, AssetSeverity.Error, "rig", "no Susp_Axle_* nodes (axle holders)");
            if (m.Nodes.Count(n => n != null && n.StartsWith("Spin_", StringComparison.Ordinal)) < 4) Add(f, AssetSeverity.Error, "rig", "fewer than 4 Spin_* nodes (wheel visuals)");
            if (!Has(m.Nodes, "Light_Head_")) Add(f, AssetSeverity.Error, "rig", "no Light_Head_* nodes (headlight emitters)");
            if (!Has(m.Nodes, "Attach_Camera_Chase")) Add(f, AssetSeverity.Error, "rig", "no Attach_Camera_Chase node");
            if (!Has(m.Nodes, "COL_")) Add(f, AssetSeverity.Error, "collision", "no COL_* collision hulls");
            if (m.WheelVisualRadius.Length != m.WheelColliderRadius.Length) Add(f, AssetSeverity.Error, "rig", "the wheel visuals and wheel colliders are not the same in number");
            else for (int i = 0; i < m.WheelVisualRadius.Length; i++)
                if (m.WheelVisualRadius[i] <= 0f || Math.Abs(m.WheelColliderRadius[i] - m.WheelVisualRadius[i]) > WheelRadiusTolerance * m.WheelVisualRadius[i])
                    Add(f, AssetSeverity.Error, "rig", $"wheel {i}: collider radius {m.WheelColliderRadius[i]:0.###} differs from the visual {m.WheelVisualRadius[i]:0.###} by more than {WheelRadiusTolerance:P0}");
            if (!m.CenterOfMassInsideBounds) Add(f, AssetSeverity.Error, "rig", "the centre of mass is outside the vehicle's bounds");
            foreach (int t in m.CollisionHullTriangles) if (t > HullTrianglesMax) { Add(f, AssetSeverity.Error, "collision", $"a collision hull has {t} triangles; the limit is {HullTrianglesMax}"); break; }
            return f;
        }

        public static bool Passes(List<AssetFinding> findings) => findings.All(x => x.Severity != AssetSeverity.Error);
    }
}
