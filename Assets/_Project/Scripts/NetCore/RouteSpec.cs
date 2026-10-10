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
    }

    /// <summary>A side road leaving the main road. angleDeg > 0 turns right of the travel direction, < 0 left.</summary>
    [Serializable]
    public class JunctionSpec
    {
        public string id, name, profile, kind = "t_junction";
        public float s, angleDeg = 90f, length = 150f;
        public float rise = 0f;     // side-road elevation change over its length (metres)
    }

    /// <summary>Procedural roadside scatter. Offsets are measured from the edge of the road formation, never from its centre.</summary>
    [Serializable]
    public class PropRule
    {
        public string id, prop;
        public string zones;                    // comma separated (urban,commercial...), empty = every zone
        public string side = "both";            // both | left | right
        public float sFrom = 0f, sTo = 0f;      // sTo <= sFrom means "to the end of the route"
        public float offsetMin = 2f, offsetMax = 12f;
        public float spacingMin = 20f, spacingMax = 40f;
        public float presence = 1f;             // probability that a slot is used, 0..1
        public float scaleMin = 1f, scaleMax = 1f;
        public float yawJitterDeg = 10f;
        public bool faceRoad = true;
    }

    /// <summary>A hand-placed set piece (sign, fuel station, bus stop). lateral is from the road centre line, positive = right.</summary>
    [Serializable]
    public class PropPlacementSpec
    {
        public string id, prop, text;
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
