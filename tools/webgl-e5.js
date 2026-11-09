// Roundabouts and the flyover (road engine stage E5), checked in the real WebGL build: an autopilot drives the REAL vehicle (colliders, wheel physics) along a path of an E5 test route and the
// log says whether it got to the end, how far it strayed from the lane, and how high it was (on the deck, or on the ground under it).
// Usage: node tools/webgl-e5.js <build-dir> <out-prefix> [quality]        (self-test of the log parsing: --selftest)
// Each scenario is one page load: ?smoke=drive&route=...&autopilot=<path>&vehicle=<id>. Output: <prefix>-sheet.png (frames taken while driving), <prefix>-report.json and a PASS / FAIL list.
// The checks are about the running game: the route loaded, the vehicle stood on real ground, reached the end of the path, stayed within 3 m of the lane, and was on the right level.
// Whether the roundabouts and the flyover LOOK right is for a person to judge from the sheet, and driving them with the keyboard is the owner's test.
const http = require('http'), fs = require('fs'), path = require('path');

// "[Autopilot] done path=lower reached=true maxErr=0.84 minY=9.97 maxY=10.31 seconds=41.2 end=(10.0,10.2,-50.0)"  ->  { ok, path, reached, maxErr, minY, maxY, seconds } ; FAILED lines give ok=false and the reason
function parseDone(line) {
  if (!line) return null;
  const m = /\[Autopilot\] (done|FAILED .*?) path=(\S+) reached=(true|false) maxErr=([\d.]+) minY=(-?[\d.]+) maxY=(-?[\d.]+) seconds=([\d.]+)/.exec(line);
  if (!m) return null;
  return { ok: m[1] === 'done', why: m[1].trim(), path: m[2], reached: m[3] === 'true', maxErr: +m[4], minY: +m[5], maxY: +m[6], seconds: +m[7] };
}
const luma = (r, g, b) => 0.2126 * r + 0.7152 * g + 0.0722 * b;
function stats(png) { let s = 0, s2 = 0, n = 0; for (let i = 0; i < png.data.length; i += 4) { const l = luma(png.data[i], png.data[i + 1], png.data[i + 2]); s += l; s2 += l * l; n++; } const m = s / n; return { mean: m, sd: Math.sqrt(Math.max(0, s2 / n - m * m)) }; }
function sheet(PNG, pngs, cols, tw, th) {
  const rows = Math.ceil(pngs.length / cols), out = new PNG({ width: cols * tw, height: rows * th }); out.data.fill(24);
  pngs.forEach((p, k) => { const ox = (k % cols) * tw, oy = Math.floor(k / cols) * th; for (let y = 0; y < th; y++) for (let x = 0; x < tw; x++) { const sx = Math.min(p.width - 1, Math.floor(x * p.width / tw)), sy = Math.min(p.height - 1, Math.floor(y * p.height / th)), si = (sy * p.width + sx) * 4, di = ((oy + y) * out.width + ox + x) * 4; out.data[di] = p.data[si]; out.data[di + 1] = p.data[si + 1]; out.data[di + 2] = p.data[si + 2]; out.data[di + 3] = 255; } });
  return out;
}
if (process.argv[2] === '--selftest') {
  let bad = 0; const t = (name, ok) => { if (!ok) { console.log('selftest FAIL ' + name); bad++; } };
  const a = parseDone('[Autopilot] done path=lower reached=true maxErr=0.84 minY=9.97 maxY=10.31 seconds=41.2 end=(10.0,10.2,-50.0)');
  t('done line', a && a.ok && a.path === 'lower' && a.reached && Math.abs(a.maxErr - 0.84) < 1e-9 && Math.abs(a.maxY - 10.31) < 1e-9 && Math.abs(a.seconds - 41.2) < 1e-9);
  const f = parseDone('[Autopilot] FAILED left the path (7.0 m off) path=upper reached=false maxErr=7.00 minY=17.1 maxY=17.9 seconds=12.0 end=(0.0,17.8,-100.0)');
  t('failed line', f && !f.ok && f.path === 'upper' && !f.reached && /left the path/.test(f.why));
  t('not a line', parseDone('[Drive] t=1.0 pos=(0,0,0)') === null && parseDone(null) === null);
  const { PNG } = require('pngjs'); const flat = new PNG({ width: 8, height: 8 }); flat.data.fill(100); const s1 = stats(flat); t('stats', Math.abs(s1.mean - 100) < 0.5 && s1.sd < 0.01);
  console.log(bad ? 'SELFTEST FAILED' : 'SELFTEST PASSED'); process.exit(bad ? 1 : 0);
}

