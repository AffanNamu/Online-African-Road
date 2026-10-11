using System;
using System.Linq;
using ARO.NetCore;
using Xunit;

public class GarageModelTests
{
    static OrbitCameraModel Cam(float sx = 2.5f, float sy = 3.2f, float sz = 9f)
    {
        var l = ShowroomFraming.LimitsFor(sx, sy, sz); var c = new OrbitCameraModel(l, ShowroomFraming.BaseDistance(sx, sy, sz)); c.CenterY = sy / 2f; return c;
    }
    static void Settle(OrbitCameraModel c) { for (int i = 0; i < 600; i++) c.Step(0.016f); }

    // ---- framing
    [Fact] public void BiggerVehiclesAreFramedFurtherAway() => Assert.True(ShowroomFraming.BaseDistance(2.5f, 3.2f, 16f) > ShowroomFraming.BaseDistance(0.8f, 1.2f, 2f));
    [Fact] public void LimitsAreOrdered() { var l = ShowroomFraming.LimitsFor(2.5f, 3.2f, 9f); Assert.True(l.MinDistance < l.MaxDistance); Assert.True(l.MinPitch < l.MaxPitch); Assert.True(l.PanRadius > 0); }
    [Fact] public void TheDefaultDistanceIsInsideTheLimits() { var c = Cam(); Assert.InRange(c.Distance, c.Limits.MinDistance, c.Limits.MaxDistance); }
    [Fact] public void ADegenerateSizeStillGivesSaneLimits() { var l = ShowroomFraming.LimitsFor(0, 0, 0); Assert.True(l.MinDistance >= 1.2f && l.MaxDistance > l.MinDistance && !float.IsNaN(l.PanRadius)); }

    // ---- orbit
    [Fact] public void DraggingRightTurnsTheCameraAroundTheVehicle() { var c = Cam(); float y0 = c.TargetYaw; c.Orbit(100, 0); Assert.NotEqual(y0, c.TargetYaw); }
    [Fact] public void DraggingUpAndDownChangesThePitch() { var c = Cam(); float p0 = c.TargetPitch; c.Orbit(0, 50); Assert.True(c.TargetPitch > p0); }
    [Fact] public void PitchNeverGoesBelowTheFloorOrOverTheTop() { var c = Cam(); c.Orbit(0, -100000); Assert.Equal(c.Limits.MinPitch, c.TargetPitch); c.Orbit(0, 100000); Assert.Equal(c.Limits.MaxPitch, c.TargetPitch); }
    [Fact] public void YawStaysInHalfOpenRange() { var c = Cam(); for (int i = 0; i < 50; i++) c.Orbit(333, 0); Assert.InRange(c.TargetYaw, -180f, 180f); }
    [Fact] public void ManualOrbitStopsTheTurntable() { var c = Cam(); c.AutoRotate = true; c.Orbit(10, 0); Assert.False(c.AutoRotate); }

    // ---- zoom
    [Fact] public void WheelZoomIsLimitedAtBothEnds() { var c = Cam(); for (int i = 0; i < 200; i++) c.Zoom(5); Assert.Equal(c.Limits.MinDistance, c.TargetDistance, 3); for (int i = 0; i < 200; i++) c.Zoom(-5); Assert.Equal(c.Limits.MaxDistance, c.TargetDistance, 3); }
    [Fact] public void ZoomInGetsCloser() { var c = Cam(); float d = c.TargetDistance; c.Zoom(1); Assert.True(c.TargetDistance < d); }
    [Fact] public void ZoomIsMultiplicativeSoEveryNotchFeelsTheSame() { var a = Cam(); var b = Cam(); a.Zoom(1); a.Zoom(1); b.Zoom(2); Assert.Equal(b.TargetDistance, a.TargetDistance, 3); }

    // ---- pan
    [Fact] public void PanMovesTheLookAtPointButNotFarFromTheVehicle()
    {
        var c = Cam(); for (int i = 0; i < 400; i++) c.Pan(300, 300);
        float len = (float)Math.Sqrt(c.TargetPanX * c.TargetPanX + c.TargetPanZ * c.TargetPanZ);
        Assert.True(len <= c.Limits.PanRadius + 1e-3f); Assert.InRange(c.TargetPanY, -c.Limits.PanRadius, c.Limits.PanRadius);
    }

