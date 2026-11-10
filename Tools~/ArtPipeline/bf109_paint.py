# Paint the clean Bf 109 G: grey-green splinter (RLM 74/75) on top, light blue (RLM 76) sides and undersides with a
# soft mottle down the fuselage, yellow under the cowl and round the rear fuselage, black-green spinner with a white
# spiral, black-and-white crosses, double chevrons, panel lines and rivets, exhaust staining. Every texel knows where it
# sits on the aircraft (texmap.npz from bf109_hull.py), so every marking is drawn as an exact shape in aircraft space and
# lies the right way round on each side: the chevrons point at the nose on both sides, and the lettering reads from
# left to right on both sides. Built the same way as p51_paint.py.
# Run: python bf109_paint.py <dir with charts.json, texmap.npz> <out dir>
import sys, json, math, numpy as np
from PIL import Image, ImageDraw, ImageFont
from scipy import ndimage
Image.MAX_IMAGE_PIXELS = None
D, OUT = sys.argv[1:3]
C = json.load(open(D + '/charts.json')); T = np.load(D + '/texmap.npz')
S, PPU, PAD = C['size'], C['ppu'], C['pad']; ids = C['ids']; rect = C['rect']; ext = C['ext']
X, Y, Z = (T['pos'][..., k] for k in range(3)); NX, NY, NZ = (T['nrm'][..., k] for k in range(3)); CH = T['chart']
cov = CH > 0; AX = np.abs(X); px = 1.0 / PPU
def fam(*names): return np.isin(CH, [ids[n] for n in names if n in ids])
FUS = fam('fus'); CHIN = fam('chin'); RAD = fam('radL', 'radR'); SPIN = fam('spin')
WING = fam('wingL', 'wingR', 'ailL', 'ailR'); TAIL = fam('tailL', 'tailR', 'elevL', 'elevR'); FIN, RUD = fam('fin'), fam('rud')
py_, px_ = np.mgrid[0:S, 0:S]
Uc = np.zeros((S, S), np.float32); Vc = np.zeros((S, S), np.float32)
for n, (x, y, w, h) in rect.items():
    if n.startswith('sw_'): continue
    m = CH == ids[n]
    Uc[m] = (px_[m] - x - PAD) / PPU + ext[n][0]; Vc[m] = (py_[m] - y - PAD) / PPU + ext[n][1]

RLM74 = np.array((72, 80, 74.0)); RLM75 = np.array((100, 100, 110.0)); RLM76 = np.array((176, 196, 206.0))
YELLOW = np.array((236, 186, 28.0)); BLACK = np.array((26, 26, 28.0)); WHITE = np.array((240, 240, 236.0))
RLM70 = np.array((38, 46, 36.0)); INK = np.array((30, 32, 34.0)); STEEL = np.array((70, 66, 64.0))
img = np.zeros((S, S, 3), np.float32); img[:] = RLM76
def lay(mask, col):
    global img
    m = np.clip(mask, 0, 1)[..., None]; img = img * (1 - m) + np.asarray(col, np.float32) * m
def aa(d, w=None): return np.clip(0.5 - d / (w or px), 0, 1)       # coverage of "d < 0" (d in aircraft units)
def band(v, a, b): return np.minimum(aa(a - v), aa(v - b))
def sstep(x, a, b): t = np.clip((x - a) / (b - a), 0, 1); return t * t * (3 - 2 * t)

# ---------------------------------------------------------------- the planform (as bf109_hull.py)
def rootf(s): return np.where(s > 0.50, np.clip(1 - (s - 0.50) / 0.55, 0, None) ** 2, 1.0)
w_le = lambda s: -1.86 + 0.035 * s - 0.10 * rootf(s) + 0.08 * np.clip((s - 3.4) / 1.2, 0, None) ** 2
w_te = lambda s: 0.12 - 0.135 * s + 0.16 * rootf(s)
t_le = lambda s: 2.55 + 0.06 * s; t_te = lambda s: 3.42 - 0.02 * s
f_le = lambda s: 2.42 + 0.85 * (s - 0.30); f_te = lambda s: 3.84 - 0.06 * (s - 0.30)
wc = (Y - w_le(AX)) / (w_te(AX) - w_le(AX)); tcf = (Y - t_le(AX)) / (t_te(AX) - t_le(AX)); fc = (Y - f_le(Z)) / (f_te(Z) - f_le(Z))
TZ = 0.45

