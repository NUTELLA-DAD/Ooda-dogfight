# Gauntlet texture pass: seam fill, edge-preserving cleanup, model-specific repaints. Output 4096 base colors.
import cv2, numpy as np, sys, os
S='/tmp/claude-0/-home-claude-ooda-dogfight/f116a480-7384-5fc5-8d26-5a8bc0804896/scratchpad/'
name=sys.argv[1]; R=4096
OUT=S+'out/'; os.makedirs(OUT,exist_ok=True)
srcf={'Bf109':S+'bf109src/jpg5.jpg'}.get(name,S+f'src/{name}_BaseColor.png')
src=cv2.imread(srcf)
if name=='Bf109':
    # put back the earlier thread's paint-out of the tail markings: copy the edited 2K patches over the 8K source
    old=cv2.imread('/home/claude/ooda-dogfight/Assets/OODA/Art/Bf109/Bf109_BaseColor.png')
    down=cv2.resize(src,(2048,2048),interpolation=cv2.INTER_AREA)
    d=np.abs(down.astype(int)-old.astype(int)).mean(2)>40
    n,lab,st,_=cv2.connectedComponentsWithStats(cv2.dilate(d.astype(np.uint8),np.ones((25,25),np.uint8)))
    up=cv2.resize(old,(8192,8192),interpolation=cv2.INTER_CUBIC)
    for i in range(1,n):
        x,y,w,h,a=st[i]
        if a<400: continue
        x0,y0,x1,y1=max(x-8,0)*4,max(y-8,0)*4,min(x+w+8,2048)*4,min(y+h+8,2048)*4
        src[y0:y1,x0:x1]=up[y0:y1,x0:x1]; print('patched',x0,y0,x1,y1)
img=cv2.resize(src,(R,R),interpolation=cv2.INTER_AREA)
pos=np.load(S+f'pos/{name}.npy')[::-1].copy()   # blender rows are bottom-up
mask=pos[...,3]>0.5; P=pos[...,:3]

# 1. edge-preserving cleanup: flatten the blotchy baked shading and JPEG/AI mush, keep ink lines
clean=cv2.pyrMeanShiftFiltering(img,5,18)
clean=cv2.bilateralFilter(clean,7,25,5)

# 2. model-specific repaints from the position map
WHITE=np.array([236,238,238],np.uint8); BLACK=np.array([34,32,30],np.uint8)
def thin_dark(im):
    g=cv2.cvtColor(im,cv2.COLOR_BGR2GRAY)
    bh=cv2.morphologyEx(g,cv2.MORPH_BLACKHAT,cv2.getStructuringElement(cv2.MORPH_ELLIPSE,(9,9)))
    return bh>45
def stripes(sel_fn, coord, edges):
    global WHITE,BLACK
    sel=sel_fn()&mask
    lines=thin_dark(clean)
    for i in range(len(edges)-1):
        band=sel&(coord>=edges[i])&(coord<edges[i+1])
        col=WHITE if i%2==0 else BLACK
        light=col.mean()>128
        paint=band & ~(lines if light else np.zeros_like(band))
        clean[paint]=col
        print('band',i,band.sum())
if name=='P51':
    ax=np.abs(P[...,0])
    wing=lambda:(ax>1.18)&(P[...,1]>-2.5)&(P[...,1]<-0.3)&(P[...,2]>-0.75)&(P[...,2]<0.05)&~((P[...,1]<-2.1)&(P[...,2]<-0.42))
    stripes(wing, ax, [1.2,1.6,2.0,2.4,2.8,3.2])
if name=='B17':
    ax=np.abs(P[...,0])
    wing=lambda:(P[...,1]>-3.65)&(P[...,1]<1.5)&(P[...,2]>-0.95)&(P[...,2]<0.4)
    WHITE,BLACK=BLACK,WHITE
    stripes(wing, ax, [4.95,5.55,6.15,6.75,7.35,7.95])
    # the old ragged stripe ends outboard of the new bands: back to bare metal
    g=cv2.cvtColor(clean,cv2.COLOR_BGR2GRAY)
    zone=wing()&mask&(ax>=7.95)&(ax<9.0)
    metal=np.median(clean[zone&(g>170)],axis=0).astype(np.uint8)
    blob=zone&(g<130)&~thin_dark(clean)
    clean[blob]=metal; print('metal',metal,blob.sum())

# 3. crisp ink: darken thin dark lines slightly and unsharp
blur=cv2.GaussianBlur(clean,(0,0),1.6)
sharp=cv2.addWeighted(clean,1.6,blur,-0.6,0)

# 4. seam fill: push island colours outward so mips and filtering never pull in background
col=sharp.astype(np.float32); m=mask.astype(np.float32)
filled=col*m[...,None]; cur=m.copy()
for it in range(40):
    if cur.min()>0: break
    s=cv2.blur(filled,(3,3)); c=cv2.blur(cur,(3,3))
    new=(cur==0)&(c>0)
    filled[new]=s[new]/c[new][:,None]; cur[new]=1
out=np.where(cur[...,None]>0,filled,cv2.blur(filled,(1,1))).clip(0,255).astype(np.uint8)
# erode the mask by 2px when deciding what is 'inside': edge texels of an island are often garbage
cv2.imwrite(OUT+f'{name}_BaseColor.png',out)
cv2.imwrite(S+f'cmp_{name}.png',np.hstack([cv2.resize(img[R//2:R//2+800,R//2:R//2+800],(600,600)),cv2.resize(out[R//2:R//2+800,R//2:R//2+800],(600,600))]))
print('done',name)
