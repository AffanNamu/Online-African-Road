# Checking the night environment on a real GPU (a local copy of the WebGL build)

CI renders with a software GL driver, so its frame times mean nothing and its pictures are not what a player's GPU draws. This is how to run the same build on your own machine. You need the WebGL build of the commit you want to check and Python 3; nothing is installed into the game and no credentials are involved.

## 1. Get the build
GitHub, repository `AffanNamu/Online-African-Road`, tab Actions, the CI run of the commit (branch `work/kano-residential`), section Artifacts, **`webgl-build`**. Download, unzip into a folder (for example `aro-build`). The build has Unity's decompression fallback switched on, so a plain static server works (no special headers).

## 2. Serve it and open it
```
cd aro-build
python3 -m http.server 8000
```
Open `http://localhost:8000/index.html?smoke=drive&quality=high&hour=night&start=3600` in Chrome or Edge. `?smoke=drive` is the test page: no sign-in, the truck is dropped on the road. Without `hold=1` you drive freely (W A S D, Space brake, C camera, 1 to 8 camera views).
To confirm the browser really uses your GPU: open `chrome://gpu` ("Graphics Feature Status: WebGL2 Hardware accelerated"), and in the game's console (F12) the `[Render]` line shows `graphicsApi=`; a software driver would say SwiftShader there.

## 3. What to try (each is one page address; add or change the parts after `?`)
| Check | Page address after `index.html` | What to look at |
|---|---|---|
| Light pools and lamps, residential, commercial, expressway, village, dark open road | `?smoke=drive&quality=high&hour=night&start=5600` (residential), `3600` (commercial), `11500` (expressway), `34000` (village), `24000` (dark open road) | pools of light on the road amber, not cream or white; glow around lamp heads; houses and shops with lit windows; the dark road really dark |
| Wet roads on all three tiers | add `&weather=rain` (or `wet`, `cloudy`, `clear`) and `quality=low`, `medium`, `high` | amber streaks under the lamps, puddles on the road, wetter-looking asphalt on every tier |
| Low beam against high beam | `?smoke=drive&quality=high&hour=night&start=24000`, drive, press **K** | the road ahead lights up farther and brighter with the high beam; **K** again dips it; **L** switches the lights off |
| Rear lamps, brake, hazards, indicators | same page; **Space** or **S** to brake, **T** hazards, **Q** left, **E** right (chase view C) | rear lamps dim red with the lights on, bright red on the brake, hazards flash both sides together, indicators one side |
| Red mast beacons | `?smoke=drive&quality=high&hour=night&start=13632`, look to the left, about 150 m ahead | red lights near the top and half way up a telecom mast, a short double flash every two seconds |
| Lit traffic | `?smoke=drive&quality=high&hour=night&start=11500`, drive on the expressway | other vehicles with white head lamps and red tail lamps |
| A whole dusk or dawn | `?smoke=drive&quality=high&hour=16.6&hourrate=0.04&start=3600` (dusk), `hour=4.8&hourrate=0.04` (dawn) | the clock runs 0.04 game hours per second (a dusk in about a minute): lamps start to glow, brighten to full by about 18:10, headlights come on by themselves; the reverse at dawn |

## 4. What to send back
Screenshots of what you saw (especially anything that looks wrong: too bright, too dark, flicker, popping lamps, white pools), the browser console lines that start with `[Lamp]`, `[Light]`, `[Traffic]` and `[Perf]` (copy them from F12, Console), the machine's GPU and the quality you used. `[Perf] ... avgFrameMs=` on a real GPU is a real frame time.

## 5. Optional: run the whole check script on your GPU
`tools/webgl-night.js` is the script CI runs (real key presses, bursts of frames, checks, contact sheets). On your machine, with Node 20 or later:
```
npm install playwright pngjs
npx playwright install chromium
NIGHT_GPU=1 NIGHT_HEADED=1 node tools/webgl-night.js aro-build night-gpu
```
It prints the GL renderer it got (it must name your GPU, not SwiftShader), a PASS or FAIL line per check, and writes `night-gpu-sheet-*.png` contact sheets and `night-gpu-night-report.json`. PASS and FAIL are the script's own checks, not a verdict on how it looks.

## What counts as verified (the words used in the docs)
* **Implemented:** the code exists and compiles in Unity.
* **Passed automated tests:** unit tests (pure rules), Unity edit-mode tests, and the checks of `tools/webgl-night.js` and `tools/webgl-lighting.js` in CI. Software GL.
* **Visually verified:** a person (or I, from the CI pictures) looked at a real frame of the running game and judged it. On a real GPU: only when you have run the steps above.
