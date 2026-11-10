# Rebuild the pilot's paws: the fused AI lumps are cut off at the wrist and replaced by a clean cartoon paw
# (thumb + three fingers) with finger bones, skinned and painted into a strip of the atlas.
# Run: python -I pilot_paws.py -- <in.blend> <basecolor.png> <metalsmooth.png> <out.blend> <out_dir>
import bpy, sys, math, bmesh, numpy as np
from mathutils import Vector, Matrix
from skimage import measure
from PIL import Image
Image.MAX_IMAGE_PIXELS = None

a = sys.argv[sys.argv.index('--') + 1:]
SRC, BASE, MS, DST, OUTDIR = a
bpy.ops.wm.open_mainfile(filepath=SRC)
rig = bpy.data.objects['PilotRig']; body = bpy.data.objects['Pilot_Body']
rig.data.pose_position = 'REST'
me = body.data
KEEP = 0.9375          # the old atlas shrinks into this corner; the paws get the strip beside it
CUT = 0.012            # the old paw is cut here, where it comes out of the cuff

# ---------------------------------------------------------------- the paw as a distance field
# local frame: s out along the arm from the wrist, t toward the thumb (the pilot's front), u up
FINGERS = [  # name, base (s,t,u), direction, length, radius
    ('Index',  (0.098,  0.045, -0.007), (1.0,  0.12, -0.03), 0.049, 0.0262),
    ('Middle', (0.102,  0.000, -0.005), (1.0,  0.00, -0.03), 0.054, 0.0268),
    ('Ring',   (0.098, -0.045, -0.007), (1.0, -0.12, -0.03), 0.047, 0.0255),
]
THUMB = ('Thumb', (0.042, 0.046, -0.012), (0.50, 0.85, -0.15), 0.052, 0.0275)
PALM_C, PALM_H, PALM_R = np.array((0.035, 0.0, -0.003)), np.array((0.085, 0.066, 0.041)), 0.034
KNUCKLE = 0.012        # the first finger joint sits this far along the capsule (the start is buried in the palm)
SPLIT = (0.42, 0.72)   # where the second and third joints fall along the rest of the finger

def unit(v): v = np.array(v, float); return v / np.linalg.norm(v)
def capsule(p, a_, b_, r):
    pa = p - a_; ba = b_ - a_
    h = np.clip((pa @ ba) / (ba @ ba), 0, 1)
    return np.linalg.norm(pa - h[..., None] * ba, axis=-1) - r
def rbox(p, c, h, r):
    q = np.abs(p - c) - (h - r)
    return np.linalg.norm(np.maximum(q, 0), axis=-1) + np.minimum(q.max(-1), 0) - r
def smin(x, y, k):
    h = np.clip(0.5 + 0.5 * (y - x) / k, 0, 1)
    return y * (1 - h) + x * h - k * h * (1 - h)

def palm_sdf(p):
    # one chunky block from inside the cuff to the knuckles, thinning a little toward the fingers
    q = p.copy(); thin = 1 - 0.22 * np.clip(q[..., 0] / 0.12, 0, 1)
    q[..., 2] = PALM_C[2] + (q[..., 2] - PALM_C[2]) / thin
    return rbox(q, PALM_C, PALM_H, PALM_R) * 0.85
def field(p, wrist_r):
    core = palm_sdf(p)
    d = core
    for name, base, dr, ln, r in FINGERS + [THUMB]:
        b = np.array(base); e = b + unit(dr) * ln
        d = np.minimum(d, smin(core, capsule(p, b, e, r), 0.012 if name != 'Thumb' else 0.018))
    return d

def paw_mesh(wrist_r):
    h = 0.0011
    lo = np.array((-0.060, -0.085, -0.06)); hi = np.array((0.195, 0.130, 0.05))
    n = np.ceil((hi - lo) / h).astype(int)
    g = np.stack(np.meshgrid(*[lo[i] + np.arange(n[i]) * h for i in range(3)], indexing='ij'), -1)
    vol = field(g, wrist_r)
    v, f, _, _ = measure.marching_cubes(vol, 0.0, spacing=(h, h, h))
    return v + lo, f

