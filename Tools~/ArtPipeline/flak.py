# The flak gun had no UVs or texture: unwrap it, bake ambient occlusion and edge wear, paint it field grey with
# gunmetal barrels and black tyres, then export with the same FBX settings as the rest.
import bpy, bmesh, numpy as np, sys, math
OUT=sys.argv[sys.argv.index('--')+1]
bpy.ops.wm.open_mainfile(filepath='/home/claude/art/blender/Flak.blend')
sc=bpy.context.scene; ob=bpy.data.objects['Flak']
bpy.context.view_layer.objects.active=ob; ob.select_set(True)
bpy.ops.object.mode_set(mode='EDIT'); bpy.ops.mesh.select_all(action='SELECT')
bpy.ops.mesh.remove_doubles(threshold=1e-5); bpy.ops.mesh.normals_make_consistent(inside=False)
bpy.ops.uv.smart_project(angle_limit=math.radians(55), island_margin=0.004)
bpy.ops.object.mode_set(mode='OBJECT')
R=2048
sc.render.engine='CYCLES'; sc.cycles.device='CPU'; sc.cycles.samples=64
def bake(kind, setup):
    img=bpy.data.images.new(kind,R,R,alpha=True,float_buffer=True)
    img.pixels.foreach_set(np.zeros(R*R*4,np.float32))
    m=bpy.data.materials.new(kind); m.use_nodes=True; nt=m.node_tree; nt.nodes.clear()
    o=nt.nodes.new('ShaderNodeOutputMaterial'); setup(nt,o)
    t=nt.nodes.new('ShaderNodeTexImage'); t.image=img; nt.nodes.active=t
    ob.data.materials.clear(); ob.data.materials.append(m)
    sc.render.bake.margin=8
    bpy.ops.object.bake(type='EMIT' if kind!='AO' else 'AO')
    return np.array(img.pixels[:],np.float32).reshape(R,R,4)
def ao_setup(nt,o):
    d=nt.nodes.new('ShaderNodeBsdfDiffuse'); nt.links.new(d.outputs[0],o.inputs[0])
def edge_setup(nt,o):
    g=nt.nodes.new('ShaderNodeNewGeometry'); e=nt.nodes.new('ShaderNodeEmission'); nt.links.new(g.outputs['Pointiness'],e.inputs['Color']); nt.links.new(e.outputs[0],o.inputs[0])
def pos_setup(nt,o):
    g=nt.nodes.new('ShaderNodeTexCoord'); e=nt.nodes.new('ShaderNodeEmission'); nt.links.new(g.outputs['Object'],e.inputs['Color']); nt.links.new(e.outputs[0],o.inputs[0])
def nrm_setup(nt,o):
    g=nt.nodes.new('ShaderNodeNewGeometry'); e=nt.nodes.new('ShaderNodeEmission'); nt.links.new(g.outputs['Normal'],e.inputs['Color']); nt.links.new(e.outputs[0],o.inputs[0])
ao=bake('AO',ao_setup)[...,0]
sc.cycles.samples=1
pt=bake('EDGE',edge_setup)[...,0]
pos=bake('POS',pos_setup); P=pos[...,:3]; cover=pos[...,3]>0.5
np.save(OUT+'/flak_maps.npy',np.dstack([ao,pt,P,cover.astype(np.float32)]))
print('baked', cover.mean())
mat=bpy.data.materials.new('Flak_Mat'); mat.use_nodes=True
ob.data.materials.clear(); ob.data.materials.append(mat)
for o in bpy.data.objects: o.select_set(False)
bpy.ops.wm.save_as_mainfile(filepath=OUT+'/Flak.blend')
bpy.ops.export_scene.fbx(filepath=OUT+'/Flak.fbx', object_types={'MESH','EMPTY'}, apply_scale_options='FBX_SCALE_UNITS',
    use_space_transform=True, bake_space_transform=False, path_mode='STRIP', mesh_smooth_type='FACE')
