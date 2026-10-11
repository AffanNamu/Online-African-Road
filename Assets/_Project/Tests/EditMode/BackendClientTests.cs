using System.IO;
using System.Linq;
using ARO.Backend;
using ARO.Jobs;
using ARO.NetCore;
using NUnit.Framework;
using UnityEngine;

namespace ARO.Tests
{
    /// <summary>
    /// The backend client against REAL server output. tools/check_api_contract.py runs every RPC on a real database and writes the replies to
    /// ApiSamples/; these tests parse them with Unity's real JsonUtility (which has quirks the .NET tests cannot see: nulls, int/long, nesting).
    /// </summary>
    public class BackendClientTests
    {
        static string Sample(string name) => File.ReadAllText(Path.Combine(Application.dataPath, "_Project/Tests/EditMode/ApiSamples", name + ".json"));
        static T Parse<T>(string name) where T : class { var v = JsonUtility.FromJson<T>(Sample(name)); Assert.IsNotNull(v, name); return v; }

        [Test] public void DashboardSummaryParsesWithEveryBlock()
        {
            var s = Parse<DashboardSummary>("get_dashboard_summary");
            Assert.AreEqual("CtA", s.profile.display_name); Assert.AreEqual("cta", s.profile.username); Assert.AreEqual(2, s.progression.level);
            Assert.AreEqual(300L, s.progression.experience); Assert.AreEqual(100L, s.progression.xp_level_start); Assert.AreEqual(400L, s.progression.xp_next_level);
            Assert.AreEqual(5000L, s.coins); Assert.IsTrue(s.vehicle.Exists); Assert.AreEqual("truck_light_01", s.vehicle.definition_id); Assert.AreEqual(1, s.vehicle_count);
            Assert.GreaterOrEqual(s.jobs.available, 1); Assert.Greater(s.jobs.best_reward, 0L);
            Assert.AreEqual(23, s.world.cities); Assert.AreEqual(12, s.world.routes); Assert.AreEqual(1, s.world.routes_open);
            Assert.IsTrue(s.convoy.Exists); Assert.AreEqual("Contract Run", s.convoy.name); Assert.IsTrue(s.convoy.is_leader);
            Assert.AreEqual(12.0, s.progression.next_milestone.current, 0.001); Assert.AreEqual(50.0, s.progression.next_milestone.target, 0.001); Assert.AreEqual("Drive 50 km", s.progression.next_milestone.title);
            Assert.GreaterOrEqual(s.unread_notifications, 1);
        }

        [Test] public void AnAbsentNestedObjectIsADefaultInstanceNotNull()
        {   // JsonUtility cannot produce null for nested objects, which is why every optional block has an Exists test instead of a null check
            var s = Parse<DashboardSummary>("get_dashboard_summary");
            Assert.IsNotNull(s.active_job); Assert.IsFalse(s.active_job.Exists);
            var none = JsonUtility.FromJson<MyConvoy>("{\"convoy\": null, \"server_time\": \"2026-10-10T10:00:00+00:00\"}");
            Assert.IsNotNull(none.convoy); Assert.IsFalse(none.convoy.Exists);
        }

        [Test] public void ProfileAndWalletParse()
        {
            var p = Parse<ProfileFull>("get_my_profile"); Assert.AreEqual("CtA", p.display_name); Assert.AreEqual(2, p.level); Assert.AreEqual(5000L, p.coins); Assert.AreEqual(900L, p.lifetime_earnings); Assert.AreEqual(12.0, p.distance_km, 0.001);
            var w = Parse<WalletInfo>("get_my_wallet"); Assert.AreEqual(5000L, w.balance);
            Assert.AreEqual("sunset", Parse<ProfileFull>("update_my_profile").avatar_id);
            Assert.IsFalse(Sample("get_public_profile").Contains("coins"), "a public profile never carries money");
        }

