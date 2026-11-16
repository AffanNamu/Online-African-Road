#!/usr/bin/env python3
"""
KANO CITY (owner directive of 2026-11-16: build the actual city, not test roads). Generates the route `ng-kano-city` from the master plan's own road graph (docs/kano/city/city_plan.json,
KSA-1: 10 km x 10 km, local frame K0, x east, z north, metres) with the engine's road network primitives (roads, T joins, roundabouts):

  * the A2 expressway from the south-west edge through the D11 gateway to the ring road interchange (the main road: where a drive starts);
  * the RING ROAD, a 3.3 km circle round the old city, two-way, with roundabouts at 0, 90, 180, 225 (the A2 interchange) and 270 degrees and T junctions at every 22.5 degrees between;
  * the MIDDLE RING (an ellipse of 2.8 x 2.7 km) and the INNER RING along the old city wall (2.3 x 2.1 km), with gate roundabouts at 0, 90, 180 and 270 degrees;
  * the eight radial arterials from the ring road to the wall, crossing the middle ring, and the eight sub-radials from the ring road to the middle ring;
  * DISTRICT D06, the modern residential estate (R-006), built as a complete district in the band between the middle ring and the ring road from the 45 degree arterial to the 67.5 degree
    sub-radial: concentric estate streets about 95 m apart and cross streets about 150 m apart (blocks of about 150 x 85 m), lined with walled compounds (low-cost rows, bungalows,
    semi-detached houses, duplexes, villas and mansions), neighbourhood mosques, shops and fuel stations on its edges, street lights and trees.
  * DISTRICTS D05 (commercial belt), D08 (institutional corridor) and D09 (transport hub, with the bus terminal plate) in their sectors of the same band, built the same way; D10
    (industrial and logistics roads, with the logistics yard plate) and D11 (gateway services) on roads off the A2;
  * the MIXED URBAN QUARTERS (the plan's filler zone F-MID: modern residential, commercial frontage, institutional plots): two concentric streets and cross streets in every other
    22.5-degree sector between the middle ring and the ring road and in every 45-degree sector between the wall road and the middle ring. The D07 sector (157.5 to 202.5 degrees,
    the traditional suburb) stays empty: the plan marks it BLOCKED on references.
Every other road carries the city fabric of its place (the filler zones of the master plan): shops, houses, offices, mosques, fuel stations, warehouses by the A2. Inside the wall nothing is
built: the old city, Kurmi market and the landmarks wait for proper references (owner directive: no historical landmark is invented). Positions follow the master plan (INDICATIVE, not a
survey). Run: python3 tools/kano/make_city.py [--check]
"""
import json, math, sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent)); sys.path.insert(0, str(Path(__file__).resolve().parents[1] / "e5"))
import make_city_routes as C
import gen_e5_routes as E

ROOT = Path(__file__).resolve().parents[2]
PLAN = json.loads((ROOT / "docs" / "kano" / "city" / "city_plan.json").read_text())
ROUTE_ID = "ng-kano-city"
ALL_ROADS = []
CX, CZ = 0.0, -200.0
Y = 10.0
ELL = {"ring": (3300.0, 3300.0), "middle": (2800.0, 2700.0), "inner": (2300.0, 2100.0)}
NODE = {n["id"]: tuple(n["xz"]) for n in PLAN["nodes"]}

# ------------------------------------------------------------------------------------------------------------------------------------------------ cross-sections
e1 = json.loads((C.ROUTES / "ng-kano-city-e1.route.json").read_text())
PROF = {p["id"]: dict(p) for p in e1["profiles"]}                                 # kano_arterial (divided, planter median), kano_collector, kano_local, kano_service
PROF["kano_expressway"] = E.prof("kano_expressway", 2, 3.65, 2.5, 1.2, median=4.0, inner_shoulder=0.6, cls="expressway", centre="none", median_style="planter")
PROF["kano_ramp"] = E.prof("kano_ramp", 1, 4.2, 1.8, 1.2, 0.0, "none", 0.0, 0.0, "dashed", "yellow", cls="collector")          # the slip ramps of the A2 interchange (as F1's ramps)
for k in ("ring_large", "ring_modern", "ring_landmark"): PROF[k] = E.P[k]
E.P.update(PROF); E.G.update({k: E.geom(v) for k, v in PROF.items()})
HW = {k: E.G[k]["paved"] for k in PROF}                                            # paved half width: where a joining road's end sits


def cp(i, x, z, profile, zone="urban"):
    d = E.cp(i, i, x, z, Y, profile, zone); del d["name"]; return d                    # no name: it repeated the id, nothing reads it, and the city must fit the 1.5 MB / 6 MB Resources budget


# ------------------------------------------------------------------------------------------------------------------------------------------------ geometry
def ell(name, deg, f=None):
    """A point of a ring at a bearing (degrees from east, counter-clockwise). f in 0..1 interpolates between the middle ring (0) and the ring road (1) for the estate streets."""
    a, b = ELL[name] if f is None else (ELL["middle"][0] + f * (ELL["ring"][0] - ELL["middle"][0]), ELL["middle"][1] + f * (ELL["ring"][1] - ELL["middle"][1]))
    t = math.radians(deg); return (CX + a * math.cos(t), CZ + b * math.sin(t))


def tangent(name, deg, f=None):
    a, b = ELL[name] if f is None else (ELL["middle"][0] + f * 500.0, ELL["middle"][1] + f * 600.0)
    t = math.radians(deg); d = (-a * math.sin(t), b * math.cos(t)); l = math.hypot(*d); return (d[0] / l, d[1] / l)


def outward(name, deg, f=None):
    tx, tz = tangent(name, deg, f); return (tz, -tx)                                # right of counter-clockwise travel = away from the centre


def add(p, d, k): return (p[0] + d[0] * k, p[1] + d[1] * k)


def dist(p, q): return math.hypot(p[0] - q[0], p[1] - q[1])


def bearing_at(name, p, f=None):
    """The ring bearing whose point is nearest p (a few Newton-free refinements)."""
    a, b = ELL[name] if f is None else (2800.0 + f * 500.0, 2700.0 + f * 600.0)
    best = math.degrees(math.atan2((p[1] - CZ) / b, (p[0] - CX) / a))
    for step in (1.0, 0.1, 0.01, 0.001):
        best = min((best + k * step for k in range(-12, 13)), key=lambda d: dist(ell(name, d, f), p))
    return best


def angle_at_distance(name, deg0, r, direction, f=None):
    """The bearing beyond deg0 (direction +1 counter-clockwise, -1 clockwise) whose point lies r metres (straight line) from the point at deg0."""
    p0 = ell(name, deg0, f); lo, hi = 0.0, 30.0
    for _ in range(60):
        mid = (lo + hi) / 2
        if dist(ell(name, deg0 + direction * mid, f), p0) < r: lo = mid
        else: hi = mid
    return deg0 + direction * (lo + hi) / 2


def line_hits_ring(p, q, name, f=None):
    """Where the segment p-q crosses a ring (bisection on the sign of the ring's implicit function)."""
    a, b = ELL[name] if f is None else (2800.0 + f * 500.0, 2700.0 + f * 600.0)
    g = lambda x: ((x[0] - CX) / a) ** 2 + ((x[1] - CZ) / b) ** 2 - 1.0
    lo, hi = 0.0, 1.0; gl = g(p)
    for _ in range(60):
        mid = (lo + hi) / 2; m = (p[0] + (q[0] - p[0]) * mid, p[1] + (q[1] - p[1]) * mid)
        if (g(m) > 0) == (gl > 0): lo = mid
        else: hi = mid
    t = (lo + hi) / 2; return (p[0] + (q[0] - p[0]) * t, p[1] + (q[1] - p[1]) * t)


class Arc:
    """A road along a ring between two bearings (counter-clockwise), with control points about every 100 m and exactly at the bearings where other roads join it."""
    def __init__(self, rid, name, ring, d0, d1, profile, stops=(), f=None, zone="urban"):
        self.id, self.name, self.ring, self.f, self.profile, self.zone = rid, name, ring, f, profile, zone
        n = max(2, int(math.ceil(abs(d1 - d0) * math.pi / 180.0 * ELL["ring"][0] / 100.0)))
        angs = sorted(set([d0 + (d1 - d0) * i / n for i in range(n + 1)] + [s for s in stops if d0 + 0.01 < s < d1 - 0.01]))
        clean = [angs[0]]
        for a in angs[1:]:
            if dist(ell(ring, a, f), ell(ring, clean[-1], f)) < 22.0:                 # a stop close to a regular point replaces it (the engine wants 20 m between control points)
                if a in stops: clean[-1] = a
                continue
            clean.append(a)
        if clean[-1] != angs[-1]: clean[-1] = angs[-1]
        self.angles = clean; self.pts = [ell(ring, a, f) for a in clean]
        self.s = [0.0]
        for i in range(1, len(self.pts)): self.s.append(self.s[-1] + dist(self.pts[i - 1], self.pts[i]))
        self.joins = []

    def station(self, deg):
        i = min(range(len(self.angles)), key=lambda k: abs(self.angles[k] - deg)); return self.s[i]

    def spec(self):
        return dict(id=self.id, name=self.name, role="road", controlPoints=[cp("%s_%d" % (self.id, i), x, z, self.profile, self.zone) for i, (x, z) in enumerate(self.pts)], decks=[], joins=self.joins)


