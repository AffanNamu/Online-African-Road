"""Data of the KANO CITY MASTER PLAN (study area KSA-1). Everything is expressed in frame K0 (docs/kano/kano_world_frame.json: x east, z north, metres, origin = the canonical Kano anchor).
The geography rule: a position is REAL only when a source verifies it (the anchor, Gidan Rumfa, Dala Hill); every other district boundary, road and plot is an INDICATIVE FICTIONAL placement and says so.
Nothing here is surveyed, modelled or read by the game; it is the engine-neutral planning data that the road-network engine work (RB-16) and the production districts will consume."""
import math

CENTRE = (0.0, -200.0)                  # centre of the indicative old-city extent
WALL_A, WALL_B = 2300.0, 2100.0         # indicative walled-city ellipse: perimeter about 13.8 km, matching the 14 km UNESCO figure (Daily Trust says 17 km); the real wall line is NOT surveyed here
RING_R = 3300.0                         # indicative ring road radius around CENTRE
STUDY = (-5000.0, -5000.0, 5000.0, 5000.0)    # KSA-1: 10 km x 10 km = 100 km2 = 2,500 chunks of 200 m


def box(x0, z0, x1, z1, ch=70.0):
    """a rectangle with chamfered corners (a convex polygon)"""
    return [(x0 + ch, z0), (x1 - ch, z0), (x1, z0 + ch), (x1, z1 - ch), (x1 - ch, z1), (x0 + ch, z1), (x0, z1 - ch), (x0, z0 + ch)]


# ---------------------------------------------------------------------------------------------------------------------------------- districts
# kind: traditional | modern_residential | commercial | institutional | transport | industrial | gateway | landmark | market
# geography: REAL_ANCHORED (a verified landmark is inside) | INDICATIVE (position proposed, layout fictional) | FICTIONAL
# requires: reference ids that must exist before detailed modelling (R-xxx = supplied concept/research; M-xx = missing area of the register)
DISTRICTS = [
    dict(id="D11", name="A2 expressway gateway", kind="gateway", geography="INDICATIVE", fidelity="A", phase=1, polygon=box(-4900, -4800, -3800, -3800), density=0.5,
         requires=["R-007", "R-008", "R-012"], notes="The city entrance on the Kaduna-Zaria-Kano highway: gantry, divided expressway, curve, interchange approach. The only direction fixed by data is the approach from the south-west (the great-circle bearing Kaduna to Kano)."),
    dict(id="D10", name="Industrial and logistics zone", kind="industrial", geography="INDICATIVE", fidelity="A", phase=1, polygon=box(-3700, -3800, -2700, -2800), density=1.5,
         requires=["R-013", "R-014", "R-015"], notes="Warehouses, truck stops and fuel and service centres on the expressway and ring road."),
    dict(id="D09", name="Transport hub", kind="transport", geography="INDICATIVE", fidelity="A", phase=1, polygon=box(-2300, -2400, -1300, -1700), density=2.0,
         requires=["R-009"], notes="Central bus terminal with canopied bays, apron, gatehouse and perimeter wall near the south-west gate."),
    dict(id="D08", name="Institutional corridor", kind="institutional", geography="INDICATIVE", fidelity="A", phase=1, polygon=box(-400, 2400, 1000, 3600), density=3.0,
         requires=["R-010", "R-011"], notes="Hospital campus and police station on a divided arterial (concept references exist); schools, government buildings and a hotel follow in phase 2 (M-10)."),
    dict(id="D06", name="Modern residential estate", kind="modern_residential", geography="FICTIONAL", fidelity="A", phase=1, polygon=box(1000, 1700, 2300, 2900), density=12.0,
         requires=["R-006"], notes="Gated villas, compound walls and gates, one-way loop streets, paver sidewalks, painted kerbs, trees and streetlights (R-006)."),
    dict(id="D05", name="Commercial belt", kind="commercial", geography="INDICATIVE", fidelity="A", phase=2, polygon=box(2400, 500, 3600, 1700), density=25.0,
         requires=["M-02", "M-04"], notes="Major shopping streets, wholesale and small shops, busy intersections. Modern commercial frontage is not covered by any concept yet."),
    dict(id="D01", name="Old city hero sector", kind="traditional", geography="REAL_ANCHORED", fidelity="A", phase=3, polygon=box(-900, -1600, 700, 0), density=40.0,
         requires=["M-01", "M-12"], notes="Traditional Hausa compound fabric around the palace precinct. BLOCKED: no authentic reference exists yet; nothing is invented."),
    dict(id="D02", name="Palace and Central Mosque precinct", kind="landmark", geography="REAL_ANCHORED", fidelity="A", phase=4, polygon=box(-400, -1250, 300, -500), density=1.0, parent="D01",
         requires=["M-11"], notes="Gidan Rumfa (verified coordinate, about 33 acres) with the Great Mosque on its north side. BLOCKED on landmark references."),
    dict(id="D03", name="Kurmi market quarter", kind="market", geography="INDICATIVE", fidelity="A", phase=3, polygon=box(-880, -1450, -480, -900), density=60.0, parent="D01",
         requires=["M-04", "M-11"], notes="The historic market and its lanes. Position is an UNVERIFIED placeholder near the palace. BLOCKED on references."),
    dict(id="D04", name="Dala Hill district", kind="traditional", geography="REAL_ANCHORED", fidelity="A", phase=4, polygon=box(-1500, 700, -600, 1500), density=35.0,
         requires=["R-001", "M-11"], notes="Dala Hill (one sourced coordinate; height disputed) and the dense settlement around it. R-001 is the only visual reference, location unconfirmed."),
    dict(id="D07", name="Traditional residential suburb", kind="traditional", geography="FICTIONAL", fidelity="A", phase=3, polygon=box(-3900, -1100, -2700, 100), density=30.0,
         requires=["M-01", "M-12"], notes="Hausa compound neighbourhoods outside the wall. BLOCKED: no authentic reference yet."),
]

