import bpy, sys, math, mathutils, os
a=sys.argv[sys.argv.index('--')+1:]
plane,pilotblend,rigname,mx,my,mz,scl,out=a[0],a[1],a[2],float(a[3]),float(a[4]),float(a[5]),float(a[6]),a[7]
shots=[s.split(':') for s in a[8].split(',')]
bpy.ops.wm.open_mainfile(filepath=plane)
sc=bpy.context.scene
for o in sc.objects:
    if 'Canopy' in o.name: o.hide_render=True
with bpy.data.libraries.load(pilotblend) as (src,dst): dst.objects=list(src.objects); dst.actions=list(src.actions)
for o in dst.objects:
    sc.collection.objects.link(o)
    if o.type=='ARMATURE': rig=o; o.location=(mx,my,mz); o.scale=(scl,)*3
if os.environ.get('BASETEX'):
    ni=bpy.data.images.load(os.environ['BASETEX'])
    for mat in bpy.data.materials:
        if mat.node_tree and not mat.name.startswith(('Pilot','Wolf')):
            for n in mat.node_tree.nodes:
                if n.type=='TEX_IMAGE' and n.image and 'BaseColor' in n.image.filepath: n.image=ni
w=bpy.data.worlds.new('W'); sc.world=w; w.use_nodes=True; w.node_tree.nodes['Background'].inputs[0].default_value=(0.55,0.65,0.8,1)
l=bpy.data.lights.new('k','SUN'); l.energy=3; lo=bpy.data.objects.new('k',l); sc.collection.objects.link(lo); lo.rotation_euler=(0.8,0,0.7)
cam=bpy.data.cameras.new('C'); cam.lens=40; co=bpy.data.objects.new('C',cam); sc.collection.objects.link(co); sc.camera=co
sc.render.engine='BLENDER_EEVEE'; sc.eevee.taa_render_samples=8; sc.render.resolution_x=sc.render.resolution_y=600
tgt=mathutils.Vector((mx,my-0.3*scl,mz+0.6*scl))
d=mathutils.Vector(tuple(float(v) for v in os.environ.get("CAMD","-0.8,-1.0,0.55").split(","))).normalized()
co.location=tgt+d*2.2*scl/1.55; co.rotation_euler=(-d).to_track_quat('-Z','Y').to_euler()
for act,fr in shots:
    rig.animation_data.action=bpy.data.actions[act]; sc.frame_set(int(fr))
    sc.render.filepath=f'{out}_{act}_{fr}.png'; bpy.ops.render.render(write_still=True)
