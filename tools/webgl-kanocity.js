// The Kano city densification routes (ng-kano-city-e1, ng-kano-city-f1) in the REAL WebGL build: the frontage engine's buildings and mosques along driven roads, seen from the cab and from above.
// Usage: node tools/webgl-kanocity.js <build-dir> <out-prefix> [quality]        (self-test of the frame analysis: --selftest)
// It belongs to the review run (the build carries the production prefabs of the private kit): each place is opened with ?smoke=drive&hold=1&route=... with the truck frozen and gives a cab frame (key 1) and a second frame
// (key 4 far chase or key 7 overhead). Output: <prefix>-sheet.png (all frames), <prefix>-report.json with the district, road and camera of every frame, the game's own [Perf] line, and a PASS / FAIL list.
// The checks are about the running game (the route loaded, the truck stood on real ground, a rendered scene, no exception). Whether the city LOOKS right is for a person to judge from the sheet; nothing here approves anything.
const http = require('http'), fs = require('fs'), path = require('path');

const luma = (r, g, b) => 0.2126 * r + 0.7152 * g + 0.0722 * b;
function stats(png) { let s = 0, s2 = 0, n = 0; for (let i = 0; i < png.data.length; i += 4) { const l = luma(png.data[i], png.data[i + 1], png.data[i + 2]); s += l; s2 += l * l; n++; } const m = s / n; return { mean: m, sd: Math.sqrt(Math.max(0, s2 / n - m * m)) }; }
function sheet(PNG, pngs, cols, tw, th) {
  const rows = Math.ceil(pngs.length / cols), out = new PNG({ width: cols * tw, height: rows * th }); out.data.fill(24);
  pngs.forEach((p, k) => { const ox = (k % cols) * tw, oy = Math.floor(k / cols) * th; for (let y = 0; y < th; y++) for (let x = 0; x < tw; x++) { const sx = Math.min(p.width - 1, Math.floor(x * p.width / tw)), sy = Math.min(p.height - 1, Math.floor(y * p.height / th)); const si = (sy * p.width + sx) * 4, di = ((oy + y) * out.width + ox + x) * 4; out.data[di] = p.data[si]; out.data[di + 1] = p.data[si + 1]; out.data[di + 2] = p.data[si + 2]; out.data[di + 3] = 255; } });
  return out;
}
if (process.argv[2] === '--selftest') {
  const { PNG } = require('pngjs'); let bad = 0;
  const flat = new PNG({ width: 8, height: 8 }); flat.data.fill(100); const s1 = stats(flat); if (Math.abs(s1.mean - 100) > 0.5 || s1.sd > 0.01) { console.log('selftest FAIL flat'); bad++; }
  const sh = sheet(PNG, [flat, flat, flat], 2, 4, 4); if (sh.width !== 8 || sh.height !== 8) { console.log('selftest FAIL sheet size'); bad++; }
  console.log(bad ? 'SELFTEST FAILED' : 'SELFTEST PASSED'); process.exit(bad ? 1 : 0);
}

const dir = path.resolve(process.argv[2] || 'Builds/WebGL'), prefix = process.argv[3] || 'webgl-kanocity', quality = process.argv[4] || 'low';
const MIME = { '.html': 'text/html', '.js': 'text/javascript', '.css': 'text/css', '.wasm': 'application/wasm', '.json': 'application/json', '.png': 'image/png', '.ico': 'image/x-icon', '.gz': 'application/octet-stream', '.data': 'application/octet-stream', '.br': 'application/octet-stream', '.txt': 'text/plain' };
function serve() {
  return new Promise(resolve => {
    const s = http.createServer((req, res) => {
      let p = decodeURIComponent(req.url.split('?')[0]); if (p.endsWith('/')) p += 'index.html'; const f = path.join(dir, p);
      if (!f.startsWith(dir) || !fs.existsSync(f) || fs.statSync(f).isDirectory()) { res.writeHead(404); res.end('not found'); return; }
      const enc = f.endsWith('.gz') ? 'gzip' : f.endsWith('.br') ? 'br' : null; const base = enc ? f.slice(0, -3) : f;
      const h = { 'Content-Type': MIME[path.extname(base)] || 'application/octet-stream' }; if (enc) h['Content-Encoding'] = enc; res.writeHead(200, h); fs.createReadStream(f).pipe(res);
    }).listen(0, '127.0.0.1', () => resolve(s));
  });
}
const results = [];
function check(name, ok, detail) { results.push({ name, ok: !!ok, detail }); console.log(`  ${ok ? 'PASS' : 'FAIL'}  ${name}${detail ? ' (' + detail + ')' : ''}`); }

