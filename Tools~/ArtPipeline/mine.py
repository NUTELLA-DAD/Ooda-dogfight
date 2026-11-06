# Kenny's Hi3D balloon mine (a red balloon over a spiked sea mine on a twisted line) as an OBJ in Resources, the way the
# pedals come in: Y up, metres, the origin in the mine's middle (the fuse is measured from there), the balloon above it.
# Clean-up: weld the split vertices, drop the specks floating loose, make the normals face out, decimate to <near> triangles
# (a pass stalls near 6k at the UV seams it keeps, so there is no far one: a few mines at 20k cost little; 9k showed the balloon faceted), smooth-shaded with the spikes and collars kept sharp.
# Run: python -I mine.py -- <prepped.blend (hi3d_prep.py, tris 0)> <out folder> [near tris]
import bpy, bmesh, sys
from mathutils import Matrix, Vector
a = sys.argv[sys.argv.index('--') + 1:]
bpy.ops.wm.open_mainfile(filepath=a[0]); out = a[1]
near = int(a[2]) if len(a) > 2 else 20000
HEIGHT = 4.2   # metres from the bottom spike to the top of the balloon

m = [o for o in bpy.data.objects if o.type == 'MESH'][0]; m.name = m.data.name = 'Mine'
bm = bmesh.new(); bm.from_mesh(m.data)
n0 = len(bm.verts)
bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=1e-6)
print('welded', n0 - len(bm.verts), 'vertices')
# loose specks: islands of fewer than 40 faces (Hi3D leaves bits of shell floating about)
seen, drop = set(), []
for f in bm.faces:
    if f.index in seen: continue
    isl, stack = [], [f]; seen.add(f.index)
    while stack:
        g = stack.pop(); isl.append(g)
        for e in g.edges:
            for h in e.link_faces:
                if h.index not in seen: seen.add(h.index); stack.append(h)
    if len(isl) < 40: drop += isl
bmesh.ops.delete(bm, geom=drop, context='FACES')
bmesh.ops.delete(bm, geom=[v for v in bm.verts if not v.link_faces], context='VERTS')
print('dropped', len(drop), 'faces in loose specks')
bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
bm.to_mesh(m.data); bm.free()

# scale and place: the mine body's middle (the lower sphere) on the origin
vs = m.data.vertices
zlo = min(v.co.z for v in vs); zhi = max(v.co.z for v in vs)
k = HEIGHT / (zhi - zlo)
xs = [v.co.x for v in vs]; ys = [v.co.y for v in vs]
cx, cy = (min(xs) + max(xs)) / 2, (min(ys) + max(ys)) / 2
low = [v.co.z for v in vs if v.co.z < zlo + 0.42 * (zhi - zlo)]   # the mine is the lower ~40%
cz = (min(low) + max(low)) / 2
m.data.transform(Matrix.Translation(Vector((-cx, -cy, -cz))))
m.data.transform(Matrix.Scale(k, 4))
print('size m', tuple(round(d * k, 2) for d in (max(xs) - min(xs), max(ys) - min(ys), zhi - zlo)), 'mine centre at', round((cz - zlo) * k, 2), 'm up')

bpy.context.view_layer.objects.active = m; m.select_set(True)
def decimate(t):
    for _ in range(6):   # a pass stalls short of the goal (it keeps the UV seams), so go again
        if len(m.data.polygons) <= t * 1.05: break
        d = m.modifiers.new('dec', 'DECIMATE'); d.ratio = max(0.2, t / len(m.data.polygons)); d.use_collapse_triangulate = True
        bpy.ops.object.modifier_apply(modifier='dec')
def export(path):
    for p in m.data.polygons: p.use_smooth = True
    bpy.ops.object.shade_auto_smooth(angle=0.7)   # the spikes and the collars keep their edges
    bpy.ops.wm.obj_export(filepath=path, export_selected_objects=True, forward_axis='NEGATIVE_Z', up_axis='Y',
                          export_uv=True, export_normals=True, export_materials=False, export_triangulated_mesh=True, apply_modifiers=True)
    print('wrote', path, len(m.data.polygons))
decimate(near); export(out + '/Mine.obj')
bpy.ops.wm.save_as_mainfile(filepath=out + '/Mine_clean.blend')
