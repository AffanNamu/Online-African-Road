// Drive scenario for the WebGL build. Usage: node tools/webgl-drive.js <build-dir> [screenshot-prefix]
// Opens the build with ?smoke=drive (skips sign-in), waits for the vehicle to land on real ground, holds W, and checks from the
// game's own [Drive] telemetry that the truck stays on the road, accelerates and moves. Screenshots go to <prefix>-N-*.png.
const http = require('http'), fs = require('fs'), path = require('path');
const { chromium } = require('playwright');

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

  const samples = logs.map(l => /\[Drive\] t=([\d.]+) pos=\(([-\d.]+),([-\d.]+),([-\d.]+)\) kmh=([-\d.]+) dist=([\d.]+)/.exec(l)).filter(Boolean)
    .map(m => ({ t: +m[1], x: +m[2], y: +m[3], z: +m[4], kmh: +m[5], dist: +m[6] }));
  console.log(`drive samples: ${samples.length}`);
  samples.filter((_, i) => i % 3 === 0).forEach(s => console.log(`  t=${s.t} pos=(${s.x}, ${s.y}, ${s.z}) kmh=${s.kmh} dist=${s.dist}`));
  console.log('--- [Drive]/[World]/[Smoke] lines ---');
  logs.filter(l => /\[(Drive|World|Smoke)\]/.test(l) && !/\[Drive\] t=/.test(l)).slice(0, 20).forEach(l => console.log(l.slice(0, 300)));

  if (samples.length < 5) failures.push(`only ${samples.length} telemetry samples (expected many)`);
  else {
    const minY = Math.min(...samples.map(s => s.y)), maxKmh = Math.max(...samples.map(s => Math.abs(s.kmh))), maxDist = Math.max(...samples.map(s => s.dist));
    console.log(`min y ${minY.toFixed(2)}, max speed ${maxKmh.toFixed(1)} km/h, furthest from spawn ${maxDist.toFixed(1)} m`);
    if (minY < -2) failures.push(`the vehicle sank to y=${minY.toFixed(1)} (fell through the world)`);
    if (maxKmh < 10) failures.push(`holding W never got the truck above 10 km/h (max ${maxKmh.toFixed(1)})`);
    if (maxDist < 8) failures.push(`the truck moved only ${maxDist.toFixed(1)} m from its spawn`);
  }
  if (has(/left the world/)) failures.push('the vehicle left the world and had to be recovered');
  if (errors.length) failures.push(`${errors.length} page error(s): ${errors[0].slice(0, 200)}`);
  const exc = logs.filter(l => /exception|NullReference|IndexOutOfRange/i.test(l) && !/GLib|swiftshader/i.test(l));
  if (exc.length) failures.push(`${exc.length} exception line(s): ${exc[0].slice(0, 200)}`);

  await browser.close(); server.close();
  if (failures.length) { console.log('\nDRIVE TEST FAILED:'); failures.forEach(f => console.log(' - ' + f)); process.exit(1); }
  console.log('\nDRIVE TEST PASSED: the truck landed on real ground, accelerated and drove.');
})();
