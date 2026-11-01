# Reviewed script: the ORIENTATION AND SCALE CALIBRATION object. Deliberately asymmetric, with every part at a known position and named for the direction it marks, so that an importer
# (Unity, in tests/BlenderAssetImportTests.cs) can prove which way front, right and up came out, whether the model is mirrored, and that 1 unit is 1 metre. It is a test fixture, not a game asset.
# Blender: front -Y (the asset faces the front-view camera), so its right is -X; up +Z. Body 2.0 (X) x 6.0 (Y) x 1.5 (Z) m on the ground. Markers: front cone (red) y=-3.5, rear cone (white) y=+3.5,
# right cylinder (green) x=-1.4, left cylinder (yellow) x=+1.4, top cylinder (blue) z=+1.9. One collision box. LOD1 is a single box. Expected in Unity: front +Z, right +X, up +Y.
S = aro_scene; ID = "aro_calibration"
S.create_primitive(bpy, "empty", ID, (0, 0, 0))
S.create_primitive(bpy, "empty", ID + "_lod0", (0, 0, 0), parent=ID)
S.create_primitive(bpy, "empty", ID + "_lod1", (0, 0, 0), parent=ID)
S.create_primitive(bpy, "cube", ID + "_body", (0, 0, 0.75), (2.0, 6.0, 1.5), parent=ID + "_lod0")
S.create_primitive(bpy, "cone", "marker_front", (0, -3.5, 0.75), (0.5, 0.5, 0.5), rotation_deg=(90, 0, 0), parent=ID + "_lod0", vertices=16)
S.create_primitive(bpy, "cone", "marker_rear", (0, 3.5, 0.75), (0.5, 0.5, 0.5), rotation_deg=(-90, 0, 0), parent=ID + "_lod0", vertices=16)
S.create_primitive(bpy, "cylinder", "marker_right", (-1.4, 0, 0.75), (0.5, 0.5, 0.5), parent=ID + "_lod0", vertices=16)
S.create_primitive(bpy, "cylinder", "marker_left", (1.4, 0, 0.75), (0.5, 0.5, 0.5), parent=ID + "_lod0", vertices=16)
S.create_primitive(bpy, "cylinder", "marker_top", (0, 0, 1.9), (0.5, 0.5, 0.5), parent=ID + "_lod0", vertices=16)
S.create_primitive(bpy, "cube", ID + "_low", (0, 0, 0.75), (2.0, 6.0, 1.5), parent=ID + "_lod1")
S.create_primitive(bpy, "cube", "col_" + ID, (0, 0, 0.75), (2.0, 6.0, 1.5), parent=ID)
for name in ("marker_front", "marker_rear"): S.transform(bpy, name, apply=True)          # bake the cone rotations into the meshes: no rotation left on the objects
for name, mat in ((ID + "_body", "concrete"), ("marker_front", "paint_red"), ("marker_rear", "paint_white"), ("marker_right", "leaf_green"), ("marker_left", "road_yellow"),
                  ("marker_top", "metal_gate_blue"), (ID + "_low", "concrete")): S.assign_material(bpy, name, mat)
result = {"id": ID}
