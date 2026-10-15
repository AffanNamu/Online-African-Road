// Browser end-to-end test: the REAL WebGL build against a REAL backend (PostgREST + Postgres through tools/e2e/server.js).
// Usage: source /tmp/aro-e2e.env && node tools/webgl-e2e.js [screenshot-prefix]      (tools/e2e/up.sh started the backend and serves the build)
// Every dashboard value on screen is checked against what the DATABASE holds; every action is checked in the database afterwards.
// Failure states are exercised by injecting request failures (?fault=...), never by injecting data.
const crypto = require('crypto'), fs = require('fs'), path = require('path'), { execFileSync } = require('child_process');
const { chromium } = require('playwright');

const BASE = process.env.E2E_URL, ANON = process.env.E2E_ANON_KEY, DB_URL = process.env.E2E_DB_URL, SECRET = process.env.PGRST_JWT_SECRET;
if (!BASE || !ANON || !DB_URL) { console.error('source /tmp/aro-e2e.env first (tools/e2e/up.sh)'); process.exit(2); }
const prefix = process.argv[2] || 'webgl-e2e';
const failures = [], passes = [];
let currentLogs = null, currentPage = null, currentTargets = null; const clickLog = [];   // clickLog: every click the harness made (what, where, where in the game's log), for the failure diagnostics below
const check = (ok, what, detail) => {
  (ok ? passes : failures).push(what); console.log(`${ok ? 'PASS' : 'FAIL'}  ${what}${!ok && detail !== undefined ? '  -> ' + String(detail).slice(0, 300) : ''}`);
  if (!ok && currentLogs) console.log('      last game log lines: ' + currentLogs.slice(-8).map(l => l.replace(/\s+/g, ' ').slice(0, 160)).join(' | '));   // what the game itself said just before
  if (!ok) failureDiagnostics();
};
// Evidence for intermittent failures (no behaviour change): a screenshot at the moment of failure and, for each of the last clicks, what the game reported. In E2E mode the game logs
// "[UIDiag] pointer pressed/released ..." from the Input System (and the older "[UIDiag] mouse pressed ..." from the legacy input, which the project also has enabled) and, for every button, "[UIDiag] onClick <name>"
// when Unity UI invokes it. Classification printed per click (the "ui hits" list is printed but NOT used: it is empty for clicks that worked, e.g. Repair and Refuel in run 38705487149, so it proves nothing):
//   NO-PRESS      the game never received a press after this click (lost before the game saw it)
//   OFF-TARGET    a press arrived more than 6 px from the position the harness clicked (the page moved, or the harness aimed wrongly)
//   MOVED         the target is now somewhere else than where the harness clicked
//   NO-ONCLICK    the press arrived on the target but Unity UI did not invoke the button (an input or UI-event problem)
//   CLICKED       the button was invoked: the missing effect is a game or backend problem
//   PRESS-ON-TARGET  the press arrived on the target; this build does not log onClick, so nothing more can be said
function failureDiagnostics() {
  try {
    const n = failures.length;
    if (currentPage) currentPage.screenshot({ path: `${prefix}-FAIL-${n}.png` }).catch(() => {});
    if (!currentLogs) return;
    console.log(`      click diagnostics for failure ${n} (screenshot ${prefix}-FAIL-${n}.png):`);
    const hooked = currentLogs.some(l => /\[UIDiag\] onClick /.test(l));
    for (const c of clickLog.slice(-3)) {
      const after = currentLogs.slice(c.from, c.to === undefined ? undefined : c.to);
      const press = after.find(l => /\[UIDiag\] (pointer|mouse) pressed/.test(l)), rel = after.find(l => /\[UIDiag\] pointer released/.test(l));
      const oc = after.find(l => new RegExp('\\[UIDiag\\] onClick ' + c.name + '\\b').test(l));
      const now = currentTargets ? currentTargets()[c.name] : null; const moved = now && c.pos && (Math.abs(now.x - c.pos.x) > 0.008 || Math.abs(now.y - c.pos.y) > 0.008);
      let off = false;
      if (press && c.pos) { const m = /pressed at (\d+),(\d+) of (\d+)x(\d+)/.exec(press); if (m) off = Math.hypot(+m[1] - c.pos.x * +m[3], +m[2] - (1 - c.pos.y) * +m[4]) > 6; }   // the game's y axis points up, the target's points down
      const cls = !press ? 'NO-PRESS' : off ? 'OFF-TARGET' : moved ? 'MOVED' : oc ? 'CLICKED' : hooked ? 'NO-ONCLICK' : 'PRESS-ON-TARGET';
      console.log(`        ${cls.padEnd(15)} click ${c.name} at ${c.pos ? c.pos.x.toFixed(3) + ',' + c.pos.y.toFixed(3) : '?'}${moved ? ` (target now ${now.x.toFixed(3)},${now.y.toFixed(3)})` : ''} | press: ${press ? press.replace(/^\[UIDiag\] /, '').slice(0, 170) : 'none'}${rel ? '' : ' | no release logged'}${oc ? ' | ' + oc.replace(/^\[UIDiag\] /, '').slice(0, 80) : ''}`);
    }
  } catch (e) { console.log('      (diagnostics failed: ' + String(e).slice(0, 100) + ')'); }
}
const db = sql => execFileSync('psql', [DB_URL, '-X', '-q', '-At', '-v', 'ON_ERROR_STOP=1', '-c', sql], { encoding: 'utf8' }).trim();
const sleep = ms => new Promise(r => setTimeout(r, ms));
const wipe = async c => { await c.page.keyboard.press('End'); for (let i = 0; i < 30; i++) await c.page.keyboard.press('Backspace'); };      // empties a focused text field (the game's fields take key presses, not select-all)

async function signup(name, onboard = true) {
  const email = name.toLowerCase() + '@e2e.test', password = 'pw-' + name + '-123';
  const r = await fetch(BASE + '/auth/v1/signup', { method: 'POST', headers: { 'content-type': 'application/json' }, body: JSON.stringify({ email, password, data: { display_name: name } }) });
  const j = await r.json(); if (r.status !== 200) throw new Error('signup ' + name + ': ' + JSON.stringify(j));
  if (onboard) db(`update profiles set onboarded_at = now() where id = '${j.user.id}'`);          // every test player except the onboarding ones has already been through first-run
  return { name, email, password, id: j.user.id };
}
const asUser = (u, sql) => db(`select testkit.as_user('${u.id}', $q$${sql}$q$)`);

function url(user, extra = '') {
  return `${BASE}/index.html?backend=${encodeURIComponent(BASE)}&anon=${encodeURIComponent(ANON)}&autologin=${encodeURIComponent(user.email + '|' + user.password)}${extra}`;
}

