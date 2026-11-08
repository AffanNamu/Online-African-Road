// The E1 street kit (kerbs, walkways, gutters, the planter median, dropped kerbs, pavement symbols), checked in the real WebGL build.
// Usage: node tools/webgl-e1.js <build-dir> <out-prefix> [quality]        (self-test of the frame analysis: --selftest)
// The game is opened on its engine TEST corridor (?smoke=drive&route=ng-kano-e1-streets) at fifteen places, day and night, with the truck frozen (hold=1); each place gives a chase-view and a bumper-view frame.
// Output: <prefix>-sheet.png (all frames), <prefix>-report.json and a PASS / FAIL list. The checks are about the running game (the corridor loaded, the truck stood on real ground, nothing threw);
// whether the kerbs, walkways and markings LOOK right is for a person to judge from the sheet.
const http = require('http'), fs = require('fs'), path = require('path');

const luma = (r, g, b) => 0.2126 * r + 0.7152 * g + 0.0722 * b;
function stats(png) { let s = 0, s2 = 0, n = 0; for (let i = 0; i < png.data.length; i += 4) { const l = luma(png.data[i], png.data[i + 1], png.data[i + 2]); s += l; s2 += l * l; n++; } const m = s / n; return { mean: m, sd: Math.sqrt(Math.max(0, s2 / n - m * m)) }; }
function sheet(PNG, pngs, cols, tw, th) {
  const rows = Math.ceil(pngs.length / cols), out = new PNG({ width: cols * tw, height: rows * th }); out.data.fill(24);
  pngs.forEach((p, k) => { const ox = (k % cols) * tw, oy = Math.floor(k / cols) * th; for (let y = 0; y < th; y++) for (let x = 0; x < tw; x++) { const sx = Math.min(p.width - 1, Math.floor(x * p.width / tw)), sy = Math.min(p.height - 1, Math.floor(y * p.height / th)), si = (sy * p.width + sx) * 4, di = ((oy + y) * out.width + ox + x) * 4; out.data[di] = p.data[si]; out.data[di + 1] = p.data[si + 1]; out.data[di + 2] = p.data[si + 2]; out.data[di + 3] = 255; } });
  return out;
}
if (process.argv[2] === '--selftest') {
  const { PNG } = require('pngjs'); let bad = 0;
  const flat = new PNG({ width: 8, height: 8 }); flat.data.fill(100); const s1 = stats(flat); if (Math.abs(s1.mean - 100) > 0.5 || s1.sd > 0.01) { console.log('selftest FAIL flat'); bad++; }
  const half = new PNG({ width: 8, height: 8 }); for (let i = 0; i < 64; i++) { const v = i < 32 ? 0 : 200; half.data[i * 4] = half.data[i * 4 + 1] = half.data[i * 4 + 2] = v; half.data[i * 4 + 3] = 255; } const s2 = stats(half); if (Math.abs(s2.mean - 100) > 1 || Math.abs(s2.sd - 100) > 1) { console.log('selftest FAIL halves ' + JSON.stringify(s2)); bad++; }
  const sh = sheet(PNG, [flat, half, flat], 2, 4, 4); if (sh.width !== 8 || sh.height !== 8) { console.log('selftest FAIL sheet size'); bad++; }
  console.log(bad ? 'SELFTEST FAILED' : 'SELFTEST PASSED'); process.exit(bad ? 1 : 0);
}

const dir = path.resolve(process.argv[2] || 'Builds/WebGL'), prefix = process.argv[3] || 'webgl-e1', quality = process.argv[4] || 'low';
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

// metres along the corridor (the arterial runs 0 to 760 m, the collector 760 to 1500 m; junctions at 840 right, 1020 left, 1250 right, 1420 right); `site` starts the truck on a site plate instead
// `view`: the second frame is the bumper view (key 8) unless this says 7, the overhead view, which shows the layout of the pavement marks
const PLACES = [
  { id: 'arterial-arrows', start: 90, hour: 'noon', what: 'planter median in a painted kerb, walkways, gutters, lane arrows' },
  { id: 'zebra', start: 262, hour: 'noon', what: 'zebra crossing and stop lines on the arterial' },
  { id: 'cross-opening', start: 372, hour: 'noon', what: 'E3 four-way crossing on the arterial: the planter median is cut across the junction, kerbs dropped on both sides, zebra crossings on the cross street' },
  { id: 'turn-arrows', start: 480, hour: 'noon', what: 'left and right turn arrows in two lanes' },
  { id: 'class-change', start: 700, hour: 'noon', what: 'arterial narrowing to the collector (the run transition)' },
  { id: 'local-mouth', start: 800, hour: 'noon', what: 'collector with a kerb dropped at the local street on the right' },
  { id: 'service-mouth', start: 975, hour: 'noon', what: 'service road with its yield triangles and loading bay lines, kerb dropped on the left' },
  { id: 'yard', site: 'site_yard', hour: 'noon', what: 'E2 logistics yard: concrete plate, kerb with an entrance, stop line, arrows, two warehouses, loading bays, keep-clear hatch', view: '7' },
  { id: 'yard-ground', site: 'site_yard', hour: 'noon', what: 'the same yard from the cab: the bays and the warehouse fronts' },
  { id: 'terminal', site: 'site_terminal', hour: 'noon', what: 'E2 bus terminal: black and white kerb, twelve bays, four shelters, ticket kiosks, market stalls, name board', view: '7' },
  { id: 'terminal-ground', site: 'site_terminal', hour: 'noon', what: 'the same terminal from the cab' },
  { id: 'arterial-night', start: 90, hour: 'night', what: 'the arterial at night: median lights on the planter, lit kerbs' },
  { id: 'collector-night', start: 880, hour: 'night', what: 'the collector at night: street lights behind the walkway' },
  { id: 'yard-night', site: 'site_yard', hour: 'night', what: 'the logistics yard at night: lamps, lit warehouse windows', view: '7' },
  { id: 'terminal-night', site: 'site_terminal', hour: 'night', what: 'the bus terminal at night', view: '7' },
];

