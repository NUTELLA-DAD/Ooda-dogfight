# The Hi3D cartoon Zero as the A6M Zero: one 300k-triangle mesh in, a blend with Zero_Body, Zero_Canopy, Zero_Propeller
# (its origin on the hub, so it spins about its own shaft), PilotMount and CockpitCamera out, ready for
# build_cockpit.py Zero and then unity_export.py.
# The model comes nose to -Y, wings along X, a unit cube: scaled to the real A6M2's 12.0 m span (the cartoon fuselage
# is as long as the span, so it comes out stubby and roomy, which suits the fox and the koi).
# Run: python -I zero.py -- <prepped.blend (glb_prep.py, tris 0)> <out.blend>
import bpy, bmesh, sys, math
from mathutils import Vector, Matrix

a = sys.argv[sys.argv.index('--') + 1:]
bpy.ops.wm.open_mainfile(filepath=a[0])
m = [o for o in bpy.data.objects if o.type == 'MESH'][0]
span = max(v.co.x for v in m.data.vertices) - min(v.co.x for v in m.data.vertices)
m.data.transform(Matrix.Scale(12.0 / span, 4))
m.name = m.data.name = 'Zero_Body'

# the same opening build_cockpit.py cuts (its CFG['Zero']): everything of the canopy above the sill goes
A, Y0, Y1, ZCUT = 0.68, -2.45, 0.75, 0.36
YC, B = (Y0 + Y1) / 2, (Y1 - Y0) / 2
def in_opening(c): return (abs(c.x) / A) ** 4 + (abs(c.y - YC) / B) ** 4 < 1 and c.z > ZCUT

bm = bmesh.new(); bm.from_mesh(m.data)
bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=1e-5)

# the propeller: the blades and spinner ahead of the cowling's lip (y -5.12); the shaft runs through the spinner's middle
YP = -5.12
front = [v.co.copy() for v in bm.verts if v.co.y < YP - 0.3 and abs(v.co.x) < 0.5 and v.co.z < 0.2 and v.co.z > -1.3]
spin = [c for c in front if c.y < -5.55]
hub = Vector((0, YP, (min(c.z for c in spin) + max(c.z for c in spin)) / 2))
print('spinner tip verts', len(spin), 'hub', tuple(round(x, 3) for x in hub))
def is_prop(c): return c.y < YP and math.hypot(c.x, c.z - hub.z) < 2.0
geom = [e for e in bm.edges if any(v.co.y < YP + 0.05 for v in e.verts)]
bmesh.ops.bisect_plane(bm, geom=geom + list({f for e in geom for f in e.link_faces}) + list({v for e in geom for v in e.verts}),
                       plane_co=(0, YP, 0), plane_no=(0, 1, 0))
bm.faces.index_update()
part = {}
for f in bm.faces:
    c = f.calc_center_median()
    part[f.index] = 'Zero_Propeller' if is_prop(c) else 'Zero_Canopy' if in_opening(c) else 'Zero_Body'
bm.to_mesh(m.data); bm.free()

def split(name):
    bm = bmesh.new(); bm.from_mesh(m.data); bm.faces.ensure_lookup_table()
    n = sum(1 for f in bm.faces if part[f.index] == name)
    bmesh.ops.delete(bm, geom=[f for f in bm.faces if part[f.index] != name], context='FACES')
    me = bpy.data.meshes.new(name); bm.to_mesh(me); bm.free()
    for mt in m.data.materials: me.materials.append(mt)
    ob = bpy.data.objects.new(name, me); bpy.context.scene.collection.objects.link(ob)
    print(name, n, 'faces')
    return ob
parts = {n: split(n) for n in ('Zero_Propeller', 'Zero_Canopy')}
bm = bmesh.new(); bm.from_mesh(m.data); bm.faces.ensure_lookup_table()
bmesh.ops.delete(bm, geom=[f for f in bm.faces if part[f.index] != 'Zero_Body'], context='FACES')
bm.to_mesh(m.data); bm.free()

def decimate(ob, tris):
    n = sum(len(p.vertices) - 2 for p in ob.data.polygons)
    if n > tris:
        d = ob.modifiers.new('dec', 'DECIMATE'); d.ratio = tris / n
        bpy.context.view_layer.objects.active = ob
        for o in bpy.data.objects: o.select_set(o == ob)
        bpy.ops.object.modifier_apply(modifier='dec')
    print(ob.name, n, '->', sum(len(p.vertices) - 2 for p in ob.data.polygons), 'triangles')
decimate(m, 90000)
decimate(parts['Zero_Propeller'], 6000)
decimate(parts['Zero_Canopy'], 3000)

ob = parts['Zero_Propeller']
ob.data.transform(Matrix.Translation(-hub)); ob.location = hub; ob.parent = m
parts['Zero_Canopy'].parent = m

# the seat and the eye, where build_cockpit.py's CFG['Zero'] puts the pilot
def empty(name, at):
    e = bpy.data.objects.new(name, None); bpy.context.scene.collection.objects.link(e)
    e.parent = m; e.location = at; e.empty_display_size = 0.1
MOUNT = Vector((0, -0.95, 0.04))
empty('PilotMount', MOUNT)
empty("CockpitCamera", MOUNT + Vector((0, -0.12, 0.67)))
for o in list(bpy.data.objects):
    if o.type not in ('MESH', 'EMPTY'): bpy.data.objects.remove(o)
bpy.ops.wm.save_as_mainfile(filepath=a[1])
print('saved', a[1])
