"""The asset registry: specs register themselves here (a separate module so that running build_transport as __main__ sees the same table)."""
from __future__ import annotations

SPECS: dict[str, dict] = {}


REALISM_VERSION = 2       # the realism pass (2026-11-13) is version 2 of every asset it paints with the surface bake (version 1 is frozen in the private library)


def spec(asset_id, kind, builder, cols=(), note="", lods=(0, 1, 2), target=None, surface=None, ground=True, version=None):
    """`builder(lod)` returns a Part; `cols` are collision boxes (x0, y0, z0, x1, y1, z1) in Blender axes (none = the asset does not collide); `target` the breakdown size [x, y, z] it was written against;
    `surface` = atlas sizes in pixels per LOD, e.g. (1024, 512, 128): the asset is painted by transport_kit.surface (one atlas material per LOD) instead of the flat palettes; `ground` False for a piece that does not
    stand on the ground (no dust splash and no ground shadow in its bake). `version` is the design version the code builds: the version folder it gets in a FRESH workspace (the CI review) and the label in its baked material
    and texture names, so that names never repeat across versions of the append-only library (default: REALISM_VERSION for a surface-baked asset, else 1)."""
    SPECS[asset_id] = dict(id=asset_id, kind=kind, lods={lv: (lambda lv=lv: builder(lv)) for lv in lods}, cols=list(cols), note=note, target=target, surface=surface, ground=ground, version=version if version is not None else (REALISM_VERSION if surface else 1))
