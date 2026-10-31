"""Rigs the three Sapphire minions and makes their clips. Run inside Blender (Scripting tab, or through the MCP):
the Microsoft Store build refuses to start from a terminal.

The Sapphire models (Sapphire_Trooper / Sapphire_Melee / Sapphire_Medic .glb from before the rig, in git
history) have the same pose and proportions as the Amber ones, so they reuse the same bones; only the
weapon and torso rules change (an obelisk head and a key instead of a candle and a mace). lib.py,
anim_lib.py and clips.py are loaded as the text blocks "minion_lib", "minion_anim_lib" and "minion_clips".
Rigs and actions are prefixed S (STrooper_Rig, STrooper_Idle...) so they can live next to the Amber ones.
"""
import bpy, numpy as np

sc = bpy.context.scene
ns = {}
for block in ('minion_lib', 'minion_anim_lib', 'minion_clips'):
    exec(compile(bpy.data.texts[block].as_string(), block, 'exec'), ns)


def obj(suffix):
    return [x for x in sc.objects if x.type == 'MESH' and x.name.endswith(suffix)][0]


# Bone tables: the Amber ones from build_amber.py.
TROOPER = {
    'Hips': ((0, 0.02, 0.50), (0, 0.02, 0.60), None),
    'Chest': ((0, 0.0, 0.60), (0, 0.0, 0.93), 'Hips'),
    'Head': ((0, -0.02, 0.95), (-0.03, -0.1, 1.82), 'Chest'),
    'RightUpperArm': ((-0.22, 0.12, 0.77), (-0.30, 0.28, 0.68), 'Chest'),
    'RightLowerArm': ((-0.30, 0.28, 0.68), (-0.38, 0.40, 0.64), 'RightUpperArm'),
    'RightHand': ((-0.38, 0.40, 0.64), (-0.40, 0.46, 0.62), 'RightLowerArm'),
    'LeftUpperArm': ((0.18, -0.13, 0.76), (0.23, -0.16, 0.56), 'Chest'),
    'LeftLowerArm': ((0.23, -0.16, 0.56), (0.26, -0.19, 0.45), 'LeftUpperArm'),
    'LeftHand': ((0.26, -0.19, 0.45), (0.27, -0.22, 0.40), 'LeftLowerArm'),
    'RightUpperLeg': ((-0.05, 0.05, 0.50), (-0.19, 0.11, 0.30), 'Hips'),
    'RightLowerLeg': ((-0.19, 0.11, 0.30), (-0.18, 0.17, 0.12), 'RightUpperLeg'),
    'RightFoot': ((-0.18, 0.17, 0.12), (-0.20, 0.05, 0.03), 'RightLowerLeg'),
    'LeftUpperLeg': ((0.07, -0.02, 0.50), (0.12, -0.15, 0.32), 'Hips'),
    'LeftLowerLeg': ((0.12, -0.15, 0.32), (0.14, -0.22, 0.14), 'LeftUpperLeg'),
    'LeftFoot': ((0.14, -0.22, 0.14), (0.15, -0.32, 0.03), 'LeftLowerLeg'),
    'Weapon': ((-0.18, 0.1, 0.48), (-0.18, -0.9, 0.48), 'Chest'),
}

MELEE = {
    'Hips': ((-0.02, -0.04, 0.58), (-0.02, -0.05, 0.68), None),
    'Chest': ((-0.02, -0.05, 0.68), (-0.03, -0.10, 1.00), 'Hips'),
    'Head': ((-0.04, -0.06, 1.00), (-0.10, -0.35, 1.70), 'Chest'),
    'Weapon': ((-0.25, 0.01, 0.88), (-0.45, 0.12, 1.85), 'Chest'),       # mace and the arm that holds it
    'LeftUpperArm': ((0.27, -0.21, 0.82), (0.38, -0.14, 0.72), 'Chest'),
    'LeftLowerArm': ((0.38, -0.14, 0.72), (0.50, -0.07, 0.68), 'LeftUpperArm'),
    'LeftHand': ((0.50, -0.07, 0.68), (0.56, -0.05, 0.65), 'LeftLowerArm'),
    'RightUpperLeg': ((-0.08, -0.01, 0.60), (-0.30, 0.02, 0.32), 'Hips'),
    'RightLowerLeg': ((-0.30, 0.02, 0.32), (-0.23, 0.18, 0.15), 'RightUpperLeg'),
    'RightFoot': ((-0.23, 0.18, 0.15), (-0.27, 0.08, 0.02), 'RightLowerLeg'),
    'LeftUpperLeg': ((0.07, -0.04, 0.59), (0.10, -0.32, 0.40), 'Hips'),
    'LeftLowerLeg': ((0.10, -0.32, 0.40), (0.18, -0.28, 0.14), 'LeftUpperLeg'),
    'LeftFoot': ((0.18, -0.28, 0.14), (0.18, -0.42, 0.02), 'LeftLowerLeg'),
}

