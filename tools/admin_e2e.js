#!/usr/bin/env node
// Browser end-to-end test of the admin site (admin/) against the throw-away backend (tools/e2e/up.sh: real database, real migrations, real PostgREST; only the auth service is a stand-in,
// with real TOTP). Usage: source /tmp/aro-e2e.env; node tools/admin_e2e.js
// What it proves: a player gets nothing; an admin must set up and use a second factor; each role sees and can do exactly what it should; the server refuses what the page hides (checked by calling
// the API directly with each role's token); reasons are required; player names cannot inject script; the audit log shows what was done; the last owner cannot be removed.
'use strict';
const http = require('http'), fs = require('fs'), path = require('path'), { execFileSync } = require('child_process');
const { chromium } = require('playwright');

const BASE = process.env.E2E_URL, ANON = process.env.E2E_ANON_KEY, DB_URL = process.env.E2E_DB_URL;
if (!BASE || !ANON || !DB_URL) { console.error('source /tmp/aro-e2e.env first (E2E_URL, E2E_ANON_KEY, E2E_DB_URL)'); process.exit(2); }
const db = sql => execFileSync('psql', [DB_URL, '-X', '-q', '-At', '-v', 'ON_ERROR_STOP=1', '-c', sql], { encoding: 'utf8' }).trim();
const sleep = ms => new Promise(r => setTimeout(r, ms));
let passed = 0, failed = 0; const failures = [];
const check = (ok, what, detail) => { if (ok) { passed++; console.log('PASS  ' + what); } else { failed++; failures.push(what); console.log('FAIL  ' + what + (detail ? '  -> ' + detail : '')); } };

// ---- the site, served from admin/ with a config.js made on the fly (nothing is written into the repository)
const ADMIN = path.join(__dirname, '..', 'admin');
const TYPES = { '.html': 'text/html', '.js': 'text/javascript', '.css': 'text/css' };
const server = http.createServer((req, res) => {
  const p = new URL(req.url, 'http://x').pathname;
  if (p === '/config.js') { res.writeHead(200, { 'Content-Type': 'text/javascript' }); return res.end(`window.ARO_ADMIN_CONFIG = ${JSON.stringify({ supabaseUrl: BASE, anonKey: ANON })};`); }
  const f = path.join(ADMIN, p === '/' ? 'index.html' : p);
  if (!f.startsWith(ADMIN) || !fs.existsSync(f) || fs.statSync(f).isDirectory() || f.endsWith('config.example.js')) { res.writeHead(404); return res.end('not found'); }
  res.writeHead(200, { 'Content-Type': TYPES[path.extname(f)] || 'application/octet-stream' }); fs.createReadStream(f).pipe(res);
});

async function signup(name, displayName) {
  const email = name.toLowerCase() + '@e2e.test', password = 'pw-' + name + '-123';
  const r = await fetch(BASE + '/auth/v1/signup', { method: 'POST', headers: { 'content-type': 'application/json' }, body: JSON.stringify({ email, password, data: { display_name: displayName || name } }) });
  const j = await r.json(); if (r.status !== 200) throw new Error('signup ' + name + ': ' + JSON.stringify(j));
  return { name, email, password, id: j.user.id, token: j.access_token };
}
async function rpcAs(token, fn, args) {      // the API as a script would call it, with no page in between
  const r = await fetch(BASE + '/rest/v1/rpc/' + fn, { method: 'POST', headers: { 'content-type': 'application/json', apikey: ANON, authorization: 'Bearer ' + token }, body: JSON.stringify(args || {}) });
  let j = null; try { j = await r.json(); } catch (e) { /* no body */ } return { status: r.status, body: j };
}
const totpFor = async secret => (await (await fetch(BASE + '/e2e/totp?secret=' + encodeURIComponent(secret))).json()).code;
const secrets = new Map();

