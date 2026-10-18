#!/usr/bin/env python3
"""Proves the repository safety gate actually catches what it claims to (and lets the public anon key through)."""
import base64, json, os, subprocess, sys, tempfile

GATE = os.path.abspath(os.path.join(os.path.dirname(__file__), "check_repo_safety.py"))

def jwt(role):
    b = lambda d: base64.urlsafe_b64encode(json.dumps(d).encode()).decode().rstrip("=")
    return f"{b({'alg':'HS256','typ':'JWT'})}.{b({'role':role,'ref':'abcdefghij'})}.{'s'*30}"

def run(files):
    with tempfile.TemporaryDirectory() as d:
        subprocess.run(["git", "init", "-q"], cwd=d, check=True)
        for name, content in files.items():
            p = os.path.join(d, name); os.makedirs(os.path.dirname(p), exist_ok=True); open(p, "w").write(content)
        subprocess.run(["git", "add", "-A"], cwd=d, check=True)
        r = subprocess.run([sys.executable, GATE], cwd=d, capture_output=True, text=True)
        return r.returncode, r.stdout

cases = [
    ("clean repo passes", {"a.cs": "class A {}"}, 0),
    ("public anon key is allowed", {"cfg.json": json.dumps({"anon": jwt("anon")})}, 0),
    ("service_role key is rejected", {"cfg.json": json.dumps({"k": jwt("service_role")})}, 1),
    ("private key is rejected", {"k.md": "-----BEGIN RSA PRIVATE KEY-----\nabc"}, 1),
    ("github token is rejected", {"t.js": "const t='ghp_" + "a" * 36 + "'"}, 1),
    ("database url with a password is rejected", {"d.py": "u='postgresql://admin:Sup3rS3cretPw@db.example.supabase.co:5432/postgres'"}, 1),
    ("database url with a placeholder is allowed", {"d.py": "u='postgresql://postgres:postgres@localhost:5432/postgres'"}, 0),
    ("unity licence file is rejected", {"Unity_lic.ulf": "x"}, 1),
    ("unity licence content is rejected", {"l.txt": '<root><TimeStamp Value="x"/><DeveloperData Value="AAAA"/></root>'}, 1),
    (".env is rejected", {".env": "A=1"}, 1),
    (".env.example is allowed", {".env.example": "A="}, 0),
    ("third party asset path is rejected", {"Assets/ThirdParty/Vendor/car.fbx": "x"}, 1),
    ("build output is rejected", {"Builds/WebGL/index.html": "x"}, 1),
    ("a tracked glb model is rejected", {"Assets/_Project/Art/Vehicles/truck.glb": "x"}, 1),
    ("a tracked fbx model is rejected", {"Assets/Anything/car.FBX": "x"}, 1),
    ("a tracked texture source is rejected", {"Assets/_Project/Art/Textures/paint.psd": "x"}, 1),
    ("a generated vehicle prefab is rejected", {"Assets/_Project/Resources/Vehicles/aro_titan_480.prefab": "x"}, 1),
    ("a generated prop material is rejected", {"Assets/_Project/Resources/Props/kiosk.mat": "x"}, 1),
    ("the prop catalogue (json) is allowed", {"Assets/_Project/Resources/Props/prop_catalog.json": "{}"}, 0),
    ("an allowlisted model is allowed", {"tools/asset_allowlist.txt": "# owner-original\nAssets/_Project/Art/Vehicles/fixture.glb\n", "Assets/_Project/Art/Vehicles/fixture.glb": "x"}, 0),
    ("the allowlist is exact: another model next to it is still rejected", {"tools/asset_allowlist.txt": "Assets/_Project/Art/Vehicles/fixture.glb\n", "Assets/_Project/Art/Vehicles/other.glb": "x"}, 1),
    ("an empty allowlist with only comments allows nothing", {"tools/asset_allowlist.txt": "# nothing\n", "x/model.obj": "x"}, 1),
]
bad = 0
for name, files, want in cases:
    code, out = run(files)
    ok = code == want; bad += not ok
    print(("PASS " if ok else "FAIL ") + name + ("" if ok else f" (exit {code}, wanted {want})\n" + out))
sys.exit(1 if bad else 0)
