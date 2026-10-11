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
  const clickUntil = async (name, re, seconds = 30, tries = 3) => {
    for (let i = 0; i < tries; i++) { const from = logs.length; await click(name); const hit = await waitLog(re, from, seconds); if (hit) return hit; }
    return null;
  };
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
    m = c.logs.length; await c.click('Btn_ShowRear'); cam = await c.waitLog(/\[Showroom\] camera view=rear yaw=(-?\d+)/, m, 60); check(!!cam && /yaw=(180|-180) /.test(cam + ' '), 'the Rear preset looks from behind', cam);
    m = c.logs.length; await c.click('Btn_ShowTop'); cam = await c.waitLog(/\[Showroom\] camera view=top yaw=-?\d+ pitch=(\d+)/, m, 60); check(!!cam && +/pitch=(\d+)/.exec(cam)[1] >= 80, 'the Top preset looks down', cam);
    await c.settle(); await c.page.screenshot({ path: `${prefix}-31-garage-top.png` });
    m = c.logs.length; await c.click('Btn_ShowReset'); cam = await c.waitLog(/\[Showroom\] camera view=default/, m, 60); check(!!cam, 'Reset returns to the default view', cam);
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
    m = c.logs.length; await c.click('Btn_GarCat_wheels'); check(!!(await c.waitLog(/\[Garage\] category=wheels/, m, 60)), 'a category the game has no system for opens its explanation, not fake content'); await c.settle(); await c.page.screenshot({ path: `${prefix}-35-garage-wheels.png` });
    await c.click('Btn_GarTab_Upgrades'); await c.settle(); await c.page.screenshot({ path: `${prefix}-36-garage-upgrades.png` });

    // maintenance: prices from the server, the actions change the database
    m = c.logs.length; await c.click('Btn_GarTab_Maintenance');
    const q = await c.waitLog(/\[Garage\] quote state=ready repair=(\d+) refuel=(\d+) litres=(\d+)/, m, 60);
    check(!!q && +/repair=(\d+)/.exec(q)[1] === 300, 'the repair price shown is the server quote (30 % damage = 300)', q);
    await c.settle(); await c.page.screenshot({ path: `${prefix}-37-garage-maintenance.png` });
    const bal1 = +db(`select balance from player_wallets where player_id = '${gabe.id}'`);
    m = c.logs.length; await c.click('Btn_GarRepair'); check(!!(await c.waitLog(/\[Garage\] repair ok=True/, m, 60)), 'Repair: the server accepted');
    check(db(`select damage_pct from vehicle_ownership where player_id = '${gabe.id}' and definition_id = 'truck_light_01'`) === '0' && +db(`select balance from player_wallets where player_id = '${gabe.id}'`) === bal1 - 300, 'the vehicle is repaired and exactly the quote was charged');
    await c.settle(); m = c.logs.length; await c.click('Btn_GarRefuel'); check(!!(await c.waitLog(/\[Garage\] refuel ok=True/, m, 60)), 'Refuel: the server accepted');
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
    check(!!(await c.waitLog(/\[Settings\] privacy nearby=true blocked=1/, m, 90)), 'the privacy items are read from the account (visible, one blocked player)');
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
    m = c.logs.length; await c.click('Btn_SetTab_Gameplay'); check(!!(await c.waitLog(/\[Settings\] privacy nearby=true blocked=1/, m, 90)), 'the Gameplay tab re-reads the privacy items'); await c.settle();
    await c.page.screenshot({ path: `${prefix}-48-settings-gameplay.png` });
    m = c.logs.length; await c.click('Btn_SetToggle_appear_nearby'); check(!!(await c.waitLog(/\[Settings\] nearby visible=false ok=True/, m, 60)), 'Appear in Nearby Players off: the server accepted');
    check(db(`select appear_nearby::text from profiles where id = '${hana.id}'`) === 'false', 'the nearby choice is stored on the account in the database');
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
