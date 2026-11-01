# Reviewed script: NEUTRAL LAYOUT MASSING for the first environment section (a residential roadside, one road section with plots on both sides).
# This is NOT architecture. Every dimension below is a PLACEHOLDER chosen only so the composition can be read (road corridor, setbacks, plot rhythm, pole spacing). Houses, walls, gates, shops, poles and
# trees are plain grey boxes and cylinders. The real proportions come from the reference pack, which the owner and I are still making; until then nothing here may be read as a Northern Nigerian
# building, and nothing here is modelled in detail. args: any key of P below overrides it (metres).
P = {"road_len": 100.0, "carriageway": 7.3, "shoulder": 2.0, "drain": 1.0, "verge": 2.5,
     "plot_front": 20.0, "plot_depth": 30.0, "wall_h": 2.2, "wall_t": 0.25, "gate": 4.0,
     "house_w": 10.0, "house_d": 12.0, "house_h": 3.2, "setback": 6.0,
     "pole_h": 9.0, "pole_spacing": 40.0, "light_h": 8.0, "light_spacing": 30.0, "plots_per_side": 5}
P.update({k: float(v) for k, v in args.items() if k in P})
S = aro_scene; ROOT = "section_massing"
S.create_primitive(bpy, "empty", ROOT, (0, 0, 0))
n = [0]


def box(name, x, y, z0, w, d, h, mat):
    """a box w (X) by d (Y) by h (Z), its bottom at z0, centred on x, y"""
    n[0] += 1
    S.create_primitive(bpy, "cube", f"massing_{name}_{n[0]:02d}", (x, y, z0 + h / 2), (w, d, h), parent=ROOT)
    S.assign_material(bpy, f"massing_{name}_{n[0]:02d}", mat)


def cyl(name, x, y, z0, dia, h, mat, v=10):
    n[0] += 1
    S.create_primitive(bpy, "cylinder", f"massing_{name}_{n[0]:02d}", (x, y, z0 + h / 2), (dia, dia, h), parent=ROOT, vertices=v)
    S.assign_material(bpy, f"massing_{name}_{n[0]:02d}", mat)


L = P["road_len"]; yc = L / 2
half = P["carriageway"] / 2
edge_shoulder = half + P["shoulder"]; edge_drain = edge_shoulder + P["drain"]; edge_verge = edge_drain + P["verge"]
box("ground", 0, yc, -0.3, 2 * (edge_verge + P["plot_depth"] + 6), L + 10, 0.3, "dry_earth")
box("carriageway", 0, yc, 0, P["carriageway"], L, 0.05, "asphalt")
for s in (-1, 1):
    box("shoulder", s * (half + P["shoulder"] / 2), yc, 0, P["shoulder"], L, 0.04, "laterite")
    box("drain", s * (edge_shoulder + P["drain"] / 2), yc, -0.5, P["drain"], L, 0.5, "concrete_dark")
# plots: frontage along the road (Y), depth away from it (X)
for s in (-1, 1):
    for i in range(int(P["plots_per_side"])):
        y0 = i * P["plot_front"]; yc_p = y0 + P["plot_front"] / 2
        shops = (s == 1 and i == 2)
        xf = s * edge_verge                                   # the plot's front edge, on the road side
        if shops:                                             # a row of four shop volumes straight onto the verge, no compound
            for k in range(4):
                box("shop", xf + s * 3.0, y0 + P["plot_front"] / 8 + k * P["plot_front"] / 4, 0, 6.0, P["plot_front"] / 4 - 0.4, 3.2, "concrete")
            continue
        # compound wall: front with a gate gap, two sides, rear
        gy0 = yc_p - P["gate"] / 2; gy1 = yc_p + P["gate"] / 2
        box("wall_front", xf, (y0 + gy0) / 2, 0, P["wall_t"], gy0 - y0, P["wall_h"], "block_wall")
        box("wall_front", xf, (gy1 + y0 + P["plot_front"]) / 2, 0, P["wall_t"], y0 + P["plot_front"] - gy1, P["wall_h"], "block_wall")
        box("wall_side", xf + s * P["plot_depth"] / 2, y0, 0, P["plot_depth"], P["wall_t"], P["wall_h"], "block_wall")
        box("wall_rear", xf + s * P["plot_depth"], yc_p, 0, P["wall_t"], P["plot_front"], P["wall_h"], "block_wall")
        box("house", xf + s * (P["setback"] + P["house_d"] / 2), yc_p, 0, P["house_d"], P["house_w"], P["house_h"], "plaster_cream")
# furniture on the road side of the verge: poles on the right, streetlights on the left
y = 10.0
while y < L - 5:
    cyl("pole", edge_drain + 0.8, y, 0, 0.3, P["pole_h"], "timber_pole"); box("pole_arm", edge_drain + 0.8, y, P["pole_h"] - 0.6, 0.12, 2.2, 0.12, "timber_pole"); y += P["pole_spacing"]
y = 5.0
while y < L - 5:
    cyl("light", -(edge_drain + 0.8), y, 0, 0.16, P["light_h"], "metal_galvanised"); box("light_arm", -(edge_drain + 0.8) + 0.9, y, P["light_h"] - 0.1, 1.8, 0.2, 0.1, "metal_galvanised"); y += P["light_spacing"]
# trees: a fixed pattern (no randomness: the same layout every run), on the verge between poles and lights and in rear yards, never inside a house footprint
vx = edge_drain + 1.8; yx = edge_verge + P["plot_depth"] - 4.0
for tx, ty in [(-vx, 20.0), (vx, 62.0), (-vx, 77.0), (vx, 18.0), (-yx, 30.0), (-yx, 70.0), (yx, 10.0), (yx, 50.0)]:
    cyl("tree", tx, ty, 0, 0.4, 3.0, "bark", 8); n[0] += 1
    S.create_primitive(bpy, "sphere", f"massing_crown_{n[0]:02d}", (tx, ty, 6.0), (5.5, 5.5, 5.5), parent=ROOT, vertices=10); S.assign_material(bpy, f"massing_crown_{n[0]:02d}", "leaf_green")
result = {"objects": n[0], "road_len_m": L, "plots": int(P["plots_per_side"]) * 2, "assumptions": "ALL dimensions are placeholders pending the reference pack"}