    // ---- presets
    [Theory] [InlineData(ViewPreset.Front, 0f)] [InlineData(ViewPreset.Rear, 180f)] [InlineData(ViewPreset.Left, -90f)] [InlineData(ViewPreset.Right, 90f)]
    public void SidePresetsLookFromTheRightSide(ViewPreset p, float yaw) { var c = Cam(); c.SetView(p); Assert.Equal(yaw, c.TargetYaw, 2); }
    [Fact] public void TopLooksDown() { var c = Cam(); c.SetView(ViewPreset.Top); Assert.Equal(c.Limits.MaxPitch, c.TargetPitch); }
    [Fact] public void FrontPresetPutsTheCameraInFrontOfAVehicleFacingPlusZ() { var c = Cam(); c.SetView(ViewPreset.Front); Settle(c); c.Pose(out var px, out _, out var pz, out var lx, out _, out var lz); Assert.True(pz > lz); Assert.Equal(lx, px, 2); }
    [Fact] public void RearPresetPutsTheCameraBehind() { var c = Cam(); c.SetView(ViewPreset.Rear); Settle(c); c.Pose(out _, out _, out var pz, out _, out _, out var lz); Assert.True(pz < lz); }
    [Fact] public void RightPresetIsOnPlusX() { var c = Cam(); c.SetView(ViewPreset.Right); Settle(c); c.Pose(out var px, out _, out _, out var lx, out _, out _); Assert.True(px > lx); }
    [Fact] public void WheelPresetMovesInAndTowardsTheWheel()
    {
        var c = Cam(); c.SetView(ViewPreset.Wheel, 1.1f, 0.5f, 2.8f); Assert.True(c.TargetDistance < c.HomeDistance); Settle(c);
        c.Pose(out _, out _, out _, out var lx, out _, out var lz); Assert.True(lx > c.CenterX); Assert.True(lz > c.CenterZ);
    }
    [Fact] public void ThePresetsKeepTheDistanceInsideTheLimits() { var c = Cam(); foreach (ViewPreset p in Enum.GetValues(typeof(ViewPreset))) { c.SetView(p, 5, 0, 5); Assert.InRange(c.TargetDistance, c.Limits.MinDistance, c.Limits.MaxDistance); } }
    [Fact] public void APresetStopsTheTurntable() { var c = Cam(); c.AutoRotate = true; c.SetView(ViewPreset.Left); Assert.False(c.AutoRotate); }

    // ---- reset
    [Fact] public void ResetGoesBackToTheDefaultView()
    {
        var c = Cam(); float y = c.TargetYaw, p = c.TargetPitch, d = c.TargetDistance; c.Orbit(400, 90); c.Zoom(3); c.Pan(50, 50);
        c.Reset(); Settle(c); Assert.Equal(y, c.Yaw, 1); Assert.Equal(p, c.Pitch, 1); Assert.Equal(d, c.Distance, 2); Assert.Equal(0f, c.PanX, 2);
    }
    [Fact] public void ResetGlidesUnlessAskedToSnap() { var c = Cam(); c.Orbit(400, 0); Settle(c); c.Reset(); Assert.NotEqual(c.TargetYaw, c.Yaw); c.Reset(true); Assert.Equal(c.TargetYaw, c.Yaw); }

    // ---- smooth interpolation
    [Fact] public void TheCurrentStateConvergesOnTheTarget() { var c = Cam(); c.Orbit(500, 30); c.Zoom(2); Settle(c); Assert.True(c.Settled); Assert.Equal(c.TargetYaw, c.Yaw, 1); Assert.Equal(c.TargetDistance, c.Distance, 2); }
    [Fact] public void MovementIsSmoothNotATeleport() { var c = Cam(); c.SetView(ViewPreset.Rear); c.Step(0.016f); float moved = Math.Abs(OrbitCameraModel.WrapDeg(c.Yaw - c.HomeYaw)); Assert.InRange(moved, 0.1f, 60f); }
    [Fact] public void YawTakesTheShortWayRound() { var c = Cam(); c.TargetYaw = 170; c.Yaw = 170; c.TargetYaw = -170; c.Step(0.05f); Assert.True(Math.Abs(OrbitCameraModel.WrapDeg(c.Yaw - 170f)) < 25f, "went the long way: " + c.Yaw); }
    [Fact] public void NoOvershootWithAHugeFrameTime() { var c = Cam(); c.Orbit(100, 10); c.Step(100f); Assert.True(c.Yaw >= Math.Min(0, c.TargetYaw) - 360 && !float.IsNaN(c.Yaw)); Assert.InRange(c.Distance, c.Limits.MinDistance, c.Limits.MaxDistance); }
    [Fact] public void ZeroOrNegativeTimeChangesNothing() { var c = Cam(); c.Orbit(100, 10); float y = c.Yaw; c.Step(0f); c.Step(-1f); Assert.Equal(y, c.Yaw); }
    [Fact] public void TheTurntableTurnsAtItsSpeed() { var c = Cam(); c.AutoRotate = true; c.AutoRotateDegPerSec = 36f; float y = c.TargetYaw; for (int i = 0; i < 10; i++) c.Step(0.1f); Assert.Equal(36f, OrbitCameraModel.WrapDeg(c.TargetYaw - y), 1); }
    [Fact] public void TheCameraNeverGoesUnderTheFloor() { var c = Cam(); c.Orbit(0, -9999); Settle(c); c.Pose(out _, out var py, out _, out _, out var ly, out _); Assert.True(py >= ly - c.Distance * 0.08f); }

