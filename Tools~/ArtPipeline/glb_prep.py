# Hi3D GLB import (the zero, fox, koi and lion): open the GLB (a mesh under a 'world' empty), apply its transform,
# save its embedded maps as PNGs (Image = base colour, Image_001 = glTF metallic-roughness: G rough, B metal),
# decimate to <tris> triangles (0 keeps them all), save a blend.
# With 'feet' last, a character is set on z 0 (it comes centred on the origin, 1 m tall) for rig.py.
# Run: python -I glb_prep.py -- <in.glb> <out.blend> <tris> <texture folder> [feet]
import bpy, sys, os
a = sys.argv[sys.argv.index('--')+1:]
src, dst, tris, texdir = a[0], a[1], int(a[2]), a[3]
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.gltf(filepath=src)
m = [o for o in bpy.data.objects if o.type=='MESH'][0]
bpy.context.view_layer.objects.active = m
for o in bpy.data.objects: o.select_set(o == m)
mw = m.matrix_world.copy(); m.parent = None; m.matrix_world = mw
bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
for o in list(bpy.data.objects):
    if o != m: bpy.data.objects.remove(o)
print('dims', tuple(m.dimensions), 'tris', len(m.data.polygons))
os.makedirs(texdir, exist_ok=True)
names = {'Image_0': 'Image', 'Image_1': 'Image_001'}
for img in bpy.data.images:
    img.name = names.get(img.name, img.name)
    img.filepath_raw = os.path.join(texdir, img.name + '.png'); img.file_format = 'PNG'; img.save()
    if img.packed_file: img.unpack(method='REMOVE')
if tris and len(m.data.polygons) > tris:
    d = m.modifiers.new('dec', 'DECIMATE'); d.ratio = tris / len(m.data.polygons)
    bpy.ops.object.modifier_apply(modifier='dec')
if len(a) > 4 and a[4] == 'feet':
    zs = min(v.co.z for v in m.data.vertices)
    for v in m.data.vertices: v.co.z -= zs
print('after', len(m.data.polygons), 'bbox z', min(v.co.z for v in m.data.vertices), max(v.co.z for v in m.data.vertices))
bpy.ops.wm.save_as_mainfile(filepath=dst)
