#!/usr/bin/env node
// Throw-away backend for CI and local end-to-end runs. Three jobs, one port:
//   1. a minimal GoTrue-compatible auth API (/auth/v1/*): sign-up, password and refresh-token sign-in, issuing HS256 JWTs exactly like Supabase does
//   2. a reverse proxy from /rest/v1/* to a REAL PostgREST in front of the REAL database (the data path is not faked: functions, RLS, grants, triggers)
//   3. static hosting of the WebGL build (same origin as the API, so the browser needs no CORS)
// Nothing here is shipped, and it is never used outside tests: it stands in for the Supabase auth service only.
//
// Env: E2E_PORT (54321), E2E_REST (http://127.0.0.1:3000), PGRST_JWT_SECRET (required, >= 32 chars), E2E_DB_URL (postgres url), E2E_BUILD_DIR (optional)
'use strict';
const http = require('http'), fs = require('fs'), path = require('path'), crypto = require('crypto'), { spawn } = require('child_process');

const PORT = +process.env.E2E_PORT || 54321;
const REST = new URL(process.env.E2E_REST || 'http://127.0.0.1:3000');
const SECRET = process.env.PGRST_JWT_SECRET;
const DB_URL = process.env.E2E_DB_URL;
const BUILD = process.env.E2E_BUILD_DIR ? path.resolve(process.env.E2E_BUILD_DIR) : null;
if (!SECRET || SECRET.length < 32) { console.error('PGRST_JWT_SECRET (>= 32 chars) is required'); process.exit(2); }
if (!DB_URL) { console.error('E2E_DB_URL is required'); process.exit(2); }

const b64 = b => Buffer.from(b).toString('base64url');
function sign(payload) {
  const h = b64(JSON.stringify({ alg: 'HS256', typ: 'JWT' })), p = b64(JSON.stringify(payload));
  return h + '.' + p + '.' + crypto.createHmac('sha256', SECRET).update(h + '.' + p).digest('base64url');
}
const ANON = sign({ role: 'anon', iss: 'aro-e2e', exp: Math.floor(Date.now() / 1000) + 86400 * 30 });

// ------------------------------------------------------------------ database access through psql (parameters travel as psql variables, never spliced into SQL)
function psql(sql, vars = {}) {
  return new Promise((resolve, reject) => {
    const args = [DB_URL, '-X', '-q', '-At', '-v', 'ON_ERROR_STOP=1'];
    for (const [k, v] of Object.entries(vars)) args.push('-v', `${k}=${v}`);
    const p = spawn('psql', args); let out = '', err = '';
    p.stdout.on('data', d => out += d); p.stderr.on('data', d => err += d);
    p.on('close', code => code === 0 ? resolve(out.trim()) : reject(new Error(err.trim())));
    p.stdin.end(sql);
  });
}

const ready = psql(`
  create schema if not exists e2e;
  create table if not exists e2e.users (user_id uuid primary key, email text unique not null, salt text not null, hash text not null);
  create table if not exists e2e.refresh_tokens (token text primary key, user_id uuid not null, used boolean not null default false);
  alter table e2e.refresh_tokens add column if not exists aal text not null default 'aal1';
  create table if not exists e2e.factors (id uuid primary key default gen_random_uuid(), user_id uuid not null, secret_hex text not null, friendly_name text not null default '', status text not null default 'unverified');`);

let TTL = 3600;                                   // seconds a new access token lives (tests shorten it to exercise renewal and expiry)

