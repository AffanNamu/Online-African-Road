// Refuel at a pump (P1 of docs/FUNCTIONAL_PLACES_PROPOSAL.md) in the real WebGL build, with NO backend (the public build and the review build bake none in).
// Usage: node tools/webgl-pump.js <build-dir> <out-prefix> [route ...]          (default routes: ng-kano-city-e1 ng-lagos-ibadan)
// For each route the game opens with ?smoke=drive&pump=1: the truck starts beside the first pump along the road. What must happen, read from the game's own log:
//   * the truck lands on real ground beside a station's pump (not on the carriageway) and the game says it is AT the pump;
//   * the HUD shows the Refuel button (its position is logged);
//   * F asks for fuel, and with no server the game says it is OFFLINE and fills NOTHING (docs/DECISIONS.md: no fake backend): the fuel reading is the same before and after.
// A real purchase (litres, price, the hose and the meter) is checked against a real backend in tools/webgl-e2e.js ("refuel at a pump").
// Output: <prefix>-<route>.png (the truck at the pump, chase view) and a PASS / FAIL list.
const http = require('http'), fs = require('fs'), path = require('path');

const dir = path.resolve(process.argv[2] || 'Builds/WebGL'), prefix = process.argv[3] || 'webgl-pump';
const routes = process.argv.length > 4 ? process.argv.slice(4) : ['ng-kano-city-e1', 'ng-lagos-ibadan'];
const MIME = { '.html': 'text/html', '.js': 'text/javascript', '.css': 'text/css', '.wasm': 'application/wasm', '.json': 'application/json', '.png': 'image/png', '.ico': 'image/x-icon', '.gz': 'application/octet-stream', '.data': 'application/octet-stream' };
function serve() {
  return new Promise(resolve => {
    const s = http.createServer((req, res) => {
      let p = decodeURIComponent(req.url.split('?')[0]); if (p.endsWith('/')) p += 'index.html'; const f = path.join(dir, p);
      if (!f.startsWith(dir) || !fs.existsSync(f) || fs.statSync(f).isDirectory()) { res.writeHead(404); res.end('not found'); return; }
      res.writeHead(200, { 'Content-Type': MIME[path.extname(f)] || 'application/octet-stream' }); fs.createReadStream(f).pipe(res);
    }).listen(0, '127.0.0.1', () => resolve(s));
  });
}
const results = [];
function check(name, ok, detail) { results.push({ name, ok: !!ok, detail }); console.log(`  ${ok ? 'PASS' : 'FAIL'}  ${name}${detail ? ' (' + detail + ')' : ''}`); }
const fuelOf = line => { const m = /fuel=([\d.]+)/.exec(line || ''); return m ? +m[1] : NaN; };

(async () => {
  const { chromium } = require('playwright');
  const server = await serve(), port = server.address().port;
  const browser = await chromium.launch({ args: ['--use-gl=angle', '--use-angle=swiftshader', '--enable-unsafe-swiftshader', '--ignore-gpu-blocklist', '--no-sandbox'] });
  for (const route of routes) {
    const page = await browser.newPage({ viewport: { width: 1024, height: 576 } });
    const logs = [], errors = []; page.on('console', m => logs.push(m.text())); page.on('pageerror', e => errors.push(String(e)));
    const has = (re, from = 0) => logs.slice(from).some(l => re.test(l)); const find = (re, from = 0) => logs.slice(from).find(l => re.test(l));
    const waitFor = async (re, secs, from = 0) => { for (let i = 0; i < secs * 4; i++) { if (has(re, from)) return true; await page.waitForTimeout(250); } return false; };
    console.log(`\n=== ${route}: the first pump along the road ===`);
    const q = route === 'ng-lagos-ibadan' ? '' : `&route=${route}`;          // the shipped route is the default one
    await page.goto(`http://127.0.0.1:${port}/index.html?smoke=drive&pump=1&quality=low&hour=noon${q}`);
    const started = await waitFor(/\[Smoke\] drive scenario/, 150);
    const beside = started && await waitFor(/\[Smoke\] starting beside /, 60), ground = beside && await waitFor(/\[Drive\] ground ready/, 120);
    check(`${route}: the truck starts beside a pump`, beside, (find(/\[Smoke\] starting beside /) || 'no "starting beside" line').slice(0, 160));
    check(`${route}: it lands on real ground`, ground, ground ? (find(/\[Drive\] ground ready/) || '').slice(0, 120) : 'no ground line');
    const atPump = ground && await waitFor(/\[Pump\] at /, 40);
    check(`${route}: the game says the truck is at the pump`, atPump, (find(/\[Pump\] at /) || 'no "[Pump] at" line').slice(0, 160));
    const button = atPump && await waitFor(/\[Hud\] refuel button x=/, 20);
    check(`${route}: the HUD shows the Refuel button`, button, (find(/\[Hud\] refuel button/) || '').slice(0, 120));
    if (atPump) {
      await page.waitForTimeout(2500); await page.mouse.click(512, 288); await page.waitForTimeout(400);
      fs.writeFileSync(`${prefix}-${route}.png`, await page.screenshot());
      const before = fuelOf(logs.filter(l => /\[Drive\] t=/.test(l)).pop()), n = logs.length;
      let offline = false; for (let i = 0; i < 3 && !offline; i++) { await page.keyboard.press('f', { delay: 200 }); offline = await waitFor(/\[Pump\] offline/, 6, n); }
      check(`${route}: with no server, F gives the offline notice`, offline, (find(/\[Pump\] (offline|buying|paid)/, n) || 'nothing after F').slice(0, 160));
      check(`${route}: nothing is sold without a server`, !has(/\[Pump\] (buying|paid|filled)/, n), '');
      await page.waitForTimeout(3000);
      const after = fuelOf(logs.filter(l => /\[Drive\] t=/.test(l)).pop());
      check(`${route}: the tank was not filled`, Number.isFinite(before) && Number.isFinite(after) && Math.abs(after - before) < 0.05, `fuel ${before} L before, ${after} L after`);
    }
    check(`${route}: the vehicle did not leave the world`, !has(/left the world/), '');
    const bad = logs.filter(l => /exception|NullReference|IndexOutOfRange|invalid route data|could not build/i.test(l) && !/GLib|swiftshader/i.test(l));
    check(`${route}: no exception`, bad.length === 0 && errors.length === 0, bad[0] ? bad[0].slice(0, 200) : errors[0] ? errors[0].slice(0, 200) : '');
    await page.close();
  }
  await browser.close(); server.close();
  const failed = results.filter(r => !r.ok);
  console.log(`\n${results.length - failed.length} of ${results.length} checks passed.`);
  if (failed.length) { console.log('PUMP CHECKS FAILED:'); failed.forEach(f => console.log(' - ' + f.name + (f.detail ? ' (' + f.detail + ')' : ''))); process.exit(1); }
})();
