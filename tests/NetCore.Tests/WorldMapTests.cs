using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using ARO.NetCore;
using Xunit;

public class WorldMapTests
{
    static readonly JsonSerializerOptions Opt = new JsonSerializerOptions { IncludeFields = true };
    static string Path(string f) => System.IO.Path.Combine(AppContext.BaseDirectory, f);
    static WorldMapData Load() => JsonSerializer.Deserialize<WorldMapData>(File.ReadAllText(Path("world_map.json")), Opt);
    static WorldMapData Clone(WorldMapData d) => JsonSerializer.Deserialize<WorldMapData>(JsonSerializer.Serialize(d, Opt), Opt);
    static WorldMapModel Model() => new WorldMapModel(Load());

    [Fact] public void ShippedDataIsValid() { var errors = WorldMapValidator.Validate(Load()); Assert.True(errors.Count == 0, string.Join("\n", errors)); }

    [Fact] public void LagosAndIbadanMatchTheBackendSeed()
    {
        string seed = File.ReadAllText(Path("seed.sql")); var m = Model();
        foreach (var name in new[] { "Lagos", "Ibadan" })
        {
            var row = Regex.Match(seed, @"\('NG',\s*'" + name + @"',\s*(-?[0-9.]+),\s*(-?[0-9.]+)\)"); Assert.True(row.Success, name + " missing from seed.sql");
            var c = m.City(name.ToLowerInvariant());
            Assert.Equal(double.Parse(row.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture), c.lat, 4);
            Assert.Equal(double.Parse(row.Groups[2].Value, System.Globalization.CultureInfo.InvariantCulture), c.lon, 4);
        }
    }

    [Fact] public void EverySeededLocationBelongsToALinkedCity()
    {
        string seed = File.ReadAllText(Path("seed.sql")); var m = Model();
        var rows = Regex.Matches(seed, @"\('(Lagos|Ibadan)',\s*'([a-z0-9-]+)'"); Assert.True(rows.Count >= 8);
        foreach (Match r in rows) Assert.True(m.TouchesRoute(m.Route("lagos-ibadan"), r.Groups[2].Value), r.Groups[2].Value);
        Assert.False(m.TouchesRoute(m.Route("lagos-ibadan"), "abuja-central"));
        Assert.False(m.TouchesRoute(m.Route("lagos-ibadan"), null));
    }

    [Fact] public void GreatCircleDistancesAreRight()
    {
        Assert.InRange(Geo.HaversineKm(0, 0, 0, 1), 111.0, 111.4);                       // one degree of longitude at the equator
        var m = Model();
        Assert.InRange(m.StraightLineKm(m.Route("lagos-ibadan")), 112.0, 116.0);
        Assert.InRange(m.StraightLineKm(m.Route("lagos-abuja")), 530.0, 548.0);          // via Ibadan, nearly straight
        Assert.Equal(0.0, Geo.HaversineKm(6.5, 3.4, 6.5, 3.4), 9);
    }

    [Fact] public void OnlyTheLagosIbadanCorridorIsBuiltAndNothingIsLive()
    {
        var m = Model();
        Assert.Equal(new[] { "lagos-ibadan" }, m.Routes.Where(r => WorldMapModel.StatusOf(r) != RouteStatus.Planned).Select(r => r.id).ToArray());
        Assert.Equal(RouteStatus.Prototype, WorldMapModel.StatusOf(m.Route("lagos-ibadan")));
        Assert.Equal("ng-lagos-ibadan", m.Route("lagos-ibadan").gameRouteId);
        Assert.All(m.Routes.Where(r => r.id != "lagos-ibadan"), r => Assert.True(string.IsNullOrEmpty(r.gameRouteId)));
    }

    [Fact] public void PlannedRoutesAreNeverDrivableAtAnyLevel()
    {
        var m = Model();
        foreach (var r in m.Routes.Where(r => WorldMapModel.StatusOf(r) == RouteStatus.Planned))
            foreach (var level in new[] { 1, 5, 20, 999 }) Assert.False(WorldMapModel.IsDrivable(m.StateFor(r, level)), $"{r.id} @ {level}");
        Assert.True(WorldMapModel.IsDrivable(m.StateFor(m.Route("lagos-ibadan"), 1)));
    }

