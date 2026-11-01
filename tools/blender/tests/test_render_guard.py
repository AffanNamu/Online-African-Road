import pytest
from aro_blender import render


def test_an_opengl_engine_is_refused_cleanly_when_opengl_is_missing(monkeypatch):
    monkeypatch.setenv("ARO_BLENDER_NO_GL", "1")
    assert render.gl_available() is False
    for engine in ("workbench", "eevee"):
        with pytest.raises(ValueError, match="needs OpenGL"): render.preview(None, None, None, "iso", engine)      # refused before Blender is touched: Blender would abort the process


def test_cycles_needs_no_opengl(monkeypatch):
    monkeypatch.setenv("ARO_BLENDER_NO_GL", "1")
    with pytest.raises(AttributeError): render.preview(None, None, None, "iso", "cycles")      # got past the guard (it then fails only because no Blender was given)


def test_unknown_view_and_engine_are_refused():
    with pytest.raises(ValueError, match="unknown view"): render.preview(None, None, None, "sideways")
    with pytest.raises(ValueError, match="unknown engine"): render.preview(None, None, None, "iso", "raytracer")


def test_a_caller_placed_camera_is_validated_before_blender_is_touched():
    loc, tgt, lens = render.parse_camera([0, -45, 1.7], [0, 0, 2.5])
    assert loc == (0.0, -45.0, 1.7) and tgt == (0.0, 0.0, 2.5) and lens == 28.0
    assert render.parse_camera((1, 2, 3), (4, 5, 6), 50)[2] == 50.0
    for bad_loc, bad_tgt, lens, match in (
        (None, [0, 0, 0], 28, "needs both"), ([0, 0, 0], None, 28, "needs both"),
        ([0, 0], [0, 0, 0], 28, "three numbers"), (["a", 0, 0], [0, 0, 0], 28, "three numbers"),
        ([float("nan"), 0, 0], [0, 0, 0], 28, "finite"), ([0, 6000, 0], [0, 0, 0], 28, "within 5 km"),
        ([1, 1, 1], [1, 1, 1], 28, "same point"), ([0, -5, 1], [0, 0, 1], 5, "between 10 and 200"), ([0, -5, 1], [0, 0, 1], 500, "between 10 and 200"),
        ([0, -5, 1], [0, 0, 1], "wide", "must be a number")):
        with pytest.raises(ValueError, match=match): render.parse_camera(bad_loc, bad_tgt, lens)


def test_the_camera_view_is_refused_without_a_camera_and_named_in_the_view_list():
    with pytest.raises(ValueError, match="needs both camera_location and look_at"): render.preview(None, None, None, "camera")
    with pytest.raises(ValueError, match="camera"): render.preview(None, None, None, "sideways")      # the error lists 'camera' among the valid views
