"""Dry-season vegetation of the environmental kit (REGIONAL, northern Nigeria; the species that really grow along Kano's roads and compounds are NOT established, gaps M-07 and M-17): a large spreading shade
tree with a sparse dusty crown (built on the residential kit's trunk-and-branch tree with dry foliage cards) and a clump of dry grass made of blade cards. Real geometry; the foliage is alpha-clipped cards."""
from __future__ import annotations
import math, random
from mathutils import Vector
from kano_kit import veg as KV
from kano_kit.geo import Part


def tree_large_shade(lod: int = 0) -> Part:
    """A spreading shade tree, 11 m tall and 11 m across: the same trunk, scaffold branches and card crown as the street trees but a sparser crown of dusty olive foliage (dry season)."""
    return KV.tree(5, 11.0, 11.0, lod, cards=(120, 5, (0.9, 1.5)), leaf_key="leaf_dry_cutout")


def grass_clump(lod: int = 0) -> Part:
    """A clump of dry grass: 22 blade cards 0.45 to 0.65 m wide and 0.8 to 1.3 m tall, leaning outward, standing on the ground."""
    p = Part("grass"); rnd = random.Random(9)
    for k in range(22):
        th = rnd.uniform(0, 2 * math.pi); rr = rnd.uniform(0.0, 0.35); base = Vector((rr * math.cos(th), rr * math.sin(th), 0.0)); h = rnd.uniform(0.8, 1.3); w = rnd.uniform(0.45, 0.65)
        lean = Vector((math.cos(th), math.sin(th), 0.0)) * rnd.uniform(0.05, 0.30); up = (lean + Vector((0, 0, 1))).normalized(); right = Vector((-math.sin(th + rnd.uniform(-0.5, 0.5)), math.cos(th + rnd.uniform(-0.5, 0.5)), 0.0)).normalized()
        flip = rnd.random() < 0.5
        p.card(base + up * (h / 2), right, up, w, h, "grass_blade_cutout", (1, 0) if flip else (0, 0), (0, 1) if flip else (1, 1))
    return p
