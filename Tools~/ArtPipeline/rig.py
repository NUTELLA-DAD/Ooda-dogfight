# Put a T-posed Hi3D character on the wolf's pilot rig: the wolf's armature and Seated action are copied in, the
# character is moved so its hip joint sits where the wolf's does, the bones are moved onto the character's joints
# (landmarks below, in the character's own height-1 frame, facing -Y, its left +X), the tail goes, and the mesh is
# bound with automatic weights (the hat and head pinned to Head).
# Run: python -I rig.py -- <work.blend> <WolfPilot.blend> <Name> <basecolor.png> <out.blend>
import bpy, sys, json
from mathutils import Vector
a = sys.argv[sys.argv.index('--')+1:]
WORK, WOLF, NAME, BASE, OUT = a
L = {
 'Wizard': dict(hips=(0,0,0.20), spine=0.275, chest=0.362, neck=(0,0,0.46), head=(0,0,0.49), headTop=(0,0,0.75),
                sh0=(0.05,0,0.40), sh=(0.16,0,0.405), el=(0.27,0,0.405), wr=(0.385,0,0.40), tip=(0.457,0,0.40),
                hip=(0.07,0,0.19), kn=(0.075,-0.01,0.12), an=(0.075,0,0.065), toe=(0.075,-0.06,0.02), toeTip=(0.075,-0.10,0.02),
                headZ=0.555, y=0.0),
 'Skeleton': dict(hips=(0,-0.03,0.33), spine=0.405, chest=0.49, neck=(0,-0.03,0.575), head=(0,-0.02,0.60), headTop=(0,-0.02,0.86),
                sh0=(0.04,-0.03,0.535), sh=(0.12,-0.01,0.535), el=(0.24,-0.005,0.537), wr=(0.355,0,0.537), tip=(0.467,0.02,0.535),
                hip=(0.05,-0.03,0.32), kn=(0.06,-0.03,0.175), an=(0.065,-0.01,0.05), toe=(0.07,-0.07,0.015), toeTip=(0.07,-0.115,0.015),
                headZ=0.585, y=-0.03),
 'Bear': dict(hips=(0,0,0.29), spine=0.36, chest=0.45, neck=(0,0,0.56), head=(0,0,0.60), headTop=(0,0,0.92),
                sh0=(0.05,0.02,0.53), sh=(0.16,0.03,0.52), el=(0.25,0.03,0.515), wr=(0.34,0.03,0.51), tip=(0.48,0.03,0.51),
                hip=(0.075,0,0.27), kn=(0.09,-0.01,0.155), an=(0.10,0.02,0.06), toe=(0.10,-0.07,0.02), toeTip=(0.10,-0.13,0.02),
                headZ=0.625, y=0.0),
 # the penguin is nearly all head and belly: short flippers at chest height, and legs that are only the feet under him
 'Penguin': dict(hips=(0,0.02,0.19), spine=0.28, chest=0.38, neck=(0,0.02,0.52), head=(0,0.02,0.56), headTop=(0,0.02,0.96),
                sh0=(0.05,0.03,0.48), sh=(0.17,0.03,0.48), el=(0.27,0.03,0.475), wr=(0.37,0.03,0.47), tip=(0.45,0.03,0.47),
                hip=(0.09,0.02,0.16), kn=(0.12,0.01,0.10), an=(0.15,0.0,0.04), toe=(0.17,-0.06,0.015), toeTip=(0.18,-0.12,0.01),
                headZ=0.575, y=0.02),
 # the GLB characters (glb_prep.py ... feet) stand with the tail behind them, so their bodies sit forward of y 0
 'Fox': dict(hips=(0,-0.09,0.36), spine=0.42, chest=0.50, neck=(0,-0.10,0.59), head=(0,-0.11,0.625), headTop=(0,-0.11,0.95),
                sh0=(0.05,-0.09,0.56), sh=(0.14,-0.09,0.55), el=(0.26,-0.09,0.55), wr=(0.37,-0.09,0.545), tip=(0.49,-0.09,0.545),
                hip=(0.075,-0.09,0.34), kn=(0.08,-0.09,0.21), an=(0.085,-0.08,0.085), toe=(0.085,-0.16,0.025), toeTip=(0.085,-0.23,0.02),
                headZ=0.615, y=-0.09),
 'Koi': dict(hips=(0,-0.10,0.27), spine=0.33, chest=0.41, neck=(0,-0.10,0.52), head=(0,-0.10,0.55), headTop=(0,-0.10,0.95),
                sh0=(0.05,-0.08,0.48), sh=(0.15,-0.07,0.48), el=(0.24,-0.06,0.48), wr=(0.32,-0.06,0.475), tip=(0.41,-0.06,0.475),
                hip=(0.07,-0.10,0.25), kn=(0.08,-0.10,0.15), an=(0.09,-0.09,0.07), toe=(0.09,-0.16,0.02), toeTip=(0.09,-0.22,0.02),
                headZ=0.545, y=-0.10),
 'Lion': dict(hips=(0,-0.16,0.34), spine=0.41, chest=0.49, neck=(0,-0.15,0.58), head=(0,-0.15,0.62), headTop=(0,-0.15,0.97),
                sh0=(0.05,-0.13,0.54), sh=(0.15,-0.12,0.535), el=(0.26,-0.11,0.53), wr=(0.34,-0.11,0.53), tip=(0.48,-0.11,0.53),
                hip=(0.075,-0.16,0.32), kn=(0.08,-0.16,0.19), an=(0.09,-0.15,0.08), toe=(0.09,-0.22,0.025), toeTip=(0.09,-0.29,0.02),
                headZ=0.595, y=-0.15),
 # the chibi secret agent (the spectre): a big round head on a short body, arms level at shoulder height
 'Spectre': dict(hips=(0,-0.01,0.29), spine=0.36, chest=0.44, neck=(0,-0.01,0.56), head=(0,-0.01,0.60), headTop=(0,-0.01,0.95),
                sh0=(0.04,0.01,0.52), sh=(0.13,0.01,0.51), el=(0.25,0.012,0.51), wr=(0.37,0.015,0.507), tip=(0.49,0.02,0.507),
                hip=(0.075,-0.01,0.27), kn=(0.088,-0.01,0.165), an=(0.09,-0.01,0.075), toe=(0.095,-0.08,0.025), toeTip=(0.095,-0.13,0.02),
                headZ=0.62, y=-0.01),
}[NAME]
bpy.ops.wm.open_mainfile(filepath=WORK)
sc = bpy.context.scene
mesh = [o for o in bpy.data.objects if o.type=='MESH'][0]; mesh.name = NAME + '_Body'
with bpy.data.libraries.load(WOLF) as (src, dst):
    dst.objects = ['WolfRig']; dst.actions = ['Seated']
