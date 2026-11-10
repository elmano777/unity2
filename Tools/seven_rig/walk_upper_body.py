"""Gives Seven_Walk a living upper body on top of its existing leg cycle.

The first walk only swung the legs: spine and chest moved < 3 deg, arms ~3 deg, neck and
clavicles not at all, so in game the torso looked bolted on. This adds, in sync with the legs:
  - pelvis yaw towards the leg that steps forward and pelvis drop on the swing side,
  - chest counter-rotation (shoulders twist against the hips) and a lean over the stance leg,
  - a forward lean that dips a little at each heel strike,
  - arm swing opposite to the legs (left arm big, gun arm small), lagging the legs by a bit,
  - clavicles that follow the arms, and neck/head that cancel most of it so the gaze stays steady,
  - a small sideways weight shift of the hips over the stance foot.

Re-runnable: the untouched clip is kept once as Seven_Walk_Base (fake user) and Seven_Walk is
rebuilt from it every time. Run inside a Blender session with seven_anim.blend open:
    exec(open(r"<repo>/Tools/seven_rig/walk_upper_body.py").read())

Seven faces -X in this file, +Y is his right, +Z is up. The clip is 24 frames (1..25):
right foot is furthest forward at frame 7, left foot at frame 19.
"""
import bpy, math
from mathutils import Quaternion, Vector

# Degrees. Tweak here and re-run.
PELVIS_YAW = 7.0
PELVIS_ROLL = 4.5
SPINE_YAW, CHEST_YAW = -7.0, -9.0      # together: shoulders end ~9 deg against the hips
SPINE_ROLL, CHEST_ROLL = -3.0, -3.0     # upper body leans over the stance leg
SPINE_LEAN, CHEST_LEAN = 3.0, 2.0       # constant forward lean
LEAN_BOB = 1.5                          # extra lean at each heel strike (2x per cycle)
NECK_YAW, HEAD_YAW = 4.0, 4.0           # cancel the shoulder twist for the head
NECK_ROLL, HEAD_ROLL = 0.8, 0.8
HEAD_LEAN = -4.0                        # head back up so the gaze stays level
LEFT_ARM_SWING, RIGHT_ARM_SWING = 16.0, 6.0
LEFT_ELBOW, RIGHT_ELBOW = 6.0, 3.0      # extra bend when the arm is in front
CLAVICLE = 4.0
ARM_LAG_FRAMES = 2.0
HIP_SHIFT = 0.025                       # metres sideways over the stance foot

FRAMES = 24
FIRST = 1

rig = bpy.data.objects["Seven_Rig"]
walk = bpy.data.actions["Seven_Walk"]
base = bpy.data.actions.get("Seven_Walk_Base")
if base is None:
    base = walk.copy()
    base.name = "Seven_Walk_Base"
    base.use_fake_user = True

UP = Vector((0, 0, 1))        # yaw axis: + turns the front to his left
BACK = Vector((1, 0, 0))      # roll axis: + tilts the top to his left
RIGHT = Vector((0, 1, 0))     # pitch axis: + leans back, + swings a hanging arm forward

rad = math.radians


def local_axis(bone_name, axis):
    """Armature-space axis expressed in the bone's rest frame (where pose rotations live)."""
    return (rig.data.bones[bone_name].matrix_local.to_3x3().inverted() @ axis).normalized()


def delta(bone_name, pieces):
    q = Quaternion()
    for axis, deg in pieces:
        if abs(deg) > 1e-6:
            q = Quaternion(local_axis(bone_name, axis), rad(deg)) @ q
    return q


def offsets(f):
    p = 2 * math.pi * (f - FIRST) / FRAMES
    s = math.sin(p)            # +1: right leg forward (frame 7), -1: left leg forward (frame 19)
    c = math.cos(p)            # -1: mid right stance (frame 13), +1: mid left stance (frame 1)
    c2 = math.cos(2 * p)       # -1 at both heel strikes
    pa = 2 * math.pi * (f - FIRST - ARM_LAG_FRAMES) / FRAMES
    sa = math.sin(pa)
    lean = LEAN_BOB * -c2
    rot = {
        "Hips": [(UP, PELVIS_YAW * s), (BACK, -PELVIS_ROLL * c)],
        "Spine": [(UP, SPINE_YAW * s), (BACK, SPINE_ROLL * -c), (RIGHT, -(SPINE_LEAN + lean * 0.6))],
        "Chest": [(UP, CHEST_YAW * s), (BACK, CHEST_ROLL * -c), (RIGHT, -(CHEST_LEAN + lean * 0.4))],
        "Neck": [(UP, NECK_YAW * s), (BACK, NECK_ROLL * -c)],
        "Head": [(UP, HEAD_YAW * s), (BACK, HEAD_ROLL * -c), (RIGHT, -HEAD_LEAN + lean * 0.5)],
        # Arms swing against their own leg: left arm forward with the right leg.
        "LeftUpperArm": [(RIGHT, LEFT_ARM_SWING * sa)],
        "RightUpperArm": [(RIGHT, -RIGHT_ARM_SWING * sa)],
        "LeftLowerArm": [(RIGHT, LEFT_ELBOW * max(sa, 0.0))],
        "RightLowerArm": [(RIGHT, RIGHT_ELBOW * max(-sa, 0.0))],
        # Clavicles: the swinging side's shoulder comes forward a little (yaw about up).
        "LeftShoulder": [(UP, -CLAVICLE * sa)],
        "RightShoulder": [(UP, -CLAVICLE * sa)],
    }
    shift = -HIP_SHIFT * c     # + = towards his right (+Y)
    return rot, shift


# Rebuild the walk from the untouched base clip.
rig.animation_data.action = base
scene = bpy.context.scene
frames = list(range(FIRST, FIRST + FRAMES + 1))
orig = {}
for f in frames:
    scene.frame_set(f)
    orig[f] = {pb.name: (pb.rotation_quaternion.copy(), pb.location.copy()) for pb in rig.pose.bones}

slot = base.slots[0] if hasattr(base, "slots") and len(base.slots) else None
rig.animation_data.action = walk
if slot is not None and hasattr(rig.animation_data, "action_slot"):
    rig.animation_data.action_slot = walk.slots[0]

hips_x = local_axis("Hips", RIGHT)    # sideways axis in the hips' frame, for the weight shift
for f in frames:
    rot, shift = offsets(f)
    for pb in rig.pose.bones:
        q0, l0 = orig[f][pb.name]
        q = q0
        if pb.name in rot:
            q = delta(pb.name, rot[pb.name]) @ q0
        pb.rotation_quaternion = q
        pb.keyframe_insert("rotation_quaternion", frame=f, group=pb.name)
        if pb.name == "Hips":
            pb.location = l0 + hips_x * shift
            pb.keyframe_insert("location", frame=f, group=pb.name)

# Seamless loop: last frame equals the first.
scene.frame_set(FIRST)
result_walk_upper_body = {"frames": len(frames), "base": base.name}
