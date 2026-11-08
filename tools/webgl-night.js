// The night environment, checked in the real WebGL build. Usage:  node tools/webgl-night.js <build-dir> <out-prefix> [scenario,scenario,...]      (self-test of the image analysis: --selftest)
//
// Scenarios (each opens the real game with ?smoke=drive&hold=1 at a place on the route, at night, presses the REAL keys where a key is the thing under test, and reads both the pixels and the game's own log lines):
//   beams       low beam against high beam on the same dark stretch (key K), bumper view: the road ahead must get brighter
//   rear        rear lamps: lights on, brake (Space), lights off (L), hazard lights (T), left and right indicators (Q, E): burst of frames per blinking state + the game's [Light] rear edges
//   beacons     the red mast beacons 110 m before a telecom mast that stands 58 to 73 m beside the road (150 m gave 1 to 4 red pixels; 70 m put the mast outside the picture, so the route's real mast position was computed): burst of frames over more than one flash cycle
//   traffic     ambient traffic with its head and tail lamps: a slow burst on the expressway + the [Traffic] lines
//   transition  a whole dusk (hour 16.6 on) and a whole dawn (hour 4.8 on) filmed in one run each (?hourrate=): the lamp level against the hour, the headlight auto switching
//   wet         rain against dry on the Low, Medium and High tiers: streaks, puddles, and the change of the road
//   pools       the tuned lamp pools on the Low tier in five places
// Output: <prefix>-sheet-<scenario>.png (contact sheets, tiles in the order of the log), <prefix>-night-report.json and a PASS / FAIL list on stdout. FAIL means a check of this script failed; it does NOT mean a picture looks wrong or right:
// the pictures still have to be looked at. NIGHT_GPU=1 runs the same suite on the machine's own GPU (hardware GL, not SwiftShader), prints the GL renderer and the game's own frame times, and so is the real-GPU test.
const http = require('http'), fs = require('fs'), path = require('path');

// ---- image analysis (pure; --selftest checks it on synthetic frames) -------------------------------------------------------------------------------
const luma = (r, g, b) => 0.2126 * r + 0.7152 * g + 0.0722 * b;
function region(png, [x0, y0, x1, y1], f) {
  const X0 = Math.max(0, Math.floor(x0 * png.width)), X1 = Math.min(png.width, Math.ceil(x1 * png.width)), Y0 = Math.max(0, Math.floor(y0 * png.height)), Y1 = Math.min(png.height, Math.ceil(y1 * png.height));
  for (let y = Y0; y < Y1; y++) for (let x = X0; x < X1; x++) { const i = (y * png.width + x) * 4; f(png.data[i], png.data[i + 1], png.data[i + 2], x, y); }
}
function meanLuma(png, r) { let n = 0, s = 0; region(png, r, (R, G, B) => { s += luma(R, G, B); n++; }); return n ? s / n : NaN; }
const isRed = (r, g, b) => r >= 190 && g < 80 && b < 80;                 // a lit red lamp (brake, tail, beacon)
const isOrange = (r, g, b) => r >= 200 && g >= 95 && g <= 200 && b < 100 && g < r * 0.85;   // a lit amber indicator
function count(png, r, pred) { let n = 0; region(png, r, (R, G, B) => { if (pred(R, G, B)) n++; }); return n; }
function sheet(pngs, cols, tw, th) {
  const { PNG } = require('pngjs'); const rows = Math.ceil(pngs.length / cols), out = new PNG({ width: cols * tw, height: rows * th });
  out.data.fill(24);
  pngs.forEach((p, k) => { const ox = (k % cols) * tw, oy = Math.floor(k / cols) * th; for (let y = 0; y < th; y++) for (let x = 0; x < tw; x++) { const sx = Math.min(p.width - 1, Math.floor(x * p.width / tw)), sy = Math.min(p.height - 1, Math.floor(y * p.height / th)), si = (sy * p.width + sx) * 4, di = ((oy + y) * out.width + ox + x) * 4; out.data[di] = p.data[si]; out.data[di + 1] = p.data[si + 1]; out.data[di + 2] = p.data[si + 2]; out.data[di + 3] = 255; } });
  return out;
}
// the lamp level the unit-tested NightLighting.LampFactor gives for an hour (clear sky): used to check that the running game follows the same function
function lampFactor(hour, overcast = 0) {
  const elev = Math.sin((hour - 6) / 12 * Math.PI), shift = 0.10 * overcast, start = 0.16 + shift, full = -0.04 + shift;
  let t = (start - elev) / (start - full); t = Math.max(0, Math.min(1, t)); return t * t * (3 - 2 * t);
}

