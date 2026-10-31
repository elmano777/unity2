"""Rigs the three Amber minions and makes their clips. Run inside Blender (Scripting tab, or through the MCP):
the Microsoft Store build refuses to start from a terminal.

Needs the static models (Amber_Trooper / Amber_Melee / Amber_Medic .glb from before the rig, in git history)
imported into the scene, and lib.py / anim_lib.py loaded as the text blocks "minion_lib" and "minion_anim_lib".
Each model is a barrel torso, a candle head and rigid limbs made of loose pieces, so skinning is rigid:
every loose island goes wholly to the nearest bone, with rules for the weapon and the torso.
Everything faces -Y; "Right" is the -X side. Bone positions were read off island centroids and renders.
"""
import bpy, math

sc = bpy.context.scene
ns = {}
exec(compile(bpy.data.texts['minion_lib'].as_string(), 'minion_lib', 'exec'), ns)
exec(compile(bpy.data.texts['minion_anim_lib'].as_string(), 'minion_anim_lib', 'exec'), ns)


def obj(suffix):
    return [x for x in sc.objects if x.type == 'MESH' and x.name.endswith(suffix)][0]


def ease(x):
    x = max(0.0, min(1.0, x))
    return x * x * (3 - 2 * x)


# ------------------------------------------------------------------ Trooper (ranged, rifle)
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


def trooper_weapon(c, mn, mx):
    if (mx[1] - mn[1]) > 0.45:
        return True
    return mn[0] > -0.285 and mx[0] < -0.04 and mn[2] > 0.12 and mx[2] < 0.95 and (c[1] < -0.2 or c[1] > 0.3)


body = obj('amber_trooper_01_model')
head = obj('amber_trooper_01.vmdl_c.head_health_high')
ns['build_rig'](sc, 'Trooper_Rig', body, {head: 'Head'}, TROOPER, trooper_weapon, 'Weapon')

# ------------------------------------------------------------------ Melee (mace, overhead swing)
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


P0, P1 = (-0.93, -0.43, 0.6), (-0.42, 0.12, 1.98)   # the mace's axis, from its ring to its gear


def melee_weapon(c, mn, mx):
    import numpy as np
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


body = obj('amber_trooper_01_melee_model')
head = obj('amber_trooper_01_melee.vmdl_c.head_health_high')
ns['build_rig'](sc, 'Melee_Rig', body, {head: 'Head'}, MELEE, melee_weapon, 'Weapon', pre=melee_pre)

# ------------------------------------------------------------------ Medic (banner pole, orb horn in the left hand)
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


def medic_weapon(c, mn, mx):
    return c[0] < -0.33 and c[1] < 0.12 and c[2] > 0.0 and not (c[2] < 0.3 and c[1] > 0.2)


def medic_pre(c, mn, mx, n):
    if 0.58 < c[2] < 1.06 and abs(c[0] + 0.02) < 0.24 and -0.3 < c[1] < 0.2:
        return 'Chest'
    if 1.0 <= c[2] < 2.2 and abs(c[0] + 0.02) < 0.4 and c[1] < 0.3:
        return 'Head'
    return None


body = obj('amber_medic_01_fractured_model')
head = obj('amber_medic_01.vmdl_c.head_health_high')
cloth = obj('amber_medic_01.vmdl_c.banner_cloth')
ns['build_rig'](sc, 'Medic_Rig', body, {head: 'Head', cloth: 'Weapon'}, MEDIC, medic_weapon, 'Weapon', pre=medic_pre)

sc.render.fps = 24

# ------------------------------------------------------------------ clips. +X pitches forward (top toward -Y).
K = ns['Kit'](sc.objects['Trooper_Rig'])
R = K.R


