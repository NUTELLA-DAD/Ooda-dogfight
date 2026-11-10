# The cockpit had no floor and no walls: looking down into it you saw the back of the hull's skin, which Unity does
# not draw, so you saw straight through the aircraft. This builds a closed tub inside the fuselage: side walls that
# follow the hull's own inner line from the floor up to the sill, a floor, a bulkhead at each end, and frames (ribs)
# along the walls. It faces inward, wears the cockpit atlas's swatches, and is named Pit_Tub under Pit.
# Run: python -I cockpit_tub.py -- <in.blend> <out.blend> [body object] [y0 y1 floor_z]
import bpy, sys, bmesh, math
from mathutils import Vector
from mathutils.bvhtree import BVHTree
a = sys.argv[sys.argv.index('--') + 1:]
bpy.ops.wm.open_mainfile(filepath=a[0])
body = bpy.data.objects[a[2] if len(a) > 2 else 'P51_Body']
Y0, Y1, FLOOR = (float(a[3]), float(a[4]), float(a[5])) if len(a) > 5 else (-1.95, 0.92, -0.16)
INSET, XMAX, NZ, DY = 0.022, 0.64, 9, 0.07
GREEN, GREY, OLIVE, LIGHT = (0.1875, 0.6875), (0.4375, 0.6875), (0.5625, 0.6875), (0.0625, 0.9375)

bm = bmesh.new(); bm.from_mesh(body.data); bm.transform(body.matrix_world); bmesh.ops.triangulate(bm, faces=bm.faces)
bvh = BVHTree.FromBMesh(bm); bm.free()
def wall(y, z, sx):
    h = bvh.ray_cast(Vector((0, y, z)), Vector((sx, 0, 0)))
    return None if h[0] is None else min(h[3], XMAX)
def top(y):
    """How high the hull's side goes at this station: the sill (or the roof, where the fuselage is closed)."""
    z = FLOOR
    while z < 1.4 and wall(y, z + 0.02, 1) is not None and wall(y, z + 0.02, -1) is not None: z += 0.02
    return z
ys = [Y0 + i * (Y1 - Y0) / round((Y1 - Y0) / DY) for i in range(round((Y1 - Y0) / DY) + 1)]
V, F, UV = [], [], []
def quad(i0, i1, i2, i3, uv): F.append((i0, i1, i2, i3)); UV.append(uv)
cols = {1: [], -1: []}
tops = [top(y) for y in ys]
# ease the sill line a little so the wall's top edge is not ragged
tops = [min(tops[max(0, i - 1):i + 2]) for i in range(len(tops))]
for sx in (1, -1):
    for y, zt in zip(ys, tops):
        col = []
        for k in range(NZ + 1):
            z = FLOOR + (zt - FLOOR) * k / NZ
            x = wall(y, min(z, zt - 0.004), sx)
            x = (x if x is not None else 0.3) - INSET
            col.append(len(V)); V.append(Vector((sx * max(0.05, x), y, z)))
        cols[sx].append(col)
    for i in range(len(ys) - 1):
        for k in range(NZ):
            p, q_ = cols[sx][i], cols[sx][i + 1]
            quad(*((p[k], q_[k], q_[k + 1], p[k + 1]) if sx == 1 else (p[k], p[k + 1], q_[k + 1], q_[k])), GREEN)
for i in range(len(ys) - 1):   # floor, facing up
    quad(cols[1][i][0], cols[-1][i][0], cols[-1][i + 1][0], cols[1][i + 1][0], GREY)
for i, flip in ((0, False), (len(ys) - 1, True)):   # bulkheads, facing into the cockpit
    L, R = cols[1][i], cols[-1][i]
    for k in range(NZ):
        quad(*((L[k], L[k + 1], R[k + 1], R[k]) if not flip else (L[k], R[k], R[k + 1], L[k + 1])), OLIVE)
