"""The asset table: id, kind, builder, collision boxes (Blender axes) and the size the approved breakdown (docs/KANO_TRANSPORT_ASSET_BREAKDOWN.md) was written against."""
from __future__ import annotations
from .registry import spec
from . import logistics as A

# ---- Family A: logistics and warehouse site
spec("rg_ngn_warehouse_hall_a", "buildings", A.warehouse_hall_a, [(-24, -11, 0, 24, 11, 10), (-23.9, -12, 0, -16, -4, 4.2)], "pitched-roof steel-frame warehouse, six docks, platform, office annex", target=[48, 24, 10])
spec("rg_ngn_warehouse_hall_b", "buildings", A.warehouse_hall_b, [(-18, -9, 0, 18, 9, 8)], "low-bay distribution shed, sawtooth roof, four docks, platform canopy", target=[36, 20, 8])
spec("rg_ngn_warehouse_cold_a", "buildings", A.warehouse_cold_a, [(-15, -8, 0, 15, 8, 7.4)], "insulated cold store, plant deck with condensers, three docks", target=[30, 18, 9])
spec("aro_dock_leveller_set", "props", A.dock_leveller, [(-1.75, -0.675, 0, 1.75, 0.675, 4.4)], "one dock bay on a 3.5 m pitch: wall, roller door, bumpers, shelter, platform, leveller", target=[3.5, 1.2, 4.4])
for v in "abc": spec(f"aro_container_20ft_{v}", "props", A.container_20(v), [(-3.03, -1.22, 0, 3.03, 1.22, 2.59)], "20 ft shipping container, plain livery, no brand", target=[6.1, 2.4, 2.6])
for v in "ab": spec(f"aro_container_40ft_{v}", "props", A.container_40({"a": "a", "b": "d"}[v]), [(-6.1, -1.22, 0, 6.1, 1.22, 2.59)], "40 ft shipping container, plain livery, no brand", target=[12.2, 2.4, 2.6])
spec("aro_container_stack_3", "props", A.container_stack, [(-6.1, -1.22, 0, 6.1, 1.22, 7.8)], "three 40 ft containers stacked, merged into one mesh", target=[12.2, 2.4, 7.8])
spec("aro_pallet_stack_a", "props", A.pallet_stack, [(-1.35, -1.05, 0, 1.35, 1.15, 1.8)], "wrapped pallets, sacks, cartons and a crate", target=[3, 2, 1.8])
spec("aro_gate_sliding_a", "props", A.gate_sliding, [(-1.0, -1.0, 0, 1.0, 1.0, 2.75), (-8.4, -0.15, 0, -4.95, 0.15, 2.0), (4.95, -0.15, 0, 8.4, 0.15, 2.0)], "two-lane yard gate with a guard hut on the island, leaves open", target=[11, 1.2, 2.6])
spec("aro_fence_palisade_set", "props", A.fence_palisade, [(-2, -0.06, 0, 2, 0.06, 2.4)], "4 m steel palisade fence run", target=[4, 0.1, 2.4])
spec("aro_wall_plot_set", "props", A.wall_plot, [(-2, -0.125, 0, 2, 0.125, 2.4)], "4 m rendered plot wall run", target=[4, 0.25, 2.4])
spec("aro_weighbridge_a", "props", A.weighbridge, [(-3.7, 0.8, 0, 0.7, 3.2, 3.0)], "18 m truck weighbridge with cabin and display; the deck is drive-over (no collision)", target=[18, 3.6, 3.2])
spec("aro_fuel_island_truck", "props", A.fuel_island_truck, [(-4.2, -3.0, 0, -2.8, 3.0, 1.6), (2.8, -3.0, 0, 4.2, 3.0, 1.6)] + [(sx - 0.28, sy - 0.28, 0, sx + 0.28, sy + 0.28, 5.8) for sx in (-6.0, 6.0) for sy in (-3.0, 3.0)], "truck fuel island: canopy, two pump islands, bollards", target=[16, 8, 6.5])
spec("aro_forklift_a", "props", A.forklift, [(-0.55, -1.6, 0, 0.55, 1.6, 2.2)], "static forklift (prop only)", target=[3.2, 1.2, 2.2])
spec("aro_truck_trailer_parked_a", "props", A.trailer_flatbed, [(-1.25, -6.5, 0, 1.25, 6.5, 1.4), (-1.25, -6.5, 1.4, 1.25, -6.3, 2.7)], "parked flatbed semi-trailer, no cab", target=[13, 2.5, 4])
spec("aro_truck_trailer_parked_b", "props", A.trailer_box, [(-1.25, -6.5, 0, 1.25, 6.5, 4.0)], "parked box semi-trailer, no cab", target=[13, 2.5, 4])
spec("aro_yard_mast_light", "lights", A.mast_light, [(-0.3, -0.3, 0, 0.3, 0.3, 6.0)], "20 m high-mast floodlight with four heads", target=[1.2, 1.2, 20])
spec("aro_bollard_set", "props", A.bollard, [(-0.15, -0.15, 0, 0.15, 0.15, 1.0)], "steel and concrete bollard, yellow-black", target=[0.3, 0.3, 1])

