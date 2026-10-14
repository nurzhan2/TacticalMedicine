# Headless Blender exporter for third-party models (BlenderKit .blend / .glb / .gltf / .fbx / .obj).
# Usage:
#   blender.exe -b --factory-startup --disable-autoexec --python tools/bk_export.py -- \
#       --in model.blend --out <dir> --name <slug> [--max-faces 20000] [--tex 1024] [--rigged]
#       [--size L=6.5 | H=1.8 | W=2] [--pivot bottom|keep] [--keep-collection NAME]
# Output in <dir>: <slug>.fbx, Textures/*.png, <slug>.materials.json, <slug>.export.json (stats).
# Materials are re-described in JSON (albedo / normal / metallic-smoothness map + values) because the FBX
# material slot only carries a diffuse texture; Unity (Editor/LicensedModelSetup.cs) builds URP Lit from it.
# Principled inputs driven by procedural nodes are baked (Cycles, emission trick) to images.
import bpy, sys, os, json, math, argparse
import numpy as np
from mathutils import Vector

argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
ap = argparse.ArgumentParser()
ap.add_argument("--in", dest="inp", required=True)
ap.add_argument("--out", required=True)
ap.add_argument("--name", required=True)
ap.add_argument("--max-faces", type=int, default=20000)
ap.add_argument("--tex", type=int, default=1024)
ap.add_argument("--rigged", action="store_true")
ap.add_argument("--size", default="")
ap.add_argument("--pivot", default="bottom")
ap.add_argument("--keep-collection", default="")
ap.add_argument("--no-lods", action="store_true")
ap.add_argument("--drop", default="", help="comma list of name substrings to delete (ground planes...)")
args = ap.parse_args(argv)

OUT = os.path.abspath(args.out)
args.inp = os.path.abspath(args.inp)
# unpacked images of unsaved scenes (glb/fbx imports) land in //textures relative to the CWD: keep them in TEMP
import tempfile
os.chdir(tempfile.mkdtemp(prefix="bk_export_"))
TEX = os.path.join(OUT, "Textures")
os.makedirs(TEX, exist_ok=True)
stats = {"name": args.name, "source": os.path.basename(args.inp), "warnings": []}


def log(*a):
    print("[bk_export]", *a, flush=True)


# ------------------------------------------------------------------ load
ext = os.path.splitext(args.inp)[1].lower()
if ext == ".blend":
    bpy.ops.wm.open_mainfile(filepath=args.inp, load_ui=False, use_scripts=False)
else:
    bpy.ops.wm.read_factory_settings(use_empty=True)
    if ext in (".glb", ".gltf"):
        bpy.ops.import_scene.gltf(filepath=args.inp)
    elif ext == ".fbx":
        bpy.ops.import_scene.fbx(filepath=args.inp)
    elif ext == ".obj":
        bpy.ops.wm.obj_import(filepath=args.inp)
    else:
        raise SystemExit("unsupported input " + ext)

scene = bpy.context.scene
vl = bpy.context.view_layer
try:
    bpy.ops.file.unpack_all(method="USE_LOCAL")
except Exception as e:
    log("unpack skipped", e)
try:
    bpy.ops.file.make_paths_absolute()
except Exception:
    pass

# make collection instances real (BlenderKit often wraps the asset in an instanced collection)
for _ in range(3):
    inst = [o for o in scene.objects if o.type == "EMPTY" and o.instance_type == "COLLECTION" and o.instance_collection]
    if not inst:
        break
    bpy.ops.object.select_all(action="DESELECT")
    for o in inst:
        try:
            o.hide_set(False); o.hide_viewport = False
            o.select_set(True)
        except Exception:
            pass
    vl.objects.active = inst[0]
    bpy.ops.object.duplicates_make_real(use_base_parent=True, use_hierarchy=True)

# ------------------------------------------------------------------ choose objects
def visible(o):
    try:
        return o.visible_get() and not o.hide_render
    except Exception:
        return False

drop = [d.strip().lower() for d in args.drop.split(",") if d.strip()]
keep = []
for o in list(scene.objects):
    ok = o.type in ("MESH", "CURVE", "SURFACE", "META", "FONT", "ARMATURE") and visible(o)
    if ok and args.keep_collection:
        ok = any(args.keep_collection.lower() in c.name.lower() for c in o.users_collection) or o.type == "ARMATURE"
    if ok and drop and any(d in o.name.lower() for d in drop):
        ok = False
    if ok:
        keep.append(o)

