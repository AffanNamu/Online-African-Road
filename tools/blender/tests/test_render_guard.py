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
