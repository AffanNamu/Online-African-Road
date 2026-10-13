using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using ARO.NetCore;
using Xunit;

public class CameraViewsTests
{
    [Fact] public void ThereAreEightViewsNumberedOneToEightInTheAgreedOrder()
    {
        Assert.Equal(8, CameraViews.Count);
        string[] order = { "Cockpit", "DriverCabin", "RearChase", "FarChase", "Left", "Right", "Overhead", "FrontBumper" };
        for (int d = 1; d <= 8; d++) { Assert.True(CameraViews.TryFromDigit(d, out var v)); Assert.Equal(order[d - 1], v.ToString()); Assert.Equal(d, CameraViews.Digit(v)); }
    }
    [Theory] [InlineData(0)] [InlineData(9)] [InlineData(-1)] [InlineData(100)]
    public void DigitsOutsideOneToEightAreRefused(int d) => Assert.False(CameraViews.TryFromDigit(d, out _));
}

public class CameraProfileTests
{
    static readonly string[] Categories = { "truck", "bus", "van", "", null, "hovercraft" };

    [Fact] public void EveryCategoryHasAllEightViewsAndNoProblems()
    {
        foreach (var cat in Categories)
        {
            var p = CameraProfiles.Build(cat);
            Assert.Empty(CameraProfiles.Validate(p));
            for (int i = 0; i < 8; i++) Assert.Equal((CameraView)i, p[(CameraView)i].View);
        }
    }
    [Fact] public void UnknownCategoriesGetTheTruckSet() => Assert.Equal("truck", CameraProfiles.Build("hovercraft").Category);
    [Fact] public void TheDriverCabinIsUnavailableUntilTheVehicleHasADriver()
    {
        var p = CameraProfiles.Build("truck");
        Assert.False(p.IsAvailable(CameraView.DriverCabin));
        Assert.Contains("driver", p[CameraView.DriverCabin].UnavailableReason, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(7, p.AvailableCount);
        var q = CameraProfiles.Build("truck", driverCabin: true);
        Assert.True(q.IsAvailable(CameraView.DriverCabin)); Assert.Equal(8, q.AvailableCount); Assert.Empty(CameraProfiles.Validate(q));
    }
    [Fact] public void ACustomReasonIsKept() => Assert.Equal("no steering wheel", CameraProfiles.Build("bus", cabinReason: "no steering wheel")[CameraView.DriverCabin].UnavailableReason);
    [Fact] public void RigidViewsAreFixedToTheVehicle()
    {
        var p = CameraProfiles.Build("truck");
        foreach (var v in new[] { CameraView.Cockpit, CameraView.DriverCabin, CameraView.FrontBumper })
        {
            var c = p[v]; Assert.True(c.Rigid); Assert.Equal(0f, c.PositionLag); Assert.Equal(0f, c.RotationLag); Assert.False(c.ChaseEffects); Assert.False(c.CollisionAware); Assert.False(c.Zoomable);
        }
    }
    [Fact] public void OnlyTheRearChaseUsesChaseEffects() =>
        Assert.Equal(new[] { CameraView.RearChase }, CameraProfiles.Build("truck").All.Where(c => c.ChaseEffects).Select(c => c.View).ToArray());
    [Fact] public void FollowingViewsAreObstructionAwareAndStayClearOfTheVehicle()
    {
        foreach (var cat in new[] { "truck", "bus", "van" })
            foreach (var c in CameraProfiles.Build(cat).All.Where(c => !c.Rigid))
            {
                Assert.True(c.CollisionAware, c.View.ToString()); Assert.True(c.MinPullIn >= 3f, c.View.ToString());
                Assert.True(c.Distance * c.MinZoom >= 5f, c.View + " too close at its nearest zoom");
            }
    }
    [Fact] public void TheChaseViewKeepsTheOriginalNumbers()
    {   // the previous FollowCamera: offset (0, 4.6, -13), distance 8..24 m, base FOV 55 + 8 at speed
        var c = CameraProfiles.Build("truck")[CameraView.RearChase];
        Assert.Equal(4.6f, c.Y, 3); Assert.Equal(-13f, c.Z, 3); Assert.Equal(55f, c.Fov); Assert.Equal(8f, c.FovSpeedBoost);
        Assert.Equal(8f, -c.Z * c.MinZoom, 3); Assert.Equal(24f, -c.Z * c.MaxZoom, 3);
    }
    [Fact] public void FarChaseIsFurtherAndWiderThanChase()
    {
        var p = CameraProfiles.Build("truck");
        Assert.True(-p[CameraView.FarChase].Z > -p[CameraView.RearChase].Z); Assert.True(p[CameraView.FarChase].Zoomable); Assert.True(p[CameraView.FarChase].Fov < p[CameraView.RearChase].Fov + 1);
    }
    [Fact] public void LeftAndRightMirrorEachOther()
    {
        var p = CameraProfiles.Build("bus");
        Assert.True(p[CameraView.Left].X < 0 && p[CameraView.Right].X > 0); Assert.Equal(-p[CameraView.Left].X, p[CameraView.Right].X, 4);
        Assert.Equal(p[CameraView.Left].Y, p[CameraView.Right].Y);
    }
    [Fact] public void OverheadIsHighAbove() => Assert.True(CameraProfiles.Build("truck")[CameraView.Overhead].Y >= 20f);
    [Fact] public void BusesAreFramedFurtherBackThanTrucks()
    {
        Assert.True(-CameraProfiles.Build("bus")[CameraView.RearChase].Z > -CameraProfiles.Build("truck")[CameraView.RearChase].Z);
        Assert.True(CameraProfiles.Build("bus")[CameraView.FrontBumper].Z > CameraProfiles.Build("truck")[CameraView.FrontBumper].Z);
    }
    [Fact] public void PerVehicleScalesChangeOnlyTheFollowingViews()
    {
        var a = CameraProfiles.Build("truck"); var b = CameraProfiles.Build("truck", 1.5f, 1.25f);
        Assert.Equal(a[CameraView.RearChase].Z * 1.5f, b[CameraView.RearChase].Z, 3);
        Assert.Equal(a[CameraView.RearChase].Y * 1.25f, b[CameraView.RearChase].Y, 3);
        Assert.Equal(a[CameraView.Cockpit].Z, b[CameraView.Cockpit].Z);
        Assert.Equal(a[CameraView.FrontBumper].Y, b[CameraView.FrontBumper].Y);
        Assert.Empty(CameraProfiles.Validate(b));
    }
    [Theory] [InlineData(0f)] [InlineData(-3f)] [InlineData(100f)] [InlineData(float.NaN)]
    public void AbsurdScalesAreClampedIntoAValidProfile(float s) => Assert.Empty(CameraProfiles.Validate(CameraProfiles.Build("truck", s, s)));
    [Fact] public void BuildingTwiceGivesIndependentObjects()
    {
        var a = CameraProfiles.Build("truck"); var b = CameraProfiles.Build("truck");
        a[CameraView.RearChase].Z = -1f; Assert.Equal(-13f, b[CameraView.RearChase].Z);
    }
    [Fact] public void ValidateCatchesBrokenConfigurations()
    {
        var p = CameraProfiles.Build("truck"); p[CameraView.RearChase].PositionLag = 0f; Assert.NotEmpty(CameraProfiles.Validate(p));
        p = CameraProfiles.Build("truck"); p[CameraView.Cockpit].PositionLag = 3f; Assert.NotEmpty(CameraProfiles.Validate(p));
        p = CameraProfiles.Build("truck"); p[CameraView.Left].Fov = 5f; Assert.NotEmpty(CameraProfiles.Validate(p));
        p = CameraProfiles.Build("truck"); p[CameraView.FarChase].MinPullIn = 0.5f; Assert.NotEmpty(CameraProfiles.Validate(p));
        p = CameraProfiles.Build("truck"); p[CameraView.Right].ChaseEffects = true; Assert.NotEmpty(CameraProfiles.Validate(p));
        p = CameraProfiles.Build("truck"); p[CameraView.Overhead].Name = "Left side"; Assert.NotEmpty(CameraProfiles.Validate(p));
        p = CameraProfiles.Build("truck"); p[CameraView.DriverCabin].UnavailableReason = ""; Assert.NotEmpty(CameraProfiles.Validate(p));
        p = CameraProfiles.Build("truck"); p[CameraView.Cockpit] = null; Assert.NotEmpty(CameraProfiles.Validate(p));
        Assert.NotEmpty(CameraProfiles.Validate(null));
    }
    [Theory] [InlineData("truck", 1f)] [InlineData("truck", 0.75f)] [InlineData("truck", 2f)] [InlineData("bus", 1f)] [InlineData("bus", 0.75f)] [InlineData("bus", 2f)] [InlineData("van", 1f)]
    public void TheClosestObstructionCanPullACameraIsOutsideThePlaceholderVehicle(string category, float scale)
    {
        // Placeholder bodies from TruckFactory (x half width, top y, rear z, front z), with a 0.2 m margin. Vans and trucks share the truck body.
        (float hw, float top, float rear, float front) box = category == "bus" ? (1.3f, 3.35f, -5.4f, 4.7f) : (1.25f, 3.15f, -3.4f, 3.3f);
        foreach (var c in CameraProfiles.Build(category, scale, scale).All.Where(c => !c.Rigid))
        {   // the camera at the nearest zoom, then at the nearest an obstacle may pull it (the floor)
            float zoom = c.MinZoom;
            var pivot = new[] { 0f, c.LookY, c.LookZ };
            var cam = new[] { c.X, c.Y * (0.8f + 0.2f * Math.Min(1f, zoom)), c.Z * zoom };
            var d = new[] { cam[0] - pivot[0], cam[1] - pivot[1], cam[2] - pivot[2] };
            float len = (float)Math.Sqrt(d[0] * d[0] + d[1] * d[1] + d[2] * d[2]);
            float t = Math.Min(c.MinPullIn, len);
            var p = new[] { pivot[0] + d[0] / len * t, pivot[1] + d[1] / len * t, pivot[2] + d[2] / len * t };
            bool inside = Math.Abs(p[0]) < box.hw + 0.2f && p[1] < box.top + 0.2f && p[2] > box.rear - 0.2f && p[2] < box.front + 0.2f;
            Assert.False(inside, $"{category} x{scale} {c.View}: the nearest camera position ({p[0]:0.0}, {p[1]:0.0}, {p[2]:0.0}) is inside the vehicle");
        }
    }
    [Fact] public void EveryViewHasANameAndAHint() => Assert.All(CameraProfiles.Build("truck").All, c => { Assert.False(string.IsNullOrWhiteSpace(c.Name)); Assert.False(string.IsNullOrWhiteSpace(c.Hint)); });
}

public class CameraSelectorTests
{
    static CameraSelector Fresh(bool cabin = false) { var s = new CameraSelector(); s.SetProfile(CameraProfiles.Build("truck", driverCabin: cabin)); return s; }