# ---------------------------------------------------------------------------------------------------------------------------------- filler zones (what the player sees BETWEEN the hero districts)
# Generated from the kit by rules, not hand-authored: plots and streets, LOD1 and baked blocks only. Densities are PLANNING ASSUMPTIONS.
FILLER = [
    dict(id="F-OLD", name="Old-city filler fabric", zone="Z-B", where="inside the old-city ellipse, outside the hero sector", character="traditional compound fabric (BLOCKED on references, M-01)", densityPlotsPerHa=40.0),
    dict(id="F-MID", name="Mixed urban filler", zone="Z-B", where="between the wall and the ring road, outside hero districts", character="modern residential, commercial frontage and institutional plots", densityPlotsPerHa=12.0),
    dict(id="F-OUT", name="Peri-urban fringe", zone="Z-C", where="beyond the ring road to the edge of the study area", character="low-density plots, farmland and scrub, terrain tint, baked skyline and impostors", densityPlotsPerHa=2.0),
]

# ---------------------------------------------------------------------------------------------------------------------------------- landmarks
# position status: VERIFIED (coordinates from sources, see COORDINATES.md) or UNVERIFIED PLACEHOLDER (a planning marker; never to be modelled until a source places it)
LANDMARKS = [
    dict(id="gidan_rumfa", name="Gidan Rumfa (Emir's Palace)", district="D02", xz=(-66.0, -874.0), position="VERIFIED within about 300 m (three web listings agree; not surveyed)", frame_id="gidan_rumfa", reference="M-11"),
    dict(id="dala_hill", name="Dala Hill", district="D04", xz=(-1061.0, 1017.0), position="VERIFIED from ONE source; height disputed (534 m or 453.67 m above sea level)", frame_id="dala_hill", reference="R-001, M-11"),
    dict(id="great_mosque", name="Great Mosque of Kano", district="D02", xz=(-70.0, -640.0), position="UNVERIFIED PLACEHOLDER: sources say it stands north of the palace", frame_id=None, reference="M-11"),
    dict(id="kurmi_market", name="Kurmi Market", district="D03", xz=(-680.0, -1180.0), position="UNVERIFIED PLACEHOLDER: in the old city near the palace", frame_id=None, reference="M-04, M-11"),
    dict(id="kofar_mata_dye_pits", name="Kofar Mata Dye Pits", district="D03", xz=(-860.0, -1050.0), position="UNVERIFIED PLACEHOLDER: old city, near the wall and the palace", frame_id=None, reference="M-11"),
]

