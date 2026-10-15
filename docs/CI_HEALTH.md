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

## Second flaky check, cause established: the HTTP rate-limit test
Run 38706152411 (commit 1e03ee1, main), job "Backend over HTTP": 112 passed, 1 failed: `excessive requests are rate limited (HTTP) -> {"ok":45,"limited":0}`. This job passed on the previous commit with identical backend code.

Cause (from the code, and the result itself): `_rate_limit` is a **fixed 60 s window** (`find_player`: 30 calls per 60 s, `supabase/migrations/20261010000002_foundation.sql`, `20261010000005_social.sql`). The test sends 45 calls in a row and expects some to be refused. With a 30-call window, 45 calls inside ONE window must give at least 15 refusals, so `ok:45, limited:0` can only happen when the loop straddled a window boundary and its calls were split across two windows (for example 20 + 25). The chance of this per run is roughly the loop duration over 60 s, which is why it appears rarely and at random. The limiter is behaving as designed; the test was racing the clock.

Fix (test only, the assertion `limited > 0 && ok <= 40` is unchanged): `tools/e2e/api_e2e.js` now waits for the next window when fewer than 15 s of the current one remain, so the loop always runs inside one window. Not yet seen passing in CI after the change.

## Infrastructure failures in the 1e03ee1 runs (no test ran)
* "WebGL build boots in a browser" (main): `docker pull public.ecr.aws/docker/library/postgres:15` failed three times with `toomanyrequests: Data limit exceeded` in "Initialize containers". The job then continued into later steps with the workspace missing (`Cannot find module tools/webgl-drive.js`, `tools/e2e/up.sh: No such file`), so five steps show red; **none of them ran a test**. This is why a single image-pull failure can look like many failures.
* "Database migrations + rule tests" (main) and "Backend over HTTP" (branch): the same image pull failure.
Re-run when it happens; the cause is the registry's rate limit on the runner's address, not the repository.

## Click failures: what four more runs showed (updated)

| Run (commit, branch) | Browser E2E | Notes |
|---|---|---|
| 1e03ee1 (main) | did not run | Docker pull limit in "Initialize containers"; later steps ran with no workspace |
| c20f75b (branch) | did not run | same Docker pull limit (smoke job); Backend, DB, Unity passed |
| fcd14a6 (branch) | **500/500 pass** | click probe: control click failed (probe flaw, see below) |
| da256f5 (main) | **500/500 pass** | same probe flaw; the job log shows `toomanyrequests: Rate exceeded` once at the start and carried on |
| ca4620d (branch) | **498/500, fail** | one root failure: `Btn_PrStyle_jobs` ("a new play style is saved"); the second failure is its database check |

The new onClick diagnostics have now classified a real failure:
* The `Btn_PrStyle_jobs` click: the game logged a pointer press at (826, 141), exactly the aimed target (0.807, 0.754); `ui hits=[]` (nothing raycast), no `onClick`. The two clicks just before it (Language, English) each had the button in `ui hits` and an `onClick` line, so the diagnostics work in this build (the probe's HOOK CHECK also confirmed it).
* The failure screenshot, taken afterwards, shows the "Deliver freight" button at exactly that position and the previous play style still selected.
* So: **the press reached the game at the right pixel, Unity UI found nothing there to click, and the button was never invoked.** That excludes a lost or mis-aimed input and excludes a handler that ignored a click; the same signature (press at target, `ui hits=[]`) was recorded in the Garage failure of run 38705487149.
* **Not yet shown: why nothing was there.** The most likely reason, from reading the code, is a loading state: a region that reloads its data is rebuilt WITHOUT its buttons until the server answers (for example `BuildGarList` returns early with "Loading vehicles..." while the data state is Idle or Loading), and the harness clicks at the position the game published before the reload. The harness' fixed 4 s wait is shorter than that reload under the software renderer (about 5 frames per second). This is a hypothesis, not a finding.
* A contradiction I have not resolved: in run 38705487149 the Repair and Refuel clicks also printed `ui hits=[]` and still worked. That is why `ui hits=[]` alone is not accepted as the cause.

Added to settle it (no gameplay behaviour, E2E mode only): the press line now lists the buttons that exist under the pointer whether or not anything raycast to them, every region rebuild is logged with a timestamp, and the harness prints for a failed click NOTHING-THERE (no button existed at that spot: a loading state or rebuild), BLOCKED (a button existed but nothing raycast to it) or NO-ONCLICK (it was hit and not invoked), plus how many ms before the press the last rebuild happened and how soon the next one followed.

Harness change (waits only, no assertion changed): `settle()` now also waits until the game has published no new click positions for 2.5 s (at most 20 s), instead of a fixed 4 s. If the loading-state hypothesis is right this removes the failure; if the failure persists, the new diagnostics will say what was under the pointer.

Probe findings: the control click failed in the first two probe runs because the probe looked up the target once with no retry; with retry it selected the bus and logged `onClick`. The probe's Repair and Refuel trials never completed ("the service action never finished"), which is a probe defect (its raw click bypassed the diagnostics); fixed in the next commit. The question the probe was written for (does a press before and a release after a list rebuild lose the click?) is still unanswered.

## Database job also lost to the image pull
ca4620d (branch): "Database migrations + rule tests" failed in "Initialize containers" (nothing ran; it passed on the main run of the same commit family).
