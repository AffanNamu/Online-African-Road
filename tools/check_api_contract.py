#!/usr/bin/env python3
"""API contract check: the C# DTOs the Unity client parses with JsonUtility versus the REAL output of the REAL database functions.

For every client-facing RPC it builds a throw-away database (clean install: auth stub + all migrations + seeds), plays a short scenario so that
every list is non-empty, calls the function as a signed-in player, and then verifies against the DTO class the client uses:
  * every field the C# class declares exists in the server's JSON (a rename on either side fails here, not in a player's game);
  * the JSON type fits the C# type (a number with decimals cannot be read into int/long; objects map to classes; arrays to arrays).
Usage: PGHOST=... PGUSER=... PGPASSWORD=... python3 tools/check_api_contract.py
"""
import json, os, re, subprocess, sys

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
SUP = os.path.join(ROOT, "supabase")
DTO_FILES = [os.path.join(ROOT, "Assets/_Project/Scripts/Backend", f) for f in ("ApiDtos.cs", "Models.cs")]
DB = f"aro_contract_{os.getpid()}"

def psql(sql=None, f=None, db=None, check=True):
    cmd = ["psql", "-X", "-q", "-At", "-v", "ON_ERROR_STOP=1", "-d", db or DB] + (["-f", f] if f else ["-c", sql])
    r = subprocess.run(cmd, capture_output=True, text=True, cwd=SUP)
    if check and r.returncode != 0: raise SystemExit(f"psql failed: {r.stderr.strip()[:800]}\nSQL: {(sql or f)[:300]}")
    return r.stdout.strip()

# ------------------------------------------------------------------ DTO parsing (C#)
OPTIONAL = {}; opt_after = {}
def parse_dtos():
    classes = {}
    for path in DTO_FILES:
        src = open(path, encoding="utf-8").read(); src = re.sub(r"// optional:[^\n]*", "@@OPTIONAL@@", src)      # fields after this marker are only sent in some contexts
        src = re.sub(r"//[^\n]*", "", src)
        for m in re.finditer(r"\[Serializable\]\s*public\s+class\s+(\w+)(?:<\w+>)?\s*\{", src):
            name, i, depth, j = m.group(1), m.end(), 1, m.end()
            while depth and j < len(src):
                depth += {"{": 1, "}": -1}.get(src[j], 0); j += 1
            body = src[i:j - 1]
            body = re.sub(r"\{[^{}]*\}", "{}", body)                      # drop property / initialiser bodies
            fields = {}; opt_at = body.find("@@OPTIONAL@@"); optional = set()
            for decl in body.split(";"):
                d = re.sub(r"\s+", " ", decl).strip()
                is_opt = "@@OPTIONAL@@" in d; d = d.replace("@@OPTIONAL@@", "").strip()
                fm = re.match(r"public (?!bool \w+ =>)([\w\[\]<>]+) ([\w, ]+?)(?: = .*)?$", d)
                if not fm or "=>" in d or "(" in d: continue
                for n in fm.group(2).split(","):
                    n = n.strip()
                    if n: fields[n] = fm.group(1); (optional.add(n) if is_opt or (opt_at >= 0 and opt_after.get(name, False)) else None)
                if is_opt: opt_after[name] = True
            classes[name] = fields; OPTIONAL[name] = optional
    return classes

