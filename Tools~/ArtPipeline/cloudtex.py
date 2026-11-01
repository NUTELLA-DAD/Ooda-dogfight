import numpy as np, cv2
S='/tmp/claude-0/-home-claude-ooda-dogfight/f116a480-7384-5fc5-8d26-5a8bc0804896/scratchpad/'
pos=np.load(S+'pos/Cloud.npy')[::-1]; R=2048
pos=cv2.resize(pos,(R,R),interpolation=cv2.INTER_NEAREST)
m=cv2.erode((pos[...,3]>0.5).astype(np.uint8),np.ones((5,5),np.uint8))>0; P=pos[...,:3]
z=P[...,2]; zm,zM=np.percentile(z[m],2),np.percentile(z[m],98)
t=np.clip((z-zm)/(zM-zm),0,1); t=t*t*(3-2*t)
# a little lumpy variation so it isn't a flat ramp: distance from the centre line
r=np.sqrt(P[...,0]**2+P[...,1]**2); rr=np.clip(r/np.percentile(r[m],98),0,1)
top=np.array([255,255,255],float); mid=np.array([250,244,240],float); bot=np.array([214,196,178],float)  # BGR: white, warm white, soft blue-grey belly
col=np.where(t[...,None]>0.45, mid+(top-mid)*((t[...,None]-0.45)/0.55), bot+(mid-bot)*(t[...,None]/0.45))
col=col*(0.94+0.06*rr[...,None])
# seam fill
f=col*m[...,None]; cur=m.astype(np.float32)
for i in range(60):
    if cur.min()>0: break
    s=cv2.blur(f.astype(np.float32),(3,3)); c=cv2.blur(cur,(3,3)); new=(cur==0)&(c>0)
    f[new]=s[new]/c[new][:,None]; cur[new]=1
f=cv2.GaussianBlur(f.astype(np.float32),(0,0),4)
cv2.imwrite(S+'out/Cloud_BaseColor.png',np.clip(f,0,255).astype(np.uint8))
ms=np.zeros((512,512,4),np.uint8); ms[...,3]=int(0.12*255)
cv2.imwrite(S+'out/Cloud_MetallicSmoothness.png',ms)
print('ok')