meshes = [o for o in keep if o.type in ("MESH", "CURVE", "SURFACE", "META", "FONT")]
arms = [o for o in scene.objects if o.type == "ARMATURE"]
if not meshes:
    raise SystemExit("no visible meshes")

# multi-user meshes -> single user so modifiers / decimation / transform apply work per object
for o in meshes:
    if o.type == "MESH" and o.data.users > 1:
        o.data = o.data.copy()

# convert curves/text & apply modifiers (keep Armature modifiers on rigged meshes)
bpy.ops.object.select_all(action="DESELECT")
for o in meshes:
    o.hide_set(False)
    vl.objects.active = o
    o.select_set(True)
    if o.type != "MESH":
        bpy.ops.object.convert(target="MESH")
    else:
        for m in list(o.modifiers):
            if m.type == "ARMATURE":
                continue
            if m.type in ("SUBSURF", "MULTIRES"):
                stats["warnings"].append("dropped %s on %s" % (m.type, o.name))
                o.modifiers.remove(m)  # game art: never bake subdivision, the decimator would only throw it away
                continue
            try:
                if not m.show_viewport:
                    o.modifiers.remove(m)
                    continue
                bpy.ops.object.modifier_apply(modifier=m.name)
            except Exception as e:
                stats["warnings"].append("modifier %s on %s: %s" % (m.name, o.name, e))
                try: o.modifiers.remove(m)
                except Exception: pass
    o.select_set(False)
# delete everything else only now: hidden curves / empties may be modifier targets (Curve, Array offsets)
kept_names = {o.name for o in meshes} | {o.name for o in arms}
for o in list(scene.objects):
    if o.name not in kept_names and o.type != "EMPTY":
        bpy.data.objects.remove(o, do_unlink=True)
for o in list(scene.objects):
    if o.type == "EMPTY" and not o.children_recursive:
        bpy.data.objects.remove(o, do_unlink=True)
meshes = [o for o in scene.objects if o.type == "MESH"]
# shape keys block decimation; this is game art, drop them
for o in meshes:
    if o.data.shape_keys:
        vl.objects.active = o
        o.shape_key_clear()


def tri_count(objs):
    n = 0
    for o in objs:
        for p in o.data.polygons:
            n += len(p.vertices) - 2
    return n

tris0 = tri_count(meshes)
stats["tris_source"] = tris0
log("objects", len(meshes), "tris", tris0)

# ------------------------------------------------------------------ decimate
if tris0 > args.max_faces:
    ratio = max(0.02, args.max_faces / tris0)
    for o in meshes:
        n = sum(len(p.vertices) - 2 for p in o.data.polygons)
        if n < 64:
            continue
        vl.objects.active = o
        md = o.modifiers.new("Dec", "DECIMATE")
        md.decimate_type = "COLLAPSE"
        md.ratio = ratio
        md.use_collapse_triangulate = True
        try:
            bpy.ops.object.modifier_move_to_index(modifier="Dec", index=0)
            bpy.ops.object.modifier_apply(modifier="Dec")
        except Exception as e:
            stats["warnings"].append("decimate %s: %s" % (o.name, e))
tris1 = tri_count(meshes)
stats["tris"] = tris1
log("tris after decimate", tris1)

# ------------------------------------------------------------------ materials
def safe(s):
    return "".join(c if c.isalnum() or c in "-_" else "_" for c in s)[:48]

def upstream(sock):
    return sock.links[0].from_socket if sock.is_linked and sock.links else None

def find_image(sock, depth=0):
    """(image_node, channel) feeding this socket through simple nodes, else None."""
    fs = upstream(sock)
    if fs is None or depth > 6:
        return None
    n = fs.node
    if n.type == "TEX_IMAGE" and n.image is not None:
        if fs.name == "Alpha":
            return (n, "A")
        return (n, "RGB")
    if n.type in ("SEPARATE_COLOR", "SEPRGB", "SEPARATE_RGB"):
        ch = {"Red": "R", "R": "R", "Green": "G", "G": "G", "Blue": "B", "B": "B"}.get(fs.name, "R")
        src = find_image(n.inputs[0], depth + 1)
        return (src[0], ch) if src else None
    if n.type in ("NORMAL_MAP",):
        return find_image(n.inputs["Color"], depth + 1)
    if n.type in ("MIX", "MIX_RGB", "GAMMA", "HUE_SAT", "BRIGHTCONTRAST", "CURVE_RGB", "VALTORGB", "MATH",
                  "INVERT", "MAP_RANGE", "CLAMP", "RGBTOBW", "REROUTE", "BUMP"):
        # follow the most meaningful colour input (first image found wins)
        for inp in n.inputs:
            if inp.is_linked:
                r = find_image(inp, depth + 1)
                if r:
                    inv = n.type == "INVERT"
                    return (r[0], r[1] + ("~" if inv else ""))
    return None

