# Cloud: round off the pock-marks (rings and dimples from the generator) with a gentle smooth, keep the UVs, export.
import bpy, sys
OUT=sys.argv[sys.argv.index('--')+1]
bpy.ops.wm.open_mainfile(filepath='/home/claude/art/blender/Cloud.blend')
ob=bpy.data.objects['Cloud']; bpy.context.view_layer.objects.active=ob; ob.select_set(True)
bpy.ops.object.mode_set(mode='EDIT'); bpy.ops.mesh.select_all(action='SELECT'); bpy.ops.mesh.remove_doubles(threshold=1e-4); bpy.ops.mesh.normals_make_consistent(inside=False); bpy.ops.object.mode_set(mode='OBJECT')
m=ob.modifiers.new('s','LAPLACIANSMOOTH'); m.iterations=4; m.lambda_factor=0.6; m.use_volume_preserve=True; m.use_normalized=True
bpy.ops.object.modifier_apply(modifier='s')
bpy.ops.object.shade_smooth()
bpy.ops.wm.save_as_mainfile(filepath=OUT+'/Cloud.blend')
bpy.ops.export_scene.fbx(filepath=OUT+'/Cloud.fbx', object_types={'MESH'}, apply_scale_options='FBX_SCALE_UNITS', use_space_transform=True, bake_space_transform=False, path_mode='STRIP', mesh_smooth_type='FACE')
