import pytest
from aro_blender import safety


def ok(code): safety.check(code)


def bad(code, *fragments):
    with pytest.raises(safety.Rejected) as e: safety.check(code)
    text = " | ".join(e.value.problems)
    for f in fragments: assert f in text, text


def test_benign_geometry_script_is_accepted():
    ok("import bpy, bmesh, math\nbm = bmesh.new()\nbmesh.ops.create_cube(bm, size=2)\nme = bpy.data.meshes.new('m'); bm.to_mesh(me); bm.free()\no = bpy.data.objects.new('o', me)\nbpy.context.scene.collection.objects.link(o)\nresult = [o.name, math.pi]")
    ok("from mathutils import Vector\nv = Vector((1, 2, 3)) * 2\nfor i in range(3):\n    v.x += i\nresult = list(v)")


@pytest.mark.parametrize("code,frag", [
    ("import os", "import of 'os'"), ("import subprocess", "subprocess"), ("import socket", "socket"), ("import sys", "'sys'"), ("from os import path", "from 'os'"),
    ("open('/etc/passwd')", "'open'"), ("eval('1')", "'eval'"), ("exec('x=1')", "'exec'"), ("compile('1','a','exec')", "'compile'"), ("__import__('os')", "__import__"),
    ("getattr(bpy, 'app')", "'getattr'"), ("setattr(bpy, 'x', 1)", "'setattr'"), ("globals()", "'globals'"),
    ("().__class__.__bases__[0].__subclasses__()", "'__class__'"), ("x = bpy.__dict__", "'__dict__'"),
    ("import bpy\nbpy.ops.wm.save_as_mainfile(filepath='/tmp/x.blend')", "bpy.ops.wm"), ("import bpy\nbpy.ops.export_scene.fbx(filepath='/tmp/x.fbx')", "bpy.ops.export_"),
    ("import bpy\nbpy.ops.import_scene.fbx(filepath='/tmp/x.fbx')", "bpy.ops.import_"), ("import bpy\nbpy.ops.render.render(write_still=True)", "bpy.ops.render"),
    ("import bpy\nbpy.app.handlers.load_post.append(lambda *a: None)", "bpy.app"), ("import bpy\nbpy.app.timers.register(lambda: 1)", "bpy.app"),
    ("import bpy\nbpy.utils.register_class(object)", "bpy.utils"), ("import bpy\nbpy.ops.script.python_file_run(filepath='x.py')", "bpy.ops.script"),
    ("import bpy\nbpy.data.images['a'].filepath = '/tmp/a.png'", "'filepath'"), ("import bpy\nbpy.data.images['a'].save_render('/tmp/a.png')", "'save_render'"),
    ("import bpy\nbpy.data.libraries.load('/tmp/a.blend')", "bpy.data.libraries"), ("import bpy\nbpy.data.texts.new('t')", "bpy.data.texts"),
    ("import bpy\nbpy.context.preferences.filepaths.temporary_directory = '/'", "bpy.context.preferences"), ("import importlib", "importlib"),
    ("def f():\n    global x\n    x = 1", "global"), ("import sys\nsys.modules['os']", "'sys'"),
])
def test_hostile_code_is_rejected(code, frag): bad(code, frag)


def test_every_problem_is_reported_not_just_the_first():
    with pytest.raises(safety.Rejected) as e: safety.check("import os\nopen('x')\neval('1')")
    assert len(e.value.problems) >= 3


def test_syntax_errors_are_reported_not_raised_raw():
    bad("def (", "syntax error")
