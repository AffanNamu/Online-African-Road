// Kano street review in the RUNNING game. Usage: node tools/webgl-kanoreview.js <build-dir> <prefix> [views]
// Opens the WebGL build with ?smoke=kanostreet&view=<v>&hour=15.5&quality=<tier> (the game's own lighting, camera rig, tone mapping and quality tier; no account, no vehicle), waits for the game's
// "[KanoReview] ready" line, lets the frame settle and saves <prefix>-<label>.png at 1280x720. Fails if the page logs a script error or if the build has no review prefab (the private assets were not in it).
// The game's own [Perf] and [Render] lines are copied to <prefix>-summary.json so frame time, draw calls and triangles come from the game, not from this script.
// A view entry is "view" or "view:variant". Variants: "merged" (the merged-frontage placements, &variant=merged: a content-side draw-call prototype) and "noshadow" (&shadows=off, the game's test-only switch
// that removes the shadow pass, to isolate its share of the engine's draw calls).
// For every picture the script also measures, in a blank browser page and without writing anything else: the mean colour, the fraction of magenta pixels (the colour of Unity's error shader), the fraction of
// near-black pixels, and, for a "view:merged" picture, its difference from the "view" picture (mean absolute difference per channel, fraction of pixels that differ by more than 24 in the sum of channels).
const http = require('http'), fs = require('fs'), path = require('path');
const { chromium } = require('playwright');
const dir = path.resolve(process.argv[2] || 'Builds/WebGL'), prefix = process.argv[3] || 'kanoreview', views = (process.argv[4] || 'ref,front,side,street1,street2,top').split(',');
const QUERY = process.env.REVIEW_QUERY || '&hour=15.5&quality=high';
const MIME = { '.html': 'text/html', '.js': 'text/javascript', '.css': 'text/css', '.wasm': 'application/wasm', '.json': 'application/json', '.png': 'image/png', '.ico': 'image/x-icon', '.gz': 'application/octet-stream', '.data': 'application/octet-stream' };
const VARIANT_QUERY = { merged: '&variant=merged', noshadow: '&shadows=off' };
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
// Image measurements, done in the browser's canvas (no extra dependency). `a` and `b` are base64 PNGs.
async function imageStats(page, b64) {
  return page.evaluate(async (a) => {
    const img = new Image(); img.src = 'data:image/png;base64,' + a; await img.decode();
    const c = document.createElement('canvas'); c.width = img.width; c.height = img.height; const x = c.getContext('2d'); x.drawImage(img, 0, 0);
    const d = x.getImageData(0, 0, c.width, c.height).data, n = d.length / 4; let r = 0, g = 0, b = 0, mag = 0, blk = 0;
    for (let i = 0; i < d.length; i += 4) { r += d[i]; g += d[i + 1]; b += d[i + 2]; if (d[i] > 200 && d[i + 2] > 200 && d[i + 1] < 90) mag++; if (d[i] + d[i + 1] + d[i + 2] < 24) blk++; }
    const f = v => Math.round(v * 1000) / 1000;
    return { width: c.width, height: c.height, meanRGB: [f(r / n), f(g / n), f(b / n)], magentaFraction: f(mag / n), nearBlackFraction: f(blk / n) };
  }, b64);
}
async function imageDiff(page, a64, b64) {
  return page.evaluate(async (pa, pb) => {
    const load = async s => { const i = new Image(); i.src = 'data:image/png;base64,' + s; await i.decode(); const c = document.createElement('canvas'); c.width = i.width; c.height = i.height; const x = c.getContext('2d'); x.drawImage(i, 0, 0); return x.getImageData(0, 0, c.width, c.height).data; };
    const A = await load(pa), B = await load(pb); if (A.length !== B.length) return { error: 'sizes differ' };
    let sum = 0, over = 0; const n = A.length / 4;
    for (let i = 0; i < A.length; i += 4) { const dr = Math.abs(A[i] - B[i]), dg = Math.abs(A[i + 1] - B[i + 1]), db = Math.abs(A[i + 2] - B[i + 2]); sum += dr + dg + db; if (dr + dg + db > 24) over++; }
    const f = v => Math.round(v * 1000) / 1000;
    return { meanAbsDifferencePerChannel: f(sum / (3 * n)), fractionOver24: f(over / n) };
  }, a64, b64);
}
(async () => {
  const failures = [], summary = {}, shots = {}; const server = await serve(); const port = server.address().port;
  const browser = await chromium.launch({ args: ['--use-gl=angle', '--use-angle=swiftshader', '--enable-unsafe-swiftshader', '--ignore-gpu-blocklist', '--no-sandbox'] });
  const QUALITIES = process.env.REVIEW_QUERY ? [null] : (process.env.REVIEW_QUALITIES || 'medium,low').split(',');   // a heavy street in SOFTWARE GL can need minutes per frame: try the better tier first, fall back to a lighter one
  const BASE = process.env.REVIEW_BASE || '&hour=15.5';
  for (const entry of views) {
    const [view, variant] = entry.split(':'); const label = variant ? `${view}-${variant}` : view; let done = false;
    if (variant && !VARIANT_QUERY[variant]) { failures.push(`${entry}: unknown variant '${variant}'`); continue; }
    for (const q of QUALITIES) {
      const page = await browser.newPage({ viewport: { width: 1280, height: 720 } }); page.setDefaultTimeout(300000); const logs = [], errors = [];
      page.on('console', m => logs.push(m.text())); page.on('pageerror', e => errors.push(String(e)));
      const query = (q === null ? QUERY : `${BASE}&quality=${q}`) + (variant ? VARIANT_QUERY[variant] : '');
      const t0 = Date.now(); await page.goto(`http://127.0.0.1:${port}/index.html?smoke=kanostreet&view=${view}${query}`);
      const has = re => logs.some(l => re.test(l));
      for (let i = 0; i < 300 && !has(/\[KanoReview\] (ready|no review prefab)/); i++) await page.waitForTimeout(1000);
      if (has(/no review prefab/)) { failures.push(`${entry}: the build has no review prefab (the assets, or the ${variant || 'standard'} variant, were not in it)`); await page.close(); done = true; break; }
      if (!has(/\[KanoReview\] ready/)) { failures.push(`${entry} (${q}): the review scene never started`); await page.close(); continue; }
      const loadSeconds = (Date.now() - t0) / 1000;                     // page load to the game's own "ready" line (E0 measurement: loading)
      await page.waitForTimeout(12000);                                 // let streaming, LODs and the sky settle in software GL
      const perf = () => logs.filter(l => l.startsWith('[Perf]') && !l.includes('BUDGET')).slice(-3);
      const lastOf = re => { const m = logs.filter(l => re.test(l)); return m.length ? m[m.length - 1].trim() : null; };
      let buf;
      try { buf = await page.screenshot({ path: `${prefix}-${label}.png`, timeout: 300000 }); }
      catch (e) { summary[`${label}@${q}`] = { failed: String(e).split('\n')[0].slice(0, 200), loadSeconds, perf: perf() }; fs.writeFileSync(`${prefix}-summary.json`, JSON.stringify(summary, null, 1)); console.log(`${label}@${q}: screenshot failed: ${summary[`${label}@${q}`].failed}`); await page.close(); continue; }
      shots[label] = buf.toString('base64');
      const jsHeapMB = await page.evaluate(() => (performance.memory ? Math.round(performance.memory.usedJSHeapSize / 1048576) : null));
      const draws = { total: lastOf(/^\[KanoReview\] draws total=/), kit: lastOf(/^\[KanoReview\] draws kit /), ground: lastOf(/^\[KanoReview\] draws ground /), rest: lastOf(/^\[KanoReview\] draws rest /),
                      byAsset: lastOf(/^\[KanoReview\] draws byAsset/), instancing: lastOf(/^\[KanoReview\] draws kitMaterialsInstancingOn/) };
      summary[label] = { quality: q === null ? 'as given' : q, loadSeconds, jsHeapMB, budgetWarnings: logs.filter(l => l.includes('BUDGET EXCEEDED')).slice(-2), perf: perf(), draws, render: logs.filter(l => l.startsWith('[Render]')).slice(-2), review: logs.filter(l => l.startsWith('[KanoReview] ready')), errors };
      fs.writeFileSync(`${prefix}-summary.json`, JSON.stringify(summary, null, 1));
      if (errors.length) failures.push(`${label}: page errors: ${errors.slice(0, 2).join(' | ')}`);
      console.log(`${label}@${q}: saved ${prefix}-${label}.png; ${JSON.stringify(summary[label].perf.slice(-1))}`);
      await page.close(); done = true; break;
    }
    if (!done) failures.push(`${entry}: no screenshot at any quality (${QUALITIES.join(',')})`);
  }
  // measurements of the pictures (the pictures themselves stay in the artifact; only numbers go to the log)
  try {
    const mp = await browser.newPage(); await mp.setContent('<html><body></body></html>');
    for (const label of Object.keys(shots)) if (summary[label]) summary[label].image = await imageStats(mp, shots[label]);
    for (const label of Object.keys(shots)) { const m = label.match(/^(.*)-merged$/); if (m && shots[m[1]] && summary[label]) summary[label].differenceFromStandard = await imageDiff(mp, shots[m[1]], shots[label]); }
    await mp.close();
  } catch (e) { failures.push('image measurements failed: ' + String(e).split('\n')[0]); }
  await browser.close(); server.close(); fs.writeFileSync(`${prefix}-summary.json`, JSON.stringify(summary, null, 1));
  if (failures.length) { console.error('FAILURES:\n' + failures.join('\n')); process.exit(1); }
})();
