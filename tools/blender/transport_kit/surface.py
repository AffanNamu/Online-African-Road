"""SURFACE BAKE: turns the flat palette colours of a kit asset into one painted texture atlas per LOD (base colour with baked ambient occlusion and weathering, a normal map, smoothness in the alpha channel).

Why. In the game (review run 42684305591, read on 2026-11-10) the transport kit read as grey boxes: constant-colour palette cells give a wall no grain, no dirt, no contact shadow, and a window is a black polygon. The
residential kit's tiling PBR maps do better but cannot carry more than four surface types per asset, nor any shading that belongs to ONE place of ONE building. This module paints every face of a model into its own
rectangle of an atlas, in real metres, so that:
  * every palette key becomes a material PATTERN evaluated in world space (plaster with mottling and hairline cracks, coursed stone, earth render, pavers, corrugated sheet, slatted shutters, plank doors, glazing with
    frames, mullions and curtains, lattice screens, sign boards), continuous across faces because it is a function of the world position;
  * AMBIENT OCCLUSION is measured on the real geometry (rays against a BVH of the mesh and the ground plane) and baked in: recesses, soffits, the foot of every wall and the back of every porch darken;
  * WEATHERING follows the building: laterite dust splashed on the bottom of walls, rain streaks, dirt gathered where the occlusion is, stains on flat roofs;
  * a NORMAL map is derived from the same height the pattern describes (joints, slats, ribs, frames), and SMOOTHNESS (glass, glazed tile, paint, raw render) goes in the base map's alpha.
One opaque material per LOD (so a building is one draw call per LOD instead of two or three), a texture of at most 2048 px, all of it deterministic (hashes of positions; no clock, no global random state).

What it is not. It is a painter, not a photograph: every pattern is procedural and generic (no brand, no text, no real facade), first-pass colours, and it cannot add depth the geometry does not have.
Texel density is 15 to 60 px per metre (it prints what it reached), so it is made for a building seen from a road, not for a wall at arm's length; a tiling detail map covers that range in the engine (see docs).

Use: `apply(part, asset_id, lod, size)` after a builder has made the Part and BEFORE palette.palettize; build_transport does it for every spec registered with `surface=...`."""
from __future__ import annotations
import math
import numpy as np
import bpy, bmesh
from mathutils import Vector
from mathutils.bvhtree import BVHTree
from aro_blender import materials as AM

AO_RAYS, AO_DIST, AO_STEP = 20, 3.2, 0.42
COVER = 0.15                                                # a sample with geometry this close straight in front of it is behind a proud element: hidden, filled from its open neighbours
DUST = np.array([0.53, 0.44, 0.35])                         # laterite dust (the colour of Kano's harmattan film on walls and roofs); first-pass, by eye from R-016 and R-037 (less saturated after the first previews read orange)
ALBEDO_MAX = 0.82                                           # no painted surface reflects more than this (fresh white paint is about 0.8): brighter texels blow out to a flat white plane in sun

# ------------------------------------------------------------------------------------------------------------------------------------------------ noise (deterministic, vectorised)
def _hash(ix, iy, iz, seed):
    h = (ix * 73856093) ^ (iy * 19349663) ^ (iz * 83492791) ^ (seed * 2654435761 + 1013904223)
    h &= 0xFFFFFFFF; h = ((h ^ (h >> 13)) * 1274126177) & 0xFFFFFFFF; h ^= h >> 16
    return h.astype(np.float64) / 4294967295.0


def vnoise(P, freq, seed=0):
    """Value noise in 0..1 at the points P (N, 3) scaled by freq (a number or three numbers)."""
    q = P * np.asarray(freq, dtype=np.float64); i = np.floor(q).astype(np.int64); f = q - i; f = f * f * (3.0 - 2.0 * f); out = np.zeros(len(P))
    for dx in (0, 1):
        wx = f[:, 0] if dx else 1.0 - f[:, 0]
        for dy in (0, 1):
            wy = f[:, 1] if dy else 1.0 - f[:, 1]
            for dz in (0, 1):
                wz = f[:, 2] if dz else 1.0 - f[:, 2]; out += wx * wy * wz * _hash(i[:, 0] + dx, i[:, 1] + dy, i[:, 2] + dz, seed)
    return out


def fbm(P, freq, octaves=3, seed=0, gain=0.5):
    out = np.zeros(len(P)); amp = 1.0; tot = 0.0
    for o in range(octaves): out += amp * vnoise(P, np.asarray(freq, dtype=np.float64) * (2.0 ** o), seed + 31 * o); tot += amp; amp *= gain
    return out / tot


def cell_hash(a, b, c=0, seed=0):
    """One random number in 0..1 per integer cell (a, b, c): the colour of one paver, one pane, one board."""
    return _hash(np.asarray(a, dtype=np.int64), np.asarray(b, dtype=np.int64), np.asarray(c, dtype=np.int64) + np.zeros_like(np.asarray(a, dtype=np.int64)), seed)


def smooth(e0, e1, x):
    t = np.clip((x - e0) / (e1 - e0 + 1e-12), 0.0, 1.0); return t * t * (3.0 - 2.0 * t)


def line(x, pitch, width):
    """1 on a line of `width` repeated every `pitch` (soft edges), else 0. x in metres."""
    d = np.abs(((x / pitch) % 1.0) - 0.5) * pitch; return 1.0 - smooth(width * 0.5, width * 0.5 + pitch * 0.02 + 0.004, pitch * 0.5 - d)


# ------------------------------------------------------------------------------------------------------------------------------------------------ material patterns
# Every pattern takes X (a dict of per-texel arrays: P world position, u and v metres inside the face rectangle, fw and fh the rectangle's size, edge the distance to the face outline, fid the face number,
# nz the normal's vertical component, along the horizontal run along the wall) and the key's library colour, and returns (rgb in sRGB 0..1, height in metres, smoothness 0..1).
def _col(key): return np.array(AM.LIBRARY[key][0], dtype=np.float64)


def _plaster(X, c, seed=3, mott=0.07, grain=0.03, crack=0.5, smooth_v=0.2):
    P = X["P"]; m = fbm(P, 0.33, 3, seed) - 0.5; g = vnoise(P, 21.0, seed + 5) - 0.5
    rgb = c[None, :] * (1.0 + mott * 2.0 * m + grain * 2.0 * g)[:, None]
    # hairline cracks: the ridges of a slow noise, only where a second noise allows them (a few per wall, not a net)
    r = np.abs(fbm(P, 0.9, 2, seed + 9) - 0.5); k = (1.0 - smooth(0.0, 0.012, r)) * smooth(0.55, 0.75, vnoise(P, 0.23, seed + 13)) * crack
    rgb *= (1.0 - 0.28 * k)[:, None]
    return rgb, 0.004 * g - 0.003 * k, np.full(len(P), smooth_v)


def _stone(X, c, seed=7):
    """Rendered stone in courses: a faint joint every 0.6 m, vertical joints staggered every 1.2 m, each block a slightly different tone."""
    rgb, h, s = _plaster(X, c, seed, 0.05, 0.035, 0.25, 0.24); z = X["P"][:, 2]; a = X["along"]
    vert = np.abs(X["nz"]) < 0.5
    row = np.floor(z / 0.75); stag = a + 0.75 * (row % 2); blk = np.floor(stag / 1.5)
    j = np.maximum(line(z, 0.75, 0.016), line(stag, 1.5, 0.016)) * vert
    tone = (cell_hash(blk, row, 0, seed) - 0.5) * 0.045 * vert
    rgb = rgb * (1.0 + tone - 0.085 * j)[:, None]
    return rgb, h - 0.004 * j, s


def _earth(X, c, seed=11):
    """Earth (kasa) render: warm and cool patches, the horizontal sweep of a hand, straw flecks, drying cracks."""
    P = X["P"]; m = fbm(P, 0.5, 3, seed) - 0.5; w = fbm(P, (0.7, 0.7, 5.0), 2, seed + 3) - 0.5; g = vnoise(P, 16.0, seed + 5) - 0.5
    rgb = c[None, :] * (1.0 + 0.16 * m + 0.08 * w + 0.07 * g)[:, None]
    rgb[:, 0] *= 1.0 + 0.05 * m; rgb[:, 2] *= 1.0 - 0.06 * m
    fleck = smooth(0.90, 0.97, vnoise(P, (30.0, 30.0, 9.0), seed + 7)); rgb += fleck[:, None] * np.array([0.10, 0.09, 0.05])
    r = np.abs(fbm(P, 1.6, 2, seed + 9) - 0.5); k = (1.0 - smooth(0.0, 0.014, r)) * smooth(0.45, 0.7, vnoise(P, 0.3, seed + 13))
    rgb *= (1.0 - 0.35 * k)[:, None]
    return rgb, 0.01 * w + 0.006 * g - 0.006 * k, np.full(len(P), 0.07)


