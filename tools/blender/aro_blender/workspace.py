"""The ARO Blender workspace: its layout, path confinement, and versioned asset folders.

Rules this module enforces for every tool (nothing else in the toolchain opens a path of its own):
  * every path a tool reads or writes resolves (symlinks included) to a place inside the workspace root;
  * a saved project or an export always goes into a NEW version folder (v001, v002, ...): nothing is overwritten;
  * a version that has been approved (approval.json present) can never be written to again.
"""
from __future__ import annotations
import json, os, re, time
from pathlib import Path

TREE = ["projects/buildings", "projects/vehicles", "projects/environments", "projects/roads", "projects/props", "projects/vegetation", "projects/signs", "projects/lights", "references", "scripts", "materials", "textures",
        "renders", "exports/unity", "exports/fbx", "exports/glb", "validation", "backups"]
KINDS = ("buildings", "vehicles", "environments", "roads", "props", "vegetation", "signs", "lights")
NAME_RE = re.compile(r"^[a-z][a-z0-9_]{1,40}$")        # asset folder / file stem: lower snake case
VERSION_RE = re.compile(r"^v(\d{3})$")


class WorkspaceError(Exception):
    """A request the workspace refuses (outside the root, bad name, approved version, ...). The message is safe to show to the model."""


def default_root() -> Path:
    return Path(os.environ.get("ARO_WORKSPACE") or Path.home() / "ARO-BLENDER-WORKSPACE").expanduser().resolve()


def ensure(root: Path) -> Path:
    for d in TREE: (root / d).mkdir(parents=True, exist_ok=True)
    return root


def confine(path: str | os.PathLike, root: Path) -> Path:
    """The resolved path, or WorkspaceError when it is not inside the workspace root. Relative paths are taken relative to the root."""
    p = Path(path)
    if not p.is_absolute(): p = root / p
    r = p.resolve()
    try: r.relative_to(root.resolve())
    except ValueError: raise WorkspaceError(f"path is outside the workspace: {path}") from None
    return r


def check_name(kind: str, name: str) -> None:
    if kind not in KINDS: raise WorkspaceError(f"unknown asset kind '{kind}': use one of {', '.join(KINDS)}")
    if not NAME_RE.match(name or ""): raise WorkspaceError(f"asset name '{name}' must be lower snake case, 2 to 41 characters (for example 'house_a_flat_roof')")


def versions(root: Path, kind: str, name: str) -> list[int]:
    base = root / "projects" / kind / name
    if not base.is_dir(): return []
    return sorted(int(m.group(1)) for d in base.iterdir() if d.is_dir() and (m := VERSION_RE.match(d.name)))


def is_approved(root: Path, kind: str, name: str, version: int) -> bool:
    return (root / "projects" / kind / name / f"v{version:03d}" / "approval.json").is_file()


def new_version_dir(root: Path, kind: str, name: str, floor: int | None = None) -> tuple[int, Path]:
    """Creates the next version folder and returns (number, path). Never reuses a number. `floor`: the lowest number acceptable (a builder that knows the design version of what it builds)."""
    check_name(kind, name)
    v = (max(versions(root, kind, name), default=0)) + 1
    env_floor = int(os.environ.get("ARO_VERSION_FLOOR") or 0)         # optional: the first version folder of an asset in a FRESH workspace gets this number (rebuilding a historical version with its historical label: docs/AGENT_HANDOVER.md)
    v = max(v, env_floor, int(floor or 0))
    d = root / "projects" / kind / name / f"v{v:03d}"
    d.mkdir(parents=True, exist_ok=False)
    return v, d


def existing_version_dir(root: Path, kind: str, name: str, version: int | None) -> tuple[int, Path]:
    check_name(kind, name)
    vs = versions(root, kind, name)
    if not vs: raise WorkspaceError(f"{kind}/{name} has no saved version yet: save_project first")
    v = version if version is not None else vs[-1]
    if v not in vs: raise WorkspaceError(f"{kind}/{name} has no version v{v:03d} (has: {', '.join(f'v{x:03d}' for x in vs)})")
    return v, root / "projects" / kind / name / f"v{v:03d}"


def unique_path(p: Path) -> Path:
    """A path that does not exist yet: `p` itself, else `<stem>_2<suffix>`, `_3`, ... Two previews rendered in the same second must not overwrite each other."""
    if not p.exists(): return p
    n = 2
    while (q := p.with_name(f"{p.stem}_{n}{p.suffix}")).exists(): n += 1
    return q


def export_dir(root: Path, fmt: str, name: str, version: int) -> Path:
    if fmt not in ("fbx", "glb", "unity"): raise WorkspaceError(f"unknown export format '{fmt}'")
    d = root / "exports" / fmt / name / f"v{version:03d}"
    if d.exists() and any(d.iterdir()): raise WorkspaceError(f"{d.relative_to(root)} already holds an export: exports are never overwritten; save a new project version")
    d.mkdir(parents=True, exist_ok=True)
    return d


def approve(root: Path, kind: str, name: str, version: int, by: str, note: str) -> Path:
    """Records a human approval. After this the version is frozen (every write into it is refused)."""
    v, d = existing_version_dir(root, kind, name, version)
    if not (by or "").strip(): raise WorkspaceError("approval needs the name of the person who approved it")
    f = d / "approval.json"
    if f.exists(): raise WorkspaceError("this version is already approved")
    f.write_text(json.dumps({"approved_by": by.strip(), "note": (note or "").strip(), "at": time.strftime("%Y-%m-%dT%H:%M:%SZ", time.gmtime())}, indent=2))
    for p in d.rglob("*"):
        if p.is_file(): p.chmod(0o444)
    return f
