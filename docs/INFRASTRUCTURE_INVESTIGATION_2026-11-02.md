# Infrastructure investigation (2026-11-02): what already works, what is missing, the exact blockers

**Purpose.** The owner reported that Unity, the game and Supabase are already set up and running, and asked for the existing environment to be investigated before any further owner action is requested. This document records what was found. **Nothing was changed in the live game, in Supabase, in any credential or in any repository setting to produce it.** No secret value was read, printed or retrieved at any point. Every statement below says how it was established.

## 1. Repositories, branches and workflows
| Repository | Visibility | What it holds | Workflows |
|---|---|---|---|
| `AffanNamu/Online-African-Road` (the game) | public | Unity project, C#, SQL, tools, the Blender toolchain | `ci.yml` (database tests, .NET tests, Blender toolchain, Unity tests + WebGL build, WebGL smoke and lighting jobs), `blender-unity.yml` (Blender asset import into Unity), `supabase-apply.yml`, `supabase-jobs.yml`, `supabase-preflight.yml`, `backend-smoke.yml`, `unity-license-check.yml`, `unity-license-debug.yml` |
| `AffanNamu/ARO-CONTENT` (assets) | private | registry, library index, reference register, text backups; the Blender sources and exports as Git LFS | `validate.yml` (always green), `unity-review.yml` (on `review/**` pushes; needs its own Unity secrets) |

Game repository branches of interest: `main`, `work/kano-residential` (pushed, the integration branch), `claude/intelligent-faraday-sykb0x`, `work/blender-mcp`, `work/blender-camera`, `work/admin-*`; `work/kano-slice` is local only by decision. Asset repository branches: `main`, `docs/unity-review-setup`, `docs/environment-library`, `backup/kano-slice-2026-11-01`, `assets/kano-residential-v001` (superseded, plain blobs, kept until its replacement is verified); `assets/kano-residential-v2` exists only locally (137 LFS pointers).

## 2. Unity credentials
| Where | Finding | How established |
|---|---|---|
| **Game repository** | **Available and working.** The Unity jobs activate a licence and run: in CI run 273 (commit `f3cdfd2`) "Unity tests + WebGL build" succeeded in 22 minutes with `Successfully activated the entitlement license`, and the two WebGL jobs that follow it passed. The job is gated by the repository variable `UNITY_CI_ENABLED`, which is therefore set. | Job conclusions and log lines of that run (read-only). |
| **Asset repository** | **Not available.** A probe branch (`review/secrets-probe`, run 41388145847) triggered the existing review workflow, whose first step checks the three secrets by name: it failed with `missing repository secrets: UNITY_LICENSE UNITY_EMAIL UNITY_PASSWORD`. The probe had no `unity/` folder so the job could not reach any Unity step: **no licence activation was used.** GitHub keeps secrets per repository; the owner's secrets exist in the game repository, which is where the project and its CI live. | The step's own result and error line (names only; no value exists in that output). |
| Direct listing of secret names | **Not possible from this environment**: the egress proxy answers `Access to this GitHub Actions path is not permitted through this proxy` for the secrets and variables API of both repositories. It was not circumvented. | The 403 itself. |

## 3. How the game runs locally and how it is built
* **Build:** `ci.yml` job "Unity tests + WebGL build" runs `game-ci/unity-builder@v4` with Unity 6000.0.58f2 and the build method `ARO.Editor.BuildScript.BuildWebGL`, authenticating with the serial recovered (masked) from `UNITY_LICENSE` plus `UNITY_EMAIL` and `UNITY_PASSWORD`. Output of run 273, **measured** from the job log: `WebGL.data.unityweb` 20,105,528 bytes, `WebGL.wasm.unityweb` 15,694,615, `WebGL.framework.js.unityweb` 95,497, `WebGL.loader.js` 47,338: **35.9 MB** in total (the `webgl-build` artifact is 33 MB zipped).
* **Local run (docs/BUILD.md):** download the `webgl-build` artifact, unzip, run `python3 -m http.server 8000` in that folder, open `http://localhost:8000`. **The Python process only serves static files.** The game is a WebGL player running in the browser; the only bridge to the page is `Assets/_Project/Plugins/WebGL/ARO_Web.jslib`. There is no channel between Python and Unity.
* **Deployment:** there is **no deploy workflow** in the repository (nothing publishes to a host); the build is an artifact. Anything beyond that (a hosting target) is outside the repository and was not inspected.
* **Browser tests:** the `webgl-smoke`, lighting and review scripts (`tools/webgl-*.js`) serve the same build on a random localhost port in headless Chromium (software GL), which is how CI proves that the build boots.

## 4. Supabase integration
* The player is **built already pointing at the backend**: `BuildScript.ApplyBackendConfigFromArgs` writes `Assets/_Project/Resources/BackendConfig.asset` from `-supabaseUrl` and `-supabaseAnonKey`, which `ci.yml` supplies from the repository variable `SUPABASE_URL` (or secret) and the secret `SUPABASE_ANON_KEY`; the build **refuses a key that is not a JWT** and only the public anon key is accepted. Nothing is committed.
* Evidence that the CI-built player is configured: the build log says `[ARO] BackendConfig written for ***` (GitHub masks the URL), the smoke job runs with `EXPECT_BACKEND: 1` (the job fails if the key exists but the build is unconfigured), and the game logs `[Boot] African Roads Online starting, backend configured.`
* The backend workflows run: `Supabase job board top-up` succeeded on its last two scheduled runs (73 and 74).
* **Not verifiable from here:** that the owner's own localhost instance talks to the same project. It runs the same CI artifact, so it carries the same baked configuration. **The review described below uses no backend configuration at all** (the review build is made without the Supabase arguments, never touches the live project and is not deployed).

