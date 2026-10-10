"""Headless Blender pipeline for the casualty "Ерлан" and his gear.

    blender.exe -b --python tools/blender/prepare_casualty.py -- <skin.fbx> <project_root>

1. Imports the Mixamo "Swat Guy" skin FBX, removes animation, applies object transforms (clean metre scale).
2. Decimates the body to <= 40k triangles, rebinds materials to the retinted textures
   (made by tools/blender/retint_textures.py into Assets/Art/Characters/Erlan/Textures).
3. Builds a coyote plate carrier shell around the torso (front/back plates, cummerbund, shoulder straps,
   MOLLE rows, magazine pouches), skins it to the same armature by weight transfer from the body.
4. Exports Assets/Art/Characters/Erlan/Erlan.fbx (armature + body + plate carrier, no textures embedded).
5. Builds rigid gear (origin = attach point, metres, Y-up after export) and exports each to Assets/Art/Gear/:
   DragHandle.fbx, IFAK_Pouch.fbx, CAT_Tourniquet.fbx, Helmet.fbx, AmputationStump.fbx.
   Also writes Assets/Art/Gear/gear_attach.json with the rest-pose positions of the attach bones.
"""
import json
import math
import os
import random
import sys

import bmesh
import bpy
from mathutils import Matrix, Vector

argv = sys.argv[sys.argv.index("--") + 1:]
SKIN_FBX = argv[0]
ROOT = argv[1]
CHAR_DIR = os.path.join(ROOT, "Assets", "Art", "Characters", "Erlan")
GEAR_DIR = os.path.join(ROOT, "Assets", "Art", "Gear")
TEX_DIR = os.path.join(CHAR_DIR, "Textures")
MAX_TRIS = 40000
INCLUDE_PC = False
random.seed(7)


def log(*a):
    print("[FMVR-BLENDER]", *a, flush=True)


# ------------------------------------------------------------------ helpers
def clear_scene():
    bpy.ops.wm.read_factory_settings(use_empty=True)


def material(name, color, rough=0.8, metal=0.0):
    m = bpy.data.materials.get(name) or bpy.data.materials.new(name)
    m.use_nodes = True
    bsdf = m.node_tree.nodes.get("Principled BSDF")
    if bsdf:
        bsdf.inputs["Base Color"].default_value = (*color, 1.0)
        bsdf.inputs["Roughness"].default_value = rough
        bsdf.inputs["Metallic"].default_value = metal
    m.diffuse_color = (*color, 1.0)
    return m


def new_obj(name, bm, mat=None):
    me = bpy.data.meshes.new(name)
    bm.to_mesh(me)
    bm.free()
    ob = bpy.data.objects.new(name, me)
    bpy.context.scene.collection.objects.link(ob)
    if mat:
        me.materials.append(mat)
    for p in me.polygons:
        p.use_smooth = True
    return ob


def box_bm(bm, center, size, bevel=0.0, rot=None):
    geom = bmesh.ops.create_cube(bm, size=1.0)
    verts = geom["verts"]
    m = Matrix.Diagonal((*size, 1.0))
    if rot is not None:
        m = rot.to_4x4() @ m
    bmesh.ops.transform(bm, matrix=Matrix.Translation(center) @ m, verts=verts)
    if bevel > 0:
        edges = list({e for v in verts for e in v.link_edges})
        bmesh.ops.bevel(bm, geom=verts + edges, offset=bevel, segments=2, affect="EDGES", profile=0.5)
    return verts


def cyl_bm(bm, center, radius, depth, segs=12, axis="Z", cap=True):
    geom = bmesh.ops.create_cone(bm, cap_ends=cap, segments=segs, radius1=radius, radius2=radius, depth=depth)
    rot = {"Z": Matrix.Identity(4), "X": Matrix.Rotation(math.pi / 2, 4, "Y"), "Y": Matrix.Rotation(math.pi / 2, 4, "X")}[axis]
    bmesh.ops.transform(bm, matrix=Matrix.Translation(center) @ rot, verts=geom["verts"])
    return geom["verts"]


