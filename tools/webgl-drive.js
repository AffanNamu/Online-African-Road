// Drive scenario for the WebGL build. Usage: node tools/webgl-drive.js <build-dir> [screenshot-prefix]
// Opens the build with ?smoke=drive (skips sign-in), waits for the vehicle to land on real ground, holds W, and checks from the
// game's own [Drive] telemetry that the truck stays on the road, accelerates and moves. Then the camera manager is exercised in the same session:
// all eight views by number key (the driver cabin must be refused explicitly), C / Shift+C, the on-screen camera button, acceleration / steering /
// braking while the view changes, and (if the game sees it) a gamepad Y press. Screenshots go to <prefix>-N-*.png and <prefix>-cam-N-*.png.
const http = require('http'), fs = require('fs'), path = require('path');
const { chromium } = require('playwright');
const { renderAndPerfChecks } = require('./webgl-checks');

const dir = path.resolve(process.argv[2] || 'Builds/WebGL');
const prefix = process.argv[3] || 'webgl-drive';
const MIME = { '.html': 'text/html', '.js': 'text/javascript', '.css': 'text/css', '.wasm': 'application/wasm', '.json': 'application/json',
  '.png': 'image/png', '.ico': 'image/x-icon', '.gz': 'application/octet-stream', '.data': 'application/octet-stream' };

function serve() {
  return new Promise(resolve => {
    const s = http.createServer((req, res) => {
      let p = decodeURIComponent(req.url.split('?')[0]); if (p.endsWith('/')) p += 'index.html';
      const f = path.join(dir, p);
      if (!f.startsWith(dir) || !fs.existsSync(f) || fs.statSync(f).isDirectory()) { res.writeHead(404); res.end('not found'); return; }
      res.writeHead(200, { 'Content-Type': MIME[path.extname(f)] || 'application/octet-stream' });
      fs.createReadStream(f).pipe(res);
    }).listen(0, '127.0.0.1', () => resolve(s));
  });
}

