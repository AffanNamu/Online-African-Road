// World-map scenario for the WebGL build. Usage: node tools/webgl-worldmap.js <build-dir> [screenshot-prefix]
// Opens index.html?smoke=worldmap (fixture player, level 5, no sign-in) and drives the real UI with the mouse and keyboard:
// artwork loads, aspect ratio is kept on several screen shapes, wheel zoom, drag pan, city + route selection, planned routes are never playable,
// then Back / nav / Escape. The game logs every state it reaches ([WorldMap] ...); this script reads those lines.
const http = require('http'), fs = require('fs'), path = require('path');
const { chromium } = require('playwright');

const dir = path.resolve(process.argv[2] || 'Builds/WebGL');
const prefix = process.argv[3] || 'webgl-worldmap';
const MIME = { '.html': 'text/html', '.js': 'text/javascript', '.css': 'text/css', '.wasm': 'application/wasm', '.json': 'application/json', '.png': 'image/png', '.jpg': 'image/jpeg', '.ico': 'image/x-icon', '.gz': 'application/octet-stream', '.data': 'application/octet-stream' };
const server = http.createServer((req, res) => {
  let p = decodeURIComponent(req.url.split('?')[0]); if (p.endsWith('/')) p += 'index.html';
  const f = path.join(dir, p);
  if (!f.startsWith(dir) || !fs.existsSync(f) || fs.statSync(f).isDirectory()) { res.writeHead(404); res.end('not found'); return; }
  res.writeHead(200, { 'Content-Type': MIME[path.extname(f)] || 'application/octet-stream' }); fs.createReadStream(f).pipe(res);
});
const ASPECT = 1672 / 941;

