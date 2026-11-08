using System;
using System.Collections.Generic;

namespace ARO.NetCore
{
    /// <summary>
    /// The road-class catalogue (RB-17) as data: the six classes of the Kano city plan with the cross-section each one builds. A route spec may use these as starting points
    /// (`RoadClasses.Make`) or declare its own profiles; the class name is carried in `RoadProfileSpec.roadClass` so tools, the validator and the prop scatter can tell an arterial from a lane.
    /// Dimensions follow the cross-sections shown in the reference set (R-022 modern boulevard, R-023 divided highway, R-006 residential street, R-013 to R-015 logistics access) and common
    /// African urban practice; they are design values, not surveyed ones. Nothing here is used by an existing route, so the golden master of every shipped route is untouched.
    /// </summary>
    public static class RoadClasses
    {
        public const string Expressway = "expressway", Arterial = "arterial", Collector = "collector", Local = "local", Lane = "lane", Service = "service";
        public static readonly string[] All = { Expressway, Arterial, Collector, Local, Lane, Service };

        public static bool IsKnown(string cls) => Array.IndexOf(All, cls) >= 0;

        /// <summary>A fresh profile of the class. `id` defaults to the class name.</summary>
        public static RoadProfileSpec Make(string cls, string id = null)
        {
            var p = new RoadProfileSpec { id = id ?? cls, roadClass = cls };
            switch (cls)
            {
                case Expressway:     // grade-separated, divided, three lanes a side, concrete median barrier, hard shoulders, guardrail
                    p.lanesPerDirection = 3; p.laneWidth = 3.65f; p.medianWidth = 4.5f; p.innerShoulderWidth = 1.0f; p.shoulderWidth = 2.8f; p.vergeWidth = 3.0f;
                    p.edgeBarrier = "guardrail"; p.centreLine = "none"; p.laneLineDash = 3f; p.laneLineGap = 9f; break;
                case Arterial:       // urban boulevard: two lanes a side, planted median in a painted kerb, kerbs, gutters and 2.5 m walkways
                    p.lanesPerDirection = 2; p.laneWidth = 3.5f; p.medianWidth = 3.0f; p.medianStyle = "planter"; p.innerShoulderWidth = 0.3f; p.shoulderWidth = 0.5f;
                    p.kerbHeight = 0.12f; p.kerbPaint = "black_white"; p.sidewalkWidth = 2.5f; p.drainWidth = 0.4f; p.vergeWidth = 1.0f; p.centreLine = "none"; break;
                case Collector:      // district street: one lane a side, kerbs and 2 m walkways, painted centre line
                    p.lanesPerDirection = 1; p.laneWidth = 3.5f; p.shoulderWidth = 0.5f; p.kerbHeight = 0.12f; p.kerbPaint = "none"; p.sidewalkWidth = 2.0f; p.drainWidth = 0.3f;
                    p.vergeWidth = 0.8f; p.centreLine = "dashed"; p.centreLineColor = "white"; break;
                case Local:          // residential street: narrow lanes, low kerb, 1.5 m walkway, no centre line
                    p.lanesPerDirection = 1; p.laneWidth = 3.0f; p.shoulderWidth = 0.3f; p.kerbHeight = 0.10f; p.sidewalkWidth = 1.5f; p.vergeWidth = 0.6f; p.centreLine = "none"; break;
                case Lane:           // access lane between plots: no kerb, tight, unmarked
                    p.lanesPerDirection = 1; p.laneWidth = 2.75f; p.shoulderWidth = 0.5f; p.vergeWidth = 0.8f; p.centreLine = "none"; p.camberPercent = 2.5f; break;
                case Service:        // logistics and bus-site access: wide lanes for trucks, a 2.5 m lay-by shoulder for loading bays, heavy kerb, no walkway
                    p.lanesPerDirection = 1; p.laneWidth = 3.75f; p.shoulderWidth = 2.5f; p.kerbHeight = 0.15f; p.vergeWidth = 1.2f; p.centreLine = "dashed"; p.centreLineColor = "white"; break;
                default: throw new ArgumentException("unknown road class '" + cls + "'");
            }
            return p;
        }

        /// <summary>One profile per class, ids "kano_" + class (the names the Kano route data uses).</summary>
        public static RoadProfileSpec[] Catalogue(string idPrefix = "kano_")
        {
            var r = new List<RoadProfileSpec>();
            foreach (var c in All) r.Add(Make(c, idPrefix + c));
            return r.ToArray();
        }
    }
}
