using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace ARO.NetCore
{
    // ---------------------------------------------------------------- data (JsonUtility- and System.Text.Json-compatible: public fields, arrays, no dictionaries)
    [Serializable] public class MapImageInfo { public string file; public int contentWidth, contentHeight, textureWidth, textureHeight; }
    [Serializable] public class MapCountry { public string code, name; }
    /// <summary>lat/lon are authoritative (Lagos and Ibadan equal the backend `cities` rows). mapX/mapY are pixel anchors on the illustrative artwork only.</summary>
    [Serializable] public class MapCity { public string id, name, country, locationSlugPrefix; public double lat, lon; public float mapX, mapY; }
    /// <summary>A route is the ordered list of city ids in `via` (great-circle legs). status: planned | prototype | live.</summary>
    [Serializable] public class MapRoute { public string id, name, status, gameRouteId, region, description; public string[] via; public int unlockLevel; public double roadKm; }
    [Serializable] public class WorldMapData { public int version; public string name, notes; public MapImageInfo image; public MapCountry[] countries; public MapCity[] cities; public MapRoute[] routes; }

    public enum RouteStatus { Planned, Prototype, Live }
    /// <summary>What a route means for THIS player: Live/Prototype = something can be driven; Planned* = not built (never playable).</summary>
    public enum RouteState { Live, Prototype, PlannedAvailable, PlannedLocked }
    public enum CityState { Open, Planned, Locked }

    public static class Geo
    {
        public const double EarthRadiusKm = 6371.0088;
        public static double HaversineKm(double lat1, double lon1, double lat2, double lon2)
        {
            double p1 = lat1 * Math.PI / 180, p2 = lat2 * Math.PI / 180, dp = p2 - p1, dl = (lon2 - lon1) * Math.PI / 180;
            double h = Math.Sin(dp / 2) * Math.Sin(dp / 2) + Math.Cos(p1) * Math.Cos(p2) * Math.Sin(dl / 2) * Math.Sin(dl / 2);
            return 2 * EarthRadiusKm * Math.Asin(Math.Min(1.0, Math.Sqrt(h)));
        }
    }

    /// <summary>Everything the world-map UI needs to know, derived from data. No Unity types, so it is unit-tested.</summary>
    public sealed class WorldMapModel
    {
        public WorldMapData Data { get; }
        readonly Dictionary<string, MapCity> _cities; readonly Dictionary<string, MapRoute> _routes;

        public WorldMapModel(WorldMapData data)
        {
            Data = data ?? throw new ArgumentNullException(nameof(data));
            _cities = data.cities.ToDictionary(c => c.id); _routes = data.routes.ToDictionary(r => r.id);
        }

        public IReadOnlyList<MapCity> Cities => Data.cities;
        public IReadOnlyList<MapRoute> Routes => Data.routes;
        public MapCity City(string id) => _cities.TryGetValue(id, out var c) ? c : null;
        public MapRoute Route(string id) => _routes.TryGetValue(id, out var r) ? r : null;
        public string CountryName(string code) => Data.countries.FirstOrDefault(c => c.code == code)?.name ?? code;

        public static RouteStatus StatusOf(MapRoute r) =>
            string.Equals(r.status, "live", StringComparison.OrdinalIgnoreCase) ? RouteStatus.Live
            : string.Equals(r.status, "prototype", StringComparison.OrdinalIgnoreCase) ? RouteStatus.Prototype : RouteStatus.Planned;

        public RouteState StateFor(MapRoute r, int playerLevel)
        {
            var s = StatusOf(r);
            if (s == RouteStatus.Live) return RouteState.Live;
            if (s == RouteStatus.Prototype) return RouteState.Prototype;
            return playerLevel >= r.unlockLevel ? RouteState.PlannedAvailable : RouteState.PlannedLocked;
        }

        /// <summary>A route can only be driven when something real (live or prototype) stands behind it; planned routes never are, whatever the player's level.</summary>
        public static bool IsDrivable(RouteState s) => s == RouteState.Live || s == RouteState.Prototype;

        public IEnumerable<MapRoute> RoutesAt(string cityId) => Data.routes.Where(r => r.via.Contains(cityId));

        public CityState StateFor(MapCity c, int playerLevel)
        {
            var touching = RoutesAt(c.id).ToList();
            if (touching.Any(r => IsDrivable(StateFor(r, playerLevel)))) return CityState.Open;
            if (touching.Count > 0 && touching.All(r => StateFor(r, playerLevel) == RouteState.PlannedLocked)) return CityState.Locked;
            return CityState.Planned;
        }

        /// <summary>Sum of great-circle legs between the route's cities (a lower bound for the road distance).</summary>
        public double StraightLineKm(MapRoute r)
        {
            double km = 0;
            for (int i = 1; i < r.via.Length; i++) { var a = _cities[r.via[i - 1]]; var b = _cities[r.via[i]]; km += Geo.HaversineKm(a.lat, a.lon, b.lat, b.lon); }
            return km;
        }

        /// <summary>The route drawn on the artwork: the anchors of its cities, in order (artwork pixels, origin top-left).</summary>
        public (float x, float y)[] PolylinePx(MapRoute r) => r.via.Select(id => (_cities[id].mapX, _cities[id].mapY)).ToArray();

        /// <summary>UV rect (x, y, w, h; origin bottom-left as in Unity) of the artwork inside its padded texture.</summary>
        public (float x, float y, float w, float h) ContentUv()
        {
            var i = Data.image; return (0f, (i.textureHeight - i.contentHeight) / (float)i.textureHeight, i.contentWidth / (float)i.textureWidth, i.contentHeight / (float)i.textureHeight);
        }

        /// <summary>Does a backend location slug (e.g. "lagos-apapa-port") belong to one of the route's cities?</summary>
        public bool TouchesRoute(MapRoute r, string locationSlug)
        {
            if (string.IsNullOrEmpty(locationSlug)) return false;
            foreach (var id in r.via) { var p = _cities[id].locationSlugPrefix; if (!string.IsNullOrEmpty(p) && locationSlug.StartsWith(p, StringComparison.Ordinal)) return true; }
            return false;
        }
    }

    public static class WorldMapValidator
    {
        static readonly Regex Id = new Regex("^[a-z0-9]+(-[a-z0-9]+)*$");
        // West Africa incl. the Cape Verde islands; a typo like a swapped lat/lon lands outside this box.
        const double MinLat = 3.0, MaxLat = 20.0, MinLon = -26.0, MaxLon = 16.0;

        /// <summary>Returns every problem found (empty = valid). Never throws on bad data.</summary>
        public static List<string> Validate(WorldMapData d)
        {
            var e = new List<string>();
            if (d == null) { e.Add("no data"); return e; }
            if (d.version != 1) e.Add($"unsupported version {d.version}");
            var im = d.image;
            if (im == null || string.IsNullOrEmpty(im.file)) e.Add("image info missing");
            else
            {
                if (im.contentWidth <= 0 || im.contentHeight <= 0) e.Add("image content size must be positive");
                if (im.textureWidth < im.contentWidth || im.textureHeight < im.contentHeight) e.Add("texture smaller than its content");
                if (im.textureWidth % 4 != 0 || im.textureHeight % 4 != 0) e.Add("texture size must be a multiple of 4 (GPU block compression)");
            }
            var countries = new HashSet<string>();
            foreach (var c in d.countries ?? new MapCountry[0]) if (!countries.Add(c.code)) e.Add($"duplicate country {c.code}");
            var cities = new Dictionary<string, MapCity>();
            foreach (var c in d.cities ?? new MapCity[0])
            {
                if (string.IsNullOrEmpty(c.id) || !Id.IsMatch(c.id)) { e.Add($"bad city id '{c.id}'"); continue; }
                if (cities.ContainsKey(c.id)) { e.Add($"duplicate city {c.id}"); continue; }
                cities[c.id] = c;
                if (!countries.Contains(c.country)) e.Add($"city {c.id}: unknown country '{c.country}'");
                if (c.lat < MinLat || c.lat > MaxLat || c.lon < MinLon || c.lon > MaxLon) e.Add($"city {c.id}: coordinates ({c.lat}, {c.lon}) outside West Africa");
                if (im != null && (c.mapX < 0 || c.mapY < 0 || c.mapX > im.contentWidth || c.mapY > im.contentHeight)) e.Add($"city {c.id}: map anchor outside the artwork");
            }
            // The artwork is stylised, but it must still keep real east-west and north-south order between cities that are clearly apart.
            var list = cities.Values.ToList();
            for (int i = 0; i < list.Count; i++)
                for (int j = i + 1; j < list.Count; j++)
                {
                    var a = list[i]; var b = list[j];
                    if (Math.Abs(a.lon - b.lon) > 1.0 && (a.lon < b.lon) != (a.mapX < b.mapX)) e.Add($"map anchors of {a.id} and {b.id} contradict their longitudes");
                    if (Math.Abs(a.lat - b.lat) > 1.0 && (a.lat > b.lat) != (a.mapY < b.mapY)) e.Add($"map anchors of {a.id} and {b.id} contradict their latitudes");
                }
            var ids = new HashSet<string>();
            foreach (var r in d.routes ?? new MapRoute[0])
            {
                if (string.IsNullOrEmpty(r.id) || !Id.IsMatch(r.id)) { e.Add($"bad route id '{r.id}'"); continue; }
                if (!ids.Add(r.id)) { e.Add($"duplicate route {r.id}"); continue; }
                if (r.via == null || r.via.Length < 2) { e.Add($"route {r.id}: needs at least two cities"); continue; }
                bool citiesOk = true;
                for (int i = 0; i < r.via.Length; i++)
                {
                    if (!cities.ContainsKey(r.via[i])) { e.Add($"route {r.id}: unknown city '{r.via[i]}'"); citiesOk = false; }
                    else if (i > 0 && r.via[i] == r.via[i - 1]) { e.Add($"route {r.id}: repeats {r.via[i]}"); citiesOk = false; }
                }
                var st = (r.status ?? "").ToLowerInvariant();
                if (st != "planned" && st != "prototype" && st != "live") e.Add($"route {r.id}: unknown status '{r.status}'");
                if (r.unlockLevel < 1) e.Add($"route {r.id}: unlockLevel must be >= 1");
                bool built = st == "prototype" || st == "live";
                if (built && string.IsNullOrEmpty(r.gameRouteId)) e.Add($"route {r.id}: a {st} route must name its game route");
                if (!built && !string.IsNullOrEmpty(r.gameRouteId)) e.Add($"route {r.id}: a planned route must not claim a game route (it would look playable)");
                if (built && r.roadKm <= 0) e.Add($"route {r.id}: a {st} route needs a known road length");
                if (citiesOk && r.roadKm > 0)
                {
                    double straight = 0; for (int i = 1; i < r.via.Length; i++) { var a = cities[r.via[i - 1]]; var b = cities[r.via[i]]; straight += Geo.HaversineKm(a.lat, a.lon, b.lat, b.lon); }
                    if (r.roadKm < straight * 0.98) e.Add($"route {r.id}: road length {r.roadKm} km is shorter than the straight line ({straight:0.#} km)");
                    if (r.roadKm > straight * 1.8) e.Add($"route {r.id}: road length {r.roadKm} km is implausibly long vs the straight line ({straight:0.#} km)");
                }
                if (built && citiesOk) foreach (var id in r.via) if (string.IsNullOrEmpty(cities[id].locationSlugPrefix)) e.Add($"route {r.id}: city {id} has no locationSlugPrefix, so jobs cannot be linked");
            }
            return e;
        }
    }
}
