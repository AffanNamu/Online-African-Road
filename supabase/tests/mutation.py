#!/usr/bin/env python3
"""Mutation test for the security suite: break one protection at a time and require the tests to FAIL.
A mutant that survives (tests still pass) means a protection is not actually covered by a test."""
import os, re, shutil, subprocess, sys, tempfile
ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
MIG = os.path.join(ROOT, "migrations")
NAMES = {1: "20261009000001_core_schema.sql", 2: "20261009000002_game_functions.sql", 3: "20261009000003_server_telemetry.sql"}
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
 "ownership check removed in accept_job":      (2, lambda t: sub(t, "select * into v_veh from vehicle_ownership where id = p_vehicle and player_id = v_uid;", "select * into v_veh from vehicle_ownership where id = p_vehicle;")),
 "idempotency lookup removed":                 (2, lambda t: sub(sub(t, "if exists (select 1 from transactions where idempotency_key = p_key) then\n    return false;\n  end if;", ""), "exception when unique_violation then\n  return false;", "exception when no_data_found then\n  return false;")),
 "job_full check removed":                     (2, lambda t: sub(t, ">= v_job.max_participants then", ">= 999 then")),
 "expiry check removed":                       (2, lambda t: sub(t, "or v_job.expires_at <= now() then", "then")),
 "category check removed":                     (2, lambda t: sub(t, "if v_cat <> v_job.required_category then raise exception 'wrong_vehicle_category'; end if;", "")),
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
 "RLS disabled on job_telemetry":              (3, lambda t: sub(t, "alter table job_telemetry enable row level security;", "")),
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
