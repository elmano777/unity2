"""Rigs the Guardian and makes its clips. Run it inside Blender (Scripting tab, or through the MCP): the
Microsoft Store build refuses to start from a terminal.

It imports the static model (the version of Assets/Models/Structures/Guardian.glb before the rig, in git
history), builds Guardian_Rig, skins the mesh and leaves the actions Guardian_Idle, Guardian_Attack and
Guardian_Die in a new scene. Then export the rig alone for the clips and the rig with the mesh for the
model, and run Tools/vindicta_rig/tidy_glb.py on both (see README.md).

The model is a robot made of loose rigid pieces, so skinning is rigid: every loose island goes wholly to
the nearest bone, except the faces with the staff material, which all go to Staff.
"""
import bpy, bmesh, json, math, os
import numpy as np
from mathutils import Vector, Matrix

HERE = r"C:\Users\LABORATORIO-L507\Desktop\ihc_yoryi\deadlock\Tools\guardian_rig"
GLB = r"C:\Users\LABORATORIO-L507\Desktop\ihc_yoryi\deadlock\Assets\Models\Structures\Guardian_static.glb"
B = json.load(open(os.path.join(HERE, "joints.json")))   # name: [head, tail, parent]; the Guardian faces -Y

sc = bpy.data.scenes.new("Guardian")
bpy.context.window.scene = sc
vl = bpy.context.view_layer
bpy.ops.import_scene.gltf(filepath=GLB)
g = [o for o in sc.objects if o.type == 'MESH'][0]

# ---- armature
ad = bpy.data.armatures.new("Guardian_Skeleton")
rig = bpy.data.objects.new("Guardian_Rig", ad)
sc.collection.objects.link(rig)
vl.objects.active = rig
rig.select_set(True)
bpy.ops.object.mode_set(mode='EDIT')
eb = ad.edit_bones
for n, (h, t, p) in B.items():
    b = eb.new(n)
    b.head = Vector(h)
    b.tail = Vector(t)
for n, (h, t, p) in B.items():
    if p:
        eb[n].parent = eb[p]
for b in eb:
    b.align_roll(Vector((0, -1, 0)))
bpy.ops.object.mode_set(mode='OBJECT')

# ---- rigid skin: loose islands -> nearest bone; staff material -> Staff
bm = bmesh.new()
bm.from_mesh(g.data)
bm.verts.ensure_lookup_table()
seen, comps = set(), []
for v in bm.verts:
    if v.index in seen:
        continue
    stack, idx = [v], []
    seen.add(v.index)
    while stack:
        x = stack.pop()
        idx.append(x.index)
        for e in x.link_edges:
            o = e.other_vert(x)
            if o.index not in seen:
                seen.add(o.index)
                stack.append(o)
    comps.append(idx)
co = np.array([v.co[:] for v in bm.verts])
vmat = np.zeros(len(bm.verts), dtype=int)
for f in bm.faces:
    for v in f.verts:
        vmat[v.index] = f.material_index
bm.free()


def seg_dist(p, h, t):
    h, t = np.array(h), np.array(t)
    d = t - h
    L = d @ d
    f = 0 if L == 0 else max(0, min(1, ((p - h) @ d) / L))
    return np.linalg.norm(p - (h + d * f))


names = [n for n in B if n not in ('Staff', 'StaffTip')]
for n in B:
    g.vertex_groups.new(name=n)
vg = {v.name: v for v in g.vertex_groups}
for idx in comps:
    c = co[idx].mean(0)
    bone = 'Staff' if vmat[idx].mean() > 0.5 else min(names, key=lambda n: seg_dist(c, B[n][0], B[n][1]))
    vg[bone].add(idx, 1.0, 'REPLACE')
g.parent = rig
g.matrix_parent_inverse = rig.matrix_world.inverted()
g.modifiers.new('Armature', 'ARMATURE').object = rig

# ---- clips (24 fps). Rotations are deltas in armature axes: +X pitches forward (top toward -Y).
pb = rig.pose.bones
bpy.context.view_layer.objects.active = rig
bpy.ops.object.mode_set(mode='POSE')
M = {b.name: b.bone.matrix_local.to_3x3() for b in pb}


def R(ax, deg):
    return Matrix.Rotation(math.radians(deg), 3, ax)


def clear():
    for b in pb:
        b.rotation_mode = 'QUATERNION'
        b.rotation_quaternion = (1, 0, 0, 0)
        b.location = (0, 0, 0)


