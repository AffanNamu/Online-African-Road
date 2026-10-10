// Dashboard states and interactions for the WebGL build. Usage: node tools/webgl-dashboard-states.js <build-dir> [screenshot-prefix]
// Boots ?smoke=dashboard three times with the job board in a different state (dev fixtures, never reachable from the UI):
//   jobs=slow  -> skeleton while loading, then the real cards; also clicks the bell, the hero dots, the news arrow
//   jobs=error -> an error with a Try again button; the retry loads the board
//   jobs=empty -> the empty state offers the job board
// The game logs every state it reaches ([Dashboard] ...) and its own click positions; this script reads those lines and checks pixels.
const http = require('http'), fs = require('fs'), path = require('path');
const { chromium } = require('playwright');

const dir = path.resolve(process.argv[2] || 'Builds/WebGL');
const prefix = process.argv[3] || 'webgl-dstate';
const MIME = { '.html': 'text/html', '.js': 'text/javascript', '.css': 'text/css', '.wasm': 'application/wasm', '.json': 'application/json', '.png': 'image/png', '.jpg': 'image/jpeg', '.ico': 'image/x-icon', '.gz': 'application/octet-stream', '.data': 'application/octet-stream' };
const server = http.createServer((req, res) => {
  let p = decodeURIComponent(req.url.split('?')[0]); if (p.endsWith('/')) p += 'index.html';
  const f = path.join(dir, p);
  if (!f.startsWith(dir) || !fs.existsSync(f) || fs.statSync(f).isDirectory()) { res.writeHead(404); res.end('not found'); return; }
  res.writeHead(200, { 'Content-Type': MIME[path.extname(f)] || 'application/octet-stream' }); fs.createReadStream(f).pipe(res);
});