def img_array(img, size):
    im = img.copy()
    w, h = im.size
    if w == 0 or h == 0:
        bpy.data.images.remove(im)
        return None
    s = min(size, max(w, h))
    # keep aspect, power of two-ish
    nw = max(4, int(round(w * s / max(w, h))))
    nh = max(4, int(round(h * s / max(w, h))))
    if (nw, nh) != (w, h):
        im.scale(nw, nh)
    a = np.empty(nw * nh * 4, dtype=np.float32)
    im.pixels.foreach_get(a)
    bpy.data.images.remove(im)
    return a.reshape(nh, nw, 4)

def save_png(arr, path):
    """PNG when the alpha channel matters (path ends .png), else JPEG q90 (opaque albedo / normal maps)."""
    h, w = arr.shape[:2]
    im = bpy.data.images.new("tmp_save", w, h, alpha=True)
    im.pixels.foreach_set(np.ascontiguousarray(arr, dtype=np.float32).ravel())
    im.filepath_raw = path
    if path.endswith(".jpg"):
        im.file_format = "JPEG"
        scene.render.image_settings.quality = 90
    else:
        im.file_format = "PNG"
    im.save()
    bpy.data.images.remove(im)

def channel(arr, ch):
    inv = ch.endswith("~")
    ch = ch.rstrip("~")
    if ch == "RGB":
        c = arr[..., 0] * 0.299 + arr[..., 1] * 0.587 + arr[..., 2] * 0.114
    else:
        c = arr[..., "RGBA".index(ch)]
    return 1.0 - c if inv else c

def resize_to(a, shape):
    if a.shape[:2] == shape:
        return a
    ys = (np.arange(shape[0]) * a.shape[0] / shape[0]).astype(int)
    xs = (np.arange(shape[1]) * a.shape[1] / shape[1]).astype(int)
    return a[ys][:, xs]

# ---- bake fallback (Cycles, emission trick)
_bake_ready = False
def bake_ready():
    global _bake_ready
    if _bake_ready:
        return
    scene.render.engine = "CYCLES"
    scene.cycles.device = "CPU"
    scene.cycles.samples = 1
    scene.render.bake.margin = 4
    _bake_ready = True

def users_of(mat):
    return [o for o in meshes if any(s.material == mat for s in o.material_slots)]

def ensure_uv(objs):
    for o in objs:
        if not o.data.uv_layers:
            bpy.ops.object.select_all(action="DESELECT")
            vl.objects.active = o
            o.select_set(True)
            bpy.ops.object.mode_set(mode="EDIT")
            bpy.ops.mesh.select_all(action="SELECT")
            bpy.ops.uv.smart_project(angle_limit=math.radians(66), island_margin=0.02)
            bpy.ops.object.mode_set(mode="OBJECT")
            stats["warnings"].append("smart UV on " + o.name)

def bake_socket(mat, from_socket, size, is_normal=False):
    """Bake whatever drives from_socket (or the material's normal output) into an RGBA array."""
    objs = users_of(mat)
    if not objs:
        return None
    bake_ready()
    ensure_uv(objs)
    nt = mat.node_tree
    out = next((n for n in nt.nodes if n.type == "OUTPUT_MATERIAL" and n.is_active_output), None) or \
        next((n for n in nt.nodes if n.type == "OUTPUT_MATERIAL"), None)
    if out is None:
        return None
    img = bpy.data.images.new("bake_" + safe(mat.name), size, size, alpha=False, float_buffer=False)
    tex = nt.nodes.new("ShaderNodeTexImage")
    tex.image = img
    for n in nt.nodes:
        n.select = False
    tex.select = True
    nt.nodes.active = tex
    old = upstream(out.inputs["Surface"])
    emit = None
    if not is_normal:
        emit = nt.nodes.new("ShaderNodeEmission")
        nt.links.new(from_socket, emit.inputs["Color"])
        nt.links.new(emit.outputs["Emission"], out.inputs["Surface"])
    bpy.ops.object.select_all(action="DESELECT")
    for o in objs:
        o.select_set(True)
    vl.objects.active = objs[0]
    try:
        if is_normal:
            bpy.ops.object.bake(type="NORMAL", normal_space="TANGENT", use_clear=True)
        else:
            bpy.ops.object.bake(type="EMIT", use_clear=True)
        a = np.empty(size * size * 4, dtype=np.float32)
        img.pixels.foreach_get(a)
        a = a.reshape(size, size, 4)
    except Exception as e:
        stats["warnings"].append("bake %s: %s" % (mat.name, e))
        a = None
    if emit is not None:
        nt.nodes.remove(emit)
        if old is not None:
            nt.links.new(old, out.inputs["Surface"])
    nt.nodes.remove(tex)
    bpy.data.images.remove(img)
    return a

