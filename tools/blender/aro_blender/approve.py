"""Records a human's approval of one asset version and freezes it: `python -m aro_blender.approve <kind> <name> <version> --by "Name" --note "why"`.
Deliberately a command for a person and not an MCP tool: the agent that builds an asset must not be able to approve it."""
from __future__ import annotations
import argparse
from . import workspace as ws


def main() -> None:
    a = argparse.ArgumentParser(description=__doc__); a.add_argument("kind"); a.add_argument("name"); a.add_argument("version", type=int); a.add_argument("--by", required=True); a.add_argument("--note", default="")
    x = a.parse_args(); root = ws.ensure(ws.default_root()); f = ws.approve(root, x.kind, x.name, x.version, x.by, x.note); print("approved and frozen:", f.parent)


if __name__ == "__main__":
    main()
