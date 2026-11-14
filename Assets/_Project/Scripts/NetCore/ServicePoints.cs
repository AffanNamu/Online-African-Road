using System;
using System.Collections.Generic;

namespace ARO.NetCore
{
    /// <summary>A socket of a placed prop, in the world: a pump, an entry, an exit (later a dock or a bus bay).</summary>
    public struct ServicePoint
    {
        public string Prop, Origin, Name, Kind;
        /// <summary>World position of the point (on the forecourt, beside the pump island).</summary>
        public V3 Pos;
        /// <summary>World heading from the point towards what it serves (for a pump: towards the dispenser island). Degrees, Unity convention (0 = +z, 90 = +x).</summary>
        public float YawDeg;
        public bool IsFuel => ServicePoints.IsFuel(Kind);
    }

    /// <summary>
    /// P1 of docs/FUNCTIONAL_PLACES_PROPOSAL.md (approved by the owner on 2026-11-14): the places a vehicle refuels at. Every placed prop whose catalogue entry has sockets gives world points; a vehicle
    /// that STOPS beside a pump point can refuel there. Pure and deterministic: computed from the same placements on every client, nothing is sent over the network. Price and litres are decided by the
    /// server (buy_fuel); the numbers here are only the estimate shown before paying and the meter shown while the tank fills.
    /// </summary>
    public static class ServicePoints
    {
        /// <summary>Horizontal distance from the vehicle's centre to a pump point that counts as "at the pump". The point is at the island's side; a truck beside the island has its centre 1.5 to 4 m from it.</summary>
        public const float PumpRadius = 5f;
        /// <summary>Vertical tolerance: a pump on a lower or upper level (a flyover deck above a forecourt) is not this vehicle's pump.</summary>
        public const float PumpMaxRise = 2f;
        /// <summary>The vehicle must be stopped.</summary>
        public const float PumpMaxKmh = 1f;
        /// <summary>Where a vehicle stands beside a pump point: its centre this far from the point, away from the island (the narrowest lane, the roadside station's 3 m between island and post, fits a 2.4 m truck).</summary>
        public const float StandOff = 1.3f;

        public static bool IsFuel(string kind) => kind == "fuel_pump" || kind == "truck_fuel_stop";

        static float Wrap(float a) { a %= 360f; if (a > 180f) a -= 360f; if (a <= -180f) a += 360f; return a; }

        /// <summary>World points of every socket of the given placed props.</summary>
        public static List<ServicePoint> FromInstances(IEnumerable<PropInstance> instances, Func<string, PropDef> lookup)
        {
            var res = new List<ServicePoint>();
            foreach (var inst in instances)
            {
                var def = lookup(inst.Prop); if (def == null || !def.HasSockets) continue;
                foreach (var s in def.sockets)
                {
                    if (s == null) continue;
                    res.Add(new ServicePoint { Prop = def.id, Origin = inst.Origin, Name = s.name, Kind = s.kind, Pos = PropBatcher.Place(new V3(s.x, s.y, s.z), inst.Pos, inst.YawDeg, inst.Scale), YawDeg = Wrap(inst.YawDeg + s.yawDeg) });
                }
            }
            return res;
        }

        /// <summary>The pump a vehicle at `pos` moving at `speedKmh` can refuel at: the nearest fuel point within <see cref="PumpRadius"/> and <see cref="PumpMaxRise"/>, only when stopped. -1 otherwise.</summary>
        public static int PumpAt(IReadOnlyList<ServicePoint> points, V3 pos, float speedKmh) => speedKmh < PumpMaxKmh ? NearestFuel(points, pos, PumpRadius) : -1;

        /// <summary>The nearest fuel point within `radius` (horizontal) and <see cref="PumpMaxRise"/> (vertical); the first in list order on a tie. -1 when none.</summary>
        public static int NearestFuel(IReadOnlyList<ServicePoint> points, V3 pos, float radius)
        {
            int best = -1; float bd = radius * radius;
            for (int i = 0; i < points.Count; i++)
            {
                var p = points[i]; if (!IsFuel(p.Kind) || Math.Abs(p.Pos.Y - pos.Y) > PumpMaxRise) continue;
                float dx = p.Pos.X - pos.X, dz = p.Pos.Z - pos.Z, d2 = dx * dx + dz * dz;
                if (d2 < bd || (best < 0 && d2 <= bd)) { bd = d2; best = i; }
            }
            return best;
        }

        /// <summary>"Pump 2" for pump_2, "Pump" for the truck island's stops and any other name.</summary>
        public static string Label(ServicePoint p)
        {
            if (p.Name != null && p.Name.StartsWith("pump_", StringComparison.Ordinal) && int.TryParse(p.Name.Substring(5), out int n)) return "Pump " + n;
            return "Pump";
        }

