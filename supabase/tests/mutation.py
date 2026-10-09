#!/usr/bin/env python3
"""Mutation test for the security suite: break one protection at a time and require the tests to FAIL.
A mutant that survives (tests still pass) means a protection is not actually covered by a test."""
import os, re, shutil, subprocess, sys, tempfile
ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
MIG = os.path.join(ROOT, "migrations")
f1 = open(os.path.join(MIG, "20261009000001_core_schema.sql")).read()
f2 = open(os.path.join(MIG, "20261009000002_game_functions.sql")).read()

def sub(txt, old, new, count=1):
    assert old in txt, "mutation anchor not found: " + old[:60]
    return txt.replace(old, new, count)

MUTANTS = {
 "wallet UPDATE re-granted to clients":        (1, lambda t: t + "\ngrant update on player_wallets to authenticated;\n"),
 "profiles UPDATE re-granted (self-award XP)": (1, lambda t: t + "\ngrant update on profiles to authenticated;\n"),
 "job_assignments UPDATE re-granted":          (1, lambda t: t + "\ngrant update on job_assignments to authenticated;\n"),
 "jobs UPDATE re-granted (edit reward)":       (1, lambda t: t + "\ngrant update on jobs to authenticated;\n"),
 "convoy_members INSERT re-granted":           (1, lambda t: t + "\ngrant insert on convoy_members to authenticated;\n"),
 "RLS: wallet visible to everyone":            (1, lambda t: sub(t, "using (player_id = auth.uid());\ncreate policy \"own transactions\"", "using (true);\ncreate policy \"own transactions\"")),
 "RLS: vehicles visible to everyone":          (1, lambda t: sub(t, 'create policy "own vehicles" on vehicle_ownership for select to authenticated using (player_id = auth.uid())', 'create policy "own vehicles" on vehicle_ownership for select to authenticated using (true)')),
 "anon can read jobs":                         (1, lambda t: t + "\ngrant select on jobs to anon;\n"),
 "RLS disabled on transactions":               (1, lambda t: sub(t, "alter table transactions       enable row level security;", "")),
 "_apply_transaction executable by clients":   (2, lambda t: t + "\ngrant execute on function _apply_transaction(uuid, transaction_kind, bigint, text, text, uuid) to authenticated;\n"),
 "generate_jobs executable by clients":        (2, lambda t: t + "\ngrant execute on function generate_jobs(int) to authenticated;\n"),
 "search_path unpinned on buy_fuel":           (2, lambda t: sub(t, "create or replace function buy_fuel(p_vehicle uuid, p_liters numeric) returns jsonb\nlanguage plpgsql security definer set search_path = public as $$", "create or replace function buy_fuel(p_vehicle uuid, p_liters numeric) returns jsonb\nlanguage plpgsql security definer as $$")),
 "time check removed":                         (2, lambda t: sub(t, "if v_elapsed < v_min_seconds then", "if false then")),
 "distance floor removed":                     (2, lambda t: sub(t, "p_distance_km < v_job.distance_km * 0.8 or", "false or")),
 "distance ceiling removed":                   (2, lambda t: sub(t, "or p_distance_km > v_job.distance_km * 3 then", "or false then")),
 "position radius widened":                    (2, lambda t: sub(t, "power(p_final_z - v_dest.world_z, 2)) > 150", "power(p_final_z - v_dest.world_z, 2)) > 1e9")),
 "fuel sanity removed":                        (2, lambda t: sub(t, "if p_fuel_remaining_l < 0 or p_fuel_remaining_l > v_def.fuel_capacity_l\n     or p_damage_pct < v_veh.damage_pct or p_damage_pct > 100 then", "if false then")),
 "damage can be reduced for free":             (2, lambda t: sub(t, "or p_damage_pct < v_veh.damage_pct or p_damage_pct > 100", "or p_damage_pct > 100")),
 "ownership check removed in accept_job":      (2, lambda t: sub(t, "select * into v_veh from vehicle_ownership where id = p_vehicle and player_id = v_uid;", "select * into v_veh from vehicle_ownership where id = p_vehicle;")),
 "assignment owner check removed (start)":     (2, lambda t: sub(t, "from job_assignments where id = p_assignment and player_id = auth.uid() for update;\n  if not found then raise exception 'assignment_not_found'; end if;\n  if v_a.status <> 'accepted'", "from job_assignments where id = p_assignment for update;\n  if not found then raise exception 'assignment_not_found'; end if;\n  if v_a.status <> 'accepted'")),
 "assignment owner check removed (complete)":  (2, lambda t: sub(t, "select * into v_a from job_assignments where id = p_assignment and player_id = v_uid for update;", "select * into v_a from job_assignments where id = p_assignment for update;")),
 "completed-state guard removed":              (2, lambda t: sub(sub(t, "if v_a.status = 'completed' then raise exception 'already_completed'; end if;", ""), "if v_a.status <> 'in_progress' then raise exception 'invalid_state'; end if;", "")),
 "idempotency lookup removed":                 (2, lambda t: sub(sub(t, "if exists (select 1 from transactions where idempotency_key = p_key) then\n    return false;\n  end if;", ""), "exception when unique_violation then\n  return false;", "exception when no_data_found then\n  return false;")),
 "job_full check removed":                     (2, lambda t: sub(t, ">= v_job.max_participants then", ">= 999 then")),
 "expiry check removed":                       (2, lambda t: sub(t, "or v_job.expires_at <= now() then", "then")),
 "category check removed":                     (2, lambda t: sub(t, "if v_cat <> v_job.required_category then raise exception 'wrong_vehicle_category'; end if;", "")),
 "convoy size cap raised":                     (2, lambda t: sub(t, ">= 8 then raise exception 'convoy_full'", ">= 80 then raise exception 'convoy_full'")),
 "disbanded convoy joinable":                  (2, lambda t: sub(t, "where id = p_convoy and disbanded_at is null", "where id = p_convoy")),
 "fuel ownership check removed":               (2, lambda t: sub(t, "select * into v_veh from vehicle_ownership where id = p_vehicle and player_id = v_uid for update;\n  if not found then raise exception 'vehicle_not_owned'; end if;\n  select fuel_capacity_l", "select * into v_veh from vehicle_ownership where id = p_vehicle for update;\n  if not found then raise exception 'vehicle_not_owned'; end if;\n  select fuel_capacity_l")),
 "fuel row lock removed":                      (2, lambda t: sub(t, "where id = p_vehicle and player_id = v_uid for update;\n  if not found then raise exception 'vehicle_not_owned'; end if;\n  select fuel_capacity_l", "where id = p_vehicle and player_id = v_uid;\n  if not found then raise exception 'vehicle_not_owned'; end if;\n  select fuel_capacity_l")),
 "repair row lock removed":                    (2, lambda t: sub(t, "where id = p_vehicle and player_id = v_uid for update;\n  if not found then raise exception 'vehicle_not_owned'; end if;\n  if v_veh.damage_pct <= 0", "where id = p_vehicle and player_id = v_uid;\n  if not found then raise exception 'vehicle_not_owned'; end if;\n  if v_veh.damage_pct <= 0")),
 "negative balance allowed (both CHECKs dropped)": (1, lambda t: sub(sub(t, "balance     bigint not null default 0 check (balance >= 0)", "balance     bigint not null default 0"), "balance_after bigint not null check (balance_after >= 0)", "balance_after bigint not null")),
 "wallet CHECK dropped alone (defence in depth: expected to SURVIVE)": (1, lambda t: sub(t, "balance     bigint not null default 0 check (balance >= 0)", "balance     bigint not null default 0")),
 "shared bonus uncapped":                      (2, lambda t: sub(t, "least(greatest(v_party - 1, 0) * 10, 30)", "(greatest(v_party - 1, 0) * 100)")),
}