// ------------------------------------------------------------------ TOTP (RFC 6238, SHA-1, 6 digits, 30 s) for the stand-in MFA endpoints
const B32 = 'ABCDEFGHIJKLMNOPQRSTUVWXYZ234567';
function base32(buf) { let bits = 0, val = 0, out = ''; for (const b of buf) { val = (val << 8) | b; bits += 8; while (bits >= 5) { out += B32[(val >>> (bits - 5)) & 31]; bits -= 5; } val &= (1 << bits) - 1; } if (bits > 0) out += B32[(val << (5 - bits)) & 31]; return out; }
function totp(secret, atMs) {
  const ctr = Buffer.alloc(8); ctr.writeBigUInt64BE(BigInt(Math.floor(atMs / 30000)));
  const h = crypto.createHmac('sha1', secret).update(ctr).digest(), o = h[h.length - 1] & 15;
  return String((((h[o] & 127) << 24) | (h[o + 1] << 16) | (h[o + 2] << 8) | h[o + 3]) % 1000000).padStart(6, '0');
}
const totpOk = (secret, code) => [-1, 0, 1].some(d => totp(secret, Date.now() + d * 30000) === String(code));
function bearer(req) {
  const t = String(req.headers.authorization || '').replace(/^Bearer /, ''), [h, p, sig] = t.split('.');
  if (!sig || crypto.createHmac('sha256', SECRET).update(h + '.' + p).digest('base64url') !== sig) return null;
  const c = JSON.parse(Buffer.from(p, 'base64url').toString()); return c.exp > Date.now() / 1000 && c.role === 'authenticated' ? c : null;
}
const challenges = new Map();      // challenge id -> factor id (in memory: this is a throw-away server)

const hashPw = (pw, salt) => crypto.scryptSync(pw, salt, 32).toString('hex');
function session(userId, email, aal = 'aal1') {
  const now = Math.floor(Date.now() / 1000);
  const amr = aal === 'aal2' ? [{ method: 'totp', timestamp: now }, { method: 'password', timestamp: now }] : [{ method: 'password', timestamp: now }];
  const access = sign({ aud: 'authenticated', role: 'authenticated', sub: userId, email, iss: 'aro-e2e', iat: now, exp: now + TTL, session_id: crypto.randomUUID(), aal, amr });   // like GoTrue: every session carries its assurance level
  const refresh = crypto.randomBytes(24).toString('hex');
  return psql('insert into e2e.refresh_tokens(token, user_id, aal) values (:\'t\', :\'u\', :\'a\')', { t: refresh, u: userId, a: aal })
    .then(() => ({ access_token: access, token_type: 'bearer', expires_in: TTL, refresh_token: refresh, user: { id: userId, email } }));
}

function body(req) { return new Promise(r => { const c = []; req.on('data', d => c.push(d)); req.on('end', () => r(Buffer.concat(c))); }); }
function json(res, code, obj) { const s = JSON.stringify(obj); res.writeHead(code, { 'Content-Type': 'application/json', 'Content-Length': Buffer.byteLength(s), ...CORS }); res.end(s); }
const CORS = { 'Access-Control-Allow-Origin': '*', 'Access-Control-Allow-Headers': 'authorization, apikey, content-type, prefer, accept, accept-profile, content-profile, x-client-info', 'Access-Control-Allow-Methods': 'GET, POST, PATCH, PUT, DELETE, OPTIONS' };

const isBanned = async id => (await psql("select (coalesce(banned_until > now(), false))::text from auth.users where id = :'u'", { u: id })) === 'true';