def straight(rid, name, pts, profile, joins=(), zone="urban"):
    return dict(id=rid, name=name, role="road", controlPoints=[cp("%s_%d" % (rid, i), x, z, profile, zone) for i, (x, z) in enumerate(pts)], decks=[], joins=list(joins))


def join(end, corridor, s, side, gap=14.0): return dict(end=end, corridor=corridor, s=round(s, 2), side=side, gap=gap)


# ------------------------------------------------------------------------------------------------------------------------------------------------ the network
RING_RBT = {0: "rr_e", 90: "rr_n", 180: "rr_w", 225: "rr_a2", 270: "rr_s"}           # ring road roundabouts (225: the A2 interchange of the plan: the A2 flies over it, four ramps)
GATE_RBT = {0: "gate_e", 90: "gate_n", 180: "gate_w", 270: "gate_s"}                 # roundabouts where the radials meet the wall road
RING_DESIGN = dict(design="large", profile="ring_large", radius=30.0, apron=0.0, island="planter", splitter=(22.0, 4.0), flare=0.0, mouth=1.8)
A2_RING_DESIGN = dict(RING_DESIGN, radius=45.0)                                     # the A2 interchange: a larger ground roundabout under the flyover (six legs: two ring-road arcs, four slip ramps)
KIT_LIGHT = {"highway_light": "aro_streetlight_double_median_a", "street_light": "aro_streetlight_arm_single_a",         # the transport kit's street lights (family C), with their night
             "street_light_village": "aro_streetlight_ornamental_a"}                                                      # light heads, instead of the code-built placeholder lamps
DECK_Y = 17.8                                                                        # the A2 deck over the interchange roundabout (7.8 m over the ground, as F1)
GATE_DESIGN = dict(design="modern", profile="ring_modern", radius=24.0, apron=2.0, island="planter", splitter=(14.0, 3.0), flare=0.0, mouth=1.9)
ESTATE_F = [0.18, 0.345, 0.51, 0.675]                                                # the estate's concentric streets between the middle ring (0) and the ring road (1): about 92 m apart, so
                                                                                     # two rows of compounds fit back to back, and the outer rows clear the shops of both rings
JUNCTION_CLEAR = 58.0                                                                # buildings of a road keep this far (along it) from a junction on it: corner lots of two roads never meet
CORNER_CLEAR = 20.0                                                                  # a shallower road's window round a junction: clear of the joining road's formation and mouth (see fabric)
END_CLEAR = 52.0                                                                     # and this far from a joined or roundabout end
EST_A0, EST_A1 = 45.0, 67.5                                                          # the estate lies between the 45 degree arterial and the 67.5 degree sub-radial


def rout_of(design): return E.ring_dims(design["profile"], design["radius"])[1]


def ring_spec(rid, name, centre, design, connect, island_props=()):
    rc, rout = E.ring_dims(design["profile"], design["radius"])
    y = round(Y - E.G[design["profile"]]["edge_drop"], 4)
    return dict(id=rid, name=name, design=design["design"], profile=design["profile"], x=round(centre[0], 3), z=round(centre[1], 3), elevation=y, attachTo="", attachEnd="end",
                radius=design["radius"], apron=design["apron"], island=design["island"], zone="urban", surface="good", flare=design["flare"], mouthScale=design["mouth"],
                splitterLength=design["splitter"][0], splitterWidth=design["splitter"][1], yieldLines=True, zebra=True, legs=[], connect=list(connect),
                props=[dict(id=pid, prop=pr, text="", x=round(px, 3), z=round(pz, 3), yawDeg=yaw, scale=1.0) for pid, pr, px, pz, yaw in island_props])


