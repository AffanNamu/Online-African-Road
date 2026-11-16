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
for k in ("ring_large", "ring_modern", "ring_landmark"): PROF[k] = E.P[k]
E.P.update(PROF); E.G.update({k: E.geom(v) for k, v in PROF.items()})
HW = {k: E.G[k]["paved"] for k in PROF}                                            # paved half width: where a joining road's end sits


def cp(i, x, z, profile, zone="urban"): return E.cp(i, i, x, z, Y, profile, zone)


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
RING_RBT = {0: "rr_e", 90: "rr_n", 180: "rr_w", 225: "rr_a2", 270: "rr_s"}           # ring road roundabouts (225: the A2 interchange of the plan, at grade for now)
GATE_RBT = {0: "gate_e", 90: "gate_n", 180: "gate_w", 270: "gate_s"}                 # roundabouts where the radials meet the wall road
RING_DESIGN = dict(design="large", profile="ring_large", radius=30.0, apron=0.0, island="planter", splitter=(22.0, 4.0), flare=0.0, mouth=1.8)
GATE_DESIGN = dict(design="modern", profile="ring_modern", radius=24.0, apron=2.0, island="planter", splitter=(14.0, 3.0), flare=0.0, mouth=1.9)
ESTATE_F = [0.18, 0.345, 0.51, 0.675]                                                # the estate's concentric streets between the middle ring (0) and the ring road (1): about 92 m apart, so
                                                                                     # two rows of compounds fit back to back, and the outer rows clear the shops of both rings
