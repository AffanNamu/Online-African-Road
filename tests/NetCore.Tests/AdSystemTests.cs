using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using ARO.NetCore;
using Xunit;

/// <summary>Advertising, game side: board ids, the atlas, the manifest policy (fallbacks and back-off) and measured exposure (loaded is not viewed).</summary>
public class AdSystemTests
{
    static readonly Regex BoardRx = new Regex(@"^[A-Za-z0-9_:.-]{3,80}$"), SlotRx = new Regex(@"^[A-Za-z0-9_:.#-]{3,90}$");   // the same checks as the ad_boards and ad_slots tables

    static List<PropInstance> Billboards(RouteModel m)
    {
        var all = new List<PropInstance>();
        for (float x = -1000f; x < m.Length + 1000f; x += 2000f) { /* chunked below */ }
        var seen = new HashSet<string>();
        float step = 400f; var b = RouteBounds(m);
        for (float x = b.minX; x < b.maxX; x += step) for (float z = b.minZ; z < b.maxZ; z += step)
            foreach (var i in PropScatter.PlaceChunk(m, x, z, x + step, z + step, 1f)) if (i.Prop == "billboard") all.Add(i);
        return all;
    }
    static (float minX, float minZ, float maxX, float maxZ) RouteBounds(RouteModel m)
    {
        float minX = float.MaxValue, minZ = float.MaxValue, maxX = float.MinValue, maxZ = float.MinValue;
        foreach (var c in m.AllCorridors) for (int i = 0; i < c.RingX.Length; i++) { minX = Math.Min(minX, c.RingX[i]); maxX = Math.Max(maxX, c.RingX[i]); minZ = Math.Min(minZ, c.RingZ[i]); maxZ = Math.Max(maxZ, c.RingZ[i]); }
        return ((float)Math.Floor(minX / 400f) * 400f - 400f, (float)Math.Floor(minZ / 400f) * 400f - 400f, maxX + 400f, maxZ + 400f);
    }
    static PropDef Def(string id) => Fx.LoadCatalog().props.FirstOrDefault(p => p.id == id);

    [Fact] public void EveryBillboardOfTheShippedRouteIsABoardWithAValidUniqueIdAndATwoToOneFace()
    {
        var m = Fx.Shipped; var inst = Billboards(m);
        Assert.True(inst.Count >= 3, "billboards on the route: " + inst.Count);
        var faces = AdFaces.Place(m.Spec.routeId, inst, Def).ToList();
        Assert.Equal(inst.Count, faces.Count);
        Assert.Equal(faces.Count, faces.Select(f => f.BoardId).Distinct().Count());
        foreach (var f in faces)
        {
            Assert.Matches(BoardRx, f.BoardId); Assert.Matches(SlotRx, f.SlotId); Assert.Equal(f.BoardId + "#1", f.SlotId);
            Assert.Equal("2:1", f.Aspect); Assert.InRange(f.Width, 7f, 7.3f); Assert.InRange(f.Height, 3.5f, 3.7f); Assert.True(f.DesignDistance >= 60f && f.DesignDistance <= 250f);
            Assert.Equal(1f, (float)Math.Sqrt(f.Normal.X * f.Normal.X + f.Normal.Z * f.Normal.Z), 3);
            var c = f.Corners(); Assert.Equal(4, c.Length); Assert.True(c[2].Y > c[0].Y);
        }
    }

    [Fact] public void BoardIdsAreTheSameEveryTimeAndForEveryChunkingOfTheRoute()
    {
        var m = Fx.Shipped;
        var a = AdFaces.Place(m.Spec.routeId, Billboards(m), Def).Select(f => f.SlotId).OrderBy(x => x).ToList();
        var b = AdFaces.Place(m.Spec.routeId, Billboards(RouteModel.Build(Fx.LoadRoute())), Def).Select(f => f.SlotId).OrderBy(x => x).ToList();
        Assert.Equal(a, b);
    }

    [Fact] public void AspectIsTheNearestStandardShape()
    {
        Assert.Equal("2:1", AdFaces.AspectOf(7.2f, 3.6f)); Assert.Equal("3:1", AdFaces.AspectOf(12.2f, 3.0f)); Assert.Equal("1:2", AdFaces.AspectOf(3f, 6f)); Assert.Equal("16:9", AdFaces.AspectOf(5f, 2.8f)); Assert.Equal("1:1", AdFaces.AspectOf(2f, 2f));
    }