def _concrete(X, c, seed=17):
    P = X["P"]; m = fbm(P, 0.4, 3, seed) - 0.5; g = vnoise(P, 14.0, seed + 5) - 0.5; pore = smooth(0.88, 0.97, vnoise(P, 40.0, seed + 7))
    board = line(P[:, 2], 0.5, 0.012) * (np.abs(X["nz"]) < 0.5)
    rgb = c[None, :] * (1.0 + 0.13 * m + 0.05 * g - 0.10 * pore - 0.10 * board)[:, None]
    return rgb, 0.003 * g - 0.004 * pore - 0.004 * board, np.full(len(P), 0.16)


def _pavers(X, c, seed=19, size=0.3, joint=0.022):
    """Square pavers with sanded joints; each paver a slightly different tone."""
    P = X["P"]; a = P[:, 0]; b = P[:, 1]; ia = np.floor(a / size); ib = np.floor(b / size)
    j = np.maximum(line(a, size, joint), line(b, size, joint)); tone = (cell_hash(ia, ib, 0, seed) - 0.5) * 0.18; m = fbm(P, 0.25, 2, seed) - 0.5
    rgb = c[None, :] * (1.0 + tone + 0.14 * m)[:, None]; rgb = rgb * (1.0 - j[:, None]) + j[:, None] * (DUST * 0.8)[None, :]
    return rgb, -0.008 * j, np.full(len(P), 0.14)


def _sheet(X, c, seed=23, pitch=0.2, rust=0.25):
    """Profiled steel sheet: ribs across the run of the wall (or down the slope of a roof), lap lines, rust creeping up from the bottom edge and down from fixings."""
    P = X["P"]; vert = np.abs(X["nz"]) < 0.5; along = np.where(vert, X["along"], X["u"]); rib = 0.5 + 0.5 * np.cos(2.0 * np.pi * along / pitch)
    m = fbm(P, 0.5, 2, seed) - 0.5; lap = line(np.where(vert, P[:, 2], X["v"]), 2.4, 0.02)
    rs = smooth(0.62, 0.9, fbm(P, (1.2, 1.2, 0.35), 3, seed + 3) + 0.25 * smooth(0.5, 0.0, X["v"])) * rust
    rgb = c[None, :] * (0.86 + 0.20 * rib + 0.08 * m - 0.12 * lap)[:, None]; rgb = rgb * (1.0 - rs[:, None]) + rs[:, None] * np.array([0.36, 0.18, 0.09])[None, :]
    return rgb, 0.012 * rib - 0.004 * lap, 0.42 * (1.0 - rs) + 0.08


def _painted_metal(X, c, seed=29, smooth_v=0.5):
    P = X["P"]; m = fbm(P, 1.1, 2, seed) - 0.5; chip = smooth(0.78, 0.9, fbm(P, 3.5, 2, seed + 3)) * smooth(0.6, 0.0, P[:, 2]) * 0.6
    rgb = c[None, :] * (1.0 + 0.06 * m)[:, None]; rgb = rgb * (1.0 - chip[:, None]) + chip[:, None] * np.array([0.33, 0.19, 0.11])[None, :]
    return rgb, -0.001 * chip, smooth_v * (1.0 - 0.7 * chip)


def _wood(X, c, seed=31, plank=0.14, vertical=True):
    P = X["P"]; a = X["u"] if vertical else X["v"]; b = X["v"] if vertical else X["u"]; ip = np.floor(a / plank)
    tone = (cell_hash(ip, X["fid"], 0, seed) - 0.5) * 0.16; grain = fbm(np.stack([a * 9.0, b * 0.8, ip * 3.7], axis=1), 1.0, 3, seed) - 0.5; gap = line(a, plank, 0.012)
    rgb = c[None, :] * (1.0 + tone + 0.20 * grain - 0.45 * gap)[:, None]
    return rgb, -0.005 * gap + 0.001 * grain, np.full(len(P), 0.28)


def _door(X, c, seed=37):
    """A plank door: two leaves, three raised rails, a row of studs along each rail, a darker kick zone at the bottom."""
    rgb, h, s = _wood(X, c, seed); u = X["u"]; v = X["v"]; fw = X["fw"]; fh = X["fh"]
    meet = 1.0 - smooth(0.0, 0.018, np.abs(u - fw * 0.5)); rail = np.zeros(len(u))
    for f in (0.12, 0.5, 0.86): rail = np.maximum(rail, 1.0 - smooth(0.05, 0.07, np.abs(v - fh * f)))
    stud = rail * (1.0 - smooth(0.012, 0.024, np.hypot(((u / 0.16) % 1.0 - 0.5) * 0.16, 0.0))) * 0.9
    rgb = rgb * (1.0 + 0.10 * rail - 0.6 * meet)[:, None]; rgb = rgb * (1.0 - stud[:, None]) + stud[:, None] * np.array([0.62, 0.50, 0.24])[None, :]
    rgb *= (1.0 - 0.25 * smooth(0.5, 0.0, v))[:, None]
    return rgb, h + 0.012 * rail - 0.012 * meet + 0.006 * stud, s + 0.3 * stud


def _shutter(X, c, seed=41):
    """A roll-up shutter: slats every 75 mm, guide rails at the sides, a bottom bar, grime towards the ground, each shutter of a facade a slightly different paint."""
    v = X["v"]; u = X["u"]; fw = X["fw"]; slat = 0.5 + 0.5 * np.cos(2.0 * np.pi * v / 0.075); guide = np.maximum(1.0 - smooth(0.04, 0.06, u), 1.0 - smooth(0.04, 0.06, fw - u)); bar = 1.0 - smooth(0.07, 0.09, v)
    tints = np.array([[0.46, 0.50, 0.55], [0.30, 0.42, 0.52], [0.45, 0.47, 0.42], [0.52, 0.50, 0.46], [0.36, 0.44, 0.38]]); t = tints[(cell_hash(X["fid"], 0, 0, seed) * 4.999).astype(int)]
    m = fbm(X["P"], 0.8, 2, seed) - 0.5; rgb = t * (0.80 + 0.22 * slat + 0.08 * m)[:, None]; rgb = rgb * (1.0 - 0.45 * np.maximum(guide, bar))[:, None]
    rgb *= (1.0 - 0.22 * smooth(0.8, 0.0, v))[:, None]
    return rgb, 0.008 * slat + 0.01 * np.maximum(guide, bar), np.full(len(u), 0.42)


def _glazing(X, c, seed=43, frame=(0.20, 0.21, 0.22), pane=0.9, lattice=None, sill=True):
    """A window or a glazed bay painted inside its face: a frame along the outline of the face (so an arched face gets an arched frame), mullions about every `pane` metres, a transom on tall openings, glass that
    reflects a lighter sky towards the top, a curtain or a blind behind some panes, and the shadow the head of the opening casts on the glass. `lattice` (a colour) draws a diagonal screen instead of plain glass."""
    u = X["u"]; v = X["v"]; fw = X["fw"]; fh = X["fh"]; e = X["edge"]; fid = X["fid"]; n = len(u)
    fwid = np.clip(0.055 + 0.01 * fw, 0.05, 0.11); fr = 1.0 - smooth(fwid, fwid + 0.012, e)
    npx = np.maximum(1, np.round(fw / pane)); pw = fw / npx; ipx = np.floor(u / pw); mull = (1.0 - smooth(0.022, 0.034, np.abs((u / pw) % 1.0 - 0.5) * pw * -1.0 + pw * 0.5)) * (npx > 1)
    tall = fh > 2.3; tz = np.where(tall, fh * 0.68, 1e9); trans = 1.0 - smooth(0.022, 0.034, np.abs(v - tz)); ipy = (v > tz).astype(np.int64)
    bars = np.maximum(mull, trans)
    sky = smooth(0.0, 1.0, v / np.maximum(fh, 0.1)); g = np.array([0.075, 0.105, 0.135])[None, :] * (1.0 - sky[:, None]) + np.array([0.30, 0.40, 0.50])[None, :] * sky[:, None]
    g = g * (0.85 + 0.3 * vnoise(np.stack([u * 0.7, v * 0.7, fid * 1.0], axis=1), 1.0, seed))[:, None]
    r = cell_hash(ipx, ipy, fid, seed); cur = (r > 0.62).astype(np.float64); drop = np.where(r > 0.86, 1.0, np.where(r > 0.62, 0.45 + 0.5 * cell_hash(ipx, fid, 7, seed), 0.0))
    curc = np.array([[0.74, 0.70, 0.60], [0.62, 0.66, 0.70], [0.70, 0.56, 0.44]])[(cell_hash(fid, ipx, 3, seed) * 2.999).astype(int)]
    behind = cur * (v > fh * (1.0 - drop)) * (0.55 + 0.25 * np.cos(u * 40.0))
    g = g * (1.0 - 0.55 * behind[:, None]) + curc * (0.55 * behind[:, None])
    g *= (1.0 - 0.45 * smooth(0.35, 0.0, (fh - v)))[:, None]                                          # the reveal's shadow on the top of the glass
    if lattice is not None:
        d1 = np.abs(((u + v) / 0.22) % 1.0 - 0.5); d2 = np.abs(((u - v) / 0.22) % 1.0 - 0.5)
        lat = 1.0 - smooth(0.0, 0.09, np.minimum(d1, d2)); g = g * (1.0 - lat[:, None]) + np.asarray(lattice)[None, :] * lat[:, None] * (0.8 + 0.2 * sky[:, None]); bars = np.maximum(bars, 0.0)
    else: lat = np.zeros(n)
    fcol = np.asarray(frame)[None, :] * (0.9 + 0.2 * vnoise(X["P"], 3.0, seed + 1))[:, None]
    k = np.maximum(fr, bars)[:, None]; rgb = g * (1.0 - k) + fcol * k
    h = 0.02 * fr + 0.012 * bars + 0.006 * lat - 0.012
    s = np.where(np.maximum(fr, bars) > 0.5, 0.45, np.where(lat > 0.5, 0.3, 0.9 - 0.5 * behind))
    return rgb, h, s


