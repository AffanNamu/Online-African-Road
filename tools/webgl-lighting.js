// Lighting scenario for the WebGL build. Usage: node tools/webgl-lighting.js <build-dir> [prefix]      (self-test of the measuring code: --selftest)
//
// Loads the build four times with ?smoke=drive&hour=dawn|noon|sunset|night (the page address sets the hour, so the result does not depend on when CI runs),
// waits for the truck to land, and takes two screenshots at each hour: the default chase view and the front-bumper view (a forward view of the road).
// The game's own [Render] and [Light] log lines are printed (colour space, HDR, additional lights, sun, exposure, ambient, headlights), and the brightness
// of every screenshot is MEASURED (mean luminance of the whole frame, the sky band and the road band, spread, crushed-black and blown-white fractions).
// The measurements are compared with the thresholds below. Screenshots and a metrics file are written next to <prefix> for human review: passing the
// thresholds says the frame is not black, blown out or flat; it does not say the lighting looks good.
const http = require('http'), fs = require('fs'), path = require('path');

// ---- what is measured ------------------------------------------------------------------------------------------------------------------------------
// Regions are fractions of the frame (x0, y0, x1, y1). They avoid the HUD: the dial (bottom left), the minimap and camera button (top right).
const REGIONS = {
  whole: [0, 0, 1, 1],
  sky: [0.25, 0.03, 0.72, 0.25],
  road: [0.30, 0.62, 0.70, 0.88],
};

function luma(r, g, b) { return 0.2126 * r + 0.7152 * g + 0.0722 * b; }   // on the displayed sRGB values: a relative measure, not photometric

/** Brightness statistics (0..255) of a decoded PNG {width, height, data} over a region [x0, y0, x1, y1] given as fractions of the frame. */
function lumaStats(png, region) {
  const x0 = Math.max(0, Math.floor(region[0] * png.width)), x1 = Math.min(png.width, Math.ceil(region[2] * png.width));
  const y0 = Math.max(0, Math.floor(region[1] * png.height)), y1 = Math.min(png.height, Math.ceil(region[3] * png.height));
  const hist = new Uint32Array(256); let n = 0, sum = 0, sum2 = 0;
  for (let y = y0; y < y1; y++) for (let x = x0; x < x1; x++) {
    const i = (y * png.width + x) * 4, l = luma(png.data[i], png.data[i + 1], png.data[i + 2]);
    hist[Math.min(255, Math.round(l))]++; n++; sum += l; sum2 += l * l;
  }
  if (n === 0) return { mean: NaN, std: NaN, p5: NaN, p95: NaN, crushed: NaN, blown: NaN, pixels: 0 };
  const pct = q => { let acc = 0; for (let v = 0; v < 256; v++) { acc += hist[v]; if (acc >= q * n) return v; } return 255; };
  let crushed = 0, blown = 0; for (let v = 0; v < 6; v++) crushed += hist[v]; for (let v = 250; v < 256; v++) blown += hist[v];
  const mean = sum / n;
  return { mean, std: Math.sqrt(Math.max(0, sum2 / n - mean * mean)), p5: pct(0.05), p95: pct(0.95), crushed: crushed / n, blown: blown / n, pixels: n };
}

function measure(file) {
  const { PNG } = require('pngjs'); const png = PNG.sync.read(fs.readFileSync(file)); const out = {};
  for (const [name, r] of Object.entries(REGIONS)) out[name] = lumaStats(png, r);
  return out;
}

// ---- the four reference hours and the thresholds ---------------------------------------------------------------------------------------------------
// Thresholds are mean luminance (0..255) of a region, plus limits on crushed blacks and blown whites. `null` = measured and printed, not enforced (baseline).
const PRESETS = [['dawn', 6.4], ['noon', 12], ['sunset', 17.6], ['night', 23]];
const THRESHOLDS = null;

