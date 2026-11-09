"""REVIEW SCENES of the transport kit for the in-game review: three compositions (a logistics yard, a bus terminal with its stops, and a lineup of the road furniture) built only from kit assets, as placement files
the game's review builder already understands (the same format and the same Unity builder as the environmental scenes of env_kit.scenes: `Assets/ThirdParty/ARO/placements/env_<name>_dry.json`, shown with
"?smoke=kanostreet&scene=env_<name>_dry&view=<view>").

WHAT THESE ARE NOT. They are review compositions, not game content: nothing in the game places them or reads them. Layouts are design choices (district D09 and D10 have no surveyed plan: M-0x gaps), and the
catalogue entries / sockets they rely on are not yet in the shipped prop catalogue. Blender axes: x right, y along the street, z up, metres; an asset's front is -Y; yaw is about Z.

Placement rules (checked by tests/test_transport_scenes.py): every asset exists in the kit; footprints (from the measured bounds) never overlap; the camera of every view stands outside every footprint."""
from __future__ import annotations
import json, math
from pathlib import Path

CATALOG = Path(__file__).resolve().parents[3] / "docs" / "kano" / "TRANSPORT_KIT_catalog.json"
SEASON = "dry"


def load_bounds(path=CATALOG) -> dict:
    d = json.loads(Path(path).read_text()); return {k: (tuple(v[0]), tuple(v[1])) for k, v in d["bounds"].items()}


class Scene:
    def __init__(self, name: str, ground: str, note: str, bounds: dict):
        self.name = name; self.ground = ground; self.note = note; self.b = bounds; self.items: list[dict] = []; self.views: list[dict] = []

    def add(self, aid: str, x: float, y: float, yaw: float = 0.0, z: float = 0.0, tag: str = ""):
        if aid not in self.b: raise KeyError(aid)
        self.items.append({"asset": aid, "x": round(x, 3), "y": round(y, 3), "z": round(z, 3), "yawDeg": round(yaw, 2), "scale": 1.0, "lod": 0, "tag": tag}); return self.items[-1]

    def run(self, aid: str, x0, y0, n: int, dx=0.0, dy=0.0, yaw=0.0, tag=""):
        for k in range(n): self.add(aid, x0 + dx * k, y0 + dy * k, yaw, 0.0, tag)

    def view(self, name: str, loc, look, lens: float = 28.0): self.views.append({"name": name, "loc": list(loc), "look": list(look), "lens": lens})

    def as_json(self) -> dict:
        return {"scene": f"env_{self.name}_{SEASON}", "frame": "street along +Y, right +X, metres, Blender axes", "kind": "environment", "season": SEASON, "ground": self.ground, "note": self.note, "views": self.views, "items": self.items}


def footprint(item: dict, bounds: dict) -> tuple:
    """Axis-aligned footprint (xmin, ymin, xmax, ymax) of a placed asset in world axes. Yaws are multiples of 90 degrees in these scenes, so the box is exact."""
    (x0, y0, _), (x1, y1, _) = bounds[item["asset"]]; a = math.radians(item["yawDeg"]); c, s = round(math.cos(a), 6), round(math.sin(a), 6)
    pts = [(x * c - y * s + item["x"], x * s + y * c + item["y"]) for x in (x0, x1) for y in (y0, y1)]
    return (min(p[0] for p in pts), min(p[1] for p in pts), max(p[0] for p in pts), max(p[1] for p in pts))