// ---- checks ---------------------------------------------------------------------------------------------------------------------------------
const results = [];
function check(scenario, name, ok, detail) { results.push({ scenario, name, ok: !!ok, detail }); console.log(`  ${ok ? 'PASS' : 'FAIL'}  ${scenario}: ${name}${detail ? ' (' + detail + ')' : ''}`); }

// ---- the browser run --------------------------------------------------------------------------------------------------------------------------
const MIME = { '.html': 'text/html', '.js': 'text/javascript', '.css': 'text/css', '.wasm': 'application/wasm', '.json': 'application/json', '.png': 'image/png', '.ico': 'image/x-icon', '.gz': 'application/octet-stream', '.data': 'application/octet-stream' };
function serve(dir) {
  return new Promise(resolve => {
    const s = http.createServer((req, res) => {
      let p = decodeURIComponent(req.url.split('?')[0]); if (p.endsWith('/')) p += 'index.html';
      const f = path.join(dir, p);
      if (!f.startsWith(dir) || !fs.existsSync(f) || fs.statSync(f).isDirectory()) { res.writeHead(404); res.end('not found'); return; }
      res.writeHead(200, { 'Content-Type': MIME[path.extname(f)] || 'application/octet-stream' }); fs.createReadStream(f).pipe(res);
    }).listen(0, '127.0.0.1', () => resolve(s));
  });
}

async function openGame(env, label, params, quality) {
  const { PNG } = require('pngjs');
  const page = await env.browser.newPage({ viewport: { width: env.gpu ? 1280 : 1024, height: env.gpu ? 720 : 576 } });
  const logs = [], errors = []; page.on('console', m => logs.push({ t: Date.now(), text: m.text() })); page.on('pageerror', e => errors.push(String(e)));
  const find = (re, from = 0) => logs.slice(from).find(l => re.test(l.text));
  const waitFor = async (re, secs, from = 0) => { for (let i = 0; i < secs * 4; i++) { const l = find(re, from); if (l) return l; await page.waitForTimeout(250); } return null; };
  console.log(`\n=== ${label} (${quality}${params ? ', ' + params : ''}) ===`);
  await page.goto(`http://127.0.0.1:${env.port}/index.html?smoke=drive&hold=1&quality=${quality}&${params}`);
  const g = { page, logs, errors, find, waitFor, label, ok: false, PNG };
  if (!await waitFor(/\[Smoke\] drive scenario/, 150)) { console.log('  the build did not start the drive scenario'); await page.close(); return g; }
  if (!await waitFor(/\[Drive\] ground ready/, 90)) { console.log('  no ground under the truck'); await page.close(); return g; }
  if (!await waitFor(/\[Drive\] smoke hold: vehicle frozen/, 60)) { console.log('  the truck was not frozen'); await page.close(); return g; }
  await page.waitForTimeout(4000); await page.mouse.click(env.gpu ? 640 : 512, env.gpu ? 360 : 288);
  g.ok = true;
  g.shot = async () => PNG.sync.read(await page.screenshot());
  g.press = async (k, delay = 120) => { await page.keyboard.press(k, { delay }); };
  g.burst = async (n, gapMs) => { const out = []; for (let i = 0; i < n; i++) { out.push(await g.shot()); if (gapMs) await page.waitForTimeout(gapMs); } return out; };
  g.last = re => [...logs].reverse().find(l => re.test(l.text));
  g.lines = re => logs.filter(l => re.test(l.text)).map(l => l.text);
  g.report = () => { for (const re of [/^\[Lamp\] init/, /^\[Lamp\] level=/, /^\[Weather\]/, /^\[Render\] tier=/]) { const l = re.source.includes('level') ? g.last(re) : find(re); if (l) console.log('  ' + l.text.slice(0, 420)); } };
  g.close = async () => { await page.close(); };
  return g;
}
const lampVal = (line, key) => { const m = new RegExp(key + '=([-\\d.]+)').exec(line || ''); return m ? +m[1] : NaN; };

