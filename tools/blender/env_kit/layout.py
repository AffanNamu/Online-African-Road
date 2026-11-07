"""Measured facts about the environmental assets that a scene needs: the bounding box of LOD0 (metres, the asset's own axes, front is -Y) and, for ground patches, the surface datum (how far the nominal surface sits
above the asset's z = 0, so the patch can be placed that much lower). Taken from the builders themselves, never typed in, and cached next to the workspace."""
from __future__ import annotations
import json
from pathlib import Path


def measure(ids) -> dict:
    """{id: {"bbox": [xmin, ymin, zmin, xmax, ymax, zmax], "datum": float or None}} built from the LOD0 builders of the environmental kit."""
    import os
    from . import build_env as BE
    from kano_kit import build_all
    e4 = None
    if os.environ.get("ARO_ENV_KIT") == "e4":                                  # batch E4 (corrections of the in-game review, env_kit.e4): its builders and datums replace the E3 ones for the ids it corrects; importing it also registers its materials
        from . import e4 as e4
    out = {}
    for aid in ids:
        use4 = e4 is not None and aid in e4.SPECS_E4
        sp = e4.SPECS_E4[aid] if use4 else (BE.SPECS.get(aid) or build_all.SPECS[aid]); part = sp["lods"][0](); vs = [v.co for v in part.bm.verts]
        out[aid] = {"bbox": [round(min(v[i] for v in vs), 4) for i in range(3)] + [round(max(v[i] for v in vs), 4) for i in range(3)], "datum": (e4.DATUM_E4 if use4 else BE.DATUM).get(aid)}
        part.bm.free()
    return out


def load(ids, cache: Path | None = None) -> dict:
    """The measurements for `ids`, from the cache file when it has them all, else measured (and cached)."""
    have = {}
    if cache and cache.is_file(): have = json.loads(cache.read_text())
    miss = [i for i in ids if i not in have]
    if miss:
        have.update(measure(miss))
        if cache: cache.parent.mkdir(parents=True, exist_ok=True); cache.write_text(json.dumps(have, indent=1))
    return {i: have[i] for i in ids}
