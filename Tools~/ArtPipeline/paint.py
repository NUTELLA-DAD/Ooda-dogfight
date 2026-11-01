import numpy as np, cv2
m=np.load('/home/claude/art/flak/flak_maps.npy'); ao,pt,P=m[...,0],m[...,1],m[...,2:5]
x,y,z=P[...,0],P[...,1],P[...,2]
base=np.zeros(ao.shape+(3,),np.float32); base[:]=(96,104,86)        # field grey, RGB
barrel=(np.abs(x)<0.2)&(z>0.85)&(z<1.56)&(y<-0.35)
tyre=(np.abs(x)>0.585)&(z<0.36)
base[barrel]=(62,64,68); base[tyre]=(30,30,30)
muzzle=barrel&(y<-1.5); base[muzzle]=(40,38,36)
a=np.clip(ao,0,1)**0.8
col=base*(0.42+0.58*a[...,None])
edge=np.clip((pt-0.52)*6,0,1); cav=np.clip((0.47-pt)*6,0,1)
col=col+edge[...,None]*55-cav[...,None]*35
col=np.clip(col,0,255).astype(np.uint8)
col=cv2.bilateralFilter(col,5,20,3)
img=col[::-1,:,::-1]   # blender bottom-up rows, RGB -> BGR
cv2.imwrite('/home/claude/art/flak/Flak_BaseColor.png',img)
ms=np.zeros(img.shape[:2]+(4,),np.uint8); ms[...,3]=np.where(barrel[::-1],120,60).astype(np.uint8); ms[...,2]=np.where(barrel[::-1],180,0).astype(np.uint8)
cv2.imwrite('/home/claude/art/flak/Flak_MetallicSmoothness.png',ms)  # BGRA: R metal (index 2), A smoothness
print('ok')
