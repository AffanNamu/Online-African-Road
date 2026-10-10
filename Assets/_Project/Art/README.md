# Art asset architecture

Everything the player sees or hears has a slot here, with one set of conventions (see each folder). Systems consume assets by **id** through
catalogs and `Resources/` conventions, so a production asset replaces a development placeholder without touching gameplay or generator code.

| Folder | Slot convention | Placeholder today |
|---|---|---|
| Vehicles | `Resources/Vehicles/{definition_id}.prefab` (VehicleRig) | primitive truck/bus built by `TruckFactory` |
| Roads | materials in `WorldMaterials`, meshes by profile | generated road mesh, procedural textures |
| Buildings, Vegetation, Props, Signs, Lights | `Resources/Props/{id}.prefab` + `prop_catalog.json` | `DevPropGeometry` (generated, flagged `dev`) |
| Environment, Materials, Textures | assets here | procedural textures, procedural sky |
| Audio | clips here | single placeholder engine loop |

Third-party and paid assets are never committed (public repository): see `docs/ASSET_PIPELINE.md`.
