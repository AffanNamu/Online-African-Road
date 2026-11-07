# Unification of the private asset library (2026-11-08)

**Owner decision (2026-11-07/08): approved.** The residential kit, the environmental kit and the review pictures were brought together into one authoritative library: `main` of the private repository `AffanNamu/ARO-CONTENT` (commit `6db5ba2`). **Nothing was deleted, overwritten or rewritten; nothing is production-approved.** The audit and checks are repeatable with `tools/library_audit.py`, `tools/verify_asset_clone.py` and `tools/release_chain.py`.

## 1. Audit before the merge (read-only)
| Question | Result |
|---|---|
| Sources | `assets/kano-residential-v3` (residential v001 to v003; its history contains `fe60106`, the v001/v002 package), `assets/kano-residential-v2` (the same plus the 14 original v002 export manifests), `assets/environment-kit-v1` (E1 to E3), `reviews/in-game-2026-11-07` (pictures), all grown from `docs/environment-library` (itself on `docs/unity-review-setup` and `main`). `assets/kano-residential-v001` is a separate chain (see 4). |
| Duplicate asset ids | **none**: no registry lists an id twice; **no asset is produced in two places** (14 residential and 26 environmental produced ids are disjoint) |
| Registry conflicts | three-way comparison against the common base (132 entries): the residential branch changed 17 entries and added 2, the environmental branch changed 26 and added 3, **no entry was changed by both**; merged registry **137 entries** |
| Classification | every id prefix agrees with its class, and the region/city fields agree with the class and the id (GLOBAL has none, REGIONAL `rg_<region>_` has its region, CITY_SPECIFIC and LANDMARK their city): **0 problems** in 137 entries: **93 GLOBAL, 28 REGIONAL, 10 CITY_SPECIFIC, 6 LANDMARK** |
| File-path conflicts between the branches | exactly four: `registry/asset_registry.json`, `LICENSES.md`, `docs/LIBRARY.md`, `unity/Materials/aro_materials.json`; every other path is unique to one branch or identical |

## 2. How the four files were reconciled
* **Registry:** entry-level three-way merge (`tools/library_audit.py merge`), classes and statuses untouched; the registry note records the unification.
* **`LICENSES.md`:** both sides only appended rows (no overlap): all 40 rows kept; the version cell of 19 rows, which still said 2 for assets that have v003, now lists the versions that exist.
* **Material kit:** the residential file (81 materials) contains every environmental material (76) with identical definitions; it is the combined kit. The older v002 file (49 materials; `M_mud_brick` and `M_roof_zinc_rust` were redefined later) and the environmental original are kept under `unity/Materials/releases/`.
* **`docs/LIBRARY.md`:** regenerated from the merged registry by the repository's own generator.

## 3. Prototypes are kept apart
The five merged-frontage prototypes (`aro_frontage_*`) are **not registry assets**: they stay under `blender/prototype_frontages/`, are indexed in `registry/prototypes.json` (`prototype_unapproved`, no registry entry, no licence row, no package manifest) and leave that list only with a registry entry, documentation and the owner's approval.

## 4. The superseded v001 branch is a distinct original build, not a duplicate
`assets/kano-residential-v001` (plain blobs from before the LFS rule) holds a **different** build of v001: of its 125 paths 25 are identical blobs, 4 have the same bytes as the LFS object the library holds, **83 differ** (the `.blend`, FBX, GLB, texture and `project.json` bytes) and **13 exist only there** (six preview JPEGs, six texture PNGs, `LFS_PENDING.md`). It cannot be merged without breaking append-only versions at the same paths, so it stays as its own branch. **It must not be deleted** (and the owner's decision is that it is deleted only on explicit authorisation).

## 5. Verification of the result (all from a fresh clone of the unified branch)
* **Preservation proof** (`library_audit.py preserve`): for each of the branches `v2`, `v3`, `environment-kit-v1`, `reviews`, `docs/*`, `main`, every path exists in the library with the same bytes (the same git blob), except the reconciled files; **0 missing**, and the four paths on `main`, the docs branches and the reviews branch that later commits of the library history updated are tooling files (validator, schema, reference register, its test).
* **LFS and frozen hashes** (`verify_asset_clone.py` with all four freeze manifests: residential v002, v003, environmental E1/E2, E3): **514 of 514 LFS objects intact, 426 of 426 frozen files byte for byte** (`.blend` sources included).
* **The private repository's own validator:** passes; **its test suite: 52 of 52** (see 6).
* **Release chain** (`release_chain.py`): **40 produced assets, 240 links: 196 ok, 44 open, 0 broken**. Open: the owner's in-game review of every asset (40), the missing reference of three Hausa assets (gap M-01) and of the concrete pole (an owner request). No asset has a complete chain yet.

## 6. Two findings, fixed in the open
* **A stale test.** `test_the_shipped_registry_claims_no_asset_beyond_what_has_been_measured` capped statuses at Blender validation and said to raise the ceiling in the commit that adds the Unity evidence. The import records had been added on the release branches without raising it, so the test failed on them. The ceiling is now `unity_import_verified` **and every such asset must name an import report that exists and lists it and carry no `approvedBy`**; `in_game_visual_review_passed` and `production_approved` stay above the ceiling until the owner has reviewed the pictures. A stricter test, not a looser one.
* **Snapshot tags cannot be pushed.** Annotated tags `snapshot/<branch>` were created, but the cloud environment's git proxy refuses every tag push (HTTP 403) and no workaround was attempted. The historical branches are untouched on the remote and are the snapshots; their tip commits are recorded in `manifests/library_releases.json`. **Owner action recommended:** protect those branches in the repository's branch settings (no force-push, no deletion). The tags remain local in the working clone only.

## 7. Where things are in the unified library
`README.md` of the private repository documents the layout: `unity/Models/<id>/vNNN/` (every version), `blender/export_manifests/` (original export manifests), `blender/prototype_frontages/` (unapproved), `unity/Materials/` (combined kit and `releases/`), `unity/import_reports/`, `previews/in_game_review/<date>/`, `manifests/library_releases.json` (the release snapshots) and `registry/prototypes.json`. For a new release use the checks of `docs/ASSET_RELEASE_CHAIN.md` and `docs/AFRICAN_ENVIRONMENT_LIBRARY_ARCHITECTURE.md`.
