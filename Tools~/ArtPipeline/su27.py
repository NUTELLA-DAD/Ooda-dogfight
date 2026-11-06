# Kenny's Hi3D cartoon Flanker as the Su-27: one 300k-triangle mesh in, a blend with Su27_Body, Su27_Canopy, PilotMount,
# CockpitCamera, the two nozzle exits (NozzleL, NozzleR: where the afterburner flames start) and six missile stations
# (MissileMount_L1, _R1 .. _L3, _R3: on the wing's underside where a pylon would hang, inboard first) out, ready for
# build_cockpit.py Su27 and then unity_export.py. Made the way f15.py makes the Eagle.
# The model comes nose to -Y, wings along X, fins up, in thousandths of a unit: scaled to the real 48.2 ft (14.7 m)
# span over the wingtip rails (the cartoon fuselage is short for it, 18.8 m against 21.9 m).
# Its bubble goes whole: the pilot sits under its front half, the spine's hump behind him.
# Run: python -I su27.py -- <prepped.blend (hi3d_prep.py, tris 0)> <out.blend>
import bpy, bmesh, sys
from mathutils import Vector, Matrix
from mathutils.bvhtree import BVHTree

a = sys.argv[sys.argv.index('--') + 1:]
bpy.ops.wm.open_mainfile(filepath=a[0])
m = [o for o in bpy.data.objects if o.type == 'MESH'][0]
span = max(v.co.x for v in m.data.vertices) - min(v.co.x for v in m.data.vertices)
m.data.transform(Matrix.Scale(14.7 / span, 4))
m.name = m.data.name = 'Su27_Body'

def tris(ob): return sum(len(p.vertices) - 2 for p in ob.data.polygons)
def decimate(ob, n):
    t = tris(ob)
    if t > n:
        d = ob.modifiers.new('dec', 'DECIMATE'); d.ratio = n / t
        bpy.context.view_layer.objects.active = ob
        for o in bpy.data.objects: o.select_set(o == ob)
        bpy.ops.object.modifier_apply(modifier='dec')
    print(ob.name, t, '->', tris(ob), 'triangles')

# weld first (Hi3D splits the mesh along its UV seams)
bm = bmesh.new(); bm.from_mesh(m.data)
bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=1e-5)
bm.to_mesh(m.data); bm.free()

# the same opening build_cockpit.py cuts (its CFG['Su27']): the whole bubble, windscreen to the hump, above the sill
A, Y0, Y1, ZCUT = 0.56, -5.75, -2.15, 0.78
YC, B = (Y0 + Y1) / 2, (Y1 - Y0) / 2
def in_opening(c): return (abs(c.x) / A) ** 4 + (abs(c.y - YC) / B) ** 4 < 1 and c.z > ZCUT

me = m.data
part = ['Su27_Canopy' if in_opening(p.center) else 'Su27_Body' for p in me.polygons]

def split(name):
    bm = bmesh.new(); bm.from_mesh(m.data); bm.faces.ensure_lookup_table()
    n = sum(1 for f in bm.faces if part[f.index] == name)
    bmesh.ops.delete(bm, geom=[f for f in bm.faces if part[f.index] != name], context='FACES')
    me = bpy.data.meshes.new(name); bm.to_mesh(me); bm.free()
    for mt in m.data.materials: me.materials.append(mt)
    ob = bpy.data.objects.new(name, me); bpy.context.scene.collection.objects.link(ob)
    print(name, n, 'faces')
    return ob
canopy = split('Su27_Canopy')
bm = bmesh.new(); bm.from_mesh(m.data); bm.faces.ensure_lookup_table()
bmesh.ops.delete(bm, geom=[f for f in bm.faces if part[f.index] != 'Su27_Body'], context='FACES')
bm.to_mesh(m.data); bm.free()

decimate(m, 120000)
decimate(canopy, 4000)
canopy.parent = m

def empty(name, at):
    e = bpy.data.objects.new(name, None); bpy.context.scene.collection.objects.link(e)
    e.parent = m; e.location = at; e.empty_display_size = 0.1
    print(name, tuple(round(c, 2) for c in at))

# the seat and the eye, where build_cockpit.py's CFG['Su27'] puts the pilot: under the front half of the bubble
MOUNT = Vector((0, -4.15, 0.58))
empty('PilotMount', MOUNT)
empty('CockpitCamera', MOUNT + Vector((0, -0.12, 0.67)))

# the nozzles: the back face of each engine can (the dark ring at the tail), x by its middle
bvh = BVHTree.FromObject(m, bpy.context.evaluated_depsgraph_get())
for name, side in (('NozzleL', 1), ('NozzleR', -1)):   # nose to -Y: the pilot's left is +X (Unity's -X)
    vs = [v.co for v in m.data.vertices if v.co.y > 6.8 and 0.45 < v.co.x * side < 1.85 and -2.0 < v.co.z < 0.0]
    yb = max(v.y for v in vs)
    ring = [v for v in vs if v.y > yb - 0.12]
    c = Vector((sum(v.x for v in ring) / len(ring), yb, (min(v.z for v in ring) + max(v.z for v in ring)) / 2))
    empty(name, c)

# the missile stations: on the wing's underside at mid-chord, three a side, inboard to outboard (1 fires first)
def under(x):
    ys = [v.co.y for v in m.data.vertices if abs(v.co.x - x) < 0.06 and v.co.z > -1.3 and -3 < v.co.y < 5.5]
    y = (min(ys) + max(ys)) / 2
    hit = bvh.ray_cast(Vector((x, y, -4.0)), Vector((0, 0, 1)), 6.0)
    return Vector((x, y, hit[0].z if hit[0] is not None else -0.95))
for i, x in ((1, 3.3), (2, 4.6), (3, 5.8)):
    for side, s in (('L', 1), ('R', -1)):
        empty(f'MissileMount_{side}{i}', under(x * s))

for o in list(bpy.data.objects):
    if o.type not in ('MESH', 'EMPTY'): bpy.data.objects.remove(o)
bpy.ops.wm.save_as_mainfile(filepath=a[1])
print('saved', a[1])
