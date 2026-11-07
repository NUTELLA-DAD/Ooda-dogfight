# Hi3D import for the wizard, skeleton, bear, penguin and chalet: open the FBX (scaled in centimetres-of-a-metre, under an empty), apply its
# transform, save its embedded maps as PNGs, decimate to <tris> triangles (0 keeps them all), save a blend.
# Run: python -I prep.py -- <in.fbx> <out.blend> <tris> <texture folder>
import bpy, sys, os
a = sys.argv[sys.argv.index('--')+1:]
src, dst, tris, texdir = a[0], a[1], int(a[2]), a[3]
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.fbx(filepath=src)
m = [o for o in bpy.data.objects if o.type=='MESH'][0]
bpy.context.view_layer.objects.active = m
for o in bpy.data.objects: o.select_set(o == m)
mw = m.matrix_world.copy(); m.parent = None; m.matrix_world = mw
bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
for o in list(bpy.data.objects):
    if o != m: bpy.data.objects.remove(o)
print('dims', tuple(m.dimensions), 'tris', len(m.data.polygons))
mat = m.data.materials[0]
for n in mat.node_tree.nodes:
    if n.type=='TEX_IMAGE':
        links = [(l.to_node.name, l.to_socket.name) for l in n.outputs[0].links] + [(l.to_node.name, l.to_socket.name) for l in n.outputs['Alpha'].links]
        print('TEX', n.image.name, n.image.colorspace_settings.name, links)
    elif n.type not in ('OUTPUT_MATERIAL',):
        print('NODE', n.type, n.name, [(i.name, i.links[0].from_node.name) for i in n.inputs if i.is_linked])
os.makedirs(texdir, exist_ok=True)
for img in bpy.data.images:
    img.filepath_raw = os.path.join(texdir, img.name.replace('.', '_') + '.png'); img.file_format = 'PNG'; img.save()
    img.unpack(method='REMOVE') if img.packed_file else None
if tris and len(m.data.polygons) > tris:
    d = m.modifiers.new('dec', 'DECIMATE'); d.ratio = tris / len(m.data.polygons)
    bpy.ops.object.modifier_apply(modifier='dec')
print('after', len(m.data.polygons), 'bbox z', min(v.co.z for v in m.data.vertices), max(v.co.z for v in m.data.vertices))
bpy.ops.wm.save_as_mainfile(filepath=dst)
