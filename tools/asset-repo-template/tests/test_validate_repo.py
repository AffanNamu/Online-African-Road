import hashlib, json, shutil, sys
from pathlib import Path
import pytest
sys.path.insert(0, str(Path(__file__).resolve().parents[1] / "tools"))
import validate_repo as V

TEMPLATE = Path(__file__).resolve().parents[1]


def make(tmp: Path) -> Path:
    for n in (".gitattributes", "README.md", "LICENSES.md"): shutil.copy(TEMPLATE / n, tmp / n)
    for d in ("blender", "references", "unity", "previews", "manifests", "tools", "schemas"): (tmp / d).mkdir(exist_ok=True)
    (tmp / "references" / "REFERENCE_REGISTER.md").write_text("R-001 R-003")
    return tmp


def add_asset(root: Path, aid="kano_test_a", version=1, refs=("R-001",), licensed=True):
    vd = root / "unity" / "Models" / aid / f"v{version:03d}"; vd.mkdir(parents=True)
    data = b"fbx-bytes"; (vd / f"{aid}.fbx").write_bytes(data)
    m = {"id": aid, "version": version, "kind": "buildings", "files": [{"path": f"{aid}.fbx", "sha256": hashlib.sha256(data).hexdigest(), "bytes": len(data)}],
         "source": {"blend": "x.blend", "script": "s.py", "toolchain": "0.1.0"}, "madeBy": {"human": "H", "tools": ["T"]}, "licence": {"kind": "owner-owned", "evidence": "e"}, "references": list(refs), "budgets": {"lodTriangles": [100, 50]}}
    (vd / f"{aid}.manifest.json").write_text(json.dumps(m))
    if licensed: (root / "LICENSES.md").write_text((root / "LICENSES.md").read_text() + f"\n| {aid} | {version} | H | R-001 | e | P, today |\n")
    return vd


def test_a_clean_repository_passes(tmp_path):
    r = make(tmp_path); add_asset(r); assert V.check(r) == []


@pytest.mark.parametrize("mutate,expect", [
    (lambda r: (r / "stray.txt").write_text("x"), "not part of the documented layout"),
    (lambda r: ((r / "previews" / "a.exr").write_bytes(b"x"), (r / ".gitattributes").write_text("*.png filter=lfs\n")), "not tracked by Git LFS"),
    (lambda r: (r / "unity" / "Models" / "kano_test_a" / "v001" / "kano_test_a.fbx").write_bytes(b"changed"), "does not match its recorded SHA-256"),
    (lambda r: (r / "unity" / "Models" / "kano_test_a" / "v001" / "kano_test_a.manifest.json").unlink(), "manifest.json is missing"),
    (lambda r: (r / "unity" / "x.blend").write_bytes(b"x"), "never go under unity/"),
    (lambda r: (r / "LICENSES.md").write_text("# empty\n"), "no row in LICENSES.md"),
    (lambda r: shutil.move(str(r / "unity" / "Models" / "kano_test_a" / "v001"), str(r / "unity" / "Models" / "kano_test_a" / "v003")), "without gaps"),
])
def test_each_rule_catches_its_violation(tmp_path, mutate, expect):
    r = make(tmp_path); add_asset(r); mutate(r)
    problems = V.check(r); assert any(expect in p for p in problems), problems


def test_an_unknown_reference_id_is_refused(tmp_path):
    r = make(tmp_path); add_asset(r, refs=("R-009",)); assert any("R-009 is not in the register" in p for p in V.check(r))
