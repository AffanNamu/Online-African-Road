using ARO.NetCore;
using Xunit;

namespace NetCore.Tests
{
    public class BusRulesTests
    {
        static readonly StopPoint[] Route =
        {
            new StopPoint("Mile 12", 2600, -900), new StopPoint("Oshodi", 3400, 800), new StopPoint("Ikeja", 4150, 2450),
        };

        [Theory]
        [InlineData(0f, 0f, true)]
        [InlineData(45f, 5.9f, true)]
        [InlineData(45.1f, 0f, false)]      // just outside our radius
        [InlineData(20f, 6f, false)]        // still rolling
        [InlineData(20f, 30f, false)]
        public void CanServe_requires_closeness_and_standstill(float dist, float kmh, bool expected) =>
            Assert.Equal(expected, BusRules.CanServe(dist, kmh));

        [Fact] public void CanServe_rejects_nan() { Assert.False(BusRules.CanServe(float.NaN, 0)); Assert.False(BusRules.CanServe(0, float.NaN)); }

        [Fact] public void Client_radius_is_tighter_than_server_radius_of_60() => Assert.True(BusRules.ServeRadius < 60f);

        [Fact] public void RouteLength_sums_legs()
        {
            float expected = (float)(System.Math.Sqrt(800 * 800 + 1700 * 1700) + System.Math.Sqrt(750 * 750 + 1650 * 1650));
            Assert.Equal(expected, BusRules.RouteLength(Route), 1);
        }

        [Fact] public void Progress_is_zero_before_first_stop_is_served() => Assert.Equal(0f, BusRules.Progress(Route, 0, 2600, -900));

        [Fact] public void Progress_is_one_after_last_stop() => Assert.Equal(1f, BusRules.Progress(Route, 3, 4150, 2450));

        [Fact] public void Progress_at_second_stop_equals_first_leg_share()
        {
            float leg1 = BusRules.Distance(2600, -900, 3400, 800), total = BusRules.RouteLength(Route);
            Assert.Equal(leg1 / total, BusRules.Progress(Route, 1, 3400, 800), 3);
        }

        [Fact] public void Progress_midway_through_a_leg_is_between_stops()
        {
            float start = BusRules.Progress(Route, 1, 2600, -900), mid = BusRules.Progress(Route, 1, 3000, -50), end = BusRules.Progress(Route, 1, 3400, 800);
            Assert.True(start < mid && mid < end);
            Assert.Equal(0f, start, 3);
        }

        [Fact] public void Progress_does_not_go_negative_when_driving_away()
        {
            Assert.Equal(0f, BusRules.Progress(Route, 1, 0, 0), 3);   // far behind the previous stop
        }

        [Fact] public void Progress_degenerate_routes_are_zero()
        {
            Assert.Equal(0f, BusRules.Progress(new StopPoint[0], 0, 0, 0));
            Assert.Equal(0f, BusRules.Progress(new[] { new StopPoint("A", 0, 0) }, 1, 0, 0));
        }

        [Fact] public void Prompt_guides_player_by_distance_and_speed()
        {
            Assert.Equal("", BusRules.Prompt("Oshodi", false, false, 500f, 40f));
            Assert.Equal("Stop at Oshodi to pick up and drop off passengers", BusRules.Prompt("Oshodi", false, false, 100f, 40f));
            Assert.Equal("Stop at Mile 12 to board passengers", BusRules.Prompt("Mile 12", false, true, 100f, 40f));
            Assert.Equal("Stop at Ikeja to let everyone off", BusRules.Prompt("Ikeja", true, false, 100f, 40f));
            Assert.Equal("Slow down to stop at Oshodi", BusRules.Prompt("Oshodi", false, false, 20f, 40f));
            Assert.Equal("Serving Oshodi...", BusRules.Prompt("Oshodi", false, false, 20f, 2f));
        }

        [Fact] public void StopSummary_describes_both_directions_and_fares()
        {
            Assert.Equal("Oshodi: 12 got off, 30 boarded  (38 aboard)  +180 coins", BusRules.StopSummary("Oshodi", 12, 30, 38, 180));
            Assert.Equal("Mile 12: 25 boarded  (25 aboard)", BusRules.StopSummary("Mile 12", 0, 25, 25, 0));
            Assert.Equal("Quiet: nobody waiting  (4 aboard)", BusRules.StopSummary("Quiet", 0, 0, 4, 0));
            Assert.Equal("Ikeja: 38 got off  (0 aboard)  +1,234 coins", BusRules.StopSummary("Ikeja", 38, 0, 0, 1234));
        }

        [Fact] public void RunState_accumulates_only_what_the_server_reports()
        {
            var s = new BusRunState();
            s.Apply(25, 0); s.Apply(38, 180);
            Assert.Equal(2, s.NextIndex); Assert.Equal(38, s.Aboard); Assert.Equal(180, s.Revenue); Assert.Equal(2, s.StopsServed);
            s.Reset();
            Assert.Equal(0, s.NextIndex); Assert.Equal(0, s.Aboard); Assert.Equal(0, s.Revenue);
        }

        [Fact] public void RunState_ignores_negative_values() { var s = new BusRunState(); s.Apply(-5, -10); Assert.Equal(0, s.Aboard); Assert.Equal(0, s.Revenue); }
    }
}
