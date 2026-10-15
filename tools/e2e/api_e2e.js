#!/usr/bin/env node
// HTTP-level end-to-end + security tests: real PostgREST, real JWTs, real Postgres (functions, RLS, grants, triggers, rate limits).
// Run by CI after tools/e2e/up.sh. Env: E2E_URL, PGRST_JWT_SECRET, E2E_DB_URL (for ops-side setup and "time passes" only: players never get this).
'use strict';
const crypto = require('crypto'), { execFileSync } = require('child_process');
const BASE = process.env.E2E_URL || 'http://127.0.0.1:54321', SECRET = process.env.PGRST_JWT_SECRET, DB_URL = process.env.E2E_DB_URL;
if (!SECRET || !DB_URL) { console.error('PGRST_JWT_SECRET and E2E_DB_URL are required'); process.exit(2); }

let passed = 0; const failed = [];
function check(name, ok, detail) { if (ok) { passed++; console.log('PASS  ' + name); } else { failed.push(name); console.log('FAIL  ' + name + (detail !== undefined ? '  -> ' + (typeof detail === 'string' ? detail : JSON.stringify(detail)).slice(0, 300) : '')); } }
const db = sql => execFileSync('psql', [DB_URL, '-X', '-q', '-At', '-v', 'ON_ERROR_STOP=1', '-c', sql], { encoding: 'utf8' }).trim();
const sleep = ms => new Promise(r => setTimeout(r, ms));

const b64 = b => Buffer.from(b).toString('base64url');
const jwt = (payload, secret = SECRET) => { const h = b64(JSON.stringify({ alg: 'HS256', typ: 'JWT' })), p = b64(JSON.stringify(payload)); return h + '.' + p + '.' + crypto.createHmac('sha256', secret).update(h + '.' + p).digest('base64url'); };
let ANON = '';

async function http(method, path, token, body, extra = {}) {
  const headers = { 'content-type': 'application/json', apikey: ANON, ...extra }; if (token) headers.authorization = 'Bearer ' + token; else headers.authorization = 'Bearer ' + ANON;
  const r = await fetch(BASE + path, { method, headers, body: body === undefined ? undefined : JSON.stringify(body) });
  const text = await r.text(); let json; try { json = JSON.parse(text); } catch { json = text; } return { status: r.status, body: json, text };
}
const rpc = (token, fn, args = {}) => http('POST', '/rest/v1/rpc/' + fn, token, args);
const rest = (token, method, path, body) => http(method, '/rest/v1/' + path, token, body);
const msg = r => (r.body && r.body.message) || (typeof r.body === 'string' ? r.body : '');
const denied = r => r.status === 401 || r.status === 403;

async function signup(name) {
  const email = name.toLowerCase() + '@e2e.test';
  const r = await http('POST', '/auth/v1/signup', null, { email, password: 'pw-' + name + '-123', data: { display_name: name } });
  if (r.status !== 200) throw new Error('signup failed for ' + name + ': ' + r.text);
  return { name, email, id: r.body.user.id, token: r.body.access_token, refresh: r.body.refresh_token };
}

