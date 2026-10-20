# w9d: prepares Quest textures for the Astra item deliveries.
# python -I tools/art_pack_textures.py <Incoming dir> <Assets/Art/Items dir> [max=1024]
# Per folder: <folder>_BaseColor.png, <folder>_Normal.png (resized), <folder>_Mask.png packed for URP Lit:
#   R = metallic, G = ambient occlusion, B = 0, A = smoothness (1 - roughness)  -> used as _MetallicGlossMap AND _OcclusionMap (.g)
import sys, os, glob
from PIL import Image
import numpy as np
src_root, dst_root = sys.argv[1], sys.argv[2]
cap = int(sys.argv[3]) if len(sys.argv) > 3 else 1024

def find(folder, key):
    for f in sorted(glob.glob(os.path.join(folder, "Textures", "*.png"))):
        n = os.path.basename(f).lower()
        if key in n and "urp" not in n and "unity" not in n and "orm" != n[:3]:
            return f
    return None

def load(path, mode, size):
    im = Image.open(path).convert(mode)
    if im.size[0] > size: im = im.resize((size, size), Image.LANCZOS)
    return im

for folder in sorted(os.listdir(src_root)):
    full = os.path.join(src_root, folder)
    if not os.path.isdir(full): continue
    bc = find(full, "basecolor")
    if bc is None: print("[TEX] skip", folder); continue
    out = os.path.join(dst_root, folder, "Textures"); os.makedirs(out, exist_ok=True)
    size = min(cap, Image.open(bc).size[0])
    b = load(bc, "RGBA", size)
    a = np.asarray(b)[:, :, 3]
    aop = find(full, "ambientocclusion")
    if aop is not None:                         # AO baked into the albedo (no occlusion-map shader variant on Quest)
        ao = np.asarray(load(aop, "L", size)).astype(np.float32) / 255.0
        rgba = np.asarray(b).astype(np.float32)
        rgba[:, :, :3] *= (1.0 - 0.7 * (1.0 - ao))[:, :, None]
        b = Image.fromarray(np.clip(rgba, 0, 255).astype(np.uint8), "RGBA")
    if a.min() > 250: b = b.convert("RGB")      # no alpha needed
    b.save(os.path.join(out, folder + "_BaseColor.png"), optimize=True)
    nm = find(full, "normal")
    if nm: load(nm, "RGB", size).save(os.path.join(out, folder + "_Normal.png"), optimize=True)
    def ch(key, default):
        p = find(full, key)
        if p is None: return np.full((size, size), default, np.uint8)
        return np.asarray(load(p, "L", size))
    met = ch("metallic", 0); ao = ch("ambientocclusion", 255); rough = ch("roughness", 160)
    mask = np.stack([met, ao, np.zeros_like(met), 255 - rough], axis=2).astype(np.uint8)
    Image.fromarray(mask, "RGBA").save(os.path.join(out, folder + "_Mask.png"), optimize=True)
    print("[TEX]", folder, size, "alpha" if b.mode == "RGBA" else "")