# ---------------------------------------------------------------- camouflage
UP = NZ > 0.15
# splinter on the upper surfaces: straight-edged patches of 74 and 75, mirrored about the centreline as the factory
# pattern was not, which reads better at game distance
def splinter(a_, b_, lines):
    k = np.zeros(a_.shape, np.int32)
    for (p, q, c) in lines: k += (p * a_ + q * b_ + c > 0).astype(np.int32)
    return k % 2
SPL_W = splinter(AX, Y, [(1, 0.85, -0.2), (1, 0.85, -1.5), (1, 0.85, -2.8), (1, 0.85, -4.0), (1, -0.9, -3.3)])
SPL_T = splinter(AX, Y, [(1, 0.6, -2.2), (1, -0.5, 0.1)])
img[:] = RLM76
w_mid = lambda s: -0.36 + 0.105 * s
top_w = (WING & ((NZ > 0) | (Z > w_mid(AX)))) | (TAIL & ((NZ > 0) | (Z > 0.12)))        # above the mean line, so the root fillets stay grey
lay(top_w * (1 - SPL_W) * WING, RLM74); lay(top_w * SPL_W * WING, RLM75)
lay(top_w * (1 - SPL_T) * TAIL, RLM74); lay(top_w * SPL_T * TAIL, RLM75)
# the fuselage: the spine in 74/75 down to a soft line, the sides 76 with mottle that thins out downward
def hullz_top(y): return np.interp(y, [-3.36, -2.8, -1.4, -0.2, 0.4, 1.0, 1.6, 2.2, 2.8, 3.35, 3.9], [0.82, 0.875, 0.82, 0.755, 0.68, 0.58, 0.47, 0.37, 0.28, 0.205, 0.09])
spine = sstep(Z - (hullz_top(Y) - 0.16), -0.02, 0.02) * (FUS | FIN | RUD)
spine_spl = splinter(X * 0 + Y, X, [(1, 0, 1.2), (1, 0, -0.9), (1, 0, -2.6), (0.5, 1, 0.0)])
lay(spine * spine_spl, RLM74); lay(spine * (1 - spine_spl), RLM75)
# mottle: a smooth random field in side view, thresholded softly
rng = np.random.default_rng(109)
NOI = ndimage.gaussian_filter(rng.random((210, 75)), 1.5)          # cells of 4 cm over y -4.2..4.2, z -0.9..2.1
NOI = (NOI - NOI.mean()) / NOI.std()
gy = np.clip((Y + 4.2) / 0.04, 0, 208.999); gz = np.clip((Z + 0.9) / 0.04, 0, 73.999)
iy0 = gy.astype(int); iz0 = gz.astype(int); fy = gy - iy0; fz = gz - iz0
noi = (NOI[iy0, iz0] * (1 - fy) * (1 - fz) + NOI[iy0 + 1, iz0] * fy * (1 - fz) + NOI[iy0, iz0 + 1] * (1 - fy) * fz + NOI[iy0 + 1, iz0 + 1] * fy * fz)
noi = noi + 0.35 * np.sin(X * 13.0)                                  # the two sides differ
dens = sstep(Z, -0.25, hullz_top(Y) - 0.15)                          # more mottle higher up the side
mott = sstep(noi + 1.6 * dens - 1.55, -0.12, 0.12) * (FUS | FIN | RUD) * (np.abs(NX) > 0.2) * (1 - spine)
lay(mott * 0.9 * (noi > 0.7), RLM75); lay(mott * 0.9 * (noi <= 0.7), np.array((110, 118, 108.0)))

