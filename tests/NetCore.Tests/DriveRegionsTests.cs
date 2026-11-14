using System;
using System.IO;
using System.Linq;
using ARO.NetCore;
using Xunit;

/// <summary>The player's choice of where to drive (owner decision 2026-11-14: Kano in the route selection). Jobs, bus routes and convoys stay on the shipped road until the server has Kano data.</summary>
public class DriveRegionsTests
{
    [Fact]
    public void TheShippedRoadIsTheDefaultAndKeepsEverything()
    {
        var s = DriveRegions.Get(null); Assert.Equal("ng-lagos-ibadan", s.RouteId); Assert.Same(s, DriveRegions.Get("")); Assert.Same(s, DriveRegions.Get("../../etc/passwd")); Assert.Same(s, DriveRegions.Get("ng-kano-e1-streets"));
        Assert.True(s.Jobs && s.Bus && s.Convoy);
        Assert.Null(DriveRegions.Blocked(s, "job")); Assert.Null(DriveRegions.Blocked(s, "bus")); Assert.Null(DriveRegions.Blocked(s, "convoy"));
    }

    [Fact]
    public void KanoIsFreeDriveOnTwoCityRoutesUntilTheServerHasKanoData()
    {
        var kano = DriveRegions.InCity("kano").ToList();
        Assert.Equal(new[] { "ng-kano-city-e1", "ng-kano-city-f1" }, kano.Select(r => r.RouteId).ToArray());
        foreach (var r in kano)
        {
            Assert.False(r.Jobs || r.Bus || r.Convoy, r.RouteId);
            Assert.Contains("Switch to Lagos – Ibadan", DriveRegions.Blocked(r, "job")); Assert.StartsWith("Bus routes in", DriveRegions.Blocked(r, "bus")); Assert.StartsWith("Convoys in", DriveRegions.Blocked(r, "convoy"));
            Assert.True(DriveRegions.Known(r.RouteId));
        }
        Assert.Single(DriveRegions.InCity("lagos")); Assert.Empty(DriveRegions.InCity("niamey"));
    }

    [Fact]
    public void EveryRegionsRouteShipsWithTheGameAndPointsAtACityOfTheWorldMap()
    {
        foreach (var r in DriveRegions.All)
        {
            Assert.True(File.Exists(Path.Combine(AppContext.BaseDirectory, r.RouteId + ".route.json")), r.RouteId);
            var spec = System.Text.Json.JsonSerializer.Deserialize<RouteSpec>(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, r.RouteId + ".route.json")), Fx.Opt);
            Assert.Empty(RouteSpecValidator.Validate(spec, Fx.LoadCatalog().props.Select(p => p.id).ToHashSet()));
            Assert.Contains(r.CityId, File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "world_map.json")));
            Assert.False(string.IsNullOrWhiteSpace(r.Name) || string.IsNullOrWhiteSpace(r.Note));
        }
        Assert.Equal(DriveRegions.All.Count, DriveRegions.All.Select(r => r.RouteId).Distinct().Count());
    }
}