def _void(X, c, seed=47):
    """An opening you look into (a porch, an arcade bay): darker towards the top and the edges, a floor that catches a little light."""
    v = X["v"]; fh = X["fh"]; e = X["edge"]; d = 0.5 + 0.5 * smooth(0.0, 0.9, 1.0 - v / np.maximum(fh, 0.1)); k = 0.35 + 0.65 * smooth(0.0, 0.5, e)
    rgb = np.array([0.105, 0.095, 0.085])[None, :] * (1.4 - 0.9 * k * (1.0 - d * 0.6))[:, None] + (vnoise(X["P"], 2.0, seed) - 0.5)[:, None] * 0.02
    return np.clip(rgb, 0.0, 1.0), np.full(len(v), -0.01), np.full(len(v), 0.1)


def _signboard(X, c, seed=53):
    """A shop's sign board with NO text and no brand: a coloured panel inside a pale border, each board of a facade another colour, faded and dusty (the game may draw a name on it later)."""
    u = X["u"]; v = X["v"]; e = X["edge"]; cols = np.array([[0.58, 0.16, 0.13], [0.13, 0.30, 0.52], [0.16, 0.42, 0.26], [0.80, 0.62, 0.16], [0.80, 0.78, 0.72], [0.20, 0.20, 0.22], [0.78, 0.40, 0.14]])
    t = cols[(cell_hash(X["fid"], 1, 0, seed) * 6.999).astype(int)]; border = 1.0 - smooth(0.05, 0.065, e); m = fbm(X["P"], 1.3, 2, seed) - 0.5
    band = (1.0 - smooth(0.0, 0.02, np.abs(v - X["fh"] * 0.5) - X["fh"] * 0.16)) * 0.10
    rgb = t * (0.92 + 0.14 * m + band)[:, None]; rgb = rgb * (1.0 - border[:, None]) + np.array([0.80, 0.78, 0.72])[None, :] * border[:, None]
    return rgb, 0.004 * border, np.full(len(u), 0.35)


def _dome(X, c, seed=59):
    """A sheeted or tiled dome: courses that follow the height, streaks that run down the slope, a paler patina where the rain washes."""
    P = X["P"]; course = line(P[:, 2], 0.45, 0.02); st = fbm(P, (2.2, 2.2, 0.25), 3, seed) - 0.5; m = fbm(P, 0.4, 2, seed + 3) - 0.5
    rgb = c[None, :] * (1.0 + 0.22 * st + 0.10 * m - 0.16 * course)[:, None]; rgb[:, 1] *= 1.0 + 0.06 * st; rgb += smooth(0.25, 0.5, st)[:, None] * np.array([0.05, 0.09, 0.07])[None, :]
    return rgb, -0.006 * course + 0.002 * st, np.full(len(P), 0.55)


def _gold(X, c, seed=61):
    m = fbm(X["P"], 2.0, 2, seed) - 0.5; return c[None, :] * (1.0 + 0.12 * m)[:, None], np.zeros(len(m)), np.full(len(m), 0.72)


def _foliage(X, c, seed=67):
    P = X["P"]; m = fbm(P, 3.0, 3, seed) - 0.5; d = smooth(0.55, 0.8, vnoise(P, 9.0, seed + 3))
    rgb = c[None, :] * (1.0 + 0.5 * m - 0.35 * d)[:, None]; rgb[:, 1] *= 1.0 + 0.12 * m
    return rgb, 0.02 * m, np.full(len(P), 0.12)


def _water(X, c, seed=71):
    m = fbm(X["P"], 1.5, 2, seed) - 0.5; return c[None, :] * (1.0 + 0.25 * m)[:, None], 0.002 * m, np.full(len(m), 0.92)


def _lamp(X, c, seed=73): return np.repeat(np.array([[0.99, 0.90, 0.66]]), len(X["u"]), axis=0) * (0.92 + 0.08 * smooth(0.0, 0.05, X["edge"]))[:, None], np.zeros(len(X["u"])), np.full(len(X["u"]), 0.6)


def _roof_tile(X, c, seed=79):
    P = X["P"]; v = X["v"]; u = X["u"]; row = np.floor(v / 0.32); t = (cell_hash(np.floor((u + 0.11 * (row % 2)) / 0.22), row, 0, seed) - 0.5) * 0.2
    lip = 1.0 - smooth(0.0, 0.06, (v / 0.32) % 1.0 * 0.32); m = fbm(P, 0.6, 2, seed) - 0.5
    return c[None, :] * (1.0 + t + 0.1 * m - 0.3 * lip)[:, None], 0.012 * (1.0 - lip) * ((v / 0.32) % 1.0), np.full(len(u), 0.3)


def _cloth(X, c, seed=83):
    u = X["u"]; v = X["v"]; w = 0.5 + 0.25 * np.cos(u * 700.0) + 0.25 * np.cos(v * 700.0); m = fbm(X["P"], 1.0, 2, seed) - 0.5
    return c[None, :] * (0.9 + 0.1 * w + 0.12 * m)[:, None], 0.001 * w, np.full(len(u), 0.1)


def _flat(X, c, seed=89, smooth_v=0.3):
    m = fbm(X["P"], 0.9, 2, seed) - 0.5; return c[None, :] * (1.0 + 0.07 * m)[:, None], np.zeros(len(m)), np.full(len(m), smooth_v)


PLASTERS = ("plaster_cream", "plaster_white", "plaster_ochre", "plaster_terracotta", "paint_white", "plaster_cream_light", "plaster_weathered")
SHEETS = ("sheet_cream", "sheet_green", "roof_zinc", "roof_zinc_rust", "container_blue", "container_red", "container_green", "container_grey")
METALS = ("metal_galvanised", "metal_gate_black", "metal_gate_blue", "frame_aluminium_dark", "railing_green", "paint_red", "paint_blue_bus", "paint_orange", "warning_yellow", "sign_blue", "sign_green", "sign_yellow",
          "kerb_black", "kerb_white", "road_white", "road_yellow", "signal_red", "signal_amber", "signal_green", "rubber", "insulator_brown")
PATTERN = {"stone_ivory": _stone, "kasa_render": _earth, "mud_brick": _earth, "laterite": _earth, "dry_earth": _earth, "concrete": _concrete, "concrete_dark": _concrete, "pavement_grey": lambda X, c: _pavers(X, c, 97, 0.6, 0.012),
           "tile_floor": lambda X, c: _pavers(X, c, 101, 0.45, 0.01), "paver_red": _pavers, "wood_door": _door, "timber_light": _wood, "timber_weathered": _wood, "shutter_steel": _shutter, "glass": _glazing, "glass_dark": _glazing,
           "interior_dark": _void, "board_dark": _signboard, "dome_green": _dome, "trim_gold": _gold, "hedge_green": _foliage, "leaf_green": _foliage, "leaf_green_dark": _foliage, "flower_red": _foliage, "flower_bougainvillea": _foliage,
           "water_pool": _water, "lantern_glow": _lamp, "roof_tile_red": _roof_tile, "burlap": _cloth, "fabric_red": _cloth, "fabric_white": _cloth, "tarp_olive": _cloth}