# ---------------------------------------------------------------- measure and cut the old hands
gi = {g.name: g.index for g in body.vertex_groups}
co = np.array([v.co[:] for v in me.vertices])
bones = rig.data.bones
uvl = me.uv_layers.active
base_im = Image.open(BASE).convert('RGB'); bw_, bh_ = base_im.size
base_px = np.asarray(base_im)

sides = {}
for side, sx in (('Left', 1), ('Right', -1)):
    W = np.array(bones[side + 'Hand'].head_local[:])
    A = unit(np.array(bones[side + 'Hand'].tail_local[:]) - W)             # out along the hand
    U = np.array((0, 0, 1.0)); U = unit(U - A * (U @ A))
    T = np.cross(U, A) * sx                                                 # toward the thumb: the pilot's front (-Y)
    if T[1] > 0: T = -T
    loc = (co - W) @ np.stack([A, T, U], 1)
    hw = np.array([next((g.weight for g in v.groups if g.group == gi[side + 'Hand']), 0) for v in me.vertices])
    ring = (np.abs(loc[:, 0] - 0.03) < 0.012) & (hw > 0.5)
    r = loc[ring]
    wr = (float(r[:, 2].mean()), float(np.ptp(r[:, 1]) / 2 * 1.04), float(np.ptp(r[:, 2]) / 2 * 1.04), float(r[:, 1].mean()))
    print(side, 'wrist ring verts', ring.sum(), 'u centre %.3f half t %.3f half u %.3f t centre %.3f' % wr)
    kill = (((loc[:, 0] > CUT) | ((hw > 0.5) & (loc[:, 0] > -0.04)) | ((loc[:, 0] > -0.006) & ((loc[:, 1] / 0.064) ** 2 + ((loc[:, 2] + 0.003) / 0.043) ** 2 < 1))) | ((hw > 0.3) & (loc[:, 0] > -0.035) & ((loc[:, 1] / 0.068) ** 2 + ((loc[:, 2] + 0.003) / 0.047) ** 2 < 1))) & (np.linalg.norm(loc[:, 1:], axis=1) < 0.13) & (co[:, 0] * sx > 0.25)
    sides[side] = dict(W=W, A=A, T=T, U=U, wr=wr, kill=kill, sx=sx)

# colours of the old paw, from its paint: the back (orange and stripes) and the pads side (cream)
def sample(mask_fn):
    out = []
    for poly in me.polygons:
        if not all(sides['Left']['kill'][v] for v in poly.vertices): continue
        if not mask_fn(poly.normal): continue
        for li in poly.loop_indices:
            u, v = uvl.data[li].uv
            out.append(base_px[min(bh_ - 1, int((1 - v) * bh_)), min(bw_ - 1, int(u * bw_))])
    return np.array(out, float)
back = sample(lambda n: n.z > 0.6); pads = sample(lambda n: n.z < -0.6)
lum = back @ np.array((0.3, 0.6, 0.1))
ORANGE = np.median(back[lum > np.percentile(lum, 45)], 0); STRIPE = np.median(back[lum < np.percentile(lum, 8)], 0)
CREAM = np.median(pads[(pads @ np.array((0.3, 0.6, 0.1))) > np.percentile(pads @ np.array((0.3, 0.6, 0.1)), 50)], 0)
print('colours orange', ORANGE.round(), 'stripe', STRIPE.round(), 'cream', CREAM.round())

bm = bmesh.new(); bm.from_mesh(me)
killall = sides['Left']['kill'] | sides['Right']['kill']
bmesh.ops.delete(bm, geom=[v for v in bm.verts if killall[v.index]], context='VERTS')
uvb = bm.loops.layers.uv.active
for f in bm.faces:
    for l in f.loops: l[uvb].uv = (l[uvb].uv[0] * KEEP, l[uvb].uv[1] * KEEP)
bm.to_mesh(me); bm.free()
# what is left of the sleeve and cuff rides the forearm alone, so turning the paw cannot tear it
for side in sides:
    gh, ga = body.vertex_groups[side + 'Hand'], body.vertex_groups[side + 'LowerArm']
    for v in me.vertices:
        w = next((g.weight for g in v.groups if g.group == gh.index), 0)
        if w > 0:
            wa = next((g.weight for g in v.groups if g.group == ga.index), 0)
            ga.add([v.index], min(1, wa + w), 'REPLACE'); gh.remove([v.index])
print('old hands cut:', int(killall.sum()), 'verts')

