using System;

namespace ARO.NetCore
{
    // Route data model. JsonUtility- and System.Text.Json-compatible (public fields, arrays, no dictionaries, enums as strings).
    // A route is DATA: nothing about Lagos -> Ibadan is hard-coded in the generator. Units: metres, degrees. World axes: x east, z north, y up.

    [Serializable]
    public class RouteMeta
    {
        public string country, from, to;
        public string status;               // planned | prototype | live (same vocabulary as the world map)
        public string worldMapRouteId;      // the id of the matching route in world_map.json (the map consumes this, it never duplicates it)
        public float realRoadKm;            // the real-world road length. The in-game corridor may be shorter: see gameCoverage
        public float gameLengthKm;          // length of the playable corridor as generated (checked against the spline by tests)
        public string gameCoverage;         // honest description of what part of the real road the playable corridor represents
        public string elevationSource;      // where the elevations come from ("design" until SRTM/Copernicus data is imported)
        public string notes;
    }

    /// <summary>One control point. The spline passes exactly through it. Zone/surface/profile apply to the road that STARTS here.</summary>
    [Serializable]
    public class ControlPoint
    {
        public string id, name;
        public float x, z, elevation;
        public string zone;                 // urban | commercial | industrial | rural | highway
        public string surface;              // good | worn | damaged
        public string profile;              // id into RouteSpec.profiles
    }

    /// <summary>A road cross-section. Lateral layout (centre outwards): [median] - inner shoulder - lanes - outer shoulder - verge. All widths in metres.</summary>
    [Serializable]
    public class RoadProfileSpec
    {
        public string id;
        public int lanesPerDirection = 1;
        public float laneWidth = 3.5f;
        public float medianWidth = 0f;          // > 0 = raised concrete median carrying a jersey barrier; 0 = painted centre line
        public string centreLine = "dashed";    // dashed | double_solid | none (ignored with a median)
        public string centreLineColor = "yellow";
        public float shoulderWidth = 1.5f;      // paved outer shoulder, each side
        public float innerShoulderWidth = 0.6f; // paved strip beside a median
        public float vergeWidth = 1.0f;         // unpaved strip beyond the shoulder
        public float camberPercent = 2f;        // crossfall (crown at the centre line, or away from the median)
        public string edgeBarrier = "none";     // none | guardrail (outer edges)
        public float ditchWidth = 2.0f, ditchDepth = 0.4f;
        public float laneLineDash = 3f, laneLineGap = 9f;

        // ---- E1 cross-section elements (docs/KANO_TRANSPORT_EXPANSION_PLAN.md section 5.2). Every field is optional and its default means "not there": a profile that sets none of them builds
        // exactly the geometry it built before E1 (the golden master of every existing route proves it).
        public string roadClass = "";           // "" | expressway | arterial | collector | local | lane | service: the road-class catalogue (RoadClasses) as data; informational for the generator, used by tools and the validator
        public float kerbHeight = 0f;           // > 0: a mountable kerb (sloped face) at the outer paved edge, 0.06..0.20 m high. 0 = none
        public string kerbPaint = "none";       // none | black_white (the painted kerb of Nigerian urban roads)
        public float sidewalkWidth = 0f;        // paved walkway beyond the kerb at kerb-top height, 0..6 m. Needs kerbHeight > 0
        public float drainWidth = 0f;           // a concrete gutter channel between the paved shoulder and the kerb, 0..1.2 m. Needs kerbHeight > 0
        public string medianStyle = "jersey";   // jersey (the raised concrete median with a barrier, as before) | planter (soil and planting inside a kerb rim, no barrier) | painted (a low paved island with a painted kerb rim, no barrier)
    }

    /// <summary>
    /// A pavement symbol painted on one corridor: arrows, zebra crossings, stop lines, yield triangles, parking and loading bays (RB-12). The symbol is anchored at station s of its corridor.
    /// Right-hand carriageway lanes carry traffic towards increasing s, left-hand lanes towards decreasing s; arrows and yield triangles point the way the lane flows.
    /// </summary>
    [Serializable]
    public class MarkingSymbolSpec
    {
        public string id;
        public string kind;                     // zebra | stop_line | yield | arrow_straight | arrow_left | arrow_right | bay
        public string corridor = "";            // "" = the main road; otherwise the id of a junction (side road)
        public float s;                         // station along that corridor (the centre of a zebra crossing, the START of an arrow, the stop line or the first bay line)
        public string side = "right";           // right | left | both (both: zebra and stop line only)
        public int lane = 0;                    // 1 = the lane nearest the median or centre line; 0 = every lane of that side (zebra, stop_line, yield)
        public float length = 0f;               // along the road: zebra depth (default 3 m), arrow length (default 4 m), bay spacing (default 2.5 m); 0 = the default
        public int count = 1;                   // bay: number of bays
        public bool yellow;                     // paint colour (white unless true)
    }