MEDIC = {
    'Hips': ((-0.02, -0.02, 0.58), (-0.02, -0.02, 0.68), None),
    'Chest': ((-0.02, -0.02, 0.68), (-0.02, -0.04, 1.00), 'Hips'),
    'Head': ((-0.02, -0.04, 1.00), (-0.02, -0.05, 1.95), 'Chest'),
    'Weapon': ((-0.25, 0.0, 0.82), (-0.45, -0.1, 2.0), 'Chest'),           # banner pole, cloth and the right arm
    'LeftUpperArm': ((0.22, -0.12, 0.80), (0.25, -0.41, 0.72), 'Chest'),
    'LeftLowerArm': ((0.25, -0.41, 0.72), (0.20, -0.62, 0.69), 'LeftUpperArm'),
    'LeftHand': ((0.20, -0.62, 0.69), (0.20, -0.74, 0.68), 'LeftLowerArm'),   # holds the horn the orb leaves from
    'RightUpperLeg': ((-0.12, 0.10, 0.62), (-0.30, 0.08, 0.38), 'Hips'),
    'RightLowerLeg': ((-0.30, 0.08, 0.38), (-0.31, 0.32, 0.10), 'RightUpperLeg'),
    'RightFoot': ((-0.31, 0.32, 0.10), (-0.32, 0.20, 0.02), 'RightLowerLeg'),
    'LeftUpperLeg': ((0.10, -0.15, 0.59), (0.12, -0.31, 0.45), 'Hips'),
    'LeftLowerLeg': ((0.12, -0.31, 0.45), (0.18, -0.25, 0.15), 'LeftUpperLeg'),
    'LeftFoot': ((0.18, -0.25, 0.15), (0.18, -0.40, 0.02), 'LeftLowerLeg'),
}


# ------------------------------------------------------------------ Trooper: obelisk head, a long key as the rifle
def trooper_weapon(c, mn, mx):
    ring = c[2] > 0.85 and abs(c[0] + 0.05) < 0.3 and abs(c[1]) < 0.3     # the collar around the torso is not the key
    if (mx[1] - mn[1]) > 0.45 and not ring:
        return True
    return mn[0] > -0.285 and mx[0] < -0.04 and mn[2] > 0.12 and mx[2] < 0.95 and (c[1] < -0.2 or c[1] > 0.3)


def trooper_pre(c, mn, mx, n):
    if abs(c[0] + 0.04) < 0.27 and abs(c[1]) < 0.3:
        if 0.62 < c[2] < 1.06:
            return 'Chest'
        if 0.48 < c[2] <= 0.62 and abs(c[0] + 0.04) < 0.2:
            return 'Hips'
    return None


body = obj('sapphire_trooper_01.vmdl_c.sapphire_trooper_01_model')
head = obj('sapphire_trooper_01.vmdl_c.head_health_high')
ns['build_rig'](sc, 'STrooper_Rig', body, {head: 'Head'}, TROOPER, trooper_weapon, 'Weapon', pre=trooper_pre)

# ------------------------------------------------------------------ Melee: a key as the weapon, raised overhead
P0, P1 = (-0.93, -0.43, 0.6), (-0.42, 0.12, 1.98)   # the weapon's axis, from its ring to its head


def melee_weapon(c, mn, mx):
    d = ns['seg_dist'](np.array(c), P0, P1)
    ext = max(mx[0] - mn[0], mx[1] - mn[1], mx[2] - mn[2])
    if c[0] < -0.37:
        return True
    if c[0] < -0.30 and 0.72 <= c[2] < 0.97:
        return True
    if c[0] < -0.30 and c[2] < 0.72 and ext > 0.15:
        return True
    return (c[0] < -0.28 and c[2] > 1.2 and d < 0.28) or (c[2] > 1.45 and d < 0.5)


def melee_pre(c, mn, mx, n):
    if 0.58 < c[2] < 1.06 and abs(c[0] + 0.03) < 0.26 and -0.32 < c[1] < 0.22:
        return 'Chest'
    if n < 150 and 0.55 < c[2] < 1.15 and abs(c[0] + 0.03) < 0.5 and -0.5 < c[1] < 0.5:
        return 'Chest'
    if 1.0 <= c[2] < 2.0 and c[1] < 0.3 and c[0] > -0.5:
        return 'Head'
    return None


body = obj('sapphire_trooper_01_melee.vmdl_c.sapphire_trooper_01_model')
head = obj('sapphire_trooper_01_melee.vmdl_c.head_health_high')
ns['build_rig'](sc, 'SMelee_Rig', body, {head: 'Head'}, MELEE, melee_weapon, 'Weapon', pre=melee_pre)

# ------------------------------------------------------------------ Medic: banner (cloth included in the body mesh), horn in the left hand
def medic_weapon(c, mn, mx):
    return c[0] < -0.33 and c[1] < 0.12 and c[2] > 0.0 and not (c[2] < 0.3 and c[1] > 0.2)


def medic_pre(c, mn, mx, n):
    if 0.58 < c[2] < 1.06 and abs(c[0] + 0.02) < 0.24 and -0.3 < c[1] < 0.2:
        return 'Chest'
    if 1.0 <= c[2] < 2.2 and abs(c[0] + 0.02) < 0.4 and c[1] < 0.3:
        return 'Head'
    return None


body = obj('sapphire_medic_01.vmdl_c.sapphire_medic_01_fractured_model')
head = obj('sapphire_medic_01.vmdl_c.head_health_high')
ns['build_rig'](sc, 'SMedic_Rig', body, {head: 'Head'}, MEDIC, medic_weapon, 'Weapon', pre=medic_pre)

sc.render.fps = 24
for rig, make, prefix in (('STrooper_Rig', 'make_trooper', 'STrooper'), ('SMelee_Rig', 'make_melee', 'SMelee'), ('SMedic_Rig', 'make_medic', 'SMedic')):
    ns[make](ns['Kit'](sc.objects[rig]), prefix)
print("Sapphire minions rigged and animated")
