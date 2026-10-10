// End-to-end check of the LIVE Supabase backend through the same HTTP paths the Unity client uses (GoTrue + PostgREST + RLS + RPCs).
// Usage: SUPABASE_URL=https://<ref>.supabase.co SUPABASE_ANON_KEY=<public anon key> node tools/backend-smoke.js
// It creates a throwaway test player, tries to cheat (must be refused), then really delivers one freight job by streaming
// legal GPS samples in real time (about 2-3 minutes), and checks the reward lands exactly once.
const URL_ = (process.env.SUPABASE_URL || '').replace(/\/$/, ''), KEY = process.env.SUPABASE_ANON_KEY || '';
if (!URL_ || !KEY) { console.error('Set SUPABASE_URL and SUPABASE_ANON_KEY'); process.exit(2); }

let token = null, failed = 0, passed = 0;
const sleep = ms => new Promise(r => setTimeout(r, ms));
async function http(method, path, body, { auth = true, anon = false } = {}) {
  const res = await fetch(URL_ + path, {
    method, headers: { apikey: KEY, 'Content-Type': 'application/json', Authorization: 'Bearer ' + (anon || !auth ? KEY : token) },
    body: body === undefined ? undefined : JSON.stringify(body),
  });
  const text = await res.text(); let json; try { json = JSON.parse(text); } catch { json = text; }
  return { status: res.status, json };
}
const rpc = (fn, args = {}) => http('POST', '/rest/v1/rpc/' + fn, args);
const msg = r => (r.json && (r.json.message || r.json.msg || r.json.error_description)) || JSON.stringify(r.json).slice(0, 160);
function check(name, ok, detail = '') { if (ok) { passed++; console.log('PASS  ' + name); } else { failed++; console.log('FAIL  ' + name + (detail ? '  -> ' + detail : '')); } }
const refused = r => r.status === 401 || r.status === 403 || r.status === 404 || r.status === 400;   // anything but success

