# Build a cartoon cockpit into each fighter and clean the plane mesh around it.
# Run: python -I build_cockpit.py -- <Plane> <in.blend> <out.blend> [fbx_out]
# Gauntlet fixes done here:
#   - cut the ragged, torn cockpit opening left by the canopy split into a clean rounded hole
#   - smooth the dents and ripples out of the flat skin (sharp edges and panel creases are left alone)
#   - merge duplicate vertices, drop loose bits, make the normals consistent
# Then the interior: tub, padded coaming, instrument panel with live needles and a turning horizon,
# glare shield and gunsight, seat and armour headrest, rear deck with radio, rudder pedals,
# throttle quadrant (moving lever), switch box, trim wheel, and the control stick (moving).
import bpy, bmesh, sys, math, json, os
from mathutils import Vector, Matrix
from mathutils.bvhtree import BVHTree

argv = sys.argv[sys.argv.index('--') + 1:]
PLANE, SRC, DST = argv[0], argv[1], argv[2]
FBX = argv[3] if len(argv) > 3 else None
HERE = os.path.dirname(os.path.abspath(__file__))
AT = json.load(open(os.path.join(HERE, 'atlas.json')))
SWATCH, GAUGES = AT['swatch'], AT['gauges']

CFG = {
    # a: half width of the opening, y0/y1: its front and back, zcut: skin above this inside the opening goes,
    # floor: the tub floor; mount/scale/lift: where the cartoon pilot sits (as the Unity builders seat him)
    'P51': dict(body='P51_Body', box='P51_Cockpit', canopy='P51_Canopy', a=0.51, y0=-1.80, y1=0.82, zcut=0.30, floor=0.08,
                mount=(0, -0.55, -0.08), scale=1.55, lift=-0.33, interior='p51_green', seat='seat_p51', grip='pistol'),
    'Spitfire': dict(body='Spit_Body', box='Spit_Cockpit', canopy='Spit_Canopy', a=0.45, y0=-1.72, y1=0.12, zcut=0.22, floor=-0.30,
                     mount=(0, -0.62, 0.072), scale=1.35, lift=-0.57, interior='spit_grey', seat='seat_spit', grip='spade'),
    'Bf109': dict(body='Bf109_Body', box='Bf109_Cockpit', canopy='Bf109_Canopy', a=0.41, y0=-1.20, y1=0.36, zcut=0.32, floor=-0.25,
                  mount=(0, -0.28, 0.052), scale=1.55, lift=-0.55, interior='bf109_grey', seat='seat_bf', grip='pistol'),
}
C = CFG[PLANE]
bpy.ops.wm.open_mainfile(filepath=SRC)
body = bpy.data.objects[C['body']]
A, Y0, Y1 = C['a'], C['y0'], C['y1']
YC, B = (Y0 + Y1) / 2, (Y1 - Y0) / 2
K = A / 0.5          # size factor for the hardware
S = C['scale']
MOUNT = Vector(C['mount']) + Vector((0, 0, C['lift']))
def rig(x, y, z):    # a point in the pilot's own frame (as modelled) to the plane's frame
    return MOUNT + Vector((x, y, z)) * S

def inside(x, y, grow=1.0, p=4.0):
    return (abs(x) / (A * grow)) ** p + (abs(y - YC) / (B * grow)) ** p < 1.0

# ---------------- 1. mesh clean-up ----------------
bm = bmesh.new(); bm.from_mesh(body.data)
n0 = len(bm.verts)
bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=1e-5)
# the torn opening: every face of the skin inside the rounded hole and above the cut height
kill = [f for f in bm.faces if (lambda c: inside(c.x, c.y) and c.z > C['zcut'])(f.calc_center_median())]
bmesh.ops.delete(bm, geom=kill, context='FACES')
# slivers left hanging on the edge of the hole, and any other loose islands smaller than a hand
bm.verts.ensure_lookup_table()
seen, small = set(), []
for f in bm.faces:
    if f.index in seen: continue
    stack, isl = [f], []
    seen.add(f.index)
    while stack:
        g = stack.pop(); isl.append(g)
        for e in g.edges:
            for h in e.link_faces:
                if h.index not in seen: seen.add(h.index); stack.append(h)
    if len(isl) < 40: small += isl