def torus_bm(bm, center, major, minor, segs=24, rsegs=8, arc=2 * math.pi, start=0.0, scale=(1, 1, 1), rot=None):
    """Torus (or arc of it) in the XY plane; minor cross-section can be flattened via scale."""
    rings = []
    closed = abs(arc - 2 * math.pi) < 1e-4
    n = segs if closed else segs + 1
    for i in range(n):
        a = start + arc * i / segs
        c = Vector((math.cos(a) * major, math.sin(a) * major, 0))
        radial = Vector((math.cos(a), math.sin(a), 0))
        ring = []
        for j in range(rsegs):
            b = 2 * math.pi * j / rsegs
            p = c + radial * math.cos(b) * minor * scale[0] + Vector((0, 0, math.sin(b) * minor * scale[2]))
            ring.append(bm.verts.new(p))
        rings.append(ring)
    for i in range(len(rings) - (0 if closed else 1)):
        r0, r1 = rings[i], rings[(i + 1) % len(rings)]
        for j in range(rsegs):
            bm.faces.new((r0[j], r0[(j + 1) % rsegs], r1[(j + 1) % rsegs], r1[j]))
    verts = [v for r in rings for v in r]
    m = Matrix.Translation(center)
    if rot is not None:
        m = m @ rot.to_4x4()
    bmesh.ops.transform(bm, matrix=m, verts=verts)
    return verts


def join(objs, name):
    bpy.ops.object.select_all(action="DESELECT")
    for o in objs:
        o.select_set(True)
    bpy.context.view_layer.objects.active = objs[0]
    bpy.ops.object.join()
    ob = bpy.context.view_layer.objects.active
    ob.name = name
    ob.data.name = name
    return ob


def tri_count(ob):
    return sum(len(p.vertices) - 2 for p in ob.data.polygons)