(async () => {
  // ---- sign up (what GameFlow does)
  const email = `aro.ci.${Date.now()}.${Math.floor(Math.random() * 1e6)}@${process.env.SMOKE_EMAIL_DOMAIN || 'gmail.com'}`   /* Supabase rejects example.com as invalid; with Confirm email OFF nothing is ever sent */, password = 'Ci-' + Math.random().toString(36).slice(2) + 'Aa1!';
  let r = await http('POST', '/auth/v1/signup', { email, password, data: { display_name: 'CI Driver' } }, { auth: false });
  if (r.status >= 400 || !r.json.access_token) {
    console.log(`Sign-up did not return a session (status ${r.status}): ${msg(r)}`);
    console.log('If the message above says the address is invalid, set SMOKE_EMAIL_DOMAIN to a domain with a mail server. If email confirmation is enabled: Supabase > Authentication > Providers > Email > turn OFF "Confirm email" (the game signs players in immediately).');
    process.exit(1);
  }
  token = r.json.access_token; const uid = r.json.user.id;
  check('sign up returns a session', true);

  // ---- the exact reads Unity does after login
  r = await http('GET', `/rest/v1/profiles?id=eq.${uid}`); check('profile created by the signup trigger', r.status === 200 && r.json.length === 1, msg(r));
  r = await http('GET', `/rest/v1/player_wallets?player_id=eq.${uid}`); check('wallet holds the 5000 starter coins', r.status === 200 && r.json[0] && r.json[0].balance === 5000, msg(r));
  r = await http('GET', '/rest/v1/vehicle_ownership?order=acquired_at'); const vehicles = r.json;
  check('starter truck owned (and only own rows visible)', r.status === 200 && vehicles.length === 1 && vehicles[0].definition_id === 'truck_light_01', msg(r));
  r = await http('GET', '/rest/v1/vehicle_definitions?select=id,category,name,cargo_capacity_kg,passenger_capacity,fuel_capacity_l,max_speed_kmh,price');
  check('vehicle catalogue (with price) readable', r.status === 200 && r.json.some(d => d.id === 'bus_city_01' && d.price === 45000), msg(r));
  r = await http('GET', '/rest/v1/bus_routes?order=code&select=id,code,name,fare,xp_reward,stops:bus_route_stops(seq,demand,alight_pct,location:locations(slug,name,world_x,world_z))');
  check('bus routes with embedded stops (BusService query)', r.status === 200 && r.json[0] && r.json[0].stops.length === 3 && r.json[0].stops[0].location.name, msg(r));
  r = await http('GET', '/rest/v1/convoys?disbanded_at=is.null&order=created_at.desc&limit=20&select=id,name,leader_id,leader:profiles!leader_id(display_name),convoy_members(count)');
  check('convoy list (ConvoyService query)', r.status === 200, msg(r));
  r = await http('GET', '/rest/v1/convoys?select=session_code'); check('convoy session codes are not readable', refused(r), `status ${r.status}`);

  // ---- cheating must be refused by the real PostgREST + RLS stack
  r = await http('PATCH', `/rest/v1/player_wallets?player_id=eq.${uid}`, { balance: 99999999 }); check('cannot edit own wallet', refused(r), `status ${r.status}`);
  r = await http('PATCH', `/rest/v1/profiles?id=eq.${uid}`, { experience: 999999, level: 50 }); check('cannot self-award XP', refused(r), `status ${r.status}`);
  r = await http('POST', '/rest/v1/vehicle_ownership', { player_id: uid, definition_id: 'truck_medium_01', fuel_l: 1 }); check('cannot grant self a vehicle', refused(r), `status ${r.status}`);
  r = await rpc('_apply_transaction', { p_player: uid, p_kind: 'BONUS', p_amount: 100000, p_key: 'x' + Date.now() }); check('internal ledger function not callable', refused(r), `status ${r.status}`);
  r = await rpc('generate_jobs', { p_count: 5 }); check('generate_jobs not callable by players', refused(r), `status ${r.status}`);
  r = await http('GET', '/rest/v1/player_wallets', undefined, { anon: true }); check('anonymous cannot read wallets', refused(r), `status ${r.status}`);
  r = await rpc('buy_vehicle', { p_definition: 'bus_city_01' }); check('bus too expensive: insufficient_funds', r.status === 400 && msg(r).includes('insufficient_funds'), msg(r));
  r = await rpc('buy_vehicle', { p_definition: 'truck_light_01' }); check('free starter truck is not for sale', msg(r).includes('not_for_sale'), msg(r));
  r = await rpc('set_display_name', { p_name: 'CI Driver ' + Math.floor(Math.random() * 1e4) }); check('set_display_name works', r.status === 204 || r.status === 200, msg(r));

  // ---- a real delivery, driven in real time
  r = await http('GET', '/rest/v1/jobs?status=eq.open&expires_at=gt.now()&order=created_at.desc&limit=50&select=*,origin:locations!origin_id(slug,name,world_x,world_z),destination:locations!destination_id(slug,name,world_x,world_z)');
  check('job board query (JobService query) returns jobs', r.status === 200 && r.json.length > 0, msg(r));
  const jobs = (r.json || []).filter(j => j.required_category === 'truck').sort((a, b) => a.distance_km - b.distance_km);
  if (!jobs.length) { console.log('No open truck jobs: run "select generate_jobs(20)" (the apply workflow does this).'); failed++; }
  else {
    const job = jobs[0], o = job.origin, d = job.destination, veh = vehicles[0];
    console.log(`delivering ${job.code}: ${o.name} -> ${d.name}, ${job.distance_km} km, reward ${job.reward}`);
    r = await rpc('accept_job', { p_job: job.id, p_vehicle: veh.id }); check('accept_job', r.status === 200, msg(r)); const asg = String(r.json).replace(/"/g, '');
    r = await rpc('complete_job', { p_assignment: asg, p_damage_pct: 0 }); check('completing before loading is refused', refused(r), msg(r));
    r = await rpc('start_job', { p_assignment: asg, p_x: o.world_x + 5000, p_z: o.world_z }); check('cannot start from far away (not_at_pickup)', msg(r).includes('not_at_pickup'), msg(r));
    r = await rpc('start_job', { p_assignment: asg, p_x: o.world_x, p_z: o.world_z }); check('start_job at the pickup', r.status === 204 || r.status === 200, msg(r));
    await sleep(1500);
    r = await rpc('submit_telemetry', { p_assignment: asg, p_x: d.world_x, p_z: d.world_z });
    check('a teleport to the destination is rejected', r.status === 200 && r.json.accepted === false && r.json.reason === 'too_fast', msg(r));
    r = await rpc('complete_job', { p_assignment: asg, p_damage_pct: 0 }); check('and delivery right after a teleport is refused', refused(r), msg(r));

    const dist = Math.hypot(d.world_x - o.world_x, d.world_z - o.world_z), speed = 27.0;   // 97 km/h, under the truck's 110 km/h limit
    const steps = Math.ceil(dist / speed); let accepted = 0, rejected = 0, last = null;
    console.log(`driving ${(dist / 1000).toFixed(2)} km in real time (~${steps} s)...`);
    for (let i = 1; i <= steps; i++) {
      await sleep(1000);
      const f = Math.min(1, (i * speed) / dist);
      r = await rpc('submit_telemetry', { p_assignment: asg, p_x: o.world_x + (d.world_x - o.world_x) * f, p_z: o.world_z + (d.world_z - o.world_z) * f });
      if (r.status === 200 && r.json.accepted) accepted++; else rejected++;
      last = r.json;
    }
    check('legal driving is accepted by the server', rejected <= Math.ceil(steps * 0.05), `${accepted} accepted, ${rejected} rejected, last ${JSON.stringify(last)}`);
    const before = (await http('GET', `/rest/v1/player_wallets?player_id=eq.${uid}`)).json[0].balance;
    r = await rpc('complete_job', { p_assignment: asg, p_damage_pct: 0 });
    check('complete_job pays the reward', r.status === 200 && r.json.reward === job.reward, msg(r));
    const after = (await http('GET', `/rest/v1/player_wallets?player_id=eq.${uid}`)).json[0].balance;
    check('wallet grew by exactly the reward (+ any shared bonus 0)', after - before === job.reward, `${before} -> ${after}`);
    r = await rpc('complete_job', { p_assignment: asg, p_damage_pct: 0 }); check('a second delivery request is refused (no double pay)', refused(r), msg(r));
    const after2 = (await http('GET', `/rest/v1/player_wallets?player_id=eq.${uid}`)).json[0].balance;
    check('wallet unchanged by the replay', after2 === after, `${after} -> ${after2}`);
    r = await http('GET', `/rest/v1/profiles?id=eq.${uid}`); check('XP and jobs_completed advanced', r.json[0].jobs_completed === 1 && r.json[0].experience >= job.xp_reward, JSON.stringify(r.json[0]));
  }

  console.log(`\n${passed} passed, ${failed} failed`);
  console.log(failed ? 'BACKEND SMOKE TEST FAILED' : 'BACKEND SMOKE TEST PASSED');
  process.exit(failed ? 1 : 0);
})().catch(e => { console.error(e); process.exit(2); });
