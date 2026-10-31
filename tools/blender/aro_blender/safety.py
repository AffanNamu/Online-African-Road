"""A guard for Python the model writes and sends to `execute_python`.

Honest scope: this is a guard against a model being talked into writing hostile code (prompt injection from a reference page or an asset description), NOT a sandbox.
The real isolation is the environment (a container with no secrets, no network listener, files confined to the workspace). The guard is deny-by-default on what can reach
the interpreter, the filesystem, the network or Blender's own persistence; everything that does file work goes through the server's own tools, which confine paths.
Reviewed scripts in the repo (`run_script`) are not subject to it: they are code that went through review.
"""
from __future__ import annotations
import ast

ALLOWED_IMPORTS = {"bpy", "bmesh", "mathutils", "math", "random", "itertools", "functools", "collections", "colorsys", "statistics"}
DENIED_CALLS = {"eval", "exec", "compile", "open", "__import__", "input", "globals", "locals", "vars", "getattr", "setattr", "delattr", "breakpoint", "memoryview"}
# attribute chains that start with one of these are refused (file operators, process control, persistence, registration)
DENIED_PREFIXES = ("bpy.app", "bpy.utils", "bpy.path", "bpy.context.preferences", "bpy.ops.wm", "bpy.ops.script", "bpy.ops.text", "bpy.ops.file", "bpy.ops.render",
                   "bpy.ops.export_", "bpy.ops.import_", "bpy.ops.image.save", "bpy.ops.image.open", "bpy.ops.sound", "bpy.ops.mesh.print3d", "bpy.ops.preferences",
                   "bpy.ops.extensions", "bpy.ops.screen.userpref", "bpy.data.libraries", "bpy.data.texts", "bpy.types", "bpy.props")
DENIED_ATTRS = {"filepath", "filepath_raw", "filename", "directory", "save", "save_render", "pack", "unpack", "load", "write", "register", "unregister",
                "handlers", "timers", "driver_add", "drivers"}


class Rejected(Exception):
    def __init__(self, problems: list[str]):
        super().__init__("; ".join(problems)); self.problems = problems


def _chain(node: ast.AST) -> str | None:
    parts = []
    while isinstance(node, ast.Attribute): parts.append(node.attr); node = node.value
    if isinstance(node, ast.Name): parts.append(node.id); return ".".join(reversed(parts))
    return None


def check(code: str) -> None:
    """Raises Rejected listing every problem, or returns None when the code is acceptable."""
    try: tree = ast.parse(code)
    except SyntaxError as e: raise Rejected([f"syntax error: {e.msg} (line {e.lineno})"]) from None
    bad: list[str] = []
    for n in ast.walk(tree):
        line = getattr(n, "lineno", "?")
        if isinstance(n, ast.Import):
            for a in n.names:
                if a.name.split(".")[0] not in ALLOWED_IMPORTS: bad.append(f"line {line}: import of '{a.name}' is not allowed")
        elif isinstance(n, ast.ImportFrom):
            if n.level or (n.module or "").split(".")[0] not in ALLOWED_IMPORTS: bad.append(f"line {line}: import from '{n.module}' is not allowed")
        elif isinstance(n, ast.Call) and isinstance(n.func, ast.Name) and n.func.id in DENIED_CALLS:
            bad.append(f"line {line}: call to '{n.func.id}' is not allowed")
        elif isinstance(n, ast.Name) and n.id in DENIED_CALLS and not isinstance(n.ctx, ast.Store):
            bad.append(f"line {line}: use of '{n.id}' is not allowed")
        elif isinstance(n, ast.Attribute):
            if n.attr.startswith("__"): bad.append(f"line {line}: access to '{n.attr}' is not allowed")
            if n.attr in DENIED_ATTRS: bad.append(f"line {line}: attribute '{n.attr}' is not allowed (files go through the server's save/export/render tools)")
            c = _chain(n)
            if c and any(c == p or c.startswith(p + ".") or (p.endswith("_") and c.startswith(p)) for p in DENIED_PREFIXES): bad.append(f"line {line}: '{c}' is not allowed")
        elif isinstance(n, ast.Name) and n.id.startswith("__"):
            bad.append(f"line {line}: use of '{n.id}' is not allowed")
        elif isinstance(n, (ast.Global, ast.Nonlocal)):
            bad.append(f"line {line}: global/nonlocal is not allowed")
        elif isinstance(n, ast.Subscript) and isinstance(n.value, ast.Attribute) and n.value.attr == "modules":
            bad.append(f"line {line}: module table access is not allowed")
    if bad: raise Rejected(sorted(set(bad), key=lambda s: int(s.split()[1].rstrip(":")) if s.split()[1].rstrip(":").isdigit() else 0))
