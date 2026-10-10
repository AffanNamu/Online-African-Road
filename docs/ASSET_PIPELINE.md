# Asset pipeline and repository safety

The repository `AffanNamu/Online-African-Road` is **public**. Paid and commercial assets (Asset Store, TurboSquid, CGTrader, commissioned work) forbid redistribution of the source files, and committing them to a public repository does exactly that.

## Rules
1. Never commit third-party or paid assets here. `Assets/ThirdParty/` and `Assets/AssetStore/` are in `.gitignore`, and CI fails if any file under them (or any licence, key, `.env`, service-role key, database URL with a password, GitHub token, private key, or build output) is tracked: `tools/check_repo_safety.py`, itself tested by `tools/test_repo_safety.py` (13 cases, including "the public anon key is allowed").
2. Before the first paid asset: either make this repository private, or keep third-party assets in a private companion repository (or submodule) that CI fetches with a token stored in GitHub Actions secrets. Compiled game builds are normally permitted; raw asset files are not.
3. Secrets only in GitHub Actions secrets (`UNITY_*`, `SUPABASE_*`, `DOCKERHUB_*`). The Supabase project URL and anon key are public by design; the service-role key and database password are never in the repo or in chat.

## Where assets go
See `Assets/_Project/Art/README.md` and one README per category (Vehicles, Environment, Roads, Buildings, Vegetation, Props, Signs, Lights, Materials, Textures, Audio) for naming, scale (1 unit = 1 m), orientation (+Z forward), pivots, materials, LOD, collision and metadata conventions.

Replacement is by id, with no code changes:
* Props: `Assets/_Project/Resources/Props/{id}.prefab` replaces the generated placeholder for that id (`PropLibrary.ProductionPrefab`); update the catalog entry to `status: production`, `source: Props/{id}`.
* Vehicles: `Assets/_Project/Resources/Vehicles/{vehicle_definition_id}.prefab` with a `VehicleRig` (or `VehicleDefinition.visualPrefab`). `TruckFactory` prefers either over the placeholder. **ARO TITAN 480**: add a `vehicle_definitions` row (new migration, not a change to existing ones) with its id and stats, then drop the prefab in. No gameplay code references a vehicle mesh.
* Textures/materials: `WorldMaterials` builds materials from code today; production PBR sets replace the textures there (or become material assets in `Art/Materials`).
* Prop catalog rules (`PropCatalogValidator`): snake_case ids, categories, real-world size, strictly decreasing LOD triangle counts under a per-category LOD0 budget, dev props must be flagged `dev` with a `dev:` source.

## What is still required from outside (cannot be generated)
Hero trucks and buses (ARO TITAN 480), traffic vehicles, road kit PBR sets, vegetation and building packs with LODs, sign and billboard art, audio. See `docs/VISUAL_DIRECTION.md` for the acquisition plan.