async function sc_beams(env) {
  const g = await openGame(env, 'beams: low beam against high beam, dark road', 'hour=night&start=24000', env.q); if (!g.ok) { check('beams', 'the scenario ran', false, 'the game did not start'); return; }
  for (let i = 0; i < 3; i++) { const mark = g.logs.length; await g.press('8', 350); if (await g.waitFor(/\[Camera\] view=FrontBumper number=8 /, 5, mark)) break; }
  await g.page.waitForTimeout(2500); const low = await g.shot(); g.report();
  const lowBeam = g.last(/^\[Light\] beam=/);
  const mark = g.logs.length; await g.press('k', 200); const high = await g.waitFor(/^\[Light\] beam=High/, 10, mark); await g.page.waitForTimeout(2500); const hi = await g.shot();
  const band = (p, y0, y1) => meanLuma(p, [0.38, y0, 0.62, y1]);
  const bands = [[0.46, 0.52], [0.52, 0.58], [0.58, 0.66], [0.66, 0.78]];
  const lowB = bands.map(b => band(low, ...b)), hiB = bands.map(b => band(hi, ...b));
  console.log('  road column luminance, far to near: low ' + lowB.map(v => v.toFixed(1)).join(' ') + ' | high ' + hiB.map(v => v.toFixed(1)).join(' '));
  check('beams', 'the K key switches the game to the high beam', !!high, high ? high.text : 'no [Light] beam=High line after K');
  check('beams', 'the road ahead is brighter with the high beam (far bands)', hiB[0] + hiB[1] > lowB[0] + lowB[1] + 1.0, `far two bands ${(lowB[0] + lowB[1]).toFixed(1)} -> ${(hiB[0] + hiB[1]).toFixed(1)}`);
  check('beams', 'the whole frame is not darker with the high beam', meanLuma(hi, [0, 0, 1, 1]) >= meanLuma(low, [0, 0, 1, 1]) - 0.5, '');
  const m2 = g.logs.length; await g.press('k', 200); const back = await g.waitFor(/^\[Light\] beam=Low/, 10, m2);
  check('beams', 'K again returns to the low beam', !!back, back ? back.text : 'no [Light] beam=Low line after the second K');
  env.sheets.beams = [low, hi]; await g.close();
}

