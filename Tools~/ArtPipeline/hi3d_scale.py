# Scale a prepped Hi3D character to 1 m tall with its feet at z 0, before rig.py. Run: python -I hi3d_scale.py -- <work.blend>
import bpy, sys
a = sys.argv[sys.argv.index('--')+1:]
bpy.ops.wm.open_mainfile(filepath=a[0]); m=[o for o in bpy.data.objects if o.type=='MESH'][0]
m.scale=(100,100,100); bpy.context.view_layer.objects.active=m; m.select_set(True)
bpy.ops.object.transform_apply(scale=True)
zs=min(v.co.z for v in m.data.vertices)
for v in m.data.vertices: v.co.z -= zs
bpy.ops.wm.save_mainfile()