        /// <summary>Where a vehicle stands to use a pump point (test pages place the vehicle there): <see cref="StandOff"/> from the point, away from the island, heading along the island.</summary>
        public static void StandingPlace(ServicePoint p, out V3 pos, out float headingDeg)
        {
            float r = p.YawDeg * (float)Math.PI / 180f;
            pos = new V3(p.Pos.X - (float)Math.Sin(r) * StandOff, p.Pos.Y, p.Pos.Z - (float)Math.Cos(r) * StandOff);
            headingDeg = Wrap(p.YawDeg - 90f);
        }

        /// <summary>Litres a pump can put in: the capacity minus the game's own reading, never negative.</summary>
        public static double LitresToFill(double fuelL, double capacityL) => Math.Max(0.0, capacityL - Math.Max(0.0, fuelL));

        /// <summary>What the server will charge for `litres` at `pricePerL`: ceil(litres x price), the formula of buy_fuel (its tests: supabase/tests/54_pump_refuel.sql). 0 for nothing.</summary>
        public static long Cost(double litres, double pricePerL) => litres <= 0.0 || pricePerL <= 0.0 ? 0 : (long)Math.Ceiling(litres * pricePerL - 1e-9);

        /// <summary>How long the pump runs for the animation: 2 s, plus a second per 25 litres, at most 6 s.</summary>
        public static float FillSeconds(double litres) => (float)Math.Max(2.0, Math.Min(6.0, 2.0 + litres / 25.0));

        /// <summary>The litres on the pump meter `t` seconds into a fill from `from` to `to` that lasts `seconds` (eased in and out; exactly `to` at the end).</summary>
        public static double Meter(double from, double to, double t, double seconds)
        {
            if (seconds <= 0.0 || t >= seconds) return to;
            if (t <= 0.0) return from;
            double u = t / seconds; u = u * u * (3.0 - 2.0 * u);
            return from + (to - from) * u;
        }

        static readonly System.Globalization.CultureInfo Inv = System.Globalization.CultureInfo.InvariantCulture;

        /// <summary>The HUD line beside a pump, before paying: "Pump 2  ·  F  Refuel 63 L for 126 coins" (the price once the server quoted it), or "Tank full".</summary>
        public static string Prompt(string label, double litres, long cost, bool priceKnown, string key)
        {
            if (litres < 0.5) return label + "  ·  Tank full";
            return label + "  ·  " + key + "  Refuel " + Math.Round(litres).ToString("0", Inv) + " L" + (priceKnown ? " for " + cost.ToString("N0", Inv) + " coins" : "");
        }

        /// <summary>The HUD line while the pump runs: the meter in litres and what it costs so far, out of the server's answer.</summary>
        public static string FillLine(string label, double meterL, double totalL, long totalCost)
        {
            meterL = Math.Max(0.0, Math.Min(meterL, totalL));
            long soFar = totalL > 0.0 ? (long)Math.Round(totalCost * (meterL / totalL)) : totalCost;
            return label + "  ·  Refuelling  " + meterL.ToString("0.0", Inv) + " / " + totalL.ToString("0.0", Inv) + " L  ·  " + soFar.ToString("N0", Inv) + " coins";
        }

        /// <summary>
        /// The first pump along the route's main road from its start, within `maxMetres` (test pages only: "?smoke=drive&pump=1" puts the vehicle beside it). Scans the chunks along the road and their
        /// neighbours in order, so the answer is deterministic. False when no pump stands that close to the road.
        /// </summary>
        public static bool FirstPump(RouteModel m, Func<string, PropDef> lookup, float chunkSize, float maxMetres, out ServicePoint pump)
        {
            pump = default; var seen = new HashSet<long>(); float end = Math.Min(m.Main.Length, maxMetres);
            for (float s = 0f; s <= end; s += chunkSize * 0.5f)
            {
                m.Main.Spline.FrameAt(s, out var at, out _, out _);
                int cx = (int)Math.Floor(at.X / chunkSize), cz = (int)Math.Floor(at.Z / chunkSize);
                var found = new List<ServicePoint>();
                for (int dz = -1; dz <= 1; dz++)
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        long key = ((long)(cx + dx) << 32) ^ (uint)(cz + dz); if (!seen.Add(key)) continue;
                        float x0 = (cx + dx) * chunkSize, z0 = (cz + dz) * chunkSize;
                        foreach (var p in FromInstances(PropScatter.PlaceChunk(m, x0, z0, x0 + chunkSize, z0 + chunkSize), lookup)) if (p.IsFuel) found.Add(p);
                    }
                if (found.Count == 0) continue;
                int best = 0; float bd = float.MaxValue;                                                     // the one nearest the road point where pumps first appear
                for (int i = 0; i < found.Count; i++) { float ex = found[i].Pos.X - at.X, ez = found[i].Pos.Z - at.Z, dd = ex * ex + ez * ez; if (dd < bd) { bd = dd; best = i; } }
                pump = found[best]; return true;
            }
            return false;
        }
    }
}