# ---- Family B: bus terminals and stops
from . import terminal as B
spec("kano_bus_terminal_hall_a", "buildings", B.terminal_hall, [(-20, -5, 0, 20, 11, 9.25)] + [(-20 + 2.5 + i * 5.0 - 0.22, -10.72, 0, -20 + 2.5 + i * 5.0 + 0.22, -10.28, 5.0) for i in range(8)], "bus terminal building: glazed concourse and ticket hall, arrivals canopy, brise-soleil upper volume", target=[40, 22, 9])
spec("kano_bus_shelter_long_a", "props", B.bus_shelter_long, [], "long platform canopy for three bays: benches, route panels, bay-number fascia; no collision (open canopy)", target=[18, 4, 4])
spec("aro_bus_shelter_a", "props", B.bus_shelter_single, [], "single-bay roadside shelter with bench and stop pole; no collision", target=[5, 2, 3])
spec("kano_ticket_kiosk_a", "props", B.ticket_kiosk, [(-1.0, -0.35, 0, 1.0, 1.15, 2.8)], "ticket booth with window, counter and queue rail", target=[3, 3, 2.8])
spec("kano_departure_board_a", "props", B.departure_board, [(-1.37, -0.07, 0, -1.23, 0.07, 4.5), (1.23, -0.07, 0, 1.37, 0.07, 4.5)], "double-face departures board on two posts", target=[3, 0.3, 4.5])
spec("kano_minibus_stand_canopy_a", "props", B.minibus_stand, [(-6.0, 2.55, 0, 6.0, 2.75, 0.9)], "minibus stand: tin roof on poles, route boards, bench, queue rail", target=[12, 6, 3.6])
spec("kano_tricycle_rank_a", "props", B.tricycle_rank, [], "tricycle rank: small canopy, bench, painted kerb, queue marks; no collision", target=[8, 5, 3])
spec("aro_parking_bay_barrier_set", "props", B.parking_bay_barrier, [(-1.25, -0.4, 0, 1.25, 0.3, 0.6)], "parking-bay wheel stop, low rail and planter box (2.5 m tile)", target=[2.5, 0.4, 0.6])

# ---- Family C: road furniture and street kit
from . import furniture as C3
from mathutils import Matrix


def shifted(f, dx=0.0, dy=0.0):
    """The same builder moved so that the footprint centre is the origin."""
    def g(lod):
        p = f(lod); p.xform(0, Matrix.Translation((dx, dy, 0))); return p
    return g


spec("aro_streetlight_arm_single_a", "lights", C3.streetlight_single, [], "single-arm 9 m street light with a cobra-head luminaire", target=[4.4, 0.6, 9])
spec("aro_streetlight_double_median_a", "lights", C3.streetlight_double, [], "twin-arm 12 m street light for a planter median", target=[6, 0.6, 12])
spec("aro_streetlight_ornamental_a", "lights", C3.streetlight_ornamental, [], "decorative 6 m lantern pole for islands and plazas", target=[1, 1, 6])
for _n, _f in (("stop", C3.sign_stop), ("yield", C3.sign_yield), ("speed50", C3._speed(50)), ("speed80", C3._speed(80)), ("no_entry", C3.sign_no_entry), ("bus_stop", C3.sign_bus_stop), ("zebra", C3.sign_zebra), ("no_left_turn", C3.sign_no_left_turn)):
    spec(f"aro_road_sign_{_n}_a", "signs", _f, [], f"road sign '{_n}' on a 3 m post (one placement per sign; the breakdown's 'post set' row)", target=[0.8, 0.1, 3])
spec("aro_road_sign_direction_a", "signs", shifted(C3.sign_direction, dy=-0.065), [], "blue-green destination sign on two posts with a blank text field", target=[3, 0.3, 4])
spec("aro_gantry_sign_a", "environments", C3.gantry, [(-12.3, -0.3, 0, -11.7, 0.3, 5.7), (11.7, -0.3, 0, 12.3, 0.3, 5.7)], "overhead sign gantry over a 2 x 3 lane carriageway (24 m between the legs), 5.6 m clearance", target=[20, 1, 7])
spec("aro_traffic_signal_a", "props", C3.traffic_signal, [], "signal mast with an arm over the road and two three-aspect heads (static)", target=[5, 0.4, 6])
spec("aro_guardrail_post_set", "props", shifted(C3.guardrail, dy=-0.075), [(-2.0, -0.105, 0, 2.0, 0.125, 0.9)], "4 m W-beam guardrail run with posts and a tapered end", target=[4, 0.3, 0.8])
spec("aro_jersey_barrier_a", "props", C3.jersey_barrier, [(-1.5, -0.3, 0, 1.5, 0.3, 0.8)], "3 m concrete safety barrier module", target=[3, 0.6, 0.8])
spec("aro_kerb_painted_run_a", "props", C3.kerb_painted, [(-1.0, -0.125, 0, 1.0, 0.125, 0.15)], "2 m painted island kerb run", target=[2, 0.25, 0.15])
spec("aro_drain_grate_set", "props", shifted(C3.drain_set, dx=0.25), [], "drain inlet grate and manhole cover as thin raised plates", target=[0.6, 0.6, 0.03])
spec("aro_bench_a", "props", C3.bench, [(-0.9, -0.3, 0, 0.9, 0.3, 0.9)], "street bench", target=[1.8, 0.6, 0.9])
spec("aro_litter_bin_a", "props", C3.litter_bin, [(-0.25, -0.25, 0, 0.25, 0.25, 0.9)], "street litter bin", target=[0.5, 0.5, 0.9])
spec("aro_planter_island_a", "props", C3._planter(4.0), [(-2.0, -1.0, 0, 2.0, 1.0, 0.6)], "kerbed planter box, 4 m", target=[4, 2, 0.6])
spec("aro_planter_island_b", "props", C3._planter(8.0), [(-4.0, -1.0, 0, 4.0, 1.0, 0.6)], "kerbed planter box, 8 m", target=[8, 2, 0.6])
spec("kano_utility_pole_set", "props", C3.utility_pole, [], "11 m utility pole with a transformer, insulators and cable stubs", target=[0.4, 0.4, 11])
