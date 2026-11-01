// Kano street review in the RUNNING game. Usage: node tools/webgl-kanoreview.js <build-dir> <prefix> [views]
// Opens the WebGL build with ?smoke=kanostreet&view=<v>&hour=15.5&quality=high (the game's own lighting, camera rig, tone mapping and quality tier; no account, no vehicle), waits for the game's
// "[KanoReview] ready" line, lets the frame settle and saves <prefix>-<view>.png at 1280x720. Fails if the page logs a script error or if the build has no review prefab (the private assets were not in it).
// The game's own [Perf] and [Render] lines are copied to <prefix>-summary.json so frame time, draw calls and triangles come from the game, not from this script.
const http = require('http'), fs = require('fs'), path = require('path');
const { chromium } = require('playwright');
const dir = path.resolve(process.argv[2] || 'Builds/WebGL'), prefix = process.argv[3] || 'kanoreview', views = (process.argv[4] || 'ref,front,side,street1,street2,top').split(',');
const QUERY = process.env.REVIEW_QUERY || '&hour=15.5&quality=high';
const MIME = { '.html': 'text/html', '.js': 'text/javascript', '.css': 'text/css', '.wasm': 'application/wasm', '.json': 'application/json', '.png': 'image/png', '.ico': 'image/x-icon', '.gz': 'application/octet-stream', '.data': 'application/octet-stream' };
function serve() {
  return new Promise(resolve => {
    const s = http.createServer((req, res) => {
      let p = decodeURIComponent(req.url.split('?')[0]); if (p.endsWith('/')) p += 'index.html';
      const f = path.join(dir, p);
      if (!f.startsWith(dir) || !fs.existsSync(f) || fs.statSync(f).isDirectory()) { res.writeHead(404); res.end('not found'); return; }
      res.writeHead(200, { 'Content-Type': MIME[path.extname(f)] || 'application/octet-stream' }); fs.createReadStream(f).pipe(res);
    }).listen(0, '127.0.0.1', () => resolve(s));
  });
}
(async () => {
  const failures = [], summary = {}; const server = await serve(); const port = server.address().port;
  const browser = await chromium.launch({ args: ['--use-gl=angle', '--use-angle=swiftshader', '--enable-unsafe-swiftshader', '--ignore-gpu-blocklist', '--no-sandbox'] });
  for (const view of views) {
    const page = await browser.newPage({ viewport: { width: 1280, height: 720 } }); const logs = [], errors = [];
    page.on('console', m => logs.push(m.text())); page.on('pageerror', e => errors.push(String(e)));
    const t0 = Date.now(); await page.goto(`http://127.0.0.1:${port}/index.html?smoke=kanostreet&view=${view}${QUERY}`);
    const has = re => logs.some(l => re.test(l));
    for (let i = 0; i < 240 && !has(/\[KanoReview\] (ready|no review prefab)/); i++) await page.waitForTimeout(1000);
    if (has(/no review prefab/)) { failures.push(`${view}: the build has no review prefab (the private assets were not in it)`); await page.close(); continue; }
    if (!has(/\[KanoReview\] ready/)) { failures.push(`${view}: the review scene never started`); await page.screenshot({ path: `${prefix}-${view}-failed.png` }); await page.close(); continue; }
    const loadSeconds = (Date.now() - t0) / 1000;                       // page load to the game's own "ready" line (E0 measurement: loading)
    await page.waitForTimeout(12000);                                   // let streaming, LODs and the sky settle in software GL
    await page.screenshot({ path: `${prefix}-${view}.png` });
    const jsHeapMB = await page.evaluate(() => (performance.memory ? Math.round(performance.memory.usedJSHeapSize / 1048576) : null));
    summary[view] = { loadSeconds, jsHeapMB, budgetWarnings: logs.filter(l => l.includes('BUDGET EXCEEDED')).slice(-5), perf: logs.filter(l => l.startsWith('[Perf]') && !l.includes('BUDGET')).slice(-3), render: logs.filter(l => l.startsWith('[Render]')).slice(-2), review: logs.filter(l => l.startsWith('[KanoReview]')), errors };
    if (errors.length) failures.push(`${view}: page errors: ${errors.slice(0, 2).join(' | ')}`);
    console.log(`${view}: saved ${prefix}-${view}.png; ${JSON.stringify(summary[view].perf.slice(-1))}`);
    await page.close();
  }
  await browser.close(); server.close(); fs.writeFileSync(`${prefix}-summary.json`, JSON.stringify(summary, null, 1));
  if (failures.length) { console.error('FAILURES:\n' + failures.join('\n')); process.exit(1); }
})();
