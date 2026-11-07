#!/usr/bin/env python3
"""Verifies, from a FRESH clone, that every Git LFS object of a branch of the private asset repository is really there and intact (owner requirement of 2026-11-03: "verify every object through a fresh clone").
It never reads or prints a credential: git uses the credentials the environment already has.

Steps: clone the branch into an empty temporary directory with the LFS smudge filter off; list the LFS pointers (path, oid, size from the pointer files); `git lfs pull`; for every pointer check that the file now exists,
that its size equals the pointer's size and that its SHA-256 equals the pointer's oid; then, for every file named in the frozen manifests of the game repository (docs/kano/*.freeze.json), check that the clone holds a file (an LFS object or a plain text file)
with the same SHA-256 (the frozen manifest hashes are the independent record of what was exported). Exit code 0 only when every object passes.

  python3 tools/verify_asset_clone.py --repo https://github.com/AffanNamu/ARO-CONTENT.git --branch assets/kano-residential-v3 [--freeze docs/kano/KANO_RESIDENTIAL_v003.freeze.json ...]

Needs the network access recorded in docs/LFS_NETWORK_REQUIREMENTS.md; without it the pull fails and so does this script (it does not work around that)."""
from __future__ import annotations
import argparse, hashlib, json, os, re, subprocess, sys, tempfile
from pathlib import Path

POINTER_RX = re.compile(rb"^version https://git-lfs\.github\.com/spec/v1\noid sha256:([0-9a-f]{64})\nsize (\d+)\n?$")


def parse_pointer(data: bytes):
    """(oid, size) when `data` is exactly a Git LFS v1 pointer file, else None."""
    m = POINTER_RX.match(data)
    return (m.group(1).decode(), int(m.group(2))) if m else None


def sha256(path: Path) -> str:
    h = hashlib.sha256()
    with path.open("rb") as f:
        for chunk in iter(lambda: f.read(1 << 20), b""): h.update(chunk)
    return h.hexdigest()


def frozen_hashes(freeze_files) -> dict:
    """{sha256: [paths in the manifests]} for every file recorded in the given freeze manifests (the BLENDER sources, exports and textures)."""
    out: dict = {}
    for fp in freeze_files:
        d = json.loads(Path(fp).read_text())
        for a in d["assets"]:
            for f in a["files"]: out.setdefault(f["sha256"], []).append(f["path"])
    return out


def hashes_in_tree(root: Path) -> set:
    """SHA-256 of EVERY file of the working tree (the LFS objects after the pull and the plain text files: project.json, validation reports, manifests), not of the .git directory."""
    return {sha256(p) for p in root.rglob("*") if p.is_file() and ".git" not in p.relative_to(root).parts}


def check_tree(root: Path, pointers: dict) -> tuple[list, list]:
    """Compares the working tree with the pointers recorded at clone time ({relative path: (oid, size)}). Returns (failures, ok paths)."""
    bad, ok = [], []
    for rel, (oid, size) in sorted(pointers.items()):
        p = root / rel
        if not p.is_file(): bad.append((rel, "missing")); continue
        if parse_pointer(p.read_bytes()[:300]) is not None: bad.append((rel, "still a pointer (the object was not downloaded)")); continue
        if p.stat().st_size != size: bad.append((rel, f"size {p.stat().st_size} != {size}")); continue
        if sha256(p) != oid: bad.append((rel, "sha256 differs from the pointer's oid")); continue
        ok.append(rel)
    return bad, ok


def main() -> int:
    ap = argparse.ArgumentParser(); ap.add_argument("--repo", required=True); ap.add_argument("--branch", required=True); ap.add_argument("--freeze", nargs="*", default=[]); ap.add_argument("--keep", default="")
    a = ap.parse_args(); tmp = Path(a.keep) if a.keep else Path(tempfile.mkdtemp(prefix="aro-content-verify-")); dest = tmp / "clone"
    env = dict(os.environ, GIT_LFS_SKIP_SMUDGE="1")
    r = subprocess.run(["git", "clone", "--quiet", "--branch", a.branch, "--single-branch", a.repo, str(dest)], env=env)
    if r.returncode: print("clone failed"); return 2
    pointers = {}
    for p in dest.rglob("*"):
        if ".git" in p.parts or not p.is_file() or p.stat().st_size > 300: continue
        t = parse_pointer(p.read_bytes())
        if t: pointers[str(p.relative_to(dest))] = t
    print(f"{a.branch}: {len(pointers)} LFS pointers, {len({o for o, _ in pointers.values()})} distinct objects")
    r = subprocess.run(["git", "lfs", "pull"], cwd=dest)
    if r.returncode: print("git lfs pull FAILED: the objects are not downloadable from this environment (see docs/LFS_NETWORK_REQUIREMENTS.md)"); return 3
    bad, ok = check_tree(dest, pointers); print(f"objects intact: {len(ok)} of {len(pointers)}")
    for rel, why in bad[:20]: print("  FAIL", rel, why)
    missing_frozen = []
    if a.freeze:
        have = hashes_in_tree(dest); fz = frozen_hashes(a.freeze)
        missing_frozen = [(h, ps[0]) for h, ps in fz.items() if h not in have]
        print(f"frozen manifest files found in the clone, byte for byte (SHA-256; .blend sources included): {len(fz) - len(missing_frozen)} of {len(fz)}")
        if missing_frozen: print(f"{len(missing_frozen)} frozen manifest files have no file with the same SHA-256 in the clone")
        for h, p in missing_frozen[:60]: print("  NOT IN THE CLONE:", p, h[:12])
    return 0 if not bad and not missing_frozen else 1


if __name__ == "__main__":
    sys.exit(main())