const dir = path.resolve(process.argv[2] || 'Builds/WebGL'), prefix = process.argv[3] || 'webgl-e5', quality = process.argv[4] || 'low';
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

// level: 'deck' = the vehicle must have been at deck height (>= 17 m) and never below the road (>= 9 m); 'ground' = on the ground under the deck the whole way (9 to 11.5 m); 'any' = no height check
const SCENARIOS = [
  { id: 'f1-upper-bus', route: 'ng-kano-e5-f1', path: 'upper', vehicle: 'bus_city_01', level: 'deck', what: 'the bus drives up the embankment, over the roundabout on the deck and down the far side' },
  { id: 'f1-lower-truck', route: 'ng-kano-e5-f1', path: 'lower', vehicle: 'truck_medium_01', level: 'ground', what: 'the medium truck drives west to east through the ground roundabout, under the deck' },
  { id: 'f1-ramp-off-bus', route: 'ng-kano-e5-f1', path: 'ramp_off', vehicle: 'bus_city_01', level: 'any', what: 'the bus drives down the off ramp from the expressway to the roundabout' },
  { id: 'f1-ramp-on-truck', route: 'ng-kano-e5-f1', path: 'ramp_on', vehicle: 'truck_light_01', level: 'any', what: 'the light truck drives up the on ramp from the roundabout to the expressway' },
  { id: 'rb-landmark-bus', route: 'ng-kano-e5-roundabouts', path: 'ring:r_landmark', vehicle: 'bus_city_01', level: 'any', what: 'the bus goes round the landmark roundabout (five legs, paved civic island)' },
  { id: 'rb-large-bus', route: 'ng-kano-e5-roundabouts', path: 'ring:r_large', vehicle: 'bus_city_01', level: 'any', what: 'the bus goes round the large four-lane roundabout (six legs)' },
  { id: 'rb-traditional-truck', route: 'ng-kano-e5-roundabouts', path: 'ring:r_traditional', vehicle: 'truck_medium_01', level: 'any', what: 'the medium truck goes round the small traditional roundabout (three legs, one wide lane)' },
  { id: 'rb-green-truck', route: 'ng-kano-e5-roundabouts', path: 'ring:r_green', vehicle: 'truck_light_01', level: 'any', what: 'the light truck goes round the green roundabout (grass island, flared entries)' },
];