rig = dst.objects[0]; sc.collection.objects.link(rig); rig.name = NAME + 'Rig'; rig.data.name = NAME + 'Rig'
seated = dst.actions[0]
wolfHips = rig.data.bones['Hips'].head_local.copy()
off = wolfHips - Vector(L['hips'])
for v in mesh.data.vertices: v.co += off
P = lambda p, mx=1: Vector((p[0]*mx, p[1], p[2])) + off
bpy.context.view_layer.objects.active = rig
for o in bpy.data.objects: o.select_set(o == rig)
bpy.ops.object.mode_set(mode='EDIT')
eb = rig.data.edit_bones
for t in [b for b in eb if b.name.startswith('Tail')]: eb.remove(t)
def put(name, h, t):
    b = eb[name]; roll = b.roll; b.use_connect = False; b.head = h; b.tail = t; b.roll = roll
hy = L['hips'][1]
put('Hips', P(L['hips']), P((0, hy, L['spine'])))
put('Spine', P((0, hy, L['spine'])), P((0, hy, L['chest'])))
put('Chest', P((0, hy, L['chest'])), P(L['neck']))
put('Neck', P(L['neck']), P(L['head']))
put('Head', P(L['head']), P(L['headTop']))
for side, mx in (('Left', 1), ('Right', -1)):
    put(side+'Shoulder', P(L['sh0'], mx), P(L['sh'], mx))
    put(side+'UpperArm', P(L['sh'], mx), P(L['el'], mx))
    put(side+'LowerArm', P(L['el'], mx), P(L['wr'], mx))
    put(side+'Hand', P(L['wr'], mx), P(L['tip'], mx))
    put(side+'UpperLeg', P(L['hip'], mx), P(L['kn'], mx))
    put(side+'LowerLeg', P(L['kn'], mx), P(L['an'], mx))
    put(side+'Foot', P(L['an'], mx), P(L['toe'], mx))
    put(side+'Toes', P(L['toe'], mx), P(L['toeTip'], mx))
