using System.Linq;
using ARO.NetCore;
using Xunit;

namespace NetCore.Tests
{
    public class BusRoutesModelTests
    {
        static BusChoice B(string id, double dmg = 0, double fuel = 100, bool sel = false) => new BusChoice { Id = id, Name = id, DamagePct = dmg, FuelL = fuel, Selected = sel, Seats = 40 };

        [Fact] public void DemandIsTheAverageWhereAndWhenPeopleBoard()
        {
            Assert.Equal(35.0, BusRouteRules.AverageDemand(new[] { 30, 40, 0 }));            // the terminus is left out: nobody boards there
            Assert.Equal(30.0, BusRouteRules.AverageDemand(new[] { 30 }));
            Assert.Equal(0.0, BusRouteRules.AverageDemand(new int[0])); Assert.Equal(0.0, BusRouteRules.AverageDemand(null));
            Assert.Equal(60.0, BusRouteRules.AverageDemand(new[] { 80, 40, 0 }));
        }
        [Theory] [InlineData(0, DemandLevel.Low)] [InlineData(19.9, DemandLevel.Low)] [InlineData(20, DemandLevel.Medium)] [InlineData(39.9, DemandLevel.Medium)] [InlineData(40, DemandLevel.High)] [InlineData(80, DemandLevel.High)]
        public void DemandLevels(double avg, DemandLevel level) => Assert.Equal(level, BusRouteRules.LevelOf(avg));
        [Fact] public void DemandWordsAndFractions()
        {
            Assert.Equal(new[] { "Low", "Medium", "High" }, new[] { DemandLevel.Low, DemandLevel.Medium, DemandLevel.High }.Select(BusRouteRules.LevelText));
            Assert.Equal(0.5f, BusRouteRules.DemandFraction(40)); Assert.Equal(0f, BusRouteRules.DemandFraction(-5)); Assert.Equal(1f, BusRouteRules.DemandFraction(500)); Assert.Equal(0f, BusRouteRules.DemandFraction(0));
        }
        [Fact] public void TextsAreFormattedTheSameEverywhere()
        {
            Assert.Equal("1 stop", BusRouteRules.StopsText(1)); Assert.Equal("3 stops", BusRouteRules.StopsText(3)); Assert.Equal("0 stops", BusRouteRules.StopsText(0));
            Assert.Equal("₦ 15", BusRouteRules.FareText(15)); Assert.Equal("₦ 1,200", BusRouteRules.FareText(1200)); Assert.Equal("₦ 0", BusRouteRules.FareText(-3));
            Assert.Equal("+150 XP", BusRouteRules.XpText(150)); Assert.Equal("+1,200 XP", BusRouteRules.XpText(1200)); Assert.Equal("+0 XP", BusRouteRules.XpText(-1));
            Assert.Equal("₦ 4,200", BusRouteRules.Money(4200));
            Assert.Equal("A → B → C", BusRouteRules.Summary(new[] { "A", "B", "C" })); Assert.Equal("", BusRouteRules.Summary(null)); Assert.Equal("A, B", BusRouteRules.Summary(new[] { "A", "B" }, ", "));
        }
        [Fact] public void WhatHappensAtAStop()
        {
            Assert.Equal("Start of the route", BusRouteRules.AlightText(0, true, false)); Assert.Equal("50% get off here", BusRouteRules.AlightText(50, false, false));
            Assert.Equal("Everyone gets off", BusRouteRules.AlightText(100, false, true)); Assert.Equal("Everyone gets off", BusRouteRules.AlightText(40, false, true));
            Assert.Equal("0% get off here", BusRouteRules.AlightText(0, false, false)); Assert.Equal("100% get off here", BusRouteRules.AlightText(140, false, false)); Assert.Equal("10% get off here", BusRouteRules.AlightText(10, true, false));
        }
        [Fact] public void StopsAreCompletedCurrentOrUpcoming()
        {
            Assert.Equal(new[] { StopProgress.Completed, StopProgress.Current, StopProgress.Upcoming, StopProgress.Upcoming }, Enumerable.Range(0, 4).Select(i => BusRouteRules.StopState(i, 1)));
            Assert.Equal(StopProgress.Current, BusRouteRules.StopState(0, 0)); Assert.Equal(StopProgress.Completed, BusRouteRules.StopState(2, 3));
            Assert.Equal(new[] { "Completed", "Current stop", "Upcoming" }, new[] { StopProgress.Completed, StopProgress.Current, StopProgress.Upcoming }.Select(BusRouteRules.StopStateText));
        }
        [Fact] public void ProgressNumbers()
        {
            Assert.Equal(28 / 45f, BusRouteRules.AboardFraction(28, 45), 4); Assert.Equal(0f, BusRouteRules.AboardFraction(5, 0)); Assert.Equal(1f, BusRouteRules.AboardFraction(60, 45)); Assert.Equal(0f, BusRouteRules.AboardFraction(-3, 45));
            Assert.Equal("28 / 45", BusRouteRules.AboardText(28, 45)); Assert.Equal("28", BusRouteRules.AboardText(28, 0));
            Assert.Equal(0f, BusRouteRules.LineFill(4, 0)); Assert.Equal(1f / 3f, BusRouteRules.LineFill(4, 1), 4); Assert.Equal(1f, BusRouteRules.LineFill(4, 3)); Assert.Equal(1f, BusRouteRules.LineFill(4, 9)); Assert.Equal(0f, BusRouteRules.LineFill(1, 0)); Assert.Equal(0f, BusRouteRules.LineFill(0, 0));
        }
        [Fact] public void ConditionIsWhatIsLeftAfterDamage()
        {
            Assert.Equal(92, BusRouteRules.Condition(8)); Assert.Equal(100, BusRouteRules.Condition(0)); Assert.Equal(0, BusRouteRules.Condition(100)); Assert.Equal(0, BusRouteRules.Condition(130)); Assert.Equal(100, BusRouteRules.Condition(-5));
            Assert.Equal("Condition: 92%", BusRouteRules.ConditionText(8)); Assert.Equal("45 seats", BusRouteRules.SeatsText(45)); Assert.Equal("", BusRouteRules.SeatsText(0));
        }
        [Fact] public void TheSelectedBusIsUsedWhenItCanRun()
        {
            Assert.Equal("b", BusRouteRules.PickBus(new[] { B("a", 5), B("b", 30, 50, true), B("c", 0) }).Id);
            Assert.Equal("c", BusRouteRules.PickBus(new[] { B("a", 5), B("b", 100, 50, true), B("c", 0) }).Id);          // the selected one is wrecked: the best other bus
            Assert.Equal("b", BusRouteRules.PickBus(new[] { B("a", 5, 10), B("b", 5, 90, false) }).Id);                      // equal damage: more fuel wins
            Assert.Equal("c", BusRouteRules.PickBus(new[] { B("a", 40), B("b", 20, 0), B("c", 30) }).Id);                     // b has no fuel; of a and c the less damaged is c
        }
        [Fact] public void WhenNoBusCanRunTheProblemIsStillShown()
        {
            var wrecked = B("w", 100, 50, true); Assert.Same(wrecked, BusRouteRules.PickBus(new[] { wrecked, B("x", 100) })); Assert.Contains("too damaged", BusRouteRules.ProblemWith(wrecked));
            Assert.Contains("out of fuel", BusRouteRules.ProblemWith(B("e", 0, 0))); Assert.Null(BusRouteRules.ProblemWith(B("ok")));
            Assert.Null(BusRouteRules.PickBus(null)); Assert.Null(BusRouteRules.PickBus(new BusChoice[0])); Assert.Contains("need a bus", BusRouteRules.ProblemWith(null));
        }
        [Fact] public void StartRouteIsBlockedForTheSameReasonsTheServerRefuses()
        {
            Assert.Null(BusRouteRules.StartBlockedReason(B("a"), false, false, true));
            Assert.Contains("in progress", BusRouteRules.StartBlockedReason(B("a"), false, true, true)); Assert.Contains("Choose a route", BusRouteRules.StartBlockedReason(B("a"), false, false, false));
            Assert.Contains("need a bus", BusRouteRules.StartBlockedReason(null, false, false, true)); Assert.Contains("freight job", BusRouteRules.StartBlockedReason(B("a"), true, false, true));
            Assert.Contains("too damaged", BusRouteRules.StartBlockedReason(B("a", 100), false, false, true)); Assert.Contains("out of fuel", BusRouteRules.StartBlockedReason(B("a", 0, 0), false, false, true));
        }
    }
}