# ---------------------------------------------------------------- the bright parts
COWL = FUS & (Y < -1.95)
lay(COWL * aa(Y + 2.98), YELLOW)                                     # the nose ring
lay((COWL * aa(Z - 0.20) + CHIN * 1.0) * (1 - aa(Y + 2.98)), YELLOW)  # under the cowl, and the oil cooler
lay(FUS * band(Y, 1.92, 2.34), YELLOW)                               # the band round the rear fuselage
lay(FUS * band(Y, -3.36, -3.30), RLM70)                              # a dark lip where the spinner meets the cowl
# the spinner: black-green with one white spiral from the point back to the base
phi = np.arctan2(X, Z - TZ) / (2 * np.pi); t = np.clip((Y - (-3.36 - 0.56)) / 0.56, 0, 1)
spiral = (np.mod(phi + 1.15 * t, 1.0) < 0.16 + 0.04 * t)
lay(SPIN * 1.0, RLM70); lay(SPIN * spiral * aa(-t + 0.03), WHITE)
# exhaust staining: soot streaks back along the side from the stacks
soot = FUS * sstep(-Y, 1.0, 2.3) * np.exp(-((Z - 0.27) / (0.07 + 0.05 * np.clip(Y + 2.9, 0, 2))) ** 2) * (Y > -2.95) * (np.abs(NX) > 0.3)
lay(soot * 0.55, np.array((44, 42, 40.0)))

# ---------------------------------------------------------------- panel lines and rivets
LINES = np.full((S, S), 9.0, np.float32); RIV = np.full((S, S), 9.0, np.float32); RIVT = np.zeros((S, S), np.float32)
PID = np.zeros((S, S), np.int64)
def add_lines(mask, coord, stations, along, scale=1.0, rivets=True):
    global LINES, RIV, RIVT, PID
    st = np.asarray(sorted(stations), np.float32)
    idx = np.searchsorted(st, coord); d = np.full(coord.shape, 9.0, np.float32)
    for k in (-1, 0):
        j = np.clip(idx + k, 0, len(st) - 1); d = np.minimum(d, np.abs(coord - st[j]))
    d = d * scale
    LINES = np.where(mask, np.minimum(LINES, d), LINES)
    if rivets:
        r = np.abs(d - 0.026); better = mask & (r < RIV)
        RIVT = np.where(better, along, RIVT); RIV = np.where(mask, np.minimum(RIV, r), RIV)
    PID = np.where(mask, PID * 31 + idx + 7, PID)
add_lines(FUS, Y, [-3.22, -2.92, -2.40, -1.95, -1.13, -0.55, 0.31, 0.80, 1.30, 1.80, 2.40, 2.95, 3.45], Vc)
add_lines(FUS & (Y < -1.95), Z, [0.62, 0.22], Y, rivets=False)                 # the cowl's side panels
# seams along the fuselage at fixed angles round its axis, so they stay parallel all the way to the tail
zc_ = np.interp(Y, [-3.36, -2.8, -1.9, -0.8, 0.4, 1.6, 2.8, 3.9], [0.45, 0.39, 0.29, 0.18, 0.15, 0.13, 0.12, 0.11])
ANG = np.degrees(np.arctan2(np.abs(X), Z - zc_))
add_lines(FUS & (Y > -1.95), ANG * 0.0105, [0.55 * 1.05, 1.25 * 1.05], Y)
add_lines(CHIN | RAD, Y, [-2.9, -2.5, -0.85, -0.45], Vc, rivets=False)
add_lines(WING, AX, [0.70, 1.20, 1.85, 2.55, 3.30, 4.05], Y)
add_lines(WING, wc, [0.30, 0.62], AX, scale=2.0)
add_lines(WING & (AX > 2.55) & (AX < 4.30), wc, [0.13], AX, scale=2.0, rivets=False)     # the leading edge slats
add_lines(WING & (AX < 2.95) & (AX > 0.62), wc, [0.74], AX, scale=2.0, rivets=False)     # the flaps
add_lines(TAIL, AX, [0.30, 0.75, 1.20], Y); add_lines(TAIL, tcf, [0.25], AX)
add_lines(FIN | RUD, Z, [0.45, 0.80], Y); add_lines(FIN | RUD, fc, [0.30], Z, scale=1.2)
side = np.sign(X).astype(np.int64)
PID = PID * 3 + side + np.round(NZ).astype(np.int64) * 5
h = (np.abs(PID * 2654435761) % 1000) / 1000.0
img *= (1.0 + (h - 0.5) * 0.06)[..., None]

