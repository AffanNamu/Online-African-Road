using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace ARO.NetCore
{
    // ===================================================================================================== Bus Routes screen: pure rules (no Unity types, unit tested)
    public enum DemandLevel { Low, Medium, High }
    public enum StopProgress { Completed, Current, Upcoming }

    /// <summary>The facts about a bus the player owns that decide whether a route can be started with it.</summary>
    public sealed class BusChoice { public string Id = "", Name = ""; public double DamagePct, FuelL; public bool Selected; public int Seats; }

    public static class BusRouteRules
    {
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
        /// <summary>The server stores a stop's waiting passengers as 0..80.</summary>
        public const int MaxDemand = 80;

        // ---------------------------------------------------------------- demand (the server's per-stop numbers; the level is a plain reading of them)
        /// <summary>Average passengers waiting at the stops where people board (every stop but the last: nobody boards at the terminus).</summary>
        public static double AverageDemand(IList<int> stopDemands)
        {
            if (stopDemands == null || stopDemands.Count == 0) return 0;
            var boarding = stopDemands.Count > 1 ? stopDemands.Take(stopDemands.Count - 1) : stopDemands;
            return boarding.Average();
        }
        public static DemandLevel LevelOf(double averageDemand) => averageDemand >= 40 ? DemandLevel.High : averageDemand >= 20 ? DemandLevel.Medium : DemandLevel.Low;
        public static string LevelText(DemandLevel l) => l == DemandLevel.High ? "High" : l == DemandLevel.Medium ? "Medium" : "Low";
        public static float DemandFraction(int demand) => Math.Max(0f, Math.Min(1f, demand / (float)MaxDemand));

        // ---------------------------------------------------------------- texts
        public static string StopsText(int n) => n == 1 ? "1 stop" : n + " stops";
        public static string FareText(int fare) => "₦ " + Math.Max(0, fare).ToString("N0", Inv);
        public static string XpText(int xp) => "+" + Math.Max(0, xp).ToString("N0", Inv) + " XP";
        public static string Money(long amount) => "₦ " + Math.Max(0, amount).ToString("N0", Inv);
        /// <summary>"Mile 12 Market, Oshodi Terminal, Ikeja Bus Park" - the names in stop order, for a one-line summary.</summary>
        public static string Summary(IList<string> stopNames, string sep = " → ") => stopNames == null || stopNames.Count == 0 ? "" : string.Join(sep, stopNames);
        /// <summary>What happens at a stop, from the server's share of passengers that get off.</summary>
        public static string AlightText(int alightPct, bool isFirst, bool isLast) =>
            isFirst && alightPct <= 0 ? "Start of the route" : isLast ? "Everyone gets off" : Math.Max(0, Math.Min(100, alightPct)).ToString(Inv) + "% get off here";

        // ---------------------------------------------------------------- a run in progress
        public static StopProgress StopState(int index, int nextIndex) => index < nextIndex ? StopProgress.Completed : index == nextIndex ? StopProgress.Current : StopProgress.Upcoming;
        public static string StopStateText(StopProgress s) => s == StopProgress.Completed ? "Completed" : s == StopProgress.Current ? "Current stop" : "Upcoming";
        public static float AboardFraction(int aboard, int capacity) => capacity <= 0 ? 0f : Math.Max(0f, Math.Min(1f, aboard / (float)capacity));
        public static string AboardText(int aboard, int capacity) => capacity > 0 ? aboard + " / " + capacity : aboard.ToString(Inv);
        /// <summary>Fraction of the line to fill between the first and last stop dot: the served stops.</summary>
        public static float LineFill(int stopCount, int nextIndex) => stopCount <= 1 ? 0f : Math.Max(0f, Math.Min(1f, nextIndex / (float)(stopCount - 1)));

        // ---------------------------------------------------------------- the player's bus
        public static int Condition(double damagePct) => (int)Math.Round(Math.Max(0, Math.Min(100, 100 - damagePct)));
        public static string ConditionText(double damagePct) => "Condition: " + Condition(damagePct) + "%";
        public static string SeatsText(int seats) => seats > 0 ? seats + " seats" : "";

        /// <summary>The bus to run a route with: the selected one if it is a bus that can run, otherwise the one in the best state that can. Null when none can.</summary>
        public static BusChoice PickBus(IEnumerable<BusChoice> buses)
        {
            var list = (buses ?? new BusChoice[0]).Where(b => b != null).ToList();
            var ready = list.Where(b => ProblemWith(b) == null).ToList();
            var chosen = ready.FirstOrDefault(b => b.Selected) ?? ready.OrderBy(b => b.DamagePct).ThenByDescending(b => b.FuelL).FirstOrDefault();
            return chosen ?? list.FirstOrDefault(b => b.Selected) ?? list.OrderBy(b => b.DamagePct).FirstOrDefault();
        }

        /// <summary>Why this bus cannot start a route (the server refuses the same cases), or null.</summary>
        public static string ProblemWith(BusChoice b) => b == null ? "You need a bus to run a route." : b.DamagePct >= 100 ? "Your bus is too damaged. Repair it in the Garage first." : b.FuelL <= 0 ? "Your bus is out of fuel. Refuel it in the Garage first." : null;

        /// <summary>The reason Start route is off right now (shown under the button), or null when it can be used.</summary>
        public static string StartBlockedReason(BusChoice bus, bool freightJobActive, bool busRunActive, bool routeKnown)
        {
            if (busRunActive) return "You already have a bus route in progress.";
            if (!routeKnown) return "Choose a route first.";
            if (bus == null) return "You need a bus to run a route.";
            if (freightJobActive) return "Finish or abandon your freight job before starting a bus route.";
            return ProblemWith(bus);
        }
    }
}
