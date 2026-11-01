"""The reference-to-asset production matrix: every generated concept reference (R-006 to R-015) broken into individual 3D assets, each classified GLOBAL, REGIONAL, CITY_SPECIFIC or LANDMARK,
with the reusable components, the city-specific components, the Blender tasks, the Unity tasks and the engine dependencies. Plus the assets the traditional and landmark districts will need,
which have NO concept yet and are therefore blocked: nothing is invented for them.
Id convention: GLOBAL aro_*, REGIONAL rg_<region>_* (ngn = Northern Nigeria), CITY_SPECIFIC kano_*, LANDMARK lmk_kano_*."""

G, R, C, L = "GLOBAL", "REGIONAL", "CITY_SPECIFIC", "LANDMARK"
ASSETS = {}


def A(id, name, cls, cat, refs, deps=(), flag="", district=()):
    ASSETS[id] = dict(id=id, name=name, cls=cls, category=cat, refs=list(refs), engineDeps=list(deps), flags=flag, districts=list(district))


# ---------------------------------------------------------------- R-006 gated modern residential street (D06)
A("aro_villa_2storey_hip_a", "Two-storey villa, hipped dark-tile roof, balcony (type A)", G, "buildings", ["R-006"], district=["D06"])
A("aro_villa_2storey_hip_b", "Two-storey villa, hipped dark-tile roof, balcony (type B)", G, "buildings", ["R-006"], district=["D06"])
A("aro_wall_compound_panel_a", "Compound wall module: rendered cream with grey cladding panel", G, "buildings", ["R-006"], district=["D06"])
A("aro_pier_stone_a", "Wall pier with stone cladding and coping", G, "buildings", ["R-006"], district=["D06"])
A("aro_gate_ornate_steel_a", "Ornate black steel double gate", G, "props", ["R-006"], district=["D06"])
A("aro_lantern_wall_a", "Wall-mounted lantern", G, "lights", ["R-006"], district=["D06"])
A("aro_balcony_glass_a", "Balcony with glass balustrade (kit)", G, "buildings", ["R-006"], district=["D06"])
A("aro_roof_hip_darktile_a", "Hipped dark-tile roof (kit)", G, "buildings", ["R-006"], district=["D06"])
A("aro_streetlight_single_arm_a", "Single-arm streetlight", G, "lights", ["R-006", "R-011"], district=["D06", "D08"])
A("aro_sign_oneway_a", "One Way regulatory sign (blue square, arrow)", G, "signs", ["R-006"], district=["D06"])
A("aro_kerb_painted_blackwhite_a", "Painted black-and-white kerb module", G, "environment", ["R-006", "R-007", "R-008", "R-011", "R-012"], ["RB-04"], district=["D06", "D08", "D11"])
A("aro_sidewalk_paver_a", "Interlocking-paver sidewalk module (pink and grey)", G, "environment", ["R-006", "R-011"], ["RB-04"], district=["D06", "D08"])
A("aro_marking_arrow_straight_a", "Pavement arrow symbol", G, "environment", ["R-006", "R-009", "R-010", "R-013", "R-014", "R-015"], ["RB-12"], district=["D06", "D08", "D09", "D10"])
A("aro_hedge_clipped_a", "Clipped hedge strip", G, "vegetation", ["R-006", "R-010"], district=["D06", "D08"])
A("aro_shrub_ixora_red_a", "Red-flowering ornamental shrub", G, "vegetation", ["R-006"], flag="species to confirm", district=["D06"])
A("rg_ngn_tree_street_broadleaf_a", "Broad-leaf street tree (form A)", R, "vegetation", ["R-006", "R-007", "R-008", "R-012"], flag="the species that really line Kano streets are NOT established (M-07)", district=["D06", "D11"])
A("rg_ngn_tree_street_broadleaf_b", "Broad-leaf street tree (form B)", R, "vegetation", ["R-006", "R-007"], flag="as above", district=["D06", "D11"])
# ---------------------------------------------------------------- R-007, R-008, R-012 expressway and A2 gantry (D11)
A("aro_streetlight_double_arm_median_a", "Double-arm streetlight for a median", G, "lights", ["R-007", "R-008", "R-011", "R-012"], ["RB-09"], district=["D11", "D08"])
A("aro_median_planter_kerbed_a", "Kerbed planter median module with shrubs", G, "environment", ["R-007", "R-008", "R-011"], ["RB-04", "RB-17"], district=["D11", "D08"])
A("aro_median_barrier_jersey_a", "Concrete jersey-barrier median module", G, "environment", ["R-012"], [], district=["D11"])
A("aro_guardrail_wbeam_a", "W-beam guardrail with posts", G, "environment", ["R-007", "R-008", "R-012"], [], district=["D11"])
A("aro_sign_chevron_curve_a", "Curve chevron sign", G, "signs", ["R-007"], [], district=["D11"])
A("aro_gantry_truss_a", "Steel-truss overhead gantry spanning a dual carriageway", G, "signs", ["R-008", "R-012"], ["RB-07"], district=["D11"])
A("aro_sign_panel_overhead_green_a", "Overhead green sign panel (text per instance)", G, "signs", ["R-008", "R-012"], ["RB-07"], flag="text must come from verified route data (the concept's distances do not match the sources)", district=["D11"])
A("aro_sign_km_marker_a", "Roadside distance sign (text per instance)", G, "signs", ["R-008", "R-012"], ["RB-07"], flag="text must come from verified route data", district=["D11"])
A("rg_ngn_terrain_rock_cutslope_a", "Rock-cut road slope", R, "environment", ["R-007"], ["RB-14"], flag="hilly terrain; not the flat plain at Kano", district=["D11"])
A("rg_ngn_hill_rocky_backdrop_a", "Rocky green hill backdrop", R, "environment", ["R-007", "R-008", "R-012"], ["RB-14"], flag="hilly terrain; not the flat plain at Kano", district=["D11"])
A("rg_ngn_tree_savanna_scattered_a", "Scattered savanna tree", R, "vegetation", ["R-007"], flag="species to confirm (M-07)", district=["D11"])
# ---------------------------------------------------------------- R-009 bus terminal (D09)
A("rg_ngn_screen_geometric_arch_a", "Arched panel with geometric lattice (facade kit)", R, "buildings", ["R-009", "R-010"], flag="pattern is the concept's; a real Kano pattern needs a reference", district=["D09", "D08"])
A("aro_bus_terminal_hall_a", "Terminal hall: glass facade, sign band, arched side panels", G, "buildings", ["R-009"], district=["D09"])
A("aro_canopy_bus_bay_solar_a", "Bus-bay canopy with solar roof and numbered posts", G, "buildings", ["R-009"], district=["D09"])
A("kano_bus_terminal_a", "Kano bus terminal: assembled complex, livery and signage", C, "buildings", ["R-009"], flag="the name is illustrative", district=["D09"])
A("aro_coach_a", "Parked coach (four liveries as material variants)", G, "props", ["R-009"], flag="close-range fidelity conflicts with the 2,500-triangle prop budget: owner decision", district=["D09"])
A("aro_suv_parked_a", "Parked SUV (generic, no brand)", G, "props", ["R-009"], flag="same budget conflict as the coach", district=["D09"])
A("aro_guardhouse_a", "Guardhouse with flat roof", G, "buildings", ["R-009", "R-011", "R-014", "R-015"], district=["D09", "D08", "D10"])
A("aro_barrier_arm_gate_a", "Barrier-arm gate", G, "props", ["R-009", "R-014"], district=["D09", "D10"])
A("aro_wall_perimeter_pier_rail_a", "Perimeter wall with piers and black railing", G, "buildings", ["R-009", "R-011"], district=["D09", "D08"])
A("aro_planter_island_kerbed_a", "Kerbed landscaped island", G, "environment", ["R-009", "R-010", "R-013"], ["RB-13"], district=["D09", "D08", "D10"])
A("aro_kerb_painted_yellowblack_a", "Painted yellow-and-black kerb module", G, "environment", ["R-009", "R-013", "R-014", "R-015"], ["RB-04"], district=["D09", "D10"])
A("aro_bollard_yellow_a", "Yellow safety bollard", G, "props", ["R-009", "R-013", "R-014", "R-015"], district=["D09", "D10"])
A("aro_bench_a", "Bench", G, "props", ["R-009", "R-010"], district=["D09", "D08"])
A("aro_bin_a", "Litter bin", G, "props", ["R-010"], district=["D08"])
A("aro_marking_zebra_crossing_a", "Zebra crossing", G, "environment", ["R-009", "R-010"], ["RB-12"], district=["D09", "D08"])
A("aro_marking_bay_set_a", "Painted parking-bay set", G, "environment", ["R-009", "R-010", "R-013", "R-014", "R-015"], ["RB-12", "RB-13"], district=["D09", "D08", "D10"])
A("aro_sign_totem_service_a", "Totem sign with service list (text per instance)", G, "signs", ["R-009", "R-010", "R-013"], ["RB-07"], district=["D09", "D08", "D10"])
A("aro_sign_building_band_a", "Building name band (text per instance)", G, "signs", ["R-009", "R-010", "R-011", "R-013", "R-014"], ["RB-07"], district=["D09", "D08", "D10"])
# ---------------------------------------------------------------- R-010 hospital (D08)
A("aro_hospital_block_glass_a", "Hospital central block with glass curtain and screens", G, "buildings", ["R-010"], district=["D08"])
A("aro_hospital_wing_a", "Hospital wing with window bands", G, "buildings", ["R-010"], district=["D08"])
A("aro_canopy_emergency_a", "Emergency entrance canopy (red)", G, "buildings", ["R-010"], district=["D08"])
A("aro_ambulance_a", "Parked ambulance", G, "props", ["R-010"], flag="real-organisation livery: owner decision; prop budget conflict", district=["D08"])
A("aro_covered_walkway_a", "Covered walkway", G, "props", ["R-010"], district=["D08"])
A("aro_bollard_light_a", "Light bollard", G, "lights", ["R-010"], district=["D08"])
A("aro_palm_ornamental_a", "Ornamental palm", G, "vegetation", ["R-010"], district=["D08"])
A("kano_hospital_a", "Hospital campus: assembled, with a fictional name", C, "buildings", ["R-010"], flag="the concept's name resembles a real institution", district=["D08"])
A("aro_telecom_mast_a", "Lattice telecom mast", G, "props", ["R-010", "R-011"], district=["D08"])
# ---------------------------------------------------------------- R-011 police station (D08)
A("aro_police_station_a", "Station block with entrance portico", G, "buildings", ["R-011"], flag="the crest imitates a real force's emblem: owner decision", district=["D08"])
A("kano_police_station_a", "Police station: assembled, with a local identity", C, "buildings", ["R-011"], flag="as above", district=["D08"])
A("aro_police_pickup_a", "Police pickup (livery)", G, "props", ["R-011"], flag="real-organisation livery: owner decision; prop budget conflict", district=["D08"])
A("aro_gate_sliding_steel_a", "Sliding steel gate", G, "props", ["R-011", "R-014"], district=["D08", "D10"])
A("aro_flagpole_a", "Flagpole with the national flag", G, "props", ["R-011"], district=["D08"])
# ---------------------------------------------------------------- R-013, R-014, R-015 fuel, warehouse and yard (D10)
A("aro_fuel_canopy_truck_a", "Truck fuel canopy with numbered islands", G, "buildings", ["R-013"], flag="generic colours; no real brand", district=["D10"])
A("aro_fuel_pump_a", "Fuel pump", G, "props", ["R-013"], district=["D10"])
A("aro_shop_restaurant_a", "Shop-and-restaurant building", G, "buildings", ["R-013"], district=["D10"])
A("aro_service_garage_a", "Service garage with workshop bays", G, "buildings", ["R-013"], district=["D10"])
A("aro_truck_tractor_a", "Tractor unit (several liveries)", G, "props", ["R-013", "R-014", "R-015"], flag="prop budget conflict at close range", district=["D10"])
A("aro_trailer_box_a", "Box trailer", G, "props", ["R-013", "R-014", "R-015"], flag="prop budget conflict at close range", district=["D10"])
A("aro_marking_truck_symbol_a", "Truck-parking symbol marking", G, "environment", ["R-013", "R-014", "R-015"], ["RB-12"], district=["D10"])
A("aro_speedhump_yellowblack_a", "Yellow-and-black speed hump", G, "environment", ["R-013", "R-015"], ["RB-12"], district=["D10"])
A("aro_warehouse_steel_large_a", "Large steel warehouse with six dock doors", G, "buildings", ["R-014", "R-015"], district=["D10"])
A("aro_dock_door_bay_a", "Loading-dock door bay with number (kit)", G, "buildings", ["R-014", "R-015"], district=["D10"])
A("aro_site_office_a", "Site office with glazed front", G, "buildings", ["R-014"], district=["D10"])
A("aro_water_tower_a", "Elevated water tower", G, "props", ["R-014"], district=["D10"])
A("aro_pallet_stack_a", "Pallet and box stack", G, "props", ["R-014", "R-015"], district=["D10"])
A("aro_wall_perimeter_plain_a", "Plain perimeter wall with piers", G, "buildings", ["R-013", "R-014"], district=["D10"])
A("aro_streetlight_pole_tall_yard_a", "Tall yard light with multi-head fixture", G, "lights", ["R-013", "R-014", "R-015"], district=["D10"])
A("aro_drain_grating_strip_a", "Drain grating strip across an entrance", G, "environment", ["R-015"], ["RB-05"], district=["D10"])
# ---------------------------------------------------------------- BLOCKED: no concept yet (nothing is invented)
for id, name, cls, cat, deps, ref, d in [
    ("rg_ngn_hausa_compound_a", "Traditional Hausa compound with zaure, parapet and zankwaye (type A)", R, "buildings", [], "M-01", ["D01", "D07"]),
    ("rg_ngn_hausa_compound_b", "Traditional Hausa compound, two-storey front block (type B)", R, "buildings", [], "M-01", ["D01", "D07"]),
    ("rg_ngn_hausa_compound_c", "Traditional Hausa compound, small plot (type C)", R, "buildings", [], "M-01", ["D01", "D07"]),
    ("rg_ngn_hausa_wall_gate_set_a", "Hausa compound wall, parapet and gate set", R, "buildings", [], "M-01", ["D01", "D07"]),
    ("kano_old_city_lane_set_a", "Old-city lane set: surface, drains, wall faces", C, "environment", ["RB-17"], "M-12", ["D01", "D03"]),
    ("kano_market_stall_set_a", "Traditional market stalls and shelters", C, "props", [], "M-04", ["D03"]),
    ("kano_shop_row_modern_a", "Modern shop row frontage", C, "buildings", [], "M-04", ["D05"]),
    ("kano_plaza_a", "Commercial plaza", C, "buildings", [], "M-02", ["D05"]),
    ("kano_midrise_a", "Mid-rise commercial or office block", C, "buildings", [], "M-02", ["D05"]),
    ("kano_neighbourhood_mosque_a", "Neighbourhood mosque oriented to the qibla", C, "buildings", [], "M-03", ["D01", "D06", "D07"]),
    ("aro_school_block_a", "School block with yard", G, "buildings", [], "M-10", ["D08"]),
    ("aro_government_building_a", "Government office building", G, "buildings", [], "M-10", ["D08"]),
    ("aro_hotel_a", "Hotel block with forecourt", G, "buildings", [], "M-10", ["D08"]),
    ("lmk_kano_gidan_rumfa_complex", "Gidan Rumfa (Emir's Palace) complex and gate", L, "buildings", [], "M-11", ["D02"]),
    ("lmk_kano_great_mosque", "Great Mosque of Kano", L, "buildings", [], "M-11", ["D02"]),
    ("lmk_kano_kurmi_market_hall", "Kurmi Market", L, "buildings", [], "M-11", ["D03"]),
    ("lmk_kano_kofar_mata_dye_pits", "Kofar Mata dye pits", L, "environment", [], "M-11", ["D03"]),
    ("lmk_kano_city_wall_gate_a", "City wall segment and gate", L, "buildings", [], "M-11", ["D01"]),
    ("lmk_kano_dala_hill_massif", "Dala Hill massif", L, "environment", ["RB-14"], "R-001, M-11", ["D04"]),
]:
    A(id, name, cls, cat, [], deps, flag=f"BLOCKED: no concept or verified reference yet ({ref})", district=d)
    ASSETS[id]["blockedBy"] = ref