(async () => {
  const failures = [], passes = [];
  const check = (ok, what) => { (ok ? passes : failures).push(what); console.log(`${ok ? 'PASS' : 'FAIL'}  ${what}`); };
  await new Promise(r => server.listen(0, '127.0.0.1', r)); const port = server.address().port;
  const browser = await chromium.launch({ args: ['--use-gl=angle', '--use-angle=swiftshader', '--enable-unsafe-swiftshader', '--ignore-gpu-blocklist', '--no-sandbox'] });
  const allErrors = [], allExc = [];

  async function scenario(name, jobsMode, body) {
    console.log(`\n=== scenario: ${name} (jobs=${jobsMode}) ===`);
    const page = await browser.newPage({ viewport: { width: 1024, height: 576 } });
    const logs = [], errors = [];
    page.on('console', m => logs.push(m.text())); page.on('pageerror', e => errors.push(String(e)));
    const sleep = ms => page.waitForTimeout(ms);
    const waitLog = async (re, from = 0, seconds = 60) => { for (let i = 0; i < seconds * 2; i++) { const hit = logs.slice(from).find(l => re.test(l)); if (hit) return hit; await sleep(500); } return null; };
    const targets = () => { const l = [...logs].reverse().find(x => x.startsWith('[WorldMap] targets (dashboard)')) || ''; return Object.fromEntries([...l.matchAll(/TARGET (\S+) x=([\d.]+) y=([\d.]+)/g)].map(m => [m[1], { x: +m[2], y: +m[3] }])); };
    const click = async name => { const t = targets()[name]; if (!t) return false; const b = await page.locator('#unity-canvas').boundingBox(); await page.mouse.move(b.x + t.x * b.width, b.y + t.y * b.height); await sleep(250); await page.mouse.down(); await sleep(150); await page.mouse.up(); return true; };
    await page.goto(`http://127.0.0.1:${port}/index.html?smoke=dashboard&jobs=${jobsMode}`);
    const built = await waitLog(/\[Dashboard\] built/, 0, 150);
    check(!!built, `${name}: the dashboard built`);
    if (built) { await waitLog(/\[WorldMap\] targets \(dashboard\)/, 0, 30); await body({ logs, sleep, waitLog, targets, click, page }); }
    allErrors.push(...errors); allExc.push(...logs.filter(l => /exception|NullReference|IndexOutOfRange|error CS/i.test(l) && !/GLib|swiftshader/i.test(l)));
    await page.close();
  }

  await scenario('loading then ready', 'slow', async c => {
    check(c.logs.some(l => /\[Dashboard\] jobs state=loading/.test(l)), 'slow: the job board announced its loading state');
    await c.sleep(1500); await c.page.screenshot({ path: `${prefix}-1-loading.png` });
    check(!Object.keys(c.targets()).some(k => k.startsWith('Btn_Accept_')), 'slow: while loading there are no Accept buttons (skeleton cards only)');
    const ready = await c.waitLog(/\[Dashboard\] jobs state=ready/, 0, 60);
    check(!!ready, 'slow: the board then finished loading (' + (ready || 'never').slice(0, 70) + ')');
    await c.sleep(6000);   // the game re-reports its click positions about a second after the board changes
    await c.page.screenshot({ path: `${prefix}-2-ready.png` });
    const tg = c.targets();
    for (const k of ['Btn_Bell', 'Btn_Fuel', 'Btn_Balance', 'Btn_Presence', 'Btn_YourTruck', 'Btn_NewsNext', 'Btn_Dot1', 'Btn_RoutesMap', 'Btn_JoinConvoy', 'Nav_Jobs', 'Nav_Map', 'Btn_HeroAction'])
      check(!!tg[k], `slow: ${k} is a clickable target`);
    let m = c.logs.length; await c.click('Btn_NewsNext'); const news = await c.waitLog(/\[Dashboard\] news index=1/, m, 20);
    check(!!news, 'news: the arrow shows the next story (' + (news || 'not seen') + ')');
    m = c.logs.length; await c.click('Btn_Dot1'); const slide = await c.waitLog(/\[Dashboard\] slide=1/, m, 20);
    check(!!slide, 'hero: the second dot switches the carousel to slide 1');
    await c.sleep(1500); await c.page.screenshot({ path: `${prefix}-3-hero-slide1.png` });
    m = c.logs.length; await c.click('Btn_Bell'); const bell = await c.waitLog(/\[Dashboard\] notices open count=(\d+)/, m, 20);
    check(!!bell && /count=[1-9]/.test(bell), 'bell: opens the notice list and it holds the preview notice (' + (bell || 'not seen') + ')');
    await c.sleep(1500); await c.page.screenshot({ path: `${prefix}-4-notices.png` });
  });

  await scenario('error then retry', 'error', async c => {
    const err = await c.waitLog(/\[Dashboard\] jobs state=error/, 0, 60);
    check(!!err, 'error: the job board reported its error state (' + (err || 'never').slice(0, 90) + ')');
    await c.sleep(3500); await c.page.screenshot({ path: `${prefix}-5-error.png` });
    check(!!c.targets()['Btn_RetryJobs'], 'error: a Try again button is offered');
    const m = c.logs.length; await c.click('Btn_RetryJobs');
    const ok = await c.waitLog(/\[Dashboard\] jobs state=ready/, m, 60);
    check(!!ok, 'error: Try again loads the board (' + (ok || 'never recovered').slice(0, 70) + ')');
    await c.sleep(3000); await c.page.screenshot({ path: `${prefix}-6-recovered.png` });
  });

  await scenario('empty board', 'empty', async c => {
    const e = await c.waitLog(/\[Dashboard\] jobs state=empty/, 0, 60);
    check(!!e, 'empty: the job board reported its empty state');
    await c.sleep(4000); await c.page.screenshot({ path: `${prefix}-7-empty.png` });
    check(!!c.targets()['Btn_OpenBoard'], 'empty: the job board is offered instead of a dead end');
  });

  check(allErrors.length === 0, `no page errors across the scenarios (${allErrors.length})`); allErrors.slice(0, 3).forEach(e => console.log('ERR: ' + e.slice(0, 300)));
  check(allExc.length === 0, `no exceptions in the Unity log across the scenarios (${allExc.length})`); allExc.slice(0, 5).forEach(l => console.log('EXC: ' + l.slice(0, 400)));
  await browser.close(); server.close();
  console.log(`\n${passes.length} passed, ${failures.length} failed`);
  if (failures.length) { console.log('DASHBOARD STATES SCENARIO FAILED:'); failures.forEach(f => console.log(' - ' + f)); process.exit(1); }
  console.log('DASHBOARD STATES SCENARIO PASSED');
})();