# ------------------------------------------------------------------------------------------------------------------------------------------------ the three scenes
def logistics_yard(b: dict) -> Scene:
    """A walled logistics yard 140 m wide and 76 m deep behind a two-lane gate: a weighbridge in the entry lane, three warehouses (two halls and a cold store), a fuel island, parked trailers, container stacks,
    a forklift and pallets by the docks, dock bays, high-mast lights and bollards. Vehicles come from -Y."""
    s = Scene("logistics_yard", "concrete", "logistics yard composed from family A (review composition, not game content)", b)
    s.add("aro_gate_sliding_a", 0.0, 0.0, 0, tag="gate")
    for k in range(14): s.add("aro_fence_palisade_set", 11.0 + 4.0 * k, 0.0, 0, tag="fence"); s.add("aro_fence_palisade_set", -11.0 - 4.0 * k - 0.04, 0.0, 180, tag="fence")
    for k in range(19):
        for sx, yaw in ((70.0, 90), (-70.0, -90)): s.add("aro_fence_palisade_set", sx, 4.0 + 4.0 * k, yaw, tag="fence")
    for k in range(35): s.add("aro_wall_plot_set", -68.0 + 4.0 * k, 76.0, 0, tag="wall")
    s.add("aro_weighbridge_a", -4.05, 20.0, 90, tag="weighbridge")
    s.add("rg_ngn_warehouse_hall_a", -36.0, 62.0, 0, tag="hall"); s.add("rg_ngn_warehouse_hall_b", 16.0, 62.0, 0, tag="hall"); s.add("rg_ngn_warehouse_cold_a", 50.0, 61.0, 0, tag="cold")
    s.add("aro_fuel_island_truck", -46.0, 12.0, 0, tag="fuel")
    for k, v in enumerate("abab"): s.add(f"aro_truck_trailer_parked_{v}", -56.0 + 4.2 * k, 32.0, 0, tag="trailer")
    for k, v in enumerate("abc"): s.add(f"aro_container_20ft_{v}", 46.0, 24.0 + 3.0 * k, 0, tag="container")
    s.add("aro_container_40ft_a", 58.0, 24.0, 0, tag="container"); s.add("aro_container_40ft_b", 58.0, 27.0, 0, tag="container")
    s.add("aro_container_stack_3", 46.0, 38.0, 0, tag="container"); s.add("aro_container_stack_3", 59.0, 38.0, 0, tag="container")
    s.add("aro_forklift_a", -30.0, 44.0, 0, tag="forklift"); s.add("aro_pallet_stack_a", -36.0, 45.0, 0, tag="pallets"); s.add("aro_pallet_stack_a", -24.0, 46.0, 0, tag="pallets")
    for k in range(3): s.add("aro_dock_leveller_set", 56.0 + 3.5 * k, 10.0, 0, tag="dock")
    for (x, y) in ((-24.0, 24.0), (24.0, 24.0), (-4.0, 44.0), (30.0, 46.0)): s.add("aro_yard_mast_light", x, y, 0, tag="mast")
    for (x, y) in ((-6.5, 8.0), (0.6, 8.0), (-6.5, 30.0), (0.6, 30.0)): s.add("aro_bollard_set", x, y, 0, tag="bollard")
    s.view("drive", (-2.75, -35.0, 2.3), (-2.75, 40.0, 3.5), 24); s.view("aerial", (0.0, -75.0, 75.0), (0.0, 40.0, 0.0), 26)
    s.view("docks", (-36.0, 28.0, 3.0), (-36.0, 52.0, 4.0), 26); s.view("yard", (30.0, -8.0, 7.0), (30.0, 38.0, 3.0), 26)
    return s


def bus_terminal(b: dict) -> Scene:
    """A bus terminal beside a road: the terminal building and its covered platform on the right, a ticket kiosk and a departures board in front, a minibus stand, a tricycle rank, a bus shelter, kerb planters
    and street lights on the left, street lights, signs and benches along both kerbs. The road runs along +Y."""
    s = Scene("bus_terminal", "asphalt_worn", "bus terminal and stops composed from family B and C (review composition, not game content)", b)
    s.add("kano_bus_terminal_hall_a", 46.0, 0.0, -90, tag="terminal"); s.add("kano_bus_shelter_long_a", 22.0, 0.0, -90, tag="shelter")
    s.add("kano_ticket_kiosk_a", 28.0, 14.0, -90, tag="kiosk"); s.add("kano_departure_board_a", 30.0, -14.0, -90, tag="board")
    s.add("kano_minibus_stand_canopy_a", -30.0, -8.0, 90, tag="minibus"); s.add("kano_tricycle_rank_a", -26.0, 16.0, 90, tag="tricycle"); s.add("aro_bus_shelter_a", -16.0, 32.0, 90, tag="shelter")
    for k in range(6): s.add("aro_parking_bay_barrier_set", -14.0, -40.0 + 2.5 * k, 90, tag="barrier")
    for y in (-30.0, -10.0, 10.0, 30.0): s.add("aro_streetlight_arm_single_a", 11.0, y, -90, tag="light"); s.add("aro_streetlight_arm_single_a", -11.0, y + 10.0, 90, tag="light")
    for y in (-14.0, 0.0, 14.0): s.add("aro_bench_a", 14.0, y + 6.0, -90, tag="bench")
    for y in (-8.0, 8.0): s.add("aro_litter_bin_a", 14.5, y + 6.0, 0, tag="bin")
    s.add("aro_road_sign_bus_stop_a", 11.0, -20.0, -90, tag="sign"); s.add("aro_road_sign_zebra_a", 11.0, -4.0, -90, tag="sign")
    s.add("aro_road_sign_speed50_a", -11.0, -40.0, 90, tag="sign"); s.add("aro_road_sign_no_left_turn_a", -11.0, 24.0, 90, tag="sign"); s.add("aro_road_sign_direction_a", 10.0, -48.0, -90, tag="sign")
    s.add("aro_planter_island_a", -10.0, -14.0, 90, tag="planter"); s.add("aro_planter_island_b", -10.0, 30.0, 90, tag="planter")
    s.view("drive", (0.0, -60.0, 2.3), (0.0, 30.0, 3.5), 24); s.view("aerial", (-15.0, -60.0, 50.0), (30.0, 0.0, 0.0), 28)
    s.view("front", (6.0, -12.0, 2.4), (40.0, 0.0, 4.0), 30); s.view("close", (12.0, -12.0, 2.2), (24.0, 6.0, 2.0), 32)
    return s


