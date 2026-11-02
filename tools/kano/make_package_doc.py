#!/usr/bin/env python3
"""Writes docs/KANO_VERTICAL_SLICE.md (the review package, revision 2) from the generated data files, so its tables cannot drift from the evidence.
Usage: python3 tools/kano/make_package_doc.py [--check]"""
import json, sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]; K = ROOT / "docs" / "kano"
inv = json.loads((K / "kano_asset_inventory.json").read_text()); st = json.loads((K / "kano_slice_stats.json").read_text()); fr = json.loads((K / "kano_world_frame.json").read_text())
wk = json.loads((K / "kano_workarounds.json").read_text()); tx = json.loads((K / "texture_findings.json").read_text()); reg = json.loads((K / "reference_register.json").read_text())
T = st["instanceTotals"]; tiers = st["tiers"]


def inv_row(p):
    lod = "/".join(str(x) for x in p["lodTriangles"])
    return f"| `{p['id']}` | {p['category']} | {p['widthM']:g} x {p['depthM']:g} x {p['heightM']:g} | {lod} | {p['collider']} | {p['cullDistance']:g} | {T.get(p['id'], 0)} | {', '.join(p['referenceIds'])} | blocked |"


def tier_row(n):
    t = tiers[n]; w = t["worstMoment"]; b = t["budget"]
    return f"| {n} | {t['propDensity']:g} / {t['propCullScale']:g} | {t['instancesInSlice']} | {w['instantiatedUpperBound']} | {w['drawnUpperBoundDrawCalls']} (of {b['drawCalls']}) | {w['plannedTriangles']:,} (of {b['triangles']:,}) |"


wk_rows = "\n".join(f"| {w['id']} | {w['what']} | {', '.join(w['standsInFor'])} | {w['removalPath']} |" for w in wk["workarounds"])
ref_rows = "\n".join(f"| {r['id']} | {r['label']} | {r['ownerInterpretation']} | not established | rights unknown |" for r in reg["references"])
miss_rows = "\n".join(f"| {m['id']} | {m['area']} | {m['status']} |" for m in reg["missingReferences"])
kit_rows = "\n".join(f"| `{k['id']}` | {k['use']} | {k['referencesNeeded']} |" for k in inv["kitModules"])