# ---------------------------------------------------------------- finger bones
bpy.context.view_layer.objects.active = rig
for o in bpy.data.objects: o.select_set(o == rig)
bpy.ops.object.mode_set(mode='EDIT')
chains = {}
for side, S in sides.items():
    M = np.stack([S['A'], S['T'], S['U']], 1)
    for name, base, dr, ln, r in FINGERS + [THUMB]:
        b = np.array(base, float); d = unit(dr)
        if name == 'Thumb': joints = [b + d * 0.004, b + d * (0.004 + (ln - 0.004) * 0.40), b + d * (0.004 + (ln - 0.004) * 0.72), b + d * (ln + r * 0.6)]
        else: joints = [b + d * KNUCKLE, b + d * (KNUCKLE + (ln - KNUCKLE) * SPLIT[0]), b + d * (KNUCKLE + (ln - KNUCKLE) * SPLIT[1]), b + d * (ln + r * 0.6)]
        down = np.array((0, 0, -1.0)) * r
        joints = [joints[0] + down * 0.35, joints[1] + down * 0.55, joints[2] + down * 0.55, joints[3] + down * 0.3]
        chains[(side, name)] = joints
        parent = rig.data.edit_bones[side + 'Hand']
        for i, seg in enumerate(('Proximal', 'Intermediate', 'Distal')):
            eb = rig.data.edit_bones.new(f'{side}{name}{seg}')
            eb.head = Vector(S['W'] + M @ joints[i]); eb.tail = Vector(S['W'] + M @ joints[i + 1])
            eb.align_roll(Vector(S['U'])); eb.parent = parent; eb.use_connect = i > 0
            parent = eb
bpy.ops.object.mode_set(mode='OBJECT')

# ---------------------------------------------------------------- the new paws
def ramp(x, c, w): return np.clip((x - (c - w)) / (2 * w), 0, 1)
STRIP_U0 = KEEP + 0.004
def build(side):
    S = sides[side]
    v, f = paw_mesh(S['wr'])
    v[:, 1] += 0.0   # the palm is centred on the bone line
    m = bpy.data.meshes.new(side + 'Paw'); m.from_pydata(v.tolist(), [], f.tolist()); m.update()
    ob = bpy.data.objects.new(side + 'Paw', m); bpy.context.scene.collection.objects.link(ob)
    bpy.context.view_layer.objects.active = ob
    for o in bpy.data.objects: o.select_set(o == ob)
    b2 = bmesh.new(); b2.from_mesh(m); bmesh.ops.remove_doubles(b2, verts=b2.verts, dist=1e-5)
    bmesh.ops.recalc_face_normals(b2, faces=b2.faces); b2.to_mesh(m); b2.free()
    mod = ob.modifiers.new('d', 'DECIMATE'); mod.ratio = 5200 / len(m.polygons)
    bpy.ops.object.modifier_apply(modifier='d')
    sm = ob.modifiers.new('s', 'SMOOTH'); sm.factor = 0.5; sm.iterations = 2
    bpy.ops.object.modifier_apply(modifier='s')
    p = np.array([x.co[:] for x in m.vertices])
    nrm = np.array([x.normal[:] for x in m.vertices])
    # weights
    for g in ['Hand', 'LowerArm'] + [n + s_ for n in ('Index', 'Middle', 'Ring', 'Thumb') for s_ in ('Proximal', 'Intermediate', 'Distal')]:
        if side + g not in ob.vertex_groups: ob.vertex_groups.new(name=side + g)
        W = np.zeros((len(p), 14)); W[:, 0] = 1
    best = np.full(len(p), 1e9); who = np.full(len(p), -1)
    caps = []
    for k, (name, base, dr, ln, r) in enumerate(FINGERS + [THUMB]):
        b = np.array(base, float); d = unit(dr); e = b + d * ln
        dist = capsule(p, b, e, r); caps.append((b, d, ln, r))
        sel = dist < best; best[sel] = dist[sel]; who[sel] = k
    for k, (name, base, dr, ln, r) in enumerate(FINGERS + [THUMB]):
        b, d, ln, r = caps[k]
        sel = (who == k) & (best < 0.004)
        t = (p - b) @ d
        j = chains[(side, name)]; j0 = (j[0] - b) @ d; j1 = (j[1] - b) @ d; j2 = (j[2] - b) @ d
        bwid = 0.012
        w1 = ramp(t, j0, bwid * (1.6 if name == 'Thumb' else 1.0)); w2 = ramp(t, j1, bwid); w3 = ramp(t, j2, bwid)
        col = 2 + k * 3
        W[sel, 0] = (1 - w1)[sel]; W[sel, col] = (w1 * (1 - w2))[sel]; W[sel, col + 1] = (w2 * (1 - w3))[sel]; W[sel, col + 2] = w3[sel]
    arm = ramp(-p[:, 0], 0.012, 0.03) * 0.9
    palmish = W[:, 0] > 0.999
    W[palmish, 1] = arm[palmish]; W[palmish, 0] = 1 - arm[palmish]
    names = [side + 'Hand', side + 'LowerArm'] + [side + n + s_ for n in ('Index', 'Middle', 'Ring', 'Thumb') for s_ in ('Proximal', 'Intermediate', 'Distal')]
    for c, gname in enumerate(names):
        g = ob.vertex_groups[gname]
        for i in np.nonzero(W[:, c] > 1e-3)[0]: g.add([int(i)], float(W[i, c]), 'REPLACE')
    # UVs: the back and the pads side, each laid flat in the strip (both paws share them)
    uv = m.uv_layers.new(name='Attribute')
    sw = 1 - STRIP_U0 - 0.004                      # strip width in UV
    sc_ = sw / 0.225                               # metres (t) to UV
    for poly in m.polygons:
        up = poly.normal.z >= 0
        for li in poly.loop_indices:
            q = p[m.loops[li].vertex_index]
            uu = STRIP_U0 + (q[1] + 0.090) * sc_
            vv = 0.004 + (q[0] + 0.065) * sc_ + (0.0 if up else 0.27 * sc_)
            uv.data[li].uv = (uu, vv)
    for poly in m.polygons: poly.use_smooth = True
    # place it on the arm
    M = np.stack([S['A'], S['T'], S['U']], 1)
    world = S['W'] + p @ M.T
    for i, x in enumerate(m.vertices): x.co = world[i]
    if np.linalg.det(M) < 0:
        m.flip_normals()
    m.update()
    ob.data.materials.append(body.material_slots[0].material)
    return ob, p, nrm