        [Test] public void PeopleListsParse()
        {
            var f = Parse<PeopleList>("list_friends"); Assert.AreEqual(1, f.items.Length); Assert.AreEqual("CtB", f.items[0].display_name); Assert.AreEqual("friends", f.items[0].friend_status); Assert.IsTrue(f.items[0].presence.online);
            var r = Parse<PeopleList>("list_recent_players"); Assert.AreEqual("CtB", r.items[0].display_name); Assert.IsFalse(string.IsNullOrEmpty(r.items[0].last_played_at));
            var q = Parse<FriendRequests>("list_friend_requests"); Assert.AreEqual(1, q.incoming.Length); Assert.AreEqual(1, q.outgoing.Length); Assert.AreEqual("CtD", q.incoming[0].person.display_name);
            Assert.AreEqual("pending", Parse<FriendSendResult>("send_friend_request").status);
            Assert.AreEqual("ctb", Parse<PersonDto>("find_player").username);
        }

        [Test] public void ConvoyRepliesParse()
        {
            var m = Parse<MyConvoy>("get_my_convoy"); Assert.IsTrue(m.convoy.Exists); Assert.AreEqual(2, m.convoy.members.Length); Assert.IsTrue(m.convoy.members[0].is_leader); Assert.IsTrue(m.convoy.is_private); Assert.AreEqual(4, m.convoy.capacity);
            var open = Parse<OpenConvoyList>("list_open_convoys"); Assert.AreEqual("Open Convoy", open.items[0].name); Assert.AreEqual(8, open.items[0].capacity);
            var inv = Parse<ConvoyInviteList>("list_convoy_invites"); Assert.AreEqual("Contract Run", inv.items[0].convoy_name); Assert.AreEqual("CtA", inv.items[0].from.display_name);
            Assert.AreEqual(5, Parse<ConvoyInfo>("set_convoy_options").capacity);
        }

        [Test] public void NotificationsPresenceAndSessionParse()
        {
            var n = Parse<NotificationList>("list_notifications"); Assert.GreaterOrEqual(n.items.Length, 1); Assert.GreaterOrEqual(n.unread, 1); Assert.IsFalse(n.items[0].read); Assert.IsFalse(string.IsNullOrEmpty(n.items[0].title));
            var pres = Parse<MyPresence>("get_my_presence"); Assert.IsTrue(pres.online); Assert.AreEqual("driving", pres.activity); Assert.AreEqual("lagos-ibadan", pres.route_code); Assert.AreEqual("Contract Run", pres.convoy_name);
            var ss = Parse<SessionStarted>("start_session"); Assert.IsFalse(string.IsNullOrEmpty(ss.session_id)); Assert.AreEqual(90.0, ss.ttl_seconds, 0.001);
            Assert.GreaterOrEqual(Parse<HeartbeatAck>("heartbeat").online_players, 1);
        }

        [Test] public void VehiclesAndCatalogParse()
        {
            var g = Parse<VehicleList>("list_my_vehicles"); var v = g.items[0];
            Assert.AreEqual("Savanna 4x2 Light Truck", v.model); Assert.AreEqual(110.0, v.top_speed_kmh, 0.001); Assert.AreEqual(4000, v.cargo_capacity_kg); Assert.IsTrue(v.selected);
            Assert.AreEqual(88.0, v.condition_pct, 0.001); Assert.AreEqual(50.0, v.fuel_pct, 0.001); Assert.AreEqual("on_job", v.status); Assert.AreEqual(g.selected_id, v.id);
            var o = VehicleService.ToOwned(v); Assert.AreEqual(v.id, o.id); Assert.AreEqual(12.0, o.damage_pct, 0.001); Assert.AreEqual(60.0, o.fuel_l, 0.001); Assert.IsTrue(o.selected);
            var c = Parse<CatalogList>("list_vehicle_catalog"); Assert.AreEqual(3, c.items.Length); var med = c.items.First(x => x.definition_id == "truck_medium_01"); Assert.AreEqual(60000L, med.price); Assert.IsFalse(med.can_afford); Assert.IsTrue(med.purchasable);
        }

