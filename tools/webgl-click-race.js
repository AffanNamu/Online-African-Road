// Click-versus-rebuild probe (INFORMATIONAL: it prints a verdict and never fails the build; the browser end-to-end suite is the test).
// Question it answers: in the Garage, does a click that is PRESSED before the page rebuilds its widgets and RELEASED after the rebuild get lost?
// Why it matters: DashboardScreen.Refill destroys every child of a region and builds NEW buttons. Unity UI sends a click only when the object that
// received the press is the object under the pointer at release. After a Repair or Refuel the game rebuilds the vehicle list (DoService ends with RefreshGarage),
// a few hundred ms after the server answers; a harness click that lands in that gap was lost in run 38705487149 ("selecting a vehicle loads it into the showroom").
// Method: after a service action finishes, press on the second vehicle card as early as possible, hold until the rebuild has happened, release, and read the game's
// own log: did the press arrive before the rebuild (log order), and did Unity invoke the card's button ("[UIDiag] onClick Btn_GarVeh_bus_city_01")?
// A control click in a quiet period must work, otherwise the probe says so and stops. Usage: source /tmp/aro-e2e.env && node tools/webgl-click-race.js [prefix]
const { execFileSync } = require('child_process');
const { chromium } = require('playwright');

const BASE = process.env.E2E_URL, ANON = process.env.E2E_ANON_KEY, DB_URL = process.env.E2E_DB_URL;
if (!BASE || !ANON || !DB_URL) { console.error('source /tmp/aro-e2e.env first (tools/e2e/up.sh)'); process.exit(2); }
const prefix = process.argv[2] || 'webgl-click-race';
const sleep = ms => new Promise(r => setTimeout(r, ms));
const db = sql => execFileSync('psql', [DB_URL, '-X', '-q', '-At', '-v', 'ON_ERROR_STOP=1', '-c', sql], { encoding: 'utf8' }).trim();
const say = s => console.log(s);

async function signup(name) {
  const email = name.toLowerCase() + '@e2e.test', password = 'pw-' + name + '-123';
  const r = await fetch(BASE + '/auth/v1/signup', { method: 'POST', headers: { 'content-type': 'application/json' }, body: JSON.stringify({ email, password, data: { display_name: name } }) });
  const j = await r.json(); if (r.status !== 200) throw new Error('signup ' + name + ': ' + JSON.stringify(j));
  db(`update profiles set onboarded_at = now() where id = '${j.user.id}'`);
  return { name, email, password, id: j.user.id };
}