def export_fbx(path, objects, armature=False):
    os.makedirs(os.path.dirname(path), exist_ok=True)
    bpy.ops.object.select_all(action="DESELECT")
    for o in objects:
        o.select_set(True)
    bpy.context.view_layer.objects.active = objects[0]
    bpy.ops.export_scene.fbx(
        filepath=path, use_selection=True, apply_unit_scale=True, apply_scale_options="FBX_SCALE_ALL",
        axis_forward="-Z", axis_up="Y", object_types={"ARMATURE", "MESH", "EMPTY"},
        use_mesh_modifiers=True, mesh_smooth_type="FACE", add_leaf_bones=False,
        primary_bone_axis="Y", secondary_bone_axis="X", bake_anim=False, path_mode="STRIP",
        embed_textures=False, use_armature_deform_only=armature)
    log("exported", path, os.path.getsize(path) // 1024, "KB")


# ------------------------------------------------------------------ 1. character
def import_character():
    clear_scene()
    bpy.ops.import_scene.fbx(filepath=SKIN_FBX, use_anim=False)
    for a in list(bpy.data.actions):
        bpy.data.actions.remove(a)
    arm = next(o for o in bpy.data.objects if o.type == "ARMATURE")
    body = next(o for o in bpy.data.objects if o.type == "MESH")
    arm.animation_data_clear()
    arm.data.pose_position = "REST"
    for pb in arm.pose.bones:
        pb.location = (0, 0, 0)
        pb.rotation_quaternion = (1, 0, 0, 0)
        pb.rotation_euler = (0, 0, 0)
        pb.scale = (1, 1, 1)
    bpy.ops.object.select_all(action="DESELECT")
    arm.select_set(True)
    body.select_set(True)
    bpy.context.view_layer.objects.active = arm
    bpy.ops.object.transform_apply(location=False, rotation=True, scale=True)
    arm.name = "Erlan"
    body.name = "Erlan_Body"
    body.data.name = "Erlan_Body"
    log("body tris before", tri_count(body))
    return arm, body


def decimate(body):
    t = tri_count(body)
    if t <= MAX_TRIS:
        return
    mod = body.modifiers.new("Decimate", "DECIMATE")
    mod.ratio = (MAX_TRIS - 1500) / t
    mod.use_collapse_triangulate = True
    # decimate must run before the armature modifier
    bpy.context.view_layer.objects.active = body
    while body.modifiers[0].name != "Decimate":
        bpy.ops.object.modifier_move_up(modifier="Decimate")
    bpy.ops.object.modifier_apply(modifier="Decimate")
    log("body tris after", tri_count(body))


def rebind_materials(body):
    for i, slot in enumerate(body.material_slots):
        idx = i + 1
        m = bpy.data.materials.new(f"Erlan_Body{idx}")
        m.use_nodes = True
        nt = m.node_tree
        bsdf = nt.nodes["Principled BSDF"]
        tex = nt.nodes.new("ShaderNodeTexImage")
        tex.image = bpy.data.images.load(os.path.join(TEX_DIR, f"Erlan_Body{idx}_Albedo.jpg"))
        nt.links.new(tex.outputs["Color"], bsdf.inputs["Base Color"])
        ntex = nt.nodes.new("ShaderNodeTexImage")
        ntex.image = bpy.data.images.load(os.path.join(TEX_DIR, f"Erlan_Body{idx}_Normal.png"))
        ntex.image.colorspace_settings.name = "Non-Color"
        nmap = nt.nodes.new("ShaderNodeNormalMap")
        nt.links.new(ntex.outputs["Color"], nmap.inputs["Color"])
        nt.links.new(nmap.outputs["Normal"], bsdf.inputs["Normal"])
        bsdf.inputs["Roughness"].default_value = 0.85
        slot.material = m
    # drop the original packed 4K images so nothing big can leak into the export
    for img in list(bpy.data.images):
        if img.packed_file is not None:
            bpy.data.images.remove(img)


def bone_head(arm, name):
    return arm.matrix_world @ arm.data.bones["mixamorig:" + name].head_local


def torso_section(body, z, half_height=0.03, max_abs_x=0.24):
    """Front (-Y) / back (+Y) / side extents of the body mesh in a horizontal slab around z."""
    mw = body.matrix_world
    ys, xs = [], []
    for v in body.data.vertices:
        p = mw @ v.co
        if abs(p.z - z) < half_height and abs(p.x) < max_abs_x:
            ys.append(p.y)
            xs.append(p.x)
    return min(ys), max(ys), min(xs), max(xs)


# ------------------------------------------------------------------ 2. plate carrier (skinned)
def build_plate_carrier(arm, body):
    coyote = material("PC_Coyote", (0.36, 0.29, 0.19), 0.9)
    webbing = material("PC_Webbing", (0.20, 0.19, 0.13), 0.85)
    chest = bone_head(arm, "Spine2")
    neck = bone_head(arm, "Neck")
    spine = bone_head(arm, "Spine")
    plate_h = 0.36
    plate_top = neck.z + 0.03
    plate_c = plate_top - plate_h / 2
    half_w = 0.16
    # fit plates to the real profile (includes the original vest) over the plate's height
    fs, bs = [], []
    for k in range(5):
        f_, b_, _, _ = torso_section(body, plate_top - 0.04 - k * 0.07, 0.035, half_w)
        fs.append(f_); bs.append(b_)
    front_y = min(fs) - 0.006
    back_y = max(bs) + 0.006
    log("plates front/back", round(front_y, 3), round(back_y, 3), "top", round(plate_top, 3))

    bm = bmesh.new()
    box_bm(bm, Vector((0, front_y - 0.02, plate_c)), (half_w * 2, 0.04, plate_h), bevel=0.012)
    box_bm(bm, Vector((0, back_y + 0.02, plate_c)), (half_w * 2, 0.04, plate_h), bevel=0.012)
    # cummerbund: ring following the real body outline (per-angle max radius) in the lower plate area
    cz = plate_c - plate_h * 0.3
    mw = body.matrix_world
    pts = [mw @ v.co for v in body.data.vertices]
    slab = [p for p in pts if abs(p.z - cz) < 0.06 and abs(p.x) < 0.3]
    cy = (front_y + back_y) / 2
    nseg = 32
    radius = [0.0] * nseg
    for p in slab:
        a = math.atan2(p.y - cy, p.x)
        i = int(round((a % (2 * math.pi)) / (2 * math.pi) * nseg)) % nseg
        radius[i] = max(radius[i], math.hypot(p.x, p.y - cy))
    for _ in range(2):  # fill gaps + smooth
        radius = [max(radius[i], 0.5 * (radius[i - 1] + radius[(i + 1) % nseg])) for i in range(nseg)]
    cx = max(radius[0], radius[nseg // 2]) + 0.015
    ring = []
    for i in range(nseg):
        a = 2 * math.pi * i / nseg
        r = radius[i] + 0.015
        ring.append((math.cos(a) * r, cy + math.sin(a) * r))
    vs_lo = [bm.verts.new((x, y, cz - 0.075)) for x, y in ring]
    vs_hi = [bm.verts.new((x, y, cz + 0.075)) for x, y in ring]
    for i in range(nseg):
        j = (i + 1) % nseg
        bm.faces.new((vs_lo[i], vs_lo[j], vs_hi[j], vs_hi[i]))
    pc_shell = new_obj("PC_Shell", bm, coyote)
    sol = pc_shell.modifiers.new("Solid", "SOLIDIFY")
    sol.thickness = 0.012
    bpy.context.view_layer.objects.active = pc_shell
    bpy.ops.object.modifier_apply(modifier="Solid")

    # shoulder straps: continuous webbing strips arcing over each shoulder, front plate top -> back plate top
    bm = bmesh.new()
    for sgn in (-1, 1):
        x = sgn * (half_w - 0.045)
        rows = []
        n = 12
        for i in range(n + 1):
            t = i / n
            y = (front_y - 0.02) + (back_y + 0.02 - (front_y - 0.02)) * t
            z = plate_top - 0.015 + math.sin(t * math.pi) * 0.05
            rows.append((bm.verts.new((x - 0.03, y, z)), bm.verts.new((x + 0.03, y, z))))
        for i in range(n):
            bm.faces.new((rows[i][0], rows[i][1], rows[i + 1][1], rows[i + 1][0]))
    straps = new_obj("PC_Straps", bm, webbing)
    sol2 = straps.modifiers.new("Solid", "SOLIDIFY")
    sol2.thickness = 0.01
    bpy.context.view_layer.objects.active = straps
    bpy.ops.object.modifier_apply(modifier="Solid")

    # MOLLE rows on the front + three magazine pouches + admin pouch
    bm = bmesh.new()
    for r in range(4):
        box_bm(bm, Vector((0, front_y - 0.047, plate_c - 0.1 + r * 0.035)), (half_w * 1.9, 0.006, 0.012))
    for i in range(3):
        box_bm(bm, Vector(((i - 1) * 0.085, front_y - 0.075, plate_c - 0.08)), (0.078, 0.055, 0.15), bevel=0.01)
    box_bm(bm, Vector((0, front_y - 0.06, plate_c + 0.1)), (0.17, 0.03, 0.09), bevel=0.008)
    pouches = new_obj("PC_Pouches", bm, coyote)
    pc = join([pc_shell, straps, pouches], "PlateCarrier")

    # skin it: copy weights from the body (nearest face interpolated)
    pc.parent = arm
    am = pc.modifiers.new("Armature", "ARMATURE")
    am.object = arm
    dt = pc.modifiers.new("WeightXfer", "DATA_TRANSFER")
    dt.object = body
    dt.use_vert_data = True
    dt.data_types_verts = {"VGROUP_WEIGHTS"}
    dt.vert_mapping = "POLYINTERP_NEAREST"
    bpy.ops.object.select_all(action="DESELECT")
    pc.select_set(True)
    bpy.context.view_layer.objects.active = pc
    bpy.ops.object.datalayout_transfer(modifier="WeightXfer")
    while pc.modifiers[0].name != "WeightXfer":
        bpy.ops.object.modifier_move_up(modifier="WeightXfer")
    bpy.ops.object.modifier_apply(modifier="WeightXfer")
    # rigid-ish: the carrier should not follow arm bones
    for vg in list(pc.vertex_groups):
        if any(k in vg.name for k in ("Arm", "Hand", "Shoulder", "Head", "Leg", "Foot")):
            pc.vertex_groups.remove(vg)
    bpy.ops.object.vertex_group_normalize_all(lock_active=False)
    log("plate carrier tris", tri_count(pc))
    return pc, dict(front_y=front_y, back_y=back_y, plate_top=plate_top, plate_c=plate_c, half_w=half_w,
                    cummerbund_z=cz, cummerbund_x=cx)


# ------------------------------------------------------------------ 3. gear (rigid, origin = attach point)
def build_drag_handle():
    """Loop of 25 mm webbing behind the neck. Origin = stitch point at the top of the back plate."""
    m = material("Gear_Webbing", (0.22, 0.20, 0.13), 0.85)
    bm = bmesh.new()
    # loop stands up and slightly back: arc in the XZ plane
    torus_bm(bm, Vector((0, 0.012, 0.035)), 0.045, 0.012, segs=16, rsegs=6, arc=math.pi, start=0.0,
             scale=(0.35, 1, 1.0), rot=Matrix.Rotation(math.pi / 2, 3, "X"))
    box_bm(bm, Vector((0, 0.0, 0.0)), (0.11, 0.012, 0.03), bevel=0.003)  # bar-tack base
    return new_obj("DragHandle", bm, m)


def build_cat(prefix=""):
    """CAT Gen7 folded as carried: band loop (~11 cm), windlass rod, clip, buckle, red tip, TIME strap.
    Origin = centre of the loop. Separate child objects so S3 can animate the state machine."""
    black = material("CAT_Black", (0.03, 0.03, 0.03), 0.6)
    red = material("CAT_Red", (0.62, 0.05, 0.04), 0.55)
    grey = material("CAT_Strap", (0.12, 0.12, 0.12), 0.7)
    white = material("CAT_TimeLabel", (0.85, 0.85, 0.8), 0.6)
    parts = []
    bm = bmesh.new()
    torus_bm(bm, Vector((0, 0, 0)), 0.055, 0.019, segs=28, rsegs=6, scale=(0.12, 1, 1.0))
    parts.append(new_obj(prefix + "CAT_Band", bm, black))
    bm = bmesh.new()
    cyl_bm(bm, Vector((0, 0.07, 0.0)), 0.0065, 0.11, segs=10, axis="X")
    parts.append(new_obj(prefix + "CAT_Windlass", bm, black))
    bm = bmesh.new()  # windlass clip: C-shaped triangle
    torus_bm(bm, Vector((0.045, 0.058, 0)), 0.014, 0.004, segs=10, rsegs=5, arc=math.pi * 1.3, start=-math.pi * 0.15,
             rot=Matrix.Rotation(math.pi / 2, 3, "Y"))
    box_bm(bm, Vector((0.045, 0.052, 0)), (0.02, 0.012, 0.036), bevel=0.002)
    parts.append(new_obj(prefix + "CAT_Clip", bm, black))
    bm = bmesh.new()
    box_bm(bm, Vector((0, -0.058, 0)), (0.012, 0.008, 0.045), bevel=0.002)
    parts.append(new_obj(prefix + "CAT_Buckle", bm, grey))
    bm = bmesh.new()
    box_bm(bm, Vector((0.0, -0.065, -0.035)), (0.012, 0.035, 0.038), bevel=0.003)
    parts.append(new_obj(prefix + "CAT_RedTip", bm, red))
    bm = bmesh.new()
    box_bm(bm, Vector((-0.04, 0.062, 0)), (0.03, 0.006, 0.026), bevel=0.002)
    parts.append(new_obj(prefix + "CAT_TimeStrap", bm, white))
    root = bpy.data.objects.new(prefix + "CAT_Tourniquet", None)
    bpy.context.scene.collection.objects.link(root)
    for p in parts:
        p.parent = root
    return root, parts


def build_ifak():
    """Rip-away IFAK pouch with an open-top TQ holder on its side. Origin = back of the pouch (on the cummerbund)."""
    coyote = material("Gear_Coyote", (0.36, 0.29, 0.19), 0.9)
    red = material("Gear_RedCross", (0.5, 0.06, 0.05), 0.8)
    bm = bmesh.new()
    box_bm(bm, Vector((0, -0.04, 0)), (0.15, 0.075, 0.12), bevel=0.018)
    box_bm(bm, Vector((0, -0.082, 0.045)), (0.13, 0.012, 0.03), bevel=0.004)  # pull tab flap
    box_bm(bm, Vector((0.09, -0.035, -0.005)), (0.035, 0.06, 0.1), bevel=0.006)  # TQ holder
    pouch = new_obj("IFAK_Pouch", bm, coyote)
    bm = bmesh.new()
    box_bm(bm, Vector((0, -0.0795, -0.01)), (0.04, 0.003, 0.012))
    box_bm(bm, Vector((0, -0.0795, -0.01)), (0.012, 0.003, 0.04))
    cross = new_obj("IFAK_Cross", bm, red)
    cross.parent = pouch
    return pouch


def build_helmet():
    """FAST-style high-cut helmet with olive cover, NVG shroud, rails. Origin = head bone."""
    olive = material("Helmet_Olive", (0.25, 0.26, 0.17), 0.8)
    black = material("Helmet_Black", (0.05, 0.05, 0.05), 0.5)
    bm = bmesh.new()
    bmesh.ops.create_uvsphere(bm, u_segments=24, v_segments=12, radius=0.135)
    # keep upper half, cut high on the sides (high-cut)
    kill = [v for v in bm.verts if v.co.z < -0.005 or (abs(v.co.x) > 0.09 and v.co.z < 0.03)]
    bmesh.ops.delete(bm, geom=kill, context="VERTS")
    bmesh.ops.scale(bm, vec=(0.92, 1.08, 0.95), verts=bm.verts)
    bmesh.ops.translate(bm, vec=(0, 0.01, 0.06), verts=bm.verts)
    shell = new_obj("Helmet", bm, olive)
    sol = shell.modifiers.new("Solid", "SOLIDIFY")
    sol.thickness = 0.012
    bm = bmesh.new()
    box_bm(bm, Vector((0, -0.135, 0.11)), (0.05, 0.02, 0.035), bevel=0.004)  # NVG shroud (front = -Y)
    for s in (-1, 1):
        box_bm(bm, Vector((s * 0.122, 0.0, 0.085)), (0.012, 0.11, 0.022), bevel=0.003)  # side rails
    hw = new_obj("Helmet_Hardware", bm, black)
    hw.parent = shell
    return shell


def build_stump():
    """Traumatic foot amputation at the ankle, worn over the trouser leg/boot top.
    Origin = ankle (RightFoot bone head); the leg axis is +Z (towards the knee).
    Ragged skin/trouser sleeve, exposed muscle cap, protruding tibia & fibula, shredded fabric strips."""
    flesh = material("Wound_Flesh", (0.42, 0.04, 0.03), 0.35)
    meat = material("Wound_Muscle", (0.30, 0.03, 0.03), 0.3)
    bone = material("Wound_Bone", (0.86, 0.80, 0.66), 0.5)
    cloth = material("Wound_Cloth", (0.22, 0.2, 0.13), 0.9)
    clot = material("Wound_Clot", (0.12, 0.01, 0.01), 0.25)
    objs = []

    # trouser/skin sleeve: cylinder with a ragged lower edge
    bm = bmesh.new()
    segs, rings, r = 20, 5, 0.062
    grid = []
    for k in range(rings):
        z = 0.12 - k * 0.03
        row = []
        for i in range(segs):
            a = 2 * math.pi * i / segs
            rr = r * (1 + 0.04 * math.sin(a * 3))
            zz = z
            if k == rings - 1:
                zz = z - random.uniform(-0.012, 0.03)
                rr *= random.uniform(0.85, 1.12)
            row.append(bm.verts.new((math.cos(a) * rr, math.sin(a) * rr, zz)))
        grid.append(row)
    for k in range(rings - 1):
        for i in range(segs):
            j = (i + 1) % segs
            bm.faces.new((grid[k][i], grid[k][j], grid[k + 1][j], grid[k + 1][i]))
    objs.append(new_obj("Stump_Sleeve", bm, cloth))

    # muscle cap: domed, irregular, recessed inside the sleeve
    bm = bmesh.new()
    bmesh.ops.create_uvsphere(bm, u_segments=18, v_segments=9, radius=0.058)
    kill = [v for v in bm.verts if v.co.z > 0.004]
    bmesh.ops.delete(bm, geom=kill, context="VERTS")
    for v in bm.verts:
        v.co.z *= 0.45
        n = random.uniform(0.85, 1.15)
        v.co.x *= n
        v.co.y *= n
        v.co.z += random.uniform(-0.006, 0.006)
    bmesh.ops.translate(bm, vec=(0, 0, 0.0), verts=bm.verts)
    objs.append(new_obj("Stump_Muscle", bm, flesh))

    # torn muscle bundles hanging out
    bm = bmesh.new()
    for i in range(7):
        a = random.uniform(0, 2 * math.pi)
        rr = random.uniform(0.015, 0.045)
        ln = random.uniform(0.02, 0.05)
        c = Vector((math.cos(a) * rr, math.sin(a) * rr, -0.02 - ln / 2))
        box_bm(bm, c, (random.uniform(0.008, 0.016), random.uniform(0.008, 0.016), ln), bevel=0.003,
               rot=Matrix.Rotation(random.uniform(-0.4, 0.4), 3, "X") @ Matrix.Rotation(random.uniform(-0.4, 0.4), 3, "Y"))
    objs.append(new_obj("Stump_Shreds", bm, meat))

    # bones: tibia (thick, anterior-medial) + fibula (thin, lateral), splintered ends
    bm = bmesh.new()
    for (x, y, rad, ln) in ((-0.012, -0.015, 0.016, 0.06), (0.03, 0.006, 0.007, 0.045)):
        vs = cyl_bm(bm, Vector((x, y, -ln / 2 + 0.015)), rad, ln, segs=10)
        for v in vs:
            if v.co.z < -ln + 0.02:
                v.co.z -= random.uniform(0.0, 0.014)  # splinters
                v.co.x += random.uniform(-0.002, 0.002)
    objs.append(new_obj("Stump_Bone", bm, bone))

    # clotted rim + fabric strips
    bm = bmesh.new()
    torus_bm(bm, Vector((0, 0, -0.012)), 0.056, 0.008, segs=20, rsegs=5)
    for v in bm.verts:
        v.co += Vector((random.uniform(-0.004, 0.004), random.uniform(-0.004, 0.004), random.uniform(-0.006, 0.004)))
    objs.append(new_obj("Stump_Clot", bm, clot))
    bm = bmesh.new()
    for i in range(6):
        a = 2 * math.pi * i / 6 + random.uniform(-0.3, 0.3)
        ln = random.uniform(0.04, 0.08)
        c = Vector((math.cos(a) * 0.064, math.sin(a) * 0.064, -0.01 - ln / 2))
        box_bm(bm, c, (0.025, 0.003, ln), rot=Matrix.Rotation(a + math.pi / 2, 3, "Z") @ Matrix.Rotation(random.uniform(-0.3, 0.3), 3, "X"))
    objs.append(new_obj("Stump_Rags", bm, cloth))

    root = bpy.data.objects.new("AmputationStump", None)
    bpy.context.scene.collection.objects.link(root)
    for o in objs:
        o.parent = root
    return root, objs


# ------------------------------------------------------------------ main
def main():
    os.makedirs(GEAR_DIR, exist_ok=True)
    arm, body = import_character()
    decimate(body)
    rebind_materials(body)
    pc, pcinfo = build_plate_carrier(arm, body)
    attach = {k: list(bone_head(arm, k)) for k in ("Hips", "Spine", "Spine1", "Spine2", "Neck", "Head",
                                                   "RightUpLeg", "RightLeg", "RightFoot", "LeftUpLeg", "LeftLeg",
                                                   "LeftFoot", "LeftArm", "LeftForeArm", "LeftHand",
                                                   "RightArm", "RightForeArm", "RightHand")}
    # Blender (x, y, z) with -Y = front  ->  Unity (-x, z, -y) after FBX axis conversion
    def to_unity(p):
        return [round(-p[0], 4), round(p[2], 4), round(-p[1], 4)]
    out = {"bones_rest_unity": {k: to_unity(v) for k, v in attach.items()},
           "drag_handle_unity": to_unity((0, pcinfo["back_y"] + (0.045 if INCLUDE_PC else 0.0), pcinfo["plate_top"] - (0.005 if INCLUDE_PC else 0.06))),
           "ifak_unity": to_unity((-pcinfo["cummerbund_x"] * 0.55, pcinfo["front_y"] + (0.02 if INCLUDE_PC else 0.04), pcinfo["cummerbund_z"] - 0.03)),
           "plate_carrier": {k: round(v, 4) for k, v in pcinfo.items()}}
    # The Swat model's own (retinted) vest reads as the plate carrier; the procedural shell is only used to
    # measure the torso and is not exported (it floated off the back in bent poses). INCLUDE_PC re-enables it.
    export_objs = [arm, body] + ([pc] if INCLUDE_PC else [])
    export_fbx(os.path.join(CHAR_DIR, "Erlan.fbx"), export_objs, armature=True)

    # gear, one file each
    for builder, fname in ((build_drag_handle, "DragHandle"), (build_ifak, "IFAK_Pouch"), (build_helmet, "Helmet")):
        clear_scene()
        ob = builder()
        objs = [ob] + list(ob.children)
        export_fbx(os.path.join(GEAR_DIR, fname + ".fbx"), objs)
    clear_scene()
    root, parts = build_cat()
    export_fbx(os.path.join(GEAR_DIR, "CAT_Tourniquet.fbx"), [root] + parts)
    clear_scene()
    root, parts = build_stump()
    export_fbx(os.path.join(GEAR_DIR, "AmputationStump.fbx"), [root] + parts)

    with open(os.path.join(GEAR_DIR, "gear_attach.json"), "w") as f:
        json.dump(out, f, indent=2)
    log("done")


main()
