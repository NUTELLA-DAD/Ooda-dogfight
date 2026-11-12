from PIL import Image, ImageDraw
import math, sys
S=2048
mask=Image.new("L",(S,S),0); ImageDraw.Draw(mask).rounded_rectangle([40,40,S-40,S-40],radius=380,fill=255)
bg=Image.new("RGBA",(S,S)); d=ImageDraw.Draw(bg)
for y in range(S):
    t=y/S; d.line([(0,y),(S,y)],fill=(int(28+20*t),int(46+60*t),int(78-30*t),255))
img=Image.new("RGBA",(S,S),(0,0,0,0)); img.paste(bg,(0,0),mask)
BLUE=(52,214,240); RED=(240,74,58); WHITE=(255,248,230); INK=(20,24,36)
R=380; GAP=30; cy=1024
bx=S/2-(R+GAP/2); rx=S/2+(R+GAP/2)
def comp(f):
    global img
    lay=Image.new("RGBA",(S,S),(0,0,0,0)); f(ImageDraw.Draw(lay)); img=Image.alpha_composite(img,lay)
def ring(cx,col):
    def f(ld):
        ld.ellipse([cx-R,cy-R,cx+R,cy+R],fill=col+(60,))
        ld.ellipse([cx-R,cy-R,cx+R,cy+R],outline=col+(255,),width=64)
    comp(f)
def ticks(cx,angles):
    def f(ld):
        for a in angles:
            a=math.radians(a); L=60
            ld.line([cx+(R-L)*math.cos(a),cy+(R-L)*math.sin(a),cx+(R+L)*math.cos(a),cy+(R+L)*math.sin(a)],fill=WHITE+(255,),width=30)
    comp(f)
ring(bx,BLUE); ring(rx,RED)
ticks(bx,[30,150,210,330]); ticks(rx,[30,150,210,330])
# top-down fighter, nose up (-y), unit ~ 1 = half span
PLANE=[(0,-1.0),(0.09,-0.85),(0.11,-0.35),(1.0,-0.12),(1.0,0.05),(0.12,0.08),(0.08,0.62),(0.42,0.75),(0.42,0.88),(0,0.85),
       (-0.42,0.88),(-0.42,0.75),(-0.08,0.62),(-0.12,0.08),(-1.0,0.05),(-1.0,-0.12),(-0.11,-0.35),(-0.09,-0.85)]
def plane(cx,ang_deg,cw,col,size=270):
    a=math.radians(ang_deg); px=cx+R*math.cos(a); py=cy+R*math.sin(a)
    # heading tangent to circle
    hx,hy=(-math.sin(a),math.cos(a)) if cw else (math.sin(a),-math.cos(a))
    rot=math.atan2(hx,-hy)   # angle from "up"
    c,s=math.cos(rot),math.sin(rot)
    pts=[(px+size*(x*c-y*s), py+size*(x*s+y*c)) for x,y in PLANE]
    def f(ld):
        # motion arc behind it
        start=ang_deg-75 if cw else ang_deg+15
        ld.arc([cx-R-70,cy-R-70,cx+R+70,cy+R+70],start=start,end=start+60,fill=WHITE+(230,),width=40)
    comp(f)
    def g(ld):
        ld.polygon(pts,fill=INK+(255,))
    lay=Image.new("RGBA",(S,S),(0,0,0,0)); ld=ImageDraw.Draw(lay); ld.polygon(pts,fill=INK+(255,),outline=INK+(255,),width=70)
    global img; img=Image.alpha_composite(img,lay)
    def h(ld):
        ld.polygon(pts,fill=WHITE+(255,))
        # coloured roundel on each wing
        for sx in (0.6,-0.6):
            wx,wy=px+size*(sx*c-(-0.03)*s), py+size*(sx*s+(-0.03)*c)
            ld.ellipse([wx-30,wy-30,wx+30,wy+30],fill=col+(255,))
    comp(h)
plane(bx,-90,True,BLUE)
plane(rx,90,True,RED)
out=Image.new("RGBA",(S,S),(0,0,0,0)); out.paste(img,(0,0),mask)
out=out.resize((1024,1024),Image.LANCZOS); out.save(sys.argv[1])
prev=Image.new("RGBA",(256+128+64+32+16+5*20,256),(40,40,40,255)); x=10
for sz in (256,128,64,32,16):
    im=out.resize((sz,sz),Image.LANCZOS); prev.paste(im,(x,0),im); x+=sz+20
prev.save(sys.argv[2] if len(sys.argv) > 2 else "furball_icon_sizes.png")