    [Fact] public void StartsInTheChaseView() => Assert.Equal(CameraView.RearChase, Fresh().Current);

    [Fact] public void NextWalksThroughAvailableViewsAndSkipsTheUnavailableOne()
    {
        var s = Fresh(); var seen = new System.Collections.Generic.List<CameraView> { s.Current };
        for (int i = 0; i < 7; i++) { Assert.Equal(CameraSelectResult.Changed, s.Next()); seen.Add(s.Current); }
        Assert.Equal(new[] { CameraView.RearChase, CameraView.FarChase, CameraView.Left, CameraView.Right, CameraView.Overhead, CameraView.FrontBumper, CameraView.Cockpit, CameraView.RearChase }, seen);
        Assert.DoesNotContain(CameraView.DriverCabin, seen);
    }
    [Fact] public void NextWrapsFromTheLastViewToTheFirst()
    {
        var s = Fresh(); Assert.Equal(CameraSelectResult.Changed, s.Select(CameraView.FrontBumper));
        Assert.Equal(CameraSelectResult.Changed, s.Next()); Assert.Equal(CameraView.Cockpit, s.Current);
    }
    [Fact] public void PreviousWrapsFromTheFirstViewToTheLast()
    {
        var s = Fresh(); s.Select(CameraView.Cockpit);
        Assert.Equal(CameraSelectResult.Changed, s.Previous()); Assert.Equal(CameraView.FrontBumper, s.Current);
        Assert.Equal(CameraSelectResult.Changed, s.Previous()); Assert.Equal(CameraView.Overhead, s.Current);
    }
    [Fact] public void PreviousSkipsTheUnavailableView()
    {
        var s = Fresh(); s.Select(CameraView.RearChase); s.Previous(); Assert.Equal(CameraView.Cockpit, s.Current);   // not DriverCabin
    }
    [Fact] public void WithADriverTheCabinIsPartOfTheCycle()
    {
        var s = Fresh(cabin: true); s.Select(CameraView.Cockpit); s.Next(); Assert.Equal(CameraView.DriverCabin, s.Current);
        s.Next(); Assert.Equal(CameraView.RearChase, s.Current);
    }
    [Fact] public void NextThenPreviousComesBack()
    {
        var s = Fresh(); var start = s.Current; for (int i = 0; i < 5; i++) s.Next(); for (int i = 0; i < 5; i++) s.Previous(); Assert.Equal(start, s.Current);
    }
    [Fact] public void EightNextPressesWithADriverReturnToTheStart()
    {
        var s = Fresh(cabin: true); var start = s.Current; for (int i = 0; i < 8; i++) s.Next(); Assert.Equal(start, s.Current);
    }
    [Theory] [InlineData(1, CameraView.Cockpit)] [InlineData(3, CameraView.RearChase)] [InlineData(4, CameraView.FarChase)] [InlineData(5, CameraView.Left)]
    [InlineData(6, CameraView.Right)] [InlineData(7, CameraView.Overhead)] [InlineData(8, CameraView.FrontBumper)]
    public void DirectSelectionByNumber(int digit, CameraView expected)
    {
        var s = Fresh(); s.Select(expected == CameraView.Overhead ? CameraView.Left : CameraView.Overhead);
        Assert.Equal(CameraSelectResult.Changed, s.SelectDigit(digit)); Assert.Equal(expected, s.Current);
    }
    [Fact] public void SelectingTheCurrentViewChangesNothing() { var s = Fresh(); Assert.Equal(CameraSelectResult.Unchanged, s.Select(CameraView.RearChase)); Assert.Equal(CameraSelectResult.Unchanged, s.SelectDigit(3)); }
    [Fact] public void SelectingTheUnavailableCabinIsExplicitAndKeepsTheCurrentView()
    {
        var s = Fresh(); s.Select(CameraView.Left);
        Assert.Equal(CameraSelectResult.Unavailable, s.SelectDigit(2)); Assert.Equal(CameraView.Left, s.Current);
        Assert.Contains("driver", s.Reason, StringComparison.OrdinalIgnoreCase);
        s.Next(); Assert.Equal("", s.Reason);   // the reason does not linger
    }
    [Theory] [InlineData(0)] [InlineData(9)] [InlineData(-4)]
    public void NumbersOutsideTheRangeAreInvalidAndChangeNothing(int d) { var s = Fresh(); Assert.Equal(CameraSelectResult.Invalid, s.SelectDigit(d)); Assert.Equal(CameraView.RearChase, s.Current); }
    [Fact] public void AnUndefinedViewIsInvalid() => Assert.Equal(CameraSelectResult.Invalid, Fresh().Select((CameraView)42));
    [Fact] public void WithoutAProfileNothingCanBeSelected()
    {
        var s = new CameraSelector(); Assert.Equal(CameraSelectResult.Invalid, s.Next()); Assert.Equal(CameraSelectResult.Invalid, s.Select(CameraView.Left)); Assert.Equal(CameraView.RearChase, s.Current);
    }
    [Fact] public void AZeroStepChangesNothing() => Assert.Equal(CameraSelectResult.Unchanged, Fresh().Step(0));
    [Fact] public void WhenOnlyOneViewIsAvailableNextDoesNotLoop()
    {
        var p = CameraProfiles.Build("truck");
        foreach (var c in p.All) if (c.View != CameraView.FrontBumper) { c.Available = false; c.UnavailableReason = "test"; }
        var s = new CameraSelector(); s.SetProfile(p);
        Assert.Equal(CameraView.FrontBumper, s.Current);   // the fallback found the one that exists
        Assert.Equal(CameraSelectResult.Unchanged, s.Next()); Assert.Equal(CameraSelectResult.Unchanged, s.Previous());
    }
    [Fact] public void ChangingVehicleKeepsTheViewWhenTheNewVehicleOffersIt()
    {
        var s = Fresh(); s.Select(CameraView.Overhead);
        Assert.Equal(CameraSelectResult.Unchanged, s.SetProfile(CameraProfiles.Build("bus"))); Assert.Equal(CameraView.Overhead, s.Current);
    }
    [Fact] public void ChangingToAVehicleWithoutTheViewFallsBackToTheChase()
    {
        var s = Fresh(cabin: true); s.Select(CameraView.DriverCabin);
        Assert.Equal(CameraSelectResult.Changed, s.SetProfile(CameraProfiles.Build("bus", driverCabin: false)));
        Assert.Equal(CameraView.RearChase, s.Current);
    }
    [Fact] public void ChangingVehicleToOneThatHasTheCabinKeepsTheCabin()
    {
        var s = Fresh(cabin: true); s.Select(CameraView.DriverCabin);
        Assert.Equal(CameraSelectResult.Unchanged, s.SetProfile(CameraProfiles.Build("bus", driverCabin: true))); Assert.Equal(CameraView.DriverCabin, s.Current);
    }
}

public class CameraMathTests
{
    // ---- obstruction pull-in
    [Fact] public void NothingInTheWayKeepsTheWantedDistance() { Assert.Equal(13f, CameraMath.PullIn(13f, -1f, 0.35f, 7f)); Assert.Equal(13f, CameraMath.PullIn(13f, float.NaN, 0.35f, 7f)); }
    [Fact] public void AWallFurtherAwayThanTheCameraChangesNothing() => Assert.Equal(13f, CameraMath.PullIn(13f, 40f, 0.35f, 7f));
    [Fact] public void AWallBehindTheCameraPullsItInToJustBeforeTheWall() => Assert.Equal(10f - 0.35f - 0.05f, CameraMath.PullIn(13f, 10f, 0.35f, 7f), 4);
    [Fact] public void AWallVeryCloseNeverPullsTheCameraIntoTheVehicle() { Assert.Equal(7f, CameraMath.PullIn(13f, 1f, 0.35f, 7f)); Assert.Equal(7f, CameraMath.PullIn(13f, 0f, 0.35f, 7f)); }
    [Fact] public void TheFloorNeverExceedsWhatWasAskedFor() => Assert.Equal(5f, CameraMath.PullIn(5f, 1f, 0.35f, 7f));
    [Fact] public void ZeroOrNegativeDesiredDistanceGivesZero() { Assert.Equal(0f, CameraMath.PullIn(0f, 5f, 0.3f, 3f)); Assert.Equal(0f, CameraMath.PullIn(-3f, 5f, 0.3f, 3f)); Assert.Equal(0f, CameraMath.PullIn(float.NaN, 5f, 0.3f, 3f)); }
    [Fact] public void ThePullInIsMonotonicInTheHitDistance()
    {
        float last = 0f;
        for (float hit = 0f; hit <= 30f; hit += 0.5f) { float v = CameraMath.PullIn(13f, hit, 0.35f, 7f); Assert.True(v >= last - 1e-4f); Assert.InRange(v, 7f, 13f); last = v; }
    }

