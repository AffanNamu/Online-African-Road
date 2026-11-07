#!/usr/bin/env python3
"""Turns the log of a review run (blender-unity.yml with [kano-review]) into a small JSON of the game's own numbers and into the tables of the performance report. Nothing is typed by hand.
  python3 tools/review_numbers.py extract <job.log> <out.json> --run <id> --commit <sha> [--note TEXT]
  python3 tools/review_numbers.py compare <before.json> <after.json>         (markdown tables on stdout)
The log lines are the `numbers` step of the job: "<timestamp>Z <label> {json}" with the labels of the review driver (ref, street1-merged, env_village_wet-drive, ...)."""
from __future__ import annotations
import json, re, sys

LINE = re.compile(r"^\S+Z ([A-Za-z0-9_\-@]+) (\{.*\})\s*$")


def extract(log_text: str) -> dict:
    views = {}
    for line in log_text.splitlines():
        m = LINE.match(line)
        if not m: continue
        try: d = json.loads(m.group(2))
        except ValueError: continue
        if "perf" in d or "failed" in d: d.pop("render", None); views[m.group(1)] = d
    return views


def num(view: dict, key: str, default=None):
    p = (view.get("perf") or [""])[-1]; m = re.search(key + r"=([\d.]+)", p); return float(m.group(1)) if m else default


def draws(view: dict, group: str) -> int | None:
    s = (view.get("draws") or {}).get(group) or ""; m = re.search(r"draws=(\d+)", s); return int(m.group(1)) if m else None


def by_asset(view: dict) -> dict:
    s = (view.get("draws") or {}).get("byAsset") or ""
    if ") " not in s: return {}
    out = {}
    for part in s.split(") ", 1)[1].split(", "):
        k, _, v = part.partition("="); a = v.split("/")
        if len(a) == 3: out[k] = tuple(int(x) for x in a)
    return out


def _ranked(line: str) -> dict:
    """{name: (draws, renderers, tris)} from a '(draws/renderers/tris) name=d/r/t, ...' line."""
    if ") " not in line: return {}
    out = {}
    for part in line.split(") ", 1)[1].split(", "):
        k, _, v = part.rpartition("="); a = v.split("/")
        if k and len(a) == 3: out[k] = tuple(int(x) for x in a)
    return out


def by_lod(view: dict) -> dict:
    """Kit draws per LOD level of the running game: {'0': (draws, renderers, tris), '1': ..., 'none': ...}; keys are the LOD level, 'none' for renderers outside a LOD group."""
    return {k[len("lod:"):] if k.startswith("lod:") else k: v for k, v in _ranked((view.get("draws") or {}).get("byLod") or "").items()}


def by_lod_asset(view: dict) -> dict:
    """{('aro_villa_2storey_hip_a', 1): (draws, renderers, tris)}: kit draws per asset and LOD level."""
    out = {}
    for k, v in _ranked((view.get("draws") or {}).get("byLodAsset") or "").items():
        base, _, lod = k.removeprefix("lodasset:").rpartition("#")
        if base: out[(base, lod)] = v
    return out


def row(label: str, v: dict) -> str:
    est = num(v, "drawEstimate"); eng = num(v, "engineDraws"); tri = num(v, "visibleTris"); ren = num(v, "visibleRenderers"); sp = num(v, "setPass"); mat = (v.get("draws") or {}).get("kit", "")
    dm = re.search(r"distinctMaterials=(\d+)", mat); dp = re.search(r"distinctMeshMaterialPairs=(\d+)", mat); img = v.get("image") or {}
    f = lambda x: "-" if x is None else f"{x:,.0f}"
    return f"| {label} | {f(est)} | {f(eng)} | {f(ren)} | {f(tri)} | {f(sp)} | {dm.group(1) if dm else '-'} / {dp.group(1) if dp else '-'} | {img.get('magentaFraction', '-')} | {img.get('nearBlackFraction', '-')} |"


HEAD = "| View | Draw calls (game estimate) | Engine draws (with shadows) | Visible renderers | Visible triangles | Set-pass | Distinct materials / (mesh, material) pairs | Magenta pixels | Near-black pixels |\n|---|---|---|---|---|---|---|---|---|"


def compare(before: dict, after: dict) -> str:
    out = ["| View | Draw calls before | after | Change | Engine draws before | after | Change |", "|---|---|---|---|---|---|---|"]
    for k, a in after["views"].items():
        b = before["views"].get(k)
        if not b: continue
        e0, e1, g0, g1 = num(b, "drawEstimate"), num(a, "drawEstimate"), num(b, "engineDraws"), num(a, "engineDraws")
        if None in (e0, e1): continue
        out.append(f"| {k} | {e0:,.0f} | {e1:,.0f} | {100 * (e1 - e0) / e0:+.1f}% | {g0:,.0f} | {g1:,.0f} | {100 * (g1 - g0) / g0:+.1f}% |" if g0 and g1 else f"| {k} | {e0:,.0f} | {e1:,.0f} | {100 * (e1 - e0) / e0:+.1f}% | - | - | - |")
    out += ["", HEAD] + [row(k, v) for k, v in after["views"].items()]
    return "\n".join(out)


def lod_table(doc: dict, assets: int = 6) -> str:
    """Markdown: for every view, the kit draws at each LOD level of the running game, and the assets with the most draws at LOD1 and above (where a lower-detail LOD would save draws)."""
    out = ["| View | LOD0 draws | LOD1 draws | LOD2 draws | LOD3 draws | Outside a LOD group | Largest assets at LOD1 or above (draws) |", "|---|---|---|---|---|---|---|"]
    for k, v in doc["views"].items():
        L = by_lod(v)
        if not L: continue
        hi = sorted(((d[0], a, l) for (a, l), d in by_lod_asset(v).items() if l not in ("0", "none")), reverse=True)[:assets]
        out.append(f"| {k} | " + " | ".join(str(L.get(x, (0, 0, 0))[0]) for x in ("0", "1", "2", "3", "none")) + " | " + (", ".join(f"{a} LOD{l} {n}" for n, a, l in hi) or "-") + " |")
    return "\n".join(out)


def main(argv) -> int:
    if len(argv) >= 4 and argv[1] == "extract":
        run = argv[argv.index("--run") + 1] if "--run" in argv else ""; commit = argv[argv.index("--commit") + 1] if "--commit" in argv else ""; note = argv[argv.index("--note") + 1] if "--note" in argv else ""
        doc = {"run": int(run) if run.isdigit() else run, "commit": commit, "renderer": "headless Chromium, SwiftShader software GL, quality tier medium unless the label says low, hour 15.5, 1280x720", "note": note, "views": extract(open(argv[2], encoding="utf-8", errors="replace").read())}
        json.dump(doc, open(argv[3], "w"), indent=1); print(f"{len(doc['views'])} views written to {argv[3]}"); return 0
    if len(argv) == 3 and argv[1] == "lods":
        print(lod_table(json.load(open(argv[2])))); return 0
    if len(argv) == 4 and argv[1] == "compare":
        print(compare(json.load(open(argv[2])), json.load(open(argv[3])))); return 0
    print(__doc__); return 2


if __name__ == "__main__":
    sys.exit(main(sys.argv))
