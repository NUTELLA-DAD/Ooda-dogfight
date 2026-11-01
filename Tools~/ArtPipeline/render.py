import bpy, sys, math, mathutils
argv=sys.argv[sys.argv.index('--')+1:]
blend, out, engine = argv[0], argv[1], (argv[2] if len(argv)>2 else 'EEVEE')
views = argv[3].split(',') if len(argv)>3 else ['34','side','top','close']
bpy.ops.wm.open_mainfile(filepath=blend)
sc=bpy.context.scene
import os
if os.environ.get('BASETEX'):
    ni=bpy.data.images.load(os.environ['BASETEX'])
    for mat in bpy.data.materials:
        if mat.node_tree:
            for n in mat.node_tree.nodes:
                if n.type=='TEX_IMAGE' and n.image and ('BaseColor' in n.image.filepath or n.image.name=='Image'):
                    n.image=ni
if os.environ.get('CLAY'):
    cm=bpy.data.materials.new('clay'); cm.use_nodes=True
    b=cm.node_tree.nodes['Principled BSDF']; b.inputs['Base Color'].default_value=(0.6,0.6,0.62,1); b.inputs['Roughness'].default_value=0.15; b.inputs['Metallic'].default_value=0.6
    for o in sc.objects:
        if o.type=='MESH':
            for sl in o.material_slots: sl.material=cm
if os.environ.get('MESHFIX'):
    exec(open(os.environ['MESHFIX']).read())
for o in sc.objects:
    if 'Canopy' in o.name and os.environ.get('NOCANOPY'): o.hide_render=True
for o in list(bpy.data.objects):
    if o.type in ('CAMERA','LIGHT'): bpy.data.objects.remove(o)
meshes=[o for o in sc.objects if o.type=='MESH' and not o.hide_render]
pts=[o.matrix_world@mathutils.Vector(c) for o in meshes for c in o.bound_box]
mn=mathutils.Vector((min(p.x for p in pts),min(p.y for p in pts),min(p.z for p in pts)))
mx=mathutils.Vector((max(p.x for p in pts),max(p.y for p in pts),max(p.z for p in pts)))
ctr=(mn+mx)/2; rad=(mx-mn).length/2
w=bpy.data.worlds.new('W'); sc.world=w; w.use_nodes=True
bg=w.node_tree.nodes['Background']; bg.inputs[0].default_value=(0.55,0.65,0.8,1); bg.inputs[1].default_value=0.8
def light(name,rot,e):
    l=bpy.data.lights.new(name,'SUN'); l.energy=e; o=bpy.data.objects.new(name,l); sc.collection.objects.link(o); o.rotation_euler=[math.radians(a) for a in rot]
light('key',(50,0,40),3.5); light('fill',(60,0,-140),1.0)
cam=bpy.data.cameras.new('C'); cam.lens=50; co=bpy.data.objects.new('C',cam); sc.collection.objects.link(co); sc.camera=co
cam.clip_end=rad*20; cam.clip_start=rad/200
sc.render.engine='BLENDER_EEVEE' if engine=='EEVEE' else ('BLENDER_WORKBENCH' if engine=='WB' else 'CYCLES')
if engine=='CYCLES': sc.cycles.samples=32; sc.cycles.device='CPU'
sc.render.resolution_x=sc.render.resolution_y=900
try: sc.eevee.taa_render_samples=8
except Exception: pass
sc.view_settings.view_transform='Standard'
# blender forward axis: guess longest horizontal axis
dirs={'34':(1,-1,0.6),'side':(1,0,0.05),'top':(0.01,0,1),'front':(0,-1,0.15),'back':(0,1,0.3),'close':(1,-1,0.6),'under':(0.6,-0.6,-0.6)}
for v in views:
    d=mathutils.Vector(dirs[v]).normalized(); dist=rad*2.8*(0.45 if v=='close' else 1)
    co.location=ctr+d*dist
    co.rotation_euler=(-d).to_track_quat('-Z','Y').to_euler()
    sc.render.filepath=f'{out}_{v}.png'; bpy.ops.render.render(write_still=True)
