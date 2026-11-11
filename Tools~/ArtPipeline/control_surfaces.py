# Cut the moving control surfaces out of an aircraft's body mesh: ailerons, elevators and rudders, each its own
# object hinged on its leading edge, so the game can swing them with the stick and pedals.
# Each surface is the skin behind a straight hinge line, between two stations along it. The hinge sits at a fraction
# of the local chord, measured from the mesh at both ends (so it runs parallel to the trailing edge, as real hinge
# lines do). The body is cut along the hinge and the two end stations, the faces behind it are moved to a new object,
# and the open edges on both sides are capped in dark ink (the gap you see into when the surface swings).
# Each new object's origin is the inboard (or lower) end of its hinge line, and a child empty <name>_Hinge marks the
# other end, so the hinge axis survives the FBX export: ControlSurfaces.cs reads it from the two.
# Run: python -I control_surfaces.py -- <in.blend> <out.blend> <spec.json> <BaseColor.png (for the ink colour)>
#   spec: {"body": "P51_Body", "prefix": "P51", "surfaces": [
#     {"name": "Aileron", "kind": "wing", "te_parallel": true, "s": [s_in, s_out], "f": 0.74, "box": {"y": [..], "z": [..]}},   both sides
#     {"name": "Elevator", "kind": "wing", ...},   (an all-moving one: "pivot": {"y": axis y, "fwd": reach ahead of it, "tip": past the tip})
#     {"name": "Rudder", "kind": "fin", "z": [z_low, z_high], "f": 0.55, "x": 0.0, "y": [..]}  (or "x": [-2.9, 2.9])]}
import bpy, bmesh, sys, json, math, numpy as np
from mathutils import Vector

a = sys.argv[sys.argv.index('--') + 1:]
bpy.ops.wm.open_mainfile(filepath=a[0])
spec = json.load(open(a[2]))
body = bpy.data.objects[spec['body']]
assert body.matrix_world.is_identity or max(abs(v) for v in body.matrix_world.to_euler()) < 1e-6, 'body must be unrotated'

# a dark texel of the atlas for the caps: the middle of the biggest patch of near-black paint
import cv2
tex = cv2.imread(a[3]); g = cv2.cvtColor(tex, cv2.COLOR_BGR2GRAY)
dark = (g < 45).astype(np.uint8)
dist = cv2.distanceTransform(dark, cv2.DIST_L2, 5)
if 'keep' in spec:                                          # never the palette corner (its rows are other colours)
    x0, y0, w, h = spec['keep']; dist[y0:y0 + h, x0:x0 + w] = 0
yy, xx = np.unravel_index(np.argmax(dist), dist.shape)
INK_UV = ((xx + 0.5) / tex.shape[1], 1 - (yy + 0.5) / tex.shape[0])
print('cap ink texel', xx, yy, 'radius', float(dist[yy, xx]), tex[yy, xx])

V = np.array([v.co[:] for v in body.data.vertices])

def wing_point(sx, s, f, box, back=None):
    for win in (0.04, 0.08, 0.15, 0.3):
        m = (np.abs(np.abs(V[:, 0]) - s) < win) & (V[:, 0] * sx > 0) & (V[:, 1] >= box['y'][0]) & (V[:, 1] <= box['y'][1]) \
            & (V[:, 2] >= box['z'][0]) & (V[:, 2] <= box['z'][1])
        if m.sum() >= 30: break
    assert m.sum() >= 10, ('no wing verts at', sx, s)
    le, te = np.percentile(V[m, 1], 1), np.percentile(V[m, 1], 99)
    y = te - back if back is not None else le + f * (te - le)
    k = m & (np.abs(V[:, 1] - y) < 0.06)
    if k.sum() < 4: k = m & (np.abs(V[:, 1] - y) < 0.2)
    if k.sum() < 4: k = m & (np.abs(V[:, 1] - y) < 0.5)
    z = (np.percentile(V[k, 2], 2) + np.percentile(V[k, 2], 98)) / 2
    return Vector((sx * s, y, z)), te - le

