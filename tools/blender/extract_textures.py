# Extracts packed textures of the Mixamo skin FBX to a folder: blender -b --python extract_textures.py -- in.fbx outdir
import bpy, sys, os
argv = sys.argv[sys.argv.index("--")+1:]
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.fbx(filepath=argv[0])
os.makedirs(argv[1], exist_ok=True)
for img in bpy.data.images:
    if img.packed_file is None: continue
    name = os.path.basename(img.filepath.replace("\\", "/"))
    with open(os.path.join(argv[1], name), "wb") as f:
        f.write(img.packed_file.data)
    print("EXTRACTED", name)
