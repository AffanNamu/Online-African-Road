import hashlib, json, sys
from pathlib import Path
sys.path.insert(0, str(Path(__file__).parent))
import verify_asset_clone as V


def ptr(oid, size): return f"version https://git-lfs.github.com/spec/v1\noid sha256:{oid}\nsize {size}\n".encode()


def test_pointer_parsing():
    oid = "a" * 64
    assert V.parse_pointer(ptr(oid, 12)) == (oid, 12)
    assert V.parse_pointer(b"not a pointer") is None and V.parse_pointer(ptr(oid, 12) + b"extra") is None


def test_tree_check_flags_missing_pointer_size_and_hash_problems(tmp_path):
    good = b"hello"; oid = hashlib.sha256(good).hexdigest()
    (tmp_path / "ok.bin").write_bytes(good); (tmp_path / "still.bin").write_bytes(ptr(oid, 5)); (tmp_path / "short.bin").write_bytes(b"hell"); (tmp_path / "wrong.bin").write_bytes(b"HELLO")
    pointers = {"ok.bin": (oid, 5), "missing.bin": (oid, 5), "still.bin": (oid, 5), "short.bin": (oid, 5), "wrong.bin": (oid, 5)}
    bad, ok = V.check_tree(tmp_path, pointers)
    assert ok == ["ok.bin"] and {b[0] for b in bad} == {"missing.bin", "still.bin", "short.bin", "wrong.bin"}
    assert dict(bad)["still.bin"].startswith("still a pointer")


def test_frozen_hashes_collects_every_file(tmp_path):
    f = tmp_path / "a.freeze.json"; f.write_text(json.dumps({"assets": [{"files": [{"path": "x.fbx", "sha256": "1" * 64}, {"path": "y.png", "sha256": "2" * 64}]}, {"files": [{"path": "z.png", "sha256": "2" * 64}]}]}))
    h = V.frozen_hashes([f]); assert set(h) == {"1" * 64, "2" * 64} and h["2" * 64] == ["y.png", "z.png"]


def test_hashes_cover_plain_text_files_and_skip_git(tmp_path):
    (tmp_path / "blender" / "validation").mkdir(parents=True); (tmp_path / ".git").mkdir()
    (tmp_path / "blender" / "validation" / "v002.json").write_bytes(b'{"ok": true}'); (tmp_path / "a.glb").write_bytes(b"binary")
    (tmp_path / ".git" / "config").write_bytes(b"secret-looking")
    got = V.hashes_in_tree(tmp_path)
    assert hashlib.sha256(b'{"ok": true}').hexdigest() in got and hashlib.sha256(b"binary").hexdigest() in got
    assert hashlib.sha256(b"secret-looking").hexdigest() not in got
