"""COMPLETE ENVIRONMENTS for the in-game review (owner request of 2026-11-03: "complete, believable environments rather than isolated assets"): three places, each in the dry and the wet season, composed from the
environmental kit (and the residential kit's pole): a Hausa-style quarter lane, a village lane and a market street. A scene is a list of placements (Blender axes: street along +Y, right +X, up +Z, metres, as the
residential review scene) plus named cameras; Unity builds the same arrangement from the same prefabs (KanoReviewBuilder, "?smoke=kanostreet&scene=<name>").

WHAT THESE ARE NOT. They are review compositions of assets, not game content: nothing places them in the game, nothing in the game reads them, and the wet versions are NOT a rainy-season simulation (no mud physics,
no flooding behaviour, no weather: the puddles and the flood sheet are static meshes). The Hausa-style assets have no real reference (M-01); the vernacular forms are not verified (M-14); seasonal vegetation is
not established (M-07, M-17). The layouts are design choices.

Placement rules (stated because they are how the scenes are checked): a frontage asset stands with its FRONT (local -Y) towards the road, on the right of the road rotated -90 degrees and on the left +90 (as
kano_kit.assemble); its front face is put on a plane at a fixed distance from the road axis; buildings never overlap each other or the carriageway (tests/test_env_scenes.py)."""
from __future__ import annotations
import math

SEASONS = ("dry", "wet")
LANE_PATCH = 24.0
SOLID = {"rg_ngn_hausa_compound_a", "rg_ngn_hausa_compound_b", "rg_ngn_hausa_wall_gate_set_a", "rg_ngn_house_mudbrick_a", "rg_ngn_house_mudbrick_b", "rg_ngn_house_block_corrugated_a", "rg_ngn_shop_row_roadside_a",
         "rg_ngn_shed_market_corrugated_a", "rg_ngn_kiosk_lockup_a", "rg_ngn_bougainvillea_wall_a"}                                   # assets that must not overlap each other (footprint check)
ROAD_IDS = {"aro_road_earth_compacted_a", "aro_road_mud_rutted_a", "aro_road_sand_loose_a", "aro_road_asphalt_cracked_a", "aro_road_pothole_set_a"}


class Scene:
    def __init__(self, name: str, season: str, lay: dict, ground: str, note: str):
        self.name = name; self.season = season; self.lay = lay; self.ground = ground; self.note = note; self.items: list[dict] = []; self.views: list[dict] = []

    # ---- low level
    def add(self, aid: str, x: float, y: float, yaw: float = 0.0, z: float = 0.0, tag: str = ""):
        self.items.append({"asset": aid, "x": round(x, 3), "y": round(y, 3), "z": round(z, 3), "yawDeg": round(yaw, 2), "scale": 1.0, "lod": 0, "tag": tag}); return self.items[-1]

    def bb(self, aid: str): return self.lay[aid]["bbox"]

    # ---- ground patches: the nominal surface at z = 0
    def road(self, aid: str, y_centres, x: float = 0.0):
        b = self.bb(aid); d = self.lay[aid]["datum"] or 0.0; xc = (b[0] + b[3]) / 2; yc = (b[1] + b[4]) / 2
        for y in y_centres: self.add(aid, x - xc, y - yc, 0.0, -d, "road")

    def drains(self, y0: float, y1: float, x: float, step: float = 6.0):
        """A run of open drains along the road edge (the asset's slab top is 0.85 m above its origin; it is placed that much lower)."""
        b = self.bb("aro_drain_open_slab_a"); top = b[5] - 0.10; y = y0
        while y + step <= y1 + 1e-6:
            self.add("aro_drain_open_slab_a", x - (b[0] + b[3]) / 2, y + step / 2 - (b[1] + b[4]) / 2, 0.0, -0.85, "drain"); y += step

    # ---- frontage: the front (-Y) towards the road
    def front(self, aid: str, side: int, plane: float, y_centre: float, tag: str = "", z: float = 0.0):
        """Puts the asset on side `side` (+1 right, -1 left) with the front face on the plane |x| = plane and the middle of its width at y_centre."""
        b = self.bb(aid); f = -b[1]; xc = (b[0] + b[3]) / 2                                       # distance of the front face from the origin; middle of the width along the asset's own x
        if side > 0: return self.add(aid, plane + f, y_centre + xc, -90.0, z, tag)                # yaw -90: local +x runs towards -y, local -y towards -x
        return self.add(aid, -(plane + f), y_centre - xc, 90.0, z, tag)                          # yaw +90: local +x runs towards +y, local -y towards +x

    def width(self, aid: str) -> float: b = self.bb(aid); return b[3] - b[0]

    def view(self, name: str, loc, look, lens: float = 28.0):
        self.views.append({"name": name, "loc": list(loc), "look": list(look), "lens": lens})

    def as_json(self) -> dict:
        return {"scene": f"env_{self.name}_{self.season}", "frame": "street along +Y, right +X, metres, Blender axes", "kind": "environment", "season": self.season, "ground": self.ground, "note": self.note, "views": self.views, "items": self.items}


