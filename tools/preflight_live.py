#!/usr/bin/env python3
"""READ-ONLY preflight of the live Supabase database before pending migrations are applied.

  * every statement is a SELECT, checked against an allow-list before anything connects (`--selftest` proves the guard rejects writes),
    and runs inside `BEGIN TRANSACTION READ ONLY` (the server itself refuses any write);
  * the connection string, password, user and host are never printed (errors are redacted);
  * the target is confirmed first (project ref of the DB connection == project ref of the game's SUPABASE_URL); if it cannot be
    confirmed NOTHING else runs;
  * every check ends as PASS, FAIL, INFO, or INCOMPLETE / NOT CHECKABLE. An empty result only counts as PASS when its query actually ran.
  * output is aggregates only (counts, hashes): this repository is public and so are its Actions logs.
Usage: DATABASE_URL=... SUPABASE_URL=... [SUPABASE_ANON_KEY=...] python3 tools/preflight_live.py
"""
import glob, json, os, re, subprocess, sys, urllib.error, urllib.request
from urllib.parse import urlparse, unquote

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
MIGRATIONS = os.path.join(ROOT, "supabase", "migrations")
EXPECTED_LIVE = ["20261009000001_core_schema.sql", "20261009000002_game_functions.sql", "20261009000003_server_telemetry.sql",
                 "20261009000004_convoy_sessions.sql", "20261009000005_bus_system.sql", "20261009000006_exclusive_activity.sql"]
# The seventeen migrations the deployment is expected to apply, in this order (reviewed list: a new, unreviewed file makes the check FAIL instead of riding along).
EXPECTED_PENDING = [
    "20261010000001_ledger_kinds.sql",
    "20261010000002_foundation.sql",
    "20261010000003_profile_presence.sql",
    "20261010000004_world_state.sql",
    "20261010000005_social.sql",
    "20261010000006_vehicles_jobs.sql",
    "20261010000007_progression_content_economy.sql",
    "20261011000001_job_board_catalog.sql",
    "20261011000002_convoy_lobby.sql",
    "20261011000003_discovery.sql",
    "20261012000001_vehicle_catalog.sql",
    "20261012000002_ledger_skin_kind.sql",
    "20261012000003_garage.sql",
    "20261012000004_person_vehicle.sql",
    "20261012000005_leaderboard_social.sql",
    "20261012000006_onboarding.sql",
    "20261012000007_nearby_opt_in.sql",
]
NEW_CATEGORIES = ("truck", "bus", "van", "car", "motorcycle")

# ------------------------------------------------------------------ the read-only guard
FORBIDDEN = re.compile(r"\b(insert|update|delete|truncate|alter|create|drop|grant|revoke|copy|call|do|execute|vacuum|analyze|refresh|lock|listen|notify|"
                       r"set|reset|comment|merge|nextval|setval|pg_advisory\w*|pg_terminate\w*|pg_cancel\w*|dblink\w*|lo_\w+|pg_sleep)\b", re.I)
def assert_read_only(sql):
    s = re.sub(r"--[^\n]*", "", sql).strip().rstrip(";").strip()
    scrubbed = re.sub(r"'(?:[^']|'')*'", "''", s)             # words and ';' inside string literals are data, not commands
    if ";" in scrubbed: raise SystemExit("preflight guard: more than one statement in one check")
    if not re.match(r"(select|with|show)\b", s, re.I): raise SystemExit("preflight guard: a check must start with SELECT / WITH / SHOW")
    m = FORBIDDEN.search(scrubbed)
    if m: raise SystemExit(f"preflight guard: forbidden keyword '{m.group(0)}' in a check")
    return s

