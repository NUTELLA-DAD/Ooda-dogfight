# The Hi3D twin-engine plane as the P-38: one 300k-triangle mesh in, a blend with P38_Body, P38_Canopy, the two
# propellers (P38_PropL, P38_PropR, each with its origin on its own hub so it spins about its own shaft), PilotMount
# and CockpitCamera out, ready for build_cockpit.py P38 and then unity_export.py.
# The model comes nose to -Y, wings along X, in hundredths of a metre: scaled to the real 52 ft (15.85 m) span.
# Run: python -I p38.py -- <prepped.blend (hi3d_prep.py, tris 0)> <out.blend>
import bpy, bmesh, sys, math
import numpy as np
from mathutils import Vector, Matrix

a = sys.argv[sys.argv.index('--') + 1:]
bpy.ops.wm.open_mainfile(filepath=a[0])
m = [o for o in bpy.data.objects if o.type == 'MESH'][0]
span = max(v.co.x for v in m.data.vertices) - min(v.co.x for v in m.data.vertices)
m.data.transform(Matrix.Scale(15.85 / span, 4))
m.name = m.data.name = 'P38_Body'

# the same opening build_cockpit.py cuts (its CFG['P38']): everything of the bubble above the sill goes to the canopy
A, Y0, Y1, ZCUT = 0.72, -4.62, -1.85, 0.42
YC, B = (Y0 + Y1) / 2, (Y1 - Y0) / 2
def in_opening(c): return (abs(c.x) / A) ** 4 + (abs(c.y - YC) / B) ** 4 < 1 and c.z > ZCUT

bm = bmesh.new(); bm.from_mesh(m.data)
bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=1e-5)
uv = bm.loops.layers.uv.active

# the spinners are the red faces ahead of each nacelle: their middle is the shaft
img = [i for i in bpy.data.images if i.size[0] > 0 and i.colorspace_settings.name == 'sRGB'][0]
img = img.copy(); img.scale(2048, 2048)   # a small copy is plenty to tell red from grey
W, H = img.size
px = np.array(img.pixels[:], dtype=np.float32).reshape(H, W, 4)
def colour(f):
    u = sum(l[uv].uv.x for l in f.loops) / len(f.loops); v = sum(l[uv].uv.y for l in f.loops) / len(f.loops)
    return px[min(H - 1, max(0, int(v * H))), min(W - 1, max(0, int(u * W)))]
hubs = {}
for side in (1, -1):
    red = [f.calc_center_median() for f in bm.faces
           if f.calc_center_median().x * side > 1.5 and f.calc_center_median().y < -3.5
           and (lambda c: c[0] > 0.55 and c[1] < 0.35 and c[2] < 0.35)(colour(f))]
    xs = [c.x for c in red]; ys = [c.y for c in red]; zs = [c.z for c in red]
    hubs[side] = Vector(((min(xs) + max(xs)) / 2, (min(ys) + max(ys)) / 2, (min(zs) + max(zs)) / 2))
    print('spinner', side, len(red), tuple(round(c, 3) for c in hubs[side]), 'back at y', round(max(ys), 3))

# cut each propeller off its nacelle on a clean plane just behind the blades and the spinner's back
YP = -4.38
def prop_side(c):
    for side, h in hubs.items():
        if abs(c.x - h.x) < 2.5 and abs(c.x) > 0.95 and math.hypot(c.x - h.x, c.z - h.z) < 2.45: return side
    return 0
geom = [e for e in bm.edges if any(prop_side(v.co) for v in e.verts)] + [v for v in bm.verts if prop_side(v.co)]
bmesh.ops.bisect_plane(bm, geom=geom + list({f for e in geom if isinstance(e, bmesh.types.BMEdge) for f in e.link_faces}),
                       plane_co=(0, YP, 0), plane_no=(0, 1, 0))
bm.faces.index_update()
part = {}
for f in bm.faces:
    c = f.calc_center_median()
    s = prop_side(c) if c.y < YP else 0
    part[f.index] = 'P38_PropR' if s == 1 else 'P38_PropL' if s == -1 else 'P38_Canopy' if in_opening(c) else 'P38_Body'
bm.to_mesh(m.data); bm.free()
for p in m.data.polygons: p.material_index = 0

def split(name):
    bm = bmesh.new(); bm.from_mesh(m.data); bm.faces.ensure_lookup_table()
    keep = [f for f in bm.faces if part[f.index] == name]
    me = bpy.data.meshes.new(name); bm2 = bm.copy()
    bm2.faces.ensure_lookup_table()
    gone = [f for f in bm2.faces if part[f.index] != name]
    bmesh.ops.delete(bm2, geom=gone, context='FACES')
    bm2.to_mesh(me); bm2.free(); bm.free()
    for mt in m.data.materials: me.materials.append(mt)
    ob = bpy.data.objects.new(name, me); bpy.context.scene.collection.objects.link(ob)
    print(name, len(keep), 'faces')
    return ob
parts = {n: split(n) for n in ('P38_PropL', 'P38_PropR', 'P38_Canopy')}
bm = bmesh.new(); bm.from_mesh(m.data); bm.faces.ensure_lookup_table()
bmesh.ops.delete(bm, geom=[f for f in bm.faces if part[f.index] != 'P38_Body'], context='FACES')
bm.to_mesh(m.data); bm.free()

# fewer triangles: the body to about 110k, the propellers and the (hidden) canopy to a few thousand each
def decimate(ob, tris):
    n = sum(len(p.vertices) - 2 for p in ob.data.polygons)
    if n > tris:
        d = ob.modifiers.new('dec', 'DECIMATE'); d.ratio = tris / n
        bpy.context.view_layer.objects.active = ob
        for o in bpy.data.objects: o.select_set(o == ob)
        bpy.ops.object.modifier_apply(modifier='dec')
    print(ob.name, n, '->', sum(len(p.vertices) - 2 for p in ob.data.polygons), 'triangles')
decimate(m, 110000)
for n, ob in parts.items(): decimate(ob, 3000 if 'Canopy' in n else 5000)

# each propeller about its own hub: the shaft through the spinner's middle, the pivot in the blades' plane
for side, n in ((1, 'P38_PropR'), (-1, 'P38_PropL')):
    ob = parts[n]; h = hubs[side]
    ob.data.transform(Matrix.Translation(-h)); ob.location = h
    ob.parent = m
parts['P38_Canopy'].parent = m

# the seat and the eye, where build_cockpit.py's CFG['P38'] puts the pilot
def empty(name, at):
    e = bpy.data.objects.new(name, None); bpy.context.scene.collection.objects.link(e)
    e.parent = m; e.location = at; e.empty_display_size = 0.1
empty('PilotMount', Vector((0, -3.0, 0.06)))
empty('CockpitCamera', Vector((0, -3.12, 0.70)))
for o in list(bpy.data.objects):
    if o.type not in ('MESH', 'EMPTY'): bpy.data.objects.remove(o)
bpy.ops.wm.save_as_mainfile(filepath=a[1])
print('saved', a[1])
