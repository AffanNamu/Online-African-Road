using System;
using System.Collections.Generic;
using System.Linq;

namespace ARO.NetCore
{
    // ===================================================================================================== frontage: blocks and lots along a road
    // The roadside scatter (PropRule) places independent points. A CITY needs buildings that do not overlap each other, keep their whole footprint off every road, stand a set distance back, come in a
    // controlled mix, and include a civic building (the mosques) every so often. That is what a FrontageRule does, deterministically: no state, no random numbers, nothing that depends on which chunks
    // are loaded, so every client that has the route data places the same buildings in the same places (multiplayer agrees by construction).
    //
    // How it works. Along a corridor the road is cut into BLOCKS of `blockLength` metres starting at sFrom. A block is the unit of determinism: its lots are laid out from the block start, one
    // after another, with hashes of (route seed, rule, block, lot) as the only source of choice, so a chunk can compute exactly the blocks it touches. A lot is a rectangle (width along the road,
    // depth away from it) set back from the road formation, turned to face the road. It is REJECTED if any sample of its footprint lies within `clearance` metres of ANY road formation, site
    // plate or island of the route, if the terrain under it is steeper than `maxHeightRange`, if the road beside it is on a bridge, or if it would overlap a lot already placed in the block.
    // Every `anchorEveryBlocks`-th block (the phase differs per side) starts with an ANCHOR lot picked from `anchors`; an anchor is never thinned by the quality tier and is retried at other
    // positions and with other anchor designs if its first position is rejected.

    /// <summary>One building design in a frontage mix: the catalogue id, its footprint in metres (width along the road, depth away from it) and its weight in the mix.</summary>
    [Serializable]
    public class FrontageLot
    {
        public string prop;
        public float weight = 1f;
        public float widthM, depthM;            // the footprint of the asset as it stands (a compound includes its forecourt and wall); must match the catalogue entry (a test checks it)
        public string tag = "";                 // what kind of building it is (mosque, shop, house, ...): for tests and reports, not for the engine
    }

    /// <summary>A rule that fills the frontage of one road (or one side of it) with lots. All numbers are metres. See the header of this file.</summary>
    [Serializable]
    public class FrontageRule
    {
        public string id;
        public string corridor = "";            // "" = the main road; otherwise a junction id, a road id or a ring id
        public string zones = "";               // comma separated; empty = every zone
        public string profiles = "";            // comma separated road profile ids; empty = every profile
        public string side = "both";            // both | left | right
        public float sFrom = 0f, sTo = 0f;      // sTo <= sFrom means "to the end of the corridor"
        public float setbackMin = 5f, setbackMax = 9f;   // from the edge of the road formation to the FRONT of the lot
        public float gapMin = 1.5f, gapMax = 6f;         // between neighbouring lots along the road
        public float blockLength = 120f;        // the unit of determinism; must hold the widest lot
        public float blockMargin = 3f;          // vacant at each end of a block (an alley between blocks)
        public float presence = 0.9f;           // probability that a slot gets its building (the others stay empty plots)
        public float clearance = 2.5f;          // kept between every point of the footprint and every road formation, site plate and island
        public float maxHeightRange = 1.6f;     // largest terrain height difference under a footprint; a steeper lot is not built
        public float yawJitterDeg = 1.5f;
        public bool fixedDensity;               // true = the quality tier's prop density does not thin this rule
        public int rows = 1;                    // 1..4: rows of lots behind the first (a city is deeper than one lot); anchors stand in the first row only
        public float rowGap = 8f;               // metres of open ground (a back lane) between one row and the next
        public FrontageLot[] mix;
        public FrontageLot[] anchors;           // civic lots, e.g. the mosques; optional
        public int anchorEveryBlocks = 0;       // 0 = no anchors; N = one anchor in every N-th block on each side
        public int anchorPhase = -1;            // -1 = the phase of each side comes from a hash; 0..N-1 = chosen by the designer: the left side uses it, the right side is staggered by half a period
    }

