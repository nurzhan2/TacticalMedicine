# Quick textured workbench preview of an FBX: blender -b --python render_preview.py -- in.fbx out_prefix
import bpy, sys, math, mathutils
argv = sys.argv[sys.argv.index("--")+1:]
bpy.ops.wm.read_factory_settings(use_empty=True)
ext = argv[0].lower().rsplit(".",1)[-1]
if ext == "fbx": bpy.ops.import_scene.fbx(filepath=argv[0])
else: bpy.ops.wm.open_mainfile(filepath=argv[0])
import os
texdir = argv[3] if len(argv) > 3 else None
if texdir:
    for img in bpy.data.images:
        cand = os.path.join(texdir, os.path.basename(img.filepath.replace("\\", "/")))
        if os.path.exists(cand): img.filepath = cand; img.reload()
sc = bpy.context.scene
sc.frame_set(int(argv[2]) if len(argv) > 2 else 1)
sc.render.engine = 'BLENDER_WORKBENCH'
sc.display.shading.light = 'STUDIO'
sc.display.shading.color_type = 'TEXTURE'
sc.render.resolution_x = 700; sc.render.resolution_y = 900
meshes = [o for o in sc.objects if o.type == 'MESH']
mn = mathutils.Vector((1e9,)*3); mx = -mn
for o in meshes:
    for c in o.bound_box:
        w = o.matrix_world @ mathutils.Vector(c)
        mn = mathutils.Vector(map(min, mn, w)); mx = mathutils.Vector(map(max, mx, w))
ctr = (mn+mx)/2; size = max(mx-mn)
cam = bpy.data.objects.new("cam", bpy.data.cameras.new("cam")); sc.collection.objects.link(cam); sc.camera = cam
for name, d in [("front", (0,-1,0.15)), ("back", (0,1,0.15)), ("side", (1,-0.3,0.2))]:
    d = mathutils.Vector(d).normalized()
    cam.location = ctr + d*size*2.0
    cam.rotation_euler = (ctr - cam.location).to_track_quat('-Z','Y').to_euler()
    cam.data.lens = 50
    sc.render.filepath = f"{argv[1]}_{name}.png"
    bpy.ops.render.render(write_still=True)