def selftest():
    good = ["select 1", "show transaction_read_only", "with a as (select 1) select * from a", "select count(*) from profiles where created_at > now()"]
    bad = ["update profiles set level = 1", "delete from jobs", "insert into x values (1)", "select 1; select 2", "alter table a add column b int", "drop table a",
           "select nextval('s')", "create table a(i int)", "grant select on a to b", "truncate a", "select pg_sleep(1)", "do $$ begin end $$", "set role x", "copy a to stdout"]
    for g in good: assert_read_only(g)
    for b in bad:
        try: assert_read_only(b)
        except SystemExit: continue
        raise SystemExit(f"selftest FAILED: guard accepted: {b}")
    for q in ALL_QUERIES_FOR_SELFTEST(): assert_read_only(q)
    print(f"selftest OK: {len(good)} reads accepted, {len(bad)} writes refused, every preflight query passes the guard")

# ------------------------------------------------------------------ plumbing
class Secret: pass
DB_URL = os.environ.get("DATABASE_URL", "").strip()
def redact(text):
    u = urlparse(DB_URL) if DB_URL else None
    out = text or ""
    for tok in ([DB_URL, unquote(u.password or ""), u.password or "", unquote(u.username or ""), u.hostname or ""] if u else []):
        if tok and len(tok) > 2: out = out.replace(tok, "<redacted>")
    return re.sub(r"postgres(?:ql)?://\S+", "<redacted-url>", out)

def psql(sql, timeout=60):
    """-> (ok, rows). rows = list of tuples of strings."""
    s = assert_read_only(sql)
    script = f"begin transaction read only; set local statement_timeout = '20s'; {s}; commit;"
    try:
        r = subprocess.run(["psql", DB_URL, "-X", "-q", "-A", "-t", "-F", "\x1f", "-v", "ON_ERROR_STOP=1", "-c", script],
                           capture_output=True, text=True, timeout=timeout,
                           env={**os.environ, "PGOPTIONS": "-c default_transaction_read_only=on", "PGCONNECT_TIMEOUT": "15"})   # second, server-side lock on top of BEGIN READ ONLY
    except Exception as e:
        return False, [("error: " + redact(type(e).__name__),)]
    if r.returncode != 0:
        msg = redact((r.stderr or "").strip().splitlines()[0] if (r.stderr or "").strip() else "psql failed")
        return False, [("error: " + msg[:200],)]
    rows = [tuple(l.split("\x1f")) for l in r.stdout.splitlines() if l.strip() not in ("", "BEGIN", "SET", "COMMIT")]
    return True, rows

RESULTS = []        # (id, title, status, detail)
def record(cid, title, status, detail=""):
    RESULTS.append((cid, title, status, detail))
    print(f"[{status:<14}] {cid} {title}" + (f"\n                 {detail}" if detail else ""), flush=True)

def ref_of_supabase_url(u):
    m = re.match(r"https://([a-z0-9]{15,30})\.supabase\.co/?$", (u or "").strip())
    return m.group(1) if m else None

def ref_of_db_url(raw):
    try:
        u = urlparse(raw); host, user = u.hostname or "", unquote(u.username or "")
    except Exception: return None, "unparseable"
    if host.endswith(".pooler.supabase.com") and user.startswith("postgres."): return user.split(".", 1)[1], "pooler user"
    m = re.match(r"db\.([a-z0-9]{15,30})\.supabase\.co$", host)
    if m: return m.group(1), "direct host"
    return None, "not a Supabase connection string"

# ------------------------------------------------------------------ what the pending migrations create / add (parsed from the repo, compared with the live catalogs)
def parse_pending(pending):
    objs = {"tables": set(), "types": set(), "indexes": set(), "functions": set(), "triggers": set(), "columns": set()}
    for name in pending:
        t = open(os.path.join(MIGRATIONS, name), encoding="utf-8").read()
        t = re.sub(r"--[^\n]*", "", t); t = re.sub(r"\$\$.*?\$\$", "<body>", t, flags=re.S)
        for m in re.finditer(r"create table (?:if not exists )?(\w+)", t, re.I): objs["tables"].add(m.group(1))
        for m in re.finditer(r"create type (\w+)", t, re.I): objs["types"].add(m.group(1))
        for m in re.finditer(r"create (?:unique )?index (?:if not exists )?(\w+)", t, re.I): objs["indexes"].add(m.group(1))
        for m in re.finditer(r"create function (\w+)\s*\(", t, re.I): objs["functions"].add(m.group(1))
        for m in re.finditer(r"create trigger (\w+)", t, re.I): objs["triggers"].add(m.group(1))
        for m in re.finditer(r"alter table (\w+)\s+(add column .*?)(?:;|$)", t, re.I | re.S):
            for c in re.finditer(r"add column (\w+)", m.group(2), re.I): objs["columns"].add((m.group(1), c.group(1)))
    return objs