JUNCTION_CLEAR = 58.0                                                                # buildings of a road keep this far (along it) from a junction on it: corner lots of two roads never meet
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
    r["traffic"] = dict(densityScale=0.0, speedLimitScale=1.0, vehicleMix=[])          # traffic on roundabouts and T joins is not modelled yet
    roads, rings = [], []
    ring_r = rout_of(RING_DESIGN); gate_r = rout_of(GATE_DESIGN)

    # ---- the ring road: arcs between its roundabouts, control points at every T bearing and at the estate's cross streets
    est_cross = estate_cross_bearings()
    outer_cross = sorted(est_cross["ring"] + [b for d in DISTRICTS if d.get("band", "outer") == "outer" for b in district_bearings(d)])
    inner_cross = sorted(b for d in DISTRICTS if d.get("band") == "inner" for b in district_bearings(d))
    ring_bounds = sorted(RING_RBT)                                                  # 0, 90, 180, 225, 270
    ring_arcs = []
    for i, a0 in enumerate(ring_bounds):
        a1 = ring_bounds[(i + 1) % len(ring_bounds)] + (360.0 if i + 1 == len(ring_bounds) else 0.0)
        d0 = angle_at_distance("ring", a0, ring_r, +1); d1 = angle_at_distance("ring", a1, ring_r, -1)
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
        d0 = angle_at_distance("middle", a0, HW["kano_arterial"] + 0.3, +1); d1 = angle_at_distance("middle", a1, HW["kano_arterial"] + 0.3, -1)
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
    a2c = ell("ring", 225.0); last = NODE["X_LOG"]; u = ((a2c[0] - last[0]) / dist(last, a2c), (a2c[1] - last[1]) / dist(last, a2c))
    a2_end = add(a2c, u, -ring_r)
    r["controlPoints"] = [cp("a2_%d" % i, x, z, "kano_expressway", "highway" if i < 2 else "urban") for i, (x, z) in enumerate([NODE["X_T1"], NODE["X_GW"], last, a2_end])]
    a2_len = dist(NODE["X_T1"], NODE["X_GW"]) + dist(NODE["X_GW"], last) + dist(last, a2_end)
    r["waypoints"] = [dict(id="wp_a2", name="A2 from Zaria", kind="district", s=0.0), dict(id="wp_gateway", name="Kano gateway (D11)", kind="district", s=round(dist(NODE["X_T1"], NODE["X_GW"]), 1)),
                      dict(id="wp_ring", name="Ring Road", kind="district", s=round(a2_len, 1))]

    # ---- district D06: the modern residential estate
    est_roads, est_frontage = estate(ring_arc_at, mid_arcs, radials, subs, est_cross)
    # ---- the other ready districts in their sectors (D05 commercial belt, D08 institutional corridor, D09 transport hub) and the plan's mixed urban filler (F-MID) everywhere else
    #      between the wall and the ring road, except the D07 sector (traditional suburb, blocked on references)
    for d in DISTRICTS:
        rds, fr, st = district(d, ring_arc_at, mid_arcs, inner_arcs, radials, subs)
        est_roads += rds; est_frontage += fr; r["sites"] += st
    # ---- along the A2: D11 gateway services and D10 industrial and logistics roads
    rds, fr, st = a2_districts([NODE["X_T1"], NODE["X_GW"], last, a2_end])
    est_roads += rds; est_frontage += fr; r["sites"] += st

    # ---- roundabouts
    def ends_at(rd_list, centre, rad):
        out = []
        for rd in rd_list:
            c = rd["controlPoints"]
            if dist((c[0]["x"], c[0]["z"]), centre) < rad + 1.0 or dist((c[-1]["x"], c[-1]["z"]), centre) < rad + 1.0: out.append(rd["id"])
        return out
    all_roads = [a.spec() for a in ring_arcs] + [a.spec() for a in mid_arcs] + [a.spec() for a in inner_arcs] + radials + subs + est_roads
    for a, rid in RING_RBT.items():
        c = ell("ring", a); con = ends_at(all_roads, c, ring_r) + ([ROUTE_ID] if a == 225 else [])
        props = [("t%d" % i, "tropical_tree", 16 * math.cos(math.radians(30 * i)), 16 * math.sin(math.radians(30 * i)), 0) for i in range(12)]
        rings.append(ring_spec(rid, "Ring Road roundabout %d degrees%s" % (a, " (A2)" if a == 225 else ""), c, RING_DESIGN, con, props))
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
            r["props"].append(dict(id="%s_light%d" % (g["id"], k), prop="street_light", text="", corridor=g["id"], s=round((k + 0.5) * L / n_l, 2), lateral=round(E.G[prof]["formation"] + 0.6, 2), yawDeg=-90.0, scale=1.0))
    # street lights, palms, kiosks, bus stops by cross-section (the E1 rules), and the expressway's median lights
    # the E1 street rules, on EVERY road of the city (allRoads: lights, palms on the arterials, kiosks, bus stops), and street trees of the environment kit on the city streets
    base = dict(e1["propRules"][0])
    r["propRules"] = [dict(p, allRoads=True) for p in e1["propRules"]] + [dict(base, id="lights_expressway_median", profiles="kano_expressway", allRoads=False)] + [
        dict(base, id=rid, prop=prop, profiles=prof, side="both", offsetMin=omin, offsetMax=omax, spacingMin=smin, spacingMax=smax, presence=pres, scaleMin=0.85, scaleMax=1.15,
             yawJitterDeg=180.0, faceRoad=False, fixedDensity=False, exclusionMargin=-1.0, allRoads=True)
        for rid, prop, prof, omin, omax, smin, smax, pres in [
            ("trees_local_a", "rg_ngn_tree_street_broadleaf_a", "kano_local", 1.8, 3.2, 26.0, 38.0, 0.45),
            ("trees_local_b", "rg_ngn_tree_street_broadleaf_b", "kano_local", 1.8, 3.2, 31.0, 45.0, 0.35),
            ("trees_collector", "rg_ngn_tree_large_shade_a", "kano_collector", 2.0, 3.4, 55.0, 85.0, 0.4),
            ("trees_service", "rg_ngn_tree_street_broadleaf_b", "kano_service", 1.8, 3.2, 40.0, 60.0, 0.3)]]
    global ALL_ROADS; ALL_ROADS = all_roads
    fab_roads = [(a.id, a.s[-1], "ring") for a in ring_arcs] + [(a.id, a.s[-1], "middle") for a in mid_arcs] + [(a.id, a.s[-1], "wall") for a in inner_arcs] + \
                [(x["id"], rd_len(x), "radial") for x in radials] + [(x["id"], rd_len(x), "sub") for x in subs] + [(ROUTE_ID, a2_len, "main")]
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
    plan = [(lowcost, "both"), (C.MIDDLE, "both"), (C.MIDDLE + no_mansion, "both"), (no_mansion, "left"), (C.PREMIUM, "right")]
    for n, (mix, side) in enumerate(plan):
        i = min(n, 3); a = streets[i]
        front.append(rule("d06_street_%d%s" % (i + 1, "" if side == "both" else "_" + side), mix, C.LOCAL_MOSQUES if n % 2 == 0 else None, 4 if n % 2 == 0 else 0, corridor=a.id, side=side,
                          s_from=END_CLEAR, s_to=a.s[-1] - END_CLEAR, setback=(3.0, 5.0), gap=(1.0, 3.0), block=150.0, presence=0.95))
    return est, front


