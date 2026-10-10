# Vegetation

Palms, trees, bushes, grass cards. Catalog category `vegetation`. LOD3 as a billboard/impostor. Foliage uses alpha-clipped cutout (no sorted transparency) for WebGL.

## Conventions (every asset in this folder)
- **Scale**: 1 Unity unit = 1 metre. Model at real-world size; the catalog entry's `heightM/widthM/depthM` is checked against the mesh.
- **Orientation**: +Z is the front/forward of the object, +Y up, +X right. Roads run along +Z in local space.
- **Pivot**: at the centre of the footprint on the ground (y = 0) unless stated; vehicles: centre of the rear axle line on the ground.
- **Naming**: `lower_snake_case` ids (`palm_tree`, `aro_titan_480`); LOD meshes `{id}_lod0 ... {id}_lod3`; materials `M_{id}_{part}`; textures `T_{id}_{BaseColor|Normal|ORM|Emissive}`.
- **Materials**: URP/Lit (metallic-smoothness), one material per surface type, no per-object material copies; shared atlas for small props.
- **Textures**: 2k hero / 1k props / 512 small props, power of two, sRGB only for BaseColor/Emissive; WebGL: DXT1/DXT5 (BC7 only if measured), mobile: ASTC 6x6. Do not claim a size without reading the player build.
- **LOD**: LOD0-LOD3 where the catalog lists them (triangle budgets in `prop_catalog.json`, enforced by tests); last LOD <= 10% of LOD0; LOD transitions set by `LodPolicy`.
- **Collision**: simple primitives only (`none` / `box`, or one convex mesh for vehicles); never the render mesh.
- **Metadata**: every placeable prop has an entry in `Resources/Props/prop_catalog.json` (id, category, real size, LOD triangles, cull distance, collider, status `dev` -> `production`). `status: dev` entries are placeholders and say so.
- **Licences**: source files live in a PRIVATE repository or submodule, never here (the repository is public). Record the licence and receipt id in the catalog entry's `description` field when an asset is bought.
