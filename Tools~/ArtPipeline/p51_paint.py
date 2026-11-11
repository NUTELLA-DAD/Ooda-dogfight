# Paint the clean P-51: polished bare metal with a quilt of panels, crisp panel lines and rivets, red nose and rudder,
# olive anti-glare panel, invasion stripes, star-and-bar insignia, code letters and serial. Every texel knows where it
# sits on the aircraft (texmap.npz from p51_hull.py), so every marking is drawn as an exact shape in aircraft space.
# Run: python p51_paint.py <dir with charts.json, texmap.npz> <old BaseColor.png> <old MetallicSmoothness.png> <out dir>
import os, sys, json, math, numpy as np
from PIL import Image, ImageDraw, ImageFont
from scipy import ndimage
Image.MAX_IMAGE_PIXELS = None
D, OLDB, OLDM, OUT = sys.argv[1:5]
C = json.load(open(D + '/charts.json')); T = np.load(D + '/texmap.npz')
S, PPU, PAD = C['size'], C['ppu'], C['pad']; ids = C['ids']; rect = C['rect']; ext = C['ext']
X, Y, Z = (T['pos'][..., k] for k in range(3)); NX, NY, NZ = (T['nrm'][..., k] for k in range(3)); CH = T['chart']
cov = CH > 0; AX = np.abs(X); px = 1.0 / PPU
def fam(*names): return np.isin(CH, [ids[n] for n in names if n in ids])
FUS, SCOOP = fam('fus'), fam('scoop'); WING = fam('wingL', 'wingR', 'ailL', 'ailR'); TAIL = fam('tailL', 'tailR', 'elevL', 'elevR')
FIN, RUD = fam('fin'), fam('rud')
# chart-local coordinates (u along the part, v across it)
py_, px_ = np.mgrid[0:S, 0:S]
Uc = np.zeros((S, S), np.float32); Vc = np.zeros((S, S), np.float32)
for n, (x, y, w, h) in rect.items():
    if n.startswith('sw_'): continue
    m = CH == ids[n]
    Uc[m] = (px_[m] - x - PAD) / PPU + ext[n][0]; Vc[m] = (py_[m] - y - PAD) / PPU + ext[n][1]

METAL = np.array((196, 202, 210.0)); RED = np.array((190, 24, 28.0)); OLIVE = np.array((70, 76, 48.0)); WHITE = np.array((238, 238, 234.0))
BLACK = np.array((30, 30, 32.0)); NAVY = np.array((22, 44, 92.0)); INK = np.array((34, 36, 40.0)); STEEL = np.array((72, 72, 78.0)); YELLOW = np.array((232, 184, 20.0))
BLUE = np.array((26, 62, 150.0)); GREEN = np.array((24, 92, 52.0))
# The paint jobs. LIVERY picks one (default: red); the game gives each P-51 in a fight a different one (PlaneLivery).
LIVERIES = {
    'red':    dict(nose=RED, rudder=RED, tip=RED, code='LH', serial='472216', stripes=True, checks=None),
    'blue':   dict(nose=BLUE, rudder=BLUE, tip=BLUE, code='PE', serial='414151', stripes=True, checks=None),
    'yellow': dict(nose=YELLOW, rudder=YELLOW, tip=BLACK, code='WZ', serial='413779', stripes=False, checks=BLACK),
    'green':  dict(nose=GREEN, rudder=WHITE, tip=GREEN, code='B6', serial='463221', stripes=False, checks=None),
}
LNAME = os.environ.get('LIVERY', 'red'); LV = LIVERIES[LNAME]; NOSE = LV['nose']
img = np.zeros((S, S, 3), np.float32); img[:] = METAL
paint = np.zeros((S, S), np.float32)            # 1 where there is paint over the metal (not metallic, less glossy)
def lay(mask, col, p=1.0):
    """mask: 0..1 coverage."""
    global img, paint
    m = np.clip(mask, 0, 1)[..., None]; img = img * (1 - m) + np.asarray(col, np.float32) * m
    paint = paint * (1 - m[..., 0]) + p * m[..., 0]
def aa(d, w=None): return np.clip(0.5 - d / (w or px), 0, 1)       # coverage of "d < 0" (d in aircraft units)
def band(v, a, b): return np.minimum(aa(a - v), aa(v - b))

