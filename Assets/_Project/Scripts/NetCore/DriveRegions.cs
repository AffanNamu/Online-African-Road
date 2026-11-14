using System;
using System.Collections.Generic;
using System.Linq;

namespace ARO.NetCore
{
    /// <summary>One world the player can drive in: a route of the game (Resources/Routes/{RouteId}.route.json) and what can be done there.</summary>
    public sealed class DriveRegion
    {
        public string RouteId, Name, CityId, Note;
        /// <summary>Server jobs, bus routes and convoys exist for this world (their locations are in it). Kano has none until the server's Kano data (P3).</summary>
        public bool Jobs, Bus, Convoy;
    }

    /// <summary>
    /// The player's choice of where to drive (owner decision of 2026-11-14: "add Kano to the player's route selection"). The shipped Lagos to Ibadan road keeps everything; the two Kano city routes are
    /// free drive (refuelling at their stations included) until the server has Kano locations, jobs and a bus route (P3 of docs/FUNCTIONAL_PLACES_PROPOSAL.md). Pure: the game stores the id on the
    /// device and loads that route at start.
    /// </summary>
    public static class DriveRegions
    {
        public const string Shipped = "ng-lagos-ibadan";
        public static readonly IReadOnlyList<DriveRegion> All = new[]
        {
            new DriveRegion { RouteId = Shipped, Name = "Lagos – Ibadan", CityId = "lagos", Jobs = true, Bus = true, Convoy = true, Note = "The production corridor: jobs, bus routes, convoys and free drive." },
            new DriveRegion { RouteId = "ng-kano-city-e1", Name = "Kano city streets", CityId = "kano", Note = "Arterial, collector and estate streets with their districts, mosques and twelve fuel stations. Free drive." },
            new DriveRegion { RouteId = "ng-kano-city-f1", Name = "Kano flyover interchange", CityId = "kano", Note = "The expressway on a flyover over a roundabout, with the city around it. Free drive." },
        };

        /// <summary>The region for a stored id; the shipped road for anything unknown, empty or tampered with.</summary>
        public static DriveRegion Get(string routeId) => All.FirstOrDefault(r => r.RouteId == routeId) ?? All[0];
        public static bool Known(string routeId) => All.Any(r => r.RouteId == routeId);
        public static IEnumerable<DriveRegion> InCity(string cityId) => All.Where(r => r.CityId == cityId);

        /// <summary>Why a server job, bus route or convoy cannot start in this region; null when it can.</summary>
        public static string Blocked(DriveRegion r, string what)
        {
            bool ok = what == "job" ? r.Jobs : what == "bus" ? r.Bus : what == "convoy" ? r.Convoy : true;
            if (ok) return null;
            string thing = what == "job" ? "Jobs" : what == "bus" ? "Bus routes" : "Convoys";
            return $"{thing} in {r.Name} come with the server's Kano data. Switch to {All[0].Name} on the world map to take one.";
        }
    }
}
