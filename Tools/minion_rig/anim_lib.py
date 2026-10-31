
import bpy, math
from mathutils import Vector, Matrix

class Kit:
    def __init__(self, rig):
        self.rig=rig; self.pb=rig.pose.bones
        bpy.context.view_layer.objects.active = rig
        bpy.ops.object.mode_set(mode='POSE')
        self.M={b.name:b.bone.matrix_local.to_3x3() for b in self.pb}
        self.down=Vector((0,0,-1))
        self.N={}
        for s in ('Right','Left'):
            self.N[s+'UpperLeg']=self.dir(s+'UpperLeg').rotation_difference(self.down).to_matrix()
            self.N[s+'LowerLeg']=self.dir(s+'LowerLeg').rotation_difference(self.down).to_matrix()
            self.N[s+'Foot']=self.dir(s+'Foot').rotation_difference(Vector((0,-1,-0.25)).normalized()).to_matrix()
        self.clear(); bpy.context.view_layer.update()
        self.ground=self.low()
    def dir(self,n):
        b=self.rig.data.bones[n]; return (b.tail_local-b.head_local).normalized()
    def has(self,n): return n in self.pb
    def clear(self):
        for b in self.pb:
            b.rotation_mode='QUATERNION'; b.rotation_quaternion=(1,0,0,0); b.location=(0,0,0)
    def setq(self,n,Rl):
        if n in self.pb: self.pb[n].rotation_quaternion=(self.M[n].inverted()@Rl@self.M[n]).to_quaternion()
    def setloc(self,n,v):
        if n in self.pb: self.pb[n].location=self.M[n].inverted()@Vector(v)
    def low(self):
        bpy.context.view_layer.update()
        z=[(self.rig.matrix_world@self.pb[n].head).z for n in ('LeftFoot','RightFoot')]+[(self.rig.matrix_world@self.pb[n].tail).z for n in ('LeftFoot','RightFoot')]
        return min(z)
    def ground_hips(self):
        dz=self.ground-self.low(); self.setloc('Hips',(0,0,dz)); bpy.context.view_layer.update()
    def R(self,ax,deg): return Matrix.Rotation(math.radians(deg),3,ax)
    def action(self,name,frames,fn,step=1,loop=True):
        for a in [a for a in bpy.data.actions if a.name==name]: bpy.data.actions.remove(a)
        act=bpy.data.actions.new(name); self.rig.animation_data_create(); self.rig.animation_data.action=act
        for f in range(1,frames+2,step):
            self.clear(); fn((f-1)/frames); bpy.context.view_layer.update()
            for b in self.pb:
                b.keyframe_insert('rotation_quaternion',frame=f)
                if b.name=='Hips': b.keyframe_insert('location',frame=f)
        act.use_fake_user=True; act.use_frame_range=True; act.frame_start=1; act.frame_end=frames+1; act.use_cyclic=loop
        return act
    # legs from the lunge stance to a neutral stance, swinging about X (forward = -Y)
    def walk_legs(self, t, A=28, knee0=6, kneeA=40, foot=6, hipsTwist=3.0):
        x=2*math.pi*t; R=self.R
        for side,ph in (('Right',0.0),('Left',math.pi)):
            xx=x+ph; th=A*math.sin(xx); kn=knee0+kneeA*((1+math.cos(xx))/2)**3
            Wt=R('X',-th)@self.N[side+'UpperLeg']; Ws=R('X',-(th-kn))@self.N[side+'LowerLeg']; Wf=R('X',-(foot*math.cos(xx)))@self.N[side+'Foot']
            self.setq(side+'UpperLeg',Wt); self.setq(side+'LowerLeg',Wt.inverted()@Ws); self.setq(side+'Foot',Ws.inverted()@Wf)
        tw=hipsTwist*math.sin(x)
        self.setq('Hips',R('Z',tw)); return tw