for b in eb:
    if b.parent and (b.parent.tail - b.head).length < 1e-4: b.use_connect = True
bpy.ops.object.mode_set(mode='OBJECT')
# a GLB comes split along every UV seam, which leaves heat weighting nothing to flow through: weld it first
import bmesh
bm = bmesh.new(); bm.from_mesh(mesh.data); n = len(bm.verts)
bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=2e-4)
print('welded', n, '->', len(bm.verts), 'verts'); bm.to_mesh(mesh.data); bm.free()
# bind
for o in bpy.data.objects: o.select_set(o in (rig, mesh))
bpy.context.view_layer.objects.active = rig
bpy.ops.object.parent_set(type='ARMATURE_AUTO')
vg = {g.index: g.name for g in mesh.vertex_groups}
unw = [v.index for v in mesh.data.vertices if not any(g.weight > 1e-3 for g in v.groups)]
print('unweighted after heat', len(unw), 'of', len(mesh.data.vertices))
bones = [(b.name, rig.matrix_world @ b.head_local, rig.matrix_world @ b.tail_local) for b in rig.data.bones]
def seg(p, h, t):
    d = t - h; u = max(0, min(1, (p - h).dot(d) / d.length_squared)); return (p - (h + d*u)).length
if len(unw) > 0.02 * len(mesh.data.vertices) and NAME == 'Lion':
    # heat fails on the lion's mane (dozens of overlapping tufts): bind a voxel-remeshed copy, one closed skin, and
    # copy its weights over to the real mesh from the nearest point on it
    proxy = mesh.copy(); proxy.data = mesh.data.copy(); proxy.modifiers.clear(); proxy.parent = None
    proxy.matrix_world = mesh.matrix_world; bpy.context.scene.collection.objects.link(proxy)
    for g in list(proxy.vertex_groups): proxy.vertex_groups.remove(g)
    r = proxy.modifiers.new('vox', 'REMESH'); r.mode = 'VOXEL'; r.voxel_size = 0.006
    bpy.context.view_layer.objects.active = proxy
    for o in bpy.data.objects: o.select_set(o == proxy)
    bpy.ops.object.modifier_apply(modifier='vox')
    for o in bpy.data.objects: o.select_set(o in (rig, proxy))
    bpy.context.view_layer.objects.active = rig
    bpy.ops.object.parent_set(type='ARMATURE_AUTO')
    print('proxy', len(proxy.data.vertices), 'verts, unweighted', sum(1 for v in proxy.data.vertices if not any(g.weight > 1e-3 for g in v.groups)))
    from mathutils.kdtree import KDTree
    kd = KDTree(len(proxy.data.vertices))
    for v in proxy.data.vertices: kd.insert(v.co, v.index)
    kd.balance()
    pn = {g.index: g.name for g in proxy.vertex_groups}
    for g in list(mesh.vertex_groups): mesh.vertex_groups.remove(g)
    mg = {g.name: mesh.vertex_groups.new(name=g.name) for g in proxy.vertex_groups}
    for v in mesh.data.vertices:
        acc = {}
        for co, i, d in kd.find_n(v.co, 4):   # the four nearest skin points, nearer ones counting more
            w = 1 / max(d, 1e-4)
            for g in proxy.data.vertices[i].groups: acc[pn[g.group]] = acc.get(pn[g.group], 0) + g.weight * w
        tot = sum(acc.values()) or 1
        for n, x in acc.items():
            if x / tot > 0.02: mg[n].add([v.index], x / tot, 'REPLACE')
    bpy.data.objects.remove(proxy)
    unw = [v.index for v in mesh.data.vertices if not any(g.weight > 1e-3 for g in v.groups)]
    print('unweighted after proxy', len(unw))
