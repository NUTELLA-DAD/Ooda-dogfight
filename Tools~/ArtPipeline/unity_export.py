# Export a prop/aircraft blend to FBX the way Unity wants it: every object carries a +90 X rotation (mesh data
# counter-rotated, so nothing moves), which the exporter's -90 X axis fix on the root cancels. The FBX then has no
# rotations at all, child offsets are Y-up, and every empty's +Z (CockpitCamera, PilotMount) faces the nose in Unity,
# as the original models had. Exporting the Z-up objects straight leaves the empties looking up and the root tilted.
# Run: python -I unity_export.py -- <in.blend> <out.fbx> [out.blend]
import bpy, sys, math
from mathutils import Matrix

a = sys.argv[sys.argv.index('--') + 1:]
bpy.ops.wm.open_mainfile(filepath=a[0])
rx = Matrix.Rotation(math.radians(90), 4, 'X')
objs = [o for o in bpy.data.objects if o.type in ('MESH', 'EMPTY')]
old = {o.name: o.matrix_world.copy() for o in objs}
def depth(o): return 0 if o.parent is None else 1 + depth(o.parent)
done = set()
for o in sorted(objs, key=depth):
    W = old[o.name]
    loc, rot, sca = W.decompose()
    assert max(abs(s - 1) for s in sca) < 1e-3, (o.name, sca)
    target = Matrix.Translation(loc) @ rx
    if o.type == 'MESH' and o.data.name not in done:
        o.data.transform(target.inverted() @ W)
        done.add(o.data.name)
    o.matrix_world = target
    bpy.context.view_layer.update()
for o in bpy.data.objects: o.select_set(False)
bpy.ops.export_scene.fbx(filepath=a[1], object_types={'MESH', 'EMPTY'}, apply_scale_options='FBX_SCALE_UNITS',
                         use_space_transform=True, bake_space_transform=False, path_mode='STRIP', use_mesh_modifiers=True,
                         mesh_smooth_type='FACE', add_leaf_bones=False)
if len(a) > 2: bpy.ops.wm.save_as_mainfile(filepath=a[2])
print('exported', a[1])