async function sc_rear(env) {
  const g = await openGame(env, 'rear lamps, brake, hazards and indicators on a lit street', 'hour=night&start=3600', env.q); if (!g.ok) { check('rear', 'the scenario ran', false, 'the game did not start'); return; }
  g.report(); const R = [0.40, 0.58, 0.60, 0.68], tiles = [];
  const shotState = async (name, f) => { const p = await g.shot(); const red = count(p, R, isRed), orange = count(p, R, isOrange); console.log(`  ${name}: red lamp pixels ${red}, amber pixels ${orange}`); tiles.push(p); return { red, orange, p }; };
  const on = await shotState('lights on', 0);
  await g.page.keyboard.down('Space'); await g.page.waitForTimeout(1800); const brake = await shotState('brake (Space held)', 0); await g.page.keyboard.up('Space'); await g.page.waitForTimeout(800);
  check('rear', 'the brake lamps are brighter than the rear lamps', brake.red > on.red * 1.2 && brake.red >= on.red + 4, `red pixels ${on.red} -> ${brake.red}`);
  const brakeEdges = g.lines(/^\[Light\] rear .*brakeLamp=True/).length; check('rear', 'the game logged the brake lamp turning on (Space)', brakeEdges >= 1, `${brakeEdges} line(s)`);
  await g.press('l', 200); await g.page.waitForTimeout(1800); const off = await shotState('lights off (L)', 0);
  check('rear', 'the rear lamps go dark with the lights off', off.red < on.red * 0.4, `red pixels ${on.red} -> ${off.red}`);
  await g.press('l', 200); await g.page.waitForTimeout(1500);
  const blink = async (name, key, wantL, wantR) => {
    const m0 = g.logs.length; await g.press(key, 200); const fr = await g.burst(10, 90); tiles.push(fr[0], fr[3], fr[6], fr[9]);
    const lit = fr.map(p => count(p, R, isOrange)); const bright = lit.filter(n => n >= 2).length, dark = lit.filter(n => n <= 1).length;   // one rear lamp is 2 to 4 pixels in this view (run 317: 2 to 3 lit pixels on the left lamp); a dark frame is 0 to 1, nothing else on the road is that amber
    const edges = g.logs.slice(m0).map(l => l.text).filter(t => /^\[Light\] rear /.test(t));
    const L = edges.filter(t => /indicatorLeft=True/.test(t)).length, Rr = edges.filter(t => /indicatorRight=True/.test(t)).length, off = edges.filter(t => /indicatorLeft=False indicatorRight=False/.test(t)).length;
    console.log(`  ${name}: amber pixels per frame ${lit.join(' ')} | log edges left-on ${L}, right-on ${Rr}, both-off ${off}`);
    check('rear', `${name}: the game's lamps flash (on and off edges in the log)`, (!wantL || L >= 1) && (!wantR || Rr >= 1) && off >= 1, `left-on ${L}, right-on ${Rr}, off ${off}`);
    check('rear', `${name}: the side that should not flash stays dark`, (wantL || L === 0) && (wantR || Rr === 0), `left-on ${L}, right-on ${Rr}`);
    check('rear', `${name}: the picture shows amber lamps in some frames and none in others`, bright >= 1 && dark >= 1, `${bright} lit frame(s), ${dark} dark`);
    await g.press(key, 200); await g.page.waitForTimeout(900);
  };
  await blink('hazard lights (T)', 't', true, true); await blink('left indicator (Q)', 'q', true, false); await blink('right indicator (E)', 'e', false, true);
  env.sheets.rear = tiles; await g.close();
}

async function sc_beacons(env) {
  const g = await openGame(env, 'red beacons on a telecom mast, 110 m before it (the mast stands 58 to 73 m beside the road at s = 13782)', 'hour=night&start=13672', env.q); if (!g.ok) { check('beacons', 'the scenario ran', false, 'the game did not start'); return; }
  g.report(); const lamp = g.last(/^\[Lamp\] level=/); check('beacons', 'the game has beacons loaded', lampVal(lamp && lamp.text, 'beacons') >= 2, lamp ? lamp.text.match(/beacons=\d+/) : 'no [Lamp] line');
  const fr = await g.burst(16, 120); const upper = [0.0, 0.0, 1.0, 0.55]; const reds = fr.map(p => count(p, upper, isRed));
  console.log('  bright red pixels in the upper frame, 16 frames: ' + reds.join(' '));
  check('beacons', 'red beacon light appears in some frames', Math.max(...reds) >= 3, `max ${Math.max(...reds)}`);
  check('beacons', 'and is absent in others (it flashes)', Math.min(...reds) < Math.max(...reds) * 0.5 && Math.max(...reds) >= 3, `min ${Math.min(...reds)}, max ${Math.max(...reds)}`);
  env.sheets.beacons = [fr[0], fr[2], fr[4], fr[6], fr[8], fr[10], fr[12], fr[14]]; await g.close();
}