REFS = {  # per concept reference: scene, district, what to compare in the game, reusable and city-specific components
    "R-006": dict(scene="Gated modern residential street, one-way", districts=["D06"], camera="eye-level, 1.7 m, looking down the street from the kerb line, 28 mm",
                  check=["house heights and roof shape", "wall height, pier spacing and gate width", "kerb stripe and paver pattern", "streetlight height and spacing", "tree size and spacing", "lane count, arrows, one-way sign"]),
    "R-007": dict(scene="Curved dual-carriageway expressway", districts=["D11"], camera="driver's eye, 1.4 m, on the left carriageway through the bend, 28 mm",
                  check=["lanes per direction and widths", "median width, kerb pattern, planting", "guardrail and kerb base", "double-arm light spacing", "chevron signs", "cut slope and hill backdrop"]),
    "R-008": dict(scene="A2 gantry, planter median", districts=["D11"], camera="driver's eye, 1.4 m, 80 m before the gantry, 28 mm", check=["gantry span and height", "sign proportions and text layout", "median and lights", "distance sign"]),
    "R-009": dict(scene="Bus terminal", districts=["D09"], camera="aerial three-quarter view from the gate, 35 mm", check=["building massing and facade panels", "canopy bays and numbering", "apron markings and island", "gatehouse, barrier and wall", "vehicle liveries"]),
    "R-010": dict(scene="Hospital campus", districts=["D08"], camera="aerial three-quarter view from the main road, 35 mm", check=["block and wing massing", "emergency canopy and ambulance bay", "signage totem", "parking, markings and island", "lighting and planting"]),
    "R-011": dict(scene="Police station beside a divided arterial", districts=["D08"], camera="elevated three-quarter view along the arterial, 35 mm", check=["station massing and portico", "gate, wall and guardhouse", "flagpoles", "arterial cross-section and median", "streetlights and sidewalk"]),
    "R-012": dict(scene="A2 gantry, concrete median barrier", districts=["D11"], camera="driver's eye, 1.4 m, 80 m before the gantry, 28 mm", check=["jersey-barrier median", "guardrail with kerb base", "gantry and sign layout"]),
    "R-013": dict(scene="Truck fuel and service centre", districts=["D10"], camera="aerial three-quarter view from the entrance, 35 mm", check=["canopy and island numbering", "shop and garage massing", "truck-parking bays and symbols", "kerbs and speed humps", "truck liveries"]),
    "R-014": dict(scene="Warehouse and logistics yard, aerial", districts=["D10"], camera="aerial three-quarter view from the gate, 35 mm", check=["warehouse massing and six docks", "office, guardhouse, gate", "truck-parking field", "water tower and fuel canopy", "perimeter wall"]),
    "R-015": dict(scene="Warehouse yard, ground level", districts=["D10"], camera="driver's eye, 1.4 m, at the gate, 28 mm", check=["dock doors and numbers", "guardhouse and air-conditioner", "speed hump and drain grating", "painted bay"]),
}
BLENDER_TASKS = ["write the concept down as a table of proportions, materials and details; the owner corrects it", "blockout from reusable kit modules; render at the reference camera and compare", "detail modelling: real walls, roofs, openings, structure and ornament (no image planes)",
                 "UV unwrap and PBR material set (shared M_<surface> names; atlas per family)", "LOD chain (prefab LOD0 and LOD1; merged tiers come from the baker) and collision (box or convex, never the render mesh)", "validate_asset against the game's budgets",
                 "save a versioned .blend and export FBX and GLB with a manifest"]
UNITY_TASKS = ["Unity import test (orientation, scale, materials, collision, LOD thresholds)", "prefab build (identity root, own LODGroup and BoxCollider, shared-kit materials)", "catalogue entry and placement data", "in-game screenshot from the reference camera and side-by-side comparison; deviations recorded",
               "performance measurement ([Perf] draw calls, triangles, texture memory)"]
STATUS_VOCAB = ["needs_concept", "concept_approved", "blockout_completed", "detailed_modelling_completed", "materials_completed", "blender_validation_passed", "unity_import_verified", "in_game_visual_review_passed", "production_approved"]