def principled(mat):
    if not mat or not mat.node_tree:
        return None
    out = next((n for n in mat.node_tree.nodes if n.type == "OUTPUT_MATERIAL" and n.is_active_output), None)
    if out and out.inputs["Surface"].is_linked:
        # walk shader mixes to the first principled
        stack = [out.inputs["Surface"].links[0].from_node]
        seen = set()
        while stack:
            n = stack.pop(0)
            if n in seen:
                continue
            seen.add(n)
            if n.type == "BSDF_PRINCIPLED":
                return n
            for i in n.inputs:
                if i.is_linked:
                    stack.append(i.links[0].from_node)
    return next((n for n in mat.node_tree.nodes if n.type == "BSDF_PRINCIPLED"), None)

def inp(bsdf, *names):
    for nm in names:
        if nm in bsdf.inputs:
            return bsdf.inputs[nm]
    return None

def val(sock, default):
    if sock is None:
        return default
    try:
        v = sock.default_value
        return list(v) if hasattr(v, "__len__") else float(v)
    except Exception:
        return default

mats_out = {}
all_mats = []
for o in meshes:
    for s in o.material_slots:
        if s.material and s.material not in all_mats:
            all_mats.append(s.material)

for mat in all_mats:
    name = safe(mat.name)
    d = {"color": [0.6, 0.6, 0.6, 1.0], "metallic": 0.0, "smoothness": 0.4, "cutout": False, "transparent": False}
    bsdf = principled(mat)
    if bsdf is None:
        try:
            d["color"] = list(mat.diffuse_color)
        except Exception:
            pass
        mats_out[mat.name] = d
        stats["warnings"].append("no principled in " + mat.name)
        continue
    sb, sm, sr, sa, sn = (inp(bsdf, "Base Color"), inp(bsdf, "Metallic"), inp(bsdf, "Roughness"),
                          inp(bsdf, "Alpha"), inp(bsdf, "Normal"))
    se = inp(bsdf, "Emission Color", "Emission")
    sest = inp(bsdf, "Emission Strength")
    d["color"] = val(sb, d["color"])
    d["metallic"] = float(val(sm, 0.0))
    d["smoothness"] = 1.0 - float(val(sr, 0.6))
    size = args.tex
    albedo = None
    # base colour
    if sb is not None and sb.is_linked:
        f = find_image(sb)
        if f:
            albedo = img_array(f[0].image, size)
        else:
            albedo = bake_socket(mat, upstream(sb), min(size, 1024))
            stats["warnings"].append("baked albedo " + mat.name)
        if albedo is not None:
            d["color"] = [1, 1, 1, 1]
    # alpha
    alpha_arr = None
    if sa is not None and sa.is_linked:
        f = find_image(sa)
        if f:
            a = img_array(f[0].image, size)
            if a is not None:
                alpha_arr = channel(a, f[1] if f[1] != "RGB" else "A")
                if f[1] == "RGB":
                    alpha_arr = channel(a, "RGB")
    elif sa is not None and float(val(sa, 1.0)) < 0.99:
        d["transparent"] = True
        d["color"][3] = float(val(sa, 1.0))
    if alpha_arr is not None:
        if albedo is None:
            albedo = np.ones(alpha_arr.shape + (4,), dtype=np.float32)
            albedo[..., :3] = np.array(d["color"][:3])[None, None, :]
            d["color"] = [1, 1, 1, 1]
        albedo[..., 3] = resize_to(alpha_arr, albedo.shape[:2])
        d["cutout"] = True
    if albedo is not None:
        ext_a = ".png" if d["cutout"] else ".jpg"
        p = os.path.join(TEX, name + "_albedo" + ext_a)
        save_png(albedo, p)
        d["albedo"] = "Textures/" + name + "_albedo" + ext_a
    # metallic + roughness -> MS map (R metal, A smoothness)
    met = rough = None
    for sock, key in ((sm, "m"), (sr, "r")):
        if sock is None or not sock.is_linked:
            continue
        f = find_image(sock)
        if f:
            a = img_array(f[0].image, size)
            c = channel(a, f[1]) if a is not None else None
        else:
            a = bake_socket(mat, upstream(sock), min(size, 512))
            c = channel(a, "R") if a is not None else None
            stats["warnings"].append("baked %s %s" % (key, mat.name))
        if key == "m":
            met = c
        else:
            rough = c
    if met is not None or rough is not None:
        shp = (met if met is not None else rough).shape
        ms = np.zeros(shp + (4,), dtype=np.float32)
        ms[..., 0] = resize_to(met, shp) if met is not None else d["metallic"]
        ms[..., 1] = ms[..., 0]
        ms[..., 2] = ms[..., 0]
        ms[..., 3] = (1.0 - resize_to(rough, shp)) if rough is not None else d["smoothness"]
        p = os.path.join(TEX, name + "_ms.png")
        save_png(ms, p)
        d["ms"] = "Textures/" + name + "_ms.png"
        d["metallic"] = 1.0
        d["smoothness"] = 1.0
    # normal
    if sn is not None and sn.is_linked:
        f = find_image(sn)
        if f and upstream(sn).node.type == "NORMAL_MAP":
            a = img_array(f[0].image, size)
            strength = float(val(upstream(sn).node.inputs.get("Strength"), 1.0))
        else:
            a = bake_socket(mat, None, min(size, 1024), is_normal=True)
            strength = 1.0
            stats["warnings"].append("baked normal " + mat.name)
        if a is not None:
            p = os.path.join(TEX, name + "_normal.jpg")
            a[..., 3] = 1.0
            save_png(a, p)
            d["normal"] = "Textures/" + name + "_normal.jpg"
            d["normalScale"] = max(0.2, min(2.0, strength))
    # emission
    if se is not None:
        ec = val(se, [0, 0, 0, 1])
        es = float(val(sest, 1.0))
        if isinstance(ec, list) and max(ec[:3]) * es > 0.01 and not se.is_linked:
            d["emission"] = [c * es for c in ec[:3]]
    mats_out[mat.name] = d
    log("material", mat.name, {k: v for k, v in d.items() if k in ("albedo", "ms", "normal", "cutout")})

