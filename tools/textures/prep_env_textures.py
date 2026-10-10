"""Prepare Poly Haven 1k textures for the trench: pack smoothness (1-roughness) into albedo alpha where
available, darken/tint for night mud, copy normals. Usage: python -I prep_env_textures.py <src_dir> <dst_dir>"""
import os, sys, shutil
from PIL import Image, ImageEnhance, ImageOps

src, dst = sys.argv[1], sys.argv[2]
os.makedirs(dst, exist_ok=True)
# name -> (polyhaven id, smoothness scale, smoothness bias)
SETS = {
    "Env_Soil":   ("excavated_soil_wall", 0.45, 0.05),
    "Env_Mud":    ("brown_mud_02",        0.85, 0.15),
    "Env_Field":  ("burned_ground_01",    None, 0),
    "Env_Planks": ("raw_plank_wall",      None, 0),
    "Env_Bark":   ("bark_brown_02",       None, 0),
    "Env_Hessian":("hessian_230",         None, 0),
    "Env_Rust":   ("green_metal_rust",    None, 0),
}
for name, (pid, sscale, sbias) in SETS.items():
    diff = Image.open(os.path.join(src, f"{pid}_diff.jpg")).convert("RGB")
    rough = os.path.join(src, f"{pid}_rough.jpg")
    if sscale is not None and os.path.exists(rough):
        r = Image.open(rough).convert("L").resize(diff.size)
        sm = ImageOps.invert(r).point(lambda v: int(min(255, v * sscale + sbias * 255)))
        img = diff.copy(); img.putalpha(sm)
        img.save(os.path.join(dst, f"{name}_Albedo.png"), optimize=True)
    else:
        diff.save(os.path.join(dst, f"{name}_Albedo.jpg"), quality=90)
    shutil.copy(os.path.join(src, f"{pid}_nor.jpg"), os.path.join(dst, f"{name}_Normal.jpg"))
    print("ok", name)
