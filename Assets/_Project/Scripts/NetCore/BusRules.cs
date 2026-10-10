using System;
using System.Globalization;

namespace ARO.NetCore
{
    /// <summary>A bus stop as the client needs it (name + world position). The server owns demand and fares.</summary>
    public struct StopPoint
    {
        public string Name; public float X, Z;
        public StopPoint(string name, float x, float z) { Name = name; X = x; Z = z; }
    }

    /// <summary>
    /// Client-side bus rules. These only decide WHEN to ask the server to serve a stop and how to present progress.
    /// The server re-checks everything (radius 60 m, fresh telemetry, minimum travel time); this never grants anything.
    /// </summary>
    public static class BusRules
    {
        /// <summary>Tighter than the server's 60 m so a request is never sent from the edge of the allowed zone.</summary>
        public const float ServeRadius = 45f;
        public const float StopSpeedKmh = 6f;

        public static float Distance(float x0, float z0, float x1, float z1)
        {
            float dx = x1 - x0, dz = z1 - z0; return (float)Math.Sqrt(dx * dx + dz * dz);
        }

        public static bool CanServe(float distanceMetres, float speedKmh) =>
            !float.IsNaN(distanceMetres) && !float.IsNaN(speedKmh) && distanceMetres <= ServeRadius && speedKmh < StopSpeedKmh;

        public static float RouteLength(StopPoint[] stops)
        {
            float total = 0f;
            for (int i = 1; i < stops.Length; i++) total += Distance(stops[i - 1].X, stops[i - 1].Z, stops[i].X, stops[i].Z);
            return total;
        }

        /// <summary>
        /// Fraction (0..1) of the route completed: whole legs already served plus the part of the current leg covered,
        /// measured as how much closer the bus is to the next stop than the leg length. Never decreases the served part.
        /// </summary>
        public static float Progress(StopPoint[] stops, int nextIndex, float x, float z)
        {
            float total = RouteLength(stops);
            if (stops.Length < 2 || total <= 0f || nextIndex <= 0) return 0f;
            if (nextIndex >= stops.Length) return 1f;
            float done = 0f;
            for (int i = 1; i < nextIndex; i++) done += Distance(stops[i - 1].X, stops[i - 1].Z, stops[i].X, stops[i].Z);
            var prev = stops[nextIndex - 1]; var next = stops[nextIndex];
            float leg = Distance(prev.X, prev.Z, next.X, next.Z);
            float covered = Math.Max(0f, Math.Min(leg, leg - Distance(x, z, next.X, next.Z)));
            return Math.Max(0f, Math.Min(1f, (done + covered) / total));
        }

        public static string Prompt(string stopName, bool isLast, bool isFirst, float distanceMetres, float speedKmh)
        {
            if (distanceMetres > ServeRadius * 3f) return "";
            string what = isFirst ? "board passengers" : isLast ? "let everyone off" : "pick up and drop off passengers";
            return distanceMetres <= ServeRadius ? (speedKmh < StopSpeedKmh ? "Serving " + stopName + "..." : "Slow down to stop at " + stopName)
                                                 : "Stop at " + stopName + " to " + what;
        }

        public static string StopSummary(string stopName, int alighted, int boarded, int aboard, long fare)
        {
            var inv = CultureInfo.InvariantCulture;
            var s = stopName + ": ";
            if (alighted > 0) s += alighted + " got off";
            if (alighted > 0 && boarded > 0) s += ", ";
            if (boarded > 0) s += boarded + " boarded";
            if (alighted == 0 && boarded == 0) s += "nobody waiting";
            s += "  (" + aboard + " aboard)";
            if (fare > 0) s += "  +" + fare.ToString("N0", inv) + " coins";
            return s;
        }
    }

    /// <summary>The player's view of an active run. Updated ONLY from server replies, never predicted.</summary>
    public sealed class BusRunState
    {
        public int NextIndex { get; private set; }
        public int Aboard { get; private set; }
        public long Revenue { get; private set; }
        public int StopsServed { get; private set; }

        public void Reset() { NextIndex = 0; Aboard = 0; Revenue = 0; StopsServed = 0; }

        public void Apply(int aboardAfter, long fare)
        {
            Aboard = Math.Max(0, aboardAfter); Revenue += Math.Max(0, fare); NextIndex++; StopsServed++;
        }
    }
}
