from PIL import Image, ImageDraw, ImageFilter
import math, sys
S=2048
img=Image.new("RGBA",(S,S),(0,0,0,0))
# background: rounded square, sky-to-grass toon gradient
bg=Image.new("RGBA",(S,S))
d=ImageDraw.Draw(bg)
for y in range(S):
    t=y/S
    c=(int(28+20*t),int(46+60*t),int(78-30*t))
    d.line([(0,y),(S,y)],fill=c+(255,))
mask=Image.new("L",(S,S),0); ImageDraw.Draw(mask).rounded_rectangle([40,40,S-40,S-40],radius=380,fill=255)
img.paste(bg,(0,0),mask)
BLUE=(52,214,240); RED=(240,74,58); WHITE=(255,248,230)
def ring(cx,cy,r,col,w,fill_a):
    lay=Image.new("RGBA",(S,S),(0,0,0,0)); ld=ImageDraw.Draw(lay)
    ld.ellipse([cx-r,cy-r,cx+r,cy+r],fill=col+(fill_a,))
    ld.ellipse([cx-r,cy-r,cx+r,cy+r],outline=col+(255,),width=w)
    return lay
def ticks(cx,cy,r,angles,w,L):
    lay=Image.new("RGBA",(S,S),(0,0,0,0)); ld=ImageDraw.Draw(lay)
    for a in angles:
        a=math.radians(a); x0=cx+(r-L)*math.cos(a); y0=cy+(r-L)*math.sin(a); x1=cx+(r+L)*math.cos(a); y1=cy+(r+L)*math.sin(a)
        ld.line([x0,y0,x1,y1],fill=WHITE+(255,),width=w)
    return lay
bx,by,br=780,1090,500; rx,ry,rr=1300,960,560
img=Image.alpha_composite(img,ring(rx,ry,rr,RED,72,70))
img=Image.alpha_composite(img,ring(bx,by,br,BLUE,72,70))
img=Image.alpha_composite(img,ticks(bx,by,br,[-90,-30,30,90,150,210],34,70))
img=Image.alpha_composite(img,ticks(rx,ry,rr,[-60,0,60,120,180,240],34,70))
# white motion arcs where each plane is going
lay=Image.new("RGBA",(S,S),(0,0,0,0)); ld=ImageDraw.Draw(lay)
ld.arc([bx-br-60,by-br-60,bx+br+60,by+br+60],start=150,end=245,fill=WHITE+(255,),width=46)
ld.arc([rx-rr-60,ry-rr-60,rx+rr+60,ry+rr+60],start=-25,end=70,fill=WHITE+(255,),width=46)
img=Image.alpha_composite(img,lay)
# clip to rounded square
out=Image.new("RGBA",(S,S),(0,0,0,0)); out.paste(img,(0,0),mask)
out.resize((1024,1024),Image.LANCZOS).save(sys.argv[1])
prev=Image.new("RGBA",(256+128+64+32+16+5*20,256),(40,40,40,255)); x=10
for s in (256,128,64,32,16):
    prev.paste(out.resize((s,s),Image.LANCZOS),(x,0),out.resize((s,s),Image.LANCZOS)); x+=s+20
prev.save("preview.png")
