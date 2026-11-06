# Step 2 of the missile rebuild (see missile.py).
# Run: python -I missile_dump.py -- <raw.blend> <src.npz>
# Dump the missile's vertices (x1000: about 10 units long) and a UV per vertex to an npz.
import sys, numpy as np, bpy
a = sys.argv[sys.argv.index('--')+1:]
bpy.ops.wm.open_mainfile(filepath=a[0]); me = bpy.data.objects['Missile'].data
n=len(me.vertices); co=np.zeros(n*3); me.vertices.foreach_get('co',co)
nl=len(me.loops); lv=np.zeros(nl,dtype=np.int64); me.loops.foreach_get('vertex_index',lv)
uv=np.zeros(nl*2); me.uv_layers.active.data.foreach_get('uv',uv); uv=uv.reshape(-1,2)
vuv=np.zeros((n,2)); vuv[lv]=uv
np.savez(a[1], co=co.reshape(-1,3)*1000, uv=vuv)
