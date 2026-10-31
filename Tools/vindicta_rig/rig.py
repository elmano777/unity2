"""Imports Vindicta.glb, builds a humanoid armature from joints.json and skins the meshes. Saves a .blend."""
import bpy, sys, json, time
from mathutils import Vector
from mathutils.geometry import intersect_point_line

args = sys.argv[sys.argv.index("--") + 1:]
glb, jf, out_blend = args[0], args[1], args[2]
J = json.load(open(jf))

bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.gltf(filepath=glb)
meshes = {o.name.rsplit(".", 1)[-1]: o for o in bpy.data.objects if o.type == 'MESH'}

arm_data = bpy.data.armatures.new("Vindicta_Skeleton")
rig = bpy.data.objects.new("Vindicta_Rig", arm_data)
bpy.context.scene.collection.objects.link(rig)
bpy.context.view_layer.objects.active = rig
rig.select_set(True)
bpy.ops.object.mode_set(mode='EDIT')
eb = arm_data.edit_bones
for name, (h, t, par) in J.items():
    b = eb.new(name)
    b.head, b.tail = Vector(h), Vector(t)
for name, (h, t, par) in J.items():
    if par:
        b = eb[name]
        b.parent = eb[par]
        b.use_connect = (eb[par].tail - b.head).length < 1e-4
# Rolls: limbs and spine point their local Z towards the character's front (-Y in this file).
for b in eb:
    b.align_roll(Vector((0, -1, 0)))
bpy.ops.object.mode_set(mode='OBJECT')

report = {}


def rigid(obj, bone):
    for o in bpy.data.objects:
        o.select_set(False)
    vg = obj.vertex_groups.new(name=bone)
    vg.add(range(len(obj.data.vertices)), 1.0, 'REPLACE')
    obj.parent = rig
    obj.matrix_parent_inverse = rig.matrix_world.inverted()
    m = obj.modifiers.new("Armature", 'ARMATURE')
    m.object = rig


rigid(meshes["gun"], "RightHand")

body = meshes["body"]
VOXEL = float(args[3]) if len(args) > 3 else 0.012

# Watertight proxy of the costume: bone heat fails on the raw extracted mesh (split verts, loose islands).
t0 = time.time()
rem = body.modifiers.new("Remesh", 'REMESH')
rem.mode = 'VOXEL'
rem.voxel_size = VOXEL
dec = body.modifiers.new("Decimate", 'DECIMATE')
dec.ratio = 1.0
dg = bpy.context.evaluated_depsgraph_get()
pm = bpy.data.meshes.new_from_object(body.evaluated_get(dg))
report["proxy_full_verts"] = len(pm.vertices)
target = 90000
if len(pm.vertices) > target:
    bpy.data.meshes.remove(pm)
    dec.ratio = target / report["proxy_full_verts"]
    dg = bpy.context.evaluated_depsgraph_get()
    pm = bpy.data.meshes.new_from_object(body.evaluated_get(dg))
body.modifiers.remove(rem)
body.modifiers.remove(dec)
proxy = bpy.data.objects.new("proxy", pm)
proxy.matrix_world = body.matrix_world
bpy.context.scene.collection.objects.link(proxy)
report["proxy_verts"] = len(pm.vertices)
report["proxy_seconds"] = round(time.time() - t0, 1)

# Bone heat fails for every vertex when any loose piece cannot see a bone, so solve each piece alone.
for o in bpy.data.objects:
    o.select_set(False)
proxy.select_set(True)
bpy.context.view_layer.objects.active = proxy
bpy.ops.object.mode_set(mode='EDIT')
bpy.ops.mesh.select_all(action='SELECT')
bpy.ops.mesh.separate(type='LOOSE')
bpy.ops.object.mode_set(mode='OBJECT')
parts = [o for o in bpy.data.objects if o.type == 'MESH' and o.name.startswith("proxy")]
report["proxy_parts"] = len(parts)
t0 = time.time()
from mathutils.kdtree import KDTree
samples = []  # (world co, {bone: weight}) from every piece bone heat could solve
solved = []
for part in parts:
    if len(part.data.vertices) < 50:
        continue
    for o in bpy.data.objects:
        o.select_set(False)
    part.select_set(True)
    rig.select_set(True)
    bpy.context.view_layer.objects.active = rig
    bpy.ops.object.parent_set(type='ARMATURE_AUTO')
    names = {vg.index: vg.name for vg in part.vertex_groups}
    got = 0
    for v in part.data.vertices:
        w = {names[g.group]: g.weight for g in v.groups if g.weight > 1e-4}
        if w:
            samples.append((part.matrix_world @ v.co, w))
            got += 1
    solved.append([len(part.data.vertices), got])
report["heat_seconds"] = round(time.time() - t0, 1)
report["parts_solved"] = sorted(solved, key=lambda x: -x[0])[:15]

# Transfer: each costume vertex averages the weights of its nearest solved proxy vertices.
kd = KDTree(len(samples))
for i, (co, _) in enumerate(samples):
    kd.insert(co, i)
kd.balance()
pweights = [w for _, w in samples]

bones = [(b.name, b.head_local.copy(), b.tail_local.copy()) for b in arm_data.bones]


def nearest_bone(co):
    best, bd = None, 1e9
    for n, h, t in bones:
        p, f = intersect_point_line(co, h, t)
        f = min(max(f, 0.0), 1.0)
        d = (co - (h + (t - h) * f)).length
        if d < bd:
            best, bd = n, d
    return best


bw = body.matrix_world
vgs = {b.name: body.vertex_groups.new(name=b.name) for b in arm_data.bones}
fallback = 0
for v in body.data.vertices:
    co = bw @ v.co
    acc = {}
    for _, idx, d in kd.find_n(co, 6):
        k = 1.0 / (d + 1e-3)
        for n, w in pweights[idx].items():
            acc[n] = acc.get(n, 0.0) + w * k
    if not acc:
        acc = {nearest_bone(co): 1.0}
        fallback += 1
    # Keep the 4 strongest influences (what Unity skins with) and normalise.
    top = sorted(acc.items(), key=lambda x: -x[1])[:4]
    s = sum(w for _, w in top)
    for n, w in top:
        if w / s > 0.01:
            vgs[n].add([v.index], w / s, 'REPLACE')
report["fallback_verts"] = fallback
for part in parts:
    bpy.data.objects.remove(part)

body.parent = rig
body.matrix_parent_inverse = rig.matrix_world.inverted()
m = body.modifiers.new("Armature", 'ARMATURE')
m.object = rig
report["body_verts"] = len(body.data.vertices)
report["groups"] = [vg.name for vg in body.vertex_groups]
bpy.ops.wm.save_as_mainfile(filepath=out_blend)
print("RESULT" + json.dumps(report))