def _veg(s: Scene, tree: str, grass: str, tree_xy, grass_xy):
    for (x, y) in tree_xy: s.add(tree, x, y, 0.0, 0.0, "tree")
    for (x, y) in grass_xy: s.add(grass, x, y, 0.0, 0.0, "grass")


def _views(s: Scene, y0: float, y1: float):
    s.view("drive", (-1.7, y0, 1.3), (0.4, y0 + 60.0, 2.2), 28.0)                           # the driver's eye, left lane, looking along the lane
    s.view("aerial", (-24.0, y0 + 6.0, 13.0), (3.0, (y0 + y1) / 2 + 2.0, 1.5), 28.0)        # three-quarter, from above the left side


def hausa_quarter(season: str, lay: dict) -> Scene:
    """A lane in a Hausa-style quarter: a compacted-earth lane (one stretch of rutted mud in the wet season, puddles), open drains each side, walled compounds and entrance frontages on both sides, large shade trees whose
    crowns overhang the lane, grass on the verges. 72 m."""
    wet = season == "wet"; s = Scene("hausa_quarter", season, lay, "grass_green" if wet else "dry_earth", "a lane in a Hausa-style quarter (compounds and frontages designed from documented qualitative features; no reference, M-01)")
    ys = [-36.0, -12.0, 12.0, 36.0]; s.road("aro_road_earth_compacted_a", [y for i, y in enumerate(ys) if not (wet and i == 1)])
    if wet: s.road("aro_road_mud_rutted_a", [ys[1]])
    s.drains(-48.0, 48.0, 4.75); s.drains(-48.0, 48.0, -4.75)
    P = 6.3                                                                                      # front-face plane, metres from the lane axis
    s.front("rg_ngn_hausa_compound_a", 1, P, -20.0, "compound"); s.front("rg_ngn_hausa_wall_gate_set_a", 1, P, -1.0, "frontage"); s.front("rg_ngn_hausa_compound_b", 1, P, 15.0, "compound"); s.front("rg_ngn_hausa_wall_gate_set_a", 1, P, 33.0, "frontage")
    s.front("rg_ngn_hausa_wall_gate_set_a", -1, P, -17.0, "frontage"); s.front("rg_ngn_hausa_compound_b", -1, P, -1.9, "compound"); s.front("rg_ngn_hausa_compound_a", -1, P, 18.0, "compound"); s.front("rg_ngn_hausa_wall_gate_set_a", -1, P, 37.0, "frontage")
    b = s.bb("rg_ngn_house_mudbrick_b"); s.add("rg_ngn_house_mudbrick_b", P + 7.5 + 3.0, -1.0 + (b[0] + b[3]) / 2, -90.0, 0.0, "house")      # a house inside the compound behind the entrance frontage, its roof visible over the wall
    tree = "rg_ngn_tree_large_shade_wet_a" if wet else "rg_ngn_tree_large_shade_a"; grass = "rg_ngn_grass_tall_wet_a" if wet else "rg_ngn_grass_tall_verge_a"
    _veg(s, tree, grass, [(5.8, -28.0), (5.8, 6.0), (5.8, 24.0), (-5.8, -22.0), (-5.8, 10.0), (-5.8, 30.0)], [(5.6, -3.0), (5.6, -10.5), (-5.6, -8.0), (-5.6, 14.0), (5.6, 28.0), (-5.6, 42.0), (5.6, 44.0), (-5.6, -40.0)])
    if wet: s.add("aro_water_puddle_set_a", -(s.bb("aro_water_puddle_set_a")[0] + s.bb("aro_water_puddle_set_a")[3]) / 2, 12.0, 0.0, 0.0, "puddles"); s.add("aro_water_puddle_set_a", 0.0, -34.0, 0.0, 0.0, "puddles")
    _views(s, -44.0, 44.0); return s


