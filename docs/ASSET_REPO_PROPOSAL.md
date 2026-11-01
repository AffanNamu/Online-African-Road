# Private asset repository: proposed structure and integration (PROPOSAL, nothing created)

Decision taken by the owner (2026-11-01): production Blender projects, FBX/GLB exports and textures go in a **private companion repository**; this repository keeps game code and integration only. The structure and method below are for the owner to approve **before anything is created**. Until then no repository exists, no secret is set, and nothing binary is committed anywhere.

## 1. Proposed repository
`AffanNamu/ARO-ASSETS` (private). Suggested; the name is the owner's to change.

```
ARO-ASSETS/
  README.md                  what is here, how to fetch it, the rules below
  LICENSES.md                one row per asset: id, author (human and tool), source references and their rights, licence, date, approved-by
  blender/                   the workspace of tools/blender, as the MCP server writes it
    projects/<kind>/<id>/vNNN/<id>.blend, project.json, approval.json
    references/<id>/...      the reference pack (only images the owner has the rights to use)
    materials/  textures/    sources of baked textures (T_<id>_BaseColor.png ...)
    validation/<id>/vNNN.json
  unity/                     EXACTLY what Unity needs; copied as one folder into Assets/ThirdParty/ARO/
    Models/<id>/vNNN/<id>.fbx (+ .glb, + <id>.manifest.json)
    Textures/<id>/T_<id>_*.png
    Resources/Props/<id>.prefab      production prefabs (LODGroup, BoxCollider, materials)
    Materials/M_<surface>.mat        the shared kit
  tools/                     nothing: the toolchain stays in the game repository (tools/blender)
```
* **Git LFS** for `*.blend *.fbx *.glb *.png *.exr *.psd`. GitHub's free LFS allowance is small (storage and bandwidth per month, and CI fetches count against it); a handful of low-poly assets with 1k textures fits, a whole neighbourhood of 2k textures may need a paid data pack. The alternative, plain git, works for small files and bloats history permanently. **Recommendation: LFS from the first commit**, and re-check the quota after the first section is in.
* Versioned asset folders (`vNNN`) are append-only; approved versions are never rewritten (the toolchain already refuses it). Corrections are a new version.
* The catalogue entries (`prop_catalog.json`: id, size, triangle counts, collider) and the placement rules (route spec) stay **in the game repository**: they are text, they describe the asset without containing it, and they must change in the same pull request as the code that uses them. A fork without the private repository then still builds: a production id with no prefab falls back to the generated placeholder (verified in `DevPropGeometry.Build`: unknown ids become a box of the catalogue's size).

## 2. How the two repositories meet
The seam already exists and is tested: **files appear under `Assets/ThirdParty/` at development or CI time**; that folder is in `.gitignore`, the repository safety gate fails if anything under it is ever tracked, and Unity treats it like any other folder. `tests/BlenderAssetImportTests.AProductionPrefabKeptUnderThePrivateAssetPathIsFoundByTheGamesOwnLookup` proves the key step: a production prefab saved under `Assets/ThirdParty/.../Resources/Props/{id}.prefab` is found by `PropLibrary.ProductionPrefab`, the call `ChunkStreamer.StageProps` makes. (See `docs/BLENDER_PIPELINE.md` section 8 for the measured result.)

* **In CI** (a step in the Unity, smoke and lighting jobs), skipped when the variable is unset so forks and pull requests without secrets still build with placeholders:
  ```yaml
  - uses: actions/checkout@v4
    if: ${{ vars.ASSETS_REPO != '' }}
    with: { repository: "${{ vars.ASSETS_REPO }}", token: "${{ secrets.ASSETS_READ_TOKEN }}", path: _assets, lfs: true }
  - run: mkdir -p Assets/ThirdParty && cp -r _assets/unity Assets/ThirdParty/ARO
    if: ${{ vars.ASSETS_REPO != '' }}
  ```
  `ASSETS_READ_TOKEN`: a fine-grained personal access token with **read-only contents access to that one repository** (or a read-only deploy key), stored as a GitHub Actions secret. Never in either repository, never in chat.
* **For development**: clone `ARO-ASSETS` anywhere and copy or symlink its `unity/` folder to `Assets/ThirdParty/ARO` (git-ignored).
* **Who writes to it**: the Blender toolchain's workspace is the working copy; an approved version is committed to `ARO-ASSETS` by a person or by the agent once the owner has attached that repository to the agent's session with push access (a deliberate step the owner takes at the time; the agent's access today covers only this repository).
* **Per asset, the sequence**: author in Blender (cloud) -> validate and export (toolchain) -> owner reviews renders -> owner approves (`approve` command) -> assets committed to `ARO-ASSETS` -> a pull request here adds the catalogue entry and the placement rule -> CI fetches the assets, imports them in Unity, runs the import tests and the build -> owner reviews in the build.

## 3. What this does and does not protect
* It keeps **source files** (`.blend`, high-resolution textures, references) out of the public repository. It does **not** hide the shipped game: the WebGL build contains the models and textures players see, so a determined player can extract them. If the assets are licensed from third parties this matters for their terms; for assets the owner owns it does not.
* The existing boundary in `ci.yml` stays: when `ASSETS_REPO` is set, **nothing built from private assets is uploaded as a build artifact** (artifacts of a public repository are readable by anyone signed in to GitHub). The consequence, already documented in `docs/ASSET_PIPELINE_PLAN.md` section 12, is that the browser smoke and lighting jobs, which download the build artifact, cannot run on a build with private assets unless they run in the same job as the build or the artifact is encrypted. **This must be solved before the first real asset enters CI**; the options are (a) fold the smoke test into the build job (simplest, longer job), (b) upload the build encrypted with a key from a secret and decrypt it downstream. Recommendation: (b), tested on the calibration fixture first.

## 4. What I need from you to proceed
1. Approve the name and structure above (or change them).
2. You create the empty private repository (or tell me to ask for the right to create it), enable LFS, and add the read-only token as the `ASSETS_READ_TOKEN` secret and the repository name as the `ASSETS_REPO` variable. I will not set secrets or variables.
3. Decide (a) or (b) for the artifact boundary.
4. Confirm in writing who owns the assets authored with the Blender toolchain (`LICENSES.md` records it per asset), because AI-assisted authorship is the owner's to claim and to stand behind.