    [Fact] public void RouteAndCityStatesFollowTheLevel()
    {
        var m = Model();
        Assert.Equal(RouteState.PlannedLocked, m.StateFor(m.Route("lagos-abuja"), 1));
        Assert.Equal(RouteState.PlannedAvailable, m.StateFor(m.Route("lagos-abuja"), 5));
        Assert.Equal(CityState.Open, m.StateFor(m.City("lagos"), 1));
        Assert.Equal(CityState.Open, m.StateFor(m.City("ibadan"), 1));
        Assert.Equal(CityState.Locked, m.StateFor(m.City("kano"), 1));
        Assert.Equal(CityState.Planned, m.StateFor(m.City("kano"), 9));
        Assert.Equal(CityState.Locked, m.StateFor(m.City("bamako"), 3));
        Assert.Equal(CityState.Open, m.StateFor(m.City("lagos"), 50));                      // stays open: it has the prototype route
    }

    [Fact] public void PolylineIsTheCityAnchorsInOrder()
    {
        var m = Model(); var p = m.PolylinePx(m.Route("lagos-accra"));
        Assert.Equal(4, p.Length); Assert.Equal(m.City("lagos").mapX, p[0].x); Assert.Equal(m.City("accra").mapY, p[3].y);
        Assert.Equal(new[] { "lagos", "cotonou", "lome", "accra" }, m.Route("lagos-accra").via);
    }

    [Fact] public void ContentUvCoversTheArtworkInsideThePaddedTexture()
    {
        var (x, y, w, h) = Model().ContentUv();
        Assert.Equal(0f, x); Assert.Equal(1f, w, 5); Assert.Equal(3f / 944f, y, 5); Assert.Equal(941f / 944f, h, 5);
    }

    [Fact] public void ValidatorCatchesBadData()
    {
        var d = Clone(Load()); d.routes.First(r => r.id == "lagos-abuja").gameRouteId = "ng-lagos-abuja";
        Assert.Contains(WorldMapValidator.Validate(d), e => e.Contains("planned route must not claim a game route"));

        d = Clone(Load()); d.routes.First(r => r.id == "lagos-abuja").via = new[] { "lagos", "atlantis" };
        Assert.Contains(WorldMapValidator.Validate(d), e => e.Contains("unknown city 'atlantis'"));

        d = Clone(Load()); d.cities[1].id = d.cities[0].id;
        Assert.Contains(WorldMapValidator.Validate(d), e => e.Contains("duplicate city"));

        d = Clone(Load()); d.cities.First(c => c.id == "lagos").mapX = 99999;
        Assert.Contains(WorldMapValidator.Validate(d), e => e.Contains("outside the artwork"));

        d = Clone(Load()); d.cities.First(c => c.id == "lagos").lat = 65; // swapped / wrong coordinates
        Assert.Contains(WorldMapValidator.Validate(d), e => e.Contains("outside West Africa"));

        d = Clone(Load()); d.routes.First(r => r.id == "lagos-ibadan").roadKm = 50;
        Assert.Contains(WorldMapValidator.Validate(d), e => e.Contains("shorter than the straight line"));

        d = Clone(Load()); d.image.textureHeight = 941;
        Assert.Contains(WorldMapValidator.Validate(d), e => e.Contains("multiple of 4"));

        d = Clone(Load()); var lagos = d.cities.First(c => c.id == "lagos"); var accra = d.cities.First(c => c.id == "accra"); (lagos.mapX, accra.mapX) = (accra.mapX, lagos.mapX);
        Assert.Contains(WorldMapValidator.Validate(d), e => e.Contains("contradict their longitudes"));

        d = Clone(Load()); d.routes.First(r => r.id == "lagos-ibadan").status = "live"; d.routes.First(r => r.id == "lagos-ibadan").gameRouteId = "";
        Assert.Contains(WorldMapValidator.Validate(d), e => e.Contains("must name its game route"));
        Assert.NotEmpty(WorldMapValidator.Validate(null));
    }
}