def village(season: str, lay: dict) -> Scene:
    """A village lane: a compacted-earth (dry) or rutted-mud (wet) lane with puddles, mud-brick and block houses with woven fences between them, banana plants behind the houses, shade trees, a rough canopy stall."""
    wet = season == "wet"; s = Scene("village", season, lay, "grass_green" if wet else "dry_earth", "a village lane (houses, fences, banana plants: R-020, R-021; vernacular not verified, M-14)")
    road = "aro_road_mud_rutted_a" if wet else "aro_road_earth_compacted_a"; s.road(road, [-36.0, -12.0, 12.0, 36.0])
    P = 5.2
    s.front("rg_ngn_house_mudbrick_a", 1, P, -26.0, "house"); s.front("rg_ngn_house_block_corrugated_a", 1, P, -8.0, "house"); s.front("rg_ngn_house_mudbrick_b", 1, P, 12.0, "house"); s.front("rg_ngn_house_mudbrick_a", 1, P, 32.0, "house")
    s.front("rg_ngn_house_mudbrick_b", -1, P, -30.0, "house"); s.front("rg_ngn_house_mudbrick_a", -1, P, -12.0, "house"); s.front("rg_ngn_house_block_corrugated_a", -1, P, 6.0, "house"); s.front("rg_ngn_house_mudbrick_b", -1, P, 26.0, "house")
    fw = s.width("rg_ngn_fence_woven_timber_a")
    for side, ys_ in ((1, (-17.4, 1.9, 22.5)), (-1, (-20.5, -3.3, 15.8))):                      # woven fences along the plot lines between the houses, running back from the road, three modules each
        for y in ys_:
            for k in range(3): s.add("rg_ngn_fence_woven_timber_a", side * (P + 1.6 + k * fw), y, 0.0, 0.0, "fence")
    banana = [(15.0, -26.0), (15.5, -8.0), (16.0, 12.0), (-16.0, -30.0), (-16.5, -12.0), (-15.5, 6.0), (-16.0, 26.0), (8.0, -20.4), (-8.0, 18.8), (17.5, 32.0)]
    for (x, y) in banana: s.add("rg_ngn_banana_plant_a", x, y, 0.0, 0.0, "banana")
    tree = "rg_ngn_tree_large_shade_wet_a" if wet else "rg_ngn_tree_large_shade_a"; grass = "rg_ngn_grass_tall_wet_a" if wet else "rg_ngn_grass_tall_verge_a"
    _veg(s, tree, grass, [(4.6, -19.6), (4.6, 24.8), (-4.6, -22.6), (-4.6, 40.0), (4.6, -42.0), (19.0, 2.0), (-19.0, -6.0), (19.5, 22.0)], [(4.5, -14.0), (-4.5, -6.0), (4.5, 9.0), (-4.5, 13.0), (4.5, 30.0), (-4.5, 34.0), (4.5, 42.0), (-4.5, -36.0)])
    b = s.bb("rg_ngn_stall_canopy_rough_a"); s.front("rg_ngn_stall_canopy_rough_a", 1, 4.6, -1.0, "stall")
    if wet:
        for y in (-30.0, -6.0, 18.0, 40.0): s.add("aro_water_puddle_set_a", -(s.bb("aro_water_puddle_set_a")[0] + s.bb("aro_water_puddle_set_a")[3]) / 2, y, 0.0, 0.0, "puddles")
    _views(s, -44.0, 44.0); return s