// Positions come from the lots the frontage engine computes (tests/NetCore.Tests/FrontageTests.cs prints them; the plan view in docs/KANO_CITY_FRONTAGE.md lists the mosques).
// `route`: the test route; `corridor`: `start` is measured along it (default the main road); `back`: face the other way. Cameras: 1 cockpit (the driver's eye), 3 rear chase, 4 far chase, 7 overhead.
// Every frame carries the district, the road and the camera so a picture can be read without the code. Districts: the city routes are ENGINE TEST routes, not a district of the Kano master plan; the closest
// planning class is named (D02 palace-and-mosque precinct is real-anchored and NOT modelled; no real place is claimed).
const E1 = 'ng-kano-city-e1', F1 = 'ng-kano-city-f1';
const PLACES = [
  { id: 'single-carriageway-mosque', route: E1, start: 725, hour: 'noon', second: '4', district: 'test corridor, collector section (a commercial-residential district class; no real district)', road: 'kano_collector: an ordinary single carriageway with kerbs and walkways, two-way', what: 'an ordinary single-carriageway road with a mosque on the right 60 m ahead, shops and houses either side' },
  { id: 'commercial-road', route: E1, start: 985, hour: 'noon', second: '4', district: 'test corridor, collector section (commercial zone)', road: 'kano_collector, shop-house frontage', what: 'a commercial road: shop rows, a shopping parade, market sheds and a mosque on the left 50 m ahead' },
  { id: 'residential-street', route: E1, corridor: 'j_local', start: 28, hour: 'noon', second: '4', district: 'test corridor, local street (a residential neighbourhood class; no real district)', road: 'kano_local: a narrow two-way residential street', what: 'a residential neighbourhood: villas, Hausa-style compounds and houses, a small mosque on the left 45 m ahead' },
  { id: 'arterial-grand-mosque', route: E1, start: 235, hour: 'noon', second: '4', district: 'test corridor, arterial section (a major-road district class; no real district)', road: 'kano_arterial: divided road, two lanes each way and a planter median', what: 'a major arterial: mid-rise blocks, a shopping parade, a grand mosque on the left 80 m ahead set back 10 to 15 m' },
  { id: 'urban-density-arterial', route: E1, start: 380, hour: 'noon', second: '7', district: 'test corridor, arterial and cross street', road: 'kano_arterial at the four-way crossing', what: 'a wider view of the urban density around the crossing: two rows of buildings each side of the arterial, mosques, a cross street', first: '4' },
  { id: 'flyover-north-driver', route: F1, start: 700, hour: 'noon', second: '4', district: 'test interchange F1, expressway deck over the ground roundabout', road: 'expressway deck, direction of increasing distance (north), driver in the right lane', what: 'the flyover from the cab going north: a populated city on both sides, a mosque alongside the deck on the left', deck: true },
  { id: 'flyover-south-driver', route: F1, start: 1150, back: true, hour: 'noon', second: '4', district: 'test interchange F1, expressway deck over the ground roundabout', road: 'expressway deck, the other direction (south), driver in the right lane', what: 'the flyover from the cab going south: the same city seen from the other direction, the mosque now on the right', deck: true },
  { id: 'flyover-wide-density', route: F1, start: 900, hour: 'noon', second: '7', district: 'test interchange F1, ground roundabout under the deck', road: 'expressway deck over the roundabout', what: 'a wider view of the interchange and the city rows beside it', first: '4' },
];

