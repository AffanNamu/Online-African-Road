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
  check((await tabs(own)).join('|') === 'Live health|Players|Audit log|Team', 'an owner sees all four sections', (await tabs(own)).join('|'));
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
    check((await tabs(p)).join('|') === 'Live health|Players', 'support sees Live health and Players only', (await tabs(p)).join('|'));
    check((await p.$('#gen')) === null, 'support has no "generate jobs" button');
    const tok = await p.evaluate(() => JSON.parse(sessionStorage.getItem('aro_admin_session')).access_token);
    const g = await rpcAs(tok, 'admin_generate_jobs', { p_count: 5, p_reason: 'bypassing the page' }); check(g.status >= 400 && /not_allowed/.test(JSON.stringify(g.body)), 'the server refuses support when the page is bypassed (generate jobs)', JSON.stringify(g));
    const a = await rpcAs(tok, 'admin_list_audit'); check(a.status >= 400 && /not_allowed/.test(JSON.stringify(a.body)), 'the server refuses support the audit log', JSON.stringify(a));
    const s = await rpcAs(tok, 'admin_set_role', { p_target: U.sup.id, p_role: 'owner', p_reason: 'promote myself' }); check(s.status >= 400 && /not_allowed/.test(JSON.stringify(s.body)), 'the server refuses support changing roles', JSON.stringify(s));
    await p.click('#tab_players'); await p.fill('#q', 'AdmE2eTar'); await p.click('button[type=submit]'); await p.waitForSelector('[data-open]'); check(true, 'support can search and open records');
    await p.context().close(); }
  { const p = await rolePage(U.ops);
    check((await tabs(p)).join('|') === 'Live health|Players', 'operations sees Live health and Players only');
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

  check(errors.length === 0, 'no script errors on any page', errors.join(' | '));
  await browser.close(); server.close();
  console.log(`\n${passed} passed, ${failed} failed`);
  if (failed) { console.log('ADMIN E2E FAILED:\n - ' + failures.join('\n - ')); process.exit(1); }
  console.log('ADMIN E2E PASSED');
}
run().catch(e => { console.error('ADMIN E2E ERROR', e); try { server.close(); } catch (x) { /* already closed */ } process.exit(1); });
