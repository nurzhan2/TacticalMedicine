"""Retint the Mixamo "Swat Guy" textures toward a ВС РК olive/khaki digital camo look and downscale for Quest.

Usage (system python with Pillow + numpy):
    python tools/blender/retint_textures.py <extracted_dir> <out_dir>

Inputs  (from extract step): Ch15_100{1,2}_{Diffuse,Normal,Glossiness}.png (4096^2)
Outputs: Erlan_Body{1,2}_Albedo.jpg (2048), Erlan_Body{1,2}_Normal.png (1024),
         Erlan_Body{1,2}_MetalSmooth.png (1024, R=metallic 0, A=smoothness)

The original is an urban grey digital camo with black riot gear. The mapping works on luminance:
    - low-saturation mid/high luminance (the camo)   -> olive / khaki / tan / dark-olive palette, varied by
      large-scale noise so neighbouring areas lean green or brown (multicam-like at distance)
    - low-saturation dark pixels (vest, pads, helmet) -> ranger green / coyote (still dark, reads as gear)
    - saturated pixels (small coloured details) are kept, slightly desaturated.
"""
import os
import sys

import numpy as np
from PIL import Image, ImageFilter

ALBEDO_SIZE = 2048
AUX_SIZE = 1024


def smoothstep(e0, e1, x):
    t = np.clip((x - e0) / (e1 - e0), 0.0, 1.0)
    return t * t * (3 - 2 * t)


def value_noise(size, cells, seed):
    rng = np.random.default_rng(seed)
    g = rng.random((cells + 1, cells + 1)).astype(np.float32)
    img = Image.fromarray((g * 255).astype(np.uint8)).resize((size, size), Image.BICUBIC)
    return np.asarray(img, dtype=np.float32) / 255.0


def retint(rgb, seed):
    h, w, _ = rgb.shape
    lum = rgb @ np.array([0.299, 0.587, 0.114], dtype=np.float32)
    mx = rgb.max(axis=2)
    mn = rgb.min(axis=2)
    sat = np.where(mx > 1e-4, (mx - mn) / np.maximum(mx, 1e-4), 0)

    # multicam-ish large blobs: 0 = green lean, 1 = brown lean
    n = 0.6 * value_noise(w, 7, seed) + 0.4 * value_noise(w, 19, seed + 1)
    n = smoothstep(0.35, 0.65, n)[..., None]

    # camo palette by luminance (dark olive -> olive -> khaki -> tan)
    stops_l = np.array([0.0, 0.25, 0.45, 0.65, 1.0], dtype=np.float32)
    green = np.array([[0.10, 0.11, 0.07], [0.21, 0.23, 0.14], [0.33, 0.34, 0.22],
                      [0.47, 0.46, 0.33], [0.62, 0.59, 0.45]], dtype=np.float32)
    brown = np.array([[0.11, 0.09, 0.06], [0.25, 0.21, 0.14], [0.39, 0.33, 0.23],
                      [0.53, 0.47, 0.35], [0.68, 0.61, 0.47]], dtype=np.float32)

    def palette(table, l):
        out = np.empty(l.shape + (3,), dtype=np.float32)
        for c in range(3):
            out[..., c] = np.interp(l, stops_l, table[:, c])
        return out

    # stretch the camo contrast a little (original is very grey)
    lc = np.clip((lum - 0.12) / 0.6, 0, 1)
    camo = palette(green, lc) * (1 - n) + palette(brown, lc) * n

    # dark gear: ranger green / coyote with the original shading kept
    gear_col = np.array([0.95, 1.0, 0.70], dtype=np.float32) * (1 - n) + np.array([1.05, 0.88, 0.62], dtype=np.float32) * n
    gear = np.clip(lum[..., None] * 2.1 * gear_col + 0.02, 0, 1)

    t_camo = smoothstep(0.13, 0.24, lum)[..., None]
    grey = camo * t_camo + gear * (1 - t_camo)

    keep = smoothstep(0.18, 0.35, sat)[..., None]
    kept = rgb * 0.8 + lum[..., None] * 0.2
    out = grey * (1 - keep) + kept * keep

    # grime: darken randomly, dirt toward the lower parts of the UV islands is unknown -> uniform noise
    grime = 0.88 + 0.12 * value_noise(w, 40, seed + 7)[..., None]
    return np.clip(out * grime, 0, 1)


def load(path, size, mode="RGB"):
    im = Image.open(path).convert(mode)
    if im.size[0] != size:
        im = im.resize((size, size), Image.LANCZOS)
    return im


def main():
    src, dst = sys.argv[1], sys.argv[2]
    os.makedirs(dst, exist_ok=True)
    for i, tag in ((1, "1001"), (2, "1002")):
        a = load(os.path.join(src, f"Ch15_{tag}_Diffuse.png"), ALBEDO_SIZE)
        rgb = np.asarray(a, dtype=np.float32) / 255.0
        rgb = retint(rgb, seed=11 * i)
        Image.fromarray((rgb * 255 + 0.5).astype(np.uint8)).save(
            os.path.join(dst, f"Erlan_Body{i}_Albedo.jpg"), quality=90, optimize=True)

        nrm = load(os.path.join(src, f"Ch15_{tag}_Normal.png"), AUX_SIZE)
        nrm.save(os.path.join(dst, f"Erlan_Body{i}_Normal.png"), optimize=True)

        gl_path = os.path.join(src, f"Ch15_{tag}_Glossiness.png")
        gloss = load(gl_path, AUX_SIZE, "L") if os.path.exists(gl_path) else Image.new("L", (AUX_SIZE,) * 2, 60)
        g = np.asarray(gloss, dtype=np.float32) * 0.55  # fabric is matte; original gloss is too shiny for cloth
        ms = np.zeros((AUX_SIZE, AUX_SIZE, 4), dtype=np.uint8)
        ms[..., 3] = np.clip(g, 0, 255).astype(np.uint8)
        Image.fromarray(ms, "RGBA").save(os.path.join(dst, f"Erlan_Body{i}_MetalSmooth.png"), optimize=True)
        print("retinted", tag)


if __name__ == "__main__":
    main()
