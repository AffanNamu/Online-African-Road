using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace ARO.NetCore
{
    // Pure rules for the Trucks & Buses screen. Nothing here talks to the network or invents a number: it filters, sorts, compares and labels the rows the
    // SERVER's catalogue returned (price, level, ownership, purchase state are the server's), so what the screen says is what the data says.

    public enum VehicleTab { All, Trucks, Buses, Light, Owned, Shop }
    public enum VehicleSort { Catalogue, PriceLow, PriceHigh, Power, Capacity, Level }
    /// <summary>What the primary button of the vehicle panel does for a vehicle.</summary>
    public enum VehicleActionKind { Selected, Select, Buy, ComingSoon, LevelRequired, NeedFunds, NotForSale }
    public enum ConditionState { Ready, MaintenanceRecommended, RepairRequired, Critical }
    public enum FuelState { Healthy, Low, Critical }

    /// <summary>One catalogue row as the screen needs it (mapped from the server's reply by the UI layer; null numbers stay null: the brand sheet does not give every value).</summary>
    public sealed class VehicleRow
    {
        public string Id = "", Name = "", Category = "truck", VehicleClass = "", Manufacturer = "", Drive, Engine, Transmission, FuelType, Description = "", PictureKey = "", Availability = "available", PurchaseState = "available";
        public long Price, NeedMore; public int UnlockLevel = 1, Owned; public int? Hp, TorqueNm, WeightKg;
        public double TopSpeedKmh, FuelCapacityL; public int CargoKg, Passengers;
        public string[] Features = new string[0];
        public bool IsOwned => Owned > 0;
        public bool Drivable => Availability == "available";
    }

    public struct VehicleAction { public VehicleActionKind Kind; public string Label; public bool Enabled; }

    public static class VehicleRules
    {
        public const int MinCompare = 2, MaxCompare = 4;

        public static bool IsLight(string category) => category == "car" || category == "van" || category == "motorcycle";

        public static bool InTab(VehicleRow v, VehicleTab tab)
        {
            switch (tab)
            {
                case VehicleTab.Trucks: return v.Category == "truck";
                case VehicleTab.Buses: return v.Category == "bus";
                case VehicleTab.Light: return IsLight(v.Category);
                case VehicleTab.Owned: return v.IsOwned;
                case VehicleTab.Shop: return !v.IsOwned;      // everything the player could still acquire, including what is announced
                default: return true;
            }
        }

        /// <summary>Case-insensitive: name, class, manufacturer and the vehicle type ("bus", "truck", "car", "van", "motorcycle" and "bike").</summary>
        public static bool Matches(VehicleRow v, string search)
        {
            if (string.IsNullOrWhiteSpace(search)) return true;
            string q = search.Trim();
            foreach (var part in q.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries))
            {
                bool hit = Has(v.Name, part) || Has(v.VehicleClass, part) || Has(v.Manufacturer, part) || Has(v.Category, part) || Has(TypeWord(v.Category), part);
                if (!hit) return false;
            }
            return true;
        }
        static bool Has(string hay, string needle) => hay != null && hay.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0;
        static string TypeWord(string category) => category == "motorcycle" ? "bike motorbike" : category == "bus" ? "buses coach" : category == "truck" ? "trucks lorry" : category == "car" ? "cars" : category == "van" ? "vans" : "";

        /// <summary>One catalogue, many views: nothing is copied per tab.</summary>
        public static List<VehicleRow> Filter(IEnumerable<VehicleRow> all, VehicleTab tab, string search, VehicleSort sort = VehicleSort.Catalogue)
        {
            var l = (all ?? new VehicleRow[0]).Where(v => InTab(v, tab) && Matches(v, search)).ToList();
            switch (sort)
            {
                case VehicleSort.PriceLow: return l.OrderBy(v => v.Price).ThenBy(v => v.Name, StringComparer.Ordinal).ToList();
                case VehicleSort.PriceHigh: return l.OrderByDescending(v => v.Price).ThenBy(v => v.Name, StringComparer.Ordinal).ToList();
                case VehicleSort.Power: return l.OrderByDescending(v => v.Hp ?? -1).ThenBy(v => v.Name, StringComparer.Ordinal).ToList();
                case VehicleSort.Capacity: return l.OrderByDescending(Capacity).ThenBy(v => v.Name, StringComparer.Ordinal).ToList();
                case VehicleSort.Level: return l.OrderBy(v => v.UnlockLevel).ThenBy(v => v.Name, StringComparer.Ordinal).ToList();
                default: return l;      // the server's catalogue order
            }
        }

        /// <summary>The number a vehicle carries: kilograms for freight vehicles, seats for passenger ones.</summary>
        public static double Capacity(VehicleRow v) => v.Category == "bus" || (v.Passengers > 0 && v.CargoKg == 0) ? v.Passengers : v.CargoKg;
        public static string CapacityLabel(VehicleRow v) => v.Category == "bus" || (v.Passengers > 0 && v.CargoKg == 0) ? "Passengers" : "Cargo capacity";
        public static string CapacityText(VehicleRow v)
        {
            if (v.Category == "bus" || (v.Passengers > 0 && v.CargoKg == 0)) return v.Passengers + " seats";
            return v.CargoKg > 0 ? v.CargoKg.ToString("N0", CultureInfo.InvariantCulture) + " kg" : "None";
        }

        /// <summary>The primary button: what the server said the player can do with this model.</summary>
        public static VehicleAction ActionFor(VehicleRow v, bool selected)
        {
            switch (v.PurchaseState)
            {
                case "owned": return selected ? new VehicleAction { Kind = VehicleActionKind.Selected, Label = "Selected", Enabled = false } : new VehicleAction { Kind = VehicleActionKind.Select, Label = "Select Vehicle", Enabled = true };
                case "coming_soon": return new VehicleAction { Kind = VehicleActionKind.ComingSoon, Label = "Coming soon", Enabled = false };
                case "level_required": return new VehicleAction { Kind = VehicleActionKind.LevelRequired, Label = "Requires Level " + v.UnlockLevel, Enabled = false };
                case "insufficient_funds": return new VehicleAction { Kind = VehicleActionKind.NeedFunds, Label = "Need " + Naira(v.NeedMore) + " more", Enabled = false };
                case "not_for_sale": return new VehicleAction { Kind = VehicleActionKind.NotForSale, Label = "Not for sale", Enabled = false };
                default: return new VehicleAction { Kind = VehicleActionKind.Buy, Label = "Buy " + Naira(v.Price), Enabled = true };
            }
        }
        public static string Naira(long n) => "₦" + n.ToString("N0", CultureInfo.InvariantCulture);

        /// <summary>Short label of the ownership/availability state shown on a card ("OWNED", "COMING SOON", the price...).</summary>
        public static string CardBadge(VehicleRow v, bool selected)
        {
            switch (v.PurchaseState)
            {
                case "owned": return selected ? "ACTIVE" : "OWNED";
                case "coming_soon": return "COMING SOON";
                case "level_required": return "LEVEL " + v.UnlockLevel;
                default: return Naira(v.Price);
            }
        }

        // ---- condition and fuel (thresholds mirror the server: needs_repair at 70 % damage, low_fuel under 10 %)
        public static ConditionState ConditionOf(double conditionPct)
        {
            if (conditionPct <= 10) return ConditionState.Critical;
            if (conditionPct < 30) return ConditionState.RepairRequired;
            if (conditionPct < 70) return ConditionState.MaintenanceRecommended;
            return ConditionState.Ready;
        }
        public static string ConditionText(double conditionPct)
        {
            switch (ConditionOf(conditionPct))
            {
                case ConditionState.Critical: return "Critical: repair before any job";
                case ConditionState.RepairRequired: return "Repair required";
                case ConditionState.MaintenanceRecommended: return "Maintenance recommended";
                default: return "Ready";
            }
        }
        public static FuelState FuelOf(double fuelPct) => fuelPct < 10 ? FuelState.Critical : fuelPct < 25 ? FuelState.Low : FuelState.Healthy;

        /// <summary>Length of a statistic bar: the value against the largest value in the catalogue (a relative bar, never an invented rating).</summary>
        public static double Fraction(double value, double max) => max <= 0 ? 0 : Math.Max(0, Math.Min(1, value / max));

        // ---- comparison
        public sealed class CompareRow { public string Label; public string[] Cells; public int[] Best; public CompareRow(string label, int n) { Label = label; Cells = new string[n]; Best = new int[0]; } }

        /// <summary>A comparison is only meaningful for 2 to 4 distinct vehicles. Mixed kinds are compared field by field and a field that does not apply to a kind says so ("n/a") instead of showing 0.</summary>
        public static List<CompareRow> Compare(IList<VehicleRow> v, out string error)
        {
            error = null;
            if (v == null || v.Count < MinCompare) { error = "Pick at least " + MinCompare + " vehicles to compare."; return new List<CompareRow>(); }
            if (v.Count > MaxCompare) { error = "You can compare up to " + MaxCompare + " vehicles."; return new List<CompareRow>(); }
            if (v.Select(x => x.Id).Distinct().Count() != v.Count) { error = "Each vehicle can only appear once."; return new List<CompareRow>(); }
            int n = v.Count; var rows = new List<CompareRow>();
            void Add(string label, Func<VehicleRow, double?> num, Func<VehicleRow, string> text, bool higherIsBetter, bool marksBest = true)
            {
                var r = new CompareRow(label, n); var vals = new double?[n];
                for (int i = 0; i < n; i++) { vals[i] = num(v[i]); r.Cells[i] = vals[i].HasValue ? text(v[i]) : "n/a"; }
                var known = vals.Select((x, i) => (x, i)).Where(t => t.x.HasValue).ToList();
                if (marksBest && known.Count >= 2)
                {
                    double best = higherIsBetter ? known.Max(t => t.x.Value) : known.Min(t => t.x.Value);
                    if (known.Any(t => Math.Abs(t.x.Value - best) > 1e-9)) r.Best = known.Where(t => Math.Abs(t.x.Value - best) < 1e-9).Select(t => t.i).ToArray();      // identical values are not "better"
                }
                rows.Add(r);
            }
            Add("Price", x => x.Price, x => Naira(x.Price), false);
            Add("Required level", x => x.UnlockLevel, x => "Level " + x.UnlockLevel, false);
            Add("Power", x => x.Hp, x => x.Hp + " HP", true);
            Add("Torque", x => x.TorqueNm, x => x.TorqueNm.Value.ToString("N0", CultureInfo.InvariantCulture) + " Nm", true);
            Add("Top speed", x => x.TopSpeedKmh, x => x.TopSpeedKmh.ToString("0", CultureInfo.InvariantCulture) + " km/h", true);
            Add("Fuel capacity", x => x.FuelCapacityL, x => x.FuelCapacityL.ToString("N0", CultureInfo.InvariantCulture) + " L", true);
            bool anyFreight = v.Any(x => !(x.Category == "bus" || (x.Passengers > 0 && x.CargoKg == 0))), anyPassenger = v.Any(x => x.Passengers > 0);
            if (anyFreight) Add("Cargo capacity", x => x.CargoKg > 0 ? (double?)x.CargoKg : (x.Passengers > 0 ? null : (double?)0), x => x.CargoKg.ToString("N0", CultureInfo.InvariantCulture) + " kg", true);
            if (anyPassenger) Add("Passengers", x => x.Passengers > 0 ? (double?)x.Passengers : null, x => x.Passengers + " seats", true);
            Add("Weight", x => x.WeightKg, x => x.WeightKg.Value.ToString("N0", CultureInfo.InvariantCulture) + " kg", false, marksBest: false);
            return rows;
        }

        // ---- job fit
        /// <summary>The sentence under the vehicle: the server's answer about the open jobs, in words.</summary>
        public static string JobFitText(bool known, bool drivable, bool compatible, string reason, int fits, int openJobs, string category)
        {
            if (!known) return "Job compatibility is not available right now.";
            if (!drivable) return "Not drivable yet: this model has not been released.";
            if (compatible) return fits == 1 ? "Can take 1 of the " + openJobs + " open jobs." : "Can take " + fits + " of the " + openJobs + " open jobs.";
            if (reason == "no_open_jobs") return "No jobs are open right now.";
            return "This vehicle cannot carry the cargo of the open jobs: they need another vehicle type.";
        }

        // ---- showroom
        /// <summary>What the showroom can honestly offer for a vehicle: the real pictures that exist. A 3D viewer is a separate capability and says so.</summary>
        public sealed class Showroom
        {
            public int ViewPictures;           // front / side / rear ... pictures that exist for the vehicle
            public bool HasInterior;           // an interior picture exists
            public bool Has3D => false;        // no vehicle model is in the game for this screen yet
            public bool CanRotate => Has3D;
            public string Unavailable3DReason => "The 3D showroom arrives with the vehicle models.";
        }
    }
}