    // ---- vehicle tags
    [Theory] [InlineData("ready", true, 98.0, VehicleTag.Active)] [InlineData("ready", false, 98.0, VehicleTag.Ready)] [InlineData("needs_repair", true, 20.0, VehicleTag.Damaged)] [InlineData("destroyed", false, 0.0, VehicleTag.Damaged)]
    [InlineData("low_fuel", true, 90.0, VehicleTag.LowFuel)] [InlineData("on_job", true, 90.0, VehicleTag.OnJob)] [InlineData("on_route", false, 90.0, VehicleTag.OnJob)] [InlineData("ready", false, 55.0, VehicleTag.Maintenance)]
    public void Tags(string status, bool selected, double cond, VehicleTag want) => Assert.Equal(want, GarageRules.TagFor(status, selected, cond));
    [Fact] public void DamageBeatsActive() => Assert.Equal(VehicleTag.Damaged, GarageRules.TagFor("needs_repair", true, 95));
    [Fact] public void EveryTagHasWords() { foreach (VehicleTag t in Enum.GetValues(typeof(VehicleTag))) Assert.False(string.IsNullOrEmpty(GarageRules.TagText(t))); }

    // ---- customisation slots are honest
    [Fact] public void OnlyPaintIsAvailableAndOnlyWhenTheModelSupportsIt()
    {
        var on = GarageRules.CustomSlots(true); Assert.Equal(new[] { "paint" }, on.Where(s => s.Available).Select(s => s.Key)); Assert.Equal(8, on.Count);
        Assert.Empty(GarageRules.CustomSlots(false).Where(s => s.Available));
    }
    [Fact] public void EveryUnavailableSlotSaysWhy() => Assert.All(GarageRules.CustomSlots(true).Where(s => !s.Available), s => Assert.False(string.IsNullOrEmpty(s.Reason)));
    [Fact] public void UpgradesAreListedButNeverPriced() { Assert.Equal(8, GarageRules.UpgradeCategories.Length); Assert.DoesNotContain("₦", GarageRules.UpgradeReason); }

    // ---- skins and colours
    [Theory] [InlineData("equipped", "Equipped")] [InlineData("owned", "Equip")] [InlineData("available", "Buy ₦15,000")] [InlineData("level_required", "Level 4")]
    public void SkinButtons(string state, string text) => Assert.Equal(text, GarageRules.SkinAction(state, 15000, 4, 0));
    [Fact] public void SkinNeedsMoney() => Assert.Equal("Need ₦2,500 more", GarageRules.SkinAction("insufficient_funds", 15000, 1, 2500));
    [Fact] public void HexColoursParse() { Assert.True(GarageRules.TryParseHex("#ff8000", out var r, out var g, out var b)); Assert.Equal(1f, r, 3); Assert.Equal(128 / 255f, g, 3); Assert.Equal(0f, b, 3); }
    [Theory] [InlineData("")] [InlineData(null)] [InlineData("ff8000")] [InlineData("#ff80")] [InlineData("#gg0000")] [InlineData("#ff8000ff")] [InlineData("red")]
    public void BadColoursAreRefused(string hex) => Assert.False(GarageRules.TryParseHex(hex, out _, out _, out _));

    // ---- maintenance wording
    [Fact] public void MaintenanceLabelsCarryTheServersNumbers() { Assert.Equal("Repair ₦2,000", GarageRules.RepairLabel(true, 2000)); Assert.Equal("Nothing to repair", GarageRules.RepairLabel(false, 0)); Assert.Equal("Refuel 40 L ₦80", GarageRules.RefuelLabel(40, 80)); Assert.Equal("Tank full", GarageRules.RefuelLabel(0, 0)); }
}
