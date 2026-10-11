using System.Collections.Generic;
using System.Linq;
using ARO.NetCore;
using Xunit;

public class VehicleModelTests
{
    static VehicleRow V(string id, string name, string cat, long price = 1000, string state = "available", int owned = 0, int level = 1, int? hp = null, int cargo = 0, int pax = 0, double speed = 100, double fuel = 100, string cls = "", int? torque = null, int? weight = null)
        => new VehicleRow { Id = id, Name = name, Category = cat, Price = price, PurchaseState = state, Owned = owned, UnlockLevel = level, Hp = hp, CargoKg = cargo, Passengers = pax, TopSpeedKmh = speed, FuelCapacityL = fuel, VehicleClass = cls, Manufacturer = "ARO Motors", TorqueNm = torque, WeightKg = weight };

    static List<VehicleRow> Catalog() => new List<VehicleRow>
    {
        V("t1", "ARO TITAN 480", "truck", 95000, "coming_soon", hp: 480, cargo: 40000, cls: "Heavy Tractor Unit", torque: 2300),
        V("t2", "Savanna 4x2 Light Truck", "truck", 0, "owned", 1, cargo: 4000, cls: "Light Truck", torque: 900, weight: 4200),
        V("b1", "Danfo City Bus", "bus", 45000, "available", pax: 40, cls: "City Bus"),
        V("b2", "ARO CITYLINER 45", "bus", 90000, "coming_soon", level: 8, pax: 55, cls: "Intercity Bus"),
        V("c1", "ARO CITYGO", "car", 28000, "coming_soon", pax: 5, cls: "Compact Car"),
        V("m1", "ARO RIDE 150", "motorcycle", 9000, "coming_soon", cls: "Motorcycle"),
        V("v1", "ARO CARGO VAN", "van", 38000, "coming_soon", cargo: 1200, cls: "Delivery Van"),
    };

    // ---- tabs: one catalogue, many views
    [Fact] public void AllShowsEveryVehicle() => Assert.Equal(7, VehicleRules.Filter(Catalog(), VehicleTab.All, null).Count);
    [Fact] public void TrucksOnlyTrucks() => Assert.Equal(new[] { "t1", "t2" }, VehicleRules.Filter(Catalog(), VehicleTab.Trucks, null).Select(v => v.Id));
    [Fact] public void BusesOnlyBuses() => Assert.Equal(new[] { "b1", "b2" }, VehicleRules.Filter(Catalog(), VehicleTab.Buses, null).Select(v => v.Id));
    [Fact] public void LightVehiclesAreCarsVansAndMotorcycles() => Assert.Equal(new[] { "c1", "m1", "v1" }, VehicleRules.Filter(Catalog(), VehicleTab.Light, null).Select(v => v.Id));
    [Fact] public void OwnedOnlyWhatThePlayerOwns() => Assert.Equal(new[] { "t2" }, VehicleRules.Filter(Catalog(), VehicleTab.Owned, null).Select(v => v.Id));
    [Fact] public void ShopIsEverythingNotYetOwned() => Assert.DoesNotContain("t2", VehicleRules.Filter(Catalog(), VehicleTab.Shop, null).Select(v => v.Id));
    [Fact] public void TabsPartitionTheCatalogue() => Assert.Equal(7, new[] { VehicleTab.Trucks, VehicleTab.Buses, VehicleTab.Light }.Sum(t => VehicleRules.Filter(Catalog(), t, null).Count));
    [Fact] public void FilteringNeverChangesTheSource() { var c = Catalog(); VehicleRules.Filter(c, VehicleTab.Buses, "city", VehicleSort.PriceHigh); Assert.Equal(7, c.Count); Assert.Equal("t1", c[0].Id); }
    [Fact] public void NullCatalogueIsEmptyNotAnError() => Assert.Empty(VehicleRules.Filter(null, VehicleTab.All, "x"));