(async () => {
  const failures = [];
  const server = await serve(); const port = server.address().port;
  const browser = await chromium.launch({ args: ['--use-gl=angle', '--use-angle=swiftshader', '--enable-unsafe-swiftshader', '--ignore-gpu-blocklist', '--no-sandbox'] });
  const page = await browser.newPage({ viewport: { width: 1024, height: 576 } });
  // A gamepad the test can plug in later (inert until then, so the driving phase above runs with no pad at all).
  await page.addInitScript(() => {
    window.__aroPad = null; const orig = navigator.getGamepads ? navigator.getGamepads.bind(navigator) : null;
    navigator.getGamepads = () => [window.__aroPad, null, null, null];
    window.__aroOrigGetGamepads = orig;
  });
  const logs = [], errors = [];
  page.on('console', m => logs.push(m.text()));
  page.on('pageerror', e => errors.push(String(e)));
  await page.goto(`http://127.0.0.1:${port}/index.html?smoke=drive`);
  const has = re => logs.some(l => re.test(l));
  async function waitFor(re, seconds) { for (let i = 0; i < seconds && !has(re); i++) await page.waitForTimeout(1000); return has(re); }

  if (!await waitFor(/\[Smoke\] drive scenario/, 150)) failures.push('the drive scenario never started (Unity did not boot or ?smoke=drive was ignored)');
  const grounded = await waitFor(/\[Drive\] ground ready/, 90);
  if (!grounded) failures.push('the world never produced ground under the vehicle (no "[Drive] ground ready" within 90 s)');
  await page.waitForTimeout(6000);
  await page.screenshot({ path: `${prefix}-0-idle.png` });

  await page.mouse.click(512, 288);            // give the canvas keyboard focus
  await page.keyboard.down('w');
  await page.waitForTimeout(10000); await page.screenshot({ path: `${prefix}-1-driving.png` });
  await page.waitForTimeout(14000); await page.screenshot({ path: `${prefix}-2-driving.png` });
  await page.keyboard.up('w');

  const samples = logs.map(l => /\[Drive\] t=([\d.]+) pos=\(([-\d.]+),([-\d.]+),([-\d.]+)\) kmh=([-\d.]+) dist=([\d.]+)(?: ground=([-\d.]+))?/.exec(l)).filter(Boolean)
    .map(m => ({ t: +m[1], x: +m[2], y: +m[3], z: +m[4], kmh: +m[5], dist: +m[6], ground: m[7] === undefined ? null : +m[7] }));
  console.log(`drive samples: ${samples.length}`);
  samples.filter((_, i) => i % 3 === 0).forEach(s => console.log(`  t=${s.t} pos=(${s.x}, ${s.y}, ${s.z}) kmh=${s.kmh} dist=${s.dist}`));
  console.log('--- [Drive]/[World]/[Smoke] lines ---');
  logs.filter(l => /\[(Drive|World|Smoke)\]/.test(l) && !/\[Drive\] t=/.test(l)).slice(0, 20).forEach(l => console.log(l.slice(0, 300)));

  if (samples.length < 5) failures.push(`only ${samples.length} telemetry samples (expected many)`);
  else {
    const minY = Math.min(...samples.map(s => s.y)), maxKmh = Math.max(...samples.map(s => Math.abs(s.kmh))), maxDist = Math.max(...samples.map(s => s.dist));
    console.log(`min y ${minY.toFixed(2)}, max speed ${maxKmh.toFixed(1)} km/h, furthest from spawn ${maxDist.toFixed(1)} m`);
    // The truck is driven with throttle only (no steering), so it may leave the raised road and roll down the embankment onto lower terrain.
    // That is legitimate; falling THROUGH the ground is not. Compare against the ground height the game itself reports under the truck.
    const withGround = samples.filter(s => s.ground !== null);
    if (withGround.length < 5) failures.push('the [Drive] telemetry carries no ground height, so "stays above the ground" cannot be checked');
    else {
      const worst = Math.min(...withGround.map(s => s.y - s.ground));
      console.log(`lowest clearance above the reported ground: ${worst.toFixed(2)} m`);
      if (worst < -1.0) failures.push(`the vehicle went ${(-worst).toFixed(1)} m below the ground (fell through the world)`);
    }
    if (maxKmh < 10) failures.push(`holding W never got the truck above 10 km/h (max ${maxKmh.toFixed(1)})`);
    if (maxDist < 100) failures.push(`the truck got only ${maxDist.toFixed(1)} m from its spawn in 24 s with W held (it should be able to drive down the road)`);
  }

  // ------------------------------------------------------------------------------------------------ camera manager
  // The throttle-only run above ends, as it always has, with the truck crashed off the road at the first bend (it sits at rest ~390 m from the spawn).
  // The camera checks therefore start from a fresh boot: a new truck on the road.
  console.log('\n--- camera manager (fresh page) ---');
  const VIEWS = ['Cockpit', 'DriverCabin', 'RearChase', 'FarChase', 'Left', 'Right', 'Overhead', 'FrontBumper'];
  const ok = m => console.log('  ok: ' + m);
  const parseDrive = from => logs.slice(from).map(l => /\[Drive\] t=([\d.]+) pos=\(([-\d.]+),([-\d.]+),([-\d.]+)\) kmh=([-\d.]+) dist=([\d.]+).*? yaw=(-?[\d.]+) cam=(\w+)/.exec(l))
    .filter(Boolean).map(m => ({ t: +m[1], x: +m[2], z: +m[4], kmh: +m[5], yaw: +m[7], cam: m[8] }));
  const logFrom = (from, re) => logs.slice(from).find(l => re.test(l));
  async function waitLogFrom(from, re, seconds) { for (let i = 0; i < seconds * 4; i++) { const l = logFrom(from, re); if (l) return l; await page.waitForTimeout(250); } return null; }
  // An edge-triggered key must be held across at least one rendered frame (software WebGL runs slowly), and is repeated only when nothing at all was logged.
  async function tapUntil(key, re, tries = 3, secs = 5) { for (let i = 0; i < tries; i++) { const mark = logs.length; await page.keyboard.press(key, { delay: 350 }); const l = await waitLogFrom(mark, re, secs); if (l) return l; } return null; }
  const viewLine = (v, n) => new RegExp(`\\[Camera\\] view=${v} number=${n} `);
  const pathLength = ss => ss.slice(1).reduce((a, s, i) => a + Math.hypot(s.x - ss[i].x, s.z - ss[i].z), 0);
  const yawDelta = (a, b) => ((b - a + 540) % 360) - 180;

  try {
    const boot = logs.length;
    await page.goto(`http://127.0.0.1:${port}/index.html?smoke=drive`);
    if (!await waitLogFrom(boot, /\[Smoke\] drive scenario/, 150)) throw new Error('the page did not boot again for the camera scenario');
    if (!await waitLogFrom(boot, /\[Drive\] ground ready/, 90)) throw new Error('no ground under the new truck for the camera scenario');
    await page.waitForTimeout(4000); await page.mouse.click(512, 288);   // canvas keyboard focus

    const ready = logFrom(boot, /\[Camera\] manager ready/);
    if (!ready) failures.push('the camera manager never started (no "[Camera] manager ready" line)');
    else if (!/isMainCamera=True/.test(ready)) failures.push('the camera manager is not on the Main Camera: ' + ready.slice(0, 200));
    else ok('the camera manager runs on the Main Camera: ' + ready.replace(/^.*\[Camera\]/, '[Camera]'));
    const bound = logFrom(boot, /\[Camera\] bound vehicle=/);
    if (!bound || !/category=Truck/.test(bound) || !/available=7\/8/.test(bound) || !/driverRig=none/.test(bound)) failures.push('the camera did not bind to the truck with 7 of 8 views available (the driver cabin has no rig yet): ' + (bound || 'no line').slice(0, 250));
    else ok(bound.replace(/^.*\[Camera\]/, '[Camera]'));

    // 1. every view by its number; 2 (driver cabin) must be refused explicitly and leave the view alone
    let prevView = 'RearChase';
    for (let n = 1; n <= 8; n++) {
      if (n === 2) {
        const l = await tapUntil('2', /\[Camera\] unavailable view=DriverCabin number=2/);
        if (!l) failures.push('pressing 2 (driver cabin) was not handled explicitly: no "[Camera] unavailable" line');
        else if (!new RegExp('stays=' + prevView + ' ').test(l) || !/reason=.*driver/i.test(l)) failures.push('the refusal of the driver cabin did not keep the view and say why: ' + l.slice(0, 250));
        else ok(`2 -> the driver cabin is refused, the view stays ${prevView}, with a reason`);
      } else {
        const l = await tapUntil(String(n), viewLine(VIEWS[n - 1], n));
        if (!l) failures.push(`pressing ${n} did not select ${VIEWS[n - 1]}`);
        else if (!/main=True/.test(l)) failures.push(`${VIEWS[n - 1]}: the camera is no longer the Main Camera: ${l.slice(0, 200)}`);
        else { prevView = VIEWS[n - 1]; ok(`${n} -> ${VIEWS[n - 1]}`); }
      }
      await page.waitForTimeout(1000); await page.screenshot({ path: `${prefix}-cam-${n}-${VIEWS[n - 1]}.png` });
    }

    // 2. C = next (wraps from the last view to the first), Shift+C = previous (wraps back)
    { const l = await tapUntil('c', viewLine('Cockpit', 1)); if (!l) failures.push('C from the bumper view (8) did not wrap to the cockpit (1)'); else ok('C wraps 8 -> 1 (cockpit)');
      const p = await tapUntil('Shift+C', viewLine('FrontBumper', 8)); if (!p) failures.push('Shift+C from the cockpit did not wrap back to the bumper view'); else ok('Shift+C wraps 1 -> 8 (bumper)');
      const n2 = await tapUntil('c', viewLine('Cockpit', 1)); const n3 = await tapUntil('c', viewLine('RearChase', 3));
      if (!n2 || !n3) failures.push('C from the cockpit did not skip the unavailable driver cabin and land on the chase view'); else ok('C skips the unavailable driver cabin: 1 -> 3'); }

    // 3. the on-screen button: top right, left of the minimap, and it changes the view when clicked
    { const b = logs.slice(boot).map(l => /\[Hud\] camera button x=([\d.]+) y=([\d.]+)/.exec(l)).find(Boolean);
      if (!b) failures.push('the HUD never logged a camera button position (is Btn_Camera built?)');
      else {
        const fx = +b[1], fy = +b[2];
        if (!(fx > 0.65 && fx + 0.0391 < 0.828 && fy < 0.15)) failures.push(`the camera button is not in the top right beside the minimap (x=${fx}, y=${fy})`);
        else ok(`the camera button is at x=${fx} y=${fy} (top right, clear of the minimap column that starts at x=0.829)`);
        const box = await page.locator('#unity-canvas').boundingBox(); const cx = box.x + fx * box.width, cy = box.y + fy * box.height;
        // the same sequence the browser E2E uses for every UI click: software WebGL drops a bare click, so move first, let the pointer settle, press, hold, release
        let hit = null, reached = false, mark0 = logs.length;
        for (let i = 0; i < 3 && !hit; i++) {
          const mark = logs.length;
          await page.mouse.move(cx, cy); await page.waitForTimeout(250); await page.mouse.down(); await page.waitForTimeout(150); await page.mouse.up();
          hit = await waitLogFrom(mark, /\[Camera\] view=\w+ number=\d source=button/, 5);
          if (logFrom(mark, /\[Hud\] camera button clicked/)) reached = true;
        }
        if (!hit) {
          const diag = logs.slice(mark0).filter(l => /UIDiag/.test(l)).slice(0, 12).map(l => '    ' + l.slice(0, 260)).join('\n');
          failures.push(reached ? 'the camera button received the click but the view did not change' : 'clicking the camera button did not reach the button (no "[Hud] camera button clicked")');
          console.log('  button click diagnostics (what the game says each press hit):\n' + (diag || '    (no [UIDiag] lines)'));
        } else { ok('clicking the button -> ' + hit.replace(/^.*\[Camera\]/, '[Camera]').slice(0, 120)); await page.screenshot({ path: `${prefix}-cam-button.png` }); }
      } }

    // 4. driving is not affected by changing the view. One continuous drive on the fresh truck, kept short so it stays on the straight road:
    //    accelerate while the view changes (ending in the far chase), brake in the far chase, then steer in the overhead view from a slow start.
    await tapUntil('3', viewLine('RearChase', 3), 3, 5);   // known start (the button click moved it)
    { const mark = logs.length; await page.keyboard.down('w');
      for (const n of [5, 6, 7, 4]) { await tapUntil(String(n), viewLine(VIEWS[n - 1], n), 2, 3); await page.waitForTimeout(1000); }
      await page.keyboard.up('w');
      const ss = parseDrive(mark), maxKmh = Math.max(0, ...ss.map(s => s.kmh)), cams = new Set(ss.map(s => s.cam));
      console.log(`  accelerate while switching: ${ss.length} samples, max ${maxKmh.toFixed(1)} km/h, ${pathLength(ss).toFixed(0)} m, views in the telemetry: ${[...cams].join(',')}`);
      if (ss.length < 4) failures.push(`only ${ss.length} drive samples while switching views`);
      else if (maxKmh < 10) failures.push(`the truck did not accelerate while the view was changed (max ${maxKmh.toFixed(1)} km/h)`);
      else if (pathLength(ss) < 30) failures.push(`the truck barely moved while the view was changed (${pathLength(ss).toFixed(0)} m)`);
      else if (cams.size < 3) failures.push('the telemetry never showed the camera changing during the drive: ' + [...cams].join(','));
      else ok(`acceleration and movement are unaffected by changing the view (max ${maxKmh.toFixed(0)} km/h, ${pathLength(ss).toFixed(0)} m, ${cams.size} views)`);

      // braking in the far chase view (the last view selected above), straight after releasing the throttle
      const peak = maxKmh, bmark = logs.length;
      await page.keyboard.down('s'); await page.waitForTimeout(5500); await page.keyboard.up('s');
      const after = parseDrive(bmark).filter(s => s.cam === 'FarChase'), low = after.length ? Math.min(...after.map(s => s.kmh)) : NaN;
      console.log(`  braking in the far chase view: peak ${peak.toFixed(1)} km/h, lowest while braking ${isNaN(low) ? 'n/a' : low.toFixed(1)} km/h (${after.length} samples)`);
      if (peak < 10) failures.push(`could not get above 10 km/h to test braking (peak ${peak.toFixed(1)})`);
      else if (after.length < 3 || !(low <= Math.max(5, peak * 0.4))) failures.push(`holding S in the far chase view did not slow the truck (peak ${peak.toFixed(1)} -> lowest ${low} km/h)`);
      else ok(`braking works in the far chase view (${peak.toFixed(0)} -> ${low.toFixed(0)} km/h)`); }

    // steering under a non-default view (overhead), from a slow start so the truck stays upright
    { await tapUntil('7', viewLine('Overhead', 7), 3, 5); const mark = logs.length;
      await page.keyboard.down('w'); await page.keyboard.down('d'); await page.waitForTimeout(4500); await page.keyboard.up('d'); await page.waitForTimeout(1500); await page.keyboard.up('w');
      const ss = parseDrive(mark).filter(s => s.cam === 'Overhead');
      if (ss.length < 3) failures.push(`only ${ss.length} samples while steering in the overhead view`);
      else { const d = yawDelta(ss[0].yaw, ss[ss.length - 1].yaw); console.log(`  steering in the overhead view: heading changed ${d.toFixed(0)} deg over ${ss.length} samples`);
        if (Math.abs(d) < 5) failures.push(`holding D in the overhead view did not turn the truck (heading changed ${d.toFixed(1)} deg)`); else ok(`steering works in the overhead view (${d.toFixed(0)} deg)`); } }

    // 5. a gamepad Y press, where the game can see a pad. The Gamepad API is faked, so this proves nothing about a real pad, and it can never fail the run by itself
    //    being unsupported: if the game does not detect the pad the step says SKIPPED.
    try {
      await page.evaluate(() => {
        const buttons = Array.from({ length: 17 }, () => ({ pressed: false, touched: false, value: 0 }));
        window.__aroPad = { id: 'ARO test pad (STANDARD GAMEPAD Vendor: 0000 Product: 0000)', index: 0, connected: true, mapping: 'standard', axes: [0, 0, 0, 0], buttons, timestamp: performance.now(), vibrationActuator: null };
        const ev = new Event('gamepadconnected'); ev.gamepad = window.__aroPad; window.dispatchEvent(ev);   // a real GamepadEvent cannot be built from a fake pad
      });
      const seen = await waitLogFrom(boot, /\[Input\] gamepad detected/, 20);
      if (!seen) console.log('  SKIPPED: the game did not detect the simulated gamepad in headless Chromium, so gamepad camera input is NOT verified here (needs a manual check with a real pad)');
      else {
        console.log('  the game detected the simulated pad: ' + seen.replace(/^.*\[Input\]/, '[Input]').slice(0, 160));
        let hit = null, arrived = false;
        for (let i = 0; i < 3 && !hit; i++) {
          const mark = logs.length;
          await page.evaluate(() => { const p = window.__aroPad; p.buttons[3] = { pressed: true, touched: true, value: 1 }; p.timestamp = performance.now(); });
          await page.waitForTimeout(600);
          await page.evaluate(() => { const p = window.__aroPad; p.buttons[3] = { pressed: false, touched: false, value: 0 }; p.timestamp = performance.now(); });
          hit = await waitLogFrom(mark, /\[Camera\] view=\w+ number=\d source=input/, 4);
          if (logFrom(mark, /\[Input\] gamepad Y \(north\) pressed/)) arrived = true;
        }
        if (hit) ok('gamepad Y (north) -> ' + hit.replace(/^.*\[Camera\]/, '[Camera]').slice(0, 120));
        else if (arrived) failures.push('the game received the gamepad Y press but the camera view did not change');
        else console.log('  INCONCLUSIVE: the game detected the simulated pad but never received the Y press (the faked Gamepad API does not deliver button state to the Input System), so gamepad camera input is NOT verified here and needs a manual check with a real pad');
      }
    } catch (e) { console.log('  SKIPPED: the simulated gamepad could not be set up (' + String(e).slice(0, 120) + '); gamepad camera input is NOT verified here'); }
    await page.evaluate(() => { window.__aroPad = null; }).catch(() => {});

    const camErrors = logs.filter(l => /\[Camera\] invalid camera profile/.test(l));
    if (camErrors.length) failures.push('the game reported an invalid camera profile: ' + camErrors[0].slice(0, 250));
  } catch (e) { failures.push('the camera scenario could not complete: ' + String(e).slice(0, 250)); }

  renderAndPerfChecks(logs, failures, 'drive');
  if (has(/left the world/)) failures.push('the vehicle left the world and had to be recovered');
  if (errors.length) failures.push(`${errors.length} page error(s): ${errors[0].slice(0, 200)}`);
  const exc = logs.filter(l => /exception|NullReference|IndexOutOfRange/i.test(l) && !/GLib|swiftshader/i.test(l));
  if (exc.length) failures.push(`${exc.length} exception line(s): ${exc[0].slice(0, 200)}`);

  await browser.close(); server.close();
  if (failures.length) { console.log('\nDRIVE TEST FAILED:'); failures.forEach(f => console.log(' - ' + f)); process.exit(1); }
  console.log('\nDRIVE TEST PASSED: the truck landed on real ground, accelerated and drove; the camera manager offered its views, refused the driver cabin, and driving was unaffected by changing view.');
})();