## 5. Why LFS access is blocked (exact)
`git lfs push` of the 133 unique objects of the local branch was traced (hosts and status only; credentials filtered):
| Step | Host | Result |
|---|---|---|
| LFS batch API | `github.com` (`/AffanNamu/aro-content.git/info/lfs`) | **200 OK**: authentication works (the client's own credential is none; the environment's git proxy supplies it), the repository accepts LFS, upload actions are returned |
| Object upload (PUT) | `github-cloud.s3.amazonaws.com` | **200 OK for all 133 objects** (about 50 MB) |
| **Verify callback (POST)** | **`lfs.github.com`** | **403: refused by the environment's egress proxy** at the CONNECT stage (`Post "https://lfs.github.com/…/verify": Forbidden`, 133 times, three attempts each) |
A second push gave the identical result: the server never registers an object whose verify call fails, so it offers it for upload again. **Cause: network policy of this execution environment, for exactly one host, `lfs.github.com`.** It is not an authentication problem, not a repository configuration problem and not a size or quota problem. The uploaded bytes sit unregistered in GitHub's storage and are harmless.

**Recheck on 2026-11-02 (second session day, after the owner asked to keep investigating):** the policy is unchanged. `lfs.github.com` still answers `CONNECT tunnel failed, response 403`. For the verification step the owner asked for (a fresh clone and `git lfs pull` of every object) the download hosts matter too: `github-cloud.githubusercontent.com` also answers 403 today and `media.githubusercontent.com` answers 302 (reachable); GitHub normally serves the objects of a repository on github.com from `github-cloud.githubusercontent.com`, so **the owner will probably need to allow both `lfs.github.com` and `github-cloud.githubusercontent.com`** (the second cannot be confirmed until the first is open and a real download is tried). The environment's own documentation gives the route: the cloud environment menu in the session's title bar, Edit, Network access, then the host under Allowed domains. Nothing was bypassed (no tunnel, no alternative transfer agent, no change to LFS configuration to skip the verify call). What is waiting: the local branches `assets/kano-residential-v2` (137 pointers) and `assets/environment-kit-v1` (186 pointers). Meanwhile a text-only snapshot of the environmental registry is preserved in the private repository (branch `backup/environment-kit-2026-11-02`).

## 6. Can the existing Unity workflow import and build the real assets without new credentials?
* **In the asset repository: no.** Its review workflow needs the three secrets there (absent) and the LFS objects registered (blocked as above). Both are duplications of what already works elsewhere.
* **In the game repository: yes.** `blender-unity.yml` already installs Blender (the `bpy` wheel), runs the toolchain, imports models into Unity 6 and runs EditMode tests, with the credentials that work. It was extended (commit `ab31f49`, opt-in) so that a commit message containing `[kano-review]` (or a manual run with `review` set) also builds the 14 residential assets **from the code** with the toolchain (every one must pass the toolchain validation), stages them under the git-ignored `Assets/ThirdParty/ARO`, builds the game with `BuildKanoReviewWebGL` (no backend baked in), drives it in headless Chromium (`?smoke=kanostreet`) and keeps one screenshot per view as a **one-day artifact**. The assets exist only inside the job; nothing is committed; no picture or build is written to the public log. No new credential, deployment process or repository setting is involved.
* **Exposure, stated plainly:** the artifact of a public repository is readable by signed-in GitHub users for its one day. It contains in-game pictures of geometry and textures that are generated by code that is itself public. It contains no concept image, no credential and no private binary.

## 7. Safest way to preserve the 137 LFS pointers (133 unique objects) in the private repository
1. **The owner adds the single host `lfs.github.com` to this environment's allowed domains** (Network access). The objects are already in storage; one `git push -u origin assets/kano-residential-v2` then registers them and pushes the branch, and a fresh clone with `git lfs pull` verifies them. This is the only route that stores the original bytes in the private repository without ordinary Git blobs.
2. Not recommended: pushing the files as plain blobs (violates the owner's rule and permanently enlarges the history) or rebuilding them later from the pushed code (the code and the frozen hashes are preserved, but byte-identical output is not verified).
3. Until then everything text-based is preserved: code (game repository, pushed), the freeze manifest with 126 file hashes, the registry, the library index, the reference register and the backup patches (`ARO-CONTENT`, pushed, CI green).

## 8. What already works / what is missing
| Works | Missing |
|---|---|
| Unity activation, tests, WebGL build, smoke jobs in the game repository | Unity secrets in the asset repository (not needed for the route used here) |
| Backend baked into the CI build, backend workflows, local serving of the artifact | `lfs.github.com` in the environment's allowed domains (one host) |
| Blender toolchain in CI with Unity import tests | A hosting target for the build (outside this task) |
| Opt-in in-game review of the real kit in the existing workflow (commit `ab31f49`) | Approval of v002 (needs the in-game review and the owner) |
