# Instructions for any agent working in this repository

**Start here: `docs/AGENT_HANDOVER.md`.** It is the complete handover for the Kano content work (what is done, where work stopped, what to do next, what to install, how to restore what only existed in the previous container). Then `docs/STATUS.md` for what the game does and does not implement.

## Rules that always apply (full list: section 2 of the handover)
* Do **not** modify live gameplay, Supabase, driving physics, road generation, the weather system, vehicle behaviour or production configuration. No engine work (batching RB-15, rainy-season/flooding/mud RB-18..RB-25) without a written technical proposal **and the owner's explicit approval**. Never describe mud, flooding or seasonal weather physics as implemented.
* Never ask the owner to paste secrets. Unity and Supabase secrets already exist in this repository's Actions settings: do not request or duplicate them.
* Frozen versions (v002, v003, the environmental freezes) are never edited; a correction is a new version. Do not delete v001.
* Never rewrite shared history or force-push. Never weaken, skip or disable a test. Report pass/fail honestly.
* A Blender render or a Unity import is **not** approval. Only the owner sets `production_approved`, after looking at actual in-game pictures. Nothing is approved today.
* Binaries go to the private repository `AffanNamu/ARO-CONTENT` as Git LFS objects only; do not bypass the network policy. This repository holds code, tests, frozen hash manifests and documents only.
* No pull requests unless the owner asks. ARO 3D Studio is a separate project: never touch it.

## Toolchain in one line
`cd tools/blender && PYTHONPATH=. ~/.aro/venv/bin/python -m pytest tests -q` (needs `tools/blender/setup.sh`, Python 3.13, bpy 5.2.3). Unity is only ever run by GitHub Actions (`.github/workflows/blender-unity.yml`, head-commit marker `[kano-review]`). Details: handover sections 6, 9, 15.
