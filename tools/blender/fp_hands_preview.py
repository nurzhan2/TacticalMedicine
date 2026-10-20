"""w10b: preview render of the extracted FP hands (Eevee) + glove-frame measurements for GloveHand constants.
blender -b --python tools/blender/fp_hands_preview.py -- <dir with FPHands_L/R.fbx> <albedo.png> <out.png> [curl_deg]"""
import sys, math, bpy
from mathutils import Vector, Matrix

argv = sys.argv[sys.argv.index("--") + 1:]
d, albedo, out = argv[0], argv[1], argv[2]
curl = float(argv[3]) if len(argv) > 3 else 0.0
bpy.ops.wm.read_factory_settings(use_empty=True)
img = bpy.data.images.load(albedo)
mat = bpy.data.materials.new("prev"); mat.use_nodes = True
nt = mat.node_tree; bsdf = nt.nodes["Principled BSDF"]
tex = nt.nodes.new("ShaderNodeTexImage"); tex.image = img
nt.links.new(tex.outputs["Color"], bsdf.inputs["Base Color"]); bsdf.inputs["Roughness"].default_value = 0.8
for i, S in enumerate(("L", "R")):
    before = set(bpy.data.objects)
    bpy.ops.import_scene.fbx(filepath=f"{d}/FPHands_{S}.fbx", automatic_bone_orientation=False, ignore_leaf_bones=False)
    new = [o for o in bpy.data.objects if o not in before]
    arm = next(o for o in new if o.type == "ARMATURE"); me = next(o for o in new if o.type == "MESH")
    me.data.materials.clear(); me.data.materials.append(mat)
    pre = f"Bip01 {S} "
    B = lambda n: arm.matrix_world @ arm.pose.bones[pre + n].head
    wrist, idx, mid, pky = B("Hand"), B("Finger1"), B("Finger2"), B("Finger4")
    side = 1 if S == "R" else -1
    F = (mid - wrist).normalized()
    X = ((pky - idx) * side); X = (X - F * X.dot(F)).normalized()
    U = X.cross(F)  # Blender is right-handed: back of the hand = X x F (Unity, left-handed: Vector3.Cross(F, X))
    # palm side check: thumb (Finger0) should be below-ish / to the index side; print diagnostics
    th = B("Finger0")
    print(f"[fp_prev] {S}: wrist->mid {(mid-wrist).length:.4f}  thumb.U {(th-wrist).dot(U):.4f} thumb.X {(th-wrist).dot(X):.4f}")
    # mesh extents in the glove frame (origin wrist)
    dg = bpy.context.evaluated_depsgraph_get()
    ev = me.evaluated_get(dg); m = ev.to_mesh()
    pts = [(ev.matrix_world @ v.co) - wrist for v in m.vertices]
    loc = [(p.dot(X), p.dot(U), p.dot(F)) for p in pts]
    ys = [l[1] for l in loc if -0.11 < l[2] < -0.07]
    print(f"[fp_prev] {S}: z {min(l[2] for l in loc):.3f}..{max(l[2] for l in loc):.3f}  y {min(l[1] for l in loc):.3f}..{max(l[1] for l in loc):.3f}  x {min(l[0] for l in loc):.3f}..{max(l[0] for l in loc):.3f}  sleeve top y {max(ys) if ys else 0:.3f}")
    hy = [l[1] for l in loc if 0.0 < l[2] < 0.09]
    hx = [l[0] for l in loc if 0.0 < l[2] < 0.09]
    print(f"[fp_prev] {S}: palm region y {min(hy):.3f}..{max(hy):.3f} x {min(hx):.3f}..{max(hx):.3f}")
    ev.to_mesh_clear()
    # curl the fingers (preview only) around the hand's side axis
    if curl and S == "R":  # right hand curled (grip), left open
        for f in "01234":
            for s in ("", "1", "2"):
                pb = arm.pose.bones[pre + "Finger" + f + s]
                axis_w = -X  # curl axis: fingertips toward the palm (Unity: sign is found numerically)
                axis_l = (arm.matrix_world @ pb.bone.matrix_local).to_3x3().inverted() @ axis_w
                pb.rotation_mode = "QUATERNION"
                from mathutils import Quaternion
                pb.rotation_quaternion = Quaternion(axis_l.normalized(), math.radians(curl))
    # place: wrist at origin of a frame, hands side by side in front of the camera, palms down
    R = Matrix((X, U, F)).transposed()  # columns X,U,F
    # world: glove x -> world X, glove z (fingers) -> world Y, glove y (back) -> world Z
    T = Matrix.Translation(Vector(((0.11 if S == "R" else -0.11), 0, 0))) @ Matrix(((1,0,0,0),(0,0,1,0),(0,1,0,0),(0,0,0,1))) @ R.inverted().to_4x4() @ Matrix.Translation(-wrist)
    arm.matrix_world = T @ arm.matrix_world
bpy.context.view_layer.update()
sc = bpy.context.scene
sc.render.engine = "BLENDER_EEVEE" if "BLENDER_EEVEE" in [e.identifier for e in bpy.types.RenderSettings.bl_rna.properties["engine"].enum_items] else "BLENDER_EEVEE_NEXT"
sc.render.resolution_x, sc.render.resolution_y = 1200, 800
cam = bpy.data.objects.new("cam", bpy.data.cameras.new("cam")); sc.collection.objects.link(cam); sc.camera = cam
cam.data.lens = 28
cam.location = (0, -0.30, 0.40)
cam.rotation_euler = (math.radians(42), 0, 0)
sun = bpy.data.objects.new("sun", bpy.data.lights.new("sun", "SUN")); sc.collection.objects.link(sun)
sun.rotation_euler = (math.radians(40), math.radians(-20), math.radians(30)); sun.data.energy = 3.5
w = bpy.data.worlds.new("w"); sc.world = w; w.use_nodes = True
w.node_tree.nodes["Background"].inputs["Color"].default_value = (0.25, 0.27, 0.3, 1); w.node_tree.nodes["Background"].inputs["Strength"].default_value = 0.8
sc.render.filepath = out
bpy.ops.render.render(write_still=True)
print("[fp_prev] wrote", out)
