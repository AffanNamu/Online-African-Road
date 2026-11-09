// Advertising rendering path, checked in the real WebGL build: ?smoke=drive&adtest=1 gives every board in a loaded chunk a generated test image through the REAL atlas and the REAL one-mesh face renderer (no network).
// Usage: node tools/webgl-ads.js <build-dir> <out-prefix> [quality]        (self-test: --selftest)
// Checks: the service starts in fixture mode, boards were found in the loaded chunks, the test images were placed in the atlas, nothing threw, and a frame is rendered. The frame is for a person to look at.
const http = require('http'), fs = require('fs'), path = require('path');
const luma = (r, g, b) => 0.2126 * r + 0.7152 * g + 0.0722 * b;
function stats(png) { let s = 0, s2 = 0, n = 0; for (let i = 0; i < png.data.length; i += 4) { const l = luma(png.data[i], png.data[i + 1], png.data[i + 2]); s += l; s2 += l * l; n++; } const m = s / n; return { mean: m, sd: Math.sqrt(Math.max(0, s2 / n - m * m)) }; }
function parseFixture(line) { const m = /\[Ads\] fixture installed on (\d+) faces in (\d+) cells/.exec(line || ''); return m ? { faces: +m[1], cells: +m[2] } : null; }
if (process.argv[2] === '--selftest') {
  let bad = 0; const t = (n, ok) => { if (!ok) { console.log('selftest FAIL ' + n); bad++; } };
  const f = parseFixture('[Ads] fixture installed on 3 faces in 3 cells'); t('fixture line', f && f.faces === 3 && f.cells === 3); t('not a fixture line', parseFixture('[Ads] faces=1') === null && parseFixture(null) === null);
  const { PNG } = require('pngjs'); const flat = new PNG({ width: 4, height: 4 }); flat.data.fill(100); t('stats', Math.abs(stats(flat).mean - 100) < 0.5);
  console.log(bad ? 'SELFTEST FAILED' : 'SELFTEST PASSED'); process.exit(bad ? 1 : 0);
}
const dir = path.resolve(process.argv[2] || 'Builds/WebGL'), prefix = process.argv[3] || 'webgl-ads', quality = process.argv[4] || 'low';
const MIME = { '.html': 'text/html', '.js': 'text/javascript', '.css': 'text/css', '.wasm': 'application/wasm', '.json': 'application/json', '.png': 'image/png', '.ico': 'image/x-icon', '.gz': 'application/octet-stream', '.data': 'application/octet-stream' };
const serve = () => new Promise(resolve => { const s = http.createServer((req, res) => { let p = decodeURIComponent(req.url.split('?')[0]); if (p.endsWith('/')) p += 'index.html'; const f = path.join(dir, p); if (!f.startsWith(dir) || !fs.existsSync(f) || fs.statSync(f).isDirectory()) { res.writeHead(404); res.end('not found'); return; } res.writeHead(200, { 'Content-Type': MIME[path.extname(f)] || 'application/octet-stream' }); fs.createReadStream(f).pipe(res); }).listen(0, '127.0.0.1', () => resolve(s)); });
const results = []; const check = (name, ok, detail) => { results.push({ name, ok: !!ok, detail }); console.log(`  ${ok ? 'PASS' : 'FAIL'}  ${name}${detail ? ' (' + detail + ')' : ''}`); };
(async () => {
  const { chromium } = require('playwright'); const { PNG } = require('pngjs');
  const server = await serve(), port = server.address().port, browser = await chromium.launch({ args: ['--use-gl=angle', '--use-angle=swiftshader', '--enable-unsafe-swiftshader', '--ignore-gpu-blocklist', '--no-sandbox'] });
  const page = await browser.newPage({ viewport: { width: 1024, height: 576 } }); const logs = [], errors = []; page.on('console', m => logs.push(m.text())); page.on('pageerror', e => errors.push(String(e)));
  const has = re => logs.some(l => re.test(l)); const waitFor = async (re, secs) => { for (let i = 0; i < secs * 4; i++) { if (has(re)) return true; await page.waitForTimeout(250); } return false; };
  // the shipped route has a hand-placed billboard at 7,000 m; 6,900 puts the truck 100 m before it
  await page.goto(`http://127.0.0.1:${port}/index.html?smoke=drive&hold=1&adtest=1&quality=${quality}&start=6900`);
  const started = await waitFor(/\[Smoke\] drive scenario/, 150), ground = started && await waitFor(/\[Drive\] ground ready/, 90), held = ground && await waitFor(/\[Drive\] smoke hold: vehicle frozen/, 60);
  check('the drive scenario started and the truck stood on real ground', started && ground && held, started ? '' : 'the build did not start');
  check('the advertising service started in fixture mode', has(/\[Ads\] ready \(fixture/), (logs.find(l => /^\[Ads\]/.test(l)) || 'no [Ads] line').slice(0, 160));
  const fx = await waitFor(/\[Ads\] fixture installed on \d+ faces/, 60), line = logs.find(l => /\[Ads\] fixture installed/.test(l));
  const f = parseFixture(line); check('boards in the loaded chunks received test images in the atlas', fx && f && f.faces >= 1 && f.cells >= 1, line || 'no fixture line');
  await page.waitForTimeout(3000); const shot = PNG.sync.read(await page.screenshot()); check('a frame is rendered', stats(shot).sd > 12, `sd ${stats(shot).sd.toFixed(0)}`); fs.writeFileSync(prefix + '-frame.png', PNG.sync.write(shot));
  check('the vehicle did not leave the world', !has(/left the world/), '');
  const bad = logs.filter(l => /exception|NullReference|IndexOutOfRange|\[Ads\].*error/i.test(l) && !/GLib|swiftshader/i.test(l)); check('no exception', bad.length === 0 && errors.length === 0, bad[0] ? bad[0].slice(0, 200) : errors[0] ? errors[0].slice(0, 200) : '');
  fs.writeFileSync(prefix + '-report.json', JSON.stringify({ quality, ads: logs.filter(l => /^\[Ads\]/.test(l)).slice(-6), checks: results }, null, 1));
  await browser.close(); server.close(); const failed = results.filter(r => !r.ok);
  console.log(`\n${results.length - failed.length} of ${results.length} checks passed.`); if (failed.length) { console.log('ADVERTISING RENDER CHECKS FAILED:'); failed.forEach(x => console.log(' - ' + x.name + (x.detail ? ' (' + x.detail + ')' : ''))); process.exit(1); }
})();