# ---------------------------------------------------------------------------------------------------------------------------------- road classes (design parameters read from the concept composition, NOT surveyed)
ROAD_CLASSES = {
    "expressway": dict(lanesPerDirection=3, laneWidthM=3.65, median="planted, kerbed, about 4 m, double-arm lights (R-007, R-008); or concrete jersey barrier (R-012)", sidewalks=False, edge="W-beam guardrail on a black/white kerb base",
                       speedKmh=100, refs=["R-007", "R-008", "R-012"], needs=["RB-16", "RB-17", "RB-04", "RB-09"], supportedToday="partial: lanes, markings, jersey median and guardrail yes; planted median, painted kerbs, median lights no"),
    "arterial_ring": dict(lanesPerDirection=2, laneWidthM=3.5, median="planted, kerbed, about 3 m, double-arm lights (R-011)", sidewalks=True, edge="paver sidewalk and painted kerb", speedKmh=70, refs=["R-011"], needs=["RB-16", "RB-17", "RB-04", "RB-09", "RB-12"], supportedToday="partial"),
    "arterial": dict(lanesPerDirection=2, laneWidthM=3.5, median="none or planted, by section", sidewalks=True, edge="paver sidewalk and painted kerb", speedKmh=60, refs=["R-011"], needs=["RB-16", "RB-17", "RB-04", "RB-12"], supportedToday="partial"),
    "collector": dict(lanesPerDirection=1, laneWidthM=3.5, median="none", sidewalks=True, edge="paver sidewalk and painted kerb", speedKmh=40, refs=["R-006"], needs=["RB-16", "RB-17", "RB-04"], supportedToday="partial: a junction stub off one main road, 150 m apart, no network"),
    "local": dict(lanesPerDirection=1, laneWidthM=3.5, median="none (one-way loops use two lanes, R-006)", sidewalks=True, edge="paver sidewalk, painted kerb, grass strip", speedKmh=30, refs=["R-006"], needs=["RB-16", "RB-17", "RB-04", "RB-12"], supportedToday="partial"),
    "lane": dict(lanesPerDirection=1, laneWidthM=3.0, median="none", sidewalks=False, edge="wall faces; no reference", speedKmh=15, refs=[], needs=["RB-16", "RB-17", "M-12 (no reference yet)"], supportedToday="partial; width and surface unknown until M-12"),
}

# node types and what the engine does with them today
NODE_TYPES = {
    "terminal": dict(rb=[], today="supported (road end)"),
    "gateway": dict(rb=["RB-07"], today="prop only: a gantry set piece; its text needs RB-07"),
    "T": dict(rb=[], today="supported with limits: undivided road, at least 150 m between junctions, straight stub"),
    "cross4": dict(rb=["RB-02", "RB-16"], today="not supported"),
    "roundabout": dict(rb=["RB-01", "RB-16"], today="not supported"),
    "interchange": dict(rb=["RB-03", "RB-16"], today="not supported"),
    "gate_cross": dict(rb=["RB-02", "RB-16"], today="not supported (a four-way crossing at an old-city gate)"),
}


def _dist(a, b): return math.hypot(a[0] - b[0], a[1] - b[1])