(async () => {
  const user = await signup('ProbePat');
  db(`select _apply_transaction('${user.id}', 'BONUS', 100000, 'fund:' || gen_random_uuid())`);
  db(`insert into vehicle_ownership(player_id, definition_id, fuel_l) values ('${user.id}', 'bus_city_01', 180)`);
  db(`update vehicle_ownership set damage_pct = 30, fuel_l = 10 where player_id = '${user.id}' and definition_id = 'truck_light_01'`);

  const browser = await chromium.launch({ args: ['--use-gl=angle', '--use-angle=swiftshader', '--enable-unsafe-swiftshader', '--ignore-gpu-blocklist', '--no-sandbox'] });
  const page = await browser.newPage({ viewport: { width: 1024, height: 576 } });
  const logs = []; page.on('console', m => logs.push(m.text()));
  const targets = () => { const l = [...logs].reverse().find(x => x.startsWith('[WorldMap] targets')) || ''; return Object.fromEntries([...l.matchAll(/TARGET (\S+) x=([\d.]+) y=([\d.]+)/g)].map(m => [m[1], { x: +m[2], y: +m[3] }])); };
  const waitFrom = async (re, from, ms) => { const end = Date.now() + ms; while (Date.now() < end) { const i = logs.findIndex((l, k) => k >= from && re.test(l)); if (i >= 0) return i; await sleep(10); } return -1; };
  const box = () => page.locator('#unity-canvas').boundingBox();
  const at = async name => { const t = targets()[name]; if (!t) return null; const b = await box(); return { x: b.x + t.x * b.width, y: b.y + t.y * b.height }; };
  // Every click reports what the game saw: was the target published, did a press arrive and where, did a release arrive, did Unity UI invoke the button (the control used to say only "NOT selected").
  const click = async (name, label, fast) => {
    let p = null; for (let i = 0; i < 100 && !(p = await at(name)); i++) await sleep(100);              // the game re-publishes click positions about a second after a rebuild
    const from = logs.length;
    if (!p) { say(`  click ${label || name}: NO TARGET POSITION published for ${name}; nothing was clicked`); return { found: false, from }; }
    await page.mouse.move(p.x, p.y); await sleep(250); await page.mouse.down(); await sleep(150); await page.mouse.up();
    if (fast) return { found: true, from };                                                              // no wait and no report: the caller is timing what happens next
    await sleep(1200);
    const seen = re => logs.slice(from).find(l => re.test(l)) || null;
    const r = { found: true, from, press: seen(/\[UIDiag\] pointer pressed/), release: seen(/\[UIDiag\] pointer released/), onClick: seen(new RegExp('\\[UIDiag\\] onClick ' + name + ' ')) };
    say(`  click ${label || name}: target found at (${p.x.toFixed(0)},${p.y.toFixed(0)}) | press: ${r.press ? r.press.replace('[UIDiag] ', '').slice(0, 90) : 'NONE'} | release: ${r.release ? 'yes' : 'NONE'} | onClick: ${r.onClick ? 'yes' : 'NONE'}`);
    return r;
  };
  const settleQuiet = async () => { await sleep(4000); let n = logs.filter(l => l.startsWith('[WorldMap] targets')).length, quiet = Date.now(); const end = Date.now() + 16000; while (Date.now() < end && Date.now() - quiet < 2500) { await sleep(250); const k = logs.filter(l => l.startsWith('[WorldMap] targets')).length; if (k !== n) { n = k; quiet = Date.now(); } } };
  const BUS = 'Btn_GarVeh_bus_city_01', TRUCK = 'Btn_GarVeh_truck_light_01';
  const selectedOnClick = (from, name) => logs.findIndex((l, k) => k >= from && l.startsWith('[UIDiag] onClick ' + name + ' ')) >= 0;

  await page.goto(`${BASE}/index.html?backend=${encodeURIComponent(BASE)}&anon=${encodeURIComponent(ANON)}&autologin=${encodeURIComponent(user.email + '|' + user.password)}`);
  if ((await waitFrom(/\[Dashboard\] built for ProbePat/, 0, 150000)) < 0) { say('PROBE: the dashboard never opened; nothing was measured'); await browser.close(); return; }
  await sleep(4000); let m = logs.length; await click('Nav_Garage', 'Nav_Garage');
  if ((await waitFrom(/\[Garage\] vehicles state=ready count=2 /, m, 90000)) < 0 || (await waitFrom(/\[Showroom\] ready vehicle=truck_light_01/, m, 120000)) < 0) { say('PROBE: the Garage did not load; nothing was measured'); await browser.close(); return; }
  await settleQuiet(); m = logs.length; const hook = await click('Btn_GarTab_Maintenance'); await waitFrom(/\[Garage\] quote state=ready repair=300/, m, 60000); await sleep(4000);
  say(`HOOK CHECK: the Maintenance tab click ${hook.onClick ? 'logged onClick, so the onClick diagnostics work in this build' : 'did NOT log onClick, so the onClick diagnostics may not be working (press seen: ' + !!hook.press + ')'}`);

  // control: a click in a quiet period must work
  m = logs.length; const cr = await click(BUS, 'control: bus card');
  const ctrlSel = await waitFrom(/\[Garage\] select vehicle=\S+ definition=bus_city_01/, m, 15000);
  const ctrl = ctrlSel >= 0 && !!cr.onClick;
  say(`CONTROL (quiet click on the bus card): ${ctrl ? 'selected, onClick logged' : `NOT selected (target published=${cr.found}, press seen=${!!cr.press}, onClick=${!!cr.onClick}, selection logged=${ctrlSel >= 0})`}`);
  if (!ctrl) { say('PROBE: the control click failed, so the probe cannot separate causes; stopping'); await page.screenshot({ path: `${prefix}-control-failed.png` }).catch(() => {}); await browser.close(); return; }
  await sleep(2000); m = logs.length; await click(TRUCK, 'back to the truck'); await waitFrom(/\[Garage\] select vehicle=\S+ definition=truck_light_01/, m, 30000); await waitFrom(/\[Garage\] quote state=ready/, m, 30000); await sleep(4000);

  const trials = [];
  for (const action of [{ btn: 'Btn_GarRepair', ok: /\[Garage\] repair ok=True/ }, { btn: 'Btn_GarRefuel', ok: /\[Garage\] refuel ok=True/ }]) {
    await settleQuiet();
    const busPos = await at(BUS), actPos = await at(action.btn);
    if (!busPos || !actPos) { trials.push({ action: action.btn, result: 'no target position' }); continue; }
    const start = logs.length;
    const ar = await click(action.btn, 'service action ' + action.btn, true);
    if (!ar.found) { trials.push({ action: action.btn, result: 'no target position published for the service button' }); continue; }
    await page.mouse.move(busPos.x, busPos.y);                                                          // go to the bus card while the server works
    const okIdx = await waitFrom(action.ok, start, 30000);                                              // polls every 10 ms
    if (okIdx < 0) { trials.push({ action: action.btn, result: 'the service action never finished' }); continue; }
    await page.mouse.down();                                                                            // press as early as possible after the server answered
    const pressIdx = await waitFrom(/\[UIDiag\] pointer pressed/, okIdx, 5000);
    const rebuildIdx = await waitFrom(/\[Garage\] quote state=ready/, okIdx, 15000);                    // logged in the same continuation that rebuilds the list
    await sleep(700);                                                                                   // hold across the rebuild
    await page.mouse.up();
    const relIdx = await waitFrom(/\[UIDiag\] pointer released/, Math.max(pressIdx, 0), 5000);
    await sleep(3000);
    const fired = logs.findIndex((l, k) => k > okIdx && l.startsWith('[UIDiag] onClick ' + BUS + ' ')) >= 0;
    const selected = logs.findIndex((l, k) => k > okIdx && /\[Garage\] select vehicle=\S+ definition=bus_city_01/.test(l)) >= 0;
    const pressedBefore = pressIdx >= 0 && rebuildIdx >= 0 && pressIdx < rebuildIdx;                    // the game saw the press before the rebuild
    const releasedAfter = relIdx >= 0 && rebuildIdx >= 0 && relIdx > rebuildIdx;
    trials.push({ action: action.btn, pressedBefore, releasedAfter, fired, selected, order: { ok: okIdx, press: pressIdx, rebuild: rebuildIdx, release: relIdx } });
    await page.mouse.move(5, 5);
    if (selected) { await sleep(2000); m = logs.length; await click(TRUCK, 'back to the truck'); await waitFrom(/\[Garage\] select vehicle=\S+ definition=truck_light_01/, m, 30000); await waitFrom(/\[Garage\] quote state=ready/, m, 30000); await sleep(3000); }
  }
  await page.screenshot({ path: `${prefix}-end.png` }).catch(() => {});
  await browser.close();

  for (const t of trials) say(`TRIAL ${t.action}: ` + (t.result ? t.result : `press before the rebuild=${t.pressedBefore}, release after it=${t.releasedAfter} -> button invoked=${t.fired}, vehicle selected=${t.selected}  (log order ${JSON.stringify(t.order)})`));
  const inWindow = trials.filter(t => t.pressedBefore && t.releasedAfter);
  if (!inWindow.length) say('VERDICT: INCONCLUSIVE - in no trial did the press arrive before the rebuild and the release after it, so the question was not tested (the control worked).');
  else if (inWindow.every(t => !t.fired && !t.selected)) say(`VERDICT: CONFIRMED - in ${inWindow.length} of ${trials.length} trial(s) a press before the rebuild and a release after it was LOST (no onClick, no selection), while the control click in a quiet period worked. A rebuild between press and release drops the click.`);
  else if (inWindow.every(t => t.fired || t.selected)) say('VERDICT: REFUTED for this mechanism - the click was delivered even though the list was rebuilt between press and release. Another cause must be found.');
  else say('VERDICT: MIXED - some press/rebuild/release trials lost the click and some did not; see the trial lines.');
})().catch(e => { console.log('PROBE ERROR: ' + String(e).slice(0, 300)); process.exit(0); });
