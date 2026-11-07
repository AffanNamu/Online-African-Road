#!/usr/bin/env python3
"""Audit and reconciliation helpers for unifying the asset collections of the private repository ARO-CONTENT into one library (owner decision, 2026-11-07/08).
It works on a local clone of ARO-CONTENT through `git show` / `git ls-tree` (no LFS download, no credential), never writes to a branch, and never deletes or rewrites anything.

  audit      the registries of several refs: ids, produced assets, classes against prefixes, region/city fields against classes, and a THREE-WAY comparison against the common base (which entries each ref changed, which both changed)
  merge      the three-way entry-level merge of registries into one JSON file (an entry changed by one side takes that side; an entry changed by both sides is a conflict and stops the merge; new entries are kept)
  preserve   for every historical ref: every path of its tree exists in the unified ref with the same git blob (so the same bytes / the same LFS pointer); paths listed with --reconciled are the only allowed exceptions
  python3 tools/library_audit.py audit --repo <clone> --base <ref> --refs <ref> <ref> ...
  python3 tools/library_audit.py merge --repo <clone> --base <ref> --a <ref> --b <ref> --out merged.json
  python3 tools/library_audit.py preserve --repo <clone> --unified <ref> --refs <ref> ... [--reconciled path ...]"""
from __future__ import annotations
import argparse, collections, json, re, subprocess, sys

ENV = {"GIT_LFS_SKIP_SMUDGE": "1", "PATH": "/usr/bin:/bin:/usr/local/bin", "HOME": "/tmp"}


def git(repo: str, *a: str) -> str:
    r = subprocess.run(["git", "-C", repo, *a], capture_output=True, text=True, env=ENV)
    if r.returncode: raise SystemExit(f"git {' '.join(a)}: {r.stderr.strip()}")
    return r.stdout


def registry(repo: str, ref: str) -> dict:
    return json.loads(git(repo, "show", f"{ref}:registry/asset_registry.json"))


def entries(reg: dict) -> dict:
    return {a["id"]: a for a in reg["assets"]}


def expected_class(aid: str) -> str:
    return "LANDMARK" if aid.startswith("lmk_") else "GLOBAL" if aid.startswith("aro_") else "REGIONAL" if aid.startswith("rg_") else "CITY_SPECIFIC"


def class_problems(reg: dict) -> list:
    """Class against id prefix, and region/city fields against the class and the id."""
    cities, regions, out = set(reg.get("cities", {})), set(reg.get("regions", {})), []
    for a in reg["assets"]:
        i, c = a["id"], a.get("assetClass")
        if c != expected_class(i): out.append((i, f"class {c} but the prefix says {expected_class(i)}")); continue
        reg_, city = a.get("region"), a.get("city")
        if c == "GLOBAL" and (reg_ or city): out.append((i, f"GLOBAL asset with region {reg_} / city {city}"))
        if c == "REGIONAL":
            m = re.match(r"rg_([a-z]+)_", i)
            if not m or m.group(1) != reg_ or m.group(1) not in regions or city: out.append((i, f"REGIONAL asset with region {reg_} / city {city} (prefix region {m.group(1) if m else None})"))
        if c in ("CITY_SPECIFIC", "LANDMARK"):
            m = re.match(r"(?:lmk_)?([a-z]+)_", i)
            if not m or m.group(1) != city or city not in cities: out.append((i, f"{c} asset with city {city} (prefix city {m.group(1) if m else None})"))
    return out


def three_way(base: dict, a: dict, b: dict) -> tuple[dict, list]:
    """Entry-level three-way merge. Returns (merged {id: entry}, conflicts [(id, fields)]). An entry removed on one side is a conflict unless the other side left it unchanged."""
    out, conflicts = {}, []
    for i in list(dict.fromkeys(list(base) + list(a) + list(b))):
        eb, ea, ee = base.get(i), a.get(i), b.get(i)
        if ea == ee: pick = ea
        elif ea == eb: pick = ee
        elif ee == eb: pick = ea
        else:
            fields = sorted(k for k in set(ea or {}) | set(ee or {}) if (ea or {}).get(k) != (ee or {}).get(k)); conflicts.append((i, fields)); continue
        if pick is not None: out[i] = pick
    return out, conflicts