def arr(items): return "array[" + ",".join("'" + i.replace("'", "''") + "'" for i in sorted(items)) + "]::text[]" if items else "array[]::text[]"

# ------------------------------------------------------------------ the queries
Q = {
 "readonly":  "show transaction_read_only",
 "version":   "select split_part(version(), ' ', 2)",
 "history_table": "select to_regclass('public._aro_migrations') is not null",
 "history":   "select name, to_char(applied_at at time zone 'utc', 'YYYY-MM-DD HH24:MI:SS') from public._aro_migrations order by name",
 "summary_fn": "select to_regprocedure('public.get_dashboard_summary()') is not null",
 "other_fns": "select p.proname from pg_proc p where p.pronamespace = 'public'::regnamespace and p.proname in ('get_my_profile','list_my_vehicles','get_world_state','complete_onboarding') order by 1",
 "dup_vehicles": "select count(*), count(distinct player_id), coalesce(max(n), 0) from (select player_id, definition_id, count(*) n from public.vehicle_ownership group by 1, 2 having count(*) > 1) d",
 "dup_slugs": "select count(*) from (select trim(both '-' from regexp_replace(lower(name), '[^a-z0-9]+', '-', 'g')) s from public.cities) x group by s having count(*) > 1",
 "bad_slugs": "select count(*) from public.cities where trim(both '-' from regexp_replace(lower(name), '[^a-z0-9]+', '-', 'g')) !~ '^[a-z0-9-]{2,40}$'",
 "bad_categories": "select count(*) from public.vehicle_definitions where category not in (" + ",".join("'%s'" % c for c in NEW_CATEGORIES) + ")",
 "public_tables": "select count(*) from information_schema.tables where table_schema = 'public' and table_type = 'BASE TABLE'",
 "counts": ("select 'profiles', count(*) from public.profiles union all select 'player_wallets', count(*) from public.player_wallets "
            "union all select 'vehicle_ownership', count(*) from public.vehicle_ownership union all select 'transactions', count(*) from public.transactions "
            "union all select 'jobs', count(*) from public.jobs union all select 'job_assignments', count(*) from public.job_assignments "
            "union all select 'convoys', count(*) from public.convoys union all select 'convoy_members', count(*) from public.convoy_members "
            "union all select 'bus_runs', count(*) from public.bus_runs union all select 'cities', count(*) from public.cities "
            "union all select 'locations', count(*) from public.locations union all select 'vehicle_definitions', count(*) from public.vehicle_definitions "
            "union all select 'auth.users', count(*) from auth.users"),
 "fp_wallets": "select count(*), coalesce(sum(balance), 0), md5(coalesce(string_agg(player_id::text || ':' || balance::text, ',' order by player_id), '')) from public.player_wallets",
 "fp_vehicles": "select count(*), md5(coalesce(string_agg(concat_ws('|', id, player_id, definition_id, fuel_l, damage_pct, odometer_km), ';' order by id), '')) from public.vehicle_ownership",
 "fp_ledger": "select count(*), coalesce(sum(amount), 0), coalesce(max(id), 0), md5(coalesce(string_agg(concat_ws('|', id, player_id, kind, amount, balance_after), ';' order by id), '')) from public.transactions",
 "fp_assignments": "select count(*), md5(coalesce(string_agg(concat_ws('|', id, job_id, player_id, vehicle_id, status), ';' order by id), '')) from public.job_assignments",
 "fp_profiles": "select count(*), md5(coalesce(string_agg(concat_ws('|', id, display_name, level, experience, distance_km, jobs_completed), ';' order by id), '')) from public.profiles",
 "has_country_col": "select count(*) from information_schema.columns where table_schema = 'public' and table_name = 'profiles' and column_name = 'country_code'",
 "country_fp": "select count(*) filter (where country_code is not null), count(distinct country_code), md5(coalesce(string_agg(id::text || ':' || coalesce(country_code, '-'), ',' order by id), '')) from public.profiles",
 "has_nearby_col": "select count(*) from information_schema.columns where table_schema = 'public' and table_name = 'profiles' and column_name = 'appear_nearby'",
 "nearby_counts": "select count(*) filter (where appear_nearby), count(*) from public.profiles",
 "fp_geo": "select (select count(*) from public.countries), (select count(*) from public.cities), (select count(*) from public.locations), md5((select coalesce(string_agg(c::text, ';' order by c.code), '') from public.countries c) || (select coalesce(string_agg(c::text, ';' order by c.id), '') from public.cities c) || (select coalesce(string_agg(l::text, ';' order by l.id), '') from public.locations l))",
 "foreign_tables": "select count(*) from information_schema.tables where table_schema = 'public' and table_type = 'BASE TABLE' and table_name <> all (array['_aro_migrations','countries','cities','locations','vehicle_definitions','profiles','player_wallets','transactions','vehicle_ownership','jobs','job_assignments','convoys','convoy_members','job_telemetry','bus_routes','bus_route_stops','bus_runs','bus_run_stops'])",
}
def ALL_QUERIES_FOR_SELFTEST():
    objs = parse_pending([os.path.basename(f) for f in sorted(glob.glob(os.path.join(MIGRATIONS, "2026101*.sql")))])
    return list(Q.values()) + list(collision_queries(objs).values())

