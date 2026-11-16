"""The realistic people (people_kit): the ten designs build on the CC0 MakeHuman base, stand on the ground at believable heights, every cloth part faces outwards (the game culls back faces),
the gown pieces end where they should, the hijab cape lies over the clothes in the pose people are seen in, and the export reads back as the game reads it (tools/blender/people_kit/export.py,
ARO.NetCore.PeopleAsset). Needs the MakeHuman data (tools/blender/people_kit/fetch_makehuman.sh; CI fetches it before the tests)."""
import re
from pathlib import Path
import numpy as np
import pytest
from people_kit import figure as FIG
from people_kit.rig import Rig, standing, walking

ROOT = Path(__file__).resolve().parents[3]


@pytest.fixture(scope="module")
def figures():
    return {n: make() for n, make in FIG.DESIGNS.items()}


def test_the_designs_are_the_crowds_designs():
    cs = (ROOT / "Assets/_Project/Scripts/NetCore/People.cs").read_text()
    ids = re.findall(r'new Design \{ Id = "([a-z_]+)"', cs)
    assert len(ids) == 10 and list(FIG.DESIGNS) == ids, "people_kit.DESIGNS must list PeopleKit.Designs' ids in the same order (the game swaps them one for one)"
    from people_kit.export import COLOURWAYS
    assert set(COLOURWAYS) == set(ids) and all(len(c) == 3 for c in COLOURWAYS.values())


def test_heights_and_feet(figures):
    for n, f in figures.items():
        child = n.startswith(("boy", "girl"))
        lo, hi = (1.25, 1.50) if child else (1.52, 1.90)
        assert lo <= f.h.stature <= hi, (n, f.h.stature)
        allv = np.concatenate([p["verts"] for p in f.parts])
        assert -0.005 < allv[:, 1].min() < 0.03, (n, allv[:, 1].min())                           # the soles (shoes) on the ground


def _outward(p):
    """Share of the part's area whose face normal points away from the part's centre line (a vertical axis through its centroid): about 1 for a shell facing out."""
    v = np.asarray(p["verts"]); c = v.mean(0); out = tot = 0.0
    for f in p["faces"]:
        q = v[f]; n = np.zeros(3)
        for k in range(1, len(f) - 1): n += np.cross(q[k] - q[0], q[k + 1] - q[0])
        a = np.linalg.norm(n)
        if a < 1e-12: continue
        d = q.mean(0) - c; d[1] *= 0.25                                                             # mostly sideways: a skirt or a cap faces away from its axis
        out += a * (1 if n @ d > 0 else 0); tot += a
    return out / tot


def _agrees_with_source(p, src_n):
    """Share of the area of a part made from base-mesh vertices whose face normal agrees with the base surface it was made from (body, tights or skirt helper)."""
    v = np.asarray(p["verts"]); ok = tot = 0.0
    for f in p["faces"]:
        q = v[f]; n = np.zeros(3)
        for k in range(1, len(f) - 1): n += np.cross(q[k] - q[0], q[k + 1] - q[0])
        a = np.linalg.norm(n)
        if a < 1e-12: continue
        ok += a * (1 if n @ src_n[p["ids"][f]].sum(0) > 0 else 0); tot += a
    return ok / tot


def test_every_cloth_part_faces_outwards(figures):
    for n, f in figures.items():
        src_n = FIG._normals(f.h.verts, f.h.faces)
        for p in f.parts:
            if p["kind"] != "cloth": continue
            share = _agrees_with_source(p, src_n) if p["ids"] is not None else _outward(p)
            assert share > (0.9 if p["ids"] is not None else 0.8), (n, p["name"], round(share, 2))


def test_shoes_are_closed_and_outwards(figures):
    """Signed volume of each shoe (divergence theorem): positive only when every face looks out (the left shoe used to be built inside out)."""
    for n, f in figures.items():
        for p in f.parts:
            if not p["name"].startswith("shoe_"): continue
            v = np.asarray(p["verts"]); vol = 0.0
            for face in p["faces"]:
                for k in range(1, len(face) - 1): vol += np.dot(v[face[0]], np.cross(v[face[k]], v[face[k + 1]])) / 6.0
            assert 2e-4 < vol < 3e-3, (n, p["name"], vol)


def test_gowns_end_where_they_should(figures):
    """A garment piece never reaches below its own hem: the thighs beside the hips are not taken for sleeves (they once hung a strip of the babban riga down to the ankles)."""
    f = figures["man_babban_riga"]; top = [p for p in f.parts if p["name"] == "riga_top"][0]; skirt = [p for p in f.parts if p["name"] == "riga_skirt"][0]
    assert np.asarray(top["verts"])[:, 1].min() > f.hip_y - 0.20, np.asarray(top["verts"])[:, 1].min()
    assert np.asarray(skirt["verts"])[:, 1].min() > f.knee_y - 0.30
    for n in ("man_kaftan_cap", "man_kaftan_plain", "woman_hijab_long", "woman_abaya", "girl_school_hijab"):
        g = figures[n]; tops = [p for p in g.parts if p["name"].endswith("_top")]
        assert tops and all(np.asarray(t["verts"])[:, 1].min() > g.hip_y - 0.25 for t in tops), n


