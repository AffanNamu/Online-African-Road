using ARO.Backend;
using NUnit.Framework;
using UnityEngine;

namespace ARO.Tests
{
    /// <summary>Payloads copied from what Postgres/PostgREST actually returns for the bus RPCs (see supabase/tests/25_bus_and_shop.sql).</summary>
    public class BusDtoTests
    {
        [Test] public void ServeStopMidRouteReplyParses()
        {
            var r = JsonUtility.FromJson<ServeStopResult>("{\"fare\": 180, \"aboard\": 38, \"boarded\": 30, \"alighted\": 12, \"next_seq\": 3, \"completed\": false}");
            Assert.IsFalse(r.completed); Assert.AreEqual(12, r.alighted); Assert.AreEqual(30, r.boarded);
            Assert.AreEqual(38, r.aboard); Assert.AreEqual(180L, r.fare); Assert.AreEqual(3, r.next_seq);
        }

        [Test] public void ServeStopFinalReplyParses()
        {
            var r = JsonUtility.FromJson<ServeStopResult>("{\"xp\": 150, \"fare\": 300, \"level\": 2, \"balance\": 51200, \"revenue\": 735, \"alighted\": 20, \"completed\": true, \"passengers_carried\": 53}");
            Assert.IsTrue(r.completed); Assert.AreEqual(735L, r.revenue); Assert.AreEqual(53, r.passengers_carried);
            Assert.AreEqual(150, r.xp); Assert.AreEqual(2, r.level); Assert.AreEqual(51200L, r.balance);
        }

        [Test] public void RouteWithEmbeddedStopsParses()
        {
            const string json = "{\"items\":[{\"id\":\"r1\",\"code\":\"LAG-R1\",\"name\":\"Mile 12 - Oshodi - Ikeja\",\"fare\":15,\"xp_reward\":150," +
                "\"stops\":[{\"seq\":2,\"demand\":40,\"alight_pct\":50,\"location\":{\"slug\":\"lagos-oshodi-terminal\",\"name\":\"Oshodi Terminal\",\"world_x\":3400,\"world_z\":800}}," +
                "{\"seq\":1,\"demand\":30,\"alight_pct\":0,\"location\":{\"slug\":\"lagos-mile12-market\",\"name\":\"Mile 12 Market\",\"world_x\":2600,\"world_z\":-900}}]}]}";
            var w = JsonUtility.FromJson<ListWrapper<BusRouteDto>>(json);
            Assert.AreEqual(1, w.items.Length);
            var route = w.items[0];
            Assert.AreEqual("LAG-R1", route.code); Assert.AreEqual(15, route.fare); Assert.AreEqual(2, route.stops.Length);
            Assert.AreEqual("Oshodi Terminal", route.stops[0].location.name);   // unsorted as delivered: BusService sorts by seq
            Assert.AreEqual(-900.0, route.stops[1].location.world_z, 0.001);
        }

        [Test] public void VehicleDefinitionCarriesPrice()
        {
            var w = JsonUtility.FromJson<ListWrapper<VehicleDefDto>>("{\"items\":[{\"id\":\"bus_city_01\",\"category\":\"bus\",\"name\":\"Danfo City Bus\",\"cargo_capacity_kg\":0,\"passenger_capacity\":40,\"fuel_capacity_l\":180,\"max_speed_kmh\":90,\"price\":45000}]}");
            Assert.AreEqual(45000L, w.items[0].price); Assert.AreEqual(40, w.items[0].passenger_capacity);
        }

        [TestCase("not_at_stop")] [TestCase("stop_too_soon")] [TestCase("run_active")] [TestCase("bus_run_active")] [TestCase("job_active")]
        [TestCase("insufficient_funds")] [TestCase("not_for_sale")] [TestCase("run_not_active")]
        public void EveryBusErrorCodeHasAPlayerMessage(string code)
        {
            string msg = ErrorMessages.ToUser(code, 400);
            Assert.IsFalse(msg.StartsWith("Something went wrong"), code + " has no friendly message");
        }
    }
}
