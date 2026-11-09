"""The asset registry: specs register themselves here (a separate module so that running build_transport as __main__ sees the same table)."""
from __future__ import annotations

SPECS: dict[str, dict] = {}


def spec(asset_id, kind, builder, cols=(), note="", lods=(0, 1, 2), target=None):
    """`builder(lod)` returns a Part; `cols` are collision boxes (x0, y0, z0, x1, y1, z1) in Blender axes (none = the asset does not collide); `target` the breakdown size [x, y, z] it was written against."""
    SPECS[asset_id] = dict(id=asset_id, kind=kind, lods={lv: (lambda lv=lv: builder(lv)) for lv in lods}, cols=list(cols), note=note, target=target)