if len(unw) > 0.02 * len(mesh.data.vertices):
    # heat failed (a mesh of loose pieces, like a skeleton's bones): each vertex to its nearest bone segments,
    # the nearest two blended by inverse distance to the 4th, so joints bend smoothly
    for g in list(mesh.vertex_groups): mesh.vertex_groups.remove(g)
    gs = {b.name: mesh.vertex_groups.new(name=b.name) for b in rig.data.bones}
    for v in mesh.data.vertices:
        p = mesh.matrix_world @ v.co
        d = sorted((seg(p, h, t), n) for n, h, t in bones)[:2]
        w = [1 / max(x, 1e-4) ** 4 for x, _ in d]; tot = sum(w)
        for (x, n), wi in zip(d, w):
            if wi / tot > 0.05: gs[n].add([v.index], wi / tot, 'REPLACE')
    print('used nearest-bone weights')
# anything still unweighted, and the head and hat: nearest bone segment
groups = {g.name: g for g in mesh.vertex_groups}
for b in rig.data.bones:
    if b.name not in groups: groups[b.name] = mesh.vertex_groups.new(name=b.name)
headZ = L['headZ'] + off.z
def to_head(p):
    if p.z > headZ: return True
    if NAME != 'Lion': return False
    # the lion's mane hangs below his chin over his shoulders: every tuft above the shoulder line goes with the head,
    # but not his sleeves (within 5.5 cm of the arm's axis, out past the shoulder)
    q = p - off
    if abs(q.x) > 0.13 and (q.y + 0.11) ** 2 + (q.z - 0.53) ** 2 < 0.055 ** 2: return False
    return q.z > 0.55 and abs(q.x) < 0.30
pinned = 0
for v in mesh.data.vertices:
    p = mesh.matrix_world @ v.co
    if to_head(p):
        for g in list(v.groups): mesh.vertex_groups[g.group].remove([v.index])
        groups['Head'].add([v.index], 1.0, 'REPLACE'); pinned += 1
    elif not any(g.weight > 1e-3 for g in v.groups):
        n = min(bones, key=lambda b: seg(p, b[1], b[2]))[0]
        groups[n].add([v.index], 1.0, 'REPLACE')
print('pinned to head', pinned)
# material
mat = bpy.data.materials.new(NAME + '_Mat'); mat.use_nodes = True
img = bpy.data.images.load(BASE); tex = mat.node_tree.nodes.new('ShaderNodeTexImage'); tex.image = img
bsdf = mat.node_tree.nodes['Principled BSDF']; mat.node_tree.links.new(tex.outputs[0], bsdf.inputs['Base Color'])
mesh.data.materials.clear(); mesh.data.materials.append(mat)
for old in [m for m in bpy.data.materials if m != mat]: bpy.data.materials.remove(old)
if rig.animation_data is None: rig.animation_data_create()
rig.animation_data.action = seated; seated.use_fake_user = True
bpy.ops.wm.save_as_mainfile(filepath=OUT)
print('rigged', NAME, 'offset', tuple(off))
