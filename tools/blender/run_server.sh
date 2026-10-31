#!/usr/bin/env bash
# Starts the ARO Blender MCP server (stdio). Used by .mcp.json; nothing listens on a network port.
# Python with the packages from requirements.txt: $ARO_BLENDER_PYTHON, else ~/.aro/venv (made by tools/blender/setup.sh).
set -euo pipefail
here="$(cd "$(dirname "$0")" && pwd)"
py="${ARO_BLENDER_PYTHON:-$HOME/.aro/venv/bin/python}"
[ -x "$py" ] || { echo "aro-blender: no Python at $py: run tools/blender/setup.sh first" >&2; exit 1; }
export PYTHONPATH="$here${PYTHONPATH:+:$PYTHONPATH}"
exec "$py" -m aro_blender.server
