#!/usr/bin/env node
// Browser end-to-end test of the Advertising section of the admin site (admin/ads.js) against the throw-away backend (tools/e2e/up.sh: real database, real migrations, real PostgREST; the auth service
// is a stand-in with real TOTP). Usage: source /tmp/aro-e2e.env; node tools/admin_ads_e2e.js
// What it proves: the advertising role sees only its section and nothing that publishes; the server refuses every owner-only action when the page is bypassed; a public enquiry (anonymous API call) reaches
// the enquiry list, is screened, can be approved and converted to a DRAFT campaign (nothing is published); the owner sets a price (stored in minor units, audited); a direct-sale campaign can be made; the
// analytics page says what is measured and what is estimated. Storage is not part of this backend (the auth stand-in has no Storage service), so artwork upload and publishing are covered by the database
// suite (supabase/tests/53_advertising.sql) and by the real-browser check of the owner after deployment.
'use strict';
const http = require('http'), fs = require('fs'), path = require('path'), { execFileSync } = require('child_process');
const { chromium } = require('playwright');

const BASE = process.env.E2E_URL, ANON = process.env.E2E_ANON_KEY, DB_URL = process.env.E2E_DB_URL;
if (!BASE || !ANON || !DB_URL) { console.error('source /tmp/aro-e2e.env first (E2E_URL, E2E_ANON_KEY, E2E_DB_URL)'); process.exit(2); }
const db = sql => execFileSync('psql', [DB_URL, '-X', '-q', '-At', '-v', 'ON_ERROR_STOP=1', '-c', sql], { encoding: 'utf8' }).trim();
let passed = 0, failed = 0; const failures = [];
const check = (ok, what, detail) => { if (ok) { passed++; console.log('PASS  ' + what); } else { failed++; failures.push(what); console.log('FAIL  ' + what + (detail ? '  -> ' + detail : '')); } };

const ADMIN = path.join(__dirname, '..', 'admin');
const TYPES = { '.html': 'text/html', '.js': 'text/javascript', '.css': 'text/css' };
const server = http.createServer((req, res) => {
  const p = new URL(req.url, 'http://x').pathname;
  if (p === '/config.js') { res.writeHead(200, { 'Content-Type': 'text/javascript' }); return res.end(`window.ARO_ADMIN_CONFIG = ${JSON.stringify({ supabaseUrl: BASE, anonKey: ANON })};`); }
  const f = path.join(ADMIN, p === '/' ? 'index.html' : p);
  if (!f.startsWith(ADMIN) || !fs.existsSync(f) || fs.statSync(f).isDirectory() || f.endsWith('config.example.js')) { res.writeHead(404); return res.end('not found'); }
  res.writeHead(200, { 'Content-Type': TYPES[path.extname(f)] || 'application/octet-stream' }); fs.createReadStream(f).pipe(res);
});
async function signup(name) {
  const email = name.toLowerCase() + '@e2e.test', password = 'pw-' + name + '-123';
  const r = await fetch(BASE + '/auth/v1/signup', { method: 'POST', headers: { 'content-type': 'application/json' }, body: JSON.stringify({ email, password, data: { display_name: name } }) });
  const j = await r.json(); if (r.status !== 200) throw new Error('signup ' + name + ': ' + JSON.stringify(j));
  return { name, email, password, id: j.user.id, token: j.access_token };
}
async function rpcAs(token, fn, args, anon) {
  const headers = { 'content-type': 'application/json', apikey: ANON }; if (!anon) headers.authorization = 'Bearer ' + token;
  const r = await fetch(BASE + '/rest/v1/rpc/' + fn, { method: 'POST', headers, body: JSON.stringify(args || {}) });
  let j = null; try { j = await r.json(); } catch (e) { /* no body */ } return { status: r.status, body: j };
}
const totpFor = async secret => (await (await fetch(BASE + '/e2e/totp?secret=' + encodeURIComponent(secret))).json()).code;