def collision_queries(o):
    cols = "array[" + ",".join("'%s.%s'" % c for c in sorted(o["columns"])) + "]::text[]" if o["columns"] else "array[]::text[]"
    return {
     "tables": f"select count(*) from pg_class c join pg_namespace n on n.oid = c.relnamespace where n.nspname = 'public' and c.relkind in ('r','v','m','S') and c.relname = any({arr(o['tables'])})",
     "types": f"select count(*) from pg_type t join pg_namespace n on n.oid = t.typnamespace where n.nspname = 'public' and t.typname = any({arr(o['types'])})",
     "indexes": f"select count(*) from pg_class c join pg_namespace n on n.oid = c.relnamespace where n.nspname = 'public' and c.relkind = 'i' and c.relname = any({arr(o['indexes'])})",
     "functions": f"select count(*) from pg_proc p where p.pronamespace = 'public'::regnamespace and p.proname = any({arr(o['functions'])})",
     "triggers": f"select count(*) from pg_trigger t where not t.tgisinternal and t.tgname = any({arr(o['triggers'])})",
     "columns": f"select count(*) from information_schema.columns where table_schema = 'public' and (table_name || '.' || column_name) = any({cols})",
    }

# ------------------------------------------------------------------ the run
def main():
    local = "--local-test" in sys.argv
    print("== READ-ONLY preflight of the live database (SELECT only, inside BEGIN TRANSACTION READ ONLY; nothing is changed) ==")
    # --- C0 target
    game_url = os.environ.get("SUPABASE_URL", "").strip(); game_ref = ref_of_supabase_url(game_url)
    if not DB_URL: record("C0", "Target: SUPABASE_DB_URL present", "INCOMPLETE", "the secret is empty"); return finish(stop=True)
    db_ref, how = ref_of_db_url(DB_URL)
    host = (urlparse(DB_URL).hostname or "")
    if local and host in ("localhost", "127.0.0.1", ""): record("C0", "Target (local test only)", "INFO", "local scratch database, target check skipped by --local-test")
    elif not game_ref: record("C0", "Target: the game's project ref", "INCOMPLETE", "SUPABASE_URL is not a https://<ref>.supabase.co address"); return finish(stop=True)
    elif not db_ref: record("C0", "Target: the connection's project ref", "INCOMPLETE", f"cannot read a project ref from the connection string ({how})"); return finish(stop=True)
    elif db_ref != game_ref: record("C0", "Target: connection project == game project", "FAIL", f"MISMATCH: game project ref {game_ref}, connection is for a different project. STOPPED, nothing was queried."); return finish(stop=True)
    else: record("C0", "Target: connection project == game project", "PASS", f"project ref {game_ref} (connection identified by its {how}); SUPABASE_URL source: {os.environ.get('SUPABASE_URL_SOURCE', 'unknown')}")
    # --- C1 read-only + version
    ok, rows = psql(Q["readonly"])
    if not ok: record("C1", "Connect and open a read-only transaction", "INCOMPLETE", rows[0][0]); return finish(stop=True)
    record("C1", "Read-only transaction", "PASS" if rows and rows[0][0] == "on" else "FAIL", f"transaction_read_only={rows[0][0] if rows else '?'}")
    if not (rows and rows[0][0] == "on"): return finish(stop=True)
    ok, rows = psql(Q["version"]); record("C1b", "Postgres version", "INFO" if ok else "INCOMPLETE", rows[0][0])
    # --- C2 migration history
    ok, rows = psql(Q["history_table"])
    applied = []
    if not ok: record("C2", "Migration history", "INCOMPLETE", rows[0][0])
    elif rows[0][0] != "t": record("C2", "Migration history", "FAIL", "table public._aro_migrations does not exist")
    else:
        ok, rows = psql(Q["history"])
        if not ok: record("C2", "Migration history", "INCOMPLETE", rows[0][0])
        else:
            applied = [r[0] for r in rows]
            lines = "; ".join(f"{r[0].split('_')[0]} @ {r[1]}" for r in rows)
            record("C2", "Migration history equals the six migrations the investigation expected", "PASS" if applied == EXPECTED_LIVE else "FAIL",
                   f"{len(applied)} applied: {lines}" + ("" if applied == EXPECTED_LIVE else f"  | expected {len(EXPECTED_LIVE)}, extra={sorted(set(applied) - set(EXPECTED_LIVE))}, missing={sorted(set(EXPECTED_LIVE) - set(applied))}"))
    files = sorted(os.path.basename(f) for f in glob.glob(os.path.join(MIGRATIONS, "*.sql")))
    pending = [f for f in files if f not in applied]
    record("C2b", "Migrations that apply.sh would apply (repo files not recorded)", "INFO" if applied else "INCOMPLETE", f"{len(pending)}: " + ", ".join(p.split('_')[0] for p in pending))
    # --- C2c the migration set and its order (repo files only; no database access)
    after_onboarding = pending.index("20261012000007_nearby_opt_in.sql") > pending.index("20261012000006_onboarding.sql") if {"20261012000007_nearby_opt_in.sql", "20261012000006_onboarding.sql"} <= set(pending) else False
    ordered_ok = bool(applied) and pending == EXPECTED_PENDING and after_onboarding and pending == sorted(pending)
    record("C2c", "Pending migrations are exactly the reviewed seventeen, in filename order, with 20261012000007_nearby_opt_in after 20261012000006_onboarding", "PASS" if ordered_ok else ("INCOMPLETE" if not applied else "FAIL"),
           f"{len(pending)} pending; unexpected={sorted(set(pending) - set(EXPECTED_PENDING))}, missing={sorted(set(EXPECTED_PENDING) - set(pending))}" if not ordered_ok else "17 pending, filename order is apply order, the opt-in migration is last")
    # --- C3 the function behind the error
    ok, rows = psql(Q["summary_fn"])
    exists = (rows[0][0] == "t") if ok else None
    record("C3", "public.get_dashboard_summary() exists", "INFO" if ok else "INCOMPLETE", ("exists" if exists else "DOES NOT EXIST (this is the missing function from the sign-in error)") if ok else rows[0][0])
    ok, rows = psql(Q["other_fns"]); record("C3b", "Other functions the current game calls at sign-in (present of: get_my_profile, list_my_vehicles, get_world_state, complete_onboarding)", "INFO" if ok else "INCOMPLETE", (", ".join(r[0] for r in rows) or "none present") if ok else rows[0][0])
    # --- C3c cross-check through the game's own endpoint (unauthenticated GET: never executes anything)
    key = os.environ.get("SUPABASE_ANON_KEY", "").strip()
    if not key or not game_ref: record("C3c", "Game endpoint cross-check", "NOT CHECKABLE", "no SUPABASE_ANON_KEY / project URL available")
    else:
        try:
            req = urllib.request.Request(f"https://{game_ref}.supabase.co/rest/v1/rpc/get_dashboard_summary", headers={"apikey": key, "Authorization": "Bearer " + key})
            try: resp = urllib.request.urlopen(req, timeout=20); code, body = resp.status, resp.read().decode()[:300]
            except urllib.error.HTTPError as e: code, body = e.code, e.read().decode()[:300]
            pg = (json.loads(body).get("code") if body.startswith("{") else None) or ""
            missing_http = (code == 404 and pg == "PGRST202")
            if exists is None: record("C3c", "Game endpoint cross-check", "INCOMPLETE", f"HTTP {code} {pg}; database answer unavailable")
            elif missing_http == (not exists): record("C3c", "Game endpoint agrees with the database about the function", "PASS", f"HTTP {code} {pg or '-'}  =>  {'missing' if missing_http else 'present'} through the game's own URL")
            else: record("C3c", "Game endpoint agrees with the database about the function", "FAIL", f"HTTP {code} {pg or '-'} but the database says {'exists' if exists else 'missing'}: different project or stale API schema cache")
        except Exception as e: record("C3c", "Game endpoint cross-check", "INCOMPLETE", type(e).__name__)
    # --- C4.. blockers
    ok, rows = psql(Q["dup_vehicles"])
    if not ok: record("C4", "No player owns two copies of one vehicle model (blocks migration 20261012000001)", "INCOMPLETE", rows[0][0])
    else:
        g, p, mx = (int(x) for x in rows[0]); record("C4", "No player owns two copies of one vehicle model (blocks migration 20261012000001)", "PASS" if g == 0 else "FAIL",
            "query ran: 0 duplicate (player, model) groups" if g == 0 else f"{g} duplicate (player, model) groups across {p} players, worst case {mx} copies. The unique index would fail; DECISION NEEDED, nothing is changed automatically")
    ok, rows = psql(Q["dup_slugs"]); record("C5", "No two cities produce the same slug (unique index in 20261010000004)", ("PASS" if not rows else "FAIL") if ok else "INCOMPLETE", ("query ran: 0 collisions" if not rows else f"{len(rows)} colliding slugs") if ok else rows[0][0])
    ok, rows = psql(Q["bad_slugs"]); record("C5b", "Every city's generated slug fits the format check", ("PASS" if rows[0][0] == "0" else "FAIL") if ok else "INCOMPLETE", f"{rows[0][0]} cities fail" if ok else rows[0][0])
    ok, rows = psql(Q["bad_categories"]); record("C5c", "Every vehicle definition's category is allowed by the widened check (20261012000001)", ("PASS" if rows[0][0] == "0" else "FAIL") if ok else "INCOMPLETE", f"{rows[0][0]} outside the list" if ok else rows[0][0])
    ok, rows = psql(Q["foreign_tables"]); record("C5d", "Public schema holds only this game's tables (no other application's)", ("PASS" if rows[0][0] == "0" else "FAIL") if ok else "INCOMPLETE", f"{rows[0][0]} unexpected tables" if ok else rows[0][0])
    # --- C6 drift: nothing the pending migrations create may already exist in the live database
    if applied:
        objs = parse_pending(pending); total = {k: len(v) for k, v in objs.items()}
        bad = []; incomplete = []
        for kind, sql in collision_queries(objs).items():
            ok, rows = psql(sql)
            if not ok: incomplete.append(kind)
            elif rows[0][0] != "0": bad.append(f"{kind}={rows[0][0]}")
        status = "INCOMPLETE" if incomplete else ("FAIL" if bad else "PASS")
        record("C6", "Nothing the pending migrations create or add already exists live (no manual drift)", status,
               f"checked {total['tables']} tables, {total['types']} types, {total['indexes']} indexes, {total['functions']} functions, {total['triggers']} triggers, {total['columns']} added columns; " + (f"collisions: {', '.join(bad)}" if bad else "") + (f" could not run: {incomplete}" if incomplete else "") + ("" if (bad or incomplete) else "0 collisions"))
    else: record("C6", "Drift check", "INCOMPLETE", "migration history unavailable")
    # --- C7 counts and C8 fingerprints (aggregates only)
    ok, rows = psql(Q["counts"]); record("C7", "Row counts", "INFO" if ok else "INCOMPLETE", ", ".join(f"{a}={b}" for a, b in rows) if ok else rows[0][0])
    for cid, key_, label in (("C8a", "fp_wallets", "Wallets (rows, total coins, md5)"), ("C8b", "fp_vehicles", "Vehicles (rows, md5)"), ("C8c", "fp_ledger", "Ledger (rows, sum, max id, md5)"),
                             ("C8d", "fp_assignments", "Job assignments (rows, md5)"), ("C8e", "fp_profiles", "Profiles core columns (rows, md5)")):
        ok, rows = psql(Q[key_]); record(cid, label, "INFO" if ok else "INCOMPLETE", " | ".join(rows[0]) if ok else rows[0][0])
    # country, Players Nearby and map data (aggregates only). The columns arrive with pending migrations, so "not present yet" is reported as such, never as a pass.
    ok, rows = psql(Q["has_country_col"])
    if not ok: record("C7b", "Player countries (profiles.country_code)", "INCOMPLETE", rows[0][0])
    elif rows[0][0] == "0": record("C7b", "Player countries (profiles.country_code)", "INFO", "column not present yet: it is added by pending migration 20261010000003 (no player has a country today)")
    else:
        ok, rows = psql(Q["country_fp"]); record("C7b", "Player countries: players with a country | distinct countries | md5 of (player, country)", "INFO" if ok else "INCOMPLETE", " | ".join(rows[0]) if ok else rows[0][0])
    ok, rows = psql(Q["has_nearby_col"])
    if not ok: record("C7c", "Players Nearby preference (profiles.appear_nearby)", "INCOMPLETE", rows[0][0])
    elif rows[0][0] == "0": record("C7c", "Players Nearby preference (profiles.appear_nearby)", "INFO", "column not present yet: it is added by pending migration 20261011000003 and made opt-in by 20261012000007")
    else:
        ok, rows = psql(Q["nearby_counts"]); record("C7c", "Players Nearby preference: visible | all players", "INFO" if ok else "INCOMPLETE", f"{rows[0][0]} visible of {rows[0][1]}" if ok else rows[0][0])
    ok, rows = psql(Q["fp_geo"]); record("C8f", "Map data (countries, cities, locations, md5); the pending migrations legitimately add to it", "INFO" if ok else "INCOMPLETE", " | ".join(rows[0]) if ok else rows[0][0])
    # --- C9 what no query can show
    record("C9", "Backup / point-in-time recovery available", "NOT CHECKABLE", "backups are a Supabase platform feature, not visible from inside the database; check Dashboard > Database > Backups")
    finish()

def finish(stop=False):
    print("\n== SUMMARY ==")
    order = {"PASS": 0, "FAIL": 1, "INCOMPLETE": 2, "NOT CHECKABLE": 3, "INFO": 4}
    for cid, title, status, _ in RESULTS: print(f"  {status:<14} {cid}  {title}")
    f = [r for r in RESULTS if r[2] == "FAIL"]; i = [r for r in RESULTS if r[2] == "INCOMPLETE"]
    verdict = "STOPPED: target not confirmed or connection failed, nothing else was run" if stop else ("NOT SAFE: " + ", ".join(r[0] for r in f) + " failed" if f else ("INCOMPLETE: " + ", ".join(r[0] for r in i) if i else "every executed check passed (informational values above; backups cannot be checked from here)"))
    print("\nVERDICT:", verdict)
    sys.exit(0 if (not f and not i and not stop) else 1)

if __name__ == "__main__":
    if "--selftest" in sys.argv: selftest(); sys.exit(0)
    main()
