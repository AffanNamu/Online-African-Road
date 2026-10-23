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

        // Provenance (the quality gate, docs/ASSET_PIPELINE_PLAN.md section 14). JsonUtility-compatible: the ARO 3D Studio sidecar or the converter fills them.
        public string Origin = "";                                 // licensed | commissioned | generated | fixture
        public string Source = "";                                 // the listing, the contract, or the generator and its version
        public bool GeneratorTermsConfirmed;                       // generated only: the generator's terms allow commercial use and the owner confirmed it
        public bool Retopologised, UvsAuthored;                    // generated only: a person rebuilt the topology and the UVs (an AI mesh is a blockout, not an asset)
        public string ReviewedBy = ""; public bool VisualReviewPassed;   // generated only: a named person compared it with the reference and accepted the look
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
            Provenance(m, f);
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

        /// <summary>Where the model came from, and what that origin must additionally prove. A generated model is a candidate, never an automatic acceptance.</summary>
        static void Provenance(VehicleAssetManifest m, List<AssetFinding> f)
        {
            string o = (m.Origin ?? "").Trim();
            if (o != Licensed && o != Commissioned && o != Generated && o != Fixture) { Add(f, AssetSeverity.Error, "provenance", $"unknown origin '{m.Origin}': it must be licensed, commissioned, generated or fixture"); return; }
            if (o == Fixture) { Add(f, AssetSeverity.Error, "gate", "a fixture (for example the TITAN reference) only exercises the pipeline: it is never accepted for gameplay"); return; }
            if (string.IsNullOrWhiteSpace(m.Source)) Add(f, AssetSeverity.Error, "provenance", "no source recorded (the listing, the contract, or the generator and its version)");
            if (o != Generated) return;
            if (!m.GeneratorTermsConfirmed) Add(f, AssetSeverity.Error, "provenance", "the generator's terms have not been confirmed to allow commercial use of its output");
            if (!m.Retopologised) Add(f, AssetSeverity.Error, "generated", "the topology was not rebuilt by a person: a generated mesh is a blockout, not an asset");
            if (!m.UvsAuthored) Add(f, AssetSeverity.Error, "generated", "the UVs were not authored by a person");
            if (string.IsNullOrWhiteSpace(m.ReviewedBy) || !m.VisualReviewPassed) Add(f, AssetSeverity.Error, "generated", "no named person has compared it with the reference and accepted the look");
        }

        public const string Licensed = "licensed", Commissioned = "commissioned", Generated = "generated", Fixture = "fixture";

        public static bool Passes(List<AssetFinding> findings) => findings.All(x => x.Severity != AssetSeverity.Error);
    }
    public enum GateOutcome { Accepted, Rejected }

    /// <summary>The verdict for one candidate model. A rejected candidate leaves the game exactly as it is; when a licensed or commissioned model that has itself passed is named as the fallback it is what stays in use.</summary>
    public sealed class GateDecision
    {
        public GateOutcome Outcome; public string AssetId, FallbackId; public List<AssetFinding> Findings = new List<AssetFinding>();
        public bool UseFallback => Outcome == GateOutcome.Rejected && !string.IsNullOrEmpty(FallbackId);
    }

    /// <summary>
    /// The single question "may this model enter gameplay?". It applies every rule above, and one more for generated models: they may only be accepted while a licensed or commissioned game-ready model that
    /// passes the same rules exists as the fallback, so a bad generation can always be backed out without touching the vehicle system. Nothing here changes any asset or any vehicle definition.
    /// </summary>
    public static class VehicleAssetGate
    {
        public static GateDecision Decide(VehicleAssetManifest candidate, Func<string, bool> licenceKnown, VehicleAssetManifest fallback)
        {
            var d = new GateDecision { AssetId = candidate?.Id, Findings = VehicleAssetValidator.Validate(candidate, licenceKnown) };
            bool fallbackOk = fallback != null && (fallback.Origin == VehicleAssetValidator.Licensed || fallback.Origin == VehicleAssetValidator.Commissioned)
                              && fallback.Id != candidate?.Id && VehicleAssetValidator.Passes(VehicleAssetValidator.Validate(fallback, licenceKnown));
            if (candidate != null && candidate.Origin == VehicleAssetValidator.Generated && !fallbackOk)
                d.Findings.Add(new AssetFinding { Severity = AssetSeverity.Error, Rule = "fallback", Message = "a generated model may only enter gameplay while a licensed or commissioned game-ready model that passes the same rules is the fallback" });
            d.Outcome = VehicleAssetValidator.Passes(d.Findings) ? GateOutcome.Accepted : GateOutcome.Rejected;
            if (fallbackOk) d.FallbackId = fallback.Id;
            return d;
        }
    }
}