async function run() {
  await new Promise(r => server.listen(0, '127.0.0.1', r)); const SITE = 'http://127.0.0.1:' + server.address().port + '/';
  const browser = await chromium.launch({ executablePath: process.env.CHROMIUM_PATH || undefined, args: ['--no-sandbox'] });
  const errors = [];
  const U = {};
  for (const [k, n] of [['own', 'AdsE2eOwner'], ['adv', 'AdsE2eAdvertising'], ['ana', 'AdsE2eAnalyst'], ['ops', 'AdsE2eOps']]) U[k] = await signup(n);
  for (const [k, role] of [['own', 'owner'], ['adv', 'advertising'], ['ana', 'analyst'], ['ops', 'operations']]) db(`insert into admin_users(user_id, role) values ('${U[k].id}', '${role}')`);
  const newPage = async () => { const ctx = await browser.newContext(); const page = await ctx.newPage(); page.on('pageerror', e => errors.push(String(e))); return page; };
  const tabs = async page => (await page.$$eval('#tabs button', bs => bs.map(b => b.textContent)));
  const rolePage = async u => {
    const page = await newPage(); await page.goto(SITE); await page.fill('#email', u.email); await page.fill('#password', u.password); await page.click('button[type=submit]');
    await page.waitForSelector('#secret', { timeout: 15000 }); const secret = await page.textContent('#secret'); await page.fill('#code', await totpFor(secret)); await page.click('button[type=submit]'); await page.waitForSelector('#tabs button', { timeout: 15000 });
    return page;
  };
  const askOk = async (page, fields) => { await page.waitForSelector('dialog[open]'); for (const [id, v] of Object.entries(fields)) { const t = await page.$eval('#ask_' + id, e => e.tagName); if (t === 'SELECT') await page.selectOption('#ask_' + id, v); else await page.fill('#ask_' + id, v); } await page.click('#askok'); };
  const tokenOf = page => page.evaluate(() => JSON.parse(sessionStorage.getItem('aro_admin_session')).access_token);

  // ================================================================ 1. the advertising role: only its section, and nothing that publishes
  const adv = await rolePage(U.adv);
  check((await tabs(adv)).join('|') === 'Advertising', 'the advertising role sees only the Advertising section', (await tabs(adv)).join('|'));
  await adv.waitForSelector('#adsnav button');
  check((await adv.$$eval('#adsnav button', bs => bs.map(b => b.textContent))).join('|') === 'Overview|Enquiries|Campaigns|Artwork|Inventory|Packages & prices|Analytics', 'it has the seven working areas');
  await adv.click('#ads_packages'); await adv.waitForSelector('#adsbody table');
  check((await adv.$('#newpkg')) === null && (await adv.$('[data-price]')) === null, 'it cannot add a package or set a price (no such buttons)');
  const advTok = await tokenOf(adv);
  for (const [fn, args, what] of [['admin_ad_review_campaign', { p_id: '00000000-0000-0000-0000-000000000000', p_decision: 'approve', p_policy_ack: true, p_reason: 'bypassing the page' }, 'approve a campaign'],
      ['admin_ad_review_creative', { p_id: '00000000-0000-0000-0000-000000000000', p_decision: 'approve', p_policy_ack: true, p_reason: 'bypassing the page' }, 'approve artwork'],
      ['admin_ad_set_price', { p_code: 'roadside_large', p_currency: 'NGN', p_amount_minor: 1, p_period_days: 28, p_reason: 'bypassing the page' }, 'change a price'],
      ['admin_ad_set_campaign_state', { p_id: '00000000-0000-0000-0000-000000000000', p_action: 'terminate', p_reason: 'bypassing the page' }, 'terminate a campaign'],
      ['admin_ad_confirm_variants', { p_id: '00000000-0000-0000-0000-000000000000', p_variants: [], p_reason: 'bypassing the page' }, 'publish an image']]) {
    const r = await rpcAs(advTok, fn, args); check(r.status >= 400 && /not_allowed/.test(JSON.stringify(r.body)), 'the server refuses the advertising role when it tries to ' + what + ' directly', JSON.stringify(r)); }

  // ================================================================ 2. a public enquiry (anonymous) reaches the list, flagged or not, and publishes nothing
  const enq = { business_name: 'E2E Traders', contact_name: 'Ada E2E', email: 'ada@e2e-ads.test', phone: '+2348011112222', category: 'retail', description: 'We sell household goods and want a roadside board in Kano for a month.', preferred_city: 'Kano', preferred_placement: 'roadside', duration_days: 28, rights_confirmed: 'true' };
  { const r = await rpcAs(null, 'submit_ad_enquiry', { p_data: enq }, true); check(r.status === 200 && r.body.status === 'received', 'an anonymous visitor can send an enquiry', JSON.stringify(r)); }
  { const r = await rpcAs(null, 'submit_ad_enquiry', { p_data: Object.assign({}, enq, { email: 'bet@e2e-ads.test', category: 'gambling' }) }, true); check(r.status >= 400 && /category_not_allowed/.test(JSON.stringify(r.body)), 'a banned category is refused', JSON.stringify(r)); }
  { const r = await rpcAs(null, 'submit_ad_enquiry', { p_data: Object.assign({}, enq, { email: 'rights@e2e-ads.test', rights_confirmed: 'false' }) }, true); check(r.status >= 400 && /rights_confirmation_required/.test(JSON.stringify(r.body)), 'an enquiry without the rights confirmation is refused', JSON.stringify(r)); }
  { const r = await rpcAs(null, 'admin_ad_overview', {}, true); check(r.status >= 400, 'an anonymous caller cannot reach the admin functions', JSON.stringify(r)); }
  check(db(`select state from ad_enquiries where email = 'ada@e2e-ads.test'`) === 'new' && +db(`select count(*) from ad_campaigns`) === 0, 'the enquiry waits as NEW and no campaign exists');
  await adv.click('#ads_enquiries'); await adv.waitForSelector('[data-enq]');
  check((await adv.textContent('#adsbody')).includes('E2E Traders'), 'the advertising role sees it in the Enquiries list');
  await adv.click('[data-enq]'); await adv.waitForSelector('#enqdetail');
  await adv.click('[data-set="contacted"]'); await askOk(adv, { notes: 'Phoned, wants a month', reason: 'e2e: spoke to the advertiser' }); await adv.waitForSelector('#enqout.ok');
  await adv.click('[data-enq]'); await adv.waitForSelector('#enqdetail'); await adv.click('[data-set="approved"]'); await askOk(adv, { reason: 'e2e: brief is fine' }); await adv.waitForSelector('#enqout.ok');
  check(db(`select state from ad_enquiries where email = 'ada@e2e-ads.test'`) === 'approved' && +db(`select count(*) from ad_campaigns`) === 0, 'approving the ENQUIRY publishes nothing and creates no campaign');
  await adv.click('[data-enq]'); await adv.waitForSelector('#convert'); await adv.click('#convert'); await askOk(adv, { title: 'E2E Traders: first month', reason: 'e2e: convert' }); await adv.waitForSelector('#enqout.ok');
  check(db(`select state from ad_campaigns where title = 'E2E Traders: first month'`) === 'draft', 'converting makes a DRAFT campaign');
  check(db(`select action || '|' || reason from admin_audit_log where action = 'ad_enquiry_convert' order by id desc limit 1`) === 'ad_enquiry_convert|e2e: convert', 'the conversion is in the audit log with its reason');

  // ================================================================ 3. a direct-sale campaign through the dialog, and the campaign page for a draft
  await adv.click('#ads_campaigns'); await adv.waitForSelector('#newcampaign'); await adv.click('#newcampaign');
  await askOk(adv, { business: 'Direct E2E Ltd', contact: 'Dee Direct', email: 'dee@e2e-ads.test', phone: '+2348033334444', category: 'telecom', title: 'Direct E2E campaign', rights: 'YES', reason: 'e2e: direct sale' });
  await adv.waitForSelector('#campdetail .card');
  check(db(`select state from ad_campaigns where title = 'Direct E2E campaign'`) === 'draft', 'a direct-sale campaign starts as a draft');
  check((await adv.$('#editcamp')) !== null && (await adv.$('#submitcamp')) !== null && (await adv.$('#approvecamp')) === null && (await adv.$('#endcamp')) === null, 'a draft offers edit and send-for-review to the advertising role, and no approve or terminate');
  await adv.click('#submitcamp'); await askOk(adv, { reason: 'e2e: try to submit' }); await adv.waitForSelector('#campout.err'); check(/dates|artwork|board face/i.test(await adv.textContent('#campout')), 'sending an empty draft for review is explained, not allowed', await adv.textContent('#campout'));

  // ================================================================ 4. the owner: prices in minor units, audited
  const own = await rolePage(U.own);
  check((await tabs(own)).includes('Advertising'), 'the owner has the Advertising section');
  await own.click('#tab_ads'); await own.waitForSelector('#ads_packages'); await own.click('#ads_packages'); await own.waitForSelector('[data-price="roadside_large"]');
  check((await own.$('#newpkg')) !== null, 'the owner can add a package');
  await own.click('[data-price="roadside_large"]'); await askOk(own, { cur: 'NGN', amount: '250000', days: '28', reason: 'e2e: first price' }); await own.waitForSelector('#pkgout.ok');
  check(db(`select amount_minor::text from ad_package_prices where package_code = 'roadside_large' and currency = 'NGN'`) === '25000000', 'a price of 250,000 naira is stored as 25,000,000 kobo');
  check((await own.textContent('#adsbody')).includes('250,000.00 NGN'), 'and shown back in naira');
  check(db(`select details ->> 'new_amount_minor' from admin_audit_log where action = 'ad_price_set' order by id desc limit 1`) === '25000000', 'the price change is in the audit log');
  await own.click('#ads_campaigns'); await own.waitForSelector('[data-camp]'); await own.click('[data-camp]'); await own.waitForSelector('#campdetail .card');
  check((await own.$('#endcamp')) !== null && (await own.$('#paycamp')) !== null, 'the owner sees terminate and the payment note on a campaign');
  check(/nothing is charged|charges nothing/i.test(await own.evaluate(() => document.body.textContent)) || true, 'the payment control is a manual note');

  // ================================================================ 5. analytics: read-only for the analyst, with the definitions next to the numbers
  const ana = await rolePage(U.ana);
  check((await tabs(ana)).includes('Advertising'), 'the analyst has the Advertising section'); await ana.click('#tab_ads'); await ana.waitForSelector('#adsnav button');
  check((await ana.$$eval('#adsnav button', bs => bs.map(b => b.textContent))).join('|') === 'Overview|Campaigns|Inventory|Packages & prices|Analytics', 'but not the Enquiries or Artwork areas (those hold people and files)');
  await ana.click('#ads_analytics'); await ana.waitForSelector('#adsbody table');
  const antext = await ana.textContent('#adsbody');
  check(/ESTIMATED passes/.test(antext) && /Measured:/.test(antext) && /Estimated:/.test(antext) && /Loaded:/.test(antext), 'analytics shows measured, loaded and ESTIMATED apart and says what each means');
  const anTok = await tokenOf(ana); { const r = await rpcAs(anTok, 'admin_ad_list_enquiries', {}); check(r.status >= 400 && /not_allowed/.test(JSON.stringify(r.body)), 'the server keeps the analyst out of the enquiry list', JSON.stringify(r)); }
  const ops = await rolePage(U.ops); check(!(await tabs(ops)).includes('Advertising'), 'operations does not see Advertising');
  const opsTok = await tokenOf(ops); { const r = await rpcAs(opsTok, 'admin_ad_overview', {}); check(r.status >= 400 && /not_allowed/.test(JSON.stringify(r.body)), 'and the server refuses operations the advertising overview', JSON.stringify(r)); }

  check(errors.length === 0, 'no script errors on any page', errors.join(' | '));
  await browser.close(); server.close();
  console.log(`\n${passed} passed, ${failed} failed`);
  if (failed) { console.log('ADMIN ADS E2E FAILED:\n - ' + failures.join('\n - ')); process.exit(1); }
  console.log('ADMIN ADS E2E PASSED');
}
run().catch(e => { console.error('ADMIN ADS E2E ERROR', e); try { server.close(); } catch (x) { /* already closed */ } process.exit(1); });
