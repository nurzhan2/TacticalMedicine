# Headless Blender: world-space layout of the objects in each SourceAssets/Incoming/*/*.blend (source of truth),
# to compare with the FBX import. blender -b --python tools/art_blend_inventory.py -- <incoming> <out_json>
import bpy, sys, os, json, glob
argv = sys.argv[sys.argv.index("--") + 1:]
root, out = argv[0], argv[1]
res = {}
for folder in sorted(os.listdir(root)):
    bl = glob.glob(os.path.join(root, folder, "*.blend"))
    if not bl: continue
    bpy.ops.wm.open_mainfile(filepath=bl[0], load_ui=False)
    objs = []
    for o in bpy.data.objects:
        d = {"name": o.name, "type": o.type, "parent": o.parent.name if o.parent else None,
             "wloc": [round(v, 4) for v in o.matrix_world.translation],
             "wrot": [round(v, 3) for v in o.matrix_world.to_euler()],
             "hide": o.hide_get() if o.name in bpy.context.view_layer.objects else None}
        if o.type == "MESH":
            ws = [o.matrix_world @ v.co for v in o.data.vertices]
            d["wmin"] = [round(min(p[i] for p in ws), 4) for i in range(3)]
            d["wmax"] = [round(max(p[i] for p in ws), 4) for i in range(3)]
            d["mods"] = [m.type for m in o.modifiers]
        objs.append(d)
    res[folder] = objs
json.dump(res, open(out, "w", encoding="utf-8"), indent=1)
print("[BINV] done")