    /// <summary>A lot the engine decided to build: where, which way it faces, how big, and why it is there.</summary>
    public struct FrontagePlacement
    {
        public string Prop, Rule, Tag; public int Block, Side, Slot; public bool Anchor;
        public float X, Y, Z, YawDeg, Width, Depth, S;     // S: station along the corridor of the lot centre
        /// <summary>The four corners of the footprint (x, z pairs, counter-clockwise from the front left).</summary>
        public void Corners(float[] xz)
        {
            float a = YawDeg * (float)(Math.PI / 180.0), fx = (float)Math.Sin(a), fz = (float)Math.Cos(a), rx = fz, rz = -fx, hw = Width * 0.5f, hd = Depth * 0.5f;
            int k = 0;
            foreach (var (sw, sd) in new[] { (-1f, 1f), (1f, 1f), (1f, -1f), (-1f, -1f) }) { xz[k++] = X + rx * hw * sw + fx * hd * sd; xz[k++] = Z + rz * hw * sw + fz * hd * sd; }
        }
        /// <summary>True when the two footprints, each grown by `pad` metres, overlap (separating axis test).</summary>
        public static bool Overlap(FrontagePlacement a, FrontagePlacement b, float pad)
        {
            float da = (float)(a.YawDeg * Math.PI / 180.0), db = (float)(b.YawDeg * Math.PI / 180.0);
            var axes = new[] { ((float)Math.Sin(da), (float)Math.Cos(da)), ((float)Math.Cos(da), -(float)Math.Sin(da)), ((float)Math.Sin(db), (float)Math.Cos(db)), ((float)Math.Cos(db), -(float)Math.Sin(db)) };
            var ca = new float[8]; var cb = new float[8]; a.Corners(ca); b.Corners(cb);
            foreach (var (ax, az) in axes)
            {
                float aMin = float.MaxValue, aMax = float.MinValue, bMin = float.MaxValue, bMax = float.MinValue;
                for (int i = 0; i < 4; i++) { float pa = ca[i * 2] * ax + ca[i * 2 + 1] * az, pb = cb[i * 2] * ax + cb[i * 2 + 1] * az; aMin = Math.Min(aMin, pa); aMax = Math.Max(aMax, pa); bMin = Math.Min(bMin, pb); bMax = Math.Max(bMax, pb); }
                if (aMax + pad <= bMin || bMax + pad <= aMin) return false;
            }
            return true;
        }
    }

    public static class FrontagePlacer
    {
        const float SampleStep = 5f;

        /// <summary>The corridor a rule runs along, or null when the id names none.</summary>
        public static RoadCorridor CorridorOf(RouteModel m, FrontageRule r) => string.IsNullOrEmpty(r.corridor) || r.corridor == m.Spec.routeId ? m.Main : m.CorridorById(r.corridor);

        /// <summary>Adds the buildings of every frontage rule that fall inside the box to `res` (called by PropScatter.PlaceChunk; a route with no frontage rules costs nothing).</summary>
        public static void AddToChunk(RouteModel m, float minX, float minZ, float maxX, float maxZ, float density, List<PropInstance> res)
        {
            var rules = m.Spec.frontage; if (rules == null || rules.Length == 0) return;
            for (int ri = 0; ri < rules.Length; ri++)
            {
                var r = rules[ri]; var c = CorridorOf(m, r); if (c == null) continue;
                float reach = r.setbackMax + MaxOf(r, l => l.depthM) + MaxOf(r, l => l.widthM) + 12f;
                if (!PropScatter.SRangeOf(c, minX, minZ, maxX, maxZ, reach, out float lo, out float hi)) continue;
                foreach (var p in Lots(m, ri, lo, hi, density))
                    if (p.X >= minX && p.X < maxX && p.Z >= minZ && p.Z < maxZ)
                        res.Add(new PropInstance { Prop = p.Prop, Origin = p.Rule, Pos = new V3(p.X, p.Y, p.Z), YawDeg = p.YawDeg, Scale = 1f });
            }
        }

