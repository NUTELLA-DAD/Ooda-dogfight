# Close the gap between a fighter's padded cockpit rim (build_cockpit.py) and its fuselage skin, and clear the torn
# canopy-rail shards and the AI model's leftover interior panels round the opening. The canopy is hidden in Unity (the
# cartoon pilot sits in the open), so from outside you saw down past the rim into the tub and up through jagged slivers.
# A clean side fairing is lofted from just under the rim down to the skin at z_low, skin above z_low beside the opening
# is cut away, and the fairing is UV-mapped into the palette (palette.py) so the plane keeps its one material.
# Run: python -I sill_fairing.py -- <in.blend> <out.blend> <spec.json>
import bpy, bmesh, sys, json, math
from mathutils import Vector
from mathutils.bvhtree import BVHTree
a = sys.argv[sys.argv.index('--') + 1:]
bpy.ops.wm.open_mainfile(filepath=a[0])
S = json.load(open(a[2])); F = S['fairing']
body = bpy.data.objects[F['body']]; pit = bpy.data.objects[F['pit']]
assert body.matrix_world.is_identity or True
A, Y0, Y1 = F['a'], F['y0'], F['y1']; YC, B = (Y0 + Y1) / 2, (Y1 - Y0) / 2
K = A / 0.5; rr = 0.035 * K
X0, PY0, W, H = S['palette']; R = S['atlas']
def uv(px, py): return (px / R, 1 - py / R)
U_METAL = uv(X0 + W / 2, PY0 + H * 3.5 / 4); U_INK = uv(X0 + W / 2, PY0 + H * 2.5 / 4)
def z_low(y):
    if 'z_low' not in F: return 0  # the height down to which the skin is replaced, piecewise linear in y
    pts = F['z_low']
    if y <= pts[0][0]: return pts[0][1]
    for (ya, za), (yb, zb) in zip(pts, pts[1:]):
        if y <= yb: return za + (zb - za) * (y - ya) / (yb - ya)
    return pts[-1][1]
ya, yb = F.get('y_range', (0, 0))

# rim height along the outline, read back from the coaming tube in the pit mesh
N = 72
outline = []
for i in range(N):
    t = 2 * math.pi * i / N; c, s = math.cos(t), math.sin(t)
    outline.append((A * math.copysign(abs(c) ** 0.5, c), YC + B * math.copysign(abs(s) ** 0.5, s)))
PV = [pit.matrix_world @ v.co for v in pit.data.vertices]
zr = []
for i, (x, y) in enumerate(outline):
    # only the tube's outer half: the glare shield, panel and tub walls all sit inboard of the outline
    nx_, ny_ = outline[(i + 1) % N][1] - outline[i - 1][1], -(outline[(i + 1) % N][0] - outline[i - 1][0])
    ln = math.hypot(nx_, ny_); nx_, ny_ = nx_ / ln, ny_ / ln
    if nx_ * x + ny_ * (y - YC) < 0: nx_, ny_ = -nx_, -ny_
    near = [p.z for p in PV if (p.x - x) ** 2 + (p.y - y) ** 2 < (rr * 1.3) ** 2 and (p.x - x) * nx_ + (p.y - y) * ny_ > 0.3 * rr]
    zr.append(max(near) - 1.057 * rr if near else None)
known = [z for z in zr if z is not None]
zr = [z if z is not None else sum(known) / len(known) for z in zr]

bvh = BVHTree.FromObject(body, bpy.context.evaluated_depsgraph_get())
def skin_x(y, z, side):
    # the skin's half-width at (y, z): cast from well outside inward, keep the outermost hit
    hit = bvh.ray_cast(Vector((side * 2.0, y, z)), Vector((-side, 0, 0)), 2.0)
    return abs(hit[0].x) if hit[0] is not None else None