    // ---- smoothing of the pull-in
    [Fact] public void AnObstacleIsReachedAlmostAtOnceButReleasedSlowly()
    {
        float r = 1f; for (int i = 0; i < 6; i++) r = CameraMath.SmoothPull(r, 0.5f, 1f / 60f);
        Assert.InRange(r, 0.5f, 0.55f);                                   // within a tenth of a second it has covered 90 % of the way in
        float released = 0.5f; for (int i = 0; i < 6; i++) released = CameraMath.SmoothPull(released, 1f, 1f / 60f);
        Assert.InRange(released, 0.5f, 0.7f);                             // out much slower: under half of the way in the same time
    }
    [Fact] public void SmoothPullNeverOvershootsAndStaysInRange()
    {
        float r = 1f; foreach (float dt in new[] { 0f, 0.016f, 0.5f, 10f, float.NaN, -1f }) { r = CameraMath.SmoothPull(r, 0.2f, dt); Assert.InRange(r, 0.2f, 1f); }
        Assert.InRange(CameraMath.SmoothPull(2f, 9f, 0.016f), 0.05f, 1f); Assert.InRange(CameraMath.SmoothPull(-4f, -2f, 0.016f), 0.05f, 1f);
    }

    // ---- damped follow and the frame time
    [Theory] [InlineData(0f, 0f)] [InlineData(-1f, 0f)] [InlineData(0.05f, 0.05f)] [InlineData(5f, 0.1f)] [InlineData(float.NaN, 0f)]
    public void FrameTimeIsClamped(float dt, float expected) => Assert.Equal(expected, CameraMath.StepTime(dt), 5);
    [Fact] public void DampingIsStableAtAnyFrameTime()
    {
        foreach (float dt in new[] { 0.001f, 0.016f, 0.033f, 0.1f, 1f, 100f })
        {
            float f = CameraMath.DampFactor(7f, dt); Assert.InRange(f, 0f, 1f);
            float v = CameraMath.Damp(0f, 10f, 7f, dt); Assert.InRange(v, 0f, 10f);   // never past the target
        }
        Assert.Equal(0f, CameraMath.DampFactor(7f, 0f)); Assert.Equal(0f, CameraMath.DampFactor(-5f, 0.016f));
    }
    [Fact] public void TheSameSecondGivesTheSameResultAtDifferentFrameRates()
    {
        float a = 0f; for (int i = 0; i < 60; i++) a = CameraMath.Damp(a, 10f, 4f, 1f / 60f);
        float b = 0f; for (int i = 0; i < 30; i++) b = CameraMath.Damp(b, 10f, 4f, 1f / 30f);
        Assert.Equal(a, b, 2);
    }

