#!/usr/bin/env bash
# One-time setup of the ARO Blender toolchain: a Python 3.13 virtual environment with Blender (the official `bpy` wheel, 5.2.3) and the MCP SDK.
# Blender's own downloads are not needed (and may be blocked): `pip` is the only network access. Needs Python 3.13 (the bpy 5.2 wheel is cp313 only).
# Software OpenGL for the Workbench/EEVEE previews (Cycles previews need none):  apt-get install -y libegl1 libgl1 libgl1-mesa-dri
set -euo pipefail
here="$(cd "$(dirname "$0")" && pwd)"
venv="${ARO_VENV:-$HOME/.aro/venv}"
py="$(command -v python3.13 || command -v python3)"
"$py" -c 'import sys; sys.exit(0 if sys.version_info[:2] == (3, 13) else 1)' || { echo "need Python 3.13 (found $("$py" --version))" >&2; exit 1; }
"$py" -m venv "$venv"
"$venv/bin/pip" install --quiet --no-input -r "$here/requirements.txt"
"$venv/bin/python" -c "import bpy; print('Blender', bpy.app.version_string, 'ready in', '$venv')"