    // ---- search
    [Fact] public void SearchIsCaseInsensitive() => Assert.Equal(new[] { "t1" }, VehicleRules.Filter(Catalog(), VehicleTab.All, "tItAn").Select(v => v.Id));
    [Fact] public void SearchBusFindsBuses() => Assert.Equal(new[] { "b1", "b2" }, VehicleRules.Filter(Catalog(), VehicleTab.All, "bus").Select(v => v.Id));
    [Fact] public void SearchByClass() => Assert.Equal(new[] { "t2" }, VehicleRules.Filter(Catalog(), VehicleTab.All, "light truck").Select(v => v.Id));
    [Fact] public void SearchByManufacturerFindsAllAro() => Assert.Equal(7, VehicleRules.Filter(Catalog(), VehicleTab.All, "aro motors").Count);
    [Fact] public void SearchBikeFindsTheMotorcycle() => Assert.Equal(new[] { "m1" }, VehicleRules.Filter(Catalog(), VehicleTab.All, "bike").Select(v => v.Id));
    [Fact] public void EveryWordMustMatch() => Assert.Empty(VehicleRules.Filter(Catalog(), VehicleTab.All, "titan bus"));
    [Fact] public void EmptySearchKeepsEverything() { Assert.Equal(7, VehicleRules.Filter(Catalog(), VehicleTab.All, "").Count); Assert.Equal(7, VehicleRules.Filter(Catalog(), VehicleTab.All, "   ").Count); }
    [Fact] public void SearchCombinesWithTheTab() => Assert.Empty(VehicleRules.Filter(Catalog(), VehicleTab.Buses, "titan"));
    [Fact] public void SearchTextIsNotARegex() => Assert.Empty(VehicleRules.Filter(Catalog(), VehicleTab.All, ".*"));

    // ---- sort
    [Fact] public void PriceLowFirst() => Assert.Equal("t2", VehicleRules.Filter(Catalog(), VehicleTab.All, null, VehicleSort.PriceLow).First().Id);
    [Fact] public void PriceHighFirst() => Assert.Equal("t1", VehicleRules.Filter(Catalog(), VehicleTab.All, null, VehicleSort.PriceHigh).First().Id);
    [Fact] public void PowerPutsVehiclesWithoutPowerLast() { var l = VehicleRules.Filter(Catalog(), VehicleTab.All, null, VehicleSort.Power); Assert.Equal("t1", l.First().Id); Assert.Null(l.Last().Hp); }
    [Fact] public void CapacityMixesKilogramsAndSeatsByTheirOwnNumber() => Assert.Equal("t1", VehicleRules.Filter(Catalog(), VehicleTab.All, null, VehicleSort.Capacity).First().Id);
    [Fact] public void LevelLowestFirst() => Assert.Equal("b2", VehicleRules.Filter(Catalog(), VehicleTab.All, null, VehicleSort.Level).Last().Id);
    [Fact] public void SortIsStableByName() { var l = VehicleRules.Filter(Catalog(), VehicleTab.Light, null, VehicleSort.Level).Select(v => v.Name).ToList(); Assert.Equal(l.OrderBy(x => x, System.StringComparer.Ordinal), l); }

    // ---- actions follow the server's purchase state
    [Fact] public void OwnedCanBeSelected() { var a = VehicleRules.ActionFor(V("x", "X", "truck", state: "owned", owned: 1), false); Assert.Equal(VehicleActionKind.Select, a.Kind); Assert.True(a.Enabled); Assert.Equal("Select Vehicle", a.Label); }
    [Fact] public void TheActiveVehicleIsMarkedNotSelectable() { var a = VehicleRules.ActionFor(V("x", "X", "truck", state: "owned", owned: 1), true); Assert.Equal(VehicleActionKind.Selected, a.Kind); Assert.False(a.Enabled); }
    [Fact] public void AvailableCanBeBoughtAtTheServerPrice() { var a = VehicleRules.ActionFor(V("x", "X", "bus", 45000), false); Assert.Equal(VehicleActionKind.Buy, a.Kind); Assert.True(a.Enabled); Assert.Equal("Buy ₦45,000", a.Label); }
    [Fact] public void ComingSoonCannotBeBought() { var a = VehicleRules.ActionFor(V("x", "X", "truck", state: "coming_soon"), false); Assert.Equal(VehicleActionKind.ComingSoon, a.Kind); Assert.False(a.Enabled); }
    [Fact] public void LevelRequiredNamesTheLevel() { var a = VehicleRules.ActionFor(V("x", "X", "truck", state: "level_required", level: 10), false); Assert.Equal("Requires Level 10", a.Label); Assert.False(a.Enabled); }
    [Fact] public void InsufficientFundsSaysHowMuchIsMissing() { var v = V("x", "X", "truck", state: "insufficient_funds"); v.NeedMore = 20000; var a = VehicleRules.ActionFor(v, false); Assert.Equal("Need ₦20,000 more", a.Label); Assert.False(a.Enabled); }
    [Fact] public void NotForSaleIsDisabled() => Assert.False(VehicleRules.ActionFor(V("x", "X", "truck", state: "not_for_sale"), false).Enabled);
    [Fact] public void AnUnknownStateNeverEnablesAPurchaseByAccident() => Assert.Equal(VehicleActionKind.Buy, VehicleRules.ActionFor(V("x", "X", "truck", state: "available"), false).Kind);
    [Fact] public void CardBadges()
    {
        Assert.Equal("OWNED", VehicleRules.CardBadge(V("x", "X", "truck", state: "owned", owned: 1), false));
        Assert.Equal("ACTIVE", VehicleRules.CardBadge(V("x", "X", "truck", state: "owned", owned: 1), true));
        Assert.Equal("COMING SOON", VehicleRules.CardBadge(V("x", "X", "truck", state: "coming_soon"), false));
        Assert.Equal("LEVEL 8", VehicleRules.CardBadge(V("x", "X", "truck", state: "level_required", level: 8), false));
        Assert.Equal("₦45,000", VehicleRules.CardBadge(V("x", "X", "truck", 45000), false));
    }