(async () => {
  const health = await (await fetch(BASE + '/e2e/health')).json(); ANON = health.anon_key;

  // ======================================================================= authentication service
  const settings = await http('GET', '/auth/v1/settings', null);
  check('auth settings are public', settings.status === 200 && settings.body.external !== undefined);
  const A = await signup('E2eAlice'), B = await signup('E2eBob');
  check('sign-up creates a profile through the database trigger', (await rpc(A.token, 'get_my_profile')).body.display_name === 'E2eAlice');
  check('the starter grant is in the ledger', (await rpc(A.token, 'get_my_wallet')).body.balance === 5000);
  check('duplicate e-mail is refused', (await http('POST', '/auth/v1/signup', null, { email: A.email, password: 'whatever-1' })).status === 400);
  check('wrong password is refused', (await http('POST', '/auth/v1/token?grant_type=password', null, { email: A.email, password: 'nope' })).body.error_description === 'Invalid login credentials');
  const login = await http('POST', '/auth/v1/token?grant_type=password', null, { email: A.email, password: 'pw-E2eAlice-123' });
  check('password sign-in returns a session', login.status === 200 && !!login.body.access_token && !!login.body.refresh_token);
  const refreshed = await http('POST', '/auth/v1/token?grant_type=refresh_token', null, { refresh_token: login.body.refresh_token });
  check('a refresh token yields a new session', refreshed.status === 200 && refreshed.body.access_token !== login.body.access_token);
  check('a refresh token cannot be replayed', (await http('POST', '/auth/v1/token?grant_type=refresh_token', null, { refresh_token: login.body.refresh_token })).status === 400);
  check('a garbage refresh token is refused', (await http('POST', '/auth/v1/token?grant_type=refresh_token', null, { refresh_token: 'garbage' })).status === 400);

  // ======================================================================= API authentication: every protected endpoint rejects the unauthenticated and the forged
  const protectedCalls = [['get_my_profile', {}], ['get_dashboard_summary', {}], ['list_available_jobs', {}], ['get_world_state', {}], ['list_notifications', {}], ['heartbeat', { p_session: crypto.randomUUID() }],
    ['accept_job', { p_job: crypto.randomUUID(), p_vehicle: crypto.randomUUID() }], ['complete_job', { p_assignment: crypto.randomUUID(), p_damage_pct: 0 }], ['get_leaderboard', { p_metric: 'xp' }], ['list_friends', {}], ['get_my_convoy', {}]];
  for (const [fn, args] of protectedCalls) {
    const anon = await rpc(null, fn, args);
    check(`${fn}: the anonymous key is refused`, denied(anon), anon.status + ' ' + anon.text);
  }
  const forged = jwt({ aud: 'authenticated', role: 'authenticated', sub: A.id, exp: Math.floor(Date.now() / 1000) + 600 }, 'x'.repeat(40));
  const fr = await rpc(forged, 'get_my_profile'); check('a token signed with another secret is refused', fr.status === 401, fr.status + ' ' + fr.text);
  const expired = jwt({ aud: 'authenticated', role: 'authenticated', sub: A.id, exp: Math.floor(Date.now() / 1000) - 60 });
  const er = await rpc(expired, 'get_my_profile'); check('an expired token is refused with JWT expired (the client refreshes on this)', er.status === 401 && /expired/i.test(msg(er)), er.status + ' ' + er.text);
  const nosub = jwt({ aud: 'authenticated', role: 'authenticated', exp: Math.floor(Date.now() / 1000) + 600 });
  const ns = await rpc(nosub, 'get_my_profile'); check('a valid token without a subject has no identity', ns.status === 400 && msg(ns) === 'not_authenticated', ns.status + ' ' + ns.text);
  check('every response carries CORS for the browser', (await fetch(BASE + '/rest/v1/rpc/get_online_count', { method: 'OPTIONS' })).headers.get('access-control-allow-origin') === '*');

  // ======================================================================= authorisation: no table write, no foreign rows, no private columns, no identity from the body
  const before = db(`select experience || '/' || level || '/' || jobs_completed from profiles where id = '${A.id}'`);
  for (const [path, body] of [[`profiles?id=eq.${A.id}`, { experience: 999999, level: 50 }], [`player_wallets?player_id=eq.${A.id}`, { balance: 99999999 }], [`transactions?player_id=eq.${A.id}`, { amount: 1 }],
    [`vehicle_ownership?player_id=eq.${A.id}`, { damage_pct: 0, fuel_l: 9999, nickname: 'x' }], ['jobs', { reward: 99999999 }], ['job_assignments', { status: 'completed' }], [`profiles?id=eq.${A.id}`, { avatar_id: 'x', username: 'hijack' }]]) {
    const r = await rest(A.token, 'PATCH', path, body); check(`PATCH /${path.split('?')[0]} ${Object.keys(body).join(',')} is refused`, denied(r), r.status + ' ' + r.text);
  }
  check('direct INSERT into the ledger is refused', denied(await rest(A.token, 'POST', 'transactions', { player_id: A.id, kind: 'BONUS', amount: 1000000, balance_after: 1000000, idempotency_key: 'hack' })));
  check('direct DELETE of ledger rows is refused', denied(await rest(A.token, 'DELETE', 'transactions?player_id=eq.' + A.id)));
  check('nothing changed in the database', db(`select experience || '/' || level || '/' || jobs_completed from profiles where id = '${A.id}'`) === before);
  const ownWallet = await rest(A.token, 'GET', `player_wallets?player_id=eq.${A.id}`), otherWallet = await rest(A.token, 'GET', `player_wallets?player_id=eq.${B.id}`);
  check('own wallet row is readable', ownWallet.status === 200 && ownWallet.body.length === 1);
  check("another player's wallet row is invisible (RLS)", otherWallet.status === 200 && otherWallet.body.length === 0, otherWallet.text);
  check('transactions list only my own rows', (await rest(A.token, 'GET', 'transactions')).body.every(t => t.player_id === A.id));
  check("another player's vehicles are invisible", (await rest(A.token, 'GET', `vehicle_ownership?player_id=eq.${B.id}`)).body.length === 0);
  check('private profile columns are refused', denied(await rest(A.token, 'GET', 'profiles?select=last_seen_at')) && denied(await rest(A.token, 'GET', 'profiles?select=lifetime_earnings')));
  check('public profile columns are served', (await rest(A.token, 'GET', 'profiles?select=id,display_name,level')).status === 200);
  check('the convoy join code column is refused', denied(await rest(A.token, 'GET', 'convoys?select=session_code')));
  for (const t of ['game_sessions', 'analytics_events', 'notifications', 'friendships', 'api_rate_limits', 'game_config', 'job_telemetry', 'world_routes', 'content_items', 'player_challenges', 'convoy_invites'])
    check(`table ${t} is not readable by clients`, denied(await rest(A.token, 'GET', t)));
  for (const fn of ['_apply_transaction', 'generate_jobs', 'admin_adjust_wallet', 'admin_set_route', 'purge_old_data', 'sweep_stale_sessions', '_notify', '_track', 'cfg', 'handle_new_user'])
    check(`function ${fn} is not callable by clients`, denied(await rpc(A.token, fn, {})) || (await rpc(A.token, fn, {})).status === 404, fn);
  const withId = await rpc(A.token, 'get_my_profile', { p_player: B.id }); check('a caller-supplied player id is not accepted (no such parameter)', withId.status === 404, withId.status + ' ' + withId.text);
  const withLevel = await rpc(A.token, 'update_my_profile', { p_display_name: 'E2eAliceX', level: 99, experience: 99999 }); check('update_my_profile has no level / xp parameters', withLevel.status === 404, withLevel.status + ' ' + withLevel.text);
  const withReward = await rpc(A.token, 'complete_job', { p_assignment: crypto.randomUUID(), p_damage_pct: 0, p_reward: 999999 }); check('complete_job accepts no reward from the client', withReward.status === 404, withReward.status + ' ' + withReward.text);

  // ======================================================================= presence (server-side expiry, online count, server time)
  const sa = (await rpc(A.token, 'start_session', { p_client: 'webgl' })).body, sb = (await rpc(B.token, 'start_session', { p_client: 'webgl' })).body;
  check('start_session returns a server-made session id and the timeout', !!sa.session_id && sa.ttl_seconds === 90);
  const hb = await rpc(A.token, 'heartbeat', { p_session: sa.session_id, p_activity: 'driving', p_route: 'lagos-ibadan', p_city: 'lagos' });
  check('heartbeat is accepted and counts both players online', hb.status === 200 && hb.body.online_players === 2, hb.text);
  check('timestamps are the server clock', Math.abs(Date.parse(hb.body.server_time) - Date.now()) < 15000, hb.body.server_time);
  check("B cannot use A's session", msg(await rpc(B.token, 'heartbeat', { p_session: sa.session_id })) === 'session_not_found');
  check('an unknown route is refused', msg(await rpc(A.token, 'heartbeat', { p_session: sa.session_id, p_route: 'atlantis' })) === 'unknown_route');
  check('presence reports activity and route', (await rpc(A.token, 'get_my_presence')).body.route_code === 'lagos-ibadan');
  db(`update game_sessions set last_seen_at = now() - interval '3 minutes' where id = '${sb.session_id}'`);
  check('a session without heartbeats expires by itself (server timeout)', (await rpc(A.token, 'get_online_count')).body === 1);
  check('and its heartbeat is refused as expired', msg(await rpc(B.token, 'heartbeat', { p_session: sb.session_id })) === 'session_expired');
  const sb2 = (await rpc(B.token, 'start_session', { p_client: 'webgl' })).body;
  check('a fresh session brings the player back', (await rpc(A.token, 'get_online_count')).body === 2);
  check('end_session logs out cleanly', (await rpc(B.token, 'end_session', { p_session: sb2.session_id })).status === 204 && (await rpc(A.token, 'get_online_count')).body === 1);

  // ======================================================================= friends, convoys, notifications (ownership + IDOR)
  const fr1 = await rpc(A.token, 'send_friend_request', { p_player: B.id });
  check('friend request is created', fr1.status === 200 && fr1.body.status === 'pending');
  check('the same request twice is refused', msg(await rpc(A.token, 'send_friend_request', { p_player: B.id })) === 'request_pending');
  check('B has a friend-request notification', (await rpc(B.token, 'list_notifications')).body.items.some(n => n.type === 'friend_event'));
  check('the requester cannot accept their own request', msg(await rpc(A.token, 'respond_friend_request', { p_request: fr1.body.request_id, p_accept: true })) === 'request_not_found');
  check('the addressee accepts', (await rpc(B.token, 'respond_friend_request', { p_request: fr1.body.request_id, p_accept: true })).body === 'accepted');
  check('friends list shows the friend with presence', (await rpc(A.token, 'list_friends')).body.items[0].display_name === 'E2eBob');
  const carol = await signup('E2eCarol'); check('a stranger sees no friends', (await rpc(carol.token, 'list_friends')).body.items.length === 0);
  const nB = (await rpc(B.token, 'list_notifications')).body.items[0];
  check("B cannot mark A's notification read", msg(await rpc(A.token, 'mark_notification_read', { p_id: nB.id })) === 'notification_not_found');
  check('B marks their own read', (await rpc(B.token, 'mark_notification_read', { p_id: nB.id })).status === 204);
  const cv = await rpc(A.token, 'create_convoy', { p_name: 'E2E Convoy' }); check('convoy is created', cv.status === 200 && typeof cv.body === 'string');
  check('B joins', (await rpc(B.token, 'join_convoy', { p_convoy: cv.body })).status === 204);
  check('the roster has both drivers', (await rpc(A.token, 'get_my_convoy')).body.convoy.member_count === 2);
  check('a non-leader cannot kick', msg(await rpc(B.token, 'kick_convoy_member', { p_player: A.id })) === 'not_leader');
  check('a stranger cannot read the join code', (await rpc(carol.token, 'get_convoy_session_code', { p_convoy: cv.body })).status === 400);
  check('B leaves', (await rpc(B.token, 'leave_convoy')).status === 204);

  // ======================================================================= jobs: server-authoritative reward, telemetry validation, duplicate completion
  db(`select testkit.mk_job('E2E-1', 1, 777)`);
  const board = (await rpc(A.token, 'list_available_jobs')).body; const job = board.items.find(j => j.code === 'E2E-1');
  check('the job board lists the job with the server reward and ETA', !!job && job.reward === 777 && job.est_minutes === 4 && job.eligible === true, job);
  const veh = (await rpc(A.token, 'list_my_vehicles')).body.items[0];
  check("B cannot use A's vehicle for a job", msg(await rpc(B.token, 'accept_job', { p_job: job.id, p_vehicle: veh.id })) === 'vehicle_not_owned');
  const asg = (await rpc(A.token, 'accept_job', { p_job: job.id, p_vehicle: veh.id })).body;
  check('A accepts', typeof asg === 'string');
  check('the active job is served', (await rpc(A.token, 'get_my_active_job')).body.active.job.code === 'E2E-1');
  check("B cannot start A's assignment", msg(await rpc(B.token, 'start_job', { p_assignment: asg, p_x: 0, p_z: 0 })) === 'assignment_not_found');
  check('starting away from the pickup is refused', msg(await rpc(A.token, 'start_job', { p_assignment: asg, p_x: 900, p_z: 900 })) === 'not_at_pickup');
  check('start at the pickup', (await rpc(A.token, 'start_job', { p_assignment: asg, p_x: 0, p_z: 0 })).status === 204);
  db(`select testkit.age('${asg}', 5)`);
  const tp = await rpc(A.token, 'submit_telemetry', { p_assignment: asg, p_x: 2600, p_z: -900 });
  check('a teleport is rejected by the server', tp.body.accepted === false && tp.body.reason === 'too_fast', tp.text);
  const total = Math.hypot(2600, -900), stepM = 80 / 3.6 * 5; let f = 0, firstThird = false, lastAccepted = true;
  while (f < 1) {
    f = Math.min(1, f + stepM / total); db(`select testkit.age('${asg}', 5)`);
    const r = await rpc(A.token, 'submit_telemetry', { p_assignment: asg, p_x: 2600 * f, p_z: -900 * f }); lastAccepted = r.body.accepted === true; if (!lastAccepted) break;
    if (!firstThird && f > 0.3) { firstThird = true; const early = await rpc(A.token, 'complete_job', { p_assignment: asg, p_damage_pct: 0 }); check('completing away from the destination is refused', early.status === 400 && /delivery_too_fast|not_at_destination|distance_implausible/.test(msg(early)), early.text); }
  }
  check('a legitimate drive is accepted sample by sample', lastAccepted);
  const balBefore = (await rpc(A.token, 'get_my_wallet')).body.balance;
  db(`select testkit.age('${asg}', 5)`); await rpc(A.token, 'submit_telemetry', { p_assignment: asg, p_x: 2600, p_z: -900 });
  const done = await rpc(A.token, 'complete_job', { p_assignment: asg, p_damage_pct: 1 });
  check('delivery completes with the server-decided reward', done.status === 200 && done.body.reward === 777 && done.body.xp === 40, done.text);
  check('the wallet moved by exactly the reward', (await rpc(A.token, 'get_my_wallet')).body.balance === balBefore + 777);
  check('one immutable ledger row with a reason', db(`select count(*) || '/' || max(reason) from transactions where kind = 'JOB_REWARD' and player_id = '${A.id}'`) === '1/job_reward');
  check('a second completion is refused and pays nothing', msg(await rpc(A.token, 'complete_job', { p_assignment: asg, p_damage_pct: 1 })) === 'already_completed' && db(`select count(*) from transactions where kind = 'JOB_REWARD' and player_id = '${A.id}'`) === '1');
  const notes = (await rpc(A.token, 'list_notifications')).body;
  check('the payout notification exists', notes.items.some(n => n.type === 'payout' && /777/.test(n.message)), notes);
  check('first-delivery achievement unlocked', (await rpc(A.token, 'list_my_achievements')).body.items.some(a => a.id === 'first_delivery' && a.unlocked_at));
  check('progression reflects the XP', (await rpc(A.token, 'get_my_progression')).body.experience === 40);
  const lb = (await rpc(A.token, 'get_leaderboard', { p_metric: 'earnings' })).body;
  check('the earnings leaderboard ranks A first from server records', lb.items[0].display_name === 'E2eAlice' && lb.items[0].value === 777 && lb.me.rank === 1, lb);
  check('the vehicle mileage came from verified distance', (await rpc(A.token, 'list_my_vehicles')).body.items[0].mileage_km > 2);

  // ======================================================================= world + content + rate limiting
  const world = (await rpc(A.token, 'get_world_state')).body;
  check('the world state is authoritative: one open route, the rest planned', world.routes.filter(r => r.effective_status === 'OPEN').length === 1 && world.cities.length === 23);
  check('authored news is labelled authored', (await rpc(A.token, 'get_content', { p_kind: 'news' })).body.items.every(i => i.source === 'authored'));
  let ok = 0, limited = 0;
  // The limiter is a FIXED 60 s window (30 calls). A loop that straddles a window boundary splits its calls between two windows and can legitimately see none limited ({ok:45,limited:0}, observed in run 38706152411).
  // Start the loop in a fresh window when fewer than 15 s of the current one remain; the assertion below is unchanged.
  { const left = 60000 - (Date.now() % 60000); if (left < 15000) await sleep(left + 500); }
  for (let i = 0; i < 45; i++) { const r = await rpc(A.token, 'find_player', { p_username: 'e2ebob' }); if (r.status === 200) ok++; else if (msg(r) === 'rate_limited') limited++; }
  check('excessive requests are rate limited (HTTP)', limited > 0 && ok <= 40, { ok, limited });
  check("one player's limit does not affect another", (await rpc(carol.token, 'find_player', { p_username: 'e2ebob' })).status === 200);

  console.log(`\n${passed} passed, ${failed.length} failed`);
  if (failed.length) { console.log('FAILED:\n - ' + failed.join('\n - ')); process.exit(1); }
  console.log('API E2E PASSED');
})().catch(e => { console.error('E2E crashed:', e); process.exit(1); });
