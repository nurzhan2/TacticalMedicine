# w9d: re-export a delivered .blend to FBX WITHOUT "Apply Transform" (bake_space_transform), which in Astra's
# FMVR_IFAK.fbx moved nested children (InnerPanel_Lower, LowerExtension and their Slot_* empties) to wrong places.
# Works on a temp COPY of the .blend; the delivery stays untouched.
# blender -b --factory-startup --python tools/art_reexport_fbx.py -- <in.blend> <out.fbx>
import bpy, sys, shutil, os, tempfile
argv = sys.argv[sys.argv.index("--") + 1:]
src, out = argv[0], argv[1]
tmp = os.path.join(tempfile.gettempdir(), "fmvr_reexport_" + os.path.basename(src))
shutil.copy(src, tmp)
bpy.ops.wm.open_mainfile(filepath=tmp, load_ui=False)
for o in bpy.data.objects:
    o.hide_set(False); o.hide_viewport = False; o.hide_select = False
bpy.ops.object.select_all(action="SELECT")
bpy.ops.export_scene.fbx(filepath=out, use_selection=False, apply_unit_scale=True, apply_scale_options="FBX_SCALE_ALL",
    axis_forward="-Z", axis_up="Y", bake_space_transform=False, object_types={"MESH", "EMPTY"},
    use_mesh_modifiers=True, mesh_smooth_type="FACE", use_tspace=True, add_leaf_bones=False, bake_anim=False,
    path_mode="STRIP", embed_textures=False)
os.remove(tmp)
print("[REEXPORT] ok", out)
