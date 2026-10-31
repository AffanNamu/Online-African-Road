#!/usr/bin/env python3
"""Runs a list of MCP tool calls in ONE server session (so the scene persists between them) and prints each result; images are saved next to the workspace's renders.
Usage: python tools/blender/mcp_batch.py steps.json   where steps.json is  [{"tool": "create_primitive", "args": {...}}, ...]
Starts the server exactly as .mcp.json does (run_server.sh), so it exercises the same path an MCP-capable agent uses."""
from __future__ import annotations
import asyncio, json, os, sys
from pathlib import Path
from mcp import ClientSession
from mcp.client.stdio import StdioServerParameters, stdio_client

HERE = Path(__file__).resolve().parent


async def run(steps: list[dict]) -> int:
    params = StdioServerParameters(command="bash", args=[str(HERE / "run_server.sh")], env=dict(os.environ))
    code = 0
    async with stdio_client(params) as (rd, wr):
        async with ClientSession(rd, wr) as s:
            await s.initialize()
            for i, st in enumerate(steps, 1):
                r = await s.call_tool(st["tool"], st.get("args", {}))
                txt = " ".join(c.text for c in r.content if c.type == "text")
                imgs = sum(1 for c in r.content if c.type == "image")
                print(f"[{i}] {st['tool']} {'ERROR' if r.isError else 'ok'}{' +%d image' % imgs if imgs else ''}\n{txt[:6000]}\n")
                if r.isError: code = 1
    return code


if __name__ == "__main__":
    sys.exit(asyncio.run(run(json.loads(Path(sys.argv[1]).read_text()))))
