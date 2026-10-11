using System;
using System.IO;
using System.Linq;
using ARO.NetCore;
using Xunit;

public class SettingsModelTests
{
    static string Root()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d != null && !Directory.Exists(Path.Combine(d.FullName, "Assets", "_Project", "Scripts"))) d = d.Parent;
        return d?.FullName ?? throw new InvalidOperationException("repository root not found");
    }
    static string Scripts() => Path.Combine(Root(), "Assets", "_Project", "Scripts");

    // ---- catalogue
    [Fact] public void EveryIdIsUnique() => Assert.Equal(SettingsCatalog.All.Count, SettingsCatalog.All.Select(d => d.Id).Distinct().Count());
    [Fact] public void EveryRowHasALabelAndAPanel() => Assert.All(SettingsCatalog.All, d => { Assert.False(string.IsNullOrWhiteSpace(d.Label)); Assert.False(string.IsNullOrWhiteSpace(SettingsCatalog.PanelTitle(d.Panel))); });
    [Fact] public void EveryDefaultIsAcceptableToItsOwnRow() => Assert.All(SettingsCatalog.All.Where(d => d.Local), d => Assert.Equal(d.Default, GameSettings.Normalise(d, d.Default)));
    [Fact] public void ChoicesHaveALabelPerOption() => Assert.All(SettingsCatalog.All.Where(d => d.Kind == SettingKind.Choice || d.Kind == SettingKind.Toggle), d => { Assert.Equal(d.Options.Length, d.OptionLabels.Length); Assert.Contains(d.Default, d.Options); });
    [Fact] public void EverySoonRowSaysWhy() => Assert.All(SettingsCatalog.All.Where(d => d.Status == SettingStatus.Soon), d => Assert.False(string.IsNullOrWhiteSpace(d.Note)));
    [Fact] public void ARowThatTakesEffectLaterSaysWhen() => Assert.All(SettingsCatalog.All.Where(d => d.Status == SettingStatus.OnReload), d => Assert.Contains("loads", d.Note));
    [Fact] public void EveryPanelHasRows() => Assert.All(Enum.GetValues(typeof(SettingsPanel)).Cast<SettingsPanel>(), p => Assert.NotEmpty(SettingsCatalog.Of(p)));
    [Fact] public void TheGameTabShowsEveryPanelAndAccountShowsNone() { Assert.Equal(6, SettingsCatalog.PanelsOf(SettingsTab.Game).Length); Assert.Empty(SettingsCatalog.PanelsOf(SettingsTab.Account)); }
    [Fact] public void EveryTabHasALabel() => Assert.All(Enum.GetValues(typeof(SettingsTab)).Cast<SettingsTab>(), t => Assert.False(string.IsNullOrEmpty(SettingsCatalog.TabLabel(t))));
    [Fact] public void DynamicInfoRowsHaveNoFixedText() { Assert.Null(SettingsCatalog.Find("aa").FixedText); Assert.Null(SettingsCatalog.Find("view_distance").FixedText); Assert.NotNull(SettingsCatalog.Find("region").FixedText); }
    [Fact] public void TheNearbyRowLivesOnTheServer() { var d = SettingsCatalog.Find("appear_nearby"); Assert.True(d.Server); Assert.False(d.Local); Assert.False(new GameSettings().TrySet("appear_nearby", "0")); }

    // ---- every real setting is wired to something in the game (no dead switches)
    [Fact] public void EveryEditableSettingIsUsedByTheGameCode()
    {
        var files = Directory.GetFiles(Scripts(), "*.cs", SearchOption.AllDirectories).Where(f => !f.EndsWith("SettingsModel.cs") && !f.Contains(Path.DirectorySeparatorChar + "Editor" + Path.DirectorySeparatorChar)).ToArray();
        var text = string.Join("\n", files.Select(File.ReadAllText));
        foreach (var d in SettingsCatalog.All.Where(x => x.Local && x.Editable))
            Assert.True(text.Contains("\"" + d.Id + "\""), $"setting '{d.Id}' can be changed but nothing in the game reads it");
    }
    [Fact] public void EverySoonRowIsReallyNotReadByTheGame()
    {   // a row marked Soon that the game already reads should be promoted, otherwise the register lies
        var files = Directory.GetFiles(Scripts(), "*.cs", SearchOption.AllDirectories).Where(f => !f.EndsWith("SettingsModel.cs") && !f.Contains(Path.DirectorySeparatorChar + "Editor" + Path.DirectorySeparatorChar)).ToArray();
        var text = string.Join("\n", files.Select(File.ReadAllText));
        foreach (var d in SettingsCatalog.All.Where(x => x.Status == SettingStatus.Soon))
            Assert.False(text.Contains("Bool(\"" + d.Id + "\")") || text.Contains("Int(\"" + d.Id + "\")") || text.Contains("Get(\"" + d.Id + "\")"), $"'{d.Id}' is marked Soon but the game reads it");
    }

    // ---- values
    [Fact] public void DefaultsAreDefaults() { var s = new GameSettings(); Assert.True(s.IsDefault); Assert.Equal("kmh", s.Get("speed_unit")); Assert.Equal(100, s.Int("master_volume")); Assert.True(s.Bool("shadows")); Assert.False(s.Bool("show_fps")); }
    [Fact] public void ChoicesAreValidated() { var s = new GameSettings(); Assert.True(s.TrySet("quality", "high")); Assert.Equal("high", s.Get("quality")); Assert.False(s.TrySet("quality", "ultra")); Assert.Equal("high", s.Get("quality")); }
    [Fact] public void TogglesAcceptOnlyZeroOrOne() { var s = new GameSettings(); Assert.False(s.TrySet("shadows", "yes")); Assert.True(s.SetBool("shadows", false)); Assert.False(s.Bool("shadows")); }
    [Fact] public void SlidersAreClampedAndSnappedToTheirStep() { var s = new GameSettings(); s.SetInt("master_volume", 143); Assert.Equal(100, s.Int("master_volume")); s.SetInt("master_volume", -9); Assert.Equal(0, s.Int("master_volume")); s.SetInt("master_volume", 47); Assert.Equal(45, s.Int("master_volume")); s.SetInt("master_volume", 48); Assert.Equal(50, s.Int("master_volume")); }
    [Fact] public void ANonNumberIsRefusedBySliders() { var s = new GameSettings(); Assert.False(s.TrySet("master_volume", "loud")); Assert.Equal(100, s.Int("master_volume")); }
    [Fact] public void SoonAndFixedRowsCannotBeChanged() { var s = new GameSettings(); Assert.False(s.TrySet("game_mode", "arcade")); Assert.False(s.SetInt("voice_volume", 10)); Assert.False(s.TrySet("region", "x")); Assert.False(s.TrySet("nope", "1")); Assert.True(s.IsDefault); }
    [Fact] public void ResetGoesBackToDefaults() { var s = new GameSettings(); s.TrySet("quality", "low"); s.SetBool("show_fps", true); Assert.False(s.IsDefault); s.ResetToDefaults(); Assert.True(s.IsDefault); }
    [Fact] public void CloneIsIndependent() { var a = new GameSettings(); var b = a.Clone(); b.TrySet("quality", "low"); Assert.Equal("auto", a.Get("quality")); Assert.False(a.SameAs(b)); }
    [Fact] public void ChangedFromListsExactlyTheDifferences() { var a = new GameSettings(); var b = a.Clone(); b.TrySet("quality", "low"); b.SetInt("engine_volume", 40); Assert.Equal(new[] { "quality", "engine_volume" }.OrderBy(x => x), b.ChangedFrom(a).OrderBy(x => x)); Assert.Empty(a.ChangedFrom(a.Clone())); }

    // ---- stored text
    [Fact] public void StoredTextRoundTrips() { var a = new GameSettings(); a.TrySet("quality", "medium"); a.SetInt("camera_shake", 35); a.SetBool("post", false); a.TrySet("time_of_day", "night"); var b = GameSettings.Parse(a.Serialize()); Assert.True(a.SameAs(b)); Assert.Equal(35, b.Int("camera_shake")); }
    [Fact] public void DisplayModeIsNeverStored() { var a = new GameSettings(); a.TrySet("display_mode", "fullscreen"); Assert.DoesNotContain("display_mode", a.Serialize()); Assert.Equal("windowed", GameSettings.Parse("display_mode=fullscreen\n").Get("display_mode")); }
    [Fact] public void NothingButEditableRowsIsStored() { var t = new GameSettings().Serialize(); Assert.DoesNotContain("game_mode", t); Assert.DoesNotContain("region", t); Assert.DoesNotContain("appear_nearby", t); Assert.Contains("master_volume=100", t); }
    [Fact] public void ADamagedFileStillGivesValidSettings()
    {
        var s = GameSettings.Parse("quality=ultra\nmaster_volume=abc\n=\nnonsense\nunknown_key=1\nshadows=0\r\nsteering=75\ngame_mode=arcade\n");
        Assert.Equal("auto", s.Get("quality")); Assert.Equal(100, s.Int("master_volume")); Assert.False(s.Bool("shadows")); Assert.Equal(75, s.Int("steering")); Assert.Equal("simulation", s.Get("game_mode"));
    }
    [Fact] public void EmptyOrNullTextIsDefaults() { Assert.True(GameSettings.Parse(null).IsDefault); Assert.True(GameSettings.Parse("").IsDefault); Assert.True(GameSettings.Parse("\n\n").IsDefault); }
    [Fact] public void ANewerFileWithAnOptionThatNoLongerExistsFallsBack() => Assert.Equal("real", GameSettings.Parse("time_of_day=dusk\n").Get("time_of_day"));

    // ---- effects
    [Theory] [InlineData(0, 0.5f)] [InlineData(50, 1.0f)] [InlineData(100, 1.5f)] [InlineData(500, 1.5f)] [InlineData(-5, 0.5f)]
    public void SteeringGain(int p, float expected) => Assert.Equal(expected, SettingsEffect.SteeringGain(p), 3);
    [Theory] [InlineData("low", 0.5f)] [InlineData("medium", 1f)] [InlineData("high", 1.5f)] [InlineData("junk", 1f)] [InlineData(null, 1f)]
    public void TrafficScale(string v, float expected) => Assert.Equal(expected, SettingsEffect.TrafficScale(v), 3);
    [Theory] [InlineData("real", -1f)] [InlineData("morning", 8f)] [InlineData("noon", 12f)] [InlineData("evening", 17.5f)] [InlineData("night", 22f)]
    public void FixedHour(string v, float expected) => Assert.Equal(expected, SettingsEffect.FixedHour(v), 3);
    [Fact] public void EveryTimeChoiceMapsToAHourOrTheClock() => Assert.All(SettingsCatalog.Find("time_of_day").Options, o => Assert.True(SettingsEffect.FixedHour(o) == -1f || (SettingsEffect.FixedHour(o) >= 0f && SettingsEffect.FixedHour(o) < 24f)));
    [Fact] public void NightIsReallyNightAndMiddayIsNot() { Assert.True(SettingsEffect.FixedHour("night") > 18.4f); Assert.InRange(SettingsEffect.FixedHour("noon"), 5.6f, 18.4f); }
    [Fact] public void MilesPerHour() { Assert.Equal(62.1371f, SettingsEffect.SpeedFor(100f, true), 2); Assert.Equal(100f, SettingsEffect.SpeedFor(100f, false)); Assert.Equal("mph", SettingsEffect.SpeedUnit(true)); Assert.Equal("km/h", SettingsEffect.SpeedUnit(false)); }
    [Fact] public void PercentIsClamped() { Assert.Equal(0f, SettingsEffect.Percent(-3)); Assert.Equal(1f, SettingsEffect.Percent(250)); Assert.Equal(0.4f, SettingsEffect.Percent(40), 3); }

    [Fact] public void TrafficChoiceNeverChangesTheSharedTier()
    {
        var before = QualityTiers.Medium.TrafficDensity; var hi = SettingsEffect.WithTraffic(QualityTiers.Medium, "high"); var lo = SettingsEffect.WithTraffic(QualityTiers.Medium, "low");
        Assert.Equal(before, QualityTiers.Medium.TrafficDensity); Assert.True(hi.TrafficDensity > before && lo.TrafficDensity < before); Assert.True(hi.MaxTrafficAgents > lo.MaxTrafficAgents);
        Assert.Equal(QualityTiers.Medium.ShadowDistance, hi.ShadowDistance); Assert.Same(QualityTiers.Medium, SettingsEffect.WithTraffic(QualityTiers.Medium, "medium"));
    }
    [Fact] public void TierSummariesAreReadable() { Assert.Equal("MSAA 4x", SettingsEffect.AntiAliasing(QualityTiers.High)); Assert.Equal("Off", SettingsEffect.AntiAliasing(QualityTiers.Low)); Assert.Equal("2200 m", SettingsEffect.ViewDistance(QualityTiers.High)); Assert.Contains("High", SettingsEffect.TierSummary(QualityTiers.High)); }
    [Fact] public void CloneCopiesEveryField()
    {
        var c = QualityTiers.High.Clone(); Assert.NotSame(QualityTiers.High, c);
        foreach (var f in typeof(QualityTier).GetFields()) Assert.Equal(f.GetValue(QualityTiers.High), f.GetValue(c));
    }

    // ---- controls shown to the player match the code that reads them
    [Fact] public void TheKeyboardBindingsShownAreTheOnesTheInputReads()
    {
        var src = File.ReadAllText(Path.Combine(Scripts(), "Input", "KeyboardVehicleInput.cs"));
        foreach (var b in ControlBindings.All.Where(x => x.Device == "Keyboard")) foreach (var k in b.KeyProps) Assert.True(src.Contains("kb." + k), $"{b.Action}: {k} is not read by KeyboardVehicleInput");
        foreach (var b in ControlBindings.All.Where(x => x.Device == "Gamepad")) foreach (var k in b.KeyProps) Assert.True(src.Contains("pad." + k), $"{b.Action}: {k} is not read by KeyboardVehicleInput");
        Assert.Contains("scroll", File.ReadAllText(Path.Combine(Scripts(), "Vehicles", "FollowCamera.cs")));
    }
    [Fact] public void EveryKeyTheInputReadsIsListed()
    {
        var src = File.ReadAllText(Path.Combine(Scripts(), "Input", "KeyboardVehicleInput.cs"));
        foreach (System.Text.RegularExpressions.Match m in System.Text.RegularExpressions.Regex.Matches(src, @"kb\.(\w+Key)\.")) Assert.True(ControlBindings.All.Any(b => b.KeyProps.Contains(m.Groups[1].Value)), $"{m.Groups[1].Value} is read but not shown in Controls");
    }
    [Fact] public void BindingsHaveTextForThePlayer() => Assert.All(ControlBindings.All, b => { Assert.False(string.IsNullOrWhiteSpace(b.Action)); Assert.False(string.IsNullOrWhiteSpace(b.Keys)); });
}
