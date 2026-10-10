// Headless-browser smoke test for the WebGL build. Usage: node tools/webgl-smoke.js <build-dir> [screenshot.png]
// Serves the build over HTTP, loads it in Chromium (software WebGL2), waits for Unity to finish loading, lets the game run,
// then checks: it loaded, no page errors or Unity exceptions, the game's own startup log appeared, and the canvas is not blank.
const http = require('http'), fs = require('fs'), path = require('path');
const { chromium } = require('playwright');
const { PNG } = require('pngjs');

const dir = path.resolve(process.argv[2] || 'Builds/WebGL');
const shot = process.argv[3] || 'webgl-smoke.png';
const MIME = { '.html': 'text/html', '.js': 'text/javascript', '.css': 'text/css', '.wasm': 'application/wasm', '.json': 'application/json',
  '.png': 'image/png', '.ico': 'image/x-icon', '.gz': 'application/octet-stream', '.data': 'application/octet-stream' };

function serve() {
  return new Promise(resolve => {
    const s = http.createServer((req, res) => {
      let p = decodeURIComponent(req.url.split('?')[0]); if (p.endsWith('/')) p += 'index.html';
      const f = path.join(dir, p);
      if (!f.startsWith(dir) || !fs.existsSync(f) || fs.statSync(f).isDirectory()) { res.writeHead(404); res.end('not found'); return; }
      res.writeHead(200, { 'Content-Type': MIME[path.extname(f)] || 'application/octet-stream' });   // no Content-Encoding on purpose: tests the decompression fallback
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
  page.on('console', m => logs.push(`[${m.type()}] ${m.text()}`));
  page.on('pageerror', e => errors.push(String(e)));
  const t0 = Date.now();
  await page.goto(`http://127.0.0.1:${port}/index.html`);

  let loaded = false;
  for (let i = 0; i < 180 && !loaded; i++) {   // up to 3 minutes: software rendering and JS decompression are slow
    await page.waitForTimeout(1000);
    loaded = await page.evaluate(() => {
      const bar = document.querySelector('#unity-loading-bar');
      return !!document.querySelector('#unity-canvas') && (!bar || bar.style.display === 'none');
    }).catch(() => false);
    if (errors.length) break;
  }
  console.log(`loading finished: ${loaded} after ${((Date.now() - t0) / 1000).toFixed(1)} s`);
  if (!loaded) failures.push('Unity did not finish loading');

  await page.waitForTimeout(15000);   // let the bootstrap scene run: GameBootstrap.Start, streamer, menus
  await page.screenshot({ path: shot });

  // ---- UI interaction: the login screen must react to a mouse click and to typing (positions are fractions of the Unity canvas).
  const interact = process.env.INTERACT !== '0';
  async function frame(file) { await page.screenshot({ path: file }); return PNG.sync.read(fs.readFileSync(file)); }
  function diffCount(a, b, box, fx0, fy0, fx1, fy1) {
    let n = 0;
    for (let y = Math.floor(box.y + fy0 * box.height); y < Math.floor(box.y + fy1 * box.height); y++)
      for (let x = Math.floor(box.x + fx0 * box.width); x < Math.floor(box.x + fx1 * box.width); x++) {
        const i = (y * a.width + x) * 4;
        if (Math.abs(a.data[i] - b.data[i]) + Math.abs(a.data[i + 1] - b.data[i + 1]) + Math.abs(a.data[i + 2] - b.data[i + 2]) > 60) n++;
      }
    return n;
  }
  if (interact && loaded) {
    const box = await page.locator('#unity-canvas').boundingBox();
    console.log(`canvas box: ${JSON.stringify(box)}`);
    const at = (fx, fy) => [box.x + fx * box.width, box.y + fy * box.height];
    // The game logs where every button / input field is (UIDiagnostics); aim at those instead of guessing.
    const targets = [...logs.join('\n').matchAll(/TARGET (\w+) '([^']*)' x=([\d.]+) y=([\d.]+)/g)]
      .map(m => ({ kind: m[1], name: m[2], x: parseFloat(m[3]), y: parseFloat(m[4]) }));
    console.log(`UI targets reported by the game: ${targets.length}`);
    targets.slice(0, 12).forEach(t => console.log(`  ${t.kind} ${t.name} (${t.x}, ${t.y})`));
    const inputsByY = targets.filter(t => t.kind === 'InputField').sort((a, b) => a.y - b.y);
    const btn = targets.find(t => t.name === 'Btn_SIGNIN') || { x: 0.745, y: 0.634 };
    const fld = inputsByY[0] || { x: 0.745, y: 0.40 };
    if (!targets.length) failures.push('the game reported no UI targets (login screen not built?)');
    const press = async (x, y) => { await page.mouse.move(...at(x, y)); await page.waitForTimeout(400); await page.mouse.down(); await page.waitForTimeout(250); await page.mouse.up(); };
    const before = await frame(shot.replace('.png', '-0-start.png'));
    await press(btn.x, btn.y);                              // SIGN IN with empty fields: a status message appears under the button
    await page.waitForTimeout(1500);
    const afterClick = await frame(shot.replace('.png', '-1-clicked.png'));
    // Sign In with empty fields shows "Enter your email and password." in the status line just under the button
    const msgPixels = diffCount(before, afterClick, box, 0.58, btn.y + 0.012, 0.91, btn.y + 0.075);
    console.log(`click test: ${msgPixels} pixels changed where the status message appears`);
    if (msgPixels < 150) failures.push('clicking SIGN IN did nothing (the UI is not receiving mouse input)');
    await press(fld.x, fld.y);                              // focus the first input field and type into it
    await page.waitForTimeout(500);
    await page.keyboard.type('player@example.com', { delay: 80 });
    await page.waitForTimeout(1500);
    const afterType = await frame(shot.replace('.png', '-2-typed.png'));
    const typedPixels = diffCount(afterClick, afterType, box, fld.x - 0.14, fld.y - 0.025, fld.x + 0.14, fld.y + 0.025);
    console.log(`typing test: ${typedPixels} pixels changed inside the first input field`);
    if (typedPixels < 150) failures.push('typing into the Email field did nothing (the UI is not receiving keyboard input)');
    console.log('--- UIDiag lines ---');
    logs.filter(l => l.includes('[UIDiag]')).slice(0, 40).forEach(l => console.log(l.slice(0, 400)));
  }

  const png = PNG.sync.read(fs.readFileSync(shot));
  const buckets = new Map(); let n = 0;
  for (let i = 0; i < png.data.length; i += 4 * 7) {   // sample every 7th pixel
    const k = (png.data[i] >> 4) << 8 | (png.data[i + 1] >> 4) << 4 | (png.data[i + 2] >> 4);
    buckets.set(k, (buckets.get(k) || 0) + 1); n++;
  }
  const top = Math.max(...buckets.values());
  const distinct = buckets.size, dominantShare = top / n;
  console.log(`screenshot ${png.width}x${png.height}: ${distinct} distinct colours (4-bit), dominant colour covers ${(dominantShare * 100).toFixed(1)}%`);
  if (distinct < 8 || dominantShare > 0.98) failures.push('canvas looks blank (no visible UI or scene)');

  const interesting = logs.filter(l => !/^\[(debug|log)\] (Download|Initialize|Loading|Build|Unity)/i.test(l));
  console.log(`--- console (${logs.length} lines, showing up to 40) ---`);
  interesting.slice(0, 40).forEach(l => console.log(l.slice(0, 300)));
  console.log(`--- page errors: ${errors.length} ---`); errors.slice(0, 10).forEach(e => console.log(e.slice(0, 400)));

  if (errors.length) failures.push(`${errors.length} page error(s)`);
  const exceptions = logs.filter(l => /exception|NullReference|IndexOutOfRange|error CS/i.test(l) && !/GLib|swiftshader/i.test(l));
  if (exceptions.length) failures.push(`${exceptions.length} exception line(s) in the Unity console`);
  const boot = logs.filter(l => l.includes('[Boot]'));
  boot.forEach(l => console.log('BOOT LINE: ' + l.slice(0, 200)));
  if (!boot.length) failures.push('the game never logged its [Boot] startup line (GameBootstrap.Start did not run)');
  if (process.env.EXPECT_BACKEND === '1' && !boot.some(l => /backend configured/.test(l))) failures.push('the build was expected to have the backend baked in, but it reports NOT configured');

  await browser.close(); server.close();
  if (failures.length) { console.log('\nSMOKE TEST FAILED:\n - ' + failures.join('\n - ')); process.exit(1); }
  console.log('\nSMOKE TEST PASSED: the WebGL build loads, runs the bootstrap, and renders.');
})().catch(e => { console.error(e); process.exit(2); });