(async () => {
  const failures = [], passes = [];
  const check = (ok, what) => { (ok ? passes : failures).push(what); console.log(`${ok ? 'PASS' : 'FAIL'}  ${what}`); };
  await new Promise(r => server.listen(0, '127.0.0.1', r)); const port = server.address().port;
  const browser = await chromium.launch({ args: ['--use-gl=angle', '--use-angle=swiftshader', '--enable-unsafe-swiftshader', '--ignore-gpu-blocklist', '--no-sandbox'] });
  const page = await browser.newPage({ viewport: { width: 1024, height: 576 } });
  const logs = [], errors = [];
  page.on('console', m => logs.push(m.text())); page.on('pageerror', e => errors.push(String(e)));
  await page.goto(`http://127.0.0.1:${port}/index.html?smoke=worldmap`);

  const sleep = ms => page.waitForTimeout(ms);
  async function waitLog(re, from, seconds = 40) { for (let i = 0; i < seconds * 2; i++) { const hit = logs.slice(from).find(l => re.test(l)); if (hit) return hit; await sleep(500); } return null; }
  const mark = () => logs.length;
  const lastState = () => { const l = [...logs].reverse().find(x => x.startsWith('[WorldMap] state')); if (!l) return null; const g = k => (new RegExp(k + '=([^ ]+)').exec(l) || [])[1]; const pan = (g('pan') || '0,0').split(',').map(Number);
    return { raw: l, screen: g('screen'), layout: g('layout'), viewport: g('viewport'), art: g('artworkOnScreen'), aspect: parseFloat(g('aspect')), zoom: parseFloat(g('zoom')), pan }; };
  const targets = () => { const l = [...logs].reverse().find(x => x.startsWith('[WorldMap] targets')) || ''; return Object.fromEntries([...l.matchAll(/TARGET (\S+) x=([\d.]+) y=([\d.]+)/g)].map(m => [m[1], { x: +m[2], y: +m[3] }])); };
  async function box() { return page.locator('#unity-canvas').boundingBox(); }
  async function at(t) { const b = await box(); return [b.x + t.x * b.width, b.y + t.y * b.height]; }
  async function click(name) { const t = targets()[name]; if (!t) return false; const [x, y] = await at(t); await page.mouse.move(x, y); await sleep(250); await page.mouse.down(); await sleep(150); await page.mouse.up(); return true; }
  async function settle(sec = 3) { await sleep(sec * 1000); }

  // ---- 1. loads
  const opened = await waitLog(/\[WorldMap\] opened/, 0, 150);
  check(!!opened, 'the world map opened from the fixture scenario');
  if (!opened) { await finish(); return; }
  check(/artwork=1672x944 content=1672x941/.test(opened), `artwork loaded at its real size (${(/artwork=\S+ content=\S+/.exec(opened) || [''])[0]})`);
  check(/cities=23 routes=12/.test(opened), 'validated data: 23 cities, 12 routes');
  await sleep(5000); await page.screenshot({ path: `${prefix}-0-landscape.png` });
  let s = lastState();
  check(!!s && Math.abs(s.aspect - ASPECT) < 0.004, `aspect ratio preserved on screen: ${s ? s.aspect : 'n/a'} (artwork ${ASPECT.toFixed(4)}), artwork ${s && s.art}`);
  check(!!s && s.layout === 'landscape' && s.zoom === 1, 'starts in landscape layout at zoom 1');
  check(Object.keys(targets()).filter(k => k.startsWith('City_')).length === 23, `all 23 city markers are clickable (${Object.keys(targets()).filter(k => k.startsWith('City_')).length})`);

  // ---- 2. different screen shapes never stretch the map
  for (const [w, h, shape] of [[600, 900, 'portrait'], [1400, 500, 'landscape'], [1024, 576, 'landscape']]) {
    const m = mark(); await page.setViewportSize({ width: w, height: h }); await waitLog(new RegExp(`state screen=${w}x${h}`), m, 30); await sleep(1500);
    s = lastState(); await page.screenshot({ path: `${prefix}-1-${w}x${h}.png` });
    check(!!s && s.screen === `${w}x${h}` && Math.abs(s.aspect - ASPECT) < 0.004 && s.layout === shape, `${w}x${h}: ${shape} layout, artwork aspect ${s ? s.aspect : 'n/a'} (not stretched)`);
  }

  // ---- 3. zoom + pan
  const b0 = await box(); const cx = b0.x + b0.width * 0.35, cy = b0.y + b0.height * 0.5;
  let m = mark(); await page.mouse.move(cx, cy); await sleep(300);
  for (let i = 0; i < 4; i++) { await page.mouse.wheel(0, -120); await sleep(400); }
  await waitLog(/state .* zoom=[2-9]/, m, 20); s = lastState(); await page.screenshot({ path: `${prefix}-2-zoomed.png` });
  check(!!s && s.zoom > 1.5, `mouse wheel zooms in (zoom ${s && s.zoom})`);
  check(!!s && Math.abs(s.aspect - ASPECT) < 0.004, `aspect ratio still preserved when zoomed (${s && s.aspect})`);
  const panBefore = s ? s.pan.slice() : [0, 0]; m = mark();
  await page.mouse.move(cx, cy); await page.mouse.down(); for (let i = 1; i <= 8; i++) { await page.mouse.move(cx - i * 25, cy - i * 12); await sleep(120); } await page.mouse.up();
  await waitLog(/state /, m, 20); await sleep(1000); s = lastState();
  check(!!s && (Math.abs(s.pan[0] - panBefore[0]) > 20 || Math.abs(s.pan[1] - panBefore[1]) > 10), `dragging pans the map (pan ${panBefore} -> ${s && s.pan})`);
  m = mark(); await click('Btn_ZoomFit'); await waitLog(/state .* zoom=1\.00/, m, 20); s = lastState();
  check(!!s && s.zoom === 1 && Math.abs(s.pan[0]) < 1 && Math.abs(s.pan[1]) < 1, 'the Fit button restores zoom 1 and centres the map');
  m = mark(); await click('Btn_ZoomIn'); await waitLog(/state .* zoom=1\.[5-9]|zoom=2/, m, 20); s = lastState();
  check(!!s && s.zoom >= 1.4, `the + button zooms in (zoom ${s && s.zoom})`);
  m = mark(); await click('Btn_ZoomFit'); await waitLog(/state .* zoom=1\.00/, m, 20); await sleep(1500);

  // ---- 4. cities
  const cityTests = [['lagos', 'Open'], ['abuja', 'Planned'], ['kano', 'Locked']];
  for (const [id, state] of cityTests) {
    m = mark(); const clicked = await click('City_' + id); const hit = await waitLog(new RegExp(`selected city=${id} state=${state}`), m, 25);
    check(clicked && !!hit, `city ${id} is selectable and reported as ${state}`); await sleep(1500);
    if (id === 'lagos') await page.screenshot({ path: `${prefix}-3-city-lagos.png` });
  }

  // ---- 5. routes: the prototype is drivable, planned routes never are
  m = mark(); await click('City_lagos'); await waitLog(/selected city=lagos/, m, 20); await sleep(1500);
  m = mark(); await click('Route_lagos-ibadan'); let hit = await waitLog(/selected route=lagos-ibadan state=Prototype straightKm=114 roadKm=128/, m, 25);
  check(!!hit, `route Lagos -> Ibadan: prototype, 114 km straight line, 128 km by road (${hit ? 'logged' : 'not seen'})`);
  await sleep(1800); await page.screenshot({ path: `${prefix}-4-route-lagos-ibadan.png` });
  let tg = targets();
  check(['Btn_ViewJobs', 'Btn_BusRoutes', 'Btn_FreeDrive'].every(k => tg[k]), 'the prototype route offers jobs, bus routes and free drive');
  check(Object.keys(tg).filter(k => k.startsWith('Btn_Accept_')).length >= 3, `open jobs from the board are listed with Accept buttons (${Object.keys(tg).filter(k => k.startsWith('Btn_Accept_')).length})`);
  m = mark(); await click('Btn_PanelBack'); await waitLog(/panel mode=Overview/, m, 20); await sleep(1200);
  m = mark(); await click('City_abuja'); await waitLog(/selected city=abuja/, m, 20); await sleep(1200);
  m = mark(); await click('Route_lagos-abuja'); hit = await waitLog(/selected route=lagos-abuja state=PlannedAvailable/, m, 25);
  check(!!hit, 'route Lagos -> Abuja at level 5: discovered but planned'); await sleep(1800); await page.screenshot({ path: `${prefix}-5-route-planned.png` }); tg = targets();
  check(!tg['Btn_NotAvailable'] && !tg['Btn_ViewJobs'] && !tg['Btn_FreeDrive'] && !Object.keys(tg).some(k => k.startsWith('Btn_Accept_')), 'a planned route offers no way to drive it (no jobs, no free drive, no accept)');
  m = mark(); await click('Btn_PanelBack'); await waitLog(/panel mode=Overview/, m, 20); await sleep(1200);
  m = mark(); await click('City_kano'); await waitLog(/selected city=kano/, m, 20); await sleep(1200);
  m = mark(); await click('Route_kaduna-kano'); hit = await waitLog(/selected route=kaduna-kano state=PlannedLocked/, m, 25);
  check(!!hit, 'route Kaduna -> Kano at level 5 is locked (needs level 9)');

  // ---- 6. leave and re-enter: Back button, dashboard entry, Escape key
  m = mark(); await click('Btn_Back'); const closed = await waitLog(/\[WorldMap\] closed/, m, 20); const dash = await waitLog(/\[Dashboard\] built/, m, 40);
  check(!!closed && !!dash, 'Back returns to the dashboard');
  await sleep(4000); await page.screenshot({ path: `${prefix}-6-dashboard.png` });
  m = mark(); const entered = await click('Nav_Map'); const reopened = await waitLog(/\[WorldMap\] opened/, m, 40);
  check(entered && !!reopened, 'the dashboard Map item opens the world map');
  await sleep(2500); m = mark(); await page.keyboard.press('Escape'); const esc = await waitLog(/\[WorldMap\] closed/, m, 20);
  check(!!esc, 'the Escape key closes the world map');

  await finish();
  async function finish() {
    const exc = logs.filter(l => /exception|NullReference|IndexOutOfRange|error CS/i.test(l) && !/GLib|swiftshader/i.test(l));
    check(errors.length === 0, `no page errors (${errors.length})`); check(exc.length === 0, `no exceptions in the Unity log (${exc.length})`);
    exc.slice(0, 5).forEach(l => console.log('EXC: ' + l.slice(0, 400))); errors.slice(0, 3).forEach(e => console.log('ERR: ' + e.slice(0, 300)));
    console.log('--- WorldMap log (first 60 lines) ---'); logs.filter(l => /\[WorldMap\]|\[Smoke\]/.test(l) && !l.startsWith('[WorldMap] targets')).slice(0, 60).forEach(l => console.log(l.slice(0, 260)));
    await browser.close(); server.close();
    console.log(`\n${passes.length} passed, ${failures.length} failed`);
    if (failures.length) { console.log('WORLD MAP SCENARIO FAILED:'); failures.forEach(f => console.log(' - ' + f)); process.exit(1); }
    console.log('WORLD MAP SCENARIO PASSED');
  }
})();
