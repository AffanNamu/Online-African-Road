#!/usr/bin/env python3
"""
Kano City Phase 1 (owner's World Design Reference R-041, 2026-11-16): the first drivable piece of the city as the board lays it out, built only from what the engine and the asset library
already have. A SPINE from the gateway into the centre, the way a driver arrives in Kano:

  A2 gateway road (Gateway District)  ->  Ring Road roundabout (arms: the ring road, Industrial District one way, Mixed Residential the other)
  ->  Transport District road  ->  Transport roundabout (arms: terminal road, industrial service road)
  ->  Commercial Belt road  ->  Central District roundabout, landmark design (arms: Modern Estate north, Traditional Suburb south)
  ->  Central District road (grand mosques, offices, hotels)  ->  Institutional roundabout (arms: Institutional District, Middle-Class Residential)

Every stretch carries the building mix of its district on the board (the mixes of tools/kano/make_city_routes.py), with mosques at intervals, street lights and palms. The spine heads
north-west, from the board's gateway (south-east) towards its centre. What it is not, honestly: the ring road is two straight arms off one roundabout, not the whole 20 km loop (the ring
primitive runs all its lanes one way and is meant for roundabouts); the expressway flyover of the board stands on the separate F1 route; the old city, Kurmi market and Dala Hill stay
pending (no authentic references, as the board itself says). Positions are the board's intent, not survey. Run: python3 tools/kano/make_city_p1.py
"""
import json, math, sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent)); sys.path.insert(0, str(Path(__file__).resolve().parents[1] / "e5"))
import make_city_routes as C                                                       # district mixes, the frontage rule helper
import gen_e5_routes as E                                                          # cross-section maths, roundabout designs

ROUTE_ID = "ng-kano-city-p1"
HEADING = 135.0                                                                    # degrees from +x towards +z: north-west, gateway (south-east) to centre
U = (math.cos(math.radians(HEADING)), math.sin(math.radians(HEADING)))
Y = 10.0

# ---------------------------------------------------------------------------------------------------------------------------------------------------- cross-sections
e1 = json.loads((C.ROUTES / "ng-kano-city-e1.route.json").read_text())
PROF = {p["id"]: p for p in e1["profiles"]}                                       # kano_arterial (divided, planter median), kano_collector, kano_local, kano_service
for k in ("ring_landmark", "ring_modern", "ring_large", "ring_commercial"): PROF[k] = E.P[k]
E.P.update(PROF); E.G.update({k: E.geom(v) for k, v in PROF.items()})


def cp(i, x, z, profile, zone="urban"): return E.cp(i, i, x, z, Y, profile, zone)


def along(p, d): return (p[0] + U[0] * d, p[1] + U[1] * d)


# ---------------------------------------------------------------------------------------------------------------------------------------------------- the spine
GATEWAY_LEN = 1500.0
DESIGNS = {d["id"]: d for d in E.DESIGNS}
SPINE = [  # (ring id, design, name, arms [(id, profile, bearing relative to the heading, length)], link to the next ring (id, length) or None)
    ("r_ringroad", "r_large", "Ring Road roundabout (the ring road meets the A2 gateway road)", [], ("transport_road", 900.0)),
    ("r_transport", "r_commercial", "Transport District roundabout (bus bays on its arms)", [("terminal_road", "kano_collector", -90, 450.0), ("service_road", "kano_service", 90, 450.0)], ("commercial_belt", 900.0)),
    ("r_central", "r_landmark", "Central District roundabout (landmark island with a mast and palms)", [("estate_road", "kano_local", 90, 600.0), ("suburb_road", "kano_local", -90, 600.0)], ("central_road", 700.0)),
    ("r_institutional", "r_modern", "Institutional roundabout", [("institutional_road", "kano_collector", 90, 600.0), ("middle_road", "kano_local", -90, 600.0)], None),
]
MIN_RADIUS = 24.0                                                                   # a divided arterial and a side road 90 degrees apart need about 42 m of ring between them (network check)
RING_ROAD_RADIUS, RING_ARM = 3300.0, 1500.0                                         # the ring road round the centre (master plan: radius 3.3 km); each arm follows its arc this far


def ring_road_arm(aid, q, rout, side):
    """One arm of the ring road: a road leaving the Ring Road roundabout's outer edge radially (side +1: left of the heading, -1: right) and following the arc of a circle of
    RING_ROAD_RADIUS whose centre lies ahead on the spine, so the ring road curves round the city as on the board. Control points every 150 m (the arc is far gentler than the 40 m limit)."""
    perp = (-U[1] * side, U[0] * side); c = (q[0] + U[0] * RING_ROAD_RADIUS, q[1] + U[1] * RING_ROAD_RADIUS); pts = []
    n = int(RING_ARM / 150.0)
    for i in range(n + 1):
        phi = (rout + RING_ARM * i / n) / RING_ROAD_RADIUS
        pts.append((c[0] + RING_ROAD_RADIUS * (-math.cos(phi) * U[0] + math.sin(phi) * perp[0]), c[1] + RING_ROAD_RADIUS * (-math.cos(phi) * U[1] + math.sin(phi) * perp[1])))
    pts[0] = (q[0] + perp[0] * rout, q[1] + perp[1] * rout)                              # exactly on the roundabout's outer paved edge, radial
    return dict(id=aid, name="Ring Road (%s arm)" % ("north-east" if side < 0 else "south-west"), role="road", controlPoints=[cp("%s%d" % (aid, i), x, z, "kano_arterial") for i, (x, z) in enumerate(pts)], decks=[], joins=[])


