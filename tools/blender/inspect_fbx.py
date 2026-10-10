import bpy, sys
argv = sys.argv[sys.argv.index("--")+1:]
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.fbx(filepath=argv[0])
for o in bpy.data.objects:
    info = ""
    if o.type == 'MESH':
        me = o.data
        tris = sum(len(p.vertices)-2 for p in me.polygons)
        info = f"verts={len(me.vertices)} tris={tris} mats={[m.name for m in me.materials]} vgroups={len(o.vertex_groups)}"
    print("OBJ", o.name, o.type, o.parent.name if o.parent else None, tuple(round(v,3) for v in o.dimensions), info)
for img in bpy.data.images:
    print("IMG", img.name, img.size[:], img.packed_file is not None, img.filepath)
for m in bpy.data.materials:
    if m.use_nodes:
        print("MAT", m.name, [(n.type, n.image.name if getattr(n,'image',None) else '') for n in m.node_tree.nodes])
arm = [o for o in bpy.data.objects if o.type=='ARMATURE']
if arm:
    print("BONES", [b.name for b in arm[0].data.bones])
    print("ARM scale", arm[0].scale[:], arm[0].rotation_euler[:])
print("ACTIONS", [(a.name, a.frame_range[:]) for a in bpy.data.actions])
