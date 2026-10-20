"""w10b: first-person hands from the owner's soldier (Soldier_KZ): gloved hand + ~14 cm of camo sleeve per side,
skinned to Bip01 <S> Hand + finger bones only (the sleeve rides rigidly on the Hand bone).
blender -b --python tools/blender/fp_hands_extract.py -- <Soldier_KZ.fbx> <out_dir> [sleeve_m=0.14]
Writes <out_dir>/FPHands_L.fbx, FPHands_R.fbx (never touches the source file)."""
import sys, os, bpy, bmesh
from mathutils import Vector

argv = sys.argv[sys.argv.index("--") + 1:]
src, out_dir = argv[0], argv[1]
sleeve = float(argv[2]) if len(argv) > 2 else 0.14
os.makedirs(out_dir, exist_ok=True)


def load():
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.fbx(filepath=src, automatic_bone_orientation=False, ignore_leaf_bones=False)
    arm = next(o for o in bpy.data.objects if o.type == "ARMATURE")
    me = next(o for o in bpy.data.objects if o.type == "MESH")
    return arm, me


def extract(S):
    arm, ob = load()
    pre = f"Bip01 {S} "
    hand_names = [pre + "Hand"] + [b.name for b in arm.data.bones if b.name.startswith(pre + "Finger")]
    fore_name = pre + "Forearm"
    mw = arm.matrix_world
    wrist = mw @ arm.data.bones[pre + "Hand"].head_local
    elbow = mw @ arm.data.bones[fore_name].head_local
    axis = (wrist - elbow).normalized()  # elbow -> wrist
    gi = {g.name: g.index for g in ob.vertex_groups}
    hand_idx = {gi[n] for n in hand_names if n in gi}
    fore_idx = gi.get(fore_name)
    omw = ob.matrix_world
    keep = []
    for v in ob.data.vertices:
        tot = sum(g.weight for g in v.groups)
        if tot <= 0:
            keep.append(False); continue
        wh = sum(g.weight for g in v.groups if g.group in hand_idx) / tot
        wf = sum(g.weight for g in v.groups if g.group == fore_idx) / tot
        p = omw @ v.co
        back = (wrist - p).dot(axis)  # metres from the wrist toward the elbow
        k = wh > 0.5 or (wh + wf > 0.8 and back < sleeve + 0.07)
        keep.append(k)
    # faces: keep a face only if all its verts are kept
    bpy.context.view_layer.objects.active = ob
    bpy.ops.object.mode_set(mode="EDIT")
    bm = bmesh.from_edit_mesh(ob.data)
    bm.verts.ensure_lookup_table()
    dead = [f for f in bm.faces if not all(keep[v.index] for v in f.verts)]
    bmesh.ops.delete(bm, geom=dead, context="FACES")
    loose = [v for v in bm.verts if not v.link_faces]
    bmesh.ops.delete(bm, geom=loose, context="VERTS")
    # clean planar sleeve cut, perpendicular to the forearm, <sleeve> metres from the wrist
    inv = omw.inverted()
    co = inv @ (wrist - axis * sleeve)
    no = (inv.to_3x3() @ axis).normalized()   # points toward the hand: clear the elbow side
    geom = bm.verts[:] + bm.edges[:] + bm.faces[:]
    bmesh.ops.bisect_plane(bm, geom=geom, plane_co=co, plane_no=no, clear_inner=True, clear_outer=False)
    loose = [v for v in bm.verts if not v.link_faces]
    bmesh.ops.delete(bm, geom=loose, context="VERTS")
    # cap the sleeve cut: boundary loops whose verts all lie near the cut plane
    bnd = [e for e in bm.edges if e.is_boundary]
    print(f"[fp_hands] {S}: boundary edges {len(bnd)}")
    capped = 0
    seen = set()
    for e in bnd:
        if e in seen:
            continue
        # walk the loop
        loop = [e]; seen.add(e); stack = [e]
        while stack:
            cur = stack.pop()
            for v in cur.verts:
                for e2 in v.link_edges:
                    if e2.is_boundary and e2 not in seen:
                        seen.add(e2); loop.append(e2); stack.append(e2)
        vs = {v for ed in loop for v in ed.verts}
        backs = [(wrist - omw @ v.co).dot(axis) for v in vs]
        print(f"[fp_hands] {S}: loop {len(loop)} edges, back {min(backs):.3f}..{max(backs):.3f}")
        if len(loop) >= 6 and min(backs) > sleeve - 0.06:
            res = bmesh.ops.edgeloop_fill(bm, edges=loop, mat_nr=loop[0].link_faces[0].material_index, use_smooth=False)
            # recess the cap into the sleeve (a shallow inward cone) and map it to a black texel of the body
            # albedo (unused corner at uv 0.07,0.99): reads as the dark inside of the sleeve, not a flat lid
            poked = bmesh.ops.poke(bm, faces=res["faces"])
            inv = omw.inverted().to_3x3()
            for v in poked["verts"]:
                v.co += inv @ (axis * 0.035)
            uvl = bm.loops.layers.uv.active
            for f in poked["faces"]:
                for l in f.loops:
                    l[uvl].uv = (0.0703, 0.992)
            capped += 1
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces[:])
    bmesh.update_edit_mesh(ob.data)
    bpy.ops.object.mode_set(mode="OBJECT")
    print(f"[fp_hands] {S}: capped {capped} loop(s)")
    # weights: everything not on a finger bone goes to the Hand bone
    hg = ob.vertex_groups[pre + "Hand"]
    for v in ob.data.vertices:
        if not v.groups or sum(g.weight for g in v.groups) <= 0:
            hg.add([v.index], 1.0, "REPLACE")
            continue
        tot = sum(g.weight for g in v.groups if g.group in hand_idx)
        other = sum(g.weight for g in v.groups if g.group not in hand_idx)
        if other > 0:
            hg.add([v.index], other, "ADD")
    for g in list(ob.vertex_groups):
        if g.index not in hand_idx and g.name not in hand_names:
            ob.vertex_groups.remove(g)
    bpy.ops.object.vertex_group_normalize_all(lock_active=False) if False else None
    # drop unused material slots
    used = {p.material_index for p in ob.data.polygons}
    for i in reversed(range(len(ob.data.materials))):
        if i not in used:
            ob.active_material_index = i
            bpy.ops.object.material_slot_remove()
    # armature: keep only Hand + fingers
    bpy.context.view_layer.objects.active = arm
    bpy.ops.object.mode_set(mode="EDIT")
    eb = arm.data.edit_bones
    for b in list(eb):
        if b.name not in hand_names:
            eb.remove(b)
    bpy.ops.object.mode_set(mode="OBJECT")
    for o in list(bpy.data.objects):
        if o not in (arm, ob):
            bpy.data.objects.remove(o)
    ob.name = f"FPHand_{S}"
    tris = sum(len(p.vertices) - 2 for p in ob.data.polygons)
    print(f"[fp_hands] {S}: verts {len(ob.data.vertices)} tris {tris} mats {[m.name for m in ob.data.materials]} bones {len(arm.data.bones)}")
    dst = os.path.join(out_dir, f"FPHands_{S}.fbx")
    bpy.ops.export_scene.fbx(filepath=dst, use_selection=False, add_leaf_bones=False, bake_anim=False,
                             apply_scale_options="FBX_SCALE_ALL", mesh_smooth_type="FACE", use_armature_deform_only=False,
                             primary_bone_axis="Y", secondary_bone_axis="X", path_mode="STRIP")
    print("[fp_hands] wrote", dst)


for S in ("L", "R"):
    extract(S)