bmesh.ops.delete(bm, geom=list(set(small)), context='FACES')
loose = [v for v in bm.verts if not v.link_faces]
bmesh.ops.delete(bm, geom=loose, context='VERTS')
bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
print(f'{PLANE}: {n0} -> {len(bm.verts)} verts, cut {len(kill)} torn faces, dropped {len(small)} sliver faces')
bm.to_mesh(body.data); bm.free()

# smooth the flat skin only: a vertex is 'flat' when every edge around it bends less than 18 degrees
bm = bmesh.new(); bm.from_mesh(body.data); bm.verts.ensure_lookup_table()
flat = []
for v in bm.verts:
    ok = True
    for e in v.link_edges:
        if len(e.link_faces) != 2 or e.calc_face_angle(0) > math.radians(18): ok = False; break
    if ok: flat.append(v.index)
bm.free()
vg = body.vertex_groups.new(name='flat'); vg.add(flat, 1.0, 'REPLACE')
mod = body.modifiers.new('smooth', 'SMOOTH'); mod.factor = 0.5; mod.iterations = 6; mod.vertex_group = 'flat'
bpy.context.view_layer.objects.active = body
for o in bpy.context.selected_objects: o.select_set(False)
body.select_set(True)
bpy.ops.object.modifier_apply(modifier='smooth')
body.vertex_groups.remove(body.vertex_groups['flat'])
print(f'{PLANE}: smoothed {len(flat)} flat-skin vertices')

# the old placeholder tub goes; the canopy stays in the file (Unity hides it)
if C['box'] in bpy.data.objects: bpy.data.objects.remove(bpy.data.objects[C['box']])

# ---------------- 2. rim heights from the skin ----------------
bvh = BVHTree.FromObject(body, bpy.context.evaluated_depsgraph_get())
def skin_z(x, y, default):
    hit = bvh.ray_cast(Vector((x, y, 4.0)), Vector((0, 0, -1)), 8.0)
    return hit[0].z if hit[0] is not None and hit[0].z > C['zcut'] - 0.2 else default

N = 72
outline = []
for i in range(N):
    t = 2 * math.pi * i / N
    c, s = math.cos(t), math.sin(t)
    x = A * math.copysign(abs(c) ** 0.5, c); y = YC + B * math.copysign(abs(s) ** 0.5, s)
    outline.append((x, y))
# the rim follows the cut edge itself: the height of the nearest open-edge vertex of the skin
bmE = bmesh.new(); bmE.from_mesh(body.data)
edge_pts = [v.co.copy() for e in bmE.edges if e.is_boundary for v in e.verts
            if v.co.z > C['zcut'] - 0.05 and inside(v.co.x, v.co.y, grow=1.25)]
bmE.free()
zr = []
for x, y in outline:
    near = sorted(edge_pts, key=lambda p: (p.x - x) ** 2 + (p.y - y) ** 2)[:4]
    zr.append(sum(p.z for p in near) / len(near) if near else None)
known = [z for z in zr if z is not None]
fallback = sum(known) / len(known) if known else C['zcut'] + 0.4
zr = [z if z is not None else fallback for z in zr]
for _ in range(3):   # a soft rim line, not every bump of the skin
    zr = [(zr[i - 1] + 2 * zr[i] + zr[(i + 1) % N]) / 4 for i in range(N)]
def rim_at(y, side=1):
    best = min(range(N), key=lambda i: abs(outline[i][1] - y) + (0 if outline[i][0] * side > 0 else 9))
    return zr[best]
def half_width_at(y):
    u = min(abs(y - YC) / B, 0.999)
    return A * (1 - u ** 4) ** 0.25

# the pilot sits down in the tub, shoulders just over the sill; the cockpit camera goes to his eyes
cc = bpy.data.objects.get('CockpitCamera')
if cc is not None:
    w = cc.matrix_world.copy(); w.translation.z = rig(0, 0, 0.69).z; cc.matrix_world = w
