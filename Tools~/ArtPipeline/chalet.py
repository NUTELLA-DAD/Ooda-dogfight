# The chalet as two OBJs (Y up, metres, centred, base at 0) the way AlpineWorld's land models are: near and far.
import bpy, sys
a = sys.argv[sys.argv.index('--')+1:]
bpy.ops.wm.open_mainfile(filepath=a[0]); out = a[1]
m = [o for o in bpy.data.objects if o.type=='MESH'][0]; m.name = 'Chalet'
bpy.context.view_layer.objects.active = m; m.select_set(True)
vs = m.data.vertices
lo = [min(v.co[i] for v in vs) for i in range(3)]; hi = [max(v.co[i] for v in vs) for i in range(3)]
k = 16.0 / max(hi[0]-lo[0], hi[1]-lo[1])     # about 16 m across its longer side
for v in vs:
    v.co.x = (v.co.x - (lo[0]+hi[0])/2) * k; v.co.y = (v.co.y - (lo[1]+hi[1])/2) * k; v.co.z = (v.co.z - lo[2]) * k
print('size m', [(hi[i]-lo[i])*k for i in range(3)])
def export(path):
    bpy.ops.wm.obj_export(filepath=path, export_selected_objects=True, forward_axis='NEGATIVE_Z', up_axis='Y',
                          export_uv=True, export_normals=True, export_materials=False, export_triangulated_mesh=True, apply_modifiers=True)
    print('wrote', path, len(m.data.polygons))
for p in m.data.polygons: p.use_smooth = False
export(out + '/Chalet_Near.obj')
d = m.modifiers.new('dec', 'DECIMATE'); d.ratio = 1500 / len(m.data.polygons); bpy.ops.object.modifier_apply(modifier='dec')
export(out + '/Chalet_Far.obj')