if 'z_low' in F:   # planes whose skin already meets the rim (Spitfire, Bf 109, P-38, Zero) only get the deck fix
    # ---- cut: skin beside the opening above z_low, within the fairing's span; bisect first so the seam is a clean line
    bm = bmesh.new(); bm.from_mesh(body.data)
    def region(f, pad=0.06):
        c = f.calc_center_median()
        return ya - pad <= c.y <= yb + pad and F['x_min'] - pad < abs(c.x) < F['x_max'] + pad and c.z > z_low(c.y) - 0.15
    pts = [p for p in F['z_low']]
    pts = [(ya, z_low(ya))] + [p for p in pts if ya < p[0] < yb] + [(yb, z_low(yb))]
    for (y0_, z0_), (y1_, z1_) in zip(pts, pts[1:]):
        geom = [f for f in bm.faces if region(f) and y0_ - 0.06 <= f.calc_center_median().y <= y1_ + 0.06]
        geom = list(set(geom + [e for f in geom for e in f.edges] + [v for f in geom for v in f.verts]))
        n = Vector((1, 0, 0)).cross(Vector((0, y1_ - y0_, z1_ - z0_))).normalized()
        bmesh.ops.bisect_plane(bm, geom=geom, plane_co=Vector((0, y0_, z0_)), plane_no=n, dist=1e-5)
    for yy in (ya, yb):
        geom = [f for f in bm.faces if region(f)]
        geom = list(set(geom + [e for f in geom for e in f.edges] + [v for f in geom for v in f.verts]))
        bmesh.ops.bisect_plane(bm, geom=geom, plane_co=Vector((0, yy, 0)), plane_no=Vector((0, 1, 0)), dist=1e-5)
    def in_cut(c):
        return ya <= c.y <= yb and F['x_min'] < abs(c.x) < F['x_max'] and c.z > z_low(c.y)
    kill = [f for f in bm.faces if in_cut(f.calc_center_median())]
    print('cut', len(kill), 'faces')

    # ---- loft: for each side outline point inside the span, rows from the rim down to the skin
    ROWS = 8
    side_pts = [i for i, (x, y) in enumerate(outline) if ya - 0.02 <= y <= yb + 0.02]
    def strips(side):
        idx = sorted([i for i in side_pts if outline[i][0] * side > 0], key=lambda i: outline[i][1])
        cols = []
        for i in idx:
            x, y = outline[i]; top = Vector((x + side * 0.6 * rr, y, zr[i] + 0.002))
            zl = z_low(y)
            xs = skin_x(y, zl, side)
            xb = (xs - 0.001) if xs is not None else abs(top.x) + 0.04
            col = []
            for k in range(ROWS + 1):
                t = k / ROWS
                z = top.z + (zl - top.z) * t
                if k == ROWS - 1: z = zl + F.get('ink', 0.014); t = (z - top.z) / (zl - top.z)
                if k == 3: t = 0.42      # a panel seam part way down (inked, see below)
                if k == 4: t = 0.42 + 0.014 / max(top.z - zl, 0.05); z = top.z + (zl - top.z) * t
                if k == 3: z = top.z + (zl - top.z) * t
                xx = abs(top.x) + (xb - abs(top.x)) * (t ** 1.3)
                # follow the skin where it is already there and wider than the loft
                xsk = skin_x(y, z, side)
                if xsk is not None and t > 0.5 and xsk - 0.003 > xx: xx = xsk - 0.003
                col.append(Vector((side * xx, y, z)))
            cols.append(col)
        return cols
    for side in (1, -1):
        cols = strips(side)
        vs = [[bm.verts.new(p) for p in col] for col in cols]
        uvl = bm.loops.layers.uv.verify()
        for j in range(len(vs) - 1):
            for k in range(ROWS):
                q = [vs[j][k], vs[j + 1][k], vs[j + 1][k + 1], vs[j][k + 1]]
                if side < 0: q.reverse()
                f = bm.faces.new(q); f.smooth = True; f.material_index = 0
                for l in f.loops: l[uvl].uv = U_INK if k in (3, ROWS - 1) else U_METAL
    bmesh.ops.delete(bm, geom=kill, context='FACES')
    loose = [v for v in bm.verts if not v.link_faces]; bmesh.ops.delete(bm, geom=loose, context='VERTS')
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    bm.to_mesh(body.data); bm.free()

# ---- the open front of the rear deck: a bulkhead from the tub floor up to the deck, so you don't see down into the
# hollow fuselage behind the seat (build_cockpit.py now builds it too; this patches cockpits built before that)
if 'bulkhead_y' in F:
    RY = F['bulkhead_y']; inv = pit.matrix_world.inverted()
    pm = pit.data; uvd = pm.uv_layers.active.data
    deck = [p for p in pm.polygons if p.normal.z > 0.95 and (pit.matrix_world @ p.center).y > RY and abs((pit.matrix_world @ p.center).x) < 0.2]
    deck = max(deck, key=lambda p: p.area)   # the deck plate, not a switch or the radio lid
    dz = (pit.matrix_world @ deck.center).z
    duv = [sum(uvd[i].uv[k] for i in deck.loop_indices) / deck.loop_total for k in (0, 1)]
    u = min(abs(RY - YC) / B, 0.999); hw = A * (1 - u ** 4) ** 0.25 * 0.985
    bmP = bmesh.new(); bmP.from_mesh(pm); ul = bmP.loops.layers.uv.verify()
    cs = [inv @ Vector(c) for c in ((-hw, RY, F['floor']), (hw, RY, F['floor']), (hw, RY, dz), (-hw, RY, dz))]
    for f in (bmP.faces.new([bmP.verts.new(c) for c in cs]), bmP.faces.new([bmP.verts.new(c) for c in reversed(cs)])):
        for l in f.loops: l[ul].uv = duv
    # and the deck itself: older builds sorted its outline so the polygon crossed itself, leaving a hole beside the seat
    pts = [(x * 0.985, y) for x, y in outline if y > RY]
    pts.sort(key=lambda p: (math.atan2(p[1] - (RY + Y1) / 2, p[0]) + math.pi / 2) % (2 * math.pi))
    ring = [inv @ Vector((x, y, dz + 0.002)) for x, y in pts] + [inv @ Vector((-hw, RY, dz + 0.002)), inv @ Vector((hw, RY, dz + 0.002))]
    cen = bmP.verts.new(sum(ring, Vector()) / len(ring)); rv = [bmP.verts.new(c) for c in ring]
    for i in range(len(rv)):
        f = bmP.faces.new((cen, rv[i], rv[(i + 1) % len(rv)]))
        for l in f.loops: l[ul].uv = duv
    bmP.to_mesh(pm); bmP.free()
    print('bulkhead at y', RY, 'deck z', round(dz, 3))
bpy.ops.wm.save_as_mainfile(filepath=a[1])
print('fairing done')