def fewer_stations(mix, k=0.3):
    """The city mixes were tuned for short test roads that had to show every station design; over 10 km of city they would stand every 250 m. Phase 1 keeps about one per kilometre of road."""
    return [dict(l, weight=round(l["weight"] * k, 3)) if l["tag"] == "fuel_station" else l for l in mix]


def build():
    r = E.base_route(ROUTE_ID, "Kano City Phase 1 (World Design Reference R-041): gateway, ring road, transport, commercial belt, central district, estates and suburbs",
                     "KANO CITY PHASE 1, FREE DRIVE. The first piece of the city laid out after the owner's World Design Reference (R-041): a spine of roads and roundabouts from the gateway to the centre, "
                     "each stretch carrying its district's buildings. Positions are the board's intent, not a survey; the ring road is two arms of one roundabout, not the whole loop; the old city, "
                     "Kurmi market and Dala Hill are pending. docs/KANO_CITY_PHASE1.md.", 4141, dict(E.TERRAIN))
    r = E.fix_meta(r)
    r["meta"]["from"] = "A2 gateway"; r["meta"]["to"] = "Institutional District"; r["meta"]["status"] = "planned"; r["meta"]["elevationSource"] = "design (near-flat city)"
    r["meta"]["notes"] = "Kano City Phase 1 after R-041. Free drive from the world map's Kano panel. Not a survey."
    r["traffic"] = dict(densityScale=0.0, speedLimitScale=1.0, vehicleMix=[])                  # traffic on rings is not modelled yet (the E5 test routes run without it)
    used = {"kano_arterial", "kano_collector", "kano_local", "kano_service"}
    start = (-U[0] * GATEWAY_LEN, -U[1] * GATEWAY_LEN)
    r["controlPoints"] = [cp("cp0", start[0], start[1], "kano_arterial", "urban"), cp("cp1", *along(start, GATEWAY_LEN * 0.5), "kano_arterial"), cp("cp2", 0.0, 0.0, "kano_arterial")]
    r["waypoints"] = [dict(id="wp_gateway", name="A2 gateway", kind="district", s=0.0), dict(id="wp_ringroad", name="Ring Road roundabout", kind="district", s=GATEWAY_LEN)]
    end, prev, total = (0.0, 0.0), ROUTE_ID, GATEWAY_LEN
    for rid, did, name, arms, link in SPINE:
        d = dict(DESIGNS[did]); d["radius"] = max(d["radius"], MIN_RADIUS); used.add(d["profile"]); rc, rout = E.ring_dims(d["profile"], d["radius"])
        centre = along(end, rout)
        legs = []                                                                  # the district arms are roads (frontage rules can follow roads, not generated legs)
        total += 2 * rout + sum(ln for _, _, _, ln in arms)
        ring = dict(id=rid, name=name, design=d["design"], profile=d["profile"], x=0.0, z=0.0, elevation=0.0, attachTo=prev, attachEnd="end", radius=d["radius"], apron=d["apron"],
                    island=d["island"], zone="urban", surface="good", flare=d["flare"], mouthScale=d.get("mouth", 1.0), splitterLength=d["splitter"][0], splitterWidth=d["splitter"][1],
                    yieldLines=True, zebra=True, legs=legs, connect=[],
                    props=[dict(id=pid, prop=pr, text="", x=round(px, 3), z=round(pz, 3), yawDeg=yaw, scale=1.0) for pid, pr, px, pz, yaw in d["island_props"]])
        for aid, ap, rel, ln in arms:                                              # a straight arm leaving the ring's outer edge radially
            b = math.radians(HEADING + rel); dvec = (math.cos(b), math.sin(b)); p0 = (centre[0] + dvec[0] * rout, centre[1] + dvec[1] * rout)
            pts = [p0, (p0[0] + dvec[0] * ln * 0.5, p0[1] + dvec[1] * ln * 0.5), (p0[0] + dvec[0] * ln, p0[1] + dvec[1] * ln)]
            r["roads"].append(dict(id=aid, name=aid.replace("_", " ").title(), role="road", controlPoints=[cp("%s%d" % (aid, i), x, z, ap) for i, (x, z) in enumerate(pts)], decks=[], joins=[]))
            ring["connect"].append(aid)
        if rid == "r_ringroad":
            for aid, side in (("ring_ne", -1), ("ring_sw", 1)):
                r["roads"].append(ring_road_arm(aid, centre, rout, side)); ring["connect"].append(aid); total += RING_ARM
        if link:
            lid, ll = link; a = along(centre, rout); b = along(a, ll)
            r["roads"].append(dict(id=lid, name=lid.replace("_", " ").title(), role="road", controlPoints=[cp("a", *a, "kano_arterial"), cp("m", *along(a, ll * 0.5), "kano_arterial"), cp("b", *b, "kano_arterial")], decks=[], joins=[]))
            ring["connect"] = ring["connect"] + [lid]; end, prev = b, lid; total += ll
        r["rings"].append(ring)
        # lights round the ring, as on the E5 test chain
        g = E.G[d["profile"]]; n_l = max(6, int(round(2 * math.pi * rc / 32.0))); L = 2 * math.pi * rc
        for k in range(n_l):
            r["props"].append(dict(id="%s_light%d" % (rid, k), prop="street_light", text="", corridor=rid, s=round((k + 0.5) * L / n_l, 2), lateral=round(g["formation"] + 0.6, 2), yawDeg=-90.0, scale=1.0))
    r["profiles"] = [PROF[k] for k in sorted(used)]
    # bus bays on the transport roundabout's terminal road
    r["symbols"] = [dict(id="bay_terminal_%d" % i, kind="bay", corridor="terminal_road", s=s, side=side, lane=0, length=0.0, count=2, yellow=False) for i, (s, side) in enumerate([(90.0, "right"), (90.0, "left"), (260.0, "right")])]
    # street lights, palms, kiosks and bus stops by cross-section (E1's rules)
    r["propRules"] = [p for p in e1["propRules"] if p["profiles"] in used]
    # the districts (R-041)
    rule = C.rule
    r["frontage"] = [
        rule("gateway", fewer_stations(C.HIGHWAY), C.GRAND, 3, corridor=ROUTE_ID, s_from=60.0, s_to=GATEWAY_LEN - 70.0, setback=(10.0, 16.0), gap=(4.0, 14.0), block=150.0, presence=0.8),
        rule("ring_industrial", fewer_stations(C.INDUSTRIAL), None, 0, corridor="ring_ne", s_from=50.0, setback=(10.0, 16.0), gap=(3.0, 8.0), block=120.0, presence=0.9),
        rule("ring_mixed", fewer_stations(C.MIDDLE + C.COMMERCIAL), C.NEIGHBOURHOOD, 3, corridor="ring_sw", s_from=50.0, setback=(10.0, 15.0), block=120.0, presence=0.9),
        rule("transport_district", fewer_stations(C.TRANSPORT), C.NEIGHBOURHOOD, 4, corridor="transport_road", s_from=50.0, s_to=850.0, setback=(10.0, 15.0), block=130.0, presence=0.9),
        rule("terminal_road", fewer_stations(C.TRANSPORT), None, 0, corridor="terminal_road", s_from=45.0, setback=(4.5, 8.0), block=110.0, presence=0.85),
        rule("service_road", fewer_stations(C.INDUSTRIAL), None, 0, corridor="service_road", s_from=45.0, setback=(8.0, 14.0), gap=(3.0, 8.0), block=100.0, presence=0.9),
        rule("commercial_belt", fewer_stations(C.COMMERCIAL + C.MAJOR), C.NEIGHBOURHOOD, 3, corridor="commercial_belt", s_from=50.0, s_to=850.0, setback=(10.0, 15.0), block=140.0, presence=0.95, rows=2),
        rule("modern_estate", fewer_stations(C.PREMIUM), C.LOCAL_MOSQUES, 3, corridor="estate_road", s_from=45.0, setback=(3.5, 6.0), gap=(1.0, 4.0), block=90.0, presence=0.92),
        rule("traditional_suburb", fewer_stations(C.TRADITIONAL), C.LOCAL_MOSQUES, 2, corridor="suburb_road", s_from=45.0, setback=(4.5, 7.0), block=90.0, presence=0.9),
        rule("central_district", fewer_stations(C.MAJOR), C.GRAND, 1, corridor="central_road", s_from=50.0, s_to=650.0, setback=(10.0, 15.0), block=150.0, presence=0.95, rows=2),
        rule("institutional", fewer_stations(C.EDGE), C.LOCAL_MOSQUES, 4, corridor="institutional_road", s_from=50.0, setback=(10.0, 18.0), gap=(6.0, 16.0), block=150.0, presence=0.8),
        rule("middle_class", fewer_stations(C.MIDDLE), C.LOCAL_MOSQUES, 3, corridor="middle_road", s_from=45.0, setback=(3.5, 6.0), gap=(1.0, 4.0), block=80.0, presence=0.92),
    ]
    r["meta"]["gameLengthKm"] = round(total / 1000.0, 2)
    r["schemaVersion"] = 3
    return r


def main():
    r = build(); p = C.ROUTES / (ROUTE_ID + ".route.json")
    text = json.dumps(r, indent=1, ensure_ascii=False) + "\n"
    if "--check" in sys.argv:
        ok = p.is_file() and p.read_text() == text; print(("up to date: " if ok else "OUT OF DATE: ") + str(p.name)); return 0 if ok else 1
    p.write_text(text); print("wrote", p, round(len(text) / 1024), "KB,", r["meta"]["gameLengthKm"], "km of road,", len(r["rings"]), "roundabouts,", len(r["frontage"]), "district rules")
    return 0


if __name__ == "__main__":
    sys.exit(main())