sill = (rim_at(MOUNT.y, 1) + rim_at(MOUNT.y, -1)) / 2
print(f'{PLANE}: sill at the seat z {sill:.2f}; pilot shoulders z {rig(0, 0, 0.59).z:.2f}, hips z {rig(0, 0, 0.41).z:.2f}, eye z {rig(0, 0, 0.69).z:.2f}, floor {C["floor"]:.2f}')
# ---------------- 3. building helpers ----------------
def new_obj(name, parent=None):
    me = bpy.data.meshes.new(name); ob = bpy.data.objects.new(name, me)
    bpy.context.scene.collection.objects.link(ob)
    if parent is not None: ob.parent = parent
    return ob

class Builder:
    def __init__(self): self.bm = bmesh.new(); self.uv = self.bm.loops.layers.uv.new('UVMap')
    def paint(self, faces, sw):
        u, v = SWATCH[sw]
        for f in faces:
            for l in f.loops: l[self.uv].uv = (u, v)
    def box(self, center, size, sw, rot=None):
        r = bmesh.ops.create_cube(self.bm, size=1.0)
        vs = r['verts']
        m = Matrix.Translation(center) @ (rot or Matrix()) @ Matrix.Diagonal(Vector(size)).to_4x4()
        bmesh.ops.transform(self.bm, matrix=m, verts=vs)
        faces = list({f for v in vs for f in v.link_faces}); self.paint(faces, sw); return faces
    def cyl(self, a, b, r, sw, seg=16, r2=None):
        a, b = Vector(a), Vector(b); d = b - a
        res = bmesh.ops.create_cone(self.bm, cap_ends=True, segments=seg, radius1=r, radius2=r if r2 is None else r2, depth=d.length)
        rot = d.normalized().to_track_quat('Z', 'Y').to_matrix().to_4x4()
        bmesh.ops.transform(self.bm, matrix=Matrix.Translation((a + b) / 2) @ rot, verts=res['verts'])
        faces = list({f for v in res['verts'] for f in v.link_faces}); self.paint(faces, sw); return faces
    def sphere(self, c, r, sw):
        res = bmesh.ops.create_uvsphere(self.bm, u_segments=14, v_segments=8, radius=r)
        bmesh.ops.translate(self.bm, vec=Vector(c), verts=res['verts'])
        faces = list({f for v in res['verts'] for f in v.link_faces}); self.paint(faces, sw); return faces
    def torus(self, c, R, r, sw, axis='Y', seg=20, sides=8):
        verts = []
        for i in range(seg):
            a = 2 * math.pi * i / seg
            ring = []
            for j in range(sides):
                b = 2 * math.pi * j / sides
                p = Vector(((R + r * math.cos(b)) * math.cos(a), r * math.sin(b), (R + r * math.cos(b)) * math.sin(a)))
                if axis == 'Z': p = Vector((p.x, p.z, p.y))
                ring.append(self.bm.verts.new(Vector(c) + p))
            verts.append(ring)
        faces = []
        for i in range(seg):
            for j in range(sides):
                faces.append(self.bm.faces.new((verts[i][j], verts[(i + 1) % seg][j], verts[(i + 1) % seg][(j + 1) % sides], verts[i][(j + 1) % sides])))
        self.paint(faces, sw); return faces
    def finish(self, ob, smooth=False):
        bmesh.ops.recalc_face_normals(self.bm, faces=self.bm.faces)
        for f in self.bm.faces: f.smooth = smooth
        self.bm.to_mesh(ob.data); self.bm.free()

# the cockpit lives under the plane body so it shares the plane's frame in Unity
root = bpy.data.objects.new('Pit', None); bpy.context.scene.collection.objects.link(root); root.parent = body

# ---------------- 4. the static interior ----------------
st = Builder(); bmS = st.bm
FL = C['floor']
# tub walls: from the floor up to just under the rim, facing inward
bot = [bmS.verts.new((x * 0.985, y, FL)) for x, y in outline]
top = [bmS.verts.new((x * 0.985, y, zr[i] - 0.004)) for i, (x, y) in enumerate(outline)]
walls = [bmS.faces.new((bot[i], bot[(i + 1) % N], top[(i + 1) % N], top[i])) for i in range(N)]
st.paint(walls, C['interior'])
fl = bmS.faces.new(list(reversed(bot))); st.paint([fl], C['interior'])
# ribs on the walls: the stringers you see in every period cockpit
for y in [Y0 + (Y1 - Y0) * k for k in (0.22, 0.38, 0.55, 0.72, 0.86)]:
    hw = half_width_at(y) * 0.97
    for side in (1, -1):
        st.box(Vector((side * (hw - 0.012 * K), y, (FL + rim_at(y, side)) / 2 - 0.02)), (0.02 * K, 0.025 * K, rim_at(y, side) - FL - 0.06), C['interior'])