(async () => {
  const { chromium } = require('playwright'); const { PNG } = require('pngjs');
  const server = await serve(), port = server.address().port;
  const browser = await chromium.launch({ args: ['--use-gl=angle', '--use-angle=swiftshader', '--enable-unsafe-swiftshader', '--ignore-gpu-blocklist', '--no-sandbox'] });
  const frames = [], report = { quality, note: 'engine test routes; frames are for a person to judge; nothing here is an approval', places: [] };
  for (const pl of PLACES) {
    const page = await browser.newPage({ viewport: { width: 1024, height: 576 } });
    const logs = [], errors = []; page.on('console', m => logs.push(m.text())); page.on('pageerror', e => errors.push(String(e)));
    const has = re => logs.some(l => re.test(l)); const waitFor = async (re, secs) => { for (let i = 0; i < secs * 4; i++) { if (has(re)) return true; await page.waitForTimeout(250); } return false; };
    console.log(`\n=== ${pl.id}: ${pl.what} (${pl.route}${pl.corridor ? ' ' + pl.corridor : ''} start=${pl.start} m${pl.back ? ' facing back' : ''}, ${quality}) ===`);
    await page.goto(`http://127.0.0.1:${port}/index.html?smoke=drive&hold=1&quality=${quality}&route=${pl.route}${pl.corridor ? '&corridor=' + pl.corridor : ''}&start=${pl.start}${pl.back ? '&facing=back' : ''}&hour=${pl.hour}`);
    const started = await waitFor(/\[Smoke\] drive scenario/, 150), ground = started && await waitFor(/\[Drive\] ground ready/, 120), held = ground && await waitFor(/\[Drive\] smoke hold: vehicle frozen/, 100);
    check(`${pl.id}: the city route loaded`, started && has(new RegExp('\\[Smoke\\] driving the test corridor ' + pl.route)) && !has(/invalid route spec/i), started ? (logs.find(l => /^\[Route\] /.test(l)) || '').slice(0, 140) : 'no drive scenario');
    check(`${pl.id}: the truck stood on real ground and was held`, held, held ? (logs.filter(l => /\[Drive\] t=/.test(l)).pop() || '').slice(0, 160) : 'no ground / hold line');
    const driveLine = held ? (logs.filter(l => /\[Drive\] t=/.test(l)).pop() || '') : '', py = (/pos=\([-\d.]+,([-\d.]+),/.exec(driveLine) || [])[1];
    if (pl.deck) {
      const driveLines = logs.filter(l => /^\[Drive\]/.test(l)), ys = driveLines.filter(l => /\[Drive\] t=/.test(l)).map(l => (/pos=\([-\d.]+,([-\d.]+),/.exec(l) || [])[1]);
      console.log('  [diagnostic] ' + pl.id + ' [Drive] lines: ' + driveLines.filter(l => !/\[Drive\] t=/.test(l)).slice(0, 8).map(l => l.slice(0, 150)).join(' | ') + ' ; y of every [Drive] t= line: ' + ys.join(', '));
      check(`${pl.id}: the truck stands on the flyover deck (y of 17.3 m or more; the deck surface is at 17.8 m, the ground at 10 m)`, held && parseFloat(py) >= 17.3, `y=${py}`);
    }
    const entry = { id: pl.id, route: pl.route, corridor: pl.corridor || 'main', startMetres: pl.start, facing: pl.back ? 'back' : 'forward', district: pl.district, road: pl.road, what: pl.what, frames: [] };
    if (held) {
      await page.waitForTimeout(6000); await page.mouse.click(512, 288); await page.waitForTimeout(500);
      const shoot = async (key, label) => {
        for (let i = 0; i < 3; i++) { const n = logs.length; await page.keyboard.press(key, { delay: 350 }); await page.waitForTimeout(800); if (logs.slice(n).some(l => /\[Camera\] view=/.test(l)) || i === 2) break; }
        await page.waitForTimeout(3500); const png = PNG.sync.read(await page.screenshot()); const st = stats(png); frames.push(png);
        entry.frames.push({ camera: label, key, mean: Math.round(st.mean), sd: Math.round(st.sd), cameraLog: (logs.filter(l => /\[Camera\] view=/.test(l)).pop() || '').slice(0, 80) });
        return st;
      };
      const a = await shoot(pl.first || '1', pl.first === '4' ? 'far chase (wide)' : 'cockpit (the driver\'s eye)');
      const b = await shoot(pl.second, pl.second === '7' ? 'overhead' : 'far chase');
      check(`${pl.id}: the frames show a rendered scene`, a.sd > 12 && b.sd > 12, `frame 1 mean ${a.mean.toFixed(0)} sd ${a.sd.toFixed(0)}, frame 2 mean ${b.mean.toFixed(0)} sd ${b.sd.toFixed(0)}`);
    }
    entry.perf = logs.filter(l => l.startsWith('[Perf]') && !l.includes('BUDGET')).slice(-1)[0] || null; entry.budgetWarnings = logs.filter(l => l.includes('[Perf] BUDGET EXCEEDED')).slice(-3);
    check(`${pl.id}: nothing threw`, errors.length === 0 && !has(/Exception|\[Error\]/), errors.concat(logs.filter(l => /Exception/.test(l))).slice(0, 2).join(' | ').slice(0, 200));
    console.log(`  [numbers] ${pl.id}: ${(entry.perf || 'no [Perf] line').slice(0, 260)}`);
    report.places.push(entry); await page.close();
  }
  await browser.close(); server.close();
  if (frames.length) fs.writeFileSync(prefix + '-sheet.png', PNG.sync.write(sheet(PNG, frames, 4, 512, 288)));
  report.checks = results; fs.writeFileSync(prefix + '-report.json', JSON.stringify(report, null, 1));
  const failed = results.filter(r => !r.ok); console.log(`\n${results.length - failed.length} of ${results.length} checks passed`); process.exit(failed.length ? 1 : 0);
})().catch(e => { console.error(e); process.exit(2); });
