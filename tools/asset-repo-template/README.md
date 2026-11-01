# ARO-ASSETS (private)

Production assets for **African Roads Online**: Blender sources, FBX/GLB exports, PBR textures, approved references, manifests, version records and previews.
The game code, asset ids, catalogue entries, route data and the Blender toolchain live in the public repository `AffanNamu/Online-African-Road`. **This repository holds the binaries. It is private and stays private.**

## Rules
1. **Nothing here is published by default.** No asset, reference or preview goes into a public repository, a public release, a public build artifact or social media without the owner's written decision for that file.
2. **References are restricted until their register entry allows more.** A supplied screenshot is *not* stored here at all unless the register (`references/REFERENCE_REGISTER.md`, mirrored from the game repository's `docs/kano/`) says so, and never under `unity/`. Files in `references/restricted/` are never copied by CI, never exported, never embedded in a manifest.
3. **Versions are append-only.** `unity/Models/<id>/vNNN/` is never rewritten once approved; a correction is a new `vNNN`. Approval is recorded in the manifest by a person.
4. **Git LFS from the first commit** for every binary extension in `.gitattributes`. `tools/validate_repo.py` fails if a binary is not covered.
5. **Every asset has a manifest** (`schemas/asset_manifest.schema.json`): its files with SHA-256, the Blender source it came from, the script and toolchain version, who and what made it, its licence evidence, the references it was read from (register ids), its measured budgets and its approval. **Every asset id has a row in `LICENSES.md`.**
6. **Material naming:** shared surfaces are `M_<surface>`; textures `T_<id>_<BaseColor|Normal|ORM|Emissive>`; assets `lower_snake_case` (Kano assets are `kano_*`).

## Layout
```
blender/projects/<kind>/<id>/vNNN/<id>.blend + project.json (+ approval.json)   editable sources
blender/validation/<id>/vNNN.json                                                measured validation reports
references/REFERENCE_REGISTER.md   references/restricted/                         provenance register; restricted files
unity/                                                                           EXACTLY what Unity needs; copied as one folder to Assets/ThirdParty/ARO/
  Models/<id>/vNNN/<id>.fbx|.glb + <id>.manifest.json
  Textures/<id>/T_<id>_*.png      Materials/M_<surface>.mat      Resources/Props/<id>.prefab
previews/<id>/vNNN/*.png          review renders
manifests/                        cross-asset records (release sets)
tools/validate_repo.py            the checks above; run by .github/workflows/validate.yml
```

## How the game uses it
Files appear under `Assets/ThirdParty/ARO/` at development or CI time (that folder is git-ignored in the game repository and its safety gate fails if anything under it is ever tracked). In CI, a step checks this repository out with a **read-only token** when the game repository's `ASSETS_REPO` variable is set, and skips it otherwise (forks and pull requests build with generated placeholders). The token and the variable are set by the owner; never commit them, never paste them in chat.
**Open decision (owner):** the game's browser-smoke jobs download the build as an artifact, and artifacts of a public repository are readable by anyone signed in to GitHub. Until the owner chooses (a) fold the browser test into the build job or (b) upload the build encrypted and decrypt downstream, **no build containing assets from this repository may be uploaded as an artifact.**
