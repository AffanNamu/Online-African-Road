#!/usr/bin/env python3
"""Repository safety gate (runs in CI). Fails if a tracked file looks like a secret, a licence, or third-party paid art.

The repository is PUBLIC. Secrets belong in GitHub Actions secrets; paid/commercial assets must live in a private repository or submodule,
never here (their licences forbid redistribution). The Supabase anon key and project URL are public by design and are allowed;
a service_role key is not.
"""
import base64, json, re, subprocess, sys

files = subprocess.run(["git", "ls-files", "-z"], capture_output=True, check=True).stdout.decode().split("\0")
files = [f for f in files if f]
problems = []

BAD_NAMES = re.compile(r"(\.ulf$|\.pem$|\.p12$|\.pfx$|\.keystore$|\.jks$|(^|/)\.env($|\.)(?!example)|service[-_]account.*\.json$|(^|/)id_rsa)", re.I)
THIRD_PARTY = re.compile(r"^Assets/(ThirdParty|AssetStore|Plugins/ThirdParty)/")
TEXT_EXT = re.compile(r"\.(cs|json|js|py|sh|yml|yaml|md|txt|sql|asmdef|asset|html|xml|toml|cfg|ini|csproj|jslib)$", re.I)
PATTERNS = [
    (re.compile(r"-----BEGIN (RSA |EC |OPENSSH |DSA )?PRIVATE KEY-----"), "private key"),
    (re.compile(r"\bgh[pousr]_[A-Za-z0-9]{36,}\b"), "GitHub token"),
    (re.compile(r"\bgithub_pat_[A-Za-z0-9_]{50,}\b"), "GitHub fine-grained token"),
    (re.compile(r"\bAKIA[0-9A-Z]{16}\b"), "AWS access key id"),
    (re.compile(r"\bsb_secret_[A-Za-z0-9_-]{20,}\b"), "Supabase secret key"),
    (re.compile(r"postgres(ql)?://[^:\s/@]+:(?!password|postgres|\*{3}|<|\$|\{|%s|xxx|\.\.\.)[^@\s]{6,}@(?!localhost|127\.0\.0\.1|db[:/])"), "database URL with a password"),
    (re.compile(r"<DeveloperData\s+Value=\"|<root>\s*<TimeStamp"), "Unity licence data"),
]
JWT = re.compile(r"eyJ[A-Za-z0-9_-]{10,}\.(eyJ[A-Za-z0-9_-]{10,})\.[A-Za-z0-9_-]{10,}")

for f in files:
    if BAD_NAMES.search(f): problems.append(f"{f}: file name looks like a secret/licence/credential")
    if THIRD_PARTY.search(f): problems.append(f"{f}: third-party asset path is tracked in a public repo (use a private repo or submodule)")
    if re.search(r"(^|/)(Library|Temp|Builds|Logs|obj)/", f): problems.append(f"{f}: build output is tracked")
    if not TEXT_EXT.search(f): continue
    try: text = open(f, encoding="utf-8", errors="ignore").read()
    except OSError: continue
    if f.startswith("tools/check_repo_safety.py"): continue
    for rx, what in PATTERNS:
        if rx.search(text): problems.append(f"{f}: contains a {what}")
    for m in JWT.finditer(text):
        seg = m.group(1) + "=" * (-len(m.group(1)) % 4)
        try: role = json.loads(base64.urlsafe_b64decode(seg)).get("role")
        except Exception: continue
        if role == "service_role": problems.append(f"{f}: contains a Supabase service_role key")

if problems:
    print("REPOSITORY SAFETY CHECK FAILED:"); [print(" - " + p) for p in problems]; sys.exit(1)
print(f"repository safety check passed: {len(files)} tracked files scanned (no secrets, licences, build output or third-party asset paths)")
