# Open a pilot exactly as the game ships it (the repo's <Name>Pilot.fbx) as the blend the polish tools expect: the rig
# and body under their own names, the clips under plain names (Seated, Fly, ...), the rig in rest pose with Seated
# assigned, and the repo's base colour on the body's material (so renders show the real paint).
# Run: python -I pilot_prep.py -- <in.fbx> <BaseColor.png> <out.blend>
import bpy, sys

a = sys.argv[sys.argv.index('--') + 1:]
SRC, BASE, DST = a[:3]
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.fbx(filepath=SRC, automatic_bone_orientation=False, ignore_leaf_bones=False)
rig = next(o for o in bpy.data.objects if o.type == 'ARMATURE')
body = next(o for o in bpy.data.objects if o.type == 'MESH')
for o in list(bpy.data.objects):
    if o not in (rig, body): bpy.data.objects.remove(o)
# the FBX importer leaves the rig at 0.01 scale with a +90 X rotation: apply both so the rig's space is metres, Z up,
# the pilot facing -Y, which is what the tools measure in
for o in bpy.data.objects: o.select_set(o in (rig, body))
bpy.context.view_layer.objects.active = rig
rig.animation_data.action = None
bpy.ops.object.transform_apply(location=False, rotation=True, scale=True)
for act in list(bpy.data.actions):
    plain = act.name.split('|')[-1]
    if plain in [x.name for x in bpy.data.actions if x is not act]: bpy.data.actions.remove(act); continue
    act.name = plain; act.use_fake_user = True
print('actions', sorted(x.name for x in bpy.data.actions))
# the FBX keeps no bone tails: put each bone's tail back on its child's head (so the arm chains are connected for
# the IK), and give the hands the length the rig was built with
bpy.ops.object.mode_set(mode='EDIT')
eb = rig.data.edit_bones
NEXT = {'Hips': 'Spine', 'Spine': 'Chest', 'Chest': 'Neck', 'Neck': 'Head'}
for b in eb:                                   # only the length changes: the bone's axis and roll stay as imported
    kids = [c for c in b.children if (c.head - b.head).length > 1e-4]
    c = eb[NEXT[b.name]] if b.name in NEXT else (kids[0] if len(kids) == 1 else None)
    L = (c.head - b.head).length if c else (0.14 if b.name.endswith('Hand') else None)
    if L:
        d = (b.tail - b.head).normalized(); roll = b.roll; b.tail = b.head + d * L; b.roll = roll
bpy.ops.object.mode_set(mode='OBJECT')
mat = body.material_slots[0].material if body.material_slots else None
if mat is None:
    mat = bpy.data.materials.new(body.name.split('_')[0] + '_Mat'); body.data.materials.append(mat)
mat.use_nodes = True
nt = mat.node_tree
bsdf = next(n for n in nt.nodes if n.type == 'BSDF_PRINCIPLED')
tex = next((n for n in nt.nodes if n.type == 'TEX_IMAGE'), None) or nt.nodes.new('ShaderNodeTexImage')
tex.image = bpy.data.images.load(BASE); tex.image.pack()
nt.links.new(tex.outputs['Color'], bsdf.inputs['Base Color'])
rig.animation_data.action = bpy.data.actions['Seated']
rig.data.pose_position = 'REST'
bpy.context.view_layer.update()
print('rig', rig.name, 'dims', tuple(round(x, 3) for x in rig.dimensions), 'body', body.name, 'dims', tuple(round(x, 3) for x in body.dimensions),
      'head z', round(rig.data.bones['Head'].head_local.z, 3), 'hand', tuple(round(x, 3) for x in rig.data.bones['RightHand'].head_local))
bpy.ops.wm.save_as_mainfile(filepath=DST)