def t_walk(t):
    x = 2 * math.pi * t
    tw = K.walk_legs(t, A=30, kneeA=42, foot=6, hipsTwist=3)
    K.setq('Chest', R('Z', -tw * 0.7) @ R('X', 3.0))
    K.setq('Head', R('Z', tw * 0.5) @ R('X', -2.0))
    K.setq('Weapon', R('X', 1.5 * math.sin(2 * x)) @ R('Z', 1.0 * math.sin(x)))
    K.setq('RightUpperArm', R('X', -3 * math.sin(x)))
    K.setq('LeftUpperArm', R('X', 3 * math.sin(x)))
    bpy.context.view_layer.update()
    K.ground_hips()


def t_idle(t):
    x = 2 * math.pi * t
    s = math.sin(x)
    K.setq('Chest', R('X', -1.2 * s) @ R('Z', 0.8 * math.sin(x + 0.6)))
    K.setq('Head', R('Z', 3 * math.sin(x + 1.2)) @ R('X', 1.0 * math.sin(2 * x)))
    K.setq('Weapon', R('X', 0.8 * math.sin(x + 0.4)) @ R('Z', 0.6 * math.sin(x + 1.1)))
    K.setq('RightUpperArm', R('X', 1.2 * math.sin(x + 0.7)))
    K.setq('LeftUpperArm', R('X', -1.2 * math.sin(x + 0.7)))
    K.setq('Hips', R('Z', 1.0 * math.sin(x + 0.5)))
    K.setloc('Hips', (0, 0, 0.01 * s))


def t_attack(t):      # a shot: the rifle kicks back and the body takes the recoil
    k = math.exp(-6.0 * t) * math.sin(math.pi * min(1.0, t * 4.0)) if t < 0.25 else math.exp(-6.0 * t) * 0.5 * (1 - t)
    k = max(0.0, k) * 1.7
    K.setloc('Weapon', (0, 0.07 * k, 0.01 * k))
    K.setq('Weapon', R('X', -5 * k))
    K.setq('Chest', R('X', -4 * k))
    K.setq('Head', R('X', -6 * k))
    K.setq('RightUpperArm', R('X', -6 * k))
    K.setq('LeftUpperArm', R('X', -5 * k))
    K.setq('RightLowerArm', R('X', -4 * k))
    K.setloc('Hips', (0, 0.02 * k, -0.015 * k))


K.action('Trooper_Idle', 72, t_idle, step=4)
K.action('Trooper_Walk', 20, t_walk)
K.action('Trooper_Attack', 12, t_attack, loop=False)
K.clear()

K = ns['Kit'](sc.objects['Melee_Rig'])
R = K.R


def m_walk(t):
    x = 2 * math.pi * t
    tw = K.walk_legs(t, A=30, kneeA=42, foot=6, hipsTwist=4)
    K.setq('Chest', R('Z', -tw * 0.7) @ R('X', 5.0))
    K.setq('Head', R('Z', tw * 0.5) @ R('X', -3.0))
    K.setq('Weapon', R('X', 3 * math.sin(2 * x)) @ R('Z', 2.0 * math.sin(x)))
    K.setq('LeftUpperArm', R('X', 8 * math.sin(x)))
    K.setq('LeftLowerArm', R('X', -4 * math.sin(x + 1)))
    bpy.context.view_layer.update()
    K.ground_hips()


def m_idle(t):
    x = 2 * math.pi * t
    s = math.sin(x)
    K.setq('Chest', R('X', -1.5 * s) @ R('Z', 1.0 * math.sin(x + 0.6)))
    K.setq('Head', R('Z', 3 * math.sin(x + 1.2)) @ R('X', 1.2 * math.sin(2 * x)))
    K.setq('Weapon', R('X', 2.0 * math.sin(x + 0.4)) @ R('Z', 1.5 * math.sin(x + 1.1)))
    K.setq('LeftUpperArm', R('X', 2 * math.sin(x + 0.7)))
    K.setq('LeftLowerArm', R('X', -2 * math.sin(x + 1.0)))
    K.setq('Hips', R('Z', 1.2 * math.sin(x + 0.5)))
    K.setloc('Hips', (0, 0, 0.012 * s))