DOC = f"""# Kano vertical slice: review package, revision 2

> **SUPERSEDED IN SCOPE (2026-11-01).** The owner corrected the scope: the 1.65 km corridor described here is only a **road-system test corridor, now called TC-1**; it is **not** the city. The deliverable is Kano City. See **`KANO_CITY_MASTER_PLAN.md`** for the master plan, and `kano/city/` for the districts, road network, reference-to-asset matrix and asset registry. This package's findings on references, coordinates, texture compression and the engine backlog still stand and are referenced from the master plan.

**Status (2026-11-01): PRE-PRODUCTION. Detailed modelling has NOT started and will not until the reference and coordinate questions are resolved and the final layout is approved.** Nothing here is a finished asset, road or playable Kano. The draft greybox of revision 1 is kept as a reviewable draft (`environments/kano_slice_greybox/v002` in the workspace; its renders are on the review page). This revision answers the owner's review decisions of 2026-11-01. **The Kano package is committed locally only; it is not pushed or merged** until the CI and asset-storage decisions are resolved.

| # | Deliverable | Where | State |
|---|---|---|---|
| 1 | Reference provenance and usage-rights report | `docs/kano/REFERENCE_REGISTER.md`, `reference_register.json` | done: all five images classified; **no location established, rights unknown**; missing-reference list of nine areas |
| 2 | Private asset repository structure and integration workflow | `docs/ASSET_REPO_PROPOSAL.md`, `tools/asset-repo-template/` | **repository `AffanNamu/ARO-CONTENT` created by the owner (private, checked) and the text-only skeleton pushed**; template and validator tested (9 tests); its own validation workflow passed on the first push |
| 3 | Verified Kano coordinate recommendation | `docs/kano/COORDINATES.md`, `kano_world_frame.json` | done; **no coordinate changed**; canonical anchor and transform recommended, impact documented |
| 4 | Road-system limitations and future-improvement backlog | `docs/ROAD_ENGINE_BACKLOG.md`, `kano_workarounds.json` | done; **shared road engine untouched**; five Kano workarounds declared, all data-only and test-enforced |
| 5 | Confirmed Unity texture-compression report | `docs/TEXTURE_BUDGET.md`, `texture_findings.json` | from a real Unity 6000.0.58f2 run in CI (cold import); **four findings: one defect (the world map ships uncompressed), one setting (Gamma colour space), two risks** |
| 6 | Final greybox review renders and layout | the review page (layout diagram, plans, eight street views) | layout unchanged by the decisions; shown with annotations |
| 7 | Updated asset inventory and production budgets | sections 4 and 6 below | regenerated; per-prefab reference blockers; budgets from the real settings |

Evidence files (all in `docs/kano/`, regenerated and checked by `tests/NetCore.Tests/KanoSliceTests.cs`, 10 tests, which passes with the rest of the core suite): the route draft, the inventory, the world frame, the workaround register, the reference register, the generated geometry and statistics. Generators: `tools/kano/*.py`. Renders and the supplied images are **not** committed (binary files stay out of the public repository).

---
## 1. References: classification and what is missing
The owner clarified that the five images were **researched and supplied by ChatGPT; the owner took screenshots and forwarded them**. They are therefore classified as **ChatGPT-provided visual research references**: not original photographs by the owner, not verified images of any named place, not production assets. **No image is claimed to show Kano.** Full record, hashes and per-image reasoning: `docs/kano/REFERENCE_REGISTER.md`.

| Id | What it is | Owner's interpretation | Location | Rights |
|---|---|---|---|---|
{ref_rows}

* **Original source of every image: unknown** (the screenshots carry no page address or credit). R-005 carries a photographer's watermark ("NUUMAN ASHAKA"); a web search found no portfolio or profile for that name.
* **Allowed uses:** private reference only (palette, density, rhythm, layout ideas), pending the owner's decision. **Public documentation: no.** **Production redistribution: no** (never a texture, decal, reference plane or part of any build). The images are not stored in either repository; a SHA-256 identifies each. The review page that shows them is private and must not be shared.
* **R-004 (roundabout) and R-005 (interchange)** are design inspiration for backlog items RB-01 and RB-03 only; nothing is implemented from them.
* Generated concept images may supplement real photographs but must be labelled **CONCEPT: not a photograph of an actual Kano location**.

### Missing references (what authentic Kano architecture and streets still need)
Each entry in `reference_register.json` lists the shots needed, the prefabs and kit modules it unblocks, and acceptable provenance (owner-taken, licensed or commissioned, openly licensed per file, archival terms read; never copied or traced).
| Id | Area | Status |
|---|---|---|
{miss_rows}

**Verified facts about Kano** (Gidan Rumfa, Kurmi Market, Dala Hill, Kofar Mata, the walls, Hausa building features, Harmattan) with sources, and what was searched for and not found, are in revision 1 of this document's research section, preserved in `docs/kano/RESEARCH.md`.

---
## 2. Layout (unchanged by the decisions)
One fictional corridor, 1.65 km at 1:1 scale, authored in the proposed Kano frame `K0` (section 5.1) and connected to the existing world-map route `kaduna-kano`. Zones in driving order: **city entrance** (divided road, median lights, gantry), **modern district**, **commercial spine** (with a 150 m damaged section), **old-neighbourhood edge** with a qibla-oriented neighbourhood mosque. Six staggered T-junctions lead to side streets whose first 120 m are lined with plots. The annotated plan is on the review page; the real landmarks (Gidan Rumfa, Kurmi Market, Dala Hill, Kofar Mata) are not in this slice and are reserved for an old-city slice (R-001 is the candidate reference for a Dala Hill environment, subject to geographic verification).

---
## 3. Road network: the decision and the workarounds
**Decision: preserve the current road-generation architecture.** The slice is authored as route data in the game's own format and validated with the game's own `RouteSpecValidator` and `RouteModel` (no building overlaps another or enters a road; the mosque faces the qibla within 1 degree; every plot has its no-apron twin planned). The limits found, and the future improvements (roundabouts, four-way intersections, flyovers, sidewalks and kerbs, drainage, surface-condition layers, text-bearing signs, side-street scatter, and more), are recorded in **`docs/ROAD_ENGINE_BACKLOG.md`** as RB-01 to RB-11 with what proper work involves.

**No permanent Kano-specific workaround.** The five workarounds the slice uses are declared in `docs/kano/kano_workarounds.json`; each names the backlog item that removes it and a **data-only** removal path; `KanoSliceTests.EveryWorkaroundIsDeclaredAndDataOnly` fails if a placement is neither a declared workaround nor a declared set piece (checked with a negative control).
| Id | Workaround | Removed by | Removal path |
|---|---|---|---|
{wk_rows}

---
## 4. Updated asset inventory
{len(inv['props'])} planned prefabs and 12 Blender kit modules. Every prefab now names the reference gaps that **block it** (register ids), and none is ready to model. Plot prefabs are exported twice from one Blender source: with the 4.5 m apron (W-01, used now) and as `<id>_noapron` (catalogued only when RB-04/RB-05 exist). The "placed" column is the count in the full slice at full density, generated from the real scatter. All ids are new (`kano_*`); dimensions are placeholder envelopes.

| id | category | w x d x h (m) | planned LOD triangles | collider | cull (m) | placed | blocked by | ready to model |
|---|---|---|---|---|---|---|---|---|
{chr(10).join(inv_row(p) for p in inv['props'])}

### Blender kit modules (internal parts, not catalogue entries)
| module | use | references needed |
|---|---|---|
{kit_rows}

---
## 5. Blender to Unity production plan

### 5.1 World architecture and coordinates
Recommended (not applied; see `docs/kano/COORDINATES.md`): a **canonical geographic anchor** (`kano_city_anchor` {fr['canonicalAnchor']['lat']}N {fr['canonicalAnchor']['lon']}E, GeoNames, cross-checked) and a **separate transform** `K0` into Unity coordinates (local tangent plane, metres, x east, z north, 200 m chunk-lattice aligned; error 5 m at 10 km and 17 m at 25 km measured against the WGS84 geodesic). The world-map pin (12.0022N, 8.592E, 8.2 km from the anchor, source unrecorded, like every other city pin offset by 1.7 to 10.8 km from GeoNames) is kept as a map pin and not used for geometry. **Neither number was replaced.** Impact on existing corridors: none (Lagos-Ibadan uses no latitude or longitude); future corridors anchor to the canonical anchor and declare their compression.

### 5.2 The per-asset sequence
Reference reading written down and corrected by the owner -> blockout -> modelling from kit modules, UVs, one atlas material with the shared `M_<surface>` names, LODs, `col_*` box -> `validate_asset` (the game's own budgets) -> `save_project` (new `vNNN`) -> `export_asset` FBX and GLB with manifest (refuses to overwrite, refuses a failing asset) -> Unity import test (measured 4/4 on a real Unity 6 run) -> prefab builder (identity root, own LODGroup and BoxCollider, shared-kit materials) -> committed to the **private asset repository** with its manifest and licence row -> catalogue entry and placement rule here (text only) -> owner reviews and runs `approve`.

### 5.3 Gates (nothing proceeds without the owner's yes)
G0 review package approved -> **G1 references classified and the missing list filled** (blocker) and **coordinate decision** (blocker) -> G2 final layout approved -> G3 first plot end to end (`kano_hausa_compound_a`: Blender -> validate -> export -> Unity import -> prefab -> catalogue -> placed in a *test* route) -> G4 production by family -> G5 route registered in the game (a new `Resources/Routes` file and tests; no existing system touched) -> G6 driving and performance validation (vehicle alignment, collisions, `[Perf]` in the browser build) -> G7 owner review and approval.

---
## 6. Production budgets
### 6.1 Geometry and draw calls (computed from the real scatter and the game's LOD policy; not measured, not frame time)
Method (`kano_slice_stats.json`): the player stands on the centre line every 25 m; a prefab counts only inside its tier-scaled cull distance at the LOD level `LodPolicy.PropLodLevel` gives; planned LOD triangles; one draw call per instance; all directions, so an upper bound; excludes terrain, road, vehicles and UI.
| Tier | density / cull scale | instances in slice | alive at the worst moment | **drawn** draw calls, upper bound | planned triangles |
|---|---|---|---|---|---|
{tier_row('low')}
{tier_row('medium')}
{tier_row('high')}

Design targets asserted by the test: props use at most **60%** of a tier's draw-call budget and **45%** of its triangle budget in any 1 km window. Real-GPU frame time is unvalidated project-wide.

### 6.2 Textures (from the actual Unity settings: `docs/TEXTURE_BUDGET.md`)
{tx['summary']}

---
## 7. The greybox
The draft greybox (placeholder volumes generated from the game's own road model, terrain and placement; category colours; `environments/kano_slice_greybox/v002`) is kept unchanged as a reviewable draft: the decisions of 2026-11-01 do not change the layout. The review page adds the annotated layout diagram. A greybox invites judgement of **rhythm, scale and layout only**; it contains no architecture.

---
## 8. Decisions and blockers
1. **Private repository:** `AffanNamu/ARO-CONTENT` exists, is private and holds the text-only skeleton and the metadata-only reference register (no image, no binary, no secret). Still open: the artifact boundary for browser tests ((a) fold, (b) encrypt, recommended (b)), the Git LFS quota, written confirmation of asset ownership, and the read token and variable (yours to set).
2. **References (blocks all modelling):** establish the original source, location and rights of R-001 to R-005 if you can, and supply the nine missing-reference areas of the register.
3. **Coordinates:** approve the canonical anchor and `K0` transform (no existing file changes) and decide whether world-map `lat/lon` means a pin or an anchor (a change to all eight pins and the live backend seed, authorised and migrated separately).
4. **Texture findings (section 6.2):** four findings in the existing project (a defect, a setting, two risks) are reported for your decision; none was changed.
5. **CI and branches:** the Kano package stays local until you resolve the above and say so. The camera branch is verified independently (see the report in the conversation); the texture-report branch (`work/texture-report`) is separate and contains only a report tool and its workflow.
6. **Network access (optional):** to read OpenStreetMap, Wikidata or the GeoNames API directly for coordinate verification, add `nominatim.openstreetmap.org`, `www.wikidata.org` and `api.geonames.org` to the environment's allowed domains.
"""

def main():
    if "--check" in sys.argv:
        ok = (ROOT / "docs/KANO_VERTICAL_SLICE.md").read_text() == DOC
        print("up to date" if ok else "OUT OF DATE: docs/KANO_VERTICAL_SLICE.md"); return 0 if ok else 1
    (ROOT / "docs/KANO_VERTICAL_SLICE.md").write_text(DOC); print("wrote docs/KANO_VERTICAL_SLICE.md", len(DOC)); return 0

if __name__ == "__main__": sys.exit(main())