only = sys.argv[1:]
survivors, killed = [], []
for name, (which, fn) in MUTANTS.items():
    if only and not any(o in name for o in only): continue
    d = tempfile.mkdtemp()
    open(os.path.join(d, "20261009000001_core_schema.sql"), "w").write(fn(f1) if which == 1 else f1)
    open(os.path.join(d, "20261009000002_game_functions.sql"), "w").write(fn(f2) if which == 2 else f2)
    env = dict(os.environ, MIGRATIONS_DIR=d)
    r = subprocess.run([os.path.join(ROOT, "tests", "run.sh")], env=env, capture_output=True, text=True)
    shutil.rmtree(d)
    ok = r.returncode != 0
    tail = [l for l in (r.stderr + r.stdout).splitlines() if "ERROR" in l or "FAILED" in l or "NOT REJECTED" in l or "ASSERT" in l or "WRONG" in l]
    print(("KILLED   " if ok else "SURVIVED ") + name + ("" if not ok else ""), flush=True)
    if ok: killed.append((name, tail[-1][:140] if tail else "")); 
    else: survivors.append(name)
print(f"\n{len(killed)} killed, {len(survivors)} survived of {len(killed)+len(survivors)}")
for s in survivors: print("  SURVIVOR:", s)
expected = [x for x in survivors if 'expected to SURVIVE' not in x]
sys.exit(1 if expected else 0)