        /// <summary>Every lot of rule `ri` whose block touches stations sLo..sHi of its corridor, in a fixed order. `density` 1 = every slot decides by its own presence.</summary>
        public static List<FrontagePlacement> Lots(RouteModel m, int ri, float sLo, float sHi, float density = 1f)
        {
            var res = new List<FrontagePlacement>(); var r = m.Spec.frontage[ri]; var c = CorridorOf(m, r); if (c == null) return res;
            float L = c.Length, from = Math.Max(0f, r.sFrom), to = r.sTo > r.sFrom ? Math.Min(L, r.sTo) : L;
            if (to - from < 8f || r.blockLength < 8f) return res;
            int b0 = Math.Max(0, (int)Math.Floor((Math.Max(sLo, from) - from) / r.blockLength) - 1), b1 = (int)Math.Floor((Math.Min(sHi, to) - from) / r.blockLength) + 1;
            foreach (int side in r.side == "left" ? new[] { -1 } : r.side == "right" ? new[] { 1 } : new[] { -1, 1 })
                for (int b = b0; b <= b1; b++) Block(m, c, r, ri, b, side, from, to, density, res);
            return res;
        }

        /// <summary>The whole route's lots of rule `ri` (tests and reports; the game asks per chunk).</summary>
        public static List<FrontagePlacement> AllLots(RouteModel m, int ri, float density = 1f) { var c = CorridorOf(m, m.Spec.frontage[ri]); return c == null ? new List<FrontagePlacement>() : Lots(m, ri, 0f, c.Length, density); }

        static float MaxOf(FrontageRule r, Func<FrontageLot, float> f)
        {
            float v = 0f; if (r.mix != null) foreach (var l in r.mix) v = Math.Max(v, f(l)); if (r.anchors != null) foreach (var l in r.anchors) v = Math.Max(v, f(l)); return v;
        }

        static FrontageLot Pick(FrontageLot[] set, float u)
        {
            float tot = 0f; foreach (var l in set) tot += l.weight;
            float t = u * tot, acc = 0f;
            foreach (var l in set) { acc += l.weight; if (t < acc) return l; }
            return set[set.Length - 1];
        }

        static void Block(RouteModel m, RoadCorridor c, FrontageRule r, int ri, int bi, int side, float from, float to, float density, List<FrontagePlacement> res)
        {
            float bs = from + bi * r.blockLength, be = Math.Min(bs + r.blockLength, to);
            if (be - bs < 8f + 2f * r.blockMargin) return;
            int seed = m.Spec.seed + ri * 7919 + (side + 1) * 104729;
            float dens = r.fixedDensity && density > 0f ? 1f : density;
            int N = r.anchors != null && r.anchors.Length > 0 ? r.anchorEveryBlocks : 0;
            int phase = N <= 0 ? 0 : r.anchorPhase >= 0 ? (side < 0 ? r.anchorPhase : r.anchorPhase + N / 2) % N : (int)(Mathx.Hash01(seed, 0, 977) * N);
            bool anchorPending = N > 0 && (bi + phase) % N == 0;
            var zones = ZoneSet(r.zones); var profiles = ProfileSet(r.profiles);
            var placed = new List<FrontagePlacement>(); int rows = Math.Max(1, Math.Min(4, r.rows)); float rowStep = MaxOf(r, l => l.depthM) + r.rowGap;
            for (int row = 0; row < rows; row++)
            {
                if (row > 0) anchorPending = false;
                float cursor = bs + r.blockMargin, end = be - r.blockMargin; int slot = 0, hrow = (bi * 4 + row) * 64; string prevProp = null;
            while (cursor < end - 3f && slot < 48)
            {
                int hk = hrow + slot;
                var set = anchorPending ? r.anchors : r.mix; var lot = Pick(set, Mathx.Hash01(seed, hk, 1));
                if (!anchorPending && set.Length > 2 && prevProp != null)
                    for (int again = 0; again < 4 && lot.prop == prevProp; again++) lot = Pick(set, Mathx.Hash01(seed, hk, 20 + again));       // no two identical buildings side by side when the mix has a choice
                if (lot.widthM > end - cursor)
                {   // the pick does not fit in what is left of the block: the narrowest one that does, or the block is full
                    FrontageLot fit = null; foreach (var l in set) if (l.widthM <= end - cursor && (fit == null || l.widthM > fit.widthM)) fit = l;
                    if (fit == null) { if (anchorPending) { anchorPending = false; continue; } break; }
                    lot = fit;
                }
                bool used = anchorPending || Mathx.Hash01(seed, hk, 2) < r.presence * dens;
                float gap = Mathx.Lerp(r.gapMin, r.gapMax, Mathx.Hash01(seed, hk, 3)), advance = lot.widthM + gap;
                if (used)
                {
                    FrontagePlacement p; bool ok;
                    if (anchorPending)
                    {   // an anchor is retried: the other anchor designs, then further along the block (every 8 m), before it is given up
                        ok = false; p = default;
                        for (int shift = 0; shift < 24 && !ok; shift++)
                            for (int a = 0; a < r.anchors.Length && !ok; a++)
                            {
                                var cand = r.anchors[(Array.IndexOf(r.anchors, lot) + a) % r.anchors.Length]; float at = cursor + shift * 8f;
                                if (at + cand.widthM > end) continue;
                                if (TryLot(m, c, r, ri, bi, side, slot, cand, at, true, seed, hk, row * rowStep, zones, profiles, placed, out p)) { ok = true; lot = cand; advance = (at - cursor) + cand.widthM + gap; }
                            }
                        anchorPending = false;
                    }
                    else ok = TryLot(m, c, r, ri, bi, side, slot, lot, cursor, false, seed, hk, row * rowStep, zones, profiles, placed, out p);
                    if (ok) { placed.Add(p); prevProp = p.Prop; }
                }
                cursor += advance; slot++;
            }
            }
            res.AddRange(placed);
        }