def fin_point(xc, z, f, yr):
    for win in (0.04, 0.08, 0.15):
        m = (np.abs(V[:, 0] - xc) < 0.15) & (np.abs(V[:, 2] - z) < win) & (V[:, 1] >= yr[0]) & (V[:, 1] <= yr[1])
        if m.sum() >= 30: break
    assert m.sum() >= 10, ('no fin verts at', xc, z)
    le, te = np.percentile(V[m, 1], 1), np.percentile(V[m, 1], 99)
    y = le + f * (te - le)
    k = m & (np.abs(V[:, 1] - y) < 0.06)
    if k.sum() < 4: k = m & (np.abs(V[:, 1] - y) < 0.2)
    if k.sum() < 4: k = m                                    # a coarse mesh: the fin's middle over its whole chord
    x = (np.percentile(V[k, 0], 2) + np.percentile(V[k, 0], 98)) / 2
    return Vector((x, y, z)), te - le

jobs = []
for sf in spec['surfaces']:
    if sf['kind'] == 'wing':
        for sx, tag in ((1, 'L'), (-1, 'R')):                 # +x is the pilot's left
            A, ca = wing_point(sx, sf['s'][0], sf['f'], sf['box'])
            if sf.get('te_parallel'):
                # an aileron's hinge runs parallel to the trailing edge, forward of it by the inboard end's chord fraction
                B, cb = wing_point(sx, sf['s'][1], sf['f'], sf['box'], back=(1 - sf['f']) * ca)
                A, ca = wing_point(sx, sf['s'][0], sf['f'], sf['box'], back=(1 - sf['f']) * ca)
            else:
                B, cb = wing_point(sx, sf['s'][1], sf['f'], sf['box'])
            if sf.get('pivot'):
                # an all-moving tailplane: it turns whole about a spanwise axis at y (square to the fuselage), from
                # the root cut out past the tip, so the part reaches forward of the axis too
                P_ = sf['pivot']; A.y = B.y = P_['y']; B.x = sx * P_.get('tip', sf['s'][1])
                jobs.append((f"{spec['prefix']}_{sf['name']}{tag}", A, B, ca + 0.3, sf.get('band', 0.25), P_['fwd'], 'root'))
            else:
                jobs.append((f"{spec['prefix']}_{sf['name']}{tag}", A, B, ca * (1 - sf['f']) + 0.15, sf.get('band', 0.25), 0.0, 'all'))
    else:
        xs = sf['x'] if isinstance(sf['x'], list) else [sf['x']]
        for xc in xs:
            tag = '' if len(xs) == 1 else ('L' if xc > 0 else 'R')
            # an upright hinge where the spec says, else at the chord fraction measured halfway up (lower down the
            # chord takes in the dorsal fillet)
            M, cm_ = fin_point(xc, (sf['z'][0] + sf['z'][1]) / 2, sf['f'], sf['y'])
            yh = sf.get('y_hinge', M.y)
            A = Vector((M.x, yh, sf['z'][0])); B = Vector((M.x, yh, sf['z'][1])); ca = cb = cm_ * 1.4
            jobs.append((f"{spec['prefix']}_{sf['name']}{tag}", A, B, max(ca, cb) * (1 - sf['f']) + 0.15, sf.get('band', 0.2), 0.0, 'all'))

bm = bmesh.new(); bm.from_mesh(body.data)
uvl = bm.loops.layers.uv.active
coll = body.users_collection[0]

def frame(A, B):
    u = (B - A).normalized(); Y = Vector((0, 1, 0))
    n = (Y - Y.dot(u) * u).normalized()                      # aft, square to the hinge
    m = u.cross(n)
    return u, n, m

def inside(c, A, L, u, n, m, depth, band, slack=0.0, fwd=0.0):
    d = c - A; t = d.dot(u); h = d.dot(n); w = d.dot(m)
    return -slack <= t <= L + slack and -fwd - slack <= h <= depth + slack and abs(w) <= band + slack