def build_network():
    nodes, edges = {}, []
    def node(id, x, z, typ, note=""): nodes[id] = dict(id=id, xz=(round(x, 1), round(z, 1)), type=typ, note=note)
    def edge(a, b, cls, name=""): edges.append(dict(id=f"E{len(edges) + 1:02d}", a=a, b=b, cls=cls, lengthM=round(_dist(nodes[a]["xz"], nodes[b]["xz"]), 1), name=name))
    def pol(ang, ra, rb=None):
        a = math.radians(ang); return CENTRE[0] + ra * math.cos(a), CENTRE[1] + (rb if rb else ra) * math.sin(a)
    main_angs = [0, 45, 90, 135, 180, 225, 270, 315]; all_angs = [i * 22.5 for i in range(16)]
    ring_types = {0: "roundabout", 90: "roundabout", 180: "roundabout", 270: "roundabout", 225: "interchange"}
    # outer ring road: 16 junctions (indicative circle)
    for ang in all_angs:
        x, z = pol(ang, RING_R); node(f"RR{ang:g}", x, z, ring_types.get(ang, "T"), "outer ring road junction")
    for i, ang in enumerate(all_angs):
        edge(f"RR{ang:g}", f"RR{all_angs[(i + 1) % 16]:g}", "arterial_ring", "outer ring road")
    # old-city gates on the indicative wall ellipse, joined by an inner ring along the wall
    for ang in main_angs:
        x, z = pol(ang, WALL_A, WALL_B); node(f"G{ang}", x, z, "gate_cross", "old-city gate crossing (indicative position)")
    for i, ang in enumerate(main_angs):
        edge(f"G{ang}", f"G{main_angs[(i + 1) % 8]}", "collector", "inner ring along the wall")
    # radial arterials ring -> gate, crossed at the middle by a middle ring road (a collector circle)
    MID_R = (RING_R + WALL_A) / 2.0
    for ang in main_angs:
        x, z = pol(ang, MID_R, (RING_R + WALL_B) / 2.0); node(f"MA{ang}", x, z, "cross4", "radial arterial crossing the middle ring")
        edge(f"RR{ang}", f"MA{ang}", "arterial", f"radial arterial {ang} deg"); edge(f"MA{ang}", f"G{ang}", "arterial", f"radial arterial {ang} deg")
    for i, ang in enumerate(main_angs):
        nxt = main_angs[(i + 1) % 8]; mid = ang + 22.5; x, z = pol(mid, MID_R, (RING_R + WALL_B) / 2.0); node(f"MM{mid:g}", x, z, "T", "middle ring junction with a sub-radial")
        edge(f"MA{ang}", f"MM{mid:g}", "collector", "middle ring"); edge(f"MM{mid:g}", f"MA{nxt}", "collector", "middle ring")
        edge(f"RR{mid:g}", f"MM{mid:g}", "collector", "sub-radial")
    # the expressway from the south-west to the ring interchange
    node("X_T1", -4900, -4700, "terminal", "A2 highway enters the study area from the south-west")
    t = 0.30; rx, rz = nodes["RR225"]["xz"]; node("X_GW", -4900 + t * (rx + 4900), -4700 + t * (rz + 4700), "gateway", "gantry and city entrance (D11)")
    t2 = 0.62; node("X_LOG", -4900 + t2 * (rx + 4900), -4700 + t2 * (rz + 4700), "T", "service-road junction for the logistics zone (D10)")
    edge("X_T1", "X_GW", "expressway", "A2 highway"); edge("X_GW", "X_LOG", "expressway", "A2 highway"); edge("X_LOG", "RR225", "expressway", "A2 highway")
    # district access nodes with a collector to the nearest network junction
    for d in DISTRICTS:
        xs = [p[0] for p in d["polygon"]]; zs = [p[1] for p in d["polygon"]]; node(f"AN_{d['id']}", (min(xs) + max(xs)) / 2, (min(zs) + max(zs)) / 2, "T", f"district access ({d['id']})")
    pool = [n for n in nodes if n.startswith(("MA", "MM", "RR", "G")) and n != "RR225"]
    for d in DISTRICTS:
        if d.get("parent") or d["id"] == "D01": continue      # D01 is joined to the network by its two south gates below
        best = {"D11": "X_GW", "D10": "X_LOG"}.get(d["id"]) or min(pool, key=lambda n: _dist(nodes[n]["xz"], nodes[f"AN_{d['id']}"]["xz"])); edge(f"AN_{d['id']}", best, "collector", f"collector to {d['id']}")
    edge("AN_D01", "G270", "collector", "collector from the south gate"); edge("AN_D01", "G225", "collector", "collector from the south-west gate"); edge("AN_D02", "AN_D01", "local", "palace forecourt street"); edge("AN_D03", "AN_D01", "lane", "market lanes")
    edge("AN_D06", "AN_D05", "collector", "east collector"); edge("AN_D06", "AN_D08", "collector", "north collector"); edge("AN_D09", "AN_D10", "collector", "logistics link"); edge("AN_D04", "AN_D07", "collector", "west collector")
    return nodes, edges