def test_the_cape_lies_over_the_clothes_when_standing(figures):
    """In the standing pose the hijab cape is outside the dress and the arms at every height it covers (nothing pokes through it)."""
    for n in ("woman_hijab_long", "woman_hijab_wrapper", "woman_abaya", "girl_school_hijab"):
        f = figures[n]; rig = Rig(f.h); tr = rig.transforms(*standing())
        cape = [p for p in f.parts if p["name"] == "hijab_cape"][0]; cv = rig.skin(np.asarray(cape["verts"]), bone="spine_03", pose=tr)
        y0, y1 = cv[:, 1].min() + 0.03, f.shoulder_y - 0.06
        cz = float(cv[:, 2].mean()); ca = np.arctan2(cv[:, 0], cv[:, 2] - cz); cr = np.hypot(cv[:, 0], cv[:, 2] - cz)
        worst = 0.0
        for p in f.parts:
            if p["kind"] != "cloth" or p["name"].startswith(("hijab", "shoe")): continue
            pv = rig.skin(np.asarray(p["verts"]), ids=p["ids"], bone=p["bone"], pose=tr); m = (pv[:, 1] > y0) & (pv[:, 1] < y1); pv = pv[m]
            for q in pv[:: max(1, len(pv) // 400)]:
                a = np.arctan2(q[0], q[2] - cz); r = np.hypot(q[0], q[2] - cz)
                near = (np.abs(cv[:, 1] - q[1]) < 0.06) & (np.abs(np.angle(np.exp(1j * (ca - a)))) < 0.2)
                if near.any(): worst = max(worst, r - cr[near].max())
        assert worst < 0.012, (n, round(worst, 3))


def test_the_walk_moves_the_feet_and_keeps_them_down(figures):
    f = figures["man_shirt_trousers"]; rig = Rig(f.h); body = [p for p in f.parts if p["name"] == "shoe_l"][0]
    z = []
    for i in range(8):
        pv = rig.skin(np.asarray(body["verts"]), bone=body["bone"], pose=rig.transforms(*walking(i / 8)))
        z.append(pv[:, 2].mean()); assert pv[:, 1].min() > -0.04, (i, pv[:, 1].min())
    assert max(z) - min(z) > 0.35, "the left foot swings through a step of at least 35 cm"


def test_export_reads_back_as_the_game_reads_it(tmp_path):
    from people_kit import export as EX
    import bpy  # noqa: F401  (the export needs Blender)
    bpy.ops.wm.read_factory_settings(use_empty=True)
    atlas = np.zeros((EX.ATLAS, EX.ATLAS, 3)); d = EX.export_design("boy_school", 0, atlas, log=lambda *_: None)
    size = EX.write([d], tmp_path / "people.bytes"); a = EX.read(tmp_path / "people.bytes")
    assert size == (tmp_path / "people.bytes").stat().st_size and a["version"] == EX.VERSION and a["walk"] == 8 and a["idle"] == 4
    D = a["designs"][0]; assert D["id"] == "boy_school" and D["child"] and D["skin_tiles"] == [0, 1, 2] and D["cloth_tiles"] == [3, 4, 5]
    l0, l1 = D["lods"]; t0, t1 = len(l0["tris"]), len(l1["tris"])
    assert 2600 < t0 <= 3500 and 600 < t1 <= 1000 and t1 < t0 / 2.5
    for L in (l0, l1):
        n = len(L["group"]); assert L["tris"].max() < n and set(np.unique(L["group"])) == {0, 1} and len(L["frames"]) == 12
        assert (L["uv"] >= 0).all() and (L["uv"] <= 1).all()
        for i, (p, q) in enumerate(L["frames"]):
            assert np.abs(p).max() < 2.0 and -0.05 < p[:, 1].min() < 0.06, (i, p[:, 1].min())             # on the ground in every frame (the walk bobs a little)
            assert np.allclose(np.linalg.norm(q, axis=1), 1.0, atol=0.03)
            if i == 8:   # idle: the figure faces +z and the game's x is mirrored, so the face (highest skin vertices in front) is at z > 0
                head = p[(L["group"] == 0) & (p[:, 1] > p[:, 1].max() - 0.25)]; assert head[:, 2].max() > 0.05
    # the texture tiles were painted (skin is brown, the boy's white shirt in colourway 0)
    sk = atlas[0:EX.TILE, 0:EX.TILE].reshape(-1, 3).mean(0); assert sk[0] > sk[2] and 0.05 < sk.mean() < 0.5
    cl = atlas[0:EX.TILE, 3 * EX.TILE:4 * EX.TILE].reshape(-1, 3).mean(0); assert cl.mean() > 0.3
    uv = EX.atlas_uv(a, l0["uv"], l0["group"], D["skin_tiles"][1], D["cloth_tiles"][2]); t = EX.TILE / EX.ATLAS
    s0 = l0["group"] == 0; assert (uv[s0, 0] >= t - 1e-6).all() and (uv[s0, 0] <= 2 * t + 1e-6).all()              # skin tone 1: tile 1
    c0 = ~s0; assert (uv[c0, 0] >= 5 * t - 1e-6).all() and (uv[c0, 0] <= 6 * t + 1e-6).all() and (uv[:, 1] >= 1 - t - 1e-6).all()