def cap(bm_, planes):
    """Close the open edges left on the cut planes with fans of dark-ink triangles (one fan per run of edges)."""
    lay = bm_.loops.layers.uv.active
    total = 0
    for p, nn in planes:
        on = lambda v: abs((v.co - p).dot(nn)) < 2e-4
        edges = [e for e in bm_.edges if e.is_boundary and on(e.verts[0]) and on(e.verts[1])]
        seen = set()
        for e0 in edges:
            if e0 in seen: continue
            # the connected run of such edges
            run, stack = [], [e0]; seen.add(e0)
            while stack:
                e = stack.pop(); run.append(e)
                for v in e.verts:
                    for e2 in v.link_edges:
                        if e2 not in seen and e2.is_boundary and on(e2.verts[0]) and on(e2.verts[1]):
                            seen.add(e2); stack.append(e2)
            if len(run) < 3: continue
            vs = {v for e in run for v in e.verts}
            c = sum((v.co for v in vs), Vector()) / len(vs)
            cv = bm_.verts.new(c)
            for e in run:
                v1, v2 = e.verts
                # wind the new triangle against the face already on the edge, so the normals agree
                lf = e.link_faces[0]
                for l in lf.loops:
                    if l.edge == e: v1, v2 = l.vert, l.link_loop_next.vert; break
                try: f = bm_.faces.new((v2, v1, cv))
                except ValueError: continue
                for l in f.loops: l[lay].uv = INK_UV
                total += 1
    return total

for name, A, B, depth, band, fwd, cuts in jobs:
    u, n, m = frame(A, B); L = (B - A).length
    planes = [(A, n), (A, u), (B, u)] if cuts == 'all' else [(A, u)]
    near = [f for f in bm.faces if inside(f.calc_center_median(), A, L, u, n, m, depth, band, slack=0.25, fwd=fwd)]
    geom = list({v for f in near for v in f.verts}) + list({e for f in near for e in f.edges}) + near
    for p, nn in planes:
        r = bmesh.ops.bisect_plane(bm, geom=geom, dist=1e-5, plane_co=p, plane_no=nn)
        geom = [x for x in r['geom'] if x.is_valid] + [x for x in geom if x.is_valid and x not in r['geom']]
        geom = list(set(x for x in geom if x.is_valid))
    bm.faces.ensure_lookup_table()
    part = [f for f in bm.faces if inside(f.calc_center_median(), A, L, u, n, m, depth, band, slack=1e-4, fwd=fwd)]
    # the new object's mesh: the part's faces, about the hinge's first end
    nb = bmesh.new(); nuv = nb.loops.layers.uv.new('UVMap'); vmap = {}
    for f in part:
        vs = []
        for v in f.verts:
            if v not in vmap: vmap[v] = nb.verts.new(v.co - A)
            vs.append(vmap[v])
        try: nf = nb.faces.new(vs)
        except ValueError: continue
        nf.smooth = f.smooth
        for l, ol in zip(nf.loops, f.loops): l[nuv].uv = ol[uvl].uv
    bmesh.ops.delete(bm, geom=part, context='FACES')
    loose = [v for v in bm.verts if not v.link_faces]
    bmesh.ops.delete(bm, geom=loose, context='VERTS')
    zero = Vector((0, 0, 0))
    nc = cap(nb, [(zero, n), (zero, u), (B - A, u)] if cuts == 'all' else [(zero, u)])
    bc = cap(bm, planes)
    me = bpy.data.meshes.new(name); nb.to_mesh(me); nb.free()
    for mat in body.data.materials: me.materials.append(mat)
    ob = bpy.data.objects.new(name, me); coll.objects.link(ob)
    ob.parent = body; ob.location = A
    hinge = bpy.data.objects.new(name + '_Hinge', None); coll.objects.link(hinge)
    hinge.parent = ob; hinge.location = B - A; hinge.empty_display_size = 0.1
    print(f'{name}: hinge {tuple(round(v, 3) for v in A)} -> {tuple(round(v, 3) for v in B)}, {len(me.polygons)} faces, caps {nc}/{bc}')

bm.to_mesh(body.data); bm.free(); body.data.update()
bpy.ops.wm.save_as_mainfile(filepath=a[1])
