import json, os
import pytest
from aro_blender import workspace as ws


@pytest.fixture
def root(tmp_path): return ws.ensure(tmp_path / "w")


def test_layout_is_created(root):
    for d in ws.TREE: assert (root / d).is_dir()


def test_paths_are_confined_to_the_root(root, tmp_path):
    assert ws.confine("renders/a.png", root) == (root / "renders" / "a.png").resolve()
    for evil in ("../outside.txt", "/etc/passwd", "renders/../../outside", str(tmp_path / "other")):
        with pytest.raises(ws.WorkspaceError): ws.confine(evil, root)


def test_a_symlink_cannot_lead_out_of_the_root(root, tmp_path):
    (tmp_path / "secret").mkdir(); os.symlink(tmp_path / "secret", root / "renders" / "link")
    with pytest.raises(ws.WorkspaceError): ws.confine("renders/link/file.txt", root)


@pytest.mark.parametrize("name", ["", "A", "1abc", "has space", "x" * 60, "Upper", "a-b", "../x", "a"])
def test_bad_asset_names_are_refused(name):
    with pytest.raises(ws.WorkspaceError): ws.check_name("props", name)


def test_unknown_kind_is_refused():
    with pytest.raises(ws.WorkspaceError): ws.check_name("spaceships", "ok_name")


def test_versions_count_up_and_never_reuse(root):
    assert ws.new_version_dir(root, "props", "bollard")[0] == 1
    assert ws.new_version_dir(root, "props", "bollard")[0] == 2
    assert ws.versions(root, "props", "bollard") == [1, 2]
    (root / "projects/props/bollard/v002").rmdir()                       # even after a gap the next number is above the highest
    assert ws.new_version_dir(root, "props", "bollard")[0] == 2
    assert ws.new_version_dir(root, "props", "bollard")[0] == 3


def test_exports_never_overwrite(root):
    d = ws.export_dir(root, "fbx", "bollard", 1); (d / "bollard.fbx").write_bytes(b"x")
    with pytest.raises(ws.WorkspaceError): ws.export_dir(root, "fbx", "bollard", 1)
    assert ws.export_dir(root, "fbx", "bollard", 2).is_dir()


def test_approval_freezes_the_version(root):
    v, d = ws.new_version_dir(root, "props", "bollard"); (d / "bollard.blend").write_bytes(b"blend")
    assert not ws.is_approved(root, "props", "bollard", v)
    with pytest.raises(ws.WorkspaceError): ws.approve(root, "props", "bollard", v, "", "")
    ws.approve(root, "props", "bollard", v, "Owner", "looks right")
    assert ws.is_approved(root, "props", "bollard", v) and json.loads((d / "approval.json").read_text())["approved_by"] == "Owner"
    assert not os.access(d / "bollard.blend", os.W_OK) or os.geteuid() == 0     # read-only for everyone but root (root ignores modes; the record is what counts)
    with pytest.raises(ws.WorkspaceError): ws.approve(root, "props", "bollard", v, "Owner", "again")
    assert ws.new_version_dir(root, "props", "bollard")[0] == v + 1       # a change is a new version


def test_missing_versions_are_explained(root):
    with pytest.raises(ws.WorkspaceError): ws.existing_version_dir(root, "props", "none", None)
    ws.new_version_dir(root, "props", "x1")
    with pytest.raises(ws.WorkspaceError): ws.existing_version_dir(root, "props", "x1", 7)


def test_unique_path_never_hands_out_an_existing_file(tmp_path):
    a = tmp_path / "x_iso_cycles_20260101T000000.png"
    assert ws.unique_path(a) == a
    a.write_bytes(b"1")
    b = ws.unique_path(a); assert b.name == "x_iso_cycles_20260101T000000_2.png"
    b.write_bytes(b"2")
    assert ws.unique_path(a).name == "x_iso_cycles_20260101T000000_3.png"
    assert a.read_bytes() == b"1" and b.read_bytes() == b"2"