    // ---- condition and fuel
    [Theory] [InlineData(100, ConditionState.Ready)] [InlineData(70, ConditionState.Ready)] [InlineData(69.9, ConditionState.MaintenanceRecommended)] [InlineData(42, ConditionState.MaintenanceRecommended)]
    [InlineData(29.9, ConditionState.RepairRequired)] [InlineData(12, ConditionState.RepairRequired)] [InlineData(10, ConditionState.Critical)] [InlineData(0, ConditionState.Critical)]
    public void ConditionBands(double pct, ConditionState want) => Assert.Equal(want, VehicleRules.ConditionOf(pct));
    [Fact] public void ConditionWordsMatchTheBands() { Assert.Equal("Ready", VehicleRules.ConditionText(98)); Assert.Equal("Maintenance recommended", VehicleRules.ConditionText(42)); Assert.Equal("Repair required", VehicleRules.ConditionText(12)); }
    [Theory] [InlineData(78, FuelState.Healthy)] [InlineData(25, FuelState.Healthy)] [InlineData(24.9, FuelState.Low)] [InlineData(10, FuelState.Low)] [InlineData(9.9, FuelState.Critical)] [InlineData(0, FuelState.Critical)]
    public void FuelBands(double pct, FuelState want) => Assert.Equal(want, VehicleRules.FuelOf(pct));

    // ---- bars
    [Fact] public void BarsAreRelativeToTheCatalogueMaximum() { Assert.Equal(1.0, VehicleRules.Fraction(480, 480)); Assert.Equal(0.5, VehicleRules.Fraction(240, 480)); }
    [Fact] public void BarsAreClampedAndSafe() { Assert.Equal(1.0, VehicleRules.Fraction(900, 480)); Assert.Equal(0.0, VehicleRules.Fraction(-5, 480)); Assert.Equal(0.0, VehicleRules.Fraction(10, 0)); }

    // ---- capacity depends on the kind of vehicle
    [Fact] public void BusesCarryPassengersTrucksCarryCargo()
    {
        Assert.Equal("Passengers", VehicleRules.CapacityLabel(V("b", "B", "bus", pax: 40))); Assert.Equal("40 seats", VehicleRules.CapacityText(V("b", "B", "bus", pax: 40)));
        Assert.Equal("Cargo capacity", VehicleRules.CapacityLabel(V("t", "T", "truck", cargo: 4000))); Assert.Equal("4,000 kg", VehicleRules.CapacityText(V("t", "T", "truck", cargo: 4000)));
    }
    [Fact] public void ACarWithSeatsIsMeasuredInSeats() => Assert.Equal("5 seats", VehicleRules.CapacityText(V("c", "C", "car", pax: 5)));
    [Fact] public void ACarWithoutSeatsOrLoadSaysNone() => Assert.Equal("None", VehicleRules.CapacityText(V("m", "M", "motorcycle")));

