"""w10c: lighter LOD of the owner's soldier for the friendly infantry / rescuers.
blender -b --python tools/blender/soldier_lod.py -- <in.fbx> <out.fbx> <ratio>"""
import sys
import bpy

argv = sys.argv[sys.argv.index("--") + 1:]
src, dst, ratio = argv[0], argv[1], float(argv[2])
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.fbx(filepath=src, automatic_bone_orientation=False, ignore_leaf_bones=False)
tris_before = tris_after = 0
for ob in bpy.data.objects:
    if ob.type != "MESH":
        continue
    me = ob.data
    tris_before += sum(len(p.vertices) - 2 for p in me.polygons)
    bpy.context.view_layer.objects.active = ob
    mod = ob.modifiers.new("lod", "DECIMATE")
    mod.ratio = ratio
    mod.use_collapse_triangulate = True
    # keep the armature modifier last so the decimate works on the rest pose
    while ob.modifiers[0].name != "lod":
        bpy.ops.object.modifier_move_up(modifier="lod")
    bpy.ops.object.modifier_apply(modifier="lod")
    tris_after += sum(len(p.vertices) - 2 for p in ob.data.polygons)
print(f"[soldier_lod] tris {tris_before} -> {tris_after}")
bpy.ops.export_scene.fbx(filepath=dst, use_selection=False, add_leaf_bones=False, bake_anim=False,
                         apply_scale_options="FBX_SCALE_ALL", mesh_smooth_type="FACE", use_armature_deform_only=False,
                         primary_bone_axis="Y", secondary_bone_axis="X", path_mode="STRIP")
print("[soldier_lod] wrote", dst)