for _k in PLASTERS: PATTERN[_k] = _plaster
for _k in SHEETS: PATTERN[_k] = (lambda X, c, _r=(0.5 if "rust" in _k else 0.22 if "container" in _k else 0.15), _p=(0.28 if "container" in _k else 0.2): _sheet(X, c, 23, _p, _r))
for _k in METALS: PATTERN[_k] = _painted_metal
# Keys a builder can use to say what a face IS (they have no palette cell: they exist only for the surface bake). The colour is the frame or the screen colour.
EXTRA_KEYS = {"window_lattice": ((0.20, 0.36, 0.27), 0.5, 0.0, 0.0, "surface bake only: a mosque window, a green lattice screen in a pale frame (R-037)"),
              "window_plain": ((0.20, 0.21, 0.22), 0.3, 0.0, 0.0, "surface bake only: a framed window with mullions and curtains"),
              "window_timber": ((0.30, 0.20, 0.13), 0.5, 0.0, 0.0, "surface bake only: a small timber-framed window of an earth building")}
for _k, _v in EXTRA_KEYS.items(): AM.LIBRARY.setdefault(_k, _v)
PATTERN["window_lattice"] = lambda X, c: _glazing(X, c, 43, (0.84, 0.80, 0.70), 0.9, lattice=(0.16, 0.34, 0.25))
PATTERN["window_plain"] = lambda X, c: _glazing(X, c, 43, (0.20, 0.21, 0.22), 0.85)
PATTERN["window_timber"] = lambda X, c: _glazing(X, c, 43, (0.30, 0.20, 0.13), 0.6)
WINDOWS = {"glass", "glass_dark", "window_lattice", "window_plain", "window_timber", "interior_dark", "wood_door", "shutter_steel", "board_dark", "lantern_glow"}     # no wall weathering on these (they have their own)

# ---- painted cladding colours of the city construction directive (2026-11-14): painted steel and aluminium composite panels (fuel station canopies, shop fascias)
CLADDING = {"paint_navy": ((0.075, 0.11, 0.29), "navy painted cladding (fuel station canopy, fascia and totem; the fictional KANO OIL chain of the owner's concept R-040)"),
            "paint_brand_yellow": ((0.95, 0.70, 0.10), "warm yellow painted trim (fuel station canopy edge, island ends)"),
            "paint_green_dark": ((0.07, 0.30, 0.20), "dark green painted cladding"), "paint_maroon": ((0.42, 0.10, 0.10), "maroon painted cladding"),
            "paint_grey_light": ((0.70, 0.71, 0.72), "light grey aluminium composite panel"), "paint_sky": ((0.36, 0.60, 0.80), "sky blue painted render")}
for _k, (_c, _n) in CLADDING.items():
    AM.LIBRARY.setdefault(_k, (_c, 0.45, 0.0, 0.0, "surface bake only: " + _n)); PATTERN[_k] = _painted_metal


def _shopfront(X, c, seed=47):
    """A lit shop window seen from the forecourt: aluminium mullions about every 1.5 m and a transom, a warm interior with rows of shelves full of coloured goods, a pale reflection of the sky across the top."""
    u = X["u"]; v = X["v"]; fw = X["fw"]; fh = X["fh"]; e = X["edge"]; fid = X["fid"]; n = len(u)
    fr = 1.0 - smooth(0.06, 0.075, e); npx = np.maximum(1, np.round(fw / 1.5)); pw = fw / npx
    mull = (1.0 - smooth(0.03, 0.045, pw * 0.5 - np.abs((u / pw) % 1.0 - 0.5) * pw)) * (npx > 1); tz = np.minimum(fh * 0.82, 2.6); trans = 1.0 - smooth(0.03, 0.045, np.abs(v - tz))
    shelf_row = np.floor(v / 0.42); on_shelf = (v > 0.25) & (v < np.minimum(tz - 0.25, 2.0))
    goods = np.array([[0.80, 0.22, 0.16], [0.18, 0.42, 0.70], [0.92, 0.80, 0.30], [0.25, 0.60, 0.32], [0.92, 0.92, 0.88], [0.70, 0.40, 0.70], [0.95, 0.55, 0.15]])
    gi = (cell_hash(np.floor(u / 0.18), shelf_row, fid, seed) * 6.999).astype(int); board = 1.0 - smooth(0.0, 0.035, (v % 0.42))
    interior = np.array([0.86, 0.80, 0.66])[None, :] * (0.85 + 0.15 * smooth(0.0, 1.0, v / np.maximum(fh, 0.1)))[:, None]
    g = np.where(on_shelf[:, None], goods[gi] * (0.75 + 0.25 * cell_hash(np.floor(u / 0.18), shelf_row, 3, seed))[:, None], interior)
    g = g * (1.0 - 0.5 * (board * on_shelf)[:, None]) + np.array([0.55, 0.55, 0.57])[None, :] * (0.5 * board * on_shelf)[:, None]
    refl = smooth(0.55, 1.0, v / np.maximum(fh, 0.1)) * (0.25 + 0.15 * vnoise(np.stack([u * 0.3, v * 0.3, fid * 1.0], axis=1), 1.0, seed))
    g = g * (1.0 - refl[:, None]) + np.array([0.62, 0.72, 0.82])[None, :] * refl[:, None]
    k = np.maximum(fr, np.maximum(mull, trans))[:, None]; fcol = np.array([0.70, 0.71, 0.72])[None, :]
    return g * (1.0 - k) + fcol * k, 0.015 * k[:, 0] - 0.01, np.where(k[:, 0] > 0.5, 0.5, 0.9)


AM.LIBRARY.setdefault("glass_shop", ((0.70, 0.71, 0.72), 0.1, 0.0, 0.0, "surface bake only: a lit shop window with shelves of goods behind it")); PATTERN["glass_shop"] = _shopfront; WINDOWS.add("glass_shop")


# ---- painted signs: words and a generic flame mark painted INTO the atlas (deterministic; Pillow's built-in scalable font, Aileron, CC0: no font file in the repository). A sign key is a colour key: a board's
# faces wider and taller than 0.25 m carry the layout, its thin edges the plain panel. Brand names are fictional (the owner's concept chain KANO OIL, R-040) or generic words (FILLING STATION, PHARMACY).
SIGNS = {}


def sign_key(key, bg, fg, lines=(), logo=False, accent=None, note="", layout="auto"):
    """Registers a sign: a panel of colour `bg`, the text `lines` (top to bottom) in `fg`, an optional flame mark (`logo`), an optional accent stripe along the bottom. `layout` auto: the mark beside the text on a wide
    face, above it on a tall one. Returns the key."""
    SIGNS[key] = dict(bg=tuple(bg), fg=tuple(fg), lines=list(lines), logo=bool(logo), accent=None if accent is None else tuple(accent), layout=layout)
    AM.LIBRARY.setdefault(key, (tuple(bg), 0.4, 0.0, 0.0, note or ("surface bake only: a painted sign" + (": " + " / ".join(lines) if lines else ""))))
    PATTERN[key] = lambda X, c, _k=key: _sign(X, SIGNS[_k]); WINDOWS.add(key); return key


def _font(px):
    from PIL import ImageFont
    try: return ImageFont.load_default(size=max(6, int(px)))
    except TypeError: return ImageFont.load_default()                     # Pillow without FreeType: the small bitmap font (still legible, coarser)


def _flame(d, x0, y0, w, h):
    """A generic flame-in-a-drop mark (not any company's logo): an orange drop with a red tongue and a yellow core."""
    def drop(cx, cy, rw, rh, fill):
        pts = []
        for i in range(48):
            t = 2 * math.pi * i / 48; r = 1.0 - 0.55 * max(0.0, math.sin(t)) ** 3
            pts.append((cx + rw * math.cos(t) * r, cy - rh * 0.25 - rh * 0.75 * math.sin(t) * (1.25 if math.sin(t) > 0 else 0.6)))
        d.polygon(pts, fill=fill)
    cx, cy = x0 + w / 2, y0 + h * 0.62
    drop(cx, cy, w * 0.42, h * 0.62, (245, 120, 20)); drop(cx + w * 0.04, cy + h * 0.06, w * 0.28, h * 0.42, (200, 30, 25)); drop(cx + w * 0.06, cy + h * 0.14, w * 0.14, h * 0.22, (255, 214, 60))


