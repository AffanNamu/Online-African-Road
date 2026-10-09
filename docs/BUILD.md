# Build & CI

## Database tests (runs on every push, no setup)
`CI > Database migrations + rule tests`: Postgres 15 service, `supabase/tests/run.sh` then `mutation.py`.

## Unity build (cloud-only; nothing installed on your PC)
Status: **NOT YET BUILT.** The pipeline exists but has never produced an artifact.
Blocker: a Unity license must be provided once (GameCI cannot build without one):
1. Actions > "Unity license activation" > Run workflow; download the `.alf` artifact.
2. Upload it at https://license.unity3d.com/manual (Unity Personal), download the `.ulf`.
3. Repo Settings > Secrets: `UNITY_LICENSE` (full contents of the `.ulf` file); Variables: `UNITY_CI_ENABLED=true`. (Email/password are not used.)
4. Push: CI runs EditMode tests, then `ARO.Editor.BuildScript.BuildWebGL`, then uploads `webgl-build`.
The project is **not build-ready** until that job is green. Before the first build, `Assets/_Project/Scenes/Bootstrap.unity`
must exist: `BuildScript` runs `SceneBuilder.Build()` first (generates scene, materials, URP asset), so no manual editor step is needed.

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
  Fix options (need the repo owner): (a) regenerate: run the "Unity license activation" workflow, upload the `.alf` at
  license.unity3d.com/manual (Unity Personal), paste the **entire** `.ulf` as `UNITY_LICENSE`; (b) use a Unity Plus/Pro serial
  (`UNITY_SERIAL` + email/password); (c) a cloud build service such as Unity Build Automation. If manual activation is no longer
  offered for your account type, (b) or (c) is required - I could not verify which applies.
- Verified so far in CI: SQL suite + mutation run, .NET unit tests, full C# compile of the Unity project.