    // ---------------------------------------------------------------- the atlas
    [Fact] public void TheAtlasIsEightMegabytesOfNonOverlappingCells()
    {
        var a = new AdAtlas(); Assert.Equal(2048 * 1024 * 4, AdAtlas.Width * AdAtlas.Height * 4);
        Assert.Equal(16, a.Count);
        for (int i = 0; i < a.Count; i++)
        {
            var c = a.Cells[i]; Assert.True(c.X >= 0 && c.Y >= 0 && c.X + c.W <= AdAtlas.Width && c.Y + c.H <= AdAtlas.Height, "cell " + i + " outside");
            for (int j = i + 1; j < a.Count; j++) { var d = a.Cells[j]; Assert.False(c.X < d.X + d.W && d.X < c.X + c.W && c.Y < d.Y + d.H && d.Y < c.Y + c.H, $"cells {i} and {j} overlap"); }
        }
    }

    [Fact] public void ImagesShareCellsByContentAndTheLeastRecentlyUsedIsReused()
    {
        var a = new AdAtlas(); var got = new List<int>(); long t = 1;
        for (int i = 0; i < 11; i++) { int c = a.Assign("img" + i, "2:1", t++, out bool up); Assert.True(up); Assert.False(a.Cells[c].Portrait); got.Add(c); }
        Assert.Equal(11, got.Distinct().Count()); Assert.DoesNotContain(AdAtlas.FallbackCell, got);                         // the fallback cell is never handed out
        Assert.Equal(got[3], a.Assign("img3", "2:1", t++, out bool again)); Assert.False(again);                              // the same creative on another board: the same cell, nothing to upload
        a.Touch(a.CellFor("img0"), t++);                                                                                      // a board near the player is looking at img0 right now
        int evicted = a.Assign("new", "2:1", t++, out bool up2); Assert.True(up2);
        Assert.Equal(-1, a.CellFor("img1"));                                                                                  // the oldest untouched image went
        Assert.NotEqual(-1, a.CellFor("img0")); Assert.Equal(evicted, a.CellFor("new"));
    }

    [Fact] public void PortraitImagesUseThePortraitCellsAndFallBackWhenTheyAreFull()
    {
        var a = new AdAtlas(); long t = 1;
        for (int i = 0; i < 3; i++) { int c = a.Assign("p" + i, "1:2", t++, out _); Assert.True(a.Cells[c].Portrait); }
        int fourth = a.Assign("p3", "1:2", t++, out _); Assert.True(a.Cells[fourth].Portrait); Assert.NotEqual(AdAtlas.FallbackPortraitCell, fourth);
        int fifth = a.Assign("p4", "1:2", t++, out bool up); Assert.True(up); Assert.True(a.Cells[fifth].Portrait);           // the oldest portrait cell is reused
    }

    [Fact] public void LetterboxingNeverStretches()
    {
        var a = new AdAtlas(); int c = 1;                                               // a 512 x 256 cell
        var (u0, v0, u1, v1) = a.UvRect(c, "2:1"); Assert.Equal(512f / 2048f, u1 - u0, 4); Assert.Equal(256f / 1024f, v1 - v0, 4);
        var s = a.UvRect(c, "1:1"); float w = (s.u1 - s.u0) * 2048f, h = (s.v1 - s.v0) * 1024f; Assert.Equal(1f, w / h, 3); Assert.Equal(256f, h, 1);   // a square image sits in the middle, 256 x 256
        var wide = a.UvRect(c, "3:1"); float ww = (wide.u1 - wide.u0) * 2048f, wh = (wide.v1 - wide.v0) * 1024f; Assert.Equal(3f, ww / wh, 2); Assert.Equal(512f, ww, 1);
        int p = a.Assign("x", "1:2", 1, out _); var q = a.UvRect(p, "1:2"); Assert.Equal(0.5f, ((q.u1 - q.u0) * 2048f) / ((q.v1 - q.v0) * 1024f), 3);
        foreach (var r in new[] { u0, v0, u1, v1, s.u0, s.v0, s.u1, s.v1, wide.u0, wide.v0, wide.u1, wide.v1 }) Assert.InRange(r, 0f, 1f);
    }

    // ---------------------------------------------------------------- the manifest policy: the game must keep working with the service down
    static AdItem Item(string slot, string sha, string aspect = "2:1") => new AdItem { SlotId = slot, Sha256 = sha, Image = "creatives/" + sha + "/2x1_v1.webp", Aspect = aspect, Width = 1024, Height = 512 };