def build():
    r = E.base_route(ROUTE_ID, "Kano City (KSA-1 master plan): A2 gateway, ring road, middle and wall rings, radial arterials, districts D05, D06, D08, D09, D10, D11 and the mixed urban quarters",
                     "KANO CITY. The actual city on the master plan's 10 x 10 km frame (KSA-1, docs/KANO_CITY_MASTER_PLAN.md): the A2 into the ring road through the D11 gateway and the D10 "
                     "industrial roads, the ring road with its roundabouts, the middle ring and the road along the old city wall, the radial arterials, the districts D05 (commercial belt), "
                     "D06 (modern residential estate), D08 (institutional corridor) and D09 (transport hub, bus terminal), and the plan's mixed urban quarters (F-MID) with their own streets "
                     "everywhere between the wall and the ring road. Not built yet: inside the wall (the old city, Kurmi market and the landmarks wait for proper references) and the D07 "
                     "traditional suburb (blocked on references). Positions are the plan's (indicative, not survey).", 6201, dict(E.TERRAIN))
    r = E.fix_meta(r)
    r["meta"].update({"from": "A2 from Zaria (city edge)", "to": "Kano city", "status": "planned", "elevationSource": "design (near-flat city)", "notes": "The Kano city route (owner directive 2026-11-16). docs/KANO_CITY.md."})
    r["traffic"] = dict(densityScale=0.8, speedLimitScale=1.0, vehicleMix=["saloon_car", "minibus", "motorcycle", "tricycle_taxi", "truck"])
    # traffic (the engine's existing 1-D model) runs along the main road: the A2 from the gateway, over the flyover to the middle ring roundabout. Traffic through junctions and round
    # the roundabouts is new engine behaviour: proposed in docs/TRAFFIC_NETWORK_PROPOSAL.md, waiting for the owner's approval.
    roads, rings = [], []
    ring_r = rout_of(RING_DESIGN); gate_r = rout_of(GATE_DESIGN); a2_r = rout_of(A2_RING_DESIGN); mid_r = rout_of(GATE_DESIGN)
    ring_r_at = lambda a: a2_r if a % 360.0 == 225.0 else ring_r

    # ---- the ring road: arcs between its roundabouts, control points at every T bearing and at the estate's cross streets
    est_cross = estate_cross_bearings()
    outer_cross = sorted(est_cross["ring"] + [b for d in DISTRICTS if d.get("band", "outer") == "outer" for b in district_bearings(d)])
    inner_cross = sorted(b for d in DISTRICTS if d.get("band") == "inner" for b in district_bearings(d))
    ring_bounds = sorted(RING_RBT)                                                  # 0, 90, 180, 225, 270
    ring_arcs = []
    for i, a0 in enumerate(ring_bounds):
        a1 = ring_bounds[(i + 1) % len(ring_bounds)] + (360.0 if i + 1 == len(ring_bounds) else 0.0)
        d0 = angle_at_distance("ring", a0, ring_r_at(a0), +1); d1 = angle_at_distance("ring", a1, ring_r_at(a1), -1)
        stops = [a0 + 22.5 * k for k in range(1, 8) if a0 + 22.5 * k < a1] + [b for b in outer_cross if a0 < b < a1] + [b + 360.0 for b in outer_cross if a0 < b + 360.0 < a1]
        arc = Arc("ring_%03d_%03d" % (a0, a1 % 360), "Ring Road (%d to %d degrees)" % (a0, a1 % 360), "ring", d0, d1, "kano_arterial", stops)
        ring_arcs.append(arc)
    def ring_arc_at(deg):
        deg %= 360.0
        for a in ring_arcs:
            lo, hi = a.angles[0] % 360.0, a.angles[-1]
            if lo <= deg <= hi or lo <= deg + 360.0 <= hi: return a, (deg if lo <= deg <= hi else deg + 360.0)
        raise ValueError(deg)

    # ---- the middle ring: arcs between the radials (T joins at both ends), control points at the sub-radials and the estate cross streets
    mid_arcs = []
    for k in range(8):
        a0, a1 = 45.0 * k, 45.0 * (k + 1)
        d0 = angle_at_distance("middle", a0, mid_r if a0 == 225.0 else HW["kano_arterial"] + 0.3, +1); d1 = angle_at_distance("middle", a1, mid_r if a1 == 225.0 else HW["kano_arterial"] + 0.3, -1)
        arc = Arc("mid_%03d" % a0, "Middle ring (%d to %d degrees)" % (a0, a1), "middle", d0, d1, "kano_collector", [a0 + 22.5] + [b for b in outer_cross + inner_cross if a0 < b < a1])
        mid_arcs.append(arc)

    # ---- the road along the wall: four arcs between the gate roundabouts, control points where the 45-degree radials join
    inner_arcs = []
    for k in range(4):
        a0, a1 = 90.0 * k, 90.0 * (k + 1)
        d0 = angle_at_distance("inner", a0, gate_r, +1); d1 = angle_at_distance("inner", a1, gate_r, -1)
        inner_arcs.append(Arc("wall_%03d" % a0, "Road along the old city wall (%d to %d degrees)" % (a0, a1), "inner", d0, d1, "kano_collector", [a0 + 45.0] + [b for b in inner_cross if a0 < b < a1]))

    # ---- radial arterials (ring road -> middle ring -> wall)
    radials = []
    for k in range(8):
        a = 45.0 * k; rr, g = ell("ring", a), ell("inner", a); u = ((g[0] - rr[0]) / dist(rr, g), (g[1] - rr[1]) / dist(rr, g)); ma = line_hits_ring(rr, g, "middle")
        joins = []
        if a in RING_RBT: start = add(rr, u, ring_r)
        else:
            arc, deg = ring_arc_at(a); start = add(rr, outward("ring", a), -HW["kano_arterial"]); joins.append(join("start", arc.id, arc.station(deg), -1, 18.0))
        if a in GATE_RBT: end = add(g, u, -gate_r)
        else:
            arc = inner_arcs[int(a // 90)]; end = add(g, outward("inner", a), HW["kano_collector"]); joins.append(join("end", arc.id, arc.station(a), +1, 18.0))
        rid = "radial_%03d" % a
        if a == 225.0:                                                               # the A2 continues as this radial's outer part (over the flyover) and ends at a roundabout on the middle ring
            mm = ell("middle", a); s0 = add(mm, u, mid_r)
            radials.append(straight(rid, "Radial arterial %d degrees (from the A2's middle ring roundabout)" % a, [s0, ((s0[0] + end[0]) / 2, (s0[1] + end[1]) / 2), end], "kano_arterial", [j for j in joins if j["end"] == "end"]))
            continue
        radials.append(straight(rid, "Radial arterial %d degrees" % a, [start, ma, end], "kano_arterial", joins))
        # the middle ring arcs end on this radial: the arc leaving counter-clockwise is on its right, the arc arriving on its left
        L = dist(start, ma)
        mid_arcs[k].joins.append(join("start", rid, L, +1)); mid_arcs[k - 1].joins.append(join("end", rid, L, -1))

    # ---- sub-radials (ring road at 22.5 + 45k -> middle ring)
    subs = []
    for k in range(8):
        a = 22.5 + 45.0 * k; rr = ell("ring", a); mm = ell("middle", a)
        arc, deg = ring_arc_at(a); start = add(rr, outward("ring", a), -HW["kano_arterial"]); end = add(mm, outward("middle", a), HW["kano_collector"])
        subs.append(straight("sub_%04d" % round(a * 10), "Sub-radial %.1f degrees" % a, [start, end], "kano_collector",
                             [join("start", arc.id, arc.station(deg), -1), join("end", mid_arcs[k].id, mid_arcs[k].station(a), +1)]))

    # ---- the A2 (main road): from the study area's south-west edge through the gateway to the A2 roundabout on the ring road
    a2c = ell("ring", 225.0); last = NODE["X_LOG"]
    ix = a2_interchange(a2c, last, a2_r, mid_r)
    r["controlPoints"] = ix["cps"]; r["bridges"] = [ix["deck"]]
    a2_len = ix["main_len"]; a2_end = ix["xz"][3]
    global MAIN_PSEUDO; MAIN_PSEUDO = dict(id=ROUTE_ID, controlPoints=ix["cps"])
    r["waypoints"] = [dict(id="wp_a2", name="A2 from Zaria", kind="district", s=0.0), dict(id="wp_gateway", name="Kano gateway (D11)", kind="district", s=round(dist(NODE["X_T1"], NODE["X_GW"]), 1)),
                      dict(id="wp_ring", name="Ring Road flyover", kind="district", s=round(ix["s_c"], 1)), dict(id="wp_middle", name="Middle ring", kind="district", s=round(a2_len, 1))]

    # ---- district D06: the modern residential estate
    est_roads, est_frontage = estate(ring_arc_at, mid_arcs, radials, subs, est_cross)
    # ---- the other ready districts in their sectors (D05 commercial belt, D08 institutional corridor, D09 transport hub) and the plan's mixed urban filler (F-MID) everywhere else
    #      between the wall and the ring road, except the D07 sector (traditional suburb, blocked on references)
    for d in DISTRICTS:
        rds, fr, st = district(d, ring_arc_at, mid_arcs, inner_arcs, radials, subs)
        est_roads += rds; est_frontage += fr; r["sites"] += st
    # ---- along the A2: D11 gateway services and D10 industrial and logistics roads
    rds, fr, st = a2_districts(ix["xz"][:4])
    est_roads += rds; est_frontage += fr; r["sites"] += st
    # ---- beyond the ring road: the plan's peri-urban fringe (F-OUT) along roads leaving the city from the ring road roundabouts
    rds, fr = fringe(ring_r)
    est_roads += rds; est_frontage += fr

    # ---- roundabouts
    def ends_at(rd_list, centre, rad):
        out = []
        for rd in rd_list:
            c = rd["controlPoints"]
            if dist((c[0]["x"], c[0]["z"]), centre) < rad + 1.0 or dist((c[-1]["x"], c[-1]["z"]), centre) < rad + 1.0: out.append(rd["id"])
        return out
    all_roads = [a.spec() for a in ring_arcs] + [a.spec() for a in mid_arcs] + [a.spec() for a in inner_arcs] + radials + subs + est_roads + ix["ramps"]
    for a, rid in RING_RBT.items():
        c = ell("ring", a); con = ends_at(all_roads, c, ring_r_at(a))
        if a == 225:                                                                  # trees of the island, none under the deck
            props = [("t%d" % i, "tropical_tree", 28 * math.cos(math.radians(15 * i)), 28 * math.sin(math.radians(15 * i)), 0) for i in range(24)
                     if abs(28 * math.cos(math.radians(15 * i)) * ix["R"][0] + 28 * math.sin(math.radians(15 * i)) * ix["R"][1]) > 18.0]
            rings.append(ring_spec(rid, "Ring Road A2 interchange roundabout (under the A2 flyover)", c, A2_RING_DESIGN, con, props))
            continue
        props = [("t%d" % i, "tropical_tree", 16 * math.cos(math.radians(30 * i)), 16 * math.sin(math.radians(30 * i)), 0) for i in range(12)]
        rings.append(ring_spec(rid, "Ring Road roundabout %d degrees" % a, c, RING_DESIGN, con, props))
    c = ell("middle", 225.0); con = ends_at(all_roads, c, mid_r) + [ROUTE_ID]
    props = [("p%d" % i, "palm_tree", 9 * math.cos(math.radians(45 * i)), 9 * math.sin(math.radians(45 * i)), 0) for i in range(8)]
    rings.append(ring_spec("mid_a2", "Middle ring roundabout at the end of the A2", c, GATE_DESIGN, con, props))
    for a, rid in GATE_RBT.items():
        c = ell("inner", a); con = ends_at(all_roads, c, gate_r)
        props = [("p%d" % i, "palm_tree", 9 * math.cos(math.radians(45 * i)), 9 * math.sin(math.radians(45 * i)), 0) for i in range(8)]
        rings.append(ring_spec(rid, "Wall road roundabout %d degrees" % a, c, GATE_DESIGN, con, props))
    r["roads"] = all_roads; r["rings"] = rings
    r["profiles"] = [PROF[k] for k in sorted(PROF)]
    # lights round every roundabout
    for g in rings:
        prof = g["profile"]; rc = g["radius"] + E.G[prof]["formation"]; n_l = max(6, int(round(2 * math.pi * rc / 32.0))); L = 2 * math.pi * rc
        for k in range(n_l):
            r["props"].append(dict(id="%s_light%d" % (g["id"], k), prop=KIT_LIGHT["street_light"], text="", corridor=g["id"], s=round((k + 0.5) * L / n_l, 2), lateral=round(E.G[prof]["formation"] + 0.6, 2), yawDeg=-90.0, scale=1.0))
    # street lights, palms, kiosks, bus stops by cross-section (the E1 rules), and the expressway's median lights
    # the E1 street rules, on EVERY road of the city (allRoads: lights, palms on the arterials, kiosks, bus stops), and street trees of the environment kit on the city streets
    base = dict(e1["propRules"][0])
    r["propRules"] = [dict(p, allRoads=True) for p in e1["propRules"]] + [dict(base, id="lights_expressway_median", profiles="kano_expressway", allRoads=False)] + [
        dict(base, id=rid, prop=prop, profiles=prof, side="both", offsetMin=omin, offsetMax=omax, spacingMin=smin, spacingMax=smax, presence=pres, scaleMin=0.85, scaleMax=1.15,
             yawJitterDeg=180.0, faceRoad=False, fixedDensity=False, exclusionMargin=-1.0, allRoads=True)
        for rid, prop, prof, omin, omax, smin, smax, pres in [
            ("trees_local_a", "rg_ngn_tree_street_broadleaf_a", "kano_local", 1.8, 3.2, 26.0, 38.0, 0.7),
            ("trees_local_b", "rg_ngn_tree_street_broadleaf_b", "kano_local", 1.8, 3.2, 31.0, 45.0, 0.5),
            ("trees_collector", "rg_ngn_tree_large_shade_a", "kano_collector", 2.0, 3.4, 55.0, 85.0, 0.6),
            ("trees_service", "rg_ngn_tree_street_broadleaf_b", "kano_service", 1.8, 3.2, 40.0, 60.0, 0.45)]] + [
        # family K: three parked cars (one asset, lengthwise, so facing the road parks them parallel to the kerb) on the right kerb of the neighbourhood streets; lights and trees take the left
        dict(base, id="cars_parked", prop="aro_car_parked_set_a", profiles="kano_local", side="right", offsetMin=1.05, offsetMax=1.25, spacingMin=55.0, spacingMax=95.0, presence=0.4,
             scaleMin=1.0, scaleMax=1.0, yawJitterDeg=0.0, faceRoad=True, fixedDensity=False, exclusionMargin=0.3, clearLength=16.0, clearDepth=1.4, allRoads=True)]     # the body reaches 2.25 m from the road edge; people at a lot front stand 2.8 m or more out (setbacks >= 5 m)
    r["propRules"].append(dict(base, id="trees_fields", prop="rg_ngn_tree_large_shade_a", zones="rural", profiles="", side="both", offsetMin=55.0, offsetMax=90.0, spacingMin=30.0, spacingMax=55.0,
                               presence=0.55, scaleMin=0.8, scaleMax=1.25, yawJitterDeg=180.0, faceRoad=False, fixedDensity=False, exclusionMargin=-1.0, allRoads=True))      # farm trees of the fringe
    # bus shelters (the transport kit's real model, tagged bus_stop: the crowd seats people waiting under it) instead of the E1 placeholder rule, whose 1 to 1.6 m offset lies inside
    # the default 3 m clearance and so placed nothing. Behind the kerb lamps (0.6 to 1 m out) with the front of the shelter at 1.2 m or more, the back short of the 5 m setback of the
    # lots, so nobody at a shop front stands in it; on the collectors on the right kerb only (their shade trees stand on the left); never across a junction's mouth (clearLength).
    r["propRules"] = [p for p in r["propRules"] if p["id"] not in ("busstops_collector", "kiosks_collector")]     # kiosks: a 3 m box fits nowhere between the lamps and the shop-front people
    for rid, prof, side, smin, smax, pres in [("shelters_collector", "kano_collector", "right", 180.0, 260.0, 0.85), ("shelters_arterial", "kano_arterial", "both", 250.0, 350.0, 0.8)]:
        r["propRules"].append(dict(base, id=rid, prop="aro_bus_shelter_a", profiles=prof, side=side, offsetMin=2.2, offsetMax=2.5, spacingMin=smin, spacingMax=smax, presence=pres,
                                   scaleMin=1.0, scaleMax=1.0, yawJitterDeg=0.0, faceRoad=True, fixedDensity=False, exclusionMargin=1.0, clearLength=5.7, clearDepth=2.0, allRoads=True))
    lights_service = next(p for p in r["propRules"] if p["id"] == "lights_service")
    r["propRules"].append(dict(lights_service, id="lights_ramp", profiles="kano_ramp", allRoads=True))                # the A2 interchange's slip ramps
    for p in r["propRules"]:
        p["prop"] = KIT_LIGHT.get(p["prop"], p["prop"])                                           # the transport kit's real street lights instead of the placeholder lamps
        if p["id"].startswith("trees_") and p["id"] != "trees_arterial": p["side"] = "left"       # the palms of the arterials keep both sides (no parked cars there)
        if p["id"] == "lights_local": p["side"] = "left"                                          # the neighbourhood streets: lights and trees on the left kerb, parked cars on the right
    global ALL_ROADS; ALL_ROADS = all_roads
    fab_roads = [(a.id, a.s[-1], "ring") for a in ring_arcs] + [(a.id, a.s[-1], "middle") for a in mid_arcs] + [(a.id, a.s[-1], "wall") for a in inner_arcs] + \
                [(x["id"], rd_len(x), "radial") for x in radials] + [(x["id"], rd_len(x), "sub") for x in subs] + \
                [(ROUTE_ID, a2_len, "main", 0.0, ix["s_in"]), (ROUTE_ID, a2_len, "radial", ix["s_out"], a2_len)]
    r["frontage"] = fabric(fab_roads, a2_len) + est_frontage
    total = a2_len + sum(rd_len(x) for x in all_roads)
    r["meta"]["gameLengthKm"] = round(a2_len / 1000.0, 2)                            # the main road; the network's total is in the doc
    r["meta"]["networkKm"] = round(total / 1000.0, 1)
    return r


def rd_len(rd):
    c = rd["controlPoints"]; return sum(dist((c[i - 1]["x"], c[i - 1]["z"]), (c[i]["x"], c[i]["z"])) for i in range(1, len(c)))


# ------------------------------------------------------------------------------------------------------------------------------------------------ D06
def estate_cross_bearings():
    """Bearings of the estate's cross streets on each ring they touch: about every 150 m of arc between the 45 degree arterial and the 67.5 degree sub-radial, clear of both."""
    n = 8; margin = 3.0
    bs = [EST_A0 + margin + (EST_A1 - EST_A0 - 2 * margin) * (i + 0.5) / n for i in range(n)]
    return {"ring": bs, "middle": bs, "estate": bs}


def estate(ring_arc_at, mid_arcs, radials, subs, cross):
    """The modern residential estate: concentric streets (joined to the 45 degree arterial and the 67.5 degree sub-radial) and cross streets (joined to the street or ring inside and
    outside them). Returns the roads and the frontage rules."""
    rad45 = next(x for x in radials if x["id"] == "radial_045"); sub67 = next(x for x in subs if x["id"] == "sub_0675")
    def pts(rd): return [(c["x"], c["z"]) for c in rd["controlPoints"]]
    def station_on(rd, p):
        ps = pts(rd); best, s, acc = None, 0.0, 0.0
        for i in range(1, len(ps)):
            a, b = ps[i - 1], ps[i]; L = dist(a, b); t = max(0.0, min(1.0, ((p[0] - a[0]) * (b[0] - a[0]) + (p[1] - a[1]) * (b[1] - a[1])) / (L * L))); q = (a[0] + (b[0] - a[0]) * t, a[1] + (b[1] - a[1]) * t)
            d = dist(p, q)
            if best is None or d < best: best, s = d, acc + L * t
            acc += L
        return s
    roads, streets = [], []
    p45, p67 = pts(rad45), pts(sub67)
    for i, f in enumerate(ESTATE_F):
        # the street meets the 45 degree arterial (between its start on the ring road and the middle ring) and the 67.5 degree sub-radial
        x0 = seg_hit(p45[0], p45[1], f); x1 = seg_hit(p67[0], p67[1], f)
        d0 = bearing_at(None, x0, f); d1 = bearing_at(None, x1, f)
        d0 = angle_at_distance(None, d0, HW["kano_arterial"] + 0.3, +1, f); d1 = angle_at_distance(None, d1, HW["kano_collector"] + 0.3, -1, f)
        arc = Arc("d06_street_%d" % (i + 1), "Estate street %d (D06)" % (i + 1), None, d0, d1, "kano_local", cross["estate"], f=f)
        arc.joins = [join("start", rad45["id"], station_on(rad45, x0), +1), join("end", sub67["id"], station_on(sub67, x1), -1)]
        streets.append(arc)
    # cross streets: from the middle ring to street 1, between consecutive streets, from street 5 to the ring road
    for k, b in enumerate(cross["estate"]):
        levels = [("middle", None)] + [(None, f) for f in ESTATE_F] + [("ring", None)]
        for j in range(len(levels) - 1):
            (n0, f0), (n1, f1) = levels[j], levels[j + 1]
            inner = mid_arcs[1] if n0 == "middle" else streets[j - 1]
            outer = streets[j] if j < len(ESTATE_F) else ring_arc_at(b)[0]
            hw_in = HW["kano_collector"] if n0 == "middle" else HW["kano_local"]; hw_out = HW["kano_arterial"] if n1 == "ring" else HW["kano_local"]
            a_in, a_out = ell(n0, b, f0), ell(n1, b, f1)                               # n is the ring's name, or None with f for an estate street
            o_in, o_out = outward(n0, b, f0), outward(n1, b, f1)
            p0 = add(a_in, o_in, hw_in); p1 = add(a_out, o_out, -hw_out)
            s_in = inner.station(b) if isinstance(inner, Arc) else 0.0; s_out = outer.station(b if n1 != "ring" else ring_arc_at(b)[1])
            rid = "d06_cross_%d_%d" % (k + 1, j)
            roads.append(straight(rid, "Estate cross street (D06)", [p0, ((p0[0] + p1[0]) / 2, (p0[1] + p1[1]) / 2), p1], "kano_local",
                                  [join("start", inner.id, s_in, +1, 10.0), join("end", outer.id, s_out, -1, 10.0)]))
    est = [a.spec() for a in streets] + roads
    rule = C.rule
    lowcost = [l for l in C.TRADITIONAL if l["tag"] in ("lowcost", "house")] + [l for l in C.MIDDLE if l["tag"] in ("bungalow", "semi_detached")]
    front = []
    no_mansion = [l for l in C.PREMIUM if l["tag"] != "mansion"]                    # a mansion's 36.6 m compound needs the deep plots on the estate's outer edge
    homes = [l for l in C.MIDDLE if l["tag"] != "school"]                          # a 36 m school lot does not fit back to back between estate streets 92 m apart (schools stand on the rings and collectors)
    plan = [(lowcost, "both"), (homes, "both"), (homes + no_mansion, "both"), (no_mansion, "left"), (C.PREMIUM, "right")]
    for n, (mix, side) in enumerate(plan):
        i = min(n, 3); a = streets[i]
        front.append(rule("d06_street_%d%s" % (i + 1, "" if side == "both" else "_" + side), mix, C.LOCAL_MOSQUES if n % 2 == 0 else None, 4 if n % 2 == 0 else 0, corridor=a.id, side=side,
                          s_from=END_CLEAR, s_to=a.s[-1] - END_CLEAR, setback=(5.0, 7.0), gap=(1.0, 3.0), block=150.0, presence=0.95))      # 5 m: room for the parked cars at the kerb in front of the gates
    return est, front


# ------------------------------------------------------------------------------------------------------------------------------------------------ the A2 interchange
MAIN_PSEUDO = None


def a2_interchange(c, last, a2_r, mid_r):
    """The A2 flyover over the ring road (F1's verified layout, compressed to the 552 m between the ring road and the middle ring at 225 degrees): the A2 rises at 2.7 percent, crosses
    the interchange roundabout on a deck 7.8 m up (piers every 24 m, none over the ring carriageway), comes down at 6 percent and runs on the ground to a roundabout on the middle ring.
    Four ground-level slip ramps make a diamond: inbound off (before the rise, right side) and on (after the descent, right side), outbound off and on on the left side. Local frame: z along
    the A2's travel at the roundabout, x to its right."""
    mm = ell("middle", 225.0); a_in = math.atan2(c[1] - last[1], c[0] - last[0]); a_rad = math.atan2(mm[1] - c[1], mm[0] - c[0])
    t = (a_in + a_rad) / 2; F = (math.cos(t), math.sin(t)); R = (F[1], -F[0])
    L = lambda x, z: (c[0] + R[0] * x + F[0] * z, c[1] + R[1] * x + F[1] * z)
    v = ((mm[0] - c[0]) / dist(c, mm), (mm[1] - c[1]) / dist(c, mm))
    z_in, z_up, z_top1, z_down, z_out = -430.0, -110.0, 80.0, 250.0, 280.0
    head = [NODE["X_T1"], NODE["X_GW"], last, L(0, -600)]
    xz = head + [L(0, z_in), L(0, z_up), L(0, z_top1), L(0, z_down), L(0, z_out), add(mm, v, -(mid_r + 100.0)), add(mm, v, -mid_r)]
    ys = [Y, Y, Y, Y, Y, DECK_Y, DECK_Y, Y, Y, Y, Y]
    cps = [dict(cp("a2_%d" % i, x, z, "kano_expressway", "highway" if i <= 4 else "urban"), elevation=round(y, 3)) for i, ((x, z), y) in enumerate(zip(xz, ys))]   # an expressway up to the interchange (traffic at 90 km/h), urban beyond
    s_at = [0.0]
    for i in range(1, len(xz)): s_at.append(s_at[-1] + dist(xz[i - 1], xz[i]))
    s600 = s_at[3]; S = lambda z: s600 + 600.0 + z; s_c = S(0.0)
    rc, rout = E.ring_dims(A2_RING_DESIGN["profile"], A2_RING_DESIGN["radius"]); f = E.G[A2_RING_DESIGN["profile"]]["formation"]; ring_in, ring_out = rc - f, rc + f
    gaps = [{"from": round(s_c - (ring_out + 4.0), 1), "to": round(s_c - (ring_in - 4.0), 1)}, {"from": round(s_c + (ring_in - 4.0), 1), "to": round(s_c + (ring_out + 4.0), 1)}]
    deck = dict(id="a2_flyover", name="A2 flyover over the Ring Road interchange roundabout", startS=round(S(-150.0), 1), endS=round(S(85.0), 1), valleyDepth=0.0, deckThickness=1.2,
                pierSpacing=24.0, pierGaps=gaps, overRoad=True)
    pav = HW["kano_expressway"]; at = lambda deg, r_: (r_ * math.cos(math.radians(deg)), r_ * math.sin(math.radians(deg)))
    def ramp(rid, name, pts, start_join=None, end_join=None):
        wpts = [L(x, z) for x, z in pts]; joins = []
        if start_join is not None: joins.append(join("start", ROUTE_ID, S(start_join[0]), start_join[1], 30.0))
        if end_join is not None: joins.append(join("end", ROUTE_ID, S(end_join[0]), end_join[1], 30.0))
        return dict(id=rid, name=name, role="ramp", controlPoints=[cp("%s_%d" % (rid, i), x, z, "kano_ramp") for i, (x, z) in enumerate(wpts)], decks=[], joins=joins)
    off = [(pav, z_in), (26.0, -390.0), (44.0, -330.0), (52.0, -260.0), at(290, rout + 100.0), at(290, rout + 45.0), at(290, rout)]
    on = [at(70, rout), at(70, rout + 45.0), at(70, rout + 100.0), (52.0, 200.0), (36.0, 245.0), (pav, z_out)]
    mirror = lambda pts: [(-x, z) for x, z in pts]
    ramps = [ramp("a2_ramp_in_off", "A2 inbound off ramp (to the Ring Road roundabout)", off, start_join=(z_in, +1)),
             ramp("a2_ramp_in_on", "A2 inbound on ramp (from the Ring Road roundabout)", on, end_join=(z_out, +1)),
             ramp("a2_ramp_out_off", "A2 outbound off ramp (to the Ring Road roundabout)", mirror(list(reversed(on))), start_join=(z_out, -1)),
             ramp("a2_ramp_out_on", "A2 outbound on ramp (from the Ring Road roundabout)", mirror(list(reversed(off))), end_join=(z_in, -1))]
    return dict(cps=cps, xz=xz, main_len=s_at[-1], s_c=s_c, s_in=S(z_in), s_out=S(z_out), deck=deck, ramps=ramps, R=R, F=F)


# ------------------------------------------------------------------------------------------------------------------------------------------------ the other districts
E1_SITES = {x["id"]: x for x in e1["sites"]}                                          # the logistics yard and bus terminal plates of the E2 kit, reused as district sites
INSTITUTIONAL = [C.L("kano_hospital_general_a", 0.7, "hospital"), C.L("kano_school_a", 1.4, "school"), C.L("kano_government_offices_a", 1.2, "government"),
                 C.L("kano_police_division_a", 0.6, "police"), C.L("kano_hotel_a", 0.5, "hotel"), C.L("rg_ngn_clinic_a", 0.6, "clinic"),
                 C.L("kano_university_faculty_a", 0.6, "university")]                                     # family K (2026-11-16): a university faculty campus
MARKET = [C.L("kano_market_neighbourhood_a", 0.8, "market")]                                           # family K: the neighbourhood market (generic; Kurmi stays pending)
ROADSIDE = [C.L("rg_ngn_roadside_stalls_a", 0.9, "stalls"), C.L("rg_ngn_container_shops_a", 0.9, "container_shops")]   # family K: the informal roadside trade of the shop streets
KEKE = [C.L("kano_keke_rank_a", 0.35, "keke_rank")]                                                      # family K: a keke (tricycle taxi) rank, tricycles waiting for passengers on the busy roads
DISTRICTS = [
    dict(id="d05", name="Commercial belt (D05)", a0=22.5, a1=45.0, start="sub_0225", end="radial_045", fs=[0.2, 0.45, 0.7], profile="kano_collector", cross=6,
         plan=[(0, "COMMERCIAL+MARKET+ROADSIDE+KEKE", "both", "NEIGHBOURHOOD", 3, (6.0, 10.0), 140.0, 0.95), (1, "COMMERCIAL+MAJOR", "both", "NEIGHBOURHOOD", 4, (8.0, 12.0), 150.0, 0.95),
               (2, "MAJOR", "both", "GRAND", 3, (8.0, 12.0), 150.0, 0.95)]),
    dict(id="d08", name="Institutional corridor (D08)", a0=67.5, a1=90.0, start="sub_0675", end="radial_090", fs=[0.25, 0.6], profile="kano_collector", cross=5,
         plan=[(0, "INSTITUTIONAL", "both", "LOCAL_MOSQUES", 4, (10.0, 16.0), 160.0, 0.85), (1, "INSTITUTIONAL", "both", None, 0, (10.0, 16.0), 160.0, 0.85)]),
    dict(id="d09", name="Transport hub (D09)", a0=202.5, a1=225.0, start="sub_2025", end="radial_225", fs=[0.16, 0.40], profile="kano_collector", cross=5,
         plan=[(0, "TRANSPORT+KEKE", "right", "NEIGHBOURHOOD", 4, (8.0, 12.0), 140.0, 0.9), (1, "TRANSPORT+COMMERCIAL+KEKE", "both", None, 0, (8.0, 12.0), 140.0, 0.9)],     # street 1 is 88 m from the middle ring: its inner side faces the ring's own buildings
         site=("site_terminal", 1, 40.0)),
]


# F-MID, the plan's "mixed urban filler" between the wall and the ring road outside the hero districts: modern residential, commercial frontage and institutional plots
# (docs/kano/city/city_plan.json fillerZones). Outer band: every 22.5-degree sector not taken by a district, except 157.5..202.5 (D07, the traditional suburb, BLOCKED on references).
# Inner band (between the wall road and the middle ring): every 45-degree sector between two radials.
D07_SECTOR = (157.5, 202.5)
_taken = [(d["a0"], d["a1"]) for d in DISTRICTS] + [(EST_A0, EST_A1), D07_SECTOR]
_bounds_outer = lambda a: ("radial_%03d" % (a % 360)) if (a % 45.0) == 0.0 else ("sub_%04d" % round((a % 360) * 10))
_q = 0
for k in range(16):
    a0, a1 = 22.5 * k, 22.5 * (k + 1)
    if any(t0 <= a0 < t1 for t0, t1 in _taken): continue
    _q += 1
    DISTRICTS.append(dict(id="fo%02d" % _q, band="outer", name="Mixed urban quarter %d (F-MID)" % _q, a0=a0, a1=a1, start=_bounds_outer(a0), end=_bounds_outer(a1), fs=[0.16, 0.40] if a0 == 225.0 else [0.27, 0.62],
                          profile="kano_local", cross=4,
                          plan=[(0, "MIDDLE", "right" if a0 == 225.0 else "both", "LOCAL_MOSQUES", 4, (5.0, 9.0), 120.0, 0.9),
                                (1, "MIDDLE+COMMERCIAL" if _q % 2 else "COMMERCIAL+MARKET+ROADSIDE", "both", "NEIGHBOURHOOD" if _q % 2 else "GRAND", 4 if _q % 2 else 6, (5.0, 9.0), 120.0, 0.9)]))
for k in range(8):
    a0, a1 = 45.0 * k, 45.0 * (k + 1)
    second = {2: "INSTITUTIONAL", 6: "PREMIUM"}.get(k, "MIDDLE")
    DISTRICTS.append(dict(id="fi%02d" % (k + 1), band="inner", name="Mixed urban quarter %d (F-MID, inside the middle ring)" % (_q + k + 1), a0=a0, a1=a1,
                          start="radial_%03d" % a0, end="radial_%03d" % (a1 % 360), fs=[-0.68, -0.34], profile="kano_local", cross=7,
                          plan=[(0, "MIDDLE+COMMERCIAL+MARKET+ROADSIDE", "both", "GRAND", 7, (5.0, 9.0), 120.0, 0.9),     # a Friday mosque every seventh block of each inner quarter's main street
                                (1, second, "both", "LOCAL_MOSQUES" if second != "INSTITUTIONAL" else None, 4 if second != "INSTITUTIONAL" else 0, (5.0, 9.0) if second != "INSTITUTIONAL" else (10.0, 16.0),
                                 120.0 if second != "INSTITUTIONAL" else 160.0, 0.9 if second != "INSTITUTIONAL" else 0.85)]))


def mixof(names, local=False):
    """A district street's building mix from mix names joined by '+'. On a neighbourhood (local) street there are no fuel stations: Kano's stations stand on the arterials and collectors."""
    if names is None: return None
    out = []
    for n in names.split("+"): out += INSTITUTIONAL if n == "INSTITUTIONAL" else MARKET if n == "MARKET" else ROADSIDE if n == "ROADSIDE" else KEKE if n == "KEKE" else fewer_stations(getattr(C, n), 0.0 if local else 0.3)
    return [l for l in out if l["weight"] > 0]


def _raw_bearings(d):
    margin = 2.5 if d.get("band", "outer") == "outer" else 3.0; n = d["cross"]; return [d["a0"] + margin + (d["a1"] - d["a0"] - 2 * margin) * (i + 0.5) / n for i in range(n)]


def district_bearings(d):
    """The bearings of a district's cross streets. Inside the middle ring a cross street that falls near a street or road meeting the middle ring from outside is moved onto that
    street's bearing, so the two meet the ring at the same point and cross it as a crossroads (a connected grid through the middle ring); one that cannot be moved (its partner lies
    too close to the quarter's own boundary roads) is left out when it is within 1.5 degrees (about 70 m), so the junctions on the ring's two sides never fall a few metres apart."""
    raw = _raw_bearings(d)
    if d.get("band", "outer") == "outer": return raw
    outside = estate_cross_bearings()["middle"] + [b for e in DISTRICTS if e.get("band", "outer") == "outer" for b in _raw_bearings(e)] + [22.5 + 45.0 * k for k in range(8)]
    gap = lambda a, b: min(abs(a - b) % 360.0, 360.0 - abs(a - b) % 360.0)
    spacing = (d["a1"] - d["a0"]) / d["cross"]; out = []
    for b in raw:
        o = min(outside, key=lambda o: gap(b, o))
        if gap(b, o) < 0.45 * spacing and d["a0"] + 3.0 <= o <= d["a1"] - 3.0: b = o        # a crossroads with the street outside
        elif gap(b, o) < 1.5: continue
        if all(gap(b, x) > 0.5 * spacing for x in out): out.append(b)
    return out


def station_on(rd, p):
    ps = [(c["x"], c["z"]) for c in rd["controlPoints"]]; best, s, acc = None, 0.0, 0.0
    for i in range(1, len(ps)):
        a, b = ps[i - 1], ps[i]; L = dist(a, b); t = max(0.0, min(1.0, ((p[0] - a[0]) * (b[0] - a[0]) + (p[1] - a[1]) * (b[1] - a[1])) / (L * L))); q = (a[0] + (b[0] - a[0]) * t, a[1] + (b[1] - a[1]) * t)
        dd = dist(p, q)
        if best is None or dd < best: best, s = dd, acc + L * t
        acc += L
    return s


def cross_point(pts, f):
    """Where a polyline (a radial or sub-radial) crosses the ellipse f."""
    a, b = 2800.0 + f * 500.0, 2700.0 + f * 600.0; g = lambda x: ((x[0] - CX) / a) ** 2 + ((x[1] - CZ) / b) ** 2 - 1.0
    for i in range(1, len(pts)):
        if (g(pts[i - 1]) > 0) != (g(pts[i]) > 0): return seg_hit(pts[i - 1], pts[i], f)
    raise ValueError("no crossing of ellipse %.3f" % f)


def district(d, ring_arc_at, mid_arcs, inner_arcs, radials, subs):
    """A district in a sector of the band between the middle ring and the ring road, between two boundary roads that run inwards (a sub-radial or a radial): concentric district
    streets and cross streets, as the estate, with the district's own building mixes and, where it has one, a site plate (the bus terminal) at the end of a short road."""
    by_id = {x["id"]: x for x in radials + subs}
    if d.get("band", "outer") == "outer": by_id["radial_225"] = MAIN_PSEUDO                       # the A2 is the 225-degree arterial between the ring road and the middle ring
    a, b = by_id[d["start"]], by_id[d["end"]]
    pa = [(c["x"], c["z"]) for c in a["controlPoints"]]; pb = [(c["x"], c["z"]) for c in b["controlPoints"]]
    bear = district_bearings(d); streets, roads, sites, crosses = [], [], [], []
    site_br = (bear[len(bear) // 2 - 1] + bear[len(bear) // 2]) / 2 if d.get("site") else None      # the site road leaves a street half way between two cross streets
    for i, f in enumerate(d["fs"]):
        x0 = cross_point(pa, f); x1 = cross_point(pb, f)
        d0 = angle_at_distance(None, bearing_at(None, x0, f), HW[a["controlPoints"][0]["profile"]] + 0.3, +1, f)
        d1 = angle_at_distance(None, bearing_at(None, x1, f), HW[b["controlPoints"][0]["profile"]] + 0.3, -1, f)
        d0 += 360.0 * round((d["a0"] - d0) / 360.0); d1 += 360.0 * round((d["a1"] - d1) / 360.0)      # the same turn as the district's bearings (the helpers answer in -180..180)
        stops = bear + ([site_br] if site_br is not None and i == d["site"][1] else [])
        arc = Arc("%s_street_%d" % (d["id"], i + 1), "%s, street %d" % (d["name"], i + 1), None, d0, d1, d["profile"], stops, f=f)
        arc.joins = [join("start", a["id"], station_on(a, x0), +1), join("end", b["id"], station_on(b, x1), -1)]
        streets.append(arc)
    inner_band = d.get("band", "outer") == "inner"
    first, last = ("inner", "middle") if inner_band else ("middle", "ring")
    for k, br in enumerate(bear):
        levels = [(first, None)] + [(None, f) for f in d["fs"]] + [(last, None)]
        for j in range(len(levels) - 1):
            (n0, f0), (n1, f1) = levels[j], levels[j + 1]
            if n0 == "inner": inner, s_in = inner_arcs[int((br % 360.0) // 90) % 4], br % 360.0
            elif n0 == "middle": inner, s_in = mid_arcs[int((br % 360.0) // 45) % 8], br % 360.0
            else: inner, s_in = streets[j - 1], br
            if n1 == "ring": outer, s_out = ring_arc_at(br)
            elif n1 == "middle": outer, s_out = mid_arcs[int((br % 360.0) // 45) % 8], br % 360.0
            else: outer, s_out = streets[j], br
            hw_in = HW["kano_collector"] if n0 in ("middle", "inner") else HW[d["profile"]]; hw_out = HW["kano_arterial"] if n1 == "ring" else HW["kano_collector"] if n1 == "middle" else HW[d["profile"]]
            p0 = add(ell(n0, br, f0), outward(n0, br, f0), hw_in); p1 = add(ell(n1, br, f1), outward(n1, br, f1), -hw_out)
            s_in = inner.station(s_in); s_out = outer.station(s_out)
            roads.append(straight("%s_cross_%d_%d" % (d["id"], k + 1, j), "%s, cross street" % d["name"], [p0, ((p0[0] + p1[0]) / 2, (p0[1] + p1[1]) / 2), p1], "kano_local",
                                  [join("start", inner.id, s_in, +1, 10.0), join("end", outer.id, s_out, -1, 10.0)]))
            crosses.append(("%s_cross_%d_%d" % (d["id"], k + 1, j), dist(p0, p1), None if n0 else j - 1, None if n1 else j))     # the district street at each end (None: a ring or the wall road)
    if d.get("site"):                                                                # a short road from a street outwards, ending at the site plate, half way between two cross streets
        sid, si, length = d["site"]; st = streets[si]; br = st.angles[min(range(len(st.angles)), key=lambda k: abs(st.angles[k] - site_br))]
        p0 = add(ell(None, br, st.f), outward(None, br, st.f), HW[d["profile"]]); p1 = add(p0, outward(None, br, st.f), length)
        rid = "%s_site_road" % d["id"]
        roads.append(straight(rid, "%s, access to the %s" % (d["name"], E1_SITES[sid]["name"].lower()), [p0, p1], d["profile"], [join("start", st.id, st.station(br), +1, 12.0)]))
        site = json.loads(json.dumps(E1_SITES[sid])); site["id"] = "%s_%s" % (d["id"], sid); site["corridor"] = rid; site["s"] = round(length - 5.0, 2); site["forward"] += 5.0; sites.append(site)
    front = []
    for si, mix, side, anchors, every, setback, block, presence in d["plan"]:
        st = streets[si]
        front.append(C.rule("%s_street_%d" % (d["id"], si + 1), mixof(mix, local=d["profile"] == "kano_local"), mixof(anchors), every if anchors else 0, corridor=st.id, side=side, s_from=END_CLEAR, s_to=st.s[-1] - END_CLEAR,
                            setback=setback, gap=(1.2, 4.0) if d["id"].startswith("f") else (2.0, 8.0), block=block, presence=min(0.95, presence + 0.05) if d["id"].startswith("f") else presence))     # the filler packs its plots closer and fuller (F-MID: 12 plots/ha)
    if d["id"].startswith("f"):
        # the side streets of the mixed quarters: houses and compounds with the odd kiosk, between the corner lots. A district street's buildings fill it right up to the cross street, so a
        # side street's own lots start beyond the deepest of them (setback + depth + 4 m); at a ring or the wall road the junction window of that road keeps the corners apart.
        reach = {si: setback[1] + max(l["depthM"] for l in (mixof(mix, local=True) or []) + (mixof(anchors) or [])) + 4.0 for si, mix, side, anchors, every, setback, block, presence in d["plan"]}
        for rid, length, at0, at1 in crosses:
            s0 = END_CLEAR if at0 is None else max(END_CLEAR, reach.get(at0, END_CLEAR)); s1 = length - (END_CLEAR if at1 is None else max(END_CLEAR, reach.get(at1, END_CLEAR)))
            if s1 - s0 < 40.0: continue
            front.append(C.rule("%s_side" % rid, SIDE_STREET, None, 0, corridor=rid, side="both", s_from=round(s0, 1), s_to=round(s1, 1), setback=(5.0, 8.0), gap=(1.2, 4.0), block=100.0, presence=0.95))
    return [x.spec() for x in streets] + roads, front, sites


FRINGE = [(0.0, "Road east out of the city (F-OUT)"), (90.0, "Road north out of the city (F-OUT)"), (270.0, "Road south out of the city (F-OUT)")]   # 180 crosses D07 (blocked)
FRINGE_LEN = 1300.0


def fringe(ring_r):
    """The plan's filler zone F-OUT, the peri-urban fringe beyond the ring road ("low-density plots, farmland and scrub"): a road leaving each ring road roundabout outwards (not at 180
    degrees: the D07 traditional suburb, blocked on references, lies there; not at 225: the A2) with a gentle bend every 200 m, traditional compounds, mud-brick houses and low-cost rows
    set far back from the road among farm trees, small mosques, roadside trade and the odd fuel station. Rural zone: big shade trees in the fields (trees_fields)."""
    roads, front = [], []
    trad = [l for l in C.TRADITIONAL if l["tag"] in ("hausa_compound", "house", "lowcost", "fuel_station")] + ROADSIDE
    for a, name in FRINGE:
        c = ell("ring", a); o = ((c[0] - CX) / dist(c, (CX, CZ)), (c[1] - CZ) / dist(c, (CX, CZ))); side = (o[1], -o[0])
        start = add(c, o, ring_r); pts = [start]
        for k in range(1, int(FRINGE_LEN // 200.0) + 1):
            wig = 18.0 * math.sin(k * 1.7 + a)                                             # a country road is never ruler-straight
            pts.append(add(add(start, o, 200.0 * k), side, wig if k > 1 else 0.0))
        rid = "fringe_%03d" % a
        roads.append(dict(id=rid, name=name, role="road", controlPoints=[cp("%s_%d" % (rid, i), x, z, "kano_collector", "urban" if i < 2 else "rural") for i, (x, z) in enumerate(pts)],
                          decks=[], joins=[]))
        front.append(C.rule("fringe_%03d" % a, trad, C.LOCAL_MOSQUES, 5, corridor=rid, s_from=END_CLEAR + 40.0, s_to=FRINGE_LEN - 40.0, setback=(8.0, 22.0), gap=(6.0, 28.0),
                            block=150.0, presence=0.55))
    return roads, front


def a2_districts(a2pts):
    """Roads off the A2 (they join its edge): the D11 gateway services road, and the D10 industrial and logistics roads with the logistics yard at the end of one of them."""
    seg = []; acc = 0.0
    for i in range(1, len(a2pts)): L = dist(a2pts[i - 1], a2pts[i]); seg.append((acc, L, a2pts[i - 1], a2pts[i])); acc += L
    def at(sv):
        for s0, L, p, q in seg:
            if sv <= s0 + L: t = (sv - s0) / L; d = ((q[0] - p[0]) / L, (q[1] - p[1]) / L); return (p[0] + (q[0] - p[0]) * t, p[1] + (q[1] - p[1]) * t), d
        raise ValueError(sv)
    plan = [("d11_services", "Gateway services road (D11)", 1180.0, +1, 360.0, "HIGHWAY_FULL", None),
            ("d10_road_1", "Industrial road 1 (D10)", 1650.0, +1, 440.0, "INDUSTRIAL", "site_yard"), ("d10_road_2", "Industrial road 2 (D10)", 1950.0, -1, 440.0, "INDUSTRIAL", None),
            ("d10_road_3", "Industrial road 3 (D10)", 2350.0, +1, 440.0, "INDUSTRIAL", None), ("d10_road_4", "Industrial road 4 (D10)", 2650.0, -1, 400.0, "INDUSTRIAL", None)]
    roads, front, sites = [], [], []
    for rid, name, sv, side, length, mix, site in plan:
        p, d = at(sv); nrm = (d[1] * side, -d[0] * side); p0 = add(p, nrm, HW["kano_expressway"]); p1 = add(p0, nrm, length)
        roads.append(straight(rid, name, [p0, add(p0, nrm, length / 2), p1], "kano_service", [join("start", ROUTE_ID, sv, side, 16.0)]))
        m = C.HIGHWAY if mix == "HIGHWAY_FULL" else getattr(C, mix)
        front.append(C.rule("%s_frontage" % rid, m, C.LOCAL_MOSQUES if mix == "HIGHWAY_FULL" else None, 2 if mix == "HIGHWAY_FULL" else 0, corridor=rid, s_from=END_CLEAR,
                            s_to=length - (110.0 if site else 25.0), setback=(8.0, 14.0), gap=(3.0, 10.0), block=120.0, presence=0.9))
        if site:
            st = json.loads(json.dumps(E1_SITES[site])); st["id"] = "d10_%s" % site; st["corridor"] = rid; st["s"] = round(length - 5.0, 2); st["forward"] += 5.0; sites.append(st)
    return roads, front, sites


def seg_hit(p, q, f):
    """Where the segment p-q crosses the estate ellipse f."""
    a, b = 2800.0 + f * 500.0, 2700.0 + f * 600.0
    g = lambda x: ((x[0] - CX) / a) ** 2 + ((x[1] - CZ) / b) ** 2 - 1.0
    lo, hi = 0.0, 1.0; gl = g(p)
    for _ in range(60):
        mid = (lo + hi) / 2; m = (p[0] + (q[0] - p[0]) * mid, p[1] + (q[1] - p[1]) * mid)
        if (g(m) > 0) == (gl > 0): lo = mid
        else: hi = mid
    t = (lo + hi) / 2; return (p[0] + (q[0] - p[0]) * t, p[1] + (q[1] - p[1]) * t)


# ------------------------------------------------------------------------------------------------------------------------------------------------ the city fabric
def scaled(mix, **k): return [dict(l, weight=round(l["weight"] * k.get(l["tag"], 1.0), 3)) for l in mix]
def fewer_stations(mix, k=0.3): return scaled(mix, fuel_station=k)
def scaled_all(mix, k): return [dict(l, weight=round(l["weight"] * k, 3)) for l in mix]
SIDE_STREET = [l for l in C.MIDDLE if l["tag"] in ("bungalow", "semi_detached", "villa", "house")] + scaled_all([l for l in C.TRADITIONAL if l["tag"] in ("hausa_compound", "lowcost")], 0.5) + \
    scaled_all(ROADSIDE, 0.25)                                                       # a side street of a mixed quarter: houses and compounds, the odd kiosk (no school, duplex or fuel station: short blocks)
# the test-road mixes had to show every design on 1.5 km; a city street has a school or a clinic among dozens of homes, and markets on their own sites
C.MIDDLE = scaled(C.MIDDLE, school=0.12, clinic=0.3)
C.COMMERCIAL = scaled(C.COMMERCIAL, market_shed=0.35, clinic=0.4)
C.TRADITIONAL = scaled(C.TRADITIONAL, fuel_station=0.3)


def pieces(length, cuts, start_clear=END_CLEAR, end_clear=END_CLEAR, shortest=70.0, window=JUNCTION_CLEAR):
    """The stretches of a road that get buildings: its length less its ends and a window round every junction on it."""
    out, s0 = [], start_clear
    for c in sorted(cuts):
        if c - window > s0 + shortest: out.append((s0, c - window))
        s0 = max(s0, c + window)
    if length - end_clear > s0 + shortest: out.append((s0, length - end_clear))
    return out


def fabric(roads, a2_len):
    """Buildings along every road of the network, each road in the stretches between its junctions. `roads`: (id, length, kind) with kind ring, middle, wall, radial, sub or main."""
    rule = C.rule; out = []; cuts = {}
    for rd in ALL_ROADS:
        for j in rd["joins"]: cuts.setdefault(j["corridor"], []).append(j["s"])
    style = {
        "ring": (fewer_stations(C.COMMERCIAL + C.MIDDLE + C.MAJOR, 0.25) + scaled_all(C.LOCAL_MOSQUES[:1], 0.3) + KEKE, C.NEIGHBOURHOOD, 4, (10.0, 15.0), (2.0, 8.0), 150.0, 0.85, "both"),
        "middle": (fewer_stations(C.MIDDLE + C.COMMERCIAL, 0.25) + ROADSIDE + scaled_all(C.LOCAL_MOSQUES[:1], 0.45) + KEKE, C.NEIGHBOURHOOD, 3, (5.0, 9.0), (1.5, 6.0), 130.0, 0.88, "both"),     # roadside mosques as ordinary lots: the ring's stretches between junctions are too short for anchors
        "wall": (fewer_stations(C.COMMERCIAL, 0.2) + ROADSIDE + ROADSIDE + scaled_all(KEKE, 1.5), C.NEIGHBOURHOOD, 4, (5.0, 9.0), (1.5, 6.0), 120.0, 0.85, "right"),      # outside the wall only: nothing is built in the old city; the busiest roadside trade and keke ranks
        "radial": (fewer_stations(C.MAJOR + C.COMMERCIAL, 0.3) + KEKE, C.GRAND, 3, (10.0, 15.0), (2.0, 8.0), 150.0, 0.9, "both"),
        "sub": (C.MIDDLE, C.LOCAL_MOSQUES, 3, (5.0, 8.0), (1.5, 6.0), 110.0, 0.9, "both"),          # 5 m: parked cars at the kerb (family K)
        "main": (fewer_stations(C.HIGHWAY, 0.4), C.GRAND, 4, (14.0, 22.0), (6.0, 18.0), 180.0, 0.6, "both"),
    }
    for item in roads:
        rid, length, kind = item[:3]; lo, hi = (item[3], item[4]) if len(item) > 3 else (0.0, length)
        mix, anchors, every, setback, gap, block, presence, side = style[kind]
        start = 200.0 if kind == "main" else END_CLEAR
        # Every road joining this one keeps its own buildings END_CLEAR (52 m) from this road's edge. When this road's deepest lot (setback + depth, with 4 m to spare) stays inside that,
        # the two roads' lots can never meet, and the window round the junction only has to clear the joining road itself: the corner building then stands at the corner, as in the
        # city, instead of a 116 m gap at every side street (the wall road lost most of its frontage to the side streets of the inner quarters before this).
        reach = setback[1] + max(l["depthM"] for l in mix + (anchors or [])) + 4.0
        window = CORNER_CLEAR if reach <= END_CLEAR else JUNCTION_CLEAR
        ps = [(max(a, lo + (END_CLEAR if lo > 0 else 0.0)), min(b, hi - (END_CLEAR if hi < length else 0.0))) for a, b in pieces(length, cuts.get(rid, []), start, END_CLEAR, window=window)]
        for n, (s0, s1) in enumerate([q for q in ps if q[1] - q[0] >= 70.0]):
            out.append(rule("fab_%s_%d" % (rid if rid != ROUTE_ID else ("a2" if kind == "main" else "a2in"), n), mix, anchors if (s1 - s0) > 1.5 * block else None, every if (s1 - s0) > 1.5 * block else 0,
                            corridor=rid, side=side, s_from=round(s0, 1), s_to=round(s1, 1), setback=setback, gap=gap, block=block, presence=presence))
    return out


FRONTAGE_DEFAULTS = dict(zones="", profiles="", side="both", setbackMin=5.0, setbackMax=9.0, gapMin=1.5, gapMax=6.0, blockLength=120.0, blockMargin=3.0, presence=0.9, clearance=2.5,
                         maxHeightRange=1.6, yawJitterDeg=1.5, fixedDensity=False, rows=1, rowGap=8.0, anchorEveryBlocks=0, anchorPhase=-1)          # NetCore.FrontageRule's own defaults


def compact(r):
    """Leaves out of every frontage rule the fields that equal the engine's own defaults (the game and the tests fill them back from NetCore.FrontageRule): the city must stay inside the
    Resources budget (1.5 MB per file, 6 MB in all). The lots are exactly the same."""
    for f in r["frontage"]:
        for k, v in FRONTAGE_DEFAULTS.items():
            if k in f and f[k] == v: del f[k]
    return pack_mixes(r)


def pack_mixes(r):
    """Route data packing: every distinct lot list (a mix or an anchor list) is written once under `mixes` and the rules name it (mixId, anchorsId); the game copies it back into each rule on load
    (NetCore FrontageShared.Resolve, run by the validator). The city's 566 rules use 26 distinct lists; repeating them took half of the file. The lots are exactly the same."""
    ids, mixes = {}, []
    def name(lots):
        key = json.dumps(lots, sort_keys=True)
        if key not in ids: ids[key] = "m%02d" % len(ids); mixes.append({"id": ids[key], "lots": lots})
        return ids[key]
    for f in r["frontage"]:
        f["mixId"] = name(f.pop("mix"))
        anchors = f.pop("anchors", None)
        if anchors: f["anchorsId"] = name(anchors)
    r["mixes"] = mixes
    return r


def main():
    r = compact(build()); p = C.ROUTES / (ROUTE_ID + ".route.json")
    text = json.dumps(r, separators=(",", ":"), ensure_ascii=False) + "\n"          # compact: the city is the largest file under Resources (budget 1.5 MB per file, 6 MB in all)
    if "--check" in sys.argv:
        ok = p.is_file() and p.read_text() == text; print(("up to date: " if ok else "OUT OF DATE: ") + p.name); return 0 if ok else 1
    p.write_text(text)
    print("wrote", p.name, round(len(text) / 1024), "KB:", len(r["roads"]), "roads,", len(r["rings"]), "roundabouts,", r["meta"]["networkKm"], "km of network,", len(r["frontage"]), "frontage rules")
    return 0


if __name__ == "__main__":
    sys.exit(main())
