# Face life for a pilot whose eyes are painted on: eyelids that close (two blend shapes, BlinkHalf and Blink, so the
# lid follows the curve of the eye on its way down) and an ear bone each side for flicks and for laying the ears back.
# The lids are clean grids laid on the eye's own surface, folded away along the upper lid line until the shapes
# unfold them; their paint goes in the atlas strip above the paws.
# Run: python -I pilot_face.py -- <in.blend> <basecolor.png in/out> <out.blend> [pilot spec.json]
#   The spec (specs/wolf_pilot.json) names another pilot's rig and body, where his eyes are, and how to find his ears;
#   without one the values are the tiger's.
import bpy, sys, math, bmesh, json, numpy as np
from mathutils import Vector
from mathutils.bvhtree import BVHTree
from PIL import Image
Image.MAX_IMAGE_PIXELS = None

a = sys.argv[sys.argv.index('--') + 1:]
SRC, TEX, DST = a[:3]
SPEC = json.load(open(a[3])) if len(a) > 3 else {}
bpy.ops.wm.open_mainfile(filepath=SRC)
rig = bpy.data.objects[SPEC.get('rig', 'PilotRig')]; body = bpy.data.objects[SPEC.get('body', 'Pilot_Body')]; me = body.data
rig.data.pose_position = 'REST'
bpy.context.view_layer.update()

# the eyes, seen from the front in the rest pose: centre x, centre z, half width, half height
EYES = {k: tuple(v) for k, v in SPEC['eyes'].items()} if 'eyes' in SPEC else {'R': (-0.083, 0.765, 0.050, 0.049), 'L': (0.095, 0.768, 0.051, 0.048)}
NU, NV, OFF = 16, 9, 0.0080
KEEP = 0.9375; SIZE = 4096
PATCH = (int((KEEP + 0.006) * SIZE), 600, 120, 120)     # x, y from the bottom, w, h in pixels of the 4K atlas

bm = bmesh.new(); bm.from_mesh(me); bmesh.ops.triangulate(bm, faces=bm.faces)
bvh = BVHTree.FromBMesh(bm); bm.free()
def surf(x, z):
    hit, n, _, _ = bvh.ray_cast(Vector((x, -1.0, z)), Vector((0, 1, 0)))
    return hit, n

verts, faces, uvs, keys = [], [], [], {'Basis': [], 'BlinkHalf': [], 'Blink': []}
for name, (xc, zc, rx, rz) in EYES.items():
    base = len(verts)
    for j in range(NV + 1):
        for i in range(NU + 1):
            u = i / NU; v = j / NV
            x = xc + rx * (2 * u - 1) * 0.995
            half = rz * math.sqrt(max(0.0, 1 - ((x - xc) / rx) ** 2))
            top, bot = zc + half, zc - half
            def at(vv):
                p, n = surf(x, top - vv * (top - bot))
                return p + n * OFF
            verts.append(at(0.0)); keys['Basis'].append(at(0.0)); keys['BlinkHalf'].append(at(v * 0.5)); keys['Blink'].append(at(v))
            uvs.append(((PATCH[0] + 4 + u * (PATCH[2] - 8)) / SIZE, (PATCH[1] + 4 + (1 - v) * (PATCH[3] - 8)) / SIZE))
    for j in range(NV):
        for i in range(NU):
            p = base + j * (NU + 1) + i
            faces.append((p, p + NU + 1, p + NU + 2, p + 1))

# the eye's surface carries the painted highlight in relief: smooth the lids over it
def blur(arr):
    g = np.array([v[:] for v in arr]).reshape(len(EYES), NV + 1, NU + 1, 3)
    for _ in range(3):
        pad = np.pad(g, ((0, 0), (1, 1), (1, 1), (0, 0)), mode='edge')
        g = (pad[:, :-2, 1:-1] + pad[:, 2:, 1:-1] + pad[:, 1:-1, :-2] + pad[:, 1:-1, 2:] + 2 * pad[:, 1:-1, 1:-1]) / 6
    return [Vector(v) for v in g.reshape(-1, 3)]