    /// <summary>A bridge between two route distances. The terrain dips into a valley under it, the deck gets parapets and a slab underside.</summary>
    [Serializable]
    public class BridgeSpec
    {
        public string id, name;
        public float startS, endS;
        public float valleyDepth = 9f;
        public float deckThickness = 1.2f;
        public float pierSpacing = 18f;
        public PierGap[] pierGaps = new PierGap[0];     // E5: no piers between these route distances (the clear span over a lower road)
        public bool overRoad;                           // E5: a grade-separated deck (a flyover): no valley is dug, the ground under it is whatever the roads and terrain below make it (valleyDepth must be 0)
    }

    /// <summary>E5: a stretch of a deck where no pier stands (the span over a road underneath).</summary>
    [Serializable] public class PierGap { public float from, to; }

    /// <summary>
    /// E5 (schemaVersion 3): an additional road corridor beyond the main road and the junction stubs: a flyover road, a ramp or a cross road. It has its own control points (x, z, elevation),
    /// so it can climb, cross the main road on a deck and come down; `decks` are the stretches carried on a deck with piers (no valley is dug under them). Rules: the main spline's (40 m radius, 8 percent grade).
    /// </summary>
    [Serializable]
    public class RoadSpec
    {
        public string id, name;
        public string role = "road";            // road | ramp | flyover: a ramp or flyover is held to the 6 percent grade of E5 and must meet the road it leaves or joins
        public ControlPoint[] controlPoints;
        public BridgeSpec[] decks = new BridgeSpec[0];
        public RoadJoinSpec[] joins = new RoadJoinSpec[0];     // where this road's ends meet the paved edge of another road (the gap in that road's kerb and edge line)
    }

    /// <summary>
    /// E5: one end of a road meets the paved edge of another corridor. `end` = start | end (which end of this road), `corridor` and `s` where on the other road, `side` = +1 right / -1 left of that road,
    /// `gap` = half length in metres of the kerb and edge-line gap on that road (a slip road joins at a shallow angle, so the gap is long: 20 to 40 m).
    /// </summary>
    [Serializable] public class RoadJoinSpec { public string end = "start", corridor; public float s, gap = 20f; public int side = 1; }

    /// <summary>
    /// E5: a leg of a roundabout: a straight road that leaves the ring at a bearing and ends `length` metres away. angleDeg is a WORLD angle, counter-clockwise from +x (east): 0 = east, 90 = north, 180 = west, 270 = south.
    /// It is a two-way road of `profile` (a corridor with the id `id`, like a junction stub); `rise` changes its elevation over its length.
    /// </summary>
    [Serializable] public class RingLegSpec { public string id, profile; public float angleDeg, length = 150f, rise; public string zone = "", surface = ""; }

    /// <summary>
    /// E5 (schemaVersion 3): a roundabout. A closed circular corridor (counter-clockwise seen from above: right-hand traffic) with the lanes of `profile` (all lanes circulate the same way; the centre line is a
    /// lane line, normally white), a central island, and legs. It is placed by its centre (x, z, elevation) or attached to the END of an existing corridor (`attachTo`): then the ring is put so that its outer paved
    /// edge meets that end and the corridor becomes one of the legs. `radius` is the island radius to the foot of the island kerb; the ring's centre line is then radius + the profile's formation half width.
    /// </summary>
    [Serializable]
    public class RingSpec
    {
        public string id, name, design;
        public string profile;                  // cross-section of the circulating carriageway (lanesPerDirection x 2 lanes, all circulating)
        public float x, z, elevation;           // the centre of the island and the elevation of the ring's centre line (ignored with attachTo)
        public string attachTo = "";            // id of a corridor (the main road, a junction stub, a RoadSpec) whose end or start meets the ring
        public string attachEnd = "end";        // end | start: which end of that corridor
        public float radius = 16f;              // island radius, metres
        public float apron = 0f;                // width of the paved (mountable) apron ring at the island's edge, metres
        public string island = "planter";       // planter | paved | grass | earth: what the island is covered with
        public string zone = "urban", surface = "good";
        public float flare = 0f;                // flared entries (the E3 fillet) at every leg; unkerbed rings only
        public float splitterLength = 14f, splitterWidth = 3f;     // raised splitter island on every leg (length 0 = none)
        public bool yieldLines = true, zebra = true;
        public RingLegSpec[] legs = new RingLegSpec[0];
        public string[] connect = new string[0];   // ids of other corridors (RoadSpecs, stubs) whose end meets the ring's outer edge (they count as legs for mouths, splitters and markings)
        public SitePropSpec[] props = new SitePropSpec[0];   // island props: x, z are metres east and north of the island centre
    }

    /// <summary>A side road leaving the main road. angleDeg > 0 turns right of the travel direction, < 0 left.</summary>
    [Serializable]
    public class JunctionSpec
    {
        public string id, name, profile, kind = "t_junction";      // kind: t_junction | four_way (schemaVersion 2: a second stub leaves on the opposite side at the same s, its id is id + "_left")
        public float s, angleDeg = 90f, length = 150f;
        public float rise = 0f;     // side-road elevation change over its length (metres)
        // ---- E3 junction forms (schemaVersion 2 only; every field defaults to "not there")
        public string profileLeft = "";     // four_way: profile of the opposite stub ("" = the same as profile)
        public float lengthLeft = 0f;       // four_way: length of the opposite stub (0 = the same as length)
        public float fillet = 0f;           // metres: a flared bellmouth at each corner of the mouth (unkerbed roads, |angleDeg| within 10 degrees of 90)
        public bool medianOpening;          // on a divided road with a planter or painted median: the median is cut across the junction (a jersey median cannot be opened)
    }

