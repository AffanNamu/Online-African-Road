#!/usr/bin/env python3
"""Checks the rules of this asset repository. Usage: python3 tools/validate_repo.py [repo-root]   (exit 1 with one line per violation)
Rules: 1 only the documented top-level layout; 2 every binary extension is tracked by Git LFS (.gitattributes); 3 every unity/Models/<id>/vNNN has <id>.manifest.json whose required keys exist,
whose files exist with the stated SHA-256 and size, and whose version folder matches; 4 versions are contiguous (v001, v002, ...); 5 every asset id has a row in LICENSES.md;
6 no .blend, no reference image and nothing under references/restricted is inside unity/; 7 every reference id in a manifest exists in references/REFERENCE_REGISTER.md when the register exists."""
import hashlib, json, re, sys
from pathlib import Path

TOP = {"blender", "references", "unity", "previews", "manifests", "tools", "schemas", "tests", ".github", ".git", ".gitattributes", ".gitignore", "README.md", "LICENSES.md", ".pytest_cache", "__pycache__"}
BIN = {".blend", ".blend1", ".fbx", ".glb", ".png", ".jpg", ".jpeg", ".webp", ".tga", ".tif", ".tiff", ".exr", ".psd"}
REQUIRED = ["id", "version", "kind", "files", "source", "madeBy", "licence", "references", "budgets"]


def check(root: Path) -> list:
    v = []
    for p in sorted(root.iterdir()):
        if p.name not in TOP: v.append(f"top-level '{p.name}' is not part of the documented layout")
    ga = root / ".gitattributes"; lfs = set()
    if ga.is_file():
        for line in ga.read_text().splitlines():
            m = re.match(r"^\*(\.\w+)\s+.*filter=lfs", line)
            if m: lfs.add(m.group(1))
    else: v.append(".gitattributes is missing")
    for p in root.rglob("*"):
        if ".git" in p.parts or not p.is_file(): continue
        if p.suffix.lower() in BIN and p.suffix.lower() not in lfs: v.append(f"{p.relative_to(root)}: {p.suffix} is not tracked by Git LFS in .gitattributes")
        rel = p.relative_to(root).as_posix()
        if rel.startswith("unity/") and (p.suffix.lower() == ".blend" or rel.startswith("unity/references") or "restricted" in p.parts): v.append(f"{rel}: sources and restricted references never go under unity/")
    lic = (root / "LICENSES.md").read_text() if (root / "LICENSES.md").is_file() else ""
    if not lic: v.append("LICENSES.md is missing")
    reg = (root / "references" / "REFERENCE_REGISTER.md"); regtext = reg.read_text() if reg.is_file() else None
    models = root / "unity" / "Models"
    for adir in sorted(models.iterdir()) if models.is_dir() else []:
        if not adir.is_dir(): continue
        aid = adir.name
        if f"| {aid} |" not in lic and f"| `{aid}` |" not in lic: v.append(f"{aid}: no row in LICENSES.md")
        vers = sorted(d.name for d in adir.iterdir() if d.is_dir())
        if vers != [f"v{i:03d}" for i in range(1, len(vers) + 1)]: v.append(f"{aid}: versions must be v001, v002, ... without gaps (found {vers})")
        for vd in (adir / n for n in vers):
            mf = vd / f"{aid}.manifest.json"
            if not mf.is_file(): v.append(f"{vd.relative_to(root)}: {aid}.manifest.json is missing"); continue
            try: m = json.loads(mf.read_text())
            except ValueError: v.append(f"{mf.relative_to(root)}: not valid JSON"); continue
            for k in REQUIRED:
                if k not in m: v.append(f"{mf.relative_to(root)}: required key '{k}' is missing")
            if m.get("id") != aid or f"v{int(m.get('version', 0)):03d}" != vd.name: v.append(f"{mf.relative_to(root)}: id/version do not match the folder")
            for f in m.get("files", []):
                fp = vd / f["path"]
                if not fp.is_file(): v.append(f"{mf.relative_to(root)}: file {f['path']} is missing"); continue
                b = fp.read_bytes()
                if hashlib.sha256(b).hexdigest() != f["sha256"] or len(b) != f["bytes"]: v.append(f"{mf.relative_to(root)}: {f['path']} does not match its recorded SHA-256/size")
            if regtext is not None:
                for r in m.get("references", []):
                    if r not in regtext: v.append(f"{mf.relative_to(root)}: reference {r} is not in the register")
    return v


if __name__ == "__main__":
    problems = check(Path(sys.argv[1] if len(sys.argv) > 1 else "."))
    print("\n".join(problems) if problems else "asset repository checks passed")
    sys.exit(1 if problems else 0)