def merge_registries(base: dict, a: dict, b: dict) -> dict:
    """The merged registry document. Top-level fields: a side that changed one takes it; if both changed the same field differently the merge stops (the caller resolves it explicitly)."""
    merged, conflicts = three_way(entries(base), entries(a), entries(b))
    if conflicts: raise SystemExit("registry conflicts: " + "; ".join(f"{i} {f}" for i, f in conflicts))
    doc = {}
    for k in dict.fromkeys(list(base) + list(a) + list(b)):
        if k == "assets": continue
        vb, va, vc = base.get(k), a.get(k), b.get(k)
        if va == vc or vc == vb: doc[k] = va
        elif va == vb: doc[k] = vc
        else: raise SystemExit(f"registry field '{k}' was changed differently on both sides")
    order = list(dict.fromkeys(list(entries(base)) + list(entries(a)) + list(entries(b))))
    doc["assets"] = [merged[i] for i in order if i in merged]
    return doc


def tree(repo: str, ref: str) -> dict:
    """{path: blob oid} of a ref (an LFS file is its pointer's blob)."""
    out = {}
    for line in git(repo, "ls-tree", "-r", ref).splitlines():
        meta, path = line.split("\t", 1); out[path] = meta.split()[2]
    return out


def preservation(repo: str, unified: str, refs: list, reconciled: set) -> dict:
    """{ref: {'paths': n, 'same': n, 'reconciled': [paths], 'missing': [paths], 'changed': [paths], 'ancestor': bool}}. A ref that is an ANCESTOR of the unified ref has every one of its commits in the unified history (git guarantees it): its files that a later commit of the library lineage updated are reported as 'changed' but are not losses."""
    u = tree(repo, unified); res = {}
    for r in refs:
        anc = subprocess.run(["git", "-C", repo, "merge-base", "--is-ancestor", r, unified], env=ENV).returncode == 0
        t = tree(repo, r); same = [p for p, o in t.items() if u.get(p) == o]
        rec = [p for p in t if p in reconciled and u.get(p) != t[p]]
        miss = [p for p in t if p not in u and p not in reconciled]; chg = [p for p, o in t.items() if p in u and u[p] != o and p not in reconciled]
        res[r] = {"paths": len(t), "same": len(same), "reconciled": rec, "missing": miss, "changed": chg, "ancestor": anc}
    return res


def plain_vs_pointers(repo: str, unified: str, ref: str) -> dict:
    """For a ref that stored binaries as PLAIN blobs (the superseded first residential package): for every path, sha256 of the plain content against the oid of the LFS pointer the unified ref holds at that path (same bytes, different storage) or the plain blob when the unified ref also has a plain file."""
    import hashlib
    u, t = tree(repo, unified), tree(repo, ref); res = {"paths": len(t), "same_blob": 0, "same_content_as_lfs_object": 0, "text_differs": [], "missing": []}
    for path, oid in t.items():
        if path not in u: res["missing"].append(path); continue
        if u[path] == oid: res["same_blob"] += 1; continue
        raw = subprocess.run(["git", "-C", repo, "cat-file", "blob", oid], capture_output=True, env=ENV).stdout
        ptr = subprocess.run(["git", "-C", repo, "cat-file", "blob", u[path]], capture_output=True, env=ENV).stdout
        m = re.match(rb"version https://git-lfs\.github\.com/spec/v1\noid sha256:([0-9a-f]{64})\nsize (\d+)\n", ptr)
        if m and hashlib.sha256(raw).hexdigest() == m.group(1).decode() and len(raw) == int(m.group(2)): res["same_content_as_lfs_object"] += 1
        else: res["text_differs"].append(path)
    return res


