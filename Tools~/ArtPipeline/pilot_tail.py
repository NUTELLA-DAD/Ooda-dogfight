# A tail that moves: a chain of four bones (Tail1..Tail4) laid down the middle of a pilot's tail, parented to the hips,
# and the tail's skin weighted along them, so pilot_anims2.py's slow wave runs down it in every clip. The tail is the
# skin behind the seat of the trousers (spec "tail": y0, where it leaves the body, and the height band it lies in);
# its root blends into the hips over a few centimetres, so the join never tears.
# Run: python -I pilot_tail.py -- <in.blend> <out.blend> <pilot spec.json>
#   spec "tail": {"y0": 0.10, "z": [0.25, 0.60], "half_x": 0.16, "blend": 0.05}
import bpy, sys, json, numpy as np
from mathutils import Vector

a = sys.argv[sys.argv.index('--') + 1:]
SRC, DST = a[:2]
SPEC = json.load(open(a[2]))
T = SPEC['tail']
bpy.ops.wm.open_mainfile(filepath=SRC)
rig = bpy.data.objects[SPEC['rig']]; body = bpy.data.objects[SPEC['body']]; me = body.data
rig.data.pose_position = 'REST'
bpy.context.view_layer.update()
co = np.array([v.co[:] for v in me.vertices])
y0, (z0, z1), hx, bl = T['y0'], T['z'], T.get('half_x', 0.16), T.get('blend', 0.05)
sel = (co[:, 1] > y0) & (co[:, 2] > z0) & (co[:, 2] < z1) & (np.abs(co[:, 0]) < hx)
P = co[sel]
print('tail verts', int(sel.sum()), 'y', P[:, 1].min().round(3), P[:, 1].max().round(3))

# the centre line: the middle of the tail in slices along its length (measured from the body outward)
L = np.linalg.norm(P - np.array((0, y0, P[:, 2].mean())), axis=1)
order = np.argsort(L); n = 12
cuts = np.linspace(0, len(P) - 1, n + 1).astype(int)
line = [P[order[cuts[i]:cuts[i + 1] + 1]].mean(0) for i in range(n)]
line = [np.array((0.0, y0, line[0][2]))] + line + [P[np.argmax(L)]]
line = np.array(line); line[:, 0] = 0.0                       # on the pilot's middle
seg = np.linalg.norm(np.diff(line, axis=0), axis=1); s = np.concatenate([[0], np.cumsum(seg)]); total = s[-1]
def at(t): return np.array([np.interp(t, s, line[:, k]) for k in range(3)])
joints = [at(total * f) for f in (0.0, 0.22, 0.46, 0.72, 1.0)]

bpy.context.view_layer.objects.active = rig
for o in bpy.data.objects: o.select_set(o == rig)
bpy.ops.object.mode_set(mode='EDIT')
parent = rig.data.edit_bones['Hips']
for i in range(4):
    eb = rig.data.edit_bones.new(f'Tail{i + 1}')
    eb.head = Vector(joints[i]); eb.tail = Vector(joints[i + 1]); eb.align_roll(Vector((0, 0, 1)))
    eb.parent = parent; eb.use_connect = i > 0; parent = eb
bpy.ops.object.mode_set(mode='OBJECT')

# weights: each tail vertex finds its place along the centre line, and blends between the two nearest bones
idx = np.nonzero(sel)[0]
dd = np.linalg.norm(P[:, None, :] - line[None, :, :], axis=2)
k = np.argmin(dd, axis=1)
tpos = s[k]
js = np.array([total * f for f in (0.0, 0.22, 0.46, 0.72, 1.0)])
mid = (js[:-1] + js[1:]) / 2
W = np.zeros((len(P), 4))
for i, t in enumerate(tpos):
    if t <= mid[0]: W[i, 0] = 1
    elif t >= mid[-1]: W[i, 3] = 1
    else:
        j = np.searchsorted(mid, t) - 1; f = (t - mid[j]) / (mid[j + 1] - mid[j]); W[i, j] = 1 - f; W[i, j + 1] = f
root = np.clip((co[idx, 1] - y0) / bl, 0, 1); root = root * root * (3 - 2 * root)
W *= root[:, None]
groups = [body.vertex_groups.get(f'Tail{i + 1}') or body.vertex_groups.new(name=f'Tail{i + 1}') for i in range(4)]
for n_, vi in enumerate(idx):
    v = me.vertices[vi]
    old = [(g.group, g.weight) for g in v.groups]
    keep = 1 - root[n_]
    for gidx, w in old: body.vertex_groups[gidx].add([int(vi)], w * keep, 'REPLACE')
    if keep < 1e-4 and not old: pass
    for j in range(4):
        if W[n_, j] > 1e-4: groups[j].add([int(vi)], float(W[n_, j]), 'REPLACE')
    if not old:                                                   # a vertex that had no weight at all rides the hips
        body.vertex_groups['Hips'].add([int(vi)], float(keep), 'REPLACE')
rig.data.pose_position = 'POSE'
bpy.ops.wm.save_as_mainfile(filepath=DST)
print('tail bones at', [tuple(j.round(3)) for j in joints], 'length', round(total, 3))
