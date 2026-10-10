"""Procedural blood & wound textures (no third-party art needed).

    python tools/make_blood_textures.py <out_dir>

BloodPool.png      512  RGBA  irregular pool, glossy wet centre, darker clotted rim (alpha = coverage)
BloodSplat.png     256  RGBA  drip/splat for floor & cloth impacts
BloodDrop.png       64  RGBA  white soft droplet for particles (tinted by material)
Wound_Fragment.png 256  RGBA  small fragment hole torn through fabric, blood ring
Wound_Ragged.png   512  RGBA  ragged laceration (thigh), exposed muscle, torn fabric edges
Wound_ChestEntry.png 256 RGBA small round entry with abrasion collar
Wound_Exit.png     256  RGBA  larger stellate exit wound
SoakNoise.png      256  L     tileable value noise for organic soak edges in the casualty shader
"""
import math
import os
import sys

import numpy as np
from PIL import Image, ImageFilter

rng = np.random.default_rng(42)


def tile_noise(size, cells, seed):
    r = np.random.default_rng(seed)
    g = r.random((cells, cells)).astype(np.float32)
    g = np.concatenate([g, g[:1]], 0)
    g = np.concatenate([g, g[:, :1]], 1)
    im = Image.fromarray((g * 255).astype(np.uint8)).resize((size + size // cells, size + size // cells), Image.BICUBIC)
    a = np.asarray(im, dtype=np.float32)[:size, :size] / 255.0
    return a


def fbm(size, seed, octaves=(4, 8, 16, 32), gains=(0.5, 0.25, 0.15, 0.1)):
    n = sum(g * tile_noise(size, c, seed + i) for i, (c, g) in enumerate(zip(octaves, gains)))
    return (n - n.min()) / (n.max() - n.min() + 1e-6)


def polar(size):
    y, x = np.mgrid[0:size, 0:size].astype(np.float32)
    cx = cy = (size - 1) / 2
    dx, dy = (x - cx) / (size / 2), (y - cy) / (size / 2)
    return np.sqrt(dx * dx + dy * dy), np.arctan2(dy, dx), dx, dy


def angular_noise(theta, lobes, amp, seed):
    r = np.random.default_rng(seed)
    out = np.zeros_like(theta)
    for k in range(1, lobes + 1):
        out += r.uniform(-1, 1) * np.sin(k * theta + r.uniform(0, 6.28)) / k
    return 1 + amp * out


def smooth(e0, e1, x):
    t = np.clip((x - e0) / (e1 - e0), 0, 1)
    return t * t * (3 - 2 * t)


def save_rgba(path, rgb, a):
    arr = np.dstack([np.clip(rgb, 0, 1), np.clip(a, 0, 1)])
    Image.fromarray((arr * 255 + 0.5).astype(np.uint8), "RGBA").save(path, optimize=True)


FRESH = np.array([0.42, 0.02, 0.02])
DARK = np.array([0.16, 0.01, 0.01])
CLOT = np.array([0.08, 0.01, 0.008])
MUSCLE = np.array([0.55, 0.08, 0.07])
FAT = np.array([0.80, 0.68, 0.45])


def blood_pool(size, seed):
    d, th, _, _ = polar(size)
    n = fbm(size, seed)
    edge = 0.78 * angular_noise(th, 9, 0.35, seed) + (n - 0.5) * 0.25
    a = smooth(edge, edge - 0.06, d)
    rim = smooth(edge - 0.18, edge - 0.03, d) * a
    rgb = FRESH * (0.75 + 0.25 * n[..., None]) * (1 - rim[..., None]) + CLOT * rim[..., None]
    # satellite droplets
    for _ in range(14):
        ang = rng.uniform(0, 6.28)
        rr = rng.uniform(0.8, 0.95)
        cx, cy = (0.5 + 0.5 * rr * math.cos(ang)) * size, (0.5 + 0.5 * rr * math.sin(ang)) * size
        rad = rng.uniform(0.008, 0.03) * size
        y, x = np.mgrid[0:size, 0:size]
        dd = np.sqrt((x - cx) ** 2 + (y - cy) ** 2)
        da = smooth(rad, rad * 0.6, dd)
        a = np.maximum(a, da)
        rgb = rgb * (1 - da[..., None]) + DARK * da[..., None]
    return rgb, a


def wound(size, seed, hole, ring, torn, stellate=0, muscle=0.0, elong=1.0):
    d, th, dx, dy = polar(size)
    d = np.sqrt((dx / elong) ** 2 + dy ** 2) if elong != 1.0 else d
    n = fbm(size, seed)
    ang = angular_noise(th, 7, 0.25, seed)
    if stellate:
        ang *= 1 + 0.35 * np.maximum(0, np.cos(stellate * th + seed)) ** 4
    hole_e = hole * ang + (n - 0.5) * 0.06
    ring_e = ring * angular_noise(th, 11, 0.4, seed + 1) + (n - 0.5) * 0.25
    torn_e = torn * angular_noise(th, 17, 0.3, seed + 2)
    in_hole = smooth(hole_e, hole_e * 0.8, d)
    in_ring = smooth(ring_e, ring_e - 0.08, d)
    in_torn = smooth(torn_e, torn_e - 0.02, d) * (1 - in_hole)
    rgb = DARK * (0.7 + 0.6 * n[..., None]) * in_ring[..., None]
    a = in_ring * 0.92
    # torn fabric fibres: dark, frayed band around the hole
    fray = in_torn * (0.5 + 0.5 * (np.sin(th * 60 + n * 10) > 0))
    rgb = rgb * (1 - fray[..., None]) + np.array([0.09, 0.05, 0.03]) * fray[..., None]
    a = np.maximum(a, fray)
    # hole: deep, almost black red, with muscle/fat visible on the walls
    wall = smooth(hole_e * 0.45, hole_e * 0.95, d) * in_hole
    inner = CLOT * in_hole[..., None]
    if muscle > 0:
        mus = (MUSCLE * (0.6 + 0.6 * n[..., None])) * wall[..., None] * muscle
        fat = FAT * (smooth(0.55, 0.75, n) * wall)[..., None] * 0.35 * muscle
        inner = inner * (1 - wall[..., None] * muscle) + mus + fat
    rgb = rgb * (1 - in_hole[..., None]) + inner
    a = np.maximum(a, in_hole)
    # wet highlight streaks
    return rgb, a


def drop(size):
    d, _, _, _ = polar(size)
    a = smooth(1.0, 0.3, d)
    return np.ones((size, size, 3)), a


def main():
    out = sys.argv[1]
    os.makedirs(out, exist_ok=True)
    save_rgba(os.path.join(out, "BloodPool.png"), *blood_pool(512, 3))
    rgb, a = blood_pool(256, 9)
    save_rgba(os.path.join(out, "BloodSplat.png"), rgb * 0.8, a)
    save_rgba(os.path.join(out, "BloodDrop.png"), *drop(64))
    save_rgba(os.path.join(out, "Wound_Fragment.png"), *wound(256, 5, hole=0.16, ring=0.62, torn=0.3, muscle=0.6))
    save_rgba(os.path.join(out, "Wound_Ragged.png"), *wound(512, 6, hole=0.26, ring=0.58, torn=0.36, stellate=3, muscle=1.0, elong=1.6))
    save_rgba(os.path.join(out, "Wound_ChestEntry.png"), *wound(256, 7, hole=0.1, ring=0.4, torn=0.18, muscle=0.2))
    save_rgba(os.path.join(out, "Wound_Exit.png"), *wound(256, 8, hole=0.28, ring=0.8, torn=0.45, stellate=5, muscle=0.9))
    Image.fromarray((fbm(256, 21, (4, 8, 16, 32)) * 255).astype(np.uint8), "L").save(os.path.join(out, "SoakNoise.png"))
    print("[blood_textures] written to", out)


if __name__ == "__main__":
    main()