def _sign_image(s, W, H):
    """The sign's layout as an RGB image of W x H pixels (row 0 = TOP of the face)."""
    from PIL import Image, ImageDraw
    bg = tuple(int(round(255 * x)) for x in s["bg"]); fg = tuple(int(round(255 * x)) for x in s["fg"]); im = Image.new("RGB", (W, H), bg); d = ImageDraw.Draw(im)
    pad = int(min(W, H) * 0.08); ax0, ay0, ax1, ay1 = pad, pad, W - pad, H - pad
    if s["accent"] is not None:
        ah = max(2, int(H * 0.12)); d.rectangle([0, H - ah, W, H], fill=tuple(int(round(255 * x)) for x in s["accent"])); ay1 = H - ah - pad // 2
    lines = s["lines"]; tall = s["layout"] == "stack" or (s["layout"] == "auto" and H > 0.75 * W)
    if s["logo"]:
        if tall or not lines: lh = int((ay1 - ay0) * (0.55 if lines else 1.0)); lw = min(ax1 - ax0, int(lh * 0.8)); _flame(d, (W - lw) / 2, ay0, lw, lh); ay0 += lh + pad // 2
        else: lw = int((ay1 - ay0) * 0.8); _flame(d, ax0, ay0, lw, ay1 - ay0); ax0 += lw + pad
    if lines and ay1 - ay0 > 4 and ax1 - ax0 > 4:
        n = len(lines); slot = (ay1 - ay0) / n; size = slot * 0.8
        for _ in range(24):                                                    # the largest size at which every line fits the width
            f = _font(size); widest = max(d.textbbox((0, 0), ln, font=f, stroke_width=max(1, int(size * 0.05)))[2] for ln in lines)
            if widest <= ax1 - ax0 or size < 7: break
            size *= 0.92
        f = _font(size); sw = max(1, int(size * 0.05))
        for i, ln in enumerate(lines):
            b = d.textbbox((0, 0), ln, font=f, stroke_width=sw); tw, th = b[2] - b[0], b[3] - b[1]
            d.text(((ax0 + ax1) / 2 - tw / 2 - b[0], ay0 + slot * i + slot / 2 - th / 2 - b[1]), ln, font=f, fill=fg, stroke_width=sw, stroke_fill=fg)
    return im


def _sign(X, s):
    n = len(X["u"]); rgb = np.repeat(np.array([s["bg"]], dtype=np.float64), n, axis=0); hgt = np.zeros(n); smo = np.full(n, 0.55)
    for f in np.unique(X["fid"]):
        m = X["fid"] == f; fw = float(X["fw"][m][0]); fh = float(X["fh"][m][0])
        if min(fw, fh) < 0.25 or not (s["lines"] or s["logo"]): continue
        ppm = math.sqrt(m.sum() / max(fw * fh, 1e-6)); k = min(4.0, 4096.0 / max(fw, fh) / max(ppm, 1e-6))   # supersampled 4 x (at most 4096 px a side)
        W = max(8, int(round(fw * ppm * k))); H = max(8, int(round(fh * ppm * k))); w2 = max(2, int(round(fw * ppm))); h2 = max(2, int(round(fh * ppm)))
        from PIL import Image
        img = np.asarray(_sign_image(s, W, H).resize((w2, h2), Image.BOX), dtype=np.float64) / 255.0
        ix = np.clip((X["u"][m] / fw * w2).astype(int), 0, w2 - 1); iy = np.clip(((1.0 - X["v"][m] / fh) * h2).astype(int), 0, h2 - 1)
        rgb[m] = img[iy, ix]; mm = fbm(X["P"][m], 1.1, 2, 211) - 0.5; rgb[m] *= (0.96 + 0.06 * mm)[:, None]
    return np.clip(rgb, 0.0, 1.0), hgt, smo


# ------------------------------------------------------------------------------------------------------------------------------------------------ the bake
def _frames(bm):
    """Per face: key index, normal, tangent U (horizontal along the wall), bitangent V (up the wall), the 2D outline in that frame, its rectangle and the plane offset."""
    out = []
    for f in bm.faces:
        n = f.normal.copy()
        if n.length < 1e-9: n = Vector((0, 0, 1))
        if abs(n.z) > 0.999: U = Vector((1, 0, 0)); V = n.cross(U).normalized()
        else: U = Vector((0, 0, 1)).cross(n).normalized(); V = n.cross(U).normalized()
        pts = [(v.co.dot(U), v.co.dot(V)) for v in f.verts]; us = [p[0] for p in pts]; vs = [p[1] for p in pts]
        out.append(dict(face=f, key=f.material_index, n=n, U=U, V=V, poly=np.array(pts), u0=min(us), v0=min(vs), w=max(max(us) - min(us), 1e-4), h=max(max(vs) - min(vs), 1e-4), d=f.verts[0].co.dot(n),
                        z0=min(v.co.z for v in f.verts), z1=max(v.co.z for v in f.verts), area=f.calc_area()))
    return out


def _hemisphere(k):
    d = []
    for i in range(k):
        a = 2.0 * math.pi * ((i * 0.6180339887498949) % 1.0); r2 = (i + 0.5) / k; r = math.sqrt(r2); d.append((r * math.cos(a), r * math.sin(a), math.sqrt(max(0.0, 1.0 - r2))))
    return d


def _in_poly(pts, poly):
    """Even-odd point-in-polygon for many points (N, 2) against one polygon (M, 2)."""
    x = pts[:, 0][:, None]; y = pts[:, 1][:, None]; a = poly; b = np.roll(poly, -1, axis=0)
    c = ((a[:, 1] > y) != (b[:, 1] > y)) & (x < (b[:, 0] - a[:, 0]) * (y - a[:, 1]) / (b[:, 1] - a[:, 1] + 1e-12) + a[:, 0])
    return (c.sum(axis=1) % 2) == 1


def coplanar_overlaps(bm, tol=1e-4, grid=6):
    """Pairs of faces that lie in the same plane, FACE THE SAME WAY and overlap (more than a sliver): the renderer cannot tell which is in front, so the surface flickers in the game and shadow rays strike the twin
    face (black patches in a ray-traced preview). Back-to-back faces (a relief on a slab) are not this case: those are buried and removed by the bake. Returns [(face index a, face index b)]."""
    bm.faces.ensure_lookup_table(); groups = {}
    for f in bm.faces:
        n = f.normal
        if n.length < 0.5: continue
        k = (round(n.x, 3), round(n.y, 3), round(n.z, 3), round(f.verts[0].co.dot(n) / (tol * 20)))
        groups.setdefault(k, []).append(f)
    out = []
    for fs in groups.values():
        if len(fs) < 2: continue
        n = fs[0].normal
        U = Vector((1, 0, 0)) if abs(n.z) > 0.999 else Vector((0, 0, 1)).cross(n).normalized(); V = n.cross(U).normalized()
        poly = {f.index: np.array([(v.co.dot(U), v.co.dot(V)) for v in f.verts]) for f in fs}
        box = {i: (p.min(axis=0), p.max(axis=0)) for i, p in poly.items()}
        for ia in range(len(fs)):
            a = fs[ia].index; la, ha = box[a]
            for ib in range(ia + 1, len(fs)):
                b = fs[ib].index; lb, hb = box[b]
                if abs(fs[ia].verts[0].co.dot(n) - fs[ib].verts[0].co.dot(n)) > tol: continue
                lo = np.maximum(la, lb) + 2e-3; hi = np.minimum(ha, hb) - 2e-3
                if (hi <= lo).any(): continue
                g = np.stack(np.meshgrid(np.linspace(lo[0], hi[0], grid), np.linspace(lo[1], hi[1], grid)), axis=-1).reshape(-1, 2)
                if (_in_poly(g, poly[a]) & _in_poly(g, poly[b])).any(): out.append((a, b))
    return out


def _components(bm):
    """Face index -> primitive id: faces that share a whole EDGE (both end positions, welded at 0.01 mm). The builders' primitives do not share vertices between their faces, so this is how a box, prism or
    tube is found again; two primitives that only touch at a corner (a container's corner post and its top rail) stay separate. Moving a primitive moves all its faces together (no cracks)."""
    parent = list(range(len(bm.faces))); first = {}
    def find(i):
        while parent[i] != i: parent[i] = parent[parent[i]]; i = parent[i]
        return i
    q = lambda v: (round(v.co.x * 1e5), round(v.co.y * 1e5), round(v.co.z * 1e5))
    for f in bm.faces:
        vs = [q(v) for v in f.verts]
        for i in range(len(vs)):
            k = tuple(sorted((vs[i], vs[(i + 1) % len(vs)])))
            if k in first:
                a, b = find(first[k]), find(f.index)
                if a != b: parent[a] = b
            else: first[k] = f.index
    return {f.index: find(f.index) for f in bm.faces}


def _frame_of(n):
    U = Vector((1, 0, 0)) if abs(n.z) > 0.999 else Vector((0, 0, 1)).cross(n).normalized(); return U, n.cross(U).normalized()


def _covered(small, big):
    """True when face `small` lies wholly inside face `big` (same plane): a grid over the small face, pulled 1 mm inside its outline, must all fall inside the big face."""
    U, V = _frame_of(big.normal); ps = np.array([(v.co.dot(U), v.co.dot(V)) for v in small.verts]); pb = np.array([(v.co.dot(U), v.co.dot(V)) for v in big.verts]); c = ps.mean(axis=0)
    inset = ps + (c - ps) * np.minimum(1.0, 1e-3 / np.maximum(np.linalg.norm(c - ps, axis=1, keepdims=True), 1e-9))
    lo, hi = ps.min(axis=0), ps.max(axis=0); g = np.stack(np.meshgrid(np.linspace(lo[0], hi[0], 9), np.linspace(lo[1], hi[1], 9)), axis=-1).reshape(-1, 2)
    g = np.concatenate([g[_in_poly(g, ps)], inset]); return bool(_in_poly(g, pb).all())