def furniture_lineup(b: dict) -> Scene:
    """A lineup of the road furniture, nearest row first: all the signs, the street lights and the mast, the barriers, kerbs, drain, planters, benches and bins, then the gantry, a signal mast and utility poles.
    Everything faces -Y, the camera stands in front."""
    s = Scene("road_furniture", "asphalt_worn", "road furniture lineup from family C (review composition, not game content)", b)
    signs = ("stop", "yield", "speed50", "speed80", "no_entry", "bus_stop", "zebra", "no_left_turn")
    for k, n in enumerate(signs): s.add(f"aro_road_sign_{n}_a", -20.0 + 4.0 * k, 0.0, 0, tag="sign")
    s.add("aro_road_sign_direction_a", 16.0, 0.0, 0, tag="sign")
    for k, n in enumerate(("aro_streetlight_arm_single_a", "aro_streetlight_arm_single_a", "aro_streetlight_double_median_a", "aro_streetlight_ornamental_a", "aro_streetlight_ornamental_a", "aro_yard_mast_light")):
        s.add(n, -20.0 + 8.0 * k, 12.0, 0, tag="light")
    for k in range(3): s.add("aro_guardrail_post_set", -22.0 + 4.0 * k, 24.0, 0, tag="barrier")
    for k in range(3): s.add("aro_jersey_barrier_a", -8.0 + 3.0 * k, 24.0, 0, tag="barrier")
    s.add("aro_kerb_painted_run_a", 4.0, 24.0, 0, tag="kerb"); s.add("aro_kerb_painted_run_a", 6.0, 24.0, 0, tag="kerb"); s.add("aro_drain_grate_set", 9.0, 24.0, 0, tag="drain")
    s.add("aro_planter_island_a", 14.0, 24.0, 0, tag="planter"); s.add("aro_planter_island_b", 24.0, 24.0, 0, tag="planter")
    s.add("aro_bench_a", -20.0, 30.0, 0, tag="bench"); s.add("aro_bench_a", -17.5, 30.0, 0, tag="bench"); s.add("aro_litter_bin_a", -14.0, 30.0, 0, tag="bin"); s.add("aro_litter_bin_a", -13.0, 30.0, 0, tag="bin")
    for k in range(4): s.add("aro_bollard_set", -10.0 + 1.0 * k, 30.0, 0, tag="bollard")
    s.add("aro_gantry_sign_a", 0.0, 42.0, 0, tag="gantry"); s.add("aro_traffic_signal_a", 34.0, 38.0, 0, tag="signal")
    s.add("kano_utility_pole_set", -42.0, 40.0, 0, tag="pole"); s.add("kano_utility_pole_set", -26.0, 40.0, 0, tag="pole")
    s.view("front", (0.0, -40.0, 3.5), (0.0, 24.0, 4.0), 24); s.view("aerial", (0.0, -35.0, 45.0), (0.0, 24.0, 0.0), 28)
    s.view("signs", (-2.0, -9.0, 2.2), (-2.0, 0.0, 2.4), 30); s.view("lights", (-2.0, 3.0, 4.0), (0.0, 12.0, 6.0), 26)
    return s


def all_scenes(bounds: dict | None = None) -> list[Scene]:
    b = bounds or load_bounds(); return [logistics_yard(b), bus_terminal(b), furniture_lineup(b)]


def asset_ids(bounds: dict | None = None) -> set:
    return {i["asset"] for s in all_scenes(bounds) for i in s.items}