async function open(user, extra, body, opts = {}) {
  const browser = await chromium.launch({ args: ['--use-gl=angle', '--use-angle=swiftshader', '--enable-unsafe-swiftshader', '--ignore-gpu-blocklist', '--no-sandbox'] });
  const page = await browser.newPage({ viewport: { width: 1024, height: 576 }, ...(opts.locale ? { locale: opts.locale } : {}) });
  const logs = [], errors = []; currentLogs = logs; currentPage = page;
  page.on('console', m => logs.push(m.text())); page.on('pageerror', e => errors.push(String(e)));
  const waitLog = async (re, from = 0, seconds = 60) => { for (let i = 0; i < seconds * 2; i++) { const hit = logs.slice(from).find(l => re.test(l)); if (hit) return hit; await sleep(500); } return null; };
  const targets = () => { const l = [...logs].reverse().find(x => x.startsWith('[WorldMap] targets')) || ''; return Object.fromEntries([...l.matchAll(/TARGET (\S+) x=([\d.]+) y=([\d.]+)/g)].map(m => [m[1], { x: +m[2], y: +m[3] }])); };
  const click = async name => {
    for (let attempt = 0; attempt < 3; attempt++) {
      const t = targets()[name]; if (!t) { await sleep(1500); continue; }
      const rec = { name, from: logs.length, pos: { x: t.x, y: t.y } }; if (clickLog.length) clickLog[clickLog.length - 1].to = rec.from; clickLog.push(rec);
      const b = await page.locator('#unity-canvas').boundingBox(); await page.mouse.move(b.x + t.x * b.width, b.y + t.y * b.height); await sleep(250); await page.mouse.down(); await sleep(150); await page.mouse.up(); return true;
    }
    return false;
  };
  const clickUntil = async (name, re, seconds = 30, tries = 3) => {
    for (let i = 0; i < tries; i++) { const from = logs.length; await click(name); const hit = await waitLog(re, from, seconds); if (hit) return hit; }
    return null;
  };
  currentTargets = targets;
  const settle = async () => { await sleep(4000); };      // the game re-reports its click positions about a second after anything is rebuilt
  await page.goto(url(user, extra));
  try { await body({ page, logs, errors, waitLog, targets, click, clickUntil, settle }); }
  finally { allErrors.push(...errors); allExc.push(...logs.filter(l => /exception|NullReference|IndexOutOfRange|error CS/i.test(l) && !/GLib|swiftshader|\[Backend\].*(->|failed)|\[Session\] create failed: .*Bad Gateway|Could not do Qos region selection/i.test(l))); await browser.close(); }
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
    const want = unreadDb(); const bell = await c.waitLog(new RegExp('\\[Dashboard\\] bell unread=' + want + '(?!\\d)'), 0, 60);
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
    const wm = await c.clickUntil('Btn_RoutesMap', /\[WorldMap\] opened\. server=(\w+)/, 40);
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
    check(asg === 'in_progress/780' || asg === 'accepted/780', 'Accept Job on the Jobs page created the assignment on the server with the server-side reward', asg);
    check(db(`select count(*) from analytics_events where player_id = '${bob.id}' and name = 'job_accepted'`) === '1', 'job_accepted was recorded');
    await c.page.screenshot({ path: `${prefix}-4-driving.png` });
  });
  check(db(`select count(*) from game_sessions where player_id = '${bob.id}' and ended_at is null`) !== '0', 'closing the tab leaves the session to time out (no clean logout)');

  // =========================================================================================== 2. failure states (injected request failures, real data)
  const fay = await signup('BrowserFay'); db(`select testkit.mk_job('BR-4', 1, 500)`);
  await open(fay, '&fault=list_available_jobs:slow:9,get_content:error:6,get_world_state:error:3,list_recent_players:error:3', async c => {
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

  // =========================================================================================== 2b. Multiplayer: find / create a convoy, route, lobby, ready check, chat, start, leave
  db(`select admin_set_route('lagos-ibadan', 'OPEN', 1)`); db(`update jobs set status = 'closed'`);
  const mia = await signup('BrowserMia'), noah = await signup('BrowserNoah'), zed = await signup('BrowserZed');
  for (const u of [noah, zed]) asUser(u, `select start_session('webgl')::text`);
  asUser(zed, `select create_convoy('Zed Public Run')`);
  const openConvoysDb = () => +db(`select count(*) from convoys where disbanded_at is null and not is_private`);
  const liveOthersDb = () => +db(`select count(distinct player_id) from game_sessions where ended_at is null and last_seen_at > now() - interval '90 seconds' and player_id <> '${mia.id}'`);
  await open(mia, '', async c => {
    check(!!(await c.waitLog(/\[Dashboard\] built for BrowserMia/, 0, 150)), 'Multiplayer: the dashboard opens for the new player');
    await c.settle(); let m = c.logs.length; await c.click('Nav_Multiplayer');
    const lst = await c.waitLog(/\[Multiplayer\] convoys state=(\w+) total=(\d+)/, m, 90);
    check(!!lst && /state=ready/.test(lst) && +/total=(\d+)/.exec(lst)[1] === openConvoysDb(), `Find Convoys lists the ${openConvoysDb()} public convoys held by the server`, lst);
    const pls = await c.waitLog(/\[Multiplayer\] players state=ready total=(\d+)/, m, 90);
    check(!!pls && +/total=(\d+)/.exec(pls)[1] === liveOthersDb(), `Live Players shows the ${liveOthersDb()} other players with a live session`, pls);
    check(!!(await c.waitLog(/\[Multiplayer\] convoy=none/, m, 60)) || !c.logs.slice(m).some(l => /\[Multiplayer\] convoy=(?!none)/.test(l)), 'a player with no convoy sees no convoy');
    await c.settle(); await c.page.screenshot({ path: `${prefix}-11-multiplayer.png` });
    let tg = c.targets();
    for (const k of ['Btn_MpTab_Convoys', 'Btn_MpTab_Players', 'Btn_MpTab_Friends', 'Btn_MpTab_Voice', 'Btn_MpTab_Servers', 'Btn_CreateConvoy', 'Btn_CreateConvoy2', 'Mp_Search_Input', 'Btn_JoinConvoy_ZedPublicRun', 'Btn_SendChat']) check(!!tg[k], `${k} is a clickable target on the Multiplayer page`);

    // create: name, route, tag; every choice is sent to the server and the result read back
    m = c.logs.length; await c.click('Btn_CreateConvoy2'); await c.settle(); await c.page.screenshot({ path: `${prefix}-12-create-dialog.png` });
    await c.click('Btn_CreateRoute_lagos-ibadan'); await c.settle(); await c.click('Btn_CreateTag_jobs'); await c.settle(); await c.click('Btn_CreateConfirm');
    const made = await c.waitLog(/\[Multiplayer\] created convoy="BrowserMia's Convoy" route=lagos-ibadan private=False cap=8 mode=relaxed level=1 lang=en password=False tags=1 ok=True/, m, 90);
    check(!!made, 'Create Convoy: the convoy was created with the chosen route and tag and no errors', made);
    check(db(`select route_code || '/' || array_to_string(tags, ',') || '/' || phase from convoys where leader_id = '${mia.id}' and disbanded_at is null`) === 'lagos-ibadan/jobs/lobby', 'the server holds the new convoy as a lobby with the route and tag');
    check(!!(await c.waitLog(/\[Multiplayer\] convoy=BrowserMia's Convoy phase=lobby members=1 route=lagos-ibadan/, m, 60)), 'the page shows the new lobby from the server');
    check(!!(await c.waitLog(/\[Multiplayer\] tab=lobby \(convoy joined\)/, m, 30)), 'creating a convoy opens the Convoy Lobby tab');

    // another player joins and readies up (through the database API, as that player); the page sees both within a few seconds
    const cv = db(`select id from convoys where leader_id = '${mia.id}' and disbanded_at is null`);
    m = c.logs.length; asUser(noah, `select join_convoy('${cv}')`); asUser(noah, `select set_my_ready(true)::text`);
    const two = await c.waitLog(/\[Multiplayer\] convoy=BrowserMia's Convoy phase=lobby members=2 route=lagos-ibadan ready=2/, m, 60);
    check(!!two, 'the lobby shows the second player and their ready flag (polled from the server)', two);
    await c.settle(); await c.page.screenshot({ path: `${prefix}-13-lobby.png` });
    check(!!c.targets()['Btn_StartDriving'], 'with a route and everyone ready the leader gets Start Driving');

    // chat: written through the UI, stored by the server; another player's line arrives by polling
    m = c.logs.length; await c.click('Chat_Input'); await sleep(400); await c.page.keyboard.type('hello convoy', { delay: 100 }); await c.page.keyboard.press('Enter', { delay: 150 });
    await sleep(6000);
    check(db(`select count(*) from convoy_messages where convoy_id = '${cv}' and sender_id = '${mia.id}' and body = 'hello convoy'`) === '1', 'a chat line typed in the page is stored by the server with the sender from the session');
    m = c.logs.length; asUser(noah, `select post_convoy_message('On my way')::text`);
    check(!!(await c.waitLog(/\[Multiplayer\] chat lines=\d+/, m, 30)), 'a line written by another player appears (polled)');
    await sleep(2500); await c.page.screenshot({ path: `${prefix}-14-chat.png` });
    check(!!c.targets()['Btn_LobbyStartBlocked'] || !!c.targets()['Btn_LobbyStart'], "the lobby tab shows the leader's Start button");
    await c.page.screenshot({ path: `${prefix}-14b-lobby-tab.png` });

    // safety: report a line and block its sender (server-side), through the page
    const lineId = db(`select id from convoy_messages where convoy_id = '${cv}' and body = 'On my way'`);
    await c.settle(); m = c.logs.length; await c.click('Btn_ChatLine_' + lineId); await c.settle(); await c.page.screenshot({ path: `${prefix}-14c-chat-line-menu.png` });
    await c.click('Btn_Report_abuse');
    check(!!(await c.waitLog(/\[Multiplayer\] report line=\d+ reason=abuse ok=True moderation=ok/, m, 30)), 'Report: the server received the report');
    check(db(`select count(*) from chat_reports where reporter_id = '${mia.id}' and reason = 'abuse'`) === '1', 'the report is stored with the reporter from the session');
    await c.settle(); m = c.logs.length; await c.click('Btn_ChatLine_' + lineId); await c.settle(); await c.click('Btn_LineBlock');
    check(!!(await c.waitLog(/\[Multiplayer\] block player="BrowserNoah" ok=True/, m, 30)), 'Block: the server stored the block');
    check(db(`select count(*) from player_blocks where blocker_id = '${mia.id}' and blocked_id = '${noah.id}'`) === '1', 'the block is in the database');
    await sleep(4000); await c.page.screenshot({ path: `${prefix}-14d-after-block.png` });
    db(`delete from player_blocks where blocker_id = '${mia.id}'`);

    // start driving: the server accepts, the live-session service is not reachable from the test, so the convoy must go back to the lobby (never stuck "driving")
    asUser(noah, `select start_session('webgl')::text`);                          // the start needs everyone online: refresh the second player's heartbeat
    m = c.logs.length; await c.click('Btn_StartDriving');
    let phase = ''; for (let i = 0; i < 90 && phase !== 'lobby'; i++) { await sleep(2000); phase = db(`select phase from convoys where id = '${cv}'`); if (db(`select count(*) from analytics_events where name = 'convoy_started' and player_id = '${mia.id}'`) === '1' && phase === 'lobby') break; }
    check(db(`select count(*) from analytics_events where name = 'convoy_started' and player_id = '${mia.id}'`) === '1', 'Start Driving reached the server (convoy_started was recorded)');
    check(phase === 'lobby', 'when the live session cannot be created the convoy is put back in the lobby, not left "driving"', phase);
    check(db(`select count(*) from convoy_messages where convoy_id = '${cv}' and kind = 'system' and body like 'The convoy is back in the lobby.'`) === '1', 'the server wrote the system line for the rollback');
    await c.page.screenshot({ path: `${prefix}-15-start-failed.png` });

    // server list and nearby players come from the database
    await c.settle(); m = c.logs.length; await c.click('Btn_MpTab_Servers');
    const srv = await c.waitLog(/\[Multiplayer\] servers state=ready count=1 first=ONLINE population=(\d+)/, m, 60);
    check(!!srv, 'Server List shows the registered server as ONLINE with its live population', srv);
    await c.settle(); await c.page.screenshot({ path: `${prefix}-15b-servers.png` });
    m = c.logs.length; await c.click('Btn_MpTab_Lobby');
    await c.settle(); await c.page.screenshot({ path: `${prefix}-15c-lobby-after.png` });

    // leave: leadership passes to the remaining member
    await c.settle(); m = c.logs.length; await c.click('Btn_ConvoyManage'); await c.settle(); await c.page.screenshot({ path: `${prefix}-16-manage.png` });
    await c.click('Btn_LeaveConvoy');
    check(!!(await c.waitLog(/\[Multiplayer\] convoy=none/, m, 60)), 'Leave convoy: the page returns to "no convoy"');
    check(db(`select count(*) from convoy_members where player_id = '${mia.id}'`) === '0' && db(`select leader_id from convoys where id = '${cv}'`) === noah.id, 'the server removed the player and passed leadership on');
    await c.settle(); await c.page.screenshot({ path: `${prefix}-17-left.png` });
    m = c.logs.length; await c.click('Btn_MpTab_Convoys');
    check(!!(await c.waitLog(/\[Multiplayer\] nearby state=(empty|ready) scope=none/, m, 60)) || c.logs.some(l => /\[Multiplayer\] nearby state=(empty|ready) scope=none/.test(l)), 'a player who is not driving has no nearby scope (no invented neighbours)');

    // a second convoy with rules and a password, then disband it
    m = c.logs.length; await c.click('Btn_CreateConvoy2'); await c.settle(); await c.click('Btn_CreateRealistic'); await c.settle();
    await c.click('Create_Password_Input'); await sleep(300); await c.page.keyboard.type('abcd1234', { delay: 80 }); await c.settle(); await c.page.screenshot({ path: `${prefix}-17b-create-rules.png` });
    await c.click('Btn_CreateConfirm');
    check(!!(await c.waitLog(/\[Multiplayer\] created convoy="BrowserMia's Convoy" route=none private=False cap=8 mode=realistic level=1 lang=en password=True tags=0 ok=True/, m, 90)), 'Create with rules: realistic driving and a password were sent to the server');
    check(db(`select driving_mode || '/' || (password_hash is not null)::text || '/' || min_level from convoys where leader_id = '${mia.id}' and disbanded_at is null`) === 'realistic/true/1', 'the server holds the convoy with its mode and a hashed password');
    await c.settle(); m = c.logs.length; await c.click('Btn_LobbyDisband'); await c.settle(); await c.page.screenshot({ path: `${prefix}-17c-disband-dialog.png` });
    await c.click('Btn_DisbandConfirm');
    check(!!(await c.waitLog(/\[Multiplayer\] disband ok=True/, m, 60)), 'Disband: the server accepted');
    check(db(`select count(*) from convoys where leader_id = '${mia.id}' and disbanded_at is null`) === '0', 'the convoy is disbanded in the database');
    await c.settle(); await c.page.screenshot({ path: `${prefix}-17d-disbanded.png` });
  });

  // a failing convoy list shows Unavailable and a retry that works (injected failures only)
  const ola = await signup('BrowserOla');
  await open(ola, '&fault=list_open_convoys:error:3', async c => {
    await c.waitLog(/\[Dashboard\] built for BrowserOla/, 0, 150); await c.settle(); let m = c.logs.length; await c.click('Nav_Multiplayer');
    check(!!(await c.waitLog(/\[Multiplayer\] convoys state=unavailable/, m, 120)), 'convoys that cannot load say "unavailable" instead of inventing rows');
    await c.settle(); await c.page.screenshot({ path: `${prefix}-18-convoys-unavailable.png` });
    check(!!c.targets()['Btn_RetryConvoys'], 'a Try again button is offered');
    m = c.logs.length; await c.click('Btn_RetryConvoys'); check(!!(await c.waitLog(/\[Multiplayer\] convoys state=(ready|empty)/, m, 90)), 'Try again reaches the server');
    m = c.logs.length; await c.click('Btn_MpTab_Voice'); await c.settle(); await c.page.screenshot({ path: `${prefix}-19-voice-soon.png` });
    check(c.logs.slice(m).some(l => /\[Multiplayer\] tab=voice/.test(l)), 'the Voice Chat tab opens an honest "not available yet" page');
  });

  // =========================================================================================== 2c. Trucks & Buses: the catalogue, tabs, search, selection, purchase, compare, garage
  const vera = await signup('BrowserVera');
  const defsDb = +db(`select count(*) from vehicle_definitions`), busDefsDb = +db(`select count(*) from vehicle_definitions where category = 'bus'`), truckDefsDb = +db(`select count(*) from vehicle_definitions where category = 'truck'`);
  const lightDefsDb = +db(`select count(*) from vehicle_definitions where category in ('car','van','motorcycle')`), titanDefsDb = +db(`select count(*) from vehicle_definitions where name ilike '%titan%'`);
  await open(vera, '', async c => {
    check(!!(await c.waitLog(/\[Dashboard\] built for BrowserVera/, 0, 150)), 'Trucks & Buses: the dashboard opens for the new player');
    await c.settle(); let m = c.logs.length; await c.click('Nav_TrucksAndBuses');
    const cat = await c.waitLog(/\[Vehicles\] catalog state=ready total=(\d+)/, m, 90);
    check(!!cat && +/total=(\d+)/.exec(cat)[1] === defsDb, `the catalogue lists the ${defsDb} vehicle definitions held by the database`, cat);
    check(!!cat && new RegExp('balance=' + db(`select balance from player_wallets where player_id = '${vera.id}'`) + ' ').test(cat + ' '), 'the wallet balance on the page is the database balance', cat);
    check(!!cat && /owned=1 /.test(cat), 'the player owns exactly the starter truck', cat);
    await c.settle(); await c.page.screenshot({ path: `${prefix}-20-vehicles.png` });
    let tg = c.targets();
    for (const k of ['Btn_VehTab_All', 'Btn_VehTab_Trucks', 'Btn_VehTab_Buses', 'Btn_VehTab_Light', 'Btn_VehTab_Owned', 'Btn_VehTab_Shop', 'Btn_VehSort', 'Veh_Search_Input', 'Btn_VehCompare', 'Btn_VehPrimary', 'Btn_VehGarage', 'Btn_Veh_aro_titan_480', 'Btn_Veh_truck_light_01', 'Btn_Veh_bus_city_01']) check(!!tg[k], `${k} is a clickable target on the Trucks & Buses page`);

    // tabs filter ONE catalogue
    m = c.logs.length; await c.click('Btn_VehTab_Buses');
    let v = await c.waitLog(/\[Vehicles\] view tab=buses .* shown=(\d+)/, m, 60);
    check(!!v && +/shown=(\d+)/.exec(v)[1] === busDefsDb, `Buses tab shows the ${busDefsDb} buses of the database`, v);
    m = c.logs.length; await c.click('Btn_VehTab_Light');
    v = await c.waitLog(/\[Vehicles\] view tab=light .* shown=(\d+)/, m, 60); check(!!v && +/shown=(\d+)/.exec(v)[1] === lightDefsDb, `Light Vehicles tab shows the ${lightDefsDb} cars, vans and motorcycles`, v);
    await c.settle(); await c.page.screenshot({ path: `${prefix}-20b-light-vehicles.png` });
    m = c.logs.length; await c.click('Btn_VehTab_Owned');
    v = await c.waitLog(/\[Vehicles\] view tab=owned .* shown=(\d+)/, m, 60); check(!!v && +/shown=(\d+)/.exec(v)[1] === 1, 'Owned tab shows only the starter truck', v);
    m = c.logs.length; await c.click('Btn_VehTab_Trucks');
    v = await c.waitLog(/\[Vehicles\] view tab=trucks .* shown=(\d+)/, m, 60); check(!!v && +/shown=(\d+)/.exec(v)[1] === truckDefsDb, `Trucks tab shows the ${truckDefsDb} trucks`, v);

    // an announced model: visible with its real data, never buyable
    m = c.logs.length; await c.click('Btn_Veh_aro_titan_480');
    const pan = await c.waitLog(/\[Vehicles\] panel id=aro_titan_480 state=coming_soon action=ComingSoon enabled=False/, m, 60);
    check(!!pan, 'the flagship ARO TITAN 480 is shown as coming soon and its action is disabled', pan);
    check(!!(await c.waitLog(/\[Vehicles\] jobfit id=aro_titan_480 compatible=\w+ fits=\d+ open=\d+ drivable=False/, m, 60)), 'the page asks the server about job compatibility and learns the model is not drivable yet');
    await c.settle(); await c.page.screenshot({ path: `${prefix}-21-titan.png` });
    await c.click('Btn_VehPrimary'); await sleep(1500);
    check(db(`select count(*) from vehicle_ownership where player_id = '${vera.id}' and definition_id = 'aro_titan_480'`) === '0', 'pressing the disabled action buys nothing');

    // search is real
    m = c.logs.length; await c.click('Veh_Search_Input'); await sleep(500); await c.page.keyboard.type('TiTan', { delay: 120 });
    v = await c.waitLog(/\[Vehicles\] view tab=trucks .*search="TiTan" shown=(\d+)/, m, 60);
    check(!!v && +/shown=(\d+)/.exec(v)[1] === titanDefsDb, 'searching "TiTan" (any case) finds the TITAN models of the database', v);
    for (let i = 0; i < 5; i++) await c.page.keyboard.press('Backspace');
    await c.waitLog(/\[Vehicles\] view tab=trucks .*search="" shown=/, m, 60);

    // not enough money: the server's own verdict, in words
    m = c.logs.length; await c.click('Btn_VehTab_Buses'); await c.settle(); await c.click('Btn_Veh_bus_city_01');
    check(!!(await c.waitLog(/\[Vehicles\] panel id=bus_city_01 state=insufficient_funds action=NeedFunds enabled=False/, m, 60)), 'a bus the player cannot afford says so (server purchase_state)');
    await c.settle(); await c.page.screenshot({ path: `${prefix}-22-cannot-afford.png` });
    await c.click('Nav_Home');
  });
  db(`select _apply_transaction('${vera.id}', 'BONUS', 100000, 'fund:' || gen_random_uuid())`);
  await open(vera, '', async c => {
    await c.waitLog(/\[Dashboard\] built for BrowserVera/, 0, 150); await c.settle(); let m = c.logs.length; await c.click('Nav_TrucksAndBuses');
    check(!!(await c.waitLog(/\[Vehicles\] catalog state=ready/, m, 90)), 'the catalogue is read again from the server after the player got money');
    m = c.logs.length; await c.click('Btn_VehTab_Buses'); await c.settle(); await c.click('Btn_Veh_bus_city_01');
    check(!!(await c.waitLog(/\[Vehicles\] panel id=bus_city_01 state=available action=Buy enabled=True/, m, 60)), 'with enough money the bus can be bought');
    const before = +db(`select balance from player_wallets where player_id = '${vera.id}'`), price = +db(`select price from vehicle_definitions where id = 'bus_city_01'`);
    await c.click('Btn_VehPrimary'); await c.settle(); await c.page.screenshot({ path: `${prefix}-23-buy-dialog.png` });
    check(!!c.targets()['Btn_BuyConfirm'] && !!c.targets()['Btn_BuyCancel'], 'the purchase asks for confirmation (Confirm and Cancel)');
    check(db(`select count(*) from vehicle_ownership where player_id = '${vera.id}' and definition_id = 'bus_city_01'`) === '0', 'nothing is bought before the player confirms');
    m = c.logs.length; await c.click('Btn_BuyConfirm');
    check(!!(await c.waitLog(/\[Vehicles\] buy id=bus_city_01 ok=True/, m, 90)), 'Confirm purchase: the server accepted');
    check(db(`select count(*) from vehicle_ownership where player_id = '${vera.id}' and definition_id = 'bus_city_01'`) === '1', 'the vehicle is owned in the database');
    check(+db(`select balance from player_wallets where player_id = '${vera.id}'`) === before - price, `the wallet was debited by exactly the server price (${price})`);
    check(db(`select count(*) from transactions where player_id = '${vera.id}' and kind = 'VEHICLE_PURCHASE'`) === '1', 'one ledger row for the purchase');
    check(!!(await c.waitLog(/\[Vehicles\] catalog state=ready .* owned=2/, m, 90)), 'the page re-read the catalogue and now shows two owned vehicles');
    await c.settle(); await c.page.screenshot({ path: `${prefix}-24-bought.png` });

    // select it
    m = c.logs.length; await c.click('Btn_VehPrimary');
    check(!!(await c.waitLog(/\[Vehicles\] select id=bus_city_01 ok=True/, m, 60)), 'Select Vehicle: the server accepted');
    check(db(`select (p.selected_vehicle_id = o.id)::text from profiles p, vehicle_ownership o where p.id = '${vera.id}' and o.player_id = p.id and o.definition_id = 'bus_city_01'`) === 'true', 'the active vehicle is persisted in the database');
    await c.settle(); await c.page.screenshot({ path: `${prefix}-25-selected.png` });

    // compare
    m = c.logs.length; await c.click('Btn_VehCompare');
    check(!!(await c.waitLog(/\[Vehicles\] compare open n=2 rows=\d+/, m, 60)), 'Compare Vehicles opens with two vehicles');
    await c.settle(); await c.page.screenshot({ path: `${prefix}-26-compare.png` });
    check(!!c.targets()['Btn_CmpPick_aro_titan_480'], 'every catalogue vehicle can be ticked in the comparison');
    m = c.logs.length; await c.click('Btn_CmpPick_aro_titan_480');
    check(!!(await c.waitLog(/\[Vehicles\] compare open n=3 rows=\d+/, m, 60)), 'ticking a third vehicle adds a column');
    await c.click('Btn_CloseDialog'); await c.settle();

    // garage
    m = c.logs.length; await c.click('Btn_VehGarage');
    check(!!(await c.waitLog(/\[Garage\] opened/, m, 60)), 'Open Garage leads to the Garage page');
    await c.settle(); await c.page.screenshot({ path: `${prefix}-27-garage.png` });
  });
  check(db(`select count(*) from vehicle_ownership where player_id = '${vera.id}'`) === '2', 'the player ended with exactly two vehicles (no duplicate from a second click)');

  // =========================================================================================== 2d. Garage: the 3D showroom (camera), skins, maintenance, active vehicle
  const gabe = await signup('BrowserGabe');
  db(`select _apply_transaction('${gabe.id}', 'BONUS', 100000, 'fund:' || gen_random_uuid())`);
  db(`insert into vehicle_ownership(player_id, definition_id, fuel_l) values ('${gabe.id}', 'bus_city_01', 180)`);
  db(`update vehicle_ownership set damage_pct = 30, fuel_l = 10 where player_id = '${gabe.id}' and definition_id = 'truck_light_01'`);
  await open(gabe, '', async c => {
    check(!!(await c.waitLog(/\[Dashboard\] built for BrowserGabe/, 0, 150)), 'Garage: the dashboard opens for the new player');
    await c.settle(); let m = c.logs.length; await c.click('Nav_Garage');
    const gl = await c.waitLog(/\[Garage\] vehicles state=ready count=(\d+) selected=\S+ active=(\S+)/, m, 90);
    check(!!gl && /count=2 /.test(gl), 'the Garage lists the 2 vehicles the player owns in the database', gl);
    const sr = await c.waitLog(/\[Showroom\] ready vehicle=truck_light_01 model=(\w+) lod=\d+ size=\S+ parts=(\d+) interior=\w+ rt=\d+x\d+/, m, 120);
    check(!!sr, 'the 3D showroom initialised with the selected vehicle', sr);
    check(!!sr && /model=placeholder/.test(sr) && +/parts=(\d+)/.exec(sr)[1] >= 5, 'the model says it is a placeholder and exposes its inspectable parts', sr);
    await c.settle(); await c.page.screenshot({ path: `${prefix}-30-garage.png` });
    let tg = c.targets();
    for (const k of ['Btn_GarTab_Customize', 'Btn_GarTab_Upgrades', 'Btn_GarTab_Maintenance', 'Btn_GarTab_PaintSkins', 'Btn_GarTab_Accessories', 'Btn_GarTab_Performance', 'Btn_ShowExterior', 'Btn_Show360', 'Btn_ShowReset', 'Btn_ShowFront', 'Btn_ShowRear', 'Btn_ShowLeft', 'Btn_ShowRight', 'Btn_ShowTop', 'Btn_ShowWheel', 'Btn_ShowZoomIn', 'Btn_GarSetActive', 'Btn_GarVeh_truck_light_01', 'Btn_GarVeh_bus_city_01', 'Btn_GarCat_paint']) check(!!tg[k], `${k} is a clickable target on the Garage page`);

    // camera: real mouse input on the canvas drives the orbit camera
    const box = await c.page.locator('#unity-canvas').boundingBox(); const cx = box.x + box.width * 0.5625, cy = box.y + box.height * 0.44;
    m = c.logs.length; await c.page.mouse.move(cx, cy); await c.page.mouse.down(); for (let i = 1; i <= 10; i++) { await c.page.mouse.move(cx + i * 14, cy + i * 2); await sleep(120); } await c.page.mouse.up();
    let cam = await c.waitLog(/\[Showroom\] camera view=free yaw=(-?\d+)/, m, 60);
    check(!!cam, 'dragging in the viewer orbits the camera', cam);
    const d0 = cam ? +/distance=([\d.]+)/.exec(cam)[1] : 0;
    m = c.logs.length; await c.page.mouse.move(cx, cy); for (let i = 0; i < 6; i++) { await c.page.mouse.wheel(0, -300); await sleep(200); }
    cam = await c.waitLog(/\[Showroom\] camera view=free yaw=-?\d+ pitch=-?\d+ distance=([\d.]+)/, m, 60);
    check(!!cam && +/distance=([\d.]+)/.exec(cam)[1] < d0, 'the mouse wheel zooms in', cam);
    m = c.logs.length; for (let i = 0; i < 30; i++) { await c.page.mouse.wheel(0, -600); await sleep(120); }
    cam = await c.waitLog(/\[Showroom\] camera view=free .* distance=([\d.]+) min=([\d.]+)/, m, 60);
    const mm = cam ? /distance=([\d.]+) min=([\d.]+)/.exec(cam) : null;
    check(!!mm && +mm[1] >= +mm[2] - 0.06, 'zooming stops at the camera limit (never inside the vehicle)', cam);
    m = c.logs.length; await c.click('Btn_ShowFront');
    cam = await c.waitLog(/\[Showroom\] camera view=front yaw=(-?\d+)/, m, 60); check(!!cam && /yaw=0 /.test(cam + ' '), 'the Front preset looks at the vehicle from the front', cam);
    cam = await c.clickUntil('Btn_ShowRear', /\[Showroom\] camera view=rear yaw=(-?\d+)/, 25, 3); check(!!cam && /yaw=(180|-180) /.test(cam + ' '), 'the Rear preset looks from behind', cam);
    cam = await c.clickUntil('Btn_ShowTop', /\[Showroom\] camera view=top yaw=-?\d+ pitch=(\d+)/, 25, 3); check(!!cam && +/pitch=(\d+)/.exec(cam)[1] >= 80, 'the Top preset looks down', cam);
    await c.settle(); await c.page.screenshot({ path: `${prefix}-31-garage-top.png` });
    cam = await c.clickUntil('Btn_ShowReset', /\[Showroom\] camera view=default/, 25, 3); check(!!cam, 'Reset returns to the default view', cam);
    m = c.logs.length; await c.click('Btn_Show360'); cam = await c.waitLog(/\[Showroom\] camera view=turntable/, m, 60); check(!!cam, '360 view turns the vehicle on a turntable', cam);
    await sleep(2500); await c.click('Btn_ShowExterior');
    m = c.logs.length; await c.page.mouse.click(cx, cy); check(!!(await c.waitLog(/\[Showroom\] pick part=\w+/, m, 60)), 'clicking the vehicle picks a part');
    await c.settle(); await c.page.screenshot({ path: `${prefix}-32-garage-part.png` });

    // paint: preview, buy, equip (the server decides and the 3D model follows)
    m = c.logs.length; await c.click('Btn_Skin_sunset_gold');
    check(!!(await c.waitLog(/\[Garage\] preview skin=sunset_gold base=#d9a21b/, m, 60)), 'selecting a skin previews its colour on the 3D vehicle');
    await c.settle(); await c.page.screenshot({ path: `${prefix}-33-garage-skin-preview.png` });
    const bal0 = +db(`select balance from player_wallets where player_id = '${gabe.id}'`);
    await c.click('Btn_SkinAct_sunset_gold'); await c.settle(); check(!!c.targets()['Btn_SkinBuyConfirm'], 'buying a skin asks for confirmation');
    check(db(`select count(*) from player_skins where player_id = '${gabe.id}'`) === '0', 'nothing is bought before the player confirms');
    m = c.logs.length; await c.click('Btn_SkinBuyConfirm');
    check(!!(await c.waitLog(/\[Garage\] buy skin=sunset_gold ok=True/, m, 90)), 'Confirm purchase: the server accepted');
    check(db(`select count(*) from player_skins where player_id = '${gabe.id}' and skin_id = 'sunset_gold'`) === '1' && +db(`select balance from player_wallets where player_id = '${gabe.id}'`) === bal0 - 15000, 'the skin is owned and exactly its server price was debited');
    await c.settle(); m = c.logs.length; await c.click('Btn_SkinAct_sunset_gold');
    check(!!(await c.waitLog(/\[Garage\] skin id=sunset_gold ok=True/, m, 90)), 'Equip: the server accepted');
    check(db(`select customization->>'skin' from vehicle_ownership where player_id = '${gabe.id}' and definition_id = 'truck_light_01'`) === 'sunset_gold', 'the equipped skin is stored on the vehicle in the database');
    await c.settle(); await c.page.screenshot({ path: `${prefix}-34-garage-skin-equipped.png` });
    check(!!(await c.clickUntil('Btn_GarCat_wheels', /\[Garage\] category=wheels/, 30, 3)), 'a category the game has no system for opens its explanation, not fake content');   // software WebGL can drop a click while the 3D preview renders (run 129): retry the click, never the assertion await c.settle(); await c.page.screenshot({ path: `${prefix}-35-garage-wheels.png` });
    await c.click('Btn_GarTab_Upgrades'); await c.settle(); await c.page.screenshot({ path: `${prefix}-36-garage-upgrades.png` });

    // maintenance: prices from the server, the actions change the database
    m = c.logs.length; await c.click('Btn_GarTab_Maintenance');
    const q = await c.waitLog(/\[Garage\] quote state=ready repair=(\d+) refuel=(\d+) litres=(\d+)/, m, 60);
    check(!!q && +/repair=(\d+)/.exec(q)[1] === 300, 'the repair price shown is the server quote (30 % damage = 300)', q);
    await c.settle(); await c.page.screenshot({ path: `${prefix}-37-garage-maintenance.png` });
    const bal1 = +db(`select balance from player_wallets where player_id = '${gabe.id}'`);
    check(!!(await c.clickUntil('Btn_GarRepair', /\[Garage\] repair ok=True/, 25, 3)), 'Repair: the server accepted');
    check(db(`select damage_pct from vehicle_ownership where player_id = '${gabe.id}' and definition_id = 'truck_light_01'`) === '0' && +db(`select balance from player_wallets where player_id = '${gabe.id}'`) === bal1 - 300, 'the vehicle is repaired and exactly the quote was charged');
    await c.settle(); check(!!(await c.clickUntil('Btn_GarRefuel', /\[Garage\] refuel ok=True/, 25, 3)), 'Refuel: the server accepted');
    check(+db(`select fuel_l from vehicle_ownership where player_id = '${gabe.id}' and definition_id = 'truck_light_01'`) === +db(`select fuel_capacity_l from vehicle_definitions where id = 'truck_light_01'`), 'the tank is full in the database');

    // inspect another vehicle and make it the active one
    m = c.logs.length; await c.click('Btn_GarVeh_bus_city_01');
    check(!!(await c.waitLog(/\[Showroom\] ready vehicle=bus_city_01 model=placeholder/, m, 120)), 'selecting a vehicle loads it into the showroom');
    await c.settle(); await c.page.screenshot({ path: `${prefix}-38-garage-bus.png` });
    m = c.logs.length; await c.click('Btn_GarSetActive');
    check(!!(await c.waitLog(/\[Garage\] active id=bus_city_01 ok=True/, m, 60)), 'Set as Active: the server accepted');
    check(db(`select (p.selected_vehicle_id = o.id)::text from profiles p, vehicle_ownership o where p.id = '${gabe.id}' and o.player_id = p.id and o.definition_id = 'bus_city_01'`) === 'true', 'the active vehicle is persisted in the database');
  });

  // =========================================================================================== 2e. Settings: device settings (draft, save, persisted across a reload) and the two account-side privacy items
  const hana = await signup('BrowserHana'), ike = await signup('BrowserIke');
  asUser(hana, `select block_player('${ike.id}')`);
  await open(hana, '', async c => {
    check(!!(await c.waitLog(/\[Dashboard\] built for BrowserHana/, 0, 150)), 'Settings: the dashboard opens for the new player');
    check(!!(await c.waitLog(/\[Settings\] loaded changed=0 stored=no/, 0, 10)), 'a player with no saved settings starts from the defaults');
    await c.settle(); let m = c.logs.length; await c.click('Nav_Settings');
    check(!!(await c.waitLog(/\[Settings\] opened tab=game dirty=False/, m, 60)), 'Settings opens from the sidebar');
    check(!!(await c.waitLog(/\[Settings\] privacy nearby=false blocked=1/, m, 90)), 'the privacy items are read from the account (hidden from Players Nearby by default, one blocked player)');
    await c.settle(); await c.page.screenshot({ path: `${prefix}-40-settings.png` });
    let tg = c.targets();
    for (const k of ['Btn_SetTab_Game', 'Btn_SetTab_Graphics', 'Btn_SetTab_Controls', 'Btn_SetTab_Audio', 'Btn_SetTab_Gameplay', 'Btn_SetTab_Account', 'Btn_SetTab_Language', 'Btn_SetDrop_quality', 'Btn_SetDrop_speed_unit', 'Btn_SetDrop_time_of_day', 'Btn_SetDrop_traffic',
      'Btn_SetToggle_shadows', 'Btn_SetToggle_post', 'Btn_SetToggle_show_fps', 'Btn_SetToggle_minimap', 'Btn_SetToggle_show_names', 'Btn_SetToggle_appear_nearby', 'Slider_master_volume', 'Slider_engine_volume', 'Slider_camera_shake', 'Slider_steering', 'Btn_SetSave', 'Btn_SetReset', 'Btn_SetAdvanced']) check(!!tg[k], `${k} is a clickable target on the Settings page`);
    check(!tg['Btn_SetDrop_game_mode'] && !tg['Btn_SetToggle_weather'] && !tg['Slider_voice_volume'] && !tg['Btn_SetToggle_profanity'] && !tg['Slider_brake_sens'], 'rows the game has no system for (game mode, weather, voice, profanity, brake sensitivity) have no working control');
    m = c.logs.length; await c.click('Btn_SetNote_game_mode'); check(!!(await c.waitLog(/\[Settings\] note id=game_mode/, m, 30)), 'a row that is not available yet explains why when tapped');

    // a dropdown: open, choose, the draft changes
    m = c.logs.length; await c.click('Btn_SetDrop_quality'); await c.settle(); check(!!c.targets()['Btn_SetOpt_quality_high'], 'the dropdown lists its options');
    await c.page.screenshot({ path: `${prefix}-41-settings-dropdown.png` });
    m = c.logs.length; await c.click('Btn_SetOpt_quality_high'); check(!!(await c.waitLog(/\[Settings\] set quality=high/, m, 40)), 'choosing Graphics Quality = High changes the draft');
    await c.settle(); m = c.logs.length; await c.click('Btn_SetDrop_speed_unit'); await c.settle(); await c.click('Btn_SetOpt_speed_unit_mph'); check(!!(await c.waitLog(/\[Settings\] set speed_unit=mph/, m, 40)), 'Speed Unit = mph');
    await c.settle(); m = c.logs.length; await c.click('Btn_SetDrop_time_of_day'); await c.settle(); await c.click('Btn_SetOpt_time_of_day_night'); check(!!(await c.waitLog(/\[Settings\] set time_of_day=night/, m, 40)), 'Time of Day = Night');
    // toggles
    await c.settle(); m = c.logs.length; await c.click('Btn_SetToggle_shadows'); check(!!(await c.waitLog(/\[Settings\] set shadows=0/, m, 40)), 'Shadows off');
    await c.settle(); m = c.logs.length; await c.click('Btn_SetToggle_show_fps'); check(!!(await c.waitLog(/\[Settings\] set show_fps=1/, m, 40)), 'FPS counter on');
    await c.settle(); m = c.logs.length; await c.click('Btn_SetToggle_minimap'); check(!!(await c.waitLog(/\[Settings\] set minimap=0/, m, 40)), 'Minimap off');
    // a slider, with the real mouse
    await c.settle(); tg = c.targets(); const box = await c.page.locator('#unity-canvas').boundingBox(), sc = box.width / 1920, sl = tg['Slider_master_volume'];
    const trackPx = (0.55 * (511 - 16) - 6 - 62) * sc, sx = box.x + sl.x * box.width, sy = box.y + sl.y * box.height;
    m = c.logs.length; await c.page.mouse.move(sx, sy); await c.page.mouse.down(); for (let i = 1; i <= 8; i++) { await c.page.mouse.move(sx - i * trackPx * 0.03, sy); await sleep(100); } await c.page.mouse.up();
    const sv = await c.waitLog(/\[Settings\] set master_volume=(\d+)/, m, 40); const mv = sv ? +/master_volume=(\d+)/.exec(sv)[1] : -1;
    check(mv >= 15 && mv <= 35, `dragging the Master Volume slider with the mouse sets about 25 % (got ${mv})`, sv);
    await c.settle(); await c.page.screenshot({ path: `${prefix}-42-settings-edited.png` });

    // Reset restores the defaults in the draft, without saving
    m = c.logs.length; await c.click('Btn_SetReset'); check(!!(await c.waitLog(/\[Settings\] reset to defaults \(not saved yet\)/, m, 40)), 'Reset to Default Settings resets the draft (not saved yet)');
    await c.settle(); m = c.logs.length; await c.click('Btn_SetSave'); check(!!(await c.waitLog(/\[Settings\] save nothing/, m, 40)), 'after a reset there is nothing to save: Save says so');

    // choose again and Save: stored and applied
    await c.settle(); m = c.logs.length; await c.click('Btn_SetDrop_quality'); await c.settle(); await c.click('Btn_SetOpt_quality_high'); await c.waitLog(/\[Settings\] set quality=high/, m, 40);
    await c.settle(); await c.click('Btn_SetToggle_shadows'); await c.waitLog(/\[Settings\] set shadows=0/, m, 40);
    await c.settle(); await c.click('Btn_SetDrop_speed_unit'); await c.settle(); await c.click('Btn_SetOpt_speed_unit_mph'); await c.waitLog(/\[Settings\] set speed_unit=mph/, m, 40);
    await c.settle(); tg = c.targets(); m = c.logs.length; await c.page.mouse.move(box.x + tg['Slider_master_volume'].x * box.width, box.y + tg['Slider_master_volume'].y * box.height); await c.page.mouse.down();
    for (let i = 1; i <= 8; i++) { await c.page.mouse.move(box.x + tg['Slider_master_volume'].x * box.width - i * trackPx * 0.03, box.y + tg['Slider_master_volume'].y * box.height); await sleep(100); } await c.page.mouse.up(); await c.waitLog(/\[Settings\] set master_volume=\d+/, m, 40);
    await c.settle(); m = c.logs.length; await c.click('Btn_SetSave');
    const sv2 = await c.waitLog(/\[Settings\] saved changed=(\S+) stored=true reload=true/, m, 40);
    check(!!sv2 && /quality/.test(sv2) && /shadows/.test(sv2) && /speed_unit/.test(sv2) && /master_volume/.test(sv2), 'Save stores exactly the rows that changed and says one of them applies on the next load', sv2);
    const ap = await c.waitLog(/\[Settings\] applied master=0\.\d+ .*shadows=False .*speed=mph quality=high .*startup=False/, m, 40);
    check(!!ap && +/master=(0\.\d+)/.exec(ap)[1] < 0.4, 'the saved settings are applied to the running game (volume, shadows, speed unit, quality choice)', ap);
    await c.settle(); await c.page.screenshot({ path: `${prefix}-43-settings-saved.png` });

    // other tabs
    for (const [tab, shot] of [['Graphics', '44-settings-graphics'], ['Controls', '45-settings-controls'], ['Audio', '46-settings-audio'], ['Language', '47-settings-language']]) {
      m = c.logs.length; await c.click('Btn_SetTab_' + tab); check(!!(await c.waitLog(new RegExp('\\[Settings\\] tab=' + tab.toLowerCase()), m, 40)), `the ${tab} tab opens`); await c.settle(); await c.page.screenshot({ path: `${prefix}-${shot}.png` });
    }

    // privacy: Appear in Nearby Players is saved to the account at once; a blocked player can be unblocked
    m = c.logs.length; await c.click('Btn_SetTab_Gameplay'); check(!!(await c.waitLog(/\[Settings\] privacy nearby=false blocked=1/, m, 90)), 'the Gameplay tab re-reads the privacy items'); await c.settle();
    await c.page.screenshot({ path: `${prefix}-48-settings-gameplay.png` });
    const hanaCountry = db(`select coalesce(country_code, 'none') from profiles where id = '${hana.id}'`);
    m = c.logs.length; await c.click('Btn_SetToggle_appear_nearby'); check(!!(await c.waitLog(/\[Settings\] nearby visible=true ok=True/, m, 60)), 'Appear in Nearby Players on (an explicit choice, off by default): the server accepted');
    check(db(`select appear_nearby::text from profiles where id = '${hana.id}'`) === 'true', 'the opt-in is stored on the account in the database');
    await c.settle(); m = c.logs.length; await c.click('Btn_SetToggle_appear_nearby'); check(!!(await c.waitLog(/\[Settings\] nearby visible=false ok=True/, m, 60)), 'Appear in Nearby Players off again: the server accepted');
    check(db(`select appear_nearby::text from profiles where id = '${hana.id}'`) === 'false', 'the opt-out is stored on the account in the database');
    check(db(`select coalesce(country_code, 'none') from profiles where id = '${hana.id}'`) === hanaCountry, `switching Players Nearby on and off never changes the player's country (${hanaCountry})`);
    await c.settle(); m = c.logs.length; await c.click('Btn_SetUnblock_0'); check(!!(await c.waitLog(/\[Settings\] unblock ok=True/, m, 60)), 'Unblock: the server accepted');
    check(db(`select count(*) from player_blocks where blocker_id = '${hana.id}'`) === '0', 'the block is removed in the database');
    await c.settle(); await c.page.screenshot({ path: `${prefix}-49-settings-unblocked.png` });

    // account tab
    m = c.logs.length; await c.click('Btn_SetTab_Account'); await c.waitLog(/\[Settings\] tab=account/, m, 40); await c.settle(); await c.page.screenshot({ path: `${prefix}-50-settings-account.png` });
    check(!!c.targets()['Btn_SetSignOut'], 'Sign Out is offered on the Account tab');
    m = c.logs.length; await c.click('Btn_SetSoon_DeleteAccount'); check(!!(await c.waitLog(/\[Settings\] note id=DeleteAccount/, m, 30)), 'account deletion says it is not available yet instead of pretending');

    // unsaved changes are discarded when the player leaves, and said so
    await c.click('Btn_SetTab_Game'); await c.settle(); m = c.logs.length; await c.click('Btn_SetToggle_post'); await c.waitLog(/\[Settings\] set post=0/, m, 40);
    await c.settle(); m = c.logs.length; await c.click('Nav_Home'); check(!!(await c.waitLog(/\[Settings\] closed \(unsaved changes discarded\)/, m, 40)), 'leaving with unsaved changes discards them and says so');

    // the saved settings survive a reload and shape the world: Graphics Quality = High is built as the High tier
    m = c.logs.length; await c.page.reload();
    const ld = await c.waitLog(/\[Settings\] loaded changed=(\d+) stored=yes/, m, 120);
    check(!!ld && +/changed=(\d+)/.exec(ld)[1] >= 4, 'after a reload the saved settings are read back from the browser', ld);
    check(!!(await c.waitLog(/\[Settings\] applied master=0\.\d+ .*shadows=False .*speed=mph quality=high .*startup=True/, m, 60)), 'and applied before the world is built');
    check(!!(await c.waitLog(/\[Render\] tier=high /, m, 240)), 'Graphics Quality = High: the world is built with the High tier');
  });

  // =========================================================================================== 2f. Friends: every list, request and action against the real backend and database
  const fin = await signup('BrowserFin'), fg = await signup('BrowserFg'), fh = await signup('BrowserFh'), fr = await signup('BrowserFr'), fd = await signup('BrowserFd'), fo = await signup('BrowserFo'), fe = await signup('BrowserFe'), fx = await signup('BrowserFx');
  const befriend = (a, b) => { asUser(a, `select send_friend_request('${b.id}')::text`); const id = db(`select id from friendships where requester_id = '${a.id}' and addressee_id = '${b.id}'`); asUser(b, `select respond_friend_request('${id}', true)`); };
  befriend(fin, fg); befriend(fin, fh);                                                          // two friends
  asUser(fr, `select send_friend_request('${fin.id}')::text`); asUser(fd, `select send_friend_request('${fin.id}')::text`);       // two incoming requests
  asUser(fin, `select send_friend_request('${fo.id}')::text`);                                    // one request sent
  db(`insert into player_encounters(player_id, other_id, last_at, times) values ('${fin.id}', '${fe.id}', now() - interval '2 hours', 3), ('${fe.id}', '${fin.id}', now() - interval '2 hours', 3)`);   // someone met in a convoy
  asUser(fg, `select start_session('webgl')::text`);
  asUser(fg, `select heartbeat('${db(`select id from game_sessions where player_id = '${fg.id}' and ended_at is null limit 1`)}', 'driving', 'lagos-ibadan', 'lagos')::text`);      // fg is online and driving
  const friendsInDb = () => +db(`select count(*) from friendships where status = 'accepted' and '${fin.id}' in (requester_id, addressee_id)`);
  await open(fin, '', async c => {
    check(!!(await c.waitLog(/\[Dashboard\] built for BrowserFin/, 0, 150)), 'Friends: the dashboard opens for the new player');
    await c.settle(); let m = c.logs.length; await c.click('Nav_Friends');
    check(!!(await c.waitLog(/\[Friends\] opened/, m, 60)), 'Friends opens from the sidebar (it is no longer "coming soon")');
    const fl = await c.waitLog(/\[Friends\] friends state=ready count=(\d+) online=(\d+)/, m, 90);
    check(!!fl && /count=2 online=1/.test(fl), 'the friend list is the database list: 2 friends, 1 of them online', fl);
    const rq = await c.waitLog(/\[Friends\] requests state=ready incoming=(\d+) outgoing=(\d+)/, m, 90);
    check(!!rq && /incoming=2 outgoing=1/.test(rq), 'requests: 2 incoming, 1 sent (as in the database)', rq);
    check(!!(await c.waitLog(/\[Friends\] recent state=ready count=1/, m, 90)), 'recent players: the one player met in a convoy');
    await c.settle(); await c.page.screenshot({ path: `${prefix}-60-friends.png` });
    let tg = c.targets();
    for (const k of ['Btn_FriTab_All', 'Btn_FriTab_Online', 'Btn_FriTab_Requests', 'Btn_FriTab_Recent', 'Btn_FriTab_Find', 'Fri_Search_Input', 'Btn_FriAdd', 'Btn_FriSort', 'Btn_FriProfile_browserfg', 'Btn_FriInvite_browserfg', 'Btn_FriMessage_browserfg', 'Btn_FriMore_browserfg', 'Btn_FriSideAccept_browserfr', 'Btn_FriSideDecline_browserfr', 'Btn_FriSideAdd_browserfe', 'Btn_FriMeProfile', 'Btn_FriSideViewRequests', 'Btn_FriSideViewRecent']) check(!!tg[k], `${k} is a clickable target on the Friends page`);

    // presence from the server: the online friend shows what they are doing; Message does not pretend
    m = c.logs.length; await c.click('Btn_FriMessage_browserfg'); check(!!(await c.waitLog(/\[Friends\] note: Messages are not available yet/, m, 30)), 'Message says it is not available yet instead of pretending');
    m = c.logs.length; await c.click('Btn_FriInvite_browserfg'); check(!!(await c.waitLog(/\[Friends\] note: Join or create a convoy first/, m, 30)), 'Invite Convoy explains that the player has to be in a convoy first');
    asUser(fin, `select create_convoy('Fin Run')`);                                               // now the player is in a convoy: the page notices at its next refresh
    m = c.logs.length; check(!!(await c.waitLog(/\[Friends\] convoy member=true/, m, 60)), 'the page refreshes by itself (every 15 s) and learns the player is in a convoy'); await c.settle();
    m = c.logs.length; await c.click('Btn_FriInvite_browserfg'); check(!!(await c.waitLog(/\[Friends\] invite id=browserfg ok=True/, m, 60)), 'Invite Convoy: the server accepted');
    check(db(`select count(*) from convoy_invites where invitee_id = '${fg.id}' and inviter_id = '${fin.id}' and status = 'pending'`) === '1', 'the invitation exists in the database');
    m = c.logs.length; await c.settle(); await c.click('Btn_FriInvite_browserfg'); check(!!(await c.waitLog(/\[Friends\] invite id=browserfg ok=False error=already_invited/, m, 60)), 'a second invitation is refused by the server and reported (already invited)');

    // search filters the loaded list (name or username); a miss says so
    m = c.logs.length; await c.click('Fri_Search_Input'); await sleep(500); await c.page.keyboard.type('fg', { delay: 120 });
    check(!!(await c.waitLog(/\[Friends\] search="fg" tab=all shown=1/, m, 60)), 'searching "fg" narrows the list to the one matching friend'); await c.settle(); await c.page.screenshot({ path: `${prefix}-61-friends-search.png` });
    for (let i = 0; i < 2; i++) await c.page.keyboard.press('Backspace');
    m = c.logs.length; await c.page.keyboard.type('zzzz', { delay: 100 });
    check(!!(await c.waitLog(/\[Friends\] search="zzzz" tab=all shown=0/, m, 60)), 'a search with no match shows nothing (and the screen says so)'); await c.settle(); await c.page.screenshot({ path: `${prefix}-62-friends-nomatch.png` });
    for (let i = 0; i < 4; i++) await c.page.keyboard.press('Backspace');
    await c.waitLog(/\[Friends\] search="" tab=all/, m, 60);

    // tabs and sort
    m = c.logs.length; await c.click('Btn_FriTab_Online'); check(!!(await c.waitLog(/\[Friends\] tab=online/, m, 40)), 'the Online tab opens'); await c.settle(); await c.page.screenshot({ path: `${prefix}-63-friends-online.png` });
    await c.click('Btn_FriTab_All'); await c.settle(); m = c.logs.length; await c.click('Btn_FriSort'); await c.settle(); check(!!c.targets()['Btn_FriSortOpt_Name'], 'the sort menu lists its options');
    await c.page.screenshot({ path: `${prefix}-64-friends-sort.png` }); m = c.logs.length; await c.click('Btn_FriSortOpt_Name'); check(!!(await c.waitLog(/\[Friends\] sort=Name/, m, 40)), 'sorting by name is applied');

    // requests: accept one from the side panel, decline one in the Requests tab, cancel the one that was sent
    await c.settle(); m = c.logs.length; await c.click('Btn_FriSideAccept_browserfr');
    check(!!(await c.waitLog(/\[Friends\] respond id=browserfr accept=True ok=True result=accepted/, m, 60)), 'Accept: the server accepted');
    check(db(`select status from friendships where requester_id = '${fr.id}' and addressee_id = '${fin.id}'`) === 'accepted', 'the friendship is accepted in the database');
    check(!!(await c.waitLog(/\[Friends\] friends state=ready count=3 /, m, 60)), 'the friend list is read again and now has 3 friends');
    await c.settle(); m = c.logs.length; await c.click('Btn_FriTab_Requests'); check(!!(await c.waitLog(/\[Friends\] tab=requests/, m, 40)), 'the Requests tab opens'); await c.settle(); await c.page.screenshot({ path: `${prefix}-65-friends-requests.png` });
    tg = c.targets(); check(!!tg['Btn_FriDecline_browserfd'] && !!tg['Btn_FriAccept_browserfd'] && !!tg['Btn_FriCancel_browserfo'], 'the Requests tab offers Accept and Decline for incoming and Cancel for sent requests');
    m = c.logs.length; await c.click('Btn_FriDecline_browserfd'); check(!!(await c.waitLog(/\[Friends\] respond id=browserfd accept=False ok=True result=rejected/, m, 60)), 'Decline: the server accepted');
    check(db(`select status from friendships where requester_id = '${fd.id}' and addressee_id = '${fin.id}'`) === 'rejected', 'the request is rejected in the database');
    await c.settle(); m = c.logs.length; await c.click('Btn_FriCancel_browserfo'); check(!!(await c.waitLog(/\[Friends\] cancel id=browserfo ok=True/, m, 60)), 'Cancel Request: the server accepted');
    check(db(`select count(*) from friendships where requester_id = '${fin.id}' and addressee_id = '${fo.id}'`) === '0', 'the sent request is gone from the database');

    // recent players: add one
    await c.settle(); m = c.logs.length; await c.click('Btn_FriTab_Recent'); check(!!(await c.waitLog(/\[Friends\] tab=recent/, m, 40)), 'the Recent Players tab opens'); await c.settle(); await c.page.screenshot({ path: `${prefix}-66-friends-recent.png` });
    m = c.logs.length; await c.click('Btn_FriAddFriend_browserfe'); check(!!(await c.waitLog(/\[Friends\] add id=browserfe ok=True status=pending/, m, 60)), 'Add Friend from Recent Players: the server accepted');
    check(db(`select status from friendships where requester_id = '${fin.id}' and addressee_id = '${fe.id}'`) === 'pending', 'the request is pending in the database');
    await c.settle(); check(!!c.targets()['Btn_FriSent_browserfe'] && !c.targets()['Btn_FriAddFriend_browserfe'], 'the card now says "Request Sent" and cannot be added again');

    // find players: exact username, from the backend
    await c.clickUntil('Btn_FriTab_Find', /\[Friends\] tab=find/, 30, 3); await c.settle(); await c.page.screenshot({ path: `${prefix}-67-friends-find.png` });      // a click the software-WebGL player drops is repeated, the assertions are not weakened
    m = c.logs.length; await c.click('Fri_Search_Input'); await sleep(500); await c.page.keyboard.type('nobody_here', { delay: 100 }); await sleep(600); await c.click('Btn_FriFindGo');
    check(!!(await c.waitLog(/\[Friends\] find "nobody_here" result=not_found/, m, 60)), 'a username that does not exist is reported as not found'); await c.settle(); await c.page.screenshot({ path: `${prefix}-68-friends-notfound.png` });
    await c.click('Fri_Search_Input'); await sleep(500); await wipe(c);                              // the box lost focus when the search ran: focus it again and empty it
    m = c.logs.length; await c.page.keyboard.type('browserfx', { delay: 100 }); await sleep(600); await c.click('Btn_FriFindGo');
    check(!!(await c.waitLog(/\[Friends\] find "browserfx" result=found status=none/, m, 60)), 'an exact username is found by the server');
    await c.settle(); await c.page.screenshot({ path: `${prefix}-69-friends-found.png` }); m = c.logs.length; await c.click('Btn_FriAddFriend_browserfx');
    check(!!(await c.waitLog(/\[Friends\] add id=browserfx ok=True status=pending/, m, 60)), 'Add Friend on a search result: the server accepted');
    check(db(`select status from friendships where requester_id = '${fin.id}' and addressee_id = '${fx.id}'`) === 'pending', 'the request is pending in the database');

    // profiles, from the server (the list is shown again with an empty search box)
    await c.click('Fri_Search_Input'); await sleep(500); await wipe(c); await sleep(800); await c.click('Btn_FriTab_All'); await c.settle(); m = c.logs.length;
    const pv = await c.clickUntil('Btn_FriProfile_browserfg', /\[Friends\] profile id=browserfg ok=True.* level=(\d+) jobs=(\d+) status=friends/, 25, 3);   // a click the software-WebGL player drops is repeated (as elsewhere in this file); the assertion is unchanged
    check(!!pv, 'View Profile loads the player\'s public profile from the server', pv);
    await c.settle(); await c.page.screenshot({ path: `${prefix}-70-friends-profile.png` }); tg = c.targets(); check(!!tg['Btn_FriProfileInvite'] && !!tg['Btn_FriProfileRemove'], 'the profile of a friend offers Invite to Convoy and Remove Friend');
    await c.click('Btn_CloseDialog'); await c.settle(); m = c.logs.length; check(!!(await c.clickUntil('Btn_FriMeProfile', /\[Friends\] profile id=browserfin ok=True .*status=self/, 25, 3)), 'View Profile on your own card shows your public profile');
    await c.click('Btn_CloseDialog');

    // remove a friend (with a confirmation) and block another
    await c.settle(); m = c.logs.length; await c.click('Btn_FriMore_browserfh'); await c.settle(); tg = c.targets(); check(!!tg['Btn_FriMenu_RemoveFriend'] && !!tg['Btn_FriMenu_ViewProfile'] && !!tg['Btn_FriMenu_BlockPlayer'], 'the more menu offers View Profile, Remove Friend and Block Player');
    await c.page.screenshot({ path: `${prefix}-71-friends-menu.png` }); await c.click('Btn_FriMenu_RemoveFriend'); await c.settle();
    check(friendsInDb() === 3, 'nothing is removed before the player confirms'); await c.page.screenshot({ path: `${prefix}-72-friends-confirm.png` });
    m = c.logs.length; await c.click('Btn_FriConfirm'); check(!!(await c.waitLog(/\[Friends\] remove id=browserfh ok=True/, m, 60)), 'Remove Friend: the server accepted');
    check(db(`select count(*) from friendships where status = 'accepted' and '${fh.id}' in (requester_id, addressee_id)`) === '0', 'the friendship is gone from the database');
    await c.settle(); m = c.logs.length; await c.click('Btn_FriMore_browserfg'); await c.settle(); await c.click('Btn_FriMenu_BlockPlayer'); await c.settle(); m = c.logs.length; await c.click('Btn_FriConfirm');
    check(!!(await c.waitLog(/\[Friends\] block id=browserfg ok=True/, m, 60)), 'Block Player: the server accepted');
    check(db(`select count(*) from player_blocks where blocker_id = '${fin.id}' and blocked_id = '${fg.id}'`) === '1', 'the block is stored in the database');

    // the same page at other window sizes (the stage scales; nothing may break or throw)
    for (const [w, h] of [[1920, 1080], [1440, 900], [1280, 720], [1024, 768], [768, 1024], [390, 844]]) {
      await c.page.setViewportSize({ width: w, height: h }); await sleep(2500); await c.page.screenshot({ path: `${prefix}-8-friends-${w}x${h}.png` });
    }
    await c.page.setViewportSize({ width: 1024, height: 576 }); await sleep(1500);
    check(c.errors.length === 0, 'resizing the window through six sizes causes no page errors', c.errors[0]);
  });
  // a service that fails shows an error with a working retry; a player with no friends sees an empty state, not a blank panel
  const fka = await signup('BrowserFk');
  await open(fka, '&fault=list_friends:error:3', async c => {
    await c.waitLog(/\[Dashboard\] built for BrowserFk/, 0, 150); await c.settle(); let m = c.logs.length; await c.click('Nav_Friends');
    check(!!(await c.waitLog(/\[Friends\] friends state=unavailable/, m, 120)), 'friends that cannot load say "unavailable" instead of showing an empty list');
    await c.settle(); await c.page.screenshot({ path: `${prefix}-73-friends-error.png` });
    check(!!c.targets()['Btn_FriRetryFriends'], 'a Try again button is offered');
    m = c.logs.length; await c.click('Btn_FriRetryFriends'); check(!!(await c.waitLog(/\[Friends\] friends state=empty count=0/, m, 90)), 'Try again reaches the server and the empty state is shown for a player with no friends');
    await c.settle(); await c.page.screenshot({ path: `${prefix}-74-friends-empty.png` }); check(!!c.targets()['Btn_FriEmptyFind'], 'the empty state offers Find Players');
    m = c.logs.length; await c.click('Btn_FriEmptyFind'); check(!!(await c.waitLog(/\[Friends\] tab=find/, m, 40)), 'the empty state leads to Find Players');
  });

  // =========================================================================================== 2g. Leaderboards: boards, podium, Your Rank, friends, search, pagination, profile, failure states
  const lbNames = 'ABCDEFGHIJKLMNO'.split('').map(l => 'BrowserLb' + l); const lbu = [];
  for (const n of lbNames) lbu.push(await signup(n));
  const lbv = lbu[13];                                                                           // the viewer: rank 14 of the group on the XP board, so Your Rank is NOT on the first page
  lbu.forEach((u, i) => db(`update profiles set experience = ${5000000 - i * 1000}, distance_km = ${900000 - i * 100}, jobs_completed = ${700000 - i * 10}, lifetime_earnings = ${9000000 + i * 1000} where id = '${u.id}'`));      // server records, set the way the server would; earnings run the other way round
  db(`select _apply_transaction('${lbu[0].id}', 'BONUS', 5000, 'e2e-lb:' || gen_random_uuid())`);        // this week's earnings come from the ledger
  const befriendLb = (a, b) => { asUser(a, `select send_friend_request('${b.id}')::text`); const id = db(`select id from friendships where requester_id = '${a.id}' and addressee_id = '${b.id}'`); asUser(b, `select respond_friend_request('${id}', true)`); };
  befriendLb(lbv, lbu[0]); befriendLb(lbv, lbu[2]); befriendLb(lbv, lbu[14]);                      // friends ranked 1st, 3rd and 15th overall: 4th..1st among friends, the viewer is third
  // countries for the first four players (the rest have none: an existing account from before onboarding); the second is hidden from Players Nearby on purpose: its flag must stay on the board
  const shipped = new Set(fs.readdirSync(path.join(__dirname, '..', 'Assets', '_Project', 'Resources', 'Flags')).filter(f => f.endsWith('.png')).map(f => f.slice(0, -4)));
  db(`insert into countries(code, name) values ('FR', 'France') on conflict do nothing`);
  [['NG', 0], ['GH', 1], ['CI', 2], ['FR', 3]].forEach(([cc, i]) => db(`update profiles set country_code = '${cc}' where id = '${lbu[i].id}'`));
  db(`update profiles set appear_nearby = false where id = '${lbu[1].id}'`);
  const rankOf = (col, id) => +db(`select count(*) + 1 from profiles where ${col} > (select ${col} from profiles where id = '${id}')`);
  const ranked = col => +db(`select count(*) from profiles where ${col} > 0`);
  await open(lbv, '', async c => {
    check(!!(await c.waitLog(/\[Dashboard\] built for BrowserLbN/, 0, 150)), 'Leaderboards: the dashboard opens for the viewer');
    await c.settle(); let m = c.logs.length; await c.click('Nav_Leaderboards');
    check(!!(await c.waitLog(/\[Leaderboards\] opened/, m, 60)), 'Leaderboards opens from the sidebar (it is no longer "coming soon")');
    const xpTotal = ranked('experience'), xpRank = rankOf('experience', lbv.id);
    const b1 = await c.waitLog(/\[Leaderboards\] board metric=xp period=all page=1 state=ready/, m, 90);
    check(!!b1 && b1.includes(`shown=12 total=${xpTotal} me=${xpRank}`), `the XP board is the database board: 12 rows on the page, ${xpTotal} ranked players, and I am rank ${xpRank}`, b1);
    const top12 = db(`select coalesce(country_code, '') from profiles where experience > 0 order by experience desc, id limit 12`).split('\n').map(s => s.trim());
    const expKnown = top12.filter(cc => shipped.has(cc.toLowerCase())).map(cc => cc.toLowerCase()), expNoArt = top12.filter(cc => cc && !shipped.has(cc.toLowerCase())).length;
    const fl = await c.waitLog(/\[Leaderboards\] flags view=table rows=12 /, m, 60);
    check(!!fl && fl.includes(`known=${expKnown.length} noartwork=${expNoArt} notset=${12 - expKnown.length - expNoArt} drawn=${expKnown.join(',')}`), `the board draws the flag of each player's stored country (${expKnown.join(',')}), the neutral placeholder for a country with no artwork (${expNoArt}) and for no country (${12 - expKnown.length - expNoArt})`, fl);
    check(expKnown.join(',') === 'ng,gh,ci', 'the players with a country are the first three, in rank order: Nigeria, Ghana (hidden from Players Nearby, flag still shown), Cote d\'Ivoire', expKnown.join(','));
    const fb1 = await c.waitLog(/\[Leaderboards\] friends metric=xp period=all state=ready/, m, 90);
    check(!!fb1 && /players=4 me=3/.test(fb1), 'the friends board is the database friends board: me and 3 friends, I am third', fb1);
    check(!!(await c.waitLog(/\[Leaderboards\] weekly state=(ready|empty)/, m, 90)), 'Top Drivers This Week is read from the server');
    await c.settle(); await c.page.screenshot({ path: `${prefix}-80-leaderboards.png` });
    let tg = c.targets();
    for (const k of ['Lb_Search_Input', 'Btn_LbTab_Xp', 'Btn_LbTab_Earnings', 'Btn_LbTab_Distance', 'Btn_LbTab_Jobs', 'Btn_LbPeriod', 'Btn_LbScope_Global', 'Btn_LbScope_Friends', 'Btn_LbPodium_1', 'Btn_LbPodium_2', 'Btn_LbPodium_3', 'Btn_LbRow_browserlbd', 'Btn_LbNext', 'Btn_LbMeProfile', 'Btn_LbFriend_browserlba', 'Btn_LbSideViewFriends']) check(!!tg[k], `${k} is a clickable target on the Leaderboards page`);
    check(!tg['Btn_LbPrev'] && !tg['Btn_LbRow_browserlbn'], 'page 1 has no Previous, and the viewer (rank 14) is not on it; Your Rank still shows them');

    // pagination: the next page is the next 12 from the server
    m = c.logs.length; await c.click('Btn_LbNext');
    const b2 = await c.waitLog(/\[Leaderboards\] board metric=xp period=all page=2 state=ready/, m, 60);
    check(!!b2 && b2.includes(`shown=${Math.min(12, xpTotal - 12)} total=${xpTotal} me=${xpRank}`), 'the next page asks the server for offset 12 and shows the rest', b2);
    await c.settle(); await c.page.screenshot({ path: `${prefix}-81-leaderboards-page2.png` }); tg = c.targets();
    check(!!tg['Btn_LbRow_browserlbn'] && !!tg['Btn_LbPrev'], 'the viewer\'s own row is on page 2 and Previous is offered');
    m = c.logs.length; await c.click('Btn_LbPrev'); check(!!(await c.waitLog(/\[Leaderboards\] page=1/, m, 30)), 'Previous goes back to page 1');

    // the podium and rows open the real public profile
    await c.settle(); m = c.logs.length; await c.click('Btn_LbPodium_1');
    check(!!(await c.waitLog(/\[Friends\] profile id=browserlba ok=True.* level=\d+ jobs=\d+ status=friends/, m, 60)), 'the first place opens that player\'s public profile from the server (status friends)');
    await c.settle(); await c.page.screenshot({ path: `${prefix}-82-leaderboards-profile.png` }); await c.click('Btn_CloseDialog'); await c.settle();
    m = c.logs.length; await c.click('Btn_LbRow_browserlbd'); check(!!(await c.waitLog(/\[Leaderboards\] profile browserlbd/, m, 30)) && !!(await c.waitLog(/\[Friends\] profile id=browserlbd ok=True/, m, 60)), 'a table row opens its player\'s profile'); await c.click('Btn_CloseDialog');
    await c.settle(); m = c.logs.length; await c.click('Btn_LbMeProfile'); check(!!(await c.waitLog(/\[Friends\] profile id=browserlbn ok=True .*status=self/, m, 60)), 'Your Rank opens your own public profile'); await c.click('Btn_CloseDialog');

    // categories: each is a real server board with its own order
    await c.settle(); m = c.logs.length; await c.click('Btn_LbTab_Earnings');
    const be = await c.waitLog(/\[Leaderboards\] board metric=earnings period=all page=1 state=ready/, m, 60); const erank = rankOf('lifetime_earnings', lbv.id), etotal = ranked('lifetime_earnings');
    check(!!be && be.includes(`total=${etotal} me=${erank}`), `the Earnings board is ordered by earnings: I am rank ${erank}, not ${xpRank}`, be);
    await c.settle(); await c.page.screenshot({ path: `${prefix}-83-leaderboards-earnings.png` });
    m = c.logs.length; await c.click('Btn_LbTab_Distance'); const bd = await c.waitLog(/\[Leaderboards\] board metric=distance period=all page=1 state=ready/, m, 60); check(!!bd && bd.includes(`me=${rankOf('distance_km', lbv.id)}`), 'the Distance board is the database distance board', bd);
    await c.settle(); m = c.logs.length; await c.click('Btn_LbTab_Jobs'); const bj = await c.waitLog(/\[Leaderboards\] board metric=jobs period=all page=1 state=ready/, m, 60); check(!!bj && bj.includes(`me=${rankOf('jobs_completed', lbv.id)}`), 'the Jobs board is the database jobs board', bj);

    // period: weekly exists for earnings, distance and jobs (server ledger); XP has no weekly record and says so
    await c.settle(); m = c.logs.length; await c.click('Btn_LbTab_Earnings'); await c.waitLog(/\[Leaderboards\] board metric=earnings period=all/, m, 60); await c.settle();
    m = c.logs.length; await c.click('Btn_LbPeriod'); await c.settle(); check(!!c.targets()['Btn_LbPeriodOpt_Week'] && !!c.targets()['Btn_LbPeriodOpt_AllTime'], 'the period menu lists All Time and This Week only');
    await c.page.screenshot({ path: `${prefix}-84-leaderboards-period.png` }); await c.click('Btn_LbPeriodOpt_Week');
    const bw = await c.waitLog(/\[Leaderboards\] board metric=earnings period=weekly page=1 state=ready/, m, 60);
    check(!!bw, 'This Week asks the server for this week\'s earnings board (from the transaction ledger)', bw);
    const weekTop = db(`select coalesce(sum(amount),0) from transactions where player_id = '${lbu[0].id}' and kind in ('JOB_REWARD','BONUS') and amount > 0 and created_at >= date_trunc('week', now())`); check(+weekTop >= 5000, 'the ledger the weekly board is computed from holds the bonus given in this test');
    await c.settle(); await c.page.screenshot({ path: `${prefix}-85-leaderboards-weekly.png` });
    m = c.logs.length; await c.click('Btn_LbTab_Xp'); check(!!(await c.waitLog(/\[Leaderboards\] board metric=xp period=all page=1 state=ready/, m, 60)), 'XP has no weekly record: the board falls back to All Time instead of inventing weekly XP');

    // friends scope
    await c.settle(); m = c.logs.length; check(!!(await c.clickUntil('Btn_LbScope_Friends', /\[Leaderboards\] scope=friends/, 30, 3)), 'the My Friends scope opens'); await c.settle(); await c.page.screenshot({ path: `${prefix}-86-leaderboards-friends.png` });
    tg = c.targets(); check(!!tg['Btn_LbRow_browserlba'] && !!tg['Btn_LbRow_browserlbc'] && !!tg['Btn_LbRow_browserlbo'] && !!tg['Btn_LbRow_browserlbn'] && !tg['Btn_LbRow_browserlbb'], 'the friends board lists me and my 3 friends, and nobody else');
    await c.click('Btn_LbScope_Global'); await c.settle();

    // search: the server finds ranked players by name, with their global rank
    m = c.logs.length; await c.click('Lb_Search_Input'); await sleep(500); await c.page.keyboard.type('x', { delay: 100 }); await sleep(1500);
    check(!c.logs.slice(m).some(l => /\[Leaderboards\] search/.test(l)), 'one character does not search (the server needs two)');
    await c.page.keyboard.type('browserlbm', { delay: 90 }); const sf = await c.waitLog(/\[Leaderboards\] search "xbrowserlbm"/, m, 60);
    check(!!sf && /state=empty found=0/.test(sf), 'a search with no match is reported as no match (server-side)', sf);
    await wipe(c);
    m = c.logs.length; await c.page.keyboard.type('browserlbm', { delay: 90 }); await c.settle();
    check(!!(await c.waitLog(/\[Leaderboards\] search "browserlbm" state=ready found=1 total=1/, m, 60)), 'searching a name finds exactly that player from the server');
    await c.page.screenshot({ path: `${prefix}-87-leaderboards-search.png` }); check(!!c.targets()['Btn_LbRow_browserlbm'] && !!c.targets()['Btn_LbSearchClear'], 'the result is a row that opens a profile, and the search can be cleared');
    m = c.logs.length; await c.click('Btn_LbSearchClear'); await c.settle(); check(!!c.targets()['Btn_LbNext'], 'clearing the search returns to the board');

    // the same page at other window sizes
    for (const [w, h] of [[1920, 1080], [1440, 900], [1280, 720], [1024, 768], [768, 1024], [390, 844]]) {
      await c.page.setViewportSize({ width: w, height: h }); await sleep(2500); await c.page.screenshot({ path: `${prefix}-8-leaderboards-${w}x${h}.png` });
    }
    await c.page.setViewportSize({ width: 1024, height: 576 }); await sleep(1500);
    check(c.errors.length === 0, 'Leaderboards at six window sizes cause no page errors', c.errors[0]);

    // an existing player with no country: Your Rank offers a clear way to add it (no forced onboarding), the profile's own country picker saves it, and the flag follows
    await c.settle(); tg = c.targets(); check(!!tg['Btn_LbAddCountry'], 'a player with no country is offered "Add your country" on Your Rank (the neutral placeholder, not a guess)');
    check(c.logs.some(l => /\[Leaderboards\] me flag=none/.test(l)), 'Your Rank shows no flag for a player who has no country');
    await c.page.screenshot({ path: `${prefix}-83-leaderboards-no-country.png` });
    m = c.logs.length; const addc = await c.clickUntil('Btn_LbAddCountry', /\[Leaderboards\] add country/, 30, 3);
    check(!!addc && !!(await c.waitLog(/\[Dashboard\] page=profile/, m, 40)), 'Add your country opens the profile (not onboarding)');
    await c.settle();
    tg = c.targets(); check(!!tg['Btn_PrCountry_GH'] && !!tg['Pr_Country_Search'], 'the country picker is open with the server\'s countries'); await c.page.screenshot({ path: `${prefix}-84-country-picker-flags.png` });
    m = c.logs.length; check(!!(await c.clickUntil('Btn_PrCountry_GH', /\[Profile\] country=GH ok=True/, 40, 3)), 'Ghana is saved through the profile country picker');
    check(db(`select country_code || '|' || appear_nearby::text from profiles where id = '${lbv.id}'`) === 'GH|false', 'the country is stored on the account and Players Nearby stays off (independent preferences)');
    await c.settle(); await c.page.screenshot({ path: `${prefix}-85-profile-flag.png` });
    check(!!(await c.waitLog(/\[Profile\] country flag=gh state=known/, m, 30)), 'the profile now shows the Ghana flag');
    m = c.logs.length; await c.click('Nav_Leaderboards'); check(!!(await c.waitLog(/\[Leaderboards\] me flag=gh/, m, 60)), 'back on Leaderboards, Your Rank shows the Ghana flag');
    await c.settle(); check(!c.targets()['Btn_LbAddCountry'], 'the Add-your-country link is gone once a country is set');
  });
  // a service that fails shows an error with a working retry; a player with no score sees "unranked", and a player with no friends sees the empty friends panel
  const lbk = await signup('BrowserLbQ');
  await open(lbk, '&fault=get_leaderboard:error:6,search_leaderboard:error:3', async c => {
    await c.waitLog(/\[Dashboard\] built for BrowserLbQ/, 0, 150); await c.settle(); let m = c.logs.length; await c.click('Nav_Leaderboards');
    check(!!(await c.waitLog(/\[Leaderboards\] board metric=xp period=all page=1 state=unavailable/, m, 120)), 'a board that cannot load says "unavailable" instead of showing an empty table');
    await c.settle(); await c.page.screenshot({ path: `${prefix}-88-leaderboards-error.png` });
    check(!!c.targets()['Btn_LbRetry'], 'a Retry button is offered');
    m = c.logs.length; await c.click('Btn_LbRetry'); const rr = await c.waitLog(/\[Leaderboards\] board metric=xp period=all page=1 state=ready/, m, 90);
    check(!!rr && /me=none/.test(rr), 'Retry reaches the server; a player with no XP is unranked (no invented rank)', rr);
    await c.settle(); await c.page.screenshot({ path: `${prefix}-89-leaderboards-unranked.png` });
    const fe2 = await c.waitLog(/\[Leaderboards\] friends metric=xp period=all state=(ready|empty)/, 0, 30); check(!!fe2 && /players=1/.test(fe2), 'a player with no friends has a friends board of one (themselves)', fe2);
    check(!!c.targets()['Btn_LbSideFindFriends'], 'the friends panel offers Find Friends');
    m = c.logs.length; await c.click('Lb_Search_Input'); await sleep(500); await c.page.keyboard.type('browserlb', { delay: 90 });
    check(!!(await c.waitLog(/\[Leaderboards\] search "browserlb" state=unavailable/, m, 90)), 'a search that fails says so'); await c.settle(); await c.page.screenshot({ path: `${prefix}-90-leaderboards-search-error.png` });
    m = c.logs.length; await c.click('Btn_LbSearchRetry'); check(!!(await c.waitLog(/\[Leaderboards\] search "browserlb" state=ready found=10/, m, 90)), 'search Retry reaches the server and shows its first 10 matches');
    m = c.logs.length; await c.click('Btn_LbSideFindFriends'); check(!!(await c.waitLog(/\[Friends\] opened/, m, 60)), 'Find Friends leads to the Friends screen');
  });

  // =========================================================================================== 2h. Onboarding (first run) and the player's own profile
  const taken = await signup('BrowserTaken');                                                      // an existing driver: their name and username are not available
  const newbie = await signup('BrowserNew', false);                                                // an account that has never been through first-run
  check(db(`select appear_nearby::text || '|' || coalesce(country_code, 'none') from profiles where id = '${newbie.id}'`) === 'false|none', 'a brand-new account is hidden from Players Nearby by default and has no country until it chooses one (nothing is guessed)');
  const ctry = db(`select code || '|' || name from countries where code <> 'NG' and name ~ '^[A-Za-z]+$' order by name limit 1`).split('|'); const [otherCode, otherName] = ctry;
  const nCountries = +db(`select count(*) from countries`), nLanguages = +db(`select count(*) from languages`);
  await open(newbie, '', async c => {
    check(!!(await c.waitLog(/\[Dashboard\] built for BrowserNew/, 0, 150)), 'Onboarding: the dashboard is built for the brand-new player');
    check(!!(await c.waitLog(/\[Onboarding\] opened/, 0, 60)), 'a player the server says is not onboarded lands on the onboarding page, not on Home');
    const ls = await c.waitLog(/\[Onboarding\] lists countries=(\d+) languages=(\d+)/, 0, 90);
    check(!!ls && ls.includes(`countries=${nCountries} languages=${nLanguages}`), `the country and language lists are the server's (${nCountries} countries, ${nLanguages} languages)`, ls);
    const sg = await c.waitLog(/\[Onboarding\] suggested locale=/, 0, 60);
    check(!!sg && /locale="en-NG" country=NG language=en/.test(sg), 'the browser locale en-NG suggests Nigeria and English (and nothing the server does not offer)', sg);
    await c.settle(); await c.page.screenshot({ path: `${prefix}-92-onboarding-name.png` });
    let tg = c.targets(); for (const k of ['Ob_Name_Input', 'Btn_ObContinue', 'Btn_ObSignOut', 'Btn_ObBlocker']) check(!!tg[k], `${k} is a clickable target on the onboarding page`);
    let m = c.logs.length; await c.click('Nav_Jobs'); await sleep(3000);
    check(!c.logs.slice(m).some(l => /\[Dashboard\] page=jobs/.test(l)), 'nothing under the onboarding page can be opened before it is finished (the sidebar is covered)');
    // step 1: a name that is too short cannot continue; the page says why
    await c.click('Ob_Name_Input'); await sleep(400); await wipe(c); await c.page.keyboard.type('ab', { delay: 80 }); await c.settle(); await c.page.screenshot({ path: `${prefix}-93-onboarding-badname.png` });
    m = c.logs.length; await c.click('Btn_ObContinue'); await sleep(2500); check(!c.logs.slice(m).some(l => /\[Onboarding\] step=where/.test(l)), 'a 2-character driver name cannot continue');
    await c.click('Ob_Name_Input'); await sleep(400); await wipe(c); await c.page.keyboard.type('BrowserTaken', { delay: 60 }); await c.settle();
    m = c.logs.length; await c.click('Btn_ObContinue'); check(!!(await c.waitLog(/\[Onboarding\] step=where/, m, 40)), 'a valid name continues to the next step (availability is the server\'s call, made at the end)');
    // step 2: country and language
    await c.settle(); await c.page.screenshot({ path: `${prefix}-94-onboarding-where.png` }); tg = c.targets();
    for (const k of ['Ob_Country_Search', 'Btn_ObCountry_NG', 'Btn_ObLang_en', 'Btn_ObLang_fr', 'Btn_ObBack', 'Btn_ObContinue']) check(!!tg[k], `${k} is a clickable target on the country step`);
    m = c.logs.length; await c.click('Ob_Country_Search'); await sleep(500); const q = otherName.slice(0, 4).toLowerCase(); await c.page.keyboard.type(q, { delay: 90 });
    const exp = +db(`select count(*) from countries where translate(lower(name), 'âäàáãåçéèêëíìîïñóòôöõúùûüýÿ', 'aaaaaaceeeeiiiinooooouuuuyy') like '%${q}%'`);
    const cs = await c.waitLog(new RegExp(`\\[Onboarding\\] country search="${q}" shown=(\\d+)`), m, 40); check(!!cs && +(/shown=(\d+)/.exec(cs) || [])[1] === exp, `searching "${q}" narrows the real list to the ${exp} matching countries`, cs);
    await c.settle(); await c.page.screenshot({ path: `${prefix}-95-onboarding-search.png` });
    m = c.logs.length; await c.click(`Btn_ObCountry_${otherCode}`); check(!!(await c.waitLog(new RegExp(`\\[Onboarding\\] country=${otherCode}`), m, 40)), `${otherName} can be chosen (replacing the suggestion)`);
    m = c.logs.length; await c.click('Btn_ObLang_fr'); check(!!(await c.waitLog(/\[Onboarding\] language=fr/, m, 40)), 'French can be chosen; the page says the game is still English'); await c.settle(); await c.page.screenshot({ path: `${prefix}-96-onboarding-language.png` });
    m = c.logs.length; await c.click('Btn_ObContinue'); check(!!(await c.waitLog(/\[Onboarding\] step=style/, m, 40)), 'country and language continue to the last step');
    // step 3: play style, speed unit, nearby
    await c.settle(); await c.page.screenshot({ path: `${prefix}-97-onboarding-style.png`}); tg = c.targets();
    for (const k of ['Btn_ObStyle_jobs', 'Btn_ObStyle_bus', 'Btn_ObStyle_convoy', 'Btn_ObStyle_explore', 'Btn_ObSpeed_kmh', 'Btn_ObSpeed_mph', 'Btn_ObNearby', 'Btn_ObFinish']) check(!!tg[k], `${k} is a clickable target on the last step`);
    m = c.logs.length; await c.click('Btn_ObFinish'); await sleep(2000); check(!c.logs.slice(m).some(l => /\[Onboarding\] complete/.test(l)), 'Start driving does nothing until a play style is chosen');
    m = c.logs.length; await c.click('Btn_ObStyle_convoy'); check(!!(await c.waitLog(/\[Onboarding\] style=convoy/, m, 40)), 'a play style can be chosen');
    m = c.logs.length; await c.click('Btn_ObSpeed_mph'); check(!!(await c.waitLog(/\[Onboarding\] speed=mph/, m, 40)), 'the speed unit can be chosen');
    await c.settle(); await c.page.screenshot({ path: `${prefix}-98-onboarding-style-chosen.png` });
    // the server refuses a name that is taken: the player is sent back to the name with the reason, and nothing is stored
    m = c.logs.length; await c.click('Btn_ObFinish'); const bad = await c.waitLog(/\[Onboarding\] complete ok=False/, m, 60);
    check(!!bad && /error=display_name_taken/.test(bad), 'the server refuses a driver name that is already taken', bad);
    check(!!bad && /nearby=false/.test(bad), 'Players Nearby is offered switched OFF: the player has not chosen it', bad);
    check(db(`select coalesce(onboarded_at::text, 'no') from profiles where id = '${newbie.id}'`) === 'no', 'a refused onboarding stores nothing (still not onboarded in the database)');
    await c.settle(); await c.page.screenshot({ path: `${prefix}-99-onboarding-taken.png` }); check(!!c.targets()['Ob_Name_Input'], 'the player is back on the name step');
    await c.click('Ob_Name_Input'); await sleep(400); await wipe(c); await c.page.keyboard.type('Chidi Eze', { delay: 70 }); await c.settle();
    m = c.logs.length; await c.click('Btn_ObContinue'); await c.waitLog(/\[Onboarding\] step=where/, m, 40); await c.settle();
    check(!!c.targets()['Btn_ObLang_fr'], 'the earlier answers were kept (country and language are still chosen)'); m = c.logs.length; await c.click('Btn_ObContinue'); await c.waitLog(/\[Onboarding\] step=style/, m, 40); await c.settle();
    m = c.logs.length; await c.click('Btn_ObNearby'); check(!!(await c.waitLog(/\[Onboarding\] nearby=true/, m, 40)), 'the player explicitly chooses to appear in Players Nearby');
    m = c.logs.length; await c.click('Btn_ObFinish'); const ok = await c.waitLog(/\[Onboarding\] complete ok=True/, m, 60);
    check(!!ok && ok.includes(`country=${otherCode} language=fr style=convoy renamed=True`), 'with a free name the server accepts the onboarding', ok);
    const row = db(`select country_code || '|' || language_code || '|' || play_style || '|' || display_name || '|' || (onboarded_at is not null) || '|' || appear_nearby from profiles where id = '${newbie.id}'`);
    check(row === `${otherCode}|fr|convoy|Chidi Eze|true|true`, 'the database holds exactly what was chosen (country, language, style, name, onboarded, Players Nearby on because the player chose it)', row);
    check(!!(await c.waitLog(/\[Onboarding\] nearby visibility on ok=True/, m, 60)), 'the explicit Players Nearby choice was sent to the server');
    check(!!(await c.waitLog(/\[Onboarding\] finished name=Chidi Eze onboarded=true landing=multiplayer/, m, 60)) && !!(await c.waitLog(/\[Dashboard\] page=multiplayer/, m, 60)), 'a player who wants to drive with friends lands on Multiplayer');
    check(!!(await c.waitLog(/\[Settings\] applied .*speed=mph/, m, 30)), 'the speed unit chosen in onboarding is applied (mph)');
    await c.settle(); await c.page.screenshot({ path: `${prefix}-100-onboarding-landed.png` });

    // ---- the profile page (same player: level, XP and the other numbers are set the way the server would)
    db(`update profiles set experience = 250, level = 2, jobs_completed = 3, distance_km = 42.5 where id = '${newbie.id}'`);
    m = c.logs.length; await c.click('Btn_Profile'); check(!!(await c.waitLog(/\[Profile\] opened/, m, 60)), 'the avatar in the header opens the profile');
    const ld = await c.waitLog(/\[Profile\] loaded profile=ok ranks=ready xp_rank=(\d+) achievements=ready unlocked=(\d+)\/(\d+)/, m, 90); const myRank = +db(`select count(*) + 1 from profiles where experience > 250`);
    check(!!ld && ld.includes(`xp_rank=${myRank}`), `the profile loads the real ranking (XP rank ${myRank}), achievements and numbers from the server`, ld);
    const nAch = +db(`select count(*) from achievement_definitions`); check(!!ld && new RegExp(`unlocked=\\d+/${nAch}\\b`).test(ld), `all ${nAch} achievements are listed`, ld);
    const wantFlag = shipped.has(otherCode.toLowerCase()) ? otherCode.toLowerCase() + ' state=known' : 'none state=noartwork';
    check(!!(await c.waitLog(new RegExp('\\[Profile\\] country flag=' + wantFlag), m, 30)), `the profile draws the flag of the country chosen in onboarding (${otherCode})`);
    await c.settle(); await c.page.screenshot({ path: `${prefix}-101-profile.png` }); tg = c.targets();
    for (const k of ['Btn_PrEdit', 'Btn_PrPublic', 'Btn_PrCountry', 'Btn_PrLanguage', 'Btn_PrStyle_jobs', 'Btn_PrStyle_bus', 'Btn_PrStyle_convoy', 'Btn_PrStyle_explore', 'Btn_PrRank_xp', 'Btn_PrRank_earnings', 'Btn_PrOpenBoards']) check(!!tg[k], `${k} is a clickable target on the profile page`);
    // edit: the server refuses a taken username, accepts a free one
    await c.click('Btn_PrEdit'); await c.settle(); check(!!c.targets()['Pr_Username_Input'], 'Edit profile opens a dialog with the name and username');
    await c.click('Pr_Username_Input'); await sleep(400); await wipe(c); await c.page.keyboard.type('browsertaken', { delay: 60 }); await sleep(500);
    m = c.logs.length; await c.click('Btn_PrEditSave'); const ue = await c.waitLog(/\[Profile\] edit name=False username=True ok=False/, m, 60); check(!!ue && /error=username_taken/.test(ue), 'the server refuses a username that is taken', ue);
    await c.settle(); await c.page.screenshot({ path: `${prefix}-102-profile-edit-taken.png` });
    await c.click('Pr_Username_Input'); await sleep(400); await wipe(c); await c.page.keyboard.type('chidi_eze', { delay: 60 }); await sleep(500);
    m = c.logs.length; await c.click('Btn_PrEditSave'); check(!!(await c.waitLog(/\[Profile\] edit name=False username=True ok=True/, m, 60)), 'a free username is saved');
    check(db(`select username from profiles where id = '${newbie.id}'`) === 'chidi_eze', 'the new username is in the database'); await c.settle();
    // country: search the real list, pick one
    m = c.logs.length; await c.clickUntil('Btn_PrCountry', /\[Profile\] country picker opened/, 30, 3); await c.settle(); check(!!c.targets()['Pr_Country_Search'], 'Change country opens a searchable list');
    await c.click('Pr_Country_Search'); await sleep(500); await c.page.keyboard.type('nigeria', { delay: 80 }); await c.waitLog(/\[Profile\] country search="nigeria"/, m, 40); await c.settle(); await c.page.screenshot({ path: `${prefix}-103-profile-country.png` });
    m = c.logs.length; await c.click('Btn_PrCountry_NG'); check(!!(await c.waitLog(/\[Profile\] country=NG ok=True/, m, 60)), 'a new country is saved');
    check(db(`select country_code from profiles where id = '${newbie.id}'`) === 'NG', 'the new country is in the database'); await c.settle();
    // language and play style
    await c.click('Btn_PrLanguage'); await c.settle(); await c.page.screenshot({ path: `${prefix}-104-profile-language.png` }); m = c.logs.length; await c.click('Btn_PrLang_en'); check(!!(await c.waitLog(/\[Profile\] language=en ok=True/, m, 60)), 'a new language is saved');
    check(db(`select language_code from profiles where id = '${newbie.id}'`) === 'en', 'the new language is in the database'); await c.settle();
    m = c.logs.length; await c.click('Btn_PrStyle_jobs'); check(!!(await c.waitLog(/\[Profile\] play style=jobs ok=True/, m, 60)), 'a new play style is saved'); check(db(`select play_style from profiles where id = '${newbie.id}'`) === 'jobs', 'the new play style is in the database');
    await c.settle(); await c.page.screenshot({ path: `${prefix}-105-profile-after.png` });
    m = c.logs.length; await c.click('Btn_PrPublic'); check(!!(await c.waitLog(/\[Friends\] profile id=chidi_eze ok=True .*status=self/, m, 60)), 'How others see me opens the public card from the server'); await c.click('Btn_CloseDialog'); await c.settle();
    m = c.logs.length; await c.click('Btn_PrRank_earnings'); check(!!(await c.waitLog(/\[Dashboard\] page=leaderboards/, m, 40)), 'a ranking opens the Leaderboards');
    // the same page at other window sizes
    await c.click('Btn_Profile'); await c.waitLog(/\[Profile\] opened/, m, 40); await c.settle();
    for (const [w, h] of [[1920, 1080], [1440, 900], [1280, 720], [1024, 768], [768, 1024], [390, 844]]) { await c.page.setViewportSize({ width: w, height: h }); await sleep(2500); await c.page.screenshot({ path: `${prefix}-8-profile-${w}x${h}.png` }); }
    await c.page.setViewportSize({ width: 1024, height: 576 }); await sleep(1500);
    check(c.errors.length === 0, 'onboarding and the profile at six window sizes cause no page errors', c.errors[0]);
  }, { locale: 'en-NG' });
  // after onboarding the player is not asked again, and a failing service shows an error with a working retry
  await open(newbie, '&fault=list_my_achievements:error:3', async c => {
    check(!!(await c.waitLog(/\[Dashboard\] built for Chidi Eze/, 0, 150)), 'signing in again shows the new driver name'); await sleep(6000);
    check(!c.logs.some(l => /\[Onboarding\] opened/.test(l)), 'a player who has been onboarded is never asked again (the server remembers)');
    await c.settle(); let m = c.logs.length; await c.click('Btn_Profile');
    check(!!(await c.waitLog(/\[Profile\] loaded profile=ok ranks=ready .* achievements=unavailable/, m, 90)), 'achievements that cannot load say "unavailable" instead of showing an empty list');
    await c.settle(); await c.page.screenshot({ path: `${prefix}-106-profile-error.png` }); check(!!c.targets()['Btn_PrRetryAch'], 'a Try again button is offered');
    m = c.logs.length; await c.click('Btn_PrRetryAch'); check(!!(await c.waitLog(/\[Profile\] loaded profile=ok ranks=ready .* achievements=ready/, m, 90)), 'Try again reaches the server');
  });
  // lists that cannot load stop the country step with a working retry
  const nl = await signup('BrowserNl', false);
  await open(nl, '&fault=countries:error:3', async c => {
    await c.waitLog(/\[Onboarding\] opened/, 0, 150); const lf = await c.waitLog(/\[Onboarding\] lists countries=unavailable/, 0, 90); check(!!lf, 'countries that cannot load are reported as unavailable');
    await c.settle(); await c.click('Ob_Name_Input'); await sleep(300); await wipe(c); await c.page.keyboard.type('Nina Okoro', { delay: 60 }); await c.settle();
    let m = c.logs.length; await c.click('Btn_ObContinue'); await c.waitLog(/\[Onboarding\] step=where/, m, 40); await c.settle(); await c.page.screenshot({ path: `${prefix}-107-onboarding-lists-error.png` });
    check(!!c.targets()['Btn_ObRetryLists'], 'the country step offers Try again instead of an empty list');
    m = c.logs.length; await c.click('Btn_ObRetryLists'); check(!!(await c.waitLog(new RegExp(`\\[Onboarding\\] lists countries=${nCountries} languages=${nLanguages}`), m, 90)), 'Try again reaches the server and the lists load'); await c.settle();
    check(!!c.targets()['Btn_ObCountry_NG'], 'the real countries are listed after the retry');
  });

  // =========================================================================================== 2i. Bus Routes: no bus, error + retry, the list and detail, a wrecked bus, a real start, a run in progress, abandon
  const nRoutes = +db(`select count(*) from bus_routes`), routeCode = db(`select code from bus_routes order by code limit 1`);
  const nobus = await signup('BrowserNoBus');
  await open(nobus, '&fault=bus_routes:error:3', async c => {
    await c.waitLog(/\[Dashboard\] built for BrowserNoBus/, 0, 150); await c.settle(); let m = c.logs.length; await c.click('Nav_TrucksAndBuses'); await c.waitLog(/\[Dashboard\] page=vehicles/, m, 60); await c.settle();
    check(!!c.targets()['Btn_VehBusRoutes'], 'Trucks & Buses has a Bus Routes button'); m = c.logs.length; await c.click('Btn_VehBusRoutes');
    check(!!(await c.waitLog(/\[BusRoutes\] opened/, m, 60)), 'Bus Routes opens from Trucks & Buses');
    check(!!(await c.waitLog(/\[BusRoutes\] routes state=unavailable/, m, 90)), 'routes that cannot load say "unavailable" instead of showing an empty list');
    await c.settle(); await c.page.screenshot({ path: `${prefix}-110-busroutes-error.png` }); check(!!c.targets()['Btn_BrRetry'], 'a Try again button is offered');
    m = c.logs.length; await c.click('Btn_BrRetry'); const ok = await c.waitLog(/\[BusRoutes\] routes state=ready/, m, 90); check(!!ok && ok.includes(`count=${nRoutes} buses=0 bus=none`), `Try again reaches the server: ${nRoutes} route(s) from the database and no bus owned`, ok);
    await c.settle(); await c.page.screenshot({ path: `${prefix}-111-busroutes-nobus.png` }); check(!!c.targets()['Btn_BrBrowse'] && !c.targets()['Btn_BrStart'], 'without a bus the page says so and leads to the shop (no Start button)');
    m = c.logs.length; await c.click('Btn_BrBrowse'); check(!!(await c.waitLog(/\[Dashboard\] page=vehicles/, m, 40)), 'Browse buses opens Trucks & Buses');
  });
  const busman = await signup('BrowserBusMan');
  db(`update player_wallets set balance = 300000 where player_id = '${busman.id}'`); asUser(busman, `select buy_vehicle('bus_city_01')::text`);
  const busId = db(`select id from vehicle_ownership where player_id = '${busman.id}' and definition_id = 'bus_city_01'`);
  db(`update vehicle_ownership set damage_pct = 100 where id = '${busId}'`);                          // a wrecked bus first: the server would refuse, the page says why and does not even ask
  await open(busman, '', async c => {
    await c.waitLog(/\[Dashboard\] built for BrowserBusMan/, 0, 150); await c.settle(); let m = c.logs.length; await c.click('Nav_TrucksAndBuses'); await c.waitLog(/\[Dashboard\] page=vehicles/, m, 60); await c.settle();
    m = c.logs.length; await c.click('Btn_VehBusRoutes'); const rl = await c.waitLog(/\[BusRoutes\] routes state=ready/, m, 90); check(!!rl && rl.includes(`count=${nRoutes} buses=1 bus=bus_city_01`), 'the list is the database list and the bus the player owns is found', rl);
    await c.settle(); await c.page.screenshot({ path: `${prefix}-112-busroutes-list.png` }); let tg = c.targets();
    for (const k of ['Btn_BrBack', `Btn_BrView_${routeCode}`, 'Btn_BrStart']) check(!!tg[k], `${k} is a clickable target on the Bus Routes page`);
    m = c.logs.length; await c.click('Btn_BrStart'); check(!!(await c.waitLog(/\[BusRoutes\] start blocked: Your bus is too damaged/, m, 30)), 'a wrecked bus cannot start a route: the page says why and does not ask the server');
    check(db(`select count(*) from bus_runs where player_id = '${busman.id}'`) === '0', 'no run was created');
    m = c.logs.length; await c.click(`Btn_BrView_${routeCode}`); check(!!(await c.waitLog(new RegExp(`\\[BusRoutes\\] selected ${routeCode}`), m, 30)), 'a route can be selected');
    await c.click('Btn_BrBack'); check(!!(await c.waitLog(/\[Dashboard\] page=vehicles/, m, 40)), 'the back link returns to Trucks & Buses');
  });
  db(`update vehicle_ownership set damage_pct = 5 where id = '${busId}'`);
  await open(busman, '', async c => {
    await c.waitLog(/\[Dashboard\] built for BrowserBusMan/, 0, 150); await c.settle(); let m = c.logs.length; await c.click('Nav_TrucksAndBuses'); await c.waitLog(/\[Dashboard\] page=vehicles/, m, 60); await c.settle();
    m = c.logs.length; await c.click('Btn_VehBusRoutes'); await c.waitLog(/\[BusRoutes\] routes state=ready/, m, 90); await c.settle(); await c.page.screenshot({ path: `${prefix}-113-busroutes-ready.png` });
    const bal0 = db(`select balance from player_wallets where player_id = '${busman.id}'`);
    m = c.logs.length; await c.click('Btn_BrStart'); const st = await c.waitLog(new RegExp(`\\[BusRoutes\\] start route=${routeCode} ok=`), m, 120);
    check(!!st && /ok=True/.test(st), 'Start route: the server accepted and the player is in the bus', st);
    check(db(`select count(*) from bus_runs where player_id = '${busman.id}' and status = 'active'`) === '1', 'the run exists in the database');
    await sleep(8000); await c.page.screenshot({ path: `${prefix}-114-busroutes-driving.png` });
    // leave the driving view: the menu shows the run in progress
    m = c.logs.length; await c.page.keyboard.press('Escape'); await c.waitLog(/\[Dashboard\] built for BrowserBusMan/, m, 60); await c.settle();
    m = c.logs.length; await c.click('Nav_TrucksAndBuses'); await c.waitLog(/\[Dashboard\] page=vehicles/, m, 60); await c.settle(); m = c.logs.length; await c.click('Btn_VehBusRoutes');
    const ra = await c.waitLog(new RegExp(`\\[BusRoutes\\] run active route=${routeCode} next=(\\d+) aboard=(\\d+) fares=(\\d+)`), m, 60);
    const dbRun = db(`select (next_seq - 1) || '/' || aboard || '/' || revenue from bus_runs where player_id = '${busman.id}' and status = 'active'`); const gm = /next=(\d+) aboard=(\d+) fares=(\d+)/.exec(ra || '');
    check(!!gm && `${gm[1]}/${gm[2]}/${gm[3]}` === dbRun, 'the page shows the run in progress with the same stop, passengers and fares as the database', `${ra} vs ${dbRun}`);
    await c.settle(); await c.page.screenshot({ path: `${prefix}-115-busroutes-run.png` }); tg = c.targets(); check(!!tg['Btn_BrResume'] && !!tg['Btn_BrAbandon'] && !tg['Btn_BrStart'], 'a run in progress offers Resume and Abandon, not Start');
    m = c.logs.length; await c.click('Btn_BrAbandon'); await c.settle(); await c.page.screenshot({ path: `${prefix}-116-busroutes-abandon-confirm.png` });
    check(db(`select count(*) from bus_runs where player_id = '${busman.id}' and status = 'active'`) === '1', 'nothing is abandoned before the player confirms');
    m = c.logs.length; await c.click('Btn_BrAbandonYes'); check(!!(await c.waitLog(/\[BusRoutes\] abandoned active=false/, m, 60)), 'Abandon: the run is closed');
    check(db(`select count(*) from bus_runs where player_id = '${busman.id}' and status = 'abandoned'`) === '1', 'the run is abandoned in the database (and nothing was paid)');
    check(db(`select balance from player_wallets where player_id = '${busman.id}'`) === bal0, 'abandoning paid nothing (the balance is unchanged)');
    check(!!(await c.waitLog(/\[BusRoutes\] routes state=ready/, m, 60)), 'the list is shown again after abandoning');
    await c.settle(); check(!!c.targets()['Btn_BrStart'], 'Start route is available again');
    for (const [w, h] of [[1920, 1080], [1440, 900], [1280, 720], [1024, 768], [768, 1024], [390, 844]]) { await c.page.setViewportSize({ width: w, height: h }); await sleep(2500); await c.page.screenshot({ path: `${prefix}-8-busroutes-${w}x${h}.png` }); }
    await c.page.setViewportSize({ width: 1024, height: 576 }); await sleep(1500);
    check(c.errors.length === 0, 'Bus Routes at six window sizes cause no page errors', c.errors[0]);
  });

  // a failing catalogue shows Unavailable and a retry that works (injected failures only)
  const uma = await signup('BrowserUma');
  await open(uma, '&fault=list_vehicle_catalog:error:3', async c => {
    await c.waitLog(/\[Dashboard\] built for BrowserUma/, 0, 150); await c.settle(); let m = c.logs.length; await c.click('Nav_TrucksAndBuses');
    check(!!(await c.waitLog(/\[Vehicles\] catalog state=unavailable/, m, 120)), 'vehicles that cannot load say "unavailable" instead of inventing a catalogue');
    await c.settle(); await c.page.screenshot({ path: `${prefix}-28-vehicles-unavailable.png` });
    check(!!c.targets()['Btn_RetryVehicles'], 'a Retry button is offered');
    m = c.logs.length; await c.click('Btn_RetryVehicles'); check(!!(await c.waitLog(/\[Vehicles\] catalog state=ready/, m, 90)), 'Retry reaches the server');
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