for kname in keys: keys[kname] = blur(keys[kname])
verts = list(keys['Basis'])
lm = bpy.data.meshes.new('Lids'); lm.from_pydata([v[:] for v in verts], [], faces); lm.update()
uvl = lm.uv_layers.new(name=me.uv_layers.active.name)
for l in lm.loops: uvl.data[l.index].uv = uvs[l.vertex_index]
for p in lm.polygons: p.use_smooth = True
lid = bpy.data.objects.new('Lids', lm); bpy.context.scene.collection.objects.link(lid)
# make sure they face out (toward -Y)
if sum(p.normal.y for p in lm.polygons if p.area > 0) > 0 or True:
    lid.shape_key_add(name='Basis');
    for kname in ('BlinkHalf', 'Blink'):
        k = lid.shape_key_add(name=kname); k.value = 0.0
        for i, v in enumerate(keys[kname]): k.data[i].co = v
lid.vertex_groups.new(name='Head').add(list(range(len(verts))), 1.0, 'REPLACE')
lid.data.materials.append(body.material_slots[0].material)
# face orientation: check on the closed shape
k = lid.data.shape_keys.key_blocks['Blink']
p0 = lm.polygons[len(lm.polygons) // 4]
c = [k.data[i].co for i in p0.vertices]; nrm = (c[1] - c[0]).cross(c[2] - c[0])
if nrm.y > 0: lm.flip_normals()

# join into the body: the body gets the same shape keys (it does not move in them)
n_body = len(me.vertices)
body.shape_key_add(name='Basis'); body.shape_key_add(name='BlinkHalf'); body.shape_key_add(name='Blink')
for kb in body.data.shape_keys.key_blocks[1:]: kb.value = 0.0   # open at rest
for o in bpy.data.objects: o.select_set(o in (lid, body))
bpy.context.view_layer.objects.active = body
bpy.ops.object.join()
me = body.data
print('lids joined: verts', n_body, '->', len(me.vertices), 'keys', [k.name for k in me.shape_keys.key_blocks])

# ---------------------------------------------------------------- ears
co = np.array([v.co[:] for v in me.vertices])
gi = {g.name: g.index for g in body.vertex_groups}
hw = np.array([next((g.weight for g in v.groups if g.group == gi['Head']), 0) for v in me.vertices])
C = np.array((0.0, 0.83))
d = np.hypot(co[:, 0] - C[0], co[:, 2] - C[1])
def sstep(x, a_, b_): t = np.clip((x - a_) / (b_ - a_), 0, 1); return t * t * (3 - 2 * t)
EAR = SPEC.get('ears')
if EAR is False:   # no ears to move (a fish's face, a skull, a ghost, a wizard under his hat)
    earw = None
elif EAR is None:   # the tiger's round ears: a band of the head's outline
    earw = sstep(d, 0.180, 0.232) * (co[:, 2] > 0.855) * (np.abs(co[:, 0]) > 0.10) * (co[:, 1] > -0.05) * (hw > 0.5)
else:
    # ears standing out of a helmet or a hat: picked by where they are (z_ramp up from the brim, x_ramp out from the
    # side of the head, an optional z band) and by their paint (unsaturated fur below max_sat, or warm fur: red over
    # blue by at least warm, and light enough: min_lum), weighted up from the root so the root stays put
    tx = np.asarray(Image.open(TEX).convert('RGB')).astype(float); th_, tw_ = tx.shape[:2]
    col = np.zeros((len(co), 3)); cnt = np.zeros(len(co)); uvd = me.uv_layers.active.data
    for l in me.loops:
        u_, v_ = uvd[l.index].uv; col[l.vertex_index] += tx[min(th_ - 1, int((1 - v_) * th_)), min(tw_ - 1, int(u_ * tw_))]; cnt[l.vertex_index] += 1
    col /= np.maximum(cnt, 1)[:, None]
    fur = np.ones(len(co))
    if 'max_sat' in EAR: fur *= (col.max(1) - col.min(1)) < EAR['max_sat']
    if 'warm' in EAR: fur *= (col[:, 0] - col[:, 2]) > EAR['warm']
    if 'min_lum' in EAR: fur *= (col @ np.array((0.3, 0.6, 0.1))) > EAR['min_lum']
    # spread the fur mask over the mesh a little, so the paint's speckle does not tear the ear
    edges = np.array([e.vertices[:] for e in me.edges])
    for _ in range(3):
        acc = np.zeros(len(co)); n_ = np.zeros(len(co))
        np.add.at(acc, edges[:, 0], fur[edges[:, 1]]); np.add.at(acc, edges[:, 1], fur[edges[:, 0]])
        np.add.at(n_, edges[:, 0], 1); np.add.at(n_, edges[:, 1], 1)
        fur = 0.5 * fur + 0.5 * acc / np.maximum(n_, 1)
    earw = sstep(fur, 0.35, 0.75) * (np.abs(co[:, 0]) > EAR.get('min_x', 0.0)) * (hw > 0.5)
    if 'z_ramp' in EAR: earw = earw * sstep(co[:, 2], *EAR['z_ramp'])
    if 'x_ramp' in EAR: earw = earw * sstep(np.abs(co[:, 0]), *EAR['x_ramp'])
    if 'z' in EAR: earw = earw * (co[:, 2] > EAR['z'][0]) * (co[:, 2] < EAR['z'][1])
    if 'max_y' in EAR: earw = earw * (co[:, 1] < EAR['max_y'])
if earw is not None:
    bpy.context.view_layer.objects.active = rig
    for o in bpy.data.objects: o.select_set(o == rig)
    bpy.ops.object.mode_set(mode='EDIT')
    for side, sx in (('Left', 1), ('Right', -1)):
        sel = (earw > 0.5) & (co[:, 0] * sx > 0)
        pts = co[sel]
        root = np.array((sx * (EAR['root'][0] if EAR else 0.150), float(pts[:, 1].mean()), EAR['root'][1] if EAR else 0.875))
        tipi = np.argmax(d * sel); tip = np.array((co[tipi, 0], float(pts[:, 1].mean()), co[tipi, 2]))
        eb = rig.data.edit_bones.new(side + 'Ear'); eb.head = Vector(root); eb.tail = Vector(root + (tip - root) * 0.9)
        eb.align_roll(Vector((0, 1, 0))); eb.parent = rig.data.edit_bones['Head']
        print(side, 'ear verts', int(sel.sum()), 'root', root.round(3), 'tip', tip.round(3))
    bpy.ops.object.mode_set(mode='OBJECT')
    hg = body.vertex_groups['Head']
    for side, sx in (('Left', 1), ('Right', -1)):
        g = body.vertex_groups.new(name=side + 'Ear')
        for i in np.nonzero((earw > 0.01) & (co[:, 0] * sx > 0))[0]:
            w = float(earw[i]); g.add([int(i)], w, 'REPLACE'); hg.add([int(i)], max(0.0, float(hw[i]) - w), 'REPLACE')

# ---------------------------------------------------------------- lid paint
im = Image.open(TEX).convert('RGB'); px = np.asarray(im).copy()
x0, yb, w, h = PATCH
# fur colour: sampled just above each eye would be brow white; take the cheek orange from the paw strip's wrist rows
fur = np.array(SPEC['lid'], float) if 'lid' in SPEC else px[SIZE - 40, int((KEEP + 0.03) * SIZE)].astype(float)
dark = np.array(SPEC.get('lash', (52, 30, 26)), float)
vv = np.linspace(0, 1, h)[:, None]            # 0 at the top of the lid, 1 at its lower edge
uu = np.linspace(0, 1, w)[None, :]
shade = 1.0 - 0.10 * vv                        # a touch darker toward the lash
col = fur[None, None, :] * shade[..., None] * np.ones((h, w, 1))
lash = np.clip((vv - (0.80 - 0.05 * np.sin(np.pi * uu))) / 0.04, 0, 1)
col = col * (1 - lash[..., None]) + dark * lash[..., None]
crease = np.clip(1 - np.abs(vv - 0.42 - 0.06 * np.sin(np.pi * uu)) / 0.02, 0, 1) * 0.18   # a soft fold line
col = col * (1 - crease[..., None]) + dark * crease[..., None]
px[SIZE - yb - h:SIZE - yb, x0:x0 + w] = np.clip(col, 0, 255).astype(np.uint8)
Image.fromarray(px).save(TEX)
mat = body.material_slots[0].material
for n in mat.node_tree.nodes:
    if n.type == 'TEX_IMAGE' and n.image and any(l.to_socket.name == 'Base Color' for l in n.outputs['Color'].links):
        img = bpy.data.images.load(TEX); n.image = img; img.pack()
rig.data.pose_position = 'POSE'
bpy.ops.wm.save_as_mainfile(filepath=DST)
print('saved', DST, 'fur', fur)