async function auth(req, res, url) {
  await ready;
  if (url.pathname === '/auth/v1/settings') return json(res, 200, { external: { google: false, apple: false, email: true }, disable_signup: false, mailer_autoconfirm: true });
  const raw = await body(req); let b = {}; try { b = raw.length ? JSON.parse(raw) : {}; } catch { return json(res, 400, { msg: 'invalid json' }); }
  if (url.pathname === '/auth/v1/signup') {
    const email = String(b.email || '').toLowerCase(), pw = String(b.password || ''), name = (b.data && b.data.display_name) || '';
    if (!/^[^@\s]+@[^@\s]+$/.test(email) || pw.length < 6) return json(res, 422, { msg: 'Unable to validate email address or password too short' });
    if (await psql('select 1 from e2e.users where email = :\'e\'', { e: email })) return json(res, 400, { msg: 'User already registered' });
    const id = await psql('insert into auth.users(id, email, raw_user_meta_data) values (gen_random_uuid(), :\'e\', jsonb_build_object(\'display_name\', :\'n\')) returning id', { e: email, n: name });
    const salt = crypto.randomBytes(16).toString('hex');
    await psql('insert into e2e.users values (:\'u\', :\'e\', :\'s\', :\'h\')', { u: id, e: email, s: salt, h: hashPw(pw, salt) });
    return json(res, 200, await session(id, email));
  }
  if (url.pathname === '/auth/v1/token') {
    const grant = url.searchParams.get('grant_type');
    if (grant === 'password') {
      const email = String(b.email || '').toLowerCase();
      const row = await psql('select user_id || \'|\' || salt || \'|\' || hash from e2e.users where email = :\'e\'', { e: email });
      const [id, salt, hash] = row ? row.split('|') : [];
      if (!row || hash !== hashPw(String(b.password || ''), salt)) return json(res, 400, { error: 'invalid_grant', error_description: 'Invalid login credentials' });
      if (await isBanned(id)) return json(res, 400, { error_code: 'user_banned', msg: 'User is banned' });          // like GoTrue, which reads auth.users.banned_until
      return json(res, 200, await session(id, email));
    }
    if (grant === 'refresh_token') {
      const t = String(b.refresh_token || '');
      const row = await psql('update e2e.refresh_tokens set used = true where token = :\'t\' and not used returning user_id || \'|\' || aal', { t });
      if (!row) return json(res, 400, { error: 'invalid_grant', error_description: 'Invalid Refresh Token: Already Used' });
      const [uid, aal] = row.split('|');
      if (await isBanned(uid)) return json(res, 400, { error_code: 'user_banned', msg: 'User is banned' });
      const email = await psql('select email from e2e.users where user_id = :\'u\'', { u: uid });
      return json(res, 200, await session(uid, email, aal));
    }
  }
  // ---- stand-in MFA endpoints (same paths and shapes as GoTrue's)
  const who = bearer(req);
  if (url.pathname === '/auth/v1/user' && req.method === 'GET') {
    if (!who) return json(res, 401, { msg: 'invalid JWT' });
    const rows = await psql("select coalesce(string_agg(id || ',' || friendly_name || ',' || status, ';'), '') from e2e.factors where user_id = :'u'", { u: who.sub });
    const factors = rows ? rows.split(';').map(r => { const [id, name, status] = r.split(','); return { id, friendly_name: name, factor_type: 'totp', status }; }) : [];
    return json(res, 200, { id: who.sub, email: who.email, factors });
  }
  if (url.pathname === '/auth/v1/factors' && req.method === 'POST') {
    if (!who) return json(res, 401, { msg: 'invalid JWT' });
    if (b.factor_type !== 'totp') return json(res, 422, { msg: 'only totp is supported' });
    await psql("delete from e2e.factors where user_id = :'u' and status = 'unverified'", { u: who.sub });
    const secret = crypto.randomBytes(20), id = await psql("insert into e2e.factors(user_id, secret_hex, friendly_name) values (:'u', :'s', :'n') returning id", { u: who.sub, s: secret.toString('hex'), n: String(b.friendly_name || 'Authenticator').slice(0, 40) });
    const b32 = base32(secret), uri = `otpauth://totp/ARO-E2E:${who.email}?secret=${b32}&issuer=ARO-E2E`;
    const svg = `<svg xmlns="http://www.w3.org/2000/svg" width="120" height="120"><rect width="120" height="120" fill="white"/><text x="6" y="60" font-size="10">QR (test double)</text></svg>`;
    return json(res, 200, { id, type: 'totp', friendly_name: b.friendly_name || '', totp: { qr_code: 'data:image/svg+xml;utf-8,' + encodeURIComponent(svg), secret: b32, uri } });
  }
  const fm = /^\/auth\/v1\/factors\/([0-9a-f-]{36})\/(challenge|verify)$/.exec(url.pathname);
  if (fm && req.method === 'POST') {
    if (!who) return json(res, 401, { msg: 'invalid JWT' });
    const row = await psql("select secret_hex || '|' || status from e2e.factors where id = :'f' and user_id = :'u'", { f: fm[1], u: who.sub });
    if (!row) return json(res, 404, { code: 'mfa_factor_not_found', msg: 'MFA factor not found' });
    const [secretHex] = row.split('|');
    if (fm[2] === 'challenge') { const cid = crypto.randomUUID(); challenges.set(cid, fm[1]); return json(res, 200, { id: cid, type: 'totp', expires_at: Math.floor(Date.now() / 1000) + 300 }); }
    if (challenges.get(String(b.challenge_id)) !== fm[1]) return json(res, 400, { code: 'mfa_challenge_expired', msg: 'MFA challenge has expired, verify against another challenge or create a new challenge.' });
    if (!totpOk(Buffer.from(secretHex, 'hex'), b.code)) return json(res, 400, { code: 'mfa_verification_failed', msg: 'Invalid TOTP code entered' });
    challenges.delete(String(b.challenge_id));
    await psql("update e2e.factors set status = 'verified' where id = :'f'", { f: fm[1] });
    return json(res, 200, await session(who.sub, who.email, 'aal2'));
  }
  if (url.pathname === '/auth/v1/recover') return json(res, 200, {});
  return json(res, 404, { msg: 'not found' });
}

