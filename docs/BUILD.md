# Build & CI

## Database tests (runs on every push, no setup)
`CI > Database migrations + rule tests`: Postgres 15 service, `supabase/tests/run.sh` then `mutation.py`.

## Unity build (cloud-only; nothing installed on your PC)
Status: **NOT YET BUILT.** The pipeline exists but has never produced an artifact.
Blocker: a Unity license must be provided once (GameCI cannot build without one):
1. Actions > "Unity license activation" > Run workflow; download the `.alf` artifact.
2. Upload it at https://license.unity3d.com/manual (Unity Personal), download the `.ulf`.
3. Repo Settings > Secrets: `UNITY_LICENSE` (contents of `.ulf`), `UNITY_EMAIL`, `UNITY_PASSWORD`; Variables: `UNITY_CI_ENABLED=true`.
4. Push: CI runs EditMode tests, then `ARO.Editor.BuildScript.BuildWebGL`, then uploads `webgl-build`.
The project is **not build-ready** until that job is green. Before the first build, `Assets/_Project/Scenes/Bootstrap.unity`
must exist: CI step `BuildScript.PrepareProject` generates it headlessly (SceneBuilder), so no manual editor step is needed.