# padded coaming round the rim: a leather roll
rr = 0.035 * K
prof = 8
rings = []
for i, (x, y) in enumerate(outline):
    c = Vector((x, y, zr[i] + rr * 0.35))
    nxt = Vector((outline[(i + 1) % N][0], outline[(i + 1) % N][1], zr[(i + 1) % N]))
    prv = Vector((outline[i - 1][0], outline[i - 1][1], zr[i - 1]))
    tng = (nxt - prv).normalized(); out = Vector((tng.y, -tng.x, 0)).normalized()
    if out.dot(Vector((x, y - YC, 0))) < 0: out = -out
    rings.append([bmS.verts.new(c + (out * math.cos(2 * math.pi * j / prof) + Vector((0, 0, 1)) * math.sin(2 * math.pi * j / prof)) * rr) for j in range(prof)])
rim = [bmS.faces.new((rings[i][j], rings[(i + 1) % N][j], rings[(i + 1) % N][(j + 1) % prof], rings[i][(j + 1) % prof])) for i in range(N) for j in range(prof)]
st.paint(rim, 'leather')

# instrument panel: 2:1 like the art, as wide as the tub allows at that station
PY = MOUNT.y + S * (-0.50)
PY = max(PY, Y0 + 0.18)
pw = half_width_at(PY) * 2 * 0.94
ptop = min(rim_at(PY, 1), rim_at(PY, -1)) + 0.03 * K
ph = pw / 2
pbot = ptop - ph
T = 0.03 * K
pf = st.box(Vector((0, PY, pbot + ph / 2)), (pw, T, ph), 'panel')
for f in pf:   # the face toward the pilot (+Y) carries the art
    if f.normal.y > 0.9:
        for l in f.loops:
            co = l.vert.co
            u = (pw / 2 - co.x) / pw; v = (co.z - pbot) / ph
            l[st.uv].uv = (u, v * 0.5)
# lower panel and the box under it, down to the floor, so there is no hole under the panel
st.box(Vector((0, PY - 0.05 * K, (FL + pbot) / 2)), (pw * 0.9, 0.06 * K, max(pbot - FL, 0.02)), C['interior'])
PFRONT = PY + T / 2   # the panel face
# glare shield: a hood over the panel, forward to the windscreen
hood_len = max(PY - (Y0 + 0.04), 0.1)
hw = pw / 2 * 1.02
seg = 12
hv = []
for k, yy in enumerate((PY + T * 0.9 + 0.03 * K, PY - hood_len)):
    hv.append([bmS.verts.new((hw * math.cos(math.pi * j / seg), yy, ptop + 0.055 * K * math.sin(math.pi * j / seg))) for j in range(seg + 1)])
hood = [bmS.faces.new((hv[0][j], hv[0][j + 1], hv[1][j + 1], hv[1][j])) for j in range(seg)]
st.paint(hood, 'leather')
# gunsight on the hood
gy = PY - 0.06 * K; gz = ptop + 0.055 * K
st.box(Vector((0, gy, gz + 0.045 * K)), (0.07 * K, 0.11 * K, 0.08 * K), 'darkmetal')
st.box(Vector((0, gy - 0.07 * K, gz + 0.06 * K)), (0.05 * K, 0.04 * K, 0.04 * K), 'black')
st.box(Vector((0, gy + 0.03 * K, gz + 0.13 * K)), (0.095 * K, 0.006 * K, 0.075 * K), 'glass', rot=Matrix.Rotation(math.radians(-35), 4, 'X'))
# miniature aircraft in front of the horizon (static): a yellow bar
gu, gv, gr = GAUGES['Horizon']
hx = pw / 2 - gu * pw; hz = pbot + gv * ph; hr = gr * pw
st.box(Vector((hx, PFRONT + 0.012 * K, hz)), (hr * 1.1, 0.004 * K, hr * 0.08), 'yellow')
st.box(Vector((hx, PFRONT + 0.012 * K, hz + hr * 0.07)), (hr * 0.1, 0.004 * K, hr * 0.14), 'yellow')

