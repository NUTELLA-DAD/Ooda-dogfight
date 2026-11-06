# Step 3 of the missile rebuild (see missile.py).
# Run: python -I missile_axis.py -- <raw.blend> <axis.npy>
# Fit the missile's true axis (circle fits per slice through the body, fins left out) and its radius profile.
import sys, numpy as np, bpy
a = sys.argv[sys.argv.index('--')+1:]
bpy.ops.wm.open_mainfile(filepath=a[0])
m = bpy.data.objects['Missile']
n=len(m.data.vertices); co=np.zeros(n*3); m.data.vertices.foreach_get('co',co); co=co.reshape(-1,3)*1000
c=co.mean(0); X=co-c
w,v=np.linalg.eigh(np.cov(X.T)); ax=v[:,-1]
def frame(ax):
    u=np.cross(ax,[0,0,1.0]); u/=np.linalg.norm(u); w=np.cross(ax,u); return u,w
def circ(p):
    A=np.c_[2*p,np.ones(len(p))]; b=(p**2).sum(1); s=np.linalg.lstsq(A,b,rcond=None)[0]; cc=s[:2]; return cc, np.sqrt(s[2]+cc@cc)
for it in range(4):
    u,wv=frame(ax); t=X@ax; P=np.c_[X@u,X@wv]
    lo,hi=t.min(),t.max(); cs=[]
    for t0 in np.linspace(lo+1.5,hi-1.2,30):
        s=np.abs(t-t0)<0.12; p=P[s]
        cc,r=circ(p[np.linalg.norm(p-np.median(p,0),axis=1)<0.9])
        for k in range(3):
            d=np.abs(np.linalg.norm(p-cc,axis=1)-r); q=p[d<0.06]
            if len(q)<50: break
            cc,r=circ(q)
        cs.append((t0,cc[0],cc[1],r))
    cs=np.array(cs)
    # line through the slice centres: shift and tilt the axis
    g=cs[(cs[:,3]<0.53)&(cs[:,0]<1.1)]
    A=np.c_[g[:,0],np.ones(len(g))]
    ku=np.linalg.lstsq(A,g[:,1],rcond=None)[0]; kw=np.linalg.lstsq(A,g[:,2],rcond=None)[0]
    ax=ax+ku[0]*u+kw[0]*wv; ax/=np.linalg.norm(ax); X=X-(ku[1]*u+kw[1]*wv); c=c+(ku[1]*u+kw[1]*wv)
    print('iter',it,'tilt',ku[0],kw[0],'off',ku[1],kw[1])
for r in cs: print('t=%.2f r=%.3f off=(%.3f,%.3f)'%(r[0],r[3],r[1],r[2]))
np.save(a[1], np.concatenate([c,ax]))