def setq(n, rl):
    pb[n].rotation_quaternion = (M[n].inverted() @ rl @ M[n]).to_quaternion()


def setloc(n, v):
    pb[n].location = M[n].inverted() @ Vector(v)


def lowest():
    bpy.context.view_layer.update()
    return min([(rig.matrix_world @ pb[n].head).z for n in ('LeftFoot', 'RightFoot')] +
               [(rig.matrix_world @ pb[n].tail).z for n in ('LeftFoot', 'RightFoot')])


clear()
bpy.context.view_layer.update()
GROUND = lowest()


def make_action(name, frames, fn, step=1, loop=True):
    act = bpy.data.actions.new(name)
    rig.animation_data_create()
    rig.animation_data.action = act
    for f in range(1, frames + 2, step):
        clear()
        fn((f - 1) / frames)
        bpy.context.view_layer.update()
        for b in pb:
            b.keyframe_insert('rotation_quaternion', frame=f)
            if b.name == 'Hips':
                b.keyframe_insert('location', frame=f)
    act.use_fake_user = True
    act.use_frame_range = True
    act.frame_start = 1
    act.frame_end = frames + 1
    act.use_cyclic = loop
    return act


def idle(t):
    x = 2 * math.pi * t
    s = math.sin(x)
    setloc('Hips', (0, 0, 0.025 * s))
    setq('Chest', R('X', 0.8 * math.sin(x + 0.5)) @ R('Z', 0.8 * math.sin(x + 1.0)))
    setq('Head', R('Z', 3.0 * math.sin(x + 2.0)) @ R('X', 1.0 * math.sin(2 * x)))
    setq('LeftUpperArm', R('X', 0.8 * math.sin(x + 0.8)))
    setq('LeftLowerArm', R('X', 0.6 * math.sin(x + 1.2)))
    setq('Staff', R('X', 1.0 * math.sin(x + 0.3)) @ R('Y', 1.2 * math.sin(x + 1.5)))
    setq('RightUpperArm', R('X', -1.0 * math.sin(x + 0.8)) @ R('Y', -0.6 * s))
    setq('RightLowerArm', R('X', -0.8 * math.sin(x + 1.0)))


def attack(t):   # the staff kicks back (top toward +Y) and the body takes the recoil
    k = math.exp(-5.0 * t) * math.sin(math.pi * min(1.0, t * 3.0)) if t < 0.34 else math.exp(-5.0 * t) * 0.55 * (1 - t)
    k = max(0.0, k) * 1.6
    setloc('Hips', (0, 0.07 * k, -0.08 * k))
    setq('Chest', R('X', -4.0 * k))
    setq('Head', R('X', 5.0 * k))
    setq('LeftUpperArm', R('X', 6.0 * k))
    setq('LeftLowerArm', R('X', -5.0 * k))
    setq('Staff', R('X', -8.0 * k))
    setq('RightUpperArm', R('X', 3.0 * k))


def die(t):      # the knees fold, the torso and head sag, the feet stay on the floor
    e = 1 - (1 - min(1.0, t / 0.75)) ** 3
    up, lo = -48 * e, 82 * e
    for s in ('Left', 'Right'):
        setq(s + 'UpperLeg', R('X', up))
        setq(s + 'LowerLeg', R('X', lo))
        setq(s + 'Foot', R('X', -(up + lo)))
    setq('Hips', R('X', -6 * e))
    setq('Chest', R('X', 24 * e) @ R('Z', 5 * e))
    setq('Head', R('X', 22 * e) @ R('Z', -8 * e))
    setq('LeftUpperArm', R('X', -20 * e) @ R('Y', -14 * e))
    setq('LeftLowerArm', R('X', 10 * e))
    setq('Staff', R('Y', 28 * e) @ R('X', -6 * e))
    setq('RightUpperArm', R('X', -14 * e) @ R('Y', 18 * e))
    setq('RightLowerArm', R('X', -20 * e))
    bpy.context.view_layer.update()
    setloc('Hips', (0, 0, GROUND - lowest()))


sc.render.fps = 24
make_action('Guardian_Idle', 96, idle, step=4)
make_action('Guardian_Attack', 14, attack)
make_action('Guardian_Die', 60, die, loop=False)
clear()
bpy.ops.object.mode_set(mode='OBJECT')
print("Guardian rig and clips ready")