(async () => {
  const { chromium } = require('playwright'); const { PNG } = require('pngjs');
  const server = await serve(), port = server.address().port;
  const browser = await chromium.launch({ args: ['--use-gl=angle', '--use-angle=swiftshader', '--enable-unsafe-swiftshader', '--ignore-gpu-blocklist', '--no-sandbox'] });
  const frames = [], report = { quality, scenarios: [] };
  const only = (process.env.E5_ONLY || '').split(',').filter(Boolean);
  for (const sc of SCENARIOS) {
    if (only.length && !only.includes(sc.id)) continue;
    const page = await browser.newPage({ viewport: { width: 1024, height: 576 } });
    const logs = [], errors = []; page.on('console', m => logs.push(m.text())); page.on('pageerror', e => errors.push(String(e)));
    const has = re => logs.some(l => re.test(l)); const waitFor = async (re, secs) => { for (let i = 0; i < secs * 4; i++) { if (has(re)) return true; await page.waitForTimeout(250); } return false; };
    console.log(`\n=== ${sc.id}: ${sc.what} (${sc.route}, autopilot=${sc.path}, ${sc.vehicle}, ${quality}) ===`);
    await page.goto(`http://127.0.0.1:${port}/index.html?smoke=drive&quality=${quality}&route=${sc.route}&autopilot=${encodeURIComponent(sc.path)}&vehicle=${sc.vehicle}`);
    const started = await waitFor(/\[Smoke\] drive scenario/, 150), pathOk = started && await waitFor(/\[Smoke\] autopilot path /, 30), ground = pathOk && await waitFor(/\[Drive\] ground ready/, 120);
    check(`${sc.id}: the route loaded and the autopilot path exists`, started && pathOk && has(new RegExp('\\[Smoke\\] driving the test corridor ' + sc.route)) && !has(/no autopilot path named/), started ? (logs.find(l => /^\[Route\] /.test(l)) || 'no [Route] line').slice(0, 200) : 'the build did not start');
    check(`${sc.id}: the vehicle stood on real ground and was released`, ground, ground ? (logs.find(l => /\[Drive\] ground ready/.test(l)) || '').slice(0, 120) : 'no ground-ready line');
    let doneLine = null, shots = 0, lastPct = -1;
    if (ground) {
      const t0 = Date.now();
      while (Date.now() - t0 < 420000) {
        doneLine = logs.slice().reverse().find(l => /\[Autopilot\] (done|FAILED)/.test(l)); if (doneLine) break;
        const prog = logs.slice().reverse().find(l => /\[Autopilot\] progress/.test(l)); const pct = prog ? +(/progress (\d+)%/.exec(prog) || [0, 0])[1] : 0;
        if (shots < 3 && pct >= 20 + shots * 30 && pct !== lastPct) { lastPct = pct; try { const png = PNG.sync.read(await page.screenshot()); frames.push(png); shots++; check(`${sc.id}: frame ${shots} (at ${pct} percent of the path) shows a rendered scene`, stats(png).sd > 12, `sd ${stats(png).sd.toFixed(0)}`); } catch (e) { } }
        await page.waitForTimeout(1000);
      }
    }
    const d = parseDone(doneLine);
    check(`${sc.id}: the autopilot reached the end of the path`, d && d.ok && d.reached, d ? (doneLine || '').slice(0, 220) : 'no [Autopilot] done line (timed out, or the vehicle never started)');
    if (d && d.ok) {
      check(`${sc.id}: the vehicle stayed within 3 m of its lane`, d.maxErr <= 3.0, `largest distance from the lane ${d.maxErr.toFixed(2)} m`);
      if (sc.level === 'deck') check(`${sc.id}: the vehicle was on the deck (17 m or more) and never below the road`, d.maxY >= 17.0 && d.minY >= 9.0, `height ${d.minY.toFixed(2)} to ${d.maxY.toFixed(2)} m`);
      if (sc.level === 'ground') check(`${sc.id}: the vehicle stayed on the ground road under the deck (9 to 11.5 m)`, d.minY >= 9.0 && d.maxY <= 11.5, `height ${d.minY.toFixed(2)} to ${d.maxY.toFixed(2)} m`);
    }
    check(`${sc.id}: the vehicle did not leave the world`, !has(/left the world/), '');
    const bad = logs.filter(l => /exception|NullReference|IndexOutOfRange|invalid road network|invalid route data|could not build/i.test(l) && !/GLib|swiftshader/i.test(l));
    check(`${sc.id}: no exception, no invalid route or road network`, bad.length === 0 && errors.length === 0, bad[0] ? bad[0].slice(0, 200) : errors[0] ? errors[0].slice(0, 200) : '');
    report.scenarios.push({ id: sc.id, route: sc.route, path: sc.path, vehicle: sc.vehicle, result: d, line: (doneLine || '').slice(0, 300), progress: logs.filter(l => /\[Autopilot\] progress/.test(l)).slice(-3) });
    await page.close();
  }
  if (frames.length) { fs.writeFileSync(prefix + '-sheet.png', PNG.sync.write(sheet(PNG, frames, 3, 512, 288))); console.log(`\nwrote ${prefix}-sheet.png (${frames.length} frames, up to three per drive: at 20, 50 and 80 percent of the path)`); }
  report.checks = results; fs.writeFileSync(prefix + '-report.json', JSON.stringify(report, null, 1));
  await browser.close(); server.close();
  const failed = results.filter(r => !r.ok);
  console.log(`\n${results.length - failed.length} of ${results.length} checks passed.`);
  if (failed.length) { console.log('E5 ROUNDABOUT AND FLYOVER CHECKS FAILED:'); failed.forEach(f => console.log(' - ' + f.name + (f.detail ? ' (' + f.detail + ')' : ''))); process.exit(1); }
})();
