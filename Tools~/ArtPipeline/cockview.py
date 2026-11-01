import bpy, sys, math, mathutils, os
argv=sys.argv[sys.argv.index('--')+1:]
blend,out=argv[0],argv[1]
bpy.ops.wm.open_mainfile(filepath=blend)
sc=bpy.context.scene
if os.environ.get('MESHFIX'): exec(open(os.environ['MESHFIX']).read())
if os.environ.get('BASETEX'):
    ni=bpy.data.images.load(os.environ['BASETEX'])
    for mat in bpy.data.materials:
        if mat.node_tree:
            for n in mat.node_tree.nodes:
                if n.type=='TEX_IMAGE' and n.image and ('BaseColor' in n.image.filepath or n.image.name=='Image'): n.image=ni
if os.environ.get('PILOT'):
    pf,rigname,mx,my,mz,scl=os.environ['PILOT'].split(',')
    with bpy.data.libraries.load(pf) as (src,dst): dst.objects=[n for n in src.objects]
    for o in dst.objects:
        sc.collection.objects.link(o)
        if o.type=='ARMATURE': o.location=(float(mx),float(my),float(mz)); o.scale=(float(scl),)*3
ck=[o for o in sc.objects if o.name.endswith('_Cockpit') or o.name=='Pit_Static'][0]
for o in sc.objects:
    if 'Canopy' in o.name or (o.name.endswith('_Cockpit') and not os.environ.get('SHOWBOX')): o.hide_render=True
pts=[ck.matrix_world@mathutils.Vector(c) for c in ck.bound_box]
ctr=sum(pts,mathutils.Vector())/8
cm=bpy.data.materials.new('clay'); cm.use_nodes=True
b=cm.node_tree.nodes['Principled BSDF']; b.inputs['Base Color'].default_value=(0.6,0.6,0.62,1); b.inputs['Roughness'].default_value=0.3
if os.environ.get('CLAY'):
    for o in sc.objects:
        if o.type=='MESH':
            for sl in o.material_slots: sl.material=cm
w=bpy.data.worlds.new('W'); sc.world=w; w.use_nodes=True; w.node_tree.nodes['Background'].inputs[0].default_value=(0.55,0.65,0.8,1)
l=bpy.data.lights.new('k','SUN'); l.energy=3; lo=bpy.data.objects.new('k',l); sc.collection.objects.link(lo); lo.rotation_euler=(0.8,0,0.7)
cam=bpy.data.cameras.new('C'); cam.lens=35; co=bpy.data.objects.new('C',cam); sc.collection.objects.link(co); sc.camera=co
sc.render.engine='BLENDER_EEVEE'; sc.eevee.taa_render_samples=8; sc.render.resolution_x=sc.render.resolution_y=800
V={'a':((0.9,0.9,1.0),2.4),'b':((0.0,1.0,0.75),2.0),'c':((0.0,0.35,1.0),0.9)}
for name in os.environ.get('VIEWS','abc'):
    if name=='d':
        eye=[o for o in sc.objects if o.name=='CockpitCamera'][0].matrix_world.translation
        co.location=eye+mathutils.Vector((0,0.25,0.1)); co.rotation_euler=(math.radians(58),0,math.radians(180)); cam.lens=22
    else:
        d,dist=V[name]; d=mathutils.Vector(d).normalized(); co.location=ctr+d*dist; co.rotation_euler=(-d).to_track_quat('-Z','Y').to_euler(); cam.lens=35
    sc.render.filepath=f'{out}_{name}.png'; bpy.ops.render.render(write_still=True)