def _sliver(f):
    zs = [v.co.z for v in f.verts]; return abs(f.normal.z) < 0.1 and max(zs) <= 0.16 and max(zs) - min(zs) <= 0.16


def separate_coplanar(bm, step=0.003, rounds=6, rules=1):
    """Resolves every coplanar overlapping pair (see coplanar_overlaps): the smaller PRIMITIVE moves `step` metres out along the shared normal (a 3 mm step cannot be seen from a road, a flickering face can);
    when both faces belong to the same primitive (a frame whose jamb tops sit inside its lintel top), the smaller face is deleted if the larger one covers it completely (nothing visible is lost). Repeats until
    nothing is left that can be resolved this way. Returns the number of primitives moved plus faces deleted.
    `rules` 2 (the families of 2026-11-14 onwards; the frozen v002 assets keep 1 so that they rebuild as frozen): a primitive is never pushed back against an earlier move (two pairs on opposite sides of it made
    it oscillate), and when one face of a pair is a thin side of a ground slab (at most 16 cm high, at the ground) and the other is not, the sliver is deleted: a few centimetres of slab edge under a wall end
    are never seen, while moving either one only shifts the coincidence to the next wall along the lot boundary."""
    moves = 0; disp = {}                                                     # displacement so far per primitive (keyed by one of its vertices): a primitive is never moved back against an earlier move
    for _ in range(rounds):
        bm.verts.index_update(); bm.faces.index_update(); bm.faces.ensure_lookup_table(); bm.normal_update()
        pairs = coplanar_overlaps(bm)
        if not pairs: break
        comp = _components(bm); area = {}; rep = {}
        for f in bm.faces: area[comp[f.index]] = area.get(comp[f.index], 0.0) + f.calc_area(); rep.setdefault(comp[f.index], f.verts[0])
        done = set(); drop = set(); acted = 0
        for a, b in pairs:
            ca, cb = comp[a], comp[b]
            if ca == cb:
                fa, fb = bm.faces[a], bm.faces[b]; sm, lg = (fa, fb) if fa.calc_area() < fb.calc_area() else (fb, fa)
                if sm.index not in drop and lg.index not in drop and _covered(sm, lg): drop.add(sm.index); acted += 1
                continue
            if rules >= 2:
                fa, fb = bm.faces[a], bm.faces[b]; sa, sb_ = _sliver(fa), _sliver(fb)
                if sa != sb_:
                    f = fa if sa else fb
                    if f.index not in drop: drop.add(f.index); acted += 1
                    continue
            n = bm.faces[a].normal; order = sorted((ca, cb), key=lambda c: (area[c], c))
            if rules < 2: ok = [order[0]] if order[0] not in done else []                                         # rules 1: exactly the behaviour the v002 assets were frozen with
            else: ok = [c for c in order if c not in done and disp.get(rep[c], Vector((0, 0, 0))).dot(n) > -1e-9]
            if not ok: continue
            mv = ok[0]; done.add(mv); d = n * step; acted += 1; disp[rep[mv]] = disp.get(rep[mv], Vector((0, 0, 0))) + d
            for v in {v for f in bm.faces if comp[f.index] == mv for v in f.verts}: v.co += d
        if drop:
            bmesh.ops.delete(bm, geom=[bm.faces[i] for i in drop], context="FACES"); loose = [v for v in bm.verts if not v.link_faces]
            if loose: bmesh.ops.delete(bm, geom=loose, context="VERTS")
        moves += acted
        if not acted: break
    bm.verts.index_update(); bm.faces.index_update(); bm.faces.ensure_lookup_table(); bm.normal_update(); return moves


_OUT = [Vector(v).normalized() for v in ((0, 0, 1), (1, 0, 0.05), (-1, 0, 0.05), (0, 1, 0.05), (0, -1, 0.05))]


def _escapes(bvh, o, d, ground):
    """True when the ray from o along d leaves the model: it meets no face at all, or (for an asset on the ground) it reaches the ground at a point that is itself in the open, i.e. from which a ray straight up or
    sideways leaves the model. The second case keeps a soffit that oversails a wall (its rays land on the street) and drops a box enclosed by walls that stand open at the bottom (its rays land on the floor inside)."""
    far = bvh.ray_cast(o, d, 600.0)
    if not (ground and d.z < -1e-3 and o.z > 0.0): return far[0] is None
    tg = o.z / -d.z
    if far[0] is not None and far[3] < tg: return False
    g = o + d * tg; g.z = 0.05
    return any(bvh.ray_cast(g, w, 600.0)[0] is None for w in _OUT)


def _occlusion(bm, frames, ground=True):
    """Ambient occlusion per face on a coarse grid (the bake upsamples it): 1 = open sky, 0 = buried. Rays are cast against the mesh itself and, for an asset that stands on the ground, the plane z = 0.
    Also returns, per face, whether any ray left the model altogether: a face from which none does stands inside a closed volume (the back of a box inside a hall) and cannot be seen."""
    bvh = BVHTree.FromBMesh(bm, epsilon=0.0); dirs = _hemisphere(AO_RAYS); res = []; opened = []
    for F in frames:
        seen = False
        if F["area"] < 0.03: nu = nv = 1
        else: nu = min(48, max(2, int(math.ceil(F["w"] / AO_STEP)) + 1)); nv = min(48, max(2, int(math.ceil(F["h"] / AO_STEP)) + 1))
        n = F["n"]; U = F["U"]; V = F["V"]; wd = [(U * dx + V * dy + n * dz) for dx, dy, dz in dirs]; g = np.ones((nv, nu)); base = n * F["d"]; cov = np.zeros((nv, nu), dtype=bool)
        for j in range(nv):
            fv = 0.5 if nv == 1 else 0.04 + 0.92 * j / (nv - 1)
            for i in range(nu):
                fu = 0.5 if nu == 1 else 0.04 + 0.92 * i / (nu - 1)
                o = base + U * (F["u0"] + F["w"] * fu) + V * (F["v0"] + F["h"] * fv) + n * 0.025; occ = 0.0
                for d in wd:
                    hit = bvh.ray_cast(o, d, AO_DIST); t = hit[3] if hit[0] is not None else None
                    if not seen and hit[0] is None: seen = _escapes(bvh, o, d, ground)                                         # can this face be seen from outside the model?
                    if ground and d.z < -1e-3 and o.z > 0.0:
                        tg = o.z / -d.z
                        if tg < AO_DIST and (t is None or tg < t): t = tg
                    if t is not None: occ += 1.0 - (t / AO_DIST) ** 2
                g[j, i] = 1.0 - occ / len(wd); cov[j, i] = bvh.ray_cast(o, n, COVER)[0] is not None       # something stands right in front of this point (a sign board, a sill, a cornice): it is hidden
        if cov.any() and not cov.all(): g = g.copy(); g[cov] = 0.0                                                     # hidden samples take their value from the open ones (no sooty halo round proud elements)
        res.append(_despeckle(_unbury(g))); opened.append(seen)
    return res, opened


def _unbury(g):
    """A sample that sits INSIDE another volume (behind a window frame, under a pier standing on a wall) is black, and the upsampling would smear it half a metre onto the visible wall as a sooty halo. Such samples
    take the value of their open neighbours instead (the real contact shadow is in those neighbours already). A face with no open sample at all stays black: it is hidden."""
    buried = g < 0.1
    if not buried.any() or buried.all(): return g
    g = g.copy()
    for _ in range(max(g.shape)):
        if not buried.any(): break
        acc = np.zeros_like(g); cnt = np.zeros_like(g); ok = (~buried).astype(np.float64); v = g * ok
        for dy, dx in ((0, 1), (0, -1), (1, 0), (-1, 0)):
            acc += np.roll(v, (dy, dx), (0, 1)) * _inside(g.shape, dy, dx); cnt += np.roll(ok, (dy, dx), (0, 1)) * _inside(g.shape, dy, dx)
        fill = buried & (cnt > 0); g[fill] = acc[fill] / cnt[fill]; buried = buried & ~fill
    return g


def _despeckle(g, drop=0.2):
    """A lone sample far darker than its neighbours (a point just behind a window pane or inside a balcony slab, where one ray in twenty finds a gap) would bake a black blotch half a metre wide. No sample may be
    more than `drop` darker than the median of its 3 x 3 neighbourhood; a real dark band (a soffit, the foot of a wall) is dark in its neighbours too and is kept."""
    if min(g.shape) < 3: return g
    pad = np.pad(g, 1, mode="edge"); st = np.stack([pad[1 + dy:1 + dy + g.shape[0], 1 + dx:1 + dx + g.shape[1]] for dy in (-1, 0, 1) for dx in (-1, 0, 1)])
    return np.maximum(g, np.median(st, axis=0) - drop)