    [Fact] public void ABadServerBacksOffAndKeepsTheLastGoodManifestForADay()
    {
        var s = new AdManifestState(); Assert.True(s.ShouldFetch(0));
        s.OnFetched(0, "v1", false, new[] { Item("a#1", "h1") });
        Assert.False(s.ShouldFetch(100)); Assert.True(s.ShouldFetch(AdManifestState.RefreshSeconds + 1));
        s.OnFailed(500); Assert.False(s.ShouldFetch(520)); Assert.True(s.ShouldFetch(500 + 61));
        s.OnFailed(600); s.OnFailed(700); s.OnFailed(800);
        Assert.True(s.ShouldFetch(800 + 481) && !s.ShouldFetch(800 + 479));                                                   // 60 * 2^3 = 480 s after the fourth failure
        for (int i = 0; i < 30; i++) s.OnFailed(900);
        Assert.False(s.ShouldFetch(900 + AdManifestState.MaxBackoffSeconds - 1)); Assert.True(s.ShouldFetch(900 + AdManifestState.MaxBackoffSeconds + 1));   // never longer than 30 minutes
        Assert.Single(s.Active(3600)); Assert.Equal("a#1", s.ItemFor("a#1", 3600).SlotId);                                    // the last good manifest still serves
        Assert.Empty(s.Active(AdManifestState.MaxAgeSeconds + 10)); Assert.Null(s.ItemFor("a#1", AdManifestState.MaxAgeSeconds + 10));   // after a day: fallback artwork
        s.OnFetched(AdManifestState.MaxAgeSeconds + 20, "v1", true, null); Assert.Single(s.Active(AdManifestState.MaxAgeSeconds + 30));  // an unchanged answer keeps the items and renews them
    }

    [Fact] public void OnlyNearbyMissingImagesAreWantedNearestFirstAndFailuresDoNotLoop()
    {
        var s = new AdManifestState(); var faces = new[]
        {
            new AdFace { SlotId = "near#1", Centre = new V3(100, 0, 0) }, new AdFace { SlotId = "mid#1", Centre = new V3(300, 0, 0) }, new AdFace { SlotId = "far#1", Centre = new V3(900, 0, 0) }, new AdFace { SlotId = "none#1", Centre = new V3(50, 0, 0) },
        };
        s.OnFetched(0, "v", false, new[] { Item("near#1", "hn"), Item("mid#1", "hm"), Item("far#1", "hf") });
        var w = s.Wanted(1, new V3(0, 0, 0), faces, it => false, max: 5);
        Assert.Equal(new[] { "hn", "hm" }, w.Select(x => x.Sha256).ToArray());                                                // far is outside 400 m, none has no item
        Assert.Single(s.Wanted(1, new V3(0, 0, 0), faces, it => false, max: 1));
        Assert.Equal(new[] { "hm" }, s.Wanted(1, new V3(0, 0, 0), faces, it => it.Sha256 == "hn", max: 5).Select(x => x.Sha256).ToArray());   // already in the atlas
        s.ImageFailed("hn", 10, false); Assert.DoesNotContain(s.Wanted(11, new V3(0, 0, 0), faces, it => false, 5), x => x.Sha256 == "hn");        // back off
        Assert.Contains(s.Wanted(10 + AdManifestState.ImageRetrySeconds + 1, new V3(0, 0, 0), faces, it => false, 5), x => x.Sha256 == "hn");        // then try again, once
        s.ImageFailed("hm", 10, true); Assert.DoesNotContain(s.Wanted(99999, new V3(0, 0, 0), faces, it => false, 5), x => x.Sha256 == "hm");      // a bad image is never retried
    }

    // ---------------------------------------------------------------- measured exposure
    [Fact] public void ExposureNeedsDistanceViewAngleNoOcclusionAndARealAdvertisement()
    {
        Assert.True(AdExposureMeter.Exposed(100f, 150f, 20f, true, false, true));
        Assert.False(AdExposureMeter.Exposed(200f, 150f, 20f, true, false, true));       // too far to read
        Assert.False(AdExposureMeter.Exposed(100f, 150f, 20f, false, false, true));      // behind the camera
        Assert.False(AdExposureMeter.Exposed(100f, 150f, 75f, true, false, true));       // seen edge-on
        Assert.False(AdExposureMeter.Exposed(100f, 150f, 20f, true, true, true));        // hidden behind something
        Assert.False(AdExposureMeter.Exposed(100f, 150f, 20f, true, false, false));      // showing the fallback, or only loaded
    }

