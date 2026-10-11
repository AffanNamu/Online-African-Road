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
  create table if not exists e2e.refresh_tokens (token text primary key, user_id uuid not null, used boolean not null default false);`);

let TTL = 3600;                                   // seconds a new access token lives (tests shorten it to exercise renewal and expiry)
const hashPw = (pw, salt) => crypto.scryptSync(pw, salt, 32).toString('hex');
function session(userId, email) {
  const now = Math.floor(Date.now() / 1000);
  const access = sign({ aud: 'authenticated', role: 'authenticated', sub: userId, email, iss: 'aro-e2e', iat: now, exp: now + TTL, session_id: crypto.randomUUID() }); // like GoTrue: every session's token is distinct, even within one second
  const refresh = crypto.randomBytes(24).toString('hex');
  return psql('insert into e2e.refresh_tokens(token, user_id) values (:\'t\', :\'u\')', { t: refresh, u: userId })
    .then(() => ({ access_token: access, token_type: 'bearer', expires_in: TTL, refresh_token: refresh, user: { id: userId, email } }));
}

function body(req) { return new Promise(r => { const c = []; req.on('data', d => c.push(d)); req.on('end', () => r(Buffer.concat(c))); }); }
function json(res, code, obj) { const s = JSON.stringify(obj); res.writeHead(code, { 'Content-Type': 'application/json', 'Content-Length': Buffer.byteLength(s), ...CORS }); res.end(s); }
const CORS = { 'Access-Control-Allow-Origin': '*', 'Access-Control-Allow-Headers': 'authorization, apikey, content-type, prefer, accept, accept-profile, content-profile, x-client-info', 'Access-Control-Allow-Methods': 'GET, POST, PATCH, PUT, DELETE, OPTIONS' };

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
      return json(res, 200, await session(id, email));
    }
    if (grant === 'refresh_token') {
      const t = String(b.refresh_token || '');
      const row = await psql('update e2e.refresh_tokens set used = true where token = :\'t\' and not used returning user_id', { t });
      if (!row) return json(res, 400, { error: 'invalid_grant', error_description: 'Invalid Refresh Token: Already Used' });
      const email = await psql('select email from e2e.users where user_id = :\'u\'', { u: row });
      return json(res, 200, await session(row, email));
    }
  }
  if (url.pathname === '/auth/v1/recover') return json(res, 200, {});
  return json(res, 404, { msg: 'not found' });
}

function proxy(req, res) {
  const headers = { ...req.headers, host: REST.host };
  const up = http.request({ hostname: REST.hostname, port: REST.port, path: req.url.replace(/^\/rest\/v1/, ''), method: req.method, headers }, r => {
    res.writeHead(r.statusCode, { ...r.headers, ...CORS }); r.pipe(res);
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
    if (url.pathname === '/e2e/health') return json(res, 200, { ok: true, anon_key: ANON });
    if (url.pathname.startsWith('/auth/v1/')) return await auth(req, res, url);
    if (url.pathname.startsWith('/rest/v1/')) return proxy(req, res);
    return serveStatic(req, res, url);
  } catch (e) { console.error('shim error', e); json(res, 500, { msg: String(e.message || e) }); }
}).listen(PORT, '127.0.0.1', () => console.log(`e2e backend on http://127.0.0.1:${PORT} (rest -> ${REST.href}, build -> ${BUILD || 'none'})`));
