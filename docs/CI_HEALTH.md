# CI health: what passes, what fails, and why

Recorded from GitHub (job results of the runs themselves), commit 0a853087 unless stated. "Infrastructure" means the job died before any test body ran.

## Jobs
| Job | State at 0a85308 | Notes |
|---|---|---|
| Network core logic (.NET unit tests, 1017) | passes on every run | pure C# models, no engine |
| Repository safety gate | passes on every run | secrets, licences, third-party paths |
| Database migrations + rule tests | passes; **infrastructure failures** on some runs | see below |
| Backend over HTTP (PostgREST, auth, security) | passes; infrastructure failures on some runs | see below |
| Unity tests + WebGL build | passes; **infrastructure failures** on some runs | see below |
| Lighting at four hours (Low enforced, Medium informational) | passes | the Low thresholds in `tools/webgl-lighting.js` are enforced |
| WebGL browser smoke: load, Drive scenario (Low and Medium), world map | pass | the drive test is game-clock based, anchored to the throttle press |
| WebGL browser smoke: **end-to-end suite (about 530 checks)** | **fails intermittently** | see below |

## Infrastructure failures (no test ran)
* `Initialize containers` fails with `toomanyrequests: Data limit exceeded` while pulling `public.ecr.aws/docker/library/postgres:15` (the Postgres service container of the database, backend and browser jobs). It hits one job at random; the same job passes on the other run of the same commit. Re-running the failed job passes.
* `Failed to resolve the latest game-ci CLI release: GitHub API returned 403` in the Unity job (the game-ci action looks up its CLI with an unauthenticated GitHub API call that is rate limited). Seen on two runs; re-running passes.
* A push to both `main` and the working branch starts two runs; the Unity job uses a single licence slot, so one run's Unity job waits for the other (and a pending run is cancelled when a newer one arrives). A "cancelled" Unity job is this, not a failure.

## The intermittent end-to-end failures
The suite fails one to seven checks in roughly half of the CI runs, always starting at a single click whose effect the test waits 60 s for (the later failures in the same run are checks that depend on it). It predates the lighting work: it failed at 4f7ace0, 2e9468d and 550f9fd, and the same commit has passed on one run and failed on another. Failing starting points seen so far:
View Profile (933f34f main), the Jobs board tab (4450e2c twice, 0a85308 branch), the Requests tab (4450e2c, 0dfc313 main, 0a85308 main), the Front preset (cb45c26, 64581f5), an exact username search (64581f5), Recent Players (4f7ace0), sorting by name (550f9fd).
Two remedies were tried and did not work: a retry that repeated a click when the game logged no press line (it printed no repeat in the next run, which means either every press was seen or the safeguard never switched on; an earlier note here that the build 'rarely logs presses' was a wrong inference, because the CI log holds only the harness output, not the browser console, and the project runs both input handlers: `activeInputHandler: 2`), and a 700 ms press hold. The assertions, regexes and timeouts have never been changed.
Cause: **not yet established.** Evidence now being collected (see below). This section is updated with the result.

### Evidence collection (added at the next commit after 0a85308)
In E2E mode the game logs every pointer press and release it receives through the Input System, with position, the UI elements under it and the frame time (`[UIDiag] pointer pressed ...`). The harness records every click it makes, and on any failed check saves a screenshot (`webgl-e2e-FAIL-<n>.png`) and prints, for the last three clicks, one of: `NO-PRESS` (the game never received a press), `MISSED` (it did, but not on the target: the target moved or was covered), `HIT` (the press landed on the target and the effect is missing for another reason). Nothing about the checks changed.
