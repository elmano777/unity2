
import bpy, bmesh, math, json
import numpy as np
from mathutils import Vector, Matrix

def islands_of(obj):
    bm = bmesh.new(); bm.from_mesh(obj.data); bm.verts.ensure_lookup_table()
    co = np.array([v.co[:] for v in bm.verts]); seen=set(); out=[]
    for v in bm.verts:
        if v.index in seen: continue
        st=[v]; seen.add(v.index); idx=[]
        while st:
            x=st.pop(); idx.append(x.index)
            for e in x.link_edges:
                q=e.other_vert(x)
                if q.index not in seen: seen.add(q.index); st.append(q)
        out.append(idx)
    bm.free(); return co, out

def seg_dist(p, h, t):
    h=np.array(h); t=np.array(t); d=t-h; L=d@d
    f = 0 if L==0 else max(0,min(1,((p-h)@d)/L))
    return float(np.linalg.norm(p-(h+d*f)))

def build_rig(sc, rig_name, body, extras, J, weapon_pred, weapon_bone, head_obj=None, pre=None):
    """body: main mesh object; extras: {object: bone} rigid pieces; J: bones dict; weapon_pred(c,mn,mx)->bool for islands of body."""
    vl = bpy.context.view_layer
    ad = bpy.data.armatures.new(rig_name+'_Skeleton'); rig = bpy.data.objects.new(rig_name, ad); sc.collection.objects.link(rig)
    vl.objects.active = rig
    for o in sc.objects: o.select_set(False)
    rig.select_set(True)
    bpy.ops.object.mode_set(mode='EDIT')
    eb = ad.edit_bones
    for n,(h,t,p) in J.items():
        b=eb.new(n); b.head=Vector(h); b.tail=Vector(t)
    for n,(h,t,p) in J.items():
        if p: eb[n].parent=eb[p]
    for b in eb: b.align_roll(Vector((0,-1,0)))
    bpy.ops.object.mode_set(mode='OBJECT')
    co, isl = islands_of(body)
    for n in J: body.vertex_groups.new(name=n)
    vg = {v.name:v for v in body.vertex_groups}
    names=[n for n in J if n not in ('Weapon',)]
    assign={}
    for idx in isl:
        p=co[idx]; c=p.mean(0); mn=p.min(0); mx=p.max(0)
        pb_=pre(c,mn,mx,len(idx)) if pre else None
        if weapon_pred(c,mn,mx): b=weapon_bone
        elif pb_: b=pb_
        else: b=min(names,key=lambda n: seg_dist(c,J[n][0],J[n][1]))
        vg[b].add(idx,1.0,'REPLACE'); assign[b]=assign.get(b,0)+len(idx)
    body.parent=rig; body.matrix_parent_inverse=rig.matrix_world.inverted()
    body.modifiers.new('Armature','ARMATURE').object=rig
    for o,bone in extras.items():
        g=o.vertex_groups.new(name=bone); g.add(range(len(o.data.vertices)),1.0,'REPLACE')
        o.parent=rig; o.matrix_parent_inverse=rig.matrix_world.inverted()
        o.modifiers.new('Armature','ARMATURE').object=rig
    return rig, assign


def reset_model(sc, rig_name, objs):
    for o in objs:
        for m in [m for m in o.modifiers if m.type=='ARMATURE']: o.modifiers.remove(m)
        o.vertex_groups.clear()
        mw=o.matrix_world.copy(); o.parent=None; o.matrix_world=mw
    r=bpy.data.objects.get(rig_name)
    if r:
        d=r.data; bpy.data.objects.remove(r); bpy.data.armatures.remove(d)
    for o in [o for o in sc.objects if o.name.startswith('dbg_')]: bpy.data.objects.remove(o)

def debug_render(sc, body, head, J, path_prefix, center, scale=2.4):
    import os
    palette=[(1,0.2,0.2),(0.2,1,0.2),(0.3,0.5,1),(1,1,0.2),(1,0.4,1),(0.2,1,1),(1,0.6,0.2),(0.6,0.4,1),(0.8,0.8,0.8),(0.5,1,0.5),(1,0.5,0.5),(0.4,0.8,1),(0.9,0.9,0.4),(0.6,0.2,0.8),(0.2,0.6,0.4),(0.8,0.4,0.2)]
    bm=bmesh.new(); bm.from_mesh(body.data)
    vgn={vg.index:vg.name for vg in body.vertex_groups}
    vb={}
    for v in body.data.vertices:
        if v.groups: vb[v.index]=vgn[v.groups[0].group]
    for k,bn in enumerate(J):
        sub=bmesh.new(); mp={}
        for f in bm.faces:
            if vb.get(f.verts[0].index)==bn:
                vs=[]
                for v in f.verts:
                    if v.index not in mp: mp[v.index]=sub.verts.new(v.co)
                    vs.append(mp[v.index])
                try: sub.faces.new(vs)
                except: pass
        m2=bpy.data.meshes.new('dbg_'+bn); sub.to_mesh(m2); sub.free()
        ob=bpy.data.objects.new('dbg_'+bn,m2); sc.collection.objects.link(ob); ob.color=(*palette[k%len(palette)],1)
    bm.free()
    for h in (head if isinstance(head,list) else [head]):
        hd=bpy.data.objects.new('dbg_h_'+h.name[-8:],h.data); sc.collection.objects.link(hd); hd.color=(1,1,1,1)
    for o in sc.objects:
        if o.type=='MESH' and not o.name.startswith('dbg_'): o.hide_render=True
        if o.name.startswith('dbg_'): o.hide_render=False
    sc.display.shading.color_type='OBJECT'; sc.display.shading.show_xray=False
    cam=sc.camera; cam.data.ortho_scale=scale; sc.render.resolution_x=600; sc.render.resolution_y=600
    cx,cy,cz=center
    for vname,(loc,rot) in {'f':((cx,-30,cz),(math.radians(90),0,0)),'s':((30,cy,cz),(math.radians(90),0,math.radians(90)))}.items():
        cam.location=loc; cam.rotation_euler=rot
        sc.render.filepath=path_prefix+vname+'.png'; bpy.ops.render.render(write_still=True)