async function sc_traffic(env) {
  const g = await openGame(env, 'ambient traffic with lit lamps, expressway', 'hour=night&start=11500', env.q); if (!g.ok) { check('traffic', 'the scenario ran', false, 'the game did not start'); return; }
  g.report(); const tiles = [];
  for (let i = 0; i < 12; i++) { tiles.push(await g.shot()); await g.page.waitForTimeout(2500); }
  const tl = g.lines(/^\[Traffic\]/); console.log('  ' + (tl.slice(-2).join('\n  ') || 'no [Traffic] lines'));
  check('traffic', 'traffic bodies existed during the run', tl.some(t => lampVal(t, 'active') >= 1), `${tl.length} [Traffic] line(s)`);
  check('traffic', 'traffic bodies carry head and tail lamp boxes with the shared lit material', tl.some(t => lampVal(t, 'lampBoxes') >= 4 && /lampMaterials=ok/.test(t)), tl.slice(-1)[0] || '');
  const init = g.find(/^\[Lamp\] init/); check('traffic', 'the lamp system reports traffic lamps available', !!init && /trafficLamps=ok/.test(init.text), init ? (init.text.match(/trafficLamps=\w+/) || [''])[0] : '');
  env.sheets.traffic = tiles; await g.close();
}

async function sc_transition(env, name, startHour, label) {
  const g = await openGame(env, `${label}: ${name}`, `hour=${startHour}&hourrate=0.04&start=3600`, env.q); if (!g.ok) { check('transition', `${name}: the scenario ran`, false, 'the game did not start'); return; }
  g.report(); const tiles = [], t0 = Date.now(); const points = [];
  for (let i = 0; i < 20; i++) { tiles.push(await g.shot()); await g.page.waitForTimeout(2800); }
  const lamps = g.lines(/^\[Lamp\] level=/); for (const l of lamps) { const h = lampVal(l, 'hour'), lv = lampVal(l, 'level'); if (isFinite(h)) points.push([h, lv]); }
  console.log('  (hour, level) from the game: ' + points.map(p => `${p[0].toFixed(2)}:${p[1].toFixed(2)}`).join('  '));
  const dusk = startHour > 12; let monotone = true, matches = true, worst = 0;
  for (let i = 1; i < points.length; i++) { const d = points[i][1] - points[i - 1][1]; if (dusk ? d < -0.02 : d > 0.02) monotone = false; }
  for (const [h, lv] of points) { const e = Math.abs(lv - lampFactor(h % 24)); worst = Math.max(worst, e); if (e > 0.06) matches = false; }
  check('transition', `${name}: the lamp level moves one way only`, points.length >= 5 && monotone, `${points.length} samples`);
  check('transition', `${name}: the lamp level is the one the unit-tested model gives for the game's hour`, points.length >= 5 && matches, `worst difference ${worst.toFixed(3)}`);
  const lo = Math.min(...points.map(p => p[1])), hi = Math.max(...points.map(p => p[1]));
  check('transition', `${name}: it covers the whole range (day to night)`, hi - lo > 0.9, `level ${lo.toFixed(2)} to ${hi.toFixed(2)}`);
  const auto = g.lines(/^\[Light\] lamps auto on=/); console.log('  ' + (auto.join('\n  ') || 'no automatic lamp switching logged'));
  const first = dusk ? auto.find(t => /on=True/.test(t)) : [...auto].reverse().find(t => /on=False/.test(t)); const hh = lampVal(first, 'hour');
  check('transition', `${name}: the headlights switch automatically ${dusk ? 'on' : 'off'} inside the lamp ramp`, !!first && new RegExp('on=' + (dusk ? 'True' : 'False')).test(first) && (dusk ? hh > 17.2 && hh < 18.5 : hh > 5.5 && hh < 6.6), first ? `hour ${hh}` : 'no event');
  const head = g.lines(/^\[Light\] headlights on=/); console.log('  headlight lines: ' + head.map(h => h.slice(0, 30)).join(' | '));
  env.sheets['transition-' + name] = tiles; await g.close();
}

