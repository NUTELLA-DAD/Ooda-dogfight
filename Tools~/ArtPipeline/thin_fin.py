# Slim a fighter's fin and rudder: scale the body's vertices across the span (x) above the fin root, easing in over a
# short band so the dorsal fillet blends into the fuselage. The cartoon P-51's fin was half as thick again as it should be.
# Run: python -I thin_fin.py -- <in.blend> <out.blend> <body> <y_from> <z_root> <z_full> <factor>
import bpy, sys
a = sys.argv[sys.argv.index('--') + 1:]
bpy.ops.wm.open_mainfile(filepath=a[0])
body = bpy.data.objects[a[2]]; y0, z0, z1, k = map(float, a[3:7])
n = 0
for v in body.data.vertices:
    if v.co.y > y0 and v.co.z > z0:
        t = min(1.0, (v.co.z - z0) / (z1 - z0)); v.co.x *= 1 - (1 - k) * t; n += 1
body.data.update()
print('fin: slimmed', n, 'vertices')
bpy.ops.wm.save_as_mainfile(filepath=a[1])