    // ---- comparison
    [Fact] public void CompareNeedsTwoToFour()
    {
        var c = Catalog();
        VehicleRules.Compare(new[] { c[0] }, out var e1); Assert.NotNull(e1);
        VehicleRules.Compare(c.Take(5).ToList(), out var e2); Assert.NotNull(e2);
        var rows = VehicleRules.Compare(c.Take(2).ToList(), out var e3); Assert.Null(e3); Assert.NotEmpty(rows);
        VehicleRules.Compare(c.Take(4).ToList(), out var e4); Assert.Null(e4);
    }
    [Fact] public void CompareRefusesTheSameVehicleTwice() { var c = Catalog(); VehicleRules.Compare(new[] { c[0], c[0] }, out var e); Assert.NotNull(e); }
    [Fact] public void CompareHighlightsTheBetterValue()
    {
        var c = Catalog(); var rows = VehicleRules.Compare(new[] { c[0], c[1] }, out _);
        Assert.Equal(new[] { 1 }, rows.First(r => r.Label == "Price").Best);        // the cheaper (free starter) is better
        Assert.Equal(new[] { 0 }, rows.First(r => r.Label == "Torque").Best);       // the stronger is better
        Assert.Equal(new[] { 0 }, rows.First(r => r.Label == "Cargo capacity").Best);
    }
    [Fact] public void CompareNeverCallsEqualValuesBetter() { var a = V("a", "A", "truck", 100, speed: 100); var b = V("b", "B", "truck", 100, speed: 100); Assert.Empty(VehicleRules.Compare(new[] { a, b }, out _).First(r => r.Label == "Top speed").Best); }
    [Fact] public void CompareMissingValuesAreNotApplicableNotZero()
    {
        var c = Catalog(); var rows = VehicleRules.Compare(new[] { c[1], c[2] }, out _);     // a truck and a bus, neither with a power figure
        Assert.Equal("n/a", rows.First(r => r.Label == "Power").Cells[0]); Assert.Empty(rows.First(r => r.Label == "Power").Best);
    }
    [Fact] public void CompareTruckAndBusUseTheirOwnCapacityRows()
    {
        var c = Catalog(); var rows = VehicleRules.Compare(new[] { c[1], c[2] }, out _);
        var cargo = rows.First(r => r.Label == "Cargo capacity"); var pax = rows.First(r => r.Label == "Passengers");
        Assert.Equal("4,000 kg", cargo.Cells[0]); Assert.Equal("n/a", cargo.Cells[1]);
        Assert.Equal("n/a", pax.Cells[0]); Assert.Equal("40 seats", pax.Cells[1]);
    }
    [Fact] public void CompareTwoTrucksHasNoPassengerRow() => Assert.DoesNotContain(VehicleRules.Compare(new[] { Catalog()[0], Catalog()[1] }, out _), r => r.Label == "Passengers");
    [Fact] public void CompareTwoBusesHasNoCargoRow() => Assert.DoesNotContain(VehicleRules.Compare(new[] { Catalog()[2], Catalog()[3] }, out _), r => r.Label == "Cargo capacity");
    [Fact] public void CompareWeightIsShownButNeverRanked() { var rows = VehicleRules.Compare(new[] { Catalog()[1], Catalog()[0] }, out _); Assert.Empty(rows.First(r => r.Label == "Weight").Best); }
    [Fact] public void EveryCompareRowHasOneCellPerVehicle() { var rows = VehicleRules.Compare(Catalog().Take(4).ToList(), out _); Assert.All(rows, r => Assert.Equal(4, r.Cells.Length)); }

    // ---- job fit sentences
    [Fact] public void JobFitUnknown() => Assert.Contains("not available", VehicleRules.JobFitText(false, true, false, null, 0, 0, "truck"));
    [Fact] public void JobFitNotDrivable() => Assert.Contains("Not drivable yet", VehicleRules.JobFitText(true, false, true, null, 2, 2, "truck"));
    [Fact] public void JobFitCompatibleCounts() { Assert.Equal("Can take 2 of the 3 open jobs.", VehicleRules.JobFitText(true, true, true, null, 2, 3, "truck")); Assert.Equal("Can take 1 of the 3 open jobs.", VehicleRules.JobFitText(true, true, true, null, 1, 3, "truck")); }
    [Fact] public void JobFitMismatchExplainsWhy() => Assert.Contains("cannot carry", VehicleRules.JobFitText(true, true, false, "category_mismatch", 0, 3, "car"));
    [Fact] public void JobFitNoJobsIsItsOwnMessage() => Assert.Equal("No jobs are open right now.", VehicleRules.JobFitText(true, true, false, "no_open_jobs", 0, 0, "truck"));

    // ---- showroom capability is honest
    [Fact] public void TheShowroomHasNoThreeDYet() { var s = new VehicleRules.Showroom { ViewPictures = 3 }; Assert.False(s.Has3D); Assert.False(s.CanRotate); Assert.False(string.IsNullOrEmpty(s.Unavailable3DReason)); }
}
