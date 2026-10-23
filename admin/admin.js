// African Roads Online - admin site. No framework, no dependency, no build step.
// Security model (docs/ADMIN.md): this page holds no secret and decides nothing. Every action is an RPC that the DATABASE authorises (admin role, multi-factor sign-in, role per function) and records
// in an append-only audit log. What the page hides from a role is a convenience; what the server refuses is the protection. All data is shown with textContent (player names are untrusted).
'use strict';
(function () {
  if (window.top !== window.self) { document.body.textContent = 'This page cannot be shown inside a frame.'; return; }   // clickjacking: the headers say it too, this is the fallback

  const cfg = window.ARO_ADMIN_CONFIG || {};
  const $ = id => document.getElementById(id);
  const main = $('main');

  // ------------------------------------------------------------------ small DOM helpers (text only, never innerHTML)
  function el(tag, attrs, ...kids) {
    const e = document.createElement(tag);
    for (const [k, v] of Object.entries(attrs || {})) { if (k === 'class') e.className = v; else if (k.startsWith('on')) e.addEventListener(k.slice(2), v); else if (v !== false && v != null) e.setAttribute(k, v === true ? '' : v); }
    for (const kid of kids.flat()) if (kid != null && kid !== false) e.append(kid.nodeType ? kid : document.createTextNode(String(kid)));
    return e;
  }
  const clear = n => { while (n.firstChild) n.removeChild(n.firstChild); };
  const when = v => v ? new Date(v).toLocaleString() : '-';
  const ago = v => { if (!v) return 'never'; const s = Math.max(0, (Date.now() - new Date(v).getTime()) / 1000); return s < 90 ? 'just now' : s < 5400 ? Math.round(s / 60) + ' min ago' : s < 129600 ? Math.round(s / 3600) + ' h ago' : Math.round(s / 86400) + ' days ago'; };
  const num = v => (v == null ? '-' : Number(v).toLocaleString());
  function table(cols, rows) {
    return el('table', {}, el('thead', {}, el('tr', {}, cols.map(c => el('th', {}, c)))), el('tbody', {}, rows.length ? rows.map(r => el('tr', {}, r.map(c => el('td', {}, c)))) : el('tr', {}, el('td', { colspan: cols.length, class: 'muted' }, 'Nothing to show.'))));
  }

  // ------------------------------------------------------------------ errors in words
  const WORDS = {
    not_admin: 'This account is not an administrator.', mfa_required: 'Sign in with your authenticator code first.', not_allowed: 'Your role is not allowed to do this.',
    reason_required: 'Write a reason (3 to 300 characters).', query_too_short: 'Type at least 2 characters (at most 64).', rate_limited: 'Too many requests. Wait a minute.',
    player_not_found: 'No such player.', last_owner: 'The last owner cannot be removed or demoted.', invalid_role: 'That is not a role.', user_not_found: 'No such account.',
    not_an_admin: 'That account is not an admin.', not_authenticated: 'Your session ended. Sign in again.', 'Invalid login credentials': 'Wrong e-mail or password.',
    'Invalid TOTP code entered': 'That code is wrong or has expired. Try the next one.',
    invalid_id: 'The id may use a-z, 0-9, - and _ (2 to 40 characters).', invalid_kind: 'Unknown kind.', invalid_category: 'Unknown category.', invalid_title: 'The title is missing or too long.',
    invalid_body: 'The text is missing or too long.', invalid_image: 'The image key may use a-z, 0-9, - and _.', invalid_priority: 'Priority must be between -1000 and 1000.',
    invalid_schedule: 'The end must be after the start.', invalid_headline: 'A banner needs a headline of at most 40 characters.', invalid_eyebrow: 'The small line is too long.', invalid_button: 'A banner needs button text of at most 30 characters.',
    invalid_action: 'Choose what the banner button does.', kind_locked: 'An existing item cannot change between news and banner.', content_not_found: 'No such item.', invalid_state: 'Invalid state.',
    invalid_message: 'The message is missing or too long.', route_not_found: 'No such route.', route_not_playable: 'This route has no playable road yet, so it cannot be opened.', invalid_status: 'Invalid status.',
    invalid_rank: 'The level a route needs must be between 1 and 100.', server_not_found: 'No such server.', invalid_capacity: 'Capacity must be 0 (no limit) to 100000.', invalid_region: 'The region must be 2 to 40 characters.',
    report_not_found: 'That report no longer exists.', invalid_decision: 'Invalid decision.', invalid_duration: 'Duration must be 1 to 2160 hours (90 days).', cannot_sanction_admin: 'Administrators cannot be restricted.',
    message_mismatch: 'That chat line was not written by this player.', already_banned: 'This player is already banned.', already_sanctioned: 'This player already has that restriction.',
    sanction_not_found: 'No such restriction.', sanction_not_active: 'That restriction is not active any more.'
  };
  class ApiError extends Error { constructor(msg, status) { super(msg); this.status = status; } }
  const words = e => WORDS[e.message] || (e instanceof ApiError ? (e.status ? 'The server refused the request (' + e.status + ').' : 'Could not reach the server.') : 'Something went wrong.');

  // ------------------------------------------------------------------ session (kept for this tab only; closing the tab ends it)
  let session = null, me = null, email = '';
  try { session = JSON.parse(sessionStorage.getItem('aro_admin_session') || 'null'); email = sessionStorage.getItem('aro_admin_email') || ''; } catch (e) { session = null; }
  function keep(s) { session = s; try { if (s) sessionStorage.setItem('aro_admin_session', JSON.stringify(s)); else { sessionStorage.removeItem('aro_admin_session'); sessionStorage.removeItem('aro_admin_email'); } } catch (e) { /* private mode: the session simply lives in memory */ } }
  const fromAuth = j => ({ access_token: j.access_token, refresh_token: j.refresh_token, expires_at: Date.now() + (j.expires_in || 3600) * 1000 });

  async function raw(path, opts) {
    const headers = { apikey: cfg.anonKey, 'Content-Type': 'application/json' };
    if (opts.auth !== false && session) headers.Authorization = 'Bearer ' + session.access_token;
    let res;
    try { res = await fetch(cfg.supabaseUrl.replace(/\/$/, '') + path, { method: opts.method || 'GET', headers, body: opts.body === undefined ? undefined : JSON.stringify(opts.body) }); }
    catch (e) { throw new ApiError('network', 0); }
    let j = null; try { j = await res.json(); } catch (e) { j = null; }
    if (!res.ok) throw new ApiError((j && (j.message || j.error_description || j.msg || j.error)) || 'http_' + res.status, res.status);
    return j;
  }
  let refreshing = null;
  async function refresh() {
    if (!session || !session.refresh_token) throw new ApiError('not_authenticated', 401);
    refreshing = refreshing || raw('/auth/v1/token?grant_type=refresh_token', { method: 'POST', auth: false, body: { refresh_token: session.refresh_token } })
      .then(j => keep(fromAuth(j))).finally(() => { refreshing = null; });
    return refreshing;
  }
  async function api(path, opts = {}) {
    if (session && opts.auth !== false && session.expires_at - Date.now() < 30000) { try { await refresh(); } catch (e) { keep(null); throw new ApiError('not_authenticated', 401); } }
    try { return await raw(path, opts); }
    catch (e) { if (e.status === 401 && opts.auth !== false && session) { try { await refresh(); } catch (e2) { keep(null); throw new ApiError('not_authenticated', 401); } return raw(path, opts); } throw e; }
  }
  const rpc = (name, args) => api('/rest/v1/rpc/' + name, { method: 'POST', body: args || {} });

  // ------------------------------------------------------------------ a question with a required answer (reasons, codes)
  function ask({ title, text, fields, ok }) {
    return new Promise(resolve => {
      const dlg = $('ask'), form = $('askform'), box = $('askfields'), err = $('askerr');
      $('asktitle').textContent = title; $('asktext').textContent = text || ''; $('askok').textContent = ok || 'Continue'; err.textContent = ''; clear(box);
      const inputs = {};
      for (const f of fields) {
        const input = f.type === 'select' ? el('select', { id: 'ask_' + f.name }, f.options.map(o => el('option', { value: o }, o))) : el('input', { id: 'ask_' + f.name, type: f.type || 'text', value: f.value || '', maxlength: f.max || 300, autocomplete: 'off', min: f.min, max: f.maxv });
        if (f.type === 'select' && f.value) input.value = f.value;
        inputs[f.name] = input; box.append(el('label', { for: 'ask_' + f.name }, f.label), input);
      }
      const done = v => { dlg.removeEventListener('close', onClose); form.onsubmit = null; $('askcancel').onclick = null; if (dlg.open) dlg.close(); resolve(v); };
      const onClose = () => done(null);
      form.onsubmit = ev => {
        ev.preventDefault(); const out = {};
        for (const f of fields) { out[f.name] = inputs[f.name].value.trim(); const bad = f.check && f.check(out[f.name]); if (bad) { err.textContent = bad; return; } }
        done(out);
      };
      $('askcancel').onclick = () => done(null); dlg.addEventListener('close', onClose); dlg.showModal(); const first = box.querySelector('input, select'); if (first) first.focus();
    });
  }
  const reasonField = label => ({ name: 'reason', label: label || 'Reason (kept in the audit log)', check: v => (v.length < 3 ? 'Write a reason (3 to 300 characters).' : '') });

  // ------------------------------------------------------------------ sign-in, then the second factor
  function screen(title, ...kids) { clear(main); main.append(el('div', { class: 'card narrow' }, el('h1', {}, title), ...kids)); $('top').hidden = true; }
  function showSignIn(message) {
    const err = el('p', { class: 'err', role: 'alert' }, message || '');
    const f = el('form', { onsubmit: async ev => {
      ev.preventDefault(); err.textContent = ''; const btn = f.querySelector('button'); btn.disabled = true;
      try { const j = await raw('/auth/v1/token?grant_type=password', { method: 'POST', auth: false, body: { email: f.email.value.trim(), password: f.password.value } }); keep(fromAuth(j)); email = f.email.value.trim(); try { sessionStorage.setItem('aro_admin_email', email); } catch (e) { /* memory only */ } f.password.value = ''; await afterSignIn(); }
      catch (e) { err.textContent = words(e); btn.disabled = false; }
    } }, el('label', { for: 'email' }, 'E-mail'), el('input', { id: 'email', name: 'email', type: 'email', required: true, autocomplete: 'username' }), el('label', { for: 'password' }, 'Password'), el('input', { id: 'password', name: 'password', type: 'password', required: true, autocomplete: 'current-password' }), err, el('button', { type: 'submit' }, 'Sign in'));
    screen('Admin sign-in', el('p', { class: 'muted' }, 'Administrators only. Every action is recorded.'), f);
    if (!cfg.supabaseUrl || !cfg.anonKey || /YOUR-/.test(cfg.supabaseUrl + cfg.anonKey)) err.textContent = 'This site is not configured (config.js is missing).';
  }
  async function signOut(message) {
    try { if (session) await raw('/auth/v1/logout', { method: 'POST' }); } catch (e) { /* the token is dropped here either way */ }
    keep(null); me = null; stopTimers(); showSignIn(message);
  }
  async function afterSignIn() {
    me = await rpc('admin_whoami');
    if (!me.is_admin) { screen('Not an administrator', el('p', {}, 'This account has no admin access.'), el('button', { type: 'button', onclick: () => signOut() }, 'Sign out')); return; }
    if (!me.mfa_ok) return showMfa();
    showApp();
  }
  async function showMfa() {
    const user = await api('/auth/v1/user'), verified = (user.factors || []).find(f => f.factor_type === 'totp' && f.status === 'verified');
    const err = el('p', { class: 'err', role: 'alert' }), codeBox = el('input', { id: 'code', name: 'code', inputmode: 'numeric', autocomplete: 'one-time-code', pattern: '[0-9]{6}', maxlength: 6, required: true });
    let factorId = verified && verified.id, enrol = null;
    async function verify(code) {
      const ch = await api('/auth/v1/factors/' + factorId + '/challenge', { method: 'POST', body: {} });
      keep(fromAuth(await api('/auth/v1/factors/' + factorId + '/verify', { method: 'POST', body: { challenge_id: ch.id, code } })));
      me = await rpc('admin_whoami'); if (!me.mfa_ok) throw new ApiError('mfa_required', 403); showApp();
    }
    const form = el('form', { onsubmit: async ev => { ev.preventDefault(); err.textContent = ''; const b = form.querySelector('button'); b.disabled = true; try { await verify(codeBox.value.trim()); } catch (e) { err.textContent = words(e); b.disabled = false; codeBox.select(); } } },
      el('label', { for: 'code' }, verified ? '6-digit code from your authenticator app' : 'Enter the 6-digit code to finish setting it up'), codeBox, err, el('button', { type: 'submit' }, 'Verify'));
    if (verified) { screen('Second step', el('p', { class: 'muted' }, 'Open your authenticator app and enter the current code.'), form, el('p', {}, el('button', { type: 'button', class: 'ghost', onclick: () => signOut() }, 'Sign out'))); codeBox.focus(); return; }
    enrol = await api('/auth/v1/factors', { method: 'POST', body: { factor_type: 'totp', friendly_name: 'ARO Admin ' + new Date().toISOString().slice(0, 16) } });
    factorId = enrol.id;
    screen('Set up two-step sign-in', el('p', { class: 'muted' }, 'Admin access needs an authenticator app (Google Authenticator, Authy, 1Password...). Scan the code or type the key, then enter the 6-digit code it shows.'),
      el('img', { class: 'qr', alt: 'QR code for your authenticator app', src: enrol.totp.qr_code }), el('p', {}, 'Key: ', el('code', { id: 'secret' }, enrol.totp.secret)), form, el('p', {}, el('button', { type: 'button', class: 'ghost', onclick: () => signOut() }, 'Sign out')));
    codeBox.focus();
  }

  // ------------------------------------------------------------------ the app
  const TABS = [
    { id: 'health', label: 'Live health', roles: ['owner', 'operations', 'support', 'analyst'], view: viewHealth },
    { id: 'players', label: 'Players', roles: ['owner', 'support', 'operations'], view: viewPlayers },
    { id: 'content', label: 'News & banners', roles: ['owner', 'content'], view: viewContent },
    { id: 'broadcast', label: 'Broadcast', roles: ['owner', 'operations'], view: viewBroadcast },
    { id: 'world', label: 'Routes & servers', roles: ['owner', 'operations'], view: viewWorld },
    { id: 'reports', label: 'Reports', roles: ['owner', 'support'], view: viewReports },
    { id: 'sanctions', label: 'Restrictions', roles: ['owner', 'support'], view: viewSanctions },
    { id: 'audit', label: 'Audit log', roles: ['owner', 'analyst'], view: viewAudit },
    { id: 'team', label: 'Team', roles: ['owner'], view: viewTeam }
  ];
  let timers = [], current = null;
  function stopTimers() { timers.forEach(clearInterval); timers = []; }
  let idleTimer = null;
  function touchIdle() { clearTimeout(idleTimer); idleTimer = setTimeout(() => { if (session) signOut('You were signed out after 20 minutes without activity.'); }, 20 * 60 * 1000); }
  ['click', 'keydown'].forEach(t => document.addEventListener(t, () => { if (session) touchIdle(); }));

  function showApp() {
    $('top').hidden = false; $('who').textContent = (email ? email + ' - ' : '') + me.role; touchIdle();
    const nav = $('tabs'); clear(nav);
    const mine = TABS.filter(t => t.roles.includes(me.role));
    for (const t of mine) nav.append(el('button', { type: 'button', id: 'tab_' + t.id, onclick: () => go(t.id) }, t.label));
    go(mine[0].id);
  }
  async function go(id) {
    const tab = TABS.find(t => t.id === id); if (!tab) return;
    stopTimers(); current = id;
    for (const b of $('tabs').children) b.removeAttribute('aria-current'); const btn = $('tab_' + id); if (btn) btn.setAttribute('aria-current', 'page');
    clear(main); main.append(el('h1', {}, tab.label));
    const body = el('div'); main.append(body);
    try { await tab.view(body); } catch (e) { if (e.message === 'not_authenticated' || e.message === 'mfa_required') return signOut(words(e)); body.append(el('p', { class: 'err', role: 'alert' }, words(e))); }
  }
  $('signout').addEventListener('click', () => signOut());

  // ---- live health
  const ALERTS = {
    jobs_empty: ['bad', "The job board is EMPTY: players see 'No open jobs right now'."], jobs_low: ['warn', 'Fewer than 10 jobs are open.'],
    jobs_not_generated_for_12h: ['warn', 'No job has been created for 12 hours: the scheduled top-up may have stopped.']
  };
  function tile(label, value, note, level) { return el('div', { class: 'tile ' + (level || '') }, el('div', { class: 'v' }, value), el('div', { class: 'l' }, label), note ? el('div', { class: 'n' }, note) : null); }
  async function viewHealth(root) {
    const alerts = el('div'), tiles = el('div', { class: 'tiles' }), act = el('div'), stamp = el('p', { class: 'muted', id: 'stamp' });
    root.append(alerts, tiles, act, stamp);
    async function load() {
      const h = await rpc('admin_health'); clear(alerts); clear(tiles);
      for (const a of h.alerts) { const [lvl, msg] = ALERTS[a] || ['warn', a]; alerts.append(el('div', { class: 'alert ' + lvl, 'data-alert': a }, msg)); }
      const j = h.jobs, p = h.players;
      tiles.append(tile('Open jobs', num(j.open), j.expiring_within_6h + ' expire within 6 h', j.open === 0 ? 'bad' : j.open < 10 ? 'warn' : ''),
        tile('Jobs created, last 24 h', num(j.created_last_24h), 'last: ' + ago(j.last_created_at)),
        tile('Players online', num(p.online_now), Object.entries(p.online_by_activity).map(([k, v]) => k + ' ' + v).join(', ') || 'nobody'),
        tile('Players', num(p.total), num(p.new_last_24h) + ' new in 24 h'),
        tile('Database', h.schema.migrations_applied == null ? '-' : num(h.schema.migrations_applied) + ' migrations', h.schema.last_migration || ''));
      stamp.textContent = 'Updated ' + when(h.server_time) + '. Refreshes every 30 seconds.';
    }
    await load(); timers.push(setInterval(() => load().catch(e => { stamp.textContent = words(e); }), 30000));
    if (['owner', 'operations'].includes(me.role)) {
      const out = el('p', { id: 'genout', class: 'muted' });
      act.append(el('h2', {}, 'Job board'), el('p', { class: 'muted' }, 'A timer already tops the board up every few hours. Use this if it is empty now.'),
        el('button', { type: 'button', id: 'gen', onclick: async () => {
          const a = await ask({ title: 'Generate jobs now', text: 'Creates new open jobs (1 to 40) from the job catalogue. This is recorded in the audit log.', ok: 'Generate', fields: [{ name: 'count', label: 'How many (1 to 40)', type: 'number', value: '20', min: 1, maxv: 40, max: 2, check: v => (!/^\d+$/.test(v) || +v < 1 || +v > 40 ? 'Enter a number from 1 to 40.' : '') }, reasonField('Reason, for example "board empty since the 14th"')] });
          if (!a) return;
          try { const r = await rpc('admin_generate_jobs', { p_count: +a.count, p_reason: a.reason }); out.textContent = 'Created ' + r.made + ' jobs; ' + r.open + ' are open now.'; out.className = 'ok'; await load(); } catch (e) { out.textContent = words(e); out.className = 'err'; }
        } }, 'Generate jobs now'), out);
    }
  }

  // ---- players
  async function viewPlayers(root) {
    const canOpen = ['owner', 'support'].includes(me.role), results = el('div'), record = el('div', { id: 'record' }), err = el('p', { class: 'err', role: 'alert' });
    const form = el('form', { onsubmit: async ev => {
      ev.preventDefault(); err.textContent = ''; clear(results); clear(record);
      try {
        const r = await rpc('admin_find_players', { p_query: form.q.value.trim() }), rows = r.items.map(p => [p.display_name, p.username || '-', p.level, p.country_code || '-', p.online ? 'online' : ago(p.last_seen_at),
          canOpen ? el('button', { type: 'button', class: 'ghost', 'data-open': p.id, onclick: () => openRecord(p) }, 'Open record') : '']);
        results.append(el('p', { class: 'muted' }, r.items.length + ' found (' + r.kind + ' search)'), table(['Name', 'Username', 'Level', 'Country', 'Last seen', ''], rows));
      } catch (e) { err.textContent = words(e); }
    } }, el('div', { class: 'row' }, el('div', {}, el('label', { for: 'q' }, 'Name, username, player id' + (['owner', 'support'].includes(me.role) ? ' or exact e-mail' : '')), el('input', { id: 'q', name: 'q', required: true, maxlength: 64, autocomplete: 'off' })), el('button', { type: 'submit' }, 'Search')));
    root.append(el('div', { class: 'card' }, form, err), results, record);
    async function openRecord(p) {
      const a = await ask({ title: 'Open ' + p.display_name, text: 'Opening a player record is written to the audit log with your reason (a ticket number or why you need it).', ok: 'Open record', fields: [reasonField('Reason')] });
      if (!a) return; clear(record); err.textContent = '';
      try { renderRecord(record, await rpc('admin_get_player', { p_id: p.id, p_reason: a.reason })); record.scrollIntoView(); } catch (e) { err.textContent = words(e); }
    }
  }
  function kv(pairs) { return el('dl', { class: 'kv' }, pairs.flatMap(([k, v]) => [el('dt', {}, k), el('dd', {}, v == null || v === '' ? '-' : String(v))])); }
  function renderRecord(root, r) {
    const p = r.profile;
    root.append(el('h2', {}, p.display_name), el('div', { class: 'card' }, kv([['Player id', p.id], ['Username', p.username], ['E-mail', p.email_masked], ['Level', p.level + ' (' + num(p.experience) + ' XP)'], ['Country', p.country_code], ['Language', p.language_code], ['Play style', p.play_style],
      ['Jobs completed', num(p.jobs_completed)], ['Distance', num(p.distance_km) + ' km'], ['Lifetime earnings', num(p.lifetime_earnings)], ['Joined', when(p.created_at)], ['Last seen', p.online ? 'online now' : when(p.last_seen_at)], ['Onboarded', when(p.onboarded_at)]])),
      el('div', { class: 'card' }, el('strong', {}, 'Wallet balance: '), el('span', { id: 'balance' }, num(r.wallet.balance)), ' coins'),
      el('h2', {}, 'Ledger (latest 30)'), table(['When', 'Kind', 'Amount', 'Balance after', 'Reason'], r.ledger.map(t => [when(t.created_at), t.kind, num(t.amount), num(t.balance_after), t.reason || ''])),
      el('h2', {}, 'Vehicles'), table(['Vehicle', 'Name', 'Fuel (l)', 'Damage %', 'Odometer km', 'Acquired'], r.vehicles.map(v => [v.definition_id, v.nickname || '', v.fuel_l, v.damage_pct, v.odometer_km, when(v.acquired_at)])),
      el('h2', {}, 'Jobs (latest 15)'), table(['Job', 'Status', 'Reward', 'Accepted', 'Completed', 'Distance km'], r.jobs.map(j => [j.job_code, j.status, num(j.reward), when(j.accepted_at), when(j.completed_at), j.reported_distance_km == null ? '-' : j.reported_distance_km])),
      el('h2', {}, 'Sessions (latest 10)'), table(['Started', 'Last seen', 'Ended', 'Activity', 'Client'], r.sessions.map(s => [when(s.started_at), when(s.last_seen_at), s.ended_at ? when(s.ended_at) + (s.end_reason ? ' (' + s.end_reason + ')' : '') : 'open', s.activity, s.client])),
      el('h2', {}, 'Social'), kv([['Friends', r.social.friends], ['Blocked by', r.social.blocked_by], ['Blocking', r.social.blocking]]), el('h2', {}, 'Restrictions'), sanctionPanel(p.id, p.display_name));
  }

  // ---- audit log
  async function viewAudit(root) {
    const body = el('tbody'), more = el('button', { type: 'button', class: 'ghost', id: 'older' }, 'Load older'), err = el('p', { class: 'err', role: 'alert' }); let before = null;
    root.append(el('p', { class: 'muted' }, 'Every admin read of a player record and every admin change. Entries cannot be edited or deleted.'),
      el('table', {}, el('thead', {}, el('tr', {}, ['When', 'Admin', 'Role', 'Action', 'Target', 'Reason', 'Details'].map(c => el('th', {}, c)))), body), err, more);
    async function load() {
      try {
        const r = await rpc('admin_list_audit', before ? { p_limit: 50, p_before: before } : { p_limit: 50 });
        for (const l of r.items) body.append(el('tr', {}, [when(l.at), l.admin_name || l.admin_id, l.role, l.action, l.target_type ? l.target_type + ' ' + (l.target_id || '') : '', l.reason || '', JSON.stringify(l.details)].map(c => el('td', {}, c))));
        if (r.items.length) before = r.items[r.items.length - 1].id; more.hidden = r.items.length < 50;
      } catch (e) { err.textContent = words(e); }
    }
    more.addEventListener('click', load); await load();
  }

  // ---- team
  async function viewTeam(root) {
    const list = el('div'), out = el('p', { id: 'teamout', class: 'muted' });
    async function load() { const r = await rpc('admin_list_admins'); clear(list); list.append(table(['Name', 'Username', 'Role', 'Active', 'Since', 'Player id'], r.items.map(a => [a.display_name || '-', a.username || '-', a.role, a.active ? 'yes' : 'no', when(a.created_at), el('code', {}, a.user_id)]))); }
    const form = el('form', { onsubmit: async ev => {
      ev.preventDefault(); out.textContent = ''; out.className = 'muted';
      const a = await ask({ title: 'Confirm the change', text: 'Role changes are recorded in the audit log with your reason.', ok: 'Change role', fields: [reasonField('Reason')] }); if (!a) return;
      try { const r = await rpc('admin_set_role', { p_target: form.uid.value.trim(), p_role: form.role.value, p_reason: a.reason }); out.textContent = 'Done: role is now ' + r.role + '.'; out.className = 'ok'; await load(); } catch (e) { out.textContent = words(e); out.className = 'err'; }
    } }, el('div', { class: 'row' }, el('div', {}, el('label', { for: 'uid' }, 'Player id (find it under Players)'), el('input', { id: 'uid', name: 'uid', required: true, pattern: '[0-9a-fA-F-]{36}', maxlength: 36, autocomplete: 'off' })),
      el('div', {}, el('label', { for: 'role' }, 'Role'), el('select', { id: 'role', name: 'role' }, ['owner', 'operations', 'support', 'analyst', 'content', 'none'].map(r => el('option', { value: r }, r)))), el('button', { type: 'submit' }, 'Change role')));
    root.append(el('p', { class: 'muted' }, 'Owner: all. Operations: live health, job generation, broadcasts, routes and servers, player search. Support: player search and records, the reports queue, mute and suspend. Analyst: live health and the audit log. Content: news and banners only. "none" removes access. The last owner cannot be removed.'), el('div', { class: 'card' }, form, out), list);
    await load();
  }


  // ------------------------------------------------------------------ content and messages
  const dtLocal = v => { if (!v) return ''; const d = new Date(v), z = n => String(n).padStart(2, '0'); return d.getFullYear() + '-' + z(d.getMonth() + 1) + '-' + z(d.getDate()) + 'T' + z(d.getHours()) + ':' + z(d.getMinutes()); };
  const dtIso = v => (v ? new Date(v).toISOString() : null);
  const badge = (text, cls) => el('span', { class: 'badge ' + (cls || text) }, text);
  const field = (label, input, id) => el('div', {}, el('label', { for: id }, label), input);
  const ACTIONS = ['jobs', 'convoy', 'bus', 'map', 'garage'], CATEGORIES = ['update', 'event', 'tip', 'maintenance'];

  async function viewContent(root) {
    const list = el('div'), editor = el('div', { id: 'editor' }), out = el('p', { id: 'contentout', class: 'muted', role: 'status' });
    root.append(el('p', { class: 'muted' }, 'News items and the home-screen banner. An item reaches players only when it is published, its start time has passed and its end time has not. Drafts and scheduled items can be prepared in advance.'),
      el('p', {}, el('button', { type: 'button', id: 'newitem', onclick: () => edit(null) }, 'New item')), out, editor, list);
    async function load() {
      const r = await rpc('admin_list_content');
      clear(list);
      list.append(table(['State', 'Kind', 'Id', 'Title', 'Priority', 'From', 'Until', ''], r.items.map(c => [badge(c.state), c.kind, el('code', {}, c.id), c.title, c.priority, when(c.publish_at), c.expires_at ? when(c.expires_at) : 'no end',
        el('span', { class: 'acts' }, el('button', { type: 'button', class: 'ghost', 'data-edit': c.id, onclick: () => edit(c) }, 'Edit'),
          el('button', { type: 'button', class: 'ghost', 'data-toggle': c.id, onclick: () => toggle(c) }, c.published ? 'Unpublish' : 'Publish'))])));
    }
    async function toggle(c) {
      const a = await ask({ title: (c.published ? 'Unpublish ' : 'Publish ') + c.id, text: c.published ? 'Players stop seeing it.' : 'Players see it once its start time has passed.', ok: c.published ? 'Unpublish' : 'Publish', fields: [reasonField('Reason')] }); if (!a) return;
      out.textContent = ''; out.className = 'muted';
      try { await rpc('admin_set_content_published', { p_id: c.id, p_published: !c.published, p_reason: a.reason }); await load(); out.textContent = c.id + (c.published ? ' unpublished.' : ' published.'); out.className = 'ok'; } catch (e) { out.textContent = words(e); out.className = 'err'; }
    }
    function edit(c) {
      clear(editor); out.textContent = ''; out.className = 'muted';
      const isNew = !c, v = c || { kind: 'news', category: 'update', priority: 0, published: false };
      const f = {};
      const mk = (name, tag, attrs, kids) => (f[name] = el(tag, Object.assign({ id: 'f_' + name }, attrs), kids || null));
      mk('kind', 'select', { disabled: !isNew }, ['news', 'hero'].map(k => el('option', { value: k }, k === 'hero' ? 'hero (home banner)' : 'news')));
      mk('id', 'input', { maxlength: 40, value: v.id || '', readonly: !isNew, placeholder: 'e.g. race-weekend' });
      mk('category', 'select', {}, CATEGORIES.map(k => el('option', { value: k }, k)));
      mk('title', 'input', { maxlength: 80, value: v.title || '' });
      mk('body', 'textarea', { maxlength: 220, rows: 3 }); f.body.value = v.body || '';
      mk('image', 'input', { maxlength: 40, value: v.image_key || '', placeholder: 'optional' });
      mk('priority', 'input', { type: 'number', min: -1000, max: 1000, value: String(v.priority) });
      mk('from', 'input', { type: 'datetime-local', value: dtLocal(v.publish_at) });
      mk('until', 'input', { type: 'datetime-local', value: dtLocal(v.expires_at) });
      mk('published', 'input', { type: 'checkbox' }); f.published.checked = !!v.published;
      mk('eyebrow', 'input', { maxlength: 80, value: v.eyebrow || '' });
      mk('headline', 'input', { maxlength: 40, value: v.headline || '' });
      mk('accent', 'input', { maxlength: 40, value: v.headline_accent || '' });
      mk('action', 'select', {}, ACTIONS.map(k => el('option', { value: k }, k)));
      mk('button', 'input', { maxlength: 30, value: v.button || '' });
      mk('reason', 'input', { maxlength: 300, placeholder: 'why this change (kept in the audit log)' });
      f.kind.value = v.kind; f.category.value = v.category; if (v.action) f.action.value = v.action;
      const heroOnly = el('div', { id: 'heroFields' }, el('div', { class: 'row' }, field('Small line above the headline', f.eyebrow, 'f_eyebrow'), field('Headline', f.headline, 'f_headline'), field('Headline accent (second colour)', f.accent, 'f_accent')),
        el('div', { class: 'row' }, field('Button text', f.button, 'f_button'), field('Button action', f.action, 'f_action')));
      const prev = el('div', { id: 'preview', class: 'preview', 'aria-label': 'Preview' }), err = el('p', { class: 'err', role: 'alert', id: 'editorerr' });
      function preview() {
        clear(prev); const hero = f.kind.value === 'hero';
        heroOnly.hidden = !hero;
        if (hero) prev.append(el('div', { class: 'pv-eyebrow' }, f.eyebrow.value), el('div', { class: 'pv-head' }, f.headline.value + ' ', el('span', { class: 'pv-accent' }, f.accent.value)), el('div', { class: 'pv-body' }, f.body.value), el('span', { class: 'pv-btn' }, f.button.value || 'Button'));
        else prev.append(el('div', { class: 'pv-eyebrow' }, f.category.value), el('div', { class: 'pv-head' }, f.title.value || 'Title'), el('div', { class: 'pv-body' }, f.body.value));
        const n = el('div', { class: 'muted' }, 'Title ' + f.title.value.length + '/80 - text ' + f.body.value.length + '/220'); prev.append(n);
      }
      const form = el('form', { id: 'contentform', onsubmit: async ev => {
        ev.preventDefault(); err.textContent = ''; const btn = form.querySelector('button[type=submit]'); btn.disabled = true;
        const hero = f.kind.value === 'hero';
        try {
          await rpc('admin_save_content', { p_id: f.id.value.trim(), p_kind: f.kind.value, p_category: f.category.value, p_title: f.title.value, p_body: f.body.value, p_image_key: f.image.value.trim() || null, p_priority: parseInt(f.priority.value || '0', 10),
            p_starts_at: dtIso(f.from.value), p_ends_at: dtIso(f.until.value), p_published: f.published.checked, p_eyebrow: hero ? f.eyebrow.value : null, p_headline: hero ? f.headline.value : null, p_headline_accent: hero ? f.accent.value : null,
            p_action: hero ? f.action.value : null, p_button: hero ? f.button.value : null, p_reason: f.reason.value });
          clear(editor); await load(); out.textContent = 'Saved ' + f.id.value.trim() + '.'; out.className = 'ok';
        } catch (e) { err.textContent = words(e); btn.disabled = false; }
      } },
        el('div', { class: 'row' }, field('Kind', f.kind, 'f_kind'), field('Id (cannot change later)', f.id, 'f_id'), field('Category', f.category, 'f_category'), field('Priority (higher shows first)', f.priority, 'f_priority')),
        field('Title', f.title, 'f_title'), field('Text', f.body, 'f_body'), heroOnly,
        el('div', { class: 'row' }, field('Image key (optional)', f.image, 'f_image'), field('Show from (your time; empty = now)', f.from, 'f_from'), field('Hide after (empty = never)', f.until, 'f_until')),
        el('label', { class: 'check' }, f.published, ' Published (visible to players when the start time has passed)'), field('Reason', f.reason, 'f_reason'),
        el('h2', {}, 'Preview'), prev, err, el('div', { class: 'row' }, el('button', { type: 'submit', id: 'savecontent' }, 'Save'), el('button', { type: 'button', class: 'ghost', onclick: () => clear(editor) }, 'Cancel')));
      form.addEventListener('input', preview); preview();
      editor.append(el('div', { class: 'card' }, el('h2', {}, isNew ? 'New item' : 'Edit ' + c.id), form)); editor.scrollIntoView();
    }
    await load();
  }

  async function viewBroadcast(root) {
    const out = el('p', { id: 'bcout', class: 'muted', role: 'status' }), hist = el('div'), err = el('p', { class: 'err', role: 'alert', id: 'bcerr' });
    const title = el('input', { id: 'bc_title', maxlength: 80, autocomplete: 'off' }), msg = el('textarea', { id: 'bc_message', maxlength: 300, rows: 3 }), reason = el('input', { id: 'bc_reason', maxlength: 300, placeholder: 'why you are sending it (kept in the audit log)' });
    const prev = el('div', { class: 'preview', id: 'bc_preview' });
    function preview() { clear(prev); prev.append(el('div', { class: 'pv-head' }, title.value || 'Title'), el('div', { class: 'pv-body' }, msg.value || 'Message'), el('div', { class: 'muted' }, 'Appears in every player\'s notification inbox.')); }
    async function load() {
      const r = await rpc('admin_list_broadcasts'); clear(hist);
      hist.append(el('h2', {}, 'Sent'), table(['When', 'Title', 'Message', 'Recipients', 'Reason'], r.items.map(b => [when(b.at), b.title, b.message, num(b.recipients), b.reason || ''])));
    }
    const form = el('form', { id: 'bcform', onsubmit: async ev => {
      ev.preventDefault(); err.textContent = ''; out.textContent = ''; out.className = 'muted';
      let total = null; try { total = (await rpc('admin_health')).players.total; } catch (e) { total = null; }
      const a = await ask({ title: 'Send to every player?', text: 'This sends the notification to ' + (total == null ? 'every player' : num(total) + ' players') + ' and cannot be recalled. At most 3 can be sent an hour.', ok: 'Send to everyone', fields: [] }); if (!a) return;
      try { const r = await rpc('admin_send_broadcast', { p_title: title.value, p_message: msg.value, p_reason: reason.value }); title.value = ''; msg.value = ''; reason.value = ''; preview(); await load(); out.textContent = 'Sent to ' + num(r.recipients) + ' players.'; out.className = 'ok'; }
      catch (e) { err.textContent = words(e); }
    } }, field('Title', title, 'bc_title'), field('Message', msg, 'bc_message'), field('Reason', reason, 'bc_reason'), el('h2', {}, 'Preview'), prev, err, el('button', { type: 'submit', id: 'bcsend' }, 'Send...'));
    form.addEventListener('input', preview); preview();
    root.append(el('p', { class: 'muted' }, 'One notification to every player, for example a maintenance notice. Use news for anything that is not urgent.'), el('div', { class: 'card' }, form), out, hist);
    await load();
  }

  async function viewWorld(root) {
    const out = el('p', { id: 'worldout', class: 'muted', role: 'status' }), box = el('div');
    root.append(el('p', { class: 'muted' }, 'Open, plan or lock a route and set a server online or in maintenance. A route can be opened only if a playable road exists for it.'), out, box);
    async function load() {
      const r = await rpc('admin_list_world'); clear(box);
      box.append(el('h2', {}, 'Routes'), table(['Code', 'Name', 'Region', 'km', 'Status', 'Level needed', 'Playable', ''], r.routes.map(x => [el('code', {}, x.code), x.name, x.region || '-', x.distance_km == null ? '-' : x.distance_km, badge(x.status), x.unlock_level, x.playable ? 'yes' : 'no',
        el('button', { type: 'button', class: 'ghost', 'data-route': x.code, onclick: () => editRoute(x) }, 'Change')])),
        el('h2', {}, 'Servers (' + num(r.online_now) + ' players online now)'), table(['Id', 'Name', 'Region', 'Capacity', 'Status', ''], r.servers.map(x => [el('code', {}, x.id), x.name, x.region || '-', x.capacity == null ? 'no limit' : x.capacity, badge(x.status),
          el('button', { type: 'button', class: 'ghost', 'data-server': x.id, onclick: () => editServer(x) }, 'Change')])));
    }
    async function run(fn) { out.textContent = ''; out.className = 'muted'; try { const msg = await fn(); await load(); out.textContent = msg; out.className = 'ok'; } catch (e) { out.textContent = words(e); out.className = 'err'; } }
    async function editRoute(x) {
      const a = await ask({ title: 'Route ' + x.code, text: 'Locking a route stops new jobs on it; players already driving are not interrupted.', ok: 'Save', fields: [{ name: 'status', label: 'Status', type: 'select', options: ['OPEN', 'PLANNED', 'LOCKED'], value: x.status },
        { name: 'rank', label: 'Player level needed (1 to 100)', type: 'number', value: String(x.unlock_level), min: 1, maxv: 100, max: 3, check: v => (!/^\d+$/.test(v) || +v < 1 || +v > 100 ? 'Enter a number from 1 to 100.' : '') }, reasonField()] }); if (!a) return;
      await run(async () => { const r = await rpc('admin_set_route_state', { p_code: x.code, p_status: a.status, p_unlock_rank: +a.rank, p_reason: a.reason }); return x.code + ' is now ' + r.status + ' (level ' + r.unlock_level + ').'; });
    }
    async function editServer(x) {
      const a = await ask({ title: 'Server ' + x.name, text: 'Maintenance shows players that the server is down for now. Capacity 0 means no limit.', ok: 'Save', fields: [{ name: 'status', label: 'Status', type: 'select', options: ['ONLINE', 'MAINTENANCE'], value: x.status },
        { name: 'capacity', label: 'Capacity (0 = no limit)', type: 'number', value: x.capacity == null ? '0' : String(x.capacity), min: 0, maxv: 100000, max: 6, check: v => (!/^\d+$/.test(v) || +v > 100000 ? 'Enter a number from 0 to 100000.' : '') },
        { name: 'region', label: 'Region', value: x.region || '', max: 40 }, reasonField()] }); if (!a) return;
      await run(async () => { const r = await rpc('admin_set_server_state', { p_id: x.id, p_status: a.status, p_capacity: +a.capacity, p_region: a.region || null, p_reason: a.reason }); return x.name + ' is now ' + r.status + '.'; });
    }
    await load();
  }

  // ------------------------------------------------------------------ moderation
  // Mute: cannot chat. Suspend: cannot start a session or chat. Ban: owner only, no end date. The server refuses anyone who may not do it; the buttons are only a convenience.
  async function applySanction(playerId, name, kind, messageId) {
    const timed = kind !== 'ban', hours = { name: 'hours', label: 'Hours (1 to 2160 = 90 days)', type: 'number', value: kind === 'mute' ? '24' : '72', min: 1, maxv: 2160, max: 4, check: v => (!/^\d+$/.test(v) || +v < 1 || +v > 2160 ? 'Enter 1 to 2160.' : '') };
    const a = await ask({ title: { mute: 'Mute ', suspend: 'Suspend ', ban: 'Ban ' }[kind] + name, ok: { mute: 'Mute', suspend: 'Suspend', ban: 'Ban permanently' }[kind],
      text: { mute: 'They cannot send chat messages until it ends.', suspend: 'They are signed out of any open session and cannot start a new one until it ends.', ban: 'They are signed out and can never sign in again unless an owner lifts it.' }[kind], fields: (timed ? [hours] : []).concat([reasonField('Reason (shown to other admins; not shown to the player)')]) });
    if (!a) return null;
    const args = { p_player: playerId, p_kind: kind, p_hours: timed ? +a.hours : null, p_reason: a.reason }; if (messageId) args.p_message = messageId;
    return rpc('admin_apply_sanction', args);
  }
  async function liftSanction(s) {
    const a = await ask({ title: 'Lift the ' + s.kind + ' on ' + (s.player_name || 'this player'), text: 'It ends now. This is recorded in the audit log.', ok: 'Lift', fields: [reasonField('Reason')] }); if (!a) return null;
    return rpc('admin_lift_sanction', { p_id: s.id, p_reason: a.reason });
  }
  const until = s => (s.kind === 'ban' ? 'never ends' : 'until ' + when(s.expires_at));

  function sanctionPanel(playerId, name) {
    const box = el('div', { id: 'sanctionpanel' }), out = el('p', { class: 'muted', id: 'sanctionout', role: 'status' });
    async function load() {
      const r = await rpc('admin_list_sanctions', { p_state: 'active', p_player: playerId }); clear(box);
      box.append(r.items.length ? table(['Restriction', 'Ends', 'Reason', 'By', ''], r.items.map(s => [badge(s.kind), until(s), s.reason, s.created_by_name || '-', el('button', { type: 'button', class: 'ghost', 'data-lift': s.id, onclick: () => act(() => liftSanction(s)) }, 'Lift')]))
        : el('p', { class: 'muted' }, 'None active.'));
      const kinds = me.role === 'owner' ? ['mute', 'suspend', 'ban'] : ['mute', 'suspend'];
      box.append(el('p', { class: 'acts' }, kinds.map(k => el('button', { type: 'button', class: k === 'ban' ? 'danger' : 'ghost', 'data-sanction': k, onclick: () => act(() => applySanction(playerId, name, k, null)) }, k === 'ban' ? 'Ban...' : k === 'mute' ? 'Mute...' : 'Suspend...'))), out);
    }
    async function act(fn) { out.textContent = ''; out.className = 'muted'; try { const r = await fn(); await load(); if (r) { out.textContent = 'Done.'; out.className = 'ok'; } } catch (e) { out.textContent = words(e); out.className = 'err'; } }
    load().catch(e => { box.append(el('p', { class: 'err' }, words(e))); });
    return box;
  }

  async function viewReports(root) {
    const list = el('div', { id: 'reportlist' }), out = el('p', { id: 'reportout', class: 'muted', role: 'status' });
    const state = el('select', { id: 'reportstate', onchange: () => load().catch(show) }, [['open', 'Needs a decision'], ['reviewed', 'Already decided'], ['all', 'All']].map(([v, l]) => el('option', { value: v }, l)));
    root.append(el('p', { class: 'muted' }, 'Chat lines that players reported. Three different reporters hide a line automatically until someone here decides. Dismiss shows it again; Remove keeps it hidden for everyone.'), el('div', { class: 'row' }, el('div', {}, el('label', { for: 'reportstate' }, 'Show'), state)), out, list);
    const show = e => { out.textContent = words(e); out.className = 'err'; };
    async function load() {
      const r = await rpc('admin_list_reports', { p_state: state.value }); clear(list);
      if (!r.items.length) list.append(el('p', { class: 'muted' }, state.value === 'open' ? 'Nothing needs a decision.' : 'Nothing to show.'));
      for (const x of r.items) {
        const ctx = el('div', { class: 'ctx', hidden: true });
        const act = fn => async () => { out.textContent = ''; out.className = 'muted'; try { const res = await fn(); if (res) { await load(); out.textContent = 'Done.'; out.className = 'ok'; } } catch (e) { show(e); } };
        const decide = decision => act(async () => {
          const a = await ask({ title: decision === 'remove' ? 'Remove this line' : 'Dismiss the reports', text: decision === 'remove' ? 'The line stays hidden for everyone.' : 'The line is shown again. A new report would bring it back here.', ok: decision === 'remove' ? 'Remove' : 'Dismiss', fields: [reasonField('Reason')] });
          return a && rpc('admin_review_report', { p_message: x.message_id, p_decision: decision, p_reason: a.reason });
        });
        const sender = x.sender_name || 'a driver';
        list.append(el('div', { class: 'card report', 'data-report': x.message_id },
          el('div', { class: 'row-head' }, el('strong', {}, sender), ' ', el('span', { class: 'muted' }, x.reports + ' report' + (x.reports === 1 ? '' : 's') + ' (' + Object.entries(x.reasons || {}).map(([k, v]) => k + ' ' + v).join(', ') + ') - sent ' + ago(x.sent_at)), ' ',
            badge(x.moderation), x.decision ? badge(x.decision === 'remove' ? 'removed' : 'dismissed') : null, x.sender_sanctions.map(k => badge(k))),
          el('blockquote', {}, x.body), ctx,
          el('p', { class: 'acts' }, el('button', { type: 'button', class: 'ghost', 'data-context': x.message_id, onclick: act(async () => { const c = await rpc('admin_get_report', { p_message: x.message_id }); clear(ctx); ctx.hidden = false;
              ctx.append(el('p', { class: 'muted' }, 'Earlier lines by this player removed so far: ' + c.sender.removed_lines + ' - restrictions so far: ' + c.sender.sanctions_so_far), el('ul', {}, c.context.map(l => el('li', { class: l.reported ? 'hit' : '' }, el('strong', {}, (l.kind === 'system' ? 'system' : l.sender_name || 'a driver') + ': '), l.body, l.moderation !== 'ok' ? ' [' + l.moderation + ']' : ''))));
              return null; }) }, 'Show context'),
            x.open ? el('button', { type: 'button', class: 'ghost', 'data-dismiss': x.message_id, onclick: decide('dismiss') }, 'Dismiss') : null,
            x.open ? el('button', { type: 'button', 'data-remove': x.message_id, onclick: decide('remove') }, 'Remove line') : null,
            x.sender_id ? el('button', { type: 'button', class: 'ghost', 'data-mute': x.message_id, onclick: act(() => applySanction(x.sender_id, sender, 'mute', x.message_id)) }, 'Mute sender...') : null,
            x.sender_id ? el('button', { type: 'button', class: 'ghost', 'data-suspend': x.message_id, onclick: act(() => applySanction(x.sender_id, sender, 'suspend', x.message_id)) }, 'Suspend sender...') : null,
            x.sender_id && me.role === 'owner' ? el('button', { type: 'button', class: 'danger', 'data-ban': x.message_id, onclick: act(() => applySanction(x.sender_id, sender, 'ban', x.message_id)) }, 'Ban sender...') : null)));
      }
    }
    await load();
  }

  async function viewSanctions(root) {
    const list = el('div', { id: 'sanctionlist' }), out = el('p', { id: 'sanctionsout', class: 'muted', role: 'status' });
    const state = el('select', { id: 'sanctionstate', onchange: () => load().catch(show) }, [['active', 'Active now'], ['history', 'Everything']].map(([v, l]) => el('option', { value: v }, l)));
    const show = e => { out.textContent = words(e); out.className = 'err'; };
    root.append(el('p', { class: 'muted' }, 'Mutes, suspensions and bans. To restrict someone, open their record under Players or act on a report. Support can lift mutes and suspensions; only an owner can lift a ban.'), el('div', { class: 'row' }, el('div', {}, el('label', { for: 'sanctionstate' }, 'Show'), state)), out, list);
    async function load() {
      const r = await rpc('admin_list_sanctions', { p_state: state.value }); clear(list);
      list.append(table(['Player', 'Restriction', 'Ends', 'Reason', 'By', 'Status', ''], r.items.map(s => [s.player_name || el('code', {}, s.player_id), badge(s.kind), until(s), s.reason, s.created_by_name || '-', s.active ? 'active' : s.lifted_at ? 'lifted: ' + (s.lift_reason || '') : 'ended',
        s.active ? el('button', { type: 'button', class: 'ghost', 'data-lift': s.id, onclick: async () => { out.textContent = ''; out.className = 'muted'; try { if (await liftSanction(s)) { await load(); out.textContent = 'Lifted.'; out.className = 'ok'; } } catch (e) { show(e); } } }, 'Lift') : ''])));
    }
    await load();
  }

  // ------------------------------------------------------------------ start
  (async function start() {
    if (!cfg.supabaseUrl || !cfg.anonKey) return showSignIn();
    if (!session) return showSignIn();
    try { await afterSignIn(); } catch (e) { keep(null); showSignIn(e.message === 'not_authenticated' ? '' : words(e)); }
  })();
})();