NPLAIN = len(F)
# frames: a raised rib up each wall every fifth station, and a longeron along each wall at two heights
def box(c0, c1, w, d, n, uv):
    """A bar from c0 to c1, w wide across (along `across`), standing d off the wall along n."""
    axis = (c1 - c0).normalized(); across = axis.cross(n).normalized() * (w / 2)
    b = len(V)
    for c in (c0, c1):
        V.extend([c - across, c + across, c + across + n * d, c - across + n * d])
    for s in ((0, 4, 5, 1), (1, 5, 6, 2), (2, 6, 7, 3), (3, 7, 4, 0)):
        F.append(tuple(b + j for j in s)); UV.append(uv)
for sx in (1, -1):
    n = Vector((-sx, 0, 0))
    for i in range(2, len(ys) - 2, 5):
        c = cols[sx][i]
        for k in range(NZ): box(V[c[k]].copy(), V[c[k + 1]].copy(), 0.035, 0.022, n, LIGHT)
    for k in (3, 6):
        for i in range(1, len(ys) - 2): box(V[cols[sx][i][k]].copy(), V[cols[sx][i + 1][k]].copy(), 0.028, 0.016, n, LIGHT)
me = bpy.data.meshes.new('Pit_Tub'); me.from_pydata([v[:] for v in V], [], F); me.update()
uvl = me.uv_layers.new(name='UVMap')
for p in me.polygons:
    for li in p.loop_indices: uvl.data[li].uv = UV[p.index]
b2 = bmesh.new(); b2.from_mesh(me)
b2.faces.ensure_lookup_table()
bmesh.ops.reverse_faces(b2, faces=b2.faces[:NPLAIN])
bmesh.ops.recalc_face_normals(b2, faces=b2.faces[NPLAIN:])
# the bars' normals are fixed up as closed boxes; walls keep the winding given above
b2.to_mesh(me); b2.free(); me.update()
ob = bpy.data.objects.new('Pit_Tub', me); bpy.context.scene.collection.objects.link(ob)
ob.parent = bpy.data.objects.get('Pit'); 
if ob.parent: ob.matrix_parent_inverse = ob.parent.matrix_world.inverted()
me.materials.append(bpy.data.materials['Pit_Mat'])
# check: the walls must face the centreline
bad = sum(1 for p in me.polygons[:2 * (len(ys) - 1) * NZ] if p.normal.x * p.center.x > 0)
print('tub', len(V), 'verts', len(F), 'faces; wall faces pointing out:', bad, 'sill z range', round(min(tops), 2), round(max(tops), 2))
# The cockpit's own open sheets (its deck and liner panels) must face the pilot too: some faced the hull, so the game
# culled them from above. Closed pieces (the seat, the boxes) are left alone.
ps = bpy.data.objects.get('Pit_Static')
if ps:
    b3 = bmesh.new(); b3.from_mesh(ps.data); b3.faces.ensure_lookup_table()
    seen = set(); flipped = 0
    for f0 in b3.faces:
        if f0.index in seen: continue
        isl = [f0]; seen.add(f0.index); i = 0
        while i < len(isl):
            for e in isl[i].edges:
                for g in e.link_faces:
                    if g.index not in seen: seen.add(g.index); isl.append(g)
            i += 1
        if not any(len(e.link_faces) == 1 for f in isl for e in f.edges): continue    # a closed piece
        for f in isl:
            c = ps.matrix_world @ f.calc_center_median(); n = ps.matrix_world.to_3x3() @ f.normal
            if abs(n.z) > 0.7: wrong = n.z < 0                      # decks and the glare shield are seen from above
            else: wrong = n.dot(Vector((0, -0.6, c.z)) - c) < -1e-4   # liners face the pilot
            if wrong: f.normal_flip(); flipped += 1
    b3.to_mesh(ps.data); b3.free(); ps.data.update()
    print('Pit_Static open sheets turned to face the pilot:', flipped)
bpy.ops.wm.save_as_mainfile(filepath=a[1])