NUM = {"int", "long", "double", "float"}
def check(value, ctype, classes, path, errors):
    if ctype.endswith("[]"):
        if not isinstance(value, list): errors.append(f"{path}: server sent {type(value).__name__}, client expects {ctype}"); return
        for k, v in enumerate(value[:3]): check(v, ctype[:-2], classes, f"{path}[{k}]", errors)
        return
    if ctype in NUM:
        if value is None: return
        if isinstance(value, bool) or not isinstance(value, (int, float)): errors.append(f"{path}: server sent {value!r}, client expects {ctype}"); return
        if ctype in ("int", "long") and isinstance(value, float) and value != int(value): errors.append(f"{path}: {value} has decimals but the client reads {ctype}")
        return
    if ctype == "string":
        if value is not None and not isinstance(value, str): errors.append(f"{path}: server sent {type(value).__name__}, client expects string")
        return
    if ctype == "bool":
        if value is not None and not isinstance(value, bool): errors.append(f"{path}: server sent {type(value).__name__}, client expects bool")
        return
    if ctype in classes:
        if value is None: return                                           # JsonUtility turns a null object into a default instance
        if not isinstance(value, dict): errors.append(f"{path}: server sent {type(value).__name__}, client expects {ctype}"); return
        for f, t in classes[ctype].items():
            if f not in value:
                if f not in OPTIONAL.get(ctype, ()): errors.append(f"{path}.{f}: the client class {ctype} declares it but the server does not send it")
            else: check(value[f], t, classes, f"{path}.{f}", errors)
        return
    errors.append(f"{path}: unknown client type {ctype}")

# ------------------------------------------------------------------ database + scenario
def build_db():
    psql("drop database if exists " + DB, db="postgres"); psql("create database " + DB, db="postgres")
    psql(f="tests/00_auth_stub.sql")
    for f in sorted(os.listdir(os.path.join(SUP, "migrations"))): psql(f="migrations/" + f)
    for f in ("seed.sql", "seed_world.sql", "seed_content.sql", "tests/01_testkit.sql"): psql(f=f)

def uid(name): return psql(f"select id from profiles where display_name = '{name}'")
def as_user(u, sql): return psql(f"select testkit.as_user('{u}', $q${sql}$q$)")

def scenario():
    for n in ("CtA", "CtB", "CtC", "CtD", "CtE", "CtF", "CtG"): psql(f"select testkit.make_user('{n}')")
    A, B, C, D, E, F, G = (uid(n) for n in ("CtA", "CtB", "CtC", "CtD", "CtE", "CtF", "CtG"))
    psql(f"update profiles set experience = 300, level = 2, distance_km = 12, jobs_completed = 2, lifetime_earnings = 900 where id = '{A}'")
    psql("select testkit.mk_job('CT-1', 1, 640)")
    psql("update jobs set expires_at = now() + interval '2 days'")
    for u in (A, B, C, F): as_user(u, "select start_session('webgl')::text")
    # A and B become friends; D asks A; A asks E
    rid = json.loads(as_user(A, f"select send_friend_request('{B}')::text"))["request_id"]
    as_user(B, f"select respond_friend_request('{rid}', true)")
    as_user(D, f"select send_friend_request('{A}')::text"); as_user(A, f"select send_friend_request('{E}')::text")
    # convoy (private) with B inside, C invited
    as_user(A, "select create_convoy('Contract Run')"); as_user(B, f"select join_convoy((select convoy_id from convoy_members where player_id = '{A}'))")
    as_user(A, "select set_convoy_options(4, true)::text"); as_user(A, f"select invite_to_convoy('{C}')")
    # a public convoy for list_open_convoys, with a route and tags so the nested shapes are exercised
    as_user(D, "select create_convoy('Open Convoy')")
    as_user(D, "select set_convoy_route('lagos-ibadan')::text"); as_user(D, "select set_convoy_tags(array['jobs','chill_drive'], 'en')::text")
    # A's convoy: route, tags, B ready, and a little chat (the lines B and A write plus the server's own system lines)
    as_user(A, "select set_convoy_route('lagos-ibadan')::text"); as_user(A, "select set_convoy_tags(array['freight'], 'fr')::text")
    as_user(B, "select set_my_ready(true)::text"); as_user(A, "select post_convoy_message('Ready when you are')::text"); as_user(B, "select post_convoy_message('Let us roll')::text")
    # a password convoy, a blocked player, a nearby crowd on A's route (heartbeats carry the place), and a second heartbeat for B
    as_user(F, "select create_convoy_lobby('Pw Convoy', null, false, 6, 'realistic', 1, 'en', 'pw1234', array['friendly'])")
    as_user(A, f"select block_player('{G}')")
    for u in (B, C, F):
        sid = psql(f"select id from game_sessions where player_id = '{u}' and ended_at is null limit 1")
        as_user(u, f"select heartbeat('{sid}', 'driving', 'lagos-ibadan', 'lagos')::text")
    # A has a job in progress; progress to the 'verified_km' path
    return A, B, C, D, E, F, G

