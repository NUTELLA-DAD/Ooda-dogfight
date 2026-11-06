# Replace a fighter's lumpy AI-generated propeller with a clean one: a smooth tangent-ogive spinner and paddle blades with
# airfoil thickness, twist and rounded tips. UVs point into a flat-colour palette that palette.py paints into an unused
# corner of the aircraft's base-colour atlas, so the plane keeps its single material.
# Run: python -I prop_rebuild.py -- <in.blend> <out.blend> <spec.json>
# spec: prop object, blade count, tip radius, spinner base (local y, +y is aft), spinner radius and length, blade plane
# (local y), blade start angle in degrees, pitch distance, palette rectangle in atlas pixels, atlas size.
import bpy, sys, json, math
import numpy as np
a = sys.argv[sys.argv.index('--') + 1:]
bpy.ops.wm.open_mainfile(filepath=a[0])
S = json.load(open(a[2]))
ob = bpy.data.objects[S['object']]
X0, Y0, W, H = S['palette']; R = S['atlas']
def uv(px, py): return (px / R, 1 - py / R)
# palette rows (see palette.py): 0 spinner colour, 1 blade strip (x = radius fraction), 2 ink, 3 metal
row = lambda i: Y0 + H * (i + 0.5) / 4
U_SPIN = uv(X0 + W / 2, row(0)); U_INK = uv(X0 + W / 2, row(2))
def u_blade(f): return uv(X0 + 6 + f * (W - 12), row(1))

V, F, UV = [], [], []
def add_quads(grid, uvgrid, close_u=True):
    n, m = len(grid), len(grid[0]); base = len(V)
    for r in grid: V.extend(r)
    for i in range(n - 1):
        for j in range(m if close_u else m - 1):
            jj = (j + 1) % m
            F.append((base + i * m + j, base + i * m + jj, base + (i + 1) * m + jj, base + (i + 1) * m + j))
            UV.append((uvgrid[i][j], uvgrid[i][jj], uvgrid[i + 1][jj], uvgrid[i + 1][j]))

# ---- spinner: tangent ogive from the tip (x=0) to the base (x=L), y local = base - L + x
Rs, L, yb, seg = S['spinner_r'], S['spinner_len'], S['spinner_base'], 64
rho = (Rs * Rs + L * L) / (2 * Rs)
xs = list(np.concatenate([L * (1 - np.cos(np.linspace(0, math.pi / 2, 22))) ** 1.0]))  # dense near the tip
xs = sorted(set([round(x, 5) for x in xs] + [L - 0.03, L - 0.012]))
def rad(x):
    r = math.sqrt(max(rho * rho - (L - x) ** 2, 0)) + Rs - rho
    cap = 0.045  # a small round cap on the point instead of a needle
    return max(r, math.sqrt(max(2 * cap * x - x * x, 0))) if x < cap else r
grid, ug = [], []
tipv = len(V); V.append((0, yb - L, 0))
for x in xs[1:]:
    r = rad(x); y = yb - L + x
    grid.append([(r * math.cos(2 * math.pi * k / seg), y, r * math.sin(2 * math.pi * k / seg)) for k in range(seg)])
    ug.append([U_INK if x > L - 0.013 else U_SPIN] * seg)
base0 = len(V); add_quads(grid, ug)
for k in range(seg):  # tip fan
    F.append((tipv, base0 + (k + 1) % seg, base0 + k)); UV.append((U_SPIN, U_SPIN, U_SPIN))
# back cap
last = len(V) - seg; c = len(V); V.append((0, yb, 0))
for k in range(seg):
    F.append((c, last + k, last + (k + 1) % seg)); UV.append((U_INK, U_INK, U_INK))

# ---- blades
Rt, yp, P = S['tip_r'], S['blade_y'], S['pitch']
def chord(f):
    if f < 0.30: return 0.11 + (S['chord'] - 0.11) * max(0, (f - 0.20) / 0.10) ** 1.5 if f > 0.20 else 0.11
    cm = S['chord']; c = cm * (0.92 + 0.08 * math.sin(math.pi * min(1, (f - 0.3) / 0.55)))
    if f > 0.84: c *= math.sqrt(max(1 - ((f - 0.84) / 0.16) ** 2, 0.0)) * 0.94 + 0.06
    return c
def thick(f): return 0.10 if f < 0.2 else max(0.018, 0.10 - 0.16 * (f - 0.2)) if f < 0.5 else max(0.016, 0.052 - 0.04 * (f - 0.5))
fs = list(np.linspace(0.10, 0.84, 16)) + list(0.84 + 0.16 * np.sin(np.linspace(0, math.pi / 2, 9)[1:]) )
M = 20
for b in range(S['blades']):
    th = math.radians(S['blade_angle0'] + 360 * b / S['blades'])
    er = np.array([math.cos(th), 0, math.sin(th)]); ax = np.array([0, -1, 0.]); et = np.cross(ax, er)
    grid, ug = [], []
    for f in fs:
        r = f * Rt; c = chord(f); t = thick(f)
        beta = math.atan(P / (2 * math.pi * max(r, 0.25)))
        if f < 0.2: c = t = 0.11  # round shank
        cd = math.cos(beta) * et + math.sin(beta) * ax; nd = -math.sin(beta) * et + math.cos(beta) * ax
        ring = []
        for k in range(M):
            ang = 2 * math.pi * k / M
            u = 0.5 * c * math.cos(ang); v = 0.5 * t * math.sin(ang)
            if f >= 0.2: v *= (1.0 + 0.6 * math.cos(ang)) / 1.0 * 0.8  # thicker toward the leading edge
            p = r * er + u * cd + v * nd + np.array([0, yp, 0])
            ring.append(tuple(p))
        grid.append(ring); ug.append([u_blade(f)] * M)
    add_quads(grid, ug)
    # tip cap
    last = len(V) - M; c0 = len(V); V.append(tuple(np.mean(np.array(V[last:last + M]), 0)))
    for k in range(M): F.append((c0, last + k, last + (k + 1) % M)); UV.append((u_blade(1),) * 3)

me = bpy.data.meshes.new(ob.name + '_clean')
me.from_pydata(V, [], F); me.update()
uvl = me.uv_layers.new(name='UVMap')
for p, quv in zip(me.polygons, UV):
    for li, t in zip(p.loop_indices, quv): uvl.data[li].uv = t
for p in me.polygons: p.use_smooth = True
# keep sharp the spinner's back rim
mat = ob.data.materials[0]; old = ob.data
me.materials.append(mat); ob.data = me; bpy.data.meshes.remove(old)
# normals: make them point out (blades were built ccw/cw depending on frame)
import bmesh
bm = bmesh.new(); bm.from_mesh(me); bmesh.ops.recalc_face_normals(bm, faces=bm.faces); bm.to_mesh(me); bm.free()
print('prop rebuilt', len(me.vertices), 'verts', len(me.polygons), 'faces')
bpy.ops.wm.save_as_mainfile(filepath=a[1])