def m_attack(t):      # wind up, overhead slam, recover
    if t < 0.28:
        w, c, la, drop = -30 * ease(t / 0.28), -8 * ease(t / 0.28), -15 * ease(t / 0.28), 0
    elif t < 0.48:
        u = ease((t - 0.28) / 0.20)
        w, c, la, drop = -30 + 125 * u, -8 + 22 * u, -15 + 45 * u, 0.05 * u
    else:
        u = ease((t - 0.48) / 0.52)
        w, c, la, drop = 95 * (1 - u), 14 * (1 - u), 30 * (1 - u), 0.05 * (1 - u)
    K.setq('Weapon', R('X', w))
    K.setq('Chest', R('X', c))
    K.setq('Head', R('X', c * 0.7))
    K.setq('LeftUpperArm', R('X', la))
    K.setloc('Hips', (0, -0.03 * drop * 4, -drop))


K.action('Melee_Idle', 72, m_idle, step=4)
K.action('Melee_Walk', 20, m_walk)
K.action('Melee_Attack', 17, m_attack, loop=False)
K.clear()

K = ns['Kit'](sc.objects['Medic_Rig'])
R = K.R


def d_walk(t):
    x = 2 * math.pi * t
    tw = K.walk_legs(t, A=26, kneeA=38, foot=6, hipsTwist=3)
    K.setq('Chest', R('Z', -tw * 0.7) @ R('X', 3.0))
    K.setq('Head', R('Z', tw * 0.5) @ R('X', -2.0))
    K.setq('Weapon', R('X', 1.5 * math.sin(2 * x)) @ R('Z', 1.0 * math.sin(x)))
    K.setq('LeftUpperArm', R('X', 5 * math.sin(x)))
    bpy.context.view_layer.update()
    K.ground_hips()


def d_idle(t):
    x = 2 * math.pi * t
    s = math.sin(x)
    K.setq('Chest', R('X', -1.2 * s) @ R('Z', 0.8 * math.sin(x + 0.6)))
    K.setq('Head', R('Z', 3 * math.sin(x + 1.2)))
    K.setq('Weapon', R('X', 1.0 * math.sin(x + 0.4)) @ R('Z', 0.8 * math.sin(x + 1.1)))
    K.setq('LeftUpperArm', R('X', 1.5 * math.sin(x + 0.7)))
    K.setq('LeftLowerArm', R('X', -1.5 * math.sin(x + 1.0)))
    K.setloc('Hips', (0, 0, 0.01 * s))


def d_attack(t):      # a thrust with the banner pole
    k = ease(t / 0.3) if t < 0.3 else (1 - ease((t - 0.3) / 0.7))
    K.setq('Weapon', R('X', 38 * k) @ R('Z', 4 * k))
    K.setq('Chest', R('X', 8 * k))
    K.setq('Head', R('X', 6 * k))
    K.setloc('Hips', (0, -0.04 * k, -0.02 * k))


def d_heal(t):        # wind up the left arm, flick the orb out at ~0.2 s, settle
    if t < 0.2:
        a = ease(t / 0.2)
        up, lo, ch = -35 * a, 30 * a, -5 * a
    elif t < 0.32:
        u = ease((t - 0.2) / 0.12)
        up, lo, ch = -35 + 75 * u, 30 - 70 * u, -5 + 11 * u
    else:
        u = ease((t - 0.32) / 0.68)
        up, lo, ch = 40 * (1 - u), -40 * (1 - u), 6 * (1 - u)
    K.setq('LeftUpperArm', R('X', up))
    K.setq('LeftLowerArm', R('X', lo))
    K.setq('LeftHand', R('X', lo * 0.4))
    K.setq('Chest', R('X', ch))
    K.setq('Head', R('X', ch * 0.6))


K.action('Medic_Idle', 72, d_idle, step=4)
K.action('Medic_Walk', 20, d_walk)
K.action('Medic_Attack', 17, d_attack, loop=False)
K.action('Medic_Heal', 19, d_heal, loop=False)
K.clear()
print("Amber minions rigged and animated")