# seat, armour headrest
SP = rig(0, 0.02, 0.33)
seat_w = 0.42 * S
st.box(Vector((0, SP.y, SP.z - 0.03 * S)), (seat_w, 0.30 * S, 0.04 * S), C['seat'])                     # pan
st.box(Vector((0, rig(0, 0.17, 0).y, rig(0, 0, 0.47).z)), (seat_w, 0.04 * S, 0.34 * S), C['seat'])      # back
for side in (1, -1):
    st.box(Vector((side * seat_w / 2, SP.y + 0.03 * S, SP.z + 0.06 * S)), (0.03 * S, 0.28 * S, 0.16 * S), C['seat'])
st.box(Vector((0, rig(0, 0.20, 0).y, rig(0, 0, 0.70).z)), (0.24 * S, 0.03 * S, 0.20 * S), 'darkmetal')  # armour plate
st.box(Vector((0, rig(0, 0.17, 0).y, rig(0, 0, 0.74).z)), (0.16 * S, 0.05 * S, 0.09 * S), 'leather')    # head pad
# seat frame down to the floor
for side in (1, -1):
    st.box(Vector((side * seat_w * 0.4, SP.y, (FL + SP.z) / 2)), (0.03 * S, 0.03 * S, max(SP.z - FL, 0.02)), 'darkmetal')
# rear deck behind the headrest, with the radio on it
RY = rig(0, 0.23, 0).y
if RY < Y1 - 0.06:
    deck_z = min(rim_at(RY + 0.05, 1), rim_at(RY + 0.05, -1)) - 0.03
    pts = [(x * 0.985, y) for x, y in outline if y > RY]
    pts.sort(key=lambda p: math.atan2(p[1] - (RY + Y1) / 2, p[0]))
    if len(pts) >= 3:
        dv = [bmS.verts.new((x, y, deck_z)) for x, y in pts] + [bmS.verts.new((half_width_at(RY) * -0.985, RY, deck_z)), bmS.verts.new((half_width_at(RY) * 0.985, RY, deck_z))]
        try:
            df = bmS.faces.new(dv); bmesh.ops.triangulate(bmS, faces=[df]); st.paint([f for f in bmS.faces if all(abs(v.co.z - deck_z) < 1e-6 for v in f.verts)], C['interior'])
        except ValueError:
            pass
        ry2 = (RY + Y1) / 2
        if Y1 - RY > 0.25:
            st.box(Vector((0, ry2, deck_z + 0.08 * K)), (0.30 * K, 0.18 * K, 0.16 * K), 'olive')
            for i, sw in enumerate(('red', 'yellow', 'cream')):
                st.cyl((-0.08 * K + i * 0.08 * K, ry2 - 0.09 * K, deck_z + 0.10 * K), (-0.08 * K + i * 0.08 * K, ry2 - 0.11 * K, deck_z + 0.10 * K), 0.018 * K, sw, seg=10)
# rudder pedals at the feet
for side in (1, -1):
    f = rig(side * 0.117, -0.37, 0.24)
    st.box(Vector((f.x, f.y, f.z)), (0.07 * S, 0.02 * S, 0.11 * S), 'metal', rot=Matrix.Rotation(math.radians(-20), 4, 'X'))
    st.box(Vector((f.x, f.y - 0.03 * S, (FL + f.z) / 2)), (0.02 * S, 0.02 * S, max(f.z - FL, 0.02)), 'darkmetal')
