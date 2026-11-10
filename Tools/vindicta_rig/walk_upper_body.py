"""Gives Vindicta_Walk a living upper body on top of its existing leg cycle.

Same idea as Tools/seven_rig/walk_upper_body.py: her first walk moved hips/spine/chest < 3.5 deg,
arms 1.5 deg and neck/clavicles not at all. She holds the rifle with both hands in front of the
chest, so the arms do NOT swing on their own (the left hand would leave the gun): the motion comes
from the torso and the rifle rides along with it.
  - pelvis yaw towards the leg that steps forward and pelvis drop on the swing side,
  - chest counter-rotation and a lean over the stance leg,
  - forward lean that dips at each heel strike, plus a small rifle bob from the chest,
  - neck/head cancel the twist so her aim/gaze stays steady,
  - sideways weight shift of the hips over the stance foot.

Re-runnable: the untouched clip is kept once as Vindicta_Walk_Base (fake user) and Vindicta_Walk is
rebuilt from it every time. Run inside a Blender session with vindicta_anim.blend open:
    exec(open(r"<repo>/Tools/vindicta_rig/walk_upper_body.py").read())

Vindicta faces -Y in this file, -X is her right, +Z is up. The clip is 24 frames (1..25):
right foot is furthest forward around frame 8, left foot around frame 20.
"""
import bpy, math
from mathutils import Quaternion, Vector

# Degrees. Tweak here and re-run.
PELVIS_YAW = 6.0
PELVIS_ROLL = 4.0
SPINE_YAW, CHEST_YAW = -6.0, -6.0      # shoulders end ~6 deg against the hips (rifle sways a bit)
SPINE_ROLL, CHEST_ROLL = -2.5, -2.5     # upper body leans over the stance leg
SPINE_LEAN, CHEST_LEAN = 2.0, 1.5       # constant forward lean
LEAN_BOB = 1.5                          # extra lean at each heel strike (2x per cycle)
CHEST_SHRUG = 1.2                       # chest rocks back a little between steps (rifle bob)
NECK_YAW, HEAD_YAW = 3.0, 3.5           # cancel the shoulder twist for the head
NECK_ROLL, HEAD_ROLL = 0.7, 0.7
HEAD_LEAN = -2.5                        # head back up so the gaze stays level
HIP_SHIFT = 0.025                       # metres sideways over the stance foot
PHASE_SHIFT = 1.0                       # her legs peak ~1 frame later than Seven's

FRAMES = 24
FIRST = 1

rig = bpy.data.objects["Vindicta_Rig"]
walk = bpy.data.actions["Vindicta_Walk"]
base = bpy.data.actions.get("Vindicta_Walk_Base")
if base is None:
    base = walk.copy()
    base.name = "Vindicta_Walk_Base"
    base.use_fake_user = True

UP = Vector((0, 0, 1))        # yaw axis: + turns the front to her left
BACK = Vector((0, 1, 0))      # roll axis: + tilts the top to her left
RIGHT = Vector((-1, 0, 0))    # pitch axis: + leans back

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
    p = 2 * math.pi * (f - FIRST - PHASE_SHIFT) / FRAMES
    s = math.sin(p)            # +1: right leg forward, -1: left leg forward
    c = math.cos(p)            # -1: mid right stance, +1: mid left stance
    c2 = math.cos(2 * p)       # -1 at both heel strikes
    lean = LEAN_BOB * -c2
    rot = {
        "Hips": [(UP, PELVIS_YAW * s), (BACK, -PELVIS_ROLL * c)],
        "Spine": [(UP, SPINE_YAW * s), (BACK, SPINE_ROLL * -c), (RIGHT, -(SPINE_LEAN + lean * 0.6))],
        "Chest": [(UP, CHEST_YAW * s), (BACK, CHEST_ROLL * -c),
                  (RIGHT, -(CHEST_LEAN + lean * 0.4) + CHEST_SHRUG * c2)],
        "Neck": [(UP, NECK_YAW * s), (BACK, NECK_ROLL * -c)],
        "Head": [(UP, HEAD_YAW * s), (BACK, HEAD_ROLL * -c),
                 (RIGHT, -HEAD_LEAN + lean * 0.5 - CHEST_SHRUG * c2)],
    }
    shift = -HIP_SHIFT * c     # + = towards her right
    return rot, shift


# Rebuild the walk from the untouched base clip.
rig.animation_data.action = base
scene = bpy.context.scene
frames = list(range(FIRST, FIRST + FRAMES + 1))
orig = {}
for f in frames:
    scene.frame_set(f)
    orig[f] = {pb.name: (pb.rotation_quaternion.copy(), pb.location.copy()) for pb in rig.pose.bones}

rig.animation_data.action = walk
if hasattr(walk, "slots") and len(walk.slots) and hasattr(rig.animation_data, "action_slot"):
    rig.animation_data.action_slot = walk.slots[0]

hips_side = local_axis("Hips", RIGHT)
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
            pb.location = l0 + hips_side * shift
            pb.keyframe_insert("location", frame=f, group=pb.name)

scene.frame_set(FIRST)
result_walk_upper_body = {"frames": len(frames), "base": base.name}