# ---------------------------------------------------------------------------------------------------------------------------------- district layouts (planning diagrams for the hero districts with references)
# Each item: (label, class of element, centre x, centre z, width, depth, yaw degrees) in the district's local metres (origin = district centre). All sizes are planning envelopes read from the concept's composition.
LAYOUTS = {
    "D06": dict(title="Modern residential estate (R-006)", size=(1300, 1200), note="Perimeter collector, one-way loop local streets, blocks of 140 x 80 m with six plots of about 22 x 32 m on each long side."),
    "D08": dict(title="Institutional corridor (R-010, R-011)", size=(1400, 1200), note="Divided arterial through the centre; hospital campus on one side, police station and future school, government building and hotel on the other."),
    "D09": dict(title="Transport hub (R-009)", size=(1000, 700), note="Terminal building, canopied bays, apron with a landscaped island, gatehouse and barrier, parking, perimeter wall."),
    "D10": dict(title="Industrial and logistics zone (R-013 to R-015)", size=(1000, 1000), note="Warehouse with six docks, yard, truck-parking field, gatehouse, a truck fuel and service centre, perimeter wall; expressway frontage."),
    "D11": dict(title="A2 expressway gateway (R-007, R-008, R-012)", size=(1100, 1000), note="Gantry across the divided carriageway, planted median with double-arm lights, a curve with chevron signs and guardrails, and the approach to the interchange."),
}

# ---------------------------------------------------------------------------------------------------------------------------------- streaming and budget model (assumptions are explicit and tested)
STREAMING = dict(
    assumptions=dict(frustumFraction=0.30, mergedAtlasesPerChunk=2, chunkHa=4.0, occlusionCredit=0.0,
                     note="The camera sees a fraction of the circle around the player; the model counts that fraction and gives no credit for occlusion. Plot densities are PLANNING ASSUMPTIONS to be replaced by reference measurement."),
    bandEdgesM=[0, 60, 150, 350, 800, 1500], capsTrisPerPlot=[5000, 1500, 120, 12, 3],
    bandProduct=["prefab LOD0", "prefab LOD1", "baked block mesh (atlas)", "baked block mesh, coarse", "impostor or skyline mesh"], individualBands=[True, True, False, False, False],
    tierRadiusScale=dict(low=0.6, medium=1.0, high=1.0), targets=dict(triangleShare=0.45, drawShare=0.6),
    currentEngine=dict(productionRing=2, chunkM=200, note="ChunkStreamer instantiates production prefabs one by one for chunks within 2 rings (a 5 x 5 chunk square, 100 ha), with no batching: the model counts one draw per plot in view."),
)