async function sc_wet(env) {
  const tiers = ['low', 'medium', 'high'], tiles = [], notes = {};
  for (const t of tiers) for (const w of ['clear', 'rain']) {
    const g = await openGame(env, `wet road, ${t} tier, ${w}`, `hour=night&start=3600&weather=${w}`, t); if (!g.ok) { check('wet', `${t}/${w}: the scenario ran`, false, 'the game did not start (software GL may be too slow for this tier)'); continue; }
    g.report(); const p = await g.shot(); tiles.push(p); const lamp = g.last(/^\[Lamp\] level=/); notes[t + '/' + w] = { lamp: lamp && lamp.text, road: meanLuma(p, [0.25, 0.62, 0.75, 0.90]) };
    const perf = g.lines(/\[Perf\].*avgFrameMs=/).slice(-3).map(l => +/avgFrameMs=([\d.]+)/.exec(l)[1]); if (perf.length) console.log(`  game frame time ${(perf.reduce((a, b) => a + b, 0) / perf.length).toFixed(0)} ms (${env.gpu ? 'real GPU' : 'software GL: not a GPU measurement'})`);
    await g.close();
  }
  for (const t of tiers) {
    const r = notes[t + '/rain'], d = notes[t + '/clear']; if (!r || !d) continue;
    check('wet', `${t}: rain makes the game draw streaks and puddles`, lampVal(r.lamp, 'streaks') > 0 && lampVal(r.lamp, 'puddles') > 0 && /puddleDraw=on/.test(r.lamp) && lampVal(r.lamp, 'wet') >= 0.85, (r.lamp || '').match(/streaks=\d+ glowVerts=\d+ puddles=\d+ wet=[\d.]+/) + '');
    check('wet', `${t}: dry weather draws no puddles`, /puddleDraw=off/.test(d.lamp) && lampVal(d.lamp, 'wet') === 0, '');
    check('wet', `${t}: the road in the picture changes in rain`, Math.abs(r.road - d.road) > 1.5, `road luminance ${d.road.toFixed(1)} -> ${r.road.toFixed(1)}`);
  }
  env.sheets.wet = tiles;
}

async function sc_pools(env) {
  const tiles = [];
  for (const [n, p] of [['depot', ''], ['residential', '&start=5600'], ['commercial', '&start=3600'], ['expressway', '&start=11500'], ['village', '&start=34000']]) {
    const g = await openGame(env, `pools: ${n}`, `hour=night${p}`, env.q); if (!g.ok) continue; g.report(); tiles.push(await g.shot()); await g.close();
  }
  env.sheets.pools = tiles; check('pools', 'five frames taken', tiles.length === 5, `${tiles.length} frame(s); look at the sheet`);
}

function selftest() {
  const { PNG } = require('pngjs'); const mk = (w, h, f) => { const p = new PNG({ width: w, height: h }); for (let y = 0; y < h; y++) for (let x = 0; x < w; x++) { const [r, g, b] = f(x, y); const i = (y * w + x) * 4; p.data[i] = r; p.data[i + 1] = g; p.data[i + 2] = b; p.data[i + 3] = 255; } return p; };
  let bad = 0; const ok = (c, m) => { if (!c) { bad++; console.log('SELFTEST FAIL: ' + m); } };
  const black = mk(100, 100, () => [0, 0, 0]), red = mk(100, 100, (x, y) => (x > 40 && x < 50 && y > 40 && y < 50 ? [255, 10, 10] : [0, 0, 0])), amber = mk(100, 100, (x, y) => (x > 40 && x < 50 && y > 40 && y < 50 ? [255, 150, 30] : [0, 0, 0]));
  ok(count(black, [0, 0, 1, 1], isRed) === 0 && count(red, [0, 0, 1, 1], isRed) === 81 && count(red, [0, 0, 1, 1], isOrange) === 0, 'red detection');
  ok(count(amber, [0, 0, 1, 1], isOrange) === 81 && count(amber, [0, 0, 1, 1], isRed) === 0, 'amber detection');
  ok(Math.abs(meanLuma(mk(10, 10, () => [100, 100, 100]), [0, 0, 1, 1]) - 100) < 0.01, 'mean luma');
  ok(count(red, [0, 0, 0.3, 0.3], isRed) === 0 && count(red, [0.35, 0.35, 0.55, 0.55], isRed) === 81, 'regions');
  const sh = sheet([red, amber, black], 2, 50, 50); ok(sh.width === 100 && sh.height === 100, 'sheet size');
  ok(lampFactor(12) === 0 && lampFactor(23) === 1 && lampFactor(17.6) > 0.1 && lampFactor(17.6) < 0.35 && Math.abs(lampFactor(6.4) - lampFactor(17.6)) < 1e-6, 'lamp factor');
  ok(lampFactor(17.2, 1) > lampFactor(17.2, 0), 'overcast moves the ramp earlier');
  console.log(bad ? `selftest: ${bad} failure(s)` : 'selftest: ok'); process.exit(bad ? 1 : 0);
}

