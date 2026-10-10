// Screenshot scenario for the WebGL build. Usage: node tools/webgl-shot.js <build-dir> <query> <ready-log-regex> <screenshot-prefix>
// Loads index.html?<query>, waits for the game to log the ready line, screenshots, then (dashboard only) clicks a nav item and checks the toast appears.
const http = require('http'), fs = require('fs'), path = require('path');
const { chromium } = require('playwright');
const { PNG } = require('pngjs');

const dir = path.resolve(process.argv[2] || 'Builds/WebGL');
const query = process.argv[3] || 'smoke=dashboard';
const ready = new RegExp(process.argv[4] || '\\[Dashboard\\] built');
const prefix = process.argv[5] || 'webgl-dashboard';
const MIME = { '.html': 'text/html', '.js': 'text/javascript', '.css': 'text/css', '.wasm': 'application/wasm', '.json': 'application/json', '.png': 'image/png', '.ico': 'image/x-icon', '.gz': 'application/octet-stream', '.data': 'application/octet-stream' };
const server = http.createServer((req, res) => {
  let p = decodeURIComponent(req.url.split('?')[0]); if (p.endsWith('/')) p += 'index.html';
  const f = path.join(dir, p);
  if (!f.startsWith(dir) || !fs.existsSync(f) || fs.statSync(f).isDirectory()) { res.writeHead(404); res.end('not found'); return; }
  res.writeHead(200, { 'Content-Type': MIME[path.extname(f)] || 'application/octet-stream' }); fs.createReadStream(f).pipe(res);
});

(async () => {
  const failures = [];
  await new Promise(r => server.listen(0, '127.0.0.1', r)); const port = server.address().port;
  const browser = await chromium.launch({ args: ['--use-gl=angle', '--use-angle=swiftshader', '--enable-unsafe-swiftshader', '--ignore-gpu-blocklist', '--no-sandbox'] });
  const page = await browser.newPage({ viewport: { width: 1024, height: 576 } });
  const logs = [], errors = [];
  page.on('console', m => logs.push(m.text())); page.on('pageerror', e => errors.push(String(e)));
  await page.goto(`http://127.0.0.1:${port}/index.html?${query}`);
  let ok = false; for (let i = 0; i < 180 && !ok; i++) { await page.waitForTimeout(1000); ok = logs.some(l => ready.test(l)); }
  if (!ok) failures.push(`the game never logged ${ready}`);
  await page.waitForTimeout(6000);
  await page.screenshot({ path: `${prefix}-0.png` });

  if (/dashboard/.test(query) && ok) {
    const box = await page.locator('#unity-canvas').boundingBox();
    const targets = [...logs.join('\n').matchAll(/TARGET (\w+) '([^']*)' x=([\d.]+) y=([\d.]+)/g)].map(m => ({ name: m[2], x: +m[3], y: +m[4] }));
    console.log(`UI targets: ${targets.length}`); targets.slice(0, 40).forEach(t => console.log(`  ${t.name} (${t.x}, ${t.y})`));
    if (targets.length < 15) failures.push(`only ${targets.length} clickable targets reported (expected the whole dashboard)`);
    const friends = targets.find(t => t.name === 'Nav_Friends');
    if (friends) {
      const before = PNG.sync.read(fs.readFileSync(`${prefix}-0.png`));
      await page.mouse.move(box.x + friends.x * box.width, box.y + friends.y * box.height); await page.waitForTimeout(300);
      await page.mouse.down(); await page.waitForTimeout(200); await page.mouse.up(); await page.waitForTimeout(1500);
      await page.screenshot({ path: `${prefix}-1-clicked.png` });
      const after = PNG.sync.read(fs.readFileSync(`${prefix}-1-clicked.png`));
      let n = 0;
      for (let y = Math.floor(box.y + 0.925 * box.height); y < Math.floor(box.y + 0.975 * box.height); y++)
        for (let x = Math.floor(box.x + 0.25 * box.width); x < Math.floor(box.x + 0.75 * box.width); x++) {
          const i = (y * before.width + x) * 4;
          if (Math.abs(before.data[i] - after.data[i]) + Math.abs(before.data[i + 1] - after.data[i + 1]) + Math.abs(before.data[i + 2] - after.data[i + 2]) > 60) n++;
        }
      console.log(`nav click test: ${n} pixels changed where the toast appears`);
      if (n < 100) failures.push('clicking a sidebar item did nothing (no toast appeared)');
    } else failures.push('the Friends nav item was not reported as a clickable target');
  }
  logs.filter(l => /\[(Dashboard|Smoke|Boot|Brand|World|Drive)\]/.test(l)).slice(0, 30).forEach(l => console.log(l.slice(0, 300)));
  if (errors.length) failures.push(`${errors.length} page error(s): ${errors[0].slice(0, 200)}`);
  const exc = logs.filter(l => /exception|NullReference|IndexOutOfRange|error CS/i.test(l) && !/GLib|swiftshader/i.test(l));
  if (exc.length) { failures.push(`${exc.length} exception line(s): ${exc[0].slice(0, 240)}`); exc.slice(0, 5).forEach(l => console.log('EXC: ' + l.slice(0, 400))); }
  await browser.close(); server.close();
  if (failures.length) { console.log('\nSCREENSHOT SCENARIO FAILED:'); failures.forEach(f => console.log(' - ' + f)); process.exit(1); }
  console.log('\nSCREENSHOT SCENARIO PASSED');
})();
