import sys, bpy
argv = sys.argv[sys.argv.index("--") + 1:]
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.fbx(filepath=argv[0], automatic_bone_orientation=False, ignore_leaf_bones=False)
for ob in bpy.data.objects:
    print("OBJ", ob.name, ob.type, tuple(round(v,3) for v in ob.location), tuple(round(v,3) for v in ob.rotation_euler), tuple(round(v,3) for v in ob.scale), ob.parent.name if ob.parent else None)
    if ob.type == "MESH":
        me = ob.data
        print("  verts", len(me.vertices), "tris", sum(len(p.vertices)-2 for p in me.polygons), "mats", [m.name if m else None for m in me.materials], "uvs", [u.name for u in me.uv_layers])
        print("  vgroups", len(ob.vertex_groups))
    if ob.type == "ARMATURE":
        for b in ob.data.bones:
            if "Hand" in b.name or "Finger" in b.name or "Forearm" in b.name or "UpperArm" in b.name or "Twist" in b.name:
                hw = ob.matrix_world @ b.head_local; tw = ob.matrix_world @ b.tail_local
                print("  BONE", repr(b.name), "parent", b.parent.name if b.parent else None, "head", tuple(round(v,4) for v in hw), "tail", tuple(round(v,4) for v in tw))
for m in bpy.data.materials:
    imgs = [n.image.name for n in (m.node_tree.nodes if m.node_tree else []) if n.type=="TEX_IMAGE" and n.image]
    print("MAT", m.name, imgs)