    // ---- transitions
    [Fact] public void EaseIsSmoothAndBounded()
    {
        Assert.Equal(0f, CameraMath.Ease(-1f)); Assert.Equal(0f, CameraMath.Ease(0f)); Assert.Equal(0.5f, CameraMath.Ease(0.5f), 4); Assert.Equal(1f, CameraMath.Ease(1f)); Assert.Equal(1f, CameraMath.Ease(7f));
        float last = 0f; for (float t = 0f; t <= 1f; t += 0.05f) { float e = CameraMath.Ease(t); Assert.True(e >= last - 1e-5f); last = e; }
    }
    [Fact] public void ATransitionTakesAboutHalfASecondAndEnds()
    {
        float t = 0f; int frames = 0; while (t < 1f && frames < 1000) { t = CameraMath.BlendProgress(t, 1f / 60f); frames++; }
        Assert.Equal(1f, t); Assert.InRange(frames / 60f, 0.4f, 0.5f);
    }
    [Fact] public void AStalledFrameDoesNotSkipTheTransition() => Assert.True(CameraMath.BlendProgress(0f, 5f) < 0.3f);

    // ---- zoom and field of view
    [Fact] public void TheMouseWheelZoomsTheChaseAndStopsAtTheLimits()
    {
        var c = CameraProfiles.Build("truck")[CameraView.RearChase]; float z = 1f;
        for (int i = 0; i < 100; i++) z = CameraMath.WheelZoom(z, 1f, c); Assert.Equal(c.MinZoom, z, 4);        // scrolling up = closer
        for (int i = 0; i < 100; i++) z = CameraMath.WheelZoom(z, -1f, c); Assert.Equal(c.MaxZoom, z, 4);
        Assert.Equal(z, CameraMath.WheelZoom(z, 0.005f, c));                                                       // a tiny wheel value is noise
    }
    [Fact] public void ViewsThatCannotZoomIgnoreTheWheel() => Assert.Equal(1f, CameraMath.WheelZoom(1f, 5f, CameraProfiles.Build("truck")[CameraView.Left]));
    [Fact] public void ZoomIsClampedIntoRange() { var c = CameraProfiles.Build("truck")[CameraView.FarChase]; Assert.Equal(c.MaxZoom, CameraMath.ClampZoom(99f, c)); Assert.Equal(c.MinZoom, CameraMath.ClampZoom(-3f, c)); Assert.Equal(1f, CameraMath.ClampZoom(float.NaN, c)); }
    [Fact] public void FieldOfViewWidensWithSpeedOnlyWhereConfigured()
    {
        var p = CameraProfiles.Build("truck");
        Assert.Equal(55f, CameraMath.Fov(p[CameraView.RearChase], 0f)); Assert.Equal(63f, CameraMath.Fov(p[CameraView.RearChase], 28f)); Assert.Equal(63f, CameraMath.Fov(p[CameraView.RearChase], 500f));
        Assert.Equal(p[CameraView.Cockpit].Fov, CameraMath.Fov(p[CameraView.Cockpit], 28f)); Assert.Equal(55f, CameraMath.Fov(p[CameraView.RearChase], float.NaN));
    }
}

/// <summary>The keys the camera reads must not collide with the driving keys, and the code, the controls list and the documentation must agree.</summary>
public class CameraControlsTests
{
    static string Scripts() { var d = AppContext.BaseDirectory; while (d != null && !Directory.Exists(Path.Combine(d, "Assets"))) d = Path.GetDirectoryName(d); return Path.Combine(d, "Assets", "_Project", "Scripts"); }
    static string Src(params string[] p) => File.ReadAllText(Path.Combine(new[] { Scripts() }.Concat(p).ToArray()));
    static string KeyProp(string keyName) => char.ToLowerInvariant(keyName[0]) + keyName.Substring(1) + "Key";

