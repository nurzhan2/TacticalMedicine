# Headless Blender: dump objects of every SourceAssets/Incoming/*/*.fbx (names, parent, dims, tris, materials, origin).
# blender.exe -b --python tools/art_inventory.py -- <incoming_dir> <out_json>
import bpy, sys, os, json, glob
argv = sys.argv[sys.argv.index("--") + 1:]
root, out = argv[0], argv[1]
res = {}
for folder in sorted(os.listdir(root)):
    fbx = glob.glob(os.path.join(root, folder, "*.fbx"))
    if not fbx: continue
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.fbx(filepath=fbx[0])
    objs = []
    dg = bpy.context.evaluated_depsgraph_get()
    for o in bpy.context.scene.objects:
        d = {"name": o.name, "type": o.type, "parent": o.parent.name if o.parent else None,
             "loc": [round(v, 4) for v in o.matrix_world.translation],
             "rot": [round(v, 3) for v in o.matrix_world.to_euler()],
             "scale": [round(v, 4) for v in o.matrix_world.to_scale()],
             "dims": [round(v, 4) for v in o.dimensions]}
        if o.type == "MESH":
            m = o.evaluated_get(dg).to_mesh()
            m.calc_loop_triangles()
            d["tris"] = len(m.loop_triangles)
            d["mats"] = [s.material.name if s.material else None for s in o.material_slots]
            import mathutils
            ws = [o.matrix_world @ v.co for v in m.vertices]
            if ws:
                mn = [min(p[i] for p in ws) for i in range(3)]; mx = [max(p[i] for p in ws) for i in range(3)]
                d["wmin"] = [round(v, 4) for v in mn]; d["wmax"] = [round(v, 4) for v in mx]
            o.evaluated_get(dg).to_mesh_clear()
        objs.append(d)
    imgs = sorted({i.name + ":%dx%d" % tuple(i.size) for i in bpy.data.images})
    res[folder] = {"file": os.path.basename(fbx[0]), "objects": objs, "images": imgs}
with open(out, "w", encoding="utf-8") as f:
    json.dump(res, f, indent=1, ensure_ascii=False)
print("[INV] done", len(res))