        static bool TryLot(RouteModel m, RoadCorridor c, FrontageRule r, int ri, int bi, int side, int slot, FrontageLot lot, float s0, bool anchor, int seed, int hk, float rowOffset, HashSet<Zone> zones, HashSet<string> profiles,
                           List<FrontagePlacement> placed, out FrontagePlacement p)
        {
            p = default; float w = lot.widthM, d = lot.depthM, s1 = s0 + w, sC = s0 + w * 0.5f, L = c.Length;
            if (s0 < 0f || s1 > L) return false;
            if (zones != null && !zones.Contains(c.ZoneAt(sC))) return false;
            if (profiles != null && !profiles.Contains(c.SectionAt(sC).Spec.id)) return false;
            if (OnValleyBridge(c, s0) || OnValleyBridge(c, sC) || OnValleyBridge(c, s1)) return false;                    // beside a valley bridge there is no ground; beside a flyover deck there is (the city under and beside it)
            c.Spline.FrameAt(sC, out var pos, out _, out var right);
            float half = Math.Max(c.HalfFormationAt(s0), Math.Max(c.HalfFormationAt(sC), c.HalfFormationAt(s1)));
            float front = half + rowOffset + Mathx.Lerp(r.setbackMin, r.setbackMax, Mathx.Hash01(seed, hk, 4)), off = front + d * 0.5f;
            float cx = pos.X + right.X * side * off, cz = pos.Z + right.Z * side * off;
            float yaw = (float)(Math.Atan2(-right.X * side, -right.Z * side) * 180.0 / Math.PI) + (Mathx.Hash01(seed, hk, 5) * 2f - 1f) * r.yawJitterDeg;
            p = new FrontagePlacement { Prop = lot.prop, Rule = r.id, Tag = lot.tag, Block = bi, Side = side, Slot = slot, Anchor = anchor, X = cx, Z = cz, YawDeg = yaw, Width = w, Depth = d, S = sC };
            // the footprint is sampled on a grid (and its edges): every sample must be clear of every road, plate and island, and the ground under it must be nearly level
            float a = yaw * (float)(Math.PI / 180.0), fx = (float)Math.Sin(a), fz = (float)Math.Cos(a), rx = fz, rz = -fx;
            int nu = Math.Max(2, (int)Math.Ceiling(w / SampleStep) + 1), nv = Math.Max(2, (int)Math.Ceiling(d / SampleStep) + 1);
            float lo = float.MaxValue, hi = float.MinValue;
            for (int i = 0; i < nu; i++)
                for (int j = 0; j < nv; j++)
                {
                    float u = -w * 0.5f + w * i / (nu - 1), v = -d * 0.5f + d * j / (nv - 1);
                    float x = cx + rx * u + fx * v, z = cz + rz * u + fz * v;
                    if (m.Terrain.InsideFormation(x, z, r.clearance)) return false;
                    float h = m.Terrain.HeightAt(x, z); lo = Math.Min(lo, h); hi = Math.Max(hi, h);
                }
            if (hi - lo > r.maxHeightRange) return false;
            p.Y = lo;
            foreach (var o in placed) if (FrontagePlacement.Overlap(p, o, 0.5f)) return false;
            return true;
        }