# ---------------------------------------------------------------- markings: decals drawn flat, laid on by projection
def decal(draw_fn, w_units, h_units, res=700):
    W, H = int(w_units * res), int(h_units * res); im = Image.new('RGBA', (W, H), (0, 0, 0, 0)); draw_fn(ImageDraw.Draw(im), W, H, res)
    return np.asarray(im).astype(np.float32)
def put(mask, a_, b_, dec, ca, cb, w_units, h_units, flip=False):
    """a_ runs left to right across the decal as a viewer sees it, b_ runs up; (ca, cb) its centre."""
    H, W = dec.shape[:2]; u = (a_ - ca) / w_units + 0.5; v = 0.5 - (b_ - cb) / h_units
    if flip: u = 1 - u
    ok = mask & (u > 0) & (u < 1) & (v > 0) & (v < 1)
    xi = np.clip(u[ok] * W - 0.5, 0, W - 1.001); yi = np.clip(v[ok] * H - 0.5, 0, H - 1.001)
    x0 = xi.astype(int); y0 = yi.astype(int); fx = (xi - x0)[:, None]; fy = (yi - y0)[:, None]
    smp = dec[y0, x0] * (1 - fx) * (1 - fy) + dec[y0, x0 + 1] * fx * (1 - fy) + dec[y0 + 1, x0] * (1 - fx) * fy + dec[y0 + 1, x0 + 1] * fx * fy
    al = smp[:, 3:4] / 255.0
    img[ok] = img[ok] * (1 - al) + smp[:, :3] * al
def rgba(c): return tuple(int(v) for v in c) + (255,)
def cross(R, outline_only=False):
    """The Balkenkreuz: a black cross with arms R long from the centre, on a white edge, edged in black."""
    def fn(d, W, H, res):
        cx, cy = W / 2, H / 2; r = R * res; a = r * 0.25; b = r * 0.12; o = r * 0.035
        def plus(arm, half, col):
            d.rectangle((cx - arm, cy - half, cx + arm, cy + half), fill=col); d.rectangle((cx - half, cy - arm, cx + half, cy + arm), fill=col)
        plus(r + o, a + b + o, rgba(BLACK)); plus(r, a + b, rgba(WHITE))
        # the white edge only on the outer corners: the black cross sits inside
        plus(r, a, rgba(BLACK) if not outline_only else (0, 0, 0, 0))
    return decal(fn, 2.1 * R, 2.1 * R), 2.1 * R, 2.1 * R
def chevrons(hgt):
    """Two chevrons, pointing left (at the nose when laid the right way round)."""
    def fn(d, W, H, res):
        th = H * 0.17; o = H * 0.04
        for k in range(2):
            x0 = H * 0.06 + k * H * 0.40
            for col, grow in ((rgba(WHITE), o), (rgba(BLACK), 0)):
                tip = (x0 - grow, H / 2); top = (x0 + H * 0.42 + grow, H * 0.06 - grow); bot = (x0 + H * 0.42 + grow, H * 0.94 + grow)
                d.line([top, tip, bot], fill=col, width=int(th + 2 * grow), joint='curve')
    return decal(fn, hgt * 1.35, hgt), hgt * 1.35, hgt
FONT = '/usr/share/fonts/truetype/dejavu/DejaVuSans-Bold.ttf'
def text(s, h_units, col, outline=None):
    f = ImageFont.truetype(FONT, 400); bb = f.getbbox(s); asp = (bb[2] - bb[0] + 40) / (bb[3] - bb[1] + 40)
    def fn(d, W, H, res):
        ff = ImageFont.truetype(FONT, int(H * 0.86 * 400 / (bb[3] - bb[1] + 40))); b2 = ff.getbbox(s)
        d.text(((W - (b2[2] - b2[0])) / 2 - b2[0], (H - (b2[3] - b2[1])) / 2 - b2[1]), s, font=ff, fill=rgba(col),
               stroke_width=int(H * 0.04) if outline is not None else 0, stroke_fill=None if outline is None else rgba(outline))
    return decal(fn, h_units * asp, h_units), h_units * asp, h_units