# empty material slots -> default material so Unity has something to remap
for o in meshes:
    if not o.material_slots or all(s.material is None for s in o.material_slots):
        m = bpy.data.materials.get("Default_Grey") or bpy.data.materials.new("Default_Grey")
        if not o.material_slots:
            o.data.materials.append(m)
        else:
            for s in o.material_slots:
                s.material = m
        mats_out.setdefault("Default_Grey", {"color": [0.45, 0.45, 0.43, 1], "metallic": 0.0, "smoothness": 0.3,
                                             "cutout": False, "transparent": False})

# ------------------------------------------------------------------ transforms, size, pivot
if not args.rigged:
    bpy.ops.object.select_all(action="DESELECT")
    for o in meshes:
        o.select_set(True)
    vl.objects.active = meshes[0]
    # parent chains: clear parent keep transform, then apply
    for o in meshes:
        if o.parent:
            mw = o.matrix_world.copy()
            o.parent = None
            o.matrix_world = mw
    bpy.ops.object.transform_apply(location=False, rotation=True, scale=True)
    for o in list(scene.objects):
        if o.type == "EMPTY":
            bpy.data.objects.remove(o, do_unlink=True)

def world_bbox(objs):
    mn = Vector((1e9, 1e9, 1e9)); mx = Vector((-1e9, -1e9, -1e9))
    dg = bpy.context.evaluated_depsgraph_get()
    for o in objs:
        oe = o.evaluated_get(dg)
        for c in oe.bound_box:
            w = oe.matrix_world @ Vector(c)
            mn = Vector(map(min, mn, w)); mx = Vector(map(max, mx, w))
    return mn, mx

