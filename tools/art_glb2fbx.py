"""glb/gltf -> fbx for the art importer (w9), keeping object names, hierarchy and origins (sub-parts such as
Strap / Windlass / BladeA must survive). Unlike bk_export.py nothing is joined, decimated or re-pivoted.

    blender -b --factory-startup --python tools/art_glb2fbx.py -- --in model.glb --out C:/.../tourniquet.fbx --tex DIR
"""
import argparse
import os
import sys

import bpy

argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
ap = argparse.ArgumentParser()
ap.add_argument("--in", dest="inp", required=True)
ap.add_argument("--out", required=True)
ap.add_argument("--tex", default="")
args = ap.parse_args(argv)

bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.gltf(filepath=args.inp)

# unpack embedded images so Unity gets real texture files next to the fbx
tex_dir = args.tex or os.path.join(os.path.dirname(args.out), "Textures")
os.makedirs(tex_dir, exist_ok=True)
for img in bpy.data.images:
    if img.source != "FILE" and not img.packed_file:
        continue
    name = "".join(c if c.isalnum() or c in "-_" else "_" for c in img.name)[:48] or "tex"
    path = os.path.join(tex_dir, name + ".png")
    try:
        img.filepath_raw = path
        img.file_format = "PNG"
        img.save()
    except Exception as e:  # noqa: BLE001
        print("[art_glb2fbx] image", img.name, "not saved:", e)

os.makedirs(os.path.dirname(args.out), exist_ok=True)
bpy.ops.export_scene.fbx(
    filepath=args.out, use_selection=False, apply_unit_scale=True, apply_scale_options="FBX_SCALE_UNITS",
    axis_forward="-Z", axis_up="Y", object_types={"EMPTY", "MESH", "ARMATURE"}, bake_anim=False,
    path_mode="COPY", embed_textures=False, add_leaf_bones=False)
names = [o.name for o in bpy.data.objects]
print("[art_glb2fbx] objects:", ", ".join(names))
print("[art_glb2fbx] wrote", args.out)