    [Fact] public void TheCameraKeysAreDeclaredInOnePlace()
    {
        var src = Src("Input", "CameraBindings.cs");
        foreach (var k in new[] { "C", "LeftShift", "RightShift", "Digit1", "Digit2", "Digit3", "Digit4", "Digit5", "Digit6", "Digit7", "Digit8" }) Assert.Contains("Key." + k, src);
    }
    [Fact] public void NoCameraKeyIsAlsoAFixedDrivingKey()
    {
        var camera = Regex.Matches(Src("Input", "CameraBindings.cs"), @"Key\.(\w+)").Select(m => KeyProp(m.Groups[1].Value)).ToHashSet();
        var driving = Regex.Matches(Src("Input", "KeyboardVehicleInput.cs"), @"kb\.(\w+Key)\.").Select(m => m.Groups[1].Value).ToHashSet();
        Assert.Empty(camera.Intersect(driving));
        Assert.Contains("wKey", driving);   // the pattern really finds the driving keys, so the empty intersection means something
    }
    [Fact] public void TheDrivingKeysTheCameraMustNotTakeAreStillRead()
    {
        var src = Src("Input", "KeyboardVehicleInput.cs");
        foreach (var k in new[] { "wKey", "aKey", "sKey", "dKey", "upArrowKey", "downArrowKey", "leftArrowKey", "rightArrowKey", "spaceKey", "hKey", "lKey", "qKey", "eKey" }) Assert.Contains("kb." + k, src);
    }
    [Fact] public void TheControlsListShowsTheCameraKeys()
    {
        string all = string.Join("|", ControlBindings.All.Select(b => b.Action + "=" + b.Keys));
        Assert.Contains("Next camera=C", all); Assert.Contains("Previous camera=Shift + C", all); Assert.Contains("Select camera=1 - 8", all);
        Assert.Contains("Change camera=Y (north)", all);
    }
    [Fact] public void TheHudHasAClickableCameraButtonNamedForTheTests() => Assert.Contains("Btn_Camera", Src("Game", "Hud.cs"));
    [Fact] public void TheCKeyIsNoLongerReadBehindTheInputSystemsBack() => Assert.DoesNotContain("cKey", Src("Game", "DrivingSession.cs"));
    [Fact] public void CameraRequestsStayLocal()
    {   // camera state must never reach the network code or the vehicle state sent to other players
        foreach (var f in new[] { Path.Combine("Multiplayer", "NetVehicleState.cs"), Path.Combine("Multiplayer", "NetworkVehicle.cs"), Path.Combine("NetCore", "VehicleNetCodec.cs") })
            Assert.DoesNotContain("CameraView", Src(f));
    }
}