roots = [o for o in scene.objects if o.parent is None]
mn, mx = world_bbox(meshes)
dims = mx - mn
if args.size:
    axis, v = args.size.split("=")
    v = float(v)
    cur = {"L": max(dims.x, dims.y), "W": min(dims.x, dims.y), "H": dims.z}[axis.upper()]
    if cur > 1e-6:
        k = v / cur
        for o in roots:
            o.matrix_world = __import__("mathutils").Matrix.Scale(k, 4) @ o.matrix_world
        bpy.context.view_layer.update()
        mn, mx = world_bbox(meshes)
        dims = mx - mn
if args.pivot in ("bottom", "center"):
    off = Vector(((mn.x + mx.x) / 2, (mn.y + mx.y) / 2, mn.z if args.pivot == "bottom" else (mn.z + mx.z) / 2))
    for o in roots:
        o.location -= off
    bpy.context.view_layer.update()
    mn, mx = world_bbox(meshes)
if not args.rigged:
    bpy.ops.object.select_all(action="DESELECT")
    for o in meshes:
        o.select_set(True)
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
stats["size_m"] = [round(dims.x, 3), round(dims.y, 3), round(dims.z, 3)]  # Blender X, Y(depth), Z(up)
log("size m", stats["size_m"])

# join static meshes per material to reduce draw calls (props): only when many small objects
if not args.rigged and len(meshes) > 1:
    bpy.ops.object.select_all(action="DESELECT")
    for o in meshes:
        o.select_set(True)
    vl.objects.active = meshes[0]
    bpy.ops.object.join()
    meshes = [o for o in scene.objects if o.type == "MESH"]
    meshes[0].name = args.name

# LOD chain for static props: <slug>_LOD0/1/2 siblings -> Unity builds the LODGroup on import
stats["lods"] = 1
if not args.rigged and len(meshes) == 1 and stats["tris"] > 1500 and not args.no_lods:
    base = meshes[0]
    base.name = args.name + "_LOD0"
    levels = [(1, 0.35)] + ([(2, 0.12)] if stats["tris"] > 8000 else [])
    for lvl, ratio in levels:
        o = base.copy()
        o.data = base.data.copy()
        scene.collection.objects.link(o)
        o.name = args.name + "_LOD%d" % lvl
        vl.objects.active = o
        md = o.modifiers.new("Dec", "DECIMATE")
        md.decimate_type = "COLLAPSE"
        md.ratio = ratio
        md.use_collapse_triangulate = True
        bpy.ops.object.select_all(action="DESELECT")
        o.select_set(True)
        bpy.ops.object.modifier_apply(modifier="Dec")
        stats["lods"] = lvl + 1
        stats["tris_lod%d" % lvl] = tri_count([o])

