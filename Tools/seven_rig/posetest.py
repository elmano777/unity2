"""Opens the rigged .blend, poses it with world-axis rotations and renders it (args: blend outdir)."""
import bpy, sys, math, os
from mathutils import Vector, Matrix, Quaternion
args = sys.argv[sys.argv.index("--") + 1:]
bpy.ops.wm.open_mainfile(filepath=args[0])
out = args[1]
rig = bpy.data.objects["Seven_Rig"]
P = rig.pose.bones

# world-axis rotations (axis, degrees); the character faces -X, its right is +Y
pose = {
    "RightUpperArm": ((0, 1, 0), -70),   # raise the right arm forward
    "RightLowerArm": ((0, 1, 0), -40),
    "LeftUpperArm": ((1, 0, 0), -60),    # lift the left arm sideways
    "LeftLowerArm": ((0, 0, 1), 40),
    "LeftUpperLeg": ((0, 1, 0), -40),    # step forward with the left leg
    "LeftLowerLeg": ((0, 1, 0), 60),
    "Neck": ((0, 0, 1), 30),
    "Spine": ((0, 0, 1), 15),
}
order = [b.name for b in rig.data.bones]
for name in order:
    if name not in pose:
        continue
    axis, deg = pose[name]
    pb = P[name]
    bpy.context.view_layer.update()
    m = rig.matrix_world @ pb.matrix
    head = m.translation.copy()
    R = Matrix.Rotation(math.radians(deg), 4, Vector(axis))
    new = Matrix.Translation(head) @ R @ Matrix.Translation(-head) @ m
    pb.matrix = rig.matrix_world.inverted() @ new
bpy.context.view_layer.update()

scn = bpy.context.scene
scn.render.engine = 'BLENDER_WORKBENCH'
scn.display.shading.light = 'STUDIO'
scn.display.shading.color_type = 'MATERIAL'
scn.render.resolution_x = scn.render.resolution_y = 900
cd = bpy.data.cameras.new("cam"); cd.type = 'ORTHO'; cd.ortho_scale = 3.4
cam = bpy.data.objects.new("cam", cd); scn.collection.objects.link(cam); scn.camera = cam
c = Vector((-0.4, -0.45, 0.85))
for n, d in {"p_front": Vector((0, -1, 0)), "p_left": Vector((-1, 0, 0)), "p_persp": Vector((-1, -0.8, 0.35)), "p_back": Vector((1, 0.3, 0.2))}.items():
    cam.location = c + d.normalized() * 6
    cam.rotation_euler = (-d).to_track_quat('-Z', 'Y').to_euler()
    scn.render.filepath = os.path.join(out, n + ".png")
    bpy.ops.render.render(write_still=True)
