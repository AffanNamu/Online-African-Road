// Browser end-to-end test: the REAL WebGL build against a REAL backend (PostgREST + Postgres through tools/e2e/server.js).
// Usage: source /tmp/aro-e2e.env && node tools/webgl-e2e.js [screenshot-prefix]      (tools/e2e/up.sh started the backend and serves the build)
// Every dashboard value on screen is checked against what the DATABASE holds; every action is checked in the database afterwards.
// Failure states are exercised by injecting request failures (?fault=...), never by injecting data.
const crypto = require('crypto'), { execFileSync } = require('child_process');
const { chromium } = require('playwright');

const BASE = process.env.E2E_URL, ANON = process.env.E2E_ANON_KEY, DB_URL = process.env.E2E_DB_URL, SECRET = process.env.PGRST_JWT_SECRET;
if (!BASE || !ANON || !DB_URL) { console.error('source /tmp/aro-e2e.env first (tools/e2e/up.sh)'); process.exit(2); }
const prefix = process.argv[2] || 'webgl-e2e';
const failures = [], passes = [];
const check = (ok, what, detail) => { (ok ? passes : failures).push(what); console.log(`${ok ? 'PASS' : 'FAIL'}  ${what}${!ok && detail !== undefined ? '  -> ' + String(detail).slice(0, 300) : ''}`); };
const db = sql => execFileSync('psql', [DB_URL, '-X', '-q', '-At', '-v', 'ON_ERROR_STOP=1', '-c', sql], { encoding: 'utf8' }).trim();
const sleep = ms => new Promise(r => setTimeout(r, ms));

async function signup(name) {
  const email = name.toLowerCase() + '@e2e.test', password = 'pw-' + name + '-123';
  const r = await fetch(BASE + '/auth/v1/signup', { method: 'POST', headers: { 'content-type': 'application/json' }, body: JSON.stringify({ email, password, data: { display_name: name } }) });
  const j = await r.json(); if (r.status !== 200) throw new Error('signup ' + name + ': ' + JSON.stringify(j));
  return { name, email, password, id: j.user.id };
}
const asUser = (u, sql) => db(`select testkit.as_user('${u.id}', $q$${sql}$q$)`);

function url(user, extra = '') {
  return `${BASE}/index.html?backend=${encodeURIComponent(BASE)}&anon=${encodeURIComponent(ANON)}&autologin=${encodeURIComponent(user.email + '|' + user.password)}${extra}`;
}

async function open(user, extra, body) {
  const browser = await chromium.launch({ args: ['--use-gl=angle', '--use-angle=swiftshader', '--enable-unsafe-swiftshader', '--ignore-gpu-blocklist', '--no-sandbox'] });
  const page = await browser.newPage({ viewport: { width: 1024, height: 576 } });
  const logs = [], errors = [];
  page.on('console', m => logs.push(m.text())); page.on('pageerror', e => errors.push(String(e)));
  const waitLog = async (re, from = 0, seconds = 60) => { for (let i = 0; i < seconds * 2; i++) { const hit = logs.slice(from).find(l => re.test(l)); if (hit) return hit; await sleep(500); } return null; };
  const targets = () => { const l = [...logs].reverse().find(x => x.startsWith('[WorldMap] targets')) || ''; return Object.fromEntries([...l.matchAll(/TARGET (\S+) x=([\d.]+) y=([\d.]+)/g)].map(m => [m[1], { x: +m[2], y: +m[3] }])); };
  const click = async name => {
    for (let attempt = 0; attempt < 3; attempt++) {
      const t = targets()[name]; if (!t) { await sleep(1500); continue; }
      const b = await page.locator('#unity-canvas').boundingBox(); await page.mouse.move(b.x + t.x * b.width, b.y + t.y * b.height); await sleep(250); await page.mouse.down(); await sleep(150); await page.mouse.up(); return true;
    }
    return false;
  };
  const settle = async () => { await sleep(4000); };      // the game re-reports its click positions about a second after anything is rebuilt
  await page.goto(url(user, extra));
  try { await body({ page, logs, errors, waitLog, targets, click, settle }); }
  finally { allErrors.push(...errors); allExc.push(...logs.filter(l => /exception|NullReference|IndexOutOfRange|error CS/i.test(l) && !/GLib|swiftshader|\[Backend\].*(->|failed)/i.test(l))); await browser.close(); }
}
const allErrors = [], allExc = [];

