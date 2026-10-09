using ARO.Backend;
using ARO.Vehicles;
using ARO.World;
using NUnit.Framework;
using UnityEngine;

namespace ARO.Tests
{
    public class RouteDefinitionTests
    {
        static RouteDefinition Straight()
        {
            var r = ScriptableObject.CreateInstance<RouteDefinition>();
            RouteNode N(float x, ZoneType z = ZoneType.Highway, RoadSurface s = RoadSurface.Good) =>
                new RouteNode { position = new Vector3(x, 0, 0), width = 10, zone = z, surface = s };
            r.nodes = new[] { N(0), N(1000), N(3000) };
            r.Rebuild();
            return r;
        }

        [Test] public void TotalLengthIsSumOfSegments() => Assert.AreEqual(3000f, Straight().TotalLength, 0.01f);

        [Test] public void PositionAndTangentFollowThePolyline()
        {
            var r = Straight();
            Assert.AreEqual(500f, r.PositionAt(500f).x, 0.01f);
            Assert.AreEqual(2000f, r.PositionAt(2000f).x, 0.01f);
            Assert.AreEqual(1f, r.TangentAt(2000f).x, 0.001f);
        }

        [Test] public void PositionClampsOutsideTheRoute()
        {
            var r = Straight();
            Assert.AreEqual(0f, r.PositionAt(-50f).x, 0.01f);
            Assert.AreEqual(3000f, r.PositionAt(9999f).x, 0.01f);
        }

        [Test] public void ProjectReturnsDistanceAndSignedLateralOffset()
        {
            var r = Straight();
            float s = r.Project(new Vector3(1200f, 0, -4f), out float lateral);   // 4 m to the +right side of a +x road
            Assert.AreEqual(1200f, s, 0.5f);
            Assert.AreEqual(Mathf.Sign(Vector3.Dot(new Vector3(0, 0, -4f), Vector3.Cross(Vector3.up, Vector3.right))), Mathf.Sign(lateral));
            Assert.AreEqual(4f, Mathf.Abs(lateral), 0.01f);
        }

        [Test] public void SegmentAtReturnsCorrectIndex()
        {
            var r = Straight();
            Assert.AreEqual(0, r.SegmentAt(10f, out _));
            Assert.AreEqual(1, r.SegmentAt(1500f, out float t));
            Assert.AreEqual(0.25f, t, 0.001f);
        }
    }

    public class TrafficLimitTests
    {
        static RouteNode Node(ZoneType z, RoadSurface s) => new RouteNode { zone = z, surface = s };

        [Test] public void DamagedRoadsAreSlowerThanGoodOnes() =>
            Assert.Less(TrafficManager.LimitFor(Node(ZoneType.Rural, RoadSurface.Damaged)), TrafficManager.LimitFor(Node(ZoneType.Rural, RoadSurface.Good)));

        [Test] public void HighwayIsFasterThanUrbanAndCommercial()
        {
            float hw = TrafficManager.LimitFor(Node(ZoneType.Highway, RoadSurface.Good));
            Assert.Greater(hw, TrafficManager.LimitFor(Node(ZoneType.Urban, RoadSurface.Good)));
            Assert.Greater(TrafficManager.LimitFor(Node(ZoneType.Urban, RoadSurface.Good)), TrafficManager.LimitFor(Node(ZoneType.Commercial, RoadSurface.Good)));
        }
    }

    public class BackendTests
    {
        [Test] public void KnownServerCodesMapToFriendlyMessages()
        {
            StringAssert.Contains("faster", ErrorMessages.ToUser("delivery_too_fast", 400));
            StringAssert.Contains("Not enough", ErrorMessages.ToUser("insufficient_funds", 400));
        }

        [Test] public void UnknownCodesFallBackByHttpStatus()
        {
            StringAssert.Contains("sign in", ErrorMessages.ToUser("whatever", 401).ToLower());
            StringAssert.Contains("Server problem", ErrorMessages.ToUser("whatever", 503));
            StringAssert.Contains("whatever", ErrorMessages.ToUser("whatever", 400));
        }

        [Test] public void ConfigIsNotConfiguredByDefault()
        {
            var c = ScriptableObject.CreateInstance<BackendConfig>();
            Assert.IsFalse(c.IsConfigured);
            c.supabaseUrl = "https://abc.supabase.co"; c.anonKey = "k";
            Assert.IsTrue(c.IsConfigured);
        }
    }

    public class VehicleDefinitionTests
    {
        [Test] public void BackendStatsOverlayOnlyTouchesProvidedFields()
        {
            var d = ScriptableObject.CreateInstance<VehicleDefinition>();
            d.stats.maxSteerDeg = 30f;
            d.ApplyBackendStats("{\"massKg\":8500,\"torqueNm\":1400,\"fuelBurnLPerKm\":0.32}");
            Assert.AreEqual(8500f, d.stats.massKg);
            Assert.AreEqual(1400f, d.stats.torqueNm);
            Assert.AreEqual(0.32f, d.stats.fuelBurnLPerKm, 1e-4f);
            Assert.AreEqual(30f, d.stats.maxSteerDeg);   // untouched
        }

        [Test] public void EmptyOrNullStatsAreIgnored()
        {
            var d = ScriptableObject.CreateInstance<VehicleDefinition>();
            float m = d.stats.massKg;
            d.ApplyBackendStats(null); d.ApplyBackendStats("");
            Assert.AreEqual(m, d.stats.massKg);
        }
    }
}