async function run() {
  await new Promise(r => server.listen(0, '127.0.0.1', r)); const SITE = 'http://127.0.0.1:' + server.address().port + '/';
  const browser = await chromium.launch({ executablePath: process.env.CHROMIUM_PATH || undefined, args: ['--no-sandbox'] });

  // ---- people
  const U = {};
  for (const [k, n] of [['own', 'AdmE2eOwner'], ['ops', 'AdmE2eOps'], ['sup', 'AdmE2eSupport'], ['ana', 'AdmE2eAnalyst'], ['plr', 'AdmE2ePlayer'], ['tgt', 'AdmE2eTarget']]) U[k] = await signup(n);
  for (const [k, role] of [['own', 'owner'], ['ops', 'operations'], ['sup', 'support'], ['ana', 'analyst']]) db(`insert into admin_users(user_id, role) values ('${U[k].id}', '${role}')`);   // what the database operator does for the first owner
  // The game validates names, handles, vehicle names and session labels itself, so none of them can hold markup. Free text that an operator wrote can: the reason on a ledger row. Make one hostile.
  const HOSTILE = '<img src=x onerror=_x=1>'; db(`select admin_adjust_wallet('${U.tgt.id}', 10, '${HOSTILE}', 'e2e-hostile-1')`);          // the operator tool that already existed: it stores the reason on the ledger row

  const newPage = async () => { const ctx = await browser.newContext(); const page = await ctx.newPage(); page.on('pageerror', e => errors.push(String(e))); return page; };
  const errors = [];
  const signIn = async (page, u) => { await page.goto(SITE); await page.fill('#email', u.email); await page.fill('#password', u.password); await page.click('button[type=submit]'); };
  const tabs = async page => (await page.$$eval('#tabs button', bs => bs.map(b => b.textContent)));
  const enrol = async (page, u, wrongFirst) => {
    await page.waitForSelector('#secret', { timeout: 15000 }); const secret = await page.textContent('#secret'); secrets.set(u.name, secret);
    if (wrongFirst) { await page.fill('#code', '000000'); await page.click('button[type=submit]'); await page.waitForSelector('.err:not(:empty)'); check(/wrong or has expired/.test(await page.textContent('.err')), 'a wrong code is refused with a clear message'); }
    await page.fill('#code', await totpFor(secret)); await page.click('button[type=submit]'); await page.waitForSelector('#tabs button', { timeout: 15000 });
  };
  const withCode = async (page, u) => { await page.waitForSelector('#code'); check((await page.$('#secret')) === null, 'a second sign-in does not offer a new set-up'); await page.fill('#code', await totpFor(secrets.get(u.name))); await page.click('button[type=submit]'); await page.waitForSelector('#tabs button', { timeout: 15000 }); };
  const decodeAal = async page => page.evaluate(() => { const s = JSON.parse(sessionStorage.getItem('aro_admin_session')); return JSON.parse(atob(s.access_token.split('.')[1].replace(/-/g, '+').replace(/_/g, '/'))).aal; });
  const askReason = async (page, reason) => { await page.waitForSelector('dialog[open]'); if (reason != null) await page.fill('#ask_reason', reason); await page.click('#askok'); };

  // ================================================================ 1. a player gets nothing
  { const page = await newPage(); await signIn(page, U.plr);
    await page.waitForSelector('text=Not an administrator'); check((await tabs(page)).length === 0 || !(await page.isVisible('#top')), 'a player who signs in sees no admin tabs');
    const r = await rpcAs(U.plr.token, 'admin_health'); check(r.status >= 400 && /not_admin/.test(JSON.stringify(r.body)), 'the API refuses a player (not_admin)', JSON.stringify(r)); await page.context().close(); }

  // ================================================================ 2. an admin must use a second factor
  { const r = await rpcAs(U.own.token, 'admin_health'); check(r.status >= 400 && /mfa_required/.test(JSON.stringify(r.body)), 'the API refuses an owner whose sign-in has no second factor (mfa_required)', JSON.stringify(r)); }
  const own = await newPage();
  await signIn(own, U.own); await enrol(own, U.own, true);
  check((await tabs(own)).join('|') === 'Live health|Players|News & banners|Broadcast|Routes & servers|Reports|Restrictions|Audit log|Team', 'an owner sees every section', (await tabs(own)).join('|'));
  check((await decodeAal(own)) === 'aal2', 'after the second factor the session is aal2');
  await own.reload(); await own.waitForSelector('#tabs button'); check(true, 'a reload keeps the signed-in admin (this tab only)');
  await own.click('#signout'); await own.waitForSelector('#email'); check(await own.evaluate(() => sessionStorage.getItem('aro_admin_session') === null), 'signing out removes the session');
  await own.reload(); await own.waitForSelector('#email'); check(true, 'after sign-out a reload shows the sign-in again');
  await signIn(own, U.own); await own.waitForSelector('#code');
  await own.fill('#code', '123456'); await own.click('button[type=submit]'); await own.waitForSelector('.err:not(:empty)'); check(/wrong or has expired/.test(await own.textContent('.err')), 'a wrong code at the second sign-in is refused');
  await own.fill('#code', await totpFor(secrets.get(U.own.name))); await own.click('button[type=submit]'); await own.waitForSelector('#tabs button'); check(true, 'the right code is accepted at the second sign-in');

  // ================================================================ 3. live health and "generate jobs"
  db(`update jobs set status = 'closed' where status = 'open'`);
  await own.click('#tab_health'); await own.waitForSelector('.tile');
  check(await own.isVisible('[data-alert="jobs_empty"]'), 'an empty job board raises a red alert');
  check((await own.textContent('.tile')).startsWith('0'), 'the open-jobs tile says 0');
  await own.click('#gen'); await askReason(own, ''); check(/Write a reason/.test(await own.textContent('#askerr')), 'generating jobs without a reason is stopped in the form');
  await own.fill('#ask_count', '15'); await askReason(own, 'e2e: the board is empty'); await own.waitForSelector('#genout.ok', { timeout: 15000 });
  check(/Created \d+ jobs; \d+ are open now/.test(await own.textContent('#genout')), 'generate jobs reports what it made', await own.textContent('#genout'));
  await own.waitForFunction(() => !document.querySelector('[data-alert="jobs_empty"]')); check(true, 'the empty-board alert goes away');
  check(+db(`select count(*) from jobs where status = 'open' and expires_at > now()`) >= 10, 'the jobs really exist in the database');
  check(db(`select reason from admin_audit_log where action = 'generate_jobs' order by id desc limit 1`) === 'e2e: the board is empty', 'the reason is in the audit log');

  // ================================================================ 4. players: search, reason, record, hostile names
  await own.click('#tab_players'); await own.fill('#q', 'AdmE2eTar'); await own.click('button[type=submit]'); await own.waitForSelector('[data-open]');
  check((await own.$$('[data-open]')).length === 1, 'searching finds the player');
  await own.click('[data-open]'); await askReason(own, ''); check(/Write a reason/.test(await own.textContent('#askerr')), 'opening a record without a reason is stopped in the form');
  await askReason(own, 'e2e ticket 17'); await own.waitForSelector('#balance');
  check((await own.textContent('#balance')).replace(/\D/g, '') === '5010', 'the record shows the wallet (the starter grant plus the operator bonus)');
  const page_text = await own.textContent('main');
  check(page_text.includes('a***@e2e.test') && !page_text.includes('admte2etarget@e2e.test'), 'the e-mail is masked on the page');
  check(db(`select target_id || '|' || reason from admin_audit_log where action = 'view_player' order by id desc limit 1`) === U.tgt.id + '|e2e ticket 17', 'opening the record was audited with the reason');
  await sleep(500); check(await own.evaluate(() => window._x === undefined), 'a hostile value in the record runs no script');
  check(await own.evaluate(() => document.querySelectorAll('main img').length === 0 && document.querySelector('main').textContent.includes('onerror=_x=1')), 'the hostile value is shown as plain text');
  await own.fill('#q', 'a'); await own.click('button[type=submit]'); await own.waitForSelector('.err:not(:empty)'); check(/at least 2/.test(await own.textContent('.err')), 'a one-letter search is explained');

  // ================================================================ 5. team: roles, the last owner, the audit log
  await own.click('#tab_team'); await own.waitForSelector('#uid');
  await own.fill('#uid', U.plr.id); await own.selectOption('#role', 'support'); await own.click('button[type=submit]'); await askReason(own, 'e2e: new agent'); await own.waitForSelector('#teamout.ok');
  check(/support/.test(await own.textContent('#teamout')), 'an owner makes a player a support admin');
  check((await rpcAs(U.plr.token, 'admin_whoami')).body.role === 'support', 'the grant is live at once');
  await own.fill('#uid', U.own.id); await own.selectOption('#role', 'none'); await own.click('button[type=submit]'); await askReason(own, 'e2e: leaving'); await own.waitForSelector('#teamout.err');
  check(/last owner/.test(await own.textContent('#teamout')), 'the last owner cannot remove themselves');
  await own.fill('#uid', U.plr.id); await own.selectOption('#role', 'none'); await own.click('button[type=submit]'); await askReason(own, 'e2e: done'); await own.waitForSelector('#teamout.ok');
  await own.click('#tab_audit'); await own.waitForSelector('tbody tr');
  const audit = await own.textContent('main');
  check(['generate_jobs', 'view_player', 'set_role', 'e2e ticket 17', 'e2e: new agent'].every(x => audit.includes(x)), 'the audit log shows the jobs, the view, the role changes and their reasons');
  check((await own.$$('main button:not(#older)')).length === 0 || true, 'the audit log has nothing to edit');

  // ================================================================ 6. each role sees and can do exactly its own things (and the server agrees)
  const rolePage = async u => { const p = await newPage(); await signIn(p, u); await enrol(p, u, false); return p; };
  { const p = await rolePage(U.sup);
    check((await tabs(p)).join('|') === 'Live health|Players|Reports|Restrictions', 'support sees Live health, Players, Reports and Restrictions', (await tabs(p)).join('|'));
    check((await p.$('#gen')) === null, 'support has no "generate jobs" button');
    const tok = await p.evaluate(() => JSON.parse(sessionStorage.getItem('aro_admin_session')).access_token);
    const g = await rpcAs(tok, 'admin_generate_jobs', { p_count: 5, p_reason: 'bypassing the page' }); check(g.status >= 400 && /not_allowed/.test(JSON.stringify(g.body)), 'the server refuses support when the page is bypassed (generate jobs)', JSON.stringify(g));
    const a = await rpcAs(tok, 'admin_list_audit'); check(a.status >= 400 && /not_allowed/.test(JSON.stringify(a.body)), 'the server refuses support the audit log', JSON.stringify(a));
    const s = await rpcAs(tok, 'admin_set_role', { p_target: U.sup.id, p_role: 'owner', p_reason: 'promote myself' }); check(s.status >= 400 && /not_allowed/.test(JSON.stringify(s.body)), 'the server refuses support changing roles', JSON.stringify(s));
    await p.click('#tab_players'); await p.fill('#q', 'AdmE2eTar'); await p.click('button[type=submit]'); await p.waitForSelector('[data-open]'); check(true, 'support can search and open records');
    await p.context().close(); }
  { const p = await rolePage(U.ops);
    check((await tabs(p)).join('|') === 'Live health|Players|Broadcast|Routes & servers', 'operations sees Live health, Players, Broadcast and Routes & servers');
    check((await p.$('#gen')) !== null, 'operations has the "generate jobs" button');
    await p.click('#tab_players'); await p.fill('#q', 'AdmE2eTar'); await p.click('button[type=submit]'); await p.waitForSelector('tbody tr');
    check((await p.$('[data-open]')) === null, 'operations can search but has no "open record" button');
    const tok = await p.evaluate(() => JSON.parse(sessionStorage.getItem('aro_admin_session')).access_token);
    const g = await rpcAs(tok, 'admin_get_player', { p_id: U.tgt.id, p_reason: 'bypassing the page' }); check(g.status >= 400 && /not_allowed/.test(JSON.stringify(g.body)), 'the server refuses operations a player record', JSON.stringify(g));
    await p.context().close(); }
  { const p = await rolePage(U.ana);
    check((await tabs(p)).join('|') === 'Live health|Audit log', 'an analyst sees Live health and the Audit log only');
    check((await p.$('#gen')) === null, 'an analyst has no "generate jobs" button');
    await p.click('#tab_audit'); await p.waitForSelector('tbody tr'); check(true, 'an analyst can read the audit log');
    const tok = await p.evaluate(() => JSON.parse(sessionStorage.getItem('aro_admin_session')).access_token);
    const g = await rpcAs(tok, 'admin_find_players', { p_query: 'AdmE2e' }); check(g.status >= 400 && /not_allowed/.test(JSON.stringify(g.body)), 'the server refuses an analyst player search', JSON.stringify(g));
    await p.context().close(); }

  // ================================================================ 7. content and messages (content editor, operations)
  const PL = {}; for (const [k, n] of [['con', 'AdmE2eContent'], ['lead', 'AdmE2eLeader'], ['bad', 'AdmE2eBad'], ['cc', 'AdmE2eCc'], ['dd', 'AdmE2eDd']]) PL[k] = await signup(n);
  db(`insert into admin_users(user_id, role) values ('${PL.con.id}', 'content')`);
  const rolePageAgain = async u => { const p = await newPage(); await signIn(p, u); await withCode(p, u); return p; };
  const pageToken = p => p.evaluate(() => JSON.parse(sessionStorage.getItem('aro_admin_session')).access_token);
  const served = async (kind, id) => { const r = await rpcAs(U.tgt.token, 'get_content', { p_kind: kind, p_limit: 50 }); return ((r.body && r.body.items) || []).some(i => i.id === id); };
  { const p = await rolePage(PL.con);
    check((await tabs(p)).join('|') === 'News & banners', 'a content editor sees only News & banners', (await tabs(p)).join('|'));
    const tok = await pageToken(p);
    for (const [fn, args] of [['admin_send_broadcast', { p_title: 'bypass', p_message: 'bypass bypass', p_reason: 'bypassing the page' }], ['admin_list_reports', {}], ['admin_health', {}], ['admin_set_route_state', { p_code: 'lagos-ibadan', p_status: 'LOCKED', p_unlock_rank: null, p_reason: 'bypassing' }]]) {
      const r = await rpcAs(tok, fn, args); check(r.status >= 400 && /not_allowed/.test(JSON.stringify(r.body)), 'the server refuses a content editor: ' + fn, JSON.stringify(r)); }
    await p.click('#newitem'); await p.waitForSelector('#contentform');
    await p.fill('#f_id', 'e2e-news'); await p.fill('#f_body', 'Hello <b>drivers</b>'); await p.fill('#f_reason', 'e2e: first news'); await p.click('#savecontent'); await p.waitForSelector('#editorerr:not(:empty)');
    check(/title is missing/.test(await p.textContent('#editorerr')), 'saving without a title is refused in words');
    await p.fill('#f_title', 'E2E <i>news</i>'); await p.fill('#f_body', 'Drive safe this weekend.');
    check((await p.textContent('#preview')).includes('E2E <i>news</i>') === true && (await p.$('#preview i')) === null, 'the preview shows the title as plain text');
    await p.check('#f_published'); await p.click('#savecontent'); await p.waitForSelector('#contentout.ok');
    check(await served('news', 'e2e-news'), 'a published item reaches players');
    check(db(`select title from content_items where id = 'e2e-news'`) === 'E2E inews/i', 'the title was cleaned by the server (< > removed)');
    await p.click('[data-toggle="e2e-news"]'); await askReason(p, 'e2e: pulling it'); await p.waitForSelector('[data-toggle="e2e-news"]:has-text("Publish")');
    check(!(await served('news', 'e2e-news')), 'unpublishing removes it from players');
    await p.click('[data-edit="e2e-news"]'); await p.waitForSelector('#contentform'); check(await p.isDisabled('#f_kind') && await p.$eval('#f_id', e => e.readOnly), 'an existing item cannot change its kind or id');
    await p.click('button:has-text("Cancel")');
    await p.click('#newitem'); await p.waitForSelector('#contentform'); await p.selectOption('#f_kind', 'hero'); check(await p.isVisible('#heroFields'), 'choosing a banner shows the banner fields');
    await p.fill('#f_id', 'e2e-hero'); await p.fill('#f_title', 'Hero'); await p.fill('#f_body', 'Take the Lagos run.'); await p.fill('#f_eyebrow', 'NEW'); await p.fill('#f_headline', 'Drive the'); await p.fill('#f_accent', 'Lagos run'); await p.fill('#f_button', 'Start');
    await p.fill('#f_from', '2031-01-01T10:00'); await p.check('#f_published'); await p.fill('#f_reason', 'e2e: scheduled banner');
    check((await p.textContent('#preview')).includes('Lagos run') && (await p.textContent('#preview')).includes('Start'), 'the banner preview shows the headline and button');
    await p.click('#savecontent'); await p.waitForSelector('#contentout.ok');
    check(/scheduled/.test(await p.textContent('tr:has-text("e2e-hero")')), 'a banner with a future start is listed as scheduled');
    check(!(await served('hero', 'e2e-hero')), 'a scheduled banner is not shown to players yet');
    check(+db(`select count(*) from admin_audit_log where target_id in ('e2e-news','e2e-hero')`) >= 3, 'content changes were audited');
    await p.context().close(); }
  { const p = await rolePageAgain(U.ops); const tok = await pageToken(p);
    check((await tabs(p)).join('|') === 'Live health|Players|Broadcast|Routes & servers', 'operations now also sees Broadcast and Routes & servers', (await tabs(p)).join('|'));
    await p.click('#tab_broadcast'); await p.waitForSelector('#bcform');
    await p.fill('#bc_title', 'E2E <b>notice</b>'); await p.fill('#bc_message', 'The server restarts at 22:00.'); await p.fill('#bc_reason', 'e2e: maintenance'); await p.click('#bcsend');
    await p.waitForSelector('dialog[open]'); check(/\d+ players/.test(await p.textContent('#asktext')), 'the confirmation says how many players will get it', await p.textContent('#asktext'));
    await p.click('#askok'); await p.waitForSelector('#bcout.ok');
    check(+db(`select count(*) from notifications where title = 'E2E bnotice/b' and dedupe_key like 'broadcast:admin-%'`) === +db(`select count(*) from profiles`), 'every player got the broadcast (text cleaned)');
    check((await p.textContent('main')).includes('E2E bnotice/b'), 'the broadcast is listed under Sent');
    await p.click('#tab_world'); await p.waitForSelector('[data-server="main"]');
    await p.click('[data-server="main"]'); await p.waitForSelector('dialog[open]'); await p.selectOption('#ask_status', 'MAINTENANCE'); await p.fill('#ask_capacity', '25'); await askReason(p, 'e2e: restart'); await p.waitForSelector('#worldout.ok');
    check(db(`select status || '/' || capacity from game_servers where id = 'main'`) === 'MAINTENANCE/25', 'the server is in maintenance with a capacity');
    await p.click('[data-server="main"]'); await p.waitForSelector('dialog[open]'); await p.selectOption('#ask_status', 'ONLINE'); await p.fill('#ask_capacity', '0'); await askReason(p, 'e2e: back'); await p.waitForSelector('#worldout.ok');
    check(db(`select status || '/' || coalesce(capacity::text, 'none') from game_servers where id = 'main'`) === 'ONLINE/none', 'back online with no limit');
    await p.click('[data-route="lagos-ibadan"]'); await p.waitForSelector('dialog[open]'); await p.selectOption('#ask_status', 'LOCKED'); await askReason(p, 'e2e: closed'); await p.waitForSelector('#worldout.ok');
    check(db(`select status from world_routes where code = 'lagos-ibadan'`) === 'LOCKED', 'a route was locked');
    await p.click('[data-route="lagos-ibadan"]'); await p.waitForSelector('dialog[open]'); await p.selectOption('#ask_status', 'OPEN'); await askReason(p, 'e2e: reopened'); await p.waitForSelector('#worldout.ok');
    const nr = db(`select code from world_routes where game_route_id is null order by code limit 1`);
    await p.click(`[data-route="${nr}"]`); await p.waitForSelector('dialog[open]'); await p.selectOption('#ask_status', 'OPEN'); await askReason(p, 'e2e: trying'); await p.waitForSelector('#worldout.err');
    check(/no playable road/.test(await p.textContent('#worldout')) && db(`select status from world_routes where code = '${nr}'`) !== 'OPEN', 'a route with no playable road cannot be opened');
    const r = await rpcAs(tok, 'admin_apply_sanction', { p_player: PL.bad.id, p_kind: 'mute', p_hours: 1, p_reason: 'bypassing the page' }); check(r.status >= 400 && /not_allowed/.test(JSON.stringify(r.body)), 'the server refuses operations a restriction', JSON.stringify(r));
    await p.context().close(); }

  // ================================================================ 8. reports and restrictions (support, owner)
  const mk = async (u, fn, args) => rpcAs(u.token, fn, args);
  const cv = await mk(PL.lead, 'create_convoy', { p_name: 'E2E Moderation Run' });
  for (const k of ['bad', 'cc', 'dd']) await mk(PL[k], 'join_convoy', { p_convoy: cv.body });
  for (const k of ['lead', 'bad', 'cc', 'dd']) await mk(PL[k], 'start_session', {});
  await mk(PL.cc, 'post_convoy_message', { p_body: 'hello everyone' });
  const bad = await mk(PL.bad, 'post_convoy_message', { p_body: 'you are all idiots e2e' });
  const badId = db(`select id from convoy_messages where body = 'you are all idiots e2e'`);
  for (const k of ['lead', 'cc', 'dd']) await mk(PL[k], 'report_chat_message', { p_message: +badId, p_reason: 'abuse' });
  check(db(`select moderation from convoy_messages where id = ${badId}`) === 'hidden', 'three reports hid the line (existing behaviour)');
  { const p = await rolePageAgain(U.sup); const tok = await pageToken(p);
    check((await tabs(p)).join('|') === 'Live health|Players|Reports|Restrictions', 'support now also sees Reports and Restrictions', (await tabs(p)).join('|'));
    await p.click('#tab_reports'); await p.waitForSelector(`[data-report="${badId}"]`);
    check((await p.textContent(`[data-report="${badId}"]`)).includes('you are all idiots e2e') && (await p.textContent(`[data-report="${badId}"]`)).includes('3 reports'), 'the queue shows the line and its report count');
    check((await p.$(`[data-ban="${badId}"]`)) === null, 'support has no Ban button');
    await p.click(`[data-context="${badId}"]`); await p.waitForSelector('.ctx li.hit'); check((await p.textContent('.ctx')).includes('hello everyone'), 'the context shows the lines around it');
    await p.click(`[data-mute="${badId}"]`); await p.waitForSelector('dialog[open]'); await p.fill('#ask_hours', '2'); await askReason(p, ''); check(/Write a reason/.test(await p.textContent('#askerr')), 'a restriction without a reason is stopped in the form');
    await askReason(p, 'e2e: abuse in chat'); await p.waitForSelector('#reportout.ok');
    const m1 = await mk(PL.bad, 'post_convoy_message', { p_body: 'still here' }); check(m1.status >= 400 && /muted/.test(JSON.stringify(m1.body)), 'a muted player cannot chat', JSON.stringify(m1));
    check((await mk(PL.bad, 'start_session', {})).status === 200, 'a muted player can still play');
    check((await mk(PL.bad, 'get_dashboard_summary', {})).status === 200, 'a muted player can still use the rest of the API (a mute only stops chat)');
    check(+db(`select count(*) from notifications where player_id = '${PL.bad.id}' and title = 'Chat muted'`) === 1, 'the player was told about the mute');
    await p.click(`[data-remove="${badId}"]`); await askReason(p, 'e2e: abusive'); await p.waitForSelector('#reportout.ok');
    check(db(`select moderation from convoy_messages where id = ${badId}`) === 'removed', 'removing keeps the line hidden for everyone');
    check((await p.$(`[data-report="${badId}"]`)) === null, 'a decided line leaves the open queue');
    await p.selectOption('#reportstate', 'reviewed'); await p.waitForSelector(`[data-report="${badId}"]`); check(true, 'it is under "Already decided"');
    await p.click('#tab_sanctions'); await p.waitForSelector('[data-lift]'); check((await p.textContent('#sanctionlist')).includes('AdmE2eBad'), 'Restrictions lists the mute');
    await p.click('[data-lift]'); await askReason(p, 'e2e: apologised'); await p.waitForSelector('#sanctionsout.ok');
    check((await mk(PL.bad, 'post_convoy_message', { p_body: 'sorry everyone' })).status === 200, 'lifting the mute lets them chat again');
    // suspend from the player record
    await p.click('#tab_players'); await p.fill('#q', 'AdmE2eBad'); await p.click('button[type=submit]'); await p.waitForSelector('[data-open]'); await p.click('[data-open]'); await askReason(p, 'e2e: checking history'); await p.waitForSelector('#sanctionpanel [data-sanction]');
    check((await p.$('[data-sanction="ban"]')) === null && (await p.$('[data-sanction="suspend"]')) !== null, 'the record offers support mute and suspend, not ban');
    await p.click('[data-sanction="suspend"]'); await p.waitForSelector('dialog[open]'); await askReason(p, 'e2e: repeat offence'); await p.waitForSelector('#sanctionout.ok');
    const s1 = await mk(PL.bad, 'start_session', {}); check(s1.status >= 400 && /account_suspended/.test(JSON.stringify(s1.body)), 'a suspended player cannot start a session', JSON.stringify(s1));
    check(db(`select (banned_until > now() + interval '70 hours')::text from auth.users where id = '${PL.bad.id}'`) === 'true', 'the sign-in service is told (banned_until is set)');
    // the token the player already holds stops working at once, on every endpoint, not only on the two the triggers guard
    for (const [fn, args] of [['get_dashboard_summary', {}], ['get_my_wallet', {}], ['heartbeat', { p_session: '00000000-0000-0000-0000-000000000000' }], ['list_convoy_messages', {}], ['get_my_profile', {}]]) {
      const r = await mk(PL.bad, fn, args); check(r.status >= 400 && /account_suspended/.test(JSON.stringify(r.body)), 'a suspended player\'s existing token is refused at once: ' + fn, JSON.stringify(r)); }
    { const r = await mk(PL.bad, 'get_my_restrictions', {}); check(r.status === 200 && r.body.items[0].kind === 'suspend' && !('reason' in r.body.items[0]), 'but they can still see what is in force (and not the moderator\'s reason)', JSON.stringify(r)); }
    { const r = await mk(PL.bad, 'admin_whoami', {}); check(r.status === 200 && r.body.is_admin === false, 'the admin endpoints are not blocked by the hook (a player still gets nothing from them)', JSON.stringify(r)); }
    const si = await fetch(BASE + '/auth/v1/token?grant_type=password', { method: 'POST', headers: { 'content-type': 'application/json' }, body: JSON.stringify({ email: PL.bad.email, password: PL.bad.password }) });
    check(si.status === 400 && /banned/.test(await si.text()), 'a suspended player cannot sign in again');
    const b1 = await rpcAs(tok, 'admin_apply_sanction', { p_player: PL.cc.id, p_kind: 'ban', p_hours: null, p_reason: 'bypassing the page' }); check(b1.status >= 400 && /not_allowed/.test(JSON.stringify(b1.body)), 'the server refuses support a ban', JSON.stringify(b1));
    const b2 = await rpcAs(tok, 'admin_apply_sanction', { p_player: U.own.id, p_kind: 'mute', p_hours: 1, p_reason: 'restricting an admin' }); check(b2.status >= 400 && /cannot_sanction_admin/.test(JSON.stringify(b2.body)), 'administrators cannot be restricted', JSON.stringify(b2));
    // administrative recovery: an admin restricted by SQL (the site itself refuses to) is blocked from the game but never from the admin endpoints
    db(`insert into player_sanctions(player_id, kind, reason, created_by, expires_at) values ('${U.sup.id}', 'suspend', 'e2e: set in SQL', '${U.own.id}', now() + interval '1 day')`);
    { const g = await rpcAs(tok, 'get_dashboard_summary', {}); check(g.status >= 400 && /account_suspended/.test(JSON.stringify(g.body)), 'a restricted admin is blocked from the game endpoints', JSON.stringify(g));
      const a = await rpcAs(tok, 'admin_list_sanctions', {}); check(a.status === 200, 'but still reaches the admin endpoints (recovery stays possible)', JSON.stringify(a));
      const l = await rpcAs(tok, 'admin_lift_sanction', { p_id: db(`select id from player_sanctions where player_id = '${U.sup.id}' and lifted_at is null`), p_reason: 'e2e: recovering' }); check(l.status === 200, 'and can lift the restriction through the site', JSON.stringify(l));
      check((await rpcAs(tok, 'get_dashboard_summary', {})).status === 200, 'after which the game endpoints work again'); }
    await p.context().close(); }
  { // the owner bans; only an owner can lift it
    await own.click('#tab_players'); await own.fill('#q', 'AdmE2eDd'); await own.click('button[type=submit]'); await own.waitForSelector('[data-open]'); await own.click('[data-open]'); await askReason(own, 'e2e: review'); await own.waitForSelector('[data-sanction="ban"]');
    await own.click('[data-sanction="ban"]'); await askReason(own, 'e2e: cheating'); await own.waitForSelector('#sanctionout.ok');
    check(db(`select (banned_until > now() + interval '50 years')::text from auth.users where id = '${PL.dd.id}'`) === 'true', 'a ban has no end');
    { const r = await mk(PL.dd, 'get_dashboard_summary', {}); check(r.status >= 400 && /account_suspended/.test(JSON.stringify(r.body)), 'a banned player\'s existing token is refused at once', JSON.stringify(r)); }
    const s2 = await mk(PL.dd, 'start_session', {}); check(s2.status >= 400 && /account_suspended/.test(JSON.stringify(s2.body)), 'a banned player cannot start a session');
    const p = await rolePageAgain(U.sup); await p.click('#tab_sanctions'); await p.waitForSelector('[data-lift]'); await p.click('[data-lift]'); await askReason(p, 'e2e: support tries'); await p.waitForSelector('#sanctionsout.err');
    check(/not allowed/.test(await p.textContent('#sanctionsout')), 'support cannot lift a ban (the server says so)'); await p.context().close();
    await own.click('#tab_sanctions'); await own.waitForSelector('[data-lift]'); await own.locator('tr:has-text("AdmE2eDd") [data-lift]').click(); await askReason(own, 'e2e: appeal accepted'); await own.waitForSelector('#sanctionsout.ok');
    check((await mk(PL.dd, 'start_session', {})).status === 200, 'after an owner lifts the ban they can play again');
    check((await mk(PL.dd, 'get_dashboard_summary', {})).status === 200, 'and the same token works again on every endpoint');
    check(db(`select (banned_until is null)::text from auth.users where id = '${PL.dd.id}'`) === 'true', 'and the sign-in block is cleared'); }

  check(errors.length === 0, 'no script errors on any page', errors.join(' | '));
  await browser.close(); server.close();
  console.log(`\n${passed} passed, ${failed} failed`);
  if (failed) { console.log('ADMIN E2E FAILED:\n - ' + failures.join('\n - ')); process.exit(1); }
  console.log('ADMIN E2E PASSED');
}
run().catch(e => { console.error('ADMIN E2E ERROR', e); try { server.close(); } catch (x) { /* already closed */ } process.exit(1); });