// ---- the browser run -------------------------------------------------------------------------------------------------------------------------------
const MIME = { '.html': 'text/html', '.js': 'text/javascript', '.css': 'text/css', '.wasm': 'application/wasm', '.json': 'application/json', '.png': 'image/png', '.ico': 'image/x-icon', '.gz': 'application/octet-stream', '.data': 'application/octet-stream' };
function serve(dir) {
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

async function main() {
  const dir = path.resolve(process.argv[2] || 'Builds/WebGL'), prefix = process.argv[3] || 'webgl-light';
  const { chromium } = require('playwright');
  const failures = [], metrics = {}, lines = {};
  const server = await serve(dir); const port = server.address().port;
  const browser = await chromium.launch({ args: ['--use-gl=angle', '--use-angle=swiftshader', '--enable-unsafe-swiftshader', '--ignore-gpu-blocklist', '--no-sandbox'] });

  for (const [name, hour] of PRESETS) {
    console.log(`\n=== ${name} (hour ${hour}) ===`);
    const page = await browser.newPage({ viewport: { width: 1024, height: 576 } });
    const logs = [], errors = [];
    page.on('console', m => logs.push(m.text())); page.on('pageerror', e => errors.push(String(e)));
    const find = (re, from = 0) => logs.slice(from).find(l => re.test(l));
    const waitFor = async (re, secs, from = 0) => { for (let i = 0; i < secs * 4; i++) { const l = find(re, from); if (l) return l; await page.waitForTimeout(250); } return null; };
    await page.goto(`http://127.0.0.1:${port}/index.html?smoke=drive&hour=${name}`);
    if (!await waitFor(/\[Smoke\] drive scenario/, 150)) { failures.push(`${name}: the build did not start the drive scenario`); await page.close(); continue; }
    if (!await waitFor(/\[Drive\] ground ready/, 90)) failures.push(`${name}: no ground under the truck`);
    await waitFor(/\[Light\] headlights on=True/, name === 'night' ? 15 : 1);   // at night the truck switches its headlights on when it spawns
    await page.waitForTimeout(5000);
    await page.mouse.click(512, 288);

    const render = find(/^\[Render\] tier=/); const light = [...logs].reverse().find(l => /^\[Light\] hour=/.test(l));
    const grade = [...logs].reverse().find(l => /^\[Light\] grade/.test(l)); const head = find(/^\[Light\] headlights/);
    console.log('  ' + (render || 'NO [Render] LINE').slice(0, 520)); console.log('  ' + (light || 'NO [Light] hour LINE').slice(0, 520));
    if (grade) console.log('  ' + grade); if (head) console.log('  ' + head);
    lines[name] = { render, light, grade, head };
    if (!render || !/colorSpace=(Gamma|Linear)/.test(render)) failures.push(`${name}: the [Render] line does not report the colour space`);
    if (!light) failures.push(`${name}: no [Light] line`);
    else {
      const m = /hour=([\d.]+) source=(\w+)/.exec(light);
      if (!m || Math.abs(+m[1] - hour) > 0.06 || m[2] !== 'url') failures.push(`${name}: the world is not lit for the requested hour (${light.slice(0, 120)})`);
    }

    metrics[name] = {};
    await page.screenshot({ path: `${prefix}-${name}-chase.png` });
    metrics[name].chase = measure(`${prefix}-${name}-chase.png`);
    // the bumper view looks along the road: press 8 (held across a rendered frame; repeated only if nothing was logged)
    for (let i = 0; i < 3; i++) { const mark = logs.length; await page.keyboard.press('8', { delay: 350 }); if (await waitFor(/\[Camera\] view=FrontBumper number=8 /, 5, mark)) break; }
    await page.waitForTimeout(1500);
    await page.screenshot({ path: `${prefix}-${name}-bumper.png` });
    metrics[name].bumper = measure(`${prefix}-${name}-bumper.png`);
    for (const view of ['chase', 'bumper']) {
      const s = metrics[name][view];
      console.log(`  ${view.padEnd(6)} whole mean=${s.whole.mean.toFixed(1)} std=${s.whole.std.toFixed(1)} p5=${s.whole.p5} p95=${s.whole.p95} | sky mean=${s.sky.mean.toFixed(1)} | road mean=${s.road.mean.toFixed(1)} p95=${s.road.p95} | crushed=${(s.whole.crushed * 100).toFixed(0)}% blown=${(s.whole.blown * 100).toFixed(1)}%`);
      if (!(s.whole.std > 2)) failures.push(`${name} ${view}: the frame is flat or blank (std ${s.whole.std.toFixed(2)})`);
    }
    if (errors.length) failures.push(`${name}: ${errors.length} page error(s): ${errors[0].slice(0, 160)}`);
    const exc = logs.filter(l => /exception|NullReference|IndexOutOfRange/i.test(l) && !/GLib|swiftshader/i.test(l));
    if (exc.length) failures.push(`${name}: ${exc.length} exception line(s): ${exc[0].slice(0, 160)}`);
    await page.close();
  }
  await browser.close(); server.close();

  fs.writeFileSync(`${prefix}-metrics.json`, JSON.stringify({ regions: REGIONS, presets: PRESETS, thresholds: THRESHOLDS, metrics, lines }, null, 2));
  const means = n => metrics[n] && metrics[n].chase ? metrics[n].chase.whole.mean : NaN;
  console.log('\n--- brightness (mean luminance 0..255, chase view) --- ' + PRESETS.map(([n]) => `${n}=${means(n).toFixed(1)}`).join('  '));
  if (!(means('noon') > means('night') + 10)) failures.push(`noon (${means('noon').toFixed(1)}) is not clearly brighter than night (${means('night').toFixed(1)})`);
  if (THRESHOLDS) enforce(metrics, failures); else console.log('  thresholds: NOT ENFORCED yet (baseline measurement)');

  if (failures.length) { console.log('\nLIGHTING TEST FAILED:'); failures.forEach(f => console.log(' - ' + f)); process.exit(1); }
  console.log('\nLIGHTING TEST PASSED: four hours lit, measured and screenshotted. Review the screenshots: passing says nothing about how good they look.');
}

function enforce(metrics, failures, thresholds = THRESHOLDS) {
  if (!thresholds) return;
  for (const [name] of PRESETS) for (const view of ['chase', 'bumper']) {
    const t = thresholds[name] && thresholds[name][view]; if (!t || !metrics[name] || !metrics[name][view]) continue; const s = metrics[name][view];
    for (const [region, lim] of Object.entries(t)) {
      const m = s[region].mean;
      if (lim.min !== undefined && !(m >= lim.min)) failures.push(`${name} ${view} ${region}: mean ${m.toFixed(1)} is below ${lim.min}`);
      if (lim.max !== undefined && !(m <= lim.max)) failures.push(`${name} ${view} ${region}: mean ${m.toFixed(1)} is above ${lim.max}`);
      if (lim.maxCrushed !== undefined && !(s[region].crushed <= lim.maxCrushed)) failures.push(`${name} ${view} ${region}: ${(s[region].crushed * 100).toFixed(0)}% of pixels are crushed black (limit ${lim.maxCrushed * 100}%)`);
      if (lim.maxBlown !== undefined && !(s[region].blown <= lim.maxBlown)) failures.push(`${name} ${view} ${region}: ${(s[region].blown * 100).toFixed(1)}% of pixels are blown white (limit ${lim.maxBlown * 100}%)`);
    }
  }
}

// ---- self-test of the measuring code (no browser, no build) ----------------------------------------------------------------------------------------
function selftest() {
  const { PNG } = require('pngjs'); let bad = 0;
  const expect = (cond, msg) => { console.log((cond ? 'PASS ' : 'FAIL ') + msg); if (!cond) bad++; };
  const make = (w, h, fn) => { const p = new PNG({ width: w, height: h }); for (let y = 0; y < h; y++) for (let x = 0; x < w; x++) { const i = (y * w + x) * 4, [r, g, b] = fn(x, y); p.data[i] = r; p.data[i + 1] = g; p.data[i + 2] = b; p.data[i + 3] = 255; } return p; };
  const black = make(100, 100, () => [0, 0, 0]), white = make(100, 100, () => [255, 255, 255]), grey = make(100, 100, () => [128, 128, 128]);
  expect(lumaStats(black, REGIONS.whole).mean === 0 && lumaStats(black, REGIONS.whole).crushed === 1, 'a black frame has mean 0 and is 100% crushed');
  expect(Math.abs(lumaStats(white, REGIONS.whole).mean - 255) < 0.01 && lumaStats(white, REGIONS.whole).blown === 1, 'a white frame has mean 255 and is 100% blown');
  expect(Math.abs(lumaStats(grey, REGIONS.whole).mean - 128) < 0.01 && lumaStats(grey, REGIONS.whole).std < 0.01, 'a grey frame has mean 128 and no spread');
  const red = lumaStats(make(10, 10, () => [255, 0, 0]), REGIONS.whole).mean, green = lumaStats(make(10, 10, () => [0, 255, 0]), REGIONS.whole).mean, blue = lumaStats(make(10, 10, () => [0, 0, 255]), REGIONS.whole).mean;
  expect(Math.abs(red - 54.2) < 0.2 && Math.abs(green - 182.4) < 0.2 && Math.abs(blue - 18.4) < 0.2, 'channel weights are Rec. 709 (red 54, green 182, blue 18)');
  const half = make(100, 100, (x, y) => y < 50 ? [200, 200, 200] : [20, 20, 20]);
  expect(lumaStats(half, [0, 0, 1, 0.5]).mean > 199 && lumaStats(half, [0, 0.5, 1, 1]).mean < 21, 'regions measure only their own part of the frame');
  expect(lumaStats(half, REGIONS.whole).std > 85 && lumaStats(half, REGIONS.whole).p5 < 25 && lumaStats(half, REGIONS.whole).p95 > 195, 'spread and percentiles see both halves');
  const dither = make(100, 100, (x, y) => (x + y) % 2 ? [0, 0, 0] : [255, 255, 255]);
  expect(Math.abs(lumaStats(dither, REGIONS.whole).crushed - 0.5) < 0.01 && Math.abs(lumaStats(dither, REGIONS.whole).blown - 0.5) < 0.01, 'half black and half white is 50% crushed and 50% blown');
  expect(lumaStats(grey, [0.5, 0.5, 0.5, 0.5]).pixels === 0 && Number.isNaN(lumaStats(grey, [0.5, 0.5, 0.5, 0.5]).mean), 'an empty region is NaN, not 0');
  expect(lumaStats(grey, [-1, -1, 2, 2]).pixels === 10000, 'a region larger than the frame is clipped to it');
  const st = (mean, crushed = 0, blown = 0) => ({ mean, crushed, blown });
  const m = (whole, road) => ({ noon: { chase: { whole, sky: st(100), road } } });
  const run = (metrics, thr) => { const f = []; enforce(metrics, f, thr); return f; };
  expect(run(m(st(100), st(100)), null).length === 0, 'enforce does nothing when no thresholds are set');
  expect(run(m(st(100), st(100)), { dawn: { chase: { whole: { min: 999 } } } }).length === 0, 'enforce ignores presets that have no thresholds or no measurements');
  expect(run(m(st(100), st(100)), { noon: { chase: { whole: { min: 50, max: 200 } } } }).length === 0, 'a mean inside its band passes');
  expect(run(m(st(30), st(100)), { noon: { chase: { whole: { min: 50 } } } }).length === 1, 'a mean below its minimum fails');
  expect(run(m(st(240), st(100)), { noon: { chase: { whole: { max: 200 } } } }).length === 1, 'a mean above its maximum fails');
  expect(run(m(st(100, 0.5), st(100)), { noon: { chase: { whole: { maxCrushed: 0.2 } } } }).length === 1, 'too many crushed blacks fails');
  expect(run(m(st(100, 0, 0.2), st(100)), { noon: { chase: { whole: { maxBlown: 0.05 } } } }).length === 1, 'too many blown whites fails');
  expect(run(m(st(100), st(10)), { noon: { chase: { whole: { min: 50 }, road: { min: 40 } } } }).length === 1, 'each region is judged on its own');
  console.log(bad ? `\nSELFTEST FAILED (${bad})` : '\nSELFTEST PASSED'); process.exit(bad ? 1 : 0);
}

module.exports = { lumaStats, REGIONS, PRESETS };
if (process.argv.includes('--selftest')) selftest(); else if (require.main === module) main().catch(e => { console.error(e); process.exit(1); });