bar = rig(0, -0.40, 0.18)
st.cyl((-0.16 * S, bar.y, max(bar.z, FL + 0.02)), (0.16 * S, bar.y, max(bar.z, FL + 0.02)), 0.012 * S, 'darkmetal', seg=8)
# throttle quadrant on the left wall, switch box on the right
TQ = rig(0.25, -0.20, 0.38)
TQ.x = min(TQ.x, half_width_at(TQ.y) * 0.97 - 0.05 * K)
st.box(Vector((TQ.x, TQ.y, TQ.z - 0.03 * K)), (0.08 * K, 0.26 * K, 0.10 * K), 'darkmetal')
st.box(Vector((TQ.x, TQ.y, TQ.z + 0.022 * K)), (0.02 * K, 0.22 * K, 0.005 * K), 'black')   # the slot
st.cyl((TQ.x - 0.045 * K, TQ.y + 0.16 * K, TQ.z - 0.03 * K), (TQ.x - 0.012 * K, TQ.y + 0.16 * K, TQ.z - 0.03 * K), 0.06 * K, 'black', seg=18)  # trim wheel
st.box(Vector((TQ.x, TQ.y, (FL + TQ.z) / 2 - 0.05 * K)), (0.05 * K, 0.05 * K, max(TQ.z - FL - 0.1 * K, 0.02)), 'darkmetal')
SB = rig(-0.25, -0.10, 0.40)
SB.x = max(SB.x, -half_width_at(SB.y) * 0.97 + 0.05 * K)
st.box(Vector((SB.x, SB.y, SB.z - 0.03 * K)), (0.08 * K, 0.30 * K, 0.10 * K), 'darkmetal')
for i, sw in enumerate(('red', 'yellow', 'cream', 'red')):
    st.box(Vector((SB.x, SB.y - 0.10 * K + i * 0.065 * K, SB.z + 0.03 * K)), (0.025 * K, 0.025 * K, 0.025 * K), sw)
st.box(Vector((SB.x, SB.y, (FL + SB.z) / 2 - 0.05 * K)), (0.05 * K, 0.05 * K, max(SB.z - FL - 0.1 * K, 0.02)), 'darkmetal')
static = new_obj('Pit_Static', root)
st.finish(static)

# ---------------- 5. the moving parts ----------------
def part(name, build, origin, smooth=False):
    b = Builder(); build(b)
    bmesh.ops.translate(b.bm, vec=-Vector(origin), verts=b.bm.verts)
    ob = new_obj(name, root); b.finish(ob, smooth); ob.location = origin; return ob
def empty(name, parent, at):
    e = bpy.data.objects.new(name, None); bpy.context.scene.collection.objects.link(e)
    e.parent = parent; e.location = Vector(at) - parent.location; e.empty_display_size = 0.03; return e

# control stick: pivot on the floor, the grip between the knees where the right hand reaches
G = rig(0, -0.31, 0.47)
base = Vector((0, G.y - 0.02, FL + 0.01))
def stick(b):
    b.cyl(base, base + Vector((0, 0, 0.05 * K)), 0.06 * K, 'leather', seg=14, r2=0.03 * K)       # boot
    b.cyl(base, G - Vector((0, 0, 0.06 * S)), 0.014 * S, 'metal', seg=10)
    if C['grip'] == 'spade':
        b.cyl(G - Vector((0, 0, 0.07 * S)), G - Vector((0, 0, 0.02 * S)), 0.018 * S, 'black', seg=10)
        b.torus(G + Vector((0, 0, 0.03 * S)), 0.045 * S, 0.011 * S, 'black')
        b.sphere(G + Vector((0, 0.0, 0.03 * S)), 0.013 * S, 'brass')
    else:
        b.cyl(G - Vector((0, 0, 0.07 * S)), G + Vector((0, 0, 0.04 * S)), 0.022 * S, 'black', seg=12)
        b.sphere(G + Vector((0, 0, 0.045 * S)), 0.023 * S, 'black')
        b.box(G + Vector((0, -0.02 * S, 0.055 * S)), (0.018 * S, 0.012 * S, 0.012 * S), 'red')          # gun button
        b.box(G + Vector((0, -0.028 * S, 0.0)), (0.012 * S, 0.02 * S, 0.03 * S), 'metal')               # trigger
stk = part('Pit_Stick', stick, base)
empty('StickGrip', stk, G)
empty('StickWrist', stk, rig(-0.045, -0.255, 0.455))   # where the right wrist goes (the IK goal), as in the Fly clip

