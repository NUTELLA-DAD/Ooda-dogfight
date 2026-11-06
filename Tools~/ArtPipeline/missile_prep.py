# Step 1 of the missile rebuild (see missile.py): open the Hi3D FBX, apply its transform, save its maps as PNGs, save a blend.
# Run: python -I missile_prep.py -- <in.fbx> <raw.blend> <texture folder>
import bpy, sys, os
a = sys.argv[sys.argv.index('--')+1:]
src, dst, texdir = a
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.fbx(filepath=src)
m = [o for o in bpy.data.objects if o.type=='MESH'][0]
bpy.context.view_layer.objects.active = m
for o in bpy.data.objects: o.select_set(o == m)
mw = m.matrix_world.copy(); m.parent = None; m.matrix_world = mw
bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
for o in list(bpy.data.objects):
    if o != m: bpy.data.objects.remove(o)
m.name = 'Missile'
import numpy as np
co = np.zeros(len(m.data.vertices)*3); m.data.vertices.foreach_get('co', co); co = co.reshape(-1,3)
print('min', co.min(0), 'max', co.max(0))
mat = m.data.materials[0]
for n in mat.node_tree.nodes:
    if n.type=='TEX_IMAGE':
        print('TEX', n.image.name, n.image.colorspace_settings.name, [(l.to_node.type, l.to_socket.name) for o in n.outputs for l in o.links])
    else: print('NODE', n.type, [(i.name, i.links[0].from_node.type) for i in n.inputs if i.is_linked])
os.makedirs(texdir, exist_ok=True)
for img in bpy.data.images:
    img.filepath_raw = os.path.join(texdir, img.name.replace('.', '_') + '.png'); img.file_format = 'PNG'; img.save()
    if img.packed_file: img.unpack(method='REMOVE')
bpy.ops.wm.save_as_mainfile(filepath=dst)