    /// <summary>Procedural roadside scatter. Offsets are measured from the edge of the road formation, never from its centre.</summary>
    [Serializable]
    public class PropRule
    {
        public string id, prop;
        public string zones;                    // comma separated (urban,commercial...), empty = every zone
        public string profiles;                 // comma separated road profile ids (urban_4lane,...), empty = every profile. Lets one zone carry different layouts (a village lane and an expressway in the same zone)
        public string side = "both";            // both | left | right | median (on the centre line, only where the road profile has a median; turned along the road)
        public float sFrom = 0f, sTo = 0f;      // sTo <= sFrom means "to the end of the route"
        public float offsetMin = 2f, offsetMax = 12f;
        public float spacingMin = 20f, spacingMax = 40f;
        public float presence = 1f;             // probability that a slot is used, 0..1
        public float scaleMin = 1f, scaleMax = 1f;
        public float yawJitterDeg = 10f;
        public bool faceRoad = true;
        public bool fixedDensity;               // true = the quality tier's prop density does not thin this rule (street lights keep their regular spacing on every tier); a density of 0 still removes everything
        public float exclusionMargin = -1f;     // metres of clearance kept beyond the road formation; -1 = the engine default (PropScatter.ExclusionMargin, 3 m). Street lights stand close to the road and ask for less
    }

    /// <summary>A hand-placed set piece (sign, fuel station, bus stop). lateral is from the road centre line, positive = right.</summary>
    [Serializable]
    public class PropPlacementSpec
    {
        public string id, prop, text;
        public string corridor = "";            // "" = placed from the main road (as always); otherwise a junction id: s and lateral are then measured along that side road
        public float s, lateral, yawDeg, scale = 1f;
    }

    [Serializable]
    public class TrafficSpec
    {
        public float densityScale = 1f;                 // multiplies the quality-tier traffic density on this route
        public float speedLimitScale = 1f;
        public string[] vehicleMix = new string[0];     // catalog vehicle ids / categories, informational until production traffic models exist
    }

    /// <summary>Navigation data: named places along the route (distance from the start). Not yet shown in the HUD.</summary>
    [Serializable]
    public class WaypointSpec { public string id, name, kind; public float s; }

    /// <summary>Terrain shape. Hills are low-frequency noise on top of a regional trend plane; roads then conform to the terrain (cut and fill).</summary>
    [Serializable]
    public class TerrainSpec
    {
        public float baseElevation = 0f;
        public float trendX = 0f, trendZ = 0f;                  // metres of rise per metre east / north
        public float hillAmplitude = 6f, hillWavelength = 900f;
        public float detailAmplitude = 0.8f, detailWavelength = 60f;
        public float maxEmbankmentSlope = 0.5f;                 // rise/run of cut and fill slopes (0.5 = 1:2)
        public float minBlendDistance = 6f;
        public string materialByZone = "";                      // reserved
    }

    [Serializable]
    public class RouteSpec
    {
        public int schemaVersion = 1;
        public string routeId, name;
        public int seed;
        public RouteMeta meta;
        public TerrainSpec terrain;
        public RoadProfileSpec[] profiles;
        public ControlPoint[] controlPoints;
        public BridgeSpec[] bridges;
        public JunctionSpec[] junctions;
        public PropRule[] propRules;
        public PropPlacementSpec[] props;
        public TrafficSpec traffic;
        public WaypointSpec[] waypoints;
        public MarkingSymbolSpec[] symbols;     // optional (E1): pavement symbols; null = none
        public SitePlateSpec[] sites;           // optional (E2): paved plates for yards, terminals and forecourts; null = none
        public RoadSpec[] roads;                // optional (E5, schemaVersion 3): additional roads (flyover road, ramps, cross roads); null = none
        public RingSpec[] rings;                // optional (E5, schemaVersion 3): roundabouts on the main road; null = none
    }

    public enum Zone { Urban, Commercial, Industrial, Rural, Highway }
    public enum Surface { Good, Worn, Damaged }

    public static class RouteVocab
    {
        public static bool TryZone(string s, out Zone z)
        {
            z = Zone.Rural;
            if (s == null) return false;
            switch (s.Trim().ToLowerInvariant())
            {
                case "urban": z = Zone.Urban; return true;
                case "commercial": z = Zone.Commercial; return true;
                case "industrial": z = Zone.Industrial; return true;
                case "rural": z = Zone.Rural; return true;
                case "highway": z = Zone.Highway; return true;
            }
            return false;
        }

        public static bool TrySurface(string s, out Surface v)
        {
            v = Surface.Good;
            if (s == null) return false;
            switch (s.Trim().ToLowerInvariant())
            {
                case "good": v = Surface.Good; return true;
                case "worn": v = Surface.Worn; return true;
                case "damaged": v = Surface.Damaged; return true;
            }
            return false;
        }
    }
}