# ---------------------------------------------------------------- panels: where the lines run
rootf = lambda s: np.where(s > 0.55, np.clip(1 - (s - 0.55) / 0.75, 0, None) ** 2, 1.0)
w_le = lambda s: -2.55 + 0.05 * s - 0.20 * rootf(s); w_te = lambda s: -0.07 - 0.137 * s + 0.30 * rootf(s)
t_le = lambda s: 2.20 + 0.223 * s; t_te = lambda s: 3.43 - 0.01 * s
f_le = lambda s: 2.50 + 0.56 * (s - 0.2); f_te = lambda s: 4.20 - 0.14 * s
wc = (Y - w_le(AX)) / (w_te(AX) - w_le(AX)); tcf = (Y - t_le(AX)) / (t_te(AX) - t_le(AX)); fc = (Y - f_le(Z)) / (f_te(Z) - f_le(Z))
LINES = np.full((S, S), 9.0, np.float32)        # distance to the nearest panel line, aircraft units
RIV = np.full((S, S), 9.0, np.float32)          # distance to the nearest rivet row's centre line, and along it
RIVT = np.zeros((S, S), np.float32)
PID = np.zeros((S, S), np.int32)                # which panel a texel is in (for the quilt)
def add_lines(mask, coord, stations, along, scale=1.0, rivets=True):
    """Lines where coord == station, inside mask. along: the coordinate running along the line (for rivet spacing)."""
    global LINES, RIV, RIVT, PID
    st = np.asarray(sorted(stations), np.float32)
    idx = np.searchsorted(st, coord); d = np.full(coord.shape, 9.0, np.float32)
    for k in (-1, 0):
        j = np.clip(idx + k, 0, len(st) - 1); d = np.minimum(d, np.abs(coord - st[j]))
    d = d * scale
    LINES = np.where(mask, np.minimum(LINES, d), LINES)
    if rivets:
        r = np.abs(d - 0.028); better = mask & (r < RIV)
        RIVT = np.where(better, along, RIVT); RIV = np.where(mask, np.minimum(RIV, r), RIV)
    PID = np.where(mask, PID * 31 + idx + 7, PID)
side = np.sign(X).astype(np.int32)
FB = FUS | SCOOP
add_lines(FUS, Y, [-3.30, -2.78, -2.25, -1.78, -0.45, 0.86, 1.52, 2.60, 3.20, 3.80], Vc)
add_lines(FUS, np.abs(Vc), [0.34, 1.05, 1.85], Y)
add_lines(SCOOP, Y, [-0.30, 0.55, 1.30], Vc); add_lines(SCOOP, np.abs(Vc), [0.45], Y)
add_lines(WING, AX, [0.72, 1.25, 1.85, 2.55, 3.30, 3.95, 4.65, 5.30], Y)
add_lines(WING, wc, [0.10, 0.38, 0.70], AX, scale=2.0)
add_lines(TAIL, AX, [0.35, 0.90, 1.45, 2.00], Y); add_lines(TAIL, tcf, [0.18, 0.62], AX, scale=1.0)
add_lines(FIN | RUD, Z, [0.45, 0.80, 1.12, 1.38], Y); add_lines(FIN | RUD, fc, [0.20, 0.62], Z, scale=1.2)
PID = PID * 3 + side + NZ.round().astype(np.int32) * 5

# the quilt: each panel a slightly different polish
h = (np.abs(PID.astype(np.int64) * 2654435761) % 1000) / 1000.0
tone = 1.0 + (h - 0.5) * 0.10
img *= tone[..., None]
img[..., 2] *= 1 + (h[..., None][..., 0] - 0.5) * 0.02

# ---------------------------------------------------------------- paint
lay(FB * aa(Y + 3.30), NOSE)                                                        # nose
if LV['checks'] is not None:                                                        # a chequered cowl
    q = 0.223; chk = ((np.floor((Y + 3.97) / q) + np.floor(Vc / q)) % 2 == 0)
    lay(FB * aa(Y + 3.30) * chk, LV['checks'])
lay(FUS * band(Y, -3.30, -3.24), WHITE); lay(FUS * band(Y, -3.24, -3.215), BLACK)     # its trim
lay(FUS * band(Y, -3.215, -1.76) * aa(np.abs(Vc) - (0.36 + 0.02 * (Y + 3.2))), OLIVE, 0.6)     # anti-glare panel
lay((RUD | (FIN & (fc > 0.62))) * 1.0, LV['rudder'])                                 # rudder
lay(FIN * aa(1.16 - Z), LV['tip'])                                                   # fin tip
# invasion stripes
def stripes(mask, v, a, wdt):
    k = np.floor((v - a) / wdt); inside = np.minimum(aa(a - v), aa(v - (a + 5 * wdt)))
    edge = np.abs(((v - a) / wdt) - np.round((v - a) / wdt)) * wdt
    col = np.where((k % 2 == 1)[..., None], BLACK, WHITE)
    lay(mask * inside, WHITE)
    lay(mask * inside * (k % 2 == 1) * np.clip(edge / px, 0, 1), BLACK)
if LV['stripes']:
    stripes(FB, Y, 1.42, 0.23)
    stripes(WING * (wc < 1.2), AX, 0.95, 0.44)
    stripes(TAIL, AX, 0.55, 0.20)

