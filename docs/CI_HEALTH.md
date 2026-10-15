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

### Evidence collection (deployed in 2bda4bc)
In E2E mode the game logs every pointer press and release it receives through the Input System, with position, the UI elements under it and the frame time (`[UIDiag] pointer pressed ...`). The harness records every click it makes, and on any failed check saves a screenshot (`webgl-e2e-FAIL-<n>.png`) and prints, for the last three clicks, a classification. Nothing about the checks changed.

### First evidence: run 38705487149 (commit 2bda4bc, main): 497 passed, 3 failed
Failing starting point: **"selecting a vehicle loads it into the showroom"** (Garage, click on the second vehicle card `Btn_GarVeh_bus_city_01`). The other two failures ("Set as Active: the server accepted", "the active vehicle is persisted in the database") are cascades: with the bus never selected, Set as Active acts on the truck, so the expected `[Garage] active id=bus_city_01` line never comes. One root failure, not three.

What the diagnostics show:
* The harness aimed at the published target (0.245, 0.459); the game logged a pointer press at (250, 311) of a 1024x576 frame (y axis up, so 265 px from the top: the same point).
* The failure screenshot shows the vehicle list with the Danfo City Bus card at that point (about y 238 to 292 from the top, x 172 to 332). The target had not moved and nothing covers it.
* Frame time at the press was 226 ms (software GL, about 4 fps).
* The game did not log a selection (`[Showroom] ready vehicle=bus_city_01`) in the 120 s the check waits.
So: the click reached the game, at the right place, on a visible card, and the card did not react. That rules out a mis-aimed or moved click and a press lost before the game saw it.

**Correction to my own classifier.** It printed `MISSED` for this click, based on `ui hits=[]` at the press. The same `ui hits=[]` and `MISSED` were printed for the Repair and Refuel clicks that immediately before had worked (their checks passed). The hit list is therefore not evidence of a miss, and the `MISSED` label was misleading. The classifier now uses press position against the aimed position, and the `ui hits` text is printed but ignored.

**Cause: still not established.** Candidates that fit the evidence: (a) Unity UI did not invoke the card's Button although the press arrived (an input-event timing problem at about 4 fps, where the 150 ms press-hold falls inside one frame); (b) the Button was invoked but the handler ignored it (for example while the list was being refreshed after the Refuel purchase); (c) the vehicle list had just been rebuilt and the old card object received the press. Not tested, so none is a finding.

**Decisive instrument added (pushed after this run, no behaviour change):** in E2E mode every button now logs `[UIDiag] onClick <name>` when Unity UI invokes it. For a failed click the harness prints `NO-ONCLICK` (the press arrived but Unity UI did not invoke the button: candidate a or c), `CLICKED` (the button fired and the handler did nothing: candidate b), or `OFF-TARGET`, `MOVED`, `NO-PRESS`. The next failing run will say which. The classifier was exercised on sample log lines only; it has not yet run in CI.

## Status of every check, commit 2bda4bc (main run 38705487149; the branch run was partly cancelled by a later push)
| Check | Result | Note |
|---|---|---|
| Repository safety gate | pass | |
| Network core (.NET) | pass | 1017 tests at the baseline |
| Database migrations + rule tests | pass | |
| Backend over HTTP | pass on main; **fail on the branch run: infrastructure** (Docker Hub pull limit in "Initialize containers", no test ran) | not re-run: the run was cancelled by the next push |
| Unity tests + WebGL build | pass | branch run's copy cancelled by a newer push, not a failure |
| WebGL boots, Drive Low, Drive Medium | pass | |
| Lighting (Low enforced; Medium and studies informational) | pass | the "contrast 0 + vignette off" study variant fails the noon road threshold (77.7 < 80); that variant is not shipped, the job still passes |
| Browser E2E | **fail, 497/500** | one root failure (above), a real unresolved problem, not infrastructure |
| World map | pass | |
