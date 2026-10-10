# Build & CI

## Database tests (runs on every push, no setup)
`CI > Database migrations + rule tests`: Postgres 15 service, `supabase/tests/run.sh` then `mutation.py`.

## Unity build (cloud-only; nothing installed on your PC)
Status: **NOT YET BUILT.** The pipeline exists but has never produced an artifact.
Blocker: a Unity license must be provided once (GameCI cannot build without one). The old "request an .alf, upload it at
license.unity3d.com/manual" route is dead: `game-ci/unity-request-activation-file` is discontinued and Unity no longer offers manual
activation for Personal accounts. Current GameCI instructions for Personal licenses (game-ci/documentation, `activation`):
1. Install Unity Hub on any computer, sign in with the Unity ID that CI will use, then Preferences > Licenses > Add > free personal license.
   (GameCI notes that a license can show in Hub without a `.ulf` file having been written.)
2. Find `Unity_lic.ulf` on that computer (normally `C:\ProgramData\Unity\Unity_lic.ulf`, macOS `/Library/Application Support/Unity/Unity_lic.ulf`,
   Linux `~/.local/share/unity3d/Unity/Unity_lic.ulf`).
3. Repo Settings > Secrets: `UNITY_LICENSE` = full contents of that file, `UNITY_EMAIL`, `UNITY_PASSWORD` (a Unity ID that has a password
   and no two-factor sign-in; a "Continue with Google" account has no password). Variables: `UNITY_CI_ENABLED=true`.
   Run Actions > "Unity license check" first: it prints the shape of each secret (never the values) and says what is wrong.
   Professional/Plus: `UNITY_SERIAL` + email + password instead of the `.ulf`.
4. Push: CI runs EditMode tests, then `ARO.Editor.BuildScript.BuildWebGL`, then uploads `webgl-build`.
The project is **not build-ready** until that job is green. Before the first build, `Assets/_Project/Scenes/Bootstrap.unity`
must exist: `BuildScript` runs `SceneBuilder.Build()` first (generates scene, materials, URP asset), so no manual editor step is needed.

### How the license actually works in CI (verified by the "Unity license debug" workflow, run 1)
In the CI editor image (6000.0.58f2): the Hub `.ulf` alone is NOT accepted (`com.unity.editor.headless was not found`, 0 entitlements),
`-manualLicenseFile` is not accepted either; **activating with the Personal serial + the Unity account email/password succeeds**
(`Successfully activated the entitlement license`). The `.ulf` contains that serial in its `DeveloperData` field, so `ci.yml` recovers it
(masked) and passes `UNITY_SERIAL` + `UNITY_EMAIL` + `UNITY_PASSWORD` to GameCI. Secrets still required: `UNITY_LICENSE`, `UNITY_EMAIL`, `UNITY_PASSWORD`.

### Playing / inspecting the WebGL build
Download the `webgl-build` artifact from the CI run page, unzip it, and serve the folder over HTTP (browsers will not run it from `file://`):
`python3 -m http.server 8000` in the unzipped folder, then open http://localhost:8000. The `webgl-smoke` CI job does exactly this in headless
Chromium on every build and fails if the build does not load, throws, never reaches `GameBootstrap`, or renders a blank canvas.

## Unity Cloud project link
Unity project name: **African Online Roads**, Cloud project ID `1844ea76-e24a-45de-bcb2-2ec2426f8ff6` (stored in
`ProjectSettings/ARO_UnityProject.json`; identifiers, not secrets). `ARO.Editor.ProjectLinker` writes the ID into PlayerSettings
on editor load and before every scripted build, so CI builds are linked. **Unverified** until the first Unity run.
In the Unity Dashboard (cloud.unity.com) for that project you must still: enable **Authentication (anonymous)**, **Relay** and
**Multiplayer Sessions/Lobby**. I cannot do that for you.

### Open risk: WebGL + Relay
Browsers cannot use UDP. Relay on WebGL needs WebSockets (WSS) transport; the Sessions API option for that must be confirmed
against the real SDK at first compile. Until then multiplayer is expected to work on desktop/Android/iOS builds first.

## CI status log (2026-10-09)
- Unity 6000.0.58f2 boots in CI; all packages resolve; **the whole project compiles** (game, editor, test and networking assemblies: `Tundra build success`).
- Docker Hub throttling of the `unityci/editor` image is mitigated by a pre-pull step with retries (`ci.yml`).
- **Remaining blocker (runs 20 and 21, identical):** Unity aborts after the project loads with `No valid Unity Editor license found`
  (exit code 198), so no EditMode test has executed yet and the WebGL build has not been attempted. Run 21 passed only the `.ulf`
  (`UNITY_LICENSE`), no email/password, and failed the same way - so the `.ulf` in the secret is not accepted by the 6000.0.58f2
  editor in CI. This is an account/licensing problem, not a code problem.
  Fix options (need the repo owner): see the numbered steps above (Hub-activated `.ulf` + a Unity ID with a password), or a Unity
  Plus/Pro serial, or a cloud build service such as Unity Build Automation.
- Verified so far in CI: SQL suite + mutation run, .NET unit tests, full C# compile of the Unity project.