        [Test] public void ConvoyLobbyRepliesParseWithTheRealJsonUtility()
        {
            var m = Parse<MyConvoy>("get_my_convoy").convoy;
            Assert.AreEqual("lobby", m.phase); Assert.IsFalse(m.Driving); Assert.IsTrue(m.route.Exists); Assert.AreEqual("lagos-ibadan", m.route.code); Assert.AreEqual("Lagos", m.route.from); Assert.AreEqual("Ibadan", m.route.to);
            Assert.AreEqual(128.0, m.route.distance_km, 0.001); Assert.AreEqual(154, m.route.est_minutes); CollectionAssert.AreEqual(new[] { "freight" }, m.tags); Assert.AreEqual("fr", m.language);
            Assert.IsTrue(m.members.All(x => x.ready)); Assert.IsFalse(string.IsNullOrEmpty(m.members[1].vehicle)); Assert.IsTrue(m.all_ready);
            var open = Parse<OpenConvoyList>("list_open_convoys"); Assert.AreEqual(1, open.total); Assert.AreEqual("Lagos \u2192 Ibadan", open.items[0].route.name); CollectionAssert.AreEqual(new[] { "chill_drive", "jobs" }, open.items[0].tags); Assert.IsTrue(open.items[0].joinable); Assert.AreEqual("lobby", open.items[0].phase);
            var chat = Parse<ChatPage>("list_convoy_messages"); Assert.IsTrue(chat.items.Any(l => l.System)); var line = chat.items.First(l => !l.System); Assert.AreEqual("Ready when you are", line.body); Assert.AreEqual("CtA", line.sender.display_name); Assert.Greater(line.id, 0L);
            Assert.Greater(Parse<ChatPosted>("post_convoy_message").id, 0L);
            var pl = Parse<ActivePlayerList>("list_active_players"); Assert.GreaterOrEqual(pl.total, 1); Assert.IsFalse(string.IsNullOrEmpty(pl.items[0].display_name)); Assert.Greater(pl.items[0].level, 0);
            var st = Parse<ServerStatus>("get_server_status"); Assert.GreaterOrEqual(st.players_online, 1); Assert.GreaterOrEqual(st.active_convoys, 2);
            Assert.AreEqual("driving", Parse<ConvoyInfo>("start_convoy_drive").phase); Assert.IsTrue(Parse<ConvoyInfo>("start_convoy_drive").Driving); Assert.AreEqual("lobby", Parse<ConvoyInfo>("end_convoy_drive").phase);
        }

        [Test] public void JobBoardReplyConvertsToTheJobShapeTheGameUses()
        {
            var page = Parse<JobBoardPage>("list_available_jobs"); Assert.GreaterOrEqual(page.total, 1);
            var j = JobService.ToJobDto(page.items[0]);
            Assert.IsFalse(string.IsNullOrEmpty(j.id)); Assert.IsTrue(j.code.StartsWith("CT-")); Assert.AreEqual("Apapa Port Depot", j.origin.name); Assert.AreEqual("Mile 12 Market", j.destination.name); Assert.AreEqual("Lagos", j.origin.city);
            Assert.AreEqual(3.0, j.distance_km, 0.001); Assert.AreEqual(4, j.est_minutes); Assert.AreEqual("Test", j.cargo_type); Assert.AreEqual("truck", j.required_category); Assert.Greater(j.reward, 0L);
            Assert.AreEqual(0.0, j.origin.world_x, 0.001); Assert.AreEqual(2600.0, j.destination.world_x, 0.001);
            var a = Parse<ActiveJobReply>("get_my_active_job"); Assert.IsTrue(a.active.Exists); Assert.AreEqual("accepted", a.active.status); Assert.AreEqual("CT-2", a.active.job.code); Assert.AreEqual(0.0, a.active.verified_km, 0.001);
            Assert.AreEqual("CT-1", JobService.ToJobDto(Parse<BoardJob>("get_job_details")).code);
            // cargo details, requirements and the viewer's estimate arrive from the server and survive the conversion
            var d = JobService.ToJobDto(Parse<BoardJob>("get_job_details"));
            Assert.AreEqual("freight", d.cargo_category); Assert.AreEqual("general", d.cargo_image); Assert.IsFalse(d.fragile); Assert.IsFalse(d.special_handling); Assert.IsTrue(d.has_vehicle);
            Assert.Greater(d.est_fuel_l, 0.0); Assert.Greater(d.est_fuel_cost, 0L); Assert.AreEqual(d.reward - d.est_fuel_cost, d.est_profit);
        }