paws = {}
for side in sides:
    ob, p, nrm = build(side); paws[side] = (ob, p)
    if side == 'Left': PAWP, PAWN = p, nrm
    print(side, 'paw', len(ob.data.vertices), 'verts', len(ob.data.polygons), 'faces')

# ---------------------------------------------------------------- paint the strip
def paint(size, P, N):
    """Colour for every texel of the strip, from where it lies on the paw (P, N: the paw's points and normals)."""
    from scipy.spatial import cKDTree
    sw = 1 - STRIP_U0 - 0.004; sc_ = sw / 0.225
    px0 = int(STRIP_U0 * size); n_u = size - px0
    n_v = int((0.004 + 2 * 0.27 * sc_) * size) + 4
    uu = (px0 + np.arange(n_u) + 0.5) / size; vv = (np.arange(n_v) + 0.5) / size
    Ug, Vg = np.meshgrid(uu, vv)
    t = (Ug - STRIP_U0) / sc_ - 0.090
    s_raw = (Vg - 0.004) / sc_
    pads_side = s_raw > 0.27
    s = np.where(pads_side, s_raw - 0.27, s_raw) - 0.065
    # which way the surface under each texel faces (up: the back of the paw), from the nearest point of that half
    face = np.zeros_like(s)
    for half, sel in ((False, N[:, 2] >= -0.05), (True, N[:, 2] <= 0.05)):
        tree = cKDTree(P[sel][:, :2]); m = pads_side == half
        _, idx = tree.query(np.stack([s[m], t[m]], 1)); face[m] = N[sel][idx, 2]
    from scipy.ndimage import gaussian_filter
    for half in (False, True):
        m = pads_side == half; rows = np.nonzero(m.any(1))[0]
        face[rows[0]:rows[-1] + 1] = gaussian_filter(face[rows[0]:rows[-1] + 1], 5)
    backw = np.clip((face + 0.90) / 0.40, 0, 1); backw = backw * backw * (3 - 2 * backw)
    o, c, k = ORANGE / 255, CREAM / 255, STRIPE / 255
    toe = ramp(s + 0.006 * np.cos(t * 70), 0.112, 0.016)
    thumb_toe = ramp(t - 0.25 * (s - 0.05), 0.088, 0.012)
    toe = np.maximum(toe, thumb_toe)
    furc = o * (1 - toe[..., None]) + c * toe[..., None]
    stripe = np.zeros_like(s)
    # wedges that start wide round the side of the paw and come to a point on its back
    for s0, wdt, lean, t_from, t_tip in ((0.030, 0.0085, 0.20, -0.075, 0.012), (0.052, 0.0080, -0.18, 0.075, -0.018),
                                         (0.074, 0.0075, 0.22, -0.075, 0.000), (0.094, 0.0060, -0.15, 0.075, 0.020)):
        q = np.clip((t - t_tip) / (t_from - t_tip), 0, 1)
        wloc = wdt * np.sqrt(q)
        dline = np.abs(s - s0 - lean * (t - t_tip))
        stripe = np.maximum(stripe, np.clip((wloc - dline) / 0.002, 0, 1) * (q > 0))
    stripe = stripe * (1 - toe)
    furc = furc * (1 - stripe[..., None]) + k * stripe[..., None]
    # underneath: cream, with soft pads on the palm and each toe
    PAD = np.array((0.72, 0.47, 0.43))
    def blob(cs, ct, rs, rt): return np.clip((1 - np.sqrt(((s - cs) / rs) ** 2 + ((t - ct) / rt) ** 2)) / 0.16, 0, 1)
    pad = blob(0.062, -0.004, 0.026, 0.036)
    for name, base, dr, ln, r in FINGERS:
        e = np.array(base) + unit(dr) * (ln - 0.002); pad = np.maximum(pad, blob(e[0], e[1], 0.017, 0.015))
    e = np.array(THUMB[1]) + unit(THUMB[2]) * (THUMB[3] - 0.002); pad = np.maximum(pad, blob(e[0], e[1], 0.015, 0.017))
    padc = c[None, None, :] * (1 - pad[..., None]) + PAD * pad[..., None]
    rgb = furc * backw[..., None] + padc * (1 - backw[..., None])
    return px0, n_v, (np.clip(rgb, 0, 1) * 255).astype(np.uint8)