# decals drawn flat, then laid on by projection
def decal(draw_fn, w_units, h_units, res=700):
    W, H = int(w_units * res), int(h_units * res); im = Image.new('RGBA', (W, H), (0, 0, 0, 0)); draw_fn(ImageDraw.Draw(im), W, H, res)
    return np.asarray(im).astype(np.float32)
def put(mask, a_, b_, dec, ca, cb, w_units, h_units, flip=False):
    """a_ runs left to right across the decal, b_ runs up; (ca, cb) its centre."""
    H, W = dec.shape[:2]; u = (a_ - ca) / w_units + 0.5; v = 0.5 - (b_ - cb) / h_units
    if flip: u = 1 - u
    ok = mask & (u > 0) & (u < 1) & (v > 0) & (v < 1)
    xi = np.clip(u[ok] * W - 0.5, 0, W - 1.001); yi = np.clip(v[ok] * H - 0.5, 0, H - 1.001)
    x0 = xi.astype(int); y0 = yi.astype(int); fx = (xi - x0)[:, None]; fy = (yi - y0)[:, None]
    smp = dec[y0, x0] * (1 - fx) * (1 - fy) + dec[y0, x0 + 1] * fx * (1 - fy) + dec[y0 + 1, x0] * (1 - fx) * fy + dec[y0 + 1, x0 + 1] * fx * fy
    al = smp[:, 3:4] / 255.0
    img[ok] = img[ok] * (1 - al) + smp[:, :3] * al; paint[ok] = paint[ok] * (1 - al[:, 0]) + al[:, 0]
def star_bar(R):
    def fn(d, W, H, res):
        cx, cy, r = W / 2, H / 2, R * res; b = r * 0.125
        d.rectangle((cx - 2 * r - b, cy - r * 0.25 - b, cx + 2 * r + b, cy + r * 0.25 + b), fill=tuple(NAVY.astype(int)) + (255,))
        d.rectangle((cx - 2 * r, cy - r * 0.25, cx + 2 * r, cy + r * 0.25), fill=tuple(WHITE.astype(int)) + (255,))
        d.ellipse((cx - r - b, cy - r - b, cx + r + b, cy + r + b), fill=tuple(NAVY.astype(int)) + (255,))
        pts = []
        for k in range(10):
            rr = r if k % 2 == 0 else r * 0.382; an = -math.pi / 2 + k * math.pi / 5; pts.append((cx + rr * math.cos(an), cy + rr * math.sin(an)))
        d.polygon(pts, fill=tuple(WHITE.astype(int)) + (255,))
    return decal(fn, 4.4 * R, 2.4 * R), 4.4 * R, 2.4 * R
FONT = '/usr/share/fonts/truetype/dejavu/DejaVuSans-Bold.ttf'
def text(s, h_units, col, outline=None):
    f = ImageFont.truetype(FONT, 400); bb = f.getbbox(s); asp = (bb[2] - bb[0] + 40) / (bb[3] - bb[1] + 40)
    def fn(d, W, H, res):
        ff = ImageFont.truetype(FONT, int(H * 0.86 * 400 / (bb[3] - bb[1] + 40) * 1.0)); b2 = ff.getbbox(s)
        d.text(((W - (b2[2] - b2[0])) / 2 - b2[0], (H - (b2[3] - b2[1])) / 2 - b2[1]), s, font=ff, fill=tuple(int(c) for c in col) + (255,),
               stroke_width=int(H * 0.035) if outline is not None else 0, stroke_fill=None if outline is None else tuple(int(c) for c in outline) + (255,))
    return decal(fn, h_units * asp, h_units), h_units * asp, h_units
SIDE = FUS & (np.abs(NX) > 0.35)
sb, sw_, sh_ = star_bar(0.27)
for sx in (1, -1):
    m = SIDE & (X * sx > 0)
    put(m, Y * sx, Z, sb, 0.62 * sx, -0.20, sw_, sh_)
    dl, lw, lh = text(LV['code'], 0.50, BLACK)
    put(m, Y * sx, Z, dl, -0.62 * sx if sx > 0 else -0.62 * sx, -0.22, lw, lh)
    dm, mw, mh = text('MUSTANG', 0.17, NOSE if LNAME != 'yellow' else BLACK, WHITE)
    put(m, Y * sx, Z, dm, -2.72 * sx, -0.02, mw, mh)
    ds, dw, dh = text(LV['serial'], 0.17, BLACK if LNAME != 'green' else BLACK)
    put((FIN | RUD) & (X * sx > 0), Y * sx, Z, ds, 3.10 * sx, 0.62, dw, dh)
sbw, sww, shw = star_bar(0.46)
ymid = lambda s: float((w_le(np.float32(s)) + w_te(np.float32(s))) / 2 - 0.12)
# bars out along the span, one point of the star forward
put(WING & (X > 0) & (NZ > 0.3), -X, -Y, sbw, -4.25, -ymid(4.25), sww, shw)             # upper left wing
put(WING & (X < 0) & (NZ < -0.3), -X, -Y, sbw, 4.25, -ymid(4.25), sww, shw)              # lower right wing