def market_street(season: str, lay: dict) -> Scene:
    """A market street: a worn asphalt road with a pothole stretch, roadside shop rows, an open market shed, lock-up kiosks, table stalls with umbrellas on the pavement, open drains, bougainvillea over a wall, a utility pole
    with overhead wires, shade trees. In the wet season: puddles on the asphalt and a sheet of flood water at the low end."""
    wet = season == "wet"; s = Scene("market_street", season, lay, "grass_green" if wet else "dry_earth", "a roadside market street (shops, kiosks, shed: R-016 to R-021; the real Kano markets have no reference, M-04, M-12)")
    s.road("aro_road_asphalt_cracked_a", [-36.0, -12.0, 24.0]); s.road("aro_road_pothole_set_a", [6.0])
    s.drains(-48.0, 36.0, 4.2); s.drains(-48.0, 36.0, -4.2)
    P = 5.6
    s.front("rg_ngn_shop_row_roadside_a", 1, P, -30.0, "shops"); s.front("rg_ngn_kiosk_lockup_a", 1, P, -13.0, "kiosk"); s.front("rg_ngn_shop_row_roadside_a", 1, P, 2.0, "shops"); s.front("rg_ngn_shed_market_corrugated_a", 1, P + 0.4, 25.0, "shed")
    s.front("rg_ngn_shop_row_roadside_a", -1, P, -26.0, "shops"); s.front("rg_ngn_kiosk_lockup_a", -1, P, -8.0, "kiosk")
    s.front("rg_ngn_bougainvillea_wall_a", -1, P + 0.5, 1.0, "wall"); s.front("rg_ngn_bougainvillea_wall_a", -1, P + 0.5, 5.6, "wall"); s.front("rg_ngn_kiosk_lockup_a", -1, P, 16.0, "kiosk"); s.front("rg_ngn_house_block_corrugated_a", -1, P + 0.5, 30.0, "house")
    for i, y in enumerate((-36.0, -33.0, -22.5)): s.add("rg_ngn_stall_umbrella_a", 4.7 if i < 2 else 4.5, y, -90.0, 0.0, "stall")                  # table stalls on the pavement in front of the shops, customer side (-Y) turned to the road
    s.front("rg_ngn_stall_canopy_rough_a", -1, 4.6, -17.0, "stall"); s.front("rg_ngn_stall_umbrella_a", 1, 4.5, 12.0, "stall")
    for y in (-40.0, -10.0, 20.0): s.add("aro_pole_electric_concrete_span_a", -4.9, y, 0.0, 0.0, "pole")
    tree = "rg_ngn_tree_large_shade_wet_a" if wet else "rg_ngn_tree_large_shade_a"; grass = "rg_ngn_grass_tall_wet_a" if wet else "rg_ngn_grass_tall_verge_a"
    _veg(s, tree, grass, [(-5.0, 9.0), (5.0, -20.0), (-5.0, 36.0), (5.0, 34.0)], [(4.9, -2.0), (-4.9, -2.5), (4.9, 18.0), (-4.9, 24.0), (4.9, -44.0), (-4.9, -44.0)])
    if wet:
        pz = s.bb("aro_water_puddle_set_a"); xp = -(pz[0] + pz[3]) / 2
        for y in (-12.0, 22.0): s.add("aro_water_puddle_set_a", xp, y, 0.0, 0.0, "puddles")
        fz = s.bb("aro_water_flood_sheet_a"); d = s.lay["aro_water_flood_sheet_a"]["datum"] or 0.0; s.add("aro_water_flood_sheet_a", -(fz[0] + fz[3]) / 2, -43.0 - (fz[1] + fz[4]) / 2, 0.0, -d, "flood")
    _views(s, -46.0, 40.0); return s


BUILDERS = {"hausa_quarter": hausa_quarter, "village": village, "market_street": market_street}


def all_scenes(lay: dict) -> list[Scene]:
    return [fn(season, lay) for fn in BUILDERS.values() for season in SEASONS]


def asset_ids() -> set:
    """Every asset id the scenes use (needs a layout only for sizes: a dummy layout with unit boxes is enough to list them)."""
    dummy = {k: {"bbox": [-1, -1, 0, 1, 1, 1], "datum": 0.0} for k in (ROAD_IDS | SOLID | {"aro_drain_open_slab_a", "aro_water_puddle_set_a", "aro_water_flood_sheet_a", "rg_ngn_fence_woven_timber_a", "rg_ngn_stall_umbrella_a", "rg_ngn_stall_canopy_rough_a",
              "rg_ngn_banana_plant_a", "rg_ngn_tree_large_shade_a", "rg_ngn_tree_large_shade_wet_a", "rg_ngn_grass_tall_verge_a", "rg_ngn_grass_tall_wet_a"})}
    return {it["asset"] for s in all_scenes(dummy) for it in s.items}


def footprint(it: dict, bbox) -> tuple:
    """The world-space rectangle (x0, y0, x1, y1) of an item from its asset's bbox and its yaw (a multiple of 90 degrees)."""
    x0, y0, _, x1, y1, _ = bbox; yaw = int(round(it["yawDeg"])) % 360; pts = [(x0, y0), (x1, y0), (x1, y1), (x0, y1)]
    c, s_ = round(math.cos(math.radians(yaw))), round(math.sin(math.radians(yaw))); rot = [(c * x - s_ * y + it["x"], s_ * x + c * y + it["y"]) for x, y in pts]
    xs = [p[0] for p in rot]; ys = [p[1] for p in rot]; return (min(xs), min(ys), max(xs), max(ys))