function proxy(req, res) {
  const headers = { ...req.headers, host: REST.host };
  const up = http.request({ hostname: REST.hostname, port: REST.port, path: req.url.replace(/^\/rest\/v1/, ''), method: req.method, headers }, r => {
    const h = { ...r.headers }; for (const k of Object.keys(h)) if (k.toLowerCase().startsWith('access-control-')) delete h[k];   // PostgREST adds its own CORS headers; two copies make browsers refuse the answer (the game is same-origin and never noticed)
    res.writeHead(r.statusCode, { ...h, ...CORS }); r.pipe(res);
  });
  up.on('error', e => json(res, 502, { message: 'PostgREST unreachable: ' + e.message }));
  req.pipe(up);
}

const MIME = { '.html': 'text/html', '.js': 'text/javascript', '.css': 'text/css', '.wasm': 'application/wasm', '.json': 'application/json', '.png': 'image/png', '.ico': 'image/x-icon' };
function serveStatic(req, res, url) {
  if (!BUILD) return json(res, 404, { msg: 'no build directory configured' });
  let p = decodeURIComponent(url.pathname); if (p.endsWith('/')) p += 'index.html';
  const f = path.join(BUILD, p);
  if (!f.startsWith(BUILD) || !fs.existsSync(f) || fs.statSync(f).isDirectory()) { res.writeHead(404); return res.end('not found'); }
  res.writeHead(200, { 'Content-Type': MIME[path.extname(f)] || 'application/octet-stream' }); fs.createReadStream(f).pipe(res);
}

http.createServer(async (req, res) => {
  try {
    const url = new URL(req.url, 'http://x');
    if (req.method === 'OPTIONS') { res.writeHead(204, CORS); return res.end(); }
    if (url.pathname === '/e2e/ttl' && req.method === 'POST') { TTL = Math.max(10, +url.searchParams.get('seconds') || 3600); return json(res, 200, { ttl: TTL }); }
    if (url.pathname === '/e2e/totp') {      // test hook: the current code for a base32 secret the site displayed (what an authenticator app would show)
      const sec = String(url.searchParams.get('secret') || '').toUpperCase().replace(/=+$/, ''); let bits = 0, val = 0; const out = [];
      for (const ch of sec) { const v = B32.indexOf(ch); if (v < 0) return json(res, 400, { msg: 'bad secret' }); val = (val << 5) | v; bits += 5; if (bits >= 8) { out.push((val >>> (bits - 8)) & 255); bits -= 8; val &= (1 << bits) - 1; } }
      return json(res, 200, { code: totp(Buffer.from(out), Date.now()) });
    }
    if (url.pathname === '/e2e/health') return json(res, 200, { ok: true, anon_key: ANON });
    if (url.pathname.startsWith('/auth/v1/')) return await auth(req, res, url);
    if (url.pathname.startsWith('/rest/v1/')) return proxy(req, res);
    return serveStatic(req, res, url);
  } catch (e) { console.error('shim error', e); json(res, 500, { msg: String(e.message || e) }); }
}).listen(PORT, '127.0.0.1', () => console.log(`e2e backend on http://127.0.0.1:${PORT} (rest -> ${REST.href}, build -> ${BUILD || 'none'})`));