(async () => {
  // =========================================================================================== 1. the dashboard on real data
  const bob = await signup('BrowserBob'), carl = await signup('BrowserCarl');
  db(`update profiles set experience = 2050, level = 5, distance_km = 120, jobs_completed = 12 where id = '${bob.id}'`);       // real level-up, route-unlock and achievement notifications are created by the server
  db(`select testkit.mk_job('BR-1', 1, 266)`); db(`select testkit.mk_job('BR-2', 1, 410)`); db(`select testkit.mk_job('BR-3', 1, 780)`);
  db(`update jobs set difficulty = 4 where code = 'BR-3'`);
  db(`update jobs set cargo_type = 'Containers' where code = 'BR-1'`); db(`update jobs set cargo_type = 'Cement Bags' where code = 'BR-2'`); db(`update jobs set cargo_type = 'Fuel (Diesel)' where code = 'BR-3'`);
  const catDb = cat => +db(`select count(*) from jobs j left join cargo_types c on c.name = j.cargo_type where j.status = 'open' and j.expires_at > now() and coalesce(c.category, 'freight') = '${cat}'`);
  asUser(carl, `select start_session('webgl')::text`);
  const rid = JSON.parse(asUser(bob, `select send_friend_request('${carl.id}')::text`)).request_id; asUser(carl, `select respond_friend_request('${rid}', true)`);
  asUser(bob, `select create_convoy('Browser Run')`); asUser(carl, `select join_convoy((select convoy_id from convoy_members where player_id = '${bob.id}'))`); asUser(carl, `select leave_convoy()`);
  db(`select admin_broadcast('Welcome to the test server', 'This is a server message.', 'br-1')`);
  const unreadDb = () => +db(`select count(*) from notifications where player_id = '${bob.id}' and read_at is null`);
  const totalDb = +db(`select count(*) from notifications where player_id = '${bob.id}'`);
  const openJobsDb = +db(`select count(*) from jobs where status = 'open' and expires_at > now()`);

  await open(bob, '', async c => {
    const built = await c.waitLog(/\[Dashboard\] built for BrowserBob/, 0, 150);
    check(!!built, 'sign-in on the real auth service reaches the dashboard for the signed-in player', built);
    if (!built) return;
    check(!!(await c.waitLog(/\[Presence\] session started/, 0, 60)), 'a presence session was started on the server');
    const live = db(`select count(*) from game_sessions where player_id = '${bob.id}' and ended_at is null and last_seen_at > now() - interval '90 seconds'`);
    check(live === '1', 'the server shows the player online (live session in the database)', live);
    check(db(`select count(*) from analytics_events where player_id = '${bob.id}' and name = 'login'`) === '1', 'a login analytics event was recorded');
    const jobs = await c.waitLog(/\[Dashboard\] jobs state=ready count=(\d+)/, 0, 60);
    check(!!jobs && +/count=(\d+)/.exec(jobs)[1] === openJobsDb, `the job board shows exactly the ${openJobsDb} open jobs in the database`, jobs);
    const hero = await c.waitLog(/\[Dashboard\] hero state=ready slides=3/, 0, 60); check(!!hero, 'the hero banners come from the content service (3 slides)', hero);
    const news = await c.waitLog(/\[Dashboard\] news state=ready count=4/, 0, 60); check(!!news, 'the news comes from the content service (4 authored items)', news);
    const players = await c.waitLog(/\[Dashboard\] players state=(\w+) count=(\d+)/, 0, 60);
    check(!!players && /state=ready count=1/.test(players), 'recent players come from the server (Carl, from the shared convoy)', players);
    const want = unreadDb(); const bell = await c.waitLog(new RegExp('\\[Dashboard\\] bell unread=' + want + '$'), 0, 60);
    check(want > 0 && !!bell, `the bell badge becomes the server's unread count (${want})`, bell);
    check((await c.waitLog(/\[World\] state=ready/, 0, 60)) !== null, 'the route and city state was loaded from the server');
    await c.settle(); await c.page.screenshot({ path: `${prefix}-1-dashboard.png` });
    const tg = c.targets();
    for (const k of ['Btn_Bell', 'Btn_Presence', 'Btn_YourTruck', 'Btn_HeroAction', 'Btn_Dot2', 'Btn_NewsNext', 'Btn_RoutesMap', 'Btn_JoinConvoy', 'Nav_Jobs', 'Btn_Accept_BR-3']) check(!!tg[k], `${k} is a clickable target`);

    // notifications: loaded from the server when opened, read state written back to the database
    let m = c.logs.length; await c.click('Btn_Bell');
    const opened = await c.waitLog(/\[Dashboard\] notices open count=(\d+) unread=(\d+) state=ready/, m, 60);
    check(!!opened && +/count=(\d+)/.exec(opened)[1] === totalDb, `the notification list holds all ${totalDb} server notifications`, opened);
    await c.settle(); await c.page.screenshot({ path: `${prefix}-2-notifications.png` });
    const before = unreadDb(); m = c.logs.length; await c.click('Notice_1'); await sleep(6000);
    check(unreadDb() === before - 1, 'tapping a notification marks exactly that one read in the database', `${before} -> ${unreadDb()}`);
    await c.click('Btn_Markread'); await sleep(8000);
    check(unreadDb() === 0, 'Mark read clears every unread notification in the database', unreadDb());

    // heartbeat keeps the session alive while the page is open
    const seen0 = db(`select extract(epoch from last_seen_at)::bigint from game_sessions where player_id = '${bob.id}' and ended_at is null order by started_at desc limit 1`);
    await sleep(40000);
    const seen1 = db(`select extract(epoch from last_seen_at)::bigint from game_sessions where player_id = '${bob.id}' and ended_at is null order by started_at desc limit 1`);
    check(+seen1 > +seen0, 'heartbeats keep advancing the session on the server', `${seen0} -> ${seen1}`);

    // the world map shows the server's state
    m = c.logs.length; await c.click('Btn_RoutesMap'); const wm = await c.waitLog(/\[WorldMap\] opened\. server=(\w+)/, m, 60);
    check(!!wm && /server=True/i.test(wm), 'the world map opens with the server-provided route state', wm);
    await c.settle(); await c.page.screenshot({ path: `${prefix}-3-worldmap.png` });
    m = c.logs.length; await c.click('Btn_Back'); await c.waitLog(/\[Dashboard\] built/, m, 60); await c.settle();

    // ---- the Jobs page: every row, filter, search result and number comes from the server
    m = c.logs.length; await c.click('Nav_Jobs');
    const board = await c.waitLog(/\[Jobs\] page state=ready total=(\d+) shown=(\d+) cat=all/, m, 90);
    check(!!board && +/total=(\d+)/.exec(board)[1] === openJobsDb, `the Jobs page lists the ${openJobsDb} open jobs held by the server`, board);
    check(!!board && /selected=/.test(board) && !/selected=none/.test(board), 'a job is selected and its details are shown', board);
    await c.settle(); await c.page.screenshot({ path: `${prefix}-3b-jobs.png` });
    let tj = c.targets();
    for (const k of ['Btn_Cat_all', 'Btn_Cat_freight', 'Btn_Cat_construction', 'Btn_Cat_fuel_chemicals', 'Btn_Cat_agriculture', 'Btn_Cat_manufacturing', 'Btn_Cat_vehicles', 'Search_Input', 'Btn_JobRow_BR-1', 'Btn_JobRow_BR-2', 'Btn_JobRow_BR-3', 'Btn_AcceptJob']) check(!!tj[k], `${k} is a clickable target on the Jobs page`);
    m = c.logs.length; await c.click('Btn_Cat_construction');
    const cons = await c.waitLog(/\[Jobs\] page state=ready total=(\d+) .*cat=construction/, m, 90);
    check(!!cons && +/total=(\d+)/.exec(cons)[1] === catDb('construction') && catDb('construction') >= 1, `the Construction filter is applied by the server (${catDb('construction')} job)`, cons);
    await c.settle(); await c.page.screenshot({ path: `${prefix}-3c-jobs-filtered.png` });
    m = c.logs.length; await c.click('Btn_Cat_vehicles');
    const none = await c.waitLog(/\[Jobs\] page state=empty total=0 .*cat=vehicles/, m, 90);
    check(!!none, 'a category with no jobs shows the empty state, not stale rows', none);
    await c.settle(); await c.page.screenshot({ path: `${prefix}-3d-jobs-empty-category.png` });
    m = c.logs.length; await c.click('Btn_Cat_all'); await c.waitLog(/\[Jobs\] page state=ready .*cat=all/, m, 90); await c.settle();
    m = c.logs.length; await c.click('Search_Input'); await sleep(500); await c.page.keyboard.type('diesel', { delay: 120 });
    const srch = await c.waitLog(/\[Jobs\] page state=ready total=1 .*search="diesel" selected=BR-3/, m, 90);
    check(!!srch, 'typing in the search box filters on the server (cargo name) and selects the match', srch);
    await c.page.screenshot({ path: `${prefix}-3e-jobs-search.png` });
    m = c.logs.length; for (let i = 0; i < 6; i++) await c.page.keyboard.press('Backspace');
    check(!!(await c.waitLog(/\[Jobs\] page state=ready total=\d+ .*search=""/, m, 90)), 'clearing the search restores the full board');
    await c.settle();
    m = c.logs.length; await c.click('Btn_JobRow_BR-2'); check(!!(await c.waitLog(/\[Jobs\] selected=BR-2/, m, 30)), 'clicking a row selects that job');
    await sleep(1500); await c.page.screenshot({ path: `${prefix}-3f-jobs-selected.png` });
    m = c.logs.length; await c.click('Btn_JobRow_BR-3'); await c.waitLog(/\[Jobs\] selected=BR-3/, m, 30); await c.settle();

    // accepting a job: the SERVER takes the job, the reward is the server's number
    m = c.logs.length; await c.click('Btn_AcceptJob'); await sleep(10000);
    const asg = db(`select a.status || '/' || j.reward from job_assignments a join jobs j on j.id = a.job_id where a.player_id = '${bob.id}' and j.code = 'BR-3'`);
    check(asg === 'accepted/780', 'Accept Job on the Jobs page created the assignment on the server with the server-side reward', asg);
    check(db(`select count(*) from analytics_events where player_id = '${bob.id}' and name = 'job_accepted'`) === '1', 'job_accepted was recorded');
    await c.page.screenshot({ path: `${prefix}-4-driving.png` });
  });
  check(db(`select count(*) from game_sessions where player_id = '${bob.id}' and ended_at is null`) !== '0', 'closing the tab leaves the session to time out (no clean logout)');

  // =========================================================================================== 2. failure states (injected request failures, real data)
  const fay = await signup('BrowserFay'); db(`select testkit.mk_job('BR-4', 1, 500)`);
  await open(fay, '&fault=list_available_jobs:slow:9,get_content:error:3,get_world_state:error:3,list_recent_players:error:3', async c => {
    check(!!(await c.waitLog(/\[Dashboard\] built for BrowserFay/, 0, 150)), 'the dashboard opens even though several services are failing');
    check(!!(await c.waitLog(/\[Dashboard\] jobs state=loading/, 0, 30)), 'slow job board: the loading state is shown first');
    await sleep(2000); await c.page.screenshot({ path: `${prefix}-5-loading.png` });
    check(!!(await c.waitLog(/\[Dashboard\] jobs state=ready/, 0, 90)), 'slow job board: it then loads');
    check(!!(await c.waitLog(/\[Dashboard\] news state=unavailable/, 0, 90)), 'news that cannot load says "unavailable" (after the retries) instead of inventing items');
    check(!!(await c.waitLog(/\[Dashboard\] players state=unavailable/, 0, 90)), 'recent players that cannot load say "unavailable"');
    check(!!(await c.waitLog(/\[World\] state=unavailable/, 0, 90)), 'a failing world state is reported as unavailable (the map fails closed)');
    await c.settle(); await c.page.screenshot({ path: `${prefix}-6-unavailable.png` });
    const tg = c.targets();
    check(!!tg['Btn_RetryNews'] && !!tg['Btn_RetryPlayers'], 'retry buttons are offered for the failed blocks');
    let m = c.logs.length; await c.click('Btn_RetryNews'); check(!!(await c.waitLog(/\[Dashboard\] news state=ready count=4/, m, 90)), 'news: Try again loads the real news');
    m = c.logs.length; await c.click('Btn_RetryPlayers'); check(!!(await c.waitLog(/\[Dashboard\] players state=(ready|empty)/, m, 90)), 'players: Try again reaches the server');
    if (c.targets()['Btn_RetryWorld']) { m = c.logs.length; await c.click('Btn_RetryWorld'); check(!!(await c.waitLog(/\[Dashboard\] world state=ready/, m, 90)), 'routes: Try again loads the world state'); }
    else check(false, 'routes: a retry control is offered when the world state is unavailable');
    await c.settle(); await c.page.screenshot({ path: `${prefix}-7-recovered.png` });
  });

  const gus = await signup('BrowserGus');
  await open(gus, '&fault=list_available_jobs:error:3', async c => {
    const err = await c.waitLog(/\[Dashboard\] jobs state=error/, 0, 150);
    check(!!err, 'job board error state after the retries are exhausted', err);
    await c.settle(); await c.page.screenshot({ path: `${prefix}-8-jobs-error.png` });
    check(!!c.targets()['Btn_RetryJobs'], 'a Try again button is offered');
    const m = c.logs.length; await c.click('Btn_RetryJobs'); check(!!(await c.waitLog(/\[Dashboard\] jobs state=(ready|empty)/, m, 90)), 'Try again reaches the server');
  });

  db(`update jobs set status = 'closed'`);
  const hal = await signup('BrowserHal');
  await open(hal, '', async c => {
    check(!!(await c.waitLog(/\[Dashboard\] jobs state=empty/, 0, 150)), 'with no open jobs the board reports the empty state');
    await c.settle(); await c.page.screenshot({ path: `${prefix}-9-jobs-empty.png` }); check(!!c.targets()['Btn_OpenBoard'], 'the empty state offers the job board');
  });

  // =========================================================================================== 3. a session that cannot be renewed sends the player back to sign-in
  await fetch(BASE + '/e2e/ttl?seconds=60', { method: 'POST' });
  const ivy = await signup('BrowserIvy');
  await open(ivy, '', async c => {
    check(!!(await c.waitLog(/\[Dashboard\] built for BrowserIvy/, 0, 150)), 'a short-lived session still opens the dashboard');
    check(!!(await c.waitLog(/\[Backend\] session refreshed/, 0, 120)), 'the client renews its access token before it expires (refresh token exchange)');
    db(`update e2e.refresh_tokens set used = true`);                       // the refresh token is revoked server-side
    check(!!(await c.waitLog(/\[Session\] expired: back to sign-in/, 0, 150)), 'when the session cannot be renewed the game returns to sign-in');
    await c.settle(); await c.page.screenshot({ path: `${prefix}-10-session-expired.png` });
  });
  await fetch(BASE + '/e2e/ttl?seconds=3600', { method: 'POST' });

  check(allErrors.length === 0, `no page errors (${allErrors.length})`, allErrors[0]);
  check(allExc.length === 0, `no exceptions in the Unity log (${allExc.length})`, allExc[0]);
  console.log(`\n${passes.length} passed, ${failures.length} failed`);
  if (failures.length) { console.log('BROWSER E2E FAILED:'); failures.forEach(f => console.log(' - ' + f)); process.exit(1); }
  console.log('BROWSER E2E PASSED');
})().catch(e => { console.error('E2E crashed:', e); process.exit(1); });