(async () => {
  const { chromium } = require('playwright'); const { PNG } = require('pngjs');
  const server = await serve(), port = server.address().port;
  const browser = await chromium.launch({ args: ['--use-gl=angle', '--use-angle=swiftshader', '--enable-unsafe-swiftshader', '--ignore-gpu-blocklist', '--no-sandbox'] });
  const frames = [], report = { quality, places: [] };
  for (const pl of PLACES) {
    const page = await browser.newPage({ viewport: { width: 1024, height: 576 } });
    const logs = [], errors = []; page.on('console', m => logs.push(m.text())); page.on('pageerror', e => errors.push(String(e)));
    const has = re => logs.some(l => re.test(l)); const waitFor = async (re, secs) => { for (let i = 0; i < secs * 4; i++) { if (has(re)) return true; await page.waitForTimeout(250); } return false; };
    console.log(`\n=== ${pl.id}: ${pl.what} (${pl.site ? 'on ' + pl.site : 'start=' + pl.start + ' m'}, hour=${pl.hour}, ${quality}) ===`);
    await page.goto(`http://127.0.0.1:${port}/index.html?smoke=drive&hold=1&quality=${quality}&route=ng-kano-e1-streets&${pl.site ? 'site=' + pl.site : 'start=' + pl.start}&hour=${pl.hour}`);
    const started = await waitFor(/\[Smoke\] drive scenario/, 150), ground = started && await waitFor(/\[Drive\] ground ready/, 90), held = ground && await waitFor(/\[Drive\] smoke hold: vehicle frozen/, 60);
    check(`${pl.id}: the drive scenario started on the corridor`, started && has(/\[Smoke\] driving the test corridor ng-kano-e1-streets/) && has(/\[Route\] ng-kano-e1-streets: .*6 junctions/), started ? (logs.find(l => /^\[Route\] /.test(l)) || 'no [Route] line').slice(0, 200) : 'the build did not start');
    check(`${pl.id}: the truck stood on real ground and was held`, held, held ? (logs.filter(l => /\[Drive\] t=/.test(l)).pop() || '').slice(0, 160) : 'no ground / hold line');
    let chase = null, bumper = null;
    if (held) {
      await page.waitForTimeout(4000); await page.mouse.click(512, 288); await page.waitForTimeout(500);
      chase = PNG.sync.read(await page.screenshot());
      const second = pl.view || '8', wantView = second === '7' ? 'Overhead' : 'FrontBumper';
      for (let i = 0; i < 3; i++) { const n = logs.length; await page.keyboard.press(second, { delay: 350 }); await page.waitForTimeout(800); if (logs.slice(n).some(l => new RegExp('\\[Camera\\] view=' + wantView).test(l))) break; }
      await page.waitForTimeout(2200); bumper = PNG.sync.read(await page.screenshot());
      const a = stats(chase), b = stats(bumper);
      check(`${pl.id}: the frames show a rendered scene`, a.sd > 12 && b.sd > 12, `chase mean ${a.mean.toFixed(0)} sd ${a.sd.toFixed(0)}, bumper mean ${b.mean.toFixed(0)} sd ${b.sd.toFixed(0)}`);
      frames.push(chase, bumper);
    }
    check(`${pl.id}: the vehicle did not leave the world`, !has(/left the world/), '');
    const bad = logs.filter(l => /exception|NullReference|IndexOutOfRange|invalid route data|could not build/i.test(l) && !/GLib|swiftshader/i.test(l));
    check(`${pl.id}: no exception, no invalid route data`, bad.length === 0 && errors.length === 0, bad[0] ? bad[0].slice(0, 200) : errors[0] ? errors[0].slice(0, 200) : '');
    report.places.push({ id: pl.id, start: pl.start, hour: pl.hour, route: (logs.find(l => /^\[Route\] /.test(l)) || '').slice(0, 300), lamp: (logs.filter(l => /^\[Lamp\] level=/.test(l)).pop() || '').slice(0, 200) });
    await page.close();
  }
  if (frames.length) { fs.writeFileSync(prefix + '-sheet.png', PNG.sync.write(sheet(PNG, frames, 4, 512, 288))); console.log(`\nwrote ${prefix}-sheet.png (${frames.length} frames, two per place: the chase view, then the bumper or overhead view)`); }
  report.checks = results; fs.writeFileSync(prefix + '-report.json', JSON.stringify(report, null, 1));
  await browser.close(); server.close();
  const failed = results.filter(r => !r.ok);
  console.log(`\n${results.length - failed.length} of ${results.length} checks passed.`);
  if (failed.length) { console.log('E1 STREET KIT CHECKS FAILED:'); failed.forEach(f => console.log(' - ' + f.name + (f.detail ? ' (' + f.detail + ')' : ''))); process.exit(1); }
})();