    [Fact] public void ALoadedImageIsNotAViewAndAViewNeedsOnePointFiveSeconds()
    {
        var m = new AdExposureMeter();
        m.Loaded("a#1"); for (int i = 0; i < 100; i++) m.Observe("a#1", 0.1f, false);
        var e = m.Flush().Single(); Assert.Equal(1, e.Loaded); Assert.Equal(0, e.Views); Assert.Equal(0f, e.Seconds);       // loaded, never exposed: no view, no seconds
        for (int i = 0; i < 14; i++) m.Observe("b#1", 0.1f, true); Assert.Empty(m.Flush().Where(x => x.Views > 0));        // 1.4 s: not yet a view
        for (int i = 0; i < 3; i++) m.Observe("b#1", 0.1f, true);                                                           // 1.7 s in one pass
        var b = m.Flush().Single(x => x.SlotId == "b#1"); Assert.Equal(1, b.Views); Assert.InRange(b.Seconds, 0.2f, 0.4f);   // (the first 1.4 s were already flushed)
    }

    [Fact] public void APassEndsAfterALapseAndTwoPassesAreTwoViews()
    {
        var m = new AdExposureMeter();
        for (int i = 0; i < 20; i++) m.Observe("a#1", 0.1f, true);                        // pass 1: 2 s
        for (int i = 0; i < 5; i++) m.Observe("a#1", 0.1f, false);                        // 0.5 s hidden: still the same pass
        for (int i = 0; i < 20; i++) m.Observe("a#1", 0.1f, true);                        // continues: still one view
        Assert.Equal(1, m.Flush().Single().Views);
        for (int i = 0; i < 15; i++) m.Observe("a#1", 0.1f, false);                       // 1.5 s away: the pass is over
        for (int i = 0; i < 20; i++) m.Observe("a#1", 0.1f, true);                        // pass 2
        Assert.Equal(1, m.Flush().Single().Views);
    }

    [Fact] public void TheFlushIsBoundedAndCarriesNoIdentity()
    {
        var m = new AdExposureMeter();
        for (int k = 0; k < 60; k++) for (int i = 0; i < 40; i++) m.Observe("slot" + k + "#1", 0.1f, true);
        var f = m.Flush(); Assert.True(f.Count <= 40); Assert.True(f.Sum(x => x.Seconds) <= AdExposureMeter.MaxSecondsPerFlush + 0.01f);
        string json = AdExposureMeter.ToJson(f); Assert.StartsWith("[{\"slot_id\":", json);
        Assert.DoesNotMatch(new Regex("player|user|session|device|email|ip\"", RegexOptions.IgnoreCase), json);
        Assert.Equal("[]", AdExposureMeter.ToJson(new AdExposureMeter.Entry[0]));
        var big = new AdExposureMeter(); for (int i = 0; i < 5000; i++) big.Observe("a#1", 0.1f, true);                       // 500 s on one face
        Assert.True(big.Flush().Single().Seconds <= 120f);                                                                     // the server clamps to 120 per entry; so does the client
    }
}

