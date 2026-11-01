# Reviewed script: a road bollard (concrete post with a painted cap band), the smallest COMPLETE asset: root empty named after the id, a LOD group per level, game naming, library materials.
# LOD0 is the post with its band (two 16-sided cylinders); LOD1 is a single box (12 triangles, under 10% of LOD0), as the Art README's LOD rules require.
# args: height (m, default 0.9), diameter (m, default 0.22)
h = float(args.get("height", 0.9)); d = float(args.get("diameter", 0.22))
S = aro_scene
S.create_primitive(bpy, "empty", "bollard", (0, 0, 0))
S.create_primitive(bpy, "empty", "bollard_lod0", (0, 0, 0), parent="bollard")
S.create_primitive(bpy, "empty", "bollard_lod1", (0, 0, 0), parent="bollard")
body = S.create_primitive(bpy, "cylinder", "bollard_body", (0, 0, h * 0.4), (d, d, h * 0.8), parent="bollard_lod0", vertices=16)
band = S.create_primitive(bpy, "cylinder", "bollard_band", (0, 0, h * 0.8 + h * 0.1), (d * 1.04, d * 1.04, h * 0.2), parent="bollard_lod0", vertices=16)
low = S.create_primitive(bpy, "cube", "bollard_low", (0, 0, h / 2), (d, d, h), parent="bollard_lod1")
S.assign_material(bpy, "bollard_body", "concrete"); S.assign_material(bpy, "bollard_band", "road_white"); S.assign_material(bpy, "bollard_low", "concrete")
result = {"parts": [body.name, band.name, low.name], "height_m": h}
