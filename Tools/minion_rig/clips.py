
import bpy, math

def ease(x):
    x = max(0.0, min(1.0, x)); return x*x*(3-2*x)

def make_trooper(K, name):
    R=K.R
    def walk(t):
        x=2*math.pi*t; tw=K.walk_legs(t,A=30,kneeA=42,foot=6,hipsTwist=3)
        K.setq('Chest',R('Z',-tw*0.7)@R('X',3.0)); K.setq('Head',R('Z',tw*0.5)@R('X',-2.0))
        K.setq('Weapon',R('X',1.5*math.sin(2*x))@R('Z',1.0*math.sin(x)))
        K.setq('RightUpperArm',R('X',-3*math.sin(x))); K.setq('LeftUpperArm',R('X',3*math.sin(x)))
        bpy.context.view_layer.update(); K.ground_hips()
    def idle(t):
        x=2*math.pi*t; s=math.sin(x)
        K.setq('Chest',R('X',-1.2*s)@R('Z',0.8*math.sin(x+0.6))); K.setq('Head',R('Z',3*math.sin(x+1.2))@R('X',1.0*math.sin(2*x)))
        K.setq('Weapon',R('X',0.8*math.sin(x+0.4))@R('Z',0.6*math.sin(x+1.1)))
        K.setq('RightUpperArm',R('X',1.2*math.sin(x+0.7))); K.setq('LeftUpperArm',R('X',-1.2*math.sin(x+0.7)))
        K.setq('Hips',R('Z',1.0*math.sin(x+0.5))); K.setloc('Hips',(0,0,0.01*s))
    def attack(t):
        k=math.exp(-6.0*t)*math.sin(math.pi*min(1.0,t*4.0)) if t<0.25 else math.exp(-6.0*t)*0.5*(1-t)
        k=max(0.0,k)*1.7
        K.setloc('Weapon',(0,0.07*k,0.01*k)); K.setq('Weapon',R('X',-5*k)); K.setq('Chest',R('X',-4*k)); K.setq('Head',R('X',-6*k))
        K.setq('RightUpperArm',R('X',-6*k)); K.setq('LeftUpperArm',R('X',-5*k)); K.setq('RightLowerArm',R('X',-4*k)); K.setloc('Hips',(0,0.02*k,-0.015*k))
    K.action(name+'_Idle',72,idle,step=4); K.action(name+'_Walk',20,walk); K.action(name+'_Attack',12,attack,loop=False); K.clear()

def make_melee(K, name):
    R=K.R
    def walk(t):
        x=2*math.pi*t; tw=K.walk_legs(t,A=30,kneeA=42,foot=6,hipsTwist=4)
        K.setq('Chest',R('Z',-tw*0.7)@R('X',5.0)); K.setq('Head',R('Z',tw*0.5)@R('X',-3.0))
        K.setq('Weapon',R('X',3*math.sin(2*x))@R('Z',2.0*math.sin(x)))
        K.setq('LeftUpperArm',R('X',8*math.sin(x))); K.setq('LeftLowerArm',R('X',-4*math.sin(x+1)))
        bpy.context.view_layer.update(); K.ground_hips()
    def idle(t):
        x=2*math.pi*t; s=math.sin(x)
        K.setq('Chest',R('X',-1.5*s)@R('Z',1.0*math.sin(x+0.6))); K.setq('Head',R('Z',3*math.sin(x+1.2))@R('X',1.2*math.sin(2*x)))
        K.setq('Weapon',R('X',2.0*math.sin(x+0.4))@R('Z',1.5*math.sin(x+1.1)))
        K.setq('LeftUpperArm',R('X',2*math.sin(x+0.7))); K.setq('LeftLowerArm',R('X',-2*math.sin(x+1.0)))
        K.setq('Hips',R('Z',1.2*math.sin(x+0.5))); K.setloc('Hips',(0,0,0.012*s))
    def attack(t):
        if t<0.28: w,c,la,drop=-30*ease(t/0.28),-8*ease(t/0.28),-15*ease(t/0.28),0
        elif t<0.48:
            u=ease((t-0.28)/0.20); w,c,la,drop=-30+125*u,-8+22*u,-15+45*u,0.05*u
        else:
            u=ease((t-0.48)/0.52); w,c,la,drop=95*(1-u),14*(1-u),30*(1-u),0.05*(1-u)
        K.setq('Weapon',R('X',w)); K.setq('Chest',R('X',c)); K.setq('Head',R('X',c*0.7)); K.setq('LeftUpperArm',R('X',la))
        K.setloc('Hips',(0,-0.03*drop*4,-drop))
    K.action(name+'_Idle',72,idle,step=4); K.action(name+'_Walk',20,walk); K.action(name+'_Attack',17,attack,loop=False); K.clear()

def make_medic(K, name):
    R=K.R
    def walk(t):
        x=2*math.pi*t; tw=K.walk_legs(t,A=26,kneeA=38,foot=6,hipsTwist=3)
        K.setq('Chest',R('Z',-tw*0.7)@R('X',3.0)); K.setq('Head',R('Z',tw*0.5)@R('X',-2.0))
        K.setq('Weapon',R('X',1.5*math.sin(2*x))@R('Z',1.0*math.sin(x))); K.setq('LeftUpperArm',R('X',5*math.sin(x)))
        bpy.context.view_layer.update(); K.ground_hips()
    def idle(t):
        x=2*math.pi*t; s=math.sin(x)
        K.setq('Chest',R('X',-1.2*s)@R('Z',0.8*math.sin(x+0.6))); K.setq('Head',R('Z',3*math.sin(x+1.2)))
        K.setq('Weapon',R('X',1.0*math.sin(x+0.4))@R('Z',0.8*math.sin(x+1.1)))
        K.setq('LeftUpperArm',R('X',1.5*math.sin(x+0.7))); K.setq('LeftLowerArm',R('X',-1.5*math.sin(x+1.0))); K.setloc('Hips',(0,0,0.01*s))
    def attack(t):   # a thrust with the banner pole
        k=ease(t/0.3) if t<0.3 else (1-ease((t-0.3)/0.7))
        K.setq('Weapon',R('X',38*k)@R('Z',4*k)); K.setq('Chest',R('X',8*k)); K.setq('Head',R('X',6*k)); K.setloc('Hips',(0,-0.04*k,-0.02*k))
    def heal(t):     # wind up the left arm, flick the orb out at ~0.2 s, settle
        if t<0.2: a=ease(t/0.2); up,lo,ch=-35*a,30*a,-5*a
        elif t<0.32: u=ease((t-0.2)/0.12); up,lo,ch=-35+75*u,30-70*u,-5+11*u
        else: u=ease((t-0.32)/0.68); up,lo,ch=40*(1-u),-40*(1-u),6*(1-u)
        K.setq('LeftUpperArm',R('X',up)); K.setq('LeftLowerArm',R('X',lo)); K.setq('LeftHand',R('X',lo*0.4)); K.setq('Chest',R('X',ch)); K.setq('Head',R('X',ch*0.6))
    K.action(name+'_Idle',72,idle,step=4); K.action(name+'_Walk',20,walk); K.action(name+'_Attack',17,attack,loop=False); K.action(name+'_Heal',19,heal,loop=False); K.clear()