        static bool OnValleyBridge(RoadCorridor c, float s) => c.InBridge(s, out var b) && !b.overRoad;

        static HashSet<Zone> ZoneSet(string z)
        {
            if (string.IsNullOrWhiteSpace(z)) return null;
            var set = new HashSet<Zone>(); foreach (var part in z.Split(',')) if (RouteVocab.TryZone(part, out var zz)) set.Add(zz);
            return set;
        }
        static HashSet<string> ProfileSet(string list)
        {
            if (string.IsNullOrWhiteSpace(list)) return null;
            var set = new HashSet<string>(); foreach (var part in list.Split(',')) { var t = part.Trim(); if (t.Length > 0) set.Add(t); }
            return set.Count == 0 ? null : set;
        }
    }

    public static class FrontageValidator
    {
        static readonly string[] Sides = { "both", "left", "right" };

        /// <summary>Data checks for the frontage rules of a route (ranges, references, and that two rules never share a side of a corridor over the same stretch). Geometry is checked when lots are placed.</summary>
        public static void Validate(RouteSpec spec, ISet<string> junctionIds, ISet<string> knownProps, List<string> e)
        {
            var rules = spec.frontage; if (rules == null || rules.Length == 0) return;
            if (spec.schemaVersion < 3) { e.Add("frontage rules need schemaVersion 3"); return; }
            var ids = new HashSet<string>(); var corridors = new HashSet<string> { "", spec.routeId };
            foreach (var j in junctionIds) corridors.Add(j);
            foreach (var rd in spec.roads ?? new RoadSpec[0]) if (rd != null) corridors.Add(rd.id);
            foreach (var rg in spec.rings ?? new RingSpec[0]) if (rg != null) corridors.Add(rg.id);
            for (int i = 0; i < rules.Length; i++)
            {
                var r = rules[i]; if (r == null) { e.Add("a frontage rule is null"); continue; }
                string tag = "frontage rule '" + r.id + "'";
                if (string.IsNullOrEmpty(r.id) || !ids.Add(r.id)) e.Add(tag + ": id is missing or duplicate");
                if (!corridors.Contains(r.corridor ?? "")) e.Add(tag + ": corridor '" + r.corridor + "' is neither the main road, a junction, a road nor a ring");
                if (!Sides.Contains(r.side)) e.Add(tag + ": side must be both, left or right");
                if (!string.IsNullOrWhiteSpace(r.zones)) foreach (var z in r.zones.Split(',')) if (!RouteVocab.TryZone(z, out _)) e.Add(tag + ": unknown zone '" + z.Trim() + "'");
                if (!string.IsNullOrWhiteSpace(r.profiles)) foreach (var pid in r.profiles.Split(',')) if (!spec.profiles.Any(pr => pr.id == pid.Trim())) e.Add(tag + ": unknown road profile '" + pid.Trim() + "'");
                if (!(r.clearance >= 0f && r.clearance <= 10f)) e.Add(tag + ": clearance must be 0..10 m");
                if (!(r.setbackMin >= r.clearance + 0.5f && r.setbackMax >= r.setbackMin && r.setbackMax <= 60f)) e.Add(tag + ": setbacks must satisfy clearance + 0.5 <= min <= max <= 60 m (the front of a lot must be clear of its own road)");
                if (!(r.gapMin >= 0f && r.gapMax >= r.gapMin && r.gapMax <= 40f)) e.Add(tag + ": gaps must satisfy 0 <= min <= max <= 40 m");
                if (!(r.blockMargin >= 0f && r.blockMargin <= 30f)) e.Add(tag + ": blockMargin must be 0..30 m");
                if (!(r.presence >= 0f && r.presence <= 1f)) e.Add(tag + ": presence must be 0..1");
                if (!(r.maxHeightRange >= 0.2f && r.maxHeightRange <= 6f)) e.Add(tag + ": maxHeightRange must be 0.2..6 m");
                if (!(r.yawJitterDeg >= 0f && r.yawJitterDeg <= 8f)) e.Add(tag + ": yawJitterDeg must be 0..8");
                if (r.rows < 1 || r.rows > 4) e.Add(tag + ": rows must be 1..4");
                if (!(r.rowGap >= 0f && r.rowGap <= 40f)) e.Add(tag + ": rowGap must be 0..40 m");
                if (r.mix == null || r.mix.Length == 0) e.Add(tag + ": mix needs at least one lot");
                float widest = 0f;
                foreach (var set in new[] { r.mix, r.anchors })
                    foreach (var l in set ?? new FrontageLot[0])
                    {
                        if (l == null || string.IsNullOrEmpty(l.prop)) { e.Add(tag + ": a lot has no prop"); continue; }
                        if (knownProps != null && !knownProps.Contains(l.prop)) e.Add(tag + ": unknown prop '" + l.prop + "'");
                        if (!(l.weight > 0f)) e.Add(tag + ": lot '" + l.prop + "' needs a positive weight");
                        if (!(l.widthM >= 3f && l.widthM <= 80f && l.depthM >= 3f && l.depthM <= 80f)) e.Add(tag + ": lot '" + l.prop + "' footprint must be 3..80 m each way");
                        widest = Math.Max(widest, l.widthM);
                    }
                if (!(r.blockLength >= widest + 2f * r.blockMargin && r.blockLength <= 600f)) e.Add(tag + ": blockLength must hold the widest lot plus both margins and be at most 600 m");
                bool hasAnchors = r.anchors != null && r.anchors.Length > 0;
                if (hasAnchors != (r.anchorEveryBlocks > 0)) e.Add(tag + ": anchors and anchorEveryBlocks go together (both or neither)");
                if (r.anchorEveryBlocks < 0 || r.anchorEveryBlocks > 40) e.Add(tag + ": anchorEveryBlocks must be 0..40");
                if (r.anchorPhase < -1 || (r.anchorEveryBlocks > 0 && r.anchorPhase >= r.anchorEveryBlocks)) e.Add(tag + ": anchorPhase must be -1 or 0..anchorEveryBlocks-1");
                for (int k = 0; k < i; k++)
                {   // two rules may not fill the same side of the same corridor over the same stretch (their lots could overlap): separate them by corridor, side, profile, zone or station range
                    var o = rules[k]; if (o == null || (o.corridor ?? "") != (r.corridor ?? "")) continue;
                    if (!(r.side == "both" || o.side == "both" || r.side == o.side)) continue;
                    float rt = r.sTo > r.sFrom ? r.sTo : float.MaxValue, ot = o.sTo > o.sFrom ? o.sTo : float.MaxValue;
                    if (Math.Max(r.sFrom, o.sFrom) >= Math.Min(rt, ot)) continue;
                    if (Disjoint(r.profiles, o.profiles) || Disjoint(r.zones, o.zones)) continue;
                    e.Add(tag + ": shares a side and a stretch of '" + (r.corridor ?? "") + "' with rule '" + o.id + "'; give them disjoint sides, stations, profiles or zones");
                }
            }
        }

        static bool Disjoint(string a, string b)
        {
            if (string.IsNullOrWhiteSpace(a) || string.IsNullOrWhiteSpace(b)) return false;
            var sa = new HashSet<string>(a.Split(',').Select(x => x.Trim().ToLowerInvariant())); return !b.Split(',').Any(x => sa.Contains(x.Trim().ToLowerInvariant()));
        }
    }
}
