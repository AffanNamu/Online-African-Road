"""Generates the environmental asset-library handover (docs/ENVIRONMENT_LIBRARY_HANDOVER.md) from the facts: the private asset registry (class, library category, region, references, paths of the Blender source and
the exports, statuses) and the Blender validation reports of the workspace. Nothing in the table is typed by hand, so a status cannot drift from what exists. It states, for every asset, whether it passed the Blender
validation and whether it has been imported into Unity or verified in the game (it has not, unless the registry says so).
Run from tools/blender:  PYTHONPATH=. ~/.aro/venv/bin/python -m env_kit.handover --registry <ARO-CONTENT>/registry/asset_registry.json --out ../../docs/ENVIRONMENT_LIBRARY_HANDOVER.md"""
from __future__ import annotations
import argparse, json, sys, time
from pathlib import Path


def reference_cell(e: dict) -> str:
    refs = ", ".join(e.get("concepts") or [])
    if refs: return refs
    return "owner request; no reference (M-01)" if e.get("ownerRequest") else "none yet"


def unity_cell(e: dict) -> str:
    u = e.get("unity") or {}
    if u.get("inGameVerified"): return "verified in the game"
    if u.get("imported"): return "imported; not verified in the game"
    return "not imported"


def validation_cell(e: dict, report: dict | None) -> str:
    if e.get("status") not in ("blender_validation_passed", "unity_import_verified", "in_game_visual_review_passed", "production_approved"): return e.get("status", "?")
    if report is not None and not report.get("passed"): return "FAILED"
    return "Blender validation passed"


def render(entries: list[dict], reports: dict[str, dict], categories: dict, regions: dict, generated: str) -> str:
    rows = []
    for e in sorted(entries, key=lambda x: (x.get("library") or "", x["id"])):
        rep = reports.get(e["id"]); lods = e.get("lodTriangles") or []
        cat = (categories.get(e.get("library") or "") or {}).get("title", e.get("library") or "-")
        region = regions.get(e.get("region") or "", "any region") if e.get("assetClass") != "GLOBAL" else "any (GLOBAL)"
        blend = f"`{e['blenderSource']}`" if e.get("blenderSource") else "not built"
        exp = f"`{e['export']}` + `.glb`" if e.get("export") else "not exported"
        rows.append(f"| `{e['id']}` | {e.get('assetClass', '-')} | {cat} | {region} | {reference_cell(e)} | {blend} | {exp} | {validation_cell(e, rep)} | {unity_cell(e)} | {'/'.join(str(t) for t in lods) or '-'} | {len(e.get('materials') or [])} |")
    head = ("| ID | Class | Library category | Intended region | Source reference | Blender file (private repository) | Export | Validation | Unity | Triangles per LOD | Materials |\n"
            "|---|---|---|---|---|---|---|---|---|---|---|")
    n_pass = sum(1 for e in entries if e.get("status") in ("blender_validation_passed", "unity_import_verified", "in_game_visual_review_passed", "production_approved"))
    n_unity = sum(1 for e in entries if (e.get("unity") or {}).get("imported")); n_game = sum(1 for e in entries if (e.get("unity") or {}).get("inGameVerified")); n_app = sum(1 for e in entries if e.get("status") == "production_approved")
    unity_sentence = ("None is imported into Unity or verified in the game, and none is approved" if n_unity == 0 and n_game == 0 and n_app == 0 else
                      f"{n_unity} have been imported through the real Unity importer (the import proof: LOD triangles and material count equal the Blender validation); {n_game} have been verified in the game by the owner's review and {n_app} are approved")
    return f"""# Environmental asset library: handover

**Generated {generated} from the private asset registry and the Blender validation reports by `tools/blender/env_kit/handover.py`; nothing below is typed by hand.** {len(entries)} environmental assets; {n_pass} passed the Blender validation. **{unity_sentence}**: the three proofs are separate (Blender validation, Unity import, in-game visual review by a person) and none is approval. The status vocabulary is `needs_concept`, `concept_approved`, `blender_validation_passed`, `unity_import_verified`, `in_game_visual_review_passed`, `production_approved`; the last needs a person.

**Where the files are.** The Blender sources, exports and previews are in the private repository `ARO-CONTENT`, branch `assets/environment-kit-v1` (Git LFS pointers; **the binaries are not on GitHub yet: the environment's network policy blocks `lfs.github.com`**, see `INFRASTRUCTURE_INVESTIGATION_2026-11-02.md` section 5). The code that produces them, the tests and the frozen hash manifests are in this repository (`tools/blender/env_kit`, `docs/kano/ENVIRONMENT_KIT_*.freeze.json`).

**How to read the columns.** *Class*: GLOBAL (any city), REGIONAL (northern Nigerian roadside, village and compound forms; not to be reused outside the region without a decision). *Source reference*: the registered design references (generated concept images, private, never used as textures); **where a reference is a generated image it is a design target, not evidence of how Kano looks**. *Materials*: the number of kit materials, i.e. of draw calls one LOD0 instance costs today, because the game batches nothing. A Blender validation proves the toolchain's rules (budgets, LOD order, ground pivot, names, UVs, closed correctly wound shells); it says nothing about Unity or the look in the game.

{head}
{chr(10).join(rows)}

## What is not claimed
* **Unity import is not an in-game review.** Where the table says "imported; not verified in the game", the asset went through the real Unity importer and was placed in review scenes that were measured and photographed (`ENVIRONMENT_SCENES.md`, `PERFORMANCE_REPORT_V003.md`); whether it looks right in the game is for the owner to judge from those pictures, and nothing is approved. An asset that says "not imported" has not been through Unity at all.
* The vernacular forms (houses, shops, stalls, kiosk, market shed, fence) are **not verified against real references** (M-14); the Hausa-style compounds and frontage have **no real reference at all** (M-01) and are design interpretations of the documented qualitative features; seasonal vegetation looks are **not established** (M-07, M-17).
* Mud, flooding, rainy-season and seasonal behaviour **do not exist in the game**: the road, water and vegetation assets are geometry only. Nothing here reads, changes or depends on the driving physics, road generation, weather system or vehicle behaviour; the engine proposals RB-18 to RB-25 are not started and need a separate technical proposal and approval.
* Each entry's complete list of open visual and factual differences is in the registry field `flags` (after `OPEN:`).
"""


def main() -> int:
    ap = argparse.ArgumentParser(); ap.add_argument("--registry", required=True); ap.add_argument("--out", required=True); ap.add_argument("--workspace", default="")
    a = ap.parse_args()
    from aro_blender import workspace as ws
    from . import build_env as BE
    reg = json.loads(Path(a.registry).read_text()); by_id = {x["id"]: x for x in reg["assets"]}
    missing = [i for i in BE.SPECS if i not in by_id]
    if missing: print("not in the registry:", missing); return 1
    entries = [by_id[i] for i in BE.SPECS]; W = Path(a.workspace) if a.workspace else ws.default_root(); reports = {}
    for i in BE.SPECS:
        vs = sorted(p.stem for p in (W / "validation" / i).glob("v*.json")) if (W / "validation" / i).is_dir() else []
        if vs: reports[i] = json.loads((W / "validation" / i / f"{vs[-1]}.json").read_text())
    text = render(entries, reports, reg.get("libraryCategories", {}), reg.get("regions", {}), time.strftime("%Y-%m-%d", time.gmtime()))
    Path(a.out).write_text(text); print(f"wrote {a.out}: {len(entries)} assets"); return 0


if __name__ == "__main__":
    sys.exit(main())