# ---------------------------------------------------------------- lines, rivets, small marks
ms = np.zeros((S, S, 4), np.float32)
line = aa(LINES - 0.0065, px * 1.2) * cov
rspace = 0.062
riv = aa(np.hypot(RIV, (RIVT % rspace) - rspace / 2) - 0.0075, px) * cov * (LINES > 0.02)
dark = INK * 0.9 + img * 0.1
m = line[..., None] * 0.92; img = img * (1 - m) + INK * m
m = riv[..., None] * 0.42; img = img * (1 - m) + INK * m
# fuel caps and gun bay doors on the wing's top, an access door on the cowl
def ring(mask, a_, b_, ca, cb, r, wdt=0.006):
    global img
    d = np.abs(np.hypot(a_ - ca, b_ - cb) - r); m = (aa(d - wdt, px * 1.2) * mask)[..., None] * 0.9; img = img * (1 - m) + INK * m
for sx in (1, -1):
    ring(WING & (NZ > 0.3), X, Y, sx * 1.55, -1.25, 0.075); ring(WING & (NZ > 0.3), X, Y, sx * 1.55, -1.25, 0.03, 0.03)
    ring(FUS & (X * sx > 0), Y, Z, -2.5, 0.52, 0.06)
    # the retracted undercarriage: wheel well and leg door outlines under the wing
    UNDER = WING & (NZ < -0.3)
    ring(UNDER, X, Y, sx * 0.95, -1.62, 0.33)
    dx = np.abs(X * sx - 1.85) - 0.62; dy = np.abs(Y + 1.62) - 0.15
    dbox = np.abs(np.maximum(dx, dy)); mm = (aa(dbox - 0.006, px * 1.2) * UNDER * (X * sx > 1.26))[..., None] * 0.9
    img = img * (1 - mm) + INK * mm

# ---------------------------------------------------------------- swatches, the propeller's palette, edges
for n, col in (('sw_red', LV['tip']), ('sw_metal', METAL), ('sw_olive', OLIVE), ('sw_ink', INK), ('sw_steel', STEEL), ('sw_glass', (200, 220, 230))):
    x, y, w, h_ = rect[n]; img[y:y + h_, x:x + w] = col; paint[y:y + h_, x:x + w] = 0 if n in ('sw_metal', 'sw_steel') else 1
    cov[y:y + h_, x:x + w] = True
# spread the charts' edges outward so no seam shows
miss = ~cov
_, (iy, ix) = ndimage.distance_transform_edt(miss, return_indices=True)
img = img[iy, ix]; paint = paint[iy, ix]; line = line[iy, ix]
metal = 1 - np.clip(paint, 0, 1)
smooth = 0.86 * metal + 0.52 * (1 - metal); smooth = smooth * (1 - 0.5 * line)
out = np.clip(img, 0, 255).astype(np.uint8)
msm = np.zeros((S, S, 4), np.uint8); msm[..., 0] = msm[..., 1] = msm[..., 2] = (metal * 247).astype(np.uint8); msm[..., 3] = (smooth * 255).astype(np.uint8)
kx, ky, kw, kh = C['keep']
ob = np.asarray(Image.open(OLDB).convert('RGB').resize((S, S))); om = np.asarray(Image.open(OLDM).convert('RGBA').resize((S, S)))
out[ky:ky + kh, kx:kx + kw] = ob[ky:ky + kh, kx:kx + kw]; msm[ky:ky + kh, kx:kx + kw] = om[ky:ky + kh, kx:kx + kw]
if LNAME != 'red':        # the spinner takes the nose colour: the red of the propeller's palette, repainted
    sq = out[ky:ky + kh, kx:kx + kw].astype(np.float32); r_, g_, b_ = sq[..., 0], sq[..., 1], sq[..., 2]
    isred = (r_ > 110) & (g_ < 0.5 * r_) & (b_ < 0.5 * r_)
    sq[isred] = NOSE[None, :] * (r_[isred] / max(1.0, float(np.median(r_[isred]))))[:, None]
    out[ky:ky + kh, kx:kx + kw] = np.clip(sq, 0, 255).astype(np.uint8)
TAG = '' if LNAME == 'red' else '_' + LNAME.capitalize()
Image.fromarray(out).save(OUT + '/P51%s_BaseColor.png' % TAG); Image.fromarray(msm).resize((S // 2, S // 2), Image.LANCZOS).save(OUT + '/P51%s_MetallicSmoothness.png' % TAG)
Image.fromarray(out).resize((1024, 1024), Image.LANCZOS).save(OUT + '/atlas_preview.jpg', quality=88)
print('painted')
