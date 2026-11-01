# Bake object-space position (of the root body frame) + coverage mask into a float EXR, per blend, for UV-mapped meshes sharing the main texture.
import bpy, sys, numpy as np
argv=sys.argv[sys.argv.index('--')+1:]
blend, out, res = argv[0], argv[1], int(argv[2])
bpy.ops.wm.open_mainfile(filepath=blend)
sc=bpy.context.scene; sc.render.engine='CYCLES'; sc.cycles.samples=1; sc.cycles.device='CPU'
img=bpy.data.images.new('POS',res,res,alpha=True,float_buffer=True)
img.pixels.foreach_set(np.zeros(res*res*4,dtype=np.float32))
m=bpy.data.materials.new('BAKE'); m.use_nodes=True; nt=m.node_tree; nt.nodes.clear()
tc=nt.nodes.new('ShaderNodeTexCoord'); em=nt.nodes.new('ShaderNodeEmission'); o=nt.nodes.new('ShaderNodeOutputMaterial')
nt.links.new(tc.outputs['Object'],em.inputs['Color']); nt.links.new(em.outputs[0],o.inputs[0])
ti=nt.nodes.new('ShaderNodeTexImage'); ti.image=img; nt.nodes.active=ti
root=[ob for ob in sc.objects if ob.type=='MESH' and ob.parent is None or (ob.parent and ob.parent.type=='ARMATURE' and ob.type=='MESH')][0]
tc.object=root
targets=[ob for ob in sc.objects if ob.type=='MESH' and len(ob.data.uv_layers)>0 and any(s.material and s.material.name.endswith(('_Mat','pbr_material')) for s in ob.material_slots)]
print('targets',[t.name for t in targets],'root',root.name)
bpy.ops.object.select_all(action='DESELECT')
for t in targets:
    if t.parent and t.parent.type=='ARMATURE':
        for mo in t.modifiers: mo.show_render=False
    t.data.materials.clear(); t.data.materials.append(m); t.select_set(True)
bpy.context.view_layer.objects.active=targets[0]
sc.render.bake.margin=0
bpy.ops.object.bake(type='EMIT')
a=np.array(img.pixels[:],dtype=np.float32).reshape(res,res,4)
np.save(out,a); print('cover',(a[...,3]>0.5).mean())
