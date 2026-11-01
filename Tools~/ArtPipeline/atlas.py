# Cockpit atlas: flat-colour swatches on top, the instrument panel art below. Stylized: thick ink outlines, cream markings.
from PIL import Image, ImageDraw, ImageFont
import math, json, sys
OUT=sys.argv[1]
W=1024; img=Image.new('RGB',(W,W),(20,20,20)); d=ImageDraw.Draw(img)
SW={ # name: colour (sRGB)
 'p51_green':(104,118,62),'spit_grey':(122,132,112),'bf109_grey':(64,68,70),'panel':(30,31,33),
 'leather':(96,56,34),'metal':(150,152,156),'darkmetal':(70,72,76),'rubber':(22,22,22),
 'red':(200,40,34),'yellow':(232,190,40),'cream':(236,226,196),'glass':(150,200,190),
 'wood':(120,80,46),'black':(10,10,10),'white':(240,240,236),'sky':(70,140,215),'ground':(130,90,50),
 'seat_p51':(84,96,52),'seat_spit':(100,72,50),'seat_bf':(52,54,56),'olive':(90,92,58),'brass':(190,150,70)}
names=list(SW)
uv={}
for i,n in enumerate(names):
    c,r=i%8,i//8
    x0,y0=c*128,r*128
    d.rectangle([x0,y0,x0+127,y0+127],fill=SW[n])
    # a soft painted edge darkening so flat faces don't look like CG plastic
    for k in range(10):
        a=int(18*(1-k/10)); col=tuple(max(v-a,0) for v in SW[n])
        d.rectangle([x0+k,y0+k,x0+127-k,y0+127-k],outline=col)
    uv[n]=((c+0.5)/8, 1-(r+0.5)/8)
# panel art in the bottom half: 1024 x 512
P=Image.new('RGB',(1024,512),SW['panel']); pd=ImageDraw.Draw(P)
try:
    F=lambda s: ImageFont.truetype('/usr/share/fonts/truetype/dejavu/DejaVuSans-Bold.ttf',s)
except Exception: F=lambda s: ImageFont.load_default()
CREAM=(240,228,196); INK=(8,8,8)
# panel screws and a subtle lighter edge
pd.rounded_rectangle([6,6,1017,505],radius=30,outline=(55,57,60),width=10)
for x,y in [(30,30),(994,30),(30,482),(994,482),(512,30),(512,482)]:
    pd.ellipse([x-9,y-9,x+9,y+9],fill=(90,92,96),outline=INK,width=3); pd.line([x-6,y,x+6,y],fill=INK,width=3)
G={ # name: (cx,cy,r, kind)
 'Airspeed':(330,170,88,'dial'),'Horizon':(512,170,88,'window'),'Altimeter':(694,170,88,'dial'),
 'Climb':(330,372,76,'dial'),'Compass':(512,372,76,'compass'),'RPM':(694,372,76,'dial'),
 'Boost':(150,215,62,'small'),'Fuel':(150,400,56,'small'),'Oil':(874,215,62,'small'),'Clock':(874,400,56,'clock')}
def ring(cx,cy,r):
    pd.ellipse([cx-r-14,cy-r-14,cx+r+14,cy+r+14],fill=(70,72,78),outline=INK,width=6)   # bezel
    pd.ellipse([cx-r-4,cy-r-4,cx+r+4,cy+r+4],fill=(120,122,128),outline=INK,width=3)
    pd.ellipse([cx-r,cy-r,cx+r,cy+r],fill=(14,14,16),outline=INK,width=4)