SIDE = FUS & (np.abs(NX) > 0.30)
cr, cw, chh = cross(0.30); ch_, chw, chh2 = chevrons(0.40); wn, wnw, wnh = text('15458', 0.075, BLACK)
for sx in (1, -1):
    # seen from this side, "left to right" runs toward the tail on the left (+X) side and toward the nose on the right
    a_ = Y * sx; m = SIDE & (X * sx > 0)
    put(m, a_, Z, cr, 1.25 * sx, 0.02, cw, chh)
    put(m, a_, Z, ch_, 0.48 * sx, 0.05, chw, chh2, flip=sx < 0)       # flipped on the right so they point at the nose
    put((FIN | RUD) & (X * sx > 0), a_, Z, wn, 3.38 * sx, 0.70, wnw, wnh)
cwg, cww, chw_ = cross(0.42)
ymid = lambda s: float((w_le(np.float32(s)) + w_te(np.float32(s))) / 2)
for sx in (1, -1):
    put(WING & (X * sx > 0) & (NZ > 0.3), Y, -X, cwg, ymid(3.3), -3.3 * sx, cww, chw_)       # upper wings
    put(WING & (X * sx > 0) & (NZ < -0.3), Y, -X, cwg, ymid(3.3), -3.3 * sx, cww, chw_)      # lower wings

# ---------------------------------------------------------------- lines and rivets on top of the paint
line = aa(LINES - 0.0055, px * 1.2) * cov
rspace = 0.058
riv = aa(np.hypot(RIV, (RIVT % rspace) - rspace / 2) - 0.0065, px) * cov * (LINES > 0.02) * (1 - SPIN)
m = line[..., None] * 0.75; img = img * (1 - m) + INK * m
m = riv[..., None] * 0.28; img = img * (1 - m) + INK * m
def ring(mask, a_, b_, ca, cb, r, wdt=0.005):
    global img
    d = np.abs(np.hypot(a_ - ca, b_ - cb) - r); m = (aa(d - wdt, px * 1.2) * mask)[..., None] * 0.8; img = img * (1 - m) + INK * m
for sx in (1, -1):
    ring(FUS & (X * sx > 0), Y, Z, 0.95, 0.25, 0.07)                     # the first-aid hatch / fuel filler
    UNDER = WING & (NZ < -0.3)
    ring(UNDER & (AX < 1.2), X, Y, sx * 0.80, -1.42, 0.26)               # the retracted wheel's well, ahead of the radiator
ring(FUS & (NZ > 0.5), X, Y, 0.0, 0.90, 0.06)                            # the fuel filler behind the cockpit

# ---------------------------------------------------------------- swatches, edges, maps
for n, col in (('sw_paint', RLM74), ('sw_under', RLM76), ('sw_ink', INK), ('sw_frame', RLM74), ('sw_steel', STEEL),
               ('sw_glass', (200, 220, 230)), ('sw_glassframe', RLM74), ('sw_prop', RLM70)):
    x, y, w, h_ = rect[n]; img[y:y + h_, x:x + w] = col; cov[y:y + h_, x:x + w] = True
miss = ~cov
_, (iy, ix) = ndimage.distance_transform_edt(miss, return_indices=True)
img = img[iy, ix]; line = line[iy, ix]
steel = np.zeros((S, S), np.float32); x, y, w, h_ = rect['sw_steel']; steel[y:y + h_, x:x + w] = 1
smooth = 0.42 * (1 - steel) + 0.55 * steel; smooth = smooth * (1 - 0.5 * line)
gloss = SPIN[iy, ix]; smooth = np.where(gloss, 0.55, smooth)
out = np.clip(img, 0, 255).astype(np.uint8)
msm = np.zeros((S, S, 4), np.uint8); msm[..., 0] = msm[..., 1] = msm[..., 2] = (steel * 200).astype(np.uint8); msm[..., 3] = (smooth * 255).astype(np.uint8)
Image.fromarray(out).save(OUT + '/Bf109_BaseColor.png'); Image.fromarray(msm).resize((S // 2, S // 2), Image.LANCZOS).save(OUT + '/Bf109_MetallicSmoothness.png')
Image.fromarray(out).resize((1024, 1024), Image.LANCZOS).save(OUT + '/atlas_preview.jpg', quality=88)
print('painted')
