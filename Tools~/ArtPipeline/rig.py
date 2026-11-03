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
pinned = 0
for v in mesh.data.vertices:
    p = mesh.matrix_world @ v.co
    if p.z > headZ:
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