def _inside(shape, dy, dx):
    """Mask that undoes the wrap-around of np.roll: 1 where the rolled value came from inside the grid."""
    m = np.ones(shape)
    if dy == 1: m[0, :] = 0
    if dy == -1: m[-1, :] = 0
    if dx == 1: m[:, 0] = 0
    if dx == -1: m[:, -1] = 0
    return m


def _density(F, key, ao_mean):
    """Texels per metre relative to a wall: what the eye is close to gets more, what it never sees gets almost none."""
    if ao_mean < 0.06: return 0.0                                  # buried inside another volume: two texels
    nz = F["n"].z
    if key in ("glass", "glass_dark", "glass_shop", "window_lattice", "window_plain", "window_timber", "wood_door", "board_dark", "shutter_steel") or key in SIGNS: return 1.6 if key in SIGNS else 1.3
    if nz > 0.7: return 0.38 if F["z0"] > 2.5 else 0.5             # roofs and paving
    if nz < -0.5: return 0.3                                       # soffits
    return 1.0


def _pack(sizes, S):
    """Shelf packing of (w, h) pixel rectangles (already turned so that h <= w) into an S x S square. Returns positions or None."""
    order = sorted(range(len(sizes)), key=lambda i: (-sizes[i][1], -sizes[i][0])); pos = [None] * len(sizes); x = y = 0; shelf = 0
    for i in order:
        w, h = sizes[i]
        if w > S: return None
        if x + w > S: x = 0; y += shelf; shelf = 0
        if y + h > S: return None
        pos[i] = (x, y); x += w; shelf = max(shelf, h)
    return pos


def bake(part, size=1024, ground=True, pad=None, seed=0, rules=1):
    """Paints the Part into an atlas. Returns {'base': (S, S, 4) sRGB + smoothness, 'normal': (S, S, 3), 'ppm': texels per metre on walls, 'faces': n, 'fill': share of the atlas used} and writes the UVs into the
    Part's bmesh (one island per face). Does not touch materials: `apply` does."""
    bm = part.bm; bm.normal_update(); moved = separate_coplanar(bm, rules=rules); bm.faces.ensure_lookup_table(); bm.normal_update(); S = int(size); pad = (2 if S >= 1024 else 1) if pad is None else pad
    left = coplanar_overlaps(bm); mixed = sum(1 for i, j in left if bm.faces[i].material_index != bm.faces[j].material_index)   # same colour + world-space pattern = the same texels: cannot flicker; different colours can
    frames = _frames(bm); keys = list(part.keys); ao, opened = _occlusion(bm, frames, ground)
    dens = [_density(F, keys[F["key"]], float(g.max()) if op else 0.0) for F, g, op in zip(frames, ao, opened)]
    # A face with no open sample is buried inside another volume (the back of a relief, the wall behind a pier): nobody can see it, it would only cost triangles and overdraw, and where it is coplanar with a visible
    # face it flickers. It is removed from the mesh. (The builders make solids by overlapping boxes; this is the pass that cleans the inside of them.)
    dead = [F["face"] for F, k in zip(frames, dens) if k <= 0.0]
    if dead and len(dead) < len(frames):
        keep = [i for i, k in enumerate(dens) if k > 0.0]; frames = [frames[i] for i in keep]; ao = [ao[i] for i in keep]; dens = [dens[i] for i in keep]
        bmesh.ops.delete(bm, geom=dead, context="FACES"); loose = [v for v in bm.verts if not v.link_faces]
        if loose: bmesh.ops.delete(bm, geom=loose, context="VERTS")
        bm.faces.ensure_lookup_table(); bm.verts.ensure_lookup_table()
    removed = len(dead) if dead and len(dead) <= len(frames) + len(dead) else 0

    def layout(ppm):
        sizes = []; turned = []; pp = []
        for F, k in zip(frames, dens):
            p = ppm * k
            if k <= 0.0: w = h = 2; p = 2.0 / max(F["w"], F["h"])
            else: w = max(2, int(math.ceil(F["w"] * p))); h = max(2, int(math.ceil(F["h"] * p)))
            w += 2 * pad; h += 2 * pad; t = h > w
            sizes.append((h, w) if t else (w, h)); turned.append(t); pp.append(p)
        return sizes, turned, pp, _pack(sizes, S)

    lo, hi = 0.5, 400.0
    for _ in range(18):
        mid = (lo + hi) / 2
        if layout(mid)[3] is not None: lo = mid
        else: hi = mid
    ppm = lo; sizes, turned, pp, pos = layout(ppm)
    if pos is None: raise ValueError(f"surface bake: {len(frames)} faces do not fit a {S} px atlas even at {ppm:.2f} px per metre")

    # ---- every texel of every island: where it is in the world and in its face
    N = sum(w * h for w, h in sizes); P = np.empty((N, 3)); uu = np.empty(N); vv = np.empty(N); fw = np.empty(N); fh = np.empty(N); edge = np.empty(N); fid = np.empty(N, dtype=np.int64); kid = np.empty(N, dtype=np.int64)
    nz = np.empty(N); along = np.empty(N); aot = np.empty(N); z0 = np.empty(N); z1 = np.empty(N); ax = np.empty(N, dtype=np.int64); ay = np.empty(N, dtype=np.int64); c = 0; slices = []
    for i, F in enumerate(frames):
        w, h = sizes[i]; iw, ih = (h, w) if turned[i] else (w, h); p = pp[i]                    # iw x ih: the island in its own (u, v) axes
        cu = (np.arange(iw) - pad + 0.5) / p; cv = (np.arange(ih) - pad + 0.5) / p; U2, V2 = np.meshgrid(cu, cv); u = U2.ravel(); v = V2.ravel(); n = len(u); s = slice(c, c + n); slices.append((s, iw, ih))
        Uv = np.array(F["U"]); Vv = np.array(F["V"]); nv = np.array(F["n"])
        P[s] = nv[None, :] * F["d"] + Uv[None, :] * (F["u0"] + u)[:, None] + Vv[None, :] * (F["v0"] + v)[:, None]
        uu[s] = u; vv[s] = v; fw[s] = F["w"]; fh[s] = F["h"]; fid[s] = i; kid[s] = F["key"]; nz[s] = F["n"].z; z0[s] = F["z0"]; z1[s] = F["z1"]
        along[s] = P[s][:, 0] * abs(Uv[0]) + P[s][:, 1] * abs(Uv[1])                                # the run along the wall in world metres (continuous round a corner well enough for joints)
        poly = F["poly"] - np.array([F["u0"], F["v0"]]); d = np.full(n, 1e9)
        for a in range(len(poly)):
            pa = poly[a]; pb = poly[(a + 1) % len(poly)]; ab = pb - pa; L2 = float(ab @ ab) + 1e-12; t = np.clip(((u - pa[0]) * ab[0] + (v - pa[1]) * ab[1]) / L2, 0.0, 1.0)
            d = np.minimum(d, np.hypot(u - (pa[0] + ab[0] * t), v - (pa[1] + ab[1] * t)))
        edge[s] = d
        g = ao[i]; gy = np.clip(v / F["h"], 0, 1) * (g.shape[0] - 1); gx = np.clip(u / F["w"], 0, 1) * (g.shape[1] - 1); x0 = np.floor(gx).astype(int); y0 = np.floor(gy).astype(int)
        x1 = np.minimum(x0 + 1, g.shape[1] - 1); y1 = np.minimum(y0 + 1, g.shape[0] - 1); tx = gx - x0; ty = gy - y0
        aot[s] = (g[y0, x0] * (1 - tx) + g[y0, x1] * tx) * (1 - ty) + (g[y1, x0] * (1 - tx) + g[y1, x1] * tx) * ty
        px, py = pos[i]; col = np.tile(np.arange(iw), ih); row = np.repeat(np.arange(ih), iw)
        if turned[i]: ax[s] = px + row; ay[s] = py + col
        else: ax[s] = px + col; ay[s] = py + row
        c += n
    X = dict(P=P, u=uu, v=vv, fw=fw, fh=fh, edge=edge, fid=fid, nz=nz, along=along)

    # ---- the pattern of every key
    rgb = np.zeros((N, 3)); hgt = np.zeros(N); smo = np.zeros(N)
    for ki, key in enumerate(keys):
        m = kid == ki
        if not m.any(): continue
        Xm = {k: v[m] for k, v in X.items()}; fn = PATTERN.get(key, _flat); r, h, s = fn(Xm, _col(key)); rgb[m] = r; hgt[m] = h; smo[m] = s

    # ---- weathering that follows the building (not the windows, doors and lamps: they carry their own)
    wall = np.ones(N, dtype=bool)
    for ki, key in enumerate(keys):
        if key in WINDOWS: wall &= kid != ki
    vert = np.abs(nz) < 0.5; up = nz > 0.5; z = P[:, 2]
    if ground:
        top = 0.45 + 0.30 * vnoise(P, 0.8, 7 + seed)                                                                                       # dust splashed up the foot of the walls: a hand to a forearm high, varying along the wall, patchy
        sp = smooth(top, 0.0, z + 0.16 * (vnoise(P, 3.1, 5 + seed) - 0.5)) * vert * wall * (0.10 + 0.24 * smooth(0.30, 0.80, fbm(P, 1.3, 3, 9 + seed)))
        rgb = rgb * (1.0 - sp[:, None]) + DUST[None, :] * sp[:, None]; smo *= 1.0 - 0.6 * sp
    streak = smooth(0.5, 0.85, fbm(np.stack([along * 2.6, z * 0.22, along * 0.0], axis=1), 1.0, 3, 21 + seed)) * vert * wall                    # rain streaks: narrow, long, stronger under ledges
    fall = smooth(z1 - 3.0, z1, z) * (z1 > 1.0)                                                                                              # strongest where the water leaves a ledge, gone 3 m below it (not blotches under every overhang)
    rgb *= (1.0 - streak * (0.03 + 0.11 * fall))[:, None]
    roof = up * wall * (z0 > 0.5); st = smooth(0.35, 0.75, fbm(P, 0.45, 3, 33 + seed)); rgb = rgb * (1.0 - 0.30 * (roof * st)[:, None]) + DUST[None, :] * (0.16 * roof * st)[:, None]
    dirt = (1.0 - aot) * wall; rgb = rgb * (1.0 - 0.12 * dirt[:, None]) + (DUST * 0.55)[None, :] * (0.06 * dirt[:, None])                      # dirt gathers where the air does not move
    rgb *= (0.46 + 0.54 * aot ** 0.9)[:, None]                                                                                                # the occlusion itself
    rgb = np.clip(rgb, 0.0, 1.0); smo = np.clip(smo, 0.02, 0.98)
    peak = rgb.max(axis=1); rgb *= np.minimum(1.0, ALBEDO_MAX / np.maximum(peak, 1e-6))[:, None]                                            # physically plausible albedo (hue kept)

    # ---- into the atlas; the normal map island by island (a gradient must not cross an island's border)
    base = np.zeros((S, S, 4)); mean = rgb.mean(axis=0); base[..., :3] = mean; base[..., 3] = 0.2; base[ay, ax, :3] = rgb; base[ay, ax, 3] = smo
    nrm = np.zeros((S, S, 3)); nrm[..., 2] = 1.0
    for i, (s, iw, ih) in enumerate(slices):
        H = hgt[s].reshape(ih, iw)
        if turned[i]: H = H.T
        p = pp[i]; gy, gx = (np.gradient(H) if min(H.shape) > 1 else (np.zeros_like(H), np.zeros_like(H))); k = p * 1.0
        n3 = np.stack([-gx * k, -gy * k, np.ones_like(H)], axis=2); n3 /= np.linalg.norm(n3, axis=2, keepdims=True); px, py = pos[i]; nrm[py:py + H.shape[0], px:px + H.shape[1]] = n3
    # ---- what was painted, by source colour (square metres of visible surface): the tests check palette rules on this, since the materials themselves are gone
    painted = {}
    for F in frames:
        k = keys[F["key"]]; painted[k] = painted.get(k, 0.0) + F["face"].calc_area()
    # ---- UVs
    uvl = bm.loops.layers.uv.verify()
    for i, F in enumerate(frames):
        px, py = pos[i]; p = pp[i]
        for l in F["face"].loops:
            lu = (l.vert.co.dot(F["U"]) - F["u0"]) * p + pad; lv = (l.vert.co.dot(F["V"]) - F["v0"]) * p + pad
            if dens[i] <= 0.0: lu = lv = pad + 1.0
            l[uvl].uv = ((px + lv) / S, (py + lu) / S) if turned[i] else ((px + lu) / S, (py + lv) / S)
    return {"base": base, "normal": nrm * 0.5 + 0.5, "ppm": ppm, "faces": len(frames), "fill": N / float(S * S), "ao_mean": float(aot[wall].mean()) if wall.any() else 1.0, "buried_faces_removed": removed, "coplanar_separated": moved, "coplanar_left_same_colour": len(left) - mixed, "coplanar_left_mixed": mixed,
            "painted_m2": {k: round(v, 2) for k, v in sorted(painted.items())}}


