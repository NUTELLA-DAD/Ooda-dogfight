# Open a jet exactly as the game ships it (the repo's FBX) as a blend for the cutting tools: every object's FBX
# rotation and scale applied, so the body sits unrotated in metres, Z up, the nose to -Y (what unity_export.py writes
# back). Run: python -I jet_prep.py -- <in.fbx> <out.blend>
import bpy, sys
a = sys.argv[sys.argv.index('--') + 1:]
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.fbx(filepath=a[0])
def depth(o): return 0 if o.parent is None else 1 + depth(o.parent)
W = {o.name: o.matrix_world.copy() for o in bpy.data.objects}
for o in sorted(bpy.data.objects, key=depth):               # bake each object's world rotation and scale into its data
    loc, rot, sca = W[o.name].decompose()
    from mathutils import Matrix
    T = Matrix.Translation(loc)
    if o.type == 'MESH' and o.data.users == 1: o.data.transform(T.inverted() @ W[o.name])
    o.matrix_world = T
    bpy.context.view_layer.update()
for o in bpy.data.objects:
    if o.type == 'MESH': print(o.name, tuple(round(v, 3) for v in o.matrix_world.translation), [round(x, 2) for x in o.dimensions])
bpy.ops.wm.save_as_mainfile(filepath=a[1])
