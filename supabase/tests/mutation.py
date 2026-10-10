#!/usr/bin/env python3
"""Mutation test for the security suite: break one protection at a time and require the tests to FAIL.
A mutant that survives (tests still pass) means a protection is not actually covered by a test."""
import os, re, shutil, subprocess, sys, tempfile
ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
MIG = os.path.join(ROOT, "migrations")
NAMES = {1: "20261009000001_core_schema.sql", 2: "20261009000002_game_functions.sql", 3: "20261009000003_server_telemetry.sql", 4: "20261009000004_convoy_sessions.sql", 5: "20261009000005_bus_system.sql", 6: "20261009000006_exclusive_activity.sql"}
F = {k: open(os.path.join(MIG, v)).read() for k, v in NAMES.items()}

def sub(txt, old, new, count=1):
    assert old in txt, "mutation anchor not found: " + old[:70]
    return txt.replace(old, new, count)

def resub(txt, pat, new):
    out, n = re.subn(pat, new, txt, count=1, flags=re.S)
    assert n == 1, "regex anchor not found: " + pat[:70]
    return out

MUTANTS = {
 # ---- grants / RLS (file 1)
 "wallet UPDATE re-granted to clients":        (1, lambda t: t + "\ngrant update on player_wallets to authenticated;\n"),
 "profiles UPDATE re-granted (self-award XP)": (1, lambda t: t + "\ngrant update on profiles to authenticated;\n"),
 "job_assignments UPDATE re-granted":          (1, lambda t: t + "\ngrant update on job_assignments to authenticated;\n"),
 "jobs UPDATE re-granted (edit reward)":       (1, lambda t: t + "\ngrant update on jobs to authenticated;\n"),
 "convoy_members INSERT re-granted":           (1, lambda t: t + "\ngrant insert on convoy_members to authenticated;\n"),
 "RLS: wallet visible to everyone":            (1, lambda t: sub(t, "using (player_id = auth.uid());\ncreate policy \"own transactions\"", "using (true);\ncreate policy \"own transactions\"")),
 "RLS: vehicles visible to everyone":          (1, lambda t: sub(t, 'create policy "own vehicles" on vehicle_ownership for select to authenticated using (player_id = auth.uid())', 'create policy "own vehicles" on vehicle_ownership for select to authenticated using (true)')),
 "anon can read jobs":                         (1, lambda t: t + "\ngrant select on jobs to anon;\n"),
 "RLS disabled on transactions":               (1, lambda t: sub(t, "alter table transactions       enable row level security;", "")),
 "negative balance allowed (both CHECKs dropped)": (1, lambda t: sub(sub(t, "balance     bigint not null default 0 check (balance >= 0)", "balance     bigint not null default 0"), "balance_after bigint not null check (balance_after >= 0)", "balance_after bigint not null")),
 "wallet CHECK dropped alone (defence in depth: expected to SURVIVE)": (1, lambda t: sub(t, "balance     bigint not null default 0 check (balance >= 0)", "balance     bigint not null default 0")),
 # ---- economy / jobs / convoys (file 2)
 "_apply_transaction executable by clients":   (2, lambda t: t + "\ngrant execute on function _apply_transaction(uuid, transaction_kind, bigint, text, text, uuid) to authenticated;\n"),
 "generate_jobs executable by clients":        (2, lambda t: t + "\ngrant execute on function generate_jobs(int) to authenticated;\n"),
 "search_path unpinned on buy_fuel":           (2, lambda t: sub(t, "create or replace function buy_fuel(p_vehicle uuid, p_liters numeric) returns jsonb\nlanguage plpgsql security definer set search_path = public as $$", "create or replace function buy_fuel(p_vehicle uuid, p_liters numeric) returns jsonb\nlanguage plpgsql security definer as $$")),
 "ownership check removed in accept_job":      (6, lambda t: sub(t, "select * into v_veh from vehicle_ownership where id = p_vehicle and player_id = v_uid;", "select * into v_veh from vehicle_ownership where id = p_vehicle;")),
 "idempotency lookup removed":                 (2, lambda t: sub(sub(t, "if exists (select 1 from transactions where idempotency_key = p_key) then\n    return false;\n  end if;", ""), "exception when unique_violation then\n  return false;", "exception when no_data_found then\n  return false;")),
 "job_full check removed":                     (6, lambda t: sub(t, ">= v_job.max_participants then", ">= 999 then")),
 "expiry check removed":                       (6, lambda t: sub(t, "or v_job.expires_at <= now() then", "then")),
 "category check removed":                     (6, lambda t: sub(t, "if v_cat <> v_job.required_category then raise exception 'wrong_vehicle_category'; end if;", "")),
 "convoy size cap raised":                     (2, lambda t: sub(t, ">= 8 then raise exception 'convoy_full'", ">= 80 then raise exception 'convoy_full'")),
 "disbanded convoy joinable":                  (2, lambda t: sub(t, "where id = p_convoy and disbanded_at is null", "where id = p_convoy")),
 "fuel ownership check removed":               (2, lambda t: sub(t, "select * into v_veh from vehicle_ownership where id = p_vehicle and player_id = v_uid for update;\n  if not found then raise exception 'vehicle_not_owned'; end if;\n  select fuel_capacity_l", "select * into v_veh from vehicle_ownership where id = p_vehicle for update;\n  if not found then raise exception 'vehicle_not_owned'; end if;\n  select fuel_capacity_l")),
 "fuel row lock removed":                      (2, lambda t: sub(t, "where id = p_vehicle and player_id = v_uid for update;\n  if not found then raise exception 'vehicle_not_owned'; end if;\n  select fuel_capacity_l", "where id = p_vehicle and player_id = v_uid;\n  if not found then raise exception 'vehicle_not_owned'; end if;\n  select fuel_capacity_l")),
 "repair row lock removed":                    (2, lambda t: sub(t, "where id = p_vehicle and player_id = v_uid for update;\n  if not found then raise exception 'vehicle_not_owned'; end if;\n  if v_veh.damage_pct <= 0", "where id = p_vehicle and player_id = v_uid;\n  if not found then raise exception 'vehicle_not_owned'; end if;\n  if v_veh.damage_pct <= 0")),
 # ---- telemetry (file 3)
 "telemetry: speed limit removed":             (3, lambda t: sub(t, "if dist > allowed then", "if false then")),
 "telemetry: speed slack x100":                (3, lambda t: sub(t, "(v_max * 1.3 / 3.6) * dt + 15", "(v_max * 130 / 3.6) * dt + 15")),
 "telemetry: rate limit removed":              (3, lambda t: sub(t, "if dt < 0.5 then", "if false then")),
 "telemetry: flag threshold disabled":         (3, lambda t: sub(t, "flagged = (rejected + 1 >= 10)", "flagged = false")),
 "telemetry: flagged samples still accepted":  (3, lambda t: sub(t, "if t.flagged then return", "if false then return")),
 "telemetry: owner check removed (submit)":    (3, lambda t: sub(t, "select * into a from job_assignments where id = p_assignment and player_id = auth.uid();", "select * into a from job_assignments where id = p_assignment;")),
 "telemetry: samples accepted after completion": (3, lambda t: sub(t, "  if a.status <> 'in_progress' then raise exception 'invalid_state'; end if;\n  select * into t", "  select * into t")),
 "telemetry: search_path unpinned (submit)":   (3, lambda t: sub(t, "returns jsonb\nlanguage plpgsql security definer set search_path = public as $$\ndeclare\n  t job_telemetry;", "returns jsonb\nlanguage plpgsql security definer as $$\ndeclare\n  t job_telemetry;")),
 "start: pickup radius removed":               (3, lambda t: sub(t, "> 120 then raise exception 'not_at_pickup'", "> 1e9 then raise exception 'not_at_pickup'")),
 "start: owner check removed":                 (3, lambda t: sub(t, "where id = p_assignment and player_id = auth.uid() for update;", "where id = p_assignment for update;")),
 "complete: time check removed":               (3, lambda t: sub(t, "if v_elapsed < v_min_seconds then", "if false then")),
 "complete: distance floor removed":           (3, lambda t: sub(t, "v_t.verified_km < v_job.distance_km * 0.8 or", "false or")),
 "complete: distance ceiling removed":         (3, lambda t: sub(t, "or v_t.verified_km > v_job.distance_km * 5 then", "or false then")),
 "complete: position radius widened":          (3, lambda t: sub(t, "power(v_t.last_z - v_dest.world_z, 2)) > 150", "power(v_t.last_z - v_dest.world_z, 2)) > 1e9")),
 "complete: stale-telemetry check removed":    (3, lambda t: sub(t, "if extract(epoch from (now() - v_t.last_at)) > 60 then raise exception 'telemetry_stale'; end if;", "")),
 "complete: flagged check removed":            (3, lambda t: sub(t, "if v_t.flagged then raise exception 'telemetry_flagged'; end if;", "")),
 "complete: damage can be reduced":            (3, lambda t: sub(t, "if p_damage_pct < v_veh.damage_pct or p_damage_pct > 100", "if p_damage_pct > 100")),
 "complete: damage rise uncapped":             (3, lambda t: sub(t, "\n     or p_damage_pct > v_veh.damage_pct + v_t.verified_km * 3 then", " then")),
 "complete: fuel burn not applied":            (3, lambda t: sub(t, "v_fuel := greatest(0, v_veh.fuel_l - v_t.verified_km * v_burn);", "v_fuel := v_veh.fuel_l;")),
 "complete: owner check removed":              (3, lambda t: sub(t, "where id = p_assignment and player_id = v_uid for update;", "where id = p_assignment for update;")),
 "complete: completed-state guards removed":   (3, lambda t: sub(sub(t, "if v_a.status = 'completed' then raise exception 'already_completed'; end if;", ""), "if v_a.status <> 'in_progress' then raise exception 'invalid_state'; end if;", "")),
 "complete: shared bonus uncapped":            (3, lambda t: sub(t, "least(greatest(v_party - 1, 0) * 10, 30)", "(greatest(v_party - 1, 0) * 100)")),
 "grants: telemetry fns left at default EXECUTE": (3, lambda t: resub(t, r"revoke execute on function\s+start_job.*?from public, anon, authenticated;", "")),
 "grants: submit_telemetry to anon":           (3, lambda t: t + "\ngrant execute on function submit_telemetry(uuid, double precision, double precision) to anon;\n"),
 "grants: job_telemetry readable by clients":  (3, lambda t: t + "\ngrant select on job_telemetry to authenticated;\n"),
 "grants: job_telemetry writable by clients":  (3, lambda t: t + "\ngrant update on job_telemetry to authenticated;\n"),
 "convoy: session_code column readable":      (4, lambda t: t + "\ngrant select (session_code) on convoys to authenticated;\n"),
 "convoy: table-wide select restored":        (4, lambda t: sub(t, "revoke select on convoys from authenticated;", "")),
 "convoy: member check removed (get code)":   (4, lambda t: sub(t, "if not exists (select 1 from convoy_members where convoy_id = p_convoy and player_id = auth.uid()) then", "if false then")),
 "convoy: leader check removed (set code)":   (4, lambda t: sub(t, "where id = p_convoy and leader_id = auth.uid() and disbanded_at is null;", "where id = p_convoy and disbanded_at is null;")),
 "convoy: code format constraint removed":    (4, lambda t: sub(t, "alter table convoys add constraint convoy_code_fmt check (session_code is null or session_code ~ '^[A-Za-z0-9]{4,16}$');", "")),
 "convoy: code fns left at default EXECUTE":  (4, lambda t: resub(t, r"revoke execute on function get_convoy_session_code.*?from public, anon, authenticated;", "")),
 "RLS disabled on job_telemetry":              (3, lambda t: sub(t, "alter table job_telemetry enable row level security;", "")),
 # ---- shop + bus system (file 5)
 "shop: not_for_sale check removed":          (5, lambda t: sub(t, "if v_def.price <= 0 then raise exception 'not_for_sale'; end if;", "")),
 "shop: price debited as zero":               (5, lambda t: sub(t, "'VEHICLE_PURCHASE', -v_def.price,", "'VEHICLE_PURCHASE', -1,")),
 "shop: vehicle granted without payment":     (5, lambda t: sub(t, "perform _apply_transaction(v_uid, 'VEHICLE_PURCHASE', -v_def.price, 'buy:' || gen_random_uuid(), 'vehicle_definition', null);", "null;")),
 "shop: starts with empty tank":              (5, lambda t: sub(t, "values (v_uid, v_def.id, v_def.fuel_capacity_l)", "values (v_uid, v_def.id, 0)")),
 "shop: buy_vehicle to anon":                 (5, lambda t: t + "\ngrant execute on function buy_vehicle(text) to anon;\n"),
 "bus start: category check removed":         (5, lambda t: sub(t, "if v_cat <> 'bus' then raise exception 'wrong_vehicle_category'; end if;", "")),
 "bus start: ownership check removed":        (5, lambda t: sub(t, "where id = p_vehicle and player_id = v_uid for update;\n  if not found then raise exception 'vehicle_not_owned'; end if;\n  select category", "where id = p_vehicle for update;\n  if not found then raise exception 'vehicle_not_owned'; end if;\n  select category")),
 "bus start: stop radius removed":            (5, lambda t: sub(t, "> 120 then raise exception 'not_at_stop'", "> 1e9 then raise exception 'not_at_stop'")),
 "bus start: out_of_fuel check removed":      (5, lambda t: sub(t, "if v_veh.fuel_l <= 0 then raise exception 'out_of_fuel'; end if;", "")),
 "bus start: destroyed check removed":        (5, lambda t: sub(t, "if v_veh.damage_pct >= 100 then raise exception 'vehicle_destroyed'; end if;", "")),
 "bus start: job_active check removed":       (5, lambda t: sub(t, "if exists (select 1 from job_assignments where player_id = v_uid and status in ('accepted','in_progress')) then\n    raise exception 'job_active';\n  end if;", "")),
 "bus start: one-active-run index dropped":   (5, lambda t: sub(t, "create unique index one_active_bus_run on bus_runs(player_id) where status = 'active';", "")),
 "bus telemetry: speed limit removed":        (5, lambda t: sub(t, "if dist > allowed then\n    update bus_runs", "if false then\n    update bus_runs")),
 "bus telemetry: rate limit removed":         (5, lambda t: sub(t, "if dt < 0.5 then return jsonb_build_object('accepted', false, 'reason', 'rate_limited'", "if false then return jsonb_build_object('accepted', false, 'reason', 'rate_limited'")),
 "bus telemetry: flag threshold disabled":    (5, lambda t: sub(t, "flagged = (rejected + 1 >= 10)", "flagged = false")),
 "bus telemetry: flagged still accepted":     (5, lambda t: sub(t, "if r.flagged then return", "if false then return")),
 "bus telemetry: owner check removed":        (5, lambda t: sub(t, "select * into r from bus_runs where id = p_run and player_id = auth.uid() for update;\n  if not found then raise exception 'run_not_found'; end if;\n  if r.status <> 'active' then raise exception 'run_not_active'; end if;\n  if r.flagged then return", "select * into r from bus_runs where id = p_run for update;\n  if not found then raise exception 'run_not_found'; end if;\n  if r.status <> 'active' then raise exception 'run_not_active'; end if;\n  if r.flagged then return")),
 "bus telemetry: status guard removed":       (5, lambda t: sub(t, "if r.status <> 'active' then raise exception 'run_not_active'; end if;\n  if r.flagged then return", "if r.flagged then return")),
 "serve: owner check removed":                (5, lambda t: sub(t, "where id = p_run and player_id = v_uid for update;\n  if not found then raise exception 'run_not_found'; end if;\n  if r.status <> 'active' then raise exception 'run_not_active'; end if;\n  if r.flagged then raise", "where id = p_run for update;\n  if not found then raise exception 'run_not_found'; end if;\n  if r.status <> 'active' then raise exception 'run_not_active'; end if;\n  if r.flagged then raise")),
 "serve: status guard removed":               (5, lambda t: sub(t, "if r.status <> 'active' then raise exception 'run_not_active'; end if;\n  if r.flagged then raise", "if r.flagged then raise")),
 "serve: flagged check removed":              (5, lambda t: sub(t, "if r.flagged then raise exception 'telemetry_flagged'; end if;", "")),
 "serve: stale-telemetry check removed":      (5, lambda t: sub(t, "if extract(epoch from (now() - r.last_at)) > 30 then raise exception 'telemetry_stale'; end if;", "")),
 "serve: stop radius removed":                (5, lambda t: sub(t, "> 60 then raise exception 'not_at_stop'", "> 1e9 then raise exception 'not_at_stop'")),
 "serve: too-soon check removed":             (5, lambda t: sub(t, "if v_since < v_min then raise exception 'stop_too_soon'; end if;", "")),
 "serve: seat capacity ignored":              (5, lambda t: sub(t, "least(v_wait, v_def.passenger_capacity - (r.aboard - v_alight))", "v_wait")),
 "serve: fare multiplied":                    (5, lambda t: sub(t, "v_fare := v_alight::bigint * v_route.fare;", "v_fare := v_alight::bigint * v_route.fare * 10;")),
 "serve: fare paid on boarding too":          (5, lambda t: sub(t, "v_fare := v_alight::bigint * v_route.fare;", "v_fare := (v_alight + v_board)::bigint * v_route.fare;")),
 "serve: terminus leaves passengers aboard":  (5, lambda t: sub(t, "v_alight := case when v_last then r.aboard else", "v_alight := case when v_last then 0 else")),
 "serve: fuel burn not applied":              (5, lambda t: sub(t, "fuel_l = greatest(0, fuel_l - r.verified_km * v_burn)", "fuel_l = fuel_l")),
 "serve: distance not credited":              (5, lambda t: sub(t, "odometer_km = odometer_km + r.verified_km where id = v_veh.id;", "odometer_km = odometer_km where id = v_veh.id;")),
 "serve: xp not awarded":                     (5, lambda t: sub(t, "set experience = experience + v_route.xp_reward,", "set experience = experience + 0,")),
 "serve: fare replay key unstable (defence in depth: expected to SURVIVE)":         (5, lambda t: sub(t, "'busstop:' || p_run || ':' || r.next_seq", "'busstop:' || gen_random_uuid()")),
 "serve: row lock removed (PK on bus_run_stops still serialises: expected to SURVIVE)":                 (5, lambda t: sub(t, "where id = p_run and player_id = v_uid for update;\n  if not found then raise exception 'run_not_found'; end if;\n  if r.status <> 'active' then raise exception 'run_not_active'; end if;\n  if r.flagged then raise", "where id = p_run and player_id = v_uid;\n  if not found then raise exception 'run_not_found'; end if;\n  if r.status <> 'active' then raise exception 'run_not_active'; end if;\n  if r.flagged then raise")),
 "serve: row lock AND stop-log PK removed (race 5 must catch)": (5, lambda t: sub(sub(t, "where id = p_run and player_id = v_uid for update;\n  if not found then raise exception 'run_not_found'; end if;\n  if r.status <> 'active' then raise exception 'run_not_active'; end if;\n  if r.flagged then raise", "where id = p_run and player_id = v_uid;\n  if not found then raise exception 'run_not_found'; end if;\n  if r.status <> 'active' then raise exception 'run_not_active'; end if;\n  if r.flagged then raise"), "  primary key (run_id, seq)\n);\n\nalter table bus_routes", ");\n\nalter table bus_routes")),
 "serve: row lock AND fare key removed (PK still holds: expected to SURVIVE)": (5, lambda t: sub(sub(t, "where id = p_run and player_id = v_uid for update;\n  if not found then raise exception 'run_not_found'; end if;\n  if r.status <> 'active' then raise exception 'run_not_active'; end if;\n  if r.flagged then raise", "where id = p_run and player_id = v_uid;\n  if not found then raise exception 'run_not_found'; end if;\n  if r.status <> 'active' then raise exception 'run_not_active'; end if;\n  if r.flagged then raise"), "'busstop:' || p_run || ':' || r.next_seq", "'busstop:' || gen_random_uuid()")),
 "accept_job: bus-run exclusivity removed":     (6, lambda t: sub(t, "if exists (select 1 from bus_runs where player_id = v_uid and status = 'active') then raise exception 'bus_run_active'; end if;", "")),
 "accept_job (6): search_path unpinned":      (6, lambda t: sub(t, "create or replace function accept_job(p_job uuid, p_vehicle uuid) returns uuid\nlanguage plpgsql security definer set search_path = public as $$", "create or replace function accept_job(p_job uuid, p_vehicle uuid) returns uuid\nlanguage plpgsql security definer as $$")),
 "abandon: owner check removed":              (5, lambda t: sub(t, "where id = p_run and player_id = auth.uid() and status = 'active';", "where id = p_run and status = 'active';")),
 "bus tables: runs UPDATE re-granted":        (5, lambda t: t + "\ngrant update on bus_runs to authenticated;\n"),
 "bus tables: route INSERT re-granted":       (5, lambda t: t + "\ngrant insert on bus_routes to authenticated;\n"),
 "bus tables: stops UPDATE re-granted":       (5, lambda t: t + "\ngrant update on bus_route_stops to authenticated;\n"),
 "bus tables: anon can read runs":            (5, lambda t: t + "\ngrant select on bus_runs to anon;\n"),
 "bus RLS: runs visible to everyone":         (5, lambda t: sub(t, 'for select to authenticated using (player_id = auth.uid());\ncreate policy "own bus run stops"', 'for select to authenticated using (true);\ncreate policy "own bus run stops"')),
 "bus RLS: run stops visible to everyone":    (5, lambda t: sub(t, "using (exists (select 1 from bus_runs r where r.id = run_id and r.player_id = auth.uid()));", "using (true);")),
 "bus RLS disabled on bus_runs":              (5, lambda t: sub(t, "alter table bus_runs        enable row level security;", "")),
 "bus fns left at default EXECUTE":           (5, lambda t: resub(t, r"revoke execute on function\s+buy_vehicle.*?from public, anon, authenticated;", "")),
 "bus: serve_stop to anon":                   (5, lambda t: t + "\ngrant execute on function serve_stop(uuid) to anon;\n"),
 "bus: search_path unpinned on serve_stop":   (5, lambda t: sub(t, "create function serve_stop(p_run uuid) returns jsonb\nlanguage plpgsql security definer set search_path = public as $$", "create function serve_stop(p_run uuid) returns jsonb\nlanguage plpgsql security definer as $$")),
}

only = sys.argv[1:]
survivors, killed = [], []
for name, (which, fn) in MUTANTS.items():
    if only and not any(o in name for o in only): continue
    d = tempfile.mkdtemp()
    for k, fname in NAMES.items():
        open(os.path.join(d, fname), "w").write(fn(F[k]) if k == which else F[k])
    env = dict(os.environ, MIGRATIONS_DIR=d)
    r = subprocess.run([os.path.join(ROOT, "tests", "run.sh")], env=env, capture_output=True, text=True)
    shutil.rmtree(d)
    ok = r.returncode != 0
    print(("KILLED   " if ok else "SURVIVED ") + name, flush=True)
    (killed if ok else survivors).append(name)
print(f"\n{len(killed)} killed, {len(survivors)} survived of {len(killed)+len(survivors)}")
for sv in survivors: print("  SURVIVOR:", sv)
expected = [x for x in survivors if 'expected to SURVIVE' not in x]
sys.exit(1 if expected else 0)