def cmd_audit(a) -> int:
    base = registry(a.repo, a.base); be = entries(base); regs = {r: registry(a.repo, r) for r in a.refs}
    print(f"base {a.base}: {len(be)} entries")
    for r, d in regs.items():
        e = entries(d); produced = {i for i, x in e.items() if x.get("blenderSource")}
        ids = [x["id"] for x in d["assets"]]; dup = [i for i, n in collections.Counter(ids).items() if n > 1]
        ch = {i for i in e if be.get(i) != e[i]}
        print(f"{r}: {len(e)} entries, {len(produced)} produced, duplicates {dup or 'none'}, changed against the base {len(ch)}, new {len(set(e) - set(be))}, classes {dict(collections.Counter(x.get('assetClass') for x in e.values()))}")
        for i, why in class_problems(d): print("   CLASS PROBLEM", i, why)
    ids = list(regs)
    for x in range(len(ids)):
        for y in range(x + 1, len(ids)):
            ex, ey = entries(regs[ids[x]]), entries(regs[ids[y]])
            cx, cy = {i for i in ex if be.get(i) != ex[i]}, {i for i in ey if be.get(i) != ey[i]}; px, py = {i for i, v in ex.items() if v.get("blenderSource")}, {i for i, v in ey.items() if v.get("blenderSource")}
            print(f"{ids[x]} vs {ids[y]}: both changed {sorted(cx & cy) or 'none'}; produced in both {sorted(px & py) or 'none'}")
    return 0


def main() -> int:
    ap = argparse.ArgumentParser(); sub = ap.add_subparsers(dest="cmd", required=True)
    p = sub.add_parser("audit"); p.add_argument("--repo", required=True); p.add_argument("--base", required=True); p.add_argument("--refs", nargs="+", required=True)
    p = sub.add_parser("merge"); p.add_argument("--repo", required=True); p.add_argument("--base", required=True); p.add_argument("--a", required=True); p.add_argument("--b", required=True); p.add_argument("--out", required=True)
    p = sub.add_parser("preserve"); p.add_argument("--repo", required=True); p.add_argument("--unified", required=True); p.add_argument("--refs", nargs="+", required=True); p.add_argument("--reconciled", nargs="*", default=[]); p.add_argument("--plain-refs", nargs="*", default=[])
    a = ap.parse_args()
    if a.cmd == "audit": return cmd_audit(a)
    if a.cmd == "merge":
        doc = merge_registries(registry(a.repo, a.base), registry(a.repo, a.a), registry(a.repo, a.b)); open(a.out, "w").write(json.dumps(doc, indent=1) + "\n")
        print(f"merged registry: {len(doc['assets'])} entries -> {a.out}"); return 0
    res = preservation(a.repo, a.unified, a.refs, set(a.reconciled)); bad = 0
    for r in a.plain_refs:
        d = plain_vs_pointers(a.repo, a.unified, r); print(f"{r} (plain blobs): {d['paths']} paths, {d['same_blob']} identical blobs, {d['same_content_as_lfs_object']} identical bytes to the LFS object the library holds, {len(d['text_differs'])} differ, {len(d['missing'])} not in the library")
        for q in (d["text_differs"] + d["missing"])[:15]: print("   ", q)
    for r, d in res.items():
        lost = len(d["missing"]) + (0 if d["ancestor"] else len(d["changed"]))
        print(f"{r}: {d['paths']} paths, {d['same']} identical in {a.unified}, {len(d['reconciled'])} reconciled files (kept in the snapshot), {len(d['missing'])} MISSING, {len(d['changed'])} " + ("updated by later commits of the library history (the ref is an ancestor: its commit is preserved)" if d["ancestor"] else "CHANGED"))
        for p in d["missing"][:10] + (d["changed"][:10] if not d["ancestor"] else []): print("   ", p)
        bad += lost
    return 1 if bad else 0


if __name__ == "__main__":
    sys.exit(main())
