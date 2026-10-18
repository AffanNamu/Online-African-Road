using System.Linq;
using ARO.NetCore;
using Xunit;

/// <summary>The acceptance rules for a vehicle model (docs/ASSET_PIPELINE_PLAN.md, section 4). The good manifest has the shape of the one measured asset (the TITAN 480 fixture: 42,140 / 56% / 30% / 15% triangles, 15 materials, no textures);
/// no art file is involved.</summary>
public class VehicleAssetValidatorTests
{
    static VehicleAssetManifest Good() => new VehicleAssetManifest
    {
        Id = "fixture_truck", Hero = true, LodTriangles = new[] { 42140, 23600, 12640, 6320 }, Renderers = 28, Materials = 15, MaxTextureSize = 0, TextureMemoryMb = 0f,
        LengthM = 8.2f, GroundY = 0f, SteeringWheelX = -0.45f, FrontLightZ = 3.9f,
        Nodes = new[] { "Susp_Axle_F", "Susp_Axle_R1", "Spin_FL", "Spin_FR", "Spin_RL", "Spin_RR", "Light_Head_L", "Light_Head_R", "Attach_Camera_Chase", "COL_Body" },
        WheelVisualRadius = new[] { 0.5f, 0.5f, 0.5f, 0.5f }, WheelColliderRadius = new[] { 0.5f, 0.5f, 0.51f, 0.5f }, CollisionHullTriangles = new[] { 120, 200 }
    };
    static bool Known(string id) => id == "fixture_truck";
    static string[] Rules(VehicleAssetManifest m) => VehicleAssetValidator.Validate(m, Known).Select(f => f.Rule).ToArray();

    [Fact] public void ATruckShapedLikeTheMeasuredFixtureWithAKnownLicencePasses()
    {
        var f = VehicleAssetValidator.Validate(Good(), Known);
        Assert.True(VehicleAssetValidator.Passes(f), string.Join("; ", f));
    }

    [Fact] public void WithoutAConfirmedLicenceItIsRefusedWhateverElseIsRight()
    {
        var m = Good(); m.Id = "bought_truck";
        Assert.Contains("licence", Rules(m)); Assert.False(VehicleAssetValidator.Passes(VehicleAssetValidator.Validate(m, Known)));
        Assert.Contains("licence", VehicleAssetValidator.Validate(Good(), null).Select(x => x.Rule));
    }

    [Fact] public void TheTriangleBudgetsAreEnforcedPerClassAndPerLod()
    {
        var m = Good(); m.LodTriangles = new[] { 60001, 20000, 10000 }; Assert.Contains("triangles", Rules(m));
        m = Good(); m.Hero = false; m.LodTriangles = new[] { 12001 }; m.Renderers = 6; Assert.Contains("triangles", Rules(m));
        m = Good(); m.LodTriangles = new[] { 40000, 25000, 10000, 5000 }; Assert.Contains("lods", Rules(m));         // LOD1 is 62.5%
        m = Good(); m.LodTriangles = new[] { 40000, 20000 }; Assert.Contains("lods", Rules(m));                        // a hero needs three LODs
        m = Good(); m.LodTriangles = new int[0]; Assert.Contains("lods", Rules(m));
    }

    [Fact] public void DrawCallMaterialAndTextureBudgetsAreEnforced()
    {
        var m = Good(); m.Renderers = 41; Assert.Contains("renderers", Rules(m));
        m = Good(); m.Hero = false; m.LodTriangles = new[] { 9000 }; m.Renderers = 9; Assert.Contains("renderers", Rules(m));
        m = Good(); m.Materials = 21; Assert.Contains("materials", Rules(m));
        m = Good(); m.UsesStandardShader = true; Assert.Contains("shader", Rules(m));
        m = Good(); m.MaxTextureSize = 4096; Assert.Contains("textures", Rules(m));
        m = Good(); m.TextureMemoryMb = 17f; Assert.Contains("textures", Rules(m));
    }

    [Fact] public void OrientationScaleAndPivotAreEnforced()
    {
        var m = Good(); m.GroundY = 0.2f; Assert.Contains("pivot", Rules(m));
        m = Good(); m.LengthM = 82f; Assert.Contains("scale", Rules(m));                                              // exported in centimetres
        m = Good(); m.LengthM = 0.8f; Assert.Contains("scale", Rules(m));
        m = Good(); m.FrontLightZ = -3.9f; Assert.Contains("orientation", Rules(m));                                  // faces -Z
        m = Good(); m.SteeringWheelX = 0.45f; Assert.Contains("orientation", Rules(m));                               // mirrored cab
    }

    [Fact] public void TheRigMustProvideEverythingTheVehicleSystemNeeds()
    {
        var m = Good(); m.Nodes = m.Nodes.Where(n => !n.StartsWith("Susp_")).ToArray(); Assert.Contains("rig", Rules(m));
        m = Good(); m.Nodes = m.Nodes.Where(n => n != "Spin_RR").ToArray(); Assert.Contains("rig", Rules(m));
        m = Good(); m.Nodes = m.Nodes.Where(n => !n.StartsWith("Light_Head_")).ToArray(); Assert.Contains("rig", Rules(m));
        m = Good(); m.Nodes = m.Nodes.Where(n => !n.StartsWith("Attach_Camera_")).ToArray(); Assert.Contains("rig", Rules(m));
        m = Good(); m.Nodes = m.Nodes.Where(n => !n.StartsWith("COL_")).ToArray(); Assert.Contains("collision", Rules(m));
    }

    [Fact] public void WheelCollidersMustMatchTheirVisualsAndCollisionHullsMustBeSmall()
    {
        var m = Good(); m.WheelColliderRadius = new[] { 0.5f, 0.5f, 0.56f, 0.5f }; Assert.Contains("rig", Rules(m));      // 12% off
        m = Good(); m.WheelColliderRadius = new[] { 0.5f, 0.5f, 0.5f }; Assert.Contains("rig", Rules(m));                // a collider is missing
        m = Good(); m.WheelVisualRadius = new[] { 0f, 0.5f, 0.5f, 0.5f }; Assert.Contains("rig", Rules(m));
        m = Good(); m.CenterOfMassInsideBounds = false; Assert.Contains("rig", Rules(m));
        m = Good(); m.CollisionHullTriangles = new[] { 120, 256 }; Assert.Contains("collision", Rules(m));
    }

    [Fact] public void AnEmptyManifestIsRefusedWithoutThrowing()
    {
        Assert.False(VehicleAssetValidator.Passes(VehicleAssetValidator.Validate(null, Known)));
        Assert.False(VehicleAssetValidator.Passes(VehicleAssetValidator.Validate(new VehicleAssetManifest(), Known)));
    }
}