def ticks(cx,cy,r,a0,a1,n,major,labels=None,red=None):
    for i in range(n+1):
        a=math.radians(a0+(a1-a0)*i/n)
        big=i%major==0; L=r*(0.22 if big else 0.12)
        x0,y0=cx+math.sin(a)*(r-6),cy-math.cos(a)*(r-6); x1,y1=cx+math.sin(a)*(r-6-L),cy-math.cos(a)*(r-6-L)
        pd.line([x0,y0,x1,y1],fill=CREAM,width=5 if big else 3)
        if labels and big:
            t=labels[i//major]; f=F(int(r*0.24)); tw=pd.textlength(t,font=f)
            xt,yt=cx+math.sin(a)*(r*0.58),cy-math.cos(a)*(r*0.58)
            pd.text((xt-tw/2,yt-r*0.13),t,fill=CREAM,font=f)
    if red:
        b0,b1=red; pd.arc([cx-r+4,cy-r+4,cx+r-4,cy+r-4],b0-90,b1-90,fill=(220,40,30),width=8)
for n,(cx,cy,r,k) in G.items():
    ring(cx,cy,r)
    if n=='Airspeed': ticks(cx,cy,r,0,330,22,2,['0','','','','200','','','','400','','','',''],red=(300,330))
    if n=='Altimeter': ticks(cx,cy,r,0,360,50,5,['0','1','2','3','4','5','6','7','8','9',''])
    if n=='Climb': ticks(cx,cy,r,-90-170,-90+170,16,2,['4','3','2','1','0','1','2','3','4'])
    if n=='RPM': ticks(cx,cy,r,-135,135,20,4,['0','1','2','3','4','5'],red=(110,135))
    if n=='Horizon': pd.ellipse([cx-r,cy-r,cx+r,cy+r],fill=(40,40,44),outline=INK,width=4)
    if n=='Compass':
        pd.ellipse([cx-r,cy-r,cx+r,cy+r],fill=(20,20,22))
        for i,t in enumerate('NESW'):
            a=math.radians(90*i); f=F(int(r*0.34)); tw=pd.textlength(t,font=f)
            pd.text((cx+math.sin(a)*r*0.62-tw/2,cy-math.cos(a)*r*0.62-r*0.2),t,fill=(250,200,60) if t=='N' else CREAM,font=f)
        ticks(cx,cy,r,0,360,24,6)
        pd.polygon([(cx,cy-r*0.3),(cx-r*0.12,cy+r*0.2),(cx,cy+r*0.08),(cx+r*0.12,cy+r*0.2)],fill=(250,200,60),outline=INK)
    if k in ('small','clock'):
        if k=='clock': ticks(cx,cy,r,0,360,12,3,['12','3','6','9',''])
        else: ticks(cx,cy,r,-120,120,8,2,['0','','','','F'] if n=='Fuel' else ['','','','',''],red=(90,120))
        # static needle painted on
        a=math.radians({'Boost':40,'Fuel':70,'Oil':-20,'Clock':130}[n])
        pd.line([cx,cy,cx+math.sin(a)*r*0.8,cy-math.cos(a)*r*0.8],fill=(245,240,220),width=6)
        if n=='Clock':
            a=math.radians(300); pd.line([cx,cy,cx+math.sin(a)*r*0.55,cy-math.cos(a)*r*0.55],fill=(245,240,220),width=7)
        pd.ellipse([cx-8,cy-8,cx+8,cy+8],fill=(60,60,60),outline=INK,width=2)
    f=F(13); lab={'Airspeed':'AIRSPEED','Altimeter':'ALT','Climb':'CLIMB','RPM':'RPM','Boost':'BOOST','Fuel':'FUEL','Oil':'OIL °C','Clock':'','Horizon':'','Compass':''}[n]
    if lab:
        tw=pd.textlength(lab,font=f); pd.text((cx-tw/2,cy-r*0.36),lab,fill=(200,190,160),font=f)
# switches: a row of toggles and two labelled red/yellow knobs
for i,x in enumerate(range(250,780,44)):
    if 455<x<570: continue
    y=480; pd.rounded_rectangle([x-12,y-26,x+12,y-4],radius=5,fill=(90,92,96),outline=INK,width=3)
    pd.line([x,y-15,x+(6 if i%2 else -6),y-34],fill=(220,220,220),width=6)
for x,c,t in [(60,(210,40,30),'GUNS'),(964,(240,190,40),'MAG')]:
    pd.ellipse([x-26,90-26,x+26,90+26],fill=c,outline=INK,width=5); f=F(15); tw=pd.textlength(t,font=f); pd.text((x-tw/2,124),t,fill=CREAM,font=f)
img.paste(P,(0,512))
img.save(OUT+'/Cockpit_Atlas.png')
# gauge centres in panel UV space (u 0..1 across, v 0..1 up) for the 3D needles
json.dump({'swatch':uv,'gauges':{n:((cx/1024),(1-cy/512),r/1024) for n,(cx,cy,r,k) in G.items()}},open(OUT+'/atlas.json','w'),indent=1)
print('ok')
