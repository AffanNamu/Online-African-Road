"""ARO Blender production toolchain: a headless Blender (the `bpy` module) behind an MCP server, plus the reusable pieces assets are built from.
No network listener anywhere: the server speaks MCP over stdio to the process that started it, and every file it touches is confined to the workspace."""
__version__ = "0.1.0"