# rigged: keep only deform bones (control rigs such as Auto-Rig Pro have hundreds of helper bones that break
# Unity's Humanoid auto-mapping). Children of removed bones are re-parented to the nearest kept ancestor.
if args.rigged:
    for arm in arms:
        for pb in arm.pose.bones:
            for c in list(pb.constraints):
                pb.constraints.remove(c)
        if arm.animation_data:
            for d in list(arm.animation_data.drivers):
                arm.animation_data.drivers.remove(d)
            arm.animation_data_clear()
        deform = {b.name for b in arm.data.bones if b.use_deform}
        used = set()
        for o in meshes:
            used |= {vg.name for vg in o.vertex_groups}
        keepb = deform & used if (deform & used) else deform
        bpy.ops.object.select_all(action="DESELECT")
        arm.hide_set(False)
        vl.objects.active = arm
        arm.select_set(True)
        bpy.ops.object.mode_set(mode="EDIT")
        eb = arm.data.edit_bones
        def kept_parent(b):
            p = b.parent
            while p is not None and p.name not in keepb:
                p = p.parent
            return p
        for b in eb:
            if b.name in keepb:
                b.parent = kept_parent(b)
        for b in list(eb):
            if b.name not in keepb:
                eb.remove(b)
        # orphans (their chain went through control bones only): attach to the nearest kept bone tail
        orphans = [b for b in eb if b.parent is None]
        if len(orphans) > 1:
            def score(b):
                n = b.name.lower()
                return (0 if any(k in n for k in ("root", "hips", "pelvis")) else 1, -len(b.children_recursive), b.head.z)
            main = sorted(orphans, key=score)[0]
            for b in orphans:
                if b is main:
                    continue
                desc = set(c.name for c in b.children_recursive) | {b.name}
                cand = [c for c in eb if c.name not in desc]
                best = min(cand, key=lambda c: (c.tail - b.head).length)
                b.parent = best
        # Auto-Rig Pro: the deform skeleton is only connected through control bones -> use its known layout
        if "root.x" in eb and "thigh_stretch.l" in eb:
            fixed = {"spine_01.x": "root.x", "spine_02.x": "spine_01.x", "spine_03.x": "spine_02.x",
                     "spine_04.x": "spine_03.x", "spine_05.x": "spine_04.x", "neck.x": "spine_05.x",
                     "subneck_twist_1.x": "neck.x", "head.x": "neck.x"}
            for sd in ("l", "r"):
                top = "spine_05.x" if "spine_05.x" in eb else ("spine_03.x" if "spine_03.x" in eb else "spine_02.x")
                fixed.update({"shoulder." + sd: top, "arm_stretch." + sd: "shoulder." + sd,
                              "arm_twist_2." + sd: "arm_stretch." + sd, "c_arm_twist_offset." + sd: "shoulder." + sd,
                              "forearm_stretch." + sd: "arm_stretch." + sd, "forearm_twist." + sd: "forearm_stretch." + sd,
                              "forearm_twist_2." + sd: "forearm_stretch." + sd, "hand." + sd: "forearm_stretch." + sd,
                              "thigh_stretch." + sd: "root.x", "thigh_twist." + sd: "thigh_stretch." + sd,
                              "thigh_twist_2." + sd: "thigh_stretch." + sd, "leg_stretch." + sd: "thigh_stretch." + sd,
                              "leg_twist." + sd: "leg_stretch." + sd, "leg_twist_2." + sd: "leg_stretch." + sd,
                              "foot." + sd: "leg_stretch." + sd, "toes_01." + sd: "foot." + sd,
                              "thumb1." + sd: "hand." + sd, "c_thumb2." + sd: "thumb1." + sd, "c_thumb3." + sd: "c_thumb2." + sd})
                for f in ("index", "middle", "ring", "pinky"):
                    fixed.update({"c_%s1_base.%s" % (f, sd): "hand." + sd, "%s1.%s" % (f, sd): "c_%s1_base.%s" % (f, sd),
                                  "c_%s2.%s" % (f, sd): "%s1.%s" % (f, sd), "c_%s3.%s" % (f, sd): "c_%s2.%s" % (f, sd)})
            for child, par in fixed.items():
                if child in eb and par in eb:
                    eb[child].parent = eb[par]
            if "root.x" in eb:
                eb["root.x"].parent = None
        bpy.ops.object.mode_set(mode="OBJECT")
        roots_b = [b.name for b in arm.data.bones if b.parent is None]
        log("deform bones", len(arm.data.bones), "roots", roots_b)
        stats["bones"] = [b.name for b in arm.data.bones]
        stats["bone_parents"] = {b.name: (b.parent.name if b.parent else None) for b in arm.data.bones}
    # vertex groups of removed bones are useless now
    for o in meshes:
        names = {b.name for a in arms for b in a.data.bones}
        for vg in list(o.vertex_groups):
            if vg.name not in names:
                o.vertex_groups.remove(vg)

# ------------------------------------------------------------------ export
fbx = os.path.join(OUT, args.name + ".fbx")
bpy.ops.object.select_all(action="SELECT")
bpy.ops.export_scene.fbx(
    filepath=fbx, use_selection=False, object_types={"MESH", "ARMATURE", "EMPTY"},
    apply_unit_scale=True, apply_scale_options="FBX_SCALE_ALL", axis_forward="-Z", axis_up="Y",
    bake_space_transform=not args.rigged, use_mesh_modifiers=True, mesh_smooth_type="FACE",
    use_tspace=False, add_leaf_bones=False, primary_bone_axis="Y", secondary_bone_axis="X",
    bake_anim=False, path_mode="STRIP", embed_textures=False)
stats["materials"] = len(mats_out)
stats["armature_bones"] = sum(len(a.data.bones) for a in arms) if arms else 0
stats.setdefault("bones", [b.name for a in arms for b in a.data.bones][:120])
with open(os.path.join(OUT, args.name + ".materials.json"), "w") as f:
    json.dump(mats_out, f, indent=1)
with open(os.path.join(OUT, args.name + ".export.json"), "w") as f:
    json.dump(stats, f, indent=1)
log("DONE", fbx, "tris", stats["tris"], "mats", len(mats_out))