        [Test] public void WorldStateDrivesTheMapModel()
        {
            var w = Parse<WorldState>("get_world_state"); Assert.AreEqual(23, w.cities.Length); Assert.AreEqual(12, w.routes.Length);
            var ta = Resources.Load<TextAsset>("WorldMap/world_map"); var model = new WorldMapModel(JsonUtility.FromJson<WorldMapData>(ta.text)); model.RequireServerState = true;
            Assert.AreEqual(RouteState.Unavailable, model.StateFor(model.Route("lagos-ibadan"), 99), "fails closed before the server answers");
            WorldService.Apply(model, w);
            Assert.AreEqual(RouteState.Prototype, model.StateFor(model.Route("lagos-ibadan"), 1)); Assert.AreEqual(CityState.Open, model.StateFor(model.City("lagos"), 1));
            Assert.IsFalse(WorldMapModel.IsDrivable(model.StateFor(model.Route("lagos-benin-city"), 99)));
            var ibadan = w.routes.First(r => r.code == "lagos-ibadan"); Assert.AreEqual("OPEN", ibadan.effective_status); Assert.AreEqual(154, ibadan.est_minutes); Assert.AreEqual("road", ibadan.distance_source); Assert.AreEqual("ng-lagos-ibadan", ibadan.game_route_id); CollectionAssert.AreEqual(new[] { "lagos", "ibadan" }, ibadan.via);
            foreach (var r in model.Routes) Assert.IsTrue(w.routes.Any(x => x.code == r.id), "the server knows route " + r.id);
        }

        [Test] public void ContentLeaderboardAndProgressionParse()
        {
            var news = Parse<ContentList>("get_content_news"); Assert.AreEqual(4, news.items.Length); Assert.AreEqual("authored", news.items[0].source); Assert.AreEqual("highway", news.items[0].image_key);
            var hero = Parse<ContentList>("get_content_hero"); Assert.AreEqual(3, hero.items.Length); Assert.AreEqual("jobs", hero.items[0].action); Assert.AreEqual("AFRICA", hero.items[0].headline_accent); Assert.AreEqual("EXPLORE|DELIVER|CONNECT", hero.items[0].eyebrow);
            var lb = Parse<LeaderboardPage>("get_leaderboard"); Assert.AreEqual("xp", lb.metric); Assert.AreEqual(1, lb.items[0].rank); Assert.AreEqual("CtA", lb.items[0].display_name); Assert.AreEqual(300.0, lb.items[0].value, 0.001); Assert.AreEqual(1, lb.me.rank);
            var pr = Parse<ProgressionDto>("get_my_progression"); Assert.AreEqual(2, pr.level); Assert.AreEqual(200L, pr.xp_into_level); Assert.AreEqual(100L, pr.xp_to_next); Assert.IsFalse(string.IsNullOrEmpty(pr.server_time));
            var ach = Parse<AchievementList>("list_my_achievements"); Assert.IsTrue(ach.items.Any(a => a.id == "distance_50" && !a.Unlocked)); Assert.IsTrue(ach.items.Any(a => a.Unlocked));
        }

        [TestCase("rate_limited")] [TestCase("session_expired")] [TestCase("offline")] [TestCase("timeout")] [TestCase("server_error")] [TestCase("request_pending")] [TestCase("invite_required")]
        [TestCase("not_leader")] [TestCase("level_too_low")] [TestCase("route_locked")] [TestCase("activity_in_progress")] [TestCase("vehicle_locked")] [TestCase("username_taken")] [TestCase("already_claimed")]
        public void NewErrorCodesHavePlayerMessages(string code) => Assert.IsFalse(ErrorMessages.ToUser(code, 400).StartsWith("Something went wrong"), code);

        [Test] public void NetworkFailuresAreNotConfusedWithRefusals()
        {
            Assert.AreEqual(FailureKind.Offline, ServiceBase.KindOf("offline")); Assert.AreEqual(FailureKind.Timeout, ServiceBase.KindOf("timeout")); Assert.AreEqual(FailureKind.SessionExpired, ServiceBase.KindOf("session_expired"));
            Assert.AreEqual(FailureKind.Client, ServiceBase.KindOf("job_full")); Assert.AreEqual(FailureKind.Server, ServiceBase.KindOf("server_error"));
        }
    }
}
