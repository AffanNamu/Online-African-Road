# Git LFS network requirements for the private asset repository (checked 2026-11-03)

**Why this document exists.** The Blender sources and exports of the residential kit (v001, v002, v003) and of the environmental kit (E1 to E3) must live in the private repository `AffanNamu/ARO-CONTENT` as Git LFS objects, never as ordinary blobs (owner requirement). The cloud environment this work runs in cannot complete an LFS push today, because of its network policy. This is the exact list of what is needed. Nothing here is a workaround: **no host was bypassed, no alternative transfer agent or tunnel was used, no LFS setting was changed to skip a step, and no binary was pushed as a plain blob.**

## What is blocked, measured today
| Step of `git lfs push` / `git lfs pull` | Host | Result from this environment | Needed |
|---|---|---|---|
| Batch API (authentication, upload and download URLs) | `github.com` (`/AffanNamu/aro-content.git/info/lfs`) | **works** (200) | already allowed |
| Upload of each object (PUT) | `github-cloud.s3.amazonaws.com` | **works** (200 for every object of the first attempt; about 50 MB) | already allowed |
| **Verify callback** (POST, after each upload, registers the object) | **`lfs.github.com`** | **refused: `CONNECT tunnel failed, response 403`** at the environment's egress proxy | **must be allowed** |
| Object download (GET) for a fresh clone and `git lfs pull` | normally `github-cloud.githubusercontent.com` | **refused: HTTP 403** on the host (`media.githubusercontent.com` answers 302 and is reachable) | **expected to be needed for the verification; confirmed only when a real download is tried after `lfs.github.com` is open** |

The second row is why nothing is lost: the objects (133 unique ones from the first attempt, about 50 MB) sit uploaded and unregistered in GitHub's storage; once `lfs.github.com` is reachable the verify call registers them and the push completes.

## What the owner does
In the cloud environment's settings: the environment menu in the session's title bar, **Edit**, **Network access**, then add under **Allowed domains** (with the *package managers* box left ticked):
1. `lfs.github.com` (required for any push)
2. `github-cloud.githubusercontent.com` (expected to be required to download the objects again, i.e. for the fresh-clone verification)
Documentation: https://code.claude.com/docs/en/cloud-environments#network-access. If a broader access level is chosen instead, the same two hosts are what the work needs.

## What happens once it is allowed (in this order, all in the private repository's local branches that already exist)
Branches waiting, all containing LFS **pointers** only (the binaries are in the local LFS store):
| Branch | Content |
|---|---|
| `assets/kano-residential-v2` | residential kit v001 and v002 (137 pointers) |
| `assets/kano-residential-v3` | the same plus v003 (211 pointers; created 2026-11-03; the v001 and v002 package files are byte-for-byte untouched in it) |
| `assets/environment-kit-v1` | environmental kit E1 to E3, with the Unity import proof (186 pointers) |
`assets/kano-residential-v001` (superseded plain blobs, from before the LFS rule) is **kept** until its replacement is pushed AND independently verified, and is deleted only on the owner's say-so; `backup/kano-slice-2026-11-01` and `backup/environment-kit-2026-11-02` (text) stay.
1. `git push -u origin assets/kano-residential-v2 assets/kano-residential-v3 assets/environment-kit-v1`
2. Verify every object through a **fresh clone**, one branch at a time:
   `python3 tools/verify_asset_clone.py --repo https://github.com/AffanNamu/ARO-CONTENT.git --branch assets/kano-residential-v3 --freeze docs/kano/KANO_RESIDENTIAL_v002.freeze.json docs/kano/KANO_RESIDENTIAL_v003.freeze.json`
   (and the same for the others with their freeze manifests). It clones into an empty directory without downloading objects, lists the pointers, runs `git lfs pull`, then checks that **every** object is present, has the pointer's size and the pointer's SHA-256, and that every texture, GLB and FBX named in the frozen manifests of this repository exists in the clone with the same hash (the `.blend` files are compared by pointer only: Blender does not write them deterministically). Exit code 0 only if everything passes.
3. Only then are the branches reported as preserved. A failed object is reported with its path, never ignored.

## What does not change
* Nothing is committed to the public game repository except text: code, tests, frozen hash manifests and documents.
* The frozen v002 manifests (`docs/kano/KANO_RESIDENTIAL_v002.*`) and the environmental freeze manifests are never edited; a correction is a new version.
* Until the objects are registered, what is preserved is: the code that regenerates them (the residential v002 outputs were rebuilt from the code and compared: every texture and GLB byte-identical to the frozen manifest), the frozen hashes, and text backups in the private repository.
* Text-only snapshots of the registry state are already in the private repository: `backup/environment-kit-2026-11-02` and `backup/kano-residential-v3-2026-11-03` (pushed; no binaries).
