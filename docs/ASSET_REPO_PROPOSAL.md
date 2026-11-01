# Private production asset repository: structure and integration workflow

**Status (2026-11-01): APPROVED by the owner; the repository does not exist yet because this session cannot create one.** The GitHub integration answered the create request with `403 Resource not accessible by integration`, so the owner (or an account with that permission) creates the empty private repository; everything else is prepared and tested in `tools/asset-repo-template/` (text files only) and is one push away. No secret or variable is set by me, ever.

Owner decision, 2026-11-01: a private companion repository holds Blender source projects, FBX and GLB production exports, PBR textures, approved asset references, asset manifests, version records and production previews. **This (public) repository stays focused on game integration: asset ids, catalogue entries, route data, configuration, the Blender toolchain and code.** Nothing is published publicly by default.

## 1. What to create (owner steps)
1. On GitHub create **`AffanNamu/ARO-ASSETS`**, **Private**, empty (no README, licence or gitignore). Git LFS is available on every repository; the template declares which files it tracks.
2. Push the template: from a clone of this repository, `cp -r tools/asset-repo-template/. ../ARO-ASSETS/ && cd ../ARO-ASSETS && git lfs install && git add -A && git commit -m "Initial structure" && git push`. (Or attach the new repository to a Claude session with push access and ask me to do it; the template has no binary and no secret.)
3. Optional, when the game's CI should fetch it: add a **read-only fine-grained token** for that one repository as the Actions secret `ASSETS_READ_TOKEN` and the repository name as the variable `ASSETS_REPO` in the game repository. Both are the owner's to set; never paste a token in chat.

## 2. Structure (as implemented in `tools/asset-repo-template/`)
```
README.md   LICENSES.md   .gitattributes   .gitignore
blender/projects/<kind>/<id>/vNNN/<id>.blend + project.json (+ approval.json)     editable sources, written by the toolchain's save_project
blender/validation/<id>/vNNN.json                                                  measured validation reports
references/README.md  REFERENCE_REGISTER.md  restricted/                           provenance register (metadata); restricted files never exported
unity/                                                                            EXACTLY what Unity needs; copied as one folder to Assets/ThirdParty/ARO/
  Models/<id>/vNNN/<id>.fbx|.glb + <id>.manifest.json
  Textures/<id>/T_<id>_<BaseColor|Normal|ORM|Emissive>.png
  Materials/M_<surface>.mat      Resources/Props/<id>.prefab
previews/<id>/vNNN/*.png         review renders
manifests/                       cross-asset release records
schemas/asset_manifest.schema.json   tools/validate_repo.py   tests/   .github/workflows/validate.yml
```
**Rules enforced by `tools/validate_repo.py` (and its 9 tests, run against deliberately broken repositories):** only the documented top-level layout; every binary extension is Git-LFS-tracked; every asset version has a manifest with the required keys whose files exist with the recorded SHA-256 and size; versions are contiguous and append-only in naming; every asset id has a row in `LICENSES.md`; no `.blend`, no reference and nothing from `references/restricted` under `unity/`; every reference id in a manifest exists in the register.

**Manifest fields:** id, version, kind; files with SHA-256 and bytes; the Blender source, the script and the toolchain version it came from; who and what made it; licence evidence; the reference-register ids it was read from; measured LOD triangles (and the catalogue entry); approval by a person.

**Naming:** shared surfaces `M_<surface>` (owner-approved), textures `T_<id>_<map>`, ids `lower_snake_case`, all Kano ids `kano_*`.

## 3. References are handled separately (and conservatively)
The five supplied screenshots are **ChatGPT-provided visual research references**, classified in `docs/kano/REFERENCE_REGISTER.md`: original source unknown, location not established, rights unknown. They are **not stored in this or the private repository**; the register records a SHA-256 so each can be identified. A reference image enters `references/restricted/` only if its register entry allows it and the owner agrees, is never exported, never a texture and never part of a build. Generated concept images are labelled "CONCEPT: not a photograph of an actual Kano location".

## 4. How the two repositories meet (integration workflow)
The seam already exists and is tested: **files appear under `Assets/ThirdParty/` at development or CI time**; that folder is git-ignored here, the safety gate fails if anything under it is ever tracked, and `BlenderAssetImportTests.AProductionPrefabKeptUnderThePrivateAssetPathIsFoundByTheGamesOwnLookup` proves the game's own `PropLibrary.ProductionPrefab` finds a prefab kept there.
* **Per asset:** author in Blender (cloud, MCP toolchain) -> `validate_asset` -> `save_project` (a new `vNNN`) -> `export_asset` (refuses to overwrite or to export a failing asset) -> owner reviews renders -> owner approves -> **committed to `ARO-ASSETS`** (by a person, or by me once the owner attaches that repository with push access) -> a pull request **here** adds the catalogue entry and the placement rule (text only) -> CI fetches the assets, runs the Unity import tests and the build -> owner reviews in the build.
* **In CI** (skipped when `ASSETS_REPO` is unset, so forks and pull requests still build with generated placeholders; a production id with no prefab falls back to the generated box):
  ```yaml
  - uses: actions/checkout@v4
    if: ${{ vars.ASSETS_REPO != '' }}
    with: { repository: "${{ vars.ASSETS_REPO }}", token: "${{ secrets.ASSETS_READ_TOKEN }}", path: _assets, lfs: true }
  - run: mkdir -p Assets/ThirdParty && cp -r _assets/unity Assets/ThirdParty/ARO
    if: ${{ vars.ASSETS_REPO != '' }}
  ```
* **Development:** clone `ARO-ASSETS` anywhere and copy or symlink its `unity/` folder to `Assets/ThirdParty/ARO`.

## 5. What this does and does not protect
It keeps **source files, high-resolution textures and references** out of the public repository. It does **not** hide the shipped game: the WebGL build contains the models and textures players see. The existing boundary in `ci.yml` stays: when `ASSETS_REPO` is set, **nothing built from private assets is uploaded as a build artifact** (artifacts of a public repository are readable by anyone signed in to GitHub).

## 6. Still open (the owner has not decided these; nothing is pushed or merged until they are)
1. **Artifact boundary for the browser tests:** (a) fold the browser smoke test into the build job (simplest, longer job) or (b) upload the build encrypted with a key from a secret and decrypt downstream. Recommendation (b), proven first on the calibration fixture. It must be solved before the first real asset enters CI.
2. **Git LFS quota.** GitHub's free LFS allowance is small and CI fetches count against it; a handful of low-poly assets with 1k textures fits, a neighbourhood of 2k textures may need a paid data pack. Re-check after the first section is in.
3. **Who owns assets authored with the toolchain**, confirmed in writing and recorded per asset in `LICENSES.md` (AI-assisted authorship is the owner's to claim and stand behind).
4. **Token and variable** (`ASSETS_READ_TOKEN`, `ASSETS_REPO`): the owner sets them when ready.