# ------------------------------------------------------------------------------------------------------------------------------------------------ the other districts
E1_SITES = {x["id"]: x for x in e1["sites"]}                                          # the logistics yard and bus terminal plates of the E2 kit, reused as district sites
INSTITUTIONAL = [C.L("kano_hospital_general_a", 0.7, "hospital"), C.L("kano_school_a", 1.4, "school"), C.L("kano_government_offices_a", 1.2, "government"),
                 C.L("kano_police_division_a", 0.6, "police"), C.L("kano_hotel_a", 0.5, "hotel"), C.L("rg_ngn_clinic_a", 0.6, "clinic")]
DISTRICTS = [
    dict(id="d05", name="Commercial belt (D05)", a0=22.5, a1=45.0, start="sub_0225", end="radial_045", fs=[0.2, 0.45, 0.7], profile="kano_collector", cross=6,
         plan=[(0, "COMMERCIAL", "both", "NEIGHBOURHOOD", 3, (6.0, 10.0), 140.0, 0.95), (1, "COMMERCIAL+MAJOR", "both", "NEIGHBOURHOOD", 4, (8.0, 12.0), 150.0, 0.95),
               (2, "MAJOR", "both", "GRAND", 3, (8.0, 12.0), 150.0, 0.95)]),
    dict(id="d08", name="Institutional corridor (D08)", a0=67.5, a1=90.0, start="sub_0675", end="radial_090", fs=[0.25, 0.6], profile="kano_collector", cross=5,
         plan=[(0, "INSTITUTIONAL", "both", "LOCAL_MOSQUES", 4, (10.0, 16.0), 160.0, 0.85), (1, "INSTITUTIONAL", "both", None, 0, (10.0, 16.0), 160.0, 0.85)]),
    dict(id="d09", name="Transport hub (D09)", a0=202.5, a1=225.0, start="sub_2025", end="radial_225", fs=[0.25, 0.6], profile="kano_collector", cross=5,
         plan=[(0, "TRANSPORT", "both", "NEIGHBOURHOOD", 4, (8.0, 12.0), 140.0, 0.9), (1, "TRANSPORT+COMMERCIAL", "both", None, 0, (8.0, 12.0), 140.0, 0.9)],
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
    DISTRICTS.append(dict(id="fo%02d" % _q, band="outer", name="Mixed urban quarter %d (F-MID)" % _q, a0=a0, a1=a1, start=_bounds_outer(a0), end=_bounds_outer(a1), fs=[0.27, 0.62],
                          profile="kano_local", cross=4,
                          plan=[(0, "MIDDLE", "both", "LOCAL_MOSQUES", 4, (4.0, 8.0), 120.0, 0.9),
                                (1, "MIDDLE+COMMERCIAL" if _q % 2 else "COMMERCIAL", "both", "NEIGHBOURHOOD" if _q % 2 else None, 4 if _q % 2 else 0, (4.0, 8.0), 120.0, 0.9)]))
for k in range(8):
    a0, a1 = 45.0 * k, 45.0 * (k + 1)
    second = {2: "INSTITUTIONAL", 6: "PREMIUM"}.get(k, "MIDDLE")
    DISTRICTS.append(dict(id="fi%02d" % (k + 1), band="inner", name="Mixed urban quarter %d (F-MID, inside the middle ring)" % (_q + k + 1), a0=a0, a1=a1,
                          start="radial_%03d" % a0, end="radial_%03d" % (a1 % 360), fs=[-0.68, -0.34], profile="kano_local", cross=7,
                          plan=[(0, "MIDDLE+COMMERCIAL", "both", "GRAND" if k in (0, 3, 5) else "NEIGHBOURHOOD", 6 if k in (0, 3, 5) else 4, (4.0, 8.0), 120.0, 0.9),     # Friday mosques on three quarters' main streets
                                (1, second, "both", "LOCAL_MOSQUES" if second != "INSTITUTIONAL" else None, 4 if second != "INSTITUTIONAL" else 0, (4.0, 8.0) if second != "INSTITUTIONAL" else (10.0, 16.0),
                                 120.0 if second != "INSTITUTIONAL" else 160.0, 0.9 if second != "INSTITUTIONAL" else 0.85)]))


def mixof(names):
    if names is None: return None
    out = []
    for n in names.split("+"): out += INSTITUTIONAL if n == "INSTITUTIONAL" else fewer_stations(getattr(C, n), 0.3)
    return out


def _raw_bearings(d):
    margin = 2.5 if d.get("band", "outer") == "outer" else 3.0; n = d["cross"]; return [d["a0"] + margin + (d["a1"] - d["a0"] - 2 * margin) * (i + 0.5) / n for i in range(n)]


def district_bearings(d):
    """The bearings of a district's cross streets. Inside the middle ring they leave out any bearing within 1.5 degrees (about 70 m) of a street or road meeting the middle ring from
    outside, so the junctions on its two sides never fall a few metres apart."""
    raw = _raw_bearings(d)
    if d.get("band", "outer") == "outer": return raw
    outside = estate_cross_bearings()["middle"] + [b for e in DISTRICTS if e.get("band", "outer") == "outer" for b in _raw_bearings(e)] + [22.5 + 45.0 * k for k in range(8)]
    near = lambda b: min(min(abs(b - o) % 360.0, 360.0 - abs(b - o) % 360.0) for o in outside) < 1.5
    return [b for b in raw if not near(b)]


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
    by_id = {x["id"]: x for x in radials + subs}; a, b = by_id[d["start"]], by_id[d["end"]]
    pa = [(c["x"], c["z"]) for c in a["controlPoints"]]; pb = [(c["x"], c["z"]) for c in b["controlPoints"]]
    bear = district_bearings(d); streets, roads, sites = [], [], []
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
    if d.get("site"):                                                                # a short road from a street outwards, ending at the site plate, half way between two cross streets
        sid, si, length = d["site"]; st = streets[si]; br = st.angles[min(range(len(st.angles)), key=lambda k: abs(st.angles[k] - site_br))]
        p0 = add(ell(None, br, st.f), outward(None, br, st.f), HW[d["profile"]]); p1 = add(p0, outward(None, br, st.f), length)
        rid = "%s_site_road" % d["id"]
        roads.append(straight(rid, "%s, access to the %s" % (d["name"], E1_SITES[sid]["name"].lower()), [p0, p1], d["profile"], [join("start", st.id, st.station(br), +1, 12.0)]))
        site = json.loads(json.dumps(E1_SITES[sid])); site["id"] = "%s_%s" % (d["id"], sid); site["corridor"] = rid; site["s"] = round(length - 5.0, 2); site["forward"] += 5.0; sites.append(site)
    front = []
    for si, mix, side, anchors, every, setback, block, presence in d["plan"]:
        st = streets[si]
        front.append(C.rule("%s_street_%d" % (d["id"], si + 1), mixof(mix), mixof(anchors), every if anchors else 0, corridor=st.id, side=side, s_from=END_CLEAR, s_to=st.s[-1] - END_CLEAR,
                            setback=setback, gap=(1.5, 5.0) if d["id"].startswith("f") else (2.0, 8.0), block=block, presence=presence))     # the filler packs its plots closer (F-MID: 12 plots/ha)
    return [x.spec() for x in streets] + roads, front, sites


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
# the test-road mixes had to show every design on 1.5 km; a city street has a school or a clinic among dozens of homes, and markets on their own sites
C.MIDDLE = scaled(C.MIDDLE, school=0.12, clinic=0.3)
C.COMMERCIAL = scaled(C.COMMERCIAL, market_shed=0.35, clinic=0.4)
C.TRADITIONAL = scaled(C.TRADITIONAL, fuel_station=0.3)


def pieces(length, cuts, start_clear=END_CLEAR, end_clear=END_CLEAR, shortest=70.0):
    """The stretches of a road that get buildings: its length less its ends and a window round every junction on it."""
    out, s0 = [], start_clear
    for c in sorted(cuts):
        if c - JUNCTION_CLEAR > s0 + shortest: out.append((s0, c - JUNCTION_CLEAR))
        s0 = max(s0, c + JUNCTION_CLEAR)
    if length - end_clear > s0 + shortest: out.append((s0, length - end_clear))
    return out


def fabric(roads, a2_len):
    """Buildings along every road of the network, each road in the stretches between its junctions. `roads`: (id, length, kind) with kind ring, middle, wall, radial, sub or main."""
    rule = C.rule; out = []; cuts = {}
    for rd in ALL_ROADS:
        for j in rd["joins"]: cuts.setdefault(j["corridor"], []).append(j["s"])
    style = {
        "ring": (fewer_stations(C.COMMERCIAL + C.MIDDLE + C.MAJOR, 0.25), C.NEIGHBOURHOOD, 4, (10.0, 15.0), (2.0, 8.0), 150.0, 0.85, "both"),
        "middle": (fewer_stations(C.MIDDLE + C.COMMERCIAL, 0.25), C.NEIGHBOURHOOD, 3, (5.0, 9.0), (1.5, 6.0), 130.0, 0.88, "both"),
        "wall": (fewer_stations(C.COMMERCIAL, 0.2), None, 0, (5.0, 9.0), (1.5, 6.0), 120.0, 0.85, "right"),      # outside the wall only: nothing is built in the old city
        "radial": (fewer_stations(C.MAJOR + C.COMMERCIAL, 0.3), C.GRAND, 3, (10.0, 15.0), (2.0, 8.0), 150.0, 0.9, "both"),
        "sub": (C.MIDDLE, C.LOCAL_MOSQUES, 3, (4.0, 7.0), (1.5, 6.0), 110.0, 0.9, "both"),
        "main": (fewer_stations(C.HIGHWAY, 0.4), C.GRAND, 4, (14.0, 22.0), (6.0, 18.0), 180.0, 0.6, "both"),
    }
    for rid, length, kind in roads:
        mix, anchors, every, setback, gap, block, presence, side = style[kind]
        start = 200.0 if kind == "main" else END_CLEAR
        for n, (s0, s1) in enumerate(pieces(length, cuts.get(rid, []), start, END_CLEAR)):
            out.append(rule("fab_%s_%d" % (rid if kind != "main" else "a2", n), mix, anchors if (s1 - s0) > 2 * block else None, every if (s1 - s0) > 2 * block else 0,
                            corridor=rid, side=side, s_from=round(s0, 1), s_to=round(s1, 1), setback=setback, gap=gap, block=block, presence=presence))
    return out


def main():
    r = build(); p = C.ROUTES / (ROUTE_ID + ".route.json")
    text = json.dumps(r, indent=1, ensure_ascii=False) + "\n"
    if "--check" in sys.argv:
        ok = p.is_file() and p.read_text() == text; print(("up to date: " if ok else "OUT OF DATE: ") + p.name); return 0 if ok else 1
    p.write_text(text)
    print("wrote", p.name, round(len(text) / 1024), "KB:", len(r["roads"]), "roads,", len(r["rings"]), "roundabouts,", r["meta"]["networkKm"], "km of network,", len(r["frontage"]), "frontage rules")
    return 0


if __name__ == "__main__":
    sys.exit(main())