async function main() {
  if (process.argv.includes('--selftest')) return selftest();
  const dir = path.resolve(process.argv[2] || 'Builds/WebGL'), prefix = process.argv[3] || 'webgl-light-nt', want = (process.argv[4] || 'beams,rear,beacons,traffic,transition,wet,pools').split(',');
  const { chromium } = require('playwright'); const { PNG } = require('pngjs'); const gpu = !!process.env.NIGHT_GPU;
  const server = await serve(dir), port = server.address().port;
  const args = gpu ? ['--ignore-gpu-blocklist', '--enable-gpu-rasterization', '--no-sandbox'] : ['--use-gl=angle', '--use-angle=swiftshader', '--enable-unsafe-swiftshader', '--ignore-gpu-blocklist', '--no-sandbox'];
  const browser = await chromium.launch({ headless: !process.env.NIGHT_HEADED, args });
  const env = { browser, port, gpu, q: (process.env.NIGHT_QUALITY || (gpu ? 'high' : 'low')).toLowerCase(), sheets: {} };
  { const pg = await browser.newPage(); const r = await pg.evaluate(() => { const c = document.createElement('canvas'), gl = c.getContext('webgl2') || c.getContext('webgl'); if (!gl) return 'no WebGL'; const e = gl.getExtension('WEBGL_debug_renderer_info'); return e ? gl.getParameter(e.UNMASKED_RENDERER_WEBGL) : gl.getParameter(gl.RENDERER); }); console.log(`GL renderer: ${r}  (${gpu ? 'NIGHT_GPU=1: hardware GL requested' : 'software GL (SwiftShader) requested'})`); env.glRenderer = r; await pg.close(); }
  const run = { beams: () => sc_beams(env), rear: () => sc_rear(env), beacons: () => sc_beacons(env), traffic: () => sc_traffic(env), transition: async () => { await sc_transition(env, 'dusk', 16.6, 'a whole dusk'); await sc_transition(env, 'dawn', 4.8, 'a whole dawn'); }, wet: () => sc_wet(env), pools: () => sc_pools(env) };
  for (const s of want) { try { await run[s](); } catch (e) { check(s, 'the scenario completed without a script error', false, String(e).slice(0, 200)); } }
  for (const [k, v] of Object.entries(env.sheets)) { if (!v || !v.length) continue; const cols = Math.min(v.length, k.startsWith('transition') ? 5 : k === 'traffic' ? 4 : k === 'rear' ? 4 : k === 'beacons' ? 4 : k === 'pools' ? 3 : 2); fs.writeFileSync(`${prefix}-sheet-${k}.png`, PNG.sync.write(sheet(v, cols, 512, 288))); }
  fs.writeFileSync(`${prefix}-night-report.json`, JSON.stringify({ glRenderer: env.glRenderer, gpu, quality: env.q, results }, null, 1));
  const failed = results.filter(r => !r.ok); console.log(`\n${results.length - failed.length} of ${results.length} checks passed` + (failed.length ? '; FAILED: ' + failed.map(f => `${f.scenario}: ${f.name}`).join(' | ') : '')); await browser.close(); server.close(); process.exit(failed.length ? 1 : 0);
}
main().catch(e => { console.error(e); process.exit(2); });