def _image(name, px, linear=False, alpha=False):
    old = bpy.data.images.get(name)
    if old is not None: bpy.data.images.remove(old)
    h, w, ch = px.shape; img = bpy.data.images.new(name, w, h, alpha=alpha)
    if ch == 3: px = np.concatenate([px, np.ones((h, w, 1))], axis=2)
    img.pixels.foreach_set(px.astype(np.float32).ravel()); img.alpha_mode = "CHANNEL_PACKED" if alpha else "NONE"; img.pack(); img.filepath_raw = f"//{name}.png"; img.file_format = "PNG"
    img.colorspace_settings.name = "Non-Color" if linear else "sRGB"; return img


def _half(n):
    """The normal map at half the resolution of the base map (2 x 2 texels averaged, renormalised): the relief it carries is joints, slats and frames, and half the size is a quarter of the memory."""
    S = n.shape[0]
    if S < 128: return n
    v = n.reshape(S // 2, 2, S // 2, 2, 3).mean(axis=(1, 3)) * 2.0 - 1.0; v /= np.maximum(np.linalg.norm(v, axis=2, keepdims=True), 1e-9); return v * 0.5 + 0.5


def material_key(asset_id, lod, version=1): return f"sb_{asset_id}_v{int(version):03d}_l{lod}"


def texture_names(asset_id, lod, version=1):
    t = f"T_{asset_id}_v{int(version):03d}_l{lod}"; return f"{t}_BaseColor", f"{t}_Normal"


def apply(part, asset_id, lod, size, ground=True, version=1, rules=1):
    """Bakes the Part and replaces all its materials by the one atlas material `M_sb_<asset>_v<NNN>_l<lod>` (base colour + smoothness in alpha, normal map). Returns the bake's numbers. The version is in every name because
    the private library is append-only: a later version must not reuse a material or texture name with other content."""
    res = bake(part, size, ground, rules=rules); key = material_key(asset_id, lod, version); bn, nn = texture_names(asset_id, lod, version)
    AM.LIBRARY[key] = ((0.8, 0.8, 0.8), 0.8, 0.0, 0.0, f"surface bake of {asset_id} LOD{lod}: painted atlas ({size} px, {res['ppm']:.0f} px per metre on walls), ambient occlusion and weathering baked in")
    AM.TEXTURED[key] = {"texture": bn, "normal": nn, "smoothness_alpha": True}
    old = bpy.data.materials.get(f"M_{key}")
    if old is not None: bpy.data.materials.remove(old)
    m = AM.get(key, bpy); nt = m.node_tree; bsdf = nt.nodes["Principled BSDF"]
    for n in list(nt.nodes):
        if n.type in ("TEX_NOISE", "VALTORGB", "TEX_COORD", "TEX_IMAGE", "UVMAP", "NORMAL_MAP", "MATH"): nt.nodes.remove(n)
    uv = nt.nodes.new("ShaderNodeUVMap"); t1 = nt.nodes.new("ShaderNodeTexImage"); t1.image = _image(bn, res["base"], alpha=True); t2 = nt.nodes.new("ShaderNodeTexImage"); t2.image = _image(nn, _half(res["normal"]), linear=True)
    nm = nt.nodes.new("ShaderNodeNormalMap"); nm.inputs["Strength"].default_value = 1.0; inv = nt.nodes.new("ShaderNodeMath"); inv.operation = "SUBTRACT"; inv.inputs[0].default_value = 1.0
    nt.links.new(uv.outputs["UV"], t1.inputs["Vector"]); nt.links.new(uv.outputs["UV"], t2.inputs["Vector"]); nt.links.new(t1.outputs["Color"], bsdf.inputs["Base Color"])
    nt.links.new(t1.outputs["Alpha"], inv.inputs[1]); nt.links.new(inv.outputs["Value"], bsdf.inputs["Roughness"]); nt.links.new(t2.outputs["Color"], nm.inputs["Color"]); nt.links.new(nm.outputs["Normal"], bsdf.inputs["Normal"])
    for f in part.bm.faces: f.material_index = 0
    part.keys = [key]
    if not hasattr(part, "_fixed"): part._fixed = set()
    part._fixed.update(part.bm.faces)
    return {k: v for k, v in res.items() if k not in ("base", "normal")}