def main():
    dump = sys.argv[sys.argv.index("--dump") + 1] if "--dump" in sys.argv else None      # also write every real reply to <dir>/<label>.json (Unity tests parse them with the real JsonUtility)
    classes = parse_dtos()
    build_db()
    try:
        A, B, C, D, E, F, G = scenario()
        psql("select testkit.mk_job('CT-2', 1, 900)")
        psql("update vehicle_ownership set damage_pct = 12, fuel_l = 60 where player_id = '%s'" % E)
        MSG = psql("select id from convoy_messages where body = 'Let us roll'"); PWC = psql("select id from convoys where name = 'Pw Convoy'")
        SESSION_A = psql("select id from game_sessions where player_id = '%s' limit 1" % A)
        calls = [  # (rpc label, user, sql, client class)
            ("get_dashboard_summary", A, "select get_dashboard_summary()::text", "DashboardSummary"),
            ("get_my_profile", A, "select get_my_profile()::text", "ProfileFull"),
            ("update_my_profile", A, "select update_my_profile(null, null, 'sunset', 'NG')::text", "ProfileFull"),
            ("get_public_profile", A, f"select get_public_profile('{B}')::text", "PublicProfile"),
            ("find_player", A, "select find_player('ctb')::text", "PersonDto"),
            ("get_my_wallet", A, "select get_my_wallet()::text", "WalletInfo"),
            ("get_my_progression", A, "select get_my_progression()::text", "ProgressionDto"),
            ("list_my_achievements", A, "select list_my_achievements()::text", "AchievementList"),
            ("start_session", E, "select start_session('webgl')::text", "SessionStarted"),
            ("heartbeat", A, "select heartbeat('" + SESSION_A + "', 'driving', 'lagos-ibadan', 'lagos')::text", "HeartbeatAck"),
            ("get_my_presence", A, "select get_my_presence()::text", "MyPresence"),
            ("list_friends", A, "select list_friends()::text", "PeopleList"),
            ("list_recent_players", A, "select list_recent_players()::text", "PeopleList"),
            ("list_friend_requests", A, "select list_friend_requests()::text", "FriendRequests"),
            ("send_friend_request", C, f"select send_friend_request('{D}')::text", "FriendSendResult"),
            ("get_my_convoy", A, "select get_my_convoy()::text", "MyConvoy"),
            ("list_open_convoys", A, "select list_open_convoys()::text", "OpenConvoyList"),
            ("list_convoy_messages", A, "select list_convoy_messages()::text", "ChatPage"),
            ("post_convoy_message", A, "select post_convoy_message('One more line')::text", "ChatPosted"),
            ("list_active_players", A, "select list_active_players()::text", "ActivePlayerList"),
            ("get_server_status", A, "select get_server_status()::text", "ServerStatus"),
            ("list_nearby_players", A, "select list_nearby_players()::text", "NearbyList"),
            ("list_servers", A, "select list_servers()::text", "ServerList"),
            ("list_blocked_players", A, "select list_blocked_players()::text", "BlockedList"),
            ("report_chat_message", A, f"select report_chat_message({MSG}, 'spam')::text", "ReportResult"),
            ("join_convoy_with_password", G, f"select join_convoy_with_password('{PWC}', 'nope')::text", "JoinResult"),
            ("set_convoy_settings", A, "select set_convoy_settings('realistic', null, 'fr')::text", "ConvoyInfo"),
            ("set_convoy_route", A, "select set_convoy_route('lagos-ibadan')::text", "ConvoyInfo"),
            ("set_convoy_tags", A, "select set_convoy_tags(array['freight','jobs'], 'fr')::text", "ConvoyInfo"),
            ("set_my_ready", B, "select set_my_ready(true)::text", "ConvoyInfo"),
            ("start_convoy_drive", A, "select start_convoy_drive()::text", "ConvoyInfo"),
            ("end_convoy_drive", A, "select end_convoy_drive()::text", "ConvoyInfo"),
            ("list_convoy_invites", C, "select list_convoy_invites()::text", "ConvoyInviteList"),
            ("set_convoy_options", A, "select set_convoy_options(5, true)::text", "ConvoyInfo"),
            ("list_notifications", A, "select list_notifications()::text", "NotificationList"),
            ("list_my_vehicles", E, "select list_my_vehicles()::text", "VehicleList"),
            ("list_vehicle_catalog", A, "select list_vehicle_catalog()::text", "CatalogList"),
            ("list_vehicle_skins", E, "select list_vehicle_skins((select id from vehicle_ownership where player_id = auth.uid() limit 1))::text", "SkinList"),
            ("get_vehicle_service_quote", E, "select get_vehicle_service_quote((select id from vehicle_ownership where player_id = auth.uid() limit 1))::text", "ServiceQuote"),
            ("get_vehicle_job_fit", A, "select get_vehicle_job_fit('truck_light_01')::text", "JobFitDto"),
            ("get_world_state", A, "select get_world_state()::text", "WorldState"),
            ("list_available_jobs", D, "select list_available_jobs()::text", "JobBoardPage"),
            ("get_job_details", D, "select get_job_details((select id from jobs where code = 'CT-1'))::text", "BoardJob"),
            ("get_content(news)", A, "select get_content('news')::text", "ContentList"),
            ("get_content(hero)", A, "select get_content('hero')::text", "ContentList"),
            ("get_leaderboard", A, "select get_leaderboard('xp')::text", "LeaderboardPage"),
            ("get_friends_leaderboard", A, "select get_friends_leaderboard('xp')::text", "LeaderboardPage"),
            ("search_leaderboard", A, "select search_leaderboard('xp', 'all', 'cta')::text", "LeaderboardPage"),
        ]
        # an active job for the get_my_active_job shape, accepted by user E
        as_user(E, "select accept_job((select id from jobs where code = 'CT-2'), (select id from vehicle_ownership where player_id = auth.uid()))")
        calls.append(("get_my_active_job", E, "select get_my_active_job()::text", "ActiveJobReply"))
        failures, report = [], []
        for label, user, sql, cls in calls:
            raw = as_user(user, sql)
            try: data = json.loads(raw)
            except Exception: failures.append(f"{label}: not JSON: {raw[:120]}"); continue
            errs = []
            check(data, cls, classes, label, errs)
            # lists must have been exercised, otherwise element fields were never compared
            for key, val in (data.items() if isinstance(data, dict) else []):
                if isinstance(val, list) and not val and label not in ("get_my_presence",) and key in ("items", "incoming", "outgoing", "members", "cities", "routes"):
                    errs.append(f"{label}.{key}: the scenario produced an empty list, so its element fields were not checked")
            if dump:
                os.makedirs(dump, exist_ok=True)
                with open(os.path.join(dump, re.sub(r"[^a-z0-9]+", "_", label.lower()).strip("_") + ".json"), "w", encoding="utf-8") as fh: json.dump(data, fh, indent=1, sort_keys=True)
            report.append(f"{'FAIL' if errs else 'ok  '} {label:26s} -> {cls}")
            failures += errs
        print("\n".join(report))
        if failures:
            print("\nAPI CONTRACT FAILED:"); [print("  - " + f) for f in failures]; sys.exit(1)
        print(f"\nAPI CONTRACT OK: {len(calls)} endpoints match their C# DTOs ({sum(len(v) for v in classes.values())} fields in {len(classes)} classes parsed)")
    finally:
        psql("drop database if exists " + DB, db="postgres", check=False)

main()