# throttle lever: pivot in the quadrant slot, knob up and forward
TP = Vector((TQ.x, TQ.y + 0.02 * K, TQ.z + 0.02 * K))
TK = TP + Vector((-0.01 * K, -0.05 * K, 0.12 * K))
def throttle(b):
    b.cyl(TP, TK, 0.012 * K, 'metal', seg=8)
    b.sphere(TK, 0.03 * K, 'red' if PLANE != 'Bf109' else 'black')
thr = part('Pit_Throttle', throttle, TP)
empty('ThrottleGrip', thr, TK)
TW = rig(0.215, -0.20, 0.455); TW.x = min(TW.x, TK.x - 0.03 * K)
empty('ThrottleWrist', thr, TW)

# needles: cream arrows lying on the panel face, pointing up at rest; pivot at the gauge centre
def gauge_at(n):
    u, v, r = GAUGES[n]
    return Vector((pw / 2 - u * pw, PFRONT, pbot + v * ph)), r * pw
for n in ('Airspeed', 'Altimeter', 'Climb', 'RPM'):
    c, r = gauge_at(n)
    def needle(b, c=c, r=r):
        v = [b.bm.verts.new(c + Vector(p)) for p in [(-0.07 * r, 0.006 * K, -0.15 * r), (0.07 * r, 0.006 * K, -0.15 * r), (0.025 * r, 0.006 * K, 0.85 * r), (-0.025 * r, 0.006 * K, 0.85 * r)]]
        f = b.bm.faces.new(v); b.paint([f], 'white')
        if f.normal.y < 0: f.normal_flip()
        b.cyl(c + Vector((0, 0.002 * K, 0)), c + Vector((0, 0.01 * K, 0)), 0.09 * r, 'black', seg=10)
    ob = part(f'Pit_Needle_{n}', needle, c)
# horizon: a disc with sky over ground, turning with the bank
c, r = gauge_at('Horizon')
def horizon(b):
    for half, sw in ((0, 'sky'), (1, 'ground')):
        vs = [b.bm.verts.new(c + Vector((0, 0.003 * K, 0)))]
        for j in range(17):
            a = math.pi * j / 16 + math.pi * half
            vs.append(b.bm.verts.new(c + Vector((math.cos(a) * r * 0.97, 0.003 * K, math.sin(a) * r * 0.97))))
        fs = [b.bm.faces.new((vs[0], vs[j], vs[j + 1])) for j in range(1, 17)]
        for f in fs:
            if f.normal.y < 0: f.normal_flip()
        b.paint(fs, sw)
    b.box(c + Vector((0, 0.004 * K, 0)), (r * 1.9, 0.002 * K, r * 0.03), 'white')
horiz = part('Pit_Horizon', horizon, c)

# ---------------- 6. material ----------------
mat = bpy.data.materials.new('Pit_Mat'); mat.use_nodes = True
nt = mat.node_tree; bsdf = nt.nodes['Principled BSDF']
img = bpy.data.images.load(os.path.join(HERE, 'Cockpit_Atlas.png'))
tex = nt.nodes.new('ShaderNodeTexImage'); tex.image = img; tex.interpolation = 'Closest' if False else 'Linear'
nt.links.new(tex.outputs['Color'], bsdf.inputs['Base Color']); bsdf.inputs['Roughness'].default_value = 0.6
for ob in root.children_recursive:
    if ob.type == 'MESH': ob.data.materials.append(mat)

print(f'{PLANE}: panel at y {PY:.2f}, {pw:.2f} x {ph:.2f} m; stick grip {tuple(round(x, 2) for x in G)}; throttle {tuple(round(x, 2) for x in TK)}')
bpy.ops.wm.save_as_mainfile(filepath=DST)
if FBX:
    for o in bpy.data.objects: o.select_set(False)
    bpy.ops.export_scene.fbx(filepath=FBX, object_types={'MESH', 'EMPTY'}, apply_scale_options='FBX_SCALE_UNITS',
                             use_space_transform=True, bake_space_transform=False, path_mode='STRIP', use_mesh_modifiers=True,
                             mesh_smooth_type='FACE', add_leaf_bones=False)
    print('exported', FBX)
