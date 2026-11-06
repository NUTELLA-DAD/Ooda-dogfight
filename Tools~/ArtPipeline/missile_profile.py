# Step 4 of the missile rebuild (see missile.py): the body radius along the axis (median over 72 sectors, so fins drop out), and where the fins are.
# Run: python -I missile_profile.py -- <raw.blend> <axis.npy> <prof.npy>
import sys, numpy as np, bpy
a = sys.argv[sys.argv.index('--')+1:]
bpy.ops.wm.open_mainfile(filepath=a[0]); ca=np.load(a[1]); c,ax=ca[:3],ca[3:]
m = bpy.data.objects['Missile']
n=len(m.data.vertices); co=np.zeros(n*3); m.data.vertices.foreach_get('co',co); X=co.reshape(-1,3)*1000-c
u=np.cross(ax,[0,0,1.0]); u/=np.linalg.norm(u); w=np.cross(ax,u)
t=X@ax; px,py=X@u,X@w; r=np.hypot(px,py); ang=np.degrees(np.arctan2(py,px))%360
print('t',t.min(),t.max())
NS=72; sec=(ang/(360/NS)).astype(int)
prof=[]
for t0 in np.arange(t.min(),t.max()+0.05,0.1):
    s=np.abs(t-t0)<0.05
    if s.sum()<20: continue
    mx=np.zeros(NS)
    np.maximum.at(mx,sec[s],r[s])
    body=np.median(mx[mx>0])
    fins=[(i*5,round(mx[i],2)) for i in range(NS) if mx[i]>body+0.15]
    prof.append((t0,body)); print('%.2f body=%.3f'%(t0,body), fins if len(fins)<20 else len(fins))
np.save(a[2],np.array(prof))