/// <summary>
/// The advertising inventory of the shipped route as SQL (supabase/seed_ads.sql). Every board in the world is a row; the admin never invents boards. The file is generated from the route data by this test
/// (ARO_WRITE_AD_SEED=1 dotnet test --filter AdInventorySeed rewrites it) and the test fails when the file is not current. Boards start as 'planned': the owner switches a board to 'available' when its production
/// model is in the game. The seed is idempotent and never overwrites a board the owner has changed.
/// </summary>
public class AdInventorySeed
{
    static string RepoRoot() { var d = new System.IO.DirectoryInfo(AppContext.BaseDirectory); while (d != null && !System.IO.File.Exists(System.IO.Path.Combine(d.FullName, "CLAUDE.md"))) d = d.Parent; return d?.FullName; }
    static string Q(string s) => "'" + s.Replace("'", "''") + "'";
    static string F(float v) => v.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);

    public static string Generate()
    {
        var m = Fx.Shipped; var sb = new System.Text.StringBuilder();
        sb.AppendLine("-- GENERATED by tests/NetCore.Tests/AdSystemTests.cs (class AdInventorySeed) from the route data. Do not edit by hand.");
        sb.AppendLine("-- Regenerate: ARO_WRITE_AD_SEED=1 dotnet test tests/NetCore.Tests --filter AdInventorySeed");
        sb.AppendLine("-- Every billboard prop the route data places, as a board with one face. Boards start 'planned' (the production model is not in the game yet); the owner switches them to 'available'.");
        sb.AppendLine("-- Idempotent: an existing board is left exactly as the owner set it.");
        sb.AppendLine("insert into ad_boards(id, route_code, city, district, road_label, station_m, side, board_type, width_m, height_m, illuminated, digital, status, source) values");
        var inst = new List<PropInstance>(); var seen = new HashSet<string>();
        float minX = float.MaxValue, minZ = float.MaxValue, maxX = float.MinValue, maxZ = float.MinValue;
        foreach (var c in m.AllCorridors) for (int i = 0; i < c.RingX.Length; i++) { minX = Math.Min(minX, c.RingX[i]); maxX = Math.Max(maxX, c.RingX[i]); minZ = Math.Min(minZ, c.RingZ[i]); maxZ = Math.Max(maxZ, c.RingZ[i]); }
        for (float x = (float)Math.Floor(minX / 400f) * 400f - 400f; x < maxX + 400f; x += 400f) for (float z = (float)Math.Floor(minZ / 400f) * 400f - 400f; z < maxZ + 400f; z += 400f)
            foreach (var i in PropScatter.PlaceChunk(m, x, z, x + 400f, z + 400f, 1f)) if (i.Prop == "billboard") inst.Add(i);
        var faces = AdFaces.Place(m.Spec.routeId, inst, id => Fx.LoadCatalog().props.First(p => p.id == id)).OrderBy(f => f.BoardId, StringComparer.Ordinal).ToList();
        var rows = new List<string>(); var slots = new List<string>();
        foreach (var f in faces)
        {
            float s = m.DistanceAlong(f.Centre.X, f.Centre.Z, out float lat); var zone = m.Main.ZoneAt(Mathx.Clamp(s, 0f, m.Length));
            string type = zone == Zone.Highway ? "highway" : zone == Zone.Urban || zone == Zone.Commercial ? "urban" : "roadside";
            rows.Add($"  ({Q(f.BoardId)}, {Q(m.Spec.routeId)}, null, null, {Q(m.Spec.name.Length > 80 ? m.Spec.name.Substring(0, 80) : m.Spec.name)}, {Math.Round(s)}, {Q(lat >= 0 ? "right" : "left")}, {Q(type)}, {F(f.Width)}, {F(f.Height)}, true, false, 'planned', 'route_data')");
            slots.Add($"  ({Q(f.SlotId)}, {Q(f.BoardId)}, 1, {Q(f.Aspect)}, {F(f.Width)}, {F(f.Height)})");
        }
        sb.AppendLine(string.Join(",\n", rows)); sb.AppendLine("on conflict (id) do nothing;");
        sb.AppendLine(); sb.AppendLine("insert into ad_slots(id, board_id, face, aspect, width_m, height_m) values");
        sb.AppendLine(string.Join(",\n", slots)); sb.AppendLine("on conflict (id) do nothing;");
        return sb.ToString().Replace("\r\n", "\n");
    }

    [Fact] public void TheSeedFileIsCurrentWithTheRouteData()
    {
        string path = System.IO.Path.Combine(RepoRoot(), "supabase", "seed_ads.sql"), now = Generate();
        if (Environment.GetEnvironmentVariable("ARO_WRITE_AD_SEED") == "1") { System.IO.File.WriteAllText(path, now); return; }
        Assert.True(System.IO.File.Exists(path), "supabase/seed_ads.sql is missing: ARO_WRITE_AD_SEED=1 dotnet test --filter AdInventorySeed");
        Assert.Equal(System.IO.File.ReadAllText(path).Replace("\r\n", "\n"), now);
    }

    [Fact] public void EveryRowFitsTheTableChecks()
    {
        var sql = Generate(); int boards = Regex.Matches(sql, @"'route_data'\)").Count;
        Assert.True(boards >= 3, "boards " + boards);
        foreach (Match mt in Regex.Matches(sql, @"^  \('([^']+)', 'ng-[^\n]*'route_data'\)", RegexOptions.Multiline)) Assert.Matches(new Regex(@"^[A-Za-z0-9_:.-]{3,80}$"), mt.Groups[1].Value);
        Assert.DoesNotContain("planned', 'route_data'),,", sql);
    }
}