def atlas(src, size, mode, fill):
    im = Image.open(src).convert(mode)
    out = Image.new(mode, (size, size), fill)
    k = int(round(size * KEEP))
    out.paste(im.resize((k, k), Image.LANCZOS), (0, size - k))
    return out
bc = atlas(BASE, 4096, 'RGB', tuple(int(x) for x in CREAM))
px0, n_v, rgb = paint(4096, PAWP, PAWN)
bc.paste(Image.fromarray(rgb[::-1]), (px0, 4096 - n_v))
bc.save(f'{OUTDIR}/Pilot_BaseColor.png')
ms_src = Image.open(MS).convert('RGBA'); mpx = np.asarray(ms_src)
msz = ms_src.size[0]
ms = atlas(MS, msz, 'RGBA', (0, 0, 0, 60))
ms.save(f'{OUTDIR}/Pilot_MetallicSmoothness.png')
print('painted', px0, n_v)

# ---------------------------------------------------------------- join, smooth, hook up the paint
for o in bpy.data.objects: o.select_set(o == body)
bpy.context.view_layer.objects.active = body
for poly in body.data.polygons: poly.use_smooth = True
bpy.ops.object.shade_smooth_by_angle(angle=math.radians(62))
for o in bpy.data.objects: o.select_set(False)
for side in sides: paws[side][0].select_set(True)
body.select_set(True); bpy.context.view_layer.objects.active = body
bpy.ops.object.join()
me = body.data
mat = body.material_slots[0].material
for n in mat.node_tree.nodes:
    if n.type == 'TEX_IMAGE' and n.image and n.outputs['Color'].links and any(l.to_socket.name == 'Base Color' for l in n.outputs['Color'].links):
        img = bpy.data.images.load(f'{OUTDIR}/Pilot_BaseColor.png'); n.image = img; img.pack()
        print('base colour swapped on', n.name)
rig.data.pose_position = 'POSE'
bpy.ops.wm.save_as_mainfile(filepath=DST)
print('saved', DST, 'verts', len(me.vertices), 'faces', len(me.polygons), 'bones', len(rig.data.bones))
